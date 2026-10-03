using Consultologist.Api.Agents;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using NSubstitute;

namespace Consultologist.Api.Tests;

/// <summary>
/// #760 (custom tier, run): the content-addressed agent provisioner (GET-or-create,
/// cached, create-conflict → GET) and the per-node unattested provenance that
/// NodeUpdateFrom stamps for a custom node.
/// </summary>
public class CustomSchemaRunTests
{
    private const string SchemaA = """{"type":"object","additionalProperties":false,"required":["a"],"properties":{"a":{"type":"string"}}}""";
    private const string SchemaB = """{"type":"object","additionalProperties":false,"required":["b"],"properties":{"b":{"type":"string"}}}""";

    // --- content-addressed name ---

    [Fact]
    public void AgentName_IsContentAddressed_StablePerSchemaAndModel()
    {
        Assert.Equal(CustomAgentProvisioner.AgentName(SchemaA, "m1"), CustomAgentProvisioner.AgentName(SchemaA, "m1"));
        Assert.NotEqual(CustomAgentProvisioner.AgentName(SchemaA, "m1"), CustomAgentProvisioner.AgentName(SchemaB, "m1"));
        Assert.NotEqual(CustomAgentProvisioner.AgentName(SchemaA, "m1"), CustomAgentProvisioner.AgentName(SchemaA, "m2"));
        Assert.StartsWith("custom-", CustomAgentProvisioner.AgentName(SchemaA, "m1"));
    }

    // --- GET-or-create ---

    [Fact]
    public async Task WhenTheAgentAlreadyExists_ItIsReused_AndNeverCreated()
    {
        var foundry = Substitute.For<IFoundryAgentClient>();
        foundry.TryGetVersionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("1");
        var provisioner = new CustomAgentProvisioner(foundry);

        var reference = await provisioner.GetOrCreateAsync(SchemaA, "m1", default);

        Assert.Equal(CustomAgentProvisioner.AgentName(SchemaA, "m1"), reference.AgentName);
        Assert.Equal("1", reference.AgentVersion);
        await foundry.DidNotReceive().CreateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenAbsent_ItIsCreatedOnce()
    {
        var foundry = Substitute.For<IFoundryAgentClient>();
        foundry.TryGetVersionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((string?)null);
        foundry.CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("1");
        var provisioner = new CustomAgentProvisioner(foundry);

        var reference = await provisioner.GetOrCreateAsync(SchemaA, "m1", default);

        Assert.Equal("1", reference.AgentVersion);
        await foundry.Received(1).CreateAsync(
            Arg.Any<string>(), "m1", Arg.Any<string>(), SchemaA, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnACreateConflict_ItAdoptsTheExistingAgent()
    {
        var foundry = Substitute.For<IFoundryAgentClient>();
        // Absent on the first probe; a concurrent run won the create; present on retry.
        foundry.TryGetVersionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((string?)null, "1");
        foundry.CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new FoundryAgentConflictException("custom-x"));
        var provisioner = new CustomAgentProvisioner(foundry);

        var reference = await provisioner.GetOrCreateAsync(SchemaA, "m1", default);

        Assert.Equal("1", reference.AgentVersion);
    }

    [Fact]
    public async Task TheAgentIsCachedPerName_OneLoadForRepeatedCalls()
    {
        var foundry = Substitute.For<IFoundryAgentClient>();
        foundry.TryGetVersionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("1");
        var provisioner = new CustomAgentProvisioner(foundry);

        await provisioner.GetOrCreateAsync(SchemaA, "m1", default);
        await provisioner.GetOrCreateAsync(SchemaA, "m1", default);

        await foundry.Received(1).TryGetVersionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithoutAModel_ItFailsLoud()
    {
        var provisioner = new CustomAgentProvisioner(Substitute.For<IFoundryAgentClient>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => provisioner.GetOrCreateAsync(SchemaA, "", default));
    }

    // --- provenance: NodeUpdateFrom marks a custom node unattested ---

    [Fact]
    public void ACustomNodesUpdate_IsMarkedUnattested_WithItsSchemaHashAndAgent()
    {
        var node = new ConsultNodeDescriptor(
            "shape", "Shape", PromptId: "p", OutputContract: OutputContracts.Custom,
            AgentName: "custom-abc", AgentVersion: "1", CustomSchemaHash: "hash-abc");
        var result = new NodeRunResult("{\"a\":\"x\"}", null, "in", "out");

        var update = ConsultGenerationOrchestrator.NodeUpdateFrom(node, result, 1, 1);

        Assert.True(update.Unattested);
        Assert.Equal("hash-abc", update.CustomSchemaHash);
        Assert.Equal("custom-abc@1", update.CustomAgent);
    }

    [Fact]
    public void ANonCustomNodesUpdate_IsNotMarkedUnattested()
    {
        var node = new ConsultNodeDescriptor(
            "extract", "Extract", PromptId: "p", OutputContract: OutputContracts.ConceptList,
            AgentName: "concept-extraction", AgentVersion: "7");
        var result = new NodeRunResult("[]", Array.Empty<ClinicalConcept>(), "in", "out");

        var update = ConsultGenerationOrchestrator.NodeUpdateFrom(node, result, 1, 1);

        Assert.Null(update.Unattested);
        Assert.Null(update.CustomSchemaHash);
        Assert.Null(update.CustomAgent);
    }
}
