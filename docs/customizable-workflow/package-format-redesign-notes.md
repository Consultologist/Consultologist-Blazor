# The package format, redesigned: working notes (Milestone 39)

> **Status:** working notes, not yet a design. They summarise the decisions made with the package
> author from 2026-11-13 to 2026-11-15. Each decision is argued in full on its issue; the
> **"Current state"** block at the top of each issue lists what its earlier text no longer says.
> The versioned design document (#1388) will bring these together and **replace these notes** as the
> consolidated source.
>
> **Conditions and `let` expressions are JSON Logic, written directly.** Some examples below show a
> condition as a short expression (`input.age < 18`) for readability. In a package file it's the
> JSON Logic it stands for, which the converter (#1423) produces.

**Why.** The format grew version by version from v5 to v30. Each step was reasonable, but together
they left:
- a node split between a manifest entry and a prompt file;
- a `format` that mixes how a node runs with what it returns;
- logic spread over Scriban strings only our code understands;
- decisions made inside prompts, invisible to the record.

The format is, in effect, a small, bounded, typed, recorded language for clinical documents. This
milestone treats it as one: files and folders for structure, a type system for values, JSON Logic
for decisions and computation, Scriban only for presentation, and a compiled document the engine
runs.

**The plan** (#1388): **one spec version, all at once, with every Milestone 39 issue.** Milestone 40
holds follow-ups once the structure is tested; Milestone 41 holds the Editor.

## 1. Files and folders (#1383, #1384)

```
manifest.json      identity, inputs, and id lists (nodes, macros, snippets)
nodes/             production: agent, rendered, computed, let, flow folders, call
checks/            verification (#1395, #1396), read as check.x
renderers/         presentation templates (#1397)
deliverables/      composition templates, in delivery folders (#1406)
functions/         named, reusable sub-workflows (#1419)
formats/           index.json + one file per format (#1385)
macros/  snippets/
dag.mmd            derived view (committed for example packages)
workflow.sw.json   derived view: Serverless Workflow export (#1422)
README.md          optional documentation, in any folder
```
`resolve.json` and `lock.json` are **evidence,** written only at publish into the published
version's folder, never into any source (#1389, #1390).

- **A leaf is a file** (`<id>.md`): a JSON header between `---` lines, then a body. A **flow node is a
  folder** described by its `index.json`. **The file name is the id.**
- **Every folder has an `index.json`** with a **required `description`** (an array of lines, or
  `null`; at most 500 characters, the manifest's included). An optional **`README.md`** is allowed in
  any folder: documentation only, published, outside `resolve.json` and its hashes.
- **Namespaces:** ids may repeat only in disjoint scopes, never shadowing. `node.x` resolves outward.
  Record keys are **qualified with `.`** everywhere (`parts.paragraphs.draft:2/1`), in the same spec
  version. The Editor tells same-id nodes apart by their paths.

## 2. Every member required, and the format's bounds (#1394, #1392, #1419)

- **Every member of every declaration is present; `null` means "deliberately none"; a missing key is
  an error.** Inside formats (JSON Schema), annotations follow JSON Schema's convention: present where
  they apply, omitted otherwise.
- **`FormatBounds`,** constants of the format (never written in packages), each 15 (`0xF`) and
  raisable one at a time:

  | Member | Limits |
  |---|---|
  | `passes` | a `while`'s `max`: 1 to 15 |
  | `depth` | nested function calls: 0 to 15 |
  | `parallel` | a loop's `maxParallel`: 2 to 15, or `null` (no cap of its own) |

## 3. Kind, format and types (#1385, #1061)

**Every node declares `kind` (how it runs) and `format` (what it returns).** Kinds: `agent`,
`rendered`, `computed`, `let`, `for`, `while`, `if`, `case`, `call`.

| Format | Is | `x-agent` |
|---|---|---|
| `string`, `number`, `boolean` (dates: `string` + `format: date`) | data | `false` |
| `plaintext`, `markdown_block`, `markdown_inline` | computed text | `false` |
| `agent_plaintext`, `agent_markdown_block`, `agent_markdown_inline` | text written by a model | `true` |
| object formats, `check_result`, `agent_boolean` (`{ answer, reason }`) | structured data | either |

- Text formats carry `contentMediaType`, and Markdown ones `x-markdown: block | inline`. **`prose`
  becomes `agent_plaintext`; `x-generated` becomes `x-agent`.**
- **`description` is for authors; `x-instruction` is for the model** (it replaces `description` in the
  schema sent to a provider).
- **Object schemas are fully typed;** a nested field is data unless it `$ref`s a text format.
- **One way to write "nullable"** (a type array for scalars, `anyOf` with `null` for `$ref`s), **a
  `$id` on every format,** and **enum labels as `oneOf` of `const` with `title`** (#1061).
- **Formats are authored per owner as `formats/index.json`** (a `$defs` of `$ref`s) plus one file per
  format: the registry's built-ins, the formats catalogue, each package. **Agent bindings live apart,
  in `agents/bindings.json`.** `resolve.json` holds one inline bundle.
- **Inputs use the same types,** and the type picks the Create page widget. Text inputs become
  `markdown_block`.

## 4. Languages: decide and compute in JSON Logic, present in Scriban (#1386)

| Purpose | Language |
|---|---|
| decide (every header `if`, `when`, `while`, `until`, `case` branch) | JSON Logic |
| compute (`let`) | JSON Logic, plus the custom `object` operation (lists are native) |
| present (prompt bodies, renderers, deliverables, macro bodies) | Scriban |
| describe types | JSON Schema 2020-12 dialect |

- **Authors write JSON Logic directly.** The converter (#1423) turns expressions into JSON Logic and
  back, and the migration uses the same logic.
- **Decisions live in headers; bodies only present.** A body `if` that decides inclusion is refused.
- **Typed operations:** data supports `==`, `!=`, `in`, `<`, `>`. Text supports `in` (an exact,
  case-sensitive search), `missing`, and `cat` (in a `let`); `==`, `<`, `substr` and `!`/`!!` as a
  blank test are refused on text.
- **Custom operations** (fixed arity, versioned, #1390): `size`, `blank`, `words`, `lines`, `lower`,
  `trim`, `starts_with`, `ends_with`, `plain`, `object`, `matches` (I-Regexp, literal patterns,
  non-backtracking, #1400), `has_word`, `all_pass`, `count_pass`, `terms_subset`.

## 5. Prompt text: snippets, parts and macros (#1409, #1412)

**One contract for conditional or reusable prompt text:** text, an `if`, and a placement.

| | Snippet | Part | Macro |
|---|---|---|---|
| Where | a shared file | a node's header (`parts`) | a shared file |
| Text | the file body | `text: [lines]` | `instruction: [lines]` (prompt side), body (output side) |
| Placed by | `{{ snippet.x }}` | `{{ part.x }}` | `{{ macro.x }}` |

**Placements carry the whitespace:**
- `{{~ x }}` alone on its line (a block; when absent, an empty line remains);
- `{{ x }}` anywhere inside a line.

**The text is the author's,** and the Editor previews each placement with and without it. The checker
refuses only forms that delete written whitespace: `~}}`, `-`, `~` inside a line, and text after a
block placement.

**Macros separate whether from where:**
- **Whether:** the macro's header `if` (JSON Logic), usually reading a **classifier node**
  (`agent_boolean`), recorded on its own.
- **Where:** the writing model places the marker.
- The engine adds **only triggered macros** to the prompt.
- It checks the reply **exactly:** each triggered marker `{{~ macro.<id>.content }}` or
  `{{ macro.<id>.content }}` once, in the form asked; no untriggered or unknown markers.
- It replaces markers **literally;** the reply is never rendered.

Macros are inline only, on agent nodes only, with a required `format`. Placements (`append`,
`before`, `after`) and deliverable macros are removed.

## 6. Loops and concurrency (#1392, #1393)

- **`parallel: true | false` is required.** `until` is a condition or `null`, and needs
  `parallel: false`. `maxParallel` is within `FormatBounds.parallel`.
- **`label`** (required, nullable) **names items** in the rail, History and the record. It replaces
  `labelField`. Snippet headers are typed data, so a snippet loop names its items with
  `"label": "title"` like any other (#1407).
- **A loop is read as `.value`** (a list of its `ret`'s format) **or `.items`** (each output paired
  with its source **by identity**, with `state` and `reason`).
- **Concurrency:**
  - a **per-job ceiling** is an engine setting, counting loops and calls alike;
  - deployment limits come from host configuration, sized to the model quota;
  - 429s are retried and recorded;
  - publish notes when a package's caps multiply past the ceiling.

## 7. Functions (#1419)

`functions/<id>/index.json` declares `inputs`, `uses`, `nodes`, `ret` and `format`. A `call` node
runs it with `with`.
- **Sealed:** a function reads only `param.*` and its own nodes. Everything else comes through `with`.
- **Declared `uses`** for package snippets, macros, formats and renderers. Built-in and catalogue
  formats are free.
- **May contain** every kind and nested calls (depth within `FormatBounds.depth`). **Not:**
  recursion, deliverables or checks.
- **One `ret`,** always valued (a node that always runs, or an exhaustive `if`/`case`), **one
  format.** Several values are an object built by a closing `let`.
- **Its ids are local;** record keys go through the call node (`hpi_section.standard_draft`).
- A macro's `if` is evaluated **where the macro is placed.** Pattern A puts a classifier inside the
  function; pattern B passes a decision in.
- **Imports from other packages** are #1420 (Milestone 40).

## 8. Checks (#1395, #1396)

- Checks live in `checks/`, read as `check.x`, in their own namespace and History section.
- **Check loops iterate a production loop's `.items`.**
- **An aggregate check** (`all_pass`, `count_pass`) gates a deliverable. Templates decide how a
  failing section appears. There's no partly-released state.
- A deliverable's check is its **final proofread:** if it fails, the deliverable isn't sent. A
  deliverable can't read its own check's value.

## 9. Renderers and the diagram (#1397, #1398)

- **Renderers are Scriban templates** in `renderers/` (`kind: rendered`, pure), called by the pipe
  `| renderer.x` on `.value`, returning computed text formats.
- Uses are recorded as `renders`, and shown as sub-rows in History.
- The C# renderers stay as the byte-equivalence reference, and are removed in Milestone 40 (#1404).
- **The diagram:** Mermaid with ELK, renderers inside the consumer's frame, with a toggle. An
  interactive Cytoscape.js graph is #1399 (Milestone 41).

## 10. Deliverables (#1406)

`deliverables/<id>.md` is a `rendered` leaf whose body is the **composition template.**
- **`sources` is gone:** the template's reads are the sources.
- **Headings are written by the author.** A loop's sections are built by a closing `let`:
  `titled_section` `{ heading, content }`.
- **The header:** `label`, `kind`, `format`, `if`, `check`, `whenEmpty` (`release`, `hold` or
  `fail`).
- **Reads:** `node.*`, `input.*`, `snippet.*`, `profile.*`, `check.*` (not its own), and `run.date`,
  `run.job`, `run.package`. **Not** other deliverables. Nodes can't read deliverables either.
- **States are closed** (`completed`, `skipped`, `not_run`). A branch on `state` needs an `else`.
  `reason` is never patient text.
- **Strict formats:** reads must match the deliverable's format. Plain text and data are escaped into
  Markdown; every other conversion happens in a node.
- **Delivery is declared on folders only** (`index.json`): `delivery` (`email`, `pdf`, `emr`, `zip`)
  and `bundle`, with no inheritance, and `[]` meaning not delivered. A kind the account hasn't set up
  is produced and recorded as not sent.
- **Provenance:** the template by reference, then sites and substitutions (with `pass` lists),
  replaying to the composed hash.

## 11. The profile (#1408)

- **Fields:** `profile.name`, `credentials` and `organisation` (`string`); `signature` and `closing`
  (`plaintext`).
- The signature is the account's chosen block, choosable per job, and recorded by its id.
- **Readable only in deliverables and macro bodies,** never in prompts or conditions.
- A missing field the package reads **stops the job before it starts.**
- Personal snippets stay Create-page insertions. Delivery endpoints are a separate account section.

## 12. The compiled documents (#1389, #1390)

- **`resolve.json`:** the package compiled. Canonical JSON (RFC 8785), resolved, type-checked,
  lowered and linked.
  - The engine loads only this.
  - **`requires`** lists what the engine must support (spec, evaluator, Scriban, operations used,
    kinds, features, agents). **`needs.profile`** lists account fields.
- **`lock.json`:** the exact toolchain that checked it.
  - Caret ranges in `requires`, set by the registry. The engine refuses only outside them.
  - Any output change in a built-in is a major version, with golden tests.
  - A corpus is re-rendered before any library upgrade.
- **Records carry `resolveHash` and `lockHash`,** and the exact versions that ran.
- **Evidence is publish-only.** It's visible as GitHub Release assets for example packages, and
  through the Editor's download and History for account packages. Uploaded copies are ignored.
- **No backfill:** old spec versions keep today's loading path until retired.

## 13. Workflow languages and the export (#1391, #1422)

- No standard replaces our structure. #1391 is in Milestone 40 for a later re-review.
- `resolve.json` is **exported to Serverless Workflow DSL 1.0.x** as `workflow.sw.json`, a view:
  - conditions are translated to jq, with the original JSON Logic in metadata;
  - constructs with no equivalent are carried as metadata;
  - an invalid export never blocks a publish.

## Issues

| Milestone | Issues |
|---|---|
| **39: the format** | #1383, #1384, #1385, #1386, #1388, #1389, #1390, #1392, #1393, #1394, #1395, #1396, #1397, #1398, #1400, #1406, #1408, #1409, #1412, #1419, #1422, #1061, #1382 (closes through #1406) |
| **40: formats, hardened** | #1391, #1402, #1403, #1404, #1405, #1415, #1416, #1417, #1420 |
| **41: the Editor** | #1399, #1410, #1411, #1413, #1414, #1418, #1421, #1423 |
| **Closed** | #1407 (snippet naming, Option A), #1184 (superseded) |
