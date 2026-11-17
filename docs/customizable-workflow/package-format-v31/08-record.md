# 08. The record: storage 12 and the provenance contract

This part defines the record a version 31 job writes: **record storage 12**, and the new
**provenance contract** version that publishes it. It collects the record side of every Milestone 39
issue: qualified keys (#1384); `resolveHash`, `lockHash`, the exact versions that ran, run facts,
run spots and extraction versions (#1389, #1390, #1425); renderer sub-rows (#1397); macro rows
(#1412); a row for every condition decision; check rows and check loops (#1395, #1396); call rows
(#1419); deliverable rows and `run.date` (#1406); profile reads and the signature block (#1408);
agent definitions and bindings; and what stays out (test results, #1449). The decisions of
2026-11-17 are on [#1388](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6957680891).

Names are written in the record's JSON form (camelCase, as `provenance-record.md` writes them); the
C# properties on `ConsultGenerationJobState` and `ConsultNodeOutputState` are their PascalCase
twins.

## 1. Storage 12

**Storage 12 is storage 11 plus additions, except that keys become qualified keys**
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6957680891)).

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
  computed` as before.
- **The record holds hashes, ids, counts, reasons and names, never element values** (#369's rule),
  with the exceptions storage 11 made (an item name drawn from a field) and the ones this part adds:
  run facts and run spot outputs (§ 8), and printed conditions (§ 5), which are package text.

## 2. Keys

### 2.1 Qualified keys everywhere

Wherever storage 11 names a node by its bare id, storage 12 names it by its **qualified key**: the
ids of its enclosing flow folders and its own, joined by `.`. A top-level node's key is just its id,
so simple packages look the same
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1384#issuecomment-6904190578)).

```
plan                                     a top-level node
parts.paragraphs.draft_paragraph         a node two loops down
hpi_section.standard_draft               a node inside a function, through its call node
standards.section.standard_draft         a call node inside a loop, then the function's node
```

**A row key** is the qualified key, then `:` and the item path for an instance inside a loop or a
`while` (v30's path grammar, unchanged): `parts.paragraphs.draft_paragraph:2/1`, `refine.judge:3`,
`standards.section.standard_draft:3`. `.` separates scopes, `/` separates path segments and `:`
separates the key from the path; no id or item id contains any of the three.

**Function calls:** inner rows are keyed through the call node's qualified key, never the function's
name. The function's name is on the call's row (§ 7). Two calls to the same function get different
keys from their call nodes ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6897717122)).

**What moves to qualified keys:**
- `nodeOutputs` keys and each row's `nodeId`;
- block ids, `skippedBlocks`, `blocks`;
- item-step and block events, `itemProgress`, `collections`;
- the concept source, `nodes` (the descriptor rows), `loopInstances`, `unresolvedLoops`;
- the run diagram's Mermaid ids, the rail, History and Editor links (part 09).

### 2.2 Separate sections, separate key spaces

**Checks are keyed in their own section** (§ 6), with keys rooted at `check.`:
`check.coverage`, `check.part_coverage.coverage:2`. Node keys carry no root. A node and a check may
share an id because they live in different maps, so a node key and a check key never collide
([#1395](https://github.com/Consultologist/Consultologist-Blazor/issues/1395), [#1396](https://github.com/Consultologist/Consultologist-Blazor/issues/1396#issuecomment-6905675034)).
Deliverables are keyed by id in their own list, as at 11.

### 2.3 Sub-row ids

Some rows have sub-rows that live **inside** their row, not in `nodeOutputs`. Each has an id for
History's links, the lineage view (#1414) and test expectations, formed as
`<row key>/<sub kind>:<n or id>`. A sub-row id is never parsed as a row key; a segment holding `:` is
always a sub-row, since item ids never contain `:`.

| Sub-row | Id | Status |
|---|---|---|
| a renderer use | `<row key>/render:<n>`, `n` its 1-based position in the body: `identify_problem/render:1`, `parts.paragraphs.draft_paragraph:2/1/render:1` | the top-level form is [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6957680891); inside a loop, the item path then the render site, [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1397#issuecomment-6959888097) |
| a macro row | `<row key>/macro:<qualified macro id>`: `assessment/macro:letrozole_prescription_text` | **Proposed** |
| a run spot | `<row key>/spot:<site>` | **Proposed** |
| a decision | `<owner key>/if:<site>` (§ 5.2) | **Proposed** |

## 3. The job header

New members at the job level, all **write-once at job start**, beside the package name and
version, and shown in History's job header:

| Member | Type | Null | Meaning |
|---|---|---|---|
| `resolveHash` | string (`sha256:…`) | null below 31 | the SHA-256 of the canonical `resolve.json` bytes the job ran ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6898433175)) |
| `lockHash` | string (`sha256:…`) | null below 31 | the SHA-256 of the canonical `lock.json` bytes ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1390#issuecomment-6898433402)) |
| `versionsRan` | object | null below 31 | **the exact versions that actually ran,** which under option c can differ from the lock within its range ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1390#issuecomment-6904042632)). Members as `lock.json`'s: `packageFormat`, `dialect`, `libraries` (`Scriban`, `Markdig`, `Corvus.Text.Json.JsonLogic`, `Corvus.JsonSchema`), `evaluator`, `operations` (those the package uses), `builtins`, `culture` (`"invariant"`). **Proposed** name and shape |
| `runFacts` | object | null below 31 | the raw value of each run fact the package reads, once per run (§ 8.1); `{}` when it reads none |
| `signatureBlockId` | string | allowed | the profile signature block the job used, by **id**, never its text; `null` when the package doesn't read `profile.signature` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1408#issuecomment-6904843222)) |
| `signatureChosen` | `"default"` or `"job"` | allowed | whether the block was the account's chosen block (`ChosenId`) or picked on the Create page for this job; `null` with `signatureBlockId`. **Proposed** |

**Unchanged and still recorded:** `packageSpecVersion` (31), `workflowPackage`, `packageFormatRef`,
`provenanceRef`, `formatsRef`, `catalogRef`, `engineCommit`, `engineRelease`, `apiHost`, the
terminology snapshot. `formatsRef` and the agents catalogue version now also appear in `lockHash`'s
document; the header keeps them for readers of older records.

**Retired at 12** (never written by a 31 job; read as today on older records): `macroChoices` and
`excludedMacros`, since per-run macro choices are optional boolean inputs at 31 and their effect is
recorded as decisions (§ 5) ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1412)), and
`classifications`, since every classifier is an ordinary agent node with its own row. **Proposed.**

### 3.1 Inputs: extraction versions

Each **document-filled input**, and each document element of a mixed array, records beside its hash
and origin ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6924904289)):

| Member (on the input origin) | Type | Null | Meaning |
|---|---|---|---|
| `converter` | object `{ name: string, version: string }` | allowed | the document-to-Markdown converter and its version; `null` for a typed input |
| `parser` | object `{ name: string, version: string }` | allowed | the parsing library and its version (PdfPig, DocumentFormat.OpenXml, …) |
| `ocr` | object `{ service: string, model: string }` | allowed | the OCR service and model version, when OCR ran (#239); `null` when it didn't |

**Proposed** member names. They join today's `kind`, `extractor`, `pageCount`, `fileSha256` and
`textSha256`; `extractor` stays for older records. #1438 (Milestone 40) builds on them.

## 4. Node rows

A production node instance's row keeps every storage 11 member (`label`, `status`, `inputHash`,
`outputHash`, `hashVersion`, `tokens`, `validation`, `reason`, the clock and failure members, …) and
gains:

| Member | Type | Null | Meaning |
|---|---|---|---|
| `nodeId` | string | never | now the **qualified key** |
| `kind` | string | never | `agent`, `rendered`, `computed`, `for`, `while`, `if`, `case` or `call` |
| `formatHash` | string | allowed | the hash of the node's resolved format (`resolve.json`'s `formatHashes`); `null` for flow kinds. With `agent`, it lets History say "the same type, served by a different agent" or the reverse ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6879413099)) |
| `agent` | object | allowed | on agent rows only (§ 4.1); `null` otherwise |
| `itemName` | string | allowed | the item's name from the loop's `itemLabel`, capped at 120 characters, kept for the life of the record (storage 11's rule, renamed from `labelField`); `null` on rows outside a loop |
| `runDependent` | boolean | never | `true` when the row's value depends on `run.*`, transitively ([#1386](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6925165983)) |
| `runNeutralHash` | string | allowed | for a body with run spots, the hash of the body rendered with each run spot left as its marker; `null` when the body has none, so `inputHash` alone is the hash (§ 8.2). **Proposed:** null rather than a repeat |
| `runSpots` | array of run spot | never | § 8.2; `[]` for none |
| `renders` | array of renderer sub-row | never | § 4.2; `[]` for none |
| `macros` | array of macro row | never | § 4.3; `[]` for none; agent rows only |
| `invalid` | object | allowed | the node's `onInvalid` outcome when a reply failed validation or the macro marker check: `{ reason: string, action: "retry" \| "fail", attempts: integer }`, where `reason` names what failed, for example `macro letrozole_prescription_text: missing`. `null` when the reply was valid. **Proposed** shape; that the outcome sits on the node's row is [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1412#issuecomment-6957666676) |
| `retries` | array of `{ atUtc: string, waitMs: integer }` | never | each 429 (capacity) retry the call needed, with its back-off ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1393)); `[]` for none. **Proposed** shape |
| `profileReads` | array of `{ field: string, hash: string }` | never | each `profile.*` read by a macro body placed in this node's prompt, by hash (§ 9); `[]` for none |

For a `computed` row, `inputHash` covers the expression and the values it read. For a run-dependent
`computed` value, the run-neutral hash covers its expression and its other reads, **not its value**
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6925165983)).

### 4.1 The agent: definition and binding

Agent rows **name their attested agent definition** (id and version) **and the binding that chose
it** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6957680891)):

| Member of `agent` | Type | Null | Meaning |
|---|---|---|---|
| `name` | string | never | the attested agent definition's id in the agents catalogue (`consult-prose`) |
| `version` | integer | never | its version (`4`) |
| `binding` | object `{ format: string, via: "format" \| "producer" }` | never | the format whose binding chose the agent, and whether it came from `bindings.json`'s `formats` or its `producers` (`resolve.json`'s `bindings`, part 07) |

The agents catalogue's version is in `lock.json`; the record doesn't repeat it per row. **The fuller
recording** (each value's model, the settings read from the attested definition, tool calls) is
Milestone 40: #1439 and #1445. **Proposed:** member names.

### 4.2 Renderer sub-rows

**One sub-row per pipe use,** in the order the uses appear in the body, per instance (per item and
pass), inside the consuming row's `renders`. It's never a node row and never scheduled
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1397#issuecomment-6877988508)):

| Member | Type | Null | Meaning |
|---|---|---|---|
| `id` | string | never | `<row key>/render:<n>` (§ 2.3) |
| `renderer` | string | never | `renderer.<id>` |
| `templateHash` | string | never | the renderer's `bodyHash` in `resolve.json` |
| `source` | string | never | the path piped in (`node.extract.value`) |
| `inputHash` | string | never | the hash of the piped value (canonical JSON for data, UTF-8 for text) |
| `inputSize` | string | allowed | a size summary from the input's format, for example `"14 concepts"`; `null` when none applies. **Proposed** |
| `outputHash` | string | never | the hash of the rendered text |
| `outputLength` | integer | never | the rendered text's length in characters |

The input value and rendered text are produced text, under the 7-day retention (§ 11).

### 4.3 Macro rows

**A macro row per macro per node instance,** for every macro the node's prompt places
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1412#issuecomment-6957666676)):

| Member | Type | Null | Meaning |
|---|---|---|---|
| `id` | string | never | `<row key>/macro:<qualified macro id>` (§ 2.3) |
| `macro` | string | never | `macro.<qualified id>` |
| `offered` | boolean | never | whether the macro's `if` (and its folder's) held, so its instruction was added to the prompt |
| `decisions` | array of string | never | the decision ids (§ 5) its offer depended on: the macro's `if` and its folder's, when they're not `null`; `[]` when both are `null` |
| `classifier` | string | allowed | the row key of the classifier node its `if` read, linking to that row; `null` when it read none |
| `placed` | boolean | never | `true` when the marker was found **exactly once**, in the form its instruction asked for; `false` when not offered, or when the check failed |
| `form` | `"block"` or `"inline"` | allowed | the form the instruction asked for; `null` when not offered. **Proposed** |
| `contentHash` | string | allowed | the hash of the macro's rendered body as substituted; `null` when not offered |

If the marker was missing, repeated or altered, the node's `onInvalid` outcome is on the node's row
(`invalid`), not here. Profile reads in macro bodies are the node's `profileReads`. **The same macro
used twice in one template** is #1457 (Milestone 40). **Proposed:** member names other than
`offered` and `placed`.

## 5. Condition decision rows

### 5.1 The row

**A row for every condition decision:** the printed condition, its result, a hash of each value it
read, and whether it was run-dependent
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6957680891)).
Decisions sit in a job-level list, `decisions`, each linking to the row it decided. **Proposed:** a
job-level list rather than a member of each row, since a decision can concern a row that never
exists (a skipped node).

| Member | Type | Null | Meaning |
|---|---|---|---|
| `id` | string | never | `<owner key>/if:<site>` (§ 5.2) |
| `owner` | string | never | the row key, check key or `deliverable.<id>` the decision concerns |
| `site` | string | never | what was decided (§ 5.2) |
| `condition` | string | never | the condition **as printed** by the one printer ([part 03](03-expressions.md)): `input.kind = "table"`. Package text, kept for the life of the record |
| `expressionHash` | string | never | the hash of the canonical JSON Logic, which the printed text came from. **Proposed** |
| `result` | boolean or string | never | `true` or `false`; for a `case`, the id of the branch taken, or `"else"` |
| `reads` | array of `{ path: string, hash: string }` | never | each value it read, by hash; never the value itself |
| `runDependent` | boolean | never | `true` when it read `run.*` or a run-dependent value |
| `decidedAtUtc` | string | never | when the engine evaluated it |

**Reasons read as sentences** built from the printed condition: *skipped: input.kind = "table" is
false*. A row skipped by a decision keeps storage 11's `reason` member, with this sentence, and the
decision's id is how History links the two.

### 5.2 Which decisions, and their sites

Every condition the engine evaluates is recorded, per instance. **Proposed:** the site names.

| Condition | `site` | Owner |
|---|---|---|
| a node's, check's, branch's or loop's header `if` | `self` | its row key |
| a `case` | `case` (result: the branch id) | its row key |
| a `while`'s condition, before or after pass *n* | `pass.<n>` | the `while`'s row key |
| a `for`'s `until`, after item *i* | `until.<i>` | the loop's row key |
| an item's own `if` (a snippet folder item's header) | `item.<i>` | the loop's row key |
| a macro's `if` | `macro.<qualified id>` | the placing node's row key |
| a macro folder's `if` (both must hold; the record shows which was false) | `macro-folder.<qualified folder id>` | the placing node's row key |
| a part's `if` | `part.<id>` | the node's row key |
| a snippet's header `if` | `snippet.<qualified id>` | the placing row key |
| a deliverable's header `if` | `self` | `deliverable.<id>` |

A body `if` in a template tests recorded facts only (loop position, `state`, whether a check passed),
which are already rows; it makes no decision row.

## 6. Check rows

**Checks have their own section,** `checks`, apart from `nodeOutputs`, keyed by check key (§ 2.2),
in the order the job runs: Inputs, Nodes, Checks, Deliverables
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6881869311)).
The record holds a check's result once; a deliverable refers to it.

| Member | Type | Null | Meaning |
|---|---|---|---|
| `key` | string | never | `check.coverage`, `check.part_coverage.coverage:2` |
| `kind` | string | never | `computed`, or `for` for a check loop's own row |
| `label` | string | never | the check's label |
| `status` | string | never | `Completed`, `Skipped`, `Failed`, as node rows |
| `itemName` | string | allowed | in a check loop, the item's name from the loop's `itemLabel`, capped at 120 |
| `alignedWith` | string | allowed | in a check loop, the row key of the production item it was paired with by identity (`parts:2`); `null` at the top level |
| `checked` | object `{ ref: string, hash: string }` | allowed | what it ran on, with that output's hash: `deliverable.consult` with its composed hash, or `node.plan` with its output hash; `null` when it ran on several reads equally. **Proposed** |
| `reads` | array of `{ path: string, hash: string }` | never | its other inputs, by hash |
| `inputHash`, `outputHash` | string | allowed | as on a `computed` row |
| `passed` | boolean | allowed | the `check_result`'s verdict; kept for the life of the record, as today's `checkPassed`; `null` while running or when skipped |
| `failWith` | string | allowed | the operation's literal `failWith` sentence when it failed (package text); `null` when it passed |
| `effect` | string | allowed | what it caused: `released`, `held`, `not sent`, or `read by <ref>`; `null` until known |
| `count` | integer | allowed | on a check loop's own row, its item count; `null` on others |

The `check_result`'s detail (which terms were missing, for example) is produced text: shown while
retained, then hashes and sizes (§ 11). **Proposed:** member names other than `passed`, the
section, and the key form.

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
| `depth` | integer | never | the call's nesting depth (1 for a call at the top level), within `FormatBounds.depth`. **Proposed** |

Inner rows are keyed through the call node (§ 2.1). A decision made outside and passed in is
recorded once, at the caller; the `computed` node inside each call shows the value it received.

## 8. Run facts and run spots

### 8.1 Run facts

**Recorded once per run, kept permanently, shown in History's "Run facts" section**
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6920512005)).
`runFacts` holds the raw value of each fact the package reads (`resolve.json`'s `run`):

| Member | Type | Meaning |
|---|---|---|
| `date` | object `{ value: string, timeZone: string }` | the job's start date in the account's time zone (`"2026-11-16"`) and that zone's IANA name (`"America/Toronto"`), recorded beside the value ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1406#issuecomment-6920512477)) |
| `id` | string | the job's id |
| `package` | string | the package name and version, as `run.package` gives it |
| `engine` | string | the engine version, as `run.engine` gives it |

Only the facts read are present. Run facts aren't patient text, so they're outside the 7-day drop.

### 8.2 Run spots and the two hashes

For a body that reads `run.*` (a prompt, a `rendered` node, a deliverable, a snippet or macro body),
the record carries **the sent-text hash** (the main hash, as today), **the run-neutral hash** (the
same text with each run spot left as a marker), and **each run spot**
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6920426229),
[#1425](https://github.com/Consultologist/Consultologist-Blazor/issues/1425)). A read of a run-dependent value is a
run spot too.

| Member of a run spot | Type | Null | Meaning |
|---|---|---|---|
| `id` | string | never | `<row key>/spot:<site>` |
| `site` | integer | never | the spot's position in the body (`resolve.json`'s `runSpots`) |
| `reads` | string | never | what it reads (`run.date`, or a run-dependent value's path) |
| `valueHash` | string | never | the hash of the value read |
| `output` | string | never | the formatted output (`"16 November 2026"`), **kept permanently** |
| `outputHash` | string | never | the output's hash |

- **The sent text is rebuilt exactly** from the run-neutral text and the spot outputs, by literal
  substitution, so the main hash stays provable.
- **Runs are compared on the run-neutral hash.** Beside any body whose two hashes differ, History says
  the difference is due to run facts.
- **A body with no run spots has one hash;** its `runNeutralHash` is `null` (**Proposed**).
- Deliverables carry the same members on their row (§ 10).

## 9. Profile reads

Each `profile.*` read is **a substitution recorded by hash**, and its text follows the 7-day
retention; the record names the **signature block's id**, not its text
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1408#issuecomment-6904843222)).
Reads happen only in deliverable templates, macro bodies and snippet bodies placed in them, so they
appear in a deliverable's `substitutions` (§ 10) and a node's `profileReads` (§ 4). The header's
`signatureBlockId` (§ 3) names the block. Today's `ConsultSignatureSnapshot` (id, text, as-of) is not
written by a 31 job: the text is a read like any other. **Proposed.**

## 10. Deliverable rows

A deliverable's row keeps storage 11's members (its id, state, `composedHash`) and adds the
template's provenance ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1406)):

| Member | Type | Null | Meaning |
|---|---|---|---|
| `templateHash` | string | never | the template's `bodyHash` in `resolve.json`; the text is shown from the pinned version, never copied |
| `format` | string | never | `markdown_block` or `plaintext` |
| `state` | string | never | `released`, `held`, `failed`, `skipped` (its header `if`) or `not sent` |
| `stateReason` | string | allowed | the sentence: *held: empty*, *skipped: <condition> is false*, *not sent: EMR isn't set up for this account*; `null` when released. **Proposed** name |
| `check` | string | allowed | the check key that gated it, linking to the check's row; `null` when ungated |
| `sites` | array of `{ site: integer, at: string, expr: string, loop: string \| null }` | never | each expression in the template, in source order, with its line and column. Package text, kept |
| `substitutions` | array of substitution | never | each evaluation: `{ site, pass: integer[] \| null, reads: string, hash: string, size: integer, renders: string \| null }`; `pass` holds one number per enclosing template loop |
| `renders` | array of renderer sub-row | never | as on node rows (§ 4.2) |
| `runNeutralHash`, `runSpots` | | | as on node rows (§ 8.2) |

**Replay:** substituting the recorded values into the template, in site and pass order, must
reproduce `composedHash`. A deliverable row shows only a one-line check status; the check's row
holds the result.

## 11. Retention

| Kept for the life of the record | Deleted 7 days after completion (#368) |
|---|---|
| every hash, key, id, status, count, clock and token figure | produced text: replies, prompts as sent, composed deliverables |
| labels, item names (from `itemLabel`, capped at 120) | concepts |
| printed conditions, `failWith` sentences, template sites (package text) | input text and held inputs |
| decision results, check verdicts (`passed`), states and reasons | renderer inputs and outputs, `check_result` details |
| run facts and run spot outputs | the text behind profile reads |
| `resolveHash`, `lockHash`, `versionsRan`, extraction versions | |
| the signature block's id | |

The sweep (`DropText`) deletes what the right-hand column names and stamps `textDroppedAtUtc`, as
today. Every hash stays, so a sub-row or substitution keeps its reference, format, size and hash and
says the text was deleted under retention.

## 12. What the record never holds

- **In-package test results.** Tests run at publish, not in jobs; their results are in the publish
  report (part 07) ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1449#issuecomment-6957669037)).
- **`resolve.json` or `lock.json` themselves.** The record pins their hashes; History fetches the
  documents from the registry by version and checks them against the hashes.
- **Element values,** apart from the exceptions in § 1.

## 13. The provenance contract

**A new provenance contract version** (`provenance@v2026.12.N`, **Proposed** number) is released
**first**, with the format spec and the agents catalogue (index § 5, step 1), and the engine stamps
it as `provenanceRef` on every 31 job. It changes:

- **`provenance-versions.json`:** `recordStorageVersions` gains `12`, pinned against
  `StorageVersions` by test as today.
- **`provenance-record.md`:** a storage 12 section with every member of this part, its type and null
  rule, and the statement that storage 2–11 records read as before.
- **`hash-definitions.md`:** the definitions 31 adds. **Proposed**, since no issue numbers them:
  - `nodeHashVersions` gains **7**: the sent-text hash over the rendered body with run spots and
    macro markers substituted, and the run-neutral hash beside it;
  - `effectiveInputHashVersions` gains **7**: inputs as the object keyed by id, with Markdown inputs
    in their converted form;
  - `workflowOutputHashVersions` gains **4**: a deliverable composed from its template, `composedHash`
    as replayed in § 10;
  - the `resolveHash` and `lockHash` definitions: SHA-256 over RFC 8785 bytes.
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
                   "builtins": { "formats": "1.0.0", "markdown_escape": "1.0.0", "plain_conversion": "1.0.0" },
                   "culture": "invariant" },
  "runFacts": { "date": { "value": "2026-11-16", "timeZone": "America/Toronto" } },
  "signatureBlockId": "sig-2", "signatureChosen": "job",
  "decisions": [
    { "id": "standards.assessment:7/if:macro.letrozole_prescription_text",
      "owner": "standards.assessment:7", "site": "macro.letrozole_prescription_text",
      "condition": "node.prescribes_letrozole.value = true", "expressionHash": "sha256:…",
      "result": true, "reads": [ { "path": "node.prescribes_letrozole.value", "hash": "sha256:…" } ],
      "runDependent": false, "decidedAtUtc": "2026-11-16T14:02:11Z" } ],
  "nodeOutputs": {
    "standards.assessment:7": {
      "nodeId": "standards.assessment", "itemId": "7", "kind": "agent", "itemName": "Assessment and plan",
      "status": "Completed", "inputHash": "sha256:…", "outputHash": "sha256:…", "hashVersion": 7,
      "formatHash": "sha256:…",
      "agent": { "name": "consult-prose", "version": 4,
                 "binding": { "format": "agent_markdown_block", "via": "format" } },
      "runDependent": false, "runNeutralHash": null, "runSpots": [],
      "renders": [ { "id": "standards.assessment:7/render:1", "renderer": "renderer.concept_bullets",
                     "templateHash": "sha256:b81…", "source": "node.extract.value",
                     "inputHash": "sha256:7d0…", "inputSize": "14 concepts",
                     "outputHash": "sha256:3f9…", "outputLength": 612 } ],
      "macros": [ { "id": "standards.assessment:7/macro:letrozole_prescription_text",
                    "macro": "macro.letrozole_prescription_text", "offered": true,
                    "decisions": ["standards.assessment:7/if:macro.letrozole_prescription_text"],
                    "classifier": "standards.prescribes_letrozole:7", "placed": true, "form": "block",
                    "contentHash": "sha256:…" } ],
      "invalid": null, "retries": [], "profileReads": []
    }
  },
  "checks": {
    "check.coverage": { "key": "check.coverage", "kind": "computed", "label": "Coverage",
                        "status": "Completed", "itemName": null, "alignedWith": null,
                        "checked": { "ref": "deliverable.consult", "hash": "sha256:9a3f…" },
                        "reads": [ { "path": "node.extract_input_terms.value", "hash": "sha256:…" } ],
                        "passed": true, "failWith": null, "effect": "released", "count": null } }
}
```

## Refusals and recorded sentences

The record refuses nothing itself; the refusals at start and load are in
[part 07](07-compiled-documents.md) § 10. The sentences it **records**, word for word:

- *skipped: <printed condition> is false* (a decision's reason, § 5)
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6957680891)).
- *held: empty* (a deliverable with `whenEmpty: hold`)
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1406)).
- *not sent: <kind> isn't set up for this account* (an unconfigured delivery kind), for example
  *not sent: EMR isn't set up for this account*
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1408#issuecomment-6881816664)).
- History's banner, built from the record: *<deliverable label> was not sent: check '<check id>' failed (<detail>).*,
  for example *Consultation note was not sent: check 'coverage' failed (10 of 12 terms present).*
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6881869311)).
- A node's `invalid.reason` for the macro check: *macro <id>: missing*, *macro <id>: repeated*,
  *macro <id>: altered*; and the failure when `onInvalid` is `fail`:
  *Node '<row key>' failed: macro '<id>' was <missing | repeated | altered> in the reply.* **Proposed.**
- A deliverable failed by `whenEmpty: fail`: *Deliverable '<id>' was empty, and the workflow fails an empty <label>.* **Proposed.**

## Proposed in this part

- **Sub-row ids** `/macro:<id>`, `/spot:<site>` and `/if:<site>`: the renderer's form (decided), used for every sub-row, unambiguous since item ids never hold `:`.
- **`versionsRan`** as the name and shape of the exact versions that ran: mirrors `lock.json`, so the two compare member by member.
- **`signatureChosen`:** the Create page's per-job choice is a recorded fact.
- **Retiring `macroChoices`, `excludedMacros` and `classifications` at 12:** their content is now inputs, decisions and agent rows.
- **Extraction members `converter`, `parser`, `ocr`:** the content is decided, the names aren't.
- **`runNeutralHash` is `null` when a body has no run spots:** the issue left "repeat or omit" to the record design.
- **Member names** `kind`, `formatHash`, `agent { name, version, binding { format, via } }`, `invalid`, `retries`, `profileReads`, `inputSize`, `form`, `decisions`, `classifier`, `checked`, `alignedWith`, `failWith`, `effect`, `count`, `depth`, `arguments`, `stateReason`: the issues decide the content, not the names.
- **Decisions in a job-level `decisions` list,** with the site names of § 5.2 and an `expressionHash`: a decision can concern a row that never exists.
- **`ConsultSignatureSnapshot` not written at 31:** the signature text is a profile read by hash.
- **The provenance contract's number and hash ladder steps** (node 7, effective input 7, workflow output 4): no issue numbers them.
- **The `onInvalid` macro failure and `whenEmpty: fail` sentences.**
