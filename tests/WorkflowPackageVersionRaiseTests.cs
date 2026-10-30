using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// v27 (#1236, design § 7; in the library since the bump #1237): 26 → 27 is a
/// version raise — the manifest's specVersion moves, nothing else; the editor's
/// newest rung and <c>scripts/migrate-workflow-package.cs --to 27</c> share it.
/// </summary>
public class WorkflowPackageVersionRaiseTests
{
    private static string Json(WorkflowPackageManifest manifest) => JsonSerializer.Serialize(manifest, WorkflowPackageManifestJson.WriteOptions);

    [Fact]
    public void TheRaise_StampsTheVersion_AndRewritesNothing()
    {
        var (manifest, files) = V26Package.Minimal();
        var json = Json(manifest);

        var raised = WorkflowPackageVersionRaise.To(json, files, 27);

        Assert.True(raised.Succeeded, string.Join(" | ", raised.Refusals));
        var before = JsonNode.Parse(json)!.AsObject();
        var after = JsonNode.Parse(raised.ManifestJson)!.AsObject();
        Assert.Equal(27, after["specVersion"]!.GetValue<int>());
        before.Remove("specVersion");
        after.Remove("specVersion");
        Assert.Equal(before.ToJsonString(), after.ToJsonString());
        Assert.Equal(files, raised.Files);
        Assert.Equal("Raised to specVersion 27; nothing rewritten — 27 permits what 26 refused.", Assert.Single(raised.Notices));
    }

    [Fact]
    public void TheRaisedMinimal_ValidatesThroughThePublicEntry()
    {
        // #1237: the public entry accepts 27 — the raised 26 minimal is the 27 minimal (§ 7).
        var (manifest, files) = V26Package.Minimal();

        var raised = WorkflowPackageVersionRaise.To(Json(manifest), files, 27);
        var at27 = WorkflowPackageManifestJson.Read(raised.ManifestJson, "raised", WorkflowPackageValidator.AcceptedSpecVersions);

        Assert.Equal(27, at27.SpecVersion);
        Assert.Empty(WorkflowPackageValidator.Validate(at27, raised.Files, TestOutputContracts.CatalogSchemas, null, V25Package.Catalog()).Errors);
    }

    [Fact]
    public void TheRaise_RefusesAPackageAlreadyThere()
    {
        var (manifest, files) = V27Package.Minimal();

        var refused = WorkflowPackageVersionRaise.To(Json(manifest), files, 27);

        Assert.False(refused.Succeeded);
        Assert.Equal("The package is already at specVersion 27.", Assert.Single(refused.Refusals));
    }

    [Fact]
    public void TheRaise_RefusesWhatDoesNotParse()
    {
        var refused = WorkflowPackageVersionRaise.To("[not a manifest", new Dictionary<string, string>(), 27);

        Assert.False(refused.Succeeded);
        Assert.StartsWith("The manifest does not parse:", Assert.Single(refused.Refusals));
    }
}
