#!/usr/bin/env bash
#
# Watch the per-account submission limit refuse a request (#266).
#
#   ./scripts/verify-rate-limit.sh                 # generates a fixture, uses limit 2
#   ./scripts/verify-rate-limit.sh --limit 5
#   ./scripts/verify-rate-limit.sh --file /tmp/fx/prior_notes.docx
#   TOKEN=... ./scripts/verify-rate-limit.sh       # non-interactive
#
# At the default of 60/hour the limit is invisible to a single operator —
# which is intended, and is why this script exists: the only practical way to
# exercise the refusal path in production is to lower the limit first.
#
#   az functionapp config appsettings set --name canada-east-ai-function \
#       --resource-group consultologist_group --settings RateLimits__SubmissionsPerHour=2
#   # and afterwards:
#   az functionapp config appsettings delete --name canada-east-ai-function \
#       --resource-group consultologist_group --setting-names RateLimits__SubmissionsPerHour
#
# WHAT IT ASSERTS. The window is fixed and aligned to the UTC hour, so this
# cannot assume a clean slate: a previous run in the same hour has already
# spent budget, and the very first request here may legitimately be refused.
# So the invariants checked are the ones that hold either way:
#
#   1. a 429 arrives within limit+1 requests
#   2. it carries a Retry-After that is positive and no further away than
#      the next UTC hour
#   3. once refused, every later request in this run is refused too — a
#      refusal that spent budget would starve the account on retry, so a
#      success after a 429 is a real defect, not a flake
#   4. every success precedes every refusal
#
# It reports how many succeeded, which says how much budget the hour had
# left. Fewer than `limit` is not a failure — it means something already
# spent some, and the script says so rather than crying wolf.
#
# Nothing is persisted by any of this: DocumentExtractions is a preview
# endpoint (docs/DOCUMENT_INPUT.md § 5). The only trace left behind is the
# account's counter row in the AccountRateLimits table.

set -uo pipefail

API="${API:-https://canada-east-ai-function-gmenbbe9erewh4bj.canadaeast-01.azurewebsites.net/api/DocumentExtractions}"
LIMIT=2
FILE=""
FIXTURE_DIR="${FIXTURE_DIR:-/tmp/consultologist-fixtures}"

while [ $# -gt 0 ]; do
    case "$1" in
        --limit) LIMIT="${2:?--limit needs a number}"; shift 2 ;;
        --file)  FILE="${2:?--file needs a path}"; shift 2 ;;
        --api)   API="${2:?--api needs a URL}"; shift 2 ;;
        -h|--help) sed -n '2,40p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "unknown argument: $1" >&2; exit 2 ;;
    esac
done

case "$LIMIT" in
    ''|*[!0-9]*) echo "--limit must be a positive integer" >&2; exit 2 ;;
    0) echo "--limit 0 disables the limiter; there is nothing to refuse" >&2; exit 2 ;;
esac

# The document is incidental — any readable file exercises the same path,
# because the limit is checked before the body is even buffered. A generated
# fixture keeps the script self-contained (#256: fixtures are generated, never
# committed).
if [ -z "$FILE" ]; then
    FILE="$FIXTURE_DIR/prior_notes.docx"
    if [ ! -f "$FILE" ]; then
        echo "generating a fixture in $FIXTURE_DIR"
        mkdir -p "$FIXTURE_DIR"
        repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
        dotnet run --file "$repo_root/scripts/make-input-fixtures.cs" -- "$FIXTURE_DIR" >/dev/null \
            || { echo "fixture generation failed — pass --file instead" >&2; exit 1; }
    fi
fi

[ -f "$FILE" ] || { echo "no such file: $FILE" >&2; exit 1; }

TOKEN="${TOKEN:-}"
if [ -z "$TOKEN" ]; then
    read -rsp "Bearer token (not echoed): " TOKEN
    echo
fi
[ -n "$TOKEN" ] || { echo "no token supplied" >&2; exit 1; }

# Check the token before spending requests on it — an access token lasts about
# an hour, and a bare 401 does not say that it went stale. Decoded locally;
# the token is never printed. Same check show-extraction.sh makes.
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

attempts=$((LIMIT + 1))
window_left=$(python3 -c 'import time; print(3600 - int(time.time()) % 3600)')

printf '\nlimit %s · %s attempts · %s\n' "$LIMIT" "$attempts" "$(basename "$FILE")"
printf 'the current UTC window resets in %dm %ds\n\n' "$((window_left / 60))" "$((window_left % 60))"

successes=0
first_refusal=0
success_after_refusal=0
retry_after=""
sentence=""
status_line=""

for i in $(seq 1 "$attempts"); do
    headers="$(mktemp)"
    body="$(curl -sS -X POST "$API" \
        -H "Authorization: Bearer $TOKEN" \
        -H "Content-Type: application/octet-stream" \
        --data-binary @"$FILE" \
        -D "$headers" \
        -o - -w $'\n%{http_code}' 2>/dev/null)"

    status="${body##*$'\n'}"
    payload="${body%$'\n'*}"

    case "$status" in
        200)
            if [ "$first_refusal" -ne 0 ]; then
                success_after_refusal=1
                printf '  %s. \033[31m200 — accepted AFTER a refusal\033[0m\n' "$i"
            else
                successes=$((successes + 1))
                # Both casings. The success payload is a record, so it
                # serializes PascalCase ("Text"); the refusal payload is an
                # anonymous object declared `new { error = ... }` and really
                # is lowercase. Reading only one of them silently reports
                # zero characters for a document that extracted fine.
                chars="$(printf '%s' "$payload" | python3 -c '
import json, sys
d = json.load(sys.stdin)
print(len(d.get("Text") or d.get("text") or ""))' 2>/dev/null || echo '?')"
                printf '  %s. \033[32m200\033[0m  extracted %s characters\n' "$i" "$chars"
            fi
            ;;
        429)
            [ "$first_refusal" -eq 0 ] && first_refusal=$i
            this_retry="$(grep -i '^retry-after:' "$headers" | tr -d '\r' | awk '{print $2}' | head -1)"
            [ -z "$retry_after" ] && retry_after="$this_retry"
            this_sentence="$(printf '%s' "$payload" | python3 -c '
import json, sys
d = json.load(sys.stdin)
print(d.get("error") or d.get("Error") or "")' 2>/dev/null)"
            [ -z "$sentence" ] && sentence="$this_sentence"
            printf '  %s. \033[33m429\033[0m  Retry-After: %ss\n' "$i" "${this_retry:-<missing>}"
            ;;
        401)
            printf '  %s. \033[31m401\033[0m — the token was not accepted (expired, wrong scope, or not for this API)\n' "$i"
            exit 1
            ;;
        403)
            printf '  %s. \033[31m403\033[0m — the token is valid but the account is not Active\n' "$i"
            exit 1
            ;;
        *)
            printf '  %s. \033[31m%s\033[0m  %s\n' "$i" "$status" "$(printf '%s' "$payload" | head -c 200)"
            status_line="unexpected"
            ;;
    esac

    rm -f "$headers"
done

echo
[ -n "$sentence" ] && printf 'the sentence a clinician sees:\n  \033[1m%s\033[0m\n\n' "$sentence"

fail() { printf '\033[31mFAIL\033[0m  %s\n' "$1"; exit 1; }

[ "$status_line" = "unexpected" ] && fail "an unexpected status came back; see above"
[ "$first_refusal" -eq 0 ] && fail "no 429 in $attempts attempts — is RateLimits__SubmissionsPerHour actually $LIMIT?"
[ "$success_after_refusal" -eq 1 ] && fail "a request succeeded after a refusal; a refused acquire must not spend budget"
[ -z "$retry_after" ] && fail "the 429 carried no Retry-After header"

case "$retry_after" in
    ''|*[!0-9]*) fail "Retry-After was not a number of seconds: '$retry_after'" ;;
esac
[ "$retry_after" -lt 1 ] && fail "Retry-After was $retry_after; it must be at least 1"
[ "$retry_after" -gt 3600 ] && fail "Retry-After was ${retry_after}s, beyond the one-hour window"

printf '\033[32mPASS\033[0m  refused at attempt %s of %s, Retry-After %ss\n' \
    "$first_refusal" "$attempts" "$retry_after"

if [ "$successes" -lt "$LIMIT" ]; then
    printf '      %s of %s succeeded — the rest of this hour'\''s budget was already spent\n' "$successes" "$LIMIT"
    printf '      (a run earlier this hour, or the app itself). Not a failure: the window\n'
    printf '      is fixed to the UTC hour and resets in %dm.\n' "$((window_left / 60))"
fi

printf '\nthe counter row this left behind:\n'
printf '  az storage entity query --account-name consultjobrecscaeast --auth-mode login \\\n'
printf '      --table-name AccountRateLimits --query "items[].{account:PartitionKey,window:RowKey,count:Count}"\n'
