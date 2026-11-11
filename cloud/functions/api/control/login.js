import { json, readBody } from '../_lib.js';
import { makeSession, sameOrigin, sessionCookie } from './_auth.js';

export async function onRequestPost({ request, env }) {
  if (!sameOrigin(request)) return json({ error: 'Invalid request origin' }, 403);
  if (!env.DASHBOARD_PASSWORD || !env.SESSION_SECRET) return json({ error: 'Controls are not configured' }, 503);
  const ip = request.headers.get('CF-Connecting-IP') || 'local';
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(ip + env.SESSION_SECRET));
  const ipHash = Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, '0')).join('');
  const now = Date.now();
  const previous = await env.DB.prepare('SELECT window_start, failures FROM login_attempts WHERE ip_hash = ?').bind(ipHash).first();
  if (previous && now - previous.window_start < 900000 && previous.failures >= 5) return json({ error: 'Too many attempts. Try again later.' }, 429);
  let body;
  try { body = await readBody(request); } catch { return json({ error: 'Invalid request' }, 400); }
  const password = typeof body.password === 'string' ? body.password : '';
  const a = new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(password)));
  const b = new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(env.DASHBOARD_PASSWORD)));
  let diff = 0;
  for (let i = 0; i < a.length; i++) diff |= a[i] ^ b[i];
  if (diff !== 0) {
    const start = previous && now - previous.window_start < 900000 ? previous.window_start : now;
    const failures = previous && start === previous.window_start ? previous.failures + 1 : 1;
    await env.DB.prepare('INSERT INTO login_attempts (ip_hash, window_start, failures) VALUES (?, ?, ?) ON CONFLICT(ip_hash) DO UPDATE SET window_start=excluded.window_start, failures=excluded.failures').bind(ipHash, start, failures).run();
    return json({ error: 'Incorrect password' }, 401);
  }
  await env.DB.prepare('DELETE FROM login_attempts WHERE ip_hash = ?').bind(ipHash).run();
  return json({ ok: true }, 200, { 'set-cookie': sessionCookie(await makeSession(env.SESSION_SECRET), request) });
}
