# Node macros — one macro, two attachment sites (specVersion 23)

> **Status:** design (Milestone 28). Not yet implemented. Design-level, not a
> step-by-step plan — the implementation plan follows this.
>
> **One-line model:** a macro is a reusable, token-templated, conditionally-placed
> text block. Attach it to a **deliverable** and it shapes the **output** the
> reader sees (today's "output macro"); attach it to a **node** and it shapes the
> **prompt** the model sees (new "node macro"). Preludes are absorbed as the
> trivial node macro: *before · always · no tokens*.

## Why

Today three concepts put reusable text somewhere:

- **Macros** (`manifest.macros` library + `results[].macros[]` attachments) — token-templated
  text placed into a **deliverable's output** at assembly, gated by `when`.
- **Preludes** (`manifest.preludes` + `prompt.prelude`) — verbatim text **prepended to a
  prompt**, always, unconditionally, one per prompt, shared across every node using that prompt.
- **Prompt text** — the node's Scriban template.

A package author who wants "add this reusable instruction to *this node's* prompt, only
when a condition holds" has no first-class way to say it — they hand-edit the prompt or
abuse a data item's content. Preludes are the closest tool but are single, always-on,
verbatim, and prompt-scoped.

The macro machinery already does everything preludes can't (reuse, tokens, `when`,
placement) — it just targets the output instead of the prompt. So rather than invent a
parallel system, we give the **existing macro** a **second attachment site**: the node.

## The key realization: it is already one type

The macro *definition* is already unified in the manifest:

- `manifest.Macros : List<WorkflowMacroSpec>` — the **library** (id + file + `optional`).
  Already top-level, already shared.
- `results[].Macros : List<WorkflowResultMacroSpec>` — an **attachment** (a reference to a
  library macro + placement: `Before`, `After`, `When`, `ForItem`, `Slot`).

What reads as "two macro types" is **one library with one attachment site**. This feature
adds a **second attachment site** (`nodes[].macros[]`). Because node macros are greenfield,
there is no legacy node-macro code to reconcile — we build the second site correctly once.

| | Output Macro | Node Macro (new; absorbs preludes) |
|---|---|---|
| attaches to | a deliverable (`result`) | a node |
| lands in | the **output** (reader) | the **prompt** (model) |
| library | `manifest.macros` | **same** |
| templating | `{{input:}}`/`{{data:}}`/`{{run:}}`/`{{profile:}}` substitution at assembly, verbatim | **same** |
| condition | `when` (node-level grammar) | **same** |
| placement | `appended` / `before·after` a source / `slot` | `before` / `after` the prompt (`slot` only if the prompt carries a `[[slot:]]` marker) |

"Output Macro" / "Node Macro" are **labels for where a macro is attached** — one entity,
one library, one templating engine, one `when` grammar. The only host-dependent axis is
**placement**, because a deliverable is a multi-source aggregate while a prompt is single text.

## Data model (specVersion 23)

### Library — unchanged
`manifest.Macros` (`WorkflowMacroSpec`) stays exactly as it is. Node macros reference the
same entries by id.

### New: node attachment
A node gains an attachment list mirroring `results[].macros[]`:

```json
{ "id": "draft-section", "prompt": "draft-section",
  "macros": [
    { "id": "snomed-tool-guidance", "at": "before" },
    { "id": "ap-guardrails", "at": "after", "when": "input:consult_type == consult" }
  ],
  "bindings": { … } }
```

New record `WorkflowNodeMacroSpec`:

| field | meaning | required |
|---|---|---|
| `id` | reference into `manifest.macros` — **the same field `results[].macros[]` uses**, so the two attachment records share their reference + `when` shape | yes |
| `at` | `"before"` \| `"after"` the prompt (the host-dependent placement axis — a deliverable instead uses source-relative `before`/`after` + `slot`) | yes |
| `when` | condition, **node-level grammar** (`input:`/`node:` — identical to the node's own `when`; the evaluator has no `data:` operand); absent = always | no |
| `optional` | per-run choice (mirrors the library macro's `optional`) | no |

Like `WorkflowResultMacroSpec`, the wire form is either a bare id string (`"snomed-tool-guidance"`
= `before`, always) or a placement object. **The attachment is an object from day one** — the
forward-compat decision that lets Phase 2 add fields without a shape break.

### Token vocabulary — already shared (no change in Phase 1)
Node macros reuse the **existing** macro token engine (`ConsultMacroExpander.Expand`), whose set
is `{{input:}}`/`{{data:}}` (scalar)/`{{classification:}}`/`{{run:}}`/`{{profile:}}`. `{{data:}}`
**already exists** (it resolves a scalar data value), so there is **no token work** and **output
macros are untouched** in Phase 1. Every token is **run-global**, so node macros stay inside the
node-level scope (no per-item variation). (`{{data:}}`-as-*collection* and `{{item:}}` are Phase 2.)

## Prelude migration (v22 → v23, done by the editor's "Upgrade to specVersion 23")

Mechanical and behavior-preserving:

1. Each `manifest.preludes[id] = file` → a `manifest.macros` library entry (same id, same
   file, verbatim — no tokens). (If `manifest.macros` already holds that id, the upgrade
   reports a collision for the author to rename; expected to be rare.)
2. For each `prompt` with `prelude: X`, for **every node** that uses that prompt, append
   `{ "macro": "X", "at": "before" }` to that node's `macros`.
3. Remove `manifest.preludes` and every `prompt.prelude`.

A prompt used by N nodes yields N attachments pointing at one library entry — reuse of the
*text* is preserved; only the prompt-level auto-fan-out (future nodes inherit) is dropped,
by design. Rendered output at every node is identical to before.

## Engine resolution & the renderer seam

Preludes already follow "engine resolves text → renderer composes": `PromptTemplateRenderer`
takes a pre-resolved `PreludeText` and does `PreludeText.TrimEnd() + "\n\n" + rendered`.
Generalize it:

- The **engine** (where node `when` is already evaluated) resolves each node-macro
  attachment: evaluate `when` (existing node-level evaluator), and if it fires, substitute
  tokens (the **existing output-macro substitution code**, reused) to a final string.
- It collects the firing attachments into ordered `before` and `after` lists (attachment
  order within each).
- `PromptTemplateRenderer` takes `before: IReadOnlyList<string>` and `after:
  IReadOnlyList<string>` (replacing the single `PreludeText`) and composes
  `[…before, rendered, …after]` joined by blank lines. Migrated preludes arrive as `before`.

This keeps `Consultologist.PackageFormat` free of `when`-evaluation and run-context data
(token values live engine-side), exactly as it is for preludes today.

## Provenance / attestation

The attested prompt is the renderer's output. Because macros compose **into** that output,
the existing attestation covers them **provided the hash is taken after composition** (it is —
the composed string is the prompt sent). Node-level `when` is evaluated once per node, so the
resolved prompt is deterministic per run given inputs; no per-item attestation is introduced
(that is Phase 2). Optionally record which macro ids fired per node for provenance richness,
but the composed-prompt hash already captures the effect.

## Validation (specVersion 23)

- `nodes[].macros[].id` must resolve to a `manifest.macros` entry.
- `at ∈ {before, after}`.
- `when` uses the node-level grammar only (reuse the node-`when` validator, `ValidateV10Clause`);
  it has no `item:` operand (that arrives with Phase 2).
- The `macros` field on a node requires specVersion ≥ 23 (gated like `when` at v18).
- A node with no prompt has nothing to compose into — rejected.
- Macro *text* tokens are already validated at the library level (`ValidateMacroPlaceholders`), so
  node macros inherit that unchanged.
- Conformance fixtures: valid node-macro (before/after/when), bare-id form, invalid
  (unknown macro id, `item:` below Phase 2, node-macros below v23).

## Editor UI

- The **Macros** library section already exists in the nav — unchanged as the single source
  of macro text.
- **Node pane:** a new **Macros** sub-section (the originally-requested "macros on the node"):
  list attached macros, each with a macro picker (from the library), a before/after control,
  and a condition that rests quietly as "Always" and expands to the shared `ConditionField`
  (#936) — consistent with Produced-when / Runs-when. Position: alongside the node's other
  sub-sections.
- **Retire the Preludes section** (migrated into Macros). The per-prompt "shared prelude"
  selector is removed.
- UI makes the host-dependent placement obvious so the two attachment sites don't blur
  (a node macro offers before/after; a deliverable macro offers the fuller set).

## specVersion & phasing

Current newest is v22. This is **v23**, and it is Phase 1. Each later capability lands at its
own specVersion bump, gated by the validator (the format's established pattern — `when` @v18,
`forEach`, `schemaRefs` @v21, custom schemas @v22). We design the full shape now so later
phases slot in; we do **not** define fields the engine would reject (no "valid-but-unrunnable").

- **Phase 1 — v23 (this spec):** node macro attachment site, shared library, the existing token
  templating, `before`/`after`, node-level `when`, prelude migration. Scriban is **not** used —
  token substitution reuses the macro engine unchanged.
- **Phase 2 — v24 (follow-up):** the per-item layer — `{{item:name}}`/`item:` tokens,
  `item:` operands in `when`, and `forItem` on node attachments → per-fan-item prompt
  variation and its per-item attestation. Also revisit **`slot` on a node macro** (when the
  prompt text carries a `[[slot:]]` marker) here — deferred out of Phase 1 to keep node
  placement to `before`/`after`.

## Scope boundaries

**In scope (Phase 1):** everything above.

**Out of scope:**
- **Surfacing the output-macro `before/after`/`slot` placement on the node pane** — a
  separate, UI-only follow-up issue (a transposition like #940). The output macro's own
  attachment record and Documents placement are entirely unchanged here.
- **Scriban in macros** — deliberately dropped; token substitution is the templating model.
- **Per-item conditions/tokens** — Phase 2.

## Resolved decisions

- **Reference field = `id`** (not `macro`) — mirrors `results[].macros[]` so the two
  attachment records share their reference + `when` shape.
- **`slot` on a node macro is deferred** out of Phase 1 (revisited with Phase 2); Phase 1
  node placement is `before`/`after` only.
