# 04. Loops and functions

This part defines the flow nodes of version 31 (`for`, `while`, `if`, `case`) as folders with an
`index.json`, every member with its type and null rule; how a loop runs (`parallel`,
`maxParallel`, `until`, `max`), how it names its items (`itemLabel`), and how it is read from
outside (`.value`, `.items`, `.stopped`); the `FormatBounds` that limit them; and functions:
named, sealed sub-workflows in `functions/`, run by a `call` node with `with`. Issues: #1392
(loops), #1406 (`.items`), #1419 (functions), and #1393 for how loops and calls count toward the
per-job ceiling (the engine side is [part 09](09-engine-and-pages.md)). Conditions inside these
declarations are JSON Logic, defined in [part 03](03-expressions.md); folders, ids and scopes are
[part 01](01-files-and-scopes.md); formats are [part 02](02-types-and-inputs.md). Part 04's
Proposed points were [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6963556118)
on 2026-11-17.

## 1. Flow nodes are folders

A `for`, `while`, `if` or `case` is a folder, `nodes/…/<id>/`, described by its `index.json`
(#1383). The folder's children are the files and folders its `index.json` lists; a child listed
nowhere is a stray and is refused (part 01).

**Members every flow node's `index.json` has** (every member present, `null` only where stated,
#1394):

| Member | Type | Null | Meaning |
|---|---|---|---|
| `kind` | `"for"` \| `"while"` \| `"if"` \| `"case"` | never | what the node is |
| `label` | string | **never** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6957670672)) | the display name: the rail, History, the diagram |
| `description` | array of strings (lines) | `null` = none | for people only, at most 500 characters joined ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6898087160)) |
| `format` | a format name or an inline schema (part 02) | never | the node's return type, written, and checked against the type its `ret` gives it (§§ 2.5, 3.4, 5.3) by type equality; a mismatch is refused ([decided in part 01](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)) |
| `reproducible` | boolean | never | written, and checked against the derived value: `true` only if everything in its body that its result depends on is `true`; a mismatch is refused ([decided in part 01](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910); revisiting it is #1466) |
| `producer` | `"computed"` | never | written, and checked against the kind: a flow node is `computed` even when its body holds agent nodes; a mismatch is refused ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868), part 02 § 1.3) |
| `modelDerived` | boolean | never | written, and checked against the derived value: `true` when its `ret`, or anything its result is made from, is model-derived, as when its body holds an agent node; a mismatch is refused ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1478#issuecomment-6982835861); part 03 § 4.5) |
| `runDependent` | boolean | never | written, and checked against the derived value: `true` when its source or its `ret` is run-dependent (a condition passes no flag on, part 03 § 4.4, [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)); a mismatch is refused ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1478#issuecomment-6982835861); part 03 § 4.4) |

Each kind adds its own members, below. A member of another kind is refused (§ 10).
Run-dependence and the model-derived flag are worked out from the body (part 03 §§ 4.4–4.5), and
written as `runDependent` and `modelDerived`, checked against those values ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1478#issuecomment-6982835861)).

**`ret` is the child's full name** ([decided 2026-11-17](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6958687389)): the root and the
qualified key of one of that body's **own** children, as the record keys it (part 01):
`"ret": "node.sections.paragraphs"` in `nodes/sections/index.json`,
`"ret": "node.sections.paragraphs.draft_paragraph"` in `nodes/sections/paragraphs/index.json`,
`"ret": "check.part_coverage.coverage"` in `checks/part_coverage/index.json` (as #1396 decided).
Inside a function the key is counted from the function's root (`"ret": "node.apply_instructions"`).
The root says which namespace the child is in (`node.` or `check.`), and the whole name can be
read without knowing which folder the file sits in. The resolver checks that `ret` names a direct
child of that body and refuses anything else (§ 10). `ret` is the only member that takes this
qualified form: references elsewhere still resolve outward by bare id (part 01, #1384), and a
node's fields are always reached through `.value` or `.items`, so `node.sections.paragraphs` is
never read as a field path. (`example-nested-note`'s loop was `parts` at 30; `parts` is now a
reserved id, so the migration renames it `sections`, part 01.) The Editor may show only the bare id; that's Milestone 41.

## 2. `for`

### 2.1 Members

| Member | Type | Null | Meaning |
|---|---|---|---|
| `in` | a reference string (§ 2.2) | never | the list the loop iterates |
| `parallel` | boolean | never | `true`: items run at the same time; `false`: one at a time, in list order ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6877352714)) |
| `maxParallel` | integer, 2 to `FormatBounds.parallel` (15) | `null` = no cap of its own | how many items run at once; must be `null` when `parallel` is `false` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6904683449)) |
| `until` | a JSON Logic condition (part 03) | `null` = runs every item | stops after the item where it holds; non-null requires `"parallel": false` |
| `max` | integer, at least 1 | `null` = no cap | runs the first `max` items in list order (v30, unchanged) |
| `itemLabel` | a field name (string) | `null` = position names | names each item (§ 2.7) ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6924320332)) |
| `nodes` | array of ids, at least one | never | the body, in order |
| `ret` | the child's full name (§ 1) | never | the body node whose value is each item's value |

Together with § 1's eight members, a `for`'s `index.json` has exactly sixteen members.

### 2.2 What a loop iterates

`in` is a **reference string** naming a list, never a JSON Logic expression (#1383;
[decided in part 03](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6962982698)).
A list that has to be computed is a `computed` node, which `in` then names (#1473):

| Source | Example | Its items are known |
|---|---|---|
| an array input | `"input.sections.value"` | at start |
| a snippet folder | `"snippet.standards"` | at start: a folder's items have no `if`, so its size is its number of files (part 01; dynamic sizes are #1468) |
| a list in a visible node's value | `"node.plan.value.sections"` | when that node completes, for this loop instance |
| a list field of the outer item | `"item.value.paragraphs"` | when the outer item is known |
| a loop's `.items` | `"node.sections.items"` | item by item, as each completes (§ 2.6) |
| inside a function, a parameter's list | `"param.sections.value"` | at the call |

- A node in an enclosing body is aligned by path (v30 § 2.2): `"node.outline.value.points"`
  inside `sections` is *this* section's outline.
- **Any loop may iterate `.items`**, a production loop as well as a check loop (#1396, part 06),
  with the same rules: for example to write one line per section, skipped sections included,
  each naming its state
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6963556118)).
- The source must be typed as a list (a JSON Schema array). Item ids keep v30's forms: array
  inputs 0-based, node values 1-based, folder items their file stems (v30 § 1.1).

### 2.3 `parallel` and `maxParallel`

- **`"parallel": true`** schedules every item at once, or at most `maxParallel` at a time; when
  one completes the next starts, in list order.
- **`"parallel": false`** runs the items one at a time, in list order; item *n*+1 starts when
  item *n* completes. It is the only form that allows `until`.
- **One way to say "one at a time":** `"parallel": true, "maxParallel": 1` is refused, pointing
  to `"parallel": false` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6877395680)).
  The Editor offers 2 and up (or "none") when `parallel` is on, and a loaded package holding 1
  shows the sentence with a one-click "Make sequential" fix.
- **Per loop instance:** an inner loop's cap applies within each outer item, so caps multiply
  across nesting (§ 8).
- **`maxParallel: null`** means the loop wants no cap of its own; it is limited only by the
  engine's per-job ceiling (§ 8). The package says how much parallelism a loop wants; the
  deployment decides how much it gets.

### 2.4 `until`, `max` and `.stopped`

As v30 § 3.4, with the condition in JSON Logic:

- **`until` is checked after each item**, over that item's body nodes by bare id, `item`, and
  anything that runs before the loop (part 03 § 4.2). The loop stops after the item where it
  holds, so which items ran is the same on every run.
- **The items it didn't run stay** in the loop's count and `.items`, with state `not_run` and
  the reason *"stopped early at \<position name\>"* (§ 2.6).
- **`max`** runs the first `max` items in list order; the rest are `not_run`, with the reason
  *"not run: beyond max \<n\>"*, and the loop's `.stopped` is `"bound"`
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6963556118)).
- **`.stopped`** is `"until"` (its `until` held), `"bound"` (its `max` was reached) or `"end"` (it
  ran out of items), typed by those values.
- An empty list settles at 0 items, with `.stopped` `"end"`, never a failure (v30 § 2.5).
- **A `null` source** (a nullable list, or a read of a node that was skipped) settles the same
  way, at 0 items with `.stopped` `"end"`, and the loop's row records the reason
  *"0 items: \<reference\> is null"* ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)).
- **At most 200 items per loop instance** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)): an engine setting, checked at run time. A
  list longer than that fails the loop's node with *"Loop '\<id\>' was given \<n\> items; this
  engine runs at most \<limit\> per loop."*, and publish notes a loop whose list could be longer
  (§ 10.2).

Example, fictional: reading prior letters in order until one answers the referral question.

```json
{ "kind": "for", "producer": "computed", "label": "Prior letters", "description": null,
  "format": { "type": "array", "items": { "$ref": "plaintext" } },
  "reproducible": false, "modelDerived": true, "runDependent": false,
  "in": "input.prior_letters.value",
  "parallel": false, "maxParallel": null,
  "until": { "===": [ { "var": "node.read_letter.value.answers_question" }, true ] },
  "max": null, "itemLabel": null,
  "nodes": ["read_letter", "letter_summary"], "ret": "node.letters.letter_summary" }
```

### 2.5 Reading a loop from outside

| Member | Type | Means |
|---|---|---|
| `.value` | `list<T>`, `T` its `ret`'s format | the values of the items that **completed**, in item order ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6959884271)) |
| `.items` | a list of the element of § 2.6 | every item, in item order, each paired with its outputs by identity |
| `.content` | `markdown_block` | the standard rendering (`## <item name>`, then the item's text, joined by blank lines), so today's readers keep their bytes (#1406; part 05) |
| `.state` | `"completed"` \| `"skipped"` \| `"not_run"` | the loop node's own state (inside an untaken branch, a loop is `skipped`) |
| `.stopped` | `"until"` \| `"bound"` \| `"end"` | § 2.4 |

- **A loop's `format`** is `list<T>`, in JSON Schema `{ "type": "array", "items": T }`;
  nested loops nest (`list<list<plaintext>>`).
- **`.value` holds completed items only** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6959884271),
  [#1406](https://github.com/Consultologist/Consultologist-Blazor/issues/1406#issuecomment-6959884701)). Items that didn't run are absent, never `null`
  placeholders; they are seen, with their `state` and `reason`, through `.items`. This replaces
  #1385 decision 5's "elements marked skipped", which predates `.items`. `.value` stays a clean
  `list<T>` that templates and `all_pass` can read without a `null` test (part 03 § 5.4).
- **No zipping by position.** Pairing two lists by index isn't offered: an `if`, an `until` or a
  reordered `index.json` would put a heading over the wrong text. `.items` pairs by identity
  (#1406).

### 2.6 `item` and the `.items` element

**Inside the body, `item`** is the innermost loop's current item; `item.parent` the enclosing
loop's, outward (`item.parent.parent`), skipping any `while` between (v30 § 2.4).

| Member | Type | Means |
|---|---|---|
| `item.id` | string | the item id (§ 2.2) |
| `item.value` | the source's element type | the element: a row, a folder item's header data, an element of a node's list |
| `item.content` | text | for a snippet folder item only: its body (#1407) |
| `item.parent` | an item | the enclosing loop's item; refused in an outermost loop |

**Each element of `.items`** ([#1406](https://github.com/Consultologist/Consultologist-Blazor/issues/1406),
[states](https://github.com/Consultologist/Consultologist-Blazor/issues/1406#issuecomment-6881816481)):

| Member | Type | Null | Means |
|---|---|---|---|
| `item` | an item, as above | never | the source element this row came from |
| `label` | string | never | the item's name (§ 2.7) |
| `state` | `"completed"` \| `"skipped"` \| `"not_run"` | never | the state of the item's `ret`, so an item whose `ret` is an untaken `if` is `skipped` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6963556118)) |
| `reason` | string | `null` when completed | package text and fixed engine sentences only, never patient text: the printed condition for `skipped` (*"skipped: item.value.kind = "narrative" is false"*), *"stopped early at \<position name\>"* or *"not run: beyond max \<n\>"* for `not_run` |
| `value` | `T` | `null` unless completed | the `ret`'s value |
| `content` | text | `null` unless completed | the text of the item's `ret` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6963556118), beside #1406's `item, value, state, reason, nodes`) |
| `nodes` | an object keyed by body id | never | each body node for this item: `.value`, `.content`, `.state` |

- **The states are a closed set.** A template that branches on `state` must handle every state
  or end with an `else` (part 05). `onInvalid` is `fail` or `retry`, so a failed item fails the
  job and never reaches a reader.
- **A loop over `.items`** (a check loop, or a production loop, § 2.2) has the element above as
  its `item`, so it reads `item.state`, `item.value` and `item.nodes.<id>` for the same item.
- **Check items share the production items' ids**, so `check.part_coverage.coverage:2` lines up
  with `node.sections:2`
  ([decided in part 06](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6978700251)).

### 2.7 `itemLabel`: naming items

([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6880614681),
[renamed](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6924320332))

- **`itemLabel` names a field of each element,** or is `null` for position names. It names items
  in the rail, progress, History and the record. `label` stays the loop's display name.
- **The field must be guaranteed** to be a non-null string on every element:
  - a node's list: listed in the element schema's `required` and typed a non-nullable `string`;
  - an array input: a required `string` property of each row (`example-conditional-sections` uses `title`);
  - a snippet folder: required in the folder's `format`. A folder item's header is typed data
    with no `if` (part 05); fields have no reserved names (part 02 § 5.3), so `test/general`'s
    field keeps its name `title` and its snippet loop writes `"itemLabel": "title"`;
  - a loop over `.items`: `"itemLabel": "label"`, the element's own `label`, which carries the
    production loop's item name; check loops name their items the same way
    ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6963556118),
    [part 06](https://github.com/Consultologist/Consultologist-Blazor/issues/1395#issuecomment-6978700251)).
- **At run time,** a value that is empty after trimming falls back to the position name, marked
  in the record.
- **Position names:** with `"itemLabel": null`, items are named `<loop label> <n>`, the loop's
  `label` and the 1-based position: *"Sections 2"*
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6963556118);
  v30 used the input's label for input loops; at 31 the loop owns naming).
- **Retention** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660), replacing v30 revision 3's rule): a name is trimmed and cut to 120
  characters ending in "…", and is **kept in the record past the retention sweep only when it
  comes from package text** (a snippet folder's header). A name drawn from an input or a model's
  output follows 7-day retention, and History then shows the position name. **Reasons always use
  position names** (*"stopped early at Sections 3"*), never a drawn name, since a reason can land in
  a deliverable (part 06 § 6.2).
- **Migration** writes `labelField` (an input's or a loop's) as `itemLabel` on each loop over it
  (#1447).

### 2.8 Examples (migrated)

`example-nested-note`, `nodes/sections/index.json` and `nodes/sections/paragraphs/index.json`
(the loop `parts` renamed `sections`, part 01):

```json
{ "kind": "for", "producer": "computed", "label": "Sections", "description": null,
  "format": { "type": "array", "items": { "type": "array", "items": { "$ref": "plaintext" } } },
  "reproducible": false, "modelDerived": true, "runDependent": false,
  "in": "node.plan.value.sections",
  "parallel": true, "maxParallel": null, "until": null, "max": null,
  "itemLabel": "title",
  "nodes": ["outline", "paragraphs"], "ret": "node.sections.paragraphs" }
```

```json
{ "kind": "for", "producer": "computed", "label": "Paragraphs", "description": null,
  "format": { "type": "array", "items": { "$ref": "plaintext" } },
  "reproducible": false, "modelDerived": true, "runDependent": false,
  "in": "node.outline.value.points",
  "parallel": true, "maxParallel": null, "until": null, "max": null,
  "itemLabel": null,
  "nodes": ["draft_paragraph"], "ret": "node.sections.paragraphs.draft_paragraph" }
```

`example-conditional-sections`, `nodes/sections_draft/index.json` (the input's `labelField`
moved to the loop):

```json
{ "kind": "for", "producer": "computed", "label": "Sections — drafting each requested section by its kind", "description": null,
  "format": { "type": "array", "items": { "$ref": "plaintext" } },
  "reproducible": false, "modelDerived": true, "runDependent": false,
  "in": "input.sections.value",
  "parallel": true, "maxParallel": null, "until": null, "max": null,
  "itemLabel": "title",
  "nodes": ["route"], "ret": "node.sections_draft.route" }
```

`test/general`, `nodes/standards/index.json`:

```json
{ "kind": "for", "producer": "computed", "label": "Standards", "description": null,
  "format": { "type": "array", "items": { "$ref": "plaintext" } },
  "reproducible": false, "modelDerived": true, "runDependent": false,
  "in": "snippet.standards",
  "parallel": true, "maxParallel": null, "until": null, "max": null,
  "itemLabel": "title",
  "nodes": ["section_instructions", "patient_section_draft", "standard_section_draft"],
  "ret": "node.standards.section_instructions" }
```

Every migrated loop gets `"parallel": true, "until": null` (or `"parallel": false` where it had
`until`), `"maxParallel": null`, and `null` for every unused member, so nothing changes meaning.

## 3. `while`

### 3.1 Members

| Member | Type | Null | Meaning |
|---|---|---|---|
| `while` | a JSON Logic condition | never | the check; the body runs again while it holds |
| `max` | integer, 1 to `FormatBounds.passes` (15) | never | the bound; there is no default |
| `until` | a JSON Logic condition | `null` = none | checked after each pass; whichever of `while` and `until` stops first wins |
| `checkFirst` | boolean | never | `false`: the check runs after each pass (at least one pass); `true`: before each pass (zero passes possible) |
| `nodes` | array of ids, at least one | never | the body, in order |
| `ret` | the child's full name (§ 1) | never | the body node whose last pass's value the while returns |

With § 1's eight members, a `while`'s `index.json` has fourteen members. A `while` has no `parallel`,
`maxParallel` or `itemLabel`: it is sequential by definition and its passes are numbered.
`checkFirst` was optional at 30 and is required at 31 (#1394).

### 3.2 How it runs

As v30 § 3.1:
- the body runs, then again for as long as `while` holds and `until` doesn't, at most `max`
  passes;
- **reaching the bound is a named state**: *"stopped at its bound of 3; the check never stopped
  holding"*; the run continues with the last pass's output;
- with `checkFirst: true` and a check that fails at once, zero passes run: `.passes` is 0,
  `.stopped` is `"check"`, and the returned node reads as skipped.

### 3.3 What a pass reads

Unchanged from v30 § 3.2. In JSON Logic:

| Read | Means | Dependency edge |
|---|---|---|
| `node.judge` or `pass.node.judge` | this pass's node | yes |
| `pass.number` | this pass's number, from 1 | no |
| `pass.previous.node.judge` | one pass back; `null` on the first pass | no |
| `pass.parent.number` | the enclosing while's pass, skipping any `for` between | this pass: yes |
| `pass.history` | every earlier pass, oldest first | no |

In conditions, `pass.history` is readable only through `size`; templates may loop over it
(part 05)
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6963556118);
positional reads are refused, part 03 § 2.2).

### 3.4 Reading a while from outside

`.value` and `.content` are the last pass's `ret`; `.passes` is the pass count; `.stopped` is
`"check"`, `"until"` or `"bound"`; `.history` is every pass (v30 § 3.2); `.state` as § 2.5. A
`while`'s `format` is exactly its `ret`'s.

### 3.5 Example (migrated)

`example-nested-note`, `nodes/refine/index.json`, from `node.judge.value.verdict != "good"`:

```json
{ "kind": "while", "producer": "computed", "label": "Refining the summary", "description": null,
  "format": "plaintext",
  "reproducible": false, "modelDerived": true, "runDependent": false,
  "while": { "!==": [ { "var": "node.judge.value.verdict" }, "good" ] },
  "max": 3, "until": null, "checkFirst": false,
  "nodes": ["draft_summary", "judge"], "ret": "node.refine.draft_summary" }
```

Printed: `node.judge.value.verdict ≠ "good"`. `verdict` is a `string` with an enum, so it is data
and strict equality applies; `"good"` is checked against the enum.

## 4. Reading across levels

#1384's scope rules (part 01) and v30 § 2.4 hold:

- A body node reads its siblings, the nodes of the bodies that enclose it (aligned by path), and
  the top level. `node.x` resolves outward; nothing is resolved by "nearest wins".
- **A node inside a body is a list from outside it**: read the loop (`node.paragraphs`), never
  its body node.
- **Siblings across instances are not readable:** `node.draft_paragraph` always aligns to the
  reader's own path.
- Inside a function, resolution stops at the function's root (§ 7.4).

**The contagious flags cross levels too**
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6963556118);
part 03 §§ 4.4–4.6). Run-dependence, the model-derived flag and the profile-carrying flag pass
into a loop's items when its source has them, out through a flow node's `ret`, and through a
call's arguments and result. A condition (an `if`, a `case` subject, a `while`, an `until`)
passes none of them on to what it chooses ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)).

- **The output of every node whose `producer` isn't `computed` is model-derived,** so every
  agent node's output is; the reverse doesn't hold (§ 7.10's pattern B is a computed
  `judgement` that is model-derived).
- **An input element from a previous run's deliverable** (#874) starts out model-derived, and the
  record links to the job it came from. In a mixed array input, only those elements are; typed
  text and uploaded documents start clean. Other machine sources (OCR, transcription) are
  producers reserved for Milestone 40 (`extracted`, #1438; part 02 § 1.3).

## 5. Branches: `if` and `case`

### 5.1 `if`

| Member | Type | Null | Meaning |
|---|---|---|---|
| `if` | a JSON Logic condition | never | the decision |
| `then` | a body: `{ "nodes": [ids], "ret": full name }` | never | runs when the condition holds |
| `else` | a body | `null` = no else | runs when it doesn't |

With § 1's eight members, eleven members. A body object has exactly `nodes` (at least one id) and
`ret`.

### 5.2 `case`

| Member | Type | Null | Meaning |
|---|---|---|---|
| `case` | a reference string | never | the subject |
| `branches` | array, at least one, of `{ "when": [values], "nodes": [ids], "ret": full name }`; an empty branch writes `"nodes": []` and `"ret": null` | never | one branch per set of values |
| `else` | a body | `null` only when the branches list every value and the subject can't be `null` | runs when no branch's values match, and when the subject is `null` |

With § 1's eight members, eleven members.

([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6963556118))

- **The subject** is a reference string, never a JSON Logic expression (part 03 § 3); a subject
  that has to be computed is a `computed` node the `case` names (#1473). It must be typed as an
  enumerated `string`: `enum`, or `oneOf` with `const` (part 02):
  `"item.value.kind"`, `"node.triage.value.category"`, `"input.encounter_kind.value"`.
- **Each `when` lists one or more values** of the subject's enum (the raw values, never titles);
  a value may appear in at most one branch, once.
- **Every `case` is exhaustive:** if the branches don't list every value, an `else` is required,
  and publish refuses a `case` that could match nothing. A `case` therefore always runs a
  branch for every value.
- **A subject that can be `null`** (a nullable enum, part 02 § 3.2, or a read of a node that may
  be skipped) **requires an `else`**, and `null` goes there ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)). A `when` never lists `null`.
- **A branch may be explicitly empty:** `{ "when": ["omit"], "nodes": [], "ret": null }`, the one
  body whose `nodes` is empty and whose `ret` is `null`, both together. For those values the `case`
  reads as skipped, with the reason *"case '<id>': arm for <value> does nothing"*
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)). So skipping some values is written in the `case` itself, never as an `if`
  around it (§ 5.4).

### 5.3 Format, state and exhaustiveness

- **The format of an `if` or `case` is the union of what its bodies return**
  (`plaintext | classification`; in JSON Schema `anyOf`). Each body's `ret` must be a
  member, and each member returned by some body (#1385 decision 5). When every body returns one
  format, the union is that format. The `anyOf` is the result itself, with no wrapper object,
  and the condition's row records which branch ran
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6959885810), part 02 § 3.3).
- **A reader of a union:** `.content` always; `.value` only if every member has a value;
  `.value.<field>` only if every member declares it compatibly.
- **An untaken `if` with no `else`,** and a `case` whose subject took an empty branch, have state
  `skipped`; their `.value` and `.content` read as `null` in JSON Logic (part 03 § 6.1). Skipped is
  a state, not a union member, so an empty branch adds nothing to the union.
- **Exhaustive** means every value of the subject has a branch, or an `else`, which every `case`
  is (§ 5.2). **Always runs a body** is stronger: an `if` with an `else`, or a `case` with no
  empty branch. An `if` with no `else`, or a `case` with an empty branch, is allowed except where
  a value must always exist: a function's `ret` (§ 7.6).
- **The decision is recorded** in the job's `decisions` list with the printed condition (or the
  subject and the branch taken), its run-dependence and whether it is model-derived (part 08).

### 5.4 Examples (migrated)

`example-conditional-sections`. The input's `kind` enum is `narrative`, `table`, `omit`. At 30,
`route` was a `case` with a branch for `narrative` and one for `table` and no `else`, so `omit`
sections were skipped. At 31 `route` keeps its shape and keys, and the migration writes the
`omit` branch explicitly empty, so every value is listed and no `else` is needed
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868); part 10).

`nodes/sections_draft/route/index.json`:

```json
{ "kind": "case", "producer": "computed", "label": "Routing a section by its kind", "description": null,
  "format": "plaintext",
  "reproducible": false, "modelDerived": true, "runDependent": false,
  "case": "item.value.kind",
  "branches": [
    { "when": ["narrative"], "nodes": ["narrative_draft"], "ret": "node.sections_draft.route.narrative_draft" },
    { "when": ["table"],     "nodes": ["table_draft"],     "ret": "node.sections_draft.route.table_draft" },
    { "when": ["omit"],      "nodes": [],                  "ret": null } ],
  "else": null }
```

An omitted section's `route` reads as skipped, with the reason *"case 'route': arm for omit does
nothing"*.

`nodes/patient_summaries/narrative_only/index.json`:

```json
{ "kind": "if", "producer": "computed", "label": "Narrative sections only", "description": null,
  "format": "plaintext",
  "reproducible": false, "modelDerived": true, "runDependent": false,
  "if": { "===": [ { "var": "item.value.kind" }, "narrative" ] },
  "then": { "nodes": ["patient_summary"], "ret": "node.patient_summaries.narrative_only.patient_summary" },
  "else": null }
```

`nodes/standing_sections/applies/index.json`:

```json
{ "kind": "if", "producer": "computed", "label": "Holding the billing section back unless asked", "description": null,
  "format": "plaintext",
  "reproducible": false, "modelDerived": true, "runDependent": false,
  "if": { "or": [ { "!==": [ { "var": "item.id" }, "billing" ] },
                  { "===": [ { "var": "input.include_billing.value" }, true ] } ] },
  "then": { "nodes": ["standing_draft"], "ret": "node.standing_sections.applies.standing_draft" },
  "else": null }
```

When a fictional section "Billing" is skipped, its reason reads *"skipped: item.id ≠ "billing"
or input.include_billing.value = true is false"*.

## 6. `FormatBounds`

([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6904683449))

| Member | Value | Limits |
|---|---|---|
| `passes` | 15 | a `while`'s `max`: 1 to 15 |
| `depth` | 15 | nested function calls: 0 to 15 (§ 7.8) |
| `parallel` | 15 | a loop's `maxParallel`: 2 to 15, or `null` |

- **Constants of the format,** defined in the spec and in `Consultologist.PackageFormat`
  (replacing `WorkflowFlowNodes.MaxPasses = 10`), covered by the dialect version (#1390), and
  **never written in a package**.
- **Each can be raised alone later,** which is always compatible; lowering one would break
  packages.
- **Messages name the limit** (§ 10).
- A `for`'s `max` has no format bound (v30: at least 1, no cap).

## 7. Functions

A function is a **named, reusable sub-workflow**: a group of nodes behind declared, typed inputs
and one typed result, run by a `call` node from anywhere
([#1419](https://github.com/Consultologist/Consultologist-Blazor/issues/1419)). Milestone 39 has
functions within a package; imports from other packages are #1420 (Milestone 40)
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6897906977)).

### 7.1 The definition folder

```
functions/
  index.json                     (the folder's own index: part 01)
  draft_section/
    index.json                   (the definition, § 7.2)
    standard_draft.md
    patient_draft.md
    apply_instructions.md
    snippets/ formats/ renderers/ macros/     (always present, each with its index.json: its own definitions, § 7.3)
```

- **`functions/<id>/`** holds one function; the folder name is its id.
- **Its nodes are its direct children** (files and flow folders), listed in `nodes` as in any
  body.
- **Its own definitions** live in sub-folders shaped like a package's. `snippets/`, `macros/`,
  `formats/` and `renderers/` are **always present**, each with its `index.json`, even when empty
  (`"items": []`), so an empty folder says visibly that the function defines nothing there
  ([decided in part 01](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)).
  Their names can't collide with a node's id: `snippet(s)`, `macro(s)`, `format(s)` and
  `renderer(s)` are reserved ids everywhere (part 01).
- **Function ids are their own namespace,** named only by a `call`'s `function`, never by
  `node.x`; a function and a node may share an id. Function ids are unique among functions
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6897717122)).

### 7.2 `functions/<id>/index.json`

| Member | Type | Null | Meaning |
|---|---|---|---|
| `label` | string | never | the display name |
| `description` | array of strings | `null` = none | for people only |
| `inputs` | an object keyed by parameter id (§ 7.2.1) | never (`{}` = no parameters) | the parameters, in written order |
| `uses` | `{ "snippets", "macros", "formats", "renderers" }`, each an array of ids | never; `[]` = none | the package definitions it uses (§ 7.3) |
| `nodes` | array of ids, at least one | never | its top-level body, in order |
| `ret` | the child's full name, counted from the function's root (§ 1) | never | the body node it returns (§ 7.6) |
| `format` | a format name or inline schema | never | the function's result type; must equal its `ret`'s format |

#### 7.2.1 Parameters: typed, and all required

Each member of `inputs` is:

| Member | Type | Null | Meaning |
|---|---|---|---|
| `label` | string | never | shown in the Editor and History |
| `description` | array of strings | `null` = none | for people only |
| `format` | a format name or inline schema | never | the parameter's type |

- **Parameters are `{ label, description, format }`, all three required**
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6963556118)):
  the package inputs' shape (#1385) without `required` and `channel`. `description` follows
  part 01's one rule (lines or `null`, at most 500 code points). **Every parameter is required:**
  a call passes every one (§ 7.7); `required` would always be true, and `channel` concerns how a
  person supplies an input, which a call never does.
- **A parameter that may be empty** declares a nullable format, and the caller passes `null`
  explicitly for an optional value; a parameter is never left out. `null` is written as the
  argument itself (`"with": { "prior_letter": null }`), the one literal `with` accepts (§ 7.7)
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)).
- **Read as `param.<id>`,** with `.value` and `.content` as for a node.

### 7.3 `uses`: declared definitions

([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6888253558))

- A function may use the package's snippets, macros, formats and renderers **only if it lists
  them** in `uses`, and may hold its own (§ 7.1).
- **Built-in formats** (the package-format registry's) and **catalogue formats**
  (`concept_list`, `classification`, …) are always available without listing.
- **A snippet passed as data** (`"with": { "standard": "snippet.standards.hpi" }`) is an
  argument, read as `param.standard`, and isn't listed. **A snippet a prompt places as text**
  (`{{~ snippet.snomed_tool_guidance.content }}`) is a definition and is listed.
- **No shadowing:** a function's own definition may not share an id with one it lists in `uses`.
- **A listed definition never used** is a warning, not a refusal.

### 7.4 Sealed: what a function reads

([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6888201229))

Inside a function, every template, condition and computed expression may read:
- **`param.<id>`**, its parameters;
- **`node.<id>`** for nodes inside the function, at any depth within it, resolved outward only up
  to the function's root;
- `item` and `pass` of loops and whiles **inside** the function;
- its own and its listed definitions (`snippet.*`, `macro.*`, `renderer.*`, `part.*` of its own
  nodes).

**Refused inside a function:** `input.*`; nodes outside it; a caller's `item` or `pass`;
`run.*` and `profile.*` read directly (pass them in, § 7.7); `check.*`; `deliverable.*`.

So everything a function uses is visible at the call site: data through `with`, definitions
through `uses`. A function's result depends only on its arguments, which makes it testable alone
(#1449).

### 7.5 What a function may contain

([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6897233071),
[checks](https://github.com/Consultologist/Consultologist-Blazor/issues/1396#issuecomment-6905675034))

- **Allowed:** every kind a package's `nodes/` may hold: `agent`, `rendered`, `computed`, `for`,
  `while`, `if`, `case`, and `call` (of **other** functions); parts and macros (§ 7.10).
- **Refused:**
  - **recursion,** direct or through other functions: the checker builds the call graph at
    publish and refuses any cycle;
  - **deliverables:** top-level and terminal; a function returns a value and a top-level
    deliverable composes it;
  - **checks:** checks gate releases and functions release nothing. A function may compute a
    `check_result` **as data** (a `computed` node with `terms_subset`) and return it. Checking
    each call's output is a check loop over the calls' `.items` (part 06).

### 7.6 One `ret`, always valued, one format

([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6897508423))

- **`ret` names one node** of the function's top-level body. The function's `format` is that
  node's format, checked at publish.
- **`ret` must always have a value:** a node that always runs; or an `if` with an `else`; or a
  `case` with no empty branch (§§ 5.2–5.3). A `ret` that may be skipped is refused.
- **One format:** when `ret` is an `if` or `case`, every body must return the **same** format;
  no unions as function results. A function that returns different shapes wraps them in an
  object format, built by a closing computed node on each body.
- **Several values** are one object format, built by a closing computed node with `object`:

  ```
  ---
  { "kind": "computed", "producer": "computed", "label": "Result", "format": "drafted_section",
    "modelDerived": true, "runDependent": false,
    "expression": { "object": {
        "title":    { "var": "param.standard.value.title" },
        "text":     { "var": "node.apply_instructions.content" },
        "concepts": { "var": "node.extract.value" } } } }
  ---
  ```

  (other leaf members as part 01). `text` is text through the nested-text rule (#1385), and the
  result is model-derived, since it is built from agents' output (part 03 § 4.5).
- **A loop as `ret`** is allowed; the function returns `list<…>`.
- **`.content` of an object result** is its canonical JSON, as for any object-valued node.

### 7.7 The `call` node

A `call` is a leaf, `<id>.md`, **header only** (its body must be empty). Its members, besides
part 01's leaf members (`kind`, `label`, `description`, `format`, `reproducible`, `modelDerived`,
`runDependent`, and the node's own condition, `if`):

| Member | Type | Null | Meaning |
|---|---|---|---|
| `kind` | `"call"` | never | |
| `format` | a format name or inline schema | never | written, as on every node, and equal to the function's `format` by type equality |
| `reproducible` | boolean | never | written, and checked against the function's `ret`'s value; a mismatch is refused ([decided in part 01](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)) |
| `modelDerived` | boolean | never | written, and checked against the derived value: `true` when the function's `ret` is model-derived, or an argument is; a mismatch is refused ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1478#issuecomment-6982835861)) |
| `runDependent` | boolean | never | written, and checked against the derived value: `true` when the function's `ret` is run-dependent, or an argument is; a mismatch is refused ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1478#issuecomment-6982835861)) |
| `function` | a function id | never | the function to run |
| `with` | an object keyed by parameter id; each value a reference string, or the literal `null` | never (`{}` for a function with no parameters) | the arguments |

- **`with` names every parameter, and only those.** A missing or extra parameter is refused.
- **Each argument is a reference string,** as `in` is, never a JSON Logic expression
  ([decided in part 03](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6962982698)):
  `input.<id>.value`, `.content` or `.title` (a bare `input.<id>` is refused, part 02),
  `node.<id>`, `check.<id>`, `item`, `item.parent`, `snippet.<folder>.<item>`, `param.<id>`
  (inside a function), `run.<fact>`, `profile.<field>` (see below), or a field path under one of
  these (`node.plan.value.sections`). The literal `null` is the one literal (§ 7.2.1): a constant or
  computed argument is a `computed` node that the argument names (compute, then name; #1473).
- **Each argument is type-checked** against its parameter's format at publish.
- **`profile.*` may be passed in through `with`** (`"with": { "signature": "profile.signature" }`),
  and then only to reach deliverables and macro bodies ([#1408, decision 7](https://github.com/Consultologist/Consultologist-Blazor/issues/1408#issuecomment-6904843222)). The parameter
  **carries the profile's restriction** through the function: reading it in a prompt, a part or a
  condition is refused (part 06 § 10.2, R-F3), and it may be read in a macro body or passed on
  through `with` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1408#issuecomment-6959886233),
  [#1419](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6959886713)).
- **`run.*` may be passed in:** `"with": { "today": "run.date" }`. The parameter is then
  run-dependent (part 03 § 4.4). In the same way, a parameter bound to a model-derived argument
  is model-derived (part 03 § 4.5).
- **Its value is the function's `ret`,** read like any node (`node.hpi_section.value`,
  `.content`).
- **A whole reference passed to `with`** (`node.<id>`, `item`, `check.<id>`) gives the function
  only its `.value` and `.content`, typed against the parameter's format ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)); the function
  reads `param.<id>.value` and `.content`, never the caller's `.state` or `.items`.
- **Its body runs inline** in the caller's orchestration, never as a sub-orchestration
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)): the call's inner nodes are scheduled by the same orchestrator as its siblings, so the
  per-job ceiling, the call limit and the start order hold inside functions (§ 8; part 09 § 3).
- **Its dependency edges are its arguments.** A call's input hashes are exactly its arguments'
  hashes.

Inside a loop, one call per item:

```json
{ "kind": "for", "producer": "computed", "label": "Standards", "description": null,
  "format": { "type": "array", "items": { "$ref": "plaintext" } },
  "reproducible": false, "modelDerived": true, "runDependent": false,
  "in": "snippet.standards",
  "parallel": true, "maxParallel": 4, "until": null, "max": null,
  "itemLabel": "title",
  "nodes": ["section"], "ret": "node.standards.section" }
```

```jsonc
// nodes/standards/section.md (header only)
---
{ "kind": "call", "producer": "computed", "label": "Drafting the section", "description": null,
  "format": "plaintext", "reproducible": false, "modelDerived": true, "runDependent": false,
  "function": "draft_section",
  "with": { "standard": "item", "draft": "input.consult_draft.content" } }
  // plus part 01's remaining leaf members
---
```

Loop bodies stay inline and anonymous; a function is the named, reusable form, and a `call` is
the one way to run it, at the top level, in a loop body or inside another function
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6897596407)).

### 7.8 Depth and recursion

([depth](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6904683688),
[recursion](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6897233071))

- **Recursion is refused** at publish, so nesting is already finite; the bound is about
  readability and cost.
- **Depth is measured,** not written: the deepest chain of nested calls, from a top-level node,
  against `FormatBounds.depth` (15).
- **Counting**
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6963556118)):
  a `call` outside every function is at depth 0; a `call` inside a function reached at depth *d*
  is at depth *d*+1. "0 means no nested calls" then holds, and `FormatBounds.depth` 15 allows
  sixteen function frames on one chain.

### 7.9 Ids and record keys

([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6897717122))

- **Ids inside a function are free:** a function may use any node id, even one at the package's
  top level or in another function. Within the function, #1384's disjoint-scope rules apply.
- **Record keys go through the call node,** joined with `.` and written with their root (part
  01); the function's name is recorded on the call's row, not in the key:

  | Where | Key |
  |---|---|
  | a top-level call | `node.hpi_section` |
  | its inner node | `node.hpi_section.standard_draft` |
  | a call inside a loop, item 3 | `node.standards.section.standard_draft:3` |
  | a nested call | `node.hpi_section.refine_call.judge` |

### 7.10 Macros inside functions

([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6897801178))

- A function places a macro only if it lists it in `uses` or holds its own.
- **A macro's `if` is evaluated where the macro is placed,** in the function's sealed scope;
  the checker verifies every name it reads exists there. A macro folder's shared `if` is
  evaluated in the same scope. The decision is recorded within the call.
- **Pattern A (the default):** the function holds its own classifier with the id the macro's
  `if` reads, so each call decides on its own arguments.
- **Pattern B (decided once):** the caller decides, passes the answer in, and a computed node
  inside the function, with the classifier's id, holds it:

  ```
  ---
  { "kind": "computed", "producer": "computed", "label": "Prescribes letrozole (passed in)", "format": "judgement",
    "reproducible": true, "modelDerived": true, "runDependent": false,
    "expression": { "var": "param.letrozole.value" } }
  ---
  ```

  saved as `functions/draft_section/prescribes_letrozole.md`. **Pattern B needs no exception**
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6963556118)):
  the computed node returns `judgement`, the same format the classifier returns
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6982429387)), with producer `computed`; its expression passes the parameter's value
  through, so the parameter is a `judgement` too, and the macro's `if` reads
  `node.prescribes_letrozole.value.answer` under either pattern. It is
  **model-derived** (part 03 § 4.5), because the caller's classifier, an agent, wrote the
  answer. Where the value came from is carried by the flag, not the format, so the macro's
  decision is shown as model-derived in History, as it is under pattern A.
- **A macro's `if` naming a parameter is refused** (part 03 § 4.2).

### 7.11 Example: a made-up package

`example-functions` is made up for this document; the patient is fictional (Margaret Okafor).

```
example-functions/
  manifest.json            identity, and inputs: consult_draft (markdown_block)
  snippets/
    index.json
    standards/             index.json (format standard_section), hpi.md, plan.md, …
  formats/                 index.json, standard_section.json
  renderers/               index.json ("items": [])
  macros/                  index.json ("items": [])
  functions/
    index.json
    draft_section/
      index.json           inputs: standard (standard_section), draft (markdown_block)
      standard_draft.md    agent · plaintext
      patient_draft.md     agent · plaintext
      apply_instructions.md agent · plaintext
      snippets/ macros/ formats/ renderers/    each index.json ("items": [])
  nodes/
    index.json             items: standards, extract_input_terms
    standards/
      index.json           for … in snippet.standards, maxParallel 4, itemLabel "title"
      section.md           call draft_section, with standard ← item, draft ← input.consult_draft.content
    extract_input_terms.md agent · concept_list
  checks/
    index.json
    section_coverage/
      index.json           for … in node.standards.items, itemLabel "label"
      coverage.md          computed · check_result (terms_subset per section)
    all_sections.md        computed · check_result (all_pass)
  deliverables/
    index.json
    emr/
      consult.md           check: check.all_sections
  tests/                   index.json, and the package's tests
```

All nine top-level folders are present, each with its `index.json`, and the function's four
definition folders likewise, empty ones included (part 01).

`functions/draft_section/index.json`:

```json
{ "label": "Draft a section",
  "description": ["Drafts one consult section from a standard, then applies the patient's facts."],
  "inputs": {
    "standard": { "label": "Standard", "description": null, "format": "standard_section" },
    "draft":    { "label": "Consult draft", "description": null, "format": "markdown_block" } },
  "uses": { "snippets": [], "macros": [], "formats": ["standard_section"], "renderers": [] },
  "nodes": ["standard_draft", "patient_draft", "apply_instructions"],
  "ret": "node.apply_instructions",
  "format": "plaintext" }
```

`checks/section_coverage/index.json` (check loops are part 06's; shown for `.items`):

```json
{ "kind": "for", "producer": "computed", "label": "Section coverage", "description": null,
  "format": { "type": "array", "items": { "$ref": "check_result" } },
  "reproducible": true, "modelDerived": true, "runDependent": false,
  "in": "node.standards.items",
  "parallel": true, "maxParallel": null, "until": null, "max": null,
  "itemLabel": "label",
  "nodes": ["coverage"], "ret": "check.section_coverage.coverage" }
```

Its `coverage` check's `if` skips sections that didn't complete:
`{ "===": [ { "var": "item.state" }, "completed" ] }`. `checks/all_sections.md`'s expression:
`{ "all_pass": [ { "var": "check.section_coverage.items" }, "Not every section covers its planned terms." ] }`.

The record's keys for the third standard: `node.standards.section:3` (the call),
`node.standards.section.patient_draft:3`; and `check.section_coverage.coverage:3`, which shares
the production item's id (part 06). The deliverable is `deliverable.emr.consult`.

## 8. Concurrency: what counts toward the per-job ceiling

([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1393#issuecomment-6904683179),
[counting](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6963556118))

- **The ceiling is not in the format.** The engine holds a per-job ceiling: at most *N* agent
  calls in flight for one job (for example 16). It is part 09's. Function bodies run inline in
  the job's orchestration, so their calls are scheduled under the same ceiling ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)).
- **Everything counts the same way:** agent calls in nested loops, inside function calls (at any
  depth), and in check loops ([#1396](https://github.com/Consultologist/Consultologist-Blazor/issues/1396#issuecomment-6905675034)).
  **Only agent calls count:** `rendered` and `computed` nodes make no model call and don't
  count.
- **Total-work limits** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)), engine settings beside the ceiling, starting at: **200 items
  per loop instance** (§ 2.4), **500 agent calls per job** (retries included as one call), and a
  **step budget per render** (part 05). Each is checked at run time and fails the job, or the
  node, with a sentence (part 09 § 3.4); publish notes a package whose worst case can exceed one
  (§ 10.2).
- **Each loop's `maxParallel` still applies within it.** The effective concurrency at a point is
  the smaller of the ceiling and the product of the caps along its nesting path.
- **429 responses and other service failures** are retried with back-off and recorded as retries
  on the node's row. A node fails after 10 minutes of waiting on 429s in total, or after 5
  attempts for another failure, and a job fails if it hasn't finished 30 minutes after its
  orchestration started ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660));
  all three are engine settings (part 09 § 3.4, [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1393#issuecomment-6984396609)). This replaces #1393's first
  rule that a job is never failed for capacity alone.
- **At publish, a note rather than a refusal** when nested caps multiply past the engine's
  ceiling (§ 10.2). The product is taken along each nesting path of loops and calls. For the
  note's worst case, a `maxParallel: null` loop over a list whose length publish knows (a
  snippet folder) counts its length, and over any other list counts as unbounded, which the note
  says. Requiring a non-null `maxParallel` is #1474 (Milestone 40).

## 9. What the record holds

The record is part 08's. For flow and functions (storage 12 adds to 11, with qualified keys):
- **loop rows** carry `parallel`, `maxParallel`, `itemLabel`'s field, the count, `.stopped` and
  its reason; one loop-instance row per inner instance (v30 § 2.6);
- **item rows** keyed by their key with its root and path (`node.sections:2`), named per § 2.7,
  with state and reason;
- **pass rows** for a `while`, its `passes` and `stopped`;
- **a decision** in the job's one `decisions` list for every `if`, `case`, `while`, `until` and
  item condition: its site, the printed condition (or the subject and the branch taken), its
  result, a hash of each value read, whether it was run-dependent, and an `expressionHash`;
  rows link to their decisions by id
  ([part 08](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979794633));
- **a call row** with the function's id, its arguments' hashes, its result's hash and its
  `depth`, expanding to its inner rows like a loop item;
- **`modelDerived`, `runDependent` and `profileCarrying`** on every row (part 03 §§ 4.4–4.6).

## 10. Refusals and notes

The wording below is the starting text. It follows the style rules in [index § 7](index.md#7-refusal-sentences) and is used as written in Milestone 39 and reviewed in #1483 (Milestone 40); every sentence is listed in [refusal-sentences.md](refusal-sentences.md).

### 10.1 Refusals at publish

Placeholders: `<id>` a node, loop, function or call id; `<n>`, `<m>` numbers; `<format>` a type
string (part 02). "decided" means the sentence is quoted from the linked decision; "v30" means it
is v30's built sentence, kept; the rest is new. Every rule below is decided
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6963556118)).

**Every flow node**

| Sentence | Status |
|---|---|
| *"Loop '\<id\>' declares label null; a loop's or branch's label is what the rail, History and the diagram show: write one."* (also `While`, `If`, `Case`) | rule decided ([#1383](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6957670672)) |
| *"\<Kind\> '\<id\>' declares \<member\>, which belongs to \<a for \| a while \| an if \| a case\>."* | v30's form, adapted to `kind` |
| *"\<subject\> returns nothing; name the body node the outside reads with ret."* (`ret` null) | v30 |
| *"\<subject\> returns \<ret\>, which is not one of its own children (\<full names\>): write the child's full name, for example node.sections.paragraphs."* | v30, adapted to full names |
| *"\<subject\> lists node '\<id\>' twice; a body is a set."* | v30 |
| *"\<Kind\> '\<id\>' declares format \<format\>; its ret makes it \<format\>."* | rule decided ([part 01](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)) |
| *"\<Kind\> '\<id\>' declares reproducible \<true \| false\>; its body makes it \<false \| true\>."* (also `Call`) | rule decided ([part 01](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)) |
| *"\<Kind\> '\<id\>' declares \<modelDerived \| runDependent\> \<true \| false\>; its body makes it \<false \| true\>."* (also `Call`) | rule decided ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1478#issuecomment-6982835861)) |
| *"\<Kind\> '\<id\>' \<in \| case \| with \<param\>\> is an expression; it names one readable thing: compute the value in a computed node and name that."* | rule decided ([part 03](https://github.com/Consultologist/Consultologist-Blazor/issues/1386#issuecomment-6962982698)) |

`<subject>` is `Loop 'x'`, `While 'x'`, `If 'x' then`, `If 'x' else`, `Case 'x' branch <n>`,
`Case 'x' else`, or `Function 'x'`.

**`for`**

| Sentence | Status |
|---|---|
| *"Loop '\<id\>' has no body; name the nodes that run per item in nodes."* | v30 |
| *"Loop '\<id\>' declares parallel true with maxParallel 1, which runs one item at a time; declare "parallel": false and "maxParallel": null instead."* | decided ([#1392](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6877395680)) |
| *"Loop '\<id\>' declares maxParallel \<n\>; the most is FormatBounds.parallel (15)."* | decided ([#1392](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6904683449)) |
| *"Loop '\<id\>' declares maxParallel \<n\>; the least is 2, or null for no cap of its own."* (0, negative, not an integer) | rule decided |
| *"Loop '\<id\>' declares maxParallel \<n\> with parallel false; a sequential loop runs one item at a time: write "maxParallel": null."* | rule decided ([#1392](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6877359197)) |
| *"Loop '\<id\>' declares until with parallel true; until stops after the item where it holds, which needs items in order: declare "parallel": false and "maxParallel": null."* | rule decided ([#1392](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6877352714)) |
| *"Loop '\<id\>' declares max 0; the least is 1."* | v30 |
| *"Loop '\<id\>' iterates \<reference\>, which is \<format\>; only a list can be iterated."* | v30's input sentence, generalised |
| *"Loop '\<id\>' iterates item.value.\<field\>, but it runs inside no loop; item.value names the enclosing loop's item."* | v30 |
| *"Loop '\<id\>' iterates node.\<y\>.value.\<z\>; \<y\> runs inside loop '\<l\>' — a loop iterates a node outside it, or in an enclosing loop's body."* | v30 |
| *"Loop '\<id\>' itemLabel '\<field\>' is not a field of the elements of \<reference\> (fields: \<fields\>)."* | v30's `labelField` sentence, renamed |
| *"Loop '\<id\>' itemLabel '\<field\>' may be missing or null; itemLabel names a field every element has as a string."* | rule decided ([#1392](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6880614681)) |
| *"Loop '\<id\>' itemLabel '\<field\>' is \<format\>; itemLabel names a string field."* | rule decided |

**`while`**

| Sentence | Status |
|---|---|
| *"While '\<id\>' declares max null; a while must declare its bound (1 to 15)."* | decided wording "…a while must declare its bound (1 to 15)" ([#1392](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6897363567)); the `null` case added |
| *"While '\<id\>' declares max \<n\>; a while runs at most FormatBounds.passes (15) passes."* | decided ([#1392](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6904683449)) |
| *"While '\<id\>' declares max \<n\>; the least is 1."* | v30 |
| *"While '\<id\>' has no body; name the nodes that run per pass in nodes."* | v30 |
| *"Node '\<id\>' in while '\<w\>' is neither returned nor read; name it with ret, read it from a body node or from the check."* | v30 |
| *"\<site\> reads pass.number, but it runs inside no while; pass is a while's pass."* | v30 |
| *"\<site\> reads pass.parent, but its while runs inside no while; pass.parent is the enclosing while's pass."* | v30 |
| *"\<site\> reads pass.previous.node.\<id\>, which is not in while '\<w\>'; a pass reads the while's own nodes."* | v30 |

**Reads across levels**

| Sentence | Status |
|---|---|
| *"\<site\> reads node '\<id\>', which runs inside loop '\<l\>'; read node.\<l\>."* | v30 |
| *"\<site\> reads item.parent, but its loop runs inside no loop; item.parent is the enclosing loop's item."* | v30 |

**`if` and `case`**

| Sentence | Status |
|---|---|
| *"If '\<id\>' declares then null; an if runs a then body."* | v30's sentence, adapted |
| *"Case '\<id\>' has no branches; a case runs one branch per set of values."* | v30's sentence, adapted |
| *"Case '\<id\>' branch \<n\> lists no value; a branch's when names one or more values."* | v30, adapted |
| *"Case '\<id\>' switches on \<reference\>, which is \<format\>; a case switches on an enumerated string."* | rule decided ([#1392](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6963556118)) |
| *"Case '\<id\>' branch \<n\> lists "\<value\>", which is not one of \<reference\>'s values (\<values\>)."* | rule decided |
| *"Case '\<id\>' lists "\<value\>" in branches \<n\> and \<m\>; a value belongs to one branch."* | rule decided |
| *"Case '\<id\>' doesn't list \<values\> and has no else; a case has a branch for every value: add a branch, an empty branch, or an else."* | rule decided ([#1392](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6963556118), [empty branch](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)) |
| *"Case '\<id\>' switches on \<reference\>, which can be null, and has no else; a null subject goes to the else: add one."* | rule decided ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)) |
| *"Case '\<id\>' branch \<n\> lists null; a null subject goes to the else."* | rule decided ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)) |
| *"Case '\<id\>' branch \<n\> writes nodes \<[] \| ids\> with ret \<ret\>; an empty branch writes nodes [] and ret null together."* | rule decided ([#1385](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)) |
| skip reason *"case '\<id\>': arm for \<value\> does nothing"* | decided ([#1385](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)) |

**Functions and calls**

| Sentence | Status |
|---|---|
| *"Node '\<id\>' in function '\<f\>' reads input.\<name\>; a function reads only its parameters: add a parameter and pass it in with."* | decided ([#1419](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6888201229)) |
| *"Node '\<id\>' in function '\<f\>' reads \<reference\>; a function reads only its parameters: add a parameter and pass it in with."* (outer `node.*`, a caller's `item` or `pass`, `run.*`, `check.*`, `profile.*`) | the decided sentence, generalised |
| *"Function '\<f\>' uses \<snippet \| macro \| format \| renderer\> '\<id\>' but doesn't list it in uses."* | decided ([#1419](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6888253558)) |
| *"Function '\<f\>' has its own \<snippet \| macro \| format \| renderer\> '\<id\>' and also uses the package's: rename one."* | decided ([#1419](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6897717122)) |
| *"Function '\<a\>' calls '\<b\>', which calls '\<a\>': functions may not call themselves."* | decided ([#1419](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6897233071)); for a longer cycle the chain is named in full |
| *"Function '\<a\>' calls '\<a\>': functions may not call themselves."* | rule decided (the direct case) |
| *"Call '\<id\>' reaches depth \<n\>; calls nest at most FormatBounds.depth (15) deep."* | decided ([#1419](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6904683688)) |
| *"Function '\<f\>' returns node.\<id\>, an if with no else: add an else, or return a node that always runs."* | decided ([#1419](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6897508423)) |
| *"Function '\<f\>' returns node.\<id\>, which runs inside if '\<b\>' and may be skipped: return a node that always runs."* | rule decided (a non-exhaustive `case` is refused for itself, § 5.2) |
| *"Function '\<f\>' returns node.\<id\>, a case with an empty branch for \<values\>: return a node that always runs."* | rule decided (§ 7.6) |
| *"Function '\<f\>' returns node.\<id\>, whose branches return \<format\> and \<format\>: a function returns one format. Wrap them in an object format."* | decided ([#1419](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6897508423)) |
| *"Function '\<f\>' declares format \<format\>; its ret '\<id\>' returns \<format\>."* | rule decided |
| *"Function '\<f\>' holds a \<checks \| deliverables\> folder; checks and deliverables live only at the package's top level: return a check_result or a value instead."* | rule decided ([#1419](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6897233071)) |
| *"Macro '\<m\>' is placed in function '\<f\>', where its if reads node.\<id\>, which doesn't exist there: add a classifier with that id (pattern A), or pass the decision in and hold it in a computed node (pattern B)."* | decided ([#1419](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6897801178)), with "a let" renamed "a computed node" (#1385) |
| *"Call '\<id\>' names function '\<f\>', which the package doesn't define."* | rule decided |
| *"Call '\<id\>' passes no \<param\>; function '\<f\>' takes \<params\>."* (a parameter left out, even an optional one: pass null) | rule decided |
| *"Call '\<id\>' passes \<param\>, which function '\<f\>' doesn't take (\<params\>)."* | rule decided |
| *"Function '\<f\>' parameter '\<param\>' declares \<required \| channel\>; a parameter has label, description and format, and every one is passed."* | rule decided ([#1392](https://github.com/Consultologist/Consultologist-Blazor/issues/1392#issuecomment-6963556118)) |
| *"Call '\<id\>' passes \<reference\> (\<format\>) as \<param\>; function '\<f\>' takes \<format\>."* | rule decided |
| *"Function '\<id\>' reads param.\<name\>, which carries profile.\<field\>, in \<a prompt \| a part \| a condition\>; the profile is read only in deliverables and macro bodies."* (part 06 R-F3) | decided ([#1408](https://github.com/Consultologist/Consultologist-Blazor/issues/1408#issuecomment-6959886233), [#1419](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6959886713)) |
| *"Call '\<id\>' declares format \<format\>; function '\<f\>' returns \<format\>."* | rule decided |
| *"Call '\<id\>' has a body; a call is a header only."* | rule decided |

### 10.2 Notes and warnings at publish

| Sentence | Kind | Status |
|---|---|---|
| *"Up to \<n\> agent calls could run at once; this engine runs at most \<N\> per job, so the rest wait their turn."* | note | decided ([#1393](https://github.com/Consultologist/Consultologist-Blazor/issues/1393#issuecomment-6904683179)) |
| *"An unbounded number of agent calls could run at once (loop '\<id\>' has no cap over a list known only at run time); this engine runs at most \<N\> per job, so the rest wait their turn."* | note | rule decided (§ 8) |
| *"Loop '\<id\>' iterates \<reference\>, which could hold more than \<limit\> items; this engine runs at most \<limit\> per loop, and fails the loop beyond that."* | note | rule decided ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)) |
| *"Up to \<n\> agent calls could run in one job; this engine runs at most \<limit\> per job, and fails the job beyond that."* | note | rule decided ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)) |
| *"Function '\<f\>' lists \<snippet \| macro \| format \| renderer\> '\<id\>' in uses but never uses it."* | warning | rule decided ([#1419](https://github.com/Consultologist/Consultologist-Blazor/issues/1419#issuecomment-6888253558)) |

## Proposed in this part

Nothing. Every point is decided; refusal sentences are used as written and reviewed in #1483 ([refusal-sentences.md](refusal-sentences.md)).
