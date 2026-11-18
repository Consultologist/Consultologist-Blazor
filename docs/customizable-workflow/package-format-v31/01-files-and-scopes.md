# 01. Files and scopes

This part defines what a version 31 package is on disk: the folders and files, which file holds
what, the front matter, the rule that every member is written (`null` for none), ids and the
limits on them, namespaces and the keys the record uses, and the two sets of format constants,
`SourceBounds` and `FormatBounds`. It covers #1383 (files and folders), #1394 (every member
required), #1384 (namespaces and keys), #1388's source limits and portable ids
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6927247843))
and the `FormatBounds` part of #1392. Part 01's own open points were settled on
[#1383](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910).
What each kind of file's own members mean is defined in the part that owns the kind; this part
says where each file lives and which members every file shares.

## 1. The model in one paragraph

A package is a folder. **A leaf is a file** (`<id>.md`): a JSON header between `---` lines, then a
body. **A flow node is a folder** (`<id>/`) described by its `index.json`. **The file or folder
name is the id.** **All nine top-level folders are always present, each with its `index.json`,**
and every folder lists its members, in order, exactly once; a file nobody lists is refused. Every
member of every declaration is written; `null` means "deliberately none" and a missing key is an
error. Ids may repeat only where neither node can see the other, and **every key is written with
its root**, its path of ids joined by `.` (`node.sections.paragraphs.draft_paragraph`,
`check.part_coverage.coverage`, `deliverable.emr.note`)
([#1383](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910),
[#1384](https://github.com/Consultologist/Consultologist-Blazor/issues/1384#issuecomment-6904190578),
[#1394](https://github.com/Consultologist/Consultologist-Blazor/issues/1394)).

| At 30 | At 31 |
|---|---|
| a node is a `nodes` entry in `manifest.json` plus `prompts/<id>.md` | a node is `nodes/<id>.md`, or `nodes/<id>/` with `index.json` for a flow node |
| the manifest lists nodes, macros and snippets | each folder's `index.json` lists its members; the manifest keeps identity and inputs |
| a bare JSON object at the top of a macro, snippet or item file | front matter: one JSON object between two `---` lines |
| the manifest lists `formats` as `{ id, file }` | `formats/index.json`, a plain index (`description`, `items`) |
| deliverables, with `sources`, in the manifest | `deliverables/<id>.md` and delivery folders (part 06) |
| checks are nodes | `checks/`, read as `check.x` (part 06) |
| renderers are C# | `renderers/<id>.md` (part 05) |
| (none) | `functions/<id>/` (part 04), `tests/<id>.json` (part 07) |
| a folder exists only when it has members | all nine top-level folders always present, each with `index.json` |
| a stray file is a warning (v28) | a stray file is refused, dot-files included |
| optional members with implied defaults | every member written, `null` for none |
| node ids unique across the package | ids unique per visible scope; every key written with its root |
| `\r\n` kept as written | `\r\n` normalised to `\n` at import and publish; a lone `\r` refused |
| import caps 512 files / 256 KB / 2 MB; publish caps 256 KB / 2 MB | `SourceBounds`, checked the same at publish and import |
| `MaxPasses = 10` | `FormatBounds`: `passes`, `depth`, `parallel`, each 15 |

## 2. The folder layout

```
<package>/
  manifest.json            identity and inputs (§ 3)
  README.md                optional, documentation only (§ 2.3)
  dag.mmd                  derived view: committed for example packages, generated at publish (part 09)
  nodes/                   production
    index.json             the top level, in order
    <id>.md                a leaf: agent, rendered, computed or call
    <id>/                  a flow node: for, while, if or case
      index.json
      <child>.md | <child>/
  checks/                  verification, read as check.<id> (part 06)
    index.json
    <id>.md                a check (kind computed)
    <id>/index.json        a check loop
  functions/               named sub-flows (part 04)
    index.json
    <id>/
      index.json           inputs, uses, nodes, ret, format
      <child>.md | <child>/
      snippets/ macros/ formats/ renderers/     the function's own definitions, each with index.json
  renderers/               presentation templates (part 05)
    index.json
    <id>.md
  snippets/                prompt input text (part 05)
    index.json
    <id>.md
    <folder>/
      index.json
      <item>.md
  macros/                  verbatim output text (part 05)
    index.json
    <id>.md
    <folder>/
      index.json
      <item>.md
  formats/                 the package's own formats (part 02)
    index.json
    <id>.json
  deliverables/            composition templates, in delivery folders (part 06)
    index.json
    <id>.md
    <folder>/
      index.json
      <id>.md
  tests/                   offline tests (part 07)
    index.json
    <id>.json
```

- **All nine top-level folders are always present, each with its `index.json`,** even when empty
  (`"items": []`): `nodes/`, `checks/`, `functions/`, `renderers/`, `snippets/`, `macros/`,
  `formats/`, `deliverables/`, `tests/`. **Inside each function folder, `snippets/`, `macros/`,
  `formats/` and `renderers/` are always present too,** each with its `index.json`. An empty folder
  says, visibly, that the author defined nothing there
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)).
- **The id lists live in the folders:** 30's manifest `nodes`, `macros` and `snippets` lists move
  into `nodes/index.json`, `macros/index.json` and `snippets/index.json`. The manifest keeps
  identity and inputs ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)).

`resolve.json` and `lock.json` are **evidence**, written only at publish into the published
version's folder, never into any source. A copy found in an upload is ignored at import and at
publish
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6905180743)).
`workflow.sw.json` is not part of 31: the export moved to Milestone 40 with #1422.

### 2.1 Which file holds what

| File | Holds | Listed by | Members defined in |
|---|---|---|---|
| `manifest.json` | name, version, title, description, tags, inputs | — | § 3; inputs in [part 02](02-types-and-inputs.md#8-inputs) |
| `<folder>/index.json` (the nine top-level folders and a function's four) | `description` and `items`, the folder's members in order | — | § 5.2; `deliverables/index.json` adds delivery settings ([part 06](06-checks-deliverables-profile.md)) |
| `nodes/<id>.md` | an `agent`, `rendered`, `computed` or `call` node: header, then its prompt or template | `nodes/index.json`, or the enclosing flow node's `index.json` | `kind`, `format` in [part 02](02-types-and-inputs.md); `computed` in [part 03](03-expressions.md); `call` in [part 04](04-loops-and-functions.md); an agent's prompt members (`parts`, `onInvalid`) in [part 05](05-templates-and-macros.md) |
| `nodes/<id>/index.json` | a `for`, `while`, `if` or `case`: its members and its children | as above | [part 04](04-loops-and-functions.md) |
| `checks/<id>.md`, `checks/<id>/index.json` | checks, check loops, aggregate checks | `checks/index.json`, or the check loop's `index.json` | [part 06](06-checks-deliverables-profile.md) |
| `functions/<id>/index.json` and its children | a function's signature (`inputs`, `uses`, `ret`, `format`) and body | `functions/index.json` | [part 04](04-loops-and-functions.md) |
| `renderers/<id>.md` | a renderer: header (`input`, `format`), then its template | `renderers/index.json` | [part 05](05-templates-and-macros.md) |
| `snippets/<id>.md` | a snippet: header, then its text | `snippets/index.json` | [part 05](05-templates-and-macros.md) |
| `snippets/<folder>/index.json`, `<item>.md` | a typed folder: `items`, `format`; each item's header is its value, its body its content | `snippets/index.json`; the folder's `items` | § 5.3 here; reads in [part 05](05-templates-and-macros.md) |
| `macros/<id>.md` | a macro: header (`if`, `instruction`, `format`), then its verbatim text | `macros/index.json` | [part 05](05-templates-and-macros.md) |
| `macros/<folder>/index.json`, `<item>.md` | a macro folder: `items` and a shared `if` | `macros/index.json`; the folder's `items` | [part 05](05-templates-and-macros.md) |
| `formats/<id>.json` | one of the package's own formats, as JSON Schema | `formats/index.json` | [part 02](02-types-and-inputs.md#6-format-files-ids-and-references) |
| `deliverables/<id>.md`, `<folder>/index.json`, `<folder>/<id>.md` | delivery settings per folder; each deliverable's header and composition template | the folder's `index.json` | [part 06](06-checks-deliverables-profile.md) |
| `tests/<id>.json` | one offline test | `tests/index.json` | [part 07](07-compiled-documents.md) |
| `README.md` (any folder) | documentation | — | § 2.3 |
| `dag.mmd` (root only) | the derived Mermaid view | — | [part 09](09-engine-and-pages.md) |

### 2.2 Children: every member listed exactly once

- **A folder's children** are exactly: its `index.json`, the members its list names, and an
  optional `README.md`. A member is `<id>.md` (a leaf), `<id>.json` (in `formats/` and `tests/`),
  or `<id>/` (a folder). **Every child is listed exactly once.**
- **A stray file is refused,** with a sentence naming it
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6924329099)).
  So is a listed member with no file, and a member listed twice.
- **Dot-files are stray files and are refused** (`.DS_Store`, `.gitkeep`): one rule, no exceptions
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)).
- **One id, one child:** a folder may not hold both `<id>.md` and `<id>/`.
- **Lists name ids, never paths. Members are listed by bare id, with no trailing `/`:** v28's
  `"standards/"` becomes `"standards"`, since one id can't name both a file and a folder
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)).
- **Order:** a list's order is meaningful: `nodes/index.json`'s `items` is the top level's order,
  a flow node's `nodes` its body's order, a snippet folder's `items` the order a loop or a join
  sees.
- **Inside a function folder,** the names `snippets`, `macros`, `formats` and `renderers` hold the
  function's own definitions
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6888253558)).
  No node can take one of them, since they are reserved ids everywhere (§ 6).
- **Folder depth:** snippet and macro folders hold items only (one level, as at 30). **Delivery
  folders are one level deep,** directly under `deliverables/`
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)).
  Flow folders nest as deep as `FormatBounds` and `pathLength` allow (§ 7, § 8).

### 2.3 Descriptions and READMEs

- **One `description` rule:** an array of strings joined with `"\n"`, or `null` for none; at most
  500 Unicode code points joined (as JSON Logic's `size` counts, part 03); `[]` is refused. It
  applies on every `index.json`, every header, every input, every function input and the manifest
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)).
- **It is for people only:** the Editor, tooltips and the diagram show it, and it is **never sent
  to a model**
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6898087160)).
- **`formats/index.json` follows the same rule:** it is a plain index, not a JSON Schema document
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466);
  [part 02 § 6](02-types-and-inputs.md#6-format-files-ids-and-references)). The JSON Schema
  `description` keyword *inside* a format file keeps JSON Schema's convention (a string, or
  omitted).
- **`README.md` is allowed in any folder.** It is documentation only: published with the package,
  but outside `resolve.json`'s compiled content, and never read by the engine. It is covered by
  `sourceHash` (part 07). Packages are public, so a README follows the fictional-examples-only rule,
  which the checker cannot verify.

### 2.4 Encoding and line endings

- **Every file is UTF-8 with no byte-order mark**
  ([#1383](https://github.com/Consultologist/Consultologist-Blazor/issues/1383), decision 4).
- **`\r\n` is normalised to `\n` at import and at publish,** so a checkout's line-ending setting
  never changes a body's bytes or its hash. **A lone `\r` is refused.** The publish report notes
  every file it normalised (part 07). Normalising to Windows `\r\n` instead is #1465
  (Milestone 40)
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)).

## 3. The manifest

`manifest.json` is a plain JSON object. Every member is present. **It holds identity and inputs
only.**

| Member | Type | `null` allowed | Meaning |
|---|---|---|---|
| `name` | string | no | the package's name (`example-nested-note`, `test/general`), as at 30 |
| `version` | string | no | `vYYYY.MM.N`, as at 30 |
| `specVersion` | integer | no | `31` |
| `derivedFrom` | string | yes | the `name@version` this package was copied from; `null` if none |
| `title` | string | yes | the display title; `null` shows the ref, as at 30 |
| `description` | array of strings | yes | lines, at most 500 code points joined (§ 2.3) |
| `tags` | array of strings | no | `[]` when none, as since v9 |
| `inputs` | object | no | the inputs, keyed by id, in Create page order ([part 02 § 8](02-types-and-inputs.md#8-inputs)); `{}` when none |

**Gone from the manifest:**
- **`nodes`, `macros`, `snippets`:** `nodes/index.json`, `macros/index.json` and
  `snippets/index.json` list them (§ 2).
- **`formats`:** `formats/index.json` lists them
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6879096324)).
- **`deliverables`** (and v24's `results`): `deliverables/` (part 06).
- **`templating`:** `lock.json` and `resolve.json`'s `requires` record the Scriban version
  (part 07), so a hand-written copy could only disagree
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)).
- **`prompts`, `schemas`, `schemaRefs`, `customSchemas`, `preludes`, `data`, `result`:** members of
  earlier versions, already unused at 30.
- **`requires` and `needs`** are derived at publish into `resolve.json`, never written (part 07).

**The manifest of `example-nested-note` at 31** (the description shortened here):

```json
{
  "name": "example-nested-note",
  "version": "v2026.12.1",
  "specVersion": 31,
  "derivedFrom": null,
  "title": "Nested note demo",
  "description": [
    "A demonstration of nesting: a node plans the note's sections, a loop outlines each",
    "section and drafts one paragraph per point, and a while redrafts the opening summary",
    "until a judge calls it good. Not intended for clinical use."
  ],
  "tags": ["demo", "nested"],
  "inputs": {
    "consult_draft": {
      "label": "Consult draft",
      "description": null,
      "format": "markdown_block",
      "required": true,
      "channel": null
    }
  }
}
```

`nodes/index.json`:

```json
{
  "description": null,
  "items": ["plan", "section_count", "sections", "refine"]
}
```

The loop the package called `parts` at 30 is `sections` at 31, since `parts` is reserved (§ 6).
Its tree:

```
example-nested-note/
  manifest.json
  dag.mmd
  nodes/
    index.json              ["plan", "section_count", "sections", "refine"]
    plan.md                 agent, section_plan
    section_count.md        computed, number
    sections/               for, over the plan's sections
      index.json
      outline.md            agent, outline
      paragraphs/           for, over the outline's points
        index.json
        draft_paragraph.md  agent, plaintext
    refine/                 while, at most 3 passes
      index.json
      draft_summary.md      agent, plaintext
      judge.md              agent, verdict
  checks/
    index.json              "items": []
  functions/
    index.json              "items": []
  renderers/
    index.json              "items": []
  snippets/
    index.json              "items": []
  macros/
    index.json              "items": []
  formats/
    index.json              ["section_plan", "outline", "verdict"]
    section_plan.json  outline.json  verdict.json
  deliverables/
    index.json
    note.md                 deliverable.note
  tests/
    index.json
    migration_snapshot.json written by the migration (part 10)
```

`test/general` at 31:

```
test/general/
  manifest.json                  identity and inputs
  nodes/
    index.json                   ["extract_patient_concepts", "identify_problem",
                                  "create_typical_trajectory", "create_patient_trajectory",
                                  "prescribes_letrozole", "standards"]
    extract_patient_concepts.md  …  create_patient_trajectory.md
    prescribes_letrozole.md      agent, judgement: the macro's classifier (part 05)
    standards/                   for, over snippet.standards, itemLabel "title"
      index.json
      section_instructions.md  patient_section_draft.md  standard_section_draft.md
  checks/
    index.json                   "items": []
  functions/
    index.json                   "items": []
  snippets/
    index.json                   ["snomed_tool_guidance", "standards"]
    snomed_tool_guidance.md
    standards/
      index.json                 { "description": null, "items": ["hpi", "pmh", …], "format": "standard_section" }
      hpi.md  pmh.md  …  assessment_plan.md
  macros/
    index.json                   ["letrozole_prescription_text"]
    letrozole_prescription_text.md
  formats/
    index.json  standard_section.json
  renderers/                     package renderers only; the built-ins are the registry's (part 05)
    index.json                   "items": []
  deliverables/
    index.json  consult.md
  tests/
    index.json  migration_snapshot.json
```

(Which of its 30 constructs the migration rewrites into these files, the classifier node and the
`test` macro's move into the deliverable template among them, is
[part 10](10-migration-and-tools.md). Its `standard_section` field keeps its name `title`: field
names have no reserved list ([part 02 § 5.3](02-types-and-inputs.md#53-object-schemas-are-fully-typed-nested-text-references-a-text-format)).)

## 4. Front matter

Every `.md` file with a header (nodes, checks, renderers, snippets, folder items, macros,
deliverables) has the same shape
([#1383](https://github.com/Consultologist/Consultologist-Blazor/issues/1383), decision 4, and
[grammar points](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6924329099)):

```
---
{ "kind": "agent", "producer": "generated", "label": "Planning the sections", "description": null, "format": "section_plan", "modelDerived": true, "runDependent": false, … }
---
Plan the sections of a consultation note for the draft below. …
```

- **Line 1 is exactly `---`.** Then **one JSON object**, then a line of exactly `---`.
- **The body starts on the line after the closing `---`.** A blank line there is part of the body,
  and of the prompt's bytes. The Editor and the migration write none.
- **A file may end right after the closing `---`:** a header-only file.
- **A body that is only whitespace is empty.** A body with content is kept byte for byte, after
  line-ending normalisation (§ 2.4).
- **The header is strict JSON (RFC 8259), never YAML:** no duplicate keys, no comments, no trailing
  commas ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)).
  The `jsonc` in this document is for annotation only.
- **Header and body stay separate:** the header is data (and JSON Logic, part 03); the body is
  Scriban (part 05).

**Bodies, per file:**

| File | Body |
|---|---|
| `agent` node | required: its prompt |
| `rendered` node, renderer, deliverable | required: its template |
| `computed` node, `call` node, check (`computed`) | must be empty: the expression and arguments are header members |
| snippet, snippet folder item, macro, macro folder item | required: the text |

`index.json`, `formats/*.json`, `tests/*.json` and `manifest.json` are plain JSON with no front
matter, held to the same strict JSON.

## 5. Members: every one written

**Every member of every declaration is required; `null` means "deliberately none"; a missing key
is an error**
([#1394](https://github.com/Consultologist/Consultologist-Blazor/issues/1394)).

- **Empty values are values:** `""`, `[]` and `{}` are not "none".
- **Members that can never be "none"** are never `null`: an id, a `kind`, a `format`, a loop's
  `in`, `nodes` and `ret`, and a loop's or branch's `label`
  ([decided 2026-11-17](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6957670672)).
  The registry's schemas say which members are nullable.
- **It covers our own declarations:** the manifest, inputs, every header, every `index.json`,
  deliverables, `resolve.json` and `lock.json`.
- **It does not cover embedded standards:** JSON Schema keywords inside formats follow JSON
  Schema's convention (present where they apply, omitted otherwise, never `null`), and JSON Logic
  follows its own operator forms, canonicalised as [part 03](03-expressions.md) defines
  ([boundary](https://github.com/Consultologist/Consultologist-Blazor/issues/1394#issuecomment-6878756179)).
- **An unknown member is refused at publish,** naming the file and the closest valid member
  (*"Did you mean 'label'?"*). Every declaration is closed, as the formats'
  `additionalProperties: false` is. The Editor and import offer to rename or remove it, shown to
  the author and never done silently. The migration never writes one
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)).

### 5.1 The members every header shares

| Member | Type | `null` | On |
|---|---|---|---|
| `kind` | string, one of the kinds ([part 02 § 1](02-types-and-inputs.md#1-kind)) | no | every node and check header, every flow `index.json`, renderers (`rendered`), deliverables (`rendered`) |
| `label` | string, not empty or whitespace | no | every node, check, flow node, function, renderer, snippet, macro, deliverable and input. It is the display name everywhere |
| `description` | array of strings | yes | every `index.json`, every header, input and function input, and the manifest (§ 2.3) |
| `format` | a format name or an inline schema ([part 02 § 2](02-types-and-inputs.md#2-format)) | no | every node, check, function, renderer, deliverable, snippet, macro and input; a snippet folder's `index.json` |
| `reproducible` | boolean | no | every node, checked against the derived value ([part 02 § 1.1](02-types-and-inputs.md#11-reproducible)) |
| `producer` | string: `"generated"` or `"computed"` at 31 | no | every node and check, checked against its kind ([part 02 § 1.3](02-types-and-inputs.md#13-producer)) |
| `modelDerived` | boolean | no | every node and check, checked against the derived value ([part 02 § 1.2](02-types-and-inputs.md#12-the-contagious-flags-run-dependent-and-model-derived); [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1478#issuecomment-6982835861)) |
| `runDependent` | boolean | no | every node and check, checked against the derived value ([part 02 § 1.2](02-types-and-inputs.md#12-the-contagious-flags-run-dependent-and-model-derived); [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1478#issuecomment-6982835861)) |
| `if` | a JSON Logic condition ([part 03](03-expressions.md)) | yes: always | every production leaf (`agent`, `rendered`, `computed`, `call`), check, snippet, macro, macro folder and deliverable. A node's own condition is spelled `if`, replacing 30's `when` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6959885064)); a `case` branch keeps `when` for its values (part 04) |

- **`label` may not be empty or whitespace**
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)).
  A loop's item naming is `itemLabel`, never `label`
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6924320332));
  `labelField` is gone.
- **Flow nodes write `format`,** and publish refuses one that differs from the format derived from
  the node's `ret` ([part 02 § 2.2](02-types-and-inputs.md#22-which-formats-each-kind-may-return);
  [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)).
- **`reproducible` is written on every node and checked against the derived value;** a mismatch is
  refused. Revisiting the member is #1466 (Milestone 40)
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910);
  the rule is [part 02 § 1.1](02-types-and-inputs.md#11-reproducible)).
- **`producer` is written on every node and checked against its kind,** like `reproducible`:
  `"generated"` for `kind: "agent"`, `"computed"` for every other kind. A mismatch, or a value
  reserved for Milestone 40 (`"grounded"`, `"extracted"`, `"attention"`), is refused
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868); the rule is [part 02 § 1.3](02-types-and-inputs.md#13-producer)).
- **`modelDerived` and `runDependent` are written on every node and checked against the values the
  resolver derives,** like `reproducible` and `producer`: an agent node is always
  `modelDerived: true`, any node reading model output, directly or through other values, is
  `true`, and a node whose reads include `run.*` or a run-dependent value is
  `runDependent: true`. A mismatch is refused. The flags are node members; formats stay pure shape
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1478#issuecomment-6982835861); the rule is [part 02 § 1.2](02-types-and-inputs.md#12-the-contagious-flags-run-dependent-and-model-derived)).

### 5.2 The members by file

The table names each file's members; the owning part gives their types, null rules and sentences.

| File | Members | Part |
|---|---|---|
| `agent` node | `kind`, `producer`, `label`, `description`, `format`, `reproducible`, `modelDerived`, `runDependent`, `if`, and the prompt members (`onInvalid`, `parts`, `ordered`, `failIfEmpty`, …) | [05](05-templates-and-macros.md) |
| `rendered` node | `kind`, `producer`, `label`, `description`, `format`, `reproducible`, `modelDerived`, `runDependent`, `if` | [05](05-templates-and-macros.md) |
| `computed` node, check | `kind`, `producer`, `label`, `description`, `format`, `reproducible`, `modelDerived`, `runDependent`, `if`, `expression` | [03](03-expressions.md), [06](06-checks-deliverables-profile.md) |
| `call` node | `kind`, `producer`, `label`, `description`, `reproducible`, `modelDerived`, `runDependent`, `if`, `function`, `with`, `format` | [04](04-loops-and-functions.md) |
| `for` / `while` / `if` / `case` `index.json` | `kind`, `producer`, `label`, `description`, `format`, `reproducible`, `modelDerived`, `runDependent`, the flow members, `nodes`, `ret` | [04](04-loops-and-functions.md) |
| function `index.json` | `label`, `description`, `inputs`, `uses`, `nodes`, `ret`, `format` | [04](04-loops-and-functions.md) |
| renderer | `kind`, `label`, `description`, `input`, `format` | [05](05-templates-and-macros.md) |
| snippet | `label`, `description`, `if`, `format` (a text format) | [05](05-templates-and-macros.md) |
| snippet folder `index.json` | `description`, `items`, `format` | § 5.3 |
| snippet folder item | its value: exactly the folder's format | § 5.3 |
| macro | `label`, `description`, `if`, `instruction`, `format` | [05](05-templates-and-macros.md) |
| macro folder `index.json` | `description`, `items`, `if` | [05](05-templates-and-macros.md) |
| deliverable | `kind`, `label`, `description`, `format`, `if`, `check`, `whenEmpty` | [06](06-checks-deliverables-profile.md) |
| `deliverables/index.json`, delivery folder `index.json` | `description`, `items`, `delivery`, `bundle` | [06](06-checks-deliverables-profile.md) |
| `nodes/`, `checks/`, `functions/`, `renderers/`, `snippets/`, `macros/`, `formats/`, `tests/` `index.json`, and a function's four | `description`, `items` | § 2 |
| test | `description`, `inputs`, `profile`, `run`, `outputs`, `expect` | [07](07-compiled-documents.md) |

**Snippet headers are `label`, `description`, `if` and `format`,** with a text format
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)).
Data types for snippets are #1467.

### 5.3 Snippet folders are typed

A snippet folder's `index.json` declares the `format` its items' headers are checked against, and
carries no naming pointer: naming items is only a loop's business
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6880614911),
[#1407 option A](https://github.com/Consultologist/Consultologist-Blazor/issues/1407#issuecomment-6880614239)).

```json
{
  "description": [
    "The nine standard sections of a consult note, in order.",
    "Each item's header holds its title; its body holds the section's instructions."
  ],
  "items": ["hpi", "pmh", "medications", "allergies", "social_history",
            "family_history", "exam", "investigations", "assessment_plan"],
  "format": "standard_section"
}
```

```
---
{ "title": "History of Present Illness" }
---
Chronological prose narrative. …
```

- **The item's header is its value,** checked against the folder's `format` (an object format, so
  every field it declares is present). Its body is its content.
- **A header member called `label` has no special meaning** any more, and v30's
  `name`/`label`/id fallback ends.
- **A folder item has no `if`,** so a folder's size is its number of files. An item is gated by an
  `if` node in the body of the loop over the folder, as `example-conditional-sections` already
  does. Dynamic folder sizes are #1468
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)).

## 6. Ids

An id names a file or folder, or a member of a keyed object.

- **The pattern:** `^[a-z][a-z0-9_]*$`, as since v28. So ids are lowercase, with no case
  collisions, and contain neither `.`, `/` nor `:`.
- **At most 64 characters** (`SourceBounds.idLength`, § 7).
- **One id rule for every id:** nodes, checks, functions, renderers, snippets, snippet and macro
  folders and their items, macros, formats, deliverables, delivery folders, tests, inputs,
  function inputs and parts, including the ones that never become file names
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)).
- **Windows names are refused:** `con`, `prn`, `aux`, `nul`, `com1`–`com9`, `lpt1`–`lpt9`. Windows
  reserves them with or without an extension
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6927247843)).
- **Reserved words are refused anywhere, in singular and plural forms:** `input(s)`, `node(s)`,
  `check(s)`, `function(s)`, `test(s)`, `snippet(s)`, `macro(s)`, `part(s)`, `renderer(s)`,
  `format(s)`, `deliverable(s)`, `param(s)`, `item(s)`, `pass(es)`, `run(s)`, `profile(s)`,
  `value(s)`, `content(s)`
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)).
  They are the roots and members a reference reads, so a node called `check`, `value` or `items`
  could never be named without ambiguity. The list is published beside `SourceBounds` (§ 7).
- **Object field names have no reserved list:** they follow the id pattern only. A field is always
  read after `.value` (`item.value.title`), never beside a member, and `.title` as a label applies
  only to an enumerated string, which has no fields. So a field may be called `title`
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868); [part 02 § 5.3](02-types-and-inputs.md#53-object-schemas-are-fully-typed-nested-text-references-a-text-format)).
- **`parts` and `part.<id>` keep their names:** `parts` is an agent node's header member and
  `part.` a root, neither of them an id (part 05).
- **The migration renames reserved ids,** reports every rename and rewrites every reference to it.
  In example packages each rename is hand-chosen: `example-nested-note`'s loop `parts` becomes
  `sections`. In account packages the kind is added as a suffix (`parts` → `parts_for`)
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910);
  [part 10](10-migration-and-tools.md)).

## 7. `SourceBounds`

A spec-level object of limits on a package's source, decided on
[#1388](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6927247843).

| Member | Value | Today's largest package |
|---|---|---|
| `files` | 512 files | 31 |
| `fileBytes` | 256 KiB (262,144 bytes) per file | 7 KB |
| `totalBytes` | 2 MiB (2,097,152 bytes) in all | 15.7 KB |
| `pathLength` | 200 characters, counted from the package root | — |
| `idLength` | 64 characters | — |

- **Published in the format-spec release as `bounds.json`.** The library, the Editor, publish,
  import and CI all read them from there. **They are not deployment settings:** a package's
  validity is the same on every deployment, a package can't set them, and raising one is a spec
  change.
- **Publish and import each check every bound,** each with its own sentence. They replace the
  import caps (`WorkflowPackageArchive.cs`: 512 files, 256 KB, 2 MB) and the publish caps
  (`WorkflowPackageContentValidation.cs`: 256 KB, 2 MB), which closes today's gap where publish
  has no file-count cap.
- **No folder-depth bound:** the file model fixes where every file goes, so folder depth follows
  from nesting depth (capped by `FormatBounds.depth` for calls and by `pathLength` before parsing,
  since each folder level takes at least two characters).
- **What counts**
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)):
  - `files` counts every file, `index.json` included (and so every empty folder's index);
  - `fileBytes` and `totalBytes` count bytes after line-ending normalisation (§ 2.4);
  - `pathLength` counts characters from the package root, with `/` separators and the extension
    (`nodes/sections/paragraphs/draft_paragraph.md` is 44).
- **Before counting** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1393#issuecomment-6984396609)): ignored `resolve.json` and `lock.json` copies are dropped first;
  `totalBytes` is measured uncompressed; the bounds are checked before anything is parsed, in the
  order of the table, and every exceeded bound is reported.
- **`bounds.json`'s shape** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6984743967)):
  ```json
  {
    "specVersion": 31,
    "sourceBounds": { "files": 512, "fileBytes": 262144, "totalBytes": 2097152,
                      "pathLength": 200, "idLength": 64 },
    "formatBounds": { "passes": 15, "depth": 15, "parallel": 15 },
    "windowsReservedIds": ["con", "prn", "aux", "nul",
                           "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
                           "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9"],
    "reservedIds": ["input", "inputs", "node", "nodes", "check", "checks", "function", "functions",
                    "test", "tests", "snippet", "snippets", "macro", "macros", "part", "parts",
                    "renderer", "renderers", "format", "formats", "deliverable", "deliverables",
                    "param", "params", "item", "items", "pass", "passes", "run", "runs",
                    "profile", "profiles", "value", "values", "content", "contents"]
  }
  ```

## 8. `FormatBounds`

Constants of the format, one member per kind of limit, decided on
[#1392](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6904683449).

| Member | Value | Limits |
|---|---|---|
| `passes` | 15 | a `while`'s `max`: 1 to 15 |
| `depth` | 15 | nested function calls, measured by the checker: 0 to 15 |
| `parallel` | 15 | a loop's `maxParallel`: 2 to 15, or `null` |

- **Defined in the spec and the format library,** covered by the dialect version (#1390), and
  published in `bounds.json` (§ 7). **Never written in a package:** packages declare their own
  `max` and `maxParallel` within them.
- **Every member starts at 15** (`0xF`) and can be **raised alone** later, which is always
  compatible. Lowering one would break packages.
- **`maxParallel: null`** means "no cap of its own": the loop is limited only by the engine's
  per-job ceiling (#1393, [part 09](09-engine-and-pages.md)). `maxParallel: 1` stays refused,
  pointing to `parallel: false` ([part 04](04-loops-and-functions.md)). Requiring a non-null
  `maxParallel` is #1474 (Milestone 40).
- They replace `MaxPasses = 10` (`WorkflowFlowNodes.cs`), the single `FormatBound` proposed on
  #1419, and "1 to 10".

## 9. Scopes and keys

### 9.1 Roots and namespaces

Each kind of definition has its own namespace, named by its root:

| Namespace | Read as | Unique among |
|---|---|---|
| nodes | `node.<id>` | visible scope (§ 9.2) |
| checks | `check.<id>` | visible scope within `checks/` (§ 9.2) |
| functions | `"function": "<id>"` in a `call` | functions |
| inputs | `input.<id>.value`, `.content`, `.title` ([part 02 § 2.1](02-types-and-inputs.md#21-reading-a-value)) | inputs |
| snippets | `snippet.<id>.content`, `snippet.<folder>.<item>` | snippets; items within their folder |
| macros | `macro.<id>`, `macro.<folder>.<item>` | macros; items within their folder ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1412#issuecomment-6887873980)) |
| renderers | `renderer.<id>` | renderers |
| formats | a `format` name, a bare-id `$ref` | every owner together: a package format may not reuse a built-in or catalogue name ([part 02 § 6](02-types-and-inputs.md#6-format-files-ids-and-references)) |
| deliverables | `deliverable.<path>` (§ 9.3) | their folder |
| tests | file names | tests |

- **A node and a check may share an id** (`node.coverage`, `check.coverage`), since their roots
  differ ([#1395](https://github.com/Consultologist/Consultologist-Blazor/issues/1395)). So may a
  function and a node.
- **Deliverable ids are paths, like node ids:** `deliverables/emr/note.md` is
  `deliverable.emr.note`, and `deliverables/note.md` is `deliverable.note`. The destination is part
  of the id, so the same file name may repeat in different delivery folders, and moving a
  deliverable changes its key
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)).
- **A package renderer may not reuse a built-in renderer's name** (#1384's no-shadowing rule
  applied to renderers, as #1397 asked)
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)).
  Scoping between built-in, catalogue and package ids is #1469 (Milestone 40).

### 9.2 Disjoint scopes: ids repeat only where neither can see the other

Decided on [#1384](https://github.com/Consultologist/Consultologist-Blazor/issues/1384):

- **A node may read** its siblings, the nodes of the bodies that enclose it (aligned by path), and
  the top level. That is lexical scope, so **`node.x` resolves outward, with no new syntax**, and
  nothing is ever written `node.folder.x` in a reference.
- **An id may repeat only where the two nodes are never both visible from the same place.** Two
  sibling loops may each hold a `draft`. A body node may **not** reuse the id of a node its
  enclosing bodies, or the top level, make visible to it. So every `node.x` has exactly one
  candidate, and nothing is resolved by "nearest wins".
- **In terms of folders:** two nodes with the same id are refused when the folder of one encloses,
  or is, the folder of the other. Siblings can't share an id anyway, and the branches of an `if` or
  `case` sit flat in one folder, so they can't either.
- **Within one `while`, ids are unique at any depth,** since a pass (`pass.….node.<id>`) reaches
  into the `while` at any depth
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)).
- **Functions are sealed scopes:** inside a function, ids are free, even repeating top-level ids or
  another function's, since nothing sees both. Within it these rules apply, with `node.x` resolving
  outward only up to the function's root
  ([#1419 decision 6](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6897717122)).
  A function's own definitions may not shadow what it lists in `uses` (part 04).
- **Checks** follow the same rules within `checks/`.
- **Compatible:** a package whose ids are all unique is already valid, and its templates read the
  same.

### 9.3 Keys

**A key is its root, then the ids of its enclosing flow nodes from the top level inward, then its
own id, joined by `.`.** Inside a function, the path goes through the **call node**, not the
function's name
([#1384](https://github.com/Consultologist/Consultologist-Blazor/issues/1384#issuecomment-6904190578),
[#1419 decision 6](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6897717122)).
**Every key is written with its root** (`node.`, `check.`, `deliverable.`), in the record,
`resolve.json`, test files and History links
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)).

| Where | Key |
|---|---|
| a top-level node | `node.plan` |
| inside two loops | `node.sections.paragraphs.draft_paragraph` |
| the same, one instance | `node.sections.paragraphs.draft_paragraph:2/1` |
| a renderer step in that instance | `node.sections.paragraphs.draft_paragraph:2/1/render:1` |
| a top-level call's inner node | `node.hpi_section.standard_draft` |
| a call inside a loop, item 3 | `node.standards.section.standard_draft:3` |
| a nested call | `node.hpi_section.refine_call.judge` |
| a check inside a check loop, item 2 | `check.part_coverage.coverage:2` (lines up with `node.sections:2`) |
| a deliverable in the `emr` folder | `deliverable.emr.note` |

- **`.` separates scopes;** `:` separates the key from the instance's item path; `/` separates the
  path's item ids, as at 30. No id or item id contains any of them.
- **Sub-rows** extend an instance's key with `/`: `…/render:<n>`, `…/macro:<id>`, `…/spot:<n>` and
  `…/if:<site>`, for example
  `node.standards.assessment:7/if:macro.letrozole_prescription_text`
  ([part 08](08-record.md)). Item paths never contain `:`.
- **Mermaid ids replace `.` with `__`:** `node__sections__paragraphs__draft_paragraph`
  ([part 09](09-engine-and-pages.md)).
- **Keys are used everywhere at 31,** never only where an id repeats: one key form, no
  conditional.
- **Where a key replaces a bare id:**
  - **the record** (record storage 12): `nodeOutputs` keys, block ids, `skippedBlocks`, item-step and
    block events, item progress, collections, the concept source and the `nodes` rows ([part 08](08-record.md));
  - **the engine:** scheduling keys, activity inputs, the derived edges and the walker's resolution
    ([part 09](09-engine-and-pages.md));
  - **the pages:** Mermaid ids, the Editor's selection keys and links, the rail and History;
  - **`resolve.json` and test files** ([part 07](07-compiled-documents.md)).
- **References inside a package** stay `node.x` and resolve outward (§ 9.2); the resolver writes
  each resolved reference as its key in `resolve.json` (part 07).
- **How pages show two nodes with the same id:** the Editor's tree places each under its folder
  (`sections › paragraphs › draft`); a pane shows `id draft · path node.sections.paragraphs.draft`;
  selection and links use the key; History and the rail show the label, with the path in a muted
  line where two rows share a label
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1384#issuecomment-6904190578)).

## Refusals

Every rule below is decided. The wording below is the starting text. It follows the style rules in [index § 7](index.md#7-refusal-sentences) and is used as written in Milestone 39 and reviewed in #1483 (Milestone 40); every sentence is listed in [refusal-sentences.md](refusal-sentences.md).

**Decided wording:**
- Duplicate in an overlapping scope ([#1384](https://github.com/Consultologist/Consultologist-Blazor/issues/1384#issuecomment-6904190578)):
  *"Node '<id>' in <path> reuses the id of node '<id>' <at the top level | in <other path>>, which <path> can read: rename one. Ids may repeat only where neither can see the other (for example in two sibling loops)."*
- `FormatBounds` ([#1392](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6904683449)):
  - *"While '<id>' declares max <n>; a while runs at most FormatBounds.passes (15) passes."*
  - *"Call '<id>' reaches depth <n>; calls nest at most FormatBounds.depth (15) deep."*
  - *"Loop '<id>' declares maxParallel <n>; the most is FormatBounds.parallel (15)."*

**Files and folders.**
- *"File '<path>' is not listed: every file in <folder> is named once in its index.json, or is index.json or README.md."*
- *"<folder> has no index.json; every folder lists its members, even when it has none (\"items\": [])."*
- *"The package has no <folder>/ folder; all nine top-level folders are always present."*
- *"<list> lists '<id>', but <folder> has no <id>.md or <id>/."*
- *"<list> lists '<id>' twice."*
- *"<list> lists '<id>/'; list a folder by its bare id."*
- *"<folder> holds both <id>.md and <id>/: an id names one file or one folder."*
- *"Delivery folder '<path>' is inside another delivery folder; delivery folders are one level deep."*
- *"File '<path>' starts with a byte-order mark; save it as UTF-8 without one."*
- *"File '<path>' contains a carriage return on line <n> that doesn't end the line; lines end with \n."*

**Front matter.**
- *"File '<path>' must start with a line of exactly ---, then one JSON object, then a line of exactly ---."*
- *"The header of '<path>' is not valid JSON: <parser message>."*
- *"The header of '<path>' declares '<member>' twice."*
- *"<Kind> '<id>' has an empty body; its <prompt | template | text> is the body."*
- *"<Kind> '<id>' has a body; a <computed node | call | check> keeps everything in its header."*

**Members.**
- *"<Declaration> is missing '<member>'; every member is written, and null means none."*
- *"<Declaration> sets '<member>' to null, which it can never be."*
- *"<Declaration> in '<path>' declares unknown member '<member>'. Did you mean '<closest>'?"*
- *"<Declaration> has an empty description; write null for none."*
- *"<Declaration>'s description is <n> characters; the most is 500."*
- *"<Declaration> has an empty label."*
- *"<Node> '<key>' declares reproducible <value>, but its <kind | body | function> makes it <derived>."*
- *"<Node> '<key>' is kind <kind> and declares producer '<value>'; a <kind> node's producer is <generated | computed>."*
- *"<Node> '<key>' declares <modelDerived | runDependent> <value>, but its reads make it <derived>: <the read that does, or none>."*

**Ids.**
- *"'<id>' is not a valid id: a lowercase letter, then lowercase letters, digits or underscores."*
- *"Id '<id>' is <n> characters; the most is SourceBounds.idLength (64)."*
- *"Id '<id>' is reserved on Windows (con, prn, aux, nul, com1–com9, lpt1–lpt9) and can't name a file: rename it."*
- *"Id '<id>' is reserved: <root | member> '<word>' is read in references, so nothing may be called '<id>'."*
- *"Format '<id>' reuses the name of a <built-in | catalogue> format: rename it."*
- *"Renderer '<id>' reuses the name of a built-in renderer: rename it."*
- *"Node '<id>' appears twice inside while '<while>' (<path> and <path>): pass.….node.<id> must name one node."*

**`SourceBounds`.**
- *"The package has <n> files; the most is SourceBounds.files (512)."*
- *"File '<path>' is <n> KiB; the most is SourceBounds.fileBytes (256 KiB)."*
- *"The package is <n> MiB; the most is SourceBounds.totalBytes (2 MiB)."*
- *"Path '<path>' is <n> characters; the most is SourceBounds.pathLength (200), counted from the package root."*

## Proposed in this part

Nothing. Every point is decided; refusal sentences are used as written and reviewed in #1483 ([refusal-sentences.md](refusal-sentences.md)).
