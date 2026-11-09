# specVersion 30 — nesting, while and let: a loop's shape the run decides

> **Status:** design, **revision 1** (2026-11-08). Milestone 38 (#1337). Decided with the package
> author on 2026-11-08: all three shapes v29 named for 30, at any depth, plus #1252. Nothing is
> built yet. This is a design, not a step-by-step plan: the Milestone 38 issues are its sections
> and take their decisions from it. Where a comment on one of them disagrees with this text, this
> text is the decision and names what it supersedes. Where this text proposes a *spelling* (a key,
> an object, a sentence's words) rather than a decision, it says so; layer A settles the spelling
> and records it here as a revision. The normative text follows in the package-format registry as
> `package-format-v30.md` with the bump (§ 14).
>
> **One-line model:** at 29 a loop is one level deep and runs each item once. 30 makes a loop's
> **shape** something the run can decide:
> - a loop may sit **inside a loop, to any depth**, so each item is named by a **path**, and an
>   inner loop has one count per outer item;
> - a **`while`** repeats its body until a check stops holding, within a declared bound, and
>   records every pass;
> - a **`for … until`** stops early, running its items in order;
> - a **`let`** computes a typed value with no model call.
>
> An item may be named by one of its own fields (`labelField`). Enum value labels and the
> renderer-pipe question (#1189) are not in 30.

v29 built the scheduler core these shapes need: per-loop totals, a count that settles mid-run,
the `count-resolved` event and record storage 10 (v29 design § 1). It named the three shapes for
30 (v29 design § 12, decided 2026-11-05) and kept each refused at 29 by its 26 sentence. #1252
(an item named by its field) was moved to Milestone 38 on 2026-11-08. The `while` and `let`
shapes were first sketched on #1100 (closed); nesting was #1062's "fan of fans", the goal of the
dynamic half (v26 design § 6).

## Why

**A package wants to draft the parts of a part.** At 29 a loop over a node's value drafts one
section per problem the case contains, but a section cannot then fan out over *its* parts: its
paragraphs, its outline points, the documents relevant to it. The shape is refused (*"Loop
'paragraphs' runs inside loop 'sections'; nesting waits for the scheduler redesign."*,
`WorkflowFlowNodes.cs:294`). The most useful case is "decide, then fan" per section: outline
each section, then write one paragraph per outline point. That is an inner loop over the value
of a node in the outer loop's body, whose count is known only when that node completes for that
section.

**A package wants to redraft until a judge is satisfied.** At 29 the nearest is `onInvalid:
retry`: one repair message, once, and only for a format that failed to validate
(`WorkflowPackageManifest.cs:1159`). Draft, judge, redraft (iterative refinement) needs a loop
whose pass count the run decides, with a bound, and a record of every pass so a reader can see
what each redraft changed.

**A package wants to stop reading once it has what it needs.** "Read the documents in order
until one is marked final" is a loop that stops early; at 29 every item runs.

**A package wants a value it does not need a model for.** A count of problems, a total score,
"over 65 and urgent": computed from inputs and node values, typed, recorded and hashed. At 29 it
needs a model call or nothing.

**Why one bump.** The three share the machinery v29 deferred:
- a count that changes after it first settles (an inner loop per outer item, a `while`'s passes,
  an `until`'s early stop);
- ids that carry more than one item (paths);
- a record that names how a loop ended.

Building them in one milestone builds that machinery once.

## The port list

| at 29 | at 30 | § |
|---|---|---|
| a loop's body holds plain nodes and `if`/`case`; a `for` in a `for` is refused | a `for` may sit **inside a `for`, `while` or branch, to any depth** | § 2 |
| one item id per instance (`node:item`, `result:loop:item`) | an **item path**: the item ids from the outermost loop inward, joined by `/`; one level reads exactly as at 29 | § 1 |
| a loop has one count, settled at most once | an inner loop has **one count per outer item** (a loop *instance*), each settling on its own | § 2 |
| `item` is the one loop's item | `item` is the innermost loop's item; **`item.parent`** reaches outward | § 2 |
| no `while`; `onInvalid: retry` is the one repeat | a **`while`** node: a body repeated while a check holds, `max` required (1–10), a row per pass | § 3 |
| every item of a `for` runs | a `for` may declare **`until`**: its items run in order, and it stops after the item where the check holds | § 3 |
| no computed node | a **`let`** node: one Scriban expression with a declared type, no model call, recorded and hashed | § 4 |
| an array item is named label plus position (*"Sections to produce 1"*) | an `array<object>` input or a node-value loop may declare **`labelField`** | § 5 |
| record storage 10 | record storage **11**: paths, loop-instance rows, pass rows, the stop reasons, the `let` row | § 7 |
| `package-format v2026.11.2`, `provenance v2026.11.1` | the bump's package-format version; a **provenance** bump (storage 11) | § 13 |

**Unchanged from 29, by decision:**
- the template objects, headers, file-name rule, formats and catalogue;
- the renderer pipe takes the node, as built at 26 (v26 design § 7.1); #1189 is not reopened;
- `onInvalid: retry` stays the one-rung key (#1100);
- the node and composed hash ladders;
- the names of the orchestrator, the entity and the activities (§ 9).

Jobs at 26–29 run exactly as today: every change in §§ 1–5 is gated on the job's package being 30.

## 1. Paths

### 1.1 The grammar

**An item path is the item ids from the outermost loop inward, joined by `/`.** The loop ids are
not in the path: a node's chain of enclosing loops is fixed by the manifest, so the path's
segments are matched to that chain by position.

- **At one level a path is the item id**, so every 29 key reads unchanged: `draft_problem:2`,
  `note:problems:2`.
- **Deeper**, a paragraph node under `sections`, then `paragraphs`:
  - its row is `draft_paragraph:hpi/2`;
  - its block is `note:paragraphs:hpi/2`. The block names the innermost loop that feeds the
    deliverable, as at 29.
- **Where a reader sees it** (the run rail, History, the run diagram, `block-skipped` labels),
  the path is shown with its loop ids interleaved: `sections/hpi/paragraphs/2`. That is the
  display form #1062 named.

**Item ids keep their source's form.** Array inputs are 0-based (`WorkflowInputFans.cs:48`),
node values 1-based (`ConsultNodeValueLoops.cs:97`) and folder items their file stems, as
recorded today. Changing either base would rewrite stored records.

**`/` cannot collide.**
- Every id is `^[a-z][a-z0-9_]*` (the v28 file-name rule) or a position of digits, so no item id
  contains `/` or `:`.
- The folder gate's internal key (`"{folder}/{itemId}"`, `ConsultGenerationEngine.cs:193`) lives
  in its own dictionary and never meets a path.

### 1.2 Where a path replaces the single item id

Every structure that carries one item id carries a path at 30:

- **Scheduling:**
  - `ConsultNodeScheduler.InstanceKey` (`ConsultGenerationEngine.cs:2116`) and the readiness
    alignment beside it. A dependency is aligned on the **common prefix** of the two nodes'
    paths.
  - `pendingTasks` and `pendingDecisions` (`ConsultGenerationEngine.cs:188`).
  - `FanItems` (`:225`). An inner loop's items are looked up by **loop instance** (loop id and
    outer path), not by collection id; § 2.3.
- **Ids:**
  - block ids (`WorkflowPackageBlocks.cs:164`, `ConsultGenerationEngine.cs:751`);
  - `NodeOutputs` and `SkippedBlocks` keys (`MarkNodeItemSkipped`,
    `ConsultGenerationState.cs:635`);
  - `ItemProgress`, which is keyed by a bare item id today and would collide across sections.
- **Events:** the `item-step` key and its parse, which splits at the *last* `:`
  (`ConsultGenerationTransport.cs:2323`). That still works, because a path has no `:`. The
  `block-*` keys, and `count-resolved`, which becomes per instance (§ 2.3).
- **The Web:** `RailSkips.LoopItemKeyOf` (`RailSkips.cs:12`), `RecordFlow.LoopOf` and
  `SkippedItemsOf` (`RecordFlow.cs:36`, `:64`), and the manifest reader's `LoopOf`.

**The depth limits to lift.** Each of these walks a fixed number of hops:
- `WorkflowFlowNodes.LoopOf` (`:80`) stops at depth 2 and returns the innermost loop only;
- the Web `LoopOf` (two hops);
- `AwaitsCount` (`ConsultGenerationEngine.cs:245`, three hops).

At 30 each becomes a walk to the root that returns the **chain** of enclosing loops.

## 2. Nesting (#1280)

### 2.1 The shape

A `for`, `while` or `if`/`case` may be listed in a `for`'s `nodes`, and a `for` in a `while`'s or
a branch's, **to any depth** (decided). The 29 rule that a branch holds plain nodes
(`WorkflowFlowNodes.cs:302`) is lifted with it.

```json
{ "id": "sections", "for": "item in node.plan.value.sections", "nodes": ["outline", "paragraphs"], "ret": "node.paragraphs" },
{ "id": "outline", "format": "outline" },
{ "id": "paragraphs", "for": "item in node.outline.value.points", "nodes": ["draft_paragraph"], "ret": "node.draft_paragraph" },
{ "id": "draft_paragraph", "format": "prose" }
```

### 2.2 What an inner loop iterates

**Everything a loop may iterate, plus what exists only inside the outer loop** (decided):

| source | example | its count is known |
|---|---|---|
| a snippet folder | `item in snippet.subsections` | at start (the same list for every outer item) |
| an array input | `item in input.questions` | at start (the same list) |
| a top-level node's value | `item in node.plan.value.themes` | when that node completes, once |
| **the outer item's list field** | `item in item.value.paragraphs` | when the outer item is known |
| **a node in the outer body** | `item in node.outline.value.points` | when that node completes **for that outer item** |

**What the inner `for` expression reads.** It names the *outer* item as `item`, because the inner
loop's own item does not exist yet; `item.value.paragraphs` reads a field of the section being
drafted. A node in the outer body is aligned by the outer path (`node.outline` is *this
section's* outline). v29's sentence *"a loop iterates a node outside every loop"* (`WorkflowPackageValidator.V29.cs:39`)
is lifted to *"a loop iterates a node outside it, or in an enclosing loop's body"*.

### 2.3 One count per loop instance

A **loop instance** is an inner loop under one outer path: `paragraphs` under `hpi` is one
instance, and `paragraphs` under `plan` is another. At 30 each instance settles its own count.

- **The orchestrator** keeps `resolvedItems` by instance (loop id plus outer path) instead of by
  collection id. When a node in an outer body completes, `ResolveLoopsFedByAsync`
  (`ConsultGenerationEngine.cs:719`) resolves the instances it feeds *under that node's path*.
- **The entity:**
  - `ResolveLoop` (`ConsultGenerationState.cs:110`) takes the instance's path;
  - `UnresolvedLoops` lists instances, not loop ids;
  - an outer item that appears creates its inner instances unresolved. Every instance is
    unresolved until its outer item exists.
- **The total** is the settled instances' blocks plus a named *not yet known* state. That is
  v29's rule, now per instance.
- **The event:** `count-resolved` is keyed by instance (`count-resolved:{loopId}:{path}`).
  Its payload gains a trailing `Path` (null for a top-level loop), so 29 consumers read it
  unchanged.

### 2.4 Reads across levels

**`item` is the innermost loop's item; `item.parent` is the enclosing loop's**, and so on
outward (`item.parent.parent`). The spelling is proposed; layer A settles it.
- A body node reads its own loop's item as at 29.
- A paragraph's prompt reads its section's title as `item.parent.value.title`.
- **A node in an outer body** is read by id, aligned on the common prefix: `node.outline` from
  inside `paragraphs` is this section's outline.
- **A node in an inner body is a list** from outside it, read as a loop's elements are today
  (`{{ for e in node.paragraphs }}`).
- **Siblings across instances are not readable.** A paragraph in `hpi` does not read a paragraph
  in `plan`. Such a read is refused with a sentence naming both paths.

### 2.5 An empty inner list

As at 29 (v29 design § 2.3), an empty list settles at **0** with its reason, *for that instance*
(`no items: node.outline.value.points was empty`, under `hpi`). It is never *not yet known* and
never a failure. The outer item still completes; its deliverable part reads an empty list.

### 2.6 What is recorded

- One loop-instance row per instance, holding its path, count, settled moment and reason (§ 7).
- Item rows keyed by path.
- The record shows the shape the run took. As at 29, it never records the element values
  (#369's rule).

## 3. `while` and `for … until` (#1281)

### 3.1 `while`: the shape

A `while` node repeats its body: it runs once, then again for as long as its check holds, at most
`max` times. It returns the **last pass's** named node.

```json
{ "id": "refine", "label": "Refining the summary", "while": "node.judge.value.verdict != \"good\"", "max": 3,
  "nodes": ["draft", "judge"], "ret": "node.draft" }
```

- **`max` is required, 1 to 10** (decided; the engine's cap is 10). A missing `max` is refused,
  and a `max` above 10 is refused naming the cap. There is no default, so the bound is always the
  author's.
- **The check runs after each pass**, over that pass's nodes, so a `while` always runs at least
  once. The check is a condition, like an `if`'s, that reads nodes in the body.
- **Reaching the bound is a named state**, never a silent stop (decided): *"stopped at its bound
  of 3; the check never stopped holding"*. The run continues with the last pass's output.
  Whether a deliverable that read it should be held is not a `while` rule: a deliverable's
  `check` does that, as it does today.

### 3.2 What a pass reads, and what the outside reads

**Inside a pass, a body node reads its own pass** by id, as any node does. **The previous pass**
is `pass.previous` (null on the first pass). For example, a redraft reads the judge's last
verdict with `pass.previous.judge.value.feedback`. `pass.number` is 1-based. Reading
`pass.previous` adds no dependency edge inside a pass, so draft and judge do not form a cycle.
The spelling is proposed; layer A settles it.

**Outside the loop**, `node.refine` reads like a loop's returned node:
- `.content` and `.value` are the last pass's `ret`;
- `.passes` is the pass count;
- `.stopped` is `check` or `bound`.

The pipe takes the node (v26 design § 7.1): `node.refine | concept_bullets` pipes the returned
node, as for any flow node.

### 3.3 A row per pass

Every pass is recorded (decided). Body rows are keyed by pass, using the path grammar with the
pass number as the segment (`draft:2`, `judge:2`), so a nested `while` composes like a nested
`for`. Each pass's output is hashed like any node's. The `while`'s own row names `passes` and
`stopped` with its reason (§ 7).

**The count does not move the total.** Only the last pass's `ret` feeds a deliverable, so a
`while` adds one block however many passes it runs. The rail shows progress as *"pass 2 of at
most 3"*, not as blocks.

### 3.4 `for … until`

A `for` may declare `until`, a condition over the item's body nodes:

```json
{ "id": "documents", "for": "item in input.documents", "until": "node.classify.value.final == true",
  "nodes": ["classify", "extract"], "ret": "node.extract" }
```

- **Its items run in order, one at a time** (decided). The loop stops after the item where the
  check holds, so which items ran is the same on every run. Only this loop is serialized;
  everything else still runs in parallel.
- **The items it did not run stay rows**: *"not run: stopped early at item 3"*. Their blocks are
  skipped with that reason, as an item *skipped by condition* is at 27.
- **So the count does not shrink.** This supersedes the "a count that shrinks" wording of v29
  design § 12. The loop's count stays the list's length; what changes is how many items ran.
  Keeping the count keeps v29's rule that a settled count never moves. A `count-resolved` never
  fires twice.
- **In a nested loop** the order and the stop apply to that loop instance only.

### 3.5 `onInvalid: retry`

`onInvalid: retry` stays the one-rung key: one repair message, once, for a format that failed to
validate (#1100). A `while` is the general repeat. They do not interact: a body node with
`retry` retries inside its pass.

## 4. `let` (#1282)

### 4.1 The shape

A `let` node computes one typed value from one Scriban expression, with no model call (decided):

```json
{ "id": "problem_count", "label": "Counting the problems", "let": "node.find_problems.value.problems | array.size", "type": "number" }
```

- **`type`** is `text`, `number`, `boolean`, or a format id for an object. A list is a format
  whose top-level field is an array, as at 29 every format's value is an object. The value must
  match its type, or the node fails, naming the mismatch.
- **What it may read:** what a template may read at that place (inputs, nodes, `item` inside a
  loop, `pass` inside a `while`), resolved by the same reference walker. So a `let` inside a loop
  computes once per item.
- **Its typical use is a reduce over a loop's list** (`node.paragraphs.value | array.map
  "words" | array.sum`). A reduce over a dynamic list waits for the loop instance to settle, as
  any read of that list does.
- **Refused functions:** anything not deterministic (`date.now`, random). A `let` must give the
  same value on replay; the Durable orchestrator replays.

### 4.2 Recorded and hashed

The engine runs a `let` as a computed node: `RunComputedNodeActivity`
(`RunComputedNodeActivity.cs:44`) with a new operation, so no activity is renamed or added. Its
row records:
- the input hash, over the expression and the values it read;
- the value, as canonical JSON;
- the output hash;
- the producer, `computed`.

A deliverable or check that reads it is attested the same way as one that reads a model's node.
`onInvalid: fail` and `flag` apply. `retry` is refused, because there is no model to repair.

## 5. Item naming (#1252)

**An item may be named by one of its own fields** (decided: a declared key, not a convention).

```json
{ "id": "sections", "label": "Sections to produce", "type": "array", "items": "object",
  "fields": [{ "id": "title", "type": "text", "required": true }, …], "labelField": "title" }
```

- **On an `array<object>` input, `labelField`** names a field. It is validated to be a
  **required `text` field** of the element.
- **On a loop over a node's value**, the loop declares `labelField`, validated against the field
  the source's format types for the element.
- **A blank value falls back** to label plus position (*"Sections to produce 2"*), the 29 name.
- **Where the name reaches:**
  - `WorkflowInputFans.Items` (`WorkflowInputFans.cs:52`) and `ConsultNodeValueLoops.ItemsOf`
    (`ConsultNodeValueLoops.cs:101`) set `name` from the field;
  - from there it reaches the composed heading, the rail, History, the block names and the
    `block-skipped` labels with no other change (`WorkflowPackageBlocks.ItemName`, `:176`).
- **Folder items are unchanged**: they are named by their `label` header.
- **It lands with the 30 bump** (decided), not as an optional key at 29.

## 6. The deliverable at 30

- **A nested list composes as nested iteration:**
  `{{ for s in node.sections }}{{ for p in s.paragraphs }}{{ p.content }}{{ end }}{{ end }}`.
  The element of an outer loop (`s`) exposes each inner loop **by its id** (`s.paragraphs`), with
  the same element members 27 gave: `item`, `content`, `value` and `skipped`. The spelling is
  proposed; layer A settles it.
- **The pipe takes the node**, as built at 26: inside nested iteration it names the body node
  (`p.draft_paragraph | concept_bullets`). #1189 is not reopened.
- **A `while`** composes as its returned node (§ 3.2). **A `let`** is read as any node's value.
- **Not run.** An item *not run: stopped early* (§ 3.4) composes as a skipped element
  (`e.skipped`), as an item skipped by condition does at 27.

## 7. The record (storage 11)

A 30 job is stamped **record storage 11**:
- the ladder at `ConsultGenerationState.cs:44` gains `>= 30 ? 11`;
- `StorageVersions` (`:1653`) gains 11;
- the provenance registry's `recordStorageVersions` gains it.

What 11 adds over 10:
- **item keys are paths** (§ 1);
- **loop-instance rows**: one per inner instance, holding its loop id, outer path, count, settled
  moment and reason. A top-level loop keeps its 29 row (path null);
- **pass rows** for a `while` (§ 3.3), and the `while` row's `passes` and `stopped`;
- **the stopped-early state** of an unrun `until` item, and the item it stopped at;
- **the `let` row** (§ 4.2).

As at 29, the record holds hashes, ids, counts and reasons, **never element values** (#369's
rule). Storage 2–10 records read exactly as today.

## 8. The validator's sentences

**Today `while`, `until`, `let` and `labelField` fail as unmapped keys**:
`WorkflowPackageManifestJson.cs:30` disallows unknown members, so the author sees a JSON error,
not a reason.
- **Layer A** adds the keys to the model.
- **The bump** gates them below 30 with sentences in the 29 idiom, for example *"Loop 'refine' is
  a while, which requires specVersion 30."* and *"Input 'sections' declares labelField, which
  requires specVersion 30."*.
- **The nesting sentence** (`WorkflowFlowNodes.cs:294`) becomes the gate below 30: *"Loop
  'paragraphs' runs inside loop 'sections', which requires specVersion 30."*.

**New at 30** (wording proposed; layer A settles it and lists them here as built):
- **`while`:**
  - *"While 'refine' declares no max; a while must declare its bound (1 to 10)."*
  - *"While 'refine' declares max 12; the most is 10."*
- **`until`:** *"Loop 'documents' until reads node.summary, which is not in its body; until
  reads the item's own nodes."*
- **`let`:**
  - *"Let 'problem_count' calls date.now, which is not deterministic; a let must give the same
    value on replay."*
  - *"Let 'problem_count' declares type number; onInvalid retry needs a model — use fail or
    flag."*
- **Nesting:** *"Node 'draft_paragraph' in section 'plan' reads node.draft_paragraph of section
  'hpi'; a node reads its own instance and its enclosing ones."* This is § 2.4's sibling refusal,
  worded per path.
- **`labelField`:**
  - *"Input 'sections' labelField 'kind' names an enum field; labelField names a required text
    field."*
  - *"Loop 'problems' labelField 'title' is not a field of the elements of
    node.find_problems.value.problems."*

**Lifted at 30:**
- the nesting sentence;
- "a branch holds plain nodes" (`WorkflowFlowNodes.cs:302`);
- "a loop iterates a node outside every loop" (`WorkflowPackageValidator.V29.cs:39`), which is narrowed (§ 2.2).

## 9. The engine

- **The orchestrator stays one flat ready loop** (`ConsultGenerationEngine.cs:32`), with one
  activity per instance and no sub-orchestrations. Its keys become paths (§ 1.2), and
  `FanItems` reads by loop instance (§ 2.3).
- **Per-instance resolution:** `ResolveLoopsFedByAsync` (`:719`) resolves the instances a node
  feeds under its path. An outer item's appearance seeds its inner instances.
- **`while`:**
  - the scheduler starts pass *n + 1* when pass *n*'s nodes are settled and the check, evaluated
    as an `if` is today (`DecideFlowItem`), holds, below `max`;
  - each pass's instances are keyed by the pass segment.
- **`until`:** the loop's items start one at a time in list order. After each item, the check is
  decided; when it holds, the remaining items are marked *not run* in one entity call.
- **`let`:** `RunComputedNodeActivity` with a `let` operation (§ 4.2).
- **The Durable contract:**
  - no orchestrator, entity or activity is renamed (the `[Function]` strings are the deployed
    contract);
  - payloads only gain trailing optional fields (`Path`, `Pass`);
  - a 26–29 job's payloads serialize exactly as today.
- **Draining before the release**, as at 29 (v29 design § 7): set `Jobs__HoldNewJobs`, wait for
  `show-active-jobs.sh` to say *"Safe to release"*, cut the release, verify, then clear the hold.

## 10. The Create page and History

- **The run rail** indents by depth: an inner loop's group sits under its outer item's row, and
  its count is per instance (*"Paragraphs 3/5"* under *Assessment and plan*, *"counting…"* under a
  section still outlining).
- **History** draws the same tree from the record, with the loop-instance rows' counts and
  reasons.
- **A `while`** shows *"pass 2 of at most 3"* while running. Once done, it shows *"3 passes —
  stopped at its bound"* or *"2 passes — the check stopped holding"*, and each pass is
  expandable.
- **An `until` loop** shows its unrun items as *"not run: stopped early at item 3"*.
- **A `let`** is a row with its value's type and hash, like a computed node.
- **The run diagram** (`RunDagDiagram`) already defers edges until the outermost subgraph closes
  (#1321), so a loop subgraph inside a loop subgraph draws correctly. A `while` is a subgraph
  labelled with its bound, and a `let` is a node with a computed shape.
- **The Create page's `count-resolved` handler** keys `settledLoopCounts` by instance (#1329's
  map).

## 11. The Editor at 30

- **The loop pane** lists a loop inside a loop, offers *for / while / if / case* as a body
  member, and shows the inner loop's source choices (§ 2.2) with the outer item's fields offered
  by name.
- **A `while` pane:** the check (a condition editor, as an `if`'s), `max` (empty until chosen,
  publish blocked until it is set — no default), and `ret`.
- **`until`**: an optional condition on a `for`, with a note that it runs the items in order.
- **A `let` pane:** the expression, with the reference walker's diagnostics, and the type,
  chosen from text/number/boolean or a format.
- **`labelField`**: a picker on an `array<object>` input and on a node-value loop, offering the
  required text fields.
- **The Graph pane** draws nested subgraphs (the same builder as the run diagram).
- **The upgrade rung:** `MigrateToNewestAsync` gains 29 → 30, with the notice *"Through 30:
  nothing changes in this package; loops may now nest, repeat (while) or stop early (until), and
  a let computes a value."*.

## 12. Migration 29 → 30

**None.** 30 only permits what 29 refused (the v27 § 7 model). `scripts/migrate-workflow-package.cs
--to 30` bumps the version and validates at 30.

## 13. The registry

- **Documents:** `package-format-v30.md` (a full grammar, as v29 is) and its generated schema.
- **`conformance/v30/`**, split as at 29 into:
  - valid;
  - invalid, schema-catchable;
  - invalid, `SHAPE_BLIND`;
  - warning.

  It holds at least: a three-level nesting, an inner loop over an outer body node, a `while` at
  its bound, an `until` that stops early, a `let` reduce, `labelField` on both sources, and each
  § 8 refusal.
- **Gate vectors under `v29/`**, one per new key and one for nesting, each refused at 29.
- **The provenance registry:** `recordStorageVersions` gains 11.

## 14. Phasing (Milestone 38)

One layer per PR, each green on its own; each gets its own issue when it starts. The bump is
last.

- **A — the format, inert.** The model gains `while`, `max`, `until`, `let`, `type` and
  `labelField`. The validator at 30, with every § 8 sentence, sits behind the ceiling
  (`AcceptedSpecVersions` stays 29). The template walker gains `item.parent`, `pass` and nested
  element members. The spellings this text proposes are settled here and recorded as revision 2.
- **B — the engine**, in this order, each inert below 30:
  - **B1, paths at one level:** every key in § 1.2 goes through one path helper; a one-segment
    path equals today's id, so nothing changes.
  - **B2, nesting:** nested fan-out, per-instance resolution, the `count-resolved` path and
    storage 11's instance rows.
  - **B3, `while` and `until`:** pass scheduling and pass rows; in-order items and *not run*.
  - **B4, `let`:** the computed operation and its row.
  - **B5, `labelField`:** naming from the field, in both fan sources.
- **C — the pages:**
  - **C1:** the Create page and History (§ 10).
  - **C2:** the Editor (§ 11).
- **The bump.**
  - **Registry PRs:** package-format and provenance, with the vectors of § 13.
  - **An engine PR** moving every ceiling to 30.
  - **A release, cut after a drain** (§ 9).
- **E — content.**
  - An example package that nests (sections → outline → paragraphs), with a `while` refinement
    and a `let` count;
  - `labelField` in `example-conditional-sections`;
  - the ten public packages raised to 30 (`--to 30`);
  - a 30 smoke, then the pins moved, only on the word.

## 15. Deferred and open

**Not in 30 (decided 2026-11-08):**
- **Enum value labels** (#1014, #1061): `{ value, label }` entries and `input.<id>.label`, for a
  later bump.
- **The renderer pipe inside a loop** (#1189): it stays as built at 26, taking the node. Nesting
  and `while` apply that rule (§§ 3.2, 6); the question is reopened only on the evidence #1189
  names.

**Not at 30:**
- **Conditions on run facts** (`run.date`), out of scope since #1062.
- **Reading siblings across loop instances** (§ 2.4): a node reads its own instance and its
  enclosing ones.
- **A `while` whose check is the format's validity** remains `onInvalid: retry`'s one rung; a
  general repair loop is a `while` the author writes.

**Open for layer A** (proposed here, settled there):
- the spellings `item.parent`, `pass.previous`, `pass.number`, `.passes` and `.stopped`;
- the element members for an inner loop (`s.paragraphs`);
- the exact wording of § 8's sentences;
- whether `type` admits a bare `list` or always a format.
