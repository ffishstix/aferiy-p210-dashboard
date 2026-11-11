const $ = id => document.getElementById(id);
let currentHours = 24;
let timer;
let refreshing = false;
if (new URLSearchParams(location.search).get('compact') === '1') document.body.classList.add('compact');

const value = (v, digits = 0) => v == null ? '—' : Number(v).toFixed(digits);
const when = iso => iso ? new Date(iso).toLocaleString([], { dateStyle: 'medium', timeStyle: 'short' }) : '—';
const clock = minutes => {
  if (minutes == null) return '—';
  const rounded = Math.max(0, Math.round(minutes));
  return rounded < 60 ? `${rounded} min` : `${Math.floor(rounded / 60)}h ${rounded % 60}m`;
};

async function get(url) {
  const response = await fetch(url, { cache: 'no-store' });
  if (!response.ok) throw new Error(`Server error ${response.status}`);
  return response.json();
}

function renderStatus({ sample: s, status }) {
  const titles = { no_data: 'Waiting for battery data', stale: 'Connection needs attention', fault: 'Battery fault reported', discharging: 'Running on battery', charging: 'Battery charging', passthrough: 'Power passing through', idle: 'Battery standing by' };
  $('state-title').textContent = titles[status] || 'Battery status';
  $('state-detail').textContent = status === 'no_data' ? 'The site is ready. Cloud collection is waiting for the BrightEMS connection.' : status === 'stale' ? 'The last reading is old. Charge and time remaining may have changed.' : status === 'discharging' && s.charge_percent != null && s.charge_percent < 15 ? 'Low charge. Save your work and shut down the PC soon.' : status === 'fault' ? 'Check the battery and its app for details.' : 'Latest readings from your P210.';
  $('last-update').textContent = s ? `UPDATED ${when(s.received_at).toUpperCase()}` : 'NO READING YET';
  $('connection').textContent = status === 'stale' ? 'Data is stale' : status === 'no_data' ? 'Awaiting cloud connection' : 'Live data';
  $('connection').className = `connection ${status === 'stale' || status === 'fault' ? 'warning' : status === 'no_data' ? '' : 'live'}`;
  $('charge-big').textContent = s?.charge_percent == null ? '—' : `${Math.round(s.charge_percent)}%`;
  $('battery-fill').style.height = `${s?.charge_percent ?? 0}%`;
  $('input').textContent = value(s?.input_w); $('output').textContent = value(s?.output_w);
  $('remaining').textContent = status === 'discharging' ? clock(s?.remaining_minutes) : '—';
  $('remaining-detail').textContent = status === 'stale' ? 'Last reading is old' : status === 'discharging' ? 'Rough estimate at the current load' : 'Shown while discharging';
  $('grid').textContent = s?.grid_present == null ? '—' : s.grid_present ? 'Present' : 'Absent';
  $('input-detail').textContent = s?.ac_input_w != null || s?.dc_input_w != null ? `AC ${value(s.ac_input_w)} W · DC ${value(s.dc_input_w)} W` : 'All input sources';
  $('health-title').textContent = status === 'no_data' ? 'No data yet' : status === 'stale' ? 'Reading is stale' : status === 'fault' ? 'Fault reported' : 'Receiving readings';
  $('health-detail').textContent = s ? `Last received ${when(s.received_at)}.` : 'Connection information will appear after the first reading.';
}

function renderChart(samples, hours) {
  const points = samples.filter(s => s.charge_percent != null && Number.isFinite(Date.parse(s.recorded_at)));
  $('chart-empty').hidden = points.length > 0;
  $('range-start').textContent = when(new Date(Date.now() - hours * 3600000).toISOString());
  $('range-end').textContent = when(new Date().toISOString());
  if (!points.length) { $('chart-line').setAttribute('d', ''); $('chart-area').setAttribute('d', ''); return; }
  const start = Date.now() - hours * 3600000, width = hours * 3600000;
  const coords = points.map(s => [Math.max(0, Math.min(1000, (Date.parse(s.recorded_at) - start) / width * 1000)), 240 - Number(s.charge_percent) * 2.4]);
  const segments = [];
  let segment = [];
  for (let i = 0; i < coords.length; i++) {
    if (i && Date.parse(points[i].recorded_at) - Date.parse(points[i-1].recorded_at) > Math.max(120000, width / 50)) { segments.push(segment); segment = []; }
    segment.push(coords[i]);
  }
  segments.push(segment);
  $('chart-line').setAttribute('d', segments.map(seg => seg.map(([x,y],i) => `${i ? 'L' : 'M'}${x.toFixed(1)} ${y.toFixed(1)}`).join(' ')).join(' '));
  $('chart-area').setAttribute('d', segments.map(seg => seg.length > 1 ? `M${seg[0][0].toFixed(1)} 240 ` + seg.map(([x,y]) => `L${x.toFixed(1)} ${y.toFixed(1)}`).join(' ') + ` L${seg.at(-1)[0].toFixed(1)} 240 Z` : '').join(' '));
}

function renderPowerChart(samples, hours) {
  const points = samples.filter(s => Number.isFinite(Date.parse(s.recorded_at)) && (s.input_w != null || s.output_w != null));
  $('power-empty').hidden = points.length > 0;
  $('power-range-start').textContent = when(new Date(Date.now() - hours * 3600000).toISOString());
  $('power-range-end').textContent = when(new Date().toISOString());
  const maximum = Math.max(100, ...points.flatMap(s => [Number(s.input_w) || 0, Number(s.output_w) || 0]));
  const scale = Math.ceil(maximum / 100) * 100;
  $('power-max').textContent = `${scale} W`;
  $('power-mid').textContent = `${scale / 2} W`;
  const start = Date.now() - hours * 3600000, width = hours * 3600000;
  for (const [field, element] of [['input_w','power-input-line'], ['output_w','power-output-line']]) {
    let path = '', previousTime = null;
    for (const point of points) {
      if (point[field] == null) { previousTime = null; continue; }
      const time = Date.parse(point.recorded_at);
      const x = Math.max(0, Math.min(1000, (time - start) / width * 1000));
      const y = 240 - Math.max(0, Math.min(scale, Number(point[field]))) / scale * 240;
      path += `${previousTime === null || time - previousTime > Math.max(120000, width / 50) ? 'M' : 'L'}${x.toFixed(1)} ${y.toFixed(1)} `;
      previousTime = time;
    }
    $(element).setAttribute('d', path);
  }
}

async function refresh(fresh = false) {
  if (refreshing) return;
  refreshing = true;
  try {
    const status = await get(fresh ? '/api/status?fresh=1' : '/api/status');
    const history = await get(`/api/history?hours=${currentHours}`);
    renderStatus(status); renderChart(history.samples, currentHours); renderPowerChart(history.samples, currentHours);
  }
  catch { $('connection').textContent = fresh ? 'Fresh reading unavailable' : 'Website unavailable'; $('connection').className = 'connection warning'; }
  finally { refreshing = false; }
}

$('compact-toggle').addEventListener('click', () => {
  document.body.classList.toggle('compact');
  const url = new URL(location.href);
  if (document.body.classList.contains('compact')) url.searchParams.set('compact', '1'); else url.searchParams.delete('compact');
  history.replaceState(null, '', url);
  $('compact-toggle').textContent = document.body.classList.contains('compact') ? 'Full view' : 'Compact view';
});
if (document.body.classList.contains('compact')) $('compact-toggle').textContent = 'Full view';
document.querySelectorAll('[data-hours]').forEach(button => button.addEventListener('click', () => { currentHours = Number(button.dataset.hours); document.querySelectorAll('[data-hours]').forEach(b => b.classList.toggle('selected', b === button)); refresh(); }));
refresh(true); timer = setInterval(() => refresh(), 30000);

