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

export function historyWindow(url) {
  const params = new URL(url).searchParams;
  const from = params.get('from'), to = params.get('to');
  if (from !== null || to !== null) {
    const start = Date.parse(from || ''), end = Date.parse(to || '');
    if (!Number.isFinite(start) || !Number.isFinite(end) || start >= end || end > Date.now() + 300000 || end - start > 720 * 3600000) return null;
    return { from: new Date(start).toISOString(), to: new Date(end).toISOString(), hours: (end - start) / 3600000 };
  }
  const hours = Math.min(720, Math.max(1, Number(params.get('hours')) || 24));
  return { from: new Date(Date.now() - hours * 3600000).toISOString(), to: new Date().toISOString(), hours };
}

