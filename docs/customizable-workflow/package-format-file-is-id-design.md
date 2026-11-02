# specVersion N — a file name is its id; the manifest stops spelling paths

> **Status:** design, **revision 1** (2026-11-02). Milestone 32 (#1268). Decided with the package
> author; not implemented. **The version number is not assigned.** It is either **28**, shipped
> ahead of the dynamic half (which then moves to 29), or the dynamic half's 28 carries it too.
> Until that is decided this text says **N**. Design-level, not a step-by-step plan; the
> normative text follows in the package-format registry as `package-format-vN.md` once layer A
> lands (§ 11).
>
> **One-line model:** at 26 a node names its prompt file, a macro or snippet maps its id to a
> path, and a folder's `index.json` pairs each item id with a file. N makes the name the id:
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

| at 27 | at N | § |
|---|---|---|
| a node's prompt is the file it names: `"prompt": "prompts/identify-problem.md"` | a non-computed node's prompt is **`prompts/<id>.md`**; `prompt` is retired and refused by name | § 1, § 2 |
| `macros` and `snippets` are maps, id → path; a path ending `/` is a folder | **lists of ids**: `"macros": ["closing", "notes/"]`; a trailing `/` marks a folder at `<kind>/<id>/` | § 2 |
| `index.json` is an ordered list of `{ id, file }` | an **ordered list of ids**, `["hpi", "plan"]`; each item is `<id>.md` beside it | § 3 |
| two nodes may name one prompt file | one file per node, by construction; the migration copies a shared file | § 4 |
| no file-naming rule | the stem is the id, everywhere (§ 1); a file under `prompts/`, `macros/` or `snippets/` that names nothing is a **warning** | § 1, § 2 |
| snippet folder item ids are never checked for snake_case (a 26 gap) | every id is snake_case, item ids included | § 3 |
| the record's node row names its `prompt` by path | unchanged: the path is always `prompts/<id>.md` now | § 5 |
| the Editor offers "rename the file to match" and lists mismatches (#1267) | nothing to offer, since a mismatch cannot be spelled; an id rename is a file move | § 7 |
| `package-format v2026.10.15`, `provenance v2026.10.10` | the bump's package-format version; **provenance unchanged** | § 10 |

**Unchanged from 27, by decision:** everything else. That covers the template objects and what
they read (by id, as before), headers (`label`, `if`) in snippet, macro and item files, formats
and the catalogue, flow nodes and loops, the hash ladders, and record storage 9.

## 1. The rule

Ids are already snake_case at 26: a lowercase letter, then lowercase letters, digits or
underscores (`WorkflowDeclaredIds.IsValid`). At N a file's stem is its id:

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

**Nodes.** `prompt` is retired. A node at N names no file:

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

**The validator's sentences, new at N** (proposed; the subjects follow the 26 idiom):

- *"Node 'identify_problem' declares prompt; prompt is retired at specVersion N — a node's
  prompt is prompts/identify_problem.md."* (the 25 retired-key pattern, with its replacement)
- *"Node 'identify_problem' has no prompt: prompts/identify_problem.md is missing from the
  package."*
- *"macros is a map; at specVersion N it is a list of ids — ["closing", "notes/"]."* (with the
  package's own ids, in its order; the same for `snippets`)
- *"Macro 'closing' is missing from the package: macros/closing.md."* /
  *"Macro folder 'notes' is missing its index: macros/notes/index.json."* (the same for snippets)
- *"macros lists 'closing' twice."*; *"Macro id 'Closing' must be snake_case (…)"* (the 26
  sentence, unchanged)
- **Warning:** *"prompts/old-draft.md names no node; at specVersion N a prompt file is
  prompts/<node id>.md."* (likewise for an unlisted file under `macros/` or `snippets/`, naming
  what it would have to be called)

**Two writers, one spelling.** The library's writer emits lists and no `prompt` at N. The macro
table converter (`WorkflowMacroTableConverter`) gains the list shape, keyed off `specVersion`
like the 25-list/26-map switch it already makes.

## 3. index.json

```json
["hpi", "medications", "assessment_plan"]
```

At N, `index.json` is an ordered list of ids. The order is the run order, as at 26. Each item's
file is `<id>.md` beside the index, and its header (`label`, `if`) stays in that file, as at 26.

Refused at N, each naming the folder:
- an object item, *"snippets/sections/index.json item { id, file } — at specVersion N an
  item is its id: ["hpi", "plan"]."*
- a duplicate id;
- a non-snake_case item id. This closes the 26 gap: the v26 text says item ids are
  snake_case, but only macro-folder items were checked.

## 4. Shared prompts

At 26 two nodes may name one file (`TwoNodes_MayShareOneFile`), and the engine de-duplicates by
path. At N each node owns `prompts/<id>.md`, so nothing can say "this node uses that node's
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
(`provenance-record.md`, "a node's row names its prompt by path"). At N that path is always
`prompts/<id>.md`. The field stays, redundant but harmless, so History and the run diagram read
an N record exactly as they read a 27 one. Record storage stays **9**, and the provenance
registry does not move.

## 6. The engine

**One normalisation, right after the read.** The engine's model keeps its path fields:
`WorkflowNodeSpec.Prompt`, `WorkflowMacroSpec.File`, the snippet table and the index's
`(id, file)` pairs. At N the reader fills them from the ids:
- `prompts/<id>.md` for every non-computed, non-flow node;
- `macros/<id>.md`, or `macros/<id>/` for a folder entry;
- `snippets/<id>.md`, or `snippets/<id>/`;
- for an id-list index, `(id, id + ".md")` (`ReadIndexAt26`'s successor).

After that everything downstream works unchanged on the paths it already uses:
`WorkflowPackageStore`'s `RootFilesOf`, `GatherDataFilesAsync` and `Materialise` (one prompt
template per path, now one per node), `WorkflowTextHeaders.Resolve`, `WorkflowDerivedEdges`'
walk, the content-validation closure, and the job starter's recorded `prompt`. The single choke
point is deliberate: the run path gains no "is this N?" branches.

The validator runs the same normalisation before its 27 pipeline. It adds the § 2–§ 3 sentences
for what the normalisation cannot read (a retired key, a map, an object item), and the stray-file
warning.

## 7. The Editor at N

- **Reading.** `WorkflowManifestReader` reads the lists and implies paths at N. Its views
  (`NodeView.Prompt`, macro and snippet paths, collection item paths) carry the implied path, so
  the panes keep their keys.
- **Writing.** Composing at N writes no `prompt`, lists for `macros` and `snippets`, and id
  lists for every `index.json`.
- **New files** are already `<id>.md` (#1267).
- **Renames move files.** Renaming a node already moves its prompt (#1225). At N, renaming a
  macro, a snippet, a folder or a folder item also moves its file, folder or item file, and
  rewrites every read (`macro.<id>`, `snippet.<id>`), because an id rename *is* a file move.
  This is new Editor work, specified in the layer C issue.
- **Retired at N:** #1267's "rename the file to match" (pane headers) and the Package pane's
  "File names" line, since a mismatch cannot be spelled. Both stay for ≤ 27 packages. The
  header rule (a pane names a file only when it is not the implied path) shows no file at all
  at N.
- **The upgrade** gains a rung after the 27 raise in the chain (Templates.razor's
  `MigrateToNewestAsync`): the § 8 migration, its notices under "Through N:", refusing by its
  own name.

## 8. Migration 27 → N

The migration is a real rewrite, not a raise, so it lives in `Consultologist.PackageFormat`
beside `WorkflowPackageMigration25To26` (`WorkflowPackageMigration27ToN`, returning the shared
`Migration26Result`). It is shared by the Editor's upgrade and by
`scripts/migrate-workflow-package.cs --to N`, which writes added and removed files the way its
26 stage does (`MigrationRunner.Write`).

1. **Unshare.** Copy each prompt that several nodes name to every node's own path.
2. **Rename.** Move every mismatch with `WorkflowFileMove.Apply`: node prompts, macros, snippets
   and folder items. A target that already exists with different text refuses the migration and
   names both paths.
3. **Respell.** Drop every `prompt`. Turn `macros` and `snippets` into lists (folders keep their
   `/`), and every `index.json` into its id list.
4. **Validate at N** in memory. The notices list each rename as `from → to`.

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
when they hold a `/`), the map values and every index item's `file`. It learns the N spelling
before the first N package lands. Each package republishes under a new CalVer, **one tag per
push**. `test/general` is the prod pin and moves on the word, after a smoke at N.

## 10. The registry

- `package-format-vN.md`: the § 1 rule, the § 2 grammar, the § 3 index, and the migration
  note. `spec-versions.json` lists N. `schemas/package-format-vN.schema.json` is generated: no
  `prompt` on a node; `macros` and `snippets` arrays of strings matching
  `^[a-z][a-z0-9_]*/?$`. The schema does not cover `index.json` (a support file).
- **Vectors under `conformance/vN/`:**
  - valid: the minimal package at N, migrated from the 27 minimal, as the 25 lesson did;
    a package with a macro folder and a snippet folder.
  - invalid, schema-catchable: `prompt` on a node; `macros` as a map; `snippets` as a map;
    an entry with a capital or hyphen.
  - invalid, `SHAPE_BLIND` (a file set, which the schema cannot see):
    - a missing implied prompt;
    - a missing folder index;
    - an object item in an index;
    - a duplicate item;
    - a non-snake_case item id.
  - warning: a stray `prompts/` file.
  - the gate under `v27/`: a list-spelled `macros` *"requires specVersion N"*.

  Run the registry's step-4 schema check locally before pushing; the 25 and 26 lessons both
  needed it.

## 11. Phasing (once N is assigned)

One layer per PR, each green on its own. The bump is last, as at 25, 26 and 27.

- **A — format, inert:** the § 6 normalisation, the § 2–§ 3 sentences, the writer and macro
  converter at N, `WorkflowPackageMigration27ToN`, tests; the registry document unlisted; the
  vectors held back in the export.
- **B — engine:** nothing beyond reading through A's normalisation. A smoke-level test runs an
  N package through `WorkflowPackageStore` and the job starter. `SupportedSpecVersions` += N is
  the bump's.
- **C — Editor:** § 7 reading and writing, id renames that move files, the upgrade rung,
  retiring #1267's affordances at N.
- **The bump:** both version sets += N; the registry lists N; the Editor's ceilings;
  `scripts/migrate-workflow-package.cs --to N`; an engine release on the word.
- **E — content:** the packages repo's `validate.yml`, then the ten packages through
  `--to N --write` (one PR, one tag per push), a smoke at N, and the pin on the word.

## 12. Deferred and open

- **The number.** Either 28 now, with the dynamic half (#1062, #1194, Milestone 32's title)
  moving to 29, or bundled with the dynamic half. Decided before layer A.
- **Local formats.** `formats` is still a list of `{ id, file }` at 26/27. Proposed: the same
  rule, `formats/<id>.json`, and `formats` a list of ids. Open, because no public package
  declares a local format yet and the catalogue's own naming is the agents repo's.
- **Package names.** Hyphenated package names (`example-macros`) are names, not ids a template
  reads, and are unaffected.
- **Stray files as errors.** Proposed as a warning, so a package carrying notes or drafts still
  publishes. A later version may refuse them once authors have seen the warning.
