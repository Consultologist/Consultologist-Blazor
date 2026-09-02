# Package format v12: the deferred grammar, taken — design

**Status: design record for Milestone 23 (settled 2026-09-02),
implementation tracked by the ladder in § 12.** v12 takes the three
grammar changes v11 § 11 deferred: **optional per-run macros**
(`macros[].optional` + `default`, `macroChoices` on the request),
**section-level macro placement** (`results[].macros` entries widen to
an object form with `before`/`after`), and **`profile:signature` as a
placeholder** (the token joins the closed `profile:` namespace,
version-keyed). No key retires; a v12 package using none of v12 is
byte-identical to its v11 self.

Decisions taken with the operator (2026-09-02): an optional macro
**declares its default** — the package decides what a formless run
does, #516's answer carried forward; placement lives **on the result,
never in the aggregator's source list** (§ 4 — the aggregator's hash
and its downstream binds stay pure); the signature's **strictly-last
placement demotes from invariant to default** while the
unsigned-although-requested state and the as-of date survive the fold
(§ 5); per-run *signature* choice stays rejected — the token may not
ride an optional macro.

## 1. Motivation

v11's constructs shipped with three roads mapped but not taken, each
deferred with a recorded trigger (#577). v12 takes them as
**foundations built ahead of evidence**: the format is the product's
grammar, satellite intake is arriving (M23's integration spikes), and
the operator judged that authoring against the full macro/signature
grammar now beats migrating authored packages through three later
bumps. The three:

- **A choice that must not touch the model or the hash.** A per-run
  yes/no on an appended block — the medico-legal disclaimer some
  referrals carry — where the workaround (a boolean input read by a
  prompt) routes a verbatim-placement decision *through a model* and
  pollutes the effective-input hash with presentation.
- **A block that cannot go last.** Append-after-everything is v11's
  only placement; a disclaimer preceding the findings, a block between
  two named sections, has no spelling.
- **A signature where the letter wants it.** `results[].signature`
  pins the block last; a letter with an enclosures list wants the
  signature above it.

## 2. The trigger, and that it did not fire

The audit ran 2026-09-02, two days after v11's gate: the live forks
sit at spec 8, 9 and 10 — no macros, no signatures, no boolean-input
workarounds; the only v11 package is the demo; the email-door half of
candidate 1's trigger was unsettled (this document settles it, § 3).
**No trigger fired.** The operator's decision is to build all three
anyway — the v8/v10/v11 § 2 tradition of a recorded override, never a
fired trigger dressed up. The risk the discipline guards — guessing a
shape a real letter would have corrected — is accepted and mitigated
two ways: the guessed assumptions are written down where the first
real letter can test them (§ 11), and the operator's own forks migrate
to v11 in parallel, so evidence starts accumulating the day this
merges.

## 3. Optional per-run macros

### Grammar

```yaml
macros:
  - { id: closing, label: Closing paragraph, file: macros/closing.md,
      optional: true, default: true }
```

`WorkflowMacroSpec` gains trailing `bool? Optional` and `bool?
Default` (nullable for the `WorkflowResultSpec.Signature` reason — a
plain bool would write `false` onto every v11 manifest).
**`optional: true` requires a declared `default`.** The email door has
no form, so the package must say what a run that makes no choice does;
an implied default is exactly the implicitness the trigger discipline
feared. This settles candidate 1's email-door half: **every door takes
the declared default when no choice arrives — the package decides**
(#516's answer, door-agnostic by construction).

### The choice on the request

`ConsultGenerationRequest` gains a trailing optional, appended last
(the `InputFormRefs` discipline):

```csharp
Dictionary<string, bool>? MacroChoices = null   // macro id → chosen
```

Keyed by **macro id**, not (result, macro): one checkbox per optional
macro on the setup form; a package wanting "the closing on the letter
but not the summary" declares two macros. The starter refuses a key
naming an undeclared or non-optional macro, by name.

### Resolution in the starter, and the hash

Resolution happens **entirely in the starter**, the signature-snapshot
pattern: each result descriptor's macro list is filtered — required
macros always, optional macros when the resolved value (choice, else
default) is true — declared order preserved, before the orchestration
input is built. The engine's append path is unchanged; a scheduled job
sleeps with its submission-time choices, the § 5 snapshot rule for
free.

`MacroChoices` stays **out of `effectiveInputHash` by construction**:
the hash covers `inputs.Supplied` and nothing else (hash-definitions
§ 2), and a separate request field never enters it. That is the
construct's point — the choice is presentation, not clinical input.
No second hash is added (§ 10); the record carries the choices
explicitly instead (§ 6).

## 4. Section-level macro placement

### Grammar — on the result

```yaml
results:
  - id: letter
    node: node:assemble_letter
    macros:
      - { id: disclaimer, before: node:findings }   # placed
      - closing                                     # v11 string form:
                                                    # after everything
```

`results[].macros` entries widen to string-or-object. The object form
carries `id` and **exactly one** of `before` / `after`, naming a
`node:<id>` present in the deliverable's aggregator's `aggregate`
list. The plain string keeps v11 semantics byte-for-byte — no
migration.

### Why not the aggregator's source list

The tempting spelling — `aggregate: ["node:intro", "macro:disclaimer",
…]` — breaks three v11 invariants at once:

1. **The hash leak.** The aggregator's `outputHash` is over `Render`'s
   bytes, before any append (v11 § 7). Macro text carries
   `{{input:…}}` and `{{run:date}}`; inside the source list, two
   clinically identical runs on different days diverge at the
   aggregator.
2. **The model leak.** An aggregator's output is bindable into
   downstream prompts (v6). A mid-graph macro would flow into a model —
   the exact failure v11 § 1 exists to prevent.
3. **The grammar cost.** The aggregator check holds one rule: every
   source is `node:<id>`. A `macro:` pseudo-source needs carve-outs
   (result-owning aggregators only, no downstream binds, no hash
   contribution) — a patched rule is the shape of a wrong guess.

### Engine behaviour

At the assembly site, parts align 1:1 with the aggregator's sources.
The deliverable's text composes per source, in aggregate order: macros
placed `before` it (expanded), the rendered part, macros placed
`after` it; then unplaced macros in declared order; then the
signature; all blank-line separated. **Normative and load-bearing: the
aggregator's `outputHash` and its recorded node output stay over
`Render(parts)` — the pure aggregation.** No node hash moves;
downstream binds never see macro text; only `documentHash`, computed
over the final text at completion, covers the interleaved document. An
anchor on a `forEach` source places the macro around the **whole
fanned block**, never between fan items.

## 5. `profile:signature` as a placeholder

`"signature"` joins the closed `ProfileFacts` set, **version-keyed**:
on a specVersion-11 manifest the token is refused as *requires
specVersion 12*, never the misleading "does not resolve". The three
v11 semantics, each accounted for:

1. **Placement — demoted from invariant to default.** That is the
   point of the fold: the token puts the signature wherever its macro
   (and § 4's placement) puts it. `results[].signature: true` remains
   valid forever as the spelling for "append the chosen block last";
   the v11 flag is not deprecated and nothing migrates.
2. **Unsigned-although-requested — kept.** The starter's snapshot gate
   widens from "the flag is true" to "…or any referenced macro's
   template carries the token". With no chosen signature block the
   token renders as the **empty string** (the § 4 optional-input
   semantic) and the deliverable records `unsigned: true` exactly as
   today — the macro's surrounding text still lands; the record names
   why the slot is empty. Suppressing the whole macro instead was
   rejected as magical (§ 10).
3. **The as-of date — kept.** Exactly one `{ kind: "signature", id,
   asOf }` `appended[]` entry per signed deliverable, flag-appended or
   token-embedded alike.

**A document is signed once**, validator-enforced: the flag plus a
token-carrying macro on one result is refused; so is a second token.
And the token may not ride an **optional** macro — that would be
per-run signature choice, which #516 option 2 rejected (the email door
kills it) and which stays rejected.

## 6. What the record says

- **`macroChoices`** (job-level, provenance registry bump): one entry
  per *optional* macro — `{ <id>: { value: bool, origin: "chosen" |
  "default" } }` — stamped from the starter's resolution. The negative
  is visible (#315's discipline: recorded, not omitted); required
  macros carry no entry; a package with no optional macros writes a
  byte-identical record.
- **`appended[]`** — entries keep their shape; the registry sentence
  "after the aggregated sections, in applied order" re-words to
  **document order**, "after the sections" becoming the default rather
  than the definition. No placement field is added: the record's
  pinned package version already says where each macro sits.
- **The embedded signature** still yields its `kind: "signature"`
  entry (§ 5.3); `unsigned` is unchanged.

## 7. Hashes and invariants

Unchanged, now stated as v12 conformance facts: `effectiveInputHash`
covers supplied inputs only — `macroChoices` never enters it; every
node `outputHash` is untouched by macros, placement included (§ 4's
purity rule, pinned by test); `documentHash` alone covers appended and
interleaved text; the rerun verdict (#549) compares clinical content
and cannot be moved by presentation choices — which is § 3's
motivation made checkable.

## 8. Validator rules (all refusals by name)

Below 12: `optional`/`default` on a macro; the object entry form in
`results[].macros`; `{{profile:signature}}` in a macro file.

At 12: `optional: true` without `default`; `default` without
`optional: true`; both `before` and `after` on one entry; a placement
naming a source the deliverable's aggregator does not aggregate; a
placement on a result whose node declares no `aggregate`; the
signature flag beside a token-carrying macro; two signature tokens on
one result's macros; the token inside an optional macro.

Unchanged: the orphan rule (a placed macro counts as referenced),
undeclared references, duplicate ids, the placeholder closed sets.

Starter-side (request, not package): a `MacroChoices` key naming an
undeclared or non-optional macro refuses the start.

## 9. Editor implications

- The **Macros pane** gains the optional/default pair (explicit-init
  rule: turning `optional` on forces a default choice before publish).
- The **Documents pane**'s macro rows gain a placement picker fed by
  the deliverable's aggregator's source list (none = after
  everything, the v11 string form).
- The **placeholder help** lists `profile:signature` at 12 with the
  signed-once and never-optional rules in its sentence.
- The **setup form** renders one checkbox per optional macro,
  defaulted from the manifest; the request carries `macroChoices`.
- Client-side gating mirrors the validator: each new control names
  specVersion 12 and points at the upgrade button; publishing at 12
  while the engine runs 11 shows the standing notice
  (publishable-before-runnable, the two ceilings).

## 10. Candidates not taken

- **`macro:` in the aggregate source list** — § 4's three broken
  invariants.
- **A compound `(result, macro)` choice key** — no named case; two
  macros spell it.
- **`presentationHash`** — a second hash with no verifier and no
  comparison partner; the record's explicit `macroChoices` is directly
  auditable.
- **Suppressing a macro when unsigned** — magical; the macro may carry
  text that must land.
- **Per-run signature choice** — re-rejected; #516 option 2's email
  argument is unchanged, and § 5's never-optional rule enforces it.

## 11. The assumptions a real letter would test

Written here so the first authored v12 letter checks them rather than
inheriting them silently:

1. **The section is the right placement granularity.** This grammar
   cannot put a macro between two items of one fanned source, or
   inside a section — deliberately. A real letter wanting either fires
   the next design pass.
2. **A downstream bind must not see mid-document macros.** The
   aggregator's output stays the pure render; an author who binds a
   result aggregator into a later prompt gets the sections, never the
   macros. If a real package wants the opposite, that is a new
   argument, not a bug.
3. **Defaults suffice for formless doors.** If email-started runs turn
   out to need a standing per-account choice rather than the package's
   default, that is candidate (d) from § 3's alternatives returning
   with evidence.

## 12. The ladder

Accepted set becomes exactly {5…12}; nothing retires.
`AcceptedSpecVersions` leads `SupportedSpecVersions` — publishable
before runnable — until the gate flips. Engine first, registry second;
the format registry's v12 publication (spec, schema, conformance,
counts) lands as one registry version after the engine rungs, v11
§ 8's order.

- **(a)** Validator accepts 12: the three grammar shapes, every § 8
  refusal, the version-keyed profile set, schema generation.
  *Publishable, not runnable.*
- **(b)** Optional macros: `MacroChoices` on the request, starter
  resolution and descriptor filtering, the record's `macroChoices`.
- **(c)** Placement in the engine: per-source composition, the
  aggregator-hash-purity test pinned, `appended[]` in document order.
- **(d)** The signature token: widened snapshot gate, token
  resolution, the appender's embedded mode, unsigned and as-of
  preserved. *(After (c) — same append path.)*
- **(e)** Editor: § 9, all of it, gated at 12.
- **(f)** Provenance registry bump: `macroChoices`, the re-worded
  `appended[]` sentence, the embedded-signature entry, a worked
  example.
- **(g)** *The engine runs twelve*: the gate, the format registry's
  v12 publication, the submodule pin, a demo package exercising all
  three constructs, `general` the control — a v12 package using none
  of v12 hashes and renders byte-identically to its v11 self.
