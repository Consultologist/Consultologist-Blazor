using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Auth;

/// <summary>
/// POST Operator/Accounts/{appUserId}/Close — account closure (#559,
/// storage-separation § 2.6), for an account listed in
/// Operators__AppUserIds. The FIRST mutating operator surface: the same
/// allowlist that has only ever gated reads now gates the one deleter of
/// what is never deleted, which is why the target must already be Disabled
/// (the #191 az write) — the two-step keeps a slip from being final.
/// </summary>
public sealed class OperatorAccounts
{
    private readonly IAccountAuthorizer _authorizer;
    private readonly AccountClosure _closure;
    private readonly ILogger<OperatorAccounts> _logger;

    public OperatorAccounts(IAccountAuthorizer authorizer, AccountClosure closure, ILogger<OperatorAccounts> logger)
    {
        _authorizer = authorizer;
        _closure = closure;
        _logger = logger;
    }

    [Function("OperatorAccountClose")]
    public async Task<HttpResponseData> CloseAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "Operator/Accounts/{appUserId}/Close")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        string appUserId)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;
        var account = await _authorizer.AuthorizeAsync(req, cancellationToken);

        if (account == null)
        {
            return AccountAuthorizer.CreateUnauthorizedResponse(req);
        }

        if (!AccountAuthorizer.CanUseApp(account) || !Operators.IsOperator(account))
        {
            return AccountAuthorizer.CreateForbiddenResponse(req);
        }

        var outcome = await _closure.CloseAsync(client, appUserId, account.AppUserId, cancellationToken);

        if (outcome.NotFound)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            FunctionCors.Apply(req, notFound);
            await notFound.WriteAsJsonAsync(new { error = "No such account." }, cancellationToken);
            return notFound;
        }

        if (outcome.Refusal != null)
        {
            var conflict = req.CreateResponse(HttpStatusCode.Conflict);
            FunctionCors.Apply(req, conflict);
            await conflict.WriteAsJsonAsync(new { error = outcome.Refusal }, cancellationToken);
            return conflict;
        }

        var closed = outcome.Closed!;
        var response = req.CreateResponse(HttpStatusCode.OK);
        FunctionCors.Apply(req, response);
        response.Headers.Add("Cache-Control", "no-store");
        await response.WriteAsJsonAsync(new
        {
            appUserId = closed.AppUserId,
            closedAtUtc = closed.ClosedAtUtc,
            alreadyClosed = outcome.AlreadyClosed,
            counts = new
            {
                jobs = closed.Jobs,
                blobs = closed.Blobs,
                packages = closed.Packages,
                links = closed.Links,
                responses = closed.Responses,
                settings = closed.Settings,
                identities = closed.Identities
            }
        }, cancellationToken);
        return response;
    }
}
