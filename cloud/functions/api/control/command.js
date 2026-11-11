import { json, readBody } from '../_lib.js';
import { requireControlSession, sameOrigin } from './_auth.js';
import { controlState } from './_state.js';

export async function onRequestPost({ request, env }) {
  if (!sameOrigin(request)) return json({ error: 'Invalid request origin' }, 403);
  const denied = await requireControlSession(request, env);
  if (denied) return denied;
  if (!request.headers.get('content-type')?.startsWith('application/json')) return json({ error: 'Invalid content type' }, 400);
  let body;
  try { body = await readBody(request); } catch { return json({ error: 'Invalid request' }, 400); }
  if (!body || typeof body.setting !== 'string' || !/^[a-zA-Z]{1,32}$/.test(body.setting)
      || !(typeof body.value === 'boolean' || Number.isInteger(body.value))) return json({ error: 'Invalid command' }, 400);
  if (!env.CONTROL_WORKER || !env.INGEST_SECRET) return json({ error: 'Controls are not configured' }, 503);
  const sample = await env.DB.prepare('SELECT device_id, received_at, raw_json FROM telemetry ORDER BY received_at DESC, id DESC LIMIT 1').first();
  const state = controlState(sample);
  if (!state.available) return json({ error: state.reason || 'Device settings are unavailable' }, 409);
  try {
    const response = await env.CONTROL_WORKER.fetch(new Request('https://control.internal/command', {
      method: 'POST', headers: { 'content-type': 'application/json', authorization: `Bearer ${env.INGEST_SECRET}` },
      body: JSON.stringify({ deviceId: sample.device_id, setting: body.setting, value: body.value }),
    }));
    const result = await response.json();
    if (!response.ok) return json({ error: result.error || 'Device command failed' }, response.status);
    return json(result);
  } catch {
    return json({ error: 'Could not reach the battery control service' }, 502);
  }
}
