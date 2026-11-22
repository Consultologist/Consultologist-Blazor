# 05. Templates and macros

This part defines every template a version 31 package holds and the one template language they
share: the Consultologist Scriban dialect (#1425), renderers as package templates (#1397),
snippets, parts, and macros with their whether and where (#1409, #1412). It states what each
template may read, how placements carry whitespace, how run facts are kept out of the
comparable hash (run spots), and every refusal sentence. Deliverable templates are templates
too: their reads are listed here, and everything else about them is in
[Part 06](06-checks-deliverables-profile.md). Personal (account-level) macros are not in
version 31 (#1417, Milestone 40).

## 1. Templates present; headers decide

**The rule:** decisions are recorded, so they live in headers as JSON Logic
([Part 03](03-expressions.md)). Bodies only present
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1412#issuecomment-6887226097)).

- **Whether** a node runs, an item is included, a part or snippet is used, a macro is offered,
  a loop stops or a deliverable exists is a header condition, evaluated and recorded by the
  engine.
- **A body** only lays out what it's given: loops over lists, joins, formatting, and the
  choice of how a recorded fact (a skipped item, a failed check) appears.

### 1.1 The template sites

| Site | Where it lives | Rendered | Its output |
|---|---|---|---|
| prompt | the body of `nodes/…/<id>.md`, `kind: agent` | before the model call, per instance | the text sent to the model |
| `rendered` node | the body of `nodes/…/<id>.md`, `kind: rendered` | per instance, no model | the node's text value |
| renderer | the body of `renderers/<id>.md` | at each pipe use | text spliced at the pipe |
| snippet body | the body of `snippets/…/<id>.md` | where it's placed, in that scope | text spliced at the placement |
| part text | `parts.<id>.text` in an agent node's header | where it's placed, when its `if` holds | text spliced at the placement |
| macro instruction | `instruction` in `macros/…/<id>.md` | where it's placed, when the macro is triggered | prompt text, with the macro's marker |
| macro body | the body of `macros/…/<id>.md` | after the model replies, before substitution | text substituted for the marker |
| deliverable | the body of `deliverables/…/<id>.md` | once, after its reads complete | the deliverable's composed text |

A `computed` node and a check have no template: their expression is JSON Logic in the header
member `expression`, and their body must be empty ([Part 03](03-expressions.md)).

### 1.2 An agent node's prompt members

Beside `kind`, `label`, `description`, `format`, `reproducible` (always `false` for an agent
node, [Part 01](01-files-and-scopes.md)) and `producer` (always `"generated"`,
[Part 02 § 1.3](02-types-and-inputs.md#13-producer)), `modelDerived` (always `true` for an agent
node) and `runDependent` (`true` when its prompt reads `run.*` or a run-dependent value) and
`profileCarrying` (`true` when its prompt places a macro whose body reads `profile.*`, or reads such a
value, [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)), all
checked against the derived values ([Part 02 § 1.2](02-types-and-inputs.md#12-the-contagious-flags-run-dependent-and-model-derived), [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1478#issuecomment-6982835861)), and its agent binding, chosen by format and
producer ([Part 02](02-types-and-inputs.md)),
a `kind: agent` node's header holds the members that govern its prompt and reply
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6978508523)):

| Member | Type | Null | Meaning |
|---|---|---|---|
| `onInvalid` | `"retry"` or `"fail"` | no | what an invalid reply does: a reply that fails its format, or a macro marker check (§ 8.6). `retry` asks once more; `fail` fails the job. As at version 30. |
| `failIfEmpty` | string | yes: an empty value doesn't fail | the sentence the job fails with when the reply's value is empty. Carried over from version 30 unchanged, and always written (`null` when unused). |
| `parts` | object | yes: none | the node's conditional prompt text (§ 7) |
| `ordered` | boolean | no | whether triggered macro markers must come back in order (§ 8.5) |

## 2. The dialect

The dialect is a published, versioned spec in the package-format registry, beside the JSON
Schema dialect ([Part 02](02-types-and-inputs.md)), with conformance vectors: a valid and an
invalid template for every rule and every refusal sentence below. **Anything not listed as
allowed is refused at publish, with a sentence**
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6920252420)).

The engine renders with **Scriban 7.2.5**, pinned exactly. The dialect's version is recorded in
`lock.json` and listed in `requires` ([Part 07](07-compiled-documents.md)).

### 2.1 Statements

**Allowed:**

| Construct | Written | Notes |
|---|---|---|
| output | `{{ x }}` | `x` is an access path, optionally piped (§ 2.2, § 5) |
| member access | `node.plan.value.sections` | index access (`list[0]`) is refused (below) |
| literals | `"text"`, `12`, `true`, `false`, `null` | as arguments to built-ins and in `if` comparisons |
| `for` | `{{ for x in list }} … {{ end }}` | over a list only; position variables `for.first`, `for.last`, `for.index`, `for.even`, `for.odd` |
| `if` | `{{ if … }} … {{ else if … }} … {{ else }} … {{ end }}` | restricted to recorded facts (§ 2.5) |
| pipe | `x \| string.strip`, `x \| renderer.concept_bullets` | to the allowed built-ins (§ 2.2) and to renderers (§ 5) only |
| comment | `{{# a note }}` | rendered as nothing |
| raw block | `{%{ … }%}` | literal braces, not interpreted |

**Refused:**
- assignment (`x = …`) and `capture`;
- `func`, `include`, `import`, `with`, `wrap`;
- Scriban's `while`, `break` and `continue`;
- `case` / `when`;
- `tablerow`;
- arithmetic (`+`, `-`, `*`, `/`, `%`, `//`) and string concatenation by `+`. Markdown numbers a
  `1.` list itself, and a computed value belongs in a `computed` node.
- a `for` over anything but a list (a range `1..3`, an object), and the `for` options `limit:`,
  `offset:` and `reversed`: they choose which elements appear, which is a decision;
- `for.rindex`, `for.length` and `for.changed`;
- **index access** (`list[0]`, `node.plan.value.sections[2]`) ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)): a list is read whole, by
  a `for`, or by identity through `.items`, as in JSON Logic (part 03 § 2.2). Pairing by position
  across the format is #1485.

  These `for` forms are refused ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6978508523)); no published package uses them. Revisiting
  them is #1475 (Milestone 40).

**Equality in bodies.** Scriban has no `===`. A body `if` compares with Scriban's `==` and
`!=`, only within § 2.5's recorded facts. The `===`/`!==`-only rule is JSON Logic's
([Part 03](03-expressions.md)), which is where every value comparison lives.

### 2.2 Built-ins

Built-ins are pure, and versioned like the built-in operations: each is listed in `requires`
with a caret range and in `lock.json` at its exact version
([#1390](https://github.com/Consultologist/Consultologist-Blazor/issues/1390)).

| Built-in | Signature | Result | Null in |
|---|---|---|---|
| `string.upcase` | `(text: string \| text format) → same format` | invariant upper case | null (renders as nothing) |
| `string.downcase` | `(text: string \| text format) → same format` | invariant lower case | null |
| `string.capitalize` | `(text: string \| text format) → same format` | first character upper case, invariant | null |
| `string.strip` | `(text: string \| text format) → same format` | leading and trailing whitespace removed | null |
| `array.join` | `(list: list<string> \| list<text format>, separator: string literal) → string`, or the elements' text format | the elements joined by `separator` | null list gives null; a null element fails the node |
| `array.size` | `(list: list<any>) → number` | the element count | null |
| `date.to_string` | `(date: date, pattern: string literal) → string` | the date formatted by `pattern` | null |

- **`date.to_string` requires an explicit pattern,** a string literal (Scriban's `%` patterns,
  for example `"%d %B %Y"`). A date output without it renders as ISO `%Y-%m-%d`.
- **Refused:** `date.now`, random numbers and environment access (they break reproducibility);
  `truncate` (it cuts clinical text); `object.default` (it chooses text, which is a decision);
  every other built-in.
- **Null:** a null value output directly renders as nothing, and **a built-in given null returns
  null**, matching JSON Logic's rule that operations on null give null
  ([Part 03](03-expressions.md)). The one exception is `array.join`: a list holding a `null`
  element fails the node ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6978508523)).

### 2.3 Limits and carried-over behaviour

The limits are dialect constants, separate from `FormatBounds`
([Part 01](01-files-and-scopes.md)):

| Limit | Value | Past it |
|---|---|---|
| iterations per `for` | 1,000 | the render fails with a sentence (§ Refusals, R-L1) |
| `if` and `for` nesting | 15 deep | refused at publish (R-L2) |
| rendered output | 1 MiB (1,048,576 bytes, UTF-8) | the render fails with a sentence (R-L3) |
| steps per render | 100,000, an engine setting ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660); part 09 § 3.4) | the render fails with a sentence (R-L4) |
| recursion | none possible | there's no `func` or `include`; snippet cycles are refused at publish (§ 3.2) |

A render failure fails the node (or the deliverable) as a definition error: no retry. A prompt
that fails to render is never sent. The failure follows the node's normal failure path, not
`onInvalid`, which is about replies ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6978508523)).

**Carried over from version 30:**
- **strict variables:** reading a member that doesn't exist is refused at publish, and a render
  that meets one fails;
- **dates render as ISO** (`%Y-%m-%d`) unless formatted;
- **an empty list counts as false** where a truth value is read.

### 2.4 The invariant culture

Every render runs in the invariant culture: English month and day names in `date.to_string`,
culture-free case mapping in `string.upcase`, `downcase` and `capitalize`, and invariant number
formatting. It's stated in the dialect and recorded in `lock.json`, so a host's culture can
never change a note's bytes or hashes
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6924903846)).
A clinician's locale is #1452 (Milestone 40).

### 2.5 A body `if` tests recorded facts only

A body `if`, in every template (snippet and macro bodies included), may test only facts the
engine has already decided and recorded
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6920252420)):

| Fact | Written | Values |
|---|---|---|
| loop position | `for.first`, `for.last`, `for.index`, `for.even`, `for.odd` | as Scriban gives them |
| an item's or node's state | `s.state == "completed"` | the closed set `completed`, `skipped`, `not_run` ([Part 06](06-checks-deliverables-profile.md#62-item-states)) |
| whether a check passed | `check.coverage.value.pass` | `true` or `false` |

- The tests combine with `!`, `&&`, `||` and parentheses.
- A `state` is compared only with a literal from the closed set.
- **Testing anything else is refused:** a data value, an input, a node's text, a run fact. The
  one exception is a renderer's body, which may test its own `value` (§ 5.1). The
  sentence points to the header forms: a part (§ 7), a snippet's `if` (§ 6) or a macro's `if`
  (§ 8). The revisit of this rule is #1428 (Milestone 40).
- **Completeness:** an `if` that branches on a `state` must handle every state or end with an
  `else`
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1406#issuecomment-6881816481)).
  An `else` keeps the template safe when a state is added later.

```scriban
{{~ for s in node.sections.items ~}}
{{~ if s.state == "completed" ~}}
{{ s.value }}
{{~ else if s.state == "skipped" ~}}
{{~ else ~}}
_{{ s.reason }}_
{{~ end ~}}
{{~ end ~}}
```

### 2.6 Placement forms and whitespace

A **placement** is an output tag whose expression is a unit: `snippet.<id>.content`,
`part.<id>` or `macro.<id>` (each possibly qualified, `snippet.<folder>.<id>.content`, #1384).
A snippet's text is read as `.content`, like a node's or an input's (§ 6.3); it's a placement
because, like a part or a macro, it may be absent. Its text is entirely the
author's: no rule on how it starts or ends
([final](https://github.com/Consultologist/Consultologist-Blazor/issues/1412#issuecomment-6905945705)).

| Written | Result |
|---|---|
| `{{~ x }}` alone on its line, nothing after it | **allowed** (a block). When the unit is absent, an empty line remains. |
| `{{ x }}` anywhere inside a line, including against a word (`diabetes{{ x }}.`) | **allowed** (inline). Nothing is removed. |
| `{{ x }}` alone on its line | allowed: the indentation before it stays. The Editor writes `{{~ x }}` when inserting on an empty line. |
| `~}}` on a placement | refused: it removes the line's newline, joining two lines |
| `-` on a placement (`{{- x }}`, `{{ x -}}`) | refused: it removes blank lines between paragraphs |
| `{{~ x }}` inside a line | refused: it removes the space the author wrote before it |
| `{{~ x }}` with text after it on its line | refused: a block placement stands alone on its line |

Scriban's markers, as tested on 7.2.5: `~` removes spaces and tabs on its side, and on the right
also the one newline ending the line; `-` removes all whitespace on its side, blank lines
included ([tests](https://github.com/Consultologist/Consultologist-Blazor/issues/1412#issuecomment-6887624987)).

**Statements and ordinary outputs** (`if`, `for`, `else`, `end`, `{{ node.x.content }}`) may
use `~` on either side, as the renderer templates need for byte-exact output (§ 5.5).
**`-` is refused on every tag,** not only placements, because it removes blank lines no preview
explains ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6978508523)).

**The Editor** previews each placement with and without its unit, side by side. That's
information, not a warning.

## 3. What each template reads

### 3.1 The table

| Site | Reads | Never |
|---|---|---|
| prompt | `node.*`, `input.*`, `check.*`, `item`, `pass`, `param` (in functions), `snippet.*`, `part.*` (its own node's), `macro.*`, the `renderer.` pipe, `run.*` | `profile.*`, `deliverable.*` |
| `rendered` node | `node.*`, `input.*`, `check.*`, `item`, `pass`, `param`, `snippet.*`, the `renderer.` pipe, `run.*` | `profile.*`, `part.*`, `macro.*`, `deliverable.*` |
| renderer | its piped `value` only | everything else, the `renderer.` pipe included |
| snippet body | what the template that places it reads (§ 3.2) | what that template may not read; and, placed in a macro body, a part's text or a macro instruction, `macro.*` and `part.*` (the guard, § 6.3) |
| part text | what its node's prompt reads, `macro.*` (§ 8.3) and `snippet.*` (under the guard) included; no `if` of its own | `profile.*`, `part.*` |
| macro instruction | what the placing prompt reads, `snippet.*` (under the guard) included, except `macro.*` and `part.*`; its own marker (§ 8.4) | `profile.*` |
| macro body | `node.*`, `input.*`, `check.*`, `item`, `pass`, `param`, `snippet.*`, the `renderer.` pipe, `run.*`, **plus `profile.*`** | `macro.*`, `part.*`, `deliverable.*` |
| deliverable | `node.*`, `input.*`, `snippet.*`, `check.*` (not its own), `profile.*`, `run.*`, the `renderer.` pipe | `item`/`pass` outside its own `for`, `part.*`, `macro.*`, `deliverable.*` |

Sources: [the dialect](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6920252420),
[macros never place macros or parts](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6920620508),
[profile reads](https://github.com/Consultologist/Consultologist-Blazor/issues/1408#issuecomment-6904843222).
Decided with part 05 ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6978508523)): a part's text may place a macro, which is then offered
only when both the part's `if` and the macro's own `if` hold (§ 8.3); snippets may be placed in
a part's text and in a macro's instruction, under the guard (§ 6.3); a renderer's body pipes
into no renderer, so chaining happens only at a use site (§ 5.2) and renderers form no graph
of their own.

- **`run.*`** is the set `run.date`, `run.id`, `run.package`, `run.engine`, read under § 4's run
  spot rule, piped to built-ins or renderers ([Part 03](03-expressions.md) defines the values). Inside a function, whether
  `run.*` is read directly or only as a parameter is [Part 04](04-loops-and-functions.md)'s.
- **`profile.*`** is never sent to a model, never read by a condition, and inside a function
  arrives only as a parameter that reaches macro bodies
  ([Part 06](06-checks-deliverables-profile.md#10-the-profile)).
- **`check.*`** in a prompt or `rendered` node is a dependency edge like any read. A body `if`
  may test `check.x.value.pass` (§ 2.5).

### 3.2 Snippet and macro bodies are templates

- **They're rendered in the scope where they're placed:** per item inside a loop, per pass
  inside a `while`, and in the function's sealed scope inside a function.
- **Their reads count as the placing node's reads.** They become dependency edges and are
  recorded under that node.
- **A snippet's body must check in every place it's placed.** Where a read isn't available
  (`item` outside a loop, `profile.*` in a prompt), that placement is refused with a sentence
  naming both the snippet and the place.
- **Cycles are refused at publish:** a snippet or macro that, through its reads and
  placements, reaches itself. A snippet placed in a part's text or a macro's instruction places
  no `part.*` or `macro.*` at all (the guard, § 6.3).
- **A macro body is rendered before literal substitution.** The model's reply is never rendered
  (§ 8.6).

## 4. Run spots and the two hashes

**The rule:** an output expression that reads `run.*` reads nothing else, for example
`{{ run.date | date.to_string "%d %B %Y" }}`. Each such expression is a **run spot**
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6920425864)).
A body's read of a **run-dependent** value (a `computed` value or decision derived from `run.*`,
transitively, [Part 03](03-expressions.md)) is a run spot too, under the same rule
([extended](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6925165442)).

- **A run spot may pipe to built-ins and to renderers** (`{{ run.date | renderer.long_date }}`).
  The spot covers the whole pipeline: the marker stands for its final output, and each
  renderer's sub-row is marked run-dependent ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6978508523)).
- Run facts can't be tested in a body `if` (§ 2.5), can't feed a `for`, and can't be combined
  arithmetically (§ 2.1).

**Rendering, in three steps:**
1. Render the body with every read substituted **except the run spots**. Each spot becomes a
   marker naming its site: the literal text `{{run:<n>}}`, where `<n>` is the spot's 1-based
   position in the body's source order ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6978508523)). It's fixed text, so the hash below is
   defined to the byte.
2. Hash that text: the **run-neutral hash**. With the same inputs, package and libraries it's
   identical across runs, whatever the date or job.
3. Put each spot's output at the **position recorded while rendering** step 1, and hash the
   result: the **sent-text hash**, which stays the body's main hash. The engine never searches
   the text for `{{run:<n>}}`: a value that happens to hold those characters is left as it is
   ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)).

**Downstream text.** When a template reads a `rendered` or `computed` node, its run-neutral
render uses that node's run-neutral text (a `rendered` node) or treats the read as a run spot
(a run-dependent `computed` value). An agent's output has no neutral form: the model saw the
real values, so a template reading it is run-specific through that read.

**What a read of a run-dependent value does, by producer** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)):

| The value read | In the run-neutral render | Kept permanently in the record |
|---|---|---|
| a raw `run.*` fact (`run.date`) | a run spot | the spot's output |
| a run-dependent `computed` value | a run spot | its output only if it reads nothing but `run.*`; otherwise it follows 7-day retention (part 08) |
| a run-dependent `rendered` node | that node's run-neutral text, its own spots kept as spots | as for that node's spots |
| a run-dependent `agent` node's output | the output itself: the model saw the real values | nothing extra; it follows 7-day retention |
| a run-dependent flow node's `ret` or call result | as its `ret`'s producer, by this table | as its `ret`'s producer |

**A body with no run spot** has one hash. The record's `runNeutralHash` is written only when the
body has a run spot, and is `null` otherwise ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6978508523), [Part 08](08-record.md)).

**The advance warning.** The Editor and publish say when a package reads `run.*` or a
run-dependent value, naming each spot. The warning text is W-1 (§ Refusals). The record side
(each spot's output and hash, the run facts kept once per run) is in [Part 08](08-record.md).

## 5. Renderers

A renderer turns a value into text. It's a package template, versioned with the package and
embedded in `resolve.json` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1397#issuecomment-6877969751)).

### 5.1 The file

`renderers/<id>.md`, listed in `renderers/index.json` ([Part 01](01-files-and-scopes.md)).

| Member | Type | Null | Meaning |
|---|---|---|---|
| `label` | string | no | the display name |
| `kind` | `"rendered"` | no | the mechanism; the folder gives the role |
| `description` | array of strings (lines) | yes: none | for people only, never sent to a model |
| `input` | a format name, or an inline schema | no | the format it accepts: any format, including `string` and text formats |
| `format` | `"plaintext"`, `"markdown_block"` or `"markdown_inline"` | no | what it returns: always a text format ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1397#issuecomment-6878563370)) |

`description` is on every header ([Part 01](01-files-and-scopes.md)). **`input` is written like a
node's `format`:** a name, or an inline schema ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6978508523)).

The body is a non-empty template that reads **only `value`**, the piped value (§ 3.1). A
renderer is reproducible by construction: the same value always gives the same bytes. Its body
pipes into no renderer (R-N7); chaining happens at a use site (§ 5.2).

```
renderers/concept_bullets.md
---
{ "label": "Concepts as bullets", "kind": "rendered", "description": null,
  "input": "concept_list", "format": "markdown_block" }
---
{{~ if value.size == 0 ~}}
(none)
{{~ else ~}}
…
```

(The `if` above tests a list's size, which § 2.5 refuses in other bodies. **A renderer's body
may test its own `value` in an `if`** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6978508523)): a renderer decides nothing that's
recorded, since its whole output is a function of `value`, hashed as one sub-row. Without this,
the two built-in renderers can't be written.)

### 5.2 The call: a pipe to the root `renderer.`

- **The one call form is the pipe:** `{{ node.extract_patient_concepts.value | renderer.concept_bullets }}`.
  The call form (`renderer.concept_bullets(x)`) is refused.
- **The root `renderer.`** can't collide with Scriban's built-ins or with `node.`, `check.`,
  `input.`, `snippet.` and `macro.`.
- **`.value` is the input,** the typed object. Version 30's `node.x | concept_bullets` (the
  node) and `node.x.content | …` become `node.x.value | renderer.concept_bullets` (#1189).
- **Typed at publish:** the piped value's format must match the renderer's `input`. The
  result's format is the renderer's `format`.
- **Chaining is allowed where a template uses renderers**
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6978508523)): `{{ node.extract.value | renderer.concept_list | renderer.bullets }}`.
  Each step's output type is checked against the next renderer's `input` at publish, and each
  step is its own sub-row (`…/render:1`, `…/render:2`, § 5.5).
- **`run.*` may pipe to renderers** (`{{ run.date | renderer.long_date }}`): the run spot covers
  the whole pipeline (§ 4).
- **Allowed in:** prompts, `rendered` nodes, snippet bodies, part text, macro instructions,
  macro bodies and deliverables. **Not in renderer bodies:** a renderer's own body may not pipe
  into another renderer (R-N7).

### 5.3 Built-in renderers

`concept_bullets` and `concept_context` are written as templates
([drafts](https://github.com/Consultologist/Consultologist-Blazor/issues/1397#issuecomment-6877969751)).
The C# renderers (`ConceptRenderers`, `WorkflowConceptRenderers`) **stay in version 31 as the
byte-equivalence reference** and are removed in Milestone 40 (#1404,
[decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1397#issuecomment-6898250316)).

- **The proof:** a test renders each template and compares it byte for byte with the C# output,
  on the Editor's fixture and every edge case (empty, not SNOMED, inactive, with and without
  `support`). The pitfalls are booleans (`True`/`False`), line endings (no trailing newline) and
  `concept_context`'s `source` and blank `support`.
- **The built-in renderers are Scriban templates in the package-format registry**
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6978508523)): read-only, versioned and locked like the operations (in `requires`, and in
  `lock.json`'s `builtins`, [Part 07](07-compiled-documents.md)), and embedded in
  `resolve.json`. **Their ids are reserved:** a package renderer may not take one (R-N6,
  [Part 01](01-files-and-scopes.md)). Scoping between built-in, catalogue and package ids is
  #1469.
- **A loop's `.content`** keeps version 30's bytes (`## label`, then the text, joined by blank
  lines) as a standard rendering typed `markdown_block`
  ([#1406](https://github.com/Consultologist/Consultologist-Blazor/issues/1406)).

### 5.4 Media-type warnings

Each text format carries `contentMediaType`, and Markdown carries `x-markdown: block | inline`
([Part 02](02-types-and-inputs.md)). Publish warns (W-2) when a `markdown_block` renderer is
piped inside a line
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1397#issuecomment-6878274999)).

### 5.5 In the record and History

A renderer use is not a node: it isn't scheduled, waits on nothing and can't fail on its own.
It's a **sub-row of the node (or deliverable) that uses it**, one per pipe use, in source order,
per instance, **keyed by the use's position** (`node.identify_problem/render:1`)
([decided 2026-11-17](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6957680891)); inside a loop, the item path comes
first, then the render site (`node.sections.paragraphs.draft_paragraph:2/1/render:1`,
[decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1397#issuecomment-6959888097)).
Each holds the renderer's id, its template hash, the source read, the input hash and size, and
the output hash and length
([refined](https://github.com/Consultologist/Consultologist-Blazor/issues/1397#issuecomment-6877988508)).
A chain gives one sub-row per step, in pipe order. Every key is written with its root
([Part 01](01-files-and-scopes.md)).
The text itself follows the 7-day retention sweep. History shows the sub-rows under the node
when it's expanded. The diagram draws them inside the node's frame (#1398,
[Part 09](09-engine-and-pages.md)).

## 6. Snippets

A snippet is **input text: the model reads it**, and may use, paraphrase or ignore it
([#1409](https://github.com/Consultologist/Consultologist-Blazor/issues/1409)). Its body is a
template (§ 3.2).

### 6.1 A single snippet

`snippets/<id>.md`:

| Member | Type | Null | Meaning |
|---|---|---|---|
| `label` | string | no | the display name |
| `description` | array of strings | yes: none | for people only, never sent to a model |
| `if` | JSON Logic | yes: always | whether the snippet's text is placed ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1412#issuecomment-6887473994)) |
| `format` | `"plaintext"`, `"markdown_block"` or `"markdown_inline"` | no | the body's text format, so a deliverable's strict reads ([Part 06](06-checks-deliverables-profile.md#64-strict-formats)) can check it |

The body must be non-empty and suit `format`. **A snippet's header is these four members,** with
a text format ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910), [Part 01](01-files-and-scopes.md)). Version 30's snippet header
carries only `label`; the `if` comes from #1412's one contract for snippets, parts and macro
instructions, and `format` from #1406's strict reads. Data types for snippets are #1467.

### 6.2 Snippet folders

A snippet folder's `index.json` holds `description`, `items` (the ids, in order) and a
**`format`** (#1383, #1407): each item's header is its **value**, checked against that format,
and its body is its **content**. A header's `label` no longer names the item; the loop's
`itemLabel` does ([Part 04](04-loops-and-functions.md)).

```
snippets/standards/index.json
{ "description": [ "The nine standard sections of a consult note, in order." ],
  "items": [ "hpi", "pmh", "medications", "allergies", "social_history",
             "family_history", "exam", "investigations", "assessment_plan" ],
  "format": "standard_section" }

snippets/standards/hpi.md
---
{ "title": "History of Present Illness" }
---
Chronological prose narrative. Open with a one-sentence orientation to the patient …
```

- A folder is read by a loop (`"in": "snippet.standards"`), where `item.value.title` is the
  header and `item.content` the body, rendered in the loop's scope.
- **A folder item's `.content` is `markdown_block`**, whatever the folder's `format` ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1505)):
  the folder's `format` types the header (`item.value`, `snippet.<folder>.<id>.value`) and
  nothing else, and the body is `markdown_block`, as 30's standards bodies are. A
  `markdown_block` deliverable writes it as it is, never escaped, and a `plaintext` one refuses
  it ([Part 06 § 6.4](06-checks-deliverables-profile.md#64-strict-formats)).
- **A body passed out of its loop is its raw text, and must be plain** ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1505)). Read as
  `item.content` in its loop, a folder item's body is rendered there; a call's `with` or a
  computed node's `expression` that reads the folder item carries the body on unrendered, so
  each body it carries holds no tag and no raw block, or it is refused. This holds at every
  depth: the item and its `.content`, `snippet.<folder>.<id>`, a folder loop's `.items`, and,
  in a loop over such `.items` however many loops down, its `item` and every read along the
  chain to the folder item (`item.item` one loop down, `item.item.value.item` two, and so on).
  A condition only decides, so it may read a body that holds tags.
- **Folder items carry no `if`** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)): a folder item's header is data, so a
  folder's size is its number of files, and conditional inclusion of a folder item is an `if`
  branch in the loop body, as `example-conditional-sections` does with `applies`. Dynamic folder
  sizes are #1468.
- **A folder item's body may be empty** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)): its `item.content` is then `""`, and its header
  still carries the value. Revisiting this is #1484.

### 6.3 Placing a snippet

- **A snippet is read as `snippet.<id>.content`** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6978508523)), the same member rule as
  `input.<id>.content` and `node.<id>.content`: `{{~ snippet.<id>.content }}` alone on a line, or
  `{{ snippet.<id>.content }}` inside a line (§ 2.6). A bare `snippet.<id>` is refused (R-S3). A
  folder item outside a loop is `snippet.<folder>.<id>.content`. Version 30's 25 uses are
  already written this way and need no rewrite.
- **Its `if` is evaluated at each placement** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)), under that place's read rules (§ 3.1): a
  snippet placed in two prompts is decided twice, and its `if` must be readable at every place
  that places it (R-R3).
- When its `if` holds, its rendered body is spliced in. When it doesn't, nothing is, and the
  decision is recorded with its reason.
- **Placed in:** prompts, `rendered` nodes, other snippets, macro bodies, deliverables, **a
  part's text and a macro's instruction** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6978508523)).
- **The guard:** a snippet placed in a part's text or a macro's instruction may not place
  `part.*` or `macro.*`, directly or through a snippet it places (R-S4). Otherwise it reads what
  its host reads. This rules out a part inside a part and a macro inside an instruction.
  Narrowing the guard to refuse only an actual cycle is #1476 (Milestone 40).

## 7. Parts

A part is **short conditional prompt text that belongs to one agent node**
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1412#issuecomment-6887226097)).
Longer or shared conditional text is a snippet with an `if`.

### 7.1 The member

`parts`, in an agent node's header, required (#1394):

| Member | Type | Null | Meaning |
|---|---|---|---|
| `parts` | object keyed by part id | yes: none | the node's parts. `{}` also means none. |
| `parts.<id>.if` | JSON Logic | yes: always | whether the part's text is placed |
| `parts.<id>.text` | array of strings | no | the lines, joined with `\n`; a blank line is `""` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1412#issuecomment-6887344692)) |

- A part id follows the one id rule (`^[a-z][a-z0-9_]*`, at most 64 characters) and the reserved
  list ([Part 01](01-files-and-scopes.md)). The member `parts` and the root `part.<id>` keep their
  names.
- `text` is always an array, never a single string. No newline is added after the last line.
- The text may use presentation templating (§ 3.1), may place a macro (§ 8.3) and snippets
  (under the guard, § 6.3), but has no `if` and places no `part.*`.
- Parts are allowed on `kind: agent` nodes only.

### 7.2 Placing a part

The body places each part with `{{~ part.<id> }}` or `{{ part.<id> }}` (§ 2.6).
- A placement naming no declared part is refused (R-P1).
- A declared part the body never places is refused (R-P2).
- **Each part is placed exactly once** (R-P3, [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6978508523)): a part placed twice would
  record one decision for two texts.

```
nodes/standards/section_instructions.md
---
{ "kind": "agent", "producer": "generated", "label": "Applying section instructions", "description": null,
  "format": "plaintext", "reproducible": false, "modelDerived": true, "runDependent": false, "profileCarrying": false,
  "onInvalid": "fail", "failIfEmpty": null, "ordered": false,
  "parts": {
    "no_standard": {
      "if": { "blank": [ { "var": "item.content" } ] },
      "text": [ "No changes. Preserve the patient-updated draft's clinical content and",
                "produce polished final prose for this section." ] } },
  … }
---
Requested section:
{{ item.value.title }}

Section standard and section-specific instructions:
{{~ part.no_standard }}
{{ item.content }}
```

(Version 30's `test/general` tests `(item.content | string.strip) == ""` in a body `if`, which
version 31 refuses (§ 2.5). The test moves to the part's `if`, using the `blank` operation
([Part 03](03-expressions.md)), and is recorded. How the migration rewrites it is
[Part 10](10-migration-and-tools.md)'s.)

## 8. Macros

A macro is **output text: guaranteed exact, placed by the model.** The model decides only
**where** it goes. Whether it applies is decided before the prompt is sent, by its header `if`,
and recorded ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1412#issuecomment-6882114291)).

| | Snippet | Macro |
|---|---|---|
| Side | input: the model reads it | output: it ends up in the result |
| The model | may use, paraphrase or ignore it | only places its marker |
| The text | part of the prompt | inserted verbatim, never rewritten |

### 8.1 The file

`macros/<id>.md`, or `macros/<folder>/<id>.md`:

| Member | Type | Null | Meaning |
|---|---|---|---|
| `label` | string | no | the display name |
| `description` | array of strings | yes: none | for people only, never sent to a model |
| `if` | JSON Logic | yes: always triggered | whether the macro is offered |
| `format` | `"plaintext"`, `"markdown_block"` or `"markdown_inline"` | no | the body's text format ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1409#issuecomment-6887874963)) |
| `instruction` | array of strings | no | where to place it, joined with `\n` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1409#issuecomment-6887475028)) |

- `layout` and `at:` placements are gone (§ 10).
- The body is the macro's content: non-empty, a template (§ 3.1), checked against `format`.

**A macro folder's `index.json`** carries `description`, `items` and an **`if`**
(`null` for always). When a folder and a macro both have an `if`, **both must hold**, and the
record shows which was false. The folder is a namespace: `macro.<folder>.<id>`
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1412#issuecomment-6887873980)).

### 8.2 Whether: the header `if`

- The `if` may read anything a condition reads ([Part 03](03-expressions.md)), typically a
  **classifier node**: an ordinary `kind: agent` node (`producer: "generated"`) with the built-in
  format `judgement` (`{ answer: boolean, reason: plaintext }`), read as
  `node.<id>.value.answer`; the record and History show its `reason` beside the answer
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6982429387)).
- **It's evaluated in the scope where the macro is placed:** per item when it reads a node in
  the loop body, once when it reads a node outside. Inside a function it's the function's
  sealed scope, and the `if` may not name a `param`
  ([#1419](https://github.com/Consultologist/Consultologist-Blazor/issues/1412#issuecomment-6897801397)).
- **Per-run choices are optional boolean inputs** the `if` reads
  ([decided 2026-11-17](https://github.com/Consultologist/Consultologist-Blazor/issues/1412#issuecomment-6957666676)):
  a choice that defaults off is `{ "===": [ { "var": "input.x.value" }, true ] }`; one that
  defaults on is `{ "!==": [ { "var": "input.x.value" }, false ] }`, which holds while the input
  is unset. An explicit `default` is #1456 (Milestone 40).
- **A per-run choice input takes the macro's id and label** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466)), and is joined
  with `and` to any existing `if`. The Create page shows three states (unset, yes, no), and unset
  is sent as `null`. A migrated choice whose id is already an input's id gets the suffix
  `_macro` (`letrozole` → `letrozole_macro`), reported by the migration ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1447#issuecomment-6980651765)).
- One classifier per macro, per node, per item. A classifier answering several questions is
  #1416 (Milestone 40).

### 8.3 Where: placing the instruction

- The prompt places the macro with `{{~ macro.<id> }}` or `{{ macro.<id> }}` (§ 2.6).
- **Only triggered macros reach the prompt.** A triggered placement renders the instruction; an
  untriggered one renders nothing.
- The instruction says only where: never "if …, otherwise nothing".
- **Only `kind: agent` nodes place macros**: in the prompt, in one of the node's parts' text, or
  through a snippet placed in the prompt. A `rendered` or `computed` node, a deliverable, a macro
  instruction, a macro body, and a snippet placed in a part's text or a macro's instruction place
  none.
- **A part's text may place a macro** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6978508523)). The macro is then offered only when
  both the part's `if` and the macro's own `if` hold, and its row links both decisions (§ 8.8).
- **The node's `producer` is `generated` and its `format` a text format** (`plaintext`,
  `markdown_block`, `markdown_inline`) ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6978508523);
  [producer](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)). A marker has no place in an object reply.
- **Format match** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6978508523)): Markdown content into a `plaintext` node is refused
  (R-M5); a `markdown_block` macro is refused in a `markdown_inline` node; a `plaintext`
  macro in a Markdown node is escaped as plain text is in deliverables
  ([Part 06](06-checks-deliverables-profile.md#64-strict-formats)), and the content hash is
  taken after escaping.
- **Two nodes may place the same macro;** if both trigger it, the text appears twice. **A macro
  is placed once per template,** counting the text of the node's parts (R-M7,
  [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6978508523)). The same macro twice in one template is #1457 (Milestone 40).

### 8.4 The marker

**The marker is `macro.<id>.content`** (qualified: `macro.<folder>.<id>.content`). Each
instruction contains its own macro's marker **exactly once**, in one of two forms, and no other
macro's marker:

| Form in the instruction | What the model must write |
|---|---|
| `{{~ macro.<id>.content }}` alone on a line of the instruction | the marker alone on its own line (a block) |
| `{{ macro.<id>.content }}` inside a line | the marker inside a sentence (inline) |

When the instruction is rendered, **its marker renders as its own literal source text**
(`macro.<id>.content` between its braces), so the model sees exactly the marker to write. The
dialect treats the marker as literal inside an instruction ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6978508523)).

```
macros/letrozole_prescription_text.md
---
{ "label": "Letrozole prescription text", "description": null,
  "if": { "===": [ { "var": "node.prescribes_letrozole.value.answer" }, true ] },
  "format": "plaintext",
  "instruction": [
    "Place the following marker, alone on its own line, in the assessment and plan,",
    "after the medication discussion:",
    "{{~ macro.letrozole_prescription_text.content }}" ] }
---
Discussed side effects including: hot flashes, joint or muscle pain, fatigue, headache, nausea,
and bone thinning. Advised to monitor side effects.
```

The classifier is an ordinary node file, for example `nodes/standards/prescribes_letrozole.md`
(`kind: agent`, `producer: "generated"`, format `judgement`), and shows in the graph and the record like any node.

### 8.5 Order

**`ordered`, `true` or `false`, is a required member of every `kind: agent` node header**
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6978508523)). `true` requires the triggered markers in the reply in the order their
placements appear in the prompt; `false` accepts any order.

### 8.6 Processing the reply

The reply is **never rendered**. Rendering it would run whatever the model wrote between braces
(template injection), break legitimate braces and run code no checker saw. The engine replaces
exact marker strings literally
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1412#issuecomment-6888096075)).

**1. The check:**
- every triggered macro's marker appears **exactly once**, in the form its instruction asked
  for;
- no marker of an untriggered macro appears;
- a marker naming an unknown macro is an error;
- near-misses (`{{macro.x.content}}`, `{{ macro.X.content }}`) aren't markers: the triggered
  macro then counts as missing;
- with `ordered: true`, the markers appear in order.

**2. Invalid** (each goes to the node's `onInvalid`: `retry` asks again, `fail` fails the job
naming the macro):
- a marker missing, repeated, unknown, or out of order;
- `{{~ … }}` written inside a sentence;
- `{{ … }}` written alone on a line when the block form was asked for;
- any text other than spaces after a block marker on its line.

**3. The replacement:**

| Marker in the reply | What the engine does |
|---|---|
| `{{~ macro.x.content }}` alone on its line | removes the indentation before the marker, replaces the marker with the macro's rendered body, **removes any spaces after it**, and keeps the line's newline |
| `{{ macro.x.content }}` inside a line | replaces the marker exactly where it is |

Trailing spaces after a block marker are removed, never a failure: in Markdown two spaces make a
hard line break, and in plain text they'd still be in the hashed bytes.

**4. The body** is rendered by the engine, in the placing scope, **before** substitution
(§ 3.2). It's the package's own text, checked at publish. **A body that reads `profile.*`
makes the node's value profile-carrying** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660); part 03 § 4.6): a prompt, a part or a
condition that reads that value afterwards is refused at publish (R-R6); a deliverable may read it.

**A retry message never quotes the reply** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6978508523)): a `retry` sends fixed engine text
naming each macro and what was wrong, since the reply may hold patient text.

### 8.7 What publish checks

- **Each instruction:** its own marker exactly once, in an allowed form; no other macro's
  marker; no `macro.*` or `part.*` placement; snippets only under the guard (§ 6.3); no
  `profile.*`.
- **Each template:** a macro placed at most once, counting its parts' text.
- **Each body:** it suits `format` (no Markdown in a `plaintext` macro); no markers; no
  placement of `macro.*` or `part.*`; its reads within § 3.1's macro-body column; no cycles.

### 8.8 In the record

A **macro row per macro per node instance**
([decided 2026-11-17](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6957680891)),
a sub-row keyed `…/macro:<id>`, with its decision at `…/if:<site>`
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979794633)), for example `node.standards.assessment:7/macro:letrozole_prescription_text`
and `node.standards.assessment:7/if:macro.letrozole_prescription_text`:
- whether it was **offered**, linking to its `if` decision (and the folder's, and the part's when
  a part's text placed it) and to the classifier's row;
- whether it was **placed**: its marker found exactly once;
- its **content hash** (the rendered body);
- when the marker was missing, repeated or altered, the node's `onInvalid` outcome, on the
  node's row.

Under a loop the rows are per item, and History and the lineage (#1414) show them under each
item. The shape of the rows is [Part 08](08-record.md)'s.

## 9. Decisions recorded for snippets and parts

Every header `if` decision (a snippet's, a part's, a macro's, a macro folder's) is an entry in
the job's one `decisions` list ([Part 08](08-record.md), [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979794633)): its site, the printed
condition, its result, a hash of each value it read, whether it was run-dependent, and an
`expressionHash` of its canonical JSON Logic. Rows link to their decisions by id. Reasons read,
for example, *"skipped: input.kind.value = "table" is false"*; the
printer shows `=` and `≠` ([Part 03](03-expressions.md), [Part 08](08-record.md)). A placed unit's
text is recorded by hash under the node that placed it.

## 10. What changes from version 30

| At 30 | At 31 |
|---|---|
| Scriban conditions in bodies (`{{ if input.x == "y" }}`) decide what the model sees, unrecorded | a body `if` tests recorded facts only; decisions are parts, snippet `if`s and macro `if`s, recorded |
| any Scriban built-in the walker accepts, arithmetic, assignment | the dialect's closed list (§ 2) |
| the host's culture | the invariant culture |
| `concept_bullets`, `concept_context` are C#, piped the node: `node.x \| concept_bullets` | renderers are templates in `renderers/`, piped `.value`: `node.x.value \| renderer.concept_bullets` |
| a snippet header holds `label`, which names folder items | a single snippet has `label`, `description`, `if` and `format`; a folder item's header is typed data, with no `if`; `itemLabel` names items |
| `{{ snippet.x.content }}`, placed in prompts and deliverables | unchanged, now a placement (§ 2.6); also placed in parts' text and macro instructions, under the guard |
| `prose`, and the `agent_*` formats | the plain format of the same shape (`plaintext`, `markdown_block`, `markdown_inline`); the node's `producer` says a model wrote it ([Part 02](02-types-and-inputs.md#13-producer)) |
| a macro's `instruction` is one string, often "if …, otherwise nothing"; the prompt places `{{ macro.x.prompt }}` | `instruction` is lines saying only where, with the marker; `if` decides whether; `{{ macro.x }}` |
| `at: before`, `after`, `append`, item-level placements, deliverable macros | removed: fixed text is a snippet read in the deliverable template |
| optional at most once, required exactly once | triggered exactly once, untriggered never |
| prompt hashes vary with run facts and nothing says so | run spots, with a run-neutral hash beside the sent-text hash |

## Refusals

The wording below is the starting text. It follows the style rules in [index § 7](index.md#7-refusal-sentences) and is used as written in Milestone 39 and reviewed in #1483 (Milestone 40); every sentence is listed in [refusal-sentences.md](refusal-sentences.md).

`<…>` are placeholders. Sentences marked *(decided)* are quoted from the deciding comment; the
rest are this part's wording.

**The dialect**
- R-D1: *"<Subject> uses <construct> at line <n>; templates present text and may not use assignment, capture, func, include, import, with, wrap, while, break, continue, case, tablerow or arithmetic."*
- R-D2: *"<Subject> calls <built-in> at line <n>; the allowed built-ins are string.upcase, string.downcase, string.capitalize, string.strip, array.join, array.size and date.to_string."*
- R-D3: *"<Subject> calls truncate at line <n>; truncate cuts clinical text."*
- R-D4: *"<Subject> calls object.default at line <n>; choosing text is a decision: put it in a header condition."*
- R-D5: *"<Subject> calls date.to_string at line <n> without a pattern; write the pattern, for example "%d %B %Y"."*
- R-D6: *"<Subject> runs a for over <expr> at line <n>; a for runs over a list."*
- R-D7: *"<Subject> tests <expr> in a body if at line <n>; a body if tests only loop position, state and whether a check passed. Decide it in a header: a part, a snippet's if or a macro's if."*
- R-D8 *(decided)*: *"Deliverable 'consult' branches on part.state and handles completed and skipped, but not not_run: add it or an else."*, generalised as *"<Subject> branches on <expr> and handles <states>, but not <missing>: add it or an else."*
- R-D9: *"<Subject> compares <expr> with "<value>" at line <n>; a state is completed, skipped or not_run."*
- R-D10: *"<Subject> reads <path> at line <n>, which <format or root> doesn't have."*
- R-D11: *"<Subject> reads <path>[<n>] at line <n>, a position in a list; lists are read whole, with a for, or by identity through .items."*

**Limits**
- R-L1: *"<Subject> stopped rendering: the for at line <n> reached 1,000 iterations."*
- R-L2: *"<Subject> nests if and for <n> deep at line <m>; the limit is 15."*
- R-L3: *"<Subject> stopped rendering: its output passed 1 MiB."*
- R-L4: *"<Subject> stopped rendering: it took more than <limit> steps."*

**Placements**
- R-W1: *"<Subject> places <unit> with ~}} at line <n>; it removes the line's newline, joining two lines. Use {{~ <unit> }} alone on a line."*
- R-W2: *"<Subject> places <unit> with - at line <n>; it removes blank lines between paragraphs."*
- R-W3: *"<Subject> places {{~ <unit> }} inside a line at line <n>; it removes the space before it. Use {{ <unit> }}."*
- R-W4: *"<Subject> has text after {{~ <unit> }} at line <n>; a block placement stands alone on its line."*
- R-W5 (§ 2.6): *"<Subject> uses - at line <n>; it removes blank lines between paragraphs. Use ~."*

**Reads**
- R-R1: *"<Subject> reads <root> at line <n>; <site> reads <allowed roots>."*
- R-R2: *"Node '<id>' reads profile.<field>; the clinician's profile is never sent to a model. Read it in a deliverable or a macro body."*
- R-R3: *"Snippet '<id>' reads <path>, which isn't available where <subject> places it at line <n>."*
- R-R4: *"<Kind> '<id>' reaches itself: <chain>."*
- R-R6: *"<Subject> reads <path> at line <n>, which carries profile text through macro '<id>'; a prompt or a part doesn't read profile text. Read it in a deliverable."*
- R-R5: *"<Subject> reads <path> and run.<fact> in one output at line <n>; an output that reads a run fact reads nothing else."* (also for a run-dependent value: *"… reads <path> and the run-dependent <value> in one output …"*)

**Renderers**
- R-N1 *(decided; the prefix is new)*: *"<Subject> pipes <source> (<format>) into renderer.<id>, which renders <input format>."*, for example *"Node 'identify_problem' pipes node.plan.value (section_plan) into renderer.concept_bullets, which renders concept_list."*
- R-N2 *(adapted from the decided "a renderer returns string")*: *"Renderer '<id>' declares format <format>; a renderer returns plaintext, markdown_block or markdown_inline."*
- R-N3: *"Renderer '<id>' reads <root>; a renderer reads only the value piped into it."*
- R-N4: *"<Subject> calls renderer.<id> at line <n>; pipe into it: <value> | renderer.<id>."*
- R-N5: *"<Subject> pipes into renderer.<id>, which this package doesn't define."*
- R-N6: *"Renderer '<id>' takes the id of a built-in renderer."*
- R-N7: *"Renderer '<id>' pipes into renderer.<other> at line <n>; chain renderers where they're used, not inside a renderer."*
- R-N8: *"<Subject> pipes renderer.<id> (<format>) into renderer.<next>, which renders <input format>."*

**Snippets**
- R-S1: *"<Subject> places snippet.<id>, which this package doesn't define."*
- R-S2: *"Snippet '<folder>/<id>' has a header that doesn't match format <format>: <schema error>."*
- R-S3 (§ 6.3): *"<Subject> reads snippet.<id> at line <n>; read its text as snippet.<id>.content."*
- R-S4 (§ 6.3): *"Snippet '<id>' places <part.* or macro.*>, and <subject> places it in <a part's text | a macro's instruction>; a snippet placed there doesn't place parts or macros."*

**Parts**
- R-P1: *"Node '<id>' places part.<name>, which its header doesn't declare."*
- R-P2: *"Node '<id>' declares part '<name>' but never places it."*
- R-P3: *"Node '<id>' places part.<name> <n> times; place a part once."*
- R-P4: *"Node '<id>' declares parts, and only agent nodes have parts."*
- R-P5: *"Part '<name>' of node '<id>' uses <if or part.*>; a part's text only presents."*

**Macros**
- R-M1 *(decided)*: *"Macro '<id>' places macro.<other> in its body; a macro body is output text, written after the model replies. Put shared text in a snippet and place it in both."* (and for `part.*`: *"Macro '<id>' places part.<name> in its body; …"*, the rest unchanged)
- R-M2: *"Macro '<id>' places <macro.* or part.*> in its instruction; combine instructions by placing each macro in the prompt."*
- R-M3: *"Macro '<id>' has <n> markers in its instruction; it must hold {{~ macro.<id>.content }} or {{ macro.<id>.content }} exactly once."*
- R-M4: *"Macro '<id>' has the marker of macro '<other>' in its instruction."*
- R-M5: *"Node '<id>' places macro.<macro>, whose format is <format>; an <node format> node takes <allowed>."*
- R-M6: *"<Subject> places macro.<id>; only agent nodes place macros, in their prompt or a part's text."*
- R-M7: *"<Subject> places macro.<id> twice, counting its parts' text; place a macro once in a template."*
- R-M11: *"Node '<id>' places macro.<macro>, and returns <format>; macros are placed only in agent nodes returning a text format."*
- R-M8: *"Macro '<id>' has format <format>, and its body <reason, e.g. writes Markdown at line 2>."*
- R-M9: *"Macro '<id>' has a marker in its body; a body is the text that replaces the marker."*
- R-M10: *"Macro '<id>' reads param.<name> in its if; decide outside the function and pass the answer in, or classify inside it."*

**Invalid replies** (sent to `onInvalid`, recorded on the node's row)
- V-1: *"The reply has no marker for macro '<id>'."*
- V-2: *"The reply has the marker for macro '<id>' <n> times."*
- V-3: *"The reply has a marker for macro '<id>', which wasn't offered."*
- V-4: *"The reply has a marker for '<id>', which isn't a macro."*
- V-5: *"The reply writes the marker for macro '<id>' <inside a sentence | alone on a line>, but it was asked for <alone on a line | inside a sentence>."*
- V-6: *"The reply has text after the marker for macro '<id>' on its line."*
- V-7: *"The reply has the markers out of order: '<id>' before '<other>'."*

**Warnings** (publish and the Editor; not refusals)
- W-1: *"<Subject> reads <run.fact or run-dependent value> at line <n>. Its main hash differs between runs; runs are compared on the run-neutral hash."*
- W-2: *"<Subject> pipes renderer.<id>, a markdown_block, inside a line at line <n>."*

## Proposed in this part

Nothing. Every point is decided; refusal sentences are used as written and reviewed in #1483 ([refusal-sentences.md](refusal-sentences.md)).
