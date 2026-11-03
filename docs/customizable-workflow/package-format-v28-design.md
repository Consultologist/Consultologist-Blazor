# specVersion 28 — a file name is its id; the manifest stops spelling paths

> **Status:** design, **revision 4** (2026-11-02). Decided with the package author; being
> implemented (#1268; layer A #1283). Revision 1 made `index.json` a bare list of ids; revision 2
> made it an object with an optional folder `label` (§ 3); revision 3 numbered it: **28**, shipped
> alone, ahead of the dynamic half, which moves to **29** (#1062; v26 design § 6), as 27 shipped
> the static shape alone. Revision 4 (planning layer A): the paths are implied in **two parts**,
> not one step after the read (§ 6), and a flow node gains a display-only `description`
> (§ 6a, #1270). Revisions 1–2 said "N" while the number was open. Design-level, not a
> step-by-step plan; the normative text follows in the package-format registry as
> `package-format-v28.md` once layer A lands (§ 11).
>
> **One-line model:** at 26 a node names its prompt file, a macro or snippet maps its id to a
> path, and a folder's `index.json` pairs each item id with a file. 28 makes the name the id:
> `prompts/<id>.md`, `macros/<id>.md`, `snippets/<id>.md`, `<folder>/<id>.md`. The manifest stops
> spelling what the id implies. No hash moves, the record keeps its shape, and the engine reads
> the implied paths into the model it already has.

The idea is the author's (2026-11-01): ids and file names should be the same, and once they are,
the manifest no longer needs to spell the path. The Editor's convention came first. #1267 names
a new file after its id, and offers "rename the file to match" for one that is not. #1225 moves a
node's prompt when the node is renamed. This is the format rule that follows.

## Why

**Two names for one thing drift.** At 27 the ten public packages carry 61 node prompts, and 58
of them are named differently from their node. Most of those are the kebab-case spelling of a
snake_case id (`identify_problem` → `prompts/identify-problem.md`). Four are other words
altogether (`scope` → `classify-scope.md`, `tone` → `classify-tone.md`,
`patient_header` → `header.md`, `standing_draft` → `standing-section.md`).

**An author reading a template has to look the file up.** A template reads `node.identify_problem`,
`macro.closing` and `snippet.sections` by id, while the files are found through the manifest.

**The path is redundant.** Each node owns one file in practice: no public package shares a
prompt. A macro's or snippet's path already equals its id in every package. Folder items differ
only by case.

## The port list

| at 27 | at 28 | § |
|---|---|---|
| a node's prompt is the file it names: `"prompt": "prompts/identify-problem.md"` | a non-computed node's prompt is **`prompts/<id>.md`**; `prompt` is retired and refused by name | § 1, § 2 |
| `macros` and `snippets` are maps, id → path; a path ending `/` is a folder | **lists of ids**: `"macros": ["closing", "notes/"]`; a trailing `/` marks a folder at `<kind>/<id>/` | § 2 |
| `index.json` is an ordered list of `{ id, file }`; a folder has no label | an **object**, `{ "label"?, "items": ["hpi", "plan"] }`: the ordered ids (each item `<id>.md` beside it) and an optional display label for the folder | § 3 |
| two nodes may name one prompt file | one file per node, by construction; the migration copies a shared file | § 4 |
| no file-naming rule | the stem is the id, everywhere (§ 1); a file under `prompts/`, `macros/` or `snippets/` that names nothing is a **warning** | § 1, § 2 |
| snippet folder item ids are never checked for snake_case (a 26 gap) | every id is snake_case, item ids included | § 3 |
| the record's node row names its `prompt` by path | unchanged: the path is always `prompts/<id>.md` now | § 5 |
| the Editor offers "rename the file to match" and lists mismatches (#1267) | nothing to offer, since a mismatch cannot be spelled; an id rename is a file move | § 7 |
| a folder is a row in the Editor's nav, holding its order arrows | a folder has a **pane**: its label and its item order | § 7 |
| a flow node says what it is for in its one-line `label` only | an optional, display-only **`description`** on `for`, `if` and `case` nodes (#1270) | § 6a |
| `package-format v2026.10.15`, `provenance v2026.10.10` | the bump's package-format version; **provenance unchanged** | § 10 |

**Unchanged from 27, by decision:** everything else. That covers the template objects and what
they read (by id, as before), headers (`label`, `if`) in snippet, macro and item files, formats
and the catalogue, flow nodes and loops, the hash ladders, and record storage 9.

## 1. The rule

Ids are already snake_case at 26: a lowercase letter, then lowercase letters, digits or
underscores (`WorkflowDeclaredIds.IsValid`). At 28 a file's stem is its id:

| declaration | its file |
|---|---|
| a node whose format is not computed | `prompts/<id>.md` |
| a macro | `macros/<id>.md` |
| a macro folder | `macros/<id>/index.json`, items `macros/<id>/<item>.md` |
| a snippet | `snippets/<id>.md` |
| a snippet folder | `snippets/<id>/index.json`, items `snippets/<id>/<item>.md` |

A computed node and a flow node (`if`, `case`, `for`) have no file, as at 26. The rule is the
one `WorkflowFileMove.ImpliedPath` already encodes for the Editor (#1267). The format adopts it.

**Not covered:** `formats/` (local format files), `LICENSE`, `README.md`, `dag.mmd` and anything
at the package root. Local formats are § 12's open question.

## 2. The manifest

**Nodes.** `prompt` is retired. A node at 28 names no file:

```json
"nodes": [
  { "id": "identify_problem", "format": "prose" },
  { "id": "coverage", "format": "terms-subset", "with": { "of": "node.draft", "in": "node.draft" } }
]
```

The validator reads `prompts/identify_problem.md`. `coverage` is computed and reads none.

**Macros and snippets.** Each is a list of ids. A trailing `/` says the id is a folder:

```json
"macros":   ["closing", "notes/"],
"snippets": ["style", "sections/"]
```

`closing` is `macros/closing.md`. `notes/` is the folder `macros/notes/` with its `index.json`.
The id a template reads is the entry without its slash (`macro.notes`, `snippet.sections`). At
26 the map's values were the only place a folder showed. A list keeps the mark on the entry, so
a reader never has to probe the file set to tell a folder from a file.

**The validator's sentences, new at 28** (proposed; the subjects follow the 26 idiom):

- *"Node 'identify_problem' declares prompt; prompt is retired at specVersion 28 — a node's
  prompt is prompts/identify_problem.md."* (the 25 retired-key pattern, with its replacement)
- *"Node 'identify_problem' has no prompt: prompts/identify_problem.md is missing from the
  package."*
- *"Node 'coverage' is computed (format terms-subset) and reads no prompt; remove
  prompts/coverage.md."* (a computed node's implied file present: the 26 "declares prompt, but
  format … is computed" rule, spelled for a file)
- *"macros is a map; at specVersion 28 it is a list of ids — ["closing", "notes/"]."* (with the
  package's own ids, in its order; the same for `snippets`)
- *"Macro 'closing' is missing from the package: macros/closing.md."* /
  *"Macro folder 'notes' is missing its index: macros/notes/index.json."* (the same for snippets)
- *"macros lists 'closing' twice."*; *"Macro id 'Closing' must be snake_case (…)"* (the 26
  sentence, unchanged)
- **Warning:** *"prompts/old-draft.md names no node; at specVersion 28 a prompt file is
  prompts/<node id>.md."* (likewise for an unlisted file under `macros/` or `snippets/`, naming
  what it would have to be called)

**Two writers, one spelling.** The library's writer emits lists and no `prompt` at 28. The macro
table converter (`WorkflowMacroTableConverter`) gains the list-of-ids shape beside the 25 list
and the 26 map. It switches on the JSON token, not on `specVersion` (a property converter cannot
see the manifest's version); the table records the shape it read, and the validator judges the
shape against the version (§ 6). `snippets` gains the same converter and table.

## 3. index.json

```json
{
  "label": "Sections",
  "items": ["hpi", "medications", "assessment_plan"]
}
```

At 28, `index.json` is an object with two keys, the same for a macro folder and a snippet folder.

- **`items`** is required and lists at least one id. The order is the run order, as at 26. Each
  item's file is `<id>.md` beside the index, and its header (`label`, `if`) stays in that file,
  as at 26.
- **`label`** is optional: the folder's display name. The Editor shows the folder id when it is
  absent, as it does today. It is **optional, unlike an item's label**, because no folder has a
  label at 27: requiring one would make the migration invent ten of them.
- **The label is for display only at 28.** The nav and the folder pane show it; a template cannot
  read it (there is no `snippet.sections.label`), so it never reaches rendered text, the preview
  or a hash. Making it readable later adds a read and breaks nothing.
- **The key set is closed.** Any other key is refused by name, which keeps room for a later
  folder `if` or `description` without guessing at them now.

**Why an object in a JSON file, not a Markdown file with a header** (a `_.md`, weighed
2026-11-02): the order is data, and scripts and `jq` read it from plain JSON without splitting a
header out of Markdown. A Markdown body would also need a rule that it is notes only, never
rendered or sent, or authors would expect a template to read it. The object gives the folder its
label and keeps both of those away.

Refused at 28, each naming the folder:
- a bare list, *"snippets/sections/index.json at specVersion 28 is an object:
  { "label"?, "items": ["hpi", "plan"] }."*
- an object item, *"snippets/sections/index.json item { id, file } — at specVersion 28 an
  item is its id: "items": ["hpi", "plan"]."*
- a missing or empty `items`;
- an unknown key, by name;
- a `label` that is not a non-empty string;
- a duplicate id;
- a non-snake_case item id. This closes the 26 gap: the v26 text says item ids are
  snake_case, but only macro-folder items were checked;
- an item whose `<id>.md` is missing.

A `.md` in the folder that `items` does not list gets the § 2 stray-file warning.

## 4. Shared prompts

At 26 two nodes may name one file (`TwoNodes_MayShareOneFile`), and the engine de-duplicates by
path. At 28 each node owns `prompts/<id>.md`, so nothing can say "this node uses that node's
file". The shape is gone by construction. No sentence refuses it, because no manifest can spell it.

**The migration (§ 8) copies** a shared file to each sharing node's path, with a notice naming
them. No public package shares a prompt today.

**What retires with it:** the 26 test is pinned to ≤ 27. `WorkflowFileMove`'s "the prompt is
shared … it keeps its name" exception, and the Editor's "Shared prompt … also used by …" line,
stay for ≤ 27 packages only.

## 5. The record and the hashes

**No hash moves.** Every hash is over text, never over a name:
- There is no package content hash. The record pins the package by `name@vYYYY.MM.N`.
- The stamp (`publish.json`) holds format hashes keyed by format id, not by path.
- The per-node hashes (definitions 5 and 6) cover the rendered prompt and the output. Snippet
  item fields are `id`, `name` and `content`, so no path is reachable from a template.

Renaming a file with its text unchanged leaves every hash where it was.

**The record keeps its shape.** A 26+ node row names its prompt by path
(`provenance-record.md`, "a node's row names its prompt by path"). At 28 that path is always
`prompts/<id>.md`. The field stays, redundant but harmless, so History and the run diagram read
a 28 record exactly as they read a 27 one. Record storage stays **9**, and the provenance
registry does not move.

## 6. The engine

*(Revised in revision 4. Revision 1–3 said "one normalisation, right after the read". Planning
layer A found two reasons it cannot work as one step: about eight readers deserialize a manifest
without `WorkflowPackageManifestJson.Read` — publish, the diagram endpoint, lineage, catalog
strands, the Editor's walk, the migrations and the scripts — and whether a node has a prompt
depends on its format being computed, which needs the format catalogue the read does not have.)*

The engine's model keeps its path fields: `WorkflowNodeSpec.Prompt`, `WorkflowMacroSpec.File`,
the snippet table and the index's `(id, file)` pairs. At 28 they are filled **in two parts**:

1. **The tables, in the converters.** `macros` and `snippets` read a list of ids into the same
   entries the 26 map gives: `macros/<id>.md`, or `macros/<id>/` for a folder entry, and the
   same under `snippets/`. A converter runs on every deserialize, whatever the reader, so every
   reader sees paths. The table records the shape it read (list, map, list of ids) and any
   duplicate, for the validator.
2. **The node prompts, from the files.** A non-flow node's prompt is `prompts/<id>.md` **when
   that file is in the package** (`WorkflowImpliedPaths.WithImpliedPrompts(manifest, files)`).
   This needs no catalogue. The validator, which has the catalogue, judges what is left: a
   non-computed node whose file is missing is refused, and so is a computed node whose file is
   present (§ 2). The store has no file list before it downloads, so at 28 it fetches each
   candidate prompt — a non-flow node with a format — optionally, and a miss is the validator's
   to name (layer B).

**The folder index** is read by one reader for every caller, given the version:
`WorkflowTextHeaders.ReadIndex(indexJson, folder, noun, specVersion, errors)`. Below 28 it is
`ReadIndexAt26` exactly; at 28 it reads the object and yields `(id, id + ".md")` for each of its
`items`, with the folder's `label` carried beside the collection for the Editor. The run path
never reads the label.

After that everything downstream works unchanged on the paths it already uses:
`WorkflowPackageStore`'s `RootFilesOf`, `GatherDataFilesAsync` and `Materialise` (one prompt
template per path, now one per node), `WorkflowTextHeaders.Resolve`, `WorkflowDerivedEdges`'
walk, the content-validation closure, and the job starter's recorded `prompt`.

The validator checks the retired `prompt` key and the table shapes on the manifest **as
received**, so an explicit `prompt` is never mistaken for an implied one. It then implies the
prompts and runs its 27 pipeline, adding the § 2–§ 3 sentences and the stray-file warnings.

## 6a. A flow node's description (#1270)

*(Added in revision 4, decided with the package author 2026-11-02.)* A `for`, `if` or `case`
node may say what it is for, beyond its one-line `label`:

```json
{ "id": "standards", "label": "Standards",
  "description": "Drafts one section per standard, each tailored to this patient.",
  "for": "item in snippet.standards", "nodes": ["section_instructions"], "ret": "node.section_instructions" }
```

- **Optional;** when present, a non-empty string of at most 500 characters (the package
  description's limit, `WorkflowPackageMetadata.MaxDescriptionLength`).
- **Flow nodes only.** On a plain node it is refused: *"Node 'draft' declares description; only a
  for, if or case node carries one — a node's prompt file says what it does."*
- **Display only.** It is never rendered, sent to a model or hashed; templates cannot read it.
  The Editor shows it in the flow node's pane and the Graph pane's box (layer C).
- **28 only.** Below 28: *"Node 'standards' declares description, which requires specVersion
  28."*
- Not a file and not a header: it is one line of authoring metadata, like `label`, and lives
  beside it (the `_.md` alternative was weighed for folders and set aside, § 3).

## 7. The Editor at 28

- **Reading.** `WorkflowManifestReader` reads the lists and implies paths at 28. Its views
  (`NodeView.Prompt`, macro and snippet paths, collection item paths) carry the implied path, so
  the panes keep their keys.
- **Writing.** Composing at 28 writes no `prompt`, lists for `macros` and `snippets`, and the
  `{ "label"?, "items" }` object for every `index.json` (no `label` key until one is set).
- **A folder pane.** At 28 a folder is selectable and gets a pane, for macro and snippet folders
  alike: the label field (empty when unset, the id shown as its placeholder) and the item order,
  whose move arrows and reset move there from the nav. Adding and removing items stay where they
  are. At ≤ 27 a folder stays a nav row, since its index has nowhere to keep a label.
- **New files** are already `<id>.md` (#1267).
- **A flow node's description** (§ 6a): a text area in the flow node's pane, beside its label,
  and the text under the box's label in the Graph pane. At 27 and below the field is not offered.
- **Renames move files.** Renaming a node already moves its prompt (#1225). At 28, renaming a
  macro, a snippet, a folder or a folder item also moves its file, folder or item file, and
  rewrites every read (`macro.<id>`, `snippet.<id>`), because an id rename *is* a file move.
  This is new Editor work, specified in the layer C issue.
- **Retired at 28:** #1267's "rename the file to match" (pane headers) and the Package pane's
  "File names" line, since a mismatch cannot be spelled. Both stay for ≤ 27 packages. The
  header rule (a pane names a file only when it is not the implied path) shows no file at all
  at 28.
- **The upgrade** gains a rung after the 27 raise in the chain (Templates.razor's
  `MigrateToNewestAsync`): the § 8 migration, its notices under "Through 28:", refusing by its
  own name.

## 8. Migration 27 → 28

The migration is a real rewrite, not a raise, so it lives in `Consultologist.PackageFormat`
beside `WorkflowPackageMigration25To26` (`WorkflowPackageMigration27To28`, returning the shared
`Migration26Result`). It is shared by the Editor's upgrade and by
`scripts/migrate-workflow-package.cs --to 28`, which writes added and removed files the way its
26 stage does (`MigrationRunner.Write`).

1. **Unshare.** Copy each prompt that several nodes name to every node's own path.
2. **Rename.** Move every mismatch with `WorkflowFileMove.Apply`: node prompts, macros, snippets
   and folder items. A target that already exists with different text refuses the migration and
   names both paths.
3. **Respell.** Drop every `prompt`. Turn `macros` and `snippets` into lists (folders keep their
   `/`), and every `index.json` into `{ "items": [...] }`, its ids in their 27 order. No label
   is written: the migration does not invent one.
4. **Validate at 28** in memory. The notices list each rename as `from → to`.

## 9. The ten public packages

All at 27 on consultologist-workflows `b11eacf`. These are the moves the migration makes. No
macro or snippet path moves, because every one already matches its id. No prompt is shared.

| package | prompts | moves | folder items moved |
|---|---|---|---|
| `example-classifier-scope` | 4 | 4: **`scope` ← `classify-scope.md`**, `plan_section`, `decline_letter`, `information_request` | — |
| `example-conditional-documents` | 2 | 2: `consult_note_section`, `progress_note_section` | — |
| `example-conditional-sections` | 5 | 4: **`narrative_draft` ← `narrative-section.md`**, **`table_draft` ← `table-section.md`**, **`standing_draft` ← `standing-section.md`**, `patient_summary` | `sections/safety_netting` ← `safety-netting.md`, `sections/follow_up` ← `follow-up.md` |
| `example-macros` | 5 | 4: **`standing_draft`**, **`narrative_draft`**, **`table_draft`** (← `*-section.md`), `patient_line` | — |
| `example-macros-warnings` | 5 | 4: as `example-macros` | — |
| `example-structured-intake` | 5 | 5: `note_section`, `senior_review_section`, `long_stay_section`, `summarise_note`, `index_note` | — |
| `example-two-documents` | 8 | 8: the seven general nodes (below) and `draft_patient_letter` | — |
| `example-v12-constructs` | 13 | 13: the seven general nodes, **`tone` ← `classify-tone.md`**, **`patient_header` ← `header.md`**, `extract_input_terms`, `extract_note_terms`, `letter_body`, `extract_letter_terms` | — |
| `general` | 7 | 7: the seven general nodes | — |
| `test/general` | 7 | 7: the seven general nodes | — |

Totals: 61 prompts, 58 moves, 2 folder items. **Bold** marks a move to a different word, not
just snake_case. The seven general nodes are `extract_patient_concepts`, `identify_problem`,
`create_typical_trajectory`, `create_patient_trajectory`, `standard_section_draft`,
`patient_section_draft` and `section_instructions`, each currently the kebab-case spelling.

**The packages repo follows.** Its `validate.yml` structural check reads `prompt` values (only
when they hold a `/`), the map values and every index item's `file`. It learns the 28 spelling
before the first 28 package lands. Each package republishes under a new CalVer, **one tag per
push**. `test/general` is the prod pin and moves on the word, after a smoke at 28.

## 10. The registry

- `package-format-v28.md`: the § 1 rule, the § 2 grammar, the § 3 index object, and the migration
  note. `spec-versions.json` lists 28. `schemas/package-format-v28.schema.json` is generated: no
  `prompt` on a node; `macros` and `snippets` arrays of strings matching
  `^[a-z][a-z0-9_]*/?$`; an optional `description` (≤ 500) on a node. The schema does not cover `index.json` (a support file).
- **Vectors under `conformance/v28/`:**
  - valid: the minimal package at 28, migrated from the 27 minimal, as the 25 lesson did;
    a package with a macro folder and a snippet folder.
  - invalid, schema-catchable: `prompt` on a node; `macros` as a map; `snippets` as a map;
    an entry with a capital or hyphen.
  - invalid, `SHAPE_BLIND` (a file set, which the schema cannot see):
    - a missing implied prompt;
    - a missing folder index;
    - a bare-list index (`invalid-index-bare-list-at-28`);
    - an unknown index key (`invalid-index-unknown-key`);
    - an empty `items`;
    - an object item in an index;
    - a duplicate item;
    - a non-snake_case item id;
    - a computed node with a `prompts/<id>.md`.
  - valid: a folder with a `label`; a flow node with a `description`.
  - invalid: a `description` on a plain node; an empty `description`.
  - warning: a stray `prompts/` file; an unlisted `.md` in a folder.
  - the gate under `v27/`: a list-spelled `macros` *"requires specVersion 28"*.

  Run the registry's step-4 schema check locally before pushing; the 25 and 26 lessons both
  needed it.

## 11. Phasing

One layer per PR, each green on its own. The bump is last, as at 25, 26 and 27.

- **A — format, inert** (#1283): the § 6 converters, index reader and implied prompts, the § 2–§ 3
  and § 6a sentences, `WorkflowPackageMigration27To28`, tests; the vectors written and held back
  in the export. (The ten public packages are run through the migration and validated at the seam
  as the PR's evidence: § 9's moves exactly, no refusals.)
- **B — engine** (#1284): the store's optional fetch of each candidate prompt (§ 6), and a
  smoke-level test running a 28 package through `WorkflowPackageStore` and the job starter.
  `SupportedSpecVersions` += 28 is the bump's.
- **C — Editor** (#1285): § 7 reading and writing, the folder pane, the flow node's description
  (§ 6a), id renames that move files, the upgrade rung, retiring #1267's affordances at 28.
- **The bump** (#1286): both version sets += 28; the registry's `package-format-v28.md`, schema
  and vectors, listed; the Editor's ceilings; `scripts/migrate-workflow-package.cs --to 28` (a
  script validates through the public entry, which accepts 28 only from here — as `--to 27` came
  with 27's bump); an engine release on the word.

  *As landed (2026-11-02):* `AcceptedSpecVersions` and `SupportedSpecVersions` list 28; the
  editor's `NewestSpecVersion` and `RunnableSpecVersion` are 28 together, the #1285 seam
  (`AuthorsAboveCeiling`) and its `InternalsVisibleTo` retired, the stored upgrade at version 4;
  the upgrade chains a 26 package through the 27 raise ("Through 27:") into the 28 rewrite;
  `--to 28` validates at 28 in memory before writing (the ten public packages validate, 58 prompts
  and 2 items renamed); next-is-refused moved to 29. Registry package-format **v2026.11.1**
  (consultologist-package-format#43): 28 listed with `package-format-v28.md` and the generated
  schema, `conformance/v28/` (20 vectors: 5 valid, 15 invalid, 11 `SHAPE_BLIND`), the below-28
  gate under `v27/`; 628 cases in all. The release and the content (#1287) follow on the word.
- **E — content:** the packages repo's `validate.yml`, then the ten packages through
  `--to 28 --write` (one PR, one tag per push), a smoke at 28, and the pin on the word.

## 12. Deferred and open

- **#1252, array items named by a field: moved to v29** (2026-11-02, with the package author).
  28 ships without it; a declared `labelField` or a `title` convention is decided with the v29
  design (#1277).

- **The number: decided 28** (2026-11-02). Bundling with the dynamic half was weighed and set
  aside: it saves one bump's mechanical work but holds a finished, self-contained design behind
  the scheduler redesign, and its inert layers would sit beside that redesign's edits to the same
  reader, validator and migration. The dynamic half is 29.
- **Local formats.** `formats` is still a list of `{ id, file }` at 26/27. Proposed: the same
  rule, `formats/<id>.json`, and `formats` a list of ids. Open, because no public package
  declares a local format yet and the catalogue's own naming is the agents repo's.
- **Package names.** Hyphenated package names (`example-macros`) are names, not ids a template
  reads, and are unaffected.
- **A folder's label in templates, and a folder `if`: decided out at 28** (with the package
  author, 2026-11-02). The label is display only (§ 3). A folder `if` would equal the same `if`
  on every item, and no folder in the ten public packages gates an item at all; the one gated
  folder read (`billing`, `example-conditional-sections`) is an `if` flow node around the loop.
  Either is a new key later, not a breaking change, since the key set is closed. A folder `if`
  would read only what is known at job start (a loop's count is fixed then); one reading a
  node's output belongs to the dynamic half.
- **Stray files as errors.** Proposed as a warning, so a package carrying notes or drafts still
  publishes. A later version may refuse them once authors have seen the warning.
