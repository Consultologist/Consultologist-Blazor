using System.Text.Json;
using System.Text.Json.Serialization;
using Consultologist.Api.Workflow;

namespace Consultologist.Api.Tests;

/// <summary>
/// #407: exports the conformance suite that consultologist-package-format
/// publishes — the artifact that lets somebody outside this repo check a
/// manifest without building the engine.
///
/// Expectations are not hand-written. Every case is run through the real
/// validator and whatever it says is what gets recorded, so a fixture can
/// never claim an error message the engine does not produce.
///
/// Skipped unless CONFORMANCE_EXPORT names a directory to write into: this is
/// a generator, and regenerating on every CI run would be a slow way to write
/// files nobody reads.
/// </summary>
public class ConformanceFixtureExport
{
    private sealed record Case(
        string Id,
        int SpecVersion,
        string Description,
        WorkflowPackageManifest Manifest,
        IReadOnlyDictionary<string, string> Files);

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static List<Case> Cases()
    {
        var v5 = V5Fixtures.Manifest();
        var v6 = V6Fixtures.SingleCollection();
        var v7 = V7Fixtures.Minimal();
        var v7Multi = V7Fixtures.MultiDeliverable();
        var v8 = V8Fixtures.Minimal();
        var v8Typed = V8Fixtures.Typed();
        var v8Conditional = V8Fixtures.Conditional();

        var cases = new List<Case>
        {
            new("v5-minimal", 5, "The canonical v5 package: one collection, forEach sections, a single result.", v5, V5Fixtures.Files(v5)),
            new("v6-single-collection", 6, "v5 with an aggregator node as the deliverable — the v6 closure.", v6, V6Fixtures.Files(v6)),
            new("v7-minimal", 7, "One declared input, one deliverable. The smallest v7 package.", v7, V6Fixtures.Files(v7)),
            new("v7-multi-deliverable", 7, "Two independent chains, two declared deliverables, one optional input.", v7Multi, V6Fixtures.Files(v7Multi)),
            new("v8-minimal-is-v7-plus-a-line", 8, "The migration v8 promises: a valid v7 manifest with specVersion 8 and nothing else changed.", v8, V6Fixtures.Files(v8)),
            new("v8-typed-inputs", 8, "One input of each declared type: text, date, enum with values, optional boolean.", v8Typed, V6Fixtures.Files(v8Typed)),
            new("v8-conditional-deliverable", 8, "A deliverable that fires only when an enum input takes a given value.", v8Conditional, V6Fixtures.Files(v8Conditional)),
        };

        // Invalid cases. Each breaks exactly one rule against a baseline that is
        // otherwise valid, so the recorded error is attributable.
        void Invalid(string id, int spec, string description, WorkflowPackageManifest manifest, IReadOnlyDictionary<string, string>? files = null)
            => cases.Add(new Case(id, spec, description, manifest, files ?? V6Fixtures.Files(manifest)));

        Invalid("invalid-spec-version-unsupported", 3,
            "A specVersion the engine does not accept. Pre-v5 packages are archived, not executable.",
            v8 with { SpecVersion = 3 });

        Invalid("invalid-derived-from-latest", 8,
            "derivedFrom naming a moving pointer. Lineage must always be a concrete version.",
            v8 with { DerivedFrom = "general@latest" });

        Invalid("invalid-inputs-before-v7", 6,
            "Declared inputs on a v6 manifest. A section the version does not have is an error, never a silently ignored field.",
            v6 with { Inputs = new List<WorkflowInputSpec> { new("consult_draft", "Consult draft") } });

        Invalid("invalid-results-before-v7", 6,
            "A declared result set on a v6 manifest, which knows only a single result.",
            v6 with { Results = new List<WorkflowResultSpec> { new("note", "node:assemble-note", "Note") } });

        // From the multi-deliverable baseline, which declares results and no
        // result: adding result to v7-minimal changes nothing, because minimal
        // already carries the singular form inherited from v6.
        Invalid("invalid-result-and-results-together", 7,
            "Both the v5/v6 single result and the v7 result set. One package, one answer.",
            v7Multi with { Result = "node:assemble-note" });

        Invalid("invalid-typed-input-before-v8", 7,
            "An input declaring a type on a v7 manifest. Types arrive with v8.",
            v7 with { Inputs = new List<WorkflowInputSpec> { new("consult_draft", "Consult draft", Type: WorkflowInputTypes.Date) } });

        Invalid("invalid-condition-before-v8", 7,
            "A deliverable condition on a v7 manifest. Conditions arrive with v8.",
            v7Multi with { Results = v7Multi.Results!.Select((r, i) => i == 1 ? r with { When = "encounter_kind == follow_up" } : r).ToList() });

        Invalid("invalid-values-without-enum", 8,
            "values declared on an input that is not an enum. values belongs to enum and to nothing else.",
            V8Fixtures.WithInput(new WorkflowInputSpec("seen_on", "Date seen", Type: WorkflowInputTypes.Date,
                Values: new List<string> { "yes", "no" })));

        Invalid("invalid-enum-without-values", 8,
            "An enum input with no values to choose from.",
            V8Fixtures.WithInput(new WorkflowInputSpec("encounter_kind", "Encounter kind", Type: WorkflowInputTypes.Enum)));

        Invalid("invalid-unknown-input-type", 8,
            "An input type the format does not define.",
            V8Fixtures.WithInput(new WorkflowInputSpec("seen_on", "Date seen", Type: "timestamp")));

        Invalid("invalid-missing-prompt-file", 8,
            "A prompt the manifest references and the bundle does not carry.",
            v8, V6Fixtures.Files(v8).Where(f => !f.Key.EndsWith("section-instructions.md", StringComparison.Ordinal))
                .ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal));

        // Same reason: emptied on the baseline that uses the result SET, or the
        // singular result survives and the package fails the "not both" rule
        // instead — a different error wearing this case's name.
        Invalid("invalid-empty-results", 8,
            "A result set declaring no deliverables. A package that can produce nothing is not a package.",
            v7Multi with { SpecVersion = 8, Results = new List<WorkflowResultSpec>() });

        return cases;
    }

    [Fact]
    public void Export()
    {
        // A no-op without the variable rather than a skip: adding a package
        // just to render one line of CI output differently is a poor trade.
        var outDir = Environment.GetEnvironmentVariable("CONFORMANCE_EXPORT");
        if (string.IsNullOrWhiteSpace(outDir))
        {
            return;
        }

        var index = new List<object>();

        foreach (var testCase in Cases())
        {
            var result = WorkflowPackageValidator.Validate(
                testCase.Manifest, testCase.Files, TestOutputContracts.CatalogSchemas);

            // Cases about one format live under that format; a case about the
            // ACCEPTED SET itself belongs to no version — filing it under v3
            // would read as "the rules for specVersion 3", which do not exist
            // here at all.
            var group = WorkflowPackageStore.SupportedSpecVersions.Contains(testCase.SpecVersion)
                ? $"v{testCase.SpecVersion}"
                : "any";
            var dir = Path.Combine(outDir!, group);
            Directory.CreateDirectory(dir);

            var payload = new
            {
                id = testCase.Id,
                specVersion = testCase.SpecVersion,
                description = testCase.Description,
                expect = new
                {
                    valid = result.IsValid,
                    errors = result.Errors,
                    warnings = result.Warnings
                },
                manifest = testCase.Manifest,
                files = testCase.Files,
            };

            File.WriteAllText(Path.Combine(dir, $"{testCase.Id}.json"), JsonSerializer.Serialize(payload, Json));
            index.Add(new { id = testCase.Id, specVersion = testCase.SpecVersion, valid = result.IsValid, path = $"{group}/{testCase.Id}.json" });
        }

        File.WriteAllText(Path.Combine(outDir!, "catalog-schemas.json"),
            JsonSerializer.Serialize(TestOutputContracts.CatalogSchemas, Json));
        File.WriteAllText(Path.Combine(outDir!, "index.json"),
            JsonSerializer.Serialize(new { cases = index }, Json));
    }
}
