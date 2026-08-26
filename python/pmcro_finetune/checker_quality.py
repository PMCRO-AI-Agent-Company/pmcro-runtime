"""Deterministic semantic quality gate for Checker SFT rows."""
from __future__ import annotations

import argparse
import json
import re
import sys
from dataclasses import dataclass
from pathlib import Path

SELF_MUTATION = re.compile(r"\b(?:(?:i\s+(?:will|can|am\s+going\s+to)|i['’]ll|let\s+me)\s+(?:fix|modify|change|create|delete|execute|run|implement|apply|edit|write|patch|remove|add|update|handle|take\s+care\s+of)|(?:i(?:\s+am|['’]m))\s+(?:fixing|modifying|changing|creating|deleting|executing|running|implementing|applying|editing|writing|patching|removing|adding|updating)|i\s+(?:fixed|modified|changed|created|deleted|executed|ran|implemented|applied|edited|wrote|patched|removed|added|updated)|(?:i\s+(?:approve|authorize)|i['’]ll\s+(?:approve|authorize)|let\s+me\s+(?:approve|authorize)))\b", re.IGNORECASE)
STATUS = re.compile(r"\b(?:PASS|FAIL)\b", re.IGNORECASE)
EVIDENCE = re.compile(r"\bevidence\b", re.IGNORECASE)

@dataclass(frozen=True)
class Finding:
    code: str
    message: str

def assistant_content(row: dict) -> str:
    messages = row.get("messages")
    if not isinstance(messages, list): return ""
    for message in reversed(messages):
        if isinstance(message, dict) and message.get("role") == "assistant":
            content = message.get("content")
            return content if isinstance(content, str) else ""
    return ""

def validate_checker_row(row: dict) -> list[Finding]:
    if row.get("role") != "checker": return []
    content = assistant_content(row)
    if not content.strip(): return [Finding("CHECKER_EMPTY", "Checker assistant content is empty.")]
    findings: list[Finding] = []
    if not STATUS.search(content): findings.append(Finding("CHECKER_NO_STATUS", "Checker output must contain PASS or FAIL."))
    if not EVIDENCE.search(content): findings.append(Finding("CHECKER_NO_EVIDENCE", "Checker output must explicitly carry evidence."))
    match = SELF_MUTATION.search(content)
    if match: findings.append(Finding("CHECKER_SELF_MUTATION", f"Checker contains self-directed mutation/approval language: {match.group(0)!r}."))
    return findings

def validate_jsonl(path: Path) -> tuple[int, list[dict]]:
    findings: list[dict] = []
    rows = 0
    with path.open(encoding="utf-8") as handle:
        for line_number, line in enumerate(handle, 1):
            if not line.strip(): continue
            rows += 1
            try: row = json.loads(line)
            except json.JSONDecodeError as exc:
                findings.append({"line": line_number, "code": "JSON_INVALID", "message": str(exc)}); continue
            for finding in validate_checker_row(row): findings.append({"line": line_number, "code": finding.code, "message": finding.message})
    return rows, findings

def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Validate Checker-role SFT semantics")
    parser.add_argument("--data", type=Path, required=True)
    args = parser.parse_args(argv)
    if not args.data.exists(): print(f"FAIL: data missing: {args.data}", file=sys.stderr); return 2
    rows, findings = validate_jsonl(args.data)
    if findings:
        print(f"FAIL: {len(findings)} Checker quality findings across {rows} rows", file=sys.stderr)
        for finding in findings: print(json.dumps(finding, ensure_ascii=False), file=sys.stderr)
        return 1
    print(f"PASS: Checker semantic quality gate validated {rows} rows")
    return 0

if __name__ == "__main__": raise SystemExit(main())
