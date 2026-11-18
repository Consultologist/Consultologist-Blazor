# 08. The record: storage 12 and the provenance contract

This part defines the record a version 31 job writes: **record storage 12**, and the new
**provenance contract** version that publishes it. It collects the record side of every Milestone 39
issue: keys with their roots (#1383, #1384); `resolveHash`, `lockHash`, the exact versions that ran,
run facts, run spots and extraction versions (#1389, #1390, #1425); type and sent-format hashes
(#1385); the run-dependent and model-derived flags (#1386); renderer sub-rows (#1397); macro rows
(#1412); a row for every condition decision; check rows and check loops (#1395, #1396); call rows
(#1419); deliverable rows and `run.date` (#1406); profile reads and the signature block (#1408);
agent definitions and bindings; and what stays out (test results, #1449). The decisions of
2026-11-17 are on [#1388](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6957680891);
the points this part left open were
[decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979794633)
on 2026-11-18.

Names are written in the record's JSON form (camelCase, as `provenance-record.md` writes them); the
C# properties on `ConsultGenerationJobState` and `ConsultNodeOutputState` are their PascalCase
twins.

## 1. Storage 12

**Storage 12 is storage 11 plus additions, except that keys are written with their roots**
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6957680891),
[roots](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)).

- **A 31 job is stamped `schemaVersion` 12 from its creation,** on every path, as 30 jobs are
  stamped 11. The ladder in `ConsultGenerationState.cs` (`Initialize` and the creation path) gains
  `>= 31 ? 12`.
- **`StorageVersions`** (`ConsultGenerationState.cs`, today `{ 2, 6, 7, 8, 9, 10, 11 }`) gains 12, and
  the provenance registry's `recordStorageVersions` gains it in the same change (§ 13).
- **History keeps reading storage 2–11 records exactly as today.** Nothing is converted. Jobs that ran
  before 31 carry no `resolveHash` or `lockHash`
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6904042859)).
- **Everything storage 11 holds keeps its meaning:** paths for item keys, `loopInstances`, `while`
  rows with `passes` and `stopped`, the not-run state, item progress per loop, the 120-character item
  name (v30 design § 7). The `let` row of storage 11 is the `computed` row at 12, with `producer:
  computed` as before. At 12 **every row records the node's `producer`** (§ 4)
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)).
- **The record holds hashes, ids, counts, reasons and names, never element values** (#369's rule),
  with the exceptions storage 11 made (an item name drawn from a field) and the ones this part adds:
  run facts and run spot outputs (§ 8), and printed conditions (§ 5), which are package text.

## 2. Keys

### 2.1 Every key with its root

Wherever storage 11 names a node by its bare id, storage 12 names it by its **rooted key**: the
root (`node`, `check`, `deliverable`), then the ids of its enclosing flow folders and its own, joined
by `.`. The same keys are used in `resolve.json`, test files and History links
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910),
[#1384](https://github.com/Consultologist/Consultologist-Blazor/issues/1384#issuecomment-6904190578)).

```
node.plan                                     a top-level node
node.sections.paragraphs.draft_paragraph      a node two loops down
node.hpi_section.standard_draft               a node inside a function, through its call node
node.standards.section.standard_draft         a call node inside a loop, then the function's node
check.part_coverage.coverage                  a check inside a check loop
deliverable.emr.note                          deliverables/emr/note.md
```

**A row key** is the rooted key, then `:` and the item path for an instance inside a loop or a
`while` (v30's path grammar, unchanged): `node.sections.paragraphs.draft_paragraph:2/1`,
`node.refine.judge:3`, `node.standards.section.standard_draft:3`, `check.part_coverage.coverage:2`.
`.` separates scopes, `/` separates path segments and `:` separates the key from the path; no id or
item id contains any of the three, and item paths never contain `:`.

**Function calls:** inner rows are keyed through the call node's rooted key, never the function's
name. The function's name is on the call's row (§ 7). Two calls to the same function get different
keys from their call nodes ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6897717122)).

**What moves to rooted keys:**
- `nodeOutputs` keys and each row's `nodeId`;
- block ids, `skippedBlocks`, `blocks`;
- item-step and block events, `itemProgress`, `collections`;
- the concept source, `nodes` (the descriptor rows), `loopInstances`, `unresolvedLoops`;
- deliverable ids, which are paths like node ids (`deliverables/emr/note.md` is
  `deliverable.emr.note`), so moving a deliverable changes its key;
- the run diagram's Mermaid ids (with `.` replaced by `__`), the rail, History and Editor links
  (part 09).

### 2.2 Separate sections, separate roots

**Checks are kept in their own section** (§ 6), and **deliverables in their own list** (§ 10).
Since every key carries its root, a node, a check and a deliverable may share an id and their keys
never collide ([#1395](https://github.com/Consultologist/Consultologist-Blazor/issues/1395),
[#1396](https://github.com/Consultologist/Consultologist-Blazor/issues/1396#issuecomment-6905675034)).

### 2.3 Sub-row ids

Some rows have sub-rows that live **inside** their row, not in `nodeOutputs`. Each has an id for
History's links, the lineage view (#1414) and test expectations, formed as
`<row key>/<sub kind>:<n or id>`. A sub-row id is never parsed as a row key; a segment holding `:` is
always a sub-row, since item paths never contain `:`
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979794633)).

| Sub-row | Id |
|---|---|
| a renderer use | `<row key>/render:<n>`, `n` its 1-based position in the body: `node.identify_problem/render:1`, `node.sections.paragraphs.draft_paragraph:2/1/render:1` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6957680891), [in a loop](https://github.com/Consultologist/Consultologist-Blazor/issues/1397#issuecomment-6959888097)) |
| a macro row | `<row key>/macro:<macro id>`: `node.assessment/macro:letrozole_prescription_text` |
| a run spot | `<row key>/spot:<n>` |
| a decision | `<owner key>/if:<site>` (§ 5.2): `node.standards.assessment:7/if:macro.letrozole_prescription_text` |

## 3. The job header

New members at the job level, all **write-once at job start**, beside the package name and
version, and shown in History's job header:

| Member | Type | Null | Meaning |
|---|---|---|---|
| `resolveHash` | string (`sha256:…`) | null below 31 | the SHA-256 of the canonical `resolve.json` bytes the job ran ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6898433175)) |
| `lockHash` | string (`sha256:…`) | null below 31 | the SHA-256 of the canonical `lock.json` bytes ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1390#issuecomment-6898433402)) |
| `versionsRan` | object | null below 31 | **the exact versions that actually ran,** which under option c can differ from the lock within its range ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1390#issuecomment-6904042632)). It has `lock.json`'s members, so the two compare member by member: `packageFormat`, `dialect`, `libraries` (`Scriban`, `Markdig`, `Corvus.Text.Json.JsonLogic`, `Corvus.JsonSchema`), `evaluator`, `operations` (those the package uses), `builtins`, `culture` (`"invariant"`) ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979794633)) |
| `runFacts` | object | null below 31 | the raw value of each run fact the package reads, once per run (§ 8.1); `{}` when it reads none |
| `maxAgentCalls` | integer | null below 31 | the per-job ceiling in force at job start, recorded once ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1393#issuecomment-6980307806), part 09). Member name **Proposed** |
| `signatureBlockId` | string | allowed | the profile signature block the job used, by **id**, never its text; `null` when the package doesn't read `profile.signature` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1408#issuecomment-6904843222)) |
| `signatureChosen` | `"default"` or `"job"` | allowed | whether the block was the account's chosen block (`ChosenId`) or picked on the Create page for this job; `null` with `signatureBlockId` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979794633)) |
| `decisions` | array of decision | never | every condition decision of the job (§ 5); `[]` for none |

**Unchanged and still recorded:** `packageSpecVersion` (31), `workflowPackage`, `packageFormatRef`,
`provenanceRef`, `formatsRef`, `catalogRef`, `engineCommit`, `engineRelease`, `apiHost`, the
terminology snapshot. `formatsRef` and the agents catalogue version now also appear in `lockHash`'s
document; the header keeps them for readers of older records.

**Retired at 12** (never written by a 31 job; older records are read exactly as today):
`macroChoices` and `excludedMacros`, since per-run macro choices are boolean inputs at 31 and their
effect is recorded as decisions (§ 5), and `classifications`, since every classifier is an ordinary
agent node with its own row
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979794633)).

### 3.1 Inputs: extraction versions and lineage

Each **document-filled input**, and each document element of a mixed array, records beside its hash
and origin ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6924904289),
[names](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979794633)):

| Member (on the input origin) | Type | Null | Meaning |
|---|---|---|---|
| `converter` | object `{ name: string, version: string }` | allowed | the document-to-Markdown converter and its version; `null` where the step wasn't used |
| `parser` | object `{ name: string, version: string }` | allowed | the parsing library and its version (PdfPig, DocumentFormat.OpenXml, …); `null` where the step wasn't used |
| `ocr` | object `{ service: string, model: string }` | allowed | the OCR service and model version, when OCR ran (#239); `null` when it didn't |

They join today's `kind`, `extractor`, `pageCount`, `fileSha256` and `textSha256`; `extractor`
stays for older records. #1438 (Milestone 40) builds on them.

**An input element from a previous run's deliverable** (#874) starts out **model-derived**, and its
origin links to the job it came from. Typed text and uploaded documents start clean
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6963556118)).

**Input elements record their `origin`** (`typed`, `document`, `previous-run`, `transcript`), never a
`producer`, which is a node's. The engine always knows the origin, so `null` is never written
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)).

## 4. Node rows

A production node instance's row keeps every storage 11 member (`label`, `status`, `inputHash`,
`outputHash`, `hashVersion`, `tokens`, `validation`, `reason`, the clock and failure members, …) and
gains the members below. Their names are
[decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979794633).

| Member | Type | Null | Meaning |
|---|---|---|---|
| `nodeId` | string | never | now the **rooted key** |
| `kind` | string | never | `agent`, `rendered`, `computed`, `for`, `while`, `if`, `case` or `call` |
| `producer` | string | never | the node's `producer` as written and checked: `generated` on agent rows, `computed` on every other ([part 02 § 1.3](02-types-and-inputs.md#13-producer), [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)) |
| `formatHash` | string | allowed | the **full** hash of the node's resolved format (`resolve.json`'s `formatHashes`); `null` for flow kinds. With `agent`, it lets History say "the same type, served by a different agent" or the reverse ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6879413099)) |
| `typeHash` | string | allowed | the format's **type** hash (`resolve.json`'s `typeHashes`); `null` with `formatHash` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466)) |
| `sentFormatHash` | string | allowed | on agent rows, the hash of the format as sent to the model: `title`, `$id` and the keywords strict mode rejects removed, by the attested agent definition's list; `null` on other rows ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466)) |
| `notSent` | array of string | never | on agent rows, the removed keywords as JSON pointers into the format (`/properties/onset/format`); `[]` for none. History shows *"Not sent to the model: …"*, and the reply is still validated against the full format. Member name **Proposed** |
| `agent` | object | allowed | on agent rows only (§ 4.1); `null` otherwise |
| `itemName` | string | allowed | the item's name from the loop's `itemLabel`, capped at 120 characters, kept for the life of the record (storage 11's rule, renamed from `labelField`); `null` on rows outside a loop |
| `runDependent` | boolean | never | `true` when the row's value depends on `run.*`, transitively ([#1386](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6925165983)): the node's `runDependent` as written and checked ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1478#issuecomment-6982835861)) |
| `modelDerived` | boolean | never | `true` when the row's value is computed from a model's output, directly or through other values ([#1386](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6962982698)). Every agent row is `true`. History, the diagram and the Editor show it (*"computed from model text: node.draft"*). The node's `modelDerived` as written and checked ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1478#issuecomment-6982835861)); requiring either flag in a read is #1478 (Milestone 40) |
| `runNeutralHash` | string | allowed | for a body with run spots, the hash of the body rendered with each run spot left as its marker; `null` when the body has none, so `inputHash` alone is the hash (§ 8.2) ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6978508523)) |
| `runSpots` | array of run spot | never | § 8.2; `[]` for none |
| `renders` | array of renderer sub-row | never | § 4.2; `[]` for none |
| `macros` | array of macro row | never | § 4.3; `[]` for none; agent rows only |
| `invalid` | object | allowed | the node's `onInvalid` outcome when a reply failed validation or the macro marker check: `{ reason: string, action: "retry" \| "fail", attempts: integer }`, where `reason` names what failed, for example `macro letrozole_prescription_text: missing`. `null` when the reply was valid. That the outcome sits on the node's row is [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1412#issuecomment-6957666676); the object's own members are **Proposed**. A retry message never quotes the reply; it names only what was wrong ([part 05](05-templates-and-macros.md)) |
| `retries` | array of `{ atUtc: string, waitMs: integer }` | never | each 429 (capacity) retry the call needed, with its back-off ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1393)); `[]` for none. The entry's members are **Proposed** |
| `profileReads` | array of `{ field: string, hash: string }` | never | each `profile.*` read by a macro body placed in this node's prompt, by hash (§ 9); `[]` for none |
| `stateReason` | string | allowed | the sentence for a row that didn't complete: *skipped: …* (§ 5), *not run: beyond max <n>* ([part 04](04-loops-and-functions.md)), or a failure's reason (a `matches` time-out, [part 03](03-expressions.md)); `null` when completed |

A node that didn't complete reads `null` for `.value` and `.content`; `.state` says why
([part 03](03-expressions.md)).

For a `computed` row, `inputHash` covers the expression and the values it read. For a run-dependent
`computed` value, the run-neutral hash covers its expression and its other reads, **not its value**
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6925165983)).
A `computed` row reading a model-derived value is `modelDerived: true`, still `reproducible`, and
its producer is `computed`; its format is its operand's shape, unchanged.

### 4.1 The agent: definition and binding

Agent rows **name their attested agent definition** (id and version) **and the binding that chose
it** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6957680891)):

| Member of `agent` | Type | Null | Meaning |
|---|---|---|---|
| `name` | string | never | the attested agent definition's id in the agents catalogue (`consult-prose`) |
| `version` | integer | never | its version (`4`) |
| `binding` | object `{ format: string, producer: string, via: string }` | never | the format and producer whose binding chose the agent, and the route: `"format"` (a registry or catalogue format's binding) or `"custom"` (a package's own format, bound to the custom-format agent); `"producer"`, an agent chosen by producer alone, is reserved at 31 and refused until #1479, Milestone 40 (`resolve.json`'s `bindings`, part 07; [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868), [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1478#issuecomment-6982835861)) |

The agents catalogue's version is in `lock.json`; the record doesn't repeat it per row. **The fuller
recording** (each value's model, the settings read from the attested definition, tool calls) is
Milestone 40: #1439 and #1445.

### 4.2 Renderer sub-rows

**One sub-row per pipe step,** in the order the steps appear in the body, per instance (per item and
pass), inside the consuming row's `renders`. A chain (`{{ node.extract.value | renderer.concept_list
| renderer.bullets }}`) gives one sub-row per step (`…/render:1`, `…/render:2`)
([part 05](05-templates-and-macros.md)). It's never a node row and never scheduled
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1397#issuecomment-6877988508)):

| Member | Type | Null | Meaning |
|---|---|---|---|
| `id` | string | never | `<row key>/render:<n>` (§ 2.3) |
| `renderer` | string | never | `renderer.<id>` |
| `templateHash` | string | never | the renderer's `bodyHash` in `resolve.json`, or for a built-in renderer its template's hash at the locked version |
| `source` | string | never | the path piped in (`node.extract.value`), or the previous step's sub-row id in a chain |
| `inputHash` | string | never | the hash of the piped value (canonical JSON for data, UTF-8 for text) |
| `inputSize` | string | allowed | a size summary from the input's format, for example `"14 concepts"`; `null` when none applies |
| `outputHash` | string | never | the hash of the rendered text |
| `outputLength` | integer | never | the rendered text's length in characters |
| `runDependent` | boolean | never | `true` when the piped value is a `run.*` fact or run-dependent; the run spot then covers the whole pipeline ([part 05](05-templates-and-macros.md)) |

The input value and rendered text are produced text, under the 7-day retention (§ 11).

### 4.3 Macro rows

**A macro row per macro per node instance,** for every macro the node's prompt places, its parts'
text included ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1412#issuecomment-6957666676)):

| Member | Type | Null | Meaning |
|---|---|---|---|
| `id` | string | never | `<row key>/macro:<macro id>` (§ 2.3) |
| `macro` | string | never | `macro.<id>` |
| `offered` | boolean | never | whether the macro's `if` (and its folder's) held, and, when a part's text places it, the part's `if` too, so its instruction was added to the prompt |
| `decisions` | array of string | never | the decision ids (§ 5) its offer depended on: the macro's `if`, its folder's, and the placing part's, when they're not `null`; `[]` when all are `null` ([part 05](05-templates-and-macros.md)) |
| `classifier` | string | allowed | the row key of the classifier node its `if` read, linking to that row, where History shows the `judgement`'s `reason` beside its `answer` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6982429387)); `null` when it read none |
| `placed` | boolean | never | `true` when the marker was found **exactly once**, in the form its instruction asked for; `false` when not offered, or when the check failed |
| `form` | `"block"` or `"inline"` | allowed | the form the instruction asked for; `null` when not offered |
| `contentHash` | string | allowed | the hash of the macro's rendered body as substituted; `null` when not offered |

If the marker was missing, repeated or altered, the node's `onInvalid` outcome is on the node's row
(`invalid`), not here. Profile reads in macro bodies are the node's `profileReads`. **The same macro
used twice in one template** is #1457 (Milestone 40).

## 5. Condition decisions

### 5.1 The decision

**One job-level `decisions` list:** a decision for every condition the engine evaluates, holding
its site, the printed condition, its result, the hashes of what it read, whether it's run-dependent,
and an `expressionHash` of its canonical JSON Logic. Rows link to their decisions by id, and a
decision can concern something that never got a row (a skipped node)
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6957680891),
[list](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979794633)).

| Member | Type | Null | Meaning |
|---|---|---|---|
| `id` | string | never | `<owner key>/if:<site>` (§ 5.2) |
| `owner` | string | never | the row key, check row key or deliverable key the decision concerns |
| `site` | string | never | what was decided (§ 5.2) |
| `condition` | string | never | the condition **as printed** by the one printer ([part 03](03-expressions.md)): `input.kind.value = "table"`. A condition comparing a `.title` prints both the raw value and the title ([part 02](02-types-and-inputs.md)). Package text, kept for the life of the record |
| `expressionHash` | string | never | the hash of the canonical JSON Logic, which the printed text came from |
| `result` | boolean or string | never | `true` or `false`; for a `case`, the id of the branch taken, or `"else"` |
| `reads` | array of `{ path: string, hash: string }` | never | each value it read, by hash; never the value itself |
| `runDependent` | boolean | never | `true` when it read `run.*` or a run-dependent value |
| `decidedAtUtc` | string | never | when the engine evaluated it |

**Reasons read as sentences** built from the printed condition: *skipped: input.kind.value =
"table" is false*. A row skipped by a decision has this sentence as its `stateReason`, and the
decision's id is how History links the two.

### 5.2 Which decisions, and their sites

Every condition the engine evaluates is recorded, per instance:

| Condition | `site` | Owner |
|---|---|---|
| a node's, check's, branch's or loop's header `if` | `self` | its row key |
| a `case` | `case` (result: the branch id) | its row key |
| a `while`'s condition, before or after pass *n* | `pass.<n>` | the `while`'s row key |
| a `for`'s `until`, after item *i* | `until.<i>` | the loop's row key |
| a macro's `if` | `macro.<id>` | the placing node's row key |
| a macro folder's `if` (both must hold; the record shows which was false) | `macro-folder.<folder id>` | the placing node's row key |
| a part's `if` | `part.<id>` | the node's row key |
| a snippet's header `if` | `snippet.<id>` | the placing row key |
| a deliverable's header `if` | `self` | its deliverable key (`deliverable.emr.note`) |

Snippet folder items have no `if` ([part 01](01-files-and-scopes.md)), so there's no per-item
site. A body `if` in a template tests recorded facts only (loop position, `state`, whether a check
passed), which are already rows; it makes no decision.

## 6. Check rows

**Checks have their own section,** `checks`, apart from `nodeOutputs`, keyed by check row key
(§ 2.2), in the order the job runs: Inputs, Nodes, Checks, Deliverables
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6881869311)).
The record holds a check's result once; a deliverable refers to it. Member names are
[decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979794633).

| Member | Type | Null | Meaning |
|---|---|---|---|
| `key` | string | never | `check.coverage`, `check.part_coverage.coverage:2` |
| `kind` | string | never | `computed`, or `for` for a check loop's own row |
| `label` | string | never | the check's label |
| `status` | string | never | `Completed`, `Skipped`, `Failed`, as node rows |
| `itemName` | string | allowed | in a check loop, the item's name from the loop's `itemLabel`, capped at 120 |
| `alignedWith` | string | allowed | in a check loop, the row key of the production item with the same id (`node.sections:2` for `check.part_coverage.coverage:2`); `null` at the top level |
| `checked` | object `{ ref: string, hash: string }` | allowed | what it ran on, with that output's hash: `deliverable.consult` with its composed hash, or `node.plan` with its output hash; `null` when it ran on several reads equally |
| `reads` | array of `{ path: string, hash: string }` | never | its other inputs, by hash |
| `inputHash`, `outputHash` | string | allowed | as on a `computed` row |
| `producer` | string | never | `computed`, as on node rows |
| `modelDerived`, `runDependent` | boolean | never | as on node rows |
| `pass` | boolean | allowed | the `check_result`'s verdict, under the same name; kept for the life of the record, as today's `checkPassed`; `null` while running or when skipped. `passed` only ever means a count ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)) |
| `failWith` | string | allowed | the `check_result`'s `message`, the operation's literal `failWith` sentence, when it failed (package text, kept); `null` when it passed |
| `effect` | string | allowed | what it caused: `released`, `held`, `not sent`, or `read by <ref>`; `null` until known |
| `count` | integer | allowed | on a check loop's own row, its item count; `null` on others |

The `check_result`'s `uncovered` and `untested` lists come from inputs, so they follow the text
retention rule: shown while retained, then deleted, with their hash kept for good (§ 11). Its
`passed` and `total` counts are kept. This holds for every package
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6978700251)).
Check operations judge only `completed` items and always return a `check_result`.

Check loops are **scheduled per item** and never stop production; a failing item's effect comes only
through what reads it ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1396#issuecomment-6905675034)).

## 7. Call rows

Each `call` instance is a row in `nodeOutputs` (`kind: call`), expanding to its inner rows like a
loop item ([#1419](https://github.com/Consultologist/Consultologist-Blazor/issues/1419)):

| Member | Type | Null | Meaning |
|---|---|---|---|
| `function` | string | never | the function's id; on the call's row, never in the key |
| `arguments` | array of `{ param: string, hash: string }` | never | each argument's hash: the call's input hashes are exactly its arguments' hashes |
| `outputHash` | string | allowed | the result's hash; one per call |
| `depth` | integer | never | the call's nesting depth: `0` for a call outside any function, *d*+1 for one inside a function reached at depth *d*, within `FormatBounds.depth` ([part 04](04-loops-and-functions.md)) |

Inner rows are keyed through the call node (§ 2.1). A decision made outside and passed in is
recorded once, at the caller; the `computed` node inside each call shows the value it received,
and is model-derived when the value was.

## 8. Run facts and run spots

### 8.1 Run facts

**Recorded once per run, kept permanently, shown in History's "Run facts" section**
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6920512005)).
`runFacts` holds the raw value of each fact the package reads (`resolve.json`'s `run`):

| Member | Type | Meaning |
|---|---|---|
| `date` | object `{ value: string, timeZone: string }` | the job's start date in the account's time zone (`"2026-11-16"`) and that zone's IANA name (`"America/Toronto"`), recorded beside the value ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1406#issuecomment-6920512477)) |
| `id` | string | the job's id |
| `package` | string | `<name>@<version>`, as `run.package` gives it |
| `engine` | string | the engine's release version, as `run.engine` gives it |

Only the facts read are present. Run facts aren't patient text, so they're outside the 7-day drop.

### 8.2 Run spots and the two hashes

For a body that reads `run.*` (a prompt, a `rendered` node, a deliverable, a snippet or macro body),
the record carries **the sent-text hash** (the main hash, as today), **the run-neutral hash** (the
same text with each run spot left as its `{{run:<n>}}` marker), and **each run spot**
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6920426229),
[#1425](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6978508523)).
A read of a run-dependent value is a run spot too, and a run spot that pipes into renderers covers
the whole pipeline.

| Member of a run spot | Type | Null | Meaning |
|---|---|---|---|
| `id` | string | never | `<row key>/spot:<n>` |
| `site` | integer | never | the spot's position in the body (`resolve.json`'s `runSpots`) |
| `reads` | string | never | what it reads (`run.date`, or a run-dependent value's path) |
| `valueHash` | string | never | the hash of the value read |
| `output` | string | never | the formatted output (`"16 November 2026"`), **kept permanently** |
| `outputHash` | string | never | the output's hash |

- **The sent text is rebuilt exactly** from the run-neutral text and the spot outputs, by literal
  substitution, so the main hash stays provable.
- **Runs are compared on the run-neutral hash.** Beside any body whose two hashes differ, History says
  the difference is due to run facts.
- **A body with no run spots has one hash;** its `runNeutralHash` is `null`.
- Deliverables carry the same members on their row (§ 10).

## 9. Profile reads

Each `profile.*` read is **a substitution recorded by hash**, and its text follows the 7-day
retention; the record names the **signature block's id**, not its text
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1408#issuecomment-6904843222)).
Reads happen only in deliverable templates, macro bodies and snippet bodies placed in them, so they
appear in a deliverable's `substitutions` (§ 10) and a node's `profileReads` (§ 4). The header's
`signatureBlockId` and `signatureChosen` (§ 3) name the block and how it was chosen.
**No `ConsultSignatureSnapshot` at 31:** the signature is a profile read, recorded by hash with its
block id; its text lives in the deliverable, under text retention
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979794633)).

## 10. Deliverable rows

A deliverable's row keeps storage 11's members (its id, state, `composedHash`) and adds the
template's provenance ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1406)):

| Member | Type | Null | Meaning |
|---|---|---|---|
| `key` | string | never | the deliverable key, its path: `deliverable.emr.note` |
| `templateHash` | string | never | the template's `bodyHash` in `resolve.json`; the text is shown from the pinned version, never copied |
| `format` | string | never | the deliverable's format (`markdown_block`, `plaintext` or an object format; never `markdown_inline`, [part 06](06-checks-deliverables-profile.md)) |
| `state` | string | never | `released`, `held`, `failed`, `skipped` (its header `if`) or `not sent` |
| `stateReason` | string | allowed | the sentence: *held: empty*, *skipped: <condition> is false*, *not sent: EMR isn't set up for this account*, *released: check '<id>' didn't apply (its if was false)*; `null` when released through a passing check or none |
| `check` | string | allowed | the check row key that gated it, linking to the check's row; `null` when ungated |
| `sites` | array of `{ site: integer, at: string, expr: string, loop: string \| null }` | never | each expression in the template, in source order, with its line and column. Package text, kept |
| `substitutions` | array of substitution | never | each evaluation: `{ site, pass: integer[] \| null, reads: string, hash: string, size: integer, renders: string \| null }`; `pass` holds one number per enclosing template loop |
| `renders` | array of renderer sub-row | never | as on node rows (§ 4.2) |
| `runNeutralHash`, `runSpots` | | | as on node rows (§ 8.2) |
| `modelDerived`, `runDependent` | boolean | never | as on node rows |

**Replay:** substituting the recorded values into the template, in site and pass order, must
reproduce `composedHash`. A deliverable row shows only a one-line check status; the check's row
holds the result.

**A deliverable whose gating check was skipped by its own `if` is released,** and recorded with the
reason above; an empty `all_pass`, by contrast, fails. **A bundle holds released deliverables
only;** History lists what was left out and why
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6978700251)).

## 11. Retention

| Kept for the life of the record | Deleted 7 days after completion (#368) |
|---|---|
| every hash, key, id, status, count, clock and token figure | produced text: replies, prompts as sent, composed deliverables |
| labels, item names (from `itemLabel`, capped at 120) | concepts |
| printed conditions, `failWith` sentences (a `check_result`'s `message`), template sites (package text) | input text and held inputs |
| decision results, check verdicts (`pass`), states and reasons | renderer inputs and outputs |
| run facts and run spot outputs | a `check_result`'s `uncovered` and `untested` lists |
| `resolveHash`, `lockHash`, `versionsRan`, extraction versions | the text behind profile reads |
| the `modelDerived` and `runDependent` flags, the removed-keyword pointers | |
| the signature block's id | |

Retention goes by where text came from, not what it says, and holds for every package, medical or
not ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6978700251)).
The sweep (`DropText`) deletes what the right-hand column names and stamps `textDroppedAtUtc`, as
today. Every hash stays, so a sub-row or substitution keeps its reference, format, size and hash and
says the text was deleted under retention.

## 12. What the record never holds

- **In-package test results.** Tests run at publish, not in jobs; their results are in the publish
  report (part 07) ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1449#issuecomment-6957669037)).
- **`resolve.json` or `lock.json` themselves.** The record pins their hashes; History fetches the
  documents from the registry by version and checks them against the hashes.
- **Element values,** apart from the exceptions in § 1.
- **The peak concurrency a job reached,** which goes to telemetry only (part 09).

## 13. The provenance contract

**A new provenance contract version** (`provenance@v2026.12.N`, the number set at release) is
released **first**, with the format spec and the agents catalogue (index § 5, step 1), and the
engine stamps it as `provenanceRef` on every 31 job. It changes:

- **`provenance-versions.json`:** `recordStorageVersions` gains `12`, pinned against
  `StorageVersions` by test as today.
- **`provenance-record.md`:** a storage 12 section with every member of this part, its type and null
  rule, and the statement that storage 2–11 records read as before.
- **`hash-definitions.md`:** the definitions 31 adds
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979794633)):
  - `nodeHashVersions` gains **7**: the sent-text hash over the rendered body with run spots and
    macro markers substituted, and the run-neutral hash beside it;
  - `effectiveInputHashVersions` gains **7**: inputs as the object keyed by id, with Markdown inputs
    in their converted form;
  - `workflowOutputHashVersions` gains **4**: a deliverable composed from its template, `composedHash`
    as replayed in § 10;
  - the `resolveHash` and `lockHash` definitions: SHA-256 over RFC 8785 bytes;
  - the type hash, full format hash and sent-format hash (#1385).
- **`registry-layout.md`:** `resolve.json`, `lock.json` and the publish report in each version's
  folder (part 07).

## 14. An example (abridged, fictional)

A `test/general` job at 31, the assessment node in the `standards` loop, with one macro offered and
one renderer use:

```jsonc
{
  "schemaVersion": 12,
  "packageSpecVersion": 31,
  "workflowPackage": "test/general@v2026.12.1",
  "resolveHash": "sha256:9b41…",
  "lockHash": "sha256:07c2…",
  "versionsRan": { "packageFormat": "package-format@v2026.12.1", "dialect": "scriban-dialect@1.0.0",
                   "libraries": { "Scriban": "7.2.6", "Markdig": "0.41.3",
                                  "Corvus.Text.Json.JsonLogic": "5.8.11", "Corvus.JsonSchema": "4.3.17" },
                   "evaluator": "1.0.0", "operations": { "terms_subset": "1.0.0" },
                   "builtins": { "concept_bullets": "1.0.0", "formats": "1.0.0", "markdown_escape": "1.0.0",
                                 "plain_conversion": "1.0.0", "printer": "1.0.0" },
                   "culture": "invariant" },
  "runFacts": { "date": { "value": "2026-11-16", "timeZone": "America/Toronto" } },
  "maxAgentCalls": 16,
  "signatureBlockId": "sig-2", "signatureChosen": "job",
  "decisions": [
    { "id": "node.standards.assessment:7/if:macro.letrozole_prescription_text",
      "owner": "node.standards.assessment:7", "site": "macro.letrozole_prescription_text",
      "condition": "node.standards.prescribes_letrozole.value.answer = true", "expressionHash": "sha256:…",
      "result": true,
      "reads": [ { "path": "node.standards.prescribes_letrozole.value.answer", "hash": "sha256:…" } ],
      "runDependent": false, "decidedAtUtc": "2026-11-16T14:02:11Z" } ],
  "nodeOutputs": {
    "node.standards.assessment:7": {
      "nodeId": "node.standards.assessment", "itemId": "7", "kind": "agent", "producer": "generated",
      "itemName": "Assessment and plan",
      "status": "Completed", "stateReason": null,
      "inputHash": "sha256:…", "outputHash": "sha256:…", "hashVersion": 7,
      "formatHash": "sha256:…", "typeHash": "sha256:…", "sentFormatHash": "sha256:…", "notSent": [],
      "agent": { "name": "consult-prose", "version": 4,
                 "binding": { "format": "markdown_block", "producer": "generated", "via": "format" } },
      "runDependent": false, "modelDerived": true, "runNeutralHash": null, "runSpots": [],
      "renders": [ { "id": "node.standards.assessment:7/render:1", "renderer": "renderer.concept_bullets",
                     "templateHash": "sha256:b81…", "source": "node.extract.value",
                     "inputHash": "sha256:7d0…", "inputSize": "14 concepts",
                     "outputHash": "sha256:3f9…", "outputLength": 612, "runDependent": false } ],
      "macros": [ { "id": "node.standards.assessment:7/macro:letrozole_prescription_text",
                    "macro": "macro.letrozole_prescription_text", "offered": true,
                    "decisions": ["node.standards.assessment:7/if:macro.letrozole_prescription_text"],
                    "classifier": "node.standards.prescribes_letrozole:7", "placed": true, "form": "block",
                    "contentHash": "sha256:…" } ],
      "invalid": null, "retries": [], "profileReads": []
    }
  },
  "checks": {
    "check.coverage": { "key": "check.coverage", "kind": "computed", "producer": "computed", "label": "Coverage",
                        "status": "Completed", "itemName": null, "alignedWith": null,
                        "checked": { "ref": "deliverable.consult", "hash": "sha256:9a3f…" },
                        "reads": [ { "path": "node.extract_input_terms.value", "hash": "sha256:…" } ],
                        "modelDerived": true, "runDependent": false,
                        "pass": true, "failWith": null, "effect": "released", "count": null } }
}
```

## Refusals and recorded sentences

The record refuses nothing itself; the refusals at start and load are in
[part 07](07-compiled-documents.md) § 10. Refusal and failure sentence wording is left to the build
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979794633)).
The sentences it **records**, word for word:

- *skipped: <printed condition> is false* (a decision's reason, § 5)
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6957680891)).
- *case '<id>': arm for <value> does nothing* (a `case` whose subject took an explicitly empty
  branch, [part 04](04-loops-and-functions.md) § 5.2) ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)).
- *not run: beyond max <n>* (an item past a loop's `max`; the loop's `.stopped` is `"bound"`)
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6963556118)).
- *held: empty* (a deliverable with `whenEmpty: hold`)
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1406)).
- *released: check '<id>' didn't apply (its if was false)* (a deliverable whose gating check was
  skipped) ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6978700251)).
- *not sent: <kind> isn't set up for this account* (an unconfigured delivery kind), for example
  *not sent: EMR isn't set up for this account*
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1408#issuecomment-6881816664)).
- History's banner, built from the record: *<deliverable label> was not sent: check '<check id>' failed (<detail>).*,
  for example *Consultation note was not sent: check 'coverage' failed (10 of 12 terms present).*
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6881869311)).
- History, for a sent format with keywords removed: *Not sent to the model: <pointers>*
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466)).
- A node's `invalid.reason` for the macro check: *macro <id>: missing*, *macro <id>: repeated*,
  *macro <id>: altered*; and the failure when `onInvalid` is `fail`:
  *Node '<row key>' failed: macro '<id>' was <missing | repeated | altered> in the reply.* **Proposed.**
- A deliverable failed by `whenEmpty: fail`: *Deliverable '<id>' was empty, and the workflow fails an empty <label>.* **Proposed.**

## Proposed in this part

These are left to the build: each build PR lists the ones it settles, and all are confirmed before the contracts release.

- **Member names `notSent` and `maxAgentCalls`** (§ 3, § 4): the content is decided (the removed keywords as JSON pointers; the ceiling recorded once in the header), the names aren't.
- **The members of `invalid` and of a `retries` entry** (§ 4): the member names are decided, their objects' own members aren't.
- **The `onInvalid` macro failure and `whenEmpty: fail` sentences.**
