# specVersion 25 — the template is the source of truth

> **Status:** design, **revision 2** (2026-10-11). Supersedes revision 1 (2026-10-09, PRs #986
> and #992), which stands in git history; where the two disagree, this revision is the
> decision. Decided with the package author; not yet implemented. Design-level, not a
> step-by-step plan — the layer issues (#987, #988, #989, #990, #991) are re-cut from it.
>
> **One-line model:** a prompt template reads the package through **objects** — `input`,
> `node`, `snippet`, `item`, `macro` — so the template is the single source of what a node
> reads and the edges are **derived** from it at publish. A node declares one **format**
> (shape, producer, macro rule) in place of contracts, schemas and node kinds; the catalogue
> of formats lives in the agents registry and binds each to the agent, operation or nothing
> that produces it. **Snippets** are templates that are read; **macros** are text that is
> placed after the model's reply, and a node records **two hashes**: the model's bytes and
> what downstream read.

This is one bump, not two. Objects, formats, snippets and macro placement all ride one
mechanism — the package is read from its templates and its format files, never from a
parallel declaration — and a half state would cost two validator paths, two migrations and
a document nobody wants to read. The delivery is phased (format → engine → editor, as v23
and v24 were); the format is not. The one migration review per published package is the
cost that matters, and one bump pays it once.

## Why

Four things the format does today pull against each other:

- **Bindings are a declaration that can disagree with the template.** A node declares
  `bindings: { section_name: item:name }`, the prompt declares `variables: [section_name]`,
  and the template writes `{{ section_name }}`. Three places say one thing; the DAG is read
  from the declaration, not from what the template does.
- **Shape is said five ways.** No contract means prose; `schemas` carries a copied JSON body;
  `schemaRefs` names a catalogue contract; `customSchemas` carries the author's own; and
  `kind: classifier | check | template` says a shape by naming a behaviour. The agent that
  serves each is implied, never written where a reader can find it.
- **A macro has two attachment sites with two vocabularies**, and a prompt-side macro is a
  thing a template could simply read.
- **A slot's instruction is prose the author has to remember to write.** The model emits a
  marker only if some text told it to; the instruction belongs with the macro.

v25 answers all four with one idea: **the template is the source of truth**, a node's
**format** is the one word for its shape, and everything else — edges, instances, agents —
is read from those or sits beside them in a registry the package never carries.

## 1. Objects in templates

A Scriban template may reference five reserved objects. Nothing is declared; every member
access is checked at publish, and **every object has a closed member set** — any other
member is a publish refusal naming it.

| Object | Members | Value |
|---|---|---|
| `input.<id>` | the value (bare) | the supplied input, **typed** as declared — a date formats, a boolean branches, an object paths, an array iterates (the v8/v9 semantics, unchanged) |
| `node.<id>` | `.content`, `.value` | `.content` is the node's **composed** output as text (§ 6); `.value` is the parsed output on an `object`, `enum` or `array` format — fields path, arrays iterate, every path checked against the format's schema at publish. The bare read is refused by a sentence naming `.content`. |
| `snippet.<id>` | `.id`, `.name`, `.content` | a snippet rendered (§ 4) |
| `snippet.<folder>` | iterable | each item a snippet with the three members above; folder size is resolved at job start (§ 4) |
| `item` | `.id`, `.name`, `.content` (snippet fan) / `.id`, `.name`, `.value` (input fan) | the fan item the instance runs for; refused on a node that does not fan |
| `macro.<id>` | `.id`, `.name`, `.content`, `.prompt` | `.content` is the macro's text rendered, read prompt-side like a snippet; `.prompt` is the inline macro's prompt text: its instruction (rendered) composed with the mechanics sentence (§ 5.3) |
| `macro.<folder>` | iterable | each item a macro with the four members above (no manifest shape at 25 — see Deferred) |
| `run` | `.date`, `.job`, `.package`, `.host` | the run facts, as the v11 `{{run:…}}` tokens gave them (A4, #999, 2026-10-13) |
| `profile` | `.name`, `.signature` | the profile facts, as the v11/v12 `{{profile:…}}` tokens gave them; a `profile.signature` read signs, and a deliverable is signed once |

**Renderers are pipes, and the pipes are typed (#1058).** `{{ node.extract.content |
concept_context }}` is the attested, byte-stable rendering a binding's `as: concept-context`
gave; the closed renderer set is unchanged. A renderer renders the `.content` of a
`concept_list` node and only that: the text of a node of another format, a node's `.value`, an
input's, a snippet's, an item's or a macro's text, a folder or a literal piped through a
renderer is refused by name at publish (and at job start, the same walk); an operand the walk
cannot type (an assigned or loop variable, a nested expression) is judged when it renders. A
loop over `node.extract.value.concepts` is the package's own rendering. Both are allowed; a
bare `.content` read of a structured format renders the JSON and the validator warns — the
same read through a renderer pipe does not.

**Everything is a template.** Snippets render (§ 4), macro text renders (§ 5), prompts
render. The revision-1 "data files are not templates" rule and its post-render pass are gone:
a snippet that carries `{{ macro.x.prompt }}` renders it like any other reference.
**Raw prompts** (`raw: true`, v15) are refused by name at ≥ 25. A literal `{{` in text is
escaped the Scriban way (`{{ "{{" }}` or a `{%{ … }%}` block); escaping is the author's
until the code editor (§ 13) does it on paste, and the desk names the line that fails to
parse.

## 2. Edges are derived; bindings are retired

At publish the validator parses each template with Scriban and walks the syntax tree:

- every `node.<id>` is an **edge** (scheduling dependency, reachability, the diagram);
- every `input.<id>`, `snippet.<id>`, `item.<member>`, `macro.<id>` is a **read**;
- a reference inside a conditional branch is still an edge — conservative, stated;
- **dynamic access is refused** (`node[name]`): static member access only. A literal integer
  index on an array path (`node.extract.value.concepts[0].term`) is static and steps into the
  element; a string-literal index is a member spelled the only way Scriban allows a hyphen
  (`node["identify-problem"].content` — Scriban reads `node.identify-problem` as a subtraction);
- a reference that does not resolve, or a member outside the closed set, is refused by name;
- **loop, `with` and `capture` variables are typed from what they bind** (A2, #997,
  2026-10-13): `for s in snippet.sections` gives `s` the snippet members, `for c in
  node.extract.value.concepts` the schema's element, `for h in input.history` the declared
  element; anything else is untyped and its members pass. A variable named one of the five
  objects is refused.

**The second edge source** is a computed node's parameters (§ 3.6): each value in `with`
is an object reference in the same grammar, parsed by the same walker, typed against the
parameter's declaration. A template reads through prose; a computed node reads through
typed slots; both yield edges.

The rules bindings had move to derived edges unchanged: refuse a cycle; refuse a fan
reading another fan over a different collection; two fans over one collection stay
item-aligned; a node whose value is read is a dependency of the reader. What stays declared
is structure, not prose: a node's `forEach`, an aggregator's `aggregate`, a deliverable's
`node` and `check` — each spelled as an object reference at 25 (`snippet.<folder>`,
`input.<id>`, `node.<id>`; § 16, #1037).

**Consequence, stated:** a template that writes `{{ node.identify-problem.content }}` names
that node, so a prompt that reads a node is in effect that node's prompt. One prompt used by
two nodes with *different* upstream nodes needs a per-node alias map, **deferred until a
package needs it**.

## 3. Formats

A **format** is one file saying what a node's output is, who produces it, and whether
macros may be placed on it. It replaces `schemas`, `schemaRefs`, `customSchemas`,
`output.schema` and the `classifier`, `check` and `template` node kinds. A node declares one.

### 3.1 The format file

```jsonc
{
  "type": "object",            // the INPUT type vocabulary, reused: text | enum | boolean | number | date | object | array
  "producer": "custom",        // attested | custom | rendered | computed (§ 3.2)
  "outputMacros": false,       // may macros be placed on this output — true only on text
  "description": "…",
  "schema": { … },             // required on every type object (a custom file carries the package's shape, a catalogue file its own); refused elsewhere
  "values": [ … ],             // only with type enum (the § 8 shape) — or the set comes through a parameter of kind values (classification)
  "parameters": { … }          // only with producer attested or computed: name → { kind: node | text | values, format?: <catalogue id> }
}
```

The file carries **no id**. The id belongs to whoever lists the file — the manifest for a
package-local format, the catalogue index for a catalogue one — as a macro file has no id
inside it. One file shape, one validator, for both.

### 3.2 Producer classes

| Producer | Meaning | What the file must carry | Who stands behind it |
|---|---|---|---|
| `attested` | a purpose-built agent from the catalogue, git-attested | `schema` if `type: object`; `parameters` if it takes typed input (classification's `values`) | the catalogue entry's agent |
| `custom` | the package's own shape, served by the catalogue's generic structured agent and **validated by the engine** (§ 3.4) | `schema`, `type: object` | the generic agent + the engine's validation |
| `rendered` | no agent: the output is the rendered prompt's bytes (the v12 template kind) | `type: text`, `outputMacros: false` | the engine |
| `computed` | an engine operation over typed parameters; no prompt (the v12 check kind) | `parameters`, `type: object` with the result `schema` | the engine's published operation set |

Cross-checks at publish (A1, #996, 2026-10-13): `schema` ⇔ `type: object` — every object
format carries its shape, since the engine validates every object reply (§ 3.5); `custom` and
`computed` ⇒ `type: object`; `rendered` ⇒ `type: text`; `outputMacros` true ⇒ `type: text`;
`rendered` and `computed` ⇒ `outputMacros: false`; `values` ⇒ `type: enum`, and an enum takes
its set from `values` or from one parameter of kind `values`, never both; `parameters` ⇒
`attested` or `computed`, required on `computed`. A parameter means one thing per producer: on
a computed format it is an operand the engine evaluates; on an attested one it is typed input
the engine passes to the agent (the classification trailer path) — one declaration, so the
node's `with` is typed the same way for both and the record carries the same thing. **Every
non-custom format comes from the catalogue; a package-local format is always `custom`** (a
package-local file may not claim `attested` — no agent would stand behind a shape the
catalogue never published). The catalogue index binds by producer: `attested` ⇒ an agent,
`computed` ⇒ an `op` the engine publishes, `rendered` ⇒ neither, `custom` ⇒ never an entry.

### 3.3 The catalogue

The catalogue lives in the **agents registry** under one pin — today's
`agents/output-contracts.json` widened — because an attested entry and its agent must be
versioned together. The index has two sections:

```jsonc
// agents/formats.json
{
  "version": "v2026.11.1",
  "formats": {
    "prose":          { "file": "formats/prose.json",          "agentName": "consult-prose",      "agentVersion": 3 },
    "classification": { "file": "formats/classification.json", "agentName": "classification",     "agentVersion": 1 },
    "concept_list":   { "file": "formats/concept_list.json",   "agentName": "concept-extraction", "agentVersion": 1 },
    "rendered":       { "file": "formats/rendered.json" },
    "terms_subset":   { "file": "formats/terms_subset.json",   "op": "terms_subset" }
  },
  "producers": {
    "custom": { "agentName": "custom-json", "agentVersion": 1,
                "trailer": "Answer with one JSON object conforming to this schema." }
  }
}
```

| Entry | Type | Producer | Output macros | Replaces |
|---|---|---|---|---|
| `prose` | text | attested | allowed | a node with no contract |
| `rendered` | text | rendered | no | `kind: template` |
| `classification` | enum | attested | no | `kind: classifier` — parameter `values` (kind `values`) carries the answer set |
| `concept_list` | object | attested | no | the `schemaRefs` entry |
| `terms_subset` | object `{ pass, uncovered[], untested[] }` | computed | no | `kind: check` — parameters `of`, `in` (nodes of format `concept_list`), `failWith` (text) |

*(#1037, 2026-10-15: the catalogue ids and the computed op are snake_case like every declared
id — `concept_list`, `terms_subset`, op `terms_subset`, since formats@v2026.10.2 (v2026.10.1
published the hyphenated spelling; the engine read both until it re-pinned, and reads only the
snake_case form since). The agent name `custom-json` is not a format id and stays. § 16.)*

`custom` is a **producer binding, never a format entry**: a custom format is a package's
own, so its agent is bound once and every custom format in every package goes through it.
Agent identity is two fields wherever code reads it — `agentName` (string),
`agentVersion` (integer) — and `name@N` is a display form only, as `output-contracts@v…`
is today.

**Publication** follows the catalogue's path today: the agents repo's CI publishes an
immutable version to the public registry, files first and the index last
(`formats/vYYYY.MM.N/formats.json`, `formats/vYYYY.MM.N/formats/<id>.json`), and refuses an
index whose attested entry names an agent whose response format is not that entry's schema,
an index naming an operation the engine does not publish, or a `producers` section missing
a class the spec says needs one. The stranding check widens from "every published schema
still resolves" to "every published format reference still resolves with the same
parameters". The engine loads the index at the pin, then each file.

**Referencing from a package.** A node names a catalogue id and **never a version** — the
#923 lesson: a package that pinned a catalogue version was stranded by every re-pin.
Publish resolves the id through the pinned index to the file, hashes the file's canonical
JSON, and stamps that **formatHash** beside the catalogue version; the starter runs the
package against its *own* stamped catalogue version, as #923 phase 2 does today. The
identity of a shape in the record is the bytes of its file, not its name. The activity's
fallback to the engine's current catalogue (for pre-#923 replays) **refuses** at 25 rather
than resolving, so a stale payload never silently runs a different agent.

**Three layers hold a piece each:** the format spec (package-format registry) owns the
producer vocabulary and what a file must carry per class; the catalogue index owns the
binding; the engine owns the implementation — one dispatch on the class, four paths.

### 3.4 The custom tier — one generic agent, engine-validated

A custom format is served by **one** catalogue agent, `custom-json`, with the package's
schema placed in the prompt rather than welded into an agent. At dispatch the engine
appends the index's `trailer` sentence and the schema to the rendered prompt — the
classification-trailer discipline, the sentence a published constant — sends, strips any
code fences from the reply, parses, and **validates against the schema** (§ 3.5). The record
stamps the generic agent, the formatHash and the validation row.

This **retires** #760's content-addressed `custom-{hash}` agents, run-time agent creation
and the `CustomAgents__Model` gate: the custom tier is available on production the day the
generic agent exists, with no operator step per package. Packages at 22–24 carrying custom
schemas run through the same path, since the engine owns the mechanism and the format never
promised one. The two tiers remain: a catalogue shape served by an agent built for it; a
package shape served by a generic agent and enforced by the engine.

**The #933 chip stays and changes its sentence** to *"custom shape, engine-validated"*,
with the validation outcome beside it. "Unattested" would now be false — the agent is
attested; what is package-authored is the shape, as every prompt is.

### 3.5 Validation and the node's policy

Every reply on an `object`, `enum`, `array`, `boolean`, `number` or `date` format is
validated against the format after parsing and before anything is composed; the outcome is
a node-row `validation { valid, errors[] }`. What happens on an invalid reply is
**package-defined on the node**, not on the format (a format is reusable; the consequence
depends on what this node feeds) and not by the user at run time (two runs of one package
must not disagree):

```jsonc
"onInvalid": "fail"            // fail | retry | flag — required by the editor only when the format is validatable
```

`retry` re-sends once with the validation errors as a repair message — a later rung,
named here so the word is reserved. A `text` format validates nothing and carries no policy.

### 3.6 The node at 25

```jsonc
{ "id": "assessment", "prompt": "assessment", "format": "prose",
  "macros": [ { "id": "closing", "at": "after" } ] }

{ "id": "meds", "prompt": "meds", "format": "medication_list", "onInvalid": "fail" }

{ "id": "scope", "prompt": "scope", "format": "classification",
  "with": { "values": [ "in_scope", "out_of_scope" ] } }

{ "id": "coverage", "format": "terms_subset",
  "with": { "of": "node.extract", "in": "node.source",
            "failWith": "The note names problems the source does not." } }

{ "id": "cover_letter", "prompt": "cover_letter", "format": "rendered" }
```

- **`format`** — a catalogue id or a package-local id from `manifest.formats[]`
  (`{ id, file }`, local files only; catalogue ids are reserved).
- **`id`** — snake_case at ≥ 25 (`^[a-z][a-z0-9_]*$`, as every other declared id), because a
  template reads a node as a Scriban member and Scriban reads `node.identify-problem` as a
  subtraction (A3, #998, 2026-10-13). A hyphenated id is refused by name with the rename in
  the sentence; the migration (§ 14) rewrites the id and every reference. The bracket
  spelling `node["identify-problem"].content` remains a valid static access, never the
  expected one.
- **`prompt`** — required unless the producer is `computed`, refused then.
- **`with`** — only the parameters the format declares, every declared one supplied; values
  are object references or literals typed by the declaration (A2, #997, 2026-10-13): a
  `node` parameter is a bare `node.<id>` (it names the node, so it is an edge; `.content`/`.value`
  refused there) whose format must match the declaration's when one is named — `of` must name
  a node of format `concept_list`; a `text` parameter is a string literal or an `input.<id>`
  reference to a text input; a `values` parameter is a list of snake_case values (the enum
  rules) or an `input.<id>` reference to an enum input. A string that is exactly an object
  reference is read as one; anything else is a literal.
- **`onInvalid`** — § 3.5.
- `forEach`, `aggregate`, `when` (v18), `failIfEmpty`, `reproducible` — unchanged in meaning;
  *(#1037, 2026-10-15: spelled as object references at 25 — `forEach: snippet.<folder>` |
  `input.<id>`, `aggregate: ["node.<id>"]` — and `when` is a Scriban boolean expression; § 16.)* *(B5
  #1004, 2026-10-14: `failIfEmpty` moves up to the node — `nodes[].failIfEmpty` — because
  its ≤24 home, `output`, is refused at 25; refused by name below 25. Judged on the reply
  before anything composes (§ 6 step 3): a blank reply on a text format, an empty top-level
  array on an array format; a rendered or computed node takes none. The registry's
  `package-format-v25.md` gained the key at A5, 2026-10-16.)*
- **Refused by name at ≥ 25:** `kind`, `values`, `op`, `of`, `in`, `failWith`,
  `output`/`schema`, `bindings`.

A deliverable's `check: node.coverage` keeps its meaning: failed is a third state beside
produced and skipped, and `failedDocuments` carries the sentence and the uncovered terms.
A check nobody's deliverable names is refused; an operand that fails fails the checked
document; a node serving only a check keeps the reachability exemption. Downstream
templates may read `node.coverage.value.pass` and `.uncovered`, which a check cannot offer
today.

### 3.7 Resolution and the record

1. **Publish:** resolve each catalogue id against the pinned index; stamp the catalogue
   version and, per format, the formatHash and the agent name and version (or operation).
2. **Job start:** load the package's stamped catalogue version; build each node's activity
   input with format id, producer, and the agent, custom binding or operation.
3. **Activity dispatch** on the producer: `attested` → send to Foundry by name and version;
   `custom` → trailer + schema, send to the generic agent; `rendered` → no call, the reply is
   the prompt; `computed` → no call and no prompt, run the operation.
4. **Validate** (§ 3.5), then compose (§ 6).
5. **Record**, per node: `format` (id), `formatHash`, `catalogVersion`, `producer`,
   `agentName`/`agentVersion` where an agent ran, `validation`, and the two hashes of § 6.

### 3.8 Release and exposure

The catalogue is the operator's. A new entry is authored in git — the format file, its line
in `agents/formats.json`, an agent yaml if attested — merged (the publish: CI creates the
Foundry version, asserts its number, runs the checks of § 3.3, publishes the immutable
version), and activated by the engine's catalogue pin, with an engine release first if a
new operation is needed. Package authors only consume it.

The editor needs a **catalogue read endpoint** (the client never reads the catalogue
today) and shows, read-only: the format list — id, description, type, producer — as the
picker; each entry's parameters, so the node pane renders the right fields; the format file
including its schema, in the same pane a custom format uses, so an author sees what
`node.x.value` holds; and one detail line, *served by concept-extraction@1, attested*. The
agent yaml is not exposed: no authoring decision depends on it.

**The promotion path:** an author ships a custom format now, engine-validated; if the shape
proves itself the operator adds a purpose-built agent and a catalogue entry, and the next
package version references the id. Custom is the proving ground, the catalogue the reviewed
set, and only the operator moves things between them.

## 4. Snippets — text that is read

`manifest.data` (scalars and collections) becomes **`manifest.snippets`**: a snippet is a
`.md` file that is a full Scriban template, and a **folder** of snippets is today's
collection (an `index.json` listing id, name and file per item). The names: `+ Snippet`,
`+ Snippet folder` in the editor.

- A snippet is used by **reading** it — `{{ snippet.standards.content }}`, or
  `{{ for s in snippet.sections }}{{ s.content }}{{ end }}` — from a prompt, another snippet,
  or a macro's text. It is **never attached** to a node or a deliverable: an inline read at
  the top of a template is the prompt-side "before" of v23, and a Scriban `if` around it is
  that attachment's `when`, with the edge derived like any other.
- A snippet may carry a **`when`** that matters **only inside a folder iteration** and may
  read **inputs only**: a folder's effective size is then resolved at job start, so a fan's
  item count stays the start-time scalar the scheduler, the progress total and the record's
  item rows depend on. A `when` on a snippet that is not in a folder, or one reading a node,
  is refused by a sentence saying the template's own `if` does that. Conditions on anything
  but inputs are the **v26** scheduler redesign, by name.
- A node fans over a folder as it fans over a collection today — spelled `forEach:
  snippet.<folder>`, one grammar beside `input.<id>` (A4, #999 spelled it `snippet:<folder>`;
  #1037 moved it to the object spelling, § 16; the bare folder name is not accepted);
  `item.content` is the item's rendered content (the ≤24 `item:content` under the object spelling, § 14).

Snippets may read snippets, inputs, nodes, `macro.<id>.content` and `macro.<id>.prompt` (the
section-instructions pattern: a standard that carries an inline macro's prompt text). A snippet is
a macro without an instruction, and the two differ only in what may be done with them: a
snippet is only ever read; a macro may also be placed (§ 5).

## 5. Macros — text that is placed

The library (`manifest.macros[]`: id, label, file, **`instruction`** (mandatory),
`optional`/`default`) is the only kind of placed text. A macro's text is a Scriban template
that renders where it is used and may read snippets, inputs and node outputs. It may also
be **read** inline as `{{ macro.<id>.content }}`, prompt-side, exactly as a snippet is — the
same words in a prompt and after a reply are one file. What a macro's text may contain does
not depend on how it is read (§ 5.2). The **instruction is a template too** (#1044): rendered
where `macro.<id>.prompt` is read, over the same scope, with a snippet's reads; its reads join
the closure of the node whose prompt reads it inline, and a `.prompt` read is an edge to it in
the reference graph (§ 5.2).

### 5.1 Instances

A macro is used in three ways, all on the **output side**:

```jsonc
// on a node — nodes[].macros[]
{ "id": "closing", "at": "after" }
{ "id": "tool_note", "at": "before", "when": "node.scope.value == \"in_scope\"", "forItem": "hpi" }

// on a deliverable — deliverables[].macros[]: document scope only
{ "id": "disclaimer", "at": "prepend", "when": "input.jurisdiction == \"on\"" }
{ "id": "signature", "at": "append" }
```

and **inline**, by writing `{{ macro.letrozole.prompt }}` in a prompt or a snippet where the
macro goes: that read *is* the inline macro's declaration — the model is told the instruction and asked
to write `{{ macro.letrozole.content }}` where the content belongs, and the reply renders it
(§ 7, #1044) — so there is no `at: slot`, no `use` toggle, and no instruction override on an
instance (one instruction per macro; a different instruction is a different macro). The node
pane lists inline macros as a derived, read-only section beside the declared before/after rows.

- **`at`** — `before` | `after` on a node; `prepend` | `append` on a deliverable.
- **`when`** — a Scriban boolean expression over the objects a template reads (§ 16, #1037;
  until then the v10 grammar).
- **`forItem`** — a fan item, unchanged (v24).
- Only a node whose format has **`outputMacros: true`** may carry an instance or an inline
  read; anything else is refused by name — prose appended to JSON breaks the parse, an enum
  is one value, a rendered or computed node has no reply to decorate.

**Order** is the node's list order per placement: before-blocks in order, the reply, then
after-blocks. Inline macros have no order: the model places the read. **Identity** is
(id, at, forItem); the same macro before *and* after is allowed, the same identity twice is
refused — two gates to one text is one row with `when: a or b`.

*(B8 #1027, 2026-10-14 — a deliverable instance's text is rendered over the job's scope in
its own activity when the deliverable composes, prepend-blocks in order, the sections, then
append-blocks, attributed in document order. Its node reads are the **deliverable's edges**
(§ 2): the document composes only once every one has settled, and the fire set skips the
deliverable when one was gated. A read of a fanned node is refused by name — a deliverable has
no item; place the macro on the node to read its items. The deliverable's own identity is
(id, at), each gate judged on its instance.)*

### 5.2 The reference rule and cycles

Nothing on the reference path from a macro's text may reach a `macro.<id>.prompt`: the text
is placed after a reply somewhere, and there is no second call to honour a marker it would
carry — and that holds however else the text is read, so one rule covers inline reads too.
That is the one transitive rule, checked over the derived reference graph. Cycles anywhere
in that graph — snippet to snippet, macro text to snippet to macro — are refused by a
depth-first search that names the path.

### 5.3 The mechanics sentence is the format's

`macro.<id>.prompt` renders as `<instruction>` — itself rendered over the reading template's
scope (#1044) — + a sentence that is a **constant published with this format version**,
appended after the render so the read it names reaches the model as written:

> *If so, write `{{ macro.<id>.content }}` verbatim and nothing else at that place; do not write
> that content yourself — it is filled in afterward.*

It is the format's so the attested `inputHash` is determined by the package plus its
declared format alone; an engine-authored sentence would put engine-versioned text inside
every such hash. The sentence lives in `package-format-v25.md` beside the grammar, and the
engine's constant is pinned to it by test (the `ProvenanceVersionSetTests` shape). The
custom trailer (§ 3.4) follows the same discipline from the catalogue side.

## 6. Output composition and the two per-node hashes

A node row records:

- **`agentOutputHash`** — SHA-256 of the reply's bytes: the model's, or the rendered prompt
  on a `rendered` node (the proof that nothing but rendering happened), or the operation's
  result on a `computed` node;
- **`outputHash`** — SHA-256 of the node's output after its instances are composed: what
  `node.<id>.content` and assembly read. Equal to `agentOutputHash` on a node with no instance.

`NodeHashVersion` moves to **6**, hash-definitions.md § 4 gains the definition, and the
record gains per-node **`appended[]`** entries (`{ kind: macro | slot, id }`) in order. The
rerun verdict compares `outputHash`.

Order at node completion, stated once:

1. The reply is taken; `agentOutputHash` is computed. On a structured format, fences are
   stripped and the reply parsed.
2. **Validation** (§ 3.5) and the node's `onInvalid`.
3. `failIfEmpty` is judged on the reply, before anything is composed.
4. The reply is **rendered as a template** over the node's scope (§ 7, #1044) — its
   `{{ macro.<id>.content }}` reads are the inline macros; a reply that does not render fails the node.
5. Before/after instances compose around the rendered reply.
6. `outputHash` is taken.

## 7. Inline macros — the reply is a template (#1044, 2026-10-16; the word, #1047)

The model writes `{{ macro.<id>.content }}` where an inline macro's instruction told it to, and the
node's completion **renders the whole reply as a Scriban template** over the node's own
scope — the scope its prompt rendered over: inputs, the nodes it reads (its derived edges),
snippets and folders, the fan item, every macro, the run and profile facts. The rendered text
is the reply the instances compose around (§ 6). One object form in, one out; the fill is a
second render, at the node, at completion — the revision-1 "node first, then document" order
and the document-level slot are gone. (Before #1044 the model emitted `[[slot:<id>]]` and the
completion replaced sanctioned markers; the marker is the ≤24 grammar now.)

- **What a reply may read is what a template may read.** A `{{ macro.<id>.content }}` is that
  macro's text, rendered; `{{ node.extract.content }}` is the node's text, and so on. Nothing is
  sanctioned, dropped or rewritten.
- **A reply that does not render fails the node, non-retryably** (decision): a parse error,
  or a read the scope has no value for (a node the prompt does not read, an undeclared input,
  an unknown member) — the renderer's sentence on the node row (`Node '<id>' reply does not
  parse: …` / `Node '<id>' reply reads node.x, which this render has no value for.`); the
  prompt or the agent is at fault. The reply is hashed as it came (`agentOutputHash`), before
  the render. For a fan, per item.
- **Attribution:** `appended[] { kind: inline, id }` (#1047; provenance@v2026.10.4) names each macro text the reply itself
  read, in render order, once per read (a read inside a filled text is that text's, not an
  entry). `excluded[]` keeps only instance `when` refusals.
- `profile.signature` in a reply (or an instruction) is the node's fact — empty unless a
  deliverable placement carried the signature; signing stays the deliverable's static rule.

**Publish rule (unchanged):** a `macro.<id>.prompt` read must be reachable from the node's
template — directly, or through a snippet it reads, or through at least one item of the folder
it fans. None → the macro is unused and the validator says so. Items of a fanned folder that
lack the read are named in a **warning**: a section whose standard carries no instruction
tells the model nothing — intended in `section-instructions`, worth telling the author. The
`.prompt` read is also what makes a fill's node reads edges of the node (`EdgesOf`): a macro
the prompt never instructed but the reply reads still renders — and fails loud if its text
reads a node not in the scope.

## 8. Enum values — unchanged at 25

Enum values stay snake_case (`^[a-z][a-z0-9_]*$`): quoted literals in a 25 `when` (§ 16), a
classification's answer set, printable where patient text is not. Revision 1 and the first
cut of revision 2 added an optional free-text **label** per value (`{ value, label }`, with
`input.<id>.label` on the object and the Create page showing the label). That is **moved to
v26** (2026-10-12): it is independent of everything else in this version, it is the one
change that touches the Create page's rendering of an input, and 25 is large enough. At 25
`values` entries are strings, as at 24, and `input.<id>` has no `.label` member.

## 9. A form input may be an object

v13 refuses `expectedContent: form` on an `object` input by name; the held form response is
a flat id → string map. v25 lifts the refusal: a `form` input may be an **object with
declared `fields`**. The response's keys fill the fields, each coerced to its field type; a
missing required field is refused at start the way a missing required input is; the input is
stamped `form-response` once; the held-response payload moves to a version that holds JSON
values. Reading needs nothing new: `{{ input.intake.allergies }}`, a loop over an array
field, a typed `when` on `input.intake.on_letrozole`.

*(B6 #1005, 2026-10-14: the keys are **nested** — the response carries the object under the
input's id and its members are the fields; the sender groups, so the response's shape is the
input's shape and one fill serves the held response and #991's file. Any declared field
shape fills from a JSON value; a string value still goes through the v13 table.)*

Declared fields are chosen over a schemaless `type: json`: coercion, required-field refusal
and typed conditions need the shape, and an editor helper derives `fields` from a sample
response. The **file door** on the Create page (#991) and the **transcript door** (§ 15)
are siblings; their origin kinds ride this version's provenance bump.

*(#991, 2026-10-17 — the file door and the editor helper, as built. **The file is a bare
object** whose keys are the input's declared field ids — the input's value as a file. Nothing
wraps it on the way in: the held payload nests under the input id only because one flow
response fills several inputs; a raw Microsoft Forms "Get response details" export (question-id
keys) is not importable without the flow's mapping; an EMR extension that knows the declared
fields emits the bare shape by construction. **The door is offered on an object input declared
`expectedContent: form`** (beside the "From a form" chip). The bytes ride the request as
`InputFormFiles[id]` BESIDE the coerced value in `Inputs` — the `InputFormRefs` posture — so the
server re-coerces them by the one coercion (`FormResponseCoercion.CoerceDeclared`), requires
equality with the submitted value, and stamps the `form-response` kind's file-sourced variant:
`fileSha256` over the bytes as received, `textSha256` over the object as it entered the
effective map, no source ids. The fill is editable; an edit drops the bytes and the input is
typed. The cap is the held-value cap (256 KiB). **The client's coercion is the library's**
(`DeclaredInputCoercion` over a `WorkflowInputSpec` built from the served declaration), which
also closed the C5 gap: a held response's object value, served as its raw JSON, fills an object
input. **The editor helper** derives the fields an owner lacks from a pasted sample object —
optional, typed from the sample (`yyyy-MM-dd` → date; an array of objects in the v9 form),
never an enum, existing fields never retyped; a key that is not snake_case, a null, an empty
array or object is refused by name and derives nothing (no "form definition" format exists to
read). Rolled out server first: engine release v2026.10.6 carried the Api half before the
editor's door merged, since an older server drops the request member silently.)*

## 10. Refused by name at ≥ 25

Each below 25 keeps its frozen rules; each at ≥ 25 is a sentence naming the key and the
replacement — the form is "`<key>` is retired at specVersion 25; <replacement>." (A4, #999) —
and a schema-catchable conformance vector where the key leaves the schema.

| Refused | Replaced by |
|---|---|
| `nodes[].bindings`, `prompts[].variables` | object references (§ 1, § 2) |
| `prompts[].raw` | escaping (§ 1) |
| `manifest.preludes`, `prompts[].prelude` | already migrated by #963 |
| `manifest.data` | `manifest.snippets` (§ 4) |
| `manifest.schemas`, `schemaRefs`, `customSchemas`, `nodes[].output` | `manifest.formats` + `nodes[].format` (§ 3) |
| `nodes[].kind`, `values`, `op`, `of`, `in`, `failWith` | the format and `with` (§ 3.6) |
| `nodes[].macros[].use`, `.at: slot`, `.instruction` | the member read and the library field (§ 5) |
| `results`, `result` | `deliverables` (§ 11) |
| `deliverables[].macros[].before/after: node:x`, `forItem` | a node instance (§ 5.1) |
| `nodes[].id` with a hyphen | the snake_case id (§ 3.6) |
| `prompts[].id` with a hyphen | the snake_case id (§ 16) |
| `forEach: snippet:<folder>` / `input:<id>` / `data:<id>` | `snippet.<folder>` / `input.<id>` (§ 16) |
| `aggregate: ["node:<id>"]`, `deliverables[].node: node:<id>` | `node.<id>` (§ 16) |
| a `when` in the v10 grammar (`node:x == yes`, `kind == new`) | a Scriban boolean expression (§ 16) |

## 11. `results` becomes `deliverables`

The manifest's list of deliverables has been `results[]` since v7 (with a single `result` as
one-entry sugar). At 25 the key is **`deliverables[]`**; the validator's sentences that begin
"Result '…'" begin "Deliverable '…'" at ≥ 25; the v5–v24 sentences and vectors do not move.
The editor's migration rewrites the key (and folds a single `result` into a one-entry list).

**Out of scope, deliberately:** the provenance record's field names — `assembledDocuments`,
`skippedDocuments`, `failedDocuments`, `resultId` — are the contract outside verifiers read
across every record ever written, and stay; the provenance document may say "the deliverables
(`assembledDocuments`)" in prose. Internal names (`WorkflowResultSpec`, the editor's
`ResultView`, `resultsEdit`) are renamed as hygiene, not contract.

## 12. The record and the registries

- **provenance** (bump): per node `format`, `formatHash`, `catalogVersion`, `producer`,
  `agentName`/`agentVersion`, `validation`, `agentOutputHash`, `appended[]`; `hashVersion` 6
  with its definition in hash-definitions.md § 4; the derived edges as a documented derived
  field (replacing the bindings field); the slot removal/exclusion rows; the file-sourced
  form-response and transcript-file origin kinds; the custom-tier fields re-documented
  under "custom shape, engine-validated" (the `unattested` key is written `false` or
  dropped — decided at the bump, and the reader told).
- **package-format** (bump to 25): the object grammar and closed members (§ 1), derived
  edges (§ 2), the **format file definition** and producer vocabulary (§ 3.1–3.2), the node
  shape (§ 3.6), snippets (§ 4), the macro library and instance shape (§ 5), the mechanics
  sentence (§ 5.3), the object form input (§ 9), every refusal of § 10;
  schema generated from the model; conformance vectors for every refusal (cross-field ones
  `SHAPE_BLIND`).
- **agents** (a catalogue version): `agents/formats.json` with its two sections, the five
  format files, the `custom-json` agent yaml, the widened CI checks and stranding check
  (§ 3.3); the engine's catalogue pin setting renamed to match the registry name.

## 13. The editor

- **Node pane.** Format picker (catalogue + the package's custom files) with the entry's
  parameters rendered as fields and `onInvalid` when validatable; one **Macros** section —
  declared **Before / After** rows (macro, applies-when, fan-item scope, reorder within a
  placement) and a derived, read-only **Inline macros** list; a derived, read-only **Reads**
  list replacing Inputs and Variables, each row linking to the thing. The desk flips from
  "every variable is bound" to "every reference resolves".
- **Prompt, snippet and macro panes.** The text editor becomes a **code editor** with
  Scriban highlighting and reference completion — never WYSIWYG, since a markdown round-trip
  changes bytes the hash covers; until it lands, plain text with manual escaping and no
  plain-text toggle. "+ variable" becomes an **insert reference** palette inserting
  `{{ input.x }}`, `{{ node.x.content | concept_context }}`, `{{ snippet.x.content }}`,
  `{{ macro.x.content }}`, `{{ macro.x.prompt }}` at the cursor. Each pane lists what the text reads and, for a
  snippet or macro, where it is read.
- **Macro pane.** Title / id / file / **instruction** → text → **Instances** (node and
  deliverable rows) and **Read inline at** (derived).
- **Snippets pane** (was Data): `+ Snippet`, `+ Snippet folder`; a folder item's `when`.
- **Formats pane** (new): the package's custom format files (schema editor), and the
  catalogue entries read-only with their detail line (§ 3.8).
- **Deliverables pane** (#985): prepend / append with applies-when, the one-line note for
  node-placed instances, the two-way picker exclusion (#983).
- **Inputs pane:** the object-form input's fields as today's object fields.

*(C1 #1007, 2026-10-14 — the node pane's picker and the Formats pane are built, gated on
`EffectiveSpecVersion >= 25` and inert until A5 moves the editor's version constants — moved
2026-10-16 (#1018): `NewestSpecVersion` and `RunnableSpecVersion` are 25, the panes are live.
Decisions: an existing node's format **may be re-picked**, and a re-pick **resets `with` and
`onInvalid`** — they belong to the old format; the pane re-renders the new entry's fields
and the desk names what is then missing. `onInvalid` offers all three words, `retry`
labelled as reserved ("runs as fail until its rung"), with no desk refusal since the format
accepts it. A new package format is born as the smallest custom shape — type object,
producer custom, an empty object schema and a blank description the desk refuses until
written (explicit init). The "+ Node" form picks a format or aggregator in the kind
selector's place; a computed format mints no prompt. With no catalogue loaded the picker
offers the package's own formats, keeps a node's catalogue pick by id, and the desk takes a
non-local id on trust, as the validator does without a catalogue. The Formats pane absorbs
#934 (editor custom-schema authoring), closed as superseded: `customSchemas` migrates to
`formats/<id>.json` with `producer: custom` (§ 14), so the ≤24 flow is never built.)*

*(C2 #1008, 2026-10-14 — the Snippets pane is built: at 25 the Data group's machinery runs
under the `snippets` table, keyed on the version at every difference (the manifest key, the
`snippets/` root, a snippet born as `snippets/<id>.md`, a folder as `snippets/<id>/` with the
default `id, name, content` index fields, the fan spelled `snippet:<folder>`, the fan item's
`id, name, text`), so below 25 the Data group is byte-identical. Ids are snake_case in the
registry's own sentence; a folder's directory takes the underscore, so the Api's path policy
widened its `snippets/<folder>/` segment (data's never did). A folder item's **`when`** is the
existing condition builder restricted to inputs — no `node:` operand, no `item:id` — written
into `index.json` beside `id/name/file` only when set; the desk says the registry's snippet
sentences word for word (an id not snake_case; a `when` that does not parse, or reads a node,
an item or arithmetic; a `forEach` fanning a file, an undeclared folder, a retired `data:`
collection or neither spelling). Where each snippet is read comes from **the engine's own
walk** run in the editor (`EditorPackageWalk`: the composed manifest, the assembled files and
the served catalogue bound back into the library's `WorkflowFormatCatalog`, then
`WorkflowDerivedEdges.PrepareNodes` + `WalkAll`): a snippet's pane lists its readers — a
prompt, another snippet, a folder item, a macro — and a folder's item panes the templates
iterating it and the nodes fanning it, each row opening that pane; Remove is refused while
read. C4's Reads list and "every reference resolves" desk reuse the same walk. Without the
catalogue the list says what it cannot see (a prompt on a catalogue format is not walked).)*

*(C3 #1009, 2026-10-14 — the macro pane, the node pane's Macros section and the Deliverables
pane's prepend/append are built, gated on 25. **Macro pane:** the `instruction` (mandatory;
a new macro is born with it blank and the desk refuses it until written — explicit init);
**Instances** in Attached-to's place — a row per node instance (before/after this node's
output, fan item, applies-when, remove) and per deliverable instance (prepend/append, gated
when, detach) on the panes' own handlers, attach pickers for a node whose format takes output
macros with a free identity and for a deliverable not yet listing it; **Read inline at** from
the engine's walk (`macro.<id>.prompt` as a slot, `macro.<id>.content`), each row a link.
**Node pane:** one Macros section — instances placed around the output, grouped before then
after with reorder within each placement, attach only where the format takes output macros,
and the **Inline slots** the walk derives from the prompt with the validator's warning for
folder items lacking the read. **Deliverables pane:** document scope only — prepend/append
with gated when; a row born from the picker rests **unplaced until chosen** (decision: the
registry refuses an at-less instance, so the editor never defaults it; a node instance is born
`before`, the format's own default); the one-line note naming the node-placed instances the
document composes, each node a link (#983, absorbed); the table composed under `deliverables`
at 25. **Desk:** the registry's macro sentences word for word, with a classification-format
node counting as a classifier (its `with.values`) for every condition builder — the node-level
grammar's `node:` operand. Decisions: the four ≤24 macro suites stay untouched as the parity
proof and a 25 suite covers the new panes (the rework the sub-issue named waits for A5/C6);
removing a library macro now strips its node instances at every version, as the deliverables'
references already left (a dangling `nodes[].macros` id was a latent refusal). Found: the
registry doc's `when` examples spell `input:<id>`, but the condition grammar takes a bare
input id (`kind == new`) — the fixture and the builder use the bare form; the doc is to be
corrected.)*

*(#1071, 2026-10-18, Milestone 31 — the macro pane shows **what the model is told**: under the instruction, `{{ macro.<id>.prompt }}` rendered through the library's own `TemplateRenderer` over `TemplatePreviewScope` — every run value a token naming its read (`⟨input.notes⟩`, `⟨node.extract.content⟩`, `⟨profile.name⟩`), the package's snippets and macro texts rendered for real — so the pane shows the engine's bytes: the instruction rendered, then the mechanics sentence; the renderer's own sentence when it does not render; a note when nothing reads the macro inline; the #1067 self-read warning word for word, live from the walk. The editor's first template render — the text previews stay Markdown.)*

*(#1072, 2026-10-18, Milestone 31 — the macro pane's **Where it goes**, in the snippet pane's idiom: *Placed on* as chips that open the owner (`after 'Assessment'`, `prepend to 'Consultation note'`; the node pane and the Deliverables pane edit the instances), the attach pickers at the row's end; *Read inline by* and *Read as text by* from the walk; one "Not used yet" line only when every row is empty — "placed only" and "inline only" are normal states. The Instances rows' duplicate controls and "Not placed anywhere" / "Not read by any template" are gone.)*

*(#1073, 2026-10-18, Milestone 31 — the macro pane's layout, the snippet pane's shape: Remove macro in the header beside Discard (its reach the tooltip and a sentence under Where it goes), the instruction a full-width field on its own row, each palette named for its target ("+ reference into the instruction" / "into the macro text"), the text's help one sentence right above the editor, and "How macros work" folded at the pane's foot through a new `Footer` slot on `WorkflowEditorPane`. With #1071 and #1072 this closes the macro-pane walk.)*

*(#1077, 2026-10-18, Milestone 31 — the macro pane's **Placed on** is rows again, reversing #1072's chips for placements only: the attach pickers first, right under the heading (a fixed element that does not jump as instances come and go), then one editable row per instance mirroring the owner's row on the node pane's and the Deliverables pane's own handlers — placement (`before the node's output` / `after the node's output`, `prepend` / `append`), fan item, remove or detach, the `Applies when` / `Gated when` builder — each led by a link to the owner showing its label as the sidebar names it. A macro placed twice on one node (before and after, or per fan item) is two rows. Reordering among a node's siblings stays on the node pane; a deliverable row's link opens the Deliverables pane as a whole (no per-deliverable focus yet). The "Remove macro (in the header) removes its instances on …" sentence is gone — the rows are the reach; the header button's tooltip still names it. *Read inline by* and *Read as text by* stay chips, as the snippet pane's *Read by* does: a read is not edited from the macro's side.)*

*(#1079, 2026-10-18, Milestone 31 — the Deliverables pane walk's first issue, the pane's **facts at 25**: the check picker offers the nodes of a computed format (`kind: check` is retired) as `node.<id>`, shown by their label, with an authored value no node matches offered as itself; the pane's refusals and the desk's duplicate say *Deliverable* (the registry's noun) at 25 and *Result* below; the resting *Produced when* help names both gating sources — an input or an enum-format node (the operand picker offered enum-format nodes already). And the edit state made honest: a condition write of what is already there is no edit — folding a blank gate back to "Always produced" used to materialise the deliverables' edit and light Undo, Publish, Discard and the sidebar dot with nothing changed; the pane's Discard, its Revert and the dot now follow the deliverables' own drafts (a deliverable's when, its macro gates), since the drafts dictionary is shared with the node pane's and the items'. Next: #1080 the (id, at) identity, #1081 layout and focus.)*

*(#1080, 2026-10-18, Milestone 31 — the editor learns the registry's **(id, at) identity** for a deliverable instance: the Deliverables pane's placement, remove, move and gate handlers address an entry by its index, as the node side's do (the id-keyed calls stay as thin wrappers for the ≤24 surfaces, where the id is the identity); a gate's key is the index and its subject carries the placement when the macro holds two entries, so the builder's labels stay distinct; the append picker offers a macro while one of its two placements is free, naming the one left ("closing — append"), and the second entry is born at it (a first is born unplaced, as before; an unplaced entry blocks a second of the same id, which would be "placed twice ()"); the macro pane's deliverable rows are one per entry, its attach picker offering a deliverable while a placement is free. Two loaded identities used to show as two rows that edited, removed and gated each other. Next: #1081 layout and focus.)*

*(#1044, 2026-10-16 — the editor's half of the reply-as-template change: the **instruction
field** carries the insert-reference palette (the palette generalised over a target — a file's
text or an instruction — offering a snippet's choices, never the macro's own members), its
help says the model writes `{{ macro.<id>.content }}` where it is read inline and the reply
renders it; the macro-text prose says placed text instructs no model; the fan-item warning and
the blank-instruction sentence in the registry's new words; the desk at 25 says the walker's
instruction sentences by construction (#1039).)*

*(C4 #1010, 2026-10-14 — the derived **Reads** list, the **insert-reference palette** and the
desk's "every reference resolves" are built, gated on 25. **Node pane:** the Inputs (bindings)
section gives way to Reads — what the node's prompt references, from the engine's walk (inputs,
nodes by `.content`/`.value`, snippets, folders, the fan item's members, macros by `.content`/`.prompt`,
run and profile facts), a computed node's parameter reads, each row a link to the thing; the
walker's warnings word for word and an early word when a reference does not resolve; an
aggregator keeps its ordered sources. **Text panes** (prompt, snippet, folder item, macro): the
palette offers the objects a template may read — grouped, an object format's fields from its
schema, arrays and folders as loops, the fan item only where the reading node fans, never a
slot read from a macro's own text — with a renderer pipe for a concept_list node's text (#1058:
the only format a renderer renders), and **appends** the
reference on its own line (decision: no cursor helper; insertion at the cursor is the code
editor rung's); the Variables form, the plain-text toggle and the slot hint are ≤24; a reads
line and the warnings sit under the palette. **Compose:** a new prompt is its id and file (no
`variables`, no `raw`); a re-pointed node writes no `bindings`. **Desk:** the retired keys in
the validator's sentences, then every walked template's own sentences once each (the walker
names the line that does not parse). Decisions: palette on all three text panes; warnings shown
non-blocking. Found and fixed: the walker refused the renderer pipes (`{{ node.x.content |
concept_context }}`) as an unknown root — the closed pipe set is now accepted by the walk and
judged when it renders; and the editor's reader-pane lookup went by prompt id where the walk
keys a prompt by the node reading it.)*

*(C5 #1011, 2026-10-15 — the object form input on the Inputs pane. Found: the editor had never
refused `expectedContent: form` on an object — its content-channel select offered every channel
of the version to every type and the desk mirrored none of the validator's channel sentences
("the server validates channel/type fit"). So the lift at 25 was already true by omission;
C5 adds the **rule**, at every version (decision): the select offers only the channels the
input's type and version take — a union none, text and an array of text every channel of the
version, a scalar `form`, an object `form` from 25, an array of anything else none — a loaded
value the list no longer offers still shown as itself; a retype drops a channel the new type
cannot be filled from; and the desk says the validator's channel sentences word for word (the
version gate, a union, an unknown channel, transcript / image / ambient-note on a non-text input,
form on an object below 25 or on an array of objects, form on an object with no fields at 25).
The object's fields are today's object fields. Deferred (decision): the run page's client-side
form-response coercion lacks the nested-object case B6 gave the server, so a held response
cannot yet fill an object input in the browser — a follow-up under #991. *(Closed under #991,
2026-10-17: the client coerces through the library; the hand mirror is gone — § 9 note.)*)*

**#1039 (C, 2026-10-15) — the desk at 25 is the registry's validator.** Every earlier version's
desk is a hand-written mirror of the validator's sentences; at 25 the editor runs
`WorkflowPackageValidator.Validate` itself, client-side, over the composed manifest, the
assembled files and the catalogue the API served (`Templates.RegistryDesk25`), so the desk says
exactly what the publish would say, by construction, and the 25 mirrors (node/format, snippet,
macro, template, clause) retired. The editor adds only what composes nothing — the builder's
unfinished drafts, a blank `failIfEmpty` the compose drops, a removed format a node still names.
With no catalogue in hand the desk cannot resolve a catalogue format, so it defers to the
publish and says so as a warning rather than trusting the ids silently. ≤24 keeps its mirrors
untouched. See § 16's C note for the builder and the last colon spellings.

## 14. Migration 24 → 25 (the editor's upgrade; mechanical, byte-identical in what the model sees)

| At 24 | At 25 |
|---|---|
| `{{ <variable> }}` bound to `input:x` / `node:x` (`as: r`) / `data:x` / `item:name` | `{{ input.x }}` / `{{ node.x.content \| r }}` / `{{ snippet.x.content }}` / `{{ item.name }}`; `bindings` and `variables` dropped |
| `item:content` / `{{ item:content }}` | `item.content` |
| no contract | `format: prose` |
| `kind: classifier` + `values` | `format: classification`, `with: { values }` |
| `kind: template` | `format: rendered` |
| `kind: check` + `op/of/in/failWith` | `format: terms-subset`, `with: { of: node.x, in: node.y, failWith }`; the deliverable's `check` rewritten to `node.x` |
| `schemaRefs: { s: concept-list }` + `output.schema: s` | `format: concept-list` |
| `customSchemas` file | `formats/<id>.json` with the schema under `schema`, `producer: custom`; `onInvalid: fail` as the migration's explicit choice, named in the notice |
| `data` scalar / collection | a snippet / a snippet folder, files moved under `snippets/` |
| a prompt-side node macro (bare / `use: prompt`) | an inline `{{ macro.x.content }}` read at the top (`before`) or bottom (`after`) of the template, wrapped in `{{ if }}` when it had a `when`; a macro never placed on any output becomes a **snippet** (it has no instruction to give) and the read becomes `snippet.x.content` |
| an output-side node macro `at: slot` + `instruction` | the instruction moves to the library macro; the template keeps `{{ macro.x.prompt }}` |
| a deliverable macro `before/after: node:x` (± `forItem`) | a node instance `{ id, at, forItem }` — the node's `outputHash` now covers it, and the record says so |
| `raw: true` prompt | the text with `{{` escaped |
| `results` / `result` | `deliverables` |
| a node id with a hyphen (`identify-problem`) | `identify_problem`, in the id and in every reference (`aggregate`, `check`, `when`, `with`, templates) |
| a prompt id with a hyphen (`draft-section`) | `draft_section`, in the id and every node's `prompt` (#1037) |
| `when: node:scope == in_scope and kind == new` (every site: a node's own, an instance's, a deliverable's, a deliverable instance's) | `node.scope.value == "in_scope" && input.kind == "new"` — the Scriban expression, literals typed by the input (a text or enum input compares as a string); a `when` the v10 grammar refuses is a refusal naming the site (#1037) |
| `forEach: data:x` / `input:x`; `aggregate: ["node:x"]`; `results[].node: node:x` | `snippet.x` / `input.x`; `["node.x"]`; `node.x` (#1037) |
| `schemaRefs: { s: concept-list }`, `kind: check` | `format: concept_list`, `format: terms_subset` — the snake_case catalogue ids (#1037; the contract ids a ≤24 package names stay hyphenated) |

A library macro placed on an output somewhere *and* used prompt-side elsewhere stays one
macro: the prompt-side use is a `.content` read. Nothing is split.

*(C6 #1012, 2026-10-15 — the migration is built, as a pure library rewriter
(`WorkflowPackageMigration24To25`: manifest and files in, manifest and files at 25 out, one
notice per rewrite, refusals that apply nothing) the editor applies the import door's way:
the migrated package replaces the session's content, is held in this browser with its notice,
publishes as a new version of the same package and is discarded by reloading the registry's
version. The toolbar still offers only the ceilings (A5 moves them); the seam takes 25 and the
tests drive it. *(A5 #1018, 2026-10-16: moved — the toolbar offers one button, "Upgrade to
specVersion 25", to any package below 25. Below 24 it chains: the 24 rung is staged exactly as
before — the prelude→node-macro migration when crossing 23 — the effective package is composed
and the rewriter runs over it; one publish, one notice naming both steps. A refusal from either
step is said in the toolbar and leaves the package as loaded, the staged rung reverted.
`MigratableSpecVersion` is gone; the seam was the door. Two rewriter gaps closed with it: a
hyphenated **prompt** id is renamed, on every node that names it (every pre-24 fixture carried
one and migrated into a package the validator refused); the string-form `result` of a v5/v6
package becomes the one-entry `deliverables` instead of the "migrate to 24 first" refusal.)*
*(2026-10-17 — the same two steps offline: `scripts/migrate-workflow-package.cs` in the engine repo
(`dotnet run -v q --file scripts/migrate-workflow-package.cs -- <package-dir> [--write]`) stages a
folder to 24 the editor's way — a port of `MigratePreludes` + the additive bump, with a hyphenated
prelude id snake_cased since the rewriter renames node, prompt, data and schema ids but not macro
ids — then runs the library rewriter with the editor's contract resolver, validates the 25 result
with the formats catalogue, prints the notices and the file plan, and with `--write` applies files,
`manifest.json` and a regenerated `dag.mmd`. The hand edits 25 asks for — a placed macro's
`instruction`, a custom format's description — are printed as the validator's errors and made on
the written files. The public packages repository migrates with it. `validate-workflow-package.cs`
and `stamp-workflow-package.cs` now load the formats catalogue too; before this every 25 package
failed them with "the format catalogue is not available".)*
Decisions: library rewriter + replaced package; internal seam; the four
canonical concept-source stamps ride their snake_case twins so a renamed canonical node renders
the same concept-context bytes. Byte identity, handled: a concept node bound with no renderer
rendered as bullets at 24 and reads `node.x.content | concept_bullets` at 25 (a bare `.content` would
be the JSON); a moved data file has its literal `{{` escaped (a snippet renders as a template);
a prompt-side macro's `when` becomes a Scriban `if` (`ConditionScriban`). Explicit init the
notice names: every remaining library macro needs an instruction (a macro read prompt-side only
becomes a snippet and needs none); a custom shape's format takes its schema's own
`description`/`title`, blank otherwise. Refused, nothing applied: a prompt bound differently by
two nodes (the deferred alias map), a rename that collides, a prelude still present, a template
that does not parse. Found and fixed on the way: a snippet or macro text was walked with no
fan, so any `item.*` read in it was refused — it is now recorded and refused at the reading site
(a node instance, what a prompt reaches, a slot's fill, a deliverable instance) when that node
does not fan, restoring v24's `{{item:field}}` in a macro composed into a fan node.)*

## 15. Phasing

The v23/v24 shape, one layer per PR, each green on its own; layer A will likely split into
grammar and validation PRs.

- **A — format** (`Consultologist.PackageFormat`, the package-format registry): the object
  grammar, closed members and the AST walk; derived edges from templates and `with`; the
  format file and producer vocabulary; the node shape; snippets and folders; the macro
  library and instances; the reference rule and cycle check; the object form
  slot; the mechanics sentence; every § 10 refusal; `AcceptedSpecVersions` += 25.
- **B — engine + provenance + catalogue**: objects at render with closed members; the
  catalogue index with two sections, the format files and the `custom-json` agent (agents
  repo); dispatch on producer, the custom trailer and validation, `onInvalid`; folder size
  at start; output composition with the two hashes and `appended`; node-only slot fill;
  `NodeHashVersion` 6; the held-response payload v2; the catalogue read endpoint; the
  provenance bump; `SupportedSpecVersions` += 25; an engine release carries it to prod.
- **C — editor**: the panes of § 13 and the migration of § 14.
- **Siblings:** #985 (Deliverables rename, first); #983 (narrowed to the Deliverables pane);
  #991 (the questionnaire file door); a **transcript door** on the Create page — a text
  transcript file (VTT, SRT, plain text) read as turns (`Speaker: text`, consecutive cues of
  one speaker merged, timings and cue markup removed; plain text untouched), the file hash on
  a `transcript` origin that already exists as an input medium since v13; audio with
  speech-to-text is **v26**, by name. *(2026-10-17: landed — #994; the parser reads VTT and
  SRT to turns under `vtt/1`/`srt/1`, the door is the transcript-channel input's own dropzone
  sentence, and provenance v2026.10.6 words the hashed text — speakers kept, since who said
  what is clinical content; timings dropped, since they fragment sentences and survive in the
  file `fileSha256` identifies.)* **Folded in:** #969, #984. **Made structural:**
  #941, #942.

**The bump is the last sub-issue (2026-10-13).** A4 (#999) wires the validator at ≥ 25 behind the
unchanged accepted set; A5 (#1018) flips `AcceptedSpecVersions`, `SupportedSpecVersions` and the
editor's two mirrors together, lists v25 in the registry and exports its schema and vectors —
after B7 (the engine runs 25) and C6 (the editor migrates 24 → 25), because, until C6, the editor's
upgrade button only rewrote the number, the format catalogue does not exist before B1/B2, and the
registry's `supported` list mirrors the engine's run set. *(2026-10-16: done — § 17.)* *(2026-10-15: and after the `ns:id`
retirement — #1037 format, #1038 engine, #1039 editor (§ 16) — a format change that must land
before the bump freezes 25.)*

## 16. No `ns:id` at 25 — `when` on the object syntax, every id snake_case (#1037, 2026-10-15)

After C6 four manifest values still spelled a reference in the ≤24 binding grammar — `when`
(the v10 grammar), `forEach` (`snippet:x`, `input:x`), `aggregate[]` (`node:x`) and a
deliverable's `node` (`node:x`) — while `check`, `with` and every template already read
objects; prompt ids had no rule, and the catalogue's format ids were hyphenated by design.
**Decision:** at 25 no value spells `ns:id` and no declared id carries a hyphen. A format
change, so it lands ahead of A5.

**The reference keys.** `forEach: snippet.<folder>` | `input.<id>`; `aggregate: ["node.<id>"]`
(each source declared, never the aggregator itself — the first aggregate rule at 25);
`deliverables[].node: node.<id>`. One parser (`TemplateObjectReference`) reads them as it
reads `with` and `check`; the colon spellings are refused by name at 25 with the spelling in
the sentence, and the readers that serve both versions (fans, closure, diagram, the engine's
aggregator and deliverable readers) strip either.

**`when` is a Scriban boolean expression** over the objects a template reads —
`node.scope.value == "in_scope"`, `input.kind == "new"`, `item.id == "hpi"`,
`(input.docs | array.size) > 0`, joined by `&&`, `||`, `!`, with the comparisons, the arithmetic
operators and `date.parse "YYYY-MM-DD"`. It is parsed as one bare expression (a template, two
statements or a blank are refused) and **walked by the template walker in condition mode**:
every chain is recorded as a prompt's would be, so **a condition's reads are edges** — the
node's own `when` and its instances' whens are the node's edges (a `when` that reads the node
it gates is a cycle), a deliverable's and its instances' are the deliverable's — and the
comparisons are **typed** in the ≤24 clause rules re-spelled: an enum's value (`node.x.value`
of an enum format, an enum input) against its answer set, `item.id` against the folder's
items, `==`/`!=` only on those closed sets, ordering only on numbers and dates, truth only on
booleans (a bare text, enum, array or object is refused with what to write instead),
arithmetic on numbers. A `when` may read anything a template reads (a node's text, a snippet,
a fact); a folder item's reads inputs only, as before. The strict probe evaluates each
condition and refuses one that yields no boolean. A node's own `when` is validated at last
(it was not at 25 before). The v10 grammar is refused by name with the 25 spelling in the
sentence — `ConditionScriban.Spell`, C6's migration helper, writes it — and its inverse
(`TryUnspell`) reads the subset the editor's builder authors back into the ≤24 tree, so
`WorkflowResultConditions.TryParseExpression` serves both spellings: the ≤24 evaluator, the
diagram and the builder read either, and layer B (#1038) decides whether the engine evaluates
the wider grammar through the renderer's scope.

**Every declared id is snake_case**, the prompt's too (refused by name with the rename, as
the node's), and the catalogue's format ids and computed op join the rule — `concept_list`,
`terms_subset`, op `terms_subset` — so one regex and one rename rule cover the format; the
agent name `custom-json` and the ≤24 output-contract ids are not ids a template reads and stay.
The hyphenated catalogue ids formats@v2026.10.1 published were read until the renamed catalogue
(formats@v2026.10.2) was pinned; the fixture flip rode that re-pin, in its own PR, which also
dropped the hyphen read.

**The editor** (its half of #1039, folded in so every suite stays green): the compose writes
the object spellings at 25, every comparison against a loaded value strips either, the
aggregate picker spells per version, and the condition builder keeps its tree and writes the
object spelling at 25 (the literals typed by the input). Decisions (2026-10-15): the two
format ids and the op renamed, the agent kept; `input:x` fans move too; typed validation with
the ≤24 sentences; a `when` reads anything a template reads; strict in A with the editor's
compose half folded in rather than a transitional warning.

*(#1038, 2026-10-15 — layer B, the engine judges a 25 `when` through the renderer's scope.
**Where.** A node's own `when`, a deliverable's and a placement's are judged at the start when
every gating `when` reads inputs and run facts only, else at the boundary; a node instance's at
the node's completion over the activity's scope (the item in it); a folder item's at the start,
inputs only. **The boundary widened** (decision): at 25 a job *decides* when a gating `when`
reads a node — its value or its text — or a profile fact; stage one runs the closure of every
node the gating whens read (`WorkflowDerivedEdges.GatingReads`, carried as the orchestration
input's `DecidingNodes`), and the boundary's `DecideActivity` takes every scalar stage-one output
and the facts as the render scope's other halves. A classifier nobody's `when` reads is just a
node. **The judge** is `WorkflowConditionAt25.Holds` over `TemplateRenderer`'s own context, so a
`when` sees exactly what a template sees — **absence is the template's** (decision): an
unsupplied optional text or enum is `""`, a boolean, number or date null, an array empty and
falsy; `input.kind != "new"` holds when kind is absent, where the ≤24 evaluator's three-valued
rule said not held. The two evaluators agree on every supplied `when`. **The reason** (decision;
provenance@v2026.10.2) is one fixed form at 25 — *needs `<the when as written>`; it does not
hold*, a cascade *needs node '<id>', which needs `<when>`; it does not hold* — package text,
never a value, since a 25 `when` may read a node's text or a snippet. A 25 `when` rides as text
on the resolved result (`WorkflowResolvedResult.When`); the ≤24 tree is no longer built for it
(a `when` beyond the builder's subset used to be dropped at load, so the deliverable always
fired). A deliverable's `when` reads join its roots, so the prune keeps what only a condition
reads. ≤24 keeps `WorkflowResultCondition` byte for byte.)*

*(#1039, 2026-10-15 — layer C, what the author sees and what the desk says. **The desk** at 25
is the registry's validator run client-side (§ 13, the #1039 note): the ≤24 condition grammar,
a `node:` aggregate source or deliverable node, an `input:`/`snippet:` fan and a hyphenated
node or prompt id are refused at the desk in the registry's own words, and a `when` that reads
a node's text (#1038's widened boundary) is accepted there. The add-time id guards refuse a
hyphen at 25 with the registry's rename. **The builder** (decision): its operand options read
in the 25 spelling — `input.kind`, `node.scope.value`, `item.id`, `(input.docs | array.size)` —
while their values stay the tree's ≤24 keys, the model underneath; the compose spells on write
as #1037 left it. **The edit-as-text door**: a hand-written 25 `when` the tree cannot hold is
shown verbatim beside a text input that writes what the author types, verbatim, for the
registry to judge at the desk — never rewritten, still clearable; below 25 the verbatim branch
is unchanged. **Two bugs found and fixed**: the forEach picker wrote `input:<id>` for an array
fan at 25, which the registry refuses (now `input.<id>`); the Consults rail stripped `data:`
alone, so a 25 fan's items fell back to the package blocks (now
`WorkflowFanSources.CollectionIdOf`). The ≤24 input-fan helper, the ≤24 bindings editor and the
`SnippetColonPrefix` read stay as the ≤24 grammar's.)*

*(#1044, 2026-10-16 — the reply is a template; the instruction is a template. **Decisions**
(AskUserQuestion): the reply renders over the node's **full scope** (the scope its prompt
rendered over, signature read included as the node's fact); a reply that does not render
**fails the node non-retryably** (the existing `InvalidOperationException` policy — the
prompt or the agent is at fault); filed and built ahead of A5 #1018 as the last format change
before the bump. **Built:** the mechanics sentence names the read the model writes;
`TemplateRenderer` renders the instruction before appending the sentence and carries a
`TemplateRenderTrace` (the prompt's `.prompt` reads; the top-level `.content` reads in order — a
macro's `.content` is produced on every read so twice read is twice placed);
`WorkflowNodeCompletion.Compose` takes the rendered reply and its inline reads (no marker fill,
no removals); `TemplateKind.Instruction` is walked per macro, visited from a `.prompt` read
(edges, `PromptReadsOf`, item reads), an edge in the reference graph (an instruction reaching
its own inline read is a cycle named by its path), probed; `ConsultNodeCompletion.Complete` renders
the reply (a classification renders nothing; plain text renders to itself and keeps the early
return). **Registry:** package-format-v25.md § The reply is a template (spec PR #29);
provenance@v2026.10.3 amends definition 6 in place — never written by a live engine — and the
`appended[]`/`excluded[]` wording (provenance PR #37). ≤24 keeps `[[slot:<id>]]`,
`ConsultSlotFiller`, the v20 warnings and the editor's slot UI.)*

*(#1047, 2026-10-16 — the word. **Decisions** (AskUserQuestion): a macro at 25 is **placed** (an
instance), **read** (`macro.<id>.content`) or **inline** (`macro.<id>.prompt`); *inline* is reserved for
the `.prompt` form — the `.content` section of the macro pane is "Read at", the node pane's list is
"Inline macros", the reads and palette say `(inline)`; the 25 node-row record kind is **`inline`**
(provenance@v2026.10.4, renamed in place — never stamped live; `slot` stays the ≤24 document kind);
the 25-side identifiers follow (`RetiredAt25.Inline`, `InlineKind`, `ConsultAppendedKinds.Inline`,
`node-inline-macros`, test names). "Slot" names only the ≤24 key `slot: true` and the `[[slot:<id>]]`
marker. Landed before A5 so the exported v25 conformance vectors pin the final sentences — `…where the
inline macro goes`, `reads macro.x.prompt (an inline macro), but …`, `…where it is read inline
(macro.<id>.prompt).` The Create page's input "slots" are #1046's.)*

*(#1049, 2026-10-16 — the member. **Decision:** the member that holds a thing's rendered output is
**`.content`** on all four objects — `node.<id>.content` (beside `.value`), `snippet.<id>.content` and a
folder item's, `item.content` (a snippet-folder fan — the ≤24 `item:content` under the object spelling),
`macro.<id>.content` (beside `.prompt`) — where 25 had said `.text`. One word: `content` is already the
folder index's field and the ≤24 item member, and a node's JSON output beside `.value` reads straighter as
`.content`. The `text` FORMAT type, the `text` content channel and the record field
`assembledDocuments[].text` are untouched. The C# identifiers that denote the member follow
(`TemplateMembers.Content`, `ReadsContent`, `MacroContents`, `ReadMacroContent`, `ReadNode(content:)`,
`ContentReads`); plain text properties (`TemplateNodeOutput.Text`, `TemplateMacroSource.Text`, the Api's
`MacroTexts` source maps) keep their names. The mechanics sentence names `{{ macro.<id>.content }}`.
Registry: spec PR #31; provenance@v2026.10.5 (doc-only). Landed before A5 so the exported conformance
vectors pin the final spelling. The dated notes above keep `.text` as the history they record.)*

## 17. The bump (#1018 A5, 2026-10-16)

The last sub-issue landed as planned: `AcceptedSpecVersions` and `SupportedSpecVersions` += 25
together, the editor's two mirrors to 25 (§ 13, § 14 notes), the registry listing v25 at
package-format **v2026.10.5** with its generated schema and vectors (registry PR #32). Four things
beyond the number, found in planning and done:

- **The generated v25 schema spelled the ≤24 forms** the validator refuses (`^(snippet|input):.+$`,
  `^node:.+$`, a hyphen in the format id, no snake_case on prompt ids) and still required the
  prompt's retired `variables`; published as it was it would have rejected every valid 25
  manifest. Every fix is gated ≥ 25; v5–v24 stay byte-identical.
- **The vectors need a format catalogue** (prose, classification, concept_list, rendered,
  terms_subset are catalogue formats). The suite ships `conformance/format-catalog.json` — the
  test fixture catalogue, the `catalog-schemas.json` precedent — which the export writes and
  the replay and the registry's publish script and CI read.
- **"Minimal is the previous version plus a line" cannot hold at 25**: the v24 minimal uses
  `output`, `bindings`, `variables`, `preludes`, `data`, `results`, all refused. The baseline is
  the v24 minimal — its prelude already a node macro, as the 23 editor leaves it — through
  `WorkflowPackageMigration24To25` (`v25-minimal-is-the-migrated-v24-minimal`), beside the
  design's minimal and a rich case. 172 vectors at 25 (167 refusals, one per sentence), ten
  below-25 gates under `v24/`, 120 `SHAPE_BLIND` entries with their reasons.
- **The editor's one button chains** (§ 14 note) — and two rewriter gaps closed with it
  (prompt-id rename; the string-form `result`).

Also with the bump: `reproducible` is refused on a rendered or computed format (the ≤24
template/check twins had no 25 counterpart); the render probe no longer throws on a duplicate
macro id (the export found it); the registry's `invalid-unknown-input-type` v9 case is
`…-at-v9` so ids are unique suite-wide. **Follow-up, not A5:** the migrated minimal is valid
with warnings — the rewrite spells a concept-list read as `node.x.content | concept_bullets`
and the walker warns on every `.content` read of a shaped format regardless of the pipe;
suppressing the warning when the read goes through a renderer pipe (C4 blesses it) is a
validator refinement. Prod runs 25 with engine release v2026.10.5. *(2026-10-17: done — #1058
typed the pipes rather than suppressing the warning: `TemplatePipes.Specs` names the format
each renderer renders, a node's `.content`/`.value` reads carry their node to the chain's last
word, and a renderer pipe's operand is judged there — a concept_list node's text passes with no
warning, anything else the walk can type is refused by name (before this, `node.prose.content |
concept_bullets` published cleanly and failed inside a live consult), anything it cannot is
judged when it renders. The migrated minimal is warning-free; package-format v2026.10.7 carries
the re-exported vectors and three new refusals; the editor's palette offers the renderer select
only on a concept_list node's read and its pane counts "problems in this text" rather than
"references that do not resolve". The grammar tidy-up — a pipe takes the node,
`{{ node.x | concept_context }}` — is #1059, for v26.)*

*(2026-10-17, #1067: the unused-macro rule counts only what a run renders — `Walk.MacrosReached`: every node's prompt and the folder items it fans, every instance's text, every condition, closed over snippets, macro texts and inline instructions and fills. Before, `used` was built from every walked template, so an unplaced macro whose instruction read its own `.content` was "used" by itself and the refusal never fired (`acct-…/test/general3@v2026.10.3` published with `test` unplaced). An instruction that reads its own `.content` is now warned: the instruction renders where `.prompt` is read, so that read puts the macro's text in the prompt — the mechanics sentence names the read. package-format v2026.10.8 carries two new refusals and one valid-with-warning vector.)*

*(2026-10-18, #1065: a template may read an aggregator's composed text — `TemplateNodeShape.Aggregator` joins both node maps (the validator's and `PrepareNodes`', so the starter and the editor see the same edge); `node.<agg>.content` is the composed sections before deliverable macros and the signature, `.value` and a renderer pipe are refused by name, the render probe probes it as text; a 25 aggregator may not declare `forEach` (the ≤24 rule, missed at 25); a node, instance or deliverable `when` may not read an aggregator — the deciding stage runs the whens' closure before the sections compose. The engine needed nothing: an aggregator completes into the same outputs map the pass it composes. package-format v2026.10.9 carries one valid and four invalid vectors; the two public packages blocked on this (`example-two-documents`, `example-v12-constructs`) migrate next.)*

*(#1046, 2026-10-17 — an input is not a "slot". The word named two things: the macro the model places
(the ≤24 `[[slot:<id>]]` marker, `slot: true`, the record kind, `WorkflowSlotMarker`, `ConsultSlotFiller`, the
v20 sentences, the editor's slot-instruction UI — all unchanged) and an *input* on the Create page (`ScalarSlot`,
"text/form/transcript slot", the editor's Inputs-pane alias, `slotOrigins`, the email-intake regex group,
~128 live-doc lines, ~280 test names). The input meaning is "input" everywhere now, § 9 above included. Four
validator sentences carried it and are pinned by the conformance vectors; they were reworded with a registry
bump — package-format **v2026.10.6**, the v13/v16/v17 vectors re-exported, the v16/v17 doc lines that quote them
and the v25 "form input" line following; the other frozen per-version docs keep their era's prose. The
form-response 422 misfits say "the input is …". `MacroChoiceSlot`, the per-run optional-macro checkbox and
neither meaning, is `MacroChoice`. No release is needed; the engine's vendored registry version rides the next.)*

## Deferred, by name

- **Enum value labels** — `{ value, label }` entries, `input.<id>.label`, the Create page
  showing the label — **v26** (§ 8).
- A per-node alias map for one prompt with different upstream nodes (§ 2).
- **Macro folders** (`macro.<folder>`): the walker reads them, but the manifest has no shape
  for a folder of macros at 25 (`macros[]` is a flat library); a shape waits for a package
  that needs one (A4, #999, 2026-10-13).
- A schemaless `type: json` input (§ 9).
- Snippet conditions on anything but inputs — dynamic fan sizes during a run; the v26
  scheduler redesign (§ 4).
- Audio transcripts with speech-to-text (§ 15).
- The `retry` policy's repair message (§ 3.5); strict content-addressed agents as a separate
  producer class if the custom tier's error rate ever justifies the operator cost (§ 3.4).
- `extends`: a package-local custom format that copies a catalogue schema (§ 3.1).
- A `boolean` catalogue format giving `node.x.value` as a Scriban bool; `classification`
  with two values covers it.
