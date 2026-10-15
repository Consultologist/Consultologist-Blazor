using System.Text.Json;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// B6 #1005 (design § 3.8): the catalogue as the editor reads it — the list,
/// each file with its schema and hash, the detail line's facts; the agent
/// yaml and the trailer never on the wire.
/// </summary>
public class WorkflowFormatsTests
{
    private static readonly WorkflowFormatsResponse Response = WorkflowFormats.Describe(TestFormats.Catalog);

    [Fact]
    public void TheList_IsEveryBundledFormat_InIdOrder_WithItsVersionAndRef()
    {
        Assert.Equal(TestFormats.Catalog.Version, Response.Version);
        Assert.Equal($"formats@{TestFormats.Catalog.Version}", Response.Ref);
        Assert.Equal(new[] { "classification", "concept_list", "prose", "rendered", "terms_subset" }, Response.Formats.Select(f => f.Id));
        Assert.All(Response.Formats, f => Assert.False(string.IsNullOrWhiteSpace(f.Description)));
    }

    [Fact]
    public void TheDetailLineFacts_FollowTheProducer()
    {
        var byId = Response.Formats.ToDictionary(f => f.Id);

        Assert.Equal(("attested", "concept-extraction", 1, null, true), (byId["concept_list"].Producer, byId["concept_list"].AgentName, byId["concept_list"].AgentVersion, byId["concept_list"].Op, byId["concept_list"].Attested));
        Assert.Equal(("attested", "classification", 1, true), (byId["classification"].Producer, byId["classification"].AgentName, byId["classification"].AgentVersion, byId["classification"].Attested));
        Assert.Equal(("computed", null, null, "terms_subset", false), (byId["terms_subset"].Producer, byId["terms_subset"].AgentName, byId["terms_subset"].AgentVersion, byId["terms_subset"].Op, byId["terms_subset"].Attested));
        Assert.Equal(("rendered", null, null, false), (byId["rendered"].Producer, byId["rendered"].AgentName, byId["rendered"].Op, byId["rendered"].Attested));
    }

    [Fact]
    public void TheNodePaneFacts_RideEachEntry()
    {
        var byId = Response.Formats.ToDictionary(f => f.Id);

        Assert.True(byId["prose"].OutputMacros);
        Assert.False(byId["concept_list"].OutputMacros);
        Assert.Equal("enum", byId["classification"].Type);
        Assert.Equal("values", Assert.Single(byId["classification"].Parameters!).Value.Kind);
        Assert.Equal(new[] { "failWith", "in", "of" }, byId["terms_subset"].Parameters!.Keys.Order());
        Assert.Equal("concept_list", byId["terms_subset"].Parameters!["of"].Format);
        Assert.NotNull(byId["concept_list"].Schema);
        Assert.Null(byId["prose"].Schema);
    }

    [Fact]
    public void TheFile_IsTheEditorsWriteForm_AndHashesToTheStamp()
    {
        foreach (var entry in Response.Formats)
        {
            var catalogued = TestFormats.Catalog.Formats[entry.Id];
            var errors = new List<string>();

            Assert.True(WorkflowFormatSpecJson.TryRead(entry.File, entry.Id, errors, out var reread), string.Join(" | ", errors));
            Assert.Equal(WorkflowFormatSpecJson.CanonicalJson(catalogued.Spec), WorkflowFormatSpecJson.CanonicalJson(reread!));
            Assert.Equal(WorkflowFormatSpecJson.Sha256Hex(catalogued.CanonicalJson), entry.FormatHash);
        }
    }

    [Fact]
    public void TheCustomProducer_IsItsAgent_NeverItsTrailer_AndNoYamlIsServed()
    {
        Assert.Equal(("custom-json", 1), (Response.Custom.AgentName, Response.Custom.AgentVersion));

        var wire = JsonSerializer.Serialize(Response);
        Assert.DoesNotContain("Trailer", wire, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Answer with one JSON object", wire, StringComparison.Ordinal);
        Assert.DoesNotContain("yaml", wire, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("instructions", wire, StringComparison.OrdinalIgnoreCase);
    }
}
