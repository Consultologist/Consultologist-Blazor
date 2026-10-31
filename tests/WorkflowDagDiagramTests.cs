using System.Text.Json;
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
    public void Diagram_At26_DrawsAnIfAndACase_AsBodySubgraphsReturningThroughRet()
    {
        var (manifest, _) = V26Package.Minimal();
        var withFlows = V26JobStartTests.WithRoute(V26JobStartTests.WithTriage(manifest, withElse: true), onNode: true, withElse: true);

        var diagram = WorkflowDagDiagram.Generate(withFlows);

        Assert.Contains("subgraph if_triage_then[\"triage: then\"]", diagram);
        Assert.Contains("subgraph if_triage_else[\"triage: else\"]", diagram);
        Assert.Contains("        full_workup[\"full_workup<br/>", diagram);
        // #1259: the condition's quotes are Mermaid entities — raw they closed the label and failed the graph.
        Assert.Contains("triage[\"triage<br/>Triage<br/>if input.kind == #quot;new#quot;\"]", diagram);
        Assert.Contains("full_workup -->|\"ret (then)\"| triage", diagram);
        Assert.Contains("brief_update -->|\"ret (else)\"| triage", diagram);
        Assert.Contains("subgraph case_route_branch_1[\"route: in_scope\"]", diagram);
        Assert.Contains("subgraph case_route_else[\"route: else\"]", diagram);
        Assert.Contains("plan -->|\"ret (in_scope)\"| route", diagram);
        Assert.Contains("scope -.->|\"case\"| route", diagram);
        // Body nodes are not also top-level boxes.
        Assert.Equal(1, diagram.Split("plan[\"").Length - 1);
    }

    // #1227: from 25 a node's inputs are the reads its templates make — the walk's
    // edges drawn when the caller hands one, labelled by the members read; an input,
    // snippet or macro folder a stadium; a read only a gate makes dotted. Below 25
    // the walk is never consulted. The same generator the GET endpoint, the --dag
    // script and the editor run.

    private static WorkflowDerivedEdges.Walk WalkOf(WorkflowPackageManifest manifest, IReadOnlyDictionary<string, string> files) =>
        WorkflowDerivedEdges.WalkOf(manifest, files, V25Package.Catalog());

    [Fact]
    public void Generate_Below25_IgnoresTheWalk()
    {
        var (manifest, files) = V24Fixtures.Base();

        Assert.Equal(WorkflowDagDiagram.Generate(manifest), WorkflowDagDiagram.Generate(manifest, WalkOf(manifest, files)));
        Assert.Equal(WorkflowDagDiagram.Generate(V8Fixtures.Conditional()), WorkflowDagDiagram.Generate(V8Fixtures.Conditional(), walk: null));
    }

    [Fact]
    public void Diagram_At25_DrawsReadsAsLabelledEdges_AndTheirStadiums()
    {
        var (manifest, files) = V25Package.Minimal();

        var diagram = WorkflowDagDiagram.Generate(manifest, WalkOf(manifest, files));

        // A node read, labelled by the member; an input and a snippet, plain, from their stadiums.
        Assert.Contains("    extract -->|\"value\"| assessment", diagram);
        Assert.Contains("    input_notes --> assessment", diagram);
        Assert.Contains("    snippet_standards --> assessment", diagram);
        Assert.Contains("input_notes([\"input.notes\"])", diagram);
        Assert.Contains("snippet_standards([\"snippet.standards\"])", diagram);
        // A with parameter reads too.
        Assert.Contains("    extract -->|\"value\"| coverage", diagram);
        // A fanned node's read (its item's text reads the input) crosses the box from outside.
        Assert.True(diagram.IndexOf("    input_notes --> section_notes", StringComparison.Ordinal) > diagram.IndexOf("    end\n", StringComparison.Ordinal));
        // Each stadium once.
        Assert.Equal(1, diagram.Split("input_notes([").Length - 1);
    }

    [Fact]
    public void Diagram_At25_WithoutAWalk_DrawsTheDeclarationsAlone()
    {
        var (manifest, _) = V25Package.Minimal();

        var diagram = WorkflowDagDiagram.Generate(manifest);

        Assert.DoesNotContain("-->|\"value\"|", diagram);
        Assert.DoesNotContain("input_notes", diagram);
        Assert.Contains("-->|\"aggregate\"| all", diagram);
    }

    private static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Rich26()
    {
        var (m26, f26) = V26Package.Minimal();
        var files = new Dictionary<string, string>(f26, StringComparer.Ordinal)
        {
            ["prompts/summary.md"] = "Summary of {{ node.section_notes.content }} and {{ node.extract.value }}",
            ["prompts/wrap.md"] = "{{ for e in node.sections }}{{ e.content }}{{ end }} {{ snippet.x.content }} {{ node.extract.value }} {{ node.extract.value }} {{ node.extract.content }}",
            ["snippets/x.md"] = V26Package.Header("X") + "X: {{ node.scope.value }}",
        };
        var manifest = m26 with
        {
            Snippets = new Dictionary<string, string>(m26.Snippets!) { ["x"] = "snippets/x.md" },
            Nodes = m26.Nodes!
                .Select(n => n.Id == "sections" ? n with { Nodes = new List<string> { "section_notes", "section_summary" }, Ret = "node.section_summary" } : n)
                .Concat(new[]
                {
                    new WorkflowNodeSpec("section_summary", "Section summary", Prompt: "prompts/summary.md", Format: "prose"),
                    new WorkflowNodeSpec("wrap", "Wrap", Prompt: "prompts/wrap.md", Format: "prose"),
                })
                .ToList(),
        };
        return (manifest, files);
    }

    [Fact]
    public void Diagram_At26_DrawsABodySiblingRead_InsideTheLoopSubgraph_AndTheRestOutside()
    {
        var (manifest, files) = Rich26();

        var diagram = WorkflowDagDiagram.Generate(manifest, WalkOf(manifest, files));

        var start = diagram.IndexOf("subgraph for_sections", StringComparison.Ordinal);
        var end = diagram.IndexOf("    end\n", start, StringComparison.Ordinal);
        var sibling = diagram.IndexOf("        section_notes -->|\"content\"| section_summary", StringComparison.Ordinal);
        Assert.True(start < sibling && sibling < end, diagram);
        // A body node's read of a top-level node crosses the boundary, after the ret edge.
        var outside = diagram.IndexOf("    extract -->|\"value\"| section_summary", StringComparison.Ordinal);
        Assert.True(outside > diagram.IndexOf("section_summary -->|\"ret\"| sections", StringComparison.Ordinal), diagram);
    }

    [Fact]
    public void Diagram_At26_DrawsALoopElementRead_FromTheLoopBox_AndOneEdgePerSource()
    {
        var (manifest, files) = Rich26();

        var diagram = WorkflowDagDiagram.Generate(manifest, WalkOf(manifest, files));

        // {{ for e in node.sections }}{{ e.content }} is a read of the loop — never of its collection.
        Assert.Contains("    sections -->|\"value, content\"| wrap", diagram);
        Assert.DoesNotContain("snippet_sections -->|\"value", diagram);
        // Three reads of extract, two members, one edge.
        Assert.Contains("    extract -->|\"value, content\"| wrap", diagram);
        Assert.Equal(1, diagram.Split('\n').Count(line => line.StartsWith("    extract ", StringComparison.Ordinal) && line.EndsWith("| wrap", StringComparison.Ordinal)));
    }

    [Fact]
    public void Diagram_At26_ReadReachedThroughASnippet_SaysVia()
    {
        var (manifest, files) = Rich26();

        var diagram = WorkflowDagDiagram.Generate(manifest, WalkOf(manifest, files));

        // wrap reads snippet.x, whose text reads node.scope.value: the snippet's stadium, and the node's edge via it.
        Assert.Contains("snippet_x([\"snippet.x\"])", diagram);
        Assert.Contains("    snippet_x --> wrap", diagram);
        Assert.Contains("    scope -->|\"value via snippet.x\"| wrap", diagram);
    }

    [Fact]
    public void Diagram_At26_DrawsAGateOnlyRead_Dotted_AndTheCaseSubjectOnce()
    {
        var (m26, files) = V26Package.Minimal();

        var triage = V26JobStartTests.WithTriage(m26);
        var withIf = WorkflowDagDiagram.Generate(triage, WalkOf(triage, files));
        Assert.Contains("input_kind([\"input.kind\"])", withIf);
        Assert.Contains("    input_kind -.->|\"if\"| triage", withIf);
        // The body nodes' own reads cross their subgraphs from outside.
        Assert.Contains("    input_notes --> full_workup", withIf);

        var route = V26JobStartTests.WithRoute(m26, onNode: true);
        var withCase = WorkflowDagDiagram.Generate(route, WalkOf(route, files));
        // The subject draws once, dotted — ReadsOf leaves it to the case edge.
        Assert.Equal(1, withCase.Split('\n').Count(line => line.StartsWith("    scope ", StringComparison.Ordinal) && line.EndsWith("| route", StringComparison.Ordinal)));
        Assert.Contains("    scope -.->|\"case\"| route", withCase);
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

    // ----- #1259: package-authored text in labels is escaped (the run diagram's #1247, here) -----

    private static IEnumerable<string> LabelLines(string diagram) =>
        diagram.Split('\n').Where(line => line.Contains("[\"", StringComparison.Ordinal) || line.Contains("{{\"", StringComparison.Ordinal) || line.Contains("|\"", StringComparison.Ordinal));

    [Fact]
    public void Diagram_At27_AQuotedCondition_AndAQuotedLabel_DrawAsEntities_NoLabelHoldsARawQuote()
    {
        // The Editor's Graph pane on the first 27 package: the inner if's condition carried
        // quotes and the whole graph failed to parse ("Parse error on line 54").
        var (manifest, _) = V27Package.WithTriageInLoop(V27Package.Minimal(), condition: "item.id != \"billing\" || input.include_billing == true");
        manifest = V26Package.WithNode(manifest, "patient_draft", n => n with { Label = "Holding the \"billing\" back" });

        var diagram = WorkflowDagDiagram.Generate(manifest);

        Assert.Contains("triage[\"triage<br/>Triage<br/>if item.id != #quot;billing#quot; || input.include_billing == true\"]", diagram);
        Assert.Contains("patient_draft[\"patient_draft<br/>Holding the #quot;billing#quot; back", diagram);
        Assert.DoesNotContain("\"billing\"", diagram);
        foreach (var line in LabelLines(diagram))
        {
            Assert.True(line.Count(c => c == '"') % 2 == 0 && !line.Contains("\"\"", StringComparison.Ordinal), line);
        }
    }

    [Fact]
    public void Diagram_Below25_AnUnparseableWhenWithAQuote_DrawsItsRawTextEscaped()
    {
        // Drafts reach the diagram before the validator does; a when the grammar refuses is drawn raw — escaped.
        var manifest = V8Fixtures.Conditional();
        // The v8 fixture carries its deliverables as the ≤24 `results` list.
        manifest = manifest with
        {
            Results = manifest.Results!.Select((d, i) => i == 0 ? d with { When = "input.kind ~ \"new\"" } : d).ToList(),
        };

        var diagram = WorkflowDagDiagram.Generate(manifest);

        Assert.Contains("-.->|\"when input.kind ~ #quot;new#quot;\"|", diagram);
        Assert.DoesNotContain("~ \"new\"", diagram);
    }

    // ----- #1244: the migration script writes the diagram --dag draws — with the walk -----

    [Fact]
    public void Diagram_OfARaisedPackage_WithTheWalk_IsTheOriginalsDiagram_AndCarriesTheReads()
    {
        // consultologist-workflows#49: --to 27 --write over the seven 26 packages dropped every
        // read edge because the script drew the declarations alone. With the walk, a raise
        // leaves dag.mmd byte for byte as it was — 27 permits what 26 refused and draws nothing new.
        var (manifest, files) = V26Package.Minimal();
        var raised = WorkflowPackageVersionRaise.To(JsonSerializer.Serialize(manifest, WorkflowPackageManifestJson.WriteOptions), files, 27);
        var at27 = WorkflowPackageManifestJson.Read(raised.ManifestJson, "raised", V27Package.Accepting27.ToList());
        Assert.Equal(27, at27.SpecVersion);

        var before = WorkflowDagDiagram.Generate(manifest, WalkOf(manifest, files));
        var after = WorkflowDagDiagram.Generate(at27, WalkOf(at27, raised.Files));

        Assert.Equal(before, after);
        // The walk is what the declarations alone lack: the input stadium and a read edge.
        Assert.Contains("input_notes([\"input.notes\"])", after);
        Assert.Contains("-->|\"", after);
        Assert.NotEqual(WorkflowDagDiagram.Generate(at27), after);
    }

    [Fact]
    public void Diagram_OfA25To26Migration_WithTheWalk_DrawsTheReads()
    {
        var (manifest, files) = V25Package.Minimal();
        var migrated = WorkflowPackageMigration25To26.Migrate(JsonSerializer.Serialize(manifest, WorkflowPackageManifestJson.WriteOptions), files);
        var at26 = WorkflowPackageManifestJson.Read(migrated.ManifestJson, "migrated", V26Package.Accepting26.ToList());

        var withWalk = WorkflowDagDiagram.Generate(at26, WalkOf(at26, migrated.Files));
        var declarationsAlone = WorkflowDagDiagram.Generate(at26);

        Assert.Contains("input_notes([\"input.notes\"])", withWalk);
        Assert.Contains("-->|\"", withWalk);
        Assert.DoesNotContain("input_notes([", declarationsAlone);
    }
}
