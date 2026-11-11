import { json, readBody, validNumber } from './_lib.js';

export async function onRequestPost({ request, env }) {
  if (!env.INGEST_SECRET) return json({ error: 'Ingest is not configured' }, 503);
  const supplied = request.headers.get('authorization') || '';
  if (supplied !== `Bearer ${env.INGEST_SECRET}`) return json({ error: 'Unauthorised' }, 401);
  let body;
  try { body = await readBody(request); } catch { return json({ error: 'Invalid JSON or payload too large' }, 400); }
  if (!body || typeof body !== 'object' || !Array.isArray(body.samples) || body.samples.length < 1 || body.samples.length > 20) return json({ error: 'Expected 1–20 samples' }, 400);
  const deviceId = body.device_id;
  if (typeof deviceId !== 'string' || !/^[a-zA-Z0-9:_-]{1,80}$/.test(deviceId)) return json({ error: 'Invalid device ID' }, 400);
  const statements = [];
  for (const s of body.samples) {
    if (!s || typeof s.sample_id !== 'string' || !/^[a-zA-Z0-9:_-]{1,100}$/.test(s.sample_id)) return json({ error: 'Invalid sample ID' }, 400);
    const time = Date.parse(s.recorded_at);
    if (!Number.isFinite(time) || time < Date.now() - 7 * 86400000 || time > Date.now() + 300000) return json({ error: 'Invalid sample time' }, 400);
    for (const [key, min, max] of [['charge_percent', 0, 100], ['input_w', 0, 10000], ['output_w', 0, 10000], ['ac_input_w', 0, 10000], ['dc_input_w', 0, 10000], ['remaining_minutes', 0, 100000]]) {
      if (!validNumber(s[key], min, max)) return json({ error: `Invalid ${key}` }, 400);
    }
    for (const key of ['grid_present', 'discharging', 'fault']) if (![true, false, null, undefined].includes(s[key])) return json({ error: `Invalid ${key}` }, 400);
    const raw = JSON.stringify(s.raw ?? {});
    if (raw.length > 12000) return json({ error: 'Raw payload too large' }, 400);
    statements.push(env.DB.prepare(`INSERT OR IGNORE INTO telemetry (device_id,sample_id,recorded_at,charge_percent,input_w,output_w,ac_input_w,dc_input_w,remaining_minutes,grid_present,discharging,fault,raw_json) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?)`).bind(deviceId, s.sample_id, new Date(time).toISOString(), s.charge_percent ?? null, s.input_w ?? null, s.output_w ?? null, s.ac_input_w ?? null, s.dc_input_w ?? null, s.remaining_minutes ?? null, s.grid_present == null ? null : Number(s.grid_present), s.discharging == null ? null : Number(s.discharging), s.fault == null ? null : Number(s.fault), raw));
  }
  await env.DB.batch(statements);
  return json({ ok: true, accepted: statements.length });
}

