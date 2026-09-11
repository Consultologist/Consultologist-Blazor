using Bunit;
using Consultologist.Web.Shared;
using Xunit;

namespace Consultologist.Web.Tests;

/// <summary>
/// #687: the app's one loading indicator. role="status" gives the spinner an
/// accessible name so a screen reader speaks the message when it appears — the
/// FluentProgressRing alone announces nothing.
/// </summary>
public class LoadingStateTests : ClientRenderTestContext
{
    [Fact]
    public void ItIsAPoliteStatusRegion_CarryingTheMessage()
    {
        var cut = Render<LoadingState>(parameters => parameters
            .AddChildContent("Loading job history..."));

        var region = cut.Find(".loading-state");
        Assert.Equal("status", region.GetAttribute("role"));
        Assert.Contains("Loading job history...", region.TextContent);
    }
}
