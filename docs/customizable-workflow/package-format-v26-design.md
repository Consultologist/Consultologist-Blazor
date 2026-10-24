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
  "when": [
    { "values": [ "in_scope" ],                              "nodes": [ "plan" ],    "ret": "node.plan" },
    { "values": [ "out_of_scope", "needs_information" ],     "nodes": [ "letter" ],  "ret": "node.letter" } ],
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
  enum-format node (`classification`) — with one branch per set of values (a branch's `values[]`
  lists one or more) and an optional `else`. It is the classifier pattern spelled once: at 25
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
inputs only; an `if` node *inside* a `for` body skips the item's body nodes and **keeps the
count** (the item is a row that says "skipped by condition"). So every loop's length is known
when the job starts, `TotalBlockCount` stays the stored scalar it has been since #176, and the
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
