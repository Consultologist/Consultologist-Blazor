using Bunit;
using Consultologist.UI.Components;
using Microsoft.AspNetCore.Components;

namespace Consultologist.Web.Tests;

/// <summary>
/// #820: the shared arm→confirm button. These pin the behaviour every call site
/// depends on — resting vs armed labels, the back-out showing only while armed,
/// the two callbacks, the busy state, and the Native (plain &lt;button&gt;) path
/// History uses. The per-site suites (Templates/Profile/History) prove the
/// wiring; this proves the component.
/// </summary>
public class ConfirmButtonTests : ClientRenderTestContext
{
    [Fact]
    public void Resting_ShowsTheRestingLabel_AndNoBackOut()
    {
        var page = Render<ConfirmButton>(ps => ps
            .Add(p => p.Armed, false)
            .Add(p => p.RestingLabel, "Discard")
            .Add(p => p.ArmedLabel, "Confirm discard"));

        Assert.Contains("Discard", page.Markup);
        Assert.DoesNotContain("Confirm discard", page.Markup);
        Assert.Empty(page.FindAll("fluent-button").Where(b => b.TextContent.Trim() == "Cancel"));
    }

    [Fact]
    public void Armed_ShowsTheArmedLabel_AndTheBackOut()
    {
        var page = Render<ConfirmButton>(ps => ps
            .Add(p => p.Armed, true)
            .Add(p => p.RestingLabel, "Discard")
            .Add(p => p.ArmedLabel, "Confirm discard")
            .Add(p => p.BackOutLabel, "Cancel"));

        var labels = page.FindAll("fluent-button").Select(b => b.TextContent.Trim()).ToList();
        Assert.Contains("Confirm discard", labels);
        Assert.Contains("Cancel", labels);
    }

    [Fact]
    public async Task ClickingMain_RaisesOnClick_AndTheBackOut_RaisesOnBackOut()
    {
        var clicked = 0;
        var backedOut = 0;
        var page = Render<ConfirmButton>(ps => ps
            .Add(p => p.Armed, true)
            .Add(p => p.RestingLabel, "Discard")
            .Add(p => p.ArmedLabel, "Confirm discard")
            .Add(p => p.BackOutLabel, "Cancel")
            .Add(p => p.OnClick, EventCallback.Factory.Create(this, () => clicked++))
            .Add(p => p.OnBackOut, EventCallback.Factory.Create(this, () => backedOut++)));

        await page.FindAll("fluent-button").First(b => b.TextContent.Trim() == "Confirm discard").ClickAsync(new());
        await page.FindAll("fluent-button").First(b => b.TextContent.Trim() == "Cancel").ClickAsync(new());

        Assert.Equal(1, clicked);
        Assert.Equal(1, backedOut);
    }

    [Fact]
    public void Busy_ShowsTheBusyLabel_DisablesTheMain_AndHidesTheBackOut()
    {
        var page = Render<ConfirmButton>(ps => ps
            .Add(p => p.Native, true)
            .Add(p => p.Armed, true)          // armed, but busy wins
            .Add(p => p.Busy, true)
            .Add(p => p.RestingLabel, "cancel")
            .Add(p => p.ArmedLabel, "confirm cancel")
            .Add(p => p.BusyLabel, "cancelling…")
            .Add(p => p.BackOutLabel, "keep")
            .Add(p => p.MainClass, "cancel-run-button")
            .Add(p => p.BackOutClass, "cancel-run-keep-button"));

        var main = page.Find(".cancel-run-button");
        Assert.Equal("cancelling…", main.TextContent.Trim());
        Assert.True(main.HasAttribute("disabled"));
        Assert.Empty(page.FindAll(".cancel-run-keep-button")); // no back-out while busy
    }

    [Fact]
    public void Native_RendersPlainButtons_CarryingTheClasses()
    {
        var page = Render<ConfirmButton>(ps => ps
            .Add(p => p.Native, true)
            .Add(p => p.Armed, true)
            .Add(p => p.RestingLabel, "cancel")
            .Add(p => p.ArmedLabel, "confirm cancel")
            .Add(p => p.BackOutLabel, "keep")
            .Add(p => p.MainClass, "cancel-run-button")
            .Add(p => p.BackOutClass, "cancel-run-keep-button"));

        // A real <button>, not a <fluent-button>.
        Assert.Empty(page.FindAll("fluent-button"));
        Assert.Equal("confirm cancel", page.Find("button.cancel-run-button").TextContent.Trim());
        Assert.Equal("keep", page.Find("button.cancel-run-keep-button").TextContent.Trim());
    }
}
