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
    public void A25Node_DrawsItsReads_AndNamesItsFormat()
    {
        // B7 #1006: at 25 the edges are the derived reads (no bindings), and
        // the label carries the format and producer.
        var diagram = RunDagDiagram.Build(Job(
            nodes: new ConsultGenerationNodeDescriptor[]
            {
                new("extract", "Extracting", "p", Format: "concept_list", Producer: "attested"),
                new("assessment", "Assessing", "p", Format: "prose", Producer: "attested", Reads: new[] { "extract" }),
            },
            outputs: new Dictionary<string, ConsultGenerationNodeStatus>
            {
                ["extract"] = Row("extract", "Completed"),
                ["assessment"] = Row("assessment", "Running"),
            }));

        Assert.Contains("extract --> assessment", diagram);
        Assert.Contains("prose · attested", diagram);
    }

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
        Assert.Contains("stroke:#0078d4", diagram);
        Assert.Contains("stroke-dasharray:4 3", diagram);
        Assert.Contains("opacity:0.6", diagram);
        // Fills must be hex, not rgba(): Mermaid's classDef splits styles on
        // commas, so rgba(...) is a parse error that fails the whole diagram.
        Assert.DoesNotContain("rgba", diagram);
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

    // ----- v26 (#1208): a storage-8 record's flow rows -----

    private static ConsultGenerationNodeDescriptor[] Storage8Nodes() => new ConsultGenerationNodeDescriptor[]
    {
        new("extract", "Extract", Format: "concept_list", Producer: "attested", Prompt: "prompts/extract.md"),
        new("scope", "Scope", Format: "classification", Producer: "attested", Prompt: "prompts/scope.md"),
        new("triage", "Triage", Kind: "if", Expression: "input.kind == \"new\"", Body: new[] { "full_workup", "brief_update" }, Ret: "node.brief_update", Branch: "else"),
        new("brief_update", "Brief update", Format: "prose", Producer: "attested", Prompt: "prompts/brief.md", Reads: new[] { "extract" }),
        new("route", "Route", Kind: "case", Expression: "node.scope.value", Body: new[] { "meds" }, Ret: "node.meds", Branch: "in_scope"),
        new("meds", "Medications", Format: "medication_list", Producer: "attested", Prompt: "prompts/meds.md"),
        new("sections", "Sections", Kind: "for", Collection: "snippet.sections", Body: new[] { "section_notes" }, Ret: "node.section_notes", Count: 2),
        new("section_notes", "Section notes", Format: "prose", Producer: "attested", Prompt: "prompts/section.md", In: "sections"),
    };

    private static Dictionary<string, ConsultGenerationNodeStatus> Storage8Outputs() => new()
    {
        ["extract"] = Row("extract", "Completed"),
        ["scope"] = Row("scope", "Completed"),
        ["triage"] = Row("triage", "Completed"),
        ["brief_update"] = Row("brief_update", "Completed"),
        ["route"] = Row("route", "Completed"),
        ["meds"] = Row("meds", "Completed"),
        ["sections"] = Row("sections", "Completed"),
        ["section_notes:hpi"] = Row("section_notes", "Completed"),
        ["section_notes:plan"] = Row("section_notes", "Running"),
    };

    [Fact]
    public void AStorage8Loop_DrawsAsTheFan_WithItsCountAndRet()
    {
        var diagram = RunDagDiagram.Build(Job(nodes: Storage8Nodes(), outputs: Storage8Outputs()));

        Assert.Contains("subgraph for_sections[\"sections: per snippet.sections item (1/2)\"]", diagram);
        // The body node sits inside the subgraph, tallied through its loop, never at the top level.
        var inside = diagram[(diagram.IndexOf("subgraph for_sections", StringComparison.Ordinal))..];
        inside = inside[..inside.IndexOf("end\n", StringComparison.Ordinal)];
        Assert.Contains("section_notes[\"section_notes<br/>Section notes<br/>(1/2) items<br/>prose · attested<br/>prompts/section.md\"]", inside);
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(diagram, @"\nsection_notes\[").Count);
        Assert.Contains("src_snippet_sections([\"snippet.sections\"])", diagram);
        Assert.Contains("src_snippet_sections -->|\"for\"| for_sections", diagram);
        Assert.Contains("sections[\"sections<br/>Sections<br/>for item in snippet.sections\"]", diagram);
        Assert.Contains("section_notes --> |\"ret\"| sections", diagram);
        Assert.Matches(@"class [^\n]*\bsection_notes\b[^\n]* running", diagram);
    }

    [Fact]
    public void AStorage8If_DrawsItsBranch_AndThePrunedBodyDashed()
    {
        var diagram = RunDagDiagram.Build(Job(nodes: Storage8Nodes(), outputs: Storage8Outputs()));

        Assert.Contains("subgraph if_triage[\"triage: if input.kind == #quot;new#quot; — branch: else\"]", diagram);
        Assert.Contains("full_workup[\"full_workup<br/>pruned\"]", diagram);
        Assert.Matches(@"class [^\n]*\bfull_workup\b[^\n]* skipped", diagram);
        Assert.Contains("triage{\"triage<br/>Triage<br/>if: input.kind == #quot;new#quot;<br/>branch: else\"}", diagram);
        Assert.Contains("brief_update --> |\"ret\"| triage", diagram);
        Assert.Contains("extract --> brief_update", diagram);
    }

    [Fact]
    public void AStorage8Case_DrawsTheDottedEdgeFromItsSubject()
    {
        var diagram = RunDagDiagram.Build(Job(nodes: Storage8Nodes(), outputs: Storage8Outputs()));

        Assert.Contains("subgraph case_route[\"route: case node.scope.value — branch: in_scope\"]", diagram);
        Assert.Contains("scope -.->|\"case\"| route", diagram);
        Assert.Contains("meds --> |\"ret\"| route", diagram);
    }

    [Fact]
    public void AStorage8Flow_ThatTookNoBranch_IsSkipped_AndNamesNone()
    {
        var outputs = Storage8Outputs();
        outputs["triage"] = Row("triage", "Skipped");
        outputs.Remove("brief_update");
        var nodes = Storage8Nodes().Select(n => n.Id == "triage" ? n with { Ret = null, Branch = null } : n).ToArray();

        var diagram = RunDagDiagram.Build(Job(nodes: nodes, outputs: outputs));

        Assert.Contains("branch: none", diagram);
        Assert.Matches(@"class [^\n]*\btriage\b[^\n]* skipped", diagram);
        // Both bodies are pruned: neither ran.
        Assert.Contains("brief_update[\"brief_update<br/>pruned\"]", diagram);
        Assert.DoesNotContain("|\"ret\"| triage", diagram);
    }

    [Fact]
    public void AStorage8Document_DrawsItsSources()
    {
        var diagram = RunDagDiagram.Build(Job(
            nodes: Storage8Nodes(),
            outputs: Storage8Outputs(),
            documents: new[] { new ConsultGenerationResultDocumentResponse("consult", "Consultation note", "…", Sources: new[] { "node.triage", "node.sections" }) }));

        Assert.Contains("triage -->|\"source\"| result_consult", diagram);
        Assert.Contains("sections -->|\"source\"| result_consult", diagram);
    }

    [Fact]
    public void A25Record_WithTheStorage8FieldsNull_DrawsTheSameBytes()
    {
        var nodes = new ConsultGenerationNodeDescriptor[]
        {
            new("extract", "Extracting", "p", Format: "concept_list", Producer: "attested"),
            new("section", "Sectioning", "p", ForEach: "snippet.standards", Format: "prose", Producer: "attested", Reads: new[] { "extract" }),
        };
        var outputs = new Dictionary<string, ConsultGenerationNodeStatus> { ["extract"] = Row("extract", "Completed"), ["section:hpi"] = Row("section", "Completed") };

        var before = RunDagDiagram.Build(Job(nodes: nodes, outputs: outputs));
        var again = RunDagDiagram.Build(Job(nodes: nodes.Select(n => n with { Kind = null, In = null, Prompt = null, Count = null }).ToArray(), outputs: outputs));

        Assert.Equal(before, again);
        Assert.DoesNotContain("subgraph", before);
        Assert.Contains("per snippet.standards item (1/1)", before);
    }

    // ----- v27 (#1236): a storage-9 record's inner if/case -----

    private static ConsultGenerationNodeDescriptor[] Storage9Nodes() => new ConsultGenerationNodeDescriptor[]
    {
        new("sections", "Sections", Kind: "for", Collection: "snippet.sections", Body: new[] { "section_notes", "triage" }, Ret: "triage", Count: 3),
        new("section_notes", "Section notes", Format: "prose", Producer: "attested", Prompt: "prompts/section.md", In: "sections"),
        new("triage", "Triage", Kind: "if", Expression: "item.id == \"hpi\"", Body: new[] { "patient_draft", "table_draft" }, In: "sections",
            Branches: new Dictionary<string, string> { ["hpi"] = "then", ["plan"] = "none", ["exam"] = "else" }),
        new("patient_draft", "Patient draft", Format: "prose", Producer: "attested", Prompt: "prompts/patient.md", In: "triage", Reads: new[] { "section_notes" }),
        new("table_draft", "Table draft", Format: "prose", Producer: "attested", Prompt: "prompts/table.md", In: "triage"),
    };

    private static Dictionary<string, ConsultGenerationNodeStatus> Storage9Outputs() => new()
    {
        ["sections"] = Row("sections", "Completed"),
        ["section_notes:hpi"] = Row("section_notes", "Completed"),
        ["section_notes:plan"] = Row("section_notes", "Completed"),
        ["section_notes:exam"] = Row("section_notes", "Completed"),
        ["triage:plan"] = new("triage", "Triage", "Skipped", null, null, null, null, Reason: "needs `item.id == \"hpi\"`; it does not hold"),
        ["patient_draft:hpi"] = Row("patient_draft", "Completed"),
        ["table_draft:exam"] = Row("table_draft", "Completed"),
    };

    [Fact]
    public void AStorage9InnerIf_IsASubgraphInsideTheLoops_WithItsBranchesTallied_AndNothingPruned()
    {
        var diagram = RunDagDiagram.Build(Job(nodes: Storage9Nodes(), outputs: Storage9Outputs()));

        // The loop's tally is its ret's — the inner if, three items decided.
        Assert.Contains("subgraph for_sections[\"sections: per snippet.sections item (3/3)\"]", diagram);
        var loop = diagram[diagram.IndexOf("subgraph for_sections", StringComparison.Ordinal)..];
        Assert.Contains("subgraph if_triage[\"triage: if item.id == #quot;hpi#quot; — per item\"]", loop);
        Assert.True(loop.IndexOf("subgraph if_triage", StringComparison.Ordinal) < loop.IndexOf("end\n", StringComparison.Ordinal), "the inner subgraph opens inside the loop's");
        Assert.Contains("patient_draft[\"patient_draft<br/>Patient draft<br/>(1/1) items<br/>prose · attested<br/>prompts/patient.md\"]", diagram);
        Assert.Contains("table_draft[\"table_draft<br/>Table draft<br/>(1/1) items<br/>prose · attested<br/>prompts/table.md\"]", diagram);
        Assert.Contains("triage{\"triage<br/>Triage<br/>if: item.id == #quot;hpi#quot;<br/>then 1 · none 1 · else 1\"}", diagram);
        Assert.Contains("triage --> |\"ret\"| sections", diagram);
        Assert.DoesNotContain("pruned", diagram);
        Assert.DoesNotContain("branch: none", diagram);
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(diagram, @"\ntriage\{").Count);
    }

    [Fact]
    public void AnInnerFlowsSkippedItems_AreOneDashedBox_InsideItsSubgraph_NamedByItem_WithTheReason()
    {
        // The rail's ⊘ rows and History's struck-through rows, drawn: without this the diagram
        // showed a skipped item only as a number in the diamond's tally.
        var rosters = new[] { new ConsultCollectionRoster("sections", new[] { new ConsultCollectionItem("hpi", "HPI"), new ConsultCollectionItem("plan", "Plan"), new ConsultCollectionItem("exam", "Exam") }) };

        var diagram = RunDagDiagram.Build(Job(nodes: Storage9Nodes(), outputs: Storage9Outputs(), collections: rosters));

        var inner = diagram[diagram.IndexOf("subgraph if_triage", StringComparison.Ordinal)..];
        Assert.Contains("triage_skipped[\"skipped (1)<br/>Plan<br/>needs `item.id == #quot;hpi#quot;`; it does not hold\"]", inner);
        Assert.True(inner.IndexOf("triage_skipped[", StringComparison.Ordinal) < inner.IndexOf("end\n", StringComparison.Ordinal), "the box sits inside the inner flow's subgraph");
        Assert.Contains("triage -.->|\"none\"| triage_skipped", diagram);
        Assert.Matches(@"\nclass [^\n]*\btriage_skipped\b[^\n]* skipped", diagram);
        // A flow that skipped nothing draws no box.
        var outputs = Storage9Outputs();
        outputs.Remove("triage:plan");
        Assert.DoesNotContain("triage_skipped", RunDagDiagram.Build(Job(nodes: Storage9Nodes(), outputs: outputs, collections: rosters)));
    }

    [Fact]
    public void ADocumentsSources_DrawTheirEdges_FromTheBareIdsTheRecordServes()
    {
        // The served row names a source by its bare id; resolving it only through node.<id>
        // left every deliverable hexagon floating (seen on the 27 smoke).
        var documents = new[]
        {
            new ConsultGenerationResultDocumentResponse("consult", "Consult note", "The note.", Sources: new[] { "sections", "node.section_notes" }),
        };

        var diagram = RunDagDiagram.Build(Job(nodes: Storage9Nodes(), outputs: Storage9Outputs(), documents: documents));

        Assert.Contains("sections -->|\"source\"| result_consult", diagram);
        Assert.Contains("section_notes -->|\"source\"| result_consult", diagram);
    }

    [Fact]
    public void PackageAuthoredText_InALabel_EscapesItsQuotes_SoTheDiagramParses()
    {
        // #1247: an if/case expression carries quotes (`item.value.kind == "narrative"`); raw inside
        // a Mermaid quoted label they closed it early and the whole diagram failed to render
        // ("Parse error on line 14") — first seen live on the 27 smoke. Mermaid's entity is #quot;.
        var nodes = new ConsultGenerationNodeDescriptor[]
        {
            new("sections", "Sections", Kind: "for", Collection: "input.sections", Body: new[] { "route" }, Ret: "route", Count: 2),
            new("route", "Routing a \"section\"", Kind: "case", Expression: "item.value.kind", Body: new[] { "narrative_draft" }, In: "sections",
                Branches: new Dictionary<string, string> { ["0"] = "narrative", ["1"] = "none" }),
            new("narrative_draft", "Drafting the \"narrative\"", In: "route", Prompt: "prompts/narrative-section.md"),
        };
        var outputs = new Dictionary<string, ConsultGenerationNodeStatus>
        {
            ["sections"] = new("sections", "Sections", "Completed", null, null, null, null),
            ["route"] = new("route", "Routing", "Completed", null, null, null, null),
            ["route:1"] = new("route", "Routing", "Skipped", null, null, null, null, Reason: "switches on `item.value.kind`; no branch lists its value"),
            ["narrative_draft"] = new("narrative_draft", "Drafting", "Completed", null, null, null, null),
            ["narrative_draft:0"] = new("narrative_draft", "Drafting", "Completed", "i", "o", null, null),
        };

        var diagram = RunDagDiagram.Build(Job(nodes: nodes, outputs: outputs, collections: new[] { new ConsultCollectionRoster("input.sections", new[] { new ConsultCollectionItem("0", "Sections to produce 1"), new ConsultCollectionItem("1", "Sections to produce 2") }) }));

        Assert.Contains("route{\"route<br/>Routing a #quot;section#quot;<br/>case: item.value.kind<br/>narrative 1 · none 1\"}", diagram);
        Assert.Contains("narrative_draft[\"narrative_draft<br/>Drafting the #quot;narrative#quot;<br/>", diagram);
        // No label holds a raw quote: every `"` in the diagram opens or closes a label or an edge text.
        foreach (var line in diagram.Split('\n').Where(l => l.Contains('[', StringComparison.Ordinal) || l.Contains('{', StringComparison.Ordinal)))
        {
            Assert.True(line.Count(c => c == '"') % 2 == 0 && !line.Contains("\"\"", StringComparison.Ordinal) || line.Contains("-->", StringComparison.Ordinal), line);
        }
    }
}
