using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using Consultologist.Web.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
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

    [Fact]
    public void TheNav_NeverShowsTheOperatorsLink()
    {
        // #733: the operator surface moved to the standalone admin app, so the
        // clinician header no longer carries the link (nor the Me read that
        // once gated it) for anyone.
        var page = Render<Header>();

        Assert.DoesNotContain(
            page.FindAll("fluent-nav-link, a").Select(a => a.TextContent.Trim()),
            text => text == "Operators");
    }

    [Fact]
    public void TheNav_SignedIn_OrdersTheWorkspaceLinksAndOmitsHome()
    {
        // #766: signed in, Home is dropped (the `/` landing redirects to
        // Consults) and the nav reads Consults | History | Editor | Help |
        // Profile, with the public Help link sitting between the workspace
        // links and Profile. (#788: the editor tab is "Editor".)
        var page = Render<Header>();

        Assert.Equal(
            new[] { "Consults", "History", "Editor", "Help", "Profile" },
            HeaderNav.Labels(page.Find("nav.top-nav")));
    }
}

/// <summary>
/// #766: the signed-out header — the marketing/verifiability audience. Rendered
/// in a not-authorized context (unlike <see cref="ClientRenderTestContext"/>,
/// which authorizes by default), so it needs its own minimal host.
/// </summary>
public class HeaderSignedOutTests : BunitContext
{
    public HeaderSignedOutTests()
    {
        Services.AddFluentUIComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
        AddAuthorization().SetNotAuthorized();
    }

    [Fact]
    public void TheNav_SignedOut_ShowsHomeThenHelpOnly()
    {
        // #685/#766: a visitor gets the public landing (Home) and the public
        // Help story — and none of the signed-in workspace links.
        var page = Render<Header>();

        Assert.Equal(new[] { "Home", "Help" }, HeaderNav.Labels(page.Find("nav.top-nav")));
    }
}

internal static class HeaderNav
{
    /// <summary>The visible nav-link labels, in document order, deduped so a
    /// FluentNavLink that renders a wrapper plus an inner anchor counts once.</summary>
    public static string[] Labels(IElement nav)
    {
        var seen = new HashSet<IElement>();
        var labels = new List<string>();
        foreach (var link in nav.QuerySelectorAll("fluent-nav-link, a"))
        {
            // Skip an anchor nested inside a fluent-nav-link we already counted.
            if (link.Closest("fluent-nav-link") is { } host && host != link && seen.Contains(host))
            {
                continue;
            }

            seen.Add(link);
            var text = link.TextContent.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                labels.Add(text);
            }
        }

        return labels.ToArray();
    }
}
