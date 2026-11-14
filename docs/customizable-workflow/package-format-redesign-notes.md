# The package format, redesigned: working notes (Milestone 39)

> **Status:** working notes, not yet a design. They record the decisions made with the package
> author on 2026-11-13 and 2026-11-14 while reviewing the package's structure as a whole, and the
> questions still open. Each decision is filed as a Milestone 39 issue, which is where it is argued
> in full; these notes summarise them. The versioned design document (#1388) brings them together.
> Where it and these notes disagree, the design says which decision it supersedes.

**Why.** The format grew version by version from v5 to v30. Each version was a reasonable step, but
together they left:
- a node split between a manifest entry and a prompt file;
- a `format` field that mixes how a node runs with what it returns;
- headers no tool recognises;
- one flat list holding both the package's nodes and its structure;
- logic spread over Scriban strings that only our code understands.

The format is, in effect, a small, bounded, typed, recorded programming language for durable
clinical workflows. This milestone treats it as one: files and folders for its structure, a type
system for its values, a standard expression language for its conditions, and a resolved document
the engine runs.

## Issues

| Issue | Subject | Status |
|---|---|---|
| #1383 | nodes as files; flow nodes as folders; front matter | decided |
| #1384 | namespaces in disjoint scopes | decided; built after #1383 |
| #1385 | `kind`, and `format` as the return type; the JSON Schema dialect; data and text types | decided |
| #1386 | conditions as JSON Logic | leaning, with a variant |
| #1389 | `resolve.json`, the resolved, validated package document | decided direction |
| #1390 | `lock.json`, the toolchain lock | decided, with a lean on mismatches |
| #1391 | standard workflow languages reviewed | decided: keep our structure |
| #1392 | a loop's `parallel` and `maxParallel` | decided |
| #1393 | nested concurrency caps and a job ceiling | open |
| #1394 | every declaration member required; `null` means "deliberately none" | decided |
| #1395 | checks in `checks/`, read as `check.x` | decided |
| #1396 | checks per item, as loops in `checks/` | follow-up |
| #1397 | renderers as typed Scriban templates in `renderers/` | decided |
| #1398 | drawing renderers in the diagram | decided for now |
| #1399 | an interactive graph (later, no milestone) | later |
| #1400 | `matches`, regular-expression search | follow-up |
| #1388 | the design document | next |
| carried in | #1061 (enum labels), #1382 (nested headings), #1184 (macros as a map) | open |

## 1. Files and folders (#1383)

```
manifest.json      identity, inputs, deliverables, and id lists
nodes/             production: agents, rendered, computed, let, flow
  plan.md
  parts/           a flow node is a folder
    index.json
    outline.md
checks/            verification (#1395)
renderers/         presentation (#1397)
formats/           package-local formats
macros/  snippets/
dag.mmd            derived: the diagram
resolve.json       derived: the package as checked (#1389)
lock.json          derived: the toolchain that checked it (#1390)
```

- **A leaf is a file** (`nodes/<id>.md`): a JSON header, then a body. `prompts/` is retired.
- **A leaf's body holds the Scriban that produces it:** an agent's prompt (a template), a rendered
  node's template, a `let`'s expression (a bare expression, not a template). A computed node's body
  is empty.
- **A flow node is a folder** (`for`, `while`, `if`, `case`) described by its `index.json`; its
  members sit inside it, to any depth. `if`/`case` branch nodes sit flat in the folder, named per
  branch in `index.json` (`then`/`else`; a case's `branches[].when`).
- **Every folder has an `index.json`:** snippet folders, macro folders, flow nodes.
- **A loop's source is a plain reference:** `"in": "node.plan.value.sections"`, not
  `"for": "item in …"`.
- **The manifest keeps id lists** (`nodes`, `macros`, `snippets`, `formats`) in order. The file
  name is the id everywhere.
- **Ids stay unique** (until #1384), and `node.x` reads a node at any depth, never `node.folder.x`.
- **Hashes:** a node's `inputHash` is still the rendered prompt; the record's prompt path moves.

**Front matter is JSON between `---` lines.** JSON is valid YAML, so Markdig and GitHub recognise
it, and we parse it with our own JSON parser. Both lines are required: the first line of the file,
and a closing line. A file may end right after the closing `---`. A body that is only whitespace is
empty, and a body with content is kept byte for byte. Scriban's `+++` front matter (code that runs)
and full YAML were rejected.

## 2. Every member required (#1394)

- **In our declarations** (the manifest, node headers, every `index.json`, inputs, deliverables,
  macro/snippet/item headers, `resolve.json`, `lock.json`): **every member is present; `null` means
  "deliberately none"; a missing key is always an error.** Empty values (`""`, `[]`) are values.
- **Inside formats** (JSON Schema), every keyword, our `x-` annotations included, follows JSON
  Schema's convention: **present where it applies, omitted otherwise, never `null`.** The
  validator enforces presence where an annotation applies, and absence where it doesn't.
- **JSON Logic in `resolve.json` has no optional operands:** one fixed form per operator.
  Variadic operators keep their lists; `if` chains become nested three-operand `if`s; `null`
  replaces a missing operand only where it provably means the same (safe for `var` and `if`, not
  for `substr`).

## 3. Kind, format and types (#1385)

**Every node declares its `kind` (how it runs) and its `format` (what it returns).**

| Kind | Runs | Body |
|---|---|---|
| `agent` | a model call | its prompt |
| `rendered` | a template, no model | its template |
| `computed` | a built-in operation (`op`), with parameters (`with`) | empty |
| `let` | one expression, no model | its expression |
| `for`, `while`, `if`, `case` | flow; a folder | (none) |

**Data and text are separate types.**

| Format | Is | `contentMediaType` | `x-markdown` | `x-agent` |
|---|---|---|---|---|
| `string` | data | (omitted) | (omitted) | `false` |
| `number`, `boolean` | data | (omitted) | (omitted) | `false` |
| `plaintext` | text, computed | `text/plain` | (omitted) | `false` |
| `markdown_block` | text, computed | `text/markdown` | `block` | `false` |
| `markdown_inline` | text, computed | `text/markdown` | `inline` | `false` |
| `agent_plaintext` | text, written by an agent | `text/plain` | (omitted) | `true` |
| `agent_markdown_block` | text, written by an agent | `text/markdown` | `block` | `true` |
| `agent_markdown_inline` | text, written by an agent | `text/markdown` | `inline` | `true` |
| object formats | structured data | (omitted) | (omitted) | `true` or `false` |

- **`agent` returns only `agent_*` text or objects with `x-agent: true`;** every other kind returns
  formats with `x-agent: false`. A computed node's format is written and checked against its
  operation; operations are named apart from their formats (`terms_subset` returns
  `check_result`).
- **Flow formats:** a `while` returns its `ret`'s format; an `if`/`case` returns the union of its
  branches; a `for` returns a list of its `ret`'s format.
- **`reproducible`** is implied `true` for every kind but `agent`.
- **Dates** are `{ "type": "string", "format": "date" }`.
- **There is no bare `prose`:** today's `prose` becomes `agent_plaintext`.
- **What can't be enforced:** a model asked for plain text may still emit Markdown; the media type
  is the format's declared intent, and composers act on it.

**The notation is JSON Schema in the file, type strings for display.** Named formats are `$defs`
in a per-package bundle, referenced with `$ref`; a list is `{ "type": "array", "items": … }`, a union
`anyOf`. The Editor and the validator's sentences show `list<agent_plaintext>`,
`agent_plaintext | classification`. The dialect is a declared JSON Schema 2020-12 vocabulary:
today's strict subset plus `$ref`, `$defs`, `anyOf`, `contentMediaType`, `x-agent` and
`x-markdown`. Azure OpenAI's strict mode accepts `$ref`, `$defs` and `anyOf` (not `anyOf` at the
root; at most 100 properties and 5 levels), and our annotations are stripped before a schema is sent.
We keep our own validator; the json-everything libraries carry a maintenance-fee licence, and
Corvus (Apache-2.0) can't enforce a keyword allow-list.

**Inputs use the same types, and the type picks the Create page widget.**

| Input type | Widget |
|---|---|
| `string` (with `enum`, a select) | a single-line field |
| `number`, `boolean` | a number field, a checkbox |
| `string` with `format: date` | a date picker |
| `plaintext` | a text area |
| `markdown_block` | a rich-text editor, stored as Markdown |

**Migration:** today's text inputs become `markdown_block`; documents uploaded to a Markdown input are
converted to Markdown; enums become `string` plus `enum` (labels are data, #1061).

## 4. Conditions (#1386)

**Leaning: JSON Logic, checked against the dialect,** to revisit at #1386's design. Today's
conditions are Scriban strings checked by our walker; JSON Logic makes them data that any JSON
Logic library can run. **A variant:** authors keep writing expression strings, and the resolver
writes JSON Logic into `resolve.json`, giving one expression language for authors and a standard,
data-only runnable document. CEL, FHIRPath, JSONPath (RFC 9535) and CQL/ELM were researched and
compared (in #1386's comments); no JSON Logic checker exists, so the checker is ours, reading the
dialect directly.

**Typed operations.**

| On | Allowed | Refused |
|---|---|---|
| data (`string`, `number`, `boolean`) | `==`, `!=`, `in` (list membership), `<`, `>` | — |
| text | `in` (standard JSON Logic: an exact, case-sensitive search, with no semantic meaning), `missing`, `cat` (in a `let`) | `==`, `!=`, `<`, `>`, `substr`, `!`/`!!` as a blank test |

**Our custom operations,** each with a fixed arity and precise types: `size` (Unicode code points),
`blank`, `words`, `lines`, `lower` (for a case-insensitive search: `"x" in lower(text)`), `trim`,
`starts_with`, `ends_with`, `plain` (Markdown to plain text). `matches` (regular expressions) is
held for #1400.

## 5. Loops (#1392, #1393)

- **`parallel: true | false` is required on every `for`;** `until` is a condition or `null`, and a
  non-null `until` requires `parallel: false`. A `while` is sequential by definition.
- **`maxParallel` is a number or `null`;** with `parallel: false` it must be `null`; a cap of 1 with
  `parallel: true` is refused, pointing to `parallel: false`.
- **Nested caps multiply** (#1393): whether a whole-job or per-deployment concurrency ceiling is
  needed is open.

## 6. Checks (#1395, #1396)

Checks live in **`checks/`**, read as **`check.x`**; a deliverable's `check` can only name a check.
The folder gives the role, `kind` the mechanism (`computed` today), and `format` is `check_result`.
Nodes may read checks. Checks get their own section in the record and History. Top-level only for
now; loops inside `checks/`, aligned item for item with a production loop, are #1396.

## 7. Renderers (#1397, #1398, #1399)

- **Renderers become Scriban templates** in **`renderers/`**: `kind: rendered`, pure (they read only
  their piped value), returning a computed text format. Their `input` may be any format.
- **Called by a pipe with a qualified root:** `{{ node.extract.value | renderer.concept_bullets }}`,
  type-checked at publish. **This settles Scriban over Liquid for bodies,** since Liquid filter names
  can't contain a dot. The "Consultologist Scriban" dialect is to be formalised as a published spec.
- **A pipe takes the value** (`.value`), which answers #1189.
- **The Editor's pane shows the real template.** Today's C# renderers are retired only once a test
  proves the templates byte-identical.
- **In the record and History:** a `renders` list on the consuming node's row, shown as expandable
  sub-rows below the node, with input and output (text while retained; hashes and sizes always).
- **In the diagram:** renderers drawn inside the consuming node's frame (a Mermaid subgraph with ELK
  layout), with a toggle falling back to compact labels (#1398). An interactive Cytoscape.js graph
  from `resolve.json` is a later improvement (#1399).

## 8. The resolved document and the lock (#1389, #1390)

- **`resolve.json`:** the package as checked, produced at publish, in canonical JSON (RFC 8785).
  Nodes are flattened into one id-keyed map, with `in`, `nodes` and `ret` explicit. It embeds all
  package and catalogue content (prompts, macros, snippets, formats) and declares the engine
  capabilities it needs in `requires`. **The engine is to load only this document;** the source is
  the edit surface and the means to verify it.
- **`lock.json`:** the exact toolchain that resolved and validated it (Scriban, the evaluator,
  renderers, operations, the dialect, the catalogues). Written at publish only; a library change is
  a package upgrade. **Leaning:** lock exactly, refuse outside a compatible range declared in
  `requires`, with any output change a major version.

## 9. Workflow languages reviewed (#1391)

CNCF Serverless Workflow, Microsoft Agent Framework, Elsa, Workflow Core, Amazon States Language,
Logic Apps, Dapr and Temporal were compared. **None replaces our structure:** each brings its own
runtime, and none offers our record, dataflow scheduling, typing or bounded `while`. **Keep our
structure and engine;** export `resolve.json` to Serverless Workflow for a standard view; watch
Microsoft Agent Framework's durable declarative workflows.

## Still to decide

- The spec version(s), and the one-version plan for #1383, #1385 and #1394 (with #1386 in or out).
- #1386's choice between JSON Logic in the source and the expression-string variant.
- #1390's mismatch rule, and its seven open questions.
- #1393's concurrency ceiling.
- #1061's labels in the dialect (`const` and `title`), and labels on classification answers.
- #1382's nested headings.
- The validator's sentences throughout.
