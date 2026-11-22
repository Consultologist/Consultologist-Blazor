using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using static Consultologist.Api.Tests.V31Source.SourceFixture;
using static Consultologist.Api.Tests.V31Templates.TemplateFixture;

namespace Consultologist.Api.Tests.V31Templates;

/// <summary>
/// #1505 (specVersion 31, area 08; part 06 § 6.4; D7, D8, D9): a deliverable's reads fit its format, row by row of
/// § 6.4's table: a <c>markdown_block</c> deliverable reads Markdown and plain text, escaping plain text and data
/// (<c>escapes</c>) and refusing an object or a list (S06-021); a <c>plaintext</c> one reads no Markdown (S06-021) and
/// writes none (S06-022); an object one is one <c>{{ node.&lt;x&gt;.value }}</c> of its format (S06-023). Each package is
/// <see cref="TemplateFixture.Package"/> with the deliverable <c>note</c> of the format and body given, run through every
/// publish step. Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class DeliverableFormatsTests
{
    private const string Note = "deliverables/note.md";

    private const string NoteKey = "deliverable.note";

    /// <summary>
    /// <see cref="TemplateFixture.Package"/> with <c>section_request</c> too, the list input <c>sections</c> of them, the
    /// plain-text agent <c>summary</c>, the <c>markdown_block</c> agent <c>assessment</c>, the <c>markdown_inline</c> agent
    /// <c>headline</c>, the number <c>count</c>, the boolean <c>urgent</c>, the <c>verdict</c> agent <c>judge</c> and the
    /// renderers <c>bullets</c> (plain text to <c>markdown_block</c>) and <c>plan_text</c> (a <c>section_plan</c> to plain text).
    /// </summary>
    private static Dictionary<string, byte[]> Base() =>
        ResolveFixture.WithFormats(Package(), "section_plan", "outline", "verdict", "section_request")
            .WithJson("manifest.json", m => m["inputs"]!.AsObject()["sections"] = System.Text.Json.Nodes.JsonNode.Parse(
                "{ \"label\": \"Sections\", \"description\": null, "
                + "\"format\": { \"type\": \"array\", \"items\": { \"$ref\": \"section_request\" } }, \"required\": true, \"channel\": null }"))
            .WithAgent("summary", "Summarise the plan for Margaret Okafor.\n\n{{ node.plan.content }}\n")
            .WithAgent("assessment", "Write the assessment and plan for Dr A. Sample.\n\n{{ node.plan.content }}\n", "\"markdown_block\"")
            .WithAgent("headline", "Write a one-line headline for the plan.\n\n{{ node.plan.content }}\n", "\"markdown_inline\"")
            .WithComputed("count", "\"number\"", "{ \"size\": [{ \"var\": \"node.plan.value.sections\" }] }")
            .WithComputed("urgent", "\"boolean\"", "{ \"===\": [{ \"var\": \"node.count.value\" }, 0] }")
            .WithJson("nodes/count.md", h => h["modelDerived"] = true)
            .WithJson("nodes/urgent.md", h => h["modelDerived"] = true)
            .WithAgent("judge", "Say whether the plan is good.\n\n{{ node.plan.content }}\n", "\"verdict\"")
            .WithRenderer("bullets", "\"plaintext\"", "- {{ value }}\n")
            .WithRenderer("plan_text", "\"section_plan\"", "{{ for s in value.sections }}{{ s.title }}\n{{ end }}", "plaintext");

    private static Dictionary<string, byte[]> WithNote(string format, string body) =>
        Base().With(Note, Md(Deliverable("Consultation note", $"\"{format}\"", "null", "hold"), body));

    private static SourceCheckResult Accepted(Dictionary<string, byte[]> files)
    {
        var check = SourceCheck.Run(files, ResolveFixture.Published());
        Assert.True(check.Refusals.Count == 0, ResolveFixture.Said(check));
        Assert.NotNull(check.Templates);
        return check;
    }

    private static void AssertRefusal(Dictionary<string, byte[]> files, string id, string sentence)
    {
        var check = SourceCheck.Run(files, ResolveFixture.Published());
        Assert.True(check.Refusals.Count == 1, ResolveFixture.Said(check));
        var refusal = check.Refusals[0];
        V31Contract.AssertSentence(refusal);
        Assert.Equal(sentence, refusal.Sentence);
        Assert.Equal((id, Note), (refusal.Id, refusal.Path));
    }

    // ---- markdown_block -----------------------------------------------------------------------

    [Fact]
    public void AMarkdownDeliverable_ReadsMarkdownAndPlainText()
    {
        var check = Accepted(WithNote("markdown_block",
            "## Plan\n"                                         // 1
            + "\n"                                              // 2
            + "{{ node.assessment.content }}\n"                 // 3: markdown_block, as it is
            + "Headline: {{ node.headline.content }}\n"         // 4: markdown_inline inside a line, as it is
            + "{{ node.summary.content }}\n"                    // 5: plaintext, escaped
            + "Sections: {{ node.count.value }}\n"              // 6: a number, escaped
            + "Urgent: {{ node.urgent.value }}\n"               // 7: a boolean, escaped
            + "{{ node.summary.value | renderer.bullets }}\n"   // 8: the renderer's markdown_block, as it is
            + "{{ for s in input.sections.value }}\n"           // 9
            + "- {{ s.title }}\n"                               // 10: a string, escaped
            + "{{ end }}\n"));

        Assert.Equal([(null, "5:1"), (null, "6:11"), (null, "7:9"), (null, "10:3")], check.Templates!.Escapes(NoteKey));
    }

    [Fact]
    public void ProfileText_InAMarkdownDeliverable_IsEscaped()
    {
        var check = Accepted(WithNote("markdown_block", "## Plan\n\n{{ node.assessment.content }}\n\n{{ profile.signature }}\n"));

        Assert.Equal([(null, "5:1")], check.Templates!.Escapes(NoteKey));
    }

    [Fact]
    public void AMarkdownDeliverable_EscapesInsideTheSnippetsItPlaces()
    {
        // A Markdown snippet is walked where it is placed; a plain-text one is escaped once, whole, at its placement.
        var check = Accepted(WithNote("markdown_block", "## Plan\n{{~ snippet.closing.content }}\n{{~ snippet.sign_off.content }}\n")
            .WithSnippet("closing", "**Next steps:** {{ node.summary.content }}\n", "markdown_block")
            .WithSnippet("sign_off", "Dr A. Sample, {{ node.summary.content }}\n"));

        Assert.Equal([("snippet.closing", "1:17"), (null, "3:1")], check.Templates!.Escapes(NoteKey));
    }

    [Theory]
    [InlineData("{{ node.plan.value }}", "node.plan.value (section_plan)")]
    [InlineData("{{ input.sections.value }}", "input.sections.value (list<section_request>)")]
    public void AMarkdownDeliverable_WritingAnObject_IsRefused(string body, string read) =>
        AssertRefusal(WithNote("markdown_block", $"## Plan\n\n{body}\n"),
            "S06-021", $"Deliverable 'note' is markdown_block, and reads {read} at line 3. Use a renderer or a node.");

    // ---- plaintext ----------------------------------------------------------------------------

    [Fact]
    public void APlaintextDeliverable_ReadsPlainTextAndData()
    {
        var check = Accepted(WithNote("plaintext",
            "Dear Margaret Okafor,\n\n{{ node.summary.content }}\nSections: {{ node.count.value }}\n2 - 1 = 1\n"
            + "Marks: {{ \"**\" }} and {{ \"**\" }}\n{{ node.plan.value | renderer.plan_text }}\n"));

        Assert.Empty(check.Templates!.Escapes(NoteKey));
    }

    [Theory]
    [InlineData("{{ node.assessment.content }}", "node.assessment.content (markdown_block)")]
    [InlineData("{{ node.headline.content }}", "node.headline.content (markdown_inline)")]
    [InlineData("{{ node.summary.value | renderer.bullets }}", "renderer.bullets (markdown_block)")]
    public void APlaintextDeliverable_ReadingMarkdown_IsRefused(string body, string read) =>
        AssertRefusal(WithNote("plaintext", $"Dear Margaret Okafor,\n\n{body}\n"),
            "S06-021", $"Deliverable 'note' is plaintext, and reads {read} at line 3. Convert it in a node.");

    [Fact]
    public void APlaintextDeliverable_PlacingAMarkdownSnippet_IsRefused() =>
        AssertRefusal(
            WithNote("plaintext", "Dear Margaret Okafor,\n\n{{~ snippet.closing.content }}\n")
                .WithSnippet("closing", "## Next steps\n\nSee you soon.\n", "markdown_block"),
            "S06-021", "Deliverable 'note' is plaintext, and reads snippet.closing.content (markdown_block) at line 3. Convert it in a node.");

    [Fact]
    public void APlaintextDeliverable_WritingAnObject_IsRefused() =>
        AssertRefusal(WithNote("plaintext", "Dear Margaret Okafor,\n\n{{ node.plan.value }}\n"),
            "S06-021", "Deliverable 'note' is plaintext, and reads node.plan.value (section_plan) at line 3. Use a renderer or a node.");

    [Theory]
    [InlineData("## Plan", "## Plan")]
    [InlineData("  - item", "- item")]
    [InlineData("> quote", "> quote")]
    [InlineData("* star", "* star")]
    [InlineData("a **b** c", "a **b** c")]
    [InlineData("# Plan for Margaret Okafor, seen by Dr A. Sample in clinic", "# Plan for Margaret Okafor, seen by Dr A")]
    public void APlaintextDeliverable_WritingAHeading_IsRefused(string line, string quoted) =>
        AssertRefusal(WithNote("plaintext", $"Dear Margaret Okafor,\n\nYour plan:\n{line}\n{{{{ node.summary.content }}}}\n"),
            "S06-022", $"Deliverable 'note' is plaintext, and line 4 writes Markdown ('{quoted}').");

    // ---- an object format ---------------------------------------------------------------------

    [Theory]
    [InlineData("{{ node.plan.value }}")]
    [InlineData("{{ node.plan.value }}\n")]
    [InlineData("{{~ node.plan.value ~}}\n")]
    public void AnObjectDeliverable_IsOneValue(string body)
    {
        var check = Accepted(WithNote("section_plan", body));

        Assert.Empty(check.Templates!.Escapes(NoteKey));
    }

    [Theory]
    [InlineData("Plan: {{ node.plan.value }}\n")]
    [InlineData("{{ node.plan.value }}\nThe plan.\n")]
    [InlineData(" {{ node.plan.value }}\n")]
    [InlineData("{{ node.plan.value }}{{ node.plan.value }}\n")]
    [InlineData("{{ node.plan.content }}\n")]
    [InlineData("{{ node.judge.value }}\n")]
    [InlineData("{{ node.plan.value | renderer.plan_text }}\n")]
    [InlineData("The plan.\n")]
    public void AnObjectDeliverable_WritingAnythingElse_IsRefused(string body) =>
        AssertRefusal(WithNote("section_plan", body),
            "S06-023", "Deliverable 'note' is section_plan; its template is {{ node.<x>.value }} and nothing else.");
}
