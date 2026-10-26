# specVersion 26 — every object is a node, every gate is Scriban

> **Status:** design, **revision 1** (2026-10-24). Milestone 30 (#1104). Decided with the package
> author; not yet implemented. Design-level, not a step-by-step plan — the Milestone 30 issues
> (#1059, #1062, #1063, #1089, #1090, #1094, #1096, #1099, #1100, #1101, #1102, #1105, #1116) are
> its sections and take their decisions from it; where a comment on one of them disagrees with
> this text, this text is the decision and names what it supersedes. The normative text follows
> in the package-format registry as `package-format-v26.md` once layer A lands (§ 14).
>
> **One-line model:** v25 made every template a Scriban template and every reference a dot read;
> 26 finishes the sentence on the manifest side. Structure and flow that 25 still spells as
> bespoke keys — `forEach`, a node's `when`, `forItem`, `aggregate` with `deliverables[].node`,
> `prompts[]` — become **flow nodes** (`if`, `case`, `for`) and **gates keyed `if`**; the two
> objects that exist only to be named by another (the aggregator, the prompt declaration) fold
> away; a file carries its own label, instruction and gate in a **JSON header**; and the one
> word with two meanings, `when`, keeps one — a `case` branch.

This is one bump. The flow nodes, the gate key, the folded aggregator, the prompt on the node and
the header all ride one idea — the manifest reads as a program over the same objects the
templates read — and a half state would spell `forEach` beside `for` and `when` beside `if` in
one file. The delivery is phased (format → engine + provenance → editor, as 25 was); the format
is not. **26 is the static half** of the program: a loop's length and a branch's condition are
known when the job starts, as every fan is today. The dynamic half — a loop over a node's value,
`while`, a loop inside a loop — is the next bump's, specified here by name (§ 6) so that 26
refuses it with the sentence that says where it is going, not with a shape error.

## Why

Four things the 25 manifest does pull against the templates it serves:

- **Flow is a key on the thing it governs.** `forEach: snippet.standards` sits on each of three
  nodes, so the reader aligns three loops that are one loop; `when` sits on the node it skips, so
  a condition shared by two nodes is written twice. The template side already reads the same
  structure as one object (`{{ for s in snippet.standards }}`), and the editor draws the three
  nodes as one fan it has to infer.
- **One word means two things.** In a template `when` is Scriban's branch label inside a `case`;
  in the manifest it is a gate on a node, an instance, a deliverable, a folder item. The editor
  shows "Applies when", "Gated when", "Produced when", "Included when" and "Runs when" for what
  is, in every case, an `if`.
- **Two objects exist only to be named by another.** An aggregator node composes text for
  exactly one deliverable, which names it; a prompt is declared in `prompts[]` so that exactly
  one node can point at it (no public package shares a prompt — § 4). Each is a name the author
  invents for a thing that has a home already.
- **A reference to one item of a fan is a bespoke key.** `forItem: "hpi"` on an instance says
  what a Scriban gate says in four words, `item.id == "hpi"`, beside a `when` that could carry
  it; the editor renders the key as a prefix on the gate's row because that is what it is.

26 answers all four with one idea: **the manifest is a program over the objects the templates
read**. A loop is a node with a body; a branch is a node with a condition; a gate is `if`; a
deliverable names what it composes; a node names its file; a file says what it is.

## The port list

Every 25 key that spells structure or flow as a bespoke key, and where it goes. (The `ns:id`
colon spellings — `node:x`, `data:x`, `forEach: data:x`, `as:` — are gone since 25; what remains
is keys, not spellings.) Each row's section carries the decision.

| v25 key | v26 | section / issue |
|---|---|---|
| `nodes[].forEach: snippet.<folder>` / `input.<id>` | a **`for`** node over the collection; the fanned nodes are its body | § 1 — #1100 |
| `nodes[].when` | an **`if`** node whose body holds the node | § 1 — #1100 |
| instance `when`, `deliverables[].when`, `deliverables[].macros[].when`, folder item `when` | the key **`if`** (a data gate; `when` exists only inside a `case`) | § 2 — #1089, #1090, #1094, #1100 |
| instance `forItem: "<item>"` | `if: "item.id == \"<item>\""`; several items, `case item.id` | § 2 — #1100 |
| `nodes[].aggregate` + `deliverables[].node` | `deliverables[].sources: ["node.<id>", …]`, the aggregator folded in; its composed text read as **`deliverable.<id>.content`** | § 3 — #1101 |
| `nodes[].prompt: <prompt id>` + `prompts[]` | `nodes[].prompt: "<file path>"`; reuse is a snippet | § 4 — #1102 |
| `index.json` item `name` / `when`; `macros[].label` / `instruction` / `optional` / `default` | a **JSON header** on the file (`label`, `if`, `instruction`, `optional`, `default`); the manifest keeps id and order | § 5 — #1105, #1099 |
| `item.name`, `snippet.<id>.name`, `macro.<id>.name` | `.label` | § 5 — #1105 |
| `{{ for s in snippet.<folder> }}` as the only way to an item | `snippet.<folder>.<item>.content` (and `.label`, `.id`), a direct read the item's `if` governs | § 5 — #1116, #1094 |
| `{{ node.x.content \| concept_context }}` | `{{ node.x \| concept_context }}` — the pipe takes the node | § 7.1 — #1059 |

**Keys that stay** — policy, position or metadata, not a reference or flow: `specVersion`,
`derivedFrom`, `title`/`description`/`tags`, `templating`; `nodes[].format`, `with` (values
already `node.<id>` / `input.<id>` / literals), `onInvalid` (`fail` | `retry` | `flag` — `retry`
stays the one-rung key; a bounded `while` is the next bump's, § 6), `failIfEmpty`,
`reproducible`; `inputs[].expectedContent`; `deliverables[].check: node.<id>` (a third state,
*failed*, beside produced and skipped — not an `if`, which would make a failed check look like a
skip), `signature`; an instance's `at`; `formats[].{id,file}`. A key's camelCase is the
manifest's JSON convention, not the ≤24 reference style. One catalogue oddity to note, not fix
here: `terms_subset`'s parameter `failWith`.

**What the seven public packages use** (`consultologist-workflows`, all at 25, read 2026-10-24),
because the migration is sized by it: every package fans — three or four nodes over one
`snippet.standards` in `general`, `test/general`, `example-two-documents` and
`example-v12-constructs`; `example-structured-intake` fans three nodes over `snippet.sections`
and two over `input.prior_notes`; one aggregator per deliverable, plus `example-two-documents`'
`agg_sections` that only a prompt reads (no deliverable names it) and `example-v12-constructs`'
`assemble_note` over `[node.section_instructions, node.patient_header]` — a fan and a scalar;
eight deliverable `when`s and two deliverable-instance `when`s (v12's tone macros); 25
renderer-pipe sites (`concept_bullets` ×20, `concept_context` ×5), every one on
`node.<id>.content`; one inline macro read (`test/general`'s `assessment_plan` reads
`macro.letrozole_prescription_text.prompt` under a text `if`). **No package uses** a node
`when`, `forItem`, a node macro instance, a folder item `when`, a nested fan, a shared prompt
(v12's `extract-terms` was split per node when 25 refused the differing binding). The port is
therefore: fold `forEach` siblings into `for` nodes, fold aggregators into `sources[]`, rewrite
25 pipe operands, rename ten gate keys, put each prompt's path on its node, move labels and
instructions into the files.

## 1. Flow nodes — `if`, `case`, `for`

*(#1100; decided 2026-10-18/19 in its comments, settled here.)* A flow node is a node in
`nodes[]` with an id and one of three flow keys in place of `format`. It has a **body** — nodes
that run inside it — and it **returns** one of them. The flow node is the only name the outside
sees.

### 1.1 The shapes

```json
{ "id": "sections",
  "for": "item in snippet.standards",
  "nodes": [ "standard_section_draft", "patient_section_draft", "section_instructions" ],
  "ret": "node.section_instructions" }

{ "id": "triage",
  "if": "input.kind == \"new\"",
  "then": { "nodes": [ "full_workup" ],   "ret": "node.full_workup" },
  "else": { "nodes": [ "brief_update" ],  "ret": "node.brief_update" } }

{ "id": "route",
  "case": "node.scope.value",
  "branches": [
    { "when": [ "in_scope" ],                              "nodes": [ "plan" ],    "ret": "node.plan" },
    { "when": [ "out_of_scope", "needs_information" ],     "nodes": [ "letter" ],  "ret": "node.letter" } ],
  "else": { "nodes": [ "hold" ], "ret": "node.hold" } }
```

- **`for`** iterates a Scriban collection — at 26 `snippet.<folder>` or `input.<id>` (an array
  input), the two collections whose length is known at job start. The loop variable is `item`,
  as in a template. Every body node runs once per item; the body is a sub-graph (body nodes read
  one another by bare id, item-aligned, as fanned siblings do at 25); leaving the body is the
  join. Two `for` nodes over the same collection are two loops, explicitly.
- **`if`** holds one Scriban boolean expression over the objects a condition may read (an input,
  a scalar node's `.value`, `run`, `profile` — the 25 condition scope) and a `then` body; `else`
  is optional.
- **`case`** switches on an expression whose type is a closed set — an enum input or an
  enum-format node (`classification`) — its `branches[]` one per set of values (a branch's
  `when[]` lists one or more, Scriban's word for the label; decided 2026-10-25 over a branch
  list keyed `when` on the node, which would have given `nodes[].when` two shapes under one
  name — the ≤25 string stays what it is and is retired) and an optional `else`. It is the classifier pattern spelled once: at 25
  "a classifier node plus N nodes each with a `when`" is N conditions that happen to partition
  the set; at 26 it is one node the editor offers branches for from the enum's values (#1091).
  **A value the set lacks is refused**, not warned: a manifest condition on an undeclared literal
  is refused at 25 already, and #1122's warning is for a template's own `if`, which is prose.
  (This corrects the 2026-10-18 comment on #1100, which said "warns".) A `when` listing a value
  twice, or two branches sharing one, is refused naming both. Scriban's `when` is the branch
  label here and **nowhere else** in a manifest (§ 2).

### 1.2 The body is a set; `ret` is required

A body's `nodes[]` is a **set**. Its order carries nothing — the body's own reads order it, as
`nodes[]` is ordered by its reads at 25. A body node is declared once, in the top-level
`nodes[]`, and *attached* by id; a node attached to two bodies, or to a body and read by bare id
from outside it, is refused: *"node 'section_instructions' runs inside loop 'sections'; read
node.sections"*.

Every flow node and every branch names the body node the outside reads with **`ret`**, and
`ret` is **required** — no default (decided 2026-10-19 16:14 on #1100, over the earlier "the
last in the body list"; the body is a set, and the editor's rule is explicit init: the picker
is empty until chosen and publish is blocked). Missing: *"loop 'sections' returns nothing; name
the body node the outside reads with ret."* A body node that is neither returned nor read by
another body node is refused as unreachable (#1067's rule, applied inside the body). A `for`
with one body node still names it — the sentence is the same and the editor fills it.

### 1.3 What the outside reads

`node.<flow>` has `.content` and `.value`, and they are **the returned node's**:

| read | `for` | `if` / `case` |
|---|---|---|
| `node.x.content` | the returned node's composed outputs in item order, concatenated — **byte-identical to the 25 aggregator's `.content` over one fan source** (#1065) | the taken branch's returned node's `.content` |
| `node.x.value` | an array of the returned node's values, one per item; typed by the returned node's format | the taken branch's `.value`, **typed only when every branch's returned node has one format** — otherwise refused by name (*"branches of 'triage' return 'object' and 'text'; read node.triage.content"*) while `.content` stays |
| `node.x` bare, in `{{ for e in node.x }}` | iterable: each element carries `item` (`id`, `label`, `content` or `value`), the returned node's `content` and `value`, **and every body node by id** (`e.standard_section_draft.content`) | not iterable; refused as at 25 (*"read node.triage.content or .value"*) |
| `node.x \| <renderer pipe>` | refused — a list has no one text (§ 7.1) | allowed when the returned node's format is the pipe's (§ 7.1) |
| `node.x.value` in a condition | refused — a condition decides before the loop runs (the 25 "aggregator in a `when`" rule, carried) | allowed when typed; a flow node whose branches disagree is refused there too |

An `if` or `case` with no `else` and no branch taken is **skipped**: the node reads as null in
a template (`{{ if node.triage }}` guards it, as a skipped node does at 25) and is refused in a
condition; a deliverable whose source is a skipped flow node is skipped with the reason naming
the branch (§ 3). There is no merge node: the `ret` of an `if`/`case` *is* the merge (this
supersedes the 2026-10-18 "`??` in prose" remark). There is no retry node (`onInvalid: retry`
stays the one-rung key) and no sub-workflow call node — out of 26 with those reasons, as on
#1100.

### 1.4 What is static at 26

A `for`'s collection is `snippet.<folder>` or `input.<id>`; a folder item's `if` (§ 2) reads
inputs only; and **at 26 a body holds plain nodes** (decided 2026-10-26) — an `if` or `case`
inside a `for` body, skipping the item's body nodes and keeping the count (the item a row that
says "skipped by condition"), is a follow-up issue of its own; a `for` inside anything waits for
the scheduler redesign (§ 6). So every loop's length is known when the job starts, `TotalBlockCount` stays the stored scalar it has been since #176, and the
engine's skeleton (`ResolveSkeleton`, `SettledFolders`) resolves the 26 program as it resolves
the 25 fans — the scheduler does not change shape in this bump. The sentence a 26 package
meets when it reaches past that is the next bump's name, § 6: a collection expression that reads
a node (*"loop 'notes' iterates node.index.value; a loop over a node's value waits for the
scheduler redesign — iterate an input or a snippet folder"*), a `for` inside a `for` (*"loop
'paragraphs' runs inside loop 'sections'; nesting waits for the scheduler redesign"*), `while`
(*"'while' is not a specVersion 26 flow node; the flow nodes are if, case and for"*).

**Dropped, by decision:** `let` — one Scriban expression evaluated to a typed value, read as
`node.<id>.value`. No public package needs it, and its one real use — a reduce over a loop's
list (`node.sections.value | array.map … | array.sum`) — is a read of a dynamic list, which
waits with the loop that produces it. `for … until` (early exit) waits with `while`, for the
same reason: a count that changes during the run.

### 1.5 The record

A flow node has a row in the record's `nodes` snapshot (§ 10): `kind: if | case | for`, its
expression or collection, its body ids and `ret`; an `if`/`case` row names the **branch taken**
(or "none"); a `for` row carries the item count. A body node's output rows keep their 25 ids
(`nodeId`, `nodeId:itemId`) — no nesting at 26 means no paths. A node that 25 *pruned* when its
`when` failed had no row at all; at 26 it is the untaken branch of an `if` row, which is the
first time the record says why a node did not run.

## 2. Gates as data, keyed `if`

*(#1089, #1090, #1094, #1096, and the 2026-10-19 decisions on #1100.)* A gate is a Scriban
boolean expression on a thing that stays data: it does not move the thing into a body, it says
when the thing is live. Its key is **`if`**, everywhere; `when` outside a `case` is refused with
the replacement (*"deliverable 'plan' declares when, which is retired at specVersion 26; a gate
is keyed if"*).

### 2.1 The six sites

| site | home at 26 | new at 26? |
|---|---|---|
| a macro's own gate | the macro file's header (§ 5) | **yes** — 25 has no `macros[].when`; #1090 |
| a node instance's gate (`nodes[].macros[].if`) | manifest | renamed; #1089 |
| a deliverable's gate (`deliverables[].if`) | manifest | renamed (decision 2026-10-24: a **key**, not an `if` node ahead of the deliverable — supersedes the 2026-10-18 remark on #1100; a deliverable is data with a few rows, and its gate stays on the Deliverables card) |
| a deliverable instance's gate (`deliverables[].macros[].if`) | manifest | renamed |
| a folder item's gate | the item file's header (§ 5) | renamed; #1094 |
| a standalone snippet's gate | the snippet file's header (§ 5) | **yes**; #1094 |

The condition scope is 25's: inputs (typed), a scalar node's `.value`, `run`, `profile`; a
flow node's `.value` when typed (§ 1.3); never a `for` node, never `deliverable.<id>` (§ 3),
never an item of a loop the gate does not sit in. A folder item's `if` reads **inputs only** at
26 (static length, § 1.4). Operators as 25: `==`, `!=`, `and`, `or`, `not`, parentheses;
literals typed by the input or the enum (a literal the enum lacks is refused — #1122's
warning is for templates).

### 2.2 Composition: AND, the source is the ceiling

*(#1090, decided 2026-10-18 20:34 and 20:39.)* A macro has three places that can gate it: its
own `if` (the **source**), an instance's `if` (the **node**), and the text guard around a read
(the **destination** — #1088's canonical `{{~ if … ~}}{{ macro.x.prompt }}{{~ end ~}}`, which
stays legal at 26). They compose by **AND**: a read or a placement narrows the macro's gate and
never widens it; a macro gated off is off everywhere. There is **no (source, destination) pair
table** in the manifest — it would be a second description of an edge the text owns. When the
readers disagree (one text wants the macro under condition A, another under B), the author
leaves the source **ungated** and gates **per occurrence** — the readers-disagree rule — and
the editor's read rows say so ("the macro's if: …" beside each read).

**Inert** means: `.prompt` renders empty **and the mechanics sentence is not appended**;
`.content` renders empty with no blank block; every placement skips (recorded in `excluded[]`
with the gate's text, as an instance's failed `when` is today). A gate that can never hold —
it compares an undeclared input, or a literal outside the enum — is an **error** at publish, as
a 25 condition is. The desk warns when a read's own guard **contradicts** the source's `if`
(`⚠ never fires: the macro's if is input.note_type == "consult"` in the mock-up below).

### 2.3 The instance gate governs the inline read (#1089)

A node that runs a text reading `macro.<id>.prompt` and holds an instance of that macro
renders the read **only where the instance's `if` holds** — the snippet needs no text `if` of
its own, and "Applies when" (now `if`) is the one control. The three questions the issue left:

- **A node with no instance of the macro** (it fans the snippet, but does not place the macro):
  the read renders as today. The read *is* the ask; a placement is not required for a macro to
  be asked for — the model is told to write the marker, and the engine fills it where the
  macro is placed, which may be a deliverable.
- **A node with two instances carrying different gates** (before and after, or two fan items):
  the read renders when **any** instance's `if` holds — a placement that will hold needs the
  model's marker. (The alternative, refusing two gates on one node, would forbid the per-item
  pattern `case item.id` is for.)
- **A read in a prompt run by several nodes**: moot at 26 — a prompt is one node's (§ 4); a
  snippet read by several nodes is judged per running node, as the fan item is.

The mechanics sentence is appended **only when the rendered `.prompt` is non-empty**: an empty
read leaves no dangling "if so, write …".

### 2.4 `forItem` folds into the gate

`forItem: "hpi"` on a node instance becomes `if: "item.id == \"hpi\""`; several items,
`case item.id` in the editor's spelling (one gate, `item.id == "hpi" or item.id == "pmh"` on
the wire — a `case` is the editor's picture, the manifest key is `if`). The key is refused at
26 with the replacement in the sentence. `item.id` in a gate is typed against the loop the
instance's node runs in (the 25 `FanItemIds` check, carried: a literal the folder lacks is
refused naming the items). An instance on a node outside any loop that reads `item.*` is
refused as at 25.

### 2.5 Instruction required only where read inline (#1096)

A macro's `instruction` (in its header, § 5) is **required where `macro.<id>.prompt` is
reachable** by a run — the #1067 walk from node prompts through the snippets and folder items
they fan — and optional otherwise. The 25 sentence *"Macro '<id>' declares no instruction;
required at specVersion 25"* becomes conditional: *"Macro '<id>' is read inline by '<text>' and
declares no instruction; the model is told nothing"*. A macro both read inline and placed
needs one; a macro only placed may carry one, kept with a desk note ("unread — this macro is
placed, not asked for"). The self-read warning (#1067) is unchanged. No migration: every 25
macro has an instruction.

### 2.6 The editor's picture — the macro pane in Scriban words

The pane speaks only the words the manifest does: `if` for every gate, `for item in …` for the
loop an instance sits in, `case`/`when`/`else` for a closed set, `and`/`or`/`not` for joins.
"Always" is an absent `if`. The node pane and the Deliverables card mirror the rows. The
mock-up, as posted on #1100 (2026-10-19 14:41), for `test/general`'s
`letrozole_prescription_text`; the second instance row shows the same placement in the `case`
spelling:

```
┌──────────────────────────────────────────────────────────────────────────────────────────────┐
│ Letrozole prescription text                                     [Discard]  [Remove macro]    │
│ id letrozole_prescription_text · file macros/letrozole_prescription_text.md                  │
├──────────────────────────────────────────────────────────────────────────────────────────────┤
│ label  [Letrozole prescription text        ]                                                 │
│                                                                                              │
│ instruction                                                                                  │
│ ┌──────────────────────────────────────────────────────────────────────────────────────────┐ │
│ │ If the plan prescribes letrozole, the side-effect counselling goes where it belongs in   │ │
│ │ the assessment and plan; otherwise nothing.                                              │ │
│ └──────────────────────────────────────────────────────────────────────────────────────────┘ │
│ ▸ + reference into the instruction                                                           │
│ What the model is told (preview) ────────────────────────────────────────────────────────── │
│ │ If the plan prescribes letrozole, … otherwise nothing.                                     │
│ │ If so, write {{ macro.letrozole_prescription_text.content }} verbatim and nothing else … │
│                                                                                              │
│ if                                                                   ← macros[].if (#1090)   │
│ [☐ not] [input.note_type ▾] [== ▾] [consult ▾]     + and   + or   ( group )                  │
│ The macro's own gate: when it does not hold, its prompt and content render empty and its     │
│ placements skip — everywhere. A read can narrow this, never widen it.                        │
│                                                                                              │
│ WHERE IT GOES                                                                                │
│ Placed on                                                                                    │
│ [attach to a node… ▾]  [attach to a deliverable… ▾]                                          │
│                                                                                              │
│ Applying section instructions  [after the node's output ▾]  for item in snippet.standards ✕  │
│     if   [☐ not] [item.id ▾] [== ▾] [History of Present Illness ▾]     + and   + or          │
│          · and the macro's if                                                                │
│                                                                                              │
│ Applying section instructions  [after the node's output ▾]  for item in snippet.standards ✕  │
│     case item.id                                                                             │
│       when [History of Present Illness ▾]  [Past Medical History ▾]   + when                 │
│       else   (skipped)                                                                       │
│          · and the macro's if                                                                │
│                                                                                              │
│ Consultation note  [prepend ▾]                                                             ✕  │
│     if   Always   · and the macro's if                                                       │
│                                                                                              │
│ Read by                                                                 ← #1085 + #1088      │
│ Snippet 'standards/assessment_plan' · reads .prompt (inline) · items: assessment_plan        │
│     if   Always   · the macro's if: input.note_type == "consult"                             │
│     writes: {{ macro.letrozole_prescription_text.prompt }}                                   │
│                                                                                              │
│ Prompt 'patient_letter' · reads .prompt (inline)                                             │
│     if   [☐ not] [input.note_type ▾] [== ▾] [follow_up ▾]                                    │
│     writes: {{~ if input.note_type == "follow_up" ~}}{{ macro.letrozole_prescription_text.prompt }}{{~ end ~}}
│     ⚠ never fires: the macro's if is input.note_type == "consult"                            │
│                                                                                              │
│ Macro text — a Scriban template rendered where it is read or composed; never .prompt.        │
│ ▸ + reference into the macro text                                                            │
│ Macro text                                        │ Preview                                  │
│ ┌───────────────────────────────────────────────┐ │ Discussed side effects including: hot    │
│ │ Discussed side effects including: hot flashes,│ │ flashes, joint or muscle pain, fatigue,  │
│ │ joint or muscle pain, fatigue, headache,      │ │ headache, nausea, and bone thinning.     │
│ │ nausea, and bone thinning. Advised to monitor │ │ Advised to monitor side effects.         │
│ │ side effects.                                 │ │                                          │
│ └───────────────────────────────────────────────┘ │                                          │
│                                                                                              │
│ ▸ How macros work                                                                            │
└──────────────────────────────────────────────────────────────────────────────────────────────┘
```

Two things the picture fixes that the text above states: the "Prompt 'patient_letter'" row
becomes "Node 'patient_letter''s prompt" at 26 (§ 4), and the `label`, `instruction` and `if`
fields at the top edit the macro file's header (§ 5), not manifest keys — the pane does not
change, its write target does.

## 3. The deliverable names what it composes

*(#1101; #1065 and #1067 survive the fold.)* At 25 a deliverable names one aggregator node,
and the aggregator exists to be named: it has no format, no prompt, no `with`, may not place
macros, and two deliverables may not share one. At 26 the aggregator is gone and the deliverable
carries its **`sources[]`**:

```json
{ "id": "consult", "label": "Consultation note",
  "sources": [ "node.sections", "node.patient_header" ],
  "if": "input.encounter_kind == \"new_patient\"",
  "check": "node.note_coverage",
  "macros": [ { "id": "closing", "at": "append" } ],
  "signature": true }
```

- **`sources[]`** is ordered. Each entry is `node.<id>` naming a node whose text is prose — a
  prose-format node, a `for` node whose returned node is prose (contributing its items'
  composed outputs in item order), or an `if`/`case` node whose every branch returns prose. A
  node of an `object`, `enum`, `array` or `concept_list` format is refused as a source (*"deliverable
  'consult' source node.extract is a concept_list; a deliverable composes prose — read its
  .value from a prose node"*); it reaches the deliverable through a prose node that reads it. A
  `check` node is never a source. **Two deliverables may share a source** — the 25 "each
  deliverable needs its own aggregator" rule was about the aggregator's one-to-one identity and
  goes with it (`example-two-documents`' note and letter stay distinct by their sources).
- **Composition order** is unchanged: prepended deliverable macros → the sources in list order
  (a `for` source's items in item order; a 26 source boundary is the 25 part boundary — the
  bytes a reader hashes do not move) → appended macros → the signature. The engine's
  `ComposeDeliverable` takes a source list where it took an aggregator's; the part model
  (`ForEachPart`/`ScalarPart`) is the same.
- `aggregate` on a node and `deliverables[].node` are refused with the replacement
  (*"deliverable 'consult' declares node, which is retired at specVersion 26; name its sources"*;
  *"node 'assemble_note' aggregates, which is retired at specVersion 26; a deliverable names its
  sources"*).

### 3.1 Reading the composed text: `deliverable.<id>.content`

The read 25 admitted on an aggregator (#1065 — a prompt may read `node.<agg>.content`, the
composed sections *before* deliverable macros and the signature) survives as a **sixth template
object**: **`deliverable.<id>.content`**. Decision 2026-10-24 over the alternative "the `for`
node's `.content` only": a deliverable may mix sources (`example-v12-constructs`' `assemble_note`
composes a fan and a scalar), and the composed text of a mixed list has no other name; the one
new object costs one closed member set.

- Members: **`.content`** only. No `.value` (*"deliverable 'consult' composes text; it has no
  value"*). Refused in any condition — a deliverable's `if`, an instance's, an item's, an `if`
  or `case` node's — with the 25 sentence carried: *"a condition decides before the sections
  compose; read a scalar node"*.
- A read is an **edge**: the reading node waits on every source (and on the sources' own gates
  being judged); a reader of a deliverable that is skipped by its `if` reads null, as a reader
  of a skipped node does. A deliverable may not source a node that reads it — the cycle check
  (#1067's reference rule) takes `deliverable.<id>` as a vertex.
- Hashes: the composed text's hash is what it is today (the aggregator's `agentOutputHash` over
  the composed bytes, now stamped on the deliverable's row — § 10); a reader's downstream-read
  hash covers it as it covered the aggregator's.
- A `for` node whose list is a deliverable's *only* source has the same bytes as that
  deliverable's `.content`; a template may read either. The migration prefers the deliverable
  object where a deliverable exists (§ 13).

### 3.2 Reachability

#1067's two rules keep their meaning with the aggregator gone:

- **Every node feeds a deliverable**: a node must transitively reach a deliverable — through a
  `sources[]` entry, or through a read by a node that does, or by being a body node returned or
  read inside a flow node that does. The 25 sentence *"Node 'x' does not feed any deliverable …
  add it to a deliverable's aggregator"* ends *"… name it in a deliverable's sources"*.
- **Every deliverable includes a loop**: a deliverable's sources transitively include at least
  one `for` node — the "no fan, no consult" rule, which at 25 said *"must transitively include
  at least one forEach source"*. `example-two-documents`' `agg_sections` (an aggregator no
  deliverable names, read by a prompt) is the shape that shows why the rule sits on the
  deliverable and not on the aggregator: it was never a deliverable's, and at 26 it is simply
  the `for` node's list, read as `node.sections.content`.

`check` keeps its third state — a deliverable is produced, skipped (its `if`), or **failed**
(its check) — and `failedDocuments` keeps its shape. The Deliverables card's node select becomes
an ordered sources list; "Composed as" (#1081) stays and now names the sources; "Delivers →" on
the aggregator's node pane goes with the aggregator.

## 4. A node names its prompt file

*(#1102.)* `nodes[].prompt` is a **file path** (`"prompts/assessment.md"`); `prompts[]` is gone.
The 25 model already says it — a template that reads a node is, in effect, that node's prompt;
the declaration added an id for the node to point at and nothing else.

- `prompts[]` is refused with the replacement (*"prompts is retired at specVersion 26; a node
  names its prompt file"*); a `prompt` value that is a declared id rather than a path is refused
  naming the file (*"node 'assessment' names prompt 'assessment'; at specVersion 26 prompt is the
  file: prompts/assessment.md"*). A path is relative to the package root, under any folder; a
  prompt file carries **no header** (§ 5) — a prompt has no label, no instruction and no gate of
  its own (its node's `if`, if any, is a flow node).
- **Sharing is a snippet.** Two nodes that want one text read one snippet from two one-line
  prompts (`{{ snippet.section_prompt.content }}`), or hold two files. **Finding (2026-10-24):**
  no public package shares a prompt — every prompt id is one node's, because 25's migration
  refused *"a prompt bound differently by two nodes"* and v12's `extract-terms` was split then.
  The deferred per-node alias map (v25 design § 2) stays deferred; nothing asks for it.
- The walk's subjects rename: *"Prompt 'p' reads …"* becomes *"Node 'x''s prompt reads …"*;
  the condition and read rows in the editor follow (§ 12). `WorkflowDerivedEdges.WalkAll`
  resolves `node.Prompt` as a file instead of through `promptsById`; the engine's
  `RunPromptNodeActivity` loads by path.
- The editor's Prompts nav goes at 26 (it stays for ≤25 packages); the node pane holds the
  prompt editor and its preview, and `TemplatePreviewScope` has one owner per prompt — the
  "preview through the first node using it" seam (#1095) has nothing left to choose.

## 5. A file says what it is — the JSON header

*(#1105; #1099, #1094 and #1116 are written against it.)* The label, the instruction, the
option and the gate of a snippet or a macro live **in the file**, in a header the format reads
and Scriban never sees. The manifest keeps what only the manifest can know: the id, the order,
the placements, the nodes. Decision 2026-10-24 over #1094's `{ file, label, when }` declaration
object (2026-10-19): one home per fact, and the file is the one that travels with the text.

### 5.1 The header

```
{
  "label": "Assessment and plan",
  "if": "input.note_type == \"consult\"",
  "instruction": "If the plan prescribes letrozole, …"
}
The text begins on the line after the closing brace.
```

- The first line of the file is exactly `{`; the header ends at the first line that is exactly
  `}`; between them is one JSON object. A BOM and CRLF are tolerated. The text is everything
  after the closing line (one newline consumed).
- A snippet or macro file with no header is refused naming the missing field (*"snippets/
  standards/hpi.md has no header; a snippet file opens with { "label": … }"*). A header on a
  **prompt** file is refused (a prompt has none of these facts).
- **Closed key set.** Snippet: `label` (required), `if`. Macro: `label` (required),
  `instruction` (required where read inline, § 2.5), `optional`, `default`, `if`. Anything else
  is refused naming the key; the reserved names `id`, `file`, `content`, `value`, `prompt` are
  refused by name (they are the object's members, not the file's facts).
- **One home.** A fact in the header *and* the manifest (`index.json` `name`, `macros[].label`)
  is refused naming both — there is no fallback order to learn.
- **Scriban never sees it.** The text that renders is the text after the header; `{{` in a
  header value is a string. The **stamp is unchanged**: files hash whole, header included — a
  label change is a text change, which is what it is.
- The keys stay flat: no `gate: { if }`, no `schemas: { refs, custom }` grouping — "keys do not
  grow dots" — and the header's key set is closed at 26 (a package-defined key set would be a
  later bump).

### 5.2 What moves, what stays

| 25 | 26 |
|---|---|
| `index.json` item `{ id, name, file, when? }` | `[{ id, file }]` — an **ordered** list; `name` → the file's `label`, `when` → the file's `if` |
| `macros[]` entry `{ id, label, file, optional?, default?, instruction? }` | `{ id, file }`; the rest in the file's header, plus `if` (§ 2.1) |
| `snippets` `"<id>": "<file>"` / `"<dir>/"` | unchanged — the id → file (or folder) map; a standalone snippet's `label` and `if` in its header |
| `item.name`, `snippet.<id>.name`, `macro.<id>.name` | **`.label`** — `name` refused with the replacement |
| — | `macro.<id>.instruction` readable (what the model will be told, as text) |

The template objects' member sets (§ 9) rename `name` to `label` everywhere — one word for the
human-facing name, the word the manifest, the editor and the Create page already use for inputs
and deliverables. The #1100 read tables wrote `item (id, name)`; they read `item (id, label)`
at 26.

### 5.3 Folders — one shape for snippets and macros (#1099)

A folder is a directory with an `index.json` holding the ordered `[{ id, file }]` list; **its
items are snippets or macros by what their headers say** — an `instruction` key makes a macro.
A macro folder is declared as a snippet folder is: `"macros": { "closing": "macros/closing.md",
"section_notes": "macros/section_notes/" }` (the `macros` map takes a folder value at 26, as
`snippets` does at 25). Decided on #1099 (2026-10-19): the index carries the item order — the
order of `{{ for m in macro.section_notes }}` and of any per-item placement — and #1103's
arrows reorder it.

The issue's two open questions, settled here:

- **Instruction per item**, in each item's header — not a folder-level default. A macro's
  instruction is what the model is told for *that* text; a shared sentence over different texts
  is the kind of thing that reads right once and wrong on the third item. The editor may offer
  "copy to the other items".
- **Read, not placed, at 26.** `macro.<folder>` is iterated (`{{ for m in macro.<folder> }}`,
  each `m` with `.id`, `.label`, `.content`, `.prompt`, `.instruction`) and an item is read
  directly (§ 5.4); placing a folder item as an instance (`macros: [{ id: "section_notes.hpi",
  at: "append" }]`) is **deferred by name** until a package needs it — the id's shape
  (`<folder>.<item>` or flat), the placement's identity and reachability through a folder are
  three decisions that want a real use. Reachability (#1067) for a folder item is through reads.

### 5.4 A folder item readable directly (#1116)

`snippet.<folder>.<item>` and `macro.<folder>.<item>` resolve to the item with the folder's
member set — `.id`, `.label`, `.content`; `.prompt` and `.instruction` for a macro item. The 25
refusal *"reads snippet.<folder>.<item>; a folder is iterated"* goes. An undeclared item is
refused naming the folder's items. The read is recorded as `<folder>/<item>`
(`WorkflowDerivedEdges.SnippetItemKey`), is an edge like any snippet read, and **the item's `if`
governs it** (#1094): an item gated off renders empty where it is read directly, exactly as it
is filtered out of every loop over its folder. `item.*` is refused in a standalone snippet's
`if` and evaluated with the item's own id bound in an item's `if`.

A gated-off snippet or item that is a prompt's *only* content leaves an empty prompt; the desk
says so (the 25 "empty prompt" case). A gated snippet is still **used** — the gate is a run-time
fact, reachability is a publish-time one.

## 6. The next bump, by name — dynamic fan sizes and the scheduler redesign

*(#1062; and the four flow shapes § 1.4 refuses.)* The invariant 25 and 26 share: **a loop's
length is known when the job starts.** A folder's items are filtered by `if`s over inputs, an
array input's elements are counted, `WorkflowPackageBlocks.Resolve` lays out the rows, and
`TotalBlockCount` is written once on the entity and never derived (#176). The record's
`collections` rosters, the SSE `snapshot`'s total and the progress bar all rest on that scalar.

26 keeps it, by decision (2026-10-24), because no public package needs a count that changes
during a run and the redesign is a release of its own: the orchestrator's totals
(`totalBlockCount`, `totalNodeCount`) become per-loop scalars resolved when a loop's collection
settles; the entity takes a resolution event (*"N sections, now M"*); the SSE stream carries it
and the Create page's progress re-totals; an item excluded by a condition that read a node is a
named state, *skipped by condition*, recorded with the condition's text and the value it read; a
`for` over a `for` node's list gives item rows a **path** (`sections/hpi/paragraphs/2`) and SSE
block ids carry it; the record-storage version bumps for the new rows and the event. That is
the precondition for every shape 26 names and refuses:

| shape | why it waits |
|---|---|
| a `for` over `node.<id>.value` (a node-valued collection) | the count is the node's output |
| a folder item's `if` reading `node.<id>` | the count is a node's output (#1062's own ask; its `when` is `if` now, and "the aggregator keeps order over the resolved set" reads "the `for` node's list keeps item order") |
| a `for` inside a `for` | rows take paths; one scalar per loop (#1062's out-of-scope "a fan of fans" is this shape — it is the goal there, not an exclusion) |
| `while` — repeat a body until a computed check holds, bounded | passes are a count that grows; open on #1100: what a template reads per pass (`.content` = the last pass's returned node; `.passes`?), the record per pass, `onInvalid: retry`'s relation to it |
| `for … until` (early exit) | a count that shrinks |
| `let` (a computed value; the reduce over a loop's list) | its one use reads a dynamic list |

Each is refused at 26 with a sentence that names the redesign (§ 1.4), and each is a row of the
next design doc, not of this one.

## 7. The smaller sections

### 7.1 Renderer pipes take the node (#1059)

`{{ node.extract | concept_context }}` replaces `{{ node.extract.content | concept_context }}`.
Today `ApplyPipe` receives the `.content` string and finds the node by **reference equality**
with an output's text — a lookup that works because `.content` is the very string the node
holds, and that is the only reason the operand is spelled `.content`. At 26 the pipe takes the
node object (`node.<id>` as a `ClosedObject`); the lookup goes.

- The old spelling is refused at 26 with the replacement (*"pipes node.extract.content through
  concept_context; at specVersion 26 a renderer pipe takes the node — node.extract |
  concept_context"*); no dual acceptance. `.value` as an operand is refused (no node identity).
- A **flow node** as operand is allowed when its returned node's format is the pipe's (an `if`
  over two `concept_list` nodes pipes); a `for` node is refused — a list has no one text
  (*"pipes node.sections through concept_bullets; a loop is a list — pipe a body node inside
  it, or read node.sections.content"*).
- Migration rewrites the 25 public sites mechanically (§ 13). No provenance change: the pipe's
  rendered bytes are what they were.

### 7.2 Audio transcripts — speech-to-text (#1063)

Engine, provenance note and Create page; **not a manifest change**. The `transcript` input
(since v13; a text transcript file since #994) accepts audio — WAV, MP3, M4A, OGG by sniff —
when `SpeechToText__Endpoint` is set, behind an `ISpeechToText` seam as OCR sits behind
`DocumentExtraction__OcrEndpoint`; Azure AI Speech in Canada. The result goes through the same
`TranscriptTurns` rule as VTT/SRT, so `textSha256` is over turns; `extractor` is
`speech/<service>+<version>`; `fileSha256` is over the audio bytes, which never rest.
Outcomes, named: `too-long`, `stt-unavailable`, `stt-low-confidence` (the per-account gate as
OCR's), `busy`. **No new record field** — duration and confidence are telemetry, not provenance
— so **no provenance bump**; the provenance doc gains a sentence naming the extractor family.
The Create page's transcript dropzone takes audio only when configured, and says so. Out of
scope: live capture, diarisation beyond the service's, translation.

### 7.3 Enum value labels

Left v26 on 2026-10-24: #1061 holds the shape (`{ value, label }` entries, `input.<id>.label`,
the Create page showing the label) and sits in no milestone and no planned version. At 26
`values` entries are strings and `input.<id>` has no `.label` member, as at 25.

## 8. Refused by name at ≥ 26

Each below 26 keeps its frozen rules; each at ≥ 26 is a sentence naming the key and the
replacement — the 25 form, *"`<key>` is retired at specVersion 26; <replacement>."* — and a
schema-catchable conformance vector where the key leaves the schema (`SHAPE_BLIND` where it
does not).

| Refused | Replaced by |
|---|---|
| `nodes[].forEach` | a `for` node whose body holds the node (§ 1) |
| `nodes[].when` | an `if` node whose body holds the node (§ 1) |
| `nodes[].aggregate` | `deliverables[].sources[]` (§ 3) |
| `deliverables[].node` | `deliverables[].sources[]` (§ 3) |
| `prompts[]`; `nodes[].prompt: <prompt id>` | `nodes[].prompt: "<file path>"` (§ 4) |
| `nodes[].macros[].forItem` | the instance's `if` on `item.id` (§ 2.4) |
| `when` on a node instance, a deliverable, a deliverable instance, a folder item | `if` (§ 2); `when` is a `case` branch label only |
| `index.json` item `name`, `when` | the item file's header `label`, `if` (§ 5) |
| `macros[].label`, `instruction`, `optional`, `default` in the manifest | the macro file's header (§ 5) |
| a snippet, folder item or macro file with no header; a header on a prompt file | the header (§ 5.1) |
| `item.name`, `snippet.<id>.name`, `macro.<id>.name` | `.label` (§ 5.2) |
| `{{ node.x.content \| <renderer pipe> }}`; `node.x.value \| <pipe>` | `{{ node.x \| <pipe> }}` (§ 7.1) |
| a flow node without `ret`; a branch without `ret` | `ret` (§ 1.2) |
| a body node read by bare id from outside its flow node; a node in two bodies | the flow node's id (§ 1.2) |
| a `case` value the set lacks; a value in two branches | the enum's values (§ 1.1) |
| `while`; a `for` inside a `for`; a `for` over `node.<id>.value`; a folder item's `if` reading a node; `for … until`; `let` | the next bump, named in the sentence (§ 6) |
| `node.<for>.content \| <pipe>`; `deliverable.<id>` in a condition; `deliverable.<id>.value` | § 7.1; § 3.1 |

## 9. The template objects at 26

The closed member sets, rewritten (`TemplateObjects.All`, `TemplateMembers`):

| object | members at 26 | change |
|---|---|---|
| `input.<id>` (+ typed fields) | as 25 | — |
| `node.<id>` | `content`, `value`; a flow node's are its returned node's (§ 1.3); a `for` node iterable with `item` + body nodes per element; a body node **scoped** to its flow node | flow nodes; scoping |
| `snippet.<id>` | `id`, **`label`**, `content` | `name` → `label` |
| `snippet.<folder>` | iterable; **`snippet.<folder>.<item>`** with the item members | direct item read (§ 5.4) |
| `item` | `id`, **`label`**, `content` (a folder) / `value` (an array input) | `name` → `label` |
| `macro.<id>` | `id`, **`label`**, `content`, `prompt`, **`instruction`** | `name` → `label`; `instruction` readable |
| `macro.<folder>` | iterable; `macro.<folder>.<item>` with the macro members | direct item read (§ 5.4) |
| **`deliverable.<id>`** | `content` | **new** (§ 3.1) |
| `run`, `profile` | as 25 | — |

The retired roots (`data`, `bindings`, `variables`) stay retired; `result`/`results` as a root
was never one.

## 10. The record and the registries

**Provenance record** (`provenance-record.md`; `recordStorageVersions` gains **8**). The
`nodes` snapshot loses `forEach`, `aggregate` and `promptId`, gains `prompt` (the path) and the
flow rows: `{ id, kind: if | case | for, expression | collection, body: [ids], ret, branch?:
"<values>" | "else" | "none", count? }`. The `nodeOutputs` ids stay `nodeId` and
`nodeId:itemId` (no nesting at 26 — no paths). A deliverable's row carries its `sources[]` and
the composed text's hash (the aggregator's `agentOutputHash`, moved); `skippedDocuments` keeps
its fixed-form reason and gains the branch spelling for a source skipped by a flow node
(*"source node.triage took no branch"*); `failedDocuments`, `excludedMacros` and `excluded[]`
unchanged in shape — an instance excluded by its `if` carries the gate's text as it carries the
`when`'s today. **Unchanged:** `effectiveInputHash` (and its version), the node hash ladder
(`NodeHashVersion` 6 — a label, an instruction and a header are declaration, and files hash
whole), the stamp. The reason the storage version bumps is the flow rows and the pruned node
that now has one (§ 1.5), not any hash.

**Package-format registry.** `package-format-v26.md` — a full grammar, not a delta, as the 25
spec is — with headings mirroring it: Objects in templates (+ `deliverable`, the item path, the
scoped body node), **Flow nodes** (new), **Gates** (new; folds 25's "Conditions and references"),
Formats (unchanged; the node), Snippets and macros (**the header**, folders one shape), Deliverables
(sources, the composed read), Refused at 26, Validation, Migration (25 → 26). The generated
schema (`schemas/package-format-v26.schema.json`, from `PackageFormatSchemaExport`); the 5–25
schemas and vectors stay byte-identical. `conformance/v26/` — valid: `v26-minimal` (= the
migrated 25 minimal, the baseline rule 25 settled), one vector per flow node (`for` over a
folder, `for` over an array input, `if` with and without `else`, `case` with several values and
`else`), a body node read inside its loop and the flow node read outside, `sources[]` mixing a
`for` and a scalar, `deliverable.<id>.content` read by a node, a direct item read under an item
`if`, a macro's own `if` with an instance `if` (AND), a header carrying every key, a pipe on a
node and on an `if` node, two deliverables sharing a source; invalid: one per § 8 row, plus
missing `ret`, a bare body-node read, a node in two bodies, a `case` value outside the set, a
value in two branches, nested `for`, `while`, a node-valued collection, an item `if` reading a
node, two homes for a field, `when` outside a `case`, a header on a prompt, `deliverable` in a
condition, a `for` node piped, a non-prose source, a reader that a deliverable sources (the
cycle); gates under `v25/` (*"… requires specVersion 26"*) for the header, the flow keys,
`sources`, `if`, `deliverable.`. The generator is a `tests/ConformanceFixtureExport.V26.cs`
partial beside the 25 one; `SHAPE_BLIND` gains the cross-field rows.

**Agents registry (formats catalogue):** nothing. No format changes; `terms_subset`'s
`failWith` is noted, not renamed.

## 11. The validator's sentences

The 25 sentences in `WorkflowPackageValidator.V25.cs` that name a retired key, and their fate.
A sentence that names a thing 26 keeps is unchanged and not listed.

| 25 sentence (abridged) | at 26 |
|---|---|
| *Node 'x' aggregates and must not declare {format \| prompt \| with \| onInvalid \| forEach}; an aggregator is structural.* | gone — no aggregator; `aggregate` meets § 8's retirement sentence |
| *Aggregator node 'x' source '…' must be node.<id>* / *aggregates itself* / *references unknown node* | become the **source** sentences: *"deliverable 'd' source '…' must be node.<id>"*, *"… names unknown node"*, *"… sources a node that reads it"* (§ 3) |
| *Node 'x' must declare a prompt; format … is …* / *names prompt 'p', which the package does not declare* | *"must name a prompt file"* / *"names prompt file '…', which the package does not hold"* (§ 4) |
| *Node 'x' forEach '…' must be snippet.<folder> or input.<id>* / *fans snippet 'id', which is a file* / *fans undeclared input* / *fans input 'id', which is a {type}; only an array can be fanned* | become the **`for`** sentences on the flow node: *"loop 'l' iterates '…'; a loop iterates snippet.<folder> or input.<id>"*, *"… iterates snippet 'id', which is a file, not a folder"*, *"… iterates input 'id', which is a {type}; only an array"* (§ 1.1) |
| *Node 'x' reads node 'y', which fans; aggregation is explicit — collect it through an aggregator node.* | *"node 'x' reads node 'y', which runs inside loop 'l'; read node.l"* (§ 1.2) |
| *Node 'x' reads node 'y' across fans ('a' vs 'b'); two fans align only over one source.* | *"… across loops ('a' vs 'b'); body nodes of one loop read one another"* — the alignment rule is now the body |
| *{condition} reads node.x, which aggregates; a condition decides before the sections compose — read a scalar node.* | the same sentence over **`deliverable.<id>`** and a `for` node (§ 2.1, § 3.1) |
| *{condition} reads …; a folder item's when reads inputs only, so the folder's size is known at job start.* | *"… a folder item's if reads inputs only …"* — and the § 6 sentence for a `for` collection reading a node |
| *Node 'x' aggregates and must not place macros; place them on the nodes it aggregates or on the deliverable.* | gone |
| *{where} is placed twice ({position}, forItem '…'); one identity (id, at, forItem) once …* | *"… placed twice (at); one identity (id, at) once — two gates to one text is one instance with if: a or b"* (§ 2.4) |
| *{where} anchors to fan item '…', but 'x' does not fan a snippet folder* / *which the folder … does not contain* | the gate's typing: *"… compares item.id, but node 'x' runs in no loop"* / *"… compares item.id to '…', which loop 'l' does not contain (items: …)"* (the walker's 25 sentence, carried) |
| *Node 'x' fans snippet folder 'f'; items … carry no macro.m.prompt read …* (warning) | *"loop 'l' over 'f': items … carry no …"* — unchanged in meaning (#1067) |
| *{deliverable} node '…' must be node.<id>* / *must reference an aggregator node* / *Deliverables 'a' and 'b' share node 'x': each deliverable needs its own aggregator.* | the source sentences above; the sharing rule is **gone** (§ 3) |
| *{deliverable macro} reads node 'x', which fans; a deliverable macro reads scalar nodes and aggregators …* | *"… which runs inside loop 'l'; a deliverable macro reads scalar nodes, flow nodes and deliverable.<id>.content — place the macro on a body node to read its items"* |
| *Deliverable 'd' must transitively include at least one forEach source: a deliverable with no fan has no consult. {remedy}* | *"… at least one loop (a for node) among its sources …"*; the remedy names the package's `for` nodes or says to add one (§ 3.2) |
| *Node 'x' does not feed {the \| any} deliverable … add it to {the \| a} deliverable's aggregator.* | *"… name it in {the \| a} deliverable's sources, or read it from a node that is"* (§ 3.2) |
| *Macro 'm' declares no instruction; required at specVersion 25.* | conditional on reachability (§ 2.5) |
| the condition subjects *Node 'x' condition* / *Node 'x' macro 'm' condition* / *Snippet folder 'f' item 'i' condition* / *Deliverable 'd' condition* / *Deliverable 'd' macro 'm' condition* | *"… if"* in place of *"… condition"*, plus *"Macro 'm' if"*, *"Snippet 's' if"*, *"Flow node 'x' if"* / *"… case"* |
| the walker's *reads node 'x' bare; read ….content or ….value* | stays for a bare read in text; a bare node **as a pipe operand** is the 26 spelling (§ 7.1) |
| the walker's *pipes … through p; a renderer pipe renders a node's text (node.<id>.content)* | *"… a renderer pipe takes the node (node.<id> \| p)"* |

New sentences are quoted where they arise: § 1.1–1.4, § 2.1–2.5, § 3, § 4, § 5.1, § 7.1.

*(#1150, 2026-10-25 — the path lands: `ValidateAt26` opens with the retirements whose replacement
is a manifest shape — `prompts`, a node's `forEach`/`when`/`aggregate`, an instance's `forItem` and
`when`, a deliverable's `node` and `when`, a deliverable instance's `when`, a folder item's `when` —
then hands the rest to the 25 pipeline, which holds at ≥ 25; behind the unchanged accepted set,
reached through the internal seam as A4 was, no vectors until the bump. The retirements whose
replacement does not exist yet follow with it: the folder index's `name`/`when` and the manifest's
macro fields with the header (#1105), a prompt id with the prompt path (#1102), `.name` and the
pipe operand with the walker (#1105, #1059). The manifest model — the 26 record members and the
"requires specVersion 26" gates — is #1165, the 26 counterpart of A3.)*

*(#1165, 2026-10-25 — the model lands: a node carries `for`, `if`, `case`, `then`, `else`, `branches`,
`nodes` and `ret` (`WorkflowFlowBodySpec(nodes, ret)` for a body, `WorkflowCaseBranchSpec(when, nodes, ret)`
for a branch), a deliverable `sources` and `if`, a node or deliverable instance `if` (counted by `IsBare`,
written after `when`), a folder index item `if` carried into the resolved item; the schema generator hides
every member below 26 so the published 5–25 schemas do not move a byte, and closes the flow bodies at 26;
`ValidateV26Members` refuses each by name below 26 — *"Node 'x' declares for, which requires specVersion
26."*, the noun following the key the version carries — from the ≤24 chain and from `ValidateAt25`.
`WorkflowDeliverableSpec.Node` stays the positional it is until #1101 settles the record; the 26 schema file
and the vectors are exported at #1149.)*

*(#1100, PR 1 of 2, 2026-10-26 — the structure lands: the 26 path has its own pipeline, the flow nodes
resolved first. Sentences, as built: one flow key (*"declares for, if; a node is one flow node — for, if or
case"*); structural (*"declares for and format; a flow node is structural"*); the keys a kind carries; a
loop's collection (*"Loop 'l' iterates '…'; a loop iterates "item in snippet.<folder>" or "item in
input.<id>""*, the file/folder/input/array sentences, *"iterates node.x.value; a loop over a node's value
waits for the scheduler redesign"*); bodies declared, plain, listed once, attached once (*"runs inside loop
'l' and inside node 'triage'; a node runs in one body"*; *"Loop 'paragraphs' runs inside loop 'sections';
nesting waits…"*; *"Node 'triage' runs inside loop 'sections'; at specVersion 26 a body holds plain nodes —
a flow node inside a body is a later issue"*); `ret` required and a member; a case over an enum input or an
enum-format node's value with each value in the set once; the scope on the edges (*"reads node 'y', which
runs inside loop 'l'; read node.l"*, *"across loops ('a' vs 'b')"*, *"reads node.l, the loop it runs in"*);
*"reads node.l, which loops; a condition decides before the loop runs"*; *"in loop 'l' is neither returned
nor read"*. An `if` node's expression is a `Flow`-site condition whose reads are the flow node's edges and
a gating read; a `case` subject is read structurally. Until PR 2 a loop reads as text (the aggregator's
shape) and an `if`/`case` as its returned node's format when every branch returns one; PR 2 brings
`{{ for e in node.<loop> }}`, `.value` across branches, the probe's iterable loop.)*

*(#1100, PR 2 of 2, 2026-10-26 — the reads land, as § 1.3 reads them. The walker's node shape carries a
flow (`TemplateFlowShape`: the returned node, a loop's fan and body, an if/case's disagreement); a loop is
**iterable** — `{{ for e in node.sections }}` — and its element carries `item` (typed by the loop's fan),
`content` and `value` (the returned node's) and every body node by id, every read recorded as the loop's (the
flow node is the only name the outside sees); `node.<loop>.content` is text, `node.<loop>.value` the array of
the returned node's values (*"reads node.sections.value, but loop 'sections' returns 'section_notes' of
format 'prose', which is text and has no value; read node.sections.content or iterate node.sections"*), a bare
loop says *"… or iterate it ({{ for e in node.sections }})"*, a bare element names its members; an `if`/`case`
reads as its returned node when the branches agree and refuses `.value` and a pipe by name when they do not
(*"branches of 'triage' return 'object' and 'text'; read node.triage.content"*); a renderer pipe on a loop is
refused (*"a list has no one text; pipe the returned node inside the loop …"*) and on its element recorded for
the loop. A body node read from outside its scope is refused where it is written (*"Prompt 'assessment'
reads node 'section_notes', which runs inside loop 'sections'; read node.sections"* — the context's `Bodies`
and the template's `Scope`); a deferred text's read is judged at its reader by the edge rule. The flow nodes
are resolved once for every walk (`WorkflowFlowNodes`, shared by the validator and by the engine's and the
editor's `PrepareNodes`), and the probe gives a loop a list that also reads as text and as the array. The DAG
diagram's loop drawing is #1160's. #1100 closes.)*

*(#1101, 2026-10-26 — the deliverable names what it composes, as § 3 says. `deliverables[].sources[]` is
required, each `node.<id>` declared once and prose — a plain node of a text format (*"source node.extract is a
concept_list; a deliverable composes prose — read its .value from a prose node"*; a check: *"a check gates a
deliverable (check: node.coverage), it is no text"*), a loop whose returned node is prose (*"loops over
'per_item' of format 'concept_list'; a deliverable composes prose"*), an `if`/`case` whose every branch returns
prose (*"returns 'object' and 'text'; …"*), never a body node by name (*"runs inside loop 'sections'; name
node.sections"*); two deliverables may share a source. The sixth object **`deliverable.<id>.content`** is the
walker's: text, recorded on the references and expanded by the walk to the deliverable's sources (a reader
waits on them; a source reading its deliverable is a cycle); no `.value`, no bare read, no renderer pipe
(*"composes sections; … renders a … node's text"*), never in a condition (*"a condition decides before the
sections compose"*). The word is reserved at every version and resolves where the context carries deliverables
(26); below, the unchanged "not an object" sentence. The deliverable's `if` and its instances' `if` are walked
at their sites. Reachability is rooted at the sources: *"Deliverable 'd' must transitively include at least one
loop (a for node): a deliverable with no loop has no consult. Name a for node among its sources or read one
from a source (loops in this package: …)"*; *"Node 'x' does not feed the deliverable: every node must
transitively reach 'd's sources. Read it from a node that does, or name it among the deliverable's sources"*.
`WorkflowDeliverableSpec.Node` is nullable (the ≤25 aggregator; its readers take the empty string at 26). The
minimal 26 package is valid but for the prompts' declaration (#1102). The engine's composition from sources is
#1155, the Deliverables card #1161, the preview's token #1162.)*

## 12. The editor

The Milestone 31 panes land at 25 and are the rows 26 re-keys. What changes **label only**,
what changes **shape**, and what is new — for a package at 26 (a ≤25 package keeps its panes
untouched, as ≤24 does today):

**Label only** (one `ConditionField` with a new resting label; the builder, operators and the
`ConditionScope` are 25's): "Applies when" (node instance) → **if**; "Gated when" (deliverable
instance) → **if**; "Produced when" (deliverable) → **if**, resting "Always produced" → "Always";
"Included when" (folder item) → **if**; "Included when" on a text read's own guard (#1088/#1092)
keeps its sense (the guard is text) and the row shows *"· and the macro's if"* beside it.

**Shape:**

- **"Runs when"** (the node's `when`) goes. The palette gains the three flow nodes; a node is
  placed inside one by the node pane's new "in [sections ▾]" picker (which replaces the
  new-node "for each" select), and the `if`/`case` pane holds the expression builder, the branch
  table (the #1091/#1120 case table, now on a node) and a **`ret` picker** per branch — empty
  until chosen, blocking publish (explicit init).
- **The `forItem` picker** (node instance "— every item —") goes; its row is the gate, with the
  `case item.id` spelling offered when the gate is a disjunction of `item.id` equalities.
- **The aggregator node** goes from the palette; the Deliverables card's node select becomes an
  **ordered sources list** (pick a node, arrows, remove); "Composed as" (#1081) names the sources;
  "Delivers →" on the node pane goes, replaced by *"Sourced by deliverable 'd'"* on a node that
  is one.
- **The Prompts nav** goes at 26; the node pane holds the prompt editor, its Reads rows and its
  preview (one `TemplatePreviewScope` owner). "Prompt 'p' reads" rows read "Node 'x''s prompt".
- **Label, instruction, optional, default and `if`** of a snippet, folder item or macro are
  edited where they are today (the macro pane's top fields, the folder item's row, the snippet
  pane's header); their **write target is the file's header**, not the manifest — the editor
  serialises the header and the text together, and a header the editor did not write (hand-
  edited, unknown key) is shown as the validator's sentence.
- **Folder items** show their kind (snippet or macro) from the header; `macro.<folder>` folders
  appear in the Macros nav group with the Snippets' folder idiom (#1103's arrows).
- **The upgrade button**: "Upgrade to specVersion 26" on any 25 package (below 25, the chain
  runs 24 → 25 → 26, one publish, one notice naming the steps); a refusal (an unfillable `ret`,
  § 13) is said in the toolbar and leaves the package as loaded.

The macro pane is § 2.6's picture.

## 13. Migration 25 → 26 (mechanical; byte-identical in what the model sees)

| At 25 | At 26 |
|---|---|
| N nodes with `forEach: snippet.standards` | **one** `for` node `{ id: "standards", for: "item in snippet.standards", nodes: [the N], ret }` — the id is the collection's (`standards`, `prior_notes`, `sections`); if a node already holds it, `<collection>_loop`, named in the notice |
| the `for`'s `ret` | the body node an aggregator sourced, when exactly one aggregator sources exactly one body node of the loop (every public package: `section_instructions`, `plan_section`, `consult_note_section`, `summarise_note`, …); **otherwise refused**, nothing applied: *"loop 'standards' has no single returned node; name ret"* |
| a node with `when: C` | an `if` node `{ id: "<node>_if", if: C, then: { nodes: [node], ret: node.<node> } }`; sibling nodes sharing C fold into one `if` (`ret` = refused unless one — the public packages have no node `when`) |
| `aggregate: ["node.a", "node.b"]` + `deliverables[].node: node.agg` | `deliverables[].sources: ["node.<loop of a>", "node.b"]` — a fanned source becomes its loop, a scalar stays; the aggregator node is removed |
| a template's `node.agg.content` where a deliverable names `agg` | `deliverable.<id>.content` (v12's `extract-note-terms`, `extract-letter-terms`) |
| a template's `node.agg.content` where **no** deliverable names `agg` and `agg` has one fanned source | `node.<loop>.content` (two-documents' `agg_sections` → `node.standards.content`); otherwise refused naming the aggregator |
| `when` on an instance, a deliverable, a deliverable instance, a folder item | `if`, the expression unchanged |
| `forItem: "hpi"` (+ `when: C`) | `if: "item.id == \"hpi\""` (`… and (C)`) |
| `prompts[]` + `nodes[].prompt: <id>` | `nodes[].prompt: "<the prompt's file>"`; `prompts[]` removed; a prompt bound by two nodes → refused (none exist) |
| `{{ node.x.content \| concept_bullets }}` | `{{ node.x \| concept_bullets }}` (25 sites) |
| `index.json` `{ id, name, file, when? }` | `[{ id, file }]`; each item file gains the header `{ "label": <name>, "if": <when> }` |
| `macros[]` `{ id, label, file, optional?, default?, instruction? }` | `{ id, file }`; the file gains the header with the rest |
| a standalone snippet file | gains `{ "label": <id humanised> }` — the one place the migration **invents** a value, named in the notice (a snippet had no label at 25; the editor shows it for review) |
| `item.name`, `.name` in templates | `.label` |

Rules: a pure library rewriter beside the 24 → 25 one — `WorkflowPackageMigration25To26`
(manifest and files in, manifest and files at 26 out, one notice per rewrite, refusals that
apply nothing) — the editor's button and `scripts/migrate-workflow-package.cs` both run it
(the script gains the 26 stage after its 25 one). Round-trip tests as
`WorkflowPackageMigration24To25Tests`: the retired keys are gone, every gate is `if`, the
migrated package validates at 26, the notices name every rewrite, and **a migrated prompt
renders the bytes the 25 one did** (the `for` node's `.content` and `deliverable.<id>.content`
are the aggregator's bytes; the renamed pipe operand renders the same text). Conformance:
`v26-minimal-is-the-migrated-v25-minimal`. The seven public packages republish at 26 after the
engine release carries it, as the 25 migration did (2026-10-17/18); `scripts/validate-workflow-package.cs`
and `stamp-workflow-package.cs` read headers.

## 14. Phasing

The 25 shape, one layer per PR, each green on its own; the bump is the last sub-issue.

- **A — format** (`Consultologist.PackageFormat`, the package-format registry): the flow nodes
  and body scoping in the manifest records and the walk; `ret`; the gate key and the macro's
  own `if`; the header reader; `sources[]` and the `deliverable` object; the prompt path; the
  pipe operand; every § 8 refusal; the § 11 sentences; the migration rewriter (§ 13);
  `package-format-v26.md`, the schema and vectors (§ 10) — `AcceptedSpecVersions` += 26 **last**
  (behind the unchanged set until then, as A4 was).
- **B — engine + provenance**: flow-node scheduling over the **same skeleton** (a `for` is the
  fan it replaces; an `if`/`case` is the deciding set it replaces; body scoping in
  `NodeDependencies`); `deliverable` composition from `sources[]` (`TryCompleteAggregatorsAsync`
  becomes the deliverable's compose step; `pendingDeliverables` keyed by deliverable); the
  header stripped before render; the prompt loaded by path; `ApplyPipe` on the object; record
  storage 8 (§ 10); `SupportedSpecVersions` += 26; an engine release.
- **C — editor**: § 12; the one-button upgrade.
- **The bump** (the 26 `#1018`, filed from this doc): both sets and `Templates.NewestSpecVersion`
  += 26 together; the registry lists 26; the seven packages republish; the pins move.
- **Siblings:** #1063 audio (engine + Create page) rides B's release or its own; #1144
  (`array<text>` element order) is an engine and provenance matter, **not a 26 item**; the
  Milestone 31 editor rows keep landing at 25 and are re-keyed by C.

**Done when** (#1104): this doc exists with a decision or a named open question for every row of
the port list — it does; each Milestone 30 issue links its section; the bump issue is filed.

## 15. Deferred, by name

- **Dynamic fan sizes — the scheduler redesign** (#1062; § 6): a count that changes during a
  run; the precondition for everything below it.
- **`while`** — a body repeated until a computed check holds, bounded; its per-pass reads and
  record shape open on #1100 (§ 6).
- **A `for` over a node's value**; **a folder item's `if` reading a node** (§ 6).
- **A `for` inside a `for`** — rows take paths (§ 6).
- **`for … until`**, **`let`** (§ 1.4, § 6).
- **Placing a macro-folder item** as an instance — the id shape, the identity, reachability
  through a folder (§ 5.3).
- A per-node alias map for one prompt with different upstream nodes (v25 § 2) — nothing asks for
  it (§ 4).
- **Enum value labels** — #1061, no planned version (§ 7.3).
- A package-defined header key set; `schemas: { refs, custom }` grouping — "keys do not grow
  dots" (§ 5.1).
- The `retry` policy's repair message; `onInvalid: retry` looping at all (today it fails as
  `fail` does — a bounded repeat is `while`'s cousin and waits with it).
- Labels on a classification format's answer values; localisation (#1061's out-of-scope).
- Carried from 25 where still open: a schemaless `type: json` input; `extends` for a
  package-local format; a `boolean` catalogue format; strict content-addressed agents.
