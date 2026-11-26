# The engine at specVersion 31 (preview)

*Area 06 of the Milestone 39 build (#1503). Built from
[the engine and the pages](package-format-v31/09-engine-and-pages.md) §§ 1, 2.1, 3.1 to 3.4 and 3.6, and
[the plan](package-format-v31/plans/area-06-engine-loading-and-scheduling.md) for the decisions. The library the
engine runs (`RunPlan`, `PlanRunner`, the seams) is described in
[the package format as a library](package-format-library.md), "specVersion 31: the run". Area 07 (#1504) added
the template language the activities run, the 31 agents catalogue and `MacroChoices` at a 31 start; the library's
side is "specVersion 31: templates" in the same guide. Area 08 (#1505) added what a job does after its run: the
deliverables' outcomes, delivery, the operator's alerts and the engine-fault log; the library's side is
"specVersion 31: checks, deliverables and delivery" in the same guide. Area 10 (#1507) migrates 30 packages to 31 in the
library ("specVersion 31: migration" in the same guide); by the owner's rulings it also changed what the engine does at
a 31 start (`MacroChoices` keyed by a macro's id), in delivery (`pdf` falls back to the link-only email) and in the
weekly summary (the hand-edits line). Area 11 (#1508) publishes a 31 package through `WorkflowPackages/Publish`, serves
it through `Current` and `Current/Content`, and imports it ("Publish, `Current`, `Content` and import" below); the
library's side is "specVersion 31: publish".*

This page is for whoever deploys and operates the engine. It covers:

- how a 31 package is published, imported, described to the satellites, loaded and started;
- the settings and their defaults;
- the limits and retries;
- delivery, the `delivering` status, the operator's alerts and the engine-fault log;
- the drain that comes before every deploy, and what the release workflow must call;
- how to size the deployment-wide limits;
- what must hold before 31 leaves preview.

**31 runs only where `WorkflowPackages__PreviewSpecVersions` lists it.** Without that setting nothing on this
page changes what the engine does. No 31 code path reads storage, every door answers a 31 package as it does
today, and the compiled-in lists (`WorkflowPackageValidator.AcceptedSpecVersions`,
`WorkflowPackageStore.SupportedSpecVersions`) stay 5–30. The drain is the exception: it applies to every job,
at every version, from this build on.

## Two load paths

| Spec version | What the engine reads |
|---|---|
| 5–30 | today's path, unchanged: `manifest.json` and the source files (`WorkflowPackageStore`) |
| 31 | `publish.json`, `resolve.json` and `lock.json` from the version's folder; never the source tree, `manifest.json` included |

**The stamp chooses the path (D1).** A 31 version's stamp is `publish.json` in its folder:
`{ "specVersion": 31, "resolveHash": "sha256:…", "lockHash": "sha256:…" }`. The engine reads the stamp first. A
stamp with `specVersion` 31 takes the 31 path. Anything else takes today's path untouched: no stamp, a 5–30
stamp, or a stamp that cannot be read. So while 31 is previewed, every 5–30 start costs one extra read of
`publish.json`.

## The load, step by step

`ICompiledPackageStore.TryLoadAsync(WorkflowPackageRef packageRef, CancellationToken ct)` (`CompiledPackageStore`)
returns `null` when the version is not a 31 one. Otherwise it returns a
`CompiledLoad(RunPlan? Plan, string? Refusal, string? RefusalId, string? OperatorDetail, bool Unreadable = false)`.
It runs these steps in order:

1. **Nothing is read** unless `SpecVersionAdmission.Runs31`.
2. **The pin.** `latest` is resolved through the 5–30 store's own pointer read and cache
   (`IWorkflowPackageVersions`); any other version is used as written.
3. **The cache.** A plan already built for `name@version` is returned and nothing is read. A stamped folder never
   changes, so the cache is keyed by `name@version`, and each plan carries its `resolveHash`.
4. **The stamp.** `{name}/{version}/publish.json` is read. If it is not a 31 stamp, the load returns `null` after
   that one read.
5. **The documents.** `resolve.json` and `lock.json` are read. If either is absent, the refusal is **A06-004**:
   *"Workflow <package>@<version> isn't published."*
6. **Integrity.** `CompiledPackage.Verify` holds both documents to the stamp's hashes and to each other. A
   mismatch is said twice (D5): the job shows part 09's sentence, and the operator's log keeps part 07's.

   | Part 07 (the operator) | Part 09 (the job) |
   |---|---|
   | S07-006 | **S09-009**: *"The compiled workflow <package>@<version> doesn't match its published hash, so it can't be run. Tell the operator."* |
   | S07-017 | **S09-014**: *"The compiled workflow <package>@<version> doesn't match its lock, so it can't be run. Tell the operator."* |

7. **Declarations (D4).** A document whose `requires.spec` or `specVersion` declares another version is S09-001.
   One that does not declare its spec version as publish writes it, or whose `package` does not name the folder it
   was read from, is S07-006, which the job shows as S09-009.
8. **`requires` and the break points.** `RequiresCheck.Engine` holds the pair to `EngineCapabilities`. The first
   sentence is the refusal (S07-003, S07-004 or S07-005), and all of them go in `OperatorDetail`.
9. **The plan.** `RunPlan.Build` runs with the regex limit. A document it refuses (S07-006) is shown as S09-009.

A refusal is not cached: once the registry is fixed, the next load loads. Two first loads at once may both build,
and both get the first plan stored.

**A registry that cannot be read.** The load never throws for what it reads. A missing or invalid `latest`
pointer, or a storage failure other than an absent blob, gives a load with `Unreadable` true and no refusal:
nothing is known of the version, so nothing is refused. `OperatorDetail` then holds the package, the version and
the failure's type and status, never its message, which can carry a URI.

**What the engine runs** (`EngineCapabilitiesFactory.Current()`):

- **Kinds:** all eight.
- **Features:** all five: `parallel`, `check-loops`, and, since area 07 (#1504), `parts`, `macro-markers` and
  `run-spots`. `EngineCapabilitiesFactory.FeaturesNotRun` is kept, and is empty.
- **Agents:** the ones the engine loads and attests at startup: the output contracts, the formats catalogue and its
  custom producer, and the **31 agents catalogue** (`V31AgentsCatalogue`, below). So a package that binds an agent
  only that catalogue lists (for example `judgement` 1) loads; one binding an agent version no catalogue holds is
  still refused with S07-005.
- **Break points:** `EngineBreakPoints.All`, compiled in (D2) and empty today. One is added only by an upgrade
  that went through the corpus re-render, which is a code change.

**The 31 agents catalogue** (#1504, ruling 3; D9). `V31AgentsCatalogue.Load()` reads `agents/v31/bindings.json` and
`agents/v31/agents.json` from the bundled agents directory (the pinned `external/consultologist-agents/agents/`,
copied into the build as `agents/v31/`), through `AgentBindings.Read`, the reader publish uses. It attests each agent
version against git: the bundled manifest `agents/<name>.yaml` must exist and declare that version. It gives:

- `Version` (`v2026.12.2`), and `Agents`, each `(name, version)`;
- `StrictRejects(name, version)`: the keywords the agent's provider's strict mode rejects, which are left out of a
  format sent to it (empty for an agent it does not hold);
- `Holds(name, version)`.

`ApiServices.ResolveAtStartup` resolves it, with `SpecVersionAdmission` and `EngineLimitSettings`, before the host
runs, but only when `WorkflowPackages__PreviewSpecVersions` admits 31: a 5–30 host never touches `agents/v31` (#1505,
R15). Nothing else reads it there either: the 31 store (`CompiledPackageStore`) takes the engine's capabilities, and
the 31 agent activity the catalogue, as a `Lazy<…>`, so only a 31 load or a 31 agent call reads it
(`CompositionTests` builds every registration and function class with a catalogue that throws). Every defect in the
two files, a version that is not an integer or a missing member included, is the catalogue's own
`InvalidOperationException` naming the file and the member. So a missing file, a file not of the published shape, or
a manifest at another version stops the host (`InvalidOperationException`) rather than failing job starts. It is
attested against git only, not against Foundry: `judgement` 1 exists in Foundry only once the contracts are released,
and `AgentAttestationService` does not compare the 31 agents.

## Starting a job

`ConsultGenerationJobStarter.StartAsync` calls the 31 branch (`V31JobStarter`) after the hold, the rate limit and
the package ref, and before today's `ResolveAsync`. A `null` load continues on today's path unchanged. So does
an `Unreadable` one, after a warning with the operator's detail: today's path reads the same registry and answers
the failure exactly as it does with the preview off, for a 5–30 package too. The branch does this:

1. **A load refusal** becomes a start failure on the job (`RecordStartFailure`, with the clinician's sentence;
   the operator's detail goes to the log). The outcome is `PackageContentRejected`, with the job's id.
2. **The inputs.** The request's typed `Inputs` (`ConsultDraft` back-fills `consult_draft`, as today), its references
   (below, "Inputs sent by reference") and its documents are read. The documents go through the starter's own extraction, so a document sent to a text input
   arrives as its text, behind the same OCR and speech-to-text gates as today.
   #1508: a document OCR read records the model's dated version in the input's `ocr.model`, `prebuilt-read/2024-11-30`
   (the result's `ApiVersion`: the pinned SDK's `AnalyzeResult` has no `ModelVersion`), and `prebuilt-read` alone when the
   service gave none. Only a 31 start reads it (`ExtractionSteps`); a 5–30 origin is written as before.
3. **The account.** The profile fields that `needs.profile` lists are read. `name` is the display name,
   `signature` is the block this job signs with, and the others are the account settings `profile.credentials`,
   `profile.organisation` and `profile.closing`. The time zone is `account.timeZone`, an IANA name (D6). The
   request's `SignatureBlockId` is read at 31 only, and only when `needs.profile` lists `signature` (D15, #1505).
   Left out or blank, the job signs with the account's chosen block. Naming a block of the account, it signs with
   that one. Naming none of the account's blocks, the start is refused with **A08-004** *"The signature chosen
   for this job isn't in your profile. Choose one in Profile."*, in S06-031's place. The job's header records the
   block's id as `signatureBlockId`, and `signatureChosen` as `"job"` when the request named a block other than
   the chosen one, else `"default"`. Both are `null` when the package reads no signature. A 5–30 start ignores
   the member.
4. **The checks.** `V31StartChecks.Account` gives S06-031 and S06-032 for the profile, then S06-008 for a zone
   that is blank or not an IANA zone the machine knows. `V31StartChecks.Inputs` gives the input refusals (since #1507
   it calls the library's `RunInputsBuilder.Build`, which the offline run of a migrated package starts with too):
   - **A06-011** *"Input '<id>' is required; fill it in to run this workflow."* for a required input that is
     absent, `null` or blank, whatever its format (R5);
   - **A06-012** *"Input '<id>' was given a value that isn't <format>."* (numeric enums included, R4);
   - **A06-013** *"Unknown input(s) <ids> (declared: <ids>)."*

   An empty list is accepted (R1). Every refusal goes into one start failure, the sentences joined by line ends,
   with outcome `InputsMismatch`. The sender is told every sentence except A06-013, which quotes the caller's own
   ids.

   After these, a start is refused as a 30 start is, with outcome `NoApplicableDeliverable` and today's wording, when
   no deliverable can exist (R8, #1505). That is when the package has deliverables and every one has an `if`, each
   `if` reads only `input.*` and the run facts the start knows (`run.id`, `run.engine` and `run.package`), and each
   is false or `null` on the start's inputs. An `if` that reads anything else, or that fails, is left to the run.
   `V31StartChecks.NoDeliverableCanExist` decides it (through `RunInputsBuilder.NoDeliverableCanExist`, since #1507),
   a rule that holds `matches` inline, since no replay exists at a start. The detail names each deliverable by label with its printed `if` (S03-001), and nothing it read.
5. **The held inputs.** The inputs document
   (`{ "inputs": { <id>: { value, content, title } }, "profile": { <field>: text | null } }`, with `"modelDerived": [<id>, …]`
   when an input holds an earlier run's text, below) is written where the
   job's held inputs live: `org-job-inputs` or `personal-job-inputs`, `{appUserId}/{jobId}.json`. The retention
   drop and account closure therefore delete it as they delete a 5–30 job's.
6. **The record.** The job's entity is initialised at `PackageSpecVersion` 31 and stamped `SchemaVersion` 12 (record
   storage 12), with `ResolveHash`, `LockHash` and `Limits`. The rows come later, from the orchestration (below, "The
   record").
7. **The schedule.** Unless the engine is draining, `ConsultGenerationOrchestratorV31` is scheduled with
   `InstanceId` = the job id.

A 31 start failure's History line is "Not started".

**Macro choices.** A per-run choice is an optional boolean input that the macro's `if` reads (part 05 § 8.2), so a
31 start reads each `MacroChoices` entry `<id>: <bool>`, keyed by a macro's id as at 30, as the value of the one boolean
input that macro's `if` reads, exactly as if the request had sent that input (#1507, ruled by the owner 2026-11-22; part
10 § 1.3). The input may have another id: a migrated `letrozole` whose id an input already had reads
`letrozole_macro`, which the entry sets, leaving the input `letrozole` alone. An id that names no macro of the package is
refused, *"MacroChoices names '<id>', which is not a macro of the package."* (A10-043), which quotes the caller's id, so
the email sender is not told it, as with A06-013. A macro whose `if` reads no boolean input, or more than one, is
refused, *"MacroChoices names macro '<id>', whose if does not read exactly one boolean input; send the input in Inputs
instead."* (A10-044). An input sent both ways with different values is
refused, *"Input '<id>' is given one value in Inputs and another in MacroChoices; send it once."* (A07-016); the same
value sent both ways is accepted, and an input sent as `null` counts as not sent. `V31StartChecks.Choices` does this,
through the library's `RunInputsBuilder.Choices`, which the byte-equivalence comparison reads a 30 scenario's choices
with; its refusals come after `Account`'s and before `Inputs`'. Each is one of the start's refusals, recorded with the others.

**Inputs sent by reference** (#1509, ruled by the owner, comment 2, ruling 1; D7). A 31 start reads `InputRefs`,
`InputFormRefs` and `InputFormFiles` with today's resolvers, in today's order and before the documents (step 2), so an
input sent only by reference is filled, never missing (A06-011). What the resolvers read of a 5–30 declaration they read of
the plan (`ConsultGenerationJobStarter.DeclaredInput31`): each input's id, whether its format is an array of a text format,
and whether its channel is `form`.

- **`InputRefs`**, an earlier run's deliverable: the run must be this account's, completed, still holding its text, and
  have the deliverable. Its text fills the input: a text input takes it as its text, an array of text as an element. A
  reference standing alone in an array of text is a list of one. Beside typed rows or documents in an array of text, each
  element takes its request `Position` among them, as at 30 (#1144); without positions the order is typed rows, then
  documents, then earlier runs.
- **`InputFormRefs`**, a held form response, and **`InputFormFiles`**, a questionnaire file: they fill only an input whose
  channel is `form`, which is an object (part 02 § 8.3). The response's answer, or the file's object, is the input's value,
  held to its format by `RunInputsBuilder` as a typed value is (A06-012), since a 31 format has no 5–30 declaration to coerce
  by. A value the request sends beside the reference must equal it; one left out is filled from it. A form reference on any
  other input is refused in today's words for an answer a form cannot fill (*"The response's answer for input '<id>' cannot
  be filled from a form answer."*), and a questionnaire file in today's words for an input that takes none.

A refusal is today's, outcome and words: no job is recorded, and the sender is told nothing more than a 5–30 start tells
it. The header records an element from an earlier run with origin `previous-run`, `jobId` (the run it came from),
`resultId` (the deliverable) and `modelDerived: true` (part 08 § 3.1). A form-filled input is recorded as `typed`, part 08
§ 3.1 having no form origin. The held inputs document then has a `modelDerived` member, the ids of the inputs holding such
an element in ordinal order (absent when none does). The orchestration reads it into the run's
`RunInputs.ModelDerivedInputs`, so a decision that reads such an input, directly or through a loop's `item` or a call's
`param`, is `modelDerived: true` (part 08 § 5.1). A node row's flags stay those `resolve.json` writes (part 08 § 4). A 5–30
start resolves its references exactly as before.

A start writes the lineage links a 5–30 start writes (#546), once the inputs are held: one row on the source job for each
element taken from an earlier run, and one for a rerun, which History's "Used by" list on the source job reads. A start
with no such input and no rerun writes none. A write that fails is logged and never refuses the start.

### Rerun (#1509, D8)

`Rerun` of a storage-12 job rebuilds the request from the job's held inputs document, where a 5–30 job's comes from its
`Supplied` half: each `inputs.<id>.value` is sent as a typed value, under the record's own package ref, and the profile
half is never read into a request (the account's profile is read afresh, as at every start). A source whose inputs were
dropped, or never held, is refused in the same words as a 5–30 job's; a pointer whose document is gone is an error, as it is
there. When the source recorded `signatureChosen: "job"`, the request names the recorded `signatureBlockId`; a default pick
is made afresh. The new job is a 31 job, and its response names the source as `RerunOf`. Its `RerunVerdict` and
`RerunDivergence` are `null`: storage 11's comparison of node hashes is not made at 31, and History shows the link between
the two jobs only. The job response lists a 31 job's held inputs, while they are held, as a 5–30 response lists its own:
each input's text, a structured value as its JSON. A rerun sends the values the source held as typed values, but the record never marks model output as not model-derived
(part 08 § 5.1): for each input, and each element of an array input, whose origin on the source was `previous-run`, the
rerun's record holds that origin again with the original `jobId` and `resultId` and `modelDerived: true`, the run's
`ModelDerivedInputs` holds the input's id, and the lineage links to the original source runs are written beside the rerun's
own. The rerun door reads the origins from the source's record and passes them to the start itself; no request member carries
them, so a caller cannot mark or unmark an input. Typed inputs and documents stay as they were.

### Asking before a start (#1509, D1)

`GET WorkflowPackages/Current/StartChecks?ref=<package ref>&signatureBlockId=<id>`
(`[Function("WorkflowPackageStartChecks")]`, `V31StartChecksRoute`) answers the account's refusals before a start, for the
Create page to say them where the person is looking. It is authorised, CORS'd, and resolves `ref` (the pin when absent;
an account package only for its owner) and loads the package as `Current` does. The answer is
`WorkflowPackageStartChecksResponse(Refusals, NeedsProfile)`: `Refusals` are the sentences of a start's account step, in the
start's order (A08-004 in S06-031's place when `signatureBlockId` names a block the account lacks, then S06-031, S06-032,
S06-008), and `NeedsProfile` is the plan's `needs.profile`. It reads the account with the starter's own code
(`V31JobStarter.ProfileAsync`, `TimeZoneAsync` and `AccountRefusals`), so a start gives the same sentences. Input
refusals are not in it (the page's own checks cover them and the start repeats them), and no profile value is. A load
refusal is the same 400 `{ error }` as `Current`'s, S09-001 when 31 is not admitted; a version below 31 answers two empty lists.

## The orchestration

`ConsultGenerationOrchestratorV31` takes a `ConsultGenerationOrchestrationInputV31(JobId, AppUserId, PackageName,
PackageVersion, ResolveHash, LockHash, Limits, RegexMilliseconds, InputsBlob, RunFactsBase, TimeZone,
ScheduledAtUtc, DrainAware, AccountKind)`. The input never carries inputs inline: `InputsBlob` names the job's
held inputs. The limits and the regex limit are the ones the start read, so a replay never reads a setting that
has changed since. The orchestration runs these steps in order:

1. It waits for the job's schedule, as the 5–30 orchestration does.
2. **The drain** (`DrainAware`): it sets its custom status to `Queued`, then calls `check-drain`; while the
   engine drains, it waits here (below).
3. It takes `startedAtUtc` from the replay-safe clock. The job limit is timed from here, so time spent queued or
   waiting behind a drain does not count.
4. It records the header on the job's entity in one call, before any node runs: `RecordV31Header` with the two
   hashes, the ten limits and `startedAtUtc`. Then it calls `MarkRunning`.
5. **`load-plan-v31`** loads and verifies the version again through the store. The plan's `resolveHash` must be
   the job's, else S09-009; its `lockHash` must be too, else S09-014. A version no longer stamped 31 is A06-004.
   When the store refuses the version here, or in any other activity that loads the plan (`load-run-inputs-v31`,
   `run-agent-node-v31`, `render-node-v31`), the job shows the refusal's sentence and the activity logs a warning
   with the package, the version, the refusal's id and the operator's detail (D5), which is package text and
   hashes. A registry that cannot be read fails the activity instead, and the job with A06-010.
   The activity returns the verified `resolve.json`, gzipped then base64, and is called even when the plan is
   cached, so every worker's history is the same. The orchestrator takes the plan from a 64-entry process cache
   keyed by `resolveHash` and the regex limit. On a miss it re-hashes the text against the job's `resolveHash`
   and builds the plan. It does no I/O of its own.
6. **`load-run-inputs-v31`** reads the held inputs. It also works out the run facts there, so the orchestration
   never reads the time-zone database: `run.date` is `startedAtUtc`'s date in the account's zone, and
   `run.package` is `name@version`. `run.id` and `run.engine` come from the start. If the held inputs are gone,
   the job fails with **A06-009**: *"This job's inputs are no longer held, so it can't run. Run the workflow
   again."*
7. It runs `PlanRunner` with:
   - `DurableRunHost`: an agent call is `run-agent-node-v31`, a render is `render-node-v31`, a rule that holds
     `matches` or `has_word` is `evaluate-rule-v31` (below), and a wait is a durable timer;
   - `StoredRunRecorder` (#1506): a round's rows, decisions and delivery rows are buffered and, at each flush, written
     in one `write-record-rows-v31` call (each row's settled text to its `settled` blob, then the rows to
     `ConsultGenerationRecordRows`, insert-if-absent; a row that differs from the stored one fails the job with
     A06-010), then counted on the entity in one `RecordV31Rows` with the round's token sums (`RecordV31Progress`
     stays for histories in flight). `FinalizeJob` writes a 31 job's usage row from those sums. Each row goes to
     telemetry as its key, state and hashes.
8. **Delivery** (#1505), when the run completed and some released deliverable has a destination: the job enters
   `delivering`, and the released deliverables are sent (below).
9. It finalises the job: `Completed`, or `Failed` with the run's sentence. A timed-out run is a failed job with
   S09-012. A failed job delivers nothing: each destination of each released deliverable is recorded *not sent: the
   job failed* (S06-038), and the operator is alerted (below).
10. **It seals the record** (#1506): after `FinalizeJob`, and before the alert, it calls `seal-record-v31`, which
    assembles the canonical record document and stamps `recordHash` (below, "The record"). The call is best effort:
    a fault is logged with its type and never changes the job's state or error, and a job whose seal failed is
    sealed by the first read, or the sweep.

A fault the run cannot say as a node's sentence fails the job with **A06-010**: *"The engine couldn't finish this
job. Tell the operator."* The exception's message is never used, since it can carry a service's response; the log
gets the fault's type only.

**The history.** `load-plan-v31`'s result holds one copy of `resolve.json`, gzipped. That copy counts toward the
10 MB gate on the orchestration history (index § 5; part 09 § 3.1), as do the rest of what the history holds:

- `load-run-inputs-v31`'s result, the inputs document, clinical text included;
- each agent activity's input, the call's scope;
- each agent activity's result, the reply;
- each `write-record-rows-v31` call's input, which holds the round's rows and their settled text (#1506, D17).

**A reply travels the history twice** (#1506, D17): once as the agent activity's result, and once as the settled text
the row write carries (a deliverable's composed text, a node's content, a `check_result`'s lists). The prompt as sent
and a renderer's input and output do not: the activity that made them writes them to the text blobs itself and strips
them from what it answers. This is flagged for the go/no-go gate's history measurement (index § 5), which should be
taken on a completed job of the largest example package with the record writes in it.

The dev dry run measures the history's size and replay time on the largest example package.

**Activities.** `run-agent-node-v31`, `render-node-v31`, `load-plan-v31`, `load-run-inputs-v31`, `check-drain`,
and, since area 08 (#1505), `evaluate-rule-v31`, `deliver-v31` and `alert-operator-v31`. None of the first four has
a Durable retry policy (D7), because the runner retries; nor have the three area 08 added: a send's failure comes
back as its outcome and the delivery runner retries it, a rule's failure is part of its answer, and an alert that
cannot be sent is logged and dropped. `check-drain` has 3 attempts, from 5 seconds, so one storage hiccup does not
fail a job.

**`evaluate-rule-v31`** (`EvaluateRuleV31Activity`, R6). A rule that holds `matches` or `has_word` is evaluated in
this activity, not inline, so a replay reads the stored outcome and never decides it again (part 09 § 3.1; the
regex time limit made a replay's decision depend on the clock). The call is the entry's key, the rule's member
(`if`, `expression`, …) and the scope the runner would have evaluated it over; the answer is the value, or the
failure's kind, limit and detail. It evaluates under the job's recorded regex limit (`RegexMilliseconds` is in
the activity's input), never the setting as it is now. A plan that is not the job's, or that lacks the entry or
its rule, answers `plan` and the runner says S07-006. A rule that holds neither is still decided inline. The
log carries the key, the member and the failure kind, never the scope.

## Agent calls and retries

`run-agent-node-v31` (`RunAgentNodeV31Activity`) makes one attempt of one call. It loads the plan through the
store's cache, held to the job's `resolveHash`, and finds the agent in the plan's `bindings`
(`bindings.<format key>.<producer>`), which the 31 agents catalogue must hold. The prompt is
`AgentTurn.Prepare(plan, call, renderSteps, catalogue.StrictRejects(agent, version))`: the entry's body composed
over the call's scope with the runner's placement decisions (`AgentCall.Placed`), within the job's recorded
`renderSteps`; for a `via: "custom"` binding, ended with the format sent; for a repair, the retry text (below). It
sends that text once, as the user message, through `IAgentAttempts`, which declares:

```csharp
Task<AgentAttempt> TryPromptOnceAsync(string stage, string userMessage, string agentName, string agentVersion, CancellationToken cancellationToken);
```

| What happened | The outcome |
|---|---|
| a reply | `Completed`, with `AgentTurn.Finish`'s value: unwrapped from the envelope, its macro markers checked and replaced, and the row's detail; with marker faults, the reply as it came and the faults (`Invalid`) |
| an empty reply (`SendPromptAsync`'s *"No assistant response found"*) | `Completed` with `""`, so the node's `onInvalid` and `failIfEmpty` decide it; its token counts are lost |
| HTTP 429 | `Throttled`, waiting `retry-after-ms`, else `retry-after` (seconds or an HTTP date) |
| a 5xx | `ServiceFailure` `error` |
| a time-out | `ServiceFailure` `timeout` |
| no connection | `ServiceFailure` `unreachable` |
| any other status (401, 403, 404, …) | propagates, and the job fails with A06-010 |
| a call that cannot be made | `Failed`, and nothing is sent: `plan` (the plan is not the job's, or lacks the entry or its binding, or the 31 agents catalogue lacks the agent), `steps` (the body passed `renderSteps`), `compose` (the body could not be composed) |

When the service gives no wait, the wait is `AgentBackoff.Wait(attempt, jitter)`: 5 seconds, doubling, capped at
60 seconds, plus up to 20% jitter (5,000 to 6,000 ms after the first attempt, never over 72,000). The wait is in
the activity's result, so it is in the history. The runner then waits on a durable timer, holding the call's
slot, and calls again. A node fails after these limits:

- `CapacityWaitMinutes` of 429 waits in all: S09-010;
- `ServiceAttempts` attempts: S09-011, which names the last error's kind, never its content.

**What a retry sends.** The runner asks again in two ways, and each attempt is prepared afresh from the plan:

- **After a 429 or a service failure** the call is made again as it was: the same scope, the same decisions and, once
  the node has been repaired, the same repair. So the text sent is the text the failed attempt sent.
- **After a reply that could not be used**, under `onInvalid: "retry"`, the node is asked once more (a repair, in the
  slot it holds). Its prompt is the composed prompt (with the format, for a custom binding), trimmed at the end, then
  two newlines and the retry text (D11). For wrong macro markers it is `Your last reply could not be used:`, one
  line `- <sentence>` per fault (S05-053 to S05-059, which name macro ids only), and `Write the reply again, with
  each marker exactly as given above.`. For a reply that did not fit its format it is `Your last reply could not be
  used: it did not fit the required format. Write the reply again.`. The retry never quotes the reply, and S05-056 is
  fixed text: it does not name the id the model wrote.

**One attempt per call holds only while `AzureAI__MaxRetries` is 0** (its default). `SendPromptAsync` builds its
own client retry policy from that setting on every call. A non-zero value makes the SDK retry a 429 or a 5xx
inside one attempt: it resends the same text, but the retry sits under the orchestration's own, outside the record's
retry rows, and its waits are not in the history. **Leave it at 0 on any app that runs 31 jobs.**

Telemetry from the activity carries the key, the attempt, lengths, hashes and token counts, never text: the
prompt's, the run-neutral text's, the model's reply's and the finished value's (they differ once markers are
replaced), and the counts of marker faults. A node's tokens are on its row (`tokens`, summed over its attempts), and a round's sums are counted on the entity by `RecordV31Rows`.

`render-node-v31` (`RenderNodeV31Activity`) returns `RenderTurn.Render(plan, call)`: the text, the run-neutral text
and the row's detail, with the runner's decisions (`RenderCall.Placed`). Its log line carries the key and the lengths
and hashes of the two texts. #1506: both activities write the text only they hold (`AgentOutcome.Produced`,
`RenderOutcome.Produced`, with `spots`, the texts of the run spots whose row `output` is null) to the row key's
`produced` blob and strip it from what they answer, so the prompt as sent never enters the history.
`StoredRunRecorder` passes only a row's `inputHash` and `runNeutralHash` to telemetry; the whole detail is stored.

## Delivery

*Part 06 § 8 and part 09 § 3.8, with #1505's decisions D10 to D13.* A job whose run completed delivers its
**released** deliverables to the destinations their folders list in `delivery`. A job that released nothing, with a
deliverable blocked or held that a mail kind (`email`, `pdf`) delivers, still sends the link-only email, as 30's reply
did (#1507 D19, ruled by the owner, below). A job with nothing to send (every deliverable skipped, or delivered nowhere,
or blocked or held with no mail kind) never enters delivery. A job that failed delivers nothing,
and records each destination of each released deliverable *not sent: the job failed* (S06-038).

**The `delivering` status.** `ConsultGenerationJobStatuses.Delivering` (`"delivering"`) is set from `running` only,
by the entity's `MarkDelivering`, and the orchestration's custom status says so too. It is not terminal: the job is
then `Completed`, however delivery went. A destination that failed is a row, not the job's failure. A 5–30 job never
has the status. These read it:

- **Cancel** refuses it, as it refuses `running`: *"This consult has already started, so it can no longer be
  cancelled."* (`RefusalForCancel`.) Another status that is not `Scheduled` is refused *"This consult is already
  <status>."*
- **The drain** counts the orchestration as producing: delivery runs inside it (the drain section).
- **The job index** carries the status with the entity's, as it does `running`. History's pages are area 12's.

**The runner.** `DeliveryRunner` (the library's side is in the library guide) runs inside the orchestration. It makes
the first attempt of every send at once, then waits for one to finish. Each destination gets
`ConsultGeneration__DeliveryAttempts` attempts (default 3), the wait before the second 2 minutes, doubling after.
No attempt starts after `ConsultGeneration__DeliveryMinutes` (default 15) from the start of delivering; a
destination unsent then (never started, or waiting to retry) is `failed`. A send in flight at the close is waited for,
not failed (#1508): its activity is not cancelled, and `deliver-v31` bounds each attempt at 2 minutes (a linked token the channel honours, whose expiry is a failed send of kind `timeout`; a channel that ignores its token is not cut off), so the record says what happened to it.
Delivery runs after the run's own timer is cancelled, so it is outside
`jobMinutes`. Both limits are the job's recorded ones (the header's `limits`), never the settings as they are now.
The waits are durable timers, all cancelled when delivery ends, so none is left outstanding.

**`deliver-v31`** (`DeliverV31Activity`) makes one attempt of one send. It has no Durable retry policy: the runner
retries inside its window. A send's failure comes back as an outcome (`DeliverySendOutcome`), and the activity never
throws for one; a channel's fault is a failed send `error`. The activity's input carries the documents' composed
text across the boundary and is never logged; the log has the job, the kind, the folder, the number of documents,
the attempt and the result.

**The channels, and what sets each up (#1505 D10).** There is no new setting. A kind is set up from what the account
already has:

| Kind | Set up when | What it sends |
|---|---|---|
| `email` | the mailbox is configured (`EmailIntake__MailboxAddress` and `EmailIntake__AppBaseUrl`), the job's start had a delivery address, and `delivery.emailPdf` was not `false` at the start | today's link-only message (the 5–30 reply's `EmailIntakeReply.Compose`: boilerplate and the History link), nothing attached. A bundle's one message names each document by label |
| `pdf` | `email` is set up | the same message with the deliverable as a password-protected PDF (`ConsultDocumentPdf`). A bundle's is one `.zip` holding each PDF. With no `delivery.documentPassword`, or attachments past the 5–30 reply's budget (2 MiB), the 5–30 reply's link-only message for that case instead, nothing attached: `sent`, its attempt `sent link-only` (#1507 R8, D17) |
| `emr` | **never, in this build** | nothing: no write-back exists |
| `zip` | **never, in this build** | nothing: no protected archive exists |

**`emr` and `zip` have no channel yet.** No account can set them up, so each destination is recorded *not sent: EMR
isn't set up for this account* or *…Zip…* (S08-006), final at once and never retried. The mapping's home in the
account is still to design (#1505 D10, flagged for the owner; #1408 item 9). A folder asking `["email", "emr"]` for
an account with an address gets one row `sent` and one `not sent`, and nothing is retried for the second. A folder
asking `["email", "pdf"]` for an account with an address and no document password gets two rows `sent`: the `email`
one as asked, the `pdf` one the link-only email, its attempt `sent link-only` and its reason `no document password`
(#1507 R8).

**The settings are read at the start (#1507 R8, D18).** As a 5–30 job does (#518: *"a choice changed mid-run does not
change what was promised"*), the delivery address and `delivery.emailPdf` are read once, when the job starts: the
starter carries its origin's `ReplyToAddress` (the account's verified delivery address, or the email door's sender) and
`EmailRequested` in the orchestration's input (`ConsultGenerationOrchestrationInputV31.Delivery`, a
`V31DeliverySettings`), and every attempt, retries included, uses them (`DeliverV31Input.Delivery`). The document
password is read at the attempt, inside the activity, as the 5–30 reply reads it at reply time, so no secret enters the
orchestration's history. An orchestration started before this build has no `Delivery` (the member is optional), and
its channels read the address and `delivery.emailPdf` at the attempt, as area 08's build did.

**Every deliverable blocked (#1507 D19, ruled by the owner on #1507, 2026-11-22T21:38Z).** A job that completes having
released nothing, with a deliverable blocked or held that a mail kind (`email`, `pdf`) delivers, enters `delivering`
and sends 30's link-only message once for the job (`DeliveryPlan.Sends`, `DeliverySend.EveryDeliverableBlocked`; by
the `email` channel when one of them lists `email`, else by `pdf`): the completed reply
with *"No document could be sent: <labels> <was | were> not released."* (A10-057) after its opening, the History link,
nothing attached and no password needed. Its send carries each document's label and no text. Each `email` or `pdf`
destination each of them lists gets a row from that one send (a `failed` or `not sent` one naming its own kind):
`sent`, its attempt `sent link-only`, its reason `every deliverable blocked`; the deliverable's own `delivery` stays
`[]`, and the canonical record gathers the row under it. Only an orchestration this build started sends it: one
started before (no `Delivery` in its input) replays the calls it made then, and never entered `delivering` for such a
job (`DeliveryRunner.RunAsync(outcomes, blockedEmail)`).

**Bundles (#1505 D11).** A folder with `bundle: "zip"` makes one send per kind for all its released deliverables, in
plan order, and each deliverable still gets its own delivery row with the send's result. A folder with nothing
released sends nothing.

**The rows (#1505 D13).** A delivery row's key is `delivery:<deliverable key>/<kind>`. Its `reason` is S08-006's
sentence for a kind not set up, `not sent: the job failed` for a failed job, `failed: <kind>` after the attempts or
the window, why a `sent` row went as the link-only email (#1507 D17, D19: `no document password`, `attachments over
the budget`, `every deliverable blocked`, A10-054 to A10-056), and `null` for any other `sent`. A row records each
attempt with its start time and `sent`, `sent link-only` or `failed: <error kind>`, where the error kind is
`unreachable`, `error` or `window` (`too-large` is no longer given: a set over the budget is sent link-only). `StoredRunRecorder`
counts the rows on the entity as `delivery:sent`, `delivery:not sent` and `delivery:failed`, apart from the run's row
states, so a failed delivery is never counted as a failed run row, and stores them with the record's rows.

**What the email carries.** Boilerplate, the labels of the documents (package text) and the History link, as today.
No document's text and no value from an input, a model or a profile is in a body, a log line or a row.

## The record (area 09, #1506)

*Part 08 is the design and `provenance-record.md` § 6 the contract; this section is how the engine writes it. Everything
here is 31 only: a 5–30 job, and a 31 job below storage 12, touches none of it.*

**The recorder.** `StoredRunRecorder` (it replaces `EntityRunRecorder`) holds every row, decision and delivery row the
runner settles in a buffer. `FlushAsync` is awaited by the runner once per round that recorded anything and at its end,
and by the delivery runner after each delivery row. A flush is one `write-record-rows-v31` activity for the round,
then one `RecordV31Rows` entity operation counting what was stored, so an event never names a row that is not there.
A row is written once, insert-if-absent. A Durable replay writes the same rows in another batch split and finds each
already stored: that is a no-op. A row that differs by a byte from the stored one is refused, logged as
`V31 record conflict. JobId, Key` (hashes only), leaves the stored row alone and fails the job with A06-010; the job's
record is then sealed with the first row. A row larger than a table row holds (1 MiB, counted as Azure does, in UTF-16)
is not written: the store answers `RecordWrite.TooLarge`, the round's other rows are written and counted,
`write-record-rows-v31` returns the keys (`TooLarge`, a trailing optional member of its result), and the recorder
throws `RecordRowTooLargeException`; the orchestration fails the job with **A09-002**, *"The job's record of '<row key>'
is larger than this engine stores. Tell the operator."*, and logs `V31 record row too large. JobId, Key, Chars` (no row
content). There is no spill: the table is the only permanent store.

**An engine fault keeps its last round.** When a run throws (an activity's fault, a conflict, a row too large), the
runner first records each gate check's row it still holds, `effect: null`, and the exception leaves it unchanged (a
recording that throws then never replaces it). The orchestration's catch flushes the recorder, best-effort (its failure
is logged with the exception's type only), before it finalizes the job, so the sealed record holds every row recorded.
A flush clears its buffer before it writes, so a flush that failed or met a conflict writes nothing twice.

**A terminal job is frozen.** From the moment a storage-12 job is `Completed`, `Failed` or `Cancelled`, no entity
operation writes a member its record header reads: `Cancel`, `FinalizeJob`, `RecordV31Header`, `Initialize`,
`RecordStartFailure`, `MarkNodeFailed` return at once, and `RecordV31Rows` counts rows but adds no tokens. The stamp
(`RecordV31Seal`) is a signal sent after `record.json` is written, so it cannot be the guard; the state is. So a
document restored from the rows (below) always hashes to the stamp. A 5–30 job is not frozen: it re-finalizes as before.

**The row table.** `ConsultGenerationRecordRows`, created on a 31 job's first write. Partition key the job id, row key
the row's key escaped for Azure Tables (`RecordKeys.Escape`: the characters a row key refuses, and `!`, `%` and `~`,
become `!` and two hex digits; an escaped key past 512 characters is `~` and the SHA-256 hex of the true key, so two
keys never share one), and the properties `key` (the true key), `section` (`node`, `check`, `deliverable`, `delivery`
or `decision`), `row` (the row's canonical JSON, continued in `row1`, `row2`… past 30,000 characters) and `hash`. The
account is `ConsultGenerationRecordStorage`'s, else `AccountStorage`'s (see Settings).

**The text blobs.** Retained text is never in a row. In the account kind's outputs container, under the job:
`{appUserId}/{jobId}/text/{sha256 hex of the key}.produced.json` is written by the activity that made the text (the
prompt as sent, a renderer's input and output, the texts of run spots and substitutions), and
`{appUserId}/{jobId}/text/{…}.settled.json` is written by `write-record-rows-v31` just before its rows (a node's
content and value, an item name, a `check_result`'s lists, a deliverable's composed text, a blocked deliverable's
included). The rows hold hashes, so the 7-day drop deletes blobs and never rewrites a row.

**Sealing.** `RecordSealer.EnsureSealedAsync` assembles `{appUserId}/{jobId}/record.json` from the header
(`ConsultGenerationJobState.ToRecordHeader()`) and the rows (`RecordDocument.Assemble`), writes it if absent, and has
the entity stamp `recordHash` once (`RecordV31Seal`, write-once). It does nothing for a job that does not exist, is
below storage 12 or is not terminal (`delivering` is not terminal), and sealing twice gives the same hash. It is
reached four ways:

- the orchestration's end, `seal-record-v31` (above);
- the cancel route, after it signals `Cancel`. Only a `Scheduled` job can be cancelled, and the signal is processed
  after the route reads, so in practice this seals nothing and the next two do;
- the retention sweep, before it drops the text: a job it cannot seal keeps its text, is not counted as dropped, and
  is tried again at the next pass (`RecordNotSealedException`, logged with the job id and the exception's type);
- the first read of a terminal job that has no `recordHash`: `GET ConsultGenerationJobs/{jobId}/Record`.

**Restoring.** `record.json` sits in the container `job-records` of the records account, to which no lifecycle rule
applies (R5, #1509; before that it sat in the text account, whose 30-day policy deleted it, and a record written there is
read as missing and restored). The document is a
materialisation of the rows under the header (decided in the build, #1506), so `RecordSealer.ReadSealedAsync`, which
the `Record` route reads through, seals the job if need be, reads the blob and, when a stamped job's blob is gone,
assembles the document again and writes it back only if it hashes to the stamped `recordHash`. It logs
`V31 record restored. JobId, Bytes, RecordHash`; a document that does not hash to the stamp is an engine fault, logged
`V31 record could not be restored. JobId, Expected, Got` (hashes only), with nothing written and the route answering
500. The rows and the entity's stamp are what must outlive the policy.

**The size of a record.** Sealing logs one line, `V31 record sealed. JobId, Rows, Bytes, RecordHash`: the rows
assembled and the document's UTF-8 byte count. It is the number for the go/no-go gate's record-size estimate (index § 5);
the dev dry run's largest-package job gives the figure to compare with the gate. The line holds ids, counts and a hash
only. A seal that fails logs `V31 record could not be sealed. JobId, ExceptionType`.

**The routes** (all `GET` and `OPTIONS`, `Anonymous` with the account checked as the Links route checks it; 404 for a
job that is not the caller's or is below storage 12):

- `ConsultGenerationJobs/{jobId}/Rows?key=<row key>`: `{ key, section, row, text, textDeleted }`. `text` holds the
  settled and produced text that still hashes to what the row says it should (a row's `content` against its
  `outputHash` or `composedHash`, and so on), and is `null` once the job's text is dropped (`textDeleted` true). With no
  `key` it lists `{ rows: [ { key, section, row } … ] }` in key order, with no text.
- `ConsultGenerationJobs/{jobId}/Record`: `record.json`'s exact bytes, with `Content-Disposition: attachment` and
  `X-Record-Hash`; 409 while the job has not finished, 500 *"The record isn't available yet. Try again."* when it
  cannot be sealed or read.
- **The event `row-finished`** (D16): one per stored row except decisions, key `row:<key>`, payload exactly
  `{ "key", "section", "status" }`, materialised from the row table and placed before `done` (see
  `docs/CONSULT_GENERATION_EVENTS.md`). A browser fetches the row from `Rows?key=`. An SSE connection lists the row
  table only when the job's row count (`rows` on the response) has moved since its last listing, and always for the
  terminal response. A poll (`GET` of the job) holds no state, so it lists the table on every poll once the job has a
  stored row (none before); each listing appends the row events, which the event store keeps once by key. That remaining
  cost, one table listing and one event-store call per poll of a running 31 job, is bounded by the poll rate.
- A rows route whose store faults, or meets a stored row that is not JSON, answers 500 *"The record isn't available yet.
  Try again."* with CORS, and logs `V31 rows not available. JobId, ExceptionType`.

**Retention and closure.** The 7-day sweep calls `IRecordTextDrop` first: it seals the job if need be, deletes the
job's `text/` blobs, and only then signals `DropText`. The sweep never deletes `record.json` or a row, and `recordHash` still holds afterwards. Account closure deletes the rows of each storage-12 job (`DeleteJobAsync`, after the events); a failure stops the
closure, which a rerun resumes. The text blobs go with the account's prefix in the text account, and `record.json` with
the account's `{appUserId}/` prefix of `job-records` in the records account (`IRecordBlobStore.DeleteAccountRecordsAsync`,
called only for an account with a storage-12 job; a failure stops the closure). Both know a storage-12 job from its index row
(`ConsultGenerationJobIndex`'s `RecordStorage`, written only for a job at storage 12 or later), so for a 5–30 job they
make the calls they made before 31: no entity read and no record store. A 5–30 row is written through the entity type
it always was, so its stored properties are unchanged. A 31 job indexed before this column existed has none and is
treated as 5–30; such jobs exist only on dev preview, so nothing is migrated.

**Deploying: drain 31 orchestrations first.** A 31 history now holds `write-record-rows-v31` and `seal-record-v31`
calls, and the entity gains the operations `RecordV31Rows` and `RecordV31Seal`. An orchestration in flight across this
build would fail its replay. **Run the drain (above) before deploying this build.** Only dev admits 31; a 5–30
orchestration is unchanged. `seal-record-v31` is the first activity to bind `[DurableClient]`; the seal is best effort,
so a binding failure shows only as the log line, and should be looked for on the first dev run.

**Deploying: the alert is no longer awaited where it is raised** (#1508, ruling 4, item 5). The `alert-operator-v31`
activity keeps its name, but an orchestration now schedules it and goes on, so a 31 history holds the alert's
scheduling before the next call's, not after its answer. An orchestration in flight across this build, whose history
was written with the alert awaited, would fail its replay on the order of its tasks. The drain before the deploy (above)
covers it: no 31 orchestration is in flight at the deploy. The activity's history event is the same name as before; the
event that is new in a history is the one the order of calls gives, which only a drained deploy avoids.

**Deploying: what a replay reads has changed** (#1508, 2026-11-25, item 8: "D16–D21 ship behind a drain"). Area 10's
D16 to D21 ([#1532](https://github.com/Consultologist/Consultologist-Blazor/pull/1532)) and these four fixes change
what the orchestrator computes from an activity's answer it already holds: an item's name cut at 120 characters is
trimmed before the `…` (item 4); a `markdown_inline` reply loses one trailing line ending before it is validated (item
8); an item's name and text are escaped where a loop joins them into `.content` when the text is not Markdown (plain
text, a nullable text, an object's or a list's JSON; item 8, D23); a text sent through a wrapped `via: "custom"`
binding is read as its plain value, and a reply there that is not the envelope holding a string does not fit (item
13). An orchestration in flight across the
build could take another branch on its replay, so the drain before the deploy (above) covers them too.

## Alerts and the weekly summary

*Part 09 § 3.9, D14.* The operator is told through the `alert-operator-v31` activity, so a replay reads that an alert
was raised and never raises it twice. The activity is scheduled where the alert is raised and **not awaited there**
(#1508): the run goes on, and the orchestration waits for its alerts once, as it ends (`PendingAlerts`), so it never
completes with one outstanding. An alert that failed is logged and never fails the job. Every alert is logged as a warning (`Operator alert. Kind=…`, with the job,
the key and the number). It is also mailed to `Operators__AlertAddress`, from `EmailIntake__MailboxAddress`, when
both are set; with either unset it is only logged. A mail fault is logged, with its type only, and never reaches the
job. A message holds the kind, a fixed sentence for it, and the `Job`, `Key` and `Number` lines it has: ids, keys
and counts only, never a value.

| Kind | Raised when | Once |
|---|---|---|
| `job-failed` | a job fails, a timed-out job and an engine fault included, and a job that fails before its run (the plan, the lock or the inputs) | per job, after the job is final |
| `capacity-wait` | the job's 429 waits (the ones taken, not a 429 that fails its node) pass 5 minutes in all; `Number` is the whole minutes | per job |
| `job-slow` | the job reaches 80% of `jobMinutes`; `Number` is the minutes it has run (a separate host timer, cancelled with the others) | per job |
| `delivery-failed` | a delivery row is `failed`; `Key` is the first such row and `Number` how many | per job, after delivery |
| `publish-failed` | a 31 publish is refused, for any reason, an author's failing test included (#1508); `Key` is the package name and `Number` the number of refusals. Raised by the publish endpoint directly, not by an orchestration | per refused publish |

`exporter-warning` is named and raised by nothing in this build.

**The weekly summary** (`OperatorWeeklySummary`) is a timer function on `%OperatorWeeklySummarySchedule%`, an
NCRONTAB value, for example `0 0 7 * * 1`. **Unset, the function is disabled** and the host is unaffected, as the
text retention timer is. It reads nothing unless the engine runs 31 (`WorkflowPackages__PreviewSpecVersions`
admits it), even with the schedule set. It lists every account's jobs from the job index that ended in the 7 days
before the run, at every version, and mails `Jobs`, `Failures`, the p50 and p90 of the durations (nearest rank) and
two lines areas 09 and 10 fill. `Largest record` (#1508, area 11) is the largest record sealed in the week:
`Largest record: <n> bytes, <rows> rows (job <id>)`, the numbers in the invariant culture with thousands separators. The
size is written where the record is sealed: the `RecordV31Seal` entity operation (signalled by the sealing activity, never
written by an orchestration) also upserts the job's index row with `RecordBytes` and `RecordRows` (trailing, optional,
write-once columns, stored only for a job at record storage 12 or later), so the summary reads it from the rows it
already lists, with no blob read per job. It is still logged at sealing. A week with no sealed 31 job reads
`Largest record: none`. A sealed 31 job whose row holds no size (sealed before sizes were written, or not sealed yet) is
**not measured, never zero**: with none read the line is `Largest record: not measured; the sizes of <k> sealed records
could not be read.`; with some, the largest of those is followed by `Largest record: <k> sealed records could not be read
and are not counted.` A 5–30 job has no size and is not counted. Ties go to the lower job id. With no alert address it is only logged, as counts.

**The migration line** (#1507, R11, D22; `OutstandingHandEdits`) is computed by the summary itself, in its timer
function, never in an orchestration. Each account's active package is the one a consult would run (the pin
resolver, as the pin health report reads it). Each distinct package is read once as stored
(`IWorkflowPackageSources.ReadSourceAsync`: the stored `manifest.json` text and, below 31, the files the load gathers),
and a package below 31 is migrated in memory by `WorkflowPackageMigration30To31.Migrate` against the bundled 31 type
registry (`PublishedTypeRegistry`, read on first use from `package-format/v31/` and `agents/v31/` in the app's
output by the library's one loader, `PackageFormat.V31.PublishedTypeRegistry.Read`, #1508 R20; the ingestor takes the
same singleton), nothing written, each package counted against the engine's configured limits
(`EngineLimitSettings.PublishLimits`: a package past the default 10,000 `JobInstances` but within the configured number carries no
A10-081 hand edit). The line reads `Outstanding migration hand edits: <n> in <p> packages; <q> packages do not
migrate`: `<n>` the hand-edit notices summed, `<p>` the packages with at least one, `<q>` the packages the migration
refused or failed on. A package at 31 is not counted. A pin or a package that cannot be read fails alone (a stale
pin is a normal state): the rest are counted on that line, and A10-059 follows it, `Outstanding migration hand edits:
<k> of the accounts' active packages could not be read and are not counted.`; when none could be read, A10-059
stands alone, never a line of zeros. When the registry cannot be read while a package is below 31, A10-058 stands
alone: `Outstanding migration hand edits: not counted; the type registry could not be read.` The summary still goes
out, and the lines and the log carry counts only, none for what was not read.

## The engine-fault log (A08-006)

A template the engine fails to read in a way no text explains is the engine's fault, not the author's: an exception
other than Scriban's own for unreadable text (`ScriptRuntimeException`, and the stack-depth exception for text too
deep) is **A08-006** at publish, *"<Subject> couldn't be checked: the engine failed reading its template. This is a
fault in the engine, not in the package; report it."*, never A07-005. Its audience is the operator, not the author: it is the log's, and a package author cannot act on it.
At run time the node fails with **A11-008**, *"<Subject> couldn't be rendered because the engine failed. Tell the
operator."*, which a clinician sees, never A06-008 (a body that fails on what it reads) and never S07-006, which would
say the compiled document is not what was published. A call's `ErrorKind` is `engine` for it (`compose` for A06-008).
The log is where the fault is seen: `run-agent-node-v31` (a prompt) and `render-node-v31` (a `rendered` node or a
deliverable) log once per failed composition, at error level, *V31 engine fault reading a template* with
`SentenceId=A08-006`, the key, the entry's key, the function and the exception's full type name. Never the
exception's message, its stack or the text. Publish carries the type on its refusal (`SourceRefusal.EngineFault`), and the publish
endpoint logs it at error level, *31 publish engine fault*, with the sentence id and the type (#1508; "Publish, `Current`,
`Content` and import" below).

## Publish, `Current`, `Content` and import (area 11, #1508)

*Part 07 §§ 5 to 7, part 09 §§ 2, 4 and 8.2. The pipeline itself is the library's: "specVersion 31: publish" in
[the package format as a library](package-format-library.md). Dev's registry, the CI and re-render scripts, and the
conditions before 31 leaves preview are in [registry operations](registry-operations.md), "specVersion 31".*

Every 31 branch below asks `SpecVersionAdmission` before it reads anything. With `WorkflowPackages__PreviewSpecVersions`
unset, a 31 publish, import, `Current`, `Content` and job start each answer **S09-001** as a 400, never a 500, and the
5–30 path is untouched.

### Publish

`POST WorkflowPackages/Publish` (`[Function("WorkflowPackagePublish")]`) takes the 31 branch when the request's
`Files["manifest.json"]` parses as an object whose `specVersion` is the integer 31. A 31 request sends `Manifest` null
and every file, `manifest.json` included, as text in `Files` (written as UTF-8 without a byte-order mark). The branch
(`WorkflowPackagePublisher.PublishV31Async`, then `V31PackagePublisher`) does this:

1. **Admission**, before anything is read: S09-001 unless 31 is admitted.
2. **A migration report** (the trailing request member `MigrationReport`, area 10's `migration-report.json`) is
   accepted only from an account in `Operators__AppUserIds`, as the drain's endpoints are (else 403, *"A migration
   report is accepted only from the operator."*), and only with `oldVersion` and `newVersion` as text and no member written twice in any object (else 400).
3. **`Source`** is optional at 31; when given it gets today's format checks and, unless `FromUpload`, today's access
   check. **The target** is decided as at 5–30 (`Target`, `NewPackageSlug`, or the account's first name), so a 31
   publish always lands in the account's private container under an `acct-…` name.
4. **The version** is the next CalVer, as at 5–30. The name and version are written into `manifest.json` in place
   (every other member, its order and its nulls kept; written indented), so `sourceHash` covers the manifest as stored.
5. **The pipeline** (`PackagePublish.RunAsync`) runs with the engine's own inputs: the bundled 31 type registry
   (`PublishedTypeRegistry`), the toolchain the engine attests (`EngineToolchain.Current()`), the limits
   (`EngineLimitSettings.PublishLimits`: `MaxAgentCallsPerJob`, `LoopItems`, `JobAgentCalls`, `JobInstances` and
   `JobMinutes`), the clock, `RegexMilliseconds` for the tests' `matches`, `EngineLimitSettings.Limits` as the limits the package's
   tests run at (`PublishOptions.RunLimits`), and no diagram. `publishedBy` is
   `operator-migration` with a migration report, else `editor` with a `Source`, else `api`. A migration report whose
   `newVersion` is not the version being tried gets that version (a member replacement, written canonical), so the
   stored `migration-report.json` names the version it is stored in; one that names it already is stored as sent.
6. **Writing.** The account's ownership is recorded once, before the first byte. If the version is already stamped, or
   the write loses to another publish, the pipeline runs again on the next CalVer (never at or below the one that was
   taken), three times in all; then the answer is **A11-006**. After the stamp the latest pointer is set and the
   account's pin flipped, as at 5–30.

**The answer.** Published: 200 with `{ Name, Version, Ref, Warnings, Report }`, where `Warnings` are the report's
warning sentences and `Report` the publish report in full. Refused: 400 with `{ errors, error, report }`: every
refusal sentence, the first one, and the refused report. Nothing is written for a refusal.

**On a refusal** the engine logs one information line (package, version, the number of refusals, the first id), logs
each engine fault at error level (`31 publish engine fault`, with the sentence id and the fault's type, or the refused
plan's sentence id; never a message), and raises the operator alert **`publish-failed`** for every refused 31 publish,
an author's failing test included (`Key` the package name, `Number` the number of refusals). A fault in the alert is
logged by type and does not change the answer.

**The folder** (`V31RegistryWriter`, in the private container the account's kind picks). One writer at a time holds a
version's folder through a 60-second lease on a lock blob beside it, `{name}/{version}.lease` (never inside the folder,
so no listing takes it for a file or a version; it is left in place after the write). The holder checks the stamp (a
folder with any `publish.json` is never written to), clears the folder but the stamp, writes the files in the order the
pipeline gave (the source, `lock.json`, `publish-report.json`, `migration-report.json` for a migration, `resolve.json`
last), renewing the lease before each write, reads `resolve.json` and `lock.json` back and verifies them against the
stamp it is about to write (`CompiledPackage.Verify`, the load's own check), and then creates `publish.json` only if
absent. A lost lease or a failed check stops the write with no stamp. A writer that died holds the folder until its
lease lapses; the retry then clears and rewrites it. Media types: `resolve.json`
`application/vnd.consultologist.resolve+json`, `lock.json` `application/vnd.consultologist.lock+json`, the report
`application/vnd.consultologist.publish-report+json`, a `.md` file `text/markdown`, `dag.mmd` `text/plain`, anything
else (the stamp included) `application/json`. A migrated version's stamp carries `migratedFrom`, the old version; the
load ignores it.

**Listings.** The listings that find versions by `{name}/{version}/manifest.json` (the account's packages,
`WorkflowPackages/Mine` and `MinePackages`, which the selector and History read; the public chain view; the operator's
catalog strand sweep) were written for 5–30, which writes the manifest last. Since the 31 writer writes the manifest
first, a version whose manifest declares specVersion 31 is listed only when `{name}/{version}/publish.json` is in the
same blob listing (`AccountPackageListing.IsUnstamped31`; no extra read), so a publish in flight, one that died, or a
write that lost its lease lists nothing. Its manifest is not cached until it is stamped, and a name whose only version
is unstamped is no package. A 5–30 version is listed as before, whatever its own `publish.json`.

### `Current` at 31

`GET WorkflowPackages/Current` tries the compiled load (`CompiledPackageStore.TryLoadAsync`) for the resolved pin first.
A plan answers 200 with `V31PackageDescription.Describe(plan)`, from `resolve.json` alone:

- `SpecVersion` 31, `Name`, `Version`, `Title` (`resolve.json`'s `title`);
- `Blocks`: the top-level nodes (`parent` null) in `graph.order`, `Id` the key and `Name` the `label`; `Results`: the
  top-level deliverables in the same order, `Id` the key and `Label` the `label`; `Macros`: always `[]`;
- each input in `inputOrder`: `Id`, `Label`, `Required`, and `Type`, `Values`, `Items` and `Fields` read from the
  format the run expands (part 02 § 12's table; a shape with no row has `Type` null, and an element with none `text`);
  `ExpectedContent` and the new `Channel` are both the input's `channel`; `Types` is null;
- **`Format`** (new, trailing) is the input's format as one JSON Schema 2020-12 compound document: the input's schema
  at the root, every format it reaches under the root's `$defs` once, each with its `$id` kept, and every reference to
  one written as `{ "$ref": "<its $id URN>" }`, with no `propertyOrder` (#1508, R10: it is a schema in the dialect;
  `Fields` carry the order). A satellite resolves references by `$id`. A format that cannot be
  expanded (recursion) is `false`.
- **`Fields`** come in written order, as part 09 § 4.3 says: `resolve.json` is canonical JSON, which sorts the
  properties, so each object that has them carries `propertyOrder` beside them, the names as the source writes them
  ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7179739459), R10), and `Fields` follow it (a property it does not list, which publish never writes, comes after,
  in the order held). A field is `Required` when its type is not nullable, and its `Label` is its `title`, else the
  title of the format it names, else its name.

A load refusal (S09-009, S09-014, a `requires` sentence) is 400 `{ error }` with the job's sentence. A 31 version with
no stamp is 400 A06-004 (*"isn't published"*) when 31 is admitted, and S09-001 when it is not. A registry that cannot be
read for a 31 version is 503 *"Workflow package registry is unavailable."*, as at 5–30. A version with no 31 stamp takes
today's path, after the one stamp read.

### `Current/Content` at 31

`GET WorkflowPackages/Current/Content` keeps today's ref parse and access check, then loads as `Current` does. A
stamped version whose pair verifies is served from its folder (`V31PackageContent.ReadAsync`), **even when this engine
will not run it** (a `requires` refusal is logged, not answered), so its owner can read it and publish a new version:

- `Manifest` the stored `manifest.json`, member for member; `Files` every stored source file but `manifest.json`, and
  `dag.mmd` when stored (never the evidence, `migration-report.json`, which is `MigrationReport`, or the lock blob);
- trailing `Resolve`, `Lock` and `PublishReport`, each parsed from the stored bytes; `MigratedFrom`, the stamp's old
  version. The pages are to show it as S09-004, *"migrated by the operator from \<old version\>"* (part 09 § 6.2);
  nothing in this build composes that sentence (area 12);
- trailing `MigrationReport`, the stored `migration-report.json` parsed, for a version the operator migrated (null when
  there is none, below 31 and on an import). A stored file that is not JSON is served as null and a warning naming the
  package and version is logged; the route does not fail.

An integrity refusal (A06-004, S09-009, S09-014) is a 400 with its sentence; a folder whose `manifest.json` is not a
JSON object is A06-004; a registry failure while reading the folder is 503. All these members are null below 31.

### Import

`POST WorkflowPackages/Ingest` reads the zip's entries as bytes. A zip whose `manifest.json` is a 31 manifest takes the
31 branch: S09-001 unless admitted; otherwise the source check only (`SourceCheck.Run`: intake, which drops the
evidence unread, the reader and every rule). A refusal is 400 `{ error, errors }`. Otherwise 200 with the content
(source files but `manifest.json`, `SpecVersion` 31, `Resolve`, `Lock` and `PublishReport` null), empty `Findings`, and
`Notices` holding S07-016 once when the zip held a `resolve.json` or `lock.json`. An uploaded `dag.mmd` is dropped.
Nothing is compiled, tested or stored by an import. A 5–30 zip is read exactly as before.

### A job started on a 31 pin with the preview off

`ConsultGenerationJobStarter` answers a 31 pin it cannot admit with the error `SpecVersionNotAdmitted` and S09-001, a
400, where it gave an unhandled spec-version message before. Other versions it does not run keep
`SpecVersionNotYetExecutable` (422).

## Settings

Each setting is read once, at startup, except those read at each send or alert: `EmailIntake__MailboxAddress` and
`EmailIntake__AppBaseUrl` by the delivery channels, and `Operators__AlertAddress` (with the mailbox) by the alerts.
`Program.cs` resolves `SpecVersionAdmission` and `EngineLimitSettings` before the host runs, so a bad value stops the
host, and the message names the setting and the value. An unset, empty or white-space value is the default. The
limits are read at a job's start, recorded in its orchestration input and in the job header's `limits`, and never
re-read for that job.

| Setting | Default | What it is |
|---|---|---|
| `WorkflowPackages__PreviewSpecVersions` | unset | A comma-separated list of the spec versions admitted beside the compiled-in list. Only `31` is allowed. It is meant for the dev app (`canada-east-ai-function-dev`) only; the engine does not enforce that, so an app runs 31 wherever the setting is given. It is removed at the bump. An entry that is not an integer, is already released (5–30) or is not implemented stops the host. The attestation lists it apart, as `PreviewSpecVersions` (`[]` when unset). |
| `ConsultGeneration__MaxAgentCallsPerJob` | 16 | The per-job ceiling: agent calls in flight at once (`maxAgentCalls`) |
| `ConsultGeneration__CapacityWaitMinutes` | 10 | The minutes a node may wait on 429s in all |
| `ConsultGeneration__ServiceAttempts` | 5 | The attempts a node's call gets for other service failures |
| `ConsultGeneration__JobMinutes` | 30 | The minutes a job may run from `startedAtUtc`. Publish notes a worst case estimated past it (S09-016) |
| `ConsultGeneration__DeliveryMinutes` | 15 | Delivery's window, from the start of delivering; no attempt starts after it (recorded in `limits`; a job delivers under its own) |
| `ConsultGeneration__DeliveryAttempts` | 3 | The attempts each destination gets, the wait before the second 2 minutes and doubling (recorded in `limits`) |
| `ConsultGeneration__LoopItems` | 200 | The items one loop instance may be given (S04-078 past it) |
| `ConsultGeneration__JobAgentCalls` | 500 | The agent calls one job may make; a retry or repair is part of its call (S09-015 past it) |
| `ConsultGeneration__RenderSteps` | 100000 | The steps one render may take (S05-062 past it) |
| `ConsultGeneration__JobInstances` | 10000 | The instances one job may open: a node, check or deliverable instance, a loop item, a pass or a call (A09-001 past it; recorded in `limits` as `jobInstances`). Publish refuses a package whose worst case opens more (A11-001, #1508) |
| `ConsultGenerationRecordStorage__TableServiceUri` (or `…__ConnectionStringName`) | unset | Where the table `ConsultGenerationRecordRows` lives. Unset, the table is on the account `AccountStorage` names, as the other job tables are; the text blobs are in the job outputs container, never here |
| `ConsultGenerationRecordStorage__BlobServiceUri` | unset | The records account's blob service, where `record.json` is written, in the container `job-records` (created on the first write; the identity needs Storage Blob Data Contributor there). Unset, the document is written to a `job-records` container of the text account (`TextStorage`'s), where its 30-day rule deletes it and the next read restores it from the rows, and the host logs a warning naming this setting once at startup |
| `ConsultGeneration__RegexMilliseconds` | 2000 | R2: the time one evaluation of `matches` or `has_word` may take (A06-001 past it). It is at most 2147483646, and it is not in the record (D8) |
| `AzureAI__MaxRetries` | 0 | Keep 0 on any app that runs 31 jobs (above) |
| `Operators__AppUserIds` | nobody | Who may call the drain's endpoints, as for `Operator/*` |
| `Operators__AlertAddress` | unset | Where the operator's alerts are mailed (from `EmailIntake__MailboxAddress`). Unset, an alert is only logged |
| `OperatorWeeklySummarySchedule` | unset | An NCRONTAB value. Unset, the weekly summary's function is disabled. A flat name, since a `%…%` binding cannot resolve a `__` key |
| `EmailIntake__MailboxAddress`, `EmailIntake__AppBaseUrl` | unset | Read as the 5–30 reply reads them. Delivery's `email` and `pdf` need both; an alert needs the mailbox only |
| `Jobs__HoldNewJobs` | `false` | Unchanged. A held start is refused, not parked, during a drain or not |

Every `ConsultGeneration__*` value is an integer of at least 1. `1.5`, `0`, `-1`, words and an overflowing number
stop the host, for example *"ConsultGeneration__JobMinutes is '1.5'; it is an integer from 1 upward, or unset for
30."*

## The time limit on `matches` and `has_word` (R2)

Each evaluation of `matches` or `has_word` stops at `ConsultGeneration__RegexMilliseconds`. Past it, the
condition or computed value fails with A06-001: *"<site> <member> could not be decided: <matches | has_word> took
longer than <n> seconds on this text. Use a simpler pattern."* The site then fails, as any failure there does.
Part 03 § 5.3's bounds still hold.

**For the owner:** the spec's "`matches` reads no clock" (part 09 § 3.1) was there so that a replay decides alike.
With a limit, an evaluation close to it can decide differently on a Durable replay, which fails the orchestration.

## The drain

No job spans two builds (part 09 § 3.6). The engine has three endpoints for the release workflow. All three are
anonymous at the Functions level and gated in code as `Operator/*` is: 401 without an account, and 403 unless the
account can use the app and is listed in `Operators__AppUserIds`. CORS is applied, each answers with
`Cache-Control: no-store`, and none takes a body.

| Endpoint | `[Function]` | What it does | Answer |
|---|---|---|---|
| `POST /api/Admin/Drain` | `AdminDrain` | Begins a drain. Calling it again changes nothing. | `{ "draining": true, "producing": n, "parked": n }` |
| `GET /api/Admin/Drain` | `AdminDrainStatus` | Says where the drain stands. | `{ "draining": bool, "producing": n, "parked": n }` |
| `POST /api/Admin/Resume` | `AdminResume` | Ends the drain, schedules every parked job oldest first, once each, and raises `engine-resumed` for every orchestration waiting behind it. | `{ "draining": false, "started": n }`, where `started` counts only the jobs this call scheduled |

`producing` counts this engine's orchestrations (`ConsultGenerationOrchestrator` and
`ConsultGenerationOrchestratorV31`) that are pending, or that are running and neither asleep on a schedule (custom
status `Scheduled`) nor waiting for an update. Delivery runs inside the orchestration, so a job that is delivering
counts as producing.

**During a drain:**

- **Every door still answers a new job with its id**: HTTP (202), email, forms, rerun. The job's entity is
  initialised as usual. Its status reads *"Waiting for an update to finish"* (S09-018), and its orchestration input
  is parked instead of scheduled.
- **A scheduled job** whose time comes during the drain waits with the same status until a check after Resume
  reads not draining.
- **A scheduled job is not asleep once its timer ends.** A drain-aware orchestration sets its custom status to
  `Queued` when its schedule timer ends, before it calls `check-drain`. So a drain begun after that check has
  read "not draining" counts the job as producing, not as asleep on its schedule.
- **The wait.** A waiting orchestration waits for `engine-resumed` or 5 minutes, whichever comes first (Resume
  may have looked for it before it began to wait). Either way it sets its status to `Queued` and calls
  `check-drain` again, and it goes on only once a check reads not draining. So a second drain begun just after
  a Resume holds it too. Cancel refuses a waiting job.
- **What does not count.** Time spent waiting does not count toward a 31 job's `jobMinutes`.
- **Older orchestrations.** An orchestration started by an earlier build has no `DrainAware` and never meets the
  check, so its replay is unchanged.

**Where the state lives.** It is on the text account (`TextStorage__BlobServiceUri`), and both kinds of blob are
read on every call, never cached:

- the flag `drain/state.json` (`{ "draining": true | false }`), in `personal-job-outputs`;
- one `drain/parked/<jobId>.json` per parked job, in its account kind's outputs container (`org-job-outputs` or
  `personal-job-outputs`). Each is deleted once its orchestration is scheduled.

The flag survives a deploy, so the new build keeps parking jobs until Resume.

### What the release workflow must call, in order

The release workflow and its credential are the user's to edit; this build does not touch `.github/`. The
workflow must call these, as an operator account (a bearer token of an account in `Operators__AppUserIds`):

1. `POST /api/Admin/Drain`, before deploying.
2. `GET /api/Admin/Drain`, polled until `producing` is 0. If `producing` is still above 0 after 60 minutes, fail
   the release and do not deploy.
3. Deploy.
4. `POST /api/Admin/Resume`, once the new build is up.

If the release is abandoned at step 2, the drain stays on and new jobs keep parking until someone calls
`POST /api/Admin/Resume`. `Jobs__HoldNewJobs` is separate and works as before.

### To check on dev, and to keep

- **`producing` relies on custom status, unverified against a live task hub.** Both instance queries (the status's
  and Resume's wake-up) set `FetchInputsAndOutputs = true`, because nothing in the client's documentation shows
  the custom status comes back without it. This has not been tried against a live task hub. Check it once on
  dev: with only a scheduled job asleep on its timer and nothing else running, `GET Admin/Drain` must say
  `producing: 0`. The cost of the flag is reading the inputs of the few orchestrations running during a drain;
  none is logged or kept.
- **A narrow double-schedule window.** A start that parks re-reads the flag afterwards. If the drain has ended (a
  Resume ran between its check and its park), it starts the job itself. Both that start and Resume check that no
  orchestration with the job's id exists before they schedule it, but the check and the schedule are two steps.
  So a start re-checking at the same instant as a Resume can see both schedule the same instance id. It needs a
  start to park within the same moment a Resume runs.
- **Later builds must keep the drain-wait replay prefix.** An orchestration waiting behind the drain when the
  deploy happens replays on the new build through that wait: the schedule timer, `check-drain`,
  `MarkWaitingForUpdate`, the `engine-resumed` wait and the recheck timer, then `check-drain` again. A later
  build must not change that prefix, nor the `DrainAware` check that leads to it.
- **A 31 orchestration mid-flight at a deploy that changes its history shape must be drained first.** A replay reads
  the history the earlier build wrote, call for call, so an orchestration that has not finished cannot run on a build
  that calls its activities in another order. Area 08's build changes the shape of every 31 orchestration: a rule
  that holds `matches` or `has_word` is an `evaluate-rule-v31` call where it was none, a run that waits makes a
  second timer (the `job-slow` timer, at `startedAt` plus 80% of `jobMinutes`), and a completed run that has
  something to send calls `deliver-v31` and its timers and `alert-operator-v31`. So an orchestration that was
  mid-flight on the build before area 08 would fail its replay. Run the drain (above) before the deploy that
  carries it, and for any later deploy that changes what a 31 orchestration calls. Only dev admits 31, so the cost
  is the few preview jobs running then; a 5–30 orchestration is unchanged. **Area 09's build changes the shape again:**
  every 31 history gains `write-record-rows-v31` (once per round that recorded anything and at the end) and
  `seal-record-v31` (after `FinalizeJob`), and the entity `RecordV31Rows` and `RecordV31Seal`. **Drain 31
  orchestrations before deploying this build**, as above; only dev admits 31.

## The bulk publish (`Admin/MigratePackage`, #1508 Task 36)

The operator's migration of the accounts' packages (part 10 § 1.5) is one engine route, one account package a call,
driven by `scripts/bulk-publish-v31.cs` ([registry operations](registry-operations.md), "The bulk publish"). Both
functions are anonymous at the Functions level and gated in code as the drain's are: 401 without an account, 403
unless the account can use the app and is listed in `Operators__AppUserIds`. Then, where the preview setting does not
admit 31, both answer S09-001 as a 400 (`{ "error" }`) before anything is read. CORS is applied, and each answers with
`Cache-Control: no-store`.

| Endpoint | `[Function]` | What it does | Answer |
|---|---|---|---|
| `GET /api/Admin/MigratePackage` | `AdminMigratePackageList` | Lists every account's active account package below 31: each account's pin (`IWorkflowPackagePinResolver`), each distinct package read once as stored (`ActivePackages`, which the weekly summary's hand-edits line reads too), only `acct-…` names. | `{ "packages": [{ "owner", "name", "version", "specVersion" }], "notRead": n }`, `notRead` the pins and packages that could not be read |
| `POST /api/Admin/MigratePackage` | `AdminMigratePackage` | Migrates and publishes one package (`MigratePackage.RunAsync`). | below |

**The body:** `{ "owner": <app user id>, "name": <package>, "options": <options.json> | null, "handEdits": { "<path>":
"<text>" | null }, "publish": bool }`. `options` null (or absent) is the defaults; in `handEdits` a text replaces or adds
its path (`manifest.json` the manifest) and null deletes it, as a `.deleted` file does (a path the migration did not
write is left absent); `publish` false (or absent) is a dry run. A body that is not strict JSON, has another member,
or gives a member of the wrong shape (or deletes `manifest.json`) is a 400 with `error` naming the member.

**What it does,** in order; the first step that cannot go on answers `not migrated: <its sentence>`:

1. **The package the account's pin names**, as stored (`IWorkflowPackageSources`). A name that is not an account
   package, or that the pin does not name: A11-016. A stored package that cannot be read: A11-007 (the fault logged by
   its type).
2. **The choices:** `options` read with `Migration31Options.FromJson` (A10-070 to A10-072, A10-082), or the defaults,
   said in the answer's `defaults` (A10-083).
3. **The migration** (`WorkflowPackageMigration30To31.Migrate`, with the 24 → 25 rung's contract resolver): a refusal is
   its first sentence, A10-015 for a package at 31 already (a rerun after a publish). **A hand edit is left** unless the
   file its notice names (`Migration31Result.HandEditFiles`, one per notice) is among `handEdits`' paths, edited or null:
   any left is its first S10-002 notice, every left one in `handEditsLeft`. An operator who answers a notice in another
   file still sends the named file, unchanged if need be, as the acknowledgement. The migration counts its package
   against the engine's configured limits (`EngineLimitSettings.PublishLimits`), so A10-081 is said only past them; its snapshot's run and the
   report's worst-case note (A10-014) count at the configured `Limits` too (`Migration31Options.RunLimits`).
4. **The operator's edits** laid over it, and then, when any were given, its snapshot test written anew
   (`WorkflowPackageMigration30To31.Snapshot`, which publishes the edited package in memory at the engine's configured
   limits, `Migration31Options.Limits`: a package past them, A11-001, or an edit left undone that the checker refuses,
   its sentence, is refused here). A10-011 is then not
   said.
5. **The publish** through `V31PackagePublisher` as the account's next version, with the migration report
   (`MigrationReport.Of`, no compared text: the engine cannot run 30 offline; `newVersion` the version assigned), so the
   stamp has `migratedFrom`, `publishedBy` is `operator-migration`, the ownership is recorded and the latest pointer and
   the pin move after the stamp, as for any publish. A refusal (A11-001 at the configured limits among them) is its
   first sentence, and raises `publish-failed` as any refused 31 publish does. With `publish: false` the same pipeline
   runs once in memory (`V31PackagePublisher.CheckAsync`): no write, no pointer, no pin, no alert.

A package that is not migrated has nothing written: its 30 version and its pin are as they were (comment 8 on #1508).
A fault of the account store or the registry's storage is not answered as `not migrated`: it reaches the route as a 500.
Before the stamp nothing is published; after it the version is stamped and the pointer or the pin may not have moved.

**The answer** (200): `{ "owner", "name", "oldVersion", "newVersion", "outcome", "handEditsLeft", "notices", "report",
"options", "defaults" }`. `outcome` is `published`, `would publish` or `not migrated: <sentence>`; `newVersion` is the
version published (or that would be), else null; `notices` the migration's notices that need no edit (A10-013, `flag`
becoming `retry`, among them), as the migration report lists them; `report` the publish report (null when no publish
ran); `options` the choices used, as `options.json` writes them; `defaults` A10-083 or null. No member carries input,
model or profile text: ids, versions, sentences, hashes and the choices.

## Sizing the deployment-wide limits (D10)

The per-job ceiling bounds one job. Nothing counts calls across jobs (part 09 § 3.3). Two host settings bound the
deployment, and both are sized to the model deployment's quota. **This build sets neither: their values are the
user's to state.**

- `host.json`: `extensions.durableTask.maxConcurrentActivityFunctions`, the activities one instance runs at once.
  It is unset today, so the Durable default applies per instance.
- The Flex Consumption app's maximum instance count.

To size them:

1. Work out how many model calls the deployment's quota allows in flight at once: the requests per minute it
   allows, times a typical call's seconds, divided by 60, with margin for the tokens-per-minute limit.
2. Choose instances × `maxConcurrentActivityFunctions` at or below that number. Every activity counts against
   `maxConcurrentActivityFunctions`, not only agent calls (renders, `load-plan-v31`, `check-drain` and the 5–30
   activities share it), so the bound is on the safe side.

A 429 that still gets through is retried (above). So a deployment sized too generously makes jobs slower, until a
node's 429 waits pass `CapacityWaitMinutes` and it fails with S09-010. Record the values chosen in
[CONFIGURATION.md](../CONFIGURATION.md) beside the deployment notes.

## Before 31 leaves preview (R7, #1508)

31 is admitted outside preview (joins `AcceptedSpecVersions`, and `WorkflowPackages__PreviewSpecVersions` is removed)
only once these hold. They are the owner's conditions on #1508, word for word; [registry operations](registry-operations.md)
keeps the same list beside dev's registry.

From [the third comment](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7073314794)
(item 3):

> 3. **Conditions before 31 is admitted outside preview,** recorded in this area's publish notes and the release plan:
>    - the v31 agents (`judgement` 1) are attested in Foundry; today they are attested against git only (D9);
>    - `consultologist-agents` catalogue `v2026.12.2` (`concept_list` with `source`, `judgement`'s newline schema) and the content-repo changes are released from their `m39-build` branches.
>
>    Until then, a 5–30 host does not resolve the 31 catalogue (#1505, ruling 7).

From [the fourth comment](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7082079188):

> **Added to the conditions before 31 is admitted outside preview** (beside [the earlier list](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7073314794)):
> - a dev-deployment run of a 31 job, including a forced replay: the Azure table and blob adapters (insert-if-absent, `row1…` splitting, the 1 MiB check) have only run over in-memory seams;
> - the owner sets `Operators__AlertAddress` and `OperatorWeeklySummarySchedule`, and drains 31 orchestrations before deploying (their history gains `job-slow`, `evaluate-rule-v31` and the delivery step).

From [the further rulings of 2026-11-25](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7171840295)
(item 3):

> **Before 31 leaves preview,** these same packages also run **live on the dev deployment** with fictional inputs. This joins the release conditions above (the dev run with a forced replay). The full live-model test suite stays Milestone 40 (#1449).

"These same packages" are the edge-case packages (`tests/Fixtures/ByteEquivalence/migrated/edge-*`); how to publish
and run them on dev is in [registry operations](registry-operations.md), "Before 31 leaves preview".
