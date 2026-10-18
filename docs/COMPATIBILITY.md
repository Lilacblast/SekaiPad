# Hardware and gameplay compatibility

SekaiPad is an experimental input bridge. The initial tested setup uses a Windows Precision Touchpad with MuMu Android 15 and Project SEKAI.

## Reported real gameplay

- **2026-10-16, Windows laptop built-in Precision Touchpad + MuMu Android 15 + Project SEKAI:** the primary tester reports multi-finger play on an **APPEND** chart. This is a play report, with separate timing and reliability measurements pending.
- **Pending measurements:** chart/result, AP/FC status, number of fingers used, HOLD/SLIDE/FLICK drop rate, notes per second, input-to-judgment latency and long-run stability.
- **Verified hardware capacity:** the MSI touchpad (VID 04f3, PID 31fd) returned **5 concurrent contacts** through a read-only HID GET_FEATURE query (report ID 2, usage 0x55). Its four-bit encoding permits a value of 15, while the actual simultaneous-contact limit is 5. Other models may differ. Game-play reliability is evaluated separately.

## Current first-class environment

Windows 11 25H2 build 26200, MuMu Android 15, Project SEKAI and an actual Precision Touchpad. Compatibility verification currently covers this Windows build; the undocumented gesture cache interface requires individual testing on other builds. A separate external mouse is also blocked during active rhythm mode.

## Further observations during normal play

Input recovery and timing are being evaluated through ordinary play sessions; long-run stability and crash recovery remain unverified.

- Measure input-packet timestamps and actual Android game judgments separately, using in-game judgments to verify event acceptance.
- Collect a reproducible non-sensitive sample for 1 through hardware-supported multi-finger contacts, plus a long HOLD, independent SLIDE and FLICK.
- Verify 3-/4-finger Windows task switching never interrupts gameplay, and F10/F6 cleanly restores all mouse and gesture behavior.
- Repeat on another laptop model and across simulator window sizes / DPI configurations.

Report benchmark numbers with the method, device model, Windows build, sample size and latency definition. Share anonymized evidence, obtaining permission for any account-related details or raw screen captures.
