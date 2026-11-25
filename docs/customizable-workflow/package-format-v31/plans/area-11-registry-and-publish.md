# Area 11: Registry and Publish (specVersion 31) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publish a specVersion 31 package end to end: receive, resolve, validate, run its in-package tests offline, build the publish report, and write the version's folder with the stamp last; serve what was published through `WorkflowPackages/Current` and `Current/Content`; and apply the owner's rulings on #1508 to the code areas 05 to 09 built.

**Architecture:** The pipeline is a pure library call, `PackagePublish.RunAsync` in `src/Consultologist.PackageFormat/V31/Publish/`, over what exists: `CompiledPackage.Publish` (steps 0 to 2 and the pair), `RunPlan.Build` and `OfflineRun.RunAsync` (the harness the tests run on). It gives a `PublishOutcome`: the report, and, when published, the files to write in part 07 § 5's order. The Api's `V31PackagePublisher` calls it with the engine's registry, toolchain and limits, and `V31RegistryWriter` writes the folder (source, `lock.json`, `publish-report.json`, `resolve.json`, then `publish.json`), which `CompiledPackageStore` already loads. Every 31 branch in the Api is entered only through `SpecVersionAdmission`, so with the preview setting unset nothing answers differently.

**Tech Stack:** C# / .NET 10, `System.Text.Json` (`JsonNode`), Azure Functions (isolated), `Azure.Storage.Blobs` through the existing container factory, xUnit and NSubstitute in `tests/Consultologist.Api.Tests.csproj`; Python 3 for `scripts/check-v31.py` in the content repo.

**Spec:** `docs/customizable-workflow/package-format-v31/` — [`07-compiled-documents.md`](../07-compiled-documents.md) in full; [`09-engine-and-pages.md`](../09-engine-and-pages.md) §§ 1, 2, 3.5, 4, 8.2, 9; [`02-types-and-inputs.md`](../02-types-and-inputs.md) § 12; [`10-migration-and-tools.md`](../10-migration-and-tools.md) §§ 1.5, 4, 5. **The owner's rulings are the four comments on #1508** (`gh issue view 1508 --comments`): read them before your task; where a ruling and the spec differ, the ruling is the decision and the spec text is corrected in the task that applies it. How the library and the engine work today is in `docs/customizable-workflow/package-format-library.md` (the "specVersion 31" sections) and `docs/customizable-workflow/engine-v31.md`.

**Not this area's** (each has a seam here): the 31 diagram and `dag.mmd`'s bytes (area 13: publish writes what `PublishOptions.Diagram` gives, and nothing when it is null); the Create page, History, the Editor's bar, "Download package" and showing the report (area 12: this area gives the response members); the Editor's "Run tests" (Milestone 41); the public repository's tag CI and Release assets (`consultologist-workflows` is read-only for this build: this area gives the script CI will call); the bulk migration run, moving pins in bulk, telling owners, releasing contracts, Azure settings (the user's).

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

- [ ] **Step 1: Failing tests.** `AnEngineFaultWhileRendering_FailsTheNodeWithA11008_NotA06008`; `ARenderThatFailsOnPackageContent_StillSaysA06008`; a contract test that A08-006's audience is `operator` in `V31Contract` and `refusal-sentences.md`.
- [ ] **Step 2 to 4: Run, implement, run.** Add `PublishAreaSentences` to `V31Contract` with A11-008 and the `A11-` dispatch branch; add the README table "Publish (area 11)" in the content repo with A11-008 (later tasks append rows), push it on `m39-build`, move the pin.
- [ ] **Step 5: Commit** `PackageFormat (#1508): an engine fault at run time has its own sentence, and A08-006 is the operator's`; push.

### Task 4: The smaller safety fixes

**Files:** find each with grep in `src/Consultologist.Api/V31/` and `src/Consultologist.PackageFormat/V31/Run/`: the job time limit (`jobMinutes`, `job-slow`), the operator alert (`OperatorAlert`), OCR (`prebuilt-read`, `AnalyzeResult`), S06-035 (`tests/V31Source/` checks tests).

- [ ] **Step 1: Failing tests,** one per item of #1508 comment 4, item 5: (a) `S06035_FailsClosed_ThroughAGatesDependencies` (a gate whose `if` reads a node that reads a nullable value, two steps away: refused); (b) `TheJobTimeLimit_IsCheckedAfterAHostRulesAnswerReturns` (a rule activity that returns after the limit: the job fails with S09-012 before anything else is dispatched); (c) `TheOperatorAlert_IsItsOwnActivity` (the orchestrator schedules an alert activity, named `send-operator-alert-v31`, and does not await a send inline; a failed alert never fails the job); (d) `TheOcrModelsDatedVersion_IsRecorded` (`AnalyzeResult.ModelVersion`, not `prebuilt-read` alone, in the input's extraction versions).
- [ ] **Step 2 to 4: Run, implement, run** the whole non-E2E suite (shared engine code). If (a) passes as written, keep the test and change nothing.
- [ ] **Step 5: Commit** per item (`Api (#1508): …`, `Tests (#1508): …`); update `engine-v31.md` (the drain's list of new history events gains the alert activity); push.

### Task 5: The ruled spec edits, and the check's sentences

**Files:** Modify `docs/customizable-workflow/package-format-v31/02-types-and-inputs.md` § 8.2, `09-engine-and-pages.md` § 5.1 (the documents bullet) and § 1.2 step 3 (R3), `03-expressions.md` § 4.1, `06-checks-deliverables-profile.md` § 6.7; in `~/Projects/consultologist-package-format`: `package-format-v31.md` (chapter 3 § 3.4.1 and the S03-019 row, and the twins of the edits above), `v31/sentences.json` if a row's words change; in `~/Projects/consultologist-provenance`: `hash-definitions.md` (`effectiveInputHash` definition 7); `src/Consultologist.PackageFormat/V31/CheckSentences.cs` and wherever a check's failure says "Computed node".

- [ ] **Step 1: Edit the texts** exactly as #1508 comment 4 items 6 and 7 say: a document sent to a Markdown input is "extracted text, as at 30" (the converter is Milestone 40, #1442), in part 02 § 8.2, part 09 § 5 and definition 7; `checks/` may read a deliverable's `.content`, `.value` and `.state`; a gate comes after its deliverables in § 6.7's order; part 09 § 1.2 step 3 says the pointer names the lock by file name and the stamp carries `lockHash`.
- [ ] **Step 2: Failing test** `AChecksFailure_SaysCheck_NotComputedNode` for every sentence a check can fail with at publish and at run time; implement by passing the subject (`Check '<id>'`) through.
- [ ] **Step 3: Run** V31 tests and both content repos' checks (`python3 scripts/check-v31.py`; provenance's own check script if it has one). Regenerate the refusal packages whose sentence moved (`RefusalPackagesExport`), read the diff.
- [ ] **Step 4: Commit** in each content repo, push `m39-build`, move the pins; commit here `Docs (#1508): the ruled spec edits …` and `PackageFormat (#1508): a check's failure says "Check"`; push.

## Part B: the compiled documents, named and closed

### Task 6: Name every compiled member; the stamp's schema

**Files:** Modify `docs/customizable-workflow/package-format-v31/07-compiled-documents.md` §§ 2.2, 2.5, 5; in the content repo `package-format-v31.md` (its twin chapter), `v31/schemas/resolve.schema.json`, Create `v31/schemas/publish-stamp.schema.json` with `v31/conformance/schemas/publish-stamp/` examples (valid: the three members; the three with `migratedFrom`; invalid: a missing hash, an uppercase hash, `specVersion` 30), `scripts/check-v31.py`.

**Interfaces:** Produces: the schema every later golden validates against. The stamp: `{ "specVersion": 31, "resolveHash", "lockHash", "migratedFrom"?: string }`, other members refused by the schema (the reader still ignores them).

- [ ] **Step 1: List what the compiler writes.** Run the compiled goldens through a throwaway script that collects every member name per entry kind, and compare with part 07 § 2.5 and the schema. The ruled list (R3) is the minimum; name anything else found.
- [ ] **Step 2: Write part 07** (a row per member in § 2.5's tables: `names`, `placements` with its five members, `runSpots.unit`, `runSpots.neutral`, `plainConverts`, `derivedFrom` in § 2.2, `"ret": null` on `if` and `case`, embedded built-in renderers in § 2.2's `renderers` row; § 5 step 4.6 names `publish.json`'s members) and the schema. **Do not close `entry` yet** (Task 9 does, after the goldens settle).
- [ ] **Step 3: `check-v31.py`:** a guard that every member name the schema's entry definitions name appears in `package-format-v31.md`, and the same for the record's member names (#1508 comment 4 item 7, last bullet: "guards the new compiled and record member names"); a `publish-stamp` schema group.
- [ ] **Step 4: Run** `python3 scripts/check-v31.py`; here `--filter "FullyQualifiedName~V31"` after moving the pin (the compiled goldens must still validate).
- [ ] **Step 5: Commit** there and here (`Docs (#1508): part 07 names every compiled member`); push both.

### Task 7: Formats in the bundle reference each other by `$id`; `requires` stays minimal

**Files:** Modify `src/Consultologist.PackageFormat/V31/CompiledFormats.cs`, `src/Consultologist.PackageFormat/V31/Run/PlanFormats.cs` (resolving a `$ref` that is a URN through the bundle's `$id`s), `src/Consultologist.PackageFormat/V31/ResolveCompiler.Requires.cs`; Tests `tests/V31Resolve/`; goldens `tests/V31Resolve/Golden/*`, `tests/V31Run/Golden/*`.

- [ ] **Step 1: Failing tests.** `AFormatThatNamesAnother_ReferencesItByItsId` (in the bundle, `{ "$ref": "urn:consultologist:…:format:<id>" }`; an input's and a node's `format` stays `#/$defs/<id>`); `TheBundle_ValidatesAsOneCompoundDocument` (Corvus, through our interface, validates a value against a format that references another by `$id`); `ThePlan_ExpandsAUrnReference` and `FormatHashesAndTypeHashes_DoNotMove` (hashes are over dereferenced content, R2: "published formats keep their bytes"); `AnUnplacedSnippetThatWritesRun_DoesNotTurnOnRunSpots`; `APlacedOne_Does`.
- [ ] **Step 2 to 3: Run, implement.** `Complete` and every hash other than `resolveHash` should hold; if a `formatHash` moves, stop and record why before regenerating.
- [ ] **Step 4: Regenerate** (`GOLDEN_UPDATE_V31=1`), read every changed golden's diff (only `$ref`s inside `formats` and `requires.features` may move, and `resolveHash`/`lock.resolve.hash` with them), export `CompiledDocumentsExport` to the content repo, run its check, push, move the pin.
- [ ] **Step 5: Run** the non-E2E suite (the 31 engine reads these); **commit** `PackageFormat (#1508): bundled formats reference each other by $id` and `…: requires lists run-spots only for placed units`; push.

### Task 8: The publish refusals the rulings add, and the limits' notes

**Files:** Modify `src/Consultologist.PackageFormat/V31/PublishNotes.cs`, `ResolveSentences.cs`, `FormatDialect.cs` or `SourceTypes.cs` (where a format file's `$defs` are read), `ResolveCompiler.cs`; Create cases in `tests/V31Source/RefusalPackages.Publish.cs`; Tests `tests/V31Resolve/PublishNotesTests.cs`.

**Interfaces:** Produces:
```csharp
public sealed record PublishLimits(int Ceiling, int ItemsPerLoop, int CallsPerJob, int? Instances = null, int? JobMinutes = null);
```
`PublishNotes.Refusals(SourcePackage, Resolution, PublishLimits)` → A11-001 (empty when `Instances` is null); `PublishNotes.Of` adds the minutes note (D16) when `JobMinutes` is given. `ResolveCompiler.Compile` returns A11-001 and A11-003 among its refusals.

- [ ] **Step 1: Failing tests.** A11-001: three nested loops with `max` 200 each and no agent beneath (8,000,000 instances) is refused, naming the outermost loop's rooted key; a package at exactly `Instances` is not; an unbounded path is not refused. Count instances as the runner opens them (part 09 § 3.4: a node, check or deliverable instance, a loop item, a pass, a call); read `PlanRunner.cs:279` and count the same things, saturating. A11-002: `formats/a.json` with `"$ref": "b"` and `"$defs": { "b": … }` is refused; a `$defs` entry no reference names is today's behaviour. A11-003: a format whose expansion passes `PlanFormats.NodeBudget` is refused at publish with the format's id (use the budget's own counter so publish and run agree; a test holds the two to one constant). The minutes note: said when the estimate passes `JobMinutes`, with S09-016's words.
- [ ] **Step 2 to 3: Run, implement.**
- [ ] **Step 4: Refusal packages** for A11-001 to A11-003 (valid and invalid), exported (`RefusalPackagesExport`); README table rows; `V31Contract.PublishAreaSentences`; `refusal-sentences.md`. Part 07 § 10 and part 04 § 10.2 gain the three sentences ("ruled by the owner on #1508"); part 09 § 3.5 says the instances limit refuses.
- [ ] **Step 5: Run V31 tests and the content check; commit; push** both.

### Task 9: A compiled case with a classifier, a macro folder, a part, a run spot and a built-in renderer; close `entry`

**Files:** Modify `tests/V31Resolve/ResolveFixture*.cs` (where `NestedNote()`, `Conditional()`, `Functions()` are), `tests/V31Resolve/CompiledDocumentsExport.cs`, `CompiledGoldenTests.cs`; Create goldens `tests/V31Resolve/Golden/macros.{resolve,lock}.json`; content repo `v31/schemas/resolve.schema.json`, `v31/conformance/compiled-documents/macros/`.

- [ ] **Step 1: Write the case `Macros()`**, fictional (a letrozole-monitoring macro in a folder, offered by a `judgement` classifier; an agent with one part; a deliverable with a `run.date` spot; a `concept_list` read through `renderer.concept_bullets`). Reuse `tests/V31Run`'s macros fixture if it already has this shape (`macros.run.txt`).
- [ ] **Step 2: Failing test** `ThePinnedFamily_IsWhatPublishGives` gains the case; `EveryGolden_ValidatesAgainstTheSchema` (if the suite has no in-repo schema validation of goldens, add one through the library's validator interface, reading the schema from `external/`).
- [ ] **Step 3: Generate, read the golden:** it must have non-empty `placements`, a `runSpots` entry with `unit` and `neutral`, a `macros` folder entry, `parts`, and `renderers["renderer.concept_bullets"]`.
- [ ] **Step 4: Close `entry`** in the schema to unknown members, per kind. Every golden (areas 05 to 10's and this one) and every conformance example must validate; add invalid examples (`v31/conformance/schemas/resolve/`: an entry with an unknown member).
- [ ] **Step 5: Export, check, push** the content repo; move the pin; run V31 tests; commit `Tests (#1508): a compiled case with macros, a part, a run spot and a built-in renderer`; push.

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

- [ ] **Step 1: Failing tests.** S07-010 for each declared input the file does not list (a listed `null` for an optional input is fine; `null` for a required one is the start's own refusal, surfaced by Task 11 as a `run` mismatch); S07-011 when `profile`'s keys are not exactly `needs.profile` (the sentence lists the fields the workflow reads, sorted; `none` when empty — use the filler earlier sentences use for an empty list); S07-012 the same for `run` against `resolve.json`'s `run` (`run.date` must be `{ "value", "timeZone" }` with both strings, else A11-005); A11-005 for an `expect` member that is not in § 8.3's table or has the wrong shape, and for an `outputs` key that is not `<rooted key>` optionally followed by `:<path>` and `#<n>` parts (reuse `RunKeys`/`RecordKeys` parsing if there is one); every refusal found is returned, in member order. Review Focus 3's hostile shapes, each asserting a sentence and no exception.
- [ ] **Step 2 to 4: Run, implement** on `SnapshotTest.Request` for the request (pass `GenerateMissing: false` once Task 11 adds it), **run.**
- [ ] **Step 5: Commit** `PackageFormat (#1508): a test file is read against the compiled package`; push.

### Task 11: Strict stubs in the harness

**Files:** Modify `src/Consultologist.PackageFormat/V31/Harness/OfflineRun.cs`, `OfflineHost.cs`, `OfflineStubs.cs`; Test `tests/V31Publish/StrictStubsTests.cs`.

**Interfaces:** Produces: `OfflineRequest(…, Func<string,string>? StubSeed = null, bool GenerateMissing = true)`; `OfflineResult` gains trailing `IReadOnlyList<string> MissingOutputs` (row keys asked for with no stub, ordinal order) and `IReadOnlyList<StubFault> InvalidOutputs` where `public sealed record StubFault(string Key, string Format, string Error)` (`Format` the type string as sentences print formats, `Error` the first validation error: path and keyword only, never the value).

- [ ] **Step 1: Failing tests.** With `GenerateMissing: false`: a call with no stub is not generated, its key is in `MissingOutputs`, and the run ends without calling later dependants' stubs as missing too (only nodes that were actually asked are listed); a stub invalid against its node's format (validated as an agent reply is, envelope included) is in `InvalidOutputs`, and the reply is not repaired by regeneration; a valid stub for a classifier decides the condition. With the default, area 10's tests and goldens do not move (run `--filter "FullyQualifiedName~V31"` and the byte-equivalence tests).
- [ ] **Step 2 to 4: Run, implement, run.**
- [ ] **Step 5: Commit** `PackageFormat (#1508): the offline run can refuse to generate a missing stub`; push.

### Task 12: Expectations, the first mismatch and the outcome hash

**Files:** Create `src/Consultologist.PackageFormat/V31/Publish/TestExpectations.cs`, `TestOutcome.cs`; Test `tests/V31Publish/TestExpectationsTests.cs`.

**Interfaces:** Produces:
```csharp
public sealed record TestMismatch(string Expectation, string Key, string Expected, string Actual);
public static class TestExpectations { public static TestMismatch? First(RunPlan plan, JsonObject expect, OfflineResult result); }
public static class TestOutcome    { public static string Hash(OfflineResult result); }   // D8
```

- [ ] **Step 1: Failing tests,** one passing and one failing per member of § 8.3's table, on a small compiled fixture (reuse `tests/V31Run` fixtures: a loop, a `while`, a `case`, a check loop, a gated deliverable): `ran`, `skipped` (by condition; not run by `until`; not run by `max`), `decisions` (boolean and branch id, by part 08 § 5's decision ids as `RunDecision` carries them), `loops` (`count`; `passes` and `stopped`), `values` (canonical JSON equality: `1.0` equals `1`, member order ignored), `prompts` (`text` and `hash`; a mismatch reports hashes both ways), `checks` (`pass`), `deliverables` (state with `text`, with `hash`, and `{ "state": "skipped" }` alone; `failed` expected and got passes a run that failed through that deliverable's `fail`). Ordering (D7): two failing expectations report the earlier member, then the lower key. `OutcomeHash_IsTheSame_ForTwoRunsOfOneTest` and `…_Differs_WhenAStubChangesAPrompt`.
- [ ] **Step 2 to 4: Run, implement, run.**
- [ ] **Step 5: Commit** `PackageFormat (#1508): a test's expectations, its first mismatch and its outcome hash`; push.

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

- [ ] **Step 1: Failing tests.** A passing test → `pass`, no refusal; a failing one → S07-007 with D7's words filled in, word for word against `sentences.json`; a missing stub → S07-008 (first missing key); an invalid stub → S07-009; an unused stub → S07-014 in `Warnings`, the test still passes; results in id order; every failing test's sentence is given (publish stops after all tests ran, § 5 step 3 "with the test's id and its first mismatch" per test); the 60 seconds with a fake `TimeProvider` advanced by the run → S07-013 alone in `Refusals`, later tests not run (D9); Review Focus 5 (a `prompts` mismatch and S07-009's error carry no stub text: assert the fictional stub string is in no sentence); area 10's `migration_snapshot` of a migration vector passes on its own package (read one vector from `external/…/v31/conformance/migration-vectors/`, fail loudly when missing).
- [ ] **Step 2 to 4: Run, implement, run.**
- [ ] **Step 5: Commit** `PackageFormat (#1508): in-package tests run offline against the compiled package`; push.

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

- [ ] **Step 1: Failing tests.** A published and a refused report each validate against `external/consultologist-package-format/v31/schemas/publish-report.schema.json` (through the library's validator interface); `publishedAtUtc` is RFC 3339 UTC with seconds (`2026-12-01T15:04:05Z`), null when refused; `warnings` and `refusals` are `{ sentence, at }` with `at` null for the package as a whole (as `resolve.json`'s warnings write it); canonical bytes are stable; an engine fault's exception type is not in the JSON.
- [ ] **Step 2 to 4: Run, implement, run.**
- [ ] **Step 5: Commit** `PackageFormat (#1508): the publish report`; push.

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

- [ ] **Step 1: Failing tests.** The happy path on `ResolveFixture.NestedNote()` with one passing test: `Published`, the file order above, `resolve.json`'s bytes hash to the report's `resolveHash` and the stamp's, `lock.json`'s to `lockHash`, `CompiledPackage.Verify` accepts the pair against the stamp, and `RunPlan.Build` builds it (what the engine will load). A source refusal → refused report with every sentence and path, no files, hashes null. `Complete == false` → refused with an engine fault (A11-007), never stored. A failing test → refused, `tests` filled, no files. An uploaded `resolve.json`, `lock.json`, `publish-report.json`, `publish.json` and `dag.mmd` are in none of the files (and the first four in `Dropped`). `\r\n` files appear in `normalised` and are stored normalised. The same upload twice gives the same `resolveHash`, `lockHash` and `outcomeHash`es (only `publishedAtUtc` and `durationMs` differ). `CheckAsync` gives the same report with `outcome` as it would be, `publishedAtUtc` null and no files. An exception thrown inside (inject through `Diagram`) → refused with A11-007 and `EngineFault` set, never thrown. The warnings in the report are `resolve.json`'s plus the tests' (S07-014), and the ceiling note is among them when the fixture passes the ceiling.
- [ ] **Step 2 to 4: Run, implement, run.** The tests run against the plan built from the in-memory `resolve.json` (`RunPlan.Build(compiled.Resolve, compiled.ResolveHash, compiled.LockHash, options.MatchTimeLimit)`), never the source.
- [ ] **Step 5: Commit** `PackageFormat (#1508): publish, steps 0 to 4, as one call`; push.

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

- [ ] **Step 1: Failing tests** (NSubstitute; mind AGENTS.md's two NSubstitute traps). Not admitted → S09-001, nothing read or written (and with the setting unset no storage client is created). Admitted: target name and access exactly as the 5–30 path decides them (reuse `ResolveTargetNameAsync` and the ownership record); D2's manifest rewrite (the stored `manifest.json` has the assigned `name` and `version`, every other member and `null` as uploaded); A11-004; `publishedBy` per D4; a `MigrationReport` from a caller who is not the operator is refused as today's forbidden answer, and from the operator it is stored and the stamp has `migratedFrom`; a refusal returns the report and writes nothing, logs each `EngineFault` (type only), and sends the operator alert `OperatorAlertKinds.PublishFailed` (through the alert seam area 08 built; lengths and ids only); a publish sets `latest.json` and flips the account pin exactly as the 5–30 path does, **after** the stamp; `WriteAsync` false → retry on the next CalVer up to the 5–30 path's three times, then A11-006; the response's `Warnings` are the report's sentences and `Report` the report.
- [ ] **Step 2 to 4: Run, implement, run** the non-E2E suite. `PublishOptions`: `PublishedTypeRegistry`, `EngineToolchain.Current()` (check its names are the release's: `package-format@<the pinned release>`, `formats@…`, `agents@…`, read from where the engine already reads them; record what they are), the engine's limits, `TimeProvider.System`, the engine's match time limit, `Diagram: null` (D1).
- [ ] **Step 5: Commit** `Api (#1508): WorkflowPackages/Publish publishes a 31 package where the preview setting admits it`; push.

### Task 17: Writing the version's folder

**Files:** Create `src/Consultologist.Api/V31/V31RegistryWriter.cs`; Modify `WorkflowPackageRegistryWriter.cs` only to share the container lookup; Test `tests/V31Engine/V31RegistryWriterTests.cs` (an in-memory fake of the blob seam `BlobRegistryFiles`/`IRegistryFiles` uses; extend that seam with write, delete-by-prefix and create-if-absent rather than adding a second one).

- [ ] **Step 1: Failing tests.** Files are written in the order given, then `publish.json` last with create-if-absent (If-None-Match `*`), media types set (`application/vnd.consultologist.resolve+json`, `…lock+json`, `…publish-report+json`, `application/json` otherwise, `text/markdown` for `.md`); `IsPublishedAsync` is true only with a stamp; a folder with files and no stamp is cleared before writing (**Review Focus 1:** after a first attempt that wrote `nodes/old.md` and died, a retry without that file leaves no `nodes/old.md`); a folder with a stamp is never touched and `WriteAsync` returns false; **Review Focus 2:** two writers interleaved file by file on one version: exactly one stamp is created, and the loser returns false (the loser's files may have overwritten the winner's before the stamp: so **the stamp's create is preceded by re-reading `resolve.json` and `lock.json` and checking their hashes against the stamp about to be written; on a mismatch return false without stamping**, and the engine's load-time `Verify` is the last line of defence; test both orders); after a write `CompiledPackageStore.TryLoadAsync` (the real one, on the same fake) loads the version.
- [ ] **Step 2 to 4: Run, implement, run.**
- [ ] **Step 5: Commit** `Api (#1508): a 31 version's folder is written source first, resolve.json last, then the stamp`; push.

### Task 18: `WorkflowPackages/Current` at 31

**Files:** Create `src/Consultologist.Api/V31/V31PackageDescription.cs`; Modify `WorkflowPackageModels.cs` (`WorkflowPackageInputResponse` gains trailing `JsonElement? Format = null, string? Channel = null`, exactly part 09 § 4.3's record), `WorkflowPackages.cs` (`GetCurrentAsync`), `WorkflowPackageStore.cs` only if the pin resolution must stop before reading the manifest; Test `tests/V31Engine/V31PackageDescriptionTests.cs`, and the existing `Current` tests.

**Interfaces:** Produces: `V31PackageDescription.Describe(RunPlan plan)` → `WorkflowPackageResponse` (or from the `resolve.json` `JsonObject`; take whichever `CompiledPackageStore.TryLoadAsync` already gives).

- [ ] **Step 1: Failing tests.** The resolved pin is tried on `CompiledPackageStore.TryLoadAsync` first (it returns null without a 31 stamp, and does nothing unless admitted), then today's path. For the `Conditional` and `NestedNote` goldens: `SpecVersion` 31; `Blocks` per D12 (`Id` `node.plan`, `Name` its label; no nested node); `Inputs` in `inputOrder` with part 02 § 12's `Type` table row by row (one test per row, built from small inline `resolve.json` inputs), `Values` for `enum` and labelled `oneOf`, `Items` and `Fields` at depth (D13), `ExpectedContent` and `Channel` both the `channel`, `Types` null, `Format` self-contained (every `$ref`, `#/$defs/…` or URN, replaced by its definition with `$id` kept; no `$ref` left; titles and annotations kept); `Results` per D12; `Macros` `[]` (not null); `Title`. Below 31: the JSON of a 5–30 response is byte-identical to before but for `"Format":null,"Channel":null` on each input (the worker writes every member; AGENTS.md "JSON response casing") — check the satellites' reader tolerates unknown members by reading `EngineDtos` is not possible here, so pin today's serialised response in a test and show only those two members were added. **Review Focus 4:** a pin to a 31 version with the setting off → 400 with S09-001; a load refusal (S09-009, S09-014, a `requires` sentence) → 400 with that sentence.
- [ ] **Step 2 to 4: Run, implement, run** the non-E2E suite.
- [ ] **Step 5: Commit** `Api (#1508): WorkflowPackages/Current describes a 31 package from resolve.json`; push.

### Task 19: `Current/Content` and import at 31

**Files:** Modify `WorkflowPackageModels.cs` (`WorkflowPackageContentResponse` gains trailing `JsonElement? Resolve, Lock, PublishReport`, `string? MigratedFrom`, `IReadOnlyList<string>? Notices`, all null by default), `WorkflowPackages.cs` (`GetCurrentContentAsync`, `IngestAsync`), `WorkflowPackageIngestor.cs`, `WorkflowPackageArchive.cs` (bytes for a 31 archive); Create `src/Consultologist.Api/V31/V31PackageContent.cs`; Test `tests/V31Engine/V31PackageContentTests.cs`.

- [ ] **Step 1: Failing tests.** Content (D14): `Files` every stored source file by path; `Manifest` is the stored `manifest.json` member for member (a `null` the author wrote is there; no member added: compare `JsonElement.GetRawText()` re-canonicalised with the stored text's); `Resolve`, `Lock`, `PublishReport` parsed from the stored bytes, and `CanonicalJson.HashText` of `Resolve`'s canonical text is the stamp's `resolveHash`; `MigratedFrom` from the stamp (D5); the caller's access is checked exactly as for a 5–30 version (an account that does not own a private package gets today's answer); only a stamped version is served; setting off → S09-001 400. Below 31 the response's JSON is as before but for the new null members. Import (D15): a 31 zip → the content response read-only, with `Notices` = [S07-016] when it held `resolve.json` or `lock.json`, and none otherwise; steps 0 to 2 are run in memory (`PackagePublish.CheckAsync` is not needed: `SourceCheck.Run` is enough) and a source refusal is returned as the 400's sentences; not admitted → S09-001; a 5–30 zip unchanged; a zip entry that is not UTF-8 → the intake's sentence, no exception.
- [ ] **Step 2 to 4: Run, implement, run** the non-E2E suite and `dotnet build Consultologist.sln` (the Web project mirrors these records: if it has its own copy of the response records, add the members there too, unused).
- [ ] **Step 5: Commit** `Api (#1508): the content endpoint and import at 31`; push.

### Task 20: The scripts: check, publish from CI, and the corpus re-render

**Files:** Create `scripts/check-workflow-package-v31.cs`, `scripts/rerender-workflow-packages-v31.cs` (file-based C# scripts, as `scripts/migrate-workflow-package.cs` is: copy how it references the library and the registries); Test `tests/V31Publish/ScriptsTests.cs` if the existing scripts have tests (follow `grep -rn "migrate-workflow-package" tests/`), else a section in the docs with the commands run and their output.

- [ ] **Step 1: `check-workflow-package-v31.cs <folder> [--write <out folder> --version <tag> --published-by "ci <tag>"]`:** reads the folder as an upload, runs `PackagePublish.CheckAsync` (or `RunAsync` with `--write`, writing `Files` and `publish.json` into `<out folder>` for CI to upload and attach as Release assets), prints the report's canonical JSON, prints S07-015 for each evidence file found in the folder, and exits 1 on a refusal. Without `--version` the manifest's own `name` and `version` are used.
- [ ] **Step 2: `rerender-workflow-packages-v31.cs <folder of name/version folders>`** per D11.
- [ ] **Step 3: Run both** on a fixture exported from `tests/V31Resolve` (the `compiled-documents/macros/source` folder in `external/`): the check passes; a re-render of what `--write` wrote reports no difference; editing a stub in the written copy's test is reported.
- [ ] **Step 4: Commit** `Scripts (#1508): check and publish a 31 package from a folder, and re-render the published corpus`; push.

### Task 21: The weekly summary's largest record

**Files:** Modify `src/Consultologist.Api/V31/OperatorWeeklySummary.cs` (lines 15–39), the sealer (`RecordSealer`, where the size is logged at sealing); Test the existing summary tests in `tests/V31Engine/`.

- [ ] **Step 1: Failing tests.** `TheLargestRecordLine_IsTheWeeksLargestSealedRecord` (`Largest record: <n> bytes, <rows> rows (job <id>)`, numbers in the invariant culture with thousands separators as the other lines write them); `WithNoSealedRecord_TheLineSaysNone`; `WhenTheSizesCannotBeRead_TheLineSaysSo_NeverZero` (as R11's line does).
- [ ] **Step 2 to 3: Implement.** At sealing, write the record's byte length and row count on the job header (or the job index row the summary already reads: take whichever the summary's job query returns without a blob read per job); the summary takes the maximum over the week. No new table.
- [ ] **Step 4: Run** the non-E2E suite; update `engine-v31.md` "Alerts and the weekly summary" (line 555).
- [ ] **Step 5: Commit** `Api (#1508): the weekly summary measures the largest record`; push.

## Part E: the contract's fixtures and the documents

### Task 22: Conformance: the publish report's fixtures and the test sentences' packages

**Files:** Modify `tests/V31Resolve/CompiledDocumentsExport.cs` (each case folder gains `publish-report.json` and `publish.json`; the `macros` and `nested-note` cases gain a `tests/` file each, one with an unused stub so S07-014 is in the report, and `nested-note`'s limits set so the ceiling note is), `tests/V31Source/RefusalPackages.Publish.cs` (S07-007 to S07-013 and A11-005, valid and invalid; the invalid side's `expect.json` carries the sentence; S07-013 is listed in `RefusalPackagesTests`' not-shipped list with its reason, since no package can state a clock), `RefusalPackagesTests.cs` (the runner calls `PackagePublish.CheckAsync` for these ids); content repo `scripts/check-v31.py` (the compiled-documents group validates `publish-report.json` and `publish.json` against their schemas and checks their hashes against the pair), `v31/conformance/README.md`, `v31/conformance/index.json`.

- [ ] **Step 1: Failing tests** `ThePinnedFamily_IsWhatPublishGives` over the report (with a fixed clock and `durationMs` written as 0 in the fixture: say so in `about.txt` and the README) and the stamp; each new refusal package gives its sentence.
- [ ] **Step 2 to 3: Implement, export** one filter at a time, read `git status` and the new files in the content repo.
- [ ] **Step 4: Run** `python3 scripts/check-v31.py`; push the content repo; move the pin; run V31 tests here.
- [ ] **Step 5: Commit** `Tests (#1508): the conformance suite gains the publish report, the stamp and the test sentences`; push.

### Task 23: The documents

**Files:** Modify `docs/customizable-workflow/package-format-library.md` (a section "specVersion 31: publish (preview)" after "migration": the pipeline, the test runner, the report, D1 to D16, what it leaves to areas 12 and 13; and fix every "area 11" forward reference found by `grep -n "area 11\|Area 11"`), `docs/customizable-workflow/engine-v31.md` (publish, `Current`, `Content`, import; the settings), `docs/CONFIGURATION.md` (a row for `WorkflowPackages__PreviewSpecVersions`; dev's registry: `WorkflowPackages__BlobServiceUri` and `__PublicBlobServiceUri` point at containers in `consultdevcaeast` on `canada-east-ai-function-dev`, part 09 § 2.2), `docs/customizable-workflow/registry-operations.md` (a section "specVersion 31": the folder's layout, the stamp, how dev's registry is filled — the catalogues copied from the release tags, example packages through `WorkflowPackages/Publish` from the migration branch — the two scripts, the corpus re-render before a library upgrade, and **"Before 31 leaves preview"** with R7's conditions word for word from #1508 comments 3 and 4), `docs/README.md` if it indexes new sections, `refusal-sentences.md` (A11 rows, if a task left any out), part 07 § 10 (the A11 sentences said at publish).

- [ ] **Step 1: Write them.** Plain words; every claim checked against the code as built (open the file, do not write from this plan).
- [ ] **Step 2: Run** the docs' own checks if any (`grep -rn "docs" scripts/*.sh | head`; the content repo's `check-v31.py citations` group if the edits cite it).
- [ ] **Step 3: Self-check against the spec:** walk the coverage table below; for each row open the code or test named and confirm it exists. Fix or record what does not.
- [ ] **Step 4: Commit** `Docs (#1508): publish at 31, dev's registry, and what must hold before 31 leaves preview`; push.

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
| § 8 in-package tests | 10 to 13; § 8.4 snapshot: 13; R9 |
| § 9 the publish report | 14, 15, 22 |
| § 10 refusals | start and load: exist (area 06); tests: 10 to 13; warnings: 13, 19, 20 |
| §§ 2, 3, 4 as ruled | 6, 7, 9 |
| part 09 § 1.3, § 2.1 admission | 16, 18, 19 (every 31 branch asks `SpecVersionAdmission`) |
| part 09 § 2.2 dev's registry | 23 (settings, how it is filled); the Azure settings are the user's |
| part 09 § 3.5 the publish note | exists (S04-072, S04-073, S04-079, S04-080, area 05); minutes, instances: 8 |
| part 09 § 4, part 02 § 12 `Current` | 18 |
| part 09 § 8.2 content | 19 |
| part 07 § 4.4 corpus re-render | 20 (D11), 23 |
| part 10 § 1.5 migrated versions | 16 (D5), 19 (`MigratedFrom`) |
| part 10 § 5 fixtures | 22 |
| #1508 comment 1 | 10, 12 (R9) |
| #1508 comment 2 | 8 (items 1, 4, 5), 7 (item 2), 6 and 9 (item 3) |
| #1508 comment 3 | 6 and 9 (item 1), 7 (item 2), 23 (item 3) |
| #1508 comment 4 | 1 (item 1), 2 (items 2, 3), 3 (item 4), 4 (item 5), 5 (items 6, 7), 6 (item 7's guard), 21 (item 8), 23 (the added conditions) |

## Recorded in the build

*(Each task adds a line here for a choice this plan does not state.)*
