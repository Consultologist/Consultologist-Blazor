using Bunit;
using Bunit.TestDoubles;

using Consultologist.Web.Services.Workflow;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

using NSubstitute;

using Xunit;

namespace Consultologist.Web.Tests;

/// <summary>
/// #766: `/` redirects a signed-in clinician to the Consults page (their real
/// landing); the account-status nudge that used to live here moved onto
/// Consults (see <see cref="ConsultsPageTests"/>).
/// </summary>
public class IndexPageTests : ClientRenderTestContext
{
    [Fact]
    public void SignedIn_RedirectsToConsults()
    {
        Render<Consultologist.Web.Pages.Index>();

        Assert.EndsWith("/consults", Services.GetRequiredService<NavigationManager>().Uri);
    }
}

/// <summary>
/// #685/#766: signed out, `/` stays the public marketing / verifiability
/// landing. Rendered in a not-authorized context, so it needs its own host.
/// </summary>
public class IndexSignedOutTests : BunitContext
{
    public IndexSignedOutTests()
    {
        Services.AddFluentUIComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
        AddAuthorization().SetNotAuthorized();

        var workflow = Substitute.For<IWorkflowEndpointService>();
        workflow.GetPublicChainAsync().Returns((PublicChainView?)null);
        Services.AddSingleton(workflow);
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AzureFunction:PublicRegistryBaseUrl"] = "https://consultpubcaeast.blob.core.windows.net" })
            .Build());
    }

    [Fact]
    public void SignedOut_ShowsTheLandingHeroAndLogIn()
    {
        var page = Render<Consultologist.Web.Pages.Index>();

        Assert.Contains("Consult drafting with traceable clinical context.", page.Markup);
        Assert.Contains("Log in", page.Markup);
    }
}
