using System.Net;
using Consultologist.Api.Auth;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Consultologist.Api;

public sealed record LinkedInStartResponse(string AuthorizationUrl);

public sealed class AccountLinkedIn
{
    private readonly IAccountAuthorizer _authorizer;
    private readonly ILinkedInLinkService _linkService;
    private readonly IAccountStore _accountStore;

    public AccountLinkedIn(
        IAccountAuthorizer authorizer,
        ILinkedInLinkService linkService,
        IAccountStore accountStore)
    {
        _authorizer = authorizer;
        _linkService = linkService;
        _accountStore = accountStore;
    }

    /// <summary>
    /// #195: disconnect the caller's own LinkedIn identity. Deferred at #133,
    /// leaving the operator to delete two table rows by hand.
    ///
    /// The "options" verb is not decoration: Account/LinkedIn has no other
    /// handler, so without it the browser's preflight 404s and the DELETE never
    /// leaves the page. Account/LinkedIn/Start is a different route and does
    /// not cover it.
    /// </summary>
    [Function("AccountLinkedInDisconnect")]
    public async Task<HttpResponseData> DisconnectAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", "options", Route = "Account/LinkedIn")] HttpRequestData req)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;

        if (string.Equals(req.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            var optionsResponse = req.CreateResponse(HttpStatusCode.OK);
            FunctionCors.Apply(req, optionsResponse);
            return optionsResponse;
        }

        var account = await _authorizer.AuthorizeAsync(req, cancellationToken);

        if (account == null)
        {
            return AccountAuthorizer.CreateUnauthorizedResponse(req);
        }

        // No CanUseApp gate, for the same reason linking has none: a user who
        // connected the wrong LinkedIn account must be able to undo it whatever
        // their status — and unlinking is the one action that CHANGES status,
        // so gating it on status would be circular.
        await _accountStore.UnlinkIdentityAsync(
            account.AppUserId, IdentityProviders.LinkedIn, cancellationToken);

        var response = req.CreateResponse(HttpStatusCode.NoContent);
        FunctionCors.Apply(req, response);
        return response;
    }

    [Function("AccountLinkedInStart")]
    public async Task<HttpResponseData> StartAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "Account/LinkedIn/Start")] HttpRequestData req)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;

        if (string.Equals(req.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            var optionsResponse = req.CreateResponse(HttpStatusCode.OK);
            FunctionCors.Apply(req, optionsResponse);
            return optionsResponse;
        }

        var account = await _authorizer.AuthorizeAsync(req, cancellationToken);

        if (account == null)
        {
            return AccountAuthorizer.CreateUnauthorizedResponse(req);
        }

        // No IsActive gate: linking LinkedIn is an input to the activation
        // decision (#133), so Pending accounts must be able to run it.

        // The callback's redirect target comes from here — the browser-set
        // Origin header checked against the CORS allow-list — never from a
        // client-supplied value, so it cannot be pointed elsewhere.
        var origin = req.Headers.TryGetValues("Origin", out var originValues)
            ? originValues.FirstOrDefault()
            : null;

        if (!FunctionCors.IsAllowedOrigin(origin))
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            FunctionCors.Apply(req, badRequest);
            await badRequest.WriteStringAsync("Origin not allowed.", cancellationToken);
            return badRequest;
        }

        var authorizationUrl = await _linkService.BuildAuthorizationUrlAsync(account.AppUserId, origin, cancellationToken);

        var response = req.CreateResponse(HttpStatusCode.OK);
        FunctionCors.Apply(req, response);
        await response.WriteAsJsonAsync(new LinkedInStartResponse(authorizationUrl), cancellationToken);
        return response;
    }

    [Function("AccountLinkedInCallback")]
    public async Task<HttpResponseData> CallbackAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "Account/LinkedIn/Callback")] HttpRequestData req)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;
        var (code, state, error) = ParseCallbackQuery(req.Url);

        var result = await _linkService.HandleCallbackAsync(code, state, error, cancellationToken);

        if (result.Outcome == LinkedInCallbackOutcome.StateInvalid || result.ReturnOrigin == null)
        {
            // No valid state means no trustworthy redirect target — answer in
            // place rather than sending the browser anywhere.
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteStringAsync(
                "This LinkedIn link session is invalid or has expired. Return to the app and try again.",
                cancellationToken);
            return badRequest;
        }

        // The browser arrives here by top-level navigation from LinkedIn, so
        // the response is a redirect back into the SPA, not JSON.
        var location = result.Outcome == LinkedInCallbackOutcome.Linked
            ? $"{result.ReturnOrigin}/profile?linkedin=connected"
            : $"{result.ReturnOrigin}/profile?linkedin=error&reason={ReasonSlug(result.Outcome)}";

        var response = req.CreateResponse(HttpStatusCode.Found);
        response.Headers.Add("Location", location);
        return response;
    }

    internal static string ReasonSlug(LinkedInCallbackOutcome outcome) => outcome switch
    {
        LinkedInCallbackOutcome.Denied => "denied",
        LinkedInCallbackOutcome.ExchangeFailed => "exchange-failed",
        LinkedInCallbackOutcome.TokenInvalid => "token-invalid",
        LinkedInCallbackOutcome.AlreadyLinked => "already-linked",
        _ => "unknown"
    };

    internal static (string? Code, string? State, string? Error) ParseCallbackQuery(Uri url)
    {
        var query = url.Query.TrimStart('?');
        string? code = null;
        string? state = null;
        string? error = null;

        foreach (var segment in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = segment.IndexOf('=');
            if (eq < 0) continue;

            var key = Uri.UnescapeDataString(segment[..eq]);
            var value = Uri.UnescapeDataString(segment[(eq + 1)..]);

            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (string.Equals(key, "code", StringComparison.OrdinalIgnoreCase))
            {
                code = value;
            }
            else if (string.Equals(key, "state", StringComparison.OrdinalIgnoreCase))
            {
                state = value;
            }
            else if (string.Equals(key, "error", StringComparison.OrdinalIgnoreCase))
            {
                error = value;
            }
        }

        return (code, state, error);
    }
}
