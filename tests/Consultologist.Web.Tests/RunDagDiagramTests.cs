using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Provenance;

namespace Consultologist.Web.Tests;

/// <summary>
/// #642: the run's own graph as a pure function — the rails' five states in
/// the rails' own colours, dashed for skipped, tallies on fanned nodes,
/// hexagons for the deliverables' three states, and determinism (the
/// poll's no-op guarantee).
/// </summary>
public class RunDagDiagramTests
{
    private static ConsultGenerationJobResponse Job(
        IReadOnlyList<ConsultGenerationNodeDescriptor>? nodes = null,
        IReadOnlyDictionary<string, ConsultGenerationNodeStatus>? outputs = null,
        IReadOnlyList<ConsultCollectionRoster>? collections = null,
        IReadOnlyList<ConsultGenerationResultDocumentResponse>? documents = null,
        IReadOnlyList<ConsultSkippedDocumentResponse>? skipped = null,
        IReadOnlyList<ConsultFailedDocumentResponse>? failed = null) =>
        new("job-1", "user-1", "Running",
            TotalBlockCount: 1, CompletedBlockCount: 0, FailedBlockCount: 0,
            GeneratedBlocks: new Dictionary<string, string>(), FailedBlocks: new Dictionary<string, string>(),
            Success: false,
            AssembledDocuments: documents, SkippedDocuments: skipped, FailedDocuments: failed,
            Nodes: nodes, NodeOutputs: outputs, Collections: collections);

    private static ConsultGenerationNodeStatus Row(string id, string status) =>
        new(id, id, status, "i", "o", null, null);

    [Fact]
    public void TheFiveStates_WearTheRailsColours()
    {
        var diagram = RunDagDiagram.Build(Job(
            nodes: new ConsultGenerationNodeDescriptor[]
            {
                new("extract", "Extracting", "p", Bindings: new Dictionary<string, ConsultNodeBindingDescriptor> { ["draft"] = new("input:consult_draft") }),
                new("classify", "Classifying", "p"),
                new("draft", "Drafting", "p", Bindings: new Dictionary<string, ConsultNodeBindingDescriptor> { ["c"] = new("node:extract") }),
                new("summarise", "Summarising", "p"),
                new("later", "Later", "p")
            },
            outputs: new Dictionary<string, ConsultGenerationNodeStatus>
            {
                ["extract"] = Row("extract", "Completed"),
                ["classify"] = Row("classify", "Failed"),
                ["draft"] = Row("draft", "Running"),
                ["summarise"] = Row("summarise", "Skipped")
            }));

        Assert.StartsWith("flowchart TD", diagram);
        Assert.Contains("class extract done", diagram);
        Assert.Contains("class classify failed", diagram);
        Assert.Contains("class draft running", diagram);
        Assert.Contains("class summarise skipped", diagram);
        Assert.Contains("class later ranNot", diagram);
        // The colours, verbatim — the rails' tokens.
        Assert.Contains("stroke:#107c10", diagram);
        Assert.Contains("stroke:#a4262c", diagram);
        Assert.Contains("stroke:#0067b8", diagram);
        Assert.Contains("stroke-dasharray:4 3", diagram);
        Assert.Contains("opacity:0.6", diagram);
        // The stadium source and its edge.
        Assert.Contains("src_input_consult_draft([\"input:consult_draft\"])", diagram);
        Assert.Contains("extract --> draft", diagram);
    }

    [Fact]
    public void ASkippedNode_DashesItsEdges()
    {
        var diagram = RunDagDiagram.Build(Job(
            nodes: new ConsultGenerationNodeDescriptor[]
            {
                new("a", "A", "p"),
                new("b", "B", "p", Bindings: new Dictionary<string, ConsultNodeBindingDescriptor> { ["x"] = new("node:a") })
            },
            outputs: new Dictionary<string, ConsultGenerationNodeStatus> { ["b"] = Row("b", "Skipped") }));

        Assert.Contains("a -.-> b", diagram);
        Assert.DoesNotContain("a --> b", diagram);
    }

    [Fact]
    public void AFannedNode_RollsUp_AndAnyFailedItemPaintsItRed()
    {
        var nodes = new ConsultGenerationNodeDescriptor[]
        {
            new("section", "Sectioning", "p", ForEach: "data:standards")
        };
        var rosters = new[] { new ConsultCollectionRoster("standards", new[] { new ConsultCollectionItem("hpi", "H"), new ConsultCollectionItem("pmh", "P"), new ConsultCollectionItem("plan", "N") }) };

        var failed = RunDagDiagram.Build(Job(nodes: nodes, collections: rosters,
            outputs: new Dictionary<string, ConsultGenerationNodeStatus>
            {
                ["section:hpi"] = Row("section", "Completed"),
                ["section:pmh"] = Row("section", "Failed")
            }));
        Assert.Contains("per data:standards item (1/3)", failed);
        Assert.Contains("class section failed", failed);

        var inFlight = RunDagDiagram.Build(Job(nodes: nodes, collections: rosters,
            outputs: new Dictionary<string, ConsultGenerationNodeStatus>
            {
                ["section:hpi"] = Row("section", "Completed")
            }));
        Assert.Contains("class section running", inFlight);

        var done = RunDagDiagram.Build(Job(nodes: nodes, collections: rosters,
            outputs: new Dictionary<string, ConsultGenerationNodeStatus>
            {
                ["section:hpi"] = Row("section", "Completed"),
                ["section:pmh"] = Row("section", "Completed"),
                ["section:plan"] = Row("section", "Completed")
            }));
        Assert.Contains("class section done", done);
    }

    [Fact]
    public void TheDeliverables_AreHexagons_InTheirThreeStates()
    {
        var diagram = RunDagDiagram.Build(Job(
            nodes: new ConsultGenerationNodeDescriptor[]
            {
                new("assemble", "Assembling", null, Aggregate: new[] { "node:draft" })
            },
            documents: new[] { new ConsultGenerationResultDocumentResponse("note", "Consultation note", "text") },
            skipped: new[] { new ConsultSkippedDocumentResponse("letter", "Letter", "needs billable") },
            failed: new[] { new ConsultFailedDocumentResponse("summary", "Summary", "check failed") }));

        Assert.Contains("result_note{{\"Consultation note\"}}", diagram);
        Assert.Contains("class result_note", diagram);
        Assert.Contains("result_letter{{\"Letter\"}}", diagram);
        Assert.Contains("result_summary{{\"Summary\"}}", diagram);
        Assert.Contains("assemble --> result_note", diagram);
        Assert.Contains("assemble -.-> result_letter", diagram);
        // done/skipped/failed classing per state.
        Assert.Matches(@"class [^\n]*result_note[^\n]* done", diagram);
        Assert.Matches(@"class [^\n]*result_letter[^\n]* skipped", diagram);
        Assert.Matches(@"class [^\n]*result_summary[^\n]* failed", diagram);
    }

    [Fact]
    public void CheckAndTemplate_AreDrawn_AndIdsAreSanitized()
    {
        var diagram = RunDagDiagram.Build(Job(
            nodes: new ConsultGenerationNodeDescriptor[]
            {
                new("extract-terms", "Extracting", "p"),
                new("note-terms", "Note terms", "p"),
                new("patient-header", "Header", "p", Template: true),
                new("coverage", "Coverage", null, Check: new ConsultCheckDescriptor("terms-subset", "node:extract-terms", "node:note-terms", "no"))
            }));

        Assert.Contains("extract_terms -.->|\"of\"| coverage", diagram);
        Assert.Contains("note_terms -.->|\"in\"| coverage", diagram);
        Assert.Contains("check: terms-subset", diagram);
        Assert.Contains("template", diagram);
    }

    [Fact]
    public void TheSameDetail_BuildsTheSameBytes()
    {
        var job = Job(
            nodes: new ConsultGenerationNodeDescriptor[] { new("a", "A", "p") },
            outputs: new Dictionary<string, ConsultGenerationNodeStatus> { ["a"] = Row("a", "Running") });

        Assert.Equal(RunDagDiagram.Build(job), RunDagDiagram.Build(job));
    }
}
