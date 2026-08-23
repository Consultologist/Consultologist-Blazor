using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace Consultologist.Web.Services.Workflow;

public interface IWorkflowEndpointService
{
    Task<WorkflowPackageResponse> GetCurrentPackageAsync();
    /// <summary>The pinned package, or a named one since #411 split editing from pinning.</summary>
    Task<WorkflowPackageContentResponse> GetCurrentPackageContentAsync(string? packageRef = null);
    Task<WorkflowPublishOutcome> PublishPackageAsync(WorkflowPackagePublishRequest request);
    Task<PublicChainView?> GetPublicChainAsync();
    Task<PublicPackageView?> GetMyPackagesAsync();
    Task<Dictionary<string, PublicCatalogEntry>?> GetCatalogAsync(string version);
    Task<IReadOnlyList<string>?> GetLineageAsync(string packageRef);
    Task<string?> GetCurrentDiagramAsync(string? packageRef = null);
    Task<string?> GetDiagramForManifestAsync(JsonElement manifest);
}

/// <summary>One entry of a specific catalog version's document (public registry blob).</summary>
public record PublicCatalogEntry(string? AgentName, string? AgentVersion);

/// <summary>
/// Minimal mirror of the anonymous Public/Chain document: the repo-owned
/// package listings (the selector's public half, #134) and the catalog's
/// contract → agent mapping (History's agent-name decoration — the job record
/// stores contract → version only; names are catalog metadata).
/// </summary>
public record PublicChainView(
    IReadOnlyList<PublicPackageView>? Packages,
    PublicCatalogView? OutputContracts,
    IReadOnlyList<PublicAgentView>? AgentDefinitions = null);

/// <summary>One published agent definition's listing (the chain's third link).</summary>
public record PublicAgentView(string? Name, string? Latest, List<string>? Versions);

/// <summary>One package's registry listing (public repo package or the caller's fork).</summary>
public record PublicPackageView(
    string? Name,
    string? Latest,
    List<string>? Versions,
    Dictionary<string, int>? SpecVersions = null,
    // v9 § 4 (#432): per-version titles; absent means untitled.
    Dictionary<string, string>? Titles = null);

public record PublicCatalogView(Dictionary<string, PublicContractView>? Contracts, string? Latest = null);

public record PublicContractView(string? AgentName);

public record WorkflowPackageBlockResponse(string Id, string Name);

/// <summary>One declared input slot — the setup form renders a field per entry.</summary>
public record WorkflowPackageInputResponse(
    string Id,
    string Label,
    bool Required,
    // v8: the declared type and, for an enum, its values — what the setup form
    // renders a control from. Null on v5-v7 packages.
    string? Type = null,
    IReadOnlyList<string>? Values = null,
    // v9 (#424): an array's element type and an object's fields. Null before 9.
    // Mirrors the server's record, which is what the response deserialises into.
    string? Items = null,
    IReadOnlyList<WorkflowPackageFieldResponse>? Fields = null);

/// <summary>Mirrors Consultologist.Api.Workflow.WorkflowPackageFieldResponse.</summary>
public record WorkflowPackageFieldResponse(
    string Id,
    string Label,
    bool Required,
    string? Type = null,
    IReadOnlyList<string>? Values = null);

/// <summary>Mirrors Consultologist.Api.Workflow.WorkflowInputTypes.</summary>
public static class WorkflowInputTypes
{
    public const string Text = "text";
    public const string Date = "date";
    public const string Enum = "enum";
    public const string Boolean = "boolean";

    // v9 (#424): a number, an object of scalar fields, an array of elements.
    public const string Number = "number";
    public const string Object = "object";
    public const string Array = "array";

    /// <summary>Every type the format has — pinned against the server's in SpecVersionMirrorTests.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Text, Date, Enum, Boolean, Number, Object, Array };

    /// <summary>The types a field may be. Mirrors the server's; pinned.</summary>
    public static readonly IReadOnlyList<string> Scalars = new[] { Text, Date, Enum, Boolean, Number };

    /// <summary>What an array may hold: a scalar, or a one-level object. Mirrors the server's; pinned.</summary>
    public static readonly IReadOnlyList<string> ElementTypes = new[] { Text, Date, Enum, Boolean, Number, Object };

    public static string Of(WorkflowPackageInputResponse input) => input.Type ?? Text;

    /// <summary>The same default for the editor's view of a declaration.</summary>
    public static string Of(WorkflowManifestReader.InputView input) => input.Type ?? Text;

    public static string Of(WorkflowManifestReader.FieldView field) => field.Type ?? Text;

    /// <summary>An object, or an array of objects — the declarations that carry fields.</summary>
    public static bool DeclaresObject(WorkflowManifestReader.InputView input) =>
        Of(input) == Object || (Of(input) == Array && input.Items == Object);

    /// <summary>An enum, or an array of enums — the declarations that carry values.</summary>
    public static bool DeclaresValues(WorkflowManifestReader.InputView input) =>
        Of(input) == Enum || (Of(input) == Array && input.Items == Enum);

    /// <summary>The shapes v9 added (#424); below 9 any of them is refused by name.</summary>
    public static bool RequiresV9(WorkflowManifestReader.InputView input) =>
        Of(input) is Number or Object or Array || input.Items != null || input.Fields != null;
}

/// <summary>One declared deliverable — blocks and result tabs group by these.</summary>
public record WorkflowPackageResultResponse(string Id, string Label);

public record WorkflowPackageResponse(
    string Name,
    string Version,
    int SpecVersion,
    IReadOnlyList<WorkflowPackageBlockResponse>? Blocks = null,
    // v7 only; null on v5/v6, whose single input is the consult_draft
    // convention and whose single deliverable needs no grouping.
    IReadOnlyList<WorkflowPackageInputResponse>? Inputs = null,
    IReadOnlyList<WorkflowPackageResultResponse>? Results = null,
    // v9 § 4 (#432): the title, or null — the ref is the stated fallback.
    string? Title = null)
{
    public string Ref => $"{Name}@{Version}";
}

/// <summary>
/// The editor's load half. The manifest rides as an opaque JsonElement: the
/// texts-only editor never modifies it, and round-tripping it verbatim to the
/// publish endpoint avoids mirroring the server's typed manifest (and its
/// binding-value converter) in the client.
/// </summary>
public record WorkflowPackageContentResponse(
    string Name,
    string Version,
    int SpecVersion,
    JsonElement Manifest,
    Dictionary<string, string> Files)
{
    public string Ref => $"{Name}@{Version}";
}

public record WorkflowPackagePublishRequest(
    string Source,
    JsonElement Manifest,
    Dictionary<string, string> Files);

public record WorkflowPackagePublishResponse(
    string Name,
    string Version,
    string Ref,
    List<string>? Warnings = null);

/// <summary>Success carries the response; failure carries the endpoint's error list for inline display.</summary>
public record WorkflowPublishOutcome(
    WorkflowPackagePublishResponse? Response,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded => Response != null;
}

public sealed class WorkflowEndpointService : IWorkflowEndpointService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly IAccessTokenProvider _accessTokenProvider;
    private readonly NavigationManager _navigation;
    private readonly ILogger<WorkflowEndpointService> _logger;

    public WorkflowEndpointService(
        HttpClient httpClient,
        IConfiguration configuration,
        IAccessTokenProvider accessTokenProvider,
        NavigationManager navigation,
        ILogger<WorkflowEndpointService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _accessTokenProvider = accessTokenProvider;
        _navigation = navigation;
        _logger = logger;
    }

    public async Task<WorkflowPackageResponse> GetCurrentPackageAsync()
    {
        var packageUrl = _configuration["AzureFunction:WorkflowPackageCurrentUrl"];

        if (string.IsNullOrWhiteSpace(packageUrl))
        {
            throw new InvalidOperationException("AzureFunction:WorkflowPackageCurrentUrl is not configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, packageUrl);
        await AddAuthorizationAsync(request);

        using var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError(
                "Workflow package endpoint failed with status {StatusCode}: {Error}",
                response.StatusCode,
                errorContent);

            throw new HttpRequestException($"Workflow package endpoint failed: {response.StatusCode}");
        }

        return await response.Content.ReadFromJsonAsync<WorkflowPackageResponse>()
            ?? throw new InvalidOperationException("Failed to deserialize workflow package response.");
    }

    public async Task<WorkflowPackageContentResponse> GetCurrentPackageContentAsync(string? packageRef = null)
    {
        var contentUrl = _configuration["AzureFunction:WorkflowPackageContentUrl"];

        if (string.IsNullOrWhiteSpace(contentUrl))
        {
            throw new InvalidOperationException("AzureFunction:WorkflowPackageContentUrl is not configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, WithPackageRef(contentUrl, packageRef));
        await AddAuthorizationAsync(request);

        using var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError(
                "Workflow package content endpoint failed with status {StatusCode}: {Error}",
                response.StatusCode,
                errorContent);

            throw new HttpRequestException($"Workflow package content endpoint failed: {response.StatusCode}");
        }

        return await response.Content.ReadFromJsonAsync<WorkflowPackageContentResponse>()
            ?? throw new InvalidOperationException("Failed to deserialize workflow package content response.");
    }

    public async Task<WorkflowPublishOutcome> PublishPackageAsync(WorkflowPackagePublishRequest request)
    {
        var publishUrl = _configuration["AzureFunction:WorkflowPackagePublishUrl"];

        if (string.IsNullOrWhiteSpace(publishUrl))
        {
            throw new InvalidOperationException("AzureFunction:WorkflowPackagePublishUrl is not configured.");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, publishUrl)
        {
            Content = JsonContent.Create(request)
        };
        await AddAuthorizationAsync(httpRequest);

        using var response = await _httpClient.SendAsync(httpRequest);

        if (response.IsSuccessStatusCode)
        {
            var published = await response.Content.ReadFromJsonAsync<WorkflowPackagePublishResponse>()
                ?? throw new InvalidOperationException("Failed to deserialize publish response.");
            return new WorkflowPublishOutcome(published, Array.Empty<string>());
        }

        // 400/403 carry { errors: [...] } for inline display; anything else is a
        // transport-level failure.
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden)
        {
            var payload = await response.Content.ReadFromJsonAsync<PublishErrorPayload>();
            return new WorkflowPublishOutcome(
                null,
                payload?.Errors is { Count: > 0 } ? payload.Errors : new List<string> { $"Publish failed: {response.StatusCode}" });
        }

        var errorContent = await response.Content.ReadAsStringAsync();
        _logger.LogError(
            "Workflow package publish endpoint failed with status {StatusCode}: {Error}",
            response.StatusCode,
            errorContent);

        throw new HttpRequestException($"Workflow package publish endpoint failed: {response.StatusCode}");
    }

    public async Task<PublicChainView?> GetPublicChainAsync()
    {
        var chainUrl = _configuration["AzureFunction:PublicChainUrl"];

        if (string.IsNullOrWhiteSpace(chainUrl))
        {
            return null;
        }

        try
        {
            // Anonymous endpoint — no bearer token; a failure only costs the
            // display decoration, never the page.
            return await _httpClient.GetFromJsonAsync<PublicChainView>(chainUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Public chain view unavailable; agent names will not be decorated.");
            return null;
        }
    }

    public async Task<PublicPackageView?> GetMyPackagesAsync()
    {
        var mineUrl = _configuration["AzureFunction:WorkflowPackageMineUrl"];

        if (string.IsNullOrWhiteSpace(mineUrl))
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, mineUrl);
            await AddAuthorizationAsync(request);

            using var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Account package listing failed with status {StatusCode}.", response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<PublicPackageView>();
        }
        catch (AccessTokenNotAvailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Account package listing unavailable; the selector shows public packages only.");
            return null;
        }
    }

    public async Task<Dictionary<string, PublicCatalogEntry>?> GetCatalogAsync(string version)
    {
        var baseUrl = _configuration["AzureFunction:PublicRegistryBaseUrl"];

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        try
        {
            // Anonymous, immutable, CORS-open registry blob: the exact catalog
            // document a job's catalogRef names — historically exact agent
            // identities, one cacheable fetch per version.
            var document = await _httpClient.GetFromJsonAsync<CatalogDocument>(
                $"{baseUrl.TrimEnd('/')}/output-contracts/{Uri.EscapeDataString(version)}/output-contracts.json");
            return document?.Contracts;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Catalog document {Version} unavailable; agent display falls back.", version);
            return null;
        }
    }

    public async Task<IReadOnlyList<string>?> GetLineageAsync(string packageRef)
    {
        var lineageUrl = _configuration["AzureFunction:WorkflowPackageLineageUrl"];

        if (string.IsNullOrWhiteSpace(lineageUrl))
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{lineageUrl}?ref={Uri.EscapeDataString(packageRef)}");
            await AddAuthorizationAsync(request);
            using var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<LineagePayload>();
            return payload?.Chain;
        }
        catch (AccessTokenNotAvailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lineage unavailable for {PackageRef}.", packageRef);
            return null;
        }
    }

    public async Task<string?> GetCurrentDiagramAsync(string? packageRef = null)
    {
        var diagramUrl = _configuration["AzureFunction:WorkflowPackageDiagramUrl"];

        if (string.IsNullOrWhiteSpace(diagramUrl))
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, WithPackageRef(diagramUrl, packageRef));
            await AddAuthorizationAsync(request);
            using var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<DiagramPayload>();
            return payload?.Diagram;
        }
        catch (AccessTokenNotAvailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Workflow diagram unavailable.");
            return null;
        }
    }

    // The effective-graph preview (#144): the composed pending manifest goes
    // to the server's generator, so the diagram stays single-sourced.
    public async Task<string?> GetDiagramForManifestAsync(JsonElement manifest)
    {
        var previewUrl = _configuration["AzureFunction:WorkflowPackageDiagramPreviewUrl"];

        if (string.IsNullOrWhiteSpace(previewUrl))
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, previewUrl)
            {
                Content = JsonContent.Create(manifest)
            };
            await AddAuthorizationAsync(request);
            using var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Diagram preview failed with status {StatusCode}.", response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<DiagramPayload>();
            return payload?.Diagram;
        }
        catch (AccessTokenNotAvailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Diagram preview unavailable.");
            return null;
        }
    }

    private sealed record DiagramPayload(string? Diagram);

    private sealed record LineagePayload(List<string>? Chain);

    private sealed record CatalogDocument(Dictionary<string, PublicCatalogEntry>? Contracts);

    private sealed record PublishErrorPayload(List<string>? Errors);

    /// <summary>
    /// Appends the package ref the caller wants, or leaves the URL alone. Null
    /// means "the pin", which is what every call meant before #411 split
    /// editing from pinning — so the endpoints keep their old behaviour for any
    /// caller that does not ask.
    /// </summary>
    private static string WithPackageRef(string url, string? packageRef)
    {
        if (string.IsNullOrWhiteSpace(packageRef))
        {
            return url;
        }

        var separator = url.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{url}{separator}ref={Uri.EscapeDataString(packageRef)}";
    }

    private async Task AddAuthorizationAsync(HttpRequestMessage request)
    {
        var apiScope = _configuration["AzureFunction:ApiScope"];

        if (string.IsNullOrWhiteSpace(apiScope))
        {
            throw new InvalidOperationException("AzureFunction:ApiScope is not configured.");
        }

        var tokenResult = await _accessTokenProvider.RequestAccessToken(new AccessTokenRequestOptions
        {
            Scopes = new[] { apiScope }
        });

        if (!tokenResult.TryGetToken(out var token))
        {
            throw new AccessTokenNotAvailableException(_navigation, tokenResult, new[] { apiScope });
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
    }
}
