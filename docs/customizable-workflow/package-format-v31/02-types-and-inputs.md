# 02. Types and inputs

This part defines the type system of version 31: every node's `kind` (how it runs) and `format`
(what it returns), the JSON Schema dialect formats are written in, the built-in, catalogue and
package formats with their `$id` URNs, the split between data and text, the annotations
(`x-agent`, `contentMediaType`, `x-markdown`, `x-instruction`), how agent formats reach their
attested agents, the input grammar, enum labels, per-run macro choices as optional boolean inputs,
and how inputs reach the satellites through `WorkflowPackages/Current`. It covers #1385 and #1061.
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
| `call` | a function, as a sub-orchestration | `<id>.md`, header only | empty | [part 04](04-loops-and-functions.md) |
| `for`, `while` | a body per item, or per pass | `<id>/index.json` | — | [part 04](04-loops-and-functions.md) |
| `if`, `case` | one branch | `<id>/index.json` | — | [part 04](04-loops-and-functions.md) |

**The folder gives the role, `kind` the mechanism**
([#1397](https://github.com/Consultologist/Consultologist-Blazor/issues/1397#issuecomment-6877969751)):
a check is `kind: computed` in `checks/`, a renderer and a deliverable are `kind: rendered` in
`renderers/` and `deliverables/`. `op` and `with` are retired: `terms_subset`, `all_pass` and
`count_pass` are operations called inside an expression (part 03).

### 1.1 Reproducible

- **`rendered` and `computed` are reproducible by construction;** **flow nodes** derive it from
  their `ret` (an `if` or `case` from every branch's); **a format that may vary** (any
  `x-agent: true` format, or a union holding one) isn't reproducible
  ([#1385](https://github.com/Consultologist/Consultologist-Blazor/issues/1385), decision 7).
- **A value that reads `run.*` is run-dependent** (part 03), and a body reading `run.*` records a
  run-neutral hash beside the sent-text hash, on which runs are compared
  ([#1389](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6920426229)).
  That answers #1385's open "reproducible with run values".
- **Proposed: no header has a `reproducible` member at 31.** An `agent` returns only `x-agent: true`
  formats (§ 2.2), which may vary, so the 30 author's claim (`"reproducible": true` on an agent
  node) can no longer be true. Reproducibility is derived for every kind and stamped on the record
  row ([part 08](08-record.md)). The migration drops the member.

## 2. Format

**Every `format` member is a format name or an inline schema** in the dialect (§ 3), for nodes and
inputs alike; an inline schema may `$ref` named formats
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6919612460)).

```jsonc
"format": "agent_plaintext"                                     // a name
"format": { "type": "array", "items": { "$ref": "markdown_block" } }  // an inline schema
```

- **A name** is resolved package-local first, then catalogue, then built-in (§ 6.3).
- **An inline schema** is an anonymous format: one-off shapes (an input's object, a computed
  node's small object) need no file. Revisiting "name or inline" is #1426 (Milestone 40).
- **Proposed: an `agent` node's format is always a name,** never inline. An agent format needs an
  `x-instruction`, a binding (§ 7) and a `formatHash` in the record, which a named file gives.
- **Proposed: an inline schema carries no `x-agent`,** and one written is refused: an inline
  schema is never an agent format, so it is `x-agent: false` by definition. It carries no
  `contentMediaType` or `x-markdown` at its root either; text is written as a `$ref` to a text
  format (§ 5.3).

### 2.1 Reading a value

As at 30 ([#1383](https://github.com/Consultologist/Consultologist-Blazor/issues/1383), § 1a):
- **data and object formats** are read as `.value` (`node.plan.value.sections`);
- **text formats** are read as `.content` (`node.draft_summary.content`);
- an object's `.content` is its canonical JSON.

### 2.2 Which formats each kind may return

| `kind` | May return | Rule |
|---|---|---|
| `agent` | `x-agent: true` formats only: `agent_plaintext`, `agent_markdown_block`, `agent_markdown_inline`, `agent_boolean`, catalogue and package object formats with `x-agent: true` | [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6878601823) |
| `rendered` (node) | a computed text format: `plaintext`, `markdown_block`, `markdown_inline` | [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1397#issuecomment-6878563370) |
| `rendered` (renderer) | a computed text format; its `input` may be any format | [part 05](05-templates-and-macros.md) |
| `rendered` (deliverable) | `markdown_block`, `plaintext`, or an object format | [part 06](06-checks-deliverables-profile.md) |
| `computed` | any `x-agent: false` format, equal to what its expression returns, checked through each operation's typed return | [part 03](03-expressions.md) |
| `computed` (check) | `check_result` | [part 06](06-checks-deliverables-profile.md) |
| `call` | the function's `format` | [part 04](04-loops-and-functions.md) |
| `for` | `list<T>`, where `T` is its `ret`'s format: `{ "type": "array", "items": T }` | [#1385](https://github.com/Consultologist/Consultologist-Blazor/issues/1385) decision 5 |
| `while` | exactly its `ret`'s format (the last pass) | as above |
| `if`, `case` | the union of what its branches return; one format when every branch returns the same | as above |

- **A union** is `{ "anyOf": [ A, B ] }`. Each branch's `ret` must be a member, and every member
  must be returned by some branch. An untaken `if` with no `else` reads as skipped through
  `.skipped`, so it is not a union member.
- **The union is the result itself,** a top-level `anyOf` with no wrapper object: only agent
  formats are sent to a model (§ 3.3), and the condition's row in the record says which branch ran
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6959885810)).
- **Reading a union:** `.content` always; `.value` only if every member has a value;
  `.value.<field>` only if every member declares that field compatibly.
- **Nested loops nest:** `list<list<agent_plaintext>>`. A loop's value holds only the items that
  completed, in order; items that didn't run are absent, never `null`, and are seen with their
  state and reason through `.items` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6959884271), part 04 § 2.5).
- **A macro's `format`** is `plaintext`, `markdown_block` or `markdown_inline`
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1412#issuecomment-6887873980)).
- **Proposed: flow nodes write `format`** in their `index.json`, and the checker refuses one that
  differs from the derived format. #1385 decision 2 has every node declare its format; some later
  examples (#1396, #1406) omit it.

### 2.3 Comparing formats

- **The checks between nodes** (a `for`'s format is an array of its `ret`'s, a union's members are
  its branches', an argument fits its parameter, a piped value fits a renderer's `input`) compare
  a **canonical form** of the dialect's shapes, not general schema equality
  ([#1385](https://github.com/Consultologist/Consultologist-Blazor/issues/1385), decision 8). The
  canonical form resolves every `$ref` to the name it names, orders `anyOf` members and
  `properties` by name, and drops `title`, `description` and `x-instruction`.
- **A text format fits where `string` is expected:** annotated strings are compatible with
  `string`; the annotations drive warnings and conversion, never type errors
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6878562886)).
  The reverse does not hold, and the typed operations still treat text as text (part 03).

### 2.4 Display: type strings

People see formats as **type strings**, printed from the schema; pickers write the schema
([#1385](https://github.com/Consultologist/Consultologist-Blazor/issues/1385), decision 8).

| In the file | Displayed |
|---|---|
| `"agent_plaintext"` or `{ "$ref": "agent_plaintext" }` | `agent_plaintext` |
| `{ "type": "number" }` | `number` |
| `{ "type": "array", "items": { "$ref": "agent_plaintext" } }` | `list<agent_plaintext>` |
| `{ "type": "array", "items": { "type": "array", "items": … } }` | `list<list<…>>` |
| `{ "anyOf": [ { "$ref": "agent_plaintext" }, { "$ref": "classification" } ] }` | `agent_plaintext \| classification` |
| **Proposed:** `{ "type": ["string", "null"] }`, `{ "anyOf": [ { "$ref": "x" }, { "type": "null" } ] }` | `string \| null`, `x \| null` |
| **Proposed:** an inline object | `{ title: string, notes: markdown_block \| null }` |
| **Proposed:** an enum | `string {consult, follow_up}` |

The Editor's picker also says what a format is, for example "written by an agent · plain text"
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6878601823)).

## 3. The dialect

Formats are written in **one JSON Schema 2020-12 dialect that matches Azure OpenAI's strict
structured outputs**, so every agent format can be sent in strict mode
([decided 2026-11-17](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6957666238)).
The format publishes the dialect's 2020-12 meta-schema, so any 2020-12 tool can check a package's
formats.

### 3.1 The keywords

| Keyword | Allowed | Rule |
|---|---|---|
| `type` | anywhere | `string`, `number`, `integer`, `boolean`, `object`, `array` or `null`; or an array of exactly one of those plus `"null"` (§ 3.2) |
| `properties`, `required` | on an object | `required` lists **every** declared property, and nothing else (§ 3.3) |
| `additionalProperties` | on every object | must be `false` |
| `items` | on an array | one schema |
| `enum` | on a string | the plain enum (§ 9) |
| `oneOf`, `const` | on a string, only in the labelled-enum form (§ 9) | every branch is exactly `{ "const": …, "title": … }` |
| `anyOf` | unions and nullable references | not at the root of an `x-agent: true` format (§ 3.3) |
| `$ref` | anywhere | a bare format id, or `#/$defs/<name>` within the file (§ 6.3); beside it, only `title`, `description` and `x-instruction` |
| `$defs` | at a format file's root | sub-definitions used inside the file |
| `$id` | at a published format's root | the format's URN (§ 6.2) |
| `title`, `description` | anywhere | annotations for people |
| `format` | only `"date"`, only beside `"type": "string"` | the built-in `date` (§ 4) |
| `contentMediaType` | at a text format's root | `text/plain` or `text/markdown` (§ 5) |
| `x-agent`, `x-markdown`, `x-instruction` | as § 5 says | our annotations |

**A keyword outside the dialect is refused** at publish, with a sentence. The rest of JSON Schema
(`pattern`, `allOf`, `if`/`then`, the length and count bounds, `patternProperties`, …) is not
adopted.

- **Proposed: `$schema` is not written in package sources.** The dialect is implied by
  `specVersion`, and `resolve.json`'s bundle declares the meta-schema once (part 07).
- **Proposed: recursive references** (a `$ref` that reaches its own definition) are refused at 31.
  Strict mode accepts them, but the canonical comparison (§ 2.3) and the printer (§ 2.4) are defined
  for finite shapes, and no package needs one today.
- **Strict mode's size limits** (properties and nesting) checked at publish are Milestone 40
  hardening ([#1385](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6879459060)).

### 3.2 One way to write "nullable"

Decided on [#1385](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6879459060):
- **Scalars:** a type array, `"type": ["string", "null"]`.
- **References and objects:** `"anyOf": [ { "$ref": "…" }, { "type": "null" } ]`, the only way to
  make a reference nullable.
- **Refused:** an `anyOf` with a `null` branch around a plain scalar, and a type array holding
  anything other than one type plus `"null"`.

### 3.3 `required`, `additionalProperties` and the root

Strict mode requires every property in `required`, `additionalProperties: false` on every object,
and an object, not an `anyOf`, at the root
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6959885810)):
- **in every format,** not only agent formats: every property of every object is listed in
  `required`, and an optional property has a nullable type (§ 3.2), for example
  `"type": ["integer", "null"]`. Every value then carries every key, with `null` when there is
  none, as every declaration does (#1394). A format whose object leaves a property out of
  `required` is refused (Refusals);
- **in every format:** `additionalProperties: false` on every object;
- **in an `x-agent: true` format only** (sent to a model): the root is not an `anyOf`. Its root
  need not be an object: the engine's envelope supplies the top-level object strict mode needs
  when it sends the format (§ 3.5);
- **in every other format** (an input's, a computed value's, a flow node's): the root may be a
  string, a list or an `anyOf`. An `if` or `case` whose branches return different formats keeps
  the `anyOf` as its result, with no wrapper (§ 2.2).

### 3.4 Validating values

- **Values are validated with Corvus.JsonSchema** (Apache-2.0): inputs, agent outputs and format
  examples. **Our own checker enforces the dialect's restrictions,** since Corvus can neither refuse
  keywords outside a dialect nor register our annotations
  ([research](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6871232338),
  [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6957666238)).
- Corvus sits behind our own interface, pinned exactly, and listed in `lock.json` (#1390). Moving to
  JsonSchema.Net later is #1459.
- **Proposed:** `format: "date"` is validated as an assertion: a string `YYYY-MM-DD` naming a real
  calendar date.

### 3.5 What a model receives

When an agent format is sent to the model provider, the engine prepares a copy; `resolve.json`
keeps the original
([#1385](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6879423102)):
1. each `description` is **replaced** by that schema's `x-instruction`, or **removed** where there is
   none; a field `$ref`ing a text format sends the format's instruction first, then its own;
2. every `x-` annotation and `contentMediaType` is stripped;
3. a referenced text format resolves to `{ "type": "string" }`;
4. a labelled enum (`oneOf` of `const`/`title`) becomes a plain `enum` of the `const`s
   ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6879459060));
5. **Proposed:** `title`, `$id` and `format: "date"` are stripped too (strict mode doesn't accept
   `format`; the reply is still validated against the original, § 3.4);
6. the result is sent inside the engine's envelope, which supplies the top-level object strict
   mode requires, so a text or list format can be sent too
   ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6959885810)).

The prepared schema is recorded by hash (the request's schema hash), so what the model received
stays provable.

## 4. Built-in and catalogue formats

| Owner | Formats | Where |
|---|---|---|
| **The package-format registry** (`consultologist-package-format`), versioned with the spec | data: `string`, `number`, `boolean`, `date`; computed text: `plaintext`, `markdown_block`, `markdown_inline`; `check_result` | `formats/index.json` + `formats/<id>.json` beside the dialect's meta-schema |
| **The formats catalogue** (`consultologist-agents`) | agent text: `agent_plaintext` (was `prose`), `agent_markdown_block`, `agent_markdown_inline`; `agent_boolean`; `concept_list`, `classification` | `agents/formats/index.json` + `<id>.json`, replacing `agents/formats.json` |
| **A package** | its own formats | `formats/index.json` + `formats/<id>.json` |

([where](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6878965680),
[per owner](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6882203602))

- **`rendered` and `terms_subset` stop being formats:** they are `kind: rendered` and an operation
  returning `check_result`. `producer` leaves every format file.
- **`date`** is `{ "type": "string", "format": "date", "x-agent": false }`, so `"format": "date"`
  works ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6919612460)).
- **`agent_boolean`** is a model-written true or false with a short reason:
  `{ answer: boolean, reason: agent_plaintext }`, `x-agent: true`
  ([#1412](https://github.com/Consultologist/Consultologist-Blazor/issues/1412)).
- **`check_result`**'s members are [part 06](06-checks-deliverables-profile.md).
- **Proposed: `titled_section`** (#1406's candidate) is not a built-in at 31; a package declares it
  in `formats/`. Nothing yet needs it shared, and adding a built-in later is compatible.
- **Proposed: packages define object and array formats only.** Scalar data and text formats come
  from the registry and the catalogue: a package references `markdown_block` or `agent_plaintext`
  rather than declaring its own text format, since an agent text format needs an attested agent
  (§ 7).

`formats/markdown_block.json` in the registry:

```json
{
  "$id": "urn:consultologist:spec:31:format:markdown_block",
  "type": "string",
  "x-agent": false,
  "contentMediaType": "text/markdown",
  "x-markdown": "block",
  "description": "Markdown text that stands on its own lines (a paragraph, a list or a table)."
}
```

## 5. Data and text

**`string` is data:** a value, not content (an id, a code, an enum value, a label). **Text formats
are content** meant to be read or composed, and always carry a media type
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6878562886),
[names](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6878601823)).

| Format | Is | `x-agent` | `contentMediaType` | `x-markdown` |
|---|---|---|---|---|
| `string`, `number`, `boolean`, `date` | data | `false` | omitted | omitted |
| `plaintext` | computed text | `false` | `text/plain` | omitted |
| `markdown_block` | computed text | `false` | `text/markdown` | `block` |
| `markdown_inline` | computed text | `false` | `text/markdown` | `inline` |
| `agent_plaintext` | text written by an agent | `true` | `text/plain` | omitted |
| `agent_markdown_block` | text written by an agent | `true` | `text/markdown` | `block` |
| `agent_markdown_inline` | text written by an agent | `true` | `text/markdown` | `inline` |
| object formats | structured data | `true` or `false` | omitted | omitted |

There is no bare `prose`. A `block` Markdown text stands on its own lines; an `inline` one sits
inside a line.

### 5.1 The annotations

They follow JSON Schema's convention: **present where they apply, omitted otherwise, never `null`.**
The checker refuses one missing where it applies, and one present where it doesn't
([#1394 boundary](https://github.com/Consultologist/Consultologist-Blazor/issues/1394#issuecomment-6878756179)).

| Annotation | Where | Meaning |
|---|---|---|
| `x-agent` | **required at the root of every format file** | `true` when an agent produces the value, so it may vary between runs. Replaces `x-generated` |
| `contentMediaType` | at the root of every text format | the text's media type |
| `x-markdown` | at the root of every Markdown format | `block` or `inline` |
| `x-instruction` | **required at the root of every `x-agent: true` format; allowed on any nested schema inside one; refused in `x-agent: false` formats** | what the model should write; replaces `description` in what the model receives (§ 3.5) |
| `description` | anywhere | what the value **is**, for authors: the Editor, the checker's sentences, the docs. Never sent to a model |

- **Our annotations sit at the top of each format,** the format as a return type, not on every
  nested field ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6878928645)).
- **A warning, not a refusal:** a field inside an agent format with a `description` and no
  `x-instruction` sends the model no guidance for it
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6879423102)).
- **Both keywords are part of the format,** so changing either changes its hash; changing an
  instruction changes what is produced (an output-affecting change under #1390).

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
A field that is text meant to be read or composed is written `{ "$ref": "agent_plaintext" }`, which
gives it its media type and its "written by an agent" marker, so the typed operations, escaping and
plain-text conversion treat it as text.

`example-nested-note`'s `formats/verdict.json` at 31:

```json
{
  "type": "object",
  "x-agent": true,
  "description": "A judgement of a draft summary: good, or revise with what to change.",
  "x-instruction": "A judgement of a draft summary: good, or revise with what to change.",
  "required": ["verdict", "feedback"],
  "additionalProperties": false,
  "properties": {
    "verdict":  { "type": "string", "enum": ["good", "revise"] },
    "feedback": { "$ref": "agent_plaintext" }
  }
}
```

(The migration copies 30's `description` into `x-instruction` unchanged, so nothing a model sees
changes, and leaves the description for the author to rewrite; § 11.)

## 6. Format files, ids and references

### 6.1 `formats/index.json` and one file per format

**Every owner** (the registry, the catalogue, a package) writes its formats as `formats/index.json`,
a JSON Schema `$defs` whose entries `$ref` their files, plus `formats/<id>.json`
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6879096084)):

```json
{
  "$defs": {
    "section_plan": { "$ref": "section_plan.json" },
    "outline":      { "$ref": "outline.json" },
    "verdict":      { "$ref": "verdict.json" }
  }
}
```

- **The file name is the id;** `$defs` lists every file in the folder exactly once, in order.
- **Types only:** no agent names or versions; bindings live apart (§ 7).
- **A format file is a JSON Schema in the dialect,** with no wrapper: 30's `type`, `producer`,
  `outputMacros` and `schema` wrapper members are gone, and the schema is the file.
- **`description` on `formats/index.json`.** Part 01 gives every `index.json` a required
  `description` of lines, but this index is a JSON Schema document, where `description` must be a
  string. **Proposed:** here it follows JSON Schema's side of the #1394 boundary: a string of at most
  500 characters, or omitted for none.
- **A function's own formats** sit in `functions/<id>/formats/`, shaped the same (part 04).

### 6.2 `$id`s are URNs

Every published format has a `$id`, a URN that doesn't depend on where anything is hosted
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6957666238)):

| Owner | `$id` | Example |
|---|---|---|
| the formats catalogue | `urn:consultologist:format:<name>@<catalogue-version>` | `urn:consultologist:format:concept_list@v2026.12.1` |
| a package | `urn:consultologist:package:<package>@<version>:format:<id>` | `urn:consultologist:package:example-nested-note@v2026.12.1:format:verdict` |
| the registry's built-ins ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6959885810)) | `urn:consultologist:spec:<specVersion>:format:<name>` | `urn:consultologist:spec:31:format:markdown_block` |

- **A package format's `$id` is derived at publish** from the package's name and version
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6879459060)).
  **Proposed:** it is not written in the package's source, and a `$id` found there is refused, since
  it would have to be rewritten at every version.
- A package name may hold `/` (`test/general`), which a URN's specific string allows.
- Within a format, `$id` follows JSON Schema's own rules.

### 6.3 References by bare id; no shadowing

- **In source, a format is named by its bare id:** `"format": "agent_plaintext"`,
  `{ "$ref": "agent_plaintext" }`. The resolver looks it up **package-local first, then catalogue,
  then built-in**, and rewrites it to `#/$defs/<id>` in `resolve.json`'s single inline bundle
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6879096084)).
- **No shadowing:** a package format may not reuse a catalogue or built-in name, as #1384 rules for
  nodes. So the lookup order never changes which format a name means.
- **Inside a format file,** `#/$defs/<name>` refers to that file's own `$defs`.
- **In a function,** a name resolves to the function's own formats, then the package formats its
  `uses` lists, then catalogue and built-in; built-in and catalogue formats need no listing
  ([#1419](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6888253558)).
- **`resolve.json`** holds exactly the formats the package uses, each inline with its origin, its
  `$id` and its hash ([part 07](07-compiled-documents.md)).

## 7. Agent bindings

**What a value is and who produces it are two facts in two files**
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6879413099)):

| File | Holds |
|---|---|
| `formats/index.json` (every owner) | types only |
| `agents/bindings.json` (the agents catalogue) | which attested agent serves which catalogue format, and the `producers` section |

```json
{
  "version": "v2026.12.1",
  "formats": {
    "agent_plaintext":       { "agentName": "consult-prose",      "agentVersion": 4 },
    "agent_markdown_block":  { "agentName": "consult-prose",      "agentVersion": 4 },
    "agent_markdown_inline": { "agentName": "consult-prose",      "agentVersion": 4 },
    "agent_boolean":         { "agentName": "classification",     "agentVersion": 1 },
    "concept_list":          { "agentName": "concept-extraction", "agentVersion": 1 },
    "classification":        { "agentName": "classification",     "agentVersion": 1 }
  },
  "producers": {
    "custom": { "agentName": "custom-json", "agentVersion": 1 }
  }
}
```

(Names and versions illustrative.)

- **An agent node gets its agent through its format's binding:** a catalogue format through
  `formats`, a package format with `x-agent: true` through `producers.custom`.
- **No agent settings per node.** Model, reasoning effort and instructions live in the attested
  agent definitions; different behaviour means a format bound to a different agent. A node can't
  name an agent
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6923942272)).
- **The checks:** publishing the agents catalogue checks that every binding names an existing format
  and an attested agent; publishing the formats catalogue checks that every `x-agent: true` format
  has a binding.
- **`resolve.json`** records the bundle and a separate `bindings` section; `lock.json` pins the
  agents catalogue beside the formats catalogue (part 07). Agent rows in the record name the attested
  definition and the binding that chose it ([part 08](08-record.md)).

## 8. Inputs

**Inputs are an object keyed by id in `manifest.json`.** The written order is the Create page's
order. Every member is present
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6919612460)).

| Member | Type | `null` | Meaning |
|---|---|---|---|
| `label` | string | no | the Create page label |
| `description` | array of strings | yes | help text on the Create page; lines joined with `"\n"`, at most 500 characters |
| `format` | a format name or an inline schema | no | the input's type, which picks the widget |
| `required` | boolean | no | whether the user must supply it; one that isn't required reads `null` when left blank |
| `channel` | string | yes | the content channel (§ 8.3); replaces `expectedContent` |

- **There is no `default` member;** an explicit default is #1456 (Milestone 40)
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6957666238)).
- **There is no `labelField`:** a loop over the input names its items with `itemLabel` (part 04).
- **Input ids** follow part 01's id rules.
- **A top-level input keeps `required: true | false`** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6959885810)): it says whether the
  user must supply it, not what the format's `required` list says. An optional input left blank
  reads `null`, in JSON Logic and in templates, and the null rules of part 03 apply.
- **Fields inside an object input follow the format rule** (§ 3.3): every field is listed in
  `required`, an optional field has a nullable type, and a field whose type is not nullable must
  be filled in ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6959885810)).
- **Order matters:** the object's member order is the Create page's. **Proposed:** `resolve.json`
  keeps it explicitly, since canonical JSON (RFC 8785) sorts members (part 07).
- **Function inputs** keep #1419's shorter `{ "format", "description" }`; every parameter is required,
  and a caller passes `null` through `with` for one it leaves out
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6924498571), part 04).

### 8.1 Which formats an input may take, and its widget

| Format | Create page widget |
|---|---|
| `string` | a single-line field |
| `string` with `enum` or labelled `oneOf` (§ 9) | a select, showing each value's `title` |
| `number`, `boolean` | a number field; a checkbox |
| `date` | a date picker |
| `plaintext` | a multi-line text area |
| `markdown_block` | a rich-text editor (toolbar, preview), **stored as Markdown, not HTML** |
| an object (inline or a package format) | a field group, one field per property, labelled by its `title` |
| an array | repeating rows, whose `items` take the same formats |

([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6878562886))

- **Object fields are JSON Schema `properties`:** a field's label is its `title`, every field is
  in the `required` list, an optional field is nullable (§ 8), and fields nest to any depth. 30's `fields`/`items`/`values` grammar goes.
  **Proposed:** `title` is required on every property of an input's schema, so no field is shown by
  its bare id.
- **Proposed: refused as an input's format:** any `x-agent: true` format (a clinician writes
  inputs, not an agent); `markdown_inline` (no widget, and an inline fragment is not something a
  clinician submits); a root `anyOf` (no widget can offer "one of these shapes").
- **Plain text pasted into a Markdown input** can be misread (a leading `#` becomes a heading); the
  rich-text editor shows the formatting as it is typed.

### 8.2 Documents

**Documents need no member.** Any input with a text format takes typed text or a document (the
parser decides), and an array of a text format takes several items, each typed, a document, or an
earlier run's deliverable, in the order sent (#872, #874, #1144). A data-format input is typed only,
except an object filled from a `form`.

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

The migration writes 30's `text` field `title` as `markdown_block` and the enum's titles equal to
its values (§ 11); `notes` is optional, so it is nullable and a blank one is sent as `null`. This
example shows the author's later edits (`title` narrowed to `string`, labels
written). The loops over `input.sections` say `"itemLabel": "title"` (part 04). A condition reads
`{ "===": [ { "var": "item.value.kind" }, "narrative" ] }`, displayed `item.value.kind = "narrative"`.

## 9. Enum labels

Decided on [#1061](https://github.com/Consultologist/Consultologist-Blazor/issues/1061#issuecomment-6879459298)
and [#1385](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6879459060):

- **Labelled values are JSON Schema's standard form,** `oneOf` of `const` with `title`:
  ```json
  { "type": "string", "oneOf": [
      { "const": "new_patient", "title": "New patient" },
      { "const": "follow_up",   "title": "Follow-up" } ] }
  ```
- **Plain `enum`** (no labels) stays allowed. A plain value's label is its value.
- **The value is stored and compared.** Conditions, skip reasons and the record name the value,
  never the label. **The label is data shown** by the Create page's select, the Editor and History.
- **Classification answers get their labels the same way.**
- **For a model provider,** the schema becomes a plain `enum` of the `const`s (§ 3.5).
- **The rules. Proposed** (30's enum rules carried over, plus #1061's): at least two values; values
  unique; titles non-empty and unique within the schema; in inputs and package formats, every value
  matches `^[a-z][a-z0-9_]*$`, as 30's `values` do, so values stay printable in conditions and
  reasons; a schema has `enum` or the labelled `oneOf`, never both.
- **Proposed: templates read no `.label`** at 31. `input.<id>` renders the value, and a template
  that wants a label writes its own text. 30's planned `input.<id>.label` (#1061's v26 shape) was
  never built, and the Scriban dialect (#1425) lists no such read.

## 10. Per-run macro choices become optional boolean inputs

At 30 a macro could be offered as a per-run choice on the Create page (`optional`, with a
`default`). At 31 **each choice is an optional boolean input that the macro's `if` reads,** so
today's defaults carry over with no new member
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1412#issuecomment-6957666676)):

| 30 default | 31 test in the macro's `if` | While the input is unset |
|---|---|---|
| off | `{ "===": [ { "var": "input.<id>" }, true ] }` | `null === true` is false: not offered |
| on | `{ "!==": [ { "var": "input.<id>" }, false ] }` | `null !== false` is true: offered |

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

- **Proposed:** the input's id is the macro's id and its `label` the macro's label; where the id is
  already an input, the migration leaves a hand edit (part 10). A macro that already had an `if`
  gets `{ "and": [ <its if>, <the test> ] }`.
- **Proposed: the Create page shows an optional boolean as three choices** (not set, yes, no), so
  "not set" keeps the 30 default. A plain checkbox would show a default-on choice unchecked while
  meaning on ([part 09](09-engine-and-pages.md)).

## 11. What the migration does to types and inputs

Mechanical, in `WorkflowPackageMigration30To31` ([part 10](10-migration-and-tools.md),
[#1447](https://github.com/Consultologist/Consultologist-Blazor/issues/1447)):

| At 30 | At 31 |
|---|---|
| every node | gains `kind`; `format` becomes its return type |
| `"format": "prose"` | `agent_plaintext` |
| a `rendered` format | `kind: rendered`, a computed text format |
| a `let` with `type` | `kind: computed`, `format` from `type`, `expression` in JSON Logic (#1423) |
| a `terms_subset` check | `checks/<id>.md`, `kind: computed`, `format: check_result` |
| `x-generated` | `x-agent` |
| a format file's wrapper | the schema alone; `producer` and `outputMacros` gone; `description` copied into `x-instruction` |
| the manifest's `formats` list | `formats/index.json` |
| inputs as an array | an object keyed by id |
| `type: text` (inputs and fields) | `markdown_block` |
| `type: enum` + `values` | `string` + `oneOf`; **Proposed:** each `title` equal to its value, so the select shows what it shows at 30 |
| `type: date` | `"date"` |
| `fields` | `properties` + `required` listing every field, an optional field made nullable (+ `title` from each field's `label`) |
| an object format's partial `required` (catalogue and package) | every property listed in `required`, the optional ones made nullable ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6959885810); part 10 § 1.8) |
| `expectedContent` | `channel` |
| `labelField` | `itemLabel` on each loop over the input |
| (no description) | `description: null` |
| a per-run macro choice | an optional boolean input (§ 10) |

- **Suggestions, never applied silently:** `agent_markdown_block` for list-like sections
  ([#1406](https://github.com/Consultologist/Consultologist-Blazor/issues/1406#issuecomment-6881841286)).
  **Proposed:** in the same way, a package format's string field whose description reads like text
  ("sentence", "summary") is suggested as a `$ref` to a text format; every string field stays data
  until the author accepts.
- **Proposed:** 30's union inputs (`type: [x, array]`, #729) become the array arm (an array of `x`
  takes one item as well as several); any other union is a hand edit. No public package declares
  one.
- **Proposed:** a node's `reproducible` member is dropped (§ 1.1).

## 12. `WorkflowPackages/Current`: what satellites see

The engine's `WorkflowPackages/Current` response is the Copilot agent's and the Zoom app's only
contract, and it **changes by addition only:** version 31 inputs map into its existing fields plus
a new `format` member, and `Macros` comes back empty for version 31 packages
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6957666238)).
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
| **`Format`** (new) | the input's format as a JSON schema (below) |

**`Type`, derived from the format:**

| Format | `Type` |
|---|---|
| `plaintext`, `markdown_block` | `text` |
| `string` with `enum` or a labelled `oneOf` | `enum` |
| `string` with no values | **Proposed:** `text` (30 has no single-line string type; the satellite's text field takes it) |
| `date` | `date` |
| `number`, **Proposed:** `integer` | `number` |
| `boolean` | `boolean` |
| an object | `object` |
| an array | `array` |

- **`Format`: Proposed** a trailing optional `JsonElement` (`null` below 31): the input's format as
  a self-contained schema, every `$ref` replaced by the definition it names (each keeping its `$id`
  URN, so a named format stays recognisable), with titles and our annotations kept. A satellite then
  needs no registry to read it.
- **`Macros`: always `[]`** (empty) for a 31 package ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6957666238)):
  31 has no per-run macro choices; they arrive as optional boolean inputs.
- **A satellite that can't show an input** (an inline object schema, a document upload) shows
  *"This workflow needs the web app"*, with a link
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1447#issuecomment-6957669427)).
- **Proposed:** `description` is not added to the response at 31; only `format` was decided.

## Refusals

Sentences marked **Proposed** are this design's wording. A decided rule whose words were not decided
is marked Proposed too.

**Decided wording:**
- *"This workflow needs the web app"* (a satellite, not the checker: [#1447](https://github.com/Consultologist/Consultologist-Blazor/issues/1447#issuecomment-6957669427)).

**Kinds and formats (Proposed wording):**
- *"Node '<id>' declares unknown kind '<kind>' (accepted: agent, rendered, computed, for, while, if, case, call)."*
- *"Node '<id>' declares kind let; a let is now kind computed, with its JSON Logic in expression."*
- *"Node '<id>' is kind agent and returns <format>, which no agent writes: an agent returns an agent_* text format or a format with x-agent: true."*
- *"Node '<id>' is kind <kind> and returns <format>, which an agent writes: only an agent node returns a format with x-agent: true."*
- *"Node '<id>' is kind rendered and returns <format>; a rendered node returns plaintext, markdown_block or markdown_inline."*
- *"Node '<id>' is kind agent and declares an inline format; an agent's format is named, in formats/."*
- *"<Flow> '<id>' declares format <declared>, but its ret gives <derived>."*
- *"Branch <n> of <if | case> '<id>' returns <format>, which is not in the declared union <union>."*
- *"Format '<name>' is not a built-in, catalogue or package format."*
- *"Node '<id>' declares reproducible; at 31 reproducibility is derived from kind and format, not declared."*

**The dialect (Proposed wording):**
- *"<Format | Input '<id>'> uses keyword '<keyword>' at <path>, which is outside the formats dialect."*
- *"<Format> at <path> declares additionalProperties <value>; every object declares additionalProperties: false."*
- *"<Format> at <path> lists '<property>' in required but declares no such property."*
- *"<Format> at <path> leaves '<property>' out of required; every property is listed in required, and an optional one is written as nullable."*
- *"Agent format '<name>' is an anyOf at its root, which strict mode can't send; wrap it in an object."*
- *"<Format> at <path> writes a nullable <type> with anyOf; write it as \"type\": [\"<type>\", \"null\"]."* (decided rule: [#1385](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6879459060))
- *"<Format> at <path> declares type <list>; a type array is one type plus \"null\"."*
- *"<Format> at <path> uses format '<value>'; the only format is date, on a string."*
- *"<Format> at <path> has '<keyword>' beside $ref; only title, description and x-instruction may sit beside a $ref."*
- *"<Format> at <path> refers to itself; recursive formats aren't supported."*
- *"Package format '<id>' declares $id; a package format's $id is written at publish."*
- *"Package format '<id>' is a <string | number | boolean>; a package defines object and array formats, and refers to built-in and catalogue formats by name."*

**Annotations (Proposed wording):**
- *"Format '<name>' declares no x-agent; every format says whether an agent writes it."*
- *"Format '<name>' is a text format and declares no contentMediaType."*
- *"Format '<name>' is Markdown and declares no x-markdown (block or inline)."*
- *"<Format> declares <contentMediaType | x-markdown> at <path>, where it doesn't apply."*
- *"Agent format '<name>' declares no x-instruction; the model needs one."*
- *"Format '<name>' is not written by an agent and declares x-instruction at <path>; no model reads it."*
- *"<Input '<id>' | Node '<id>'>'s inline format declares x-agent; an inline format is never written by an agent."*
- Warning: *"Agent format '<name>' gives '<field>' a description and no x-instruction: the model will see no guidance for '<field>'."*

**Formats folder (Proposed wording):**
- *"formats/index.json lists '<id>', but formats/ has no <id>.json."*
- *"formats/index.json's entry '<id>' refers to '<file>'; an entry refers to <id>.json."*

**Inputs (Proposed wording):**
- *"Input '<id>' takes format <format>, which an agent writes; inputs are written by people."*
- *"Input '<id>' takes markdown_inline; use plaintext or markdown_block."*
- *"Input '<id>' takes a choice of shapes (anyOf); an input has one shape."*
- *"Input '<id>' declares channel '<channel>' (accepted: transcript, image, ambient-note, form)."*
- *"Input '<id>' declares channel '<channel>', which is only for a text input or an array of text."*
- *"Input '<id>' declares channel 'form', which needs an object with at least one property."*
- *"Input '<id>' field '<property>' has no title; the Create page labels each field by its title."*
- *"Input '<id>' declares default; inputs have no default at 31."*

**Enums (Proposed wording):**
- *"<Subject> declares one value; an enum with one value is a constant, not a choice."* (30's sentence, carried over)
- *"<Subject> declares value '<value>' twice."*
- *"<Subject> value '<value>' must be snake_case (a lowercase letter, then lowercase letters, digits, or underscores)."* (30's sentence)
- *"<Subject> gives two values the title '<title>'."*
- *"<Subject> writes oneOf branch <n> as <shape>; each branch is exactly { \"const\": <value>, \"title\": <label> }."*
- *"<Subject> declares both enum and oneOf; write one."*

## Proposed in this part

- **No `reproducible` member; derived for every kind:** an agent returns only formats that may vary, so 30's author claim can't hold.
- **An agent node's format is always a name:** agent formats need an `x-instruction`, a binding and a per-format hash.
- **Inline schemas carry no `x-agent`, `contentMediaType` or `x-markdown`:** they are never agent formats, and text is written as a `$ref` to a text format.
- **Flow nodes write `format`, checked against the derived one:** #1385 decision 2; some later examples omit it.
- **The canonical form for comparing formats** (refs by name, sorted `anyOf` and `properties`, annotations dropped): #1385 asks for one without defining it.
- **Display of nullables, inline objects and enums** (`x | null`, `{ title: string }`, `string {a, b}`): the printer was decided only for lists, unions and primitives.
- **`$schema` not written in package sources:** `specVersion` implies the dialect.
- **Recursive formats refused:** the comparison and printer are defined for finite shapes; nothing needs recursion.
- **`format: "date"` asserted by Corvus and stripped before sending:** strict mode rejects `format`, and the value must still be a real date.
- **`title`, `$id` and `format` stripped from what a model receives:** strict mode doesn't need them.
- **`titled_section` is not a built-in at 31:** nothing needs it shared yet; adding it later is compatible.
- **Packages define object and array formats only:** text and data scalars are the registry's and catalogue's, and agent text needs an attested agent.
- **A package format's `$id` is not written in source:** it is derived at publish and would change every version.
- **`formats/index.json`'s `description` is a JSON Schema string or omitted:** the file is a schema, where `description` can't be lines or `null`.
- **`resolve.json` keeps input order explicitly:** canonical JSON sorts members, and the order is the Create page's.
- **`title` required on every input property:** no field is shown by its bare id.
- **Inputs refuse agent formats, `markdown_inline` and a root `anyOf`:** no widget, and people write inputs.
- **Enum rules** (two or more values, unique values and titles, snake_case values in inputs and package formats, never `enum` and `oneOf` together): 30's rules and #1061's, carried into the dialect form.
- **No `.label` read in templates:** never built at 30, and the #1425 dialect lists none.
- **Per-run choice inputs take the macro's id and label, `and`-ed with an existing `if`; the Create page shows three states:** the decision gives the tests, not the input's names or widget.
- **Migration: enum titles equal to their values; text-like string fields suggested as text, never applied; union inputs to their array arm; `reproducible` dropped.**
- **`WorkflowPackages/Current`:** a plain `string` maps to `Type: text` and `integer` to `number`; `Format` is a self-contained schema with `$id`s kept; `Macros` is `[]` (decided); no `description` member yet.
- **Every refusal sentence marked Proposed:** the issues decided the rules, not their words.
