import test from 'node:test';
import assert from 'node:assert/strict';
import { makeSession as farmSession } from '../functions/api/farm-control/_auth.js';
import { makeSession as batterySession } from '../functions/api/control/_auth.js';
import { onRequestGet as settings } from '../functions/api/farm-control/settings.js';
import { onRequestPost as command } from '../functions/api/farm-control/command.js';

test('farm settings need their own signed session', async () => {
  let calls = 0;
  const env = { SESSION_SECRET: 'test-secret', INGEST_SECRET: 'private-key', CONTROL_WORKER: {
    async fetch(request) {
      calls++;
      assert.equal(new URL(request.url).pathname, '/optics/settings');
      assert.equal(request.headers.get('authorization'), 'Bearer private-key');
      return Response.json({ devices: [{ model: 'MATE3S' }] });
    },
  } };
  const url = 'https://example.com/api/farm-control/settings';
  assert.equal((await settings({ request: new Request(url), env })).status, 401);
  const battery = await batterySession(env.SESSION_SECRET);
  assert.equal((await settings({ request: new Request(url, { headers: { cookie: `battery_control_session=${battery}` } }), env })).status, 401);
  const farm = await farmSession(env.SESSION_SECRET);
  const response = await settings({ request: new Request(url, { headers: { cookie: `farm_control_session=${farm}` } }), env });
  assert.equal(response.status, 200);
  assert.deepEqual((await response.json()).devices, [{ model: 'MATE3S' }]);
  assert.equal(calls, 1);
});

test('farm change endpoint rejects cross-origin and unauthenticated requests before reaching worker', async () => {
  let calls = 0;
  const env = { SESSION_SECRET: 'test-secret', INGEST_SECRET: 'private-key', CONTROL_WORKER: { fetch() { calls++; throw new Error('Must not send'); } } };
  const url = 'https://example.com/api/farm-control/command';
  const request = origin => new Request(url, { method: 'POST', headers: { origin, 'content-type': 'application/json' }, body: JSON.stringify({ kind: 'setting', port: 1, key: 'example', expectedCurrent: '0', value: '1' }) });
  assert.equal((await command({ request: request('https://other.example'), env })).status, 403);
  assert.equal((await command({ request: request('https://example.com'), env })).status, 401);
  assert.equal(calls, 0);
});
