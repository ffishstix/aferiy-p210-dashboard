import { json } from '../_lib.js';
import { requireFarmSession } from './_auth.js';

export async function onRequestGet({ request, env }) {
  const denied = await requireFarmSession(request, env);
  if (denied) return denied;
  if (!env.CONTROL_WORKER || !env.INGEST_SECRET) return json({ error: 'Farm controls are not configured' }, 503);
  try {
    const response = await env.CONTROL_WORKER.fetch(new Request('https://control.internal/optics/settings', {
      method: 'POST', headers: { authorization: `Bearer ${env.INGEST_SECRET}` },
    }));
    const result = await response.json();
    return json(result, response.status);
  } catch {
    return json({ error: 'Could not read OpticsRE settings' }, 502);
  }
}
