using Consultologist.Api.Agents;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1175 + #1177 (package-format-v26-design.md § 2.2–2.4): the two macro gates
/// at run time, judged over the node's scope before the render and at
/// completion. The instance's `if` governs the inline read — a suppressed
/// read renders empty with no mechanics sentence and the placement is excluded
/// by the gate's text; any instance holding reads; no instance reads as today;
/// `item.id` per item. The macro's own `if` (its header's) makes it inert —
/// `.prompt` and `.content` empty, every placement excluded with the header's
/// text, judged before the instance's gate. The two compose by AND. A 25
/// `when` never suppressed the read: the bytes are pinned. The deliverable
/// side skips an inert macro's placements and judges a 26 instance `if`.
/// </summary>
public class V26NodeGatesTests
{
    private static readonly TemplateRunFacts Run = new("2026-10-27", "abcdef12", "general@v2026.10.1", "api.example.test");
    private static readonly TemplateProfileFacts Profile = new("Dr Example", "Dr Example, FRCPC");
    private static readonly Dictionary<string, string> Inputs = new(StringComparer.Ordinal) { ["notes"] = "Referral.", ["kind"] = "new" };
    private static readonly Dictionary<string, string> Supplied = new(StringComparer.Ordinal) { ["notes"] = "\"Referral.\"", ["kind"] = "\"new\"" };

    private const string InlineRead = "Standards. {{ macro.closing.prompt }}";
    private static readonly string Sentence = WorkflowMacroMechanics.Prompt("closing", "Add a closing line if the plan changed.");

    /// <summary>The standards snippet reading the macro inline, under its header.</summary>
    private static void ReadingInline(Dictionary<string, string> files) =>
        files["snippets/standards.md"] = V26Package.Header("Local standards") + InlineRead;

    /// <summary>The closing macro with a header gate.</summary>
    private static void Gated(Dictionary<string, string> files, string @if) =>
        files["macros/closing.md"] = V26Package.Header("Closing", instruction: "Add a closing line if the plan changed.", @if: @if) + "Closing: {{ profile.name }}.";

    private static WorkflowPackageManifest Instances(WorkflowPackageManifest m, string nodeId, params WorkflowNodeMacroSpec[] instances) =>
        V26Package.WithNode(m, nodeId, n => n with { Macros = instances.Length == 0 ? null : instances.ToList() });

    /// <summary>A 26 node prepared as the activity prepares it: the descriptor, the input, the gated render and its reads.</summary>
    private static (WorkflowPackage Package, ConsultPromptNodeActivityInput Input, HashSet<string> Reads, string Rendered) Prepare(
        string nodeId,
        Func<WorkflowPackageManifest, WorkflowPackageManifest>? change = null,
        Action<Dictionary<string, string>>? withFiles = null,
        IReadOnlyDictionary<string, string>? item = null,
        string? forEach = null)
    {
        var package = V26JobStartTests.Package(change, withFiles);
        var node = package.Nodes!.First(n => n.Id == nodeId);
        var descriptor = ConsultGenerationJobStarter.DescribeNode(
            node, null, macroFiles: package.TextFiles, macros: package.EffectiveManifest.Macros, formats: package.Formats, effectiveInputs: Inputs, manifest: package.EffectiveManifest,
            flows: ConsultGenerationJobStarter.FlowsOf(package));
        var input = new ConsultPromptNodeActivityInput(
            ConsultNodeScheduler.InstanceKey(nodeId, item?["id"]), node.Prompt!, Inputs, package.Ref,
            Format: descriptor.Format, Producer: descriptor.Producer,
            NodeTexts: new Dictionary<string, string> { ["extract"] = """{ "concepts": [ "Asthma" ] }""", ["scope"] = "in_scope" },
            Item: item, ForEach: forEach ?? descriptor.ForEach, Run: Run, Profile: Profile,
            Supplied: Supplied, Classifications: new Dictionary<string, string> { ["scope"] = "in_scope" },
            FailIfEmpty: descriptor.FailIfEmpty, Instances: descriptor.Macros);
        var trace = new TemplateRenderer.TemplateRenderTrace();
        var rendered = ConsultRenderPayload.RenderAt25(input, package, package.TextOf(node.Prompt!), trace);
        return (package, input, trace.PromptReads.ToHashSet(StringComparer.Ordinal), rendered);
    }

    private static NodeRunResult Settled(string reply) =>
        new(reply, null, "in", ConsultGenerationProvenance.Sha256Hex(reply), ConsultGenerationProvenance.NodeHashVersionAt25, AgentOutputHash: ConsultGenerationProvenance.Sha256Hex(reply));

    private static readonly IReadOnlyDictionary<string, string> Hpi = new Dictionary<string, string> { ["id"] = "hpi", ["label"] = "HPI", ["content"] = "History: {{ input.notes }}" };
    private static readonly IReadOnlyDictionary<string, string> Plan = new Dictionary<string, string> { ["id"] = "plan", ["label"] = "Plan", ["content"] = "Plan." };

    // ----- the instance gate (#1175) ------------------------------------------------------

    [Fact]
    public void DescribeNode_At26_CarriesTheInstancesIf()
    {
        var (_, input, _, _) = Prepare("assessment", m => Instances(m, "assessment", new WorkflowNodeMacroSpec("closing", At: "after", If: "input.kind == \"follow_up\"")), ReadingInline);

        var closing = Assert.Single(input.Instances!);
        Assert.Equal(("closing", true, "input.kind == \"follow_up\"", null), (closing.Id, closing.After, closing.If, closing.When));
    }

    [Fact]
    public void ASuppressedRead_RendersEmpty_NoSentence_AndThePlacementIsExcludedByTheIfText()
    {
        var (package, input, reads, rendered) = Prepare("assessment", m => Instances(m, "assessment", new WorkflowNodeMacroSpec("closing", At: "after", If: "input.kind == \"follow_up\"")), ReadingInline);

        Assert.Equal("Assess.\nStandards. \nReferral.\n- Asthma\n", rendered);
        Assert.Empty(reads);

        var completed = ConsultNodeCompletion.Complete(Settled("The plan."), input, package, reads);

        Assert.Equal("The plan.", completed.RawOutput);
        Assert.Null(completed.Appended);
        Assert.Equal(new[] { new ConsultNodeExclusion("macro", "closing", "needs `input.kind == \"follow_up\"`; it does not hold") }, completed.Excluded);
    }

    [Fact]
    public void TwoInstances_OneHolding_RendersTheRead_AndExcludesOnlyTheOther()
    {
        var (package, input, reads, rendered) = Prepare("assessment",
            m => Instances(m, "assessment", new WorkflowNodeMacroSpec("closing", At: "before", If: "input.kind == \"follow_up\""), new WorkflowNodeMacroSpec("closing", At: "after", If: "input.kind == \"new\"")), ReadingInline);

        Assert.Equal("Assess.\nStandards. " + Sentence + "\nReferral.\n- Asthma\n", rendered);
        Assert.Equal(new[] { "closing" }, reads);

        var completed = ConsultNodeCompletion.Complete(Settled("The plan."), input, package, reads);

        Assert.Equal("The plan.\n\nClosing: Dr Example.", completed.RawOutput);
        Assert.Equal(new[] { ("macro", "closing") }, completed.Appended!.Select(a => (a.Kind, a.Id)));
        var excluded = Assert.Single(completed.Excluded!);
        Assert.Equal("needs `input.kind == \"follow_up\"`; it does not hold", excluded.Reason);
    }

    [Fact]
    public void NoInstance_RendersTheRead_AsToday()
    {
        // The closing instance moved onto section_notes: assessment reads the macro inline and places nothing.
        var (package, input, reads, rendered) = Prepare("assessment",
            m => Instances(Instances(m, "assessment"), "section_notes", new WorkflowNodeMacroSpec("closing", At: "after")), ReadingInline);

        Assert.Equal("Assess.\nStandards. " + Sentence + "\nReferral.\n- Asthma\n", rendered);
        Assert.Equal(new[] { "closing" }, reads);
        Assert.Null(ConsultNodeCompletion.Complete(Settled("The plan."), input, package, reads).Excluded);
    }

    [Fact]
    public void AnInstanceGatedOnTheItem_SuppressesPerItem()
    {
        static WorkflowPackageManifest Change(WorkflowPackageManifest m) =>
            Instances(Instances(m, "assessment"), "section_notes", new WorkflowNodeMacroSpec("closing", At: "after", If: "item.id == \"plan\""));
        static void Files(Dictionary<string, string> files) => files["prompts/section.md"] = "{{ item.label }}: {{ item.content }} {{ macro.closing.prompt }}";

        var hpi = Prepare("section_notes", Change, Files, Hpi);
        Assert.Equal("HPI: History: Referral. ", hpi.Rendered);
        Assert.Empty(hpi.Reads);
        var hpiCompleted = ConsultNodeCompletion.Complete(Settled("The history."), hpi.Input, hpi.Package, hpi.Reads);
        Assert.Equal("The history.", hpiCompleted.RawOutput);
        Assert.Equal("needs `item.id == \"plan\"`; it does not hold", Assert.Single(hpiCompleted.Excluded!).Reason);

        var plan = Prepare("section_notes", Change, Files, Plan);
        Assert.Equal("Plan: Plan. " + Sentence, plan.Rendered);
        Assert.Equal(new[] { "closing" }, plan.Reads);
        var planCompleted = ConsultNodeCompletion.Complete(Settled("The plan."), plan.Input, plan.Package, plan.Reads);
        Assert.Equal("The plan.\n\nClosing: Dr Example.", planCompleted.RawOutput);
        Assert.Null(planCompleted.Excluded);
    }

    [Fact]
    public void ANonBooleanInstanceIf_FailsUnderTheIfSubject()
    {
        var (package, input, reads, _) = Prepare("assessment", m => Instances(m, "assessment", new WorkflowNodeMacroSpec("closing", At: "after")), ReadingInline);
        var broken = input with { Instances = new[] { new ConsultNodeMacroDescriptor("closing", true, If: "input.kind") } };

        var ex = Assert.Throws<InvalidOperationException>(() => ConsultNodeCompletion.Complete(Settled("The plan."), broken, package, reads));

        Assert.StartsWith("Node 'assessment' macro 'closing' if must be a boolean expression", ex.Message, StringComparison.Ordinal);
    }

    // ----- the macro's own gate (#1177) ---------------------------------------------------

    [Fact]
    public void AnInertMacro_RendersEmptyPromptAndContent_AndItsPlacementIsExcludedWithTheHeadersText()
    {
        var (package, input, reads, rendered) = Prepare("assessment", m => Instances(m, "assessment", new WorkflowNodeMacroSpec("closing", At: "after")),
            files => { ReadingInline(files); Gated(files, "input.kind == \"follow_up\""); });

        Assert.Equal("Assess.\nStandards. \nReferral.\n- Asthma\n", rendered);
        Assert.Empty(reads);

        var completed = ConsultNodeCompletion.Complete(Settled("Plan. {{ macro.closing.content }}"), input, package, reads);

        Assert.Equal("Plan. ", completed.RawOutput);
        Assert.Null(completed.Appended);
        Assert.Equal(new[] { new ConsultNodeExclusion("macro", "closing", "needs `input.kind == \"follow_up\"`; it does not hold") }, completed.Excluded);
    }

    [Fact]
    public void Inert_AndAHoldingInstance_IsInert_WithTheHeadersReason()
    {
        var (package, input, reads, rendered) = Prepare("assessment", m => Instances(m, "assessment", new WorkflowNodeMacroSpec("closing", At: "after", If: "input.kind == \"new\"")),
            files => { ReadingInline(files); Gated(files, "input.kind == \"follow_up\""); });

        Assert.Equal("Assess.\nStandards. \nReferral.\n- Asthma\n", rendered);
        var completed = ConsultNodeCompletion.Complete(Settled("The plan."), input, package, reads);

        Assert.Equal("The plan.", completed.RawOutput);
        Assert.Equal("needs `input.kind == \"follow_up\"`; it does not hold", Assert.Single(completed.Excluded!).Reason);
    }

    [Fact]
    public void ALiveMacro_WithAFailingInstance_IsSuppressed_NotInert()
    {
        var (package, input, reads, rendered) = Prepare("assessment", m => Instances(m, "assessment", new WorkflowNodeMacroSpec("closing", At: "after", If: "input.kind == \"follow_up\"")),
            files => { ReadingInline(files); Gated(files, "input.kind == \"new\""); });

        // The read is suppressed by the instance; the macro itself is live — its content still renders where read.
        Assert.Equal("Assess.\nStandards. \nReferral.\n- Asthma\n", rendered);
        var completed = ConsultNodeCompletion.Complete(Settled("Plan. {{ macro.closing.content }}"), input, package, reads);

        Assert.Equal("Plan. Closing: Dr Example.", completed.RawOutput);
        Assert.Equal(new[] { ("inline", "closing") }, completed.Appended!.Select(a => (a.Kind, a.Id)));
        Assert.Equal("needs `input.kind == \"follow_up\"`; it does not hold", Assert.Single(completed.Excluded!).Reason);
    }

    [Fact]
    public void A25When_DoesNotSuppressTheRead_TheBytesArePinned()
    {
        var package = V25JobStartTests.Package(
            m => V25Package.WithNode(m, "assessment", n => n with { Macros = new List<WorkflowNodeMacroSpec> { new WorkflowNodeMacroSpec("closing", At: "after", When: "input.kind == \"follow_up\"") } }),
            files => { files["snippets/standards.md"] = InlineRead; return files; });
        var node = package.Nodes!.First(n => n.Id == "assessment");
        var descriptor = ConsultGenerationJobStarter.DescribeNode(node, null, macroFiles: package.SourceFiles, formats: package.Formats, effectiveInputs: Inputs, manifest: package.Manifest);
        var input = new ConsultPromptNodeActivityInput("assessment", node.Prompt!, Inputs, package.Ref, Format: descriptor.Format, Producer: descriptor.Producer,
            NodeTexts: new Dictionary<string, string> { ["extract"] = """{ "concepts": [ "Asthma" ] }""" }, Run: Run, Profile: Profile, Supplied: Supplied, Instances: descriptor.Macros);
        var trace = new TemplateRenderer.TemplateRenderTrace();

        var rendered = ConsultRenderPayload.RenderAt25(input, package, package.SourceFiles!["prompts/assessment.md"], trace);

        Assert.Equal("Assess.\nStandards. " + Sentence + "\nReferral.\n- Asthma\n", rendered);
        Assert.Equal(new[] { "closing" }, trace.PromptReads);
        var completed = ConsultNodeCompletion.Complete(Settled("The plan."), input, package, trace.PromptReads.ToHashSet(StringComparer.Ordinal));
        Assert.Equal(new[] { new ConsultNodeExclusion("macro", "closing", "needs `input.kind == \"follow_up\"`; it does not hold") }, completed.Excluded);
    }

    // ----- the deliverable side ----------------------------------------------------------

    private static WorkflowPackageManifest Placing(WorkflowPackageManifest m, params WorkflowDeliverableMacroSpec[] entries) =>
        m with { Deliverables = m.Deliverables!.Select(d => d.Id == "consult" ? d with { Macros = entries.ToList() } : d).ToList() };

    private static (WorkflowPackage Package, WorkflowResolvedResult Result) Deliverable(Func<WorkflowPackageManifest, WorkflowPackageManifest> change, Action<Dictionary<string, string>>? withFiles = null)
    {
        var package = V26JobStartTests.Package(change, withFiles);
        return (package, package.Results!.Single());
    }

    [Fact]
    public void TheStore_ReadsA26DeliverableInstanceIf_IntoItsCondition()
    {
        var (_, gated) = Deliverable(m => Placing(m, new WorkflowDeliverableMacroSpec("closing", At: WorkflowDeliverableMacroSpec.Append, If: "input.kind == \"follow_up\"")));
        var condition = Assert.Single(gated.MacroConditions!);
        Assert.Equal(("closing", "append", "input.kind == \"follow_up\"", null), (condition.MacroId, condition.At, condition.If, condition.When));

        var (_, bare) = Deliverable(m => Placing(m, new WorkflowDeliverableMacroSpec("closing", At: WorkflowDeliverableMacroSpec.Append)));
        Assert.Null(bare.MacroConditions);
    }

    [Fact]
    public void DecidePlacementWhens_AnInertMacro_IsExcludedWithTheHeadersReason_EvenWithNoInstanceGate()
    {
        var (package, result) = Deliverable(m => Placing(m, new WorkflowDeliverableMacroSpec("closing", At: WorkflowDeliverableMacroSpec.Append)), files => Gated(files, "input.kind == \"follow_up\""));
        Assert.Null(result.MacroConditions);

        var (macros, excluded, placements) = ConsultGenerationJobStarter.DecidePlacementWhens(result, V26JobStartTests.NewPatient, null, V26JobStartTests.Scope(package, V26JobStartTests.NewPatient));

        Assert.Equal(new[] { new ConsultExcludedMacro("consult", "closing", "needs `input.kind == \"follow_up\"`; it does not hold") }, excluded);
        Assert.Null(macros);
        Assert.Null(placements);

        // The gate holding: kept, nothing recorded.
        var (live, kept, _) = ConsultGenerationJobStarter.DecidePlacementWhens(result, V26JobStartTests.FollowUp, null, V26JobStartTests.Scope(package, V26JobStartTests.FollowUp));
        Assert.Empty(kept);
        Assert.Equal(new[] { "closing" }, live);
    }

    [Fact]
    public void DecidePlacementWhens_InertBeforeTheInstanceGate_AndA26InstanceIf_IsJudged()
    {
        var (package, result) = Deliverable(m => Placing(m, new WorkflowDeliverableMacroSpec("closing", At: WorkflowDeliverableMacroSpec.Append, If: "input.kind == \"follow_up\"")), files => Gated(files, "input.kind == \"follow_up\""));
        var scope = V26JobStartTests.Scope(package, V26JobStartTests.NewPatient);

        // Both fail: one refusal, the header's.
        var (_, excluded, _) = ConsultGenerationJobStarter.DecidePlacementWhens(result, V26JobStartTests.NewPatient, null, scope);
        Assert.Equal("needs `input.kind == \"follow_up\"`; it does not hold", Assert.Single(excluded).Reason);

        // The macro live, the instance's if failing: the instance's refusal.
        var (livePackage, liveResult) = Deliverable(m => Placing(m, new WorkflowDeliverableMacroSpec("closing", At: WorkflowDeliverableMacroSpec.Append, If: "input.kind == \"follow_up\"")));
        var (_, refused, _) = ConsultGenerationJobStarter.DecidePlacementWhens(liveResult, V26JobStartTests.NewPatient, null, V26JobStartTests.Scope(livePackage, V26JobStartTests.NewPatient));
        Assert.Equal("needs `input.kind == \"follow_up\"`; it does not hold", Assert.Single(refused).Reason);

        // A non-boolean instance if is loud under the 26 subject.
        var broken = liveResult with { MacroConditions = new[] { new WorkflowResolvedMacroCondition("closing", null, "append", If: "input.kind") } };
        var ex = Assert.Throws<InvalidOperationException>(() => ConsultGenerationJobStarter.DecidePlacementWhens(broken, V26JobStartTests.NewPatient, null, V26JobStartTests.Scope(livePackage, V26JobStartTests.NewPatient)));
        Assert.StartsWith("Deliverable 'consult' macro 'closing' if must be a boolean expression", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBoundary_ExcludesAnInertPlacement()
    {
        var (package, _) = Deliverable(m => Placing(m, new WorkflowDeliverableMacroSpec("closing", At: WorkflowDeliverableMacroSpec.Append)), files => Gated(files, "input.kind == \"follow_up\""));
        var effective = V26JobStartTests.Effective(V26JobStartTests.NewPatient);

        var decision = DecideActivity.Decide(package, V26JobStartTests.NewPatient, new Dictionary<string, string>(), effectiveInputs: effective,
            nodeTexts: new Dictionary<string, string>(), run: Run, profile: Profile);

        Assert.Equal(new[] { ("consult", "closing") }, decision.ExcludedMacros!.Select(e => (e.ResultId, e.MacroId)));
        Assert.Null(Assert.Single(decision.Results).MacroPlacements);
    }

    [Fact]
    public void TheDeliverableMacroRender_ReadsAnInertMacrosContent_AsEmpty()
    {
        var package = V26JobStartTests.Package(
            m => Placing(m with { Macros = m.Macros!.Append(new WorkflowMacroSpec("see_also", null, "macros/see_also.md")).ToList() },
                new WorkflowDeliverableMacroSpec("see_also", At: WorkflowDeliverableMacroSpec.Append)),
            files =>
            {
                Gated(files, "input.kind == \"follow_up\"");
                files["macros/see_also.md"] = V26Package.Header("See also", instruction: "Mention the closing.") + "See also: [{{ macro.closing.content }}]";
            });
        var input = new ConsultDeliverableMacrosActivityInput("consult", package.Ref, new[] { new ConsultMacroPlacement("see_also", At: "append") },
            Inputs, null, new Dictionary<string, string>(), null, Run, Profile);

        var result = RenderDeliverableMacrosActivity.Render(input, package);

        Assert.Equal("See also: []", Assert.Single(result.Append).Text);
    }

    [Fact]
    public void TheConditionScopesInertMemo_ResetsWithTheFoldersAndTheItem()
    {
        var (package, _) = Deliverable(m => Placing(m, new WorkflowDeliverableMacroSpec("closing", At: WorkflowDeliverableMacroSpec.Append)), files => Gated(files, "input.kind == \"follow_up\""));
        var scope = V26JobStartTests.Scope(package, V26JobStartTests.NewPatient);

        Assert.NotNull(scope.InertReason("closing"));
        Assert.NotNull(scope.WithFolders(new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>()).InertReason("closing"));
        Assert.NotNull(scope.WithItem("sections", package.Data!.Collections["sections"].Items[0]).InertReason("closing"));
        Assert.Null(scope.InertReason("nope"));
    }

    // ----- the snippet's and the folder item's gate (#1179) --------------------------------

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> HpiOnly =
        new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> { ["sections"] = new[] { Hpi } };

    [Fact]
    public void AnInertSnippet_RendersEmptyContent_AndItsIdAndLabelStillRead()
    {
        static void GatedStandards(Dictionary<string, string> files) =>
            files["snippets/standards.md"] = V26Package.Header("Local standards", @if: "input.kind == \"follow_up\"") + "Follow the local standards.";

        var (package, input, _, rendered) = Prepare("assessment", null, GatedStandards);
        Assert.Equal("Assess.\n\nReferral.\n- Asthma\n", rendered);
        Assert.Equal("standards|Local standards|", ConsultRenderPayload.RenderAt25(input, package, "{{ snippet.standards.id }}|{{ snippet.standards.label }}|{{ snippet.standards.content }}"));

        // The gate holding: as today.
        var (_, _, _, live) = Prepare("assessment", null, files => files["snippets/standards.md"] = V26Package.Header("Local standards", @if: "input.kind == \"new\"") + "Follow the local standards.");
        Assert.Equal("Assess.\nFollow the local standards.\nReferral.\n- Asthma\n", live);
    }

    [Fact]
    public void AFolderItemGatedOffAtJobStart_IsAbsentFromTheLoop_AndReadsEmptyDirectly()
    {
        // The starter settled the folder to [hpi] (the item's if, judged with the item in hand — #1154);
        // the render sees the full folder with plan inert: out of the loop, empty when read directly.
        var package = V26JobStartTests.Package(withFiles: files => files["snippets/sections/plan.md"] = V26Package.Header("Plan", @if: "input.kind == \"follow_up\"") + "Plan.");
        var node = package.Nodes!.First(n => n.Id == "assessment");
        var input = new ConsultPromptNodeActivityInput("assessment", node.Prompt!, Inputs, package.Ref, Format: "prose", Producer: "attested",
            NodeTexts: new Dictionary<string, string>(), Folders: HpiOnly, Run: Run, Profile: Profile);

        var rendered = ConsultRenderPayload.RenderAt25(input, package,
            "[{{ for s in snippet.sections }}{{ s.label }}={{ s.content }};{{ end }}] {{ snippet.sections | array.size }} [{{ snippet.sections.plan.content }}] {{ snippet.sections.plan.id }} [{{ snippet.sections.hpi.content }}]");

        Assert.Equal("[HPI=History: Referral.;] 1 [] plan [History: Referral.]", rendered);

        var scope = ConsultGatedScope.Judge(input, package);
        Assert.Equal(new[] { "sections/plan" }, scope.InertSnippets!.Order());
        Assert.Equal(new[] { "hpi", "plan" }, scope.Folders["sections"].Select(item => item.Id));
    }

    [Fact]
    public void TheDeliverableMacroRender_ReadsAnInertSnippet_AndAGatedOffItem_AsEmpty()
    {
        var package = V26JobStartTests.Package(
            withFiles: files =>
            {
                files["snippets/standards.md"] = V26Package.Header("Local standards", @if: "input.kind == \"follow_up\"") + "Follow the local standards.";
                files["snippets/sections/plan.md"] = V26Package.Header("Plan", @if: "input.kind == \"follow_up\"") + "Plan.";
                files["macros/closing.md"] = V26Package.Header("Closing", instruction: "Add a closing line if the plan changed.") + "[{{ snippet.standards.content }}][{{ snippet.sections.plan.content }}][{{ for s in snippet.sections }}{{ s.id }}{{ end }}]";
            });
        var input = new ConsultDeliverableMacrosActivityInput("consult", package.Ref, new[] { new ConsultMacroPlacement("closing", At: "append") },
            Inputs, null, new Dictionary<string, string>(), HpiOnly, Run, Profile);

        var result = RenderDeliverableMacrosActivity.Render(input, package);

        Assert.Equal("[][][hpi]", Assert.Single(result.Append).Text);
    }

    [Fact]
    public void Below26_TheFoldersAreTheSettledSnapshot_AndNoKeyIsSet()
    {
        var package = V25JobStartTests.Package();
        var node = package.Nodes!.First(n => n.Id == "assessment");
        var settled = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>
        {
            ["sections"] = new[] { (IReadOnlyDictionary<string, string>)new Dictionary<string, string> { ["id"] = "hpi", ["name"] = "HPI", ["content"] = "History: {{ input.notes }}" } },
        };
        var input = new ConsultPromptNodeActivityInput("assessment", node.Prompt!, Inputs, package.Ref, Format: "prose", Producer: "attested",
            NodeTexts: new Dictionary<string, string>(), Folders: settled, Run: Run, Profile: Profile);

        var scope = ConsultGatedScope.Judge(input, package);

        Assert.Null(scope.InertSnippets);
        Assert.Equal(new[] { "hpi" }, scope.Folders["sections"].Select(item => item.Id));
        Assert.Equal("[HPI;]", ConsultRenderPayload.RenderAt25(input, package, "[{{ for s in snippet.sections }}{{ s.name }};{{ end }}]"));
    }

    // ----- macro folders (#1182) ---------------------------------------------------------------

    private static WorkflowPackage WithMacroFolder(string? planIf = null)
    {
        var (manifest, files) = WorkflowV26MacroFolderTests.WithFolder("{{ for m in macro.section_notes }}[{{ m.id }}|{{ m.label }}|{{ m.prompt }}|{{ m.content }}]{{ end }} {{ macro.section_notes.hpi.content }}");
        if (planIf is not null)
        {
            files["macros/section_notes/plan.md"] = V26Package.Header("Plan notes", instruction: "Note the plan.", @if: planIf) + "Plan noted.";
        }

        Assert.Equal(new List<string>(), V26Package.Errors((manifest, files)));
        return WorkflowPackageStoreMaterialiseTests.Load(manifest, files);
    }

    private static ConsultPromptNodeActivityInput FolderInput(WorkflowPackage package) =>
        new("assessment", "prompts/assessment.md", Inputs, package.Ref, Format: "prose", Producer: "attested",
            NodeTexts: new Dictionary<string, string> { ["extract"] = "{ \"concepts\": [ \"Asthma\" ] }" }, Run: Run, Profile: Profile);

    [Fact]
    public void APromptIteratingAMacroFolder_RendersEachItemsLabelPromptAndContent_InTheIndexsOrder()
    {
        var package = WithMacroFolder();
        var input = FolderInput(package);
        var trace = new TemplateRenderer.TemplateRenderTrace();

        var rendered = ConsultRenderPayload.RenderAt25(input, package, package.TextOf("prompts/assessment.md"), trace);

        Assert.Equal(
            "Assess. [hpi|HPI notes|" + WorkflowMacroMechanics.Prompt("hpi", "Note the history.") + "|History noted: Dr Example.][plan|Plan notes|" + WorkflowMacroMechanics.Prompt("plan", "Note the plan.") + "|Plan noted.] History noted: Dr Example.",
            rendered);
        Assert.Equal(new[] { "section_notes/hpi", "section_notes/plan" }, trace.PromptReads);
        Assert.Empty(ConsultGatedScope.Judge(input, package).InertMacros!);
    }

    [Fact]
    public void AFolderItemGatedOffByItsHeader_RendersEmptyPromptAndContent_AndStaysInTheLoop()
    {
        var package = WithMacroFolder("input.kind == \"follow_up\"");
        var input = FolderInput(package);

        var rendered = ConsultRenderPayload.RenderAt25(input, package, package.TextOf("prompts/assessment.md"));

        Assert.Equal("Assess. [hpi|HPI notes|" + WorkflowMacroMechanics.Prompt("hpi", "Note the history.") + "|History noted: Dr Example.][plan|Plan notes||] History noted: Dr Example.", rendered);
        Assert.Equal(new[] { "section_notes/plan" }, ConsultGatedScope.Judge(input, package).InertMacros!.Order());
    }

    [Fact]
    public void TheDeliverableMacroRender_IteratesAMacroFolder_WithItsGates()
    {
        var (manifest, files) = WorkflowV26MacroFolderTests.WithFolder();
        files["macros/section_notes/plan.md"] = V26Package.Header("Plan notes", instruction: "Note the plan.", @if: "input.kind == \"follow_up\"") + "Plan noted.";
        // The placed macro (closing) reads the folder: a live item's content, a gated-off item's none.
        files["macros/closing.md"] = V26Package.Header("Closing", instruction: "Close.") + "[{{ for m in macro.section_notes }}{{ m.content }}{{ end }}]";
        manifest = manifest with { Deliverables = manifest.Deliverables!.Select(d => d.Id == "consult" ? d with { Macros = new List<WorkflowDeliverableMacroSpec> { new("closing", At: WorkflowDeliverableMacroSpec.Append) } } : d).ToList() };
        Assert.Equal(new List<string>(), V26Package.Errors((manifest, files)));
        var package = WorkflowPackageStoreMaterialiseTests.Load(manifest, files);
        var input = new ConsultDeliverableMacrosActivityInput("consult", package.Ref, new[] { new ConsultMacroPlacement("closing", At: "append") },
            Inputs, null, new Dictionary<string, string>(), null, Run, Profile);

        var result = RenderDeliverableMacrosActivity.Render(input, package);

        Assert.Equal("[History noted: Dr Example.]", Assert.Single(result.Append).Text);
    }
}
