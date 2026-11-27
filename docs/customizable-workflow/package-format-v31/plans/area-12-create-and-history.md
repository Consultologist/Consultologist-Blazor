# Area 12: Create and History (specVersion 31) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A clinician can fill in, start, watch and read back a specVersion 31 job in the web app: the Create page draws a 31 package's inputs from their formats and says the start's refusals before submission, History shows record storage 12 in the order the job ran, Profile holds the letter details and the time zone, and the Editor opens a 31 package read-only. The owner's rulings on #1509 are applied to the engine where the pages depend on them.

**Architecture:** The Api gains only what the pages cannot do without: a start-checks route over `V31StartChecks`, the inputs sent by reference at a 31 start, rerun at 31, `record.json` in the records account, and `MigrationReport` on the content response. In the Web, everything 31 lives in new files beside the three large pages (`Consults.razor`, `History.razor`, `Templates.razor`), which each gain one branch on `SpecVersion >= 31` / `SchemaVersion >= 12` and otherwise keep their markup: pure readers and sentence builders under `Services/Provenance/V31/` and `Services/Workflow/V31/` (unit-tested without a renderer), and components under `Shared/Create31/`, `Shared/History31/` and `Shared/WorkflowEditor/V31/` (bUnit). History reads a job as its header (the job response) plus `Rows` (listed once, without text), and fetches a row's text by key only when the row is opened.

**Tech Stack:** C# / .NET 10, Blazor WebAssembly with Fluent UI, `System.Text.Json` (`JsonElement`, `JsonNode`), Azure Functions (isolated); xUnit, NSubstitute and bUnit (`tests/Consultologist.Web.Tests`, base `ClientRenderTestContext`), xUnit for the Api (`tests/Consultologist.Api.Tests.csproj`, `tests/V31Engine/`), Playwright for `tests/Consultologist.Web.E2E.Tests`.

**Spec:** `docs/customizable-workflow/package-format-v31/` — [`09-engine-and-pages.md`](../09-engine-and-pages.md) §§ 5 to 8 and their sentences in § 9, with §§ 3.6 to 3.8 and 4 for what the pages show; [`08-record.md`](../08-record.md) in full (it is what History shows); [`02-types-and-inputs.md`](../02-types-and-inputs.md) §§ 8.1, 9, 10; [`06-checks-deliverables-profile.md`](../06-checks-deliverables-profile.md) §§ 5, 7, 8, 9, 10.3, 10.4, 10.7. **The owner's rulings are the four comments on #1509** (`gh issue view 1509 --comments`): read them before your task; where a ruling and the spec differ, the ruling is the decision and the spec text is corrected in the task that applies it. How the engine works today is `docs/customizable-workflow/engine-v31.md` ("Starting a job", "The record", "`Current` at 31", "`Current/Content` at 31") and `docs/CONSULT_GENERATION_EVENTS.md` (`row-finished`).

**Not this area's** (each has a seam here): the 31 diagram, `dag.mmd`'s bytes and the run diagram (area 13: the Editor's 31 view shows `dag.mmd` through `WorkflowDagView` when the content holds it and no Graph entry when it does not; History's call rows carry `id="row-<key>"` anchors for the diagram to link to); the reaper that fails a dead job, and releasing a blocked deliverable after review (Milestone 40, #1524); editing a 31 package, "Run tests", the debugger (Milestone 41); the satellites, the email intake's replies and the SMART panel (their repositories; the sentences are the same); `emr` and `zip` delivery (#1443); Azure settings, containers' lifecycle rules and role assignments (the user's: Task 5 names what to set).

## Global Constraints

- **Worktree only:** `~/Projects/worktrees/m39-area-12` on `m39-area-12`. A content repo is worked in `~/Projects/consultologist-package-format` or `~/Projects/consultologist-provenance` on `m39-build` (create from `origin/main` if missing), pushed directly; a pin under `external/` moves only to a pushed commit. `~/Projects/consultologist-workflows` is read-only.
- **Never edit** `.github/`, `.claude/`, `scripts/m39-build/`, `AGENTS.md`, `CLAUDE.md` or `BUILD-RUNBOOK.md`, in any repo. A shell command naming one of those paths with a `>` in it is refused by the guard: write files with the file tool. Never force-push; a pushed commit is never amended.
- **31 stays off in production:** `WorkflowPackageValidator.AcceptedSpecVersions`, `WorkflowPackageStore.SupportedSpecVersions` and the Editor's `NewestSpecVersion` (30) do not change. Every Api 31 branch asks `SpecVersionAdmission` and answers S09-001 as a 400 when 31 is not admitted.
- **The 5–30 pages keep their behaviour:** no 5–30 bUnit expectation, golden, response or request changes. A page's 31 markup is reached only through `SpecVersion >= 31` (a package) or `SchemaVersion >= 12` (a job). `[Function]` strings and routes stay; records change by trailing optional members only.
- **One vocabulary** (#1509, comment 4, ruling 2): a status, state, result or effect is shown as the stored row or the job header writes it, verbatim. The Web keeps no table that renames a 31 status word. The only display strings that are not stored words are *"Delivering…"* (S09-019) for the status `delivering`, and the sentences listed below.
- **Item numbers are 1-based** wherever a person sees one (#1509, comment 2, ruling 2). Row keys already are; nothing in the Web adds or subtracts one.
- **Sentences are used as written.** A spec sentence is copied word for word from `refusal-sentences.md`; a new one is an area sentence `A12-NNN` (table below), added to `refusal-sentences.md`, to `tests/V31Source/V31Contract.cs` (a dictionary `PagesAreaSentences` and an `A12-` branch in `AssertSentence`, as A11 has) and, in Task 26, to the conformance README. Clinician sentences avoid format terms (index § 7, rule 7). No sentence the Web composes carries a value from an input, a model or the profile: only labels, ids, keys, counts, hashes and package text.
- **Retained text is shown only while retained.** After the drop a row shows its hashes and sizes and A12-002; nothing renders an empty pane. Text of a `blocked` deliverable is never offered through a copy control.
- **Accessibility as the pages have it:** every new control has a label; a disclosure is a `<details>`/`<summary>` or a button with `aria-expanded`; AGENTS.md's traps apply (a Remove inside a `<label>` slot is wrapped in the preventDefault span; a pick-to-act select is `OneShotSelect`).
- **Fictional only:** invented people and clinical text (Margaret Okafor, Dr A. Sample, Sample Oncology Clinic).
- **Commits:** small, by concern: `Api (#1509): …`, `Web (#1509): …`, `PackageFormat (#1509): …`, `Tests (#1509): …`, `Docs (#1509): …`; in a content repo `Format spec (Consultologist-Blazor#1509): …` or `Provenance (Consultologist-Blazor#1509): …`. Every message ends with exactly `Co-Authored-By: Claude <noreply@anthropic.com>`. Push after each task (`git push -u origin m39-area-12` the first time).
- **Tests:** Web: `dotnet test tests/Consultologist.Web.Tests`; Api: `dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~V31"`; a task that touches shared code (`ConsultGeneration*`, `WorkflowPackages.cs`, the models, any of the three pages) also runs `dotnet test Consultologist.sln --filter "FullyQualifiedName!~Consultologist.Web.E2E.Tests"`.
- **Each task ticks its own checkboxes in this file** in its last commit, and adds a line to *Recorded in the build* (at the end) for any choice it had to take that this plan does not state. A golden or capture file that is new or changed is named in the commit message body.

## Decisions this plan takes

Each goes in the PR body under *Decisions made during build*. R = ruled by the owner (the issue is named); D = where the spec is silent or two parts differ.

| # | Where | Choice |
|---|---|---|
| R1 | #1509 comment 1 (ruled on #1507, item 3) | History shows a blocked deliverable's text under the heading **"Refused, not delivered"**, with its `stateReason` and the check's `failWith`, in a panel with no copy control and `user-select: none`; after the drop the heading, the hash and A12-002 remain. No release button. |
| R2 | #1509 comment 2, ruling 1 | A 31 start reads `InputRefs`, `InputFormRefs` and `InputFormFiles` (Task 3). |
| R3 | #1509 comment 2, ruling 2 | Item numbers are 1-based everywhere users see them. |
| R4 | #1509 comment 3, notes 1 to 3 | Decisions owned by a node that never ran are shown under that node, which says the row's stored status (`NotRun`); a macro row whose `classifier` is `null` shows *no single classifier*; the Create page sends a per-run choice as its boolean input in `Inputs`, never `MacroChoices`. |
| R5 | #1509 comment 4, ruling 1 | `record.json` lives in its own container, `job-records`, in the records account; account closure deletes `{appUserId}/` there (Task 5). |
| R6 | #1509 comment 4, ruling 2 | One vocabulary (Global Constraints). A deliverable its check refused is `blocked`, and the banner says so. |
| R7 | #1509 comment 4, ruling 3 | Built here: *"Delivering…"* with fast polling; a named state for a job whose orchestration died (D9); `ProvenanceRecordCheck` at storage 12 and its definition 7 message (Task 6); an input's `kind` shown; the Create page sends `SignatureBlockId`. |
| R8 | #1509 comment 4, ruling 4 | Shown as built: a `case` result as the arm's position or `else`; sub-rows with their `pass`; a gate's row after its deliverables with `effect` as stored. |
| D1 | part 09 § 5.3 "the page checks what `resolve.json` declares" | The checks run in the Api, on the code the start runs: a new route `GET WorkflowPackages/Current/StartChecks` (`[Function("WorkflowPackageStartChecks")]`, query `ref` and optional `signatureBlockId`) answers `WorkflowPackageStartChecksResponse(IReadOnlyList<string> Refusals, IReadOnlyList<string> NeedsProfile)`: `V31StartChecks.Signature`'s refusal, then `V31StartChecks.Account`'s sentences, in the start's order; `NeedsProfile` is `needs.profile`. A load refusal is the same 400 `{ error }` `Current` gives; below 31 both lists are empty. Input refusals are not in it: the page's own required and shape checks cover them, and the start repeats them. |
| D2 | part 09 § 5.1 boolean; part 02 § 8.1 "a checkbox" | Every boolean input, required or not, is the three-state select (unset, yes, no), starting unset (part 09, the pages' part). A required one left unset is a missing required input. |
| D3 | part 09 § 5.1 "left blank is sent as `null`" | A blank optional top-level input is left out of `Inputs` (the start reads absent and `null` alike); a blank nullable field inside an object or a row is written as JSON `null`, so the object keeps every property. A blank text is never sent as `""`. |
| D4 | part 09 § 5.1 "its `description` as help text"; § 4.3 "No `description` member yet" | The Create page reads each input's `description` from `Resolve.inputs.<id>.description` of the content response it already loads for the rail, joined by line ends; no help text when the content cannot be read. `Current` gains nothing. |
| D5 | part 09 § 5.1 `markdown_block` "a rich-text editor (toolbar, preview)" | `Shared/Create31/MarkdownInput.razor`: a text area holding Markdown, a toolbar (bold, italic, heading, bulleted list, numbered list) that wraps or prefixes the selection, and a Preview toggle rendered by the existing `MarkdownPreview` (Markdig, advanced extensions). The value sent is the Markdown text. No HTML is stored. |
| D6 | part 09 § 5.2 | The signature select is shown when `NeedsProfile` holds `signature` and the account has more than one block. It starts on `ChosenId` (on "choose…" when none is chosen). `SignatureBlockId` is sent only when the pick differs from `ChosenId`. The start checks are asked again with the pick. |
| D7 | part 08 § 3.1 previous-run lineage | An element from an earlier run's deliverable is recorded with origin `{ origin: "previous-run", jobId, resultId, modelDerived: true }`; a decision that reads an input holding such an element is `modelDerived: true`. Node rows' flags stay the written ones (part 08 § 4). |
| D8 | rerun at 31 | `Rerun` of a storage-12 job rebuilds the request from the job's held inputs document (`inputs.<id>.value`), and sets `RerunOf` as at 30. The rerun verdict and divergence (storage 11's comparison of node hashes) are not computed at 31: they stay `null`, and History shows the link between the two jobs only. |
| D9 | #1509 comment 4, ruling 3, second item | A job is shown as **"Stopped"** when it is not terminal, has `StartedAtUtc`, and the time since then is more than 60 minutes (part 09 § 3.6's hard cap, which already covers `jobMinutes` + `deliveryMinutes` and the margin). The Web decides it (`JobLiveness31.IsStopped`), says A12-001, stops fast polling for it and offers no record download. The stored status is unchanged. |
| D10 | part 09 § 6.2 "linking to the byte-equivalence report and the list of hand edits"; § 8.1 | The content response gains trailing `MigrationReport` (`JsonElement?`, the stored `migration-report.json`, null when there is none or below 31). History's line *"migrated by the operator from <old version>"* (S09-004) links to `/editor` at the job's package ref, where the Editor's 31 view shows the report's compared texts and hand edits. |
| D11 | part 09 § 8.1 "view the publish report: warnings, notes" | The publish report has no `notes` member: the notes of § 3.5 are among its `warnings`, and the Editor lists `warnings` once, under "Warnings and notes". |
| D12 | part 09 § 7.2 "a select list of IANA zone names" | The list is `TimeZoneInfo.GetSystemTimeZones()` filtered to `HasIanaId`, in ordinal order of id; "Use this browser's zone" reads `Intl.DateTimeFormat().resolvedOptions().timeZone` through one JS interop call and selects it only if the list holds it. Clearing the select deletes the setting. |
| D13 | part 09 § 7.1 "saving whitespace clears the field" | Saving a letter-details field whose value is empty or whitespace deletes its setting (`DeleteSettingAsync`); otherwise the trimmed value is saved as `text/plain`. |
| D14 | part 09 § 8.1 | A 31 package in the Editor is drawn by `V31PackageView` in place of the editing layout; the toolbar keeps the picker, Download and Import only. Importing a 31 zip opens the same view with its `Notices`. Download adds `resolve.json`, `lock.json` and `publish-report.json` (canonical bytes re-serialised from the response members are **not** used: the members are written with `JsonElement.GetRawText()`), when the content holds them. |
| D15 | Create's run view at 31 | The rail lists `Blocks` (the top-level node keys) and sets each from `row-finished` events and the response's `RowStates`/`CurrentKey`; under a block, its item and pass rows appear as they finish (key prefix `<block key>.` or `<block key>:`/`#`). The result panel shows each `deliverable.*` row's text from `Rows?key=`, with its stored `state`. The 5–30 rail code is not touched. |

### Area sentences

| Id | Sentence | Audience |
|---|---|---|
| A12-001 | *"This job stopped before it finished, so it has no record. Run the workflow again."* | clinician |
| A12-002 | *"The text was deleted 7 days after the job finished. Its hash is kept."* | clinician |
| A12-003 | *"Part of this text comes from run facts, such as the date, so it differs from run to run."* | clinician |
| A12-004 | *"The workflow's checks couldn't be run. Reload the page to try again."* | clinician |
| A12-005 | *"The record couldn't be checked: \<what could not be read\>."* (Verify, when the record or a row cannot be fetched) | clinician |
| A12-006 | *"This workflow can't be filled in here: this page has no field for '\<input label\>'. Tell the operator."* (Create, an input whose format has no widget; Task 9) | clinician |
| A12-007 | *"The template this job ran couldn't be read, so it isn't shown."* (History, a deliverable's template the pinned version does not give; Task 20) | clinician |

A task that needs another sentence adds the next id and records it.

## Review Focus

1. **A job whose text was dropped** (older than 7 days): every History section renders from hashes alone; opening a row whose `Rows?key=` answers `text: null, textDeleted: true` shows A12-002, never a blank pane or an exception. Tests in Tasks 18 to 20.
2. **A row with a member missing, `null` where an array is expected, or a member the Web does not know** (a dev-preview record written by an earlier build): the row still renders what it has; nothing throws. Test in Task 13 (`AParsedRow_ToleratesMissingAndUnknownMembers`).
3. **A large record** (a 200-item loop, thousands of rows): the rows are listed once per load, sections render collapsed, and no row's text is fetched until its disclosure opens. Tests in Tasks 13 and 18 (`OpeningARow_FetchesItsTextOnce`, `NoTextIsFetchedBeforeARowIsOpened`).
4. **A blocked deliverable's text copied by habit:** it has no Copy button, is not in "Copy note" or any download, and sits under the "Refused, not delivered" heading at every viewport. Test in Task 20 and E2E in Task 25.
5. **The account changes under an open Create page** (the time zone set in another tab, the chosen signature block removed, one block left): the start checks are asked again on window focus and before submission, a pick naming a removed block falls back to the default, and a start the engine still refuses shows the engine's sentence. Tests in Task 11.

---

## Part A: what the pages need from the engine

### Task 1: The Web's mirrors of the 31 members

**Files:** Modify `src/Consultologist.Web/Services/Workflow/WorkflowEndpointService.cs` (`WorkflowPackageInputResponse` l.118, `WorkflowPackagePublishResponse` l.415, `PublishErrorPayload` l.893, the comment at l.385), `src/Consultologist.Web/Services/AI/AIEndpointService.cs` (`ConsultGenerationRequest` l.479, `StartConsultGenerationJobAsync` l.169 and its interface line); Test `tests/Consultologist.Web.Tests/SpecVersionMirrorTests.cs` (or the mirror test that pins these records; `grep -rln "WorkflowPackageInputResponse" tests/Consultologist.Web.Tests`).

**Interfaces — Produces:**
- `WorkflowPackageInputResponse(…, IReadOnlyList<string>? Types = null, JsonElement? Format = null, string? Channel = null)`.
- `WorkflowPackagePublishResponse(string Name, string Version, string Ref, List<string>? Warnings = null, JsonElement? Report = null)`; `PublishErrorPayload(List<string>? Errors, string? Error = null, JsonElement? Report = null)`.
- `ConsultGenerationRequest(…, Dictionary<string, InputFilePayload>? InputFormFiles = null, string? SignatureBlockId = null)` with `[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` on the new member; `StartConsultGenerationJobAsync(…, IReadOnlyDictionary<string, InputFilePayload>? formFiles = null, string? signatureBlockId = null)`.

- [x] **Step 1: Failing tests.** `TheWebInputRecord_MirrorsTheApisTrailingMembers` (member names and order equal the Api record's, by reflection, as the existing mirror tests compare); `AStartRequest_WithNoSignaturePick_WritesNoSignatureBlockId` (serialised JSON has no `SignatureBlockId` key); `AStartRequest_WithAPick_WritesIt`; `A5To30Response_ReadsFormatAndChannelAsNull`.
- [x] **Step 2: Run, fail.** `dotnet test tests/Consultologist.Web.Tests --filter "FullyQualifiedName~Mirror"`.
- [x] **Step 3: Add the members;** thread `signatureBlockId` into the request. Change the l.385 comment to say the Editor and History read them (Tasks 16, 23).
- [x] **Step 4: Run the Web suite; pass.**
- [x] **Step 5: Commit** `Web (#1509): the client mirrors Format, Channel, SignatureBlockId and the publish report`; push.

### Task 2: The start-checks route (D1)

**Files:** Create `src/Consultologist.Api/V31/V31StartChecksRoute.cs`; Modify `src/Consultologist.Api/Workflow/WorkflowPackageModels.cs` (the response record), `src/Consultologist.Api/V31/V31JobStarter.cs` (extract what the route shares: the profile read at l.278 and the zone read at l.109, into `internal static Task<(IReadOnlyDictionary<string,string?> Profile, string? TimeZone, SignatureBlocks.SignatureBlockSet Blocks)> AccountAsync(IAccountSettingsStore, string appUserId, string? displayName, CancellationToken)` or the nearest shape the existing code allows without changing the start's behaviour), `src/Consultologist.Web/Services/Workflow/WorkflowEndpointService.cs`, `src/Consultologist.Web/Services/Locations/ApiLocations.cs`; Test `tests/V31Engine/V31StartChecksRouteTests.cs`, `tests/FunctionCorsTests.cs` if it lists routes.

**Interfaces — Produces:**
- Api: `public sealed record WorkflowPackageStartChecksResponse(IReadOnlyList<string> Refusals, IReadOnlyList<string> NeedsProfile);` `[Function("WorkflowPackageStartChecks")]` `GET`/`OPTIONS` `WorkflowPackages/Current/StartChecks?ref=<package ref>&signatureBlockId=<id>`, authorised and CORS'd exactly as `WorkflowPackages/Current` is.
- Web: `Task<WorkflowPackageStartChecksResponse> GetStartChecksAsync(string? packageRef = null, string? signatureBlockId = null)` on `IWorkflowEndpointService`; `ApiRoutes.WorkflowPackageStartChecks = "WorkflowPackages/Current/StartChecks"`. A 400 throws the exception type `GetCurrentPackageAsync` throws, carrying `error`.

- [x] **Step 1: Failing tests** (fakes: `FakeRegistry`, the settings store substitute `V31JobStartTests` uses): `AnAccountWithEverythingSet_HasNoRefusals`; `NoTimeZone_SaysS06008` (exact text); `AMissingSignature_SaysS06031_BeforeTheZone`; `AMissingCredentialsLine_SaysS06032_WithItsLabel`; `ASignaturePickNotInTheProfile_SaysA08004`; `ASignaturePick_SatisfiesAPackageThatReadsTheSignature_WhenNoBlockIsChosen`; `NeedsProfile_IsThePlans`; `APackageBelow31_AnswersEmptyLists`; `A31RefWithThePreviewOff_IsA400_WithS09001`; `ALoadRefusal_IsTheSame400AsCurrent`; `AnotherAccountsPackage_IsRefusedAsCurrentRefusesIt`; `TheRoute_AnswersOptionsWithCors`.
- [x] **Step 2: Run, fail.**
- [x] **Step 3: Implement.** Resolve the ref and load as `Current` does (`CompiledPackageStore.TryLoadAsync`); read the account as the starter does; call `V31StartChecks.Signature(blocks, signatureBlockId)` only when `needs.profile` lists `signature`, put its block's text in the profile map under `signature`, then `V31StartChecks.Account(plan, profile, timeZone)`. No value from the profile is in the answer.
- [x] **Step 4: Run** the V31 filter and the solution suite; pass. Add the route to `engine-v31.md` ("Starting a job": "Asking before a start").
- [x] **Step 5: Commit** `Api (#1509): the start's account checks can be asked before a start`, then `Web (#1509): the client asks the start checks`; push.

### Task 3: Inputs sent by reference are read at a 31 start (R2, D7)

**Files:** Modify `src/Consultologist.Api/V31/V31JobStarter.cs` (`Supplied`, l.256, and `StartAsync`), `src/Consultologist.Api/Jobs/ConsultGenerationJobStarter.cs` (make `ResolveInputRefsAsync` l.1874, `VerifyInputFormRefsAsync` l.2476 and `VerifyInputFormFiles` l.2376 callable for a 31 plan: where they take a `WorkflowPackageManifest`, add an overload taking what they read of it, an input's id, whether it is an array of text, and whether its channel is `form`), `src/Consultologist.Api/Jobs/ConsultGenerationState.cs` (l.2705, the origin's doc and writer), `src/Consultologist.PackageFormat/V31/Run/RunInputsBuilder.cs` and the decision-flag code it feeds (`grep -rn "cannot trace\|ModelDerived" src/Consultologist.PackageFormat/V31/Run/*.cs`); Test `tests/V31Engine/V31JobStartTests.cs`, `tests/V31Run/` (the decisions' flags tests).

**Interfaces — Produces:** `RunInputs` gains `IReadOnlySet<string> ModelDerivedInputs` (input ids holding a previous-run element; empty by default, so every existing caller and golden is unchanged). The header's input origin for such an element: `{ "origin": "previous-run", "jobId": "<source job>", "resultId": "<deliverable id>", "modelDerived": true }`.

- [x] **Step 1: Failing tests.** `AnInputRef_ToAnEarlierRunsDeliverable_FillsATextInput_WithThatText`; `InputRefs_InAnArray_KeepTheirPositionAmongTypedRowsAndDocuments` (the request's `Position`s); `APreviousRunElement_IsRecordedWithItsOriginAndSourceJob`; `ADecisionReadingAPreviousRunInput_IsModelDerived`; `ADecisionReadingATypedInput_IsNot`; `AnInputRef_ToAnotherAccountsJob_IsRefusedAs30RefusesIt` (same outcome and words as the 5–30 path); `AnInputRef_WhoseTextWasDropped_IsRefusedAs30RefusesIt`; `AFormRef_FillsAFormChannelObjectInput`; `AFormFile_FillsAFormChannelObjectInput`; `AFormRef_OnAnInputThatIsNotAFormObject_IsRefusedAs30RefusesIt`; `ARequiredInputSentOnlyByRef_IsNoLongerA06011`; `A5To30Start_ResolvesItsRefsExactlyAsBefore` (an existing test, unchanged, still green).
- [x] **Step 2: Run, fail.**
- [x] **Step 3: Implement.** In the 31 branch, after the documents and before `V31StartChecks.Choices`: resolve refs with the 5–30 resolvers, merge each into `supplied` as the value the format takes (a text; an element of an array at its position; an object parsed from the form response, validated by `RunInputsBuilder` like a typed one), and carry the origins to the header and `ModelDerivedInputs` to the run. A refusal keeps the 5–30 outcome and words.
- [x] **Step 4: Docs.** `engine-v31.md`: replace "Not read at 31 yet" with how they are read. If `external/consultologist-provenance`'s `provenance-record.md` § 6 does not already give the `previous-run` origin's `jobId`, `resultId` and `modelDerived`, add them there (content repo, `m39-build`, push, move the pin) and to part 08 § 3.1.
- [x] **Step 5: Run** the V31 filter and the solution suite; **commit** `PackageFormat (#1509): a run knows which inputs hold an earlier run's text`, `Api (#1509): a 31 start reads inputs sent by reference`, `Docs (#1509): …`; push.

### Task 4: Rerun at 31, and the held inputs on a 31 job's response (D8)

**Files:** Modify `src/Consultologist.Api/Jobs/ConsultGenerationTransport.cs` (`RerunConsultGenerationJob` l.556, `RerunRequestFrom` l.761, and where the response's `HeldInputs` is filled), `src/Consultologist.Api/V31/RunInputsStore.cs` (a reader giving each input's `value`); Test `tests/V31Engine/V31RerunTests.cs` (new).

- [x] **Step 1: Failing tests.** `A31JobsResponse_ListsItsHeldInputs_WhileTheyAreHeld` (ids and the display text the 5–30 response gives for a typed value; a structured value as its JSON); `…_AndNoneOnceDropped`; `Rerunning_A31Job_StartsA31Job_WithTheSameInputs_AndRerunOf`; `Rerunning_KeepsTheSignaturePick_WhenTheSourceRecordedJob` (`signatureChosen: "job"` → the request's `SignatureBlockId` is the recorded block id); `Rerunning_AJobWhoseInputsWereDropped_IsRefusedAs30RefusesIt`; `ARerunsVerdict_IsNullAt31`; `Rerunning_A30Job_IsUnchanged`.
- [x] **Step 2: Run, fail.** **Step 3: Implement** by reading the 31 held document where the route reads `JobInputsPayload` for a storage-12 source. Never write the profile half of the document into a request.
- [x] **Step 4: Run; docs** (`engine-v31.md`, a "Rerun" paragraph). **Step 5: Commit** `Api (#1509): a 31 job can be rerun from its held inputs`; push.

### Task 5: `record.json` moves to the records account (R5)

**Files:** Modify `src/Consultologist.Api/V31/RecordBlobStore.cs`, `src/Consultologist.Api/V31/RecordSealer.cs` (only if it names the container), `src/Consultologist.Api/Auth/AccountClosure.cs` (l.111–146), `src/Consultologist.Api/StorageAccounts.cs`, `docs/STORAGE.md`, `docs/customizable-workflow/engine-v31.md` ("Restoring", "Retention and closure", "Settings"); Test `tests/V31Engine/RecordBlobStoreTests.cs`, `RecordSealerTests.cs`, the account-closure tests (`grep -rln "AccountClosure" tests/`).

**Interfaces — Produces:** `RecordBlobStore.RecordContainer = "job-records"`; the record blob's name stays `{appUserId}/{jobId}/record.json`. The blob service is `ConsultGenerationRecordStorage:BlobServiceUri`, else the text account's as today (so a deployment without the setting keeps working, and says so once at startup in a warning naming the setting). Text blobs do not move.

- [x] **Step 1: Failing tests.** `TheRecord_IsWrittenToTheRecordsContainer_NotTheOutputsContainer`; `TextBlobs_StayInTheOutputsContainer`; `ARecordWrittenBeforeTheMove_IsStillRead` (a blob only at the old place is read, and written to the new place when it hashes to `recordHash`, by the restore path that already exists); `AccountClosure_DeletesTheAccountsPrefix_InTheRecordsContainer`; `WithoutTheSetting_TheRecordStaysInTheTextAccount_InItsOwnContainer`.
- [x] **Step 2: Run, fail.** **Step 3: Implement.** The container is created if absent on first write, as the row table is.
- [x] **Step 4: Docs.** STORAGE.md: the container, that no lifecycle rule applies to it, and **for the user**: set `ConsultGenerationRecordStorage__BlobServiceUri` on each Function App and grant its identity Storage Blob Data Contributor on the records account (named in `docs/STORAGE.md`). Part 08 § 1.1 and part 09 § 3.7 corrected ("in the records account's `job-records` container"). Put the user's step in *Recorded in the build* for the PR.
- [x] **Step 5: Run; commit** `Api (#1509): record.json lives in the records account, and closure deletes it there`; push.

### Task 6: `ProvenanceRecordCheck` at storage 12 (R7)

**Files:** Modify `src/Consultologist.Api/Workflow/ProvenanceRecordCheck.cs`, `scripts/verify-job-hashes.cs`, `src/Consultologist.Web/Services/Provenance/ProvenanceHashes.cs`; Create `src/Consultologist.Web/Services/Provenance/V31/RecordCheck31.cs`; Test `tests/ProvenanceRecordCheckTests.cs`, `tests/Consultologist.Web.Tests/RecordCheck31Tests.cs`.

**Interfaces — Produces:**
- Api: `ProvenanceRecordCheck.CheckRecord(ConsultGenerationJobResponse header, ReadOnlySpan<byte> recordBytes, IReadOnlyDictionary<string, string?> composedTexts)` → the same result type `Check` returns: one line for `recordHash` (SHA-256 of the bytes, `sha256:` + lowercase hex, against `header.RecordHash`), and per deliverable in the document one line for `composedHash` (the text's hash) and one for the replay (`RecordReplay.Verify(text, substitutions, out fault)`); a deliverable whose text is `null` says "text deleted; hash kept" and is not a failure. `Check` on a storage-12 record without the bytes says "storage 12 is checked through the record: fetch /Record" instead of the definition default.
- `InputHash` at definition 7: with inputs supplied, compute it with the function the 31 start uses for `EffectiveInputHash` (`grep -n "EffectiveInputHash" src/Consultologist.Api/V31/V31JobStarter.cs`) and compare; without them, today's "supply them with --inputs" message, which is then true.
- Web: `public static class RecordCheck31 { public static RecordCheckResult Check(string? recordHash, byte[] recordBytes, IReadOnlyDictionary<string, string?> composedTexts); }` with `RecordCheckResult(bool Ok, IReadOnlyList<RecordCheckLine> Lines)`, `RecordCheckLine(string What, bool? Ok, string Detail)`; it calls `RecordReplay.Verify` from the referenced library.

- [x] **Step 1: Failing tests** on a sealed record built by the engine's own code (`tests/V31Engine/V31Job.cs` gives one): `ASealedRecord_ChecksOut`; `AByteChanged_FailsRecordHash`; `ADeliverablesTextChanged_FailsComposedHash_AndNamesTheDeliverableKey`; `ASubstitutionMoved_FailsTheReplay_WithRecordReplaysFault`; `DroppedText_IsNotAFailure`; `Definition7_WithInputs_IsRecomputed`; `Definition7_WithoutInputs_AsksForThem`; `AStorage12Record_WithoutItsBytes_SaysWhereToFetchThem`; the Web twin of the first five.
- [x] **Step 2: Run, fail.** **Step 3: Implement;** the script fetches `/Record` and each `deliverable.*` row's text through `Rows?key=` for a storage-12 job.
- [x] **Step 4: Run; commit** `Api (#1509): a storage 12 record is checked by its record hash and a replay of each deliverable`, `Web (#1509): …`; push.

### Task 7: The migration report on the content response (D10)

**Files:** Modify `src/Consultologist.Api/Workflow/WorkflowPackageModels.cs` (l.218), `src/Consultologist.Api/V31/V31PackageContent.cs`, `src/Consultologist.Web/Services/Workflow/WorkflowEndpointService.cs` (l.380); Test `tests/V31Engine/V31PackageContentTests.cs`, `tests/WorkflowPackageContentResponseTests.cs`.

**Interfaces — Produces:** `WorkflowPackageContentResponse(…, IReadOnlyList<string>? Notices = null, JsonElement? MigrationReport = null)`, Api and Web.

- [x] **Step 1: Failing tests.** `AMigratedVersion_ServesItsMigrationReport_ParsedFromTheStoredBytes`; `AVersionThatWasNotMigrated_ServesNull`; `AnImport_ServesNull`; `APackageBelow31_ServesNull`; `Files_StillHoldNoMigrationReport`.
- [x] **Step 2–4:** fail, implement, run. Update `engine-v31.md` "`Current/Content` at 31" and part 09 § 8.2 (the member named).
- [x] **Step 5: Commit** `Api (#1509): the content response carries a migrated version's report`; push.

## Part B: the Create page

### Task 8: Reading an input's format (pure)

**Files:** Create `src/Consultologist.Web/Services/Workflow/V31/InputShape.cs`; Test `tests/Consultologist.Web.Tests/V31/InputShapeTests.cs`.

**Interfaces — Produces:**
```csharp
public enum InputWidget { Line, Area, Markdown, Select, Date, Boolean, Number, Integer, Group, Rows, Unsupported }
public sealed record InputChoice(string Value, string Title);            // Title == Value for a plain enum
public sealed record InputShape(
    InputWidget Widget, bool Nullable, string? Title,
    IReadOnlyList<InputChoice> Choices,                                   // Select only
    IReadOnlyList<InputField> Fields,                                     // Group only, in the order given
    InputShape? Items);                                                   // Rows only
public sealed record InputField(string Id, string Label, bool Required, InputShape Shape);
public static class InputShapes {
    // format: WorkflowPackageInputResponse.Format; fields/items: the response's own, which carry written order and labels.
    public static InputShape Of(JsonElement format, IReadOnlyList<WorkflowPackageFieldResponse>? fields, WorkflowPackageElementResponse? items);
}
```
Rules (part 02 § 8.1, part 09 § 5.1; references are `{ "$ref": "<$id URN>" }` resolved in the root's `$defs` by `$id`, as `engine-v31.md` "`Current` at 31" says): a schema whose `$id` names `plaintext` → `Area`; `markdown_block` → `Markdown`; `date` → `Date`; `type: string` with `oneOf` of `const`/`title` → `Select` with titles; with `enum` → `Select` with bare values; other `string` → `Line`; `boolean` → `Boolean`; `number` → `Number`; `integer` → `Integer`; `object` → `Group`, its fields from `fields` (order and labels) and each field's shape from `properties.<id>`; `array` → `Rows`; a nullable type (`type` an array holding `"null"`, the dialect's one way, part 02 § 3.2) sets `Nullable` and is otherwise read as its other type; `false`, a top-level `anyOf`, or anything else → `Unsupported`.

- [x] **Step 1: Failing tests,** one per rule above, plus: `AReference_IsResolvedByItsId_AtAnyDepth`; `ALabelledChoice_KeepsWrittenOrder`; `AGroupInsideRowsInsideAGroup_IsRead`; `AnUnresolvableReference_IsUnsupported_AndNeverThrows`; `ARecursiveFormat_IsUnsupported` (the Api sends `false`); `ANullableField_IsNotRequired`.
- [x] **Step 2–4:** fail, implement (never throw on a format; depth capped at 32, past it `Unsupported`), pass.
- [x] **Step 5: Commit** `Web (#1509): an input's widget is read from its format`; push.

### Task 9: The Create page draws a 31 package's inputs

**Files:** Create `src/Consultologist.Web/Shared/Create31/Input31.razor` (+ `.razor.css`) and `Shared/Create31/InputValue31.cs`; Modify `src/Consultologist.Web/Pages/Consults.razor` (`SyncInputFields` l.3101, the field loop l.195–633, `SuppliedInputs` l.3628, the macro panel l.132–147, `HasNoInputProblems`); Test `tests/Consultologist.Web.Tests/V31/ConsultsInputs31Tests.cs`, fixture `tests/Consultologist.Web.Tests/V31/Package31Fixtures.cs`.

**Interfaces:**
- Consumes: `InputShapes.Of` (Task 8); `MarkdownInput` (Task 10; until it lands, `Markdown` draws the `Area` widget).
- Produces: `Package31Fixtures.Package(params WorkflowPackageInputResponse[] inputs)` → a `WorkflowPackageResponse` at `SpecVersion` 31 with `Macros = []`, `Blocks` `node.plan`/`node.sections`, `Results` `deliverable.note`; `Package31Fixtures.Input(string id, string label, bool required, string formatJson, string? channel = null)`; `Package31Fixtures.Content(...)` → a content response whose `Resolve` holds the inputs' `description`. `InputValue31`: a mutable tree (`Scalar`, `Group`, `Rows`) with `JsonNode? ToJson()` (D3) and `string? Problem(InputShape, bool required)` ("Required." for a required blank, as the page says today). `Input31` parameters: `[Parameter] InputShape Shape`, `InputValue31 Value`, `string Label`, `string? Help`, `bool Required`, `EventCallback Changed`.

- [x] **Step 1: Failing bUnit tests.** `A31Package_DrawsItsInputsInWrittenOrder_WithLabelAndHelpText` (D4); `AString_IsASingleLineField`; `Plaintext_IsATextArea`; `AnEnum_ShowsBareValues`; `ALabelledChoice_ShowsTitles_AndSendsTheConst`; `ADate_IsADatePicker`; `ABoolean_StartsUnset_AndIsSentOnlyWhenYesOrNo` (three options; unset → the id absent from `Inputs`; yes → `true`); `ARequiredBooleanLeftUnset_KeepsStartDisabled` (D2); `ANumberAndAnInteger_AreNumberFields_AndSendNumbers`; `AnObject_IsAFieldGroup_LabelledByTitles_ToAnyDepth`; `ABlankNullableField_IsSentAsNull`; `ABlankFieldThatIsNotNullable_KeepsStartDisabled`; `AnArrayOfObjects_IsRepeatingRows`; `ABlankOptionalText_IsLeftOut_NeverSentAsEmptyText`; `A31Package_HasNoMacroPanel`; `AnOptionalBooleanFromAMigratedMacro_TravelsInInputs_AndMacroChoicesIsNull` (R4); `AChannel_ShowsItsChip_AndDoesWhatExpectedContentDid` (`transcript`, `image`, `ambient-note`, `form`: the existing chips and upload doors, driven by `ExpectedContent`, which the Api fills from the channel); `ATextInput_StillTakesADocument_AnEarlierRun_AndASnippet` (the existing slots, unchanged, on a 31 text input and an array of text); `AnUnsupportedFormat_SaysTheWorkflowCannotBeFilledInHere_AndDisablesStart`; `A30Package_IsDrawnExactlyAsBefore` (an existing 30 test still green, plus one asserting no `Create31` markup).
- [x] **Step 2: Run, fail.**
- [x] **Step 3: Implement.** For a 31 package, a field whose shape is `Line`, `Area`, `Markdown`, `Rows` of text or carries a channel keeps today's slot (documents, earlier runs, snippets, chips), choosing the control by widget; `Select`, `Date`, `Boolean`, `Number`, `Integer`, `Group` and `Rows` of data are `Input31`. `SuppliedInputs` at 31 takes each `Input31` value's `ToJson()` (D3) through the wire value the page already sends structured inputs with.
- [x] **Step 4: Run the Web suite and the solution suite; pass.**
- [x] **Step 5: Commit** `Web (#1509): the Create page draws a 31 package's inputs from their formats`; push.

### Task 10: The Markdown input (D5)

**Files:** Create `src/Consultologist.Web/Shared/Create31/MarkdownInput.razor` (+ `.razor.css`), `src/Consultologist.Web/wwwroot/js/markdownInput.js` (one function, `wrapSelection(element, before, after, linePrefix)` returning the new value and selection; registered as the page's other JS modules are); Modify `Consults.razor` (the `Markdown` branch of Task 9); Test `tests/Consultologist.Web.Tests/V31/MarkdownInputTests.cs`.

**Interfaces — Produces:** `[Parameter] string? Value`, `EventCallback<string?> ValueChanged`, `string Label`, `bool Required`, `int Rows = 10`.

- [x] **Step 1: Failing tests.** `TypingText_ReportsItAsMarkdown`; `TheToolbar_HasFiveLabelledButtons`; `Bold_AsksTheBrowserToWrapTheSelection` (JSInterop invocation with `**`, `**`); `Preview_RendersThroughMarkdownPreview_AndHidesTheTextArea`; `Preview_OfALeadingHash_ShowsAHeading` (part 02 § 8.1's warning made visible); `ADocument_CanStillBeAttached_ToAMarkdownInput` (through the page, Task 9's slot).
- [x] **Step 2–4:** fail, implement, pass. The preview is sanitised exactly as `MarkdownPreview` already is; no raw HTML passes that it does not pass today.
- [x] **Step 5: Commit** `Web (#1509): a Markdown input has a toolbar and a preview`; push.

### Task 11: The signature choice and the checks before submission

**Files:** Create `src/Consultologist.Web/Shared/Create31/StartChecks31.razor`; Modify `Consults.razor` (above the Start button l.702–706, `CreateConsultAsync` l.2269 and its three start calls, `LoadPackageBlocksAsync` l.2221 for a `Current` 400); Test `tests/Consultologist.Web.Tests/V31/ConsultsStartChecks31Tests.cs`.

**Interfaces — Consumes:** `GetStartChecksAsync` (Task 2), `SignatureBlocks.Parse` and `AccountService.GetSettingAsync(SignatureBlocks.SettingKey)`, `signatureBlockId` (Task 1).

- [x] **Step 1: Failing tests.** `EachRefusal_IsShownAboveStart_WordForWord_AndStartIsDisabled`; `NoRefusals_LeaveStartEnabled`; `TheProfileSentences_LinkToProfile`; `ARequiresRefusalFromCurrent_IsShownAboveStart_AndNoInputsAreDrawn` (the 400's `error`, e.g. S07-003's words); `TheSignatureSelect_IsShown_OnlyWithMoreThanOneBlock_AndAPackageThatReadsTheSignature` (four cases); `ItStartsOnTheChosenBlock`; `PickingAnother_SendsItsId_AndAsksTheChecksAgainWithIt`; `PickingTheChosenBlock_SendsNoSignatureBlockId`; `ThePick_NeverChangesTheAccountsSetting` (no `SaveSettingAsync` call); `OnWindowFocus_TheChecksAreAskedAgain`; `BeforeSubmission_TheChecksAreAskedAgain_AndARefusalStopsTheStart`; `APickWhoseBlockWasRemoved_FallsBackToTheChosenBlock`; `WhenTheChecksCannotBeReached_ItSaysA12004_AndStartIsDisabled`; `AStartTheEngineStillRefuses_ShowsTheEnginesSentence` (existing refusal path); `A30Package_AsksNoChecks`.
- [x] **Step 2–4:** fail, implement, run both suites. Window focus is one JS listener registered through the page's existing interop pattern and disposed with the page.
- [x] **Step 5: Commit** `Web (#1509): the Create page says the start's refusals before submission and offers the signature choice`; push.

### Task 12: Watching a 31 job on the Create page (D15)

**Files:** Create `src/Consultologist.Web/Services/Provenance/V31/RunRail31.cs`, `src/Consultologist.Web/Shared/Create31/RunView31.razor`; Modify `Consults.razor` (`HandleConsultGenerationJobEventAsync` l.2702: `row-finished`; the run view l.715–1234 branches to `RunView31` for a job whose `SchemaVersion >= 12`; the status text), `src/Consultologist.Web/Services/AI/AIEndpointService.cs` (the rows client, shared with Task 13: define it here if this task runs first); Test `tests/Consultologist.Web.Tests/V31/RunRail31Tests.cs`, `ConsultsRunView31Tests.cs`.

**Interfaces — Produces:**
- `IAIEndpointService`: `Task<IReadOnlyList<RecordRowRef>> GetJobRowsAsync(string jobId)`; `Task<RecordRowText?> GetJobRowAsync(string jobId, string key)` (null on 404); `Task<RecordDownload> DownloadJobRecordAsync(string jobId)`. `RecordRowRef(string Key, string Section, JsonElement Row)`; `RecordRowText(string Key, string Section, JsonElement Row, JsonElement? Text, bool TextDeleted)`; `RecordDownload(byte[] Bytes, string? RecordHash, string FileName)` (throws the service's exception with the body's text on 409 and 500).
- `RunRail31.Build(IReadOnlyList<WorkflowPackageBlockResponse> blocks, IReadOnlyCollection<RowFinished> finished, string? currentKey)` → `IReadOnlyList<RailBlock31>`; `RowFinished(string Key, string Section, string Status)`; `RailBlock31(string Key, string Name, string? Status, bool Current, IReadOnlyList<RailChild31> Children)`; `RailChild31(string Key, string Label, string Status)` where `Label` is the item path or pass as the key writes it (`2/1`, `#3`), 1-based.

- [x] **Step 1: Failing tests.** Pure: `ABlock_TakesItsOwnRowsStoredStatus`; `ItemAndPassRows_AppearUnderTheirTopLevelBlock_InKeyOrder`; `ARowOfAnotherBlockWithALongerSharedPrefix_IsNotMisfiled` (`node.plan` vs `node.planning`); `TheCurrentKey_MarksItsBlock`; `CheckAndDeliverableRows_AreNotRailBlocks`. bUnit: `ARowFinishedEvent_UpdatesTheRail_WithoutAFetch`; `TheStatusDelivering_ReadsDelivering…_AndTheDocumentIsShown`; `WaitingForAnUpdateToFinish_IsShownAsWritten`; `AReleasedDeliverable_ShowsItsText_FetchedByKeyOnce`; `ABlockedDeliverable_IsNamedBlocked_WithALinkToHistory_AndNoText`; `AFailedJob_ShowsItsErrorSentence`; `Reattaching_ToA31Job_BuildsTheRailFromTheRowsListing`; `A30Job_UsesTodaysRunView`.
- [x] **Step 2–4:** fail, implement, run both suites. Update `docs/CONSULT_GENERATION_EVENTS.md` (l.129, 140: the page handles `row-finished` for a 31 job).
- [x] **Step 5: Commit** `Web (#1509): the Create page follows a 31 job by its finished rows`; push.

## Part C: History at storage 12

### Task 13: The record, read (pure)

**Files:** Create `src/Consultologist.Web/Services/Provenance/V31/Record31.cs`, `RowKey31.cs`; Test `tests/Consultologist.Web.Tests/V31/Record31Tests.cs`, `RowKey31Tests.cs`, fixture `tests/Consultologist.Web.Tests/V31/Record31Fixtures.cs`.

**Interfaces — Consumes:** the rows client (Task 12). **Produces:**
```csharp
public sealed record RowKey31(string Root, string Path, IReadOnlyList<KeyStep31> Steps, string Raw) {
    public static RowKey31 Parse(string key);          // never throws; an unparseable key has Root "" and Path == Raw
    public string? ParentInstance { get; }              // the enclosing item's or pass's or call's row key, or null
}
public sealed record KeyStep31(string Scope, int? Pass, IReadOnlyList<string> Items);
public sealed class Row31 {                             // one stored row, read leniently
    public string Key { get; } public string Section { get; } public JsonElement Json { get; }
    public string? Text(string member); public bool? Flag(string member); public int? Int(string member);
    public IReadOnlyList<JsonElement> List(string member);   // [] when missing or not an array
    public JsonElement? Object(string member);
}
public sealed class Record31 {
    public static Record31 From(ConsultGenerationJobResponse header, IReadOnlyList<RecordRowRef> rows);
    public IReadOnlyList<Row31> Nodes { get; } public IReadOnlyList<Row31> Checks { get; }
    public IReadOnlyList<Row31> Deliverables { get; } public IReadOnlyList<Row31> Decisions { get; }
    public IReadOnlyList<Row31> DeliveriesOf(string deliverableKey);
    public IReadOnlyList<Row31> DecisionsOwnedBy(string ownerKey);
    public Row31? Find(string key);
}
```
`Record31Fixtures.Job()` builds the fixture **from the engine's own writers** where the Web test project can reference them (`RecordRows` in the library), else from part 08 § 14's example extended to: a `for` with two items (one `NotRun`), a `while` with two passes, a `call`, a `case` decision with result `"2"`, a skipped node with its decision, a node with decisions and no row, a macro row with `classifier: null`, a renderer chain in a template loop (`pass: [2]`), a check loop aligned with the `for`, a gate, and deliverables released, held, blocked, skipped and failed with delivery rows `sent`, `not sent` and `failed`. All text fictional.

- [x] **Step 1: Failing tests.** Keys: each example of part 08 § 2.1 parses to its root, scopes, items and passes; `ASubRowId_IsNotARowKey`; `ParentInstance` for `node.sections.paragraphs.draft_paragraph:2/1`, `node.refine.sections#2:3`, `node.hpi_section.standard_draft`. Record: `RowsAreSortedIntoSections_ByTheirSection`; `DecisionsOwnedBy_FindsThoseOfANodeWithNoRow`; `DeliveriesOf_AreInKeyOrder`; `AParsedRow_ToleratesMissingAndUnknownMembers` (Review Focus 2); `AThousandRows_AreReadInOnePass` (no per-row fetch: the builder takes only the listing).
- [x] **Step 2–4:** fail, implement, pass. **Step 5: Commit** `Web (#1509): a storage 12 record is read into its sections`; push.

### Task 14: History's sentences and banners (pure)

**Files:** Create `src/Consultologist.Web/Services/Provenance/V31/HistorySentences31.cs`, `Banners31.cs`, `JobLiveness31.cs`; Modify `tests/V31Source/V31Contract.cs` (`PagesAreaSentences`, the `A12-` branch), `docs/customizable-workflow/package-format-v31/refusal-sentences.md` (A12-001 to A12-005); Test `tests/Consultologist.Web.Tests/V31/HistorySentences31Tests.cs`, `Banners31Tests.cs`, `JobLiveness31Tests.cs`, `SentenceMirror31Tests.cs`.

**Interfaces — Produces:**
```csharp
public static class HistorySentences31 {
    public const string Delivering = "Delivering…";                                  // S09-019
    public const string RefusedHeading = "Refused, not delivered";                   // R1
    public const string Stopped = "…"; /* A12-001 */  public const string TextDeleted = "…"; /* A12-002 */
    public const string RunFactsDiffer = "…"; /* A12-003 */
    public static string Blocked(string label, string stateReason);                  // S06-003 / S06-037, from the row's "blocked: check '<id>' failed (<detail>)"
    public static string CouldNotSend(string destination);                           // S09-017
    public static string RunAgainLater(string error);                                // the stored error + " Run the workflow again later." for S09-010 to S09-012's stored halves (part 08's last bullet)
    public static string MigratedFrom(string oldVersion);                            // S09-004
    public static string NotSentToTheModel(IEnumerable<string> pointers);            // part 08: "Not sent to the model: <pointers>"
    public static string ComputedFromModelText(IEnumerable<string> reads);           // "computed from model text: node.draft"
}
public sealed record Banner31(string Kind, string Text, string? LinkKey);            // Kind: blocked | held | failed | not-sent | delivery-failed
public static class Banners31 { public static IReadOnlyList<Banner31> Of(Record31 record); }
public static class JobLiveness31 { public static bool IsStopped(string? status, DateTimeOffset? startedAtUtc, DateTimeOffset now); public const int StoppedAfterMinutes = 60; }
```
Banner rules (part 09 § 6.3): one per deliverable whose `state` is `blocked` (`Blocked(label, stateReason)`), `held` (`<label> was held: empty`, from the row's reason after `held: `) or `failed` (`<label> failed: <stateReason>`); one per delivery row whose `result` is `not sent` (`<label> was <reason>` — the row's reason begins `not sent: …`) or `failed` (`CouldNotSend`). Order: deliverable key, then delivery key. `LinkKey` is the row's key.

- [x] **Step 1: Failing tests.** One per banner rule with the spec's example words (*"Consultation note was blocked: check 'coverage' failed (10 of 12 terms present)."*; *"… could not run (…)."*; *"Couldn't send to email. Copy the note instead."*; *"… not sent: EMR isn't set up for this account"*); `AReleasedDeliverable_WhoseEveryDestinationWasSent_HasNoBanner`; `ASkippedDeliverable_HasNoBanner`; `ALabelIsNeverReplacedByAKey_UnlessTheRowHasNone`; liveness: terminal → false; no `StartedAtUtc` → false; `delivering` at 59 minutes → false, at 61 → true; `SentenceMirror31Tests`: each constant and each method's fixed words equal the row of the same id in `refusal-sentences.md` (read from the repo, located as the existing mirror tests locate repo files; a missing file fails).
- [x] **Step 2–4:** fail, implement, pass (Web and `--filter "FullyQualifiedName~V31Contract"`).
- [x] **Step 5: Commit** `Web (#1509): History's sentences and banners at storage 12`, `Docs (#1509): the pages' own sentences, A12-001 to A12-005`; push.

### Task 15: The node tree (pure)

**Files:** Create `src/Consultologist.Web/Services/Provenance/V31/NodeTree31.cs`; Test `tests/Consultologist.Web.Tests/V31/NodeTree31Tests.cs`.

**Interfaces — Produces:**
```csharp
public sealed record TreeNode31(
    string Key, string Title, string Kind, string Status, Row31? Row,
    IReadOnlyList<Row31> Decisions, IReadOnlyList<TreeNode31> Children);
public static class NodeTree31 {
    public static IReadOnlyList<TreeNode31> Build(Record31 record);                        // the Nodes section
    public static IReadOnlyList<TreeNode31> BuildChecks(Record31 record);                  // the Checks section
    public static string ItemTitle(Row31 row, string? retainedName);                        // the name while retained or kept in the row; else "<loop label> <position>" (*Sections 3*)
}
```
Rules: top-level rows in the order of their first `startedAtUtc`, never-started rows after, ties by key (ordinal); a `for` row's children are its item instances (rows whose `ParentInstance` is the loop at that item), grouped per item in numeric item order; a `while` row's children are its pass rows `#n` in order, each holding that pass's rows; a `call` row's children are the rows keyed under it; a decision is attached to the node whose key is its `owner`; an owner with no row becomes a node with `Row: null` and `Status` `"NotRun"` (R4), placed among its siblings by key; `Title` is the row's `label`, for an item `ItemTitle`.

- [x] **Step 1: Failing tests** on `Record31Fixtures.Job()`: `ALoop_ExpandsPerItem_InNumericOrder` (item 10 after 9); `AWhile_ExpandsPerPass`; `ACall_ExpandsToItsOwnRows`; `ANestedLoopsItems_AreUnderTheirOuterItem`; `ADecision_SitsOnItsOwner`; `ADecisionsOwnerWithNoRow_IsShownNotRun`; `AnItemThatDidNotRun_KeepsItsStatusAndReason`; `ItemTitle_UsesTheRowsNameWhenItHoldsOne_ThenTheRetainedName_ThenPosition`; `PositionsAreOneBased`; `CheckLoops_ExpandPerItem_AndCarryAlignedWith`.
- [x] **Step 2–4:** fail, implement, pass. **Step 5: Commit** `Web (#1509): the rows of a job as the tree History shows`; push.

### Task 16: History's page at storage 12: header, banners, state, download, verify

**Files:** Create `src/Consultologist.Web/Shared/History31/RecordView31.razor` (+ `.razor.css`), `Shared/History31/Header31.razor`, `Shared/History31/RowText31.cs` (a per-view cache: `Task<RecordRowText?> Get(string key)`, one fetch per key); Modify `src/Consultologist.Web/Pages/History.razor` (the status badge l.114–138, the detail panel l.233–969: for `SchemaVersion >= 12` it renders `RecordView31` in place of the provenance list's 5–30-only lines and the workflow node grid; `PollInFlightJobsAsync` l.1470–1539; Verify l.395–411; `RerunBlockedReason` l.1362); Test `tests/Consultologist.Web.Tests/V31/History31PageTests.cs`.

**Interfaces — Consumes:** Tasks 6 (`RecordCheck31`), 7, 12 (rows client), 13 to 15. **Produces:** `RecordView31` parameters `[Parameter] ConsultGenerationJobResponse Detail`, `Record31 Record`, `RowText31 Texts`, `WorkflowPackageContentResponse? Content`; it renders `Header31`, the banners, then the five sections' components in § 6.4's order (Tasks 17 to 20 add them; until then, an empty `<section>` with its heading).

- [x] **Step 1: Failing bUnit tests.** `A31Job_ShowsResolveHashAndLockHash_BesideThePackage`; `…_ShowsEveryLimit_WithItsName` (the ten of part 08 § 3); `AMigratedVersion_ShowsS09004_LinkingToTheEditor` (D10); `Banners_AreAtTheTop_EachLinkingToItsRow`; `TheFiveSections_AreInTheOrderTheJobRuns` (Inputs, Run facts, Nodes, Checks, Deliverables); `Delivering_IsShownAsDelivering…_AndPolledFast` (`FastPollInterval`, as `HistoryDuplicateRowsTests` drives it); `AStatusIsShownAsStored` (`Completed`, `Failed`, `Cancelled`, *Waiting for an update to finish*); `AStoppedJob_SaysA12001_IsNotPolledFast_AndOffersNoDownload` (D9; the page takes its clock from an injected `TimeProvider`, registered in `Program.cs` and the test context); `DownloadRecord_SavesTheRoutesBytes_UnderItsFileName` (the existing `consultologistDownload` interop); `…_IsOfferedOnlyForATerminalJob`; `ADownloadThatFails_ShowsTheRoutesSentence`; `Verify_ChecksTheRecordHash_AndReplaysEachDeliverable` (lines from `RecordCheck31`); `Verify_WhenTheRecordCannotBeFetched_SaysA12005`; `Rerun_IsOffered_WhenTheInputsAreHeld` (Task 4's `HeldInputs`); `TheRowsAreListedOnce_PerLoad`; `A30Job_RendersExactlyAsBefore` (an existing detail test unchanged, plus one asserting no `History31` markup); `AJobWhoseRowsCannotBeListed_ShowsTheHeader_AndTheRoutesSentence`.
- [x] **Step 2–4:** fail, implement, run both suites.
- [x] **Step 5: Commit** `Web (#1509): History opens a storage 12 job: its header, banners, state and record`; push.

### Task 17: Inputs and Run facts

**Files:** Create `Shared/History31/Inputs31.razor`, `Shared/History31/RunFacts31.razor`; Modify `RecordView31.razor`, and `src/Consultologist.Web/Services/AI/AIEndpointService.cs` if the job response does not yet mirror the header's `inputs` (add trailing `JsonElement? Inputs31` only if the Api response carries them; if the Api's `ToResponse` does not, add the header's `inputs` to it as a trailing member named as the Api names its 31 members, with a test in `tests/V31Engine/RecordRoutesTests.cs`); Test `tests/Consultologist.Web.Tests/V31/History31InputsTests.cs`.

- [x] **Step 1: Failing tests.** `EachInput_ShowsItsOrigin_AndHash`; `ADocument_ShowsItsKind_Extractor_Parser_Converter_AndOcr` (`kind` `image` and `ambient-note` shown although `origin` is `document`, R7; a `null` step is not listed); `AnInputOfSeveralElements_ListsThem_NumberedFromOne`; `APreviousRunElement_LinksToItsJob_AndSaysItCameFromAModel` (D7); `RunFacts_ShowEachFactRead_WithTheZoneBesideTheDate` (`run.date: 2026-11-16`, `America/Toronto`); `NoFactsRead_SaysSo`; `EachRunSpot_IsListedUnderTheFactItReads_WithItsBodyKey_Site_AndOutput` (gathered from every row's `runSpots` and macro rows' `runSpots`); `ASpotWhoseOutputIsNotInTheRow_ShowsItWhileRetained_ThenItsHash`; `ABodyWhoseTwoHashesDiffer_SaysA12003`.
- [x] **Step 2–4:** fail, implement, pass. **Step 5: Commit** `Web (#1509): History's Inputs and Run facts at storage 12`; push.

### Task 18: Nodes

**Files:** Create `Shared/History31/Nodes31.razor`, `Shared/History31/NodeRow31.razor`, `Shared/History31/Decision31.razor`, `Shared/History31/SubRows31.razor` (renders, macros, units, run spots); Modify `RecordView31.razor`; Test `tests/Consultologist.Web.Tests/V31/History31NodesTests.cs`.

**Interfaces — Consumes:** `NodeTree31`, `RowText31`, `HistorySentences31`. Every row's element has `id="row-<key>"`; a link to a row is `#row-<key>` and opens its ancestors' disclosures.

- [x] **Step 1: Failing tests.** `EachNode_IsARow_ByItsKeyWithRoot`; `ALoop_ExpandsPerItem_NamedByItsLabel_AndByPositionAfterTheDrop`; `AWhile_ExpandsPerPass`; `ANotRunItem_ShowsItsStatusAndReason`; `ADecision_ShowsThePrintedCondition_ItsResult_AndTheSkipSentence` (*skipped: input.kind.value = "table" is false*, the row's `stateReason` verbatim); `ACaseDecision_ShowsTheArmTaken_AsStored` (`2`, `else`; R8); `ARunDependentDecision_IsMarked_AndWhatItChoseIsNot`; `DecisionsOfANodeThatNeverRan_AreUnderIt_AndItSaysNotRun` (R4); `RendererSubRows_AreUnderTheirNode_ById_WithRenderer_InputHash_OutputHash_AndPass` (a chain shows two; a gap in numbering is kept); `AMacroRow_ShowsOfferedAndPlaced_LinksItsDecisions_AndItsContentHash`; `AMacroWithNoClassifier_SaysNoSingleClassifier` (R4); `AMacroWithAClassifier_LinksToThatRow`; `AnInvalidOutcome_IsOnTheNodesRow`; `AnAgentRow_NamesItsDefinitionAndVersion_AndTheBinding`; `KeywordsRemoved_AreListed_AsNotSentToTheModel`; `AUnitNotSent_SaysSubstitutedNotSent`; `AModelDerivedComputedRow_SaysComputedFromModelText`; `Retries_ShowHowMany_OfWhichKind_AndTheTotalWait`; `ACall_IsOneRowThatExpands_AndNamesItsFunction`; `OpeningARow_FetchesItsTextOnce`; `NoTextIsFetchedBeforeARowIsOpened` (Review Focus 3); `ARowWhoseTextWasDeleted_ShowsItsHashes_AndA12002` (Review Focus 1); `AFailedNode_ShowsItsStoredReason_AndRunAgainLater_ForTheModelServicesLimits`.
- [x] **Step 2–4:** fail, implement, pass. **Step 5: Commit** `Web (#1509): History's Nodes at storage 12`; push.

### Task 19: Checks

**Files:** Create `Shared/History31/Checks31.razor`, `Shared/History31/CheckRow31.razor`; Modify `RecordView31.razor`; Test `tests/Consultologist.Web.Tests/V31/History31ChecksTests.cs`.

- [x] **Step 1: Failing tests.** `ACheckRow_HasFourLines_Checks_Reads_Result_Effect`; `Checks_NamesWhatItRanOn_WithThatOutputsHash_AndLinksToIt`; `Reads_LinkEachToItsRow`; `Result_ShowsPassOrFail_TheCounts_AndFailWith` (`7 of 9`, the `failWith` sentence when failed); `UncoveredAndUntested_AreShownWhileRetained_ThenTheirHashAndSize`; `Effect_IsTheStoredWord` (`released`, `blocked deliverable.note; released deliverable.summary`, `read by node.x`; `null` shows nothing); `ACheckLoop_ExpandsPerItem_BesideItsProductionItem_WithTheSameName` (links to `alignedWith`); `AnAggregate_ShowsItsFailWithSentence`; `ASkippedCheck_ShowsItsReason`; `AGatesRow_ComesAfterItsDeliverables_InTheOrderRecorded` (R8: no re-sorting of checks by key).
- [x] **Step 2–4:** fail, implement, pass. **Step 5: Commit** `Web (#1509): History's Checks at storage 12`; push.

### Task 20: Deliverables, and the text a check refused (R1)

**Files:** Create `Shared/History31/Deliverables31.razor`, `Shared/History31/DeliverableRow31.razor`, `Shared/History31/RefusedText31.razor` (+ `.razor.css`), `src/Consultologist.Web/Services/Provenance/V31/TemplateReads31.cs`; Modify `RecordView31.razor`; Test `tests/Consultologist.Web.Tests/V31/History31DeliverablesTests.cs`, `TemplateReads31Tests.cs`.

**Interfaces — Produces:** `TemplateReads31.Group(Row31 deliverable)` → `IReadOnlyList<SiteReads31>`; `SiteReads31(int Site, string At, string Expr, string? Loop, IReadOnlyList<Substitution31> Passes)`; `Substitution31(IReadOnlyList<int>? Pass, string Reads, string Hash, int Start, int Size, string? Renders)`. The template's text is read from `Content.Resolve` at the deliverable's entry (the member part 07 § 2.2 names for a body; find it in a compiled golden under `tests/`), and shown only when its hash equals the row's `templateHash`; otherwise the row says the pinned version's template could not be read.

- [x] **Step 1: Failing tests.** `ADeliverable_ShowsItsStoredState_Format_AndComposedHash`; `ItsTemplate_IsShownFromThePinnedVersion_WhenTheHashMatches`; `…_AndNotWhenItDoesNot`; `Reads_AreGroupedBySite_InSourceOrder`; `ALoopedSite_IsCollapsedToTimesN_AndExpandsPerPass` ("× 3 passes"); `EachRead_ShowsItsSizeAndHash_AndItsTextWhileRetained` (the slice `Start`..`Size` in runes of the composed text); `AProfileRead_ShowsTheFieldAndItsHash`; `TheSignatureRead_NamesTheBlocksId` (the header's `signatureBlockId` and `signatureChosen`); `TheCheckLine_ReadsCheckIdPassedOrFailed_AndLinksToTheChecksRow`; `AReleasedDeliverableWhoseCheckDidNotApply_ShowsItsStoredReason`; `EachDestination_ShowsItsStoredResult_ItsReason_AndItsAttempts`; `AFailedDestination_SaysCouldNotSend`; `ABundle_ListsWhatItLeftOut_AndWhy` (the folder's other deliverables that are not `released`, each with its state and reason); `AReleasedDeliverable_HasACopyControl`; **`ABlockedDeliverablesText_IsUnderRefusedNotDelivered_WithItsCheckAndSentence_AndHasNoCopyControl`**; `…_IsNotInAnyCopyOrDownloadThePageOffers`; `AfterTheDrop_TheHeadingAndTheHashRemain_WithA12002`; `ThereIsNoReleaseButton`.
- [x] **Step 2–4:** fail, implement, pass. Correct part 09 § 6.4 "Deliverables" to name the heading (R1).
- [x] **Step 5: Commit** `Web (#1509): History's Deliverables at storage 12, with refused text marked as refused`; push.

## Part D: Profile and the Editor

### Task 21: The "Letter details" card (D13)

**Files:** Create `src/Consultologist.Web/Shared/Profile/LetterDetailsCard.razor`, `src/Consultologist.Web/Services/Accounts/LetterDetails.cs` (the three keys and limits: `profile.credentials` 120, `profile.organisation` 200, `profile.closing` 4,000, mirrored from `AccountSettingKeys` and `Account.cs` l.546–548 and pinned by a mirror test); Modify `src/Consultologist.Web/Pages/Profile.razor` (under "Workflow preferences", after the signature card, l.884); Test `tests/Consultologist.Web.Tests/LetterDetailsProfileTests.cs`, `ProfileSectioningTests.cs`.

- [x] **Step 1: Failing tests.** `TheCard_HasCredentials_Organisation_AndClosingLine_WithTheirControls` (two single-line fields, one text area); `ItLoadsWhatIsSaved`; `Saving_WritesEachChangedField_Trimmed_AsTextPlain`; `SavingABlankOrWhitespaceValue_DeletesTheSetting`; `EachField_StopsAtItsLimit` (`maxlength` 120, 200, 4000) `_AndShowsTheEnginesRefusalIfOneArrives`; `AnUnsetField_ReadsNotSet`; `TheLimits_MirrorTheApis`.
- [x] **Step 2–4:** fail, implement, pass. **Step 5: Commit** `Web (#1509): Profile has a Letter details card`; push.

### Task 22: The account time zone (D12)

**Files:** Create `src/Consultologist.Web/Shared/Profile/TimeZoneCard.razor`, `src/Consultologist.Web/Services/Accounts/TimeZones.cs` (`IReadOnlyList<string> Iana()`; `const string SettingKey = "account.timeZone"`), a one-line function in the page's existing JS file for the browser's zone; Modify `Profile.razor` (under "Account", first card); Test `tests/Consultologist.Web.Tests/TimeZoneProfileTests.cs`.

- [x] **Step 1: Failing tests.** `TheSelect_StartsEmpty_WhenNoZoneIsSet` (and nothing is saved on load); `ItListsIanaNames_InOrder`; `PickingAZone_SavesIt`; `UseThisBrowsersZone_FillsTheSelect_OnlyWhenClicked` (no interop call on render); `…_AndSavesNothingUntilSaveIsPressed`; `ABrowserZoneTheListDoesNotHold_IsNotSelected_AndSaysSo`; `ClearingTheSelect_DeletesTheSetting`; `UntilItIsSet_TheCardSaysS06008`; `TheEnginesRefusalOfAZone_IsShown`.
- [x] **Step 2–4:** fail, implement, pass. If `TimeZoneInfo.GetSystemTimeZones()` is empty in the published WASM app (checked in Task 25's E2E), take the list from `Intl.supportedValuesOf('timeZone')` instead and record it.
- [x] **Step 5: Commit** `Web (#1509): Profile sets the account's time zone`; push.

### Task 23: The Editor opens a 31 package read-only (D14)

**Files:** Create `src/Consultologist.Web/Shared/WorkflowEditor/V31/V31PackageView.razor` (+ `.razor.css`), `src/Consultologist.Web/Services/Workflow/V31/PackageDownload31.cs`; Modify `src/Consultologist.Web/Pages/Templates.razor` (`.editor-bar` toolbar ~l.84–250, the notices stack ~l.255–330, `.editor-layout` l.345, `DownloadPackageAsync` l.20211, `ImportPackageAsync` l.20237 and `ApplyImportedPackageAsync`, `LoadedSpecVersion` l.5413); Test `tests/Consultologist.Web.Tests/V31/TemplatesV31Tests.cs`, `EditorFixtures.cs` (`V31()`: a small fictional package's source files, with `Resolve`, `Lock`, `PublishReport`; `V31Migrated()` adds `MigratedFrom` and `MigrationReport`; `V31Imported()` has no evidence and one notice).

**Interfaces — Produces:** `V31PackageView` parameters `[Parameter] WorkflowPackageContentResponse Content`, `bool Owned`; left, a file list (`manifest.json` first, then `Files` by path, ordinal; then `resolve.json`, `lock.json` when present and `Owned`; then "Diagram" when `Files` holds `dag.mmd`); right, the selected file's text in a read-only `<pre>` (JSON pretty-printed for the three evidence views, source files as stored). `PackageDownload31.Entries(WorkflowPackageContentResponse)` → `IReadOnlyList<(string Path, byte[] Bytes)>`: `manifest.json` (`Manifest.GetRawText()`), every file, and `resolve.json`, `lock.json`, `publish-report.json` from their members' raw text when present.

- [x] **Step 1: Failing tests.** `A31Package_ShowsTheBar` (*"This package uses the new format. You can view and download it; editing returns in a later release."*, word for word); `ItHasNoPublish_Upgrade_Undo_Discard_OrNewPackageControls`; `EverySourceFile_IsListed_AndOpensReadOnly` (no `textarea`, no input that is not the picker or the import); `TheManifest_IsShownAsPublished_NullsKept`; `ResolveAndLock_AreViewable_ForAVersionTheAccountOwns_AndNotOtherwise`; `TheDiagramEntry_AppearsOnlyWithADagFile` (area 13's seam); `Download_Includes_ResolveLockAndThePublishReport` (the zip's entry names, through the fixture's interop capture as `TemplatesDownloadTests` does); `ImportingA31Zip_OpensItReadOnly_WithItsNotices` (S07-016 shown); `AnImportThe31SourceCheckRefuses_ShowsEveryError`; `A30Package_StillEdits` (an existing test unchanged); `ANewPackage_IsCreatedAt30` (`NewestSpecVersion` is 30).
- [x] **Step 2–4:** fail, implement, run both suites. `ProjectLoadedContent` and every 5–30 reader are skipped for a 31 content (they read a 30 manifest); nothing reads a 31 manifest with `WorkflowManifestReader`.
- [x] **Step 5: Commit** `Web (#1509): the Editor opens a 31 package read-only`; push.

### Task 24: The publish report and the migration report in the Editor (D10, D11)

**Files:** Create `Shared/WorkflowEditor/V31/PublishReport31.razor`, `Shared/WorkflowEditor/V31/MigrationReport31.razor`; Modify `V31PackageView.razor` (two entries, "Publish report" and, when migrated, "Migration"), `Templates.razor` (`PublishAsync` l.18564: a response's `Report`, and a 400's `Error` and `Report`, are shown with `PublishReport31` beside today's warnings and errors); Test `tests/Consultologist.Web.Tests/V31/EditorReports31Tests.cs`.

- [x] **Step 1: Failing tests.** `ThePublishReport_ShowsItsOutcome_Time_AndPublishedBy`; `…_ListsWarningsAndNotes_EachWithWhereItIs` (`sentence`, `at`); `…_ListsEachTest_PassOrFail_WithItsFirstMismatch` (hashes or plain words as the report holds them, nothing else); `…_ListsWhatWasNormalised`; `ARefusedReport_ListsItsRefusals`; `AMigratedVersion_SaysMigratedByTheOperatorFromItsOldVersion` (S09-004); `TheMigrationView_ListsHandEdits_Notices_AndTheCounts`; `…_ListsEachComparedText_EqualExpectedOrUnexplained_WithItsExplanation` (sites and hashes only); `AVersionThatWasNotMigrated_HasNoMigrationEntry`; `APublishAnswerCarryingAReport_ShowsIt`; `AReportWithAMemberMissing_StillRenders`.
- [x] **Step 2–4:** fail, implement, pass. **Step 5: Commit** `Web (#1509): the Editor shows a 31 version's publish and migration reports`; push.

## Part E: the browser suite and the documents

### Task 25: The browser suite

**Files:** Modify `tests/Consultologist.Web.E2E.Tests/ApiMock.cs` (fulfil, when a test asks for a 31 scenario: `**/WorkflowPackages/Current` at 31, `**/WorkflowPackages/Current/StartChecks*`, `**/WorkflowPackages/Current/Content*`, `**/Account/Settings/*`, `**/ConsultGenerationJobs/*/Rows*`, `**/ConsultGenerationJobs/*/Record`; the default scenario is unchanged); Create `tests/Consultologist.Web.E2E.Tests/Create31E2ETests.cs`, `History31E2ETests.cs`, `Profile31E2ETests.cs`, `Fixtures31.cs` (fictional JSON bodies).

- [ ] **Step 1: Write the tests.** `Create31_ABooleanStartsUnset_AndALabelledChoiceShowsTitles`; `Create31_ARefusalDisablesStart_AndReadsWordForWord`; `Create31_RemovingARow_LeavesNoEmptyRowBehind` (AGENTS.md's label trap, on `Input31`'s rows); `History31_ABlockedDeliverable_IsBanneredAndMarkedRefused_WithNoCopyButton` (also at a 390-pixel-wide viewport); `History31_OpeningARow_FetchesItsTextOnce` (count the route's hits); `Profile31_TheZoneList_IsNotEmpty_AndTheBrowserZoneButtonFillsIt`; `Editor31_OpensReadOnly_WithTheBar`.
- [ ] **Step 2: Run** `.github/scripts/run-e2e.sh` from the worktree (read its output through `| tail -60`; never redirect with `>`). A failure naming the browser itself (*"Executable doesn't exist"*, a missing shared library) is the server's setup: record a block as the runbook says. Any other failure is fixed in the task that owns the code.
- [ ] **Step 3: Commit** `Tests (#1509): the browser suite covers the 31 pages`; push.

### Task 26: The documents, the contract's tables and the self-check

**Files:** Modify `docs/customizable-workflow/engine-v31.md` (a section "The pages (area 12, #1509)": the start-checks route, references at a start, rerun, the record's container, what History fetches and when), `docs/customizable-workflow/package-format-library.md` ("What it leaves to areas 12 and 13" → area 12 built; `RunInputs.ModelDerivedInputs`), `docs/CONSULT_GENERATION_EVENTS.md`, `docs/README.md` (index, if a new doc was added), `docs/customizable-workflow/package-format-v31/09-engine-and-pages.md` and `08-record.md` (each correction a task made, checked in one read), `refusal-sentences.md`; in `~/Projects/consultologist-package-format` (`m39-build`): `v31/conformance/README.md` (a "The pages (area 12)" table of A12-001 to the last id; l.880's row now says History builds S06-001 to S06-007, S06-033, S06-034, S06-037 and S06-038), then `python3 scripts/check-v31.py`, push, and move the pin under `external/`.

- [ ] **Step 1: Write the documents.** No new claim that is not in the code: each paragraph names the class or route it describes.
- [ ] **Step 2: Self-check, by command.** `git diff --stat origin/m39-build -- .github .claude scripts/m39-build AGENTS.md CLAUDE.md docs/customizable-workflow/package-format-v31/BUILD-RUNBOOK.md` is empty; `grep -rn "AcceptedSpecVersions\|SupportedSpecVersions" src | grep 31` shows no 31 added; `grep -rn "NewestSpecVersion = " src/Consultologist.Web` is 30; every sentence id in the *Area sentences* table is in `refusal-sentences.md`, `V31Contract.cs` and the conformance README; every checkbox above is ticked; *Recorded in the build* has a line for every choice a task took.
- [ ] **Step 3: Run** `dotnet build Consultologist.sln` and `dotnet test Consultologist.sln --filter "FullyQualifiedName!~Consultologist.Web.E2E.Tests"`; pass.
- [ ] **Step 4: Commit** `Docs (#1509): the pages at specVersion 31`; push both repos.

## Part F: the fix-first group (#1508's rulings, items 1 to 9)

**Read first:** the owner's two comments of 2026-11-26: the rulings,
[#1508 comment 7199237966](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7199237966)
(`gh api repos/Consultologist/Consultologist-Blazor/issues/comments/7199237966 --jq .body`; items 1 to 9 are built here,
10 and 11 need no change), and how they are built here,
comment 7199238400 (`gh api repos/Consultologist/Consultologist-Blazor/issues/comments/7199238400 --jq .body`).
They cite PR #1535's *For the owner to rule or see* (§ 1: PR #1532's unexpected differences 1, 2 and 4; § 3: the
edge-case sites; § 4: items 4.3 and 4.13) and R15 (area 11's plan, `plans/area-11-registry-and-publish.md`, Part F's
decisions table). **The principle:** 31 stays strict; the migration writes the explicit thing that gives 30's output;
new packages are told at publish. Nothing here changes Create or History, and no Part A to E task waits on it.

**Constraints special to this part** (the Global Constraints above hold too):

- **Code map** (under `src/Consultologist.PackageFormat/`, `PF` = `…/V31`): `PF/Migration/` (`Migrate.Nodes.cs`,
  `Migrate.Flows.cs`, `Migrate.Deliverables.cs`, `Migrated.cs`, `ConditionTyping.cs`, `MigrationSentences.cs`,
  `TemplateRewriter.cs`), `WorkflowPackageMigration30To31.cs` (`Migration31Options`, `WithSnapshot`), `PF/Harness/`
  (`ExpectedDifferences.cs`, `ByteEquivalence.cs`, `MigrationReport.cs`), `PF/Publish/PackagePublish.cs`; the Api's
  `src/Consultologist.Api/V31/` (`V31PackagePublisher.cs`, `MigratePackage.cs`, `OutstandingHandEdits.cs`,
  `EngineLimitSettings.cs`). Tests `tests/V31Migration/`, `tests/V31Publish/`, `tests/V31Engine/`; fixtures
  `tests/Fixtures/ByteEquivalence/`. Line numbers are as of `821c9139`.
- **Goldens are regenerated once, in F10 only.** From F1 to F9 these golden tests are expected red and are left red:
  `MigratedPackagesTests.TheMigratedSource_IsTheGolden`, `ByteEquivalenceTests.TheReport_IsTheGolden`,
  `HandEditsTests.TheReport_IsTheGolden`, `ConditionConverterTests.TheGolden_IsWhatTheConverterGives`,
  `ConditionEquivalenceTests.TheGolden_IsWhatTheRunGives`, `MigrationVectorsTests.ThePinnedVectors_AreWhatMigrateGives`.
  Every other test stays green after every task. Each task's report names which of these six it turned red. No task
  before F10 sets `GOLDEN_UPDATE_V31` or `CONFORMANCE_EXPORT_V31`.
- **Hand-maintained fixtures are not goldens** and are updated in the task whose change moves them:
  `tests/Fixtures/ByteEquivalence/hand-edits/**` (whole files laid over `migrated/` by `MigrationFixture.Edited`) and
  `tests/Fixtures/ByteEquivalence/edge-tests/*.json` (the edge packages' pinned tests, whose `expect` holds 31's text where
  it differs from 30's). Area 01's `vendored/`, `fixture-packages/`, `scenarios/`, `captures/` and
  `conditions/expressions.json`, `conditions/vectors.json` never change.
- **Commands.** Focused, while a test is written and made to pass:
  `dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~V31Migration.<TestClass>"`.
  Before each commit, the V31 suite less the six golden tests (call this **the V31 run**):
  `dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~V31&FullyQualifiedName!~TheMigratedSource_IsTheGolden&FullyQualifiedName!~TheReport_IsTheGolden&FullyQualifiedName!~TheGolden_IsWhat&FullyQualifiedName!~ThePinnedVectors_AreWhatMigrateGives"`.
  F9 and F10 also run `dotnet build Consultologist.sln` and
  `dotnet test Consultologist.sln --filter "FullyQualifiedName!~Consultologist.Web.E2E.Tests"`.
- **31 stays off in production:** nothing here touches `WorkflowPackageValidator.AcceptedSpecVersions`,
  `WorkflowPackageStore.SupportedSpecVersions` or `SpecVersionAdmission`; every Api path changed (F9) is already behind it.
- **Spec text is corrected in the task that changes behaviour:** part 10 (`10-migration-and-tools.md` §§ 1.2, 1.4, 2.2,
  3.2), part 03 § 4.2 where F8 says, and the content repo's twin chapters (`~/Projects/consultologist-package-format`,
  branch `m39-build`: `package-format-v31.md` chapter 10, §§ 10.1, 10.9, 10.11, and chapter 3 § 3.4.2 for F8). **A10
  sentences live in three places:** `PF/Migration/MigrationSentences.cs`, `tests/V31Source/V31Contract.cs`
  (`MigrationAreaSentences`, l.255) and the content repo's `v31/conformance/README.md` (the A10 table, rows l.1047 to
  l.1067). `refusal-sentences.md` holds no A10 row (only S10-002, l.457), so none is added there. New ids, in build order:
  **A10-085** (F6), **A10-086** (F8), **A10-087** (F1); a task that needs another takes the next.
- **The content repo:** each task that edits it commits on `m39-build` (subject `Format spec (Consultologist-Blazor#1509): …`),
  runs `python3 scripts/check-v31.py` there, and pushes; the pin `external/consultologist-package-format` moves **once**, in
  F10, after the vectors are re-exported (F-D18).
- **Commits:** `PackageFormat (#1509): …`, `Api (#1509): …`, `Tests (#1509): …`, `Docs (#1509): …`, each ending as the Global
  Constraints say. Every commit body that touches a hand-maintained fixture names the file.

### Decisions Part F takes

| # | Where | Choice | Why |
|---|---|---|---|
| F-D1 | item 1, "gives each such node or loop the `if`" | The `if` is written on **each leaf** (agent, rendered, computed, call) of the node's subtree: a leaf itself, or every leaf in a `for` or an `if`/`case` flow's bodies, joined by `and` after a leaf's own `if` (null for every 30 leaf). No wrapping flow is written. | A 31 `for` has no `if` member (part 04 § 2.1: exactly sixteen members); a wrapping `if` flow would move every rooted key under it (`Migrated.Keys`, the comparison's `KeyMap`) and the hand-edit files laid over by path (`hand-edits/example-structured-intake/nodes/sections_senior_review_section/…`). Every 31 leaf has `if` (`SourceShapes.NodeAgent` … `NodeCall`). |
| F-D2 | item 1, "only an unreleased deliverable needs" | A top-level 30 node `T` (not a check, not a classifier) is gated when it lies outside **K** and some deliverable with a converted 30 condition reaches it. K = what 30 ran whatever the deliverables: the closure (30's edges, `WorkflowNodeClosure.Reachable`) of every classifier (format `classification`), of every node a deliverable's condition reads, of every top-level `if`/`case` flow's decision reads (`m.Walk30.EdgesOf(flow)`), and of the roots of every deliverable with no condition or a condition the converter refused. A deliverable's roots are its `sources`, the node reads of its placed macros (`m.Walk30.MacroEdges`) and its check's node, as `ConsultGenerationJobStarter.DecideFireSet` (l.1332–1540) roots them. The gate is the reaching deliverables' converted conditions, in 30's deliverable order, one alone or joined by `or`. | It is 30's prune (`DecideFireSet`: only the firing deliverables' closure runs, classifiers and gating reads always), stated statically; a deliverable whose condition is a hand edit gates nothing, since nothing is joined to a condition not yet written (as R15 does, `WhenASourceRan`). |
| F-D3 | item 1 | A subtree that holds a `while` is not gated. | A gated body would leave a `while`'s condition reading skipped nodes and running passes to `max`; no fixture has the case (the 45 prompts are all `for` bodies). Recorded for the PR. |
| F-D4 | item 1 and R15 | `ConversionRead.AlwaysValued` (what the checker and the template rewriter see, so R17's guards are written) counts a gated leaf as not always valued; R15's `WhenASourceRan` judges a source by **30's** always-valued, ignoring gates (`Migrated.AlwaysValued30`). | A gated source runs whenever its deliverable applies (the deliverable is among those whose `or` gates it), so R15's `.state` term would be redundant; the checker, which sees the leaf's `if`, must still see the leaf as nullable or A11-009 refuses unguarded member reads of it. |
| F-D5 | item 1 | The `<macro>_applies` classifier nodes R13 writes (`Migrate.Nodes.ChoiceClassifiers`) are not gated. | Their prompt is 31's alone and already credited by `macro-choice-classifier`; gating it is no 30 output. |
| F-D6 | item 1 | A notice per gated leaf file, A10-087: *"\<file\>, if: written so that the node runs only when deliverable \<ids, joined by " or "\> applies, as 30 ran only what an applicable deliverable needed."* | Every `if` the migration invents is said (A10-079's precedent). |
| F-D7 | item 2 | Only an input whose own 31 format is `"date"`, read whole as a template output (`{{ input.x }}` → `{{ input.x.value \| date.to_string "%d %b %Y" }}`), in every template the rewriter writes (prompts, rendered nodes, parts, snippets, macro bodies, deliverables). A read already piped, a read inside an `if` test or an expression, and a date field of an object input are left as today. No notice. | The ruling's words, "a date input written whole"; the fixtures' differences are exactly these reads (`example-conditional-documents`' section prompts, `example-v12-constructs`' `patient_header`). |
| F-D8 | item 3 | `\| string.strip` is written on **each source piece** of a deliverable's composition (`{{ node.<id>.content \| string.strip }}`), never on a snippet piece (a 30 macro placed bare) and never on the whole. | 30's composer trimmed each source at both ends (`awkward-texts`: `closing`'s reply `"\n\nClosing line…\n\n\n"` composed trimmed, the empty `opening` still taking its separator); a macro body keeps R5's rule (`macro-trailing-whitespace`), and item 5 keeps the empty-read trailing space as an expected rule, which stripping a snippet would erase. See **Blocks** for a loop's items. |
| F-D9 | items 4, 5 | Three new **bytes** rules in `ExpectedDifferences.Rules`, general (no package named): `loop-item-not-run` (item 4), `chosen-macro-final-line-ending` and `empty-read-trailing-space` (item 5). | The ruling makes them expected differences; a rule keyed to the edge packages would explain nothing in an account package's report. |
| F-D10 | item 6 | The trim is part 03 § 5.2's `trim` (the only trimming operation JSON Logic has at 31; `string.strip` is a template built-in), written on **every text read rooted at an input** inside the compared operand: `{ "in": [ { "trim": [ <read> ] }, ["lit"] ] }`, the trim wrapped around the read itself where the operand is an operation over it (`lower(trim(input.x))`). A literal whose first character is white space is a hand edit (A10-085). | 30 trimmed every text scalar of an input at its end (`ConsultGenerationJobStarter.NormalizeValue`, `CanonicalText.Normalize` = `TrimEnd`), fields and elements too; a node's text was not trimmed, so a node read gets no `trim`. `trim` also drops **leading** white space, which 30 kept: an input with leading white space now matches where 30's did not. No 31 operation trims one end only, so that remainder is said in A10-074 and listed for the owner; a literal that starts with white space could match at 30 and never matches a trimmed text, so it is the "no trim fits" case S10-002 names. |
| F-D11 | item 7 | A10-064 keeps its id with corrected words; the dropped line is credited under the existing rule `sent-format` (widened), not a new rule. | One difference (30's answer line against 31's format instruction) that `sent-format`'s `AnswersSent` already isolates; no new golden-hit obligation. |
| F-D12 | item 8, "where `state` is not readable" | The term for each source is chosen by **asking the deliverable-`if` typing**: `<read>.state === "completed"` when it types there, else `<read>.value !== null`, else none (the source is then one "the migration does not read"). The migration's typing scope (`ConditionTyping.Scope`) learns `.state` as `ScopeTree.Member` types it (l.316). Part 03 § 4.2's deliverable-`if` row (and the twin § 3.4.2) is corrected to list `.state`. | `ExpressionChecker.Rule` (l.217–241) refuses only `.content` and `check.*` at a deliverable `if`, so `.state` is readable for every visible node, as area 11 already writes it; part 03's row says `.value` only. The fallback is built and unit-tested through its seam but no source the migration writes today reaches it (said in the PR). |
| F-D13 | item 8, "a hand edit (S10-002)" | A hand edit on the deliverable's `if` when at least one source has no term (not in `Migrated.NodeReads`, or read where neither term types, as a `check.` read) **and** no source is known to always run (30's always-valued, or a `while`); its reason A10-086 names the unread sources and, for the others, the term R15 would join. Otherwise as today. | "So nothing is released empty without a notice": a deliverable with a source that always runs is never empty. |
| F-D14 | item 9 | `PublishOptions` and `Migration31Options` gain a trailing `RunLimits? RunLimits = null` (null: the spec's starting values, as now). The publisher, the bulk publish and the weekly summary pass `EngineLimitSettings.Limits`/`.PublishLimits`. The two CI scripts and `scripts/migrate-workflow-package.cs` keep the starting values. | The three places the ruling names; the scripts run with no deployment (`registry-operations.md` l.405–410 says so). |
| F-D15 | goldens | Golden tests are red from F1 to F9 (the six named above). | The ruling: regenerate once, after all nine. |
| F-D16 | commits | Subjects carry `#1509` (this area's issue), though the rulings are on #1508. | The second comment assigns the work to this area's branch. |
| F-D17 | edge pinned tests (F4, F5) | `EdgeCasePackagesTests.TheMigratedPackage_PublishesWithItsPinnedTest_AndGives30sOutput` decides by the comparison's `Equal` (a credited but unequal site expects 31's text), no longer by `Unruled`; `Unruled` is deleted when F5 empties it. | After F4 and F5 those sites are expected, not unruled, and their texts still differ. |
| F-D18 | content repo pin | Moved once, in F10. | The vectors are stale from F1 to F9 whatever the pin. |

### Blocks

- **Item 3 for a loop's items** (part only; the rest of item 3 is built). The ruling: *"the migration writes `| string.strip`
  on each part 30 trimmed, not once at the top level, so only those deliverables change."* 30 also trimmed **each item**
  of a loop it composed (`composer-sources` `awkward-texts`: reply `"   Examination paragraph, indented."` composed as
  `"Examination paragraph, indented."`, and `"History paragraph.\n\n"` trimmed), but at 31 a `for`'s `.content` is the
  engine's standard rendering of its items (`PlanRunner.Loops.SettleLoopAsync`, l.612–631; part 05 § 5.3): no template
  site exists where a pipe can stand on an item. So after F3 these stay unexplained: the `opening`, `closing`,
  `draft_plan` and `plain_line` prompts (each reads `node.topic_loop.content`), the deliverables `topics` and
  `topics_then_opening` inside their loop part, and `tally`'s count (80 against 85). Building them needs the owner to choose
  between (a) accepting them as expected (a rule, say `loop-items-trimmed`), or (b) the migration making each composed
  loop's `ret` a new `rendered` node whose body is `{{ node.<ret>.content | string.strip }}` (a structural change, a new
  31-only render per item, and new keys). F3 builds the source pieces only; F10 lists these sites for the PR's
  *For the owner*.

### Order

Run **F9, F7, F2, F6, F8, F3, F1, F4, F5, F10.** Shared files: `Migrate.Deliverables.cs` (F8 `WhenASourceRan`, F3
`Composition`, F1 `Deliverables` and `WhenASourceRan`'s always-valued test: F8 before F1, so F1 builds on F8's loop);
`ConditionTyping.cs` (F6 `TextLiteral`, F8 `Scope.Node` and a new reader: F6 first); `Migrate.Nodes.cs` (F7 `WriteNode`'s
trailer, F1 `Nodes` and `WriteNode`'s `if`: F7 first); `ExpectedDifferences.cs` (F7 `AnswersSent`, F4 and F5 new rules);
`EdgeCasePackagesTests.cs` (F4, then F5 deletes `Unruled`); `MigrationSentences.cs`, `V31Contract.cs` and the conformance
README (F7, F6, F8, F1, in that order, so the ids fall as stated). F4 and F5 come after F1 to F3, whose texts their rules
are tested on. F9 touches only the limits and can go first.

---

### Task F1: A node only an unreleased deliverable needs runs only when one applies (item 1)

**Files:** Modify `PF/Migration/Migrate.Nodes.cs` (`Nodes`, l.20–43: after the first `AlwaysValued` loop at l.33–34;
`WriteNode`, the leaf header's `["if"] = null`, l.71), `PF/Migration/Migrate.Flows.cs` (`AlwaysValued`, l.223–238: the
leaf arm `_ => true`), `PF/Migration/Migrated.cs` (beside `NodeReads`, l.156), `PF/Migration/Migrate.Deliverables.cs`
(`WhenASourceRan`, l.76: `read.AlwaysValued`), `PF/Migration/MigrationSentences.cs`, `tests/V31Source/V31Contract.cs`
(`MigrationAreaSentences`); fixture `tests/Fixtures/ByteEquivalence/hand-edits/example-structured-intake/nodes/sections_senior_review_section/senior_review_section.md`
(and any other `hand-edits/*/nodes/**` file whose migrated counterpart gains an `if`); Tests
`tests/V31Migration/MigrateNodesTests.cs`, `MigrateDeliverablesTests.cs`, `HandEditsTests.cs`; docs part 10 § 1.2, twin § 10.1,
conformance README.

**Interfaces — Produces:**
- `Migrated.DeliverableGates`: `Dictionary<string, JsonNode>` (30 leaf id → its gate, canonical JSON Logic), filled in the
  nodes pass before any node is written.
- `Migrated.AlwaysValued30`: `HashSet<string>` (30 node ids always valued ignoring gates: `AlwaysValued` as today).
- `Migrate.AlwaysValued(NodeTree tree, string id, Dictionary<string, CaseShape> cases, int depth, IReadOnlySet<string> gated)`:
  the leaf arm becomes `_ => !gated.Contains(id)`.
- `private static void DeliverableGates(Migrated m, NodeTree tree)` in `Migrate.Nodes.cs` (or a new `Migrate.Gates.cs` partial).
- `MigrationSentences.RunsOnlyWhen(string file, IReadOnlyList<string> deliverables)` → A10-087 (F-D6's words).

**Consumes:** `WorkflowNodeClosure.Edges(IReadOnlyList<WorkflowNodeSpec>, IReadOnlyDictionary<string, IReadOnlySet<string>>?)`,
`WorkflowNodeClosure.Reachable(IEnumerable<string>, IReadOnlyDictionary<string, HashSet<string>>)`, `m.Walk30.Edges`,
`m.Walk30.EdgesOf(string)`, `m.Walk30.MacroEdges(string)`, `NodeTree.Children(WorkflowNodeSpec)`,
`WorkflowConditionConverter.FromScriban(text, m.Scope((string?)null), ConversionKind.Condition)`, `JsonLogicShape.Paths(JsonNode?)`.

- [x] **Step 1: Failing tests** (`MigrateNodesTests` unless named).
  - `ANodeOnlyAConditionalDeliverableNeeds_GetsItsIf`: in `example-structured-intake`,
    `nodes/sections_senior_review_section/senior_review_section.md`'s header `if` equals `deliverables/senior_review.md`'s
    `if` (`{">=":[{"var":"input.patient.value.age"},65]}`), and `long_stay_section`'s and `index_note`'s equal their
    deliverables'; each loop's `index.json` is unchanged (sixteen members, no `if`); the A10-087 notice once per gated
    file, word for word, `V31Contract.AssertSentence(new SourceRefusal("A10-087", notice, ""))`.
  - `ALoopsBodyLeaves_EachTakeTheGate`: in `example-classifier-scope`, `plan_section`, `decline_letter` and
    `information_request` (in `nodes/plan_sections/`, `nodes/letter_decline_letter/`, `nodes/letter_information_request/`)
    each carry their deliverable's `if` (reading `node.scope.value.value`); in `example-conditional-documents` the two
    section leaves carry `consult_note`'s and `progress_note`'s.
  - `AClassifierAndWhatADeliverablesConditionReads_AreNeverGated`: `example-classifier-scope`'s `nodes/scope.md` keeps
    `if: null`.
  - `ANodeAnUnconditionalDeliverableNeeds_IsNotGated`: `example-structured-intake`'s `note_section` and
    `summarise_note` keep `if: null` (`note` and `notes_digest` have none).
  - `TwoConditionalDeliverables_GateTheirSharedNode_ByOr`: `Migrated("composer-sources", root => …)` giving `pair` and
    `topics_then_opening` conditions on two inputs: `opening.md`'s `if` is `{"or":[<pair's>,<topics_then_opening's>]}` in
    deliverable order.
  - `AHandEditedDeliverableCondition_GatesNothing`: `plan`'s condition set to the unconvertible
    `(input.consult_draft | string.upcase) == ""` (as `MigrateDeliverablesTests` l.195 does): `plan_gate`'s leaves keep
    `if: null`.
  - `AWhile_IsNeverGated`: `composer-sources`' `refine` body (`draft_summary`) keeps `if: null` when `summary` is given a
    condition.
  - `MigrateDeliverablesTests.AGatedSource_AddsNoStateTerm`: a deliverable whose only source is a gated leaf keeps 30's
    `if` exactly (no `.state` term, no A10-079).
  - `HandEditsTests.EachHandEditedNode_KeepsTheMigrationsGate`: for every `hand-edits/*/nodes/**.md`, the hand edit's header
    `if` equals the migrated file's when the latter is non-null.
  - `ByteEquivalenceTests.TheGatedPackages_SendOnlyThePrompts30Sent`: over every scenario of `example-classifier-scope`,
    `example-conditional-documents` and `example-structured-intake` (`EquivalenceFixture.Compared`), no `#prompts/` text is
    31's alone.
  - `MigratedPackagesTests.NoHandEdit_MeansPublishRefusesNothing` (existing) stays green for all packages.
- [x] **Step 2: Run, fail** (focused filter on `MigrateNodesTests`).
- [x] **Step 3: Implement.** In `Nodes`, after l.33–34: copy each id whose `AlwaysValued` holds into
  `m.AlwaysValued30`; call `DeliverableGates(m, tree)`; then recompute every `m.NodeReads[id]` with
  `AlwaysValued(tree, id, cases, 0, m.DeliverableGates.Keys.ToHashSet())`. `DeliverableGates`:
  1. `edges` = a mutable copy of `WorkflowNodeClosure.Edges(m.Manifest30.Nodes ?? [], m.Walk30.Edges)`, plus, for every flow
     in `tree.ById`, an edge to each of `NodeTree.Children(flow)`.
  2. For each deliverable of `m.Manifest30.EffectiveDeliverables`: its roots (each `sources` entry without `node.`, each
     `m.Walk30.MacroEdges(macro.Id)` of `deliverable.Macros`, the id of `deliverable.Check` without `node.`) and its rule:
     `deliverable.If ?? deliverable.When` converted by `WorkflowConditionConverter.FromScriban` directly (no notice: the
     deliverables pass converts and says it as today), `Logic` or null.
  3. K = `Reachable(classifiers ∪ node ids in JsonLogicShape.Paths(each rule) ∪ m.Walk30.EdgesOf(each top-level if/case) ∪
     roots of every deliverable whose rule is null, edges)`; a classifier is a node whose 30 `Format` is `classification`.
  4. For each top-level id in `tree.Top`, not a check (`IsCheck`), not in K, whose subtree holds no `while`: R = the
     deliverables with a rule whose `Reachable(roots, edges)` contains it; when R is not empty, gate = R's rules in order (one,
     or `{ "or": [ … ] }`, each `DeepClone`d), written into `m.DeliverableGates` for the id if it is a leaf, else for every
     leaf of its subtree (walk `NodeTree.Children` recursively; skip checks).
  In `WriteNode`, the leaf header's `["if"]` is `m.DeliverableGates.GetValueOrDefault(id)?.DeepClone()`, and when set, the
  A10-087 notice for `file` with R's ids. In `WhenASourceRan` (l.76) test `m.AlwaysValued30.Contains(id)` in place of
  `read.AlwaysValued`. Then carry the gate into the hand edit `senior_review_section.md`'s header (copy the migrated `if`)
  and into any other hand-edited node file the new test names; say in each README's answer that the `if` is the migration's.
- [x] **Step 4: Run** the V31 run. A clean package that gains a hand edit through the new guards (R17 now sees a gated
  leaf as nullable) is recorded in *Recorded in the build*, not hidden.
- [x] **Step 5: Docs.** Part 10 § 1.2 gains a row after the R15 row: *"a node only deliverables with a condition need (30
  ran only what an applicable deliverable needed) | **each leaf of it gets those deliverables' `if`**, one or joined by
  `or` (a `for` has no `if`, so its body's leaves take it); a classifier, what a condition reads and a `while` are never
  gated; a notice (A10-087) ([ruled](…#issuecomment-7199237966), item 1)"*; the twin § 10.1 the same; the conformance README
  row A10-087. **Commit** `PackageFormat (#1509): a node only conditional deliverables need runs only when one of them applies`,
  `Tests (#1509): the senior review hand edit keeps the migration's gate`, `Docs (#1509): part 10 says which nodes the migration gates`;
  tick; push.

### Task F2: A date input read whole keeps 30's form (item 2)

**Files:** Modify `PF/Migration/TemplateRewriter.cs` (`Run.Output`, l.299–325: the call `Read(chain, head, vars, sink, false)`
at l.310; `Run.Read`, l.1031, its `input` case l.1056–1068); Tests `tests/V31Migration/MigrateTemplatesTests.cs`,
`TemplateRewriterTests.cs`, `ByteEquivalenceTests.cs`; docs part 10 § 1.2, twin § 10.1.

**Interfaces — Produces:** `TemplateRewriter.ThirtyDatePattern = "%d %b %Y"` (internal const); `Run.Read(…, bool wrap,
bool output = false)`.

- [x] **Step 1: Failing tests.**
  - `MigrateTemplatesTests.AWholeDateInput_IsWrittenIn30sForm`: `example-conditional-documents`'
    `nodes/sections_consult_note_section/consult_note_section.md` body holds
    `Encounter date: {{ input.seen_on.value | date.to_string "%d %b %Y" }}`; `example-v12-constructs`' `nodes/patient_header.md`
    holds `{{ input.seen_on.value | date.to_string "%d %b %Y" }}.`
  - `TemplateRewriterTests.ADateAlreadyPiped_InATest_OrAField_IsLeftAsToday` (with `Working(package, change)`): a prompt
    `{{ input.seen_on | date.to_string "%d %B %Y" }}` becomes `{{ input.seen_on.value | date.to_string "%d %B %Y" }}`;
    `{{ if input.seen_on }}x{{ end }}`'s test gains no pipe; a text input read whole gains none.
  - `ByteEquivalenceTests.AMigratedDate_RendersAs30Did`: `example-conditional-documents/new-patient-billing`'s
    `#prompts/node.sections_consult_note_section.consult_note_section:assessment` and `example-v12-constructs/default`'s
    `#renders/node.patient_header` are equal (absent 31-only prompts aside, F1's).
  - The migrated packages still publish (`MigratedPackagesTests.NoHandEdit_MeansPublishRefusesNothing`).
- [x] **Step 2: Run, fail.**
- [x] **Step 3: Implement.** `Output` calls `Read(chain, head, vars, sink, false, output: true)`. In `Read`'s `input` case
  with `steps.Count == 1`, when `output` and the input's format is the JSON string `"date"`, the inserted text at `End(token)`
  is `.value | date.to_string "%d %b %Y"` (one edit, so `Order` sees one insertion) in place of `.value`. Nothing else.
- [x] **Step 4: Run** the V31 run.
- [x] **Step 5: Docs.** Part 10 § 1.2's bare-read row (l.78) gains: *"a date input read whole is written
  `{{ input.x.value | date.to_string "%d %b %Y" }}`, 30's `09 Nov 2026` ([ruled](…#issuecomment-7199237966), item 2); a new
  package keeps the ISO form (#357)"*; twin § 10.1. **Commit** `PackageFormat (#1509): a date input read whole migrates in 30's "%d %b %Y" form`,
  `Docs (#1509): …`; tick; push.

### Task F3: Each source a deliverable composes is stripped, as 30 trimmed it (item 3)

**Files:** Modify `PF/Migration/Migrate.Deliverables.cs` (`Composition`, l.124–138: the source piece at l.132; its summary
l.120–123 and the class summary l.6–20); fixture `tests/Fixtures/ByteEquivalence/edge-tests/*.json` where an `expect` text
moves; Tests `tests/V31Migration/MigrateDeliverablesTests.cs` (`EachSource_IsReadAsItsContent_InSourcesOrder`, l.110–134),
`ByteEquivalenceTests.cs`, `EdgeCasePackagesTests.cs`; docs part 10 § 1.2 (the deliverables row, l.69), twin § 10.1.

- [x] **Step 1: Failing tests.**
  - `EachSource_IsReadAsItsContent_InSourcesOrder`'s rows become `{{ node.<id>.content | string.strip }}` per source
    (`pair`: `{{ node.opening.content | string.strip }}\n\n{{ node.closing.content | string.strip }}`).
  - `MigrateDeliverablesTests.ASnippetPiece_IsNotStripped`: `composer-macros`' `prepended.md` is
    `{{ snippet.letterhead.content }}\n\n{{ node.letter_body.content | string.strip }}`.
  - `ByteEquivalenceTests.EachComposedSource_IsTrimmedAs30Did`: `composer-macros/new-visit-defaults#deliverables/deliverable.prepended`
    is equal or expected (only `escaped-plain-text` credited where it was), and so is every deliverable over `letter_body`;
    `composer-sources/awkward-texts#deliverables/deliverable.pair` is equal.
  - The `escaped-plain-text` credit on `composer-sources` `tally` and `example-v12-constructs` `consult` still holds
    (`EscapedReads` finds a stripped value: if it does not, match the held value's `Trim()` too).
- [x] **Step 2: Run, fail.** **Step 3: Implement:** the piece is `$"{{{{ {SourceRead(m, source)}.content | string.strip }}}}"`;
  the pieces of `DeliverableMacro` and the signature are unchanged. Update any `edge-tests/<package>.json` whose `expect`
  text for a site `EdgeCasePackagesTests.Unruled` lists now differs (the test prints the site): write the run's text.
- [x] **Step 4: Run** the V31 run. Write in *Recorded in the build* the sites still unexplained for difference 4 (the
  **Blocks** list).
- [x] **Step 5: Docs.** Part 10 § 1.2's deliverables row: *"…each source read as `{{ node.<id>.content | string.strip }}`,
  as 30's composer trimmed each part, never the whole ([ruled](…#issuecomment-7199237966), item 3); a macro's snippet is
  not stripped (R5)"*; twin. **Commit** `PackageFormat (#1509): each source a migrated deliverable composes is stripped, as 30 trimmed it`;
  tick; push.

### Task F4: A loop item that did not run is an expected difference (item 4)

**Files:** Modify `PF/Harness/ExpectedDifferences.cs` (`Rules`, l.62–181: a new rule after `while-no-pass-separator`; a new
private function beside `WhileNoPassSeparator`, l.1501); `tests/V31Migration/EdgeCasePackagesTests.cs` (`Unruled`, l.49–57:
remove the two `edge-items-not-run` sites; the `Q4` const; the test at l.113–144 per F-D17); fixture
`edge-tests/edge-items-not-run.json` if its `problem_list` `expect` must hold 31's text; Tests
`tests/V31Migration/ExpectedDifferencesTests.cs`; docs part 10 § 3.2, twin § 10.11.

**Interfaces — Produces:** `ExpectedDifferences.Rules` gains `new("loop-item-not-run", "#1508, 2026-11-26, item 4, ruled by
the owner (Q4 stands; both differences expected); part 10 §§ 1.2, 3.2", "an item of a loop that did not run (past its max,
or its body skipped) writes nothing at 31, where 30's template loop wrote its fixed text for it with its reads empty; that
text is 30's alone") { Bytes = LoopItemNotRun }`.

- [x] **Step 1: Failing tests.**
  - `ExpectedDifferencesTests.ALoopItemThatDidNotRun_IsExpected`: `EquivalenceFixture.Compared("edge-items-not-run",
    "third-problem-not-run")`: `#renders/node.problem_lines` and `#deliverables/deliverable.problem_list` are
    `(Equal: false, Expected: true, Unexplained: false)` with `Rules == ["loop-item-not-run"]`.
  - Near misses on hand-made pairs: a 30-only line that lacks the guarded body's literal text (`"- "`, `": "`) is not credited;
    a 30-only line when every item of the loop completed is not credited; a template loop that is not the migration's guarded
    form (`{{ for e in node.x.items }}…` without `{{ if e.state == "completed" }}`) credits nothing.
  - `EdgeCasePackagesTests.TheReport_HasNoUnexplainedText("edge-items-not-run")` with `Unruled` holding no site of it.
- [x] **Step 2: Run, fail.**
- [x] **Step 3: Implement `LoopItemNotRun(DifferenceContext c, string old, string now)`.** (1) Only `Render`, `Deliverable`
  and `Prompt` rows. (2) The templates the row is made from: the row's own source file in `c.Migrated.Files` and the files of
  the rendered nodes it reads, walked through `c.Plan` as `PlacedMacros` (l.1072) walks reads. (3) In each, every R16 guard the
  migration writes: `{{ for <v> in <loop read>.items }}{{ if <v>.state == "completed" }}BODY{{ else }}{{ end }}{{ end }}`;
  BODY's literal fragments (text outside tags), each `Trim()`med, empty ones dropped. (4) `k` = the item rows of that loop in
  `c.Run31.Rows` (key `<loop key>:<n>`) whose `State` is not `RowState.Completed`. (5) Align 30's and 31's lines with
  `Aligned`; each run of lines only 30 has that holds BODY's fragments in order, at most `k` per loop, is taken out of 30's
  text with the line ending that joins it (the one before it; after it, for a run at the start); return `(oldWithout, now)`,
  or null when none is taken. Then per F-D17: in the test at l.136 replace `Unruled.ContainsKey(site)` with "the
  comparison's text for the site is not `Equal`" (from `EquivalenceFixture.Compared`), and drop the two sites from `Unruled`.
- [x] **Step 4: Run** the V31 run (`EveryRule_HasAGoldenHit_OrANamedReason` finds the new rule's hit in the live report).
- [x] **Step 5: Docs.** Part 10 § 3.2 gains the bullet: *"**An item that did not run** ([ruled](…#issuecomment-7199237966),
  item 4; Q4 stands): past a loop's `max`, or with its body skipped, an item writes nothing at 31 (§ 1.2's guard), where 30's
  template loop wrote its fixed text for it; that text is 30's alone (`loop-item-not-run`; `edge-items-not-run`). 30's fixed
  text for an item that never ran was 30's defect."*; twin § 10.11. **Commit** `PackageFormat (#1509): a loop item that did not run is an expected difference (Q4)`;
  tick; push.

### Task F5: Two white-space differences become expected rules (item 5)

**Files:** Modify `PF/Harness/ExpectedDifferences.cs` (two rules after `loop-item-not-run`; two functions beside
`MacroTrailingWhitespace`, l.1029, reusing `WhiteSpaceRuns` l.1116, `Undo` l.590, `PlacedMacros` l.1072);
`tests/V31Migration/EdgeCasePackagesTests.cs` (delete `Unruled` and its uses; `TheReport_HasNoUnexplainedText`, l.146–160,
asserts every text equal or expected and `counts.partlyExplained + counts.unexplained == 0`); fixture
`edge-tests/edge-unruled-whitespace.json` if its `expect` must hold 31's text; Tests `ExpectedDifferencesTests.cs`; docs part 10
§ 3.2, twin § 10.11.

**Interfaces — Produces:**
- `chosen-macro-final-line-ending` (basis *"#1508, 2026-11-26, item 5, ruled by the owner; R5; part 05 § 8.6; part 10 § 3.2"*;
  explanation *"a macro the model chose whose body ends in a line ending loses that one line ending at 31, where 30 wrote the
  body whole"*), `Bytes = ChosenMacroLineEnding`: macros of `ChoiceClassifiers.Of(c.Migrated)` (the ones the model chose)
  among `PlacedMacros(c)`, whose body in `c.Migrated.Files` (under `macros/`, in `c.Migrated.MacroBodies`) ends in `\n`; a
  white-space run of 31's text equal to 30's aligned run less one line ending, following the body's last word in 31's text,
  is written as 30's run.
- `empty-read-trailing-space` (basis *"#1508, 2026-11-26, item 5, ruled by the owner; part 10 § 3.2"*; explanation *"a read
  that rendered empty at a line's end leaves the white space before it at 31, which 31's snippet keeps and 30's macro
  trimmed"*), `Bytes = EmptyReadTrailingSpace`: a run of spaces or tabs in 31's text just before a line ending or the
  text's end, absent at the aligned place of 30's, where a snippet the row reads that is in `c.Migrated.MacroBodies` (a 30
  macro written as a snippet, D4) has a line ending in that white space and one read tag (`Seen at: {{ input.clinic.content }}`);
  31's run is taken out.

- [x] **Step 1: Failing tests.** `ExpectedDifferencesTests.AChosenMacrosFinalLineEnding_IsExpected` and
  `AnEmptyReadsTrailingSpace_IsExpected`: `edge-unruled-whitespace/default`'s `#deliverables/deliverable.letter` credited
  `chosen-macro-final-line-ending` alone and `#deliverables/deliverable.problems` `empty-read-trailing-space` alone, each
  expected with nothing left; near misses: a macro the engine placed (`problem_close`, still `macro-trailing-whitespace`'s
  hit), a line ending lost after text that is no chosen macro's body, a trailing space after literal text (not a read).
  `EdgeCasePackagesTests.TheReport_HasNoUnexplainedText` for all seven edge packages with no `Unruled`.
- [x] **Step 2 to 4: Run, implement, run** the V31 run.
- [x] **Step 5: Docs.** Part 10 § 3.2 two bullets with the two ids and `edge-unruled-whitespace`, citing item 5 ("no
  migration change"); twin. **Commit** `PackageFormat (#1509): two white-space differences the edge packages show become expected rules`;
  tick; push.

### Task F6: Text compared with a literal is compared on the input trimmed by `trim` (item 6)

**Files:** Modify `PF/Migration/ConditionTyping.cs` (`TextLiteral`, l.277–336 and its summary; the class summary l.15–20;
`IsInputRead`, l.341), `PF/Migration/MigrationSentences.cs` (`TextLiteralBecameIn`, l.48–58; a new A10-085 reason),
`tests/V31Source/V31Contract.cs` (`["A10-074"]`, l.328; `["A10-085"]`), `PF/Harness/ExpectedDifferences.cs`
(`ConditionReasons`, l.34–38, only if Step 4 finds a case); Tests `tests/V31Migration/ConditionConverterTests.cs`,
`ConditionEquivalenceTests.cs`, `MigrateNodesTests.cs`; docs part 10 §§ 1.2 (l.66), 2.2 (l.390, l.423–426), twin §§ 10.1, 10.9,
conformance README rows A10-074 (l.1058) and A10-085.

**Interfaces — Produces:**
- A10-074 (corrected): *"\<file\>, \<member\>: '\<comparison\>' became \<rule\>; text is compared with a literal by in at 31,
  and an input's text is trimmed at both ends first, where 30 trimmed its end."*
- A10-085 (a reason S10-002 gives): *"'\<comparison\>' compares an input's text with a literal that starts with white space,
  which 30 could match and 31's trim never can; write the comparison so that it keeps 30's answer."*
- The written form: `{ "in": [ <x with each text input read r as { "trim": [r] }>, ["lit"] ] }`, and `{ "!": [ … ] }` for `!=`.

- [x] **Step 1: Failing tests** (`ConditionConverterTests`, the converter over `MigrationFixture.ScopeOf`).
  - `TextComparedWithALiteral_IsComparedOnTheTrimmedInput`: `input.reply == "yes"` (a text input) →
    `{"in":[{"trim":[{"var":"input.reply.content"}]},["yes"]]}`; `!=` its negation; `string.downcase input.reply == "yes"` →
    `{"in":[{"lower":[{"trim":[{"var":"input.reply.content"}]}]},["yes"]]}`; a text field `input.intake.note == "yes"` →
    the trim on `input.intake.value.note`; a node's text `node.draft == "yes"` → no `trim`.
  - `…_AnswersAs30_ForTrailingWhiteSpace`: the rule evaluated (the converter tests' evaluator) on `"yes "` and `"yes\n"` holds,
    as 30's comparison holds on the value `CanonicalText.Normalize` gives; on `null` it does not.
  - `ALiteralThatStartsWithWhiteSpace_IsAHandEdit`: `input.reply == " yes"` → no logic; the notice is A10-085 word for word
    (`V31Contract.AssertSentence`).
  - `MigrateNodesTests` (or the test that pins A10-074 today): the notice's new words, word for word, for `composer-macros`'
    `section_close.if`.
- [x] **Step 2: Run, fail.**
- [x] **Step 3: Implement.** In `TextLiteral`, after the blank-text branch and before `member` (l.332): a literal whose first
  rune is white space and whose `reads` hold an input read → `found.Refusals.Add(("A10-085", …))`, return null (as A10-076
  does, l.325–329). Else `x` is rewritten by a new `TrimInputReads(JsonNode x, ExpressionSite operand, Scope scope,
  FormatSet formats)`: each `{ "var": p }` subtree with `IsInputRead(p)` whose type (`ExpressionChecker.Check`) has
  `FormatShape.Text` becomes `{ "trim": [ that ] }`; the blank-text branch (l.309–330) is unchanged. Correct
  `TextLiteral`'s summary and `TextLiteralBecameIn`'s doc to say what is written (F-D10's words), and A10-074's text as above.
- [x] **Step 4: Run** the V31 run. `ConditionEquivalenceTests.TheUnruledDifferences_AreTheseOnes` (expects none) must stay
  green: if a vector case now differs only because `trim` dropped leading white space, add the reason `trim-both-ends`
  (*"#1508, 2026-11-26, item 6: an input's text compared with a literal is trimmed by trim, which also drops the leading white
  space 30 kept"*) to `ConditionReasons` and the reason picker (l.275); none is expected (no vector input has leading text
  white space; checked).
- [x] **Step 5: Docs.** Part 10 § 2.2 l.390 row: `{ "in": [ trim(x)…, ["lit"] ] }` "with `trim` on each text read of an input
  in `x` (30 trimmed an input's text at its end; `trim` trims both); a literal that starts with white space is a hand edit
  (A10-085)"; l.423–426 bullet corrected the same (no longer "answers as 30's comparison did" without the trim); § 1.2 l.66;
  twin §§ 10.1, 10.9; README rows A10-074 (new words) and A10-085; content check; push. **Commit**
  `PackageFormat (#1509): text compared with a literal is compared on the input trimmed by trim`, `Docs (#1509): …`; tick; push.

### Task F7: A migrated classifier's prompt ends with its format's instruction alone (item 7)

**Files:** Modify `PF/Migration/Migrate.Nodes.cs` (`WriteNode`, l.86–98: `trailer` and the A10-064 notice; `ClassifierTrailer`,
l.102–116, deleted; `ClassifierValues`' summary l.118–121), `PF/Migration/MigrationSentences.cs` (`ValuesInPrompt`, l.241–247,
renamed `ValuesInAnswerFormat`), `tests/V31Source/V31Contract.cs` (`["A10-064"]`, l.318, and its comment l.235),
`PF/Harness/ExpectedDifferences.cs` (`AnswersSent`, l.785–815; the `sent-format` rule's basis and explanation, l.132–137);
Tests `tests/V31Migration/MigrateNodesTests.cs` (l.97, l.305–336), `ExpectedDifferencesTests.cs` (l.246–260),
`ByteEquivalenceTests.cs`; docs part 10 §§ 1.2 (l.58), 3.2 (the classifier bullet), twin §§ 10.1, 10.11, README row A10-064
(l.1047).

**Interfaces — Produces:** A10-064 (corrected): *"Node '\<id\>' declared with at 30, which 31 has no member for; its values
are the enum of its answer format, which 31 sends after its prompt."*

- [x] **Step 1: Failing tests.**
  - `MigrateNodesTests.AClassifiersPrompt_HasNo30AnswerLine`: `example-classifier-scope`'s `nodes/scope.md` body does not hold
    `Answer with exactly one of` and ends with the last line of 30's prompt; the A10-064 notice in its new words
    (`V31Contract.AssertSentence`); the tests at l.305–336 rewritten to these expectations.
  - `ExpectedDifferencesTests.ADroppedAnswerLine_IsCreditedToSentFormat`: 30 = `body + "\n\nAnswer with exactly one of:
    in_scope, out_of_scope."`, 31 = `body + "\n\n" + <the custom trailer> + <the answers' format>` → `sent-format` credited,
    nothing left; near misses: values in another order, a format with a `title`, a 30 text that does not end with the line.
  - `ByteEquivalenceTests.AClassifiersPrompt_IsExpected`: `example-classifier-scope/default#prompts/node.scope` expected with
    `Rules == ["sent-format"]`; and `edge-answer-outside-set`'s classifier prompt likewise.
- [x] **Step 2: Run, fail.**
- [x] **Step 3: Implement.** In `WriteNode` drop `trailer`; the notice is `ValuesInAnswerFormat(id)` when `kind == "agent" &&
  ClassifierValues(node) is not null && node.With is { Count: > 0 }`, else `MemberDropped(id, "with")` as now; the body is
  `PromptOf(m, node)`. In `AnswersSent`: with `listed` as today, require `old.EndsWith("\n\n" + listed)` and return
  `(old[..^("\n\n" + listed).Length], before)` (30's line off 30's text; the custom trailer and the format off 31's), no longer
  requiring `before` to end with `listed`. Correct the summaries of `AnswersSent`, `ClassifierValues` and the `sent-format` rule
  (basis gains *"#1508, 2026-11-26, item 7 (30's answer line dropped)"*; explanation *"…or a classifier's answers sent as its
  format, where 30 listed them in its prompt's last line, which the migration drops"*).
- [x] **Step 4: Run** the V31 run.
- [x] **Step 5: Docs.** Part 10 § 1.2 l.58: *"The prompt ends as 30's did before its last line; 30's *"Answer with exactly one
  of: <values>."* is dropped, so the prompt ends with the format's instruction only (A10-064, [ruled](…#issuecomment-7199237966),
  item 7), an expected difference (§ 3.2)"*; § 3.2's classifier bullet the same; twin; README A10-064. **Commit**
  `PackageFormat (#1509): a migrated classifier's prompt ends with its answer format's instruction alone`, `Docs (#1509): …`;
  tick; push.

### Task F8: A deliverable whose source's run cannot be read is a hand edit; R15 falls back to the value (item 8)

**Files:** Modify `PF/Migration/Migrate.Deliverables.cs` (`WhenASourceRan`, l.52–103: the loop l.73–80 and the term l.82–86),
`PF/Migration/ConditionTyping.cs` (`Scope.Node`, l.463–478; a new public reader beside `TypeOf`, l.84),
`PF/Migration/MigrationSentences.cs` (A10-086), `tests/V31Source/V31Contract.cs`; Tests
`tests/V31Migration/MigrateDeliverablesTests.cs` (beside `ADeliverableWhoseSourcesMayNotRun_GetsAnIf`, l.137); docs part 10
§ 1.2 (the R15 row, l.71), part 03 § 4.2 (the deliverable-`if` row, l.159), twin §§ 10.1 and 3.4.2 (l.1838), README A10-086.

**Interfaces — Produces:**
- `ConditionTyping.ReadsAtDeliverableIf(JsonNode logic, ConversionScope conversion)` → `bool`: the logic checked by
  `ExpressionChecker.Check` at `new ExpressionSite(ExpressionSiteKind.DeliverableIf, "The read", "if", "", null, null)` over
  `Scope`, no refusal and a boolean type.
- `Scope.Node`: a segment `state` right after a node's path is a `ScopeHead(ScopeHeadKind.Value, path.Length + 1,
  {"type":"string","enum":["completed","skipped","not_run"]}, false, null)`, as `ScopeTree.Member` (l.316) types it.
- `internal static JsonNode? RanTerm(string path, Func<JsonNode, bool> readable)` in `Migrate.Deliverables.cs`:
  `{"===":[{"var":path+".state"},"completed"]}` when readable, else `{"!==":[{"var":path+".value"},null]}` when readable, else null.
- A10-086 (a reason S10-002 gives on a deliverable's `if`): *"every source may not run, and the migration cannot read whether
  \<unread sources, joined by ", "\> ran; write the if that skips the deliverable when none of its sources ran, as 30 skipped it\<,
  joining \<term\> for the others\>."* (the bracketed clause only when other sources have a term).

- [x] **Step 1: Failing tests** (`MigrateDeliverablesTests`).
  - `RanTerm_PrefersState_ThenTheValue_ThenNothing`: with `readable` true for all → the `.state` term; refusing `.state` →
    `{"!==":[{"var":"node.x.value"},null]}`; refusing both → null.
  - `TheStateTerm_TypesAtADeliverableIf_AndACheckReadDoesNot`: `ConditionTyping.ReadsAtDeliverableIf` over
    `m.Scope((string?)null)` of `composer-sources` is true for `node.plan_gate.state === "completed"` and
    `node.plan_gate.value !== null`, false for `check.<id>.state === "completed"` (S03 site rule).
  - `ASourceTheMigrationDoesNotRead_IsAHandEdit`: a deliverable whose sources may not run and one of which names a check
    (a 30 manifest reshaped so `plan`'s `sources` hold `node.plan_gate` and a `terms_subset` node), or a node id
    `NodeReads` lacks: `deliverables/plan.md`'s `if` keeps 30's condition, one S10-002 on `if` with A10-086 word for word;
    near miss: the same with a source that always runs keeps today's behaviour (no `if`, no hand edit).
  - The existing `ADeliverableWhoseSourcesMayNotRun_GetsAnIf` and `ARefusedConditionWhoseSourcesMayNotRun_…` stay green
    unchanged (the `.state` term is what is written today).
- [x] **Step 2: Run, fail.**
- [x] **Step 3: Implement.** In `WhenASourceRan`: for each source, a known source that always runs (today's test) or is a
  `while` returns `rule` as now; otherwise its term is `RanTerm(read.Path, t => ConditionTyping.ReadsAtDeliverableIf(t,
  m.Scope((string?)null)))`; a source not in `NodeReads`, or with a null term, is listed as unread. After the loop: with any
  unread source, `m.HandEdit(file, "if", MigrationSentences.SourceRunNotRead(unread, termOfTheOthers))` and return `rule`;
  else join the terms as today (one, or `or`). Correct the summary (l.52–66): the read is the one the deliverable's `if` scope
  has, by asking its typing.
- [x] **Step 4: Run** the V31 run.
- [x] **Step 5: Docs.** Part 10 § 1.2's R15 row gains: *"the term is `.state === "completed"` where the deliverable's `if`
  can read the source's state, else `.value !== null`; a source whose run it cannot read at all is a hand edit (A10-086)
  ([ruled](…#issuecomment-7199237966), item 8)"*; part 03 § 4.2 l.159: *"`input`, a node's **`.value`** (data) and `.state`,
  `run`"* (F-D12), twin § 3.4.2; README A10-086. **Commit** `PackageFormat (#1509): a deliverable whose source's run cannot be read is a hand edit, and R15 falls back to the value`,
  `Docs (#1509): …`; tick; push.

### Task F9: Configured limits everywhere (item 9)

**Files:** Modify `PF/Publish/PackagePublish.cs` (`PublishOptions`, l.17–27; the tests' call, l.131),
`WorkflowPackageMigration30To31.cs` (`Migration31Options`, l.25–34; `WithSnapshot`, l.442–470: the request at l.461–463),
`src/Consultologist.Api/V31/V31PackagePublisher.cs` (`OptionsOf`, l.222–230), `src/Consultologist.Api/V31/MigratePackage.cs`
(`options with { … }`, l.210–214; `MigrationReport.WorstCaseNote(edited, types)`, l.258),
`src/Consultologist.Api/V31/OutstandingHandEdits.cs` (constructor, l.116; `new Migration31Options(…)`, l.163–165); Tests
`tests/V31Publish/PackagePublishTests.cs`, `tests/V31Migration/SnapshotTestTests.cs`, `tests/V31Engine/V31PackagePublisherTests.cs`,
`MigratePackageTests.cs`, `OperatorWeeklySummaryTests.cs`; docs part 10 § 1.4 (l.146–147), part 07 § 8.4 (l.741–748),
`docs/customizable-workflow/engine-v31.md` ("The migration line", l.650), `registry-operations.md` (the bulk publish),
`package-format-library.md` (l.2466, l.2517).

**Interfaces — Produces:**
- `PublishOptions(…, TimeSpan? MatchTimeLimit = null, RunLimits? RunLimits = null)`; `PackagePublish` passes
  `options.RunLimits` to `PackageTests.RunAsync(plan, tests, files, options.Clock, options.RunLimits)`.
- `Migration31Options(…, V31.PublishLimits? Limits = null, V31.Run.RunLimits? RunLimits = null)`; `WithSnapshot` runs
  `SnapshotTest.Fictional(plan)` `with { Limits = options.RunLimits }` when set.
- `OutstandingHandEdits(pins, sources, registry, toolchain, formats, contracts, EngineLimitSettings limits, logger)`; its
  options carry `Limits: limits.PublishLimits`.

**Consumes:** `EngineLimitSettings.Limits` (`RunLimits`) and `.PublishLimits` (singleton, `ApiServices.cs` l.173; the summary
is registered at l.211, so DI needs no change).

- [x] **Step 1: Failing tests.**
  - `PackagePublishTests.ThePackagesTests_RunAtTheGivenRunLimits`: `WithTest()`'s package publishes with `RunLimits` null; with
    `new RunLimits() with { JobAgentCalls = 1 }` it is refused, its test failing at `run` (the nested note makes many calls).
  - `SnapshotTestTests.TheSnapshot_RunsAtTheOptionsRunLimits`: a clean package (`example-prior-letters`) migrated with
    `RunLimits` `JobAgentCalls = 1` gets no snapshot and A10-061's notice (`FailsItsRun`); with none, its snapshot.
  - `V31PackagePublisherTests.APublish_RunsItsTestsAtTheEngineConfiguredLimits`: configuration
    `ConsultGeneration:JobAgentCalls = 1` refuses the publish that passes unset.
  - `MigratePackageTests.TheStoredReportsWorstCaseNote_IsAtTheConfiguredLimits`: with `ConsultGeneration:MaxAgentCallsPerJob = 1`
    and `ConsultGeneration:JobMinutes = 1` the stored report's notices hold S09-016's note; unset they do not.
  - `OperatorWeeklySummaryTests.TheMigrationLine_CountsAtTheConfiguredJobInstances`: the package
    `MigratePackageTests.AtTheConfiguredLimits_APackageOverTheDefaultButWithinThem_Publishes` uses is counted with its A10-081
    hand edit at the default and without it at `ConsultGeneration:JobInstances = 100000`.
- [x] **Step 2: Run, fail.** **Step 3: Implement** the members and the four call sites above; nothing else reads limits.
- [x] **Step 4: Run** `dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~V31Engine|FullyQualifiedName~V31Publish"`,
  the V31 run, `dotnet build Consultologist.sln` and the solution suite.
- [x] **Step 5: Docs.** Part 10 § 1.4 l.146–147: *"at the deployment's configured `RunLimits.JobInstances` (10,000 unless
  set) in the bulk publish and the weekly summary, the starting value in the script"*; part 07 § 8.4 gains *"A publish runs
  the tests at the engine's configured limits."*; `engine-v31.md`, `registry-operations.md`, `package-format-library.md` as the
  code now is; remove from the PR's minors (#1535 § 6, first bullet) the summary's default count. **Commit**
  `PackageFormat (#1509): publish and the migration's snapshot run at the limits they are given`,
  `Api (#1509): the publish, the bulk publish and the weekly summary count at the configured limits`, `Docs (#1509): …`; tick; push.

### Task F10: One regeneration, and the list of what moved

**Files:** regenerated `tests/Fixtures/ByteEquivalence/migrated/**`, `reports/**`, `conditions/converted.json`,
`conditions/equivalence.json`; `~/Projects/consultologist-package-format` `v31/conformance/migration-vectors/**` (and
`v31/conformance/index.json` if the export rewrites it); the pin `external/consultologist-package-format`; this plan's
*Recorded in the build*.

- [x] **Step 1: Regenerate once:** `GOLDEN_UPDATE_V31=1 dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~V31Migration"`,
  then the same without the variable (all green but `ThePinnedVectors_AreWhatMigrateGives`).
- [x] **Step 2: The vectors:** `CONFORMANCE_EXPORT_V31=$HOME/Projects/consultologist-package-format dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~MigrationVectorsExport"`;
  read `git -C ~/Projects/consultologist-package-format status` and the diff; `python3 scripts/check-v31.py` there (every group
  ok); commit `Format spec (Consultologist-Blazor#1509): the migration vectors after #1508's fix-first rulings`; push
  `m39-build`; move the pin (`git -C external/consultologist-package-format fetch origin m39-build` and check out the pushed
  commit; `git add external/consultologist-package-format`).
- [x] **Step 3: Read every changed golden** (`git diff --stat -- tests/Fixtures/ByteEquivalence`, then each file). Only
  these may move: leaf `if`s and A10-087 notices (F1), date pipes (F2), `| string.strip` on deliverable sources (F3), the
  trim and A10-074's words (F6), classifier prompts and A10-064 (F7), the snapshot tests and `reports/` that follow, and
  `converted.json`/`equivalence.json` for F6. Anything else is a bug in the task that caused it: fix it there, not here.
- [x] **Step 4: Write the list for the PR** into *Recorded in the build* (one line headed "Part F, what moved"): per item,
  the files changed under `migrated/` (count and the packages), and per package in `reports/` the `counts` before and after
  (from `git show HEAD~:…` against the new file); each site that became equal or expected, by item (PR #1532's differences 1,
  2 and 4; the four edge sites; item 4.3's classifier prompts); each site still unexplained, with its cause (the **Blocks**
  list for item 3; any other found); the vectors' cases changed; the pin's old and new commit. Say which items were built
  (1 to 9, item 3 in part).
- [x] **Step 5: Run** `dotnet build Consultologist.sln` and `dotnet test Consultologist.sln --filter "FullyQualifiedName!~Consultologist.Web.E2E.Tests"`;
  pass. **Commit** `Tests (#1509): migrated/, reports/ and the condition goldens regenerated once after #1508's items 1 to 9`
  (every changed golden named in the body), `Tests (#1509): the content repo pin moves to the re-exported migration vectors`,
  `Docs (#1509): Part F's record of what moved`; tick; push.

### Spec coverage (Part F)

| #1508 ruling of 2026-11-26 | Task |
|---|---|
| 1. Nodes only an unreleased deliverable needs get its `if` | F1 (F-D1 to F-D6) |
| 2. A date input written whole: `\| date.to_string "%d %b %Y"` | F2 (F-D7) |
| 3. `\| string.strip` on each composed part 30 trimmed | F3 (F-D8); a loop's items: **Blocks** |
| 4. Loop items that did not run: expected (Q4 stands) | F4 (F-D9, F-D17) |
| 5. Two white-space differences: expected rules, no migration change | F5 (F-D9, F-D17) |
| 6. Text compared with a literal on the explicitly trimmed input; A10-074 and `ConditionTyping.TextLiteral` corrected | F6 (F-D10) |
| 7. Drop 30's "Answer with exactly one of: …"; expected | F7 (F-D11) |
| 8. A source the migration does not read: a hand edit; R15's `!== null` fallback | F8 (F-D12, F-D13) |
| 9. Configured limits at publish tests, bulk publish, weekly summary | F9 (F-D14) |
| Regenerate `migrated/`, `reports/`, the vectors once; list what moved | F10 |

## Spec coverage

| Spec | Task |
|---|---|
| part 09 § 5.1 inputs and widgets; part 02 § 8.1 | 8, 9, 10 |
| part 02 § 9 enum labels (the select shows titles, sends the value; History shows conditions as printed) | 8, 9, 18 |
| part 02 § 10 per-run choices as optional booleans; three states; #1509 comment 3 note 3 | 9 |
| part 09 § 5.1 documents, earlier runs, `channel`, snippets | 3, 9 |
| part 09 § 5.2 the signature choice; part 06 § 10.3 | 1, 2, 11 |
| part 09 § 5.3 checks before submission; part 06 § 10.4; § 9's profile, zone and requires sentences | 2, 11 |
| part 09 § 3.6 *Waiting for an update to finish*, § 3.8 *Delivering…* on the pages | 12, 16 |
| part 09 § 6.1 which records (2–11 unchanged) | 16 |
| part 09 § 6.2 the job header | 7, 16 |
| part 09 § 6.3 banners; part 06 §§ 5, 7 | 14, 16 |
| part 09 § 6.4 Inputs, Run facts | 17 |
| part 09 § 6.4 Nodes; part 08 §§ 4, 5, 7 | 13, 15, 18 |
| part 09 § 6.4 Checks; part 06 § 5; part 08 § 6 | 15, 19 |
| part 09 § 6.4 Deliverables; part 06 §§ 8, 9; part 08 § 10; R1 | 20 |
| part 09 § 6.5 test results never in a job's record (shown in the Editor) | 24 |
| part 09 § 7.1 Letter details; part 06 § 10.7 | 21 |
| part 09 § 7.2 the time zone; part 06 § 10.7 | 22 |
| part 09 § 7.3 previews never read the profile | no change: the 31 view has no preview; nothing new reads the profile |
| part 09 § 8.1 the Editor view-only | 23, 24 |
| part 09 § 8.2 the content endpoint (built in area 11); the migration report | 7 |
| part 09 § 9 sentences History shows (S09-010 to S09-012, S09-015, S09-017, A09-001, A09-002, the loop limit) | 14, 18, 20 (stored on the rows; shown as stored) |
| #1509 comment 2: references at a start; 1-based numbers | 3, 4; 13, 15 |
| #1509 comment 3: not-run nodes' decisions; `classifier` null | 15, 18 |
| #1509 comment 4: the record's account; one vocabulary; Delivering, the dead job, the record check, `kind`, `SignatureBlockId` | 5; all of Part C; 16; 14, 16; 6; 17; 11 |
| the browser suite; the documents | 25, 26 |

## Recorded in the build

*(Each task adds a line here for a choice this plan does not state.)*

- Task 13: `Record31Fixtures.Job()` is made by the engine's public writers (`RunRow.ToJson`, `RunDecision.ToJson`, `DeliveryRow.ToJson`) from hand-given `Detail`s, since `RecordRows` is internal and an offline run's rows are not those of a small hand-picked case. `KeyStep31` carries the key's innermost pass and all its item ids on the last scope's step. `ParentInstance` is the key minus its last scope (items and passes kept), null for a one-scope key, a deliverable or a delivery; a loop's own item row is keyed like its loop, so a consumer resolves the parent with `Record31.Find`. `Record31` also exposes `Header`. A delivery row is grouped by its `deliverable` member, else by the deliverable its key names. `RunRail31` was left as it is (it splits no keys); `RowKeyOrder31` now uses `RowKey31.InstanceStart`.
- Task 6: `CheckRecord` names its lines `recordHash`, `deliverables[<key>].composedHash` and `deliverables[<key>].replay` (the Web twin the same in `What`); a deliverable with no retained text gives one line (the `composedHash` one, "text deleted; hash kept") and no replay line. `Check` at storage 12 returns that one `recordHash` line (plus the input-hash line when inputs are supplied). The script takes `--host <api url>` (token in `$TOKEN`) to fetch `/Record` and each deliverable's `Rows?key=` text, or `--record <file>` offline (texts then count as dropped). `ProvenanceHashes.cs` only gained a pointer comment: a storage 12 record is not checked there.

- Task 5: **For the user, before the build is deployed to a Function App:** set `ConsultGenerationRecordStorage__BlobServiceUri` on each Function App (the records account's blob service URI, named in `docs/STORAGE.md`) and give its identity Storage Blob Data Contributor on the records account; the `job-records` container is created on the first write. Without the setting the document is written to a `job-records` container of the text account and the host warns once at startup. Choices: the setting is explicit only (no derivation from `Storage__Region`, as the brief says "else the text account's"); account closure deletes the records prefix only for an account with a storage-12 job (as it does for the rows, F5), so a 5-30 account's closure makes the calls it made before; a record written to the old place is not read from there (no fallback code): the restore path assembles it from the rows and writes it to `job-records`.

- Task 2: the starter's account reading is shared as three `internal static` members of `V31JobStarter` (`ProfileAsync(settings, accounts, plan, appUserId, requested, ct)`, `TimeZoneAsync`, `AccountRefusals`) rather than the brief's one `AccountAsync`, because the display name comes from `IAccountStore` and A08-004's substitution for S06-031 must be shared too. The route takes `ref` as `Current/Content` does (`ParseRequestedPackage`, ownership check), `Current` itself taking none. `NeedsProfile` is in the plan's order (sorted), not the package's written order. The Web throws `HttpRequestException` whose message is the 400's `error`.
- Task 3: the references are read inside the starter's 31 extraction (`ExtractFor31Async`), in today's order and before the documents (references, form references, questionnaire files, then documents), not after them: the extraction is what places an earlier run's elements among typed rows and documents at their `Position`s (`RefGroups`). `ResolveInputRefsAsync` takes no manifest; its 31 overload (over `DeclaredInput31`: id, array of text, channel `form`) only makes a lone reference to an array of text a list of one. A form reference or questionnaire file fills only an input whose channel is `form`; on any other input a form reference is refused as `InputFormRefMismatch` in today's words for an answer a form cannot fill (*"The response's answer for input '<id>' cannot be filled from a form answer."*), narrower than 30, where a held response may fill a text input by coercion. The held answer (or the file's object) is the value, held to its format by `RunInputsBuilder`; a value sent beside it must equal it (today's mismatch words), and one left out is filled from it, so today's *"carries a form reference but no value"* is not said at 31. A form-filled input is recorded with origin `typed` (part 08 § 3.1 names no form origin). `ModelDerivedInputs` reaches the orchestration through the held inputs document's `modelDerived` member (ids in ordinal order, absent when none), not the orchestration input. `Supplied` is unchanged: the resolvers fill the request's `Inputs`, which it reads. A 31 start writes no lineage link rows (`IConsultGenerationLinkStore`) yet.
- Task 4: a 31 start writes the lineage link rows a 5–30 start writes (`LinksFrom`, through an optional `IConsultGenerationLinkStore` on `V31JobStarter`), after it holds its inputs, the rerun edge included (controller's ruling; commit c46151f4). The 31 header now carries the origin's `RerunBaseline`, which is how `RerunOf` is set; a 31 rerun's baseline holds the source id and no hashes, and the entity does not stamp a verdict or divergence at record storage 12 (D8). The held inputs of a 31 job are read in `HydrateOutputsAsync` and the route through an optional `IRunInputsStore` on `ConsultGenerationJobs`, a trailing constructor parameter as `V31.RecordSealers` is; the route's request is built by `RerunRequestAsync`, so a test can reach it. A 31 rerun sends the held values as typed values and repeats the source's `previous-run` origins (original job and deliverable, `modelDerived: true`, the id in the run's `ModelDerivedInputs`, the lineage links to the original sources) so earlier-run text is never recorded as typed (part 08 § 5.1): the rerun door puts them on `ConsultGenerationJobOrigin.InheritedInputOrigins` (server-side, no request member; `RerunOriginFor`), read from the source's response `InputOrigins`. A default signature pick is not carried (the account's chosen block is read afresh); a `job` pick is.
- Task 7: `V31PackageContent.ReadAsync` takes a trailing optional `ILogger? logger = null`, which `Current/Content` passes its own logger, so the not-JSON warning (package and version only, never the file text) is logged from the reader; a stored report that is not JSON is served as null with the route still 200.
- Task 8: a reference resolves by `$id` or by `#/$defs/<name>` in the root's `$defs`; a sibling `title` beside a reference wins over the target's. A field is `Required` only when `fields` says so and its shape is not nullable. With no `fields`, a group reads its `properties` as written and `required`. `Unsupported` also covers a `type` array that is not exactly one type plus `"null"`, an array with no `items`, and a field named in `fields` but absent from `properties`.
- Task 9: a 31 input's slot is chosen by its shape alone (`InputShapes.Of` over `Format`), not by the 30 members the Api also fills: a text input (`Line`, `Area`, `Markdown`), an array of text, and any input with a channel keep today's slot (a form-channel group keeps the chip and questionnaire door, and its fields are `Input31` with `ShowLabel="false"`, an optional parameter beyond the brief's six; a second one, `SnippetItems`, offers the account's snippets on a text field inside a group or a row, as the 30 tree does). `InputShapes` gained part 02 § 3.2's other two nullable spellings (an `anyOf` of a reference or an object and `{ "type": "null" }`, a `{ "const": null }` arm of a labelled enum), without which the spec's own nullable `notes` field (§ 8.4) was unsupported. At 31 a blank required input holds Start (as a blank non-nullable field does, part 09 § 5.1, and a required boolean left unset, D2) and says *"Required."* at once; below 31 it still waits for the click. The #802 referral floor and its acknowledgement are not drawn at 31: `V31JobStarter` applies no floor. A 31 data value travels as `ConsultInputValue.FromJson(InputValue31.ToJson())`: the wire value already carries JSON `null` inside structure (`ConsultInputKind.Null`), so nothing was extended. A form or questionnaire fill of a 31 input is compared, for "still as filled", with what the input holds after the fill (every field written, a blank one as null). An input whose format has a part with no widget is named with A12-006 and holds Start.
- Task 10: `MarkdownInput` is also drawn by `Input31` for a Markdown field inside a group or a row (`Rows="6"`), and for rows of an array of Markdown; `wrapSelection` is exposed as `window.consultologistMarkdown.wrapSelection` (script tag in `index.html` after `download.js`) and numbers a numbered list per selected line. The component takes `Required` and `Placeholder` parameters; `Required` is accepted but draws nothing (the slot draws the star and the hint). A Markdown text area is not focused by the page after "type instead" (`ScalarInput.TextArea` stays null for it).
- Task 11: `Current`'s 400 now throws `HttpRequestException` whose message is the `error` (as the start checks' does; only a 31 load answers `Current` with a 400), and the page says it above Start with no input drawn; the package picker stays offered at the top, without today's "Unable to load section standards" bar. A 400 from the start checks themselves is said the same way. The checks are asked after a 31 load, on each pick, on window focus (`wwwroot/js/windowFocus.js`, `consultologistWindowFocus.listen/stop`, a `DotNetObjectReference` to the page, stopped in `DisposeAsync`) and before each of the three starts; each ask re-reads `profile.signatures` first, so a pick naming a removed block, or one that was the default when the default changed, falls back to the chosen block. Every refusal from the checks is the account's, so one "Open Profile" link (`/profile`) follows them; Current's sentence and A12-004 have none. "choose…" is offered only while no block is chosen. The pick stays after a queued or scheduled start (it is never saved). The test base (`ClientRenderTestContext`) answers the start checks with two empty lists unless a test says otherwise. A12-004 added to `refusal-sentences.md` and `V31Contract`.
- Task 12: the 31 run view (`RunView31`) is reached by the job's `SchemaVersion >= 12` alone, so until the first snapshot a fresh 31 run shows today's view for a moment. `RowFinished.Status` and `RailChild31.Status` are `string?` (the event's `status` is `null` for a row that has none). A child's `Label` is the path after `:` (`2/1`), the pass from `#` (`#3`, `#2:3`), or, for a row under the block with neither (a call's inner node), the scopes after the block key; children are in key order as a person counts (`RowKeyOrder31`: the rooted key as text, then the item path's segments and pass numbers as numbers; shared with History). Only a `released` deliverable is fetched (`Rows?key=`, never two at once for a key, not awaited in the SSE handler); a fetch that finds no row, no text yet, or fails says the route's sentence (*"The record isn't available yet. Try again."*, `RecordRoutes.NotAvailable`, also for a 404 or a failure with no route words) and is asked again on the next listing, poll and `done` (where the rows are listed once more); `blocked`, `held`, `failed` and `skipped` show their stored state and label only, a blocked one with a "View in History" link to `/history/{jobId}`. The text is `text.content`, rendered as Markdown when the row's `format` is `markdown_block`, else as plain text; a dropped text says A12-002 (added to `refusal-sentences.md` and `V31Contract` here, first said on this page) with the row's `composedHash`. The rail lists `GET …/Rows` on attaching and on a poll whose `Rows` count moved (a poll carries no row events); decisions are not rail rows. The job's `RowStates` are shown as counts beside their stored words; the error shown is `RuntimeFailureError` (else `StartFailure`), the response's field for the header's `error`. A released deliverable with its text has the 5–30 view's Copy note (the same clipboard call and feedback); none for any other state or a dropped text. The test `TheStatusDelivering_ReadsDelivering…_AndTheDocumentIsShown` is named without the ellipsis (not a C# identifier character). The record download (`DownloadJobRecordAsync`) takes its file name from `Content-Disposition`, else `record-<jobId>.json`.
- Task 14: a failed deliverable's banner drops the stored reason's own `failed: ` (`<label> failed: <reason>`), a held one its `held: `, a blocked one its `blocked: ` (S06-033/S06-037's words, ended with a full stop); a not-sent delivery's banner is `<label> was <reason>` with the stored `not sent: …` as it is. A delivery's `<destination>` is the engine's `DeliveryPlan.KindWords(kind)` (`Email`, `PDF`, `EMR`, `Zip`), as S06-034 writes `<Kind>`. Banners run per deliverable (its own, then its deliveries in key order). A deliverable with no stored label is named by its key. `RunView31.TextDeletedSentence` is now `HistorySentences31.TextDeleted`. A12-005 is registered with no constant yet (Verify, Task 20).
- Task 15: item and pass rows are the group nodes (built on `Record31.ParentOf`/`ChildrenOf`); an item's title is its row's `itemName`, else the retained name, else `<loop row's label> <item number from the key>` (the loop's last scope for a row that truly has no label; the engine always writes one and the fixture now does); a pass's title is `Pass <n>`; a node for an owner with no row has `Kind` `""`. A row whose item or pass row is missing sits on the nearest row present (`ParentOf`'s rule), so the fixture's `node.sections.draft:10` is a child of the loop. `TreeNode31` also carries `IsItem` and `LoopLabel` so a page can retitle an item with `ItemTitle` once its retained name is fetched; a row with no `status` shows an empty one, `NotRun` is for owners with no row only.
- Task 16: a storage 12 job keeps the page's chips (the resolve and lock hashes are two chips beside the package's), help, Rerun row, Provenance list and "Used by", drawn by the same markup as 5–30 (the shared lines stay in `History.razor` rather than a component: their helpers read page state and the page's scoped CSS styles them); the list's 5–30-only lines (Decided, Delivered, the input origins, the output hash and per-document lines, the macro lines, Unattested and Custom shapes) and the 5–30 Verify are guarded by `!IsStorage12`. `RecordView31` follows the list, in place of the rerun comparison (D8: `Header31` links to `RerunOf` only, and the source is not fetched) and the node grid; `Header31` holds only 31's own lines. Verify walks the downloaded record's own `deliverables`, a text counting as dropped only when its row says `textDeleted`. A stopped 31 job is fetched at the 60 s rate even while another job keeps the poll fast. `JobLiveness31.IsTerminal`/`DeliveringStatus` and `HistorySentences31.StatusText` are the one terminal set and the one "Delivering…" mapping (`RunRail31.StatusText` delegates). The page also fetches the job's package content (`GetCurrentPackageContentAsync(WorkflowPackage)`, once per detail load, a failure read as no content) for D10's line, which links to `/editor` (the Editor has no deep link to a ref). The rows are listed once per detail load; a poll lists them again only when the job's `Rows` count has moved, and a later listing that fails keeps the earlier one. A first listing that fails says the route's words (`ConsultGenerationRefusedException`), else *"The record isn't available yet. Try again."* (`HistorySentences31.RecordNotAvailable`); the download says the same. "Stopped" (D9) is decided only for a job a detail fetch has shown to be at storage 12, since the account's job list carries no storage version; the grid shows the label and A12-001 beside it, and the job is still polled every 60 s. `delivering` is polled at the fast cadence. A12-005's `<what>` is *the record file* or *the text of '<deliverable label>'* (the key when unlabelled); a row fetch that fails or finds no row says it, and nothing is checked. Verify's result reads *Verify: passed* or *Verify: failed* over `RecordCheck31`'s lines, a `null` line marked attested. `RowText31` forgets a failed fetch so the next ask tries again. `RerunBlockedReason` needed no change: a 31 job's held inputs come through `HeldInputs` (Task 4). Confirmed in the Api: `StartedAtUtc` is first written by `RecordV31Header`/`MarkRunning`, after `WaitWhileDrainingAsync`, and `MarkWaitingForUpdate` moves only a Queued or Scheduled job, so a job waiting behind a drain has no start time and `JobLiveness31` needed no change.
- Task 17: the job response lacked each input's hash and origin word (`InputOrigins` has the extraction members but not the hash, and a typed input has no origin entry), so the Api response gained a trailing `Inputs` (`JsonArray?`, omitted below storage 12: the header's own `inputs` array, `RecordInputs()`) and the Web mirrors it; 5–30 responses are byte-equal (the 30 golden is unchanged). Everything else (run facts, zone, versions) was already on the response. Run spots are gathered from every node, check and deliverable row's `runSpots` and each row's `macros[].runSpots`; a spot is listed under the fact its `reads` names (`run.<fact>` or beneath it), and a spot reading anything else (a run-dependent value) under a group headed by what it reads. A spot whose `output` is null is fetched through `RowText31` on its *Show output* button (aria-expanded), from the row that owns the body (a macro's spot from the row holding the macro), and sliced by `start`/`length` in runes; if the slice does not fit only the hash shows. A body's sent-text hash for A12-003 is chosen by kind (fix round 1): `inputHash` for an agent row, `outputHash` for a rendered node, `composedHash` for a deliverable, `contentHash` for a macro; A12-003 shows only when both it and `runNeutralHash` are present and differ. A computed row is deliberately not a body and gets no A12-003: its two hashes cover an expression and the values it read (part 08 § 4), not text with run spots, and the sentence speaks of text (part 09 § 6.4 lists a prompt, a rendered node, a deliverable or a snippet as bodies). A retained spot output is read from `text.spots[<spot id>]` (the engine cuts it from the sent text, the content or the composed text), never sliced by the page; a macro's own spots have no retained text, so they show the hash with no disclosure; a missing row, failed fetch or absent entry says `RecordNotAvailable`. A link to a row in Run facts and the banners goes through `RowLink31`/`RowLinks31`: a click calls Nodes31.Reveal (Task 18's mechanism), and a target outside Nodes follows the fragment with `NavigationManager`. Inputs31 has no row links (its only link is to another job's page). *No run facts were read.* and *No inputs were recorded.* are labels for an empty section, not area sentences (no value, no format term).
- Task 18: a row's disclosure is a button with `aria-expanded` (as Task 17's), and a collapsed row draws only its line (title, key, kind, stored status, a call's function, the run-dependent mark, its `stateReason`); its body (hashes, agent, sub-rows, decisions, text, the rows inside it) is drawn only once opened, which is when its text is fetched (an owner with no row fetches nothing). Which rows are open is held by `Nodes31` and shared with each row through `Nodes31.View`; a `#row-<key>` fragment, on arrival or on `LocationChanged`, and a sub-row's link clicked in the section (`@onclick:preventDefault`, since `<base href="/">` resolves a bare `#…` link to the home page), open the row holding the target (the row, a decision's owner, or the longest leading part of a sub-row id cut at `/` that is a row) and every row it sits in, by the tree `NodeTree31` built, then focus it (`ElementReference.FocusAsync`, `tabindex="-1"`). The engine stores S09-010 to S09-012 whole (with *Run the workflow again later.*) though part 08 calls that History's half: `HistorySentences31.StateReason` shows a stored reason as it is and adds `RunAgainLater` only to a limits sentence that lacks it. *computed from model text: <keys>* names the model-derived `node.*` sources of the row's rooted key (`nodeId`) in the job's `resolve.json` `graph.edges` (kinds `reads`, `for`, `ret`; model-derived as `resolve.json`'s `nodes` declare, else as a record row says), passed from the content History already loads; with no graph the row says *model-derived*; an agent row says neither. *substituted, not sent* and *no single classifier* are constants of `HistorySentences31` (labels from part 08 and R4, not registry sentences). A sub-row repeated per template `pass` carries `id="row-<id>"` on its first occurrence only (an id must be unique in the page) and shows its `pass` on each; a node's run spots use the classes `history31-node-spot*`, apart from Run facts' `history31-spot*`. `Record31Fixtures` gained `NodeRow` and `DecisionRow` (the engine's writers, the decision's two flags given).
- Task 19: `Checks31`/`CheckRow31` follow Nodes' pattern (collapsed row draws only its line: title, key, stored status, the production item it is aligned with as a link, a skipped check's `stateReason`; the four lines, the text fetch (once, on opening) and the decisions only once opened) with their own open set and `#row-<key>` handling; `RecordView31`'s `RowLinks31.Reveal` tries Nodes, then Checks. A line with nothing to say is not drawn (`effect: null` shows no effect line, a skipped check no result line); `pass` is shown as `pass`/`fail`, `passed`/`total` as `7 of 9`, `failWith` only when failed. A `reads` path links when it, or its longest leading part cut at a `.`, is a node, check or deliverable row; an input path is shown unlinked. `uncovered`/`untested` come from the retained text (`text.uncovered`/`text.untested`) while retained, else the row's hash and size and A12-002 (only when a hash exists); a failed fetch says `RecordNotAvailable` beside them. The Checks order is `NodeTree31.BuildChecks`'s (by `startedAtUtc`, else key), never re-sorted. `RowLinks31` gained `JobId` (set from `Detail` on each parameter set) and `RowLink31`'s `href` is `/history/<jobId>#row-<key>` (Task 18's review finding; Nodes, Run facts and banners gain it), the click handler staying as the enhancement (its `preventDefault` is static in Blazor, so a ctrl-click is still taken by the handler; middle click and "open in new tab" use the href); `SubRows31`'s two bare `#row-` links now use `RowLink31`. The five existing href assertions moved to the full path. The fixture needed no extension: `Job()` already holds a check loop with item rows, and the tests add rows with `Record31Fixtures.NodeRow`.
- Task 19 (fix round 1): the disclosure is one piece, `Shared/History31/Disclosure31.cs` (open set, parents, `FocusKey`, `Toggle`, `Reveal`, `#row-` on arrival and on `LocationChanged`, a decision's owner and a sub-row's row found as Nodes did) plus `DisclosureRow31<TView>.cs`, a base component for a row (the `Node` and `View` parameters, `IsOpen`, `ControlsId`, its text fetched once on first opening with `Fetched`/`FetchFailed`, focus, and an `OnOpenedAsync` hook). A section builds its tree, creates `new Disclosure31(Navigation, () => InvokeAsync(StateHasChanged))` (Nodes derives a `View` from it to add `ModelSources`), sets `Texts`, calls `SetTree(record, tree)` when its record changes and `Arrive()` after the first, exposes `Reveal`, disposes it, and is added to `RecordView31`'s `RowLinks31.Reveal`; each row component says `@inherits DisclosureRow31<ItsView>`, puts `@ref="Element"`, `id="row-@Node.Key"`, `tabindex="-1"` and `aria-controls="@ControlsId"` on its element, and draws its own line and body (Deliverables: flat tree of `deliverable.*` nodes with the delivery rows as children or drawn inside the body). `aria-controls` is rendered only while the row is open, in both row components. A check's "beside" link names the aligned item as Nodes does (its own name, else the retained name fetched through the shared `RowText31`, else its position), the fetch made when the check row is opened. `Nodes31.View.Reveal` is removed (`Disclosure31.Reveal`).
- Task F9: the weekly summary's `OutstandingHandEdits` takes `EngineLimitSettings` and counts at `PublishLimits` only (it writes no snapshot, so `RunLimits` is not given there); `MigratePackage` also passes `limits.Limits` to `MigrationReport.WorstCaseNote` (its existing `limits` parameter), which the ruling's "all count at the configured limits" covers though the brief's call-site list named it. Not done here: the PR's minors (#1535 § 6, first bullet) live in the PR body, not the repo.
- Task F7: `AnswersSent` reads 30's text before its answer line with its end trimmed (`old[..^line.Length].TrimEnd()`, not the brief's untrimmed slice), as 31's `WorkflowCustomPrompt.Compose` trims the rendered prompt's end before the trailer; without it the prompt's own last line ending (`…}}\n`) stays a remainder. The goldens turned red by F7: `MigratedPackagesTests.TheMigratedSource_IsTheGolden`, `ByteEquivalenceTests.TheReport_IsTheGolden`, `HandEditsTests.TheReport_IsTheGolden`.
- Task F2: `ByteEquivalenceTests.ADifferenceNoRuleExplains_IsUnexpected` used the encounter date as its unexpected difference, which is now equal; it builds its difference from a captured prompt with an added line instead. `MigrateNodesTests.TheRenderedFormat_IsARenderedNode_OfATextFormat_ItsTemplateKept` expects the date in 30's form. No golden turned red that F7 had not (the same three are red).
- Task F6: A10-085 is said when a `trim` was written on the comparison (`TrimInputReads` wrapped a read), not on "`reads` hold an input read": an input read that is not text (a number inside `cat`) gets no trim and differs from 30 in nothing. A read the author already trimmed (`| string.strip`, a `trim` as written) gets no second `trim` and is no A10-085 case (30 trimmed it at both ends too). A10-085's `<comparison>` is the printed comparison, as A10-074's. Part 10 § 2.3 and the twin § 10.10 gain a refusal bullet for A10-085, and `package-format-library.md`'s converter note and A10 range say it. No vector case differed (`TheUnruledDifferences_AreTheseOnes` green), so no `trim-both-ends` reason. No hand-maintained fixture moved. Goldens turned red by F6: `ConditionConverterTests.TheGolden_IsWhatTheConverterGives`, `MigrationVectorsTests.ThePinnedVectors_AreWhatMigrateGives` (five of six red; `ConditionEquivalenceTests.TheGolden_IsWhatTheRunGives` stays green).
- Task F6 (fix round 1): an item of a loop over an input's list is an input's text (30's `NormalizeValue` trimmed it per element and per field): `SitePlace` and `ConversionScope` gain a trailing optional `ItemIsInputElement` (set where the innermost loop iterates `input.<id>`, or a list field of an outer such item; false for a node's list and a snippet folder), and `item.value…` text reads there are trimmed and refused (A10-085) as an input's are. Not in the findings: such an item compared with blank text (`item.value.y == ""`) is refused with A10-076, as `input.x.y == ""` is, since 30 read its blank text as `""`. A10-074 keeps one id and one sentence whose last clause (", and an input's text is trimmed at both ends first, where 30 trimmed its end") is said only where a `trim` was written (`BlankConversion.Trimmed`, a trailing init member); the three places write that clause bracketed, as A10-086's optional clause is. Part 10 § 2.2 says that production's 31 start already normalises inputs as 30 did (`V31JobStarter` → `NormalizeInputs` → `CanonicalText.Normalize`), so the written `trim` changes production only through leading white space. Hand-maintained fixture moved: `hand-edits/composer-macros/macros/section_close.md` (its `if` gains the `trim`). Goldens red: the same five; none newly turned red.
- Task F8: 30 refuses a deliverable source that names no node or names a check (`NoPackage30Accepts_HasASourceTheMigrationDoesNotRead`), and every other source is a node whose `.state` the deliverable's `if` reads, so no package reaches A10-086 or the `.value` fallback; `ASourceTheMigrationDoesNotRead_IsAHandEdit` reaches them through a seam (the pass run by hand with entries taken out of `Migrated.NodeReads`), not the brief's reshaped manifest with a check source. When a source is unread and 30's condition was refused by the converter, the `if` gets A10-086 alone (its `joining <term>` clause names the others' term), not A10-084 as well. `ConditionTyping.ReadsAtDeliverableIf` checks the boolean type itself, though the checker already refuses a non-boolean at an `if` site. `package-format-library.md`'s A10 range says A10-086. No migrated output moved: the same 18 cases of the five red goldens fail before and after F8, and no golden turned red by F8 (`ConditionEquivalenceTests.TheGolden_IsWhatTheRunGives` stays green).
- Task F3: the source piece is stripped as briefed; `ExpectedDifferences.WhileNoPassSeparator` looks for the stripped tag as well as the bare one (a hand-written template reads the bare). Two edge pinned tests moved (`edge-items-not-run` `deliverable.problem_list`, `edge-unruled-whitespace` `deliverable.problems`: the final line ending is gone), and `EdgeCasePackagesTests`' edit of `note.md` replaces the stripped tag. The sites still unexplained after F3 for difference 4 (loop items 30 trimmed; the owner chooses between a rule `loop-items-trimmed` and a `rendered` node per loop's `ret`, see **Blocks**): in `composer-sources/awkward-texts` the prompts `node.opening`, `node.closing`, `node.plan_gate.draft_plan`, `node.tone_case.plain_line` and the deliverables `topics`, `plan`, `tally`, `topics_then_opening`; and `deliverable.tally` of `composer-sources/item-names-and-bounds`. (`plan` and the second `tally` are not in the Blocks list; they are the same cause.) Sites unexplained for other causes (the hand-edit deliverables of `composer-macros`, `example-structured-intake` and `example-classifier-scope` prompts, `edge-items-not-run` and `edge-unruled-whitespace`, which F1, F4 and F5 take) are not F3's.
- Task F1: the gates live in a new partial `Migrate.Gates.cs`; besides `Migrated.DeliverableGates` and `AlwaysValued30`, `Migrated.GatingDeliverables` (leaf id → the gating deliverables' ids) carries A10-087's `<ids>` to `WriteNode`. A leaf that is itself in K (a classifier or a read inside a gated top-level node) is not gated, though its top-level node is; no fixture has the case. Code over brief: a 31 `for`'s `index.json` has seventeen members (`SourceShapes.FlowFor`), not sixteen, so the test asserts the shape's members and no `if`. `TwoConditionalDeliverables_GateTheirSharedNode_ByOr` removes `single` and `summary_then_opening` (unconditional readers of `opening`, which would keep it in K); `AWhile_IsNeverGated` gives `summary_then_opening` a condition too, else `refine` is in K whatever the while rule; `AClassifierAndWhatADeliverablesConditionReads_AreNeverGated`'s second case is `composer-sources`' `closing` read by `tally`'s condition (30 refuses a condition reading a loop, and `scope` as prose cannot be read at `.value`). `TheGatedPackages_SendOnlyThePrompts30Sent` compares only scenarios 30 completed: `example-structured-intake` `all-optional-inputs-unset` was refused by 30 before any model call (`empty-input-list-runs`), so its 31 prompts are not a gate's. Two existing tests moved with the behaviour: `ByteEquivalenceTests.ATextOneSideLacks_HasTheEmptyTextsHash` took its 31-only prompt from `classifier-scope` `decline_letter` (now gated) and now takes `all-optional-inputs-unset`'s `note_section:assessment`; `EdgeCasePackagesTests.AnAnswerOutsideTheSet_IsRefusedAtBoth` expected `failed-job-dispatch-order` on `problem_draft`'s prompts, which 31 no longer sends (`note`'s condition reads `triage`, so `problem_draft` takes its `if`); the rule keeps its unit tests. No clean package gained a hand edit (R17's guards: no fixture template reads a gated leaf). Hand-maintained fixture moved: `hand-edits/example-structured-intake/nodes/sections_senior_review_section/senior_review_section.md` (its `if` is `senior_review`'s), and that README's answer says so. `package-format-library.md`'s A10 range says A10-087. Goldens red: the same five (38 cases, was 18); none newly turned red; `ConditionEquivalenceTests.TheGolden_IsWhatTheRunGives` stays green.
- Task F4: `ExpectedDifferencesTests.ALoopItemThatDidNotRun_IsExpected` expects `deliverable.problem_list`'s rules to be `["escaped-plain-text", "loop-item-not-run"]`, not the brief's `["loop-item-not-run"]`: the deliverable reads the plain-text `problem_lines` as Markdown, so its text is escaped too (the render's rules are `["loop-item-not-run"]`). Since F3 the deliverable reads `{{ node.problem_lines.content | string.strip }}`, so `escaped-plain-text` no longer found the escaped render whole (its trailing line ending is stripped) and left the template's own `\-` and `\:` escaped: `RunValues` now holds each rendered and computed node's text trimmed as well as whole (part 10 § 3.2 and the twin § 10.11 say so); across every report only `edge-items-not-run` moved. A run of lines only 30 has counts as one item against the loop's items not completed (a run of two adjacent items not run is one); an item row is any row of the loop's entry key whose key is `<loop key>:…`. `TheMigratedPackage_IsByteForByte30s` accepts a text equal or expected by `loop-item-not-run`, as `Unruled` no longer lists Q4's two sites. `EdgeCasePackagesTests.Q4` is deleted; `edge-tests/edge-items-not-run.json` did not move (F3 had set it). Goldens red: the same five (38 cases); none newly turned red; `ConditionEquivalenceTests.TheGolden_IsWhatTheRunGives` stays green.
- Task F5: `AnEmptyReadsTrailingSpace_IsExpected` expects `deliverable.problems`' rules to be `["macro-trailing-whitespace", "before-after-macro-marker", "empty-read-trailing-space"]`, not the brief's `empty-read-trailing-space` alone: problem_close's white space in the same deliverable is R5's and the engine placement's, as before (`deliverable.letter` is `["chosen-macro-final-line-ending"]` alone, as briefed). `empty-read-trailing-space` reads the snippets the row places, itself or through a rendered node it reads (as `LoopItemNotRun` walks reads), and a snippet line counts only with literal text before its white space (31's text before the run must end with it, which is what tells a read's trailing space from one after literal text); at the text's end, where 30 has no aligned run, the white space is taken where that brings the texts nearer. `chosen-macro-final-line-ending` takes a body's final `\r\n` as one line ending too, and a body whose text before that ending has trailing white space of its own keeps it at both. `EdgeCasePackagesTests.Unruled` is deleted; `TwoWhiteSpaceDifferences_NoRulingCovers_AreShown_…` is renamed `TwoWhiteSpaceDifferences_AreExpected_AndAnAfterMacrosWhiteSpace_IsR5s` and asserts both sites expected. `edge-tests/edge-unruled-whitespace.json` did not move (its `expect` already held 31's texts). No migrated output moved; goldens red: the same five (38 cases); none newly turned red; `ConditionEquivalenceTests.TheGolden_IsWhatTheRunGives` stays green.
- Task F10: two sites the first regeneration left unexplained were F3's and are fixed before the one regeneration is committed. (a) `reply-never-a-template` replaced a served reply only whole, and since F3 a deliverable reads it stripped, so `composer-sources/awkward-texts` `deliverable.plan` lost the rule: `RenderedReplies` also puts back the reply stripped by 30's render stripped (`ExpectedDifferencesTests.ReplyNeverATemplate_…`, red then green); the site is now expected. F3's record put `plan` among the loop-item sites; it was this. (b) `hand-edits/composer-macros/deliverables/gated.md`, `optional.md` and `echoes.md` (hand-maintained) still read `{{ node.letter_body.content }}` bare, so their 11 sites stayed unexplained (difference 4); they read it with `| string.strip`, as the migrated file the operator edits now does, and are equal. `failed-job-dispatch-order` keeps a golden hit (`composer-sources/failed-item`, six sites) though `edge-answer-outside-set` no longer gives one, so no named reason was added. Production's dates: 30's custom-prompt renderer (`PromptTemplateRenderer.Render`, l.76–86) sets Scriban's date format to `%Y-%m-%d` (#357), but a 30 package's prompts, nodes and deliverables render through `TemplateRenderer.RenderWith` (`ContextWith`, l.787–792; `ConsultRenderPayload` l.123), which keeps Scriban's default `%d %b %Y`, hence the captures' `09 Nov 2026`. `package-format-library.md`'s node row now says A10-064 as F7 built it.
- **Part F, what moved** (items 1 to 9 of #1508's rulings of 2026-11-26 built; item 3 in part, a loop's items being **Blocks**; regenerated once in F10, `e167fd03`). **`migrated/`, 86 files:** (the per-item file counts overlap: `notices.json` is counted under items 1 and 7, the conditional-documents prompts under items 1 and 2) item 1, 13 files in 4 packages (the leaf `if`s and A10-087 notices: `edge-answer-outside-set` `problem_draft`; `example-classifier-scope` `decline_letter`, `information_request`, `plan_section`; `example-conditional-documents` `consult_note_section`, `progress_note_section`; `example-structured-intake` `index_note`, `long_stay_section`, `senior_review_section`; each package's `notices.json`); item 2, 3 files in 2 packages (`example-conditional-documents`' two section prompts, `example-v12-constructs` `patient_header`); item 3, 60 deliverable files in 23 packages (every deliverable source piece; `composer-macros` 11, `composer-sources` 10, `example-structured-intake` 5, `composer-gates` 4, `example-classifier-scope` 3, two each in `edge-items-not-run`, `edge-no-branch`, `edge-unruled-whitespace`, `example-conditional-documents`, `example-conditional-sections`, `example-macros`, `example-macros-warnings`, `example-two-documents`, `example-v12-constructs`, one each in `edge-answer-outside-set`, `edge-flag`, `edge-missing-marker`, `edge-unset-object`, `example-nested-note`, `example-prior-letters`, `example-problem-list`, `general`, `test/general`); item 6, 2 files (`composer-macros` `macros/section_close.md`'s `trim` and its A10-074 notice); item 7, 8 files in 4 packages (`triage`, `scope`, `follow_up`, `tone` without 30's answer line, and A10-064's words in each `notices.json`); the snapshot tests that follow, 4 (`edge-answer-outside-set`, `example-classifier-scope`, `example-conditional-documents`: fewer or changed prompts; `edge-items-not-run`: the stripped `problem_list`). Items 4, 5, 8 and 9 moved no migrated file. `conditions/converted.json`: 3 cases gain `trim` (item 6); `conditions/equivalence.json` did not move. **`reports/` counts** (explained/partly/unexplained, before → after): `composer-macros` 26/9/26 → 34/1/2; `composer-sources` 47/3/7 → 48/2/6; `edge-answer-outside-set` 4/0/0 → 2/0/0; `edge-items-not-run` 0/1/1 → 2/0/0; `edge-unruled-whitespace` 4/1/1 → 6/0/0; `example-classifier-scope` 5/0/11 → 5/0/0; `example-conditional-documents` 0/0/20 → 0/0/0; `example-structured-intake` 15/0/24 → 11/0/0; `example-v12-constructs` 23/6/16 → 29/0/0; `edge-flag` 5/0/0, `example-nested-note` 16/0/0, `example-prior-letters` 21/0/0, `example-problem-list` 6/0/0 unchanged (explanations only: `sent-format`'s widened words). **Became equal or expected, by item:** difference 1 (item 1): the 45 prompts only 31 sent are no longer sent (`example-classifier-scope` 11, `example-conditional-documents` 10, `example-structured-intake` 24), nor 6 that were expected (`example-structured-intake` `all-optional-inputs-unset`'s 4, `empty-input-list-runs`; `edge-answer-outside-set` `problem_draft:1`, `:2`, `failed-job-dispatch-order`); difference 2 (item 2): 32 texts, `example-conditional-documents`' 10 section prompts equal, `example-v12-constructs`' 8 `patient_header` renders and 8 `extract_note_terms` prompts equal and 6 `consult` deliverables expected (`escaped-plain-text`); difference 4 (item 3): `composer-macros` 24 deliverables equal (`appended`, `both`, `prepended` and `gated`, `optional` ×4, `echoes` ×3, `review_summary`) and 8 expected (`authored` ×3, `signed_letter` ×3, `signed_only` ×2, `escaped-plain-text`), `composer-sources/awkward-texts` `pair` equal and `plan` expected (`reply-never-a-template`); the four edge sites: `edge-items-not-run` `node.problem_lines` (`loop-item-not-run`) and `deliverable.problem_list` (`escaped-plain-text`, `loop-item-not-run`) (item 4), `edge-unruled-whitespace` `deliverable.letter` (`chosen-macro-final-line-ending`) and `deliverable.problems` (… `empty-read-trailing-space`) (item 5); item 4.3 (item 7): the 17 classifier prompts (`triage` 2, `scope` 4, `follow_up` 3, `tone` 8) now end with the format's instruction alone, still expected under `sent-format`. **Still unexplained, with cause:** a loop's items 30 trimmed (**Blocks**, for the owner: a rule `loop-items-trimmed`, or a `rendered` node per loop's `ret`): `composer-sources/awkward-texts` prompts `node.opening`, `node.closing`, `node.plan_gate.draft_plan`, `node.tone_case.plain_line` (each reads `node.topic_loop.content`, its items `"History paragraph.\n\n"` and `"   Examination paragraph, indented."` untrimmed) and deliverables `topics`, `topics_then_opening` (unexplained) and `tally` (partly, `escaped-plain-text`; 85 against 80); found here: `composer-sources/item-names-and-bounds` `deliverable.tally` (partly, `escaped-plain-text`; 404 against 414) is not a loop item's white space but the position name, `Topics 4` against 30's `Requested topics 4` (`position-names-from-the-loop` credits the name in `topics`, but not its length counted by `topic_tally`'s `string.size`); `composer-macros/no-signature` `signed_letter`, `signed_only` (unexplained) and `authored` (partly, `escaped-plain-text`): with no signature block 31's `{{ profile.signature }}` reads empty and leaves the blank line before it (`signed_*`) or the snippet's line ending (`author_line`) at the text's end, where 30 appended nothing; no ruling covers it, for the owner. **Vectors** (`v31/conformance/migration-vectors`, 35 files in 8 cases: `composer-gates` 4, `composer-macros` 13, `composer-sources` 10, `example-conditional-sections` 2, `example-macros` 2, `hand-built` 2, `example-nested-note` 1, `test-general` 1): item 3's strips and item 6's `section_close` `trim` and notice; `index.json` did not move; content repo `eb209ec`. **The pin:** `external/consultologist-package-format` `fc2738b` → `eb209ec` (`m39-build`, with the twin commits 407fd0b, 513d8ae, e575ba1, dee3167, 5483efe, 0b263f1, c19d661, a0fc1b6, 4b4e3eb).
- Task F10 (fix round 1): `reply-never-a-template`'s explanation, part 10 § 3.2 and the content repo's § 10.11 now say the rule also finds a reply 31's text holds stripped, as a migrated deliverable reads a source (#1508 item 3), not only as written; one near-miss test (a stripped reply where 30 kept something else is not credited); the one golden report that credits the rule (`reports/composer-sources.json`) moved in that explanation text only. The content repo pin moved a second time, `eb209ecc` to `93c1e229`, an exception to F-D18 so the pinned twin says what the code does.
- Task 20: `Deliverables31` builds a flat tree (each deliverable in key order, named by its label else its key, its delivery rows as children) on the shared `Disclosure31`, and `RecordView31`'s `RowLinks31.Reveal` tries Nodes, Checks, then Deliverables, so a banner's link and a check's `checked.ref` now open the row (the Inputs test that followed the fragment for `deliverable.refused` asserts that instead). A destination is a row component of its own, `DeliveryRow31` (a file the brief does not name), so a link to `delivery:…` opens its deliverable and lands on it; it fetches no text. The template is `resolve.json`'s `deliverables.<key>.body` (part 07 § 2.5), shown when `CanonicalJson.HashText(body)` equals the row's `templateHash`; otherwise (no content, no entry, no `templateHash`, another hash) the row says **A12-007** *"The template this job ran couldn't be read, so it isn't shown."*, added to `refusal-sentences.md`, `V31Contract.cs` and the area table. Sites keep the row's order; a substitution naming a site the row does not list is kept after them; a site with a `loop` and more than one substitution is collapsed to *× n passes* (a button with `aria-expanded`); a read's size reads *<n> characters* (runes). The check line is *Check: <id> passed/failed* from the check row's `pass`, its stored status when it gave none (a skipped gate). A bundle is the entry's `bundle` in `resolve.json` (none when the content was not read): a released deliverable of a bundled folder (its key minus the last segment) lists the folder's other deliverables not `released`, each with its stored state and reason. Copy note is the Create page's mechanism (`navigator.clipboard.writeText`, *Copied ✓* / *Copy failed* for 2 s) in the row, for a `released` text only; a held, failed or skipped retained text is shown without one. A `blocked` deliverable's text is drawn by `RefusedText31` alone: its reads show size and hash but no slice, and the panel also cancels the copy and cut events. `Record31Fixtures.Job()`'s deliverable rows gained `composedHash` (Task 13's deferred note); no existing test changed for it.
- Task 21: the card loads and saves its own settings (it is a component with `IAccountEndpointService` injected; `Profile.razor` only places it after the signature card). One Save writes each field whose trimmed text differs from what is saved (nothing for an unchanged field; a blank changed field is deleted); the closing line keeps its inner line ends, only its ends are trimmed. The Api's 400 for a value over a limit is plain text naming the key and the limit, and `AccountEndpointService.SaveSettingAsync` did not carry it (its exception said `BadRequest`): it now throws `HttpRequestException(<the text>, null, BadRequest)` when a 400 has a body, as `GetStartChecksAsync` does, and the card shows that message; any other failure says *"We couldn't save your letter details. Please try again."* The other cards catch `HttpRequestException` and show their own copy, so they are unchanged. `TheLimits_MirrorTheApis` checks the keys against `AccountSettingKeys` and the limits against `Account.ValidateProfileSetting` (at the limit passes, one over is refused) and `Account.Max*Length`.
- Task 22: the card loads and saves its own setting (`TimeZoneCard`, placed first under Account). Picking a zone only selects it; Save writes it, or deletes the setting when the select is empty (and does nothing when empty and unset). "Use this browser's zone" calls `window.consultologistBrowserTimeZone`, which is in `wwwroot/js/download.js` (the Profile page has no JS file of its own; this is the existing small page-utilities file); a browser zone the list lacks selects nothing and the card says *"This browser's time zone isn't one we can offer. Choose one from the list."* (a Web sentence naming no value). A saved zone the runtime's list lacks is kept as an extra first option, so Save does not clear it. The Api's 400 for a zone is carried by `SaveSettingAsync` (Task 21's change) and shown as written. `TimeZones.Iana()` is `GetSystemTimeZones()` with `HasIanaId`, distinct, ordinal order; `EveryZoneTheCardOffers_IsOneTheApiAccepts` checks each against `V31StartChecks.IsZone` on the test machine. Whether the published WASM app's list is empty is still for Task 25's E2E to check; if it is, the `Intl.supportedValuesOf('timeZone')` fallback in the brief applies and is not built here.
- Task 23: `dag.mmd` is the diagram, not a source file: `V31PackageView` lists it once, as "Diagram" (last), drawn by the Graph pane's `WorkflowDagView`; the other files are listed by path. The two evidence views are pretty-printed with System.Text.Json (indented, relaxed escaping, so text is shown as written). `Owned` is the page's `EditingOwnPackage` (an account package loaded from the registry; never an import). The download is `PackageDownload31.Zip` (each entry's bytes, UTF-8 without a byte-order mark), not `WorkflowPackageArchive.Zip`, and carries no `migration-report.json`. A refused 31 import's `errors` reach the page through a trailing `Errors` on `WorkflowIngestOutcome` (the Api's 400 `{ error, errors }`); the import bar lists every one, and is unchanged when there is no list. A 31 import is not held in this browser for a reload (it has nothing to name or publish), drops the import held before, and says no "Imported …" notice: the view's bar and its `Notices` say what it is. At 31 the bar does not read the title or `derivedFrom` (a 30 reader), and the "not your package" and "publishable but not yet runnable" bars are not shown (both are about publishing). Two existing tests pinned editing a manifest raised to 31 and its refusal at publish; under D14 that package opens read-only, so their 31 halves assert the view instead: `TemplatesV27LoopPaneTests.A27Package_PublishesClean_AndTheVersionAfter_MeetsTheVersionSentence` → `…_AndTheVersionAfter_OpensReadOnly`, `TemplatesV28ReadWriteTests.TheVersionAfter_31_IsRefusedAtPublish` → `TheVersionAfter_31_OpensReadOnly`.
- Task 24: `V31PackageView` lists "Publish report" (when the account owns the version and the content holds it, after `lock.json`) and "Migration" (when the content holds `MigrationReport` and `MigratedFrom`), both before the diagram; a package's reports are read leniently through `ReportJson` (a missing member or wrong kind shows what there is). The test results show id, result, `outcomeHash` and the first mismatch's four members; the duration and `fileHash` are not shown. A compared text's standing is the word `equal`, `expected` or `unexplained` from its flags (nothing else is composed); the migration report's `options` are not shown. No page path publishes a 31 package today (a loaded 31 package is read-only): `WorkflowPublishOutcome` gains a trailing `Report` (the response's `Report`, or a 400's `report`), `Templates.PublishAsync` holds it in `publishReport` and shows `PublishReport31` after the warnings and errors; it is tested through a 30 package whose publish answer is mocked to carry a report. A 400's `error` is the first of its `errors`, which the page already lists, so it is not shown twice. No new sentence (no `A12-` id taken). The Task 23 test `ItHasNoPublish_…` now excludes the file-list buttons, and `EverySourceFile_IsListed_…` expects "Publish report" after `lock.json`.
