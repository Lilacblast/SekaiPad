# Third-party source provenance and copyright review

Reviewed 2026-10-16 against the upstream repositories and package metadata. This is source attribution evidence, not a legal opinion.

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

These counts are a textual provenance check, not proof that code behavior is the same. Other portions of this project are **not** automatically attributed to emoacht merely because they use Windows Raw Input APIs.

The upstream author's copyright applies to the adapted portions. Original SekaiPad modifications use the project's MIT license without implying endorsement or authorship by the upstream author.

## Android Emulator Controller protocol definitions

- `src/SekaiPad/Protocol/emulator_controller.proto` begins with the upstream `Copyright (C) 2018 The Android Open Source Project` and Apache-2.0 notice.
- Full Apache-2.0 text is included in `licenses/Apache-2.0`.
- The protocol is used by the optional Google Emulator gRPC backend. It is **not required** for the MuMu+scrcpy touch transport, but is included in the current public source snapshot and must retain its provenance.
- Independently matched against Google's public `google/android-emulator-webrtc` repository, revision `ccf3c3223dc95d13c9d34ecc3be7cf8956dcb2a6`, at `proto/emulator_controller.proto`. The upstream file and SekaiPad's checked-in proto were **byte-identical**, each 81,619 bytes, SHA256 `1d62c6bcad5f06621f90ec2bf26c661ba769ccd0f1416b5314d25a68e04eee5f`.
- Pinned upstream source: https://github.com/google/android-emulator-webrtc/blob/ccf3c3223dc95d13c9d34ecc3be7cf8956dcb2a6/proto/emulator_controller.proto. The original AOSP 2018 copyright and Apache-2.0 header are preserved unchanged.

## External runtime and NuGet packages

- scrcpy Android control server is user-provided separately. No scrcpy binaries are part of the source snapshot or downloadable alpha ZIP.
- The `tools/verify-alpha-deps.ps1` gate checks exactly 6 approved NuGet dependency versions, 7 staged DLLs and corresponding license text before making a ZIP.
- NuGet package metadata has been checked for the 6 direct/transitive package licenses. Full license texts are stored in `licenses/` for Apache-2.0, BSD-3-Clause and MIT.
- Original gRPC upstream LICENSE/NOTICE text and Microsoft logging package notices were added and hash-pinned on 2026-10-18. Subsequent package upgrades or new binary dependencies require a fresh review; any general-purpose binary release also requires separate owner approval.
- A live `dotnet list package --vulnerable --include-transitive` query on 2026-10-18 found no reported vulnerable packages in the pinned graph. This is the NuGet advisory feed result at that date, not a warranty against undisclosed issues.

## Publication handling

Public source must include original copyright headers and full license text. Never relabel imported code as exclusively original SekaiPad code. Include only the reviewed allowlisted source files and a newly initialized public Git history; private workspace history must not be pushed. When updating or replacing the imported upstream code, re-check its license and provenance.

## Pinned gRPC upstream license contents (2026-10-18 review)

- The NuGet manifests for `Grpc.Net.Client`, `Grpc.Net.Common` and `Grpc.Core.Api` 2.66.0 identify gRPC-DotNet repository commit `64e87a676b6aba16945f70aed3bb1c1dbbbbbad0`. Its original `LICENSE` was fetched directly at that revision and preserved byte-for-byte in `licenses/Grpc.DotNet.LICENSE.txt` (SHA256 `cfc7749b96f63bd31c3c42b5c471bf756814053e847c10f3eb003417bc523d30`). It contains the Apache 2.0 template. The already-shipped `licenses/Apache-2.0` instead matches scrcpy's Apache text with specific Genymobile attribution in the appendix, and is **not** byte-identical to gRPC-DotNet's original license.
- Build-only NuGet package `Grpc.Tools` 2.66.0 identifies upstream `grpc/grpc` commit `13cecab1c4f45902197a9f8fe4e787eb9c4d4db1`. Its original `LICENSE` (SHA256 `590198e3f305f2c347fde64d637c65492bbef554db6c8364e149cd375e3797ee`) and `NOTICE.txt` (SHA256 `693ff28ec216d5112ac1bbfe64ef539005867d1c7bd427b57d579683293b947f`) are preserved in `licenses/Grpc.Tools.*.txt`. That upstream combined license contains additional BSD-3-Clause and MPL-2.0 text relating to gRPC upstream components. The `Grpc.Tools` compiler is a **build-time** NuGet dependency; its native binaries are excluded from the published SekaiPad ZIP.
- `Google.Protobuf` 3.29.3's locally preserved BSD license exactly matches upstream protobuf revision `v29.3` `LICENSE` (SHA256 `6e5e117324afd944dcf67f36cf329843bc1a92229a8cd9bb573d7a83130fea7d`). The pinned `emoacht` MIT and AOSP protocol origins were independently reconfirmed.
- The public source and optional binary preview preserve these upstream text files independently. This verification establishes source identity and additional upstream notice coverage; distribution review for future binary dependencies remains ongoing.
