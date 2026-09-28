# Mixed typed + uploaded-document array inputs — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let one `array<text>` input hold typed rows + uploaded documents + previous-run deliverables together, combined into one array server-side with per-element provenance.

**Architecture:** The engine stops overwriting typed values with file-derived arrays and instead concatenates, in the order typed → documents → previous-run, stamping every element a positional origin (typed rows get a new `typed` origin kind). The Create/Consults intake renders all three sources together for `array<text>` and submits the typed portion alongside the files/refs for the same id. A new provenance origin kind means a provenance-registry bump.

**Tech Stack:** .NET 10 (Consultologist.Api isolated Functions, Consultologist.PackageFormat), Blazor WASM (Consultologist.Web), xUnit + bUnit, the `external/consultologist-provenance` GitOps submodule.

**Spec:** `docs/superpowers/specs/2026-09-28-mixed-typed-and-file-array-inputs-design.md`

## Global Constraints

- Only `array<text>` inputs (`WorkflowInputTypes.Array` + element `text`, i.e. `TakesSeveralDocuments`) mix sources. Every other input type keeps "typed OR one document OR one ref," byte-identical to today.
- A pure typed array (no files, no refs) records **no** origins — the `typed` kind is stamped only when an array actually mixes sources. Existing packages' bytes and provenance must not change.
- Element order is fixed: **typed rows (as entered) → documents (chosen order) → previous-run (chosen order)**. UI order matches value order.
- `InputOrigins[id]` stays a positional list, one origin per array element, aligned 1:1 with the combined array.
- Origins are recorded **beside** the effective-input hash, never inside it — the input hash is over the combined array text only.
- Commits: small, GPG-signed (key `333983ABC7132E90`), no AI attribution. No premature `Closes`.
- Deploy: engine release required to run; provenance submodule PR + pin bump; SWA frontend must not rely on mixing before the engine release is deployed.

## Review Focus

- **A file refused mid-mix** (too large / unreadable) while typed rows and other documents are present → the typed rows and accepted documents survive; only the refused file is reported (Task 3 tests).
- **Same id in both `Inputs` and `InputFiles` maps** where the typed value is *not* an array (defensive) → the combine only triggers for `array<text>`; a scalar id with a file keeps the overwrite path (Task 3 tests).
- **Loaded (previous-run) + documents on one id** → `MergeOrigins` must concatenate in element order, not overwrite; the combined origins length equals the array length (Task 3 tests).
- **Empty typed row alongside a valid document** → still blocked at submit by name ("row N is empty"), not silently dropped (Task 5 tests).
- **A mixed array's History rendering** → one positional row per element, `typed` rows read "entered as text," document rows keep extractor/pages; a pure typed array still shows no origins (Task 6 tests).

---

## Task 1: Provenance registry — the `typed` origin kind (submodule)

**Files:**
- Modify: `external/consultologist-provenance/provenance-versions.json` (version → `v2026.09.13`)
- Modify: `external/consultologist-provenance/provenance-record.md` (document the `typed` origin kind)

**Interfaces:**
- Produces: provenance version string `v2026.09.13`; the normative description of origin kind `typed` (element entered as text; carries `TextSha256` only; recorded beside the input hash; stamped only for a mixed array).

- [ ] **Step 1: Read the current registry**

Run: `cat external/consultologist-provenance/provenance-versions.json` and skim `provenance-record.md` for how `ambient-note` (v2026.09.11) / the input-origin section is written.

- [ ] **Step 2: Add the `typed` kind to the record**

In `provenance-record.md`, in the input-origin-kinds section, add an entry mirroring the existing kinds' prose:

```markdown
- `typed` (provenance@v2026.09.13): an array element the clinician entered as text
  in a slot that also carries documents or previous-run deliverables. Carries only
  `TextSha256` (the canonical text as it entered the effective-input map); no
  extractor, page count or file hash. Recorded beside the effective-input hash,
  never inside it. Stamped only for a mixed array — a pure typed input records no
  origin, as before.
```

- [ ] **Step 3: Bump the version**

In `provenance-versions.json`, set `version` to `v2026.09.13` (follow the file's existing shape; if it lists supported versions, append `v2026.09.13`).

- [ ] **Step 4: Commit (in the submodule)**

```bash
cd external/consultologist-provenance
git checkout -b typed-origin-kind
git add provenance-versions.json provenance-record.md
git commit -m "provenance@v2026.09.13: the typed element origin kind"
```

- [ ] **Step 5: Push + PR the submodule**

```bash
git push -u origin typed-origin-kind
gh pr create --repo Consultologist/consultologist-provenance --base main \
  --title "provenance@v2026.09.13: typed element origin kind" \
  --body "Adds the \`typed\` per-element origin kind for mixed array inputs (Consultologist-Blazor#872)."
```

- [ ] **Step 6: Bump the engine's submodule pin**

```bash
cd ../..                      # back to the engine repo
git add external/consultologist-provenance
git commit -m "Pin provenance to v2026.09.13 (typed origin kind)"
```

---

## Task 2: Engine — declare `ConsultInputOriginKinds.Typed`

**Files:**
- Modify: `src/Consultologist.Api/Models/ConsultGenerationRequest.cs` (add the constant, after `AmbientNote` ~:198)
- Test: `tests/` (a small guard, e.g. append to an existing origins test file)

**Interfaces:**
- Produces: `ConsultInputOriginKinds.Typed == "typed"`.

- [ ] **Step 1: Write the failing test**

Add to a suitable test class (e.g. `ProvenanceVersionSetTests` or a new `InputOriginKindsTests`):

```csharp
[Fact]
public void TypedOriginKind_IsTyped()
{
    Assert.Equal("typed", Consultologist.Api.Models.ConsultInputOriginKinds.Typed);
}
```

- [ ] **Step 2: Run it, verify it fails to compile**

Run: `dotnet test tests/Consultologist.Api.Tests.csproj --filter TypedOriginKind_IsTyped`
Expected: build error — `Typed` does not exist.

- [ ] **Step 3: Add the constant**

In `ConsultGenerationRequest.cs`, after the `AmbientNote` constant:

```csharp
    // #872 (provenance@v2026.09.13): an array element the clinician entered as
    // text in a slot that also carries documents or previous-run deliverables.
    // Carries only TextSha256; no extractor/pages/file hash. Stamped only for a
    // mixed array — a pure typed input records no origin, as before.
    public const string Typed = "typed";
```

- [ ] **Step 4: Run it, verify it passes**

Run: `dotnet test tests/Consultologist.Api.Tests.csproj --filter TypedOriginKind_IsTyped`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Consultologist.Api/Models/ConsultGenerationRequest.cs tests/
git commit -m "Add the typed input-origin kind (#872)"
```

---

## Task 3: Engine — combine typed + documents (+ refs) into one array

**Files:**
- Modify: `src/Consultologist.Api/Jobs/ConsultGenerationJobStarter.cs`
  - `ExtractInputFilesAsync` combine + typed origins (~:1814–1925, the overwrite at :1923)
  - `MergeOrigins` concatenate-per-id (~:1736–1745)
- Test: `tests/ConsultGenerationJobStarterTests.cs`

**Interfaces:**
- Consumes: `ConsultInputOriginKinds.Typed` (Task 2); `ConsultInputValue.OfArray`/`.Elements`/`.Kind`; `ConsultGenerationProvenance.Sha256Hex`, `CanonicalText.Normalize` (used at :1918); `ConsultInputOrigin(kind, extractor, pageCount, trackedChanges, FileSha256:, TextSha256:)`.
- Produces: for a `TakesSeveralDocuments`/`several` id, `inputs[id] = OfArray(typedElements ++ documentTexts)` and `origins[id]` a positional list of the same length (typed origins then document origins). `MergeOrigins` concatenates lists for a shared id (first ++ second).

- [ ] **Step 1: Write the failing test — combine typed + files, aligned origins**

In `ConsultGenerationJobStarterTests.cs`, add (mirror the existing extraction-test setup — a manifest declaring an `array<text>` input, a `request` with both `Inputs[id]` typed and `InputFiles[id]`):

```csharp
[Fact]
public async Task ExtractInputFiles_CombinesTypedRowsThenDocuments_WithAlignedOrigins()
{
    var manifest = /* package declaring array<text> input "consult_draft" */;
    var request = BaseRequest() with
    {
        Inputs = new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal)
        {
            ["consult_draft"] = ConsultInputValue.OfArray(new[] { ConsultInputValue.OfText("typed one") })
        },
        InputFiles = new Dictionary<string, IReadOnlyList<InputFilePayload>>(StringComparer.Ordinal)
        {
            ["consult_draft"] = new[] { TextFilePayload("doc one text") }   // helper: a .txt payload
        }
    };

    var extraction = await ConsultGenerationJobStarter.ExtractInputFilesAsync(
        request, manifest, TimeSpan.Zero, ocr: null, ocrMinConfidence: 0, CancellationToken.None);

    Assert.Null(extraction.Error);
    var value = extraction.Request.Inputs!["consult_draft"];
    Assert.Equal(
        new[] { "typed one", "doc one text" },
        value.Elements!.Select(e => e.Canonical).ToArray());

    var origins = extraction.Origins!["consult_draft"];
    Assert.Equal(2, origins.Count);
    Assert.Equal(ConsultInputOriginKinds.Typed, origins[0].Kind);
    Assert.Null(origins[0].Extractor);
    Assert.NotNull(origins[0].TextSha256);
    Assert.Equal(ConsultInputOriginKinds.Document, origins[1].Kind);
    Assert.NotNull(origins[1].Extractor);
}
```

- [ ] **Step 2: Run it, verify it fails**

Run: `dotnet test tests/Consultologist.Api.Tests.csproj --filter ExtractInputFiles_CombinesTypedRowsThenDocuments_WithAlignedOrigins`
Expected: FAIL — value has only `["doc one text"]` and origins has one entry (current overwrite).

- [ ] **Step 3: Implement the combine + typed origins**

In `ExtractInputFilesAsync`, replace the tail of the per-id loop (:1921–1924) so it prepends the already-present typed array elements and their `typed` origins:

```csharp
            // #872: for a mixed array<text> slot the request may already carry
            // typed rows in inputs[id]; keep them, in order, ahead of the
            // documents, each with a positional `typed` origin. A single
            // document into a text slot is unchanged (the else arm).
            if (several)
            {
                var typedElements = inputs.TryGetValue(id, out var existing)
                    && existing.Kind == ConsultInputKind.Array
                        ? existing.Elements!
                        : Array.Empty<ConsultInputValue>();

                var combined = new List<ConsultInputValue>(typedElements.Count + texts.Count);
                var combinedOrigins = new List<ConsultInputOrigin>(typedElements.Count + slotOrigins.Count);
                foreach (var element in typedElements)
                {
                    combined.Add(element);
                    combinedOrigins.Add(new ConsultInputOrigin(
                        ConsultInputOriginKinds.Typed, null, null, false,
                        TextSha256: ConsultGenerationProvenance.Sha256Hex(
                            CanonicalText.Normalize(element.HasCanonical ? element.Canonical : element.AsJson()))));
                }
                combined.AddRange(texts);
                combinedOrigins.AddRange(slotOrigins);

                inputs[id] = ConsultInputValue.OfArray(combined);
                origins[id] = combinedOrigins;
            }
            else
            {
                inputs[id] = texts[0];
                origins[id] = slotOrigins;
            }
```

(Also fold the typed elements' lengths into the aggregate cap at :1892 if the reviewer wants the cap to count typed text; note it in the PR. Minimal version: leave the cap over documents, since typed text already passed the door's own length check.)

- [ ] **Step 4: Run it, verify it passes**

Run: `dotnet test tests/Consultologist.Api.Tests.csproj --filter ExtractInputFiles_CombinesTypedRowsThenDocuments_WithAlignedOrigins`
Expected: PASS.

- [ ] **Step 5: Write the failing test — MergeOrigins concatenates a shared id**

```csharp
[Fact]
public void MergeOrigins_ConcatenatesForASharedId_InElementOrder()
{
    var loaded = OneOrigin(ConsultInputOriginKinds.PreviousRun);   // helpers building a 1-item list
    var docs = OneOrigin(ConsultInputOriginKinds.Document);
    var merged = ConsultGenerationJobStarter.MergeOriginsForTest(
        new() { ["x"] = loaded }, new() { ["x"] = docs });
    Assert.Equal(
        new[] { ConsultInputOriginKinds.PreviousRun, ConsultInputOriginKinds.Document },
        merged!["x"].Select(o => o.Kind).ToArray());
}
```

(Expose `MergeOrigins` to the test via an `internal` test seam or an existing `InternalsVisibleTo` wrapper named `MergeOriginsForTest`.)

- [ ] **Step 6: Run it, verify it fails**

Expected: FAIL — current `merged[id] = list` keeps only `docs`.

- [ ] **Step 7: Implement concatenation in `MergeOrigins`**

```csharp
        foreach (var (id, list) in second)
        {
            merged[id] = merged.TryGetValue(id, out var existing)
                ? existing.Concat(list).ToList()
                : list;
        }
```

- [ ] **Step 8: Run both tests, verify pass**

Run: `dotnet test tests/Consultologist.Api.Tests.csproj --filter "ExtractInputFiles_CombinesTypedRowsThenDocuments_WithAlignedOrigins|MergeOrigins_ConcatenatesForASharedId_InElementOrder"`
Expected: PASS.

- [ ] **Step 9: Write the guard test — files-only and typed-only unchanged**

```csharp
[Fact]
public async Task ExtractInputFiles_FilesOnly_IsUnchanged()
{
    // request with InputFiles[id] and NO Inputs[id] → array of just the docs, origins = docs only.
    // assert value.Elements == [docText] and origins.Count == 1, Kind == Document.
}
```

- [ ] **Step 10: Run it, verify it passes; commit**

```bash
git add src/Consultologist.Api/Jobs/ConsultGenerationJobStarter.cs tests/ConsultGenerationJobStarterTests.cs
git commit -m "Combine typed rows with uploaded documents into one array (#872)"
```

---

## Task 4: Frontend — submit the typed portion alongside files

**Files:**
- Modify: `src/Consultologist.Web/Pages/Consults.razor` — `SuppliedInputs()` (:2783), `EffectiveValue` (:3826), `IsFilled`/`EffectiveText` as needed
- Test: `tests/Consultologist.Web.Tests/ConsultsSetupTests.cs` (or the intake test file)

**Interfaces:**
- Consumes: `field.TakesSeveralDocuments`, `field.Supplied` (typed array from `Root.Supplied()`), `field.Documents`, `field.HasFile`.
- Produces: for a `TakesSeveralDocuments` field, `SuppliedInputs()` includes the typed array even when `HasFile`; `SuppliedFiles()` still carries the documents (unchanged). `EffectiveValue` returns the combined array (typed ++ documents ++ loaded texts).

- [ ] **Step 1: Write the failing test**

In the intake test file, render `Consults`, load a package with an `array<text>` input, add a typed row and attach a document (simulate via the field API or a fake file), submit, and assert the captured request has BOTH `Inputs[id]` (typed) and `InputFiles[id]` (the doc):

```csharp
[Fact]
public void MixedArrayInput_SubmitsTypedRowsAndFilesForTheSameId()
{
    // arrange: package with array<text> "consult_draft"; type one row; attach one doc.
    // act: click Create consult; capture the WorkflowConsultGenerationRequest.
    Assert.True(sent.Inputs!.ContainsKey("consult_draft"));       // typed portion present
    Assert.True(sent.Files!.ContainsKey("consult_draft"));        // document present
    Assert.Single(sent.Inputs["consult_draft"].Elements!);        // the one typed row
}
```

- [ ] **Step 2: Run it, verify it fails**

Expected: FAIL — `SuppliedInputs()` excludes the field because `HasFile` is true, so `Inputs` lacks the id.

- [ ] **Step 3: Implement the submit change**

In `SuppliedInputs()` (:2783), include the typed portion of a mixed array even when it has files/loaded:

```csharp
  private Dictionary<string, ConsultInputValue> SuppliedInputs() =>
    inputFields
      .Where(field =>
          (!field.HasFile && !field.HasLoaded && field.Supplied is not null)   // typed-only, as before
          || (field.TakesSeveralDocuments && field.Supplied is not null))       // #872: typed portion of a mix
      .ToDictionary(field => field.Id, field => field.Supplied!, StringComparer.Ordinal);
```

(`field.Supplied` is `Root.Supplied()` — the typed rows only; documents/loaded are not in `Root`, so this is exactly the typed portion. If there are no typed rows, `Supplied` is null and the id is omitted — files-only stays as today.)

- [ ] **Step 4: Update `EffectiveValue` for display/replay**

Replace (:3826) so a `TakesSeveralDocuments` field combines all present sources in order:

```csharp
    public ConsultInputValue EffectiveValue => TakesSeveralDocuments
      ? ConsultInputValue.OfArray(
          (Supplied?.Elements ?? Array.Empty<ConsultInputValue>())
            .Concat(Documents.Select(d => ConsultInputValue.OfText(d.PreviewText)))
            .Concat(Loaded.Select(l => ConsultInputValue.OfText(l.Text))))
      : HasFile ? ConsultInputValue.OfText(Documents[0].PreviewText)
      : HasLoaded ? ConsultInputValue.OfText(Loaded[0].Text)
      : Supplied!;
```

- [ ] **Step 5: Run the test, verify it passes**

Run: `dotnet test tests/Consultologist.Web.Tests/ --filter MixedArrayInput_SubmitsTypedRowsAndFilesForTheSameId`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Consultologist.Web/Pages/Consults.razor tests/Consultologist.Web.Tests/
git commit -m "Submit typed rows alongside documents for a mixed array (#872)"
```

---

## Task 5: Frontend — render typed rows + documents + loaded together

**Files:**
- Modify: `src/Consultologist.Web/Pages/Consults.razor` — the render dispatch chain (:220, :259, :311, :346)
- Test: `tests/Consultologist.Web.Tests/ConsultsSetupTests.cs`

**Interfaces:**
- Consumes: existing document-chip markup (:265–294), loaded-chip markup (:226–244), rows markup (:353–377), and the controls (:378–396).
- Produces: for `TakesSeveralDocuments`, a single block rendering typed rows (editable) → document chips (read-only, ↑/↓/×) → loaded chips (×) → `+ Add entry`, `Choose Files` (multiple), `PreviousRunPicker`, link control.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void MixedArray_ShowsTypedRowsAndDocumentChipsTogether()
{
    // render Consults; array<text> field; add one typed row; attach one document.
    // assert BOTH a typed row control (.input-field__row) AND a document chip
    // (.input-field__document) are present at the same time.
    Assert.NotEmpty(page.FindAll(".input-field__row"));
    Assert.NotEmpty(page.FindAll(".input-field__document"));
}
```

- [ ] **Step 2: Run it, verify it fails**

Expected: FAIL — attaching a file switches to documents-only; `.input-field__row` is absent.

- [ ] **Step 3: Rework the dispatch conditions**

In `Consults.razor`:
- `:220` `@if (field.HasLoaded)` → `@if (field.HasLoaded && !field.TakesSeveralDocuments)`.
- Delete the `:259` `else if (field.HasFile && field.TakesSeveralDocuments) { … }` block (its chip markup moves into the array branch below).
- `:311` `else if (field.HasFile)` → `else if (field.HasFile && !field.TakesSeveralDocuments)`.

- [ ] **Step 4: Fold documents + loaded into the array branch**

In the array branch (:346–398), after the rows loop and `+ Add entry`, and inside `@if (field.TakesSeveralDocuments)`, render (a) the loaded chips (moved from :226–244 with their `RemoveLoaded`), then (b) the document chips (moved from :265–294 with `MoveDocument`/`RemoveDocument`), then the existing `Choose Files` / `PreviousRunPicker` / link controls. Keep the order **rows → documents → loaded** for display? — Note: value order is typed → documents → loaded (Task 3/4); render documents then loaded beneath the rows to match. Reuse the exact chip markup already written; only its location changes.

- [ ] **Step 5: Run the test, verify it passes**

Run: `dotnet test tests/Consultologist.Web.Tests/ --filter MixedArray_ShowsTypedRowsAndDocumentChipsTogether`
Expected: PASS.

- [ ] **Step 6: Write the empty-row guard test**

```csharp
[Fact]
public void MixedArray_EmptyTypedRow_BlocksSubmit_ByName()
{
    // array<text>; add an empty typed row + attach a valid document.
    // assert the "row 1 is empty; fill it in or remove it" refusal shows and Create is blocked.
    Assert.Contains(Refusals(page), r => r.Contains("row 1 is empty"));
}
```

- [ ] **Step 7: Run it (should already hold via existing validation); adjust if needed; commit**

```bash
git add src/Consultologist.Web/Pages/Consults.razor tests/Consultologist.Web.Tests/
git commit -m "Render typed rows, documents and loaded together for a mixed array (#872)"
```

---

## Task 6: History — render the `typed` origin

**Files:**
- Modify: `src/Consultologist.Web/Pages/History.razor` — `DescribeOrigin` (:922)
- Test: `tests/Consultologist.Web.Tests/HistoryDetailTests.cs`

**Interfaces:**
- Consumes: `ConsultInputOrigin.Kind == "typed"`.
- Produces: `DescribeOrigin` returns "entered as text" for the `typed` kind; a mixed array shows one positional row per element.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void MixedArrayOrigins_RenderTypedThenDocument_Positionally()
{
    WithJob(3, inputOrigins: new Dictionary<string, IReadOnlyList<ConsultInputOrigin>>
    {
        ["consult_draft"] = new[]
        {
            new ConsultInputOrigin("typed", null, null, false, TextSha256: "aa"),
            new ConsultInputOrigin("document", "docintel", 2, false, FileSha256: "bb", TextSha256: "cc"),
        }
    });
    var page = Render<History>(p => p.Add(x => x.JobId, JobId));
    var text = page.Find(".provenance-list").TextContent;
    Assert.Contains("entered as text", text);
    Assert.Contains("read from a document", text);
}
```

- [ ] **Step 2: Run it, verify it fails**

Expected: FAIL — the `typed` kind falls into the default arm ("read from a document"), so "entered as text" is absent.

- [ ] **Step 3: Add the `typed` arm to `DescribeOrigin`**

In `History.razor` `DescribeOrigin` (:922), before the default arm:

```csharp
        "typed" => "entered as text",
```

- [ ] **Step 4: Run it, verify it passes**

Run: `dotnet test tests/Consultologist.Web.Tests/ --filter MixedArrayOrigins_RenderTypedThenDocument_Positionally`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Consultologist.Web/Pages/History.razor tests/Consultologist.Web.Tests/HistoryDetailTests.cs
git commit -m "Render the typed element origin in History (#872)"
```

---

## Task 7: Full build + suite + branch review

- [ ] **Step 1: Build**

Run: `dotnet build Consultologist.sln`
Expected: 0 errors.

- [ ] **Step 2: Full test suite**

Run: `dotnet test Consultologist.sln`
Expected: Api + Web + Admin green (E2E needs a running dev server — unrelated).

- [ ] **Step 3: Push branch, open engine PR (no premature `Closes`)**

```bash
git push -u origin mixed-file-typed-inputs-872
gh pr create --repo Consultologist/Consultologist-Blazor --base main \
  --title "Mixed typed + uploaded-document array inputs (#872)" --body "<summary; part of #872; needs provenance PR + engine release>"
```

- [ ] **Step 4: Merge order + release (gated on the user)**

Provenance submodule PR merges first (Task 1), then the engine PR; then an engine release so v-… runs. The SWA frontend ships on merge but must not be relied on for mixing until the release deploys.

---

## Verification (after release)

- In the running app, load a package with an `array<text>` input; type one row **and** attach one document; run a consult. Confirm the produced note reflects both, and History shows two positional origin rows: "entered as text" and "read from a document by …".
- A files-only run and a typed-only run are unchanged (byte-identical value; typed-only records no origins).

## Self-review notes

- **Spec coverage:** engine combine + typed origins (Task 3), typed kind (Task 2), provenance bump (Task 1), frontend render (Task 5) + submit (Task 4), History (Task 6), order/constraints (Global Constraints). All spec sections mapped.
- **Review Focus:** refused-file-mid-mix (Task 3 Step 9 extended), scalar-id-with-file (Task 3 else arm + guard), loaded+documents concat (Task 3 Steps 5–8), empty-row (Task 5 Step 6), History positional (Task 6). All have owning tests.
- **Type consistency:** `ConsultInputOriginKinds.Typed`, `ConsultInputValue.OfArray/.Elements/.Canonical/.HasCanonical/.AsJson`, `ConsultInputOrigin(kind, extractor, pageCount, trackedChanges, FileSha256:, TextSha256:)`, `field.Supplied/.Documents/.Loaded/.TakesSeveralDocuments` — consistent across tasks.
