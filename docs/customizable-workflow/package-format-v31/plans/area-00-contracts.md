# Area 00: the contracts (specVersion 31) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Put the specVersion 31 contracts on the `m39-build` branches of the three content repos (the package-format spec, schemas, constants and conformance frame; the agents catalogue; the provenance contract for record storage 12), pin them under `external/`, and prove the pins with tests, while the engine keeps accepting 5–30 only.

**Architecture:** Everything for 31 is **additive and parallel**: a `v31/` directory and `package-format-v31.md` in `consultologist-package-format`, an `agents/v31/` directory in `consultologist-agents`, new sections and a `preview` member in `consultologist-provenance`. Nothing the engine at 30 reads changes by one byte, because this repo's tests pin the published sets and catalogues to the engine's (`SpecVersionSetTests`, `ProvenanceVersionSetTests`, `ConformanceSuiteTests`, `FormatCatalogLoaderTests`). 31 is named under a new `preview` member in each index, mirroring `WorkflowPackages__PreviewSpecVersions` (part 09 § 2.1); moving it into `supported` is the bump (index § 5 step 4).

**Tech Stack:** Markdown, JSON Schema 2020-12, Python 3 with `jsonschema` 4.x for the content repos' check scripts, Scriban 7.2.5 templates, xUnit in `tests/Consultologist.Api.Tests.csproj`.

**Spec:** `docs/customizable-workflow/package-format-v31/` (`index.md`, parts 01–10, `refusal-sentences.md`), revision 10. Area issue #1497, parent #1496. Runbook: `BUILD-RUNBOOK.md` in the same folder.

## Global Constraints

- **Repos and branches.** Content repos are worked in `~/Projects/consultologist-package-format`, `~/Projects/consultologist-agents`, `~/Projects/consultologist-provenance`, each on a branch `m39-build` created from that repo's `origin/main` and pushed directly (`git push -u origin m39-build`). The engine repo is worked only in `~/Projects/worktrees/m39-area-00` on `m39-area-00`. Never touch `~/Projects/Consultologist-Blazor` or `~/Projects/consultologist-workflows`.
- **Moving a pin:** after pushing a content repo, in the worktree run `git -C external/<repo> fetch origin && git -C external/<repo> checkout <pushed sha> && git add external/<repo>`, commit, push. Pins move only to pushed commits.
- **Never edit** `.github/`, `.claude/`, `AGENTS.md`, `CLAUDE.md`, `scripts/m39-build/` or `BUILD-RUNBOOK.md`, in any repo.
- **Nothing the engine at 30 reads may change:** in package-format, `supported`, `documents`, `schemas` and `version` of `spec-versions.json`, `conformance/index.json` and every file it names, `conformance/catalog-schemas.json`, `conformance/format-catalog.json`, `schemas/package-format-v*.schema.json`, `package-format-v5.md` … `v30.md`. In agents, `agents/formats.json`, `agents/formats/*.json`, `agents/output-contracts.json`, `agents/schemas/*.json`, every existing `agents/*.yaml`. In provenance, the four ladders and `version` of `provenance-versions.json`, the sentence "storage versions 2, 6, 7, 8, 9, 10 and 11", every existing row of `hash-definitions.md`; no new line of `hash-definitions.md` may start with `` | `fileSha256` | ``, `` | `textSha256` | ``, `` | `documentHash` | ``, `` | `documentHash` (specVersion 12) | ``, `| 6 (composed output) |` or `| 6 (agentOutputHash) |`.
- **No release:** no tag, no `version` bump, no publish. Release is the user's step.
- **Content-repo Markdown links:** relative links must resolve inside that repo; absolute links never contain `/blob/main/` or `/tree/main/`. Links to this repo's design use `https://github.com/Consultologist/Consultologist-Blazor/blob/def6f6cf/docs/customizable-workflow/package-format-v31/<file>`.
- **Sentences are used as written:** every refusal, warning and recorded sentence is copied character for character from the design; placeholders stay `<…>`.
- **Fictional only:** every example carries invented people and invented clinical text (the design's Margaret Okafor, Dr A. Sample).
- **Goldens come from code.** This area writes no expected output bytes of code that does not exist yet (operation outputs, printer text, `resolve.json` bytes, hashes of compiled documents). Schema examples are hand-written instances, marked as examples, never goldens.
- **Commits:** small, by concern. Engine repo subjects `Docs (#1497): …`, `Tests (#1497): …`, `PackageFormat (#1497): …`. Content repos: `Format spec (Consultologist-Blazor#1497): …`, `Agents catalogue (Consultologist-Blazor#1497): …`, `Provenance (Consultologist-Blazor#1497): …`. Push after each task. Every commit message ends with `Co-Authored-By: Claude <noreply@anthropic.com>`.
- **`$id`s:** file-kind schemas are JSON Schema 2020-12 (`"$schema": "https://json-schema.org/draft/2020-12/schema"`) with `"$id": "urn:consultologist:spec:31:schema:<name>"`. Formats are in the dialect, `"$schema": "urn:consultologist:spec:31:dialect"`. Built-in formats: `urn:consultologist:spec:31:format:<name>`. Catalogue formats: `urn:consultologist:format:<name>@v2026.12.1` (the design's example version; the release sets the real one).
- **Every member written:** each schema of our own declarations lists every member in `required` and sets `additionalProperties: false`; a nullable member's type includes `null`.

## Layout this plan creates

```
consultologist-package-format (m39-build)
  package-format-v31.md            the normative text, chapters 1–11
  spec-versions.json               + "preview": { "31": { "document": "package-format-v31.md", "directory": "v31" } }
  README.md                        + a "specVersion 31 (preview)" section
  scripts/check-v31.py             the one check: schemas, examples, formats, constants, sentences, links
  scripts/render-sentences.py      writes chapter 11's table from v31/sentences.json
  v31/bounds.json                  part 01 § 7, exactly
  v31/constants.json               versions, kinds, features, producers, limits, Markdig extensions
  v31/profile.json                 the closed profile list and its preview values
  v31/sentences.json               every row of refusal-sentences.md
  v31/dialect.schema.json          the formats dialect's meta-schema
  v31/scriban-dialect.md           the template dialect, version 1.0.0
  v31/formats/index.json, <id>.json            the nine built-in formats
  v31/renderers/index.json, concept_bullets.md, concept_context.md
  v31/schemas/<name>.schema.json   one per file kind and compiled document
  v31/conformance/README.md, index.json
  v31/conformance/schemas/<name>/valid-*.json, invalid-*.json

consultologist-agents (m39-build)
  agents/v31/README.md
  agents/v31/bindings.json
  agents/v31/agents.json           per attested agent version: strictRejects
  agents/v31/formats/index.json, classification.json, concept_list.json
  agents/judgement.yaml            a new attested agent, version "1"
  scripts/check-v31.py

consultologist-provenance (m39-build)
  provenance-versions.json         + "preview": { … }
  provenance-record.md             + "## 6. Storage 12 (specVersion 31, preview)"
  hash-definitions.md              + "## 7. Definitions specVersion 31 adds (preview)"
  registry-layout.md               + "## 10. A specVersion 31 version folder (preview)"
  README.md                        + a note

Consultologist-Blazor (m39-area-00)
  docs/customizable-workflow/package-format-v31/plans/area-00-contracts.md   this plan
  external/*                       the three pins
  tests/V31ContractsTests.cs
  tests/V31BuiltInRenderersTests.cs
```

## Review Focus

1. **A pin that breaks the engine at 30.** Any byte changed in a file the 30 engine reads fails a hundred tests through a static initialiser. Expected: `dotnet test Consultologist.sln` green after every pin move; Task 13 adds `V31ContractsTests.TheEngineAt30_StillSeesItsOwnSets`.
2. **A schema that accepts a declaration with a member left out.** The design's first rule is that every member is written. Expected: each file-kind schema has an `invalid-missing-<member>.json` example that the check refuses (Tasks 4–5).
3. **A schema that refuses the design's own examples.** Expected: each example the design prints for a file kind is a `valid-*.json` instance (Tasks 3–5).
4. **A sentence that drifted from the design's table.** Expected: `V31ContractsTests.EverySentenceOfTheDesign_IsPublishedWordForWord` compares `v31/sentences.json` with `refusal-sentences.md` row by row (Tasks 2, 13).
5. **A built-in renderer whose bytes differ from the C# renderer** on an empty list, a non-SNOMED concept, an inactive concept, or a blank `support`. Expected: `V31BuiltInRenderersTests` (Task 6).

---

### Task 1: package-format — the branch, `bounds.json`, the constants, the profile list, the check script

**Files** (in `~/Projects/consultologist-package-format`):
- Create: `v31/bounds.json`, `v31/constants.json`, `v31/profile.json`, `scripts/check-v31.py`
- Modify: `spec-versions.json` (add `preview` only), `README.md`

**Interfaces — Produces:**
- `python3 scripts/check-v31.py` exits 0 and prints one line per check group; exits 1 listing every failure. Later tasks add check groups to it as functions `check_<group>() -> list[str]` registered in a `CHECKS` list.
- `v31/bounds.json`: exactly the object of part 01 § 7 ("`bounds.json`'s shape").
- `v31/constants.json`:
  ```json
  { "specVersion": 31,
    "dialects": { "formats": "urn:consultologist:spec:31:dialect", "templates": "scriban-dialect@1.0.0" },
    "evaluator": "1.0.0",
    "operations": { "<each of the 18 names of part 07 § 3.1>": "1.0.0" },
    "builtins": { "concept_bullets": "1.0.0", "concept_context": "1.0.0", "formats": "1.0.0",
                  "markdown_escape": "1.0.0", "plain_conversion": "1.0.0", "printer": "1.0.0" },
    "templateBuiltins": { "<each of the 7 of part 05 § 2.2>": "1.0.0" },
    "standardOperations": ["var","missing","if","===","!==","<","<=",">",">=","and","or","!","in","cat","+","-","*","/","%","min","max"],
    "kinds": ["agent","rendered","computed","for","while","if","case","call"],
    "features": ["parallel","macro-markers","parts","run-spots","check-loops"],
    "producers": { "accepted": ["generated","computed"], "reserved": ["grounded","extracted","attention"] },
    "runFacts": ["date","id","package","engine"],
    "channels": ["transcript","image","ambient-note","form"],
    "deliveryKinds": ["email","pdf","emr","zip"],
    "whenEmpty": ["release","hold","fail"],
    "states": ["completed","skipped","not_run"],
    "deliverableStates": ["released","held","failed","skipped","blocked"],
    "edgeKinds": ["reads","if","for","ret","placed","calls","aligned","gates"],
    "limits": { "descriptionCodePoints": 500, "matchesPatternCodePoints": 200, "matchesRepetition": 100,
                "matchesTextBytes": 1048576, "templateForIterations": 1000, "templateNesting": 15,
                "templateOutputBytes": 1048576, "testsSeconds": 60, "itemNameCharacters": 120 },
    "mediaTypes": { "resolve": "application/vnd.consultologist.resolve+json",
                    "lock": "application/vnd.consultologist.lock+json",
                    "publishReport": "application/vnd.consultologist.publish-report+json" },
    "markdig": { "version": "<resolved>", "extensions": ["<names>"] } }
  ```
- `v31/profile.json`: `{ "specVersion": 31, "fields": { "<field>": { "format": …, "label": …, "preview": … } }, "previewTimeZone": "America/Toronto" }` with the five fields of part 06 § 10.1, their formats, the field labels of part 09 § 9 (`name`, `credentials`, `organisation`, `signature`, `closing line`) and fictional preview values (`Dr A. Sample`, `MD, FRCPC`, `Sample Oncology Clinic`, a two-line sample signature, a one-line sample closing).

- [x] **Step 1:** `git fetch origin && git checkout -b m39-build origin/main`.
- [x] **Step 2: Write `scripts/check-v31.py` first, with the `constants` group failing.** The group asserts: `v31/bounds.json` equals the literal of part 01 § 7 (the literal is written in the script); `constants.json` has exactly the keys above, 18 operations, each version `1.0.0`; `spec-versions.json` has `preview["31"] == {"document": "package-format-v31.md", "directory": "v31"}`, `31 not in supported`, `"31" not in documents`. Run `python3 scripts/check-v31.py`; expected: exit 1, files missing.
- [x] **Step 3: Write the three JSON files and the `preview` member.** For `markdig`: in the engine worktree, find the version `0.41.*` resolves to (`dotnet list src/Consultologist.PackageFormat package` or the project that references Markdig) and list the extension type names of `new MarkdownPipelineBuilder().UseAdvancedExtensions().Build().Extensions` with a throwaway `dotnet run` file-based script kept outside every repo (`/tmp`); write the version and the names in pipeline order, each without its `Extension` suffix.
- [x] **Step 4:** `python3 scripts/check-v31.py` exits 0. `git diff origin/main -- spec-versions.json` shows only the added `preview` member.
- [x] **Step 5: README.** Add a section "specVersion 31 (preview)": what `v31/` holds, that 31 is outside `supported` until the engine's bump, how to run the check, and that `.github/workflows/validate.yml` does not run it yet (the release adds it).
- [x] **Step 6:** Commit (`Format spec (Consultologist-Blazor#1497): specVersion 31 preview — bounds, constants, the profile list, the check`), push.

### Task 2: package-format — every sentence, published

**Files:** Create `v31/sentences.json`, `scripts/render-sentences.py`; modify `scripts/check-v31.py`.

**Interfaces — Produces:** `v31/sentences.json`:
```json
{ "specVersion": 31,
  "sentences": [ { "id": "S01-001", "part": "01", "section": "5. Members: every one written",
                   "sentence": "Did you mean 'label'?", "audience": "author" } ] }
```
One entry per table row of the design's `refusal-sentences.md` (`~/Projects/worktrees/m39-area-00/docs/customizable-workflow/package-format-v31/refusal-sentences.md`), in order. `section` is the link text of the Section cell; `sentence` is the Sentence cell with the wrapping `*"` … `"*` removed and Markdown escapes undone (`\<` → `<`, `\>` → `>`, `\|` → `|`, `\*` → `*`); a cell holding text after the closing `"*` keeps only the sentence. `scripts/render-sentences.py` prints chapter 11's Markdown table (`| Id | Chapter | Sentence | Audience |`) from the JSON, escaping the same characters.

- [x] **Step 1:** Add check group `sentences`: ids unique, match `^S(0[1-9]|10)-\d{3}$`, ascending within a part; `audience` is `author` or `clinician`; no sentence contains a backslash before `<`, `>` or `|`; the count per part equals the count of that part's rows. Run: fails (file missing).
- [x] **Step 2:** Write a one-off extraction (kept in `/tmp`, not committed) that reads the design table and writes `v31/sentences.json`; read the output for the ten longest and the ten shortest sentences against the source.
- [x] **Step 3:** Write `scripts/render-sentences.py`; round-trip check: rendering and re-parsing the table gives the same sentences (add it to the `sentences` group).
- [x] **Step 4:** `python3 scripts/check-v31.py` exits 0. Commit, push.

### Task 3: package-format — the formats dialect's meta-schema and the nine built-in formats

**Files:** Create `v31/dialect.schema.json`, `v31/formats/index.json`, `v31/formats/{string,number,boolean,date,plaintext,markdown_block,markdown_inline,check_result,judgement}.json`, `v31/schemas/common.schema.json`, `v31/conformance/schemas/dialect/valid-*.json` and `invalid-*.json`; modify `scripts/check-v31.py`.

**Interfaces — Produces:**
- `v31/dialect.schema.json`, `$id` `urn:consultologist:spec:31:dialect`: a 2020-12 meta-schema that admits exactly the keywords of part 02 § 3.1 (`unevaluatedProperties: false` over that list) and enforces what a schema can: `type` one of the seven or a two-element array holding one type plus `"null"`; on an object schema `additionalProperties: false` required and `required` present; `format` only `"date"` and only beside `"type": "string"`; beside `$ref` only `title` and `description`; `oneOf` branches exactly `{const, title}` or `{const: null}`; `enum` with at least two non-null values; `x-markdown` one of `block`, `inline`; `contentMediaType` one of `text/plain`, `text/markdown`. `$defs.format` is a format's root (requires `$schema`); `$defs.inline` is an inline schema (no `$schema`, `$id`, `$defs`, `contentMediaType`, `x-markdown`).
- `v31/schemas/common.schema.json`, `$id` `urn:consultologist:spec:31:schema:common`, `$defs`: `id` (`^[a-z][a-z0-9_]*$`, maxLength 64, not a reserved or Windows id), `label` (string with a non-whitespace character), `description` (null, or a non-empty array of strings), `formatRef` (a format id string, or `{"$ref": "urn:consultologist:spec:31:dialect#/$defs/inline"}`), `textFormat` (enum `plaintext`, `markdown_block`, `markdown_inline`), `sha256` (`^sha256:[0-9a-f]{64}$`), `rootedKey`, `jsonLogic` (structural: a literal, an array of rules, or an object with exactly one member whose name is in `constants.standardOperations` ∪ operations; `var` takes a string; `object` takes an object; every other takes an array), `condition` (`jsonLogic` or null), `referenceString`.
- The built-in formats: `string`, `number`, `boolean` as `{ $schema, $id, type, description }`; `date` with `"format": "date"`; `plaintext`, `markdown_block` (the file of part 02 § 4, byte for byte in content), `markdown_inline` with their annotations (part 02 § 5 table); `check_result` (part 06 § 2.1) and `judgement` (part 02 § 4), each with a `description` on the format and on each property.
- `v31/formats/index.json`: `{ "description": [...], "items": ["string","number","boolean","date","plaintext","markdown_block","markdown_inline","check_result","judgement"] }`.
- Check group `examples`: for each directory `v31/conformance/schemas/<name>/`, every `valid-*.json` validates against the schema named `<name>` (`dialect` → `v31/dialect.schema.json`, otherwise `v31/schemas/<name>.schema.json`) and every `invalid-*.json` does not; `$ref`s by `$id` resolve through a `referencing.Registry` loaded with every schema under `v31/`. An instance file is `{ "about": "<the rule, in words>", "instance": <the JSON> }`. A schema directory with no valid or no invalid example fails. Check group `formats`: every file in `v31/formats/` except `index.json` is listed once in `items`, validates against the dialect, and carries the `$id` of its name; `check_result` (which the dialect cannot express for a nullable array — see Step 3) is handled as Step 3 says.

- [x] **Step 1:** Write the `examples` and `formats` groups; write the dialect examples first. Valid: `markdown_block`, `judgement`, the `verdict` format of part 02 § 5.3, the labelled enum of § 9, a nullable plain enum listing `null`, a nullable reference by `anyOf`. Invalid, one per rule of part 02's "The dialect" and "Enums" refusals that a schema can state: `pattern` (outside the dialect), `additionalProperties: true`, a property left out of `required` is **not** expressible by a meta-schema — leave it to the validator and say so in `v31/conformance/README.md`; a nullable scalar written with `anyOf` likewise; a type array of two non-null types; `format: "date-time"`; a keyword beside `$ref`; a one-value enum; `enum` and `oneOf` together; a `oneOf` branch with an extra member. Run: fails.
- [x] **Step 2:** Write `dialect.schema.json` and `common.schema.json`. Run until the dialect examples pass.
- [x] **Step 3:** Write the nine formats and the index. `check_result`'s `uncovered` and `untested` are written in the design as `{ "type": ["array","null"], "items": … }`, a type array of a non-scalar plus null, which part 02 § 3.2 keeps for scalars; keep the design's shape for `check_result` byte for byte in meaning, have the dialect meta-schema admit `"array"` in a type array, and record it for the PR's *Decisions made during build* in `v31/conformance/README.md` under "Open points found while writing".
- [x] **Step 4:** `python3 scripts/check-v31.py` exits 0. Commit, push.

### Task 4: package-format — schemas of the source file kinds

**Files:** Create under `v31/schemas/`: `manifest`, `index`, `deliverables-index`, `snippet-folder-index`, `macro-folder-index`, `function-index`, `node-agent`, `node-rendered`, `node-computed`, `node-call`, `flow-for`, `flow-while`, `flow-if`, `flow-case`, `check`, `renderer`, `snippet`, `macro`, `deliverable`, `test` (each `<name>.schema.json`); examples under `v31/conformance/schemas/<name>/`.

**Interfaces — Consumes:** `common.schema.json` `$defs`, the `examples` check group. **Produces:** one schema per file kind, `$id` `urn:consultologist:spec:31:schema:<name>`, `title` naming the file (`nodes/<id>.md header, kind agent`), each member with a `description` giving its meaning in one line.

Member lists, exactly (every one in `required`, `additionalProperties: false`):

| Schema | Members | Source |
|---|---|---|
| `manifest` | `name`, `version` (`^v\d{4}\.(0[1-9]|1[0-2])\.[1-9]\d*$`), `specVersion` (const 31), `derivedFrom` (null), `title` (null), `description`, `tags`, `inputs` (keyed by id: `label`, `description`, `format`, `required`, `channel` (null or one of the channels)) | part 01 § 3, part 02 § 8 |
| `index` | `description`, `items` (array of ids, unique) | part 01 § 5.2 |
| `deliverables-index` | `description`, `items`, `delivery` (array of delivery kinds), `bundle` (null or `"zip"`) | part 06 § 8 |
| `snippet-folder-index` | `description`, `items`, `format` | part 01 § 5.3 |
| `macro-folder-index` | `description`, `items`, `if` | part 05 § 8.1 |
| `function-index` | `label`, `description`, `inputs` (keyed: `label`, `description`, `format`), `uses` (`snippets`, `macros`, `formats`, `renderers`), `nodes` (≥1), `ret`, `format` | part 04 § 7.2 |
| `node-agent` | `kind` (const), `producer` (const `generated`), `label`, `description`, `format` (a name only), `reproducible` (const false), `modelDerived` (const true), `runDependent`, `profileCarrying`, `if`, `onInvalid` (`retry`, `fail`), `failIfEmpty` (string or null), `parts` (null, or keyed by id: `if`, `text` (array of strings)), `ordered` | part 01 § 5.2, part 05 § 1.2, § 7.1 |
| `node-rendered` | `kind`, `producer` (const `computed`), `label`, `description`, `format` (a text format), `reproducible` (const true), `modelDerived`, `runDependent`, `profileCarrying`, `if` | part 01 § 5.2 |
| `node-computed` | as `node-rendered` with `format` any, plus `expression` (`jsonLogic`) | part 01 § 5.2, part 03 |
| `node-call` | `kind`, `producer` (const `computed`), `label`, `description`, `format`, `reproducible`, `modelDerived`, `runDependent`, `profileCarrying`, `if`, `function` (id), `with` (object: reference string or null) | part 04 § 7.7 |
| `flow-for` | the eight of part 04 § 1 (`kind`, `label`, `description`, `format`, `reproducible`, `producer`, `modelDerived`, `runDependent`, `profileCarrying` — nine names; the design counts eight, see Step 3) plus `in`, `parallel`, `maxParallel` (null or 2–15), `until`, `max` (null or ≥1), `itemLabel`, `nodes` (≥1), `ret` | part 04 §§ 1, 2.1 |
| `flow-while` | § 1's plus `while`, `max` (1–15), `until`, `checkFirst`, `nodes`, `ret` | part 04 § 3.1 |
| `flow-if` | § 1's plus `if` (not null), `then` (`{nodes ≥1, ret}`), `else` (body or null) | part 04 § 5.1 |
| `flow-case` | § 1's plus `case`, `branches` (≥1 of `{when ≥1 strings, nodes, ret}`, where `nodes: []` goes with `ret: null` and only then), `else` | part 04 § 5.2 |
| `check` | `kind` (const `computed`), `producer` (const `computed`), `label`, `description`, `format` (const `check_result`), `reproducible` (const true), `modelDerived`, `runDependent`, `profileCarrying`, `if`, `expression` | part 06 § 1.1 |
| `renderer` | `label`, `kind` (const `rendered`), `description`, `input`, `format` (a text format) | part 05 § 5.1 |
| `snippet` | `label`, `description`, `if`, `format` (a text format) | part 05 § 6.1 |
| `macro` | `label`, `description`, `if`, `format` (a text format), `instruction` (array of strings, ≥1) | part 05 § 8.1 |
| `deliverable` | `label`, `kind` (const `rendered`), `description`, `format` (a name), `if`, `check` (null or `^check\.`), `whenEmpty` | part 06 § 6.1 |
| `test` | `description`, `inputs`, `profile`, `run`, `outputs`, `expect` (members of part 07 § 8.3, each optional, no others) | part 07 § 8 |

- [x] **Step 1: Examples first.** For each schema: every instance the design prints for that kind as `valid-<package>-<id>.json` (the manifest of part 01 § 3; `nodes/index.json`; the loops of part 04 § 2.8; `refine`; `route`; `narrative_only`; `applies`; `draft_section`'s `index.json`; `section.md`'s call; `section_instructions` of part 05 § 7.2 with its elided members written out; `coverage.md`; `concept_bullets`; `letrozole_prescription_text`; the two deliverables of part 06 § 6.5; the delivery folders of § 8; `two_sections.json` with its hash written as 64 zeros), and at least: `invalid-missing-<member>.json` (one member removed), `invalid-unknown-member.json`, and one per kind-specific rule in the table (`maxParallel` 1, `max` 0 and 16 on a while, an empty branch with a `ret`, `whenEmpty` `"drop"`, `producer` `"grounded"`, an empty `description` array, a blank `label`, a reserved id in `items`, `var` written as an array). Run: fails.
- [x] **Step 2:** Write the twenty schemas. Run until green.
- [x] **Step 3:** The design's own examples disagree with its member tables in places (an agent header example without `if`; "eight members" for nine names; `call` examples marked "plus part 01's remaining leaf members"). The schema follows the **member tables**; a printed example that omits a member is completed in its `valid-` instance, and each such place is listed in `v31/conformance/README.md` under "Open points found while writing", with the part and section.
- [x] **Step 4:** `python3 scripts/check-v31.py` exits 0. Commit, push.

### Task 5: package-format — schemas of the compiled documents and the registry's own files

**Files:** Create under `v31/schemas/`: `resolve`, `lock`, `publish-report`, `migration-report`, `bounds`, `constants`, `profile`, `sentences`, `bindings`, `agents`, `conformance-index`; examples for each; modify `scripts/check-v31.py`.

**Interfaces — Produces:**
- `resolve`: the top-level members of part 07 § 2.2, every one required; `inputs`, `formats` (`{"$defs": {…}}`), `formatHashes`, `typeHashes`, `bindings` (format → producer → `{agentName, agentVersion, via}` with `via` `format` or `custom`); `nodes`, `checks`, `renderers`, `snippets`, `macros`, `deliverables` as maps keyed by rooted key (`propertyNames` pattern per root) whose entries require the derived members of § 2.5 that apply to every entry (`id`, `parent`, `reads`, `runDependent`, `profileCarrying`, `modelDerived`) and leave the header members open (`additionalProperties: true` on entries: the header's shape is the source schemas', applied by the resolver); `functions`; `graph` (`edges` with `kind` from `constants.edgeKinds`, `order`); `run`; `warnings` (`{sentence, at}`); `requires` (§ 3.1) and `needs` (§ 3.2) closed.
- `lock`: § 4.1's members, closed; `libraries` requires the four names; `culture` const `invariant`.
- `publish-report`: § 9, closed, with the test-result shape.
- `migration-report`: part 10 § 3.2: `oldVersion`, `newVersion`, `handEdits` (array of strings), `texts` (array of `{site, oldHash, newHash, equal, expected, explanation}`), closed. The design names these four things and fixes only the entry's members; the three top-level names are this plan's (recorded as a decision).
- `bounds`, `constants`, `profile`, `sentences`: the files of Tasks 1–2, closed. `bindings`: part 02 § 7's shape (`version`, `formats`, `custom`), each binding `{agentName, agentVersion}` closed. `agents`: `{ "version": string, "agents": { "<name>": { "<version>": { "strictRejects": [string] } } } }`. `conformance-index`: Task 8's index.
- Check group `self`: `bounds.json`, `constants.json`, `profile.json`, `sentences.json`, `conformance/index.json` validate against their schemas.

- [x] **Step 1:** Examples first. `lock/valid-example-nested-note.json` is part 07 § 4.1's example with a full 64-hex hash; `publish-report/valid-*.json` one published and one refused; `resolve/valid-minimal.json` a hand-written smallest package (one input, one agent node, one deliverable, `{}` elsewhere) with every hash 64 zeros, its `about` saying "an instance for the schema; not compiled output"; invalid: a missing top-level member each, `via: "producer"`, a timestamp member added to `lock`, an unrooted key in `nodes`, `outcome: "ok"`. Run: fails.
- [x] **Step 2:** Write the schemas; add the `self` group. Run until green.
- [x] **Step 3:** Commit, push.

### Task 6: the built-in renderers, proven against the C# renderers

**Files:**
- Create (package-format): `v31/renderers/index.json`, `v31/renderers/concept_bullets.md`, `v31/renderers/concept_context.md`
- Create (engine worktree): `tests/V31BuiltInRenderersTests.cs`
- Modify (engine worktree): `external/consultologist-package-format` (the pin)

**Interfaces — Produces:** two renderer files in part 05 § 5.1's shape (front matter `{ "label", "kind": "rendered", "description": null, "input": "concept_list", "format": "markdown_block" }`, then the template reading only `value`); `index.json` `{ "description": […], "items": ["concept_bullets", "concept_context"] }`. Check group `renderers` in `check-v31.py`: each file starts with `---`, its header validates against `renderer.schema.json`, its id is listed, its body is non-empty and contains no `renderer.`.

- [x] **Step 1: The failing test.** `V31BuiltInRenderersTests` (namespace `Consultologist.Api.Tests`) finds the repo root as `SpecVersionSetTests` does, reads `external/consultologist-package-format/v31/renderers/<id>.md`, asserts the file exists with the message "… is missing — the submodule is not checked out (git submodule update --init).", takes the body after the second `---` line, and renders it with Scriban 7.2.5 (`Template.Parse`, a `ScriptObject` holding `value`, the invariant culture, `MemberRenamer = member => member.Name` so the template reads the JSON names). `[Theory]` `ConceptBullets_MatchesTheCSharpRenderer_ByteForByte` and `ConceptContext_…` over these fictional concept lists: empty; one active SNOMED concept; one not SNOMED (`id` null); one inactive; with and without `support`; `support` whitespace only; three mixed; a term holding Markdown characters. Expected value: the existing C# renderer's output for the same list (`ConceptRenderers` in `src/Consultologist.PackageFormat/ConceptRenderers.cs`, reached the way `WorkflowConceptRenderers` reaches it). `value` is the format's value as the 31 design pipes it (`node.x.value`): if the C# renderer takes the concepts array, bind `value` to what a `concept_list` value is (the object with `concepts`) and have the template read `value.concepts`.
  Run: `dotnet test tests/Consultologist.Api.Tests.csproj --filter V31BuiltInRenderersTests` — FAIL, file missing.
- [x] **Step 2:** Write the two templates in the dialect of part 05 § 2 (only `if`, `for`, output, `~`; no assignment, no arithmetic, no `-` trim, no built-in outside the seven). Iterate in the content repo working tree by pointing the test at it through the submodule working copy (edit `external/consultologist-package-format/v31/renderers/` in place while iterating, then copy the final bytes to `~/Projects/consultologist-package-format`, commit and push there, and `git checkout` the pushed sha in the submodule so it holds no local edits).
- [x] **Step 3:** If a byte cannot be matched inside the dialect (booleans printing `True`, a trailing newline), stop and report it with the smallest failing case: it is a finding for the area issue, not something to paper over with a construct the dialect refuses.
- [x] **Step 4:** Test passes; `python3 scripts/check-v31.py` exits 0 in package-format; `git -C external/consultologist-package-format status --short` is empty.
- [x] **Step 5:** Commit and push package-format; move the pin; commit (`Tests (#1497): the built-in renderers at 31 match the C# renderers byte for byte`) and push the worktree.

### Task 7: the normative text, chapters 1–4

**Files:** Create `package-format-v31.md` (package-format); modify `scripts/check-v31.py`.

**Interfaces — Produces:** `package-format-v31.md` beginning:

```markdown
# Workflow Package Format — specVersion 31 (a folder of typed files, compiled)

*specVersion 31 — preview. Published as part of `package-format@vYYYY.MM.N`; see `spec-versions.json`. 31 is listed under `preview` until the engine accepts it.*
```

then "How to read this document" (MUST-level words are not used; every rule is a statement, and every refusal has its sentence; `<…>` marks a placeholder; the JSON files beside the text — `v31/bounds.json`, `v31/constants.json`, `v31/schemas/` — are normative where the text names them), "What changes from 30" (index § 2's table), then chapters as `## 1. Files and scopes` … with the design's section numbers kept beneath (`### 1.2 …` for part 01 § 2) so `sentences.json`'s `section` values can be found. Chapters 1–4 restate parts 01–04 in full as rules.

**The transformation, for every chapter task (7–10):**
- Keep every rule, table, member list, example and refusal sentence. Keep the design's wording where it states a rule.
- Drop the decision trail: `([decided](…))` links, "Proposed in this part", revision notes, issue numbers used as authority. An issue number stays only where it names later work a reader may meet (`#1468`), written as a full pinned-free URL `https://github.com/Consultologist/Consultologist-Blazor/issues/<n>`.
- Cross-references become in-document anchors (`[chapter 3 § 5.1](#51-standard-json-logic-operations)`) or relative links to files in this repo (`v31/bounds.json`).
- "At 30 …" stays only in the "What changes from 30" tables and chapter 10 (migration).
- Each schema is linked where its file kind is defined.

- [x] **Step 1:** Add check group `document`: the file exists, contains "specVersion 31", has `## N.` headings for the chapters written so far in order, every relative link target exists, no link contains `/blob/main/` or `/tree/main/`, every in-document anchor resolves to a heading (GitHub's slug rule), and every sentence of `v31/sentences.json` whose `part` is a chapter already written appears in the document verbatim (compare after undoing Markdown escapes and emphasis). Run: fails.
- [x] **Step 2:** Write the front matter and chapters 1–4 from parts 01–04 (read each part in full before writing its chapter).
- [x] **Step 3:** Check passes for parts 01–04. Commit per chapter, push.

### Task 8: the normative text, chapters 5–6, and the Scriban dialect document

**Files:** Modify `package-format-v31.md`; create `v31/scriban-dialect.md`.

**Interfaces — Produces:** chapters `## 5. Templates and macros` (part 05) and `## 6. Checks, deliverables and the profile` (part 06). `v31/scriban-dialect.md`: "The Consultologist Scriban dialect, version 1.0.0 (`scriban-dialect@1.0.0`)": the engine (Scriban 7.2.5, the invariant culture), the allowed statements, the refused ones, the built-ins with signatures and null behaviour, the limits, the placement and whitespace forms, the body-`if` rule, the marker, each with its refusal sentence ids (R-D1 … as the design labels them, beside the `Sxx-nnn` id), and a closing table "Feature → allowed or refused → sentence". Chapter 5 states the same rules in full and links the dialect document as their versioned form.

- [x] **Step 1:** Extend `document`'s chapter list to 6 and add: `v31/scriban-dialect.md` exists, names `scriban-dialect@1.0.0`, and its links resolve. Run: fails.
- [x] **Step 2:** Write chapter 5, the dialect document, chapter 6.
- [x] **Step 3:** Check passes. Commit per file, push.

### Task 9: the normative text, chapter 7 (the compiled documents)

**Files:** Modify `package-format-v31.md`.

**Interfaces — Produces:** `## 7. The compiled documents` restating part 07 in full: both documents and their common rules, `resolve.json` member by member, `requires` and `needs`, `lock.json`, versioning of operations and machinery (naming `v31/constants.json` as where each version is declared), the corpus re-render and break points, the publish pipeline's steps and write order, where the files live, evidence and views, in-package tests, the publish report, the sentences. Links `v31/schemas/resolve.schema.json`, `lock.schema.json`, `publish-report.schema.json`, `test.schema.json`.

- [x] **Step 1:** Extend the chapter list to 7. **Step 2:** Write. **Step 3:** Check passes; commit, push.

### Task 10: the normative text, chapters 8–11, and the conformance frame

**Files:** Modify `package-format-v31.md`, `scripts/check-v31.py`; create `v31/conformance/README.md`, `v31/conformance/index.json`.

**Interfaces — Produces:**
- `## 8. What an engine does with a package`: part 09 §§ 1 and 3 as an engine's obligations (the two load paths, the load step by step, admitting versions and the preview list, scheduling, the per-job ceiling and dispatch order, retries and limits with their setting names and starting values, the publish note, the drain, where the record is written, delivery at the end) and § 9's sentences. The pages (Create, History, Profile, the Editor), `WorkflowPackages/Current` and the diagram are the app's, and the chapter says so in one paragraph, naming `dag.mmd` as a view with its Mermaid id rule (part 01 § 9.3).
- `## 9. The record` : one page: what a 31 job's record pins (`resolveHash`, `lockHash`, rooted keys, the sub-row id forms), and that its contract is `consultologist-provenance`'s storage 12; part 08's recorded sentences.
- `## 10. Migration (30 → 31)`: part 10 §§ 1.2–1.4, 1.8–1.10, 2.2–2.3, 3.2's report shape, § 4, and its sentences.
- `## 11. Every sentence`: the table printed by `scripts/render-sentences.py`.
- `## 12. Conformance`: part 10 § 5's list, each item with where it lives under `v31/` and **which build area exports it**: schemas, constants and sentences (here, now); the valid and invalid package for every sentence (areas 02–08, each for its parts' sentences); operation, printer, null, text, number and date vectors (area 04); Markdown escape vectors and built-in renderer goldens (areas 07, 08); compiled-document fixtures (areas 05, 11); migration vectors (area 10).
- `v31/conformance/index.json`: `{ "specVersion": 31, "families": [ { "id", "path", "about", "exportedBy", "cases": [] } ] }` with one family per item of § 5 (`schemas` has `"cases"` listing the example files of Tasks 3–5; the others are empty until their area exports them). `v31/conformance/README.md`: the layout, what a case file of each family will look like at the level part 10 § 5 fixes (a package folder plus `expect.json` with `valid` and the sentences), the rule that expectations are exported by the library's generators and never hand-written, and "Open points found while writing" collected from Tasks 3–5.

- [x] **Step 1:** Extend `document` to chapters 8–12 and every sentence of every part; add: chapter 11's table equals `render-sentences.py`'s output; `conformance/index.json`'s `schemas` cases equal the example files on disk. Run: fails.
- [x] **Step 2:** Write the chapters and the two conformance files.
- [x] **Step 3:** `python3 scripts/check-v31.py` exits 0. Also run the three structural checks of `.github/workflows/validate.yml` by hand (copy each embedded Python block to `/tmp` and run it from the repo root): all pass unchanged. Commit, push.

### Task 11: agents — the catalogue at 31

**Files** (in `~/Projects/consultologist-agents`): create `agents/v31/README.md`, `agents/v31/bindings.json`, `agents/v31/agents.json`, `agents/v31/formats/index.json`, `agents/v31/formats/classification.json`, `agents/v31/formats/concept_list.json`, `agents/judgement.yaml`, `scripts/check-v31.py`; modify `README.md`.

**Interfaces — Produces:**
- `agents/v31/formats/concept_list.json`: `{ "$schema": "urn:consultologist:spec:31:dialect", "$id": "urn:consultologist:format:concept_list@v2026.12.1", "type": "object", "description": <30's description>, "required": ["concepts"], "additionalProperties": false, "properties": { "concepts": … } }` — the shape of `agents/formats/concept_list.json`'s `schema`, unchanged (every property is already in `required`).
- `agents/v31/formats/classification.json`: the object `{ value: string }` (`required: ["value"]`), with `$schema`, `$id` and a description: today's shape, unchanged (part 02 § 4).
- `agents/v31/formats/index.json`: `{ "description": […], "items": ["classification", "concept_list"] }`.
- `agents/v31/bindings.json`:
  ```json
  { "version": "v2026.12.1",
    "formats": {
      "plaintext":       { "generated": { "agentName": "test-json", "agentVersion": 48 } },
      "markdown_block":  { "generated": { "agentName": "test-json", "agentVersion": 48 } },
      "markdown_inline": { "generated": { "agentName": "test-json", "agentVersion": 48 } },
      "judgement":       { "generated": { "agentName": "judgement", "agentVersion": 1 } },
      "classification":  { "generated": { "agentName": "classification", "agentVersion": 1 } },
      "concept_list":    { "generated": { "agentName": "concept-extraction", "agentVersion": 1 } } },
    "custom": { "generated": { "agentName": "custom-json", "agentVersion": 1 } } }
  ```
  (the agents and versions `agents/formats.json` binds today: `prose` → `test-json@48`, and so on).
- `agents/v31/agents.json`: `{ "version": "v2026.12.1", "agents": { "<name>": { "<version>": { "strictRejects": ["format"] } } } }` for the five agent versions bound above. `strictRejects` is the list part 02 § 3.5 step 5 takes from the attested definition; it sits beside the definitions because an existing `agents/<name>.yaml` cannot change without a new Foundry version.
- `agents/judgement.yaml`: `name: judgement`, `version: "1"`, `status: active`, `definition` with the same `kind`, `model` and `reasoning` as `agents/classification.yaml`, instructions for answering one yes-or-no question about the text given with `answer` and a one-or-two-sentence `reason` drawn from that text, and `text.format` `json_schema`, `name: judgement`, `strict: true`, `schema` the built-in `judgement` as a model receives it (part 02 § 3.5: `$schema`, `$id`, `title` removed, `reason` as `{ "type": "string", "description": … }`). Header comment in the file's established style, saying it is published by the release, not by this branch.
- `scripts/check-v31.py`: each format file is listed once in the index and carries `$schema` and the `$id` of its name and the bindings' `version`; every bound format is a file in `agents/v31/formats/` or one of the nine built-in names; every bound agent has an `agents/<name>.yaml` whose `version` equals the binding's; every bound agent version has an `agents.json` entry; `judgement.yaml`'s embedded schema's property names are `answer`, `reason`; the 30 files are byte-identical to `origin/main` (`git diff --quiet origin/main -- agents/formats.json agents/formats agents/output-contracts.json agents/schemas agents/classification.yaml agents/concept-extraction.yaml agents/custom-json.yaml agents/test-json.yaml`).

- [x] **Step 1:** `git fetch origin && git checkout -b m39-build origin/main`. Write `scripts/check-v31.py`; run: fails.
- [x] **Step 2:** Write the files. `agents/v31/README.md` says: what the directory is, why it sits beside the catalogue the engine at 30 reads instead of replacing it (the design's "replacing `agents/formats.json`" happens when 30 retires), that the `prose`, `rendered` and `terms_subset` formats have no 31 file (they are the registry's `plaintext`, `kind: rendered` and an operation), and that the version `v2026.12.1` is the design's example, set for real at release.
- [x] **Step 3:** `python3 scripts/check-v31.py` exits 0. Run the first job steps of `.github/workflows/validate.yml` that need no checkout of the engine by hand (the yaml and index checks): pass.
- [x] **Step 4:** Commit (`Agents catalogue (Consultologist-Blazor#1497): the catalogue at specVersion 31 — pure-shape formats, bindings by format and producer, the judgement agent`), push.

### Task 12: provenance — record storage 12

**Files** (in `~/Projects/consultologist-provenance`): modify `provenance-versions.json` (add `preview` only), `provenance-record.md`, `hash-definitions.md`, `registry-layout.md`, `README.md`; create `scripts/check-v31.py`.

**Interfaces — Produces:**
- `provenance-versions.json` gains `"preview": { "recordStorageVersions": [12], "effectiveInputHashVersions": [7], "workflowOutputHashVersions": [4], "nodeHashVersions": [7] }`; nothing else changes.
- `provenance-record.md` gains `## 6. Storage 12 (specVersion 31, preview)`: part 08 §§ 1–12 restated as the contract, by the transformation of Task 7: where the record is stored and sealed; keys, row keys and sub-row ids; the job header's members; input origins' extraction versions; node rows, the agent, renderer sub-rows, macro rows; decisions and their sites; check rows; call rows; run facts and run spots; profile reads; deliverable rows and delivery rows; retention (including that a short value's hash can be reversed by hashing likely candidates); what the record never holds; the recorded sentences; and the statement "Storage 2–11 records read as before; nothing is converted." Every member with its type and null rule, in tables. The example of § 14.
- `hash-definitions.md` gains `## 7. Definitions specVersion 31 adds (preview)`: per-node definition 7, effective-input definition 7, workflow-output definition 4, each as a prose definition at the precision part 08 § 13 gives, written as paragraphs and bullet lists (**no table row beginning `| **7** |` or `| **4** |`** — those rows join their tables when the ladder moves, with worked examples computed by the engine); `resolveHash` and `lockHash` (SHA-256 over the RFC 8785 bytes, written `sha256:<64 lowercase hex>`); `sourceHash` (part 07 § 2.6); `bodyHash`; the type hash, the full format hash (which leaves out `$id`) and the sent-format hash (part 02 §§ 2.3, 3.5); `recordHash`; the note on short values.
- `registry-layout.md` gains `## 10. A specVersion 31 version folder (preview)`: part 07 § 6's folder, the write order and the "published" stamp of § 5, the media types.
- `scripts/check-v31.py`: the four ladders, `version` and `documents` equal `origin/main`'s; `preview` is exactly the object above; the record document still contains "storage versions 2, 6, 7, 8, 9, 10 and 11"; no line of `hash-definitions.md` added since `origin/main` starts with one of the six pinned prefixes or with `| **`; every member name in part 08's tables (listed in the script) appears in section 6 in backticks; links resolve and none point at `main`.

- [x] **Step 1:** Branch; write the check; run: fails.
- [x] **Step 2:** Write the sections.
- [x] **Step 3:** Check passes; the embedded Python blocks of `.github/workflows/validate.yml`, run by hand from `/tmp` copies, pass unchanged. Commit per document, push.

### Task 13: the engine repo — the pins and the tests that hold them

**Files** (worktree): modify `external/consultologist-package-format`, `external/consultologist-agents`, `external/consultologist-provenance`; create `tests/V31ContractsTests.cs`.

**Interfaces — Consumes:** every file above, at the pushed heads.

`V31ContractsTests` (namespace `Consultologist.Api.Tests`; root found as `SpecVersionSetTests` finds it; a missing file fails with the "submodule is not checked out" message):

```csharp
[Fact] public void TheEngineAt30_StillSeesItsOwnSets()
// spec-versions.json: 31 not in supported, "31" not in documents; preview["31"].document == "package-format-v31.md";
// the file exists and contains "specVersion 31". provenance-versions.json: 12 not in recordStorageVersions;
// preview.recordStorageVersions == [12], nodeHashVersions == [7], effectiveInputHashVersions == [7], workflowOutputHashVersions == [4].
// Neither WorkflowPackageStore.SupportedSpecVersions nor WorkflowPackageValidator.AcceptedSpecVersions contains 31.

[Fact] public void Bounds_AreTheDesignsNumbers()
// v31/bounds.json: files 512, fileBytes 262144, totalBytes 2097152, pathLength 200, idLength 64, jsonDepth 32,
// expressionNodes 1000; passes 15, depth 15, parallel 15; 22 windowsReservedIds; 36 reservedIds including "parts" and "value".

[Fact] public void EverySentenceOfTheDesign_IsPublishedWordForWord()
// parses docs/customizable-workflow/package-format-v31/refusal-sentences.md's table rows (| Sxx-nnn | … |) with the
// unescaping rule of Task 2 and asserts the (id, sentence, audience) list equals v31/sentences.json's, in order.

[Fact] public void EveryFileKind_HasItsSchema()
// each of the names of Tasks 4 and 5 exists as v31/schemas/<name>.schema.json, parses, and carries
// "$id": "urn:consultologist:spec:31:schema:<name>".

[Fact] public void BuiltInFormats_AreTheNine_InTheDialect()
// v31/formats/index.json items == the nine, each file's $schema is the dialect and $id urn:consultologist:spec:31:format:<id>;
// judgement requires ["answer","reason"]; check_result requires the six members.

[Fact] public void EveryBinding_NamesAFormatAndAnAttestedAgentVersion()
// agents/v31/bindings.json: each format key is a built-in or a file in agents/v31/formats; each agentName has
// external/consultologist-agents/agents/<name>.yaml whose version equals agentVersion; via is never written here.

[Fact] public void The30Catalogue_IsUntouchedBesideThe31One()
// agents/formats.json still names exactly classification, concept_list, prose, rendered, terms_subset.
```

- [x] **Step 1:** Write the tests; run `dotnet test tests/Consultologist.Api.Tests.csproj --filter V31ContractsTests` before the agents and provenance pins move: FAIL (files missing).
- [x] **Step 2:** Move the three pins to the pushed heads of `m39-build`.
- [x] **Step 3:** The filter passes. Then `dotnet build Consultologist.sln` and `dotnet test Consultologist.sln`: both green, output read.
- [x] **Step 4:** Commit the pins (`PackageFormat (#1497): the contracts at specVersion 31, pinned as preview`) and the tests (`Tests (#1497): the 31 contracts are published as preview, word for word, and the engine at 30 sees its own sets`) separately; push.

---

## Spec coverage

| Requirement | Task |
|---|---|
| index § 5 step 1, part 10 § 1.6: the spec text | 7–10 |
| its schemas, `resolve.json`'s and `lock.json`'s included (part 10 § 5) | 3, 4, 5 |
| `bounds.json`, the reserved ids (part 01 §§ 7, 8) | 1 |
| the dialect's meta-schema, the built-in formats (part 02 §§ 3, 4, 6) | 3 |
| the published constants: producers, operation versions, the Markdig extension list (part 10 § 5, part 07 § 4.3) | 1 |
| the built-in renderers in the registry (part 05 § 5.3) | 6 |
| the Scriban dialect as a published, versioned spec (part 05 § 2) | 8 |
| the profile's closed list and preview values (part 06 § 10) | 1 |
| every refusal sentence pinned (index § 7) | 2, 10, 13 |
| the conformance suite (part 10 § 5) | 10 (frame, schemas, constants); cases and vectors with the areas whose code exports them |
| the agents catalogue: formats as pure shape, `$schema`, `$id`, `formats/index.json`, `bindings.json` by format and producer, the rejected-keywords list, new agent versions (part 02 § 7, part 10 §§ 1.6, 1.8) | 11 |
| the provenance contract: storage 12, the hash definitions, the registry layout (part 08 § 13) | 12 |
| the engine's submodule pins moved (part 10 § 1.6) | 6, 13 |

## Decisions this plan takes (for the PR's *Decisions made during build*)

1. **The contracts are additive and named `preview`.** Index § 5 step 1 releases the contracts first; the runbook keeps 31 outside the accepted versions; this repo's tests hold the published `supported`, `recordStorageVersions` and catalogues equal to the engine's. So 31 sits under a `preview` member and in parallel paths, and joins `supported` and the ladders at the bump.
2. **The 31 agents catalogue is `agents/v31/`,** not `agents/formats/` and `agents/bindings.json`: two of its format files would take the names of files the 30 reader reads strictly. The design's paths apply when 30 retires (#1450).
3. **`strictRejects` sits in `agents/v31/agents.json`,** beside the attested definitions, since an existing definition file cannot change without a new Foundry version.
4. **No existing agent gets a new version:** no bound agent's embedded schema changes at 31 (`concept_list` and `classification` keep their shapes). The one new attested agent is `judgement`, version 1.
5. **Golden vectors and conformance cases are exported by the areas that build the code;** this area publishes the frame, the schemas, the constants and the sentences.
6. **Catalogue version `v2026.12.1`** in `$id`s and `bindings.json` is the design's example; the release sets the real one.
7. **Content repos' CI is not changed** (`.github/` is the user's): each repo gets `scripts/check-v31.py`, to be wired into `validate.yml` at release.
8. **`migration-report.json`'s top-level names** (`oldVersion`, `newVersion`, `handEdits`, `texts`) are this plan's; part 10 § 3.2 fixes only the entry.
