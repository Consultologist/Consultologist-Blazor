# 06. Checks, deliverables and the profile

This part defines verification and output: checks in their own folder and root (#1395), check
loops aligned with a production loop's items and the aggregate checks that gate a document
(#1396), deliverables as files with composition templates, derived sources, authored headings,
`whenEmpty`, gating and delivery (#1406, which resolves #1382), and the closed list of profile
fields a template may read (#1408). The template language itself, and what each template may
read, is [Part 05](05-templates-and-macros.md). Personal macros are #1417 (Milestone 40):
a clinician's fixed boilerplate is a profile field (§ 10), not a macro.

## 1. Checks: the folder and the root

A check is **verification, not production:** nothing it produces goes into a note. It has its
own folder, root, record section and History section
([#1395](https://github.com/Consultologist/Consultologist-Blazor/issues/1395)).

```
nodes/     production: agents, rendered and computed nodes, loops, branches, calls
checks/    verification: what gates a deliverable
  index.json
  coverage.md
```

- **The root is `check.<id>`,** singular like `node.`; the folder is plural like `nodes/`.
- **Separate namespaces:** `node.coverage` and `check.coverage` may both exist. Record keys are
  rooted at `node.` or `check.`, with #1384's qualified-key rules inside each
  ([Part 01](01-files-and-scopes.md)).
- **The folder gives the role, `kind` the mechanism, and `format` is always `check_result`.**
  In version 31 the mechanism is `kind: computed`, one JSON Logic expression in the member
  `expression` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6919283640)).
  A model-judged check (`kind: agent`) is left for later.
- **`op` and `with` are retired.** The operations are called inside the expression.
- **The body is empty** ([Part 01](01-files-and-scopes.md)).

### 1.1 The file

`checks/<id>.md` holds the members of a `computed` node ([Part 03](03-expressions.md)), with:

| Member | Type | Null | Meaning |
|---|---|---|---|
| `label` | string | no | the display name |
| `description` | array of strings | yes: none | for people only |
| `kind` | `"computed"` | no | the mechanism |
| `format` | `"check_result"` | no | always; any other value is refused (R-C2) |
| `reproducible` | `true` | no | as on every node header, checked against the derived value ([Part 01](01-files-and-scopes.md)) |
| `producer` | `"computed"` | no | as on every node header, checked against the kind ([Part 02 § 1.3](02-types-and-inputs.md#13-producer), [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)) |
| `modelDerived` | boolean | no | as on every node header, checked against the derived value: `true` when the check reads model output, directly or through other values ([Part 02 § 1.2](02-types-and-inputs.md#12-the-contagious-flags-run-dependent-and-model-derived), [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1478#issuecomment-6982835861)) |
| `runDependent` | boolean | no | as on every node header, checked against the derived value: `true` when the check reads `run.*` or a run-dependent value ([Part 02 § 1.2](02-types-and-inputs.md#12-the-contagious-flags-run-dependent-and-model-derived), [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1478#issuecomment-6982835861)) |
| `if` | JSON Logic | yes: always | whether the check runs; in a check loop, typically skips unrun items. **A gate check's `if`** (a check a deliverable's `check` names) **may not read a value that can be `null`** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660); R-C10), so a gate is never skipped by a missing value |
| `expression` | JSON Logic | no | an expression returning `check_result` (§ 2) |

```
checks/coverage.md
---
{ "kind": "computed", "producer": "computed", "label": "Terms covered", "description": null, "format": "check_result",
  "reproducible": true, "modelDerived": true, "runDependent": false, "if": null,
  "expression": { "terms_subset": [ { "var": "node.extract_input_terms.value" },
                                    { "var": "node.extract_note_terms.value" },
                                    "The note does not cover every clinical term found in the referral." ] } }
---
```

### 1.2 Who reads checks, and what checks read

- **Nodes may read checks:** a prompt, a `rendered` node, a `computed` node or a condition may
  read `check.x.value`, as a dependency edge like any read.
- **Deliverables may read checks,** except a check on that deliverable (§ 6.3).
- **Checks may read** nodes, inputs, other checks, and **a deliverable's `.content`** (that's
  how a document is proofread before release, § 7). A deliverable is readable by checks only.
- **A deliverable's `check` names only a `check.*`,** never a production node.
- **`check_result` as data:** a `computed` node in `nodes/` (or inside a function) may return a
  `check_result` as data only. It gates nothing: **only a `check.*` can gate a deliverable**
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6978700251)). That's how #1395's "a check placed in `nodes/`" refusal is read, consistent
  with [#1419](https://github.com/Consultologist/Consultologist-Blazor/issues/1396#issuecomment-6905675034).
- **Scheduling is unchanged:** a check sits in the dependency graph after what it reads, and is
  recorded with its hashes. A failing check never stops production; its effect comes only
  through what reads it.

## 2. `check_result` and the three check operations

### 2.1 The format

`check_result` is an object format in the package-format registry's built-ins
([Part 02](02-types-and-inputs.md)); a check that returns it has `producer: "computed"`. Version 30's `terms_subset`
output is `{ pass, uncovered, untested }`. Version 31 has two more operations returning it, so
there's **one shape for all three, every member present** (#1394,
[decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6978700251)):

```jsonc
{ "type": "object", "additionalProperties": false,
  "required": [ "pass", "message", "uncovered", "untested", "passed", "total" ],
  "properties": {
    "pass":      { "type": "boolean" },
    "message":   { "type": [ "string", "null" ] },
    "uncovered": { "type": [ "array", "null" ], "items": { "type": "string" } },
    "untested":  { "type": [ "array", "null" ], "items": { "type": "string" } },
    "passed":    { "type": [ "integer", "null" ] },
    "total":     { "type": [ "integer", "null" ] } } }
```

| Member | Meaning |
|---|---|
| `pass` | whether the check passed; what a body `if` and a gate test |
| `message` | the operation's `failWith` sentence when `pass` is `false`; `null` when it passed |
| `uncovered`, `untested` | `terms_subset`'s lists, as version 30 computes them; `null` from the aggregates |
| `passed`, `total` | the aggregates' counts; `null` from `terms_subset` |

**Retention goes by where text came from, not what it says** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6978700251)):
- **`uncovered` and `untested`** come from inputs, so they follow the text retention rule: kept
  7 days, then deleted, with their hash kept for good (#368). This holds for every package,
  medical or not.
- **`message`** is package text (the author's `failWith`), so it's kept, as are `pass` and the
  counts.

### 2.2 Signatures

Fixed arity, versioned like the other built-in operations (in `requires` and `lock.json`)
([signatures](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6919283116)):

| Operation | Arguments | Returns | Passes when |
|---|---|---|---|
| `terms_subset` | `of` (concept_list), `in` (concept_list), `failWith` (string literal) | `check_result` | every concept of `of` appears in `in`, as at version 30 |
| `all_pass` | `list` (of `check_result`), `failWith` (string literal) | `check_result` | every judged element passed; a `null` or empty list fails (below) |
| `count_pass` | `list` (of `check_result`), `min` (non-negative integer literal), `failWith` (string literal) | `check_result` | at least `min` judged elements passed |

- **`failWith` must be a literal string,** checked at publish: a failed check speaks the
  package's own sentence, never a computed one.
- **`list` may be a check loop's `.items`,** as the decided examples write it
  (`{ "var": "check.part_coverage.items" }`,
  [#1396](https://github.com/Consultologist/Consultologist-Blazor/issues/1396#issuecomment-6919284053)).
  **Check operations judge only `completed` items** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6978700251)): an element is *judged*
  when its `state` is `completed`, and its `value` is the `check_result`; `skipped` and
  `not_run` elements are left out of `passed` and `total`. A plain `list<check_result>` (a
  loop's `.value`) judges every element.
- **`min` is a non-negative integer literal** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6978700251)), since it's the package's own
  rule, like `failWith`. With a literal, `min` is never null.
- **Null and empty lists** are [Part 03](03-expressions.md)'s (the operations' null behaviour).
  **A null or empty list fails `all_pass`** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1396#issuecomment-6958687810)): a gate that judged nothing hasn't
  passed (JSON Logic's `all` is false on an empty array); a package that accepts nothing judged
  writes `count_pass(list, 0, …)`. The cases are revisited in #1461 (Milestone 40). `terms_subset` reads a null list as empty.
- **Whatever the arguments, an operation returns a `check_result`, never `null`,** so a gate
  always has a result to read ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6978700251)).

## 3. Check loops

A check that runs per item of a production loop is **a loop inside `checks/`**, never a check
inside a production loop folder
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1396#issuecomment-6905675034)).

### 3.1 Alignment through `.items`

A check loop **iterates the production loop's `.items`** ([Part 04](04-loops-and-functions.md)):
each element pairs a production item with its outputs **by identity**, so the check for
section 2 reads section 2's outputs. Positions are never matched up, and nothing reads into
another loop's body.

- In the check loop, `item` is the `.items` element: `item.value` (the production item's
  returned value), `item.nodes.<id>.value` (its body nodes' outputs), `item.state` and
  `item.reason`. **Check items share the production items' ids** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6978700251)):
  `check.part_coverage.coverage:2` lines up with `node.sections:2`.
- **Unrun items** keep their `state` (`skipped`, `not_run`). The check's header `if` skips them:
  `{ "===": [ { "var": "item.state" }, "completed" ] }`.
- The loop's `index.json` has every member of a `for` ([Part 04](04-loops-and-functions.md)):
  `parallel`, `maxParallel`, `until` and `max` apply, within `FormatBounds`. **Check items are
  named with `"itemLabel": "label"`** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6978700251)): the `.items` element's own `label`,
  which carries the production item's name ([Part 04](04-loops-and-functions.md)).
- **A check loop returns `list<check_result>`;** its `ret` names the check in its body.
- A check loop's body holds checks only (`kind: computed`, `format: check_result`).
  **Branches and nested loops in `checks/` follow `nodes/`'s rules** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6978700251)), and hold
  checks only, like a check loop's body. A `case` there is exhaustive, as everywhere
  ([Part 04](04-loops-and-functions.md)).

### 3.2 Scheduling

Each item's check runs **as soon as that item's outputs are ready,** so checking overlaps
production. Checks are `computed` and make no model calls, so they take **no slot** under the
per-job ceiling (#1393, [Part 09](09-engine-and-pages.md)), which counts agent calls only
([Part 04 § 8](04-loops-and-functions.md#8-concurrency-what-counts-toward-the-per-job-ceiling)).
An agent node inside a check loop's body counts like any other.

### 3.3 No checks inside functions

Checks gate releases, and functions release nothing. A function may return a `check_result` as
data, and checking each call is a check loop over the calls' `.items`.

### 3.4 Example (a made-up package)

`example-checked-sections`, with fictional inputs only. The production loop drafts each planned
section and extracts its terms:

```
nodes/plan.md                    kind agent, format section_plan
nodes/sections/index.json           for over node.plan.value.sections, itemLabel "title"
nodes/sections/planned_terms.md     kind agent, format concept_list   (the terms the plan names)
nodes/sections/draft.md             kind agent, format plaintext
nodes/sections/section_terms.md     kind agent, format concept_list   (the terms the draft covers)
nodes/sections/section.md           kind computed, format titled_section (the loop's ret, § 6.5)
checks/part_coverage/index.json
checks/part_coverage/coverage.md
checks/all_sections_covered.md
deliverables/index.json
deliverables/note.md
```

```jsonc
// checks/part_coverage/index.json
{ "kind": "for", "producer": "computed", "label": "Section coverage", "description": null,
  "format": { "type": "array", "items": { "$ref": "check_result" } }, "reproducible": true,
  "modelDerived": true, "runDependent": false,
  "in": "node.sections.items", "itemLabel": "label",
  "parallel": true, "maxParallel": null, "until": null, "max": null,
  "nodes": [ "coverage" ], "ret": "check.part_coverage.coverage" }
```

```
checks/part_coverage/coverage.md
---
{ "kind": "computed", "producer": "computed", "label": "Section covers its planned terms", "description": null,
  "format": "check_result", "reproducible": true, "modelDerived": true, "runDependent": false,
  "if": { "===": [ { "var": "item.state" }, "completed" ] },
  "expression": { "terms_subset": [ { "var": "item.nodes.planned_terms.value" },
                                    { "var": "item.nodes.section_terms.value" },
                                    "The section does not cover every term its plan names." ] } }
---
```

## 4. Aggregates and the gate

**The whole document is gated by an explicit aggregate check:** a `computed` check returning one
`check_result`, which the deliverable's `check` names. The rule is written and typed, never
implied. There's **no "partly released" state**
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1396#issuecomment-6905675034)).

```
checks/all_sections_covered.md
---
{ "kind": "computed", "producer": "computed", "label": "Every section covers its planned terms", "description": null,
  "format": "check_result", "reproducible": true, "modelDerived": true, "runDependent": false, "if": null,
  "expression": { "all_pass": [ { "var": "check.part_coverage.items" },
                                "Not every section covers its planned terms." ] } }
---
```

"At least 8 of 9" is `{ "count_pass": [ { "var": "check.part_coverage.items" }, 8, "Fewer than 8 sections cover their planned terms." ] }`.

**Single sections are handled in the template.** A deliverable may read checks other than its
own (§ 6.3), so its template chooses how a failing section appears, by a body `if` on
`check.x.value.pass` or on an item's state ([Part 05](05-templates-and-macros.md#25-a-body-if-tests-recorded-facts-only)).

## 5. Checks in the record and History

**The record** has a checks section apart from nodes. A top-level check's key is `check.<id>`;
a check loop's items are `check.part_coverage.coverage:2`, with the production item's id and
named by the loop's `itemLabel`, each linking to its production item (`node.sections:2`). The
row holds the check's reads and their hashes, its result (the `check_result`, with `uncovered`
and `untested` retained 7 days, then their hashes and sizes, § 2.1, #368), and its effect. The
shape is [Part 08](08-record.md)'s.

**History** has four sections in the order the job runs
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6881869311)):
Inputs, Nodes, Checks, Deliverables. Each check row says:

| Line | Shows |
|---|---|
| checks | what it ran on (a deliverable or a node), with that output's hash |
| reads | its other inputs, each linking to its node's row |
| result | pass or fail with its detail (*"12 of 12 terms present"*, *"7 of 9 sections passed"*) |
| effect | released, held, blocked, or read by something else |

```
 ┌ Checks ─────────────────────────────────────────────────────────────────────┐
 │ ▾ part_coverage     7 of 9 passed   for over node.sections.items            │
 │     ▸ Oncologic History           passed                                    │
 │     ▸ Surgery and Pathology       failed   2 planned terms missing          │
 │ ▾ all_sections_covered   failed   all_pass → check_result     b19e…         │
 │     reads           check.part_coverage (9 items, 7 passed)                 │
 │     result          Not every section covers its planned terms.             │
 │     effect          note blocked                                            │
 └─────────────────────────────────────────────────────────────────────────────┘
```

- **No duplication:** a deliverable row shows a one-line `Check: <id>  <passed|failed> →`
  linking to the check's row. A node a check reads gets no extra row.
- **A failed check that stops a deliverable is a banner at the top of the job:** *"Consultation
  note was blocked: check 'coverage' failed (10 of 12 terms present)."* A gate check that
  errored gives *"Consultation note was blocked: check 'coverage' could not run (\<error\>)."*
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)). A destination the account hasn't set up has its own banner line (§ 8).
- **The diagram** draws a Checks frame, a thick `gates` edge into the deliverable, and a check
  loop as a frame inside it with a dotted `aligned` edge to its production loop
  ([#1398](https://github.com/Consultologist/Consultologist-Blazor/issues/1398#issuecomment-6950081672),
  [Part 09](09-engine-and-pages.md)).

## 6. Deliverables as files

### 6.1 The file

**Each deliverable is `deliverables/<id>.md`** (or in a sub-folder, § 8): a JSON header, then a
non-empty body that is its **composition template**. A deliverable is a `kind: rendered` leaf;
the folder gives its role. There's no implicit template: the Editor and the command-line tool
generate the file, and the migration writes one per deliverable
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1406#issuecomment-6881577331)).

**A deliverable's id is its path, like a node's** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)): `deliverables/consult.md`
is `deliverable.consult`, and `deliverables/emr/note.md` is `deliverable.emr.note`. The
destination folder is part of the id, so the same file name may repeat in different folders
(`emr/note.md`, `email/note.md`), and moving a deliverable changes its key. The key, with its
root, is what the record, History and `WorkflowPackages/Current`'s `Results` use
([Part 09](09-engine-and-pages.md)).

| Member | Type | Null | Meaning |
|---|---|---|---|
| `label` | string | no | the display name |
| `kind` | `"rendered"` | no | the mechanism |
| `description` | array of strings | yes: none | for people only |
| `format` | `"markdown_block"`, `"plaintext"`, or an object format | no | the composed output's format (§ 6.4) |
| `if` | JSON Logic | yes: always | whether the deliverable exists (§ 6.3) |
| `check` | string, `check.<id>` | yes: ungated | the check that gates release (§ 7) |
| `whenEmpty` | `"release"`, `"hold"` or `"fail"` | no | what happens when the composed text is empty (§ 6.6) |

- **`sources` is removed.** The template's reads are the deliverable's sources: the resolver
  derives them, records them in `resolve.json` and draws them in the diagram
  ([Part 07](07-compiled-documents.md)).
- **`macros` and `signature` are removed:** deliverables don't call macros (#1409), and the
  signature is a profile field (§ 10).
- **`markdown_inline` is refused as a deliverable format** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6978700251)): a document stands
  on its own lines.

### 6.2 Item states

A template reads each loop item's `state` and `reason` through `.items`
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1406#issuecomment-6881816481)):

| `state` | Meaning | `reason` |
|---|---|---|
| `completed` | the item ran | `null` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6978700251)) |
| `skipped` | its `if` didn't hold | the condition's printed text |
| `not_run` | stopped early by `until` | *"stopped early at <position name>"*, for example *"stopped early at Sections 3"*: reasons name items by position, never by a drawn name ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)) |

- The set is closed and checked at publish. A failed item fails the job (`onInvalid` is `fail`
  or `retry`), so it never reaches a template.
- **`reason` is never patient text:** it's built only from package text and fixed engine
  sentences, because it can land in the deliverable.
- A branch on `state` handles every state or ends with an `else`; an unknown state is refused
  ([Part 05](05-templates-and-macros.md#25-a-body-if-tests-recorded-facts-only)).

### 6.3 What a deliverable reads

**The template** reads `node.*`, `input.*`, `snippet.*`, `check.*` (not its own check),
`profile.*`, the `run.*` set and the `renderer.` pipe
([Part 05](05-templates-and-macros.md#31-the-table)). Each read of `run.*` or of a
run-dependent value is a run spot (Part 05 § 4).

**Refused:**
- other deliverables (deliverables are terminal, § 6.7);
- `macro.*` and `part.*`;
- **its own check:** the check runs on the deliverable, so reading it is a cycle (R-D2).

**The header `if`** decides, before composition, whether the deliverable exists. It may read
`input.*`, a node's `.value` (data) and `run.*`. It may not read `.content` (text), `check.*`
(a possible cycle), `profile.*` or deliverables
([corrected](https://github.com/Consultologist/Consultologist-Blazor/issues/1406#issuecomment-6924497456)).
When it doesn't hold, the deliverable is **skipped by condition**, recorded with the
condition's printed text, never the value it read.

### 6.4 Strict formats

**A deliverable's reads must match its format.** Conversions happen in nodes (a `computed`
node with `plain`, or a renderer); the only automatic step is escaping
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1406#issuecomment-6881816481)).

| Deliverable `format` | May read | Refused |
|---|---|---|
| `markdown_block` | `markdown_block`; `markdown_inline` inside a line; plain-text formats (escaped); `string` and `number` data (escaped) | objects (use a renderer or a node) |
| `plaintext` | plain-text formats; `string` and `number` data | any Markdown (convert in a node) |
| an object format | exactly that format, as `{{ node.x.value }}` and nothing else | everything else |

- **Escaping:** plain text and data read into Markdown are escaped, protecting only the
  characters Markdown would misread (`*`, `#`, `_`, `[`, …), so the displayed text is exactly
  the plain text. The substitution's hash is taken **after** escaping. **The escape set is
  CommonMark's backslash escapes for ASCII punctuation,** published with test vectors in the
  conformance suite ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6978700251)).
- **The template's own literal text is typed too:** a `plaintext` deliverable may not write
  Markdown syntax (a line starting `#`, `-`, `*` or `>`, or a `**` pair). A Markdown
  deliverable's literal text is the author's Markdown. An object deliverable allows nothing
  but its single expression.
- **An object deliverable's output** is canonical JSON (RFC 8785).
- **`profile.signature` and `profile.closing` are `plaintext`,** so a Markdown deliverable reads
  them escaped.

### 6.5 Headings are the author's

**The template writes the headings,** which resolves #1382: there are no invented
`## Paragraphs 1` headings
([#1382](https://github.com/Consultologist/Consultologist-Blazor/issues/1382#issuecomment-6905790583)).
#1382 closes once this is running and verified.

**The pattern for a loop's sections: a closing `computed` node builds the pair** and is the
loop's `ret`. With the format `titled_section`:

```jsonc
// formats/titled_section.json (declared by the package; not a built-in at 31)
{ "$schema": "urn:consultologist:spec:31:dialect",
  "type": "object", "required": [ "title", "content" ], "additionalProperties": false,
  "properties": { "title": { "type": "string" }, "content": { "$ref": "plaintext" } } }
```

```
nodes/standards/section.md
---
{ "kind": "computed", "producer": "computed", "label": "Section", "description": null, "format": "titled_section",
  "reproducible": true, "modelDerived": true, "runDependent": false, "if": null,
  "expression": { "object": { "title":   { "var": "item.value.title" },
                              "content": { "var": "node.section_instructions.content" } } } }
---
```

- `titled_section` is **declared by the package, not a built-in at 31** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6978700251)).
- The loop returns `list<titled_section>`. **Formats are shape only:** `content` is `plaintext`,
  the same format the agent's text has; the node's producer is `computed`, and `reproducible`
  stays `true` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)). Where the text came from is the **model-derived** flag instead:
  the row is marked model-derived, and History, the diagram and the Editor say *"computed from
  model text: node.standards.section_instructions"* ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6962982698),
  [Part 03](03-expressions.md)).
- The node's row hashes prove which title went with which text.
- **`.items` is always available too,** for reaching a skipped item's `state` and `reason`.
  Zipping two lists by position is not offered.
- **A loop's `.content`** keeps version 30's bytes as a standard rendering
  ([Part 05](05-templates-and-macros.md#53-built-in-renderers)), so a migrated template can
  reproduce today's note exactly.

**`test/general`'s deliverable, after the author's edit** (the migration first writes a template
reproducing version 30's bytes, [Part 10](10-migration-and-tools.md)):

```
deliverables/consult.md
---
{ "label": "Consultation note", "kind": "rendered", "description": null, "format": "markdown_block",
  "if": null, "check": null, "whenEmpty": "hold" }
---
{{~ for s in node.standards.value ~}}
## {{ s.title }}

{{ s.content }}

{{~ end ~}}
{{~ snippet.test.content }}
```

Version 30's `test` macro (`at: append`, "the last line of the note") becomes `snippets/test.md`,
read verbatim at the end
([#1409](https://github.com/Consultologist/Consultologist-Blazor/issues/1409)).

**`example-nested-note`, with headings the author chose.** The inner paragraphs get no heading.
Its loop `parts` is renamed `sections`, since `parts` is a reserved id
([Part 01](01-files-and-scopes.md)). A closing node in `sections/` builds
`{ title, paragraphs }` (a package format `titled_paragraphs`, `paragraphs` a list of
`plaintext`, model-derived), and the template reads:

```
deliverables/note.md
---
{ "label": "Nested consultation note", "kind": "rendered", "description": null,
  "format": "markdown_block", "if": null, "check": null, "whenEmpty": "fail" }
---
## Summary

{{ node.refine.content }}
{{~ for s in node.sections.value ~}}

## {{ s.title }}
{{~ for p in s.paragraphs ~}}

{{ p }}
{{~ end ~}}
{{~ end ~}}
```

Each paragraph is `plaintext`, so it's escaped into the Markdown note.

**The `whenEmpty` values above are the migration's** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1447#issuecomment-6980651765),
[Part 10](10-migration-and-tools.md)): the example packages are hand-chosen, `"hold"` for
`test/general`, `"fail"` for `example-nested-note` and `"release"` for the rest. Account packages
migrate with `"release"`, which keeps version 30's behaviour of producing an empty deliverable as
is.

### 6.6 When a deliverable comes out empty

**Empty means** the composed text, after trimming whitespace, is empty. The template's own
fixed text (a heading) counts as content, so an author who wants nothing when there are no
sections puts the heading inside a loop or a recorded-fact `if`.

| `whenEmpty` | Effect |
|---|---|
| `release` | an empty document is released and recorded |
| `hold` | not released, recorded as *held: empty*; the other deliverables go on |
| `fail` | the job fails, naming the deliverable |

**An object-format deliverable is never empty** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6978700251)): its canonical JSON is never
blank, so `whenEmpty` has no effect there, but is still written (#1394).

### 6.7 Deliverables are terminal

- **Nodes may not read deliverables,** and other deliverables may not either.
- **Checks may** read a deliverable's `.content`.
- Text a later node wants to reuse is made by a `rendered` node, which both read.
- **The graph:** nodes, then deliverables, then their checks, then release.

## 7. Gating and outcomes

**A deliverable's check is its final proofread before sending.** The check named in `check` may
read the composed deliverable (`deliverable.<id>.content`, for example
`deliverable.emr.note.content`) or only nodes. It runs after
everything it reads, and release waits for it
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1406#issuecomment-6881816481)).

**The outcomes, in order of evaluation:**

| Outcome | When | Recorded as | The job |
|---|---|---|---|
| skipped | the header `if` didn't hold | skipped by condition, with the printed condition | goes on |
| failed | composed empty with `whenEmpty: fail`, or the render failed | the deliverable's failure | fails, naming the deliverable |
| held | composed empty with `whenEmpty: hold` | *held: empty* | goes on |
| blocked | its gate check returned `pass: false`, or **errored** (failed to run) | composed, blocked: fails closed, with the check's result or error recorded, never delivered ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)) | goes on |
| released | composed, and its check passed, its check was skipped by its own `if`, or it has none | composed, released, then a delivery result per destination (§ 8) | goes on |

- **`blocked` is an outcome; `not sent` is a delivery result** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)). A deliverable is
  released, held, failed, skipped or blocked; each destination of a released deliverable is then
  sent or not sent (§ 8).
- A deliverable that's blocked or held is **never something to dig for:** History shows the
  banner (§ 5).
- **A deliverable whose gating check was skipped by its own `if` is released**
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6978700251)), recorded and shown in History as *"released: check '<id>' didn't apply (its
  if was false)"* (H-4). A skipped gate gives no `check_result` to fail on. **An empty
  `all_pass`, by contrast, fails** (§ 2.2): that check ran and judged nothing.
- The record holds the outcome as the deliverable row's `state` (`released`, `held`, `failed`,
  `skipped` or `blocked`) with its reason sentence, and each destination's result in
  `delivery` ([Part 08](08-record.md)).

## 8. Delivery

**Delivery is declared on folders only.** The root `deliverables/index.json` is required;
routing a deliverable differently means putting it in a sub-folder
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1406#issuecomment-6881816481)).
**Delivery folders are one level deep** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)), and the folder is part of each
deliverable's id (§ 6.1): `deliverables/patient/patient_letter.md` is
`deliverable.patient.patient_letter`.

```
deliverables/
  index.json          { …, "delivery": ["emr"], "bundle": null }
  consult.md
  patient/
    index.json        { …, "delivery": ["email"], "bundle": null }
    patient_letter.md
  referral_pack/
    index.json        { …, "delivery": ["email"], "bundle": "zip" }
    gp_letter.md
    summary.md
```

| Member | Type | Null | Meaning |
|---|---|---|---|
| `description` | array of strings | yes: none | for people only |
| `items` | array of ids | no | the deliverables and sub-folders, in order ([Part 01](01-files-and-scopes.md)) |
| `delivery` | array of `"email"`, `"pdf"`, `"emr"`, `"zip"` | no | the kinds of destination; `[]` means deliberately not delivered (kept in History only) |
| `bundle` | `"zip"` | yes: none | send the folder's deliverables together, as one archive |

- **No inheritance between folders:** every `index.json` states its own delivery in full.
- **The package declares kinds, never addresses** (packages are public). **The account maps
  kinds to endpoints** in a separate account section, beside today's delivery address; templates
  never read it ([#1408](https://github.com/Consultologist/Consultologist-Blazor/issues/1408#issuecomment-6904843222)).
- **Delivery happens at the end, and only then** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)): nothing is delivered until every
  deliverable's outcome is final and the job has succeeded. A job that fails delivers nothing,
  and its released deliverables stay in History, recorded as *"not sent: the job failed"*.
- **A result per destination** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)): each kind a released deliverable goes to gets its own
  result, `sent` or `not sent` with a reason, so an email that went out beside an EMR that
  isn't set up reads as one of each.
- **A kind the account hasn't configured:** the job still runs, and the deliverable is produced;
  that destination is recorded as **not sent**: *"not sent: EMR isn't set up for this account"*.
  The job doesn't fail, and the document stays in History.
- **A bundle holds released deliverables only** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6978700251)): a held, skipped, failed or
  blocked one is left out, and History lists what was left out and why.
- `description` and `items` follow every folder's `index.json` ([Part 01](01-files-and-scopes.md));
  #1406's examples show only `delivery` and `bundle`.

## 9. Deliverables in the record and History

**The row's key** is the deliverable's id with its root (`deliverable.emr.note`), and a renderer
use under it is `deliverable.emr.note/render:1` ([Part 08](08-record.md)).

**Hashes:** the template's hash before substitution, one hash per substitution, and the hash of
the final text (`composedHash`)
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1406#issuecomment-6881577331)).

- **Sites:** each expression in the template, numbered in source order, with its text and
  position (package text, safe to keep).
- **Substitutions:** each evaluation (once, or once per loop pass), naming its site, its `pass`
  (a list, one number per enclosing `for`), what it read, and its hash and size. Renderer uses
  add `renders` ([Part 05](05-templates-and-macros.md#55-in-the-record-and-history)). Profile
  reads are substitutions like any other.
- **Replay:** substituting the recorded values into the template, in site and pass order,
  reproduces `composedHash`. Escaped reads are recorded after escaping, so replay holds.
- **The template is kept by reference:** the record stores `templateHash`, and History shows the
  text from `resolve.json` of the pinned version. It's never deleted, since it holds nothing
  personal.
- **Run spots** add the run-neutral hash beside `composedHash`
  ([Part 05](05-templates-and-macros.md#4-run-spots-and-the-two-hashes)).
- **The outcome** (§ 7, the row's `state`) and each destination's delivery result (§ 8).

The record's shape and field names are [Part 08](08-record.md)'s. **History** shows the
template with its reads grouped by site, looped sites collapsed to "× N passes" and expandable
per pass named by `itemLabel`, values while retained (7 days), then hashes and sizes, and the
one-line check status.

## 10. The profile

`profile.*` is personal data about the clinician: not patient data, but it ends up in
deliverable text and hashes. The format says exactly what may be read, how it's typed, and how
it's recorded ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1408#issuecomment-6904843222)).

### 10.1 The closed list

| Field | Format | Null | Preview value (fictional) |
|---|---|---|---|
| `profile.name` | `string` | never: a missing value refuses the job (§ 10.4) | `Dr A. Sample` |
| `profile.credentials` | `string` | never | `MD, FRCPC` |
| `profile.organisation` | `string` | never | `Sample Oncology Clinic` |
| `profile.signature` | `plaintext` | never | the chosen signature block |
| `profile.closing` | `plaintext` | never | a closing line or contact block |

- **Adding a field later is compatible;** removing or retyping one is a format change.
- `profile.closing` is the home of a clinician's fixed boilerplate (#1412). Personal macros are
  #1417.
- **Personal snippets are unchanged:** they're inserted into inputs on the Create page and
  recorded with the input. There are no `profile.snippets` reads.
- **Delivery endpoints aren't profile fields** (§ 8).

### 10.2 Where reads are allowed

| Place | `profile.*` |
|---|---|
| deliverable templates | yes |
| macro bodies (rendered into the reply, never sent to a model) | yes |
| snippet bodies placed in either of those | yes |
| prompts, `rendered` nodes, parts, macro instructions, snippets placed in prompts | **no:** the clinician's details aren't sent to a model |
| conditions and `computed` expressions, the deliverable header `if` included | **no** ([confirmed](https://github.com/Consultologist/Consultologist-Blazor/issues/1408#issuecomment-6924498184)) |
| a **profile-carrying** value (text a profile-reading macro body was substituted into, and anything made from it; [Part 03 § 4.6](03-expressions.md#46-profile-carrying-is-contagious)) | deliverables only: a prompt, a part or a condition that reads one is refused (R-F5, [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)) |
| inside a function | only as a parameter passed in through the call's `with`, and then only to macro bodies |

**A parameter bound to a `profile.*` value carries the same restriction through the function**
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1408#issuecomment-6959886233), [#1419](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6959886713)): reading it in a prompt, a part or a condition is refused (R-F3), just as reading
`profile.*` there would be.

### 10.3 The signature

- `profile.signature` is the account's **chosen block** (`ChosenId`; an account holds up to 5
  named blocks, each up to 4,000 characters).
- **The Create page shows a signature choice only when the account has more than one block and
  the package reads `profile.signature`,** defaulting to the chosen block
  ([decided 2026-11-17](https://github.com/Consultologist/Consultologist-Blazor/issues/1408#issuecomment-6957668236),
  [refined](https://github.com/Consultologist/Consultologist-Blazor/issues/1393#issuecomment-6980307806)).
- **The record names the block's id,** never its text: `signatureBlockId`, beside
  `signatureChosen`, which is `"default"` (the account's chosen block) or `"job"` (picked on the
  Create page) ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979794633)). Packages never name blocks. There's no signature snapshot at
  31: the signature is a profile read, recorded by hash, and its text lives in the deliverable,
  under text retention.

### 10.4 A missing field refuses the job

**When the package reads a field the account hasn't set, the job is refused before it starts,**
on the Create page. There's no empty substitute. The package's needs are known in advance from
`resolve.json`'s derived `needs.profile` (for example `["name", "signature"]`,
[Part 07](07-compiled-documents.md)).

**This refusal applies to 31 jobs only** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)): a job on a package still at 30 keeps 30's
behaviour until its package moves. The owners' notice before the cutover says so (Part 10). When
the email intake refuses a job for this reason, its reply carries the same sentence (J-1, J-2).

**An empty or whitespace-only value counts as not set** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6978700251)), so a job that reads
it is refused at the start.

### 10.5 Recording and retention

- Each `profile.*` read is a substitution recorded by hash (§ 9).
- The record names the signature block's id.
- The text follows produced text's retention: deleted after 7 days (#368). A value that is
  profile-carrying (§ 10.2) follows the same retention.
- **A short value's hash is kept,** as every substitution's is, and the record's documentation
  says it can be reversed by guessing: a name or credentials line is short enough to test
  candidates against its hash ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660), option c). Long texts keep their hash for verification as
  before.

### 10.6 Previews

The Editor, the debugger (#1413) and preview panes use **fictional sample values from the
registry** (the table in § 10.1), never the author's own profile.

### 10.7 The Profile page and the time zone

- **Profile gains a "Letter details" card** for `credentials`, `organisation` and `closing`
  ([decided 2026-11-17](https://github.com/Consultologist/Consultologist-Blazor/issues/1408#issuecomment-6957668236)),
  with limits of 120, 200 and 4,000 characters (the last the same as a signature block,
  [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1393#issuecomment-6980307806)).
- **The account time zone** (an IANA name, for `run.date`) is an **account setting, not a profile
  field:** it's never read as `profile.*`. It's a select list that starts empty, with a "Use this
  browser's zone" button that fills it only when clicked. The zone is recorded beside
  `run.date`. **Every job is refused until it's set**, whether or not the package reads
  `run.date` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1406#issuecomment-6958802620)): *"Set your time zone in your account settings before running a workflow."* Like § 10.4's
refusal, it applies to 31 jobs only, is announced in the owners' notice, and is the email
intake's reply when it refuses ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)). The pages themselves are [Part 09](09-engine-and-pages.md)'s.

## 11. What changes from version 30

| At 30 | At 31 |
|---|---|
| a check is a node in `nodes`, `op: terms_subset` with `with: { of, in, failWith }`, read as `node.coverage` | `checks/<id>.md`, `kind: computed`, `expression` with `terms_subset`, read as `check.coverage` |
| checks are top-level only | check loops over a production loop's `.items`, gated by `all_pass` or `count_pass` |
| a deliverable is a manifest entry with `sources`, `macros` and a `check` naming a node | `deliverables/<id>.md` with a composition template, its id its path (`deliverable.emr.note`); sources derived; `check` names a `check.*`; `whenEmpty` |
| the composer writes `## {item name}` over every loop item, at every depth (#1382) | the template writes the headings; `.content` keeps the old bytes as a standard rendering |
| deliverable macros at `before`, `after`, `append` | snippets read in the template |
| one delivery address per account | delivery kinds per folder, mapped to endpoints by the account |
| `profile.name`, `profile.signature`; an unset value renders as empty text | five typed fields; an unset field the package reads refuses the job |
| `profile.*` readable in prompts | deliverables, macro bodies and snippets placed in them only |

## Refusals

The wording below is the starting text. It follows the style rules in [index § 7](index.md#7-refusal-sentences) and is used as written in Milestone 39 and reviewed in #1483 (Milestone 40); every sentence is listed in [refusal-sentences.md](refusal-sentences.md).

`<…>` are placeholders. Sentences marked *(decided)* are quoted from the deciding comment; the
rest are this part's wording.

**Checks**
- R-C1: *"Deliverable '<id>' is checked by <reference>; a deliverable's check names a check.*."*
- R-C2: *"Check '<id>' has format <format>; a check returns check_result."*
- R-C3: *"Check '<id>' has kind <kind>; a check is computed."*
- R-C4: *"<Path> in checks/ is <kind>; checks/ holds checks and the loops over them."*
- R-C5: *"Check '<id>' passes <expr> as failWith; failWith is a literal sentence."*
- R-C6: *"Check '<id>' passes <expr> as min; min is a whole number of at least 0."*
- R-C7: *"Check loop '<id>' runs over <source>; a check loop runs over a production loop's .items."*
- R-C8: *"Function '<id>' holds check '<check>'; functions hold no checks. Return a check_result as data and check the calls' .items."*
- R-C9: *"Node '<id>' reads deliverable.<deliverable>; only checks read deliverables."*
- R-C10: *"Check '<id>' gates deliverable '<deliverable>', and its if reads <path>, which can be null; a gate check's if reads values that are always set."*

**Deliverables**
- R-D1 *(decided)*: *"Node '<id>' reads deliverable.<deliverable>; deliverables are the job's outputs: read the nodes it is composed from."*
- R-D2 *(decided)*: *"Deliverable '<id>' reads check.<check>, which checks <id>: a deliverable can't read its own check."*
- R-D3: *"Deliverable '<id>' reads deliverable.<other>; a deliverable reads nodes, inputs, snippets, checks, the profile and run facts."*
- R-D4: *"Deliverable '<id>' has format <format>; a deliverable is markdown_block, plaintext or an object format."*
- R-D5: *"Deliverable '<id>' is <format>, and reads <path> (<read format>) at line <n>. <Convert it in a node | Use a renderer or a node>."*
- R-D6 *(decided)*: *"Deliverable 'letter' is plaintext, and line 4 writes Markdown ('## Plan')."*, generalised as *"Deliverable '<id>' is plaintext, and line <n> writes Markdown ('<text>')."*
- R-D7: *"Deliverable '<id>' is <object format>; its template is {{ node.<x>.value }} and nothing else."*
- R-D8: *"Deliverable '<id>' reads <path> in its if; a deliverable's if reads inputs, node values and run facts."*
- R-D9: *"Deliverable '<id>' places <macro.* or part.*>; deliverables place snippets, and macros belong to agent nodes."*
- R-D10: *"Deliverable '<id>' has whenEmpty <value>; write release, hold or fail."*
- R-D11: *"Deliverables folder '<path>' has delivery kind <kind>; the kinds are email, pdf, emr and zip."*
- R-D12: *"deliverables/index.json is missing; the deliverables folder states its delivery."*
- R-D13 *(decided, [Part 05](05-templates-and-macros.md#refusals) R-D8)*: *"Deliverable 'consult' branches on part.state and handles completed and skipped, but not not_run: add it or an else."*

**Profile**
- R-F1: *"<Subject> reads profile.<field>, which isn't a profile field; the fields are name, credentials, organisation, signature and closing."*
- R-F2: *"<Condition owner> reads profile.<field> in a condition; the profile is read only in deliverables and macro bodies."*
- R-F3 *(decided: [#1408](https://github.com/Consultologist/Consultologist-Blazor/issues/1408#issuecomment-6959886233), [#1419](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6959886713); § 10.2)*: *"Function '<id>' reads param.<name>, which carries profile.<field>, in <a prompt | a part | a condition>; the profile is read only in deliverables and macro bodies."*
- R-F5: *"<Subject> reads <path>, which carries profile text; prompts, parts and conditions don't read profile text. Read it in a deliverable."*
- R-F4 (Part 05 R-R2, for prompts): *"Node '<id>' reads profile.<field>; the clinician's profile is never sent to a model. Read it in a deliverable or a macro body."*

**Refused at job start** (Create page)
- J-1 *(decided)*: *"This workflow needs your signature. Choose one in Profile."*
- J-2 (the other fields): *"This workflow needs your <field label>. Set it in Profile, under Letter details."*
- J-3 *(rule decided, [every job](https://github.com/Consultologist/Consultologist-Blazor/issues/1406#issuecomment-6958802620); wording new)*: *"Set your time zone in your account settings before running a workflow."*

**Shown in History** (not refusals)
- H-1 *(decided, revised by [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660))*: *"<Deliverable label> was blocked: check '<id>' failed (<detail>)."*
- H-2 *(decided)*: *"not sent: <Kind> isn't set up for this account"*
- H-5: *"<Deliverable label> was blocked: check '<id>' could not run (<error>)."*
- H-6: *"not sent: the job failed"* (each destination of a released deliverable in a job that failed)
- H-3 *(decided)*: *"stopped early at <position name>"* (an item's `reason` for `not_run`)
- H-4 *(decided, [part 06](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6978700251))*: *"released: check '<id>' didn't apply (its if was false)"*

## Proposed in this part

Nothing. Every point is decided; refusal sentences are used as written and reviewed in #1483 ([refusal-sentences.md](refusal-sentences.md)).
