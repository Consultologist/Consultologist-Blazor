using Bunit;
using Consultologist.Web.Services.Workflow;
using Consultologist.Web.Shared.WorkflowEditor;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #453: one chip per tag any listed version declares; pressing one narrows
/// every group to the versions carrying it, and the selection stays listed.
/// </summary>
public class PickerTagFilterTests : ClientRenderTestContext
{
    private void WithRegistry(bool withFork = true)
    {
        WorkflowService.GetPublicChainAsync().Returns(new PublicChainView(
            new[]
            {
                new PublicPackageView("general", "v2026.09.1",
                    new List<string> { "v2026.08.1", "v2026.09.1" },
                    new Dictionary<string, int> { ["v2026.08.1"] = 8, ["v2026.09.1"] = 9 },
                    null,
                    new Dictionary<string, List<string>> { ["v2026.09.1"] = new() { "General", "new-patient" } }),
                new PublicPackageView("cardiology", "v2026.09.1",
                    new List<string> { "v2026.09.1" },
                    new Dictionary<string, int> { ["v2026.09.1"] = 9 },
                    null,
                    new Dictionary<string, List<string>> { ["v2026.09.1"] = new() { "cardiology" } })
            },
            null));

        if (withFork)
        {
            WorkflowService.GetMyPackagesAsync().Returns(new PublicPackageView("acct-1234567890ab", "v2026.09.2",
                new List<string> { "v2026.09.1", "v2026.09.2" },
                new Dictionary<string, int> { ["v2026.09.1"] = 9, ["v2026.09.2"] = 9 },
                null,
                new Dictionary<string, List<string>> { ["v2026.09.1"] = new() { "oncology" }, ["v2026.09.2"] = new() { "oncology", "general" } }));
        }
    }

    private static IReadOnlyList<string> Chips(IRenderedComponent<WorkflowPackagePicker> picker) =>
        picker.FindAll(".package-picker__tag").Select(chip => chip.TextContent.Trim()).ToList();

    private static IReadOnlyList<string> Options(IRenderedComponent<WorkflowPackagePicker> picker) =>
        picker.FindAll("option").Select(option => option.GetAttribute("value")!).ToList();

    private static IReadOnlyList<string> Groups(IRenderedComponent<WorkflowPackagePicker> picker) =>
        picker.FindAll("optgroup").Select(group => group.GetAttribute("label")!).ToList();

    private static void Press(IRenderedComponent<WorkflowPackagePicker> picker, string tag) =>
        picker.FindAll(".package-picker__tag").Single(chip => chip.TextContent.Trim() == tag).Click();

    [Fact]
    public void TheChips_AreEveryDeclaredTag_DistinctIgnoringCase_FirstSeenCasing()
    {
        WithRegistry();
        var picker = Render<WorkflowPackagePicker>(parameters => parameters.Add(p => p.WritesPin, false).Add(p => p.Selected, "general@latest"));

        // "General" (public, seen first) and "general" (fork) are one chip.
        Assert.Equal(new[] { "cardiology", "General", "new-patient", "oncology" }, Chips(picker));
        Assert.All(picker.FindAll(".package-picker__tag"), chip => Assert.Equal("false", chip.GetAttribute("aria-pressed")));
    }

    [Fact]
    public void PressingAChip_NarrowsEveryGroup_AndKeepsTheSelection()
    {
        WithRegistry();
        var picker = Render<WorkflowPackagePicker>(parameters => parameters.Add(p => p.WritesPin, false).Add(p => p.Selected, "general@v2026.08.1"));

        Press(picker, "oncology");

        // Only the fork carries oncology; general stays for the selection's
        // sake (its @latest goes — no general version carries the tag);
        // cardiology is gone.
        Assert.Equal(new[] { "general", "My fork (acct-1234567890ab)" }, Groups(picker));
        Assert.Equal(
            new[] { "general@v2026.08.1", "acct-1234567890ab@latest", "acct-1234567890ab@v2026.09.2", "acct-1234567890ab@v2026.09.1" },
            Options(picker));
        Assert.Equal("true", picker.FindAll(".package-picker__tag").Single(chip => chip.TextContent.Trim() == "oncology").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void AMatchIgnoresCase_AndLatestFollowsItsVersions()
    {
        WithRegistry();
        var picker = Render<WorkflowPackagePicker>(parameters => parameters.Add(p => p.WritesPin, false).Add(p => p.Selected, "cardiology@v2026.09.1"));

        Press(picker, "General");

        // general@v2026.09.1 declares "General", the fork's v2026.09.2 declares
        // "general": both match, each with its @latest; the fork's v2026.09.1
        // does not; cardiology stays only as the selection.
        Assert.Equal(
            new[]
            {
                "general@latest", "general@v2026.09.1",
                "cardiology@v2026.09.1",
                "acct-1234567890ab@latest", "acct-1234567890ab@v2026.09.2"
            },
            Options(picker));
    }

    [Fact]
    public void PressingTheActiveChip_ClearsTheFilter()
    {
        WithRegistry();
        var picker = Render<WorkflowPackagePicker>(parameters => parameters.Add(p => p.WritesPin, false).Add(p => p.Selected, "general@latest"));
        var all = Options(picker);

        Press(picker, "cardiology");
        Assert.NotEqual(all, Options(picker));
        Press(picker, "cardiology");

        Assert.Equal(all, Options(picker));
        Assert.All(picker.FindAll(".package-picker__tag"), chip => Assert.Equal("false", chip.GetAttribute("aria-pressed")));
    }

    [Fact]
    public void NoDeclaredTags_NoChipRow()
    {
        WorkflowService.GetPublicChainAsync().Returns(new PublicChainView(
            new[] { new PublicPackageView("general", "v2026.08.1", new List<string> { "v2026.08.1" }, new Dictionary<string, int> { ["v2026.08.1"] = 8 }) },
            null));
        var picker = Render<WorkflowPackagePicker>(parameters => parameters.Add(p => p.WritesPin, false).Add(p => p.Selected, "general@latest"));

        Assert.Empty(picker.FindAll(".package-picker__tags"));
    }
}
