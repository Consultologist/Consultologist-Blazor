using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using Consultologist.PackageFormat.V31.Templates;
using static Consultologist.Api.Tests.V31Source.SourceFixture;
using static Consultologist.Api.Tests.V31Templates.TemplateFixture;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1505 (specVersion 31, area 08; part 06 §§ 6.4, 9; D7, D8): a deliverable composed at run time. A Markdown deliverable
/// escapes each output its <c>escapes</c> lists where it is written (each pass of a <c>for</c> on its own, a <c>null</c>
/// writing nothing, a placed plain-text snippet once and whole) and writes Markdown and a renderer's output as they are;
/// its hash is of the escaped text. An object deliverable is its value's canonical JSON. A document whose deliverable lists
/// no escapes composes as before. Each package is published and loaded as the engine loads it (<see cref="RunFixture.Plan"/>)
/// and rendered as the run renders it (<see cref="RenderTurn.Render"/>). Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class DeliverableComposeTests
{
    private const string Note = "deliverable.note";

    /// <summary>
    /// <see cref="TemplateFixture.Package"/> with the list input <c>lines</c> (nullable strings), the plain-text agent
    /// <c>summary</c>, the <c>markdown_block</c> agent <c>assessment</c> and the renderer <c>bullets</c> (plain text to
    /// <c>markdown_block</c>), and the deliverable <c>note</c> of <paramref name="format"/> and <paramref name="body"/>.
    /// </summary>
    private static Dictionary<string, byte[]> WithNote(string format, string body) =>
        Package()
            .WithJson("manifest.json", m => m["inputs"]!.AsObject()["lines"] = JsonNode.Parse(
                "{ \"label\": \"Lines\", \"description\": null, "
                + "\"format\": { \"type\": \"array\", \"items\": { \"type\": [\"string\", \"null\"] } }, \"required\": true, \"channel\": null }"))
            .WithAgent("summary", "Summarise the plan for Margaret Okafor.\n\n{{ node.plan.content }}\n")
            .WithAgent("assessment", "Write the assessment and plan for Dr A. Sample.\n\n{{ node.plan.content }}\n", "\"markdown_block\"")
            .WithRenderer("bullets", "\"plaintext\"", "- {{ value }}\n")
            .With("deliverables/note.md", Md(Deliverable("Consultation note", $"\"{format}\"", "null", "hold"), body));

    private static RenderOutcome Render(RunPlan plan, JsonObject scope, IReadOnlyDictionary<string, bool>? placed = null) =>
        RenderTurn.Render(plan, new RenderCall(Note, Note, null, scope, 100_000, placed));

    /// <summary>The outcome's text, asserted composed, its <c>inputHash</c> the hash of that text.</summary>
    private static string Composed(RenderOutcome outcome)
    {
        Assert.True(outcome.Failure == null, outcome.Failure);
        Assert.False(outcome.TooManySteps);
        Assert.NotNull(outcome.Text);
        Assert.Equal(CanonicalJson.HashText(outcome.Text!), (string?)outcome.Detail!["inputHash"]);
        return outcome.Text!;
    }

    [Fact]
    public void PlainText_IsEscaped_PerItem()
    {
        // Review Focus 3: each pass escapes its own item; a line end stays a line end; null writes nothing.
        var plan = RunFixture.Plan(WithNote("markdown_block", "## Items\n{{ for t in input.lines.value }}<{{ t }}>\n{{ end }}"));
        Assert.Equal("[{\"at\":\"2:34\",\"unit\":null}]", CanonicalJson.Write(plan.Entries[Note].Json["escapes"]));

        var outcome = Render(plan, BodyComposerTests.Scope(("input.lines.value", new JsonArray("# 1", "*bold*", "a\nb", null))));

        var text = Composed(outcome);
        Assert.Equal("## Items\n<\\# 1>\n<\\*bold\\*>\n<a\nb>\n<>\n", text);
        Assert.Equal(CanonicalJson.HashText(text), (string?)outcome.Detail!["inputHash"]);
    }

    [Fact]
    public void MarkdownReads_AreWrittenAsTheyAre()
    {
        var plan = RunFixture.Plan(WithNote("markdown_block", "{{ node.assessment.content }}\n{{ node.summary.content }}\n"));
        const string written = "**Plan:** review in clinic_1 (Dr A. Sample).";

        var text = Composed(Render(plan, BodyComposerTests.Scope(("node.assessment.content", written), ("node.summary.content", written))));

        Assert.Equal($"{written}\n\\*\\*Plan\\:\\*\\* review in clinic\\_1 \\(Dr A\\. Sample\\)\\.\n", text);
    }

    [Fact]
    public void APlacedPlainTextSnippet_IsEscapedOnce()
    {
        // The Markdown snippet's own plain read is escaped where it is written; the plain-text snippet is escaped whole, at
        // its placement, so the read inside it is not escaped twice.
        var plan = RunFixture.Plan(WithNote("markdown_block", "## Plan\n{{~ snippet.closing.content }}\n{{~ snippet.sign_off.content }}\n")
            .WithSnippet("closing", "**Next steps:** {{ node.summary.content }}", "markdown_block")
            .WithSnippet("sign_off", "Dr A. Sample, {{ node.summary.content }}"));
        Assert.Equal(
            "[{\"at\":\"1:17\",\"unit\":\"snippet.closing\"},{\"at\":\"3:1\",\"unit\":null}]",
            CanonicalJson.Write(plan.Entries[Note].Json["escapes"]));

        var text = Composed(Render(
            plan, BodyComposerTests.Scope(("node.summary.content", "see *you* soon")), BodyComposerTests.Placed("snippet.closing", "snippet.sign_off")));

        Assert.Equal("## Plan\n**Next steps:** see \\*you\\* soon\nDr A\\. Sample\\, see \\*you\\* soon\n", text);
    }

    [Fact]
    public void ARenderersOutput_IsNotEscaped()
    {
        var plan = RunFixture.Plan(WithNote("markdown_block", "{{ node.summary.value | renderer.bullets }}"));
        Assert.Equal("[]", CanonicalJson.Write(plan.Entries[Note].Json["escapes"]));

        var text = Composed(Render(plan, BodyComposerTests.Scope(("node.summary.value", "*urgent* review"))));

        Assert.Equal("- *urgent* review\n", text);
    }

    [Fact]
    public void AnEscapedRunSpot_RecordsItsEscapedOutput_InBothTexts()
    {
        // A run spot the deliverable escapes: its output is escaped in the text, recorded escaped, and its run-neutral
        // output (the read node's neutral text) is escaped too, its marker kept.
        var plan = RunFixture.Plan(WithNote("markdown_block", "## Seen\n{{ node.stamp.content }}\n")
            .WithRendered("stamp", "Seen {{ run.date }} (clinic).\n").RunDependent("nodes/stamp.md"));
        var scope = BodyComposerTests.Scope(("node.stamp.content", "Seen 2026-11-21 (clinic).\n"), ("node.stamp.neutral", "Seen {{run:1}} (clinic).\n"));

        var outcome = Render(plan, scope);

        Assert.Equal("## Seen\nSeen 2026\\-11\\-21 \\(clinic\\)\\.\n\n", Composed(outcome));
        Assert.Equal("## Seen\nSeen {{run:1}} \\(clinic\\)\\.\n\n", outcome.NeutralText);
        Assert.Equal(CanonicalJson.HashText(outcome.NeutralText!), (string?)outcome.Detail!["runNeutralHash"]);
        var spot = Assert.Single(outcome.Detail!["runSpots"]!.AsArray())!;
        Assert.Equal(CanonicalJson.HashText("Seen 2026\\-11\\-21 \\(clinic\\)\\.\n"), (string?)spot["outputHash"]);
    }

    [Fact]
    public void AnObjectDeliverable_ComposesCanonicalJson()
    {
        var plan = RunFixture.Plan(WithNote("section_plan", "{{ node.plan.value }}\n"));

        var value = Composed(Render(plan, BodyComposerTests.Scope(("node.plan.value", JsonNode.Parse("{\"b\":1,\"a\":\"x\"}")))));
        var none = Composed(Render(plan, BodyComposerTests.Scope(("node.plan.value", null))));

        Assert.Equal("{\"a\":\"x\",\"b\":1}", value);
        Assert.Equal("null", none);
    }

    [Fact]
    public async Task ADocumentWithoutEscapes_ComposesAsBefore()
    {
        // macros.run.txt pins the deliverable's composed hash from before escaping existed (the four compiled goldens' runs
        // render on FakeRunHost and compose nothing); its deliverable lists no escapes, and composes the same text.
        var plan = RunFixture.Plan(RunGoldenTests.Macros());
        Assert.Equal("[]", CanonicalJson.Write(plan.Entries[Note].Json["escapes"]));
        var golden = File.ReadAllLines(Path.Combine(RunGoldenTests.GoldenFolder(), "macros.run.txt"));
        var pinned = JsonNode.Parse(golden.Single(l => l.StartsWith(Note + " | {", StringComparison.Ordinal))[(Note.Length + 3)..])!;

        var host = new ComposingRunHost(plan, (call, prompt) =>
            call.EntryKey == "node.plan" ? PlacementDecisionsTests.Sections
            : prompt.TextReply ? ComposingRunHost.EchoMarkers(call, prompt)
            : ComposingRunHost.Judgement(true));
        var recorder = new ListRecorder(() => host.Calls.Count);
        var inputs = RunFixture.GoldenInputs() with { RunFacts = new JsonObject { ["date"] = "2026-11-21" } };
        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(inputs, FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        var draft = (string?)result.Values["node.draft"]!["content"];
        Assert.Equal(draft + "\n", (string?)result.Values[Note]!["content"]);
        Assert.Equal((string?)pinned["inputHash"], (string?)recorder.Row(Note).Detail["inputHash"]);
    }
}
