# Compatibility

**Reference setup:** Windows 11 25H2 build 26200, MuMu Android 15, Project SEKAI and a Windows Precision Touchpad. Windows gesture suppression uses a build-specific Shell interface; each additional Windows version requires testing. Rhythm mode also blocks external mice.

## Touchpad contacts

An MSI Precision Touchpad (VID 04f3, PID 31fd) reports **5 simultaneous contacts** through its HID Contact Count Maximum Feature Report (report ID 2, usage 0x55). The four-bit field encodes up to 15; this device's contact capacity is 5.

## Reported gameplay

On 2026-10-16, the primary tester reported multi-finger play on a Project SEKAI **APPEND** chart. Sustained HOLD/SLIDE/FLICK reliability, precise finger usage and note-judgment latency remain to be measured.

## Observations during normal play

- Check HOLD/SLIDE/FLICK and multi-contact continuity.
- Check F10/F6 recovery and Windows three-/four-finger gesture suppression.
- Compare HID and scrcpy diagnostics with actual in-game judgments.
- Record device, Windows build, emulator version and test conditions when reporting timing or compatibility.

Share sanitized logs and results.
