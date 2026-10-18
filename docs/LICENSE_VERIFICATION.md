# External license verification (2026-10-16)

This audit is a **partial, source-specific inventory**, not a legal opinion or an approval to redistribute the whole project.

| Component | Current usage | License observed | Upstream evidence |
| --- | --- | --- | --- |
| emoacht/RawInput.Touchpad | Adapted HID decoding sample | MIT | https://github.com/emoacht/RawInput.Touchpad |
| Genymobile/scrcpy | Separately supplied v5.0 Android control server | Apache-2.0 | https://github.com/Genymobile/scrcpy |
| Google.Protobuf 3.29.3 | NuGet runtime package | BSD-3-Clause | https://www.nuget.org/packages/Google.Protobuf/3.29.3 |
| Grpc.Net.Client 2.66.0 | NuGet gRPC transport | Apache-2.0 | https://www.nuget.org/packages/Grpc.Net.Client/2.66.0 |
| Grpc.Tools 2.66.0 | NuGet build-time generator | Apache-2.0 | https://www.nuget.org/packages/Grpc.Tools/2.66.0 |
| Grpc.Core.Api 2.66.0 | Transitive runtime DLL | Apache-2.0 | https://www.nuget.org/packages/Grpc.Core.Api/2.66.0 |
| Grpc.Net.Common 2.66.0 | Transitive runtime DLL | Apache-2.0 | https://www.nuget.org/packages/Grpc.Net.Common/2.66.0 |
| Microsoft.Extensions.Logging.Abstractions 6.0.0 | Transitive runtime DLL | MIT | https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions/6.0.0 |
| AOSP emulator_controller.proto | Optional Google emulator interface | Apache-2.0, original header preserved | https://github.com/google/android-emulator-webrtc/blob/ccf3c3223dc95d13c9d34ecc3be7cf8956dcb2a6/proto/emulator_controller.proto |

2026-10-16 upstream source review: cloned the official `emoacht/RawInput.Touchpad` revision `38a9e68c1c42cb19bae02fe84ee88b2a4b2a8639`; upstream MIT LICENSE.txt and `licenses/RawInput.Touchpad.MIT` were byte-for-byte identical (SHA256 `f872f91a21689d046c7e05b1d787a9deeaf713d855e9241699b5789ea5b7f1d1`). Both derived C# files now include original copyright, SPDX and source revision headers. See `docs/THIRD_PARTY_PROVENANCE.md` for file-level evidence.

The AOSP protocol file was also independently byte-matched to `google/android-emulator-webrtc` at commit `ccf3c3223dc95d13c9d34ecc3be7cf8956dcb2a6`, SHA256 `1d62c6bcad5f06621f90ec2bf26c661ba769ccd0f1416b5314d25a68e04eee5f`, preserving the upstream Apache-2.0 header.

The existing checkout preserves `licenses/RawInput.Touchpad.MIT` and `THIRD_PARTY_LICENSES.md`. The MIT license in SekaiPad's repository applies to **original SekaiPad contributions**, not a relicensing of upstream imports.

**Alpha ZIP dependency gate:** `tools/verify-alpha-deps.ps1` examines the actual compiled `SekaiPad.deps.json` and every staged DLL. It accepts only the six reviewed NuGet package name+version pairs and seven identified DLLs, requires the corresponding complete license files, and rejects a new assembly or changed package version until the inventory is reviewed. Positive package build passed on 2026-10-16; negative tests independently confirmed fail-closed behavior for an unknown DLL and a synthetic `Google.Protobuf/9.99.99` dependency. This does not establish that all transitive copyright notices and binary redistribution obligations have been manually checked.

**Remaining release blockers:** review *every* imported file header; verify source-provenance and copyright notices against the checked-in source (not only package listings); inspect transitive NuGet package licenses and shipped runtime binaries; preserve original license text in any binary release, as required. Do not bundle the MuMu executable, emulator disk images, proprietary Project SEKAI data or the scrcpy server in this public snapshot.

**NuGet package notice verification (2026-10-16):** The official NuGet 6.0.0 package for `Microsoft.Extensions.Logging.Abstractions` was downloaded and matched to its NuGet V3 catalog SHA512. Its exact packaged `LICENSE.TXT` (SHA256 d7a68596ab69b06f51ca278a6545148e4269a9381c26d597c13df5d88e08cf5b) and `THIRD-PARTY-NOTICES.TXT` (SHA256 a6344bfb229bc08d1071cec400eea216e307439f961c5943ec1b56d2245acb95) are now preserved for redistribution, with canonical LF variants separately accepted. Other transitive dependency notices still require review.

**Additional exact upstream texts (2026-10-18):** The original gRPC-DotNet 2.66.0 source license is now preserved as `licenses/Grpc.DotNet.LICENSE.txt`, SHA256 `cfc7749b96f63bd31c3c42b5c471bf756814053e847c10f3eb003417bc523d30`. Build-time `Grpc.Tools` upstream combined license and notice are preserved as `licenses/Grpc.Tools.LICENSE.txt` and `licenses/Grpc.Tools.NOTICE.txt`, respectively, with pinned hashes in the provenance auditor. These include upstream BSD/MPL notices beyond the Apache 2.0 template. The gRPC-DotNet runtime license is copied into the alpha ZIP; the tools' upstream notices are included as additional transparency, without bundling the build-time binaries. Details and commit identifiers: `docs/THIRD_PARTY_PROVENANCE.md`.
