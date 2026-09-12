using Bunit;
using Consultologist.Web.Shared;
using Xunit;

namespace Consultologist.Web.Tests;

/// <summary>
/// #688: a keyboard user can jump the header nav — the first focusable element
/// is a skip-link into a now-focusable main landmark.
/// </summary>
public class MainLayoutTests : ClientRenderTestContext
{
    [Fact]
    public void ASkipLink_PrecedesTheNav_AndTargetsAFocusableMain()
    {
        var page = Render<MainLayout>(parameters => parameters
            .Add(p => p.Body, "<h1>Page</h1>"));

        var skip = page.Find("a.skip-link");
        Assert.Equal("#main-content", skip.GetAttribute("href"));
        Assert.Equal("Skip to main content", skip.TextContent.Trim());

        var main = page.Find("main#main-content");
        Assert.Equal("-1", main.GetAttribute("tabindex"));

        // It has to come before the nav to be the first thing a keyboard hits.
        Assert.True(page.Markup.IndexOf("skip-link") < page.Markup.IndexOf("top-nav"));
    }
}
