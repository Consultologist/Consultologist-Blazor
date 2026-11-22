# Area 08: Checks, Deliverables and the Profile (specVersion 31) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give specVersion 31 its verification and its output: hold `checks/` and `deliverables/` to part 06 at publish, let a check proofread a composed deliverable, compose deliverables in their strict formats, decide each deliverable's outcome (skipped, failed, held, blocked, released), deliver the released ones at the end of the job with retries inside their own window, alert the operator, and record which signature block a job used.

**Architecture:** Publish rules join the existing steps of `SourceCheck.Run` in `src/Consultologist.PackageFormat/V31/` (the reader, the types, the resolver, the expressions, the dialect, the derived step); each new refusal is a method on a sentence class and has a refusal package. At run time `PlanRunner` gains an outcome step for deliverables (a deliverable is composed, its gate check reads the composed text, everything else reads it only once its outcome is final), and a second pure runner, `DeliveryRunner`, sends each destination through an `IDeliveryHost` as `PlanRunner` calls agents through `IRunHost`. The engine (`src/Consultologist.Api/V31/`) hosts both on the Durable orchestration: delivery, alerts and pattern-bearing rules run in activities, so a replay reads their stored results.

**Tech Stack:** C# / .NET 10, Scriban 7.2.5 (pinned), `System.Text.Json` (`JsonNode`), Durable Functions (isolated), Microsoft Graph mail through the existing `IGraphMailClient`, PdfSharp/MigraDoc through the existing `ConsultDocumentPdf`, xUnit and NSubstitute in `tests/Consultologist.Api.Tests.csproj`.

**Spec:** `docs/customizable-workflow/package-format-v31/` — [`06-checks-deliverables-profile.md`](../06-checks-deliverables-profile.md) in full (its *Refusals* list is the sentence list; `v31/sentences.json` rows S06-001 to S06-038) and [`09-engine-and-pages.md`](../09-engine-and-pages.md) §§ 3.1, 3.4, 3.8, 3.9, with [`03-expressions.md`](../03-expressions.md) §§ 4.1, 4.2, 4.4 to 4.6, 5.4, [`05-templates-and-macros.md`](../05-templates-and-macros.md) § 3.1, [`07-compiled-documents.md`](../07-compiled-documents.md) §§ 2.5, 2.7, 3.2, [`08-record.md`](../08-record.md) §§ 3, 6, 10 (member names only). The owner's rulings are on [#1505](https://github.com/Consultologist/Consultologist-Blazor/issues/1505) (three comments) and [#1507](https://github.com/Consultologist/Consultologist-Blazor/issues/1507) (items 1, 3, 7). Earlier areas' decisions are in PRs #1525 (05-D…), #1526 (06-D…) and #1529 (07-D…).

**Not this area's** (each has a seam here): storing rows, the record blob, `sites` and `substitutions` of a deliverable row, retention (area 09: this area puts outcomes and delivery rows through `IRunRecorder` under part 08's member names); the Create page's signature choice, the Profile page's Letter details card and time zone, History's banners and *"Delivering…"* (area 12: this area gives the request member, the header members, the status and the reasons); raising the publish and exporter alerts (areas 10 and 11: this area gives the alert kinds and the sender); converting a 30 package (area 10); the diagram's `gates` and `aligned` edges (area 13).

## Global Constraints

- **Worktree only:** `~/Projects/worktrees/m39-area-08` on `m39-area-08`. The content repo is worked in `~/Projects/consultologist-package-format` on `m39-build`, pushed directly; the pin `external/consultologist-package-format` moves only to a pushed commit. `~/Projects/consultologist-workflows` is read-only.
- **Never edit** `.github/`, `.claude/`, `scripts/m39-build/`, `AGENTS.md`, `CLAUDE.md` or `BUILD-RUNBOOK.md`, in any repo. A shell command that names one of those paths with a `>` in it is refused by the guard: write files with the file tool.
- **31 stays off in production:** `WorkflowPackageValidator.AcceptedSpecVersions` and `WorkflowPackageStore.SupportedSpecVersions` do not change. With `WorkflowPackages__PreviewSpecVersions` unset nothing answers differently, no 31 code path reads storage, and no timer of this area runs unless its schedule setting is set.
- **The 5–30 path keeps its behaviour:** no 5–30 golden, capture or expectation changes; `SendEmailIntakeReplyActivity`, `ConsultGenerationOrchestrator` and `RecordDelivery` are read and reused, not changed in behaviour. `[Function]` strings and activity names already deployed stay as they are; new ones end in `-v31` or `V31`.
- **Sentences are used as written.** A sentence with a row in `v31/sentences.json` is that row with its `<…>` filled, under that row's id, held by `V31Contract.AssertSentence`. A rule with no row gets an area sentence `A08-NNN` (table below), added to `V31Contract` (a new dictionary `ChecksAreaSentences` and an `A08-` branch, as A07 has) and to the conformance README's table. Where a rule of part 06 is already said under another part's id (table *Rules already said*), it stays so: one rule, one sentence (05-D2).
- **No sentence, log line, telemetry, row, alert or email body carries a value** from an input, a model or a profile: only ids, keys, paths, line numbers, counts, limits, hashes, lengths and package text. A delivery email's body is boilerplate and the History link, as today.
- **Never throw on package content, model output or a deliverable's text.** `ArgumentNullException` for a null argument is the only exception a public method of the library throws.
- **The orchestration stays deterministic:** the runners read no clock but their host's, await no task but their host's and recorder's, and iterate in the fixed order or list order.
- **Invariant culture, ordinal comparison,** everywhere. Plan JSON is read-only: `DeepClone` before placing a node in a new tree.
- **Fictional only:** invented people and clinical text (Margaret Okafor, Dr A. Sample, Sample Oncology Clinic).
- **Goldens come from code,** are read after regeneration (`GOLDEN_UPDATE_V31=1`; `UPDATE_V31_GOLDENS=1` for `tests/V31Types/Golden/format-hashes.json`), and are listed in the PR.
- **Commits:** small, by concern: `PackageFormat (#1505): …`, `Api (#1505): …`, `Tests (#1505): …`, `Docs (#1505): …`; in the content repo `Format spec (Consultologist-Blazor#1505): …`. Every message ends with exactly `Co-Authored-By: Claude <noreply@anthropic.com>` (a pushed commit cannot be amended: the guard refuses a force-push). Push after each task: `git push -u origin m39-area-08` the first time (the branch tracks `origin/m39-build`; never push to that).
- **Tests:** publish tests in `tests/V31Source/`, `tests/V31Resolve/` and `tests/V31Templates/`, run-time tests in `tests/V31Run/`, engine tests in `tests/V31Engine/`. Command: `dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~V31"`; a task that touches shared code also runs `dotnet test Consultologist.sln --filter "FullyQualifiedName!~Consultologist.Web.E2E.Tests"`.
- **Refusal packages and compiled documents.** A task that adds a publish refusal or changes `resolve.json`'s bytes adds its `RefusalCase`s in a new `tests/V31Source/RefusalPackages.Checks.cs` (method `Checks()`, joined to `All()`; `SourceSentences`-style classes it adds are listed in `EverySentence_TheCheckerCanSay_HasACase`), then re-exports (`CONFORMANCE_EXPORT_V31=~/Projects/consultologist-package-format dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~RefusalPackagesExport|FullyQualifiedName~CompiledDocumentsExport|FullyQualifiedName~DialectGoldensExport"`), reads the diff there, commits and pushes there, runs `python3 scripts/check-v31.py` there, and moves the pin, in the same task. A sentence said only at run time or at job start is listed in a new `ChecksNotShipped` list with its reason.

## Decisions this plan takes

Each goes in the PR body under *Decisions made during build*. R = ruled by the owner; the rest are where the spec is silent or two parts differ.

| # | Where | Choice |
|---|---|---|
| R1 | #1507 item 1 | A deliverable skipped by its `if` reads `null` at `.content` and `.value`; reading it does not fail the job. "Ruled by the owner on #1507". |
| R2 | #1507 item 3 | A deliverable its check refuses is withheld, not erased: every reader but its gate (D5) reads `.content` and `.value` as `null`; its `.state` and its check's result stay readable; it is never delivered; its outcome keeps its composed text and hash for the record. The ruling's word `refused` is part 06 § 7's state `blocked`; the state written is `blocked`. "Ruled by the owner on #1507". |
| R3 | #1507 item 7 | A job whose every deliverable is blocked is Completed. "Ruled by the owner on #1507". |
| R4 | #1505 comment 2, ruling 1 (05-D15) | `delivery` and `bundle` on every deliverable entry are named in part 07 § 2.5 and required by `resolve.schema.json`'s deliverable entry. "Ruled by the owner on #1505". |
| R5 | #1505 comment 2, ruling 2 (05-D31) | `modelDerived`, `runDependent` and `profileCarrying` follow earlier-pass reads (`pass.previous…node.<id>`, `pass.history`). Earlier passes stay non-edges for graph order and circles. "Ruled by the owner on #1505". |
| R6 | #1505 comment 2, ruling 3 | A rule that holds `matches` or `has_word` is evaluated in an activity, so a replay reads the stored outcome. Part 09 § 3.1's "`matches` reads no clock" bullet is amended to say so. "Ruled by the owner on #1505". |
| R7 | #1505 comment 3 | The four carried-over rulings are "already done by area 07" (PR #1529, group 1: Tasks 7, 8, 12b, 12c). Task 1 confirms each by its test and builds nothing. |
| R8 | #1507 comment 2 | A 31 start is refused, as a 30 start is, when no deliverable can exist: every deliverable has an `if`, each reads only `input.*` and `run.*`, and each is false or `null` on the start's inputs. Today's error (`NoApplicableDeliverable`) and wording are reused. |
| D1 | part 03 § 4.1 against part 06 §§ 1.2, 6.7, 7 | Part 03 lists `deliverable.*` as never readable by JSON Logic; part 06 says three times that checks read a deliverable. Built per part 06 (the part that owns checks): a rule in `checks/` reads `deliverable.<id>.content`, `.value` and `.state`; everywhere else S03-019 stands. Flagged: part 03 § 4.1 needs the exception written. |
| D2 | part 06 § 1.2; R2 | A deliverable's members for a check: `.content` (nullable `string`: the composed text), `.value` (for an object format the object, else the text; nullable), `.state` (`string`, one of `released`, `held`, `skipped`, `blocked`). A `failed` deliverable fails the job, so it is never read. |
| D3 | part 06 § 7; R2 | **Who reads the composed text.** A deliverable's *gate set* is its `check` and every check that check depends on, transitively. A reader in the gate set reads the composed deliverable (`.content`, `.value`) as soon as it is composed, and may not read its `.state` (A08-003). Every other reader waits for the outcome and reads R1/R2's form. Publish adds the wait as an edge from the gate check to each such reader for circles; `RunPlan` derives the same sets from the compiled document, so `resolve.json` gains no member for it. |
| D4 | part 06 § 7 "errored (failed to run)" | A check some deliverable names as its `check` that fails at run time (its `if` or `expression` fails, or its value is not a `check_result`) settles `Failed` with its sentence and does **not** fail the job: each deliverable it gates is `blocked` with S08-008, and it reads `null` with `.state` `failed`. Any other check failing at run time fails the job, as today. |
| D5 | part 06 § 7 the table's order | Outcomes are decided in the table's order: skipped (its `if`), failed (the render failed, or empty with `whenEmpty: fail`), held (empty with `hold`), then, once its gate check settles, blocked or released. A skipped, failed or held deliverable does not wait for its gate. An ungated deliverable is final when composed. |
| D6 | part 06 § 5 `<detail>`; S08-007 | `<detail>` is built from the `check_result` alone, never its `message` (an `object` can compute one): with `passed` and `total` set, `<passed> of <total> passed`; else with `uncovered` set, `<n> terms missing` (`1 term missing`); else `it did not pass`. S08-008's `<error>` is the check's run-time sentence without its final full stop. |
| D7 | part 06 § 6.4 escaping | A deliverable entry gains the derived member `escapes`: `[ { "unit": <site or null>, "at": "<line>:<column>" } ]`, one per output tag whose value is escaped, in order of appearance with placed snippets expanded (the shape of `runSpots`' `unit` and `at`, 07-D4). A value is escaped where plain text enters Markdown: in a `markdown_block` deliverable, an output of a plain-text format, a `string`, a number or a boolean written in the body or in a placed `markdown_block`/`markdown_inline` snippet; a placed plain-text snippet is escaped once, whole. A renderer's output is never escaped (it is the renderer's format). `[]` for a `plaintext` or object deliverable. |
| D8 | part 06 § 6.4 an object deliverable | Its body is exactly one output tag reading `node.<x>.value` (whitespace marks and a final line end allowed), the node's format the deliverable's by `FormatTypes.Same`; A07-007 is not said for it. At run time its text is the value's RFC 8785 JSON, with no final line end; a `null` value composes `null` and the deliverable is never empty. |
| D9 | part 06 § 6.4 "may not write Markdown syntax" | In a `plaintext` deliverable's body (outside tags, raw blocks counted as text) a line whose first non-space character is `#`, `-`, `*` or `>` is refused, and so is a line holding two `**`. S06-022's `<text>` is the line trimmed, cut to 40 characters. Placed snippets are held to the same rule by their format: a `markdown_*` snippet placed in a `plaintext` deliverable is S06-021. |
| D10 | part 06 § 8; #1408 item 9 "still to design" | **Kinds to endpoints, from what the account already has; no new setting.** `email`: set up when `delivery.address` is set, `delivery.emailPdf` is not false and the mailbox is configured; it sends today's link-only message. `pdf`: set up when `email` is and `delivery.documentPassword` is set; it sends the message with the deliverable as a password-protected PDF (`ConsultDocumentPdf`). `emr` and `zip`: no account can set them up in this build (no write-back and no protected archive exist), so each is recorded *not sent: EMR isn't set up for this account* / *Zip…*. Flagged for the owner: the mapping's home in the account is still to design. |
| D11 | part 06 § 8 `bundle` | A folder with `bundle: "zip"` makes one send per kind for all its released deliverables: `email` one message naming each; `pdf` one `.zip` attachment holding each protected PDF. Each deliverable still gets its own delivery row, with the send's result. A folder with nothing released sends nothing. |
| D12 | part 09 § 3.8 "3 times, with back-off inside the window" | Each destination gets `deliveryAttempts` attempts in all. The wait before attempt 2 is 2 minutes, doubling after, each cut to the time the window has left; no attempt starts after `deliveryMinutes` from the start of delivering; a destination unsent then is `failed`. Destinations are sent side by side. Not set up is final at once. |
| D13 | part 08 § 10 | A delivery row's key is `delivery:<deliverable key>/<kind>`; its `reason` is S08-006's sentence for not set up, `not sent: the job failed` for a failed job, `failed: <kind>` after the attempts or the window, and `null` when sent. `<Kind>` in sentences is `Email`, `PDF`, `EMR` or `Zip`. |
| D14 | part 09 § 3.9 | Alerts go to `Operators__AlertAddress` through the existing mailbox; with either unset the alert is only logged (`Operator alert. Kind=…`). The 429 alert is raised once per job, when the job's capacity waits pass 5 minutes in all; the slow-job alert once, at 80% of `jobMinutes`. The weekly summary runs on `%OperatorWeeklySummarySchedule%` and is off when that is unset; its "largest record" and "outstanding migration hand edits" lines read `not measured in this build`, for areas 09 and 10 to fill. |
| D15 | part 06 § 10.3; part 08 § 3 | The request gains `SignatureBlockId` (read at 31 only). Set and naming a block of the account: that block is `profile.signature` for this job. Naming none: the start is refused with A08-004. The header's `signatureBlockId` is the block used and `signatureChosen` is `"job"` when the request named a block other than `ChosenId`, else `"default"`; both `null` when `needs.profile` lacks `signature`. |
| D16 | part 06 § 10.7 limits | `PUT Account/Settings/{key}` refuses `profile.credentials` over 120 characters, `profile.organisation` over 200, `profile.closing` over 4,000, and an `account.timeZone` that `V31StartChecks` would not accept, each with a 400 worded as that route's other refusals are. |
| D17 | part 06 § 3.1 "a check loop's body holds checks only" | Under `checks/` a leaf is `kind: computed`, `format: check_result` (S06-010, S06-009), and a flow node is `for`, `if` or `case` (a `while` or `call` is S06-011, as is anything else). A `for` in `checks/` iterates `node.<loop>.items` or `check.<loop>.items` of a `for` (S06-014). |
| D18 | part 06 § 1.1 R-C10 | "Can be `null`" is the resolver's `Nullable` on a read (05-D14): a gate check's `if` is refused for the first such read (S06-035). |

**Area sentences** (no row in `sentences.json`; A08-001 is part 06's own wording for R-C1):

| Id | Sentence |
|---|---|
| A08-001 | `Deliverable '<id>' is checked by <reference>; a deliverable's check names a check.*.` (also a `check.<id>` that is not a check at the top of `checks/`) |
| A08-002 | `Deliverables folder '<path>' has bundle <value>; write "zip" or null.` |
| A08-003 | `Check '<id>' reads deliverable.<deliverable>.state, and deliverable '<deliverable>' waits for check '<id>' through its check '<gate>': read its .content, or read the state in a check it doesn't wait for.` |
| A08-004 | `The signature chosen for this job isn't in your profile. Choose one in Profile.` (job start) |
| A08-005 | `Deliverable '<id>' was empty, and the workflow fails an empty <label>.` (run time; part 08's recorded sentence, used under its `sentences.json` id instead if a row says it) |

**Rules already said** under another part's id, kept (listed in the PR and the conformance README): R-C5 → S03-029 / A04-010; R-C6 → S03-029 / S03-030; R-C8 → S04-062; R-C9 and R-D1 → S03-019 (a rule outside `checks/`), S05-020 (a template); R-D3 → S05-020; R-D8 → S03-020, S03-021, S03-018, S03-019; R-D9 → S05-047, S05-020; R-D12 → S01-007 / S01-008; delivery folders one level deep → S01-013; R-D13 → S05-009; R-F1 → S06-028 (templates), S03-018 (rules); R-F2 → S03-018; R-F3 → S04-069; R-F4 → S05-021; R-F5 → S03-044, S05-064. S06-012, 013, 015, 016, 017, 019, 024, 027, 029, 030 and 036 are therefore never said; S06-001 to 007, 033, 034, 037 and 038 are History's (area 12), built from the reasons this area records.

## Review Focus

1. **A gate that reads a withheld deliverable.** Deliverable B's gate reads deliverable A's `.content`, and A is blocked: B's gate reads `null`, still returns a `check_result`, and the job completes (Task 8).
2. **Whitespace is empty.** A deliverable whose template leaves only line ends and spaces (every section skipped) is empty for `whenEmpty`; one whose only content is a fixed heading is not; an object deliverable never is (Task 8).
3. **Plain text that looks like Markdown, per item.** A `for` over a list whose items hold `# 1`, `*bold*`, a line end and a `null`: each item is escaped on its own, the `null` writes nothing, and the composed hash is of the escaped text (Task 7).
4. **The window closes on a slow send.** A destination whose every attempt fails, and one whose send is still in flight when the window closes: both are `failed`, the job stays Completed, and no timer is left outstanding (Tasks 9, 11).
5. **One kind set up, one not.** An account with an address and no document password, a folder asking `["email", "pdf"]`: one row `sent`, one `not sent` with its sentence, and nothing is retried for the second (Tasks 9, 11).

---

### Task 1: The owner's rulings on lineage (R5) and the carried-over four (R7)

**Files:**
- Modify: `src/Consultologist.PackageFormat/V31/SourceDerived.Graph.cs` (`Content`, around line 421), `SourceDerived.Flags.cs`, `SourceDerived.Circles.cs` (unchanged in effect: asserted)
- Test: `tests/V31Resolve/SourceDerivedTests.cs`; fixtures and goldens that declared the old flags

**Interfaces:**
- Produces: a vertex's flags follow `Content` **and** earlier-pass reads; `reproducible`, circles and `graph.order` are as before.

- [x] **Step 1: Confirm R7.** Run `dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~ALocalName_ReadsItsRootedKey|FullyQualifiedName~ANullTitle_CompilesToThePackagesName|FullyQualifiedName~PassNodeReadsTests|FullyQualifiedName~RequiresCheckTests"`. Expected: PASS, and `tests/V31Resolve/Golden/functions.resolve.json` holds integer `requires.agents` values. Build nothing; the PR records each as "already done by area 07".
- [x] **Step 2: Write the failing tests** in `SourceDerivedTests`:
  - `AnEarlierPassRead_OfAnAgent_IsModelDerived`: a while whose computed body node reads only `pass.previous.node.draft.content` (an agent) and declares `modelDerived: false` is refused with S03-036 naming `modelDerived`; declared `true` it publishes.
  - `AnEarlierPassRead_OfARunFact_IsRunDependent` and `…_OfProfileText_IsProfileCarrying`: the same for the other two flags.
  - `ACondition_ReadingProfileTextOfAnEarlierPass_IsRefused`: S03-044 (it is already; pin it).
  - `EarlierPasses_StayOutOfTheOrderAndTheCircles`: two while-body nodes that read each other through `pass.previous` publish, and `graph.order` is unchanged from the golden.
- [x] **Step 3: Run** them. Expected: the first three FAIL (the flag is derived `false`).
- [x] **Step 4: Implement.** The flags are derived over `Content` plus the reads marked `Earlier` (they keep their vertex id); `reproducible` already walks both; circles keep dropping `Earlier`.
- [x] **Step 5: Regenerate and read** every golden and refusal package whose header flags flip (`GOLDEN_UPDATE_V31=1`, then the export of *Global Constraints*); `nested-note`'s `refine` body is the likely one. Each changed header flag goes in the PR's *Goldens to review*.
- [x] **Step 6: Run** the V31 filter. Expected: PASS.
- [x] **Step 7: Commit** `PackageFormat (#1505): the lineage flags follow earlier-pass reads`, the content repo's export, the pin; push.

### Task 2: `delivery` and `bundle` named in the contract (R4)

**Files:**
- Modify: `docs/customizable-workflow/package-format-v31/07-compiled-documents.md` (§ 2.5, after the derived-members table)
- Content repo: `v31/schemas/resolve.schema.json` (the deliverable entry), `package-format-v31.md` chapter 7's twin of § 2.5, `v31/conformance/schemas/resolve/` (one valid and one invalid instance)
- Test: `tests/V31Resolve/ResolveCompilerTests.cs`

- [x] **Step 1: Write the failing test** `ADeliverableEntry_FitsThePublishedSchema_WithItsDeliveryAndBundle`: each deliverable of the three goldens validates against the pinned schema's deliverable definition, which requires `delivery` (an array of `email`, `pdf`, `emr`, `zip`) and `bundle` (`"zip"` or `null`).
- [x] **Step 2: Run** it. Expected: FAIL (the schema does not name them).
- [x] **Step 3: Edit the contract.** Part 07 § 2.5 gains one paragraph: each `deliverables` entry carries `delivery` and `bundle` as its delivery folder's `index.json` writes them (`[]` and `null` when the folder writes none), because the engine loads only `resolve.json`. The schema gains the two properties, required; add `valid-deliverable-delivery.json` and `invalid-deliverable-missing-bundle.json`.
- [x] **Step 4: Run** `python3 scripts/check-v31.py` there, push, move the pin, run the test. Expected: PASS.
- [x] **Step 5: Commit** `Docs (#1505): part 07 names delivery and bundle on a deliverable entry`; push.

### Task 3: Checks at publish

**Files:**
- Create: `src/Consultologist.PackageFormat/V31/CheckSentences.cs`, `src/Consultologist.PackageFormat/V31/SourceChecks.cs`
- Modify: `SourceCheck.cs` (call it in the type step), `SourceParser.Nodes.cs:107` and `SourceTypes.Nodes.cs:84` (the "area 08" notes), `tests/V31Source/V31Contract.cs` (the A08 dictionary and branch)
- Test: `tests/V31Source/SourceChecksTests.cs`, `tests/V31Source/RefusalPackages.Checks.cs`

**Interfaces:**
- Produces: `internal static class CheckSentences` with `HasFormat(id, format, path)` S06-009, `HasKind(id, kind, path)` S06-010, `HoldsKind(path, kind)` S06-011, `LoopRunsOver(id, source, path)` S06-014, `GateIfReadsNullable(id, deliverable, readPath, path)` S06-035, `CheckedBy(deliverable, reference, path)` A08-001, each a `SourceRefusal`.
- Produces: `internal static class SourceChecks { static IReadOnlyList<SourceRefusal> Check(SourcePackage package, ScopeTree? tree) }`: the shape rules need no tree; S06-014, S06-035 and A08-001 run once the resolver has one.

- [x] **Step 1: Write the failing tests**, one per rule (D17, D18), each asserting exactly one refusal with the id and `V31Contract.AssertSentence`:
  - `ACheck_ThatIsAnAgent_IsRefused` (S06-010), `ACheck_WithAnotherFormat_IsRefused` (S06-009: `format: "boolean"`), `AWhile_InChecks_IsRefused` and `ACall_InChecks_IsRefused` (S06-011: `checks/again/index.json in checks/ is while; …`);
  - `ACheckLoop_OverAnInput_IsRefused` and `…_OverANodesValue_IsRefused` (S06-014), `ACheckLoop_OverACheckLoopsItems_Publishes`;
  - `AGateChecksIf_ReadingANullableValue_IsRefused` (S06-035), `ACheckThatGatesNothing_MayReadOne`;
  - `ADeliverablesCheck_NamingANode_IsRefused`, `…_NamingNoCheck_IsRefused`, `…_NamingACheckInsideALoop_IsRefused` (A08-001).
- [x] **Step 2: Run** them. Expected: FAIL (each publishes, or is refused under another id).
- [x] **Step 3: Implement** `SourceChecks.Check`; the functions golden (`ResolveFixture.Functions()`) must still publish.
- [x] **Step 4: Add the refusal cases** S06-009, S06-010, S06-011, S06-014, S06-035, A08-001 to `RefusalPackages.Checks.cs` (each invalid package one `.Edit` from its valid one); add the A08 table to the conformance README; export, check, push, pin.
- [x] **Step 5: Run** the V31 filter. Expected: PASS.
- [x] **Step 6: Commit** `PackageFormat (#1505): checks/ holds computed checks and the loops over them`; push.

### Task 4: A deliverable's header and its folder at publish

**Files:**
- Create: `src/Consultologist.PackageFormat/V31/DeliverableSentences.cs`, `src/Consultologist.PackageFormat/V31/SourceDeliverables.cs`
- Modify: `SourceCheck.cs`
- Test: `tests/V31Source/SourceDeliverablesTests.cs`, `RefusalPackages.Checks.cs`

**Interfaces:**
- Produces: `DeliverableSentences`: `ReadsOwnCheck(id, check, path)` S06-018, `HasFormat(id, format, path)` S06-020, `HasWhenEmpty(id, value, path)` S06-025, `HasDeliveryKind(folderPath, kind)` S06-026, `HasBundle(folderPath, value)` A08-002; and, for Task 6, `ReadsFormat(id, format, path, readFormat, line, fix, file)` S06-021 (`fix` is `Convert it in a node` or `Use a renderer or a node`), `WritesMarkdown(id, line, text, file)` S06-022, `IsObject(id, format, file)` S06-023.
- Produces: `SourceDeliverables.Check(SourcePackage package, FormatSet formats, ScopeTree? tree)`.

- [x] **Step 1: Write the failing tests:** `ADeliverable_OfMarkdownInline_IsRefused` and `…_OfAList_IsRefused` (S06-020; `markdown_block`, `plaintext` and a named object format publish), `WhenEmpty_Otherwise_IsRefused` (S06-025: `"skip"`), `ADeliveryKind_Unknown_IsRefused` (S06-026: `"fax"`, naming `deliverables/patient`), `ABundle_Otherwise_IsRefused` (A08-002: `"tar"`), `ADeliverable_ReadingItsOwnCheck_IsRefused` (S06-018, in its body; also through a placed snippet), `ADeliverable_ReadingAnotherCheck_Publishes`.
- [x] **Step 2: Run.** Expected: FAIL.
- [x] **Step 3: Implement.** The format is judged by `FormatTypes.Shape` of the named format (an object shape, or the two text names); the own-check read is found in the body's typed reads (`PackageTemplates`), so it runs after the dialect step.
- [x] **Step 4: Refusal cases** S06-018, S06-020, S06-025, S06-026, A08-002; export, check, push, pin.
- [x] **Step 5: Run** the V31 filter. Expected: PASS.
- [x] **Step 6: Commit** `PackageFormat (#1505): a deliverable's format, whenEmpty, delivery kinds and its own check`; push.

### Task 5: Checks read deliverables (D1, D2, D3)

**Files:**
- Modify: `ScopeTree.cs` (a `deliverable` head, around line 227), `ExpressionChecker.cs:36,217` (`Unreadable` is per site), `SourceDerived.Graph.cs` (a deliverable vertex is content to a check), `SourceDerived.Circles.cs` (check and deliverable vertices; the wait edge), `ResolveCompiler.Graph.cs` (a `reads` edge from the deliverable), `Run/RunPlan.cs`
- Test: `tests/V31Resolve/ScopeTreeTests.cs`, `SourceDerivedTests.cs`, `ResolveCompilerTests.cs`, `tests/V31Run/RunPlanTests.cs`, `RefusalPackages.Checks.cs`

**Interfaces:**
- Produces (publish): in a frame under `checks/`, `deliverable.<key path>.content|.value|.state` resolves (D2), typed and nullable; the read is a `reads` edge `deliverable.<id>` → the check, carries the deliverable's three flags to the check, and counts toward the check's `reproducible`. Outside `checks/` nothing changes (S03-019, S05-020, S03-014).
- Produces (publish): `CheckSentences.ReadsStateItDecides(check, deliverable, gate, path)` A08-003; circles through checks and deliverables are found with the wait edge of D3 and said with A05-003 (ids as written, `check '<id>'` and `deliverable '<id>'` where `node '<id>'` stands today only if the row's words allow; else the bare ids, as 05-D34).
- Produces (run): `PlanEntry.Gates` (`IReadOnlyList<string>`: the deliverables this check gates, empty otherwise), `PlanEntry.Check` (a deliverable's gate key, or null) and `PlanEntry.ReadsComposed` (`IReadOnlySet<string>`: the deliverable keys this entry reads as composed: it is in their gate set). `RunPlan.Build` refuses (S07-006) a deliverable read by an entry outside `checks`, and a gate set that reads the deliverable's `.state`.

- [x] **Step 1: Write the failing tests:**
  - `ACheck_ReadsADeliverablesContent`: `check.proofread` with `{ "!": [ { "blank": [ { "var": "deliverable.emr.consult.content" } ] } ] }`-style expression publishes; its entry's `reads` holds `deliverable.emr.consult.content`; `graph.edges` holds `{ from: "deliverable.emr.consult", to: "check.proofread", kind: "reads" }` beside the `gates` edge back.
  - `ACheck_OfAModelDerivedDeliverable_IsModelDerived`: declaring `false` is S03-036.
  - `AComputedNode_ReadingADeliverable_IsStillRefused` (S03-019) and `ACheck_ReadingAMemberADeliverableLacks_IsRefused` (S03-014: `.items`).
  - `AGate_ReadingItsDeliverablesState_IsRefused` (A08-003), `ACheckOutsideTheGateSet_MayReadState`.
  - `TwoGates_ReadingEachOthersDeliverable_AreACircle`: A's gate reads B's `.content` and B's gate reads A's; neither is in the other's gate set, so each waits for the other (D3): A05-003. `AGate_ReadingAnotherDeliverable_Publishes` when only one of them does.
  - `RunPlan_SaysWhoReadsComposed`: for the first package, `Entries["check.proofread"].ReadsComposed` is `{ deliverable.emr.consult }` and `Gates` the same; an aggregate the gate reads, which reads the deliverable, is in the set too.
- [x] **Step 2: Run.** Expected: FAIL (S03-019 on the first).
- [x] **Step 3: Implement** publish, then `RunPlan`. The gate set is the transitive closure of `Depends` from the deliverable's `check`, over `checks` entries.
- [x] **Step 4: Refusal case** A08-003; a new compiled golden case `checked-note` (`CompiledGoldenTests.Cases()`, `ResolveFixture.CheckedNote()`): part 06 § 3.4's package cut down, with a `markdown_block` deliverable gated by a check that reads its `.content`, a second deliverable in `patient/` (`["email", "pdf"]`, `bundle: null`, `whenEmpty: "hold"`) and an aggregate over a check loop. Export, check, push, pin.
- [x] **Step 5: Run** the V31 filter. Expected: PASS.
- [x] **Step 6: Commit** `PackageFormat (#1505): a check reads a deliverable, and the gate set reads it composed`; push.

### Task 6: Strict formats at publish (D7, D8, D9)

**Files:**
- Create: `src/Consultologist.PackageFormat/V31/Templates/SourceTemplates.Deliverables.cs`
- Modify: `Templates/SourceTemplates.cs` (A07-007 at 327 and 445 skips D8's form; call the new rules for `TemplateSite.Deliverable`), `Templates/PackageTemplates.cs` (expose a deliverable's `escapes`), `ResolveCompiler.Entries.cs` (write `escapes` on deliverables)
- Test: `tests/V31Templates/DeliverableFormatsTests.cs`, `RefusalPackages.Checks.cs`

**Interfaces:**
- Consumes: `DeliverableSentences.ReadsFormat`, `WritesMarkdown`, `IsObject` (Task 4); `TypedRead.Format` of each output (`SiteReads`).
- Produces: `PackageTemplates.Escapes(string deliverableKey)` → `IReadOnlyList<(string? Unit, string At)>`; `resolve.json` deliverable entries gain `escapes` (D7).

- [x] **Step 1: Write the failing tests** (part 06 § 6.4's table, row by row):
  - `AMarkdownDeliverable_ReadsMarkdownAndPlainText` publishes, and `escapes` lists exactly the outputs of `plaintext`, `string`, number and boolean, with their `at`; an output piped into a renderer is not listed; a `for` variable's `{{ s.title }}` (a `string`) is.
  - `AMarkdownDeliverable_WritingAnObject_IsRefused` (S06-021 with `Use a renderer or a node`; a list too).
  - `APlaintextDeliverable_ReadingMarkdown_IsRefused` (S06-021 with `Convert it in a node`: a `markdown_block` node, a `markdown_inline` one, and a placed `markdown_block` snippet).
  - `APlaintextDeliverable_WritingAHeading_IsRefused` (S06-022: line 4, `'## Plan'`), and `- item`, `> quote`, `a **b** c`; `2 - 1 = 1` mid-line and `*` inside a tag publish.
  - `AnObjectDeliverable_IsOneValue`: `{{ node.summary.value }}` and `{{~ node.summary.value ~}}\n` publish with `escapes: []`; text beside it, a second tag, `.content`, another node's format and a pipe are S06-023.
  - `ProfileText_InAMarkdownDeliverable_IsEscaped`: `{{ profile.signature }}` is in `escapes`.
- [x] **Step 2: Run.** Expected: FAIL.
- [x] **Step 3: Implement.**
- [x] **Step 4: Refusal cases** S06-021, S06-022, S06-023; regenerate the compiled goldens (`escapes` on every deliverable; read each); extend `resolve.schema.json`'s deliverable entry with `escapes`; export, check, push, pin.
- [x] **Step 5: Run** the V31 filter. Expected: PASS.
- [x] **Step 6: Commit** `PackageFormat (#1505): a deliverable's reads fit its format, and resolve.json says which are escaped`; push.

### Task 7: Composing a deliverable

**Files:**
- Modify: `Templates/BodyComposer.cs` (`TextPlan.Escapes`, `ComposeContext`), `Templates/BodyComposer.Units.cs`, `Templates/RenderTurn.cs`
- Test: `tests/V31Run/DeliverableComposeTests.cs`; `tests/V31Templates/DialectGoldensExport.cs` and its cases

**Interfaces:**
- Consumes: the entry's `escapes` (Task 6), `MarkdownEscape.Escape` (07-D7).
- Produces: `BodyComposer.Compose` escapes each listed output where it is written (a `null` writes nothing; a run spot's recorded output is the escaped text); for D8's object deliverable `RenderTurn.Render` gives the value's canonical JSON. `RenderOutcome.Detail` is unchanged in shape.

- [x] **Step 1: Write the failing tests:**
  - `PlainText_IsEscaped_PerItem` (Review Focus 3): items `# 1`, `*bold*`, `a\nb` and `null` in a `for` compose `\# 1`, `\*bold\*`, `a\nb` (the line end kept) and nothing; `CanonicalJson.HashText` of the text is what the outcome's hash is.
  - `MarkdownReads_AreWrittenAsTheyAre`; `APlacedPlainTextSnippet_IsEscapedOnce`; `ARenderersOutput_IsNotEscaped`.
  - `AnObjectDeliverable_ComposesCanonicalJson`: `{"b":1,"a":"x"}` composes `{"a":"x","b":1}`; a `null` value composes `null`.
  - `ADocumentWithoutEscapes_ComposesAsBefore`: the four run goldens' deliverable texts are unchanged.
- [x] **Step 2: Run.** Expected: FAIL.
- [x] **Step 3: Implement.**
- [x] **Step 4: Dialect goldens:** add cases `deliverable-escaped`, `deliverable-plaintext` and `deliverable-object` to the `dialect-goldens` family (each a `package/` and `entry.txt`); the export **merges**: every case area 07 exported is still there, byte for byte (assert it in `DialectGoldenTests`). Export, check, push, pin.
- [x] **Step 5: Run** the V31 filter. Expected: PASS.
- [x] **Step 6: Commit** `PackageFormat (#1505): a deliverable composes with its plain reads escaped, an object one as canonical JSON`; push.

### Task 8: Outcomes: skipped, failed, held, blocked, released (R1, R2, R3, D3 to D6)

**Files:**
- Create: `src/Consultologist.PackageFormat/V31/Run/PlanRunner.Deliverables.cs`, `Run/DeliverableOutcome.cs`
- Modify: `Run/PlanRunner.cs` (`RenderedAsync`, `ComputeAsync`, `GateAsync`, `RunResult`), `Run/IRunRecorder.cs` (`RowState`), `Run/RunScope.cs` (`Read` by reader), `Run/RunInstance.cs`, `Run/RunSentences.cs`, `src/Consultologist.Api/V31/EntityRunRecorder.cs` (`StateName`)
- Test: `tests/V31Run/DeliverableOutcomeTests.cs`, `RunGoldenTests` (a `checked-note` run golden)

**Interfaces:**
- Consumes: `PlanEntry.Gates`, `Check`, `ReadsComposed` (Task 5).
- Produces:

```csharp
public enum DeliverableState { Released, Held, Failed, Skipped, Blocked }

/// Key: deliverable.emr.note. Folder: "" for deliverables/, else the folder's id. Reason: part 08 § 10's stateReason.
/// Check: the gate's row key, or null. Text and ContentHash: the composed text and its hash, null when skipped or the render failed.
public sealed record DeliverableOutcome(
    string Key, string Label, string Format, string Folder, DeliverableState State, string? Reason, string? Check,
    string? Text, string? ContentHash, IReadOnlyList<string> Delivery, string? Bundle);
```

- Produces: `RunResult` gains `IReadOnlyList<DeliverableOutcome> Deliverables` (plan order; only deliverables whose outcome was decided). `RowState.Composed` is replaced by `Released`, `Held` and `Blocked` (`StateName`: `released`, `held`, `blocked`). A deliverable's one row is recorded when its outcome is final: `Reason` the state reason, `ValueHash` the composed hash, `Detail` the render's detail plus `check` and `composedHash`.
- Produces: read forms. To an entry in `ReadsComposed`: `{ value, content, state: "composed" }`. To any other reader, once final: released or held `{ value, content, state }`; skipped or blocked `{ value: null, content: null, state }`. A reader outside the gate set is not ready until the outcome is final.
- Reasons, as written: skipped: S03-001's form (as today); held: `held: empty`; blocked: `blocked: check '<id>' failed (<detail>)` (D6), `blocked: check '<id>' could not run (<error>)`; released through a skipped gate: `released: check '<id>' didn't apply (its if was false)`; released otherwise: `null`; failed: A08-005 (the job fails with it, naming the deliverable's key).

- [x] **Step 1: Write the failing tests** (`FakeRunHost` scripts each render; `ListRecorder` holds the rows):
  - `AnUngatedDeliverable_IsReleased_WhenComposed`; `ASkippedDeliverable_ReadsNull_AndTheJobGoesOn` (R1: a check outside its gate set reads `.content` `null`, `.state` `skipped`).
  - `AnEmptyDeliverable_Holds_Releases_OrFailsTheJob` (a theory over `whenEmpty`; text `" \n\n"`; Review Focus 2 adds `"## Plan\n"` released under `hold`, and an object deliverable released under `fail`).
  - `AFailingGate_BlocksIt_AndTheJobCompletes` (state `Blocked`, reason `blocked: check 'all_sections' failed (1 of 2 passed)`, `Text` kept, `RunResult.State` Completed); `ATermsGate_SaysHowManyTermsAreMissing` (`2 terms missing`).
  - `ASkippedGate_ReleasesIt_WithItsReason`; `AGateThatCannotRun_BlocksIt` (D4: the gate's row is `Failed`, the job Completed, reason S08-008); `AnotherCheckThatCannotRun_FailsTheJob`.
  - `ABlockedDeliverable_ReadsNullToTheRestOfTheJob` (R2, Review Focus 1: B's gate reads A's `.content` as `null` and `.state` as `blocked`, and B is decided on that).
  - `EveryDeliverableBlocked_IsACompletedJob` (R3).
  - `ARow_IsRecordedOnce_WhenTheOutcomeIsFinal`: one row per deliverable, after its gate's row.
- [x] **Step 2: Run.** Expected: FAIL (`RowState.Released` does not exist).
- [x] **Step 3: Implement** `PlanRunner.Deliverables.cs`: `ComposedAsync` (after the render), `DecideOutcomesAsync` (in the settle round, in the fixed order, for each composed deliverable whose gate has settled), and the reader's form in `RunScope`.
- [x] **Step 4: Regenerate and read** the four run goldens (the `composed` rows become `released`, in their new place) and add `checked-note.run.txt` with a stub that fails one section's coverage, so the golden shows a blocked deliverable, a held one and their reasons.
- [x] **Step 5: Run** the V31 filter. Expected: PASS.
- [x] **Step 6: Commit** `PackageFormat (#1505): a deliverable's outcome: skipped, failed, held, blocked or released`; push.

### Task 9: The delivery runner (D10 to D13, the library half)

**Files:**
- Create: `src/Consultologist.PackageFormat/V31/Run/Delivery.cs`, `Run/DeliveryRunner.cs`
- Modify: `Run/IRunRecorder.cs`, `tests/V31Run/ListRecorder.cs`
- Test: `tests/V31Run/DeliveryRunnerTests.cs`, `tests/V31Run/FakeDeliveryHost.cs`

**Interfaces:**
- Consumes: `DeliverableOutcome` (Task 8), `RunLimits.DeliveryMinutes`, `DeliveryAttempts`.
- Produces:

```csharp
public sealed record DeliveryDocument(string Deliverable, string Label, string Format, string Text);

/// One send: one kind, for one deliverable, or for every released deliverable of a bundle folder.
public sealed record DeliverySend(string Kind, string? Bundle, string Folder, IReadOnlyList<DeliveryDocument> Documents, int Attempt);

public enum DeliverySendResult { Sent, NotSetUp, Failed }
public sealed record DeliverySendOutcome(DeliverySendResult Result, string? ErrorKind);

public interface IDeliveryHost
{
    DateTime UtcNow { get; }
    Task<DeliverySendOutcome> SendAsync(DeliverySend send);
    Task DelayAsync(TimeSpan wait, CancellationToken cancel);
}

public sealed record DeliveryAttempt(DateTime AtUtc, string Outcome);   // "sent", "failed: <error kind>"
public sealed record DeliveryRow(
    string Key, string Deliverable, string Kind, string Result, string? Reason, DateTime? AtUtc, IReadOnlyList<DeliveryAttempt> Attempts);

public static class DeliveryPlan
{
    public static readonly IReadOnlyList<string> Kinds = ["email", "pdf", "emr", "zip"];
    public static string KindWords(string kind);                                   // Email, PDF, EMR, Zip
    public static string Key(string deliverable, string kind);                     // delivery:<deliverable>/<kind>
    public static IReadOnlyList<DeliverySend> Sends(IReadOnlyList<DeliverableOutcome> outcomes);   // attempt 1 each, fixed order
    public static IReadOnlyList<DeliveryRow> NotSentJobFailed(IReadOnlyList<DeliverableOutcome> outcomes);
}

public sealed record DeliveryResult(IReadOnlyList<DeliveryRow> Rows, int Failed);

public sealed class DeliveryRunner(RunLimits limits, IDeliveryHost host, IRunRecorder recorder)
{
    public Task<DeliveryResult> RunAsync(IReadOnlyList<DeliverableOutcome> outcomes);
}
```

- Produces: `IRunRecorder.DeliveryAsync(DeliveryRow row)`, called once per row when its result is final. `Result` is `sent`, `not sent` or `failed`.

- [ ] **Step 1: Write the failing tests:**
  - `ASend_PerKind_PerReleasedDeliverable`: two released deliverables in `["email", "pdf"]`, one held, one in `[]`: four sends, in plan order then kind order; the held and the undelivered one have none.
  - `ABundle_IsOneSendPerKind_OfItsReleasedDeliverables` (D11): three in a `zip` folder, one blocked: one send holding two documents, and two rows with one result.
  - `NotSetUp_IsRecordedAtOnce_AndNeverRetried` (Review Focus 5): row `not sent`, reason `not sent: EMR isn't set up for this account`, one call.
  - `AFailedSend_IsRetried_ThenFailed` (D12): attempts at 0, +2 and +6 minutes with `DeliveryAttempts` 3; the row is `failed`, reason `failed: email`, three `Attempts`; `DeliveryResult.Failed` is 1.
  - `TheWindow_ClosesOnWhatIsUnsent` (Review Focus 4): with `DeliveryMinutes` 5 the third attempt never starts; a send in flight at the close is `failed`; `FakeDeliveryHost.OutstandingDelays` is 0 after.
  - `ASecondAttempt_ThatSends_IsSent`: `AtUtc` is the host's time of the send.
  - `AFailedJob_RecordsNotSent`: `NotSentJobFailed` gives one row per destination of each released deliverable, reason `not sent: the job failed`.
- [ ] **Step 2: Run.** Expected: FAIL (the types do not exist).
- [ ] **Step 3: Implement.** Sends run side by side; rows are recorded in the fixed order of `Sends` once all are final or the window closes; every timer is cancelled at the end, as `PlanRunner` does.
- [ ] **Step 4: Run** the V31 filter. Expected: PASS.
- [ ] **Step 5: Commit** `PackageFormat (#1505): delivery at the end: a send per destination, retried inside its window`; push.

### Task 10: A pattern's decision is stored (R6)

**Files:**
- Modify: `Run/IRunHost.cs`, `Run/PlanRunner.cs` and its partials (every `Decide` and `Evaluate` call), `IJsonLogicRule`'s home (`JsonLogicEvaluator.cs`), `src/Consultologist.Api/V31/DurableRunHost.cs`, `Jobs/ConsultGenerationActivityNames` (a new name), `docs/customizable-workflow/package-format-v31/09-engine-and-pages.md` § 3.1
- Create: `src/Consultologist.Api/V31/EvaluateRuleV31Activity.cs`
- Test: `tests/V31Run/PatternRulesTests.cs`, `tests/V31Engine/EvaluateRuleV31ActivityTests.cs`, `OrchestratorV31Tests.cs`

**Interfaces:**
- Produces: `IJsonLogicRule.ReadsPattern` (`bool`: the rule holds `matches` or `has_word` at any depth).
- Produces:

```csharp
/// Member: "if", "expression", "while", "until", or a placement's site ("macro.<id>", "part.<id>", …).
public sealed record RuleCall(string Key, string EntryKey, string? Function, string Member, JsonObject Scope);

/// Value: the rule's value (a condition's is its boolean). Failure: the failure's kind and its limit, as JsonLogicFailure holds them; never a value.
public sealed record RuleOutcome(JsonNode? Value, string? FailureKind, double? FailureLimitSeconds);
```

  and `IRunHost.EvaluateAsync(RuleCall call)` → `Task<RuleOutcome>`. The runner calls it for a rule whose `ReadsPattern` is true, awaited where the rule is decided (it takes no slot), and evaluates every other rule inline as today. The activity is `evaluate-rule-v31` (`ConsultGenerationActivityNames.EvaluateRuleV31`), with no retry policy; it loads the plan as `RenderNodeV31Activity` does and evaluates the entry's compiled rule over the call's scope.

- [ ] **Step 1: Write the failing tests:**
  - `ARuleWithAPattern_IsDecidedByTheHost`: a leaf whose `if` is `matches` makes one `EvaluateAsync` call (key, member `if`, a scope of exactly its reads) and none for a rule without one; the same for a computed `expression`, a `while`, an `until` and a placement's `if`.
  - `TheHostsAnswer_IsWhatTheRunUses`: the fake answers `false` where an inline evaluation would say `true`; the node is skipped. A `RuleOutcome` with `FailureKind` `MatchTooSlow` fails the node with A06-001.
  - `AReplay_ReadsTheStoredDecision` (`OrchestratorV31Tests`): the orchestration run twice over one history calls the evaluator once, and the second run's dispatch is the first's though the activity would now answer differently.
  - `TheActivity_EvaluatesTheCompiledRule`, `…_OfAPlanThatIsNotTheJobs_Fails`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.** `FakeRunHost` and `ComposingRunHost` answer by evaluating the plan's rule, so existing tests keep their results.
- [ ] **Step 4: Amend part 09 § 3.1:** the bullet "`matches` reads no clock" becomes: each evaluation of `matches` and `has_word` has the engine's time limit (#1503); a rule that holds one is evaluated in an activity and its outcome stored in the orchestration's history, so a replay reads the decision and never times it again; every other rule is evaluated inline.
- [ ] **Step 5: Run** the V31 filter and the run goldens (unchanged). Expected: PASS.
- [ ] **Step 6: Commit** `Api (#1505): a rule that holds a pattern is decided in an activity, and a replay reads its outcome`; push.

### Task 11: The engine delivers

**Files:**
- Create: `src/Consultologist.Api/V31/DeliverV31Activity.cs`, `V31/DeliveryChannels.cs`, `V31/DurableDeliveryHost.cs`
- Modify: `V31/ConsultGenerationOrchestratorV31.cs` (after `PlanRunner.RunAsync`), `V31/EntityRunRecorder.cs`, `Jobs/ConsultGenerationState.cs` (a status, `MarkDelivering`), `Email/GraphMailClient.cs` (`GraphMailAttachment` gains `string ContentType = "application/pdf"`), `ApiServices.cs`
- Test: `tests/V31Engine/DeliverV31ActivityTests.cs`, `DeliveryChannelsTests.cs`, `OrchestratorV31Tests.cs`

**Interfaces:**
- Consumes: `DeliveryRunner`, `IDeliveryHost`, `DeliverySend`, `DeliverySendOutcome`, `DeliveryPlan` (Task 9); `RunResult.Deliverables` (Task 8); `IGraphMailClient`, `IAccountSettingsStore`, `ConsultDocumentPdf.Render`, `EmailIntakeReply.Compose`, `DeliveryAddress.EmailPdfOf`.
- Produces: `ConsultGenerationJobStatuses.Delivering = "delivering"` and the entity operation `MarkDelivering()` (valid only from running; not terminal).
- Produces: `public sealed record DeliverV31Input(string JobId, string AppUserId, DeliverySend Send)` and the activity `deliver-v31` (`ConsultGenerationActivityNames.DeliverV31`) → `DeliverySendOutcome`: one attempt, no retry policy, never throwing for a send's failure (`Failed` with `ErrorKind` `error`, `too-large` or `unreachable`).
- Produces: `internal interface IDeliveryChannel { string Kind { get; } Task<DeliverySendOutcome> SendAsync(DeliverV31Input input, CancellationToken ct); }` with `EmailDeliveryChannel` and `PdfDeliveryChannel` (D10, D11); a kind with no channel is `NotSetUp`.

- [ ] **Step 1: Write the failing tests:**
  - Channels: `Email_WithNoAddress_IsNotSetUp`, `Email_TurnedOff_IsNotSetUp`, `Email_Sends_TheLinkOnlyMessage` (the body holds the job's History link and no document text); `Pdf_WithNoPassword_IsNotSetUp`; `Pdf_AttachesAProtectedPdf_PerDocument` (a `plaintext` deliverable's text is escaped to Markdown first; an object one is a fenced block); `Pdf_OfABundle_AttachesOneZip` (`application/zip`, holding one PDF per document); `Pdf_OverTheAttachmentBudget_Fails` (`too-large`, the 2 MB budget of `SendEmailIntakeReplyActivity`); `AMailFault_IsAFailedSend_NotAThrow`.
  - Activity: `AKindWithNoChannel_IsNotSetUp` (`emr`, `zip`).
  - Orchestration: `ACompletedJob_Delivers_ThenCompletes` (calls in order: `MarkDelivering`, `deliver-v31` per send, `FinalizeJob` Completed; custom status `delivering` in between); `AJobWithNothingToDeliver_NeverEntersDelivering`; `AFailedDelivery_LeavesTheJobCompleted` (Review Focus 4); `AFailedJob_DeliversNothing_AndRecordsNotSent`; `ABlockedDeliverable_IsNeverSent`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.** `DurableDeliveryHost` is `DurableRunHost`'s twin (the activity, durable timers, the replay-safe clock). `EntityRunRecorder.DeliveryAsync` logs the row's key, kind, result and attempt count and signals `RecordV31Progress`. The 5–30 delivery path is untouched: `DeliveryRecordTests` and `EmailIntakeReplyTests` stay green.
- [ ] **Step 4: Run** the solution's tests (shared code changed). Expected: PASS.
- [ ] **Step 5: Commit** `Api (#1505): a 31 job delivers its released deliverables, then completes`; push.

### Task 12: Monitoring and alerts (D14)

**Files:**
- Create: `src/Consultologist.Api/V31/OperatorAlerts.cs`, `V31/AlertOperatorV31Activity.cs`, `V31/OperatorWeeklySummary.cs`
- Modify: `Run/IRunRecorder.cs`, `Run/PlanRunner.cs` and `PlanRunner.Agents.cs` (the two notices), `V31/EntityRunRecorder.cs`, `V31/ConsultGenerationOrchestratorV31.cs`, `ApiServices.cs`, `docs/CONFIGURATION.md`
- Test: `tests/V31Run/RunNoticesTests.cs`, `tests/V31Engine/OperatorAlertsTests.cs`, `OperatorWeeklySummaryTests.cs`, `OrchestratorV31Tests.cs`

**Interfaces:**
- Produces: `public sealed record RunNotice(string Kind, string? Key, int Minutes)` and `IRunRecorder.NoticeAsync(RunNotice notice)`; kinds `capacity-wait` (once, when the job's 429 waits pass `PlanRunner.CapacityAlertMinutes` = 5 in all) and `job-slow` (once, at `PlanRunner.SlowJobPercent` = 80 of `jobMinutes`, on a host timer cancelled at the run's end).
- Produces: `public sealed record OperatorAlert(string Kind, string? JobId, string? Key, int? Number)`; `OperatorAlertKinds`: `job-failed`, `capacity-wait`, `job-slow`, `delivery-failed`, and for areas 10 and 11 `publish-failed`, `exporter-warning`. `IOperatorAlerts.RaiseAsync(OperatorAlert alert, CancellationToken ct)`; the activity `alert-operator-v31` calls it.
- Produces: `[Function("OperatorWeeklySummary")]` on `%OperatorWeeklySummarySchedule%`, and `OperatorSummary.Compose(IReadOnlyList<(string Status, TimeSpan? Duration)> jobs)` → subject and body: jobs, failures, p50 and p90 duration (nearest-rank), and D14's two lines.

- [ ] **Step 1: Write the failing tests:**
  - `CapacityWaitsPastFiveMinutes_RaiseOneNotice` (two nodes waiting 3 minutes each: one notice, at the second); `AJobAtEightyPercent_RaisesOneNotice`; `AQuickJob_RaisesNone_AndLeavesNoTimer`.
  - `AnAlert_IsLogged_AndMailedWhenAnAddressIsSet`; `…_IsOnlyLogged_WhenNoneIs`; `AnAlertsBody_HoldsIdsAndCountsOnly`; `AMailFault_NeverFailsTheCaller`.
  - Orchestration: `AFailedJob_AlertsTheOperator` (also a timed-out one and an engine fault), `AFailedDelivery_AlertsTheOperator`, `ACompletedJob_AlertsNoOne`.
  - `TheSummary_CountsAndRanks` (10 jobs, 2 failed, durations 1 to 10 minutes: p50 5 minutes, p90 9 minutes); `WithNoSchedule_TheTimerIsOff` (the pattern `TextRetentionSweep` uses).
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.** The summary reads the week's jobs from the source `Workflow/OperatorUsage.cs` reads. Document `Operators__AlertAddress` and `OperatorWeeklySummarySchedule` in `docs/CONFIGURATION.md`.
- [ ] **Step 4: Run** the solution's tests. Expected: PASS.
- [ ] **Step 5: Commit** `Api (#1505): the operator is alerted of a failed job, a long 429 wait, a slow job and a failed delivery, and sent a weekly summary`; push.

### Task 13: The profile at job start (D15, D16, R8)

**Files:**
- Modify: `src/Consultologist.Api/Models/ConsultGenerationRequest.cs`, `V31/V31JobStarter.cs` (`ProfileAsync`, around 224), `V31/V31StartChecks.cs`, `Jobs/ConsultGenerationState.cs` (`ConsultGenerationJobInitialize`, the state), `Account.cs` (the per-key checks, around 812), `tests/V31Source/V31Contract.cs` (A08-004)
- Test: `tests/V31Engine/V31JobStartTests.cs`, `V31StartChecksTests.cs`, `tests/` the account settings tests beside `DeliveryPasswordTests.cs`, `EmailIntakeProcessorTests.cs`

**Interfaces:**
- Produces: `ConsultGenerationRequest.SignatureBlockId` (`string?`); the entity state's `SignatureBlockId` and `SignatureChosen` (write-once at `Initialize`); `V31StartChecks.Signature(SignatureBlockSet blocks, string? requested)` → `(SignatureBlock? Block, string? Chosen, string? Refusal)`; `V31StartChecks.NoDeliverableCanExist(RunPlan plan, RunInputs inputs)` → `bool`.

- [ ] **Step 1: Write the failing tests:**
  - `TheChosenBlock_IsTheDefault` (`signatureChosen` `default`, `signatureBlockId` the `ChosenId`); `ABlockNamedByTheRequest_IsThisJobs` (`job`, and `profile.signature` is that block's text); `NamingTheChosenBlock_IsStillTheDefault`; `ABlockThatIsNotTheAccounts_RefusesTheStart` (A08-004); `ABlankBlock_CountsAsNotSet` (S06-031); `APackageThatReadsNoSignature_RecordsNeither`; `A30Start_IgnoresTheMember`.
  - `ALetterDetail_OverItsLimit_IsRefused` (120, 200, 4,000; at the limit it saves); `ATimeZone_ThatIsNotIana_IsRefused`.
  - `TheEmailIntake_RepliesWithTheStartsSentence` for S06-031, S06-032 and S06-008 (pin it; fix the reply if it says anything else).
  - `AStart_WhereNoDeliverableCanExist_IsRefused` (R8: `NoApplicableDeliverable`, today's wording); `…_WithADeliverableWhoseIfReadsANode_Starts`; `…_WithAnUnconditionalDeliverable_Starts`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the solution's tests. Expected: PASS.
- [ ] **Step 5: Commit** `Api (#1505): a job's signature block is chosen, checked and recorded by id; Letter details keep their limits`; push.

### Task 14: The guides and the conformance record

**Files:**
- Modify: `docs/customizable-workflow/package-format-library.md` (a section "specVersion 31: checks, deliverables and delivery (preview)"), `docs/customizable-workflow/engine-v31.md` (delivery, the `delivering` state, the channels and what sets each up, the alerts and their settings, the pattern activity), `docs/README.md` if a link is missing
- Content repo: `v31/conformance/README.md` (area 08's section: the A08 table, *Rules already said*, the cases added, `ChecksNotShipped` with reasons), `package-format-v31.md` chapter 6 where D1, D2 and D7 change what it says

- [ ] **Step 1: Write** both guides from the code as built (every setting with its default; D10's flag that `emr` and `zip` have no channel yet).
- [ ] **Step 2: Update** the content repo's README and chapter 6; run `python3 scripts/check-v31.py`; push; move the pin.
- [ ] **Step 3: Run** `dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~RefusalPackages|FullyQualifiedName~CompiledGolden|FullyQualifiedName~DialectGolden|FullyQualifiedName~V31Contract"`. Expected: PASS, the pinned suite equal to what the code gives.
- [ ] **Step 4: Commit** `Docs (#1505): the guides say how a 31 job checks, composes and delivers`; push.

## Addendum: the owner's rulings of 2026-11-22 (#1505, comment 4)

Posted after this plan was written and after Tasks 1 to 7 were built. Tasks 15 to 17 apply them and run before Task 8. Ruling 4 (placements are decided at the gate; deliverables reuse `PlanRunner`'s placement decisions) binds Task 8. Ruling 9 (the two guard traps are build-server matters) needs no work. The comment also voids the carry-over (R7 already says so). Each is recorded in the PR as "ruled by the owner on #1505".

### Task 15: The escape protects leading indentation; shared conformance families are merged (#1505 comment 4, rulings 1 and 3)

Runs before Task 8.

**Rulings (the owner, 2026-11-22, verbatim):**
1. **`markdown_escape` protects leading indentation** (area 07's D7). Today a plaintext line starting with four spaces or a tab becomes a Markdown code block, which breaks part 06 § 6.4 ("the displayed text is exactly the plain text"). Entity-encode the first leading space or tab of each line. Keep line breaks as hard breaks. Update the vectors before 1.0.0 is frozen; deliverables use the fixed escape.
3. **Shared conformance families.** This area adds its cases to `renderer-goldens` and `markdown-escape-vectors` rather than rewriting them (the export currently rewrites them whole: PR #1529 group 4, item 11). Add a test that fails when a case disappears.

**Files:**
- Modify: `src/Consultologist.PackageFormat/V31/Templates/MarkdownEscape.cs`; the in-repo escape vectors and their exporter; the renderer-goldens exporter (find them under `tests/V31Templates/` and its neighbours); the spec prose that describes the escape (part 05 or part 06 § 6.4, and the content repo's twin chapter)
- Content repo: `v31/conformance/markdown-escape-vectors/`, `v31/conformance/renderer-goldens/`, `index.json`, `README.md`
- Test: beside the existing `MarkdownEscape` tests; `tests/V31Run/DeliverableComposeTests.cs`

- [x] **Step 1: Write the failing tests:**
  - `ALineStartingWithFourSpaces_IsNotACodeBlock`, `ALineStartingWithATab_IsNotACodeBlock`: the first leading space or tab of each line is written as its entity (`&#32;` for a space, `&#9;` for a tab); the rest of the line is escaped as today; a line with no leading space or tab is as today; every line of a multi-line text is treated on its own; line breaks stay hard breaks as today.
  - `ADeliverable_EscapesLeadingIndentation`: a `markdown_block` deliverable reading a plain-text value `"    indented\n\tx"` composes with the entities.
  - `TheSharedFamilies_KeepEveryCase`: pins the names of every case of `renderer-goldens` and every vector of `markdown-escape-vectors` as they are at the pin before this task (an in-repo list, as `DialectGoldenTests` pins the 40 area-07 dialect cases); it fails when one disappears. A vector's expected bytes may change (ruling 1); a name may not vanish.
- [x] **Step 2: Run.** Expected: FAIL.
- [x] **Step 3: Implement** the escape. Make the exports of `renderer-goldens` and `markdown-escape-vectors` **merge**: a case already in the content repo that the export does not produce is kept, and nothing is deleted silently.
- [x] **Step 4: Regenerate and read** every golden the escape changes (escape vectors; the `deliverable-escaped` dialect golden; run goldens if any); add vectors for a leading space, a leading tab, and a mid-line run of spaces (unchanged). Export one filter at a time, `python3 scripts/check-v31.py`, push the content repo, move the pin. Update the prose that describes the escape (spec part and content-repo chapter).
- [x] **Step 5: Run** the V31 filter. Expected: PASS.
- [x] **Step 6: Commit** `PackageFormat (#1505): markdown_escape protects a line's leading indentation`, `Tests (#1505): shared conformance families are merged, and a vanished case fails`; push.

### Task 16: Folder items are `markdown_block`; A07-013 at every depth; a parser fault is the engine's (#1505 comment 4, rulings 2, 5, 6)

Runs before Task 8.

**Rulings (verbatim):**
2. **A folder item's `.content` is `markdown_block`** at 31 (PR #1529 group 4, item 12). Write it into part 05 § 6.2: the folder's `format` types the header; the body is `markdown_block`, as 30's standards bodies are.
5. **A07-013 at every depth.** Keep A07-013. Close its depth hole: today only `item.item` is checked, and `item.item.item` and deeper must be refused too. Write the rule into part 05 § 6.2: a body passed out of its loop is its raw text, and must be plain.
6. **`TemplateSyntax.Parse` no longer catches everything.** An unexpected exception is logged and reported with an engine-fault sentence of its own, distinct from A07-005, so an engine bug never reads as the author's fault. Add it to `refusal-sentences.md`.

**Files:**
- Modify: `docs/customizable-workflow/package-format-v31/05-templates-and-macros.md` § 6.2 (and the content repo's twin chapter), `docs/customizable-workflow/package-format-v31/refusal-sentences.md`, `src/Consultologist.PackageFormat/V31/Templates/SourceTemplates.FolderItems.cs` and `SourceTemplates.FolderRoutes.cs` (A07-013), `Templates/TemplateSyntax.cs` (the `catch (Exception …)` near line 104), `Templates/DialectSentences.cs`, `tests/V31Source/V31Contract.cs` (`ChecksAreaSentences`: A08-006)
- Test: `tests/V31Templates/FolderItemTests.cs`, `TemplateSyntaxTests.cs`, `DeliverableFormatsTests.cs`, `tests/V31Source/RefusalPackages.Checks.cs`

**Area sentence:** A08-006 `<Subject> couldn't be checked: the engine failed reading its template. This is a fault in the engine, not in the package; report it.` No package content produces it, so it has no refusal package: list it in `ChecksNotShipped` with that reason.

- [x] **Step 1: Write the failing tests:**
  - `AFolderItemsContent_IsMarkdownBlock`: in a `markdown_block` deliverable `{{ item.content }}` over a snippet folder is written as it is and is not in `escapes`, whatever the folder's `format`; in a `plaintext` deliverable it is S06-021. (Where the code already does so, the test pins it.)
  - `AFolderItemCarried_ThreeLoopsDeep_IsRefused` and a four-deep one (A07-013): a body with a tag passed out of its loop through `item.item.item…`, to a function parameter and to an expression.
  - `AnUnexpectedParserException_IsAnEngineFault`: through the smallest internal test seam that makes the parser throw an unexpected exception, `Parse` gives one A08-006 fault, not A07-005; what is logged or carried is the exception's type only, never the body's text; `OutOfMemoryException` still propagates; Scriban's expected parse errors and the depth limit are still A07-005.
- [x] **Step 2: Run.** Expected: FAIL.
- [x] **Step 3: Implement.** Only what Scriban throws for unreadable text is A07-005; anything else is A08-006. If the library has no logger, carry the exception's type on the fault for the caller to log, and record the choice under Decisions.
- [x] **Step 4: Write the spec:** part 05 § 6.2 gains ruling 2's and ruling 5's sentences; `refusal-sentences.md` gains A08-006; the content repo's chapter 5 twin and the conformance README's A08 table. Add a refusal case for the deeper A07-013; export one filter at a time, check, push, pin.
- [x] **Step 5: Run** the V31 filter. Expected: PASS.
- [x] **Step 6: Commit** `PackageFormat (#1505): a folder item's body is markdown_block, A07-013 holds at every depth, and a parser fault is the engine's`; push.

### Task 17: The 31 catalogue is resolved only under preview; one golden switch (#1505 comment 4, rulings 7 and 8)

Runs before Task 8.

**Rulings (verbatim):**
7. **The 31 agents catalogue is resolved at startup only when `PreviewSpecVersions` admits 31** (D9; `ApiServices.ResolveAtStartup`). A 5–30 host does not touch `agents/v31`. Make the `int.Parse` and `!` there fail inside the `FormatException` wrap, with a clear message. The release conditions are on #1508.
8. **One golden switch.** `FormatHashGoldenTests` reads `UPDATE_V31_GOLDENS`, while every other golden test reads `GOLDEN_UPDATE_V31`. Rename it to `GOLDEN_UPDATE_V31` (group 6, trap 3; fixed rather than documented).

**Files:**
- Modify: `src/Consultologist.Api/ApiServices.cs` (`ResolveAtStartup`, near line 34), `src/Consultologist.Api/V31/V31AgentsCatalogue.cs`, `tests/V31Types/FormatHashGoldenTests.cs`, `docs/customizable-workflow/package-format-library.md` and `engine-v31.md` where they name the switch or the startup resolve
- Test: `tests/V31Engine/`, beside the existing catalogue tests

- [x] **Step 1: Write the failing tests:** `WithPreviewUnset_StartupNeverReadsTheV31Catalogue` (a catalogue source that throws when touched; startup succeeds); `WithPreviewAdmitting31_StartupResolvesIt` (an incoherent catalogue still stops startup); `ACatalogueWithANonIntegerVersion_FailsWithAClearMessage` and `ACatalogueWithAMissingMember_FailsWithAClearMessage` (the failure is the catalogue's wrapped startup error naming the file and the member, never a bare `FormatException` or `NullReferenceException`).
- [x] **Step 2: Run.** Expected: FAIL.
- [x] **Step 3: Implement**; rename the switch in `FormatHashGoldenTests` and in the current guides (not in earlier areas' plans).
- [x] **Step 4: Run** the solution's tests (shared code). Expected: PASS.
- [x] **Step 5: Commit** `Api (#1505): the 31 agents catalogue is resolved at startup only under preview`, `Tests (#1505): FormatHashGoldenTests reads GOLDEN_UPDATE_V31`; push.

## Spec coverage

| Spec | Task |
|---|---|
| Part 06 § 1 checks: folder, root, file, who reads what | 3, 5 |
| § 2 `check_result` and the three operations | built by area 04 (S03-029, S03-030, A04-010; evaluation); 3 holds the format; 8 reads the result (D6) |
| § 3 check loops | 3 (D17); running them was area 06's |
| § 4 aggregates and the gate | 3, 5, 8 |
| § 5 checks in the record and History | reasons and effects: 8; rows and pages: areas 09, 12 |
| § 6.1 the file, § 6.3 what it reads | 4, 5; R-D3, R-D8, R-D9 already said |
| § 6.2 item states | area 07 (S05-009); `reason` was area 06's |
| § 6.4 strict formats | 6, 7 |
| § 6.5 headings | no engine heading is written (area 07's composer); 7 pins the templates |
| § 6.6 empty, § 6.7 terminal, § 7 outcomes | 8 |
| § 8 delivery | 2 (the contract), 4 (the folder), 9, 11 |
| § 9 deliverables in the record | outcome members: 8, 9; `sites`, `substitutions`: area 09 |
| § 10 the profile | 13; the closed list, `needs.profile` and the start refusals were areas 05 to 07's; pages: area 12 |
| Refusals R-C, R-D, R-F, J, H | 3 to 6, 13; *Rules already said*; H sentences: area 12 from 8's and 9's reasons |
| Part 09 § 3.8 delivery at the end | 9, 11 |
| Part 09 § 3.9 monitoring and alerts | 12 |
| #1505 rulings | 1 (R5, R7), 2 (R4), 10 (R6) |
| #1507 items 1, 3, 7 | 8 (R1 to R3); the start refusal 13 (R8) |
| #1505 comment 4, rulings 1 to 8 | 15 (1, 3), 16 (2, 5, 6), 17 (7, 8), 8 (4) |
