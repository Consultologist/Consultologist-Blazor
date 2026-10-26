using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1116 (package-format-v26-design.md § 5.4): a folder item readable directly —
/// snippet.&lt;folder&gt;.&lt;item&gt; and macro.&lt;folder&gt;.&lt;item&gt; resolve to the item with
/// the folder's member set, recorded under the item's key; an undeclared item
/// refused naming the folder's items; below 26 a folder is iterated only.
/// </summary>
public class WorkflowV26DirectReadTests
{
    private static List<string> Reading(string text) => V26Package.FlowErrors(m => m, files => files["prompts/assessment.md"] = "Assess. " + text);

    private static readonly TemplateRunFacts Run = new("2026-10-26", "abcdef12", "test/general@v2026.10.8", "api.example.test");
    private static readonly TemplateProfileFacts Profile = new("Dr Example", "Dr Example, FRCPC");

    // ----- the snippet item -------------------------------------------------------------

    [Fact]
    public void ASnippetItem_ReadsDirectly_WithTheFoldersMembers()
    {
        Assert.Empty(Reading("{{ snippet.sections.hpi.content }} {{ snippet.sections.hpi.id }} {{ snippet.sections.plan.label }}"));
        Assert.Contains("Node 'assessment' prompt reads snippet.sections.nope, which folder 'sections' does not contain (items: hpi, plan).", Reading("{{ snippet.sections.nope.content }}"));
        Assert.Contains("Node 'assessment' prompt reads snippet.sections.hpi.name; at specVersion 26 the member is .label.", Reading("{{ snippet.sections.hpi.name }}"));
        Assert.Contains(Reading("{{ snippet.sections.hpi }}"), e => e.Contains("sections/hpi", StringComparison.Ordinal) && e.Contains("bare", StringComparison.Ordinal));
        // The folder as a value and the loop are as ever.
        Assert.Contains(Reading("{{ snippet.sections }}"), e => e.StartsWith("Node 'assessment' prompt reads snippet.sections", StringComparison.Ordinal));
        Assert.Empty(Reading("{{ for s in snippet.sections }}{{ s.content }}{{ end }} {{ snippet.sections.hpi.content }}"));
    }

    [Fact]
    public void TheRead_IsRecordedUnderTheItemsKey_AndTheReaderWaitsOnWhatTheItemReads()
    {
        var (manifest, files) = V26Package.Minimal();
        files["prompts/assessment.md"] = "Assess. {{ snippet.sections.hpi.content }}";
        files["snippets/sections/hpi.md"] = V26Package.Header("HPI") + "History: {{ node.scope.value }}";
        var nodes = WorkflowDerivedEdges.PrepareNodes(manifest, files, V25Package.Catalog(), out var snippets, out var contextFor);
        var walk = WorkflowDerivedEdges.WalkAll(manifest, files, nodes, snippets, contextFor);

        var prompt = Assert.Single(walk.Templates, t => t.Kind == TemplateKind.Prompt && t.Id == "assessment");
        Assert.Contains("sections/hpi", prompt.References.Snippets);
        Assert.DoesNotContain("sections", prompt.References.SnippetFolders);
        Assert.Contains("scope", walk.EdgesOf("assessment"));
        Assert.Empty(V26Package.FlowErrors((manifest, files)));
    }

    [Fact]
    public void Below26_AFolderIsIteratedOnly()
    {
        var (manifest, files) = V25Package.Minimal();
        files["prompts/assessment.md"] = "Assess. {{ snippet.sections.hpi.content }}";
        var nodes = WorkflowDerivedEdges.PrepareNodes(manifest, files, V25Package.Catalog(), out var snippets, out var contextFor);
        var walk = WorkflowDerivedEdges.WalkAll(manifest, files, nodes, snippets, contextFor);

        var prompt = Assert.Single(walk.Templates, t => t.Kind == TemplateKind.Prompt && t.Id == "assessment");
        Assert.Contains("Prompt 'assessment' reads snippet.sections.hpi; a folder is iterated: {{ for s in snippet.sections }}.", prompt.References.Errors);
    }

    // ----- the macro item ---------------------------------------------------------------

    [Fact]
    public void AMacroItem_ReadsDirectly_WithTheMacroMembers()
    {
        Assert.Empty(V26Package.Errors(WorkflowV26MacroFolderTests.WithFolder("{{ macro.section_notes.hpi.prompt }} {{ macro.section_notes.plan.content }} {{ macro.section_notes.plan.instruction }} {{ macro.section_notes.hpi.label }}")));
        Assert.Contains("Node 'assessment' prompt reads macro.section_notes.nope, which folder 'section_notes' does not contain (items: hpi, plan).",
            V26Package.Errors(WorkflowV26MacroFolderTests.WithFolder("{{ macro.section_notes.nope.prompt }}")));

        // Read inline directly: the item's instruction is required (#1096), the reader named.
        var (manifest, files) = WorkflowV26MacroFolderTests.WithFolder("{{ macro.section_notes.plan.prompt }}");
        files["macros/section_notes/plan.md"] = V26Package.Header("Plan notes") + "Plan noted.";
        Assert.Contains("Macro 'section_notes/plan' is read inline by node 'assessment' and declares no instruction; the model is told nothing (macros/section_notes/plan.md).", V26Package.Errors((manifest, files)));
    }

    [Fact]
    public void AMacroItemRead_ReachesTheItem_AndItsGate()
    {
        var (manifest, files) = WorkflowV26MacroFolderTests.WithFolder("{{ macro.section_notes.hpi.prompt }}");
        files["macros/section_notes/hpi.md"] = V26Package.Header("HPI notes", instruction: "Note the history.", @if: "node.scope.value == \"in_scope\"") + "History noted.";
        var nodes = WorkflowDerivedEdges.PrepareNodes(manifest, files, V25Package.Catalog(), out var snippets, out var contextFor, out var macroFolders);
        var walk = WorkflowDerivedEdges.WalkAll(manifest, files, nodes, snippets, contextFor, macros: macroFolders);

        Assert.Contains("section_notes/hpi", walk.PromptReadsOf("assessment"));
        Assert.DoesNotContain("section_notes/plan", walk.PromptReadsOf("assessment"));
        Assert.Contains("scope", walk.EdgesOf("assessment"));
        Assert.Empty(V26Package.Errors((manifest, files)));
    }

    // ----- the renderer ---------------------------------------------------------------------

    [Fact]
    public void TheRenderer_ReadsAnItemOnTheFolder_AndStillIterates()
    {
        var folders = new Dictionary<string, IReadOnlyList<TemplateFolderItem>>
        {
            ["sections"] = new List<TemplateFolderItem> { new("hpi", "HPI", "History."), new("plan", "Plan", "Plan.") },
        };
        var macroFolders = new Dictionary<string, IReadOnlyList<TemplateMacroSource>>
        {
            ["section_notes"] = new List<TemplateMacroSource> { new("hpi", "HPI notes", "History noted.", "Note the history."), new("plan", "Plan notes", "Plan noted.", "Note the plan.") },
        };
        TemplateRenderScope Scope(IReadOnlySet<string>? inert = null) => new(
            new Dictionary<string, string>(), null, null, new Dictionary<string, TemplateNodeOutput>(), new Dictionary<string, string>(),
            folders, null, new Dictionary<string, TemplateMacroSource>(), Run, Profile, InertSnippets: inert, MacroFolders: macroFolders);

        Assert.Equal("History. hpi [History.Plan.] " + WorkflowMacroMechanics.Prompt("plan", "Note the plan.") + " Plan noted.",
            TemplateRenderer.Render("Prompt 'p'", "{{ snippet.sections.hpi.content }} {{ snippet.sections.hpi.id }} [{{ for s in snippet.sections }}{{ s.content }}{{ end }}] {{ macro.section_notes.plan.prompt }} {{ macro.section_notes.plan.content }}", Scope()));
        // An item gated off (#1094, the engine's key) reads empty directly.
        Assert.Equal("[]", TemplateRenderer.Render("Prompt 'p'", "[{{ snippet.sections.hpi.content }}]", Scope(new HashSet<string>(StringComparer.Ordinal) { "sections/hpi" })));
    }
}
