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
  (`ConsultGenerationTransport.cs:2322`). That still works, because a path has no `:`. The
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
section's* outline). v29's sentence *"a loop iterates a node outside every loop"* (`V29.cs:39`)
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
