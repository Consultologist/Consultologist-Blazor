# Recipe: conditionally append a macro on a model judgment (classifier-gated macro)

Recorded 2026-09-27. A practical, verified recipe for the common authoring
question: *"append this exact boilerplate block only when some clinical fact is
true — a fact the model decides from the draft, not a structured input."*

The worked example throughout: append a letrozole side-effects block after the
Assessment & Plan section, **only when letrozole is actually prescribed** — not on
every oncology note. Verified live on `acct-…/test/general@v2026.09.7` (a
letrozole draft appended the block; a no-letrozole oncology draft did not).

## The problem

A macro's `when` gate reads only **structured** signals — inputs, data values, or
a **classifier node's** answer. "Was letrozole prescribed?" is a free-text
judgment the model makes from the consult draft, so you cannot gate on it
directly. Gating on the specialty input (`consult_specialty == medical_oncology`)
over-fires: it appends the letrozole block to oncology notes where letrozole was
never prescribed. The fix is a classifier node that turns the judgment into a
value the `when` gate can read.

## The recipe

**1. Add a classifier node.** A classifier is a prompt node whose answer is one of
a declared value set. It is `kind: "classifier"` + `values: [...]`; it carries a
`prompt` and `bindings`; it declares **no `output`** and **no `forEach`** (its
output is the classification contract, implied by kind; one answer, never fanned).
It **always runs** (a classifier may not carry a node-level `when`), and it may
bind only inputs and other classifiers.

```json
{
  "id": "letrozole-prescribed",
  "label": "Letrozole prescribed?",
  "prompt": "classify-letrozole",
  "bindings": { "consult_draft": "input:consult_draft" },
  "kind": "classifier",
  "values": ["yes", "no"]
}
```

Its prompt spec + file:

```json
{ "id": "classify-letrozole", "file": "prompts/classify-letrozole.md", "variables": ["consult_draft"] }
```
```text
Decide whether **letrozole** is prescribed for this patient in the consult below —
that is, whether the plan recommends, starts, or continues letrozole (or names
letrozole as the adjuvant endocrine therapy).

Answer `yes` if letrozole is prescribed. Answer `no` if it is not.

Consult draft:
{{ consult_draft }}
```

- `values`: at least 2, unique, each snake_case (`^[a-z][a-z0-9_]*$`) — `yes`/`no`
  are fine. A one-value classifier is a constant, and is refused.
- The prompt's `variables` must match the binding keys; interpolate with Scriban
  `{{ consult_draft }}`.

**2. Gate the macro on the classifier's answer.** On the deliverable's macro
reference (or a deliverable, or a node), set `when` to compare the classifier to a
declared value:

```json
{ "id": "letrozole_prescription_text", "after": "node:section-instructions",
  "forItem": "assessment_plan", "when": "node:letrozole-prescribed == yes" }
```

**3. (Optional) place it after one section.** `forItem: <collection-item-id>` on a
`before`/`after`-a-fan-source placement anchors the macro right after that
section's block (here, after Assessment & Plan). See the v19 design note.

## The gotchas (get these wrong and it silently misbehaves)

- **The `when` operand is `node:<id> == <value>`, NOT `classification:<id>`.** The
  `classification:` namespace is only for `{{classification:<id>}}` **placeholders
  inside a macro file's text** (it interpolates the answered value). A `when`
  clause compares a `node:<id>` operand. Using `classification:…` in a `when` is
  wrong.
- The literal (`yes`) must be one of the classifier's declared `values`, and must
  be snake_case. Only `==` / `!=` are allowed for a classifier operand (never
  ordered `< > <= >=`); a bare `node:<id>` truthiness test is refused — compare it
  to a value.
- A classifier declares **no output schema** and **no `forEach`**; putting either
  on it fails validation. Its value is bindable by other nodes but **not
  aggregatable**.
- Gating on a **model judgment costs one extra classifier call per run**. That is
  the price of firing correctly instead of on every note of a specialty.

## specVersion floor

- Classifier node (`kind` + `values`) and `when: node:<id> == <value>`: **v10**.
- `{{classification:<id>}}` inside a macro file: **v11** (macros).
- Node-level `when` on an ordinary node: **v18**. `forItem` placement: **v19**.

## Placement is section-granular, not inline

The block lands **after** the anchored section, not at a spot the model chooses
mid-prose. Truly inline, model-chosen placement (a `{{slot:…}}` marker the model
emits, filled verbatim by the assembler) is a design proposal, not built — see
issue #863. Do **not** instruct the model in a prompt/standard to "run the macro":
the model cannot invoke a macro, and it will just write a literal placeholder.

## Publishing the change

Author it in the editor, or publish a new version directly against the API — see
the operator-publish recipe in memory. Note the editor bug #864 (un-checking a
declared macro's "optional" is silently dropped on publish); the API path avoids
it. Either way the server validates the classifier, its prompt, and the `when`
grammar in full before minting the version.

## Verifying

Run two consults on the same specialty that differ only in the judgment: one where
the fact is true (block should appear) and one where it is false (block should be
suppressed). Confirm the classifier answered as expected and the macro fired/was
gated accordingly. This is the only way to prove the gate both ways — a single
positive run does not show the suppression path.
