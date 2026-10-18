#!/usr/bin/env python3
"""Offline byte-precise audit of a locally built SekaiPad alpha ZIP.

Source-only code, official-license texts and the approved runtime payload are
verified without unpacking or executing the app or accessing Android.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
import zipfile
from pathlib import Path, PurePosixPath

APP_FILES = {
    "BUILDING.md", "LICENSE", "README.md", "SAFETY.md",
    "THIRD_PARTY_LICENSES.md", "THIRD_PARTY_NOTICES.md",
    "SekaiPad.exe", "SekaiPad.dll", "SekaiPad.deps.json",
    "SekaiPad.runtimeconfig.json",
    "SekaiPad.CursorGuard.exe", "SekaiPad.CursorGuard.dll",
    "SekaiPad.CursorGuard.deps.json", "SekaiPad.CursorGuard.runtimeconfig.json",
}
DEPENDENCY_DLLS = {
    "Google.Protobuf.dll", "Grpc.Core.Api.dll", "Grpc.Net.Client.dll",
    "Grpc.Net.Common.dll", "Microsoft.Extensions.Logging.Abstractions.dll",
}
LICENSES = {
    "RawInput.Touchpad.MIT", "Apache-2.0", "Google.Protobuf.BSD-3-Clause",
    "Microsoft.DotNet.MIT", "Grpc.DotNet.LICENSE.txt",
    "Grpc.Tools.LICENSE.txt", "Grpc.Tools.NOTICE.txt",
    "Microsoft.Extensions.Logging.Abstractions.LICENSE.txt",
    "Microsoft.Extensions.Logging.Abstractions.THIRD-PARTY-NOTICES.txt",
}
APPROVED = (
    {"SekaiPad/" + f for f in APP_FILES | DEPENDENCY_DLLS}
    | {"SekaiPad/licenses/" + name for name in LICENSES}
    | {"SekaiPad/tools/stage-scrcpy-server.ps1"}
)
KNOWN_RUNTIME = {
    "Google.Protobuf/3.29.3",
    "Grpc.Core.Api/2.66.0",
    "Grpc.Net.Client/2.66.0",
    "Grpc.Net.Common/2.66.0",
    "Microsoft.Extensions.Logging.Abstractions/6.0.0",
}
SECRET_MARKERS = [
    re.compile(rb"(?i)[A-Z]:[\\/]Users[\\/][^\\/\r\n]+[\\/]"),
    re.compile(rb"ghp_[A-Za-z0-9]{30,}"),
    re.compile(rb"github_pat_[A-Za-z0-9_]{30,}"),
    re.compile(rb"-----BEGIN (?:RSA |EC |OPENSSH |)PRIVATE KEY-----"),
]
TEXT_SUFFIXES = {".txt", ".md", ".json", ".ps1"}
BINARY_PRIVATE_PATH = re.compile(rb"(?i)[A-Z]:[\\/]Users[\\/][^\\/\r\n]{1,96}[\\/]")
# These files are copied verbatim into the staging area by package-alpha.
# CRLF/LF is the only accepted variation between Windows checkouts.
COPIED_TEXT = {
    "SekaiPad/LICENSE": "LICENSE",
    "SekaiPad/README.md": "README.md",
    "SekaiPad/THIRD_PARTY_NOTICES.md": "THIRD_PARTY_NOTICES.md",
    "SekaiPad/THIRD_PARTY_LICENSES.md": "THIRD_PARTY_LICENSES.md",
    "SekaiPad/BUILDING.md": "docs/BUILDING.md",
    "SekaiPad/SAFETY.md": "docs/SAFETY.md",
    "SekaiPad/tools/stage-scrcpy-server.ps1": "tools/stage-scrcpy-server.ps1",
}
def lf(data: bytes) -> bytes:
    return data.replace(b"\r\n", b"\n").replace(b"\r", b"\n")


MAX_FILE_BYTES = 25 * 1024 * 1024
MAX_TOTAL_BYTES = 100 * 1024 * 1024


def audit(archive_path: Path, source_root: Path) -> tuple[list[str], dict[str, object]]:
    issues: list[str] = []
    counts: dict[str, object] = {}
    try:
        with zipfile.ZipFile(archive_path) as archive:
            all_infos = archive.infolist()
            infos = [i for i in all_infos if not i.is_dir()]
            names = [i.filename for i in infos]
            counts["entries"] = len(names)
            counts["directories"] = len(all_infos) - len(infos)
            # Accept explicit directory records only for parents of the
            # allowlisted files. ZipFile.is_dir() alone must not hide unsafe
            # directories, symlink-like Unix attributes, or path aliases.
            allowed_dirs = {
                "/".join(parts[:i]) + "/"
                for approved_name in APPROVED
                for parts in [approved_name.split("/")]
                for i in range(1, len(parts))
            }
            all_names = [i.filename for i in all_infos]
            if len(all_names) != len(set(all_names)):
                issues.append("Duplicate ZIP entry")
            alias_names = [name.rstrip("/").casefold() for name in all_names]
            if len(alias_names) != len(set(alias_names)):
                issues.append("Case-insensitive ZIP entry collision")
            for info in all_infos:
                name = info.filename
                raw_path = name.rstrip("/") if info.is_dir() else name
                path = PurePosixPath(raw_path)
                if (not raw_path or raw_path != path.as_posix()
                        or ".." in path.parts or "\\" in name
                        or ":" in name or path.is_absolute()):
                    issues.append("Unsafe ZIP entry path")
                if info.is_dir() and name not in allowed_dirs:
                    issues.append("Unreviewed directory in ZIP: " + name)
                if ((info.external_attr >> 16) & 0o170000) == 0o120000:
                    issues.append("ZIP links are forbidden")
                if info.file_size > MAX_FILE_BYTES:
                    issues.append("Unexpectedly large ZIP entry")
            if sum(info.file_size for info in infos) > MAX_TOTAL_BYTES:
                issues.append("Unexpected total decompressed ZIP size")
            missing = APPROVED - set(names)
            extra = set(names) - APPROVED
            for name in sorted(missing):
                issues.append("Missing reviewed archive member: " + name)
            for name in sorted(extra):
                issues.append("Unreviewed archive member: " + name)

            # Hash exact checked-in license/NOTICE text instead of relying on
            # the currently installed runtime packages or license labels.
            for license_name in sorted(LICENSES):
                name = "SekaiPad/licenses/" + license_name
                if name not in names:
                    continue
                origin = source_root / "licenses" / license_name
                if not origin.is_file():
                    issues.append("Missing pinned license in source checkout: " + license_name)
                    continue
                packed = archive.read(name)
                reviewed = origin.read_bytes()
                # Git checkout canonicalization permits LF/CRLF changes only.
                if hashlib.sha256(lf(packed)).digest() != hashlib.sha256(lf(reviewed)).digest():
                    issues.append("Bundled legal text differs from reviewed source: " + license_name)

            for name, relative in sorted(COPIED_TEXT.items()):
                if name not in names:
                    continue
                origin = source_root / relative
                if not origin.is_file():
                    issues.append("Missing bundled documentation source: " + relative)
                    continue
                packed = archive.read(name)
                reviewed = origin.read_bytes()
                if hashlib.sha256(lf(packed)).digest() != hashlib.sha256(lf(reviewed)).digest():
                    issues.append("Bundled docs/tool differs from reviewed source: " + name)

            for name in names:
                suffix = PurePosixPath(name).suffix.lower()
                if suffix in {".exe", ".dll", ".pdb"}:
                    data = archive.read(name)
                    # PE CodeView records can store full local build paths to
                    # the omitted PDB. Check ASCII and UTF-16LE binary strings.
                    wide_text = data.decode("utf-16le", errors="ignore").encode("utf-8")
                    if BINARY_PRIVATE_PATH.search(data) or BINARY_PRIVATE_PATH.search(wide_text):
                        issues.append("Developer build path embedded in binary: " + name)
                if suffix not in TEXT_SUFFIXES:
                    continue
                data = archive.read(name)
                if any(pattern.search(data) for pattern in SECRET_MARKERS):
                    issues.append("Possible sensitive text in ZIP: " + name)
            dep_path = "SekaiPad/SekaiPad.deps.json"
            if dep_path in names:
                deps = json.loads(archive.read(dep_path))
                packages = {
                    name for name, entry in deps.get("libraries", {}).items()
                    if entry.get("type") == "package"
                }
                if packages != KNOWN_RUNTIME:
                    issues.append("Unreviewed or missing runtime packages in ZIP")
            counts["dlls"] = len([n for n in names if n.lower().endswith(".dll")])
            counts["licenses"] = len([n for n in names if n.startswith("SekaiPad/licenses/")])
            counts["archive_sha256"] = hashlib.sha256(archive_path.read_bytes()).hexdigest()
    except (OSError, ValueError, zipfile.BadZipFile, KeyError, TypeError,
            json.JSONDecodeError) as exc:
        # Fail closed without leaking local paths/account data from exceptions.
        issues.append("Archive could not be safely validated: " + type(exc).__name__)
    return sorted(set(issues)), counts


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--zip", type=Path, required=True)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parent.parent)
    args = parser.parse_args(argv)
    issues, counts = audit(args.zip, args.root)
    for item in issues:
        print("ALPHA_ZIP_BLOCKER:", item)
    print("ALPHA_ZIP_AUDIT:", counts)
    print("ALPHA_ZIP_EXACT_RUNTIME_LICENSE_PRIVACY_" + ("BLOCKED" if issues else "PASS"))
    return int(bool(issues))


if __name__ == "__main__":
    sys.exit(main())
