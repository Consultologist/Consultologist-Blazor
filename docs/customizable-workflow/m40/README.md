# Milestone 40 planning

Planning for [Milestone 40: inputs to delivery, typed and verifiable](https://github.com/Consultologist/Consultologist-Blazor/milestone/40),
done **while Milestone 39 is being built**, and reconciled with what M39 actually built once it is in
`main`. This file is the handover for the agent doing the planning: read it, then `AGENTS.md`, before
anything else.

**Status (2026-11-21): set up, nothing planned yet.** Branch `m40-planning`, from `main` at
`3b0e707a`, in its own clone (`~/Projects/Consultologist/app/M40`).

## Scope

The milestone's description on GitHub is the source of truth; re-read it and its open issues at the
start of **every** session, since issues are added to it while M39 is built. On 2026-11-21 it held 64
open issues, in these groups (from the description):

- **The chain from inputs to delivery:** inputs (#1442 and its sub-issues #1430–#1437; #1429, #1451,
  #1452), extraction (#1438), production (#1439 and its sub-issues #1438, #1440, #1441; #1415, #1416),
  tool calls (#1445),
  formats (#1402–#1405), review (#1446), delivery (#1443), confidence (#1444), patient data (#1448).
- **Revisits of M39 decisions:** #1426, #1427, #1428, #1453, and the many "Revisit …" / "Review …"
  issues filed during M39's design and build (#1455–#1487, #1493, #1522–#1527).
- **Retiring old spec versions:** #1450 (placeholder).
- **Other follow-ups:** #1391, #1399, #1417, #1420, #1422, #1454.

The milestone's own sentence: it **starts after the Milestone 39 format has been tested.** Planning
now is fine; building is not.

## Rules for this branch

1. **New files only, in this folder** (`docs/customizable-workflow/m40/`). Leave the v31 design
   (`docs/customizable-workflow/package-format-v31/`) and every other existing file as it is: the M39
   build sessions read the v31 design, and a change on `main` reaches them.
2. **Never touch M39's protected paths:** `scripts/m39-build/`, the runbook
   (`package-format-v31/BUILD-RUNBOOK.md`), `.github/`. A change to them on `main` makes the build
   driver's tamper check stop the build.
3. **No code.** Planning only, until the owner says M40's building starts.
4. **The other agent.** Another Claude Code session (in `~/Projects/Consultologist/app/Consultologist-Blazor`)
   watches the M39 build and posts the owner's rulings on M39's area issues. When a ruling moves
   something to M40 it files an M40 issue saying "found during the M39 build". **This agent owns M40's
   planning; it does not post on M39's issues (#1496–#1510) or their PRs.**
5. **The branch's life (decided by the owner, 2026-11-21):** `m40-planning` lives on GitHub and
   collects the planning until M39 is done. No planning PR merges into `main` before then. After
   `m39-build` merges into `main`, a **reconciliation pass** runs on this branch (below). Then the
   branch merges into `main`: by a merge, or after a rebase or fast-forward, as the owner chooses
   then. The two branches change different files, so bringing `main` in should not conflict; if a
   conflict does appear, it means a rule above was broken, so stop and tell the owner.

## Reading what M39 is building

M39 builds into the integration branch **`m39-build`**, one area at a time; it is merged into `main`
only at the end. Read it without checking it out:

```bash
git fetch origin m39-build
git show origin/m39-build:<path>                    # one file as built
git diff --stat main origin/m39-build               # everything built so far
git log --oneline main..origin/m39-build            # the merges, one per area
```

| What | Where |
|---|---|
| the v31 design (what M39 implements) | `docs/customizable-workflow/package-format-v31/` (start with its `README.md`) |
| the build's parent issue, and the area issues 00–13 | #1496; #1497 (area 00) … #1510 (area 13) |
| each area's plan | `package-format-v31/plans/` on `m39-build` (or the area branch `m39-area-NN`) |
| what each area decided on its own | the area PR's **"Decisions made during build"** section |
| the owner's rulings | comments on the area issues (#1502–#1510 so far) |

Areas 00–06 were built by 2026-11-21 (PRs #1511, #1512, #1513, #1520, #1521, #1525, #1526); area 07 was
in progress.

## Planning against a moving M39

M39's design is final (revision 10), but the build makes decisions the design left open, and the owner's
rulings change some of what the design says. So:

- **Name the M39 assumptions.** Every M40 plan lists the M39 facts it relies on (a member's name, a
  sentence, a record field, a decision number), each with its source: the design section, or the area
  PR and decision that settled it.
- **Prefer what was built over what was designed** when they differ, and say which you used.
- **End with reconciliation.** Before any M40 plan is final, after M39 merges into `main`: bring `main`
  into this branch (rebase or merge, the owner's choice); go through every M39 area PR's "Decisions made during build" and the rulings on the
  area issues; check each named assumption; record what changed in each plan.

## How the owner works

- **Grill, then plan.** Settle decisions with the owner in rounds (the `mattpocock-skills:grilling`
  skill), each question with a recommended answer. Facts are found, never asked. Plan mode before
  writing a plan; plans with `superpowers:writing-plans`.
- **Small commits grouped by concern,** several per pull request.
- **Signed with the owner's key:** `timeout 90 git commit --gpg-sign=333983ABC7132E90` (a bare `gpg`
  defaults to an older key). If the key is locked, ask the owner to run
  `! echo unlock | gpg --clearsign -u 333983ABC7132E90 >/dev/null` and wait; never commit unsigned.
  The key expires 2027-01-01: remind the owner to extend it.
- **No AI attribution** anywhere: commits, PRs, issues, comments, files. Everything goes out under the
  owner's account and key.
- **Commit and push only when asked. Merge, close, tag, release only on the owner's word.**
- **Pull request and issue edits through REST** (`gh api -X PATCH repos/…/pulls/N -F body=@body.md`,
  `… /issues/N -f state=closed`); `gh pr edit` and `gh issue close` fail on this repository.
- **Tell the owner early** about anything deferred or held; give a held state a name. **Explicit over
  default:** a new value starts empty and is refused until chosen.
- **Fictional inputs only** in any example or test; never patient data.
- For search or lookup subagents, a faster model is fine; keep deep reasoning on the main model.
- Parallel shell calls share one shell: use absolute paths and `git -C <path>`, never a bare `cd` that
  others rely on.
