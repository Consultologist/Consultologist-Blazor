using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1154 (package-format-v26-design.md § 1.3), PR 3 — the 26 render: a loop
/// iterates with its item, the returned node's content and value and every
/// body node by id, and reads as its composed text and as the array of values;
/// a flow node that took no branch reads null under a guard; an if/case reads
/// as its returned node, typed; the item's `label` at 26; the orchestrator's
/// half — the loop payload and the returned nodes — from the snapshot.
/// </summary>
public class V26RenderTests
{
    private static readonly TemplateRunFacts Run = new("2026-10-26", "abcdef12", "general@v2026.10.1", "api.example.test");
    private static readonly TemplateProfileFacts Profile = new("Dr Example", "Dr Example, FRCPC");
    private static readonly Dictionary<string, string> Inputs = new(StringComparer.Ordinal) { ["notes"] = "Referral.", ["kind"] = "new" };

    private static ConsultPromptNodeActivityInput Input(
        string nodeId,
        string promptId,
        IReadOnlyDictionary<string, string>? nodeTexts = null,
        IReadOnlyDictionary<string, string>? item = null,
        string? forEach = null,
        IReadOnlyDictionary<string, ConsultLoopPayload>? loops = null,
        IReadOnlyDictionary<string, string>? flowRets = null,
        IReadOnlyList<string>? skipped = null) =>
        new(nodeId, promptId, Inputs, "general@v2026.10.1", Format: "prose", Producer: "attested",
            NodeTexts: nodeTexts ?? new Dictionary<string, string>(), Item: item, ForEach: forEach, Run: Run, Profile: Profile,
            Loops: loops, FlowRets: flowRets, SkippedNodes: skipped);

    private const string Composed = "## HPI\n\nThe history.\n\n## Plan\n\nThe plan.";

    private static readonly IReadOnlyList<IReadOnlyDictionary<string, string>> Items = new[]
    {
        (IReadOnlyDictionary<string, string>)new Dictionary<string, string> { ["id"] = "hpi", ["label"] = "HPI", ["content"] = "History: {{ input.notes }}" },
        new Dictionary<string, string> { ["id"] = "plan", ["label"] = "Plan", ["content"] = "Plan." },
    };

    private static ConsultLoopPayload Sections() =>
        new("snippet.sections", "section_notes", Items, new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["section_notes"] = new Dictionary<string, string> { ["hpi"] = "The history.", ["plan"] = "The plan." },
        });

    /// <summary>The minimal with a prose node `summary` reading the loop whole, sourced by the deliverable — valid at 26, so the probe saw the same members the render exposes.</summary>
    private static WorkflowPackage WithSummary(string prompt) =>
        V26JobStartTests.Package(
            m => V26JobStartTests.Sourcing(V26Package.WithNodes(m, new WorkflowNodeSpec("summary", "Summary", Prompt: "prompts/summary.md", Format: "prose")), "node.summary"),
            files => files["prompts/summary.md"] = prompt);

    [Fact]
    public void ALoop_Iterates_WithItsItem_TheReturnedNodesContent_AndEveryBodyNode_AndReadsAsItsText()
    {
        const string prompt = "{{ for e in node.sections }}{{ e.item.id }}/{{ e.item.label }}|{{ e.content }}|{{ e.section_notes.content }};{{ end }}{{ node.sections.content }}";
        var package = WithSummary(prompt);
        var input = Input("summary", "prompts/summary.md", new Dictionary<string, string> { ["sections"] = Composed }, loops: new Dictionary<string, ConsultLoopPayload> { ["sections"] = Sections() }, flowRets: new Dictionary<string, string> { ["sections"] = "section_notes" });

        var rendered = ConsultRenderPayload.RenderAt25(input, package, prompt);

        Assert.Equal("hpi/HPI|The history.|The history.;plan/Plan|The plan.|The plan.;" + Composed, rendered);
    }

    [Fact]
    public void ALoopsItem_RendersItsOwnText_AndTheListsValue_IsTheArrayOfTheReturnedNodes()
    {
        // Render-only: .value on a prose loop is the walker's to refuse; the
        // list carries it all the same, as the array of the returned node's values.
        var package = WithSummary("{{ node.sections.content }}");
        var input = Input("summary", "prompts/summary.md", new Dictionary<string, string> { ["sections"] = Composed }, loops: new Dictionary<string, ConsultLoopPayload> { ["sections"] = Sections() });

        Assert.Equal("History: Referral.;Plan.;2", ConsultRenderPayload.RenderAt25(input, package, "{{ for e in node.sections }}{{ e.item.content }};{{ end }}{{ node.sections.value | array.size }}"));
        Assert.Equal("The plan.", ConsultRenderPayload.RenderAt25(input, package, "{{ node.sections.value[1] }}"));
    }

    [Fact]
    public void AnElementsBodyNode_IsANode_ARendererPipeTakes()
    {
        var package = WithSummary("{{ node.sections.content }}");
        var input = Input("summary", "prompts/summary.md", new Dictionary<string, string> { ["sections"] = Composed }, loops: new Dictionary<string, ConsultLoopPayload> { ["sections"] = Sections() });

        var ex = Assert.Throws<InvalidOperationException>(() => ConsultRenderPayload.RenderAt25(input, package, "{{ for e in node.sections }}{{ e.section_notes | concept_bullets }}{{ end }}"));

        // The pipe took the node (not "pipes a … through"): it is the renderer's own refusal of prose, or its render.
        Assert.DoesNotContain("pipes a", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFlowNodeThatTookNoBranch_ReadsNull_UnderAGuard()
    {
        var package = V26JobStartTests.Package(m => V26JobStartTests.WithTriage(m, withElse: false));
        var input = Input("assessment", "prompts/assessment.md", skipped: new[] { "triage" });

        Assert.Equal("no", ConsultRenderPayload.RenderAt25(input, package, "{{ if node.triage }}yes{{ else }}no{{ end }}"));
    }

    [Fact]
    public void AnIfNode_ReadsAsItsReturnedNode_Typed()
    {
        var package = V26JobStartTests.Package(m => V26JobStartTests.WithTriage(m));
        var input = Input("assessment", "prompts/assessment.md",
            new Dictionary<string, string> { ["triage"] = """{ "concepts": [ "Asthma" ] }""" },
            flowRets: new Dictionary<string, string> { ["triage"] = "extract" });

        Assert.Equal("Asthma|{ \"concepts\": [ \"Asthma\" ] }", ConsultRenderPayload.RenderAt25(input, package, "{{ node.triage.value.concepts[0] }}|{{ node.triage.content }}"));
    }

    [Fact]
    public void AFanInstanceAt26_RendersItsItemsLabel()
    {
        var package = V26JobStartTests.Package();
        var item = new Dictionary<string, string> { ["id"] = "hpi", ["label"] = "HPI", ["content"] = "History: {{ input.notes }}" };
        var input = Input("section_notes:hpi", "prompts/section.md", item: item, forEach: "snippet.sections");

        Assert.Equal("HPI: History: Referral.", ConsultRenderPayload.RenderAt25(input, package, package.SourceFiles!["prompts/section.md"]));
        Assert.Equal("HPI|HPI", ConsultRenderPayload.RenderAt25(input, package, "{{ item.label }}|{{ item.name }}"));
        Assert.Equal("HPI;Plan;", ConsultRenderPayload.RenderAt25(input with { Folders = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> { ["sections"] = Items } }, package,
            "{{ for s in snippet.sections }}{{ s.label }};{{ end }}"));
    }

    // ----- the orchestrator's half -------------------------------------------------------

    private static readonly ConsultNodeDescriptor Body = new("section_notes", "Section notes", ForEach: "snippet.sections", Format: "prose", In: "sections");
    private static readonly ConsultNodeDescriptor Loop = new("sections", "Sections", Kind: "for", Collection: "snippet.sections", Body: new[] { "section_notes" }, Ret: "section_notes");
    private static readonly ConsultNodeDescriptor Triage = new("triage", "Triage", Kind: "if", Expression: "input.kind == \"new\"", Body: new[] { "full_workup" }, Ret: "full_workup", Branch: "then");
    private static readonly ConsultNodeDescriptor Reader = new("summary", "Summary", Format: "prose", Reads: new[] { "sections", "triage" });

    [Fact]
    public void LoopsFor_CarriesTheItems_AndEveryBodyNodesOutputPerItem_ForTheLoopsRead()
    {
        var nodesById = new[] { Body, Loop, Triage, Reader }.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var outputs = new Dictionary<string, NodeRunResult>(StringComparer.Ordinal)
        {
            ["section_notes:hpi"] = new("The history.", null, "i", "o"),
            ["section_notes:plan"] = new("The plan.", null, "i", "o"),
            ["sections"] = new(Composed, null, "i", "o"),
        };
        var collections = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> { ["sections"] = Items };

        var loops = ConsultRenderPayload.LoopsFor(Reader, nodesById, outputs, collections)!;

        var sections = Assert.Single(loops).Value;
        Assert.Equal(("snippet.sections", "section_notes"), (sections.Collection, sections.RetId));
        Assert.Same(Items, sections.Items);
        Assert.Equal(new Dictionary<string, string> { ["hpi"] = "The history.", ["plan"] = "The plan." }, sections.Texts["section_notes"]);
        Assert.Equal(new Dictionary<string, string> { ["sections"] = "section_notes", ["triage"] = "full_workup" }, ConsultRenderPayload.FlowRetsFor(Reader, nodesById));

        Assert.Null(ConsultRenderPayload.LoopsFor(Body, nodesById, outputs, collections));
        Assert.Null(ConsultRenderPayload.FlowRetsFor(Body, nodesById));
        Assert.Throws<InvalidOperationException>(() => ConsultRenderPayload.LoopsFor(Reader, nodesById, new Dictionary<string, NodeRunResult>(), collections));
    }

    // ----- #1155: deliverable.<id>.content --------------------------------------------------

    /// <summary>The minimal with a second deliverable `patient_letter` whose node `letter` reads deliverable.consult.content.</summary>
    private static WorkflowPackage WithLetter(string prompt = "Dear doctor,\n{{ deliverable.consult.content }}") =>
        V26JobStartTests.Package(
            m => V26Package.WithNodes(m, new WorkflowNodeSpec("letter", "Letter", Prompt: "prompts/letter.md", Format: "prose")) with
            {
                Deliverables = m.Deliverables!.Append(new WorkflowDeliverableSpec("patient_letter", null, "Patient letter", Sources: new List<string> { "node.letter", "node.sections" })).ToList(),
            },
            files => files["prompts/letter.md"] = prompt);

    [Fact]
    public void ADeliverablesContent_RendersTheComposedText_AndASkippedOneReadsNull_UnderAGuard()
    {
        var package = WithLetter();
        var composed = Input("letter", "prompts/letter.md") with { Deliverables = new Dictionary<string, string?> { ["consult"] = "## HPI\n\nThe history." } };
        Assert.Equal("Dear doctor,\n## HPI\n\nThe history.", ConsultRenderPayload.RenderAt25(composed, package, package.SourceFiles!["prompts/letter.md"]));

        var skipped = composed with { Deliverables = new Dictionary<string, string?> { ["consult"] = null } };
        Assert.Equal("none", ConsultRenderPayload.RenderAt25(skipped, package, "{{ if deliverable.consult }}{{ deliverable.consult.content }}{{ else }}none{{ end }}"));

        // Not threaded at all: the object does not exist — loud, never empty.
        Assert.Throws<InvalidOperationException>(() => ConsultRenderPayload.RenderAt25(Input("letter", "prompts/letter.md"), package, package.SourceFiles!["prompts/letter.md"]));
    }

    [Fact]
    public void ADeliverableRead_OutsideContent_IsLoud()
    {
        var package = WithLetter();
        var input = Input("letter", "prompts/letter.md") with { Deliverables = new Dictionary<string, string?> { ["consult"] = "The note." } };

        var ex = Assert.Throws<InvalidOperationException>(() => ConsultRenderPayload.RenderAt25(input, package, "{{ deliverable.consult.value }}"));

        Assert.Equal("Prompt 'prompts/letter.md' reads deliverable.consult.value, which this render has no value for.", ex.Message);
    }

    [Fact]
    public void ADeliverableMacro_ReadsAnotherDeliverablesContent()
    {
        // A second macro, placed on a second deliverable, reads the first deliverable's text (placed on `consult` itself it would be the cycle the validator refuses).
        var package = V26JobStartTests.Package(
            m => V26Package.WithNodes(m with
                {
                    Macros = m.Macros!.Append(new WorkflowMacroSpec("see_also", null, "macros/see_also.md")).ToList(),
                    Deliverables = m.Deliverables!.Append(new WorkflowDeliverableSpec("patient_letter", null, "Patient letter",
                        Sources: new List<string> { "node.letter", "node.sections" },
                        Macros: new List<WorkflowDeliverableMacroSpec> { new("see_also", At: WorkflowDeliverableMacroSpec.Append) })).ToList(),
                }, new WorkflowNodeSpec("letter", "Letter", Prompt: "prompts/letter.md", Format: "prose")),
            files =>
            {
                files["prompts/letter.md"] = "Dear doctor.";
                files["macros/see_also.md"] = V26Package.Header("See also", instruction: "Mention the note.") + "Closing after: {{ deliverable.consult.content | string.size }} chars.";
            });
        var input = new ConsultDeliverableMacrosActivityInput(
            "patient_letter", package.Ref, new[] { new ConsultMacroPlacement("see_also", At: "append") },
            new Dictionary<string, string> { ["notes"] = "Referral.", ["kind"] = "new" }, null,
            new Dictionary<string, string>(), null,
            Run, Profile,
            Deliverables: new Dictionary<string, string?> { ["consult"] = "The note." });

        var result = RenderDeliverableMacrosActivity.Render(input, package);

        // The macro's header still rides the raw file (the store's header read is #1157's); the body renders the read.
        Assert.EndsWith("Closing after: 9 chars.", Assert.Single(result.Append).Text);
    }
}
