#!/usr/bin/env python3
"""Check imported source provenance before any public-source export."""
from __future__ import annotations

import argparse
import hashlib
from pathlib import Path


PINNED_SHA256 = {
    "src/SekaiPad/Protocol/emulator_controller.proto":
        "1d62c6bcad5f06621f90ec2bf26c661ba769ccd0f1416b5314d25a68e04eee5f",
    "licenses/RawInput.Touchpad.MIT":
        "f872f91a21689d046c7e05b1d787a9deeaf713d855e9241699b5789ea5b7f1d1",
    "licenses/Grpc.DotNet.LICENSE.txt":
        "cfc7749b96f63bd31c3c42b5c471bf756814053e847c10f3eb003417bc523d30",
    "licenses/Grpc.Tools.LICENSE.txt":
        "590198e3f305f2c347fde64d637c65492bbef554db6c8364e149cd375e3797ee",
    "licenses/Grpc.Tools.NOTICE.txt":
        "693ff28ec216d5112ac1bbfe64ef539005867d1c7bd427b57d579683293b947f",
    "licenses/Microsoft.Extensions.Logging.Abstractions.LICENSE.txt":
        "d7a68596ab69b06f51ca278a6545148e4269a9381c26d597c13df5d88e08cf5b",
    "licenses/Microsoft.Extensions.Logging.Abstractions.THIRD-PARTY-NOTICES.txt":
        "a6344bfb229bc08d1071cec400eea216e307439f961c5943ec1b56d2245acb95",
}
PINNED_LF_CANONICAL_SHA256 = {
    # The original MIT text uses CRLF. The audited public export uses LF.
    # Both digests were computed from the same SHA-verified upstream LICENSE.txt.
    "licenses/RawInput.Touchpad.MIT":
        "d1184486886fb11c62163eaad137d624c981c7a156f8b69b5f1204fe99a28dad",
    "licenses/Microsoft.Extensions.Logging.Abstractions.LICENSE.txt":
        "cfc21f5e8bd655ae997eec916138b707b1d290b83272c02a95c9f821b8c87310",
    "licenses/Microsoft.Extensions.Logging.Abstractions.THIRD-PARTY-NOTICES.txt":
        "df255d595f29db06c6d462ceb7c04b33d627e98ac2f1745e1f1fb9f08eaaecb0",
}
IMPORTED = {
    "src/SekaiPad/Input/TouchpadContact.cs",
    "src/SekaiPad/Input/TouchpadHelper.cs",
}
UPSTREAM_REVISION = "38a9e68c1c42cb19bae02fe84ee88b2a4b2a8639"


def audit(root: Path) -> list[str]:
    errors: list[str] = []
    for relative, sha in PINNED_SHA256.items():
        path = root / relative
        if not path.is_file():
            errors.append("Missing upstream source/license: " + relative)
            continue
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        approved = {sha, PINNED_LF_CANONICAL_SHA256.get(relative, sha)}
        if digest not in approved:
            errors.append("Unreviewed upstream source/license revision: " + relative)
    for relative in sorted(IMPORTED):
        path = root / relative
        if not path.is_file():
            errors.append("Missing copyright-attributed source: " + relative)
            continue
        head = path.read_text("utf-8-sig").splitlines()[:10]
        text = "\n".join(head)
        for needle in (
            "SPDX-License-Identifier: MIT",
            "Copyright (c) 2021 emoacht",
            "https://github.com/emoacht/RawInput.Touchpad",
            UPSTREAM_REVISION,
        ):
            if needle not in text:
                errors.append("Missing imported-code provenance in " + relative)
                break
    aosp = root / "src/SekaiPad/Protocol/emulator_controller.proto"
    if aosp.is_file():
        header = "\n".join(aosp.read_text("utf-8").splitlines()[:15])
        if ("Copyright (C) 2018 The Android Open Source Project" not in header
                or "Licensed under the Apache License, Version 2.0" not in header):
            errors.append("AOSP source copyright/license header not preserved")
    apache = root / "licenses/Apache-2.0"
    if not apache.is_file():
        errors.append("Full Apache-2.0 notice is missing")
    elif "Apache License" not in apache.read_text("utf-8")[:180]:
        errors.append("Bundled Apache-2.0 notice is invalid")
    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parent.parent)
    args = parser.parse_args()
    issues = audit(args.root)
    for issue in issues:
        print("THIRD_PARTY_ATTRIBUTION_BLOCKER:", issue)
    print("PINNED_THIRD_PARTY_PROVENANCE_" + ("BLOCKED" if issues else "PASS"))
    return int(bool(issues))


if __name__ == "__main__":
    raise SystemExit(main())
