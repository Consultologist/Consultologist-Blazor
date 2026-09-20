using System.Linq;
using System.Reflection;
using AngleSharp.Dom;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using NSubstitute;
using Xunit;

namespace Consultologist.Web.Tests;

/// <summary>
/// #777: the delivery-password field guides entry — a live counter toward the
/// 16-character minimum and a show/hide reveal of the value being typed.
/// </summary>
public class DeliveryPasswordProfileTests : ClientRenderTestContext
{
    private static AccountIdentity Entra() =>
        new("entra-external-id", "https://login.microsoftonline.com/x", "sub-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private void WithAccount() =>
        AccountService.GetCurrentAccountAsync().Returns(new AccountMeResponse(
            "user-1", "A Clinician", "clinician@example.com", "Active", Entra(), new[] { Entra() }));

    private static IElement Field(IRenderedComponent<Profile> page) => page.Find("fluent-text-field.delivery-password-input");
    private static IElement Button(IRenderedComponent<Profile> page, string label) =>
        page.FindAll("fluent-button").First(b => b.TextContent.Trim() == label);

    // The Immediate @bind is driven by the browser in production; in bUnit we set
    // the bound field directly and re-render, then assert what the markup shows.
    private static void SetInput(IRenderedComponent<Profile> page, string value)
    {
        typeof(Profile).GetField("deliveryPasswordInput", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(page.Instance, value);
        page.Render();
    }

    [Fact]
    public void TheReveal_TogglesTheFieldTypeAndItsLabel()
    {
        WithAccount();
        var page = Render<Profile>();

        Assert.Equal("password", Field(page).GetAttribute("type"));

        Button(page, "Show").Click();
        Assert.Equal("text", Field(page).GetAttribute("type"));

        Button(page, "Hide").Click();
        Assert.Equal("password", Field(page).GetAttribute("type"));
    }

    [Fact]
    public void TheCounter_TracksTowardSixteen_AndTheButtonEnablesAtSixteen()
    {
        WithAccount();
        var page = Render<Profile>();

        SetInput(page, "short");
        Assert.Contains("of 16 characters minimum", page.Find(".password-hint").TextContent);
        Assert.True(Button(page, "Set password").HasAttribute("disabled"));

        SetInput(page, new string('x', 16));
        Assert.Contains("Length looks good", page.Find(".password-hint").TextContent);
        Assert.False(Button(page, "Set password").HasAttribute("disabled"));
    }
}
