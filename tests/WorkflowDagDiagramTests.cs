using Consultologist.Api.Workflow;

namespace Consultologist.Api.Tests;

// The generator's structural pins run on the test fixtures; the general
// package's dag.mmd snapshot lives with the package in the
// consultologist-workflows repo since #16 (content left the app repo).
public class WorkflowDagDiagramTests
{
    [Fact]
    public void Diagram_DrawsNodesEdgesAndTheForEachSubgraph()
    {
        var diagram = WorkflowDagDiagram.Generate(V6Fixtures.SingleCollection());

        Assert.StartsWith("flowchart TD", diagram);
        // Inputs as stadium nodes.
        Assert.Contains("input_consult_draft([\"input:consult_draft\"])", diagram);
        // A scalar node with its schema annotation.
        Assert.Contains("extract-patient-concepts<br/>Extracting clinical concepts<br/>output: concept-list", diagram);
        // The fan-in edge with a renderer annotation.
        Assert.Contains("-->|\"patient_trajectory_concepts (as concept-context)\"|", diagram);
        // The forEach chain as a per-collection subgraph fed by its collection.
        Assert.Contains("-->|\"forEach\"| foreach_", diagram);
        Assert.Contains("subgraph foreach_", diagram);
        // Item-aligned edges chain the steps inside the box.
        Assert.Contains("standard_section_draft -->|\"standard_section_draft\"| patient_section_draft", diagram);
        // The v6 aggregator box and its ordered source edge.
        Assert.Contains("assemble_note", diagram);
        Assert.Contains("-->|\"aggregate\"| assemble_note", diagram);
    }

    [Fact]
    public void Diagram_RendersV5ManifestsNatively()
    {
        var diagram = WorkflowDagDiagram.Generate(V5Fixtures.Manifest());

        // The collection stadium and the per-item subgraph.
        Assert.Contains("data_standards([\"data:standards\"])", diagram);
        Assert.Contains("data_standards -->|\"forEach\"| foreach_data_standards", diagram);
        Assert.Contains("subgraph foreach_data_standards[\"per data:standards item\"]", diagram);
        // item: fields surface in the node labels.
        Assert.Contains("section-instructions<br/>Applying section instructions<br/>item: name, content", diagram);
        // Broadcast edge crosses the boundary.
        Assert.Contains("create_patient_trajectory -->|\"patient_trajectory_concepts (as concept-context)\"| standard_section_draft", diagram);
    }

    // #353: the result set is the package's output contract, and it was the one
    // part of the manifest the diagram never drew.

    [Fact]
    public void Diagram_DrawsEachDeliverableAsItsOwnTerminal()
    {
        var diagram = WorkflowDagDiagram.Generate(V8Fixtures.Conditional());

        // A hexagon, so a deliverable never reads as one more aggregator, and
        // an edge from the aggregator it names.
        Assert.Contains("result_consult_note{{\"consult_note<br/>Consultation note\"}}", diagram);
        Assert.Contains("assemble_note --> result_consult_note", diagram);
        Assert.Contains("result_patient_letter{{\"patient_letter<br/>Patient letter\"}}", diagram);
        Assert.Contains("assemble_letter --> result_patient_letter", diagram);
    }

    [Fact]
    public void Diagram_DrawsAConditionAsADottedEdgeFromTheInputItReads()
    {
        var diagram = WorkflowDagDiagram.Generate(V8Fixtures.Conditional());

        // Dotted because the edge is a predicate: none of the input's value
        // travels along it.
        Assert.Contains(
            "input_encounter_kind -.->|\"when == follow_up\"| result_patient_letter",
            diagram);

        // The unconditional deliverable gets no such edge: exactly one line in
        // the whole diagram is dotted, and it is the letter's.
        var dotted = diagram.Split('\n').Where(line => line.Contains("-.->", StringComparison.Ordinal)).ToList();

        Assert.Equal(
            new[] { "    input_encounter_kind -.->|\"when == follow_up\"| result_patient_letter" },
            dotted);
    }

    [Fact]
    public void Diagram_DrawsAnInputOnlyAConditionReads()
    {
        // The reason this was missing: CollectExternalSources walked bindings
        // and forEach, and a condition-only input is bound by nothing — so it
        // was absent from the diagram of the very package it decides.
        var conditional = V8Fixtures.Conditional();

        Assert.DoesNotContain(
            conditional.Nodes!.SelectMany(node => node.Bindings?.Values ?? Enumerable.Empty<WorkflowBindingValue>()),
            binding => binding.From == "input:encounter_kind");

        Assert.Contains("input_encounter_kind([\"input:encounter_kind\"])",
            WorkflowDagDiagram.Generate(conditional));
    }

    [Theory]
    [InlineData("encounter_kind != follow_up", "when != follow_up")]
    [InlineData("billable", "when true")]
    public void Diagram_DrawsEachFormOfTheGrammar(string when, string expected)
    {
        var diagram = WorkflowDagDiagram.Generate(V8Fixtures.Conditional(when));

        Assert.Contains($"-.->|\"{expected}\"| result_patient_letter", diagram);
    }

    [Fact]
    public void Diagram_DrawsAV6SingleResult()
    {
        // v6 carries neither an id nor a label — just the node reference. The
        // deliverable is still drawn, or the diagram would say nothing about
        // what the package produces.
        var diagram = WorkflowDagDiagram.Generate(V6Fixtures.SingleCollection());

        Assert.Contains("result{{\"result\"}}", diagram);
        Assert.Contains("assemble_note --> result", diagram);
    }

    [Fact]
    public void Generate_RejectsManifestsWithoutNodes()
    {
        var noNodes = V5Fixtures.Manifest() with { Nodes = null };

        Assert.Throws<InvalidOperationException>(() => WorkflowDagDiagram.Generate(noNodes));
    }
}
