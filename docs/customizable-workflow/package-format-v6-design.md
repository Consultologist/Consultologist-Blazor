# specVersion 6 Design: Multi-Collection Fan, Aggregator Nodes, One Assembled Document

Recorded 2026-07-20 at the opening of Milestone 9 (#152; design issue #153,
implementation #116). Status: **implemented 2026-07-21** (#116: format layer
PR #161, engine PR #162, delivery PR 3; normative spec:
[package-format-v6.md](https://github.com/Consultologist/consultologist-package-format/blob/main/package-format-v6.md)). Working alias during
Milestone 9 planning was "5.1"; the manifest field is an integer, so the
step ships as `specVersion: 6`.

v6 is three moves designed together:

1. **Multiple collections** — the v5.0 single-collection closure relaxes;
   chains over different collections are independent for-loops.
2. **Aggregator nodes** — a deterministic node property (`aggregate:`)
   collects other nodes' outputs; the **only** way outputs cross from a
   forEach chain to the rest of the graph.
3. **One assembled document** — every v6 package's `result` references an
   aggregator; the deliverable is a single server-assembled document, not
   client-assembled sections.

Design-round decisions recorded on #153/PR #155 (three rounds, 2026-07-20):
integer bump to 6, engine accepts {5, 6}; general-purpose scope; aggregates
fail loud; labeled-blocks rendering; reachability required; aggregation only
via aggregator nodes (an earlier draft's implicit aggregate *bindings* into
prompt nodes is **superseded** by round 3); ordered multi-source
aggregators; result-on-aggregator required in v6. Declared inputs and
multiple deliverables stay future (§ 11).

## 1. Motivation

v5.0 closes three things at once (package-format-v5.md § closures):
aggregate edges, cross-collection edges, and — because of those two —
multiple collections themselves. The validator's own comment states the
dependency: with both edge families closed, a second collection's forEach
chain would be *disconnected* — publishable but unreachable. Deleting the
single-collection check alone would produce packages that validate but
cannot matter. So relaxing it requires a legal way for a chain's outputs to
be consumed: the aggregator node.

Independently, the v5 deliverable is client-assembled: the engine emits
per-section outputs and every consumer (Consults note view, copy button,
History) pieces the document together. v6 makes assembly part of the
workflow itself — deterministic, server-side, recorded — so the consult
leaves the engine as one document with one hash.

The scope stays deliberately general (round-1 decision): per-item chains,
ordered aggregation, one document. Concrete verticals — reference corpora
summarized into drafting context, structured patient lists reconciled into
a summary variable — are expressible, but none of them shapes the format.

To state the loop shape plainly: chains over different collections are
**independent for-loops** — they fan over unrelated item sets, run in
parallel under the readiness scheduler, and one chain's per-item failures
never touch another chain — but every chain must connect downstream to the
result (§ 7 reachability): independent in execution, never orphaned from
the deliverable.

## 2. Vocabulary

One addition, in the v5 "one kind with properties" spirit (`forEach` makes
a node multiple; `aggregate` makes it deterministic):

```jsonc
{
  "id": "assemble-note",
  "label": "Assemble note",
  "aggregate": ["node:section-instructions", "node:closing-remarks"]
}
```

An **aggregator node** declares `aggregate`: a non-empty **ordered list**
of `node:` refs. It declares **no** `prompt`, `bindings`, `output`, or
`forEach` — the property is the behavior. All other nodes ("prompt nodes")
are unchanged v5 nodes.

Edge semantics:

| Edge | v5.0 | v6 |
|---|---|---|
| forEach node → same-collection forEach node (binding) | item-aligned | unchanged |
| forEach node → different-collection forEach node | rejected | still rejected |
| forEach node → scalar **prompt** node | rejected ("aggregate closed") | **still rejected** — aggregation is never implicit in a binding |
| forEach node → **aggregator** (listed as a source) | n/a | **the aggregate edge**: the chain's collected instances |
| scalar prompt node → aggregator (listed as a source) | n/a | the node's single output, verbatim |
| aggregator → anything (bound as `node:<id>`) | n/a | broadcasts like a scalar — into prompt nodes, forEach instances (the reference-corpus pattern), or other aggregators (composition) |
| `as` renderer on any aggregate-related edge | n/a | rejected (concept renderers stay per-item; future) |

`result` must reference an **aggregator** node in v6 (§ 4).

## 3. Aggregator semantics

### Rendering (normative)

An aggregator renders its sources **in declared order**, joined by blank
lines:

- A **forEach source** renders as labeled blocks in the collection's
  `index.json` order: for each item, a line `## ` + the item's `name`, a
  blank line, the instance's output text, a blank line before whatever
  follows.
- A **scalar source** (prompt node or aggregator) renders as its output
  text, verbatim — no heading. A heading over scalar content is authorable
  content, not format behavior.

No prologue, no epilogue, no trailing newline beyond the final text's own.
The format is **normative, not advisory**: aggregator output feeds hashes
and downstream prompt inputs, so the bytes are pinned by the spec exactly
as Scriban rendering is. (Round-1 decision: labeled blocks over plain
concatenation and JSON — readable by the model and by a human inspecting a
job's inputs; item names give the model stable referents. A JSON aggregate
form can arrive later behind a renderer.)

### Execution

Deterministic, engine-side, **no agent call** — the operation has one
correct answer, so an agent would add cost, latency, and alteration risk
for nothing. The orchestrator composes the recorded outputs inline: no
activity, no retry semantics. The output is a **first-class node output**
— hashed, recorded like any node's, with its own row in History's grid.

Note what this makes expressible *without* any further format feature:
**agent-mediated assembly is an authoring choice.** A package may route
`sections-aggregator → harmonizing prompt node → single-source aggregator
→ result`; the harmonizing agent (which can alter section content — a
provenance trade-off) is then visible in the graph, chosen by the author,
and recorded per-node, rather than being a format special case.

### Failure (normative)

**Aggregators fail loud** (round-1 decision). If any contributing instance
of any source failed or was skipped, the aggregator fails with an error
naming the failed item id(s); it never composes a partial document.
Failure cascades downstream through the existing skip propagation.
Per-item independence *within* a chain is unchanged; what a failure can no
longer do is silently thin out a document someone consumed. Rationale: a
consult built on a silently-reduced aggregate is a provenance hazard — the
record would look complete while resting on missing context; fail-loud is
the clinically conservative default.

## 4. The deliverable: one assembled document

In v6 the `result` references an aggregator, and **its output is the
deliverable**: the engine completes the job with one assembled document.
Clients render, copy, and export that document instead of piecing sections
together.

**Blocks replace sections as the deliverable units.** The result
aggregator's *expansion*, in source order, defines the units: one block
per (forEach source, item) pair — index order within each source — and one
block per scalar source. Setup listing ("what this consult will contain"),
streaming preview (blocks fill in as instances complete), and progress
counts enumerate exactly these units; the M8 run rail's per-chain groups
already present them. Unit keys are composite (`nodeId:itemId`) since item
ids are unique per collection, not globally.

**Hashes.** `workflowOutputHash` gains **definition v2** for v6 jobs:
`Sha256Hex` of the assembled document's UTF-8 bytes. (v1 — the
ordinal-sorted per-section hash map — remains the definition for v5 jobs;
records carry the definition version as today.) Per-block provenance is
unchanged: every instance output keeps its own recorded hash.

**v5 coexistence.** specVersion 5 packages keep the per-section
deliverable and v1 hash; the engine supports both modes. `general` stays 5
until republished as 6.

## 5. The fan: per-collection item sets

The orchestration input's single `Items` snapshot (the result collection)
becomes **one item set per fanned collection**, all snapshotted at job
start (the operational-snapshot species — Durable replay determinism,
unchanged taxonomy). Shape note for #116: the v5 single-set shape must
keep deserializing (records and in-flight jobs). Instance keying is
already collection-agnostic: `nodeId:itemId` works unchanged because a
node belongs to exactly one collection.

## 6. Readiness

- **forEach instance** (unchanged): every same-collection `node:`
  dependency item-aligned complete, every scalar dependency (prompt node
  or aggregator, broadcast) complete.
- **Prompt scalar node** (unchanged): every dependency settled.
- **Aggregator**: every source settled — for a forEach source, all
  instances of that node completed; any failed/skipped instance makes the
  aggregator fail per § 3 rather than run.

Acyclicity over the full graph (binding edges + aggregate source edges) is
required and checked with the same Kahn pass.

## 7. The v6 closure set

Kept from v5.0:

1. Schema catalog match — every declared schema canonically matches a
   catalog output contract.
2. Cross-collection item edges closed — no forEach → forEach edges across
   collections.
3. One deliverable — exactly one `result`; **in v6 it references an
   aggregator node** (changed from "a forEach node").
4. No conditionals, loops, or expression language.
5. Orphan prompts are errors; a prompt is referenced by exactly one node.
6. Acyclicity, binding/variable set-equality, per-item renderer rules,
   templating gates — unchanged.

New in v6:

7. **Reachability** — every node must transitively feed the `result`
   through binding or aggregate edges. A node or chain whose outputs
   cannot reach the deliverable is a publish error — the orphan-prompt
   philosophy applied to execution: no agent spend for outputs the
   document never sees. (Vacuous under v5.0's single collection; bites
   once the relaxations below exist. Round-2 decision.)
8. **Aggregator well-formedness** — `aggregate` is a non-empty ordered
   list of existing `node:` refs; aggregator nodes declare no
   prompt-family fields; the result aggregator must **transitively include
   at least one forEach source** (a package with no fan has no consult).
9. **Aggregation is explicit** — forEach nodes are bindable only
   item-aligned (same collection) or via aggregators; never directly by a
   scalar prompt node.

Relaxed:

10. ~~All forEach nodes share one collection~~ → forEach nodes may
    iterate any declared collection.
11. ~~Aggregate closed~~ → aggregator nodes per §§ 2–3.

Still out of scope in v6 (future steps, not promises):

- Declared inputs beyond `input:consult_draft` (sketched in § 11).
- Multiple `result`s / multiple deliverables (sketched in § 11).
- Renderers on aggregate edges, including a JSON aggregate form.
- Cross-collection item-aligned semantics (zip/cartesian) — no known need.

## 8. Progress and provenance

- **The run rail / setup listing** enumerate the deliverable's blocks
  (§ 4) and each chain's nodes — the M8 rail already renders per-chain
  chevron groups, and secondary chains present as node-level rows with
  per-item groups exactly like the main chain.
- **History's grid** gains aggregator rows like any node row: input hashes
  (the source outputs), output hash (the composed document or intermediate
  aggregate). NodeOutputs' `nodeId:itemId` keying generalizes untouched.
- **Hashes**: aggregator outputs hash like any node output; the
  deliverable hash is § 4's workflowOutputHash v2. No new record species —
  refs / operational snapshots / derived projections cover everything
  (provenance.md taxonomy unchanged).

## 9. Versioning mechanics

- A manifest declares the rule set it was validated under: `specVersion:
  5` validates under the frozen v5.0 closures, `6` under § 7. A package
  needs 6 only when it uses what 6 opens.
- **The engine accepts exactly {5, 6}**; the store's acceptance and the
  package selector's integer spec gating (#137) extend naturally.
- The publisher stamps the declared version it validated; it never
  upgrades a manifest's declaration.
- package-format-v5.md remains the frozen v5 normative spec (header note
  added); the v6 normative spec is written when #116 implements.

## 10. Editor implications (forward pointers)

- **#154 "+ data folder"** becomes meaningful: a new collection is
  publishable *and consumable* once a chain iterates it and an aggregator
  bridges it. The #115 membership machinery generalizes; the editor's
  compose step drops its single-collection assumption.
- **#117 node editing** speaks this vocabulary: forEach assignment offers
  any declared collection; creating an **aggregator node** (choose and
  order sources) joins node creation; BindingSourceEditor offers
  aggregators as scalar sources; client-side pre-checks surface
  unreachable nodes (§ 7.7) the way unbound variables are surfaced — a
  named, per-node publish block, with the server validator the authority.
- The DAG diagram renders aggregators as their own boxes — the assembly
  is graph-visible by construction, no synthetic presentation needed.

## 11. Future steps beyond v6 (sketched, not promised)

Recorded from the design review so the roadmap is legible; nothing above
depends on this section, and no shape here is committed.

### Declared inputs — the natural v7

The engine has exactly one input today (`input:consult_draft`; effective-
input-hash v2 covers the draft alone) — but the binding vocabulary is
already namespaced (`input:<id>`), so the format was built with room. The
step: the manifest declares `inputs: [{id, label, required}]`; `input:<id>`
bindings validate against the declaration; the Consults setup phase renders
one field per declared input — **the package defines its own intake form**,
composing with the fork model; the request carries all inputs and the
effective-input-hash bumps to v3 (id-keyed canonical map). Contained blast
radius: format + validator, one endpoint contract, the setup UI, one hash
version.

### Multiple deliverables — further out, and a product decision first

`result` becoming a list is a product change wearing a format change's
clothes: a job would produce a set of documents (note + patient letter +
structured summary…). Everything assuming one deliverable moves — the
blocks model per deliverable, the result view, History, the output-hash
form, copy. It redefines what a job is, which is a decision to make
deliberately, not a format step to slip in. One observation committed:
reachability (§ 7.7) would generalize to "reaches *a* result", and the v6
aggregator machinery is its natural building block (one aggregator per
deliverable).
