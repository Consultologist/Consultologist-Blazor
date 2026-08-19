# specVersion 5 Design: Fork Model, Data Collections, One-Kind Nodes (Not Implemented)

Recorded 2026-07-15 at the close of the post-milestone-4 exploration. Status:
**implemented 2026-07-15** (#59: format layer PR #74, one-kind interpreter PR #75,
input model PR #76, general@v2026.07.6 publish #72; normative spec:
[package-format-v5.md](https://github.com/Consultologist/consultologist-package-format/blob/main/package-format-v5.md)). Four exploration threads converged into one format generation;
designed together they carry none of the compromises each would carry alone:

1. forks kill the account override that forced standards into the input hash;
2. `data/` collections make per-item bindings statically validatable;
3. statically-known collections make `forEach` nodes checkable, killing the map
   container;
4. one-kind execution completes the per-item provenance chain by construction.

Supersedes in part: the standards sections of
[in-app-editing.md](in-app-editing.md) (the override layer retires; the editor's
publish contract becomes v5-shaped) and the two-kind decision in
[output-contract-catalog.md](output-contract-catalog.md) (whose catalog itself stays
orthogonal and valid). Subsumes issue #53 — the prose-step unification happens
through one-kind execution rather than as separate surgery.

## 1. Fork-everything customization + `derivedFrom` lineage

All customization — standards included — is authoring a **new immutable package
version** (the in-app-editing model, extended to everything). The account standards
override (`consult.sectionStandardsMarkdown`) and its client-side markdown
parse/merge retire entirely: one editing model, one Publish button, one provenance
regime.

**The manifest gains `derivedFrom`** — a single **concrete** ref (never `@latest`)
recording the *fork origin*. (Renamed from `parent` 2026-07-15: FHIR's `derivedFrom`
spelling, familiar in the clinical domain, and immune to the misreading of "parent"
as the within-name version predecessor.)

- `derivedFrom: null` for Consultologist-provided roots (`general`).
- `acct-x@v2`'s `derivedFrom` stays `general@v2026.07.5` across that account's own
  version bumps; a *rebase* updates it (e.g. to `general@v2026.08.1`). The
  within-name predecessor is already implicit in the CalVer sequence.
- The full derivation chain (`acct-b@v1 → acct-a@v4 → general@v2026.07.5 → null`)
  is **reconstructed by walking `derivedFrom`, never stored as an array** —
  derivable data stored redundantly can lie (the same rule that rejected the
  authored edge list).
- **No stored `root` field either (decided 2026-07-15)**: "which Consultologist
  package does this ultimately lead back to, if any?" is a real end-user question,
  but the answer is derivable by walking `derivedFrom` to null, and the same
  redundancy rule applies — a stored root can drift from the walked truth across
  rebases, and since forks diverge freely it carries no content guarantee anyway.
  GitHub's fork API is the model: it exposes both `parent` and `source` (the
  ultimate root), both *computed*, neither author-supplied. The root surfaces as a
  **derived view** on the read side (editor banner, package API), and because
  published versions are immutable a package's ancestry never changes — the
  computed root is cacheable forever.

**The fork model's one real cost, named**: overrides layered on a moving default
(`@latest`); forks freeze at their fork origin, so upstream improvements stop
flowing the moment anything is customized. `derivedFrom` is the metadata that makes
this tractable —
the editor can detect "your package derives from `general@v2026.07.5`;
`v2026.08.1` is available" and offer the v1 answer: **assisted re-fork** (re-fork
from the new upstream, replay the origin→fork diff, surface conflicts). A true
three-way merge is a later luxury. This cost only bites standards (the only layer
that ever floated); prompts and steps never layered.

Cross-account derivation (user forking another user's package) implies a
**package-sharing design** — visibility, discoverability, trust in someone else's
prompts. Explicit non-goal here; the `acct-*` owner-only access rule
(in-app-editing.md) stands, and `derivedFrom` is forward-compatible with sharing.

## 2. `data/` folder + `data:` binding namespace

Package-shipped content that binds into prompts without being a prompt — the prelude
mechanism's honest successor. Manifest gains a `data` table (like `schemas`);
files live under `data/`. The namespace rule that keeps provenance coherent:

| Source | Provenance regime |
|---|---|
| `input:…` | effective-input hash (what the physician supplied) |
| `item:…` | the current collection item (regime follows the collection's source) |
| `node:…` | per-node output hashes |
| `data:…` | the package ref (versioned workflow content) |

**Layout (decided 2026-07-15): each collection is a self-contained subdirectory with
an `index.json` describing itself** — item index plus the declared item shape — with
collection-relative file paths and per-item `.md` prose files (JSON-inline prose was
rejected as authoring-hostile):

```
packages/general/
├── manifest.json
├── prompts/…
├── schemas/…
└── data/
    ├── clinic-guidelines.md          # scalar: one bindable text
    └── standards/                    # collection: self-describing directory
        ├── index.json
        ├── hpi.md
        ├── pmh.md
        └── …
```

```json
// data/standards/index.json
{
  "fields": ["id", "name", "content"],
  "items": [
    { "id": "hpi", "name": "History of Present Illness", "file": "hpi.md" },
    { "id": "pmh", "name": "Past Medical History", "file": "pmh.md" }
  ]
}
```

The top manifest's `data` table maps an id to **either a file (scalar) or a
directory (collection)** — one table, two shapes, distinguished by what the path is:

```json
"data": {
  "clinic-guidelines": "data/clinic-guidelines.md",
  "standards": "data/standards/"
}
```

Rationale for the directory-with-`index.json` spelling over a flat index beside the
files: the collection becomes a cohesive unit (one directory = one collection —
copy, diff, and fork it as a whole, which the fork model's per-directory diffs
reward); it scales to multiple collections without paired `x.json` + `x/` clutter at
the `data/` root; and it gives the item-shape declaration (`fields`) a natural home
next to the items it describes — the `file` content becomes the `content` field, and
`item:` bindings validate statically against `fields`. `index.json` was chosen over
`collection.json` for familiarity (the `index.html` convention: the directory's
table of contents); the name is unambiguous against the package `manifest.json`.
Cost accepted: file gathering (store, validator, publish script) becomes two-stage —
top manifest → collection indexes → their files — bounded and mechanical.

**Standards become such a collection.** With the override layer gone, standards are
fully package-determined: the input/data straddle that milestone-era provenance
carried (standards hashed as input *because* the unversioned override made them vary
outside any artifact) **dissolves**.

Other data enters the same way: clinic guideline excerpts, specialty glossaries,
letter scaffolds — scalars or collections, versioned and diffed independently of the
templates that consume them.

**Why `prompts/` does not get an `index.json` (decided 2026-07-15)**: the question
was asked directly — if collections describe themselves, shouldn't the prompts
directory too? No. In a data collection the index earns its keep because the top
manifest deliberately knows nothing beyond the directory pointer, and two consumers
need the item declarations: `forEach` needs the item list to schedule per-item work,
and `item:` bindings need `fields` to validate statically. The collection describes
itself because nobody else does. Prompts already have their index — the manifest's
`prompts` table — and it sits there for a structural reason: a prompt's `variables`
are one half of a validation contract whose other half is the node `bindings` beside
it (the validator checks them set-equal). Splitting prompt declarations into
`prompts/index.json` would put the two halves of one contract in different files,
buy no new capability (nothing iterates prompts; nothing binds `item:` against
them), and extend the two-stage gathering cost to a second directory for nothing.
Even the fork/diff argument fails: editing prompt *text* is already file-local, and
a variable change forces a same-edit binding change — naturally manifest-local. The
asymmetry is the feature: **"has an `index.json`" marks a bindable data collection**
(homogeneous items, forEach-iterable, declared shape) versus workflow structure the
engine consumes by id — the same boundary the binding namespaces draw. The
underlying rule, once more: put a fact next to its consumer, and never write it in
two places.

**Input-model consequence (must be explicit, never silent)**: with sections
package-determined, the effective-input hash can shrink to the draft — the sections'
content is redundant with the package ref. That is a provenance-semantics version
change. **Decided 2026-07-15: the hash shrinks to the draft for v5 jobs, and the
provenance record gains an explicit hash-definition version** so v4-era and v5-era
hashes are never compared as equals; the alternative (retaining section coverage for
cross-boundary continuity) was rejected as redundant coverage that merely repeats
the package ref. Per-consult section *selection*, if ever wanted, returns to the
input side as an id-filter against the package collection.

## 3. One node kind with `forEach`

The node calculus (output-contract-catalog.md) established that executor and
multiplicity are a node's only real dimensions and the floor is one kind; v5 is the
moment the one-kind spelling becomes the *simpler* design, because `data:`
collections make it validatable. The map container, its `steps` array,
`previous_step_output`, the v4→v3 lowering, and the `run-prose-step` activity all
retire. The canonical section pipeline respelled:

```json
{ "id": "draft-section", "forEach": "data:standards",
  "prompt": "standard-section-draft", "label": "Drafting section",
  "bindings": { "section_name": "item:name",
                "patient_trajectory_concepts": { "from": "node:create-patient-trajectory", "as": "concept-context" } } },

{ "id": "apply-patient-info", "forEach": "data:standards",
  "prompt": "patient-section-draft", "label": "Applying patient information",
  "bindings": { "standard_section_draft": "node:draft-section",
                "consult_draft": "input:consult_draft",
                "section_name": "item:name" } },

{ "id": "apply-instructions", "forEach": "data:standards",
  "prompt": "section-instructions", "label": "Applying section instructions",
  "bindings": { "patient_section_draft": "node:apply-patient-info",
                "section_name": "item:name",
                "section_standard": "item:content" } }
```

`previous_step_output` is gone: a `node:` edge between two nodes sharing the same
`forEach` collection is **item-aligned** — instance *i* reads instance *i*. The step
chain is plain graph structure.

**Edge semantics** (relational rules replacing the container's structural closures):

| Edge | Meaning | v5.0 status |
|---|---|---|
| forEach → forEach, same collection | item-aligned | allowed (the step chain) |
| scalar → forEach | broadcast (every instance reads the same value) | allowed (concept-context into every section) |
| forEach → scalar | **aggregate** (collection fans into one prompt) | **closed** — the old "map is terminal" closure as a rule; opens when a workflow needs e.g. a summary-over-all-sections node |
| forEach → forEach, different collections | cross-product ambiguity | closed — no honest semantics yet |

**Scheduling** generalizes to per-(node, item) readiness: section *i*'s second step
starts the moment *its* first step completes; the milestone-4 "map runs after all
prompt nodes" conservatism disappears naturally. Execution unifies on
`run-prompt-node` — **per-item, per-node provenance hashes fall out by
construction**, completing the #53 goal.

**Workflow-result contract**: with no container to define "the section outputs", the
manifest names the deliverable — `result: "node:apply-instructions"` — the per-item
outputs that section-completed events, entity section states, and the generated
consult hang off.

### Why a property and not a container (decision rationale, 2026-07-15)

The question "should `forEach` be a container loop or a node property?" was asked
directly and deserves its answer recorded, because someone will re-ask it.

Steelmanned, the container's advantages are real: lexical locality (the per-item
program reads top-to-bottom in one box), free closures (aggregate-not-bindable
because the container has no output id; `item:` scoping is lexical; one-map-only is
counting), an implicit result contract (the last step is the deliverable), and
author-proofing (chain membership is positional, so "forgot `forEach` on one node"
and "mismatched collections" cannot happen). The property's advantages: one node
kind (no second, lesser step species with parallel machinery for ids, bindings,
execution, SSE, and provenance), `previous_step_output` reduced to an ordinary
item-aligned edge, per-item *graphs* rather than only linear chains (a `steps` array
is structurally a list forever), and a natural opening for aggregation (an edge rule
to relax, versus inventing an aggregate output for a box).

**The sorting observation that decides it**: every container advantage is
*presentational* — locality, groupedness, implicit ordering, a friendly mental
model. Every property advantage is *semantic* — uniformity of execution, provenance,
vocabulary, and growth room. Presentation can always be derived from semantics;
semantics can never be retrofitted onto presentation. (The same asymmetry decided
the authored-edge-file question: store the minimal truth, generate the human view.)

Therefore:

- **The manifest stores the property spelling** — one kind, `forEach`, flat list,
  with the authoring convention of keeping a chain's nodes contiguous.
- **Every human surface renders the container**: the derived DAG diagram draws
  same-collection forEach chains as a grouped subgraph ("per section" box), and the
  in-app editor's steps tab *is* a container UI — an ordered list that reads and
  writes `forEach` nodes underneath. Authors in the editor never perceive the flat
  spelling.
- **The closures survive as validator rules** (aggregate closed, cross-collection
  closed, `item:` requires `forEach`, chained nodes must share a collection) — more
  rules than the container needed, each a one-liner, and relaxable one at a time
  where a container must be demolished all at once.
- **Both spellings are NOT accepted as input dialects** — two ways to write the same
  semantics is the keep-format-dumb violation. v4 containers remain valid forever
  via synthesis (they lower mechanically to forEach chains); v5 packages write only
  the property spelling.
- The container's one irreplaceable feature — the implicit result — costs a single
  explicit `result:` field, which is an improvement anyway: a workflow's deliverable
  deserves to be named, not inferred from which box something happened to be last in.

In short: the container was the right training wheels for v4 (it made the closures
trivially sound while the interpreter was new); the property is the right skeleton
for v5, with the container living on as the derived view and the editor metaphor.

## 4. Migration sketch (for the eventual implementation plan; not planned here)

- v4→v5 synthesis is mechanical: the container map lowers to forEach nodes
  (steps → nodes, `previous_step_output` → item-aligned `node:` edges,
  `item:standard` → `item:content`); `standards.md` parses once into the collection
  spelling. One interpreter path for all spec versions, as always.
- Entity/SSE generalize to per-item node status (SchemaVersion bump;
  `section-prose-step` folds into per-item `node-completed`); strict drain window.
- The in-app editor (#57) is the UX that makes fork-everything one-click
  (fork-on-first-edit; the "My standards" tab becomes per-section editing of the
  fork's `data/standards/*`); its content/publish contract carries `data/` and
  `derivedFrom`.
- The output-contract catalog (#55 PR 1) is orthogonal and can land before or after.

## 5. Relationship to shelved work

| Item | Effect |
|---|---|
| #53 (prose-step unification) | **Subsumed** — achieved by one-kind execution |
| #55 PR 2 (unification via catalog) | Subsumed with it |
| #55 PR 1 (output-contract catalog) | Orthogonal; unaffected; still the enabler for new output shapes |
| #57 (in-app editor) | Reframed onto the fork model; standards tab and publish contract become v5-shaped; the editor is v5's UX half |
| Deferred SCTID pattern, HITL/`function` kinds, eval gating (#16) | Unchanged; kinds still grow only along the executor axis |
