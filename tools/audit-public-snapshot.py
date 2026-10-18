#!/usr/bin/env python3
"""Fail-closed, read-only audit of a *source-only* GitHub publication snapshot.

Checks exactly the SHA-256 manifest, explicit source-file allowlist, source
content for common secrets/private local paths, and disguised binary payloads.
This is a technical gate, not a substitute for human provenance review.
"""
from __future__ import annotations

import argparse
import hashlib
import re
import sys
from pathlib import Path, PurePosixPath

MAX_BYTES = 2 * 1024 * 1024
SECRETS = [
    re.compile(rb"-----BEGIN (?:RSA |EC |OPENSSH |)PRIVATE KEY-----"),
    re.compile(rb"ghp_[A-Za-z0-9]{30,}"),
    re.compile(rb"github_pat_[A-Za-z0-9_]{30,}"),
    re.compile(rb"AKIA[0-9A-Z]{16}"),
    re.compile(rb"xox[baprs]-[A-Za-z0-9-]{20,}"),
    re.compile(rb"sk-[A-Za-z0-9_-]{32,}"),
    re.compile(rb"(?i)[A-Z]:[\\/]Users[\\/][^\\/\r\n]+[\\/]"),
    re.compile(rb"(?i)[\\/]vm-lab[\\/]"),
]
BLOCKED_SUFFIXES = {
    ".apk", ".apks", ".xapk", ".aab", ".obb", ".vdi", ".vmdk",
    ".qcow2", ".iso", ".img", ".p12", ".pfx", ".pem",
    ".keystore", ".pcap", ".pcapng", ".har", ".dmp", ".mdmp",
    ".etl", ".sqlite", ".sqlite3", ".db", ".png", ".jpg",
    ".jpeg", ".webp", ".gif", ".mp4", ".exe", ".dll", ".pdb",
    ".jar", ".zip", ".7z", ".rar", ".msi", ".msix",
}
BLOCKED_PARTS = {
    ".git", ".ssh", ".aws", "secrets", "downloads", "runtime",
    "screenshots", "captures", "recordings", "bin", "obj", "dist",
}
EXPECTED_MANIFEST = "PUBLICATION_MANIFEST.sha256"
EXPECTED_LIST = "tools/public-source-allowlist.txt"


def safe_relpath(value: str) -> str:
    p = PurePosixPath(value)
    if (
        not value or "\\" in value or value.startswith("/") or ":" in value
        or value.endswith("/") or value != p.as_posix()
        or any(s in ("", ".", "..") for s in value.split("/"))
        or any(s.casefold() in BLOCKED_PARTS for s in p.parts)
        or p.suffix.casefold() in BLOCKED_SUFFIXES
        or p.name.casefold() in {".env", "scrcpy-server"}
        or p.name.casefold().startswith((".env.", "id_rsa", "id_ed25519"))
    ):
        raise ValueError("Forbidden or malformed publication path: " + value)
    return value


def audit(root: Path, *, allow_git_metadata: bool = False) -> tuple[list[str], dict[str, int]]:
    issues = []
    root = root.resolve()
    if not (root / EXPECTED_MANIFEST).is_file():
        return ["Missing strict publication manifest"], {}
    if not (root / EXPECTED_LIST).is_file():
        return ["Missing explicit reviewed source-file allowlist"], {}
    allowlist = set()
    for raw in (root / EXPECTED_LIST).read_text("utf-8-sig").splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        try:
            key = safe_relpath(line)
        except ValueError as ex:
            issues.append(str(ex))
            continue
        if key.casefold() in {s.casefold() for s in allowlist}:
            issues.append("Duplicate case-insensitive source allowlist entry: " + key)
        allowlist.add(key)
    expected = {}
    for line in (root / EXPECTED_MANIFEST).read_text("utf-8-sig").splitlines():
        match = re.fullmatch(r"([0-9a-fA-F]{64})  (.+)", line)
        if not match:
            issues.append("Malformed manifest entry (content suppressed)")
            continue
        digest, name = match.groups()
        try:
            key = safe_relpath(name)
        except ValueError as ex:
            issues.append(str(ex))
            continue
        if key in expected:
            issues.append("Duplicate manifest entry: " + key)
        expected[key] = digest.lower()
    if allowlist != set(expected):
        for extra in sorted(allowlist - set(expected)):
            issues.append("Source allowlist path missing from manifest: " + extra)
        for extra in sorted(set(expected) - allowlist):
            issues.append("Manifest contains unreviewed source path: " + extra)
    actual = set()
    files = 0
    for path in root.rglob("*"):
        rel = path.relative_to(root).as_posix()
        # A GitHub Actions checkout always has .git; exclude only when
        # --allow-git-metadata was explicitly passed by the CI workflow.
        if allow_git_metadata and rel.split("/")[0] == ".git":
            continue
        if path.is_symlink():
            issues.append("Symlink/reparse point not allowed: " + rel)
            continue
        if path.is_dir():
            if path.name.lower() in BLOCKED_PARTS:
                issues.append("Forbidden directory: " + rel)
            continue
        files += 1
        try:
            name = safe_relpath(rel)
        except ValueError as ex:
            issues.append(str(ex))
            continue
        actual.add(name)
        if name not in expected and name != EXPECTED_MANIFEST:
            issues.append("Unexpected file in exported source tree: " + name)
            continue
        if name == EXPECTED_MANIFEST:
            continue
        raw = path.read_bytes()
        if len(raw) > MAX_BYTES:
            issues.append("File exceeds 2 MiB source ceiling: " + name)
        if raw.startswith((b"PK\x03\x04", b"\x7fELF", b"MZ", b"\x89PNG", b"\xff\xd8\xff")):
            issues.append("Disguised binary signature inside source: " + name)
        digest = hashlib.sha256(raw).hexdigest()
        if expected.get(name) != digest:
            # A Windows Git checkout may transparently convert LF <-> CRLF.
            # Never relax snapshot/source-only audits. In explicitly marked
            # Git checkouts accept *only* that well-defined text conversion,
            # not arbitrary byte differences or edited source lines.
            lf = raw.replace(b"\r\n", b"\n")
            crlf = lf.replace(b"\n", b"\r\n")
            eol_only_match = allow_git_metadata and expected.get(name) in {
                hashlib.sha256(lf).hexdigest(),
                hashlib.sha256(crlf).hexdigest(),
            }
            if not eol_only_match:
                issues.append("SHA256 mismatch versus reviewed manifest: " + name)
        if b"\x00" in raw:
            issues.append("NUL byte inside source: " + name)
        for regex in SECRETS:
            if regex.search(raw):
                issues.append("Potential private token/path in: " + name)
                break
    for name in sorted(set(expected) - actual):
        issues.append("Manifest file missing: " + name)
    return sorted(set(issues)), {
        "manifest_files": len(expected),
        "source_files_found": files - int(EXPECTED_MANIFEST in actual),
        "allowlisted_paths": len(allowlist),
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--strict", action="store_true")
    parser.add_argument("--allow-git-metadata", action="store_true",
                        help="Trust only Git metadata in a source-only checkout (CI).")
    args = parser.parse_args(argv)
    try:
        issues, counts = audit(args.root, allow_git_metadata=args.allow_git_metadata)
    except (OSError, UnicodeError, ValueError) as exc:
        print("EXPORT_AUDIT_ERROR:", type(exc).__name__)
        return 2
    print("EXPORT_AUDIT_COUNTS:", counts)
    for issue in issues:
        print("BLOCKER:", issue)
    print("EXPORT_SHA256_PATH_SECRET_GUARD_" + ("BLOCKED" if issues else "PASS"))
    return 1 if issues and args.strict else 0


if __name__ == "__main__":
    sys.exit(main())
