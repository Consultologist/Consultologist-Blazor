using System.Net;
using System.Text.Json;
using Azure;
using Consultologist.Api.Auth;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Workflow;

public sealed class WorkflowPackages
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // Fork manifests are immutable; spec versions read once hold for the
    // process lifetime (the Mine endpoint's per-version reads).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int?> SpecVersionCache = new(StringComparer.Ordinal);

    private readonly IWorkflowPackageStore _packageStore;
    private readonly IWorkflowPackagePinResolver _pinResolver;
    private readonly WorkflowPackagePublisher _publisher;
    private readonly WorkflowPackageLineageResolver _lineage;
    private readonly WorkflowPackageBlobContainerFactory _containerFactory;
    private readonly IAccountAuthorizer _authorizer;
    private readonly ILogger<WorkflowPackages> _logger;

    public WorkflowPackages(
        IWorkflowPackageStore packageStore,
        IWorkflowPackagePinResolver pinResolver,
        WorkflowPackagePublisher publisher,
        WorkflowPackageLineageResolver lineage,
        WorkflowPackageBlobContainerFactory containerFactory,
        IAccountAuthorizer authorizer,
        ILogger<WorkflowPackages> logger)
    {
        _packageStore = packageStore;
        _pinResolver = pinResolver;
        _publisher = publisher;
        _lineage = lineage;
        _containerFactory = containerFactory;
        _authorizer = authorizer;
        _logger = logger;
    }

    [Function("WorkflowPackageCurrent")]
    public async Task<HttpResponseData> GetCurrentAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "WorkflowPackages/Current")] HttpRequestData req)
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

        if (!AccountAuthorizer.IsActive(account))
        {
            return AccountAuthorizer.CreateForbiddenResponse(req);
        }

        var packageRef = await _pinResolver.ResolvePinAsync(account.AppUserId, cancellationToken);

        WorkflowPackage package;
        try
        {
            package = await _packageStore.ResolveAsync(packageRef, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Workflow package resolution failed. Pin={Pin}", packageRef);
            var errorResponse = req.CreateResponse(HttpStatusCode.ServiceUnavailable);
            FunctionCors.Apply(req, errorResponse);
            await errorResponse.WriteAsJsonAsync(new { error = "Workflow package registry is unavailable." }, cancellationToken);
            return errorResponse;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        FunctionCors.Apply(req, response);
        await response.WriteAsJsonAsync(Describe(package), cancellationToken);

        return response;
    }

    /// <summary>
    /// The current-package projection the consult setup form runs on: blocks
    /// (the sections it will generate) plus, for v7 packages, the declared
    /// inputs to render fields for and the deliverables to group by. Inputs and
    /// Results are null on v5/v6 — the client's frozen single-draft path.
    /// </summary>
    internal static WorkflowPackageResponse Describe(WorkflowPackage package) =>
        new(
            package.Manifest.Name,
            package.Manifest.Version,
            package.Manifest.SpecVersion,
            WorkflowPackageBlocks.Resolve(package)
                .Select(block => new WorkflowPackageBlockResponse(block.Id, block.Name))
                .ToList(),
            package.Manifest.Inputs?
                .Select(input => new WorkflowPackageInputResponse(
                    input.Id,
                    input.Label,
                    input.Required,
                    // Only a declared type travels: text is the default, so a
                    // v7 package's response is byte-identical to before.
                    input.Type,
                    input.Values))
                .ToList(),
            package.Results?
                .Select(result => new WorkflowPackageResultResponse(result.Id, result.Label))
                .ToList());

    [Function("WorkflowPackageMine")]
    public async Task<HttpResponseData> GetMineAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "WorkflowPackages/Mine")] HttpRequestData req)
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

        if (!AccountAuthorizer.IsActive(account))
        {
            return AccountAuthorizer.CreateForbiddenResponse(req);
        }

        // The selector's "My fork" group: the account package's versions from
        // the private registry (#134). No forks published yet is a normal,
        // empty answer — including a registry whose container does not exist.
        var name = WorkflowPackageNaming.ForAccount(account.AppUserId);
        var container = _containerFactory.GetContainer();
        var blobNames = new List<string>();
        string? latestPointerJson = null;
        var specVersions = new Dictionary<string, int>(StringComparer.Ordinal);

        try
        {
            await foreach (var blob in container.GetBlobsAsync(prefix: $"{name}/", cancellationToken: cancellationToken))
            {
                blobNames.Add(blob.Name);
            }

            var latestPath = $"{name}/latest.json";
            if (blobNames.Contains(latestPath))
            {
                var download = await container.GetBlobClient(latestPath).DownloadContentAsync(cancellationToken);
                latestPointerJson = download.Value.Content.ToString();
            }

            // Per-version spec, so the selector can mark unsupported history.
            foreach (var manifestPath in blobNames.Where(n => n.Split('/') is { Length: 3 } parts && parts[2] == "manifest.json"))
            {
                if (!SpecVersionCache.TryGetValue(manifestPath, out var spec))
                {
                    var manifest = await container.GetBlobClient(manifestPath).DownloadContentAsync(cancellationToken);
                    spec = AccountPackageListing.ReadSpecVersion(manifest.Value.Content.ToString());
                    SpecVersionCache[manifestPath] = spec;
                }

                if (spec is int value)
                {
                    specVersions[manifestPath.Split('/')[1]] = value;
                }
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            blobNames.Clear();
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Account package listing failed. Package={Package}", name);
            var errorResponse = req.CreateResponse(HttpStatusCode.ServiceUnavailable);
            FunctionCors.Apply(req, errorResponse);
            await errorResponse.WriteAsJsonAsync(new { error = "Workflow package registry is unavailable." }, cancellationToken);
            return errorResponse;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        FunctionCors.Apply(req, response);
        await response.WriteAsJsonAsync(
            AccountPackageListing.Build(name, blobNames, latestPointerJson, specVersions.Count > 0 ? specVersions : null),
            cancellationToken);
        return response;
    }

    [Function("WorkflowPackageContent")]
    public async Task<HttpResponseData> GetCurrentContentAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "WorkflowPackages/Current/Content")] HttpRequestData req)
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

        if (!AccountAuthorizer.IsActive(account))
        {
            return AccountAuthorizer.CreateForbiddenResponse(req);
        }

        var packageRef = await _pinResolver.ResolvePinAsync(account.AppUserId, cancellationToken);

        WorkflowPackage package;
        try
        {
            package = await _packageStore.ResolveAsync(packageRef, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Workflow package content resolution failed. Pin={Pin}", packageRef);
            var errorResponse = req.CreateResponse(HttpStatusCode.ServiceUnavailable);
            FunctionCors.Apply(req, errorResponse);
            await errorResponse.WriteAsJsonAsync(new { error = "Workflow package registry is unavailable." }, cancellationToken);
            return errorResponse;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        FunctionCors.Apply(req, response);
        await response.WriteAsJsonAsync(
            new WorkflowPackageContentResponse(
                package.Manifest.Name,
                package.Manifest.Version,
                package.Manifest.SpecVersion,
                package.Manifest,
                package.SourceFiles ?? new Dictionary<string, string>(StringComparer.Ordinal)),
            cancellationToken);

        return response;
    }

    [Function("WorkflowPackageDiagram")]
    public async Task<HttpResponseData> GetDiagramAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "WorkflowPackages/Current/Diagram")] HttpRequestData req)
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

        if (!AccountAuthorizer.IsActive(account))
        {
            return AccountAuthorizer.CreateForbiddenResponse(req);
        }

        var packageRef = await _pinResolver.ResolvePinAsync(account.AppUserId, cancellationToken);

        WorkflowPackage package;
        try
        {
            package = await _packageStore.ResolveAsync(packageRef, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Workflow package diagram resolution failed. Pin={Pin}", packageRef);
            var errorResponse = req.CreateResponse(HttpStatusCode.ServiceUnavailable);
            FunctionCors.Apply(req, errorResponse);
            await errorResponse.WriteAsJsonAsync(new { error = "Workflow package registry is unavailable." }, cancellationToken);
            return errorResponse;
        }

        // The same generator that produces the checked-in dag.mmd (pinned by
        // WorkflowDagDiagramTests) — a read-only projection of the manifest.
        return await CreateJsonResponseAsync(req, HttpStatusCode.OK,
            new { diagram = WorkflowDagDiagram.Generate(package.Manifest) }, cancellationToken);
    }

    [Function("WorkflowPackageDiagramPreview")]
    public async Task<HttpResponseData> PostDiagramPreviewAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "WorkflowPackages/Diagram")] HttpRequestData req)
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

        if (!AccountAuthorizer.IsActive(account))
        {
            return AccountAuthorizer.CreateForbiddenResponse(req);
        }

        // Pure compute over the caller's own posted manifest — the editor's
        // effective-graph preview (#144). No storage access; the diagram stays
        // single-sourced in WorkflowDagDiagram.
        string body;

        using (var reader = new StreamReader(req.Body))
        {
            body = await reader.ReadToEndAsync(cancellationToken);
        }

        WorkflowPackageManifest? manifest;

        try
        {
            manifest = JsonSerializer.Deserialize<WorkflowPackageManifest>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            return await CreateJsonResponseAsync(req, HttpStatusCode.BadRequest,
                new { error = $"Malformed manifest: {ex.Message}" }, cancellationToken);
        }

        if (manifest == null)
        {
            return await CreateJsonResponseAsync(req, HttpStatusCode.BadRequest,
                new { error = "A manifest document is required." }, cancellationToken);
        }

        try
        {
            return await CreateJsonResponseAsync(req, HttpStatusCode.OK,
                new { diagram = WorkflowDagDiagram.Generate(manifest) }, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return await CreateJsonResponseAsync(req, HttpStatusCode.BadRequest,
                new { error = ex.Message }, cancellationToken);
        }
    }

    [Function("WorkflowPackageLineage")]
    public async Task<HttpResponseData> GetLineageAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "WorkflowPackages/Lineage")] HttpRequestData req)
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

        if (!AccountAuthorizer.IsActive(account))
        {
            return AccountAuthorizer.CreateForbiddenResponse(req);
        }

        var rawRef = System.Web.HttpUtility.ParseQueryString(req.Url.Query)["ref"];

        if (!WorkflowPackageRef.TryParse(rawRef, out var packageRef) || packageRef!.IsLatest)
        {
            return await CreateJsonResponseAsync(req, HttpStatusCode.BadRequest,
                new { error = "ref must be a concrete package reference (name@vYYYY.MM.N)." }, cancellationToken);
        }

        if (!WorkflowPackageNaming.CanAccess(packageRef.Name, account.AppUserId))
        {
            return await CreateJsonResponseAsync(req, HttpStatusCode.Forbidden,
                new { error = "Workflow package is not accessible from this account." }, cancellationToken);
        }

        IReadOnlyList<string> chain;
        try
        {
            chain = await _lineage.GetLineageAsync(packageRef, cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("was not found", StringComparison.Ordinal))
        {
            return await CreateJsonResponseAsync(req, HttpStatusCode.NotFound,
                new { error = ex.Message }, cancellationToken);
        }

        // The acct-* rule on every hop — unreachable by construction (publish
        // stamping validates sources), enforced anyway.
        if (chain.Any(hop => WorkflowPackageRef.TryParse(hop, out var hopRef)
            && !WorkflowPackageNaming.CanAccess(hopRef!.Name, account.AppUserId)))
        {
            return await CreateJsonResponseAsync(req, HttpStatusCode.Forbidden,
                new { error = "Workflow package lineage crosses another account's package." }, cancellationToken);
        }

        return await CreateJsonResponseAsync(req, HttpStatusCode.OK, new WorkflowPackageLineageResponse(chain), cancellationToken);
    }

    [Function("WorkflowPackagePublish")]
    public async Task<HttpResponseData> PublishAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "WorkflowPackages/Publish")] HttpRequestData req)
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

        if (!AccountAuthorizer.IsActive(account))
        {
            return AccountAuthorizer.CreateForbiddenResponse(req);
        }

        WorkflowPackagePublishRequest? publishRequest = null;
        var requestBody = await req.ReadAsStringAsync() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(requestBody))
        {
            try
            {
                publishRequest = JsonSerializer.Deserialize<WorkflowPackagePublishRequest>(requestBody, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Invalid WorkflowPackagePublish request: malformed JSON body.");
                return await CreateJsonResponseAsync(req, HttpStatusCode.BadRequest, new { errors = new[] { "Malformed JSON request body." } }, cancellationToken);
            }
        }

        if (publishRequest is null)
        {
            return await CreateJsonResponseAsync(req, HttpStatusCode.BadRequest, new { errors = new[] { "A publish request body is required." } }, cancellationToken);
        }

        WorkflowPackagePublishResult result;
        try
        {
            result = await _publisher.PublishAsync(account.AppUserId, publishRequest, cancellationToken);
        }
        catch (Azure.RequestFailedException ex)
        {
            _logger.LogError(ex, "Workflow package publish failed against the registry. AppUserId={AppUserId}", account.AppUserId);
            return await CreateJsonResponseAsync(req, HttpStatusCode.ServiceUnavailable, new { errors = new[] { "Workflow package registry is unavailable." } }, cancellationToken);
        }

        if (result.Forbidden)
        {
            return await CreateJsonResponseAsync(req, HttpStatusCode.Forbidden, new { errors = result.Errors }, cancellationToken);
        }

        if (!result.Succeeded)
        {
            return await CreateJsonResponseAsync(req, HttpStatusCode.BadRequest, new { errors = result.Errors }, cancellationToken);
        }

        return await CreateJsonResponseAsync(req, HttpStatusCode.OK, result.Response!, cancellationToken);
    }

    private static async Task<HttpResponseData> CreateJsonResponseAsync<T>(
        HttpRequestData req,
        HttpStatusCode statusCode,
        T payload,
        CancellationToken cancellationToken)
    {
        var response = req.CreateResponse(statusCode);
        FunctionCors.Apply(req, response);
        await response.WriteAsJsonAsync(payload, cancellationToken);
        return response;
    }
}
