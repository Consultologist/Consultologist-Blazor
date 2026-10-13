using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// A2 (#997): cycles over the derived reference graph, named by path, and
/// the transitive rule — no path from a macro's text to a .prompt read
/// (design § 5.2).
/// </summary>
public class TemplateReferenceGraphTests
{
    private static TemplateReferences Refs(string template, string subject = "Template 't'") =>
        TemplateReferenceWalker.Walk(subject, template, Context());

    private static TemplateReferenceContext Context() => TemplateFixtures.Context() with
    {
        Snippets = new HashSet<string>(StringComparer.Ordinal) { "a", "b", "c", "standards", "intro" },
        SnippetFolders = new HashSet<string>(StringComparer.Ordinal) { "sections" },
        Macros = new HashSet<string>(StringComparer.Ordinal) { "m", "n", "closing", "letrozole" },
        MacroFolders = new HashSet<string>(StringComparer.Ordinal) { "addenda" },
    };

    [Fact]
    public void ATreeOfReads_HasNoCycle_AndNoPromptReach()
    {
        var graph = new TemplateReferenceGraph();
        graph.Add(TemplateKind.Prompt, "p", Refs("{{ snippet.a.text }} {{ macro.m.text }} {{ macro.letrozole.prompt }}"));
        graph.Add(TemplateKind.Snippet, "a", Refs("{{ snippet.b.text }} {{ input.notes }}"));
        graph.Add(TemplateKind.Snippet, "b", Refs("plain"));
        graph.Add(TemplateKind.Macro, "m", Refs("{{ snippet.b.text }} {{ node.assessment.text }}"));

        Assert.False(graph.TryFindCycle(out _));
        Assert.False(graph.TryFindMacroTextReachingPrompt(out _, out _));
    }

    [Fact]
    public void ASnippetCycle_IsNamedByItsPath()
    {
        var graph = new TemplateReferenceGraph();
        graph.Add(TemplateKind.Snippet, "a", Refs("{{ snippet.b.text }}"));
        graph.Add(TemplateKind.Snippet, "b", Refs("{{ snippet.c.text }}"));
        graph.Add(TemplateKind.Snippet, "c", Refs("{{ snippet.a.text }}"));

        Assert.True(graph.TryFindCycle(out var path));
        Assert.Equal("snippet 'a' → snippet 'b' → snippet 'c' → snippet 'a'", TemplateReferenceGraph.Describe(path));
    }

    [Fact]
    public void AMacroThroughASnippet_Cycles()
    {
        var graph = new TemplateReferenceGraph();
        graph.Add(TemplateKind.Prompt, "p", Refs("{{ macro.m.text }}"));
        graph.Add(TemplateKind.Macro, "m", Refs("{{ snippet.a.text }}"));
        graph.Add(TemplateKind.Snippet, "a", Refs("{{ macro.m.text }}"));

        Assert.True(graph.TryFindCycle(out var path));
        Assert.Equal("macro 'm' → snippet 'a' → macro 'm'", TemplateReferenceGraph.Describe(path));
    }

    [Fact]
    public void ASelfRead_IsACycleOfOne()
    {
        var graph = new TemplateReferenceGraph();
        graph.Add(TemplateKind.Snippet, "a", Refs("{{ snippet.a.text }}"));

        Assert.True(graph.TryFindCycle(out var path));
        Assert.Equal("snippet 'a' → snippet 'a'", TemplateReferenceGraph.Describe(path));
    }

    [Fact]
    public void AFolderRead_EdgesToEveryMember()
    {
        var graph = new TemplateReferenceGraph();
        graph.AddSnippetFolder("sections", new[] { "a", "b" });
        graph.Add(TemplateKind.Snippet, "a", Refs("{{ for s in snippet.sections }}{{ s.text }}{{ end }}"));
        graph.Add(TemplateKind.Snippet, "b", Refs("plain"));

        Assert.True(graph.TryFindCycle(out var path));
        Assert.Equal("snippet 'a' → snippet 'a'", TemplateReferenceGraph.Describe(path));
    }

    [Fact]
    public void MacroPromptReads_AreNotEdges_SoAPromptReadingASlotIsFine()
    {
        var graph = new TemplateReferenceGraph();
        graph.Add(TemplateKind.Prompt, "p", Refs("{{ macro.m.prompt }}"));
        graph.Add(TemplateKind.Macro, "m", Refs("{{ snippet.a.text }}"));
        graph.Add(TemplateKind.Snippet, "a", Refs("{{ macro.m.prompt }}"));

        // The snippet reads m's prompt; the prompt reads the snippet only by
        // way of nothing — but m's TEXT reads the snippet, which is the rule below.
        Assert.False(graph.TryFindCycle(out _));
    }

    [Fact]
    public void AMacroText_CarryingAPromptRead_IsRefused_Directly()
    {
        var graph = new TemplateReferenceGraph();
        graph.Add(TemplateKind.Macro, "m", Refs("Also: {{ macro.letrozole.prompt }}"));

        Assert.True(graph.TryFindMacroTextReachingPrompt(out var path, out var promptMacro));
        Assert.Equal("macro 'm'", TemplateReferenceGraph.Describe(path));
        Assert.Equal("letrozole", promptMacro);
    }

    [Fact]
    public void AMacroText_ReachingAPromptRead_ThroughASnippet_IsRefused()
    {
        var graph = new TemplateReferenceGraph();
        graph.Add(TemplateKind.Prompt, "p", Refs("{{ snippet.standards.text }}"));
        graph.Add(TemplateKind.Snippet, "standards", Refs("{{ snippet.intro.text }}"));
        graph.Add(TemplateKind.Snippet, "intro", Refs("{{ macro.letrozole.prompt }}"));
        graph.Add(TemplateKind.Macro, "closing", Refs("{{ snippet.standards.text }}"));

        Assert.True(graph.TryFindMacroTextReachingPrompt(out var path, out var promptMacro));
        Assert.Equal("macro 'closing' → snippet 'standards' → snippet 'intro'", TemplateReferenceGraph.Describe(path));
        Assert.Equal("letrozole", promptMacro);
    }

    [Fact]
    public void AMacroText_ReachingAPromptRead_ThroughAMacroFolder_IsRefused()
    {
        var graph = new TemplateReferenceGraph();
        graph.AddMacroFolder("addenda", new[] { "n" });
        graph.Add(TemplateKind.Macro, "m", Refs("{{ for x in macro.addenda }}{{ x.text }}{{ end }}"));
        graph.Add(TemplateKind.Macro, "n", Refs("{{ macro.letrozole.prompt }}"));

        Assert.True(graph.TryFindMacroTextReachingPrompt(out var path, out _));
        Assert.Equal("macro 'm' → macro 'n'", TemplateReferenceGraph.Describe(path));
    }

    [Fact]
    public void ASnippetWithAPromptRead_ReadOnlyByPrompts_IsFine()
    {
        var graph = new TemplateReferenceGraph();
        graph.Add(TemplateKind.Prompt, "p", Refs("{{ snippet.intro.text }}"));
        graph.Add(TemplateKind.Snippet, "intro", Refs("{{ macro.letrozole.prompt }}"));
        graph.Add(TemplateKind.Macro, "closing", Refs("{{ snippet.standards.text }}"));
        graph.Add(TemplateKind.Snippet, "standards", Refs("plain"));

        Assert.False(graph.TryFindMacroTextReachingPrompt(out _, out _));
    }

    [Fact]
    public void AnUnknownTarget_IsSkipped_NotAnError()
    {
        var graph = new TemplateReferenceGraph();
        graph.Add(TemplateKind.Prompt, "p", Refs("{{ snippet.a.text }}"));

        Assert.False(graph.TryFindCycle(out _));
        Assert.False(graph.TryFindMacroTextReachingPrompt(out _, out _));
    }

    [Fact]
    public void ATemplate_IsAddedOnce()
    {
        var graph = new TemplateReferenceGraph();
        graph.Add(TemplateKind.Snippet, "a", Refs("plain"));

        Assert.Throws<ArgumentException>(() => graph.Add(TemplateKind.Snippet, "a", Refs("plain")));
    }
}
