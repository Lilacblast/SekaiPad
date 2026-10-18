# Third-party source provenance and copyright review

Upstream code and package metadata were checked on 2026-10-16 and 2026-10-18.

## Windows Precision Touchpad Raw Input

- Original project: `emoacht/RawInput.Touchpad`.
- Official upstream URL: https://github.com/emoacht/RawInput.Touchpad
- Source revision inspected: `38a9e68c1c42cb19bae02fe84ee88b2a4b2a8639` (2021-06-17).
- License: MIT. Exact upstream `LICENSE.txt` SHA256: `f872f91a21689d046c7e05b1d787a9deeaf713d855e9241699b5789ea5b7f1d1`.
- The bytes of `licenses/RawInput.Touchpad.MIT` have the **same SHA256** as upstream (not merely equivalent license wording).
- The originally-derived source files now carry per-file SPDX/copyright/source-revision headers, in addition to the repository notice.

| SekaiPad file | Original upstream file | Comparison |
| --- | --- | --- |
| `src/SekaiPad/Input/TouchpadContact.cs` | `Source/RawInput.Touchpad/TouchpadContact.cs` | 54 aligned unchanged lines out of 55 upstream lines, before accounting for the new attribution header. Primarily a nullable annotation change. |
| `src/SekaiPad/Input/TouchpadHelper.cs` | `Source/RawInput.Touchpad/TouchpadHelper.cs` | 322 unchanged aligned lines out of 481 upstream lines, before accounting for the new attribution header. Raw HID frame parsing/validation and app-specific reporting have been substantially modified. |

The line comparisons identify source ancestry; attribution is scoped to the two adapted input files.

emoacht retains copyright in the adapted code. Original SekaiPad changes use the project MIT license.

## Android Emulator Controller protocol definitions

- `src/SekaiPad/Protocol/emulator_controller.proto` begins with the upstream `Copyright (C) 2018 The Android Open Source Project` and Apache-2.0 notice.
- Full Apache-2.0 text is included in `licenses/Apache-2.0`.
- The Google Emulator gRPC backend uses this protocol; MuMu uses the scrcpy transport. The source retains the original AOSP header.
- Independently matched against Google's public `google/android-emulator-webrtc` repository, revision `ccf3c3223dc95d13c9d34ecc3be7cf8956dcb2a6`, at `proto/emulator_controller.proto`. The upstream file and SekaiPad's checked-in proto were **byte-identical**, each 81,619 bytes, SHA256 `1d62c6bcad5f06621f90ec2bf26c661ba769ccd0f1416b5314d25a68e04eee5f`.
- Pinned upstream source: https://github.com/google/android-emulator-webrtc/blob/ccf3c3223dc95d13c9d34ecc3be7cf8956dcb2a6/proto/emulator_controller.proto. The original AOSP 2018 copyright and Apache-2.0 header are preserved unchanged.

## External runtime and NuGet packages

- The scrcpy server is installed separately from SekaiPad.
- The `tools/verify-alpha-deps.ps1` gate checks exactly 6 approved NuGet dependency versions, 7 staged DLLs and corresponding license text before making a ZIP.
- NuGet package metadata has been checked for the 6 direct/transitive package licenses. Full license texts are stored in `licenses/` for Apache-2.0, BSD-3-Clause and MIT.
- Original gRPC upstream LICENSE/NOTICE text and Microsoft logging package notices were added and hash-pinned on 2026-10-18. Subsequent package upgrades or new binary dependencies require a fresh review; any general-purpose binary release also requires separate owner approval.
- A NuGet advisory query on 2026-10-18 reported zero known vulnerabilities in the pinned package graph.

## Pinned gRPC upstream license contents (2026-10-18 review)

- The NuGet manifests for `Grpc.Net.Client`, `Grpc.Net.Common` and `Grpc.Core.Api` 2.66.0 identify gRPC-DotNet repository commit `64e87a676b6aba16945f70aed3bb1c1dbbbbbad0`. Its original `LICENSE` was fetched directly at that revision and preserved byte-for-byte in `licenses/Grpc.DotNet.LICENSE.txt` (SHA256 `cfc7749b96f63bd31c3c42b5c471bf756814053e847c10f3eb003417bc523d30`). It contains the Apache 2.0 template. The already-shipped `licenses/Apache-2.0` instead matches scrcpy's Apache text with specific Genymobile attribution in the appendix, and is **not** byte-identical to gRPC-DotNet's original license.
- Build-only NuGet package `Grpc.Tools` 2.66.0 identifies upstream `grpc/grpc` commit `13cecab1c4f45902197a9f8fe4e787eb9c4d4db1`. Its original `LICENSE` (SHA256 `590198e3f305f2c347fde64d637c65492bbef554db6c8364e149cd375e3797ee`) and `NOTICE.txt` (SHA256 `693ff28ec216d5112ac1bbfe64ef539005867d1c7bd427b57d579683293b947f`) are preserved in `licenses/Grpc.Tools.*.txt`. That upstream combined license contains additional BSD-3-Clause and MPL-2.0 text relating to gRPC upstream components. The `Grpc.Tools` compiler is a **build-time** NuGet dependency; its native binaries are excluded from the published SekaiPad ZIP.
- `Google.Protobuf` 3.29.3's locally preserved BSD license exactly matches upstream protobuf revision `v29.3` `LICENSE` (SHA256 `6e5e117324afd944dcf67f36cf329843bc1a92229a8cd9bb573d7a83130fea7d`). The pinned `emoacht` MIT and AOSP protocol origins were independently reconfirmed.
- Review license texts and package notices again when dependencies change.
