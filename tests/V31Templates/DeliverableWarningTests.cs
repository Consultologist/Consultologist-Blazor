using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Templates;

/// <summary>
/// #1508 (ruled by the owner, Q3 and Q4 of 2026-11-26; R15, R16): the two publish warnings a new package is told. A11-010: a
/// deliverable with no <c>if</c> and <c>whenEmpty: release</c>, every node or check of which it reads may not run, could be
/// released empty. A11-011: a deliverable's <c>for</c> over a loop's <c>.items</c> that writes fixed text (literal text beyond
/// white space, or a read of anything but the item) outside an <c>if</c> on the item's <c>state</c> writes it for items that
/// did not run too. Each is a warning: the package publishes. Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class DeliverableWarningTests
{
    // ---- A11-010 ----------------------------------------------------------------------------------

    /// <summary>
    /// Minimal with the optional boolean input <c>include_billing</c>, the plan run only when it is true, and the deliverable
    /// <c>note</c> (plain text, reading the plan) released when empty.
    /// </summary>
    private static Dictionary<string, byte[]> MayNotRun() =>
        Replaced(Minimal()
            .WithJson("manifest.json", m => m["inputs"]!["include_billing"] = JsonNode.Parse(
                "{ \"label\": \"Include billing\", \"description\": null, \"format\": \"boolean\", \"required\": false, \"channel\": null }"))
            .WithJson("nodes/plan.md", h => h["if"] = JsonNode.Parse("{ \"===\": [{ \"var\": \"input.include_billing.value\" }, true] }")),
            "deliverables/note.md", "\"whenEmpty\": \"hold\"", "\"whenEmpty\": \"release\"");

    [Fact]
    public void ADeliverableReleasedEmpty_WhoseSourcesMayAllNotRun_IsWarned()
    {
        var (refusals, warnings) = Publish(MayNotRun());

        Assert.True(refusals.Count == 0, TemplateFixture.Said(refusals));
        var warning = Assert.Single(warnings, w => w.Id == "A11-010");
        V31Contract.AssertSentence(warning);
        Assert.Equal("deliverables/note.md", warning.Path);
        Assert.Equal(
            "Deliverable 'note' could be released with nothing in it: everything it reads may not run. Give it an if, or whenEmpty: hold.",
            warning.Sentence);
    }

    [Fact]
    public void ADeliverableWithAnIf_IsNotWarned()
    {
        var files = MayNotRun()
            .WithJson("deliverables/note.md", h => h["if"] = JsonNode.Parse("{ \"===\": [{ \"var\": \"input.include_billing.value\" }, true] }"));

        Assert.DoesNotContain("A11-010", Accepted(files));
    }

    [Fact]
    public void ADeliverableHeldWhenEmpty_IsNotWarned()
    {
        var files = Replaced(MayNotRun(), "deliverables/note.md", "\"whenEmpty\": \"release\"", "\"whenEmpty\": \"hold\"");

        Assert.DoesNotContain("A11-010", Accepted(files));
    }

    [Fact]
    public void ADeliverableReadingOneSourceThatAlwaysRuns_IsNotWarned()
    {
        var files = Replaced(MayNotRun()
            .WithAgent("summary", "Summarise the draft for Dr A. Sample.\n\n{{ input.consult_draft.content }}\n"),
            "deliverables/note.md", "{{ node.plan.content }}\n", "{{ node.plan.content }}\n{{ node.summary.content }}\n");

        Assert.DoesNotContain("A11-010", Accepted(files));
    }

    // ---- A11-011 ----------------------------------------------------------------------------------

    /// <summary><c>example-nested-note</c> with the deliverable <c>note</c> (Markdown, held when empty) whose body is a heading, then <paramref name="loop"/> on line 2.</summary>
    private static Dictionary<string, byte[]> Sections(string loop) =>
        ResolveFixture.NestedNote().With("deliverables/note.md", Md(
            Deliverable("Nested consultation note", "\"markdown_block\"", "null", "hold"), "## Sections\n" + loop + "\n"));

    [Theory]
    [InlineData("{{ for s in node.sections.items }}### {{ s.label }}\n{{ end }}")]
    [InlineData("{{ for s in node.sections.items }}{{ s.content }}{{ node.refine.content }}{{ end }}")]
    [InlineData("{{ for s in node.sections.items }}{{ if for.first }}First.{{ end }}{{ s.content }}{{ end }}")]
    [InlineData("{{ for s in node.sections.items }}{%{### Section}%}{{ s.content }}{{ end }}")]
    [InlineData("{{ for s in node.sections.items }}{{ \"### Section\" }}{{ s.content }}{{ end }}")]
    public void ALoopOverItemsWritingFixedText_IsWarned(string loop)
    {
        var (refusals, warnings) = Publish(Sections(loop));

        Assert.True(refusals.Count == 0, TemplateFixture.Said(refusals));
        var warning = Assert.Single(warnings, w => w.Id == "A11-011");
        V31Contract.AssertSentence(warning);
        Assert.Equal("deliverables/note.md", warning.Path);
        Assert.Equal(
            "Deliverable 'note' writes fixed text for every item of node.sections at line 2, also for items that did not run. Loop over .value, or guard on state.",
            warning.Sentence);
    }

    [Theory]
    [InlineData("{{ for s in node.sections.value }}### Section\n{{ end }}")]
    [InlineData("{{ for s in node.sections.items }}{{ if s.state == \"completed\" }}### {{ s.label }}\n{{ node.refine.content }}{{ else }}{{ end }}{{ end }}")]
    [InlineData("{{ for s in node.sections.items }}{{ if s.state != \"completed\" }}{{ else }}### {{ s.label }}\n{{ end }}{{ end }}")]
    [InlineData("{{ for s in node.sections.items }}{{ s.label }} {{ s.content }}\n{{ end }}")]
    [InlineData("{{ for s in node.sections.items }}{{ for p in s.value }}{{ p }}\n{{ end }}{{ end }}")]
    public void ALoopOverValue_OrGuardedOnState_OrOnlyTheItemsOwnReads_IsNotWarned(string loop)
    {
        Assert.DoesNotContain("A11-011", Accepted(Sections(loop)));
    }

    [Fact]
    public void AnIfOnAnotherLoopsState_DoesNotGuard()
    {
        var loop = "{{ for s in node.sections.items }}\n{{ for t in node.sections.items }}"
            + "{{ if t.state == \"completed\" }}### {{ s.label }}\n{{ else }}{{ end }}{{ end }}{{ end }}";

        var (refusals, warnings) = Publish(Sections(loop));

        Assert.True(refusals.Count == 0, TemplateFixture.Said(refusals));
        // The outer loop (line 2) writes fixed text for every item; the inner (line 3) guards on its own item's state.
        var warning = Assert.Single(warnings, w => w.Id == "A11-011");
        Assert.Contains(" at line 2,", warning.Sentence, StringComparison.Ordinal);
    }

    private static (IReadOnlyList<SourceRefusal> Refusals, IReadOnlyList<SourceRefusal> Warnings) Publish(Dictionary<string, byte[]> files)
    {
        var (refusals, compiled) = CompiledPackage.Publish(files, ResolveFixture.Published(), CompiledGoldenTests.Fixed);
        return (refusals, compiled?.Warnings ?? []);
    }

    /// <summary>The file <paramref name="path"/> with <paramref name="old"/> (asserted there) replaced by <paramref name="replacement"/>.</summary>
    private static Dictionary<string, byte[]> Replaced(Dictionary<string, byte[]> files, string path, string old, string replacement)
    {
        var text = Text(files, path);
        Assert.Contains(old, text, StringComparison.Ordinal);
        return files.With(path, text.Replace(old, replacement, StringComparison.Ordinal));
    }

    /// <summary>The ids of the warnings of <paramref name="files"/>, which publish accepts.</summary>
    private static List<string> Accepted(Dictionary<string, byte[]> files)
    {
        var (refusals, warnings) = Publish(files);
        Assert.True(refusals.Count == 0, TemplateFixture.Said(refusals));
        return warnings.Select(w => w.Id).ToList();
    }
}
