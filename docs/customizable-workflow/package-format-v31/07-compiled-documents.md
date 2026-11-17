# 07. The compiled documents

This part defines what publish produces from a version 31 package: **`resolve.json`**, the package
as checked, which is the only thing the engine loads (#1389); **`lock.json`**, the exact toolchain
that checked it (#1390); `requires` and `needs`; where each file lives, and the line between
evidence and views; **in-package tests** in `tests/` and the **publish report** that records them
(#1449). Whether `resolve.json` ever needs a `semantics` block is reviewed in Milestone 40 (#1455).
The record that pins both documents is [part 08](08-record.md).

## 1. Two documents, written once

| | `resolve.json` | `lock.json` |
|---|---|---|
| **Says** | what the package is, once read, resolved, typed and checked | exactly which implementations read, resolved, typed and checked it |
| **Read by** | the engine (the only file it loads for a 31 package), History, the Editor, outside tools | the engine's start check, History, the registry's upgrade checks, reviewers |
| **Media type** | `application/vnd.consultologist.resolve+json` | `application/vnd.consultologist.lock+json` |
| **Its schema** | published in the format-spec release | published in the format-spec release |

The rules common to both ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1390#issuecomment-6871587418),
[2026-11-17](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6957667030)):

- **Data, never code.** Both are JSON; the engine reads them and never executes them.
- **Canonical by RFC 8785** (the JSON Canonicalization Scheme). The bytes stored are the canonical
  bytes, so re-deriving either document from the same source with the same toolchain gives the
  same bytes and the same hash.
- **Hashes are SHA-256 over the canonical, uncompressed bytes,** written `sha256:<64 lowercase hex>`.
  Compression (for example gzip) is only for storage and transport.
- **Written only by publish,** with the registry's toolchain. Never authored, never accepted from
  an upload, never in any source (§ 7).
- **Immutable with the version.** A published version is **never re-resolved** and **never
  re-locked**. A change of toolchain that matters is a new version of the package
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6957667030)).
- **No backfill.** Versions below 31 never get either document; they keep today's loading path
  until they're retired (#1450). Packages that must keep running are migrated to 31 (#1447)
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6904042859)).
- **Every member is present** (#1394): `null` means "deliberately none" and a missing key is an
  error. Embedded JSON Schema keeps JSON Schema's own rules (annotations present where they apply).

**How they point at each other.** `lock.json` names the hash of the `resolve.json` it belongs to
(`resolve.hash`). `resolve.json` names its lock **by file name** (`lock: "lock.json"`), not by hash,
because each hash would otherwise have to contain the other. **Proposed.** The record pins both
hashes (part 08), which ties the pair for every job.

## 2. `resolve.json`

### 2.1 What's in it, and what isn't

**Embedded:** every piece of package content and every catalogue format used: prompt and template
bodies inline as JSON strings, formats as one `$defs` bundle, conditions and `computed` expressions
as canonical JSON Logic, the flattened node tree, the derived graph, `requires`, `needs` and the
warnings. Packages are at most 2 MiB of source (`SourceBounds`, [part 01](01-files-and-scopes.md)),
so one document and one hash cover everything
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6957667030)).

**Declared, never embedded:** engine capabilities (the template engine, the evaluator, built-in
operations, node kinds, features, attested agents), listed in `requires` with ranges (§ 3).

**Never in it:** a job's inputs; engine configuration (model deployments, endpoints, terminology
services, keys); records; test results (they go in the publish report, § 9).

**What each language holds** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6898321250)):
conditions and `computed` expressions are JSON Logic; templates (prompts, renderers, snippets,
parts, macro bodies and instructions, deliverables) are Scriban text in the dialect of
[part 05](05-templates-and-macros.md); formats are JSON Schema in the dialect of
[part 02](02-types-and-inputs.md). Everything the engine computes or decides is data.

### 2.2 Top-level members

| Member | Type | Null | Meaning |
|---|---|---|---|
| `documentVersion` | integer | never | the shape of `resolve.json` itself; `1` at 31 ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6957667030)) |
| `specVersion` | integer | never | `31`. It alone identifies the rules (null handling, whitespace, escaping, culture); there is **no `semantics` block** (#1455) and **no `resolvedBy`** (the toolchain is `lock.json`'s) |
| `package` | object `{ name: string, version: string }` | never | the package name and its version tag, for example `{ "name": "example-nested-note", "version": "v2026.12.1" }`. **Proposed** (the sketches used one `"name@version"` string) |
| `title` | string | never | the manifest's title |
| `description` | string | allowed | the manifest's description; `null` when it has none |
| `tags` | array of string | never | the manifest's tags; `[]` for none |
| `sourceHash` | string (`sha256:…`) | never | the hash of the source as published (§ 2.6) |
| `lock` | string | never | `"lock.json"`, the companion's file name in the same folder. **Proposed** |
| `requires` | object | never | engine capabilities (§ 3.1) |
| `needs` | object | never | what the account must provide (§ 3.2) |
| `inputs` | object keyed by input id | never | the package's inputs (§ 2.3); `{}` for none |
| `formats` | object (a JSON Schema with `$defs`) | never | the formats bundle (§ 2.4) |
| `formatHashes` | object, format id → `sha256:…` | never | each bundled format's hash, over its canonical schema. **Proposed** name |
| `bindings` | object, format id → binding | never | the attested agent serving each agent format used (§ 2.4); `{}` when the package has no agent node |
| `nodes` | object keyed by qualified key | never | every production node, flat (§ 2.5) |
| `checks` | object keyed by qualified key | never | every check and check loop, flat; `{}` for none |
| `functions` | object keyed by function id | never | each function, with its own flat `nodes` map (§ 2.5); `{}` for none |
| `renderers` | object keyed by renderer id | never | the package's renderers; `{}` for none |
| `snippets` | object keyed by qualified snippet id | never | every snippet and snippet folder; `{}` for none |
| `macros` | object keyed by qualified macro id | never | every macro and macro folder; `{}` for none |
| `deliverables` | object keyed by deliverable id | never | every deliverable |
| `graph` | object `{ edges, order }` | never | the derived dependency graph (§ 2.7) |
| `run` | array of string | never | the `run.*` facts read anywhere, sorted (`["run.date", "run.id"]`); `[]` for none. **Proposed** name |
| `warnings` | array of warning | never | every warning publish gave (§ 2.8); `[]` for none |

The manifest's own members that only steer publish (for example a `derivedFrom` pointer) are carried
under the same names when they exist at 31; [part 01](01-files-and-scopes.md) lists the manifest.

### 2.3 `inputs`

The manifest's inputs object, keyed by id, with each entry's five members as written
([part 02](02-types-and-inputs.md)): `label` (string), `description` (lines or `null`), `format`
(a `$ref` into the bundle, `{ "$ref": "#/$defs/<id>" }`, or the inline schema), `required`
(boolean) and `channel`. Bare format ids are rewritten to `#/$defs/<id>`, as everywhere in the
document.

### 2.4 `formats` and `bindings`

**`formats` is one self-contained JSON Schema bundle:** `{ "$defs": { "<id>": <schema>, … } }`,
holding every format the package uses, from its three homes, with no shadowing between them
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6879096608)):
- **built-ins** (`string`, `number`, `boolean`, `date`, `plaintext`, `markdown_block`,
  `markdown_inline`, `check_result`, …) from the package-format registry's definitions file;
- **catalogue formats** (`agent_plaintext`, `agent_markdown_*`, `agent_boolean`, `concept_list`,
  `classification`, …) from the formats catalogue at the pinned version, **only those used**;
- **package formats** from `formats/`.

Each entry keeps its `$id` URN ([part 02](02-types-and-inputs.md),
[decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6957666238)),
which names its origin and version, and all its annotations (`x-agent`, `contentMediaType`,
`x-markdown`, `x-instruction`). Annotations are stripped only from the copy sent to a model
provider, never here. Inline schemas written on an input or a node stay inline where they're
written.

**`bindings`** records which attested agent serves each agent format the package uses, kept apart
from the types so that a change of agent and a change of type are distinguishable
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6879413099)):

| Member | Type | Null | Meaning |
|---|---|---|---|
| `agentName` | string | never | the attested agent's name in the agents catalogue |
| `agentVersion` | integer | never | its version |
| `via` | `"format"` or `"producer"` | never | `"format"` when `bindings.json`'s `formats` names this format; `"producer"` for a package-local agent format served through `producers` (for example `custom` → `custom-json`). **Proposed** |

A node never names an agent; its agent comes through its format's binding, and its settings live
in the attested agent definition
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6923942272)).

### 2.5 The flat maps: `nodes`, `checks`, `functions`

**The folder tree is flattened into one map per namespace, keyed by qualified key**, the same keys
the record uses ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6871657218),
#1384). `nodes` and `checks` are separate maps because `node.x` and `check.x` are separate roots
(#1395): a node and a check may share an id. Each function's body is a `nodes` map of its own,
keyed by qualified keys local to the function (a function is a sealed scope, #1419).

Each entry holds **every header member as written** (parts [01](01-files-and-scopes.md) to
[06](06-checks-deliverables-profile.md) define them per kind), canonicalised: bare format ids
rewritten to `#/$defs/<id>`, conditions and expressions in canonical JSON Logic
([part 03](03-expressions.md)). Publish then adds the **derived members**:

| Derived member | Type | Null | On | Meaning |
|---|---|---|---|---|
| `id` | string | never | every entry | the bare id (the file or folder name) |
| `parent` | string | allowed | every entry | the enclosing flow node's qualified key; `null` at the top level (of the package, or of a function). **Proposed** name: #1389's flattening sketch called it `in`, which a loop's header now uses for its source (#1383) |
| `nodes` | array of string | never | `for`, `while`, branches | the children's qualified keys, in order |
| `ret` | string | allowed | `for`, `while`, branches, functions | the returned node's qualified key, as written |
| `reads` | array of string | never | every entry | every path the entry reads (header and body), canonical and sorted: `node.plan.value`, `input.consult_draft`, `item.value.point`, `run.date`, `profile.signature`, … Snippet and macro body reads count as the placing node's ([part 05](05-templates-and-macros.md)) |
| `runDependent` | boolean | never | every entry | `true` when anything the entry reads is a `run.*` fact or a run-dependent value, transitively ([part 03](03-expressions.md)) |
| `body` | string | allowed | templated kinds | the Scriban body inline (`\n` line endings); `null` for kinds with no body (`computed`, `for`, `while`, `if`, `case`, `call`) |
| `bodyHash` | string | allowed | templated kinds | `sha256:` over the body's UTF-8 bytes; `null` when `body` is `null` |
| `parts` | object keyed by part id | never | agent nodes | the node's parts, each with its `if` and its text (an array of lines, #1412); `{}` for none |
| `places` | array of string | never | agent nodes | the qualified ids of the macros its prompt places, in order of appearance; `[]` for none. **Proposed** name |
| `renders` | array of render site | never | templated kinds | each pipe into a `renderer.*`, in order of appearance: `{ "n": <1-based position>, "source": "<path>", "renderer": "<renderer id>" }`; `[]` for none. Its position `n` is the record's `render:N` (part 08). **Proposed** member names |
| `runSpots` | array of run spot | never | templated kinds | each run spot in the body ([part 05](05-templates-and-macros.md)): `{ "site": <1-based position>, "at": "<line>:<column>", "reads": "<path>" }`; `[]` for none. **Proposed** member names |
| `formatHash` | string | allowed | entries with a `format` | the hash of the resolved format, as in `formatHashes`; `null` for flow kinds that have none |

`renderers`, `snippets`, `macros` and `deliverables` entries carry their header members the same
way, with `body`, `bodyHash`, `reads`, `runDependent`, `renders` and `runSpots`. A deliverable's
`reads` are its sources: the retired `sources` member is derived from them
([#1406](https://github.com/Consultologist/Consultologist-Blazor/issues/1406)).

### 2.6 `sourceHash`

`sourceHash` is the SHA-256 of a canonical listing of the source: a JSON array, sorted by path, of
`{ "path": "<path from the package root, / separated>", "hash": "sha256:<hash of the file's bytes>" }`
for every source file, canonicalised by RFC 8785. **Proposed.**

- **Included:** `manifest.json` and every file under `nodes/`, `checks/`, `functions/`,
  `renderers/`, `snippets/`, `macros/`, `formats/`, `deliverables/` and `tests/`, and any
  `README.md`.
- **Excluded:** `resolve.json`, `lock.json` and the publish report (evidence, never source), and
  `dag.mmd` (a view, regenerated).

So `tests/` is covered by `sourceHash`, and through it by `resolveHash`, without being embedded:
the engine never runs a test (**Proposed**, settling #1449's open point).

### 2.7 `graph`

| Member | Type | Meaning |
|---|---|---|
| `edges` | array of `{ "from": string, "to": string, "kind": string }` | every dependency, from a node, check, input, `run.*` or `profile.*` read to what reads it. `kind` is one of `read`, `if`, `for`, `ret`, `placed`, `calls`, `aligned`, `gates`, matching the diagram's edges ([part 09](09-engine-and-pages.md)). Keys are rooted (`node.plan`, `check.coverage`, `input.consult_draft`, `deliverable.note`, `macro.letrozole`). **Proposed** shape |
| `order` | array of string | one topological order of the rooted keys, ties broken by source order. It's for readers and tools; the engine schedules by dataflow |

`dag.mmd` is drawn from this graph.

### 2.8 `warnings`

Each warning is `{ "sentence": string, "at": string | null }`: the sentence word for word as
publish gave it, and the file path it concerns (`nodes/parts/outline.md`), or `null` for the package
as a whole. **Proposed** shape. Warnings never block publish; run-spot warnings (#1425), the
per-job ceiling note (#1393) and conversion hints are all here.

### 2.9 An example: `example-nested-note` at 31 (abridged)

```jsonc
{
  "documentVersion": 1,
  "specVersion": 31,
  "package": { "name": "example-nested-note", "version": "v2026.12.1" },
  "title": "Nested note demo",
  "description": "A demonstration of nesting, computed values and while. Not intended for clinical use.",
  "tags": ["demo", "nested"],
  "sourceHash": "sha256:5e0c…",
  "lock": "lock.json",
  "requires": {
    "spec": "31",
    "evaluator": "^1.0.0",
    "scriban": "^7.2.5",
    "operations": { "size": "^1.0.0" },
    "kinds": ["agent", "computed", "for", "while"],
    "features": [],
    "agents": { "consult-prose": "4", "custom-json": "1" }
  },
  "needs": { "profile": [] },
  "inputs": {
    "consult_draft": { "label": "Consult draft", "description": null,
                       "format": { "$ref": "#/$defs/markdown_block" }, "required": true, "channel": null }
  },
  "formats": { "$defs": {
    "markdown_block":  { "$id": "urn:…:format:markdown_block@…", "type": "string",
                         "contentMediaType": "text/markdown", "x-markdown": "block", "x-agent": false },
    "agent_plaintext": { "$id": "urn:consultologist:format:agent_plaintext@v2026.12.1", "type": "string",
                         "contentMediaType": "text/plain", "x-agent": true },
    "section_plan":    { "$id": "urn:consultologist:package:example-nested-note@v2026.12.1:format:section_plan", "…": "…" },
    "outline": { "…": "…" }, "verdict": { "…": "…" }, "number": { "…": "…" } } },
  "formatHashes": { "agent_plaintext": "sha256:…", "section_plan": "sha256:…", "…": "…" },
  "bindings": {
    "agent_plaintext": { "agentName": "consult-prose", "agentVersion": 4, "via": "format" },
    "section_plan":    { "agentName": "custom-json",   "agentVersion": 1, "via": "producer" },
    "outline":         { "agentName": "custom-json",   "agentVersion": 1, "via": "producer" },
    "verdict":         { "agentName": "custom-json",   "agentVersion": 1, "via": "producer" }
  },
  "nodes": {
    "plan": { "id": "plan", "label": "Planning the sections", "kind": "agent", "parent": null,
              "format": { "$ref": "#/$defs/section_plan" }, "if": null, "onInvalid": "fail",
              "reads": ["input.consult_draft"], "runDependent": false,
              "body": "You are planning the sections of a consultation note.\n…", "bodyHash": "sha256:…",
              "parts": {}, "places": [], "renders": [], "runSpots": [], "formatHash": "sha256:…" },
    "section_count": { "id": "section_count", "label": "Counting the sections", "kind": "computed", "parent": null,
                       "format": { "$ref": "#/$defs/number" }, "if": null, "onInvalid": "fail",
                       "expression": { "size": [ { "var": "node.plan.value.sections" } ] },
                       "reads": ["node.plan.value"], "runDependent": false, "body": null, "bodyHash": null,
                       "renders": [], "runSpots": [], "formatHash": "sha256:…" },
    "parts": { "id": "parts", "label": "Sections", "kind": "for", "parent": null,
               "in": "node.plan.value.sections", "itemLabel": "title",
               "parallel": true, "maxParallel": null, "until": null, "if": null,
               "nodes": ["parts.outline", "parts.paragraphs"], "ret": "node.parts.paragraphs",
               "reads": ["node.plan.value"], "runDependent": false, "body": null, "bodyHash": null,
               "renders": [], "runSpots": [], "formatHash": null },
    "parts.outline":    { "…": "…", "parent": "parts" },
    "parts.paragraphs": { "…": "…", "kind": "for", "parent": "parts", "in": "node.outline.value.points",
                          "nodes": ["parts.paragraphs.draft_paragraph"], "ret": "node.parts.paragraphs.draft_paragraph" },
    "parts.paragraphs.draft_paragraph": { "…": "…", "kind": "agent", "parent": "parts.paragraphs",
                          "format": { "$ref": "#/$defs/agent_plaintext" },
                          "reads": ["input.consult_draft", "item.parent.value.title", "item.id", "item.value.point"] },
    "refine": { "id": "refine", "label": "Refining the summary", "kind": "while", "parent": null,
                "while": { "!==": [ { "var": "node.judge.value.verdict" }, "good" ] }, "max": 3,
                "nodes": ["refine.draft_summary", "refine.judge"], "ret": "node.refine.draft_summary", "…": "…" },
    "refine.draft_summary": { "…": "…" }, "refine.judge": { "…": "…" }
  },
  "checks": {}, "functions": {}, "renderers": {}, "snippets": {}, "macros": {},
  "deliverables": {
    "note": { "id": "note", "label": "Nested consultation note", "kind": "rendered",
              "format": { "$ref": "#/$defs/markdown_block" }, "if": null, "check": null, "whenEmpty": "fail",
              "reads": ["node.parts.items", "node.refine.content"], "runDependent": false,
              "body": "## Summary\n\n{{ node.refine.content }}\n…", "bodyHash": "sha256:…",
              "renders": [], "runSpots": [] }
  },
  "graph": {
    "edges": [ { "from": "input.consult_draft", "to": "node.plan", "kind": "read" },
               { "from": "node.plan", "to": "node.section_count", "kind": "read" },
               { "from": "node.plan", "to": "node.parts", "kind": "for" },
               { "from": "node.parts", "to": "deliverable.note", "kind": "read" },
               { "from": "node.refine", "to": "deliverable.note", "kind": "read" } ],
    "order": ["input.consult_draft", "node.plan", "node.section_count", "node.parts", "…", "deliverable.note"]
  },
  "run": [],
  "warnings": []
}
```

The member names of loops, `while`, `until` and deliverable headers are defined in parts
[04](04-loops-and-functions.md) and [06](06-checks-deliverables-profile.md); the example only
shows where they sit. `"…"` marks elision.

## 3. `requires` and `needs`

Both are **derived, never authored,** and **minimal**: only what the package uses, so a package
without `call` runs on an engine without functions
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6904925306)).

### 3.1 `requires`: what the package needs from the engine

| Member | Type | Null | Lists |
|---|---|---|---|
| `spec` | string | never | the spec version, `"31"` |
| `evaluator` | string (caret range) | never | the JSON Logic evaluator's range. The evaluator's version is **our** version of the evaluator behind our own interface: our null rules, `===`/`!==` only, the canonical form. The library underneath (`Corvus.Text.Json.JsonLogic`) is pinned exactly and listed in `lock.json`. **Proposed** reading |
| `scriban` | string (caret range) | never | Scriban's range, for example `"^7.2.5"` |
| `operations` | object, operation name → caret range | never | each built-in custom operation **used**: from `size`, `blank`, `words`, `lines`, `lower`, `trim`, `starts_with`, `ends_with`, `plain`, `object`, `matches`, `has_word`, `terms_subset`, `all_pass`, `count_pass`, `days_between`, `years_between`, `add_days` ([part 03](03-expressions.md)); `{}` for none |
| `kinds` | array of string | never | the node kinds used, from `agent`, `rendered`, `computed`, `for`, `while`, `if`, `case`, `call` |
| `features` | array of string | never | behaviours not tied to one kind: `parallel` (a loop with `parallel: true`), `macro-markers`, `parts`, `run-spots`, `check-loops`; `[]` for none. The first three are [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6904925306); `run-spots` and `check-loops` are **Proposed** |
| `agents` | object, agent name → version string | never | the attested agents (name and exact version) the `bindings` name; `{}` for none |

**Not listed:** package content (prompts, snippets, renderers, formats, functions), which is
embedded and covered by the hash; catalogue versions, which are in `lock.json`; delivery kinds, since
an unsupported kind is produced and recorded as not sent (#1406).

**The value validator** (`Corvus.JsonSchema` behind our own interface) and **Markdig** are not in
`requires`. They're pinned exactly and locked (§ 4); a change in their output reaches packages
through the versions of what uses them (the evaluator, `plain`, the Markdown escaping), which golden
tests guard (§ 4.3). **Proposed.**

**The engine checks `requires` before the job starts,** when the Create page asks, and again when
the job starts. Anything missing or out of range refuses with the sentences of § 10.

### 3.2 `needs`: what the account must provide

| Member | Type | Null | Lists |
|---|---|---|---|
| `profile` | array of string | never | the `profile.*` fields any template reads, from `name`, `credentials`, `organisation`, `signature`, `closing` ([part 06](06-checks-deliverables-profile.md)), sorted; `[]` for none ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6904925306)) |

The Create page reads `needs` before submission and refuses, naming the field, when the account
hasn't set it (§ 10).

## 4. `lock.json`

### 4.1 Members

| Member | Type | Null | Meaning |
|---|---|---|---|
| `documentVersion` | integer | never | the shape of `lock.json`; `1` at 31. **Proposed** (mirrors `resolve.json`) |
| `package` | object `{ name, version }` | never | as in `resolve.json` |
| `resolve` | object `{ hash: string }` | never | the hash of the `resolve.json` this lock belongs to ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1390#issuecomment-6871587418)) |
| `resolver` | object `{ packageFormat: string, dialect: string }` | never | the format library and registry release that resolved and validated it (`package-format@v2026.12.1`), and the Scriban dialect spec's version (#1425). **Proposed** member names |
| `libraries` | object, package id → exact version | never | the third-party libraries whose change could alter a rendered prompt, a validation verdict, a composed document or a hash, each **exact**: `Scriban`, `Markdig`, `Corvus.Text.Json.JsonLogic`, `Corvus.JsonSchema` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1390#issuecomment-6957667429)) |
| `evaluator` | string | never | our evaluator's exact version (`"1.0.0"`) |
| `operations` | object, operation name → exact version | never | each built-in operation the package uses (the same names as `requires.operations`), exact; `{}` for none |
| `builtins` | object, name → exact version | never | the built-in machinery that isn't an operation: `markdown_escape` (escaping plain text into Markdown), `plain_conversion` (Markdown to plain text), `formats` (the built-in format definitions). **Proposed** names |
| `culture` | string | never | `"invariant"`: every render and operation runs in the invariant culture ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1425)) |
| `catalogues` | object | never | `formats` (the formats catalogue, `formats@v…`: types), `agents` (the agents catalogue, `agents@v…`: bindings and attested agent definitions), `outputContracts` (`output-contracts@v…`, or `null` when the package uses none), as **separate entries** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1390#issuecomment-6879413591)) |

**Not locked:** .NET, the JSON and HTTP libraries, Durable Functions, the Azure SDKs. The engine's
commit and release stay on the job's record ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1390#issuecomment-6904042632)).
**No time stamp** is written in either document: it would make re-derivation give different
bytes. The publish time is in the publish report and the registry. **Proposed.**

```jsonc
{
  "documentVersion": 1,
  "package": { "name": "example-nested-note", "version": "v2026.12.1" },
  "resolve": { "hash": "sha256:9b41…" },
  "resolver": { "packageFormat": "package-format@v2026.12.1", "dialect": "scriban-dialect@1.0.0" },
  "libraries": { "Corvus.JsonSchema": "4.3.17", "Corvus.Text.Json.JsonLogic": "5.8.11",
                 "Markdig": "0.41.3", "Scriban": "7.2.5" },
  "evaluator": "1.0.0",
  "operations": { "size": "1.0.0" },
  "builtins": { "formats": "1.0.0", "markdown_escape": "1.0.0", "plain_conversion": "1.0.0" },
  "culture": "invariant",
  "catalogues": { "agents": "agents@v2026.12.1", "formats": "formats@v2026.12.1", "outputContracts": null }
}
```
(Library versions are illustrative.)

### 4.2 Lock exactly, refuse by compatible range (option c)

[Decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1390#issuecomment-6904042632):
- **`lock.json` records the exact version that checked the package;** `requires` states a
  compatible range; **the engine refuses only outside the range;** the record names the exact
  versions that ran (part 08). A package locked at Scriban 7.2.5 runs on 7.2.6, and the record says
  so.
- **Ranges are caret only:** `^7.2.5` means at least 7.2.5 and below 8.0.0, so only a major version
  leaves the range.
- **The registry sets each range at publish,** from the locked version and the component's version
  policy. Authors never write ranges.
- **A library pinned exactly in `Directory.Packages.props`** (Markdig, both Corvus libraries, and
  Scriban) moves only by a deliberate upgrade under § 4.4.

### 4.3 Versioning built-in operations and machinery

- **Semantic versions, with any change to output bytes a major version.** Additions are minor;
  internal fixes with identical output are patch.
- **Each version is declared in the package-format registry** beside the built-in formats.
- **Golden tests** per operation and per piece of built-in machinery (fixed inputs, fixed expected
  bytes) are part of the conformance suite and **fail CI** if output changes without a major bump.
- Renderers are package templates (#1397), covered by `resolve.json`'s hash, and need no version
  of their own.

### 4.4 Upgrading a locked library: the corpus re-render

**Before any upgrade of a locked library, the registry re-renders a test corpus** under the current
and the candidate version: every published 31 package's templates and operations on fictional
values, its in-package tests, and the conformance suite's golden tests. **Any byte difference blocks
the upgrade** until it's understood. If the upgrade is adopted anyway, it counts as **major** for
our purposes, whatever the library calls it, and packages locked to the old version are refused
until republished ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1390#issuecomment-6904042632)).

**How "counts as major" reaches the engine** when the library's own version stays inside the caret
range: the package-format registry publishes, per locked component, a **floor**, the first version
adopted with a byte difference. The engine refuses a package whose locked version is below the
floor, with the same sentence as an out-of-range version. For a library behind our interfaces
(the Corvus libraries, Markdig), the floor is expressed instead as a major bump of the evaluator,
operation or machinery whose output changed. **Proposed.**

Swapping either Corvus library for json-everything (#1458, #1459) follows the same rule: byte-identical
golden tests and corpus re-render, or it counts as major
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1390#issuecomment-6957667429)).

## 5. The publish pipeline

Publish runs the same steps whatever starts it: CI from a tag in `consultologist-workflows`, or the
engine's `WorkflowPackages/Publish` endpoint (the Editor, the API, the operator's bulk migration).
In order:

0. **Receive.** Any `resolve.json`, `lock.json` or publish report in the upload is **dropped
   unread** (§ 7). Every `SourceBounds` bound is checked ([part 01](01-files-and-scopes.md)). A
   version that already exists is refused, as today.
1. **Resolve.** Read the file model; flatten the tree into qualified keys; resolve every `$ref`
   against the built-ins and the pinned catalogues, rewriting bare ids; embed every body; derive
   `reads`, `runDependent`, `renders`, `runSpots`, the graph, `requires` and `needs`.
2. **Validate.** Every rule of parts [01](01-files-and-scopes.md) to
   [06](06-checks-deliverables-profile.md): the file model, types, JSON Logic typing, the Scriban
   dialect, scopes, macros, checks, deliverables. **Any refusal stops publish** with every refusal
   sentence found, each with its file path. Warnings are collected.
3. **Test.** Run every `tests/<id>.json` offline against the resolved document (§ 8). **A failing
   test stops publish** with the test's id and its first mismatch.
4. **Lock.** Write `lock.json`, naming the canonical `resolve.json`'s hash.
5. **Write.** Into the registry's `{name}/{version}/`: the source files, the regenerated `dag.mmd`,
   the publish report, `lock.json`, and **`resolve.json` last**, so a version is loadable only once
   all its files are present. **Proposed** write order. The version's stamp records `resolveHash`
   and `lockHash`.

Validation and tests run against `resolve.json`, the document the engine will run, never against the
source a second time. The Editor's "Run tests" and CI run steps 0–3 without writing.

## 6. Where the files live

| | Example and demo packages | Account packages |
|---|---|---|
| **Source lives** | the public `consultologist-workflows` repository | the registry only (written by the Editor or `WorkflowPackages/Publish`) |
| **Published by** | CI, from a tag | the engine's publish endpoint |
| **`resolve.json`, `lock.json`** | the registry's `{name}/{version}/`, and **GitHub Release assets** on the version's tag, byte-identical so their hashes match the records | the registry's `{name}/{version}/`; **"Download package"** in the Editor (#808) includes them for a published version; the Editor and History show the compiled document and its toolchain for versions the account owns |
| **Publish report** | the registry, and a Release asset. **Proposed** | the registry; shown in the Editor after publish |
| **`dag.mmd`** | committed beside the source, regenerated by the tools and checked by CI; regenerated at publish | generated at publish into the version's folder; shown in the Editor's Graph pane |

[Decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6905180743).
The Serverless Workflow export (`workflow.sw.json`) is a view too, and follows `dag.mmd`'s rule when
it ships in Milestone 40 (#1422).

```
{name}/{version}/                    in the registry
  manifest.json
  nodes/ checks/ functions/ renderers/ snippets/ macros/ formats/ deliverables/ tests/
  dag.mmd                            view
  resolve.json                       evidence: what the engine runs
  lock.json                          evidence: what checked it
  publish-report.json                the publish's outcome, tests included   (name Proposed)
```

## 7. Evidence and views

**The rule: evidence is publish-only; views may be committed**
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6905180743)).

| | Evidence: `resolve.json`, `lock.json` | Views: `dag.mmd` (and `workflow.sw.json`, M40) |
|---|---|---|
| **Claims** | what the package is and what checked it; records pin their hashes | nothing; for readers |
| **Written by** | publish only | the tools, CI and publish |
| **In source** | never: not in the repository, not in an account's working copy | example packages: committed and CI-checked; account packages: generated at publish |

**Never trust an uploaded copy:**
- **Importing a package** (.zip, #847) that contains `resolve.json` or `lock.json` ignores them, and
  the Editor says so once (§ 10). Publishing always regenerates both.
- A `resolve.json` or `lock.json` **committed to the examples repository** is ignored by publish, and
  CI warns that it shouldn't be there (§ 10).
- A committed `dag.mmd` that differs from a fresh one **fails CI**.

## 8. In-package tests (`tests/`)

Packages carry their own tests, run **offline**: a test makes no model calls. It runs on the
engine's deterministic harness, the one the migration builds for byte-equivalence (#1447), with
every agent node's output stubbed ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1449)).
Tests against live models are Milestone 40 (#1402, #1415).

**Where they run:** CI in the public repository; "Run tests" in the Editor; and **at publish, where a
failing test blocks publish** (§ 5, step 3). **Results go in the publish report, never in job
records** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1449#issuecomment-6957669037)).

### 8.1 The file: `tests/<id>.json`

One test per file; the file name is the id (`^[a-z][a-z0-9_]*$`, as every id). Pure JSON, no front
matter. Every member is present (#1394).

| Member | Type | Null | Meaning |
|---|---|---|---|
| `description` | array of string (lines) | allowed | what the test pins; `null` for none |
| `inputs` | object keyed by input id | never | a **fictional** value for **every** declared input, valid against its format, or `null` for an optional input left unset. Never patient text (#1402's rule). Document-filled inputs take their text form here; file fixtures are Milestone 40 (#1442). **Proposed:** every input listed |
| `profile` | object keyed by profile field | never | a fixed fictional value for **exactly** the fields in `needs.profile`; `{}` when it's empty. **Proposed:** exactly those fields |
| `run` | object keyed by run fact | never | a fixed value for **exactly** the facts in `resolve.json`'s `run`: `"date": { "value": "2026-11-16", "timeZone": "America/Toronto" }`, `"id"`, `"package"`, `"engine"` as strings; `{}` when none is read. **Proposed** shape for `date` |
| `outputs` | object keyed by stub key | never | a stubbed value for every agent node instance that runs, valid against the node's format (§ 8.2) |
| `expect` | object | never | the expectations (§ 8.3); `{}` checks only that the run completes |

### 8.2 Stub keys

A stub key is the node's **qualified key**, then, for an instance inside a loop or a `while`, `:` and
its path: item ids and pass numbers from the outermost inward, joined by `/`, exactly the record's
row key ([part 08](08-record.md), #1384): `plan`, `parts.outline:2`,
`parts.paragraphs.draft_paragraph:2/1`, `refine.judge:3`. Inside a function the key goes through
the call node: `hpi_section.standard_draft`.

- A text format's stub is a JSON string; a data or object format's stub is the JSON value.
- **A node that runs with no stub** is an error (§ 10). **A stub for a node that doesn't run** is a
  warning. **A stub invalid against its format** is an error, validated as an agent reply would be.
- Classifiers (`agent_boolean`, `classification`) are stubbed like any agent node, so every
  condition that reads them is decided by the test.

### 8.3 Expectations

`expect` lists what is checked. **An omitted expectation means "not checked",** which is distinct
from `null` (#1394's rule applies to members of declarations, not to the expectations listed). Its
members, each optional (**Proposed** names):

| Member | Type | Checks |
|---|---|---|
| `ran` | array of row key | these instances completed |
| `skipped` | array of row key | these instances were skipped (by condition, or not run by `until`) |
| `decisions` | object, decision key → boolean or branch id | each listed condition's result ([part 08](08-record.md) § 5's keys) |
| `loops` | object, row key → `{ "count": integer }` or `{ "passes": integer, "stopped": string }` | loop counts, `while` passes and how a `while` or `until` stopped |
| `values` | object, row key → JSON value | `computed` values, compared as canonical JSON |
| `prompts` | object, row key → `{ "text": string }` or `{ "hash": string }` | the sent text of an agent node's prompt, exactly or by its sent-text hash |
| `checks` | object, check row key → `{ "passed": boolean }` | check results |
| `deliverables` | object, deliverable id → `{ "state": string, "text": string }` or `{ "state": string, "hash": string }` | each deliverable's state (`released`, `held`, `failed`, `skipped`) and its composed text or hash |

**A test passes** when the run completes (or fails exactly where `expect` says, through a
deliverable's `fail`) and every listed expectation holds. Its **first mismatch** is the first failing
expectation in the order of the table above, then by key.

### 8.4 Limits and the migration's snapshot test

- Test files count against `SourceBounds` like any source file. **At publish, all tests together
  have 60 seconds;** past it, publish stops with a sentence. **Proposed.**
- **The migration writes one snapshot test per package,** `tests/migrated.json`
  (**Proposed** name), from its migrated run: fictional inputs, stubs from the formats, and the
  resulting prompts and deliverables as expectations, so every migrated package starts with one test
  pinning its behaviour (#1447, [part 10](10-migration-and-tools.md)).

### 8.5 Example (made-up package `example-checks`, fictional values)

```jsonc
// tests/two_sections.json
{
  "description": ["Two planned sections; the second fails coverage; the note is held."],
  "inputs": { "referral": "Margaret Okafor, 58, referred for fatigue and a raised TSH (fictional)." },
  "profile": { "signature": "Dr A. Fictional\nInternal Medicine" },
  "run": { "date": { "value": "2026-11-16", "timeZone": "America/Toronto" } },
  "outputs": {
    "plan": { "sections": [ { "title": "History" }, { "title": "Plan" } ] },
    "parts.draft:1": "Fatigue for three months; no weight change.",
    "parts.draft:2": "Repeat thyroid tests in six weeks."
  },
  "expect": {
    "checks": { "check.part_coverage.coverage:1": { "passed": true },
                "check.part_coverage.coverage:2": { "passed": false },
                "check.all_covered": { "passed": false } },
    "deliverables": { "note": { "state": "held", "hash": "sha256:…" } }
  }
}
```

## 9. The publish report

Written at step 5 into the version's folder, as canonical JSON; returned in full by the publish
endpoint and printed by CI whether publish succeeds or not. **Not evidence:** records never pin it,
and it says nothing about a job. **Proposed:** file name `publish-report.json`, media type
`application/vnd.consultologist.publish-report+json`, and every member name below; the content
(each test, pass or fail, with its hashes) is
[decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1449#issuecomment-6957669037).

| Member | Type | Null | Meaning |
|---|---|---|---|
| `documentVersion` | integer | never | `1` |
| `package` | object `{ name, version }` | never | the version published |
| `outcome` | `"published"` or `"refused"` | never | a written report always says `published`; a refused publish returns the report without writing it |
| `publishedAtUtc` | string (RFC 3339) | allowed | when it was written; `null` when refused |
| `publishedBy` | string | never | `ci` with the tag, `editor`, `api` or `operator-migration` |
| `sourceHash` | string | never | as in `resolve.json` |
| `resolveHash` | string | allowed | `null` when publish stopped before step 4 |
| `lockHash` | string | allowed | `null` when publish stopped before step 4 |
| `refusals` | array of `{ sentence, at }` | never | every refusal sentence, with its file path; `[]` when published |
| `warnings` | array of `{ sentence, at }` | never | as `resolve.json`'s `warnings` |
| `tests` | array of test result | never | one per test, in id order; `[]` for none |

**A test result:**

| Member | Type | Null | Meaning |
|---|---|---|---|
| `id` | string | never | the test's id |
| `fileHash` | string | never | `sha256:` of the test file's bytes |
| `result` | `"passed"` or `"failed"` | never | |
| `outcomeHash` | string | never | `sha256:` over the harness's canonical outcome (row keys, decisions, values, sent-text hashes, deliverable states and hashes), so two runs of the same test can be compared |
| `firstMismatch` | object `{ expectation: string, key: string, expected: string, actual: string }` | allowed | `null` when passed. `expected` and `actual` are hashes for text, values for everything else |
| `durationMs` | integer | never | the test's run time |

## 10. Refusals

**At start** (the Create page, then the engine), from `requires` and `needs`:
- *"This workflow (<package>@<version>) needs <component> <range>; this engine runs <component> <engine version>. Publish a new version of the workflow to run it on this engine."*
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1390#issuecomment-6904042632);
  `<component>` is `Scriban`, `the evaluator` or an operation's name). The same sentence covers a
  locked version below a published floor (§ 4.4).
- *"This workflow (<package>@<version>) needs <kind or feature> '<name>', which this engine doesn't support. Publish a new version of the workflow to run it on this engine."* **Proposed.**
- *"This workflow (<package>@<version>) needs agent <agent name> version <version>, which this engine doesn't have. Publish a new version of the workflow to run it on this engine."* **Proposed.**
- *"This workflow needs your signature. Choose one in Profile."*
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1408#issuecomment-6904843222)); for other fields,
  *"This workflow needs your <field label>. Set it in Profile."* **Proposed.**
- *"Set your time zone in your account settings before running a workflow."* Every job, until the account's time zone is set ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1406#issuecomment-6958802620)); wording
  **Proposed**. No `needs` member is required, since every job needs the zone.

**At load** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6898433175): the engine refuses a document whose bytes don't match):
- *"Workflow <package>@<version> can't run: its compiled document doesn't match the hash published for it."* **Proposed** wording.

**At publish, from tests** (§ 8):
- *"Test '<test id>' failed: <expectation> for <key> expected <expected>, got <actual>. Publish stopped."* **Proposed** wording; the content (the test's name and its first mismatch) is decided.
- *"Test '<test id>' has no stub for <row key>, which runs. Add it to outputs."* **Proposed.**
- *"Test '<test id>' stubs <row key> with a value that isn't <format>: <first validation error>."* **Proposed.**
- *"Test '<test id>' gives no value for input '<input id>'. List every input; use null for an optional input left unset."* **Proposed.**
- *"Test '<test id>' must give exactly the profile fields the workflow reads: <fields>."* and *"Test '<test id>' must give exactly the run facts the workflow reads: <facts>."* **Proposed.**
- *"Tests took longer than 60 seconds in all. Publish stopped."* **Proposed.**

**Warnings** (never blocking):
- *"Test '<test id>' stubs <row key>, which doesn't run."* **Proposed.**
- CI, in the examples repository: *"<path> is evidence written only by publish; it is ignored and shouldn't be committed."* **Proposed.**
- The Editor, on import: *"This package's resolve.json and lock.json were ignored; publishing writes new ones."* **Proposed.**

## Proposed in this part

- **`resolve.json` names its lock by file name, not hash** (`lock: "lock.json"`): mutual hashes are impossible, and the record pins both.
- **`package` as `{ name, version }`**, and `title`, `description`, `tags` carried at the top level: two members are easier to read than a joined string.
- **Member names `formatHashes`, `run`, `places`, `renders`, `runSpots`, `id` and `parent` on every entry** (`parent`, not #1389's `in`, which a loop header uses for its source), and the shapes of `renders` and `runSpots` entries: the issues decide the content, not the names.
- **`bindings` entries gain `via`** (`format` or `producer`): the record must name the binding that chose the agent (part 08).
- **`sourceHash` is over a sorted listing of path and file hash,** including `tests/` and `README.md` and excluding evidence and `dag.mmd`: settles #1449's "is `tests/` part of `resolveHash`" (yes, through `sourceHash`, not embedded).
- **The `graph` edge shape** `{ from, to, kind }` with the diagram's edge kinds: one graph model for the diagram and tools.
- **The `warnings` shape** `{ sentence, at }`: the same shape as the publish report's refusals.
- **`evaluator` in `requires` is our evaluator's version,** not Corvus's; Markdig and Corvus.JsonSchema are locked but not in `requires`: libraries behind our interfaces reach packages through our versioned components.
- **`features` gains `run-spots` and `check-loops`:** both are engine behaviours a package can depend on.
- **`lock.json` has `documentVersion`, `resolver { packageFormat, dialect }`, `builtins` and nullable `catalogues.outputContracts`:** mirrors `resolve.json` and names the built-in machinery #1390 versions.
- **No time stamp in either document:** re-derivation must give the same bytes.
- **A published floor per locked component** makes "adopted with a byte difference counts as major" enforceable inside a caret range.
- **`resolve.json` is written last** at publish, so a version is loadable only when complete.
- **The publish report:** its file name, media type, Release-asset copy and every member name.
- **Test grammar details:** every input listed, exactly the profile fields and run facts read, `run.date` as `{ value, timeZone }`, the `expect` member names, first-mismatch order, a 60-second total at publish, and the snapshot test named `tests/migrated.json`.
- **Every refusal and warning sentence marked Proposed above:** only the Scriban, signature and time-zone sentences are decided word for word.
