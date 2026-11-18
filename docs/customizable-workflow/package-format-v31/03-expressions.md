# 03. Expressions

This part defines the one language a version 31 package uses to **decide** (every header condition)
and to **compute** (a `kind: computed` node's `expression`): JSON Logic, written directly in the
header, with a fixed set of standard and custom operations, typed against the format dialect of
[part 02](02-types-and-inputs.md). It covers where expressions appear and what each site may read,
`run.*` reads and the two contagious flags (run-dependent and model-derived), the date
operations, null semantics, strict equality, the printer, and the evaluator. Issues: #1386 (the
language), #1400 (`matches`, `has_word`), with #1453, #1471, #1472 and #1478 (Milestone 40) as
context only. Scriban, the presentation language, is [part 05](05-templates-and-macros.md).
Part 03's Proposed points were [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6962982698)
on 2026-11-17, and the evaluator's points left to the build were [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1393#issuecomment-6984396609) on 2026-11-18.

## 1. The rule: JSON Logic decides and computes; Scriban presents

| Purpose | Language | Where |
|---|---|---|
| decide | JSON Logic | every header condition (§ 3) |
| compute | JSON Logic, plus the custom operations (`object` among them) | a `kind: computed` node's `expression` |
| present | Scriban (part 05) | prompt bodies, `rendered` bodies, renderers, deliverable templates, snippet and macro bodies |
| describe types | the JSON Schema dialect (part 02) | formats |

- **Authors write JSON Logic directly.** What is written in the header is what `resolve.json`
  stores and what the engine runs. There is no expression-string syntax in the spec. The
  converter (#1423, part 10) turns expressions into JSON Logic and back as a tool, not as the
  format ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6898726017)).
- **Decisions live in headers; bodies only present.** Whether a node runs, an item is included, a
  part or macro is used or a loop stops is a header condition, evaluated and recorded by the
  engine ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6882114622)).
- **`let` is gone.** One no-model kind, `computed`, holds one JSON Logic expression in its member
  `expression`; `op` and `with` are retired
  ([#1385](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6919282701),
  [member name](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6924329099)).

**What changes from 30.** At 30 a condition is a Scriban boolean expression walked by
`WorkflowConditionAt25` and evaluated through the template renderer, and a `let` is a Scriban
expression evaluated by `WorkflowLetAt30`. At 31 both are JSON Logic, checked by a new walker
over the dialect and evaluated by the evaluator of § 9. Packages at 30 and below keep their
load path until retired (#1450).

## 2. The written form

### 2.1 Shape

A JSON Logic **rule** is one of:

- a **literal**: a JSON string, number, `true`, `false` or `null`;
- an **array** of rules, which evaluates each element and yields the list (lists are native);
- an **operation**: a JSON object with exactly one member, whose name is the operation and whose
  value is its operands.

### 2.2 The canonical form

`resolve.json` holds JSON Logic "in one canonical form" (#1394). The canonical form is also the
only form an author may write, so what is written is byte-for-byte what is stored
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6962982698)):

- **`var` takes one string**, the path: `{ "var": "input.kind.value" }`. The array form and the
  default second operand are refused (fixed arity, #1394). An empty path (the whole data) is
  refused.
- **`object` takes one object**, a map from field names to rules (§ 5.4).
- **Every other operation takes an array**, even with one operand: `{ "!": [ … ] }`,
  `{ "size": [ { "var": "input.prior_notes.value" } ] }`.
- **Paths** are dot-separated segments: a root (§ 4), then member and field names, each matching
  `^[a-z_][a-z0-9_]*$` or a format's declared property name. Numeric segments (list indexes) are
  refused: a positional read of a list is the zip #1406 rules out; a loop's `.items` pairs by
  identity instead.

The serialisation in `resolve.json` (member order, number form, RFC 8785) is
[part 07](07-compiled-documents.md)'s.

### 2.3 Example

The `applies` branch of `example-conditional-sections`, migrated from
`item.id != "billing" || input.include_billing == true`:

```json
{ "or": [
    { "!==": [ { "var": "item.id" }, "billing" ] },
    { "===": [ { "var": "input.include_billing.value" }, true ] } ] }
```

Printed (§ 8): `item.id ≠ "billing" or input.include_billing.value = true`.

## 3. Where expressions appear

| Site | Member | Defined in | Must type as |
|---|---|---|---|
| an `if` flow node | `if` | [part 04](04-loops-and-functions.md) § 5 | condition |
| a `while` | `while` | part 04 § 3 | condition |
| a `for` or `while` | `until` (or `null`) | part 04 §§ 2–3 | condition |
| a check | `if` (or `null`) | [part 06](06-checks-deliverables-profile.md) | condition |
| a deliverable | `if` (or `null`) | part 06 | condition |
| a part (a node's `parts`) | `if` (or `null`) | part 05 | condition |
| a macro, and a macro folder | `if` (or `null`) | part 05 | condition |
| a production leaf (`agent`, `rendered`, `computed`, `call`) | `if` (or `null`) | part 01 | condition ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6959885064), see below) |
| a `computed` node | `expression` | part 01; § 5 here | the node's declared `format` |

- **A condition** must type as `boolean` (or `boolean | null`), or as the list `missing`
  returns (§ 5.1). It is tested by truthiness (§ 6.4).
- **A `computed` expression** must type as the node's declared `format`, checked through each
  operation's typed result (§ 7).
- **Not expressions:** a `for`'s source (`in`), a `case` subject and a `call`'s `with` arguments
  are **reference strings** (part 04), never JSON Logic expressions
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6962982698)).
  Each names one readable thing. A list, subject or argument that has to be computed is a
  `computed` node, which the reference then names: compute, then name. Author documentation for
  this pattern is #1473.
- **Snippet folder items have no `if`**
  ([decided in part 01](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)),
  so a folder's size is its number of files and there is no folder-item condition to read
  anything. Dynamic folder sizes are #1468 (Milestone 40).
- **A production leaf's own condition is spelled `if`** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6959885064),
  [#1386](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6959885428)). At 30 a node may carry a `when` gate (v18); at 31 it
  is `if`, as a folder item's `when` became `if` at 26, a required member that is a condition or
  `null`, so every header gate has one name. A `case` branch keeps `when` for its list of values
  (part 04 § 5.2). The migration rewrites a node's `when` to `if` (part 10).

## 4. What each site may read

### 4.1 The roots

| Root | Means | Typed by |
|---|---|---|
| `input.<id>` | a package input, read like a node: `.value` (the raw value, every input), `.content` (a text-format input's text), `.title` (the chosen `oneOf` arm's title, at any depth); a bare `input.<id>` is refused ([decided in part 02](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466)) | the input's `format` (part 02) |
| `node.<id>` | a visible node: `.value`, `.content`, `.state`, and per kind `.items`, `.passes`, `.stopped`, `.history` (part 04) | the node's `format` |
| `check.<id>` | a visible check: `.value` (a `check_result`), `.state`; a check loop's `.items` | part 06 |
| `item` | the innermost loop's item: `.id`, `.value`, `.parent` (part 04 § 2.6) | the loop source's element |
| `pass` | the enclosing `while`'s pass: `.number`, `.node.<id>`, `.previous`, `.parent`, `.history` (part 04 § 3.3) | as v30 § 3.2 |
| `param.<id>` | a function's parameter, inside a function only: `.value`, `.content` (part 04 § 7) | the parameter's `format` |
| `run.<fact>` | a run fact (§ 4.3) | § 4.3 |

**Never readable by JSON Logic:** `profile.*` (no condition and no computed expression reads the
profile, [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6924497801)),
`snippet.*`, `part.*`, `macro.*`, `renderer.*` and `deliverable.*`. A snippet folder's item data
is read through the loop that iterates it (`item.value.title`).

**`.title` may be compared** in conditions and computed expressions, as a `string`: publish
checks that the literal is one of the format's titles, and `.title` on a plain `enum` is refused.
The stored and recorded value is always the raw value; the record prints the condition with both
the raw value and the title (§ 10;
[decided in part 02](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466)).

Visibility (which `node.<id>` a site can see, alignment by path, reading into a loop's body) is
#1384's rule, in [part 01](01-files-and-scopes.md), and the same for JSON Logic as for templates.
Every `var` is a dependency edge, as every template read is, except reads of earlier passes
(`pass.previous…`, `pass.history`), as at 30.

### 4.2 Per site

| Site | May read | Refused |
|---|---|---|
| `if` flow node | everything visible at its place: `input`, `node`, `check`, `item`, `pass`, `param`, `run` | `profile` and the others of § 4.1 |
| `while` | its body's nodes by bare id (this pass), `pass`, and anything that runs before the loop; `item`, `param`, `run` | as above |
| `until` on a `for` | the item's body nodes by bare id, `item`, anything that runs before the loop; `pass`, `param`, `run` | as above |
| `until` on a `while` | as the `while` | as above |
| check `if` | as part 06 says; in a check loop, `item` is the `.items` element (`item.state`) | as above |
| deliverable `if` | `input`, a node's **`.value`** (data), `run` | `.content` (text), `check.*` (a possible cycle), `profile`, deliverables ([corrected](https://github.com/Consultologist/Consultologist-Blazor/issues/1406#issuecomment-6924497456)) |
| part `if` | what the node's prompt reads, data roots only | as above |
| macro `if`, macro folder `if` | evaluated **where the macro is placed**: what the placing node's prompt reads, data roots only | `param.*` (a shared macro must not depend on one function's parameter, [#1419](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6897801178)) |
| production leaf `if` (§ 3) | what a template at its place reads, data roots only | as above |
| `computed` `expression` | everything visible at its place: `input`, `node`, `check`, `item`, `pass`, `param`, `run` | as above |

**Inside a function** every site reads only `param.*` and the function's own nodes; `input.*`,
outer nodes, an outer `item` or `pass`, `run.*`, `check.*` and `profile.*` are refused
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6888201229);
part 04 § 7.4).

### 4.3 Run facts

JSON Logic may read the same `run.*` set as bodies (#1425): `run.date`, `run.id`,
`run.package`, `run.engine`
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6924497801)).

| Fact | Type | Value |
|---|---|---|
| `run.date` | `date` (`string`, `format: date`) | the job's start date in the account's time zone, an IANA name recorded beside it ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1406#issuecomment-6920512477)) |
| `run.id` | `string` | the job's id |
| `run.package` | `string` | `<name>@<version>` of the package version run ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6962982698)) |
| `run.engine` | `string` | the engine's release version, in the `v2026.11.5` form ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6962982698)) |

- **Recorded like any read:** the raw value of each run fact the package reads is recorded once
  per run ([part 08](08-record.md)).
- **No time zone, no job:** every job is refused at start until the account's time zone is set,
  whether or not the package reads `run.date` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1406#issuecomment-6958802620); part 09).

### 4.4 Run-dependence is contagious

([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6925165013))

- **A value is run-dependent** when its expression reads `run.*`, directly or through any value
  that is itself run-dependent.
- **A decision is run-dependent** when its condition reads `run.*` or a run-dependent value. The
  record marks it so, and History can say a branch differed because a run fact differed.
- **A decision doesn't pass the flag on** to what it chooses: the flags describe content only.
  A node that runs because a run-dependent `if` was true isn't run-dependent for that; the
  decision has its own row ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)).
- **The run-neutral hash of a run-dependent computed value** covers its recipe (the expression
  and its other reads), not the value, so two runs with the same inputs share it even when the
  dates differ.
- **A body's read of a run-dependent value is a run spot** (#1425, part 05): a marker in the
  run-neutral render, filled afterwards at the position recorded while rendering (part 05 § 4).
- **The advance warning** (Editor and publish) names every run-dependent value and decision,
  not only direct `run.*` reads (§ 11.2).

**How far it spreads**
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6962982698)).
Run-dependence passes through everything a value is made from:

- `computed` and `rendered` nodes that read a run-dependent value;
- `agent` nodes whose prompt reads `run.*` or a run-dependent value;
- a `call`'s result when any argument is run-dependent (the parameter bound to it is
  run-dependent inside the function);
- a flow node whose `ret` is run-dependent;
- a loop's items when its source is.

It never passes through a condition: a branch's `if`, a `case` subject or a `while` condition
decides which content exists, not what it is made from ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)).

It is worked out from `resolve.json`'s edges before any run, so the warning can name each value
before a job starts. Each row records it as `runDependent` (part 08). **Every node header
writes it too, as `runDependent: true | false`, checked against the derived value** like
`reproducible`; a mismatch is refused ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1478#issuecomment-6982835861)). Requiring a run-dependent value in a read is #1478
(Milestone 40).

### 4.5 Model-derived is contagious

([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6962982698),
[sources](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6963556118))

**Model-derived** is a second contagious flag, beside run-dependence and worked out the same
way. It says a value came from a model's output somewhere upstream.

- **Where it starts:**
  - the output of every node whose `producer` isn't `computed` (part 02 § 1.3), so every
    `agent` node's output is model-derived (the reverse doesn't hold: a `computed` value can be
    model-derived too);
  - an input element from a previous run's deliverable (#874), whose record links to the job it
    came from (part 04 § 4).

  Typed text and uploaded documents start clean. Other machine sources (OCR, transcription) are
  producers reserved for Milestone 40 (`extracted`, #1438; part 02 § 1.3).
- **How it spreads:** exactly as run-dependence does (§ 4.4): through `computed` and `rendered`
  nodes, `agent` prompts, `call` arguments and results, a flow node's `ret`, and a loop's items.
  A decision whose condition reads a model-derived value is model-derived; as with
  run-dependence, the decision doesn't pass the flag on to what it chooses ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)).
- **Formats are shape only.** `lower`, `trim` and any other `computed` expression return their
  operand's format unchanged, so `trim` of an agent's `plaintext` is `plaintext`; the node's
  producer is `computed` and its `reproducible` stays `true`. The value keeps its lineage through
  the flag, never through its format
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)).
- **It is recorded and shown:** each row carries `modelDerived` (part 08), and History, the
  diagram and the Editor show it: *"computed from model text: node.draft"*.
- **It is worked out, and written on every node header:** the resolver computes it from
  `resolve.json`'s edges before any run, and each header declares `modelDerived: true | false`,
  checked against that value; a mismatch is refused ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1478#issuecomment-6982835861)). It stays a node member, never a
  format's. Requiring a model-derived value in a read is #1478 (Milestone 40).

Example, fictional: a `computed` node that checks whether a model's draft names a drug:

```json
{ "in": [ "letrozole", { "lower": [ { "var": "node.draft.content" } ] } ] }
```

It returns `boolean`, its producer is `computed`, it is reproducible, and it is model-derived,
because `node.draft` is an `agent` node's output: its header writes `modelDerived: true` and
`runDependent: false`. A macro's `if` that reads it is a model-derived
decision.

### 4.6 Profile-carrying is contagious

([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660))

**Profile-carrying** is a third contagious flag, beside run-dependence and the model-derived
flag, worked out the same way from `resolve.json`'s edges and describing content only.

- **Where it starts:** any value whose text had a profile-reading macro substituted into it
  (part 05 § 8). No JSON Logic expression reads `profile.*` (§ 4.1), so it starts only in text.
- **How it spreads:** as the other two do (§ 4.4): through `computed` and `rendered` nodes,
  `agent` prompts, `call` arguments and results, a flow node's `ret`, and a loop's items. A
  condition doesn't pass it on.
- **Where it may be read:** deliverables may read a profile-carrying value. A prompt, a part or
  a condition that reads one is refused at publish (§ 11.1), so a clinician's signature or
  credentials never reach a model or decide a branch.
- **It is written and recorded:** every node header writes `profileCarrying: true | false`,
  checked against the derived value like `runDependent`; a mismatch is refused ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)).
  Each row carries `profileCarrying` (part 08), and a row that is
  profile-carrying follows the profile's retention.

## 5. The operations

### 5.1 Standard JSON Logic operations

([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6962982698))

The allowed standard operations are `var`, `missing`; `if` (with an else); `===`, `!==`, and
`<`, `<=`, `>`, `>=` with two operands; `and`, `or`, `!`, `in`, `cat`; `+`, `-`, `*`, `/`, `%`,
`min`, `max`. Every operation not in this table or §§ 5.2–5.5 is refused at publish (§ 11.1).
Reviewing the whole set is #1471 (Milestone 40).

"data" is `string`, `number`, `boolean` and `date`, plus `null` where the operand's type is
nullable; "text" is any text format (`plaintext`, `markdown_block`, `markdown_inline`).

| Operation | Operands | Operand types | Result | Null behaviour | Notes |
|---|---|---|---|---|---|
| `var` | one path string | n/a | the path's type | absent or null reads as `null` | § 2.2 |
| `missing` | one array of literal path strings | paths of the site's roots | `list<string>` (the absent paths) | a path is missing when its value is `null`, absent or `""` (as json-logic-js does, so an unsupplied text input counts as missing) | tested by truthiness (empty list is false) |
| `if` | 3 or more, odd count: `[c1, a1, c2, a2, …, else]` | conditions, then branches of one type | the branches' type | a `null` condition is false | lazy; an even count (no else) is refused, since every member is explicit (#1394) |
| `and`, `or` | 2 or more | `boolean` (nullable allowed) | `boolean` | `null` is false | short-circuits; standard JSON Logic returns an operand, which is a boolean here because operands are typed boolean |
| `!` | 1 | `boolean` (nullable allowed) | `boolean` | `!null` is `true` | refused on text, numbers, lists and objects (not a blank test, [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6878721006)) |
| `===`, `!==` | 2 | two data values of one type, or either `null` | `boolean` | `null` is compared as a value: `null === null` is true | refused on text, lists and objects; an enum literal must be a value of the enum |
| `<`, `>`, `<=`, `>=` | exactly 2 | two `number`s, or two `date`s | `boolean` | **false** when either operand is `null` (no coercion to 0) | dates compare chronologically; the 3-operand "between" form is refused (one spelling: `and` of two comparisons) |
| `+`, `*` | 2 or more | `number` | `number` | `null` if any operand is `null` | strings are never coerced |
| `-` | 1 (negation) or 2 | `number` | `number` | `null` if any operand is `null` | |
| `/`, `%` | 2 | `number` | `number` | `null` if any operand is `null`; division or remainder by zero gives `null` | |
| `min`, `max` | 1 or more | `number` | `number` | `null` if any operand is `null` | part of arithmetic |
| `in` (list) | 2: `[needle, list]` | data needle; a `list` of the same data type | `boolean` | `null` needle: true only if the list holds `null`; `null` list: false | membership: `input.note_type.value in ["consult", "follow_up"]` |
| `in` (text) | 2: `[needle, haystack]` | a `string` needle; a text or `string` haystack | `boolean` | either `null`: false | exact, case-sensitive substring search (ordinal); on Markdown it searches the source ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6878673101)) |
| `cat` | 2 or more | `string`, `number`, plain-text formats | `string` when every operand is `string`; `plaintext` otherwise | a `null` operand contributes `""` (json-logic-js writes `"null"`) | Markdown operands refused (convert with `plain` first, so one result has one media type); numbers in the invariant culture |

**Refused standard operations** (§ 11.1): `==` and `!=` (strict equality only,
[decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6957665806));
`substr` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6878721006));
`!!` (with `!` limited to booleans it only repeats its operand) and `?:` (a second spelling of
`if`); `missing_some`, `merge` and `log`; `map`, `filter`, `reduce`, `all`, `some` and `none`
(they change what `var` names inside them, which the checker's typing of § 7 doesn't cover; none
is used by today's packages); three-operand comparisons; and Corvus's non-standard extras
(`asDouble`, `asLong`, …)
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6962982698)).

### 5.2 Custom text operations

Every custom operation has a **fixed arity with no optional operands** (#1394), runs in the
**invariant culture** ([#1425](https://github.com/Consultologist/Consultologist-Blazor/issues/1425#issuecomment-6924903846)),
and is **versioned** with a semantic version where any change to output bytes is a major
version, pinned by golden tests and listed in `lock.json` (#1390, part 07). All are version
`1.0.0` at 31.

"Text-like" below means a text format or `string`. Characters are **Unicode scalar values**
(code points), never UTF-16 units. Whitespace is Unicode `White_Space`.

([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6962982698):
definitions and null behaviour)

| Operation | Signature | Result | Meaning | Null behaviour |
|---|---|---|---|---|
| `size` | `size(x)` | `number` | text-like: the count of code points; a list: its element count | `null` |
| `blank` | `blank(x)` | `boolean` | true if `x` is empty or only whitespace | `true` |
| `words` | `words(x)` | `number` | the count of maximal runs of non-whitespace characters | `null` |
| `lines` | `lines(x)` | `number` | `0` for `""`; otherwise 1 plus the count of line breaks (`\r\n`, `\n`, `\r`), not counting one at the very end | `null` |
| `lower` | `lower(x)` | text-like (see below) | lower-cased by the invariant culture's simple case mapping | `null` |
| `trim` | `trim(x)` | text-like (see below) | leading and trailing whitespace removed | `null` |
| `starts_with` | `starts_with(x, prefix)` | `boolean` | ordinal, case-sensitive prefix test; on Markdown, of the source | `false` if either is `null` |
| `ends_with` | `ends_with(x, suffix)` | `boolean` | ordinal, case-sensitive suffix test | `false` if either is `null` |
| `plain` | `plain(x)` | `plaintext` | Markdown converted to plain text by the same conversion as plain-text output (#1397; Markdig with the listed extensions, [#1385](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6924903111)); a plain-text operand is returned unchanged | `null` |
| `matches` | `matches(x, pattern)` | `boolean` | § 5.3 | `false` if `x` is `null` |
| `has_word` | `has_word(x, word)` | `boolean` | § 5.3 | `false` if `x` is `null` |

- **`lower` and `trim` return their operand's format unchanged**
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6962982698),
  [pure shape](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)): `plaintext` stays `plaintext`, `markdown_block` stays `markdown_block`,
  `markdown_inline` stays `markdown_inline`, `string` stays `string`. A computed node that trims
  an agent's text has producer `computed`, and its value stays **model-derived** (§ 4.5).
- **Case-insensitive search** is written by lowering the haystack:
  `{ "in": [ "letrozole", { "lower": [ { "var": "node.draft.content" } ] } ] }`. `in` stays
  standard.
- **Text searched as read** is written with `plain`:
  `"letrozole" in plain(input.consult_draft.content)`.

### 5.3 `matches` and `has_word`

([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1400#issuecomment-6905611896),
[verified](https://github.com/Consultologist/Consultologist-Blazor/issues/1400#issuecomment-6957667807))

**`matches(x, pattern)`**
- **Dialect: I-Regexp (RFC 9485), with search semantics:** true if the pattern occurs
  **anywhere** in `x` (JSONPath's `search()`, not its whole-string `match()`).
- **I-Regexp has no `\b`, no `\d`/`\w`/`\s`, and no `^`/`$` anchors.** A start or end is tested
  with `starts_with`/`ends_with`; a whole word with `has_word`.
- **`pattern` is a literal string** in the package, never computed, checked against RFC 9485's
  grammar at publish, at most **200** characters long (counted in code points,
  [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6962982698)).
- **No flags:** always case-sensitive. Case-insensitive matching lowers the text and writes the
  pattern in lower case: `matches(lower(x), "letrozole|femara")`.
- **Typing:** `x` is a text format or `string` data. On Markdown it searches the source.
- **Engine:** the I-Regexp pattern is translated to .NET per RFC 9485 § 5 (for example `.`
  becomes `[^\n\r]`) and run with `RegexOptions.NonBacktracking | RegexOptions.CultureInvariant`.
- **`matches` doesn't depend on the clock** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)): there is no wall-clock time-out, so a
  replay gives the same decision. Each pattern is compiled at publish with the engine's exact
  options; a counted repetition (`{n,m}`) is at most **100**; the text searched is at most
  **1 MiB** (longer text fails the node that owns the condition, § 11.3). .NET's non-backtracking
  matching is linear in the text, so these bounds bound the work. Decision times come from the
  orchestration's replay-safe clock. (.NET matches UTF-16
  units, so the conformance suite carries vectors with characters outside the Basic
  Multilingual Plane to prove the translation counts code points.)
- **The record** keeps the pattern (package text, part of the printed condition) and the true or
  false result. **The matched text is never recorded.**

**`has_word(x, word)`**
- True if `word` occurs in `x` (ordinal, case-sensitive) with a **word boundary** on each side:
  the character before the occurrence, if any, and the character after it, if any, are not word
  characters. Word characters are .NET's: Unicode letters (`L*`), marks (`M*`), decimal digits
  (`Nd`) and connector punctuation (`Pc`, which includes `_`)
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6962982698)).
  `has_word(input.draft.content, "pain")` is false for "painful" and "Spain".
- **`word` is a literal, non-blank string,** checked at publish like a pattern, so it is package
  text in the record. It may contain spaces (`"chest pain"`).
- **Typing** as `matches`.
- A broader `has_word` is #1472 (Milestone 40).

### 5.4 Custom structural and check operations

| Operation | Signature | Operand types | Result | Null behaviour |
|---|---|---|---|---|
| `object` | `{ "object": { "<field>": rule, … } }` | each field's rule typed against the target format's property | an object | a field's rule may yield `null` only where the property is nullable (checked at publish) |
| `terms_subset` | `terms_subset(of, in, failWith)` | `concept_list`, `concept_list`, literal `string` | `check_result` | a `null` list reads as empty, as today (`ConsultCheckExecutor.TermsSubset`, [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6962982698)) |
| `all_pass` | `all_pass(list, failWith)` | a check loop's `.value` or `.items`, literal `string` | `check_result` | elements that did not complete are ignored; a `null` or empty list (nothing judged) **fails**, with `failWith` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1396#issuecomment-6958687810); revisited in #1461) |
| `count_pass` | `count_pass(list, min, failWith)` | as `all_pass`; `min` a non-negative integer **literal** ([decided in part 06](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6978700251)) | `check_result` | counts passed elements among those that completed |

**Check operations judge only `completed` items, and always return a `check_result`, never
`null`** ([decided in part 06](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6978700251)).
The `check_result` shape (`pass`, `message`, `uncovered`, `untested`, `passed`, `total`, every
member present) is part 06's.

([signatures](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6919283116),
[object](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6898320858),
[aggregates](https://github.com/Consultologist/Consultologist-Blazor/issues/1396#issuecomment-6919284053))

- **`object` builds objects.** Lists are native arrays; a JSON object in a rule is read as an
  operation, so `object` is the one way to build one. Its operand is one map; each field may use
  any allowed operation, including a nested `object`. Fields are checked at publish against the
  computed node's `format`, field by field: every property present (every property is in
  `required`, part 02 § 3.3; a nullable one may be `null`), no other field.
- **`failWith` is a literal string,** checked at publish: a failed check speaks the package's
  own sentence, never a computed one.
- **`terms_subset`** passes when every codable concept in `of` (an active SNOMED CT concept with
  an id) has the same concept id in `in`; uncovered and untested terms are reported as today.
  The `check_result` shape is part 06's.
- **The `list` operand of `all_pass` and `count_pass` is a check loop's `.value` or its
  `.items`;** both are accepted
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)), and part 06's rule that check operations judge only `completed` items reads
  the same either way. A `.value` is
  `list<check_result>` of the items that completed (part 04 § 2.5); with `.items`, each
  element's `value` is used and an element whose `state` is not `completed` is ignored. Ignoring
  unrun items lets a check loop's `if` skip sections that weren't produced without failing the
  aggregate.

Migrated check example (part 06 owns checks; shown for the expression):

```json
{ "kind": "computed", "producer": "computed", "modelDerived": true, "runDependent": false, "profileCarrying": false,
  "format": "check_result",
  "expression": { "terms_subset": [ { "var": "node.extract_input_terms.value" },
                                    { "var": "node.extract_note_terms.value" },
                                    "The note does not cover every clinical term found in the referral." ] } }
```

### 5.5 Date operations

([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6925346993))

All take and give `date` values (`string` with `format: date`, `YYYY-MM-DD`, no time of day),
in the proleptic Gregorian calendar, with no time zone arithmetic (`run.date` is already a date
in the account's zone).

| Operation | Signature | Result | Meaning | Null behaviour |
|---|---|---|---|---|
| `days_between` | `days_between(a, b)` | `number` (integer) | whole days from `a` to `b`; negative if `b` is earlier | `null` if either is `null` |
| `years_between` | `years_between(a, b)` | `number` (integer) | **completed** years from `a` to `b`: a year counts once its anniversary is reached; negative if `b` is earlier (the same rule mirrored) | `null` if either is `null` |
| `add_days` | `add_days(date, n)` | `date` | `date` plus `n` days | `null` if either is `null` |

([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6962982698))

- **29 February:** in a year with no 29 February, an anniversary of 29 February is reached on
  **1 March**. Someone born on 2008-02-29 is 0 on 2009-02-28 and 1 on 2009-03-01.
- **`n` in `add_days`:** a `number`; a non-integer, or a result outside years 0001–9999, gives
  `null`.
- **A malformed date at run time** gives `null`. It can only arise from data that escaped
  validation, since date inputs and date fields are validated against `format: date`.
- **Comparing dates** uses `<`, `>`, `<=`, `>=` and `===` on two `date`s (§ 5.1).

Example, fictional: *a reminder about bone density screening, for patients 65 or older at the
date of the run.*

```json
{ ">=": [ { "years_between": [ { "var": "input.date_of_birth.value" }, { "var": "run.date" } ] }, 65 ] }
```

Printed: `years_between(input.date_of_birth.value, run.date) ≥ 65`. It reads `run.date`, so the
decision is run-dependent (§ 4.4).

## 6. Values, absence and null

### 6.1 What a read gives

| Read | Gives |
|---|---|
| `input.<id>.value` (or `.content`, `.title`) of a supplied input | its raw value (its text, the chosen arm's title), typed by its format |
| an unsupplied or blank optional input | `null` for `.value`, `.content` and `.title`, whatever its format; a template renders it as nothing (part 02 § 8's table; [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)) |
| `node.<id>.value` of a node that completed | its value |
| `node.<id>.value`, `.content` of a node that did not complete (skipped, not run) | `null`; `.state` says why ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6962982698)) |
| `node.<id>.state` | `"completed"`, `"skipped"` or `"not_run"` |
| a nullable field holding `null` | `null` |
| `pass.previous…` on the first pass | `null` |

### 6.2 The null rules

Milestone 39 implements **runtime semantics**: nullable operands are not refused at publish, and
`null` behaves as each operation's row in § 5 says
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6925346993)).
In summary:

- **comparisons** (`<`, `>`, `<=`, `>=`) with a `null` operand are **false**; `null` is never
  coerced to `0`;
- **`===` and `!==` compare `null` as a value;**
- **arithmetic and the date operations give `null`** when any operand is `null`;
- **the text operations:** `size`, `words`, `lines`, `lower`, `trim` and `plain` give `null`;
  `blank` gives `true`; `starts_with`, `ends_with`, `matches` and `has_word` give `false`;
- **`cat`** reads `null` as `""`; **`missing`** counts `null` and `""` as missing;
- **truthiness:** `null` is false (§ 6.4);
- **evaluation never throws;** division by zero gives `null`.

**The pitfall stated for authors:** under these rules `{ "!": [ { ">": [ { "var": "input.age.value" }, 65 ] } ] }`
is **true** when the age is missing: "not over 65" includes "unknown". An author who means
"known and not over 65" writes the `null` test:
`{ "and": [ { "!==": [ { "var": "input.age.value" }, null ] }, { "<=": [ { "var": "input.age.value" }, 65 ] } ] }`.

Revisiting the rule (required guards, or a library's default behaviour) is #1453, Milestone 40.
It doesn't change Milestone 39.

### 6.3 Each operation's null behaviour is a conformance vector

The conformance suite (part 10) carries a vector for every row of §§ 5.1–5.5 with a `null`
operand, and for every absence case of § 6.1.

### 6.4 Truthiness

A condition's result is tested by truthiness: `false`, `null` and the empty list are false;
`true` and a non-empty list are true. Because conditions must type as `boolean` or as `missing`'s
list (§ 3), the other cases of JSON Logic's truthiness (`0`, `""`, objects) never reach a
decision.

## 7. Typing

The checker types every rule against the dialect (part 02), reading formats as they are,
`$ref`s and `anyOf` included, as v25's walker typed Scriban conditions.

- **Data and text are separate types**
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6878621423)):
  - **data** (`string`, `number`, `boolean`, `date`) supports `===`, `!==`, `in` (membership),
    and orderings on numbers and dates;
  - **text** supports `blank`, `size`, `words`, `lines`, `in` (search), `starts_with`,
    `ends_with`, `matches`, `has_word`, `lower`, `trim`, `plain` and `cat` (plain text only).
- **Refused on text:** `===`/`!==` against a literal or another text; `<`, `>`, `<=`, `>=`;
  `substr`; `!` as a blank test.
- **Numbers** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)) are IEEE doubles. An operation whose result isn't finite (an overflow,
  `NaN`) gives `null`. `integer` is a kind of `number`: an `integer` value may go where a
  `number` is expected, not the reverse. A built-in format name equals its schema under type
  equality (part 02).
- **A nested field is data unless it `$ref`s a text format** (#1385): `verdict.verdict` (a
  `string` enum) is data; `verdict.feedback` (`$ref: plaintext`) is text.
- **Enums:** a literal compared with an enum-typed value (a `string` with `enum`, or `oneOf` of
  `const`) must be one of its values; `in` against a literal list checks each element. A literal
  compared with a `.title` must be one of the `oneOf`'s titles; `.title` on a plain `enum` is
  refused (part 02).
- **Unions:** a read of `.value` is allowed only if every member of the union has a value;
  `.value.<field>` only if every member declares that field compatibly (#1385 decision 5).
- **`if`:** the branches must have one type. A `null` branch makes the result nullable.
- **`object`:** checked field by field against the computed node's format.
- **A computed node's result** must match its `format` (type equality, part 02). Its producer is
  `computed`, whatever it reads; where its value came from is the model-derived flag's job
  (§ 4.5), not its format's.
- **Reads must exist** at the site, with the visibility rules of part 01 and the per-site rules
  of § 4.

## 8. The printer

**One printer** turns JSON Logic into text for the Editor, History, the record's reason
sentences, the validator's sentences and the diagram's labels
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6957665806)).
It writes ordinary infix, functions as `name(args)`, and strict equality as **`=`** and **`≠`**.
The rest of its rules were
[decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6962982698)
on 2026-11-17: `and`, `or` and `not` as words; `≤` and `≥`; standard precedence, with
parentheses only where needed, so the printed text parses back to exactly the tree written.

| Rule | Printed |
|---|---|
| `{ "var": "a.b" }` | `a.b` |
| a string literal | JSON text: double quotes, JSON escapes (`"table"`) |
| a number | its RFC 8785 form (`65`, `0.5`) |
| `true`, `false`, `null` | `true`, `false`, `null` |
| an array | `[a, b, c]` |
| `===`, `!==` | `a = b`, `a ≠ b` |
| `<`, `>`, `<=`, `>=` | `a < b`, `a > b`, `a ≤ b`, `a ≥ b` |
| `in` | `a in b` |
| `and`, `or` | `a and b and c`, `a or b` |
| `!` | `not a` |
| `+`, `-`, `*`, `/`, `%` | infix; one-operand `-` as `-a` |
| `if` | `if c then a else b`; more pairs as `if c1 then a else if c2 then b else d` |
| `object` | `{ title: a, content: b }`, fields in written order |
| every other operation | `name(a, b)`: `size(input.prior_notes.value)`, `missing(["input.age.value"])` |

- **Precedence**, tightest first: function forms and literals; `not` and unary `-`;
  `*`, `/`, `%`; `+`, `-`; the comparisons and `in`; `and`; `or`; `if … then … else`.
- **Parentheses** only where needed to keep the tree: a child operation is parenthesised when its
  precedence is lower than **or equal to** its parent's (an `and` inside an `and` prints
  `(a and b) and c`, since a flat `and` of three is a different tree), so the printed text maps
  back to exactly one tree.
- **Spacing:** one space either side of an infix operator, `, ` between arguments, none inside
  brackets.
- **Byte-stable and versioned:** printed text is recorded (reasons, decisions), so the printer is
  versioned like a custom operation (any change to its output is a major version) and locked in
  `lock.json`'s `builtins` (part 07). The converter (#1423) parses its output back to the
  identical tree, and the conformance suite tests the round trip.
- **`.title` reads** print as written (`input.specialty.title = "Medical oncology"`); the record
  shows the raw value beside the title (§ 10).

**Reason sentences** are built from the printed condition
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6957680891)):
*"skipped: input.kind.value = "table" is false"*.

## 9. The evaluator

- **The evaluator is `Corvus.Text.Json.JsonLogic` (Apache-2.0)**, pinned exactly and listed in
  `lock.json`
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6957665806)).
  The research found version 5.8.11; the build pins the version current when it starts.
- **`requires.evaluator` names our evaluator's version,** not Corvus's: packages depend on our
  behaviour (the custom operations, the null rules, `===`/`!==` only), with Corvus underneath
  ([decided in part 07](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979256096)).
  Moving towards standard components, possibly including the evaluator, is #1477
  (Milestone 40).
- **Behind our own interface** in `Consultologist.PackageFormat`
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1393#issuecomment-6984396609)): `IJsonLogicEvaluator`, with one implementation, `CorvusJsonLogicEvaluator`. The engine, the
  validator's probe, the Editor and the in-package tests (#1449) all evaluate through it.
- **Our custom operations** (§§ 5.2–5.5) are registered through Corvus's `IOperatorCompiler`.
- **Our null rules override Corvus's defaults** wherever they differ, by registering our own
  compilers for the affected built-ins (custom operators are checked before built-ins):
  `<`, `>`, `<=`, `>=`, the arithmetic, `min`, `max`, `in` and `cat`, per § 5.1's null column.
  Strict `===`/`!==` is standard and needs no override.
- **Refused operations are refused twice:** at publish by the checker, and at run time by the
  evaluator ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1393#issuecomment-6984396609)), which declines a rule naming any operation outside § 5, so a hand-edited
  or corrupted `resolve.json` can never fall through to Corvus's defaults.
- **Invariant culture**, always.
- **Compiled once:** each rule in a `resolve.json` is compiled to Corvus's cached delegate tree
  once per package version and cached ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1393#issuecomment-6984396609)).
- **A condition that yields no boolean at run time fails its node** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1393#issuecomment-6984396609)), with the
  reason recorded: impossible after the checker, except through data that escaped validation; the
  node that owns the condition fails with § 11.3's sentence.
- **Eventually** json-everything's `JsonLogic`, paying its maintenance fee, is #1458 (no
  milestone); a swap must give byte-identical golden tests and corpus re-render.

## 10. What the record holds

The record's shape is [part 08](08-record.md)'s. For expressions:

- **A decision for every condition**
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6957680891)),
  in the job's one `decisions` list
  ([part 08](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979794633)):
  its site, the printed condition, its result, a hash of each value it read, whether it was
  run-dependent and model-derived, and an `expressionHash` of its canonical JSON Logic. A
  condition reading `.title` is printed with both the raw value and the title.
- **A computed node's row:** the input hash (over the canonical expression and the values it
  read), the value as canonical JSON, the output hash (its producer is `computed`),
  `reproducible: true`, `modelDerived` (§ 4.5), and `runDependent` with the run-neutral hash of
  its recipe (§ 4.4).
- **`matches`:** the pattern (in the printed condition) and the result; never the matched text.
- **Run facts:** each one read, once per run, `run.date` with its time zone.
- **`lock.json`'s hash** (`lockHash`) ties every decision to the evaluator and operation
  versions that made it.

## 11. Refusals and warnings

The wording below is the starting text. It follows the style rules in [index § 7](index.md#7-refusal-sentences) and is used as written in Milestone 39 and reviewed in #1483 (Milestone 40); every sentence is listed in [refusal-sentences.md](refusal-sentences.md).

### 11.1 Refusals at publish

Every rule below is decided (linked, or in [part 03's decision](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6962982698)).

Placeholders: `<site>` names the declaration as its sentences do elsewhere: `Node 'x'`,
`Loop 'x'`, `While 'x'`, `Check 'x'`, `Deliverable 'x'`, `Macro 'x'`, `Part 'p' of node 'x'`,
`Computed node 'x'`, and inside a function
`Node 'x' in function 'f'`. `<member>` is `if`, `while`, `until` or `expression`. `<path>` is a
`var` path; `<format>` a format's type string (part 02).

| Sentence | Status |
|---|---|
| *"\<site\> compares \<path\> (\<format\>) with a literal; text is tested by whether it is blank, its size, or in."* | rule decided ([rule](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6878621423), [revised](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6878673101)) |
| *"\<site\> compares \<path\> (\<format\>) with \<path\> (\<format\>); text is tested by whether it is blank, its size, or in."* | as above |
| *"\<site\> uses ==; equality is written === (shown as =)."* | rule decided |
| *"\<site\> uses !=; inequality is written !== (shown as ≠)."* | as above |
| *"\<site\> uses \<operation\>, which is not a JSON Logic operation of this format (\<the allowed list\>)."* | rule decided (§ 5.1) |
| *"\<site\> uses substr; text is cut with trim or plain, and measured with size."* | rule decided |
| *"\<site\> orders \<path\> (\<format\>); \<, \>, ≤ and ≥ compare numbers or dates."* | rule decided |
| *"\<site\> tests \<path\> (\<format\>) with not; not takes a boolean — test text with blank."* | rule decided |
| *"\<site\> gives \<operation\> \<n\> operands; it takes \<m\>."* | rule decided (§ 5.1, three-operand comparisons included) |
| *"\<site\> writes \<operation\> with a bare operand; operands are written as an array."* | rule decided (§ 2.2) |
| *"\<site\> reads var with a default; every read is one path."* | rule decided (§ 2.2) |
| *"\<site\> reads \<path\>, a position in a list; lists are read whole, or by identity through .items."* | rule decided (§ 2.2) |
| *"\<site\> reads \<path\>, which doesn't exist here."* | rule decided |
| *"\<site\> reads input.\<id\>; an input is read as input.\<id\>.value, .content or .title."* | rule decided ([part 02](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466)) |
| *"\<site\> reads \<path\>.title, but \<format\> is a plain enum with no titles; read .value, or give the format oneOf with const and title."* | rule decided (part 02) |
| *"\<site\> compares \<path\>.title with "\<text\>", which is not one of its titles (\<titles\>)."* | rule decided (part 02) |
| *"\<site\> reads profile.\<field\>; profile fields are read only in deliverable templates and macro bodies, never in conditions or computed expressions."* | rule decided ([#1386](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6924497801)) |
| *"\<site\> reads \<root\>.\<rest\>; JSON Logic reads input, node, check, item, pass, param and run."* (for `snippet`, `part`, `macro`, `renderer`, `deliverable`) | rule decided (§ 4.1) |
| *"Deliverable '\<id\>' if reads node.\<id\>.content; a deliverable's if reads data (.value), not text."* | rule decided ([#1406](https://github.com/Consultologist/Consultologist-Blazor/issues/1406)) |
| *"Deliverable '\<id\>' if reads check.\<id\>; a deliverable's existence can't depend on a check."* | as above |
| *"Macro '\<id\>' if reads param.\<name\>; a macro's if is shared, so it can't depend on one function's parameter."* | rule decided ([#1419](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6897801178)) |
| *"\<site\> compares \<path\> with "\<value\>", which is not one of its values (\<values\>)."* | rule decided (v25's enum rule) |
| *"\<site\> reads \<path\>.value.\<field\>, which not every member of \<format\> declares."* | rule decided (#1385 decision 5) |
| *"\<site\> \<member\> yields \<format\>; a condition yields a boolean."* | rule decided (§ 3) |
| *"Computed node '\<id\>' yields \<format\>; it declares \<format\>."* | rule decided (v30's let sentence, adapted) |
| *"Computed node '\<id\>' builds \<format\> without \<field\>; object must give every property of the format."* | rule decided (§ 5.4) |
| *"Computed node '\<id\>' builds \<format\> with \<field\>, which the format doesn't declare."* | as above |
| *"\<site\> gives \<operation\> a computed \<operand\>; \<operand\> must be written as a literal."* | rule decided ([#1400](https://github.com/Consultologist/Consultologist-Blazor/issues/1400#issuecomment-6905611896), [failWith](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6919283116), [min](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6978700251)); `<operand>` is `pattern`, `word`, `failWith` or `count_pass`'s `min` |
| *"\<site\> gives count_pass a min of \<n\>; min is a whole number, 0 or more."* | rule decided ([part 06](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6978700251)) |
| *"\<site\> pattern "\<pattern\>" is not I-Regexp (RFC 9485): \<reason\>."* | rule decided |
| *"\<site\> pattern "\<pattern\>" is \<n\> characters long; the most is 200."* | rule decided (200, [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6962982698)) |
| *"\<site\> pattern "\<pattern\>" repeats \<n\> times; a counted repetition is at most 100."* | rule decided ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)) |
| *"\<site\> pattern "\<pattern\>" doesn't compile with the engine's options: \<reason\>."* | rule decided ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)) |
| *"\<site\> reads \<path\>, which carries profile text; prompts, parts and conditions don't read profile text."* | rule decided ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660), § 4.6) |
| *"\<site\> gives has_word a blank word; has_word finds one word or phrase."* | rule decided |
| *"\<site\> uses if without an else; write the else."* | rule decided |
| *"\<site\> cats \<path\> (\<format\>); convert Markdown with plain first."* | rule decided |
| *"Node '\<id\>' declares \<modelDerived \| runDependent \| profileCarrying\> \<value\>, but its reads make it \<derived\>: \<the read that does, or none\>."* | rule decided ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1478#issuecomment-6982835861)) |

### 11.2 Warnings at publish and in the Editor

- **Run-dependence**
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6925165013)),
  one line per value or decision:
  *"\<site\> \<member\> reads run.\<fact\>; its decision can differ between runs with the same inputs."*
  and *"Computed node '\<id\>' reads \<run.fact or path\>, which is run-dependent; its value can differ between runs with the same inputs."*
- **Model-derived** is shown, not warned: History, the diagram and the Editor mark each
  model-derived value and decision (§ 4.5); the Editor shows:
  *"computed from model text: node.\<id\>"*.

### 11.3 Failures at run time

The rules are decided (§ 5.3: text over 1 MiB fails the node, [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660); § 6.2: evaluation never
throws; § 9: a condition that yields no boolean fails its node).

- *"\<site\> \<member\> could not be decided: matches was given \<n\> MiB of text; the most is 1 MiB."* The node that
  owns the condition fails, recorded with this reason.
- *"\<site\> \<member\> yields \<a value kind\>; a condition yields a boolean."* The node that owns
  the condition fails (§ 9).

## Proposed in this part

Nothing. Every point is decided; refusal sentences are used as written and reviewed in #1483 ([refusal-sentences.md](refusal-sentences.md)).
