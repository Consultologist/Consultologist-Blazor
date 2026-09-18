# Product Stages: What Comes After the Customizable Workflow

Recorded 2026-07-09. Question considered: from an app / consult-generation perspective,
is the app complete once the customizable-workflow vision (packages, DAG, provenance) is
implemented? Answer: no — it is stage 1 of roughly four, and a well-chosen stage 1.
EMR integration and scheduling are planned separately and bracketed out here (the
copy-back-parity-now / native-integration-deferred positioning is recorded in
`decoupling-roadmap.md` § "EMR interoperability positioning", #188).

The recurring theme: the SNOMED-validated concept layer — which most competing
documentation tools do not have — is the asset that the later stages cash in on.

## Stage 1 — the generation engine (the customizable-workflow vision)

Input draft → validated concepts → sectioned note; versioned workflow packages,
specialty bundles, per-consult provenance, harness independence. This is the compiler.
See the rest of this folder. The test of a good stage 1 is that later stages land as
content and node types on the same rails rather than new architecture — this one passes.

## Stage 2 — the learning loop

Today the physician's edits to a generated note go nowhere: the single most valuable
signal the app receives is discarded. Capture the diff between the generated note and
the finalized note to get:

- real eval data for eval-gated package publishing (golden fixtures mined from actual
  edits instead of synthetic ones)
- evidence for which prompts underperform per specialty
- eventually, per-physician style adaptation

The package registry provides the *mechanism* for improvement; edit capture provides
the *signal*. Without stage 2, quality is frozen at whatever the package author guessed.
This is the piece that turns the engine into a product.

The in-app package editor (#57, shipped 2026-07-16 — see
[in-app-editing.md](in-app-editing.md)) is stage 2's authoring seed: the loop's
"act on the signal" half already exists as fork-publish-activate with full
provenance. What stage 2 adds is the signal itself — edit capture feeding
eval-gated publishing on these same rails.

**Sequencing note:** edit capture is much easier *before* EMR integration, while
finalization still happens inside this app — an argument for doing stage 2 early.

## Stage 3 — the verification and safety layer

The prompts say "use only facts from the draft" but nothing checks. The concept
infrastructure enables:

- **Claim verification**: a workflow step that extracts the claims in the generated
  note and checks each against the draft's validated concepts, flagging unsupported
  statements before the physician sees them — hallucination detection as a first-class
  pipeline stage.
- **Completeness checks** against the specialty standard (an oncology consult missing
  staging gets flagged, not shipped).

This earns clinical trust, differentiates against prose-only scribes, and complements
provenance: provenance says *what produced this note*; verification says *what in this
note is supported*. Structurally it is just another DAG node type — stage 1 builds its
rails.

## Stage 4 — outputs beyond the note, and the platform

An encounter does not end at one document:

- **Referring-physician letter** — same analysis outputs, different audience and tone;
  trivially a second workflow package.
- **Patient-facing summary** — likewise.
- **Billing/coding suggestions** — the sleeper: SNOMED concepts are already in hand,
  SNOMED→ICD mapping is well-trodden terminology work, and the MCP server is positioned
  for it.
- **Registry submissions and clinic analytics** — byproducts of structured concepts
  rather than projects.
- **The platform endgame**: once packages are versioned, evaluated, and publishable,
  specialty packages authored by other clinicians become an ecosystem — Consultologist
  as the registry and runtime, the clinical community as the content authors.

## Overall assessment

The customizable-workflow milestone is roughly the first third of the consult-generation
story. Every later stage — verification nodes, edit-driven evals, letter and coding
workflows — lands as content and node types on the stage-1 rails.
