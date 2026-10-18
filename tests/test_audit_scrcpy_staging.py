"""Offline packaging checks for official scrcpy-server local staging.

Uses synthetic bytes in a temporary directory only. Never downloads a
server, contacts Android/ADB, modifies Windows input or opens any game.
"""
from __future__ import annotations

import hashlib
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "tools" / "stage-scrcpy-server.ps1"
FAKE_SERVER = b"NONEXECUTABLE-SEKAIPAD-SCRCPY-FIXTURE\n" * 7_000


@unittest.skipUnless(sys.platform == "win32", "Staging helper is PowerShell on Windows")
class ScrcpyServerStagingTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory(prefix="SekaiPad-scrcpy-stage-")
        self.addCleanup(self.temp.cleanup)
        self.parent = Path(self.temp.name)
        self.stage = self.parent / "standalone-unpacked-preview"
        self.stage.mkdir()
        for filename in ("SekaiPad.exe", "LICENSE", "THIRD_PARTY_NOTICES.md"):
            (self.stage / filename).write_bytes(b"synthetic fixture")
        self.source = self.parent / "scrcpy-server"
        self.source.write_bytes(FAKE_SERVER)
        self.sha = hashlib.sha256(FAKE_SERVER).hexdigest()
        self.dest = self.stage / "tools" / "scrcpy-server"

    def run_stage(
        self, expected: str | None = None, source: Path | None = None
    ) -> subprocess.CompletedProcess[str]:
        cmd = [
            "powershell.exe", "-NoProfile", "-NonInteractive", "-ExecutionPolicy",
            "Bypass", "-File", str(SCRIPT), "-ServerFile", str(source or self.source),
            "-ReleaseDirectory", str(self.stage),
        ]
        if expected is not None:
            cmd += ["-ExpectedSha256", expected]
        return subprocess.run(cmd, capture_output=True, text=True, errors="replace",
                              timeout=35, check=False)

    def test_valid_sha256_stages_exact_bytes(self) -> None:
        result = self.run_stage(expected=self.sha)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(self.dest.read_bytes(), FAKE_SERVER)
        self.assertIn("LOCAL_SCRCPY_SERVER_STAGED", result.stdout)

    def test_unreviewed_hash_rejected_before_creating_tools(self) -> None:
        result = self.run_stage(expected="f" * 64)
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(self.dest.exists())

    def test_existing_staged_server_never_overwritten(self) -> None:
        first = self.run_stage(expected=self.sha)
        self.assertEqual(first.returncode, 0, first.stdout + first.stderr)
        second = self.run_stage(expected=self.sha)
        self.assertNotEqual(second.returncode, 0)
        self.assertEqual(self.dest.read_bytes(), FAKE_SERVER)

    def test_wrong_source_name_rejected(self) -> None:
        wrong = self.parent / "unreviewed-server.jar"
        wrong.write_bytes(FAKE_SERVER)
        result = self.run_stage(expected=self.sha, source=wrong)
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(self.dest.exists())

    def test_source_git_ancestor_rejected(self) -> None:
        (self.parent / ".git").mkdir()
        result = self.run_stage(expected=self.sha)
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(self.dest.exists())

    def test_linked_git_worktree_parent_rejected(self) -> None:
        (self.parent / ".git").write_text("gitdir: elsewhere\n", encoding="utf-8")
        result = self.run_stage(expected=self.sha)
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(self.dest.exists())

    def test_missing_release_binary_rejected(self) -> None:
        (self.stage / "SekaiPad.exe").unlink()
        result = self.run_stage(expected=self.sha)
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(self.dest.exists())

    def test_reparse_tools_directory_rejected_when_supported(self) -> None:
        target = self.parent / "outside-preview"
        target.mkdir()
        link = self.stage / "tools"
        try:
            link.symlink_to(target, target_is_directory=True)
        except (OSError, NotImplementedError):
            self.skipTest("Current Windows test runner does not permit symlinks")
        result = self.run_stage(expected=self.sha)
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse((target / "scrcpy-server").exists())


if __name__ == "__main__":
    unittest.main()
