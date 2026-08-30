# Customizable Workflow — Design Notes

Design records, begun 2026-07-09, on turning the consult-generation workflow and
section template system into versioned, code-independent artifacts. Since then
formats v2–v10 have shipped and v11 is designed (2026-08-31), the normative
specifications live in their own registry (#376) and the record contract in
another (#400); what stays here is why each was chosen. Public by design, like the rest of this repository.

## The goal

1. Update default templates and workflows completely separately from the codebase, with
   the defaults versioned the same way model weights are versioned (immutable, pinned,
   published).
2. Bundle collections of defaults tailored per specialty (e.g. breast oncology,
   cardiology) instead of a single hardcoded default workflow.
3. Record full provenance per consult so any other harness can re-run the system from
   published artifacts: open model weights, workflow definition, SNOMED edition, and the
   Apache-2.0 MCP server code — with no dependency on this web app.

## Contents

> **The normative package-format specifications moved out (#376).**
> `package-format-v5..v8.md` now live in their own versioned registry,
> [consultologist-package-format](https://github.com/Consultologist/consultologist-package-format),
> published alongside the packages they govern and vendored here as
> `external/consultologist-package-format`. What stays below is the design
> record — why each format was chosen, which is a different audience from what
> the format *is*. The links to v5–v8 in this file point outward.


- [current-state.md](current-state.md) — what is hardcoded vs. data-driven today
  (verified against the code)
- [workflow-packages.md](workflow-packages.md) — the versioned "workflow package"
  concept, registry, pinning, specialty bundles, and the milestone sequencing
- [provenance.md](provenance.md) — the provenance design record: why the record is
  shaped as it is, attestation, reproducibility limits, and the legs declared but not
  yet recorded. The normative contract moved out (#400) to
  [consultologist-provenance](https://github.com/Consultologist/consultologist-provenance),
  vendored as `external/consultologist-provenance`.
- [dag-improvements.md](dag-improvements.md) — prioritized improvements for when the
  DAG-as-data milestone is built (structured-output edges, eval-gated publishing,
  human-in-the-loop nodes, memoization, per-step provenance, safety rails)
- [product-stages.md](product-stages.md) — where this fits in the larger product: the
  workflow vision as stage 1 of four (engine → learning loop → verification layer →
  outputs and platform)
- [completeness.md](completeness.md) — what remains after all the stages (governance,
  regulatory track, evidence, the moving substrate) and the marker for "truly done
  building"
- [package-format-v2.md](package-format-v2.md) — normative specVersion-2 format:
  prompt templates (Scriban), variable contract, strictness rules, rollout
- [package-format-v3.md](package-format-v3.md) — normative specVersion-3 format:
  package-defined section steps, declarative bindings, fallback retirement
- [content-repos.md](content-repos.md) — splitting workflow and agent content into
  dedicated GitOps repos publishing to the registry (CI-only writes, full agent GitOps,
  migration checklist)
- [package-format-library.md](package-format-library.md) — what defines the format
  lives in `src/Consultologist.PackageFormat` (#450); what runs it stays with the engine.
- [registry-operations.md](registry-operations.md) — browsing and inspecting the
  package registry (az CLI, portal, Storage Explorer) and the publish flow
- [decoupling-roadmap.md](decoupling-roadmap.md) — post-milestone-2 forecast: phase
  plan for milestones 3–4, the refactoring bill, risks, and the precise end-state
  boundary of the "generic engine" claim
- [package-format-v4.md](package-format-v4.md) — normative specVersion-4 format:
  the node DAG (implicit edges, one map node), schema file refs welded to attested
  agents, per-node failure policy, the v4.0 closures
- [package-format-v10-design.md](package-format-v10-design.md) — design record
  (2026-08-26, Milestone 21): the classifying node and the boundary at which
  the fire set is decided, a condition evaluator with `and`/`or`/`not` and
  arithmetic, unbounded nesting and hash definition 6; the ladder of PRs;
  the three v9 § 13 roads taken deliberately, trigger unfired
- [package-format-v11-design.md](package-format-v11-design.md) — design record
  (2026-08-31, Milestone 22): macros as package-owned templates with
  placeholders from closed namespaces (the three clinical senses, one
  grammar), the per-deliverable signature flag appended at completion
  inside the hash, and `reproducible` on nodes for the rerun verdict; the
  ladder of PRs; trigger unfired, said so
- [forms-intake-spike.md](forms-intake-spike.md) — spike record (#511, 2026-08-28):
  a Microsoft Forms response as an intake source — no supported API, so the
  clinician's Power Automate flow pushes responses with the clinician's own
  token into a held list the setup form picks from; organisation sign-ins
  only; experiments E1–E4 pending
- [storage-separation.md](storage-separation.md) — design record (#545,
  2026-08-30): the four classes of state — inputs, outputs, history/record,
  usage — each with its store, key, retention, deleter and residency; a text
  (PHI) account and a records account per region; what a purge leaves
  behind; the migration order
- [package-format-v5-design.md](package-format-v5-design.md) — converged design
  (2026-07-15): fork-everything with derivedFrom lineage, data/
  collections replacing the standards straddle, one node kind with forEach,
  per-item provenance; executing as Milestone 5 (#59)
- [package-format-v5.md](https://github.com/Consultologist/consultologist-package-format/blob/main/package-format-v5.md) — normative specVersion-5 format:
  derivedFrom fork lineage, the data table and self-describing collections, one
  node kind with forEach, item-aligned edges, the result contract, per-item
  provenance and the versioned draft-only input hash
- [in-app-editing.md](in-app-editing.md) — design (2026-07-15, not yet
  implemented): the Templates page as a package editor publishing immutable
  per-account versions, the acct-* access rule, and the publish pipeline
- [output-contract-catalog.md](output-contract-catalog.md) — next-slice design
  (2026-07-15, not yet implemented): schema-keyed agent selection as engine
  configuration, the one-kind node calculus, derived DAG visualization, and the plan
  folding in the prose-step unification
- [dag-as-data-design.md](dag-as-data-design.md) — milestone 4 phases B–D at
  implementation resolution (specVersion-4 nodes/schemas, interpreter, migration);
  gate lifted 2026-07-14, executing

## Key terms

- **DAG** (directed acyclic graph): the workflow expressed as steps (nodes) and
  data-dependency arrows (edges), no cycles. Today's pipeline is a DAG expressed in C#
  control flow; "DAG customizable" means promoting that structure to data.
- **Workflow package**: a named, versioned artifact bundling section standards, prompt
  templates, per-step model parameters, and (eventually) the step/DAG definition.
- **Attestation**: a runtime check that a deployed, mutable thing (the Foundry agent,
  the MCP app) matches its git-tracked source of truth; fail or warn loudly on drift.
