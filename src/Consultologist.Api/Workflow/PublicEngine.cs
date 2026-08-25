using System.Net;
using Consultologist.Api.Agents;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Consultologist.Api.Workflow;

/// <summary>
/// GET Public/Engine — the deployed engine attests its build (#449): commit,
/// catalog ref, format registry version, spec versions, Scriban. Anonymous and
/// open-CORS like Public/Chain; the facts are fixed for the life of the
/// process, so they are described once. Consumers: the content repos' CI
/// (validate against this commit, not main) and the operator's strand check.
/// </summary>
public sealed class PublicEngine
{
    private readonly EngineAttestationResponse _attestation;

    public PublicEngine(EngineAttestationResponse attestation)
    {
        // One instance for the process (Program.cs): what this serves is what
        // the starter stamps on every record (#398).
        _attestation = attestation;
    }

    [Function("PublicEngine")]
    public async Task<HttpResponseData> GetEngineAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "Public/Engine")] HttpRequestData req)
    {
        if (string.Equals(req.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            var optionsResponse = req.CreateResponse(HttpStatusCode.NoContent);
            FunctionCors.ApplyPublic(optionsResponse);
            return optionsResponse;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        FunctionCors.ApplyPublic(response);
        response.Headers.Add("Cache-Control", "public, max-age=60");
        await response.WriteAsJsonAsync(_attestation, req.FunctionContext.CancellationToken);
        return response;
    }
}
