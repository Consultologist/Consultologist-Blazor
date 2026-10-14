using System.Net;
using System.Text.RegularExpressions;
using Consultologist.Api.Auth;
using Consultologist.PackageFormat;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Workflow;

/// <summary>
/// v25 (package-format-v25-design.md § 3.8, B6 #1005): <c>GET WorkflowFormats</c>
/// — the formats catalogue, read-only, for the editor: the list (id,
/// description, type, producer, parameters), each format file including its
/// schema in the editor's own write form, and the detail line's facts (the
/// agent's name and version, the operation, whether attested). The engine's
/// pinned catalogue by default; <c>?ref=formats@vYYYY.MM.N</c> reads the version
/// a package's stamp names. The agent yaml is never served: no authoring
/// decision depends on it, and the trailer is the engine's business, not the
/// author's. Authorized, CORS-applied, never cached.
/// </summary>
public sealed class WorkflowFormats
{
    private static readonly Regex ConcreteRef = new($"^{WorkflowFormatRegistry.Name}@v\\d{{4}}\\.\\d{{2}}\\.[1-9]\\d*$", RegexOptions.Compiled);

    private readonly IAccountAuthorizer _authorizer;
    private readonly WorkflowFormatCatalog _catalog;
    private readonly FormatCatalogResolver _resolver;
    private readonly ILogger<WorkflowFormats> _logger;

    public WorkflowFormats(
        IAccountAuthorizer authorizer,
        WorkflowFormatCatalog catalog,
        FormatCatalogResolver resolver,
        ILogger<WorkflowFormats> logger)
    {
        _authorizer = authorizer;
        _catalog = catalog;
        _resolver = resolver;
        _logger = logger;
    }

    [Function("WorkflowFormats")]
    public async Task<HttpResponseData> GetAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "WorkflowFormats")] HttpRequestData req)
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

        if (!AccountAuthorizer.CanUseApp(account))
        {
            return AccountAuthorizer.CreateForbiddenResponse(req);
        }

        var requested = System.Web.HttpUtility.ParseQueryString(req.Url.Query)["ref"];
        var catalog = _catalog;

        if (!string.IsNullOrWhiteSpace(requested))
        {
            if (!ConcreteRef.IsMatch(requested))
            {
                return await JsonAsync(req, HttpStatusCode.BadRequest,
                    new { error = $"ref must be a concrete formats catalogue reference ({WorkflowFormatRegistry.Name}@vYYYY.MM.N)." }, cancellationToken);
            }

            try
            {
                catalog = await _resolver.GetAsync(requested, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Formats catalogue could not be read. Ref={Ref}", requested);
                return await JsonAsync(req, HttpStatusCode.ServiceUnavailable,
                    new { error = $"Formats catalogue {requested} is unavailable." }, cancellationToken);
            }
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        FunctionCors.Apply(req, response);
        response.Headers.Add("Cache-Control", "no-store");
        await response.WriteAsJsonAsync(Describe(catalog), cancellationToken);
        return response;
    }

    /// <summary>The catalogue as the editor reads it — every format in id order, each with its file and the facts behind its detail line; the custom producer's agent, never its trailer.</summary>
    internal static WorkflowFormatsResponse Describe(WorkflowFormatCatalog catalog) => new(
        catalog.Version,
        catalog.ResolvedRef,
        catalog.Formats.Values
            .OrderBy(format => format.Id, StringComparer.Ordinal)
            .Select(format => new WorkflowFormatEntryResponse(
                format.Id,
                format.Spec.Description,
                format.Spec.Type,
                format.Spec.Producer,
                format.Spec.AllowsOutputMacros,
                format.Spec.Values,
                format.Spec.Parameters?.ToDictionary(
                    pair => pair.Key,
                    pair => new WorkflowFormatParameterResponse(pair.Value.Kind, pair.Value.Format),
                    StringComparer.Ordinal),
                format.Spec.Schema,
                WorkflowFormatSpecJson.ToJson(format.Spec),
                WorkflowFormatSpecJson.Sha256Hex(format.CanonicalJson),
                format.Entry.AgentName,
                format.Entry.AgentVersion,
                format.Entry.Op,
                string.Equals(format.Spec.Producer, WorkflowFormatProducers.Attested, StringComparison.Ordinal)))
            .ToList(),
        new WorkflowFormatCustomResponse(catalog.CustomProducer.AgentName, catalog.CustomProducer.AgentVersion));

    private static async Task<HttpResponseData> JsonAsync<T>(HttpRequestData req, HttpStatusCode status, T payload, CancellationToken cancellationToken)
    {
        var response = req.CreateResponse(status);
        FunctionCors.Apply(req, response);
        await response.WriteAsJsonAsync(payload, cancellationToken);
        return response;
    }
}
