using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1234 (package-format-v27-design.md § 1, § 3, § 4): a flow node inside a
/// for at 27 — the shape (an if/case in the loop's nodes[], attached once
/// through the flow node, the loop returning it, one level), what the inner
/// condition may read (the item, inputs, earlier body nodes; never the loop,
/// never its own branch nodes), a case over the item's enum field, what the
/// outside reads of a loop returning a flow node, and the sentences of § 3 —
/// each at the seam (<see cref="V27Package.Accepting27"/>) until the bump.
/// </summary>
public class WorkflowV27FlowTests
{
    private static readonly (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Triaged = V27Package.WithTriageInLoop(V27Package.Minimal());

    private static List<string> Errors((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle) => V27Package.Errors(bundle);

    // ----- § 1.1: the shape -------------------------------------------------------

    [Fact]
    public void ALoop_MayReturnAnIf_WhoseBranchNodesRunPerItem()
    {
        // The then reads the item and the loop's section_notes one level up.
        Assert.Empty(Errors(Triaged));
    }

    [Fact]
    public void ALoop_MayReturnAPlainNode_BesideAGatedBranch_ReadByBareId()
    {
        var bundle = V27Package.With(Triaged,
            m => V26Package.WithNode(V26Package.WithNodes(m, new WorkflowNodeSpec("summary", "Summary", Prompt: "prompts/summary.md", Format: "prose")),
                "sections", n => n with { Nodes = new List<string> { "section_notes", "triage", "summary" }, Ret = "node.summary" }),
            files => files["prompts/summary.md"] = "Summary of {{ node.triage.content }} after {{ node.section_notes.content }}.");

        Assert.Empty(Errors(bundle));
    }

    [Fact]
    public void ABranchNode_ListedInTheLoopToo_IsAttachedOnce()
    {
        var errors = Errors(V27Package.With(Triaged, m => V26Package.WithNode(m, "sections", n => n with { Nodes = new List<string> { "section_notes", "triage", "patient_draft" } })));

        Assert.Contains("Node 'patient_draft' is attached to 'triage' and to loop 'sections'; a branch node is attached once, through its flow node.", errors);
    }

    [Fact]
    public void ABranchNode_InTwoFlowNodesOfOneLoop_RunsInOneBody()
    {
        var errors = Errors(V27Package.With(Triaged, m => V26Package.WithNode(V26Package.WithNodes(m,
                new WorkflowNodeSpec("route", "Route", If: "item.id == \"plan\"", Then: V27Package.Body("node.patient_draft", "patient_draft"))),
            "sections", n => n with { Nodes = new List<string> { "section_notes", "triage", "route" } })));

        Assert.Contains("Node 'patient_draft' runs inside node 'triage' and inside node 'route'; a node runs in one body.", errors);
    }

    [Fact]
    public void AnIfInsideAnIf_InsideAFor_IsOneLevel()
    {
        var errors = Errors(V27Package.With(Triaged, m => V26Package.WithNode(V26Package.WithNodes(m,
                new WorkflowNodeSpec("detail", "Detail", If: "item.id == \"plan\"", Then: V27Package.Body("node.table_draft", "table_draft"))),
            "triage", n => n with { Then = V27Package.Body("node.patient_draft", "patient_draft", "detail") })));

        Assert.Contains("Node 'detail' runs inside node 'triage', which runs inside loop 'sections'; at specVersion 27 a branch holds plain nodes.", errors);
    }

    [Fact]
    public void AnIfInsideAnIf_AtTheTopLevel_StaysRefused()
    {
        // § 1.1: nobody asked; the top-level case keeps its refusal, in 27's words.
        var errors = V27Package.Errors(b => V27Package.With(b, m => V26Package.WithNodes(m,
            new WorkflowNodeSpec("full_workup", "Full work-up", Prompt: "prompts/extract.md", Format: "prose"),
            new WorkflowNodeSpec("detail", "Detail", If: "input.kind == \"new\"", Then: V27Package.Body("node.full_workup", "full_workup")),
            new WorkflowNodeSpec("triage", "Triage", If: "input.kind == \"new\"", Then: V27Package.Body("node.detail", "detail")))));

        Assert.Contains("Node 'detail' runs inside node 'triage'; at specVersion 27 a branch holds plain nodes.", errors);
    }

    [Fact]
    public void AForInsideAnInnerIf_WaitsForTheSchedulerRedesign()
    {
        var errors = Errors(V27Package.With(Triaged, m => V26Package.WithNode(V26Package.WithNodes(m,
                new WorkflowNodeSpec("inner", "Inner", For: "item in snippet.sections", Nodes: new List<string> { "table_draft" }, Ret: "node.table_draft")),
            "triage", n => n with { Then = V27Package.Body("node.inner", "inner") })));

        Assert.Contains("Loop 'inner' runs inside node 'triage'; nesting waits for the scheduler redesign.", errors);
    }

    // ----- § 1.2: what the condition may read ----------------------------------------

    [Fact]
    public void TheCondition_ReadsTheItem_Inputs_AndAnEarlierBodyNode()
    {
        Assert.Empty(Errors(V27Package.WithTriageInLoop(V27Package.Minimal(), "item.id == \"hpi\" && input.kind == \"new\"")));
        Assert.Empty(Errors(V27Package.WithTriageInLoop(V27Package.Minimal(), "node.section_notes.content != \"\"")));
        Assert.Empty(Errors(V27Package.WithTriageInLoop(V27Package.Minimal(), "node.scope.value == \"in_scope\"")));
    }

    [Fact]
    public void TheCondition_ReadingItsLoop_IsNamed()
    {
        var errors = Errors(V27Package.WithTriageInLoop(V27Package.Minimal(), "node.sections.content != \"\""));

        Assert.Contains("Node 'triage' if reads node.sections, the loop it runs in; a condition inside a loop reads the item, inputs and earlier body nodes.", errors);
    }

    [Fact]
    public void TheCondition_ReadingItsOwnBranchNode_IsNamed()
    {
        var errors = Errors(V27Package.WithTriageInLoop(V27Package.Minimal(), "node.patient_draft.content != \"\""));

        Assert.Contains("Node 'triage' if reads node 'patient_draft', which 'triage' decides; a condition reads what runs before it.", errors);
    }

    [Fact]
    public void TheCondition_ReadingAnotherLoopsBodyNode_IsScoped()
    {
        var errors = Errors(V27Package.With(V27Package.WithTriageInLoop(V27Package.Minimal(), "node.other_notes.content != \"\""),
            m => V26Package.WithNodes(m,
                new WorkflowNodeSpec("other_notes", "Other notes", Prompt: "prompts/section.md", Format: "prose"),
                new WorkflowNodeSpec("other", "Other", For: "item in snippet.sections", Nodes: new List<string> { "other_notes" }, Ret: "node.other_notes"))));

        Assert.Contains("Node 'triage' if reads node 'other_notes', which runs inside loop 'other'; read node.other.", errors);
    }

    [Fact]
    public void ALoopSibling_ReadingABranchNode_ReadsTheFlowNode()
    {
        // Never down: the outside of a branch reads its flow node.
        var errors = Errors(V27Package.With(Triaged, files: files => files["prompts/section.md"] = "{{ item.content }} {{ node.patient_draft.content }}"));

        Assert.Contains("Node 'section_notes' prompt reads node 'patient_draft', which runs inside node 'triage'; read node.triage.", errors);
    }

    [Fact]
    public void ABranchNode_ReadingItsLoop_IsNamed()
    {
        var errors = Errors(V27Package.With(Triaged, files: files => files["prompts/patient.md"] = "{{ node.sections.content }}"));

        Assert.Contains("Node 'patient_draft' reads node.sections, the loop it runs in; a body node reads its siblings, the outside reads the loop.", errors);
    }

    // ----- a case over the item ---------------------------------------------------

    [Fact]
    public void ALoop_MayReturnACase_OnTheItemsEnumField()
    {
        Assert.Empty(Errors(V27Package.WithRouteInLoop(V27Package.Minimal())));
    }

    [Fact]
    public void ACase_OnATextFieldOfTheItem_OrAFieldItDoesNotCarry_IsRefused()
    {
        const string rule = "a case switches on an enum input (input.<id>), an enum-format node's value (node.<id>.value) or, inside a loop over an array input, an enum field of the item (item.value.<field>)";

        Assert.Contains($"Node 'route' switches on item.value.title, which is a text; {rule}.", Errors(V27Package.WithRouteInLoop(V27Package.Minimal(), "item.value.title")));
        Assert.Contains($"Node 'route' switches on item.value.nope, which the item does not carry (fields: kind, title); {rule}.", Errors(V27Package.WithRouteInLoop(V27Package.Minimal(), "item.value.nope")));
    }

    [Fact]
    public void ACase_OnAFolderItemsField_IsRefused()
    {
        const string rule = "a case switches on an enum input (input.<id>), an enum-format node's value (node.<id>.value) or, inside a loop over an array input, an enum field of the item (item.value.<field>)";
        var errors = Errors(V27Package.With(V27Package.Minimal(), m => V26Package.WithNode(V26Package.WithNodes(m,
                new WorkflowNodeSpec("route", "Route", Case: "item.value.kind", Branches: new List<WorkflowCaseBranchSpec> { new(new List<string> { "narrative" }, new List<string> { "section_notes" }, "node.section_notes") })),
            "sections", n => n with { Nodes = new List<string> { "route" }, Ret = "node.route" })));

        Assert.Contains($"Node 'route' switches on item.value.kind, but a folder item's fields are text; {rule}.", errors);
    }

    [Fact]
    public void ABranchValue_IsOneOfTheItemsFieldValues()
    {
        var errors = Errors(V27Package.WithRouteInLoop(V27Package.Minimal(), "item.value.kind",
            new WorkflowCaseBranchSpec(new List<string> { "narrative" }, new List<string> { "plan" }, "node.plan"),
            new WorkflowCaseBranchSpec(new List<string> { "maybe" }, new List<string> { "letter" }, "node.letter")));

        Assert.Contains("Node 'route' branch 2 when 'maybe' is a value item.value.kind does not declare (values: narrative, table).", errors);
    }

    // ----- § 1.5: what the outside reads ---------------------------------------------

    [Fact]
    public void TheOutside_IteratesTheLoop_AndTheProbeRenders()
    {
        var bundle = V27Package.With(Triaged, files: files => files["prompts/assessment.md"] =
            "Assess.\n{{ for e in node.sections }}{{ e.item.label }}: {{ e.content }} / {{ e.triage.content }} / {{ e.section_notes.content }}{{ end }}\n{{ node.sections.content }}");

        Assert.Empty(Errors(bundle));
    }

    [Fact]
    public void ALoopElement_CarriesSkipped_At27_AndNotAt26()
    {
        // #1235 (v27 design § 1.4): e.skipped says whether an inner if/case skipped the item; below 27 the element's set has no such member.
        const string read = "Assess.\n{{ for e in node.sections }}{{ if e.skipped }}(skipped){{ else }}{{ e.content }}{{ end }}{{ end }}";

        Assert.Empty(Errors(V27Package.With(Triaged, files: files => files["prompts/assessment.md"] = read)));
        Assert.Contains("Node 'assessment' prompt reads e.skipped, which is not a member (a loop element has item, content, value and the body nodes: section_notes).",
            V26Package.FlowErrors(m => m, files => files["prompts/assessment.md"] = read));
    }

    [Fact]
    public void TheOutside_ReadingTheInnerFlow_ReadsTheLoop()
    {
        var errors = Errors(V27Package.With(Triaged, files: files => files["prompts/assessment.md"] = "Assess. {{ node.triage.content }}"));

        Assert.Contains("Node 'assessment' prompt reads node 'triage', which runs inside loop 'sections'; read node.sections.", errors);
    }

    [Fact]
    public void TheLoopsValue_OverDisagreeingBranches_IsRefusedByName_AndTheDeliverableNamesTheBranch()
    {
        var bundle = V27Package.With(
            V27Package.WithTriageInLoop(V27Package.Minimal(), then: V27Package.Body("node.full_extract", "full_extract")),
            m => V26Package.WithNodes(m, new WorkflowNodeSpec("full_extract", "Full extract", Prompt: "prompts/full_extract.md", Format: "concept_list", OnInvalid: WorkflowOnInvalid.Fail)),
            files =>
            {
                files["prompts/full_extract.md"] = "Extract from {{ item.content }}.";
                files["prompts/assessment.md"] = "Assess. {{ node.sections.content }} {{ node.sections.value }} {{ for e in node.sections }}{{ e.value }}{{ end }}";
            });
        var errors = Errors(bundle);

        Assert.Contains("Node 'assessment' prompt reads node.sections.value, but loop 'sections' returns 'triage', whose branches return 'object' and 'text'; node.sections.value needs one format — read node.sections.content.", errors);
        Assert.Contains("Node 'assessment' prompt reads e.value, but loop 'sections' returns 'triage', whose branches return 'object' and 'text'; e.value needs one format — read e.content.", errors);
        Assert.Contains("Deliverable 'consult' source node.sections loops over 'triage', whose then returns 'full_extract' of format 'concept_list'; a deliverable composes prose.", errors);
    }

    [Fact]
    public void TheInnerFlow_NeitherReturnedNorRead_IsUnreachable()
    {
        var errors = Errors(V27Package.WithTriageInLoop(V27Package.Minimal(), ret: "node.section_notes"));

        Assert.Contains("Node 'triage' in loop 'sections' is neither returned nor read; name it with ret or read it from a body node.", errors);
    }

    [Fact]
    public void TheLoopsShape_IsTheBranchShape_AndTheInnerFlowRunsUnderTheLoopsFan()
    {
        var (manifest, files) = Triaged;
        var nodes = WorkflowDerivedEdges.PrepareNodes(manifest, files, V25Package.Catalog(), out _, out _);

        var loop = nodes.Single(n => n.Node.Id == "sections");
        Assert.Equal(TemplateFlowKind.If, loop.Shape!.Flow!.Ret!.Flow!.Kind);
        Assert.Equal("triage", loop.Shape.Flow.RetId);
        Assert.Contains("triage", loop.Shape.Flow.Body!.Keys);

        Assert.IsType<SnippetFolderFan>(nodes.Single(n => n.Node.Id == "triage").Fan);
        Assert.IsType<SnippetFolderFan>(nodes.Single(n => n.Node.Id == "patient_draft").Fan);
        Assert.Equal("sections", nodes.Single(n => n.Node.Id == "triage").Scope);
        Assert.Equal("triage", nodes.Single(n => n.Node.Id == "patient_draft").Scope);
    }
}
