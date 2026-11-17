# 01. Files and scopes

This part defines what a version 31 package is on disk: the folders and files, which file holds
what, the front matter, the rule that every member is written (`null` for none), ids and the
limits on them, namespaces and the qualified keys the record uses, and the two sets of format
constants, `SourceBounds` and `FormatBounds`. It covers #1383 (files and folders), #1394 (every
member required), #1384 (namespaces and qualified keys), #1388's source limits and portable ids
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6927247843))
and the `FormatBounds` part of #1392. What each kind of file's own members mean is defined in the
part that owns the kind; this part says where each file lives and which members every file shares.

## 1. The model in one paragraph

A package is a folder. **A leaf is a file** (`<id>.md`): a JSON header between `---` lines, then a
body. **A flow node is a folder** (`<id>/`) described by its `index.json`. **The file or folder
name is the id.** Every folder that holds members lists them, in order, exactly once, and a file
nobody lists is refused. Every member of every declaration is written; `null` means "deliberately
none" and a missing key is an error. Ids may repeat only where neither node can see the other, and
the record names every node by its **qualified key**, its path of ids joined by `.`
([#1383](https://github.com/Consultologist/Consultologist-Blazor/issues/1383),
[#1384](https://github.com/Consultologist/Consultologist-Blazor/issues/1384#issuecomment-6904190578),
[#1394](https://github.com/Consultologist/Consultologist-Blazor/issues/1394)).

| At 30 | At 31 |
|---|---|
| a node is a `nodes` entry in `manifest.json` plus `prompts/<id>.md` | a node is `nodes/<id>.md`, or `nodes/<id>/` with `index.json` for a flow node |
| a bare JSON object at the top of a macro, snippet or item file | front matter: one JSON object between two `---` lines |
| the manifest lists `formats` as `{ id, file }` | `formats/index.json`, a JSON Schema `$defs` of `$ref`s |
| deliverables, with `sources`, in the manifest | `deliverables/<id>.md` and delivery folders (part 06) |
| checks are nodes | `checks/`, read as `check.x` (part 06) |
| renderers are C# | `renderers/<id>.md` (part 05) |
| (none) | `functions/<id>/` (part 04), `tests/<id>.json` (part 07) |
| a stray file is a warning (v28) | a stray file is refused |
| optional members with implied defaults | every member written, `null` for none |
| node ids unique across the package | ids unique per visible scope; qualified keys everywhere |
| import caps 512 files / 256 KB / 2 MB; publish caps 256 KB / 2 MB | `SourceBounds`, checked the same at publish and import |
| `MaxPasses = 10` | `FormatBounds`: `passes`, `depth`, `parallel`, each 15 |

## 2. The folder layout

```
<package>/
  manifest.json            identity, inputs, and the id lists of nodes, macros, snippets (§ 3)
  README.md                optional, documentation only (§ 2.3)
  dag.mmd                  derived view: committed for example packages, generated at publish (part 09)
  nodes/                   production
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
      snippets/ macros/ formats/ renderers/     the function's own definitions
  renderers/               presentation templates (part 05)
    index.json
    <id>.md
  snippets/                prompt input text (part 05)
    <id>.md
    <folder>/
      index.json
      <item>.md
  macros/                  verbatim output text (part 05)
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

`resolve.json` and `lock.json` are **evidence**, written only at publish into the published
version's folder, never into any source. A copy found in an upload is ignored at import and at
publish
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6905180743)).
`workflow.sw.json` is not part of 31: the export moved to Milestone 40 with #1422.

### 2.1 Which file holds what

| File | Holds | Listed by | Members defined in |
|---|---|---|---|
| `manifest.json` | name, version, inputs, the `nodes`/`macros`/`snippets` id lists | — | § 3; inputs in [part 02](02-types-and-inputs.md#8-inputs) |
| `nodes/<id>.md` | an `agent`, `rendered`, `computed` or `call` node: header, then its prompt or template | the manifest's `nodes`, or the enclosing folder's `index.json` | `kind`, `format` in [part 02](02-types-and-inputs.md); `computed` in [part 03](03-expressions.md); `call` in [part 04](04-loops-and-functions.md); an agent's prompt members (`parts`, `onInvalid`) in [part 05](05-templates-and-macros.md) |
| `nodes/<id>/index.json` | a `for`, `while`, `if` or `case`: its members and its children | as above | [part 04](04-loops-and-functions.md) |
| `checks/index.json`, `checks/<id>.md`, `checks/<id>/index.json` | checks, check loops, aggregate checks | `checks/index.json`, or the check loop's `index.json` | [part 06](06-checks-deliverables-profile.md) |
| `functions/<id>/index.json` and its children | a function's signature (`inputs`, `uses`, `ret`, `format`) and body | `functions/index.json` | [part 04](04-loops-and-functions.md) |
| `renderers/<id>.md` | a renderer: header (`input`, `format`), then its template | `renderers/index.json` | [part 05](05-templates-and-macros.md) |
| `snippets/<id>.md` | a snippet: header, then its text | the manifest's `snippets` | [part 05](05-templates-and-macros.md) |
| `snippets/<folder>/index.json`, `<item>.md` | a typed folder: `items`, `format`; each item's header is its value, its body its content | the manifest's `snippets`; the folder's `items` | § 5.3 here; reads in [part 05](05-templates-and-macros.md) |
| `macros/<id>.md` | a macro: header (`if`, `instruction`, `format`), then its verbatim text | the manifest's `macros` | [part 05](05-templates-and-macros.md) |
| `macros/<folder>/index.json`, `<item>.md` | a macro folder: `items` and a shared `if` | the manifest's `macros`; the folder's `items` | [part 05](05-templates-and-macros.md) |
| `formats/index.json`, `formats/<id>.json` | the package's own formats, as JSON Schema | `formats/index.json` | [part 02](02-types-and-inputs.md#6-format-files-ids-and-references) |
| `deliverables/index.json`, `<id>.md`, `<folder>/index.json` | delivery settings per folder; each deliverable's header and composition template | the folder's `index.json` | [part 06](06-checks-deliverables-profile.md) |
| `tests/<id>.json` | one offline test | `tests/index.json` | [part 07](07-compiled-documents.md) |
| `README.md` (any folder) | documentation | — | § 2.3 |
| `dag.mmd` (root only) | the derived Mermaid view | — | [part 09](09-engine-and-pages.md) |

**Proposed:** `checks/`, `renderers/`, `functions/` and `tests/` each have a root `index.json`
listing their members, as `deliverables/index.json` does
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1406#issuecomment-6881816481))
and as `checks/` and `renderers/` already have a required `description`
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6898087160)).
Only the manifest lists `nodes/`, `snippets/` and `macros/`, as decided on
[#1383](https://github.com/Consultologist/Consultologist-Blazor/issues/1383) (decision 3) and
[#1385](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6879096324).

**Proposed:** a top-level folder with no members is absent (a package with no checks has no
`checks/`). `deliverables/` always exists, because its root `index.json` is required.

### 2.2 Children: every member listed exactly once

- **A folder's children** are exactly: its `index.json` (where it has one), the members its list
  names, and an optional `README.md`. A member is `<id>.md` (a leaf), `<id>.json` (in `formats/`
  and `tests/`), or `<id>/` (a folder). **Every child is listed exactly once.**
- **A stray file is refused,** with a sentence naming it
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6924329099)).
  So is a listed member with no file, and a member listed twice.
- **One id, one child:** a folder may not hold both `<id>.md` and `<id>/`.
- **Lists name ids, never paths.** **Proposed:** a folder member is listed by its bare id, without
  the trailing `/` that v28 wrote in the manifest's `snippets` (`"standards/"` becomes
  `"standards"`); the rule above makes the slash redundant, and every list then has one form.
- **Proposed:** names beginning with `.` (`.DS_Store`, `.gitkeep`) are strays like any other.
- **Order:** a list's order is meaningful: the manifest's `nodes` is the top level's order, a
  flow node's `nodes` its body's order, a snippet folder's `items` the order a loop or a join sees.
- **Inside a function folder,** the names `snippets`, `macros`, `formats` and `renderers` hold the
  function's own definitions
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6888253558)).
  **Proposed:** a node inside a function may not use those four ids.
- **Folder depth:** snippet and macro folders hold items only (one level, as at 30). Flow folders
  nest as deep as `FormatBounds` and `pathLength` allow (§ 7, § 8). **Proposed:** delivery folders
  are one level below `deliverables/`.

### 2.3 Descriptions and READMEs

- **Every `index.json` has a required `description`:** an array of strings joined with `"\n"`, or
  `null` for none. It is for people only: the Editor, tooltips and the diagram show it, and it is
  **never sent to a model**
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6898087160)).
- **At most 500 characters,** counted on the joined lines. **Proposed:** counted in Unicode code
  points, as JSON Logic's `size` counts (part 03).
- **Proposed:** the same `description` member, with the same type and limit, is required on every
  node and check header, every input, every function input and the manifest. The manifest's
  `description` becomes lines too (proposed on
  [#1383](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6898087160)).
- **Proposed:** an empty array `[]` is refused for a description; "no description" is `null`.
- **The exception is `formats/index.json`,** which is a JSON Schema document: see
  [part 02 § 6](02-types-and-inputs.md#6-format-files-ids-and-references).
- **`README.md` is allowed in any folder.** It is documentation only: published with the package,
  but outside `resolve.json` and its hashes, and never read by the engine. Packages are public, so a
  README follows the fictional-examples-only rule, which the checker cannot verify.

### 2.4 Encoding and line endings

Every file is UTF-8 with no byte-order mark
([#1383](https://github.com/Consultologist/Consultologist-Blazor/issues/1383), decision 4).
**Proposed:** every line ends with `\n`; a `\r` anywhere is refused, because a body's bytes are
hashed and a checkout's line-ending setting must not change them.

## 3. The manifest

`manifest.json` is a plain JSON object. Every member is present.

| Member | Type | `null` allowed | Meaning |
|---|---|---|---|
| `name` | string | no | the package's name (`example-nested-note`, `test/general`), as at 30 |
| `version` | string | no | `vYYYY.MM.N`, as at 30 |
| `specVersion` | integer | no | `31` |
| `derivedFrom` | string | yes | the `name@version` this package was copied from; `null` if none |
| `title` | string | yes | the display title; `null` shows the ref, as at 30 |
| `description` | array of strings | yes | lines, at most 500 characters joined (§ 2.3) |
| `tags` | array of strings | no | `[]` when none, as since v9 |
| `inputs` | object | no | the inputs, keyed by id, in Create page order ([part 02 § 8](02-types-and-inputs.md#8-inputs)); `{}` when none |
| `nodes` | array of ids | no | the top level of `nodes/`, in order |
| `macros` | array of ids | no | the members of `macros/` (files and folders); `[]` when none |
| `snippets` | array of ids | no | the members of `snippets/` (files and folders); `[]` when none |

**Gone from the manifest:**
- **`formats`:** `formats/index.json` lists them
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6879096324)).
- **`deliverables`** (and v24's `results`): `deliverables/` (part 06).
- **The node objects:** `nodes` is an id list
  ([#1383](https://github.com/Consultologist/Consultologist-Blazor/issues/1383), decision 3).
- **`prompts`, `schemas`, `schemaRefs`, `customSchemas`, `preludes`, `data`, `result`:** members of
  earlier versions, already unused at 30.
- **Proposed: `templating`.** The Scriban version is recorded in `lock.json` and declared in
  `resolve.json`'s `requires` (part 07), so a hand-written copy in the manifest could only disagree.
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
  },
  "nodes": ["plan", "section_count", "parts", "refine"],
  "macros": [],
  "snippets": []
}
```

Its tree:

```
example-nested-note/
  manifest.json
  dag.mmd
  formats/
    index.json  section_plan.json  outline.json  verdict.json
  nodes/
    plan.md                 agent, section_plan
    section_count.md        computed, number
    parts/                  for, over the plan's sections
      index.json
      outline.md            agent, outline
      paragraphs/           for, over the outline's points
        index.json
        draft_paragraph.md  agent, agent_plaintext
    refine/                 while, at most 3 passes
      index.json
      draft_summary.md      agent, agent_plaintext
      judge.md              agent, verdict
  deliverables/
    index.json
    note.md
  tests/
    index.json
    snapshot.json           written by the migration (part 10)
```

`test/general` at 31 adds the other folders:

```
test/general/
  manifest.json             "nodes": ["extract_patient_concepts", "identify_problem",
                            "create_typical_trajectory", "create_patient_trajectory",
                            "prescribes_letrozole", "standards"],
                            "macros": ["letrozole_prescription_text"],
                            "snippets": ["snomed_tool_guidance", "standards"]
  nodes/
    extract_patient_concepts.md  …  create_patient_trajectory.md
    prescribes_letrozole.md      agent, agent_boolean: the macro's classifier (part 05)
    standards/                   for, over snippet.standards, itemLabel "title"
      index.json
      section_instructions.md  patient_section_draft.md  standard_section_draft.md
  snippets/
    snomed_tool_guidance.md
    standards/
      index.json                 { "description": null, "items": ["hpi", "pmh", …], "format": "standard_section" }
      hpi.md  pmh.md  …  assessment_plan.md
  macros/
    letrozole_prescription_text.md
  formats/
    index.json  standard_section.json
  renderers/                     concept_bullets, concept_context as templates (part 05)
    index.json  concept_bullets.md  concept_context.md
  deliverables/
    index.json  consult.md
  tests/
    index.json  snapshot.json
```

(Which of its 30 constructs the migration rewrites into these files, the classifier node and the
`test` macro's move into the deliverable template among them, is
[part 10](10-migration-and-tools.md).)

## 4. Front matter

Every `.md` file with a header (nodes, checks, renderers, snippets, folder items, macros,
deliverables) has the same shape
([#1383](https://github.com/Consultologist/Consultologist-Blazor/issues/1383), decision 4, and
[grammar points](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6924329099)):

```
---
{ "kind": "agent", "label": "Planning the sections", "description": null, "format": "section_plan", … }
---
Plan the sections of a consultation note for the draft below. …
```

- **Line 1 is exactly `---`.** Then **one JSON object**, then a line of exactly `---`.
- **The body starts on the line after the closing `---`.** A blank line there is part of the body,
  and of the prompt's bytes. The Editor and the migration write none.
- **A file may end right after the closing `---`:** a header-only file.
- **A body that is only whitespace is empty.** A body with content is kept byte for byte.
- **The header is JSON, never YAML:** no duplicate keys. **Proposed:** strict RFC 8259 JSON, so no
  comments and no trailing commas (the `jsonc` in this document is for annotation only).
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
matter.

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
- **Proposed:** an unknown member is refused, naming the member and the file. Every declaration
  is closed, as the formats' `additionalProperties: false` is.

### 5.1 The members every header shares

| Member | Type | `null` | On |
|---|---|---|---|
| `kind` | string, one of the kinds ([part 02 § 1](02-types-and-inputs.md#1-kind)) | no | every node and check header, every flow `index.json`, renderers (`rendered`), deliverables (`rendered`) |
| `label` | string | no | every node, check, flow node, function, renderer, macro, deliverable and input. It is the display name everywhere |
| `description` | array of strings | yes | every `index.json` (decided); every header (Proposed, § 2.3) |
| `format` | a format name or an inline schema ([part 02 § 2](02-types-and-inputs.md#2-format)) | no | every node, check, function, renderer, deliverable, macro and input; a snippet folder's `index.json` |
| `if` | a JSON Logic condition ([part 03](03-expressions.md)) | yes: always | every production leaf (`agent`, `rendered`, `computed`, `call`), check, snippet, macro, macro folder and deliverable. A node's own condition is spelled `if`, replacing 30's `when` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6959885064)); a `case` branch keeps `when` for its values (part 04) |

**Proposed:** `label` is non-empty after trimming. A loop's item naming is `itemLabel`, never
`label` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6924320332));
`labelField` is gone.

### 5.2 The members by file

The table names each file's members; the owning part gives their types, null rules and sentences.

| File | Members | Part |
|---|---|---|
| `agent` node | `kind`, `label`, `description`, `format`, `if`, and the prompt members (`onInvalid`, `parts`, …) | [05](05-templates-and-macros.md) |
| `rendered` node | `kind`, `label`, `description`, `format`, `if` | [05](05-templates-and-macros.md) |
| `computed` node, check | `kind`, `label`, `description`, `format`, `if`, `expression` | [03](03-expressions.md), [06](06-checks-deliverables-profile.md) |
| `call` node | `kind`, `label`, `description`, `if`, `function`, `with`, `format` | [04](04-loops-and-functions.md) |
| `for` / `while` / `if` / `case` `index.json` | `kind`, `label`, `description`, `format`, the flow members, `nodes`, `ret` | [04](04-loops-and-functions.md) |
| function `index.json` | `label`, `description`, `inputs`, `uses`, `nodes`, `ret`, `format` | [04](04-loops-and-functions.md) |
| renderer | `kind`, `label`, `description`, `input`, `format` | [05](05-templates-and-macros.md) |
| snippet | Proposed: `label`, `description`, `if`, `format` | [05](05-templates-and-macros.md) |
| snippet folder `index.json` | `description`, `items`, `format` | § 5.3 |
| snippet folder item | its value: exactly the folder's format | § 5.3 |
| macro | `label`, `description`, `if`, `instruction`, `format` | [05](05-templates-and-macros.md) |
| macro folder `index.json` | `description`, `items`, `if` | [05](05-templates-and-macros.md) |
| deliverable | `kind`, `label`, `description`, `format`, `if`, `check`, `whenEmpty` | [06](06-checks-deliverables-profile.md) |
| delivery folder `index.json` | `description`, `items`, `delivery`, `bundle` | [06](06-checks-deliverables-profile.md) |
| `checks/`, `renderers/`, `functions/`, `tests/` `index.json` | Proposed: `description`, `items` | § 2.1 |
| test | `description`, `inputs`, `profile`, `run`, `outputs`, `expect` | [07](07-compiled-documents.md) |

**Proposed:** a flow node writes its `format`, and the checker refuses one that differs from the
format derived from its `ret` ([part 02 § 2.2](02-types-and-inputs.md#22-which-formats-each-kind-may-return)).

**Proposed:** no header has a `reproducible` member at 31; reproducibility is derived and stamped
([part 02 § 1.1](02-types-and-inputs.md#11-reproducible)).

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
  `title` is required there). Its body is its content.
- **A header member called `label` has no special meaning** any more, and v30's
  `name`/`label`/id fallback ends.
- **Proposed:** a folder item has no `if`. An item is gated by an `if` node in the body of the loop
  over the folder, as `example-conditional-sections` already does, so the item's header stays
  exactly its value.

## 6. Ids

An id names a file or folder, or a member of a keyed object.

- **The pattern:** `^[a-z][a-z0-9_]*$`, as since v28. So ids are lowercase, with no case
  collisions, and contain neither `.`, `/` nor `:`.
- **At most 64 characters** (`SourceBounds.idLength`, § 7).
- **Reserved ids are refused:** `con`, `prn`, `aux`, `nul`, `com1`–`com9`, `lpt1`–`lpt9`. Windows
  reserves them with or without an extension. The list is published in the spec beside
  `SourceBounds`
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6927247843)).
- **These rules apply to every id:** nodes, checks, functions, renderers, snippets, snippet and
  macro folders and their items, macros, formats, deliverables, delivery folders, tests, inputs,
  function inputs, and parts. **Proposed:** the ones that never become file names (inputs, function
  inputs, parts) follow the same rules, so there is one rule.
- **Proposed:** a node may not be called `check`. Check keys are written `check.<id>` (§ 9), so a
  top-level flow node named `check` would give its children keys that read as checks.

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

- **Published in the format-spec release.** The library, the Editor, publish, import and CI all
  read them from there. **They are not deployment settings:** a package's validity is the same on
  every deployment, a package can't set them, and raising one is a spec change.
- **Publish and import each check every bound,** each with its own sentence. They replace the
  import caps (`WorkflowPackageArchive.cs`: 512 files, 256 KB, 2 MB) and the publish caps
  (`WorkflowPackageContentValidation.cs`: 256 KB, 2 MB), which closes today's gap where publish
  has no file-count cap.
- **No folder-depth bound:** the file model fixes where every file goes, so folder depth follows
  from nesting depth (capped by `FormatBounds.depth` for calls and by `pathLength` before parsing,
  since each folder level takes at least two characters).
- **What counts.** **Proposed:**
  - `files` and `totalBytes` count every file in the source, `README.md` and `dag.mmd` included,
    after ignored `resolve.json` and `lock.json` copies are dropped;
  - `totalBytes` is uncompressed;
  - `pathLength` counts the characters of the path from the package root, with `/` separators and
    the extension (`nodes/parts/paragraphs/draft_paragraph.md` is 41);
  - the bounds are checked before anything is parsed, in the order of the table, and every
    exceeded bound is reported.
- **Proposed: where it is published.** The registry release carries a `bounds.json` beside the
  spec text:
  ```json
  {
    "specVersion": 31,
    "sourceBounds": { "files": 512, "fileBytes": 262144, "totalBytes": 2097152,
                      "pathLength": 200, "idLength": 64 },
    "formatBounds": { "passes": 15, "depth": 15, "parallel": 15 },
    "reservedIds": ["con", "prn", "aux", "nul",
                    "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
                    "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9"]
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

- **Defined in the spec and the format library,** covered by the dialect version (#1390). **Never
  written in a package:** packages declare their own `max` and `maxParallel` within them.
- **Every member starts at 15** (`0xF`) and can be **raised alone** later, which is always
  compatible. Lowering one would break packages.
- **`maxParallel: null`** means "no cap of its own": the loop is limited only by the engine's
  per-job ceiling (#1393, [part 09](09-engine-and-pages.md)). `maxParallel: 1` stays refused,
  pointing to `parallel: false` ([part 04](04-loops-and-functions.md)).
- They replace `MaxPasses = 10` (`WorkflowFlowNodes.cs`), the single `FormatBound` proposed on
  #1419, and "1 to 10".

## 9. Scopes and qualified keys

### 9.1 Roots and namespaces

Each kind of definition has its own namespace, named by its root:

| Namespace | Read as | Unique among |
|---|---|---|
| nodes | `node.<id>` | visible scope (§ 9.2) |
| checks | `check.<id>` | visible scope within `checks/` (§ 9.2) |
| functions | `"function": "<id>"` in a `call` | functions |
| inputs | `input.<id>` | inputs |
| snippets | `snippet.<id>`, `snippet.<folder>.<item>` | snippets; items within their folder |
| macros | `macro.<id>`, `macro.<folder>.<item>` | macros; items within their folder ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1412#issuecomment-6887873980)) |
| renderers | `renderer.<id>` | renderers |
| formats | a `format` name, a bare-id `$ref` | every owner together: a package format may not reuse a built-in or catalogue name ([part 02 § 6](02-types-and-inputs.md#6-format-files-ids-and-references)) |
| deliverables | `deliverable.<id>` | Proposed: all deliverables, at any folder depth |
| tests | file names | tests |

- **A node and a check may share an id** (`node.coverage`, `check.coverage`), since their roots
  differ ([#1395](https://github.com/Consultologist/Consultologist-Blazor/issues/1395)). So may a
  function and a node.
- **Proposed: delivery folders are not a namespace.** They exist for routing
  ([#1406](https://github.com/Consultologist/Consultologist-Blazor/issues/1406#issuecomment-6881816481));
  a namespace would change a deliverable's key whenever its routing changed.
- **Proposed:** a package renderer may not reuse a built-in renderer's name (#1384's no-shadowing
  rule applied to renderers, as #1397 asked).

### 9.2 Disjoint scopes: ids repeat only where neither can see the other

Decided on [#1384](https://github.com/Consultologist/Consultologist-Blazor/issues/1384):

- **A node may read** its siblings, the nodes of the bodies that enclose it (aligned by path), and
  the top level. That is lexical scope, so **`node.x` resolves outward, with no new syntax**, and
  nothing is ever written `node.folder.x`.
- **An id may repeat only where the two nodes are never both visible from the same place.** Two
  sibling loops may each hold a `draft`. A body node may **not** reuse the id of a node its
  enclosing bodies, or the top level, make visible to it. So every `node.x` has exactly one
  candidate, and nothing is resolved by "nearest wins".
- **In terms of folders:** two nodes with the same id are refused when the folder of one encloses,
  or is, the folder of the other. Siblings can't share an id anyway, and the branches of an `if` or
  `case` sit flat in one folder, so they can't either.
- **A `while`'s passes** (`pass.….node.<id>`) reach into the `while` at any depth, so the rule must
  cover what a pass can name. **Proposed:** within one `while` folder, at any depth, every id is
  unique.
- **Functions are sealed scopes:** inside a function, ids are free, even repeating top-level ids or
  another function's, since nothing sees both. Within it these rules apply, with `node.x` resolving
  outward only up to the function's root
  ([#1419 decision 6](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6897717122)).
  A function's own definitions may not shadow what it lists in `uses` (part 04).
- **Checks** follow the same rules within `checks/`.
- **Compatible:** a package whose ids are all unique is already valid, and its templates read the
  same.

### 9.3 Qualified keys

**A node's qualified key is the ids of its enclosing flow nodes, from the top level inward, then
its own id, joined by `.`.** A top-level node's key is its id. Inside a function, the path goes
through the **call node**, not the function's name
([#1384](https://github.com/Consultologist/Consultologist-Blazor/issues/1384#issuecomment-6904190578),
[#1419 decision 6](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6897717122)).

| Where | Qualified key |
|---|---|
| a top-level node | `plan` |
| inside two loops | `parts.paragraphs.draft_paragraph` |
| the same, one instance | `parts.paragraphs.draft_paragraph:2/1` |
| a top-level call's inner node | `hpi_section.standard_draft` |
| a call inside a loop, item 3 | `standards.section.standard_draft:3` |
| a nested call | `hpi_section.refine_call.judge` |
| a check inside a check loop, item 2 | `check.part_coverage.coverage:2` |

- **`.` separates scopes;** `:` separates the key from the instance's item path; `/` separates the
  path's item ids, as at 30. No id or item id contains any of them.
- **Check keys carry their root** (`check.`), as every decided example writes them. **Proposed:**
  node keys carry none, as every decided example writes them, and the reserved node id `check`
  (§ 6) keeps the two forms apart.
- **Qualified keys are used everywhere at 31,** never only where an id repeats: one key form, no
  conditional.
- **Where a bare id becomes a qualified key:**
  - **the record** (record storage 12): `nodeOutputs` keys, block ids, `skippedBlocks`, item-step and
    block events, item progress, collections, the concept source and the `nodes` rows ([part 08](08-record.md));
  - **the engine:** scheduling keys, activity inputs, the derived edges and the walker's resolution
    ([part 09](09-engine-and-pages.md));
  - **the pages:** Mermaid ids, the Editor's selection keys and links, the rail and History.
- **References inside a package** stay `node.x` and resolve outward (§ 9.2); the resolver writes
  each resolved reference as its qualified key in `resolve.json` (part 07).
- **How pages show two nodes with the same id:** the Editor's tree places each under its folder
  (`parts › paragraphs › draft`); a pane shows `id draft · path parts.paragraphs.draft`; selection
  and links use the qualified key; History and the rail show the label, with the path in a muted
  line where two rows share a label
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1384#issuecomment-6904190578)).

## Refusals

Sentences marked **Proposed** are this design's wording, for the package author to confirm; the
others are decided, with their link.

**Decided:**
- Duplicate in an overlapping scope ([#1384](https://github.com/Consultologist/Consultologist-Blazor/issues/1384#issuecomment-6904190578)):
  *"Node '<id>' in <path> reuses the id of node '<id>' <at the top level | in <other path>>, which <path> can read: rename one. Ids may repeat only where neither can see the other (for example in two sibling loops)."*
- `FormatBounds` ([#1392](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6904683449)):
  - *"While '<id>' declares max <n>; a while runs at most FormatBounds.passes (15) passes."*
  - *"Call '<id>' reaches depth <n>; calls nest at most FormatBounds.depth (15) deep."*
  - *"Loop '<id>' declares maxParallel <n>; the most is FormatBounds.parallel (15)."*

**Proposed: files and folders.**
- *"File '<path>' is not listed: every file in <folder> is named once in <list>, or is index.json or README.md."*
- *"<list> lists '<id>', but <folder> has no <id>.md or <id>/."*
- *"<list> lists '<id>' twice."*
- *"<folder> holds both <id>.md and <id>/: an id names one file or one folder."*
- *"File '<path>' starts with a byte-order mark; save it as UTF-8 without one."*
- *"File '<path>' contains a carriage return on line <n>; lines end with \n only."*

**Proposed: front matter.**
- *"File '<path>' must start with a line of exactly ---, then one JSON object, then a line of exactly ---."*
- *"The header of '<path>' is not valid JSON: <parser message>."*
- *"The header of '<path>' declares '<member>' twice."*
- *"<Kind> '<id>' has an empty body; its <prompt | template | text> is the body."*
- *"<Kind> '<id>' has a body; a <computed node | call | check> keeps everything in its header."*

**Proposed: members.**
- *"<Declaration> is missing '<member>'; every member is written, and null means none."*
- *"<Declaration> sets '<member>' to null, which it can never be."*
- *"<Declaration> declares unknown member '<member>'."*
- *"<Declaration> has an empty description; write null for none."*
- *"<Declaration>'s description is <n> characters; the most is 500."*
- *"<Declaration> has an empty label."*

**Proposed: ids.**
- *"'<id>' is not a valid id: a lowercase letter, then lowercase letters, digits or underscores."*
- *"Id '<id>' is <n> characters; the most is SourceBounds.idLength (64)."*
- *"Id '<id>' is reserved on Windows (con, prn, aux, nul, com1–com9, lpt1–lpt9) and can't name a file: rename it."*
- *"A node can't be called check: check.<id> names checks."*
- *"Node '<id>' in function '<function>' uses the name of the function's <snippets | macros | formats | renderers> folder: rename it."*
- *"Format '<id>' reuses the name of a <built-in | catalogue> format: rename it."*
- *"Renderer '<id>' reuses the name of a built-in renderer: rename it."*
- *"Deliverables '<path>' and '<path>' share the id '<id>': deliverable ids are unique across every delivery folder."*
- *"Node '<id>' appears twice inside while '<while>' (<path> and <path>): pass.….node.<id> must name one node."*

**Proposed: `SourceBounds`.**
- *"The package has <n> files; the most is SourceBounds.files (512)."*
- *"File '<path>' is <n> KiB; the most is SourceBounds.fileBytes (256 KiB)."*
- *"The package is <n> MiB; the most is SourceBounds.totalBytes (2 MiB)."*
- *"Path '<path>' is <n> characters; the most is SourceBounds.pathLength (200), counted from the package root."*

## Proposed in this part

- **Root `index.json` for `checks/`, `renderers/`, `functions/`, `tests/`** (`description`, `items`): the stray-file rule needs a list for every folder, and `checks/` and `renderers/` already carry a required `description`.
- **Empty top-level folders are absent; `deliverables/` always exists:** #1394 governs members, not files.
- **Folder members listed by bare id, no trailing `/`:** one id cannot name both a file and a folder, so the slash says nothing.
- **Dot-files are strays:** one rule, no exceptions to explain.
- **No node called `snippets`, `macros`, `formats` or `renderers` inside a function:** those names hold the function's own definitions.
- **Delivery folders one level deep:** routing needs one level; nothing asked for more.
- **`description` (lines or `null`, ≤500) on every header, input, function input and the manifest; `[]` refused; counted in code points:** one rule for every description, and the manifest's was already proposed on #1383.
- **`\r` refused:** bodies are hashed byte for byte, and a checkout's line-ending setting must not change them.
- **Strict RFC 8259 JSON in headers (no comments, no trailing commas):** one parser, one canonical form.
- **`templating` leaves the manifest:** `lock.json` and `requires` record the Scriban version.
- **Unknown members refused:** every declaration is closed, matching the formats' `additionalProperties: false`.
- **`label` non-empty:** it is shown everywhere as the display name.
- **Flow nodes write `format`, checked against the derived one:** #1385 decision 2 says every node declares its format; some examples on #1396 and #1406 omit it.
- **No `reproducible` member at 31:** it is derived; see part 02 § 1.1.
- **Snippet headers carry `label`, `description`, `if`, `format`:** a snippet has an `if` (#1412) and its text must be typed for #1406's strict reads; no issue lists the members.
- **Snippet folder items have no `if`:** the item header is exactly its value (#1407); gating is an `if` node in the loop body.
- **The id rules apply to inputs, function inputs and parts too:** one rule for every id.
- **No node called `check`:** keeps node keys and `check.` keys from colliding.
- **Within one `while`, ids are unique at any depth:** `pass.….node.<id>` reaches into the while at any depth (#1384 asks the rule to cover it).
- **Node keys unprefixed, check keys prefixed `check.`:** as every decided example writes them.
- **Deliverable ids unique across delivery folders; delivery folders not a namespace:** routing changes must not change keys.
- **A package renderer may not reuse a built-in renderer's name:** #1384's no-shadowing rule, as #1397 asked.
- **`SourceBounds` counting rules and a `bounds.json` in the registry release:** the decision says the values are published, not in which file or how each is counted.
- **Every refusal sentence above marked Proposed:** the issues decided that each rule has a sentence, not its words.
