# Area 11: Registry and Publish (specVersion 31) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publish a specVersion 31 package end to end: receive, resolve, validate, run its in-package tests offline, build the publish report, and write the version's folder with the stamp last; serve what was published through `WorkflowPackages/Current` and `Current/Content`; and apply the owner's rulings on #1508 to the code areas 05 to 09 built.

**Architecture:** The pipeline is a pure library call, `PackagePublish.RunAsync` in `src/Consultologist.PackageFormat/V31/Publish/`, over what exists: `CompiledPackage.Publish` (steps 0 to 2 and the pair), `RunPlan.Build` and `OfflineRun.RunAsync` (the harness the tests run on). It gives a `PublishOutcome`: the report, and, when published, the files to write in part 07 § 5's order. The Api's `V31PackagePublisher` calls it with the engine's registry, toolchain and limits, and `V31RegistryWriter` writes the folder (source, `lock.json`, `publish-report.json`, `resolve.json`, then `publish.json`), which `CompiledPackageStore` already loads. Every 31 branch in the Api is entered only through `SpecVersionAdmission`, so with the preview setting unset nothing answers differently.

**Tech Stack:** C# / .NET 10, `System.Text.Json` (`JsonNode`), Azure Functions (isolated), `Azure.Storage.Blobs` through the existing container factory, xUnit and NSubstitute in `tests/Consultologist.Api.Tests.csproj`; Python 3 for `scripts/check-v31.py` in the content repo.

**Spec:** `docs/customizable-workflow/package-format-v31/` — [`07-compiled-documents.md`](../07-compiled-documents.md) in full; [`09-engine-and-pages.md`](../09-engine-and-pages.md) §§ 1, 2, 3.5, 4, 8.2, 9; [`02-types-and-inputs.md`](../02-types-and-inputs.md) § 12; [`10-migration-and-tools.md`](../10-migration-and-tools.md) §§ 1.5, 4, 5. **The owner's rulings are the comments on #1508** (`gh issue view 1508 --comments`; the first four for Parts A to E, those of 2026-11-25 and 2026-11-26 for Part F): read them before your task; where a ruling and the spec differ, the ruling is the decision and the spec text is corrected in the task that applies it. How the library and the engine work today is in `docs/customizable-workflow/package-format-library.md` (the "specVersion 31" sections) and `docs/customizable-workflow/engine-v31.md`.

**Not this area's** (each has a seam here): the 31 diagram and `dag.mmd`'s bytes (area 13: publish writes what `PublishOptions.Diagram` gives, and nothing when it is null); the Create page, History, the Editor's bar, "Download package" and showing the report (area 12: this area gives the response members); the Editor's "Run tests" (Milestone 41); the public repository's tag CI and Release assets (`consultologist-workflows` is read-only for this build: this area gives the script CI will call); running the bulk publish on a deployment, sending owners their notices, releasing contracts, Azure settings (the user's; Part F builds the bulk publish's tool).

## Global Constraints

- **Worktree only:** `~/Projects/worktrees/m39-area-11` on `m39-area-11`. Content repos are worked in `~/Projects/consultologist-package-format` (and `-provenance`, `-agents` if a task says so) on `m39-build`, pushed directly; a pin under `external/` moves only to a pushed commit. `~/Projects/consultologist-workflows` is read-only.
- **Never edit** `.github/`, `.claude/`, `scripts/m39-build/`, `AGENTS.md`, `CLAUDE.md` or `BUILD-RUNBOOK.md`, in any repo. A shell command that names one of those paths with a `>` in it is refused by the guard: write files with the file tool. Never force-push; a pushed commit is never amended.
- **31 stays off in production:** `WorkflowPackageValidator.AcceptedSpecVersions` and `WorkflowPackageStore.SupportedSpecVersions` do not change. Every 31 branch asks `SpecVersionAdmission.Admits(31)` (or `Runs31`); when it says no, a 31 publish, import, `Current` or `Content` answers S09-001 (`SpecVersionAdmission.NotSupported(31)`) as a 400, never a 500.
- **The 5–30 path keeps its behaviour:** no 5–30 golden, capture, response or expectation changes. `[Function]` strings and routes stay. Response records change by trailing optional members only.
- **A stamped version folder is immutable.** Nothing writes into a folder that has `publish.json`. An unstamped folder does not exist for any reader.
- **Evidence is never accepted from an upload:** `resolve.json`, `lock.json`, `publish-report.json` and `publish.json` in an upload are dropped unread.
- **No sentence, report, log line, alert or telemetry member carries a value** from an input, a model, a profile or a test's fictional text: only package ids, keys, paths, counts, limits, hashes and package text. A test mismatch on text reports hashes (part 07 § 9).
- **Never throw on package content.** `ArgumentNullException` for a null argument is the only exception a public library method throws. An engine fault is A08-006's shape (`SourceRefusal.EngineFault`), logged by the caller.
- **Invariant culture, ordinal comparison,** everywhere. Canonical JSON is `CanonicalJson` (RFC 8785); a hash is `sha256:<64 lowercase hex>` (`CanonicalJson.Hash`, `CanonicalJson.HashText`).
- **Sentences are used as written.** S07-007 to S07-016 are `v31/sentences.json`'s rows, word for word. A new sentence is an area sentence `A11-NNN` (table below), added to `tests/V31Source/V31Contract.cs` (a dictionary `PublishAreaSentences` and an `A11-` branch in `AssertSentence`, as A10 has), to `docs/customizable-workflow/package-format-v31/refusal-sentences.md`, and to the conformance README's tables (a new "Publish (area 11)" table).
- **Fictional only:** invented people and clinical text (Margaret Okafor, Dr A. Sample, Sample Oncology Clinic).
- **Goldens come from code,** are read after regeneration (`GOLDEN_UPDATE_V31=1`), and every new or changed golden or capture file is listed in the task's last commit message body, for the PR's *Goldens to review*. Run the conformance exports one filter at a time (`CONFORMANCE_EXPORT_V31=<content repo root>`), and read `git status` in the content repo after each.
- **Commits:** small, by concern: `PackageFormat (#1508): …`, `Api (#1508): …`, `Tests (#1508): …`, `Docs (#1508): …`; in a content repo `Format spec (Consultologist-Blazor#1508): …`. Every message ends with exactly `Co-Authored-By: Claude <noreply@anthropic.com>`. Push after each task (`git push -u origin m39-area-11` the first time).
- **Tests:** library tests in `tests/V31Publish/` (new), engine tests in `tests/V31Engine/`. Command: `dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~V31"`; a task that touches shared code (`WorkflowPackages.cs`, `WorkflowPackagePublisher.cs`, the models, `ConsultGeneration*`) also runs `dotnet test Consultologist.sln --filter "FullyQualifiedName!~Consultologist.Web.E2E.Tests"`. After a content-repo edit run that repo's `python3 scripts/check-v31.py`.
- **Each task ticks its own checkboxes in this file** in its last commit, and adds a line to *Recorded in the build* (at the end) for any choice it had to take that this plan does not state.

## Decisions this plan takes

Each goes in the PR body under *Decisions made during build*. R = "ruled by the owner on #1508"; D = where the spec is silent or two parts differ.

| # | Where | Choice |
|---|---|---|
| R1 | #1508 comment 2, item 1 | Publish **refuses** a package whose worst case opens more than `jobInstances` instances: the product of the caps along each nesting path (05-D41's walk), counted over instances, against `PublishLimits.Instances`. A11-001. An unbounded path is not refused (the run-time limit holds it); the note says so as S04-073 already does. |
| R2 | comment 2, item 2 | Bundled formats reference each other by `$id` (the URN): `resolve.json`'s `formats` is a JSON Schema 2020-12 compound document. Inputs and entries still write `{ "$ref": "#/$defs/<id>" }`. Published format files keep their bytes; only a reference *inside* a bundled format is rewritten to the target's `$id`. |
| R3 | comment 2, item 3; comment 3, item 1 | Every compiled member is named in part 07 and `resolve.schema.json` (`plainConverts`, `delivery`, `bundle`, `derivedFrom`, `"ret": null` on `if` and `case`, `names`, `placements`, `runSpots.unit`, `runSpots.neutral`, embedded built-in renderers), the stamp gets `publish-stamp.schema.json`, and then `entry` is closed (`additionalProperties: false`, or `unevaluatedProperties: false` where the schema composes). Part 09 § 1.2 step 3 is reworded: the pointer names the lock by file name, and the stamp carries `lockHash`. |
| R4 | comment 2, item 4 | A format file that names a format `x` and also defines `x` in its own `$defs` is refused: A11-002. No byte change. |
| R5 | comment 2, item 5 | The run's format-expansion budget (`PlanFormats.NodeBudget`, 1,048,576 nodes) is applied at publish: A11-003. |
| R6 | comment 3, item 2 | `requires.features` lists `run-spots` only when a *placed* unit (or a body) has a run spot; an unplaced snippet does not count. Goldens regenerated. |
| R7 | comment 3, item 3; comment 4, last list | The conditions before 31 is admitted outside preview are written in `docs/customizable-workflow/registry-operations.md` ("Before 31 leaves preview") and in `engine-v31.md`. |
| R8 | comment 4, items 1 to 8 | The fixes to areas 08 and 09's code and the spec edits, Tasks 1 to 5, **before** this area's own tasks. |
| R9 | comment 1 | `expect.deliverables` accepts `{ "state": "skipped" }` alone (already in part 07 § 8.3); the test runner and `test.schema.json` allow it. |
| D1 | part 07 § 5 step 4.5 "the regenerated `dag.mmd`" | Area 13 owns the drawing. `PublishOptions.Diagram` is `Func<string, string>?` (the canonical `resolve.json` text → `dag.mmd`'s text); null until area 13, and then no `dag.mmd` is written. An uploaded `dag.mmd` is never stored (it is a view, regenerated). |
| D2 | part 07 § 5 step 0 "a version that already exists is refused"; § 2.2 `package` | For an account publish the registry chooses the name and assigns the next CalVer, as at 5–30, and **writes both into `manifest.json`'s `name` and `version`** (a `JsonNode` member replacement that keeps every other member and its order) before step 0, so `sourceHash` covers the manifest as stored. A version whose folder has a stamp is refused with today's conflict answer; an unstamped folder is overwritten (its blobs deleted first). A11-004 when the manifest's `name` or `version` is not a string. |
| D3 | part 07 § 5 "the engine's `WorkflowPackages/Publish` endpoint" | A 31 publish is the same route and request record: `Manifest` is null and `Files` holds every source file by path, `manifest.json` included (text; the endpoint encodes each as UTF-8 without a byte-order mark). The publisher takes the 31 branch when `Files["manifest.json"]` parses as an object whose `specVersion` is the integer 31. The response gains trailing `Report` (`JsonElement?`, the publish report in full) and, on a refusal, the 400's body carries `error` (the first sentence) and `report`. |
| D4 | part 07 § 9 `publishedBy` | `editor` when the request's `Source` is the Editor's value as today, `operator-migration` when the request carries `MigrationReport` (D5), `api` otherwise; `ci` with the tag (`ci <tag>`) is written only by the script (Task 20). |
| D5 | part 10 § 1.5 "recorded as a migration"; S09-004 | A publish request may carry trailing `MigrationReport` (`JsonElement?`, area 10's `migration-report.json`). It is accepted only from a caller the operator routes admit (the authorisation `Admin/Drain` uses), validated to have `oldVersion` and `newVersion` strings, written as `migration-report.json` beside the publish report (before `resolve.json`), and the stamp gains `migratedFrom` (the old version; `PublishedStamp.TryRead` ignores it). *"migrated by the operator from <old version>"* (S09-004) is composed from the stamp by `Current/Content` (member `MigratedFrom`). S10-005 is said by the script and the operator's tools when a package is left at 30; the publish path has no caller for it in this build beyond `PackagePublishSentences.StaysAt30`. |
| D6 | part 07 § 8.2 "a node that runs with no stub is an error" | `OfflineRequest` gains `bool GenerateMissing = true`; the test runner passes `false`, and `OfflineHost` then records the row key in `Missing` and ends the call as a failed node. The runner reports S07-008 for the first missing key in ordinal order, in place of S07-007. |
| D7 | part 07 § 8.3 "only the first mismatch is reported"; § 9 `firstMismatch` | Order: a start refusal or a run that ends other than completed (when `expect` does not say so through a deliverable's `failed`) is the mismatch `{ expectation: "run", key: "", expected: "completed", actual: <state> }`; then `ran`, `skipped`, `decisions`, `loops`, `values`, `prompts`, `checks`, `deliverables`, each by key in ordinal order. `expected`/`actual` are hashes for `prompts` and `deliverables` text, canonical JSON text for `values`, plain words otherwise (`ran`, `skipped`, `not run`, `true`, `false`, a branch id, `count 3`, `passes 2, stopped max`, a state). A key the run has no row for is `actual: "not run"`. |
| D8 | part 07 § 9 `outcomeHash` | SHA-256 of the canonical JSON `{ "state", "rows": [{ "key", "state" }…], "decisions": [{ "id", "result" }…], "values": { key: value… }, "prompts": { key: hash… }, "deliverables": { key: { "state", "hash" }… } }`, rows and decisions in ordinal key order. `durationMs` is not in it. |
| D9 | part 07 § 8.4 "all tests together have 60 seconds" | The limit is `constants.json`'s `limits.testsSeconds` (60), held as `PackageTests.LimitSeconds` and pinned to the constant by a test. Time is read from `PublishOptions.Clock` (`TimeProvider`), checked between tests and by a `CancellationToken` cancelled at the limit; past it publish stops with S07-013 alone and the tests run so far are in the report. Tests run one at a time, in id order. |
| D10 | part 07 § 9 "a refused publish returns the report without writing it" | A refused report's `resolveHash` and `lockHash` are null when publish stopped before step 4 (a refusal at steps 0 to 3 is always before it), `publishedAtUtc` null, `tests` the results so far. `sourceHash` is `""`-free: when intake refuses before a hash can be taken, it is the hash of the files as received after normalisation (`SourceHash.Of`), and when even that is impossible (no readable file), the hash of the empty listing. |
| D11 | part 07 § 4.4 the corpus re-render | `scripts/rerender-workflow-packages-v31.cs`: for each stamped 31 version in a folder of registry copies, re-run steps 0 to 3 in memory with the current build's toolchain and compare `resolveHash` and each test's `outcomeHash` with the stored `publish-report.json`; any difference is printed and the exit code is 1. Run under the current and the candidate library version, it is § 4.4's check. Break points stay engine configuration (area 06). |
| D12 | part 09 § 4.2 `Blocks` "in the graph's order" | Top-level entries of `nodes` (those with `parent: null`), ordered by their position in `graph.order`. `Results` are `deliverables` in `graph.order` too. |
| D13 | part 09 § 4.3 `Fields[].Required`; part 02 § 12 | A field is `Required` when its type is not nullable (part 02 § 12's wording, the later text). `Label` is the property's `title`, the property name when it has none. |
| D14 | part 09 § 8.2 "every file under the package root, by path" | `Files` holds every stored source file but `manifest.json` (which is `Manifest`, parsed from the stored bytes with no member added or dropped), and `dag.mmd` when stored; `Resolve`, `Lock` and `PublishReport` are `JsonElement?` parsed from the stored bytes, null below 31. |
| D15 | part 07 § 7 import | `WorkflowPackages/Ingest` of a 31 zip (admitted) returns the content response with the source files and `SpecVersion` 31, `Resolve`, `Lock` and `PublishReport` null, and a trailing `Notices` (`IReadOnlyList<string>?`) holding S07-016 once when the archive held an evidence file. Nothing is published by an import. |
| D16 | part 09 § 3.5 "its estimated worst case passes the job limit" | `PublishLimits` gains `JobMinutes` and `Instances`; `PublishNotes.Of` adds the worst-case-minutes note (area 10's estimate, `MigrationReport.WorstCaseNote`'s arithmetic moved into `PublishNotes`, the sentence S09-016) after S04-080. All new `PublishLimits` members are trailing and optional (null means "not measured") so the compiled goldens of areas 05 to 10 change only where a task says so. |

### Area sentences

| Id | Sentence | Audience |
|---|---|---|
| A11-001 | *"Up to \<n\> steps could run in one job, through \<loop key\>; this engine runs at most \<limit\> for one job. Lower a loop's max or split the workflow."* | author |
| A11-002 | *"Format '\<id\>' names format '\<name\>' and also defines '\<name\>' in its own $defs. Rename the definition or remove it."* | author |
| A11-003 | *"Format '\<id\>' expands to more than \<limit\> parts, more than this engine can run. Make it smaller."* | author |
| A11-004 | *"manifest.json must give name and version as text."* | author |
| A11-005 | *"Test '\<test id\>' \<member\> is not written as the test grammar says: \<what is wrong\>."* (a member of `expect` of the wrong shape, an unknown expectation, an `outputs` key that is no row key) | author |
| A11-006 | *"Version \<version\> of \<package\> is already published. Publish a new version."* | author |
| A11-007 | *"The workflow couldn't be published because the engine failed. Tell the operator."* (an engine fault at publish; operator audience in the log, this to the caller) | author |
| A11-008 | *"\<Subject\> couldn't be rendered because the engine failed. Tell the operator."* (R8 item 4: the run-time engine fault's own sentence, replacing the reuse of A06-008) | clinician |

A task that needs another sentence adds the next id and records it.

## Review Focus

1. **A publish that dies between `resolve.json` and the stamp, then is retried** with different source: the retry overwrites the unstamped folder completely (no stale file from the first attempt is left beside the stamp). Test in Task 17.
2. **Two publishes of the same name racing for one CalVer:** exactly one stamps; the other retries on the next version or answers A11-006, and never mixes files. Test in Task 17.
3. **A test file that is valid JSON but hostile in shape** (`expect.values` an array, `outputs` keyed by `node.x:` or a 10,000-character key, `run.date` a number): A11-005 or S07-009 to S07-012, never an exception. Test in Tasks 12 and 13.
4. **A 31 pin met with the preview setting off** (the setting removed on a deployment whose registry holds 31 versions): `Current`, `Content` and job start answer S09-001 as a 400, not a 500. Test in Tasks 18 and 19.
5. **A test whose stubbed text would leak into a sentence** (a mismatch on `values` holding fictional clinical text is allowed as the canonical value per § 9, but a `prompts` or `deliverables` mismatch must show hashes only; S07-009's `<first validation error>` names the path and the keyword, never the value). Test in Task 13.

---

## Part A: the rulings on built code (R8), first

### Task 1: A send in flight at the window's close is waited for

**Files:** Modify `src/Consultologist.PackageFormat/V31/Run/DeliveryRunner.cs` (`Close()`, about line 237), its caller in `src/Consultologist.Api/V31/`; Test `tests/V31Run/DeliveryRunnerTests.cs` (or the file that holds the window tests; find with `grep -rn "failed: window" tests/`).

**Interfaces:** Produces: no signature change if `Close()` is already async; otherwise `Task CloseAsync()`.

- [x] **Step 1: Failing tests.** `ASendInFlightAtTheWindowsClose_IsWaitedFor_AndRecordedAsItEnded` (the host's send completes after the window closes: the destination's row is `sent`, never `failed: window`); `ASendInFlightThatFails_AfterTheClose_IsFailedWithItsOwnReason_AndNotRetried`; `ADestinationNeverStarted_AtTheClose_IsFailedWindow` (unchanged behaviour).
- [x] **Step 2: Run, see them fail.**
- [x] **Step 3: Implement.** At the close, await every in-flight send (the activity has its own timeout), land its result, then fail only destinations with no send in flight. No new attempt starts after the close. The orchestration stays deterministic: await the host's tasks only.
- [x] **Step 4: Run** `--filter "FullyQualifiedName~V31"`; pass. Update `engine-v31.md` "Delivery" and `package-format-library.md` where they describe the close.
- [x] **Step 5: Commit** `PackageFormat (#1508): a send in flight at the window's close is waited for`; push.

### Task 2: `sentFormatHash` before wrapping; `failWith` from the literal

**Files:** Modify `src/Consultologist.PackageFormat/V31/Templates/EnvelopedFormat.cs:39`, `tests/V31Templates/AgentTurnTests.cs:324-325`, `src/Consultologist.PackageFormat/V31/Run/RecordRows.cs:173`, `docs/customizable-workflow/engine-v31.md:309`, `docs/customizable-workflow/package-format-library.md:1926`.

- [x] **Step 1: Failing tests.** In `AgentTurnTests`: for a non-object root, `sentFormatHash` equals `prepared.Hash` (the format before the envelope); change the existing pin. In the record-rows tests: `AnAggregatesFailWith_IsTheOperationsLiteral_NeverTheValuesMessage` (a `check_result` value whose `message` differs from the `all_pass`/`count_pass` operation's `failWith` literal: the row holds the literal; a check with no such operation holds null).
- [x] **Step 2: Run, fail.**
- [x] **Step 3: Implement.** `EnvelopedFormat`: return `prepared.Hash` in the wrapped branch. `RecordRows`: read `failWith` from the entry's `expression` (the `failWith` argument of its `all_pass` or `count_pass`), never from the value.
- [x] **Step 4: Fix the two stale descriptions of S05-056** (it no longer names the model's id) at the lines above. Regenerate any run or record golden that moves (`GOLDEN_UPDATE_V31=1`), read the diff, list the files.
- [x] **Step 5: Run V31 tests; commit** `PackageFormat (#1508): sentFormatHash is taken before wrapping, and failWith comes from the operation's literal`; push.

### Task 3: Engine faults are the engine's

**Files:** Modify `src/Consultologist.PackageFormat/V31/Run/RunSentences.cs`, `src/Consultologist.PackageFormat/V31/Templates/BodyComposer.cs` (lines 44–99, 480), `src/Consultologist.PackageFormat/V31/Templates/DialectSentences.cs:64`, `tests/V31Source/V31Contract.cs`, `refusal-sentences.md`, the content repo's `v31/conformance/README.md`.

**Interfaces:** Produces: `RunSentences.EngineFailedRendering(string subject)` → A11-008. `SourceRefusal.EngineFault` stays the carrier a caller logs (Task 16 logs it at publish).

- [x] **Step 1: Failing tests.** `AnEngineFaultWhileRendering_FailsTheNodeWithA11008_NotA06008`; `ARenderThatFailsOnPackageContent_StillSaysA06008`; a contract test that A08-006's audience is `operator` in `V31Contract` and `refusal-sentences.md`.
- [x] **Step 2 to 4: Run, implement, run.** Add `PublishAreaSentences` to `V31Contract` with A11-008 and the `A11-` dispatch branch; add the README table "Publish (area 11)" in the content repo with A11-008 (later tasks append rows), push it on `m39-build`, move the pin.
- [x] **Step 5: Commit** `PackageFormat (#1508): an engine fault at run time has its own sentence, and A08-006 is the operator's`; push.

### Task 4: The smaller safety fixes

**Files:** find each with grep in `src/Consultologist.Api/V31/` and `src/Consultologist.PackageFormat/V31/Run/`: the job time limit (`jobMinutes`, `job-slow`), the operator alert (`OperatorAlert`), OCR (`prebuilt-read`, `AnalyzeResult`), S06-035 (`tests/V31Source/` checks tests).

- [x] **Step 1: Failing tests,** one per item of #1508 comment 4, item 5: (a) `S06035_FailsClosed_ThroughAGatesDependencies` (a gate whose `if` reads a node that reads a nullable value, two steps away: refused); (b) `TheJobTimeLimit_IsCheckedAfterAHostRulesAnswerReturns` (a rule activity that returns after the limit: the job fails with S09-012 before anything else is dispatched); (c) `TheOperatorAlert_IsItsOwnActivity` (the orchestrator schedules an alert activity, named `send-operator-alert-v31`, and does not await a send inline; a failed alert never fails the job); (d) `TheOcrModelsDatedVersion_IsRecorded` (`AnalyzeResult.ModelVersion`, not `prebuilt-read` alone, in the input's extraction versions).
- [x] **Step 2 to 4: Run, implement, run** the whole non-E2E suite (shared engine code). If (a) passes as written, keep the test and change nothing.
- [x] **Step 5: Commit** per item (`Api (#1508): …`, `Tests (#1508): …`); update `engine-v31.md` (the drain's list of new history events gains the alert activity); push.

### Task 5: The ruled spec edits, and the check's sentences

**Files:** Modify `docs/customizable-workflow/package-format-v31/02-types-and-inputs.md` § 8.2, `09-engine-and-pages.md` § 5.1 (the documents bullet) and § 1.2 step 3 (R3), `03-expressions.md` § 4.1, `06-checks-deliverables-profile.md` § 6.7; in `~/Projects/consultologist-package-format`: `package-format-v31.md` (chapter 3 § 3.4.1 and the S03-019 row, and the twins of the edits above), `v31/sentences.json` if a row's words change; in `~/Projects/consultologist-provenance`: `hash-definitions.md` (`effectiveInputHash` definition 7); `src/Consultologist.PackageFormat/V31/CheckSentences.cs` and wherever a check's failure says "Computed node".

- [x] **Step 1: Edit the texts** exactly as #1508 comment 4 items 6 and 7 say: a document sent to a Markdown input is "extracted text, as at 30" (the converter is Milestone 40, #1442), in part 02 § 8.2, part 09 § 5 and definition 7; `checks/` may read a deliverable's `.content`, `.value` and `.state`; a gate comes after its deliverables in § 6.7's order; part 09 § 1.2 step 3 says the pointer names the lock by file name and the stamp carries `lockHash`.
- [x] **Step 2: Failing test** `AChecksFailure_SaysCheck_NotComputedNode` for every sentence a check can fail with at publish and at run time; implement by passing the subject (`Check '<id>'`) through.
- [x] **Step 3: Run** V31 tests and both content repos' checks (`python3 scripts/check-v31.py`; provenance's own check script if it has one). Regenerate the refusal packages whose sentence moved (`RefusalPackagesExport`), read the diff.
- [x] **Step 4: Commit** in each content repo, push `m39-build`, move the pins; commit here `Docs (#1508): the ruled spec edits …` and `PackageFormat (#1508): a check's failure says "Check"`; push.

## Part B: the compiled documents, named and closed

### Task 6: Name every compiled member; the stamp's schema

**Files:** Modify `docs/customizable-workflow/package-format-v31/07-compiled-documents.md` §§ 2.2, 2.5, 5; in the content repo `package-format-v31.md` (its twin chapter), `v31/schemas/resolve.schema.json`, Create `v31/schemas/publish-stamp.schema.json` with `v31/conformance/schemas/publish-stamp/` examples (valid: the three members; the three with `migratedFrom`; invalid: a missing hash, an uppercase hash, `specVersion` 30), `scripts/check-v31.py`.

**Interfaces:** Produces: the schema every later golden validates against. The stamp: `{ "specVersion": 31, "resolveHash", "lockHash", "migratedFrom"?: string }`, other members refused by the schema (the reader still ignores them).

- [x] **Step 1: List what the compiler writes.** Run the compiled goldens through a throwaway script that collects every member name per entry kind, and compare with part 07 § 2.5 and the schema. The ruled list (R3) is the minimum; name anything else found.
- [x] **Step 2: Write part 07** (a row per member in § 2.5's tables: `names`, `placements` with its five members, `runSpots.unit`, `runSpots.neutral`, `plainConverts`, `derivedFrom` in § 2.2, `"ret": null` on `if` and `case`, embedded built-in renderers in § 2.2's `renderers` row; § 5 step 4.6 names `publish.json`'s members) and the schema. **Do not close `entry` yet** (Task 9 does, after the goldens settle).
- [x] **Step 3: `check-v31.py`:** a guard that every member name the schema's entry definitions name appears in `package-format-v31.md`, and the same for the record's member names (#1508 comment 4 item 7, last bullet: "guards the new compiled and record member names"); a `publish-stamp` schema group.
- [x] **Step 4: Run** `python3 scripts/check-v31.py`; here `--filter "FullyQualifiedName~V31"` after moving the pin (the compiled goldens must still validate).
- [x] **Step 5: Commit** there and here (`Docs (#1508): part 07 names every compiled member`); push both.

### Task 7: Formats in the bundle reference each other by `$id`; `requires` stays minimal

**Files:** Modify `src/Consultologist.PackageFormat/V31/CompiledFormats.cs`, `src/Consultologist.PackageFormat/V31/Run/PlanFormats.cs` (resolving a `$ref` that is a URN through the bundle's `$id`s), `src/Consultologist.PackageFormat/V31/ResolveCompiler.Requires.cs`; Tests `tests/V31Resolve/`; goldens `tests/V31Resolve/Golden/*`, `tests/V31Run/Golden/*`.

- [x] **Step 1: Failing tests.** `AFormatThatNamesAnother_ReferencesItByItsId` (in the bundle, `{ "$ref": "urn:consultologist:…:format:<id>" }`; an input's and a node's `format` stays `#/$defs/<id>`); `TheBundle_ValidatesAsOneCompoundDocument` (Corvus, through our interface, validates a value against a format that references another by `$id`); `ThePlan_ExpandsAUrnReference` and `FormatHashesAndTypeHashes_DoNotMove` (hashes are over dereferenced content, R2: "published formats keep their bytes"); `AnUnplacedSnippetThatWritesRun_DoesNotTurnOnRunSpots`; `APlacedOne_Does`.
- [x] **Step 2 to 3: Run, implement.** `Complete` and every hash other than `resolveHash` should hold; if a `formatHash` moves, stop and record why before regenerating.
- [x] **Step 4: Regenerate** (`GOLDEN_UPDATE_V31=1`), read every changed golden's diff (only `$ref`s inside `formats` and `requires.features` may move, and `resolveHash`/`lock.resolve.hash` with them), export `CompiledDocumentsExport` to the content repo, run its check, push, move the pin.
- [x] **Step 5: Run** the non-E2E suite (the 31 engine reads these); **commit** `PackageFormat (#1508): bundled formats reference each other by $id` and `…: requires lists run-spots only for placed units`; push.

### Task 8: The publish refusals the rulings add, and the limits' notes

**Files:** Modify `src/Consultologist.PackageFormat/V31/PublishNotes.cs`, `ResolveSentences.cs`, `FormatDialect.cs` or `SourceTypes.cs` (where a format file's `$defs` are read), `ResolveCompiler.cs`; Create cases in `tests/V31Source/RefusalPackages.Publish.cs`; Tests `tests/V31Resolve/PublishNotesTests.cs`.

**Interfaces:** Produces:
```csharp
public sealed record PublishLimits(int Ceiling, int ItemsPerLoop, int CallsPerJob, int? Instances = null, int? JobMinutes = null);
```
`PublishNotes.Refusals(SourcePackage, Resolution, PublishLimits)` → A11-001 (empty when `Instances` is null); `PublishNotes.Of` adds the minutes note (D16) when `JobMinutes` is given. `ResolveCompiler.Compile` returns A11-001 and A11-003 among its refusals.

- [x] **Step 1: Failing tests.** A11-001: three nested loops with `max` 200 each and no agent beneath (8,000,000 instances) is refused, naming the outermost loop's rooted key; a package at exactly `Instances` is not; an unbounded path is not refused. Count instances as the runner opens them (part 09 § 3.4: a node, check or deliverable instance, a loop item, a pass, a call); read `PlanRunner.cs:279` and count the same things, saturating. A11-002: `formats/a.json` with `"$ref": "b"` and `"$defs": { "b": … }` is refused; a `$defs` entry no reference names is today's behaviour. A11-003: a format whose expansion passes `PlanFormats.NodeBudget` is refused at publish with the format's id (use the budget's own counter so publish and run agree; a test holds the two to one constant). The minutes note: said when the estimate passes `JobMinutes`, with S09-016's words.
- [x] **Step 2 to 3: Run, implement.**
- [x] **Step 4: Refusal packages** for A11-001 to A11-003 (valid and invalid), exported (`RefusalPackagesExport`); README table rows; `V31Contract.PublishAreaSentences`; `refusal-sentences.md`. Part 07 § 10 and part 04 § 10.2 gain the three sentences ("ruled by the owner on #1508"); part 09 § 3.5 says the instances limit refuses.
- [x] **Step 5: Run V31 tests and the content check; commit; push** both.

### Task 9: A compiled case with a classifier, a macro folder, a part, a run spot and a built-in renderer; close `entry`

**Files:** Modify `tests/V31Resolve/ResolveFixture*.cs` (where `NestedNote()`, `Conditional()`, `Functions()` are), `tests/V31Resolve/CompiledDocumentsExport.cs`, `CompiledGoldenTests.cs`; Create goldens `tests/V31Resolve/Golden/macros.{resolve,lock}.json`; content repo `v31/schemas/resolve.schema.json`, `v31/conformance/compiled-documents/macros/`.

- [x] **Step 1: Write the case `Macros()`**, fictional (a letrozole-monitoring macro in a folder, offered by a `judgement` classifier; an agent with one part; a deliverable with a `run.date` spot; a `concept_list` read through `renderer.concept_bullets`). Reuse `tests/V31Run`'s macros fixture if it already has this shape (`macros.run.txt`).
- [x] **Step 2: Failing test** `ThePinnedFamily_IsWhatPublishGives` gains the case; `EveryGolden_ValidatesAgainstTheSchema` (if the suite has no in-repo schema validation of goldens, add one through the library's validator interface, reading the schema from `external/`).
- [x] **Step 3: Generate, read the golden:** it must have non-empty `placements`, a `runSpots` entry with `unit` and `neutral`, a `macros` folder entry, `parts`, and `renderers["renderer.concept_bullets"]`.
- [x] **Step 4: Close `entry`** in the schema to unknown members, per kind. Every golden (areas 05 to 10's and this one) and every conformance example must validate; add invalid examples (`v31/conformance/schemas/resolve/`: an entry with an unknown member).
- [x] **Step 5: Export, check, push** the content repo; move the pin; run V31 tests; commit `Tests (#1508): a compiled case with macros, a part, a run spot and a built-in renderer`; push.

## Part C: the pipeline, in the library

### Task 10: The test file, read strictly

**Files:** Create `src/Consultologist.PackageFormat/V31/Publish/PackageTestFile.cs`, `PackagePublishSentences.cs`; Test `tests/V31Publish/PackageTestFileTests.cs`; content repo `v31/schemas/test.schema.json` (R9, if it does not yet allow `{ "state": "skipped" }` alone).

**Interfaces:** Produces:
```csharp
public sealed record PackageTestFile(string Id, string Path, string FileHash, OfflineRequest Request, JsonObject Expect);
public static class PackageTestFile
{   // FileHash: sha256 of the file's bytes as stored (after normalisation)
    public static (PackageTestFile? Test, IReadOnlyList<SourceRefusal> Refusals) Read(SourceTest test, string text, RunPlan plan);
}
public static class PackagePublishSentences   // S07-007 … S07-014, S07-016, A11-004 … A11-007, S10-005 (StaysAt30)
```
(Name the static reader `PackageTestFiles` if a record and a static class cannot share the name.) `at` of every test refusal is the test's path.

- [x] **Step 1: Failing tests.** S07-010 for each declared input the file does not list (a listed `null` for an optional input is fine; `null` for a required one is the start's own refusal, surfaced by Task 11 as a `run` mismatch); S07-011 when `profile`'s keys are not exactly `needs.profile` (the sentence lists the fields the workflow reads, sorted; `none` when empty — use the filler earlier sentences use for an empty list); S07-012 the same for `run` against `resolve.json`'s `run` (`run.date` must be `{ "value", "timeZone" }` with both strings, else A11-005); A11-005 for an `expect` member that is not in § 8.3's table or has the wrong shape, and for an `outputs` key that is not `<rooted key>` optionally followed by `:<path>` and `#<n>` parts (reuse `RunKeys`/`RecordKeys` parsing if there is one); every refusal found is returned, in member order. Review Focus 3's hostile shapes, each asserting a sentence and no exception.
- [x] **Step 2 to 4: Run, implement** on `SnapshotTest.Request` for the request (pass `GenerateMissing: false` once Task 11 adds it), **run.**
- [x] **Step 5: Commit** `PackageFormat (#1508): a test file is read against the compiled package`; push.

### Task 11: Strict stubs in the harness

**Files:** Modify `src/Consultologist.PackageFormat/V31/Harness/OfflineRun.cs`, `OfflineHost.cs`, `OfflineStubs.cs`; Test `tests/V31Publish/StrictStubsTests.cs`.

**Interfaces:** Produces: `OfflineRequest(…, Func<string,string>? StubSeed = null, bool GenerateMissing = true)`; `OfflineResult` gains trailing `IReadOnlyList<string> MissingOutputs` (row keys asked for with no stub, ordinal order) and `IReadOnlyList<StubFault> InvalidOutputs` where `public sealed record StubFault(string Key, string Format, string Error)` (`Format` the type string as sentences print formats, `Error` the first validation error: path and keyword only, never the value).

- [x] **Step 1: Failing tests.** With `GenerateMissing: false`: a call with no stub is not generated, its key is in `MissingOutputs`, and the run ends without calling later dependants' stubs as missing too (only nodes that were actually asked are listed); a stub invalid against its node's format (validated as an agent reply is, envelope included) is in `InvalidOutputs`, and the reply is not repaired by regeneration; a valid stub for a classifier decides the condition. With the default, area 10's tests and goldens do not move (run `--filter "FullyQualifiedName~V31"` and the byte-equivalence tests).
- [x] **Step 2 to 4: Run, implement, run.**
- [x] **Step 5: Commit** `PackageFormat (#1508): the offline run can refuse to generate a missing stub`; push.

### Task 12: Expectations, the first mismatch and the outcome hash

**Files:** Create `src/Consultologist.PackageFormat/V31/Publish/TestExpectations.cs`, `TestOutcome.cs`; Test `tests/V31Publish/TestExpectationsTests.cs`.

**Interfaces:** Produces:
```csharp
public sealed record TestMismatch(string Expectation, string Key, string Expected, string Actual);
public static class TestExpectations { public static TestMismatch? First(RunPlan plan, JsonObject expect, OfflineResult result); }
public static class TestOutcome    { public static string Hash(OfflineResult result); }   // D8
```

- [x] **Step 1: Failing tests,** one passing and one failing per member of § 8.3's table, on a small compiled fixture (reuse `tests/V31Run` fixtures: a loop, a `while`, a `case`, a check loop, a gated deliverable): `ran`, `skipped` (by condition; not run by `until`; not run by `max`), `decisions` (boolean and branch id, by part 08 § 5's decision ids as `RunDecision` carries them), `loops` (`count`; `passes` and `stopped`), `values` (canonical JSON equality: `1.0` equals `1`, member order ignored), `prompts` (`text` and `hash`; a mismatch reports hashes both ways), `checks` (`pass`), `deliverables` (state with `text`, with `hash`, and `{ "state": "skipped" }` alone; `failed` expected and got passes a run that failed through that deliverable's `fail`). Ordering (D7): two failing expectations report the earlier member, then the lower key. `OutcomeHash_IsTheSame_ForTwoRunsOfOneTest` and `…_Differs_WhenAStubChangesAPrompt`.
- [x] **Step 2 to 4: Run, implement, run.**
- [x] **Step 5: Commit** `PackageFormat (#1508): a test's expectations, its first mismatch and its outcome hash`; push.

### Task 13: Running a package's tests

**Files:** Create `src/Consultologist.PackageFormat/V31/Publish/PackageTests.cs`; Test `tests/V31Publish/PackageTestsTests.cs`.

**Interfaces:** Consumes Tasks 10 to 12. Produces:
```csharp
public sealed record TestResult(string Id, string FileHash, bool Pass, string OutcomeHash, TestMismatch? FirstMismatch, long DurationMs)
{ public JsonObject ToJson(); }   // part 07 § 9's test result, every member present, result "pass" | "fail"
public sealed record TestsOutcome(IReadOnlyList<TestResult> Results, IReadOnlyList<SourceRefusal> Refusals, IReadOnlyList<SourceRefusal> Warnings);
public static class PackageTests
{
    public const int LimitSeconds = 60;
    public static Task<TestsOutcome> RunAsync(RunPlan plan, IReadOnlyList<SourceTest> tests, IReadOnlyDictionary<string,string> files, TimeProvider clock, RunLimits? limits = null);
}
```
A test that cannot be read (Task 10's refusals) has result `fail`, `outcomeHash` the hash of the empty outcome, `firstMismatch` null, and its refusals in `Refusals`.

- [x] **Step 1: Failing tests.** A passing test → `pass`, no refusal; a failing one → S07-007 with D7's words filled in, word for word against `sentences.json`; a missing stub → S07-008 (first missing key); an invalid stub → S07-009; an unused stub → S07-014 in `Warnings`, the test still passes; results in id order; every failing test's sentence is given (publish stops after all tests ran, § 5 step 3 "with the test's id and its first mismatch" per test); the 60 seconds with a fake `TimeProvider` advanced by the run → S07-013 alone in `Refusals`, later tests not run (D9); Review Focus 5 (a `prompts` mismatch and S07-009's error carry no stub text: assert the fictional stub string is in no sentence); area 10's `migration_snapshot` of a migration vector passes on its own package (read one vector from `external/…/v31/conformance/migration-vectors/`, fail loudly when missing).
- [x] **Step 2 to 4: Run, implement, run.**
- [x] **Step 5: Commit** `PackageFormat (#1508): in-package tests run offline against the compiled package`; push.

### Task 14: The publish report

**Files:** Create `src/Consultologist.PackageFormat/V31/Publish/PublishReport.cs`; Test `tests/V31Publish/PublishReportTests.cs`; content repo `v31/schemas/publish-report.schema.json` only if a test shows the builder's output cannot validate (record why).

**Interfaces:** Produces:
```csharp
public sealed record PublishReport(string Name, string Version, bool Published, DateTimeOffset? PublishedAtUtc, string PublishedBy,
    string SourceHash, string? ResolveHash, string? LockHash, IReadOnlyList<string> Normalised,
    IReadOnlyList<SourceRefusal> Refusals, IReadOnlyList<SourceRefusal> Warnings, IReadOnlyList<TestResult> Tests)
{
    public const string FileName = "publish-report.json";
    public const string MediaType = "application/vnd.consultologist.publish-report+json";
    public JsonObject ToJson();           // every member present; documentVersion 1; outcome "published" | "refused"
    public string ToCanonical();
}
```

- [x] **Step 1: Failing tests.** A published and a refused report each validate against `external/consultologist-package-format/v31/schemas/publish-report.schema.json` (through the library's validator interface); `publishedAtUtc` is RFC 3339 UTC with seconds (`2026-12-01T15:04:05Z`), null when refused; `warnings` and `refusals` are `{ sentence, at }` with `at` null for the package as a whole (as `resolve.json`'s warnings write it); canonical bytes are stable; an engine fault's exception type is not in the JSON.
- [x] **Step 2 to 4: Run, implement, run.**
- [x] **Step 5: Commit** `PackageFormat (#1508): the publish report`; push.

### Task 15: The pipeline

**Files:** Create `src/Consultologist.PackageFormat/V31/Publish/PackagePublish.cs`; Modify `CompiledPackage.cs` (carry `Normalised`, `Dropped` and the parsed tests out: add trailing members or an overload `Publish(…, out SourceCheckResult check)`; keep the existing signature working), `PublishedStamp.cs` (`MigratedFrom`); Test `tests/V31Publish/PackagePublishTests.cs`.

**Interfaces:** Produces:
```csharp
public sealed record PublishOptions(TypeRegistry Registry, Toolchain Toolchain, PublishLimits Limits, string PublishedBy,
    TimeProvider Clock, ITemplateReads? Templates = null, Func<string, string>? Diagram = null,
    string? MigrationReport = null, string? MigratedFrom = null, TimeSpan? MatchTimeLimit = null);
public sealed record PublishFile(string Path, byte[] Bytes);
public sealed record PublishOutcome(PublishReport Report, IReadOnlyList<PublishFile> Files, PublishedStamp? Stamp, CompiledPackage? Compiled, IReadOnlyList<string> Dropped)
{ public bool Published => Stamp != null; }
public static class PackagePublish
{
    public static Task<PublishOutcome> RunAsync(IReadOnlyDictionary<string, byte[]> upload, PublishOptions options);  // steps 0–4.4
    public static Task<PublishOutcome> CheckAsync(IReadOnlyDictionary<string, byte[]> upload, PublishOptions options); // steps 0–3: never Files or Stamp
}
// PublishedStamp(string ResolveHash, string LockHash, string? MigratedFrom = null); ToJson writes migratedFrom only when not null
```
`Files`, when published, is in write order: every source file as normalised (sorted by path, `manifest.json` among them), `dag.mmd` when `Diagram` is given, `lock.json`, `publish-report.json`, `migration-report.json` when given, then `resolve.json` **last**. The stamp is not in `Files`; the writer writes it after them. `Dropped` is the evidence files the upload held (for S07-015 and S07-016).

- [x] **Step 1: Failing tests.** The happy path on `ResolveFixture.NestedNote()` with one passing test: `Published`, the file order above, `resolve.json`'s bytes hash to the report's `resolveHash` and the stamp's, `lock.json`'s to `lockHash`, `CompiledPackage.Verify` accepts the pair against the stamp, and `RunPlan.Build` builds it (what the engine will load). A source refusal → refused report with every sentence and path, no files, hashes null. `Complete == false` → refused with an engine fault (A11-007), never stored. A failing test → refused, `tests` filled, no files. An uploaded `resolve.json`, `lock.json`, `publish-report.json`, `publish.json` and `dag.mmd` are in none of the files (and the first four in `Dropped`). `\r\n` files appear in `normalised` and are stored normalised. The same upload twice gives the same `resolveHash`, `lockHash` and `outcomeHash`es (only `publishedAtUtc` and `durationMs` differ). `CheckAsync` gives the same report with `outcome` as it would be, `publishedAtUtc` null and no files. An exception thrown inside (inject through `Diagram`) → refused with A11-007 and `EngineFault` set, never thrown. The warnings in the report are `resolve.json`'s plus the tests' (S07-014), and the ceiling note is among them when the fixture passes the ceiling.
- [x] **Step 2 to 4: Run, implement, run.** The tests run against the plan built from the in-memory `resolve.json` (`RunPlan.Build(compiled.Resolve, compiled.ResolveHash, compiled.LockHash, options.MatchTimeLimit)`), never the source.
- [x] **Step 5: Commit** `PackageFormat (#1508): publish, steps 0 to 4, as one call`; push.

## Part D: the registry and the endpoints

### Task 16: The engine's publish options, and the publisher's 31 branch

**Files:** Create `src/Consultologist.Api/V31/V31PackagePublisher.cs`; Modify `src/Consultologist.Api/Workflow/WorkflowPackagePublisher.cs` (branch before `ValidateFilePaths`), `WorkflowPackageModels.cs` (trailing `MigrationReport` on the request, `Report` on the response), `WorkflowPackages.cs` (`PublishAsync`: the 400 body), `ApiServices.cs`, `src/Consultologist.Api/V31/EngineLimitSettings.cs` (a `PublishLimits` with `Instances` and `JobMinutes` from the engine's numbers); Test `tests/V31Engine/V31PackagePublisherTests.cs`.

**Interfaces:** Consumes Task 15 and Task 17's writer (write this task against the interface; Task 17 implements it):
```csharp
public interface IV31RegistryWriter
{
    Task<bool> IsPublishedAsync(AccountKind kind, string name, string version, CancellationToken ct);
    Task<bool> WriteAsync(AccountKind kind, string name, string version, IReadOnlyList<PublishFile> files, PublishedStamp stamp, CancellationToken ct); // false: lost the race, nothing of ours is stamped
}
```
Produces: `V31PackagePublisher.PublishAsync(appUserId, accountKind, request, targetName, ct)` → `(WorkflowPackagePublishResponse? Published, PublishReport Report)`.

- [x] **Step 1: Failing tests** (NSubstitute; mind AGENTS.md's two NSubstitute traps). Not admitted → S09-001, nothing read or written (and with the setting unset no storage client is created). Admitted: target name and access exactly as the 5–30 path decides them (reuse `ResolveTargetNameAsync` and the ownership record); D2's manifest rewrite (the stored `manifest.json` has the assigned `name` and `version`, every other member and `null` as uploaded); A11-004; `publishedBy` per D4; a `MigrationReport` from a caller who is not the operator is refused as today's forbidden answer, and from the operator it is stored and the stamp has `migratedFrom`; a refusal returns the report and writes nothing, logs each `EngineFault` (type only), and sends the operator alert `OperatorAlertKinds.PublishFailed` (through the alert seam area 08 built; lengths and ids only); a publish sets `latest.json` and flips the account pin exactly as the 5–30 path does, **after** the stamp; `WriteAsync` false → retry on the next CalVer up to the 5–30 path's three times, then A11-006; the response's `Warnings` are the report's sentences and `Report` the report.
- [x] **Step 2 to 4: Run, implement, run** the non-E2E suite. `PublishOptions`: `PublishedTypeRegistry`, `EngineToolchain.Current()` (check its names are the release's: `package-format@<the pinned release>`, `formats@…`, `agents@…`, read from where the engine already reads them; record what they are), the engine's limits, `TimeProvider.System`, the engine's match time limit, `Diagram: null` (D1).
- [x] **Step 5: Commit** `Api (#1508): WorkflowPackages/Publish publishes a 31 package where the preview setting admits it`; push.

### Task 17: Writing the version's folder

**Files:** Create `src/Consultologist.Api/V31/V31RegistryWriter.cs`; Modify `WorkflowPackageRegistryWriter.cs` only to share the container lookup; Test `tests/V31Engine/V31RegistryWriterTests.cs` (an in-memory fake of the blob seam `BlobRegistryFiles`/`IRegistryFiles` uses; extend that seam with write, delete-by-prefix and create-if-absent rather than adding a second one).

- [x] **Step 1: Failing tests.** Files are written in the order given, then `publish.json` last with create-if-absent (If-None-Match `*`), media types set (`application/vnd.consultologist.resolve+json`, `…lock+json`, `…publish-report+json`, `application/json` otherwise, `text/markdown` for `.md`); `IsPublishedAsync` is true only with a stamp; a folder with files and no stamp is cleared before writing (**Review Focus 1:** after a first attempt that wrote `nodes/old.md` and died, a retry without that file leaves no `nodes/old.md`); a folder with a stamp is never touched and `WriteAsync` returns false; **Review Focus 2:** two writers interleaved file by file on one version: exactly one stamp is created, and the loser returns false (the loser's files may have overwritten the winner's before the stamp: so **the stamp's create is preceded by re-reading `resolve.json` and `lock.json` and checking their hashes against the stamp about to be written; on a mismatch return false without stamping**, and the engine's load-time `Verify` is the last line of defence; test both orders); after a write `CompiledPackageStore.TryLoadAsync` (the real one, on the same fake) loads the version.
- [x] **Step 2 to 4: Run, implement, run.**
- [x] **Step 5: Commit** `Api (#1508): a 31 version's folder is written source first, resolve.json last, then the stamp`; push.

### Task 18: `WorkflowPackages/Current` at 31

**Files:** Create `src/Consultologist.Api/V31/V31PackageDescription.cs`; Modify `WorkflowPackageModels.cs` (`WorkflowPackageInputResponse` gains trailing `JsonElement? Format = null, string? Channel = null`, exactly part 09 § 4.3's record), `WorkflowPackages.cs` (`GetCurrentAsync`), `WorkflowPackageStore.cs` only if the pin resolution must stop before reading the manifest; Test `tests/V31Engine/V31PackageDescriptionTests.cs`, and the existing `Current` tests.

**Interfaces:** Produces: `V31PackageDescription.Describe(RunPlan plan)` → `WorkflowPackageResponse` (or from the `resolve.json` `JsonObject`; take whichever `CompiledPackageStore.TryLoadAsync` already gives).

- [x] **Step 1: Failing tests.** The resolved pin is tried on `CompiledPackageStore.TryLoadAsync` first (it returns null without a 31 stamp, and does nothing unless admitted), then today's path. For the `Conditional` and `NestedNote` goldens: `SpecVersion` 31; `Blocks` per D12 (`Id` `node.plan`, `Name` its label; no nested node); `Inputs` in `inputOrder` with part 02 § 12's `Type` table row by row (one test per row, built from small inline `resolve.json` inputs), `Values` for `enum` and labelled `oneOf`, `Items` and `Fields` at depth (D13), `ExpectedContent` and `Channel` both the `channel`, `Types` null, `Format` self-contained (every `$ref`, `#/$defs/…` or URN, replaced by its definition with `$id` kept; no `$ref` left; titles and annotations kept); `Results` per D12; `Macros` `[]` (not null); `Title`. Below 31: the JSON of a 5–30 response is byte-identical to before but for `"Format":null,"Channel":null` on each input (the worker writes every member; AGENTS.md "JSON response casing") — check the satellites' reader tolerates unknown members by reading `EngineDtos` is not possible here, so pin today's serialised response in a test and show only those two members were added. **Review Focus 4:** a pin to a 31 version with the setting off → 400 with S09-001; a load refusal (S09-009, S09-014, a `requires` sentence) → 400 with that sentence.
- [x] **Step 2 to 4: Run, implement, run** the non-E2E suite.
- [x] **Step 5: Commit** `Api (#1508): WorkflowPackages/Current describes a 31 package from resolve.json`; push.

### Task 19: `Current/Content` and import at 31

**Files:** Modify `WorkflowPackageModels.cs` (`WorkflowPackageContentResponse` gains trailing `JsonElement? Resolve, Lock, PublishReport`, `string? MigratedFrom`, `IReadOnlyList<string>? Notices`, all null by default), `WorkflowPackages.cs` (`GetCurrentContentAsync`, `IngestAsync`), `WorkflowPackageIngestor.cs`, `WorkflowPackageArchive.cs` (bytes for a 31 archive); Create `src/Consultologist.Api/V31/V31PackageContent.cs`; Test `tests/V31Engine/V31PackageContentTests.cs`.

- [x] **Step 1: Failing tests.** Content (D14): `Files` every stored source file by path; `Manifest` is the stored `manifest.json` member for member (a `null` the author wrote is there; no member added: compare `JsonElement.GetRawText()` re-canonicalised with the stored text's); `Resolve`, `Lock`, `PublishReport` parsed from the stored bytes, and `CanonicalJson.HashText` of `Resolve`'s canonical text is the stamp's `resolveHash`; `MigratedFrom` from the stamp (D5); the caller's access is checked exactly as for a 5–30 version (an account that does not own a private package gets today's answer); only a stamped version is served; setting off → S09-001 400. Below 31 the response's JSON is as before but for the new null members. Import (D15): a 31 zip → the content response read-only, with `Notices` = [S07-016] when it held `resolve.json` or `lock.json`, and none otherwise; steps 0 to 2 are run in memory (`PackagePublish.CheckAsync` is not needed: `SourceCheck.Run` is enough) and a source refusal is returned as the 400's sentences; not admitted → S09-001; a 5–30 zip unchanged; a zip entry that is not UTF-8 → the intake's sentence, no exception.
- [x] **Step 2 to 4: Run, implement, run** the non-E2E suite and `dotnet build Consultologist.sln` (the Web project mirrors these records: if it has its own copy of the response records, add the members there too, unused).
- [x] **Step 5: Commit** `Api (#1508): the content endpoint and import at 31`; push.

### Task 20: The scripts: check, publish from CI, and the corpus re-render

**Files:** Create `scripts/check-workflow-package-v31.cs`, `scripts/rerender-workflow-packages-v31.cs` (file-based C# scripts, as `scripts/migrate-workflow-package.cs` is: copy how it references the library and the registries); Test `tests/V31Publish/ScriptsTests.cs` if the existing scripts have tests (follow `grep -rn "migrate-workflow-package" tests/`), else a section in the docs with the commands run and their output.

- [x] **Step 1: `check-workflow-package-v31.cs <folder> [--write <out folder> --version <tag> --published-by "ci <tag>"]`:** reads the folder as an upload, runs `PackagePublish.CheckAsync` (or `RunAsync` with `--write`, writing `Files` and `publish.json` into `<out folder>` for CI to upload and attach as Release assets), prints the report's canonical JSON, prints S07-015 for each evidence file found in the folder, and exits 1 on a refusal. Without `--version` the manifest's own `name` and `version` are used.
- [x] **Step 2: `rerender-workflow-packages-v31.cs <folder of name/version folders>`** per D11.
- [x] **Step 3: Run both** on a fixture exported from `tests/V31Resolve` (the `compiled-documents/macros/source` folder in `external/`): the check passes; a re-render of what `--write` wrote reports no difference; editing a stub in the written copy's test is reported.
- [x] **Step 4: Commit** `Scripts (#1508): check and publish a 31 package from a folder, and re-render the published corpus`; push.

### Task 21: The weekly summary's largest record

**Files:** Modify `src/Consultologist.Api/V31/OperatorWeeklySummary.cs` (lines 15–39), the sealer (`RecordSealer`, where the size is logged at sealing); Test the existing summary tests in `tests/V31Engine/`.

- [x] **Step 1: Failing tests.** `TheLargestRecordLine_IsTheWeeksLargestSealedRecord` (`Largest record: <n> bytes, <rows> rows (job <id>)`, numbers in the invariant culture with thousands separators as the other lines write them); `WithNoSealedRecord_TheLineSaysNone`; `WhenTheSizesCannotBeRead_TheLineSaysSo_NeverZero` (as R11's line does).
- [x] **Step 2 to 3: Implement.** At sealing, write the record's byte length and row count on the job header (or the job index row the summary already reads: take whichever the summary's job query returns without a blob read per job); the summary takes the maximum over the week. No new table.
- [x] **Step 4: Run** the non-E2E suite; update `engine-v31.md` "Alerts and the weekly summary" (line 555).
- [x] **Step 5: Commit** `Api (#1508): the weekly summary measures the largest record`; push.

## Part E: the contract's fixtures and the documents

### Task 22: Conformance: the publish report's fixtures and the test sentences' packages

**Files:** Modify `tests/V31Resolve/CompiledDocumentsExport.cs` (each case folder gains `publish-report.json` and `publish.json`; the `macros` and `nested-note` cases gain a `tests/` file each, one with an unused stub so S07-014 is in the report, and `nested-note`'s limits set so the ceiling note is), `tests/V31Source/RefusalPackages.Publish.cs` (S07-007 to S07-013 and A11-005, valid and invalid; the invalid side's `expect.json` carries the sentence; S07-013 is listed in `RefusalPackagesTests`' not-shipped list with its reason, since no package can state a clock), `RefusalPackagesTests.cs` (the runner calls `PackagePublish.CheckAsync` for these ids); content repo `scripts/check-v31.py` (the compiled-documents group validates `publish-report.json` and `publish.json` against their schemas and checks their hashes against the pair), `v31/conformance/README.md`, `v31/conformance/index.json`.

- [x] **Step 1: Failing tests** `ThePinnedFamily_IsWhatPublishGives` over the report (with a fixed clock and `durationMs` written as 0 in the fixture: say so in `about.txt` and the README) and the stamp; each new refusal package gives its sentence.
- [x] **Step 2 to 3: Implement, export** one filter at a time, read `git status` and the new files in the content repo.
- [x] **Step 4: Run** `python3 scripts/check-v31.py`; push the content repo; move the pin; run V31 tests here.
- [x] **Step 5: Commit** `Tests (#1508): the conformance suite gains the publish report, the stamp and the test sentences`; push.

### Task 23: The documents

**Files:** Modify `docs/customizable-workflow/package-format-library.md` (a section "specVersion 31: publish (preview)" after "migration": the pipeline, the test runner, the report, D1 to D16, what it leaves to areas 12 and 13; and fix every "area 11" forward reference found by `grep -n "area 11\|Area 11"`), `docs/customizable-workflow/engine-v31.md` (publish, `Current`, `Content`, import; the settings), `docs/CONFIGURATION.md` (a row for `WorkflowPackages__PreviewSpecVersions`; dev's registry: `WorkflowPackages__BlobServiceUri` and `__PublicBlobServiceUri` point at containers in `consultdevcaeast` on `canada-east-ai-function-dev`, part 09 § 2.2), `docs/customizable-workflow/registry-operations.md` (a section "specVersion 31": the folder's layout, the stamp, how dev's registry is filled — the catalogues copied from the release tags, example packages through `WorkflowPackages/Publish` from the migration branch — the two scripts, the corpus re-render before a library upgrade, and **"Before 31 leaves preview"** with R7's conditions word for word from #1508 comments 3 and 4), `docs/README.md` if it indexes new sections, `refusal-sentences.md` (A11 rows, if a task left any out), part 07 § 10 (the A11 sentences said at publish).

- [x] **Step 1: Write them.** Plain words; every claim checked against the code as built (open the file, do not write from this plan).
- [x] **Step 2: Run** the docs' own checks if any (`grep -rn "docs" scripts/*.sh | head`; the content repo's `check-v31.py citations` group if the edits cite it).
- [x] **Step 3: Self-check against the spec:** walk the coverage table below; for each row open the code or test named and confirm it exists. Fix or record what does not.
- [x] **Step 4: Commit** `Docs (#1508): publish at 31, dev's registry, and what must hold before 31 leaves preview`; push.

## Part F: the rulings of 2026-11-25 and 2026-11-26, a fix-first group before the PR

**Read first:** the four owner comments on #1508 dated 2026-11-25T18:47Z ("comment 5"), 2026-11-25T19:06Z ("comment 6"), 2026-11-26T01:24Z ("comment 7", which replaces comment 6's item 4) and 2026-11-26T01:30Z ("comment 8"); `gh issue view 1508 --comments`. Area 10's PR body ([#1532](https://github.com/Consultologist/Consultologist-Blazor/pull/1532), *For the owner to rule or see*) numbers the "unexpected differences" the comments cite. The principle the owner states: **31 stays strict; the migration writes the explicit thing that gives 30's output; new packages are told at publish.** The Global Constraints above hold for every task here. One addition: `tests/Fixtures/ByteEquivalence/` is area 01's frozen ground truth; a task here may **add** a fixture package, scenario or capture, and never changes an existing `vendored/`, `fixture-packages/`, `scenarios/` or `captures/` file.

**Code map** (paths under `src/Consultologist.PackageFormat/`, `PF` = `…/V31`): the migration is `WorkflowPackageMigration30To31.cs` and `PF/Migration/` (`Migrate.Nodes.cs`, `Migrate.Flows.cs`, `Migrate.Macros.cs`, `Migrate.Templates.cs`, `Migrate.Deliverables.cs`, `Migrate.Renames.cs`, `Migrated.cs` with `Notice`/`HandEdit`, `MigrationSentences.cs`, `ConditionTree.cs`, `ConditionTyping.cs`, `TemplateRewriter.cs`); the converter `WorkflowConditionConverter.cs`; the harness `PF/Harness/` (`ExpectedDifferences.cs`, `ByteEquivalence.cs`, `OfflineStubs.cs`, `OfflineHost.cs`, `MigrationReport.cs`, `SnapshotTest.cs`); the script's logic `Migration31Script.cs`. Tests `tests/V31Migration/`; fixtures `tests/Fixtures/ByteEquivalence/` (`migrated/<pkg>/`, `reports/<pkg>.json`, `hand-edits/<pkg>/`, `conditions/`); goldens regenerate with `GOLDEN_UPDATE_V31=1`, the conformance families with `CONFORMANCE_EXPORT_V31=<content repo root>` (`MigrationVectorsExport`, `RefusalPackagesExport`, `CompiledDocumentsExport`). The next free ids are **A11-009** and **A10-070**; a task that needs a sentence takes the next id and adds it where the *Area sentences* rule above says (A10 rows go in `V31Contract.MigrationAreaSentences`). Line numbers are as of commit `3bba0ef2`.

### Decisions this part takes

| # | Where | Choice |
|---|---|---|
| R10 | comment 7, B1 | `propertyOrder`: in `resolve.json`, every schema object that has `properties` (a bundled format at any depth, an input's inline format, a function parameter's) carries `"propertyOrder": [<names in written order>]` beside `properties`. It is written only into the compiled copy, after `formatHash` and `typeHash` are taken from the source, so neither moves; it is stripped from what is sent to a model (`SentFormat`), so `sentFormatHash` does not move. `Fields` follow it. Task 37, last, with the one regeneration. |
| R11 | comment 7, B2; comment 8 | A11-001 as built. The migration says an S10-002 hand edit when the migrated package's count passes `RunLimits.JobInstances`' default. The bulk publish records a package it cannot publish as `not migrated: <sentence>`, keeps its 30 version and pin, and names its owner in the notification file with the reason and "stops working when specVersion 30 is retired". |
| R12 | comment 5, 1 | A 30 classifier's format at 31 is a **package format** `<node id>_answer` (fitted to 64 characters as A6 fits ids): `classification`'s shape with `value` an `enum` of the `with` values in written order. Reads keep `.value.value`. A `case` on it is written with no hand edit (A10-032 retired); a `case` branch value outside the enum is the checker's own refusal. The values stay in the prompt wherever 30 sent them (byte-equivalence decides). |
| R13 | comment 6, 1 | **Every 30 macro that migrates to an offered macro with a non-empty instruction** is "a macro the model used to choose" (30's mechanics sentence begins "If so, write …", so every one was optional): each gets a `judgement` agent node `<macro id>_applies` (fitted, renamed on collision as § 1.9), a sibling placed immediately before the placing node, reading what the placing node's prompt reads, with the macro's own offer `if` as its `if`; the macro's `if` gains `and node.<…>_applies.value.answer === true`. Its prompt is the reads followed by an **empty question line**, and S10-002 is said. The "otherwise nothing" clause is not rewritten by code (it cannot be found reliably): the hand edit's reason says to remove it. A macro with an empty instruction, or `{{ "" }}`, is unchanged (area 10's hand edits stand). |
| R14 | comment 6, 2 | `OfflineStubs.Reply` appends offered markers only to a reply it generated. `ByteEquivalence.RequestFor` answers each `<…>_applies` classifier from the capture: `answer: true` exactly when 30's captured reply for the placing node held the macro's text. |
| R15 | comment 7, Q3 | A migrated deliverable all of whose sources may not run (`AlwaysValued` false for each) gets `if`: 30's condition `and` (`or` over the sources of "this source ran"), written with the read the deliverable's `if` scope has for it (the source's `.state === "completed"`, or `!== null` on its value where `state` is not readable there). Warning **A11-010** for a new package. |
| R16 | comment 7, Q4 | `TemplateRewriter`: a `for` over a loop node whose body reads only the item's text loops over `.value`; otherwise it keeps `.items` and its body is wrapped in `{{ if e.state == "completed" }}…{{ end }}` unless the body already tests `state`. Warning **A11-011** for a new package. |
| R17 | comment 7, Q5 | Refusal **A11-009**: a template read that steps through a nullable object before its last step, where the render would fail at run time, with no enclosing `{{ if <that object> }}` (or `!= null`) guard. JSON Logic reads keep their own rules (S06-035, `var` over null). The migration writes the guard around each such read; where the dialect accepts no guard at that site, S10-002. |
| R18 | comment 5, 6 | `options.json` beside `hand-edits/`: `{ "renames": {…}, "whenEmpty": {…}, "folderFormats": {…} }`, every member required, `{}` for none, unknown members refused. `Migration31Options.FromJson(JsonObject, registry, toolchain, formats30)` reads it; the script takes `--options <file>` (the three repeatable flags stay and are refused together with it); `migration-report.json` gains a required `options` member holding it (schema and examples in the content repo). |
| R19 | comment 5, 5 | `ExpectedDifferences.Explain` credits rule by rule: each bytes rule that changes the pair is credited; what is left after all rules is the unexplained remainder. `ComparedText` gains `Rules` (the ids credited, in order) and `Unexplained` (bool); `Expected` stays "no unexplained remainder". The report gains per-package counts `explained`, `partlyExplained`, `unexplained`. |
| R20 | comment 5, 11 | One loader: `PublishedTypeRegistry.Read(string packageFormatV31Folder, string agentsV31Folder)` (static, in the library beside `TypeRegistry`), used by the Api's singleton, the ingestor (which takes the singleton), the three scripts and `V31Contract.PublishedTypes()`. |
| R21 | comments 5 to 8 | The bulk publish is built as an operator route per package and a script that drives it (Task 36). Sending notices, running it and moving production pins stay the user's. |

### Task 24: The engine's small fixes

**Files:** Modify `PF/Run/PlanRunner.Loops.cs` (`Name`, l.314–336; the join at l.620), `PF/Run/PlanRunner.Agents.cs` (`RepliedAsync`, l.160–183), `PF/Harness/OfflineHost.cs` (`Fault`, l.106), `PF/Templates/EnvelopedFormat.cs` (`Unwrap`), `PF/Templates/AgentTurn.cs:106`; Tests `tests/V31Run/PlanRunnerLoopsTests.cs` (l.492), `tests/V31Run/AgentTurnTests.cs`, the loop-content tests.

- [x] **Step 1: Failing tests.** `AnItemNameCutAt120_IsTrimmedBeforeTheEllipsis` (a title whose 119th character is a space: the name is `title[..119].TrimEnd() + "…"`, exactly `WorkflowItemNames.FromField`'s result for the same text: assert equality with that 5–30 function over five titles, a surrogate pair at the cut among them). `AMarkdownInlineReply_LosesOneTrailingLineEnding_BeforeItIsValidated` (`"x\n"` and `"x\r\n"` are accepted as `x`; `"x\n\n"` still fails with A10-052; the offline host does the same: one shared helper). `ATextSentThroughAWrappedCustomBinding_IsUnwrappedToItsPlainValue` (a `via: custom` text format whose reply is `{"value":"She said \"yes\"."}`: the node's value and the composed document hold `She said "yes".`, no JSON quotes or escapes; a list or object value is unchanged). `PlaintextItems_JoinedIntoALoopsContent_AreEscaped` (a `for` over `plaintext` items read as `.content` in a `markdown_block` deliverable: `*` and `#` in an item's text and in the item's name are escaped as a direct read escapes them; `markdown_block` items are not).
- [x] **Step 2 to 4: Run, implement, run** the non-E2E suite. Regenerate the run and record goldens that move, read each diff, list the files.
- [x] **Step 5: Docs:** `engine-v31.md` (the drain's list: these and area 10's D16–D21 ship behind a drain), `package-format-library.md` where each is described. **Commit** one per fix (`PackageFormat (#1508): …`); tick; push.

### Task 25: Every member read is checked at publish

**Files:** Modify `PF/Templates/SiteReads.cs` (`PathTyping.Type`, l.137–182: `"part" or "macro" => (null, null)`), `PF/Templates/SourceTemplates.cs` (`Unit`, l.603–621), `SourceTemplates.Macros.cs`; Tests `tests/V31Source/` template tests; `tests/V31Source/RefusalPackages.Templates.cs`.

- [x] **Step 1: Failing tests.** `AReadPastAMacrosMembers_IsRefused` (`macro.<folder>.<id>.anything`, `macro.<id>.content.x`, `macro.<folder>.<id>.content.x`, `part.<id>.x`: each S05-011, naming the path and line). A table test `EveryHead_IsCheckedAtEveryStep`: for each head (`node`, `input`, `check`, `item`, `pass`, `param`, `run`, `profile`, `deliverable`, `snippet`, `macro`, `part`) a read whose **last** step does not exist and one whose **middle** step does not exist, in a prompt, a deliverable, a snippet, a macro body and an instruction where that head is legal there: each refused, none passing as untyped. The same table over JSON Logic `var` paths (S03-014).
- [x] **Step 2 to 4: Run, implement, run** V31 tests. A head that is still untyped after this task is a finding: fix it or record why it cannot be typed.
- [x] **Step 5:** an invalid refusal package for the folder-item case (S05-011), exported; content check; pin. **Commit** `PackageFormat (#1508): every member read is checked at every step`; tick; push.

### Task 26: A member read through a nullable object is refused (A11-009)

**Files:** Modify `PF/Templates/SiteReads.cs`, `PF/Templates/TemplateSyntax.cs` (`TemplateOutput.Enclosing`, `TemplateIf.Tests`), `PF/Templates/DialectSentences.cs`; `tests/V31Source/V31Contract.cs`; refusal packages; every in-repo 31 fixture the rule now refuses.

**Interfaces:** Produces A11-009 (author): *"\<Subject\> reads \<path\> at line \<n\> through \<object path\>, which can be null. Put the read inside {{ if \<object path\> }}."*

- [x] **Step 1: Establish what fails at run time.** Write a run test per kind of nullable object (an unset optional object input; a nullable property that is an object; a node that may be skipped, read as `.value.<member>`): render a member read through it when it is null, in a prompt and in a deliverable. The rule refuses exactly the reads whose render fails or faults; a read that renders empty by the dialect's own rule is not refused. Record what you found in *Recorded in the build*.
- [x] **Step 2: Failing tests.** `AMemberReadThroughANullableObject_IsRefused` (A11-009, word for word); `…InsideAnIfOnThatObject_IsAccepted` (`{{ if input.intake.value }}`, and the `!= null` form; an `else` branch is not guarded; a guard on a different path is no guard; a nested object needs its own guard or the conjunction); `ARequiredObject_NeedsNoGuard`; the last step being nullable is not this rule.
- [x] **Step 3: Implement** with `TypedRead.Nullable` per step and the enclosing `TemplateIf`s.
- [x] **Step 4: Run** the non-E2E suite. Every fixture, golden source and conformance package the rule now refuses gets its guard (never a weaker rule); list each file changed. Regenerate what moves.
- [x] **Step 5:** refusal packages (valid and invalid), README row, `refusal-sentences.md`, part 05 (the read rules) and part 07 § 10 with the content repo's twin; content check; pin. **Commit** `PackageFormat (#1508): a member read through a nullable object is refused at publish`; tick; push.

### Task 27: Tests and stubs as ruled; one registry loader

**Files:** Modify `PF/Publish/PackageTestFile.cs` (`OutputKey`, l.31; `Outputs`, l.147), content repo `v31/schemas/test.schema.json` (l.82) with examples, `PF/Harness/SnapshotTest.cs` (`Request`, l.171), `PF/Harness/OfflineStubs.cs` (`Reply`, l.26–44), `PF/Harness/ByteEquivalence.cs`; Create `PF/PublishedTypeRegistry.cs` (R20; the Api's class becomes a thin holder of it or is replaced), Modify `src/Consultologist.Api/ApiServices.cs:209`, `Workflow/WorkflowPackageIngestor.cs:50`, the three scripts, `tests/V31Source/V31Contract.cs:345`.

- [x] **Step 1: Failing tests.** `SnapshotOutputs_MayHoldCheckKeys` (`check.<id>` and its `#n`/`:path` forms are read and validate against the schema; a key that is neither `node.` nor `check.` is still A11-005). `Request_UsesTheTestsOwnDate` (`run.date` `{ "value": "2027-02-03", "timeZone": "America/Vancouver" }`: the run's date fact is that value in that zone and the clock is a time on that date in that zone, not 2026-11-16). `APinnedReply_IsUsedExactlyAsWritten` (a pinned reply that leaves an offered marker out: with `onInvalid: fail` the node fails with S05-053; with `retry` the second attempt is asked for and, having no stub, is S07-008; a generated stub still gets its markers). `TheRegistry_HasOneLoader` (the Api singleton, the ingestor and the contract fixture give registries whose canonical listings are equal; a grep-style test that `TypeRegistry.Read(` is called from one production file).
- [x] **Step 2 to 4: Run, implement, run** the non-E2E suite. Area 10's snapshots and byte-equivalence goldens that relied on appended markers now show the difference: where a capture's pinned reply lacks a marker, that is R13's case and Task 32 settles it; until then pin what the code gives and say so in the commit body.
- [x] **Step 5:** content repo schema, examples, check; pin. **Commit** per concern; tick; push.

### Task 28: Two publish warnings for new packages (A11-010, A11-011)

**Files:** Modify `PF/Templates/SourceTemplates.Deliverables.cs`, `PF/ResolveSentences.cs`, `PF/ScopeTree.Branches.cs` (read `AlwaysValued`); `tests/V31Source/V31Contract.cs`; `tests/V31Source/RefusalPackages.Publish.cs` (warning cases as the suite ships warnings: follow S07-014's).

**Interfaces:** Produces warnings in `Templates.Warnings` (so they reach `resolve.json`'s `warnings` and the report):
- A11-010 (author): *"Deliverable '\<id\>' could be released with nothing in it: everything it reads may not run. Give it an if, or whenEmpty: hold."* When the deliverable has no `if`, `whenEmpty` is `release`, it reads at least one node, and every node or check it reads may not run.
- A11-011 (author): *"Deliverable '\<id\>' writes fixed text for every item of \<loop key\> at line \<n\>, also for items that did not run. Loop over .value, or guard on state."* When a `for` over a loop's `.items` has literal text or a non-item read in its body outside any `if` that tests the loop variable's `state`.

- [x] **Step 1: Failing tests,** one saying and two near misses each (A11-010: an `if` present; `whenEmpty: hold`; one source that always runs. A11-011: a loop over `.value`; a body wholly inside `{{ if e.state == "completed" }}`; a body that is only the item's own reads, with no literal text beyond white space).
- [x] **Step 2 to 4: Run, implement, run** V31 tests; regenerate compiled goldens whose `warnings` move, read them, list them.
- [x] **Step 5:** README rows, `refusal-sentences.md`, part 06 § 6 and part 07 § 10 with the twins; content check; pin. **Commit** `PackageFormat (#1508): publish warns of a deliverable released empty and of loop text for items not run`; tick; push.

### Task 29: The migration's options live in a file

**Files:** Modify `WorkflowPackageMigration30To31.cs` (`Migration31Options`), `Migration31Script.cs` (`Parse`, l.101), `scripts/migrate-workflow-package.cs`, `PF/Harness/MigrationReport.cs`, `tests/V31Migration/ExampleChoices.cs`, `MigrationFixture.cs` (`Options`, l.121); Create `tests/Fixtures/ByteEquivalence/hand-edits/<pkg>/options.json` for every package (R18's shape; `test/general`: `whenEmpty` `{ "consult": "hold" }`, `folderFormats` `{ "standards": "standard_section" }`; `example-nested-note`: `renames` `{ "parts": "sections" }`, `whenEmpty` `{ "note": "fail" }`; `{}` members elsewhere); content repo `v31/schemas/migration-report.schema.json` and its examples, the migration vectors' `options.json` (D33) in the same shape.

**Interfaces:** Produces `Migration31Options.FromJson` (R18) → `(Migration31Options? Options, string? Refusal)`; `MigrationOptionsFile.FileName = "options.json"`; `MigrationReport` gains `Options` (`JsonObject`, required in `ToJson`).

- [x] **Step 1: Failing tests.** `ExampleChoices_ReadTheFiles` (no dictionary literal is left in `ExampleChoices`; a package with no `options.json` fails loudly); `FromJson` refuses an unknown member, a non-string value and a `whenEmpty` outside `release|hold|fail`, each with a sentence (next A10 ids) and never an exception; the script with `--options` gives the same package as the three flags did; `--options` with `--rename` is exit 2; the report holds the options and validates against the schema.
- [x] **Step 2 to 4: Run, implement, run** `--filter "FullyQualifiedName~V31"`. Regenerate `reports/` and the vectors; only the new member may move.
- [x] **Step 5:** part 10 §§ 1.4, 1.5, 1.9, 1.10 and `package-format-library.md` say where the choices live; content repo pushed; pin. **Commit** `PackageFormat (#1508): the migration's choices are read from options.json and recorded in the report`; tick; push.

### Task 30: Conversions that changed meaning; `flag` becomes `retry`

**Files:** Modify `PF/Migration/ConditionTree.cs` (`Builtin`, l.248–292; `Sized`, l.422), `ConditionTyping.cs` (`NullableSizes`, l.74; `TrimIsEmpty`, l.197), `Migrate.Nodes.cs` (`Condition`, l.204; `OnInvalid`, l.134), `MigrationSentences.cs`; Tests `ConditionConverterTests`, `ConditionEquivalenceTests`, `MigrateNodesTests`; goldens `conditions/converted.json`, `conditions/equivalence.json`, `migrated/`.

- [x] **Step 1: Failing tests.** `StringContainsOverAList_IsAHandEdit` (the operand types as a list: S10-002 with a reason naming the 30 meaning, a substring test on the list written as text; over text it is still `in`). `TheSizeOfANullableList_Keeps30sMeaning` (`size(x) == 0` → `x === null or size(x) === 0`; `size(x) > 0` and `!= 0` → `x !== null and size(x) > 0`; any other comparison is written so that a null list counts 0; A10-027 stays the notice; `row.size.member#0` in `equivalence.json` is now equal, no longer `unruled`). `TextComparedWithALiteral_BecomesIn` (`x == "lit"` → `{ "in": [x, ["lit"]] }`, `!=` → its negation, with a notice (next A10 id); the seven `row.*` fixtures and `composer-macros`' `section_close.if` that controller ruling 4 left refused now convert and evaluate as at 30). `StripEqualsEmpty_IsExpected` (the three `row.strip.*` cases are `expected` under `blank-conversion`, not `unruled`). `OnInvalidFlag_BecomesRetry` (A10-013's words say retry: "one more attempt with the validation error, then the node fails"; the notice is kept, since #1447's notification lists it).
- [x] **Step 2 to 4: Run, implement, run** V31 tests; regenerate, read every moved golden; no `unruled` case is left in `equivalence.json` (if one is, record it for the PR's *For the owner*).
- [x] **Step 5:** part 10 § 1.2 (the union row), §§ 2.2, 2.3 with the twin chapter; `sentences.json` only if an S row's words change. **Commit** `PackageFormat (#1508): …` per conversion; tick; push.

### Task 31: A classifier's format is the enum of its values

**Files:** Modify `PF/Migration/Migrate.Nodes.cs` (`WriteNode`, l.48–99; `AgentFormat`, l.187; `ClassifierTrailer`, l.112), `Migrate.Flows.cs` (`CaseBranches`, l.294–340), `Migrate.Layout.cs` (formats written), `MigrationSentences.cs` (A10-032 retired: keep the id unused, as A10-009 is); Tests `MigrateNodesTests`, `MigrateFlowsTests`; goldens `migrated/`, `reports/`, `hand-edits/*/README.md`.

- [x] **Step 1: Failing tests** (R12). `AClassifiersFormat_IsTheEnumOfItsValues` (`formats/<id>_answer.json` and its index row; `$schema` and `$id` as every migrated format has; every property required); `ACaseOnAClassifier_NeedsNoHandEdit` (three values, a `case` with two branches and no `else`: the missing value gets the empty branch § 1.2 writes, and no S10-002); `AnAnswerOutsideTheSet_IsInvalid` (offline run with a pinned reply `{"value":"other"}`: the node's `onInvalid` outcome, never a silent no-match); two classifiers with the same values get a format each; a `with` with no values keeps today's notice.
- [x] **Step 2 to 4: Run, implement, run.** Regenerate; in `reports/` no text may become unexpected through this (a prompt that gains or loses the values is a bug here: 30's bytes hold). Hand-edit folders whose only edit was a `case` lose it: delete the answered edit and its README line.
- [x] **Step 5:** part 10 § 1.2 gains the row, § 2.2 loses its "decided: no enum" text, with the twin; the vectors re-exported. **Commit** `PackageFormat (#1508): the migration writes a classifier's format as the enum of its values`; tick; push.

### Task 32: A macro the model used to choose is gated by a classifier

**Files:** Modify `PF/Migration/Migrate.Macros.cs` (`MacroHeader`, l.173; `MacroIf`, l.206; `Instruction`, l.351), `Migrate.Nodes.cs` or `Migrate.Layout.cs` (the new node's files), `Migrate.Renames.cs`, `PF/Harness/ByteEquivalence.cs` (`RequestFor`), `ExpectedDifferences.cs`; hand edits `tests/Fixtures/ByteEquivalence/hand-edits/{test/general,example-macros,example-macros-warnings,composer-macros}/`; Tests `MigrateMacrosTests`, `HandEditsTests`.

- [x] **Step 1: Failing tests** (R13, R14). `AModelChosenMacro_GetsAJudgementNode` (the node's header: `kind: agent`, `format: judgement`, the built-in binding; placed before the placing node among its siblings, inside the same loop when the placing node is in one; its `if` the macro's 30 offer condition; its prompt reads what the placing node's prompt reads and ends with an empty question); `TheMacrosIf_ReadsTheAnswer`; `TheEmptyQuestion_IsAHandEdit_AndPublishRefusesIt` (S10-002 names the node's prompt file and says to write the question and remove the instruction's "otherwise" clause; publishing the migration unedited is refused: by the existing sentence for an empty prompt if there is one, else a new A11 sentence *"Node '\<id\>' asks nothing: its prompt has no question."*); a macro placed by two nodes gets one classifier per placing node; `{{ "" }}` and empty instructions are untouched; an optional macro (§ 1.3) keeps its boolean input and gains the classifier too.
- [x] **Step 2 to 3: Implement.** In the comparison: the classifier's prompt exists only at 31, an expected difference under a new rule `macro-choice-classifier`; `RequestFor` answers it from the capture (R14).
- [x] **Step 4: Redo the hand edits.** `test/general`: the question *"Does the plan prescribe letrozole?"* gates `letrozole_prescription_text`, and the instruction loses "; otherwise nothing". `example-macros` and `example-macros-warnings`: questions for `red_flags`, `safety_net`, `house_style` and `counselling` (one fictional yes/no question each, drawn from the macro's own instruction). Any other package whose macros now get a classifier is edited the same way. A package that still cannot be edited for another reason stays in `HandEditsTests.NotEdited` with that reason, for the PR's *For the owner*. Unexpected difference 11 (`test/general`) must be gone from `reports/`.
- [x] **Step 5: Run** V31 tests; regenerate and read; part 10 §§ 1.2, 1.4 with the twin (and that a `condition` member is #1534, Milestone 40); vectors re-exported. **Commit** `PackageFormat (#1508): the migration gates a model-chosen macro with a judgement node`, `Tests (#1508): the hand edits write each question`; tick; push.

### Task 33: The migration keeps 30's output where 31 is strict (Q3, Q4, Q5, B2)

**Files:** Modify `PF/Migration/Migrate.Deliverables.cs` (`Deliverables`, l.27; `Composition`, l.70), `Migrate.Flows.cs` (`AlwaysValued`, l.219), `TemplateRewriter.cs` (`Run.For`, l.601; `Block`, l.232; `Read`, l.822–873), `WorkflowPackageMigration30To31.cs` (after `Derived`, l.200), `PF/PublishNotes.cs` (reuse `Refusals`), `MigrationSentences.cs`; Tests `MigrateDeliverablesTests`, `TemplateRewriterTests`, `MigratedPackagesTests`.

- [x] **Step 1: Failing tests.** R15: `ADeliverableWhoseSourcesMayNotRun_GetsAnIf` (an `if` flow with no `else` as the only source: the deliverable's `if` reads that the flow took a branch; with 30's own condition the two are joined by `and`; one source that always runs: no `if`; a notice says what was written); the `while` with no pass: covered by the same `if`, or pinned as it is with the reason. R16: `ALoopThatNeedsOnlyText_ReadsValue`; `ALoopThatNeedsTheItem_IsGuardedOnState` (a body that already tests `state` or `skipped` is not wrapped twice). R17: `AMemberReadThroughAnOptionalObject_IsGuarded` (the guard is exactly a form Task 26 accepts; the migrated package publishes; a site that takes no guard is S10-002). R11: `ANestedLoopPastTheInstanceLimit_IsAHandEdit` (S10-002 naming the outer loop's file and `max`, with A11-001's count and limit; no `max` is written).
- [x] **Step 2 to 4: Run, implement, run** V31 tests. Regenerate `migrated/`, `reports/`, the vectors; unexpected differences 8 and 12 and the `not_run` loop text must be gone from `reports/` or pinned with a recorded reason. `example-nested-note` now carries the R11 hand edit: its `hand-edits/README.md` records it as "Not made: the owner ruled that no max is guessed (#1508)".
- [x] **Step 5:** part 10 § 1.2 gains the three rows and § 1.4 the instance case, with the twin. **Commit** per ruling; tick; push.

### Task 34: The comparison credits differences one at a time

**Files:** Modify `PF/Harness/ExpectedDifferences.cs` (`Rules`, l.58; `Explain`, l.144–176), `ByteEquivalence.cs` (`ComparedText`, l.11; `Compare`, l.169), `MigrationReport.cs`; content repo `migration-report.schema.json`; Tests `ExpectedDifferencesTests`, `ByteEquivalenceTests`; goldens `reports/`.

- [x] **Step 1: Failing tests** (R19). `ARuledDifferenceBesideAnUnexplainedOne_IsStillCredited` (the text is `Unexplained`, and `Rules` names the rule); `TwoRuledDifferences_AreBothCredited`; the report's three counts. New or widened rules, one explained pair and one near pair each (comment 5, item 7): `sent-format` also explains a sent `description` (#3); `reply-never-a-template` (#5); `input-trailing-whitespace` (#6); `failed-job-dispatch-order` (#9); `before-after-macro-marker` (#10); `escaped-plain-text` covers a computed text value (#13). `refused-deliverable-withheld` is removed, and the test that every rule has a golden hit or a named reason holds (`macro-trailing-whitespace` and `sent-format` should now have hits; assert it).
- [x] **Step 2 to 4: Run, implement, run.** Regenerate `reports/`, read every one: write the counts per package and each remaining unexplained cause into *Recorded in the build* (differences 1, 2 and 4 of PR #1532 are not ruled and stay unexpected, for the PR's *For the owner*).
- [x] **Step 5:** part 10 § 3.2 with the twin; schema and examples; pin. **Commit** `PackageFormat (#1508): the byte-equivalence report credits each difference`; tick; push.

### Task 35: The edge-case packages

**Files:** Create six 30 packages under `tests/Fixtures/ByteEquivalence/fixture-packages/edge-<case>/` with a scenario and a capture each (generated by area 01's capture code, `tests/ByteEquivalence/PackageCapture.cs`, as the `composer-*` fixtures were), their `hand-edits/edge-<case>/` (`options.json`, edits), `migrated/edge-<case>/`, `reports/edge-<case>.json`; Create `tests/V31Migration/EdgeCasePackagesTests.cs`; refusal or warning packages for the "new package" side in `tests/V31Source/RefusalPackages.Publish.cs`.

The six (comment 6 item 3; fictional; small: two or three nodes each): `edge-missing-marker` (a reply that leaves the macro out), `edge-no-branch` (an `if` flow that takes no branch, feeding a deliverable), `edge-items-not-run` (a loop whose later items do not run, read by a deliverable with fixed text per item), `edge-unset-object` (an optional object input, unset, whose member a prompt and a deliverable read), `edge-answer-outside-set` (a classifier reply outside its `with` values), `edge-flag` (an `onInvalid: flag` node with an invalid reply).

- [x] **Step 1: Failing tests.** For each case: the migrated package, with its hand edits, publishes through `PackagePublish.CheckAsync` with a test `tests/edge.json` whose `outputs` are **pinned replies** (the capture's, under 31 keys; for `edge-missing-marker` the classifier answers false and the reply holds no marker) and whose `expect` states the outcome (deliverable text or state, the node's state); its byte-equivalence report has no unexplained text, or the one difference the owner accepted (`edge-answer-outside-set`: refused at both; `edge-flag`: the `retry` attempt, listed under a rule named for it). For each case the **new-package side:** the same 31 package written without the migration's explicit thing gives its warning or refusal (A11-010, A11-011, A11-009; a pinned reply without the marker fails the node with S05-053; an out-of-set answer is invalid; for `flag`, the existing refusal of an unknown `onInvalid` value).
- [x] **Step 2 to 4: Build the fixtures, run, read** every new golden and capture.
- [x] **Step 5:** `registry-operations.md` "Before 31 leaves preview" gains: these six packages run live on the dev deployment with fictional inputs (comment 6 item 3), where they are (`migrated/edge-*`), and how to publish them with `check-workflow-package-v31.cs`. **Commit** `Tests (#1508): six edge-case packages, migrated, pinned and run offline`; tick; push.

### Task 36: The operator's bulk publish

**Files:** Create `src/Consultologist.Api/V31/MigratePackage.cs` (the operation), `src/Consultologist.Api/Jobs/AdminMigratePackage.cs` (the route, beside `AdminDrain.cs`), `scripts/bulk-publish-v31.cs`; Modify `src/Consultologist.Api/V31/OutstandingHandEdits.cs` (share the enumeration of account packages below 31), `ApiServices.cs`; Tests `tests/V31Engine/MigratePackageTests.cs`, `tests/V31Publish/ScriptsTests.cs`.

**Interfaces:** Produces `POST Admin/MigratePackage` (operators only, as `Admin/Drain` authorises; not admitted → S09-001 as a 400), body `{ "owner": <app user id>, "name": <package>, "options": <options.json>, "handEdits": { "<path>": "<text>" | null }, "publish": bool }` (`null` deletes the file, as `.deleted` does), answering `{ "owner", "name", "oldVersion", "newVersion": string|null, "outcome": "published" | "would publish" | "not migrated: <sentence>", "handEditsLeft": [<S10-002 sentences>], "notices": [...], "report": <publish report> }`. `GET Admin/MigratePackage` lists `{ owner, name, version, specVersion }` for every active account package below 31. The script: `bulk-publish-v31.cs <operator folder> --engine <url> [--publish]`, where the folder holds `<owner>/<name>/options.json` and `hand-edits/`; without `--publish` it is a dry run. It writes `<operator folder>/bulk-publish-record.json` (one row per package, the answer above with the options used and the time) and `owners.json` (per owner: the packages published with each notice an owner must see, `flag` becoming `retry` among them; the packages not migrated, each with its reason and the sentence *"This package stops working when specVersion 30 is retired."*).

- [x] **Step 1: Failing tests** (NSubstitute; mind the two NSubstitute traps the repo's agent guide lists). The operation migrates the owner's **pinned** version, lays the hand edits over it, and publishes through `V31PackagePublisher` as that owner's package with the migration report (so the stamp has `migratedFrom`, the report's `publishedBy` is `operator-migration`, and the pin moves as any publish moves it); a hand edit left, a migration refusal or a publish refusal (A11-001 among them) answers `not migrated: <first sentence>` and **writes nothing: the 30 version and the pin are as they were** (assert no write on the registry seam and the pin unchanged); `publish: false` never writes; a package already at 31 is `not migrated` with a sentence saying so (idempotent rerun); a package with no `options.json` uses the account defaults (§§ 1.9, 1.10) and says so in the record; a non-operator caller gets today's forbidden answer; no sentence or record member carries input or profile text. The script against a fake engine (an in-process handler): the two files' shape, the dry run, exit 0 when every package published or would, 1 when any is not migrated, 2 on a fault.
- [x] **Step 2 to 4: Run, implement, run** the non-E2E suite and `dotnet build Consultologist.sln`.
- [x] **Step 5:** `registry-operations.md` (a section "The bulk publish": the folder, the dry run, the record, what it does with a package it cannot publish, word for word from comment 8), `engine-v31.md` (the route), part 10 § 1.5 with the twin (a package that cannot be published keeps its 30 version and pin; "pins move always" applies to packages that publish). **Commit** `Api (#1508): the operator migrates and publishes one account package`, `Scripts (#1508): the bulk publish, its record and the owners' list`; tick; push.

### Task 37: `propertyOrder`, and the one regeneration

**Files:** Modify `PF/CompiledFormats.cs` (`Bundled`, l.171, after the hashes at l.58–59), `PF/ResolveCompiler.cs` (inputs' inline formats, l.84–124) and `ResolveCompiler.Entries.cs` (function parameters), `PF/Templates/SentFormat.cs` (strip), `PF/Run/PlanFormats.cs` (`Expand` keeps it; validation ignores it), `src/Consultologist.Api/V31/V31PackageDescription.cs:97–111`; `docs/customizable-workflow/package-format-v31/07-compiled-documents.md` §§ 2.2, 2.3, `09-engine-and-pages.md` § 4.3; content repo `package-format-v31.md` (§§ 7.2.2, 7.2.3), `v31/schemas/resolve.schema.json`, `scripts/check-v31.py`; every compiled golden.

- [x] **Step 1: Failing tests** (R10). `AnObjectFormat_KeepsItsPropertyOrder` (properties written `zeta, alpha, mid`: `propertyOrder` is exactly that, at the root, in a nested object, in an array's `items`, in an inline input format and in a catalogue format read from the registry); `FormatHashTypeHashAndSentFormatHash_DoNotMove` (against `tests/V31Types/Golden/format-hashes.json`, untouched, and a sent-format test); `TheSentFormat_HasNoPropertyOrder`; `ASchemaWithPropertyOrder_StillValidatesValues`; `Fields_ComeInWrittenOrder` at every depth (`V31PackageDescriptionTests`); the schema requires `propertyOrder` beside `properties` (an invalid conformance example without it; an older preview pair then fails the schema, which is right while 31 is in preview: say so in the docs).
- [x] **Step 2 to 3: Run, implement.**
- [x] **Step 4: Regenerate once** (`GOLDEN_UPDATE_V31=1`, the whole V31 suite), then each conformance export, one filter at a time. Read every changed golden: only `propertyOrder` members and `resolveHash`/`lock.resolve.hash` (and what earlier tasks of this part already moved) may differ. Run the content repo's check, push, pin; run the non-E2E suite.
- [x] **Step 5: Commit** `PackageFormat (#1508): resolve.json keeps a format's property order`, `Api (#1508): Fields come in written order`, `Docs (#1508): …`; list every golden in the commit body; tick; push.

### Task 38: The documents, and the self-check

- [x] **Step 1:** `package-format-library.md` (the migration and publish sections say what Tasks 24 to 37 built; fix every statement they made stale: `grep -n "A10-032\|flag.*fail\|refused-deliverable-withheld\|ordinal order\|WithMarkers"`), `engine-v31.md`, `registry-operations.md` ("Before 31 leaves preview" holds every condition of comments 3, 4 and 6), part 10 § 1.4 (D30: exit 1 is "written; hand edits remain", also for a dry run), `refusal-sentences.md`, this plan's *Spec coverage* and *Recorded in the build*.
- [x] **Step 2:** the content repo's `python3 scripts/check-v31.py` in full; `git status` clean in all three content repos; the pins are pushed commits.
- [x] **Step 3: Walk comments 5 to 8 item by item** against the code and tests; write, for the PR, the list *For the owner*: unexpected differences 1, 2 and 4; any package still in `NotEdited`; anything a task recorded as not done.
- [x] **Step 4: Commit** `Docs (#1508): the rulings of 2026-11-25 as built`; tick; push.

## Spec coverage

| Spec | Task |
|---|---|
| part 07 § 5 step 0 receive | exists (`SourceIntake`, area 02); version exists, D2: 16, 17 |
| § 5 steps 1, 2 | exist (areas 02 to 08); the rulings' additions 7, 8 |
| § 5 step 3 tests | 10 to 13 |
| § 5 step 4 compute, write, stamp | 15 (compute, order), 17 (write, stamp), 16 (caller) |
| § 5 "run steps 0–3 without writing" | 15 (`CheckAsync`), 20 |
| § 6 where the files live | 17 (registry), 19 (content: the compiled document and toolchain for owned versions), 20 (CI's assets); Release assets, the Editor's panes: not this area's |
| § 7 evidence and views | exists (dropped unread); S07-015: 20; S07-016: 19; `dag.mmd`: D1, area 13 |
| § 8 in-package tests | 10 to 13; § 8.4 snapshot: 13; R9; `check.*` outputs, the test's own date, replies as written: 27 |
| § 9 the publish report | 14, 15, 22 |
| § 10 refusals | start and load: exist (area 06); tests: 10 to 13; warnings: 13, 19, 20; the rulings' refusals and warnings: 25 (A11-012), 26 (A11-009), 28 (A11-010, A11-011), 32 (A11-013), 35 (A11-014) |
| §§ 2, 3, 4 as ruled | 6, 7, 9; § 2.2 `propertyOrder`: 37 |
| part 09 § 1.3, § 2.1 admission | 16, 18, 19 (every 31 branch asks `SpecVersionAdmission`) |
| part 09 § 2.2 dev's registry | 23 (settings, how it is filled); the Azure settings are the user's |
| part 09 § 3.5 the publish note | exists (S04-072, S04-073, S04-079, S04-080, area 05); minutes, instances: 8 |
| part 09 § 4, part 02 § 12 `Current` | 18; § 4.3 `Fields` in written order, `Format` without `propertyOrder`: 37 |
| part 09 § 8.2 content | 19 |
| part 07 § 4.4 corpus re-render | 20 (D11), 23 |
| part 10 § 1.5 migrated versions | 16 (D5), 19 (`MigratedFrom`); the bulk publish: 36 |
| part 10 § 5 fixtures | 22; the migration vectors re-exported: 29 to 33 |
| part 10 §§ 1.2, 1.4, 2, 3.2 as ruled | 29 to 34; the edge-case packages (§ 3.3): 35 |
| #1508 comment 1 | 10, 12 (R9) |
| #1508 comment 2 | 8 (items 1, 4, 5), 7 (item 2), 6 and 9 (item 3) |
| #1508 comment 3 | 6 and 9 (item 1), 7 (item 2), 23 (item 3) |
| #1508 comment 4 | 1 (item 1), 2 (items 2, 3), 3 (item 4), 4 (item 5), 5 (items 6, 7), 6 (item 7's guard), 21 (item 8), 23 (the added conditions) |
| #1508 comment 5 (2026-11-25T18:47Z) | 31 (item 1), 30 (2, 3), 24 (4, 8's D23 and line ending, 13), 34 (5, 7, 8's rule), 29 (6), 38 (8's D30), 27 (9, 10, 11), 25 (12) |
| #1508 comment 6 (2026-11-25T19:06Z) | 32 (item 1), 27 (2), 35 (3); item 4 replaced by comment 7 |
| #1508 comment 7 (2026-11-26T01:24Z) | 37 (B1), 33 and 36 (B2), 33 and 28 (Q3, Q4), 33 and 26 (Q5), 35 (the edge cases) |
| #1508 comment 8 (2026-11-26T01:30Z) | 36 |

## Recorded in the build

Each line is a choice this plan does not state, taken while building. All of them go in the PR body under *Decisions made during build*.

- **Order.** Task 11 ran before Task 10 (10 consumes `GenerateMissing`, which 11 adds).
- **Task 2.** `failWith` is read from the root operation's literal (`all_pass`, `count_pass`, and `terms_subset` too); an aggregate wrapped in an `if` gives null.
- **Task 4.** The alert activity keeps its deployed name `alert-operator-v31` (the plan's `send-operator-alert-v31` was wrong). The OCR model's dated version is `AnalyzeResult.ApiVersion`: the SDK in use (1.0.0) has no `ModelVersion`.
- **Task 5.** The sentence rows S03-026, S03-027, S03-028, S03-038, A04-009 and A06-003 now start with the site (`Check 'x'` or `Computed node 'x'`).
- **Task 7.** A placed macro's `run.*` is no run spot (07-D5), so it never lists `run-spots`.
- **Task 8 (R1 as built).** Every `for` is bounded by the items limit (`loopItems`, 200) where its length is not known, a `while` by its `max`, and a ring of calls is refused earlier, so for A11-001 no path is unbounded and R1's clause "an unbounded path is not refused" has no case. At the default `jobInstances` (10,000) the `nested-note` example counts 80,614 instances and is refused. Put to the owner on #1508.
- **Task 9.** `entry` is closed over the union of the kinds' members, not per kind; a snippet folder's item is the one open entry.
- **Task 15.** Intake drops an uploaded `publish.json` unread (it was a stray-file refusal). A11-004 is said first, and only when `manifest.json` is an object without a text `name` or `version`. An incomplete pair is A11-007 with no fault type; a fresh `resolve.json` that `RunPlan.Build` refuses is A11-007 with fault `S07-006`. In-package tests run under the default `RunLimits`.
- **Task 16.** The writer's interface takes the account kind as `string?`. The 31 branch is at the top of `PublishAsync`. Every refused 31 publish raises the `publish-failed` alert. A manifest without a text `name` or `version` is stored as uploaded, so A11-004 is the pipeline's.
- **Task 17.** One writer at a time holds a version's folder, through a lease on a lock blob beside it (`{name}/{version}.lease`); a clear never deletes a stamp; any `publish.json` counts as a stamp; `dag.mmd` is stored as `text/plain`.
- **Task 18.** An input's `Format` is one compound document: the input's schema at the root, each format it reaches once under `$defs` with its `$id`, references by `$id` (the plan's "no `$ref` left" gives duplicate `$id`s). An unreadable registry answers 503. `Fields` come in ordinal order, because `resolve.json` is canonical JSON and keeps no property order; part 09 § 4.3 says "written order". Put to the owner on #1508.
- **Task 19.** `Content` serves any stamped version whose pair verifies against the stamp, whether or not this engine can run it; an uploaded `dag.mmd` and the stored `migration-report.json` are not in `Files`; a job start for a 31 pin with 31 not admitted answers S09-001 as a 400.
- **Task 20.** The scripts reference the Api project and use the spec's default limits. `--write` refuses an out folder that is not empty. `--version` replaces only the manifest's `version`. `publishedBy` defaults to `ci <tag>`. Both scripts skip links.
- **Task 21.** The record's size and row count are written on the job's index row at sealing; a record sealed before this build is "not measured".
- **Task 22.** Every compiled case is published against stated limits (the engine's defaults; `nested-note` at 100,000 instances), so four golden pairs moved. `durationMs` is 0 on a stopped clock.
- **Task 23.** `publish-report.schema.json`'s `publishedBy` accepts `ci <tag>`, as part 07 § 9 says and the script writes; the schema listed bare `ci` only.
- **After the whole-branch review.** A version whose manifest declares specVersion 31 is listed (the account's listing, the public chain view, the catalogue sweep's count) only once its stamp exists. The corpus re-render walks nested names and exits 2 when it checked nothing. `PendingAlerts` never faults, a cancelled alert included.
- **Task 24.** `PlanRunner.Name` reuses 5–30's `WorkflowItemNames.FromField`. The one trailing line ending of a `markdown_inline` reply is stripped in `PlanFormats.Read`, which the offline host shares. D23's escape at the join applies to every item whose `ret` did not write Markdown (an object, a list, a nullable text), and to its name too. A wrapped text reply that is not the envelope is now `AgentOutcome.NotOfFormat` (it was accepted as the text). Comment 5's item 13 cannot be reached through publish today; the fix is defensive. Goldens moved: `checked-note`, `checked-note-outcomes`, `functions` (run and record).
- **Task 25.** A read past `part.<id>`, `macro.<id>` or `macro.<folder>.<id>` is S05-011 (in a snippet, said of the snippet); a `for` over a unit is S05-006. A part or a macro used in an expression or piped (it read as null at run time) is the new A11-012. S05-011's conformance case is now the folder-item read. JSON Logic `var` paths were already checked at every step for every head.
- **Task 26 (what fails at run time).** With the object null, an unguarded member read through an unset optional object input, a nullable object field or a skipped node's `.value` fails the render, in a prompt and in a deliverable; inside `{{ if <object> }}` it renders nothing and the job completes; a nullable last step renders empty and is not refused. A11-009 accepts as guards a body `{{ if <object> }}` or `!= null` on the `&&` spine (never an `else`), the holding unit's header `if`, and `pass.number > 1` for `pass.previous` of a node that always runs; a guard holds only the binding it tests (a shadowing `for` variable is another binding). A presence test on a nullable object is a fourth body-`if` fact (part 05 § 2.5). `example-structured-intake` was refused until Task 33 guarded it (`HandEditsTests.ForTask33`).
- **Task 27.** A pinned reply in a package test, a snapshot test or the comparison is served exactly as written, and a generated stub alone gets the offered markers (the comparison's one exception, a macro 30's engine placed, is Task 34's). Two goldens moved in between (`reports/composer-macros.json`, `reports/test/general.json`), settled by Tasks 32 and 34.
- **Task 28.** A11-010 does not judge an object deliverable. A11-011's line is the `for` tag's, and an `if` chain that tests the item's state guards every branch, its `else` included. Neither warning is shipped as a conformance package (as S07-014 is not).
- **Task 29.** The vectors' `options.json` keeps `toolchain` beside R18's three members. `--options` with `--snapshot` is a usage error. The operator's run reads `_hand-edits/<package>/options.json` and uses the defaults when there is none. A10-070 to A10-072 are in `V31Contract` and the conformance README only, as earlier A10 ids are. Goldens moved: the 13 `reports/` (the `options` member) and 8 vector `options.json` (`null` became `{}`).
- **Task 30.** `ExpressionChecker.In` accepts a text needle in a written list of string literals, so the ruled `x in ["lit"]` publishes (part 03; it reaches new packages too). `string.contains` on a list is a hand edit (A10-073), text compared with a literal a notice (A10-074), a text input compared with `""` is written with its null test (A10-075), and any other comparison of an input's text with blank text is a hand edit (A10-076), since 31 reads a blank input as null. The size of a nullable list is written in one of three forms (`or`, `and`, `if`). No `unruled` case is left in `equivalence.json`.
- **Task 31.** A classifier's answer format `<id>_answer` is a package format bound through `custom`, written with `$schema` only (no migrated format has `$id`), and listed after the folders' formats. So the engine appends "Answer with one JSON object…" and the schema after 30's own line listing the values: the values are sent twice, an expected difference under `sent-format` (the controller's ruling; for the owner). A10-032 is retired. No hand-edit folder had a `case` edit.
- **Task 32.** Every offered 30 macro with a non-empty instruction that is no template, and that the engine did not place, gets `<macro>_applies` (`judgement`, `onInvalid: fail`, label "Whether <label> applies"), whose prompt is the placing prompt's top-level reads that are not snippets, then an empty line (A10-077). A macro placed by two agents gets a classifier each, and its `if` requires both answers: a hand edit (A10-078, the controller's ruling). A11-013 refuses a judgement node that asks nothing, counting a placed snippet's or part's text. `counselling` (placed by the engine) and `composer-macros`' `section_intro` (an empty instruction) get no classifier. `test/general`'s classifier is written by its hand edit (its macro is placed in a snippet folder's item, A10-051); `example-macros`' and `example-macros-warnings`' questions are written in their `README.md`s, not made, since those packages stay in `NotEdited`. `Migration31Result.MacroIds30` lets the comparison find 30's marker under a renamed macro.
- **Task 33.** A deliverable all of whose sources may not run gets the `if` "a source's `.state === "completed"`" (`or` over them), joined to 30's own condition by `and` (A10-079); a `while` source gets none (30 released it empty too; its separator is the rule `while-no-pass-separator`, Task 34). A loop that needs only its items' text reads `.value`; otherwise its body is guarded on `state` inside the loop's marks. A read through an object that can be null is guarded, or a hand edit where no guard can stand (A10-080). A package past the instances limit is a hand edit naming the heaviest path's outer loop, its file and `max`, with A11-001's count and limit (A10-081); no `max` is written. `example-nested-note` carries that hand edit ("Not made"), loses its snapshot test (fixture and vector), and is in `NotEdited` but compared (`ComparedAsMigrated`); its clean-package tests moved to `example-prior-letters`. Unexpected differences 8 and 12 are gone from `reports/`.
- **Task 34.** A rule is credited only where it lowers the token-level distance between the texts (Myers, bounded). Rules added: `reply-never-a-template`, `input-trailing-whitespace`, `failed-job-dispatch-order`, `before-after-macro-marker`, `while-no-pass-separator`; widened: `sent-format` (descriptions), `escaped-plain-text` (a computed text; a prompt that reads a Markdown loop's `.content`, D23), `position-names-from-the-loop` (an empty `itemLabel`); `refused-deliverable-withheld` removed. The comparison writes the marker of a macro 30's engine placed before or after the reply into the captured reply, where its instruction asks (`OfflineRequest.EnginePlaced`; the controller's ruling: without it `composer-macros` fails at `section_draft` and difference #10 cannot be compared; for the owner). The counts and the causes left unexplained are in *For the owner* (differences 1, 2 and 4). All the `reports/` goldens moved.
- **Task 35.** A seventh package, `edge-unruled-whitespace`, shows the two white-space differences no ruling covers (a macro the model chose, whose body ends with a line ending, loses it at 31; an empty read at a line's end leaves a trailing space 31's snippet keeps and 30 trimmed), and its `problem_close`, placed `after`, is `macro-trailing-whitespace`'s golden hit. `edge-items-not-run` gains a template loop (`problem_lines`, read by `problem_list`): 30 wrote its fixed text for the item beyond `max`, R16's guard does not. These four texts stay unexplained, listed by site with the reason in `EdgeCasePackagesTests.Unruled`, for the owner; no rule was added for them. The pinned tests of `edge-answer-outside-set` and `edge-flag` keep D6: the stub is refused (S07-009) before `onInvalid`, their `expect` is `{}`, and the node's state is shown on the comparison run. A11-014 is new: no refusal of an unknown `onInvalid` existed. `flag-becomes-retry` credits only the retry's text in the retried prompt, so `sent-format` credits the rest.
- **Task 36.** A hand edit is "left" when the migration says one and the request gives no edits; once edits are given the checker decides (each unmade edit is written in a form it refuses), and the edited package's snapshot test is written anew (`Snapshot`, at the engine's default limits, so A11-001 can be said there before the publish), A10-011 then not said. The migration report recorded with the publish has no compared text (the engine cannot run 30 offline), and the publisher writes the version it assigns into its `newVersion` when the report names another. The answer gains trailing `options` (the choices used) and `defaults` (A10-083 when no `options.json` was given); `GET` answers `{ packages, notRead }` so an unread package is counted, never dropped, and the script exits 1 for it. Four sentences: A10-082 (an empty `options.json` id or choice, carried from Task 29's review, with the byte-order mark dropped and A10-019 for text that is not strict JSON), A10-083, A11-015 (the deadline) and A11-016 (a package no pin names, or no account package). A null hand edit for a path the migration did not write is no edit. The script reads `ACCESS_TOKEN`, checks every folder before it sends anything, stops at the first engine fault (the files then hold what was done before it) and writes `owners.json` in a dry run too, as a preview, each published entry with its `outcome`; `owners.json`'s not-migrated entry carries S10-005 as the owner's notice beside A11-015. **Fix round 1 (the controller's rulings):** a hand edit is left unless the file its notice names is among `handEdits`' paths (edited or null), from `Migration31Result.HandEditFiles` (one file per notice; every S10-002 names a file, so none is left for lack of one), and an operator who answers a notice in another file sends the named file as the acknowledgement; this replaces "left only when no edits are given". The migration (A10-081) and the snapshot count against `Migration31Options.Limits`, the engine's configured `PublishLimits` in the bulk publish (default the starting values), so a package within a raised `JobInstances` is neither a hand edit nor refused. A run carries forward from an existing record every `published` row whose package it did not answer, and builds both files from the merged rows (earlier first); a record it cannot read is exit 2 before anything is sent, neither file written. A package at 31 asked for anyway replaces its earlier published row with its `not migrated` answer.
- **Task 37.** `propertyOrder` is written by `CompiledFormats.Rewrite`, so it is beside every `properties` of every format `resolve.json` writes (an input's, a function parameter's, a node's, a renderer's, a snippet's, a deliverable's, and the bundle's entries), the bundle's built-ins and catalogue formats in the order their published files write. It stays compiled-only: the source dialect does not admit it and is unchanged. A compiled format is its source format in the dialect plus `propertyOrder`; its `$schema` names its source's dialect, and a reader checking it against the dialect removes `propertyOrder` first (parts 07 § 2.2 and the twin's § 7.2.2). `resolve.schema.json` (after the task review) judges an inline format at a site by `compiledInline`: the dialect's schema keywords and rules plus `propertyOrder`, closed (`unevaluatedProperties: false`), recursing through itself; the bundle's entries are judged by the open `bundledFormat` (the `propertyOrder` rules only), as they were never judged by the dialect in this schema (an entry was `"type": "object"`; a reference in the bundle is an `$id`, which the dialect's `$ref` pattern refuses). The content repo's check holds each `propertyOrder` of a compiled document to the names of the properties beside it, once each. `Fields` put a property `propertyOrder` does not list (publish never writes one) after the listed ones, in the order held. `Format` in `WorkflowPackages/Current` leaves `propertyOrder` out at every depth (the controller's ruling in the task review; `PlanFormats.SelfContained`), so it is a schema in the dialect, and `Fields` carry the order. `CompiledFormats.Walk` stops at 64 schema levels; publish never reaches it, since the reader refuses source JSON nested past 32 levels (S01-041). The sent copy keeps no order of its own (it is written as canonical JSON).
