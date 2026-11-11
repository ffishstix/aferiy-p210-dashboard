import { json } from '../_lib.js';

const encoder = new TextEncoder();
const COOKIE = 'battery_control_session';
const MAX_AGE = 24 * 60 * 60;

async function hmac(message, secret) {
  const key = await crypto.subtle.importKey('raw', encoder.encode(secret), { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);
  return Array.from(new Uint8Array(await crypto.subtle.sign('HMAC', key, encoder.encode(message))), b => b.toString(16).padStart(2, '0')).join('');
}

function equal(a, b) {
  if (typeof a !== 'string' || typeof b !== 'string') return false;
  let diff = a.length ^ b.length;
  for (let i = 0; i < Math.max(a.length, b.length); i++) diff |= (a.charCodeAt(i) || 0) ^ (b.charCodeAt(i) || 0);
  return diff === 0;
}

export async function authorised(request, env) {
  if (!env.SESSION_SECRET) return false;
  const cookie = request.headers.get('cookie') || '';
  const value = cookie.split(';').map(v => v.trim()).find(v => v.startsWith(`${COOKIE}=`))?.slice(COOKIE.length + 1);
  if (!value) return false;
  const [expires, signature] = value.split('.');
  if (!expires || !signature || !/^\d+$/.test(expires) || Number(expires) < Date.now()) return false;
  return equal(signature, await hmac(expires, env.SESSION_SECRET));
}

export async function makeSession(secret) {
  const expires = String(Date.now() + MAX_AGE * 1000);
  return `${expires}.${await hmac(expires, secret)}`;
}

export function sessionCookie(value, request) {
  const secure = new URL(request.url).protocol === 'https:' ? '; Secure' : '';
  return `${COOKIE}=${value}; HttpOnly; SameSite=Strict; Path=/api/control; Max-Age=${MAX_AGE}${secure}`;
}

export function clearSessionCookie(request) {
  const secure = new URL(request.url).protocol === 'https:' ? '; Secure' : '';
  return `${COOKIE}=; HttpOnly; SameSite=Strict; Path=/api/control; Max-Age=0${secure}`;
}

export function sameOrigin(request) {
  const origin = request.headers.get('origin');
  return origin === new URL(request.url).origin;
}

export async function requireControlSession(request, env) {
  return await authorised(request, env) ? null : json({ error: 'Sign in to use battery controls' }, 401);
}
