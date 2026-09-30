#!/usr/bin/env python3
"""Check repository bootstrap metadata and the active OpenSpec package."""

import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
errors: list[str] = []

for relative in (
    "README.md", "ROADMAP.md", "HANDOFF.md", "AGENTS.md", "LICENSE",
    ".ai-rules/workflow.md", ".ai-rules/completion.md",
    "docs/assets/capture-plan.md",
):
    if not (ROOT / relative).is_file():
        errors.append(f"missing required file: {relative}")

try:
    project = json.loads((ROOT / ".project.json").read_text(encoding="utf-8"))
except (OSError, json.JSONDecodeError) as exc:
    errors.append(f"invalid .project.json: {exc}")
    project = {}

expected_id = ROOT.name
if project.get("id") != expected_id:
    errors.append(f"project id must match directory name {expected_id!r}")
metadata = project.get("discoverability", {})
for key in ("description", "keywords", "topics", "audience", "use_cases", "search_phrases", "homepage", "license_spdx", "visibility"):
    if not metadata.get(key):
        errors.append(f"discoverability.{key} is required")
if any(not re.fullmatch(r"[a-z0-9][a-z0-9-]{0,49}", topic) for topic in metadata.get("topics", [])):
    errors.append("discoverability topics must be normalized lowercase GitHub topics")
if project.get("deployment", {}).get("deployable") is not False:
    errors.append("deployment.deployable must remain false until release evidence exists")
if project.get("publication", {}).get("state") != "metadata_verified":
    errors.append("GitHub publication must be metadata_verified")

handoff = (ROOT / "HANDOFF.md").read_text(encoding="utf-8") if (ROOT / "HANDOFF.md").exists() else ""
match = re.search(r"^current_spec: ([a-z][a-z0-9]*(?:-[a-z0-9]+)*)$", handoff, re.MULTILINE)
if not match:
    errors.append("HANDOFF.md must contain exactly one canonical current_spec")
else:
    change = ROOT / "openspec" / "changes" / match.group(1)
    for relative in ("proposal.md", "design.md", "tasks.md"):
        if not (change / relative).is_file():
            errors.append(f"active OpenSpec package is missing {relative}")
    spec_dir = change / "specs"
    if not spec_dir.is_dir() or not list(spec_dir.rglob("spec.md")):
        errors.append("active OpenSpec package is missing a capability spec")

if errors:
    for error in errors:
        print(f"ERROR: {error}")
    raise SystemExit(1)
print(f"Bootstrap metadata and active OpenSpec package are present for {expected_id}.")
