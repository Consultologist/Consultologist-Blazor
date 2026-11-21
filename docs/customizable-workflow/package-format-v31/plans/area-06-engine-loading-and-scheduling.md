# Area 06: Engine Loading and Scheduling (specVersion 31) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the engine load a published specVersion 31 package from `resolve.json` and `lock.json` alone, refuse what it must at load and at job start, and run the compiled graph (leaves, `for`, `while`, `if`, `case`, `call` inline) under the per-job ceiling, the retries and the limits, with a drain before every deploy; 31 admitted only through `WorkflowPackages__PreviewSpecVersions`.

**Architecture:** The run itself is a pure library: `Consultologist.PackageFormat/V31/Run/` holds `RunPlan` (the in-memory plan built from `resolve.json`) and `PlanRunner` (the scheduler), which talks to the outside only through `IRunHost` (agent calls, renders, timers, the clock) and `IRunRecorder` (rows, decisions). So the same runner serves the engine, area 10's byte-equivalence run and area 11's in-package tests. `src/Consultologist.Api/V31/` adapts it to Durable Functions: a store that reads and verifies the stamped pair, a job-start path, one new orchestrator and two new activities, and the drain. The 5–30 path is entered exactly as today and changes only where the drain needs it.

**Tech Stack:** C# / .NET 10, `System.Text.Json` (`JsonNode`), Durable Functions isolated worker, areas 02 to 05's services (`CompiledPackage.Verify`, `RequiresCheck`, `CorvusJsonLogicEvaluator`, `CorvusFormatValueValidator`, `JsonLogicPrinter`, `CanonicalJson`, `Toolchain`), xUnit + NSubstitute in `tests/Consultologist.Api.Tests.csproj`.

**Spec:** `docs/customizable-workflow/package-format-v31/` — [`09-engine-and-pages.md`](../09-engine-and-pages.md) §§ 1, 2.1, 3.1 to 3.4, 3.6 and § 9's sentences for them; [`04-loops-and-functions.md`](../04-loops-and-functions.md) §§ 2 to 8 as they run; with [`03-expressions.md`](../03-expressions.md) §§ 5.3, 6, 9, 11.3, [`07-compiled-documents.md`](../07-compiled-documents.md) §§ 1 to 5, [`08-record.md`](../08-record.md) § 3 (the header members this area writes) and [`02-types-and-inputs.md`](../02-types-and-inputs.md) (inputs, `onInvalid`, `failIfEmpty`). The published contract is `external/consultologist-package-format/v31/`. Area issue #1503 (read its three comments: they are rulings), #1507's rulings 4 to 6, parent #1496, runbook [`BUILD-RUNBOOK.md`](../BUILD-RUNBOOK.md). What exists is in [`package-format-library.md`](../../package-format-library.md) (the four "specVersion 31" sections) and the plans of areas 02 to 05 (their decisions are cited "05-D10"). **Read the spec sections a task names, and the guide's sections, before writing its tests.**

**Not this area's** (each has a seam here): the Scriban dialect and how a body is composed (area 07: `IBodyComposer`); a deliverable's outcome, gating, delivery, monitoring, part 09 §§ 3.8, 3.9 (area 08: the runner settles a deliverable as `composed` and stops there); the record's storage, part 09 § 3.7 (area 09: `IRunRecorder`); `WorkflowPackages/Current`, publish, import, the publish note, dev's registry, part 09 §§ 2.2, 3.5, 4 (area 11, which asks `SpecVersionAdmission`); the Create, History and Profile pages (area 12, which calls `V31StartChecks`).

## Global Constraints

- **Worktree only:** `~/Projects/worktrees/m39-area-06` on `m39-area-06`. The content repo is worked in `~/Projects/consultologist-package-format` on `m39-build`, pushed directly; the pin under `external/` moves only to a pushed commit.
- **Never edit** `.github/`, `.claude/`, `scripts/m39-build/`, `AGENTS.md`, `CLAUDE.md` or `BUILD-RUNBOOK.md`, in any repo.
- **31 stays off in production:** `WorkflowPackageValidator.AcceptedSpecVersions` and `WorkflowPackageStore.SupportedSpecVersions` do not change; `V31ContractsTests` and `SpecVersionSetTests` stay green. With `WorkflowPackages__PreviewSpecVersions` unset, no door answers a 31 package differently from today, and no 31 code path makes a storage read.
- **The 5–30 path keeps its behaviour:** no existing golden, capture or test expectation changes, except where a task names it. `[Function]` strings, activity names and orchestrator class names already deployed are not renamed; new ones are additive and end in `V31`.
- **The orchestrator stays deterministic:** `PlanRunner` reads no clock but `IRunHost.UtcNow`, uses no `Guid`, no `Random`, no `Task.Delay`, no dictionary-order dependence (iterate in `graph.order`, list order or ordinal order only), and awaits only tasks the host gave it.
- **Sentences are used as written.** A sentence with a row in `v31/sentences.json` is that row with its `<…>` filled, under that row's id, held by `V31Contract.AssertSentence`. A rule with no row gets an area sentence `A06-NNN` (table below), added to `V31Contract` and the conformance README's list, and listed in the PR. Before using an A06 sentence, search `sentences.json` for a row that says the rule.
- **No sentence, log line or telemetry carries a value** from an input, a model or a profile: only ids, keys, paths, counts, limits, hashes, lengths and package text.
- **Never throw on package content or model output.** `RunPlan.Build`, `PlanRunner.RunAsync` and the store return a refusal or a failed run for anything; `ArgumentNullException` for a null argument is the only exception. Recursion over package structure is bounded.
- **One evaluator per compiled package version** (04-D15, 04-D47): `RunPlan` owns its `CorvusJsonLogicEvaluator`; none is process-wide.
- **Invariant culture, ordinal comparison,** everywhere. **Treat plan JSON as read-only:** `DeepClone` before placing a node in a new tree.
- **Fictional only:** invented people and clinical text (Margaret Okafor, Dr A. Sample).
- **Goldens come from code,** are read, and are listed in the PR.
- **Commits:** small, by concern: `PackageFormat (#1503): …`, `Api (#1503): …`, `Tests (#1503): …`, `Docs (#1503): …`; in the content repo `Format spec (Consultologist-Blazor#1503): …`. Every message ends with `Co-Authored-By: Claude <noreply@anthropic.com>`. Push after each task.
- **Tests:** library tests in `tests/V31Run/` (namespace `Consultologist.Api.Tests.V31Run`), engine tests in `tests/V31Engine/` (`Consultologist.Api.Tests.V31Engine`). Command: `dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~V31"`; a task that touches the 5–30 path or shared code also runs the whole `dotnet test Consultologist.sln --filter "FullyQualifiedName!~Consultologist.Web.E2E.Tests"`.
- **Refusal packages and compiled documents.** A task that adds a publish refusal or changes `resolve.json`'s bytes re-exports (`CONFORMANCE_EXPORT_V31=~/Projects/consultologist-package-format dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~RefusalPackagesExport|FullyQualifiedName~CompiledDocumentsExport"`), reads the diff, commits and pushes there, runs that repo's `scripts/check-v31.py`, and moves the pin, in the same task.

## Decisions this plan takes

Each goes in the PR body under *Decisions made during build*. R = ruled by the owner; the rest are where the spec is silent.

| # | Where | Choice |
|---|---|---|
| R1 | #1503, comment 1; part 04 § 2.4 | At 31 an empty or null input list is never refused at the start: the loop settles at 0 items, `.stopped` `"end"`. The 30 path keeps `NoApplicableDeliverable`. |
| R2 | #1503, comment 2; part 03 § 5.3, part 09 § 3.1 | Each evaluation of `matches` and of `has_word` has a time limit, the engine setting `ConsultGeneration__RegexMilliseconds`, default 2000. Past it the condition or computed value fails with A06-001, and the site fails as any failure there does. The bounds stay. *To flag in the PR:* the spec's "reads no clock" was there so a replay decides alike; with a limit, an evaluation near it can decide differently on a Durable replay, which fails the orchestration. |
| R3 | #1507, item 4 | A `while` with `checkFirst: true` whose `while` rule reads a node of its own body (`node.<id>` or `pass.node.<id>`) is refused at publish with A06-002. `pass.previous.node.<id>` is allowed (it is `null` on the first pass by § 3.3). |
| R4 | #1507, item 5 | A numeric `enum` in a format is supported: the dialect accepts it, a value is validated against it, and an enum input of numbers starts a job. |
| R5 | #1507, item 6 | A required input of a text format or `string` supplied blank (`""` or white space only, as `blank()` judges) is refused at the start with the missing-required-input sentence. |
| D1 | part 09 § 1.2 step 3 "the version's stamp" | The stamp of a 31 version is `publish.json` in the version's folder: `{ "specVersion": 31, "resolveHash": "sha256:…", "lockHash": "sha256:…" }`, read by `PublishedStamp.TryRead`, which ignores other members (area 11 writes it and may add some). The load path is chosen by the stamp, read first: a stamp with `specVersion` 31 takes the 31 path; anything else takes today's path untouched. So the engine never reads a 31 package's source tree, `manifest.json` included. |
| D2 | part 09 § 1.2 step 4 "break points … as configuration" | The break points are compiled in (`EngineBreakPoints.All`, empty today): they change only by an upgrade that went through the corpus re-render, which is a code change. |
| D3 | 04-D14 `plainConverts` | `resolve.json` keeps it: every node, check and deliverable entry gains `plainConverts`, an object from each rule member the entry has non-null (`if`, `expression`, `while`, `until`) to its list of booleans (`[]` when the rule has no `plain`). `RunPlan` compiles each rule with its list. The schema's `entry` is open, so no schema change; the goldens and the `compiled-documents` family are regenerated. A part's, a macro's and a snippet's `if` are area 07's and follow the same member. |
| D4 | part 09 § 1.2 step 4 `requires.spec`; 05-D47(7) | The store refuses a pair whose `requires.spec` is not `"31"` or whose `specVersion` is not 31 with S09-001's sentence, and one whose `package` does not name the folder it was read from with S07-006. |
| D5 | part 09 § 9 against part 07 § 10 | A hash or lock mismatch is said with part 07's rows as `CompiledPackage.Verify` gives them (S07-006, S07-017); part 09's S09-009 and S09-014 word the same refusals for the clinician and are what the job's start failure shows. The record keeps the S07 sentence in the operator's log, the S09 one on the job. |
| D6 | part 09 § 1.2 step 6, § 7.2 | The account's time zone is the account setting `account.timeZone` (an IANA name); the profile fields are `profile.credentials`, `profile.organisation`, `profile.closing` (settings), `name` the account's display name, `signature` the chosen signature block. Area 12's pages write these keys. `run.date` is the orchestration's start (`startedAtUtc`) in that zone. |
| D7 | part 09 § 3.4 retries, Durable | A retry is the orchestrator's: the activity makes one attempt and answers `Throttled` or `ServiceFailure` with the wait it worked out (the response's `retry-after`, else the back-off with its jitter), the runner waits on a durable timer and calls again, holding the slot. So every wait is in the history and a replay repeats it. The activity's Durable retry policy is none at 31. |
| D8 | part 08 § 3 `limits` | The header's `limits` has exactly the nine members part 08 lists; the regex time limit of R2 is an engine setting beside them and is not in the record. |
| D9 | part 09 § 3.6 the drain | `POST Admin/Drain` and `POST Admin/Resume` are authorised as the `Operator/*` endpoints are. While draining, every door still answers a new job with its id; the starter records the job and parks its orchestration input in a queue (one blob per job) instead of scheduling it, and Resume schedules what is parked, oldest first. A scheduled job whose timer ends during a drain waits for Resume before it starts (an orchestration started by this build carries `DrainAware`; one already in flight never meets the new check, so its replay is unchanged). `GET Admin/Drain` says how many orchestrations are producing or delivering; the release workflow polls it and enforces the 60 minutes. `Jobs__HoldNewJobs` keeps working as today. **The release workflow's two calls and its credential are the user's** (`.github/` is protected). |
| D10 | part 09 § 3.3 | `host.json`'s `maxConcurrentActivityFunctions` and the instance count are not set here: their values are sized to the model deployment's quota, which is the user's to state. `docs/` says how to size them. |
| D11 | part 04 § 2.5 `.content`; § 7.6 | A loop's `.content` is built by the runner as § 2.5 words it (`## <item name>`, a blank line, the item's text; items joined by a blank line; completed items only); an object or list value's `.content` is its canonical JSON. Area 07 owns anything richer. |
| D12 | part 09 § 3.1; the seams | Until area 07, `IBodyComposer`'s default renders a body with Scriban as it stands, over the scope the runner built, and is marked as the seam. Until area 09, `IRunRecorder`'s engine implementation keeps the header and progress on the job's entity and sends each finished row to telemetry as key, state and hashes only. |
| D13 | record keys, part 04 §§ 2.6, 7.9 | An instance's key is its entry's key (through the call for a function's node), then `:` and its item ids from the outermost loop inward joined by `/`; a `while` pass is `#<n>` after its while's position in that path (`node.refine.judge#2`, `node.sections.paragraphs.draft_paragraph:2/1`). Area 09 owns the final form; one function, `RunKeys.Of`, writes it. |

**Area sentences** (ids not in `sentences.json`):

| Id | Sentence |
|---|---|
| A06-001 | `<site> <member> could not be decided: <matches \| has_word> took longer than <n> seconds on this text. Use a simpler pattern.` |
| A06-002 | `While '<id>' declares checkFirst true, but its check reads node '<n>' of its own body, which has no value before the first pass: declare "checkFirst": false, or read something that runs before the while.` |
| A06-003 | `Node '<id>' computed a value that isn't <format>.` (a computed value that does not fit its format at run time, 04-D49; use a row of part 03 § 11.3 or part 02 if one says it) |
| A06-004 | `Workflow <package>@<version> isn't published.` (a folder with no stamp, or a stamp that cannot be read) |

## Files

```
src/Consultologist.PackageFormat/V31/
  IRegexp.cs, JsonLogicFunctions.cs, JsonLogicEvaluator.cs, ExpressionSentences.cs   the time limit (R2)      Task 1
  SourceResolve.Loops.cs, ResolveSentences.cs, FormatDialect.Enums.cs                R3, R4                   Task 2
  ResolveCompiler.Entries.cs                                                         plainConverts (D3)       Task 2
  PublishedStamp.cs                                                                  the stamp (D1)           Task 3
  Run/RunPlan.cs, Run/PlanEntry.cs, Run/PlanFormats.cs                               the plan                 Task 3
  Run/RunLimits.cs, Run/IRunHost.cs, Run/IRunRecorder.cs, Run/RunKeys.cs             the seams                Task 4
  Run/PlanRunner.cs, Run/RunScope.cs, Run/RunInstance.cs                             leaves, order, ceiling   Task 4
  Run/PlanRunner.Agents.cs                                                           retries, onInvalid       Task 5
  Run/PlanRunner.Loops.cs                                                            for                      Task 6
  Run/PlanRunner.Flow.cs                                                             while, if, case          Task 7
  Run/PlanRunner.Calls.cs                                                            call                     Task 8
src/Consultologist.Api/
  Workflow/SpecVersionAdmission.cs, Workflow/EngineAttestation.cs, Program.cs        admission (§ 2.1)        Task 10
  V31/EngineLimitSettings.cs                                                         the settings             Task 10
  V31/EngineBreakPoints.cs, V31/EngineCapabilitiesFactory.cs, V31/CompiledPackageStore.cs   the load          Task 11
  V31/IBodyComposer.cs, V31/RunAgentNodeV31Activity.cs, V31/RenderNodeV31Activity.cs,
  V31/DurableRunHost.cs, V31/EntityRunRecorder.cs, V31/ConsultGenerationOrchestratorV31.cs,
  Agents/AgentSectionGenerator.cs (+ one method)                                     the orchestration        Task 12
  V31/V31StartChecks.cs, V31/V31JobStarter.cs, Jobs/ConsultGenerationJobStarter.cs,
  Jobs/ConsultGenerationState.cs, Auth/AccountModels.cs                              job start                Task 13
  Jobs/EngineDrain.cs, Jobs/AdminDrain.cs, Jobs/ConsultGenerationEngine.cs (the DrainAware check)   the drain Task 14
tests/V31Run/        RunFixture.cs (plans from ResolveFixture, a scripted FakeRunHost, a ListRecorder), *Tests.cs,
                     RunGoldenTests.cs, Golden/*.run.txt                                                      Tasks 3–9
tests/V31Engine/     *Tests.cs, FakeRegistry.cs                                                               Tasks 10–14
tests/V31Source/     V31Contract (A06 rows), RefusalPackages.Engine.cs
docs/customizable-workflow/package-format-library.md   + "specVersion 31: the run (preview)"                  Task 15
docs/customizable-workflow/engine-v31.md               load, settings, limits, the drain, sizing (D10)        Task 15
consultologist-package-format (m39-build)   v31/conformance/refusal-packages/**, compiled-documents/**, README.md   Task 2
```

## Review Focus

1. **A compiled document that is not what publish wrote:** a node whose `parent`, `ret`, `in` or `function` names a key that is not there; a rule naming an operation outside part 03 § 5; a body list that holds its own loop; `graph.order` missing a key; a member of the wrong JSON kind. Expected: `RunPlan.Build` refuses or the run fails with a sentence, never an exception, never a hang. (Task 3 `ACorruptDocument_IsRefusedOrFails_NeverThrows`.)
2. **Model output that is not what the format says:** an empty reply, text where JSON is due, JSON 600 levels deep, a 2 MiB reply, a list of 5,000 where a loop iterates it, `null` for a required field. Expected: the node fails or repairs as `onInvalid` says, a loop over 200 fails with S04-078, and nothing throws. (Task 5 `AnyReply_SettlesTheNode`; Task 6 `ALoopGivenTooManyItems_FailsWithItsSentence`.)
3. **The ceiling under nesting:** three nested parallel loops of 10 with `maxParallel` 3, 2 and `null`, a call inside, ceiling 4, with replies arriving in a shuffled order. Expected: never more than 4 in flight, never more than a loop instance's cap, every dispatch in the fixed order whatever the completion order, and the same dispatch sequence on a second run. (Task 9 `TheCeiling_HoldsUnderNesting_AndDispatchIsDeterministic`.)
4. **A job that outlives its limit while calls are in flight, and a service that stays busy:** the clock passes `jobMinutes` with three calls unanswered; a call answered `Throttled` for eleven minutes. Expected: the job fails with S09-012 and each running row is `cancelled` with S08-009, late answers ignored; the throttled node fails with S09-010 after 10 minutes of waits. (Task 5.)
5. **A drain met from every door:** a start, a scheduled job's timer ending, an email start, each during a drain; Resume called twice; a drain with nothing parked. Expected: each job gets an id and waits, each starts exactly once after Resume, and a second Resume starts nothing twice. (Task 14 `EveryDoor_WaitsAndStartsOnce`.)

---

### Task 1: The time limit on `matches` and `has_word` (R2)

**Spec:** #1503 comment 2; part 03 §§ 5.3, 11.3; 04-D15, 04-D47, 04-D48.

**Files:** Modify `IRegexp.cs`, `JsonLogicFunctions.cs`, `JsonLogicEvaluator.cs`, `ExpressionSentences.cs`; tests `tests/V31Logic/MatchTimeLimitTests.cs`; `V31Contract` (A06-001).

**Interfaces — Produces:**
- `public CorvusJsonLogicEvaluator(TimeSpan? matchTimeLimit = null)`; `null` keeps today's behaviour (no limit), which publish-time probing and existing tests use. `public TimeSpan? MatchTimeLimit { get; }`.
- `JsonLogicFailureKind.MatchTooSlow`, with `Detail` the operation's name (`matches` or `has_word`).
- `ExpressionSentences.RunTime` gives A06-001 for it, `<n>` the limit in seconds (invariant, at most three decimals, no trailing zeros).

`matches`: the pattern is run with the instance's limit as the `Regex` match time-out (a limited evaluator keeps its own pattern cache, since the time-out is part of a compiled `Regex`); a `RegexMatchTimeoutException` becomes the failure. `has_word`: the ordinal search checks a `Stopwatch` every 4,096 candidate positions. The failure is returned in the result as every failure is; nothing throws.

- [x] **Step 1: Write the failing tests.**
  - `ASlowPattern_FailsWithA06001`: `(.*a.{100}){3}b` on 1 MiB of `a`, limit 50 ms → `Decide` gives `Failure.Kind == MatchTooSlow`, within 2 s of wall time; `RunTime("Node 'x'", "if", failure)` passes `V31Contract.AssertSentence` and reads `… matches took longer than 0.05 seconds on this text. Use a simpler pattern.`
  - `AFastPattern_IsUnchangedByALimit`: every vector of `tests/V31Logic`'s `matches` and `has_word` cases gives the same result with a 2 s limit as with none.
  - `NoLimit_NeverTimesOut`: the default constructor has `MatchTimeLimit == null`.
  - `HasWord_HonoursTheLimit`: a `has_word` over 1 MiB with a limit of `TimeSpan.FromTicks(1)` fails `MatchTooSlow` with detail `has_word`.
  - `TwoEvaluators_WithDifferentLimits_DoNotShareACache`: the same pattern through a limited and an unlimited instance; the unlimited one never fails.
- [x] **Step 2: Run them; they fail.**
- [x] **Step 3: Implement.**
- [x] **Step 4: Run `--filter "FullyQualifiedName~V31"`; all pass.**
- [x] **Step 5: Commit and push.** `PackageFormat (#1503): matches and has_word stop at the engine's time limit`

### Task 2: Two rulings at publish, and `plainConverts` in the compiled document (R3, R4, D3)

**Spec:** #1507 items 4 and 5; part 04 § 3; part 02's dialect (enums); 04-D14; part 07 § 2.5.

**Files:** Modify `SourceResolve.Loops.cs`, `ResolveSentences.cs`, `FormatDialect.Enums.cs` (and `FormatValues.cs`, `SentFormat.cs` only if a numeric enum trips them), `ResolveCompiler.Entries.cs`; tests `tests/V31Resolve/SourceResolveLoopsTests.cs`, `tests/V31Types/NumericEnumTests.cs`, `tests/V31Resolve/PlainConvertsTests.cs`; `tests/V31Source/RefusalPackages.Engine.cs` (a `partial`, added to `All()`), `V31Contract` (A06-002); the goldens in `tests/V31Resolve/Golden/`; the content repo.

**Interfaces — Produces:** in `resolve.json`, on every entry of `nodes`, `checks`, `deliverables` and of a function's `nodes`: `"plainConverts": { "<member>": [bool, …] }` for each of `if`, `expression`, `while`, `until` the entry has non-null, from `ExpressionCheck.PlainConverts` of that site; `{}` when it has none.

- [x] **Step 1: Write the failing tests.**
  - `ACheckFirstWhile_ReadingItsOwnBody_IsA06002` (theory: `node.judge.value.verdict`, `pass.node.judge.value.verdict`): `ResolveFixture.NestedNote()` with `refine`'s `checkFirst` set `true` → exactly one refusal, A06-002, naming `refine` and `judge`, path `nodes/refine/index.json`. `ACheckFirstWhile_ReadingAnEarlierPassOrOutside_IsAccepted` (theory: `pass.previous.node.judge.value.verdict`, `pass.number`, `node.plan.value.sections`).
  - `NumericEnumTests`: a package format `{ "type": "integer", "enum": [1, 2, 3] }` and `{ "type": "number", "enum": [0.5, 1] }` pass the dialect; `CorvusFormatValueValidator` accepts `2` and refuses `4`; an input of that format compiles; a mixed `["a", 1]` enum is still what the dialect said before this task (assert the current refusal id, unchanged).
  - `PlainConvertsTests.EveryRuleMember_HasItsList`: compile a fixture with a computed node `{"plain":[{"var":"input.consult_draft.content"}]}` (a Markdown input: converts) and one over `plaintext` (does not): the entries read `{"expression":[true]}` and `{"expression":[false]}`; an agent with an `if` and no `plain` reads `{"if":[]}`; a `for` with `until` null reads `{}`.
- [x] **Step 2: Run; fail.**
- [x] **Step 3: Implement.** Regenerate the goldens (`GOLDEN_UPDATE_V31=1`), read the diff (only `plainConverts` members appear), add the A06-002 refusal case, re-export both families, push the content repo, run `check-v31.py`, move the pin.
- [x] **Step 4: Run all V31; pass.**
- [x] **Step 5: Commit and push.** `PackageFormat (#1503): a checkFirst while can't read its own body; numeric enums; plainConverts compiled in`

### Task 3: The stamp and the in-memory plan

**Spec:** part 09 § 1.2 steps 3 and 5; part 07 §§ 1, 2; part 03 § 9; 05-D47(1); D1, D3.

**Files:** Create `PublishedStamp.cs`, `Run/RunPlan.cs`, `Run/PlanEntry.cs`, `Run/PlanFormats.cs`; modify `FormatValues.cs` (one overload); tests `tests/V31Run/{RunFixture,PublishedStampTests,RunPlanTests,PlanFormatsTests}.cs`.

**Interfaces — Produces:**
- `public sealed record PublishedStamp(string ResolveHash, string LockHash)` with `public const string FileName = "publish.json"`, `public const int SpecVersion = 31`, `public string ToJson()` (canonical, with `specVersion`), `public static PublishedStamp? TryRead(string? json)` (null unless an object with `specVersion` 31 and two `sha256:` + 64 lowercase hex strings; other members ignored; never throws).
- `public enum PlanKind { Agent, Rendered, Computed, For, While, If, Case, Call, Deliverable }`.
- `public sealed class PlanEntry` — `Key`, `Id`, `Kind`, `Parent` (key or null), `Function` (id or null), `Order` (position in `graph.order`; a function's node: its position in the function's body walk), `Json` (the entry), `Format` (the entry's `format` node), `If`, `Expression`, `While`, `Until` (`IJsonLogicRule?`, compiled with D3's lists), `Printed(string member)` (`JsonLogicPrinter.Print` of that rule), `Body` (`IReadOnlyList<string>` child keys, for a `for`, `while`, a function), `Branches` (for `if`: then, else; for `case`: each branch with its `when`, and else; each `(IReadOnlyList<string> Nodes, string? Ret)`), `Ret`, `Reads` (the entry's `reads` plus every `var` path of its rules, `in`, `case` and `with` values), `Depends` (the keys of the plan those reads name: longest key that is a prefix of the path at a dot; a read through `item`, `pass`, `param`, `run`, `input`, `profile`, `snippet` names none; `pass.previous.…` names none).
- `public sealed class PlanFunction` — `Id`, `Inputs` (ordered ids), `Body`, `Ret`, `Entries` (its local map), `Format`.
- `public sealed class RunPlan` — `Package`, `Version`, `ResolveHash`, `LockHash`, `Document` (`JsonObject`), `Entries` (nodes, checks and deliverables by key), `TopLevel` (keys with no parent, in `graph.order`), `Functions`, `Inputs` / `InputOrder`, `NeedsProfile`, `RunFacts` (the `run` list), `Snippets` (key → entry JSON), `Formats` (`PlanFormats`), `Evaluator` (its own, 04-D47), and
  `public static (RunPlan? Plan, SourceRefusal? Refusal) Build(string resolve, string resolveHash, string lockHash, TimeSpan? matchTimeLimit)`.
  A document `Build` cannot make a plan from (a key named and absent, a member of the wrong kind, a `ret` outside its body, a body that contains an ancestor, a rule the evaluator declines) is refused with S07-006's sentence for that package (it is not what publish wrote); the caller has already run `Verify`, so this is for corrupt input.
- `public sealed class PlanFormats` — `public JsonNode Expand(JsonNode? format)` (every `#/$defs/<key>` replaced by that entry, `$id` and `$schema` dropped from inlined entries, to depth 64; a cycle or a missing key gives `false`, the schema nothing fits); `public FormatValueResult Validate(JsonNode? value, JsonNode? format)`; `public bool IsText(JsonNode? format)` (a text format: `plaintext`, `markdown_block`, `markdown_inline` by the bundle's `$id`); `public (JsonNode? Value, bool Parsed) Read(string reply, JsonNode? format)` (a text format: the reply as a string; otherwise the reply parsed as JSON, to 512 levels).
- `CorvusFormatValueValidator.ValidateExpanded(JsonNode? value, JsonNode schema)`: validation of a self-contained schema (what `Validate` does after it expands).
- `tests/V31Run/RunFixture.cs`: `Plan(GoldenCase)` and `Plan(Dictionary<string, byte[]> files, ITemplateReads reads)` (through `CompiledPackage.Publish` with `CompiledGoldenTests.Fixed`, then `RunPlan.Build`); used by every later task.

- [x] **Step 1: Write the failing tests.**
  - `PublishedStampTests.RoundTrips`; `TryRead_RefusesAnythingElse` (theory: null, `""`, `"[]"`, a 30 stamp from `WorkflowPackageStamp.ToJson()`, `specVersion` 30, a hash of 63 hex, upper-case hex, not JSON) → null.
  - `RunPlanTests.TheGoldens_Build`: each of the three goldens builds; nested-note's `TopLevel` is `node.plan, node.section_count, node.sections, node.refine, deliverable.note`; `node.sections.paragraphs.draft_paragraph`'s `Parent` is `node.sections.paragraphs`, its `Depends` holds no key (it reads `item` only), `node.section_count.Depends` is `[node.plan]`, `node.refine.While` is compiled and `Printed("while")` is `node.refine.judge.value.verdict ≠ "good"`; functions' `draft_section` has three entries and `Ret` `node.apply_instructions`.
  - `RunPlanTests.EachPlan_HasItsOwnEvaluator`: two builds of one document give different `Evaluator` instances, each with the limit passed.
  - `RunPlanTests.ACorruptDocument_IsRefusedOrFails_NeverThrows`: from each golden, 300 mutations by `Random(31)` (delete a member, swap a value for another JSON kind, rename a key in `nodes`, point a `ret`, `parent`, `in`, `function` or a body id at a key that is not there, put a loop in its own body, drop a key from `graph.order`, replace a rule's operation with `eval`) → `Build` returns; when it returns a plan, the plan's every `Parent`, `Ret`, `Body` id and `function` names something present.
  - `PlanFormatsTests`: `Expand` of `{"$ref":"#/$defs/check_result"}` has no `$ref` and no `$id`; `Validate` accepts a `check_result`, refuses one missing `pass`; a list-of-`plaintext` format validates `["a"]`; `Read("{\"a\":1}", plaintext)` is the string; `Read("not json", concept_list)` is not parsed; a self-referencing `$defs` entry expands to `false` and validates nothing.
- [x] **Step 2: Run; fail.**  - [x] **Step 3: Implement.**  - [x] **Step 4: Run all V31; pass.**
- [x] **Step 5: Commit and push.** `PackageFormat (#1503): the published stamp, and the plan an engine builds from resolve.json`

### Task 4: The runner: leaves, readiness, the dispatch order and the ceiling

**Spec:** part 09 §§ 3.1, 3.2, 3.4 (total-work limits); part 04 § 8; part 03 §§ 6, 9, 11.3; 04-D43, 04-D49; D11, D13.

**Files:** Create `Run/RunLimits.cs`, `Run/IRunHost.cs`, `Run/IRunRecorder.cs`, `Run/RunKeys.cs`, `Run/PlanRunner.cs`, `Run/RunScope.cs`, `Run/RunInstance.cs`; tests `tests/V31Run/{FakeRunHost,ListRecorder,PlanRunnerLeavesTests,PlanRunnerCeilingTests}.cs`.

**Interfaces — Produces** (namespace `Consultologist.PackageFormat.V31.Run`):
```csharp
public sealed record RunLimits(int MaxAgentCalls = 16, int CapacityWaitMinutes = 10, int ServiceAttempts = 5, int JobMinutes = 30,
    int DeliveryMinutes = 15, int DeliveryAttempts = 3, int LoopItems = 200, int JobAgentCalls = 500, int RenderSteps = 100_000)
{ public JsonObject ToJson(); }   // part 08 § 3's nine members: maxAgentCalls, capacityWaitMinutes, serviceAttempts, jobMinutes, deliveryMinutes, deliveryAttempts, loopItems, jobAgentCalls, renderSteps

public sealed record RunInputs(IReadOnlyDictionary<string, JsonNode?> Inputs,      // by input id: { value, content, title } as part 02 reads them
    JsonObject RunFacts, IReadOnlyDictionary<string, string?> Profile);

public enum AgentOutcomeKind { Completed, Throttled, ServiceFailure }
public sealed record AgentCall(string Key, string EntryKey, string? Function, int Attempt, bool Repair, JsonObject Scope);
public sealed record AgentOutcome(AgentOutcomeKind Kind, string? Reply, int WaitMilliseconds, string? ErrorKind);   // ErrorKind: "timeout" | "error" | "unreachable"
public sealed record RenderCall(string Key, string EntryKey, string? Function, JsonObject Scope, int StepLimit);
public sealed record RenderOutcome(string? Text, bool TooManySteps, string? Failure);

public interface IRunHost
{
    DateTime UtcNow { get; }
    Task<AgentOutcome> CallAgentAsync(AgentCall call);
    Task<RenderOutcome> RenderAsync(RenderCall call);
    Task DelayAsync(TimeSpan wait);
}

public enum RowState { Completed, Skipped, NotRun, Failed, Cancelled, Composed }
public sealed record RunRow(string Key, string EntryKey, PlanKind Kind, RowState State, string? Reason, string? ValueHash,
    JsonObject Detail);          // what area 09 turns into a storage-12 row: counts, stopped, retries, itemLabel, function, depth
public sealed record RunDecision(string Site, string Member, string Printed, bool? Result, string? Branch, DateTime DecidedAtUtc);
public interface IRunRecorder { Task RowAsync(RunRow row); Task DecisionAsync(RunDecision decision); }

public enum RunState { Completed, Failed, TimedOut }
public sealed record RunResult(RunState State, string? Sentence, string? FailedKey,
    IReadOnlyDictionary<string, JsonNode?> Values,      // every top-level entry's settled read form { value, content, state, … }
    int AgentCalls, int PeakInFlight);

public sealed class PlanRunner
{
    public PlanRunner(RunPlan plan, RunLimits limits, IRunHost host, IRunRecorder recorder);
    public Task<RunResult> RunAsync(RunInputs inputs, DateTime startedAtUtc);
}
public static class RunKeys { public static string Of(string entryKey, IReadOnlyList<string> itemPath, IReadOnlyList<(int Position, int Pass)> passes); }
```

How it runs (this task: the top level's leaves; Tasks 6 to 8 add frames):
- A **frame** is one running of a body: the top level, a loop item, a while pass, a branch, a call. It holds an instance per body entry, its `item` and `pass` bindings, its parameters, and its parent frame. `RunScope.Data(entry, frame, paths)` builds the evaluator's data object for exactly the paths a rule reads: roots `input`, `run`, `node`, `check`, `item`, `pass`, `param`; `node.<key path>` is nested objects along the key's dots, each visible instance read at the reader's own path; a skipped, not-run or absent thing reads `null` at `.value` and `.content`, and its `.state` says which.
- An instance is **ready** when every key in its entry's `Depends` that is visible from its frame has settled.
- **Each round:** settle everything that needs no call, in the fixed order, until nothing more settles (an `if` deciding, a `computed` evaluating and its value validated against its format (A06-003 when it does not fit), a skip); then dispatch ready agent and render calls in the fixed order — `Order`, then item path (outer items first, then list order), then pass — an agent call only while in flight < `MaxAgentCalls` and its loops' caps allow; then `await Task.WhenAny` of what is in flight and settle what finished. The run ends when nothing is in flight and nothing is ready.
- **`if` on a leaf:** false or `null` → `Skipped`, reason `JsonLogicPrinter.Reason("skipped", rule, false)`, a `RunDecision`; a failure (`NotABoolean`, `MatchesTextTooLong`, `MatchTooSlow`, …) fails the node with `ExpressionSentences.RunTime`.
- **A failed node fails the job:** nothing new is dispatched, calls in flight are awaited no further, `RunResult` is `Failed` with the node's sentence and key.
- **`JobAgentCalls`:** the call that would be the 501st is not made; the job fails with S09-015. A retry or repair is part of its call.
- **`rendered` and deliverables** take no slot; a deliverable's `if` is decided like a leaf's, and a rendered deliverable settles `Composed` (area 08 takes it from there). `TooManySteps` fails the node with part 09 § 9's *"<Subject> stopped rendering: it took more than <limit> steps."* (find its row id).
- **`.content`** of a leaf is its text for a text format, else the canonical JSON of its value (D11).

`FakeRunHost` (tests): scripted by `EntryKey` or `Key` → reply or outcome; answers are held until the test releases them (`Release(key)`, `ReleaseAll()`, `ReleaseInOrder(Random)`), or at once in auto mode; records `Dispatched` (keys in order), `InFlightPeak`, per-loop-instance peaks; `UtcNow` and `DelayAsync` on a manual clock (`Advance(TimeSpan)`).

- [x] **Step 1: Write the failing tests** (`RunFixture` packages built on `SourceFixture.Minimal()`):
  - `ASingleAgent_Completes`: one agent reading `input.consult_draft.content`; the call's `Scope` holds exactly `input.consult_draft.content`; the row is `Completed` with `ValueHash` = `CanonicalJson.HashText(reply)`; `RunResult.Values["node.plan"]` reads `value`, `content`, `state: "completed"`.
  - `AComputedNode_IsEvaluatedInline_WithNoCall`: `node.section_count` over a stubbed `node.plan` → value 3, `host.Dispatched` holds only `node.plan`.
  - `AComputedValue_ThatDoesNotFitItsFormat_FailsWithA06003`.
  - `AnIfThatIsFalse_SkipsTheLeaf_AndReadersSeeNull`: reason `skipped: … is false` (S03-001's form), a decision recorded with `Result == false`; a computed node reading its `.content` gets `null`.
  - `AConditionThatYieldsNoBoolean_FailsItsNode` (S03-041); `ASlowMatch_FailsItsNode` (A06-001, limit 1 tick).
  - `Readiness_FollowsReads`: `b` reads `a`, `c` reads nothing: `a` and `c` are dispatched before `b`, `b` only after `a` is released.
  - `PlanRunnerCeilingTests.NeverMoreThanTheCeiling`: 10 independent agents, ceiling 4: `InFlightPeak == 4`, and `Dispatched` is in `graph.order` whatever order the test releases in (three seeds).
  - `TheCallLimit_FailsTheJob`: `JobAgentCalls = 3`, 4 agents → `Failed`, S09-015 with `3`.
  - `AFailedCall_FailsTheJob_AndDispatchesNothingMore`.
  - `ADeliverable_IsRenderedAndSettlesComposed`; `ADeliverableWhoseIfIsFalse_IsSkipped`; `ARenderPastItsSteps_FailsWithItsSentence`.
  - `TheRunner_ReadsNoClockButTheHosts`: a source scan of `Run/*.cs` finds no `DateTime.UtcNow`, `DateTime.Now`, `Stopwatch`, `Guid.NewGuid`, `new Random`, `Task.Delay`, `Task.Run`.
- [x] **Step 2: Run; fail.**  - [x] **Step 3: Implement.**  - [x] **Step 4: Run all V31; pass.**
- [x] **Step 5: Commit and push.** `PackageFormat (#1503): the runner: leaves in their fixed order, under the per-job ceiling`

### Task 5: Agent replies: `onInvalid`, retries, and the job limit

**Spec:** part 09 § 3.4 and § 9 (S09-010, S09-011, S09-012; S08-009); part 02 (`onInvalid`, `failIfEmpty`: find their sections and sentences with `grep -n "onInvalid\|failIfEmpty"` over the parts and `sentences.json`); D7.

**Files:** Create `Run/PlanRunner.Agents.cs`; tests `tests/V31Run/PlanRunnerAgentsTests.cs`.

**Interfaces — Consumes** Task 4's types. **Produces** in `RunRow.Detail` for an agent row: `retries`: `[{ "atUtc", "waitMilliseconds", "for": "capacity" | "service", "errorKind" }]`, `repaired`: bool.

Rules:
- A `Completed` reply is read by `PlanFormats.Read` and validated. Invalid with `onInvalid: "retry"`: one repair call (`Repair = true`, `Attempt + 1`), same slot, counted as the same call; invalid again, or `onInvalid: "fail"`, fails the node with the spec's sentence for an invalid output. `failIfEmpty` as part 02 says.
- `Throttled`: add `WaitMilliseconds` to the node's capacity wait; if the total passes `CapacityWaitMinutes`, the node fails with S09-010 (`<n>` the limit); else `await host.DelayAsync(wait)` and call again. The slot is held throughout.
- `ServiceFailure`: the node's service attempts count; at `ServiceAttempts` the node fails with S09-011 (`timed out` / `returned an error` / `couldn't be reached` by `ErrorKind`, `<n>` the attempts); else delay `WaitMilliseconds` and call again.
- **The job limit:** when `host.UtcNow - startedAtUtc` reaches `JobMinutes` (checked whenever the runner wakes, with a `DelayAsync` for the time left among the awaited tasks), the run is `TimedOut` with S09-012; every instance in flight gets a `Cancelled` row with S08-009; nothing later is read.

- [x] **Step 1: Write the failing tests.**
  - `AnInvalidReply_IsRepairedOnce_InTheSameSlot` (ceiling 1: the repair is dispatched before any other waiting call; `AgentCalls == 1` for the node); `AnInvalidReply_WithOnInvalidFail_FailsTheNode`; `ASecondInvalidReply_FailsTheNode`.
  - `AnyReply_SettlesTheNode` (theory: `""`, white space, `not json` for a data format, valid JSON 600 levels deep, a 2 MiB string, `null`, a list where an object is due): the node completes, repairs or fails with a sentence; `RunAsync` never throws.
  - `AThrottledCall_WaitsAndRetries_HoldingItsSlot`: three `Throttled(1000)` then a reply → completed, `retries` has three `capacity` entries, the clock advanced 3 s, in flight never dropped below 1.
  - `ABusyService_FailsTheNodeAfterTenMinutes`: `Throttled(60000)` forever → S09-010 with `10`, after the eleventh wait is refused.
  - `AFailingService_FailsAfterFiveAttempts` (theory over the three kinds and their words) → S09-011 with `5`.
  - `AJobPastItsLimit_TimesOut_AndCancelsWhatIsRunning`: three calls never answered, clock advanced 30 minutes → `TimedOut`, S09-012 with `30`, three `Cancelled` rows with S08-009; releasing the calls afterwards changes nothing.
- [x] **Step 2: Run; fail.**  - [x] **Step 3: Implement.**  - [x] **Step 4: Run all V31; pass.**
- [x] **Step 5: Commit and push.** `PackageFormat (#1503): repairs, retries with recorded waits, and the job limit`

### Task 6: `for`

**Spec:** part 04 §§ 2, 4, 8; part 06 § 3.2 (check loops per item); R1; D11, D13.

**Files:** Create `Run/PlanRunner.Loops.cs`; tests `tests/V31Run/PlanRunnerLoopsTests.cs`.

**Interfaces — Produces:** a settled loop reads `.value` (completed items' values, in item order), `.items` (§ 2.6's element: `item { id, value[, content] }`, `label`, `state`, `reason`, `value`, `content`, `nodes { <body id>: { value, content, state } }`), `.content` (D11), `.state`, `.stopped`; its row's `Detail` holds `parallel`, `maxParallel`, `itemLabel`, `count`, `stopped`, `reason`; each item's row is keyed by `RunKeys.Of`.

Rules:
- **The source** is resolved when the loop is ready: an array input (ids 0-based), a node's list (1-based), a snippet folder (`snippet.<folder>`: ids the file stems, `item.value` the header data, `item.content` the body, from `RunPlan.Snippets`), `item.value.<field>`, `param.<id>.value`, a loop's `.items`. Empty → 0 items, `"end"`. `null` → 0 items, `"end"`, reason S04-077. More than `LoopItems` → the loop fails with S04-078. Not a list at run time → the loop fails (find or word the sentence as A06-003's sibling).
- **`max`:** only the first `max` items get frames; the rest are `NotRun` with S04-002, `.stopped` `"bound"`.
- **`parallel: true`:** every item's frame is opened at once; `maxParallel` caps the item frames of **this loop instance** with an agent call in flight or a body unfinished to `maxParallel` at a time, the next item starting in list order when one completes. **`parallel: false`:** item *n*+1's frame opens when item *n* settles.
- **`until`** (sequential only): evaluated after each item over that item's frame; when it holds, the rest are `NotRun` with S04-001 (`<position name>` = `<loop label> <n>`, 1-based), `.stopped` `"until"`; a decision is recorded per evaluation.
- **An item's state** is its `ret`'s state. **Names:** `itemLabel`'s field trimmed, cut to 120 characters ending `…`; empty after trimming, or `itemLabel` null → `<loop label> <n>`, marked `positionName: true` on the item's row. Reasons always use position names.
- **Over `.items`** (a check loop, or a production loop): the loop is ready when the production loop has opened its items; item *i* is ready when production item *i* has settled, so checking overlaps production; its `item` is the `.items` element and its id is the production item's id.
- **A failed item fails the job** (#1507 ruling 2).

- [x] **Step 1: Write the failing tests.**
  - `NestedLoops_RunEveryItem_AndReadByPath`: nested-note with a 2-section plan of 2 and 3 points → 5 `draft_paragraph` calls keyed `node.sections.paragraphs.draft_paragraph:1/1` … `:2/3`; each call's scope holds its own `item.value` and `item.parent.value.title`; `node.sections.value` is a list of two lists.
  - `AnEmptyList_SettlesAtZeroItems` (`"end"`, no call, the job completes: R1); `ANullSource_SettlesAtZero_WithItsReason` (S04-077); `ALoopGivenTooManyItems_FailsWithItsSentence` (201 items, S04-078 with `201` and `200`; and a model list of 5,000).
  - `MaxParallel_CapsEachLoopInstance`: outer `maxParallel` 2 over 4 items, inner `maxParallel` 3 over 5: `host` per-instance peaks are ≤ 3 and ≤ 2, overall peak ≤ 6 with ceiling 16 and ≤ 4 with ceiling 4.
  - `ASequentialLoop_RunsInOrder`; `Until_StopsAfterTheItemWhereItHolds` (§ 2.4's example: stops at letter 2 of 4; items 3 and 4 `NotRun` with `stopped early at Prior letters 2`; `.stopped` `"until"`; `.value` has 2 elements, `.items` 4).
  - `Max_RunsTheFirstN` (S04-002, `"bound"`).
  - `ItemLabel_NamesItems_AndFallsBack` (a title of 200 characters is cut to 120 ending `…`; a blank title gives `Sections 2`).
  - `ASnippetFolderLoop_ReadsHeaderAndBody` (functions fixture: ids `hpi`, `assessment_plan`).
  - `ACheckLoop_RunsEachItemAsItsProductionItemSettles`: functions fixture, production items released one by one: `check.section_coverage.coverage:<id>` settles right after its production item, before the next production item is released; ids match.
  - `ALoopsContent_IsItsItemsUnderTheirNames` (D11's exact bytes); `ASkippedRet_MakesTheItemSkipped` (absent from `.value`, present in `.items` with the printed reason).
  - `AFailedItem_FailsTheJob`.
- [x] **Step 2: Run; fail.**  - [x] **Step 3: Implement.**  - [x] **Step 4: Run all V31; pass.**
- [x] **Step 5: Commit and push.** `PackageFormat (#1503): for loops run as resolve.json writes them`

### Task 7: `while`, `if` and `case`

**Spec:** part 04 §§ 3, 5; part 03 § 6.1; the reasons S04-005, S04-006 / S04-049.

**Files:** Create `Run/PlanRunner.Flow.cs`; tests `tests/V31Run/PlanRunnerFlowTests.cs`.

Rules:
- **`while`:** passes run one after another, each its own frame with `pass.number`, `pass.previous.node.<id>` (`null` on pass 1), `pass.parent`, `pass.history` (readable through `size`). `checkFirst: false`: the `while` rule is decided after each pass; `true`: before each pass (zero passes possible: `.passes` 0, `.stopped` `"check"`, the while reads as skipped). `until` is decided after each pass; whichever stops first wins. At `max` with the check still holding: `.stopped` `"bound"`, reason S04-005's form with the real bound, and the run continues with the last pass's output. Reads from outside: `.value` and `.content` (the last pass's `ret`), `.passes`, `.stopped`, `.history`, `.state`. Each decision is recorded with its pass.
- **`if`:** decided when what its rule reads has settled; the taken body's frame opens; no `else` and false → `Skipped` with the printed reason. Its read form is its taken body's `ret`'s.
- **`case`:** the subject is a reference; the branch whose `when` holds its value runs; `null` or an unlisted value goes to `else`; an empty branch → `Skipped` with *"case '<id>': arm for <value> does nothing"*; the decision records the branch taken.
- A flow node inside an untaken branch is `Skipped`; its inner entries get no rows.

- [x] **Step 1: Write the failing tests.**
  - `AWhile_RunsUntilItsCheckStopsHolding` (nested-note's `refine`: verdicts `poor`, `good` → 2 passes, `"check"`, keys `node.refine.judge#1`, `#2`, value pass 2's `draft_summary`); `AWhile_AtItsBound_ContinuesWithTheLastPass` (3 × `poor`: `"bound"`, reason `stopped at its bound of 3; the check never stopped holding`, the job completes); `ACheckFirstWhile_ThatFailsAtOnce_RunsNoPass` (reads as skipped, a reader gets `null`); `Until_StopsAWhile` (`"until"`); `APass_ReadsThePreviousOne` (`null` on pass 1, pass 1's value on pass 2); `PassHistory_IsSizedInAConditions`.
  - `AWhileInsideALoop_RunsPerItem` (keys carry both, `…:2#1`); `ALoopInsideAWhile_RunsPerPass`.
  - `AnIf_RunsThenOrElse`; `AnIfWithNoElse_ThatIsFalse_IsSkipped` (§ 5.4's `narrative_only` over items of kinds narrative, table: the reason is `skipped: item.value.kind = "narrative" is false`); `ACase_RunsTheMatchingBranch` (§ 5.4's `route`: narrative, table, omit → two drafts and one skip with `case 'route': arm for omit does nothing`); `ANullSubject_GoesToElse`; `AnUnlistedValue_GoesToElse`.
  - `EveryDecision_IsRecordedOnce_WithItsPrintedCondition`.
- [x] **Step 2: Run; fail.**  - [x] **Step 3: Implement.**  - [x] **Step 4: Run all V31; pass.**
- [x] **Step 5: Commit and push.** `PackageFormat (#1503): while, if and case, each decision recorded`

### Task 8: `call`: a function's body inline

**Spec:** part 04 § 7 (7.4, 7.7, 7.9), § 8; part 09 § 3.1.

**Files:** Create `Run/PlanRunner.Calls.cs`; tests `tests/V31Run/PlanRunnerCallsTests.cs`.

Rules: a call is ready when its arguments' keys have settled; its `if` is decided like a leaf's. It opens a frame over the function's `Body` with `param.<id>` bound to each argument: a field path gives that value as `.value` (and its text as `.content`); a whole reference (`node.<id>`, `check.<id>`, `item`, `item.parent`, `param.<id>`) gives only `.value` and `.content`; `null` gives `null`; `run.<fact>`, `profile.<field>`, `snippet.<folder>.<item>` give their values. Inside the frame `node.<id>` resolves in the function's own entries and never outward; `item` and `pass` of the caller are not visible. Its nodes are scheduled by the same rounds, so the ceiling, the call limit and the fixed order hold; their `Order` is the call's `Order`, then the function's body order. Keys go through the call node (`node.standards.section.patient_draft:hpi`); the call's row has `function`, `depth` and its arguments' hashes; the call's value is the function's `ret`'s. Depth is not checked at run time (publish bounds it), but a frame chain deeper than `FormatBounds.Depth + 1` fails the call (a corrupt document).

- [x] **Step 1: Write the failing tests.**
  - `ACallInALoop_RunsTheFunctionPerItem`: functions fixture → for each of `hpi`, `assessment_plan`: `standard_draft`, `patient_draft`, `apply_instructions` keyed through the call; `node.standards.value` holds the two `apply_instructions` texts.
  - `Parameters_AreBoundFromArguments` (theory: a field path, a whole `item`, `null`, `run.date`, a snippet item): what `param.<id>.value` and `.content` read in the function.
  - `AFunction_SeesOnlyItsOwnNodes`: a function node `plan` and a top-level `plan`: the function's reader gets the function's.
  - `ANestedCall_KeysThroughBothCalls` (`node.hpi_section.refine_call.judge`), row `depth` 1.
  - `CallsInsideFunctions_CountTowardTheCeilingAndTheCallLimit` (ceiling 2 over 2 items × 3 agents: peak 2; `JobAgentCalls = 5` fails with S09-015).
  - `ACallWhoseIfIsFalse_IsSkipped_AndRunsNothing`.
  - `ACallRing_InACorruptDocument_FailsAndDoesNotHang`.
- [x] **Step 2: Run; fail.**  - [x] **Step 3: Implement.**  - [x] **Step 4: Run all V31; pass.**
- [x] **Step 5: Commit and push.** `PackageFormat (#1503): a call runs its function's body inline, under the same ceiling`

### Task 9: Whole runs, pinned

**Spec:** part 09 § 3.2 (the fixed order); Review Focus 3.

**Files:** Create `tests/V31Run/RunGoldenTests.cs`, `tests/V31Run/RunTrace.cs`, `tests/V31Run/Golden/{nested-note,conditional,functions}.run.txt`, `tests/V31Run/PlanRunnerNestingTests.cs`.

**Interfaces — Produces:** `RunTrace.Of(FakeRunHost, ListRecorder, RunResult)`: one text: the dispatch sequence, then every row (`key state reason valueHash`) in the order recorded, every decision, the result. Replies come from a deterministic stub seeded by the key (`WorkflowStubOutputs`' idea: a value built from the entry's format and `CanonicalJson.HashText(key)`), so no golden holds invented clinical text beyond the fixtures'. `GOLDEN_UPDATE_V31=1` rewrites.

- [x] **Step 1: Write the tests.**
  - `RunGoldenTests.AGoldenRun_IsWhatTheRunnerGives` (theory over the three compiled goldens, auto-release).
  - `ARun_IsTheSame_WhateverOrderRepliesArriveIn`: each golden run with 5 shuffled release orders gives the same rows, decisions and dispatch sequence as auto-release when the ceiling is 1, and the same rows and result (dispatch order of *ready* calls still by the fixed order at every dispatch point) at ceiling 16.
  - `TheCeiling_HoldsUnderNesting_AndDispatchIsDeterministic` (Review Focus 3's package: three nested parallel loops of 10 with `maxParallel` 3, 2, `null`, a call in the innermost, ceiling 4, shuffled releases, two runs with one seed): peak ≤ 4, every loop instance within its cap, both runs' dispatch sequences equal, 500-call limit raised for the test.
- [x] **Step 2: Generate the goldens; read each; run again without the flag.**
- [x] **Step 3: Run all V31; pass.**
- [x] **Step 4: Commit and push.** `Tests (#1503): three whole runs pinned, and the ceiling held under nesting`

### Task 10: Which versions the engine admits, and its limits

**Spec:** part 09 §§ 1.3, 2.1, 3.2, 3.4; R2; D8.

**Files:** Create `src/Consultologist.Api/Workflow/SpecVersionAdmission.cs`, `src/Consultologist.Api/V31/EngineLimitSettings.cs`; modify `Workflow/EngineAttestation.cs`, `Program.cs`; tests `tests/V31Engine/{SpecVersionAdmissionTests,EngineLimitSettingsTests}.cs`, `tests/EngineAttestationTests.cs`.

**Interfaces — Produces:**
- `public sealed class SpecVersionAdmission` — `public static readonly IReadOnlyList<int> Implemented = [31]` (implemented, not yet released); `public SpecVersionAdmission(IConfiguration configuration)` reads `WorkflowPackages:PreviewSpecVersions` once and **throws `InvalidOperationException`** naming `WorkflowPackages__PreviewSpecVersions` and the offending value when an entry is not an integer, is not in `Implemented`, or is already in the compiled-in lists; `public IReadOnlyList<int> Preview { get; }` (sorted, distinct); `public bool Admits(int specVersion)` (the compiled-in supported list, or `Preview`); `public bool Runs31 => Admits(31)`; `public string NotSupported(int specVersion)` (S09-001's sentence, `<list>` as `WorkflowPackageValidator.DescribeAcceptedSpecVersions()` with the preview versions added in order). Registered as a singleton and **resolved in `Program.cs` before the host runs**, so a bad value stops the host at startup.
- `EngineAttestationResponse` gains `IReadOnlyList<int> PreviewSpecVersions` (never null; `[]` when none), beside the two lists, which do not change.
- `public sealed class EngineLimitSettings(IConfiguration configuration)` — `public RunLimits Limits { get; }` and `public TimeSpan RegexLimit { get; }`, from `ConsultGeneration:MaxAgentCallsPerJob` (16), `:CapacityWaitMinutes` (10), `:ServiceAttempts` (5), `:JobMinutes` (30), `:DeliveryMinutes` (15), `:DeliveryAttempts` (3), `:LoopItems` (200), `:JobAgentCalls` (500), `:RenderSteps` (100000), `:RegexMilliseconds` (2000); unset or empty is the default; a value that is not an integer of at least 1 throws `InvalidOperationException` naming the setting (resolved at startup too).

- [x] **Step 1: Write the failing tests.** `Unset_AdmitsOnlyTheCompiledInList` (`Admits(30)`, not `Admits(31)`, `Preview` empty; also for `""` and `" "`); `Listed31_IsAdmittedBesideTheList`; `AnUnimplementedOrMalformedValue_StopsTheHost` (theory: `32`, `31,32`, `x`, `31;`, `30`, `-1`) with the message naming the setting and the value; `NotSupported_IsS09001` (through `V31Contract.AssertSentence`); `TheCompiledInSets_AreUnchanged` (31 in neither static list); attestation: `PreviewSpecVersions` is `[31]` with the setting and `[]` without, and `AcceptedSpecVersions` / `SupportedSpecVersions` are the static lists either way (`TheResponse_CarriesExactlyTheDeploymentFacts` gains the member); `EngineLimitSettingsTests.Defaults` (the ten values), `AnOverride_IsRead`, `AValueBelowOne_OrNotANumber_Throws`.
- [x] **Step 2: Run; fail.**  - [x] **Step 3: Implement.**  - [x] **Step 4: Run the whole non-E2E suite; pass.**
- [x] **Step 5: Commit and push.** `Api (#1503): the preview setting admits 31 beside the compiled-in list; the engine's limits are settings`

### Task 11: Loading a 31 package

**Spec:** part 09 §§ 1.1, 1.2 (steps 1 to 5), § 9; part 07 §§ 3, 4.4, 5; 05-D10, 05-D43; D1, D2, D4, D5.

**Files:** Create `src/Consultologist.Api/V31/{EngineBreakPoints,EngineCapabilitiesFactory,CompiledPackageStore}.cs`; modify `Program.cs`; tests `tests/V31Engine/{FakeRegistry,CompiledPackageStoreTests,EngineCapabilitiesFactoryTests}.cs`.

**Interfaces — Produces:**
```csharp
public interface IRegistryFiles { Task<byte[]?> TryReadAsync(string packageName, string path, CancellationToken ct); }   // null when absent; the blob-backed one uses WorkflowPackageBlobContainerFactory.GetContainersFor
public sealed record CompiledLoad(RunPlan? Plan, string? Refusal, string? RefusalId, string? OperatorDetail);
public interface ICompiledPackageStore
{
    /// null: the version has no 31 stamp, so it is today's path's. Otherwise the plan, or the refusal a start failure records.
    Task<CompiledLoad?> TryLoadAsync(WorkflowPackageRef packageRef, CancellationToken ct);
}
public static class EngineBreakPoints { public static readonly IReadOnlyList<BreakPoint> All = []; }
public sealed class EngineCapabilitiesFactory { public EngineCapabilities Current(); }
```
`TryLoadAsync`, in order: the version is resolved as today for `latest` (reuse the store's pointer read; do not duplicate its cache); read `{name}/{version}/publish.json`; `PublishedStamp.TryRead` null → return null **without reading anything else**; read `resolve.json` and `lock.json` (either absent → A06-004); `CompiledPackage.Verify(name, version, resolve, lock, stamp.ResolveHash, stamp.LockHash)` → on S07-006 the refusal is S09-009's sentence, on S07-017 S09-014's, the S07 sentence in `OperatorDetail` (D5); D4's checks; `RequiresCheck.Engine(resolve, lock, capabilities)` → its first sentence is the refusal, all of them in `OperatorDetail`; `RunPlan.Build` with the settings' regex limit. A plan is cached in memory per `name@version` and `resolveHash`, never stored; a refusal is not cached. `EngineCapabilitiesFactory.Current()`: `Evaluator` `JsonLogicOperations.EvaluatorVersion`; `Scriban` and `Libraries` from the loaded assemblies as `Toolchain.Current` reads them; `Operations` from `JsonLogicOperations`; `Kinds` and `Features` what this build runs (`constants.json`'s lists: a test holds the factory to them, less any feature this area does not run, each named with its reason); `Agents` from the attested agents catalogue the engine already loads (name → versions); `Builtins` the built-in machinery versions as `LockCompiler` writes them; `BreakPoints` `EngineBreakPoints.All`.

- [x] **Step 1: Write the failing tests** (`FakeRegistry : IRegistryFiles` over a dictionary, counting reads; a published golden put with `Put(GoldenCase)`):
  - `AStamped31Version_Loads` (plan's hashes are the stamp's); `ASecondLoad_ReadsNothing` (cache); `AVersionWithNoStamp_IsNotThisPaths` (null, exactly one read, `publish.json`); `A30Stamp_IsNotThisPaths`.
  - `Verify_AcceptsOnlyTheBytesPublished` (theory: `resolve.json` re-indented, one byte changed, a trailing newline → S09-009; `lock.json` changed, a lock naming another hash → S09-014; each `OperatorDetail` passes `V31Contract.AssertSentence` as S07-006 / S07-017).
  - `AStampWithoutItsDocuments_IsNotPublished` (A06-004); `ADocumentForAnotherPackage_IsRefused` (D4); `RequiresSpecOtherThan31_IsS09001`.
  - `APackageNeedingWhatTheEngineLacks_IsRefused` (theory: Scriban `^99.0.0` → S07-003; a kind `teleport` → S07-004; an agent version the catalogue lacks → S07-005; a break point after the locked Scriban with the engine past it → S07-003), each a stamped pair re-published with the mutated `requires` so `Verify` passes.
  - `ARefusal_IsNotCached` (fix the registry, load again, it loads).
  - `EngineCapabilitiesFactoryTests.TheGoldens_AreAdmitted` (`RequiresCheck.Engine` on each golden pair gives nothing, with the agents the goldens bind present); `Kinds_AndFeatures_AreTheContracts`.
- [x] **Step 2: Run; fail.**  - [x] **Step 3: Implement; register in `Program.cs`.**  - [x] **Step 4: Run all V31; pass.**
- [x] **Step 5: Commit and push.** `Api (#1503): a 31 package is loaded from its stamped resolve.json and lock.json, verified and checked`

### Task 12: The orchestration at 31

**Spec:** part 09 §§ 3.1, 3.2, 3.4; part 04 § 7.7 ("inline"); D7, D12.

**Files:** Create `src/Consultologist.Api/V31/{IBodyComposer,RunAgentNodeV31Activity,RenderNodeV31Activity,DurableRunHost,EntityRunRecorder,ConsultGenerationOrchestratorV31}.cs`; modify `Agents/AgentSectionGenerator.cs` (one added method; `SendPromptAsync` unchanged), `Jobs/ConsultGenerationState.cs` (activity names; additive entity operations), `Program.cs`; tests `tests/V31Engine/{OrchestratorV31Tests,RunAgentNodeV31ActivityTests,AgentAttemptTests}.cs`, reusing `tests/Engine/FakeOrchestrationContext.cs`.

**Interfaces — Produces:**
- `public sealed record ConsultGenerationOrchestrationInputV31(string JobId, string AppUserId, string PackageName, string PackageVersion, string ResolveHash, string LockHash, JsonObject Limits, int RegexMilliseconds, string InputsBlob, JsonObject RunFactsBase, string? TimeZone, DateTimeOffset? ScheduledAtUtc, bool DrainAware)` — inputs travel by reference (the inputs blob the starter already writes), never inline.
- `[Function(nameof(ConsultGenerationOrchestratorV31))] public async Task RunAsync([OrchestrationTrigger] TaskOrchestrationContext context)`: waits for a schedule as today's does; sets `startedAtUtc = context.CurrentUtcDateTime` and records it with the hashes and limits **before any node runs** (part 09 § 1.2 step 6; one entity call); loads the plan replay-safely — the activity `load-plan-v31` awaits `ICompiledPackageStore.TryLoadAsync` (verifying again, so a job never runs bytes other than the ones its start checked: its hashes must equal the input's, else the job fails with S09-009) and returns the verified `resolve.json` text, gzip then base64; the orchestrator takes the plan from a process-wide cache keyed by `resolveHash`, or on a miss builds it with `RunPlan.Build` from that text, with no I/O of its own (the plan is a deterministic function of `resolve.json`; the one copy in the history counts toward index § 5's 10 MB gate, said in the engine guide); runs `new PlanRunner(plan, limits, new DurableRunHost(context, input), new EntityRunRecorder(context, input)).RunAsync(...)`; then finalises the job on its entity: `Completed`, or `Failed` with the sentence (`TimedOut` is a failed job with S09-012).
- Activity names in `ConsultGenerationActivityNames`: `RunAgentNodeV31 = "run-agent-node-v31"`, `RenderNodeV31 = "render-node-v31"`, `LoadPlanV31 = "load-plan-v31"`. No Durable retry policy on any (D7).
- `DurableRunHost : IRunHost` — `UtcNow` is `context.CurrentUtcDateTime`; `CallAgentAsync` / `RenderAsync` call the activities with the call and the package ref + `resolveHash`; `DelayAsync` is `context.CreateTimer`.
- `public interface IBodyComposer { ComposedBody Compose(RunPlan plan, string entryKey, string? function, JsonObject scope, int stepLimit); }` with `ComposedBody(string? Text, bool TooManySteps, string? Failure)`; the default `ScribanBodyComposer` renders the entry's `body` with Scriban over `scope` (D12), counting steps against `stepLimit` with Scriban's loop limit; area 07 replaces it.
- `RunAgentNodeV31Activity`: loads the plan (store cache), composes the body, finds the agent by the entry's format and producer in the plan's `bindings`, and makes **one** attempt through `AgentSectionGenerator.TryPromptOnceAsync(...)`, a new method returning `AgentAttempt(AgentOutcomeKind Kind, string? Text, ConsultTokenUsage? Tokens, int? RetryAfterMilliseconds, string? ErrorKind)`: HTTP 429 → `Throttled` with `retry-after-ms` or `retry-after` when present; 5xx → `ServiceFailure` `error`; a time-out → `timeout`; no connection → `unreachable`; anything else propagates as today. The activity fills `WaitMilliseconds`: the service's value, else `AgentBackoff.Wait(attempt, jitterSeed)` — 5 s doubling, capped at 60 s, plus up to 20% jitter from `Random.Shared` (an activity may; the result is in the history). Telemetry: key, attempt, lengths and hashes, never text.
- `EntityRunRecorder : IRunRecorder` (D12): `RowAsync` updates progress on the job's entity (an additive operation `RecordV31Progress(key, state)`: counts and the current key only) and is the seam area 09 replaces; `DecisionAsync` likewise counts.

- [x] **Step 1: Write the failing tests.**
  - `OrchestratorV31Tests.AJob_RunsToCompleted`: nested-note's golden in a `FakeRegistry`, `FakeOrchestrationContext` answering `run-agent-node-v31` from a stub by key and `render-node-v31` through the real composer: the entity ends `Completed`; the first entity call after the schedule wait carries `resolveHash`, `lockHash`, the nine limits and `startedAtUtc`, and precedes every activity call; the agent activity calls appear in `RunGoldenTests`' dispatch order.
  - `AFailedNode_FailsTheJob_WithItsSentence`; `AThrottledCall_WaitsOnADurableTimer` (the context records a timer of the returned wait between two calls of one key).
  - `NeverMoreThanTheCeiling_InFlight` (ceiling 2 in the input's limits; the fake context's pending activities never exceed 2).
  - `RunAgentNodeV31ActivityTests`: the prompt sent is the composed body; the agent is the binding's; a 429 with `retry-after-ms: 1500` → `Throttled`, 1500; a 429 with none → a wait between 5,000 and 6,000 on attempt 1 and between 10,000 and 12,000 on attempt 2, never over 72,000; a 503 → `ServiceFailure`, `error`; `TaskCanceledException` from the time-out → `timeout`; `HttpRequestException` with no status → `unreachable`.
  - `ScribanBodyComposer_RendersTheScope`, `…_StopsAtItsStepLimit`.
  - `The30Orchestrator_IsUntouched`: `git diff origin/m39-build -- src/Consultologist.Api/Jobs/ConsultGenerationEngine.cs` is empty at this task's commit (asserted by the reviewer, not a unit test), and every `EngineRun*` and capture golden passes unchanged.
- [x] **Step 2: Run; fail.**  - [x] **Step 3: Implement.**  - [x] **Step 4: Run the whole non-E2E suite; pass.**
- [x] **Step 5: Commit and push.** `Api (#1503): a 31 job's orchestration: the plan run inline, calls as activities, waits as timers`

### Task 13: Starting a 31 job

**Spec:** part 09 § 1.2 (step 6 and its last paragraph), §§ 5.3, 7.2, § 9; part 02 (inputs: required, blank, formats; find the job-start sentences for a missing or invalid input in `sentences.json`); part 06 § 10.4; R1, R4, R5; D6.

**Files:** Create `src/Consultologist.Api/V31/{V31StartChecks,V31JobStarter}.cs`; modify `Jobs/ConsultGenerationJobStarter.cs` (one branch), `Jobs/ConsultGenerationState.cs` (additive nullable header members), `Auth/AccountModels.cs` (setting keys); tests `tests/V31Engine/{V31StartChecksTests,V31JobStartTests}.cs`.

**Interfaces — Produces:**
- `AccountSettingKeys.TimeZone = "account.timeZone"`, `.ProfileCredentials = "profile.credentials"`, `.ProfileOrganisation = "profile.organisation"`, `.ProfileClosing = "profile.closing"`.
- `public static class V31StartChecks` (pure; area 12's Create page calls the same):
  `public static IReadOnlyList<SourceRefusal> Account(RunPlan plan, IReadOnlyDictionary<string, string?> profile, string? timeZone)` — `RequiresCheck.Profile` (S06-031, S06-032; a blank value is not set), then S06-008 when `timeZone` is blank or not an IANA zone `TimeZoneInfo` finds;
  `public static (RunInputs? Inputs, IReadOnlyList<SourceRefusal> Refusals) Inputs(RunPlan plan, IReadOnlyDictionary<string, JsonNode?> supplied, JsonObject runFacts, IReadOnlyDictionary<string, string?> profile)` — each input in `InputOrder`: required and absent or `null` → the missing-input sentence; required, of a text format or `string`, and blank → the same sentence (R5); an optional one absent → `null`; a value that does not fit its format (`PlanFormats.Validate`, numeric enums included, R4) → the invalid-input sentence; an input the package does not declare → refused as today's start does. An empty list is accepted (R1). Each input's read form is `{ value, content, title }` (part 02: `title` the label of a `oneOf` entry).
  `public static JsonObject RunFacts(RunPlan plan, DateTimeOffset startedAtUtc, string timeZone)` — each fact in `plan.RunFacts` (part 03 § 4.3), `run.date` the date of `startedAtUtc` in the zone, ISO.
- `V31JobStarter.StartAsync(...)`: called from `ConsultGenerationJobStarter.StartAsync` after the hold, the rate limit and the package ref, **only when `SpecVersionAdmission.Runs31`**, with `ICompiledPackageStore.TryLoadAsync`: null → today's path continues unchanged; a refusal → a start failure recorded on the job (today's `RecordStartFailure` shape, the sentence the clinician's) and the outcome's error; else: the request's typed inputs and extracted documents become `supplied` (reuse the starter's extraction for text inputs: a document sent to a text input arrives as its text, as today), `Account` and `Inputs` are checked (any refusal → a recorded start failure with every sentence), the inputs blob is written, the entity is initialised with `PackageSpecVersion` 31 and the new header members, and `ConsultGenerationOrchestratorV31` is scheduled with `InstanceId = jobId`. `run.*` facts are worked out in the orchestrator from `startedAtUtc` (D6) with `V31StartChecks.RunFacts`.
- `ConsultGenerationJobInitialize` and the entity's state gain nullable `ResolveHash`, `LockHash`, `Limits` (`JsonObject?`), `StartedAtUtc`; absent below 31, so every stored 30 payload reads as before.

- [ ] **Step 1: Write the failing tests.**
  - `V31StartChecksTests`: `AMissingRequiredInput_IsRefused`; `ABlankRequiredText_IsRefused` (theory: `""`, `"  "`, `"\n\t"`; and `"x"` accepted); `ABlankOptionalText_ReadsNull`; `AnEmptyRequiredList_IsAccepted` (R1); `ANumericEnumInput_IsAccepted_AndAnUnlistedNumberRefused` (R4); `AValueOfTheWrongShape_IsRefused`; `NoTimeZone_IsS06008` (theory: null, `""`, `Mars/Phobos`; `America/Toronto` accepted); `AMissingSignature_IsS06031`, `AMissingProfileField_IsS06032` (only when `needs.profile` lists it); `RunDate_IsTheStartInTheAccountsZone` (`2026-11-17T03:30Z` in `America/Toronto` → `2026-11-16`); no refusal sentence contains a supplied value.
  - `V31JobStartTests` (the real starter with NSubstitute stores, a `FakeRegistry`, as `tests/Engine/EngineRun.cs` wires it): `WithoutThePreviewSetting_A31PackageIsAnsweredAsToday` (`SpecVersionNotYetExecutable`; the compiled store is never asked; no `publish.json` read); `WithIt_AJobIsScheduledOnTheV31Orchestrator` (the input carries both hashes and the limits; the entity's `Initialize` carries them too); `ALoadRefusal_IsARecordedStartFailure` (S09-009's sentence on the job; no orchestration scheduled); `AnAccountWithNoTimeZone_IsRefusedAtStart` (S06-008, recorded); `A30Package_WithThePreviewSettingOn_StartsExactlyAsBefore` (the same `Initialize` and orchestration input as with it off, compared as JSON); `ExamplePriorLetters_WithNoLetters_StartsAt31` (R1: a 31 package with an empty fanned input is scheduled, where 30 answers `NoApplicableDeliverable`, asserted side by side).
- [ ] **Step 2: Run; fail.**  - [ ] **Step 3: Implement.**  - [ ] **Step 4: Run the whole non-E2E suite; pass** (`DurablePayloadReplayTests` and the starter's tests included).
- [ ] **Step 5: Commit and push.** `Api (#1503): a 31 job starts from its compiled package: inputs, profile and time zone checked, hashes and limits recorded`

### Task 14: The drain

**Spec:** part 09 § 3.6, § 3.4 (queued time does not count), S09-018; D9.

**Files:** Create `src/Consultologist.Api/Jobs/{EngineDrain,AdminDrain}.cs`; modify `Jobs/ConsultGenerationJobStarter.cs`, `V31/V31JobStarter.cs`, `Jobs/ConsultGenerationEngine.cs` and `V31/ConsultGenerationOrchestratorV31.cs` (the `DrainAware` check after the schedule wait), `Jobs/ConsultGenerationState.cs` (`DrainAware` on the 30 orchestration input, default false; a waiting status), `Program.cs`; tests `tests/V31Engine/{EngineDrainTests,AdminDrainTests,DrainDoorsTests}.cs`.

**Interfaces — Produces:**
- `public interface IEngineDrain { Task<bool> IsDrainingAsync(CancellationToken ct); Task BeginAsync(CancellationToken ct); Task<IReadOnlyList<ParkedJob>> EndAsync(CancellationToken ct); Task ParkAsync(ParkedJob job, CancellationToken ct); }` with `ParkedJob(string JobId, string OrchestratorName, string InputJson, DateTimeOffset ParkedAtUtc)`. State lives in the engine's own storage (a `drain/state.json` blob and one `drain/parked/<jobId>.json` blob per job, in the container the job outputs use), read on every call, never cached; `EndAsync` clears the flag and returns the parked jobs oldest first, deleting each blob only after its orchestration is scheduled (scheduling with `InstanceId = jobId` is idempotent, so a repeat schedules nothing twice).
- `POST Admin/Drain` (`[Function("AdminDrain")]`), `POST Admin/Resume` (`[Function("AdminResume")]`), `GET Admin/Drain` (`[Function("AdminDrainStatus")]`): operator-authorised as `Operator/*`, `FunctionCors` applied. Drain and the status answer `{ "draining": bool, "producing": n, "parked": n }`, `producing` the count of this engine's orchestrations that are `Running` and not waiting for an update or asleep on a schedule (by their custom status); Resume answers `{ "draining": false, "started": n }`.
- The starters: while draining, everything up to and including the entity's initialisation happens as usual, the job's status is *"Waiting for an update to finish"* (S09-018), the orchestration input is parked instead of scheduled, and the outcome carries the job id with no error (the HTTP door answers 202 as it does for any start).
- The orchestrators: an input with `DrainAware` true, after its schedule wait and before anything else, calls the activity `check-drain`; while draining it publishes the waiting status and awaits the external event `engine-resumed`, which Resume raises for every orchestration it finds waiting. `startedAtUtc` is taken after this wait, so queued time does not count. An input without `DrainAware` (an orchestration started by an earlier build) takes no new step.

- [ ] **Step 1: Write the failing tests.**
  - `EngineDrainTests` (an in-memory blob fake): `Begin_ThenEnd_RoundTrips`; `Park_KeepsJobsInOrder`; `End_Twice_ReturnsNothingTheSecondTime`.
  - `AdminDrainTests`: `ANonOperator_IsRefused` (403, as `Operator/*`); `Drain_ReportsWhatIsProducing`; `Resume_SchedulesEveryParkedJob_OldestFirst`.
  - `DrainDoorsTests.EveryDoor_WaitsAndStartsOnce` (Review Focus 5): during a drain, a 30 start, a 31 start and an email start each return a job id, schedule nothing, and leave the job *"Waiting for an update to finish"*; Resume schedules each exactly once; a second Resume schedules none; with no drain, every door schedules as before (the 30 orchestration input equals today's but for `DrainAware: true`).
  - `AScheduledJob_WhoseTimerEndsDuringADrain_WaitsForResume` (both orchestrators, on `FakeOrchestrationContext`: no activity but `check-drain` is called until `engine-resumed` is raised); `AnOrchestrationFromBeforeThisBuild_TakesNoNewStep` (an input without `DrainAware`: the call sequence equals `EngineRunGoldenTests`' today, golden unchanged).
  - `HoldNewJobs_StillRefusesAsToday`.
- [ ] **Step 2: Run; fail.**  - [ ] **Step 3: Implement.** The `EngineRun*` goldens must not change; if `DrainAware: true` adds a `check-drain` line to them, the harness starts with a drain that is off and the trace filter leaves that activity out, said in the test's comment.
- [ ] **Step 4: Run the whole non-E2E suite; pass.**
- [ ] **Step 5: Commit and push.** `Api (#1503): Admin/Drain and Admin/Resume: jobs wait behind an update and start once after it`

### Task 15: The guides, and the whole area checked

**Files:** Modify `docs/customizable-workflow/package-format-library.md` (a section "specVersion 31: the run (preview)": `PublishedStamp`, `RunPlan`, `PlanRunner`, the seams and what areas 07 to 12 plug into each, the keys of D13, the time limit); create `docs/customizable-workflow/engine-v31.md` (the two load paths, the load step by step, every setting with its default, the limits, retries, the drain's three endpoints and what the release workflow must call, D10's sizing of `maxConcurrentActivityFunctions` and the instance count), linked from `docs/README.md`; this plan (tick-offs, decisions amended as taken).

- [ ] **Step 1: Write both documents** from the code as built; every signature quoted is copied from the source.
- [ ] **Step 2: Spec coverage.** Fill the table at the end of this plan with the commit that built each row; a row with no commit is a gap to close or a block to record.
- [ ] **Step 3: Run the runbook's step 6** (build, the non-E2E suite, `.github/scripts/run-e2e.sh`); all green.
- [ ] **Step 4: Commit and push.** `Docs (#1503): the run at 31, and the engine's settings, limits and drain`

## Spec coverage

| Section | Task |
|---|---|
| part 09 § 1.1 two load paths; no source tree read at 31 | 11 (D1), 13 |
| § 1.2 step 1 the pin | 13 (the starter's, unchanged) |
| § 1.2 steps 2, 3 read and verify; the stamp | 3, 11 |
| § 1.2 step 4 `requires`, break points | 11 (D2, D4) |
| § 1.2 step 5 the plan, in memory, cached | 3, 11 |
| § 1.2 step 6 profile, time zone, hashes recorded before any node | 12, 13 |
| § 1.3 which versions are admitted; S09-001 | 10, 13 |
| § 2.1 the preview setting; startup stop; the attestation | 10 (publish, import and `Current` ask `SpecVersionAdmission` in area 11) |
| § 3.1 order from `resolve.json`; computed, conditions inline | 3, 4 |
| § 3.1 a call's body inline | 8, 12 |
| § 3.1 `matches` and the clock | 1 (R2 replaces "no time-out"), 4 (decision times from the host's clock) |
| § 3.1 check loops per item | 6 |
| § 3.2 the ceiling, what counts, `maxParallel`, the fixed order | 4, 6, 8, 9, 10 |
| § 3.2 the record holds the ceiling | 12, 13 (the header's `limits`); peak in telemetry: 12 |
| § 3.3 deployment-wide limits | 15 (D10) |
| § 3.4 429s, other failures, the job limit, cancelled rows | 5, 12 |
| § 3.4 total-work limits | 4 (calls, render steps), 6 (items) |
| § 3.4 settings recorded in `limits`; retries on the row | 10, 5 |
| § 3.6 the drain | 14 (D9) |
| § 9 the sentences for the above | with each task |
| part 04 § 2 `for` | 6 |
| § 3 `while` | 7; R3 in 2 |
| § 4 reading across levels | 4 (`RunScope`), 6, 7 |
| § 5 `if`, `case` | 7 |
| § 7 functions and `call` | 8 |
| § 8 concurrency | 4, 6, 8, 9 |
| #1503 rulings: empty list; regex limit | 6 and 13 (R1); 1 (R2) |
| #1507 rulings 4, 5, 6 | 2 (R3, R4), 13 (R4, R5) |
| 04-D14 `plainConverts`; 04-D49 computed values validated | 2 (D3), 4 |
