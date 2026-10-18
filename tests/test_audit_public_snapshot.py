"""Offline fixtures for the SHA256/allowlist/privacy gate for public snapshots."""
from __future__ import annotations

import hashlib
import importlib.util
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[1] / "tools" / "audit-public-snapshot.py"
spec = importlib.util.spec_from_file_location("audit_public_snapshot", SCRIPT)
assert spec and spec.loader
audit_mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit_mod)


class SourceSnapshotAuditTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="sekaipad-synthetic-audit-")
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.items = {
            "README.md": b"# Synthetic tool\n",
            "tools/public-source-allowlist.txt":
                b"README.md\ntools/public-source-allowlist.txt\n",
        }
        self.build()

    def build(self):
        for name, content in self.items.items():
            path = self.root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(content)
        entries = []
        for name, content in self.items.items():
            digest = hashlib.sha256(content).hexdigest()
            entries.append(f"{digest}  {name}")
        (self.root / "PUBLICATION_MANIFEST.sha256").write_text(
            "\n".join(entries) + "\n", encoding="utf-8"
        )

    def test_clean_fixture(self):
        problems, stats = audit_mod.audit(self.root)
        self.assertEqual(problems, [])
        self.assertEqual(stats["manifest_files"], 2)

    def test_git_directory_is_not_silently_ignored(self):
        folder = self.root / '.git'
        folder.mkdir()
        (folder / 'config').write_text('synthetic git config', encoding='utf-8')
        blocked, _ = audit_mod.audit(self.root)
        self.assertTrue(blocked)
        safe, _ = audit_mod.audit(self.root, allow_git_metadata=True)
        self.assertEqual(safe, [])

    def test_unlisted_extra_file(self):
        self.build()
        (self.root / "unexpected-source.cs").write_text("class A{}", "utf-8")
        problems, _ = audit_mod.audit(self.root)
        self.assertTrue(any("Unexpected file" in p for p in problems))

    def test_only_git_checkout_line_endings_may_differ(self):
        self.items["README.md"] = b"one\ntwo\n"
        self.build()
        (self.root / "README.md").write_bytes(b"one\r\ntwo\r\n")
        strict, _ = audit_mod.audit(self.root)
        self.assertTrue(any("SHA256" in p for p in strict))
        checkout, _ = audit_mod.audit(self.root, allow_git_metadata=True)
        self.assertEqual(checkout, [])

    def test_tampered_source(self):
        (self.root / "README.md").write_text("tampered", "utf-8")
        problems, _ = audit_mod.audit(self.root)
        self.assertTrue(any("SHA256" in p for p in problems))

    def test_disguised_binary(self):
        self.items["README.md"] = b"PK\x03\x04contents"
        self.build()
        problems, _ = audit_mod.audit(self.root)
        self.assertTrue(any("Disguised binary" in p for p in problems))

    def test_secret_value_not_leaked(self):
        fake = "ghp_" + "A" * 40
        self.items["README.md"] = fake.encode()
        self.build()
        problems, _ = audit_mod.audit(self.root)
        self.assertTrue(any("Potential private" in p for p in problems))
        self.assertNotIn(fake, repr(problems))

    def test_local_windows_profile_path(self):
        self.items["README.md"] = "C:\\Users\\SecretProfile\\data".encode()
        self.build()
        problems, _ = audit_mod.audit(self.root)
        self.assertTrue(any("Potential private" in p for p in problems))

    def test_unreviewed_addition(self):
        self.items["new-unreviewed-source.cs"] = b"class Unreviewed{}"
        self.build()
        problems, _ = audit_mod.audit(self.root)
        self.assertTrue(any("unreviewed" in p for p in problems))

    def test_path_traversal(self):
        with self.assertRaises(ValueError):
            audit_mod.safe_relpath("../outside.txt")
        with self.assertRaises(ValueError):
            audit_mod.safe_relpath("src/private/../../secrets.txt")


if __name__ == "__main__":
    unittest.main()
