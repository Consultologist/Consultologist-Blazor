# Package format v11: macros, signatures, and reproducible stages — design

**Status: design record for Milestone 22 (settled 2026-08-31), implementation
tracked by the ladder in § 12.** v11 adds three declarations to the manifest:
`macros` with `results[].macros` (#513), `results[].signature` (#516), and
`nodes[].reproducible` (#550). v10 published immutable
(`package-format@v2026.08.8`; every schema level says
`additionalProperties: false`, and the engine refuses a property the format
does not have by name), so a manifest key is a rung by definition — this is
the deliberate later step with its own bump, in v10's rhythm: the record
settled on paper first, the ladder filed as issues, engine first and
registry second.

Decisions taken with the operator: a **macro is one construct** — a
package-owned template file with placeholders from closed namespaces — and
the three senses the word carries in medical software (a fixed phrase, a
template over the run's values, a link to run and profile facts) are what
the placeholder namespaces allow, not three manifest kinds (2026-08-31);
the **signature is appended at completion, inside the hash**, snapshotted
onto the job at start (2026-08-31); **optional per-run macros are not
taken** (§ 11); `reproducible` is the package's claim, read by the rerun
verdict (#549), never a run-time choice.

## 1. Motivation

Three needs, none expressible in v10:

- **The package's own words on the document.** A specialty's disclaimer, a
  standing closing paragraph, a boilerplate the clinic reuses — text that
  belongs to the *package*, versioned with it, identical for everyone who
  runs it, and landing on the assembled document **verbatim, never through
  a model**. v10 has fixed text only as data values, which exist to be
  read by prompts; a model paraphrasing a disclaimer is the failure mode
  this feature exists to prevent. The idea's lineage is the macro of
  clinical software — Epic's SmartPhrases, SmartTexts and SmartLinks;
  Dragon's auto-texts; radiology's "normals" — three senses one construct
  can carry (§ 4).
- **A signed document.** A patient letter carries the clinician's
  signature block; an internal summary does not. Which documents are
  signed is the package author's call (`results[].signature`); what the
  signature says is the clinician's (#516's profile half). The email door
  decides nothing at a form, so the package's flag is the only design
  that works there.
- **A reproducibility claim per stage.** The clinical-term extraction
  stage must yield the same output for the same input; the prose stage
  need not. Whether a stage makes that promise is a property of the
  package (`nodes[].reproducible`), and the rerun comparison (#549) needs
  it declared to have a verdict (#550).

## 2. The trigger, and that it did not fire

v8 was overridden by product decision; v9's trigger was met; v10's did not
fire and its record says so. v11's has not fired either: **no published
package needs a macro, a signature, or a reproducibility claim** — the
registry holds `general` and the v10 demo, and neither asked. The
operator's decision (2026-08-30/31) is again to build ahead of the
trigger, for v10 § 2's reasons — the roads are mapped here rather than in
an issue thread, the format is the product's grammar, and deferring has
its own cost now that #549's rerun and #516's profile half want their
declarations — weighed against the same reverse reasons v10 recorded and
accepted. Never a quiet loosening: a bump, a record, a control.

## 3. Vocabulary

- **Macro** — a template file the package owns, applied to a deliverable
  by substitution and appended verbatim; the union of the three clinical
  senses under one grammar (§ 4).
- **Placeholder** — `{{namespace:id}}` inside a macro file; the closed set
  of namespaces is the whole of what a macro may reach.
- **Signature** — the profile's signature block (name, credentials,
  contact), chosen on the profile, appended by the app to the deliverables
  the package flags; never in the package.
- **Appended text** — macro expansions and the signature: text on the
  assembled document that no model produced. Inside the document's hash;
  outside every node's hash.
- **Reproducible stage** — a node the package declares must yield the same
  output for the same input; the unit of #549's verdict.
- **The assembled document** — a deliverable's full text as stored,
  rendered, hashed, delivered and reusable (#510): aggregated sections,
  then macros, then the signature.

## 4. Macros (normative)

### Declaration

```yaml
macros:
  - { id: disclaimer, label: Standing disclaimer, file: macros/disclaimer.md }
  - { id: closing,    label: Closing paragraph,   file: macros/closing.md }
results:
  - { id: letter, node: node:assemble-letter, label: Decline letter,
      macros: [disclaimer, closing], signature: true }
```

- `macros` is a top-level list; each entry has `id` (snake_case, the
  declared-id grammar of `WorkflowDeclaredIds`, unique among macros),
  `label` (non-blank) and `file` (present in the package, **non-empty** —
  stated here because prompts check presence only). Files live under any
  path; `macros/<id>.md` is the convention.
- `results[].macros` is an ordered list of declared macro ids; each named
  macro must exist, and a macro no result names is an error (the orphan
  rule, as prompts have). Below specVersion 11 both keys are refused by
  name, the v10 pattern (`… requires specVersion 11.`).

### Placeholders — three senses, one grammar

A macro file is markdown that may contain `{{namespace:id}}` tokens. The
namespaces are **closed**:

| Namespace | Resolves from | Clinical-software sense |
|---|---|---|
| *(none — no placeholders)* | the file itself | SmartPhrase / auto-text: fixed canned text |
| `input:<declaredId>` | the run's effective inputs | SmartText: a template over the run's values |
| `data:<id>` | the package's data values | SmartText |
| `classification:<nodeId>` | a classifier's normalised answer (v10) | SmartText |
| `run:date` \| `run:job` \| `run:package` \| `run:host` | the run: completion date (UTC, `yyyy-MM-dd`), the job id's first 8, the package ref, the `apiHost` | SmartLink: facts about the run |
| `profile:name` | the account's display name | SmartLink |

Publish-time validation: every placeholder must name a declared input, a
declared data value, a declared classifier node, or a word from the
closed `run:`/`profile:` list — anything else is refused naming the token.
A macro referencing an **optional** input draws a warning (the author
chose it knowingly); at run time an absent optional input renders as the
empty string. Resolution is **substitution at assembly** — no model, no
Scriban, no recursion (a macro cannot include a macro), deterministic by
construction. An input placeholder may carry patient text; that text is
already in the document's world, and the no-PHI rules bind logs, not
documents.

What placeholders may *not* reach, by the closed list: other runs, the
account's settings, free-form profile fields, the clock beyond the run's
own completion date. `profile:signature` is deliberately absent — the
signature is § 5's flag, with placement and recording of its own.

### The append rule

A deliverable's assembled document is: the aggregated sections exactly as
`ConsultAggregateRenderer.Render` produces them (its no-prologue,
no-epilogue contract is untouched — the append happens **after** `Render`
returns, in the engine, `ConsultGenerationEngine.cs:436-466`), then each
macro of `results[].macros` in declared order, each separated by a blank
line, expanded, verbatim — no heading is invented for a macro; its file
brings its own markdown. Then the signature (§ 5), last. The document is
recorded (`CompleteResultDocument`) with the appended text already in
`Text`, so everything downstream — the app, History, the PDF, the
delivery email, a #510 reuse — is one text.

## 5. Signatures (normative)

- `results[].signature: true` — the package says this deliverable is
  signed. That is the manifest's entire vocabulary: no placement, no
  per-package signature text, nothing of the person in the package (the
  registry's own argument against author fields stands:
  `package-format-v5.md`, publication facts are stamped, never asserted).
- The **profile** owns one or more named signature blocks and a chosen
  default (#516's profile half; the address-card pattern, explicit
  initialisation — a new account has none, and nothing is appended until
  one exists and is chosen).
- **Snapshot at start**: the chosen signature (id, text, its as-of date)
  rides the orchestration input, appended last, as `EmailRequested` does —
  a signature changed mid-run does not change what was promised, and a
  sleeping scheduled job signs with what was chosen when it was submitted.
- **Appended at completion, inside the hash**: after the deliverable's
  macros, before `CompleteResultDocument`, so `documentHash` and the
  result-set hash cover it and every surface shows one text. The
  delivery leg reads the recorded document — the in-memory copy the PDF
  is built from today (`ConsultGenerationEngine.cs:780-783`) reads
  `AssembledDocuments[].Text` from the ladder's rung (c) on, closing the
  fork.
- A signed deliverable on an account with **no chosen signature** is
  produced unsigned, and the record and History say so by name
  (*signature requested by the package; none chosen on the profile*) —
  explicit initialisation, never a silent hold, never a refusal (the
  document is the work; the signature is a block on it).
- The email door: the package's flag plus the account's chosen signature;
  no form is needed, which is why the package decides (#516 option 1).

## 6. Reproducible stages (normative)

- `nodes[].reproducible: true` — the package's claim that this node's
  output is the same for the same input. Any node may carry it; the
  classifier and concept-extraction stages are the intended cases.
  Refused below 11 by name. No behaviour changes at run time: the flag is
  carried, not enforced.
- The **rerun verdict** (#549/#550) reads it: a rerun **passes** when
  every reproducible node's `outputHash` equals the source run's for the
  same `inputHash`, and **fails naming the first that differs**;
  non-reproducible nodes are shown, not counted.
- The honest caveat, recorded: reproducibility is the agent's property as
  much as the package's — temperature, the tool's determinism (the
  terminology build is attested per run, #403). The format's promise is
  only that the package *asked*; a failed verdict is attributable through
  the attestation, and the record's § 4 gap (model checkpoint and sampling
  parameters are not recorded) is unchanged by this flag.

## 7. Provenance

**No hash definition moves.** The per-document hash stays SHA-256 of
`assembledDocuments[].text` (unversioned by design, hash-definitions § 5);
appended text is inside it because it is inside `text` before the hash is
stamped at completion (`StampOutputHashes`,
`ConsultGenerationState.cs:1058-1092`); no node's `inputHash`/`outputHash`
changes, because appended text is not a node's work. The aggregator's
`outputHash` is over `Render`'s bytes, before the append.

The record gains, per deliverable, `appended[]` — in applied order:
`{ kind: "macro", id }` (the package version already on the record pins
the template's bytes) and `{ kind: "signature", id, asOf }`; and the
deliverable-level *unsigned although requested* state when that is the
case. `nodes[]` gains `reproducible` beside the declared fields it already
snapshots. The provenance registry documents both and bumps; the rerun's
`rerunVerdict` is #549's bump and may travel with it. *(As built: #549
shipped the rerun and its report-only comparison table with the `rerun`
origin kind — provenance `v2026.08.12`; the verdict and its record
fields are #582's, with their own bump.)*

**The control**: a v11 package with no `macros`, no `signature: true` and
no `reproducible: true` hashes and renders byte-identically to its v10
self — no append enters, no field appears — which is the assertion that
nothing a v10 package does was redefined.

## 8. Versioning mechanics

The accepted set becomes exactly **{5, 6, 7, 8, 9, 10, 11}**; no version
retires. `AcceptedSpecVersions` leads `SupportedSpecVersions` —
publishable before runnable, the editor's two ceilings and its verbatim
not-yet-runnable notice return — until *the engine runs eleven* flips the
gate together with the registry pin. Engine first, registry second: the
schema is generated from the manifest type and the conformance set from
engine outcomes, so `package-format-v11.md`,
`schemas/package-format-v11.schema.json`, `conformance/v11/` plus the
`v10/invalid-*-at-v10` gate cases, the README's counts and the
unsupported-set sentence publish as one registry version after the engine
PRs. Every new key passes the three gates: the C# record field (the
serializer disallows unmapped members), the validator's
refusal-by-name below 11, and the accepted/supported pair. Migration: a
v10 package is a v11 package with one edit (`specVersion: 11`); nothing
is required.

## 9. Editor implications

- A **Macros pane**: macro files listed and edited like prompts
  (`addedPrompts`' shape: add by id, `macros/<id>.md`, a text pane), a new
  pending kind in the registry `BuildPendingKinds` and the draft payload.
- The **Documents pane** gains, per deliverable, its macro list (ordered,
  from the declared macros) and the **signed** toggle; both mutate
  `resultsEdit` as `when` does.
- The **node editor** gains the **reproducible** toggle.
- Client-side gating mirrors the validator: each new form names
  specVersion 11 and points at the upgrade button; publishing at 11 while
  the engine runs 10 shows the standing notice.
- Placeholder help: the pane lists the declared inputs, data values and
  classifiers plus the closed `run:`/`profile:` words; an unknown token is
  flagged before publish with the validator's sentence.

## 10. Content & rollout

`general` is the control — byte-identical output and hashes at 10 and 11.
The demo is `example-classifier-scope`: the decline letter gains
`signature: true` and a `closing` macro whose template uses one of each
sense (`{{input:...}}`, `{{run:date}}`), the first v11 package, published
when the gate flips. The operator's profile gains a signature first (the
#516 profile half has no format dependency and may land any time).

## 11. Candidates not taken

**Macro kinds in the manifest** (`kind: phrase|text|link`) — the senses
differ in what a placeholder may name, not in what a macro is; a kind
field would be three grammars where one suffices.

**Optional per-run macros** (`optional: true`, a setup-form checkbox) — a
run-time control outside the declared inputs: its own request field, its
own record field, unreachable from the email door. The shape is mapped —
`macroChoices` on the request, `appended[]` already names what applied —
so a later version can take it if the trigger fires. A package wanting a
choice today declares a boolean input and reads it in a prompt.

**A personal snippet library** (the clinician's own dot phrases) — an app
feature, not grammar: profile-owned canned text inserted into inputs at
setup, becoming ordinary typed text. Filed as #561.

**`profile:signature` as a placeholder** — the signature has placement,
absence semantics and an as-of date of its own (§ 5); folding it into the
namespace table would lose all three. A later version may unify once both
have run.

**A macro inside a prompt** — that is a data value, which exists.

**Per-run signature choice and sign-everything** (#516 options 2 and 3) —
the email door kills the first; the second signs an internal summary like
a letter.

**Signature at delivery, outside the hash** — #516's literal wording,
declined: the stored document and the delivered document would differ, the
hash could not attest what was sent, and the browser's record check would
pass a text the patient never saw. Resolved to completion-time (§ 5).

**Section-level macro placement** (a macro between two sections) — the
append rule covers the named cases; a placement marker is aggregator
grammar, a bigger step, unasked.

## 12. The implementation ladder

Filed as M22 issues when this record lands, in dependency order; each is
its own PR; (a) makes v11 publishable, (g) makes it runnable.

- **(a)** Validator accepts 11; `macros`/`results[].macros`/
  `results[].signature`/`nodes[].reproducible` on the records; every new
  form refused below 11 by name; placeholder validation; the schema
  generator keys by version. *Publishable, not runnable.*
- **(b)** Macros in the engine (#513): expansion and append at assembly,
  `appended[]` on the record, the replay pins.
- **(c)** Signatures (#516): the profile's signature card; the snapshot at
  start; the append at completion; the PDF leg reads the recorded text;
  the unsigned-although-requested state.
- **(d)** Reproducible carried (#550): the flag on descriptor, payload and
  record. (The verdict itself is #549's, after inputs are stored.)
- **(e)** Editor: the Macros pane, the deliverable's macro list and signed
  toggle, the node toggle, the gating notices.
- **(f)** Provenance registry: `appended[]`, `nodes[].reproducible`, the
  unsigned state; a worked example.
- **(g)** *The engine runs eleven*: the gate, the format registry's v11
  publication (spec, schema, conformance, counts), the submodule pin, the
  demo package, `general` the control.
