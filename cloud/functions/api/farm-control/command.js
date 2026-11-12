import { json, readBody } from '../_lib.js';
import { requireFarmSession, sameOrigin } from './_auth.js';

export async function onRequestPost({ request, env }) {
  if (!sameOrigin(request)) return json({ error: 'Invalid request origin' }, 403);
  const denied = await requireFarmSession(request, env);
  if (denied) return denied;
  if (!request.headers.get('content-type')?.startsWith('application/json')) return json({ error: 'Invalid content type' }, 400);
  if (!env.CONTROL_WORKER || !env.INGEST_SECRET) return json({ error: 'Farm controls are not configured' }, 503);
  let body;
  try { body = await readBody(request); } catch { return json({ error: 'Invalid request' }, 400); }
  if (!body || !['setting', 'quick', 'system'].includes(body.kind) || !Number.isSafeInteger(body.port ?? 0)
      || typeof body.key !== 'string' || typeof body.expectedCurrent !== 'string'
      || !['string', 'number'].includes(typeof body.value)) return json({ error: 'Invalid OpticsRE change' }, 400);
  try {
    const response = await env.CONTROL_WORKER.fetch(new Request('https://control.internal/optics/command', {
      method: 'POST', headers: { 'content-type': 'application/json', authorization: `Bearer ${env.INGEST_SECRET}` },
      body: JSON.stringify(body),
    }));
    const result = await response.json();
    return json(result, response.status);
  } catch {
    return json({ error: 'Could not reach OpticsRE controls' }, 502);
  }
}
