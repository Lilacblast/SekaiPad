"""Regression cases for the offline GitHub Actions supply-chain gate."""

from __future__ import annotations

import importlib.util
import unittest
from pathlib import Path

TOOL = Path(__file__).resolve().parents[1] / "tools" / "audit-github-actions.py"
spec = importlib.util.spec_from_file_location("sekaipad_actions_audit", TOOL)
assert spec and spec.loader
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)

GUARD = " && ".join(audit.PRIVATE_GATES)
PIN = "a" * 40
BASE = f"""name: Minimal synthetic workflow
on: workflow_dispatch
permissions:
  contents: read
jobs:
  smoke:
    runs-on: windows-latest
    steps:
      - name: Build opt-in private local Windows test bundle (no app execution)
        if: ${{{{ {GUARD} }}}}
        run: echo isolated
      - name: Upload private artifacts
        if: ${{{{ {GUARD} }}}}
        uses: actions/upload-artifact@{PIN}
        with:
          retention-days: 1
"""


class WorkflowAuditTests(unittest.TestCase):
    def test_reviewed_private_gate(self) -> None:
        self.assertEqual(audit.audit_text(BASE), [])

    def test_unpinned_action_caught_with_step_dash(self) -> None:
        changed = BASE.replace("    steps:\n", "    steps:\n      - uses: outsider/action@main\n")
        self.assertTrue(any("unpinned" in message for message in audit.audit_text(changed)))

    def test_unknown_pinned_action_rejected(self) -> None:
        changed = BASE.replace("actions/upload-artifact@", "unreviewed/action@")
        self.assertTrue(any("unreviewed" in message for message in audit.audit_text(changed)))

    def test_missing_branch_gate_rejected_even_when_comment_mentions_it(self) -> None:
        guard = GUARD.replace(" && " + audit.PRIVATE_GATES[-1], "")
        changed = BASE.replace(GUARD, guard)
        changed += f"\n# Disabled gate was: {audit.PRIVATE_GATES[-1]}\n"
        self.assertTrue(any("all four AND gates" in message for message in audit.audit_text(changed)))

    def test_or_true_cannot_bypass_gate(self) -> None:
        changed = BASE.replace(GUARD, GUARD + " || true")
        self.assertTrue(any("all four AND gates" in message for message in audit.audit_text(changed)))

    def test_no_private_artifact_guard(self) -> None:
        changed = BASE.replace(f"        if: ${{{{ {GUARD} }}}}\n", "")
        self.assertTrue(any("missing guard" in message for message in audit.audit_text(changed)))

    def test_expiry_required(self) -> None:
        changed = BASE.replace("          retention-days: 1\n", "          retention-days: 90\n")
        self.assertTrue(any("expire" in message for message in audit.audit_text(changed)))

    def test_pull_request_target_denied(self) -> None:
        changed = BASE.replace("on: workflow_dispatch", "on:\n  pull_request_target:")
        self.assertTrue(any("pull_request_target" in message for message in audit.audit_text(changed)))


if __name__ == "__main__":
    unittest.main()
