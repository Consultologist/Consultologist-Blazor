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
says "skipped by condition"), is a follow-up issue of its own — **specVersion 27**, #1173,
`package-format-v27-design.md` (decided 2026-10-29; the dynamic half below is 28); a `for` inside
anything waits for the scheduler redesign (§ 6). So every loop's length is known when the job starts, `TotalBlockCount` stays the stored scalar it has been since #176, and the
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
`when` failed had no row at all; at 26 it is the untaken branch of an `if` row — named in the
row's `body`, the row's `branch` saying why, no row of its own (#1223, provenance v2026.10.9) —
which is the first time the record says why a node did not run.

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
never an item of a loop the gate does not sit in. A folder item's `if` reads **inputs and its own
`item.id`** at 26 (static length, § 1.4; the id a static fact typed against the folder — decided 2026-10-26,
#1094; `item.label`/`.content` refused). Operators as 25: `==`, `!=`, `and`, `or`, `not`, parentheses;
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

**Built (#1263, 2026-11-01).** Every row of the picture is on the pane: "(preview)" on the prompt block; "· and the macro's if" under every instance row, on the macro pane, the node pane and the Deliverables card; the loop an instance sits in spelled as `for item in …` where the 25 forItem select stood (retired at 26 — its pick was lost on publish); on Read-by rows "· items:", "the macro's if:" and never fires against the read's guard. One departure, chosen: the instance's case spelling is a read-only sentence above the builder (`case item.id — when HPI, Plan — else skipped`) rather than a second editor. "· items:" names a folder item's read by its item and a loop body prompt's read as "every item (N)"; a read outside every loop runs once and says nothing.

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
  bytes a reader hashes do not move) → appended macros → the signature. The engine's compose step
  (`ConsultDeliverableComposition.TryCompose`, as built by #1155) takes the deliverable's source list
  where the aggregator arm took the node's; the part model (`ForEachPart`/`ScalarPart`) is the same.
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
- The walk's subjects rename: *"Prompt 'p' reads …"* becomes *"Node 'x' prompt reads …"* (the family
  of *"Node 'x' condition"* and *"Node 'x' if"*; built 2026-10-26);
  the condition and read rows in the editor follow (§ 12). `WorkflowDerivedEdges.WalkAll`
  resolves `node.Prompt` as a file instead of through `promptsById`; the engine's
  `RunPromptNodeActivity` loads by path.
- The editor's Prompts nav goes at 26 (it stays for ≤25 packages); the node pane holds the
  prompt editor and its preview, and `TemplatePreviewScope` has one owner per prompt — the
  "preview through the first node using it" seam (#1095) has nothing left to choose. #1243 (2026-10-30): the preview renders a loop's elements too — one per folder item, a placeholder for an array input, the inner if/case judged per element at 27.

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
  `instruction` (required where read inline, § 2.5; built 2026-10-26), `optional`, `default` (the pair
  judged as at v12 — optional without a default, default without optional — over the header, on a
  standalone macro and on a folder item keyed `<folder>/<item>`; #1224, package-format v2026.10.13),
  `if` (the key is carried from #1105; #1090 and #1094
  give a macro's and a snippet's `if` their effect). Anything else
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
| `macros[]` entry `{ id, label, file, optional?, default?, instruction? }` | **`macros` is a map** `{ "<id>": "<file>" \| "<dir>/" }` on the wire, as `snippets` is (§ 5.3; decided 2026-10-26 — the list every reader holds in memory, a converter between); the rest in the file's header, plus `if` (§ 2.1) |
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

## 6. The dynamic half, by name — dynamic fan sizes and the scheduler redesign (specVersion 28)

*(#1062; and the four flow shapes § 1.4 refuses. Numbered 28 on 2026-10-29: 27 is the static
shape § 1.4 names as a follow-up — a flow node inside a `for`, `package-format-v27-design.md`.)* The invariant 25 and 26 share: **a loop's
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
  it ({{ for e in node.sections }}{{ e.per_item | concept_bullets }}{{ end }}), or read
  node.sections.content"*). **Inside a loop the pipe takes the body node by id** (decided
  2026-10-26, one rule everywhere): `e.content | concept_bullets` is refused with the replacement
  naming the returned node (*"… at specVersion 26 a renderer pipe takes the node — e.per_item |
  concept_bullets"*); `e.content` stays readable as text.
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
whole), the stamp. The reason the storage version bumps is the flow rows, whose `body` and
`branch` name a pruned node for the first time (§ 1.5; #1223 — the node has no row of its own),
not any hash.

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

*(#1102, 2026-10-26 — a node names its prompt file, as § 4 says: at 26 the walk reads `nodes[].prompt` as a
path under the node's own subject (*"Node 'assessment' prompt reads …"*); a declared prompt's id is refused
naming the file the package holds (*"names prompt 'assessment'; at specVersion 26 prompt is the file
(prompts/assessment.md)"*), a missing file is refused (*"prompt 'prompts/nope.md' is missing from the
package"*); two nodes may name one file; the publish closure references the nodes' prompt files as `prompts[]`
did. **The design's minimal 26 package is valid** — the first the path accepts whole. The engine's load by
path and the store's gathering of the nodes' prompt files are #1157; the editor's node-pane prompt #1162;
the migration #1152.)*

*(#1151, 2026-10-26 — the 26 conformance partial exists (`tests/ConformanceFixtureExport.V26.cs`): the
design's minimal as the valid baseline beside an if node, a case node, the loop read from outside, two
deliverables sharing a source and a deliverable's text read, and one refusal per sentence the path has built so
far — the ten retirements, the flow nodes, the deliverable, the prompt. The export **holds back** every case
above the engine's accepted ceiling and says so: validated through the public entry they would record the
accepted-set sentence, and the registry files an unsupported version under `any/`; a dry run today reproduces
the registry's `conformance/` byte for byte. The bump (#1149) lifts the hold-back by accepting 26; the
migrated-25 baseline joins with #1152's rewriter; the "requires specVersion 26" gates (spec-25 cases) ride the
bump too, since exported early they would change the supported v25 set.)*

*(#1105, 2026-10-26 — a file says what it is, as § 5 says. `WorkflowTextHeaders` (new): `Split` (the first
line `{`, the first `}` line, BOM and CRLF tolerated, a `{{` line never a header), `Parse` per kind (the
closed sets, the reserved names, the types, the label required — every sentence names the file),
`ReadIndexAt26` (the ordered `[{ id, file }]`; the 25 object, an item's `name`/`when` and any other key
refused naming the header) and `Resolve`: every snippet, folder item and macro file the manifest names is
split and parsed, a prompt file that opens with a header refused, and the result is **the effective manifest**
(each macro's header facts filled into `macros[]`, whose `label` is nullable now) **and the bodies** — what the
26 pipeline, the shared walk (`WalkAll`, idempotent on bodies), `PrepareNodes` and the preview scope run over;
the resolver at 26 takes the headers and reads a folder's items from the list with fields `id`, `label`,
`content` and the item's `if`. The manifest's `macros[].label/instruction/optional/default` are retired by
sentence; the instruction is required in the header (*"Macro 'closing' header declares no instruction; required
at specVersion 26"*) until #1096. The walker's members at 26: `.label` for `.name` (*"reads item.name; at
specVersion 26 the member is .label"*), a macro's `.instruction` the same read as `.prompt`; the probe
labelled. The folder item's `if` is walked at the item's site (the effect is #1094's). The schema at 26: a
macro entry `{ id, file }`. The publish closure reads the 26 index. The minimal 26 package carries its
headers and is valid; the conformance partial carries the § 5 cases, held back. The engine's strip before
render, the renderer's `.label`/`.instruction` and the store's reading of the 26 index are #1157; the editor's
header pane #1163; the migration's `{ "label": <name>, "if": <when> }` #1152.)*

*(#1089, 2026-10-26 — the instance gate governs the inline read, as § 2.3 says; the library half. A node
instance's `if` is walked at its site under the 26 subject (*"Node 'assessment' macro 'closing' if compares
input.kind to 'maybe' …"*; a 25 `when` keeps *"… condition"*), typed against the inputs, refused reading a
loop, `item.id` typed against the loop the node runs in (§ 2.4) and refused outside one. The judgement is one
pure function, `WorkflowInlineGates.Suppressed(nodeId, instances, itemId, scope)`, over the node's render scope:
a macro with no instance on the node is never named; a macro is suppressed when every instance of it on the
node carries a gate and none holds (an instance anchored by `forItem` to another item is not this node's); any
instance holding reads. The renderer's scope carries the set (`TemplateRenderScope.SuppressedPrompts`): a
suppressed `macro.<id>.prompt` renders **empty — no instruction, no mechanics sentence, no read recorded**;
null is the 25 render. `.content` is untouched (a placement's skip is completion's; a text's `.content` read
is the macro's own `if`, #1090). Decided 2026-10-26: the engine half — the descriptor carrying `if`, the activity
judging before the render, completion excluding by `if` — is **#1175** (layer B, after #1157; landed 2026-10-27); an instruction
template that renders empty keeps today's sentence until #1096; the preview's node context is the editor's
(M33).)*

*(#1090, 2026-10-26 — the macro's own gate, as § 2.2 says; the library half. The macro file's header `if`
(#1105) is walked at its own site, `ConditionSite.Macro`, under *"Macro 'closing' if"* — in no fan and no
scope: inputs, scalar node values, run and profile; an item (*"reads item.id, but the node does not fan"*), a
loop and `deliverable.<id>` refused as at every condition site. The validator hands the headers it resolved to
the shared walk, which resolves them itself when called with the package as published. A reader's edges close
over the gate's reads as over the macro's text — a node reading `macro.closing.prompt` waits on the scalar node
the gate compares. `WorkflowInlineGates.Inert(macros, scope)` is the judgement beside `Suppressed`; the render
scope's `InertMacros` empties **both** `.prompt` (no instruction, no mechanics sentence) and `.content` (no
blank block), recording no read; it composes by AND with the instance gate's set. Decided 2026-10-26: the engine
half — the inert render judged before the prompt, an inert macro's placements skipped into `excluded[]` /
`excludedMacros` with the header's text — is **#1177** (layer B, beside #1175, after #1157; landed 2026-10-27); the desk's
"never fires" warning is the editor's (M33); the migration's lifting of a uniform text guard is #1152's.)*

*(#1094, 2026-10-26 — the snippet's and the folder item's gate, as § 2.1 and § 5.4 say; the library half. A
standalone snippet's header `if` is walked at `ConditionSite.Snippet` under *"Snippet 'standards' if"* in no fan
and no scope (an item refused as *"reads item.id, but the node does not fan"*; a loop and `deliverable.<id>` as
everywhere); a reader's edges close over the gate's reads. A folder item's `if` is walked under its folder's fan
(subject *"Snippet folder 'sections' item 'hpi' if"*; the 25 `when` keeps *"… condition"* and no fan), so its own
`item.id` types against the folder (*"compares item.id to 'nope', which the folder does not contain (items:
hpi, plan)"*); the 26 rule reads *"a folder item's if reads inputs and item.id only, so the folder's size is
known at job start"*. `WorkflowInlineGates.InertSnippets` is the judgement; the render scope's `InertSnippets`
empties `snippet.<id>.content` where read, the id as ever; null is the 25 render. Decided 2026-10-26: the engine
half — the inert render judged before the prompt, the job starter's fan filter reading the item's `if`, the
direct read gated once #1116 lands — is **#1179** (layer B, beside #1175/#1177, after #1157; landed 2026-10-27); the preview's
picture is the editor's (M33); the migration's lifting of a uniform text guard is #1152's.)*

*(#1096, 2026-10-26 — the instruction required only where read inline, as § 2.5 says. `ValidateInstructionsAt26`
runs after the walk over the inline-read closure from node prompts (`Walk.PromptReadsOf`, the #1067 closure):
a macro with no header instruction that a node's prompt reaches — directly or through the snippets and folder
items it reads — is refused naming the reading node (*"Macro 'closing' is read inline by node 'assessment' and
declares no instruction; the model is told nothing (macros/closing.md)."*); a placed-only macro's instruction is
optional and, when present, kept (the desk note "unread" is the editor's, M33). The renderer: a blank rendered
instruction — absent, or a template that renders to nothing — is an **empty read**, no mechanics sentence, no
read recorded (decided 2026-10-26; the case #1089 deferred, closed — `WorkflowMacroMechanics.Prompt` gives an
empty prompt for a blank instruction at every version; a valid 25 macro's instruction is non-blank). No engine
half: the engine renders through the renderer. The gates group (#1089, #1090, #1094, #1096) is complete in the
library.)*

*(#1099, 2026-10-26 — macro folders, as § 5.3 says; the library half. **The map on the wire, the list in
memory** (decided 2026-10-26): at 26 `macros` is `{ "<id>": "<file>" | "<dir>/" }`, snippet-identical;
`WorkflowMacroTableConverter` reads the map and the 25 list into the `WorkflowMacroSpec` list every reader holds,
marked with the shape read (`WorkflowMacroTable.IsMap`), and writes the map for bare entries, the list
otherwise (every 5–25 manifest round-trips its bytes); the list is refused at 26 and the map below 26 by
sentence; the 26 schema describes the map. **The folder:** an entry whose path ends with `/` is a folder as a
`snippets` entry is — its index the ordered list, its items macro files parsed with the macro key set (the
kind follows the declaration, decided 2026-10-26), resolved through the shared resolver with the noun "Macro"
(*"Macro collection 'section_notes' is missing 'macros/section_notes/index.json'."*), each item's header on
the item; walked as macro texts keyed `<folder>/<item>` with their instructions and their own gates;
`macro.<folder>` iterated, the element's `.content` and `.prompt`/`.instruction` reads expanded to every item
for reachability, the inline-read closure (an item read inline needs its instruction, § 2.5) and a reader's
edges; **read, not placed** — placing the folder (*"names macro folder 'section_notes'; a folder is read …,
not placed, at specVersion 26"*) or an item by `<folder>.<item>` refused by name; the probe and the renderer
iterate the folder as an array of macro objects in the index's order (the inert and suppressed sets keyed
`<folder>/<item>`); the preview scope carries the folders; the publish closure references the index and its
items; the Web manifest reader reads the map. The engine half — the store gathering a folder, the render
payload's folders — is **#1182** (layer B, after #1157; landed 2026-10-27); the direct item read `macro.<folder>.<item>` is #1116;
the editor's `+ Macro folder` is #1163's milestone; #1152 writes the map.)*

*(#1116, 2026-10-26 — a folder item readable directly, as § 5.4 says; both the snippet item and the macro item
(decided 2026-10-26). At 26 a declared item is a member of its folder: `snippet.<folder>.<item>` resolves to the
item with the snippet member set, `macro.<folder>.<item>` with the macro's (`.prompt`, `.instruction` included),
recorded under the item's key `<folder>/<item>` — an edge, a reach and an inline read exactly as the scalar's
(an item read inline needs its instruction, § 2.5; the reader's edges close over the item's reads and its own
gate). An undeclared item is refused naming the folder's items (*"reads snippet.sections.nope, which folder
'sections' does not contain (items: hpi, plan)."*); below 26 the 25 sentence holds. The folder itself is now
recorded where it is iterated or indexed, not where it is named, so a direct read waits on the item alone; an
item read directly counts as its folder's use. The probe's and the renderer's folder arrays carry the items as
members (Scriban's array members), so `snippet.sections.hpi.content` renders beside `{{ for s in snippet.sections }}`;
a gated-off item's direct read renders empty through the engine's key (#1179). The editor's palette choice is
M33's.)*

*(#1207, 2026-10-27 — the editor's half. The read palette at 26 lists each folder's items under the folder's
loop entry, **one pick per item** — `snippet.<folder>.<item>.content` labelled with the header's label (decided
2026-10-27: `.label` and `.id` stay typable, the list stays one row per item) — and, new, a macro folder's loop
(`for m in macro.<folder>`) followed by its items with the macro idiom (`.content`; `.prompt (inline)` outside a
macro text); the reading item never offers itself; the fan item's member reads `item.label` at 26. The read rows
know the key shape: a `<folder>/<item>` key in `Snippets` / `MacroContents` / `MacroPrompts` is an item read —
*"Item 'hpi' of folder 'sections' · reads snippet.sections.hpi.content · directly"* (a macro item *"Macro item
'hpi' of folder 'notes'"*, inline when `.prompt`), opening the item's pane, the member spelled with dots so its
guard site matches; a `MacroFolders` row *"Macro folder 'notes' · iterates its items"* joins the snippet folder's.
The snippet item pane lists its **direct readers** ("Read directly by", *reads it directly*) beside the folder's,
and the macro-folder item pane gains its readers (inline and text reads keyed `<folder>/<item>`, then every
template iterating the folder). Two leftovers closed on the way: the editor's walk never passed the resolved
macro folders to `WalkAll`, so a macro folder's items were not walked in the editor (#1099); and the preview
scope was never **labelled** at 26 — `TemplatePreviewScope.Tokens` now sets `Labelled` and the snippets' header
labels and names the fan item's token `item.label`, so `item.label`, `s.label`, a direct read's `.label` and a
macro item's `.instruction` render in the preview as the engine renders them. Below 26 the palette is unchanged
and the spelling is the library's refusal.)*

*(#1059, 2026-10-26 — a renderer pipe takes the node, as § 7.1 says. The walker at 26 judges a bare node
operand by its format: the edge and the rendering are recorded on the node (`Rendered` carries the node, the
pipe and the guard as before); `node.x.content | pipe` is refused with the replacement, `node.x.value | pipe`
too; an `if`/`case` node passes when its returned node's format is the pipe's and is refused naming the
disagreement; a `for` node is refused naming the body node to pipe; inside a loop the body node by id passes
(`e.per_item | concept_bullets`, a body node reading its sibling by bare id likewise) and the element's content
is refused naming it (decided 2026-10-26); a deliverable's text, a snippet, an input and a bare element are not
nodes. Below 26 every sentence is as it was. The renderer's node object (`NodeObject`) carries its id and
output, so `ApplyPipe` takes the node whole; the reference-equality lookup over `.content` stays for a 25
package's spelling. The probe and the preview scope needed nothing (the probe ignores the operand; the
preview resolves the output first). The engine renders `{{ node.x | pipe }}` through the same renderer; the
loop element's body nodes as node objects at run time are #1154's. #1152 rewrites the 28 public sites.)*

*(#1152, 2026-10-26 — the migration 25 → 26, as § 13 says. `WorkflowPackageMigration25To26.Migrate(manifestJson,
files)` beside the 24 → 25 rewriter: the texts checked for a header already; the fans folded into loops, **a
loop per aggregated chain** (decided 2026-10-26: the collection's id when sole, `<collection>_<ret>`
otherwise; a node read by two chains and a fanned node no aggregator reaches refused); a node's `when`
wrapped in an `if` node; the aggregators dissolved into `sources[]` and their reads rewritten
(`deliverable.<id>.content` where a deliverable names the aggregator, the loop's `.content` where none does
and it has one fanned source, refused otherwise); every gate keyed `if`, `forItem` as `item.id == …`; the
prompts by path; the renderer pipes taking the node; the folder indexes as lists and every snippet, item and
macro file opening with its header (a standalone snippet's label invented and named in the notice);
`macros` the map; `.name` reading `.label`; one notice per rewrite, refusals applying nothing. The 25 minimal
migrates into a valid 26 package (the conformance baseline `v26-minimal-is-the-migrated-v25-minimal`); a pipe
on the node renders the bytes the `.content` one did. **The seven public packages migrated without a refusal
and validated at 26** through the seam (the dry run at the bump; its three tests read the sibling checkout
and retired to in-repo twins over the 25 minimal once the content moved to 26 — #1229): loops `standards` (general, test/general, two-documents),
`standards_section_instructions` + `standards_letter_body` (v12), `plan_sections` + `letter_decline_letter` +
`letter_information_request` (classifier-scope), `sections_consult_note_section` + `sections_progress_note_section`
(conditional-documents), the five of structured-intake. The runner `scripts/migrate-workflow-package.cs` gains
`--to 26` (validation at 26 waits for the bump; the suite validates through the seam). The editor's chain to
26 is M33's; the republish follows the engine release.)*

*(#1153, 2026-10-26 — the registry's v26, the document half. `package-format-v26.md` landed in
`consultologist-package-format` as a full grammar mirroring v25's headings (objects at 26, derived edges, flow
nodes, gates, the node's prompt file, the header and folders of one shape, deliverables, refused at 26,
validation, the migration as amended), **unlisted until the bump** as v25's document first landed; the README's
table through 26; the registry at v2026.10.11. Decided 2026-10-26, without a pending state: the schema and the
`conformance/v26/` vectors wait for #1149 — the registry's CI makes `documents` equal `supported`, files a case
whose version is outside `supported` under `any/` and lets `SHAPE_BLIND` name only indexed cases, and the
engine replays every indexed vector through the public validator; the vectors stay generated-and-held in the
engine (116 cases, pinned through the seam) and #1149's body carries that half. Layer A is complete.)*

*(#1154, PR 1 of 3, 2026-10-26 — the engine's starter over the SAME skeleton, as § 1.4 and § 14 say. A loop
is the fan it replaces: its body nodes' descriptors carry the loop's collection as their `forEach`
(`snippet.<folder>` | `input.<id>`, `WorkflowFlowNodes.Flow.Collection`) and `In` names the loop, so
`InstanceKey`, `InstanceReady`, `FanItems` and the chain steps run unchanged; the flow node has a row of its
own (`Flow`, `Collection` | `Expression`, `Body`, `Ret`, `Branch` on `ConsultNodeDescriptor`, appended last).
An `if`/`case` is decided where a 25 `when` was — `DecideFireSet` at start when its expression reads inputs
and run facts, the boundary (`DecideActivity.Decide`) when it reads a node's value or the profile: a flow
node's derived edges are its DECISION's reads (`Walk.EdgesOf` adds a case's subject, read structurally;
`GatingReads` the subjects, `GatingReadsProfile` the `Flow` site), never its bodies, so stage one runs what
the decision needs and not both branches; the engine attaches the taken body to the prune's map itself. The
judge is `WorkflowFlowDecision.Decide` (new; an `if` through `WorkflowConditionAt25.Holds`, a `case` by
evaluating its subject and taking the first branch whose `when` lists the value, else `else`, else none);
the branch the record names is "then" | "else" | "<v1, v2>" | "none". The untaken bodies are PRUNED (a set
of their own, never `gated` — the cascade does not fire on them); the row stays with its branch; a
deliverable sourcing a flow that took no branch is skipped *"source node.<id> took no branch"*. **Blocks come
from `deliverables[].sources[]`** (`WorkflowPackageBlocks.ResolveSourceBlocks`; `WorkflowResolvedResult.Sources`,
the deliverable's `if` riding as its `When` text): a loop source one block per settled item **named by the
loop** — `consult:standards:hpi` (decided 2026-10-26 over the returned node's name: the source as declared;
the body node that completes the block is the engine's concern, PR 2) — an if/case or prose source one block.
A folder item's `if` is judged at start with the item in hand (`ConsultConditionScope.WithItem`; the rule
allows `item.id`); a 26 item's roster name is its header's label. Pinned by the offline half of the done-when
(`V26SkeletonIdentityTests`): each public package and its migrated twin resolve the same block count, names
and items, collections and rosters, and decide the same fire set over the same inputs — the ids differing
only in the loop's name. Left to PR 2: the loop's completion as the aggregator's bytes, the if/case alias,
the skipped row, the scheduler's skipped set; to PR 3: the loop read at render (until then a loop reads as
text), the `label` member at run time. #1155 composes the deliverable; #1157 reads the headers and the
prompts in the store.)*

*(#1154, PR 2 of 3, 2026-10-26 — the engine loop, as § 1.3 says. A flow node is structural: the ready loop
never starts it and the expected-instance count leaves it out; it settles on the aggregators' pass
(`ConsultFlowCompletion.TryCompose`, pure over the snapshot and the outputs). **A loop completes as the 25
aggregator over one fan source did**: once every item of its returned node has an output, `outputs[loop]`
is `ConsultAggregateRenderer.Render` over those items in item order with the aggregator's ladder
(`ComputeAggregateInputHash` over the items' output hashes, the SHA-256 of the render as output and agent-output
hash, definition 6) — byte-for-byte the #1065 identity the migration relies on (pinned); a failed item fails
the loop with the aggregator's sentence. **An if/case completes as its taken body's returned node did** —
`outputs[flow]` IS that node's result (the alias; its row carries no hash, the returned node's does). A
reader of a loop waits on the loop's own row, not its items (its descriptor's `ForEach` is null, so
`InstanceKey` is the bare id). **No branch taken = skipped**: `MarkSkippedFlowsAsync` runs once the graph is
final (at start for a non-deciding job, after the boundary for a deciding one) — the row's status Skipped
with the reason *"took no branch"* (`MarkNodeSkipped`, new on the entity; the branch "none" rides the
descriptor), the counts moved on; `InstanceReady` takes the skipped set (a skipped dependency is satisfied)
and `NodeTextsFor` omits the read — the render's null is PR 3's. The deliverable table (`ConsultDeliverables`)
carries `Sources` as declared and `SourceIds` as the COMPLETION keys (a loop's returned node, an if/case's own
id, a prose node's own); a block is named by the loop (`BlockNodeOf`: the returned body node → its loop) at the
item's completion and at its failure, an if/case source's one block completes with the flow; a 26 item's
display name is its header's label (`WorkflowPackageBlocks.ItemName`, in the engine's `ItemName` too — the
`## {Name}` heading bytes). Known limit, left as it is: a gating condition reading an if/case node whose own
expression reads a node (two decision moments in one job) — stage one holds the flow undecided, the boundary's
judge finds no output; the validator allows the read (§ 1.3) and no public package writes it; a follow-up if
one does. Left to PR 3: the loop read at render (`{{ for e in node.<loop> }}`), a skipped flow reading null,
the `label` member. #1155: `FinalOutcome`, `pendingDeliverables` and the compose step from `sources[]` — a 26
job cannot complete its deliverable until then (none runs before the bump).)*

*(#1154, PR 3 of 3, 2026-10-26 — the render scope, as § 1.3 reads a loop. The orchestrator hands the activity
every loop the node reads whole (`ConsultLoopPayload`: the collection, the returned node, the settled items in
item order, each body node's output per item), the returned node of every flow node read (`FlowRets`, so an
if/case alias types as its returned node) and the flows that took no branch (`SkippedNodes`) — three trailing
fields on the activity input, null below 26. `TemplateRenderScope` gains `Loops`, `SkippedNodes` and
`Labelled`. **`{{ for e in node.<loop> }}` iterates**: each element carries `item` (the fan item, rendered
when read as the instance's own `item` is), `content` and `value` (the returned node's for that item) and
every body node by id as a node object — so `e.<body> | pipe` takes the node (§ 7.1); the list reads as the
loop's composed text (the aggregator's bytes, `node.<loop>.content`) and as the array of the returned node's
values. **One builder** (`TemplateRenderer.LoopList`) assembles the list for the validator's probe
(`ProbeFlowNodesAt26`, rewritten over it) and for the render, so the two member sets cannot drift; a package
valid at 26 whose prompt iterates its loop renders over the run's scope (pinned). A flow node that took no
branch reads null — `{{ if node.triage }}` guards it. At 26 the item, snippet, folder-item and macro objects
carry `label` beside `name` (the walker admits `.label` and refuses `.name` there); a 26 folder item's name
is its header's label everywhere the engine names one. **Left to #1157**: the store resolving the headers
(a macro's `label`/`instruction` at run time, a folder item's `if` and label from the store's own read) and
the prompt loaded by path. #1155 composes the deliverable from `sources[]`; a deliverable macro reads a loop
as text alone until then. #1154 closes.)*

*(#1155, 2026-10-26 — the deliverable composes from `sources[]`, as § 3 and § 3.1 say. **The compose step is
the deliverable's** (`ConsultDeliverableComposition.TryCompose`, pure; a third arm of the aggregators' pass after
the flow arm, the ≤25 aggregator arm untouched): once every declared source has settled — a loop's own row,
an if/case's alias, a prose node — the parts in `sources[]` order through the aggregator's renderer and ladder
(a loop source → one `ForEachPart` from its returned node's items with those items' output hashes; an if/case
or a prose node → a `ScalarPart` with its output hash), so the bytes and the hashes are the 25 aggregator's
(pinned over the migrated `example-v12-constructs`: `consult` = the `standards` fan part then `patient_header`).
Composition waits on the SOURCES only; the release waits on the reads and the check as it did. **The composed
hash is stamped at release on the document's row** (decided 2026-10-26 over an own entity event):
`ConsultGenerationResultDocument`, the state row, the response and the failed document carry `ComposedHash` and
`Sources` (trailing, null below 26) — the aggregator's `agentOutputHash`, moved; storage version 8 is #1156's.
**`deliverable.<id>.content` at render**: the walk gains the per-node accessor (`Walk.DeliverableReadsOf`,
`MacroDeliverableReads`; `WorkflowDerivedEdges.DeliverableReadsByNode` / `DeliverableReadsByDeliverable` — the
B8 name `DeliverableReads` keeps meaning deliverable id → its macros' node reads), closed as the edges are over
the snippets and macro texts a prompt reads; the node and result descriptors carry `DeliverableReads`; the
orchestrator threads `Deliverables` (id → the composed text; null for one skipped by its `if`) to the prompt
activity and the deliverable-macro activity; the render's sixth object is a closed `{content}` per entry — the
probe's set — and a skipped one reads null (`{{ if deliverable.x }}` guards it). Scheduling is unchanged: a
reader's `Reads` are the sources (the walk's expansion), the deliverable composes on the pass its last source
completes, the ready loop runs after. **The release holds** (`IsReleaseReady`): a read naming a flow that took no
branch counts as settled (without it a 26 deliverable whose macro reads `node.<if>` never released); a read of
another deliverable holds until it composes, or passes when it is not in the run. **`FinalOutcome` at 26**:
produced = composed (25's meaning, decided 2026-10-26 over "released"); the failures in their own map by
deliverable id. Limits, left as they are: a reader of `deliverable.<id>.content` inside the deciding closure
(the mirror of #1154's two-decision-moment limit — the compose arm runs only once the graph is final); a
deliverable macro's raw file still carries its header at render until #1157 reads the headers in the store;
no 26 job runs before the bump (#1149), whose done-when is the dev run with equal hashes.)*

*(#1157, 2026-10-27 — the header stripped before render and the prompt loaded by path, as § 4 and § 5 say.
**The store splits once, after validation** (`WorkflowPackageStore.Materialise`, the pure half of a load,
reachable by tests without blobs): at 26 the download set adds the nodes' prompt files and skips a macro entry
ending in `/` (a folder — #1182 gathers it; `Validate` refuses the missing index loud until then), a folder's
`index.json` is read in its version's shape (`ItemFilesOf`: the ≤25 object or the 26 list), then
`WorkflowTextHeaders.Resolve` runs ONCE and rides the package as **`WorkflowPackage.Texts`** beside the raw form —
`Manifest` and `SourceFiles` stay as published (the walk resolves the headers itself from the raw files and
the resolver is not idempotent: run over bodies it finds no header and drops every gate; the content endpoint
round-trips the files whole; publish compares raw), `EffectiveManifest`/`TextFiles`/`TextOf` give the
resolved form. The prompts are keyed by PATH at 26 (one `WorkflowPromptTemplate` per distinct `nodes[].prompt`,
no variables, no prelude, never raw), so the activity's lookup is the one it was — the descriptor's `PromptId`
was the path already; the data resolves through the headers (an item's label and gate on the item, the
bodies as content). **The readers switched**: the render scope's macros are the effective manifest's (label,
instruction, optional, default from the header) over the bodies, with the header's `if` carried on
`TemplateMacroSource.If` for #1177's judge — so every render site (the prompt activity, the completion, the
deliverable-macro activity, the condition scope) sees bodies and the #1155 limit closes; the starter's
`macroTexts`, `ResolveMacroChoices` (a per-run choice on an optional macro works at 26) and the Current
endpoint's optional macros read the effective manifest; `DescribeNode` takes the text files (a format or
schema file is byte-identical in both maps). The publisher's relabel warning resolves the new version through
the headers at 26 (it was silently dead there: the 25-shape resolve saw every headered text as changed and the
26 index as no folder). **`macro.<id>.instruction` at render** (26): the instruction rendered over the scope
without the mechanics sentence, the same read as `.prompt` (recorded; empty when suppressed or inert).
Pinned by the done-when: the 25 minimal and its migrated twin, each loaded the store's way, render the same
bytes — the assessment prompt (by id, by path), a fanned item (name, label), the macro's `.prompt` and
`.content`. Decided 2026-10-27: the macro's own gate is CARRIED here and judged by #1177 beside #1175's
instance gate, over one scope. The bump (#1149) lifts `SupportedSpecVersions` AND `AcceptedSpecVersions`;
until then `Materialise` takes the accepted set and no 26 package loads live.)*

*(#1175 + #1177, 2026-10-27 — the instance gate and the macro's own gate at run time, as § 2.2–2.4 say; one PR,
both engine halves (decided 2026-10-27: judged over one scope in one place). `ConsultNodeMacroDescriptor`
carries the instance's `if` (both describe paths; durable, trailing). **One judgement**,
`ConsultGatedScope.Judge(input, package)`: the node's scope (`ScopeAt25`) with `InertMacros` =
`WorkflowInlineGates.Inert` over the macros' header gates (carried on the macro source by #1157) and
`SuppressedPrompts` = `WorkflowInlineGates.Suppressed` over the instances' `if` — **at 26 only**: a 25 instance's
`when` never suppressed the inline read (the text guard was the author's tool), so below 26 the scope is the one it
was and the bytes are pinned. `ConsultRenderPayload.RenderAt25` renders through it, so the activity's render and
its template early return are gated with no caller able to forget; the completion builds the same gated scope
(the reply's template render and the instances' texts see the sets) — judged twice, pure, before the model call.
**Completion's order**: the item anchor → inert first (the source is the ceiling; the header's text is the reason)
→ the instance's gate `if ?? when` under the "if" / "condition" subject → render; `excluded[]` unchanged. **The
deliverable side**: `WorkflowResolvedMacroCondition.If` (the store keeps an entry with `when` or `if`);
`ConsultConditionScope.InertReason` memoised (reset with the folders and the item); `DecidePlacementWhens` judges
inert over the live placements BEFORE the ≤24 fallback — a 26 deliverable placing an inert macro with no instance
gate carries no macro condition and the fallback would have kept it (pinned) — then the instance's gate under the
26 subject; `excludedMacros` unchanged; the deliverable-macro render carries the inert set too, so a placed macro's
text reading an inert macro's `.content` gets empty ("a macro gated off is off everywhere"). Pinned: a suppressed
read renders empty with no mechanics sentence and the placement is excluded by the `if` text; any instance holding
reads; no instance reads as today; `item.id` per item; an inert macro's `.prompt` and `.content` empty and its
placement excluded with the header's text; inert + a holding instance is inert; a live macro + a failing instance
is suppressed, its `.content` still composing inline; a 25 `when` does not suppress. Left to #1179 (the snippet's
and the folder item's gate — `InertSnippets`, the starter's fan filter), #1182 (macro folders), the bump.)*

*(#1179, 2026-10-27 — the snippet's and the folder item's gate at run time, as § 2.1 and § 5.4 say; the last
gate. **A folder item's gate is judged once, at job start**, with the item bound — the starter's fan filter
(`SettledFolders`, #1154): the settled snapshot the orchestrator threads holds the live items and the render never
re-judges. Two facts decided the direct read: the renderer's context is strict (a direct read of an item absent
from the rendered folder refused the member), and the renderer had kept an inert item IN the loop with empty
content. So the renderer now leaves an inert folder item out of every loop over its folder and keeps it as a
direct member (`array.size` is the live count) — the design's sentence, literally — and at 26 the render sees the
FULL declared folder (bodies, labels, from the package's data) with every item the settled snapshot lacks keyed
`<folder>/<item>` in `InertSnippets` (`ConsultGatedScope.JudgeSnippets`, called by `Judge` and by the
deliverable-macro render): the loop iterates the settled set, the direct read of a gated-off item renders empty.
**A standalone snippet's header `if`** is judged in `JudgeSnippets` over the node's scope
(`WorkflowInlineGates.InertSnippets`; the gate from the resolved texts' headers by the manifest's path) — inert,
its content is empty where read, its id and label as ever; its label at 26 is its header's
(`TemplateRenderScope.SnippetLabels`, filled by the scope builder — the one slice of #1157's label work the
snippet lacked). Below 26 the folders are the settled snapshot and no key is set: the bytes are pinned. The
editor's "empty prompt" desk note is M33's. Left to #1182 (macro folders: the store's gathering, the folder's
items' gates by the same key).)*

*(#1182, 2026-10-27 — macro folders at run time, as § 5.3 says; the engine half of #1099 and the last engine
half of layer B. **The store gathers a folder as it gathers a snippet folder** — the entries whose path ends
with `/` form a snippet-shaped table and go through the same gathering (the index, then each item file; the
noun "Macro" for the index's sentences) — and `Materialise` resolves them through the headers
(`WorkflowTextHeaders.ResolveMacroFolders`; loud on an error, since the validator already passed them) into
`WorkflowPackage.MacroFolders` beside `Texts`: id → the index's ordered items, each with its header whole and
its body as content; null below 26 and when none. **The render's folders** (`TemplateRenderScope.MacroFolders`,
filled by the scope builder): each item a macro source with the header's label, instruction and `if`, the body
as text, in the index's order — the renderer iterates the array and keys the direct read by item id; the
mechanics sentence names the item's id, as the library's test pinned. **The gates by key**: one helper
(`ConsultGatedScope.InertMacroKeys`) judges the flat macros by id and a folder's items by `<folder>/<item>`,
the key the renderer and the walk use — the prompt activity, the completion and the deliverable-macro render
all go through it; an inert item stays in the loop with empty prompt and content (a macro's gate is a render
fact — unlike a snippet item's, which the starter's fan filter judged once). **Two gaps the engine half
surfaced**: every convenience wrapper the engine calls (`Derive`, the deliverable reads, the gating reads, the
signature readers) ran the walk WITHOUT the resolved macro folders, so a reader of `macro.<folder>` had no
edges to what the items read and the scheduler could have run it before its sources — the wrappers now
resolve the folders through the six-argument `PrepareNodes` and pass them, the validator's one map; and the
publisher's file-path rule admitted only single-segment `macros/<file>`, so a package with a folder could never
be published — `macros/<folder>/<file>` joins the admitted shapes, the folder id snake_case as a snippet
folder's. A macro's gate is the reader's edge, not a gating read (it is judged at the render, never at the
boundary). Left to the record (#1156) and the bump (#1149).)*

*(#1156, 2026-10-27 — the provenance record at storage 8, as § 1.5 and § 10 say; two PRs in two repos, in
order: the registry first (`consultologist-provenance` PR #41 publishes `provenance@v2026.10.7` on merge:
`recordStorageVersions` += 8, the record's `nodes` row at 8, a flow row's instance in `nodeOutputs`, a
document's `sources` and `composedHash`, the branch spelling, hash-definitions § 4's one sentence that a `for`
node is the aggregator it describes), then the engine with the submodule bumped to that merge (the
ladder-equality test pins the two together). **Two decisions (2026-10-27):** the flow row's discriminator is
spelled **`kind`** on the wire and the engine's property is **renamed `Kind`** — `Flow` was #1154's naming
slip, not a design choice, and no 26 record exists (26 is inert until the bump; the Durable replay pin was
the only site); the nodes row **follows the design** — `prompt` (the path) in place of `promptId`, `forEach`
and `aggregate` absent, a body node's `in` and the loop row's `collection` saying what they said. **The finding
that shaped it:** `ForEach` and `PromptId` are the orchestrator's working fields (the scheduler fans a body
node through `ForEach` at fifteen sites; the activity input takes `PromptId`), so the entity keeps them and the
**served record** projects (`ConsultNodeDescriptor.AsRecordRowAt8`, applied in `ToResponse` at 26) — the
contract is the served record ("field names are as served"), and `ToResponse` already projected (documents
withheld unless Completed). The row gains `Prompt` (the path, set at 26 beside `PromptId`) and `Count` (a loop
row's settled fan — the collection sets the starter and the boundary hold; an item gated off at job start is
not counted). **The stamp:** one entity helper (`Stamp(state, at25)`) at the nine sites writes 8 when the
package is at 26, else what the path wrote (6 for a node, 7 for a deliverable); `Seed` stamps 8 at creation,
so a 26 record never says 2, 6 or 7 — the registry's sentence. A 25 run still writes 6/7 (the existing pins
unchanged). History's DAG reads `forEach`/`promptId` to mark a fanned node and loses the marker for 26 runs
until a Web follow-up — noted, not filed. Left to the bump (#1149).)*
*(The Web follow-up is #1208 (2026-10-27, Milestone 33): the client's row mirrors the storage-8 fields and
History reads them — see § 12.)*

*(#1063, 2026-10-27 — audio transcripts with speech-to-text, as § 7.2 says; engine, Create page and a
provenance sentence, not a manifest change. Built INERT behind `SpeechToText__Endpoint` (unset everywhere; the
OCR precedent). **The service:** Azure AI Speech **fast transcription** — in Canada Central, not Canada East
(Document Intelligence's split), diarisation, per-phrase confidence, locale identification among candidates;
Entra auth through the resource's custom domain and *Cognitive Services User* on the user-assigned identity;
the .NET client `Azure.AI.Speech.Transcription` 1.0.0 beside the Document Intelligence one (SDK, not raw
REST — decided 2026-10-27). **Decisions (2026-10-27):** the caps stay 10 MB / 20 MB (about twenty minutes of
64 kbps MP3; the duration cap `SpeechToText__MaxDurationMinutes` 60 is judged on the service's measurement —
`too-long`); locales `en-CA, fr-CA` with `SpeechToText__Locales` overriding; the gate has its own keys
(`stt.confidenceGate`/`stt.minConfidence`, the OCR gate's rules) and its own Profile card; the Create page
learns the capability from `Public/Engine` → `features.speechToText` and the transcript door's sentence adds
"or a recording" only then. **As built:** `ISpeechToText` (never throws; `Transcribed | Empty | Unavailable |
Busy | TooLong`) and `AzureSpeechToText` (cues `Speaker N` as the service numbered them, mean phrase
confidence, 429 → Busy, telemetry never text, `speech/<version>+<commit>`); `AudioDocumentExtractor` sniffs
WAV/MP3 (ID3 or a Layer III sync — Layer III only, because a UTF-16 byte-order mark `FF FE` reads as a
Layer I sync and must stay text)/OGG/FLAC/M4A and returns `audio-needs-transcription`, last in the format
list; the impure edge routes it after the parse slot as a scan routes to OCR, renders the cues by
`TranscriptTurns.Render` (the one rule — the same `textSha256` as the VTT of the same conversation) and
gates on the account's minimum; outcomes `audio-not-enabled` (415), `stt-unavailable` (503),
`stt-low-confidence`, `too-long`, `busy`, with their sentences; `EngineAttestationResponse.Features`
(trailing; the property pin updated). The provenance registry gained one sentence (`provenance@v2026.10.8`,
no ladder moves). **Left:** provisioning on the user's word (the recipe in CONFIGURATION.md; dev first, then
a live preview with a fictional recording), the release that carries the engine half.)*

*(#1149, 2026-10-27 — the bump, engine + registry; **the content half waits for Milestone 33** (decided
2026-10-27: the editor's panes and chain for 26 are #1159–#1164, so the seven public packages and the
prod/operator pins stay at 25 until then, and `Templates.NewestSpecVersion` stays 25 while
`RunnableSpecVersion` is 26 — `SpecVersionMirrorTests` pins the lag by name, and the editor's one button
targets `min(Runnable, Newest)` so "Upgrade to specVersion 25" keeps showing and no 26 button is offered).
**Engine:** `AcceptedSpecVersions` and `SupportedSpecVersions` += 26; the migration runner's 26 stage now
validates at 26 before writing; the next-is-refused tests moved to 27. **Registry (`package-format
v2026.10.12`):** `spec-versions.json` lists 26 with the document and the generated schema; `conformance/v26/`
holds 117 vectors (19 valid, 98 invalid: 5 schema-caught, 93 `SHAPE_BLIND` — the flow bodies, the gates'
expressions, the sources, the headers and every template read are relations and file facts) and eight
below-26 gates sit under `v25/` (`invalid-<key>-at-v25`, each one sentence, all caught by the 25 schema); the
`any` case's sentence gains 26; the README's counts. **Three facts the export surfaced:** the 26 schema still
required a deliverable's retired `node` — it now requires `sources` and keeps `node` as a property so a
25-shaped manifest is refused by sentence, not shape; the typed writer spells a 25 manifest's macros as the
list and a 26 manifest's as the bare map, so four manifest-side macro refusals (the map below 26; the list, a
label or an instruction beside the map at 26) can carry no vector and stay pinned in the engine's suite
alone; and the `if` gate case must read an input the 25 minimal declares, or it carries two sentences. **Left
(each on the user's word):** the engine release; a 26 smoke on an account-owned migrated copy of
`test/general`; the M33 issue for the republish and the pins.)*

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
  *(The `if`/`case` half, as built by #1159: the client reader carries `if`/`case`/`then`/`else`/
  `branches` on `NodeView` (`IsIf`, `IsCase`, `IsFlow`), and every flow node's bodies are
  **addressed** — `FlowBodyRef(flowId, loop|then|else|branch:n)`, keyed as a loop's bare id (#1160's
  spelling, kept) or `<id>/<body>` — through `Bodies()` and `BodyOf(nodes, nodeId)`. **One placement
  picker** (decided 2026-10-27): the node pane's "in [— not in a body — | Sections (snippet.sections) |
  Triage · then | Route · in_scope, … | Route · else ▾]" and the add pane's "in" list every body; one
  mover, `PlaceInBody`, takes a node out of its body (clearing a `ret` that named it) and onto the end of
  the chosen one. **The if pane**: label, the condition through the 25 node-when builder (`ConditionField`
  on a `node-if:<id>` target writing the node's `if`), the then body and, once "+ else body" is pressed,
  the else — each body section the loop pane's idiom with its own `ret` picker. **The case pane** (one
  row per branch with value chips, decided 2026-10-27 over the text table's row-per-value): the subject
  select (enum inputs as `input.<id>`, enum-format nodes as `node.<id>.value`), per branch its chips (✕
  per chip, a one-shot "add a value…" over the values no branch lists yet), its body and `ret`, "Remove
  branch"; "+ branch" appends an empty branch the desk names ("lists no value"); an else as on the if
  pane; the unrouted values are said in the section's note. Every edit is one own dict, `flowEdits`
  (the whole tuple, self-removing). **"Runs when" goes at 26.** The add pane offers for, if and case
  before the formats; an `if` is born with an empty condition and an empty then (the desk names it until
  the builder completes it), a `case` needs its subject at birth ("Choose what the case switches on.").
  The Nodes nav nests then/else/branch rows beneath the flow node with a tag ("· then", "· in_scope").
  The diagram draws one subgraph per body returning into the flow's box through `ret`, and a dotted
  `case` edge from a subject node. An if/case inside a for, and the reverse, stay refused by the library
  (#1173).)*
  *(The `for` half, as built by #1160: the client reader carries `for`/`nodes`/`ret` on
  `NodeView` with `IsLoop`, `Collection` (the text after `item in `, the 25 fan spelling) and
  `RetNodeId`; `EffectiveFan(node, nodes)` = the node's own `forEach` below 26, its loop's
  collection at 26 — the one answer every item-typing site reads, so a body node's `item.*`
  types through its loop. The **loop pane** (a `NodeView` with `IsLoop`, at ≥26) is
  structural: label, the collection select (`item in …` written), the **body** in `nodes[]`
  order — each row a link to the node's pane, the returned one badged, a Remove-from-loop
  per row, a one-shot "add a node to the body…" over the plain nodes in no loop — and the
  **`ret` picker** over the body, resting "choose…" when unset: publish stays open and the
  desk refuses with the validator's own *"Loop 'x' returns nothing"*. The **in-loop picker**
  ("in [— not in a loop — | sections ▾]") sits on a plain node's pane where the for-each
  select sat; a pick moves the node between bodies and clears a `ret` that named it (explicit
  init again). Every change is one own dict, `loopEdits` (the whole tuple per loop, self-
  removing when it equals the loaded one; a pending loop is mutated), written by the composer
  as `for`/`nodes`/`ret` with both casings removed first. The add pane offers **"for — a
  loop"** first and **never the aggregator** at 26 (decided 2026-10-27; a deliverable's sources
  gather), a loop needing id, label and its collection ("Choose what the loop iterates."); a
  plain node picks "in [loop ▾]" and joins that body. The **Nodes nav nests** a loop's body
  beneath it in body order (decided 2026-10-27; a ≤25 list stays flat). The Graph pane's
  diagram draws a loop as its own subgraph (`for_<id>`, fed by its collection) returning into
  the loop's box through `ret`, and a deliverable's `sources` each draw an edge. Two reads
  the pane needed landed with it: a 26 folder's `index.json` list (`[{ id, file }]`, label and
  `if` from the item file's header — read only; the write target is #1163's) and a node born
  at 26 naming its prompt by file. The Prompt-text section on a body node waits on #1162
  (prompts by file); "Runs when" on a plain node is #1159's to retire.)*
  *(As built by #1227, found live over the first 25 and 26 packages the Graph pane drew: the
  diagram had drawn declarations alone — bindings, `aggregate`, `forEach`, the loop and if/case
  boxes, a deliverable's sources — so from 25, where a node's inputs are the reads its
  templates make, a package drew as a row of boxes. The generator now takes the walk
  (`WorkflowDerivedEdges.Walk.ReadsOf`, the closure `EdgesOf` closes, one read per source with
  its members): a node read is an edge labelled `value` / `content` / `value, content`, with
  `via snippet.x` or `via macro.y` when the prompt never reads the node itself but a carrier it
  reads does; an input, a snippet or folder, a macro folder is a stadium (`input.notes`,
  `snippet.standards`, `macro.notes`) feeding its reader plain; a read only the node's gates make
  — its `when`, an instance's `if`, an if/case node's own expression — draws dotted and
  labelled `if` (`when` at 25), the predicate convention; a body node's reads of the same body
  draw inside its subgraph, the rest cross the boundary after the box and its `ret`; a loop
  element read (`e.content`, `e.value`) draws from the **loop's box**, never its collection —
  the walk records it on the loop, the flow node is the only name the outside sees (§ 1.2),
  and the box is the loop's output where the stadium is its input; a case node's subject
  draws once, the dotted `case` edge as before. Below 25 the walk is never consulted. **The
  editor draws it in the browser** from its own walk (`EditorPackageWalk.Diagram`) — the
  loaded package, an import, an upgrade and pending edits alike, prompt edits included, with
  no round trip and live on merge — and asks the engine only when it cannot (no format
  catalogue, a manifest that does not parse); the engine's `GET …/Current/Diagram` and the
  `--dag` script run the same generator over the package's files and catalogue, so the
  checked-in `dag.mmd` matches; the preview `POST` stays the declared-only fallback. The
  pending-edit decoration counts every Mermaid link line for `linkStyle` — the dotted edges a
  case or a gate draws had shifted every index after them. Not built: a pending READ shown
  blue (a prompt edit adding a read — needs a walk of the loaded content and a (source,
  target) diff); the 26 deliverable `if` is still not drawn.)*
- **The `forItem` picker** (node instance "— every item —") goes; its row is the gate, with the
  `case item.id` spelling offered when the gate is a disjunction of `item.id` equalities.
- **The aggregator node** goes from the palette; the Deliverables card's node select becomes an
  **ordered sources list** (pick a node, arrows, remove); "Composed as" (#1081) names the sources;
  "Delivers →" on the node pane goes, replaced by *"Sourced by deliverable 'd'"* on a node that
  is one.
  *(As built by #1161: `ResultView` carries `sources` and `if` (a macro entry its `if`), and
  `SourceNodeIds` is the one answer every reader keys on — the roots, "Composed as", "Also
  placed on nodes", the carrying walk. The card's **Sources** section is the aggregator's
  source-list idiom: numbered rows linking to the node's pane, move up/down, remove, and a
  one-shot "+ add source…" that offers only what can compose prose — every flow node (its ret is
  the desk's to judge: a case returning a medication_list is refused by the validator's own
  sentence) and a top-level plain node of a text-typed format; never a body node, an enum, an
  object, a check, or a node already listed (decided 2026-10-27). "+ Deliverable" births
  `sources: []` (explicit init; the desk says "names no sources"). The gate section reads **if**
  and rests **Always**, the deliverable macro rows' gate reads **if** too, and both write `if`
  (never `when`); the composer never writes `node` at 26. "Delivers →" became *"Sourced by
  deliverable 'd'"* on the node, loop, if and case panes. A macro's label at 26 lives in its file
  header and shows as its id in "Composed as" until #1163 reads the headers.)*
  *(As built by #1222, found live over the first 26 package the editor opened: the Graph pane's
  "deliverable" select is the string-`result` door of the single-deliverable form and renders only
  while the package has no deliverables list — a listed package, at any version, edits its
  deliverables on the Deliverables pane, and a pick here would have composed a stray `result`
  beside `deliverables[]`; the ≤24 "Not feeding the deliverable yet" warning (bindings and
  `aggregate`, a reverse walk the page does itself) yields from 25 to the validator, whose own
  sentences the desk says — at 26 it had named every top-level node of a package that runs; the
  Deliverables subtitle names sources at 26; the Graph intro names format, prompt and reads from
  25. The ≤24 pane is byte-identical.)*
- **The Prompts nav** goes at 26; the node pane holds the prompt editor, its Reads rows and its
  preview (one `TemplatePreviewScope` owner). "Prompt 'p' reads" rows read "Node 'x''s prompt".
  *(As built by #1162: the editor's prompt model at 26 is DERIVED from the nodes — one
  `PromptView` per prompted node, keyed by the file on both Id and File, so every id-keyed site
  matches a node's `Prompt` (now a path) and `TemplateWalkOf(file)` finds exactly one owner: the
  node. The nav's Prompts group, "+ Prompt", the add pane's prompt select and the node card's
  prompt select go at 26; a node always mints `prompts/<id>.md` (an existing file refuses the id),
  lands on its own pane, and the pane's discard reverts the text. The node pane's Reads section
  gains **Read by** — every node whose prompt reads `node.<id>`; a deliverable sourcing it is the
  #1161 "Sourced by" line. The walk's subject stays the library's *"Node 'x' prompt"* (a wording
  change would move the conformance vectors); the Renderers/Formats rows link to the node pane.
  The desk says *"Node 'x' prompt has no text yet — write it on the node's pane."* A #1160
  leftover closed on the way: the preview's "preview as item" picks on a body node now resolve
  the folder through its loop (`EffectiveFan`), so one owner also means one item scope. **Not built:**
  a node-id rename — none exists at any version, so "renaming a node offers to rename the file"
  waits on a rename feature of its own. **Built in #1225 (2026-11-01):** the node pane's "rename" at
  26 and 27 rewrites every read by token (`WorkflowNodeRename` — `node.<id>`, `node["<id>"]`,
  `node.<loop>[i].<id>`, a loop element's `e.<id>`), every gate, header and structural site, and
  every pending edit; the prompt file always follows the id to `prompts/<new>.md` (#1267), unless
  another node shares it; undo and the pane's Discard take it back. **#1267 (2026-11-01):** at 26+
  a pane header names a file only when it is not the path its id implies (`prompts/`, `macros/`,
  `snippets/` or the folder, plus `<id>.md`), with "rename the file to match" beside it (node
  prompts, macros, snippets, folder items — `WorkflowFileMove`); the Package pane lists every
  mismatch and brings them in step at once. v28 (#1268) is to require it.)*
- **Label, instruction, optional, default and `if`** of a snippet, folder item or macro are
  edited where they are today (the macro pane's top fields, the folder item's row, the snippet
  pane's header); their **write target is the file's header**, not the manifest — the editor
  serialises the header and the text together, and a header the editor did not write (hand-
  edited, unknown key) is shown as the validator's sentence.
- **Folder items** show their kind (snippet or macro) from the header; `macro.<folder>` folders
  appear in the Macros nav group with the Snippets' folder idiom (#1103's arrows).
  *(As built by #1163, both bullets: the client reader reads every snippet and macro file's header
  once (`ReadTextHeaders`, by the file's declared kind — a folder item's kind **follows the
  declaration**, the snippets map or the macros map, as the library decided on 2026-10-26, not
  "instruction present"); `ReadMacros` over the 26 map fills label, instruction, optional, default
  and the macro's own `if` from the header, and a `"<dir>/"` entry is a `CollectionView` of kind
  Macro, read through the same list-index path as a snippet folder. The panes keep their fields;
  at 26 every one writes **one dictionary keyed by file**, `headerEdits` (the whole header tuple,
  self-removing when it equals the loaded one), and the text editor shows the **body, not the
  file** (`edits[path]` holds the body; the header never appears in the editor). The composer
  recomposes a headed file as `WorkflowTextHeaders.Compose(header) + body` — the migrator's bytes,
  the same key order — **only when something pends for it**; an untouched file passes through
  byte-identical (files hash whole). The `macros` map gains and loses keys (a file, a `"<dir>/"`
  folder) and never a label; a folder's index is written as the `[{ id, file }]` list, recomposed
  on membership and order alone. New: the macro pane's **if** section ("Always" at rest, the
  § 2.6 sentence), the optional/default pair back on the pane at 26 (the library judges the pair
  over the headers since #1224 — a standalone macro and a folder item alike — and the desk shows
  its words; until then the desk mirrored the sentence itself, the library judging the manifest's
  pair, empty at 26), the standalone snippet pane's **label** and **if** fields, "+ Snippet" with a label that
  defaults from the id as "+ Macro"'s does, "+ Macro folder" and a macro folder's item pane with
  its instruction and pair. **A refused header** (none, an unknown or reserved key, no label, bad
  JSON) opens as the whole file in the text editor with the library's own sentences in a bar and
  the fields hidden; a text edit that makes it parse brings the fields back live, and nothing is
  rewritten silently. "Composed as" shows the macro's label (#1161's caveat closes).)*
  *(#1205, 2026-10-27 — the rendered preview judges the three gates over a pick, through the library:
  `TemplatePreviewScope.Gates(manifest, walk)` lists every gate the walk's conditions carry at 26 (a
  node instance's `if` with the node as owner — an ungated instance listed too, since it keeps its
  macro's read live; a macro's own, keyed id or `<folder>/<item>`; a snippet's; a folder item's) with
  what each reads, and `Judged(picked, gates, pickedInputs, pickedNodes, itemPicked, reader, item)`
  applies `WorkflowInlineGates.Suppressed/Inert/InertSnippets` and, for an item, `Holds` with the item
  bound (the starter's filter) — **only over gates whose every read has a pick** (decided 2026-10-27: an
  unchosen token compares false and would gate everything off); the rest wait, naming the picks. The
  editor's preview judges the gates **its own text reads** (a macro's where `macro.<id>` is read or its
  folder iterated, a snippet's where read, an item's where its folder is iterated or the item read, the
  reader node's instances where `macro.<id>.prompt` is read — the node pane passes the node), offers a
  control for every input a judged gate reads, and lists beside the pane what it applied (*"macro
  'closing' is inert for this pick: its if (…) does not hold"*, *"the read of macro.x.prompt is
  suppressed …: node 'n''s instance if (…) does not hold"*, *"snippet 's' renders empty …"*, *"item 'i'
  of folder 'f' is out of the loop … (its direct read renders empty)"*) and what waits (*"pick input.kind
  to judge …"*, *"pick an item to judge …"*). Without a gate, or below 26, the bytes are the ones they
  were. Folded in: the node instance's gate is `if` at 26 — read into `NodeMacroView.If`, the rows read
  "if", the composer writes `if` and never `when`/`forItem` (a #1159 leftover: the editor wrote `when`
  at 26, which the validator refuses). The preview scope's macro sources carry their header `if` now,
  as the engine's do.)*
  *(#1206, 2026-10-27 — the three desk notes, pane notes only (no validator change). **Never fires** on an
  instance row — the node pane's Macros rows, the macro pane's node- and deliverable-instance rows, the
  Deliverables card's macro rows — *"never fires: the macro's if (…) and this instance's if (…) cannot both
  hold"*, judged **over one enum input by brute force** (decided 2026-10-27: the library records no compared
  literals; both texts read through the builder's tree, every clause `==`/`!=` on the same enum input, every
  declared value tried, none satisfies both; any other shape says nothing). **Unread** on the macro pane's
  "What the model is told" when the macro is placed, read inline by nothing and carries an instruction —
  *"unread: no prompt reads macro.<id>.prompt; the instruction is kept, not shown."* (the 25 sentence stays
  otherwise). **Empty prompt** there when the macro is read inline and its instruction is blank, or renders to
  whitespace over the token scope while its walk reads nothing a run could change (no input, node, item, run or
  profile fact — decided 2026-10-27, so a conditional instruction with unchosen tokens is not called empty) —
  *"empty prompt: macro.<id>.prompt renders to nothing — the model is told nothing where it is read (…)"*
  naming the readers. Macros only: a prompt whose only content is a gated snippet is #1205's preview note.)*
- **History at storage 8** *(#1208, 2026-10-27 — Milestone 33's subtitle, not the editor: the surfaces that read
  a record catch up with the 26 model. The client's `ConsultGenerationNodeDescriptor` mirrors the storage-8 row
  (`kind`, `collection`, `expression`, `body`, `ret`, `in`, `branch`, `deliverableReads`, `prompt`, `count`; a
  document's `sources` and `composedHash`), and one reading, `RecordFlow`, answers the fan for every surface —
  a node's own `forEach` below 8, its loop row's `collection` at 8. The run diagram draws a loop as the fan it is
  (a subgraph of its body with the settled count, the collection's `for` edge, the loop's box, the `ret` edge), an
  if/case as the decided set (a subgraph titled with the expression and the branch taken, a rhombus for the
  flow, `ret` when taken, a dotted `case` edge from a subject node) with its **pruned** bodies dashed inside, a
  node's prompt by path on its box, and a storage-8 document's `source` edges. History's rows gain a flow tag
  (`for item in … (count)`, `if … · branch: …`, `in <loop>`), the prompt path, and a muted *"pruned by <flow>"* row
  per pruned body; the Consults run rail fans a body node through its loop. **A pruned node is derived** (decided
  2026-10-27): a body id of an if/case row with neither a descriptor nor an output row — the engine's served
  `nodes` leaves pruned bodies out (`ConsultGenerationJobStarter` filters them at the fire set) where the
  registry's sentence listed them; both shapes draw the same. That engine/spec mismatch was resolved by #1223
  (2026-10-28): the registry's sentence now describes the engine's shape (provenance v2026.10.9).
  A storage-7 record draws and reads byte-identical.)*
- **The upgrade button**: "Upgrade to specVersion 26" on any 25 package (below 25, the chain
  runs 24 → 25 → 26, one publish, one notice naming the steps); a refusal (an unfillable `ret`,
  § 13) is said in the toolbar and leaves the package as loaded.
  *(Interim, as built by #1209 — retired whole by #1164: while the editor's ceiling lags the
  engine's (`NewestSpecVersion` 25, the engine running 26), a package above the ceiling opens
  **read-only with a banner** — *"This package is at specVersion 26; this editor authors up to
  25. Reading only; #1159–#1164 bring the 26 panes."* — and every edit door is refused by name:
  publish, the draft and history writes, undo/redo, discard/revert, import; the pane itself
  renders `inert` (one attribute that blocks every click, key and focus inside it, Fluent web
  components included) while the nav stays live so every pane can still be read, and the
  download stays open. The 26-pane tests render through a test-only page parameter,
  `AuthorsAboveCeiling`, so #1160's loop pane and the panes to come keep being exercised; the
  gate, the banner, the seam and the banner's test go together with #1164.)*
  *(As built by #1164, 2026-10-27 — the ceiling moves: `Templates.NewestSpecVersion` is 26, the mirror test is the
  equality again, and the one button reads "Upgrade to specVersion 26" on any package below it. The chain
  (`MigrateToNewestAsync`, the C6 shape with one more rung): below 24 the staged 24 rung (the prelude migration),
  then the composed package through `WorkflowPackageMigration24To25` where it is below 25, then through
  `WorkflowPackageMigration25To26` — one publish, one notice ("Migrated to specVersion 26. Through 24: … Through 25:
  … " then the 26 rewrites). **A refusal at any rung refuses whole** (decided 2026-10-27): nothing applies, the
  author's pending state comes back, the package stays as loaded, and the toolbar says the rung — *"At specVersion
  26: Node 'x' fans 'f' but no aggregator sources it …"*. The stored upgrade is v2 (a held 25 upgrade from before the
  chain reached 26 is not restored as a 26 one). The #1209 interim — the gate, the banner, `inert`, the guards, the
  `AuthorsAboveCeiling` seam and its test — is retired whole; a 26 package opens live and the 26-pane suites render
  plain. Below 25 a package's upgrade now lands at 26: the older suites' expectations moved with it (a chained
  7 → 26 package has no `prompts[]`, its snippet opens with a header, its aggregator dissolves into `sources`; at 26 an
  instruction is required only where read inline, so the explicit-init desk names the inline macro alone).)*

The macro pane is § 2.6's picture.

## 13. Migration 25 → 26 (mechanical; byte-identical in what the model sees)

| At 25 | At 26 |
|---|---|
| N nodes with `forEach: snippet.standards` | **a `for` node per aggregated chain** (decided 2026-10-26; the one-loop-per-collection rule migrated only `general` and `test/general`): for each body node an aggregator sources, one loop `{ id, for: "item in snippet.standards", nodes: [that node + the body nodes it reads inside the collection], ret: node.<that node> }`. The id is the collection's when it is the collection's only loop (`standards`, `plan_sections`); otherwise `<collection>_<ret>` (`sections_consult_note_section`, `standards_letter_body`, `prior_notes_summarise_note`), named in the notice. Two aggregators sourcing one node share its loop (two-documents). A fanned node no aggregator reaches, a node read by two chains, a loop id a node holds — **refused**, nothing applied |
| the `for`'s `ret` | the body node the aggregator sourced — one per loop by construction |
| a node with `when: C` | an `if` node `{ id: "<node>_if", if: C, then: { nodes: [node], ret: node.<node> } }`; sibling nodes sharing C fold into one `if` (`ret` = refused unless one — the public packages have no node `when`) |
| `aggregate: ["node.a", "node.b"]` + `deliverables[].node: node.agg` | `deliverables[].sources: ["node.<loop of a>", "node.b"]` — a fanned source becomes its loop, a scalar stays; the aggregator node is removed |
| a template's `node.agg.content` where a deliverable names `agg` | `deliverable.<id>.content` (v12's `extract-note-terms`, `extract-letter-terms`) |
| a template's `node.agg.content` where **no** deliverable names `agg` and `agg` has one fanned source | `node.<loop>.content` (two-documents' `agg_sections` → `node.standards.content`); otherwise refused naming the aggregator |
| `when` on an instance, a deliverable, a deliverable instance, a folder item | `if`, the expression unchanged |
| `forItem: "hpi"` (+ `when: C`) | `if: "item.id == \"hpi\""` (`… and (C)`) |
| `prompts[]` + `nodes[].prompt: <id>` | `nodes[].prompt: "<the prompt's file>"`; `prompts[]` removed; a prompt bound by two nodes → refused (none exist) |
| `{{ node.x.content \| concept_bullets }}` | `{{ node.x \| concept_bullets }}` (25 sites over four packages, every operand a scalar); inside a loop `e.content \| pipe` → `e.<ret> \| pipe` (§ 7.1; no such site exists at 25) |
| `index.json` `{ fields, items: [{ id, name, file, when? }] }` | `[{ id, file }]` (`fields` dropped); each item file gains the header `{ "label": <name>, "if": <when> }` |
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
- **The bump** (the 26 `#1018`, filed from this doc): both sets += 26 together; the registry lists
  26 — *landed 2026-10-27 (#1149) ahead of C, with the editor's authoring ceiling
  (`Templates.NewestSpecVersion`) held at 25 by name until #1164*; the seven packages republish and
  the pins move **after C** (Milestone 33's closing step).
  *(The content moved 2026-10-28, #1204 — Milestone 33 complete: the seven public packages migrated
  with the runner at engine v2026.10.13 (`consultologist-workflows` PR #43; each 0 errors / 0 warnings;
  the one hand edit "SNOMED tool guidance", the other invented labels kept) and published — general
  v2026.10.2, test/general v2026.10.12, example-two-documents v2026.10.2, example-v12-constructs
  v2026.10.2, example-classifier-scope v2026.10.3, example-conditional-documents v2026.10.2,
  example-structured-intake v2026.10.2 — every `latest.json` moved and every manifest served at 26; the
  production default pin `WorkflowPackages__Default` moved to `test/general@v2026.10.12` (the operator
  account's own pin was absent); a 26 smoke on the pinned package (job `43e4dfeb658642009bb167eb59cbc9cf`)
  completed 9/9 with a storage-8 record — the `standards` row `kind: for` over `snippet.standards`, body
  of three, `ret` section_instructions, `count` 9; body nodes' `in`; prompts by path; the document's
  `sources` [standards] and `composedHash` equal to the loop row's output hash. Gotcha for the next
  republish: pushing more than three tags in one push creates no GitHub event — dispatch `publish.yml`
  per tag.)*
- **Siblings:** #1063 audio (engine + Create page) rides B's release or its own; #1144
  (`array<text>` element order) is an engine and provenance matter, **not a 26 item**; the
  Milestone 31 editor rows keep landing at 25 and are re-keyed by C.

**Done when** (#1104): this doc exists with a decision or a named open question for every row of
the port list — it does; each Milestone 30 issue links its section; the bump issue is filed.

## 15. Deferred, by name

- **A flow node inside a `for`** — specVersion 27, #1173, `package-format-v27-design.md` (§ 1.4).
- **Dynamic fan sizes — the scheduler redesign** (#1062; § 6; specVersion 28): a count that
  changes during a run; the precondition for everything below it.
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
