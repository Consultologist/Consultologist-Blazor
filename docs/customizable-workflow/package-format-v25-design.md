# specVersion 25 — templates reference the package by object

> **Status:** design. Decided with the package author on 2026-10-09; not yet implemented.
> Design-level, not a step-by-step plan — the layer issues follow this document.
>
> **One-line model:** a prompt template reads the package through **objects** — `input`,
> `node`, `data`, `item`, `macro` — so the template is the single source of what a node
> reads; the edges are **derived** from it at publish, `bindings` are retired, and a macro
> becomes one **instance** model with a side (prompt or output) and three placements
> (before, after, slot). An output-side macro becomes part of the node's output, so a node
> records **two hashes**: the model's bytes and what downstream read.

This is one bump, not two. Macro instances and object references both ride the same
mechanism (templates referencing the package by object, derived from the AST), and a
half-object state — `{{ section_name }}` beside `{{ macro.x }}` for one version — would
cost two validator paths, two migrations and a document nobody wants to read. The
delivery is phased (format → engine → editor, as v23 and v24 were); the format is not.

## Why

Three things the format does today pull against each other:

- **Bindings are a declaration that can disagree with the template.** A node declares
  `bindings: { section_name: item:name }`, the prompt declares `variables: [section_name]`,
  and the template writes `{{ section_name }}`. Three places say one thing; the validator
  spends rules keeping them consistent (an unbound variable, an unused binding). The DAG —
  scheduling, reachability, the diagram, the record's edges — is read from the declaration,
  not from what the template actually does.
- **A macro has two attachment sites with two vocabularies.** A deliverable macro is
  appended / before·after a source / slot (#845, #863); a node macro is before/after the
  prompt (#955, #957). The Documents dropdown spells out `before node:section-instructions`
  for a package with one source — the engine's stored value shown as a choice. The node pane
  has two sections ("Macros", "Output macros") for one library.
- **A slot's instruction is prose the author has to remember to write.** The model emits
  `[[slot:x]]` only if some text told it to; the editor offers a snippet (#941) and warns
  when none does (#942). The instruction belongs with the attachment, not in a paragraph.

v25 answers all three with one idea: **the template is the source of truth**, and everything
else — edges, instances, instructions — is read from it or sits beside it as a field.

## 1. Objects in templates

A Scriban template may reference five reserved objects. Nothing is declared; every member
access is checked at publish.

| Object | Member | Value |
|---|---|---|
| `input` | `input.<id>` | the supplied input, **typed** as declared (a date formats, a boolean branches, an object paths, an array iterates — the v8/v9 semantics, unchanged) |
| `node` | `node.<id>` | the named node's output as text; `node.<id> \| concept_context` renders a structured output through the concept-context renderer |
| `data` | `data.<id>` | a data scalar; a collection is not readable whole (a fan reads it item by item) |
| `item` | `item.id`, `item.name`, `item.content` (data fan) / `item.id`, `item.name`, `item.value` (input fan) | the fan item the instance runs for; refused on a node that does not fan |
| `macro` | `macro.<id>` | the macro **instance** attached to this node: its text (prompt side) or its instruction (output slot) — § 3 |

**Enum values carry a label** (§ 6): `{{ input.consult_specialty }}` renders the value,
`{{ input.consult_specialty.label }}` the label.

Renderers become pipes. A bare read of a structured output (`node.x` where `x` has a
contract) renders plain and the validator **warns**; `| concept_context` is the renderer a
binding's `as: concept-context` gave. The closed renderer set is unchanged.

**Raw prompts** (`raw: true`, v15) are not parsed and cannot reference objects — as today
they could carry no variable. A raw prompt that contains `{{` is refused, as today.

**Data files are not templates.** A standard's content is inserted as a string and never
re-rendered, so it contributes no edges. The one token that must work inside a data file
is `{{ macro.x }}` (an output slot's instruction placed by a standard — the
`section-instructions` pattern); the engine substitutes that literal in a **post-render
pass** over the rendered prompt, which also covers raw prompts. No other object is
substituted post-render.

## 2. Edges are derived; bindings are retired

At publish the validator parses each template with Scriban and walks the syntax tree:

- every `node.<id>` is an **edge** (scheduling dependency, reachability, the diagram);
- every `input.<id>`, `data.<id>`, `item.<field>`, `macro.<id>` is a **read**;
- `node.<id>` inside a conditional branch is still an edge — conservative, correct for
  scheduling, stated so no one expects laziness;
- **dynamic access is refused** (`node[name]`): static member access only;
- a reference that does not resolve is refused by name, the way an unknown macro
  placeholder is today.

The rules bindings had move to derived edges unchanged: refuse a cycle; refuse a fan
reading another fan over a different collection; two fans over one collection stay
item-aligned; a classifier is a dependency of whatever reads it.

**Refused by name at ≥ 25:** `nodes[].bindings`, `prompts[].variables`. Below 25 both stay
valid under their frozen rules. What stays declared is structure, not prose: a node's
`forEach` (what `item.*` is validated against), an aggregator's `aggregate`, a check's
`of`/`in`, a deliverable's `node`.

**Consequence, stated:** a template that writes `{{ node.patient-section-draft }}` names
that node, so a prompt that reads a node is in effect that node's prompt. Prompts that read
only `input`, `item`, `data` and `macro` stay shareable. One prompt used by two nodes with
*different* upstream nodes is not expressible; it needs a per-node alias map, which is
**deferred until a package needs it** (no published package does; #949 already edits the
prompt inside the node pane).

## 3. Macros — one library, one instance model

The library (`manifest.macros`: id, label, file, `optional`/`default`) is unchanged. An
**instance** is one attachment of a library macro, on a node or on a deliverable.

### Node instances — `nodes[].macros[]`

```jsonc
{ "id": "snomed_tool_guidance" }                                   // prompt · before · always (the bare form, unchanged)
{ "id": "tool_guidance", "use": "prompt", "at": "after", "when": "node:scope == in_scope" }
{ "id": "closing", "use": "output", "at": "after", "forItem": "hpi" }
{ "id": "letrozole_prescription_text", "use": "output", "at": "slot",
  "instruction": "Where the patient is on letrozole" }
```

- **`use`** — `prompt` (default; every v23/v24 entry) or `output`.
- **`at`** — `before` | `after` | `slot` (default `before`).
- **`when`** — the node-level grammar, unchanged (`input:`, `node:`, `item:id` at a data fan).
- **`forItem`** — a data-fan item, unchanged (v24).
- **`instruction`** — on an **output slot** only: the author's condition in prose
  (`"Always"`, `"Where the patient is on letrozole"`). The engine composes it with the
  format's **mechanics sentence** (§ 3.3) where the template's `{{ macro.<id> }}` sits.
- `optional` stays as declared on the library macro; a per-run choice applies to the output
  side only, as today.

| side · placement | what happens | where the text goes |
|---|---|---|
| prompt · before / after | today's node macro: the macro text, token-expanded, composed around the rendered template | the prompt — inside `inputHash` |
| prompt · slot | the macro text replaces `{{ macro.<id> }}` where it sits in the template | the prompt — inside `inputHash` |
| output · before / after | the macro text is composed around the model's output **and becomes the node's output** (§ 4) | the node's output — inside the new `outputHash`, outside `agentOutputHash` |
| output · slot | the instruction replaces `{{ macro.<id> }}` in the prompt; the model emits `[[slot:<id>]]`; the marker is replaced by the macro text at node completion (§ 5) | the prompt (instruction) and the node's output (text) |

**Only a text-contract node may carry an output instance.** A node whose output is a
structured contract (`concept-list` and every catalog schema, a custom schema), a
classifier, a check or a template is refused by name: appending prose to JSON breaks the
parse, a classifier's answer is one declared value, a check and a template have no model
output to decorate.

**Order** is the node's list order, per side and placement: before-blocks in list order,
then the prompt, then after-blocks — the rule v23 set, now applied to both sides. Prompt
rows and output rows under one placement never compose together, so their relative order
is meaningless and the editor reorders only within a side. Slot rows have no order: a
prompt slot is placed by its token, an output slot by the model.

**Identity** is (id, use, at, forItem). The same macro may appear in several placements on
one node (before the prompt *and* after the output); the same identity twice is refused —
two gates to one text is one row with `when: a or b`.

### Deliverable instances — `results[].macros[]`

Unchanged in meaning; the authoring vocabulary is **prepend / append / slot** at document
scope (#983). On the wire `prepend` is `before: <the aggregator's first source>` (the format
has no prepend kind), `append` the bare appended form, `slot` as today. Section-anchored
placement (`before`/`after: node:x`, `forItem`) is **no longer authored here** — it is a
node instance with `use: output` — but a loaded deliverable that carries one still renders
as itself; nothing is rewritten. The Documents pane lists document-scope entries only, with
a one-line note naming what is placed around sections and a link to the node; the append
picker excludes a macro attached anywhere on the deliverable (either site), and the node
pane's picker does the same, since a macro listed twice is refused.

### 3.3 The mechanics sentence is the format's, not the engine's

An output slot's prompt text is `<instruction>` + the mechanics sentence, where the
sentence is a **constant published with this format version**:

> *If so, emit `[[slot:<id>]]` verbatim and nothing else at that place; do not write that
> content yourself — it is filled in afterward, and only when it applies.*

It is the format's so the attested `inputHash` is determined by the package plus its
declared format alone. An engine-authored sentence would put engine-versioned text inside
every such hash and silently move them on a wording change. The sentence lives in
`package-format-v25.md` beside the grammar, and the engine's constant is pinned to it by
test (the `ProvenanceVersionSetTests` shape).

## 4. Output-side composition and the two per-node hashes

Today a node's `outputHash` is over the model's raw output, and every appended text is
placed at assembly, outside every node hash. v25 lets an output instance become **part of
the node's output** — what downstream nodes read through `node.<id>` and what assembly
aggregates. So a node row records:

- **`agentOutputHash`** — SHA-256 of the model's raw output (what `outputHash` was);
- **`outputHash`** — SHA-256 of the node's output after its output instances are composed
  (what downstream and assembly read). Equal to `agentOutputHash` on a node with no output
  instance.

This is a new per-node hash definition: `NodeHashVersion` moves to **6**,
hash-definitions.md § 4 gains the definition, the provenance record gains the field and
per-node **`appended`** entries (`{ kind: macro | slot, id }`) naming what was composed,
in order — the deliverable's `appended` shape, on the node row. The rerun verdict compares
`outputHash`, which is right: the composed output is the node's work.

Order at node completion, stated once:

1. `failIfEmpty` is judged on the **agent output**, before anything is composed.
2. Output slots attached to this node fill their markers (§ 5).
3. Output before/after instances compose around the result.
4. `outputHash` is taken; `agentOutputHash` was taken at step 1.

The provenance doctrine does not move: the node's hash still covers exactly what the next
reader received; the record names, per node, every byte that was not the model's.

## 5. Slot fill order — node first, then document

A slot marker `[[slot:<id>]]` is filled by the **node instance** at node completion when the
node carries an output slot for that id; whatever survives is filled by the **deliverable
instance** at assembly, as today. Node first, then document — the engine's own order.

- A node instance whose `when` refuses **removes** its marker and records the exclusion on
  the node row. It does not fall through: a document instance must not override a gate the
  author put on the node.
- A surviving marker that no instance sanctions is **removed**, as the assembler does today
  (an unsanctioned marker is never left in a deliverable), and the record says so.
- For a fan, the same order holds per item: a `forItem` instance fills its own item's
  output; other items' markers survive to the document level.
- The same macro as an output slot on a node *and* on the document is allowed: the node
  fills first, the document fills what remains.

**Publish rule for an output slot:** `{{ macro.<id> }}` must be present in the node's
template, or in at least one item of the collection the node fans (the data-file route).
Neither → refused by name. Items of a fanned collection that lack the token are named in a
**warning**, since a section whose standard carries no instruction cannot emit the marker —
intended in `section-instructions`, worth telling the author who expected every section
to be able to fill.

`{{ macro.<id> }}` inside **macro text** is refused: macros do not nest.

## 6. Enum value labels

Enum values stay snake_case (`^[a-z][a-z0-9_]*$`): they are bare literals in `when`, a
classifier's answer set, and printable where patient text is not. v25 adds an optional
free-text **label** per value:

```jsonc
"values": ["family_medicine", { "value": "medical_oncology", "label": "Medical oncology" }]
```

A plain string stays valid (label = value). The Create page shows the label and stores the
value; conditions, classifiers, reasons and the record keep the value;
`{{ input.consult_specialty.label }}` renders the label. A label is free text; a label on a
value not declared is refused. Classifier `values` take the same shape.

## 7. A form slot may be an object

v13 refuses `expectedContent: form` on an `object` slot by name; the held form response is
a flat id → string map. v25 lifts the refusal: a `form` slot may be an **object with
declared `fields`**. The response's keys fill the fields, each coerced to its field type;
a missing required field is refused at start the way a missing required input is; the slot
is stamped `form-response` once. The held-response payload moves to a version that holds
JSON values. Reading needs nothing new: `{{ input.intake.allergies }}`, a loop over an
array field, a typed `when` on `input.intake.on_letrozole`.

A **file door** on the Create page — "Import a questionnaire response…" on an object slot,
taking a JSON file: parse, validate against the declared fields, coerce, show the filled
fields for review, stamp a new file-sourced form-response origin kind carrying
`fileSha256` as a document does — is a **sibling issue**; its origin kind rides this
version's provenance bump. Declared fields are chosen over a schemaless `type: json`:
coercion, required-field refusal and typed conditions need the shape, and an editor helper
derives `fields` from a sample response. A schemaless type is deferred until a form's
shape genuinely cannot be known in advance.

## 8. Preludes are refused

At ≥ 25 `manifest.preludes` and `prompts[].prelude` are refused by name. The editor's
migration (#963) already turns each prelude into a bare prompt-side node macro on any
upgrade past 23, so a package arriving at 25 has none; the 25 rung adds the refusal, a
schema-catchable conformance vector, and this sentence in the migration section. The
renderer keeps its prelude seam for ≤ 24 packages.

## 9. The record and the registries

- **provenance** (bump): `nodeOutputs[].agentOutputHash`, `nodeOutputs[].appended[]`,
  `hashVersion` 6 with its definition in hash-definitions.md § 4; `nodes[].bindings` becomes
  a derived field — the sources a node read with the renderer each was read through —
  documented as derived from the template at publish; the file-sourced form-response origin
  kind (§ 7); the `[[slot]]` removal/exclusion rows on a node.
- **package-format** (bump to 25): the object grammar (§ 1), derived edges and the
  refusals (§ 2, § 8), the instance shape (§ 3), the mechanics sentence (§ 3.3), enum
  labels (§ 6), the object form slot (§ 7); schema generated from the model; conformance
  vectors for every refusal above (the cross-field ones `SHAPE_BLIND`).
- **agents**: no change — the catalog and its contracts are read as today.

## 10. The editor

- **Node pane.** One **Macros** section grouped **Before / After / Slot**; each row: the
  macro, a **Prompt / Output** toggle, applies-when (the existing builder), the fan-item
  scope on a data fan, and the **instruction** field on an output-slot row; reorder arrows
  that move a row only within its side; slot rows unordered. This replaces today's "Macros"
  and "Output macros" sections.
- **Inputs and Variables become one derived, read-only Reads list** — what the template
  references, each row linking to the thing — on the node pane; the prompt pane's variable
  form goes. "+ variable" becomes an **insert reference** palette that inserts
  `{{ input.x }}` or `{{ node.x | concept_context }}` at the cursor; the renderer is the pipe,
  with a row select that rewrites it. The desk flips from "every variable is bound" to
  "every reference resolves", in the validator's words.
- **Macro pane.** Title / id / file → macro text + placeholders → **Instances**, one entry
  per attachment (node or deliverable) with the row's controls.
- **Deliverables pane** (#985 renames it): document scope only — prepend / append / slot
  with applies-when, the one-line note for section-placed instances, the two-way picker
  exclusion (#983).
- **Inputs pane:** a label field beside each enum value chip; the object-form slot's fields
  as today's object fields.

## 11. Migration 24 → 25 (the editor's upgrade, mechanical and byte-identical in what the model sees)

1. For each node, rewrite its template: `{{ <variable> }}` → the bound source as an object
   reference (`{{ item.name }}`, `{{ input.consult_draft }}`,
   `{{ node.identify-problem | concept_context }}` carrying the renderer); `{{ if <var> }}`
   likewise. Then drop `bindings` and `prompts[].variables`.
2. Each existing node macro is `use: prompt` (the default; the bare form is unchanged on the
   wire).
3. Each deliverable macro anchored `before`/`after: node:x` (± `forItem`) becomes a node
   instance `{ id, use: output, at, forItem }` on that node; appended and slot entries stay
   on the deliverable. **Byte-identical output:** section-anchored text composed at assembly
   around a leaf node's part and the same text composed into that node's output read the
   same in the assembled document — but the node's `outputHash` now covers it (§ 4), which
   is the point, and the record says so.
4. Preludes are already gone (#963); a 24 package carrying one is migrated by the same path.
5. Hand-written slot instructions keep working; an author may move the condition into the
   instance's `instruction` field and delete the paragraph.

## 12. Phasing

The v23/v24 shape, one layer per PR, each green on its own:

- **A — format** (`Consultologist.PackageFormat`, the package-format registry): the object
  grammar and the AST walk, derived edges, refusals, the instance shape, enum labels, the
  object form slot, the mechanics sentence; `AcceptedSpecVersions` += 25 (the staged gap
  reopens in the editor).
- **B — engine + provenance**: object exposure to Scriban and the post-render macro pass,
  output composition with the two hashes and per-node `appended`, node-first slot fill,
  `NodeHashVersion` 6, the held-response payload v2, the provenance bump;
  `SupportedSpecVersions` += 25 (the gap closes); an engine release carries it to prod.
- **C — editor**: the Macros section, the derived Reads list and the insert palette, the
  Instances view, enum labels, the migration.
- **Siblings:** #985 (Deliverables rename, first), #983 (narrowed to the Deliverables pane),
  the questionnaire import door. **Folded in:** #969 (prompt-side slot = `{{ macro.x }}`),
  #984 (node-scoped slot = a node instance). **Made structural:** #941, #942.

## Deferred, by name

- A per-node alias map for one prompt with different upstream nodes (§ 2).
- A schemaless `type: json` input (§ 7).
- Node-level fill of a marker emitted by a *different* node — a marker is filled where it
  was emitted or at the document; routing it elsewhere has no case.
