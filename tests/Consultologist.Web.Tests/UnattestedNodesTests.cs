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

    // ----- B7 #1006: the 25 kind — a custom shape, engine-validated -----

    private static ConsultGenerationNodeDescriptor Shape25() => new(
        "shape", "Shape", "p", Format: "mine", Producer: "custom", FormatHash: "0123456789abcdefdeadbeef",
        AgentName: "custom-json", AgentVersion: "1", OnInvalid: "flag");

    [Fact]
    public void A25CustomProducer_IsACustomShape_NotUnattested()
    {
        var shape = Shape25();

        Assert.True(UnattestedNodes.IsCustomShape(shape));
        Assert.False(UnattestedNodes.IsCustom(shape));
        Assert.False(UnattestedNodes.IsUnattested(shape, null));
        Assert.True(UnattestedNodes.IsMarked(shape, null));
        Assert.Equal(UnattestedNodes.CustomShapeKind, UnattestedNodes.KindOf(shape, null));
        Assert.Equal("custom shape", UnattestedNodes.Pill(UnattestedNodes.CustomShapeKind));
        Assert.False(UnattestedNodes.IsCustomShape(new ConsultGenerationNodeDescriptor("prose", "Prose", "p", Format: "prose", Producer: "attested")));
    }

    [Fact]
    public void Of_CollectsA25CustomShape_FromItsDescriptor_WithNoRowStamp()
    {
        // The record carries no unattested marker for a 25 custom producer: the
        // descriptor's format, hash and agent are the whole reading.
        var job = Job(
            nodes: new[] { Shape25(), new ConsultGenerationNodeDescriptor("extract", "Extract", "p", Format: "concept_list", Producer: "attested", AgentName: "concept-extraction", AgentVersion: "1") },
            outputs: new Dictionary<string, ConsultGenerationNodeStatus>
            {
                ["shape"] = new("shape", "Shape", "Completed", "in", "out", null, null, Validation: new ConsultValidationOutcome(true, Array.Empty<string>())),
            });

        var entry = Assert.Single(UnattestedNodes.Of(job)).Value;

        Assert.Equal(UnattestedNodes.CustomShapeKind, entry.Kind);
        Assert.Equal("format mine · hash 0123456789ab · served by custom-json@1, attested", UnattestedNodes.Detail(entry));
        Assert.Equal("1 custom-shape block", UnattestedNodes.Summary(UnattestedNodes.Of(job)));
        Assert.Equal(UnattestedNodes.CustomShapeSentence, UnattestedNodes.SentencesOf(UnattestedNodes.Of(job)));
        Assert.StartsWith("Custom shape, engine-validated:", UnattestedNodes.CustomShapeSentence);
    }

    [Fact]
    public void AMixedSet_CountsEachKind_UnattestedFirst()
    {
        var job = Job(
            nodes: new[] { new ConsultGenerationNodeDescriptor("old", "Old", OutputContract: "custom"), Shape25() },
            outputs: new Dictionary<string, ConsultGenerationNodeStatus>
            {
                ["old"] = new("old", "Old", "Completed", "in", "out", null, null, Unattested: true, CustomSchemaHash: "fedcba9876543210", CustomAgent: "custom-fedc@2"),
            });

        var entries = UnattestedNodes.Of(job);

        Assert.Equal("1 unattested block · 1 custom-shape block", UnattestedNodes.Summary(entries));
        Assert.Equal($"{UnattestedNodes.Sentence} {UnattestedNodes.CustomShapeSentence}", UnattestedNodes.SentencesOf(entries));
        Assert.Equal("2 custom-shape blocks", UnattestedNodes.CountSentence(UnattestedNodes.CustomShapeKind, 2));
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
