# Security Policy

## Supported status

SekaiPad is an experimental source alpha.

## Reporting a vulnerability

Report vulnerabilities through GitHub **private vulnerability reporting**. If it is unavailable, ask the maintainer for a private channel.

Reports about restoring mouse input, Windows touchpad gesture registry values, low-level Windows hooks, the crash guard, ADB loopback exposure, dependencies or unsafe input injection are especially valuable.

Test only devices and accounts you control. Keep tokens, login screens and input captures private.

## Default threat model

- Native code runs in the current Windows user session, with access to regular user input APIs.
- Android touch-control traffic stays on an explicitly local ADB/emulator connection.
- Disabling the Windows mouse temporarily affects external mice as well. A physical keyboard is needed for the recovery shortcuts.
- An independent guard attempts crash recovery. Power loss and other programs changing cursor/gesture settings may still require manual restoration.
- Run SekaiPad as a standard Windows user.