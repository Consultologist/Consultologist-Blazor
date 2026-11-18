# 02. Types and inputs

This part defines the type system of version 31: every node's `kind` (how it runs) and `format`
(what it returns), `reproducible` and the two contagious flags, the JSON Schema dialect formats
are written in, the built-in, catalogue and package formats with their `$id` URNs, the split
between data and text, the annotations (`contentMediaType`, `x-markdown`), each node's
`producer`, how a model's output reaches its attested agent, the input grammar and
how inputs are read, enum labels, per-run macro choices as optional boolean inputs, and how inputs
reach the satellites through `WorkflowPackages/Current`. It covers #1385 and #1061, and the
naming half of #1439. Part 02's own open points were settled on
[#1385](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466),
and the producer on [#1385's revision 4 follow-ups](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868), which replace the format annotation
and name prefix first decided on
[#1439](https://github.com/Consultologist/Consultologist-Blazor/issues/1439#issuecomment-6979257923).
JSON Logic's typed operations are [part 03](03-expressions.md); where each kind's file lives is
[part 01](01-files-and-scopes.md).

## 1. Kind

**Every node declares its `kind`,** the mechanism that makes its value. Kinds are `agent`,
`rendered`, `computed`, `for`, `while`, `if`, `case` and `call`; `let` is no longer a kind
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6919282701)).

| `kind` | Runs | File | Body | Defined in |
|---|---|---|---|---|
| `agent` | a model call, through the attested agent bound to its format (§ 7) | `<id>.md` | its prompt | [part 05](05-templates-and-macros.md) |
| `rendered` | a template, no model | `<id>.md` | its template | [part 05](05-templates-and-macros.md) |
| `computed` | one JSON Logic expression (member `expression`), no model | `<id>.md`, header only | empty | [part 03](03-expressions.md) |
| `call` | a function, its body run inline in the caller's orchestration ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)) | `<id>.md`, header only | empty | [part 04](04-loops-and-functions.md) |
| `for`, `while` | a body per item, or per pass | `<id>/index.json` | — | [part 04](04-loops-and-functions.md) |
| `if`, `case` | one branch | `<id>/index.json` | — | [part 04](04-loops-and-functions.md) |

**The folder gives the role, `kind` the mechanism**
([#1397](https://github.com/Consultologist/Consultologist-Blazor/issues/1397#issuecomment-6877969751)):
a check is `kind: computed` in `checks/`, a renderer and a deliverable are `kind: rendered` in
`renderers/` and `deliverables/`. `op` and `with` are retired: `terms_subset`, `all_pass` and
`count_pass` are operations called inside an expression (part 03).

**`kind: "agent"` says how a node runs; its `producer` says how its output can be verified**
(§ 1.3). The two are separate facts, and the kind keeps its name
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)).

### 1.1 Reproducible

**`reproducible` is written on every node and checked against the derived value;** a mismatch is
refused
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)):

| Node | `reproducible` |
|---|---|
| `rendered`, `computed` | `true` |
| `agent` | `false` |
| a flow node (`for`, `while`, `if`, `case`) | `true` only if everything in its body that its result depends on is `true` |
| `call` | the function's `ret` value |

- **Run-dependence is recorded separately** and doesn't make a node `false`: a body reading
  `run.*` records a run-neutral hash beside the sent-text hash, on which runs are compared
  ([#1389](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6920426229)).
- Revisiting the member is #1466 (Milestone 40).

### 1.2 The contagious flags: run-dependent and model-derived

Three flags spread through the graph: run-dependent, model-derived and, since revision 7,
profile-carrying ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)). The resolver works out all three from `resolve.json`'s edges
before any run, records them on each row as `runDependent`, `modelDerived` and `profileCarrying`
([part 08](08-record.md)), and History, the diagram and the Editor show them
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6962982698)).
**Every node header also writes all three, `modelDerived: true | false`,
`runDependent: true | false` and `profileCarrying: true | false`, checked against the derived values** as `reproducible` and
`producer` are; a mismatch is refused ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1478#issuecomment-6982835861)). The flags are node members, never formats; `profileCarrying` is written since revision 7 ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)).

- **Run-dependent:** the value depends on a `run.*` fact, directly or through anything it is made
  from (part 03).
- **Model-derived:** the value came from a model's output somewhere upstream. It starts at the
  output of every node whose `producer` isn't `computed` (§ 1.3) and at an input element
  taken from a previous run's deliverable (#874); typed text and uploaded documents start clean
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6963556118)).
  It spreads through `computed` and `rendered` nodes, `call` results, flow nodes' `ret` and loop
  items. History shows it as, for example, *"computed from model text: node.draft"*.
- **Profile-carrying:** the value's text had a profile-reading macro substituted into it
  (part 03 § 4.6). It spreads the same way, is written on every node header as `profileCarrying` and checked,
  and is recorded on each row.
  A prompt, a part or a condition may not read a profile-carrying value; a deliverable may.
- **The flags describe content only** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)). They pass along what a value is made from, never
  through a condition: a node chosen by a model-derived `if` isn't model-derived for that. The
  decision has its own row.
- **Formats are shape, never the flags.** `trim` of a model's `plaintext` returns `plaintext`,
  the same format as its operand; its node's producer is `computed`, it stays model-derived, and
  it is `reproducible: true`
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)). Model-derived is a flag on values, never a format.
- Requiring a model-derived or run-dependent value in a read is #1478 (Milestone 40).

### 1.3 Producer

**Every node header writes `producer`,** how its output can be verified. Like `reproducible`, it
is checked against the node's kind, a mismatch is refused, and it is recorded on every row
(member `producer`, [part 08](08-record.md))
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)):

| Node | `producer` at 31 |
|---|---|
| `agent` | `"generated"`: a model wrote it, so it may vary between runs |
| every other kind (`rendered`, `computed`, `call`, `for`, `while`, `if`, `case`), and every check | `"computed"`: no model at this node |

- **Reserved for Milestone 40** (#1439), and refused at 31: `"grounded"` (model text verified
  against the input; #1439's narrowed `agent`), `"extracted"` (OCR and extraction; #1438),
  `"attention"` (attention readings; #1440), and a name for #1440's `adata`, to be settled there.
- **Formats are pure shape.** There is one `plaintext`, one `markdown_block`, one `boolean`, with
  no producer families and no prefixes; the producer is the node's, never the format's (§ 5.1).
- **The rules for a model's output apply to the output of a node whose `producer` isn't
  `computed`:** its format is always named (§ 2), its root is no `anyOf` and the engine's envelope
  supplies the top-level object (§ 3.3), the sent format is recorded (§ 3.5), and its agent is
  chosen by its format and producer (§ 7). "Every property in `required`, the optional ones
  nullable" applies to every format.
- **A `call` or flow node is `computed`** even when its body holds an agent node: the agent node is
  `generated`, and what spreads from it is the model-derived flag (§ 1.2).
- **Input elements have no `producer`.** They keep their `origin` (`typed`, `document`,
  `previous-run`, `transcript`). The engine always knows an element's origin and a node's
  producer, so `null` is never written for either.

## 2. Format

**Every `format` member is a format name or an inline schema** in the dialect (§ 3), for nodes and
inputs alike; an inline schema may `$ref` named formats
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6919612460)).

```jsonc
"format": "plaintext"                                                 // a name
"format": { "type": "array", "items": { "$ref": "markdown_block" } }  // an inline schema
```

- **A name** is resolved package-local first, then catalogue, then built-in (§ 6.3).
- **An inline schema** is an anonymous format: one-off shapes (an input's object, a computed
  node's small object) need no file. Revisiting "name or inline" is #1426 (Milestone 40).
- **The output of a node whose `producer` isn't `computed` always has a named format,** never
  inline: it needs a binding (§ 7) and its hashes in the record, which a named file gives
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466);
  [producer](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)).
- **An inline schema carries no `contentMediaType` or `x-markdown`,** and one written is refused:
  text inside an inline schema is a `$ref` to a named text format (§ 5.3)
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466)).

### 2.1 Reading a value

**Nodes, inputs and snippets are read the same way**
([#1383](https://github.com/Consultologist/Consultologist-Blazor/issues/1383), § 1a;
[decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466)):

| Read | Gives | On |
|---|---|---|
| `.value` | the raw value (`"medical_oncology"`, `72`, an object) | every node and every input |
| `.content` | the text; an object's `.content` is its canonical JSON | text formats (`node.draft_summary.content`, `input.consult_draft.content`, `snippet.<id>.content`) |
| `.title` | the chosen `oneOf` arm's title (*"Medical oncology"*), at any depth (`item.value.kind.title`) | a labelled enum (§ 9); refused on a plain `enum` |

- **A bare `input.<id>` is refused,** as a bare `node.<id>` is. A condition reads
  `{ "var": "input.visit_type.value" }`; a template reads `{{ input.consult_draft.content }}`.
- **`const` and `title` live in the format;** the stored and recorded value is always the raw
  string.
- A node that didn't complete reads `null` for `.value` and `.content` (part 03).

### 2.2 Which formats each kind may return

| `kind` | May return | Rule |
|---|---|---|
| `agent` | a named format with a binding for it and the producer `generated` (§ 7): a registry or catalogue format (`plaintext`, `markdown_block`, `markdown_inline`, `judgement`, `concept_list`, `classification`, …), or a package's own object or array format through the custom-format agent; sent in strict mode (§ 3.3) | [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6878601823), [producer](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868) |
| `rendered` (node) | a text format: `plaintext`, `markdown_block`, `markdown_inline` | [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1397#issuecomment-6878563370) |
| `rendered` (renderer) | a text format; its `input` may be any format, written like a node's `format` | [part 05](05-templates-and-macros.md) |
| `rendered` (deliverable) | `markdown_block`, `plaintext`, or an object format; never `markdown_inline` | [part 06](06-checks-deliverables-profile.md) |
| `computed` | any format, equal to what its expression returns, checked through each operation's typed return; `lower`, `trim` and the like return their operand's format unchanged | [part 03](03-expressions.md) |
| `computed` (check) | `check_result` | [part 06](06-checks-deliverables-profile.md) |
| `call` | the function's `format` | [part 04](04-loops-and-functions.md) |
| `for` | `list<T>`, where `T` is its `ret`'s format: `{ "type": "array", "items": T }` | [#1385](https://github.com/Consultologist/Consultologist-Blazor/issues/1385) decision 5 |
| `while` | exactly its `ret`'s format (the last pass) | as above |
| `if`, `case` | the union of what its branches return; one format when every branch returns the same | as above |

- **A union** is `{ "anyOf": [ A, B ] }`. Each branch's `ret` must be a member, and every member
  must be returned by some branch. An untaken `if` with no `else` reads as skipped through
  `.skipped`, so it is not a union member.
- **The union is the result itself,** a top-level `anyOf` with no wrapper object: only the
  output of a node whose `producer` isn't `computed` is sent to a model (§ 3.3), and the condition's row in the record says
  which branch ran
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6959885810)).
- **Reading a union:** `.content` always; `.value` only if every member has a value;
  `.value.<field>` only if every member declares that field compatibly.
- **Nested loops nest:** `list<list<plaintext>>`. A loop's value holds only the items that
  completed, in order; items that didn't run are absent, never `null`, and are seen with their
  state and reason through `.items` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6959884271), part 04 § 2.5).
- **A macro's `format`** is `plaintext`, `markdown_block` or `markdown_inline`
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1412#issuecomment-6887873980)).
- **Flow nodes write `format`** in their `index.json`, and publish refuses one that differs from
  the derived format
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)).

### 2.3 Comparing formats: two equalities, two hashes

There are two comparisons, each with its own hash
([#1385](https://github.com/Consultologist/Consultologist-Blazor/issues/1385), decision 8;
[decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466)):

| Comparison | How | Used for | Hash |
|---|---|---|---|
| **type equality** | references compared by name (never dereferenced), `anyOf` members and `properties` sorted by name, annotations (`title`, `description`) dropped | type checks: a written `format` against the derived one (a flow node's, a `computed` node's), `with` against a function's parameters, branch results against each other, a piped value against a renderer's `input` | `typeHash` |
| **full equality** | everything dereferenced, every keyword kept | hashes, and what a model receives | `formatHash` |

- **`formatHash` leaves out `$id`** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)), so the same shape published under two ids hashes
  the same.
- **A built-in format's name equals its schema** under type equality ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)): `date` and
  `{ "type": "string", "format": "date" }` are one type.
- **`integer` is a kind of `number`** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)): an `integer` value fits where a `number` is
  expected, not the reverse. Numbers are IEEE doubles; part 03 § 7 gives the rules.
- **The record carries both hashes for each format** (`typeHash`, `formatHash`), so a change of
  wording (a `description`, which is what the model is told) is told apart from a change of shape ([part 08](08-record.md)).
- **A text format fits where `string` is expected:** annotated strings are compatible with
  `string`; the annotations drive warnings and conversion, never type errors
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6878562886)).
  The reverse does not hold, and the typed operations still treat text as text (part 03).

### 2.4 Display: type strings

People see formats as **type strings**, printed from the schema; pickers write the schema
([#1385](https://github.com/Consultologist/Consultologist-Blazor/issues/1385), decision 8;
nullables, objects and enums
[decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466)).

| In the file | Displayed |
|---|---|
| `"plaintext"` or `{ "$ref": "plaintext" }` | `plaintext` |
| `{ "type": "number" }` | `number` |
| `{ "type": "array", "items": { "$ref": "plaintext" } }` | `list<plaintext>` |
| `{ "type": "array", "items": { "type": "array", "items": … } }` | `list<list<…>>` |
| `{ "anyOf": [ { "$ref": "plaintext" }, { "$ref": "classification" } ] }` | `plaintext \| classification` |
| `{ "type": ["string", "null"] }`, `{ "anyOf": [ { "$ref": "x" }, { "type": "null" } ] }` | `string \| null`, `x \| null` |
| an inline object | `{ title: string, notes: plaintext \| null }` |
| an `enum`, or a labelled `oneOf` | `string {narrative, table, omit}` |

The Editor's picker also says what a format is, for example "plain text"
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6878601823));
who wrote it ("written by a model") is the node's `producer`, shown beside it (§ 1.3).

## 3. The dialect

Formats are written in **one JSON Schema 2020-12 dialect that matches Azure OpenAI's strict
structured outputs**, so the format of every node whose `producer` isn't `computed` can be sent
in strict mode
([decided 2026-11-17](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6957666238)).
The format publishes the dialect's 2020-12 meta-schema, so any 2020-12 tool can check a package's
formats. **The dialect's id is `urn:consultologist:spec:31:dialect`.**

### 3.1 The keywords

| Keyword | Allowed | Rule |
|---|---|---|
| `$schema` | **required** at the root of every format file | the dialect's id, `urn:consultologist:spec:31:dialect`; a mismatch with `specVersion` is refused |
| `type` | anywhere | `string`, `number`, `integer`, `boolean`, `object`, `array` or `null`; or an array of exactly one of those plus `"null"` (§ 3.2) |
| `properties`, `required` | on an object | `required` lists **every** declared property, and nothing else (§ 3.3) |
| `additionalProperties` | on every object | must be `false` |
| `items` | on an array | one schema |
| `enum` | on a string | the plain enum (§ 9) |
| `oneOf`, `const` | on a string, only in the labelled-enum form (§ 9) | every branch is exactly `{ "const": …, "title": … }` |
| `anyOf` | unions and nullable references | not at the root of a format sent to a model (§ 3.3) |
| `$ref` | anywhere | a bare format id, or `#/$defs/<name>` within the file (§ 6.3); beside it, only `title` and `description` |
| `$defs` | at a format file's root | sub-definitions used inside the file |
| `$id` | at a published format's root | the format's URN (§ 6.2) |
| `title`, `description` | anywhere | annotations: `title` for people, `description` for people and the model (§ 5.1) |
| `format` | only `"date"`, only beside `"type": "string"` | the built-in `date` (§ 4) |
| `contentMediaType` | at a text format's root | `text/plain` or `text/markdown` (§ 5) |
| `x-markdown` | as § 5 says | our annotation |

**A keyword outside the dialect is refused** at publish, with a sentence. The rest of JSON Schema
(`pattern`, `allOf`, `if`/`then`, the length and count bounds, `patternProperties`, …) is not
adopted.

- **`$schema` is written in every format file** (the registry's, the catalogue's and a package's),
  so any 2020-12 tool finds the dialect, and publish refuses one whose dialect isn't the
  `specVersion`'s. An inline schema is part of its file and carries none
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466)).
- **Recursive formats are refused** (a `$ref` that reaches its own definition): the comparisons
  (§ 2.3) and the printer (§ 2.4) are defined for finite shapes
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466)).
- **Strict mode's size limits** (the property count and the nesting depth Azure OpenAI
  documents) are checked at publish at 31 ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)), on every format sent to a model.

### 3.2 One way to write "nullable"

Decided on [#1385](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6879459060):
- **Scalars:** a type array, `"type": ["string", "null"]`.
- **References and objects:** `"anyOf": [ { "$ref": "…" }, { "type": "null" } ]`, the only way to
  make a reference nullable.
- **Enums** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)): a nullable plain enum lists `null` among its values as well,
  `"type": ["string", "null"], "enum": ["new_patient", "follow_up", null]`; a nullable labelled
  enum gains a `{ "const": null }` arm in its `oneOf`, with no `title`. The `null` value and arm
  don't count towards § 9's two values.
- **Refused:** an `anyOf` with a `null` branch around a plain scalar, and a type array holding
  anything other than one type plus `"null"`.

### 3.3 `required`, `additionalProperties` and the root

Strict mode requires every property in `required`, `additionalProperties: false` on every object,
and an object, not an `anyOf`, at the root
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6959885810)):
- **in every format,** whoever produces the value: every property of every object is listed in
  `required`, and an optional property has a nullable type (§ 3.2), for example
  `"type": ["integer", "null"]`. Every value then carries every key, with `null` when there is
  none, as every declaration does (#1394). A format whose object leaves a property out of
  `required` is refused (Refusals);
- **in every format:** `additionalProperties: false` on every object;
- **in the format of a node whose `producer` isn't `computed` only** (sent to a model): the root
  is not an `anyOf`. Its root
  need not be an object: the engine's envelope supplies the top-level object strict mode needs
  when it sends the format (§ 3.5);
- **in every other format** (an input's, a computed value's, a flow node's): the root may be a
  string, a list or an `anyOf`. An `if` or `case` whose branches return different formats keeps
  the `anyOf` as its result, with no wrapper (§ 2.2).

### 3.4 Validating values

- **Values are validated with Corvus.JsonSchema** (Apache-2.0): inputs, model outputs and format
  examples. **Our own checker enforces the dialect's restrictions,** since Corvus can neither refuse
  keywords outside a dialect nor register our annotations
  ([research](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6871232338),
  [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6957666238)).
- Corvus sits behind our own interface, pinned exactly, and listed in `lock.json` (#1390). Moving to
  JsonSchema.Net later is #1459.
- **A reply is validated against the full format,** including what was not sent (§ 3.5).
- **`format: "date"` is validated as an assertion** when a reply is checked against its full
  format: a string `YYYY-MM-DD` naming a real calendar date, so `2026-02-30` is refused
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6982429387)).

### 3.5 What a model receives

When a node whose `producer` isn't `computed` sends its format to the model provider, the engine
prepares a copy;
`resolve.json` keeps the original
([#1385](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6879423102);
[decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466)):
1. each `description` is **kept:** it is the instruction to the model, at the top of the format
   and on each property, and strict mode accepts it; a field `$ref`ing a text format sends that
   text format's `description` first, then its own
   ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6982429387));
2. every `x-` annotation and `contentMediaType` is stripped;
3. a referenced text format resolves to `{ "type": "string" }`;
4. a labelled enum (`oneOf` of `const`/`title`) becomes a plain `enum` of the `const`s
   ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6879459060));
5. `title`, `$id` and **the keywords strict mode rejects** (such as `format: "date"`) are removed.
   **The list of rejected keywords comes from the attested agent definition,** not from the
   format, so a provider that accepts more is a new agent version, not a format change;
6. the result is sent inside the engine's envelope, which supplies the top-level object strict
   mode requires, so a text or list format can be sent too
   ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6959885810)).

**The sent format is recorded:** the row holds `formatHash` (the full format), `sentFormatHash`
(what the model received) and the removed keywords as JSON pointers
([part 08](08-record.md)). History shows them as *"Not sent to the model: …"*. The reply is still
validated against the full format (§ 3.4).

## 4. Built-in and catalogue formats

| Owner | Formats | Where |
|---|---|---|
| **The package-format registry** (`consultologist-package-format`), versioned with the spec | data: `string`, `number`, `boolean`, `date`; text: `plaintext` (30's `prose`), `markdown_block`, `markdown_inline`; objects: `check_result`, `judgement` | `formats/index.json` + `formats/<id>.json` beside the dialect's meta-schema |
| **The formats catalogue** (`consultologist-agents`) | object formats: `concept_list`, `classification`, … | `agents/formats/index.json` + `<id>.json`, replacing `agents/formats.json` |
| **A package** | its own object and array formats | `formats/index.json` + `formats/<id>.json` |

([where](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6878965680),
[per owner](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6882203602),
[pure shape](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868))

- **Formats are pure shape,** with no producer families and no prefixes: a model's plain text and a
  template's plain text are both `plaintext`, and a yes or no is `boolean` wherever it sits (an
  input, a computed value, a `judgement`'s `answer`). Who produced a
  value is its node's `producer` (§ 1.3)
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)).
- **`rendered` and `terms_subset` stop being formats:** they are `kind: rendered` and an operation
  returning `check_result`. 30's format-file `producer` member is gone.
- **`date`** is `{ "type": "string", "format": "date" }` (with its `$schema` and `$id`), so
  `"format": "date"` works
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6919612460)).
- **A classifier returns `judgement`,** the built-in `{ answer: boolean, reason: plaintext }`,
  with every property required and `reason` never `null`. A macro's `if` reads
  `node.<id>.value.answer`, and the record and History show the reason beside the answer
  ([#1412](https://github.com/Consultologist/Consultologist-Blazor/issues/1412),
  [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6982429387)). Today's catalogue `classification` (`{ value: string }`) is unchanged.
- **`check_result`**'s members are [part 06](06-checks-deliverables-profile.md).
- **`titled_section`** (#1406's candidate) is not a built-in at 31; a package declares it in
  `formats/`. Adding a built-in later is compatible
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6978700251)).
- **Packages define only object and array formats.** Scalar data and text formats come from the
  registry and the catalogue: a package references `markdown_block` or `plaintext` rather than
  declaring its own text format, since text a model writes needs a catalogue binding (§ 7) ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466)).

`formats/markdown_block.json` in the registry:

```json
{
  "$schema": "urn:consultologist:spec:31:dialect",
  "$id": "urn:consultologist:spec:31:format:markdown_block",
  "type": "string",
  "contentMediaType": "text/markdown",
  "x-markdown": "block",
  "description": "Markdown text that stands on its own lines (a paragraph, a list or a table)."
}
```

`formats/judgement.json` in the registry (descriptions illustrative):

```json
{
  "$schema": "urn:consultologist:spec:31:dialect",
  "$id": "urn:consultologist:spec:31:format:judgement",
  "type": "object",
  "description": "A yes-or-no answer to the question asked, with the reason for it.",
  "required": ["answer", "reason"],
  "additionalProperties": false,
  "properties": {
    "answer": { "type": "boolean", "description": "true for yes, false for no." },
    "reason": { "$ref": "plaintext", "description": "Why, in a sentence or two, from the text given." }
  }
}
```

## 5. Data and text

**`string` is data:** a value, not content (an id, a code, an enum value, a label). **Text formats
are content** meant to be read or composed, and always carry a media type
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6878562886),
[names](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6878601823)).

| Format | Is | `contentMediaType` | `x-markdown` |
|---|---|---|---|
| `string`, `number`, `boolean`, `date` | data | omitted | omitted |
| `plaintext` | text | `text/plain` | omitted |
| `markdown_block` | text | `text/markdown` | `block` |
| `markdown_inline` | text | `text/markdown` | `inline` |
| object formats | structured data | omitted | omitted |

Each is the same format whether a model or a template wrote the value (§ 1.3); 30's `prose` becomes
`plaintext` (§ 11). A `block` Markdown text stands on its own lines; an `inline` one sits inside a
line.

### 5.1 The annotations

They follow JSON Schema's convention: **present where they apply, omitted otherwise, never `null`.**
The checker refuses one missing where it applies, and one present where it doesn't
([#1394 boundary](https://github.com/Consultologist/Consultologist-Blazor/issues/1394#issuecomment-6878756179)).

| Annotation | Where | Meaning |
|---|---|---|
| `contentMediaType` | at the root of every text format | the text's media type |
| `x-markdown` | at the root of every Markdown format | `block` or `inline` |
| `description` | anywhere | what the value **is** and what the model should write: sent to the model (strict mode accepts it, § 3.5) and shown in the Editor, the checker's sentences and the docs ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6982429387)) |

- **A format file is plain JSON Schema plus our layout annotations** (`contentMediaType`,
  `x-markdown`). 30's `x-generated` and its `agent_*` and `prose` names are gone, with no
  producer annotation or prefix in their place: the producer is the node's (§ 1.3)
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)).
- **A format's instructions to the model are standard JSON Schema `description` keywords,** at the
  top of the format and on each property, in the catalogue and in packages alike. 30's
  `x-instruction` is gone, and bindings hold no instructions (§ 7)
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6982429387)).
- **Our annotations sit at the top of each format,** the format as a return type, not on every
  nested field ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6878928645)).
- **`title` and `description` are part of the format,** so changing either changes its `formatHash` (not its
  `typeHash`, § 2.3): type equality ignores them, and full equality includes them, since a
  `description` changes what the model is told
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6982429387)).

### 5.2 Markdown means Markdig's advanced extensions

`markdown_block` and `markdown_inline` mean **Markdig with `UseAdvancedExtensions`** (pipe and grid
tables, footnotes, task lists, emphasis extras, auto-links and the rest of that set). The spec lists
the extensions by name as of the pinned Markdig version; Markdig is pinned **exactly** and listed in
`lock.json`
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6924903111)).
Escaping plain text into Markdown, `plain()` and composing to HTML or PDF are defined against that
list. Flavours (CommonMark, GFM) are #1451.

### 5.3 Object schemas are fully typed; nested text references a text format

Every nested field has its complete type, a `required` list naming every property (§ 3.3) and
`additionalProperties: false`,
defined inline or by `$ref` to a definition. **A nested field is data unless it references a text
format**
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6878928645)).
A field that is text meant to be read or composed is written `{ "$ref": "plaintext" }`, which gives
it its media type, so the typed operations, escaping and plain-text conversion treat it as text.

**Field names follow the id pattern** of part 01 § 6, **with no reserved names**
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)). A field never sits at the same level as a member: it is always read after
`.value` (`item.value.title`), and `.title` as a label applies only to an enumerated string, which
has no fields. So a field may be called `title`, `label` or `value`; ids keep their reserved list.

`example-nested-note`'s `formats/verdict.json` at 31:

```json
{
  "$schema": "urn:consultologist:spec:31:dialect",
  "type": "object",
  "description": "A judgement of a draft summary: good, or revise with what to change.",
  "required": ["verdict", "feedback"],
  "additionalProperties": false,
  "properties": {
    "verdict":  { "type": "string", "enum": ["good", "revise"] },
    "feedback": { "$ref": "plaintext" }
  }
}
```

(The format keeps its 30 name and `description`, which is sent to the model as at 30, § 3.5.)

## 6. Format files, ids and references

### 6.1 `formats/index.json` and one file per format

**Every owner** (the registry, the catalogue, a package) writes its formats as
`formats/<id>.json`, one format per file, listed by **`formats/index.json`, a plain index** like
every other `index.json`: `description` (lines or `null`) and `items`
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466)):

```json
{
  "description": null,
  "items": ["section_plan", "outline", "verdict"]
}
```

- **The file name is the id;** `items` lists every file in the folder exactly once, in order
  ([part 01 § 2.2](01-files-and-scopes.md)).
- **Types only:** no agent names, versions or producers; bindings live apart (§ 7). The
  instructions to the model are the format's own `description`s (§ 5.1).
- **A format file is a JSON Schema in the dialect,** with no wrapper: 30's `type`, `producer`,
  `outputMacros` and `schema` wrapper members are gone, and the schema is the file, starting with
  its `$schema` (§ 3.1).
- **A function's own formats** sit in `functions/<id>/formats/`, shaped the same (part 04).

### 6.2 `$id`s are URNs

Every published format has a `$id`, a URN that doesn't depend on where anything is hosted
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6957666238)):

| Owner | `$id` | Example |
|---|---|---|
| the formats catalogue | `urn:consultologist:format:<name>@<catalogue-version>` | `urn:consultologist:format:concept_list@v2026.12.1` |
| a package | `urn:consultologist:package:<package>@<version>:format:<id>` | `urn:consultologist:package:example-nested-note@v2026.12.1:format:verdict` |
| the registry's built-ins ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6959885810)) | `urn:consultologist:spec:<specVersion>:format:<name>` | `urn:consultologist:spec:31:format:markdown_block` |

- **A package format's `$id` is derived at publish** from the package's name and version, **never
  written in source**; a `$id` found there is refused, since it would have to be rewritten at
  every version
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466)).
- A package name may hold `/` (`test/general`), which a URN's specific string allows.
- Within a format, `$id` follows JSON Schema's own rules.

### 6.3 References by bare id; no shadowing

- **In source, a format is named by its bare id:** `"format": "plaintext"`,
  `{ "$ref": "plaintext" }`. The resolver looks it up **package-local first, then catalogue,
  then built-in**, and rewrites it to `#/$defs/<id>` in `resolve.json`'s single inline bundle
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6879096084)).
- **No shadowing:** a package format may not reuse a catalogue or built-in name, as #1384 rules for
  nodes. So the lookup order never changes which format a name means.
- **Inside a format file,** `#/$defs/<name>` refers to that file's own `$defs`.
- **In a function,** a name resolves to the function's own formats, then the package formats its
  `uses` lists, then catalogue and built-in; built-in and catalogue formats need no listing
  ([#1419](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6888253558)).
- **`resolve.json`** holds exactly the formats the package uses, each inline with its origin, its
  `$id` and its hashes ([part 07](07-compiled-documents.md)).

## 7. Agent bindings

**What a value is and who produces it are two facts in two files**
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6879413099)):

| File | Holds |
|---|---|
| `formats/index.json` and the format files (every owner) | types only |
| `agents/bindings.json` (the agents catalogue) | which attested agent serves which format for which producer, and the custom-format agent; no instructions |

```json
{
  "version": "v2026.12.1",
  "formats": {
    "plaintext": {
      "generated": { "agentName": "consult-prose", "agentVersion": 4 }
    },
    "markdown_block": {
      "generated": { "agentName": "consult-prose", "agentVersion": 4 }
    },
    "judgement": {
      "generated": { "agentName": "classification", "agentVersion": 1 }
    },
    "concept_list": {
      "generated": { "agentName": "concept-extraction", "agentVersion": 1 }
    }
  },
  "custom": {
    "generated": { "agentName": "custom-json", "agentVersion": 1 }
  }
}
```

(Names and versions illustrative.)

- **The agent is chosen by format and producer:** a registry or catalogue format through
  `formats.<format>.<producer>`, a package's own format through `custom.<producer>`. At 31 the only
  producer with bindings is `generated` (§ 1.3)
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)).
- **`resolve.json`'s bindings record the route as `via`:** `"format"` for a binding in
  `formats`, `"custom"` for a package's own format bound to the custom-format agent. `"producer"`,
  an agent chosen by producer alone, is #1479 (Milestone 40): **the value is reserved at 31 and
  refused** until then
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868), [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1478#issuecomment-6982835861); part 07).
- **Bindings hold no instructions.** What the model should write is the format's own
  `description`s (§ 5.1), so a catalogue format and a package's own format carry their
  instructions the same way, and a package format needs no binding of its own to have them
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6982429387)).
- **No agent settings per node.** Model, reasoning effort and instructions live in the attested
  agent definitions; different behaviour means a format bound to a different agent. A node can't
  name an agent
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6923942272)).
- **The attested agent definition also lists the keywords its provider's strict mode rejects**
  (§ 3.5).
- **The checks:** publishing the agents catalogue checks that every binding names an existing
  format, a producer in use and an attested agent; publishing a package refuses an agent node
  whose format and producer have no binding.
- **`resolve.json`** records the bundle and a separate `bindings` section; `lock.json` pins the
  agents catalogue beside the formats catalogue (part 07). Agent rows in the record name the attested
  definition and the binding that chose it ([part 08](08-record.md)).

## 8. Inputs

**Inputs are an object keyed by id in `manifest.json`.** The written order is the Create page's
order. Every member is present
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6919612460)).

| Member | Type | `null` | Meaning |
|---|---|---|---|
| `label` | string, not empty or whitespace | no | the Create page label |
| `description` | array of strings | yes | help text on the Create page; part 01's one `description` rule |
| `format` | a format name or an inline schema | no | the input's type, which picks the widget |
| `required` | boolean | no | whether the user must supply it; one that isn't required reads `null` when left blank |
| `channel` | string | yes | the content channel (§ 8.3); replaces `expectedContent` |

- **There is no `default` member;** an explicit default is #1456 (Milestone 40)
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6957666238)).
- **There is no `labelField`:** a loop over the input names its items with `itemLabel` (part 04).
- **Input ids** follow part 01's id rules and reserved list.
- **Inputs are read like nodes:** `input.<id>.value`, `.content` for a text format, `.title` for a
  labelled choice; a bare `input.<id>` is refused (§ 2.1).
- **A top-level input keeps `required: true | false`** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6959885810)): it says whether the
  user must supply it, not what the format's `required` list says. An optional input left blank
  reads `null`, in JSON Logic and in templates, and the null rules of part 03 apply.
- **What a blank optional input reads** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)): one rule for every format.

  | Read | Supplied | Left blank |
  |---|---|---|
  | `input.<id>.value` | its value | `null` |
  | `input.<id>.content` (text formats) | its text | `null` |
  | `input.<id>.title` (labelled choices) | the chosen arm's title | `null` |
  | in a template, any of the three | the value, text or title | nothing |
  | in JSON Logic | the value | `null`, under part 03 § 6.2's null rules |

  A blank text input is `null`, not `""`; v25's `""` for an absent text is retired.
- **Fields inside an object input follow the format rule** (§ 3.3): every field is listed in
  `required`, an optional field has a nullable type, and a field whose type is not nullable must
  be filled in ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6959885810)).
- **Order matters:** the object's member order is the Create page's, and **`resolve.json` carries
  the inputs' order explicitly**, since canonical JSON (RFC 8785) sorts members (part 07)
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466)).
- **Function inputs** are `{ label, description, format }`, all required, with no `required` or
  `channel`; a caller passes the literal `null` through `with` for an optional value
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6963556118),
  [`null` literal](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868), part 04).

### 8.1 Which formats an input may take, and its widget

| Format | Create page widget |
|---|---|
| `string` | a single-line field |
| `string` with a labelled `oneOf` (§ 9) | a select, showing each value's `title` |
| `string` with a plain `enum` (§ 9) | a select, showing each bare value |
| `number`, `boolean` | a number field; a checkbox (an optional boolean: three choices, § 10) |
| `date` | a date picker |
| `plaintext` | a multi-line text area |
| `markdown_block` | a rich-text editor (toolbar, preview), **stored as Markdown, not HTML** |
| an object (inline or a package format) | a field group, one field per property, labelled by its `title` |
| an array | repeating rows, whose `items` take the same formats |

([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6878562886))

- **Object fields are JSON Schema `properties`:** every field is in the `required` list, an
  optional field is nullable (§ 8), and fields nest to any depth. 30's `fields`/`items`/`values`
  grammar goes.
- **Every property of an object input requires a `title`,** its label on the Create page, so no
  field is shown by its bare id
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466)).
- **Field names follow the id pattern, with no reserved names** (§ 5.3): a field is always read
  after `.value` (`item.value.title`), so a field called `title` never collides with the `.title`
  read of an enumerated string
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)).
- **Any format may be an input's,** since formats are pure shape (§ 1.3), **except**
  `markdown_inline` (no widget, and an inline fragment is not something a clinician submits); a
  top-level `anyOf` (no widget can offer "one of these shapes")
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466)).
- **Plain text pasted into a Markdown input** can be misread (a leading `#` becomes a heading); the
  rich-text editor shows the formatting as it is typed.

### 8.2 Documents

**Documents need no member.** Any input with a text format takes typed text or a document (the
parser decides), and an array of a text format takes several items, each typed, a document, or an
earlier run's deliverable, in the order sent (#872, #874, #1144). A data-format input is typed only,
except an object filled from a `form`. An element from an earlier run's deliverable starts out
model-derived (§ 1.2), and its record links to the job it came from.

- **A document uploaded to a Markdown input is converted to Markdown:** headings, lists and tables
  preserved from docx; best effort from PDF and OCR
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6878562886)).
- A document uploaded to a `plaintext` input gives its extracted text, as today.

### 8.3 `channel`

| `channel` | On |
|---|---|
| `null` | any input |
| `transcript`, `image`, `ambient-note` | a text-format input, or an array of a text format |
| `form` | an object with at least one property; never an array of objects |

The restrictions are 30's `expectedContent` rules carried over (`WorkflowPackageValidator.cs`).
Revisiting the name is #1427 (Milestone 40).

### 8.4 An example: `example-conditional-sections`' inputs at 31

```json
"inputs": {
  "consult_draft": {
    "label": "Consult draft",
    "description": null,
    "format": "markdown_block",
    "required": true,
    "channel": null
  },
  "sections": {
    "label": "Sections to produce",
    "description": null,
    "format": {
      "type": "array",
      "items": {
        "type": "object",
        "additionalProperties": false,
        "required": ["title", "kind", "notes"],
        "properties": {
          "title": { "type": "string", "title": "Title" },
          "kind": {
            "type": "string",
            "title": "Kind",
            "oneOf": [
              { "const": "narrative", "title": "Narrative" },
              { "const": "table",     "title": "Table" },
              { "const": "omit",      "title": "Leave out" }
            ]
          },
          "notes": {
            "anyOf": [ { "$ref": "markdown_block" }, { "type": "null" } ],
            "title": "Notes for this section"
          }
        }
      }
    },
    "required": true,
    "channel": null
  },
  "include_billing": {
    "label": "Include the billing section",
    "description": null,
    "format": "boolean",
    "required": false,
    "channel": null
  }
}
```

The migration writes 30's `text` field as `markdown_block` and keeps 30's enum a plain `enum`
(§ 11); `notes` is optional, so it is nullable and a blank one is sent as `null`. This example
shows the author's later edits: `title` narrowed to `string`, and `kind` changed to a labelled
`oneOf` with titles written. The field keeps its 30 name `title`: fields have no reserved names
(§ 5.3).

The loops over `input.sections.value` say `"itemLabel": "title"` (part 04). A condition reads
`{ "===": [ { "var": "item.value.kind" }, "narrative" ] }`, displayed
`item.value.kind = "narrative"`; a template may show the label with `{{ item.value.kind.title }}`
(*"Narrative"*). The billing section's `if` reads `{ "var": "input.include_billing.value" }`.

## 9. Enum labels

Decided on [#1061](https://github.com/Consultologist/Consultologist-Blazor/issues/1061#issuecomment-6879459298),
[#1385](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6879459060)
and [#1385 (part 02)](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466):

- **`enum` and `oneOf` are the author's choice, everywhere.** `enum` means bare values with no
  labels. **Labelled values** are JSON Schema's standard form, `oneOf` of `const` with `title`:
  ```json
  { "type": "string", "oneOf": [
      { "const": "new_patient", "title": "New patient" },
      { "const": "follow_up",   "title": "Follow-up" } ] }
  ```
  The Editor's suggestion to switch from one to the other is #1470 (Milestone 41).
- **The rules:** at least two values; values unique, and titles unique (and non-empty); in inputs
  and package formats, every value is snake_case (`^[a-z][a-z0-9_]*$`), as 30's `values` are, so
  values stay printable in conditions and reasons; a field has `enum` or the labelled `oneOf`,
  never both.
- **The value is stored, compared and recorded,** always the raw string. Conditions, skip reasons
  and the record name the value.
- **The label is read as `.title`** (§ 2.1): `input.visit_type.title`, `item.value.kind.title`, at
  any depth. `.title` on a plain `enum` is refused. The Create page's select, the Editor and
  History show it.
- **`.title` may be compared in conditions and computed expressions,** for example
  `{ "===": [ { "var": "input.visit_type.title" }, "Follow-up" ] }`. Publish checks that the
  literal is one of the format's titles. **The record prints such a condition with both the raw
  value and the title**, so a decision reads the same whichever was compared.
- **Classification answers get their labels the same way** (`classification`).
- **For a model provider,** the schema becomes a plain `enum` of the `const`s (§ 3.5).

## 10. Per-run macro choices become optional boolean inputs

At 30 a macro could be offered as a per-run choice on the Create page (`optional`, with a
`default`). At 31 **each choice is an optional boolean input that the macro's `if` reads,** so
today's defaults carry over with no new member
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1412#issuecomment-6957666676)):

| 30 default | 31 test in the macro's `if` | While the input is unset |
|---|---|---|
| off | `{ "===": [ { "var": "input.<id>.value" }, true ] }` | `null === true` is false: not offered |
| on | `{ "!==": [ { "var": "input.<id>.value" }, false ] }` | `null !== false` is true: offered |

The input the migration writes:

```json
"include_safety_netting": {
  "label": "Safety-netting advice",
  "description": null,
  "format": "boolean",
  "required": false,
  "channel": null
}
```

- **The input takes the macro's id and label,** and the test is joined with `and` to any existing
  `if`: `{ "and": [ <its if>, <the test> ] }`
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466)).
  Where the macro's id is already an input's id, the input gets the suffix `_macro`
  (`letrozole` → `letrozole_macro`), with the rename in the migration report
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1447#issuecomment-6980651765)).
- **The Create page shows an optional boolean as three choices** (unset, yes, no), so "unset" keeps
  the 30 default, and **unset is sent as `null`**. A plain checkbox would show a default-on choice
  unchecked while meaning on ([part 09](09-engine-and-pages.md)).

## 11. What the migration does to types and inputs

Mechanical, in `WorkflowPackageMigration30To31` ([part 10](10-migration-and-tools.md),
[#1447](https://github.com/Consultologist/Consultologist-Blazor/issues/1447)):

| At 30 | At 31 |
|---|---|
| every node | gains `kind`; `format` becomes its return type; `reproducible` written with the derived value (§ 1.1); `producer` written from its kind (§ 1.3); `modelDerived`, `runDependent` and `profileCarrying` written with the derived values (§ 1.2, [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1478#issuecomment-6982835861), [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)) |
| `"format": "prose"`, `agent_*` | the plain format of the same shape: `prose` and `agent_plaintext` → `plaintext`, `agent_markdown_block` → `markdown_block`, `agent_markdown_inline` → `markdown_inline` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)) |
| a package or catalogue object format | keeps its name |
| a `rendered` format | `kind: rendered`, a text format |
| a `let` with `type` | `kind: computed`, `format` from `type`, `expression` in JSON Logic (#1447) |
| a `terms_subset` check | `checks/<id>.md`, `kind: computed`, `format: check_result` |
| `x-generated` | dropped: the node's `producer` says it (§ 1.3) |
| a format file's wrapper | the schema alone, with `$schema` written; `producer` and `outputMacros` gone |
| the manifest's `formats` list | `formats/index.json`, a plain index |
| inputs as an array | an object keyed by id |
| `type: text` (inputs and fields) | `markdown_block` |
| `type: enum` + `values` | `string` + a plain `enum`, with no invented titles |
| `type: date` | `"date"` |
| `fields` | `properties` + `required` listing every field, an optional field made nullable (+ `title` from each field's `label`) |
| an object format's partial `required` (catalogue and package) | every property listed in `required`, the optional ones made nullable ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6959885810); part 10 § 1.8) |
| a bare `input.x` read | `input.x.value`, or `input.x.content` for a text format |
| `expectedContent` | `channel` |
| `labelField` | `itemLabel` on each loop over the input |
| (no description) | `description: null` |
| a per-run macro choice | an optional boolean input (§ 10) |
| a "one or many" union input (`type: [x, array]`, #729) | its array form (an array of `x` takes one item as well as several) |

([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466))

- **Suggestions, never applied automatically:** `markdown_block` for list-like sections
  ([#1406](https://github.com/Consultologist/Consultologist-Blazor/issues/1406#issuecomment-6881841286)),
  and, for a long free-text `string` field, becoming a text format
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466)).
  Every string field stays data until the author accepts.
- **The migration report lists each node whose 30 `reproducible` claim differed** from the
  derived value it wrote; `reproducible` is written explicitly on every node
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6982429387)).

## 12. `WorkflowPackages/Current`: what satellites see

The engine's `WorkflowPackages/Current` response is the Copilot agent's and the Zoom app's only
contract, and it **changes by addition only:** version 31 inputs map into its existing fields plus
new `Format` and `Channel` members, and `Macros` comes back empty for version 31 packages
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6957666238),
[mapping decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466)).
The satellites are updated before the cutover, so they work with 30 and 31 alike
([#1447](https://github.com/Consultologist/Consultologist-Blazor/issues/1447#issuecomment-6957669427)).

**Each input becomes one `WorkflowPackageInputResponse`:**

| Response member | From a 31 input |
|---|---|
| `Id` | the input's key |
| `Label` | `label` |
| `Required` | `required` |
| `Type` | derived from `format` (below) |
| `Values` | the `enum` values, or the labelled `oneOf`'s `const`s, in order; `null` otherwise |
| `Items` | for an array, its `items` mapped by the same rules |
| `Fields` | for an object, one `WorkflowPackageFieldResponse` per property: `Id` the property name, `Label` its `title`, `Required` whether its type is not nullable (every field is listed in `required`, § 8), and `Type`, `Values`, `Items`, `Fields` by the same rules |
| `ExpectedContent` | `channel` |
| `Types` | `null` (31 has no union inputs) |
| **`Format`** (new) | the input's format as a self-contained schema (below) |
| **`Channel`** (new) | `channel`, alongside `ExpectedContent`, so a satellite can tell an input it can't fill ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1393#issuecomment-6984396609)) |

**`Type`, derived from the format:**

| Format | `Type` |
|---|---|
| `plaintext`, `markdown_block` | `text` |
| `string` with `enum` or a labelled `oneOf` | `enum` |
| `string` with no values | `text` (30 has no single-line string type; the satellite's text field takes it) |
| `date` | `date` |
| `number`, `integer` | `number` |
| `boolean` | `boolean` |
| an object | `object` |
| an array | `array` |

- **`Format`:** a trailing optional `JsonElement` (`null` below 31): the input's format as a
  self-contained schema, every `$ref` replaced by the definition it names (each keeping its `$id`
  URN, so a named format stays recognisable), with titles and our annotations kept. A satellite then
  needs no registry to read it.
- **`Macros`: always `[]`** (empty) for a 31 package: 31 has no per-run macro choices; they arrive
  as optional boolean inputs, and an unset one is sent as `null` (§ 10).
- **A satellite that can't show an input** (an inline object schema, a document upload) shows
  *"This workflow needs the web app"*, with a link
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1447#issuecomment-6957669427)).
- **There is no `description` member yet** at 31; only `Format` and `Channel` are added.

## Refusals

Every rule below is decided. The wording below is the starting text. It follows the style rules in [index § 7](index.md#7-refusal-sentences) and is used as written in Milestone 39 and reviewed in #1483 (Milestone 40); every sentence is listed in [refusal-sentences.md](refusal-sentences.md).

**Decided wording:**
- *"This workflow needs the web app"* (a satellite, not the checker: [#1447](https://github.com/Consultologist/Consultologist-Blazor/issues/1447#issuecomment-6957669427)).
- History's *"Not sent to the model: …"* (§ 3.5) and *"computed from model text: <key>"* (§ 1.2).

**Kinds and formats:**
- *"Node '<id>' declares unknown kind '<kind>' (accepted: agent, rendered, computed, for, while, if, case, call)."*
- *"Node '<id>' declares kind let; a let is now kind computed, with its JSON Logic in expression."*
- *"Node '<id>' is kind <kind> and declares producer '<value>'; a <kind> node's producer is <generated | computed>."*
- *"Node '<id>' declares producer '<value>', which is reserved for a later version (accepted at 31: generated, computed)."*
- *"Node '<id>' declares <modelDerived | runDependent | profileCarrying> <value>, but its reads make it <derived>: <the read that does, or none>."*
- *"Node '<id>' is kind agent and returns <format>, which has no binding for producer <producer> in agents/bindings.json."*
- *"Node '<id>' is kind rendered and returns <format>; a rendered node returns plaintext, markdown_block or markdown_inline."*
- *"Node '<id>' is kind agent and declares an inline format; an agent's format is named, in formats/."*
- *"<Flow> '<id>' declares format <declared>, but its ret gives <derived>."*
- *"Branch <n> of <if | case> '<id>' returns <format>, which is not in the declared union <union>."*
- *"Format '<name>' is not a built-in, catalogue or package format."*

**Reads:**
- *"'input.<id>' is read bare; read input.<id>.value, or input.<id>.content for text."*
- *"'<path>.title' reads the title of a plain enum, which has none; use a labelled oneOf, or read .value."*
- *"<Condition> compares <path>.title with '<literal>', which is not one of its titles (<titles>)."*

**The dialect:**
- *"<Format | Input '<id>'> uses keyword '<keyword>' at <path>, which is outside the formats dialect."*
- *"Format '<name>' declares no $schema; every format file names the dialect (urn:consultologist:spec:31:dialect)."*
- *"Format '<name>' declares $schema '<value>', but the package is specVersion <n>, whose dialect is <id>."*
- *"<Format> at <path> declares additionalProperties <value>; every object declares additionalProperties: false."*
- *"<Format> at <path> lists '<property>' in required but declares no such property."*
- *"<Format> at <path> leaves '<property>' out of required; every property is listed in required, and an optional one is written as nullable."*
- *"Node '<id>' returns <format>, an anyOf at its root, which strict mode can't send to a model; wrap it in an object."*
- *"<Format> at <path> writes a nullable <type> with anyOf; write it as \"type\": [\"<type>\", \"null\"]."* (decided rule: [#1385](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6879459060))
- *"<Format> at <path> declares type <list>; a type array is one type plus \"null\"."*
- *"<Format> at <path> uses format '<value>'; the only format is date, on a string."*
- *"<Format> at <path> has '<keyword>' beside $ref; only title and description may sit beside a $ref."*
- *"<Format> at <path> refers to itself; recursive formats aren't supported."*
- *"Package format '<id>' declares $id; a package format's $id is written at publish."*
- *"Package format '<id>' is a <string | number | boolean>; a package defines object and array formats, and refers to built-in and catalogue formats by name."*

**Annotations:**
- *"Format '<name>' is a text format and declares no contentMediaType."*
- *"Format '<name>' is Markdown and declares no x-markdown (block or inline)."*
- *"<Format> declares <contentMediaType | x-markdown> at <path>, where it doesn't apply."*
- *"<Input '<id>' | Node '<id>'>'s inline format declares <contentMediaType | x-markdown>; text inside an inline format is a $ref to a text format."*

**Formats folder:**
- *"formats/index.json lists '<id>', but formats/ has no <id>.json."*

**Inputs:**
- *"Input '<id>' takes markdown_inline; use plaintext or markdown_block."*
- *"Input '<id>' takes a choice of shapes (anyOf); an input has one shape."*
- *"Input '<id>' declares channel '<channel>' (accepted: transcript, image, ambient-note, form)."*
- *"Input '<id>' declares channel '<channel>', which is only for a text input or an array of text."*
- *"Input '<id>' declares channel 'form', which needs an object with at least one property."*
- *"Input '<id>' field '<property>' has no title; the Create page labels each field by its title."*
- *"Input '<id>' declares default; inputs have no default at 31."*

**Enums:**
- *"<Subject> declares one value; an enum with one value is a constant, not a choice."* (30's sentence, carried over)
- *"<Subject> declares value '<value>' twice."*
- *"<Subject> value '<value>' must be snake_case (a lowercase letter, then lowercase letters, digits, or underscores)."* (30's sentence)
- *"<Subject> gives two values the title '<title>'."*
- *"<Subject> writes oneOf branch <n> as <shape>; each branch is exactly { \"const\": <value>, \"title\": <label> }."*
- *"<Subject> declares both enum and oneOf; write one."*
- *"<Subject> is nullable but its enum doesn't list null; a nullable enum lists null among its values."*
- *"<Subject> is nullable but its oneOf has no { \"const\": null } arm; a nullable labelled enum adds one."*

**Strict mode's size limits:**
- *"Format '<name>' declares <n> properties; a format sent to a model declares at most <limit>."*
- *"Format '<name>' nests <n> levels deep at <path>; a format sent to a model nests at most <limit>."*

## Proposed in this part

Nothing. Every point is decided; refusal sentences are used as written and reviewed in #1483 ([refusal-sentences.md](refusal-sentences.md)).
