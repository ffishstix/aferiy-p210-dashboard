import { historyWindow, json } from './_lib.js';
export async function onRequestGet({ request, env }) {
  const window = historyWindow(request.url);
  if (!window) return json({ error: 'Choose a valid range of up to 30 days' }, 400);
  const { hours, from, to } = window;
  const bucketSeconds = Math.max(60, Math.ceil(hours * 3600 / 1500 / 60) * 60);
  const rows = await env.DB.prepare(`SELECT MIN(recorded_at) AS recorded_at,
    AVG(charge_percent) AS charge_percent, AVG(input_w) AS input_w,
    AVG(output_w) AS output_w, AVG(ac_input_w) AS ac_input_w, AVG(dc_input_w) AS dc_input_w,
    AVG(remaining_minutes) AS remaining_minutes,
    MAX(grid_present) AS grid_present, MAX(discharging) AS discharging,
    MAX(fault) AS fault
    FROM telemetry WHERE recorded_at >= ? AND recorded_at <= ?
    GROUP BY CAST(strftime('%s', recorded_at) AS INTEGER) / ?
    ORDER BY recorded_at ASC LIMIT 2000`).bind(from, to, bucketSeconds).all();
  return json({ from, to, hours, bucketSeconds, samples: rows.results });
}
