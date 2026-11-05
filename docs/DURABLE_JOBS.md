# Durable Jobs, Server-Sent Events, and .NET Version Notes

## Summary

This document captures the Phase 3 async job-based generation option for consult section generation. The current app is at Phase 2: the Blazor UI sends one aggregate consult-generation request, and the Azure Function fans out section generation concurrently while holding the HTTP request open.

Phase 3 changes the unit of work from an HTTP request to a durable job. The browser starts a job, receives a `jobId`, and then observes progress while background work generates sections and writes durable state.

```text
Current Phase 2
Blazor -> POST /api/ConsultGeneration -> Function waits -> Foundry calls -> final response
```

```text
Phase 3
Blazor -> POST create job -> jobId returned
Worker/orchestrator -> generates sections in background -> updates durable state
Blazor -> status stream or status reads -> renders partial/final results
```

## Durable Functions + SSE + Durable State

Durable Functions + Server-Sent Events + Durable State is the most managed orchestration version of Phase 3. It is more capable than a queue/table/polling design, but it also adds more framework and hosting complexity.

```text
Blazor UI
  |
  | POST /api/ConsultGenerationJobs
  v
HTTP Starter Function
  |
  | schedules durable orchestration
  v
ConsultGenerationOrchestrator
  |
  | fans out one activity per section
  v
GenerateSectionActivity
  |
  | calls existing AgentSectionGenerator
  v
Azure AI Foundry Agent
```

SSE status flow:

```text
Blazor UI
  |
  | GET /api/ConsultGenerationJobs/{jobId}/events
  v
SSE Status Stream Function
  |
  | reads durable orchestration/entity state repeatedly
  v
event: progress / block-completed / block-failed / done
```

Durable Functions responsibilities:

| Area | What It Means Here |
|---|---|
| Orchestration | A `ConsultGenerationOrchestrator` becomes the durable job controller. |
| Fan-out/fan-in | The orchestrator schedules one durable activity per section and waits for completion. |
| Recovery | If the Function host restarts, Durable replays orchestration history instead of losing the job. |
| Status APIs | Durable provides orchestration start, status query, terminate, event, purge, and management concepts. |
| Constraint | The orchestrator must not call Foundry directly. Foundry and other external I/O belong in activity functions. |

Durable orchestrator code must stay deterministic. It should call Durable activities and entities through the orchestration context, not start arbitrary HTTP calls, timers, random IDs, or wall-clock operations directly.

## State Model

There are three distinct state layers in the Durable version:

| State Type | Best Use | Limitation |
|---|---|---|
| Orchestration runtime state | `Running`, `Completed`, `Failed`, timestamps, final output | Good for job-level status, not ideal for rich live partial output. |
| Custom status | Small progress snapshot: counts, stage, per-section statuses | Not suitable for full generated prose because custom status has a small payload limit. |
| Durable Entity | Durable per-job state object, including generated/failed sections keyed by section ID | More moving parts, but better for partial generated sections before orchestration completion. |

For this app, custom status should contain progress metadata only:

```json
{
  "status": "Running",
  "totalSectionCount": 9,
  "completedSectionCount": 4,
  "failedSectionCount": 1,
  "sections": {
    "hpi": "Completed",
    "past_history": "Completed",
    "exam": "Failed"
  }
}
```

Full generated section text should be stored in a Durable Entity or another queryable store, not in custom status.

Example Durable Entity state:

```text
ConsultGenerationJobEntity(jobId)
├─ Status
├─ TotalBlockCount            (stamped once — at Initialize, or at Decide for a
│                              deciding job; v10 #496)
├─ Deciding / DecidedAtUtc    (null DecidedAtUtc = not yet decided)
├─ Classifications            (classifier node → answered value)
├─ CompletedBlockCount
├─ FailedBlockCount
└─ Sections
   ├─ hpi: Completed + generated text
   ├─ exam: Failed + error
   └─ assessment: Running
```

## Rich Per-Section Completions

The main design caveat is that a simple fan-out/fan-in orchestration does not naturally publish rich per-section completions.

Insufficient pattern:

```csharp
var tasks = sections.Select(section =>
    context.CallActivityAsync<SectionGenerationResult>("GenerateSectionActivity", section));

var results = await Task.WhenAll(tasks);

return results;
```

This schedules all section activities and waits until all are done. It is good for backend parallelism, but poor for live progress because app code does not publish `hpi completed` or `exam failed` when each individual task finishes.

Rich per-section completions require the orchestration to observe completed activities incrementally and write state after each completion.

Required flow:

```text
schedule all section activities
while unfinished activities remain:
  wait for next completed activity
  read that section result
  persist section result/status immediately
  update job progress immediately
finish orchestration after all sections are completed/failed
```

Conceptual result flow:

```text
Section A completes -> write A result -> SSE sends A completed
Section C fails     -> write C error  -> SSE sends C failed
Section B completes -> write B result -> SSE sends B completed
All done            -> write final job status -> SSE sends done
```

Each activity should return a section-level success/failure envelope so one failed section does not crash the whole orchestration:

```csharp
public record SectionGenerationResult(
    string BlockId,
    string BlockName,
    bool Success,
    string? GeneratedText,
    string? Error);
```

Preferred incremental orchestration shape:

```csharp
var pending = sections
    .Select(section => context.CallActivityAsync<SectionGenerationResult>(
        "GenerateSectionActivity",
        section))
    .ToList();

var completed = new List<SectionGenerationResult>();

while (pending.Count > 0)
{
    var finishedTask = await Task.WhenAny(pending);
    pending.Remove(finishedTask);

    var result = await finishedTask;
    completed.Add(result);

    context.SetCustomStatus(BuildProgressSnapshot(completed, sections.Count));

    // Also write the full section result to a Durable Entity or another queryable store.
}
```

The live UI flow then becomes:

```text
Activity finishes
  -> Orchestrator observes completion
  -> Orchestrator writes section state
  -> SSE endpoint sees changed state
  -> Browser receives block-completed event
  -> Blazor renders that section immediately
```

Without this incremental write, SSE can honestly show `job running`, but not rich per-section completions until the whole fan-in finishes.

## Server-Sent Events

SSE is a one-way stream from the Function app to the browser. It can make generation feel live without the browser issuing repeated polling requests.

Example event stream:

```text
GET /api/ConsultGenerationJobs/{jobId}/events

event: snapshot
data: { "status": "Running", "completedSectionCount": 2, "failedSectionCount": 0, "totalSectionCount": 9 }

event: block-completed
data: { "sectionId": "hpi", "generatedText": "..." }

event: block-failed
data: { "sectionId": "exam", "error": "Azure AI request timeout" }

event: done
data: { "status": "Completed" }
```

The SSE endpoint should not wait on the orchestration itself. It should read durable state, emit changes, wait briefly, and read again:

```text
open SSE response
emit current snapshot
loop while job is active:
  read durable state
  emit changed section events
  emit progress snapshot
  delay briefly
emit final done/failed/canceled event
close response
```

SSE tradeoffs:

| Option | Pros | Cons |
|---|---|---|
| Server-Sent Events | Better UX than polling, immediate progress events, simple browser `EventSource` API | Long-lived HTTP connection, reconnect/resume handling, must validate Azure Functions hosting behavior |
| Polling | Simple, reliable with Blazor WASM + Azure Functions, easy to implement and debug | Slight delay between updates; repeated GET requests |
| Manual Refresh | Easiest implementation | Poor workflow because users must refresh progress themselves |

SSE should be prototyped in the deployed Azure Functions hosting environment before making it the only progress path. A polling endpoint should remain available as a fallback.

## Draining before a release (#1302)

A release replaces the code that running orchestrations replay through. Most
releases are safe across that line, because Durable payloads only ever gain
trailing optional fields that bind null on replay. A release that changes the
scheduler's shape (v29 design § 7) is cut **after a drain**, and any release
may be.

1. **Hold new jobs.** `Jobs__HoldNewJobs=true` (`docs/CONFIGURATION.md`,
   "Holding new jobs for a release"). Every door then refuses a new consult by
   name; email is queued and starts by itself once the hold clears.
2. **Wait for the engine to empty.** Run `scripts/show-active-jobs.sh` until it
   ends `Safe to release`: no orchestration running, and no `Scheduled` job
   due to wake within the window (`--window`, default 30 minutes). What counts
   is the task hub's own instance table (`consultologistjobsInstances` on the
   host account), not the job index: the index holds rows left at `Queued` by
   starts whose orchestration was never scheduled (seven from August and
   September 2026), which would hold a drain forever. The script lists those
   separately and doesn't count them.
3. **Cut the release** (`gh release create vYYYY.MM.N --target main …`), and
   wait at least five minutes after the deploy finishes before verifying, since
   a job can land on an instance that hasn't been replaced yet.
4. **Verify** (`Public/Engine` reports the new release; `HttpProbe` 204).
5. **Clear the hold.** Delete the setting; queued email starts on the next poll.

**A scheduled job further out is not waited for.** It is an orchestration that
has already started, asleep on a durable timer for up to seven days
(`ConsultGenerationEngine`, `context.CreateTimer`), so the hold cannot stop it
and a drain cannot wait for it. It wakes on the new code, having recorded
nothing but its timer. That is safe only while payload changes stay
append-only, so a release that drains still keeps them that way, as the
second line of defence. `show-active-jobs.sh` lists those jobs with their wake
times, so the release notes can name any that will wake on the new code.

## Architecture Choices

Worker model tradeoffs:

| Option | Pros | Cons |
|---|---|---|
| Durable Functions | Built-in orchestration, recovery, status management, fan-out/fan-in, termination support | Adds Durable package/model complexity; orchestrator determinism rules; replay/versioning care |
| Queue Trigger | Simple, Azure Functions-native, good for per-section work, easy retry model | Job orchestration, state aggregation, and cancellation are manual |
| Timer Poller | Simple conceptually, no queue trigger needed | Less responsive, less scalable, awkward for parallel section work |

Storage tradeoffs:

| Option | Pros | Cons |
|---|---|---|
| Durable Entity / Durable State | Keeps job state tied to orchestration lifecycle; good if using Durable Functions | Custom status is too small for full note text; Durable Entities add complexity |
| Azure Tables | Good for keyed job metadata and per-section rows; cheap and queryable | More explicit schema and state-management code |
| Blob JSON | Simple file-like model | Concurrent per-section updates are awkward and risk overwrite conflicts |

Recommended Durable-heavy shape:

```text
HTTP Starter Function
  -> starts orchestration and returns jobId

ConsultGenerationOrchestrator
  -> schedules GenerateSectionActivity for each section
  -> observes activity completions incrementally
  -> updates custom status with progress metadata
  -> writes full section outputs/errors to Durable Entity

SSE Status Function
  -> reads Durable Entity/custom status
  -> emits changed section events
  -> closes when job reaches terminal state

GET Status Function
  -> returns current durable state as fallback for polling/reconnect
```

## .NET 10 Runtime

The current API project targets `net10.0`, and the repository pins the SDK to `10.0.102` in `global.json`. The Blazor WebAssembly app also targets `net10.0`.

Runtime baseline:

| Area | Current setting |
|---|---|
| Blazor WebAssembly target framework | `net10.0` |
| Azure Functions target framework | `net10.0` |
| Azure Functions model | Isolated worker on Azure Functions v4 |
| SDK pin | `10.0.102` with `rollForward: latestFeature` |
| GitHub Actions SDK | `10.0.x` |
| Expected hosting plan | Linux Flex Consumption or another .NET 10-compatible Functions plan |

Durable Functions impact:

| Area | .NET 10 behavior |
|---|---|
| Durable orchestration model | Supported in isolated worker |
| Activity/orchestrator structure | Same conceptual design |
| Need for Durable package | Yes |
| SSE fallback requirement | Keep polling/status endpoints as reconnect and fallback paths |

Hosting remains the main deployment check. The Function App should not be on Linux Consumption for .NET 10 isolated Functions. Confirm the app remains on Linux Flex Consumption, and if the runtime stack needs to be set explicitly, use the supported Linux `linuxFxVersion` value from `az functionapp list-runtimes --os linux`.

## Recommended Implementation Direction

Default safe path:

```text
.NET 10
  + Durable Functions
  + Durable custom status for progress metadata
  + Durable Entity or Azure Tables for full section outputs
  + polling status endpoint first
  + optional SSE endpoint after deployed-hosting prototype
```

More ambitious path:

```text
.NET 10
  + Durable Functions
  + Durable Entity for section outputs
  + SSE as primary progress stream
  + polling/status endpoint as reconnect and fallback path
```

The core implementation requirement is independent of .NET version: rich per-section completions require writing section state immediately after each activity completes. Without that, the frontend can show only coarse `running` status until the entire durable fan-in completes.
