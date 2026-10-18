# Changelog

## 0.1.0-alpha — 2026-10-18 (source preview)

- Decode independent Windows Precision Touchpad contacts and map them to Android touches via local scrcpy v5 control transport; expose mapped-contact diagnostics.
- Provide absolute screen and optional lane mapping, multi-contact note input and F6/F10 keyboard recovery paths. Sustained HOLD/SLIDE/FLICK reliability and real crash restoration remain for normal-use observation.
- Run .NET 10 Windows CI with synthetic five-contact/transport, gesture-backup, cursor policy and dummy-watchdog tests.
- Audit exact allowlisted source, private-history leakage, approved DLLs, pinned NuGet packages and complete upstream license/NOTICE texts.
- Build a framework-dependent Windows preview ZIP locally; MuMu and scrcpy are installed separately.

Windows binary distribution is planned separately from the published source.
