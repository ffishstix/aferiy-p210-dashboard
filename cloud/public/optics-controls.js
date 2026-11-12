const optics = id => document.getElementById(id);
const categories = [
  ['all', 'All settings'],
  ['generator', 'Generator & AC'],
  ['battery', 'Battery & charging'],
  ['inverter', 'Inverter'],
  ['solar', 'Solar controllers'],
  ['system', 'System & schedules'],
];
const quickNames = { generator: 'Generator', inverter: 'Inverter', acPower: 'AC input', battery: 'Battery charge' };
let farmSettings = null;
let selectedCategory = 'all';
let opticsBusy = false;

function opticsMessage(text, error = false) {
  optics('optics-status').textContent = text;
  optics('optics-status').classList.toggle('error', error);
}

async function opticsRequest(url, options = {}) {
  const response = await fetch(url, { cache: 'no-store', ...options });
  const result = await response.json().catch(() => ({}));
  if (!response.ok) throw Object.assign(new Error(result.error || `Request failed (${response.status})`), { status: response.status });
  return result;
}

function opticsSignedIn(signedIn) {
  optics('optics-login').hidden = signedIn;
  optics('optics-content').hidden = !signedIn;
  optics('optics-logout').hidden = !signedIn;
}

function categoryFor(device, tab, attribute) {
  if (device.model.startsWith('FM')) return 'solar';
  if (tab.name === 'AGS' || tab.name === 'AC Input') return 'generator';
  if (tab.name === 'Battery Charging' || tab.name === 'Global Charge' || tab.name === 'HBX Mode') return 'battery';
  if (tab.name === 'Modes') return /charg|battery/i.test(attribute.label) ? 'battery' : 'inverter';
  if (device.model.includes('FX') || device.model.includes('VFX')) return 'inverter';
  return 'system';
}

function allFields() {
  const devices = (farmSettings?.devices || []).flatMap(device => device.tabs.flatMap(tab => tab.subsections.flatMap(section => section.attributes.map(attribute => ({ device, tab, section, attribute, category: categoryFor(device, tab, attribute) })))));
  const setup = (farmSettings?.setup || []).map(attribute => ({ device: { model: 'System setup', port: 'setup' }, tab: { name: 'Registration' }, section: { name: null }, attribute, category: attribute.category }));
  return [...devices, ...setup];
}

function text(tag, value, className) {
  const element = document.createElement(tag);
  element.textContent = value;
  if (className) element.className = className;
  return element;
}

function canChange() {
  return farmSettings?.online && farmSettings.pending === 0 && !opticsBusy;
}

async function sendFarmChange(payload, description, before, after) {
  if (!canChange()) return;
  if (!window.confirm(`Send ${description} change to the farm?\n\nCurrent: ${before}\nNew: ${after}\n\nOpticsRE may queue the request. Check the actual device state after sending.`)) return;
  opticsBusy = true;
  renderFarmSettings();
  opticsMessage(`Sending ${description.toLowerCase()} change to OpticsRE…`);
  try {
    const result = await opticsRequest('/api/farm-control/command', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(payload) });
    farmSettings.pending = result.pending ?? 1;
    opticsMessage(result.message || 'Request accepted. Refresh to check the farm device.');
  } catch (error) {
    if (error.status === 401) { opticsSignedIn(false); farmSettings = null; }
    opticsMessage(error.message, true);
  } finally {
    opticsBusy = false;
    if (farmSettings) renderFarmSettings();
  }
}

function renderQuick() {
  const container = optics('optics-quick');
  container.replaceChildren();
  for (const [key, label] of Object.entries(quickNames)) {
    const item = farmSettings.quick?.[key];
    if (!item) continue;
    const card = document.createElement('div');
    card.className = 'optics-quick-card';
    card.append(text('strong', label), text('small', item.status || 'Current setting'));
    const row = document.createElement('div');
    row.className = 'control-editor';
    const select = document.createElement('select');
    select.setAttribute('aria-label', `${label} control`);
    for (const option of item.options) {
      const choice = document.createElement('option');
      choice.value = String(option.value);
      choice.textContent = option.text;
      choice.disabled = option.isDisabled === true;
      select.append(choice);
    }
    select.value = String(item.current);
    select.disabled = item.disabled || !canChange();
    const button = text('button', 'Send', 'ghost');
    button.type = 'button';
    button.disabled = select.disabled;
    button.addEventListener('click', () => {
      const value = Number(select.value);
      const after = item.options.find(option => option.value === value)?.text || select.value;
      const before = item.options.find(option => option.value === item.current)?.text || String(item.current);
      sendFarmChange({ kind: 'quick', key, value, expectedCurrent: String(item.current) }, label, before, after);
    });
    row.append(select, button);
    card.append(row);
    container.append(card);
  }
}

function renderFarmSettings() {
  if (!farmSettings) return;
  optics('optics-updated').textContent = `Read ${new Date(farmSettings.readAt).toLocaleString()} · ${farmSettings.online ? 'Online' : 'Offline'}${farmSettings.pending ? ` · ${farmSettings.pending} pending` : ''}`;
  renderQuick();
  const fields = allFields();
  const search = optics('optics-search').value.trim().toLocaleLowerCase();
  const filtered = fields.filter(field => (selectedCategory === 'all' || selectedCategory === field.category)
    && `${field.attribute.label} ${field.attribute.key} ${field.device.model} ${field.device.port} ${field.tab.name} ${field.section.name || ''}`.toLocaleLowerCase().includes(search));
  optics('optics-count').textContent = `${filtered.length} of ${fields.length} editable settings`;
  const navigation = optics('optics-categories');
  navigation.replaceChildren();
  for (const [key, label] of categories) {
    const count = key === 'all' ? fields.length : fields.filter(field => field.category === key).length;
    if (!count) continue;
    const button = text('button', `${label} (${count})`);
    button.type = 'button';
    button.className = key === selectedCategory ? 'selected' : '';
    button.setAttribute('aria-pressed', String(key === selectedCategory));
    button.addEventListener('click', () => { selectedCategory = key; renderFarmSettings(); });
    navigation.append(button);
  }
  const groups = optics('optics-groups');
  groups.replaceChildren();
  if (!filtered.length) { groups.append(text('p', 'No settings match this search.', 'optics-empty')); return; }
  const grouped = new Map();
  for (const field of filtered) {
    const key = `${field.device.port}:${field.tab.name}`;
    if (!grouped.has(key)) grouped.set(key, []);
    grouped.get(key).push(field);
  }
  for (const entries of grouped.values()) {
    const first = entries[0];
    const details = document.createElement('details');
    details.className = 'optics-group';
    if (search || grouped.size <= 2) details.open = true;
    const summary = document.createElement('summary');
    summary.append(text('strong', `${first.tab.name} · ${first.device.model} · port ${first.device.port}`), text('span', `${entries.length} settings`));
    details.append(summary);
    for (const { device, attribute } of entries) {
      const row = document.createElement('div');
      row.className = 'optics-setting';
      const label = text('label', attribute.label);
      const id = `optics-${device.port}-${attribute.key}`;
      label.htmlFor = id;
      const current = text('small', `Current: ${attribute.current}${attribute.unit && attribute.unit !== 'Enum' ? ` ${attribute.unit}` : ''}`);
      const edit = document.createElement('div');
      edit.className = 'control-editor';
      let input;
      if (attribute.options?.length) {
        input = document.createElement('select');
        for (const option of attribute.options) {
          const choice = document.createElement('option');
          choice.value = option.text;
          choice.textContent = option.text;
          input.append(choice);
        }
      } else if (attribute.type === 'text') {
        input = document.createElement('input');
        input.type = 'text';
        input.maxLength = 80;
      } else {
        input = document.createElement('input');
        input.type = 'number';
        input.min = String(attribute.range.min);
        input.max = String(attribute.range.max);
        input.step = String(attribute.range.increment || 'any');
        input.title = `Allowed: ${attribute.range.min} to ${attribute.range.max}${attribute.unit ? ` ${attribute.unit}` : ''}; step ${attribute.range.increment}`;
      }
      input.id = id;
      input.value = String(attribute.current);
      input.disabled = !canChange();
      const button = text('button', 'Save', 'ghost');
      button.type = 'button';
      button.disabled = !canChange();
      button.addEventListener('click', () => {
        if (!input.reportValidity()) return;
        const payload = { kind: device.port === 'setup' ? 'system' : 'setting', key: attribute.key, expectedCurrent: String(attribute.current), value: input.value };
        if (device.port !== 'setup') payload.port = device.port;
        sendFarmChange(payload, `${device.model} ${attribute.label}`, String(attribute.current), input.value);
      });
      edit.append(input, button);
      row.append(label, current, edit);
      details.append(row);
    }
    groups.append(details);
  }
}

async function refreshFarmSettings() {
  optics('optics-refresh').disabled = true;
  opticsMessage('Reading current farm settings…');
  try {
    farmSettings = await opticsRequest('/api/farm-control/settings');
    opticsSignedIn(true);
    renderFarmSettings();
    opticsMessage(farmSettings.online ? (farmSettings.pending ? 'OpticsRE has pending changes. Refresh before sending another.' : 'Settings are ready. Review each change before sending.') : 'The farm gateway is offline; settings cannot be changed.', !farmSettings.online);
  } catch (error) {
    if (error.status === 401) { opticsSignedIn(false); farmSettings = null; opticsMessage('Sign in to view farm controls.'); }
    else opticsMessage(error.message, true);
  } finally { optics('optics-refresh').disabled = false; }
}

optics('optics-login').addEventListener('submit', async event => {
  event.preventDefault();
  try {
    await opticsRequest('/api/farm-control/login', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ password: optics('optics-password').value }) });
    optics('optics-password').value = '';
    await refreshFarmSettings();
  } catch (error) { opticsMessage(error.message, true); }
});
optics('optics-logout').addEventListener('click', async () => {
  await opticsRequest('/api/farm-control/logout', { method: 'POST' }).catch(() => {});
  farmSettings = null;
  opticsSignedIn(false);
  opticsMessage('Signed out of farm controls.');
});
optics('optics-refresh').addEventListener('click', refreshFarmSettings);
optics('optics-search').addEventListener('input', renderFarmSettings);
refreshFarmSettings();
