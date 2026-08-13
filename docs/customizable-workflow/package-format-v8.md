# Workflow Package Format — specVersion 8 (Typed Inputs, Conditional Deliverables)

Normative specification for `specVersion: 8` packages — the **format
closure**, as implemented by #313, #314 and #315 (design and rationale:
[package-format-v8-design.md](package-format-v8-design.md), including two
dated errata where the design changed under review). Everything not stated
here is unchanged from [package-format-v7.md](package-format-v7.md):
declared inputs, the result set and its reachability, per-deliverable
blocks and documents, delivery, CalVer, immutability, `derivedFrom`.

A manifest declares the rule set it was validated under; the engine accepts
exactly **{5, 6, 7, 8}**. v5, v6 and v7 packages keep validating and
executing under their frozen rules. A package needs 8 only when it uses
what 8 opens.

**Both v8 additions are optional over a v7 declaration**, so a v7 manifest
is a valid v8 one and the minimal migration is the `specVersion` line
alone. That is deliberate: it makes a migration provable by having nothing
else to blame.

## `type` — the shape of an input slot

```yaml
inputs:
  - id: consult_draft
    label: Consult draft          # type omitted = text
  - id: seen_on
    label: Date seen
    type: date
  - id: encounter_kind
    label: Encounter kind
    type: enum
    values: [new_patient, follow_up]
  - id: billable
    label: Billable encounter
    type: boolean
    required: false
```

- `type` is optional; absent means `text`. Accepted values are `text`,
  `date`, `enum`, `boolean`. Any other is an error.
- `values` is **required for `enum` and forbidden otherwise**: at least
  two, unique, each matching the declared-id grammar
  (`^[a-z][a-z0-9_]*$`). Enum values are authored package content and are
  therefore safe wherever result ids are.
- `type` or `values` on a pre-v8 manifest is an error — a section the
  version does not have is never a silently ignored field.

### Wire form (normative)

Input values are **typed JSON**. JSON has string, number, boolean, null,
object and array, and **no date**, so of the four types only `boolean`
travels as something other than a string:

| type | wire form | rejected |
|---|---|---|
| `text` | JSON string, within the 256 KB cap | a JSON boolean |
| `date` | JSON string, ISO 8601 calendar date `YYYY-MM-DD` | a boolean; any other spelling, including `2026-8-1` |
| `enum` | JSON string, one of the declared `values` | a boolean; anything outside the set |
| `boolean` | JSON `true` / `false` | **a string, including `"true"`** |

A date carries **no time and no timezone**.

**Two failure modes, two answers.** A token JSON should not carry at all —
a number, an object, an array — is a *shape* error and answers **400**, in
keeping with the rule that request-shape problems are the 400s. A
well-formed value that disagrees with the *declaration* answers **422** and
names the slot.

Values are **rejected, never normalised**. Rewriting `2026-8-1` into
`2026-08-01` would hash a value nobody sent.

A JSON `null` reads as blank text — the same "required input missing" a
caller received before values were typed.

### Rendering

A typed value enters the template **as its own type**, so a prompt may
format and branch:

```
Seen {{ seen_on | date.to_string "%d %B %Y" }}
{{ if billable }}Include the billing summary.{{ end }}
```

There is no per-input default format: `{{ seen_on }}` renders
`2026-08-10`. An **absent optional** input still resolves to the empty
string, unchanged from v7.

### Content minimums

The referral-content check (#290) applies to **text inputs only**. A typed
value is short by nature — a date is ten characters — and its type already
constrains it more tightly than a length could.

## `when` — a conditional deliverable

```yaml
results:
  - id: consult_note
    node: node:assemble-note
    label: Consultation note        # no when: always produced
  - id: billing_summary
    node: node:assemble-billing
    label: Billing summary
    when: billable
```

- `when` is optional; a deliverable without one always fires.
- The string `result:` sugar takes no condition.
- Every deliverable may be conditional. Conditions can be legitimately
  exhaustive, and proving that in general is a satisfiability question, not
  a format rule.

### Grammar (normative)

```
when    := <input-id> | <input-id> == <literal> | <input-id> != <literal>
literal := true | false | <enum-value>
```

- The input must be an **`enum` or a `boolean`** — the two types that
  express a choice. A condition on a `date` or `text` input is refused at
  publish, naming the type.
- The bare form tests truth and therefore requires a `boolean`.
- The literal must be admissible: a declared value for an enum,
  `true`/`false` for a boolean. Comparing an enum to a value it does not
  declare is an **error**, not a condition that never holds.
- No `and`/`or`, no arithmetic, no ordering.

### Evaluation (normative)

Conditions are evaluated **once, at job start**, against the supplied
inputs. Consequences that are part of the contract:

- The **fire set** — the deliverables whose conditions hold — is known
  before any node runs.
- Blocks are expanded over the fire set, so a deliverable that will not
  fire contributes none. `TotalBlockCount` remains a stored scalar stamped
  once.
- The **node set is pruned to the fire set's transitive closure**: a node
  that feeds only a deliverable which did not fire never runs. The closure
  is over the two node→node edges — a binding's `node:` source and an
  aggregator's source list — rooted at the firing deliverables' nodes. A
  package with nothing skipped prunes to itself, since every node must
  already reach some deliverable.
- **Absence is not falsity**: an optional input nobody supplied satisfies
  no condition, including the negated form.
- An **empty fire set is refused at start**, naming each deliverable and
  what its condition wanted. No job is created.

### Outcome and record (normative)

- **Completed** means every deliverable that *fired* produced. A
  deliverable that did not fire is not missing.
- A **skipped deliverable is recorded**, with the input, its supplied value
  and what the condition wanted, and is named on every surface that reports
  the job: History, the result panel, and the completion reply. A job that
  produces fewer documents than its package declares and says nothing is
  indistinguishable from one whose deliverable failed.
- Skipped deliverables are **not** in the workflow-output hash, which is
  over produced documents.

## Provenance (normative bytes)

- **Effective-input hash v4**: SHA-256 of the canonical JSON of the
  supplied inputs as an ordinal-sorted map of **typed values** — a boolean
  serialises as `true`, not `"true"` — with absent optionals omitted. v8
  jobs stamp `effectiveInputHashVersion: 4`.

  A different function from v3, not the same bytes renamed:
  `{"billable": true}` and `{"billable": "true"}` hash differently. Where a
  value is text the two agree byte for byte, which is immaterial — per
  [provenance.md](provenance.md), definitions are never compared across
  versions.
- **Workflow-output hash**: unchanged. `ResultSetHashVersion` 3 covers v8;
  a fire set is a set of produced documents like any other.
- No hash for the fire set: it is derivable from the supplied inputs and
  the pinned package, both already recorded.

## Migration

A v7 package becomes a v8 package by changing `specVersion` and nothing
else. `general` did exactly that, which is what makes it the control: its
block ids, block count and rendered prompt bytes are identical either
side, so any difference is the engine rather than the manifest.

The effective-input hash version moves 3 → 4 and the output hash does not.
That pair is the assertion — inputs were redefined, outputs were not.
