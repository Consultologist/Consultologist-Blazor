using Consultologist.Api.Agents;
using Consultologist.Api.Jobs;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #923 phase 2: DescribeNode resolves each node's agent pin from the package's
/// OWN catalog at job-start and threads it onto the descriptor, so the executor
/// runs the stamped catalog version's agent rather than the global pin. A
/// template runs no agent; a call without the map threads no pin and the
/// activity falls back to the global catalog.
/// </summary>
public class DescribeNodeAgentPinTests
{
    private static readonly IReadOnlyDictionary<string, OutputContractEntry> ContractAgents =
        new Dictionary<string, OutputContractEntry>(StringComparer.Ordinal)
        {
            ["concept-list"] = new("concept-list", "concept-extraction", "7", null),
            ["text"] = new("text", "test-json", "47", null),
            ["classification"] = new("classification", "classification", "3", null),
        };

    [Fact]
    public void AModelNode_CarriesTheAgentPinForItsContract()
    {
        var node = new WorkflowNodeSpec("extract", "Extract", Prompt: "p", Output: new WorkflowNodeOutputSpec("my-concepts"));
        var schemaContracts = new Dictionary<string, string>(StringComparer.Ordinal) { ["my-concepts"] = "concept-list" };

        var descriptor = ConsultGenerationJobStarter.DescribeNode(node, schemaContracts, ContractAgents);

        Assert.Equal("concept-list", descriptor.OutputContract);
        Assert.Equal("concept-extraction", descriptor.AgentName);
        Assert.Equal("7", descriptor.AgentVersion);
    }

    [Fact]
    public void ANodeWithoutAnOutputSchema_CarriesTheTextDefaultAgent()
    {
        var node = new WorkflowNodeSpec("prose", "Prose", Prompt: "p");

        var descriptor = ConsultGenerationJobStarter.DescribeNode(node, schemaContracts: null, ContractAgents);

        Assert.Null(descriptor.OutputContract);
        Assert.Equal("test-json", descriptor.AgentName);
        Assert.Equal("47", descriptor.AgentVersion);
    }

    [Fact]
    public void AClassifier_CarriesTheClassificationAgent()
    {
        var node = new WorkflowNodeSpec("scope", "Scope", Prompt: "p", Kind: WorkflowNodeKinds.Classifier, Values: new List<string> { "in", "out" });

        var descriptor = ConsultGenerationJobStarter.DescribeNode(node, schemaContracts: null, ContractAgents);

        Assert.Equal("classification", descriptor.OutputContract);
        Assert.Equal("classification", descriptor.AgentName);
        Assert.Equal("3", descriptor.AgentVersion);
    }

    [Fact]
    public void ATemplateNode_CarriesNoAgentPin()
    {
        // The render IS the output — no model runs, so no agent is threaded.
        var node = new WorkflowNodeSpec("render", "Render", Prompt: "p", Output: new WorkflowNodeOutputSpec("my-concepts"), Kind: WorkflowNodeKinds.Template);
        var schemaContracts = new Dictionary<string, string>(StringComparer.Ordinal) { ["my-concepts"] = "concept-list" };

        var descriptor = ConsultGenerationJobStarter.DescribeNode(node, schemaContracts, ContractAgents);

        Assert.True(descriptor.Template == true);
        Assert.Null(descriptor.AgentName);
        Assert.Null(descriptor.AgentVersion);
    }

    [Fact]
    public void WithoutTheContractAgentMap_NoPinIsThreaded_SoTheActivityFallsBackToTheGlobalCatalog()
    {
        // A snapshot taken before phase 2 (or a bare call) threads no pin; the
        // executor then resolves the agent against the global catalog.
        var node = new WorkflowNodeSpec("extract", "Extract", Prompt: "p", Output: new WorkflowNodeOutputSpec("my-concepts"));
        var schemaContracts = new Dictionary<string, string>(StringComparer.Ordinal) { ["my-concepts"] = "concept-list" };

        var descriptor = ConsultGenerationJobStarter.DescribeNode(node, schemaContracts, contractAgents: null);

        Assert.Equal("concept-list", descriptor.OutputContract);
        Assert.Null(descriptor.AgentName);
        Assert.Null(descriptor.AgentVersion);
    }
}
