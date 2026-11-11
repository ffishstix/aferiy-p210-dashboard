export function json(data, status = 200, headers = {}) {
  return new Response(JSON.stringify(data), {
    status,
    headers: { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store', 'x-content-type-options': 'nosniff', ...headers },
  });
}

export function validNumber(value, min, max) {
  return value === null || value === undefined || (typeof value === 'number' && Number.isFinite(value) && value >= min && value <= max);
}

export function statusOf(sample) {
  if (!sample) return 'no_data';
  if (Date.now() - Date.parse(sample.received_at) > 120000) return 'stale';
  if (sample.fault === 1) return 'fault';
  if (sample.discharging === 1 && sample.grid_present !== 1) return 'discharging';
  if (sample.grid_present === 1 && sample.output_w > 0 && Math.abs(sample.input_w - sample.output_w) <= 25) return 'passthrough';
  if (sample.input_w > sample.output_w + 25) return 'charging';
  return 'idle';
}

export async function readBody(request) {
  if (Number(request.headers.get('content-length') || 0) > 32768) throw new Error('Payload too large');
  const text = await request.text();
  if (text.length > 32768) throw new Error('Payload too large');
  return JSON.parse(text);
}

