import { json, statusOf } from './_lib.js';
export async function onRequestGet({ request, env }) {
  if (new URL(request.url).searchParams.get('fresh') === '1') {
    if (!env.CONTROL_WORKER || !env.INGEST_SECRET) return json({ error: 'Live readings are not configured' }, 503);
    try {
      const response = await env.CONTROL_WORKER.fetch(new Request('https://control.internal/check', {
        method: 'POST', headers: { authorization: `Bearer ${env.INGEST_SECRET}` },
      }));
      if (!response.ok) return json({ error: 'Could not get a fresh battery reading' }, 502);
    } catch {
      return json({ error: 'Could not reach the battery reading service' }, 502);
    }
  }
  const sample = await env.DB.prepare(`SELECT recorded_at, received_at, charge_percent, input_w, output_w,
    ac_input_w, dc_input_w, remaining_minutes, grid_present, discharging, fault
    FROM telemetry ORDER BY received_at DESC, id DESC LIMIT 1`).first();
  return json({ sample, status: statusOf(sample) });
}

