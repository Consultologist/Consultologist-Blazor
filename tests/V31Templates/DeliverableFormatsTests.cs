using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Run;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using Consultologist.PackageFormat.V31.Templates;
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
    /// <see cref="TemplateFixture.Package"/> with <c>section_request</c> and <c>standard_section</c> too, the list input <c>sections</c> of them, the
    /// plain-text agent <c>summary</c>, the <c>markdown_block</c> agent <c>assessment</c>, the <c>markdown_inline</c> agent
    /// <c>headline</c>, the number <c>count</c>, the boolean <c>urgent</c>, the <c>verdict</c> agent <c>judge</c> and the
    /// renderers <c>bullets</c> (plain text to <c>markdown_block</c>) and <c>plan_text</c> (a <c>section_plan</c> to plain text).
    /// </summary>
    private static Dictionary<string, byte[]> Base() =>
        ResolveFixture.WithFormats(Package(), "section_plan", "outline", "verdict", "section_request", "standard_section")
            .WithJson("manifest.json", m => m["inputs"]!.AsObject()["sections"] = JsonNode.Parse(
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

    /// <summary>
    /// <paramref name="files"/> published and loaded as the engine loads them, and the deliverable composed as the run
    /// composes it over the data <paramref name="paths"/> give; its text, asserted composed.
    /// </summary>
    private static string Composed(Dictionary<string, byte[]> files, params (string Path, JsonNode? Value)[] paths)
    {
        var outcome = RenderTurn.Render(RunFixture.Plan(files), new RenderCall(NoteKey, NoteKey, null, BodyComposerTests.Scope(paths), 100_000));
        Assert.True(outcome.Failure == null, outcome.Failure);
        Assert.NotNull(outcome.Text);
        return outcome.Text!;
    }

    [Theory]
    [InlineData("# not a heading")]
    [InlineData("**x**")]
    public void AnUntypedRead_InAMarkdownDeliverable_IsEscapedAsAString(string value)
    {
        // #1507, 2026-11-22 (2), 3 (R10): formats fail closed. An output publish cannot type (an interpolated string: its
        // format is null) is escaped as the string it writes; before, it was neither judged nor escaped, so model text
        // could write Markdown into the document (part 06 § 6.4).
        var files = WithNote("markdown_block", "## Plan\n\n{{ $\"{node.summary.content}\" }}\n");

        Assert.Equal([(null, "3:1")], Accepted(files).Templates!.Escapes(NoteKey));
        Assert.Equal($"## Plan\n\n{MarkdownEscape.Escape(value)}\n", Composed(files, ("node.summary.content", value)));
    }

    [Theory]
    [InlineData("# not a heading")]
    [InlineData("**x**")]
    public void AUnionRead_InAMarkdownDeliverable_IsEscapedAsAString(string value)
    {
        // R10: a union is escaped as a string, a nullable plain text (plaintext | null, an item's .content) as any plain
        // text; before, the union's text kind was not seen through, and it was written as Markdown.
        var files = WithNote("markdown_block", "## Plan\n{{ for d in node.drafts.items }}{{ d.content }}\n{{ end }}")
            .WithLoop("drafts", "node.plan.value.sections", "draft", "Draft the section {{ item.value.title }}.\n");

        Assert.Equal([(null, "2:33")], Accepted(files).Templates!.Escapes(NoteKey));
        var items = new JsonArray(new JsonObject { ["content"] = value }, new JsonObject { ["content"] = null });
        Assert.Equal($"## Plan\n{MarkdownEscape.Escape(value)}\n\n", Composed(files, ("node.drafts.items", items)));
    }

    /// <summary>
    /// <see cref="SourceFixture.Minimal"/> with the list input <c>sections</c> (of <c>section_request</c>) and the loop
    /// <c>drafts</c> over it, labelled by its title, whose body is the agent <c>draft</c> of format
    /// <paramref name="itemFormat"/>; the deliverable <c>note</c> of <paramref name="format"/> is <paramref name="body"/>.
    /// </summary>
    private static Dictionary<string, byte[]> OverALoop(string format, string body, string itemFormat = "markdown_block") =>
        ResolveFixture.WithFormats(Minimal(), "section_request")
            .WithJson("manifest.json", m => m["inputs"]!.AsObject()["sections"] = JsonNode.Parse(
                "{ \"label\": \"Sections\", \"description\": null, "
                + "\"format\": { \"type\": \"array\", \"items\": { \"$ref\": \"section_request\" } }, \"required\": true, \"channel\": null }"))
            .With("nodes/index.json", Index(["plan", "drafts"]))
            .With("nodes/drafts/index.json", Json(SourceFixture.Node("for", "Sections", ArrayOf($"\"{itemFormat}\""), false, true,
                "\"in\": \"input.sections.value\", \"parallel\": true, \"maxParallel\": null, \"until\": null, \"max\": null, "
                + "\"itemLabel\": \"title\", \"nodes\": [\"draft\"], \"ret\": \"node.drafts.draft\"")))
            .With("nodes/drafts/draft.md", Agent("Drafting a section", $"\"{itemFormat}\"", "Write the section '{{ item.value.title }}'.\n"))
            .With(Note, Md(Deliverable("Consultation note", $"\"{format}\"", "null", "hold"), body));

    [Theory]
    [InlineData("markdown_block")]
    [InlineData("plaintext")]
    public async Task ALoopsContent_InAMarkdownDeliverable_IsNotEscaped(string itemFormat)
    {
        // #1507 (D23; part 04 § 2.5, part 05 § 5.3): a loop's .content is 30's bytes, a standard rendering typed
        // markdown_block, whatever its items' format: written as it is, ## headers and the items' Markdown included.
        var files = OverALoop("markdown_block", "{{ node.drafts.content }}\n", itemFormat);
        Assert.Empty(Accepted(files).Templates!.Escapes(NoteKey));

        var plan = RunFixture.Plan(files);
        var replies = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["node.drafts.draft:1"] = "Margaret Okafor reports **three months** of fatigue.",
            ["node.drafts.draft:2"] = "Repeat TSH in **six weeks** with Dr A. Sample.",
        };
        var host = new ComposingRunHost(plan, (call, _) => replies.TryGetValue(call.Key, out var reply) ? reply : RunFixture.StubReply(plan, call));
        var inputs = PlanRunnerLeavesTests.Inputs();
        var given = new Dictionary<string, JsonNode?>(inputs.Inputs, StringComparer.Ordinal)
        {
            ["sections"] = new JsonObject
            {
                ["value"] = new JsonArray(
                    new JsonObject { ["title"] = "History", ["kind"] = "narrative" },
                    new JsonObject { ["title"] = "Plan", ["kind"] = "narrative" }),
                ["content"] = null,
                ["title"] = null,
            },
        };

        var result = await new PlanRunner(plan, new RunLimits(), host, new ListRecorder()).RunAsync(inputs with { Inputs = given }, FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(
            "## History\n\nMargaret Okafor reports **three months** of fatigue.\n\n"
            + "## Plan\n\nRepeat TSH in **six weeks** with Dr A. Sample.\n",
            (string?)result.Values[NoteKey]!["content"]);
    }

    [Fact]
    public async Task AFlowOverALoop_HoldsTheLoopsRendering_AndIsNotEscaped()
    {
        // #1507 (D23; Task 24's fix round 1): a flow's .content is its ret's, so an if, a case or a while whose ret is a
        // for, and an outer for's item whose ret is an inner for, hold that loop's standard rendering byte for byte at run
        // time; their .content is typed markdown_block, and the Markdown note writes it as it is. The if whose branches are
        // a for and a plain-text line is the union, escaped (R10): the note's one escape.
        var files = ResolveFixture.LoopFlows();
        Assert.Equal([(null, "9:10")], Accepted(files).Templates!.Escapes(NoteKey));

        var plan = RunFixture.Plan(files);
        var host = new ComposingRunHost(plan, (call, _) => call.EntryKey.EndsWith("_draft", StringComparison.Ordinal) || call.EntryKey.EndsWith(".note", StringComparison.Ordinal)
            ? $"**{call.Key}** for Margaret Okafor."
            : RunFixture.StubReply(plan, call));
        var inputs = PlanRunnerLeavesTests.Inputs();
        JsonObject Given(JsonNode value) => new() { ["value"] = value, ["content"] = null, ["title"] = null };
        var given = new Dictionary<string, JsonNode?>(inputs.Inputs, StringComparer.Ordinal)
        {
            ["sections"] = Given(new JsonArray(
                new JsonObject { ["title"] = "History", ["kind"] = "narrative" },
                new JsonObject { ["title"] = "Plan", ["kind"] = "narrative" })),
            ["visits"] = Given(new JsonArray(
                new JsonObject { ["title"] = "First visit", ["points"] = new JsonArray("fatigue", "weight") },
                new JsonObject { ["title"] = "Second visit", ["points"] = new JsonArray("TSH") })),
            ["tone"] = Given("formal"),
        };

        var result = await new PlanRunner(plan, new RunLimits(), host, new ListRecorder()).RunAsync(inputs with { Inputs = given }, FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        string Content(string key) => (string)result.Values[key]!["content"]!;
        // each item's reply names its row key (a pass of the while carries its number, #1)
        string Rendering(string loop, string pass = "") =>
            $"## History\n\n**{loop}.{loop.Split('.')[^1]}_draft{pass}:1** for Margaret Okafor.\n\n"
            + $"## Plan\n\n**{loop}.{loop.Split('.')[^1]}_draft{pass}:2** for Margaret Okafor.";
        Assert.Equal(Rendering("node.pick.then_loop"), Content("node.pick"));
        Assert.Equal(Rendering("node.choose.formal_loop"), Content("node.choose"));
        Assert.Equal(Rendering("node.redo.redo_loop", "#1"), Content("node.redo"));
        var visits = result.Values["node.visits"]!["items"]!.AsArray();
        foreach (var visit in visits)
        {
            Assert.StartsWith("## Points 1\n\n**node.visits.points.note:", (string)visit!["content"]!, StringComparison.Ordinal);
            Assert.Equal((string?)visit["nodes"]!["points"]!["content"], (string?)visit["content"]);
        }

        Assert.Equal(
            $"{Content("node.pick")}\n\n{Content("node.choose")}\n\n{Content("node.redo")}\n\n"
            + $"{(string)visits[0]!["content"]!}\n\n{(string)visits[1]!["content"]!}\n\n{MarkdownEscape.Escape(Content("node.mixed"))}\n",
            Content(NoteKey));
    }

    [Fact]
    public void ALoopsContent_InAPlaintextDeliverable_IsRefused() =>
        // part 06 § 6.4: a plain-text deliverable reads no Markdown, and a loop's .content is markdown_block (D23)
        AssertRefusal(OverALoop("plaintext", "Dear Margaret Okafor,\n\n{{ node.drafts.content }}\n", "plaintext"),
            "S06-021", "Deliverable 'note' is plaintext, and reads node.drafts.content (markdown_block) at line 3. Convert it in a node.");

    [Theory]
    [InlineData("{{ node.plan.value }}", "node.plan.value (section_plan)")]
    [InlineData("{{ input.sections.value }}", "input.sections.value (list<section_request>)")]
    public void AMarkdownDeliverable_WritingAnObject_IsRefused(string body, string read) =>
        AssertRefusal(WithNote("markdown_block", $"## Plan\n\n{body}\n"),
            "S06-021", $"Deliverable 'note' is markdown_block, and reads {read} at line 3. Use a renderer or a node.");

    /// <summary>
    /// <see cref="WithNote"/> with the snippet folder <c>standards</c> of the header format <paramref name="folderFormat"/>
    /// holding <c>hpi</c> (header <paramref name="header"/>), whose body is Markdown.
    /// </summary>
    private static Dictionary<string, byte[]> WithFolder(string format, string body, string folderFormat, string header) =>
        WithNote(format, body)
            .Listed("snippets/index.json", "standards")
            .With("snippets/standards/index.json", Index(["hpi"], extra: $", \"format\": \"{folderFormat}\""))
            .With("snippets/standards/hpi.md", Md(header, "**Open** with a one-sentence orientation to Margaret Okafor.\n"));

    /// <summary>The folder formats of <see cref="AFolderItemsContent_IsMarkdownBlock"/>: the header's format is not the body's.</summary>
    public static TheoryData<string, string> FolderFormats => new()
    {
        { "standard_section", "{ \"title\": \"History of Present Illness\" }" },
        { "verdict", "{ \"verdict\": \"good\" }" },
    };

    [Theory]
    [MemberData(nameof(FolderFormats))]
    public void AFolderItemsContent_IsMarkdownBlock(string folderFormat, string header)
    {
        // #1505 ruling 2 (R10): a folder item's .content is markdown_block, whatever the folder's format, which types its
        // header only. A deliverable reaches a folder item as snippet.<folder>.<item>.content (a body for over a folder is
        // S05-037): written as it is, in a line or on its own, and never escaped.
        var check = Accepted(WithFolder("markdown_block",
            "## Plan\n\n{{ snippet.standards.hpi.content }}\nAlso: {{ snippet.standards.hpi.content }}\n", folderFormat, header));

        Assert.Empty(check.Templates!.Escapes(NoteKey));

        AssertRefusal(WithFolder("plaintext", "Dear Margaret Okafor,\n\n{{ snippet.standards.hpi.content }}\n", folderFormat, header),
            "S06-021", "Deliverable 'note' is plaintext, and reads snippet.standards.hpi.content (markdown_block) at line 3. Convert it in a node.");
    }

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
    // An astral character across the 40th character is left out whole, not split into half a surrogate pair.
    [InlineData("# Plan for Margaret Okafor, seen by Dr \U0001F600 in clinic", "# Plan for Margaret Okafor, seen by Dr ")]
    public void APlaintextDeliverable_WritingAHeading_IsRefused(string line, string quoted) =>
        AssertRefusal(WithNote("plaintext", $"Dear Margaret Okafor,\n\nYour plan:\n{line}\n{{{{ node.summary.content }}}}\n"),
            "S06-022", $"Deliverable 'note' is plaintext, and line 4 writes Markdown ('{quoted}').");

    [Theory]
    [InlineData("# Heading")]
    [InlineData("- item")]
    [InlineData("* star")]
    [InlineData("> quote")]
    [InlineData("a **b** c")]
    [InlineData(" # Heading")]
    public void ABlockMarkerOpenedByAnIf_IsRefused(string marker)
    {
        // R10: D9 sees through the tags that write nothing (an if, its else and end, a for) to the line's first character,
        // so a marker an if opens is S06-022, as the bare line is. The line is quoted as written, trimmed.
        var line = $"{{{{ if for.first }}}}{marker}{{{{ end }}}}";
        AssertRefusal(WithNote("plaintext", $"Dear Margaret Okafor,\n\n{{{{ for s in node.plan.value.sections }}}}\n{line}\n{{{{ end }}}}\n"),
            "S06-022", $"Deliverable 'note' is plaintext, and line 4 writes Markdown ('{line.Trim()}').");
    }

    [Theory]
    [InlineData("{{ s.title }}# 1")]
    [InlineData("{{ if for.first }}{{ s.title }}- 1{{ end }}")]
    [InlineData("{{ if for.first }}Title{{ end }}")]
    public void ATagWritingAValue_AtTheLinesStart_IsNoMarker(string line)
    {
        // R10: what an output writes stands before the character after it, so that character does not start the line.
        var check = Accepted(WithNote("plaintext", $"Dear Margaret Okafor,\n\n{{{{ for s in node.plan.value.sections }}}}\n{line}\n{{{{ end }}}}\n"));

        Assert.Empty(check.Templates!.Escapes(NoteKey));
    }

    // ---- an object format ---------------------------------------------------------------------

    [Theory]
    [InlineData("{{ node.plan.value }}")]
    [InlineData("{{ node.plan.value }}\n")]
    [InlineData("{{~ node.plan.value ~}}\n")]
    public void AnObjectDeliverable_IsOneValue(string body)
    {
        var check = Accepted(WithNote("section_plan", body));

        Assert.Empty(check.Templates!.Escapes(NoteKey));
        Assert.Equal("node.plan.value", SourceTemplates.OneValueRead(TemplateSyntax.Parse(body)));
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
    public void AnObjectDeliverable_WritingAnythingElse_IsRefused(string body)
    {
        AssertRefusal(WithNote("section_plan", body),
            "S06-023", "Deliverable 'note' is section_plan; its template is {{ node.<x>.value }} and nothing else.");

        // The form is one definition, which the run reads too: only a read of another format is of the form and refused.
        Assert.Equal(body == "{{ node.judge.value }}\n", SourceTemplates.OneValueRead(TemplateSyntax.Parse(body)) is not null);
    }

    [Fact]
    public void AnObjectDeliverable_WhoseReadFailsTyping_IsRefusedOnce()
    {
        // The form is right; the read's own refusal is the one said, and S06-023 is not said on top of it.
        var check = SourceCheck.Run(WithNote("section_plan", "{{ node.missing.value }}\n"), ResolveFixture.Published());

        Assert.True(check.Refusals.Count == 1, ResolveFixture.Said(check));
        Assert.NotEqual("S06-023", check.Refusals[0].Id);
        V31Contract.AssertSentence(check.Refusals[0]);
    }

    [Fact]
    public void AnUntypedUnrefusedRead_NeverReachesAnObjectDeliverable()
    {
        // The one way a node's read comes back untyped with no refusal of its own (a deferred head: a loop whose source is
        // refused) is refused by the resolver, and publish stops there: the dialect never judges the deliverable. Were it to,
        // OneValue would say S06-023 for it, as no refusal stands for it in the body.
        var check = SourceCheck.Run(
            WithNote("section_plan", "{{ node.drafts.value }}\n").WithLoop("drafts", "node.nothing.value", "draft", "Draft a section.\n"),
            ResolveFixture.Published());

        var said = Assert.Single(check.Refusals);
        Assert.Equal(("S03-014", "nodes/drafts/index.json"), (said.Id, said.Path));
        Assert.Null(check.Templates);
    }
}
