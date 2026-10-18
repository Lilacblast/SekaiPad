# Security Policy

## Supported status

SekaiPad is currently an **experimental alpha**; there is no supported stable release or security SLA.

## Reporting a vulnerability

Please use GitHub's **private vulnerability reporting** for this repository when enabled. If it is unavailable, request a private contact path from the maintainer **without posting the exploit or secrets in a public issue**.

Reports about restoring mouse input, Windows touchpad gesture registry values, low-level Windows hooks, the crash guard, ADB loopback exposure, dependencies or unsafe input injection are especially valuable.

Do not test against other people's machines, accounts, or emulators. Do not publish secrets, access tokens, private input captures or login screens.

## Default threat model

- Native code runs in the current Windows user session, with access to regular user input APIs.
- Android touch-control traffic stays on an explicitly local ADB/emulator connection.
- Disabling the Windows mouse temporarily affects external mice as well. A physical keyboard is needed for the recovery shortcuts.
- An independent guard attempts crash recovery; it does **not** guarantee recovery from an OS failure, power loss or unrelated software changing system cursor/gesture settings.
- Never run SekaiPad or its installer with elevated privileges unless a specific audited step demands it; the current design should work as an ordinary user.