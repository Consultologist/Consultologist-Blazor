using Consultologist.Api.Agents;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>B3 #1002: the 25 render end to end — the orchestrator's half of the scope and the activity's render over it.</summary>
public class V25RenderTests
{
    private static readonly TemplateRunFacts Run = new("2026-10-13", "abcdef12", "general@v2026.08.1", "api.example.test");
    private static readonly TemplateProfileFacts Profile = new("Dr Example", "Dr Example, FRCPC");
    private static readonly Dictionary<string, string> Inputs = new(StringComparer.Ordinal) { ["notes"] = "Referral.", ["kind"] = "new" };

    private static ConsultPromptNodeActivityInput Input(string nodeId, string promptId, IReadOnlyDictionary<string, string>? nodeTexts = null, IReadOnlyDictionary<string, string>? item = null, string? forEach = null) =>
        new(nodeId, promptId, Inputs, "general@v2026.08.1", Format: "prose", Producer: "attested",
            NodeTexts: nodeTexts ?? new Dictionary<string, string>(), Item: item, ForEach: forEach, Run: Run, Profile: Profile);

    [Fact]
    public void APrompt_RendersItsSnippet_ItsInput_AndTheReadNodesValue()
    {
        var package = V25JobStartTests.Package();
        var input = Input("assessment", "assessment", new Dictionary<string, string> { ["extract"] = """{ "concepts": [ "Asthma", "Cough" ] }""" });

        var rendered = ConsultRenderPayload.RenderAt25(input, package, package.SourceFiles!["prompts/assessment.md"]);

        Assert.Equal("Assess.\nFollow the local standards.\nReferral.\n- Asthma\n- Cough\n", rendered);
    }

    [Fact]
    public void AFanInstance_RendersItsItem()
    {
        var package = V25JobStartTests.Package();
        var item = new Dictionary<string, string> { ["id"] = "hpi", ["name"] = "HPI", ["content"] = "History: {{ input.notes }}" };
        var input = Input("section_notes:hpi", "section", item: item, forEach: "snippet.sections");

        Assert.Equal("HPI: History: Referral.", ConsultRenderPayload.RenderAt25(input, package, package.SourceFiles!["prompts/section.md"]));
    }

    [Fact]
    public void AFolder_IsIteratedAsSettled_AndAMacroRenders()
    {
        var package = V25JobStartTests.Package();
        var folders = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>
        {
            ["sections"] = new List<IReadOnlyDictionary<string, string>>
            {
                new Dictionary<string, string> { ["id"] = "hpi", ["name"] = "HPI", ["content"] = "History: {{ input.notes }}" },
            },
        };
        var input = Input("assessment", "assessment") with { Folders = folders };

        var rendered = ConsultRenderPayload.RenderAt25(input, package,
            "{{ for s in snippet.sections }}{{ s.name }}={{ s.content }};{{ end }}{{ macro.closing.content }}|{{ macro.closing.prompt }}");

        Assert.Equal(
            "HPI=History: Referral.;Closing: Dr Example.|" + WorkflowMacroMechanics.Prompt("closing", "Add a closing line if the plan changed."),
            rendered);
    }

    [Fact]
    public void ThePipe_RendersTheBytesABindingGave()
    {
        var package = V25JobStartTests.Package();
        const string json = """{ "concepts": [ { "term": "Asthma", "id": "195967001", "type": "disorder", "isActive": true } ] }""";
        var input = Input("assessment", "assessment", new Dictionary<string, string> { ["extract"] = json });

        var rendered = ConsultRenderPayload.RenderAt25(input, package, "{{ node.extract.content | concept_context }}");

        Assert.Equal(ConceptRenderers.Context(ConceptOutputContract.Deserialize(json, "extract")), rendered);
        Assert.Contains("Asthma", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void APayloadWithoutTheScope_IsRefused()
    {
        var package = V25JobStartTests.Package();
        var input = new ConsultPromptNodeActivityInput("assessment", "assessment", Inputs, "general@v2026.08.1", Format: "prose");

        var ex = Assert.Throws<InvalidOperationException>(() => ConsultRenderPayload.RenderAt25(input, package, "x"));

        Assert.Equal("Prompt node 'assessment' declares format 'prose', but its payload carries no 25 render scope (snapshotted before the render shipped).", ex.Message);
    }

    [Fact]
    public void AReadTheOrchestratorDidNotThread_IsRefusedByName()
    {
        var package = V25JobStartTests.Package();
        var input = Input("assessment", "assessment");

        var ex = Assert.Throws<InvalidOperationException>(() => ConsultRenderPayload.RenderAt25(input, package, package.SourceFiles!["prompts/assessment.md"]));

        Assert.Equal("Prompt 'assessment' reads node.extract, which this render has no value for.", ex.Message);
    }

    [Fact]
    public void NodeTexts_AreTheReads_ItemAligned()
    {
        var extract = new ConsultNodeDescriptor("extract", "Extract", Format: "concept_list");
        var sections = new ConsultNodeDescriptor("section_notes", "Sections", ForEach: "snippet.sections", Format: "prose", Reads: new[] { "extract" });
        var review = new ConsultNodeDescriptor("review", "Review", ForEach: "snippet.sections", Format: "prose", Reads: new[] { "extract", "section_notes" });
        var nodesById = new[] { extract, sections, review }.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var outputs = new Dictionary<string, NodeRunResult>(StringComparer.Ordinal)
        {
            ["extract"] = new("E", null, "i", "o"),
            ["section_notes:hpi"] = new("S-hpi", null, "i", "o"),
            ["section_notes:plan"] = new("S-plan", null, "i", "o"),
        };
        var hpi = new Dictionary<string, string> { ["id"] = "hpi" };

        var texts = ConsultRenderPayload.NodeTextsFor(review, hpi, nodesById, outputs);

        Assert.Equal(new Dictionary<string, string> { ["extract"] = "E", ["section_notes"] = "S-hpi" }, texts);
        Assert.Empty(ConsultRenderPayload.NodeTextsFor(extract, null, nodesById, outputs));

        var missing = Assert.Throws<InvalidOperationException>(() => ConsultRenderPayload.NodeTextsFor(review, new Dictionary<string, string> { ["id"] = "ros" }, nodesById, outputs));
        Assert.Equal("Node 'review' reads node 'section_notes' ('section_notes:ros'), whose output the orchestrator does not hold.", missing.Message);
    }

    [Fact]
    public void Folders_DropInputFans_AndTheFacts_AreTheTokensForms()
    {
        var collections = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>
        {
            ["sections"] = new List<IReadOnlyDictionary<string, string>>(),
            ["input:prior_notes"] = new List<IReadOnlyDictionary<string, string>>(),
        };

        Assert.Equal(new[] { "sections" }, ConsultRenderPayload.FoldersFor(collections).Keys);
        Assert.Equal(new TemplateRunFacts("2026-10-13", "0123abcd", "p@v1", ""), ConsultRenderPayload.RunFactsFor(new DateTime(2026, 10, 13, 14, 5, 0, DateTimeKind.Utc), "0123abcd-ffff", "p@v1", null));
        Assert.Equal(new TemplateProfileFacts("", "Dr X"), ConsultRenderPayload.ProfileFactsFor(null, new ConsultSignatureSnapshot("s", "Dr X", "2026-10-01")));
    }

    [Fact]
    public void APrompt_RendersTheAggregatorsComposedText()
    {
        // #1065: node.<agg>.content is the stored composed sections, keyed by the plain id.
        var package = V25JobStartTests.Package(
            m => m with
            {
                Prompts = m.Prompts!.Append(new WorkflowPromptSpec("letter", "prompts/letter.md", new List<string>())).ToList(),
                Nodes = m.Nodes!
                    .Append(new WorkflowNodeSpec("letter", "Letter", Prompt: "letter", Format: "prose"))
                    .Append(new WorkflowNodeSpec("letter_all", "Letter, assembled", Aggregate: new List<string> { "node.letter" }))
                    .ToList(),
                Deliverables = m.Deliverables!.Append(new WorkflowDeliverableSpec("letter", "node.letter_all", "Letter")).ToList(),
            },
            f => new Dictionary<string, string>(f, StringComparer.Ordinal) { ["prompts/letter.md"] = "Letter from:\n{{ node.all.content }}" });
        var input = Input("letter", "letter", new Dictionary<string, string> { ["all"] = "## HPI\n\nHistory.\n\n## Plan\n\nPlan." });

        Assert.Equal("Letter from:\n## HPI\n\nHistory.\n\n## Plan\n\nPlan.", ConsultRenderPayload.RenderAt25(input, package, package.SourceFiles!["prompts/letter.md"]));
    }
}
