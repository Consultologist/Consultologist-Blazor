using Bunit;
using Bunit.TestDoubles;
using Consultologist.Admin.Services.Operators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using NSubstitute;

namespace Consultologist.Admin.Tests;

/// <summary>
/// #733: the minimum host the admin app's pages need to reach a rendered state
/// — the Consultologist.Web.Tests harness pared to what the admin surface uses
/// (FluentUI, loose JSInterop, an authorized user, the operator endpoint
/// substitute). The server gate is the real boundary; here the substitute
/// stands in for it, including the allowlist's OperatorAccessException.
/// </summary>
public abstract class AdminRenderTestContext : BunitContext
{
    protected IOperatorEndpointService OperatorService { get; } = Substitute.For<IOperatorEndpointService>();

    protected AdminRenderTestContext()
    {
        // Fluent components resolve LibraryConfiguration from DI and fail
        // activation without it.
        Services.AddFluentUIComponents();

        // FluentButton and friends import their .razor.js modules on first
        // render; strict mode would throw on the import.
        JSInterop.Mode = JSRuntimeMode.Loose;

        // The Usage page wraps its content in [Authorize]; an unauthorized
        // render yields nothing to assert against.
        AddAuthorization().SetAuthorized("operator@example.com");

        Services.AddSingleton(OperatorService);
    }
}
