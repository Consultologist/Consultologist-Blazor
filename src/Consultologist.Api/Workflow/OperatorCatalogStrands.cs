using System.Net;
using Consultologist.Api.Agents;
using Consultologist.Api.Auth;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;

namespace Consultologist.Api.Workflow;

/// <summary>
/// GET Operator/CatalogStrands?candidate=output-contracts@vYYYY.MM.N — the
/// #452 sweep: would bumping the catalog pin to this published version strand
/// any published package? Operator-gated like Operator/PinHealth. Reads only.
/// </summary>
public sealed class OperatorCatalogStrands
{
    private readonly IAccountAuthorizer _authorizer;
    private readonly CatalogStrandSweeper _sweeper;
    private readonly IConfiguration _configuration;

    public OperatorCatalogStrands(IAccountAuthorizer authorizer, CatalogStrandSweeper sweeper, IConfiguration configuration)
    {
        _authorizer = authorizer;
        _sweeper = sweeper;
        _configuration = configuration;
    }

    [Function("OperatorCatalogStrands")]
    public async Task<HttpResponseData> GetAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "Operator/CatalogStrands")] HttpRequestData req)
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

        var candidate = System.Web.HttpUtility.ParseQueryString(req.Url.Query)["candidate"];
        var problem = CatalogStrands.ValidateCandidate(candidate);
        if (problem != null)
        {
            return await ProblemAsync(req, HttpStatusCode.BadRequest, problem, cancellationToken);
        }

        var publicUri = _configuration["WorkflowPackages:PublicBlobServiceUri"]
            is { Length: > 0 } explicitUri
                ? explicitUri
                : StorageAccounts.DerivedUri(_configuration, StorageAccounts.PublicRole, "blob"); // #596
        if (string.IsNullOrWhiteSpace(publicUri))
        {
            return await ProblemAsync(req, HttpStatusCode.UnprocessableEntity, "The public registry is not configured, so no candidate catalog can be loaded.", cancellationToken);
        }

        OutputContractCatalog loaded;
        try
        {
            loaded = await OutputContractCatalog.LoadFromRegistryAsync(new Uri(publicUri), candidate!, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await ProblemAsync(req, HttpStatusCode.UnprocessableEntity, $"Candidate catalog {candidate} did not load: {ex.Message}", cancellationToken);
        }

        var report = await _sweeper.RunAsync(loaded, cancellationToken);
        var response = req.CreateResponse(HttpStatusCode.OK);
        FunctionCors.Apply(req, response);
        response.Headers.Add("Cache-Control", "no-store");
        await response.WriteAsJsonAsync(report, cancellationToken);
        return response;
    }

    private static async Task<HttpResponseData> ProblemAsync(HttpRequestData req, HttpStatusCode status, string error, CancellationToken cancellationToken)
    {
        var response = req.CreateResponse(status);
        FunctionCors.Apply(req, response);
        await response.WriteAsJsonAsync(new { error }, cancellationToken);
        return response;
    }
}
