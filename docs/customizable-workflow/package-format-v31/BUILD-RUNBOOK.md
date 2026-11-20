# Milestone 39 build runbook

You are a Claude Code session started by `scripts/m39-build/run-areas.sh` on the build server.
The prompt names **one area**. Take that area from wherever it stands to a ready pull request, then
stop. Nobody is watching: every decision you make is recorded where the user will read it, and
anything that needs the user is written down as a **block**.

The design you build from is the v31 specification in this folder: [`index.md`](index.md) and its
ten parts. Section 5 of `index.md` lists the areas. Each area's scope is its row there, plus every
section of the parts and every issue that the row's subject owns. The spec is final (revision 10).
Read the parts your area touches in full before you plan.

## The areas

| Area | Subject | Notes |
|---|---|---|
| 00 | the contracts: the package-format spec and schemas, the agents catalogue, the provenance contract | `index.md` § 5 step 1; work happens in the three content repos (see *Other repos*) |
| 01 | byte-equivalence fixtures | captures today's (spec 30) prompts and notes, before any later area changes them; part 10 § 3 |
| 02 | parser and file model | |
| 03 | types | |
| 04 | JSON Logic | |
| 05 | resolver and lock | |
| 06 | engine loading and scheduling | |
| 07 | prompts and macros | |
| 08 | checks, deliverables and the profile | |
| 09 | the record | |
| 10 | migration | #1447 |
| 11 | registry and publish | |
| 12 | Create and History | |
| 13 | diagram | #1398 |

## Names

| Thing | Name |
|---|---|
| Integration branch | `m39-build` (every area PR targets it) |
| Area branch | `m39-area-NN` (two digits, e.g. `m39-area-01`) |
| Worktree | `~/Projects/worktrees/m39-area-NN` |
| Area plan | `docs/customizable-workflow/package-format-v31/plans/area-NN-<slug>.md` |
| Parent issue | title `M39 build: specVersion 31`, label `m39-build-parent`, Milestone 39 |
| Area issue | title `M39 build, area NN: <subject>`, label `m39-area`, Milestone 39, a sub-issue of the parent |
| Labels the driver reads | `m39-built` (merged; set by the driver), `needs-decision` (blocked; set by you) |

The driver finds your work by these names, so they are exact.

## Your session

Work through these steps in order. Each ends on its **done** line. On a resumed session, the first
step whose done line does not hold yet is where you start.

1. **Orient.** Read the area issue and its comments, the area branch's log, the plan's checkboxes,
   and the PR with its checks, whichever of them exist.
   *Done:* you can name the next unfinished step.

2. **File the issues** (the first session of the build only). Create the labels `m39-area`,
   `m39-build-parent`, `m39-built` and `needs-decision` if they are missing. File the parent issue
   and all fourteen area issues in Milestone 39. Each area body names its subject, the spec parts
   and sections it owns, and the design issues it builds. Link each area issue as a sub-issue
   (`gh api -X POST repos/Consultologist/Consultologist-Blazor/issues/<parent>/sub_issues -F sub_issue_id=<the issue's id, not its number>`).
   *Done:* the parent lists fourteen sub-issues, 00 to 13.

3. **Make the worktree:**
   `git -C ~/Projects/Consultologist-Blazor fetch origin && git -C ~/Projects/Consultologist-Blazor worktree add ~/Projects/worktrees/m39-area-NN -b m39-area-NN origin/m39-build`.
   Work only inside it from here on.
   *Done:* the worktree exists on `m39-area-NN`, based on the latest `origin/m39-build`.

4. **Plan** with `superpowers:writing-plans`, saved at the area's plan path. The user has
   pre-chosen **subagent-driven** execution and pre-approved the plan, so after the skill's
   self-review go straight on. Commit the plan as the branch's first commit and push.
   *Done:* every requirement in the area's spec sections maps to a task, and the plan is pushed.

5. **Build** with `superpowers:subagent-driven-development`, following the plan. Push after each
   task, so a lost session loses at most one task.
   *Done:* every task's checkbox is ticked and its review passed.

6. **Verify** with `superpowers:verification-before-completion`, from the worktree:
   - `dotnet build Consultologist.sln`;
   - `dotnet test Consultologist.sln --filter "FullyQualifiedName!~Consultologist.Web.E2E.Tests"`;
   - the browser suite, served as CI serves it, in **one** shell command (the server lives only
     as long as that command):
     ```bash
     dotnet publish src/Consultologist.Web -c Release -o publish -p:E2E=true \
       && { python3 tests/Consultologist.Web.E2E.Tests/serve.py 5000 publish/wwwroot & srv=$!; } \
       && curl -sf --retry 30 --retry-connrefused --retry-delay 1 http://localhost:5000/ >/dev/null \
       && E2E_BASE_URL=http://localhost:5000 dotnet test tests/Consultologist.Web.E2E.Tests -c Release; \
       rc=$?; kill $srv 2>/dev/null; exit $rc
     ```
     Stop the server by its process id, as above, never with `pkill -f serve.py`: that pattern also
     matches the shell running the command, and kills it.

   All green, output read. A browser-suite failure that names the browser itself (*"Executable
   doesn't exist"*, a missing shared library) is the server's setup, not the code: record it as a
   **block**.
   *Done:* all three pass on the pushed head.

7. **Open the PR** into `m39-build`, ready for review (not a draft), with the body below. Comment the
   PR's link on the area issue. Wait for the checks (`gh pr checks <n> --watch`); when one goes red,
   fix it, push, and wait again.
   *Done:* the PR is ready, every check is green, and the body is complete. **Stop here:** the
   driver merges.

### The PR body

```markdown
Part of #<parent>. Area NN: <subject> (#<area issue>).

## What this builds
## Spec coverage
<each section of the area's spec parts → the task or commit that builds it>
## Decisions made during build
<each choice the spec left open: the section, the choice, why; "None" if none>
## Goldens to review
<every new or changed golden or capture file; "None" if none>
## Other repos
<commits pushed to a content repo's m39-build and pinned here; "None" if none>
## Verification
<the commands run and their results>
```

## When the spec does not settle something

- **The spec is silent:** take the smallest choice consistent with the spec and the code around
  it, record it under *Decisions made during build*, and carry on.
- **The spec contradicts itself or the code, or the work needs the user** (an account, a secret, a
  deploy, a design choice with real trade-offs): this is a **block**. Add the `needs-decision` label
  to the area issue and comment there: the spec section quoted, what conflicts, the options, and your
  recommendation. Then end the session. The driver stops and notifies the user.

## Other repos

Area 00, and any later area that changes a contract, works in the content repos at
`~/Projects/consultologist-package-format`, `~/Projects/consultologist-agents` and
`~/Projects/consultologist-provenance`. In each, commit on a branch named `m39-build` (create it
from that repo's `origin/main` if missing) and push it directly. Then move the matching submodule
under `external/` in your worktree to the pushed commit, and list the commits in the PR's *Other
repos* section. Releasing a contract (a tag, a publish) is the user's step at the end.
`~/Projects/consultologist-workflows` is read-only for this build: area 01 vendors packages from
its tags.

## Standing rules

- **Commits:** small, grouped by concern, several per PR. Subject as the history does it:
  `Tests (#<area issue>): …`, `Api (#…): …`, `Web (#…): …`, `PackageFormat (#…): …`,
  `Docs (#…): …`. Signing is set up in git config.
- **Inputs are fictional**, always: test data, fixtures, prompts and examples carry invented
  people and invented clinical text.
- **Tests prove things on in-repo fixtures.** A test that needs another repo's content pins a tag
  and fails loudly when it is missing (`AGENTS.md`, the sibling-checkout trap).
- **GitHub writes go through `gh`;** edits to issues and PRs use REST (`gh api -X PATCH …`), as
  `AGENTS.md` explains. The GitHub MCP tools are for reading and searching.
- **What you own:** the `m39-area-NN` branches and worktrees, the `m39-build` branches of the
  content repos, the area issues and their PRs. Merging, `main`, tags, releases, deploys, pins,
  Azure, milestones and closing issues belong to the user and the driver. Area issues stay open
  until the user merges `m39-build` into `main`.
- **31 stays off in production.** Every area lands with 31 outside the compiled-in accepted
  versions, admitted only through `WorkflowPackages__PreviewSpecVersions` (part 09 § 2.1).
- **Protected paths:** `.github/`, `.claude/`, `scripts/m39-build/`, `AGENTS.md`, `CLAUDE.md` and
  this runbook change only by the user's hand. A PR that touches them is refused by the driver; a
  hook on the server refuses the edit. A trap worth adding to `AGENTS.md` goes in the PR body under
  *Decisions made during build*, for the user to add.
- **Goldens freeze ground truth.** Generate them with the code that the spec says produces them,
  read each one, and list every one under *Goldens to review*.

## After area 13

The build ends at area 13. What follows in `index.md` § 5 (dev smoke runs, the go/no-go gate,
the satellites, the bump, publishing, the migration and the pins) is the user's.
