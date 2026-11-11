import { json } from './_lib.js';
export async function onRequestGet({ request, env }) {
  const url = new URL(request.url);
  const hours = Math.min(720, Math.max(1, Number(url.searchParams.get('hours')) || 24));
  const since = new Date(Date.now() - hours * 3600000).toISOString();
  const bucketSeconds = Math.max(60, Math.ceil(hours * 3600 / 1500 / 60) * 60);
  const rows = await env.DB.prepare(`SELECT MIN(recorded_at) AS recorded_at,
    AVG(charge_percent) AS charge_percent, AVG(input_w) AS input_w,
    AVG(output_w) AS output_w, AVG(remaining_minutes) AS remaining_minutes,
    MAX(grid_present) AS grid_present, MAX(discharging) AS discharging,
    MAX(fault) AS fault
    FROM telemetry WHERE recorded_at >= ?
    GROUP BY CAST(strftime('%s', recorded_at) AS INTEGER) / ?
    ORDER BY recorded_at ASC LIMIT 2000`).bind(since, bucketSeconds).all();
  return json({ hours, bucketSeconds, samples: rows.results });
}

