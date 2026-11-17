# 10. Migration and tools

This part covers how every package reaches 31 and the tools that ship with the version: the
migration from 30 (#1447), summarised here with its step-by-step detail kept on the issue; the
condition converter's logic, which ships in the Milestone 39 library (#1423); the byte-equivalence
harness and its fixtures; the snapshot test the migration writes (#1449); the conformance suite;
the author documentation to rewrite; the satellites' work; and what moved to Milestones 40 and 41.
The engine's preview setting and dev registry are in [part 09](09-engine-and-pages.md#2-the-dev-preview-setting-and-devs-registry).

## 1. The migration in summary

The detail, and every later correction, is on #1447. In one paragraph: **one library rewrite from
30, chained from older versions; byte-equivalence reported, never a gate; account packages
migrated and published by the operator, with hand edits made by the operator, pins moved always,
and owners told before and after.**

### 1.1 The tool

- **`WorkflowPackageMigration30To31`** in `Consultologist.PackageFormat`, beside the 24 → 25,
  25 → 26 and 27 → 28 rewriters. **It reads specVersion 30;** an older package is first raised
  through the existing chain in the same run
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1447),
  [class name](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6957680891)).
- **Callers in Milestone 39:**
  - `scripts/migrate-workflow-package.cs --to 31`, for the public repository;
  - the operator's bulk run over account packages (§ 1.4);
  - the byte-equivalence harness (§ 3).

  #1447 also names the Editor as a caller. The Editor can't show or edit 31 packages as editable
  until Milestone 41, so **the Editor's "upgrade to 31" arrives with Milestone 41: Proposed.**
- **Output:** the migrated package as a folder (part 01's layout), a list of notices, and a
  snapshot test (§ 4). The shape of the result matches the earlier rewriters', so every caller
  treats every rung alike.

### 1.2 What the rewrite does mechanically

| At 30 | At 31 | Part |
|---|---|---|
| one `manifest.json` with every node, prompts as files | files and folders: `nodes/`, flow folders with `index.json`, `checks/`, `deliverables/`, `renderers/`, `snippets/`, `macros/`, `formats/`, `tests/` | [01](01-files-and-scopes.md) |
| optional members omitted | every member written, `null` for none | [01](01-files-and-scopes.md) |
| `format` with `producer` | `kind` and `format` on every node; `prose` → `agent_plaintext`; `x-generated` → `x-agent` | [02](02-types-and-inputs.md) |
| an object format (catalogue or package) whose `required` lists only some properties | every property listed in `required`, the optional ones made nullable (§ 1.8) | [02](02-types-and-inputs.md) |
| a node's `when` gate | `if` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6959885064)); a `case` branch keeps `when` | [03](03-expressions.md) |
| inputs as an array with `type`, `values`, `fields`, `items`, `expectedContent` | an object keyed by id: `text` → `markdown_block`; `enum` + `values` → `oneOf`; `date` → `"date"`; `fields` → `properties` + `required` listing every field, optional ones made nullable; `expectedContent` → `channel`; `description: null` added ([grammar](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6919612460)) | [02](02-types-and-inputs.md) |
| `labelField` on an input | `itemLabel` on **each loop** that iterates the input (several loops get the same value) | [04](04-loops-and-functions.md) |
| string conditions (Scriban) | JSON Logic, by the converter (§ 2); `==`/`!=` written `===`/`!==` | [03](03-expressions.md) |
| `let` (a Scriban expression) | `kind: computed` with JSON Logic in `expression`, by the converter | [03](03-expressions.md) |
| a `terms_subset` check node | a file in `checks/`, `kind: computed`, `format: check_result`, `failWith` a literal; every reader of `node.x` rewritten to `check.x` | [06](06-checks-deliverables-profile.md) |
| deliverables with `sources`, composed by the engine | `deliverables/<id>.md` whose generated template **reproduces today's composer bytes** | [06](06-checks-deliverables-profile.md) |
| the deliverable header's signature | `{{ profile.signature }}` in the template | [06](06-checks-deliverables-profile.md) |
| built-in renderers in C# (`concept_bullets`, `concept_context`) | package templates in `renderers/`, piped as `\| renderer.x` | [05](05-templates-and-macros.md) |
| whitespace forms #1412 refuses | rewritten to the accepted forms | [05](05-templates-and-macros.md) |
| simple `{{ if }}…{{ end }}` blocks in prompts | lifted into parts | [05](05-templates-and-macros.md) |
| optional macros: per-run choices with a `default` | **optional boolean inputs** (§ 1.3) | [05](05-templates-and-macros.md) |
| no tests | one snapshot test (§ 4) | [07](07-compiled-documents.md) |

### 1.3 Per-run macro choices become boolean inputs

Decided on [#1447](https://github.com/Consultologist/Consultologist-Blazor/issues/1447#issuecomment-6957669427)
and [#1385](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6957666238):
inputs have no `default` member, so each optional macro at 30 becomes an optional boolean input,
and the macro's offer condition reads it so that a run that doesn't answer gets 30's default:

| 30's `default` | The macro's `if` at 31 | Unanswered (`null`) means |
|---|---|---|
| `false` | `{ "===": [ { "var": "input.<id>" }, true ] }` | not offered |
| `true` | `{ "!==": [ { "var": "input.<id>" }, false ] }` | offered |

When 30's macro already had an `if`, the two are joined with `and`. The new input is written:

```jsonc
"include_billing_note": {            // Proposed: the input id is the macro's id
  "label": "Include the billing note",  // the macro's label
  "description": null,
  "format": "boolean",
  "required": false,
  "channel": null
}
```

**Proposed:** the input takes the macro's id and label; an id that collides with an existing
input gets the suffix `_macro`. Re-evaluating a `default` member is #1456 (Milestone 40).

### 1.4 Hand edits, with notices

When the rewrite can't decide, it still writes the package, with **one notice per edit**, and
validation fails with sentences until each is made, as the 25 migration did. The cases:
- a body `if` too complex to lift into a part;
- macros placed on deliverables with conditions or model judgement (#1406);
- a condition or `let` the converter can't map (§ 2.3).

The script exits **1** with *"written; hand edits remain"*. **Proposed:** it exits 0 when the
package validates, and 2 when it can't write a package at all (unreadable input, or a version
below 5).

Each notice reads (**Proposed** wording):
> *"Hand edit needed in <file>, <member>: <reason>."*

### 1.5 Account packages, by the operator

- **The operator runs the migration** over every account package and **publishes the migrated
  version as a new version of each owner's package,** making any hand edits, so every package
  migrates.
- **Pins move always:** each account pin moves to the migrated version at publish.
- **Recorded as a migration:** the version's publish record says *"migrated by the operator from
  <old version>"*, with the byte-equivalence report and the list of hand edits attached. The
  Editor and History show it ([part 09](09-engine-and-pages.md#62-the-job-header)).
- **Owners are told** (the standing "tell the user early" rule): before the bulk run, a visible
  notice says their packages will be migrated, their pins moved, and that editing returns with
  Milestone 41; after it, each owner sees what changed, the report and the hand edits.
- **No backfill:** old versions stay readable and old records keep their meaning; old specs keep
  their load path until retirement (#1450).

### 1.6 The order

Index [§ 5](index.md#5-phasing) gives the whole rollout. The migration's part of it:

0. **The content repositories release first**
   ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1447#issuecomment-6924905094)),
   each one PR and its tags, with the engine's submodule pins moved to them:
   - `consultologist-package-format`: the spec text, its schemas (including `resolve.json`'s and
     `lock.json`'s) and the conformance suite (§ 5);
   - `consultologist-agents`: the migrated formats catalogue (`prose.json` → `agent_plaintext.json`,
     `formats/index.json`, `agents/bindings.json`, a `$id` on every format, `producer` removed,
     every property listed in `required` with the optional ones nullable, each rewritten format
     released as a new version, § 1.8),
     and **new Foundry versions of every attested agent whose embedded schema changes**, with the
     catalogue pins moved;
   - `consultologist-provenance`: the new record contract (storage 12).
1. **Dry run on dev:** the build behind `WorkflowPackages__PreviewSpecVersions=31` on
   `canada-east-ai-function-dev`, with dev's own registry in `consultdevcaeast`: the example
   packages migrated and smoke-run there, and the bulk migration run over copies of account
   packages, all with fictional inputs
   ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1447#issuecomment-6957669427)).
   **Proposed:** the copies are the account packages' sources only (they hold no patient text),
   published to dev's registry under the same owners' ids, and deleted after the dry run.
2. **The satellites** are released and verified against migrated public packages (§ 7).
3. **The engine accepts 31 beside 30,** released after a drain, as with 29 and 30.
4. **The public example packages** migrate in one PR in `consultologist-workflows`, tagged one per
   push, with a live smoke run each.
5. **The production and operator pins move,** on the package author's word.
6. **Account packages:** the notice to owners, the operator's bulk run (migrate, hand edits,
   publish, pins moved), then each owner's report.
7. **Retirement of old specs** is #1450 (Milestone 40).

### 1.7 Rollback: pins back

Until old specs retire (#1450), the 30 load path stays, so **the operator can move account and
production pins back** to the pre-migration versions if the new release misbehaves. Each move is
recorded and announced to owners. The engine keeps running 30 throughout
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1447#issuecomment-6924497114)).

### 1.8 Every property required

At 31 every property of every format is listed in `required`, and an optional one is nullable
(part 02 § 3.3). The migration rewrites today's formats to match ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6959885810), [#1447](https://github.com/Consultologist/Consultologist-Blazor/issues/1447#issuecomment-6959888494)):
- **package formats and inputs' object fields** in each migrated package;
- **the catalogue's formats,** released as new versions in the contracts release (index § 5,
  step 1; § 1.6, step 0).

Agents then return explicit `null`s for optional fields where they used to leave the key out.
**The record and History treat an explicit `null` as absent,** and the byte-equivalence report
(§ 3.2) notes the difference rather than failing on it.

## 2. The converter

### 2.1 Where it lives

**The conversion logic ships in the Milestone 39 library** (`Consultologist.PackageFormat`),
because the migration uses it; only the Editor's screen (a page, and the converter inside every
condition builder) waits for Milestone 41
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1423#issuecomment-6957670275)).
The tool and the migration therefore never disagree. The converter isn't part of the format: the
format and the checker only ever see JSON Logic.

**Its surface: Proposed.**

```csharp
public static class WorkflowConditionConverter
{
    // 30's Scriban condition or let expression → canonical JSON Logic, typed against the package.
    public static ConversionResult FromScriban(string expression, ConversionScope scope);
}

public sealed record ConversionResult(
    JsonNode? Logic,                    // null when it can't convert
    string? ResultFormat,               // the type the checker gives it (boolean for a condition)
    IReadOnlyList<string> Reads,        // what it reads (input.x, node.x.value, item, pass, run.x)
    IReadOnlyList<string> Notices);     // one sentence per reason it can't convert
```

`ConversionScope` carries what the expression can read at its place (the package's inputs and
their formats, the nodes in scope and their formats, the loop context) so the result is
type-checked as the 31 checker will check it. The reverse direction, JSON Logic to text, is the
printer (part 03), which the converter reuses; there is one printer.

### 2.2 What it maps

30's conditions are Scriban boolean expressions (since 25, `WorkflowConditionAt25`), and 30's
`let`s are single Scriban expressions over pure builtins (`WorkflowLetAt30`). The converter maps
the subset that has a counterpart in 31's operation set (part 03). **Proposed:** the table below;
the operations themselves, their signatures and their null rules are part 03's.

| At 30 (Scriban) | At 31 (JSON Logic) |
|---|---|
| a read: `input.x`, `input.x.y`, `node.x.value.y`, `node.x.content`, `item.id`, `item.value.y`, `pass.number`, `run.x` | `{ "var": "<the same path>" }` |
| `a == b`, `a != b` | `{ "===": [a, b] }`, `{ "!==": [a, b] }` |
| `a < b`, `a <= b`, `a > b`, `a >= b` | the same operators |
| `a && b`, `a \|\| b` | `and`, `or` (flattened: `a && b && c` is one `and`) |
| `!a`, with `a` boolean | `{ "!": [a] }` |
| `+ - * / %` on numbers | the same operators |
| `(x \| array.size)`, `x.size` on a list | `{ "size": [x] }` |
| `string.downcase x`, `string.strip x` | `{ "lower": [x] }`, `{ "trim": [x] }` |
| `string.starts_with x "p"`, `string.ends_with x "p"` | `starts_with`, `ends_with` |
| `string.contains x "p"` | `{ "in": ["p", x] }` |
| `date.parse "YYYY-MM-DD"` | the literal string, typed `date` by its comparison |
| a number, `true`, `false`, a quoted string | the JSON literal |
| `{ a: x, b: y }` (a `let`'s object) | the `object` operation (part 03) |
| `[x, y]` | a JSON array |

- **Equality is always written strict** (`===`/`!==`); `==`/`!=` are never written (#1386).
- **The output is canonical:** the same input always gives the same bytes (RFC 8785 ordering for
  any object), so migrated packages diff cleanly and the round-trip fixtures are stable.

### 2.3 What it refuses

Anything outside the table gives `Logic: null` and one notice per reason, which the migration
turns into a hand edit (§ 1.4). In particular:
- a builtin or pipe with no 31 operation;
- a comparison the 31 checker refuses by type (for example `<` on text, or `!` used as a blank
  test on text), using **the checker's own sentence** (part 03);
- a read of `profile.*` in a condition, which 31 refuses (#1408);
- an assignment, a template, or more than one statement.

**`!x` on text** isn't converted to `blank`, since Scriban's truthiness of an empty string and
31's `blank` (which also counts whitespace) can differ; it becomes a hand edit: **Proposed**.

### 2.4 How it's tested

- **Fixtures for every row** of § 2.2 and every refusal of § 2.3, with the expected bytes.
- **Semantic equivalence: Proposed.** For every condition in the public packages and the fixture
  set, 30's evaluator on the Scriban string and 31's evaluator on the converted JSON Logic give the
  same decision over a set of fictional scopes, including `null` and absent values. Where 31's
  null rules (part 03) give a different answer from 30's, the case is listed as an expected
  difference in the byte-equivalence report (§ 3), never silently accepted.
- **The Editor's round trip** (expression → JSON Logic → expression, over a fixture set covering
  every operation) is Milestone 41's, with its screen (#1423).

## 3. Byte-equivalence

### 3.1 The harness

- **Built first** (build area 1), as the main safety net for an all-at-once release
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6905990797)).
- **What it does:** renders the 30 package and the migrated 31 package on fixed fictional inputs,
  with model outputs **stubbed deterministically from the formats**, and compares **every prompt
  and every deliverable**, byte for byte.
- **The stubs: Proposed.** Each agent node's output is generated from its format's schema, seeded
  by the node's qualified key, item path and pass, so the same package and inputs always give the
  same stubs. Format examples are Milestone 40 (#1402), so stubs can't rely on them.
- **The same deterministic harness** runs in-package tests offline (#1449) and the corpus re-render
  that must be byte-identical before any library upgrade (#1390).

### 3.2 The report

- **Reported, never a gate.** Each difference is listed with its file, its site and both texts'
  hashes, and marked **expected** where the rewrite declares and explains it (for example a
  whitespace form #1412 refuses). An optional field that 30 left out and 31 sends as `null`
  (§ 1.8) is noted, never counted as a failure. The report **never blocks publish**, for public or account
  packages ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1447)).
- **Where it goes:** attached to the publish record of each migrated account version, and shown by
  the Editor and History; for the public packages, attached to the migration PR.
- **Its shape: Proposed.** One JSON document (`migration-report.json`) beside the publish report
  in the version's folder: the old and new versions, the hand edits made, and one entry per
  compared text with `{ "site", "oldHash", "newHash", "equal", "expected", "explanation" }`, with
  `explanation` `null` unless `expected` is true. Like the other derived files, it never lives in
  source.

### 3.3 The fixtures

Captured from today's engine **before** any 31 code changes it:
- every public package's prompts and notes on fictional inputs (`example-nested-note`,
  `example-conditional-sections`, `test/general` and the rest);
- **generated deliverable templates** reproducing today's composer bytes (#1406);
- **Scriban renderers matching the C# renderers** (`concept_bullets`, `concept_context`, #1397);
- **the converter's** fixtures and equivalence vectors (§ 2.4).

The Editor's round trip (#1410) is no longer one of Milestone 39's safety nets; it moved to
Milestone 41 with the Editor
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1447#issuecomment-6924328714)).

## 4. The snapshot test

**The migration writes one test per package** from its migrated run
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1447#issuecomment-6924070749)),
so every migrated package starts with one test pinning its behaviour. It follows #1449's grammar
(part 07): fictional `inputs`; fixed `profile` and `run` values for what the package reads;
`outputs` stubbed per agent node, item and pass from the harness (§ 3.1); and `expect` holding
the resulting prompts and deliverables, **by hash: Proposed** (exact text would put a whole note
into the package; a hash pins the same bytes).

- **Its id: `migration_snapshot`, at `tests/migration_snapshot.json`: Proposed.**
- Like every test, it runs offline in CI, and at publish, where a failure blocks publish. Its
  result goes in the publish report, never in a job's record.

## 5. The conformance suite

Released with the spec in `consultologist-package-format` (§ 1.6, step 0), exported by the
library's generators as for every spec bump
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6957680891)):

- **A valid and an invalid package for every refusal sentence** in parts 01 to 07; each invalid
  package carries the exact sentence it must produce.
- **Golden tests:** every built-in operation's output (any change to output bytes is a major
  version, #1390); the printer's text for every operation, with `=`/`≠`; Scriban dialect
  features (#1425).
- **Null and absence vectors** for every JSON Logic operation, one per rule in part 03's table.
- **Fixtures for the compiled documents:** fixture packages with their expected `resolve.json` and
  `lock.json` bytes and hashes, `requires` and `needs`, and the publish report (including the
  ceiling note and test results).
- **The schemas** of the 31 file kinds, of `resolve.json` and `lock.json` (with their media types,
  `application/vnd.consultologist.resolve+json` and `application/vnd.consultologist.lock+json`),
  and the dialect's meta-schema.
- **The published constants:** `FormatBounds`, `SourceBounds`, the reserved ids, and the Markdig
  extension list for the pinned Markdig version.
- **Migration vectors: Proposed.** 30 packages with their expected 31 output and notices, so an
  outside implementation of the migration can be checked too.

## 6. Author documentation

**Rewritten before the example packages publish at 31**
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6957680891)),
with JSON Logic throughout and `===`/`!==` in every example:

- the workflows repository's README (`consultologist-workflows`);
- the app's Help page;
- the spec's own examples (`package-format-v31.md` in the package-format registry);
- **Proposed:** the recipes in `docs/customizable-workflow/` that teach a 30 construct
  (for example `macro-classifier-gating.md`, whose `when: node:<id> == yes` becomes a macro `if`
  in JSON Logic), each updated or marked as applying to 30, and the 31 migration documented for
  authors of the public repository (`migrate-workflow-package.cs --to 31`, the notices, the
  report).

The converter's own expression syntax is documented on its page, with the screen, in
Milestone 41 (#1423).

## 7. The satellites

Decided on [#1447](https://github.com/Consultologist/Consultologist-Blazor/issues/1447#issuecomment-6957669427):
not in lockstep any more. The contract is `WorkflowPackages/Current`, which changes **by addition
only** ([part 09 § 4](09-engine-and-pages.md#4-the-satellites-contract-workflowpackagescurrent)).
Each satellite is updated **before the cutover** to work with 30 and 31 alike:

| Satellite | Today | Its work |
|---|---|---|
| **Copilot agent** (`consultologist-copilot-agent`) | `IntakeCardBuilder` builds Teams intake cards from `Id`, `Label`, `Required`, `Type`, `Values`, `Items`, `Fields`; offers `Macros` as per-run choices | add the trailing `Format` member to its mirrored `WorkflowPackageInputResponse`; use `oneOf` titles from `Format` for enum choices where present; treat an empty `Macros` as "no choices"; show *"This workflow needs the web app"* with a link for any input it can't render |
| **Zoom app** (`consultologist-zoom-app`) | mirrors the engine DTOs | the same |
| **SMART panel, email intake** | read the same endpoint or start jobs | checked against the contract; no change expected |

- **Filed as issues in their own repositories,** linked from the build parent issue.
- **Released and verified against migrated public packages** (on dev first) before the engine
  bump (§ 1.6).

## 8. What moved to Milestones 40 and 41

Milestone 39 ships one version; these were decided out of it, or filed for later, during the
design.

**Milestone 40: inputs to delivery, typed and verifiable**

| Issue | What |
|---|---|
| #1422 | Export `resolve.json` to CNCF Serverless Workflow (`workflow.sw.json`), a descriptive view; moved 2026-11-17, since a view needs no spec version |
| #1453 | Revisit null in JSON Logic: required guards, or a library's default null behaviour |
| #1455 | Review a `semantics` block for `resolve.json`; Milestone 39 relies on `specVersion` alone |
| #1456 | Re-evaluate a `default` member for inputs; Milestone 39 migrates macro choices to optional booleans |
| #1457 | The same macro placed twice in one node's template |
| #1399 | The interactive graph: Cytoscape.js compound nodes, drawn from `resolve.json` |
| #1454 | Graphviz as a diagram renderer, with ports |
| #1439 | Model-produced formats split by how they can be verified; recording each value's model, agent version and settings |
| #1445 | Recording an agent's tool calls |
| #1448 | `x-phi`: marking which values can contain patient data |
| #1450 | Retiring old spec versions: refuse publish, refuse new jobs, remove load paths; old records always readable |
| #1426, #1427 | Revisit inline schemas in `format`; revisit the name `channel` |
| #1451 | Markdown formats by flavour (CommonMark, GFM, Markdig) |
| #1420 | Importing functions from other packages |
| #1417 | Personal (account-level) macros |
| #1402, #1415 | Formats carrying fictional examples; closed formats verifiable; live-model tests (#1449) |
| #1438, #1442 | Text from OCR and extraction as formats; input kinds beyond text and typed values |

**No milestone (eventually)**

| Issue | What |
|---|---|
| #1458 | Move the JSON Logic evaluator from Corvus to json-everything's JsonLogic, paying its fee |
| #1459 | Move value validation from Corvus.JsonSchema to JsonSchema.Net, paying its fee |

Either swap must leave golden tests and a corpus re-render byte-identical.

**Milestone 41: the Editor at the new format**

| Issue | What |
|---|---|
| #1423 (UI) | The condition converter's page and its place in every condition builder, with the pattern tester; the logic is in Milestone 39 |
| #1410 | Deliverables as tree items with their own panes, built from blocks; its byte-identical round trip |
| #1411 | The three-column deliverable builder |
| #1413 | The step-by-step debugger |
| #1414 | The lineage view (debugger and History) |
| #1418 | The macro pane |
| #1421 | A pane for every folder |
| (editing) | Editing and publishing 31 packages at all: until then they are view-only ([part 09 § 8](09-engine-and-pages.md#8-the-editor)), and "Run tests" in the Editor (#1449) |

## Refusals

Sentences this part's tools write. The grammar's sentences are in parts 01 to 07; the engine's
and the pages' in part 09.

- The script, when hand edits remain (#1447): *"written; hand edits remain"*
- A hand-edit notice (**Proposed** wording): *"Hand edit needed in <file>, <member>: <reason>."*
- The publish record of a migrated version (#1447): *"migrated by the operator from <old
  version>"*
- A converter refusal on type: the 31 checker's own sentence (part 03), unchanged.
- A converter refusal for anything else (**Proposed**): *"The condition '<expression>' uses
  <construct>, which has no JSON Logic form at 31: rewrite it by hand."*

## Proposed in this part

- **The Editor's "upgrade to 31" waits for Milestone 41** (§ 1.1): the Editor can't edit 31 until
  then, though #1447's body lists it as a caller.
- **The migrated macro-choice input takes the macro's id and label; `_macro` suffix on collision**
  (§ 1.3): needed to write the input; not decided.
- **Script exit codes 0 and 2** (§ 1.4): #1447 fixes only exit 1.
- **Hand-edit notice wording** (§ 1.4) and **the converter's general refusal wording** (Refusals).
- **Dry run on copies of account packages' sources, deleted afterwards** (§ 1.6): the dry run
  needs real shapes; sources hold no patient text.
- **The converter's surface** (`WorkflowConditionConverter`, `ConversionResult`, § 2.1).
- **The mapping table** (§ 2.2): it follows part 03's operation set, but no issue lists the
  Scriban-to-JSON Logic pairs.
- **`!x` on text is a hand edit, not `blank`** (§ 2.3): their truthiness can differ.
- **Semantic-equivalence tests for the converter** (§ 2.4): the Editor round trip moved to M41, so
  M39 needs its own check.
- **Stubs generated from the schema, seeded by key, item and pass** (§ 3.1): format examples are
  Milestone 40.
- **`migration-report.json` and its shape** (§ 3.2): the report's form isn't decided.
- **Snapshot test id `migration_snapshot`, expectations by hash** (§ 4).
- **Migration vectors in the conformance suite** (§ 5).
- **Recipes in `docs/customizable-workflow/` updated or marked as 30** (§ 6).
