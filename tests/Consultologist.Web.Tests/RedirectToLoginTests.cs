using Bunit;

using Consultologist.Web.Shared;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Consultologist.Web.Tests;

/// <summary>
/// #681: a protected route reached while signed out redirects to sign-in with a
/// returnUrl back to where the visitor was headed — no dead-end.
/// </summary>
public class RedirectToLoginTests : ClientRenderTestContext
{
    [Fact]
    public void Rendering_NavigatesToLogin_WithAReturnUrl()
    {
        var nav = Services.GetRequiredService<NavigationManager>();

        Render<RedirectToLogin>();

        Assert.Contains("authentication/login?returnUrl=", nav.Uri);
    }
}
