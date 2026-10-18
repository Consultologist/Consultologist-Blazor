using System.Text.Json;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1066: the content (and ingest) response carries the manifest as its
/// canonical JSON — camelCase, no null members — not as the record the worker's
/// default serializer writes. At 25 that default put "Results": null beside
/// "Deliverables": [ … ], and the editor's reader stopped at the null: the
/// Deliverables pane showed the single-deliverable form and a Convert or a macro
/// removal rewrote the real list on publish.
/// </summary>
public class WorkflowPackageContentResponseTests
{
    private static readonly IReadOnlyDictionary<string, string> NoFiles = new Dictionary<string, string>(StringComparer.Ordinal);

    [Fact]
    public void A25Manifest_CarriesDeliverables_AndNoResultsKeyInEitherCasing()
    {
        var manifest = new WorkflowPackageManifest(
            "test/general", "v2026.10.10", 25,
            Nodes: new List<WorkflowNodeSpec> { new("assemble_note", "Assembling note", Aggregate: new List<string> { "node.a" }) },
            Deliverables: new List<WorkflowDeliverableSpec>
            {
                new("consult", "node.assemble_note", "Consultation note", Macros: new List<WorkflowDeliverableMacroSpec> { new("test", At: "append") })
            });

        var response = WorkflowPackageContentResponse.For(manifest, NoFiles);

        Assert.Equal("test/general", response.Name);
        Assert.Equal(25, response.SpecVersion);
        var json = response.Manifest;
        Assert.Equal(25, json.GetProperty("specVersion").GetInt32());
        var deliverable = Assert.Single(json.GetProperty("deliverables").EnumerateArray());
        Assert.Equal("Consultation note", deliverable.GetProperty("label").GetString());
        Assert.Equal("append", Assert.Single(deliverable.GetProperty("macros").EnumerateArray()).GetProperty("at").GetString());
        foreach (var absent in new[] { "results", "Results", "result", "Result", "Deliverables", "preludes", "data" })
        {
            Assert.False(json.TryGetProperty(absent, out _), absent);
        }
    }

    [Fact]
    public void A24Manifest_CarriesResults_AndNoDeliverablesKey()
    {
        var manifest = new WorkflowPackageManifest(
            "acct-x", "v2026.10.1", 24,
            Results: new List<WorkflowDeliverableSpec> { new("consult", "node:all", "Note") });

        var json = WorkflowPackageContentResponse.For(manifest, NoFiles).Manifest;

        Assert.Equal("consult", Assert.Single(json.GetProperty("results").EnumerateArray()).GetProperty("id").GetString());
        Assert.False(json.TryGetProperty("deliverables", out _));
        Assert.False(json.TryGetProperty("Results", out _));
    }

    [Fact]
    public void TheWireShape_IsTheRegistrys()
    {
        // The publisher writes registry manifests with the same options, so what
        // the editor loads reads back exactly as what it will publish.
        var manifest = new WorkflowPackageManifest("acct-x", "v2026.10.1", 25, Deliverables: new List<WorkflowDeliverableSpec> { new("consult", "node.all", "Note") });

        var wire = WorkflowPackageContentResponse.For(manifest, NoFiles).Manifest.GetRawText();
        var registry = JsonSerializer.Serialize(manifest, WorkflowPackageManifestJson.WriteOptions);

        Assert.Equal(registry, wire);
    }
}
