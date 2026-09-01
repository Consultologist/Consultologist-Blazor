# Workflow Packages: Versioned Content, Decoupled from Code

The goal is not primarily "users editing workflows" — it is **content as versioned
artifacts, decoupled from code deploys**. The pattern is already running in production
for the agent: the app pins `test-json` (version 47 as of 2026-07-13) via the
`AzureAI__AgentVersion` app setting, and new agent behavior ships by publishing a new
version and flipping one setting, with no redeploy. Workflow content gets the same
lifecycle.

## The package concept

A **workflow package** is one named, versioned artifact bundling everything content-ish:

- section standards (what `consult_section_standards.md` holds today)
- prompt templates for each stage/step (Scriban, with declared variables — since
  specVersion 2)
- the section step sequence itself (`sectionSteps`, declarative bindings — since
  specVersion 3)
- per-step model parameters (reasoning on/off, temperature, seed — see provenance.md;
  not yet)
- eventually: the full DAG definition, including per-step output contracts

## Versioning: CalVer `vYYYY.MM.N` (decided 2026-07-09)

Package versions use **CalVer**: `vYYYY.MM.N` — zero-padded month, within-month release
counter starting at 1 (not zero-padded). Rationale: SemVer earns its keep by signaling
interface compatibility, and content packages have no interface — runtime compatibility
is carried separately by the integer `specVersion` in the manifest. A date-bearing
version matches the model-weights analogy (weights and SNOMED editions are date-stamped),
makes staleness visible (a clinical package from 2024 deserves scrutiny), and answers
"which standards were in force last November?" from the identifier alone.

**Comparison is numeric, never lexicographic**: `v2026.07.10` sorts after `v2026.07.2`
numerically but before it as a string (and in blob listings). The registry client parses
and compares numerically (`CalVerVersion` in
`src/Consultologist.Api/Workflow/WorkflowPackageModels.cs`). The provenance record mixes
schemes (agent `45`, package `v2026.07.2`, SNOMED `20260401`) — expected; each artifact
keeps its native scheme.

## Registry, pinning, bundles

> **Milestone 1 implemented 2026-07-09**: blob registry (`workflow-packages` container,
> `{name}/{version}/manifest.json` + `standards.md`, mutable `{name}/latest.json`
> pointer), `WorkflowPackageStore` + authenticated `WorkflowPackages/Current` endpoint,
> account pin setting `consult.workflowPackage` with `WorkflowPackages__Default` app
> setting fallback (`general@latest`), frontend loads standards from the endpoint, and
> jobs record `WorkflowPackage`/`EffectiveInputHash`/`AgentVersion`
> (job `SchemaVersion` 2). Seed package source lives in `packages/general/`; publish
> from the consultologist-workflows repo (tag-triggered CI, OIDC; #16).
> Registry auth is Entra-first: the store uses the app's managed identity against
> `WorkflowPackages__BlobServiceUri` (Storage Blob Data Reader on the account); the
> publish script uses `az --auth-mode login` (Storage Blob Data Contributor for the
> publisher). Connection strings remain only as the local-dev/Azurite fallback.

- **Registry**: packages live in storage — Azure Blob is the natural fit
  (the public `workflow-packages` container and the private
  `org-`/`personal-account-packages` pair since #602, `{name}/{version}/...`),
  published **immutably**: a new version never
  mutates an old one, exactly like model weights or agent versions.
- **Authoring**: keep package sources in their own git repo with CI publishing to blob
  on tag — code-style review and history, weights-style pinning at runtime. (Mirrors
  how `snomed-snowstorm-mcp` is a separate repo with its own deployment.) Full design
  for this split — including the agents repo and the CI-only trust model — in
  [content-repos.md](content-repos.md).
- **Pinning**: an account (or an individual consult run) references `name@version`. The
  current hardcoded behavior becomes the seed package (`general@v2026.07.1`) so nothing in the
  app is special-cased.
- **Specialty bundles fall out for free**: `breast-oncology@v2026.09.2`, `cardiology@v2026.05.1` are just
  different packages in the registry; the Consults page grows a package picker.
- **Account customization becomes thin overrides layered on a pinned package** (like
  today's `consult.sectionStandardsMarkdown` overriding the bundled file), not
  free-floating documents. Overrides change the effective-input hash (provenance.md).

  > **Superseded 2026-07-15 (v5)**: overlays lost. The override retired with the
  > specVersion-5 input model (#71); account customization is package *forking*
  > (`derivedFrom` lineage, in-app editor #57 —
  > [in-app-editing.md](in-app-editing.md)). See the Milestone 5 block below.

## Two rules imposed by Durable Functions and by versioning

1. **Snapshot the package (name, version, resolved content hash) into the orchestration
   input at job start.** Durable replay requires the definition to be immutable per
   instance — never re-read settings or the registry mid-run.
2. **Published versions are immutable.** Corrections are new versions. This is what
   makes the provenance record meaningful.

## Important scoping observation

**Specialty bundles do not require the customizable DAG.** If specialties share the
pipeline shape (concept extraction → problem → trajectories → sectioned prose) and
differ only in prompts, section lists, and standards, then content-only packages deliver
the entire stated goal while the DAG stays compiled. DAG-as-data becomes necessary only
when a specialty needs a structurally different pipeline.

## Milestone sequencing

1. **Package + registry + pinning layer**, wrapping today's exact behavior as
   `general@v2026.07.1`. Defaults leave the codebase; versioning and provenance exist from day
   one.
2. **Prompts into packages** — the seven hardcoded prompts become package content
   (templates with declared variables). Format specified in
   [package-format-v2.md](package-format-v2.md) (specVersion 2, Scriban).
3. **Section prose steps as package-defined ordered lists** (N steps, string→string).
   Format specified in [package-format-v3.md](package-format-v3.md) (specVersion 3,
   declarative bindings; compiled fallbacks retire).
4. **DAG in the package format** — including per-step output contracts (schemas), which
   is what finally severs harness dependence. Only build this when a real specialty
   demands a different shape; by then the registry/versioning/provenance rails exist.

> **Milestone 2 implemented 2026-07-12** (#4, PRs #21–#23/#25): specVersion 2 — the
> seven prompts became package content: Scriban templates, file-per-prompt with
> manifest-declared variables and a shared SNOMED-guidance prelude; rendered via
> `IWorkflowPromptProvider`, validated identically at publish and load time
> (`WorkflowPackageValidator`). First package `general@v2026.07.3`; production-verified
> with byte-checked prompt provenance (job `b4d31df0…`, 31/31 sections).

> **Milestone 3 implemented 2026-07-13** (#5, PRs #32–#36): specVersion 3 — the
> per-section prose pipeline became a package-declared ordered step list
> (`sectionSteps`: prompt + label + variable bindings against the engine's closed
> binding vocabulary), executed by one generic `run-prose-step` activity. A package is
> now mandatory: the job start resolves the pin server-side to a concrete immutable
> version and snapshots the step list into the job; the compiled fallback prompts, the
> legacy `ConsultGeneration`/`AgentProxy` endpoints, and the fixed step vocabulary were
> deleted. v2 packages remain valid via a normative synthesized step list. First
> package `general@v2026.07.4`; production-verified 9/9 with 27/27 prompts byte-matched
> against the package source (job `9a558ff3…`).

> **Milestone 4 implemented 2026-07-14** (#6, PRs #45/#47/#49–#52): specVersion 4 —
> the analysis DAG became package data: `nodes` with implicit edges via `node:`
> binding references, schema file refs welded to the attested structured-output agent
> (`concept-extraction`, published from `agents/*.yaml`), per-node `failIfEmpty`
> policy, and one terminal map node subsuming the section fan-out. The orchestrator is
> a topological wave interpreter; the bullet parser is gone (structured outputs);
> per-node input/output hashes extend the provenance record to a step-level
> verification chain. v2/v3 packages stay valid via synthesis. First package
> `general@v2026.07.5`; production-verified across three runs of the baseline input
> with byte-identical first-node prompts across format generations (jobs `2b06412d…`,
> `8725c440…`, `913dda03…`).

> **Milestone 5 engine implemented 2026-07-15** (#59 via #64; PRs #74–#76 and the
> catalog #66/#67): specVersion 5 — fork lineage (`derivedFrom`, root always derived
> by walking), a `data` table with self-describing collections (`index.json` +
> per-item files) replacing `standards.md`, ONE node kind with `forEach`
> (item-aligned `node:` edges replaced `previous_step_output`; the map container,
> `run-prose-step`, and the v4→v3 lowering are deleted), a `result:` deliverable
> contract, per-(node, item) scheduling and provenance hashes, server-resolved
> sections, and the draft-only effective-input hash (`effectiveInputHashVersion` 2).
> The account standards override retired (customization returns as package forking
> with the in-app editor, #57). Agent selection is catalog-keyed
> (`agents/output-contracts.json`, #55). First v5 package: `general@v2026.07.6`
> (#72). **v5-only rebase (#77, 2026-07-15)**: specVersions 1–4 retired from the
> engine entirely — synthesis, the map container, `standards.md`, the request's
> section payload, the v1 input hash, and the legacy entity/response fields are
> deleted (entity SchemaVersion 5); pre-v5 registry versions remain as archives.

> **Milestone 5 completed 2026-07-16** (#57, PRs #84–#86): the in-app editor —
> the Templates page edits the pinned package's prompt and standards texts and
> publishes an immutable per-account fork (`acct-<12 hex>`; server-stamped
> name, next CalVer, and `derivedFrom`; manifest-last conditional create), then
> flips the pin to the concrete ref. The `acct-*` owner-only access rule ships
> in the same PR as the API. Production-verified: fork
> `acct-1234567890ab@v2026.07.1` (derived from `general@v2026.07.6`, 2 of 21
> files differing by byte-diff), run `e550fe66…` expressing both edits, first-node
> InputHash byte-identical through its eighth generation. Design + records:
> [in-app-editing.md](in-app-editing.md).

UI consequences: the Templates page grows from "edit section standards" toward a package
view — post-v5, the fork editor (#57), the override path having retired; progress
displays become generic step lists rather than
hardcoded stage names (done for the prose steps in milestone 3 — the client renders
package-declared labels from the `section-prose-step` event; the analysis stages stay
hardcoded until milestone 4).
