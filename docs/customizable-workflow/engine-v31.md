# The engine at specVersion 31 (preview)

*Area 06 of the Milestone 39 build (#1503). Built from
[the engine and the pages](package-format-v31/09-engine-and-pages.md) §§ 1, 2.1, 3.1 to 3.4 and 3.6, and
[the plan](package-format-v31/plans/area-06-engine-loading-and-scheduling.md) for the decisions. The library the
engine runs (`RunPlan`, `PlanRunner`, the seams) is described in
[the package format as a library](package-format-library.md), "specVersion 31: the run".*

This page is for whoever deploys and operates the engine. It covers:

- how a 31 package is loaded and a 31 job started;
- the settings and their defaults;
- the limits and retries;
- the drain that comes before every deploy, and what the release workflow must call;
- how to size the deployment-wide limits.

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
`CompiledLoad(RunPlan? Plan, string? Refusal, string? RefusalId, string? OperatorDetail)`. It runs these steps in
order:

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

**What the engine runs** (`EngineCapabilitiesFactory.Current()`):

- **Kinds:** all eight.
- **Features:** `parallel` and `check-loops`. `parts`, `macro-markers` and `run-spots` are refused (S07-004) until
  area 07's body composer lands. The reasons are in `EngineCapabilitiesFactory.FeaturesNotRun`.
- **Agents:** the ones the engine already loads and attests at startup: the output contracts, the formats
  catalogue and its custom producer. **The 31 agents catalogue (`agents/v31/agents.json`) is not loaded.** So a
  package that binds an agent only that catalogue lists (for example `judgement` 1) is refused with S07-005.
- **Break points:** `EngineBreakPoints.All`, compiled in (D2) and empty today. One is added only by an upgrade
  that went through the corpus re-render, which is a code change.

## Starting a job

`ConsultGenerationJobStarter.StartAsync` calls the 31 branch (`V31JobStarter`) after the hold, the rate limit and
the package ref, and before today's `ResolveAsync`. A `null` load continues on today's path unchanged. The
branch does this:

1. **A load refusal** becomes a start failure on the job (`RecordStartFailure`, with the clinician's sentence;
   the operator's detail goes to the log). The outcome is `PackageContentRejected`, with the job's id.
2. **The inputs.** The request's typed `Inputs` (`ConsultDraft` back-fills `consult_draft`, as today) and its
   documents are read. The documents go through the starter's own extraction, so a document sent to a text input
   arrives as its text, behind the same OCR and speech-to-text gates as today.
3. **The account.** The profile fields that `needs.profile` lists are read. `name` is the display name,
   `signature` is the chosen block, and the others are the account settings `profile.credentials`,
   `profile.organisation` and `profile.closing`. The time zone is `account.timeZone`, an IANA name (D6).
4. **The checks.** `V31StartChecks.Account` gives S06-031 and S06-032 for the profile, then S06-008 for a zone
   that is blank or not an IANA zone the machine knows. `V31StartChecks.Inputs` gives the input refusals:
   - **A06-011** *"Input '<id>' is required; fill it in to run this workflow."* for a required input that is
     absent, `null` or blank, whatever its format (R5);
   - **A06-012** *"Input '<id>' was given a value that isn't <format>."* (numeric enums included, R4);
   - **A06-013** *"Unknown input(s) <ids> (declared: <ids>)."*

   An empty list is accepted (R1). Every refusal goes into one start failure, the sentences joined by line ends,
   with outcome `InputsMismatch`. The sender is told every sentence except A06-013, which quotes the caller's own
   ids.
5. **The held inputs.** The inputs document
   (`{ "inputs": { <id>: { value, content, title } }, "profile": { <field>: text | null } }`) is written where the
   job's held inputs live: `org-job-inputs` or `personal-job-inputs`, `{appUserId}/{jobId}.json`. The retention
   drop and account closure therefore delete it as they delete a 5–30 job's.
6. **The record.** The job's entity is initialised at `PackageSpecVersion` 31, with `ResolveHash`, `LockHash` and
   `Limits`.
7. **The schedule.** Unless the engine is draining, `ConsultGenerationOrchestratorV31` is scheduled with
   `InstanceId` = the job id.

A 31 start failure's History line is "Not started".

**Not read at 31 yet.** A 31 start reads typed inputs and documents only. These request members are not read,
so an input sent only through one of them reads as missing (A06-011):

- `InputRefs`, the references to a previous run's input or result;
- `InputFormRefs` and `InputFormFiles`, a form response's references and files;
- `MacroChoices`.

Reruns and the pages that send references are area 12's; macro choices are area 07's.

## The orchestration

`ConsultGenerationOrchestratorV31` takes a `ConsultGenerationOrchestrationInputV31(JobId, AppUserId, PackageName,
PackageVersion, ResolveHash, LockHash, Limits, RegexMilliseconds, InputsBlob, RunFactsBase, TimeZone,
ScheduledAtUtc, DrainAware, AccountKind)`. The input never carries inputs inline: `InputsBlob` names the job's
held inputs. The limits and the regex limit are the ones the start read, so a replay never reads a setting that
has changed since. The orchestration runs these steps in order:

1. It waits for the job's schedule, as the 5–30 orchestration does.
2. **The drain** (`DrainAware`): while the engine drains, it waits here (below).
3. It takes `startedAtUtc` from the replay-safe clock. The job limit is timed from here, so time spent queued or
   waiting behind a drain does not count.
4. It records the header on the job's entity in one call, before any node runs: `RecordV31Header` with the two
   hashes, the nine limits and `startedAtUtc`. Then it calls `MarkRunning`.
5. **`load-plan-v31`** loads and verifies the version again through the store. The plan's `resolveHash` must be
   the job's, else S09-009; its `lockHash` must be too, else S09-014. A version no longer stamped 31 is A06-004.
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
   - `DurableRunHost`: an agent call is `run-agent-node-v31`, a render is `render-node-v31`, and a wait is a durable
     timer;
   - `EntityRunRecorder`: progress counts on the entity through `RecordV31Progress`, and each row to telemetry
     as its key, state and value hash.
8. It finalises the job: `Completed`, or `Failed` with the run's sentence. A timed-out run is a failed job with
   S09-012.

A fault the run cannot say as a node's sentence fails the job with **A06-010**: *"The engine couldn't finish this
job. Tell the operator."* The exception's message is never used, since it can carry a service's response; the log
gets the fault's type only.

**The history.** `load-plan-v31`'s result holds one copy of `resolve.json`, gzipped. That copy counts toward the
10 MB gate on the orchestration history (index § 5; part 09 § 3.1), as do the rest of what the history holds:

- `load-run-inputs-v31`'s result, the inputs document, clinical text included;
- each agent activity's input, the call's scope;
- each agent activity's result, the reply.

The dev dry run measures the history's size and replay time on the largest example package.

**Activities.** `run-agent-node-v31`, `render-node-v31`, `load-plan-v31`, `load-run-inputs-v31` and `check-drain`.
None of the first four has a Durable retry policy (D7), because the runner retries. `check-drain` has 3 attempts,
from 5 seconds, so one storage hiccup does not fail a job.

## Agent calls and retries

`run-agent-node-v31` (`RunAgentNodeV31Activity`) makes one attempt of one call. It loads the plan through the
store's cache, held to the job's `resolveHash`, and composes the entry's body with the job's recorded
`renderSteps`. It finds the agent in the plan's `bindings` (`bindings.<format key>.<producer>`), then sends the
body once:

```csharp
Task<AgentAttempt> TryPromptOnceAsync(string stage, string userMessage, string agentName, string agentVersion, CancellationToken cancellationToken);
```

| What happened | The outcome |
|---|---|
| a reply | `Completed`, with the text |
| an empty reply (`SendPromptAsync`'s *"No assistant response found"*) | `Completed` with `""`, so the node's `onInvalid` and `failIfEmpty` decide it; its token counts are lost |
| HTTP 429 | `Throttled`, waiting `retry-after-ms`, else `retry-after` (seconds or an HTTP date) |
| a 5xx | `ServiceFailure` `error` |
| a time-out | `ServiceFailure` `timeout` |
| no connection | `ServiceFailure` `unreachable` |
| any other status (401, 403, 404, …) | propagates, and the job fails with A06-010 |
| a call that cannot be made | `Failed`: `plan` (the plan is not the job's, or lacks the entry or its binding), `steps`, `compose` |

When the service gives no wait, the wait is `AgentBackoff.Wait(attempt, jitter)`: 5 seconds, doubling, capped at
60 seconds, plus up to 20% jitter (5,000 to 6,000 ms after the first attempt, never over 72,000). The wait is in
the activity's result, so it is in the history. The runner then waits on a durable timer, holding the call's
slot, and calls again. A node fails after these limits:

- `CapacityWaitMinutes` of 429 waits in all: S09-010;
- `ServiceAttempts` attempts: S09-011, which names the last error's kind, never its content.

**One attempt per call holds only while `AzureAI__MaxRetries` is 0** (its default). `SendPromptAsync` builds its
own client retry policy from that setting on every call. A non-zero value makes the SDK retry a 429 or a 5xx
inside one attempt. Those retries sit under the orchestration's own and outside the record's retry rows. **Leave
it at 0 on any app that runs 31 jobs.**

Telemetry from the activity carries the key, the attempt, lengths, hashes and token counts, never text. The tokens
are not recorded on the job yet (area 09).

## Settings

Each setting is read once, at startup. `Program.cs` resolves `SpecVersionAdmission` and `EngineLimitSettings`
before the host runs, so a bad value stops the host, and the message names the setting and the value. An unset,
empty or white-space value is the default. The limits are read at a job's start, recorded in its orchestration
input and in the job header's `limits`, and never re-read for that job.

| Setting | Default | What it is |
|---|---|---|
| `WorkflowPackages__PreviewSpecVersions` | unset | A comma-separated list of the spec versions admitted beside the compiled-in list. Only `31` is allowed, on `canada-east-ai-function-dev` only, and it is removed at the bump. An entry that is not an integer, is already released (5–30) or is not implemented stops the host. The attestation lists it apart, as `PreviewSpecVersions` (`[]` when unset). |
| `ConsultGeneration__MaxAgentCallsPerJob` | 16 | The per-job ceiling: agent calls in flight at once (`maxAgentCalls`) |
| `ConsultGeneration__CapacityWaitMinutes` | 10 | The minutes a node may wait on 429s in all |
| `ConsultGeneration__ServiceAttempts` | 5 | The attempts a node's call gets for other service failures |
| `ConsultGeneration__JobMinutes` | 30 | The minutes a job may run from `startedAtUtc` |
| `ConsultGeneration__DeliveryMinutes` | 15 | Delivery's window. It is recorded in `limits`; delivery itself is area 08's |
| `ConsultGeneration__DeliveryAttempts` | 3 | The attempts each destination gets. It is recorded in `limits`; delivery itself is area 08's |
| `ConsultGeneration__LoopItems` | 200 | The items one loop instance may be given (S04-078 past it) |
| `ConsultGeneration__JobAgentCalls` | 500 | The agent calls one job may make; a retry or repair is part of its call (S09-015 past it) |
| `ConsultGeneration__RenderSteps` | 100000 | The steps one render may take (S05-062 past it) |
| `ConsultGeneration__RegexMilliseconds` | 2000 | R2: the time one evaluation of `matches` or `has_word` may take (A06-001 past it). It is at most 2147483646, and it is not in the record (D8) |
| `AzureAI__MaxRetries` | 0 | Keep 0 on any app that runs 31 jobs (above) |
| `Operators__AppUserIds` | nobody | Who may call the drain's endpoints, as for `Operator/*` |
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
- **A scheduled job** whose time comes during the drain waits with the same status until Resume.
- **The wait.** A waiting orchestration checks the drain again every 5 minutes, in case Resume looked for it
  before it began to wait. Cancel refuses a waiting job.
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
  `MarkWaitingForUpdate`, the `engine-resumed` wait and the recheck timer. A later build must not change that
  prefix, nor the `DrainAware` check that leads to it.

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
