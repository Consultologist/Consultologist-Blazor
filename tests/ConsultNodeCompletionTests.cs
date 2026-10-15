using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// B5 #1004 (design §§ 6–7): the completion order in the activity — the reply's
/// digest, failIfEmpty on the reply, the sanctioned slots filled (the render's
/// reads, cross-checked against the walk), the instances composed, outputHash
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
        var reads = new HashSet<string>(StringComparer.Ordinal);
        var rendered = ConsultRenderPayload.RenderAt25(input, package, package.SourceFiles![$"prompts/{node.Prompt}.md"], reads);
        return (package, input, reads, rendered);
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
    public void ASanctionedMarker_IsFilledWithTheMacrosText_RenderedOverTheNodesScope()
    {
        // The prompt reads macro.closing.prompt (through its snippet); the model
        // emits the marker; closing.md reads scope — one of this node's edges.
        var (package, input, reads, rendered) = Prepare(
            "assessment",
            m => V25Package.WithNode(m, "assessment", n => n with { Macros = null }),
            f => new Dictionary<string, string>(f, StringComparer.Ordinal)
            {
                ["snippets/standards.md"] = "Standards. {{ macro.closing.prompt }}",
                ["macros/closing.md"] = "Closing for {{ node.scope.text }}: {{ profile.name }}.",
            });

        Assert.Contains(WorkflowMacroMechanics.Prompt("closing", "Add a closing line if the plan changed."), rendered);
        Assert.Equal(new[] { "closing" }, reads);

        var result = ConsultNodeCompletion.Complete(Settled("Plan.\n\n[[slot:closing]]"), input, package, reads);

        Assert.Equal("Plan.\n\nClosing for in_scope: Dr Example.", result.RawOutput);
        Assert.Equal(new[] { "slot:closing" }, result.Appended!.Select(e => $"{e.Kind}:{e.Id}"));
        Assert.Null(result.Excluded);
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex(result.RawOutput), result.OutputHash);
    }

    [Fact]
    public void AMarkerNoReadSanctions_IsRemoved_AndNamed()
    {
        var (package, input, reads, _) = Prepare("assessment", Unplaced);

        var result = ConsultNodeCompletion.Complete(Settled("Plan. [[slot:closing]] End."), input, package, reads);

        Assert.Equal("Plan.  End.", result.RawOutput);
        Assert.Null(result.Appended);
        Assert.Equal(new ConsultNodeExclusion("slot", "closing", "no read in this node's prompt sanctions [[slot:closing]]"), Assert.Single(result.Excluded!));
    }

    [Fact]
    public void AMarkerWhoseReadSitsBehindABranchNotTaken_IsRemoved_AndSaysSo()
    {
        // The walk reaches the read (conservative over branches); the render did
        // not take it: the marker is unsanctioned by the prompt that ran, and
        // the record says exactly that.
        var (package, input, reads, rendered) = Prepare(
            "assessment",
            m => V25Package.WithNode(m, "assessment", n => n with { Macros = null }),
            f => new Dictionary<string, string>(f, StringComparer.Ordinal)
            {
                ["prompts/assessment.md"] = "{{ if input.kind == 'follow_up' }}{{ macro.closing.prompt }}{{ end }}Assess {{ input.notes }}",
            });

        Assert.Equal("Assess Referral.", rendered);
        Assert.Empty(reads);

        var result = ConsultNodeCompletion.Complete(Settled("Plan. [[slot:closing]]"), input, package, reads);

        Assert.Equal("Plan. ", result.RawOutput);
        Assert.Equal(
            new ConsultNodeExclusion("slot", "closing", "the prompt that ran did not read macro.closing.prompt; the read sits behind a condition the render did not take"),
            Assert.Single(result.Excluded!));
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
        Assert.Equal(new ConsultNodeExclusion("macro", "closing", "needs kind to be 'follow_up'; it is 'new'"), Assert.Single(result.Excluded!));
        Assert.Equal(result.AgentOutputHash, result.OutputHash);
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

        var (package, forHpi, hpiReads, _) = Prepare("section_notes", anchored, item: hpi, forEach: "snippet:sections");
        var (_, forPlan, planReads, _) = Prepare("section_notes", anchored, item: plan, forEach: "snippet:sections");

        Assert.Equal("History.", ConsultNodeCompletion.Complete(Settled("History."), forHpi, package, hpiReads).RawOutput);
        Assert.Equal("The plan.\n\nClosing: Dr Example.", ConsultNodeCompletion.Complete(Settled("The plan."), forPlan, package, planReads).RawOutput);
    }

    [Fact]
    public void AFanItemsOwnSlotRead_SanctionsItsMarker_AndNotAnotherItems()
    {
        var hpi = new Dictionary<string, string> { ["id"] = "hpi", ["name"] = "HPI", ["content"] = "History. {{ macro.closing.prompt }}" };
        var plan = new Dictionary<string, string> { ["id"] = "plan", ["name"] = "Plan", ["content"] = "Plan." };
        Func<Dictionary<string, string>, Dictionary<string, string>> files = f => new Dictionary<string, string>(f, StringComparer.Ordinal)
        {
            ["snippets/sections/hpi.md"] = "History. {{ macro.closing.prompt }}",
        };

        var (package, forHpi, hpiReads, _) = Prepare("section_notes", changeFiles: files, item: hpi, forEach: "snippet:sections");
        var (_, forPlan, planReads, _) = Prepare("section_notes", changeFiles: files, item: plan, forEach: "snippet:sections");

        Assert.Equal(new[] { "closing" }, hpiReads);
        Assert.Empty(planReads);

        var filled = ConsultNodeCompletion.Complete(Settled("H. [[slot:closing]]"), forHpi, package, hpiReads);
        var removed = ConsultNodeCompletion.Complete(Settled("P. [[slot:closing]]"), forPlan, package, planReads);

        Assert.Equal("H. Closing: Dr Example.", filled.RawOutput);
        Assert.Equal("P. ", removed.RawOutput);
        Assert.Equal("no read in this node's prompt sanctions [[slot:closing]]", Assert.Single(removed.Excluded!).Reason);
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
