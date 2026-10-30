using Consultologist.Api.Jobs;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1235 (package-format-v27-design.md § 1.4, § 1.5): the 27 render — a loop
/// returning an if/case iterates every item, a skipped one with `skipped` true,
/// no content and no value, and only the body nodes that ran; the list's text
/// and value are over the present items; a body node reading the inner flow by
/// bare id reads the taken branch's returned node for its item, typed by it.
/// </summary>
public class V27RenderTests
{
    private static readonly TemplateRunFacts Run = new("2026-10-29", "abcdef12", "test/general@v2026.10.1", "api.example.test");
    private static readonly TemplateProfileFacts Profile = new("Dr Example", "Dr Example, FRCPC");
    private static readonly Dictionary<string, string> Inputs = new(StringComparer.Ordinal) { ["notes"] = "Referral.", ["kind"] = "new" };

    private static readonly IReadOnlyList<IReadOnlyDictionary<string, string>> Items = new[]
    {
        (IReadOnlyDictionary<string, string>)new Dictionary<string, string> { ["id"] = "hpi", ["label"] = "HPI", ["content"] = "History." },
        new Dictionary<string, string> { ["id"] = "plan", ["label"] = "Plan", ["content"] = "Plan." },
    };

    private const string Composed = "## HPI\n\nDrafted h.";

    private static ConsultLoopPayload Sections() =>
        new("snippet.sections", "triage", Items, new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["section_notes"] = new Dictionary<string, string> { ["hpi"] = "Notes h.", ["plan"] = "Notes p." },
            ["triage"] = new Dictionary<string, string> { ["hpi"] = "Drafted h." },
            ["patient_draft"] = new Dictionary<string, string> { ["hpi"] = "Drafted h." },
        }, Skipped: new[] { "plan" });

    private static ConsultPromptNodeActivityInput Input(string nodeId, string promptId, IReadOnlyDictionary<string, string>? nodeTexts = null, IReadOnlyDictionary<string, string>? item = null, string? forEach = null,
        IReadOnlyDictionary<string, ConsultLoopPayload>? loops = null, IReadOnlyDictionary<string, string>? flowRets = null) =>
        new(nodeId, promptId, Inputs, "test/general@v2026.10.1", Format: "prose", Producer: "attested",
            NodeTexts: nodeTexts ?? new Dictionary<string, string>(), Item: item, ForEach: forEach, Run: Run, Profile: Profile, Loops: loops, FlowRets: flowRets);

    /// <summary>The triaged 27 package with a prose node `summary` reading the loop whole, sourced — valid at 27, so the probe saw the members the render exposes.</summary>
    private static WorkflowPackage WithSummary(string prompt) =>
        V27JobStartTests.Package(b => V27Package.With(V27Package.WithTriageInLoop(b),
            m => V26JobStartTests.Sourcing(V26Package.WithNodes(m, new WorkflowNodeSpec("summary", "Summary", Prompt: "prompts/summary.md", Format: "prose")), "node.summary"),
            files => files["prompts/summary.md"] = prompt));

    [Fact]
    public void ALoopReturningAnIf_IteratesEveryItem_ASkippedOneWithSkippedTrue_AndReadsAsThePresentItemsText()
    {
        const string prompt = "{{ for e in node.sections }}{{ e.item.id }}:{{ if e.skipped }}skipped/{{ e.section_notes.content }}{{ else }}{{ e.content }}/{{ e.triage.content }}{{ end }};{{ end }}{{ node.sections | array.size }} {{ node.sections.content }}";
        var package = WithSummary(prompt);
        var input = Input("summary", "prompts/summary.md", new Dictionary<string, string> { ["sections"] = Composed },
            loops: new Dictionary<string, ConsultLoopPayload> { ["sections"] = Sections() },
            flowRets: new Dictionary<string, string> { ["sections"] = "triage", ["triage:hpi"] = "patient_draft" });

        var rendered = ConsultRenderPayload.RenderAt25(input, package, prompt);

        Assert.Equal("hpi:Drafted h./Drafted h.;plan:skipped/Notes p.;2 " + Composed, rendered);
    }

    [Fact]
    public void ABodyNode_ReadingTheInnerFlowByBareId_ReadsTheTakenBranchsReturnedNode_ForItsItem()
    {
        var package = V27JobStartTests.Package(b => V27Package.With(V27Package.WithTriageInLoop(b),
            m => V26Package.WithNode(V26Package.WithNodes(m, new WorkflowNodeSpec("summary", "Summary", Prompt: "prompts/summary.md", Format: "prose")),
                "sections", n => n with { Nodes = new List<string> { "section_notes", "triage", "summary" }, Ret = "node.summary" }),
            files => files["prompts/summary.md"] = "Summary of {{ node.triage.content }} for {{ item.label }}."));
        var input = Input("summary", "prompts/summary.md", new Dictionary<string, string> { ["triage"] = "Drafted h." }, item: Items[0], forEach: "snippet.sections",
            flowRets: new Dictionary<string, string> { ["triage:hpi"] = "patient_draft" });

        Assert.Equal("Summary of Drafted h. for HPI.", ConsultRenderPayload.RenderAt25(input, package, "Summary of {{ node.triage.content }} for {{ item.label }}."));
    }
}
