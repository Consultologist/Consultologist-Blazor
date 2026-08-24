using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using Consultologist.Web.Services.Workflow;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #411 PR 2. Choosing a package in the editor used to write
/// <c>consult.workflowPackage</c>, so looking at general pointed your consults
/// at general — and Publish then built a version of your own package from it.
/// Three accidental publishes on 2026-08-19 came through that door.
///
/// Consults keeps the pin-writing picker; that page is where choosing what runs
/// belongs. These pin the split.
/// </summary>
public class EditingIsNotPinningTests : ClientRenderTestContext
{
    private const string PinKey = "consult.workflowPackage";
    private const string MyFork = "acct-1234567890ab";

    /// <summary>
    /// Every existing test renders the picker with no options at all, because
    /// the base context returns null for both listings — a disabled select
    /// cannot be changed. Selection tests have to supply a registry first.
    /// </summary>
    private void WithRegistry()
    {
        WorkflowService.GetPublicChainAsync().Returns(new PublicChainView(
            new[]
            {
                new PublicPackageView("general", "v2026.08.1", new List<string> { "v2026.08.1" },
                    new Dictionary<string, int> { ["v2026.08.1"] = 8 })
            },
            null));

        WorkflowService.GetMyPackagesAsync().Returns(new[]
        {
            new PublicPackageView(
                MyFork, "v2026.07.1", new List<string> { "v2026.07.1" },
                new Dictionary<string, int> { ["v2026.07.1"] = 7 })
        });
    }

    private static void Choose(IRenderedComponent<Templates> page, string packageRef) =>
        page.Find("select[aria-label='Workflow package']").Change(packageRef);

    [Fact]
    public void ChoosingAPackageInTheEditor_DoesNotChangeWhatYourConsultsRun()
    {
        // The defect, in one assertion.
        WithRegistry();
        WorkflowService.GetCurrentPackageContentAsync(Arg.Any<string?>()).Returns(EditorFixtures.V7());
        var page = Render<Templates>();

        Choose(page, "general@v2026.08.1");

        AccountService.DidNotReceive().SaveSettingAsync(PinKey, Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public void ChoosingAPackageInTheEditor_LoadsThatPackage()
    {
        // The other half: it must still do the thing it appears to do.
        WithRegistry();
        WorkflowService.GetCurrentPackageContentAsync(Arg.Any<string?>()).Returns(EditorFixtures.V7());
        var page = Render<Templates>();

        Choose(page, "general@v2026.08.1");

        WorkflowService.Received().GetCurrentPackageContentAsync("general@v2026.08.1");
    }

    [Fact]
    public void OpeningTheEditor_AsksForThePin()
    {
        // Null means "whatever is pinned", which is the landing when this
        // browser has not been used to edit something else.
        WorkflowService.GetCurrentPackageContentAsync(Arg.Any<string?>()).Returns(EditorFixtures.V7());

        Render<Templates>();

        WorkflowService.Received().GetCurrentPackageContentAsync(null);
    }

    [Fact]
    public void APackageRememberedFromLastTime_IsWhatOpens()
    {
        // Drafts are keyed per package ref, so landing back on the pin would
        // leave a draft against another package invisible until it was chosen
        // again. Remembering keeps the two together.
        JSInterop.Setup<string?>("localStorage.getItem", "workflow-editor-package")
            .SetResult("general@v2026.08.1");
        WorkflowService.GetCurrentPackageContentAsync(Arg.Any<string?>()).Returns(EditorFixtures.V7());

        Render<Templates>();

        WorkflowService.Received().GetCurrentPackageContentAsync("general@v2026.08.1");
    }

    [Fact]
    public void ARememberedPackageThatNoLongerLoads_FallsBackToThePinAndSaysSo()
    {
        // A pruned version, or a memory older than the registry. A dead editor
        // would be the worst answer available.
        JSInterop.Setup<string?>("localStorage.getItem", "workflow-editor-package")
            .SetResult("general@v2026.01.1");
        WorkflowService.GetCurrentPackageContentAsync("general@v2026.01.1")
            .Returns<Task<WorkflowPackageContentResponse>>(_ => throw new HttpRequestException("NotFound"));
        WorkflowService.GetCurrentPackageContentAsync(null).Returns(EditorFixtures.V7());

        var page = Render<Templates>();

        Assert.NotEmpty(page.FindAll(".editor-bar"));
        Assert.Contains("Could not open general@v2026.01.1", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EditingSomethingOtherThanThePin_NamesWhatTheConsultsRun()
    {
        // Once the two can differ, not showing the pin hides the fact the whole
        // split exists to make visible.
        AccountService.GetSettingAsync(PinKey)
            .Returns(new AccountSettingResponse(PinKey, $"{MyFork}@v2026.08.16", "text/plain", DateTimeOffset.UnixEpoch));
        WorkflowService.GetCurrentPackageContentAsync(Arg.Any<string?>())
            .Returns(EditorFixtures.NotMine());

        var page = Render<Templates>();

        // Queried by its own class, not by the phrase: the page header also
        // says "what your consults run", so a markup substring matches the
        // furniture whether or not the line rendered.
        Assert.Equal($"your consults run {MyFork}@v2026.08.16", page.Find(".editor-bar__pin").TextContent.Trim());
    }

    [Fact]
    public void EditingExactlyWhatIsPinned_SaysNothingAboutIt()
    {
        // The ordinary case. A line shown every visit is one nobody reads.
        var mine = EditorFixtures.V7();
        AccountService.GetSettingAsync(PinKey)
            .Returns(new AccountSettingResponse(PinKey, mine.Ref, "text/plain", DateTimeOffset.UnixEpoch));
        WorkflowService.GetCurrentPackageContentAsync(Arg.Any<string?>()).Returns(mine);

        var page = Render<Templates>();

        Assert.NotEmpty(page.FindAll(".editor-bar"));
        Assert.Empty(page.FindAll(".editor-bar__pin"));
    }
}
