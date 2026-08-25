# Per-Consult Provenance Record

> **Status (2026-07-09)**: milestone 1 records the first three fields on every job —
> `WorkflowPackage`, `EffectiveInputHash` (SHA-256 over canonical draft+sections JSON,
> computed server-side at job start), and `AgentVersion` — snapshotted into the
> orchestration input and exposed on the job response (`SchemaVersion` 2). The remaining
> fields land with later milestones.

Every consult generation records the versioned artifacts that produced it. Conceptually:

```
model_weights_version - agent_version - SNOMED_version - mcp_version - DAG_workflow_version - input_hash
```

Store it as a **structured JSON object on the job** (version labels will eventually
contain hyphens; derive a string key from the object when needed).

## Fields and their sources

| Field | Content | Source of truth |
|---|---|---|
| `model_weights_version` | Exact checkpoint identity of the model (DeepSeek V4 Pro at a pinned revision, e.g. HF repo + revision hash), not just the family name | Published weights registry |
| `model_parameters` | Sampling params and the reasoning toggle (see below) | Git-tracked agent config / per-step package params |
| `backend_fingerprint` | Serving-stack fingerprint per run, if the API exposes one | Response metadata |
| `agent_version` | **Historical** (records ≤ 2026-07-17): contract → Foundry agent version map (`agentVersions`). Purified by #105 — later records store `catalogRef` only, and the catalog version document holds contract → {agentName, agentVersion}. The record stores refs, not copies: refs (`workflowPackage`, `catalogRef`) / operational snapshots (nodes, items — Durable replay determinism) / derived projections (`workflowOutputHash`) are the record's three living species | The job's `catalogRef` → catalog registry |
| `snomed_version` | Terminology edition + version + import date | MCP `get_terminology_info` (returns exactly this) |
| `mcp_version` | Release (git tag) of the Apache-2.0 `snomed-snowstorm-mcp` repo | Git tag; the deployed app attests its own build at `GET /api/Public/Engine` (#449) — the MCP release is still not recorded (#403) |
| `workflow_package` | `name@version` of the pinned workflow package (CalVer `vYYYY.MM.N`, e.g. `general@v2026.07.1`) | Package registry |
| `packageTitle` | Since #432: the pinned manifest's title as it was when the job ran — a display convenience shown beside the ref, never a substitute for it. Absent on an untitled package and on every job before it | Stamped at job start from the resolved manifest |
| `startFailure` | Since #434: set only on a job created already **Failed** because no deliverable applied to its inputs (v8 § 5 *The empty case*; v9 § 5 *The empty fan*) — the refusal sentence, composed of authored package content only. Nothing ran: `runtimeFailureError` is null, there are no blocks, `totalBlockCount` is a stated zero, and `skippedDocuments` lists every deliverable with what it wanted. Absent on every job that started | Written at job start, in the same entity write that creates the record |
| `packageTags` | Since #453: the pinned manifest's `tags` as they were when the job ran — authored labels, the safety class of the title, shown on the record for finding it. Absent before v9 and on every job before it; an empty list for a v9 package that declared none | Stamped at job start from the resolved manifest |
| `catalogRef` | `output-contracts@vYYYY.MM.N` — the concrete catalog version the job ran under, resolving every `agentVersions` entry to its contract→agent mapping (#93). Distinct from the catalog the *package* was **published** under, which its `publish.json` records (#433): the two may differ, and comparing them is how a contract redefined between publish and run is detected after the fact — the record #377 said by-ref could not produce | Public catalog registry (immutable versions); attested equal to git at startup |
| `input_hash` | Hash of the **effective** input | Computed at job start |
| `workflowOutputHash` (v1) | The deliverable hash of a **completed v5** job: SHA-256 of the canonical JSON `{sectionId: sha256(sectionText)}`, ordinal-sorted keys — a Merkle-style root over `generatedSections`. **Derived at response time, never stored**: anyone holding the record recomputes it, and two runs produced the byte-identical note iff their hashes match (#88) | Derived from the record itself |
| `workflowOutputHash` (v2) | The deliverable hash of a **completed v6** job: SHA-256 of the assembled document's UTF-8 bytes — the deliverable is one document, so its digest is the whole story (#116; package-format-v6.md). Aggregator node outputs additionally record like any node's: input hash = canonical JSON array of source output hashes in aggregation order, output hash = the rendered digest; the rendering bytes are normative, so both reproduce from the record | Derived from the record itself |
| `inference_stack` (optional) | Inference engine + version, for bit-exact re-run attempts | Deployment config |

## Attestation: git as source of truth for mutable deployments

> **Implemented 2026-07-10** for the agent: `AgentAttestationService` (hosted service in
> the Api) loads the bundled `agents/{name}.yaml` at startup, fetches the deployed agent
> version from the Foundry API with the app's managed identity, and compares model,
> instructions, reasoning effort, tool choice, and tools. Drift logs as an error;
> `AgentAttestation__Enforce=true` fails the host instead. The MCP-deployment
> attestation remains future work.

> **Registry legs added 2026-07-16 (Milestone 6)**: attestation now welds **three**
> stores per startup — git ↔ Foundry (above), git ↔ the public catalog registry
> (runtime `output-contracts@pin` must equal the bundled file, #93), and git ↔ the
> public agent-definition registry (the published artifact must equal
> `redact(bundled manifest)` — the redaction strips only `tools[].server_url` and
> `project_connection_id`, #94). A job record's `agentVersions` + `catalogRef`
> therefore resolve through publicly readable, immutable registry versions that are
> attested equal to git at every startup.

> **The engine attests its build (#449, 2026-08-24)**: `GET /api/Public/Engine`
> (anonymous, open-CORS, deployment facts only) returns the commit the Functions
> build was stamped with (`-p:SourceRevisionId` in the deploy workflow; `null` on
> an unstamped local build), the catalog `ResolvedRef`, the vendored
> package-format registry version, `AcceptedSpecVersions`,
> `SupportedSpecVersions` and the Scriban version. The content repos' CI checks
> the app out at that commit for engine validation — "passed engine validation"
> means the deployed engine, with `main` as the stated fallback and a visible
> notice when the endpoint cannot say. The property set is pinned by test.
> Since #384 the app also reports every account's pin against the loaded
> catalog at startup and at `GET /api/Operator/PinHealth` (registry-operations.md).

The Foundry agent (system prompt, parameters, tool wiring) is edited in a portal and is
therefore mutable state. Rule: **track the agent config in git; at startup or job start,
fetch the deployed agent version and verify it matches the tracked manifest** — fail or
warn loudly on drift. This makes `agent_version` and the git content redundant by
construction, which is the point: the check keeps them honest. The same pattern applies
to the MCP deployment.

Planned evolution ([content-repos.md](content-repos.md)): the attested manifest moves
from the app package to a registry written **only by CI** from a dedicated agents repo
(GitHub→Azure OIDC; humans read-only). The CI-only-write restriction is the
precondition — a registry humans can write would make the attestation tautological.

## The effective-input hash

> **Versioned 2026-07-15 (Milestone 5)**: jobs record `effectiveInputHashVersion`.
> Definitions are added beside their predecessors, never replaced, and **never
> compared as equals** across versions:
>
> - **1** (null/absent, pre-v5 jobs) — draft + sections, as described below;
>   historical.
> - **2** (specVersion 5/6) — **the draft only**, because sections are package
>   data covered by the `workflowPackage` ref and the account standards override
>   is retired.
> - **3** (specVersion 7) — the supplied inputs as an ordinal-sorted `{id: text}`
>   map, absent optionals omitted (package-format-v7-design.md § 6).
> - **4** (specVersion 8) — the same map as **typed scalars**: a boolean hashes
>   as `true`, not `"true"` (package-format-v8-design.md § 6).
> - **5** (specVersion 9) — the same map as **structured values**: field ids
>   ordinal-sorted at every level, array elements in the caller's order, a
>   number as the digits sent, and UTF-8 written as-is where 2–4 escape it
>   (package-format-v9-design.md § 8). Definition 4 refuses structure, so a
>   misrouted gate fails rather than stamping 4.
>
> The per-node chain below extends to **per-(node, item)** entries
> (`nodeId:itemId` keys) — every forEach instance records its own input/output
> hashes.
>
> **v5-only rebase (#77, 2026-07-15)**: the v1 definition is historical — the
> engine computes only v2, and job records referencing pre-v5 packages are no
> longer re-runnable (pre-release test data; the reproducibility promise starts at
> specVersion 5). Explicit revocation under the pre-release-churn doctrine.

The input is not just the consult draft. The request reaching the orchestrator is
draft + section list + standards, and account-level overrides change the standards.
Hash the **fully resolved content** (draft + resolved sections/standards after
overrides). Cleanest rule: overrides produce an ephemeral package-content hash recorded
alongside the pinned package version — otherwise two different runs can claim the same
input. *(v1 semantics — see the versioning note above.)*

**Prompts are not part of the input hash.** Since specVersion 2
([package-format-v2.md](package-format-v2.md)) the prompts are package content, covered
by the `workflowPackage` provenance field; the effective-input hash continues to cover
only draft + resolved sections/standards. Two jobs with the same hash but different
package versions ran the same clinical input through different prompts — which is
exactly what the two fields, read together, are designed to express.

## Model parameters: reasoning toggle

DeepSeek V4 Pro supports disabling reasoning via parameters. Implications:

- The toggle materially changes output distribution → it is provenance-critical config
  and belongs in the git-tracked agent parameters (under attestation), never an implicit
  deployment default.
- Reasoning **off** gives cleaner hashing (recorded output = full model output, no
  trace-exclusion rule), tighter format compliance for the strict concept-parser
  contract, lower latency and cost.
- Consider a **per-step parameter in the workflow package** rather than one global
  setting: the four analysis stages are where reasoning could improve clinical quality;
  the three prose steps are format-sensitive transformations where reasoning-off is
  likely strictly better. Per-step params then land in provenance via the package
  version, keeping the record complete without extra fields.

## What the record guarantees — and what it does not

- **Auditability everywhere**: the record fully identifies what produced the output.
  Since the in-app editor (#57, 2026-07-16), the `workflowPackage` ref may name a
  per-account fork (`acct-…@vN`); the fork's manifest carries a server-stamped
  `derivedFrom` chain that walks back to the repo root, and account versions are
  never deleted — provenance refs stay resolvable forever.
- **Where variance enters is visible**: per-(node, item) Input/OutputHash chains
  localize divergence between runs. In practice (2026-07-16): two runs of the same
  package on the same draft diverged at exactly one point — the first node's
  OutputHash — with the difference propagating precisely along DAG edges. The
  first node's InputHash is the deterministic cross-run anchor (byte-identical
  through eight generations of engine and format as of the editor's verification);
  downstream InputHashes embed upstream model outputs and are only stable when
  those converge.
- **Statistical reproducibility across harnesses**: same record ⇒ same configuration ⇒
  statistically similar outputs. Sampling is stochastic; identical records do not imply
  identical outputs.
- **Bit-exact reproduction** additionally requires: open weights (satisfied — DeepSeek),
  pinned seed, temperature 0 (or equivalent), and a matching inference stack. Hosted
  Foundry inference and self-hosted inference of the same weights can differ
  (quantization, engine, batching). The AI-Copyright-Reproducibility harness's
  content-hashing methodology is the verification tool: run N times per stack and
  compare hash distributions.

## Harness independence: the one structural prerequisite

The claim "the web app version is unnecessary — any harness can run the system from
published artifacts" holds **only after the inter-step contracts move out of the app**.
Today `ConsultGenerationConceptParser` (which output lines parse vs. drop) and the
concept formatter (how concept lists serialize into downstream prompts) are workflow
semantics living in C#. Two harnesses running the same DAG + prompts but parsing or
formatting differently produce different outputs. Until the workflow package format
specifies these contracts (per-step output schemas, or a published versioned "workflow
spec" the DAG version implies), the app version is silently part of the provenance.
This lands in milestone 4 of workflow-packages.md.

Once that is done, the published artifact set — open weights (DeepSeek), workflow
package, SNOMED edition, MCP server code (Apache 2.0) — is sufficient to re-run the
system with no dependency on this application or its Azure tenant.
