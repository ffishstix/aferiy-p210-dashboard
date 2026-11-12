# AFERIY P210 cloud monitor

Public battery and farm status and history, with sign-in for controls: [battery.fishstix.uk](https://battery.fishstix.uk). This repository contains the Pages dashboard and Windows app. The [collector and cloud Worker](https://github.com/ffishstix/aferiy-p210-monitor) feed the shared Cloudflare D1 database.

The Cloudflare Pages site, D1 history database, and Windows tray program are live. A scheduled Cloudflare Worker uses the P210's existing BrightEMS cloud connection and saves a reading each minute. The owner compared live input/output readings with the phone app. Real low-battery alerts and every advanced field still need validation. The Local MQTT token is not used for this cloud login.

See [PROJECT_PLAN.md](PROJECT_PLAN.md) for the diagram and remaining steps. No account credentials are committed to this repository.

Farm power is collected separately from OpticsRE into `farm_telemetry` and shown on the same website and Windows app. The farm system has a MATE3s, VFX3048E inverter and two FM80 charge controllers. It has no FLEXnet DC monitor, so a main-battery charge percentage is unavailable. The tray app gives manual AFERIY input-plug reminders based on fresh farm and P210 readings.

Third-party code and protocol references are documented in
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Licensing

The original project code is dual-licensed under
[AGPLv3-or-later](https://www.gnu.org/licenses/agpl-3.0.html) or a separate
commercial license. See [COMMERCIAL-LICENSE.md](COMMERCIAL-LICENSE.md) for the
commercial licensing path. Third-party components retain their own licenses;
see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
