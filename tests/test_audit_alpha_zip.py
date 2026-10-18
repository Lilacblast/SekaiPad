"""Synthetic offline checks for reviewed source-to-binary ZIP provenance.

Constructs ZIPs with placeholder binaries; no SDK, network, emulator,
installer, process hooks or real Windows input is involved.
"""
from __future__ import annotations

import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
import warnings
import zipfile

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "tools" / "audit-alpha-zip.py"
SPEC = importlib.util.spec_from_file_location("audit_alpha_zip", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
audit_mod = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(audit_mod)


class AlphaZipReviewTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory(prefix="SekaiPad-alpha-zip-")
        self.addCleanup(self.temp.cleanup)
        self.zip_path = Path(self.temp.name) / "stage.zip"
        self.members: list[tuple[str, bytes]] = []
        packages = {name: {"type": "package"}
                    for name in sorted(audit_mod.KNOWN_RUNTIME)}
        for name in sorted(audit_mod.APPROVED):
            if name.startswith("SekaiPad/licenses/"):
                contents = (ROOT / name.removeprefix("SekaiPad/")).read_bytes()
            elif name in audit_mod.COPIED_TEXT:
                contents = (ROOT / audit_mod.COPIED_TEXT[name]).read_bytes()
            elif name == "SekaiPad/SekaiPad.deps.json":
                contents = json.dumps({"libraries": packages}).encode()
            elif name.endswith((".exe", ".dll")):
                contents = b"synthetic zero-executable binary"
            else:
                contents = b"synthetic fixture, no private paths"
            self.members.append((name, contents))

    def verify(self) -> list[str]:
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", UserWarning)
            with zipfile.ZipFile(self.zip_path, "w", zipfile.ZIP_DEFLATED) as z:
                for name, raw in self.members:
                    z.writestr(name, raw)
        failures, _ = audit_mod.audit(self.zip_path, ROOT)
        return failures

    def edit(self, name: str, contents: bytes | None) -> None:
        self.members = [(n, raw) for n, raw in self.members if n != name]
        if contents is not None:
            self.members.append((name, contents))

    def test_reviewed_zip_accepted(self) -> None:
        self.assertEqual(self.verify(), [])

    def test_missing_approved_dll_rejected(self) -> None:
        self.edit("SekaiPad/Grpc.Net.Client.dll", None)
        self.assertTrue(self.verify())

    def test_additional_dll_rejected(self) -> None:
        self.members.append(("SekaiPad/Injected.dll", b"synthetic"))
        self.assertTrue(self.verify())

    def test_reviewed_parent_directory_entries_accepted(self) -> None:
        for name in ("SekaiPad/", "SekaiPad/licenses/", "SekaiPad/tools/"):
            self.members.append((name, b""))
        self.assertEqual(self.verify(), [])

    def test_unreviewed_directory_entry_rejected(self) -> None:
        self.members.append(("SekaiPad/unreviewed-private-dir/", b""))
        self.assertTrue(any("Unreviewed directory" in issue
                            for issue in self.verify()))

    def test_directory_traversal_entry_rejected(self) -> None:
        self.members.append(("SekaiPad/../", b""))
        self.assertTrue(any("Unsafe ZIP entry path" in issue
                            for issue in self.verify()))

    def test_zip_symlink_entry_rejected(self) -> None:
        link = zipfile.ZipInfo("SekaiPad/README.md")
        link.create_system = 3
        link.external_attr = (0o120777 << 16)
        self.members = [(name, raw) for name, raw in self.members
                        if name != "SekaiPad/README.md"]
        # Preserve the original correct content so the symlink-specific check
        # must reject this independently of the docs/hash verification gate.
        data = (ROOT / "README.md").read_bytes()
        self.members.append((link, data))
        self.assertIn("ZIP links are forbidden", self.verify())

    def test_case_colliding_directory_entry_rejected(self) -> None:
        self.members.append(("SekaiPad/", b""))
        self.members.append(("sekaipad/", b""))
        self.assertIn("Case-insensitive ZIP entry collision", self.verify())

    def test_game_asset_file_rejected(self) -> None:
        self.members.append(("SekaiPad/Project-Sekai.apk", b"synthetic"))
        self.assertTrue(self.verify())

    def test_license_text_tampering_rejected(self) -> None:
        self.edit("SekaiPad/licenses/Grpc.Tools.NOTICE.txt", b"altered upstream")
        self.assertTrue(self.verify())

    def test_line_endings_are_canonicalized(self) -> None:
        name = "SekaiPad/licenses/Grpc.DotNet.LICENSE.txt"
        original = (ROOT / name.removeprefix("SekaiPad/")).read_bytes()
        self.edit(name, original.replace(b"\n", b"\r\n"))
        self.assertEqual(self.verify(), [])

    def test_unapproved_runtime_package_rejected(self) -> None:
        obj = {"libraries": {"Google.Protobuf/9.99.99": {"type": "package"}}}
        self.edit("SekaiPad/SekaiPad.deps.json", json.dumps(obj).encode())
        self.assertTrue(self.verify())

    def test_embedded_developer_pdb_path_in_dll_rejected(self) -> None:
        self.edit("SekaiPad/SekaiPad.dll",
                  b"prefix\x00C:\\Users\\PrivatePerson\\AppData\\Local\\SekaiPad.pdb\x00")
        self.assertTrue(self.verify())

    def test_modified_readme_rejected(self) -> None:
        self.edit("SekaiPad/README.md", b"unaligned release README")
        self.assertTrue(self.verify())

    def test_modified_offline_staging_helper_rejected(self) -> None:
        self.edit("SekaiPad/tools/stage-scrcpy-server.ps1", b"modified helper")
        self.assertTrue(self.verify())

    def test_private_path_in_text_rejected(self) -> None:
        self.edit("SekaiPad/README.md", b"C:\\Users\\PrivatePerson\\Downloads")
        self.assertTrue(self.verify())

    def test_path_traversal_rejected(self) -> None:
        self.members.append(("SekaiPad/../../private.txt", b"synthetic"))
        self.assertTrue(self.verify())

    def test_duplicate_member_rejected(self) -> None:
        self.members.append(self.members[0])
        self.assertTrue(self.verify())

    def test_case_collision_rejected(self) -> None:
        self.members.append(("sekaipad/license", b"synthetic"))
        self.assertTrue(self.verify())


if __name__ == "__main__":
    unittest.main()
