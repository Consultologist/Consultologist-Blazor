using Consultologist.Web.Services.Locations;
using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #515: the location rule — empty until chosen, the first listed used until
/// then, and every URL built on the choice.
/// </summary>
public class ApiLocationsTests
{
    private static IConfiguration Config(params (string id, string name, string apiBase)[] locations)
    {
        var values = new Dictionary<string, string?>();
        for (var i = 0; i < locations.Length; i++)
        {
            values[$"Locations:{i}:Id"] = locations[i].id;
            values[$"Locations:{i}:Name"] = locations[i].name;
            values[$"Locations:{i}:ApiBase"] = locations[i].apiBase;
        }
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static readonly (string, string, string) East = ("ca-east", "Canada East", "https://east.ca.api.consultologist.ai/api/");
    private static readonly (string, string, string) West = ("ca-west", "Canada West", "https://west.ca.api.consultologist.ai/api");

    private static ApiLocations Locations(string? stored, params (string, string, string)[] list) =>
        new(Config(list), Substitute.For<IJSRuntime>(), stored);

    [Fact]
    public void Unchosen_UsesTheFirstListed_AndSaysItIsNotChosen()
    {
        var locations = Locations(null, East, West);

        Assert.Null(locations.Chosen);
        Assert.Equal("ca-east", locations.Current.Id);
        Assert.Equal("east.ca.api.consultologist.ai", locations.Current.Host);
    }

    [Fact]
    public void AStoredChoice_IsTheCurrentLocation()
    {
        var locations = Locations("ca-west", East, West);

        Assert.Equal("ca-west", locations.Chosen?.Id);
        Assert.Equal("https://west.ca.api.consultologist.ai/api", locations.ApiBase);
    }

    [Theory]
    [InlineData("eu-west")]
    [InlineData("")]
    [InlineData("  ")]
    public void AStoredIdTheListNoLongerHas_ReadsAsUnchosen(string stored)
    {
        var locations = Locations(stored, East);

        Assert.Null(locations.Chosen);
        Assert.Equal("ca-east", locations.Current.Id);
    }

    [Fact]
    public void Urls_AreBuiltOnTheBase_WithOneSlash()
    {
        var locations = Locations(null, East);

        Assert.Equal("https://east.ca.api.consultologist.ai/api", locations.ApiBase);
        Assert.Equal("https://east.ca.api.consultologist.ai/api/Account/Me", locations.Url(ApiRoutes.AccountMe));
        Assert.Equal("https://east.ca.api.consultologist.ai/api/Public/Engine", locations.Url("/Public/Engine"));
    }

    [Fact]
    public async Task Choosing_WritesTheDevice_AndClearingForgets()
    {
        var js = Substitute.For<IJSRuntime>();
        var locations = new ApiLocations(Config(East, West), js, null);

        await locations.ChooseAsync("ca-west");
        Assert.Equal("ca-west", locations.Chosen?.Id);
        await js.Received(1).InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>(
            "localStorage.setItem", Arg.Is<object?[]>(a => (string)a[0]! == ApiLocations.StorageKey && (string)a[1]! == "ca-west"));

        await locations.ClearAsync();
        Assert.Null(locations.Chosen);
        Assert.Equal("ca-east", locations.Current.Id);
    }

    [Fact]
    public async Task ChoosingAnUnknownLocation_IsRefused()
    {
        var locations = Locations(null, East);

        await Assert.ThrowsAsync<ArgumentException>(() => locations.ChooseAsync("mars"));
    }

    [Fact]
    public void NoLocationConfigured_IsSaidPlainly()
    {
        var locations = Locations(null);

        Assert.Empty(locations.All);
        Assert.Throws<InvalidOperationException>(() => locations.Current);
    }

    [Fact]
    public void AnEntryWithoutAnIdOrBase_IsDropped()
    {
        var locations = Locations(null, ("", "Nowhere", "https://x.example/api"), ("ca-east", "Canada East", ""), East);

        Assert.Single(locations.All);
    }
}
