# Decoupling Roadmap: Forecast and End-State Boundary

Recorded 2026-07-13, immediately after milestone 2 closed (#4: prompts into packages).
Two questions considered: the trajectory toward full content/pipeline/orchestrator
decoupling with the refactoring each phase demands, and what "the app becomes a generic
engine" precisely does and does not mean.

## Where the boundary sits after milestone 2

**Decoupled into content**: standards, the seven prompt texts, the SNOMED tool
guidance. The agent binding is externalized on its own axis (Foundry version pin +
attestation).

**Still compiled**: the prose step *sequence*, the analysis DAG's shape and typed
contracts (bullet-format parser, the two concept formatters), the progress/state
vocabulary (stage names, named entity fields, History UI labels), the section fan-out,
per-step model parameters.

## Phase forecast

### Now → milestone 3 (#5, specVersion 3)

Expect a period of pure **content iteration** first — clinically tuning prompts and
standards through the registry is what milestone 2 made cheap, and it will surface step
-customization requirements organically.

Milestone 3's one genuinely new spec question: how a package-declared step names the
**variables it receives** (the current three prose steps each get different inputs) —
v2's fixed per-prompt variable table generalizes into declarative bindings.

Refactoring bill:
- The three named prose activities collapse into one generic `RunProseStep` activity.
  Durable caution: activity names are deployed contract → drain-or-tolerate deploy.
- `AgentSectionGenerator` shrinks toward a pure agent client (three prose methods →
  one render-and-send).
- Progress plumbing is already count-based (anticipated), but `ProseStepStatus` strings
  and History UI labels must stop assuming the three fixed step names.
- **Decision point: retire the compiled fallbacks** (make a package mandatory).
  Forecast: yes, around M3 — the parity tests retire with them, having done their job.
  Delete the legacy direct-SSE endpoint (`Agents/ConsultGeneration.cs`) in the same
  sweep (flagged as deprecation candidate in the v2 spec; it drags compiled prompts).

### Milestone 4 (#6, specVersion 4) — the big one (~3–5× milestone 2)

> **Status 2026-07-14**: milestone 4 **shipped in full** — the gate was lifted by
> decision rather than by a second consumer. Phases 0/A (file split, structured
> outputs via the attested concept-extraction agent) and B–D (specVersion 4, the
> wave interpreter, `general@v2026.07.5` as the first DAG-as-data package, cleanup)
> are all in production, verified with cross-version per-node hash comparisons.
> One correction to the forecast below: the concept formatters did **not** move into
> the package format — they stayed engine-pinned as the closed renderer vocabulary
> (`concept-bullets`/`concept-context`), which the "stays in the app" binding-contract
> list already anticipated. The bullet-format parser died as forecast, replaced by
> agent-enforced structured outputs rather than request-level schemas (Foundry
> constraint, spike #39).

DAG-as-data: the orchestrator becomes an interpreter — topological walk,
`Task.WhenAll` on parallel-ready nodes, a **map/fan-out node type that subsumes the
section loop** rather than special-casing it. The deep refactor is everything the fixed
shape currently touches:

- **The parser dies**: structured outputs (JSON-schema edges, dag-improvements.md #1)
  replace the bullet-format regime. Cascades: agent instructions lose the STRICT bullet
  rules (new agent version), validation warnings become schema violations, and the
  concept formatters either move into the package format or get pinned as engine spec —
  the piece that finally makes provenance.md's harness-independence claim true.
- **Entity state generalizes**: named fields (`PatientConcepts`, `ProblemContext`, …)
  and `ConsultGenerationAnalysisStatuses` become a per-node-id output map — job
  `SchemaVersion` 3, History UI genericizes to step lists.
- **Step zero: split `ConsultGenerationJobs.cs`** (~2,700 lines holding orchestrator,
  activities, entity, parser, SSE streaming) into engine/state/transport files. M3
  nibbles at it; M4 is intractable without the split.

### #16 (content repos / GitOps) — expected to keep sliding, correctly

The logic that put it after milestone 2 (don't put CI infrastructure between the author
and format iteration) applies through M3 and arguably M4. It is enforcement and
hygiene, not capability; its natural moment is when the package format stops churning —
or earlier if a second author (a clinician) appears, since CI-only-write is really
about multi-author trust.

### End state

The app becomes a generic durable DAG interpreter + agent client + registry client +
SSE/state transport, with all workflow semantics in published, versioned artifacts. The
product stages on the Consultologist board (verification layer, human-in-the-loop
review, letter workflows) then stop being features and become **node types and
packages** — the test the stage-1 design set for itself.

## Risks

1. **Over-generalizing before a second consumer exists.** M4's gate is "a real
   specialty demands a different shape" (workflow-packages.md) — hold to it; the
   forcing function should be an actual specialty package, not architectural appetite.
2. **In-flight Durable jobs** across each schema/activity-shape change — drain-window
   discipline every phase.
3. **UI lag**: engine genericity outpacing Templates/History, which still speak the
   fixed vocabulary. Budget UI work into M3/M4 rather than letting it trail.

## What "generic engine" precisely means

"No special pipeline code" holds for pipeline *semantics*, not for contracts. The
distinction: **mechanism and contract stay in the app; content and shape leave.**

Gone from the codebase after M3+M4 (assuming fallback retirement):
- Every prompt string (compiled fallbacks and `ConsultGenerationCompiledPrompts`)
- The stage vocabulary (analysis statuses, prose step names, named entity fields)
- The step sequences (three-prose-step chain, four-stage analysis order)
- The bullet-format parser and both concept formatters
- The hardcoded section fan-out (subsumed by the generic map node)

Stays in the app, correctly, forever:
1. **The binding contract** — the engine must know how to *supply* values
   (`consult_draft`, section list + override resolution, predecessor outputs). The
   vocabulary of engine-supplied inputs is code and versioned spec, even though every
   *use* is package-defined — the relationship a language has to programs.
2. **The input model** — "a consult draft plus sections with standards" is the one
   domain assumption that survives M4. The engine is a generic interpreter *of
   consult-generation workflows*, not arbitrary workloads. Package-defined input
   schemas would be a conceivable future milestone (see the final section); nothing
   on the boards demands it.
3. **Policy and trust** — specVersion/engineVersion gates, pin resolution order,
   attestation, the effective-input-hash definition, provenance recording, per-node
   tool allowlists and budgets. These must never be package-controlled: the package
   cannot be the authority on whether to trust the package.
4. **The machinery** — Scriban renderer at pinned version, Durable interpreter,
   registry client, agent client, SSE/entity transport, auth/accounts/CORS.
5. **The frontend** — domain UI by nature; M3/M4 make it more generic (step lists
   instead of stage names) but it remains Consultologist screens.

**Operational test of the end state**: a cardiology deployment — different prompts,
steps, even a different DAG — runs with **zero code changes** (new package, maybe a new
agent version, same binaries). A non-consult product does not; the input model is the
honest edge of the decoupling, and defensibly so — every externalized layer so far
earned it with a concrete second consumer in view.

**Caveat**: "no special code" holds for the app codebase, but the *system* keeps domain
semantics in two externalized places — the Foundry agent (SNOMED tool wiring, output
discipline) and the MCP server. They are versioned and attested rather than compiled —
which is the point — but a reimplementing harness needs them too, which is why
provenance.md counts them as first-class artifacts in the record.

## A conceivable future milestone: the input model as content

Deliberately unplanned — recorded here so the boundary decision stays conscious rather
than accidental. (Formerly numbered "milestone 5"; unnumbered 2026-07-15 when
Milestone 5 came to name the specVersion-5 generation, so this footnote never needs
renumbering again as real milestones land ahead of it.)

> **Reconciled 2026-07-15 with Milestone 5**
> ([package-format-v5-design.md](package-format-v5-design.md)): v5 annexes part of
> this territory. Standards move from the input side into package `data/`
> collections, so "sections with standards" leaves the input model and the
> effective-input hash can shrink to the draft alone. The fixed input this section
> contemplates generalizing is therefore smaller after M5 — closer to "a consult
> draft" — while what remains here (a package-declared input *schema* and a
> schema-driven input form) is untouched and still waits for a demanding consumer.

**What it is.** After M4, packages control the prompts, steps, and graph, but every
workflow still begins from the same fixed input: a consult draft plus sections with
standards. That shape is compiled into the request contract, the effective-input hash,
the binding vocabulary (`consult_draft`, the section list), and the Consults page's
input form. This milestone would move *that* into the package: a manifest-declared
**input schema** (JSON Schema, presumably) defining what each workflow takes in.

**What it would entail**, roughly in order of pain:

- **The frontend becomes schema-driven** — the Consults page stops being a hardcoded
  draft-plus-sections form and renders whatever input form the pinned package declares.
  Most of the cost lives here.
- **The binding contract derives from the schema** — engine-supplied variables come
  from declared input fields rather than a fixed vocabulary, and M4's map node fans out
  over any declared collection, not specifically "sections".
- **The effective-input hash generalizes** to canonical serialization of
  schema-conformant input — mechanical; the hash machinery already works this way in
  miniature.
- **Overrides and standards generalize** into package-defined content slots rather than
  the one hardwired standards document.

**What it would buy.** Workflows over genuinely different source material — discharge
summaries from ward notes, tumor-board summaries from multi-document packets, operative
notes — and, at the platform endgame (product-stages.md stage 4), third parties
building documentation products on the engine without the app growing input handling
for each.

**Why it stays a footnote.** The second-consumer rule that has governed every
externalization so far: nothing on either board needs it. Notably, the stage-4 outputs
(referring-physician letter, patient summary, coding) do **not** — they are additional
workflows over the *same* consult input and analysis outputs, so M4 covers them.

**The realistic forcing function to watch: EMR integration.** The day input stops being
a pasted draft and becomes structured payloads (FHIR bundles, referral document
packets), the fixed input model starts to pinch and this becomes a real milestone
rather than a footnote. Until then it is the honest, defensible edge of the decoupling.

## EMR interoperability positioning (#188)

Strategy record from the 2026-07-23 competitive review. It governs how EMR integration is
*claimed* while it stays the footnote above.

**Copy-back parity, claimable today.** The produced note copies to the clipboard as plain
text (the **Copy note** button, `Consults.razor`) and pastes into any EMR or document — no
integration, no setup. That already matches the incumbent claim "works with every EMR": the
sentence is true today at zero engineering cost and should be stated wherever the product is
evaluated. Its user-facing home in this repo is the public Help page's "Getting the note into
your chart" section; the head-to-head competitor comparison lives on the marketing site
(out of this repo).

**Fax parity, already unblocked.** A referral can arrive by email (#158 intake), and — since
#239 built OCR — a faxed or scanned image-only PDF is read and answered rather than rejected
wherever OCR is configured. A fax-to-email bridge turns a fax number into the #158 intake
mailbox with no fax-specific code. See `ASYNC_DELIVERY.md`, `DOCUMENT_INPUT.md`, and
`CONFIGURATION.md` (the OCR section) — recorded there, not re-derived here.

**Native integration is deliberately deferred** until beta-user demand picks a target. Racing
a funded competitor through per-EMR partner programs (QHR/Accuro, Telus/PS Suite) spends the
differentiation window building their moat. When demand arrives, **OSCAR** is the likely first
native candidate: open-source, Ontario-heavy, no partner-program gatekeeper — and an open EMR
pairs naturally with a publicly-auditable pipeline, which is the product's actual edge. The
Epic/Cerner hospital segment is tracked separately (the SMART-on-FHIR satellites, #654/#662):
different intake, different buyer, different politics.
