const control = id => document.getElementById(id);
const groups = [
  { title: 'Outputs', fields: [
    { key: 'acOutput', label: 'AC output', kind: 'toggle', help: 'Power for the AC sockets. Turning this off cuts power to anything plugged in.' },
    { key: 'dcOutput', label: 'DC output', kind: 'toggle', help: 'Power for the DC sockets and car socket.' },
    { key: 'usbOutput', label: 'USB output', kind: 'toggle', help: 'Power for the USB charging ports.' },
    { key: 'ledMode', label: 'LED light', kind: 'select', options: [[0, 'Off'], [1, 'On'], [2, 'SOS'], [3, 'Flash']], help: 'Sets the built-in light to steady, SOS, flashing, or off.' },
  ] },
  { title: 'Charging', fields: [
    { key: 'acChargingRate', label: 'AC charging power', kind: 'select', options: [[1, '300 W'], [2, '500 W'], [3, '700 W'], [4, '900 W'], [5, '1100 W']], help: 'Sets the target power drawn from a wall socket. Actual charging power may vary.' },
    { key: 'dcInputType', label: 'DC input mode', kind: 'select', options: [[0, 'Solar (MPPT)'], [1, 'DC source']], help: 'Solar is for panels. DC source is for a compatible external power supply.' },
    { key: 'dcInputCurrent', label: 'DC charging current', kind: 'number', min: 1, max: 20, unit: 'A', help: 'Limits how much current the solar or DC input can draw, in amps.' },
    { key: 'chargeLimit', label: 'Charge limit', kind: 'number', min: 60, max: 100, unit: '%', help: 'Upper battery percentage for AC charging in UPS mode.' },
    { key: 'dischargeLimit', label: 'Discharge limit', kind: 'number', min: 0, max: 50, unit: '%', help: 'Battery percentage at which discharging stops.' },
    { key: 'silentCharging', label: 'Silent charging', kind: 'toggle', help: 'Charges more slowly from a wall socket to reduce fan noise. The fan may still run.' },
  ] },
  { title: 'Sound and standby', fields: [
    { key: 'buzzer', label: 'Button sound', kind: 'toggle', help: 'Turns the beep from the battery buttons on or off.' },
    { key: 'usbStandbyTime', label: 'USB standby', kind: 'select', options: [[0, 'Never'], [3, '3 min'], [5, '5 min'], [10, '10 min'], [30, '30 min']], help: 'Turns USB output off after this long with no load. Never disables the timer.' },
    { key: 'acStandbyTime', label: 'AC standby', kind: 'select', options: [[0, 'Never'], [480, '8 h'], [960, '16 h'], [1440, '24 h']], help: 'Turns AC output off after this long with no load. Never disables the timer.' },
    { key: 'dcStandbyTime', label: 'DC standby', kind: 'select', options: [[0, 'Never'], [480, '8 h'], [960, '16 h'], [1440, '24 h']], help: 'Turns DC output off after this long with no load. Never disables the timer.' },
    { key: 'screenRestTime', label: 'Screen rest', kind: 'select', options: [[0, 'Never'], [180, '3 min'], [300, '5 min'], [600, '10 min'], [1800, '30 min']], help: 'Dims the battery screen after this long. It does not turn off the battery.' },
    { key: 'sleepTime', label: 'Battery idle shutoff', kind: 'select', options: [[5, '5 min'], [10, '10 min'], [30, '30 min'], [480, '8 h']], help: 'Turns the battery off after this long when it is idle.' },
  ] },
];
let currentSettings = null;
let busy = false;

function message(text, error = false) {
  const element = control('controls-message');
  element.textContent = text;
  element.classList.toggle('error', error);
}

async function request(url, options = {}) {
  const response = await fetch(url, { cache: 'no-store', ...options });
  const result = await response.json().catch(() => ({}));
  if (!response.ok) throw Object.assign(new Error(result.error || `Request failed (${response.status})`), { status: response.status });
  return result;
}

function setSignedIn(signedIn) {
  control('controls-login').hidden = signedIn;
  control('controls-session').hidden = !signedIn;
  control('controls-logout').hidden = !signedIn;
}

async function send(setting, value, label) {
  if (busy) return;
  if (setting === 'acOutput' && value === false && !window.confirm('Turn off AC output? Anything plugged into the AC sockets will lose power.')) return;
  if (setting === 'dcOutput' && value === false && !window.confirm('Turn off DC output? Anything plugged into the DC sockets will lose power.')) return;
  busy = true;
  control('controls-fields').querySelectorAll('button,input,select').forEach(element => element.disabled = true);
  message(`Sending ${label.toLowerCase()} setting to the battery…`);
  try {
    const result = await request('/api/control/command', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ setting, value }) });
    if (!result.confirmed) throw new Error('The command was sent, but the new state could not be confirmed. Check BrightEMS before trying again.');
    currentSettings[setting] = value;
    renderSettings();
    message(`${label} confirmed on the battery. Saved readings may take a minute to update.`);
  } catch (error) {
    if (error.status === 401) setSignedIn(false);
    message(error.message, true);
  } finally {
    busy = false;
    control('controls-fields').querySelectorAll('button,input,select').forEach(element => element.disabled = false);
  }
}

function renderSettings() {
  const container = control('controls-fields');
  container.replaceChildren();
  for (const group of groups) {
    const section = document.createElement('section');
    section.className = 'controls-group';
    const heading = document.createElement('h3');
    heading.textContent = group.title;
    section.append(heading);
    for (const field of group.fields) {
      const row = document.createElement('div');
      row.className = 'control-row';
      const label = document.createElement('label');
      label.textContent = field.label;
      const id = `setting-${field.key}`;
      label.htmlFor = id;
      const labelWrap = document.createElement('div');
      labelWrap.className = 'control-label';
      labelWrap.append(label);
      if (field.help) {
        const help = document.createElement('button');
        help.type = 'button';
        help.className = 'control-help';
        help.textContent = '?';
        help.setAttribute('aria-label', `${field.label}: ${field.help}`);
        const tooltip = document.createElement('span');
        tooltip.className = 'control-tooltip';
        tooltip.setAttribute('aria-hidden', 'true');
        tooltip.textContent = field.help;
        help.append(tooltip);
        labelWrap.append(help);
      }
      const editor = document.createElement('div');
      editor.className = 'control-editor';
      const current = currentSettings[field.key];
      if (field.kind === 'toggle') {
        const button = document.createElement('button');
        button.id = id;
        button.type = 'button';
        button.className = `control-toggle ${current ? 'on' : ''}`;
        button.textContent = current == null ? 'Unavailable' : current ? 'On' : 'Off';
        button.disabled = current == null;
        button.setAttribute('aria-label', `${field.label}: ${button.textContent}. Activate to change.`);
        button.addEventListener('click', () => send(field.key, !current, field.label));
        editor.append(button);
      } else {
        let input;
        if (field.kind === 'select') {
          input = document.createElement('select');
          for (const [value, text] of field.options) {
            const option = document.createElement('option');
            option.value = String(value);
            option.textContent = text;
            input.append(option);
          }
          if (current != null && !field.options.some(([value]) => value === current)) {
            const option = document.createElement('option');
            option.value = String(current);
            option.textContent = `${current} (current)`;
            input.append(option);
          }
        } else {
          input = document.createElement('input');
          input.type = 'number'; input.min = String(field.min); input.max = String(field.key === 'dcInputCurrent' ? Math.min(field.max, currentSettings.dcInputMaxCurrent || field.max) : field.max); input.step = '1';
          if (field.unit) input.setAttribute('aria-description', field.unit);
        }
        input.id = id;
        if (current != null) input.value = String(current);
        input.disabled = current == null;
        const save = document.createElement('button');
        save.type = 'button'; save.className = 'ghost'; save.textContent = 'Save'; save.disabled = current == null;
        save.addEventListener('click', () => {
          const value = Number(input.value);
          if (!Number.isInteger(value) || (input.type === 'number' && (value < Number(input.min) || value > Number(input.max)))) {
            message(`Enter a valid value for ${field.label.toLowerCase()}.`, true);
            return;
          }
          send(field.key, value, field.label);
        });
        editor.append(input, save);
      }
      row.append(labelWrap, editor);
      section.append(row);
    }
    container.append(section);
  }
}

async function refreshSettings() {
  try {
    const result = await request('/api/control/state');
    setSignedIn(true);
    control('controls-updated').textContent = result.receivedAt ? `Settings read ${new Date(result.receivedAt).toLocaleString()}` : 'No settings read';
    if (!result.available || !result.settings) {
      control('controls-fields').replaceChildren();
      message(result.reason || 'Battery settings are unavailable.', true);
      return;
    }
    currentSettings = result.settings;
    renderSettings();
    message('Settings are ready. Changes require a fresh device response.');
  } catch (error) {
    if (error.status === 401) { setSignedIn(false); message('Sign in to use battery controls.'); }
    else message(error.message, true);
  }
}

control('controls-login').addEventListener('submit', async event => {
  event.preventDefault();
  const password = control('controls-password').value;
  try {
    await request('/api/control/login', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ password }) });
    control('controls-password').value = '';
    await refreshSettings();
  } catch (error) { message(error.message, true); }
});
control('controls-logout').addEventListener('click', async () => {
  await request('/api/control/logout', { method: 'POST' }).catch(() => {});
  currentSettings = null;
  control('controls-fields').replaceChildren();
  setSignedIn(false);
  message('Signed out of battery controls.');
});
control('controls-refresh').addEventListener('click', refreshSettings);
refreshSettings();
