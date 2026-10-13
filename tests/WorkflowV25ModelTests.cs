using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// A3 (#998, package-format-v25-design.md §§ 3.6, 4, 5.1, 11): the manifest
/// MODEL at 25 — records and converters only. Every new shape round-trips
/// through the publisher's wire form; a v24 manifest still writes the bytes
/// it always wrote; below 25 each new key is refused by name; and 25 itself is
/// still refused, since the wiring and the bump are A4's.
/// </summary>
public static class V25Fixtures
{
    /// <summary>The publisher's options (WorkflowPackagePublisher.ManifestJsonOptions).</summary>
    public static readonly JsonSerializerOptions Wire = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Base()
    {
        var (manifest, files) = V24Fixtures.Base();
        return (manifest with { SpecVersion = 25 }, files);
    }

    public static WorkflowPackageManifest RoundTrip(WorkflowPackageManifest manifest, out string json)
    {
        json = JsonSerializer.Serialize(manifest, Wire);
        return JsonSerializer.Deserialize<WorkflowPackageManifest>(json, WorkflowPackageManifestJson.ReadOptions)!;
    }

    public static JsonObject Keys(string json) => (JsonObject)JsonNode.Parse(json)!;

    public static List<string> Errors((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle) =>
        WorkflowPackageValidator.Validate(bundle.Manifest, bundle.Files, TestOutputContracts.CatalogSchemas).Errors;
}

public class WorkflowV25ManifestModelTests
{
    [Fact]
    public void TwentyFive_IsStillRefused_UntilA4()
    {
        // The inert rule: A1–A3 ship shapes; A4 wires and bumps.
        Assert.DoesNotContain(25, WorkflowPackageValidator.AcceptedSpecVersions);
        Assert.Contains(V25Fixtures.Errors(V25Fixtures.Base()), e => e.Contains("specVersion 25 is not supported"));
    }

    [Fact]
    public void Deliverables_RoundTrip_UnderTheirOwnKey()
    {
        var (manifest, _) = V25Fixtures.Base();
        var list = manifest.Results!;
        var at25 = manifest with { Results = null, Deliverables = list };

        var read = V25Fixtures.RoundTrip(at25, out var json);
        var keys = V25Fixtures.Keys(json);

        Assert.NotNull(keys["deliverables"]);
        Assert.Null(keys["results"]);
        Assert.Null(keys["result"]);
        Assert.Equal(list.Select(d => d.Id), read.Deliverables!.Select(d => d.Id));
        Assert.Null(read.Results);
    }

    [Fact]
    public void Results_StillRead_AtAnyVersion()
    {
        var (manifest, _) = V24Fixtures.Base();

        var read = V25Fixtures.RoundTrip(manifest, out var json);

        Assert.NotNull(V25Fixtures.Keys(json)["results"]);
        Assert.Null(V25Fixtures.Keys(json)["deliverables"]);
        Assert.Equal(manifest.Results!.Count, read.Results!.Count);
    }

    [Fact]
    public void EffectiveDeliverables_ComeFromEitherKey()
    {
        var (manifest, _) = V24Fixtures.Base();
        var list = manifest.Results!;

        Assert.Same(list, manifest.EffectiveDeliverables);
        Assert.Same(list, (manifest with { Results = null, Deliverables = list }).EffectiveDeliverables);
        Assert.Empty((manifest with { Results = null }).EffectiveDeliverables);

        // The v5/v6 one-node `result` string is a different shape, not folded.
        Assert.Empty((manifest with { Results = null, Result = "node:assemble" }).EffectiveDeliverables);
    }

    [Fact]
    public void WithDeliverablesKeyFor_MovesTheListToTheVersionsKey()
    {
        var (manifest, _) = V24Fixtures.Base();
        var list = manifest.Results!;

        var at25 = manifest.WithDeliverablesKeyFor(25);
        Assert.Same(list, at25.Deliverables);
        Assert.Null(at25.Results);

        var back = at25.WithDeliverablesKeyFor(24);
        Assert.Same(list, back.Results);
        Assert.Null(back.Deliverables);

        var neither = manifest with { Results = null };
        Assert.Same(neither, neither.WithDeliverablesKeyFor(25));
    }

    [Fact]
    public void Snippets_RoundTrip_FilesAndFolders()
    {
        var (manifest, _) = V25Fixtures.Base();
        var at25 = manifest with
        {
            Snippets = new Dictionary<string, string> { ["standards"] = "snippets/standards.md", ["sections"] = "snippets/sections/" }
        };

        var read = V25Fixtures.RoundTrip(at25, out var json);

        Assert.Equal("snippets/standards.md", read.Snippets!["standards"]);
        Assert.EndsWith("/", read.Snippets["sections"]);
        Assert.NotNull(V25Fixtures.Keys(json)["snippets"]);
    }

    [Fact]
    public void ASnippetFolderItem_MayCarryAWhen()
    {
        const string index = """{ "fields": ["id", "name", "content"], "items": [ { "id": "a", "name": "A", "file": "a.md", "when": "input:kind == new" }, { "id": "b", "name": "B", "file": "b.md" } ] }""";

        var read = JsonSerializer.Deserialize<WorkflowDataIndexFile>(index, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.Equal("input:kind == new", read.Items![0].When);
        Assert.Null(read.Items[1].When);
    }

    [Fact]
    public void Formats_RoundTrip()
    {
        var (manifest, _) = V25Fixtures.Base();
        var at25 = manifest with { Formats = new List<WorkflowFormatReference> { new("medication_list", "formats/medication_list.json") } };

        var read = V25Fixtures.RoundTrip(at25, out var json);

        var format = Assert.Single(read.Formats!);
        Assert.Equal("medication_list", format.Id);
        Assert.Equal("formats/medication_list.json", format.File);
        Assert.Equal("formats/medication_list.json", V25Fixtures.Keys(json)["formats"]![0]!["file"]!.GetValue<string>());
    }

    [Fact]
    public void AMacrosInstruction_RoundTrips()
    {
        var (manifest, _) = V25Fixtures.Base();
        var at25 = manifest with { Macros = new List<WorkflowMacroSpec> { new("closing", "Closing", "macros/closing.md", Instruction: "Add a closing line if the plan changed.") } };

        var read = V25Fixtures.RoundTrip(at25, out var json);

        Assert.Equal("Add a closing line if the plan changed.", read.Macros!.First().Instruction);
        Assert.NotNull(V25Fixtures.Keys(json)["macros"]![0]!["instruction"]);
    }

    [Fact]
    public void AV24Manifest_WritesTheBytesItAlwaysWrote()
    {
        var (manifest, _) = V24Fixtures.Base();
        var at24 = manifest with { Macros = new List<WorkflowMacroSpec> { new("closing", "Closing", "macros/closing.md") } };

        var json = JsonSerializer.Serialize(at24, V25Fixtures.Wire);
        var keys = V25Fixtures.Keys(json);

        foreach (var key in new[] { "deliverables", "snippets", "formats" })
        {
            Assert.Null(keys[key]);
        }

        Assert.Null(keys["macros"]![0]!["instruction"]);
        Assert.Equal("""{"id":"closing","label":"Closing","file":"macros/closing.md"}""", keys["macros"]![0]!.ToJsonString());
    }

    [Fact]
    public void TheNewKeys_ReadCaseInsensitively_TheWorkerLesson()
    {
        // #864: the Functions worker serializes PascalCase; the reader must take either casing.
        var (manifest, _) = V25Fixtures.Base();
        var json = JsonSerializer.Serialize(manifest with { Results = null, Deliverables = manifest.Results, Snippets = new Dictionary<string, string> { ["s"] = "s.md" } },
            new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });

        Assert.Contains("\"Deliverables\"", json);
        Assert.Contains("\"Snippets\"", json);

        var read = JsonSerializer.Deserialize<WorkflowPackageManifest>(json, WorkflowPackageManifestJson.ReadOptions)!;

        Assert.NotNull(read.Deliverables);
        Assert.NotNull(read.Snippets);
    }
}
