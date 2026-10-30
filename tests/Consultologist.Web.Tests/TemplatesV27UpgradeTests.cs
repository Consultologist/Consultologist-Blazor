using Consultologist.Web.Services.Workflow;

namespace Consultologist.Web.Tests;

/// <summary>v27 (#1236, design § 7): 26 → 27 is a version raise — the manifest's specVersion moves, nothing else; the button offers it once the bump lifts the ceiling (#1237).</summary>
public class TemplatesV27UpgradeTests
{
    [Fact]
    public void TheRaise_StampsTheVersion_AndRewritesNothing()
    {
        var at26 = EditorFixtures.V26();
        var files = new Dictionary<string, string>(at26.Files, StringComparer.Ordinal);

        var raised = WorkflowPackageVersionRaise.To(at26.Manifest.GetRawText(), files, 27);

        Assert.True(raised.Succeeded, string.Join(" | ", raised.Refusals));
        var before = System.Text.Json.Nodes.JsonNode.Parse(at26.Manifest.GetRawText())!.AsObject();
        var after = System.Text.Json.Nodes.JsonNode.Parse(raised.ManifestJson)!.AsObject();
        Assert.Equal(27, after["specVersion"]!.GetValue<int>());
        before.Remove("specVersion");
        after.Remove("specVersion");
        Assert.Equal(before.ToJsonString(), after.ToJsonString());
        Assert.Equal(files, raised.Files);
        Assert.Equal("Raised to specVersion 27; nothing rewritten — 27 permits what 26 refused.", Assert.Single(raised.Notices));
    }

    [Fact]
    public void TheRaise_RefusesAPackageAlreadyThere()
    {
        var at27 = EditorFixtures.V27Flow();

        var refused = WorkflowPackageVersionRaise.To(at27.Manifest.GetRawText(), at27.Files, 27);

        Assert.False(refused.Succeeded);
        Assert.Equal("The package is already at specVersion 27.", Assert.Single(refused.Refusals));
    }
}
