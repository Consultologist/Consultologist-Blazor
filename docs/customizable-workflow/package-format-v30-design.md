# specVersion 30 — nesting, while and let: a loop's shape the run decides

> **Status:** design, **revision 2** (2026-11-09): **layer A is built** (#1339: PRs #1349, #1350,
> #1352 and A3), inert behind the ceiling. Revision 1 (2026-11-08) proposed spellings; layer A
> settled them, and the package author widened `while` on 2026-11-09. This revision records both:
> every spelling below is the built one, and § 8 lists the validator's sentences as built.
> Milestone 38 (#1337). Decided with the package author on 2026-11-08: all three shapes v29 named
> for 30, at any depth, plus #1252. This is a design, not a step-by-step plan: the Milestone 38
> issues are its sections and take their decisions from it. Where a comment on one of them
> disagrees with this text, this text is the decision and names what it supersedes. The normative
> text follows in the package-format registry as `package-format-v30.md` with the bump (§ 14).
>
> **Revision 3** (2026-11-11), decided with the package author while building B5 (#1344): an item
> named by its `labelField` carries that field's text in the record, kept past the retention
> sweep — the one exception to § 7's rule, stated in § 5 — and a name drawn from a field is capped
> at 120 characters.
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
| no `while`; `onInvalid: retry` is the one repeat | a **`while`** node: a body repeated while a check holds, `max` required (1–10), a row per pass; `checkFirst` (zero passes possible), an `until` beside the check, every earlier pass readable (`pass.previous`, `pass.history`) | § 3 |
| every item of a `for` runs | a `for` may declare **`until`** (its items run in order, and it stops after the item where the check holds) and **`max`** (the first N items); `.stopped` says why it ended | § 3 |
| no computed node | a **`let`** node: one Scriban expression with a declared type, no model call, pure builtins only, recorded and hashed | § 4 |
| an array item is named label plus position (*"Sections to produce 1"*) | an `array<object>` input or **any loop over objects** may declare **`labelField`** | § 5 |
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
is narrowed at 30 to *"Loop 'x' iterates node.y.value.z; y runs inside loop 'l' — a loop iterates a
node outside it, or in an enclosing loop's body."* (as built, § 8).

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
outward (`item.parent.parent`) — settled in layer A (revision 2).
- A body node reads its own loop's item as at 29.
- A paragraph's prompt reads its section's title as `item.parent.value.title`.
- **A node in an outer body** is read by id, aligned on the common prefix: `node.outline` from
  inside `paragraphs` is this section's outline.
- **A node in an inner body is a list** from outside it, read as a loop's elements are today
  (`{{ for e in node.paragraphs }}`).
- **Siblings across instances are not readable** — and need no sentence of their own. The
  grammar cannot name another instance: `node.draft_paragraph` always aligns to the reader's own
  path, so revision 1's proposed sibling sentence is unreachable statically and was dropped in
  revision 2. What does refuse a cross-level read: a body node read from outside its loop
  (*"… reads node 'draft_paragraph', which runs inside loop 'paragraphs'; read node.paragraphs."*)
  and a body node reading the loop it runs in.

### 2.5 An empty inner list

As at 29 (v29 design § 2.3), an empty list settles at **0** with its reason, *for that instance*
(`no items: node.outline.value.points was empty`, under `hpi`). It is never *not yet known* and
never a failure. The outer item still completes; its deliverable part reads an empty list.

### 2.6 What is recorded

- One loop-instance row per instance, holding its path, count, settled moment and reason (§ 7).
- Item rows keyed by path.
- The record shows the shape the run took. As at 29, it never records the element values
  (#369's rule) — except an item's name drawn from its `labelField` (§ 5, revision 3).

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
  once — unless it declares **`checkFirst: true`** (revision 2): then the check runs before each
  pass, and a `while` may run **zero passes**. Zero passes read as `.passes` 0 and `.stopped`
  `check`; its returned node reads as skipped, as an untaken `if` branch's does.
- **The check is a condition** that reads the body by bare id (that pass's nodes), `pass`, and
  anything that runs before the loop — inputs, earlier nodes, an earlier loop (revision 2).
- **`until` may sit beside `while`** (revision 2): checked after each pass; whichever fires first
  stops the while. A node with `until` and neither `for` nor `while` is refused.
- **Reaching the bound is a named state**, never a silent stop (decided): *"stopped at its bound
  of 3; the check never stopped holding"*. The run continues with the last pass's output.
  Whether a deliverable that read it should be held is not a `while` rule: a deliverable's
  `check` does that, as it does today.

### 3.2 What a pass reads, and what the outside reads

**A pass is an object, with one shape everywhere** (settled in revision 2): `pass`,
`pass.previous`, `pass.parent` and each element of `pass.history` all have `.number` (1-based),
`.node.<id>`, `.previous`, `.parent` and `.history`.

| read | means | a dependency edge? |
|---|---|---|
| `node.judge` or `pass.node.judge` | this pass's node | yes |
| `pass.previous.node.judge` | one pass back (null on the first) | no |
| `pass.previous.previous.node.judge` | two passes back | no |
| `{{ for p in pass.history }}…{{ p.node.judge.value.feedback }}…{{ end }}`, `pass.history[-1]` | every earlier pass, oldest first (empty on the first) | no |
| `pass.parent.number`, `pass.parent.previous.node.critic` | the enclosing while's pass (`pass.parent.parent` …), skipping any `for` between | this pass: yes; earlier: no |

- **`pass.….node.<id>`** names a node under that pass's while **at any depth** — inside a branch or
  an inner loop — read where `node.<id>` would be (the same scope rule): a node inside an inner
  `for` is aligned on the item path, and from outside that `for` the read names the loop
  (`pass.previous.node.points`).
- **Reading an earlier pass adds no dependency edge**, so a redraft and its judge form no cycle;
  the walk records it (`PassPrevious`) so the engine knows which outputs a pass keeps.
- **A negative index** counts from the end of a history only; elsewhere it stays refused.

**Outside the while**, `node.refine` reads like a loop's returned node:
- `.content` and `.value` are the last pass's `ret`;
- `.passes` is the pass count;
- `.stopped` is `check`, `until`, `bound` (§ 3.4);
- **`.history`** is every pass: each element carries `.number` and `.node.<id>` for the while's
  *direct* body nodes (an inner loop read whole), recorded as a read of the while — as a loop's
  element is. `.previous`, `.parent` and `.history` on such an element are refused: the list is in
  pass order.

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
- **What `until` reads** (revision 2): the item's body nodes by bare id, `item`, and anything that
  runs before the loop — as a while's check does. Revision 1's *"until reads the item's own
  nodes"* is dropped.
- **`max` on any `for`** (revision 2): at least 1, no cap — it runs the first N items. A while's
  `max` stays 1–10.
- **`.stopped`** on a `for` and on a `while` (revision 2): `check` (a while's check stopped
  holding, zero passes included), `until` (its until held), `bound` (its `max` was reached) or
  `end` (a `for` ran out of items). It is typed by those four values.
- **Leaving from inside the body** — `break`, `continue`, a labelled break — is not in 30
  (#1351).

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

- **`type`** is `text`, `number`, `boolean`, or a declared object format's id — **no bare `list`**
  (settled in revision 2): a list is a format whose top-level field is an array, as at 29 every
  format's value is an object. A text format is refused. The value must match its type: a
  mismatch the walk can know (`… | array.size` is a number; a comparison is a boolean) is
  refused at publish (*"Let 'problem_count' yields a number; it declares type text."*); the rest
  is the run's `onInvalid`.
- **`onInvalid` is required** — `fail` or `flag`, no default; `retry` is refused (there is no model
  to repair).
- **It carries none of a model node's keys** — `format`, `prompt`, `with`, `failIfEmpty`,
  `reproducible`, `macros`, `aggregate` — and implies no prompt file.
- **What it may read:** what a template may read at that place (inputs, nodes, `item` inside a
  loop, `pass` inside a `while`), resolved by the same reference walker. So a `let` inside a loop
  computes once per item.
- **Its typical use is a reduce over a loop's list** (`node.paragraphs.value | array.map
  "words" | array.sum`). A reduce over a dynamic list waits for the loop instance to settle, as
  any read of that list does.
- **Pure builtins only, by allowlist** (revision 2): a `let` calls only the Scriban builtins named
  pure (`TemplateObjects.PureBuiltins`); `date.now`, `date.utc_now`, `math.random`, `math.uuid`,
  `object.eval`, `include` and date's culture-settable format are refused by name. A test
  enumerates Scriban's builtins so every one is classified. A `let` must give the same value on
  replay; the Durable orchestrator replays. (A prompt may still read the clock.)

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
- **On any loop over objects** (revision 2: a node's value, or an outer item's field
  `item.value.<f>`), the loop declares `labelField`, validated against the field the element is
  typed with: a required text field — in a format's schema, listed in `required` and not
  nullable. A loop over an input takes the input's `labelField`; a folder's items keep their
  `label` header; a `labelField` on either loop is refused naming that.
- **A blank value falls back** to label plus position (*"Sections to produce 2"*), the 29 name.
- **Where the name reaches:**
  - `WorkflowInputFans.Items` (`WorkflowInputFans.cs:52`) and `ConsultNodeValueLoops.ItemsOf`
    (`ConsultNodeValueLoops.cs:101`) set `name` from the field;
  - from there it reaches the composed heading, the rail, History, the block names and the
    `block-skipped` labels with no other change (`WorkflowPackageBlocks.ItemName`, `:176`).
- **Folder items are unchanged**: they are named by their `label` header.
- **Capped** (revision 3): a name drawn from a field is the field's text, trimmed, then cut to
  120 characters ending in "…". The whole text reaches only the composed note, which is produced
  text and deleted with it.
- **What is recorded, and for how long** (revision 3, decided 2026-11-11): an item named by its
  field carries that text in the record — its block's name, the roster's item, item progress,
  History's labels, and a `not run … (Name)` reason. The retention sweep (`DropText`, #368)
  deletes produced text, concepts and held inputs, not names, so the name stays for the life of
  the record, as a folder item's `label` does. This is the one exception to § 7's rule, and it
  is deliberate: the name is what History shows.
- **The authoring consequence:** `labelField` should name a field that identifies the item
  without patient detail — a section's title, not a finding. A field that holds patient text puts
  that text in the permanent record. The Editor's `labelField` choice (§ 11) says so, and the
  normative `package-format-v30.md` carries the sentence (§ 14).
- **It lands with the 30 bump** (decided), not as an optional key at 29.

## 6. The deliverable at 30

- **A nested list composes as nested iteration:**
  `{{ for s in node.sections }}{{ for p in s.paragraphs }}{{ p.content }}{{ end }}{{ end }}`.
  The element of an outer loop (`s`) exposes each inner loop **by its id** (`s.paragraphs`), with
  the same element members 27 gave: `item`, `content`, `value` and `skipped` — settled in layer A.
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
- **the `let` row** (§ 4.2);
- **item names may be element text**: an item named by its `labelField` (§ 5, revision 3).

As at 29, the record holds hashes, ids, counts and reasons, **never element values** (#369's
rule), **except an item's name drawn from its `labelField`** (§ 5, revision 3: kept past the
retention sweep, capped at 120 characters). Storage 2–10 records read exactly as today.

## 8. The validator's sentences

**As built in layer A** (revision 2). Every new sentence is said at 30 only. Below 30 each new key
is a name-gate, and every 26–29 sentence stands word for word until the bump re-exports the vectors
that pin it.

**Below 30** (`ValidateV30Members`): *"Node 'refine' declares while, which requires specVersion
30."* — likewise `max`, `until`, `checkFirst`, `let`, `type`, `labelField`; and *"Input 'sections'
declares labelField, which requires specVersion 30."* The 26 nesting sentence and 27's "a branch
holds plain nodes" stand below 30; at 30 they are lifted.

**Nesting:**
- *"Node 'x' lists itself in its body; a body cannot hold the node that holds it."* / *"Node 'x'
  runs inside itself (a in b in x); …"*
- *"Loop 'x' iterates item.value.f, but it runs inside no loop; item.value names the enclosing
  loop's item."*; *"…; an inner loop iterates a list field of the outer item —
  item.value.<field>."*; *"…, which the outer item types as a text, not an array."*
- *"Loop 'x' iterates node.y.value.z; y runs inside loop 'l' — a loop iterates a node outside it,
  or in an enclosing loop's body."*
- *"… reads item.parent, but its loop runs inside no loop; item.parent is the enclosing loop's
  item."*

**`while` and `until`:**
- *"While 'refine' declares no max; a while must declare its bound (1 to 10)."* / *"… declares max
  11; the most is 10."* / *"… max 0; the least is 1."* / *"Loop 'x' declares max 0; the least is
  1."*
- *"While 'refine' has no body; name the nodes that run per pass in nodes."*
- *"Node 'x' declares max, which belongs to a for or while node."* — likewise `until`; *"…
  checkFirst, which belongs to a while node."*; *"Node 'x' declares for, while; a node is one flow
  node — for, while, if or case."*
- *"Node 'x' in while 'refine' is neither returned nor read; name it with ret, read it from a body
  node or from the check."*

**`pass`:**
- *"… reads pass.number, but it runs inside no while; pass is a while's pass."*
- *"… reads pass.parent, but its while runs inside no while; pass.parent is the enclosing while's
  pass."*
- *"… reads pass.previous.node.plan, which is not in while 'refine'; a pass reads the while's own
  nodes."*
- *"… reads pass.history as a value; a history is iterated ({{ for p in pass.history }}) or
  indexed (pass.history[-1])."*
- *"… reads node.refine.history[-1].previous; a pass of node.refine.history has .number and
  .node.<id> — the list is in pass order."*
- *"… which is not a member (a while has .content, .value, .passes, .stopped and .history)."*

**`let`:**
- *"Let 'problem_count' calls date.now, which is not deterministic; a let must give the same value
  on replay."*
- *"Let 'problem_count' declares onInvalid retry; retry needs a model — use fail or flag."* / *"…
  must declare onInvalid (fail | flag); its value is checked against its type."*
- *"Let 'problem_count' declares no type; …"* / *"… type 'prose' is a text format; a let's format
  types an object — or write type text, number or boolean."*
- *"Let 'problem_count' yields a number; it declares type text."*
- *"Let 'x' declares format, which a let does not carry; a let computes its value from its
  expression and names it with type."*

**`labelField`:**
- *"Input 'sections' labelField 'kind' names an enum field; labelField names a required text
  field."* / *"… is optional; …"* / *"Input 'notes' declares labelField, but it is not an array
  of objects; …"*
- *"Loop 'problems' labelField 'title' is not a field of the elements of
  node.find_problems.value.problems (fields: name, severity)."*
- *"Loop 'x' declares labelField, but it iterates input.y — the input names its items (input.y's
  labelField)."*

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
- **`until`**: an optional condition on a `for` (with a note that it runs the items in order) and
  on a `while`; **`max`** on a `for`; **`checkFirst`** on a `while`.
- **A `let` pane:** the expression, with the reference walker's diagnostics, and the type,
  chosen from text/number/boolean or a format.
- **`labelField`**: a picker on an `array<object>` input and on any loop over objects, offering the
  required text fields. Beside it (revision 3): *"The item's name is kept in the job's record after
  its text is deleted — pick a field that names the item without patient detail."* (§ 5).
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

- **A — the format, inert (#1339, built).** The model gains `while`, `max`, `until`,
  `checkFirst`, `let`, `type` and `labelField`; the validator at 30, with every § 8 sentence, sits
  behind the ceiling (`AcceptedSpecVersions` stays 29); the walker gains `item.parent`, the pass
  object and nested element members. Built in four PRs: **A1** nesting (#1349), **A2** `while` /
  `until` (#1350), **A2b** the pass object, history and the further stops (#1352), **A3** `let`,
  `labelField` and this revision.
- **B — the engine**, in this order, each inert below 30 (filed: B1 #1340, B2 #1341, B3 #1342,
  B4 #1343, B5 #1344):
  - **B1, paths at one level:** every key in § 1.2 goes through one path helper; a one-segment
    path equals today's id, so nothing changes.
  - **B2, nesting:** nested fan-out, per-instance resolution, the `count-resolved` path and
    storage 11's instance rows.
  - **B3, `while` and `until`:** pass scheduling and pass rows; in-order items and *not run*.
  - **B4, `let`:** the computed operation and its row.
  - **B5, `labelField`:** naming from the field, in both fan sources.
- **C — the pages** (filed: C1 #1345, C2 #1346):
  - **C1:** the Create page and History (§ 10).
  - **C2:** the Editor (§ 11).
- **The bump** (#1347).
  - **Registry PRs:** package-format and provenance, with the vectors of § 13; the normative
    `package-format-v30.md` carries § 5's retention sentence for `labelField` (revision 3).
  - **An engine PR** moving every ceiling to 30.
  - **A release, cut after a drain** (§ 9).
- **E — content** (#1348).
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
- **Leaving a loop from inside its body** — `break`, `continue`, a labelled break (#1351, filed
  2026-11-09 for after v30): keeping runs repeatable means serializing part of a pass, and the
  returned node may not run in the last pass.

**Settled in B5** (revision 3, 2026-11-11): a `labelField` name is recorded and kept past the
retention sweep, capped at 120 characters (§ 5, § 7). The alternatives — names reset to label
plus position at the sweep, or never recorded — were weighed and not taken.

**Settled in layer A** (revision 2; was "open for layer A"): the spellings `item.parent`,
`s.<inner loop>`, the pass object (`.number`, `.node`, `.previous`, `.parent`, `.history`),
`.passes`, `.stopped` and `.history`; § 8's sentences as built; `type` admits no bare `list`.
