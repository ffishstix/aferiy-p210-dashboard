import test from 'node:test';
import assert from 'node:assert/strict';
import { statusOf } from '../functions/api/_lib.js';
import { onRequestPost as ingest } from '../functions/api/ingest.js';
import { onRequestGet as history } from '../functions/api/history.js';
import { onRequestGet as status } from '../functions/api/status.js';
import { authorised, makeSession } from '../functions/api/control/_auth.js';
import { controlState } from '../functions/api/control/_state.js';
import { onRequestGet as controlStatus } from '../functions/api/control/state.js';
import { onRequestPost as controlCommand } from '../functions/api/control/command.js';

test('status is public and selects only displayed measurements', async () => {
  let sql;
  const sample = { recorded_at: new Date().toISOString(), received_at: new Date().toISOString(), charge_percent: 50, input_w: 0, output_w: 20, grid_present: 0, discharging: 1 };
  const env = { DB: { prepare(query) { sql = query; return { first: async () => sample }; } } };
  const response = await status({ request: new Request('https://example.com/api/status'), env });
  assert.equal(response.status, 200);
  assert.deepEqual((await response.json()).sample, sample);
  assert.match(sql, /SELECT recorded_at, received_at/);
  assert.doesNotMatch(sql, /\bdevice_id\b|\braw_json\b|SELECT \*/);
});

test('fresh status collects before reading the newly saved sample, every time', async () => {
  let collections = 0;
  let saved = null;
  const env = {
    INGEST_SECRET: 'private-key',
    CONTROL_WORKER: { async fetch(request) {
      assert.equal(request.method, 'POST');
      assert.equal(new URL(request.url).pathname, '/check');
      assert.equal(request.headers.get('authorization'), 'Bearer private-key');
      saved = { received_at: new Date().toISOString(), charge_percent: ++collections };
      return Response.json({ ok: true });
    } },
    DB: { prepare() { return { first: async () => saved }; } },
  };
  const request = new Request('https://example.com/api/status?fresh=1');
  assert.equal((await (await status({ request, env })).json()).sample.charge_percent, 1);
  assert.equal((await (await status({ request, env })).json()).sample.charge_percent, 2);
  assert.equal(collections, 2);
});

test('failed fresh collection does not present an older sample as current', async () => {
  let queried = false;
  const env = {
    INGEST_SECRET: 'private-key',
    CONTROL_WORKER: { fetch: async () => new Response('failure', { status: 502 }) },
    DB: { prepare() { queried = true; throw new Error('Should not read stale data'); } },
  };
  const response = await status({ request: new Request('https://example.com/api/status?fresh=1'), env });
  assert.equal(response.status, 502);
  assert.equal(queried, false);
});

test('controls require a signed session and expose no raw registers', async () => {
  const env = { SESSION_SECRET: 'test-secret', DB: { prepare() { return { first: async () => ({ received_at: new Date().toISOString(), raw_json: JSON.stringify({ holdingRegisters: new Array(80).fill(0), inputRegisters: new Array(80).fill(0) }) }) }; } } };
  const anonymous = await controlStatus({ request: new Request('https://example.com/api/control/state'), env });
  assert.equal(anonymous.status, 401);
  const token = await makeSession(env.SESSION_SECRET);
  const request = new Request('https://example.com/api/control/state', { headers: { cookie: `battery_control_session=${token}` } });
  assert.equal(await authorised(request, env), true);
  const response = await controlStatus({ request, env });
  assert.equal(response.status, 200);
  assert.equal(JSON.stringify(await response.json()).includes('holdingRegisters'), false);
});

test('control state decodes output bits and documented limits', () => {
  const holding = new Array(86).fill(0), input = new Array(80).fill(0);
  holding[13] = 3; holding[26] = 1; holding[27] = 2; holding[66] = 150; holding[67] = 900;
  const result = controlState({ received_at: new Date().toISOString(), raw_json: JSON.stringify({ holdingRegisters: holding, inputRegisters: input }) });
  assert.equal(result.available, true);
  assert.equal(result.settings.acOutput, true);
  assert.equal(result.settings.dcOutput, false);
  assert.equal(result.settings.ledMode, 2);
  assert.equal(result.settings.dischargeLimit, 15);
  assert.equal(result.settings.chargeLimit, 90);
});

test('battery commands require sign-in, same origin, and fresh settings', async () => {
  const holding = new Array(86).fill(0), input = new Array(80).fill(0);
  holding[67] = 1000;
  let calls = 0;
  const sample = { device_id: 'device', received_at: new Date().toISOString(), raw_json: JSON.stringify({ holdingRegisters: holding, inputRegisters: input }) };
  const env = { SESSION_SECRET: 'test-secret', INGEST_SECRET: 'private-key', DB: { prepare() { return { first: async () => sample }; } }, CONTROL_WORKER: { async fetch(request) { calls++; assert.equal(request.headers.get('authorization'), 'Bearer private-key'); return Response.json({ confirmed: true }); } } };
  const url = 'https://example.com/api/control/command';
  const post = headers => new Request(url, { method: 'POST', headers: { 'content-type': 'application/json', ...headers }, body: JSON.stringify({ setting: 'acOutput', value: false }) });
  assert.equal((await controlCommand({ request: post({ origin: 'https://wrong.example' }), env })).status, 403);
  assert.equal((await controlCommand({ request: post({ origin: 'https://example.com' }), env })).status, 401);
  assert.equal(calls, 0);
  const token = await makeSession(env.SESSION_SECRET);
  const headers = { origin: 'https://example.com', cookie: `battery_control_session=${token}` };
  assert.equal((await controlCommand({ request: post(headers), env })).status, 200);
  assert.equal(calls, 1);
  sample.received_at = new Date(Date.now() - 180000).toISOString();
  assert.equal((await controlCommand({ request: post(headers), env })).status, 409);
  assert.equal(calls, 1);
});

test('old readings are stale and outage readings are discharging', () => {
  assert.equal(statusOf({ received_at: new Date(Date.now() - 180000).toISOString(), discharging: 1, grid_present: 0 }), 'stale');
  assert.equal(statusOf({ received_at: new Date().toISOString(), discharging: 1, grid_present: 0 }), 'discharging');
  assert.equal(statusOf({ received_at: new Date().toISOString(), grid_present: 1, input_w: 161, output_w: 160 }), 'passthrough');
});

test('ingest requires a secret and validates values before writing', async () => {
  let wrote = false;
  const env = { INGEST_SECRET: 'private-test-key', DB: { prepare() { throw new Error('Should not write'); }, batch() { wrote = true; } } };
  const body = { device_id: 'P210', samples: [{ sample_id: '1', recorded_at: new Date().toISOString(), charge_percent: 150 }] };
  const unauthorized = await ingest({ request: new Request('https://example.com/api/ingest', { method: 'POST', body: JSON.stringify(body) }), env });
  assert.equal(unauthorized.status, 401);
  const invalid = await ingest({ request: new Request('https://example.com/api/ingest', { method: 'POST', headers: { authorization: 'Bearer private-test-key' }, body: JSON.stringify(body) }), env });
  assert.equal(invalid.status, 400);
  assert.equal(wrote, false);
});

test('month history groups samples into bounded time buckets', async () => {
  let sql, bindings;
  const env = { DB: { prepare(query) { sql = query; return { bind(...args) { bindings = args; return { all: async () => ({ results: [] }) }; } }; } } };
  const request = new Request('https://example.com/api/history?hours=720');
  const response = await history({ request, env });
  const result = await response.json();
  assert.equal(response.status, 200);
  assert.ok(sql.includes('GROUP BY'));
  assert.equal(bindings[2], 1740);
  assert.equal(result.bucketSeconds, 1740);
});
