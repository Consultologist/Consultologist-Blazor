using System.Text.Json.Nodes;
using Consultologist.Api.Agents;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #760 (custom tier, run), as B4 #1003 left it: the content-addressed agents
/// are retired — a custom node runs the formats catalogue's generic agent with
/// its ORIGINAL schema in the prompt — and the per-node unattested provenance
/// that NodeUpdateFrom stamps for a custom node and, since #980, that
/// ItemUpdateFrom stamps per fan item.
/// </summary>
public class CustomSchemaRunTests
{
    [Fact]
    public void TheSchemaHash_IsOverTheOriginalSchema_NotACanonicalisedCopy()
    {
        // #760 (regression, caught by a live first-run): a user schema may name a
        // property "title" or "description". The hash the record stamps — and the
        // schema the dispatch embeds in the prompt — is the ORIGINAL schema, NOT
        // WorkflowPackageValidator.CanonicalizeSchema, which strips those keys.
        var body = """
            {"type":"object","additionalProperties":false,"required":["title","description"],
             "properties":{"title":{"type":"string"},"description":{"type":"string"}}}
            """;
        var (manifest, files) = V22Fixtures.Custom(body: body);
        var node = manifest.Nodes!.First(n => n.Id == "create-typical-trajectory");

        var hash = ConsultGenerationJobStarter.CustomSchemaHashOf(node, manifest, files);

        var original = JsonNode.Parse(body)!.ToJsonString();
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex(original), hash);
        Assert.NotEqual(ConsultGenerationProvenance.Sha256Hex(WorkflowPackageValidator.CanonicalizeSchema(body)), hash);
        Assert.Null(ConsultGenerationJobStarter.CustomSchemaHashOf(node with { Output = null }, manifest, files));
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
    public void ACustomNodesItemUpdate_IsMarkedUnattested_WithItsSchemaHashAndAgent()
    {
        // #980: the per-item row of a fanned custom node carries the same stamp.
        var node = new ConsultNodeDescriptor(
            "shape", "Shape", PromptId: "p", OutputContract: OutputContracts.Custom, ForEach: "data:standards",
            AgentName: "custom-abc", AgentVersion: "1", CustomSchemaHash: "hash-abc");
        var result = new NodeRunResult("{\"a\":\"x\"}", null, "in", "out");

        var update = ConsultGenerationOrchestrator.ItemUpdateFrom(node, "hpi", "HPI", result, 1, 2);

        Assert.True(update.Unattested);
        Assert.Equal("hash-abc", update.CustomSchemaHash);
        Assert.Equal("custom-abc@1", update.CustomAgent);
    }

    [Fact]
    public void ANonCustomNodesItemUpdate_IsNotMarkedUnattested()
    {
        var node = new ConsultNodeDescriptor(
            "draft", "Draft", PromptId: "p", OutputContract: OutputContracts.ConceptList, ForEach: "data:standards",
            AgentName: "concept-extraction", AgentVersion: "7");
        var result = new NodeRunResult("[]", Array.Empty<ClinicalConcept>(), "in", "out");

        var update = ConsultGenerationOrchestrator.ItemUpdateFrom(node, "hpi", "HPI", result, 1, 2);

        Assert.Null(update.Unattested);
        Assert.Null(update.CustomSchemaHash);
        Assert.Null(update.CustomAgent);
    }

    [Fact]
    public void TheStamp_IsOneReading_ForNodeItemAndSummaryRows()
    {
        // #980: the fan's summary row is built from the same stamp the node and
        // item updates use, so the three rows cannot disagree.
        var custom = new ConsultNodeDescriptor(
            "shape", "Shape", PromptId: "p", OutputContract: OutputContracts.Custom,
            AgentName: "custom-abc", AgentVersion: "1", CustomSchemaHash: "hash-abc");
        Assert.Equal((true, "hash-abc", "custom-abc@1"), ConsultGenerationOrchestrator.CustomStamp(custom));

        var unpinned = custom with { AgentName = null, AgentVersion = null };
        Assert.Equal(((bool?)true, (string?)"hash-abc", (string?)null), ConsultGenerationOrchestrator.CustomStamp(unpinned));
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
