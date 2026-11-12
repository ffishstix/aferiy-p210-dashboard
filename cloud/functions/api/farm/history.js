import { historyWindow, json } from '../_lib.js';

export async function onRequestGet({ request, env }) {
  const window = historyWindow(request.url);
  if (!window) return json({ error: 'Choose a valid range of up to 30 days' }, 400);
  const { hours, from, to } = window;
  const bucketSeconds = Math.max(60, Math.ceil(hours * 3600 / 1500 / 60) * 60);
  const rows = await env.DB.prepare(`SELECT MIN(recorded_at) AS recorded_at, AVG(solar_w) AS solar_w, AVG(generator_w) AS generator_w, MAX(generator_running) AS generator_running, AVG(main_charge_percent) AS main_charge_percent, AVG(main_battery_w) AS main_battery_w, AVG(main_battery_v) AS main_battery_v, AVG(load_w) AS load_w FROM farm_telemetry WHERE recorded_at >= ? AND recorded_at <= ? GROUP BY CAST(strftime('%s', recorded_at) AS INTEGER) / ? ORDER BY recorded_at ASC LIMIT 2000`).bind(from, to, bucketSeconds).all();
  return json({ from, to, hours, bucketSeconds, samples: rows.results });
}
