using System.Diagnostics;
using Consultologist.Api.Models;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.Core;
using Microsoft.Extensions.Logging;
using OpenAI.Responses;
using System.ClientModel;
using System.ClientModel.Primitives;

namespace Consultologist.Api.Agents;

/// <summary>
/// The agent client: sends one rendered prompt to the pinned Foundry agent and returns
/// the assistant text. Prompt content lives in workflow packages (milestone 3 retired
/// the compiled prompt builders); rendering happens at the call sites.
/// </summary>
public sealed class AgentSectionGenerator
{
    private readonly ILogger<AgentSectionGenerator> _logger;
    private readonly TokenCredential _credential;

    public AgentSectionGenerator(ILogger<AgentSectionGenerator> logger, TokenCredential credential)
    {
        _logger = logger;
        _credential = credential;

        Console.Error.WriteLine($"[Api.StartupDiagnostics] AgentSectionGenerator constructed. Utc={DateTimeOffset.UtcNow:O}");
        Console.Error.WriteLine(PromptDiagnostics.StartupLine());
        _logger.LogInformation("AgentSectionGenerator constructed. LogPromptText={LogPromptText}", PromptDiagnostics.LogPromptText);
    }

    /// <summary>
    /// Sends one prompt to the given pinned Foundry agent. The caller resolves the pin
    /// through the output-contract catalog: a schema-typed contract's agent is
    /// published with a json_schema text format, so its responses are
    /// schema-conformant JSON — Foundry rejects request-level text options for
    /// agent-bound calls, which is why the format lives on a dedicated agent rather
    /// than on this request.
    /// </summary>
    public async Task<string> SendPromptAsync(
        string stage,
        string userMessage,
        string agentName,
        string agentVersion,
        CancellationToken cancellationToken)
    {
        var endpoint = Environment.GetEnvironmentVariable("AzureAI__Endpoint");
        var azureClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
        var isRunningInAzure = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID"));
        var networkTimeoutSeconds = GetEnvironmentInt("AzureAI__NetworkTimeoutSeconds", 270);
        var maxRetries = GetEnvironmentInt("AzureAI__MaxRetries", 0);

        if (isRunningInAzure && string.IsNullOrWhiteSpace(azureClientId))
        {
            _logger.LogError(
                "AZURE_CLIENT_ID is missing while running in Azure. Configure it so DefaultAzureCredential uses the attached user-assigned managed identity.");

            throw new InvalidOperationException(
                "AZURE_CLIENT_ID must be set when running in Azure so DefaultAzureCredential uses the attached user-assigned managed identity.");
        }

        if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(agentName) || string.IsNullOrEmpty(agentVersion))
        {
            _logger.LogError(
                "Azure AI configuration missing. HasEndpoint={HasEndpoint}, HasAgentName={HasAgentName}, HasAgentVersion={HasAgentVersion}",
                !string.IsNullOrEmpty(endpoint),
                !string.IsNullOrEmpty(agentName),
                !string.IsNullOrEmpty(agentVersion));

            throw new InvalidOperationException("Azure AI configuration missing: AzureAI__Endpoint and a catalog-resolved agent name/version are required");
        }

        var endpointUri = new Uri(endpoint);

        _logger.LogInformation(
            "Azure AI configuration loaded. EndpointHost={EndpointHost}, AgentName={AgentName}, AgentVersion={AgentVersion}, HasAzureClientId={HasAzureClientId}",
            endpointUri.Host,
            agentName,
            agentVersion,
            !string.IsNullOrWhiteSpace(azureClientId));

        var projectClientOptions = new AIProjectClientOptions
        {
            NetworkTimeout = TimeSpan.FromSeconds(networkTimeoutSeconds),
            RetryPolicy = new ClientRetryPolicy(maxRetries)
        };

        var projectClient = new AIProjectClient(endpointUri, _credential, projectClientOptions);

        ResponseResult sdkResponse;
        // #245: length and digest, never the prompt — see PromptDiagnostics.
        Console.Error.WriteLine(PromptDiagnostics.Describe("AgentPrompt", stage, userMessage));

        try
        {
            _logger.LogInformation(
                "Sending Foundry agent request. Stage={Stage}, AgentName={AgentName}, AgentVersion={AgentVersion}, MessageLength={MessageLength}",
                stage,
                agentName,
                agentVersion,
                userMessage.Length);

            var sdkStopwatch = Stopwatch.StartNew();
            sdkResponse = await CreateAgentResponseAsync(
                projectClient.ProjectOpenAIClient,
                new AgentReference(agentName, agentVersion),
                userMessage,
                cancellationToken);

            _logger.LogInformation(
                "Foundry agent request completed with version. Stage={Stage}, ElapsedMs={ElapsedMs}",
                stage,
                sdkStopwatch.ElapsedMilliseconds);
        }
        catch (ClientResultException ex) when (ex.Status == 400)
        {
            _logger.LogWarning(ex, "Foundry SDK rejected agent version payload. Retrying without agent_reference.version.");

            var retryStopwatch = Stopwatch.StartNew();
            sdkResponse = await CreateAgentResponseAsync(
                projectClient.ProjectOpenAIClient,
                new AgentReference(agentName),
                userMessage,
                cancellationToken);

            _logger.LogInformation(
                "Foundry agent request completed without version. Stage={Stage}, ElapsedMs={ElapsedMs}",
                stage,
                retryStopwatch.ElapsedMilliseconds);
        }

        var assistantText = sdkResponse.GetOutputText();

        if (string.IsNullOrWhiteSpace(assistantText))
        {
            _logger.LogError("Foundry response did not contain assistant text. Stage={Stage}", stage);
            throw new InvalidOperationException("No assistant response found");
        }

        Console.Error.WriteLine(PromptDiagnostics.Describe("AgentResponse", stage, assistantText));
        return assistantText;
    }

    internal static string FormatConcepts(IReadOnlyList<ClinicalConcept> concepts)
    {
        if (concepts.Count == 0)
        {
            return "(none)";
        }

        return string.Join(
            Environment.NewLine,
            concepts.Select(concept =>
            {
                var support = string.IsNullOrWhiteSpace(concept.Support) ? string.Empty : $" support: {concept.Support}";
                return $"- {concept.Term} ({concept.Type}) - {concept.Id}; active: {concept.IsActive}; source: {concept.Source}{support}";
            }));
    }

    private static async Task<ResponseResult> CreateAgentResponseAsync(
        ProjectOpenAIClient projectOpenAIClient,
        AgentReference agentReference,
        string userMessage,
        CancellationToken cancellationToken)
    {
        var responsesClient = projectOpenAIClient.GetProjectResponsesClientForAgent(agentReference);
        var options = new CreateResponseOptions
        {
            StoredOutputEnabled = false
        };

        options.InputItems.Add(ResponseItem.CreateUserMessageItem(userMessage));

        var response = await responsesClient.CreateResponseAsync(options, cancellationToken);
        return response.Value;
    }

    private static int GetEnvironmentInt(string name, int defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);

        if (int.TryParse(value, out var parsed) && parsed >= 0)
        {
            return parsed;
        }

        return defaultValue;
    }
}
