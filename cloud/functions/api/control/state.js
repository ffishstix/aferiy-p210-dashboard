import { json } from '../_lib.js';
import { requireControlSession } from './_auth.js';
import { controlState } from './_state.js';

export async function onRequestGet({ request, env }) {
  const denied = await requireControlSession(request, env);
  if (denied) return denied;
  const sample = await env.DB.prepare('SELECT received_at, raw_json FROM telemetry ORDER BY received_at DESC, id DESC LIMIT 1').first();
  return json(controlState(sample));
}
