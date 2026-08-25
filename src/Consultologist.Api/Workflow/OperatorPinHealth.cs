using System.Net;
using Consultologist.Api.Auth;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Consultologist.Api.Workflow;

/// <summary>
/// GET Operator/PinHealth — the #384 report on demand, fresh each call, for
/// an account listed in Operators__AppUserIds. Reads only.
/// </summary>
public sealed class OperatorPinHealth
{
    private readonly IAccountAuthorizer _authorizer;
    private readonly PinHealthReporter _reporter;

    public OperatorPinHealth(IAccountAuthorizer authorizer, PinHealthReporter reporter)
    {
        _authorizer = authorizer;
        _reporter = reporter;
    }

    [Function("OperatorPinHealth")]
    public async Task<HttpResponseData> GetAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "Operator/PinHealth")] HttpRequestData req)
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

        var report = await _reporter.RunAsync(cancellationToken);
        var response = req.CreateResponse(HttpStatusCode.OK);
        FunctionCors.Apply(req, response);
        response.Headers.Add("Cache-Control", "no-store");
        await response.WriteAsJsonAsync(report, cancellationToken);
        return response;
    }
}
