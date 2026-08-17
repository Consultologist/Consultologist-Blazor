# Output-Contract Catalog and the Node Calculus

Recorded 2026-07-15 during post-milestone-4 exploration; tracked by the
"output-contract catalog" issue (#55, Milestone 5). Status: **the catalog is
implemented** (PR "the catalog" on the m5-output-contract-catalog branch):
`agents/output-contracts.json` + `OutputContractCatalog`, catalog-keyed selection in
`run-prompt-node`/`run-prose-step`, per-entry attestation with the schema
cross-check, the catalog-match validator closure, and the `agentVersions` provenance
map all shipped as designed below. The DAG visualization (PR 3) follows; the
unification (old PR 2) remains subsumed by #59. Context: the app is pre-release;
the stated posture is "churn is acceptable, build a solid app before release",
which lifted the gate-on-second-consumer rule for this slice.

## The two questions this design answers

**Can different JSON schemas be used with the same agent?** No — a Foundry agent
version's `text.format` is part of its published definition and request-level
overrides are rejected (spike #39). One agent version = one output format, always.
Union/envelope schemas weaken enforcement to "the model picks a shape"; abusing the
version sequence as a schema catalog wrecks versioning and attestation semantics.
Therefore: **N output shapes ⇒ N attested agents.** Cheap in practice — an agent is
~40 lines of YAML, one `az rest`, attestation for free.

**Should the output-type → agent mapping live in content?** It should leave *code*
(today: `useConceptAgent: bool` + two hardcoded env pairs — the last compiled-in
"twoness"), but its destination is **engine configuration, not package content**:

- The package declares the **contract** (`output.schema: concept-list`) — portable,
  deployment-agnostic, already the shipped shape.
- The engine declares the **executor** for each contract — deployment-specific,
  attested, trust-bearing. A package that named agents would leak deployment identity
  into portable content and hand executor selection to the least-trusted artifact;
  it also could not verify the claim (agent contents are invisible at
  package-validation time; only startup attestation knows them).

## The catalog

An engine-side, config-driven registry keyed by schema id:

| Catalog entry | Carries |
|---|---|
| `concept-list` | canonical schema text (`ConceptOutputContract.SchemaJson`), agent name pin, agent version pin |
| *(text — the absent-`output` default)* | prose agent name + version pins |
| *(future shapes)* | their schema text + their attested agent pins |

Configuration shape (decided 2026-07-15, see Decisions below): a bundled,
git-tracked catalog file deployed with the app and attested at startup, schema text
bundled like the agent manifests. Requirements that matter more than the spelling:

1. `run-prompt-node` resolves the agent **by the node's schema id** through the
   catalog — `useConceptAgent` dies; selection is name-keyed and open-ended.
2. The validator's v4.0 identity closure relaxes from "every schema equals *the*
   canonical schema" to "every schema canonically matches *a catalog entry*" — the
   closure becomes data-shaped. (The catalog must be an input to validation, which it
   already implicitly is via `ConceptOutputContract.SchemaJson`.)

   > **The closure has two directions, and both are now checked at publish
   > (2026-08-17).** Package→catalog: a package cannot publish declaring a schema
   > no contract matches (#185 runs the engine validator in the workflows repo's
   > CI). Catalog→packages: a catalog cannot publish if it would leave an
   > already-published package's schema matching nothing (#374 runs
   > `check-catalog-strands-packages.cs` in the agents repo's CI).
   >
   > The second direction exists because the match is re-evaluated at **every
   > load** while a published version is **immutable** — so a contract change can
   > strand a package that was valid when published, and the remedy would be a new
   > package version rather than a fix. The check covers public packages only;
   > `acct-*` forks live in the private account a public CI job cannot read.
3. **Attestation iterates the catalog**: every entry's agent is attested against its
   `agents/{name}.yaml`, including the embedded-schema comparison — an entry whose
   agent schema drifts from the catalog schema is a startup failure.
4. **Provenance records every agent a job used**, keyed by schema id (generalizing
   the current `AgentVersion` + `ConceptAgentVersion` pair to a map).
5. Adding an output shape becomes a **zero-code operation**: author the agent YAML
   (schema embedded), publish, add catalog config, and packages may declare nodes
   with the new schema id. Deferred tightenings (e.g. the SCTID `pattern`,
   dag-as-data-design.md) land by publishing agent v2 + updating the catalog entry —
   engine contract and agent move in lockstep by construction.

## The node calculus: the fewest node kinds is one

A node has exactly two orthogonal dimensions:

- **Executor** — what runs (render prompt → catalog-selected agent → output).
- **Multiplicity** — how many times, over what (once; or once per item of a
  collection).

Everything else that looks like a "type" is an **edge pattern**, not a kind:

- **Fan-out** = one node referenced by many bindings (the canonical DAG's
  `extract-patient-concepts` feeds two consumers).
- **Fan-in** = one node with many bindings; the prompt template is the combiner and
  the model does the combining (`create-patient-trajectory` binds three upstream
  nodes — the diamond does both patterns with zero special kinds).
- **Conditional branch** = excluded by doctrine (dag-improvements #3): a decision is
  a model step's *output* — data on ordinary edges — never format control flow.
  Expressiveness can be added later; it can never be removed.

Therefore the theoretical floor is **one kind with a multiplicity property**: `map`
stops being a container and becomes an annotation (e.g. `forEach: input:sections`) on
ordinary nodes; a mapped node's output is a collection; a mapped node binding another
mapped node over the same collection aligns item-wise (which is how
`previous_step_output` generalizes); the current three prose steps become three
per-item-chained nodes. The shipped two-kind form (`prompt` + `map`-with-steps) is
the *container spelling* of the same algebra — kept in v4.0 because it makes the
"aggregate output is not bindable" closure trivially enforceable and the per-item
pipeline visually obvious.

> **Superseded in part 2026-07-15** by
> [package-format-v5-design.md](package-format-v5-design.md): the one-kind/`forEach`
> spelling becomes the simpler design once `data:` collections make it statically
> validatable, and #53's unification is subsumed by one-kind execution. The catalog
> itself (schema-keyed agent selection) remains orthogonal and authoritative.

**Decision for this slice** (as originally recorded): keep the two-kind container spelling; do not add kinds.
Revisit the one-kind/`forEach` spelling only if a workflow needs per-item graphs more
complex than a linear step chain (the moment item-wise alignment and collection
fan-in semantics must be defined anyway). Future kinds grow along the **executor axis
only** — `human` (HITL pause, dag-improvements #4) and possibly a deterministic
`function` kind — never the control-flow axis.

## Edges: derived visualization, no authored edge file

Bindings *are* the edges plus strictly more (an edge list says "A depends on B"; a
binding says which variable receives B's output under which renderer). An authored
edge file would be a second source of truth requiring consistency rules that exist
only because the same fact is written twice; the only thing it could add — data-less
ordering constraints — nothing needs and is usually a modeling smell.

The legitimate underlying need (seeing the graph) is met by **deriving** it: a small
generator reading a manifest and emitting Mermaid/DOT — nodes with kinds and labels,
edges from `node:` references annotated with renderers, the map body as a subgraph.
Emit alongside the package at publish, and/or check a generated `dag.mmd` into the
package source with a CI freshness pin (the established pin pattern). Always true
because always computed.

## Slice plan

Folds in **#53** (prose-step unification) — same code region, one drain window
instead of two, and unification *through the catalog* is cleaner than unification
before it.

1. **PR 1 — the catalog** (no behavior change): catalog model + config binding;
   attestation iterates entries (schema-vs-agent comparison per entry); provenance
   generalizes the agent-version fields to a schema-id-keyed map (additive);
   validator closure relaxes to catalog-match; `run-prompt-node` selects through the
   catalog (with `concept-list` + text as the two entries, behavior is identical).
   `useConceptAgent` and the hardcoded pin pairs die.
2. **PR 2 — unification (#53)** (strict drain window): map steps execute as
   `run-prompt-node` per item — orchestrator resolves step variables (item fields +
   upstream concepts + previous output are all recorded data); per-step
   input/output hashes land in a per-section step map; `run-prose-step`,
   `ProseStepVariableBuilder`, and the v4→v3 lowering are deleted;
   `section-prose-step` SSE and `MarkSectionProseStep` rewire onto the unified
   results.
3. **PR 3 — DAG visualization generator** (independent, no drain): `scripts/` tool +
   generated `dag.mmd` per package + CI freshness pin.

Explicitly out of scope: new node kinds; package-side agent naming; conditional
control flow; multiple/non-terminal maps (each waits for a demanding workflow).

## Decisions (settled 2026-07-15; were "open questions to settle at implementation")

- **Catalog config spelling: a bundled catalog file** — git-tracked, deployed with
  the app, attested at startup exactly like `agents/*.yaml`. Deciding argument: the
  indexed-app-settings alternative's headline advantage is illusory (an app-setting
  change restarts the Function App just like a deploy, and a new entry needs a deploy
  for its schema file regardless), while half of every entry is file-shaped anyway —
  settings would split one logical entry across two systems with different audit
  stories. The file gets git history and PR review, cannot drift from the code that
  reads it, and matches the #16 GitOps trajectory. Accepted cost: pin rollback is a
  git revert + CI run instead of one CLI command.
- **The text/prose default becomes an explicit catalog entry** (no schema, prose
  agent pins). Attestation and provenance treat it like every other entry; the last
  hardcoded pin pair (`AzureAI__AgentName/Version`) dies; "node without `output`"
  simply resolves to this entry.
- **Per-item hash storage: store and expose everything** — entity `NodeOutputs`
  generalizes to per-(node, item) entries with Input/OutputHash, all returned on the
  job response like today's per-node hashes. Growth is bounded (nodes × sections).
  Lands under #59's per-item execution rather than this slice.
- **SSE payloads generalize to per-item `node-completed`** — settled by the v5
  one-kind decision (`section-prose-step` folds in; see
  [package-format-v5-design.md](package-format-v5-design.md)).
