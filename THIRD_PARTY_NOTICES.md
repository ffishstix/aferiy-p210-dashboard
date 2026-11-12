# Third-party notices

## This project

Unless a file or directory says otherwise, the original project code is
dual-licensed under AGPLv3-or-later or a separate commercial license. See
`LICENSE` and `COMMERCIAL-LICENSE.md`.

## Sydpower / Fossibot integration

The files in `collector/sydpower/` are derived from the `sydpower` module in
[`iamslan/ha-fossibot`](https://github.com/iamslan/ha-fossibot), Copyright (c)
2025 iamslan. The comparison used upstream commit
`ae885a881f6cfdbad43eac37522b4fe199158ebb`.

That project is distributed under the MIT License. The applicable license text
is retained in `collector/SYDPOWER_LICENSE.txt`. This repository's version
adds the input-telemetry timestamp tracking in
`collector/sydpower/mqtt_client.py`; the remaining module files were compared
against the upstream module at the time this notice was added.

## Fossibot cloud protocol reference

The Cloudflare Worker implementation in `worker/` was written as an
independent implementation using the public protocol behavior documented by
[`ldiogoz/fossibot-ha`](https://github.com/ldiogoz/fossibot-ha) as a reference.
No source files from that repository are redistributed here. The upstream
repository did not include a license file when checked on 11 November 2026,
at commit `d700d22c83168b0dc47fbc431e19ed270ecb8e6f`, so this project does not
treat it as a code dependency.
