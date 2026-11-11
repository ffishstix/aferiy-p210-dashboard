# Cloudflare Pages dashboard

Deployed at [aferiy-p210-monitor.pages.dev](https://aferiy-p210-monitor.pages.dev). Status and history remain public and read-only. The status API returns only the measurements shown on the page; device identifiers and raw register dumps remain in D1. The controls section requires the former dashboard password. Its signed cookie lasts one day and is scoped to `/api/control`. The separate [collector repository](https://github.com/ffishstix/aferiy-p210-collector) feeds this database through the BrightEMS cloud connection. The existing `/api/ingest` endpoint still requires its separate secret for an authenticated local fallback.

The Pages secrets `DASHBOARD_PASSWORD`, `SESSION_SECRET`, and `INGEST_SECRET` must remain private. The Pages control endpoint checks the session, request origin, and fresh settings before calling the cloud Worker through a service binding. The Worker validates the requested setting and value again, requires its own fresh settings read, then confirms the result with a register readback. A write with no confirmed readback is reported as uncertain; check BrightEMS before retrying. Never commit credentials.

From `cloud/`, run `npm ci` and `npm test`. A push to GitHub `main` triggers the Pages test/deploy workflow. The D1 database ID is in `wrangler.jsonc`; secrets are set in Cloudflare, outside the repository.
