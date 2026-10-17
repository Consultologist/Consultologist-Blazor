#!/usr/bin/env bash
#
# Show what the parser reads out of a document — the same text a consult
# would be generated from.
#
#   ./scripts/show-extraction.sh prior_notes.docx
#   ./scripts/show-extraction.sh *.pdf *.docx
#   ./scripts/show-extraction.sh            # every fixture in the working directory
#   TOKEN=... ./scripts/show-extraction.sh file.pdf
#
# Fixtures are generated, not committed:
#
#   dotnet run --file scripts/make-pdf-fixtures.cs -- <dir>
#   dotnet run --file scripts/make-input-fixtures.cs -- <dir>
#
# Why this is the right way to answer "what did the job actually read":
# History deliberately stores hashes and origins, never input text, and the
# run-phase draft bar keeps it only in the submitting tab. So after a refresh,
# or for anything submitted by email, there is no read path to the extracted
# text. Extraction is deterministic for the same bytes and the same pinned
# extractor, so running the file back through this endpoint reproduces exactly
# what the job used.
#
# Nothing is persisted by this call. It is a preview endpoint.

set -uo pipefail

API="https://canada-east-ai-function-gmenbbe9erewh4bj.canadaeast-01.azurewebsites.net/api/DocumentExtractions"

files=("$@")

# With no arguments, sweep the working directory — not the script's own, which
# is where this looked before it moved into the repository (#256). The
# fixtures are generated output and do not live beside the script; only their
# source text does, under scripts/fixtures.
if [ ${#files[@]} -eq 0 ]; then
    mapfile -t files < <(find "$PWD" -maxdepth 1 -type f \
        \( -name '*.pdf' -o -name '*.docx' -o -name '*.vtt' -o -name '*.srt' -o -name 'consult_draft.txt' -o -name 'prior_notes*.txt' \) | sort)
    [ ${#files[@]} -gt 0 ] || { echo "no documents found in $PWD — pass one as an argument" >&2; exit 1; }
fi

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

raw = sys.stdin.read().strip()
parts = raw.split(".")
if len(parts) != 3:
    print("  \033[31mthat does not look like a JWT\033[0m — check what was pasted")
    raise SystemExit(1)

def seg(p):
    return json.loads(base64.urlsafe_b64decode(p + "=" * (-len(p) % 4)))

try:
    claims = seg(parts[1])
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

# A hint only. The parser dispatches on content, so a wrong value here changes
# nothing — that is the point of the seam (docs/DOCUMENT_INPUT.md § 1).
content_type() {
    case "${1,,}" in
        *.pdf)  echo "application/pdf" ;;
        *.docx) echo "application/vnd.openxmlformats-officedocument.wordprocessingml.document" ;;
        *.txt|*.md) echo "text/plain" ;;
        *)      echo "application/octet-stream" ;;
    esac
}

for file in "${files[@]}"; do
    [ -f "$file" ] || { printf '\n\033[31m%s — no such file\033[0m\n' "$file"; continue; }

    printf '\n\033[1m%s\033[0m  (%s bytes, sent as %s)\n' \
        "$(basename "$file")" "$(stat -c%s "$file")" "$(content_type "$file")"
    printf '%.0s─' {1..72}; echo

    out="$(curl -sS -X POST "$API" \
        -H "Authorization: Bearer $TOKEN" \
        -H "Content-Type: $(content_type "$file")" \
        --data-binary @"$file" \
        -w $'\n%{http_code}' 2>/dev/null)"

    # curl appends "\n<status>" last, so the status follows the final newline.
    status="${out##*$'\n'}"
    body="${out%$'\n'*}"

    if [ "$status" = "401" ]; then
        # CreateUnauthorizedResponse sends no body, so there is nothing to
        # print — say what it means instead of showing a blank line.
        printf '  \033[31mrejected (401)\033[0m — the token was not accepted.\n'
        printf '  Expired, wrong scope, or not for this API. A fresh one is the usual fix.\n'
        continue
    fi

    if [ "$status" = "403" ]; then
        printf '  \033[31mforbidden (403)\033[0m — the token is valid but the account is not Active.\n'
        continue
    fi

    if [ "$status" != "200" ]; then
        printf '  \033[31mrefused (HTTP %s)\033[0m\n' "$status"
        printf '%s' "$body" | python3 -c '
import json, sys
raw = sys.stdin.read()
try:
    d = json.loads(raw)
except Exception:
    print("  " + (raw.strip() or "(no body)")); raise SystemExit
print("  outcome: " + str(d.get("outcome")))
print("  " + str(d.get("error")))
'
        continue
    fi

    printf '%s' "$body" | python3 -c '
import json, sys, unicodedata

d = json.load(sys.stdin)
text = d.get("Text") or ""

extractor = d.get("Extractor")
pages = d.get("PageCount")

print("  extractor:  " + str(extractor))
if pages is not None:
    print("  pages:      " + str(pages))
print("  characters: " + str(len(text)))

# Control characters are worth surfacing rather than rendering invisibly: a
# stray CR or an unmapped glyph reads fine on screen and is wrong in the
# record. Tab and newline are excluded because we emit them on purpose —
# tabs are table cell boundaries — and flagging those would cry wolf on
# every DOCX and bury the ones that matter.
ctrl = sorted({c for c in text if unicodedata.category(c) == "Cc" and c not in "\n\t"})
if ctrl:
    print("  \033[31mcontrol characters: " + ", ".join(f"U+{ord(c):04X}" for c in ctrl) + "\033[0m")
else:
    print("  control characters: none besides newline and tab")

print()
for line in text.splitlines():
    # Tabs are real structure here — table cell boundaries — so show them.
    print("    " + line.replace("\t", " \033[2m→\033[0m "))
'
done

echo
