#!/usr/bin/env bash
# Drives the Milestone 39 build on the build server: one fresh Claude Code session per area
# (docs/customizable-workflow/package-format-v31/BUILD-RUNBOOK.md), and a merge gate in plain
# bash. The agent never merges; this script does, and only when the gate holds.
#
#   scripts/m39-build/run-areas.sh [first-area]     # e.g. 01; default 00
#
# Environment (all optional):
#   M39_ROOT          the main checkout            (default ~/Projects/Consultologist-Blazor)
#   M39_LOGS          session logs                 (default ~/m39-build-logs)
#   M39_STOP_FILE     touch it to stop after the current area (default ~/m39-build.stop)
#   M39_MAX_SESSIONS  sessions per area before giving up        (default 8)
#   M39_WAIT_MINUTES  pause after a session that failed, e.g. on a usage limit (default 30)
#   M39_CHECK_MINUTES how long to wait for a PR's checks        (default 120)
#   NTFY_TOPIC        if set, notifications also go to https://ntfy.sh/$NTFY_TOPIC
#
# Exit codes: 0 every area built; 3 needs the user (a block, or a refused PR); 4 an area ran out
# of sessions; 5 the setup is wrong.
#
# Every GitHub call is REST (gh api): gh's GraphQL commands fail on this repo (AGENTS.md).
set -euo pipefail

# Run from a private copy: bash reads a script as it runs, so an edit to the checkout's copy
# mid-run would change what runs next.
if [[ -z "${M39_DRIVER_COPY:-}" ]]; then
  copy=$(mktemp "${TMPDIR:-/tmp}/m39-run-areas.XXXXXX")
  cp "$0" "$copy" && chmod 500 "$copy"
  M39_DRIVER_COPY=1 exec bash "$copy" "$@"
fi

REPO="Consultologist/Consultologist-Blazor"
OWNER="${REPO%%/*}"
BASE="m39-build"
RUNBOOK="docs/customizable-workflow/package-format-v31/BUILD-RUNBOOK.md"
ROOT="${M39_ROOT:-$HOME/Projects/Consultologist-Blazor}"
LOGS="${M39_LOGS:-$HOME/m39-build-logs}"
STOP_FILE="${M39_STOP_FILE:-$HOME/m39-build.stop}"
MAX_SESSIONS="${M39_MAX_SESSIONS:-8}"
WAIT_MINUTES="${M39_WAIT_MINUTES:-30}"
CHECK_MINUTES="${M39_CHECK_MINUTES:-120}"
# A PR touching any of these is refused: CI, this driver, the agent's instructions and settings
# change by the user's hand only.
PROTECTED='(^(\.github/|scripts/m39-build/)|(^|/)\.claude/|(^|/)(AGENTS|CLAUDE(\.local)?)\.md$|^docs/customizable-workflow/package-format-v31/BUILD-RUNBOOK\.md$)'
AREAS=(00 01 02 03 04 05 06 07 08 09 10 11 12 13)

log()    { printf '%s  %s\n' "$(date -u +%FT%TZ)" "$*"; }
notify() {
  log "NOTIFY: $*"
  if [[ -n "${NTFY_TOPIC:-}" ]]; then
    curl -fsS -m 20 -H "Title: M39 build" -d "$*" "https://ntfy.sh/$NTFY_TOPIC" >/dev/null || true
  fi
}

# --- setup ----------------------------------------------------------------------------------
[[ -f "$ROOT/$RUNBOOK" ]] || { echo "No runbook at $ROOT/$RUNBOOK" >&2; exit 5; }
command -v claude >/dev/null || { echo "claude is not on PATH" >&2; exit 5; }
gh api "repos/$REPO" --jq .full_name >/dev/null || { echo "gh cannot reach $REPO" >&2; exit 5; }
mkdir -p "$LOGS"

git -C "$ROOT" fetch -q origin --prune
if [[ "$(git -C "$ROOT" branch --show-current)" == main ]] && git -C "$ROOT" diff --quiet; then
  git -C "$ROOT" merge -q --ff-only origin/main
fi
if ! git -C "$ROOT" ls-remote --exit-code --heads origin "$BASE" >/dev/null; then
  log "Creating $BASE from origin/main"
  git -C "$ROOT" push -q origin "origin/main:refs/heads/$BASE"
fi
# The gate is only a gate if nothing reaches m39-build around it: a ruleset must require a PR
# and status checks there, and forbid force pushes (the server's keys can push branches).
for branch in main "$BASE"; do
  rules=$(gh api "repos/$REPO/rules/branches/$branch" --jq '[.[].type] | join(",")')
  for rule in pull_request required_status_checks non_fast_forward; do
    if [[ ",$rules," != *",$rule,"* ]]; then
      echo "$branch has no '$rule' rule. Add a branch ruleset on $branch: require a pull request, require the 'test' check, block force pushes." >&2
      exit 5
    fi
  done
done

# The checkout's CI, driver, instructions and runbook must be exactly origin/main's: a session that edited
# them locally would otherwise change what the next session reads or what the hook enforces.
GUARDED=(.github .claude scripts/m39-build ':(glob)**/AGENTS.md' ':(glob)**/CLAUDE.md' ':(glob)**/CLAUDE.local.md' "$RUNBOOK")
untampered() {
  git -C "$ROOT" fetch -q origin --prune
  git -C "$ROOT" diff --quiet origin/main -- "${GUARDED[@]}" \
    && [[ -z "$(git -C "$ROOT" status --porcelain -- "${GUARDED[@]}")" ]]
}
untampered || { echo "$ROOT differs from origin/main in ${GUARDED[*]}." >&2; exit 5; }
for label in "m39-area:1d76db" "m39-build-parent:0e8a16" "m39-built:5319e7" "needs-decision:d93f0b"; do
  gh api -X POST "repos/$REPO/labels" -f name="${label%%:*}" -f color="${label##*:}" >/dev/null 2>&1 || true
done

# --- lookups --------------------------------------------------------------------------------
# The area's issue as "number label,label,…", or nothing before the issues are filed.
area_issue() {
  gh api --paginate "repos/$REPO/issues?labels=m39-area&state=all&per_page=100" \
    --jq ".[] | select(.pull_request == null) | select(.title | startswith(\"M39 build, area $1:\"))
          | \"\(.number) \([.labels[].name] | join(\",\"))\"" | head -1
}
# The area's open PR into m39-build as "number draft sha", or nothing.
area_pr() {
  gh api "repos/$REPO/pulls?state=open&base=$BASE&head=$OWNER:m39-area-$1" \
    --jq '.[0] // empty | "\(.number) \(.draft) \(.head.sha)"'
}
add_label() { gh api -X POST "repos/$REPO/issues/$1/labels" -f "labels[]=$2" >/dev/null; }
comment()   { gh api -X POST "repos/$REPO/issues/$1/comments" -f body="$2" >/dev/null; }

# --- the gate -------------------------------------------------------------------------------
# Returns 0 merged, 10 the area needs another session, 20 the user is needed.
GATE_REASON=""
gate() {
  local nn=$1 pr draft sha
  read -r pr draft sha <<<"$(area_pr "$nn")"
  [[ -n "${pr:-}" ]] || { GATE_REASON="no open PR"; return 10; }
  [[ "$draft" == false ]] || { GATE_REASON="PR #$pr is still a draft"; return 10; }

  local labels; labels=$(gh api "repos/$REPO/issues/$pr" --jq '[.labels[].name] | join(",")')
  [[ ",$labels," != *",needs-decision,"* ]] || { GATE_REASON="PR #$pr is labelled needs-decision"; return 20; }

  local touched
  touched=$(gh api --paginate "repos/$REPO/pulls/$pr/files?per_page=100" --jq '.[] | .filename, (.previous_filename // empty)' | grep -E "$PROTECTED" || true)
  if [[ -n "$touched" ]]; then
    GATE_REASON="PR #$pr changes protected paths: $(tr '\n' ' ' <<<"$touched")"; return 20
  fi

  # Wait for every check run on the head to complete; require `test`, and nothing red.
  local deadline=$(( $(date +%s) + CHECK_MINUTES * 60 )) runs pending
  while :; do
    runs=$(gh api --paginate "repos/$REPO/commits/$sha/check-runs?per_page=100" \
      --jq '.check_runs[] | "\(.name)\t\(.status)\t\(.conclusion)"')
    pending=$(awk -F'\t' '$2 != "completed"' <<<"$runs")
    if [[ -n "$runs" && -z "$pending" ]] && awk -F'\t' '$1 == "test" {found=1} END {exit !found}' <<<"$runs"; then break; fi
    if (( $(date +%s) > deadline )); then GATE_REASON="checks on PR #$pr did not finish in ${CHECK_MINUTES}m"; return 10; fi
    sleep 60
  done
  local red; red=$(awk -F'\t' '$3 != "success" && $3 != "skipped" && $3 != "neutral" {print $1"="$3}' <<<"$runs")
  [[ -z "$red" ]] || { GATE_REASON="PR #$pr has failing checks: $(tr '\n' ' ' <<<"$red")"; return 10; }

  # Merge exactly the commit that was checked; a newer push makes GitHub refuse.
  if ! gh api -X PUT "repos/$REPO/pulls/$pr/merge" -f merge_method=merge -f sha="$sha" >/dev/null 2>"$LOGS/merge-$pr.err"; then
    GATE_REASON="merging PR #$pr failed: $(head -c 300 "$LOGS/merge-$pr.err")"; return 10
  fi
  GATE_REASON="PR #$pr merged into $BASE at $sha"
  return 0
}

# --- sessions -------------------------------------------------------------------------------
run_session() {
  local nn=$1 n=$2 logfile="$LOGS/area-$1-$(date -u +%Y%m%dT%H%M%SZ).jsonl"
  log "Area $nn: session $n → $logfile"
  # Settings: the user's own, plus the guard hook from the checked checkout. Project and local
  # settings are not read, so a .claude/ the session writes cannot switch the hook off.
  local prompt="You are running area $nn of the Milestone 39 build, unattended. Read $RUNBOOK in full and follow it for area $nn until step 7's done line holds or you have recorded a block. Then stop."
  ( cd "$ROOT" && claude -p "$prompt" --dangerously-skip-permissions \
      --setting-sources user --settings "$ROOT/scripts/m39-build/vps-claude-settings.json" \
      --output-format stream-json --verbose ) >"$logfile" 2>&1
}

# --- the loop -------------------------------------------------------------------------------
start="${1:-00}"
started=false
for nn in "${AREAS[@]}"; do
  [[ "$nn" == "$start" ]] && started=true
  $started || continue

  sessions=0
  while :; do
    if [[ -e "$STOP_FILE" ]]; then notify "Stopped before area $nn: $STOP_FILE exists."; exit 0; fi
    if ! untampered; then
      notify "Area $nn: $ROOT differs from origin/main in ${GUARDED[*]}. Stopped."; exit 3
    fi

    read -r issue labels <<<"$(area_issue "$nn")"
    if [[ ",${labels:-}," == *",m39-built,"* ]]; then log "Area $nn: already built (#$issue)"; break; fi
    if [[ ",${labels:-}," == *",needs-decision,"* ]]; then
      notify "Area $nn is blocked: #$issue needs a decision."; exit 3
    fi

    if [[ -n "${issue:-}" ]]; then
      set +e; gate "$nn"; rc=$?; set -e
      case $rc in
        0)  log "Area $nn: $GATE_REASON"
            add_label "$issue" m39-built
            comment "$issue" "Built: $GATE_REASON."
            notify "Area $nn built: $GATE_REASON."
            git -C "$ROOT" fetch -q origin --prune
            break ;;
        20) notify "Area $nn needs you: $GATE_REASON."; exit 3 ;;
        *)  log "Area $nn: $GATE_REASON" ;;
      esac
    fi

    if (( sessions >= MAX_SESSIONS )); then
      notify "Area $nn is still unfinished after $sessions sessions ($GATE_REASON). Logs: $LOGS"; exit 4
    fi
    sessions=$((sessions + 1))
    if ! run_session "$nn" "$sessions"; then
      log "Area $nn: session $sessions ended with an error; waiting ${WAIT_MINUTES}m (a usage limit, most often)"
      sleep $((WAIT_MINUTES * 60))
    fi
  done
done

$started || { echo "Unknown first area: $start" >&2; exit 5; }
notify "All areas are built on $BASE. Review and merge $BASE into main when ready."
