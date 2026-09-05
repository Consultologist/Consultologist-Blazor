using System.Net;
using Consultologist.Api.Auth;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Consultologist.Api;

public sealed record CernerLinkRequest(string? IdToken);

public sealed record CernerLinkResponse(string FhirUser, string Issuer);

/// <summary>
/// #662: bind the clinician's Cerner (Oracle Health) identity to their account,
/// from the SMART panel — the Epic link (#654, AccountEpic) generalized to a
/// second EHR. The panel does the SMART launch and holds the id_token; it POSTs
/// that token here under the clinician's own Entra access_as_user bearer (the
/// #611 satellite pattern), so the caller is authenticated as the clinician and
/// the id_token proves Cerner-account control. Proof/display only: it never
/// signs in, and it does not activate the account
/// (IdentityProviders.ActivatesAccount is LinkedIn-only).
/// </summary>
public sealed class AccountCerner
{
    private readonly IAccountAuthorizer _authorizer;
    private readonly ICernerIdTokenValidator _validator;
    private readonly IAccountStore _accountStore;

    public AccountCerner(IAccountAuthorizer authorizer, ICernerIdTokenValidator validator, IAccountStore accountStore)
    {
        _authorizer = authorizer;
        _validator = validator;
        _accountStore = accountStore;
    }

    /// <summary>Refusal words on the flat error wire.</summary>
    internal const string InvalidToken = "cerner-token-invalid";
    internal const string ConflictOtherUser = "cerner-identity-linked-elsewhere";

    [Function("AccountCernerLink")]
    public async Task<HttpResponseData> LinkAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "Account/Cerner/Link")] HttpRequestData req)
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

        // No IsActive gate: linking is proof a Pending account may establish,
        // the LinkedIn-Start posture. (It does not itself activate.)
        CernerLinkRequest? request;
        try
        {
            request = await req.ReadFromJsonAsync<CernerLinkRequest>(cancellationToken);
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
            IdentityProviders.Cerner,
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
            new CernerLinkResponse(claims.FhirUser ?? claims.Subject, claims.Issuer), cancellationToken);
        return response;
    }

    /// <summary>
    /// Disconnect the caller's own Cerner identity. No CanUseApp gate (undoing a
    /// wrong link must work at any status), and — because Cerner never activated
    /// the account — removing it never demotes. The "options" verb is required:
    /// Account/Cerner has no other handler for the preflight.
    /// </summary>
    [Function("AccountCernerDisconnect")]
    public async Task<HttpResponseData> DisconnectAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", "options", Route = "Account/Cerner")] HttpRequestData req)
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

        await _accountStore.UnlinkIdentityAsync(account.AppUserId, IdentityProviders.Cerner, cancellationToken);

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
