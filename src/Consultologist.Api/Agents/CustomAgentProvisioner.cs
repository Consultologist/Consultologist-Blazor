using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Azure.Core;
using Consultologist.Api.Workflow;

namespace Consultologist.Api.Agents;

/// <summary>The content-addressed Foundry agent a custom schema runs on.</summary>
public readonly record struct CustomAgentRef(string AgentName, string AgentVersion);

/// <summary>
/// #760 (custom tier): provisions the no-tool Foundry agent a custom output schema
/// runs on. The agent is CONTENT-ADDRESSED by the (canonical schema + model) hash —
/// <c>custom-{hash}</c> — created lazily on first use and NEVER deleted, so the same
/// schema always maps to the same immutable agent: strict json_schema conformance,
/// shared across every run and package with that schema, and re-fetchable forever for
/// reproducibility. Custom output is explicitly UNATTESTED (a user-defined shape, no
/// git-attested manifest) — the agent is not a catalog entry, so startup attestation
/// never checks it; provenance records the schema hash + agent instead.
/// </summary>
public interface ICustomAgentProvisioner
{
    Task<CustomAgentRef> GetOrCreateAsync(string canonicalSchema, string modelId, CancellationToken cancellationToken);
}

/// <summary>
/// The Foundry calls behind <see cref="CustomAgentProvisioner"/>, as a seam so tests
/// substitute them and count loads — and so the whole custom path is inert offline (no
/// RBAC / live Foundry yet).
/// </summary>
public interface IFoundryAgentClient
{
    /// <summary>The agent's version if it already exists, else null.</summary>
    Task<string?> TryGetVersionAsync(string agentName, CancellationToken cancellationToken);

    /// <summary>Create the no-tool strict-json_schema agent; returns its version. Throws
    /// <see cref="FoundryAgentConflictException"/> if it already exists (a concurrent race).</summary>
    Task<string> CreateAsync(
        string agentName, string modelId, string instructions, string strictSchemaJson, CancellationToken cancellationToken);
}

/// <summary>A concurrent create lost the race — the agent now exists; GET it.</summary>
public sealed class FoundryAgentConflictException : Exception
{
    public FoundryAgentConflictException(string agentName)
        : base($"Foundry agent '{agentName}' already exists.") { }
}

public sealed class CustomAgentProvisioner : ICustomAgentProvisioner
{
    // A generic, schema-agnostic instruction: the strict json_schema format on the
    // agent is what enforces the shape; the prompt just asks for the object.
    private const string Instruction =
        "Produce a single JSON object that conforms to the required output schema. Output only the JSON object — no prose, no code fences.";

    // Foundry agent names are length-bounded; custom-{56 hex} = 63 chars stays under
    // common limits while 56 hex (224 bits) keeps collisions impossible in practice.
    // VERIFY the exact limit at go-live and widen/narrow this if needed.
    private const int HashHexLength = 56;

    private readonly IFoundryAgentClient _foundry;
    private readonly ConcurrentDictionary<string, CustomAgentRef> _cache = new(StringComparer.Ordinal);

    public CustomAgentProvisioner(IFoundryAgentClient foundry) => _foundry = foundry;

    public async Task<CustomAgentRef> GetOrCreateAsync(
        string canonicalSchema, string modelId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new InvalidOperationException(
                "A custom output schema requires a model; set CustomAgents__Model.");
        }

        var name = AgentName(canonicalSchema, modelId);
        if (_cache.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var version = await _foundry.TryGetVersionAsync(name, cancellationToken);
        if (version is null)
        {
            try
            {
                version = await _foundry.CreateAsync(name, modelId, Instruction, canonicalSchema, cancellationToken);
            }
            catch (FoundryAgentConflictException)
            {
                // A concurrent run created it first — adopt the existing one.
                version = await _foundry.TryGetVersionAsync(name, cancellationToken)
                    ?? throw new InvalidOperationException(
                        $"Custom agent '{name}' reported a create conflict but was not found.");
            }
        }

        var reference = new CustomAgentRef(name, version);
        _cache.TryAdd(name, reference);
        return reference;
    }

    /// <summary>The deterministic, content-addressed agent name for a (schema, model) pair.</summary>
    public static string AgentName(string canonicalSchema, string modelId)
    {
        var hash = ConsultGenerationProvenance.Sha256Hex($"{canonicalSchema}\n{modelId}");
        return $"custom-{hash[..HashHexLength]}";
    }
}

/// <summary>
/// Production <see cref="IFoundryAgentClient"/>: reuses the Foundry endpoint + managed-
/// identity Bearer flow the attestation service uses (AzureAI__Endpoint / __ApiVersion,
/// scope https://ai.azure.com/.default). The GET is the same shape attestation already
/// runs; the CREATE needs the engine identity to hold Foundry agent-CREATE RBAC (it has
/// read today) and is NOT verifiable offline — the create payload below is modelled on
/// the agent-definition YAML (classification.yaml) and the GET response shape and MUST
/// be validated live at go-live.
/// </summary>
public sealed class FoundryAgentClient : IFoundryAgentClient
{
    // The content-addressed NAME is the identity, so one version per name suffices.
    private const string Version = "1";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TokenCredential _credential;

    public FoundryAgentClient(IHttpClientFactory httpClientFactory, TokenCredential credential)
    {
        _httpClientFactory = httpClientFactory;
        _credential = credential;
    }

    public async Task<string?> TryGetVersionAsync(string agentName, CancellationToken cancellationToken)
    {
        var (endpoint, apiVersion) = Config();
        var url = $"{endpoint.TrimEnd('/')}/agents/{Uri.EscapeDataString(agentName)}/versions/{Version}?api-version={apiVersion}";
        using var request = await AuthorizedRequestAsync(HttpMethod.Get, url, cancellationToken);

        using var response = await Send(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return Version;
    }

    public async Task<string> CreateAsync(
        string agentName, string modelId, string instructions, string strictSchemaJson, CancellationToken cancellationToken)
    {
        var (endpoint, apiVersion) = Config();
        var url = $"{endpoint.TrimEnd('/')}/agents/{Uri.EscapeDataString(agentName)}/versions?api-version={apiVersion}";

        // Modelled on classification.yaml's no-tool strict-json_schema definition —
        // VALIDATE this payload against a live Foundry at go-live.
        var payload = new JsonObject
        {
            ["definition"] = new JsonObject
            {
                ["kind"] = "prompt",
                ["model"] = modelId,
                ["instructions"] = instructions,
                ["text"] = new JsonObject
                {
                    ["format"] = new JsonObject
                    {
                        ["type"] = "json_schema",
                        ["name"] = "custom",
                        ["strict"] = true,
                        ["schema"] = JsonNode.Parse(strictSchemaJson),
                    },
                },
            },
        };

        using var request = await AuthorizedRequestAsync(HttpMethod.Post, url, cancellationToken);
        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await Send(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Conflict)
        {
            throw new FoundryAgentConflictException(agentName);
        }

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonNode.Parse(body)?["version"]?.GetValue<string>() ?? Version;
    }

    private async Task<HttpRequestMessage> AuthorizedRequestAsync(HttpMethod method, string url, CancellationToken cancellationToken)
    {
        var token = await _credential.GetTokenAsync(
            new TokenRequestContext(new[] { "https://ai.azure.com/.default" }), cancellationToken);
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return request;
    }

    private Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        return client.SendAsync(request, cancellationToken);
    }

    private static (string Endpoint, string ApiVersion) Config()
    {
        var endpoint = Environment.GetEnvironmentVariable("AzureAI__Endpoint")
            ?? throw new InvalidOperationException(
                "AzureAI__Endpoint is not configured, so a custom output schema's agent cannot be provisioned.");
        var apiVersion = Environment.GetEnvironmentVariable("AzureAI__ApiVersion") ?? "v1";
        return (endpoint, apiVersion);
    }
}
