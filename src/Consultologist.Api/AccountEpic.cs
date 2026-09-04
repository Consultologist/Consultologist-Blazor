using System.Net;
using Consultologist.Api.Auth;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Consultologist.Api;

public sealed record EpicLinkRequest(string? IdToken);

public sealed record EpicLinkResponse(string FhirUser, string Issuer);

/// <summary>
/// #654 leg 1: bind the clinician's Epic identity to their account, from the
/// SMART panel. Unlike LinkedIn (a server-side redirect dance), the panel
/// does the SMART launch in the browser and already holds the id_token; it
/// POSTs that token here under the clinician's own Entra access_as_user
/// bearer (the #611 satellite pattern), so the caller is authenticated as
/// the clinician and the id_token proves Epic-account control. The Entra
/// bearer is the primary replay/CSRF defense — no server-issued state round
/// trip. The Epic identity is proof/display only: it never signs in, and it
/// does not activate the account (IdentityProviders.ActivatesAccount).
/// </summary>
public sealed class AccountEpic
{
    private readonly IAccountAuthorizer _authorizer;
    private readonly IEpicIdTokenValidator _validator;
    private readonly IAccountStore _accountStore;

    public AccountEpic(IAccountAuthorizer authorizer, IEpicIdTokenValidator validator, IAccountStore accountStore)
    {
        _authorizer = authorizer;
        _validator = validator;
        _accountStore = accountStore;
    }

    /// <summary>Refusal words on the flat error wire.</summary>
    internal const string InvalidToken = "epic-token-invalid";
    internal const string ConflictOtherUser = "epic-identity-linked-elsewhere";

    [Function("AccountEpicLink")]
    public async Task<HttpResponseData> LinkAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "Account/Epic/Link")] HttpRequestData req)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;

        if (string.Equals(req.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            var optionsResponse = req.CreateResponse(HttpStatusCode.OK);
            FunctionCors.Apply(req, optionsResponse);
            return optionsResponse;
        }

        var authorized = await _authorizer.AuthorizeWithUserAsync(req, cancellationToken);

        if (authorized == null)
        {
            return AccountAuthorizer.CreateUnauthorizedResponse(req);
        }

        // No IsActive gate: linking Epic is proof a Pending account may
        // establish, the LinkedIn-Start posture. (It does not itself
        // activate — that stays the LinkedIn/operator path.)
        EpicLinkRequest? request;
        try
        {
            request = await req.ReadFromJsonAsync<EpicLinkRequest>(cancellationToken);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            request = null;
        }

        if (string.IsNullOrWhiteSpace(request?.IdToken))
        {
            return await Error(req, HttpStatusCode.BadRequest, InvalidToken, cancellationToken);
        }

        var claims = await _validator.ValidateAsync(request.IdToken, cancellationToken);

        if (claims == null)
        {
            return await Error(req, HttpStatusCode.UnprocessableEntity, InvalidToken, cancellationToken);
        }

        var outcome = await _accountStore.LinkIdentityAsync(
            authorized.Account.AppUserId,
            IdentityProviders.Epic,
            claims.Issuer,
            claims.Subject,
            displayName: claims.FhirUser,
            email: null,
            pictureUrl: null,
            verifiedCategories: null,
            cancellationToken);

        if (outcome == IdentityLinkOutcome.ConflictOtherUser)
        {
            return await Error(req, HttpStatusCode.Conflict, ConflictOtherUser, cancellationToken);
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        FunctionCors.Apply(req, response);
        await response.WriteAsJsonAsync(
            new EpicLinkResponse(claims.FhirUser ?? claims.Subject, claims.Issuer), cancellationToken);
        return response;
    }

    /// <summary>
    /// Disconnect the caller's own Epic identity. Like LinkedIn's disconnect:
    /// no CanUseApp gate (undoing a wrong link must work at any status), and
    /// — because Epic never activated the account — removing it never demotes.
    /// The "options" verb is required: Account/Epic has no other handler for
    /// the preflight.
    /// </summary>
    [Function("AccountEpicDisconnect")]
    public async Task<HttpResponseData> DisconnectAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", "options", Route = "Account/Epic")] HttpRequestData req)
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

        await _accountStore.UnlinkIdentityAsync(account.AppUserId, IdentityProviders.Epic, cancellationToken);

        var response = req.CreateResponse(HttpStatusCode.NoContent);
        FunctionCors.Apply(req, response);
        return response;
    }

    private static async Task<HttpResponseData> Error(
        HttpRequestData req, HttpStatusCode status, string word, CancellationToken cancellationToken)
    {
        var response = req.CreateResponse(status);
        FunctionCors.Apply(req, response);
        await response.WriteAsJsonAsync(new { error = word }, cancellationToken);
        return response;
    }
}
