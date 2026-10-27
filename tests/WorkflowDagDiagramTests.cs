using Consultologist.Api.Workflow;

using Consultologist.PackageFormat;
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

    // v26 (#1160): a loop draws as its own subgraph — fed by its collection, the body
    // in order — returning into the loop's box through ret; a deliverable's sources
    // each draw an edge. A ≤25 manifest never reaches the loop branch.

    [Fact]
    public void Diagram_At26_DrawsALoopAsASubgraphReturningThroughRet()
    {
        var (manifest, _) = V26Package.Minimal();

        var diagram = WorkflowDagDiagram.Generate(manifest);

        Assert.Contains("snippet_sections([\"snippet.sections\"])", diagram);
        Assert.Contains("snippet_sections -->|\"for\"| for_sections", diagram);
        Assert.Contains("subgraph for_sections[\"sections: per snippet.sections item\"]", diagram);
        Assert.Contains("        section_notes[\"section_notes<br/>", diagram);
        Assert.Contains("sections[\"sections<br/>Sections<br/>item in snippet.sections\"]", diagram);
        Assert.Contains("section_notes -->|\"ret\"| sections", diagram);
        // The body node is not also a top-level box, nor the loop.
        Assert.Equal(1, diagram.Split("section_notes[\"").Length - 1);
        Assert.Equal(1, diagram.Split("    sections[\"").Length - 1);
        // The deliverable's sources each feed its hexagon.
        Assert.Contains("sections --> result_consult", diagram);
        Assert.DoesNotContain("     --> result_consult", diagram);
    }

    [Fact]
    public void Diagram_At25_HasNoLoopSubgraph()
    {
        var (manifest, _) = V25Package.Minimal();

        var diagram = WorkflowDagDiagram.Generate(manifest);

        Assert.DoesNotContain("subgraph for_", diagram);
        Assert.Contains("subgraph foreach_", diagram);
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

    [Theory]
    [InlineData("length_of_stay > 7", "input_length_of_stay -.->|\"when > 7\"| result_patient_letter")]
    [InlineData("patient.age >= 65", "input_patient -.->|\"when .age >= 65\"| result_patient_letter")]
    [InlineData("count(prior_notes) > 1", "input_prior_notes -.->|\"when count > 1\"| result_patient_letter")]
    [InlineData("prior_notes", "input_prior_notes -.->|\"when true\"| result_patient_letter")]
    public void Diagram_DrawsEachV9FormOfTheGrammar(string when, string expected)
    {
        // #427: the edge still leaves the INPUT's stadium; the suffix says
        // which part of it is read.
        var diagram = WorkflowDagDiagram.Generate(V9Fixtures.Conditional(when));

        Assert.Contains(expected, diagram);
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
