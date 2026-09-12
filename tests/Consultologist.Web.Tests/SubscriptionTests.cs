using System;
using System.Threading.Tasks;
using Bunit;
using Consultologist.Web.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Consultologist.Web.Tests;

/// <summary>
/// #669/#691: the Microsoft Marketplace landing page. It posts the single-use
/// ?token= to the backend to resolve and activate the subscription — success,
/// failure, and the no-token idle state each show their own message.
/// </summary>
public class SubscriptionTests : ClientRenderTestContext
{
    private void ArriveWithToken(string? token)
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(token == null ? "subscription" : $"subscription?token={token}");
    }

    [Fact]
    public void WithNoToken_ExplainsHowToArrive_AndCallsNothing()
    {
        ArriveWithToken(null);

        var page = Render<Subscription>();

        Assert.Contains("completes a Microsoft Marketplace purchase", page.Markup);
        AccountService.DidNotReceive().ResolveMarketplaceAsync(Arg.Any<string>());
    }

    [Fact]
    public void WithAToken_ResolvesAndShowsActive()
    {
        ArriveWithToken("mp-token-123");
        AccountService.ResolveMarketplaceAsync("mp-token-123").Returns(Task.CompletedTask);

        var page = Render<Subscription>();

        Assert.Contains("subscription is active", page.Markup);
        AccountService.Received(1).ResolveMarketplaceAsync("mp-token-123");
    }

    [Fact]
    public void WhenResolveFails_ShowsTheError()
    {
        ArriveWithToken("mp-token-bad");
        AccountService.ResolveMarketplaceAsync(Arg.Any<string>())
            .Returns(Task.FromException(new InvalidOperationException("Offer not found for this account.")));

        var page = Render<Subscription>();

        Assert.Contains("Offer not found for this account.", page.Markup);
    }
}
