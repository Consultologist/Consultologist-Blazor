# Mixed typed entries + uploaded documents in one array input

Design spec for [#872](https://github.com/Consultologist/Consultologist-Blazor/issues/872). Date: 2026-09-28.

## Context

An `array<text>` input (e.g. `consult_draft` after test/general@v2026.09.12) can be filled two ways: the clinician types entries, or attaches documents that the server extracts. Today these are **mutually exclusive** for one input:

- **UI** (`Consults.razor`): the render chain hits `else if (field.HasFile && field.TakesSeveralDocuments)` (~:259) *before* the array/rows branch (~:346), so attaching a file swaps the whole field to a documents-only view and hides the typed rows. `EffectiveValue` (:3826) is `HasFile ? documents : HasLoaded ? loaded : typed`.
- **Submit**: `SuppliedInputs()` (:2783) excludes any field with a file or loaded deliverable; `SuppliedFiles()` (:2805) carries the file payloads separately.
- **Server**: `ConsultGenerationJobStarter.ExtractInputFilesAsync` extracts each file and does `inputs[id] = several ? OfArray(fileTexts) : texts[0]` (:1923) — it **overwrites** any typed value for that id, and sets `origins[id] = slotOrigins` (one document origin per element, positional).

So a clinician gets *typed OR files OR a previous-run*, never a mix. Typing two rows then attaching a `.docx` silently drops the typed rows from the run.

## Goal

Let one `array<text>` input hold **typed rows + one or more uploaded documents + previous-run deliverables together** — all shown, each reorderable/removable within its source, and **all submitted as one combined array** — while uploaded documents keep their full server-side extraction and document provenance (extractor, page count, tracked-changes, file hash, and the document/transcript/ambient/image origin kind).

### Non-goals
- Cross-source interleaving (a typed row *between* two documents). A single, defined element order is enough.
- Changing single-value (non-`array<text>`) inputs: they stay "typed OR one document," exactly as today.
- New extraction behavior, new upload limits, or a new file picker.

## Current behavior (reference)

| Concern | Location |
| --- | --- |
| Documents-only render branch (intercepts array) | `Consults.razor` ~:259–310 |
| Array/rows render branch | `Consults.razor` ~:346–398 |
| `EffectiveValue` (files→typed precedence) | `Consults.razor` :3826 |
| Typed submit (excludes `HasFile`/`HasLoaded`) | `Consults.razor` :2783–2786 |
| File payload submit | `Consults.razor` :2805–2812 |
| Server overwrites typed with files | `ConsultGenerationJobStarter.cs` :1923–1924 |
| Positional per-element origins (History) | `History.razor` :460–481 |
| Origin kinds (no "typed") | `ConsultGenerationRequest.cs` :145–198 |

## Design

### Element order (defined)
The combined array is assembled in a **fixed order**: **typed rows (as entered) → uploaded documents (in chosen order) → previous-run deliverables (in chosen order)**. The UI renders the sources in this same top-to-bottom order so what the clinician sees matches what runs. Within each source, existing ↑/↓/× reordering applies.

### Provenance: the key decision
`InputOrigins[id]` is a **positional list, one origin per array element** (`History.razor` :470 "one row per document, positionally"). It is display metadata, *not* part of the input hash. Today typed elements have **no** origin, and origins only exist for document/loaded/form/rerun sources. A mixed array in **typed-first** order therefore leaves the leading (typed) elements without positional origins, which would misalign the document rows in History.

**Chosen approach — introduce a `typed` origin kind (recommended).** Add `ConsultInputOriginKinds.Typed = "typed"`. In a *mixed* array, the engine stamps every element an origin, aligned 1:1 with the array: typed elements get a `typed` origin carrying only `TextSha256` (no extractor/pages/file hash); document elements keep their existing origins; previous-run elements keep the `previous-run` origin. History gains a `"typed"` arm in `DescribeOrigin` ("entered as text"). This keeps the natural typed-first order, gives every element positional provenance, and matches the product's provenance-completeness ethos.

- **Cost:** a new origin kind is, by the project's own precedent (transcript #671, image #730, ambient-note #673), a **provenance-registry bump** — a third repo (`consultologist-provenance`: version string + a row in `provenance-record.md`) and the engine records the new kind. It is **additive** (a new `Kind` string value; the `ConsultInputOrigin` record shape is unchanged), so no record-field/replay-golden churn.
- **Backward compatibility:** a *pure* typed array (no files, no loaded) records **no** origins, exactly as today — the `typed` kind is stamped only when an array actually mixes sources, so every existing package's bytes and provenance are unchanged.

**Alternative considered (documents-first, no bump).** Order the array documents-first so `origins[id]` (document origins only) aligns to the leading elements, with typed rows trailing and un-originned. Avoids the provenance bump but (a) reorders the array away from the typed-first mental model and (b) leaves typed elements without positional provenance, which reads as a gap in a provenance-first product. Rejected unless the reviewer prefers to avoid the provenance bump.

> **Decision (confirmed 2026-09-28):** the `typed`-origin-kind approach with the provenance bump. The documents-first alternative is not taken.

### Engine (`ConsultGenerationJobStarter.ExtractInputFilesAsync`)
- Instead of `inputs[id] = OfArray(fileTexts)` (overwrite), **combine**: read the typed value already in `request.Inputs[id]` (if present and an array), and build `OfArray(typedElements ++ documentTexts)` in the defined order. Non-array/single-doc inputs keep the current overwrite path unchanged.
- Build `origins[id]` as a positional list of the **same length** as the combined array: a `typed` origin (with `TextSha256` of each typed element's normalized text) per typed element, then the document origins as built today. Previous-run origins (from `resolution.Origins`, merged at :400 via `MergeOrigins`) must likewise align — confirm `MergeOrigins` appends rather than replaces for a shared id, and extend if needed so all three sources' origins concatenate in element order.
- The aggregate length cap (:1892) now counts typed + document text together for the slot.

### Frontend (`Consults.razor`)
- For `TakesSeveralDocuments`, render **one** unified block (fold the documents-mode chips at :265–294 into the array branch at :346): typed rows (editable) → document chips (read-only, with preview + ↑/↓/×) → loaded chips, followed by the `+ Add entry`, `Choose Files` (multiple), `PreviousRunPicker`, and link controls. Remove the early `HasFile && TakesSeveralDocuments` interception (:259) and scope the single-file branch (:311) to `!TakesSeveralDocuments`; scope the `HasLoaded` interception (:220) so a `TakesSeveralDocuments` array is handled by the unified block.
- **Submit:** `SuppliedInputs()` must include the typed portion of a `TakesSeveralDocuments` field **even when it also has files/loaded** (today it excludes any `HasFile`/`HasLoaded` field). `SuppliedFiles()`/`SuppliedRefs()` are unchanged (still carry the documents/refs for the same id). The same id may now appear in both the inputs map and the files/refs map — the server combine (above) is what merges them; confirm the request contract (`ConsultGenerationRequest`) needs no shape change (the maps are independent dictionaries).
- `EffectiveValue`/`EffectiveText` (display + memento) updated to reflect the combined array.
- Validation: an empty typed row still blocks submit ("row N is empty; fill it in or remove it"); a field is filled if it has any typed row **or** any document **or** any loaded deliverable.

### Request contract
No new fields expected: `Inputs`, `InputFiles`, and refs are separate maps and may all carry the same id. Confirm during implementation that nothing asserts an id appears in only one map.

## Data flow (mixed `consult_draft`)
1. Clinician types row 1, attaches `labs.docx`, adds a previous-run deliverable.
2. Submit sends: `Inputs["consult_draft"] = OfArray([row1])`, `InputFiles["consult_draft"] = [labs.docx bytes]`, `Refs["consult_draft"] = [deliverable ref]`.
3. Engine extracts `labs.docx`, copies the previous-run text, and assembles `Inputs["consult_draft"] = OfArray([row1, labsText, deliverableText])` with `origins = [typed, document, previous-run]`.
4. Prompts join it (`{{ consult_draft | array.join "\n\n---\n\n" }}`) → one combined text, as today.

## Error handling
- A refused file (too large, unreadable) leaves the field otherwise intact — typed rows and other documents remain (unchanged from today's per-file refusal).
- Aggregate over-length names the slot and counts all sources together.
- Empty typed rows block the run by name.

## Testing
- **bUnit** (`Consults`/intake tests): typed rows + attached documents render together; adding a file no longer hides typed rows; the combined submit carries typed values in `SuppliedInputs` and files in `SuppliedFiles` for the same id; absence-gating and empty-row validation still hold.
- **Engine** (`ConsultGenerationJobStarterTests` / extraction tests): a request with both `Inputs[id]` (array) and `InputFiles[id]` produces the combined array in the defined order with aligned per-element origins (`typed` then `document`); a files-only request is byte-identical to today; a typed-only request records no origins.
- **Provenance**: `typed` origin kind round-trips; History renders the `typed` arm; provenance-registry mirror/guard tests updated for the new kind (if the recommended approach is confirmed).
- **End-to-end** (after release): run test/general with one typed row + one document + confirm both appear in the produced note and both origins show in History.

## Deploy / rollout
- **Engine release** required (server assembly change).
- If the `typed` origin kind is adopted: a **provenance-registry** version bump (third repo) — additive, no record-field/replay-golden churn — plus the engine stamping it. Same three-repo shape as #671/#730/#673.
- Frontend ships on the SWA (path-filtered) on merge; the mixed submit degrades safely against the pre-release engine only in that the server would still overwrite — so the frontend change should land with or after the engine release, not before. Sequence: engine (+ provenance) first, then frontend, or same PR train with the release cut before the SWA relies on mixing.

## Decisions (confirmed 2026-09-28)
1. Provenance: **`typed` origin kind + provenance-registry bump** (not documents-first).
2. Element order: **typed → documents → previous-run**.
3. `expectedContent` (transcript/ambient) stamps its kind on the *document* elements only; typed rows are `typed`.
