using Bunit;
using Consultologist.Web.Shared;
using Xunit;

namespace Consultologist.Web.Tests;

/// <summary>
/// #688: the icon-only theme toggle names itself for assistive tech and its
/// name reflects the current System/Light/Dark mode.
/// </summary>
public class HeaderTests : ClientRenderTestContext
{
    [Fact]
    public void TheThemeToggle_HasAnAccessibleNameReflectingTheMode()
    {
        var page = Render<Header>();

        var toggle = page.Find("fluent-button[aria-label*='Theme']");
        Assert.Contains("Theme:", toggle.GetAttribute("aria-label"));
    }

    [Fact]
    public void TheNav_HasAPublicHelpLink()
    {
        // #685: Help is public — present even when signed out.
        var page = Render<Header>();

        Assert.Contains(
            page.FindAll("fluent-nav-link, a").Select(a => a.TextContent.Trim()),
            text => text == "Help");
    }
}
