#!/usr/bin/env bash
#
# What a consult was actually built from (#295).
#
#   ./scripts/show-job-provenance.sh <job-id> [<job-id> …]
#   TOKEN=... ./scripts/show-job-provenance.sh <job-id>
#
# Read-only: one GET per job. It starts nothing, costs nothing, and changes
# nothing.
#
# WHY THIS EXISTS. The provenance record is durable and has always been
# readable — it lives on the job's durable entity, GET
# /api/ConsultGenerationJobs/{id} serves it, and History renders it as the
# Provenance panel. What was missing was a way to reach it from a terminal.
#
# During #294's verification that gap cost several rounds: the events table
# was reachable from the CLI and empty, so it looked like nothing had been
# recorded. ConsultGenerationJobEvents is written only by the SSE endpoint —
# it is browser-resume data, not an audit trail — and a job nobody streamed
# correctly has no rows. The answer was on the job's own History page the
# whole time.
#
# WHAT TO READ IT FOR. "Which documents did this consult read?" An input with
# an origin was read from a file. An input with NO origin was not: it came
# from typed text or an email body, or it never arrived at all. That absence
# is the signal every defect in Milestone 16 turned on — #290, #291 and #294
# were each diagnosed from exactly this.

set -uo pipefail

API="${API:-https://canada-east-ai-function-gmenbbe9erewh4bj.canadaeast-01.azurewebsites.net/api/ConsultGenerationJobs}"

# The job response carries InputOrigins but NOT the declared input list, so
# origins alone cannot show what is missing — and what is missing is the
# whole point. The package manifest has the declaration, and repo-owned
# packages are anonymously readable, so the ref in the response is enough.
REGISTRY="${REGISTRY:-https://consultpubcaeast.blob.core.windows.net/workflow-packages}"

case "${1:-}" in
    -h|--help)
        sed -n '2,27p' "$0" | sed 's/^# \{0,1\}//'
        exit 0
        ;;
    "")
        echo "usage: $0 <job-id> [<job-id> …]" >&2
        exit 2
        ;;
esac

TOKEN="${TOKEN:-}"
if [ -z "$TOKEN" ]; then
    read -rsp "Bearer token (not echoed): " TOKEN
    echo
fi
[ -n "$TOKEN" ] || { echo "no token supplied" >&2; exit 1; }

# Check the token before spending a request on it. An access token lasts about
# an hour, so the usual cause of a 401 here is simply that it went stale — and
# a bare 401 does not say so. Decoded locally; the token is never printed.
printf '%s' "$TOKEN" | python3 -c '
import base64, json, sys, time

parts = sys.stdin.read().strip().split(".")
if len(parts) != 3:
    print("  \033[31mthat does not look like a JWT\033[0m — check what was pasted")
    raise SystemExit(1)

try:
    claims = json.loads(base64.urlsafe_b64decode(parts[1] + "=" * (-len(parts[1]) % 4)))
except Exception:
    print("  \033[31mcould not decode the token payload\033[0m")
    raise SystemExit(1)

exp = claims.get("exp")
left = int(exp - time.time()) if exp else None
scope = claims.get("scp") or " ".join(claims.get("roles", [])) or "?"

if left is None:
    print("  token carries no exp claim")
elif left <= 0:
    print(f"  \033[31mtoken expired {abs(left) // 60} min ago\033[0m — fetch a fresh one")
    raise SystemExit(1)
else:
    print(f"  token valid for {left // 60} min · scope: {scope}")
' || exit 1

# A start returns 202 before the entity has processed its Initialize signal,
# so an immediate read can come back without provenance. Waiting on
# EffectiveInputHash is what separates "not ready yet" from "recorded
# nothing" — without it those two look identical, which is the distinction
# this script exists to make. Same rule as verify-document-provenance.sh.
JOB_JSON=""
fetch_job() {
    local job="$1" tries="${2:-10}" i
    for (( i = 0; i < tries; i++ )); do
        JOB_JSON="$(curl -sS "$API/$job" -H "Authorization: Bearer $TOKEN" 2>/dev/null)"

        if printf '%s' "$JOB_JSON" \
            | python3 -c 'import json,sys; sys.exit(0 if json.load(sys.stdin).get("EffectiveInputHash") else 1)' 2>/dev/null
        then
            return 0
        fi

        sleep 1
    done
    return 1
}

# Defined once, in a quoted heredoc: the renderer uses both quote styles and
# nesting it inside a shell string is how the first draft broke.
RENDER=$(cat <<'PY'
import json, sys

DASH = "\u2014"
d = json.load(sys.stdin)

def line(label, value):
    print("  {:<22} {}".format(label, value if value not in (None, "") else DASH))

line("status", d.get("Status"))
line("source", d.get("Source"))
line("created", (d.get("CreatedAtUtc") or "")[:19])
line("completed", (d.get("CompletedAtUtc") or "")[:19])
line("package", d.get("WorkflowPackage"))
line("input hash", "{} (v{})".format(d.get("EffectiveInputHash"), d.get("EffectiveInputHashVersion")))

import os

origins = d.get("InputOrigins") or {}

# The declaration comes from the package manifest, not the job response --
# the response has no Inputs key, so origins alone can only show what WAS
# read and never what was not. Naming the absence is the point.
required = {}
for row in (os.environ.get("DECLARED") or "").splitlines():
    if "\t" in row:
        name, kind = row.split("\t", 1)
        required[name] = kind

declared = sorted(set(list(required) + list(origins)))

print()
if not declared:
    print("  no inputs recorded")
else:
    if not required:
        print("  inputs (declaration unavailable - only inputs read from a")
        print("          document are listed; an absent one cannot be shown)")
    else:
        print("  inputs")
    for name in declared:
        o = origins.get(name)
        kind = required.get(name)
        label = name if kind is None else "{} ({})".format(name, kind)
        if o:
            bits = ["read from a document by " + (o.get("Extractor") or DASH)]
            if o.get("PageCount") is not None:
                bits.append("{} page(s)".format(o["PageCount"]))
            if o.get("TrackedChangesResolved"):
                bits.append("tracked changes resolved")
            print("    \033[32m{:<30}\033[0m {}".format(label, " \u00b7 ".join(bits)))
        else:
            # The absence IS the finding: typed text, an email body, or a
            # document that never arrived. #290, #291 and #294 all turned on
            # exactly this line.
            print("    \033[33m{:<30}\033[0m no document origin \u2014 typed, from an email body, or never arrived".format(label))
PY
)

# Declared inputs for a package ref like "example-two-documents@v2026.07.1",
# newline separated. Empty when the ref is an acct-* fork (private registry,
# no anonymous read) or the fetch fails — the render then says so rather than
# implying the declaration was empty.
declared_inputs() {
    local ref="$1" name version
    name="${ref%@*}"
    version="${ref#*@}"

    case "$ref" in
        ""|acct-*) return 0 ;;
    esac

    curl -sS --max-time 10 "$REGISTRY/$name/$version/manifest.json" 2>/dev/null | python3 -c '
import json, sys
try:
    m = json.load(sys.stdin)
except Exception:
    raise SystemExit(0)
for i in m.get("inputs") or []:
    print("{}\t{}".format(i.get("id"), "required" if i.get("required") else "optional"))
' 2>/dev/null
}

status=0

for job in "$@"; do
    printf '\n\033[1m%s\033[0m\n' "$job"
    printf '%.0s─' {1..72}; echo

    if ! fetch_job "$job"; then
        # Distinguish the three ways this can fail, because "no output" is
        # exactly the ambiguity that sent #294's investigation sideways.
        code="$(curl -sS -o /dev/null -w '%{http_code}' "$API/$job" -H "Authorization: Bearer $TOKEN" 2>/dev/null)"
        case "$code" in
            401) printf '  \033[31mnot accepted (401)\033[0m — the token is stale, wrong scope, or not for this API\n' ;;
            403) printf '  \033[31mforbidden (403)\033[0m — valid token, account not Active\n' ;;
            404) printf '  \033[31mno such job (404)\033[0m — check the id\n' ;;
            *)   printf '  \033[31mno provenance after 10s (HTTP %s)\033[0m — still initializing, or it recorded none\n' "$code" ;;
        esac
        status=1
        continue
    fi

    pkg="$(printf %s "$JOB_JSON" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("WorkflowPackage") or "")' 2>/dev/null)"
    printf %s "$JOB_JSON" | DECLARED="$(declared_inputs "$pkg")" python3 -c "$RENDER"
done

echo
exit $status
