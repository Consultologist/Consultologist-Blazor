using Bunit;
using Consultologist.Web.Shared;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #412: the bar that tells an open tab a new build is waiting. It shows only
/// when the service says so, and the only way out of it is the user's click.
/// </summary>
public class UpdateBannerTests : ClientRenderTestContext
{
    [Fact]
    public void WithNoNewBuild_RendersNothing()
    {
        AppUpdate.UpdateReady.Returns(false);

        var banner = Render<UpdateBanner>();

        Assert.Empty(banner.FindAll(".update-banner"));
        Assert.Equal(string.Empty, banner.Markup.Trim());
    }

    [Fact]
    public void Rendering_StartsTheWatcher()
    {
        Render<UpdateBanner>();

        AppUpdate.Received(1).StartAsync();
    }

    [Fact]
    public void WithANewBuildWaiting_NamesItAndOffersReload()
    {
        AppUpdate.UpdateReady.Returns(true);

        var banner = Render<UpdateBanner>();

        Assert.Contains("A new version of Consultologist is ready", banner.Markup, StringComparison.Ordinal);
        Assert.Equal("Reload", banner.Find(".update-banner__reload").TextContent.Trim());
        AppUpdate.DidNotReceive().ReloadAsync();
    }

    [Fact]
    public void TheBarAppearsWhenTheWatcherReports()
    {
        AppUpdate.UpdateReady.Returns(false);
        var banner = Render<UpdateBanner>();
        Assert.Empty(banner.FindAll(".update-banner"));

        AppUpdate.UpdateReady.Returns(true);
        AppUpdate.UpdateReadyChanged += Raise.Event<Action>();

        banner.WaitForAssertion(() => Assert.Single(banner.FindAll(".update-banner")));
        AppUpdate.DidNotReceive().ReloadAsync();
    }

    [Fact]
    public void Reload_IsTheUsersClick()
    {
        AppUpdate.UpdateReady.Returns(true);
        var banner = Render<UpdateBanner>();

        banner.Find(".update-banner__reload").Click();

        AppUpdate.Received(1).ReloadAsync();
    }
}
