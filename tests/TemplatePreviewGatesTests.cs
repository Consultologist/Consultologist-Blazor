using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1205 (package-format-v26-design.md § 2, § 5.4, § 12): the editor's preview judges the three
/// gates over a pick through the library — a node instance's if suppressing the inline read, a
/// macro's own if making it inert, a snippet's or folder item's if emptying it / dropping it from
/// the loop — only once every read a gate makes has a pick; the rest wait, naming the picks.
/// </summary>
public class TemplatePreviewGatesTests
{
    private const string Read = "[{{ macro.closing.prompt }}|{{ macro.closing.content }}|{{ snippet.standards.content }}|{{ for s in snippet.sections }}{{ s.id }},{{ end }}|{{ snippet.sections.plan.content }}|{{ snippet.sections.plan.label }}]{{ for m in macro.section_notes }}{{ m.id }}{{ end }}";

    /// <summary>The macro-folder package with every gate set over input.kind, and an instance of closing on assessment gated too.</summary>
    private static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Gated(string instanceIf = "input.kind == \"new\"")
    {
        var (manifest, files) = WorkflowV26MacroFolderTests.WithFolder(Read);
        files["macros/closing.md"] = V26Package.Header("Closing", instruction: "Add a closing line if the plan changed.", @if: "input.kind == \"new\"") + "Closing: {{ profile.name }}.";
        files["snippets/standards.md"] = V26Package.Header("Local standards", @if: "input.kind == \"new\"") + "Follow the local standards.";
        files["snippets/sections/plan.md"] = V26Package.Header("Plan", @if: "input.kind == \"new\"") + "Plan.";
        manifest = manifest with
        {
            Nodes = manifest.Nodes!.Select(n => n.Id == "assessment"
                ? n with { Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: WorkflowNodeMacroSpec.After, If: instanceIf) } }
                : n).ToList(),
        };
        return (manifest, files);
    }

    private static (TemplateRenderScope Scope, IReadOnlyList<TemplatePreviewGate> Gates) Prepared((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle)
    {
        Assert.Empty(V26Package.Errors(bundle));
        var catalog = V25Package.Catalog();
        var nodes = WorkflowDerivedEdges.PrepareNodes(bundle.Manifest, bundle.Files, catalog, out var snippets, out var contextFor, out var macroFolders);
        var walk = WorkflowDerivedEdges.WalkAll(bundle.Manifest, bundle.Files, nodes, snippets, contextFor, macros: macroFolders);
        return (TemplatePreviewScope.Tokens(bundle.Manifest, bundle.Files, catalog), TemplatePreviewScope.Gates(bundle.Manifest, walk));
    }

    private static TemplateRenderScope Picked(TemplateRenderScope scope, string kind) =>
        scope with { Inputs = new Dictionary<string, string>(scope.Inputs, StringComparer.Ordinal) { ["kind"] = kind } };

    private static string Rendered(TemplateRenderScope scope, IReadOnlyDictionary<string, string> files) =>
        TemplateRenderer.Render("Node 'assessment' prompt", files["prompts/assessment.md"], scope, TemplatePreviewScope.Pipes);

    private static readonly IReadOnlySet<string> None = new HashSet<string>();
    private static readonly IReadOnlySet<string> Kind = new HashSet<string> { "kind" };

    [Fact]
    public void Gates_ListsEveryGate_WithItsReads_AndTheUngatedInstance()
    {
        var (_, gates) = Prepared(Gated());

        Assert.Contains(gates, g => g.Kind == TemplatePreviewGateKind.Macro && g.Key == "closing" && g.If == "input.kind == \"new\"" && g.Inputs.SetEquals(Kind));
        Assert.Contains(gates, g => g.Kind == TemplatePreviewGateKind.Snippet && g.Key == "standards");
        Assert.Contains(gates, g => g.Kind == TemplatePreviewGateKind.Item && g.Owner == "sections" && g.Key == "sections/plan");
        Assert.Contains(gates, g => g.Kind == TemplatePreviewGateKind.Instance && g.Owner == "assessment" && g.Key == "closing" && g.If.Length > 0);
        // The loop's body node holds no gated instance; section_notes is not listed.
        Assert.DoesNotContain(gates, g => g.Kind == TemplatePreviewGateKind.Instance && g.Owner == "section_notes");
    }

    [Fact]
    public void Below26_ThereAreNoGates()
    {
        var (manifest, files) = V25Package.Minimal();
        var nodes = WorkflowDerivedEdges.PrepareNodes(manifest, files, V25Package.Catalog(), out var snippets, out var contextFor, out var macroFolders);
        var walk = WorkflowDerivedEdges.WalkAll(manifest, files, nodes, snippets, contextFor, macros: macroFolders);

        Assert.Empty(TemplatePreviewScope.Gates(manifest, walk));
    }

    [Fact]
    public void WithTheInputUnpicked_EveryGateWaits_AndTheBytesAreTheUngatedOnes()
    {
        var bundle = Gated();
        var (scope, gates) = Prepared(bundle);

        var judged = TemplatePreviewScope.Judged(scope, gates, None, None, itemPicked: false, "assessment", null);

        Assert.Empty(judged.Applied);
        Assert.Equal(4, judged.Waiting.Count);
        Assert.All(judged.Waiting, w => Assert.Equal(new[] { "input.kind" }, w.Missing));
        Assert.Equal(Rendered(scope, bundle.Files), Rendered(judged.Scope, bundle.Files));
    }

    [Fact]
    public void WithAFailingPick_TheThreeGatesApply_AsTheEngineRenders()
    {
        var bundle = Gated();
        var (scope, gates) = Prepared(bundle);

        var judged = TemplatePreviewScope.Judged(Picked(scope, "follow_up"), gates, Kind, None, itemPicked: false, "assessment", null);

        Assert.Equal(new[] { "closing", "closing", "standards", "sections/plan" }, judged.Applied.Select(g => g.Key));
        Assert.Empty(judged.Waiting);
        // The inert macro: both members empty; the snippet empty; plan out of the loop, its direct read empty, its label as ever.
        Assert.Equal("Assess. [|||hpi,||Plan]hpiplan", Rendered(judged.Scope, bundle.Files));
    }

    [Fact]
    public void WithAHoldingPick_NothingApplies_AndTheBytesAreTheUngatedOnes()
    {
        var bundle = Gated();
        var (scope, gates) = Prepared(bundle);

        var judged = TemplatePreviewScope.Judged(Picked(scope, "new"), gates, Kind, None, itemPicked: false, "assessment", null);

        Assert.Empty(judged.Applied);
        Assert.Equal(Rendered(Picked(scope, "new"), bundle.Files), Rendered(judged.Scope, bundle.Files));
        Assert.Contains("Closing: ⟨profile.name⟩.", Rendered(judged.Scope, bundle.Files));
    }

    [Fact]
    public void AnInstanceGate_IsTheReaderNodes_AndSuppressesThePromptAlone()
    {
        // The macro holds (no header gate); only the instance fails.
        var (manifest, files) = Gated(instanceIf: "input.kind == \"follow_up\"");
        files["macros/closing.md"] = V26Package.Header("Closing", instruction: "Add a closing line if the plan changed.") + "Closing: {{ profile.name }}.";
        var (scope, gates) = Prepared((manifest, files));

        var asReader = TemplatePreviewScope.Judged(Picked(scope, "new"), gates, Kind, None, false, "assessment", null);
        var single = Assert.Single(asReader.Applied);
        Assert.Equal((TemplatePreviewGateKind.Instance, "assessment", "closing"), (single.Kind, single.Owner, single.Key));
        Assert.StartsWith("Assess. [|Closing: ⟨profile.name⟩.|", Rendered(asReader.Scope, files));

        // Another reader (or none) never judges assessment's instances.
        var elsewhere = TemplatePreviewScope.Judged(Picked(scope, "new"), gates, Kind, None, false, null, null);
        Assert.DoesNotContain(elsewhere.Applied, g => g.Kind == TemplatePreviewGateKind.Instance);
        Assert.Contains("[" + WorkflowMacroMechanics.Prompt("closing", "Add a closing line if the plan changed."), Rendered(elsewhere.Scope, files));
    }

    [Fact]
    public void AGateReadingTheItem_WaitsOnTheItemPick_UnlessItIsTheItemsOwn()
    {
        var (manifest, files) = Gated(instanceIf: "item.id == \"hpi\"");
        manifest = manifest with
        {
            Nodes = manifest.Nodes!.Select(n => n.Id == "section_notes"
                ? n with { Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: WorkflowNodeMacroSpec.Before, If: "item.id == \"hpi\"") } }
                : n with { Macros = null }).ToList(),
        };
        files["prompts/section.md"] = "{{ item.label }}: {{ item.content }} {{ macro.closing.prompt }}";
        var (scope, gates) = Prepared((manifest, files));

        var waiting = TemplatePreviewScope.Judged(Picked(scope, "new"), gates, Kind, None, itemPicked: false, "section_notes", null);
        Assert.Equal(new[] { "item" }, Assert.Single(waiting.Waiting, w => w.Gate.Kind == TemplatePreviewGateKind.Instance).Missing);

        var asPlan = TemplatePreviewScope.Judged(Picked(scope, "new") with { Item = new TemplateFanItem("plan", "Plan", Text: "Plan.") }, gates, Kind, None, itemPicked: true, "section_notes", "plan");
        Assert.Contains(asPlan.Applied, g => g.Kind == TemplatePreviewGateKind.Instance && g.Owner == "section_notes");
        Assert.Equal("Plan: Plan. ", TemplateRenderer.Render("Node 'section_notes' prompt", files["prompts/section.md"], asPlan.Scope, TemplatePreviewScope.Pipes));
    }
}
