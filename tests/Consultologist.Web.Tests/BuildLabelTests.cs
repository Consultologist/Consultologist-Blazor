using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using Consultologist.Web.Services.AppUpdate;
using Consultologist.Web.Services.Workflow;
using Consultologist.Web.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #412: which build a tab is running. The footer carries the short sha on
/// every page; Profile carries the full client commit beside the engine's.
/// </summary>
public class BuildLabelTests : ClientRenderTestContext
{
    private const string Sha = "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678";

    private void WithBuild(string? commit)
    {
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureFunction:PublicRegistryBaseUrl"] = "https://consultpubcaeast.blob.core.windows.net",
                ["Build:Commit"] = commit
            })
            .Build());
    }

    private void WithAccount()
    {
        var entra = new AccountIdentity("entra-external-id", "https://login.microsoftonline.com/x", "sub-1",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        AccountService.GetCurrentAccountAsync().Returns(new AccountMeResponse(
            "user-1", "A Clinician", "clinician@example.com", "Active", entra, new[] { entra }));
    }

    [Fact]
    public void Footer_ShowsTheShortSha()
    {
        WithBuild(Sha);

        var label = Render<Footer>().Find(".build-label");

        Assert.Equal("Build a1b2c3d", label.TextContent.Trim());
        Assert.Equal(Sha, label.GetAttribute("title"));
    }

    [Fact]
    public void Footer_NamesAnUnstampedBuild()
    {
        WithBuild(null);

        Assert.Equal("Build local", Render<Footer>().Find(".build-label").TextContent.Trim());
    }

    [Fact]
    public void Profile_ShowsClientAndEngineCommits()
    {
        WithBuild(Sha);
        WithAccount();
        WorkflowService.GetEngineAsync().Returns(new EngineView("0123abc", "v2026.08.6", "v2026.08.6"));

        var page = Render<Profile>();

        page.WaitForAssertion(() =>
        {
            Assert.Equal(Sha, page.Find(".build-client").TextContent.Trim());
            Assert.Equal("0123abc", page.Find(".build-engine").TextContent.Trim());
        });
    }

    [Fact]
    public void Profile_SaysWhenTheEngineIsUnreachable()
    {
        WithBuild(null);
        WithAccount();
        WorkflowService.GetEngineAsync().Returns((EngineView?)null);

        var page = Render<Profile>();

        page.WaitForAssertion(() =>
        {
            Assert.Equal("Unstamped (local build)", page.Find(".build-client").TextContent.Trim());
            Assert.Equal("Unavailable", page.Find(".build-engine").TextContent.Trim());
        });
    }

    [Fact]
    public void ShortSha_IsSevenCharacters_AndNeverThrowsOnAShortValue()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Build:Commit"] = " abc " })
            .Build();

        Assert.Equal("abc", BuildLabel.Short(config));
        Assert.Equal("abc", BuildLabel.Describe(config));
    }
}
