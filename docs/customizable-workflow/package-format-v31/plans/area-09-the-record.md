# Area 09: The Record (specVersion 31, record storage 12) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give a specVersion 31 job its record: every row, decision and delivery in part 08's shape, each written once to a table as it finishes; the job header's 31 members; the retained text beside the record; the canonical record document and `recordHash` sealed at every terminal state; the row, record and event routes that serve them; and the 7-day text drop.

**Architecture:** The runner (`src/Consultologist.PackageFormat/V31/Run/`) already settles every instance and decision through `IRunRecorder`; this area makes what it hands over part 08's rows (`RunRow`, `RunDecision`, `DeliveryRow` each give their record JSON), and adds `RecordDocument`, the pure assembler of the canonical record. In the Api, `StoredRunRecorder` replaces `EntityRunRecorder`: it buffers the rows of a round and writes them in one activity (`write-record-rows-v31`) to the table `ConsultGenerationRecordRows`, insert-if-absent; the producing activities write the text only they hold (the prompt as sent, renderer inputs and outputs, substitution values) to text blobs; the entity keeps the header and counts. `RecordSealer` assembles the rows into `record.json` and writes `recordHash` once, from the orchestration, the cancel path, the retention sweep, or the first read of a terminal job. The record holds hashes, ids, counts, reasons and package text; every retained text lives in the text blobs, so the sweep deletes blobs and never rewrites a row, and `recordHash` holds for the record's life.

**Tech Stack:** C# / .NET 10, `System.Text.Json` (`JsonNode`), Durable Functions (isolated), `Azure.Data.Tables`, `Azure.Storage.Blobs` through the existing `StorageTables` and `TextBlobClientFactory`, xUnit and NSubstitute in `tests/Consultologist.Api.Tests.csproj`.

**Spec:** `docs/customizable-workflow/package-format-v31/` — [`08-record.md`](../08-record.md) in full; [`09-engine-and-pages.md`](../09-engine-and-pages.md) §§ 3.1, 3.4, 3.7, 3.8, 6; [`03-expressions.md`](../03-expressions.md) § 10; [`04-loops-and-functions.md`](../04-loops-and-functions.md) § 9; [`05-templates-and-macros.md`](../05-templates-and-macros.md) §§ 4, 5.5, 8.6, 8.8, 9; [`06-checks-deliverables-profile.md`](../06-checks-deliverables-profile.md) §§ 5, 9, 10.5. The owner's rulings are the three comments on #1506 and #1507's item 3; read them before Task 1. The contract's twin is `external/consultologist-provenance/provenance-record.md` § 6 and `hash-definitions.md` § 7.

**Not this area's** (each has a seam here): History's pages, banners, the lineage view, *"Delivering…"*, the Create page (area 12: this area gives the routes and the response members); the publish half of the instances limit and naming compiled members (area 11); reruns and inputs by reference, so `previous-run` origins (area 12; 06's D20); `ProvenanceRecordCheck` for a 12 record (later); `resolve.json`'s bytes (no task here changes them).

## Global Constraints

- **Worktree only:** `~/Projects/worktrees/m39-area-09` on `m39-area-09`. Content repos are worked in `~/Projects/consultologist-package-format` and `~/Projects/consultologist-provenance` on `m39-build`, pushed directly; a pin under `external/` moves only to a pushed commit. `~/Projects/consultologist-workflows` is read-only.
- **Never edit** `.github/`, `.claude/`, `scripts/m39-build/`, `AGENTS.md`, `CLAUDE.md` or `BUILD-RUNBOOK.md`, in any repo. A shell command that names one of those paths with a `>` in it is refused by the guard: write files with the file tool. Never force-push; a pushed commit is never amended.
- **31 stays off in production:** `WorkflowPackageValidator.AcceptedSpecVersions` and `WorkflowPackageStore.SupportedSpecVersions` do not change. With `WorkflowPackages__PreviewSpecVersions` unset nothing answers differently and no 31 code path reads or creates storage (the new table is created lazily, on a 31 job's first write).
- **The 5–30 path keeps its behaviour:** no 5–30 golden, capture or expectation changes; a storage 2–11 record reads exactly as today. `[Function]` strings, activity names and entity operation names already deployed stay; new ones end in `-v31` or `V31`.
- **The record never holds a value** from an input, a model or a profile: only hashes, keys, ids, counts, lengths, clocks, states, reasons, labels and package text (printed conditions, `failWith` sentences, template sites), with part 08 § 1's exceptions (run facts, the outputs of run spots that read raw `run.*` only, item names that come from package text). The same holds for every log line, telemetry member, event and sentence. Retained text goes only to the text blobs.
- **A row is written once.** No task rewrites a stored row; what is known later is another row or the canonical document's work.
- **The orchestration stays deterministic:** the runner reads no clock but its host's, awaits no task but its host's and recorder's; a recorder's tasks are completed tasks or durable tasks of the context.
- **Never throw on package content or model output.** `ArgumentNullException` for a null argument is the only exception a public method of the library throws. An engine fault fails the job with A06-010, never an exception's message.
- **Invariant culture, ordinal comparison,** everywhere. Canonical JSON is `CanonicalJson` (RFC 8785); a hash is `sha256:<hex>` (`CanonicalJson.Hash`, `CanonicalJson.HashText`). Positions and lengths count **runes**.
- **Sentences are used as written.** A recorded sentence part 08 lists is that text. A new sentence is an area sentence `A09-NNN` (table below), added to `tests/V31Source/V31Contract.cs` (a dictionary `RecordAreaSentences` and an `A09-` branch, as A08 has), to `refusal-sentences.md` and to the conformance README's table.
- **Fictional only:** invented people and clinical text (Margaret Okafor, Dr A. Sample, Sample Oncology Clinic).
- **Goldens come from code,** are read after regeneration (`GOLDEN_UPDATE_V31=1`), and are listed in the PR. Run the conformance exports one filter at a time (`RefusalPackagesExport`, `CompiledDocumentsExport`, `DialectGoldensExport`) and read `git status` in the content repo after each.
- **Every `IRunRecorder` implementation** (`StoredRunRecorder`, `tests/V31Run/ListRecorder.cs`, any test double) gains a new member in the task that adds it.
- **Commits:** small, by concern: `PackageFormat (#1506): …`, `Api (#1506): …`, `Tests (#1506): …`, `Docs (#1506): …`; in a content repo `Format spec (Consultologist-Blazor#1506): …` or `Provenance (Consultologist-Blazor#1506): …`. Every message ends with exactly `Co-Authored-By: Claude <noreply@anthropic.com>`. Push after each task (`git push -u origin m39-area-09` the first time).
- **Tests:** run-time tests in `tests/V31Run/`, template tests in `tests/V31Templates/`, engine tests in `tests/V31Engine/`. Command: `dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~V31"`; a task that touches shared code (`ConsultGenerationState.cs`, the transport, the sweep, closure) also runs `dotnet test Consultologist.sln --filter "FullyQualifiedName!~Consultologist.Web.E2E.Tests"`. After a content-repo edit run that repo's `python3 scripts/check-v31.py`.
- **Each task ticks its own checkboxes in this file** in its last commit.

## Decisions this plan takes

Each goes in the PR body under *Decisions made during build*. R = ruled by the owner; D = where the spec is silent or two parts differ.

| # | Where | Choice |
|---|---|---|
| R1 | #1507 item 3; #1506 comment 1 | A deliverable its check refuses (state `blocked`) is kept in the record: its row with `composedHash`, `check`, `stateReason`, sites and substitutions; its composed text in the retained text; never delivered (`delivery: []`). "Ruled by the owner on #1507". |
| R2 | #1506 c2 r1 | A tenth limit, `jobInstances`: the instances one job may open. Default 10,000 (D1). "Ruled by the owner on #1506", as are R3 to R14. |
| R3 | #1506 c2 r2 | Area 06's row shapes are the contract: `:` then item ids joined by `/`, `#<n>` per pass where its `while` sits, call-through keys, `itemName` and `positionName`, `arguments` as `[{param, hash}]`. Part 08 § 2.1's `node.refine.judge:3` becomes `node.refine.judge#3`, and `#` joins the characters no id holds. **Item ids are 1-based everywhere**, array inputs included. |
| R4 | #1506 c2 r3 | `versionsRan` is the lock's nested shape minus `documentVersion`, `package` and `resolve`: `{ resolver{packageFormat, dialect}, libraries, evaluator, operations, builtins, culture, runtime{dotnet, unicode}, catalogues{agents, formats, outputContracts} }`. |
| R5 | #1506 c2 r4 | Recorded lineage flags follow earlier-pass reads (area 08 built it at publish; this area's decision flags follow them too, D4). |
| R6 | #1506 c3 r1 | A decision row has part 08 § 5.1's members: `id`, `owner`, `site`, `condition`, `expressionHash`, `result`, `reads`, `runDependent`, `modelDerived`, `decidedAtUtc`. `owner` is the row key, `site` the site word. Placements stay decided at the gate, so a gated-in node never called still has decision rows. |
| R7 | #1506 c3 r2 | A macro body's `run.*` is filled in directly, with no spot in the placing body; its macro row carries `runNeutralHash` and `runSpots`. Part 08 § 8.2 and S05-060 say so. |
| R8 | #1506 c3 r3 | `sentFormatHash` is the hash of the stripped format, taken before wrapping; the envelope is the engine version's; an object root is sent unwrapped (part 02 § 3.5 step 6). |
| R9 | #1506 c3 r4 | `units` is kept on the row and named in part 08; a snippet substituted into a body is marked `"substituted, not sent"`. The stored row holds the whole of `AgentTurn`'s detail. |
| R10 | #1506 c3 r5 | One unit for positions: runes (run-spot `start` and `length`, `outputLength`, substitution `start` and `size`). |
| R11 | #1506 c3 r6 | `classifier` is `null` unless the `if` reads exactly one node; `render:<n>` is numbered per entry across every unit, placed or not, so keys may have gaps. |
| R12 | #1506 c3 r7 | S05-056 is fixed text: `The reply has a marker that names no macro.` |
| R13 | #1506 c3 r8 | A macro body drops exactly one final line ending; its `contentHash` is taken after. Part 05 § 8.6 says so. |
| R14 | #1506 c3 r4, "the recorder persists" | Tokens join the persisted detail: `tokens { input, output, total }` on an agent row, summed over its attempts. |
| D1 | R2 "instances" | An instance is anything the runner opens with a row key: a node, check or deliverable instance, a loop item, a pass, a call. The limit is checked where the runner opens one; past it the job fails with A09-001 and the instance is not opened. Setting `ConsultGeneration__JobInstances`. 10,000 is twenty times the agent-call limit; the owner may set another starting value. |
| D2 | part 08 § 1.1 "never rewritten" against § 11's deletions | **Retained text is never in a row.** Every text § 11's right-hand column names lives in the job's text blobs. A row's member that would hold such a text holds `null` and its hash: `itemName` is the name only when it comes from package text (a snippet folder's header), else `null` with `itemNameHash`; a run spot's `output` is the text only when the spot reads raw `run.*`, else `null`. So the sweep deletes blobs only, and `recordHash` is provable after it. Part 08 §§ 4, 8.2 and 11 say so. |
| D3 | part 08 § 5.1 `result` for a `case` | A `case`'s arms have no ids at 31 (06's task 7), so `result` is the arm's 1-based position as text (`"2"`) or `"else"`; `condition` is the printed subject. |
| D4 | part 08 § 5.1 `reads`, flags | A decision's `reads` are the paths its own rule reads (the rule's `var`s, as `resolve.json` writes them), each with the canonical hash of the value read (`null` hashes as `null`). `runDependent` is true when a read is `run.*` or names an entry whose `runDependent` is true; `modelDerived` likewise from `modelDerived`. A read through `item`, `pass` or `param` takes the flags of what it stands for (the loop's source, the body node, the call's argument reads); a read the runner cannot trace is `modelDerived: true`, `runDependent: true`, so the record never marks model output as not model-derived. |
| D5 | part 08 §§ 2.3, 4.2 against a template `for` | A renderer use or a run spot inside a template `for` repeats per iteration. Each sub-row gains `pass: integer[] \| null` (one number per enclosing template loop, 1-based, as a substitution's); `render:<n>`'s `<n>` stays the use's position in the body, and a run spot's id is `<row key>/spot:<n>` with `<n>` its 1-based order of filling, `site` staying the body's site. An id and its `pass` name one sub-row. |
| D6 | part 08 § 10 "replay … must reproduce composedHash" | Each substitution records where its text sits in the composed text: `start` and `size` in runes. Replay is checked by position: the composed text's slice at each substitution hashes to the substitution's `hash`, and substitutions never overlap. `RecordReplay.Verify` is that check. |
| D7 | part 08 § 6 `effect` against "written once" | A check's `effect` is written on its one row: `read by <ref>` (its first reader in key order) for a check that gates nothing and is read; `null` when nothing reads it; for a gate, its row is recorded when the last deliverable it gates settles, with `released`, `held` or `blocked` (the deliverable's state word; part 08's "not sent" is corrected), and with more than one deliverable `<word> <deliverable key>` joined by `; ` in key order. A run that ends first records the gate's row with `effect: null`. |
| D8 | part 08 § 6 `checked` | `checked` is the one `deliverable.*` the check reads, with its composed hash; else the one node it reads, with its output hash; else `null`. `reads` holds every other read. |
| D9 | part 08 § 4 "inputHash covers the expression and the values it read" | A computed row's (and a check's) `inputHash` is the hash of the canonical JSON `{ "expression": <canonical rule>, "reads": [ { "path", "hash" } … ] }`, reads in path order; its `runNeutralHash`, when the row is run-dependent, is the same with the run-dependent reads' hashes left out (their paths stay). `hash-definitions.md` § 7.1 gains it. |
| D10 | part 08 § 1.1 | **Where things are stored.** Rows: table `ConsultGenerationRecordRows` (config sections `ConsultGenerationRecordStorage`, `AccountStorage`), partition key the job id, row key `RecordKeys.Escape(key)`, properties `key`, `section` (`node`, `check`, `deliverable`, `delivery`, `decision`), `row` (the row's canonical JSON, continued in `row1`… past 30,000 characters) and `hash`. Text: blobs `{appUserId}/{jobId}/text/{sha256 hex of key}.{part}.json` in the account kind's outputs container, `part` being `produced` (written by the activity that made it) or `settled` (written with the row). The canonical document: `{appUserId}/{jobId}/record.json` in the same container, never deleted by the sweep. |
| D11 | part 08 § 1.1 "an identical second write is a no-op; a differing one is refused and recorded as an engine fault" | The write compares the stored `hash`. A differing write logs `V31 record conflict. JobId, Key` (hashes only), leaves the stored row, and the job fails with A06-010. |
| D12 | `IRunRecorder` "awaited inline" | The recorder buffers; `IRunRecorder.FlushAsync()` is awaited by the runner once per round that recorded anything and at its end, and by the delivery runner after each row. One activity writes a round's rows, then one entity operation counts them, so an event never names a row that is not stored. |
| D13 | part 08 § 1.1 "at every terminal state" | Sealing is one idempotent operation, `RecordSealer.EnsureSealedAsync`, reached four ways: the orchestration's end (`seal-record-v31`, after `FinalizeJob`), the cancel route, the retention sweep (before the text drop), and a read of a terminal job with no `recordHash`. The canonical document holds the header without `recordHash` and without anything that changes after the end (`textDroppedAtUtc`, the History event log). |
| D14 | part 08 § 13 | The engine now writes them, so on `m39-build` the provenance registry's ladders gain `recordStorageVersions` 12, `nodeHashVersions` 7, `effectiveInputHashVersions` 7 and `workflowOutputHashVersions` 4, and `preview` empties. The registry's version string and its release stay the user's. |
| D15 | part 08 § 3.1 | A document-filled input's origin gains `converter`, `parser` and `ocr` from what the extraction already knows (the library's name and its assembly's version; the OCR service and model when OCR ran), each `null` where the step was not used. `origin` is `typed`, `document` or `transcript`; `previous-run` waits for area 12. |
| D16 | part 09 § 3.7 "events carry keys" | A 31 job's event stream gains `row-finished` (event key `row:<key>`, payload `{ key, section, status }`), materialised from the row table; the browser fetches `GET ConsultGenerationJobs/{jobId}/Rows?key=`. |
| D17 | history size (index § 5's gate) | A reply travels the orchestration's history twice (the agent activity's result, the row write's settled text). The prompt as sent and renderer texts do not: their activity writes them. Flagged for the go/no-go gate's history measurement. |

**Area sentences:**

| Id | Sentence |
|---|---|
| A09-001 | `The job reached <n> steps, the most this engine runs for one job.` (run time; `<n>` written as the other limits are, `10,000`) |

## Review Focus

1. **A Durable replay writes every row again.** The same rows, in another batch split, must be no-ops; one row differing by a byte must fail the job with A06-010 and leave the first row stored (Tasks 9, 10).
2. **A key that Azure Tables cannot hold.** `node.a.b#2:3/1/x` holds `/` and `#`; a snippet-folder item id can be long. Every key must round-trip through the row key, two different keys never share one, and a key past the row-key limit still stores (Task 9).
3. **A job that ends without its orchestration's last step.** A cancelled job, a job failed at start, an orchestration that faulted before any row: each gets a sealed record with its end state, and sealing twice gives the same `recordHash` (Task 11).
4. **Text after the drop.** After the sweep a row fetch answers the row with its hashes and says the text was deleted; `record.json` still hashes to `recordHash`; no row, event or log held the text in the first place (a sentinel input is searched for) (Tasks 7, 13).
5. **A deliverable with a loop and nothing in it.** A template `for` over zero items, a substitution of `null` (writes nothing, `size` 0), and two substitutions side by side: positions stay in runes (an emoji and a combining mark in the text), never overlap, and `RecordReplay.Verify` passes (Task 6).

---

### Task 1: The rulings written into the spec

**Files:**
- Modify: `docs/customizable-workflow/package-format-v31/08-record.md`, `02-types-and-inputs.md` (§ 3.5 step 6), `05-templates-and-macros.md` (§ 8.6, V-4, S05-060's warning W-1), `09-engine-and-pages.md` (§ 3.4), `refusal-sentences.md`
- Modify: `src/Consultologist.PackageFormat/V31/Templates/ReplySentences.cs`, `MacroReply.cs:67`; `tests/V31Source/V31Contract.cs`
- Content repos: `consultologist-package-format` (`package-format-v31.md`'s twin chapters, `v31/sentences.json` S05-056, `v31/conformance/README.md`'s open list at "`versionsRan` is given three shapes"); `consultologist-provenance` (`provenance-record.md` § 6)
- Test: `tests/V31Templates/` (the test that pins S05-056)

**Interfaces:**
- Produces: `ReplySentences.NotAMacro()` (no parameter) returning `The reply has a marker that names no macro.`; `V31Contract.RecordAreaSentences` with A09-001.

- [x] **Step 1: Write the failing test** `AMarkerNamingNoMacro_IsSaidWithoutItsId`: a reply holding `{{ macro.smith_john.content }}` gives one fault S05-056 whose sentence is exactly `The reply has a marker that names no macro.` and does not contain `smith_john`.
- [x] **Step 2: Run** it. Expected: FAIL (the sentence carries the id).
- [x] **Step 3: Implement** R12 in `ReplySentences` and `MacroReply`; change S05-056 in `refusal-sentences.md`, V-4 in part 05, and the content repo's `sentences.json`.
- [x] **Step 4: Edit part 08**, one concern per commit:
  - § 2.1: R3's key grammar (`#<n>` for a pass; `#` in the reserved characters; 1-based item ids, array inputs included), the examples corrected.
  - § 2.3 and § 4.2: R11's numbering with gaps; D5's `pass` on renderer sub-rows and run spots.
  - § 3: `versionsRan` in R4's shape (the member list and § 14's example); `limits` with `jobInstances` (and § 14's example made whole: ten members).
  - § 4: `units` (R9: `[{ site, hash, sent }]`, `sent` false for a snippet substituted into a body, which History shows as *"substituted, not sent"*); `tokens` (R14); `sentFormatHash` per R8; D2's `itemName` and `itemNameHash`; `classifier` per R11 in § 4.3; macro rows gain `runNeutralHash` and `runSpots` (R7).
  - § 5.1: D3's `result` for a `case`; D4's rule for `reads` and the flags.
  - § 6: D7's `effect` (and the word `blocked`), D8's `checked`.
  - § 8.2: R7 (macro bodies leave the list of bodies carrying spots; their rows carry their own), R10 (a run spot gains `start` and `length`, in runes of the sent text; `outputLength` in runes), D2's `output`.
  - § 10: R1 (a `blocked` deliverable is kept, marked and never delivered), D6's `start` and `size`.
  - § 1.1 and § 11: D2 (retained text is beside the record, never in a row), D10's stores in one sentence each.
- [x] **Step 5: Edit the other parts:** part 02 § 3.5 step 6 (R8); part 05 § 8.6 (R13) and W-1 (R7); part 09 § 3.4 (the tenth limit, its setting and A09-001, also in § 9's list and `refusal-sentences.md`).
- [x] **Step 6: Mirror** each edit in `package-format-v31.md` and `provenance-record.md` § 6; strike the conformance README's `versionsRan` item (R4 settles it); run both repos' `check-v31.py`; push both; move the two pins.
- [x] **Step 7: Run** the V31 filter. Expected: PASS.
- [x] **Step 8: Commit** `Docs (#1506): part 08 takes the owner's rulings` (and the per-concern commits), `PackageFormat (#1506): S05-056 quotes nothing from the reply`; push.

### Task 2: 1-based item ids and the instances limit

**Files:**
- Modify: `src/Consultologist.PackageFormat/V31/Run/PlanRunner.Loops.cs:221-223`, `RunLimits.cs`, `PlanRunner.cs` (`Add`, around line 241), `RunSentences.cs`; `src/Consultologist.Api/V31/EngineLimitSettings.cs`
- Test: `tests/V31Run/PlanRunnerLoopsTests.cs`, `RunLimitsTests.cs`, `tests/V31Engine/EngineLimitSettingsTests.cs`; goldens `tests/V31Run/Golden/*.run.txt`

**Interfaces:**
- Produces: `RunLimits` gains `int JobInstances = 10_000` (last parameter; at least 1; in `ToJson` and `FromJson` as `jobInstances`, the tenth member); `RunSentences.JobInstances(int limit)` returning A09-001; `EngineLimitSettings` reads `ConsultGeneration:JobInstances`.

- [x] **Step 1: Write the failing tests:**
  - `AnArrayInputsItems_AreNumberedFromOne`: a loop over an array input of three gives row keys ending `:1`, `:2`, `:3`, and `item.id` reads `"1"` in the first.
  - `AJobPastItsInstances_FailsWithItsSentence`: `JobInstances: 5` and a loop of ten items: the run is `Failed` with `The job reached 5 steps, the most this engine runs for one job.`, no agent call is dispatched after the limit, and at most 5 rows have a key of their own.
  - `TheLimits_AreTenMembers`: `ToJson()` has exactly the ten names, `jobInstances` last; `FromJson` refuses a document without it.
  - `JobInstances_IsRead`: the setting `ConsultGeneration:JobInstances` = `250` gives 250; `0` stops the host as the other limits do.
- [x] **Step 2: Run** them. Expected: FAIL.
- [x] **Step 3: Implement.** Count where `made` is incremented and where loop items and passes are opened; fail the job through the path `JobAgentCalls` uses.
- [x] **Step 4: Regenerate and read** the run goldens (`conditional.run.txt` moves from `:0` to `:1`); fix the tests that pinned 0-based ids.
- [x] **Step 5: Run** the V31 filter. Expected: PASS.
- [x] **Step 6: Commit** `PackageFormat (#1506): item ids start at 1; a job has an instances limit`; push.

### Task 3: Decision rows

**Files:**
- Modify: `src/Consultologist.PackageFormat/V31/Run/IRunRecorder.cs` (`RunDecision`), `RunPlan.cs` (`VarPaths`, rule compilation near lines 420-429 and 895), `PlanEntry.cs` (`PlanPlacement`), `RunScope.cs`, every `RunDecision` construction (`PlanRunner.cs:323`, `PlanRunner.Flow.cs:211,338,370`, `PlanRunner.Loops.cs:390`, `PlanRunner.Placements.cs:43`), `src/Consultologist.Api/V31/EntityRunRecorder.cs` (compiles; it is replaced in Task 10)
- Modify: `tests/V31Run/RunTrace.cs`, `ListRecorder.cs`
- Test: `tests/V31Run/RunDecisionTests.cs` (new); goldens `tests/V31Run/Golden/*.run.txt`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record RunRead(string Path, string Hash);
  public sealed record RunDecision(
      string Owner, string Site, string Condition, string ExpressionHash, bool? Result, string? Branch,
      IReadOnlyList<RunRead> Reads, bool RunDependent, bool ModelDerived, DateTime DecidedAtUtc)
  {
      public string Id { get; }            // "<Owner>/if:<Site>"
      public JsonObject ToJson();          // part 08 § 5.1, "result" a boolean or the branch text
  }
  ```
- Produces: `PlanRule` on the plan: each compiled rule keeps its canonical JSON, its `Reads` (paths, in order of first appearance, no repeats) and its `ExpressionHash` (`CanonicalJson.Hash` of the rule's JSON); `PlanPlacement` keeps the same for its `if`. `RunScope.Lineage(PlanEntry entry, Frame frame, string path)` returns `(bool RunDependent, bool ModelDerived)` per D4.

- [x] **Step 1: Write the failing tests** in `RunDecisionTests`, on the existing run fixtures:
  - `AHeaderIf_RecordsItsReadsByHash`: for `input.kind.value === "table"` decided false, `Owner` is the row key, `Site` `self`, `Id` `<key>/if:self`, `Condition` `input.kind.value = "table"`, `Reads` is one read `input.kind.value` whose hash is `CanonicalJson.Hash` of the value, `ExpressionHash` is the hash of the rule's canonical JSON, and both flags are false.
  - `ADecisionReadingAnAgent_IsModelDerived`, `…ReadingRunDate_IsRunDependent`, `…ReadingAnEarlierPassOfAnAgent_IsModelDerived` (R5), `…ReadingAnItemOfAModelList_IsModelDerived`, `…ReadingAParamGivenAModelValue_IsModelDerived`.
  - `ACase_RecordsItsArmByPosition`: `Site` `case`, `Result` null, `Branch` `"2"`, `ToJson()["result"]` the string `"2"`; an unmatched subject gives `"else"`.
  - `APlacement_RecordsItsOwnRule`: a macro's `if` gives `Site` `macro.<id>` with that rule's reads only, not the entry's.
  - `AGatedInNode_NeverCalled_StillHasItsPlacementDecisions` (R6).
  - `ToJson_HasExactlyPart08sMembers`: the ten names of § 5.1, `decidedAtUtc` as round-trip UTC text.
- [x] **Step 2: Run** them. Expected: FAIL (compile errors first; then the reads and flags).
- [x] **Step 3: Implement.** The read's value comes from the scope data the rule was decided over; a rule decided in `evaluate-rule-v31` records the same reads from the scope it was sent.
- [x] **Step 4: Update `RunTrace`** to print `id | condition | result | reads (paths) | flags`; regenerate and read the goldens.
- [x] **Step 5: Run** the V31 filter. Expected: PASS.
- [x] **Step 6: Commit** `PackageFormat (#1506): a decision records its site, its reads by hash and its flags`; push.

### Task 4: Node rows

**Files:**
- Create: `src/Consultologist.PackageFormat/V31/Run/RecordRows.cs`
- Modify: `IRunRecorder.cs` (`RunRow`), `PlanRunner.cs` (`SettleAsync`, lines 515-534), `PlanRunner.Agents.cs`, `PlanRunner.Loops.cs` (`ItemNameDetail`, `SettleItemAsync`), `PlanRunner.Flow.cs` (`SettlePassAsync`), `Templates/AgentTurn.cs` (lines 220-323), `Templates/RenderTurn.cs`, `Templates/Composition.cs`, `Templates/BodyComposer.RunSpots.cs`, `BodyComposer.Renderers.cs`, `Run/IRunHost.cs` (`AgentOutcome`), `src/Consultologist.Api/V31/RunAgentNodeV31Activity.cs` (tokens into the outcome)
- Test: `tests/V31Run/NodeRowTests.cs` (new), `tests/V31Templates/` (run spots, units), `tests/V31Engine/AgentTurnActivityTests.cs`

**Interfaces:**
- Produces: `RunRow` gains `JsonObject? Text` (the settled text, D2; null when there is none) and `string Section` (`node`, `check` or `deliverable`, from the key's root), and `JsonObject ToJson()`: the row in part 08's shape for its section, every member written.
- Produces: `internal static class RecordRows` with `Node(RunRow row)`, used by `ToJson()`; the runner fills `Detail` with what only it knows, `RecordRows` writes the defaults (`[]`, `null`) so every member of § 4 is present.
- Produces: a node row's members: storage 11's (`nodeId` the rooted entry key, `itemId` the last item id or null, `label`, `status`, `inputHash`, `outputHash`, `hashVersion` 7, `tokens`, `stateReason`) and § 4's (`kind`, `producer`, `formatHash`, `typeHash`, `sentFormatHash`, `notSent`, `agent { name, version, binding { format, producer, via } }`, `itemName`, `itemNameHash`, `positionName`, `runDependent`, `modelDerived`, `profileCarrying`, `runNeutralHash`, `runSpots`, `renders`, `macros`, `units`, `invalid`, `retries`, `profileReads`), plus area 06's (`function`, `depth`, and a loop's, while's, pass's and call's own members as they are today). `status` is `Completed`, `Skipped`, `Failed`, `NotRun` or `Cancelled`.
- Produces: `Text` members (settled): `content` (the value's `.content`), `value` (a computed value's canonical JSON, when it is not text), `itemName` (when not from package text), `spots` (`{ "<spot id>": output }` for the spots whose row `output` is null). `AgentOutcome.Detail` and `RenderOutcome.Detail` carry no text.
- Produces: a run spot `{ id, site, pass, reads, valueHash, output, outputHash, start, length }` and a renderer sub-row with `pass` (D5), positions in runes (R10); a macro row with `runNeutralHash` and `runSpots` (R7); `units` entries `{ site, hash, sent }` (R9); `tokens` on `AgentOutcome.Detail` per attempt and summed on the row (R14).

- [x] **Step 1: Write the failing tests** in `NodeRowTests` (through `ComposingRunHost` and the run fixtures):
  - `AnAgentRow_HasEveryMemberOfPart08`: the member-name set of `ToJson()` equals the list above; `producer` `generated`, `modelDerived` true, `agent` names the binding's `agentName` and `agentVersion` as `name` and `version` with `binding.format` the format key, `binding.producer` `generated`, `binding.via` the binding's.
  - `ARenderedRow_AndAComputedRow_AreComputed`: `producer` `computed`, `agent` null, `sentFormatHash` null, `notSent` `[]`, `macros` `[]`; the flags are the entry's.
  - `AFlowRow_HasNoFormat`: a `for`, `while`, `if`, `case` and `call` row have `formatHash` and `typeHash` null.
  - `ASkippedRow_SaysWhy`: `status` `Skipped`, `stateReason` `skipped: <printed> is false`, `outputHash` null.
  - `AnItemNameFromAnInput_IsNotOnTheRow`: `itemName` null, `itemNameHash` the name's hash, `Text["itemName"]` the name; from a snippet folder's header the name is on the row and `Text` has none.
  - `ARunSpot_IsPlacedInRunes`: a body `🙂 {{ run.date }}` gives `start` 2, `length` the date's rune count, and the sent text's runes at `[start, start + length)` are the output; a spot reading a run-dependent computed value that reads an input has `output` null and its text in `Text["spots"]`.
  - `ASpotInATemplateFor_HasItsPass`: two iterations give ids `…/spot:1` and `…/spot:2`, the same `site`, `pass` `[1]` and `[2]`.
  - `AMacroBodysRunFact_IsOnItsMacroRow` (R7): the placing row has no spot for it; the macro row has `runNeutralHash` and one `runSpots` entry.
  - `ASubstitutedSnippet_IsMarkedNotSent` (R9) and `TheRowKeepsItsUnits`.
  - `ARetriedCall_SumsItsTokens` (R14): two attempts of 10/5 and 20/7 tokens give `tokens` `{ input: 30, output: 12, total: 42 }`.
  - `ACallRow_HasItsFunctionArgumentsAndDepth` (part 08 § 7): `kind` `call`, `function`, `arguments` `[{ param, hash }]`, `depth` 0 outside a function and 1 inside one; its inner rows are keyed through the call node.
  - `NoDetail_CarriesText`: for every row of every golden run, neither `ToJson()` nor the outcomes' `Detail` holds the stub replies' text or the inputs' text.
- [x] **Step 2: Run** them. Expected: FAIL.
- [x] **Step 3: Implement.** Entry facts come from `PlanEntry.Json` (`label`, `producer`, the three flags, `formatHash`, `typeHash`) and `plan.Document["bindings"]`; an agent row that failed before a completed attempt still has every member.
- [x] **Step 4: Update `RunTrace`** (`# details` prints `ToJson()` for every row); regenerate and read the goldens.
- [x] **Step 5: Run** the V31 filter. Expected: PASS.
- [x] **Step 6: Commit** `PackageFormat (#1506): a node row is part 08's, with its sub-rows placed in runes`; push.

### Task 5: Check rows, and the hashes of a computed value

**Files:**
- Modify: `src/Consultologist.PackageFormat/V31/Run/RecordRows.cs`, `PlanRunner.cs` (the computed settle), `PlanRunner.Deliverables.cs` (the gate's row, D7), `PlanRunner.Loops.cs` (a check loop's items)
- Test: `tests/V31Run/CheckRowTests.cs` (new); goldens

**Interfaces:**
- Consumes: `RunRow.ToJson()`, `RunRead`, `PlanRule` (Tasks 3, 4).
- Produces: `RecordRows.Check(RunRow row)`: `key`, `kind` (`computed` or `for`), `label`, `status`, `itemName`, `itemNameHash`, `alignedWith`, `checked { ref, hash }`, `reads [{ path, hash }]`, `inputHash`, `outputHash`, `runNeutralHash`, `producer`, `modelDerived`, `runDependent`, `profileCarrying`, `pass`, `failWith`, `passed`, `total`, `uncoveredHash`, `uncoveredSize`, `untestedHash`, `untestedSize`, `effect`, `count`, `stateReason`. `Text` gains `uncovered` and `untested` (the lists).
- Produces: `RecordRows.ComputedInputHash(JsonNode rule, IReadOnlyList<RunRead> reads)` and `ComputedNeutralHash(rule, reads, runDependentPaths)` (D9), used for computed node rows too.

- [x] **Step 1: Write the failing tests:**
  - `ACheckRow_HasPart08sMembers`: on `checked-note`, `check.proofread` has `checked.ref` `deliverable.note` with the composed hash, `pass` the verdict, `failWith` null when it passed and the package's sentence when it failed, `producer` `computed`.
  - `ACheckResultsLists_AreByHash`: `uncoveredHash` is the canonical hash of the list, `uncoveredSize` its count, the list itself only in `Text`; `passed` and `total` are on the row.
  - `AGatesEffect_IsItsDeliverablesState`: `released` when it passed, `blocked` when it failed, `held` for an empty held deliverable; a gate of two deliverables writes `blocked deliverable.a; released deliverable.b`; the gate's row is recorded after its deliverables settle and exactly once.
  - `ACheckThatIsRead_SaysByWhat`: `read by check.all_sections`; a check nothing reads has `effect` null.
  - `ACheckLoopsItem_IsAlignedWithItsProductionItem`: `check.part_coverage.coverage:2` has `alignedWith` `node.sections:2`; the loop's own row has `kind` `for` and `count`.
  - `AComputedRowsInputHash_CoversItsRuleAndItsReads`: equals `ComputedInputHash`; changing a read value changes it; for a run-dependent value `runNeutralHash` is unchanged when only `run.date` changes.
  - `ARunThatFailsBeforeAGateSettles_StillRecordsTheCheck`: `effect` null.
- [x] **Step 2: Run** them. Expected: FAIL.
- [x] **Step 3: Implement.**
- [x] **Step 4: Regenerate and read** the goldens.
- [x] **Step 5: Run** the V31 filter. Expected: PASS.
- [x] **Step 6: Commit** `PackageFormat (#1506): check rows, and a computed value's two hashes`; push.

### Task 6: Deliverable rows: sites, substitutions and replay

**Files:**
- Create: `src/Consultologist.PackageFormat/V31/Run/RecordReplay.cs`
- Modify: `Templates/BodyComposer.cs` (output tags), `Templates/Composition.cs`, `Templates/RenderTurn.cs`, `Run/RecordRows.cs`, `Run/PlanRunner.Deliverables.cs` (`DeliverableSettled`, lines 139-170), `src/Consultologist.Api/V31/RenderNodeV31Activity.cs` (compiles; the text it writes is Task 10's)
- Test: `tests/V31Templates/SubstitutionTests.cs` (new), `tests/V31Run/DeliverableRowTests.cs` (new); goldens

**Interfaces:**
- Produces: `Composition.Substitutions`: `IReadOnlyList<Substitution>` with `record Substitution(int Site, IReadOnlyList<int>? Pass, string Reads, string Hash, int Start, int Size, string? Renders, string Value)`; `Value` is the text written (after escaping), which never enters a row. `Composition.Sites`: `[{ site, at "<line>:<column>", expr, loop }]` in source order.
- Produces: `RecordRows.Deliverable(RunRow row)`: `key`, `label`, `templateHash` (the entry's `bodyHash`), `format`, `state`, `stateReason`, `composedHash`, `hashVersion` 4, `delivery` (the delivery row keys of a released deliverable, `[]` otherwise), `check` (the gate's row key or null), `sites`, `substitutions` (`[{ site, pass, reads, hash, start, size, renders }]`), `renders`, `runNeutralHash`, `runSpots`, `units`, `profileReads`, `modelDerived`, `runDependent`, `profileCarrying`. `state` is `released`, `held`, `failed`, `skipped` or `blocked`.
- Produces: `RenderOutcome` gains `JsonObject? Produced` (substitution values by index, renderer inputs and outputs by sub-row id and pass), which Task 10's activity writes to the text blob and strips before it answers; in-process hosts keep it.
- Produces: `public static class RecordReplay { public static bool Verify(string composedText, JsonArray substitutions, out string? fault); }` (D6).

- [x] **Step 1: Write the failing tests:**
  - `EachOutputTag_IsASite`: a template of three tags, one inside a `for`, gives three sites with `at` and `expr`, the looped one naming its `loop`.
  - `ASubstitution_SaysWhereItsTextSits`: for `Dear {{ input.name.value }} 🙂 {{ node.plan.content }}`, the slices of the composed text at each `start`/`size` (runes) are the values, each `hash` is the slice's hash, and `RecordReplay.Verify` is true.
  - `ALoopOverNothing_HasNoSubstitutions`, `ANullRead_WritesNothing` (`size` 0, the hash of the empty text), `TwoTagsSideBySide_DoNotOverlap`, `AnEscapedRead_IsRecordedAfterEscaping`, `ACombiningMark_CountsAsItsRunes` (Review Focus 5).
  - `ALoopedSite_HasOnePassPerIteration`: `pass` `[1]`, `[2]`; nested loops give two numbers.
  - `ARenderedSubstitution_NamesItsLastRender`: `renders` is the chain's last sub-row id.
  - `AProfileRead_IsASubstitution`: `reads` `profile.signature`, by hash; `profileReads` lists it.
  - `ADeliverableRow_HasPart08sMembers`, `ABlockedDeliverable_KeepsItsHashAndIsNeverDelivered` (R1: `state` `blocked`, `composedHash` set, `delivery` `[]`, `Text["content"]` the composed text), `AReleasedDeliverable_NamesItsDeliveryRows` (`delivery:deliverable.note/email`), `ASkippedDeliverable_HasNoComposedHash`.
  - `Verify_RefusesAMovedSubstitution`: shifting one `start` by one makes it false with a fault naming the site.
- [x] **Step 2: Run** them. Expected: FAIL.
- [x] **Step 3: Implement.** Positions are counted as the composer writes, as run spots' are; an object-format deliverable has one substitution covering its whole text.
- [x] **Step 4: Regenerate and read** the goldens.
- [x] **Step 5: Run** the V31 filter. Expected: PASS.
- [x] **Step 6: Commit** `PackageFormat (#1506): a deliverable row records its sites and substitutions, replayable by position`; push.

### Task 7: The canonical record document

**Files:**
- Create: `src/Consultologist.PackageFormat/V31/Run/RecordDocument.cs`, `RecordKeys.cs`, `VersionsRan.cs`
- Modify: `Run/Delivery.cs` (`DeliveryRow.ToJson()`), `Run/IRunRecorder.cs` (`FlushAsync`), `Run/PlanRunner.cs` and `DeliveryRunner.cs` (the flushes, D12), `tests/V31Run/ListRecorder.cs`, `RunTrace.cs`
- Test: `tests/V31Run/RecordDocumentTests.cs`, `RecordKeysTests.cs`, `VersionsRanTests.cs` (new); goldens `tests/V31Run/Golden/*.record.json` (new, one per run golden)

**Interfaces:**
- Produces:
  ```csharp
  public sealed record RecordRow(string Section, string Key, string Json);   // Json: the row's canonical JSON
  public static class RecordDocument
  {
      public static RecordRow Of(RunRow row);
      public static RecordRow Of(RunDecision decision);    // Section "decision", Key = decision.Id
      public static RecordRow Of(DeliveryRow row);         // Section "delivery"
      public static string Assemble(JsonObject header, IEnumerable<RecordRow> rows);  // canonical bytes as text
      public static string Hash(string document);          // recordHash
  }
  public static class RecordKeys { public static string Escape(string key); public const int MaxRowKey = 512; }
  public static class VersionsRan { public static JsonObject Of(Toolchain toolchain, JsonObject lockDocument); }
  ```
- Produces: the assembled document: the header's members, then `decisions` (an array, by id), `nodeOutputs` and `checks` (objects by key), `deliverables` (an array by key, each with `deliveries`: its delivery rows by key). A header member named `recordHash`, `textDroppedAtUtc` or `history` is refused (`ArgumentException`): they are not part of the sealed bytes (D13). Rows repeat-safe: the same `(Section, Key, Json)` twice is one; the same key with other JSON throws `InvalidOperationException` (an engine fault).
- Produces: `RecordKeys.Escape`: `!` then two hex digits for `!`, `/`, `\`, `#`, `?`, `%` and any character below U+0020 or from U+007F to U+009F; a result longer than `MaxRowKey` is `~` and the SHA-256 hex of the key. Injective.
- Produces: `IRunRecorder.FlushAsync()`; the runner awaits it once after each round that recorded a row or decision and before it returns; the delivery runner after each delivery row.
- Produces: `VersionsRan.Of`: R4's shape; `operations` and `builtins` take the lock's keys with this engine's versions (`JsonLogicOperations`, the built-ins table `LockCompiler` reads).

- [x] **Step 1: Write the failing tests:**
  - `Escape_RoundTripsThroughTables`: for `node.a.b#2:3/1/x`, `delivery:deliverable.emr.note/email` and `node.x/if:macro.y`, the result holds none of `/ \ # ?` or a control character; `Escape("a/b") != Escape("a!2Fb")`; a 600-character key gives `~` and 64 hex digits; two long keys differing in their last character differ.
  - `Assemble_IsCanonicalAndInKeyOrder`: rows given in any order give the same text; the text equals `CanonicalJson` of itself; `decisions` is sorted by id; a deliverable holds its two delivery rows.
  - `Assemble_TakesARepeatedRowOnce` and `Assemble_RefusesADifferingRow`.
  - `Assemble_RefusesWhatChangesAfterTheEnd` (`recordHash`, `textDroppedAtUtc`).
  - `TheGoldenRuns_AssembleToTheirRecordGoldens`: each run golden's rows, decisions and deliveries (through `ListRecorder`), under a fixed fictional header, assemble to `tests/V31Run/Golden/<name>.record.json`, byte for byte.
  - `NoRecord_HoldsAValue` (Review Focus 4): each golden run is run with the sentinel `ZZ-SENTINEL-OKAFOR` in every input and every stub reply; no record document holds `SENTINEL`.
  - `TheRunner_FlushesEachRound`: `ListRecorder.Flushes` is at least 1 per round that recorded, and the last recorded thing is followed by a flush.
  - `VersionsRan_IsTheLocksShapeWithoutThree`: for `nested-note.lock.json`, the member names are exactly R4's eight, `resolver`, `runtime` and `catalogues` nested, `operations` the lock's keys.
- [x] **Step 2: Run** them. Expected: FAIL.
- [x] **Step 3: Implement.**
- [x] **Step 4: Generate and read** the record goldens (`GOLDEN_UPDATE_V31=1`); each goes in the PR's *Goldens to review*.
- [x] **Step 5: Run** the V31 filter. Expected: PASS.
- [x] **Step 6: Commit** `PackageFormat (#1506): the canonical record document, its keys and versionsRan`; push.

### Task 8: The header of a 31 job

**Files:**
- Modify: `src/Consultologist.Api/Jobs/ConsultGenerationState.cs` (`Stamp` :43, `Seed` :306, `StorageVersions` :2037, `ConsultGenerationV31Header` :1822, `RecordV31Header` :412, the state's members near :2189), `src/Consultologist.Api/V31/V31JobStarter.cs`, `ConsultGenerationOrchestratorV31.cs` (the header call, after the run facts are loaded), `LoadRunInputs` activity (the lock for `versionsRan`), the document-origin code the starter uses (D15)
- Content repo: `consultologist-provenance` (`provenance-versions.json`, D14; `hash-definitions.md` § 7.2 if its wording needs the starter's exact input)
- Test: `tests/V31Engine/V31JobStartTests.cs`, `OrchestratorV31Tests.cs`, `tests/ProvenanceVersionSetTests.cs`, `tests/V31ContractsTests.cs:72-94`

**Interfaces:**
- Produces: a 31 job is `SchemaVersion` 12 on both ladders; `StorageVersions` ends `…, 11, 12`.
- Produces: state members (all `JsonIgnore(WhenWritingNull)`, write-once): `VersionsRan` (`JsonObject`), `RunFacts` (`JsonObject`), `RecordHash` (`string`), `RecordBytes` (`long?`); `Limits` holds ten members; `EffectiveInputHash` with `EffectiveInputHashVersion` 7 (as `hash-definitions.md` § 7.2 defines it); each document origin gains `Converter`, `Parser` (`{ name, version }`) and `Ocr` (`{ service, model }`).
- Produces: `ConsultGenerationV31Header` gains `JsonObject? VersionsRan, JsonObject? RunFacts` (trailing, optional, so a history written before stays readable); the orchestration records them once, before any node runs.
- Produces: `JsonObject ConsultGenerationJobState.ToRecordHeader()`: the canonical document's header: `schemaVersion`, `packageSpecVersion`, `workflowPackage`, `packageFormatRef`, `provenanceRef`, `formatsRef`, `catalogRef`, `engineCommit`, `engineRelease`, `apiHost`, `jobId`, `status`, `error`, `createdAtUtc`, `startedAtUtc`, `completedAtUtc`, `resolveHash`, `lockHash`, `versionsRan`, `runFacts`, `limits`, `signatureBlockId`, `signatureChosen`, `effectiveInputHash`, `effectiveInputHashVersion`, `inputs` (each `{ id, origin, hash, … }`), `tokens`; a member with no value is `null`, never absent.

- [x] **Step 1: Write the failing tests:**
  - `A31Job_IsStorage12_FromItsCreation` (the start, a start failure, a decision failure) and `A30Job_IsStill11`.
  - `TheRecordStorageVersions_AreThePublishedOnes` now includes 12; `TheEngineAt30_StillSeesItsOwnSets` becomes `The31Sets_HaveJoinedTheLadders` (12, 7, 7 and 4 in the ladders; `preview` holds none of them).
  - `TheHeader_RecordsWhatRan`: after the orchestration's header step the entity holds `VersionsRan` in R4's shape, `RunFacts` with exactly the facts the package reads (`date` as `{ value, timeZone }`), and ten `Limits`; a second header call changes nothing.
  - `TheStart_HashesItsInputs`: `EffectiveInputHashVersion` 7 and the hash of the inputs as § 7.2 defines; two starts with the same inputs agree, another input value differs.
  - `ADocumentInput_RecordsItsExtraction`: a PDF input's origin has `Parser.name` and a version, `Converter`, and `Ocr` null without OCR; a typed input has origin `typed` and none of the three.
  - `A31Job_WritesNothingRetiredAt12` (part 08 § 3): the state of a 31 job has no `MacroChoices`, `ExcludedMacros`, `Classifications` or signature snapshot.
  - `ToRecordHeader_HasEveryMember_AndNoText`: the names above, exactly; no `history`, `recordHash` or `textDroppedAtUtc`.
- [x] **Step 2: Run** them. Expected: FAIL.
- [x] **Step 3: Implement**; edit `provenance-versions.json` on the content repo's `m39-build`, run its check, push, move the pin.
- [x] **Step 4: Run** the whole suite (shared code). Expected: PASS.
- [x] **Step 5: Commit** `Api (#1506): a 31 job is storage 12 and its header records what ran`; push.

### Task 9: The stores

**Files:**
- Create: `src/Consultologist.Api/V31/RecordRowStore.cs` (`IRecordRowStore`, `TableRecordRowStore`), `src/Consultologist.Api/V31/RecordBlobStore.cs` (`IRecordBlobStore`, `BlobRecordStore`)
- Modify: `src/Consultologist.Api/ApiServices.cs` (two singletons, beside the event store's)
- Test: `tests/V31Engine/RecordRowStoreTests.cs`, `RecordBlobStoreTests.cs` (new), with in-memory fakes `tests/V31Engine/FakeRecordStores.cs` that later tasks use

**Interfaces:**
- Consumes: `RecordRow`, `RecordKeys` (Task 7); `StorageTables.CreateClient`, `TextBlobClientFactory`, `TextBlobNaming.ContainerFor`.
- Produces:
  ```csharp
  public enum RecordWrite { Written, AlreadyThere, Conflict }
  public interface IRecordRowStore
  {
      Task<IReadOnlyList<(string Key, RecordWrite Result)>> WriteAsync(string jobId, IReadOnlyList<RecordRow> rows, CancellationToken ct);
      Task<RecordRow?> ReadAsync(string jobId, string key, CancellationToken ct);
      Task<IReadOnlyList<RecordRow>> ListAsync(string jobId, CancellationToken ct);
      Task DeleteJobAsync(string jobId, CancellationToken ct);
  }
  public interface IRecordBlobStore
  {
      Task WriteTextAsync(string? accountKind, string appUserId, string jobId, string key, string part, JsonObject text, CancellationToken ct); // part: "produced" | "settled"
      Task<JsonObject?> ReadTextAsync(string? accountKind, string appUserId, string jobId, string key, CancellationToken ct);  // both parts merged; null when neither is there
      Task DeleteTextAsync(string? accountKind, string appUserId, string jobId, CancellationToken ct);                          // the job's text/ prefix only
      Task<bool> WriteRecordAsync(string? accountKind, string appUserId, string jobId, string document, CancellationToken ct);  // false when one is already there
      Task<string?> ReadRecordAsync(string? accountKind, string appUserId, string jobId, CancellationToken ct);
  }
  ```
- Produces: D10's names and D11's comparison. `WriteAsync` inserts each row alone (a batch transaction fails whole on one conflict): `Add`; on 409 it reads the stored `hash`: equal is `AlreadyThere`, other is `Conflict`. The table is created on the first write. A `row` longer than 30,000 characters is split across `row`, `row1`, `row2`…

- [x] **Step 1: Write the failing tests** (the table store against a substituted `TableClient` as `ConsultGenerationJobEventStore`'s tests do, or its seam; the fakes against the same assertions):
  - `ARow_IsWrittenOnce`: the second identical write is `AlreadyThere`; a write of the same key with other JSON is `Conflict` and `ReadAsync` still gives the first (Review Focus 1).
  - `AKeyWithASlashAndAHash_RoundTrips` and `ALongKey_Stores` (Review Focus 2): `ReadAsync(key)` gives the row with its true `Key`.
  - `ALongRow_IsSplitAndRejoined`: a 70,000-character row reads back equal.
  - `List_GivesEveryRowOfTheJob_AndNoOthers`; `DeleteJob_RemovesThePartition`.
  - `Text_MergesItsTwoParts`; `DeleteText_LeavesTheRecord`: after it `ReadTextAsync` is null and `ReadRecordAsync` still answers.
  - `TheRecord_IsWrittenOnce`: a second `WriteRecordAsync` answers false and leaves the first.
  - `NothingIsCreated_UntilA31JobWrites`: constructing the stores touches no storage.
- [x] **Step 2: Run** them. Expected: FAIL.
- [x] **Step 3: Implement.**
- [x] **Step 4: Run** the V31 filter and the composition test that resolves `ApiServices`. Expected: PASS.
- [x] **Step 5: Commit** `Api (#1506): the record's rows in a table, its text and its document in blobs`; push.

### Task 10: The recorder, and the activities' text

**Files:**
- Create: `src/Consultologist.Api/V31/StoredRunRecorder.cs`, `WriteRecordRowsV31Activity.cs`
- Delete: `src/Consultologist.Api/V31/EntityRunRecorder.cs` (its `StateName` and `DeliveryStateName` move)
- Modify: `ConsultGenerationOrchestratorV31.cs`, `RunAgentNodeV31Activity.cs`, `RenderNodeV31Activity.cs`, `DurableRunHost.cs`, `src/Consultologist.Api/Jobs/ConsultGenerationState.cs` (`ConsultGenerationActivityNames`, a new entity operation, `ConsultV31ProgressState`, the usage row in `FinalizeJob` :1234)
- Test: `tests/V31Engine/StoredRunRecorderTests.cs` (new), `OrchestratorV31Tests.cs`, `RunAgentNodeV31ActivityTests.cs`, `AgentTurnActivityTests.cs`

**Interfaces:**
- Consumes: `IRecordRowStore`, `IRecordBlobStore`, `RecordDocument.Of`, `IRunRecorder.FlushAsync`.
- Produces: activity `write-record-rows-v31` (`ConsultGenerationActivityNames.WriteRecordRowsV31`): input `WriteRecordRowsV31Input(string JobId, string AppUserId, string? AccountKind, IReadOnlyList<RecordRow> Rows, IReadOnlyDictionary<string, JsonObject> Settled)`; it writes each row's settled text, then the rows; result `WriteRecordRowsV31Result(IReadOnlyList<string> Conflicts)`.
- Produces: `StoredRunRecorder(TaskOrchestrationContext, ConsultGenerationOrchestrationInputV31) : IRunRecorder`: `RowAsync`, `DecisionAsync` and `DeliveryAsync` buffer and return completed tasks; `FlushAsync` calls the activity with the buffer, throws `RecordConflictException` when it names a conflict (the orchestration's catch fails the job with A06-010), then calls the entity once: `RecordV31Rows(ConsultGenerationV31Rows)` with each row's `(Key, State)` (a decision's state null, a delivery's `delivery:<result>`), and the round's token sums. `NoticeAsync` is as today.
- Produces: entity operation `RecordV31Rows`: counts as `RecordV31Progress` does, adds tokens to `V31Progress.Tokens { Input, Output, Total }`; `RecordV31Progress` stays for histories in flight. `FinalizeJob` writes a 31 job's usage row from `V31Progress.Tokens`.
- Produces: `run-agent-node-v31` writes `produced` text `{ sent, neutral, renders, macros }` for its row key (the prompt as sent; each renderer step's input and output; each macro's rendered body), and `render-node-v31` writes `{ neutral, renders, substitutions }`, each overwriting an earlier attempt's; both strip `Produced` from what they answer. Their logs keep lengths and hashes only.

- [x] **Step 1: Write the failing tests** (with `FakeOrchestrationContext` and Task 9's fakes):
  - `ARoundsRows_GoInOneActivity_ThenOneEntityCall`: three rows and two decisions buffered, one flush: one `write-record-rows-v31` call holding five rows, then one `RecordV31Rows`; a flush with nothing buffered calls nothing.
  - `AReplay_WritesNothingNew` (Review Focus 1): running the orchestration twice over the same fakes leaves each row stored once and reports no conflict.
  - `ADifferingRow_FailsTheJobAsAnEngineFault`: a store that answers `Conflict` gives a job `Failed` with `ConsultGenerationOrchestratorV31.EngineFault`, the first row still stored, and a log line holding the key and no row content.
  - `A31Job_StoresEveryRow`: a golden run through the orchestration stores exactly the rows of its `.record.json` golden.
  - `TheAgentActivity_WritesWhatItSent`: the blob store holds `sent` for the row key, equal to the composed prompt, whose hash is the row's `inputHash`; the activity's result holds no `Produced` and no prompt.
  - `TheRenderActivity_WritesItsSubstitutions`: values by index, each hashing to its substitution's `hash`.
  - `TheSettledText_HoldsTheContent`: a completed agent row's `content` hashes to its `outputHash`; a blocked deliverable's `content` hashes to its `composedHash` (R1).
  - `Tokens_ReachTheUsageRow`: a 31 job's usage row sums its rows' tokens.
- [x] **Step 2: Run** them. Expected: FAIL.
- [x] **Step 3: Implement.**
- [x] **Step 4: Run** the whole suite (shared code). Expected: PASS.
- [x] **Step 5: Commit** `Api (#1506): a 31 job's rows are stored as they finish; its activities keep the text they make`; push.

### Task 11: Sealing

**Files:**
- Create: `src/Consultologist.Api/V31/RecordSealer.cs`, `SealRecordV31Activity.cs`
- Modify: `ConsultGenerationOrchestratorV31.cs` (`FinalizeAsync` and the catch), `src/Consultologist.Api/Jobs/ConsultGenerationState.cs` (entity operation `RecordV31Seal`, activity name), the cancel route in `src/Consultologist.Api/Jobs/ConsultGenerationTransport.cs`, `src/Consultologist.Api/ApiServices.cs`
- Test: `tests/V31Engine/RecordSealerTests.cs` (new), `OrchestratorV31Tests.cs`

**Interfaces:**
- Consumes: `ToRecordHeader()` (Task 8), `RecordDocument.Assemble` and `Hash` (Task 7), the stores (Task 9).
- Produces:
  ```csharp
  public sealed record SealedRecord(string RecordHash, long Bytes, int Rows);
  public interface IRecordSealer
  {
      /// Null when the job is not a terminal 31 job. Idempotent.
      Task<SealedRecord?> EnsureSealedAsync(string jobId, CancellationToken ct);
  }
  ```
  It reads the entity's state (through `DurableTaskClient`, as the job routes do); a terminal `SchemaVersion` 12 job with no `RecordHash`: lists the rows, assembles, `WriteRecordAsync`; when a document is already there it hashes that one; signals `RecordV31Seal(RecordHash, Bytes)` (write-once on the entity); logs `V31 record sealed. JobId, Rows, Bytes, RecordHash`.
- Produces: activity `seal-record-v31` calling it, awaited by the orchestration after `FinalizeJob` on every path (the catch included, where its failure is logged and never replaces the fault); the cancel route calls the sealer after it finalises a 31 job.

- [x] **Step 1: Write the failing tests** (Review Focus 3):
  - `ACompletedJob_IsSealed`: the document equals `Assemble(header, rows)`, `RecordHash` is its hash, the header's `status` is `Completed`, and the entity holds the hash and the byte count.
  - `SealingTwice_GivesTheSameHash`, also when the second call races the first (the blob is there, the entity is not yet stamped).
  - `AFailedJob`, `ACancelledJob`, `ATimedOutJob` and `AJobThatNeverStarted` (a start failure, no rows) are each sealed with their end state.
  - `ADeliveringJob_IsNotSealed`: null, and no blob; it is sealed after delivery ends, with its delivery rows under their deliverables.
  - `A30Job_IsNeverSealed`.
  - `AnEngineFault_StillSeals`: the orchestration's catch path leaves a sealed record with `status` `Failed`; a sealer that throws there does not change the job's error.
- [x] **Step 2: Run** them. Expected: FAIL.
- [x] **Step 3: Implement.**
- [x] **Step 4: Run** the whole suite. Expected: PASS.
- [x] **Step 5: Commit** `Api (#1506): the record is sealed at every terminal state`; push.

### Task 12: Serving the record

**Files:**
- Create: `src/Consultologist.Api/V31/RecordRoutes.cs`
- Modify: `src/Consultologist.Api/Jobs/ConsultGenerationState.cs` (`ToResponse` :2459), `src/Consultologist.Api/Models/ConsultGenerationRequest.cs` (`ConsultGenerationJobResponse`), `src/Consultologist.Api/Jobs/ConsultGenerationTransport.cs` (`CreateSemanticEventCandidates` :2140, the two materialisers)
- Test: `tests/V31Engine/RecordRoutesTests.cs` (new), `tests/ConsultGenerationJobEventPayloadTests.cs`, `tests/FunctionRegistrationTests.cs`

**Interfaces:**
- Consumes: the stores, `IRecordSealer`, the job routes' authorisation and owner check (`GetJobResponseAsync`), `FunctionCors`.
- Produces: `ConsultGenerationJobResponse` gains (null below 31, so a 5–30 response writes today's bytes): `ResolveHash`, `LockHash`, `VersionsRan`, `RunFacts`, `Limits`, `SignatureBlockId`, `SignatureChosen`, `RecordHash`, `Rows` (the count), `RowStates`, `Decisions`, `CurrentKey`.
- Produces: functions (GET and OPTIONS, the job routes' auth, CORS applied):
  - `GetConsultGenerationJobRows`, `ConsultGenerationJobs/{jobId}/Rows`: with `?key=<row key>` one row: `{ key, section, row, text, textDeleted }`, `text` the merged text blob while it is kept and each of its members offered only when it hashes to the row's hash for it (`content` against `outputHash` or `composedHash`, `sent` against `inputHash`), `textDeleted` true once the job's text was dropped; 404 for a key the job has no row for. Without `key`: `{ rows: [{ key, section, row }] }`, every row, no text.
  - `GetConsultGenerationJobRecord`, `ConsultGenerationJobs/{jobId}/Record`: the canonical document's bytes as `application/json` with `Content-Disposition: attachment; filename="record-<jobId>.json"` and the header `X-Record-Hash`; it seals a terminal job that is not sealed (D13); 409 with `{ error = "The job hasn't finished." }` for a job not terminal; 404 below storage 12.
- Produces: event `row-finished` (D16) for a 31 job, one per stored row not yet announced (decisions excluded), in key order within a materialisation, before `done`.

- [ ] **Step 1: Write the failing tests:**
  - `A31JobsResponse_ShowsItsHeader` and `A30JobsResponse_IsUnchanged` (its JSON holds none of the new names).
  - `ARow_IsFetchedByItsKey` (a key with `/` and `#` in the query), `ARowOfAnotherAccountsJob_IsNotFound`, `AnUnknownKey_Is404`.
  - `ARowsText_IsOfferedOnlyWhenItHashes`: a `content` that does not hash to `outputHash` (a late write after a cancel) is left out.
  - `AfterTheDrop_TheRowSaysItsTextWasDeleted` (Review Focus 4): the row and its hashes are there, `text` null, `textDeleted` true.
  - `TheRecord_DownloadsAsItsSealedBytes`: the body hashes to `X-Record-Hash` and to the entity's `RecordHash`; a running job answers 409; a cancelled job never sealed is sealed by the request.
  - `EachStoredRow_IsAnnouncedOnce`: two materialisations over a growing table give one `row-finished` per row, each payload `{ key, section, status }` and nothing else.
  - `TheNewRoutes_AnswerOptions_WithCors`.
- [ ] **Step 2: Run** them. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the whole suite and `dotnet build Consultologist.sln` (the Web project reads the response type's twin, if it has one; add the members there as nullable). Expected: PASS.
- [ ] **Step 5: Commit** `Api (#1506): a 31 job's rows, record and row events are served`; push.

### Task 13: Retention and closure

**Files:**
- Modify: `src/Consultologist.Api/Jobs/ConsultGenerationState.cs` (`DropText` :1030), `src/Consultologist.Api/Jobs/TextRetentionSweep.cs` (`JobTextPurger.PurgeAsync` :92), `src/Consultologist.Api/Auth/AccountClosure.cs` (`CloseAsync` :77)
- Test: `tests/TextRetentionTests.cs`, `tests/AccountClosureTests.cs`, `tests/V31Engine/RecordRetentionTests.cs` (new)

**Interfaces:**
- Consumes: `IRecordSealer.EnsureSealedAsync`, `IRecordBlobStore.DeleteTextAsync`, `IRecordRowStore.DeleteJobAsync`.
- Produces: for a terminal storage-12 job the purge seals first, then deletes the job's `text/` blobs, then stamps `TextDroppedAtUtc` as today; rows and `record.json` stay. Closing an account deletes each job's row partition (the blobs go with the account's prefix, as today).

- [ ] **Step 1: Write the failing tests:**
  - `TheSweep_DeletesA31JobsText_AndKeepsItsRecord`: after it `ReadTextAsync` is null for every row, every row reads as before, `record.json` hashes to `RecordHash`, and `TextDroppedAtUtc` is set.
  - `TheSweep_SealsAJobNobodyRead`: an unsealed cancelled job is sealed by the sweep before its text goes.
  - `TheSweep_LeavesA30JobAsToday` (its existing assertions, unchanged).
  - `TheSweep_IsSafeToRunTwice`.
  - `ClosingAnAccount_RemovesItsRecordRows`: `DeleteJobAsync` for each of its jobs; a failure for one job does not stop the rest (as the events' deletion behaves).
- [ ] **Step 2: Run** them. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the whole suite. Expected: PASS.
- [ ] **Step 5: Commit** `Api (#1506): the text drop and account closure know a 31 job's record`; push.

### Task 14: The contract and the guides

**Files:**
- Content repo `consultologist-provenance`: `provenance-record.md` § 6 (whatever Tasks 2 to 13 settled beyond Task 1's edits: the row members as built, the stores, the routes' names left out), `hash-definitions.md` § 7 (D9's computed hashes; `recordHash` over D13's document; `expressionHash`; substitution positions and D6's replay; rune positions)
- Content repo `consultologist-package-format`: `package-format-v31.md` (the record chapter's twin of every part 08 edit), `v31/conformance/README.md` (the A09 table)
- Modify: `docs/customizable-workflow/package-format-v31/08-record.md` (any member the build named that Task 1 did not), `docs/customizable-workflow/engine-v31.md` (lines 156, 303, 308, 368, 396: the record as built, the new activities and entity operations in a 31 history, the settings `ConsultGeneration__JobInstances` and `ConsultGenerationRecordStorage`), `docs/STORAGE.md` (the table and the blobs), `docs/CONSULT_GENERATION_EVENTS.md` (`row-finished`), `docs/customizable-workflow/provenance.md`, `docs/customizable-workflow/storage-separation.md` § 2.2
- Test: `tests/V31ContractsTests.cs`

- [ ] **Step 1: Write the failing test** `TheRecordContract_NamesEveryMemberTheEngineWrites`: every member name in the record goldens (`tests/V31Run/Golden/*.record.json`) and in `ToRecordHeader()` appears in backticks in `external/consultologist-provenance/provenance-record.md` § 6.
- [ ] **Step 2: Run** it. Expected: FAIL, listing the names not yet written.
- [ ] **Step 3: Write the contract** in the provenance repo; mirror in the package-format repo and part 08; run both checks; push both; move the pins.
- [ ] **Step 4: Write the guides.** `engine-v31.md` also says: drain 31 orchestrations before deploying this build (a 31 history gains `write-record-rows-v31` and `seal-record-v31`); the record's size per job is logged at sealing (`Rows`, `Bytes`) for index § 5's gate; D17's note on history size.
- [ ] **Step 5: Run** the whole suite. Expected: PASS.
- [ ] **Step 6: Commit** `Docs (#1506): the provenance contract and the guides describe storage 12 as built`; push.
