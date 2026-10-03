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
    public void ACustomNode_CarriesItsContentAddressedAgentAndSchemaHash()
    {
        // #760: a custom node's contract is 'custom' (no catalog entry); its agent comes
        // from the provisioned map, keyed by schema id, and its hash rides the descriptor.
        var node = new WorkflowNodeSpec("shape", "Shape", Prompt: "p", Output: new WorkflowNodeOutputSpec("my-shape"));
        var schemaContracts = new Dictionary<string, string>(StringComparer.Ordinal) { ["my-shape"] = OutputContracts.Custom };
        var customAgents = new Dictionary<string, ConsultGenerationJobStarter.CustomAgentPin>(StringComparer.Ordinal)
        {
            ["my-shape"] = new("custom-abc123", "1", "hash-abc"),
        };

        var descriptor = ConsultGenerationJobStarter.DescribeNode(node, schemaContracts, ContractAgents, customAgents);

        Assert.Equal(OutputContracts.Custom, descriptor.OutputContract);
        Assert.Equal("custom-abc123", descriptor.AgentName);
        Assert.Equal("1", descriptor.AgentVersion);
        Assert.Equal("hash-abc", descriptor.CustomSchemaHash);
    }

    [Fact]
    public void TwoCustomNodes_EachCarryTheirOwnAgent_KeyedBySchemaId()
    {
        // The two share contract id 'custom' but have different content-addressed agents.
        var schemaContracts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["shape-a"] = OutputContracts.Custom,
            ["shape-b"] = OutputContracts.Custom,
        };
        var customAgents = new Dictionary<string, ConsultGenerationJobStarter.CustomAgentPin>(StringComparer.Ordinal)
        {
            ["shape-a"] = new("custom-aaa", "1", "ha"),
            ["shape-b"] = new("custom-bbb", "1", "hb"),
        };

        var a = ConsultGenerationJobStarter.DescribeNode(
            new WorkflowNodeSpec("a", "A", Prompt: "p", Output: new WorkflowNodeOutputSpec("shape-a")), schemaContracts, ContractAgents, customAgents);
        var b = ConsultGenerationJobStarter.DescribeNode(
            new WorkflowNodeSpec("b", "B", Prompt: "p", Output: new WorkflowNodeOutputSpec("shape-b")), schemaContracts, ContractAgents, customAgents);

        Assert.Equal("custom-aaa", a.AgentName);
        Assert.Equal("custom-bbb", b.AgentName);
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
