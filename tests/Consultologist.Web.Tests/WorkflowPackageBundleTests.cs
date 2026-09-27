using Consultologist.Web.Services.Workflow;
using Xunit;

namespace Consultologist.Web.Tests;

/// <summary>
/// #859: splitting an uploaded archive back into packages — the reverse of the
/// Profile multi-package export's per-package subfolders. Anchors on manifest.json
/// so a package name that contains '/' still groups correctly.
/// </summary>
public class WorkflowPackageBundleTests
{
    [Fact]
    public void AMultiPackageExport_SplitsIntoOnePackagePerSubfolder()
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["acct-a1b2/triage/manifest.json"] = "{\"name\":\"acct-a1b2/triage\"}",
            ["acct-a1b2/triage/prompts/draft.md"] = "triage draft",
            ["acct-a1b2/general/manifest.json"] = "{\"name\":\"acct-a1b2/general\"}",
            ["acct-a1b2/general/prompts/draft.md"] = "general draft",
            ["acct-a1b2/general/data/x.txt"] = "x",
        };

        var packages = WorkflowPackageBundle.Split(entries, out var strays);

        Assert.Empty(strays);
        Assert.Equal(2, packages.Count);

        var triage = packages.Single(p => p.Prefix == "acct-a1b2/triage/");
        Assert.Equal("{\"name\":\"acct-a1b2/triage\"}", triage.ManifestJson);
        Assert.Equal(new[] { "prompts/draft.md" }, triage.Files.Keys.ToArray());
        Assert.Equal("triage draft", triage.Files["prompts/draft.md"]);

        var general = packages.Single(p => p.Prefix == "acct-a1b2/general/");
        Assert.Equal(new[] { "data/x.txt", "prompts/draft.md" }, general.Files.Keys.OrderBy(k => k).ToArray());
    }

    [Fact]
    public void ASinglePackageZip_WithARootManifest_IsOnePackage()
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["manifest.json"] = "{\"name\":\"acct-a1b2/solo\"}",
            ["prompts/draft.md"] = "solo draft",
            ["schemas/concept-list.json"] = "{}",
        };

        var packages = WorkflowPackageBundle.Split(entries, out var strays);

        Assert.Empty(strays);
        var solo = Assert.Single(packages);
        Assert.Equal(string.Empty, solo.Prefix);
        Assert.Equal(new[] { "prompts/draft.md", "schemas/concept-list.json" }, solo.Files.Keys.OrderBy(k => k).ToArray());
        Assert.DoesNotContain("manifest.json", solo.Files.Keys);
    }

    [Fact]
    public void AMultiSegmentName_GroupsUnderItsManifest()
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["acct-a/oncology/breast/manifest.json"] = "{\"name\":\"acct-a/oncology/breast\"}",
            ["acct-a/oncology/breast/prompts/p.md"] = "p",
        };

        var package = Assert.Single(WorkflowPackageBundle.Split(entries, out _));

        Assert.Equal("acct-a/oncology/breast/", package.Prefix);
        Assert.Equal(new[] { "prompts/p.md" }, package.Files.Keys.ToArray());
    }

    [Fact]
    public void AFileUnderNoManifest_IsAStray()
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["acct-a/triage/manifest.json"] = "{\"name\":\"acct-a/triage\"}",
            ["acct-a/triage/prompts/draft.md"] = "d",
            ["loose/orphan.md"] = "no manifest here",
        };

        var packages = WorkflowPackageBundle.Split(entries, out var strays);

        Assert.Single(packages);
        Assert.Equal(new[] { "loose/orphan.md" }, strays.ToArray());
    }
}
