using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Provenance;

namespace Consultologist.Web.Tests;

/// <summary>#933: the one reading of the custom tier's marker (#760) both pages share.</summary>
public class UnattestedNodesTests
{
    private static ConsultGenerationJobResponse Job(
        IReadOnlyList<ConsultGenerationNodeDescriptor>? nodes = null,
        IReadOnlyDictionary<string, ConsultGenerationNodeStatus>? outputs = null) =>
        new("job", "user-1", "Completed", 1, 1, 0, new(), new(), true, Nodes: nodes, NodeOutputs: outputs);

    [Fact]
    public void ACustomDescriptor_IsUnattested_BeforeAnyRowIsStamped()
    {
        var custom = new ConsultGenerationNodeDescriptor("shape", "Shape", OutputContract: "custom");
        Assert.True(UnattestedNodes.IsCustom(custom));
        Assert.True(UnattestedNodes.IsUnattested(custom, null));
        Assert.False(UnattestedNodes.IsUnattested(new ConsultGenerationNodeDescriptor("digest", "Digest", OutputContract: "concept-list"), null));
    }

    [Fact]
    public void AStampedRow_IsUnattested_WhateverTheDescriptorSays()
    {
        var stamped = new ConsultGenerationNodeStatus("shape", "Shape", "Completed", "in", "out", null, null, Unattested: true);
        Assert.True(UnattestedNodes.IsUnattested(null, stamped));
    }

    [Fact]
    public void Of_CollectsCustomNodes_WithTheirHashAndAgent_NeverItemRows()
    {
        var job = Job(
            nodes: new[]
            {
                new ConsultGenerationNodeDescriptor("digest", "Digest", OutputContract: "concept-list"),
                new ConsultGenerationNodeDescriptor("shape", "Shape", OutputContract: "custom", ForEach: "data:standards")
            },
            outputs: new Dictionary<string, ConsultGenerationNodeStatus>
            {
                ["digest"] = new("digest", "Digest", "Completed", "in", "out", null, null),
                ["shape"] = new("shape", "Shape", "Completed", null, null, null, null, Unattested: true, CustomSchemaHash: "0123456789abcdef", CustomAgent: "custom-0123@1"),
                // Items are stamped since #980; an unstamped one (a record from before) is custom by descriptor.
                ["shape:hpi"] = new("shape", "Shape", "Completed", "in-h", "out-h", null, null)
            });

        var unattested = UnattestedNodes.Of(job);

        var entry = Assert.Single(unattested).Value;
        Assert.Equal("shape", entry.NodeId);
        Assert.Equal("schema 0123456789ab · agent custom-0123@1", UnattestedNodes.Detail(entry));
    }

    [Fact]
    public void Of_TakesTheHashAndAgent_FromAnItemRow_WhenTheSummaryRowLacksThem()
    {
        // #980: a fan's items carry the stamp; a summary row without one (a record
        // from the gap, or an in-flight fan) still names what ran.
        var job = Job(
            nodes: new[] { new ConsultGenerationNodeDescriptor("shape", "Shape", OutputContract: "custom", ForEach: "data:standards") },
            outputs: new Dictionary<string, ConsultGenerationNodeStatus>
            {
                ["shape"] = new("shape", "Shape", "Completed", null, null, null, null),
                ["shape:pmh"] = new("shape", "Shape", "Completed", "in-p", "out-p", null, null, Unattested: true, CustomSchemaHash: "fedcba9876543210", CustomAgent: "custom-fedc@2"),
                ["shape:hpi"] = new("shape", "Shape", "Completed", "in-h", "out-h", null, null, Unattested: true, CustomSchemaHash: "fedcba9876543210", CustomAgent: "custom-fedc@2")
            });

        var entry = Assert.Single(UnattestedNodes.Of(job)).Value;
        Assert.Equal("schema fedcba987654 · agent custom-fedc@2", UnattestedNodes.Detail(entry));
    }

    [Fact]
    public void Of_ReadsAStampedRow_WhenTheDescriptorIsMissing()
    {
        var job = Job(outputs: new Dictionary<string, ConsultGenerationNodeStatus>
        {
            ["shape"] = new("shape", "Shape", "Completed", null, null, null, null, Unattested: true)
        });

        var entry = Assert.Single(UnattestedNodes.Of(job)).Value;
        Assert.Equal("schema not recorded · agent not recorded", UnattestedNodes.Detail(entry));
        Assert.Equal("1 unattested block", UnattestedNodes.CountSentence(1));
        Assert.Equal("2 unattested blocks", UnattestedNodes.CountSentence(2));
    }
}
