using System.Text.Json;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Provenance;
using Consultologist.Web.Services.Workflow;

namespace Consultologist.Web.Tests;

/// <summary>
/// #1340 (package-format-v30-design.md § 1.2): the pages' loop walks reach the root —
/// a node nested under more than two flow nodes still finds its loop (no answer
/// changes at ≤29, where the shape stops at two) — and a body that holds its
/// holder ends the walk instead of looping.
/// </summary>
public class ItemPathWalksTests
{
    private static ConsultGenerationNodeDescriptor Row(string id, string? kind = null, string? @in = null) =>
        new(id, id, Kind: kind, In: @in);

    [Fact]
    public void TheRecordsLoopOf_WalksThroughEveryIfToTheLoop()
    {
        var nodes = new[]
        {
            Row("parts", "for"),
            Row("gate", "if", "parts"),
            Row("inner_gate", "if", "gate"),
            Row("draft", @in: "inner_gate"),
        };

        Assert.Same(nodes[0], RecordFlow.LoopOf(nodes[3], nodes));
        Assert.Same(nodes[0], RecordFlow.LoopOf(nodes[1], nodes));
        Assert.Null(RecordFlow.LoopOf(nodes[0], nodes));
    }

    [Fact]
    public void TheRecordsLoopOf_StopsAtACycle()
    {
        var nodes = new[] { Row("a", "if", "b"), Row("b", "if", "a"), Row("draft", @in: "a") };
        Assert.Null(RecordFlow.LoopOf(nodes[2], nodes));
    }

    [Fact]
    public void TheManifestReadersLoopOf_WalksThroughEveryFlowNodeToTheLoop()
    {
        using var manifest = JsonDocument.Parse("""
            { "specVersion": 30, "nodes": [
              { "id": "parts", "label": "Parts", "for": "item in input.parts", "nodes": ["gate"], "ret": "node.gate" },
              { "id": "gate", "label": "Gate", "if": "item.id == \"a\"", "then": { "nodes": ["inner_gate"], "ret": "node.inner_gate" } },
              { "id": "inner_gate", "label": "Inner gate", "if": "item.id == \"a\"", "then": { "nodes": ["draft"], "ret": "node.draft" } },
              { "id": "draft", "label": "Draft", "format": "prose" } ] }
            """);
        var nodes = WorkflowManifestReader.ReadNodes(manifest.RootElement);
        var loop = nodes.Single(n => n.Id == "parts");

        Assert.Same(loop, WorkflowManifestReader.NodeView.LoopOf(nodes, "draft"));
        Assert.Same(loop, WorkflowManifestReader.NodeView.LoopOf(nodes, "inner_gate"));
        Assert.Same(loop, WorkflowManifestReader.NodeView.LoopOf(nodes, "gate"));
        Assert.Null(WorkflowManifestReader.NodeView.LoopOf(nodes, "parts"));
    }

    [Theory]
    [InlineData("note:sections:plan", "sections:plan")]
    [InlineData("sections:plan", "sections:plan")]
    [InlineData("note:paragraphs:hpi/2", "paragraphs:hpi/2")]
    [InlineData("plan", null)]
    public void TheRailsSkipKey_IsTheHelpersLoopItem(string blockId, string? key) =>
        Assert.Equal(key, RailSkips.LoopItemKeyOf(blockId));
}
