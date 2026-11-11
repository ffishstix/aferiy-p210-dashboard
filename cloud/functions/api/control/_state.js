const word = (registers, index) => Number.isInteger(registers?.[index]) && registers[index] >= 0 && registers[index] <= 65535 ? registers[index] : null;
const low = value => value == null ? null : value & 255;
const state = (registers, index) => {
  const value = word(registers, index);
  return value == null ? null : value > 0;
};

// Cloud register layout used by BrightEMS. It differs from the local V0 layout.
export function controlState(sample) {
  if (!sample) return { available: false, reason: 'Waiting for a device reading', settings: null };
  let raw;
  try { raw = JSON.parse(sample.raw_json); } catch { raw = null; }
  const holding = Array.isArray(raw?.holdingRegisters) ? raw.holdingRegisters : null;
  const input = Array.isArray(raw?.inputRegisters) ? raw.inputRegisters : null;
  const fresh = Number.isFinite(Date.parse(sample.received_at)) && Date.now() - Date.parse(sample.received_at) <= 120000;
  const complete = holding?.length >= 85 && input?.length >= 79;
  const plausible = complete && [24, 25, 26].every(index => [0, 1].includes(word(holding, index)))
    && [0, 1, 2, 3].includes(word(holding, 27))
    && word(holding, 66) != null && word(holding, 66) <= 500
    && word(holding, 67) != null && word(holding, 67) >= 600 && word(holding, 67) <= 1000;
  const available = Boolean(fresh && complete && plausible);
  const reason = !fresh ? 'The last device reading is stale' : !complete ? 'Settings registers are unavailable' : !plausible ? 'Settings values do not match the supported device profile' : null;
  return {
    available,
    reason,
    receivedAt: sample.received_at,
    diagnostic: { holdingCount: holding?.length ?? 0, inputCount: input?.length ?? 0, holdingRead: raw?.holdingRead ?? null },
    settings: complete ? {
      usbOutput: state(holding, 24), dcOutput: state(holding, 25), acOutput: state(holding, 26),
      ledMode: word(holding, 27),
      acChargingRate: word(holding, 13), dcInputType: low(word(holding, 15)),
      dcInputMaxCurrent: low(word(holding, 17)), dcInputCurrent: low(word(holding, 20)),
      buzzer: state(holding, 56), silentCharging: state(holding, 57),
      usbStandbyTime: word(holding, 59), acStandbyTime: word(holding, 60), dcStandbyTime: word(holding, 61),
      screenRestTime: word(holding, 62),
      dischargeLimit: word(holding, 66) == null ? null : word(holding, 66) / 10,
      chargeLimit: word(holding, 67) == null ? null : word(holding, 67) / 10,
      sleepTime: word(holding, 68),
    } : null,
  };
}
