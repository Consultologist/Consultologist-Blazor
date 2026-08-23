using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #432, v9 § 4: a package may declare a title and a description. The editor
/// says up front that a fork does not carry them, and (from commit 5) authors
/// them in a Package pane.
/// </summary>
public class TemplatesPackageMetadataTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse fixture)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture);
        return Render<Templates>();
    }

    private static IReadOnlyList<string> ForeignNoticeBullets(IRenderedComponent<Templates> page) =>
        page.FindAll(".fluent-messagebar-message li").Select(item => item.TextContent.Trim()).ToList();

    [Fact]
    public void TheForeignPackageNotice_SaysTheTitleIsNotCarried_OnlyWhenThereIsOne()
    {
        var titled = RenderEditor(EditorFixtures.WithTitle(EditorFixtures.NotMine(), "Breast oncology consults"));
        Assert.Contains(ForeignNoticeBullets(titled), bullet => bullet.StartsWith("Its title and description are not carried over", StringComparison.Ordinal));

        var plain = RenderEditor(EditorFixtures.NotMine());
        Assert.DoesNotContain(ForeignNoticeBullets(plain), bullet => bullet.Contains("not carried over", StringComparison.Ordinal));
    }
}
