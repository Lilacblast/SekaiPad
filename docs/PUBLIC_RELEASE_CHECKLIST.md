# PUBLIC RELEASE CHECKLIST (private review)

Dedicated Windows input-recovery and gameplay-latency testing is deferred to normal future play. Both remain unverified.

## Source-only experimental alpha — publication blockers

For a source-only preview, complete source, privacy, license and CI review. Keep unresolved gameplay and recovery limits visible. GitHub visibility requires explicit approval.

- [x] Inspect the 85-file source allowlist, executable behaviors, NuGet sources and imported-file headers. Preserve the original 2 adapted RawInput C# headers and AOSP proto header. Public history is a separate clean root commit.
- [x] Preserve and SHA256-check original upstream MIT, AOSP Apache-2.0, protobuf BSD, gRPC combined license/NOTICE and Microsoft logging notices.
- [x] Audit private history and a separately initialized one-commit source-only repository; retain the reviewed SHA256 manifest. The owner chooses the final public Git author identity.
- [x] Inspect the 85-file public source candidate, NuGet.Config, issue templates and code paths for developer home paths, tokens, credentials, network destinations, binary payloads and personal data. Third-party upstream NOTICE author contacts are preserved.
- [x] Require 85 exact allowlisted source paths and reject proprietary game/emulator assets, screenshots, credentials and binary files.
- [x] Exclude old experimental, login and private diagnostic work through the strict explicit source allowlist.
- [x] Verify .NET 10, pinned public NuGet dependencies, synthetic five-finger transport/restore tests and the compiled source-to-ZIP audit on private hosted Windows CI.
- [x] Verify the portable scrcpy staging helper with offline synthetic valid/invalid SHA256, duplicate and source-tree tests. The server itself remains user-supplied and outside the released ZIP.
- [x] Explain session-wide mouse suppression, emergency F10/F6 keys, build 26200 compatibility scope and deferred real-world recovery/latency measurements in README and SAFETY.
- [ ] Get explicit owner approval to publish the **new source-only repository with fresh commit history**. No automatic push in scripts.
- [ ] Create a new source-only public repository, with a fresh reviewed commit history, only after approval; preserve checksums and third-party notices. Treat binary tags/releases as a separate decision.

## Deferred — collect during normal gameplay

- [ ] During normal future play, observe F10/F6 restoration and report unexpected mouse, cursor or gesture behavior. Save forced-crash checks for a separate safe session, if ever needed.
- [ ] During normal future play, observe HOLD/SLIDE/FLICK, simultaneous fingers, continuity and perceived timing.
- [ ] Document any measured device/timing data when available; keep remaining compatibility claims scoped to evidence.

## Binary redistribution — additional safety review

Before describing a downloadable Windows executable as ready for general use, complete a safety review of recovery behavior on a supported real Windows device, or clearly limit distribution to a controlled experimental test group with an explicit manual recovery procedure. Public binary distribution requires separate owner approval. Keep CI tests synthetic and read-only with respect to global Windows input.

**This private branch is preparation only, not a public release.**
