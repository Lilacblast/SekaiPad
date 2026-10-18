#!/usr/bin/env python3
"""Read-only Git source/history publication gate. Never prints matching secret values."""
from __future__ import annotations
import argparse
import re
import subprocess
from pathlib import Path

BLOCK_PATH = re.compile(
    r"(?i)(^|/)(runtime|downloads|captures|screenshots|recordings|secrets|\.ssh|\.aws)(/|$)|"
    r"\.(apk|apks|xapk|aab|obb|vdi|vmdk|iso|img|pem|p12|pfx|keystore|"
    r"pcap|pcapng|har|dmp|mdmp|etl|sqlite3?|png|jpe?g|webp|gif|mp4|"
    r"mov|mkv|pdf|docx?|xlsx?|pptx?|exe|dll|pdb|jar|zip|7z)$|"
    r"(^|/)\.env($|\.)|(^|/)id_(rsa|ed25519)($|\.)"
)
SENSITIVE_SIGNATURE = (
    r"-----BEGIN[[:space:]]+(RSA|EC|OPENSSH|PRIVATE)[[:space:]]+PRIVATE[[:space:]]+KEY-----"
    r"|ghp_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}"
    r"|AKIA[0-9A-Z]{16}|xox[baprs]-[A-Za-z0-9-]{20,}"
    r"|sk-[A-Za-z0-9_-]{32,}"
)

def git(root: Path, *args: str, codes=(0,)) -> str:
    p = subprocess.run(["git", "-C", str(root), *args],
                       text=True, capture_output=True, errors="replace")
    if p.returncode not in codes:
        raise RuntimeError("Git operation failed: " + args[0])
    return p.stdout

def audit(root: Path) -> tuple[list[str], dict]:
    git(root, "rev-parse", "--show-toplevel")
    issues: set[str] = set()
    files = [p.replace("\\", "/") for p in git(root, "ls-files").splitlines() if p]
    for p in files:
        if BLOCK_PATH.search(p):
            issues.add("HEAD restricted path: " + p)
    objects = git(root, "rev-list", "--objects", "--all").splitlines()
    for obj in objects:
        _, sep, p = obj.partition(" ")
        if sep and BLOCK_PATH.search(p.replace("\\", "/")):
            issues.add("HISTORY restricted path: " + p)
    commits = [c for c in git(root, "rev-list", "--all").splitlines() if c]
    for commit in commits:
        matches = git(root, "grep", "-I", "-l", "-E", "-e",
                      SENSITIVE_SIGNATURE, commit, "--", codes=(0, 1))
        for line in matches.splitlines():
            issues.add("HISTORY possible secret in file: " + line)
    stats = {
        "tracked_files": len(files), "reachable_objects": len(objects),
        "commits_scanned": len(commits),
        "author_identity_variants": len(set(git(root, "log", "--all",
                     "--format=%an <%ae> | %cn <%ce>").splitlines()))
    }
    return sorted(issues), stats

def main() -> int:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--repo", type=Path, default=Path(__file__).resolve().parent.parent)
    p.add_argument("--strict", action="store_true")
    args = p.parse_args()
    try:
        issues, stats = audit(args.repo.resolve())
    except (OSError, RuntimeError) as exc:
        print("SCAN_ERROR:", exc)
        return 2
    print("SOURCE_AUDIT_COUNTS:", stats)
    for issue in issues:
        print("BLOCKER:", issue)
    print("PUBLICATION_PATH_SIGNATURE_SCAN_"+("BLOCKED" if issues else "PASS"))
    return 1 if issues and args.strict else 0

if __name__ == "__main__":
    raise SystemExit(main())
