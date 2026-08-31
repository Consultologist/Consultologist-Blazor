# Per-Consult Provenance — the design record

> **The normative contract moved out (#400, 2026-08-25).** What a job record
> carries, what each field means, and how every hash on it is computed now
> live in their own versioned registry,
> [consultologist-provenance](https://github.com/Consultologist/consultologist-provenance),
> published as `provenance@vYYYY.MM.N` beside the artifacts a record cites and
> vendored here as `external/consultologist-provenance`. The engine's own
> numbers are held against that document by `tests/ProvenanceVersionSetTests.cs`,
> which also recomputes the document's worked examples through
> `ConsultGenerationProvenance`; `GET /api/Public/Engine` attests the version
> the deployed build conforms to. What stays below is the design record — why
> the record is shaped as it is, what it guarantees and what it does not,
> and the legs it was designed to carry and does not yet.

Every consult generation records the versioned artifacts that produced it. The
chain the record was designed around — since #403 (2026-08-25) the record
carries `terminology` (the edition the server had loaded) and
`terminologyServerRef` (the server's build, a commit rather than the git tag
the chain first imagined), both read from `GET /api/Public/Terminology` on
the MCP function app (`Terminology__InfoUrl`) and stamped write-once at start:

```
model_weights_version - agent_version - SNOMED_version - mcp_version - DAG_workflow_version - input_hash
```

Of those, the record today carries the workflow package (`workflowPackage`, a
concrete ref), the agents (`catalogRef`, resolving contract → agent through an
immutable catalog version; per-node `outputContract` on the record's nodes),
and the input (`effectiveInputHash` with its definition number). The others are
below, under *Declared, not recorded*.

## Three kinds of field

The published contract sorts every field into one of three species, and the
sorting is the design decision that keeps the record honest over time:

- **refs** — the record stores a reference into a public, immutable registry,
  never a copy. Always concrete (`name@vYYYY.MM.N`), never `@latest`: a record
  pointing at a moving target would not be a record.
- **operational snapshots** — what the engine had in hand when the job ran
  (nodes, rosters, origins, skipped deliverables, title, tags), kept so the
  job replays and reads the same way forever. Durable replay determinism is
  the reason these are stored rather than re-derived. `outputsBlob` (#557)
  is one of these: where the produced text lives — container + name, never
  a URL — written at completion, kept after the text is deleted (part of
  what happened), with `textDroppedAtUtc` gating every read of it.
  `inputsBlob` and `inputsDroppedAtUtc` (#547) are its twins for the held
  inputs, written at start and gated the same way.
- **derived projections, stored at completion** — the deliverable hashes,
  computed from the text once when the job completes and kept (#368; records
  from before derive them on read, and serve the same values). Anyone holding
  the record recomputes them while the text is present; after the retention
  policy deletes it (`textDroppedAtUtc`) they attest what was produced and
  can no longer be checked against it. Since #551 (provenance
  `v2026.09.1`) the record also carries **token counts** — per node
  instance (`nodeOutputs[].tokens`, the provider's `{ input, output }`
  for that one Responses-API call) and the job's totals, summed once at
  completion. Counts are numbers, never text, and survive every
  retention drop; absent means *not recorded*, never zero — aggregates
  and fan roll-ups record none, and every record from before says
  nothing. The usage stores (#552) are written from these at completion
  and never re-derive from records.

`agentVersions` was the one copy the record ever stored; #105 retired it in
favour of `catalogRef` for exactly the reason above.

## Two ladders that are not one

`schemaVersion` is the record's own storage shape (2, 6 or 7, stamped by the
code path that produced the record); `packageSpecVersion` is the format the
package was written against. They collide at 7 by coincidence, which is why
#373 renamed what History shows and the contract states them apart.

## The decision, when the shape is known later

A v10 package with classifiers (design record § 5, #496) decides its fire set
at a boundary inside the run rather than at start. The record carries that
decision as three fields the registry names since `provenance@v2026.08.7`
(step (h), #499), beside effective-input hash definition 6 and a classifier's
per-node pair:
`decidedAtUtc` (the boundary time; stamped at start for every package without
classifiers, so older records read as decided at start), `classifications`
(classifier node → the value it answered, a declared value and so printable),
and `decisionFailureKind` (`could-not-decide` or `nothing-applied`) alongside
`startFailure` when the run ended in the deciding stage. The node hash is
unchanged: the decision is over recorded activity outputs, not a new input.

## Declared, not recorded

The chain names legs the record does not yet carry. They are design intent, and
each has, or needs, its own work:

| Leg | What it would record | Where it stands |
|---|---|---|
| `model_weights_version` | Exact checkpoint identity of the model (HF repo + revision hash), not the family name | Not recorded. The agent definition names the model; the checkpoint behind a hosted deployment is not exposed to the engine. |
| `model_parameters` | Sampling params and the reasoning toggle (below) | Not recorded on the job; they are the agent definition's, covered by `catalogRef` → agent version → the attested manifest. |
| `backend_fingerprint` | Serving-stack fingerprint per run, if the API exposes one | Not recorded; the API does not expose one. |
| `inference_stack` | Inference engine + version, for bit-exact re-run attempts | Not recorded. |

## Attestation: git as source of truth for mutable deployments

> **Implemented 2026-07-10** for the agent: `AgentAttestationService` (hosted service in
> the Api) loads the bundled `agents/{name}.yaml` at startup, fetches the deployed agent
> version from the Foundry API with the app's managed identity, and compares model,
> instructions, reasoning effort, tool choice, and tools. Drift logs as an error;
> `AgentAttestation__Enforce=true` fails the host instead. The MCP deployment
> attests its own build since #403 (`Public/Terminology`, stamped by its
> deploy workflow), and every record names it.

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
> Since #400 it attests the provenance registry version too, and since #398
> (2026-08-25) **every job record names both**: `packageFormatRef`
> (`package-format@v…`, the rules its package was read by) and `provenanceRef`
> (`provenance@v…`, the contract the record conforms to), stamped write-once
> at start from the build's own attestation — never a run-time lookup, never a
> pin the engine might not run. History resolves a record's numbers through
> its own refs, and through the attestation only for records from before.
>
> Since #514 (2026-08-28) the attestation also names the deployment's
> **canonical public host** (`Public__ApiHost`, e.g.
> `east.ca.api.consultologist.ai`) and every record carries it as `apiHost`
> — where the data was processed, the residency statement — beside
> `engineCommit`, the build that ran the job. The host is configuration, not
> the request's authority: both hostnames serve one app, and a record must
> say one name per region. A deployment that names none leaves the field
> null and History says *not recorded*; it never borrows the base URL the
> client happens to call.
>
> Since #512 (2026-08-28) a document's origin carries two digests beside
> the extractor's identity: `fileSha256` over the bytes as received — the
> one thing the bytes leave on the record, since they are never kept — and
> `textSha256` over the text as it entered the effective-input map. Equal
> file digests with unequal text digests say the extraction changed, not
> the document (compare `extractor`); unequal file digests say a different
> file. A holder of the original file can check the first against the
> record at any later date; neither is a name.
>
> Since #510 (2026-08-29, provenance `v2026.08.10`) an origin may be
> `previous-run`: the element was copied at start from the same account's
> completed run `sourceJobId`, deliverable `sourceResultId`, and
> `textSha256` is over the copy. The reference resolves through the
> account's own records, never a registry, and only while the source text
> is held; the form shows the copy and does not let it be edited.
> History reads it as *copied from deliverable … of run …*.
>
> Since #549 (provenance `v2026.08.12`) an origin may be `rerun`: the
> whole run replays a completed run's held inputs (#547), every
> effective slot carries one naming `sourceJobId`, and `textSha256` is
> over the slot's effective value verbatim — equal to the source's by
> construction, so the two records' `effectiveInputHash` agree or one of
> them is wrong. The rerun could only start while the source's held
> inputs existed; the origin stands after they are deleted. The rerun's
> History shows the per-stage table against the source.
>
> Since #582 (provenance `v2026.08.13`) a rerun's record also carries the
> judgment: `rerunOf` lifts the source id to the job level, and
> `rerunVerdict`/`rerunDivergence` are derived once at the rerun's
> completion — `pass` when every reproducible node's `outputHash` equals
> the source's for the same `inputHash` and per-node `hashVersion`;
> `fail` naming the first divergent stage in package order (or
> `effective-inputs`, the by-construction breach); and
> `no-reproducible-stages`, named rather than a vacuous pass. Stages
> failing the preconditions are shown, not counted.

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

## The effective-input hash — why it is versioned the way it is

The definitions themselves (1–5) and their worked examples are in the
published contract's `hash-definitions.md`. What this record keeps is the
rule and its history:

> **Versioned 2026-07-15 (Milestone 5)**: jobs record `effectiveInputHashVersion`.
> Definitions are added beside their predecessors, never replaced, and **never
> compared as equals** across versions. The ladder has moved with the format —
> 2 (specVersion 5/6: the draft only), 3 (v7: the supplied inputs as an
> ordinal-sorted `{id: text}` map), 4 (v8: typed scalars), 5 (v9: structured
> values, UTF-8 written as-is) — each time because the bytes that describe an
> input changed, and a hash that silently changed its bytes under one number
> would be worthless.
>
> **v5-only rebase (#77, 2026-07-15)**: definition 1 (draft + sections) is
> historical — the engine computes only 2 upward, and job records referencing
> pre-v5 packages are no longer re-runnable (pre-release test data; the
> reproducibility promise starts at specVersion 5). Explicit revocation under
> the pre-release-churn doctrine.
>
> The per-node chain extends to **per-(node, item)** entries (`nodeId:itemId`
> keys) — every forEach instance records its own input/output hashes. Since
> #375 (2026-08-25) each pair carries `hashVersion`, one number defining both
> hashes: the ladder has five dated definitions, one per change to the
> rendered-prompt bytes (`ConsultGenerationProvenance.NodeHashVersion` is the
> newest, held equal to the published index by test), and records from before
> carry none — the contract says how to place them by date.

Since #402 a holder can check a record: History's *Verify* recomputes the
deliverable hashes in the browser and `scripts/verify-job-hashes.cs` runs the
engine's own functions over a record and the holder's inputs
(registry-operations.md, *Verifying a record*); History's definition numbers
link to the published document at the version `Public/Engine` attests.

**Prompts are not part of the input hash.** Since specVersion 2
([package-format-v2.md](package-format-v2.md)) the prompts are package content, covered
by the `workflowPackage` provenance field. Two jobs with the same hash but different
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
  never deleted while the account exists — provenance refs stay resolvable for as
  long as the account does. (Amended 2026-08-30, #559: account closure removes an
  account's forks together with its records, so no ref outlives its package.)
- **Where variance enters is visible**: per-(node, item) Input/OutputHash chains
  localize divergence between runs. In practice (2026-07-16): two runs of the same
  package on the same draft diverged at exactly one point — the first node's
  OutputHash — with the difference propagating precisely along DAG edges. The
  first node's InputHash is the deterministic cross-run anchor (byte-identical
  through eight generations of engine and format as of the editor's verification
  on 2026-07-16 — under per-node definition 1; the ladder since names each
  change to those bytes);
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
This landed with the output-contract catalog (#93) and the per-node output contracts (package-format-v4.md): the inter-step contracts are package content now, and the published provenance contract says how a record names them.

Once that is done, the published artifact set — open weights (DeepSeek), workflow
package, SNOMED edition, MCP server code (Apache 2.0) — is sufficient to re-run the
system with no dependency on this application or its Azure tenant.
