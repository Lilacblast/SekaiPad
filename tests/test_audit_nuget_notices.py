"""Offline integration regression for the reviewed alpha NuGet package/NOTICE gate.

All package binaries are synthetic zero-byte fixtures. The test never touches
Windows input, emulator state, or any user's installed .NET environment.
"""

from __future__ import annotations

import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CHECK = ROOT / "tools" / "verify-alpha-deps.ps1"

RUNTIME_PACKAGES = (
    "Google.Protobuf/3.29.3",
    "Grpc.Core.Api/2.66.0",
    "Grpc.Net.Client/2.66.0",
    "Grpc.Net.Common/2.66.0",
    "Microsoft.Extensions.Logging.Abstractions/6.0.0",
)
APPROVED_DLLS = (
    "SekaiPad.dll",
    "SekaiPad.CursorGuard.dll",
    "Google.Protobuf.dll",
    "Grpc.Core.Api.dll",
    "Grpc.Net.Client.dll",
    "Grpc.Net.Common.dll",
    "Microsoft.Extensions.Logging.Abstractions.dll",
)
LICENSES = (
    "RawInput.Touchpad.MIT",
    "Apache-2.0",
    "Google.Protobuf.BSD-3-Clause",
    "Microsoft.DotNet.MIT",
    "Grpc.DotNet.LICENSE.txt",
    "Grpc.Tools.LICENSE.txt",
    "Grpc.Tools.NOTICE.txt",
    "Microsoft.Extensions.Logging.Abstractions.LICENSE.txt",
    "Microsoft.Extensions.Logging.Abstractions.THIRD-PARTY-NOTICES.txt",
)


@unittest.skipUnless(sys.platform == "win32", "PowerShell verifier runs on Windows")
class NuGetNoticeGateTests(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = tempfile.TemporaryDirectory(prefix="SekaiPad-AlphaGate-")
        self.addCleanup(self.tmp.cleanup)
        self.stage = Path(self.tmp.name)
        (self.stage / "licenses").mkdir()
        for name in LICENSES:
            shutil.copyfile(ROOT / "licenses" / name, self.stage / "licenses" / name)
        for name in APPROVED_DLLS:
            (self.stage / name).write_bytes(b"")
        (self.stage / "LICENSE").write_text("synthetic", encoding="utf-8")
        (self.stage / "THIRD_PARTY_NOTICES.md").write_text("synthetic", encoding="utf-8")
        self.deps = {"libraries": {name: {"type": "package"} for name in RUNTIME_PACKAGES}}
        self.write_deps()

    def write_deps(self) -> None:
        (self.stage / "SekaiPad.deps.json").write_text(
            json.dumps(self.deps, separators=(",", ":")), encoding="utf-8"
        )

    def verify(self) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            ["powershell.exe", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
             "-File", str(CHECK), "-StageDirectory", str(self.stage)],
            stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
            errors="replace", timeout=30, check=False,
        )

    def test_reviewed_runtime_deps_and_notices_accepted(self) -> None:
        result = self.verify()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("ALPHA_LICENSE_AND_DLL_INVENTORY_PASS", result.stdout)

    def test_corrupted_nuget_third_party_notice_rejected(self) -> None:
        path = self.stage / "licenses" / LICENSES[-1]
        with path.open("ab") as handle:
            handle.write(b"\nSYNTHETIC-TAMPER")
        self.assertNotEqual(self.verify().returncode, 0)

    def test_corrupt_grpc_dotnet_upstream_license_rejected(self) -> None:
        notice = self.stage / "licenses" / "Grpc.DotNet.LICENSE.txt"
        notice.write_bytes(notice.read_bytes() + b"tampered")
        self.assertNotEqual(self.verify().returncode, 0)

    def test_corrupt_grpc_tools_notice_rejected(self) -> None:
        notice = self.stage / "licenses" / "Grpc.Tools.NOTICE.txt"
        notice.write_bytes(notice.read_bytes() + b"tampered")
        self.assertNotEqual(self.verify().returncode, 0)

    def test_removed_license_rejected(self) -> None:
        (self.stage / "licenses" / LICENSES[-2]).unlink()
        self.assertNotEqual(self.verify().returncode, 0)

    def test_changed_dependency_version_rejected(self) -> None:
        self.deps["libraries"].pop("Grpc.Net.Client/2.66.0")
        self.deps["libraries"]["Grpc.Net.Client/9.99.99"] = {"type": "package"}
        self.write_deps()
        self.assertNotEqual(self.verify().returncode, 0)

    def test_unreviewed_dll_rejected(self) -> None:
        (self.stage / "Injected.Unknown.dll").write_bytes(b"")
        self.assertNotEqual(self.verify().returncode, 0)

    def test_required_dll_missing_rejected(self) -> None:
        (self.stage / "Grpc.Net.Common.dll").unlink()
        self.assertNotEqual(self.verify().returncode, 0)

    def test_nested_dll_rejected_after_windows_short_path_normalization(self) -> None:
        nested = self.stage / "unreviewed"
        nested.mkdir()
        (nested / "Google.Protobuf.dll").write_bytes(b"")
        self.assertNotEqual(self.verify().returncode, 0)


if __name__ == "__main__":
    unittest.main()
