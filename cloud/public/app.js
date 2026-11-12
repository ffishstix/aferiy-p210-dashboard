const $ = id => document.getElementById(id);
let currentHours = 24;
let customRange = null;
const zoomStack = [];
let timer;
let refreshing = false;
let pendingRefresh = false;
const charts = {};
if (new URLSearchParams(location.search).get('compact') === '1') document.body.classList.add('compact');

const value = (v, digits = 0) => v == null ? '—' : Number(v).toFixed(digits);
const when = iso => iso ? new Date(iso).toLocaleString([], { dateStyle: 'medium', timeStyle: 'short' }) : '—';
const clock = minutes => {
  if (minutes == null) return '—';
  const rounded = Math.max(0, Math.round(minutes));
  return rounded < 60 ? `${rounded} min` : `${Math.floor(rounded / 60)}h ${rounded % 60}m`;
};
const watts = v => v == null ? 'unavailable' : `${Math.round(v).toLocaleString()} W`;
const chartX = (iso, from, to) => Math.max(0, Math.min(1000, (Date.parse(iso) - from) / (to - from) * 1000));
const linePath = (points, field, from, to, yFor, gap) => {
  let path = '', previous = null;
  for (const point of points) {
    const time = Date.parse(point.recorded_at), reading = point[field];
    if (reading == null || !Number.isFinite(Number(reading))) { previous = null; continue; }
    const x = chartX(point.recorded_at, from, to), y = yFor(Number(reading));
    path += `${previous === null || time - previous > gap ? 'M' : 'L'}${x.toFixed(1)} ${y.toFixed(1)} `;
    previous = time;
  }
  return path;
};
const historyQuery = () => customRange ? `from=${encodeURIComponent(new Date(customRange.from).toISOString())}&to=${encodeURIComponent(new Date(customRange.to).toISOString())}` : `hours=${currentHours}`;
const localInputValue = date => {
  const pad = n => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
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

function renderFarm({ sample: s, status }) {
  $('farm-connection').textContent = status === 'live' ? 'Live data' : status === 'stale' ? 'Farm reading is old' : 'Waiting for farm readings';
  $('farm-connection').className = `connection ${status === 'live' ? 'live' : status === 'stale' ? 'warning' : ''}`;
  $('farm-updated').textContent = s ? `Last reading ${when(s.recorded_at)}${status === 'stale' ? ' · figures may have changed' : ''}` : 'The farm gateway has not sent a reading yet.';
  $('farm-solar').textContent = s?.solar_w == null ? '—' : `${value(s.solar_w)} W`;
  $('farm-generator').textContent = s?.generator_running === 0 ? 'Off' : s?.generator_w == null ? s?.generator_running === 1 ? 'Running' : '—' : `${value(s.generator_w)} W`;
  $('farm-charge').textContent = s?.main_charge_percent != null ? `${value(s.main_charge_percent)}%` : s?.main_battery_v != null ? `${value(s.main_battery_v, 1)} V` : '—';
  $('farm-load').textContent = s?.load_w == null ? '—' : `${value(s.load_w)} W`;
  $('farm-battery-flow').textContent = s?.main_battery_w == null ? 'Power flow unavailable' : `${Math.abs(s.main_battery_w).toFixed(0)} W ${s.main_battery_w > 0 ? 'charging' : s.main_battery_w < 0 ? 'draining' : 'balanced'}`;
  $('farm-source').textContent = s?.ac_source ? `AC source: ${s.ac_source}` : 'AC source unknown';
}

function setChart(id, points, from, to, bucketSeconds, series, explain) {
  charts[id] = { points, from, to, bucketSeconds, series, explain };
  const svg = $(id);
  const overlay = svg.querySelector('.chart-hover');
  let dots = svg.querySelector('.chart-isolated');
  if (!dots) { dots = document.createElementNS('http://www.w3.org/2000/svg', 'g'); dots.classList.add('chart-isolated'); overlay.before(dots); }
  dots.replaceChildren();
  const gap = Math.max(bucketSeconds * 2500, 120000);
  for (let i = 0; i < points.length; i++) {
    const time = Date.parse(points[i].recorded_at);
    if ((i > 0 && time - Date.parse(points[i - 1].recorded_at) <= gap) || (i < points.length - 1 && Date.parse(points[i + 1].recorded_at) - time <= gap)) continue;
    for (const item of series) {
      if (points[i][item.field] == null) continue;
      const dot = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
      dot.setAttribute('cx', chartX(points[i].recorded_at, from, to)); dot.setAttribute('cy', item.y(Number(points[i][item.field])));
      dot.setAttribute('r', 3); dot.setAttribute('fill', item.color);
      dots.append(dot);
    }
  }
  overlay.replaceChildren();
  const readout = svg.closest('.chart-wrap').parentElement.querySelector('.chart-readout');
  readout.textContent = 'Hover or tap the graph to inspect a point. Drag across it to zoom in.';
  readout.classList.remove('has-point');
}

function renderChart(data) {
  const { samples, bucketSeconds } = data;
  const from = Date.parse(data.from), to = Date.parse(data.to);
  const points = samples.filter(s => s.charge_percent != null && Number.isFinite(Date.parse(s.recorded_at)));
  $('chart-empty').hidden = points.length > 0;
  $('range-start').textContent = when(data.from);
  $('range-end').textContent = when(data.to);
  const gap = Math.max(bucketSeconds * 2500, 120000);
  $('chart-line').setAttribute('d', linePath(points, 'charge_percent', from, to, v => 240 - Math.max(0, Math.min(100, v)) * 2.4, gap));
  const areas = []; let segment = [];
  for (const point of points) {
    if (segment.length && Date.parse(point.recorded_at) - Date.parse(segment.at(-1).recorded_at) > gap) { areas.push(segment); segment = []; }
    segment.push(point);
  }
  if (segment.length) areas.push(segment);
  $('chart-area').setAttribute('d', areas.map(group => group.length > 1 ? `M${chartX(group[0].recorded_at, from, to).toFixed(1)} 240 ` + group.map(point => `L${chartX(point.recorded_at, from, to).toFixed(1)} ${(240 - Number(point.charge_percent) * 2.4).toFixed(1)}`).join(' ') + ` L${chartX(group.at(-1).recorded_at, from, to).toFixed(1)} 240 Z` : '').join(' '));
  setChart('chart', points, from, to, bucketSeconds, [{ field: 'charge_percent', color: '#bdfb7a', y: v => 240 - Math.max(0, Math.min(100, v)) * 2.4 }], s => {
    const net = s.input_w != null && s.output_w != null ? s.input_w - s.output_w : null;
    const direction = s.fault ? 'The P210 reported a fault.' : net == null ? 'Power direction is unavailable.' : net > 25 ? 'The P210 was taking in more power than it supplied.' : net < -25 ? 'The P210 supplied more power than it received.' : 'Input and output were roughly balanced.';
    return [`AFERIY charge ${value(s.charge_percent, 1)}%.`, direction, `Input ${watts(s.input_w)} · output ${watts(s.output_w)}.`];
  });
}

function renderPowerChart(data) {
  const { samples, bucketSeconds } = data;
  const from = Date.parse(data.from), to = Date.parse(data.to);
  const points = samples.filter(s => Number.isFinite(Date.parse(s.recorded_at)) && (s.input_w != null || s.output_w != null));
  $('power-empty').hidden = points.length > 0;
  $('power-range-start').textContent = when(data.from);
  $('power-range-end').textContent = when(data.to);
  const maximum = Math.max(100, ...points.flatMap(s => [Number(s.input_w) || 0, Number(s.output_w) || 0]));
  const scale = Math.ceil(maximum / 100) * 100;
  $('power-max').textContent = `${scale} W`;
  $('power-mid').textContent = `${scale / 2} W`;
  const y = v => 240 - Math.max(0, Math.min(scale, v)) / scale * 240;
  const gap = Math.max(bucketSeconds * 2500, 120000);
  $('power-input-line').setAttribute('d', linePath(points, 'input_w', from, to, y, gap));
  $('power-output-line').setAttribute('d', linePath(points, 'output_w', from, to, y, gap));
  setChart('power-chart', points, from, to, bucketSeconds, [{ field: 'input_w', color: '#74cde2', y }, { field: 'output_w', color: '#f5c46b', y }], s => {
    const net = s.input_w != null && s.output_w != null ? s.input_w - s.output_w : null;
    const direction = net == null ? 'Power direction is unavailable.' : net > 25 ? `Input exceeded output by ${watts(net)}; the P210 was charging.` : net < -25 ? `Output exceeded input by ${watts(-net)}; the P210 was draining.` : 'Input and output were roughly balanced.';
    return [`Input ${watts(s.input_w)} · output ${watts(s.output_w)}.`, `AC input ${watts(s.ac_input_w)} · DC input ${watts(s.dc_input_w)}.`, direction];
  });
}

function renderFarmChart(data) {
  const { samples, bucketSeconds } = data;
  const from = Date.parse(data.from), to = Date.parse(data.to);
  const points = samples.filter(s => Number.isFinite(Date.parse(s.recorded_at)) && ['solar_w', 'generator_w', 'main_battery_w', 'load_w'].some(field => s[field] != null));
  $('farm-empty').hidden = points.length > 0;
  $('farm-range-start').textContent = when(data.from);
  $('farm-range-end').textContent = when(data.to);
  const peak = Math.max(100, ...points.flatMap(s => ['solar_w', 'generator_w', 'main_battery_w', 'load_w'].map(field => Math.abs(Number(s[field]) || 0))));
  const scale = Math.ceil(peak / 100) * 100;
  $('farm-max').textContent = `${scale.toLocaleString()} W`;
  $('farm-min').textContent = `−${scale.toLocaleString()} W`;
  const y = v => 120 - Math.max(-scale, Math.min(scale, v)) / scale * 119;
  const gap = Math.max(bucketSeconds * 2500, 120000);
  const series = [
    { field: 'solar_w', element: 'farm-solar-line', color: '#bdfb7a', y },
    { field: 'generator_w', element: 'farm-generator-line', color: '#f5c46b', y },
    { field: 'load_w', element: 'farm-load-line', color: '#74cde2', y },
    { field: 'main_battery_w', element: 'farm-battery-line', color: '#de9bfa', y },
  ];
  for (const item of series) $(item.element).setAttribute('d', linePath(points, item.field, from, to, y, gap));
  setChart('farm-chart', points, from, to, bucketSeconds, series, s => {
    const battery = s.main_battery_w == null ? 'Main battery flow unavailable.' : s.main_battery_w > 25 ? `The main battery gained about ${watts(s.main_battery_w)}.` : s.main_battery_w < -25 ? `The main battery supplied about ${watts(-s.main_battery_w)}.` : 'The main battery was roughly balanced.';
    const generator = s.generator_running ? 'The generator was running during this interval.' : s.generator_running === 0 ? 'The generator was off.' : 'Generator state is unavailable.';
    return [`Solar ${watts(s.solar_w)} · generator ${watts(s.generator_w)}.`, `Farm load ${watts(s.load_w)} · battery ${s.main_battery_v == null ? 'voltage unavailable' : `${value(s.main_battery_v, 1)} V`}.`, battery, generator];
  });
}

function setupChartInspection(svg) {
  const ns = 'http://www.w3.org/2000/svg';
  const wrap = svg.closest('.chart-wrap');
  const overlay = document.createElementNS(ns, 'g');
  overlay.classList.add('chart-hover');
  svg.append(overlay);
  const selectionOverlay = document.createElementNS(ns, 'g');
  selectionOverlay.classList.add('chart-selection');
  svg.append(selectionOverlay);
  const tip = document.createElement('div');
  tip.className = 'chart-readout';
  tip.id = `${svg.id}-readout`;
  tip.setAttribute('role', 'status');
  tip.setAttribute('aria-live', 'polite');
  tip.textContent = 'Hover or tap the graph to inspect a point. Drag across it to zoom in.';
  wrap.parentElement.querySelector('.chart-footer').after(tip);
  svg.setAttribute('aria-describedby', tip.id);
  const hide = () => { overlay.replaceChildren(); tip.textContent = 'Hover or tap the graph to inspect a point. Drag across it to zoom in.'; tip.classList.remove('has-point'); };
  const show = index => {
    const chart = charts[svg.id];
    if (!chart?.points.length) return;
    index = Math.max(0, Math.min(chart.points.length - 1, index));
    const point = chart.points[index];
    const x = chartX(point.recorded_at, chart.from, chart.to);
    chart.index = index;
    overlay.replaceChildren();
    const guide = document.createElementNS(ns, 'line');
    guide.setAttribute('class', 'chart-hover-line');
    guide.setAttribute('x1', x); guide.setAttribute('x2', x); guide.setAttribute('y1', 0); guide.setAttribute('y2', 240);
    overlay.append(guide);
    for (const series of chart.series) {
      const reading = point[series.field];
      if (reading == null) continue;
      const dot = document.createElementNS(ns, 'circle');
      dot.setAttribute('class', 'chart-hover-dot');
      dot.setAttribute('cx', x); dot.setAttribute('cy', series.y(Number(reading))); dot.setAttribute('r', 5); dot.setAttribute('fill', series.color);
      overlay.append(dot);
    }
    tip.replaceChildren();
    const title = document.createElement('strong'); title.textContent = when(point.recorded_at); tip.append(title);
    for (const line of chart.explain(point)) { const row = document.createElement('p'); row.textContent = line; tip.append(row); }
    const note = document.createElement('small'); note.textContent = chart.bucketSeconds > 60 ? `Values are averages over about ${Math.round(chart.bucketSeconds / 60)} minutes.` : 'Values are from this one-minute interval.'; tip.append(note);
    tip.classList.add('has-point');
  };
  const plotX = clientX => {
    const rect = svg.getBoundingClientRect();
    return Math.max(0, Math.min(1000, (clientX - rect.left) / rect.width * 1000));
  };
  const timeAt = (chart, x) => chart.from + x / 1000 * (chart.to - chart.from);
  let drag = null;
  const drawSelection = (a, b) => {
    selectionOverlay.replaceChildren();
    const left = Math.min(a, b), width = Math.abs(b - a);
    if (width < 2) return;
    const band = document.createElementNS(ns, 'rect');
    band.setAttribute('class', 'chart-zoom-band');
    band.setAttribute('x', left); band.setAttribute('y', 0); band.setAttribute('width', width); band.setAttribute('height', 240);
    selectionOverlay.append(band);
  };
  svg.addEventListener('pointermove', event => {
    const chart = charts[svg.id];
    if (!chart) return;
    if (drag && event.pointerId === drag.pointerId) {
      drag.endX = plotX(event.clientX);
      drawSelection(drag.startX, drag.endX);
      if (Math.abs(drag.endX - drag.startX) > 8) {
        overlay.replaceChildren();
        tip.textContent = `Release to zoom: ${when(new Date(timeAt(chart, Math.min(drag.startX, drag.endX))))} to ${when(new Date(timeAt(chart, Math.max(drag.startX, drag.endX))))}.`;
        tip.classList.add('has-point');
      }
      return;
    }
    if (!chart.points.length) return;
    const rect = svg.getBoundingClientRect();
    const time = chart.from + Math.max(0, Math.min(1, (event.clientX - rect.left) / rect.width)) * (chart.to - chart.from);
    let low = 0, high = chart.points.length;
    while (low < high) { const mid = (low + high) >> 1; if (Date.parse(chart.points[mid].recorded_at) < time) low = mid + 1; else high = mid; }
    const index = low > 0 && (low === chart.points.length || time - Date.parse(chart.points[low - 1].recorded_at) < Date.parse(chart.points[low].recorded_at) - time) ? low - 1 : low;
    if (Math.abs(Date.parse(chart.points[index].recorded_at) - time) > Math.max(120000, chart.bucketSeconds * 2000)) { hide(); return; }
    show(index);
  });
  svg.addEventListener('pointerdown', event => {
    if (event.button !== 0) return;
    svg.focus();
    drag = { pointerId: event.pointerId, startX: plotX(event.clientX), endX: plotX(event.clientX) };
    svg.setPointerCapture(event.pointerId);
  });
  svg.addEventListener('pointerup', event => {
    if (!drag || event.pointerId !== drag.pointerId) return;
    drag.endX = plotX(event.clientX);
    const chart = charts[svg.id];
    const start = Math.min(drag.startX, drag.endX), end = Math.max(drag.startX, drag.endX);
    selectionOverlay.replaceChildren();
    drag = null;
    if (!chart || end - start < 8) return;
    const from = Math.round(timeAt(chart, start)), to = Math.round(timeAt(chart, end));
    if (to - from < 60000) { tip.textContent = 'Select at least one minute to zoom in.'; return; }
    zoomStack.push({ customRange, currentHours });
    customRange = { from, to };
    $('history-from').value = localInputValue(new Date(from)); $('history-to').value = localInputValue(new Date(to));
    $('history-search-message').textContent = `Zoomed to ${when(new Date(from))} to ${when(new Date(to))} across all graphs.`;
    document.querySelectorAll('[data-hours]').forEach(button => button.classList.remove('selected'));
    $('zoom-back').disabled = false;
    void refresh();
  });
  svg.addEventListener('pointercancel', () => { drag = null; selectionOverlay.replaceChildren(); hide(); });
  svg.addEventListener('pointerleave', () => { if (!drag && document.activeElement !== svg) hide(); });
  svg.addEventListener('focus', () => { const chart = charts[svg.id]; if (chart?.points.length) show(chart.index ?? chart.points.length - 1); });
  svg.addEventListener('blur', hide);
  svg.addEventListener('keydown', event => {
    if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
    const chart = charts[svg.id];
    if (!chart?.points.length) return;
    event.preventDefault();
    const index = event.key === 'Home' ? 0 : event.key === 'End' ? chart.points.length - 1 : (chart.index ?? chart.points.length - 1) + (event.key === 'ArrowLeft' ? -1 : 1);
    show(index);
  });
}

async function refresh(fresh = false) {
  if (refreshing) { pendingRefresh = true; return; }
  refreshing = true;
  const query = `${historyQuery()}&at=${Date.now()}`;
  await Promise.all([
    get('/api/status').then(status => {
      renderStatus(status);
      if (fresh) void get('/api/status?fresh=1').then(renderStatus).catch(() => {});
    }).catch(() => {
      $('connection').textContent = 'Website unavailable';
      $('connection').className = 'connection warning';
    }),
    get(`/api/history?${query}`).then(history => {
      renderChart(history);
      renderPowerChart(history);
    }).catch(() => {}),
    get(`/api/farm/status?at=${Date.now()}`).then(renderFarm).catch(() => {
      $('farm-connection').textContent = 'Farm reading unavailable';
      $('farm-connection').className = 'connection warning';
    }),
    get(`/api/farm/history?${query}`).then(renderFarmChart).catch(() => {
      $('farm-empty').hidden = false;
      $('farm-empty').textContent = 'Farm history is unavailable right now.';
    }),
  ]);
  refreshing = false;
  if (pendingRefresh) { pendingRefresh = false; void refresh(); }
}

$('compact-toggle').addEventListener('click', () => {
  document.body.classList.toggle('compact');
  const url = new URL(location.href);
  if (document.body.classList.contains('compact')) url.searchParams.set('compact', '1'); else url.searchParams.delete('compact');
  history.replaceState(null, '', url);
  $('compact-toggle').textContent = document.body.classList.contains('compact') ? 'Full view' : 'Compact view';
});
if (document.body.classList.contains('compact')) $('compact-toggle').textContent = 'Full view';
document.querySelectorAll('[data-chart]').forEach(setupChartInspection);
const setSearchInputs = () => { $('history-from').value = localInputValue(new Date(Date.now() - currentHours * 3600000)); $('history-to').value = localInputValue(new Date()); };
setSearchInputs();
document.querySelectorAll('[data-hours]').forEach(button => button.addEventListener('click', () => {
  currentHours = Number(button.dataset.hours); customRange = null; zoomStack.length = 0; $('zoom-back').disabled = true; setSearchInputs();
  $('history-search-message').textContent = '';
  document.querySelectorAll('[data-hours]').forEach(b => b.classList.toggle('selected', b === button));
  refresh();
}));
$('history-search').addEventListener('submit', event => {
  event.preventDefault();
  const from = new Date($('history-from').value).getTime(), to = new Date($('history-to').value).getTime();
  if (!Number.isFinite(from) || !Number.isFinite(to) || from >= to || to > Date.now() + 300000 || to - from > 720 * 3600000) {
    $('history-search-message').textContent = 'Choose a start before the end, within a 30-day span and no later than now.';
    return;
  }
  customRange = { from, to };
  zoomStack.length = 0; $('zoom-back').disabled = true;
  $('history-search-message').textContent = `Showing ${when(new Date(from))} to ${when(new Date(to))} across all graphs.`;
  document.querySelectorAll('[data-hours]').forEach(b => b.classList.remove('selected'));
  refresh();
});
$('history-latest').addEventListener('click', () => {
  currentHours = 24; customRange = null; zoomStack.length = 0; $('zoom-back').disabled = true; setSearchInputs(); $('history-search-message').textContent = '';
  document.querySelectorAll('[data-hours]').forEach(b => b.classList.toggle('selected', b.dataset.hours === '24'));
  refresh();
});
$('zoom-back').addEventListener('click', () => {
  const previous = zoomStack.pop();
  if (!previous) return;
  customRange = previous.customRange; currentHours = previous.currentHours;
  if (customRange) { $('history-from').value = localInputValue(new Date(customRange.from)); $('history-to').value = localInputValue(new Date(customRange.to)); }
  else setSearchInputs();
  $('zoom-back').disabled = zoomStack.length === 0;
  $('history-search-message').textContent = customRange ? `Showing ${when(new Date(customRange.from))} to ${when(new Date(customRange.to))} across all graphs.` : `Showing the latest ${currentHours} hours across all graphs.`;
  document.querySelectorAll('[data-hours]').forEach(button => button.classList.toggle('selected', !customRange && Number(button.dataset.hours) === currentHours));
  void refresh();
});
refresh(true); timer = setInterval(() => refresh(), 30000);
