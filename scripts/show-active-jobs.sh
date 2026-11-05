#!/usr/bin/env bash
#
# List the consult jobs still active, and say whether a release may be cut (#1302).
#
#   HOST_ACCOUNT=<host> INDEX_ACCOUNT=<records> ./scripts/show-active-jobs.sh
#   HOST_ACCOUNT=<host> INDEX_ACCOUNT=<records> ./scripts/show-active-jobs.sh --window 60
#
# The two storage accounts are given, not built in (this repository is public,
# #397): HOST_ACCOUNT is the one AzureWebJobsStorage names (the Durable task
# hub), INDEX_ACCOUNT the one ConsultGenerationJobIndexStorage resolves to —
# both in docs/CONFIGURATION.md.
#
# Read-only, with your own az login (--auth-mode login, never an account key).
#
# WHAT COUNTS. The Durable task hub's own instance table is the truth about
# what is running: every ConsultGenerationOrchestrator instance whose
# RuntimeStatus is Running or Pending (on the host account; hub
# consultologistjobs). The job index (ConsultGenerationJobIndex) is read
# only for each live job's status and, for a scheduled one, its wake time.
# The index alone is NOT enough: it holds rows left at Queued by starts whose
# orchestration was never scheduled, which no drain would ever see finish.
# Those are listed as "no orchestration" and not counted.
#
# It prints one line per job (id, status, source, wake time) and no user data
# beyond the ids, then one verdict:
#
#   Safe to release              nothing running, and no scheduled job wakes
#                                within the window
#   Wait: N job(s) running or due
#
# A scheduled job that wakes AFTER the window is listed but does not hold the
# release: it is asleep on a durable timer and wakes on the new code, which is
# safe while payload changes stay append-only (docs/DURABLE_JOBS.md,
# "Draining before a release"). Set the hold (Jobs__HoldNewJobs=true) first,
# or new jobs keep arriving while you wait.

set -uo pipefail

HUB="${HUB:-consultologistjobs}"
INDEX_TABLE="${INDEX_TABLE:-ConsultGenerationJobIndex}"
WINDOW=30

while [ $# -gt 0 ]; do
    case "$1" in
        --window) WINDOW="${2:?--window needs a number of minutes}"; shift 2 ;;
        -h|--help) sed -n '2,36p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "unknown argument: $1" >&2; exit 2 ;;
    esac
done

case "$WINDOW" in
    ''|*[!0-9]*) echo "--window must be a whole number of minutes" >&2; exit 2 ;;
esac

HOST_ACCOUNT="${HOST_ACCOUNT:?set HOST_ACCOUNT to the AzureWebJobsStorage account (docs/CONFIGURATION.md)}"
INDEX_ACCOUNT="${INDEX_ACCOUNT:?set INDEX_ACCOUNT to the job index account (docs/CONFIGURATION.md)}"

# The CLI's table decoder crashes on a --select column a row lacks, so every
# query below projects with --query instead.
live=$(az storage entity query \
    --account-name "$HOST_ACCOUNT" --auth-mode login --table-name "${HUB}Instances" \
    --filter "Name eq 'ConsultGenerationOrchestrator' and (RuntimeStatus eq 'Running' or RuntimeStatus eq 'Pending')" \
    --num-results 1000 \
    --query "items[].{JobId:PartitionKey,Runtime:RuntimeStatus}" --output json) \
    || { echo "the task hub could not be read (az login? RBAC on $HOST_ACCOUNT?)" >&2; exit 1; }

index=$(az storage entity query \
    --account-name "$INDEX_ACCOUNT" --auth-mode login --table-name "$INDEX_TABLE" \
    --filter "Status eq 'Running' or Status eq 'Queued' or Status eq 'Scheduled'" \
    --num-results 1000 \
    --query "items[].{JobId:JobId,Status:Status,Source:Source,ScheduledAtUtc:ScheduledAtUtc}" --output json) \
    || { echo "the job index could not be read (az login? RBAC on $INDEX_ACCOUNT?)" >&2; exit 1; }

LIVE="$live" INDEX="$index" WINDOW="$WINDOW" python3 -I - <<'PY'
import json, os
from datetime import datetime, timedelta, timezone

live = {r["JobId"]: r for r in json.loads(os.environ["LIVE"] or "[]")}
index = {r["JobId"]: r for r in json.loads(os.environ["INDEX"] or "[]")}
window = timedelta(minutes=int(os.environ["WINDOW"]))
now = datetime.now(timezone.utc)

def when(value):
    return datetime.fromisoformat(str(value).replace("Z", "+00:00")) if value else None

holding = 0
for job_id in sorted(live):
    row = index.get(job_id, {})
    status = row.get("Status") or live[job_id]["Runtime"]
    wake = when(row.get("ScheduledAtUtc"))
    if status == "Scheduled" and wake is not None and wake > now + window:
        note = f"wakes {wake:%Y-%m-%d %H:%MZ}, after the window (sleeps through the release)"
    else:
        holding += 1
        note = f"wakes {wake:%Y-%m-%d %H:%MZ}, within the window" if status == "Scheduled" and wake else "holds the release"
    print(f"{job_id:34} {status:10} {row.get('Source') or '-':8} {note}")

if not live:
    print("No job is running.")

orphans = sorted(set(index) - set(live))
if orphans:
    print()
    print(f"{len(orphans)} index row(s) say active but have no orchestration (not counted):")
    for job_id in orphans:
        print(f"  {job_id:34} {index[job_id].get('Status', '?')}")

print()
print("Safe to release" if holding == 0 else f"Wait: {holding} job(s) running or due")
PY
