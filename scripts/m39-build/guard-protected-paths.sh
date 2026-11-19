#!/usr/bin/env bash
# A Claude Code PreToolUse hook for the build server (see vps-claude-settings.json).
# It refuses, with exit 2 and a reason Claude reads:
#   - an Edit/Write/MultiEdit/NotebookEdit of a protected path;
#   - a Bash command that writes to a protected path, merges a PR, tags, releases,
#     force-pushes, or calls az.
# Protected paths: .github/, scripts/m39-build/, the build runbook.
# The driver's merge gate (run-areas.sh) is the control that holds; this hook stops the
# attempt early, inside the session, where the agent can still choose another way.
exec python3 -I -c '
import json, re, sys

event = json.load(sys.stdin)
tool = event.get("tool_name", "")
args = event.get("tool_input", {}) or {}

# A path starts the string or follows a separator: "/", whitespace, a quote, "=" or ":".
PROTECTED = re.compile(r"(^|[/\s\"=:])(\.github/|scripts/m39-build/|docs/customizable-workflow/package-format-v31/BUILD-RUNBOOK\.md)")

def refuse(reason):
    print(f"Refused by the M39 build guard: {reason} "
          "(BUILD-RUNBOOK.md, Standing rules). Record a block if the work needs it.", file=sys.stderr)
    sys.exit(2)

if tool in ("Edit", "Write", "MultiEdit", "NotebookEdit"):
    path = args.get("file_path") or args.get("notebook_path") or ""
    if PROTECTED.search(path):
        refuse(f"{path} is a protected path")

elif tool == "Bash":
    cmd = args.get("command", "")
    WRITES = re.compile(r"((?<![0-9&])>(?!&)|\btee\b|\bsed\s+(-\w*\s+)*-i|\bperl\s+-\w*i|\bmv\b|\bcp\b|\brm\b|\btruncate\b|\bchmod\b|\bpatch\b"
                        r"|\bgit\s+(add|mv|rm|checkout|restore|apply|am)\b)")
    if PROTECTED.search(cmd) and WRITES.search(cmd):
        refuse("the command writes to a protected path")
    if re.search(r"\bgh\s+pr\s+merge\b|pulls/\d+/merge\b", cmd):
        refuse("merging is the driver'"'"'s job")
    if re.search(r"\bgit\s+tag\b|\bgit\s+push\b.*(--tags|refs/tags/)|\bgh\s+release\b", cmd):
        refuse("tags and releases are the user'"'"'s")
    if re.search(r"\bgit\s+push\b.*(--force\b|--force-with-lease\b|\s-f\b|\s\+\S)", cmd):
        refuse("force-pushing rewrites shared history")
    if re.search(r"(^|[;&|(\s])az\s", cmd):
        refuse("Azure is out of scope for the build")

sys.exit(0)
'
