# specVersion 29 — the dynamic half: a count the run discovers

> **Status:** design, **revision 1** (2026-11-05). Milestone 32 (#1277). Decided with the package
> author; not yet implemented. Design-level, not a step-by-step plan — the Milestone 32 issues are
> its sections and take their decisions from it; where a comment on one of them disagrees with this
> text, this text is the decision and names what it supersedes. The normative text follows in the
> package-format registry as `package-format-v29.md` once layer A lands (§ 11).
>
> **One-line model:** from 25 to 28 a loop's length is known when the job starts. 29 lets a loop
> iterate **a node's value** — a list a node returned — so that loop's count is known only when the
> node completes; the job's total becomes **per-loop**, a loop that has not settled is a named
> state (*not yet known*, never zero), and the entity records the moment it settles. A folder
> item's `if` may read a node too, but its item stays a row (*skipped by condition*, as at 27), so
> that count does not move. Loops stay one level deep; nesting, `while` and `let` are named for 30.

The dynamic half was named when 26 was decided (v26 design § 6, 2026-10-24: "the invariant 25
and 26 share — a loop's length is known when the job starts"), kept out of 27 by decision ("29
is where a count moves during a run", v27 design, Why) and out of 28, which shipped the file-name
rule alone (v28 design, Status). Milestone 32 split #1062's original ask into the scheduler core
(#1062) and five shapes (#1278–#1282) on 2026-11-02; this document decides which of them 29 is.

## Why

**A package wants to draft what the case contains, not what the author listed.** At 28 a loop
iterates a snippet folder (sections the author wrote) or an array input (rows the clinician
typed). A package that first reads the documents and *finds* the problems, the medications or the
sections worth writing — and then drafts one part per finding — cannot be spelled: the list is a
node's output, and a loop over it is refused (*"a loop over a node's value waits for the scheduler
redesign"*, `WorkflowFlowNodes.cs:408`).

**A folder item wants to be gated on a judgment.** At 28 a folder item's `if` reads inputs and
`item.id` only (`WorkflowPackageValidator.V26.cs:797`), "so the folder's size is known at job
start". A *Billing* section drafted only when a classifier judged the case in scope is #1062's
original ask (#1279).

**Why the scheduler has to change for one and not the other.** Everything downstream of the
starter rests on one number fixed at start: `WorkflowPackageBlocks.Resolve`
(`Api/Workflow/WorkflowPackageBlocks.cs:22`) lays out every block, `TotalBlockCount` is written
once (`ConsultGenerationState.cs:184`, `:1750`; never derived, #176), and the record's
`collections` rosters, the SSE `snapshot`'s total and the Create page's progress bar all read it.
A loop over a node's value has no blocks until that node completes. A gated folder item, decided
as 27 decides an inner `if` — a row that may be *skipped by condition* — keeps its block, so only
the first needs the count to move (§ 3).

## The port list

| at 28 | at 29 | § |
|---|---|---|
| a `for` iterates `snippet.<folder>` or `input.<id>`; `node.<id>.value` is refused naming the redesign | a `for` may iterate **`node.<id>.value`** (or a field path under it) when the node's format types that value as an array | § 2 |
| every loop's count is known at start; `TotalBlockCount` is one scalar written once | a loop over a node's value starts **unresolved**; the job's total is the settled loops' blocks plus a named *not yet known* state; the count settles once, when the source node completes | § 1 |
| no event says a count changed | a **`count-resolved`** entity operation and SSE event (*"Problems: 4 items"*) | § 1 |
| a folder item's `if` reads inputs and `item.id` only; the starter drops an excluded item | it may also read a scalar node's `.value`; such an item stays a row and is decided when the node completes — *skipped by condition*, the count kept | § 3 |
| a deliverable's row is written at release; "produced" means composed (#1155) | at 29, a row is written **at composition** (`MarkDeliverableComposed`), *composed, held on its check* is a named state, and "produced" means **released** | § 4 |
| record storage 9 | record storage **10**: an unresolved loop row, the resolution, the empty-list reason, the composed deliverable row | § 5 |
| no way to stop new jobs for a deploy | a hold switch (`Jobs__HoldNewJobs`) and a drain step in the release runbook | § 7 |
| `package-format v2026.11.1`, `provenance v2026.10.10` | the bump's package-format version; a **provenance** bump (storage 10) | § 10 |

**Unchanged from 28, by decision:** everything else. Loops are one level deep (a `for` inside a
`for` is still refused, `WorkflowFlowNodes.cs:294`); block and row ids keep their form
(`result:loop:item`, `node:item` — no paths); the template objects, headers, file-name rule,
formats and catalogue; the node and composed hash ladders (§ 5); the orchestrator's, entity's and
activities' names (§ 7). Jobs at 26–28 run exactly as today — every change in §§ 1, 3, 4 is gated
on the job's package being 29.

## 1. The scheduler core (#1062)

### 1.1 Per-loop totals

At start, the starter (`ResolveSkeleton`, `ConsultGenerationJobStarter.cs:1395`) lays out every
block it can: the blocks of loops over folders and inputs, and every non-loop source, exactly as
at 28. A loop over a node's value contributes **no blocks** and is marked **unresolved** on the
entity — a list of loop ids whose counts are not yet known (`UnresolvedLoops`, trailing on the
initialize payload).

The job's total is then **the blocks laid out so far**, and the job is *not yet known* while any
loop is unresolved. The precedent is the v10 deciding job (`decidedAtUtc`, `provenance-record.md`:
*"a count that is not yet known, not a count of zero"*): the response says the count is pending,
the Create page shows it (§ 1.4), and nothing reads a provisional total as final.

`TotalBlockCount` stays the stored scalar #176 made it — it is **re-written once per loop
resolution**, by the entity operation below, never derived from the size of a dictionary. A job
with no node-valued loop writes it once, as at 28.

### 1.2 The count-resolved operation

When a loop's source node completes, the orchestrator reads its recorded output, takes the list
(§ 2.2), and calls one entity operation, **`ResolveLoop`** (a new method on the existing entity;
the payload `ConsultGenerationLoopResolution { LoopId, Items[{Id, Name}], ResolvedAtUtc, Reason? }`):

- appends the loop's blocks (one per item per deliverable sourcing the loop, ids
  `result:loop:item` as `ResolveSourceBlocks` makes them, `WorkflowPackageBlocks.cs:158`);
- appends the loop's roster to `collections` (`ConsultCollectionRoster`, as `Decide` writes one,
  `ConsultGenerationState.cs:83`);
- re-writes `TotalBlockCount` and removes the loop from `UnresolvedLoops`;
- writes the loop row's `count` and `resolvedAtUtc` (§ 5).

The operation is idempotent per loop (a replayed call finds the loop resolved and does nothing),
and the list is read in the orchestrator from the **recorded** output, so replay is deterministic.
A loop resolves at most once; nothing at 29 changes a count after it settles.

The loop's body nodes are scheduled for the new items by the existing per-item path
(`StartReadyInstances`, `ConsultGenerationEngine.cs:425`, reading `FanItems`, `:209`) — the
change is where `FanItems` finds a resolved loop's items (the resolution, not `input.Collections`).

### 1.3 The SSE event

The stream carries a new event, **`count-resolved`**, with `{ loopId, count, total, pending }`
(`pending`: loops still unresolved). Its event key is `count-resolved:{loopId}`.

A second `snapshot` cannot do this: the event store keeps one event per key and returns an
existing one (`ConsultGenerationJobEventStore.cs:149`), and the snapshot's key is the constant
`"snapshot"` — a re-sent snapshot with the new total would never reach the client. A key per loop
fits both the store and resume (`Last-Event-ID`, `docs/SSE_RESUME.md`): a resumed stream replays
each resolution once. `docs/CONSULT_GENERATION_EVENTS.md`'s event contract gains the row.

### 1.4 The Create page

Progress re-totals on `count-resolved` (and on any snapshot or poll, from the response's total and
pending loops). While a loop is unresolved the bar names it — *"Problems: counting…"* — and the
total reads as a floor, not a number (*"3 of 5+"*), never as final. The page stops pre-totalling
from the Current endpoint's blocks before the job id exists (`Consults.razor:2119`), which is
already wrong at 26+ for input fans; it waits for the snapshot.

History and the record panel read the resolved roster as they read any roster; an unresolved loop
in a job that failed before resolving shows as *count never known* — a named state, not an empty
list.

### 1.5 What does not move

Row and block ids, the `node-completed` / `item-step` / `block-*` events, the per-item scheduling,
the deliverable composition (the loop's items are its items once resolved), and every job whose
package is below 29.

## 2. A `for` over a node's value (#1278)

### 2.1 The shape

```json
{ "id": "problems", "for": "item in node.find_problems.value", "nodes": ["draft_problem"], "ret": "draft_problem" }
```

- The source is **`node.<id>.value`**, or a field path under it (`node.extract.value.problems`).
- The node is a **plain node outside any loop**, upstream of the loop (the reference is the edge;
  `WorkflowDerivedEdges` draws it as it draws any read).
- Its format types the value at that path as an **array** (`type: "array"`, `items` an object or
  a string schema — the format catalogue's JSON schema subset). A `concept_list` is not iterable at
  29 (§ 12).
- The loop is one level deep, as every loop is at 29.

### 2.2 The items

- **The item id is the 1-based position**: `1`, `2`, … — a node's list has no ids of its own, and
  a position is stable for one run. Rows are `draft_problem:1`, blocks `result:problems:1`.
- **`item.value`** is the element; `item.value.<field>` reads a field of an object element, and the
  walker types it from the format's `items` schema. `item.id` is the position.
- **The item's name** (rows, headings, progress) is the loop's label plus the position
  (*"Problems 1"*), as an array input's elements are named today. Naming an item by one of its
  fields is #1252's open question (§ 12); 29 does not decide it.

### 2.3 An empty list

The node returned `[]`: the loop resolves with **zero items**. Its row says so —
`reason: no items: node.find_problems.value was empty` — and the job does not fail. What reads the
loop sees what 27 gives a loop whose every item was skipped (v27 design § 1.4): `.content` is
empty, `.value` is `[]`, and a deliverable sourcing it composes **a document with no section**, not
a skipped document (the loop ran; it returned nothing).

A source node that **failed** or was **skipped** leaves the loop *count never known*: its body does
not run, and a deliverable sourcing it fails with a sentence naming the source node, as a
deliverable whose source failed does today.

### 2.4 What is recorded

The loop row's `count`, `resolvedAtUtc`, and the per-item rows and output hashes, as for any loop.
**Never the element values** — #369's rule: the record holds what the run produced and its hashes,
not a patient value copied out of a node's output. The source node's own row already carries its
output hash, which covers the list.

## 3. A folder item's `if` that reads a node (#1279)

### 3.1 What it may read

At 29 a folder item's `if` (in the item file's header) may read, beyond inputs and `item.id`,
**a scalar node's `.value`** — the 25 condition scope (`{ "label": "Billing", "if":
"node.scope.value == \"in_scope\"" }`). The node must be outside any loop, and not the loop over
this folder or its body (*"a condition decides before the loop runs"*, the 26 rule). `.content`,
`snippet.*`, `macro.*`, `run.*` and `profile.*` stay refused, by the 26 sentence.

### 3.2 When it is decided, and what an excluded item is

An item whose `if` reads inputs only is decided at start and dropped by the starter, **as at 28**
(`SettledFolders`, starter `:1364`) — unchanged, so no 28 package's count moves.

An item whose `if` reads a node **stays in the folder's item set**: it is a block and a row from
the start, so the count is known at start. It is decided **when the node completes**, per item,
through the 27 per-item decision (`decide-flow-item`, `StartDecision`, `ConsultGenerationEngine.cs:404`).
When the condition does not hold, the item is **skipped by condition** — the 27 named state, with
the condition's text and never the value it read (v27 design § 1.4, § 2): the block is `Skipped`,
streamed as `block-skipped`, counted among the completed blocks.

This is the decision taken with the author on 2026-11-05 over a smaller count: the skipped row
keeps the item visible on the record and keeps #1279 out of the scheduler core (§ 1). A loop over
the folder sees the item as 27's loop sees a skipped item (v27 design § 1.4's table).

### 3.3 A folder read without a loop

A template that reads the folder directly (`{{ for s in snippet.sections }}`) or a node that reads
`snippet.sections` gains an **edge on the gating node**: it runs after the node completes and sees
only the items whose `if` holds — as a template sees a settled folder today.

## 4. The deliverable at 29 (#1194)

Both #1155 decisions are changed **at 29 only** (decided 2026-11-05; 26–28 are untouched).

- **The row at composition.** When a deliverable's sources settle and `TryCompose`
  (`ConsultDeliverableComposition.cs`) composes it, the engine calls a new entity operation,
  **`MarkDeliverableComposed`**, writing the document row with `ComposedHash` and `Sources` and no
  text. The release (`CompleteResultDocument`) fills in the text. Between the two, the row's state
  is **composed, held on its check** — a named state (the "tell the user early" rule), so a
  deliverable held by an unsettled read or a pending check has a row to point at. #1155's option 1
  (the hash stamped at release) is superseded at 29.
- **"Produced" means released.** `ConsultDeliverables.FinalOutcome` at 29 counts a deliverable
  produced when its document was recorded or failed its check. A deliverable composed but never
  released is no longer the reply leg's thrown fault (`ReplyDocumentsFor`): the outcome names it
  and why it was held. #1155's option 1 ("composed, 25's meaning") is superseded at 29.

## 5. The record (storage 10)

**`nodes`.** A loop row over a node's value carries `source: node.<id>.value…` (the reference as
written), `count` **null until resolved**, `resolvedAtUtc`, and — for an empty list or a source that
never completed — a `reason`. A folder item's skipped row is 27's (*skipped by condition*, a
`reason` naming the condition's text).

**Job fields.** `unresolvedLoops` (the loop ids not yet settled; empty when every count is known).
`totalBlockCount`'s description loses "fixed when the job starts and never changes"
(`provenance-record.md`, line 50): at 10 it is *the blocks laid out so far; final when
`unresolvedLoops` is empty*.

**Documents.** A row may exist before its text: `composedAtUtc`, `composedHash`, `sources`, and the
state *composed, held on its check* until release (§ 4).

**No hash ladder moves.** The source node's output hash already covers the list; a resolved loop's
aggregate input hash is over its items' output hashes in item order (the 25 ladder); the composed
hash is computed as at 26, only written earlier. `NodeHashVersion` and the composed ladder are
unchanged.

**Why 10.** A field that changes meaning (`totalBlockCount`), a row that can be incomplete
(`count: null`), a document row without text — a reader at 9 would read a provisional total as
final and a composed row as a released one. The provenance registry bumps
(`recordStorageVersions` gains 10; `provenance-record.md`'s job fields, `nodes` and documents).
The positional `ConsultGenerationJobInitialize` gains one trailing field (`UnresolvedLoops`), so
`DurablePayloadReplayTests` gains a stored-JSON case for a 28 payload read by the 29 code (the v14
lesson).

## 6. The validator's sentences (new at 29)

Proposed; the subjects follow the 26 idiom.

**Narrowed.** *"Loop 'x' iterates node.y.value; a loop over a node's value waits for the scheduler
redesign — iterate an input or a snippet folder."* is said **below 29** only, as the gate:
*"Loop 'problems' iterates node.find_problems.value, which requires specVersion 29."* The nesting
sentence (`WorkflowFlowNodes.cs:294`) stays at 29.

**New at 29:**
- *"Loop 'problems' iterates node.find_problems.value; find_problems returns prose — a loop
  iterates a value its format types as an array."*
- *"Loop 'problems' iterates node.find_problems.value.summary, which find_problems's format
  types as a string, not an array."*
- *"Loop 'problems' iterates node.draft_problem.value; draft_problem runs inside loop 'problems'
  — a loop iterates a node outside every loop."* (and the same for any loop's body)
- *"Loop 'problems' iterates node.find_problems.value, and find_problems reads node.problems — a
  loop's source runs before the loop."* (the cycle, said by name before the generic cycle sentence)
- *"Folder 'sections' item 'billing' if reads node.scope.content; a folder item's if reads a
  node's .value (a scalar), inputs and item.id."*
- *"Folder 'sections' item 'billing' if reads node.sections; a condition decides before the loop
  runs — read a scalar node."*
- Below 29: *"Folder 'sections' item 'billing' if reads node.scope.value, which requires
  specVersion 29."* (the 28 sentence at `WorkflowPackageValidator.V26.cs:797` is kept for the
  other reads).

**No warnings are added at 29.** That a loop's count is known only when its source completes is
a run fact the Create page says (§ 1.4), not something wrong with the package.

## 7. The engine

- **The starter** (`ResolveSkeleton`) lays out what it can (§ 1.1) and passes `UnresolvedLoops`;
  `SettledFolders` keeps dropping input-gated items and keeps node-gated ones (§ 3.2). An array
  input with no rows still fails the job at start (`:785`); a node's empty list does not (§ 2.3).
- **The orchestrator** resolves a loop when its source node's completion is recorded (§ 1.2), then
  schedules its body through the existing per-item path; a gated folder item is decided through
  the 27 per-item decision (§ 3.2).
- **The deliverable** — `MarkDeliverableComposed` and the 29 `FinalOutcome` (§ 4).
- **The Durable contract.** The orchestrator (`ConsultGenerationOrchestrator`), the entity
  (`ConsultGenerationJobEntity`) and the activity names stay; payloads only gain trailing optional
  fields, which bind null when a 28 job replays, as every change since #215 has. The new entity
  operations (`ResolveLoop`, `MarkDeliverableComposed`) are new methods, never renamed ones.
- **Draining before the release** (decided 2026-11-05). A new app setting,
  **`Jobs__HoldNewJobs`**, makes the starter refuse a new job with a named sentence (*"New consults
  are paused for an update; try again in a few minutes."*) — interactive, email and scheduled
  intake alike — while jobs already running finish. `docs/DURABLE_JOBS.md` gains the runbook:
  set the hold; wait until no job is running and no scheduled job is due inside the window (a
  scheduled job can sleep up to seven days, so its wake time is checked, not assumed); cut the
  release; verify; clear the hold. Append-only payloads stay the second line of defence for a job
  the drain missed.

## 8. The Editor at 29

- **The loop pane**: the iterate picker offers a node's value (and its array fields) beside inputs
  and snippet folders, listing only nodes outside every loop whose format types an array there;
  the body's palette offers `item.value.<field>` from the format's `items` schema.
- **The folder item's gate**: the item-header `if` palette offers scalar nodes' `.value`; the
  preview judges the gate over a picked value, as the 26 previews judge a branch.
- **The Graph pane** draws the source node's edge to the loop and the gating node's edge to a
  folder read (both come from `WorkflowDerivedEdges`).
- **The upgrade rung**: `MigrateToNewestAsync` gains 28 → 29, a version-only step (§ 9), with the
  notice *"Through 29: nothing changes in this package; loops may now iterate a node's value."*

## 9. Migration 28 → 29

**None.** 29 only permits what 28 refused (the v27 § 7 model); a valid 28 package is a valid 29
package with the version changed. `scripts/migrate-workflow-package.cs --to 29` bumps the version
and validates at 29.

## 10. The registry

- `package-format-v29.md` and the generated schema (the `for` source grammar, the item header's
  `if`).
- `conformance/v29/`: **valid** — a loop over a node's array value, over a field path, an empty
  list's package (valid; emptiness is a run fact), a node-gated folder item; **invalid,
  schema-catchable** — none expected (the grammar is a string); **invalid, `SHAPE_BLIND`** — a
  prose source, a string field, a source inside a loop, a source downstream of the loop, an item
  `if` reading `.content`, an item `if` reading the loop; **warning** — none.
- The gate under `v28/`: a loop over a node's value at 28, a node-gated item at 28.
- The provenance registry: `recordStorageVersions` gains 10; `provenance-record.md` for § 5.

## 11. Phasing (Milestone 32)

One layer per PR, each green on its own; each gets its own issue when it starts. The bump is last.

- **A — format, inert.** The validator at 29 behind the ceiling (`AcceptedSpecVersions` stays 28),
  the walker's types for `item.value`, the derived edges, the v29 vectors held back.
- **B — engine.** The scheduler core (§ 1), the folder-item decision (§ 3), the deliverable at 29
  (§ 4), storage 10 (§ 5), the hold switch and the runbook (§ 7), the Create page and History
  (§ 1.4).
- **C — Editor.** § 8.
- **The bump.** The registry PR (`package-format-v29.md`, vectors, provenance storage 10) and the
  engine PR moving every ceiling to 29; a release, cut **after a drain** (§ 7).
- **E — content.** An example package whose loop iterates a node's list (a problem list drafted
  one part per problem), and a 29 smoke job.

## 12. Deferred and open

**Named for 30** (Milestone 38, decided 2026-11-05) — each still refused at 29 by its 26 sentence:

- **A `for` inside a `for` (#1280).** Rows and blocks take paths (`sections/hpi/paragraphs/2`);
  one total per loop *instance*; SSE ids, the event store's keys, History and the run diagram carry
  the path. Open: what an inner loop iterates (the outer item's field, a folder, a node's value),
  reads across levels, the depth limit.
- **`while` and `for … until` (#1281).** A pass count that grows or shrinks; a required bound
  whose reaching is a named state; a row per pass or the last only; the relation to
  `onInvalid: retry` (#1100).
- **`let` (#1282).** Most likely a node with no model call (as a computed format is), recorded and
  hashed like any node's output; its set of reductions is open.

**Not at 29:**
- **Iterating a `concept_list`** — one item per concept; with the format catalogue's concept
  shape settled, a later bump.
- **Naming an item by its field (#1252)** — a declared `labelField` or a `title` convention;
  decided later, for array inputs and node lists together. Until then both are named label plus
  position.
- **Conditions on run facts** (`run.date`) in a folder item's `if` — out of scope since #1062.
