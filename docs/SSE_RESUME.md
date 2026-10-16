# SSE Resume Plan

> **Amendment, 2026-10-16 (#1051).** On prod the catch-up store refused a job's `snapshot`/`done` row:
> the whole public job response (~46K characters on a test/general job) in one Table property, over
> the 64KB / 32K-character limit, and the live stream closed without `done` (the client reconnected
> once, then polled). Since #1051 a row holds its payload in chunks (`PayloadJson`, `PayloadJson1` …
> `PayloadJson15`, `PayloadChunkCount`), event JSON omits nulls and never carries the held input text,
> and a stream whose events the store still refuses keeps streaming: it sends them **without ids**
> (not resumable) and always sends `done`. The polling path logs the refusal and serves the job.

## Goal

Consult generation progress should use this resilience chain:

```text
SSE live stream -> SSE resume -> polling fallback
```

The live SSE stream provides real-time progress. SSE resume should recover from transient stream drops by replaying missed semantic events. Polling remains the final fallback and authoritative way to retrieve current or terminal job state.

Job ownership and authorization continue to use the authenticated account and the consult generation job ID. SSE event IDs are cursors within an authorized job stream; they are not authorization tokens.

## Current State

- The app has a working live SSE stream at `GET /api/ConsultGenerationJobs/{jobId}/events`.
- The app has polling fallback through `GET /api/ConsultGenerationJobs/{jobId}`.
- Semantic SSE events include persisted `id:` values in the form `{jobId}:{sequenceNumber}`.
- Semantic stream events are materialized from Durable job state into an append-only Azure Table log.
- Heartbeat events are not persisted and do not advance the semantic event sequence.
- `Last-Event-ID` replay is implemented with manual `HttpClient` reconnect and one retry before polling fallback.

## Target Architecture

- Keep `GET /api/ConsultGenerationJobs/{jobId}` as the authoritative current/final state endpoint.
- Keep `GET /api/ConsultGenerationJobs/{jobId}/events` as the live progress endpoint.
- Add ordered SSE event IDs in this form:

```text
{jobId}:{sequenceNumber}
```

Example:

```text
id: 04c45d68053c4b75b5db57e04b977e01:000000000012
event: concepts-extracted
data: {...}
```

- Persist semantic stream events so reconnects can replay missed events.
- On reconnect, read `Last-Event-ID`, replay events after that sequence, then continue streaming live events.
- If live SSE or resume fails, fall back to polling.

## Phases

### Phase 1: Current SSE and Polling

- Keep the current SSE stream and polling fallback.
- Treat SSE as an optimization for progress updates.
- Treat polling as the source of truth for current and terminal job state.

### Phase 2: Diagnostic SSE IDs

- Add `id:` values to emitted SSE events without implementing replay yet.
- Use monotonically increasing sequence numbers scoped to the opened stream connection.
- Use IDs for browser Network tab diagnostics and server logs.
- Do not rely on IDs for client recovery until events are persisted.
- Expect Phase 2 IDs to restart at `000000000001` after reconnects.

### Phase 3: Persist Semantic Event Log

- Add an append-only event table for consult generation stream events.
- Persist only semantic events:
  - `snapshot`
  - analysis stages
  - section prose steps
  - `block-completed`
  - `block-failed`
  - `done` for successful terminal job completion
  - terminal `error` for failed terminal jobs
- Do not persist heartbeat events.
- Make event writes idempotent so retries do not duplicate sequence numbers.
- Materialize missing semantic events from Durable job state during SSE streaming and normal polling.
- A terminal failed job ends its stream with `error` and does not emit `done`.

### Phase 4: Stream Exit Diagnostics

- Add a client-generated SSE attempt ID for each stream open.
- Send the attempt ID to `GET /api/ConsultGenerationJobs/{jobId}/events`, either as `attemptId` query string or `X-Consultologist-Sse-Attempt` header.
- Include `JobId`, `AttemptId`, latest event ID, latest event type, elapsed time, and emitted event counts in server logs.
- Log one server-side stream exit record from `finally` with an explicit exit reason:
  - `Completed`
  - `TerminalFailure`
  - `RequestAborted`
  - `ServerTimeout`
  - `ServerError`
  - `TerminalInitialState`
  - `ChannelCompleted`
- Use `Completed` only when the stream reaches a successful terminal job state.
- Use `TerminalFailure` when the stream reaches a failed terminal job state.
- Add a small authenticated diagnostics endpoint such as:

```text
POST /api/Diagnostics/SseExit
```

- Send client-side stream exit diagnostics with:
  - `JobId`
  - `AttemptId`
  - reason
  - latest event ID
  - latest event name
  - elapsed time
  - received event count
  - whether polling fallback started or completed
  - `document.visibilityState`
  - `navigator.onLine`
  - whether a service worker controlled the page
- Use explicit client reasons:
  - `completed-via-sse`
  - `ended-before-done`
  - `timeout`
  - `exception`
  - `server-error-event`
  - `manual-cancel`
  - `component-disposed`
  - `navigation`
  - `polling-fallback-started`
  - `polling-fallback-completed`
- For terminal failed jobs, the expected client diagnostic is `Reason=server-error-event` with `LatestEventType=error`.
- For successful terminal jobs, the expected client diagnostic is `Reason=completed-via-sse` with `LatestEventType=done`.
- Do not initially intercept or mutate SSE traffic in the service worker. Record whether a service worker controlled the page first; add service-worker fetch diagnostics later only if attempt-level correlation still leaves the cause ambiguous.

### Preparation Between Phase 4 and Phase 5

These items were completed before enabling `Last-Event-ID` replay:

1. Validate Phase 4 diagnostics:
   - Confirm successful streams report `Reason=completed-via-sse` with `LatestEventType=done`.
   - Confirm terminal failed jobs report `Reason=server-error-event` and server `ExitReason=TerminalFailure`.
   - Cover success, terminal failure, navigation/component disposal, polling fallback, and anonymous diagnostics `401` cases.
2. Clarify stream terminal semantics:
   - `done` means successful terminal stream completion.
   - failed terminal jobs emit `error` and do not emit `done`.
   - server exit reasons include `TerminalFailure`.
3. Clean public response payloads:
   - replayable polling/SSE payloads no longer expose internal concept extraction arrays or validation warning internals.
   - replay stores only sanitized UI-facing event payloads.
4. Add event-store replay support:
   - `IConsultGenerationJobEventStore` supports reading stored events after a sequence with `ReadAfterAsync(jobId, appUserId, sequence)`.
5. Choose client reconnect design:
   - keep the existing Blazor `HttpClient` streaming implementation.
   - add manual reconnect logic in `Pages/Consults.razor`.
   - keep one opaque `attemptId` per physical SSE connection.
6. Choose cursor transport:
   - use the SSE-standard `Last-Event-ID` header for the replay cursor.
   - keep `attemptId` in the query string for diagnostics.
   - allow `Last-Event-ID` through CORS.
7. Stage replay activation:
   - parse and validate the cursor server-side.
   - authorize by bearer token and job ownership before replaying anything.
   - replay stored events after the requested sequence, then continue live streaming.
   - enable one manual reconnect attempt before falling back to polling.

### Phase 5: Implement `Last-Event-ID` Replay

- Replayable public payloads are limited to UI state.
- Polling responses and replayable SSE `snapshot`/`done` payloads should not expose internal preprocessing arrays:
  - `PatientConcepts`
  - `ProblemContext`
  - `TypicalTrajectoryConcepts`
  - `PatientTrajectoryConcepts`
  - `ValidationWarnings`
- Successful terminal jobs emit `done` with the sanitized public job response.
- Failed terminal jobs emit `error` with a sanitized failure category/message and do not emit `done`.
- This keeps replay from preserving and re-sending internal concept extraction details.
- Parse `Last-Event-ID` from reconnecting SSE requests.
- Validate that the event ID job portion matches the requested route job ID.
- Authorize the caller against the job owner before replaying anything.
- Replay persisted events after the requested sequence number.
- Continue live streaming after replay completes.

### Phase 6: Harden Fallbacks and Observability

- Keep polling as the final fallback for stream failures, resume failures, and timeouts.
- Log stream starts, reconnects, replay counts, last event IDs, and fallback reasons.
- Add smoke coverage for:
  - live SSE
  - reconnect with `Last-Event-ID`
  - invalid or mismatched `Last-Event-ID`
  - polling fallback after stream failure

### Phase 7: Profile Job History

- Add a user-indexed job history table for the Profile tab.
- Do not use the SSE event log as the Profile history source.
- Write an index row when a job is created.
- Update the index row when a job starts, completes, fails, or progress counts change.
- Add an authenticated list endpoint:

```text
GET /api/Account/Jobs?limit=20&continuationToken=...
```

- Return only jobs belonging to the authenticated `AppUserId`.
- Show recent jobs in the Profile tab with status, timestamps, and progress counts.
- Reuse `GET /api/ConsultGenerationJobs/{jobId}` for full job detail when a user selects a job.

## Storage Design

### SSE Event Log

Use Azure Table Storage for the resumable event stream:

```text
Table: ConsultGenerationJobEvents
PartitionKey = JobId
RowKey       = zero-padded sequence number
AppUserId
EventType
EventKey
PayloadChunkCount        (#1051; absent on rows written before)
PayloadJson              (chunk 0 — the whole payload on a row written before #1051)
PayloadJson1 … PayloadJson15   (chunks of 30 000 UTF-16 characters, never split inside a surrogate pair)
CreatedAtUtc
```

A property holds 32K UTF-16 characters at most; the row's own cap is sixteen chunks (480 000
characters, ≈960KB under the 1MB entity limit), past which the store refuses by name — and the SSE
path then sends that response's events unpersisted (see the amendment above). The event row is a
dictionary entity; the `!sequence` and `!event-key` rows keep their typed shapes.

Example row key:

```text
000000000012
```

The SSE `id:` value should combine the job ID and sequence number:

```text
04c45d68053c4b75b5db57e04b977e01:000000000012
```

The implementation also stores metadata rows in the same partition for idempotency:

```text
RowKey = !sequence
RowKey = !event-key:{sha256(EventKey)}
```

`EventKey` values are deterministic semantic keys such as `snapshot`, `analysis:concepts-extracted`, `item-step:hpi:section-standard-draft-created`, `block-completed:hpi`, `error:runtime-failed`, and `done`.

### Profile Job Index

Use a separate user-indexed table for job history:

```text
Table: ConsultGenerationJobIndex
PartitionKey = AppUserId
RowKey       = reverseTimestamp_jobId
JobId
Status
CreatedAtUtc
StartedAtUtc
CompletedAtUtc
SectionCount
CompletedBlockCount
FailedBlockCount
```

Use reverse timestamp row keys so newest jobs list first.

Keep this table metadata-only by default. Do not store consult draft text, generated note text, or clinical preview text in the index table unless there is a separate product and privacy decision.

## Client Behavior

- Open the SSE stream only after job creation succeeds with `202 Accepted` and a job ID.
- Track the latest received event ID.
- If the stream drops, reconnect with `Last-Event-ID`.
- If resume fails or times out, fall back to polling.
- Polling remains responsible for confirming terminal state.

## Security

- Never trust `Last-Event-ID` for authorization.
- Always authorize with the bearer token and server-side job ownership checks.
- Reject or ignore `Last-Event-ID` values for a different job ID.
- Do not expose full bearer tokens in diagnostics.
- Do not accept client-supplied user IDs for job history queries; always use the authenticated `AppUserId`.

## Acceptance Criteria

- Live SSE still works without resume.
- Event IDs appear in the browser Network tab.
- A dropped stream can reconnect and replay missed semantic events.
- Polling still reaches final state if SSE or resume fails.
- Unauthorized users cannot stream, replay, poll, or list another user's jobs.
- Profile can list the current user's recent jobs without scanning Durable entities.

## Assumptions

- Azure Table Storage is the preferred event log and job index storage.
- Durable job state remains the authoritative current job state.
- Polling remains the final fallback and source of truth for terminal state.
- Resume is implemented incrementally, not as a rewrite of the current working SSE pipeline.
