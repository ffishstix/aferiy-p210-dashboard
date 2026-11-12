CREATE TABLE IF NOT EXISTS telemetry (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  device_id TEXT NOT NULL,
  sample_id TEXT NOT NULL,
  recorded_at TEXT NOT NULL,
  received_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
  charge_percent REAL,
  input_w REAL,
  output_w REAL,
  ac_input_w REAL,
  dc_input_w REAL,
  remaining_minutes REAL,
  grid_present INTEGER,
  discharging INTEGER,
  fault INTEGER,
  raw_json TEXT NOT NULL,
  UNIQUE(device_id, sample_id)
);
CREATE INDEX IF NOT EXISTS telemetry_device_time ON telemetry(device_id, recorded_at DESC);
CREATE TABLE IF NOT EXISTS farm_telemetry (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  source_id TEXT NOT NULL,
  sample_id TEXT NOT NULL,
  recorded_at TEXT NOT NULL,
  received_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
  solar_w REAL,
  generator_w REAL,
  generator_running INTEGER,
  ac_source TEXT,
  main_charge_percent REAL,
  main_battery_w REAL,
  main_battery_v REAL,
  load_w REAL,
  UNIQUE(source_id, sample_id)
);
CREATE INDEX IF NOT EXISTS farm_telemetry_time ON farm_telemetry(recorded_at DESC);
CREATE TABLE IF NOT EXISTS login_attempts (
  ip_hash TEXT PRIMARY KEY,
  window_start INTEGER NOT NULL,
  failures INTEGER NOT NULL
);

