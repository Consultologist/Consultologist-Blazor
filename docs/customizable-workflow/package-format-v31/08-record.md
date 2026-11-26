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
  with the exceptions storage 11 made (an item name, now only when it comes from package text) and
  the ones this part adds: run facts and the outputs of run spots that read raw `run.*` facts (§ 8),
  and printed conditions (§ 5), which are package text.

### 1.1 Where the record is stored

At storage 11 the record lives in the job's Durable entity state, with no size guard. **At 12 it
moves** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)):

- **Each record row is one table row,** written once when the row finishes and never rewritten.
  The partition key is the job id; the row key is the rooted row key (§ 2.1) escaped for Azure
  Tables, with the true key kept as a property (`key`). Node, check, call, deliverable and decision
  rows are all stored this way; sub-rows (§ 2.3) stay inside their row.
- **Retained text is never in a row** (decided in the build, #1506). Every text § 11's right-hand
  column names lives in the job's text blobs, beside the record. A row member that would hold such a
  text holds `null` and its hash instead (`itemName` with `itemNameHash`, § 4; a run spot's `output`,
  § 8.2). So the sweep deletes blobs and never rewrites a row, and `recordHash` can be proved for the
  record's whole life.
- **Where each thing is stored** (decided in the build, #1506):
  - **Rows** are in the table `ConsultGenerationRecordRows`: partition key the job id, row key the
    escaped row key, and the properties `key`, `section` (`node`, `check`, `deliverable`,
    `delivery` or `decision`), `row` (the row's canonical JSON, continued in `row1`, `row2`… past
    30,000 characters) and `hash`.
  - **Retained text** is in the blobs `{appUserId}/{jobId}/text/{sha256 hex of the key}.{part}.json`
    in the account's outputs container, `part` being `produced` (written by the activity that made
    the text) or `settled` (written with the row).
  - **The canonical record document** is the blob `{appUserId}/{jobId}/record.json` in the same
    container, which the sweep never deletes. It is a materialisation of the rows under the header: it may be
    assembled again from them (when the blob is gone), and is kept only if it hashes to `recordHash` (decided in the
    build, #1506).
- **A row write is insert-if-absent,** keyed by job and rooted key ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6993822677)): an identical second
  write (a Durable replay) is a no-op; a differing one is refused and recorded as an engine fault:
  the first row stays, the job fails with A06-010, and its record is sealed with the first row. A row
  larger than a table row holds (1 MiB) is not written; the other rows of its round are, and the job
  fails with A09-002, naming the row by its key (decided in the build, #1506; [part 09](09-engine-and-pages.md) § 9).
- **A job that has finished producing and has deliveries to make is `delivering`** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6994467727)),
  shown as *"Delivering…"*, between producing and its terminal state. The document is viewable
  throughout. Each delivery attempt is a row in the same record, keyed
  `delivery:<deliverable>/<destination>` (§ 10), so nothing is recorded on a deliverable's row
  after the job completes.
- **The record is sealed when delivery ends,** not before. At every terminal state (completed,
  failed, cancelled or timed out; [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6993822677)), **a canonical
  record document** is assembled from the rows into a blob: the job
  header (§ 3), noting the end state, and every row, in key order, as RFC 8785 JSON. It is what History's download and the
  export serve, and **one hash over the whole record** (`recordHash`) is taken over its bytes and
  written on the header.
- **The Durable entity keeps only the job header and progress** (counts, the current step), never
  row contents. **Events carry keys, not row contents:** a browser following a job fetches a row by
  its key once the event says it finished.
- **Rows don't record a build.** Every deploy is preceded by a drain (part 09 § 3.6), so no job
  spans two builds; the header's `engineCommit` and `engineRelease` hold for every row.
- **Sealing is one idempotent operation** (decided in the build, #1506), reached whichever way a job
  ends: its orchestration's last step, a cancel, the retention sweep before it drops text, or the
  first read of a terminal job that has no `recordHash`. Sealing twice gives the same document and
  the same hash, and the first hash written is the one kept.
- **A row key too long, or holding a character a table refuses, is kept whole as the `key`
  property;** two different keys never share a stored row key.

### 1.2 The canonical record document

The sealed record is one JSON document, written as RFC 8785 canonical JSON in UTF-8 without a byte
order mark. It holds the header (§ 3) as its first members, then four more:

| Member | Type | Meaning |
|---|---|---|
| `decisions` | array of decision | every decision row (§ 5), in ordinal order of its `id` |
| `nodeOutputs` | object | every node row (§ 4), keyed by its row key (§ 2.1) |
| `checks` | object | every check row (§ 6), keyed by its check row key |
| `deliverables` | array of deliverable row | every deliverable row (§ 10), in ordinal order of its `key`, each with one more member, `deliveries`: the delivery rows of that deliverable, in ordinal order of their `key`; `[]` for none |

The document **leaves out** what changes after the end of the job, so it can be hashed once:
`recordHash` itself, `textDroppedAtUtc`, and History's event log (`history`); and it never holds
retained text (§ 11). The header notes the end state: `status` (`Completed`, `Failed` or
`Cancelled`), `completedAtUtc` and `error`.

**`recordHash`** is `sha256:` and the lowercase hex of the SHA-256 over the document's UTF-8 bytes.
It is written once on the job header and never changes, before or after the text drop, since the
document holds no retained text. Rows stored twice with the same bytes are one row. A second,
different row under a key already stored is refused: the first row stays, the job fails as an engine
fault (A06-010), and its record is sealed with the first row. **Once the job is terminal its header
never changes** (decided in the build, #1506): no later signal (a cancel, a second finalize, a late
header write) writes a member the header holds, so a document assembled again from the rows hashes to
the same `recordHash`.
- **Built under #1490** (record storage 12).

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

**A row key** is the rooted key, then `:` and the ids of the items the instance sits in, from the
outermost loop inward, joined by `/`; and `#<n>` for a pass of a `while`, written where the `while`
sits in that path ([ruled by the owner](https://github.com/Consultologist/Consultologist-Blazor/issues/1506#issuecomment-7064144456), ruling 2; area 06's row shapes are the contract):
`node.sections.paragraphs.draft_paragraph:2/1`, `node.refine.judge#3` (the third pass of `refine`),
`node.refine.sections#2:3` (item 3 of the second pass),
`node.standards.section.standard_draft:3`, `check.part_coverage.coverage:2`. `.` separates scopes,
`/` separates item ids, `:` separates the key from the path and `#` marks a pass; **no id or item id
contains any of the four,** and item paths never contain `:` or `#`.

**Item ids are 1-based everywhere a person sees them** ([ruled by the owner](https://github.com/Consultologist/Consultologist-Blazor/issues/1506#issuecomment-7064144456), ruling 2): a loop's items by
position, array inputs included (an array input's first element is item `1`, as the other loops'
items always were).

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
| a renderer use | `<row key>/render:<n>`, `n` its 1-based number among the renderer uses of the whole entry, counted across every unit its text could place (the prompt, its parts, its snippets and macros), placed or not, so the ids of one row may have gaps ([ruled by the owner](https://github.com/Consultologist/Consultologist-Blazor/issues/1506#issuecomment-7073314482), ruling 6): `node.identify_problem/render:1`, `node.sections.paragraphs.draft_paragraph:2/1/render:1` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6957680891), [in a loop](https://github.com/Consultologist/Consultologist-Blazor/issues/1397#issuecomment-6959888097)) |
| a macro row | `<row key>/macro:<macro id>`: `node.assessment/macro:letrozole_prescription_text` |
| a run spot | `<row key>/spot:<n>`, `n` its 1-based order of filling |
| a decision | `<owner key>/if:<site>` (§ 5.2): `node.standards.assessment:7/if:macro.letrozole_prescription_text` |

**A renderer use or a run spot inside a template `for` repeats per iteration** (decided in the build,
#1506). Each sub-row then carries `pass`, an array of integers or `null`: one number per enclosing
template loop, 1-based, as a substitution's (§ 10). `render:<n>`'s `<n>` stays the use's number in
the entry, and a run spot's id is `<row key>/spot:<n>` with `<n>` the 1-based order in which the
spots were filled, its `site` staying the body's site number. An id and its `pass` name one
sub-row.

## 3. The job header

New members at the job level, all **write-once at job start**, beside the package name and
version, and shown in History's job header:

| Member | Type | Null | Meaning |
|---|---|---|---|
| `resolveHash` | string (`sha256:…`) | null below 31 | the SHA-256 of the canonical `resolve.json` bytes the job ran ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6898433175)) |
| `lockHash` | string (`sha256:…`) | null below 31 | the SHA-256 of the canonical `lock.json` bytes ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1390#issuecomment-6898433402)) |
| `versionsRan` | object | null below 31 | **the exact versions that actually ran,** which under option c can differ from the lock within its range ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1390#issuecomment-6904042632)). It is `lock.json`'s **nested shape minus `documentVersion`, `package` and `resolve`** (the record already holds their hashes) ([ruled by the owner](https://github.com/Consultologist/Consultologist-Blazor/issues/1506#issuecomment-7064144456), ruling 3), so the two compare member by member: `{ resolver: { packageFormat, dialect }, libraries, evaluator, operations, builtins, culture, runtime: { dotnet, unicode }, catalogues: { agents, formats, outputContracts } }`. `libraries` holds `Scriban`, `Markdig`, `Corvus.Text.Json.JsonLogic` and `Corvus.JsonSchema`; `operations` those the package uses; `culture` is `"invariant"`; `runtime` the .NET runtime and its Unicode version; `catalogues` the three catalogue versions the job ran, `outputContracts` `null` when the package uses none ([part 07](07-compiled-documents.md); [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979794633)) |
| `recordHash` | string (`sha256:…`) | allowed | the hash of the canonical record document, sealed when delivery ends (§ 1.1); `null` until then, and below 31 ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)) |
| `runFacts` | object | null below 31 | the raw value of each run fact the package reads, once per run (§ 8.1); `{}` when it reads none |
| `limits` | object `{ maxAgentCalls, capacityWaitMinutes, serviceAttempts, jobMinutes, deliveryMinutes, deliveryAttempts, loopItems, jobAgentCalls, jobInstances, renderSteps }`, each an integer | null below 31 | every engine limit in force at job start, recorded once ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1393#issuecomment-6980307806), [shape](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6984743967)): the per-job ceiling on agent calls in flight, the minutes a node may wait on 429s in all, the attempts allowed for another service failure, the minutes a job may run from orchestration start, the minutes delivery may take and the attempts each destination gets, and the total-work limits: items per loop instance, agent calls per job, **the instances one job may open** (`jobInstances`, a tenth limit, [ruled by the owner](https://github.com/Consultologist/Consultologist-Blazor/issues/1506#issuecomment-7064144456), ruling 1: a node, check or deliverable instance, a loop item, a pass or a call; [part 09](09-engine-and-pages.md) § 3.4), and steps per render ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660); [part 09](09-engine-and-pages.md) § 3) |
| `startedAtUtc` | string | never | when the job's orchestration started; the job limit is timed from here, not from creation or scheduling, so time queued or waiting behind a drain doesn't count ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)) |
| `signatureBlockId` | string | allowed | the profile signature block the job used, by **id**, never its text; `null` when the package doesn't read `profile.signature` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1408#issuecomment-6904843222)) |
| `signatureChosen` | `"default"` or `"job"` | allowed | whether the block was the account's chosen block (`ChosenId`) or picked on the Create page for this job; `null` with `signatureBlockId` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979794633)) |
| `decisions` | array of decision | never | every condition decision of the job (§ 5); `[]` for none. Each decision is stored as its own table row and gathered here in the canonical record document (§ 1.1) |

**`versionsRan`'s members** are `resolver` (`packageFormat` and `dialect`, the resolver's versions),
`libraries` (library name to version), `evaluator`, `operations` and `builtins` (name to version),
`culture`, `runtime` (`dotnet` and `unicode`) and `catalogues` (`agents`, `formats` and
`outputContracts`). The keys of `libraries`, `operations` and `builtins` are names, not members.

**The header's own members** (decided in the build, #1506; written by every 31 job, in the canonical
record document and on History's job header; a member with no value is `null`, never absent):

| Member | Type | Null | Meaning |
|---|---|---|---|
| `schemaVersion` | integer | never | `12` |
| `jobId` | string | never | the job's id |
| `status` | string | never | the job's state word; in a sealed record `Completed`, `Failed` or `Cancelled` |
| `error` | string | allowed | the sentence a failed job ended with, never an exception's message; `null` otherwise |
| `createdAtUtc`, `completedAtUtc` | string | `completedAtUtc` allowed | the job's clocks, as round-trip UTC text; `completedAtUtc` is `null` until the job ends |
| `effectiveInputHash` | string (`sha256:…`) | allowed | the hash of the job's inputs as storage 11 defines it, at `effectiveInputHashVersion` |
| `effectiveInputHashVersion` | integer | allowed | `7` at 31 |
| `inputs` | array of input | never | each input, by `id` in ordinal order (§ 3.1); `[]` for none |
| `tokens` | object `{ input, output, total }` | allowed | the job's token totals: each the sum of the agent rows' `tokens` member of that name (§ 4); `null` when the job made no model call |

**Unchanged and still recorded:** `packageSpecVersion` (31), `workflowPackage`, `packageFormatRef`,
`provenanceRef`, `formatsRef`, `catalogRef`, `engineCommit`, `engineRelease`, `apiHost`, the
terminology snapshot (`terminology`; `null` at 31, since a 31 job's start reads no terminology server,
and written all the same). `formatsRef` and the agents catalogue version now also appear in `lockHash`'s
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

An input in the header is `{ id, origin, hash }`: its `id`, its `origin` (below) and the hash of
its value as `effectiveInputHash` takes it, never the value. An input a document filled adds the
members above and `kind`, `extractor`, `pageCount`, `fileSha256` and `textSha256`; an input of
several elements lists them, in order, as `elements`, each with its own `origin` and, for a
document, those members. In an `ocr` object, `service` and `model` are the OCR service and its
model. `extractor` (the extraction's name and version, as storage 11 writes it) stays, for readers of
older records. #1438 (Milestone 40) builds on them.

**An input element from a previous run's deliverable** (#874) starts out **model-derived**, and its
origin links to the job it came from. Typed text and uploaded documents start clean
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6963556118)).
Such an element is recorded as `{ "origin": "previous-run", "jobId": "<source job>", "resultId": "<deliverable id>",
"modelDerived": true }` (decided in the build, #1509, D7). On an input it alone filled, the three members sit beside `id`,
`origin` and `hash`; in `elements`, beside the element's `origin`. A decision that reads an input holding such an element is
`modelDerived: true` (§ 5.1).

| Member (on a `previous-run` origin) | Type | Null | Meaning |
|---|---|---|---|
| `jobId` | string | never | the job whose deliverable the element was copied from, the same account's |
| `resultId` | string | never | that deliverable's id |
| `modelDerived` | boolean | never | `true`: the element starts out model-derived |

**Input elements record their `origin`** (`typed`, `document`, `previous-run`, `transcript`), never a
`producer`, which is a node's. The engine always knows the origin, so `null` is never written
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)).

## 4. Node rows

A production node instance's row keeps storage 11's members (`label`, `status`, `inputHash`,
`outputHash`, `hashVersion`, `tokens`, and the clock members `startedAtUtc`, `completedAtUtc` and
`durationMs`), some under their storage 12 names: storage 11's `validation` is `invalid` at 12, and
its `reason` and failure members are `stateReason`. It gains the members below. Their names are
[decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979794633).

| Member | Type | Null | Meaning |
|---|---|---|---|
| `nodeId` | string | never | now the **rooted key** |
| `itemId` | string | allowed | the id of the innermost loop item the row sits in (`1` of `node.a.b:2/1`); `null` outside every item |
| `kind` | string | never | `agent`, `rendered`, `computed`, `for`, `while`, `if`, `case` or `call` |
| `producer` | string | never | the node's `producer` as written and checked: `generated` on agent rows, `computed` on every other ([part 02 § 1.3](02-types-and-inputs.md#13-producer), [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)) |
| `formatHash` | string | allowed | the **full** hash of the node's resolved format (`resolve.json`'s `formatHashes`); `null` for flow kinds. With `agent`, it lets History say "the same type, served by a different agent" or the reverse ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6879413099)) |
| `typeHash` | string | allowed | the format's **type** hash (`resolve.json`'s `typeHashes`); `null` with `formatHash` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466)) |
| `sentFormatHash` | string | allowed | on agent rows, the hash of the **stripped** format, taken **before** it is wrapped ([ruled by the owner](https://github.com/Consultologist/Consultologist-Blazor/issues/1506#issuecomment-7073314482), ruling 3): `title`, `$id` and the keywords strict mode rejects removed, by the attested agent definition's list. The envelope is identified by the engine version, not by this hash, and an object root is sent unwrapped; only a non-object root is wrapped as `{ value: … }` ([part 02 § 3.5](02-types-and-inputs.md#35-what-a-model-receives), step 6). `null` on other rows ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466)) |
| `notSent` | array of string | never | on agent rows, the removed keywords as JSON pointers into the format (`/properties/onset/format`); `[]` for none. History shows *"Not sent to the model: …"*, and the reply is still validated against the full format ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6984743967)) |
| `agent` | object | allowed | on agent rows only (§ 4.1); `null` otherwise |
| `units` | array of `{ site: string, hash: string, sent: boolean }` | never | on agent rows, each part and snippet the prompt placed, by its site and the hash of its rendered text; `[]` for none. `sent` is `false` for a snippet substituted into a body that is not sent to the model (a macro's), which History shows as *"substituted, not sent"* ([ruled by the owner](https://github.com/Consultologist/Consultologist-Blazor/issues/1506#issuecomment-7073314482), ruling 4). The recorder keeps the whole of what an agent turn finishes with: `notSent`, `sentFormatHash`, `units`, the macro rows and the run spots |
| `tokens` | object `{ input: integer, output: integer, total: integer }` | allowed | on agent rows, the tokens the node's calls spent, summed over its attempts ([ruled by the owner](https://github.com/Consultologist/Consultologist-Blazor/issues/1506#issuecomment-7073314482), ruling 4; storage 11's `{ input, output }` with `total` added; the recorder persists them with the rest of the turn's detail); `null` on other rows |
| `itemName` | string | allowed | the item's name from the loop's `itemLabel`, capped at 120 characters (storage 11's rule, renamed from `labelField`); **in the row only when the label comes from package text** (a snippet folder's header), and then kept for the life of the record. Otherwise it is `null` and the name is in the retained text, deleted 7 days after completion ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660), § 11; decided in the build, #1506: a row never holds retained text, § 1.1). `null` on rows outside a loop. Reasons name items by position (*Sections 3*), never by `itemName` |
| `itemNameHash` | string | allowed | the hash of the item's name when `itemName` is `null` for the reason above; `null` when `itemName` holds the name or the row is outside a loop |
| `positionName` | boolean | never | `true` when `itemName` (or its hash) is the item's position (*Section 3*) rather than a name from the loop's label; `false` outside a loop |
| `function` | string | allowed | the id of the function the row runs in, `null` outside every function; on a call row, the function it calls (§ 7) |
| `depth` | integer | never | the nesting depth of the function calls the row runs in: `0` outside every function (§ 7) |
| `runDependent` | boolean | never | `true` when the row's value depends on `run.*`, transitively ([#1386](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6925165983)): the node's `runDependent` as written and checked ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1478#issuecomment-6982835861)) |
| `modelDerived` | boolean | never | `true` when the row's value is computed from a model's output, directly or through other values ([#1386](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6962982698)). Every agent row is `true`. History, the diagram and the Editor show it (*"computed from model text: node.draft"*). The node's `modelDerived` as written and checked ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1478#issuecomment-6982835861)); requiring either flag in a read is #1478 (Milestone 40) |
| `profileCarrying` | boolean | never | `true` when the row's value carries text a profile-reading macro body was substituted into, directly or through other values ([part 03 § 4.6](03-expressions.md#46-profile-carrying-is-contagious), [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)). The node's `profileCarrying` as written and checked |
| `runNeutralHash` | string | allowed | for a body with run spots, the hash of the body rendered with each run spot left as its marker; `null` when the body has none, so `inputHash` alone is the hash (§ 8.2) ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6978508523)) |
| `runSpots` | array of run spot | never | § 8.2; `[]` for none |
| `renders` | array of renderer sub-row | never | § 4.2; `[]` for none |
| `macros` | array of macro row | never | § 4.3; `[]` for none; agent rows only |
| `invalid` | object | allowed | the node's `onInvalid` outcome when a reply failed validation or the macro marker check: `{ reason: string, action: "retry" \| "fail", attempts: integer }`, where `reason` names what failed, for example `macro letrozole_prescription_text: missing`. `null` when the reply was valid. That the outcome sits on the node's row is [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1412#issuecomment-6957666676); the object's own members are [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6984743967) too. A retry message never quotes the reply; it names only what was wrong ([part 05](05-templates-and-macros.md)) |
| `retries` | array of `{ atUtc: string, waitMs: integer, kind: "capacity" \| "service", error: string \| null }` | never | each retry the call needed, with its back-off ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1393), [shape](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6984743967)): `kind` is `"capacity"` for a 429 and `"service"` for another service failure; `error` is that failure's kind (`"timeout"`, `"5xx"`, `"connection"`), never its content, and `null` for capacity. `[]` for none |
| `profileReads` | array of `{ field: string, hash: string }` | never | each `profile.*` read by a macro body placed in this node's prompt, by hash (§ 9); `[]` for none |
| `stateReason` | string | allowed | the sentence for a row that didn't complete: *skipped: …* (§ 5), *not run: beyond max <n>* ([part 04](04-loops-and-functions.md)), or a failure's reason (a `matches` text over 1 MiB, [part 03](03-expressions.md); the model service's limits or a total-work limit, [part 09](09-engine-and-pages.md) § 3.4); for a row still running when the job limit passed, `cancelled` with *cancelled: the job didn't finish within <n> minutes* ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)); `null` when completed |
| `startedAtUtc` | string | allowed | storage 11's clock (decided in the build, #1506): when the instance started, as round-trip UTC text from the orchestration's replay-safe clock: an agent or render call sent, a computed node evaluated, a flow node first stepped, a loop item's or a pass's frame opened. `null` for a row that never started (skipped, not run, or refused by a limit before it started) |
| `completedAtUtc` | string | allowed | when the row settled, from the same clock; `null` with `startedAtUtc` |
| `durationMs` | integer | allowed | the whole milliseconds from `startedAtUtc` to `completedAtUtc`; `null` with them |

**`cancelled`** is a row state at 12 ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)): an activity still running when the job limit passes
is recorded `cancelled`, and its late result, if one arrives, is ignored and never written.

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
| `binding` | object `{ format: string, producer: string, via: string }` | never | the format and producer whose binding chose the agent, and the route (`via`): `"format"` (a registry or catalogue format's binding) or `"custom"` (a package's own format, bound to the custom-format agent); `"producer"`, an agent chosen by producer alone, is reserved at 31 and refused until #1479, Milestone 40 (`resolve.json`'s `bindings`, part 07; [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868), [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1478#issuecomment-6982835861)) |

The agents catalogue's version is in `lock.json`; the record doesn't repeat it per row. **The fuller
recording** (each value's model, the settings read from the attested definition, tool calls) is
Milestone 40: #1439 and #1445.

### 4.2 Renderer sub-rows

**One sub-row per pipe step,** in the order the steps appear in the body, per instance (per item and
pass), inside the consuming row's `renders`. A chain (`{{ node.extract.value | renderer.concept_list
| renderer.bullets }}`) gives one sub-row per step (`…/render:1`, `…/render:2`)
([part 05](05-templates-and-macros.md)). The number is the entry's (§ 2.3), so a row's ids may skip one.
It's never a node row and never scheduled
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1397#issuecomment-6877988508)):

| Member | Type | Null | Meaning |
|---|---|---|---|
| `id` | string | never | `<row key>/render:<n>` (§ 2.3) |
| `renderer` | string | never | `renderer.<id>` |
| `templateHash` | string | never | the renderer's `bodyHash` in `resolve.json`, or for a built-in renderer its template's hash at the locked version |
| `source` | string | never | the path piped in (`node.extract.value`), or the previous step's sub-row id in a chain |
| `inputHash` | string | never | the hash of the piped value (canonical JSON for data, UTF-8 for text) |
| `pass` | array of integer | allowed | one number per enclosing template loop, 1-based; `null` when the use is in no template loop (§ 2.3) |
| `inputSize` | string | allowed | a size summary from the input's format, for example `"14 concepts"`; `null` when none applies |
| `outputHash` | string | never | the hash of the rendered text |
| `outputLength` | integer | never | the rendered text's length **in runes** ([ruled by the owner](https://github.com/Consultologist/Consultologist-Blazor/issues/1506#issuecomment-7073314482), ruling 5) |
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
| `classifier` | string | allowed | the row key of the classifier node its `if` read, linking to that row, where History shows the `judgement`'s `reason` beside its `answer` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6982429387)); **`null` unless the `if` reads exactly one node** ([ruled by the owner](https://github.com/Consultologist/Consultologist-Blazor/issues/1506#issuecomment-7073314482), ruling 6). Several classifiers for one value is #1416 (Milestone 40) |
| `placed` | boolean | never | `true` when the marker was found **exactly once**, in the form its instruction asked for; `false` when not offered, or when the check failed |
| `form` | `"block"` or `"inline"` | allowed | the form the instruction asked for; `null` when not offered |
| `contentHash` | string | allowed | the hash of the macro's rendered body as substituted, taken after the body's one final line ending is dropped ([part 05 § 8.6](05-templates-and-macros.md#86-processing-the-reply)); `null` when not offered |
| `runNeutralHash` | string | allowed | the hash of the body with each of its run spots left as its `{{run:<n>}}` marker; `null` when the body reads no `run.*`, so `contentHash` alone is the hash. A macro body's `run.*` is filled in directly, with no spot in the placing body, so the macro row carries its own ([§ 8.2](#82-run-spots-and-the-two-hashes); [ruled by the owner](https://github.com/Consultologist/Consultologist-Blazor/issues/1506#issuecomment-7073314482), ruling 2) |
| `runSpots` | array of run spot | never | the body's run spots (§ 8.2), each `<macro row id>/spot:<n>`; `[]` for none |

If the marker was missing, repeated or altered, the node's `onInvalid` outcome is on the node's row
(`invalid`), not here. Profile reads in macro bodies are the node's `profileReads`. **The same macro
used twice in one template** is #1457 (Milestone 40).

### 4.4 Loop, while and pass rows

A flow row keeps every member of § 4 (a loop's `formatHash`, `agent` and the rest are `null`, its
lists `[]`) and adds the members of its kind (decided in the build, #1506):

| Row | Member | Type | Null | Meaning |
|---|---|---|---|---|
| `for` | `parallel` | boolean | never | the loop's `parallel` as written |
| `for` | `maxParallel` | integer | allowed | its `maxParallel` as written |
| `for` | `itemLabel` | string | allowed | the name of the field its items' names come from (the label field, not a name) |
| `for` | `count` | integer | allowed | the items its source gave; `null` when the source was never read |
| `for` | `stopped` | string | allowed | `until` (its `until` held), `bound` (items beyond `max` were not run) or `end`; `null` while it runs |
| `while` | `max`, `checkFirst` | integer, boolean | `max` allowed | its `max` and `checkFirst` as written |
| `while` | `passes` | integer | never | the passes that ran |
| `while` | `stopped` | string | allowed | `check` (the check stopped holding), `until` or `bound` (`max` passes ran with the check still holding) |
| `while` | `pass` | integer | allowed | on a **pass row** (below), the pass's number, 1-based; `null` on the loop's own row |
| `for`, `while` | `reason` | string | allowed | the sentence for a loop that did not run its items or passes in full: a `null` source, a stop at its `until`, items beyond `max`, or a `while` that stopped at its bound (*stopped at its bound of 3; the check never stopped holding*); `null` otherwise |
| `call` | `function`, `arguments`, `depth` | | | § 7 |

Each pass of a `while` is a row of its own, kind `while`, keyed `<while key>#<n>` (§ 2.1), whose
state and `outputHash` are its `ret`'s; it carries `pass`.

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
| `result` | boolean or string | never | `true` or `false`; for a `case`, the arm taken: **its 1-based position as text** (`"2"`) or `"else"`, since a `case`'s arms have no ids at 31 (decided in the build, #1506); the `condition` is then the printed subject |
| `reads` | array of `{ path: string, hash: string }` | never | each value its own rule reads, as its `path` (the rule's variable, as `resolve.json` writes it) and the canonical `hash` of the value read (a `null` hashes as `null`); never the value itself |
| `runDependent` | boolean | never | `true` when it read `run.*` or a run-dependent value |
| `modelDerived` | boolean | never | `true` when it read a model-derived value. A decision's flags describe the decision only: they don't pass to what it chooses ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)) |
| `decidedAtUtc` | string | never | when the engine evaluated it, from the orchestration's replay-safe clock ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)) |

**How a decision's flags are found** (decided in the build, #1506; the flags follow earlier-pass reads, [ruled by the owner](https://github.com/Consultologist/Consultologist-Blazor/issues/1506#issuecomment-7064144456), ruling 4). `runDependent` is `true` when a read is `run.*` or names an entry whose `runDependent` is `true`, and `modelDerived` is `true` likewise from the entries' `modelDerived`, and for a read of an input holding an element from a previous run's deliverable (§ 3.1; #1509, D7). A read through `item`, `pass` or `param` takes the flags of what it stands for: the loop's source, the body node, the call's argument reads. A read the runner cannot trace is `modelDerived: true` and `runDependent: true`, so the record never marks model output as not model-derived.

**Reasons read as sentences** built from the printed condition: *skipped: input.kind.value =
"table" is false*. A row skipped by a decision has this sentence as its `stateReason`, and the
decision's id is how History links the two.

### 5.2 Which decisions, and their sites

Every condition the engine evaluates is recorded, per instance:

| Condition | `site` | Owner |
|---|---|---|
| a node's, check's, branch's or loop's header `if` | `self` | its row key |
| a `case` | `case` (result: the arm's position, or `"else"`) | its row key |
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
| `checked` | object `{ ref: string, hash: string }` | allowed | what it ran on, as its `ref` and that output's `hash`: the one `deliverable.*` the check reads, with its composed hash (`deliverable.consult`); else the one node it reads, with its output hash (`node.plan`); else `null` (decided in the build, #1506) |
| `reads` | array of `{ path: string, hash: string }` | never | every other read, by hash |
| `inputHash`, `outputHash` | string | allowed | as on a `computed` row |
| `producer` | string | never | `computed`, as on node rows |
| `modelDerived`, `runDependent`, `profileCarrying` | boolean | never | as on node rows |
| `itemNameHash` | string | allowed | as on node rows: the hash of `itemName` when the name is not kept (§ 4) |
| `runNeutralHash` | string | allowed | as on computed node rows (§ 8.2) |
| `stateReason` | string | allowed | the sentence for a check that didn't complete: *skipped: …* or a failure's reason; `null` when completed |
| `pass` | boolean | allowed | the `check_result`'s verdict, under the same name; kept for the life of the record, as today's `checkPassed`; `null` while running or when skipped. `passed` only ever means a count ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)) |
| `failWith` | string | allowed | the `check_result`'s `message`, the operation's literal `failWith` sentence, when it failed (package text, kept); `null` when it passed |
| `effect` | string | allowed | what it caused, **written on its one row** (decided in the build, #1506): for a check that gates nothing and is read, `read by <ref>` (its first reader in key order); `null` when nothing reads it. For a gate, its row is recorded when the last deliverable it gates settles, with `released`, `held` or `blocked`, the deliverable's state word; with more than one deliverable, `<word> <deliverable key>` joined by `; ` in key order. A run that ends first records the gate's row with `effect: null` |
| `passed`, `total` | integer | allowed | the `check_result`'s counts (how many held, out of how many), kept for the life of the record; `null` when the check has no result |
| `uncoveredHash`, `untestedHash` | string | allowed | the canonical hash of the `check_result`'s `uncovered` and `untested` lists; `null` when the list is `null` |
| `uncoveredSize`, `untestedSize` | integer | allowed | the number of elements in those lists; `null` with the hash |
| `count` | integer | allowed | on a check loop's own row, its item count; `null` on others |
| `startedAtUtc`, `completedAtUtc`, `durationMs` | string, string, integer | allowed | as on node rows (§ 4) |

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
| `date` | object `{ value: string, timeZone: string }` | the job's start date in the account's time zone (`value`, `"2026-11-16"`) and that zone's IANA name (`timeZone`, `"America/Toronto"`), recorded beside the value ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1406#issuecomment-6920512477)) |
| `id` | string | the job's id |
| `package` | string | `<name>@<version>`, as `run.package` gives it |
| `engine` | string | the engine's release version, as `run.engine` gives it |

Only the facts read are present. Run facts aren't patient text, so they're outside the 7-day drop.

### 8.2 Run spots and the two hashes

For a body that reads `run.*` (a prompt, a `rendered` node, a deliverable, or a snippet placed in one),
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
| `pass` | array of integer | allowed | one number per enclosing template loop, 1-based; `null` for a spot in no template loop (§ 2.3) |
| `start` | integer | never | where the spot's output begins in the sent text, in runes ([ruled by the owner](https://github.com/Consultologist/Consultologist-Blazor/issues/1506#issuecomment-7073314482), ruling 5) |
| `length` | integer | never | the output's length, in runes |
| `reads` | string | never | what it reads (`run.date`, or a run-dependent value's path) |
| `valueHash` | string | never | the hash of the value read |
| `output` | string | allowed | the formatted output (`"16 November 2026"`), **in the row only when the spot reads raw `run.*` facts**, and then kept for the life of the record; a spot that is also input- or model-derived (a run-dependent `computed` value that reads an input) has `output` `null`, its text is in the retained text under 7-day retention, and `outputHash` stays ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660); decided in the build, #1506) |
| `outputHash` | string | never | the output's hash |

- **The sent text is rebuilt exactly** from the run-neutral text and the spot outputs, each put
  at the position recorded while rendering (`start`, in runes), never found by searching the text ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)), so the main
  hash stays provable.
- **Runs are compared on the run-neutral hash.** Beside any body whose two hashes differ, History says
  the difference is due to run facts.
- **A body with no run spots has one hash;** its `runNeutralHash` is `null`.
- **A macro body's `run.*` is filled in directly,** with no spot in the placing body ([ruled by the owner](https://github.com/Consultologist/Consultologist-Blazor/issues/1506#issuecomment-7073314482), ruling 2): macro bodies are not among the bodies that carry spots in the body they are placed in. The macro's own row carries its `runNeutralHash` and `runSpots` (§ 4.3), so macro rows compare across runs.
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
| `label` | string | allowed | the deliverable's label |
| `hashVersion` | integer | never | `4`, the version of `composedHash` |
| `units` | array | never | as on node rows (§ 4); `[]` for none |
| `profileReads` | array | never | as on node rows (§ 4); `[]` for none |
| `templateHash` | string | never | the template's `bodyHash` in `resolve.json`; the text is shown from the pinned version, never copied |
| `format` | string | never | the deliverable's format (`markdown_block`, `plaintext` or an object format; never `markdown_inline`, [part 06](06-checks-deliverables-profile.md)) |
| `state` | string | never | `released`, `held`, `failed`, `skipped` (its header `if`) or `blocked` (its gate check failed or errored, [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)) |
| `stateReason` | string | allowed | the sentence: *held: empty*, *skipped: <condition> is false*, *blocked: check '<id>' failed (<detail>)*, *blocked: check '<id>' could not run (<error>)*, *released: check '<id>' didn't apply (its if was false)*; `null` when released through a passing check or none |
| `delivery` | array of delivery row key | never | the delivery rows of a released deliverable, one per destination (below); `[]` for a deliverable that wasn't released. The results are on those rows, not on this one |
| `check` | string | allowed | the check row key that gated it, linking to the check's row; `null` when ungated |
| `sites` | array of `{ site: integer, at: string, expr: string, loop: string \| null }` | never | each expression in the template, in source order: `site` its number, `at` its line and column (`"3:1"`), `expr` its source text and `loop` the template loop it sits in, or `null`. Package text, kept |
| `substitutions` | array of substitution | never | each evaluation: `{ site, pass: integer[] \| null, reads: string, hash: string, start: integer, size: integer, renders: string \| null }`; `pass` holds one number per enclosing template loop. `start` and `size` say where its text sits in the composed text, in runes (decided in the build, #1506), `size` `0` for a value that writes nothing (a `null`) |
| `renders` | array of renderer sub-row | never | as on node rows (§ 4.2) |
| `runNeutralHash`, `runSpots` | | | as on node rows (§ 8.2) |
| `modelDerived`, `runDependent`, `profileCarrying` | boolean | never | as on node rows |
| `startedAtUtc`, `completedAtUtc`, `durationMs` | string, string, integer | allowed | as on node rows (§ 4): when its render was sent and when its outcome was decided |

**A delivery row** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6994467727)) is written for each destination of a released deliverable, keyed
`delivery:<deliverable>/<destination>`, and is the one place a result is recorded. Its members are
`{ key: string, deliverable: string, kind: string, result: "sent" \| "not sent" \| "failed", reason: string \| null, atUtc: string \| null, attempts: array of { atUtc: string, outcome: string } }`:
`key` is its own key, `deliverable` the deliverable key it belongs to (the canonical document gathers
it under that deliverable's `deliveries`, § 1.2), and `outcome` an attempt's result word: `sent`, `failed: <error kind>`,
or `sent link-only` for the link-only email, whose row is `sent` with its `reason` saying why (*no document
password*, *attachments over the budget*, *every deliverable blocked*; part 06 § 8, [ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1507#issuecomment-7082078968)).
A job that completes having released nothing, with a deliverable blocked or held, writes a row for each `email` or
`pdf` destination of each of them, for that email ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1507#issuecomment-7086202369)).
The results are written once the job has succeeded: *not sent: EMR isn't set up for this account*,
*not sent: the job failed*; `failed` after the attempts ran out or the delivery window closed
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6993822677), part 09 § 3.8), each attempt in `attempts`. The canonical record gathers the
delivery rows under their deliverable.

**Replay:** substituting the recorded values into the template, in site and pass order, must
reproduce `composedHash`. It is checked by position, never by search: the composed text's slice at
each substitution (`start`, for `size` runes) hashes to the substitution's `hash`, and substitutions
never overlap (decided in the build, #1506). A deliverable row shows only a one-line check status; the check's row
holds the result.

**A deliverable whose gating check was skipped by its own `if` is released,** and recorded with the
reason above; an empty `all_pass`, by contrast, fails. **A `blocked` deliverable is kept** (ruled by the owner on #1507, item 3, and on [#1506](https://github.com/Consultologist/Consultologist-Blazor/issues/1506#issuecomment-7055768051)): its row holds `composedHash`, `check`, `stateReason`, `sites` and `substitutions` like any other, its composed text is in the retained text, under 7-day retention with the hashes kept, and it is never delivered (`delivery: []`). History and the planned lineage view show it, and a later release after review (#1524, Milestone 40) would act on it. **A bundle holds released deliverables
only;** History lists what was left out and why
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6978700251)).

## 11. Retention

| Kept for the life of the record | Deleted 7 days after completion (#368) |
|---|---|
| every hash, key, id, status, count, clock and token figure | produced text: replies, prompts as sent, composed deliverables |
| labels, item names whose label comes from package text (capped at 120) | concepts |
| printed conditions, `failWith` sentences (a `check_result`'s `message`), template sites (package text) | input text and held inputs |
| decision results, check verdicts (`pass`), states and reasons | renderer inputs and outputs |
| run facts, and the outputs of run spots that read raw `run.*` facts only | a `check_result`'s `uncovered` and `untested` lists |
| `resolveHash`, `lockHash`, `versionsRan`, `recordHash`, extraction versions | the text behind profile reads, and profile-carrying text |
| the `modelDerived`, `runDependent` and `profileCarrying` flags, the removed-keyword pointers | item names drawn from inputs or model output; run spot outputs that are also input- or model-derived |
| the signature block's id | |

Retention goes by where text came from, not what it says, and holds for every package, medical or
not ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6978700251)).
The sweep (`DropText`) deletes the text blobs that hold what the right-hand column names and stamps
`textDroppedAtUtc`, as today; **no row is rewritten** (§ 1.1). Every hash stays, so a sub-row or substitution keeps its reference, format, size and hash and
says the text was deleted under retention.

**Hashes of short values** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660), option c): every hash stays, short values' included. A hash of a
short value (a name, a credentials line, a yes or no) can be reversed by hashing likely candidates,
and `provenance-record.md` says so beside the hash definitions. Long texts keep their hash for
verification as before.

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
  - `effectiveInputHashVersions` gains **7**: inputs as the object keyed by id, a document's text as extracted
    (as at 30);
  - `workflowOutputHashVersions` gains **4**: a deliverable composed from its template, `composedHash`
    as replayed in § 10;
  - the `resolveHash` and `lockHash` definitions: SHA-256 over RFC 8785 bytes;
  - the type hash, full format hash and sent-format hash (#1385), the last taken after the format is
    stripped and before the engine's envelope wraps it;
  - as built (#1506): the hash of a read value (canonical JSON, `null` hashing as `null`); a
    computed row's and a check's `inputHash` and `runNeutralHash` over `{ expression, reads }`;
    `expressionHash` (a `case`'s over `{ "var": <subject> }`); a substitution's `hash` with its
    `start` and `size` in runes and the replay by position; `itemNameHash`; and `recordHash` over the
    UTF-8 bytes of the canonical document (§ 1.2).
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
  "versionsRan": { "resolver": { "packageFormat": "package-format@v2026.12.1", "dialect": "scriban-dialect@1.0.0" },
                   "libraries": { "Scriban": "7.2.6", "Markdig": "0.41.3",
                                  "Corvus.Text.Json.JsonLogic": "5.8.11", "Corvus.JsonSchema": "4.3.17" },
                   "evaluator": "1.0.0", "operations": { "terms_subset": "1.0.0" },
                   "builtins": { "concept_bullets": "1.0.0", "formats": "1.0.0", "markdown_escape": "1.0.0",
                                 "plain_conversion": "1.0.0", "printer": "1.0.0" },
                   "culture": "invariant",
                   "runtime": { "dotnet": "10.0.3", "unicode": "16.0" },
                   "catalogues": { "agents": "agents@v2026.12.1", "formats": "formats@v2026.12.1",
                                   "outputContracts": null } },
  "runFacts": { "date": { "value": "2026-11-16", "timeZone": "America/Toronto" } },
  "limits": { "maxAgentCalls": 16, "capacityWaitMinutes": 10, "serviceAttempts": 5, "jobMinutes": 30,
              "deliveryMinutes": 15, "deliveryAttempts": 3, "loopItems": 200, "jobAgentCalls": 500,
              "jobInstances": 10000, "renderSteps": 100000 },
  "signatureBlockId": "sig-2", "signatureChosen": "job",
  "decisions": [
    { "id": "node.standards.assessment:7/if:macro.letrozole_prescription_text",
      "owner": "node.standards.assessment:7", "site": "macro.letrozole_prescription_text",
      "condition": "node.standards.prescribes_letrozole.value.answer = true", "expressionHash": "sha256:…",
      "result": true,
      "reads": [ { "path": "node.standards.prescribes_letrozole.value.answer", "hash": "sha256:…" } ],
      "runDependent": false, "modelDerived": true, "decidedAtUtc": "2026-11-16T14:02:11Z" } ],
  "nodeOutputs": {
    "node.standards.assessment:7": {
      "nodeId": "node.standards.assessment", "itemId": "7", "kind": "agent", "producer": "generated",
      "itemName": "Assessment and plan", "itemNameHash": null,
      "status": "Completed", "stateReason": null,
      "inputHash": "sha256:…", "outputHash": "sha256:…", "hashVersion": 7,
      "formatHash": "sha256:…", "typeHash": "sha256:…", "sentFormatHash": "sha256:…", "notSent": [],
      "agent": { "name": "consult-prose", "version": 4,
                 "binding": { "format": "markdown_block", "producer": "generated", "via": "format" } },
      "units": [ { "site": "snippet.clinic_header", "hash": "sha256:…", "sent": true } ],
      "tokens": { "input": 1840, "output": 412, "total": 2252 },
      "runDependent": false, "profileCarrying": false, "modelDerived": true, "runNeutralHash": null, "runSpots": [],
      "renders": [ { "id": "node.standards.assessment:7/render:1", "renderer": "renderer.concept_bullets",
                     "templateHash": "sha256:b81…", "source": "node.extract.value",
                     "inputHash": "sha256:7d0…", "inputSize": "14 concepts",
                     "outputHash": "sha256:3f9…", "outputLength": 612, "runDependent": false } ],
      "macros": [ { "id": "node.standards.assessment:7/macro:letrozole_prescription_text",
                    "macro": "macro.letrozole_prescription_text", "offered": true,
                    "decisions": ["node.standards.assessment:7/if:macro.letrozole_prescription_text"],
                    "classifier": "node.standards.prescribes_letrozole:7", "placed": true, "form": "block",
                    "contentHash": "sha256:…", "runNeutralHash": null, "runSpots": [] } ],
      "invalid": null, "retries": [], "profileReads": []
    }
  },
  "checks": {
    "check.coverage": { "key": "check.coverage", "kind": "computed", "producer": "computed", "label": "Coverage",
                        "status": "Completed", "itemName": null, "alignedWith": null,
                        "checked": { "ref": "deliverable.consult", "hash": "sha256:9a3f…" },
                        "reads": [ { "path": "node.extract_input_terms.value", "hash": "sha256:…" } ],
                        "modelDerived": true, "runDependent": false, "profileCarrying": false,
                        "pass": true, "failWith": null, "effect": "released", "count": null } }
}
```

## Refusals and recorded sentences

The record refuses nothing itself; the refusals at start and load are in
[part 07](07-compiled-documents.md) § 10. The wording below is the starting text. It follows the style rules in [index § 7](index.md#7-refusal-sentences) and is used as written in Milestone 39 and reviewed in #1483 (Milestone 40); every sentence is listed in [refusal-sentences.md](refusal-sentences.md). The sentences it **records**:

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
- *not sent: the job failed* (each destination of a released deliverable in a job that failed;
  nothing is delivered until the job has succeeded, [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)).
- *failed: <kind>* (a destination whose delivery was retried and still didn't go, with each
  attempt on the result; the job stays completed, [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6993822677)).
- *blocked: check '<id>' failed (<detail>)* and *blocked: check '<id>' could not run (<error>)*
  (a deliverable whose gate check failed or errored, [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)).
- *cancelled: the job didn't finish within <n> minutes* (a row still running at the job limit, [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)).
- History's banner, built from the record: *<deliverable label> was blocked: check '<check id>' failed (<detail>).*,
  for example *Consultation note was blocked: check 'coverage' failed (10 of 12 terms present).*
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6881869311), revised by [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)).
- History, for a sent format with keywords removed: *Not sent to the model: <pointers>*
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466)).
- A node's `invalid.reason` for the macro check: *macro <id>: missing*, *macro <id>: repeated*,
  *macro <id>: altered*; and the failure when `onInvalid` is `fail`:
  *Node '<row key>' failed: macro '<id>' was <missing | repeated | altered> in the reply.*
- A deliverable failed by `whenEmpty: fail`: *Deliverable '<id>' was empty, and the workflow fails an empty <label>.*
- A node failed by the model service's limits ([part 09](09-engine-and-pages.md) § 3.4, [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1393#issuecomment-6984396609)):
  *The model service stayed busy for <n> minutes.*, or *The model service <timed out | returned an
  error | couldn't be reached> <n> times in a row.*; and a job past its time limit: *The job didn't
  finish within <n> minutes.* History adds *Run the workflow again later.* ([part 09](09-engine-and-pages.md) § 9).

## Proposed in this part

Nothing. Every point is decided; refusal sentences are used as written and reviewed in #1483 ([refusal-sentences.md](refusal-sentences.md)).
