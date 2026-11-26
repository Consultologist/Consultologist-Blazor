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

- [ ] **Step 1: Failing bUnit tests.** `A31Job_ShowsResolveHashAndLockHash_BesideThePackage`; `…_ShowsEveryLimit_WithItsName` (the ten of part 08 § 3); `AMigratedVersion_ShowsS09004_LinkingToTheEditor` (D10); `Banners_AreAtTheTop_EachLinkingToItsRow`; `TheFiveSections_AreInTheOrderTheJobRuns` (Inputs, Run facts, Nodes, Checks, Deliverables); `Delivering_IsShownAsDelivering…_AndPolledFast` (`FastPollInterval`, as `HistoryDuplicateRowsTests` drives it); `AStatusIsShownAsStored` (`Completed`, `Failed`, `Cancelled`, *Waiting for an update to finish*); `AStoppedJob_SaysA12001_IsNotPolledFast_AndOffersNoDownload` (D9; the page takes its clock from an injected `TimeProvider`, registered in `Program.cs` and the test context); `DownloadRecord_SavesTheRoutesBytes_UnderItsFileName` (the existing `consultologistDownload` interop); `…_IsOfferedOnlyForATerminalJob`; `ADownloadThatFails_ShowsTheRoutesSentence`; `Verify_ChecksTheRecordHash_AndReplaysEachDeliverable` (lines from `RecordCheck31`); `Verify_WhenTheRecordCannotBeFetched_SaysA12005`; `Rerun_IsOffered_WhenTheInputsAreHeld` (Task 4's `HeldInputs`); `TheRowsAreListedOnce_PerLoad`; `A30Job_RendersExactlyAsBefore` (an existing detail test unchanged, plus one asserting no `History31` markup); `AJobWhoseRowsCannotBeListed_ShowsTheHeader_AndTheRoutesSentence`.
- [ ] **Step 2–4:** fail, implement, run both suites.
- [ ] **Step 5: Commit** `Web (#1509): History opens a storage 12 job: its header, banners, state and record`; push.

### Task 17: Inputs and Run facts

**Files:** Create `Shared/History31/Inputs31.razor`, `Shared/History31/RunFacts31.razor`; Modify `RecordView31.razor`, and `src/Consultologist.Web/Services/AI/AIEndpointService.cs` if the job response does not yet mirror the header's `inputs` (add trailing `JsonElement? Inputs31` only if the Api response carries them; if the Api's `ToResponse` does not, add the header's `inputs` to it as a trailing member named as the Api names its 31 members, with a test in `tests/V31Engine/RecordRoutesTests.cs`); Test `tests/Consultologist.Web.Tests/V31/History31InputsTests.cs`.

- [ ] **Step 1: Failing tests.** `EachInput_ShowsItsOrigin_AndHash`; `ADocument_ShowsItsKind_Extractor_Parser_Converter_AndOcr` (`kind` `image` and `ambient-note` shown although `origin` is `document`, R7; a `null` step is not listed); `AnInputOfSeveralElements_ListsThem_NumberedFromOne`; `APreviousRunElement_LinksToItsJob_AndSaysItCameFromAModel` (D7); `RunFacts_ShowEachFactRead_WithTheZoneBesideTheDate` (`run.date: 2026-11-16`, `America/Toronto`); `NoFactsRead_SaysSo`; `EachRunSpot_IsListedUnderTheFactItReads_WithItsBodyKey_Site_AndOutput` (gathered from every row's `runSpots` and macro rows' `runSpots`); `ASpotWhoseOutputIsNotInTheRow_ShowsItWhileRetained_ThenItsHash`; `ABodyWhoseTwoHashesDiffer_SaysA12003`.
- [ ] **Step 2–4:** fail, implement, pass. **Step 5: Commit** `Web (#1509): History's Inputs and Run facts at storage 12`; push.

### Task 18: Nodes

**Files:** Create `Shared/History31/Nodes31.razor`, `Shared/History31/NodeRow31.razor`, `Shared/History31/Decision31.razor`, `Shared/History31/SubRows31.razor` (renders, macros, units, run spots); Modify `RecordView31.razor`; Test `tests/Consultologist.Web.Tests/V31/History31NodesTests.cs`.

**Interfaces — Consumes:** `NodeTree31`, `RowText31`, `HistorySentences31`. Every row's element has `id="row-<key>"`; a link to a row is `#row-<key>` and opens its ancestors' disclosures.

- [ ] **Step 1: Failing tests.** `EachNode_IsARow_ByItsKeyWithRoot`; `ALoop_ExpandsPerItem_NamedByItsLabel_AndByPositionAfterTheDrop`; `AWhile_ExpandsPerPass`; `ANotRunItem_ShowsItsStatusAndReason`; `ADecision_ShowsThePrintedCondition_ItsResult_AndTheSkipSentence` (*skipped: input.kind.value = "table" is false*, the row's `stateReason` verbatim); `ACaseDecision_ShowsTheArmTaken_AsStored` (`2`, `else`; R8); `ARunDependentDecision_IsMarked_AndWhatItChoseIsNot`; `DecisionsOfANodeThatNeverRan_AreUnderIt_AndItSaysNotRun` (R4); `RendererSubRows_AreUnderTheirNode_ById_WithRenderer_InputHash_OutputHash_AndPass` (a chain shows two; a gap in numbering is kept); `AMacroRow_ShowsOfferedAndPlaced_LinksItsDecisions_AndItsContentHash`; `AMacroWithNoClassifier_SaysNoSingleClassifier` (R4); `AMacroWithAClassifier_LinksToThatRow`; `AnInvalidOutcome_IsOnTheNodesRow`; `AnAgentRow_NamesItsDefinitionAndVersion_AndTheBinding`; `KeywordsRemoved_AreListed_AsNotSentToTheModel`; `AUnitNotSent_SaysSubstitutedNotSent`; `AModelDerivedComputedRow_SaysComputedFromModelText`; `Retries_ShowHowMany_OfWhichKind_AndTheTotalWait`; `ACall_IsOneRowThatExpands_AndNamesItsFunction`; `OpeningARow_FetchesItsTextOnce`; `NoTextIsFetchedBeforeARowIsOpened` (Review Focus 3); `ARowWhoseTextWasDeleted_ShowsItsHashes_AndA12002` (Review Focus 1); `AFailedNode_ShowsItsStoredReason_AndRunAgainLater_ForTheModelServicesLimits`.
- [ ] **Step 2–4:** fail, implement, pass. **Step 5: Commit** `Web (#1509): History's Nodes at storage 12`; push.

### Task 19: Checks

**Files:** Create `Shared/History31/Checks31.razor`, `Shared/History31/CheckRow31.razor`; Modify `RecordView31.razor`; Test `tests/Consultologist.Web.Tests/V31/History31ChecksTests.cs`.

- [ ] **Step 1: Failing tests.** `ACheckRow_HasFourLines_Checks_Reads_Result_Effect`; `Checks_NamesWhatItRanOn_WithThatOutputsHash_AndLinksToIt`; `Reads_LinkEachToItsRow`; `Result_ShowsPassOrFail_TheCounts_AndFailWith` (`7 of 9`, the `failWith` sentence when failed); `UncoveredAndUntested_AreShownWhileRetained_ThenTheirHashAndSize`; `Effect_IsTheStoredWord` (`released`, `blocked deliverable.note; released deliverable.summary`, `read by node.x`; `null` shows nothing); `ACheckLoop_ExpandsPerItem_BesideItsProductionItem_WithTheSameName` (links to `alignedWith`); `AnAggregate_ShowsItsFailWithSentence`; `ASkippedCheck_ShowsItsReason`; `AGatesRow_ComesAfterItsDeliverables_InTheOrderRecorded` (R8: no re-sorting of checks by key).
- [ ] **Step 2–4:** fail, implement, pass. **Step 5: Commit** `Web (#1509): History's Checks at storage 12`; push.

### Task 20: Deliverables, and the text a check refused (R1)

**Files:** Create `Shared/History31/Deliverables31.razor`, `Shared/History31/DeliverableRow31.razor`, `Shared/History31/RefusedText31.razor` (+ `.razor.css`), `src/Consultologist.Web/Services/Provenance/V31/TemplateReads31.cs`; Modify `RecordView31.razor`; Test `tests/Consultologist.Web.Tests/V31/History31DeliverablesTests.cs`, `TemplateReads31Tests.cs`.

**Interfaces — Produces:** `TemplateReads31.Group(Row31 deliverable)` → `IReadOnlyList<SiteReads31>`; `SiteReads31(int Site, string At, string Expr, string? Loop, IReadOnlyList<Substitution31> Passes)`; `Substitution31(IReadOnlyList<int>? Pass, string Reads, string Hash, int Start, int Size, string? Renders)`. The template's text is read from `Content.Resolve` at the deliverable's entry (the member part 07 § 2.2 names for a body; find it in a compiled golden under `tests/`), and shown only when its hash equals the row's `templateHash`; otherwise the row says the pinned version's template could not be read.

- [ ] **Step 1: Failing tests.** `ADeliverable_ShowsItsStoredState_Format_AndComposedHash`; `ItsTemplate_IsShownFromThePinnedVersion_WhenTheHashMatches`; `…_AndNotWhenItDoesNot`; `Reads_AreGroupedBySite_InSourceOrder`; `ALoopedSite_IsCollapsedToTimesN_AndExpandsPerPass` ("× 3 passes"); `EachRead_ShowsItsSizeAndHash_AndItsTextWhileRetained` (the slice `Start`..`Size` in runes of the composed text); `AProfileRead_ShowsTheFieldAndItsHash`; `TheSignatureRead_NamesTheBlocksId` (the header's `signatureBlockId` and `signatureChosen`); `TheCheckLine_ReadsCheckIdPassedOrFailed_AndLinksToTheChecksRow`; `AReleasedDeliverableWhoseCheckDidNotApply_ShowsItsStoredReason`; `EachDestination_ShowsItsStoredResult_ItsReason_AndItsAttempts`; `AFailedDestination_SaysCouldNotSend`; `ABundle_ListsWhatItLeftOut_AndWhy` (the folder's other deliverables that are not `released`, each with its state and reason); `AReleasedDeliverable_HasACopyControl`; **`ABlockedDeliverablesText_IsUnderRefusedNotDelivered_WithItsCheckAndSentence_AndHasNoCopyControl`**; `…_IsNotInAnyCopyOrDownloadThePageOffers`; `AfterTheDrop_TheHeadingAndTheHashRemain_WithA12002`; `ThereIsNoReleaseButton`.
- [ ] **Step 2–4:** fail, implement, pass. Correct part 09 § 6.4 "Deliverables" to name the heading (R1).
- [ ] **Step 5: Commit** `Web (#1509): History's Deliverables at storage 12, with refused text marked as refused`; push.

## Part D: Profile and the Editor

### Task 21: The "Letter details" card (D13)

**Files:** Create `src/Consultologist.Web/Shared/Profile/LetterDetailsCard.razor`, `src/Consultologist.Web/Services/Accounts/LetterDetails.cs` (the three keys and limits: `profile.credentials` 120, `profile.organisation` 200, `profile.closing` 4,000, mirrored from `AccountSettingKeys` and `Account.cs` l.546–548 and pinned by a mirror test); Modify `src/Consultologist.Web/Pages/Profile.razor` (under "Workflow preferences", after the signature card, l.884); Test `tests/Consultologist.Web.Tests/LetterDetailsProfileTests.cs`, `ProfileSectioningTests.cs`.

- [ ] **Step 1: Failing tests.** `TheCard_HasCredentials_Organisation_AndClosingLine_WithTheirControls` (two single-line fields, one text area); `ItLoadsWhatIsSaved`; `Saving_WritesEachChangedField_Trimmed_AsTextPlain`; `SavingABlankOrWhitespaceValue_DeletesTheSetting`; `EachField_StopsAtItsLimit` (`maxlength` 120, 200, 4000) `_AndShowsTheEnginesRefusalIfOneArrives`; `AnUnsetField_ReadsNotSet`; `TheLimits_MirrorTheApis`.
- [ ] **Step 2–4:** fail, implement, pass. **Step 5: Commit** `Web (#1509): Profile has a Letter details card`; push.

### Task 22: The account time zone (D12)

**Files:** Create `src/Consultologist.Web/Shared/Profile/TimeZoneCard.razor`, `src/Consultologist.Web/Services/Accounts/TimeZones.cs` (`IReadOnlyList<string> Iana()`; `const string SettingKey = "account.timeZone"`), a one-line function in the page's existing JS file for the browser's zone; Modify `Profile.razor` (under "Account", first card); Test `tests/Consultologist.Web.Tests/TimeZoneProfileTests.cs`.

- [ ] **Step 1: Failing tests.** `TheSelect_StartsEmpty_WhenNoZoneIsSet` (and nothing is saved on load); `ItListsIanaNames_InOrder`; `PickingAZone_SavesIt`; `UseThisBrowsersZone_FillsTheSelect_OnlyWhenClicked` (no interop call on render); `…_AndSavesNothingUntilSaveIsPressed`; `ABrowserZoneTheListDoesNotHold_IsNotSelected_AndSaysSo`; `ClearingTheSelect_DeletesTheSetting`; `UntilItIsSet_TheCardSaysS06008`; `TheEnginesRefusalOfAZone_IsShown`.
- [ ] **Step 2–4:** fail, implement, pass. If `TimeZoneInfo.GetSystemTimeZones()` is empty in the published WASM app (checked in Task 25's E2E), take the list from `Intl.supportedValuesOf('timeZone')` instead and record it.
- [ ] **Step 5: Commit** `Web (#1509): Profile sets the account's time zone`; push.

### Task 23: The Editor opens a 31 package read-only (D14)

**Files:** Create `src/Consultologist.Web/Shared/WorkflowEditor/V31/V31PackageView.razor` (+ `.razor.css`), `src/Consultologist.Web/Services/Workflow/V31/PackageDownload31.cs`; Modify `src/Consultologist.Web/Pages/Templates.razor` (`.editor-bar` toolbar ~l.84–250, the notices stack ~l.255–330, `.editor-layout` l.345, `DownloadPackageAsync` l.20211, `ImportPackageAsync` l.20237 and `ApplyImportedPackageAsync`, `LoadedSpecVersion` l.5413); Test `tests/Consultologist.Web.Tests/V31/TemplatesV31Tests.cs`, `EditorFixtures.cs` (`V31()`: a small fictional package's source files, with `Resolve`, `Lock`, `PublishReport`; `V31Migrated()` adds `MigratedFrom` and `MigrationReport`; `V31Imported()` has no evidence and one notice).

**Interfaces — Produces:** `V31PackageView` parameters `[Parameter] WorkflowPackageContentResponse Content`, `bool Owned`; left, a file list (`manifest.json` first, then `Files` by path, ordinal; then `resolve.json`, `lock.json` when present and `Owned`; then "Diagram" when `Files` holds `dag.mmd`); right, the selected file's text in a read-only `<pre>` (JSON pretty-printed for the three evidence views, source files as stored). `PackageDownload31.Entries(WorkflowPackageContentResponse)` → `IReadOnlyList<(string Path, byte[] Bytes)>`: `manifest.json` (`Manifest.GetRawText()`), every file, and `resolve.json`, `lock.json`, `publish-report.json` from their members' raw text when present.

- [ ] **Step 1: Failing tests.** `A31Package_ShowsTheBar` (*"This package uses the new format. You can view and download it; editing returns in a later release."*, word for word); `ItHasNoPublish_Upgrade_Undo_Discard_OrNewPackageControls`; `EverySourceFile_IsListed_AndOpensReadOnly` (no `textarea`, no input that is not the picker or the import); `TheManifest_IsShownAsPublished_NullsKept`; `ResolveAndLock_AreViewable_ForAVersionTheAccountOwns_AndNotOtherwise`; `TheDiagramEntry_AppearsOnlyWithADagFile` (area 13's seam); `Download_Includes_ResolveLockAndThePublishReport` (the zip's entry names, through the fixture's interop capture as `TemplatesDownloadTests` does); `ImportingA31Zip_OpensItReadOnly_WithItsNotices` (S07-016 shown); `AnImportThe31SourceCheckRefuses_ShowsEveryError`; `A30Package_StillEdits` (an existing test unchanged); `ANewPackage_IsCreatedAt30` (`NewestSpecVersion` is 30).
- [ ] **Step 2–4:** fail, implement, run both suites. `ProjectLoadedContent` and every 5–30 reader are skipped for a 31 content (they read a 30 manifest); nothing reads a 31 manifest with `WorkflowManifestReader`.
- [ ] **Step 5: Commit** `Web (#1509): the Editor opens a 31 package read-only`; push.

### Task 24: The publish report and the migration report in the Editor (D10, D11)

**Files:** Create `Shared/WorkflowEditor/V31/PublishReport31.razor`, `Shared/WorkflowEditor/V31/MigrationReport31.razor`; Modify `V31PackageView.razor` (two entries, "Publish report" and, when migrated, "Migration"), `Templates.razor` (`PublishAsync` l.18564: a response's `Report`, and a 400's `Error` and `Report`, are shown with `PublishReport31` beside today's warnings and errors); Test `tests/Consultologist.Web.Tests/V31/EditorReports31Tests.cs`.

- [ ] **Step 1: Failing tests.** `ThePublishReport_ShowsItsOutcome_Time_AndPublishedBy`; `…_ListsWarningsAndNotes_EachWithWhereItIs` (`sentence`, `at`); `…_ListsEachTest_PassOrFail_WithItsFirstMismatch` (hashes or plain words as the report holds them, nothing else); `…_ListsWhatWasNormalised`; `ARefusedReport_ListsItsRefusals`; `AMigratedVersion_SaysMigratedByTheOperatorFromItsOldVersion` (S09-004); `TheMigrationView_ListsHandEdits_Notices_AndTheCounts`; `…_ListsEachComparedText_EqualExpectedOrUnexplained_WithItsExplanation` (sites and hashes only); `AVersionThatWasNotMigrated_HasNoMigrationEntry`; `APublishAnswerCarryingAReport_ShowsIt`; `AReportWithAMemberMissing_StillRenders`.
- [ ] **Step 2–4:** fail, implement, pass. **Step 5: Commit** `Web (#1509): the Editor shows a 31 version's publish and migration reports`; push.

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
- Task 15: item and pass rows are the group nodes (built on `Record31.ParentOf`/`ChildrenOf`); an item's title is its row's `itemName`, else the retained name, else `<loop row's label> <item number from the key>` (the loop's last scope when no label is stored); a pass's title is `Pass <n>`; a node for an owner with no row has `Kind` `""`. A row whose item or pass row is missing sits on the nearest row present (`ParentOf`'s rule), so the fixture's `node.sections.draft:10` is a child of the loop.
