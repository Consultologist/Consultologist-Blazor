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

public class WorkflowV25NodeModelTests
{
    private static WorkflowNodeSpec FirstNode(WorkflowPackageManifest manifest) => manifest.Nodes!.First();

    [Fact]
    public void FormatWithAndOnInvalid_RoundTrip_WithNestedJsonPreserved()
    {
        var (manifest, _) = V25Fixtures.Base();
        var node = FirstNode(manifest) with
        {
            Format = "terms-subset",
            With = new Dictionary<string, JsonNode?>
            {
                ["of"] = "node.extract",
                ["values"] = new JsonArray("in_scope", "out_of_scope"),
                ["nested"] = new JsonObject { ["a"] = 1 }
            },
            OnInvalid = WorkflowOnInvalid.Fail
        };
        var at25 = manifest with { Nodes = manifest.Nodes!.Select(n => n.Id == node.Id ? node : n).ToList() };

        var read = V25Fixtures.RoundTrip(at25, out var json);
        var readNode = FirstNode(read);

        Assert.Equal("terms-subset", readNode.Format);
        Assert.Equal("fail", readNode.OnInvalid);
        Assert.Equal("node.extract", readNode.With!["of"]!.GetValue<string>());
        Assert.Equal(2, readNode.With["values"]!.AsArray().Count);
        Assert.Equal(1, readNode.With["nested"]!["a"]!.GetValue<int>());

        var wire = V25Fixtures.Keys(json)["nodes"]![0]!;
        Assert.Equal("terms-subset", wire["format"]!.GetValue<string>());
        Assert.NotNull(wire["with"]);
        Assert.Equal("fail", wire["onInvalid"]!.GetValue<string>());
    }

    [Fact]
    public void OnInvalid_IsAClosedSet()
    {
        Assert.Equal(new[] { "fail", "retry", "flag" }, WorkflowOnInvalid.All);
        Assert.True(WorkflowOnInvalid.IsKnown("flag"));
        Assert.False(WorkflowOnInvalid.IsKnown("ignore"));
        Assert.False(WorkflowOnInvalid.IsKnown(null));
    }

    [Fact]
    public void AV24Node_WritesTheBytesItAlwaysWrote()
    {
        var (manifest, _) = V24Fixtures.Base();

        var wire = V25Fixtures.Keys(JsonSerializer.Serialize(manifest, V25Fixtures.Wire))["nodes"]![0]!;

        Assert.Null(wire["format"]);
        Assert.Null(wire["with"]);
        Assert.Null(wire["onInvalid"]);
    }

    [Fact]
    public void ADeliverableInstance_RoundTrips_AndIsNotBare()
    {
        var entries = new List<WorkflowDeliverableMacroSpec>
        {
            "closing",
            new("disclaimer", At: WorkflowDeliverableMacroSpec.Prepend, When: "input:jurisdiction == on"),
            new("signature", At: WorkflowDeliverableMacroSpec.Append)
        };

        var json = JsonSerializer.Serialize(entries, V25Fixtures.Wire);
        var wire = JsonNode.Parse(json)!.AsArray();

        Assert.Equal(JsonValueKind.String, wire[0]!.GetValueKind());
        Assert.Equal("""{"id":"disclaimer","at":"prepend","when":"input:jurisdiction == on"}""", wire[1]!.ToJsonString());
        Assert.Equal("""{"id":"signature","at":"append"}""", wire[2]!.ToJsonString());

        var read = JsonSerializer.Deserialize<List<WorkflowDeliverableMacroSpec>>(json, WorkflowPackageManifestJson.ReadOptions)!;

        Assert.True(read[0].IsBare);
        Assert.False(read[1].IsBare);
        Assert.Equal("prepend", read[1].At);
        Assert.False(read[1].IsAppend);
        Assert.True(read[2].IsAppend);
        Assert.Null(read[2].Before);
    }

    [Fact]
    public void AnAtOnly_Instance_SurvivesRepublish()
    {
        // The IsBare trap: an adornment the writer does not key on drops on republish.
        var once = JsonSerializer.Serialize(new WorkflowDeliverableMacroSpec("x", At: "append"), V25Fixtures.Wire);
        var twice = JsonSerializer.Serialize(JsonSerializer.Deserialize<WorkflowDeliverableMacroSpec>(once, WorkflowPackageManifestJson.ReadOptions), V25Fixtures.Wire);

        Assert.Equal(once, twice);
        Assert.Contains("\"at\"", twice);
    }

    [Fact]
    public void TheNodeKeys_ReadCaseInsensitively_TheWorkerLesson()
    {
        const string json = """{ "Id": "n", "Label": "N", "Format": "prose", "OnInvalid": "flag", "With": { "A": 1 } }""";

        var node = JsonSerializer.Deserialize<WorkflowNodeSpec>(json, WorkflowPackageManifestJson.ReadOptions)!;

        Assert.Equal("prose", node.Format);
        Assert.Equal("flag", node.OnInvalid);
        Assert.Equal(1, node.With!["A"]!.GetValue<int>());
    }
}

/// <summary>
/// Below 25 every new member is refused by name — the v22/v23 posture — so a
/// v24 manifest spelling one does not parse and pass where the strict reader
/// used to refuse it as unmapped. Nothing a v24 manifest could say before
/// changes meaning.
/// </summary>
public class WorkflowV25BelowGateTests
{
    private static List<string> ErrorsAt24(Func<WorkflowPackageManifest, WorkflowPackageManifest> change, Action<Dictionary<string, string>>? withFiles = null)
    {
        var (manifest, files) = V24Fixtures.Base();
        withFiles?.Invoke(files);
        return V25Fixtures.Errors((change(manifest), files));
    }

    [Fact]
    public void TheV24Fixture_IsValid_AsBefore()
    {
        Assert.Empty(ErrorsAt24(m => m));
    }

    [Fact]
    public void Deliverables_Below25_IsRefusedByName()
    {
        Assert.Contains("deliverables requires specVersion 25.", ErrorsAt24(m => m with { Deliverables = m.Results, Results = null }));
    }

    [Fact]
    public void Snippets_Below25_IsRefusedByName()
    {
        Assert.Contains("snippets requires specVersion 25.", ErrorsAt24(m => m with { Snippets = new Dictionary<string, string> { ["s"] = "snippets/s.md" } }));
    }

    [Fact]
    public void Formats_Below25_IsRefusedByName()
    {
        Assert.Contains("formats requires specVersion 25.", ErrorsAt24(m => m with { Formats = new List<WorkflowFormatReference> { new("f", "formats/f.json") } }));
    }

    [Fact]
    public void AMacroInstruction_Below25_IsRefusedByName()
    {
        var errors = ErrorsAt24(
            m => m with { Macros = new List<WorkflowMacroSpec> { new("closing", "Closing", "macros/closing.md", Instruction: "x") } },
            files => files["macros/closing.md"] = "Closing.");

        Assert.Contains("Macro 'closing' declares instruction, which requires specVersion 25.", errors);
    }

    [Theory]
    [InlineData("format")]
    [InlineData("with")]
    [InlineData("onInvalid")]
    public void ANodeMember_Below25_IsRefusedByName(string member)
    {
        var errors = ErrorsAt24(m =>
        {
            var node = m.Nodes!.First();
            var changed = member switch
            {
                "format" => node with { Format = "prose" },
                "with" => node with { With = new Dictionary<string, JsonNode?> { ["of"] = "node.x" } },
                _ => node with { OnInvalid = "fail" }
            };
            return m with { Nodes = m.Nodes!.Select(n => n.Id == node.Id ? changed : n).ToList() };
        });

        var id = V24Fixtures.Base().Manifest.Nodes!.First().Id;
        Assert.Contains($"Node '{id}' declares {member}, which requires specVersion 25.", errors);
    }

    [Fact]
    public void AnObjectFormSlot_Below25_IsStillRefused()
    {
        // B6 #1005: the v13 sentence holds at 24; the lift is 25's.
        var errors = ErrorsAt24(m => m with
        {
            Inputs = (m.Inputs ?? new List<WorkflowInputSpec>()).Append(new WorkflowInputSpec("intake", "Intake", Required: false, Type: new WorkflowTypeSet(new[] { WorkflowInputTypes.Object }),
                Fields: new List<WorkflowFieldSpec> { new("age", "Age") }, ExpectedContent: WorkflowExpectedContent.Form)).ToList(),
        });

        Assert.Contains("Input 'intake' declares expectedContent 'form', which a slot of type 'object' cannot be filled from.", errors);
    }

    [Fact]
    public void ANodeLevelFailIfEmpty_Below25_IsRefusedByName()
    {
        // B5 #1004: below 25 the sentence lives in output.failIfEmpty; the node
        // member is 25's.
        var errors = ErrorsAt24(m =>
        {
            var node = m.Nodes!.First();
            return m with { Nodes = m.Nodes!.Select(n => n.Id == node.Id ? n with { FailIfEmpty = "Empty." } : n).ToList() };
        });

        var id = V24Fixtures.Base().Manifest.Nodes!.First().Id;
        Assert.Contains($"Node '{id}' declares failIfEmpty at the node, which requires specVersion 25; below 25 it is output.failIfEmpty.", errors);
    }

    [Fact]
    public void ADeliverableInstanceAt_Below25_IsRefusedByName()
    {
        var errors = ErrorsAt24(
            m => m with
            {
                Macros = new List<WorkflowMacroSpec> { new("closing", "Closing", "macros/closing.md") },
                Results = m.Results!.Select((r, i) => i == 0 ? r with { Macros = new List<WorkflowDeliverableMacroSpec> { new("closing", At: "append") } } : r).ToList()
            },
            files => files["macros/closing.md"] = "Closing.");

        var id = V24Fixtures.Base().Manifest.Results!.First().Id;
        Assert.Contains($"Result '{id}' macro 'closing' declares at, which requires specVersion 25.", errors);
    }

    [Fact]
    public void AFolderItemWhen_Below25_IsRefusedByName()
    {
        var errors = ErrorsAt24(
            m => m with { Data = new Dictionary<string, string>(m.Data ?? new()) { ["notes"] = "data/notes/" } },
            files =>
            {
                files["data/notes/index.json"] = """{ "fields": ["id", "name", "content"], "items": [ { "id": "a", "name": "A", "file": "a.md", "when": "input:kind == new" } ] }""";
                files["data/notes/a.md"] = "A.";
            });

        Assert.Contains("Data collection 'notes' item 'a' declares when, which requires specVersion 25.", errors);
    }
}
