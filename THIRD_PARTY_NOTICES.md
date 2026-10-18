# Third-party components and license inventory

This file records external projects used or referenced by SekaiPad. They **are not relicensed** under SekaiPad's MIT license. Verify each upstream license file and preserve its notices whenever redistributing the respective code or binary.

| Component | How it is used | License / upstream |
| --- | --- | --- |
| [emoacht/RawInput.Touchpad](https://github.com/emoacht/RawInput.Touchpad) | Basis for C# Windows Precision Touchpad Raw Input decoding, adapted in project source | MIT |
| [Genymobile/scrcpy](https://github.com/Genymobile/scrcpy) | External Android control server/protocol (tested with v5.0) | Apache-2.0 |
| [Android Open Source Project](https://source.android.com/) | `emulator_controller.proto` for the optional legacy emulator interface, byte-matched to Google's [Android Emulator WebRTC proto](https://github.com/google/android-emulator-webrtc/blob/ccf3c3223dc95d13c9d34ecc3be7cf8956dcb2a6/proto/emulator_controller.proto) | Apache-2.0 (2018 AOSP original header retained) |
| [Google.Protobuf](https://www.nuget.org/packages/Google.Protobuf/3.29.3) | NuGet runtime serialization dependency | BSD-3-Clause |
| [Grpc.Net.Client](https://www.nuget.org/packages/Grpc.Net.Client/2.66.0) | NuGet gRPC client dependency | Apache-2.0 |
| [Grpc.Tools](https://www.nuget.org/packages/Grpc.Tools/2.66.0) | Build-time protobuf/gRPC generator | Apache-2.0 |
| [Grpc.Core.Api](https://www.nuget.org/packages/Grpc.Core.Api/2.66.0) | Transitive runtime API DLL included in alpha preview | Apache-2.0 |
| [Grpc.Net.Common](https://www.nuget.org/packages/Grpc.Net.Common/2.66.0) | Transitive runtime common DLL included in alpha preview | Apache-2.0 |
| [Microsoft.Extensions.Logging.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions/6.0.0) | Transitive runtime DLL included in alpha preview | MIT |

**Redistribution rules:**

- The proposed alpha source/release packages **do not bundle** Project SEKAI APKs, split APKs, assets, emulator images, accounts, login data or third-party scrcpy server files.
- Users obtain scrcpy from its original publisher and provide the control-only server separately. Do not mirror modified upstream binaries without reviewing notices and source obligations.
- The original 2021 upstream copyright is retained in both adapted `src/SekaiPad/Input/TouchpadHelper.cs` and `src/SekaiPad/Input/TouchpadContact.cs`, alongside their `SPDX-License-Identifier: MIT` headers. Original source revision, SHA256-matched MIT license and line-by-line provenance evidence are described in `docs/THIRD_PARTY_PROVENANCE.md`.
- Keep the project's pre-existing `THIRD_PARTY_LICENSES.md` (if present), including all original attribution / license text, when merging this preparation bundle.
- The source preview and local alpha ZIP include pinned upstream texts in `licenses/`: `RawInput.Touchpad.MIT`, `Apache-2.0` (scrcpy/AOSP), `Google.Protobuf.BSD-3-Clause`, `Grpc.DotNet.LICENSE.txt`, `Grpc.Tools.LICENSE.txt`, `Grpc.Tools.NOTICE.txt`, `Microsoft.DotNet.MIT` and both `Microsoft.Extensions.Logging.Abstractions.*.txt` notices. Full original-package provenance and SHA256 gates are documented in `docs/THIRD_PARTY_PROVENANCE.md`.
- The reviewed ZIP includes five runtime NuGet dependencies: Google.Protobuf, Grpc.Core.Api, Grpc.Net.Client, Grpc.Net.Common and Microsoft.Extensions.Logging.Abstractions. `Grpc.Tools` is build-only. Source licenses, individual package metadata, and bundled NOTICE text have been checked against pinned upstream revisions. A general-purpose binary release remains a separate owner decision and safety review.
**Provenance evidence:** Original Microsoft logging package `LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT` were verified against the NuGet V3 catalog SHA512 (2026-10-16). The gRPC-DotNet and gRPC native tools LICENSE/NOTICE files were matched against their pinned source revisions (2026-10-18). Recheck attribution whenever these versions change.
