# Area 01: byte-equivalence fixtures Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Freeze what today's engine (specVersion 30) produces — every public package's prompts and deliverables on fixed fictional inputs with deterministic stubs, the composer's bytes, the two C# concept renderers' bytes, and the 30 evaluator's answer for every condition and `let` — as in-repo capture files, before any later area changes the code that produces them.

**Architecture:** The public packages are vendored at their spec-30 tags into `tests/Fixtures/ByteEquivalence/`, with a hash index. A deterministic stub generator lives in the library (`WorkflowStubOutputs`), because the 31 harness, in-package tests and the corpus re-render reuse it. A test-side runner (`CaptureRun`) drives the real starter, entity and orchestrator on `FakeOrchestrationContext` with the production activities' pure halves as handlers, and writes one capture file per package scenario. Golden tests compare a fresh run with the committed capture, byte for byte. The 31 side of every comparison (the migrated package, the converter's JSON Logic, `migration-report.json`) is area 10's; this area produces the 30 side it compares against.

**Tech Stack:** .NET 10, xUnit, NSubstitute, Scriban 7.2.5, System.Text.Json; a file-based `dotnet run --file` script for vendoring.

**Spec:** `docs/customizable-workflow/package-format-v31/10-migration-and-tools.md` § 3 (the harness, the report, the fixtures) and § 2.4 (the converter's fixtures and vectors); `07-compiled-documents.md` § 8 (the test grammar the scenario and capture files mirror). Revision 10. Area issue #1498, parent #1496. Runbook: `BUILD-RUNBOOK.md` in the same folder.

## Global Constraints

- **Work only in** `~/Projects/worktrees/m39-area-01` on `m39-area-01`. `~/Projects/consultologist-workflows` is read-only: read it with `git show <tag>:<path>` and `git ls-tree -r --name-only <tag> <path>` only; never fetch, check out, or write there. Never run a `git tag` command (a hook refuses it).
- **Never edit** `.github/`, `.claude/`, `scripts/m39-build/`, `AGENTS.md`, `CLAUDE.md` or `BUILD-RUNBOOK.md`.
- **Today's engine is the ground truth and does not change.** No file under `src/Consultologist.Api/` or `src/Consultologist.Web/` is modified. In `src/Consultologist.PackageFormat/` the only change is the new file `WorkflowStubOutputs.cs`. `tests/Engine/EngineRun.cs`, `tests/Engine/FakeOrchestrationContext.cs` and every existing file under `tests/Golden/` stay byte-identical. If today's engine fails or misbehaves on a package, the capture records exactly that; nothing is fixed.
- **Goldens come from code.** Every capture file is written by running today's code, never by hand. This area writes no expected output of code that does not exist yet: no JSON Logic, no 31 package, no `migration-report.json`.
- **A missing capture is written and fails the test** with `Wrote <path>; review it and commit it.` (the idiom of `tests/EngineRunGoldenTests.cs` `Matches`). To regenerate one, delete it and run its test. A capture is never rewritten silently.
- **Bytes are exact.** Fixtures are read with `File.ReadAllBytes` / strict UTF-8 (`new UTF8Encoding(false, true)`) and compared without newline normalisation. `tests/Fixtures/ByteEquivalence/.gitattributes` holds `* -text` so git never rewrites a line ending. Captures are generated on Linux (CI is `ubuntu-latest`; `ConceptRenderers` joins with `Environment.NewLine`).
- **Capture JSON is written one way:** `WriteIndented = true`, `Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping`, `\n` newlines, one trailing `\n`, members in the order this plan lists them.
- **Hashes** are `"sha256:" +` lowercase hex of SHA-256 over the text's UTF-8 bytes.
- **The vendored folder is `vendored/`, not `packages/`:** the root `.gitignore` hides every `packages/` folder.
- **Fixture root:** `tests/Fixtures/ByteEquivalence/`, found from source with `[CallerFilePath]` (the `EngineRunGoldenTests.SourceFile` idiom), never from a sibling checkout. No test reads `~/Projects/consultologist-workflows`.
- **Inputs are fictional, always:** invented people (the design's Margaret Okafor; `Dr A. Sample`) and invented clinical text, in every scenario, fixture package and vector.
- **31 stays off:** nothing here adds 31 to any accepted set.
- **Commits:** small, by concern, subjects `Tests (#1498): …`, `PackageFormat (#1498): …`, `Scripts (#1498): …`, `Docs (#1498): …`; each message ends with `Co-Authored-By: Claude <noreply@anthropic.com>`. Push after each task (`git push -u origin m39-area-01`).
- **Run tests** from the worktree: `dotnet test tests/Consultologist.Api.Tests.csproj --filter FullyQualifiedName~<Class>`.

## What the spike established (2026-11-20)

A throwaway run of all thirteen packages through today's engine found the following; tasks rely on it.

- `EngineRun` as written cannot be the capture runner: it registers three of the five activities these packages call (`render-deliverable-macros` and `decide-deliverables` are missing), records the bare render instead of the text sent (a classifier's and a custom-format node's trailers are absent), answers a `rendered` node with a stand-in, and never calls `ConsultNodeCompletion.Complete`.
- The production activities have pure halves a test can call: `ConsultRenderPayload.RenderAt25`, `ConsultNodeDispatch.Plan` / `.Settle`, `ConsultNodeCompletion.Complete`, `RunPromptNodeActivity.TemplateResult`, `RunComputedNodeActivity.Compute`, `DecideFlowItemActivity.Decide`, `RenderDeliverableMacrosActivity.Render`, `DecideActivity.Decide` (see `src/Consultologist.Api/Jobs/RunPromptNodeActivity.cs:290-360` for the production order).
- With those as handlers, eleven packages complete. **`example-macros` and `example-macros-warnings` fail at node `summary`** with *"Node 'summary' rendered macro.notes/red_flags.prompt, which the package's walk does not reach from its prompt; the walk and the render disagree."* `ConsultNodeCompletion.SanctionedBy` (`src/Consultologist.Api/Jobs/ConsultNodeCompletion.cs:176-186`) walks without the macro folders. That is today's engine, so the captures record the failure (Task 5).
- The final deliverable text is `State.AssembledDocuments[i].Text` (by `ResultId`); skipped ones are in `State.SkippedDocuments`, check-refused ones in `State.FailedDocuments`.
- The profile name comes from `IAccountStore.GetDisplayNameAsync`, the signature from the `AccountSettingKeys.ProfileSignatures` setting (a serialised `SignatureBlocks.SignatureBlockSet`); per-run macro choices travel as `ConsultGenerationRequest.MacroChoices`.
- A classifier's reply is `{"value":"<member>"}`. Two runs of the same package, inputs and stubs are byte-identical. The only run fact any package reads is `run.date`, from the fake context's fixed clock (`2026-11-10`).
- `dag.mmd` is never downloaded in production (`WorkflowPackageStore.RootFilesOf`), so the runner leaves it out of `files`.

## Layout this plan creates

```
scripts/vendor-public-packages.cs              reads the pinned tags, writes vendored/ and its index
src/Consultologist.PackageFormat/WorkflowStubOutputs.cs
tests/ByteEquivalence/
  ByteEquivalenceFixtures.cs                   paths, strict reads, hashes, the capture JSON writer, the golden match
  VendoredPackagesTests.cs
  WorkflowStubOutputsTests.cs
  CaptureScenario.cs                           the scenario file's model and reader
  CaptureRun.cs                                the faithful runner
  PackageCapture.cs                            a run as its capture document
  CaptureRunTests.cs
  PackageCaptureGoldenTests.cs                 one theory row per scenario file; the coverage tests
  ConceptRendererCaptureTests.cs
  ConditionVectorTests.cs
tests/Fixtures/ByteEquivalence/
  .gitattributes                               * -text
  README.md                                    what each folder holds, how to regenerate, what area 10 reads
  vendored/index.json                          per package: name, tag, commit, files → sha256
  vendored/<name>/…                            every file at the tag (test/general nests)
  fixture-packages/<name>/…                    packages authored here, for the composer
  scenarios/<name>/<scenario>.json             inputs, profile, macro choices, pinned outputs
  captures/<name>/<scenario>.json              written by the code; the goldens
  renderers/concept-renderers.json             the C# renderers' bytes
  conditions/expressions.json                  hand-written: every expression and what it reads
  conditions/vectors.json                      written by the code: 30's answer per expression and scope
```

## Review Focus

- **A capture that passes because it re-reads itself.** A golden test must compare a fresh run with the committed file; a missing file must fail, not pass. Task 4 pins both.
- **A scenario that pins an output for an instance that never runs** (a typo in a key) silently captures the generated stub instead. The runner refuses it; Task 3 pins the sentence.
- **A vendored file altered after vendoring** (an editor trimming whitespace, a line-ending rewrite). Task 1's index test catches any byte change and any file on disk the index does not name.
- **A stub invalid against its format** makes the engine take its `onInvalid` path, and the capture would freeze the wrong behaviour without anyone noticing. Task 2 validates stubs over every bundled and vendored format and many seeds; Task 3 fails a run whose generated stub the engine flags invalid.
- **A deliverable or prompt no scenario reaches** leaves a hole area 10 cannot compare. Task 4's coverage tests name every unreached deliverable and prompt node, with a reason for each allowed exception.

---

### Task 1: Vendor the public packages at their spec-30 tags

**Files:**
- Create: `scripts/vendor-public-packages.cs`
- Create: `tests/Fixtures/ByteEquivalence/.gitattributes`, `tests/Fixtures/ByteEquivalence/README.md`
- Create: `tests/Fixtures/ByteEquivalence/vendored/index.json` and `vendored/<name>/…` (written by the script)
- Create: `tests/ByteEquivalence/ByteEquivalenceFixtures.cs`
- Test: `tests/ByteEquivalence/VendoredPackagesTests.cs`

**Interfaces:**
- Produces, in namespace `Consultologist.Api.Tests.ByteEquivalence`:
  - `internal static class ByteEquivalenceFixtures` with `string Root` (the fixture root), `string ReadText(string path)` (strict UTF-8, no BOM handling, throws on invalid bytes), `string HashOf(string text)` (`sha256:<hex>`), `IReadOnlyList<VendoredPackage> Vendored` (from `vendored/index.json`), `IReadOnlyList<string> FixturePackages` (directory names under `fixture-packages/`, empty when the folder is absent), `(string ManifestJson, Dictionary<string, string> Files) ReadPackage(string name)` (looks in `vendored/` then `fixture-packages/`; `Files` keyed by path relative to the package, `manifest.json` and `dag.mmd` left out), `WorkflowPackage Load(string name)`.
  - `internal sealed record VendoredPackage(string Name, string Tag, string Commit, IReadOnlyDictionary<string, string> Files)`.
  - `Load` is: `WorkflowPackageManifestJson.Read(manifestJson, $"{name}@{version}", WorkflowPackageValidator.AcceptedSpecVersions)` then `WorkflowPackageStore.PackageOf(WorkflowPackageStore.Materialise($"{name}@{version}", manifest, files, null, V29Catalog.Catalog, TestFormats.Catalog, WorkflowPackageValidator.AcceptedSpecVersions))`, `version` being the manifest's.

**The pinned tags** (each package's latest tag, all specVersion 30; the script holds this list):

| Package | Tag |
|---|---|
| `example-classifier-scope` | `example-classifier-scope-v2026.11.3` |
| `example-conditional-documents` | `example-conditional-documents-v2026.11.3` |
| `example-conditional-sections` | `example-conditional-sections-v2026.11.3` |
| `example-macros` | `example-macros-v2026.11.3` |
| `example-macros-warnings` | `example-macros-warnings-v2026.11.3` |
| `example-nested-note` | `example-nested-note-v2026.11.2` |
| `example-prior-letters` | `example-prior-letters-v2026.11.1` |
| `example-problem-list` | `example-problem-list-v2026.11.2` |
| `example-structured-intake` | `example-structured-intake-v2026.11.3` |
| `example-two-documents` | `example-two-documents-v2026.11.3` |
| `example-v12-constructs` | `example-v12-constructs-v2026.11.3` |
| `general` | `general-v2026.11.3` |
| `test/general` | `test/general-v2026.11.3` |

- [ ] **Step 1: Write the failing tests** in `VendoredPackagesTests`:
  - `TheIndex_NamesTheThirteenPublicPackages_AtTheirPinnedTags`: the index's `(Name, Tag)` pairs equal the table above, in that order; every `Commit` is 40 hex characters.
  - `EveryVendoredFile_HasTheBytesTheIndexRecords`: for each package and each indexed path, `sha256:<hex>` of the file's bytes on disk equals the index's value.
  - `NoFileOnDisk_IsMissingFromTheIndex`: the set of files under `vendored/` (other than `index.json`) equals the set the index names.
  - `EveryVendoredPackage_IsAtSpecVersion30_AndLoads` (a theory over the thirteen names): the manifest's `specVersion` is 30, its `name` and `version` match the tag, and `ByteEquivalenceFixtures.Load(name)` does not throw.
- [ ] **Step 2: Run them; expect failure** (`index.json` is missing).
- [ ] **Step 3: Write `scripts/vendor-public-packages.cs`**, in the style of `scripts/validate-workflow-package.cs` (shebang, a usage comment, no project reference needed). Usage: `dotnet run -v q --file scripts/vendor-public-packages.cs -- <path to consultologist-workflows> [<fixture root>]`. For each pinned tag: resolve the commit with `git -C <repo> rev-parse <tag>^{commit}`; list `packages/<name>/` with `git ls-tree -r --name-only <tag>`; read each file's raw bytes with `git show <tag>:<path>`; write them unchanged under `vendored/<name>/`; record the sha256. It first deletes `vendored/` so a removed file cannot linger, then writes `index.json` as `{ "packages": [ { "name", "tag", "commit", "files": { "<relative path>": "sha256:<hex>" } } ] }`, files sorted ordinally, in the capture JSON style. A tag that does not resolve stops the script with exit code 1 and the sentence `Tag '<tag>' is not in <repo>: fetch the tags there and run again.`, writing nothing.
- [ ] **Step 4: Write `.gitattributes`** (`* -text`) **before** adding any vendored file, then run the script against `~/Projects/consultologist-workflows`.
- [ ] **Step 5: Write `ByteEquivalenceFixtures`** and `README.md` (what `vendored/` is, the script's command line, that the tags are the pins and the index is what CI checks).
- [ ] **Step 6: Run the tests; expect pass.** Confirm `git status --short ~/Projects/consultologist-workflows` printed nothing new.
- [ ] **Step 7: Commit** as two commits (`Scripts (#1498): the public packages are vendored from their spec-30 tags`, then `Tests (#1498): the vendored packages hold the tags' bytes and load at 30`) and push.

### Task 2: The deterministic stub generator

**Files:**
- Create: `src/Consultologist.PackageFormat/WorkflowStubOutputs.cs`
- Test: `tests/ByteEquivalence/WorkflowStubOutputsTests.cs`

**Interfaces:**
- Consumes: `ByteEquivalenceFixtures.Vendored`, `.ReadPackage` (Task 1); `TestFormats.Catalog`; `JsonSchemaSubsetValidator.Validate(JsonNode?, JsonNode)`.
- Produces, in namespace `Consultologist.PackageFormat`:

```csharp
/// The deterministic stubs of part 10 § 3.1: a value for a node's output made from its format
/// alone, seeded by the instance's key (the node's key, then ':' and its item path and pass).
public static class WorkflowStubOutputs
{
    public static string Text(string seed);                                   // a text format's reply
    public static string Member(IReadOnlyList<string> values, string seed);   // an enum's or classifier's member
    public static JsonNode? FromSchema(JsonNode? schema, string seed);        // a structured format's value
}
```

**The algorithm** (the tests do not determine it; write it exactly, and state it in the class's summary):

- `Pick(seed, path, n)` = the first eight bytes of SHA-256 over the UTF-8 of `seed + "\n" + path`, read as a big-endian unsigned 64-bit integer, modulo `n`. `Hex8(seed, path)` = the first eight lowercase hex characters of the same digest.
- `Text(seed)` = `"Stub reply for " + seed + " (" + Hex8(seed, "$") + ")."`
- `Member(values, seed)` = `values[Pick(seed, "$", values.Count)]`.
- `FromSchema(schema, seed)` walks from path `$`:
  - `enum` present → the member at `Pick(seed, path, count)`, cloned;
  - `const` present → that value;
  - `type` an array → its first member that is not `"null"`; no `type` → `object` when `properties` is present, else `string`;
  - `object` → every property in `properties`, in declared order, at path `<path>.<name>`;
  - `array` → `1 + Pick(seed, path, 3)` items, raised to `minItems` and lowered to `maxItems` when present, item `i` at path `<path>[<i>]`;
  - `boolean` → `Pick(seed, path, 2) == 0`;
  - `integer` and `number` → `Pick(seed, path, 10)`, raised to `minimum` and lowered to `maximum` when present;
  - `string` → `"stub " + Hex8(seed, path)`, cut to `maxLength` when present;
  - `null` → `null`.
- Whatever else `JsonSchemaSubsetValidator` enforces on the bundled and vendored formats (read it) is honoured the same way: the smallest deterministic value that validates.

- [ ] **Step 1: Write the failing tests:**
  - `TheSameSeed_GivesTheSameStub`, `ADifferentSeed_GivesADifferentText`.
  - `Text_IsTheStatedSentence`: `Text("draft:2/1")` starts with `"Stub reply for draft:2/1 ("` and ends with `")."`, with eight hex characters between.
  - `Member_IsOneOfTheValues_AndEveryValueIsReachable`: over seeds `n0`…`n63` and values `a, b, c`, every result is a member and all three occur.
  - `AnArray_HoldsOneToThreeItems_WithinItsBounds`: plain, with `minItems: 4`, with `maxItems: 1`.
  - `ANullableType_IsStubbedWithItsValue`: `{"type":["string","null"]}` gives a string.
  - `EveryBundledStructuredFormat_IsStubbedValid` and `EveryVendoredPackageFormat_IsStubbedValid`: for each schema (the catalogue's structured formats; each vendored package's `formats/*.json` `schema` member) and seeds `s0`…`s49`, `JsonSchemaSubsetValidator.Validate(JsonNode.Parse(stub.ToJsonString()), schema)` returns no error.
- [ ] **Step 2: Run; expect failure** (the class does not exist).
- [ ] **Step 3: Implement `WorkflowStubOutputs`.**
- [ ] **Step 4: Run; expect pass.**
- [ ] **Step 5: Commit** (`PackageFormat (#1498): stubs are made from a format alone, seeded by the instance's key`) and push.

### Task 3: The scenario file and the faithful runner

**Files:**
- Create: `tests/ByteEquivalence/CaptureScenario.cs`, `tests/ByteEquivalence/CaptureRun.cs`, `tests/ByteEquivalence/PackageCapture.cs`
- Test: `tests/ByteEquivalence/CaptureRunTests.cs`

**Interfaces:**
- Consumes: `WorkflowStubOutputs` (Task 2); `ByteEquivalenceFixtures` (Task 1); `FakeOrchestrationContext` (unchanged); `V30Package` (`tests/WorkflowV30Tests.cs`) for in-test packages.
- Produces:

```csharp
/// scenarios/<package>/<scenario>.json — every member present (07 § 8.1's shape, at 30's keys).
internal sealed record CaptureScenario(
    IReadOnlyList<string>? Description,
    IReadOnlyDictionary<string, JsonNode?> Inputs,        // raw value per declared input; null = left unset
    CaptureProfile Profile,
    IReadOnlyDictionary<string, bool> MacroChoices,
    IReadOnlyDictionary<string, JsonNode?> Outputs)       // pinned by instance key: a JSON string for a text format, else the JSON value
{
    public static CaptureScenario Read(string json);      // refuses a missing or unknown member by name
}
internal sealed record CaptureProfile(string? Name, string? Signature);

internal sealed class CaptureRun
{
    public static Task<CaptureRun> StartAsync(WorkflowPackage package, CaptureScenario scenario);
    public string Status { get; }                          // ConsultGenerationJobStatuses
    public string? Failure { get; }                        // the job's failure sentence, null when completed
    public IReadOnlyList<(string Key, JsonNode? Value)> Outputs { get; }     // every stub served, in served order
    public IReadOnlyList<(string Key, string Text)> Prompts { get; }         // the text SENT (ConsultNodeDispatch.Plan(...).Sent), in dispatch order
    public IReadOnlyList<(string Key, string Text)> Renders { get; }         // rendered nodes' output
    public IReadOnlyList<CapturedDeliverable> Deliverables { get; }          // in the manifest's order
}
internal sealed record CapturedDeliverable(string Id, string State, string? Reason, string? Text);   // State: composed | skipped | failed

internal static class PackageCapture
{
    /// The capture document, in the capture JSON style.
    public static string Write(string package, string? tag, string scenario, CaptureRun run);
}
```

**The capture document** (`captures/<package>/<scenario>.json`), members in this order:

```jsonc
{
  "package": "example-nested-note",
  "tag": "example-nested-note-v2026.11.2",      // null for a fixture package
  "scenario": "default",
  "status": "Completed",
  "failure": null,
  "outputs": { "plan": { "sections": [ … ] }, "draft_paragraph:1/1": "Stub reply for …" },
  "prompts": [ { "key": "plan", "hash": "sha256:…", "text": "…" } ],
  "renders": [ { "key": "patient_header", "hash": "sha256:…", "text": "…" } ],
  "deliverables": [ { "id": "note", "state": "composed", "reason": null, "hash": "sha256:…", "text": "…" } ]
}
```

A skipped or failed deliverable has `hash` and `text` `null` and its sentence in `reason`.

**How the runner runs.** `CaptureRun.StartAsync` follows `EngineRun.StartAsync` (`tests/Engine/EngineRun.cs`) for the starter, the entity and the fake context, with these differences:

- The starter's `IAccountStore` answers `GetDisplayNameAsync` with `scenario.Profile.Name`; its `IAccountSettingsStore` answers `AccountSettingKeys.ProfileSignatures` with one `SignatureBlocks.SignatureBlock("sig-1", "Clinic", scenario.Profile.Signature, 2026-11-01T00:00:00Z)` chosen, when the signature is not null. The request is `new ConsultGenerationRequest(null, Inputs: inputs, MacroChoices: scenario.MacroChoices)` (null when empty), `inputs` being `ConsultInputValue.FromJson(value.ToJsonString())` for each non-null scenario input. Both catalogues are the real ones (`V29Catalog.Catalog`, `TestFormats.Catalog`).
- `RunPromptNode` is production's order minus the model call: `RenderAt25`; for `node.Template == true`, record the render under `Renders` and return `RunPromptNodeActivity.TemplateResult(...)` stamped as production stamps it; otherwise `ConsultNodeDispatch.Plan`, record `plan.Sent` under `Prompts`, choose the reply, `ConsultNodeDispatch.Settle`, `ConsultNodeCompletion.Complete` with the trace's prompt reads.
- The reply: the scenario's pinned output for `node.NodeId` when there is one; otherwise by `plan.Kind`: `Enum` → `{ "value": WorkflowStubOutputs.Member(plan.Values, key) }`, `Structured` → `WorkflowStubOutputs.FromSchema(plan.Schema, key)`, anything else → `WorkflowStubOutputs.Text(key)`. The value is recorded under `Outputs`; the text sent to `Settle` is the string itself for a JSON string, else `ToJsonString()`.
- `RunComputedNode`, `DecideFlowItem`, `RenderDeliverableMacros` and `DecideDeliverables` call their production pure halves (`RunComputedNodeActivity.Compute`, `DecideFlowItemActivity.Decide`, `RenderDeliverableMacrosActivity.Render`, `DecideActivity.Decide`).
- A job that fails is a result, not an exception: `Status` and `Failure` carry it, and what was captured up to it stays. An exception that escapes the orchestrator from a handler is caught only when it is the job's own failure path; an activity name with no handler still throws `The test answers no activity '…'`.
- After the run, a pinned output whose key was never served throws `InvalidOperationException`: `Scenario pins <key>, which did not run.` A **generated** stub the engine flags invalid (`NodeRunResult` reporting the reply invalid) throws `Generated stub for <key> is not valid for its format: <the engine's sentence>.`

- [ ] **Step 1: Write the failing tests** in `CaptureRunTests`, on `V30Package.Minimal()` and small variants of it (loaded as `ByteEquivalenceFixtures.Load` loads, from the in-test manifest and files):
  - `AScenario_ReadsEveryMember_AndRefusesAMissingOne`: a scenario without `macroChoices` throws naming `macroChoices`; one with an extra member throws naming it.
  - `TheMinimal_Completes_AndEveryAgentNodesSentPromptIsCaptured`: status `Completed`; `Prompts` has one entry per `run-prompt-node` call that was not a rendered node; each key is the instance key.
  - `TheSentText_IncludesTheClassifiersTrailer`: the classifier node's captured prompt ends with the `Answer with exactly one of:` trailer that `ConsultNodeDispatch.Plan` appends, which `EngineRun`'s capture lacks.
  - `AStructuredNode_IsStubbedFromItsFormat_AndTheStubIsRecorded`: `Outputs` holds the node's key with a value valid against the package format.
  - `APinnedOutput_IsServedInsteadOfTheStub`: pin the plan node to one section; exactly one section instance runs.
  - `APinForAnInstanceThatDidNotRun_IsRefused`: message `Scenario pins nowhere:9, which did not run.`
  - `TwoRuns_AreByteIdentical`: `PackageCapture.Write` of two runs of the same scenario are equal.
  - `ADeliverable_IsCapturedWithItsFinalText`: the deliverable's `Text` equals `State.AssembledDocuments` text for it; a deliverable whose `if` is false is `skipped` with its reason.
  - `TheProfile_ReachesASignedDeliverable`: a variant with `"signature": true` ends with `"\n\n" + scenario.Profile.Signature`.
  - `AFailedJob_IsCaptured_NotThrown`: a pinned output of `""` on a node with `failIfEmpty` gives status `Failed`, a non-null `Failure`, and the prompts captured before it.
  - `TheCaptureDocument_HasItsMembersInOrder_AndEndsWithOneNewline`.
- [ ] **Step 2: Run; expect failure.**
- [ ] **Step 3: Implement** `CaptureScenario`, `CaptureRun`, `PackageCapture`.
- [ ] **Step 4: Run; expect pass.** Also run `--filter FullyQualifiedName~EngineRun` and confirm the existing goldens still pass untouched.
- [ ] **Step 5: Commit** (`Tests (#1498): a capture run sends what production sends, with stubs from the formats`) and push.

### Task 4: Captures for the first seven packages, and the coverage rules

**Files:**
- Create: `tests/ByteEquivalence/PackageCaptureGoldenTests.cs`
- Create: `tests/Fixtures/ByteEquivalence/scenarios/<name>/<scenario>.json` and `captures/<name>/<scenario>.json` for `example-nested-note`, `example-prior-letters`, `example-problem-list`, `example-conditional-documents`, `example-classifier-scope`, `example-conditional-sections`, `example-structured-intake`
- Modify: `tests/ByteEquivalence/ByteEquivalenceFixtures.cs` (add `Matches`), `tests/Fixtures/ByteEquivalence/README.md`

**Interfaces:**
- Consumes: `CaptureScenario.Read`, `CaptureRun.StartAsync`, `PackageCapture.Write` (Task 3); `ByteEquivalenceFixtures` (Task 1).
- Produces: `ByteEquivalenceFixtures.Matches(string relativePath, string actual)` — a missing file is written and the test fails with `Wrote <path>; review it and commit it.`; otherwise the file's exact text must equal `actual`. `PackageCaptureGoldenTests.Scenarios` — a `TheoryData<string, string>` of every `(package, scenario)` found under `scenarios/`, which Tasks 5 and 6 extend by adding files only.

**The scenarios.** Each package gets a scenario named `default` plus the further ones the coverage rules need. Every scenario file carries a `description` saying what it reaches. Inputs are written by hand, fictional, valid for the manifest's declared inputs; outputs are pinned only where a branch needs deciding. What each package needs reached:

| Package | Beyond `default` |
|---|---|
| `example-nested-note` | a scenario whose `judge` verdict is pinned not `good` on pass 1 and `good` on pass 2, so the `while` runs twice and the redraft's prompt reads `pass.previous`; one whose verdict is never `good`, so the bound (3) stops it |
| `example-prior-letters` | no prior letters; three letters with `read_letter`'s `answers_question` pinned `false`, `false`, `true`; three letters all `false` |
| `example-problem-list` | `follow_up` pinned `yes` and pinned `no` |
| `example-conditional-documents` | `encounter_kind` `new_patient`, `follow_up`, and left unset; `include_billing` both ways |
| `example-classifier-scope` | `scope` pinned to each of `in_scope`, `out_of_scope` (the signed decline letter), `needs_information` |
| `example-conditional-sections` | sections of both kinds and a `billing` section, with `include_billing` `true`, `false` and unset |
| `example-structured-intake` | each conditional deliverable both composed and skipped: `patient.age` 64 and 65, `length_of_stay` 7 and 8, two and three `prior_notes`, and the optional inputs unset |

- [ ] **Step 1: Write the failing tests** in `PackageCaptureGoldenTests`:
  - `AScenario_RunsToTheCapturedBytes` (theory over `Scenarios`): load the package, read the scenario, run, and `Matches($"captures/{package}/{scenario}.json", PackageCapture.Write(package, tag, scenario, run))`.
  - `AMissingCapture_IsWritten_AndFailsTheTest`: `Matches` on a temporary root writes the file and throws with `Wrote`; a second call passes; a call with different text fails without rewriting the file.
  - `EveryCapture_HasItsScenario`: no file under `captures/` lacks a scenario file.
  - `EveryDeliverable_IsComposedInSomeScenario` (theory over packages that have scenarios): each deliverable id in the manifest is `composed` in at least one capture of the package, except the ids in a `static readonly` exception table `(package, deliverable) → reason`, which starts empty.
  - `EveryAgentNode_HasACapturedPrompt` (same theory): each node with a prompt file has a `prompts` or `renders` entry in at least one capture of the package, except entries in an exception table `(package, node) → reason`, which starts empty.
- [ ] **Step 2: Run; expect failure** (no scenarios, no captures).
- [ ] **Step 3: Implement `Matches` and the tests' plumbing.**
- [ ] **Step 4: Write the scenario files** for the seven packages.
- [ ] **Step 5: Run to write the captures** (each new one fails once with `Wrote …`), then **read every capture file in full**: the prompts carry the scenario's fictional inputs and the stubs where the prompt reads them; trailers are present on classifier and custom-format nodes; each deliverable's text has its headings, macros and signature where the manifest places them; the branches the description names were taken. A capture that shows a run that did not do what its description says means the scenario is wrong: fix the scenario, delete the capture, run again.
- [ ] **Step 6: Run again; expect pass** for the seven packages' rows and the two coverage theories. Run the whole class twice to confirm nothing is rewritten (`git status --short` shows no change between runs).
- [ ] **Step 7: Update `README.md`** (scenario and capture shapes, how to add a scenario, how to regenerate) and **commit** in two commits (`Tests (#1498): capture goldens, a missing capture fails, and every deliverable and prompt is reached`; `Tests (#1498): captures of seven public packages at 30`) and push.

### Task 5: Captures for the remaining six packages

**Files:**
- Create: `scenarios/<name>/<scenario>.json` and `captures/<name>/<scenario>.json` for `general`, `test/general`, `example-two-documents`, `example-v12-constructs`, `example-macros`, `example-macros-warnings`
- Modify: `tests/ByteEquivalence/PackageCaptureGoldenTests.cs` (one test, and the exception tables), `tests/Fixtures/ByteEquivalence/README.md`

**Interfaces:**
- Consumes: Task 4's tests, unchanged apart from the exception tables.

| Package | Beyond `default` |
|---|---|
| `general` | none needed beyond `default`; add one with a pinned prose output that writes an inline macro's `{{ macro.<id>.content }}` placeholder if the package's prompts offer one |
| `test/general` | each `note_type` that changes which snippets or macros apply (read its prompts and snippet headers to find them); `consult_draft` with one entry and with three; the `test` macro appended to the deliverable is visible in the text |
| `example-two-documents` | `prior_notes` supplied and unset |
| `example-v12-constructs` | `tone` pinned `formal` and `friendly`; the three concept lists pinned so `note_coverage` and `letter_coverage` pass, and a scenario where `note_coverage` fails (the consult is `failed`, with its `failWith` sentence as the reason); the optional macro `followup` chosen, refused, and left to its default; optional inputs supplied and unset |
| `example-macros`, `example-macros-warnings` | `note_type` `consult`, `follow_up` and `discharge_summary`; `include_counselling` both ways; `follow_up_reminder` chosen, refused and default; sections of both kinds |

**`example-macros` and `example-macros-warnings`.** The spike found both fail on today's engine at node `summary` (see *What the spike established*). First confirm it with `CaptureRun`: the `default` scenario's status and failure sentence. If they fail, **capture the failure as it is** — the captures hold status `Failed`, the failure sentence, and the prompts sent before it — and add each deliverable and each unreached prompt node to the exception tables with the reason `today's engine fails this package at node 'summary': the walk does not reach macro.notes/<item>.prompt (ConsultNodeCompletion.SanctionedBy)`. Do not work around the failure, in the runner or the scenarios, and do not change the engine. If instead they complete, capture them like the others and say so in the report. Either way, report exactly what was seen.

- [ ] **Step 1: Write the failing test** `EveryVendoredPackage_HasADefaultScenario` in `PackageCaptureGoldenTests` (it names each vendored package with no `scenarios/<name>/default.json`), run it and see the six named, then **write the scenario files** for the six packages.
- [ ] **Step 2: Run to write the captures, and read every one in full** as in Task 4 Step 5. For `example-v12-constructs` check the two `terms_subset` checks decide what the description says and the rendered nodes are under `renders`.
- [ ] **Step 3: Run the whole class; expect pass**, including `EveryVendoredPackage_HasADefaultScenario` for all thirteen and both coverage theories, with only the exceptions the previous section allows.
- [ ] **Step 4: Update `README.md`** with the known failure, if seen, and **commit** (`Tests (#1498): captures of the remaining six public packages at 30`) and push.

### Task 6: The composer's bytes on fixture packages

Part 10 § 3.3 asks for fixtures that a generated deliverable template must reproduce: today's composer bytes (#1406). The public packages exercise the composer where they happen to; these fixture packages exercise each of its rules by itself.

**Files:**
- Create: `tests/Fixtures/ByteEquivalence/fixture-packages/composer-sources/…`, `fixture-packages/composer-macros/…`, `fixture-packages/composer-gates/…` (each a specVersion 30 package: `manifest.json`, prompts, snippets, macros, formats as needed)
- Create: `scenarios/composer-*/<scenario>.json`, `captures/composer-*/<scenario>.json`
- Modify: `tests/ByteEquivalence/PackageCaptureGoldenTests.cs` (a theory that every fixture package loads at 30), `tests/Fixtures/ByteEquivalence/README.md`

**Interfaces:**
- Consumes: Tasks 1, 3 and 4. `ByteEquivalenceFixtures.Load` already looks in `fixture-packages/`; captures of a fixture package have `"tag": null`.

Read `src/Consultologist.Api/Jobs/ConsultDeliverableComposition.cs`, `ConsultAggregateRenderer.cs`, `ConsultSignatureAppend.cs`, `RenderDeliverableMacrosActivity.cs` and the release path in `ConsultGenerationEngine.cs` (around lines 1840–1890) first. Between them the three packages' scenarios must reach each of these, and each scenario's `description` names the ones it reaches:

| Package | What its scenarios reach |
|---|---|
| `composer-sources` | one scalar source; two scalar sources (the blank-line join); a `for` loop source (`## <item name>` blocks, item order, `labelField` naming the block); a nested loop source; a `while` source (the last pass's text); an `if` source that took its branch and one that took none; a `case` source; a `let` of type text as a source; a loop with zero items; a source whose text is empty; a source whose text ends with newlines; a source whose text contains Markdown headings and `{{` |
| `composer-macros` | a deliverable macro `prepend`, `append`, and both; two macros at the same place (their order); a macro whose `if` holds and one whose `if` does not; a macro reading `profile.name`, `profile.signature` and `run.date`; an optional macro chosen, refused and defaulted; node macros `before` and `after` inside a loop's item; `signature: true` with a signature, and with no signature on the profile |
| `composer-gates` | two deliverables over the same node; a deliverable whose `if` is false (`skipped`, with its reason); a deliverable with a passing `check` and one refused by its check (`failed`, with the `failWith` sentence); a deliverable that reads another through `deliverable.<id>.content` in a prompt |

Any rule in that code that this table misses is added to the package it fits, with a scenario.

- [ ] **Step 1: Write the failing test** `EveryFixturePackage_IsAtSpecVersion30_AndLoads` (theory over `ByteEquivalenceFixtures.FixturePackages`, which must name exactly `composer-gates`, `composer-macros`, `composer-sources`).
- [ ] **Step 2: Run; expect failure.**
- [ ] **Step 3: Author the three packages** and validate each with `dotnet run -v q --file scripts/validate-workflow-package.cs -- tests/Fixtures/ByteEquivalence/fixture-packages/<name>` (expect it to report the package valid).
- [ ] **Step 4: Write their scenarios, run to write the captures, and read every capture in full**, checking each rule the description names against the composer code's stated behaviour.
- [ ] **Step 5: Run the class; expect pass**, the coverage theories included (every fixture package's deliverables composed in some scenario, with any deliberate exception in the table with its reason).
- [ ] **Step 6: Update `README.md`** and **commit** (`Tests (#1498): the composer's bytes at 30, rule by rule, on fixture packages`) and push.

### Task 7: The C# concept renderers' bytes

Part 10 § 3.3: the built-in Scriban renderers must match the C# renderers (#1397). `tests/V31BuiltInRenderersTests.cs` (area 00) compares the two live; this task freezes the C# bytes so the comparison outlives the C# renderers.

**Files:**
- Create: `tests/Fixtures/ByteEquivalence/renderers/concept-renderers.json`
- Modify: `tests/V31BuiltInRenderersTests.cs` (make `Render` and the registry read `internal static`, nothing else)
- Test: `tests/ByteEquivalence/ConceptRendererCaptureTests.cs`

**Interfaces:**
- Consumes: `ConceptRenderers.Bullets`, `ConceptRenderers.Context`; `ByteEquivalenceFixtures.Matches`; `V31BuiltInRenderersTests.Render(string rendererId, …)`.
- Produces the fixture, in the capture JSON style:

```jsonc
{ "cases": [ { "name": "one inactive",
               "concepts": [ { "term": "…", "type": "…", "id": "…", "isSnomedConcept": true, "isActive": false, "support": null, "source": "extract" } ],
               "concept_bullets": "…", "concept_context": "…" } ] }
```

The cases are the thirteen of `V31BuiltInRenderersTests.Cases` (same names, same values) plus: `two in order` (order is kept, never sorted), `non-ASCII term` (an accented term and a non-Latin one), `empty term`, `null type on a SNOMED concept`, `null id on a SNOMED concept`, `newline in support`, `long list` (twelve concepts). Null `type` and `id` reach the C# renderers as `""`, as `V31BuiltInRenderersTests.ToConcepts` passes them.

- [ ] **Step 1: Write the failing tests:**
  - `TheCSharpRenderers_GiveTheCapturedBytes`: build the document from the case table by calling the two C# renderers and `Matches("renderers/concept-renderers.json", document)`.
  - `TheRegistrysScribanRenderers_GiveTheCapturedBytes` (theory over the fixture's case names × the two renderers): the registry template rendered on the case's concepts equals the **fixture's** string, read from the file.
  - `TheFixture_NamesTheTwentyCases`.
- [ ] **Step 2: Run; expect failure**, then the first run writes the fixture.
- [ ] **Step 3: Read the fixture in full** against the two renderers' doc comments in `src/Consultologist.PackageFormat/ConceptRenderers.cs`. If a registry template disagrees with a captured string on a new case, the capture stands (it is the C# renderer's output); report the case and the two strings, leave that theory row failing, and stop the task there for the controller.
- [ ] **Step 4: Run; expect pass.**
- [ ] **Step 5: Commit** (`Tests (#1498): the C# concept renderers' bytes are frozen, and the registry's templates give them`) and push.

### Task 8: The 30 evaluator's answers, for the converter

Part 10 § 2.4 tests the converter by semantic equivalence: 30's evaluator on the Scriban text and 31's on the converted JSON Logic must agree on generated inputs, null and absent values included. Area 10 builds the converter; this task freezes 30's side: the expressions, the scopes, and what today's evaluator answers.

**Files:**
- Create: `tests/Fixtures/ByteEquivalence/conditions/expressions.json` (hand-written), `conditions/vectors.json` (written by the code)
- Test: `tests/ByteEquivalence/ConditionVectorTests.cs`
- Modify: `tests/Fixtures/ByteEquivalence/README.md`

**Interfaces:**
- Consumes: `WorkflowConditionAt25.TryParse` / `.Holds`, `WorkflowLetAt30.Evaluate` / `.ValueOf` / `.ToJson`; `TemplateRenderScope` (built as `tests/WorkflowConditionAt25Tests.cs:219` and `tests/WorkflowV30LetValueTests.cs:16` build it); `ByteEquivalenceFixtures.ReadPackage`, `.Matches`.

**`expressions.json`:**

```jsonc
{ "expressions": [
  { "id": "structured-intake.senior_review",
    "kind": "condition",                       // condition | let
    "expression": "input.patient.age >= 65",
    "letType": null,                           // a let's declared type: text | number | boolean | <format id>
    "origin": "package example-structured-intake, deliverable senior_review, if",
    "reads": { "input.patient": { "type": "object", "fields": { "age": "number" } } } } ] }
```

`reads` declares each thing the expression reads and its type: an input (`text`, `enum` with `values`, `number`, `boolean`, `date`, `array` with `items`, `object` with `fields`), a node (`text`, `classification` with `values`, or `object` with a JSON Schema), `item` (`id` and a `value` type), `pass.number`.

**It holds three groups:**

1. **Every condition and `let` in the vendored and fixture packages:** each manifest `if`, string `when`, `while`, `until`, `let` and `case` (on nodes, deliverables and macro placements) and each `if:` in a snippet's or macro's text header. `origin` names the package and site.
2. **One or more expressions per row of part 10 § 2.2's table**, `origin` `"§ 2.2: <the row's left cell>"`: reads of a node's value member, a node's content, `item.id`, `item.value.y`, `pass.number`, `run.date`; input reads `input.x` and `input.x.y`; `==` and `!=`; each of `<`, `<=`, `>`, `>=`; `&&` and `||` with three operands; `!a` on a boolean; `!a` on a text; each of `+ - * / %`; `(x | array.size)` and `x.size`; `string.downcase`, `string.strip`; `string.starts_with`, `string.ends_with`; `string.contains`; `date.parse "2026-11-10"` in a comparison; number, `true`, `false` and string literals; a `let` object `{ a: x, b: y }`; a `let` array `[x, y]`.
3. **One or more per refusal of § 2.3**, `origin` `"§ 2.3: <the bullet>"`: a builtin with no 31 operation (`string.upcase`, `math.round`, `array.first`); `<` on text; a read of `profile.name`; an assignment (`x = 1`); a template (`{{ input.kind }}`); two statements.

**`vectors.json`** — for each expression, in order: `{ "id", "refused": <30's sentence, or null>, "cases": [ { "scope": { "<read>": <value, or the string "<absent>"> }, "result": <true | false | the let's stored value as JSON>, "error": <the sentence when evaluation threw, else null> } ] }`. `refused` is `WorkflowConditionAt25.TryParse`'s error (with a template refused as `Walk` refuses it); a refused expression has no cases.

**The scopes are generated from `reads`, by these rules:**

- `text`: absent, `""`, `" "` (whitespace only, the `blank(x)` case of § 2.3), `"lorem"`, and each quoted string literal in the expression;
- `enum` and `classification`: absent and each declared value;
- `number`: absent, `0`, and for each numeric literal `n` in the expression `n - 1`, `n`, `n + 1`;
- `boolean`: absent, `true`, `false`;
- `date`: absent, `"2026-11-09"`, `"2026-11-10"`, `"2026-11-11"`;
- `array`: absent, empty, one item, three items;
- `object`: absent, and each field varied by its own type with the other fields at their first non-absent value;
- `item.id`: `"1"` and each quoted literal; `pass.number`: `1`, `2`, `3`.

The cases are the product over the expression's reads, in declared order, cut to the first 200. "Absent" must be what the engine makes of an unsupplied input or an unsettled node at that site, so build the scope's inputs through the engine's own effective-input map (`src/Consultologist.Api/Jobs/ConsultConditionScope.cs` and the starter's `ResolveEffectiveInputs`) and say in the README which function made them.

- [ ] **Step 1: Write the failing tests:**
  - `EveryConditionAndLetOfThePackages_IsInTheExpressions`: enumerate group 1 from the packages on disk and fail naming each `(package, site, expression)` the file lacks.
  - `EveryRowOf2_2_AndEveryRefusalOf2_3_HasAnExpression`: the set of `origin`s starting `§ 2.2: ` equals the table's sixteen left cells; the set starting `§ 2.3: ` equals the four bullets (both lists are constants in the test, copied from the spec).
  - `TheEvaluatorAt30_GivesTheCapturedAnswers`: generate the scopes, evaluate, and `Matches("conditions/vectors.json", document)`.
  - `AWhitespaceOnlyText_IsTruthyAt30`: for the `!a`-on-text expression, the case whose value is `" "` has result `false` — the one documented difference from `blank(x)`.
  - `AnAbsentInput_ReadsAsTheEngineMakesIt`: the pinned examples from `WorkflowConditionAt25.Holds`'s doc comment (an unsupplied optional text is `""`, a number null, an array empty).
- [ ] **Step 2: Run; expect failure.**
- [ ] **Step 3: Write `expressions.json`** and the scope generator and evaluator plumbing in the test class.
- [ ] **Step 4: Run to write `vectors.json`, and read it:** every package condition's cases include both outcomes where the type allows; every `error` is one today's engine would raise at that site, not a mistake in the scope builder.
- [ ] **Step 5: Run; expect pass.**
- [ ] **Step 6: Update `README.md`** (what area 10 does with the two files: convert each `expression`, evaluate the JSON Logic on each `scope`, compare with `result`, with the whitespace case listed as expected) and **commit** (`Tests (#1498): what the evaluator at 30 answers, for every package condition and every converter row`) and push.

---

## Spec coverage

| Spec | Task |
|---|---|
| 10 § 3.1 the harness is built first; renders the 30 package on fixed fictional inputs | 3, 4, 5 |
| 10 § 3.1 stubs generated from each node's format, deterministic, seeded by key, item path and pass; no reliance on format examples | 2, 3 |
| 10 § 3.1 compares every prompt and every deliverable, byte for byte | 3 (the 30 side: sent prompts, renders, final deliverable texts, with hashes); the comparison with 31 is area 10's |
| 10 § 3.1 the same harness runs in-package tests and the corpus re-render | 2 (the generator is in the library); the 31 runner is areas 06–11's |
| 10 § 3.2 the report's inputs: a site and a hash per compared text | 3 (`key`/`id` and `hash` per text in each capture); the report itself is area 10's |
| 10 § 3.3 every public package's prompts and notes on fictional inputs | 1, 4, 5 |
| 10 § 3.3 generated deliverable templates reproducing today's composer bytes (#1406) | 6 (the bytes to reproduce), 4, 5 |
| 10 § 3.3 the built-in Scriban renderers matching the C# renderers (#1397) | 7 |
| 10 § 3.3 the converter's fixtures and equivalence vectors; § 2.4 | 8 (expressions, scopes and 30's answers; the expected JSON Logic bytes are area 10's, from the converter) |
| 10 § 2.4 the documented `blank(x)` whitespace case, never silently accepted | 8 |
| Runbook: packages vendored from tags; in-repo fixtures; fails loudly | 1 |
