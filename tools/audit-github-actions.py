#!/usr/bin/env python3
"""Offline fail-closed checks for SekaiPad's GitHub Actions supply-chain policy.

This verifies *source configuration*, not the upstream actions' integrity,
or that a remote CI workflow was actually executed.
"""
from __future__ import annotations
import argparse
from pathlib import Path
import re

# The allowed upstream sources are official GitHub-maintained JavaScript actions.
ALLOWED = {
    "actions/checkout",
    "actions/setup-dotnet",
    "actions/setup-python",
    "actions/upload-artifact",
}
USE = re.compile(r"(?m)^\s*(?:-\s*)?uses:\s*([^\s#]+)")
PINNED = re.compile(r"^([A-Za-z0-9_-]+/[A-Za-z0-9_-]+)@([a-f0-9]{40})$")
STEP_START = re.compile(r"(?m)^      - name: (.+)$")
PRIVATE_GATES = (
    "github.event_name == 'workflow_dispatch'",
    "inputs.private_runtime_probe",
    "github.event.repository.private == true",
    "github.ref == 'refs/heads/experiment/dotnet10'",
)

def audit_text(text: str, *, name: str = "windows-ci.yml") -> list[str]:
    findings: list[str] = []
    if "\t" in text:
        findings.append(f"{name}: tabs forbidden in workflow YAML")
    if not text.startswith("name: "):
        findings.append(f"{name}: workflow must declare a name")
    if re.search(r"(?m)^  pull_request_target:\s*$", text):
        findings.append(f"{name}: pull_request_target forbidden for OSS build")
    for match in USE.finditer(text):
        ref = match.group(1).strip()
        pinned = PINNED.fullmatch(ref)
        if not pinned:
            findings.append(f"{name}: unpinned external action: {ref}")
        elif pinned.group(1) not in ALLOWED:
            findings.append(f"{name}: unreviewed external action: {ref}")
    if not re.search(r"(?m)^permissions:\n  contents: read\s*$", text):
        findings.append(f"{name}: require minimum workflow permissions")
    boundaries=list(STEP_START.finditer(text))
    for i, match in enumerate(boundaries):
        start=match.start()
        stop=boundaries[i+1].start() if i+1<len(boundaries) else len(text)
        block=text[start:stop]
        title=match.group(1).strip()
        restricted = (
            "upload-artifact@" in block or
            "Build opt-in private local Windows test bundle" in title
        )
        if restricted:
            guard = re.search(r"(?m)^        if:\s*(.+)$", block)
            if guard is None:
                findings.append(f"{name}: missing guard for private upload step: {title}")
            else:
                expression = guard.group(1).strip()
                if not (expression.startswith("${{") and expression.endswith("}}")):
                    findings.append(f"{name}: malformed private artifact guard: {title}")
                else:
                    clauses = [part.strip() for part in expression[3:-2].split("&&")]
                    if len(clauses) != len(PRIVATE_GATES) or set(clauses) != set(PRIVATE_GATES):
                        findings.append(f"{name}: private artifact guard must enforce all four AND gates: {title}")
            if "upload-artifact@" in block:
                if "retention-days: 1" not in block:
                    findings.append(f"{name}: private experiment artifacts must expire in one day")
    # Executable workspaces must never default to a public binary release,
    # even when the source repository transitions to public.
    if re.search(r"(?m)^\s*uses:\s*softprops/action-gh-release@",text):
        findings.append(f"{name}: unreviewed release action")
    return findings

def audit(root: Path) -> tuple[list[str], int]:
    workflows=root/".github"/"workflows"
    if not workflows.is_dir():
        return ["Missing GitHub workflow directory"], 0
    paths=sorted(list(workflows.glob("*.yml"))+list(workflows.glob("*.yaml")))
    if not paths:
        return ["No workflow YAML files"], 0
    findings=[]
    for path in paths:
        try:
            body=path.read_text(encoding="utf-8")
        except UnicodeError:
            findings.append(path.name+": workflow is not UTF-8")
            continue
        findings.extend(audit_text(body,name=path.name))
    return findings,len(paths)

def main() -> int:
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument("--root",type=Path,default=Path(__file__).resolve().parent.parent)
    args=p.parse_args()
    issues,count=audit(args.root)
    print("GITHUB_ACTIONS_WORKFLOWS_CHECKED="+str(count))
    for f in issues:print("WORKFLOW_SECURITY_BLOCKER:",f)
    print("GITHUB_ACTIONS_IMMUTABLE_ACTIONS_AND_PRIVATE_ARTIFACT_GATES_"+("BLOCKED" if issues else "PASS"))
    return int(bool(issues))

if __name__=="__main__":
    raise SystemExit(main())
