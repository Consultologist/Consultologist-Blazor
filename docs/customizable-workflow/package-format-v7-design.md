# Package format v7: declared inputs and multiple deliverables — design

**Status: design record for #209 (settled 2026-07-26), implementation
tracked by its sub-issues.** v6's design doc sketched declared inputs as
"the natural v7" (package-format-v6-design.md § 11); this doc makes that
step — and the multiple-deliverables step it pairs with — normative.
Decisions taken with the operator: editor v7 authoring ships in the same
milestone; `general` migrates to a **minimal v7** (one declared input, one
result, byte-identical output) as the proving package, with a separate
demo package exercising the new width.

## 1. Motivation

Two ceilings, one format revision:

- **One input.** The engine binds exactly `input:consult_draft` — one
  string end-to-end. Real referrals arrive as parts: the referral letter,
  prior notes, labs. File upload (#208) and email attachments (#210) both
  stall against the single-slot model: everything must be concatenated
  into one draft, erasing structure the workflow could use.
- **One output.** v6's deliverable is one assembled document from one
  result aggregator. A consult encounter naturally produces siblings —
  the consult note, a patient-facing letter, a billing summary — and the
  aggregator machinery already renders and hashes every aggregator; only
  the deliverable layer is single.

The pairing is deliberate: both change the same manifest, the same
validator, and both bump a provenance hash definition. Designing them
apart risks two conflicting revisions.

## 2. Vocabulary

- **Input slot**: a named, package-declared text input. Declared in the
  manifest, bound by nodes as `input:<id>`, supplied per-job by the
  caller.
- **Deliverable (result)**: one assembled document, produced by exactly
  one aggregator node, with an authored id and label. v7 packages may
  declare several.
- **Result set**: the ordered list of a package's deliverables.

## 3. Declared inputs (normative)

### Declaration

```yaml
inputs:
  - id: consult_draft          # snake_case, ^[a-z][a-z0-9_]*$
    label: Consult draft       # UI field label
    required: true             # default true; optional inputs may be absent
  - id: prior_notes
    label: Prior notes
    required: false
```

- `inputs` is a **required** top-level manifest section in v7 (explicit
  over implicit — a v7 package always states its intake form). Ids are
  unique; `consult_draft` remains the *conventional* primary slot name,
  carrying no special engine meaning in v7 beyond the request
  back-compat rule below.
- Appended to `WorkflowPackageManifest` as a trailing optional positional
  parameter (after `Result`) — source-compatible; v5/v6 manifests simply
  leave it null.

### Binding grammar

`input:<id>` parses **structurally** (any well-formed id), matching the
`item:`/`data:` pattern; the vocabulary closure moves from
`WorkflowNodeBindingSources.TryParse`'s static whitelist into the
validator, which checks every `input:` binding against the declared
`inputs` — exactly the split the parser's own architecture note
prescribes ("parsing is namespace-syntactic; vocabulary closures belong
to the validator"). For v5/v6 packages the validator closes the
vocabulary to `{consult_draft}`, preserving today's behavior and error
text.

### Request contract

The job request gains an optional map:

```json
{ "inputs": { "consult_draft": "…", "prior_notes": "…" },
  "workflowPackage": "…", "scheduledAtUtc": null }
```

- `consultDraft` (the v5/v6 field) remains valid: when `inputs` is
  absent, it back-fills the `consult_draft` slot. Sending **both** is
  rejected — no client ever needs both, and silently preferring one
  would drop caller data (the same explicit-over-silent posture as the
  unknown-id rule below). The back-fill targets a declared slot like
  any other: a v7 package that does not declare `consult_draft` rejects
  a `consultDraft`-only request as an unknown id — the convention is
  not mandatory, and the engine grants it no exemption.
- Validation at job start, against the resolved package's declaration:
  every **required** input present and non-blank; **unknown ids
  rejected** (explicit, never silently dropped); per-input size cap
  (256 KB, matching the intake bound).
- For a v5/v6 package, an `inputs` map with anything other than
  `consult_draft` is rejected.

### Email intake

Intake originally supplied exactly one text — the message body, which
back-fills `consult_draft` — making a v7 package email-eligible only if
its declaration included `consult_draft` and every other input was
optional. **#210 lifted that**: text attachments now fill declared slots
too, so a package needing more than a body is reachable by email.

Assignment (implemented 2026-07-28): a filename stem claims the slot it
names (`prior_notes.txt` → `prior_notes`) and **outranks the body** for
that slot — naming a file is deliberate, whereas a body may be nothing
but a client-appended signature. The body then takes `consult_draft` if
no attachment claimed it, and one remaining attachment fills one
remaining slot. Any
wider ambiguity is **refused**, because replies carry no PHI and a
filename can itself be PHI — the sender can never be told where a file
landed, so a positional guess between two files would be silent wrong
data rather than a visible failure. An attachment this version cannot
read (PDF, pending the extraction work) fails the whole message: the
alternative generates a consult from a body reading only "please see
attached". Rejections record `rejected-attachments` and move to
Rejected, never a partial run.

### Resolution

`ConsultNodeVariableResolver.Resolve`'s `string consultDraft` parameter
becomes `IReadOnlyDictionary<string, string> inputs`; the `input:` arm
becomes a prefix match + map lookup. An **optional, absent** input
resolves to the empty string (prompt templates render it as empty — the
package author owns handling absence, e.g. via prompt wording).

## 4. Multiple deliverables (normative)

### Declaration

```yaml
results:
  - id: consult_note           # snake_case, ^[a-z][a-z0-9_]*$, unique
    node: node:assemble-note   # must be an aggregator; distinct per result
    label: Consultation note
  - id: patient_letter
    node: node:assemble-letter
    label: Patient letter
```

- `results` is optional in v7; `result: node:<id>` (the string form)
  remains valid as sugar for a one-entry result set with
  `id: "consult"`, `label: <node label>` — the id `consult` keeps
  single-result v7 delivery filenames identical to today's
  `consult-{jobId8}.pdf`. Declaring **both** is an error. At least one
  deliverable, always.
- Result ids share the input-id convention (snake_case) — one casing
  rule across both declared-vocabulary sections. Node ids keep their
  existing convention untouched.
- Each result's node must be an aggregator (v6's rule, per-result);
  result nodes are distinct (two results may not share one node — the
  same content twice is authorable by two aggregators over the same
  sources).

### Rendering (normative)

Unchanged per deliverable: each result aggregator renders exactly as
v6's normative rendering (forEach sources as `## {name}` blocks, scalar
sources verbatim, `\n\n` separators). v7 adds no cross-document
concatenation — deliverables are separate documents, full stop.

### Execution

Multiple aggregators already render, hash, and report
(`MarkNodeCompleted`) today; v7 changes the *deliverable* layer:

- **Blocks** gain the deliverable dimension **for v7 jobs only**. Block
  ids become `{resultId}:{sourceNodeId}:{itemId}` (scalar sources
  `{resultId}:{sourceNodeId}`), eliminating the collision when two
  deliverables share a source; v5/v6 jobs keep today's ids unchanged.
  `TotalBlockCount` is the sum across the result set — a source shared
  by two deliverables counts once per deliverable (still a stored
  scalar — the phase-7 rule stands).
- **Entity state** gains `AssembledDocuments` (ordered id→text map);
  `CompleteDocument` becomes per-result (`resultId`, text). v6's single
  `AssembledDocument` string stays for v6 jobs — the two shapes never
  mix in one record.
- **Job outcome**: Completed requires every declared deliverable
  produced; a missing deliverable is Failed with the failing
  aggregator's error (first by result-set order).
- **Reachability** generalizes: every node must transitively reach *a*
  result node (union-rooted BFS); at least one forEach source must be
  reachable from *each* result.

### Failure (normative)

Per-deliverable failure semantics are v6's, applied per result: an
aggregator with failed sources renders what completed only if the
package's failure posture allows (unchanged `FailIfEmpty` semantics on
source nodes); a result aggregator that cannot render fails the job.

## 5. Delivery, display, progress

- **Consults**: the setup phase renders one field per declared input,
  all always visible — optional ones marked "(optional)" — so the
  clinician sees the package's full intake form at a glance. The result
  phase renders **one tab per deliverable** (result-set order; labels
  authored), each with its own copy action; the run rail is unchanged
  (nodes are nodes).
- **History**: the detail view lists per-deliverable output hashes; the
  summary row is unchanged (counts already sum across blocks).
- **Email delivery**: one encrypted PDF **per deliverable** on the one
  completion reply, filename `{resultId}-{jobId8}.pdf` (e.g.
  `consult_note-ab12cd34.pdf`) — snake_case ids cannot contain `-`, so
  the separator parses unambiguously; the result id is authored package
  content, never patient data, preserving the no-PHI-in-filenames rule.
  Total-attachment size posture: inline attachments cap ~3 MB base64;
  if the set exceeds it, attach none and fall back to the link-only
  reply (degrade whole, never partially — a partial document set
  misleads).

## 6. Provenance

Per provenance.md's discipline (definitions are versioned, added beside
their predecessors, never compared across versions):

- **Effective-input hash v3**: SHA-256 of the canonical JSON of the
  supplied inputs as a sorted-key map `{"<id>": "<text>", …}` (absent
  optional inputs omitted, not empty-stringed). v7 jobs stamp
  `effectiveInputHashVersion: 3`; v5/v6 jobs keep v2 (draft-only).
  `ComputeDraftOnlyHash` stays; `ComputeDeclaredInputsHash` is added.
- **Workflow-output hash v3**: SHA-256 of the canonical JSON of the
  sorted-key map `{"<resultId>": "<sha256-of-document-bytes>", …}`.
  v7 jobs stamp `workflowOutputHashVersion: 3`; v6's v2
  (single-document bytes) and v5's v1 stay. The
  `AssembledDocument != null` discriminator in `ToResponse` becomes an
  explicit three-way dispatch (documents map → v3; single string → v2;
  else → v1).

Both v3 definitions say "canonical JSON"; the exact byte-level rules
(UTF-8, no insignificant whitespace, ordinal key sort, escaping) are
pinned in the normative v7 spec when the format sub-issue implements —
a hash definition is its bytes, so the design doc deliberately does not
freeze them informally.

## 7. The v7 closure set

**Kept from v6** (apply to "6 or later"): aggregate composition rules,
prompt sharing, multi-collection fans, result-must-be-aggregator,
per-node output contracts, the normative rendering bytes.

**New in v7**: `inputs` declaration (required); structural `input:`
parsing with validator closure; `results` declaration (optional; string
`result` = one-entry sugar); union-rooted reachability; per-deliverable
blocks/state/hashes/delivery.

**Still out of scope**: non-text inputs (files bind through extraction —
#208/#210 — never as binary inputs); per-deliverable delivery routing
(all deliverables go everywhere the job's output goes); conditional
deliverables; cross-package composition.

## 8. Versioning mechanics

The engine accepts exactly **{5, 6, 7}**. Gate dispositions, one by one
(the survey's sharp edges — the two `== 6` comparisons would otherwise
route a v7 package through **v5** rules silently):

| Gate | Disposition |
|---|---|
| `WorkflowPackageValidator` spec gate (`is not (5 or 6)`) | → `is not (5 or 6 or 7)` |
| `WorkflowPackageValidator` rule dispatch (`v6 = SpecVersion == 6`) | → `v6OrLater = SpecVersion >= 6`; the five v6-keyed rules are all closures that apply to 7 (audited individually in the implementation) |
| `WorkflowPackageBlocks.Resolve` (`SpecVersion == 6`) | → `>= 6`, with the v7 result-set expansion beside the v6 single-result path |
| `WorkflowPackageStore.SupportedSpecVersions` | += 7; `ResolveAsync` derives the result **set** (the `manifest.Result!` strip becomes set-aware) |
| Engine v6 mode (`collections is { Count: > 0 }`) | already shape-derived — generalizes untouched |
| `WorkflowPackagePublisher` | no own spec check (validator-only) — untouched |
| Package pickers' integer spec gating (#137) | display-only readers — untouched |

The publisher stamps the declared version it validated; it never
upgrades. package-format-v5.md and the v6 docs remain frozen; the v7
normative spec is written when the format sub-issue implements.

**Content repos**: consultologist-workflows' CI validator is structural
only (parse, CalVer, immutability, file closure) — v7 needs **no CI
change** there; this repo's validator remains the sole well-formedness
gate, exercised at publish time and at engine load.

## 9. Editor implications (in-milestone by decision)

The opaque-manifest round-trip (`JsonElement` + surgical `JsonNode`
writes) already carries `inputs`/`results` through publish untouched;
the editor work is the authoring surfaces:

- An **inputs editor** (add/remove/rename slots, labels, required).
- The **results selector** becomes list-aware: `ReadResultRef` /
  `resultEdit` currently assume `result` is a string — a `results` list
  renders as an ordered deliverable list with per-entry node pickers.
- `BindingSourceEditor`'s hardcoded `input:consult_draft` option list is
  replaced by the declared inputs (it already preserves unrecognized
  current values, so partially-migrated states degrade gracefully).

## 10. Content & rollout

1. `general@vNext` migrates to **minimal v7**: declares
   `inputs: [{id: consult_draft, label: Consult draft}]` and keeps its
   single result — output byte-identical to the v6 package (the proving
   migration; the effective-input hash version changes, the output hash
   version changes, the bytes do not). Declaring `consult_draft` keeps
   it email-eligible per § 3 — intake continuity is part of what the
   migration proves.
2. A **demo package** (working name `duo`) exercises the width: two
   inputs (`consult_draft`, `prior_notes` optional), two deliverables
   (consult note + patient letter).
3. Rollout order: engine ships before content (a v7-capable engine runs
   v5/v6 unchanged); `general` v7 publishes once the engine is live;
   pins flip explicitly per the standing pin discipline.

## 11. Future steps beyond v7 (sketched, not promised)

**The maintained list now lives in
[package-format-v8-design.md](package-format-v8-design.md)**, together with
§ 7's "still out of scope" entries, the trigger that would justify cutting a
v8 at all, and what a version bump costs. Two lists of the same candidates
would drift, so this section keeps only the forward glance:

- **Typed inputs** (dates, structured patient fields) — today all inputs
  are text; typing changes prompt templating and the intake form.
- **Per-deliverable delivery routing** (e.g. the patient letter goes to
  a different destination than the consult note) — a product decision
  wearing a delivery change's clothes.
- **Attachment-shaped inputs** — #210's named-input binding for email
  attachments builds directly on § 3; extraction (#208 phase 2) feeds
  text into slots and stays outside the format. **Not a format change**,
  and recorded as such in the v8 sketch so it stops being read as one.
- **Relaxing forEach reachability to the package** (#227) — the rule is
  enforced per deliverable ("**each** result must transitively include at
  least one forEach source", § 4) but justified per package. The full
  argument, both sides, is in
  [package-format-v8-design.md](package-format-v8-design.md) § 11; it
  needs a rule change or an explicitly documented **erratum**, never a quiet
  loosening. v8 was cut and deliberately left it unchanged.
