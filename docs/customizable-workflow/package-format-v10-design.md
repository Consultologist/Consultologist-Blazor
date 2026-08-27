# Package format v10: the shape of a job known later, an evaluator, and depth — design

**Status: design record for Milestone 21 (settled 2026-08-26), implementation
tracked by the ladder in § 14.** v10 takes the three roads v9 § 13 mapped and
declined — conditions that read node output (#336 → #451), `and`/`or` and
arithmetic (#338 → #469), and nested structure (#340 → #470) — as one
version. Unlike v8 and v9, none of the three had its trigger fire; § 2
records that plainly. The candidates v10 does not take are kept in § 13.

Decisions taken with the operator: the three are **designed and shipped as
one version**; a **classifying node** decides the fire set at a **boundary**
after the classifiers settle, and `TotalBlockCount` stays a stored scalar
stamped once — what moves is *when*; a condition is a **full expression**
with `and`, `or`, `not`, parentheses and arithmetic, evaluated by an
evaluator with a stated order of operations; typed inputs nest to
**unbounded depth**; the design record is written first, the normative
specification and conformance set follow the engine, as v9's did.

## 1. Motivation

Three ceilings, each named by an earlier record and each left standing on
purpose:

- **The shape of a job is known before it starts.** v8 § 5 made that the
  load-bearing decision — the fire set is decided once, at start, against
  the supplied inputs — and #336 asked for the opposite and was declined.
  The cost is a class of workflow the format cannot express: *if the note
  concludes the referral is out of scope, produce a decline letter instead
  of a plan.* Asking the clinician at intake is not a substitute, because
  the classification *is* the work.
- **A condition is one operand, one operator, one literal.** v8 refused
  `and`; v9 widened to six operators, paths and `count()` and refused `and`
  again, predicting the request. A workflow that needs two declared inputs
  read together for one deliverable splits into two deliverables or a
  synthetic enum input — both are the author working around the grammar.
- **Structure is one level deep.** v9 § 4's bound keeps canonicalisation
  finite, the form a row and a path two segments. A medication list where
  each entry carries its own dose schedule, or a family history grouped by
  relative, cannot be declared; the author flattens or concatenates, which
  is v7's motivation two levels up.

They are one version for v9's reason: *"they change the same manifest, the
same validator closure and the same hash family, and designing them apart
risks two conflicting revisions."* All three revise the condition grammar
(a node operand, an expression, a longer path); two revise the input model;
two move the effective-input hash; all three touch the editor's pickers.

## 2. The trigger, and that it did not fire

v8 was *"overridden by product decision"*; v9's trigger *"was met"*. v10's
was not, and this record should not read otherwise. **No published package
needs a classifier, a compound condition, or a second level.** Each of
#451, #469 and #470 named its trigger and said *not a commitment*.

The operator's decision (2026-08-26) is to build ahead of the trigger, for
three reasons, recorded here so a later reader weighs them rather than
finding a bar that reads as cleared:

1. The roads are mapped. v9 § 13 wrote the classifier's shape in detail
   and named the cost of the other two precisely; the design work left is
   the reconciliation, not the discovery.
2. The format is the product's grammar. Every earlier version was pulled
   by a package; this one is pushed by the observation that authors shape
   packages to what the grammar allows, so the trigger for an
   inexpressible workflow tends not to fire.
3. Deferring three times has a cost of its own: three records, three
   "still out of scope" lists, and an editor whose pickers describe a
   smaller language than the engine could run.

**Two of the three reverse reasons given twice.** v8 § 5 and v9 § 6 refused
an evaluator because *"an evaluator is a thing with an order of operations,
and a manifest is content an operator cannot review line by line"*; v9 § 4
bounded depth because nesting *"would make canonicalisation recursive, the
intake form a tree, and a path arbitrary-length, which is an expression
parser wearing a schema's clothes."* Those reasons were correct and are
still true. v10 accepts their costs deliberately: the order of operations
is stated in § 6 and pinned by conformance cases so it is a published fact
rather than a surprise; the operator's review of a manifest is what the
validator's refusals and the pre-publish desk check are for, and both name
every clause; recursion is bounded by the manifest the author wrote, not by
the caller. This is the *deliberate later step with its own bump* v9
asked for, never a quiet loosening.

## 3. Vocabulary

- **Classifier**: a node whose output is one value of a declared set, and
  which a condition may read.
- **Boundary**: the moment, after every classifier has settled, when the
  fire set is computed and `TotalBlockCount` is stamped.
- **Deciding**: the stage before the boundary; **producing**: after it.
- **Clause**: one comparison or truthiness test — v9's whole grammar.
- **Expression**: clauses joined by `and`/`or`, negated by `not`, grouped by
  parentheses, over arithmetic terms.
- **Path**: a dotted reference of any length into structure,
  `<input-id>(.<field-id>)*`.
- **Depth**: how many `object`/`array` layers a declaration nests.

## 4. The classifying node (normative)

### Declaration

A node gains an optional `kind`. Absent, the node is what it always was: a
prompt node, or an aggregator when `aggregate` is present. `kind: classifier`
declares a classifying node:

```yaml
nodes:
  - id: scope
    kind: classifier
    label: Is the referral in scope?
    prompt: prompts/classify-scope.md
    bindings:
      referral: input:consult_draft
    values: [in_scope, out_of_scope, needs_information]
```

- `values` is required on a classifier and forbidden elsewhere: at least
  two, unique, `^[a-z][a-z0-9_]*$` — the enum-input rule.
- A classifier declares `prompt` and `bindings` like a prompt node, never
  `aggregate`, never `forEach` (a classification is one answer; a fanned
  classifier would be a list, which is a different thing — § 13).
- A classifier's `output` is implied: the **`classification`** contract
  (§ 4, *The contract*). Declaring `output` on a classifier is refused.
- A classifier may bind inputs and other classifiers (`node:<classifier>`),
  never a prompt node or an aggregator: the classifier closure must be
  runnable before anything is produced, or the boundary is unreachable.
- A prompt node may bind a classifier (`node:scope`); the value renders as
  its text. An aggregator may not aggregate one.
- `kind` and `values` on a manifest below 10 are refused naming the
  version: *`Node 'scope' declares kind, which requires specVersion 10.`*

### The contract

The output-contract catalog matches schemas exactly, so the value set
cannot be the schema. `classification` is one new catalog contract: a JSON
object with a single required string member `value`. The engine parses the
answer, trims it, lower-cases it, and requires it to be one of the node's
`values`; anything else is the node failing, with the sentence *`Classifier
'scope' answered outside its values.`* — the answer itself is never printed,
it is model output over the referral. The agent behind the contract is
selected as every contract's is, by schema.

> **Amendment, 2026-08-27 (#495).** The agent is generic — one agent per
> contract — and the values live in the manifest, so the engine appends a
> fixed trailer to the rendered prompt: *`Answer with exactly one of: a, b,
> c.`* (values in declared order). The message sent, trailer included, is
> what the node's input hash covers. The agent: gpt-5.6-sol, reasoning
> effort low, no tools. A malformed or out-of-set answer is retried under
> the agent activity policy before the node fails.

### Evaluation

A classifier runs exactly as a prompt node runs — one activity, the same
retry policy, the same node hash — and its output hash is the SHA-256 of
the normalised value. Its value is available to conditions from the
boundary on and to prompts thereafter.

## 5. The boundary and #176 (normative)

#176's rule is *stamped once, never recomputed, never goes backwards*. v10
keeps every word of it and moves only the moment.

**At start** the starter validates inputs, resolves fans, and — new —
partitions nodes into the classifier closure (classifiers and what they
bind) and the rest. `Initialize` records the nodes, the inputs, the
classifier roster, and `TotalBlockCount` as **absent**: a named state,
*not yet decided*, distinct from the stated zero of a job born Failed
(#434). A package with no classifiers has an empty deciding stage: the
boundary is at start, `Initialize` stamps the count as it does today, and
every payload the engine writes is **byte-identical to v9's** — the
`DurablePayloadReplayTests` bytes are the pin, and the new fields are
trailing optionals on `ConsultGenerationJobInitialize` and the
orchestration input.

**The deciding stage** runs the classifier closure through the ordinary
ready loop; nothing else is scheduled. Progress is the classifiers': *n of
m decided*. The Consults rail shows Setup → **Deciding what to produce** →
Producing; the deliverable panel says *not yet decided* in words. A
classifier that fails ends the job in stage one: a terminal row named
*Failed — could not decide what to produce*, with the node's own sentence
and no count (`totalBlockCount` absent, and History says so).

**At the boundary** the orchestrator evaluates every `when` once, against
the supplied inputs and the classifier values, exactly as the starter does
today for inputs alone; narrows the results to the fire set; prunes the
nodes to the closure of the firing deliverables (#355's rule, now applied
here); expands blocks; and signals one write-once entity operation,
`Decide`, carrying the fire set, the skipped set with each condition's
sentence, the block skeleton and the count. `Decide` stamps
`TotalBlockCount` first-writer-wins and refuses a second call; the
orchestrator reads it back on replay. Determinism holds because the
classifier values come from replayed activity outputs and the evaluation
is pure.

**An empty fire set at the boundary** is the empty-fire-set case (#434)
one stage later: the job ends Failed with the sentence *`No document
applies after classification: <the skipped deliverables and what each
wanted>`*, `totalBlockCount` a stated zero, and — new — the classifier
values on the record, because a declared value is printable (§ 6, *Explain*).

**The record** carries the classifier values (`classifications:
{ scope: out_of_scope }`), the fire set and the skipped set as v8 records
them, and the boundary time. Provenance § 8.

## 6. Conditions (normative)

### Grammar

```
when        := <expression>                       (a string)
expression  := <or-expr>
or-expr     := <and-expr> ( "or" <and-expr> )*
and-expr    := <not-expr> ( "and" <not-expr> )*
not-expr    := "not" <not-expr> | <clause>
clause      := "(" <expression> ")"
             | <term> <cmp> <term>
             | <truthy>
cmp         := == | != | > | < | >= | <=
term        := <sum>
sum         := <product> ( ("+" | "-") <product> )*
product     := <atom> ( ("*" | "/") <atom> )*
atom        := <operand> | <literal> | "(" <sum> ")" | "-" <atom>
operand     := <path> | count(<path>) | node:<node-id>
path        := <input-id> ( "." <field-id> )*
truthy      := <path>                              (boolean or array, as v9)
literal     := true | false | <enum-value> | <number> | YYYY-MM-DD
```

**Order of operations**, highest first: parentheses; unary minus;
`*` `/`; `+` `-`; comparison; `not`; `and`; `or`. Stated here, pinned by
conformance cases, and rendered by the editor with explicit grouping so an
author never relies on it.

**Every v9 condition is a v10 expression, byte for byte**: one clause, no
operator words. The v9 conformance cases are the regression corpus for the
parser (§ 14, step c).

> **Amendment, 2026-08-27 (#494).** Two rules the implementation fixed:
> **(1) the whitespace rule** — the arithmetic operators `+ - * /` are
> tokens only with whitespace on both sides, so `-1` and `2026-1-1` are one
> token, a literal, and the v9 sentences about them (*not a whole number*,
> *not a date written YYYY-MM-DD*) are produced by the v9 rules byte for
> byte; `seen_on - 7` is subtraction. **(2) the compound sentence** as
> implemented: *needs (length_of_stay to be > 7 and count(prior_notes) to
> be > 0); length_of_stay is not, count(prior_notes) is 2* — what each
> clause wanted, then what each found, under the same no-PHI rule per
> clause; an arithmetic clause prints its terms by name (*needs
> length_of_stay - 2 to be > 5; it is not*). A `node:` clause evaluates
> against the classifications the boundary supplies (step e); until then it
> is absent — never held, even negated — and its sentence says *not decided*.

### Types

- Arithmetic is defined over `number` and `count()`; a `date` admits
  `± <integer>` days and nothing else; text, enum, boolean and node values
  admit no arithmetic. Division by zero makes the clause not hold and is
  named at start (*`… divides by zero`*), never a job failure mid-run.
- Comparison: ordering for `number`, `date` and `count()`; equality for
  enum, boolean and `node:`; **text is still never compared**, and a whole
  object or array is not compared — its fields, or its count, are.
- A `node:<id>` operand names a classifier; its literals are that node's
  `values`; it admits `==` and `!=` only — no ordering, no truthiness, no
  `count()`. A `node:` naming a non-classifier is refused at publish.
- A path of any length reaches a field; the field's type is what the
  operand has. `count(<path>)` counts the array at the end of a path.
- Literals are held to the operand's type, as v9.
- Below 10: `and`/`or`/`not`, parentheses, arithmetic, a path longer than
  two segments, and `node:` are each refused naming the version, in the
  v9 sentence shape: *`Result 'letter' condition uses 'and', which requires
  specVersion 10.`*

### Evaluation

Once, at the boundary (§ 5), against the supplied inputs and the classifier
values. Absence is three-valued as v9: a clause over an absent optional does
not hold, and does not error; `not` over an absent clause does not hold
either (absence is not falsity); `count()` of an absent array is zero — the
one exception, kept. `and`/`or` combine held/not-held; an absent clause
counts as not held on both sides.

### Explain

The sentence reaches History and the email door's reply, so v9's rule holds
per clause: it prints a declared enum value, `true`/`false`, a classifier's
value (declared, so printable), the condition's own literals and a count of
entries — **never a number, a date or a field's value the patient supplied**.
A compound sentence names each clause and whether it held: *`needs
(scope to be 'in_scope' and count(prior_notes) to be > 0); scope is
'out_of_scope', prior_notes has 2 entries`*. Arithmetic terms are printed as
written, with the patient's operands replaced by their names.

## 7. Nested structure (normative)

### Declaration

The one-level bound is lifted: a field's `type` may be `object` or `array`,
`items` may be `array`, and `fields` and `items` recurse with the same
vocabulary at every level. Depth is bounded only by the manifest the author
wrote.

> **Amendment, 2026-08-27 (#492).** `items` is a type name in v9 —
> `items: text` — and a type name has nowhere to say what an inner array
> holds. At 10 `items` may also be an **element spec**: `items: { type:
> array, items: text }`, or `{ type: object, fields: […] }`, or `{ type:
> enum, values: […] }`, recursively. A spec that is only a type writes the
> string form, so every v9 manifest round-trips byte for byte; the object
> form below 10 is refused by name (*`declares items as a shape, which
> requires specVersion 10`*). When `items` is a spec it carries the
> element's own `fields` and `values`; the array declares neither.
>
> **Amendment, 2026-08-27 (#493).** The wire admits depth, so it needs a
> bound the manifest cannot give an *undeclared* value: the converter
> refuses structure nested deeper than **eight** levels as a shape error
> (400), and the door applies its per-array and per-object caps at every
> level plus one total — **4,096 values per input** — so depth cannot
> multiply the worst case. Refused, never truncated. Whether a declaration
> admits a value's shape in a given slot stays the starter's 422, with the
> path spelled: *element 1 field 'contact' field 'phone' is a text and …*. A field declared with structure below 10 is refused naming the
version: *`Input 'medications' field 'schedule' declares type 'object', which
requires specVersion 10.`*

```yaml
inputs:
  - id: medications
    type: array
    items: object
    fields:
      - id: name
      - id: schedule
        type: object
        fields:
          - { id: dose, type: number }
          - { id: times, type: array, items: text }
```

### Wire form and canonical form

The v9 table applies at every level. Canonicalisation is **effective-input
hash definition 6**: definition 5's rules — ordinal-sorted keys, arrays in
supplied order, numbers as spelled, UTF-8 as-is, absent optionals omitted
— applied recursively. The converter's depth site becomes a stack; a
structured field is accepted where its declaration says so and refused
with the same 400/422 split otherwise. The two-element publish-time probe
recurses: every array at every level probes as two elements.

### Rendering, intake and the email door

Templates see nested objects and arrays as Scriban does natively; an absent
optional at any level renders as v9's amendment says — empty object, empty
array, `null` number — via the same declaration map, now recursive.

The intake form becomes a tree: an object field renders as a group inside
its row, an array field as rows inside a row, each level with its own
*+ Add entry* and ordinals. The explicit-initialisation rule holds at every
level.

The email door names the **top-level array only** (numbered stems). A
required input whose structure is deeper than one level is unreachable by
email; publishing such a package **warns**, as v9 warns for a required
`number` or `object`.

### Paths and fans

A condition path reaches any depth (§ 6). `forEach: input:<path>` may fan
over a nested array; the item is `{ id, name, value }` as v9 defines, with
`name` built from the labels along the path.

## 8. Provenance

- **Effective-input hash 6** — definition 5 recursed. v10 stamps 6; v9
  keeps 5; the ladder below 9 is unchanged. A v10 package with no nested
  structure hashes byte-identically under 5 and 6 (the recursion never
  enters), which is the control.
- **Node hash** unchanged for prompt nodes; a classifier's output hash is
  the SHA-256 of its normalised value; the node hash version does not move.
- **The record** gains `classifications` (declared values, printable), the
  boundary time, and the fire set as decided there; `skippedDocuments`
  carries the compound sentence. The provenance registry bumps once for
  these fields and the hash definition (§ 14, step h), with a worked
  example per the record contract's ladder.
- **Workflow-output hash** unchanged.

## 9. The v10 closure set

**Kept from v9** (apply to 9 or later): typed and structured inputs, fans
over caller data, several documents per slot, metadata and tags, the
six-operator clause grammar, per-document provenance.

**New in v10**: the classifier node kind and the `classification` contract;
the boundary and the *not yet decided* count; the `node:` operand;
expressions with `and`/`or`/`not`, parentheses and arithmetic; unbounded
nesting, recursive canonicalisation and effective-input hash 6; paths of
any length and `count(<path>)`; fans over nested arrays.

**Unchanged, explicitly**: `TotalBlockCount` is a stored scalar stamped once
and never recomputed; the opaque-manifest round-trip; per-result
reachability; text is never compared; the no-PHI sentence rule.

**Still out of scope for the format**: archives (#372's Fork 2);
cross-package composition; per-deliverable delivery routing; text
literals; fanned classifiers; node-valued arithmetic (§ 13).

## 10. Versioning mechanics

The accepted set becomes exactly **{5, 6, 7, 8, 9, 10}**; no version
retires. The dispatch hazards, in v8's and v9's tradition of naming them
before anything lands:

- **The hash choice** (`ConsultGenerationJobStarter.EffectiveInputHashOf`)
  is a four-way `>=` ladder; it becomes five-way, and the born-Failed
  record uses the same function.
- **The fire set moves.** Today the starter narrows `package.Results` and
  prunes nodes *before* `WorkflowPackageBlocks.Resolve`, and `Initialize`
  stamps the count from the block list. For a package with classifiers the
  narrowing, the prune and the expansion move behind the boundary, into the
  orchestrator, and `Decide` stamps. For a package without, nothing moves —
  by control flow, not by an argument, as #355 did it — so the v9 bytes are
  untouched.
- **`WorkflowNodeSpec` gains `kind` and `values`** as trailing optionals;
  `WorkflowNodeClosure.Edges`, `ValidateNodes`, `DescribeNode` and
  `ConsultNodeDescriptor` learn the third kind; the engine's
  `node.Aggregate != null` / `node.ForEach != null` branches gain
  `node.Kind == classifier`.
- **`WorkflowFieldSpec` regains `Items` and `Fields`**, `ConsultInputValue`'s
  `OfObject`/`OfArray` accept structure, and the converter's `Site` becomes
  a depth stack; the validator's three one-level refusals become
  version gates.
- **The condition parser is replaced**, not widened: `WorkflowResultCondition`
  becomes an expression tree with the v9 record as its one-clause leaf, so
  every v9 `Explain` sentence is produced by the same code path and
  `TheV8Sentences_AreUnchanged` / `TheV9…` stay green.
- **The editor's text reader** (`WorkflowResultConditionText`) cannot read
  an expression; the editor uses the PackageFormat library's parser
  directly (it is referenced already for `IsV9Form`) and composes through
  it.
- **`AcceptedSpecVersions` leads `SupportedSpecVersions`** — publishable
  before runnable, with the editor's two ceilings and its verbatim notice,
  until *the engine runs ten* flips the gate together with the registry pin.
- **Engine first, registry second**: the schema is generated from the
  manifest type and the conformance set from engine outcomes, so
  `package-format-v10.md`, `schemas/package-format-v10.schema.json`,
  `conformance/v10/` plus `v9/invalid-*-at-v9` gate cases, the
  `SHAPE_BLIND` list (every expression case is shape-blind), the README's
  counts, `catalog-schemas.json` (gains `classification`) and the
  unsupported-set sentence (*"5, 6, 7, 8, 9 or 10"*) publish as one
  registry version after the engine PRs.

## 11. Editor implications

- The node-kind picker gains **classifier** (prompt + bindings + a values
  editor, the enum-input control reused); gated at 10 with the v9 notice
  shape.
- The operand picker gains `node:<id>` for each classifier, with that node's
  values as the literal control.
- The condition editor becomes an **expression builder**: a list of clauses
  each edited by v9's three pickers, joined by `and`/`or` with explicit
  grouping shown as nested rows; `not` a toggle per clause; arithmetic
  authored as a term row (operand, operator, operand). A loaded expression
  the builder cannot represent is shown verbatim and left alone — the v9
  path.
- The fields editor recurses; the intake form renders the tree.
- Two ceilings until the gate flips; the pending-change registry's kinds do
  not change.

## 12. Content & rollout

No package is migrated; a v9 package is a v10 package with one edit.
`general` is the control — byte-identical output and hash under 5 and 6.
The first classifier package is a demo (*scope → plan or decline letter*)
published to the public registry after the gate flips, and is the one the
editor's tests and the conformance suite's valid cases are drawn from.

## 13. Candidates not taken

**Fanned classifiers** — a classifier under `forEach` would produce a list
of values, and a condition over a list is a quantifier (`any`/`all`), a
fourth grammar. Not needed by the trigger that has not fired.

**Arithmetic over node values** — a classifier's value is a symbol.

**Text literals and text comparison** — still the same answer: a text
comparison in a manifest is a rule about the patient's words.

**A boolean-valued classifier** — `values: [yes, no]` is the same thing.

**Quantifiers over arrays** (`any(medications.schedule.dose > 10)`) — the
natural next request after depth and expressions; a separate step with its
own record, so the evaluator lands and is understood first.

**Per-version manifest types** — v9 § 13's re-read stands.

## 14. The implementation ladder

Filed as M21 issues when this record lands, in dependency order; each is
its own PR; (a) makes v10 publishable, (i) makes it runnable.

- **(a)** Validator accepts 10; `kind`/`values` on nodes; recursive
  declarations; every new form refused below 10 by name; the schema
  generator keys by version. *Publishable, not runnable.*
- **(b)** Effective-input hash 6; recursive `ConsultInputValue` and the
  converter's depth stack; the recursive probe.
- **(c)** The expression parser and evaluator with `Explain`; the v9
  conformance corpus and both sentence suites stay green.
- **(d)** The `classification` contract in the output-contracts registry
  and the catalog; the agent behind it.
- **(e)** The boundary: `Decide`, the classifier stage in the orchestrator,
  the rail's *Deciding* stage, History's named rows.
- **(f)** The intake form as a tree; the email door's warning.
- **(g)** Editor: the classifier kind, the expression builder, recursive
  fields, the `node:` operand.
- **(h)** Provenance registry: hash definition 6, the record's new fields,
  a worked example.
- **(i)** *The engine runs ten*: the gate, the format registry's v10
  publication (spec, schema, conformance, `SHAPE_BLIND`), the submodule
  pin, the demo package.
