"""Synthetic offline regression for the read-only publication gate."""
from __future__ import annotations
import importlib.util
import subprocess
import tempfile
import unittest
from pathlib import Path

FILE = Path(__file__).resolve().parents[1] / "tools" / "audit-publication.py"
spec = importlib.util.spec_from_file_location("publication_audit", FILE)
assert spec and spec.loader
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)

def git(root: Path, *args: str) -> None:
    subprocess.run(["git", "-C", str(root), *args], check=True,
                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

class PublicationAuditTests(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        git(self.root, "init")
        git(self.root, "config", "user.name", "Test Only")
        git(self.root, "config", "user.email", "fixture@example.invalid")
    def commit(self, path: str, text: str):
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(text, encoding="utf-8")
        git(self.root, "add", path)
        git(self.root, "commit", "-m", "test fixture")
    def test_clean(self):
        self.commit("src/A.cs", "class A {}")
        issues, stats = mod.audit(self.root)
        self.assertFalse(issues)
        self.assertEqual(stats["commits_scanned"], 1)
    def test_game_asset_name(self):
        self.commit("runtime/private.apk", "synthetic")
        issues, _ = mod.audit(self.root)
        self.assertTrue(any("private.apk" in issue for issue in issues))
    def test_deleted_history_remains_blocked(self):
        self.commit("downloads/old.zip", "synthetic")
        git(self.root, "rm", "downloads/old.zip")
        git(self.root, "commit", "-m", "remove")
        issues, _ = mod.audit(self.root)
        self.assertTrue(any("HISTORY" in issue for issue in issues))
    def test_no_secret_value_leak(self):
        fake = "ghp_" + "a" * 40
        self.commit("src/B.cs", fake)
        issues, _ = mod.audit(self.root)
        self.assertTrue(issues)
        self.assertNotIn(fake, repr(issues))

if __name__ == "__main__":
    unittest.main()
