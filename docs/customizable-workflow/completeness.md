# After the Stages: What "Complete" Means

Recorded 2026-07-09. Question considered: after all the stages in
[product-stages.md](product-stages.md) (plus the separately planned EMR integration and
scheduling), would the app be complete?

Answer: functionally yes — with the caveat that "complete" for this kind of product
means **the remaining work stops being capability-building and becomes operations,
evidence, and scale**. After the stages, the consult-generation core has no missing
architectural pieces: generation, customization, verification, learning, multi-output,
and provenance all exist on shared rails. New specialties, document types, and workflow
steps are content, not code. That is a legitimate definition of done.

What remains open is a different *kind* of work:

## Team and governance surface

The app is single-physician today. Real clinics have residents drafting and attendings
signing, clinic-level decisions about which package versions are approved, and admins
who are not the users. Roles, review chains, and package governance are the last genuine
feature gap — small compared to the stages, but real.

## The regulatory and trust track

Unavoidable, and never "inside" a feature stage:

- Privacy compliance — PHIPA/PIPEDA (deployment is Canada East), HIPAA if crossing the
  border.
- Security attestations institutions demand (e.g. SOC 2), retention policy for produced text (done, #368: 7 days, provenance kept) and audit policy.
- A genuine classification question: stage 3's claim verification and stage 4's coding
  suggestions edge toward clinical decision support, which regulators treat differently
  than transcription (SaMD territory).

The provenance architecture is a major asset here — it is most of an audit story
already. But this is a workstream, not a milestone with an end.

## Evidence

Adoption in medicine follows demonstrated outcomes: time saved per consult, note quality
versus unassisted, error rates caught by the verification layer. The project is
unusually well-equipped to produce this — the AI-Copyright-Reproducibility harness
mindset, per-step provenance, and edit-capture diffs are literally a study apparatus.
At some point "complete" means *proven*, and that is a paper or a pilot, not a commit.

## The moving substrate

The permanent one. DeepSeek V4 Pro will be superseded; SNOMED ships new editions twice a
year; the hosted serving stack will change underneath. A product with a model inside is
never finished the way a compiler is — but the architecture pre-answers this: new model
or terminology version ⇒ re-run package evals against the new pin, compare hash
distributions, publish updated packages. The maintenance loop is designed, which is as
complete as that problem gets.

## The marker for "truly done building"

The first specialty package authored end-to-end by a clinician who isn't the developer,
running in a clinic that isn't the developer's, surviving a model upgrade via re-eval
alone. Everything after that is a healthy product living its life.
