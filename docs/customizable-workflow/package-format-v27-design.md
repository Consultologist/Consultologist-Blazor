# specVersion 27 — a flow node inside a `for`; the count is kept

> **Status:** design, **revision 1** (2026-10-29). Milestone 35 (#1173). Decided with the package
> author; not yet implemented. Design-level, not a step-by-step plan — the Milestone 35 issues are
> its sections and take their decisions from it; where a comment on one of them disagrees with this
> text, this text is the decision and names what it supersedes. The normative text follows in the
> package-format registry as `package-format-v27.md` once layer A lands (§ 9).
>
> **One-line model:** 26 made a loop a node whose body holds plain nodes and whose length is known
> when the job starts. 27 lets that body hold an **`if` or `case`** — per item the condition decides
> which branch's nodes run, the loop may return the flow node, and **every item stays a row**: an
> item whose branch holds no nodes is a row that says *skipped by condition*. Nothing a run
> discovers changes a count; `TotalBlockCount` stays the stored scalar (#176) and the scheduler
> keeps its shape. The dynamic half — a count the run discovers — is **28** (Milestone 32;
> v26 design § 6).

This is one bump, and a small one by design: one new permission in the grammar (a flow node in a
loop's `nodes[]`), one new decision moment in the engine (per item), one new row state in the
record (*skipped by condition*), and the surfaces that follow. It was named as a follow-up when
26 was decided (v26 design § 1.4, 2026-10-26) and filed as #1173; the number 27 was given to it on
2026-10-29, the dynamic half moving to 28, so that the static shape ships first and alone.

## Why

The `general` package drafts every section the same way. A real package wants a section drafted
one way for a new patient and another for a follow-up, or a section skipped when the item says
so — *per item*, inside the loop that already walks the sections. At 26 that is spelled as two
loops over the same folder each gated by a folder item's `if` over inputs, or not at all when the
condition reads the item or a body node. The classifier pattern 26 folded into `case` at the top
level has the same need one level down: *per section*, decide, then draft.

The decision that keeps this small: **the loop's length does not change.** An item whose
condition takes no branch is not dropped from the count; it is a row that says why nothing ran.
That is the line between 27 and 28 — 28 is where a count moves during a run.

## The port list

| at 26 | at 27 | § |
|---|---|---|
| a `for`'s `nodes[]` holds plain nodes; an `if`/`case` listed there is refused "a flow node inside a body is a later issue" | a `for`'s `nodes[]` may name an `if` or `case` node; the branches' nodes are body nodes of the loop, attached once through the flow node | § 1.1 |
| a `for`'s `ret` names a plain body node | a `for`'s `ret` may name the flow node (`node.triage`) | § 1.1 |
| an `if`/`case` is decided once per job — at start, or at the boundary when it reads a node's value | an `if`/`case` inside a loop is decided **once per item**, when the item's reads settle | § 1.3 |
| the condition scope: inputs, a scalar node's `.value`, `run`, `profile` | inside a loop, also **`item.*`** and the body nodes earlier in the body, by bare id; never the loop itself, never an outside body node | § 1.2 |
| an `if` with no `else` and no branch taken skips the node for the job | inside a loop, it skips **the item**: a row *skipped by condition*; the count is kept | § 1.4 |
| the flow row's `branch` is one string | an inner flow row carries **`branches`**, one per item; top-level rows keep `branch` | § 5 |
| record storage 8 | record storage **9** — the per-item branch map and the skipped-by-condition row | § 5 |
| `package-format v2026.10.13`, `provenance v2026.10.9` | the bump's versions | § 9 |

**Unchanged from 26, by decision:** everything else. Gates, headers, sources, the prompt path,
the template objects, the hash ladders, the editor's model of a body (one level deeper only).

## 1. A flow node inside a `for`

### 1.1 The shape

```json
{ "id": "sections",
  "for": "item in input.standards",
  "nodes": [ "standard_draft", "triage" ],
  "ret": "node.triage" }

{ "id": "triage",
  "if": "item.value.kind == \"narrative\"",
  "then": { "nodes": [ "patient_draft", "instructions" ], "ret": "node.instructions" },
  "else": { "nodes": [ "table_draft" ],                   "ret": "node.table_draft" } }
```

- A `for`'s `nodes[]` may name an **`if` or `case` node**. The flow node is a member of the loop's
  body like any other; its branches' nodes are **body nodes of the loop too**, attached once —
  through the flow node, never also listed in the loop's `nodes[]` (*"node 'instructions' is
  attached to 'triage' and to loop 'sections'; a branch node is attached once, through its flow
  node"*). They run once per item, item-aligned, as every body node does.
- A body node may read the inner flow node by bare id, item-aligned (`{{ triage.content }}` from
  a later body node is the taken branch's returned node's text *for this item*); the outside reads
  it only through the loop (§ 1.5).
- The loop's **`ret` may name the flow node** (`node.triage`); then the loop returns, per item, what
  the flow node returned for that item. A loop may still return a plain body node beside the flow
  node (the flow node then gates a side branch read by that node).
- **One level.** A loop's body may hold flow nodes; a flow node's body holds **plain nodes** — an
  `if` inside an `if` inside a `for`, or a `case` in a branch, is refused naming the version that
  takes it (*"node 'detail' runs inside node 'triage', which runs inside loop 'sections'; at
  specVersion 27 a branch holds plain nodes"*). A `for` inside anything stays 28 (nesting takes
  paths — v26 design § 6). An `if`/`case` inside an `if`/`case` **at the top level** stays refused
  as at 26 (the same sentence, "a flow node inside a body"), unchanged by this bump: nobody asked,
  and the decision moment is the same as the outer's — a later permission, not a 27 one.

### 1.2 What the condition may read

Inside a loop, the `if` expression (and a `case` subject) reads:

- **`item.*`** — the loop element: a folder item's `id`, `label`, `content`; an array input's
  element's `id`, `label`, `value` and its typed fields (`item.value.kind` on an `array<object>`
  input — the 26 element grammar; **no `item.<field>` alias**, decided 2026-10-29 at #1234 — the
  enum field the `case` switches on);
- **inputs**, `run`, `profile` — the 26 condition scope;
- **body nodes earlier in the body**, by bare id — a plain body node of the same loop that the flow
  node's own reads order before it (`node.standard_draft.value.kind`), item-aligned; a classification
  body node is a `case` subject (`"case": "node.standard_draft.value"`);
- **a top-level scalar node's `.value`** — settled before the loop runs, as at 26.

Never: **the loop itself** (*"Node 'triage' if reads node.sections, the loop it runs in; a condition
inside a loop reads the item, inputs and earlier body nodes"* — the loop is what it is inside of), **a body node
of another loop** (the 26 cross-scope sentence), **the flow node's own branch nodes** (they run
after the decision), a `deliverable.` (as at 26).

A `case` inside a loop switches on a closed set as at 26: an enum input, an enum-format body node,
or an **enum field of the item** (`item.value.<field>`, an `array<object>` input's field declared
`enum`; a folder item's field is text and cannot be a subject — *"Node 'route' switches on
item.value.kind, but a folder item's fields are text; a case switches on an enum input (input.<id>),
an enum-format node's value (node.<id>.value) or, inside a loop over an array input, an enum field of
the item (item.value.<field>)"*; a text field *"… which is a text; …"*, an undeclared one *"… which
the item does not carry (fields: …); …"*).

### 1.3 When the decision is made

A top-level `if`/`case` is decided once per job: at start when it reads inputs, run facts and the
item-free scope; at the boundary when it reads a node's value (v26 design § 1, the two decision
moments). An inner flow node is decided **once per item**, by the same rule applied inside the
item:

- reads of `item.*`, inputs, `run`, `profile` and top-level settled nodes only → decided **when the
  item starts** (its first body node is dispatched);
- reads of an earlier body node → decided **when that body node completes for the item** — the
  decision is an item-aligned dependency like any other, and the flow node's taken branch's nodes
  become ready for that item only then.

The decision is made where 26's boundary decision is made — in an activity, over the recorded
outputs, so the orchestrator stays deterministic — once per `flow:item`. An inner flow node's
condition is **not a gating read** of the job (`GatingReads` excludes it): the job starts as a 26
job does, the loop's length known; the per-item decisions are inside the loop's run.

### 1.4 Skipped by condition

An inner `if` with no `else` and no branch taken, or a `case` whose value no branch lists and no
`else`, **skips the item for that flow node**. The item is still a row of the loop (the count is
kept): the flow node's row for the item is *skipped by condition*, recorded with the **condition's
text** — never the value it read (#369's rule stands: a record never prints a patient value; the
v26 design § 6 wording "and the value it read" is **superseded** here for 27, and 28 inherits this
decision). The branch nodes of that item run nothing and have no rows for it, as a pruned body has
no row (#1223 — named by the flow row's per-item branch, § 5).

What the loop returns for a skipped item:

| read | with the skipped item |
|---|---|
| `node.sections.content` | the item contributes **nothing** — the concatenation runs over the items that returned; the record names the skipped ones |
| `node.sections.value` | **one element per returned item** (not null holes): the list is what the loop returned; a reader that needs the position reads `{{ for e in node.sections }}` |
| `{{ for e in node.sections }}` | **every item**, in order; a skipped element has `e.item`, `e.skipped == true`, and `e.content`/`e.value` null — the loop is still the collection |
| a deliverable sourcing the loop | composes the returned items' parts (the 25 aggregator's `ForEachPart` over the present items); a document whose every item was skipped is a document with no section, **not** a skipped document (the loop ran; it returned nothing) |

The loop's aggregate input hash is over the present items' output hashes, in item order — the 25
ladder with absent items absent, so a 26 loop with no inner flow hashes byte-identically at 27.

### 1.5 What the outside reads

`node.<loop>` is as at 26 (v26 design § 1.3): `.content` concatenated in item order, `.value` the
list, the bare name iterable. When the loop returns an inner flow node, "the returned node" is,
per item, **the branch's returned node for that item** — `.value` is typed only when every branch's
returned node has one format (the 26 rule on an `if`/`case`, applied to the loop's ret), else
refused by name (*"loop 'sections' returns 'triage', whose branches return prose and
medication_list; node.sections.value needs one format — read node.sections.content"*). A
deliverable sourcing a loop that returns a flow node needs **every branch prose** (as a sourced
top-level `if` does), refused by name otherwise.

`node.<inner flow>` from **outside the loop** is refused as a body node is (*"node 'triage' runs
inside loop 'sections'; read node.sections"*); the flow node has no job-level value.

## 2. The record (storage 9)

**`nodes`.** An inner flow row carries `in: <loop>` (as a body node's does), `kind`, `expression`
or `case`, `body` (every branch's node ids), `ret` (per branch, as today), and **`branches`** — a
map `itemId → "then" | "else" | "<values>" | "none"`, one entry per item of the loop, written as
the items decide (the row is complete when the loop completes). A top-level flow row keeps its
one `branch`; a row never carries both. A body node of a branch has `in: <inner flow>`; its loop
is reached through the flow row's `in` (two levels; a reader walks `in` until it meets a `for`).

**`nodeOutputs`.** A branch node's rows stay `nodeId:itemId` for the items it ran. The inner flow
node gets a row per item **only when skipped**: `<flow>:<item>` with `status: skipped` and
`reason: needs \`<condition text>\`; it does not hold` (the fixed form `WorkflowConditionAt25.Reason`
already prints — the text, never a value). A taken item has no flow-node row: the taken branch's
returned node's row *is* its output, as a top-level flow node has no row of its own.

**Documents.** `sources`, `composedHash` and the deliverable rows are unchanged; a deliverable
whose loop source returned nothing (every item skipped) is a document with no section and a
composed hash of the empty composition — not a `skippedDocuments` entry.

**Why 9.** A new field on a row (`branches`), a new row state (a flow node's per-item skipped
row with a reason) — a reader at 8 would miss the map and mistake the row. No hash ladder moves
(`NodeHashVersion` 6, the aggregate ladder over present items, the composed hash). The provenance
registry bumps (`recordStorageVersions` gains 9; `provenance-record.md`'s `nodes` and
`nodeOutputs` rows); the positional `ConsultGenerationJobInitialize` gains nothing (the map rides
the flow row; the skipped row is a `nodeOutputs` entry) — **no replay-golden churn** expected
(the spec-bump memory's v14 lesson applies only if a job-initialize field is appended).

## 3. The validator's sentences (new at 27)

As built at #1234 (2026-10-29) — the subjects follow the 26 idiom (*"Node 'x'"*, *"Node 'x' if"*,
*"Deliverable 'd' source node.x"*):

- Below 27: *"Node 'triage' runs inside loop 'sections'; a flow node inside a loop requires
  specVersion 27."* (replaces the 26 "later issue" sentence for an `if`/`case` in a `for`; the
  `for`-inside-anything sentence is unchanged).
- *"Node 'patient_draft' is attached to 'triage' and to loop 'sections'; a branch node is attached
  once, through its flow node"*; a node in the branches of two flow nodes of one loop keeps the 26
  sentence *"Node 'patient_draft' runs inside node 'triage' and inside node 'route'; a node runs in
  one body"*.
- *"Node 'detail' runs inside node 'triage', which runs inside loop 'sections'; at specVersion 27 a
  branch holds plain nodes"* (one level); at the top level, in 27's words, *"Node 'detail' runs
  inside node 'triage'; at specVersion 27 a branch holds plain nodes"* (the 26 sentence stays for a
  26 package).
- *"Node 'triage' if reads node.sections, the loop it runs in; a condition inside a loop reads the
  item, inputs and earlier body nodes"*; *"Node 'triage' if reads node 'patient_draft', which
  'triage' decides; a condition reads what runs before it"*; the 26 cross-scope sentence for
  another loop's body node (*"… reads node 'other_notes', which runs inside loop 'other'; read
  node.other"*).
- The case rule at 27: *"a case switches on an enum input (input.<id>), an enum-format node's value
  (node.<id>.value) or, inside a loop over an array input, an enum field of the item
  (item.value.<field>)"* — *"Node 'route' switches on item.value.title, which is a text; …"*, *"…
  which the item does not carry (fields: kind, title); …"*, *"… but a folder item's fields are
  text; …"*, *"… but it runs in no loop; …"*.
- *"Node 'assessment' prompt reads node.sections.value, but loop 'sections' returns 'triage', whose
  branches return 'object' and 'text'; node.sections.value needs one format — read
  node.sections.content"* (and *"… reads e.value, … e.value needs one format — read e.content"* on
  the element); *"Deliverable 'consult' source node.sections loops over 'triage', whose then
  returns 'full_extract' of format 'concept_list'; a deliverable composes prose"* (the first
  non-prose branch named).
- *"Node 'assessment' prompt reads node 'triage', which runs inside loop 'sections'; read
  node.sections"* (the 26 sentence, now also for a flow node read from outside); *"Node
  'section_notes' prompt reads node 'patient_draft', which runs inside node 'triage'; read
  node.triage"* (a loop body node never reads down into a branch); *"Node 'patient_draft' reads
  node.sections, the loop it runs in; a body node reads its siblings, the outside reads the loop"*
  (the loop two levels up).
- Reachability inside the branch as at 26; the inner flow node is a member of the loop's body
  (returned by the loop or read by a later body node — a branch node's read keeps a loop body node
  reachable), else the 26 sentence *"Node 'triage' in loop 'sections' is neither returned nor
  read; name it with ret or read it from a body node"*.

## 4. The walk and the edges

`WorkflowFlowNodes.Resolve` keeps **two levels of owner**: a branch node's owner is the inner flow
node, whose owner is the loop; `BodyScopes` carries the chain so a bare-id read inside a branch
resolves siblings of the branch *and* the loop's body nodes earlier than the flow node;
`FanOf` reaches the loop's fan through the flow node, so `item.*` resolves in a branch node's prompt
and in the flow node's condition. The derived edges: the inner flow node depends on its condition's
reads (item-aligned where they are body nodes); a branch node depends on the flow node (the
decision) and on what it reads; the loop depends on its `ret` as today. `GatingReads` excludes
inner flow conditions (and an inner case's subject). As built: the inner condition is walked under the
loop's fan with **the flow node's own id as its scope**, so the walker admits its branch nodes as
siblings and the validator says the "which 'triage' decides" sentence; a bare-id read is in scope
when the target runs in the reader's body or in the body the reader's own flow node runs in (one
level up, read through `Bodies` — a scope's own entry names its owner; no new field on
`TemplateBodyScope`), never down. `ShapeOf` gives a loop whose `ret` is a flow node the branch shape (one
format when the branches agree, else the disagreement by name) — the Aggregator fallback is
retired for that case.

## 5. The engine

- **Skeleton:** unchanged — every item is a block; `TotalBlockCount` is the loop's count as at 26.
- **Fire set:** `DecideFireSet` decides and prunes **top-level** flow nodes only; an inner flow node
  is skipped there (its `branches` map starts empty).
- **Per item:** when an item's flow node is ready (§ 1.3), a decision activity evaluates the
  condition over a scope with the item bound (`ConsultConditionScope.WithItem` gains the input-array
  element and the body-node outputs for the item; `WorkflowFlowDecision.Decide` is unchanged) and
  records `branches[item]`; the taken branch's nodes become ready for that item; a `none` writes the
  skipped row and completes the item's block as *skipped by condition* (progress ticks).
- **Readiness:** `InstanceReady` treats `flow:item` as a dependency — a branch node's instance for
  an item is ready when its flow node decided *that item* and took its branch; a body node reading
  the flow node by bare id waits on the branch's returned node for the item (or the skipped row).
- **Composition:** `ConsultFlowCompletion.TryCompose` for a loop whose `ret` is a flow node
  composes, per item, the taken branch's returned node's output, skipping skipped items;
  `ConsultRenderPayload.LoopsFor` yields a skipped element (`e.skipped`) instead of throwing on a
  body node with no output for an item; `NodeTextsFor`/`FlowRetsFor` per item.
- **Record:** the inner flow row's `branches` map, the skipped `nodeOutputs` row with its reason,
  storage 9; `Public/Engine` reports the provenance version from the pin.

**As built (#1235, 2026-10-29).** The decision is the activity `decide-flow-item`, one call per
(flow node, item), dispatched by the orchestrator exactly as a node instance is — when the flow
node's reads settle for the item (`InstanceReady` over its `Reads`, item-aligned through the
loop's collection as its working `ForEach`) — over `ConsultConditionScope.WithFanItem`; the
result carries the branch, the taken body's node ids and returned node, and the reason when none
(`WorkflowConditionAt25.Reason` for an `if`; *"switches on \`<subject>\`; no branch lists its
value"* for a `case` — never a value). The orchestrator keeps the calculus in `ConsultInnerFlows`
(taken, untaken, settled, the taken ret, the loop returning the flow): a branch node's instance
starts only once its flow node took the branch holding it; an untaken instance is settled for the
chain's completion and the stall guard; the flow's instance completes as the taken branch's
returned node did — the alias `flow:item` — which also completes the loop's block for the item;
a skipped item's instance key joins the skipped set the readiness check and the payload consult.
The skipped item's block is a named state, **`Skipped`**, with the reason (decided 2026-10-29),
counted among the completed blocks, served as `skippedBlocks` and streamed as `block-skipped`.
`FlowRets` carries the returned node per item (`flow:item`), so a body node's bare-id read of the
flow and a loop element type by the item's branch. `SupportedSpecVersions` stays at 26 until the
bump (the store's set is pinned by test against the registry and the conformance suite) — the §
9 B line "`SupportedSpecVersions` += 27" is the bump's. Found and fixed on the way: the engine's
render scope typed an input fan's element from the ≤24 typed-variables map (empty at 25+), so
`item.value.<field>` of an array of objects rendered null where the validator typed it; it now
reads the array's declaration by input id.

## 6. The editor (at 27)

- A loop's body list may hold an `if`/`case`: the loop pane's "+ add" offers flow nodes too; the
  node pane's "in" picker is offered to a flow node (loops only as targets); a branch's list
  offers plain nodes only. The Nodes nav nests **two levels** (loop → inner flow → branch nodes).
- The inner flow's pane: the condition builder's scope lists `item.*`, inputs and the earlier body
  nodes; the `case` subject picker offers the item's enum fields and earlier classification body
  nodes.
- The preview's item scope reaches a branch node through its two-level owner; the preview judges
  the inner flow's gate per picked item (`TemplatePreviewScope.Gates` gains the per-item flow gate).
- History draws the inner flow as a subgraph inside the loop's (`RunDagDiagram.EmitFlow` recurses
  one level), the per-item branches as the item rows' tags, a skipped item dashed with its reason;
  the Consults rail fans a branch node through its loop (`RecordFlow.FanOf` walks `in` to the `for`).
- The desk: the § 3 sentences in the validator's words; "Upgrade to specVersion 27" is a **version
  raise**, no rewrite (§ 7).

**As built (#1236, 2026-10-29).** The editor cannot open a 27 package before the bump (its manifest
reads use the library's public accepted set), so the 27 panes are built and tested behind a
test-only seam — `Templates.AuthorsAboveCeiling` widens the accepted set at the three manifest reads
and the desk's validate (`InternalsVisibleTo` the Web); no banner, since the API refuses 27 until
the bump; #1237 retired it (2026-10-29) as #1164 retired #1209's. The flow-in-loop controls (the loop pane's
if/case choices, the if/case panes' "in loop" picker, the + Node form's loop for an if/case) are
offered at 27 only, so the 26 panes stay byte-identical. The editor's reader resolves a branch
node's loop two levels up (`NodeView.LoopOf` through `FlowOf`); the Nodes nav nests two levels. The
inner if's builder offers the item (a folder's `item:id`; an array input's `item:value.<field>`,
typed — the builder's spelling gained the element field) and narrows its node operands to top-level
nodes and the loop's body nodes, never the loop or its own branches; a case inside a loop offers
`item.value.<enum field>` and reads its values from the input's element. **The preview judges flow
gates** (decided 2026-10-29): a new `TemplatePreviewGateKind.Flow` — a top-level if/case over the
picks (the 26 gap closed in passing), one inside a loop over the picked item — and a branch node's
pane says which branch its flow node takes for this pick and whether the node runs. The upgrade's
newest rung from 26 is `WorkflowPackageVersionRaise` (specVersion stamped, nothing rewritten);
the button offers 27 at the bump. **History** (decided 2026-10-29) shows an inner if/case as a
per-item group from the `branches` map — a taken item as the branch node that returned for it, a
skipped one dashed with its reason — and the flow tag `per item`; `RecordFlow` walks `in` to the
`for`; the DAG nests the inner subgraph inside the loop's with the branches tallied on the diamond;
nothing is pruned for an inner flow. **The consult page** shows a skipped block as a named line in
the section's place, the rail's section row and an item row read Skipped, the counts count it, and
`block-skipped` is a live event. The preview's loop elements landed as #1243 (2026-10-30): a reader that iterates a loop
previews one element per folder item (a placeholder for an array input), the inner if/case judged
per element from the picks, `e.skipped` per item.

## 7. Migration 26 → 27

None: 27 only permits what 26 refused. The editor's upgrade and `scripts/migrate-workflow-package.cs
--to 27` raise `specVersion` and validate; the seven public packages are valid at 27 unchanged and
**do not republish for this bump** (no content uses the shape). A new example package
(`example-conditional-sections`: a loop over sections whose `if` reads the item's kind) is the
content's proof and the smoke's subject.

## 8. Deferred, by name

- An `if`/`case` inside an `if`/`case` (any level) — nobody asked; the top-level case keeps its 26
  sentence, the inner case takes the one-level sentence (§ 1.1).
- A `for` inside anything; a count that changes; `while`, `for … until`, `let` — **28** (Milestone
  32; v26 design § 6).
- A folder item's text field as a `case` subject (needs a closed-set declaration on the folder
  index) — with enum value labels (#1061).
- Recording the value a condition read — refused by #369's rule, here and at 28.

## 9. Phasing (Milestone 35)

One layer per PR, each green on its own; the bump is the last.

- **A — format** (`Consultologist.PackageFormat`, the package-format registry): the two-level owner
  and scopes in `WorkflowFlowNodes` and the walk (§ 4); the condition scope inside a loop (§ 1.2);
  `ShapeOf` for a loop returning a flow node; every § 3 sentence; `package-format-v27.md`, the
  schema (no shape change — the grammar's permission is a cross-field rule, so the vectors are
  `SHAPE_BLIND`) and vectors: valid — a loop returning an `if`, a loop returning a `case` on an
  item's enum field, a loop returning a plain node beside a gated side branch, a condition reading
  an earlier body node; invalid — one per § 3 sentence; the gate under `v26/` (*"requires
  specVersion 27"*). As done (2026-10-29): the registry PR carries the v27 document unlisted and the
  regenerated below-27 gate vector (package-format v2026.10.14); the v27 vectors are held back in
  the engine's export and the 27 schema waits; `AcceptedSpecVersions` += 27 is **the bump's**
  (#1237).
- **B — engine + record + provenance** (§ 5, § 2): the per-item decision, readiness, composition,
  the skipped element, storage 9, `provenance-record.md` + `recordStorageVersions` 9 (a provenance
  registry PR), `SupportedSpecVersions` += 27; an engine release.
- **C — editor** (§ 6); the version raise as the upgrade.
- **The bump:** both sets += 27 together; the registry lists 27; the editor's ceiling follows C.
  *Landed 2026-10-29 (#1237), engine + registry.* Engine: `AcceptedSpecVersions` and
  `SupportedSpecVersions` += 27 together; the version raise moved into the library
  (`WorkflowPackageVersionRaise` — the editor's newest rung and
  `scripts/migrate-workflow-package.cs --to 27` share it, as they share the 25 → 26 rewrite; the
  script's 27 stage validates in memory and writes `manifest.json` and `dag.mmd` alone); the
  editor's two ceilings to 27 at once, the #1236 seam and the library's `InternalsVisibleTo` for it
  retired, the one button "Upgrade to specVersion 27" — and the upgrade chained: a package below 26
  takes the 26 rewrite (its notices under "Through 26:") and then the raise, each rung refusing by
  its own name (the gap the bump would have opened: a 25 package rewritten to 26 and stamped 27 —
  found in planning, closed here); the stored upgrade's version 3; the next-is-refused tests moved
  to 28; the editor's 27 suites render without the seam. Registry package-format **v2026.10.15**
  (consultologist-package-format#42): 27 listed with `package-format-v27.md` and the generated
  `schemas/package-format-v27.schema.json` (the 26 shape with its version); `conformance/v27/` —
  18 vectors, 6 valid and 12 invalid, all 12 `SHAPE_BLIND` in three reasoned groups (a branch
  node's attachment and the one level; the inner condition's reach; the loop's read of its inner
  flow); the `any` case's sentence ends "or 27"; the README's counts (607 cases, 514 invalid, 148
  schema-rejected unchanged, 365 schema-blind); the v27 document's accepted-set sentences in the
  listed form. **Left:** the engine release carrying it (`Public/Engine` reports 5–27 — on the
  word, closes #1237) and the content smoke (#1238).
- **Content:** `example-conditional-sections` in consultologist-workflows; a 27 smoke on it with a
  storage-9 record; the pins stay (no public package needs 27).
  *Landed 2026-10-30 (#1238; consultologist-workflows#45, published as
  `example-conditional-sections@v2026.10.1`).* Two premises of the issue gave way to the format: an
  `if` with both bodies never skips (§ 1.4 — a skip is a flow node taking no branch), and a folder item
  carries no `kind` (v26 § header keys: `label`, `if`), so a kind lives on an `array<object>` input and a
  folder condition reads `item.id`. The package (decided 2026-10-30: three loops, each producing
  something distinct): a `case` on `item.value.kind` (narrative → prose, table → a table, an `omit`
  section skipped, no `else`); an `if` with no `else` writing one patient-facing line per narrative
  section; an `if` over a folder of standing sections (`item.id != "billing" || input.include_billing ==
  true`); a closing node reading the first loop's elements (`{{ for e in node.sections_draft }} … e.skipped
  … e.item.value.title`) to name what was left out. Validated at 27 with 0 errors / 0 warnings first pass;
  `dag.mmd` by `--dag`. **The smoke** (job `43812b8ea261456d98c0a22b3979d544`, 2026-10-30, named on the
  start body — no pin moved, no owned publish): Completed 10/10 in 13 s, **record storage 9**; `route`
  (`case`, in `sections_draft`) `branches {0: narrative, 1: table, 2: none}`; `narrative_only` (`if`, in
  `patient_summaries`) `{0: then, 1: none, 2: none}`; `applies` (`if`, in `standing_sections`)
  `{safety_netting: then, follow_up: then, billing: none}`; four `<flow>:<item>` rows `Skipped` with the
  condition's text as reason (*"switches on `item.value.kind`; no branch lists its value"*, *"needs
  `item.value.kind == "narrative"`; it does not hold"*, *"needs `item.id != "billing" || …`; it does not
  hold"*); `skippedBlocks` names the four blocks by `<deliverable>:<loop>:<item>`; every loop's `count` 3
  and its composition over the returned items alone; both deliverables produced (the consult note ends
  *"Billing codes were left out at the author's request."* — the `e.skipped` read, live). The seven public
  packages and the pin `test/general@v2026.10.12` stay at 26. **History seen live** (2026-10-30, the
  job's page): `format v27`, the record storage line linking *storage version 9 in provenance-record.md,
  provenance@v2026.10.10*; each inner flow a group of its items — `0 · narrative → narrative_draft`,
  `1 · table → table_draft`, `2 · skipped — switches on \`item.value.kind\`; no branch lists its value`
  (struck through); `narrative_only` with `0 · then → patient_summary` and two skipped; `applies` with
  `safety_netting` and `follow_up` then and `billing` skipped — the flow tags `· case item.value.kind ·
  per item` / `· if … · per item`, the branch nodes `in route` / `in narrative_only` / `in applies` fanning
  per item with their counts (1), (1), (2). Observed, not a defect of this issue: an
  `array<object>` loop's item label composes as the input's label plus the position ("Sections to produce
  1"), not the item's `title` — an issue to file on the word.

**Done when** (#1173): this doc exists with a decision or a named open question for every port-list
row — it does; each Milestone 35 issue links its section; the bump issue is filed.
