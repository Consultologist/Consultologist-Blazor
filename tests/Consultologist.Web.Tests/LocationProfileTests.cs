using Bunit;
using Bunit.TestDoubles;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using Consultologist.Web.Services.Locations;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>#515: the profile's Location card — empty until chosen, and the unchosen line says what is used.</summary>
public class LocationProfileTests : ClientRenderTestContext
{
    private static AccountIdentity Entra() =>
        new("entra-external-id", "https://login.microsoftonline.com/x", "sub-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static readonly ApiLocation West = new("ca-west", "Canada West", "https://west.ca.api.consultologist.ai/api");

    private FakeApiLocations WithLocations(string? chosen, params ApiLocation[] all)
    {
        AccountService.GetCurrentAccountAsync().Returns(new AccountMeResponse(
            "user-1", "A Clinician", "clinician@example.com", "Active", Entra(), new[] { Entra() }));
        var locations = new FakeApiLocations(all.Length == 0 ? null : all, chosen);
        Services.AddSingleton<IApiLocations>(locations);
        return locations;
    }

    private static string State(IRenderedComponent<Profile> page) => page.Find(".location-state").TextContent.Trim();

    [Fact]
    public void OneLocation_Unchosen_SaysItIsUsed_AndOffersIt()
    {
        WithLocations(null);

        var page = Render<Profile>();

        Assert.Equal("Not chosen — Canada East, the only location, is used", State(page));
        Assert.Equal("Use Canada East", page.Find(".location-choose").TextContent.Trim());
        Assert.Empty(page.FindAll(".location-clear"));
        Assert.Empty(page.FindAll(".location-answers"));
    }

    [Fact]
    public void TwoLocations_Unchosen_NeverChoosesItself()
    {
        WithLocations(null, FakeApiLocations.CanadaEast, West);

        var page = Render<Profile>();

        Assert.Equal("Not chosen — Canada East is used until you choose", State(page));
        Assert.Equal(2, page.FindAll(".location-choose").Count);
    }

    [Fact]
    public void Chosen_NamesTheLocationAndItsHost_AndWhatItAnswersAs()
    {
        WithLocations("ca-east");
        WorkflowService.GetEngineAsync().Returns(new EngineView("abc", null, null, "east.ca.api.consultologist.ai"));

        var page = Render<Profile>();

        Assert.Equal("Canada East — east.ca.api.consultologist.ai", State(page));
        page.WaitForAssertion(() => Assert.Equal("Answers as east.ca.api.consultologist.ai", page.Find(".location-answers").TextContent.Trim()));
        Assert.Empty(page.FindAll(".location-choose"));
        Assert.NotNull(page.Find(".location-clear"));
    }

    [Fact]
    public async Task Choosing_WritesTheDevice_RecordsItOnTheAccount_AndReloads()
    {
        var locations = WithLocations(null, FakeApiLocations.CanadaEast, West);
        var navigation = Services.GetRequiredService<BunitNavigationManager>();

        var page = Render<Profile>();
        await page.Find(".location-choose[data-location='ca-west']").ClickAsync(new());

        Assert.Equal("ca-west", locations.ChosenId);
        await AccountService.Received(1).SaveSettingAsync(LocationPreference.SettingKey, "ca-west", "text/plain");
        Assert.Contains(navigation.History, h => h.Uri == "/profile" && h.Options.ForceLoad);
    }

    [Fact]
    public async Task Clearing_ForgetsTheDevice_AndTheRecord()
    {
        var locations = WithLocations("ca-east");

        var page = Render<Profile>();
        await page.Find(".location-clear").ClickAsync(new());

        Assert.Null(locations.ChosenId);
        await AccountService.Received(1).DeleteSettingAsync(LocationPreference.SettingKey);
    }

    [Fact]
    public async Task ARefusedRecord_DoesNotUndoTheChoice()
    {
        // The device decides; the account's record is best effort.
        var locations = WithLocations(null);
        AccountService.SaveSettingAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromException(new HttpRequestException("offline")));

        var page = Render<Profile>();
        await page.Find(".location-choose").ClickAsync(new());

        Assert.Equal("ca-east", locations.ChosenId);
    }
}
