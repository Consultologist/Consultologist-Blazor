#if E2E
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace Consultologist.Web;

/// <summary>
/// #710: test-only authentication for the Playwright E2E harness. Compiled ONLY
/// under <c>-p:E2E=true</c> (the <c>#if E2E</c> guard) and physically absent
/// from any production build. It satisfies <c>[Authorize]</c>/<c>&lt;AuthorizeView&gt;</c>
/// and hands the endpoint services a dummy token so their per-call token
/// requests don't throw — Playwright mocks the API responses themselves.
/// </summary>
internal static class E2eAuth
{
    private const string Email = "e2e@example.com";

    internal sealed class StateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var identity = new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.Name, "E2E Clinician"),
                    new Claim(ClaimTypes.Email, Email),
                    new Claim("preferred_username", Email),
                },
                authenticationType: "e2e");

            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
        }
    }

    internal sealed class TokenProvider : IAccessTokenProvider
    {
        private static AccessTokenResult Success() =>
            new(
                AccessTokenResultStatus.Success,
                new AccessToken
                {
                    Value = "e2e-token",
                    Expires = DateTimeOffset.MaxValue,
                    GrantedScopes = Array.Empty<string>(),
                },
                interactiveRequestUrl: null,
                interactiveRequest: null);

        public ValueTask<AccessTokenResult> RequestAccessToken() => new(Success());

        public ValueTask<AccessTokenResult> RequestAccessToken(AccessTokenRequestOptions options) => new(Success());
    }
}
#endif
