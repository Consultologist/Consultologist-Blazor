# Package format v8: typed inputs and conditional deliverables — design

**Status: design record for #312 (settled 2026-08-10), implementation
tracked by #313–#317.** This document replaces the v8 *sketch* — a list of
candidates and a trigger — with a design. The candidates it does not take
are kept in § 11 rather than deleted, so a later reader can see what was
considered.

Decisions taken with the operator: conditions read **declared inputs
only**; a job with **no** applicable deliverable is refused at start rather
than run; the type set is **text, date, enum, boolean**.

## 1. Motivation

Two ceilings, one format revision:

- **Every input is a string.** v7 gave packages an intake form; every slot
  in it is text. A date arrives as whatever the clinician typed, and a
  prompt that wants to reason about *when* something happened has to
  interpret prose. There is nothing to validate against and nothing for a
  form control to be.
- **Every declared deliverable is always produced.** v7's result set is
  fixed at pin time. A billing summary that only applies to billable
  encounters has no way to say so: the package either always produces it,
  or a second package exists that differs by one document.

The pairing is deliberate, and closer than v7's. Both change `inputs`/
`results` in the same manifest, both extend the validator's closure set, and
one bumps a provenance hash. They are also **coupled semantically**: a
condition needs an operand, and typing is what supplies one. Designing them
apart risks two conflicting revisions of the same two sections.

## 2. The trigger, and that it was overridden

The sketch set a bar: a bump waits on *"a demanding consumer, not an
accumulation of nice-to-haves."*

**That bar was not met. It was overridden by product decision**, and this
document records the override rather than quietly reclassifying the
candidates as demands. No workflow today fails for want of a typed input,
and none has been blocked on a conditional deliverable. The reasoning for
proceeding anyway: the two changes are cheap while the editor and validator
are already being worked (M17), and the alternative — shipping them
separately later — pays the 18-gate versioning cost twice.

A later reader should not find a bar here that reads as cleared.

## 3. Vocabulary

- **Input type**: the declared shape of an input slot's value —
  `text` (default), `date`, `enum`, `boolean`.
- **Canonical form**: the single textual spelling the engine accepts for a
  typed value. Typed inputs travel as text; the *type* constrains which
  text.
- **Condition**: a declared test on one input, attached to a deliverable,
  deciding whether it is produced by a given job.
- **Fire set**: the deliverables whose conditions hold for a job's supplied
  inputs — computed once, at start.

## 4. Typed inputs (normative)

### Declaration

```yaml
inputs:
  - id: consult_draft
    label: Consult draft
    required: true
    # type omitted = text: every v7 declaration stays valid unchanged
  - id: seen_on
    label: Date seen
    type: date
    required: true
  - id: encounter_kind
    label: Encounter kind
    type: enum
    values: [new_patient, follow_up, procedure]
    required: true
  - id: billable
    label: Billable encounter
    type: boolean
    required: false
```

- `type` is **optional and defaults to `text`**, so a v7 `inputs` block is
  a valid v8 one. This is the migration story: `general@vNext` declares
  `specVersion: 8` and changes nothing else (§ 10).
- `values` is **required for `enum` and forbidden otherwise**, with at
  least two entries, unique, each matching the input-id convention
  (`^[a-z][a-z0-9_]*$`). Enum values are authored package content, never
  patient data, so they are safe in logs and filenames by the same argument
  result ids already carry.
- `number` is deliberately absent (§ 11).

### Canonical form and validation

Typed inputs remain **text on the wire**. The request contract is unchanged
— `{"inputs": {"<id>": "<text>"}}` — and the type decides which text is
accepted, checked at job start against the resolved package:

| type | canonical form | rejected |
|---|---|---|
| `text` | any string within the 256 KB cap | — |
| `date` | ISO 8601 calendar date, `YYYY-MM-DD` | any other spelling, including valid-but-different (`2026-8-1`) |
| `enum` | exactly one of the declared `values` | anything else |
| `boolean` | `true` or `false`, lowercase | `yes`, `1`, `True` |

**Rejecting at the door rather than canonicalising** is the deliberate
choice. A silent normalisation of `2026-8-1` into `2026-08-01` would mean
two callers sending different bytes get the same effective-input hash, and
provenance would be recording a value nobody sent. The intake form only ever
produces canonical values, so the strictness costs the UI nothing; it is API
callers who are held to it, and told exactly what was wrong.

A **date carries no time and no timezone.** A calendar date is the thing
clinicians write; the moment it gains a clock it owes an answer about whose
midnight it is, and no workflow has asked for one.

### Rendering

A typed value interpolates into Scriban as **its canonical form** —
`{{ seen_on }}` renders `2026-08-10`. No per-input display format in v8:
formats are a localisation decision, ISO is unambiguous to a model, and an
author wanting prose can say so in the prompt around it.

An **optional, absent** input still resolves to the empty string, unchanged
from v7 § 3. This is why an absent `boolean` is not `false` — absence and
falsity are different, and a condition testing an absent input does not
hold (§ 5).

### Intake

One control per type: textarea, date picker, select, checkbox. The
explicit-initialisation rule applies — a new value starts empty with
publish blocked until chosen, never auto-defaulted to a plausible value.
Detail belongs to #316.

## 5. Conditional deliverables (normative)

### Declaration

```yaml
results:
  - id: consult_note
    node: node:assemble-note
    label: Consultation note
    # no condition: always produced
  - id: billing_summary
    node: node:assemble-billing
    label: Billing summary
    when: billable == true
```

- `when` is **optional**. A deliverable without one always fires, so a v7
  `results` block is a valid v8 one.
- The string `result:` sugar (v7 § 4) is unchanged and takes no condition —
  a package with one deliverable that might not fire has nothing to produce
  and would be refused at every start (§ 5, *The empty case*).

### Grammar

A **closed grammar over one declared input**, not an expression language:

```
when := <input-id> | <input-id> == <literal> | <input-id> != <literal>
literal := true | false | <enum-value> | "<text>" | YYYY-MM-DD
```

- The bare form `when: billable` is truthy-tests a `boolean` only.
- Both sides are validated at publish: the id must be declared, and the
  literal must be admissible for that input's type — comparing an enum to a
  value it does not declare is an error, not an always-false condition.
- No `and`/`or`, no arithmetic, no date ordering. A manifest is content that
  authors fork freely and an operator cannot review line by line; an
  evaluator is a thing with an order of operations, and this format does not
  need one. Compound logic, if it is ever wanted, is a deliberate later step
  with its own bump.

### Evaluation time

**Once, at job start, against the supplied inputs.** This is the load-bearing
decision of v8 and it is what keeps the rest cheap:

- `WorkflowPackageBlocks.ResolveResultSetBlocks` expands blocks per
  (deliverable × source × item) and `ConsultGenerationJobStarter` builds
  that list before the orchestration input exists. Filtering the result set
  to the fire set *before* expansion means the block skeleton is still
  complete and correct at start.
- `ConsultGenerationState` stamps `TotalBlockCount` once, from
  `input.Items.Count`. It is a **stored scalar by deliberate decision**
  (#176, phase 7) and stays one. Progress never recounts, and never goes
  backwards.

A condition that could read node output would make the fire set unknowable
until mid-run, forcing `TotalBlockCount` to become mutable or to over-count
and correct. That is a materially different engine, for expressiveness
nothing has asked for. **Inputs only.**

### The empty case

If **no** deliverable's condition holds, the job is **refused at start**,
naming the deliverables that did not apply and the condition each wanted:

> No document applies to these inputs. 'Billing summary' is produced when
> `billable` is `true`.

Because conditions read only inputs, this is knowable before any model call.
Refusing costs nothing, spends nothing and tells the author immediately;
running would create a job record whose only content is that it produced
nothing. This follows the same up-front-notification posture as the
deferred-state work: a held or inapplicable state gets a name and a message
rather than silence.

### Outcome and record

- **Completed means every deliverable that fired produced.**
  `ConsultGenerationEngine.FinalOutcome` is unchanged; it is handed the fire
  set instead of the declared set. A fired deliverable that fails is Failed,
  exactly as v7.
- **A skipped deliverable is recorded, not omitted.** The job carries the
  declared deliverables it did not produce, with the condition that excluded
  each. A package declaring two documents whose job shows one should say
  why; omission would leave History indistinguishable from a one-document
  package, and provenance is the thing this system sells.
- Skipped deliverables are **not** in the workflow-output hash. That hash is
  over produced documents (`ResultSetHashVersion` 3, `{resultId:
  sha256(text)}`) and its definition is unchanged by v8 — two jobs that
  produced byte-identical documents still hash identically, which is the
  property it exists for.

## 6. Provenance

Per provenance.md's discipline — definitions are versioned, added beside
their predecessors, never compared across versions:

- **Effective-input hash v4**: the *function* is unchanged from v3 (SHA-256
  of canonical JSON of the supplied inputs as an ordinal-sorted
  `{id: text}` map, absent optionals omitted). The **definition** moves to 4
  because the input space is now typed: a v4 hash asserts that every value
  was canonical for its declared type, which a v3 hash does not. v8 jobs
  stamp `effectiveInputHashVersion: 4`; v5/v6 keep 2, v7 keeps 3.

  This is the reason the bump is not cosmetic. Two identical strings hashed
  under v3 and v4 give the same bytes — and mean different things, because
  only one of them was checked. Recording *which definition produced it* is
  the whole point of a version.
- **Workflow-output hash**: unchanged. `ResultSetHashVersion` 3 covers v8;
  a fire set is a set of produced documents like any other.
- **No new hash for the fire set.** The condition results are derivable from
  the supplied inputs and the pinned package, both already recorded.

## 7. The v8 closure set

**Kept from v7** (apply to "7 or later"): `inputs` required; structural
`input:` parsing with validator closure; `results` declaration with string
`result` sugar; union-rooted reachability; per-deliverable
blocks/state/hashes/delivery; normative rendering bytes.

**New in v8**: `type` on an input (default `text`) with `values` for `enum`;
canonical-form validation at start; `when` on a result with the closed
grammar; start-time fire-set evaluation; refusal of an empty fire set; the
skipped-deliverable record; effective-input hash 4.

**Unchanged, explicitly**: per-result reachability applies to conditional
results too — a deliverable that might not fire is still a deliverable, and
must still transitively include a forEach source. #314 should test that
rather than assume it.

**Still out of scope**: cross-package composition (§ 11); per-deliverable
delivery routing; non-text inputs (files bind through extraction, never as
binary inputs); compound conditions; `number`.

## 8. Versioning mechanics

The engine accepts exactly **{5, 6, 7, 8}**.

**The sharp edge is the mirror of v7's.** v7's survey found two `== 6`
comparisons that would have routed a v7 package through v5 rules — a loud
failure. v8's hazard is `>= 7`, which is how most gates are written, so a v8
package flows through v7 paths *silently and almost correctly*. The specific
danger: `ResolveResultSetBlocks` would expand **every** declared
deliverable, conditions ignored, and the job would run to completion
producing documents that should not exist. Nothing would error.

Eighteen engine-side dispatch points, re-counted against the current tree
(`Workflow/` and `Jobs/`; the sketch's "17" predates v7's landing). Each
needs its own disposition — **superset** (v8 is v7 plus additions, correct
unchanged) or **changed behaviour** (must become version-aware):

| Gate | Disposition |
|---|---|
| `WorkflowPackageStore.SupportedSpecVersions` (`{5,6,7}`) | += 8 |
| `WorkflowPackageStore` membership check | derives from the list — untouched |
| `WorkflowPackageStore:98` (`< 7`) | superset: v8 takes the v7 arm |
| `WorkflowPackageValidator:89` spec gate (`is not (5 or 6 or 7)`) | → `is not (5 or 6 or 7 or 8)` |
| `WorkflowPackageValidator:139` (`v6OrLater = >= 6`) | superset |
| `WorkflowPackageValidator:185, 684` (error-text choice `>= 7`) | superset |
| `WorkflowPackageValidator:411` (`< 7`) | superset |
| `WorkflowPackageValidator:535, 662` (`>= 7 && Results != null`) | superset for existing rules; **the new `when` and `type` closures are added beside them, keyed `>= 8`** |
| **`WorkflowPackageBlocks.Resolve:15` (`>= 7`)** | **changed behaviour** — v8 must filter the result set to the fire set before expansion. The gate stays `>= 7`; `ResolveResultSetBlocks` gains the fire set as a parameter, so the v7 path is "every result fires" |
| `WorkflowPackageBlocks:20` (`== 6`) | v6-specific, correct |
| `ConsultGenerationJobStarter:269` (`< 7 && Inputs`) | superset |
| `ConsultGenerationJobStarter:294` (`>= 6`) | superset |
| `ConsultGenerationJobStarter:343` (`isV7 = >= 7`) | superset for shape; **hash version becomes a three-way choice (2 / 3 / 4)** |
| `ConsultGenerationJobStarter:529` (`< 7`) | superset |
| `InputContent:143, 185` (`< 7`) | superset |
| Web spec gates (`Templates.razor`, `WorkflowPackagePicker`) | editor/display; the `== 6` ones are v6-specific and correct. `IsV7` (`>= 7`) becomes a capability question per feature, not a version ladder — #316 |

The publisher stamps the declared version it validated and never upgrades.
The v5/v6/v7 normative specs stay frozen; the v8 normative spec is written
when #313/#314 implement.

**Content repos**: `consultologist-workflows`' CI validator is structural
only (parse, CalVer, immutability, file closure) — v8 needs **no CI change**
there, as v7 did not. This repo's validator remains the sole
well-formedness gate.

## 9. Editor implications

The opaque-manifest round-trip carries unknown sections through publish
untouched, so a v8 manifest survives an editor that does not understand it —
but authoring is in-milestone (#316):

- The **inputs editor** gains a type selector, with `values` authoring for
  `enum` and the explicit-initialisation rule for every new slot.
- The **results editor** gains a `when` row: an input picker, an operator,
  and a literal control **typed by the chosen input** — an enum's literal is
  a select over its declared values, not free text. This is what makes the
  closed grammar authorable rather than a string to get wrong.
- The **Consults intake form** renders one control per type (§ 4), and
  should show which documents will be produced as the inputs change — the
  fire set is knowable in the browser by the same rule that makes it
  knowable at start.

Neither change adds a **pending-change kind**: `inputsEdit` and
`resultsEdit` are whole-list edits, so the registry (#329) and the draft
slices are untouched. #334 is not triggered by this milestone.

## 10. Content & rollout

1. `general@vNext` migrates to **minimal v8**: `specVersion: 8` and nothing
   else changed — no `type`, no `when`. Its rendered output must be
   **byte-identical** to its v7 predecessor. That is the point: it proves
   the format step independently of any behaviour change, so a difference in
   output can only mean the migration broke something. The effective-input
   hash version changes (3 → 4) and the output hash does not, which is
   itself the assertion that inputs were re-defined and outputs were not.
2. A **demo package** exercises the new width: a typed date and enum, and a
   deliverable that fires on one enum value. It is where the refusal path
   and the skipped-deliverable record are seen working.
3. `example-two-documents` stays v7 until there is a reason — an unmigrated
   package in the repo is the standing evidence that v7 still runs.

## 11. Candidates not taken

Kept from the sketch, unpromised, so a later reader sees what was weighed.

**Cross-package composition** (sketch § 3.3) — *considered and deferred.*
One package referencing another's nodes or prompts. It is **resolution
rather than declaration**: a dependency graph between packages with its own
versioning and resolution order, crossing a boundary built on purpose.
`PublicChain.cs` records that *"acct-\* content is unreachable by
construction"*, and repo packages live in an anonymously readable container
while forks do not — so *which package may reference which* is an
access-control question before it is a format one. The fork model
(`derivedFrom`) covers the common case. Not a v8 candidate; not abandoned.

**Per-deliverable delivery routing** (sketch § 3.4) — waits on whether
routing belongs to the account or the package. A product question wearing a
delivery change's clothes.

**Relaxing forEach reachability to the package** (#227) — *the full
argument, both sides, since package-format-v7-design.md § 11 points here for
it.*

The validator requires **each** result to transitively include at least one
forEach source. The justification is about the **package** — *a package with
no fan has no consult* (package-format-v6-design.md § 7) — and the
enforcement is per deliverable.

The lineage explains the gap. v5 required `result` to be a forEach node
because the deliverable **was** the fan; v6 restated that as an aggregator
including a forEach source; #214 generalised it per-result by symmetry with
the other v6 closures, not because multiple deliverables made it more
necessary. A package whose note fans over section standards and whose letter
is a single summarising prompt satisfies the justification completely and
still fails the rule.

**Both sides, because they matter equally.** It is **weak as a guarantee** —
an author satisfies it with an aggregator over the fan bound to a
barely-used variable, since the unused-variable check is only a warning. But
it worked as a **nudge**: in `example-two-documents` it pushed the patient
letter to read from an aggregator over the assembled sections rather than
generate independently from trajectory concepts, so the letter can only
summarise what the note actually says. That is a real gain and the reason
not to relax it casually.

The relaxation would be *at least one* result reaches a fan. It needs a v8
rule change or an explicitly documented erratum, never a quiet loosening —
and it waits on a package that legitimately needs it. None exists. v8 makes
the tension slightly more likely, since a conditional deliverable is often a
small summarising document, but not more urgent: **v8 keeps the per-result
rule unchanged** (§ 7).

**The input model as content** (sketch § 3.6) — typing was most of what this
had left after v7, and v8 takes it. What remains is the intake form as
authored content, which is a larger idea than a format bump.

**`number` as a type** — no workflow has asked. Each type costs a form
control, a render rule, a validator rule and a comparison operator, and
number brings format, precision and locale questions the others do not.
