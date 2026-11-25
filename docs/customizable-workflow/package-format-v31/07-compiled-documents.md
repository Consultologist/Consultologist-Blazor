# 07. The compiled documents

This part defines what publish produces from a version 31 package: **`resolve.json`**, the package
as checked, which is the only thing the engine loads (#1389); **`lock.json`**, the exact toolchain
that checked it (#1390); `requires` and `needs`; where each file lives, and the line between
evidence and views; **in-package tests** in `tests/` and the **publish report** that records them
(#1449). Whether `resolve.json` ever needs a `semantics` block is reviewed in Milestone 40 (#1455).
The record that pins both documents is [part 08](08-record.md). The points this part left open were
[decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979256096)
on 2026-11-18; the producer on the node, which replaced that day's format-naming decision
([#1439](https://github.com/Consultologist/Consultologist-Blazor/issues/1439#issuecomment-6979257923)),
was [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868) the same day (§ 2.4).

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
- **No timestamps in either document.** Compiling the same source with the same toolchain gives the
  same bytes. The time of publishing is in the registry and the publish report
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979256096)).
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
- **Every key is written with its root** (`node.sections.paragraphs.draft_paragraph`,
  `check.part_coverage.coverage`, `deliverable.emr.note`, `input.consult_draft`), as in the record,
  test files and History links
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)).

**How they point at each other.** `lock.json` names the hash of the `resolve.json` it belongs to
(`resolve.hash`). `resolve.json` names its lock **by file name** (`lock: "lock.json"`), not by hash,
because each hash would otherwise have to contain the other. Every job's record pins both hashes
(part 08), which ties the pair for every job ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6959887217), [#1390](https://github.com/Consultologist/Consultologist-Blazor/issues/1390#issuecomment-6959887636)).

## 2. `resolve.json`

### 2.1 What's in it, and what isn't

**Embedded:** every piece of package content and every catalogue format used: prompt and template
bodies inline as JSON strings, formats as one `$defs` bundle, conditions and `computed` expressions
as canonical JSON Logic, the flattened node tree, the derived graph, `requires`, `needs` and the
warnings. Packages are at most 2 MiB of source (`SourceBounds`, [part 01](01-files-and-scopes.md)),
so one document and one hash cover everything
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6957667030)).

**Declared, never embedded:** engine capabilities (the template engine, the evaluator, built-in
operations, built-in renderers, node kinds, features, attested agents), listed in `requires` with
ranges (§ 3) and locked exactly in `lock.json` (§ 4).

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
| `package` | object `{ name: string, version: string }` | never | the package name and its version tag, for example `{ "name": "example-nested-note", "version": "v2026.12.1" }` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979256096)) |
| `derivedFrom` | string | allowed | the manifest's `derivedFrom`, carried under the same name: the `name@version` this package was copied from, or `null` if none. It only steers publish; the engine does not read it |
| `title` | string | never | the manifest's title, at the top level; the package's `name` (not `name@version`) when the manifest's `title` is `null` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1504)) |
| `description` | string | allowed | the manifest's description; `null` when it has none |
| `tags` | array of string | never | the manifest's tags; `[]` for none |
| `sourceHash` | string (`sha256:…`) | never | the hash of the source as published (§ 2.6) |
| `lock` | string | never | `"lock.json"`, the companion's file name in the same folder (§ 1) |
| `requires` | object | never | engine capabilities (§ 3.1) |
| `needs` | object | never | what the account must provide (§ 3.2) |
| `inputs` | object keyed by input id | never | the package's inputs (§ 2.3); `{}` for none |
| `inputOrder` | array of string | never | the input ids in written order, the Create page's order (§ 2.3); `[]` for none ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6984743967)) |
| `formats` | object (a JSON Schema with `$defs`) | never | the formats bundle (§ 2.4) |
| `formatHashes` | object, format id → `sha256:…` | never | each bundled format's **full** hash (§ 2.4) |
| `typeHashes` | object, format id → `sha256:…` | never | each bundled format's **type** hash (§ 2.4), beside `formatHashes` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6984743967)) |
| `bindings` | object, format id → producer → binding | never | the attested agent serving each format and producer an agent node uses (§ 2.4); `{}` when the package has no agent node |
| `nodes` | object keyed by rooted key | never | every production node, flat (§ 2.5) |
| `checks` | object keyed by rooted key | never | every check and check loop, flat; `{}` for none |
| `functions` | object keyed by function id | never | each function, with its own flat `nodes` map (§ 2.5); `{}` for none |
| `renderers` | object keyed by rooted key | never | the package's renderers, and each built-in renderer the package uses (`renderer.concept_bullets`, `renderer.concept_context`), embedded with the registry's header and body in the shape of a package renderer, so that the engine loads nothing but `resolve.json`; `lock.json`'s `builtins` says which are built in; `{}` for none |
| `snippets` | object keyed by rooted key | never | every snippet and snippet folder; `{}` for none |
| `macros` | object keyed by rooted key | never | every macro and macro folder; `{}` for none |
| `deliverables` | object keyed by rooted key | never | every deliverable, keyed by its path (`deliverable.emr.note`) |
| `graph` | object `{ edges, order }` | never | the derived dependency graph (§ 2.7) |
| `run` | array of string | never | the `run.*` facts read anywhere, sorted (`["run.date", "run.id"]`); `[]` for none ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979256096)) |
| `warnings` | array of warning | never | every warning publish gave (§ 2.8); `[]` for none |

The manifest keeps identity and inputs; its old `nodes`, `macros` and `snippets` lists are the
folders' `index.json` files at 31 ([part 01](01-files-and-scopes.md),
[decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)).
Members of the manifest that only steer publish are carried under the same names when they exist
at 31; `derivedFrom` is the one there is.

### 2.3 `inputs`

The manifest's inputs object, keyed by id, with each entry's five members as written
([part 02](02-types-and-inputs.md)): `label` (string), `description` (lines or `null`), `format`
(a `$ref` into the bundle, `{ "$ref": "#/$defs/<id>" }`, or the inline schema), `required`
(boolean) and `channel`. Bare format ids are rewritten to `#/$defs/<id>`, as everywhere in the
document.

**The order is explicit** in `inputOrder`, since RFC 8785 sorts an object's members and the written
order is the Create page's
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466)).

### 2.4 `formats` and `bindings`

**`formats` is one self-contained JSON Schema bundle:** `{ "$defs": { "<id>": <schema>, … } }`,
holding every format the package uses, from its three homes, with no shadowing between them
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6879096608)):
- **built-ins** (`string`, `number`, `boolean`, `date`, `plaintext`, `markdown_block`,
  `markdown_inline`, `check_result`, …) from the package-format registry's definitions file;
- **catalogue formats** (`concept_list`, `classification`, …) from the formats catalogue at the
  pinned version, **only those used**;
- **package formats** from `formats/`.

Each entry keeps its `$id` URN ([part 02](02-types-and-inputs.md),
[decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6957666238)),
which names its origin and version; a package format's `$id` is derived at publish
(`urn:consultologist:package:example-nested-note@v2026.12.1:format:verdict`), never written in
source. Each entry keeps its `$schema` (`urn:consultologist:spec:31:dialect`, checked against
`specVersion` at publish) and its annotations (`contentMediaType`, `x-markdown`)
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466)).
Inline schemas written on an input or a node stay inline where they're written; they carry no
`contentMediaType` or `x-markdown`.

**Formats are pure shape** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)): one `plaintext`, one `markdown_block`, one `boolean`,
with no producer families or prefixes. **Every node entry carries its `producer`**, as written
and checked against its kind: `"generated"` for an agent node, `"computed"` for every other
([part 02 § 1.3](02-types-and-inputs.md#13-producer)). The rules for a model's output (strict
mode, the envelope, the sent-format record, a named format, a binding) apply to the output of a
node whose `producer` isn't `computed`. `kind: "agent"` is unchanged: the kind says how a node
runs; the producer says how its output can be verified.

**Two hashes per format** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466)):
- **`formatHashes`**, the **full** hash: everything dereferenced, every keyword kept. It's what
  a model's reply is validated against.
- **`typeHashes`**, the **type** hash: references by name, sorted, annotations dropped. Type checks
  compare these: a written `format` against the derived one, `with` against parameters, branch
  results against each other.

**What a model receives** is its format with `title`, `$id` and the keywords strict mode rejects
(such as `format: "date"`) removed; the list comes from the attested agent definition, and the
reply is still validated against the full format. `resolve.json` keeps the full format; the record
holds the sent format's hash and the removed keywords (part 08).

**`bindings`** records which attested agent serves each format and producer the package's agent
nodes use, keyed by format id, then producer, kept apart from the types so that a change of agent
and a change of type are distinguishable
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6879413099);
[by format and producer](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)):

| Member | Type | Null | Meaning |
|---|---|---|---|
| `agentName` | string | never | the attested agent's name in the agents catalogue |
| `agentVersion` | integer | never | its version |
| `via` | string | never | the binding route: `"format"` for a registry or catalogue format bound in `agents/bindings.json`, `"custom"` for a package's own format bound to the catalogue's custom-format agent ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)). `"producer"`, an agent chosen by producer alone, is #1479 (Milestone 40), added without changing the shape: **the value is reserved at 31 and refused** until then ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1478#issuecomment-6982835861)) |

A binding holds no instructions: what the model should write is the format's own `description`s,
kept in the format above ([part 02 § 5.1](02-types-and-inputs.md#51-the-annotations),
[decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6982429387)).

A node never names an agent; its agent comes through its format's and producer's binding, and its
settings live in the attested agent definition
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6923942272)).

### 2.5 The flat maps: `nodes`, `checks`, `functions`

**The folder tree is flattened into one map per namespace, keyed by rooted key**, the same keys
the record uses ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6871657218),
#1384, [roots](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)).
`nodes` and `checks` are separate maps because `node.x` and `check.x` are separate roots
(#1395): a node and a check may share an id. Each function's body is a `nodes` map of its own,
keyed by rooted keys local to the function (a function is a sealed scope, #1419).

Each entry holds **every header member as written** (parts [01](01-files-and-scopes.md) to
[06](06-checks-deliverables-profile.md) define them per kind), canonicalised: bare format ids
rewritten to `#/$defs/<id>`, conditions and expressions in canonical JSON Logic
([part 03](03-expressions.md)), reference strings (`in`, `ret`, a `case` subject, `with`) written
with their roots. The written `format`, `reproducible`, `producer`, `modelDerived`,
`runDependent` and `profileCarrying` are among them; publish has already checked each against the value it derives,
and refused a mismatch ([part 01](01-files-and-scopes.md); [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1478#issuecomment-6982835861)). Publish then adds the **derived members**
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979256096)):

| Derived member | Type | Null | On | Meaning |
|---|---|---|---|---|
| `id` | string | never | every entry | the bare id (the file or folder name) |
| `parent` | string | allowed | every entry | the enclosing flow node's rooted key; `null` at the top level (of the package, or of a function). Not `in`, which a loop's header uses for its source |
| `nodes` | array of string | never | `for`, `while`, branches | the children's rooted keys, in order |
| `ret` | string | allowed | `for`, `while`, `if`, `case`, functions | the returned node's rooted key; `null` on an `if` and a `case`, whose `then`, `else` and `branches` each hold their own `{ nodes, ret }` |
| `reads` | array of string | never | every entry | every path the entry reads (header and body), canonical and sorted: `node.plan.value`, `input.consult_draft.content`, `item.value.point`, `run.date`, `profile.signature`, … Snippet and macro body reads count as the placing node's ([part 05](05-templates-and-macros.md)) |
| `runDependent` | boolean | never | every entry | `true` when anything the entry reads is a `run.*` fact or a run-dependent value, transitively ([part 03](03-expressions.md)). On a node or check entry it is the value the header declared, checked against this derivation ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1478#issuecomment-6982835861)); on other entries it is derived only |
| `profileCarrying` | boolean | never | every entry | `true` when the entry's value carries text a profile-reading macro body was substituted into, transitively ([part 03 § 4.6](03-expressions.md#46-profile-carrying-is-contagious), [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)). On a node or check entry it is the value the header declared, checked against this derivation ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)); on other entries it is derived only. A condition passes none of the three flags on |
| `modelDerived` | boolean | never | every entry | `true` when the entry's value is computed from a model's output, directly or through other values: every agent node, and anything that reads a model-derived value, transitively ([part 03](03-expressions.md)). On a node or check entry it is the value the header declared, checked against this derivation ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1478#issuecomment-6982835861)); on other entries it is derived only |
| `body` | string | allowed | templated kinds | the Scriban body inline (`\n` line endings); `null` for kinds with no body (`computed`, `for`, `while`, `if`, `case`, `call`) |
| `bodyHash` | string | allowed | templated kinds | `sha256:` over the body's UTF-8 bytes; `null` when `body` is `null` |
| `parts` | object keyed by part id | never | agent nodes | the node's parts, each with its `if` and its text (an array of lines, #1412); `{}` for none |
| `places` | array of string | never | agent nodes | the rooted ids of the macros its prompt places, its parts' text included, in order of appearance; `[]` for none |
| `names` | object, written head → rooted key | never | agent nodes, `rendered` nodes, deliverables | each head (`node.<…>` or `check.<…>`) that the entry's body, its parts, and the snippets and macros it places write, mapped to the rooted key it names, **only where the two differ**; `{}` otherwise. A body names a node as written at its place (`node.draft_summary`) and the run's scope holds rooted keys, so the composer reads through this member |
| `placements` | array of placement | never | agent nodes, `rendered` nodes, deliverables | every unit the body places, in order of appearance with placed units expanded in place (a macro body's snippets right after the macro's own entry); `[]` for none. Members below |
| `renders` | array of render site | never | templated kinds | each pipe step into a `renderer.*`, in order of appearance: `{ "n": <1-based position>, "source": "<path>", "renderer": "<renderer id>" }`; a chain gives one entry per step, and a later step's `source` is the step before it. `[]` for none. Its position `n` is the record's `render:<n>` (part 08). Entry shape [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6984743967) |
| `runSpots` | array of run spot | never | templated kinds | each run spot in the body ([part 05](05-templates-and-macros.md)), the `{{run:<n>}}` marker's `n`, numbered per entry in order of appearance with placed snippets, parts and macro instructions expanded in place: `{ "site": <1-based position>, "at": "<line>:<column>", "reads": "<path>", "unit": <site or null>, "neutral": "marker" \| "text" }`; `[]` for none. `unit` is the site whose text holds the spot (`null` for the body itself) and `at` counts in that text. `neutral` says what stands for the spot in the run-neutral render: `"marker"` for a raw `run.*` fact, a run-dependent `computed` value or a run-dependent `for` loop's value; `"text"` for a run-dependent `rendered` node, or a `while`, `if`, `case` or `call` whose `ret` is one, whose neutral render holds that node's run-neutral text. A read of a run-dependent agent's output is no spot. A run spot whose read pipes into renderers covers the whole pipeline |
| `formatHash` | string | allowed | entries with a `format` | the resolved format's full hash, as in `formatHashes`; `null` for flow kinds that have none |
| `typeHash` | string | allowed | entries with a `format` | its type hash, as in `typeHashes`; `null` with `formatHash` |
| `plainConverts` | object, `if` / `expression` / `while` / `until` → array of boolean | never | nodes, checks, deliverables | for each of those four members the entry has non-null, whether each `plain` in its rule converts, in the evaluator's order ([part 03](03-expressions.md)); `[]` for a rule with none; `{}` for an entry with none of the four rules |

`renderers`, `snippets`, `macros` and `deliverables` entries carry their header members the same
way, with `body`, `bodyHash`, `reads`, `runDependent`, `modelDerived`, `renders` and `runSpots`. A
deliverable's `reads` are its sources: the retired `sources` member is derived from them
([#1406](https://github.com/Consultologist/Consultologist-Blazor/issues/1406)).

A `placements` entry is `{ "site": "part.<id>" | "snippet.<id>" | "macro.<id>" | "macro-folder.<id>", "within": <the site whose text places it, or null for the body>, "if": <the unit's rule, its keys rooted for this entry's place, or null>, "plainConverts": [<boolean>…], "form": "block" | "inline" | null }`. `plainConverts` is that rule's, as above. `form` is a macro's marker form and `null` for the other kinds. A macro in a folder is preceded, once per entry, by a `macro-folder.<id>` entry, so the folder is decided wherever one of its macros may be. The engine decides what a prompt holds from this member alone; a snippet's, a macro's and a part's own `if` stay as written.

Each `deliverables` entry also carries `delivery` and `bundle`, as its delivery folder's `index.json`
writes them ([part 06 § 8](06-checks-deliverables-profile.md)): `delivery` an array of `email`, `pdf`,
`emr` and `zip`, `bundle` `"zip"` or `null`, and `[]` and `null` when the folder writes none. It also
carries `escapes`: `{ "unit": <site or null>, "at": "<line>:<column>" }` for each output tag whose value
composition escapes ([part 06 § 6.4](06-checks-deliverables-profile.md#64-strict-formats)), in order of
appearance with placed snippets expanded, `unit` and `at` as in `runSpots`; `[]` for a `plaintext` or
object deliverable. They are on the entry because the engine loads only `resolve.json` (§ 1); the
schema requires all three ([#1505](https://github.com/Consultologist/Consultologist-Blazor/issues/1505)).

### 2.6 `sourceHash`

`sourceHash` is the SHA-256 of a canonical listing of the source: a JSON array, sorted by path, of
`{ "path": "<path from the package root, / separated>", "hash": "sha256:<hash of the file's bytes>" }`
for every source file, canonicalised by RFC 8785. File hashes are taken **after line-ending
normalisation** ([part 01](01-files-and-scopes.md))
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979256096)).

- **Included:** `manifest.json` and every file under `nodes/`, `checks/`, `functions/`,
  `renderers/`, `snippets/`, `macros/`, `formats/`, `deliverables/` and `tests/` (every
  `index.json` included), and any `README.md`.
- **Excluded:** `resolve.json`, `lock.json` and the publish report (evidence, never source), and
  `dag.mmd` (a view, regenerated).

So `tests/` is covered by `sourceHash`, and through it by `resolveHash`, without being embedded:
the engine never runs a test.

### 2.7 `graph`

| Member | Type | Meaning |
|---|---|---|
| `edges` | array of `{ "from": string, "to": string, "kind": string }` | every dependency, from a node, check, input, `run.*` or `profile.*` read to what reads it. `kind` is one of `reads`, `if`, `for`, `ret`, `placed`, `calls`, `aligned`, `gates`, matching the diagram's edges ([part 09](09-engine-and-pages.md)). Keys are rooted (`node.plan`, `check.coverage`, `input.consult_draft`, `deliverable.note`, `macro.letrozole`) ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979256096)) |
| `order` | array of string | one topological order of the rooted keys over every edge but `gates`, ties broken by source order: a gate check may read the deliverable it gates, which is composed before the gate runs ([part 06 § 7](06-checks-deliverables-profile.md#7-gating-and-outcomes)). It's for readers and tools; the engine schedules by dataflow |

`dag.mmd` is drawn from this graph.

### 2.8 `warnings`

Each warning is `{ "sentence": string, "at": string | null }`, the publish report's refusal shape:
the sentence word for word as publish gave it, and the file path it concerns
(`nodes/sections/outline.md`), or `null` for the package as a whole
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979256096)).
Warnings never block publish; run-spot warnings (#1425), the per-job ceiling note (#1393) and
conversion hints are all here.

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
    "features": ["parallel"],
    "agents": { "consult-prose": 4, "custom-json": 1 }
  },
  "needs": { "profile": [] },
  "inputs": {
    "consult_draft": { "label": "Consult draft", "description": null,
                       "format": { "$ref": "#/$defs/markdown_block" }, "required": true, "channel": null }
  },
  "inputOrder": ["consult_draft"],
  "formats": { "$defs": {
    "markdown_block":  { "$schema": "urn:consultologist:spec:31:dialect",
                         "$id": "urn:…:format:markdown_block@…", "type": "string",
                         "contentMediaType": "text/markdown", "x-markdown": "block" },
    "plaintext":       { "$schema": "urn:consultologist:spec:31:dialect",
                         "$id": "urn:…:format:plaintext@…", "type": "string",
                         "contentMediaType": "text/plain" },
    "section_plan":    { "$schema": "urn:consultologist:spec:31:dialect",
                         "$id": "urn:consultologist:package:example-nested-note@v2026.12.1:format:section_plan",
                         "…": "…" },
    "outline": { "…": "…" }, "verdict": { "…": "…" }, "number": { "…": "…" } } },
  "formatHashes": { "plaintext": "sha256:…", "section_plan": "sha256:…", "…": "…" },
  "typeHashes":   { "plaintext": "sha256:…", "section_plan": "sha256:…", "…": "…" },
  "bindings": {
    "plaintext":    { "generated": { "agentName": "consult-prose", "agentVersion": 4, "via": "format" } },
    "section_plan": { "generated": { "agentName": "custom-json",   "agentVersion": 1, "via": "custom" } },
    "outline":      { "generated": { "…": "…", "via": "custom" } },
    "verdict":      { "generated": { "…": "…", "via": "custom" } }
  },
  "nodes": {
    "node.plan": { "id": "plan", "label": "Planning the sections", "kind": "agent", "producer": "generated", "parent": null,
                   "format": { "$ref": "#/$defs/section_plan" }, "reproducible": false,
                   "if": null, "onInvalid": "fail",
                   "reads": ["input.consult_draft.content"], "runDependent": false, "profileCarrying": false, "modelDerived": true,
                   "body": "You are planning the sections of a consultation note.\n…", "bodyHash": "sha256:…",
                   "parts": {}, "places": [], "renders": [], "runSpots": [],
                   "formatHash": "sha256:…", "typeHash": "sha256:…" },
    "node.section_count": { "id": "section_count", "label": "Counting the sections", "kind": "computed", "producer": "computed",
                            "parent": null, "format": { "$ref": "#/$defs/number" }, "reproducible": true,
                            "if": null, "onInvalid": "fail",
                            "expression": { "size": [ { "var": "node.plan.value.sections" } ] },
                            "reads": ["node.plan.value"], "runDependent": false, "profileCarrying": false, "modelDerived": true,
                            "body": null, "bodyHash": null, "renders": [], "runSpots": [],
                            "formatHash": "sha256:…", "typeHash": "sha256:…" },
    "node.sections": { "id": "sections", "label": "Sections", "kind": "for", "producer": "computed", "parent": null,
                       "format": "…", "reproducible": false,
                       "in": "node.plan.value.sections", "itemLabel": "title",
                       "parallel": true, "maxParallel": null, "until": null, "if": null,
                       "nodes": ["node.sections.outline", "node.sections.paragraphs"],
                       "ret": "node.sections.paragraphs",
                       "reads": ["node.plan.value"], "runDependent": false, "profileCarrying": false, "modelDerived": true,
                       "body": null, "bodyHash": null, "renders": [], "runSpots": [], "…": "…" },
    "node.sections.outline":    { "…": "…", "parent": "node.sections" },
    "node.sections.paragraphs": { "…": "…", "kind": "for", "producer": "computed", "parent": "node.sections",
                                  "modelDerived": true, "runDependent": false, "profileCarrying": false,
                                  "in": "node.sections.outline.value.points",
                                  "nodes": ["node.sections.paragraphs.draft_paragraph"],
                                  "ret": "node.sections.paragraphs.draft_paragraph" },
    "node.sections.paragraphs.draft_paragraph": { "…": "…", "kind": "agent", "producer": "generated",
                                  "parent": "node.sections.paragraphs",
                                  "format": { "$ref": "#/$defs/plaintext" }, "reproducible": false,
                                  "modelDerived": true, "runDependent": false, "profileCarrying": false,
                                  "reads": ["input.consult_draft.content", "item.id",
                                            "item.parent.value.title", "item.value.point"] },
    "node.refine": { "id": "refine", "label": "Refining the summary", "kind": "while", "producer": "computed", "parent": null,
                     "modelDerived": true, "runDependent": false, "profileCarrying": false,
                     "while": { "!==": [ { "var": "node.refine.judge.value.verdict" }, "good" ] }, "max": 3,
                     "nodes": ["node.refine.draft_summary", "node.refine.judge"],
                     "ret": "node.refine.draft_summary", "…": "…" },
    "node.refine.draft_summary": { "…": "…" }, "node.refine.judge": { "…": "…" }
  },
  "checks": {}, "functions": {}, "renderers": {}, "snippets": {}, "macros": {},
  "deliverables": {
    "deliverable.note": { "id": "note", "label": "Nested consultation note", "kind": "rendered",
                          "format": { "$ref": "#/$defs/markdown_block" }, "if": null, "check": null,
                          "whenEmpty": "fail",
                          "reads": ["node.refine.content", "node.sections.items"],
                          "runDependent": false, "profileCarrying": false, "modelDerived": true,
                          "body": "## Summary\n\n{{ node.refine.content }}\n…", "bodyHash": "sha256:…",
                          "renders": [], "runSpots": [] }
  },
  "graph": {
    "edges": [ { "from": "input.consult_draft", "to": "node.plan", "kind": "reads" },
               { "from": "node.plan", "to": "node.section_count", "kind": "reads" },
               { "from": "node.plan", "to": "node.sections", "kind": "for" },
               { "from": "node.sections", "to": "deliverable.note", "kind": "reads" },
               { "from": "node.refine", "to": "deliverable.note", "kind": "reads" } ],
    "order": ["input.consult_draft", "node.plan", "node.section_count", "node.sections", "…",
              "deliverable.note"]
  },
  "run": [],
  "warnings": []
}
```

The member names of loops, `while`, `until` and deliverable headers are defined in parts
[04](04-loops-and-functions.md) and [06](06-checks-deliverables-profile.md); the example only
shows where they sit. `"…"` marks elision. The loop is `sections`, not 30's `parts`, since `parts`
is a reserved id at 31; the migration renames it ([part 10](10-migration-and-tools.md)).

## 3. `requires` and `needs`

Both are **derived, never authored,** and **minimal**: only what the package uses, so a package
without `call` runs on an engine without functions
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6904925306)).

### 3.1 `requires`: what the package needs from the engine

| Member | Type | Null | Lists |
|---|---|---|---|
| `spec` | string | never | the spec version, `"31"` |
| `evaluator` | string (caret range) | never | the JSON Logic evaluator's range. It's **our** evaluator's version, behind our own interface: our null rules, `===`/`!==` only, the canonical form. The library underneath (`Corvus.Text.Json.JsonLogic`) is pinned exactly and listed in `lock.json` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979256096)). Revisiting this towards off-the-shelf components throughout, possibly the evaluator too, is #1477 (Milestone 40) |
| `scriban` | string (caret range) | never | Scriban's range, for example `"^7.2.5"`. With `lock.json`, this records the Scriban version that `templating` used to name in the manifest ([part 01](01-files-and-scopes.md)) |
| `operations` | object, operation name → caret range | never | each built-in custom operation **used**: from `size`, `blank`, `words`, `lines`, `lower`, `trim`, `starts_with`, `ends_with`, `plain`, `object`, `matches`, `has_word`, `terms_subset`, `all_pass`, `count_pass`, `days_between`, `years_between`, `add_days` ([part 03](03-expressions.md)); `{}` for none |
| `kinds` | array of string | never | the node kinds used, from `agent`, `rendered`, `computed`, `for`, `while`, `if`, `case`, `call` |
| `features` | array of string | never | behaviours not tied to one kind: `parallel` (a loop with `parallel: true`), `macro-markers`, `parts`, `run-spots`, `check-loops`; `[]` for none ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6904925306), [run-spots and check-loops](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979256096)) |
| `agents` | object, agent name → integer version | never | the attested agents (name and exact version, the integer `bindings` carries) the `bindings` name; `{}` for none. One version per agent: a package whose bindings name two versions of one agent is refused at publish ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1504)) |

**Not listed:** package content (prompts, snippets, renderers, formats, functions), which is
embedded and covered by the hash; catalogue versions, which are in `lock.json`; delivery kinds, since
an unsupported kind is produced and recorded as not sent (#1406).

**The value validator** (`Corvus.JsonSchema` behind our own interface) and **Markdig** are not in
`requires`. They're pinned exactly in `lock.json` and covered by the engine's break points (§ 4);
a change in their output reaches packages through the versions of what uses them (the evaluator,
`plain`, the Markdown escaping), which golden tests guard (§ 4.3)
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979256096)).

**The engine checks `requires` before the job starts,** when the Create page asks, and again when
the job starts, and compares `lock.json`'s exact versions with its break points (§ 4.4).
Anything missing, out of range or locked before a break point refuses with the sentences of § 10.

### 3.2 `needs`: what the account must provide

| Member | Type | Null | Lists |
|---|---|---|---|
| `profile` | array of string | never | the `profile.*` fields any template reads, from `name`, `credentials`, `organisation`, `signature`, `closing` ([part 06](06-checks-deliverables-profile.md)), sorted; `[]` for none ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6904925306)) |

The Create page reads `needs` before submission and refuses, naming the field, when the account
hasn't set it (§ 10). An empty or whitespace-only profile value counts as not set
([part 06](06-checks-deliverables-profile.md)).

## 4. `lock.json`

### 4.1 Members

[Decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979256096):

| Member | Type | Null | Meaning |
|---|---|---|---|
| `documentVersion` | integer | never | the shape of `lock.json`; `1` at 31 |
| `package` | object `{ name, version }` | never | as in `resolve.json` |
| `resolve` | object `{ hash: string }` | never | the hash of the `resolve.json` this lock belongs to ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1390#issuecomment-6871587418)) |
| `resolver` | object `{ packageFormat: string, dialect: string }` | never | the format library and registry release that resolved and validated it (`package-format@v2026.12.1`), and the Scriban dialect spec's version (#1425) |
| `libraries` | object, package id → exact version | never | the third-party libraries whose change could alter a rendered prompt, a validation verdict, a composed document or a hash, each **exact**: `Scriban`, `Markdig`, `Corvus.Text.Json.JsonLogic`, `Corvus.JsonSchema` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1390#issuecomment-6957667429)) |
| `evaluator` | string | never | our evaluator's exact version (`"1.0.0"`) |
| `operations` | object, operation name → exact version | never | each built-in operation the package uses (the same names as `requires.operations`), exact; `{}` for none |
| `builtins` | object, name → exact version | never | the built-in machinery that isn't an operation: `markdown_escape` (escaping plain text into Markdown), `plain_conversion` (Markdown to plain text), `formats` (the built-in format definitions), `printer` (the condition printer, [part 03](03-expressions.md)), and each built-in renderer the package uses (`concept_bullets`, `concept_context`, [part 05](05-templates-and-macros.md)). Key names [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6984743967) |
| `culture` | string | never | `"invariant"`: every render and operation runs in the invariant culture ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1425)) |
| `runtime` | object `{ dotnet: string, unicode: string }` | never | the exact .NET runtime that published the package (`"10.0.3"`) and the Unicode version its text handling follows (`"16.0"`) ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)). `lower`, `trim`, `words` and `has_word` depend on both; the conformance suite carries vectors for each (part 10 § 5) |
| `catalogues` | object | never | `formats` (the formats catalogue, `formats@v…`: types), `agents` (the agents catalogue, `agents@v…`: bindings and attested agent definitions), `outputContracts` (`output-contracts@v…`, or `null` when the package uses none), as **separate entries** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1390#issuecomment-6879413591)) |

**Recorded, not locked as a range:** the .NET runtime and its Unicode version (`runtime`), so a
change in case mapping or word characters is visible between two records. **Not locked:** the
JSON and HTTP libraries, Durable Functions, the Azure SDKs. The engine's
commit and release stay on the job's record ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1390#issuecomment-6904042632)).
**No time stamp** is written (§ 1).

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
  "builtins": { "formats": "1.0.0", "markdown_escape": "1.0.0", "plain_conversion": "1.0.0",
                "printer": "1.0.0" },
  "culture": "invariant",
  "runtime": { "dotnet": "10.0.3", "unicode": "16.0" },
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
- **Package renderers** are package templates (#1397), covered by `resolve.json`'s hash, and need
  no version of their own. **Built-in renderers** (`concept_bullets`, `concept_context`) are
  Scriban templates in the registry, versioned like operations and locked in `builtins`; their ids
  are reserved ([part 05](05-templates-and-macros.md)).

### 4.4 Upgrading a locked library: the corpus re-render

**Before any upgrade of a locked library, the registry re-renders a test corpus** under the current
and the candidate version: every published 31 package's templates and operations on fictional
values, its in-package tests, and the conformance suite's golden tests. **Any byte difference blocks
the upgrade** until it's understood. If the upgrade is adopted anyway, it counts as **major** for
our purposes, whatever the library calls it, and packages locked to the old version are refused
until republished ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1390#issuecomment-6904042632)).

**How "counts as major" reaches the engine** when the library's own version stays inside the caret
range ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6959887217), [#1390](https://github.com/Consultologist/Consultologist-Blazor/issues/1390#issuecomment-6959887636)). `requires` has no per-component floor: the caret range's lower bound is already
the package's floor, and a published document never changes. Instead:
- **The engine keeps a list of break points per locked component,** each the version at which
  output changed (for example *"Scriban output changed at 7.3.0"*).
- **At job start it compares them with the exact versions in the package's `lock.json`,** and
  refuses a package locked before a break point with #1390's sentence (§ 10), as for a version
  outside the range.
- **The list is engine configuration,** changed only by an upgrade that went through the corpus
  re-render above.

Swapping either Corvus library for json-everything (#1458, #1459) follows the same rule: byte-identical
golden tests and corpus re-render, or it counts as major
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1390#issuecomment-6957667429)).

## 5. The publish pipeline

Publish runs the same steps whatever starts it: CI from a tag in `consultologist-workflows`, or the
engine's `WorkflowPackages/Publish` endpoint (the Editor, the API, the operator's bulk migration).
In order:

0. **Receive.** Any `resolve.json`, `lock.json` or publish report in the upload is **dropped
   unread** (§ 7). Line endings are normalised (`\r\n` to `\n`; a lone `\r` is refused) and each
   normalised file is noted for the report ([part 01](01-files-and-scopes.md)). Every
   `SourceBounds` bound is checked. A version that already exists is refused, as today.
1. **Resolve.** Read the file model; compute **`sourceHash`** (§ 2.6); flatten the tree into
   rooted keys; resolve every `$ref` against the built-ins and the pinned catalogues, rewriting
   bare ids; embed every body; derive `reads`, `runDependent`, `modelDerived`, `profileCarrying`, `renders`,
   `runSpots`, the graph, `requires` and `needs`; compile `resolve.json` **in memory**.
2. **Validate.** Every rule of parts [01](01-files-and-scopes.md) to
   [06](06-checks-deliverables-profile.md): the file model, types, JSON Logic typing, the Scriban
   dialect, scopes, macros, checks, deliverables, and the written `format`, `reproducible`,
   `producer`, `modelDerived`, `runDependent` and `profileCarrying` against the derived ones. **Any refusal stops publish** with every refusal sentence found, each
   with its file path. Warnings are collected.
3. **Test.** Run every `tests/<id>.json` offline against the in-memory `resolve.json` (§ 8). **A
   failing test stops publish** with the test's id and its first mismatch.
4. **Compute, then write.** The order is
   [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979256096):
   1. compute `sourceHash` (done in step 1);
   2. compile `resolve.json` in memory (step 1) and take **`resolveHash`** from its canonical bytes;
   3. build `lock.json` in memory with `resolveHash`, and take **`lockHash`**;
   4. build the publish report with all three hashes, the time and the test results;
   5. write into the registry's `{name}/{version}/`: the source files and the regenerated `dag.mmd`,
      then `lock.json`, then the publish report, then **`resolve.json` last**.

   6. write the version's **"published" stamp last**, `publish.json`, recording `resolveHash` and `lockHash`:
      `{ "specVersion": 31, "resolveHash": "sha256:…", "lockHash": "sha256:…" }`, the two hashes `resolve.json` and
      `lock.json` must have. It is canonical, holds no time, and gains one member, `migratedFrom` (a string:
      the old version), when the operator's migration publishes the package ([part 10 § 1.5](10-migration-and-tools.md#15-account-packages-by-the-operator)). Its schema,
      `publish-stamp.schema.json`, refuses any other member; the reader ignores
      them.

   Every hash is taken from final bytes. **A version exists only once stamped:** the engine and
   the registry ignore a folder without the stamp, so a publish that dies partway can be retried
   from step 0, overwriting the unstamped folder. **A stamped version folder is immutable**;
   nothing writes to it again. At load the engine checks `resolve.json`'s hash, `lock.json`'s
   hash and `lock.json`'s `resolve.hash` against the stamp (part 09 § 1.2).

Validation and tests run against `resolve.json`, the document the engine will run, never against the
source a second time. The Editor's "Run tests" and CI run steps 0–3 without writing.

## 6. Where the files live

| | Example and demo packages | Account packages |
|---|---|---|
| **Source lives** | the public `consultologist-workflows` repository | the registry only (written by the Editor or `WorkflowPackages/Publish`) |
| **Published by** | CI, from a tag | the engine's publish endpoint |
| **`resolve.json`, `lock.json`** | the registry's `{name}/{version}/`, and **GitHub Release assets** on the version's tag, byte-identical so their hashes match the records | the registry's `{name}/{version}/`; **"Download package"** in the Editor (#808) includes them for a published version; the Editor and History show the compiled document and its toolchain for versions the account owns |
| **Publish report** | the registry, and a Release asset ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979256096)) | the registry; shown in the Editor after publish |
| **`dag.mmd`** | committed beside the source, regenerated by the tools and checked by CI; regenerated at publish | generated at publish into the version's folder; shown in the Editor's Graph pane |

[Decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6905180743).
The Serverless Workflow export (`workflow.sw.json`) is a view too, and follows `dag.mmd`'s rule when
it ships in Milestone 40 (#1422).

```
{name}/{version}/                    in the registry
  manifest.json
  nodes/ checks/ functions/ renderers/ snippets/ macros/ formats/ deliverables/ tests/
                                     all nine, always, each with its index.json
  dag.mmd                            view
  resolve.json                       evidence: what the engine runs
  lock.json                          evidence: what checked it
  publish-report.json                the publish's outcome, tests included
  publish.json                       the "published" stamp, written last (§ 5)
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
Tests against live models are Milestone 40 (#1402, #1415). The test grammar below is
[decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979256096).

**Where they run:** CI in the public repository; "Run tests" in the Editor; and **at publish, where a
failing test blocks publish** (§ 5, step 3). **Results go in the publish report, never in job
records** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1449#issuecomment-6957669037)).

### 8.1 The file: `tests/<id>.json`

One test per file; the file name is the id (`^[a-z][a-z0-9_]*$`, as every id). Pure JSON, no front
matter. Every member is present (#1394).

| Member | Type | Null | Meaning |
|---|---|---|---|
| `description` | array of string (lines) | allowed | what the test pins; `null` for none |
| `inputs` | object keyed by input id | never | a **fictional** value for **every** declared input, valid against its format, or `null` for an optional input left unset. The value is the raw value, as `input.<id>.value` reads it. Never patient text (#1402's rule). Document-filled inputs take their text form here; file fixtures are Milestone 40 (#1442) |
| `profile` | object keyed by profile field | never | a fixed fictional value for **exactly** the fields in `needs.profile`; `{}` when it's empty |
| `run` | object keyed by run fact | never | a fixed value for **exactly** the facts in `resolve.json`'s `run`: `"date": { "value": "2026-11-16", "timeZone": "America/Toronto" }`, `"id"`, `"package"`, `"engine"` as strings; `{}` when none is read |
| `outputs` | object keyed by stub key | never | a stubbed value for every agent node instance that runs, valid against the node's format (§ 8.2) |
| `expect` | object | never | the expectations (§ 8.3); `{}` checks only that the run completes |

### 8.2 Stub keys

A stub key is the node's **rooted key**, then, for an instance inside a loop or a `while`, `:` and
its path: item ids from the outermost inward, joined by `/`, and `#<n>` for a pass of a `while`
where it sits, exactly the record's row key ([part 08](08-record.md) § 2.1, #1384; [ruled by the owner](https://github.com/Consultologist/Consultologist-Blazor/issues/1506#issuecomment-7064144456), ruling 2): `node.plan`, `node.sections.outline:2`,
`node.sections.paragraphs.draft_paragraph:2/1`, `node.refine.judge#3`. Inside a function the key
goes through the call node: `node.hpi_section.standard_draft`.

- A text format's stub is a JSON string; a data or object format's stub is the JSON value.
- **A node that runs with no stub** is an error (§ 10). **A stub for a node that doesn't run** is a
  warning. **A stub invalid against its format** is an error, validated as an agent reply would be.
- Classifiers (agent nodes returning `judgement` or `classification`) are stubbed like any agent node, so every
  condition that reads them is decided by the test.

### 8.3 Expectations

`expect` lists what is checked. **An omitted expectation means "not checked",** which is distinct
from `null` (#1394's rule applies to members of declarations, not to the expectations listed). Its
members, each optional:

| Member | Type | Checks |
|---|---|---|
| `ran` | array of row key | these instances completed |
| `skipped` | array of row key | these instances were skipped (by condition, or not run by `until` or `max`) |
| `decisions` | object, decision id → boolean or branch id | each listed condition's result ([part 08](08-record.md) § 5's ids) |
| `loops` | object, row key → `{ "count": integer }` or `{ "passes": integer, "stopped": string }` | loop counts, `while` passes and how a `while` or `until` stopped |
| `values` | object, row key → JSON value | `computed` values, compared as canonical JSON |
| `prompts` | object, row key → `{ "text": string }` or `{ "hash": string }` | the sent text of an agent node's prompt, exactly or by its sent-text hash |
| `checks` | object, check row key → `{ "pass": boolean }` | check results, named as `check_result`'s `pass` |
| `deliverables` | object, deliverable key → `{ "state": string, "text": string }`, `{ "state": string, "hash": string }`, or **`{ "state": "skipped" }` alone** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1507#issuecomment-7055597366): a deliverable skipped by its `if` has no composed text) | each deliverable's state (`released`, `held`, `failed`, `skipped`, `blocked`, [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)) and its composed text or hash; a skipped deliverable has neither |

**A test passes** when the run completes (or fails exactly where `expect` says, through a
deliverable's `fail`) and every listed expectation holds. Only the **first mismatch** is reported:
the first failing expectation in the order of the table above, then by key.

### 8.4 Limits and the migration's snapshot test

- Test files count against `SourceBounds` like any source file. **At publish, all tests together
  have 60 seconds;** past it, publish stops with a sentence.
- **The migration writes one snapshot test per package,** `tests/migration_snapshot.json`, from
  its migrated run: fictional inputs, stubs generated from the formats, and the resulting prompts
  and deliverables as expectations **by hash**, so every migrated package starts with one test
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
    "node.plan": { "sections": [ { "title": "History" }, { "title": "Plan" } ] },
    "node.sections.draft:1": "Fatigue for three months; no weight change.",
    "node.sections.draft:2": "Repeat thyroid tests in six weeks."
  },
  "expect": {
    "checks": { "check.part_coverage.coverage:1": { "pass": true },
                "check.part_coverage.coverage:2": { "pass": false },
                "check.all_covered": { "pass": false } },
    "deliverables": { "deliverable.note": { "state": "held", "hash": "sha256:…" } }
  }
}
```

## 9. The publish report

Built at step 4 (§ 5) and written into the version's folder, as canonical JSON; returned in full by
the publish endpoint and printed by CI whether publish succeeds or not. **Not evidence:** records
never pin it, and it says nothing about a job. It's `publish-report.json`, media type
`application/vnd.consultologist.publish-report+json`, in the version's folder and, for example
packages, a Release asset. It holds the time, each test's result with its hashes, any line-ending
normalisation, the warnings, and the per-job ceiling note
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979256096),
[#1449](https://github.com/Consultologist/Consultologist-Blazor/issues/1449#issuecomment-6957669037)).

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
| `normalised` | array of string | never | the paths whose line endings were normalised at step 0; `[]` for none ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6984743967)) |
| `refusals` | array of `{ sentence, at }` | never | every refusal sentence, with its file path; `[]` when published |
| `warnings` | array of `{ sentence, at }` | never | as `resolve.json`'s `warnings`, the per-job ceiling note included |
| `tests` | array of test result | never | one per test, in id order; `[]` for none |

**A test result:**

| Member | Type | Null | Meaning |
|---|---|---|---|
| `id` | string | never | the test's id |
| `fileHash` | string | never | `sha256:` of the test file's bytes |
| `result` | `"pass"` or `"fail"` | never | named as a check's verdict; `passed` only ever means a count ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)) |
| `outcomeHash` | string | never | `sha256:` over the harness's canonical outcome (row keys, decisions, values, sent-text hashes, deliverable states and hashes), so two runs of the same test can be compared |
| `firstMismatch` | object `{ expectation: string, key: string, expected: string, actual: string }` | allowed | `null` when passed. `expected` and `actual` are hashes for text, values for everything else |
| `durationMs` | integer | never | the test's run time |

## 10. Refusals

The wording below is the starting text. It follows the style rules in [index § 7](index.md#7-refusal-sentences) and is used as written in Milestone 39 and reviewed in #1483 (Milestone 40); every sentence is listed in [refusal-sentences.md](refusal-sentences.md).

**At start** (the Create page, then the engine), from `requires`, `needs` and the engine's break
points (§ 4.4):
- *"This workflow (<package>@<version>) needs <component> <range>; this engine runs <component> <engine version>. Publish a new version of the workflow to run it on this engine."*
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1390#issuecomment-6904042632);
  `<component>` is `Scriban`, `the evaluator` or an operation's name). The same sentence covers a
  package locked before one of the engine's break points (§ 4.4).
- *"This workflow (<package>@<version>) needs <kind or feature> '<name>', which this engine doesn't support. Publish a new version of the workflow to run it on this engine."*
- *"This workflow (<package>@<version>) needs agent <agent name> version <version>, which this engine doesn't have. Publish a new version of the workflow to run it on this engine."*
- *"This workflow needs your signature. Choose one in Profile."*
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1408#issuecomment-6904843222)); for other fields,
  *"This workflow needs your <field label>. Set it in Profile, under Letter details."*
- *"Set your time zone in your account settings before running a workflow."* Every 31 job, until the account's time zone is set ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1406#issuecomment-6958802620); 31 jobs only, [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)); wording
  new. No `needs` member is required, since every 31 job needs the zone.

**At load** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6898433175): the engine refuses a document whose bytes don't match):
- *"Workflow <package>@<version> can't run: its compiled document doesn't match the hash published for it."*
- *"Workflow <package>@<version> can't run: its lock doesn't match the hash published for it, or doesn't belong to its compiled document."* ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660))

**At publish, from tests** (§ 8):
- *"Test '<test id>' failed: <expectation> for <key> expected <expected>, got <actual>. Publish stopped."* The content (the test's name and its first mismatch) is decided.
- *"Test '<test id>' has no stub for <row key>, which runs. Add it to outputs."*
- *"Test '<test id>' stubs <row key> with a value that isn't <format>: <first validation error>."*
- *"Test '<test id>' gives no value for input '<input id>'. List every input; use null for an optional input left unset."*
- *"Test '<test id>' must give exactly the profile fields the workflow reads: <fields>."* and *"Test '<test id>' must give exactly the run facts the workflow reads: <facts>."*
- *"Tests took longer than 60 seconds in all. Publish stopped."*

**Warnings** (never blocking):
- *"Test '<test id>' stubs <row key>, which doesn't run."*
- CI, in the examples repository: *"<path> is evidence written only by publish; it is ignored and shouldn't be committed."*
- The Editor, on import: *"This package's resolve.json and lock.json were ignored; publishing writes new ones."*

## Proposed in this part

Nothing. Every point is decided; refusal sentences are used as written and reviewed in #1483 ([refusal-sentences.md](refusal-sentences.md)).
