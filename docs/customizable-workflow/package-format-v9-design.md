# Package format v9: structured inputs, numbers, and fanning over caller data — design

**Status: design record for #419 (settled 2026-08-21), implementation
tracked by #421–#430.** Unlike every version before it, v9 has no *sketch* to
replace: its candidates were filed as individual records — #338 and #340 —
and this document is written from them. The candidates it does not take are
kept in § 13 rather than deleted, so a later reader can see what was
weighed.

Decisions taken with the operator: `number` and structured inputs are
**designed and shipped as one version**; an array's elements may be
**scalars or objects, with the element type declared**; `object` is a
**standalone input type**, not merely an element shape; an array may be
**fanned with `forEach`**, not only iterated in a template; and a condition
may read **dotted paths and array predicates** as well as the ordering
operators `number` brings.

> **Amendment, 2026-08-21 (#371).** v9 also carries a package **title and
> description** in the manifest — § 4, *Package title and description*. In
> scope by product decision, not by the pairing argument, and § 2 records
> that with the same care it gives #338's unfired trigger. The subsection
> sits inside § 4 rather than renumbering the sections #421–#430 already
> cite.

## 1. Motivation

Two ceilings, one format revision:

- **Every input is a scalar.** v8 typed the scalars — a date is a date and a
  boolean is a boolean — but the wire converter still admits exactly two
  kinds, a string and a bool, and *"a JSON object or array is a shape error
  and answers 400"*. A referral is rarely one document, and a medication
  list pasted into prose is v7's own motivation one level up: v7 existed
  because *"everything must be concatenated into one draft, erasing
  structure the workflow could use."*
- **Only the package may supply a collection to fan over.** The validator
  says it plainly — `forEach '<x>' must be a data: collection reference` —
  so a job can fan over section standards the author wrote and never over
  the four documents the clinician actually sent.

The pairing is #312's test applied again: *"they change the same manifest,
the same validator closure and the same hash family, and designing them
apart risks two conflicting revisions."* `number` and structured inputs both
revise `WorkflowInputSpec`, both extend the closed `WorkflowInputTypes` set,
both add a canonical-form rule, both move the effective-input hash
definition, and both widen the condition grammar. Four things twice.

## 2. The trigger, and that it fired

v8's record is careful to say its bar *"was not met. It was overridden by
product decision."* v9's was met, and the distinction is worth the same
care.

#340's trigger was *"a workflow that wants to iterate over caller-supplied
items — per-job, not per-package."* That workflow is **prior notes**. It
arrived through #372, which asked how one input slot holds several
documents, chose an array over concatenation, and closed into #340 rather
than being built.

**#338's trigger did not fire.** No workflow has asked to branch on a
quantity. `number` is in v9 on the pairing argument above — it revises the
same four things structure does, and the alternative is two conflicting
revisions of them — not because anything demanded it. A later reader should
not find a bar here that reads as cleared.

**Neither did a trigger fire for the title and description** (#371, step 1
of its sequencing). They are in v9 by product decision: the naming pain is
felt daily — every surface shows `acct-7bca2dcc1ed4@v2026.08.13` — and #416
turned this from a quiet addition into a version-gated one, because an
unknown manifest field is now refused. The manifest, the schema and the
conformance suite are being revised anyway; shipping the two fields
separately later would pay the versioning cost twice, which is the same
reasoning v8's § 2 recorded for its own override.

## 3. Vocabulary

- **Element type**: the declared shape of an array's elements — the value of
  `items`.
- **Field**: one named, scalar member of an `object`, declared with the same
  vocabulary an input uses.
- **Item**: one unit of a fan. Until v9 an item was always a package-authored
  `data:` collection entry; v9 admits caller-supplied ones.
- **Item identity**: the value `InstanceKey` and the per-item failure keys
  are built from, and the value cross-node alignment matches on.
- **Path**: a two-segment reference into an object, `<input-id>.<field-id>`.

## 4. The input model (normative)

### Declaration

```yaml
inputs:
  - id: consult_draft
    label: Consult draft
    required: true
    # type omitted = text: every v7 and v8 declaration stays valid unchanged

  - id: length_of_stay
    label: Length of stay (days)
    type: number
    required: false

  - id: patient
    label: Patient record
    type: object
    required: false
    fields:
      - id: family_name
        label: Family name
      - id: age
        label: Age
        type: number

  - id: prior_notes
    label: Prior notes
    type: array
    items: text
    required: false

  - id: medications
    label: Medications
    type: array
    items: object
    required: false
    fields:
      - id: name
        label: Drug
      - id: dose
        label: Dose
```

- `type` remains **optional, defaulting to `text`**, so a v8 `inputs` block
  is a valid v9 one. This is the migration story, exactly as v8's was
  (§ 12).
- `items` is **required for `array` and forbidden otherwise**. Its value is
  one of `text`, `date`, `enum`, `boolean`, `number`, `object`.
- `fields` is **required when the declared shape is an object** — either
  `type: object` or `items: object` — and **forbidden otherwise**. Each
  field is declared with the same vocabulary an input uses: `id`, `label`,
  optional `type`, optional `required`, and `values` when its type is
  `enum`.
- `values` remains **enum-only**, at least two entries, unique, each
  matching `^[a-z][a-z0-9_]*$`. A field may be an enum on the same terms.

**Structure is exactly one level deep.** A field's type may not be `object`
or `array`, and `items: array` is refused. This is a bound, not an
oversight: it keeps canonicalisation finite, keeps the intake form a
repeating row rather than a tree, and keeps a path two segments so the
condition grammar needs no expression parser. Deeper structure is § 13.

### Canonical form and validation

v8's table gains three rows. JSON has no date, and now it has everything
else v9 needs:

| type | wire form | rejected |
|---|---|---|
| `text` | JSON string, within the 256 KB cap | a boolean, a number, a structure |
| `date` | JSON string, ISO 8601 calendar date `YYYY-MM-DD` | any other spelling, including valid-but-different (`2026-8-1`) |
| `enum` | JSON string, exactly one of the declared `values` | anything outside the set |
| `boolean` | JSON `true` / `false` | a string, including `"true"` |
| `number` | JSON number, plain decimal | a string, including `"3"`; exponent form (`1e3`); a leading `+`; a leading zero (`007`); `NaN` and `Infinity`, which JSON does not carry anyway |
| `object` | JSON object whose keys are exactly the declared field ids | a missing required field; any key not declared; a nested object or array |
| `array` | JSON array, every element satisfying `items` | a null element; an element of the wrong kind |

**A number is a decimal, not a float.** Values are carried and compared as
`decimal`, so `0.1` is the value the caller sent rather than the nearest
double, and two callers who sent the same digits hash identically. A value
outside `decimal`'s range is refused rather than rounded — the same posture
as refusing `2026-8-1` instead of canonicalising it. The canonical spelling
is the digits as sent, minus nothing: v9 does not trim `1.50` to `1.5`,
because trimming would mean provenance records a value nobody sent.

**An empty array is present and empty**, not absent. Supplied for a required
input it is refused, naming the slot — the same posture #290 takes towards
an empty referral. Absent optionals stay absent.

The 400-versus-422 rule is unchanged and now carries more weight, because
the shape class has narrowed: a token JSON cannot carry at all is still a
**400**, while a well-formed value disagreeing with the *declaration* — a
number where an object was declared, an undeclared object key, a null
element — is a **422** naming the slot, because that check needs the package
and the slot id.

### Rendering

Structured values enter Scriban as their own kinds, so a template can
iterate and reach:

```
{{ for note in prior_notes }}
--- prior note ---
{{ note }}
{{ end }}

{{ patient.family_name }}, age {{ patient.age }}
{{ if length_of_stay > 7 }}Prolonged admission.{{ end }}
```

- An **object** enters as a script object with exactly its declared fields.
- An **array** enters as a script array, in the order the caller sent.
- A **number** renders as its canonical spelling — no thousands separators,
  no locale, for the reason `date` renders ISO: a default format would be a
  localisation decision the author did not ask for.
- An **absent optional** object or array enters as **null**, following
  #358's rule and for its exact reason: Scriban's empty string is truthy, so
  the empty-string convention made `{{ if x }}` fire on absence.

**One asymmetry, stated rather than hidden.** An *empty* array enters as an
empty script array, which Scriban treats as **truthy** — its only falsy
values are `null`, `EmptyScriptObject.Default` and `bool false`. So
`{{ if prior_notes }}` is true for an empty array where `when: prior_notes`
does not hold (§ 6). The idiom for authors is
`{{ if prior_notes.size > 0 }}`, and the validator warns on a bare
`{{ if <array-input> }}`. This is the same residual asymmetry v8 documented
for `{{ if !billable }}`, in the same place, for the same reason: a
condition is three-valued and Scriban has one falsy null.

### The publish-time probe

`WorkflowPackageValidator` dry-runs every prompt against a probe object, and
`ProbeTypes` special-cases only `date` and `boolean` — everything else is
the string `"placeholder"`. Left alone, `{{ for note in prior_notes }}`
would be validated against a scalar and `{{ patient.age }}` against a
string, so a correct template **could not publish**, exactly as #357's
documented date idiom could not.

The probe learns the new types: a number probes as a number, an object as an
object carrying its declared fields, an array as a **two-element** array of
its element probe. Two rather than one, so a template that assumes a
singleton fails the probe rather than the job.

The same validator **runs at load**, so this is a § 12 concern as well as a
§ 4 one: any new error it learns to raise can make an already-published
package unresolvable. New checks that could catch existing content are
**warnings**, on #370's precedent.

### Intake

One control per type: textarea, date picker, select, checkbox, and now a
number field, a repeating row set for `array`, and a field group for
`object`. The explicit-initialisation rule applies throughout — a new value
starts empty with publish blocked until chosen, never auto-defaulted to a
plausible value.

**Two doors, two reachabilities.** Email supplies text, so a required
`number` or `object` input is unreachable by email in the way a required
`boolean` already is, and publishing **warns** on the two shapes #370 named,
extended to the new types. An **array of `text` is reachable**: attachments
fill it, which is exactly what § 7 describes.

### Package title and description

*Added by the 2026-08-21 amendment (#371, step 1).* Two optional top-level
manifest fields:

```yaml
name: acct-7bca2dcc1ed4
version: v2026.09.1
specVersion: 9
title: Breast oncology consults
description: Referral triage and consult notes for the breast clinic.
```

- Both **arrive at 9**. On a v8-or-earlier manifest they are refused —
  *"a section the version does not have is never a silently ignored field"*
  — which since #416 the engine enforces rather than merely documents.
- `title`: when present, non-empty, a single line, at most 80 characters.
  `description`: when present, non-empty, at most 500 characters. Both are
  **authored package content**, the same safety class as labels and enum
  values: written at publish, never per consult, so safe in the UI and in
  anything the system composes.
- **The fallback is the ref, stated rather than assumed.** Every package
  published before this exists has no title; every surface that shows a
  title must show the ref when there is none.
- **No uniqueness rule.** Two identically-titled packages are legal, and the
  picker disambiguates by showing the ref beside the title — which
  provenance wants visible anyway.
- **History shows the title as it was at the pinned version**, beside the
  ref. Automatic rather than clever: a job records a ref, the ref names an
  immutable manifest, and that manifest's title is what the reader sees. A
  later rename cannot rewrite what an old consult ran.
- **A fork across names starts with no title.** Inheriting the parent's
  title would put a plausible, wrong name on a diverging package — the
  misleading default #371 warned about, and exactly what the
  explicit-initialisation rule exists to prevent. A republish of the same
  package keeps its title.
- **Why the manifest and not an account record**: the fields describe *the
  package* and travel with it — a shared or public package should arrive
  carrying its own name. Describing a version immutably is honest rather
  than awkward: the package is being published anyway when it changes.
  Filing — folders, *your* organisation of *your* packages — is #371 steps
  2 and 3, deliberately **not** here.

## 5. Fanning over caller data (normative)

### Declaration

```yaml
nodes:
  - id: summarise-note
    label: Summarising a prior note
    forEach: input:prior_notes
    prompt: summarise-note
    bindings:
      note: item:value
```

`forEach` accepts `data:<id>` as before, and now `input:<id>` where the
named input is declared `type: array`. Everything else about a fan is
unchanged.

### Items and identity

An item is a field map, as it has always been. v9 says where one comes from
when the caller supplied it:

- For **`items: object`**, the item *is* the element: its declared fields,
  reachable as `item:<field-id>`.
- For a **scalar element type**, the engine wraps it — the item is
  `{ value: <element> }`, reachable as `item:value`. One rule, so the
  existing item model serves both and `ConsultNodeVariableResolver` needs no
  new source kind.

**The engine mints the item identity, always: the element's zero-based
index.** Not a declared `id` field, even when the elements are objects,
because two sources of truth for identity is how cross-node alignment starts
disagreeing with per-item failure keys. An object element may of course
declare a field called `id`; it is a field like any other and means nothing
to the fan.

The index is the right identity here because **array order is the caller's,
is significant, and is recorded**: it is the order the elements hash in
(§ 8), so an item's identity is stable across a replay for the same reason
the hash is.

### What this does not change

**`TotalBlockCount` stays a stored scalar, stamped once at start** (#176,
phase 7). A caller-supplied array arrives *with the request*, so the item
count is knowable before anything runs, and block expansion per (deliverable
× source × item) is complete and correct at start exactly as it is today.
This is the whole reason v9 can take the fan while #336 stays out: v9 widens
*where items come from* and leaves *when the shape of the job is known*
alone.

Per-result reachability is unchanged: every deliverable must still
transitively include a `forEach` source, and an input fan is a fan for that
rule's purposes.

### The empty fan

A job whose fanned input is an **empty array** produces no items, therefore
no blocks, therefore no document — v8's empty-fire-set case wearing
different clothes. It is **refused at start**, naming the input:

> No document applies to these inputs. 'Prior notes' has no entries, and
> every document this package produces is written from them.

Same argument as v8's: knowable before any model call, costs nothing,
spends nothing, and running would create a job record whose only content is
that it produced nothing. It needs no part of #337 — the refusal is a value
in the existing start-failure enum, as the empty fire set was.

*Note, 2026-08-21:* #337 is now scheduled — the empty-fire-set class gains
a Failed job record so History has a row to point at. The empty fan is that
class, so it inherits the record through the same mechanism, with no format
consequence: what § 5 decides is that the outcome is knowable at start, and
that survives either way of recording it.

## 6. Conditions (normative)

### Grammar

The closed grammar grows once, deliberately, and is still not an expression
language:

```
when     := <operand> | <operand> <op> <literal>
operand  := <input-id> | <input-id>.<field-id> | count(<input-id>)
op       := == | != | > | < | >= | <=
literal  := true | false | <enum-value> | <number> | YYYY-MM-DD
```

- **Ordering operators are for ordered types only** — `number` and `date`.
  Applied to an enum or a boolean they are refused at publish, naming the
  type, so an author learns why rather than hunting a syntax error.
- **`date` gains ordering**, which v8's #314 erratum explicitly anticipated:
  it excluded date equality because *"was it exactly this day"* is not a
  choice *"until ordering exists (#338)"*. It now does, and
  `when: seen_on >= 2026-01-01` is a choice.
- **Text is still not comparable.** Comparing a referral byte for byte is
  not a choice, and no operator makes it one.
- **A path reads one field of one object**, and the field must be a scalar —
  which § 4 guarantees, since fields cannot nest.
- **`count()` is the only function, and it is named in the grammar** rather
  than opening a function namespace. Its operand is an array input, its
  value a non-negative integer, and it composes with the six operators:
  `when: count(prior_notes) > 1`.
- **The bare form is a truthiness test**, admitted for a `boolean` (its
  flag) and for an `array` (non-empty). Refused for every other type, which
  must compare explicitly.
- **Still no `and`/`or`, and no arithmetic.** A manifest is content authors
  fork freely and an operator cannot review line by line; an evaluator is a
  thing with an order of operations, and this format still does not need
  one. #338 predicted that *"the next request after `>` is usually `and`"*;
  when it comes it is a deliberate later step with its own bump.

As in v8, the parser accepts the wider form and the **validator** carries
the narrowing, so widening later is a guard and a literal rule rather than a
parser change.

### Evaluation time and absence

**Once, at job start, against the supplied inputs** — unchanged, and still
the load-bearing decision. Conditions read inputs only; #336 stays out
(§ 13).

Absence is three-valued as before: a condition on an absent input does not
hold, whichever operator it uses, and a path into an **absent object** does
not hold rather than erroring.

**`count()` is the one operand defined on absence**, and deliberately so: an
absent optional array counts **zero**, because "no entries supplied" and "an
empty list supplied" answer the same clinical question. So
`when: count(prior_notes) == 0` *does* hold when the slot was left empty,
where `when: prior_notes == …` would not. That is an exception to the rule
above, and it is stated here rather than discovered — a condition whose
whole purpose is asking "how many" is useless if it cannot say "none".

## 7. Several documents for one slot (normative)

#340 absorbed #372, so this is v9's business rather than a follow-on.

- **`InputFiles` maps a slot to a list**, not to one payload. A slot
  declared `type: array, items: text` may be filled by several documents;
  each is extracted and becomes one element, in the order supplied.
- **`InputFilePayload` still carries no filename.** It deliberately does not,
  because a filename can itself be PHI — which is also the argument that
  settled #372 in favour of an array: concatenation would have had to invent
  a boundary marker, and the only marker that preserved identity is the one
  the request is careful never to transmit. An array has no boundary to
  invent.
- **Provenance becomes a list per slot.** `ConsultInputOrigin` is recorded
  per **document**, positionally: `origins[id][i]` describes element `i`.
  This is a **response-contract change**, and History renders one row per
  document rather than one per slot — otherwise a four-document referral
  reads as *"read from a document by pdfpig · 4 pages"*, a sentence about an
  aggregate that is not a document.
- **Two caps, and they must compose.** `DocumentExtraction.MaxCharacters`
  bounds each extracted document, as now. v9 adds an **aggregate cap per
  slot**, equal to `MaxInputLength`, checked *after* extraction — because
  `ValidateRequest` runs at the door before extraction fills `Inputs`, so
  today a slot's total is bounded by nothing. Over the cap is refused,
  never truncated: a consult written from most of a referral with nothing
  saying so is the worst available outcome.
- **The content floor (#290) applies to the slot**, not the element. One
  empty document among four is not an empty referral. A document that
  extracted to nothing is still recorded in provenance as itself, so the
  reader can see which one it was.
- **Order is the caller's**, and the intake form must let them set it — the
  file picker preserves selection order and the list is re-orderable. This
  is not cosmetic: order is significant in the hash (§ 8), so a
  non-deterministic order would hash the same documents differently between
  runs.

**Archives are not in scope**, deliberately: `.zip` and `.7z` would bring
compression bombs, path traversal, nesting depth, entry counts, per-entry
size and a third-party dependency with its own attestation question. #372
keeps that analysis for whoever wants it (§ 13).

## 8. Provenance

Per provenance.md's discipline — definitions are versioned, added beside
their predecessors, never compared across versions:

- **Effective-input hash v5**: SHA-256 of the canonical JSON of the supplied
  inputs, where canonical means:
  - top-level slot ids **ordinal-sorted**, as v4;
  - object field keys **ordinal-sorted** at every level, which v4 never had
    to say because there were no objects;
  - array elements in **supplied order**, which is significant and is the
    caller's;
  - numbers in their canonical spelling (§ 4);
  - absent optionals omitted;
  - text normalised **per element**, which needs a hook `CanonicalText`
    does not have today — it runs per value.

  v9 jobs stamp `effectiveInputHashVersion: 5`; v8 keeps 4, v7 keeps 3,
  v5/v6 keep 2. A genuinely different function again, not the same bytes
  under a new number.
- **Workflow-output hash**: unchanged. `ResultSetHashVersion` 3 covers v9 —
  documents produced from caller-supplied items are documents like any
  other.
- **No new hash for the item set.** The items are derivable from the
  supplied inputs and the pinned package, both already recorded.
- **The node input hash is unchanged** and quietly does more work: it is
  SHA-256 of the *rendered* prompt, so two items that rendered identically
  are visibly identical without the format saying anything new.

## 9. The v9 closure set

**Kept from v8** (apply to "8 or later"): `type` on an input defaulting to
`text`; `values` for `enum`; canonical-form validation at start; `when` on a
result; start-time fire-set evaluation; refusal of an empty fire set; the
skipped-deliverable record.

**New in v9**: `number`, `object` and `array` as declared types; `items` for
arrays and `fields` for objects, with the one-level bound; structured
canonical forms; `forEach: input:<id>` with engine-minted item identity;
the refusal of an empty fan; the widened condition grammar — six operators,
paths, `count()`, array truthiness; several documents for one slot with a
per-slot aggregate cap; per-document provenance; effective-input hash 5;
`title` and `description` on the manifest, with the ref as the stated
fallback.

**Unchanged, explicitly**: conditions read declared inputs only, evaluated
once at start; `TotalBlockCount` is a stored scalar; per-result
reachability, which an input fan satisfies like any other fan; the
opaque-manifest round-trip.

**Still out of scope for the format**: nested structure; `and`/`or` and
arithmetic; conditions over node output (#336); archives (#372's Fork 2);
cross-package composition; per-deliverable delivery routing. A job record
for an empty fire set (#337) is scheduled but is not format work — it needs
no version and changes no manifest.

## 10. Versioning mechanics

The engine accepts exactly **{5, 6, 7, 8, 9}**. No version retires: the
engine runs old formats forever, and #405's note stands — *"additive changes
can converge; subtractive ones cannot."*

**The sharp edge is v8's, one rung up, and worse in one specific place.**
v7's hazard was two `== 6` comparisons that would have routed a v7 package
through v5 rules — a loud failure. v8's was `>= 7`, silent and almost
correct. v9's is `>= 8`, and the danger is concentrated:

- **`ConsultGenerationJobStarter`'s hash selection** reads
  `>= 8 => ComputeTypedInputsHash` and `>= 8 => TypedInputsHashVersion`. A
  v9 job taking that arm is hashed by **version 4's function** and stamped
  `4`, with no error anywhere — a provenance record that is wrong and says
  it is right. This is the one gate that must be a four-way choice before
  anything else lands.
- **`WorkflowPackageBlocks.Resolve`'s `>= 7`** expands blocks from package
  collections alone. A package fanning over caller data would take it and
  produce a wrong `TotalBlockCount` silently.
- **`ConsultGenerationJobStarter`'s `>= 6` collection snapshot** builds
  `collectionSets` from `package.Data.Collections` and throws when the id is
  absent. An `input:` fan is not in scope there at all.
- **The resolver's collapse to `Dictionary<string, string>`** is where the
  type is lost. v8 survived it by reconstructing the type at the last hop
  from a canonical string that round-trips; an array has no such string.
  Structure travels as a **trailing optional** beside `VariableTypes`, on
  its precedent and for its reason — *"a v5-v7 job replays with null and the
  renderer behaves exactly as it did"* — so every in-flight and replayed job
  is untouched. The orchestration input is immutable once started and
  `Initialize` is positional, which is why this is a cliff by construction
  whatever it carries.

Every dispatch point gets its own disposition — **superset** or **changed
behaviour** — enumerated when the implementation issues are written. The
headline is the one #405 measured and v9 reverses: **v8 added zero execution
branches; v9 adds several, in the starter, the block builder, the resolver,
the renderer and the engine's fan.** #405's decision not to introduce
per-version types was taken on the evidence that four formats had produced
one behavioural cliff. That evidence changes here, and § 13 records what to
re-read rather than pretending it does not.

`AcceptedSpecVersions` leads `SupportedSpecVersions`, as it did for v8, so
v9 is publishable before it is executable. With an execution cliff that gap
is long, and the editor must **say so** rather than let an author publish
into silence — a deferred state gets a visible name and an up-front
notification, never a silent hold.

**Content repos.** Unlike v7 and v8 this is no longer only this repo's
business. Since #376 the prose, `spec-versions.json`, the JSON Schemas and
the conformance suite publish from `consultologist-package-format`, and the
schemas are **generated from `WorkflowPackageManifest`** in this repo — so
the registry artifact is downstream of the engine. The release order is
therefore engine-first, registry-second, and the conformance suite is what
proves they agree: every published case replays against the engine with
byte-identical outcomes, error order included. `consultologist-workflows`'
CI is structural only and needs no change, as it did not for v7 or v8.

## 11. Editor implications

The opaque-manifest round-trip carries unknown sections through publish
untouched, so a v9 manifest survives an editor that does not understand it.
Authoring is a different question, and #347's lesson is the one to avoid
repeating — a v7 fork could not be migrated to v8 because the editor's model
of a package was thinner than the registry's.

- The **inputs editor** gains the three new types, an element-type selector
  for `array`, and a field-list editor for `object` — with the
  explicit-initialisation rule on every new slot and every new field.
- The **results editor** gains the six operators, an operand picker that
  offers declared inputs *and* their paths, and `count()` where the operand
  is an array. The literal control stays typed by the chosen operand, which
  is what makes the closed grammar authorable rather than a string to get
  wrong.
- The **nodes editor** gains `input:` as a `forEach` source, which means the
  source picker stops being a list of `data:` collections.
- A **metadata pane** for the title and description — with the fork-clears-
  title rule (§ 4) and the explicit-initialisation posture on both fields.
- The **Consults intake form** renders the new controls (§ 4) and the
  multi-file slot (§ 7), including the re-orderable list.

Neither `inputsEdit` nor `resultsEdit` changes kind — both are whole-list
edits — so the pending-change registry and the draft slices are untouched.

## 12. Content & rollout

1. `general@vNext` migrates to **minimal v9**: `specVersion: 9` and nothing
   else changed. Its rendered output must be **byte-identical** to its v8
   predecessor. That is the point: it proves the format step independently of
   any behaviour change, so a difference in output can only mean the
   migration broke something. The effective-input hash version changes
   (4 → 5) and the output hash does not, which is itself the assertion that
   inputs were re-defined and outputs were not.
2. A **demo package** exercises the new width: an array of prior notes
   fanned by `forEach`, an object input read by path, a number driving an
   ordering comparison, a deliverable conditioned on `count()`, and a
   declared title — the first package whose picker entry is a name somebody
   chose. It is
   where the empty-fan refusal and the per-document provenance are seen
   working.
3. `example-two-documents` stays v7 until there is a reason — an unmigrated
   package in the repo is the standing evidence that v7 still runs.
4. The **registry release** follows the engine: `package-format@vYYYY.MM.N`
   carrying the v9 prose, the generated schema, the widened `supported` set
   and new conformance cases — including rejection cases for every refusal
   this document names.

## 13. Candidates not taken

Kept unpromised, so a later reader sees what was weighed.

**Nested structure** — an object holding an object, or an array of arrays.
Refused by § 4's one-level bound. Every motivating case in #340 is flat: a
problem list, a medication list, a patient record. Nesting would make
canonicalisation recursive, the intake form a tree, and a path
arbitrary-length, which is an expression parser wearing a schema's clothes.
Additive later; not needed now.

**`and` / `or` and arithmetic in conditions** — #338 predicted the request
and the answer is unchanged from v8's: an evaluator is a thing with an order
of operations, and a manifest is content an operator cannot review line by
line. A deliberate later step with its own bump, never a quiet loosening.

**Conditions that read node output (#336)** — *considered and deferred,
again.* It is the one change #405 calls deeper than this one, because it is
not a branch but a change to *when the shape of a job is known*. v9 widens
where items come from and leaves start-time knowability intact, which is
exactly what keeps `TotalBlockCount` a stored scalar. Its trigger has not
fired.

**A job record for an empty fire set (#337)** — not a format candidate,
and now scheduled as ordinary work (2026-08-21): the empty-fire-set class
gains a Failed job record, and § 5's empty fan inherits it through the same
mechanism. It needed no format version, which is why scheduling it changes
nothing in this document beyond the two notes that say so.

**Archives as an input transport (#372, Fork 2)** — *considered and
deferred.* Two readings were laid out there — the archive as one input's
contents, and the archive as a bag of named inputs matched by filename stem
— along with the extraction attack surface each brings. v9 takes neither:
the first cut is the app's file picker, and email intake makes its own case
later on its own evidence.

**A caller-supplied `data:` collection** — letting a request replace a
package's authored collection rather than adding a new fan source. It
inverts the ownership the fork model is built on: a `data:` collection is
authored content that the package's own validator closes over, and a caller
substituting one would make a published package's behaviour depend on the
request. `forEach: input:` gets the capability without the inversion.

**Text literals in conditions** — refused in v8 by #314's erratum, still
refused. Comparing a referral byte for byte is not a choice.

**Per-version manifest types (#405)** — that record decided *not* to
refactor, on the measurement that four formats had produced one behavioural
cliff and v8 had added zero execution branches. **v9 falsifies the second
half of that measurement**, so the decision should be re-read before the
implementation issues are written — with its own recommendation in hand:
*"the honest refactor is probably not per-version classes but isolating the
one place shape is decided."* v9 creates exactly one such place, the
resolver's collapse from typed values to a string map (§ 10), which is the
thing to isolate if anything is.
