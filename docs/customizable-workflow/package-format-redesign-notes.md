# The package format, redesigned: working notes (Milestone 39)

> **Status:** working notes, not yet a design. These notes record the decisions made with the
> package author on 2026-11-13 while reviewing the package's structure as a whole, and the
> questions still open. Each decision is filed as a Milestone 39 issue, which is where it is
> argued:
> - #1383, nodes as files;
> - #1384, namespaces;
> - #1385, kind and format;
> - #1386, conditions as JSON Logic.
>
> The inputs carried in are #1382 (nested headings), #1184 (macros as a map) and #1061 (enum
> value labels). A design document (with a version number) follows once the review is done; where
> it and these notes disagree, the design will say which decision it supersedes.

**Why.** The format grew version by version from v5 to v30. Each version was a reasonable step, but
together they left:
- a node split between a manifest entry and a prompt file;
- a `format` field that mixes how a node runs with what it returns;
- headers no tool recognises;
- one flat list holding both the package's nodes and its structure.

This milestone revisits the structure with what has been learned since.

## 1. Nodes become files (#1383)

- **A leaf node is `nodes/<id>.md`:** a JSON header (what the manifest's `nodes` entry holds
  today), then a body. `prompts/` is retired. A prompt could not be shared by two nodes since 28
  anyway, and reused text lives in `snippets/`.
- **A leaf's body holds the Scriban that produces it:** an agent node's prompt (a template), a
  `let`'s expression (a bare Scriban expression, not a template; a body wrapped in `{{ }}` is
  refused). A computed node's and a check's body is empty.
- **A flow node is a folder:** `for`, `while`, `if` and `case` are each `nodes/<id>/`, described by
  an `index.json`. Its members sit inside it, at any depth, so the tree on disk is the package's
  shape. A folder has no body, so a flow node's expressions live in its `index.json`.
- **`if`/`case` branches sit flat in their folder.** `index.json` names which ids belong to which
  branch: `"then"` / `"else"`, or a case's `"branches": [ { "when": [values], "nodes", "ret" } ]`.
- **`index.json` for every folder** (snippet folders, macro folders, flow nodes): one shape, plain
  data. This keeps v28's choice over a folder `_.md` (#1269), extended to flow nodes. Every listed
  id is a child of the folder, and every child is listed exactly once.
- **The manifest keeps a top-level `"nodes": [ids]` list,** in order, like `macros` and `snippets`.
- **Ids stay unique and `node.x` is unchanged** at any depth, never `node.folder.x`. A path in the
  reference would tie templates to the file layout and clash with member access (`node.x.value`).
- **Hashes:** a node's `inputHash` stays the SHA-256 of its rendered prompt. The record's prompt
  path moves (`nodes/…/x.md`), which is a record-storage and provenance change.

### Front matter

Every Markdown file with a header (nodes, macros, snippets, folder items) uses **JSON between `---`
lines**:

```
---
{ "label": "Identifying primary problem", "kind": "agent", "format": "concept_list", "onInvalid": "fail" }
---
{{ snippet.snomed_tool_guidance.content }}
Identify the primary disease or problem concept …
```

- **Why JSON between `---` lines.** JSON is valid YAML (from YAML 1.2), so Markdig and GitHub
  recognise the block as front matter, and we parse it with our own JSON parser. The bare JSON
  object used today is recognised by no tool.
- **Both lines are needed.** Front-matter tools require the file to *start* with `---`, and a lone
  closing `---` under text is a Markdown heading underline.
- **Rejected: Scriban's `+++` front matter.** It is code that runs. In a test with Scriban 7.2.5, a
  bare object between `+++` lines was printed into the output, and its values could be read only
  by executing assignments.
- **Rejected: full YAML.** It would add a YAML dependency and YAML's quirks.
- **Rejected: Scriban assignments as the header.** The header must be known without running
  anything: the validator, registry, Editor and diagram all read it before a run. It would also
  mean executing author code in the registry, and the Editor could not write it back.
- **The rules:**
  - exactly `---` on the first line and on the closing line;
  - no byte-order mark;
  - one JSON object, with no duplicate keys;
  - a file may end right after the closing `---` (header-only);
  - **a body that is only whitespace is empty**;
  - a body with content is kept byte for byte, since it becomes the rendered prompt.

## 2. Namespaces (#1384, built after #1383 in this milestone)

- **Disjoint scopes only, no shadowing.** An id may repeat only where the two nodes are never both
  visible from the same place (two sibling loops each holding a `draft`). A body node may not
  reuse an id its enclosing bodies or the top level make visible. Every `node.x` has exactly one
  candidate.
- **No new syntax: `node.x` resolves outward.** The format's visibility rules (siblings, enclosing
  bodies aligned by path, the top level) are already lexical scope, and reading into another
  loop's body is already refused. So nothing a valid template reads ever needs qualifying.
- **Qualified keys in the record,** with `.` as the separator: `parts.paragraphs.draft_paragraph:2/1`.
  Item paths already use `/` and the row key uses `:`. This touches record keys, events, checks'
  `with`, classifier gates, macro placements, `pass.….node.<id>`, the engine and the pages.
- **Compatible:** a package with unique ids is already valid and unchanged.

## 3. Kind and format (#1385)

- **Every node declares its `kind`** (what it is): `agent`, `rendered`, `computed`, `let`, `for`,
  `while`, `if` or `case`. It is not called `type`, because `type` already means an input's type.
- **Every node declares its `format`** (what it returns, like a function's return type). `producer`
  leaves the format files.
  - **Built-ins complete JSON's primitives:** `string`, `number`, `boolean`.
  - **`prose`** is model-written text, which may vary between runs.
  - **Object formats** are marked model-written (`x-generated`) or not.
  - **Inputs' `text` becomes `string`.**
- **Which formats each kind returns:**
  - **`agent`:** `prose`, or a generated object;
  - **`rendered`:** `string`;
  - **`let`:** `string`, `number`, `boolean` or a non-generated object (this replaces v30's `type`);
  - **`computed`:** written and checked against its operation.
- **Operations are named apart from their formats:** `op: terms_subset` returns
  `format: check_result`, so future checks share a return type, and `with` holds the operation's
  parameters.
- **Flow formats:**
  - **`while`:** its `ret`'s format;
  - **`if`/`case`:** a union of its branches' formats, readable only through what every member
    shares;
  - **`for`:** a list of its `ret`'s format.
- **`reproducible`:** implied `true` for `rendered`, `let` and `computed` (`false` is refused); an
  author's claim for `agent`; derived for flow nodes.

### The notation: JSON Schema in the file, type strings for display

A node's `format` is written in **JSON Schema's own vocabulary**, so standard tools can read every
node's return type. People see it as a type string.

| Meaning | In the file | Displayed as |
|---|---|---|
| a primitive | `{ "type": "number" }` | `number` |
| a named format | `{ "$ref": "#/$defs/prose" }` | `prose` |
| a list (a `for`) | `{ "type": "array", "items": { "$ref": "#/$defs/prose" } }` | `list<prose>` |
| nested loops | `{ "type": "array", "items": { "type": "array", "items": { "$ref": "#/$defs/prose" } } }` | `list<list<prose>>` |
| a union (an `if`/`case`) | `{ "anyOf": [ { "$ref": "#/$defs/prose" }, { "$ref": "#/$defs/classification" } ] }` | `prose \| classification` |

- **Named formats are `$defs`** in a per-package bundle the registry publishes, with a stable `$id`.
- **Model-written is an annotation** (`"x-generated": true`). Standard validators keep it and
  ignore it while checking shape; our validator enforces the kind rules from it.
- **A declared 2020-12 dialect.** Today's subset (`type`, `properties`, `required`,
  `additionalProperties: false`, `items`, `enum`, `title`, `description`, `$schema`) plus `$ref`,
  `$defs`, `anyOf` and `x-generated`, published as a meta-schema. Anything else is refused at
  publish.
- **The display:** the Editor's pickers and panes, the validator's sentences and the docs render
  the schema as type strings.
- **Considered and set aside:**
  - type strings in the file (`"list<prose>"`): a second grammar, invisible to JSON Schema;
  - our own JSON keys (`{ "list": … }`, `{ "union": … }`): readable, but not a standard.
- **Open:** whether the model provider's strict mode accepts `$ref`, `$defs` and `anyOf` in an
  agent's format (node return types are never sent to a model).

## 4. Conditions: Scriban strings or JSON Logic (#1386)

The question: should the header's **expressions** follow formats into structured JSON, displayed as
strings, so the whole front matter is data?

**What the strings hold today.**
- **Conditions:**
  - which: an `if`; a `while` check; an `until`; a node's or deliverable's `when`; a macro's `if`;
    a folder item's `if`;
  - what: since 25, each is a full Scriban boolean expression: comparisons, `&&`, `||`, `!`,
    literals, references, and pipes and builtins such as `(input.docs | array.size) > 0`;
  - how they're checked: walked by `WorkflowConditionAt25`, with reads becoming edges and
    comparisons typed;
  - in the record: a condition that does not hold gives its own text as the reason.
- **A `for` source and a `case` subject** are references.
- **A `let`'s expression** is its body, so it is out of scope here.

**The caveat that frames the choice.** The header is already fully JSON-Schema-validatable with
expressions as strings. What JSON Schema can't see is *inside* an expression: whether `node.scope`
exists, whether a comparison is well-typed, whether a read is allowed from here. A JSON tree
doesn't change that: `{ "==": [ { "var": "node.scope.value" }, "in_scope" ] }` is structurally
valid whether or not `node.scope` exists. Those checks are semantics and remain the validator's in
every option. The choice is about **how much structure outside tools can see**.

**Option A: Scriban strings, references tidied.**
- Conditions stay Scriban text, checked by the walker.
- Header and body share one expression language: the same `node.scope.value == "in_scope"` in a
  header's `if` and a template's `{{ if }}`.
- Pure references become plain fields (`"in": "node.plan.value.sections"` for a `for`), checkable by
  a schema pattern.

**Option B: conditions as JSON Logic, displayed as strings.** JSON Logic (jsonlogic.com) is a
standard with evaluators in many languages.

| In the file | Displayed as |
|---|---|
| `{ "==": [ { "var": "item.value.kind" }, "narrative" ] }` | `item.value.kind == "narrative"` |
| `{ "!=": [ { "var": "node.judge.value.verdict" }, "good" ] }` | `node.judge.value.verdict != "good"` |
| `{ ">": [ { "count": { "var": "input.docs" } }, 0 ] }` | `(input.docs \| array.size) > 0` |

- **For:**
  - every read is an explicit `var`, so tools find dependencies without parsing Scriban;
  - standard evaluators exist, so a third party could evaluate when a deliverable fires;
  - the header holds no code;
  - it is consistent with the format notation.
- **Against:**
  - **two condition languages,** because template bodies keep Scriban's `{{ if }}`;
  - **coverage:** JSON Logic has no Scriban builtins (`array.size`, string functions, pipes), so
    the format defines custom operations, a dialect again;
  - **the widest churn yet:** every condition in every package, a second evaluator, a printer for
    the record's reasons, rewritten conformance cases;
  - **heavy to write by hand.**

**Where it stands.** A was recommended: the standard's real gain for formats is validating
*outputs*, while for conditions it mainly makes *reads* visible, which `dag.mmd`, the derived edges
and the record's `reads` already do. The package author chose to file **B** as its own issue,
#1386, as the way to let outside tools read and evaluate a package's conditions. Open in #1386:
- the operation set and each operation's typing and absence rules;
- a byte-stable printer (record reasons are recorded);
- the walker over `var` paths;
- the `for` source and `case` subject;
- the migration;
- one evaluator or two;
- the Editor's builder writing the tree.

## Carried in

- **#1382:** a nested loop's items compose as `## Paragraphs N`. `labelField` would name them but
  would keep clinical text in the record, and nothing lets an item go unheaded.
- **#1184:** macros as a map in memory too.
- **#1061:** enum value labels, `{ value, label }`, including #1014's points (closed as its
  duplicate). The open question there is whether classification answers take labels.

## Still to decide (across the milestone)

- The spec version(s) these land in, and whether #1383, #1385 and #1386 share one.
- The exact validator sentences, and whether a stray child file is refused or warned.
- Whether a blank line right after the closing `---` belongs to the body.
- Whether an inline schema may be a `let`'s or computed node's format.
- The name of the model-written annotation and the `$defs` bundle's `$id` scheme.
- The provider's strict-mode support for `$ref` and `anyOf` in agent formats.
