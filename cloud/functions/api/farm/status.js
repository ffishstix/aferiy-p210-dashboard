import { json } from '../_lib.js';

export async function onRequestGet({ env }) {
  const sample = await env.DB.prepare(`SELECT recorded_at, received_at, solar_w, generator_w, generator_running, ac_source, main_charge_percent, main_battery_w, main_battery_v, load_w FROM farm_telemetry ORDER BY recorded_at DESC, id DESC LIMIT 1`).first();
  const age = sample ? Date.now() - Date.parse(sample.recorded_at) : Infinity;
  return json({ sample, status: !sample ? 'no_data' : !Number.isFinite(age) || age < -300000 || age > 120000 ? 'stale' : 'live' });
}
