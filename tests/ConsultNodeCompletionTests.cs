using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// B5 #1004 → #1044 (design §§ 6–7): the completion order in the activity — the
/// reply's digest, failIfEmpty on the reply, the reply RENDERED as a template
/// over the node's scope (its macro.&lt;id&gt;.text reads are its slots; a reply
/// that does not render fails the node), the instances composed, outputHash
/// over what came out, and every placement and exclusion on the record.
/// </summary>
public class ConsultNodeCompletionTests
{
    private static readonly TemplateRunFacts Run = new("2026-10-14", "abcdef12", "general@v2026.08.1", "api.example.test");
    private static readonly TemplateProfileFacts Profile = new("Dr Example", "Dr Example, FRCPC");
    private static readonly Dictionary<string, string> Inputs = new(StringComparer.Ordinal) { ["notes"] = "Referral.", ["kind"] = "new" };
    private static readonly Dictionary<string, string> Supplied = new(StringComparer.Ordinal) { ["notes"] = "\"Referral.\"", ["kind"] = "\"new\"" };

    private static (WorkflowPackage Package, ConsultPromptNodeActivityInput Input, HashSet<string> Reads, string Rendered) Prepare(
        string nodeId,
        Func<WorkflowPackageManifest, WorkflowPackageManifest>? change = null,
        Func<Dictionary<string, string>, Dictionary<string, string>>? changeFiles = null,
        IReadOnlyDictionary<string, string>? item = null,
        string? forEach = null,
        string? failIfEmpty = null)
    {
        var package = V25JobStartTests.Package(change, changeFiles);
        var node = package.Nodes!.First(n => n.Id == nodeId);
        var descriptor = ConsultGenerationJobStarter.DescribeNode(
            node, null, macroFiles: package.SourceFiles, formats: package.Formats, effectiveInputs: Inputs, manifest: package.Manifest);
        var input = new ConsultPromptNodeActivityInput(
            ConsultNodeScheduler.InstanceKey(nodeId, item?["id"]), node.Prompt!, Inputs, package.Ref,
            Format: descriptor.Format, Producer: descriptor.Producer,
            NodeTexts: new Dictionary<string, string> { ["extract"] = """{ "concepts": [ "Asthma" ] }""", ["scope"] = "in_scope" },
            Item: item, ForEach: forEach, Run: Run, Profile: Profile,
            Supplied: Supplied, Classifications: new Dictionary<string, string> { ["scope"] = "in_scope" },
            FailIfEmpty: failIfEmpty ?? descriptor.FailIfEmpty, Instances: descriptor.Macros);
        var trace = new TemplateRenderer.TemplateRenderTrace();
        var rendered = ConsultRenderPayload.RenderAt25(input, package, package.SourceFiles![$"prompts/{node.Prompt}.md"], trace);
        return (package, input, trace.PromptReads.ToHashSet(StringComparer.Ordinal), rendered);
    }

    /// <summary>The closing instance moved off assessment (onto section_notes, so the macro stays placed and the package valid).</summary>
    private static WorkflowPackageManifest Unplaced(WorkflowPackageManifest m) =>
        V25Package.WithNode(
            V25Package.WithNode(m, "assessment", n => n with { Macros = null }),
            "section_notes", n => n with { Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: "after") } });

    private static NodeRunResult Settled(string reply, string? classification = null) =>
        new(reply, null, "in", ConsultGenerationProvenance.Sha256Hex(classification ?? reply), ConsultGenerationProvenance.NodeHashVersionAt25, classification,
            AgentOutputHash: ConsultGenerationProvenance.Sha256Hex(reply));

    [Fact]
    public void AnAfterInstance_ComposesAfterTheReply_AndTheTwoHashesPart()
    {
        // The minimal package places closing after assessment's output.
        var (package, input, reads, _) = Prepare("assessment");

        var result = ConsultNodeCompletion.Complete(Settled("The plan."), input, package, reads);

        Assert.Equal("The plan.\n\nClosing: Dr Example.", result.RawOutput);
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex("The plan."), result.AgentOutputHash);
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex("The plan.\n\nClosing: Dr Example."), result.OutputHash);
        Assert.Equal(6, result.HashVersion);
        Assert.Equal(new[] { "macro:closing" }, result.Appended!.Select(e => $"{e.Kind}:{e.Id}"));
        Assert.Null(result.Excluded);
    }

    [Fact]
    public void NothingToCompose_LeavesTheSettledResultAlone()
    {
        var (package, input, reads, _) = Prepare("assessment", Unplaced);
        var settled = Settled("The plan.");

        var result = ConsultNodeCompletion.Complete(settled, input, package, reads);

        Assert.Same(settled, result);
        Assert.Equal(result.OutputHash, result.AgentOutputHash);
    }

    [Fact]
    public void TheReplysTextRead_IsTheMacrosText_RenderedOverTheNodesScope()
    {
        // The prompt reads macro.closing.prompt (through its snippet); the model
        // writes the read the sentence named; closing.md reads scope — one of
        // this node's edges.
        var (package, input, reads, rendered) = Prepare(
            "assessment",
            m => V25Package.WithNode(m, "assessment", n => n with { Macros = null }),
            f => new Dictionary<string, string>(f, StringComparer.Ordinal)
            {
                ["snippets/standards.md"] = "Standards. {{ macro.closing.prompt }}",
                ["macros/closing.md"] = "Closing for {{ node.scope.text }}: {{ profile.name }}.",
            });

        Assert.Contains(WorkflowMacroMechanics.Prompt("closing", "Add a closing line if the plan changed."), rendered);
        Assert.Contains("{{ macro.closing.text }}", rendered);
        Assert.Equal(new[] { "closing" }, reads);

        var result = ConsultNodeCompletion.Complete(Settled("Plan.\n\n{{ macro.closing.text }}"), input, package, reads);

        Assert.Equal("Plan.\n\nClosing for in_scope: Dr Example.", result.RawOutput);
        Assert.Equal(new[] { "slot:closing" }, result.Appended!.Select(e => $"{e.Kind}:{e.Id}"));
        Assert.Null(result.Excluded);
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex(result.RawOutput), result.OutputHash);
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex("Plan.\n\n{{ macro.closing.text }}"), result.AgentOutputHash);
    }

    [Fact]
    public void AReplysRead_NoPromptInstructed_StillRenders_OverTheFullScope()
    {
        // #1044: nothing in a reply is sanctioned or removed — the reply renders
        // over the node's full scope, every macro in it.
        var (package, input, reads, _) = Prepare("assessment", Unplaced);

        var result = ConsultNodeCompletion.Complete(Settled("Plan. {{ macro.closing.text }} End."), input, package, reads);

        Assert.Equal("Plan. Closing: Dr Example. End.", result.RawOutput);
        Assert.Equal(new[] { "slot:closing" }, result.Appended!.Select(e => $"{e.Kind}:{e.Id}"));
        Assert.Null(result.Excluded);
    }

    [Fact]
    public void AReplyReadingTheScope_RendersIt_AndNamesNoSlot()
    {
        var (package, input, reads, _) = Prepare("assessment", Unplaced);

        var result = ConsultNodeCompletion.Complete(Settled("Scope {{ node.scope.text }}; kind {{ input.kind }}; {{ snippet.standards.text }}"), input, package, reads);

        Assert.StartsWith("Scope in_scope; kind new; ", result.RawOutput);
        Assert.Null(result.Appended);
        Assert.Null(result.Excluded);
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex(result.RawOutput), result.OutputHash);
        Assert.NotEqual(result.OutputHash, result.AgentOutputHash);
    }

    [Theory]
    [InlineData("Plan {{ 1 + }}", "Node 'assessment' reply does not parse: ")]
    [InlineData("Plan {{ unclosed", "Node 'assessment' reply failed to render: ")]
    [InlineData("Plan {{ node.meds.text }}", "Node 'assessment' reply reads node.meds, which this render has no value for.")]
    [InlineData("Plan {{ input.severity }}", "Node 'assessment' reply reads input.severity, which this render has no value for.")]
    [InlineData("Plan {{ macro.closing.size }}", "Node 'assessment' reply reads macro.closing.size, which this render has no value for.")]
    public void AReplyThatDoesNotRender_FailsTheNode_NonRetryably(string reply, string sentence)
    {
        // #1044: the prompt or the agent is at fault — an InvalidOperationException,
        // which the orchestrator's retry policy does not retry (ConsultGenerationEngine.AgentActivityRetryOptions).
        var (package, input, reads, _) = Prepare("assessment", Unplaced);

        var ex = Assert.Throws<InvalidOperationException>(() => ConsultNodeCompletion.Complete(Settled(reply), input, package, reads));

        Assert.StartsWith(sentence, ex.Message);
    }

    [Fact]
    public void AReadInsideAFilledText_IsTheTextsNotAnEntry_AndTwiceReadIsTwicePlaced()
    {
        // The fixture keeps closing placed after assessment (so the package stays
        // valid); note is read only through closing's text.
        var (package, input, reads, _) = Prepare(
            "assessment",
            m => m with { Macros = m.Macros!.Append(new WorkflowMacroSpec("note", "Note", "macros/note.md", Instruction: "Add a note.")).ToList() },
            f => new Dictionary<string, string>(f, StringComparer.Ordinal)
            {
                ["macros/closing.md"] = "Closing. {{ macro.note.text }}",
                ["macros/note.md"] = "Note.",
            });

        var result = ConsultNodeCompletion.Complete(Settled("{{ macro.closing.text }} {{ macro.closing.text }}"), input, package, reads);

        Assert.Equal("Closing. Note. Closing. Note.\n\nClosing. Note.", result.RawOutput);
        Assert.Equal(new[] { "slot:closing", "slot:closing", "macro:closing" }, result.Appended!.Select(e => $"{e.Kind}:{e.Id}"));
    }

    [Fact]
    public void AnInstructionIsATemplate_RenderedBeforeTheSentence()
    {
        // #1044: the instruction renders over the node's scope; the sentence,
        // carrying the literal read, is appended after.
        var (_, _, reads, rendered) = Prepare(
            "assessment",
            m => V25Package.WithNode(m, "assessment", n => n with { Macros = null }) with
            {
                Macros = m.Macros!.Select(x => x with { Instruction = "Add a closing line for a {{ input.kind }} referral if {{ node.scope.text }}." }).ToList(),
            },
            f => new Dictionary<string, string>(f, StringComparer.Ordinal) { ["snippets/standards.md"] = "Standards. {{ macro.closing.prompt }}" });

        Assert.Contains(WorkflowMacroMechanics.Prompt("closing", "Add a closing line for a new referral if in_scope."), rendered);
        Assert.Equal(new[] { "closing" }, reads);
    }

    [Fact]
    public void AReplyRead_BehindABranchThePromptDidNotTake_StillRenders()
    {
        // The walk reaches the read (conservative over branches); the render did
        // not take it; the reply renders all the same — no sanction at 25.
        var (package, input, reads, rendered) = Prepare(
            "assessment",
            m => V25Package.WithNode(m, "assessment", n => n with { Macros = null }),
            f => new Dictionary<string, string>(f, StringComparer.Ordinal)
            {
                ["prompts/assessment.md"] = "{{ if input.kind == 'follow_up' }}{{ macro.closing.prompt }}{{ end }}Assess {{ input.notes }}",
            });

        Assert.Equal("Assess Referral.", rendered);
        Assert.Empty(reads);

        var result = ConsultNodeCompletion.Complete(Settled("Plan. {{ macro.closing.text }}"), input, package, reads);

        Assert.Equal("Plan. Closing: Dr Example.", result.RawOutput);
        Assert.Equal(new[] { "slot:closing" }, result.Appended!.Select(e => $"{e.Kind}:{e.Id}"));
        Assert.Null(result.Excluded);
    }

    [Fact]
    public void ARenderReadTheWalkDoesNotReach_IsAnEngineFault()
    {
        var (package, input, _, _) = Prepare("assessment", Unplaced);
        var impossible = new HashSet<string>(StringComparer.Ordinal) { "closing" };

        var ex = Assert.Throws<InvalidOperationException>(() => ConsultNodeCompletion.Complete(Settled("Plan."), input, package, impossible));

        Assert.Equal("Node 'assessment' rendered macro.closing.prompt, which the package's walk does not reach from its prompt; the walk and the render disagree.", ex.Message);
    }

    [Fact]
    public void AnInstanceWhoseWhenRefuses_IsExcludedByName_WithTheExplainersSentence()
    {
        var (package, input, reads, _) = Prepare(
            "assessment",
            m => V25Package.WithNode(m, "assessment", n => n with { Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: "after", When: "input.kind == \"follow_up\"") } }));

        var result = ConsultNodeCompletion.Complete(Settled("The plan."), input, package, reads);

        Assert.Equal("The plan.", result.RawOutput);
        Assert.Null(result.Appended);
        // #1038: the fixed 25 reason form — the condition's own text, never a value.
        Assert.Equal(new ConsultNodeExclusion("macro", "closing", "needs `input.kind == \"follow_up\"`; it does not hold"), Assert.Single(result.Excluded!));
        Assert.Equal(result.AgentOutputHash, result.OutputHash);
    }

    [Fact]
    public void AnInstanceGatedOnANodesText_IsJudgedOverTheActivitysScope()
    {
        // #1038: the when reads extract's TEXT — threaded as a node text, as the
        // prompt's own reads are — and is judged over the same scope the instance
        // renders over; the ≤24 tree could not hold it.
        var (package, input, reads, _) = Prepare(
            "assessment",
            m => V25Package.WithNode(m, "assessment", n => n with { Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: "after", When: "node.extract.text != \"\"") } }));

        var result = ConsultNodeCompletion.Complete(Settled("The plan."), input, package, reads);

        Assert.Equal("The plan.\n\nClosing: Dr Example.", result.RawOutput);
        Assert.Equal(new[] { "macro:closing" }, result.Appended!.Select(e => $"{e.Kind}:{e.Id}"));
    }

    [Fact]
    public void AnInstanceGatedOnTheFanItem_IsJudgedPerItem()
    {
        var hpi = new Dictionary<string, string> { ["id"] = "hpi", ["name"] = "HPI", ["content"] = "History." };
        var plan = new Dictionary<string, string> { ["id"] = "plan", ["name"] = "Plan", ["content"] = "Plan." };
        Func<WorkflowPackageManifest, WorkflowPackageManifest> gated = m =>
            V25Package.WithNode(m, "section_notes", n => n with { Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: "after", When: "item.id == \"plan\"") } });

        var (package, forHpi, hpiReads, _) = Prepare("section_notes", gated, item: hpi, forEach: "snippet.sections");
        var (_, forPlan, planReads, _) = Prepare("section_notes", gated, item: plan, forEach: "snippet.sections");

        var forHpiResult = ConsultNodeCompletion.Complete(Settled("History."), forHpi, package, hpiReads);
        Assert.Equal("History.", forHpiResult.RawOutput);
        Assert.Equal("needs `item.id == \"plan\"`; it does not hold", Assert.Single(forHpiResult.Excluded!).Reason);
        Assert.Equal("The plan.\n\nClosing: Dr Example.", ConsultNodeCompletion.Complete(Settled("The plan."), forPlan, package, planReads).RawOutput);
    }

    [Fact]
    public void AnInstanceGatedOnAClassifier_ReadsTheAnswersThreaded()
    {
        var (package, input, reads, _) = Prepare(
            "assessment",
            m => V25Package.WithNode(m, "assessment", n => n with { Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: "before", When: "node.scope.value == \"in_scope\"") } }));

        var result = ConsultNodeCompletion.Complete(Settled("The plan."), input, package, reads);

        Assert.Equal("Closing: Dr Example.\n\nThe plan.", result.RawOutput);
        Assert.Equal(new[] { "macro:closing" }, result.Appended!.Select(e => $"{e.Kind}:{e.Id}"));
    }

    [Fact]
    public void AForItemInstance_ComposesForItsItemOnly_PerItem()
    {
        var hpi = new Dictionary<string, string> { ["id"] = "hpi", ["name"] = "HPI", ["content"] = "History: {{ input.notes }}" };
        var plan = new Dictionary<string, string> { ["id"] = "plan", ["name"] = "Plan", ["content"] = "Plan." };
        Func<WorkflowPackageManifest, WorkflowPackageManifest> anchored = m =>
            V25Package.WithNode(m, "section_notes", n => n with { Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: "after", ForItem: "plan") } });

        var (package, forHpi, hpiReads, _) = Prepare("section_notes", anchored, item: hpi, forEach: "snippet.sections");
        var (_, forPlan, planReads, _) = Prepare("section_notes", anchored, item: plan, forEach: "snippet.sections");

        Assert.Equal("History.", ConsultNodeCompletion.Complete(Settled("History."), forHpi, package, hpiReads).RawOutput);
        Assert.Equal("The plan.\n\nClosing: Dr Example.", ConsultNodeCompletion.Complete(Settled("The plan."), forPlan, package, planReads).RawOutput);
    }

    [Fact]
    public void AFanItemsOwnSlotRead_InstructsItsItem_AndEitherItemsReplyRenders()
    {
        var hpi = new Dictionary<string, string> { ["id"] = "hpi", ["name"] = "HPI", ["content"] = "History. {{ macro.closing.prompt }}" };
        var plan = new Dictionary<string, string> { ["id"] = "plan", ["name"] = "Plan", ["content"] = "Plan." };
        Func<Dictionary<string, string>, Dictionary<string, string>> files = f => new Dictionary<string, string>(f, StringComparer.Ordinal)
        {
            ["snippets/sections/hpi.md"] = "History. {{ macro.closing.prompt }}",
        };

        var (package, forHpi, hpiReads, _) = Prepare("section_notes", changeFiles: files, item: hpi, forEach: "snippet.sections");
        var (_, forPlan, planReads, _) = Prepare("section_notes", changeFiles: files, item: plan, forEach: "snippet.sections");

        Assert.Equal(new[] { "closing" }, hpiReads);
        Assert.Empty(planReads);

        var forHpiResult = ConsultNodeCompletion.Complete(Settled("H. {{ macro.closing.text }} ({{ item.name }})"), forHpi, package, hpiReads);
        var forPlanResult = ConsultNodeCompletion.Complete(Settled("P. {{ macro.closing.text }} ({{ item.name }})"), forPlan, package, planReads);

        Assert.Equal("H. Closing: Dr Example. (HPI)", forHpiResult.RawOutput);
        Assert.Equal("P. Closing: Dr Example. (Plan)", forPlanResult.RawOutput);
        Assert.Null(forPlanResult.Excluded);
    }

    [Fact]
    public void FailIfEmpty_IsJudgedOnTheReply_BeforeAnythingComposes()
    {
        // assessment carries an after-instance: a blank reply would compose to
        // non-blank text, so the judgment comes first.
        var (package, input, reads, _) = Prepare("assessment", failIfEmpty: "The assessment is empty.");

        var ex = Assert.Throws<ConsultNodeEmptyReplyException>(() => ConsultNodeCompletion.Complete(Settled("  \n"), input, package, reads));

        Assert.Equal("The assessment is empty.", ex.Sentence);
        Assert.Equal("Node 'assessment' reply is empty: The assessment is empty.", ex.Message);
        Assert.True(ConsultNodeCompletion.IsEmptyReplyFailure(typeof(ConsultNodeEmptyReplyException).FullName));
        Assert.Equal("The plan.\n\nClosing: Dr Example.", ConsultNodeCompletion.Complete(Settled("The plan."), input, package, reads).RawOutput);
    }

    [Fact]
    public void Empty_ByTheFormatsType()
    {
        // text: blank; array: an empty top-level array, or no concepts once
        // deserialised; object and enum: never — they parsed and validated.
        Assert.True(ConsultNodeCompletion.IsEmpty(Settled(" "), "text"));
        Assert.False(ConsultNodeCompletion.IsEmpty(Settled("x"), "text"));
        Assert.True(ConsultNodeCompletion.IsEmpty(Settled("[]"), "array"));
        Assert.False(ConsultNodeCompletion.IsEmpty(Settled("[1]"), "array"));
        Assert.True(ConsultNodeCompletion.IsEmpty(Settled("""{ "concepts": [] }""") with { Concepts = Array.Empty<ClinicalConcept>() }, "array"));
        Assert.False(ConsultNodeCompletion.IsEmpty(Settled("{}"), "object"));
        Assert.False(ConsultNodeCompletion.IsEmpty(Settled("", "in_scope"), "enum"));
    }

    [Fact]
    public void TheDescriptor_CarriesTheNodesFailIfEmpty_At25()
    {
        var (_, input, _, _) = Prepare("assessment", m => V25Package.WithNode(m, "assessment", n => n with { FailIfEmpty = "The assessment is empty." }));

        Assert.Equal("The assessment is empty.", input.FailIfEmpty);
    }
}
