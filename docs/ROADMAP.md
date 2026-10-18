# Roadmap

These are proposed milestones, not completed version claims. Gameplay and Windows input observations will be collected during normal future use.

## v0.1.0-alpha — public source preview

- [x] Core README, safety, license, attribution and contributor docs drafted.
- [x] Offline Windows CI, initial issue templates and publication audit prepared on private working branch.
- [x] Inspect the 85-file allowlisted source tree and its upstream imports; verify original copyright, pinned licenses, path/token scans and one-commit fresh-history rehearsal.
- [x] Prepare a locally audited clean source-only export and Windows build with a single fresh-history rehearsal.
- [x] Verify the integrated source on Windows 11 private GitHub Actions, including .NET 10, synthetic input safety and actual built ZIP auditing.
- [ ] Obtain final maintainer acceptance of the source/attribution/privacy review, including approval of the public-facing author identity.
- [ ] Owner explicitly approves public visibility; publish only a fresh history.

## v0.2.0-alpha — physical gameplay reliability

- [x] Read the device HID `Contact Count Maximum` Feature Report: this MSI touchpad reports 5 simultaneous contacts (read-only capability probe on 2026-10-16).
- [ ] Collect physical 1–5 finger, HOLD, SLIDE, FLICK and timing observations during ordinary play.
- [ ] Verify UI overlay accuracy at various MuMu window sizes and DPIs.
- [ ] Reproduce Android event acknowledgment separately from raw HID and transport counters.

## v0.5.0-beta — portable/safe deployment

- [ ] Remove all private SDK/local filesystem assumptions.
- [ ] Validate mouse/gesture recovery through normal-use observations; reserve abnormal-exit checks for a separate safe opportunity.
- [ ] Publish independent checksums and verify a framework-dependent ZIP on a clean Windows test user.
- [ ] Validate at least one additional Precision Touchpad and emulator setup.

## v1.0.0 — stability

- [ ] Compatibility matrix and repeatable timing/jitter statistics.
- [ ] Clean signed installer or updater, long-term maintenance and localized help.

Out of scope: game resource redistribution, bypassing anti-cheat/authentication, collecting credentials and routing live gameplay over a remote server.
