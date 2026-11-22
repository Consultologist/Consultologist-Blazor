using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat;
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

    private const string NoteFile = "deliverables/note.md";

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
    public void APlacedPlainTextSnippet_IsRecordedAsSpliced()
    {
        // Part 06 § 9: escaped reads are recorded after escaping, so a unit escaped whole is recorded by the hash of the
        // text spliced; a Markdown unit, written as it is, by its own.
        var plan = RunFixture.Plan(WithNote("markdown_block", "## Plan\n{{~ snippet.closing.content }}\n{{~ snippet.sign_off.content }}\n")
            .WithSnippet("closing", "**Next steps:** {{ node.summary.content }}", "markdown_block")
            .WithSnippet("sign_off", "Dr A. Sample, {{ node.summary.content }}"));

        var outcome = Render(
            plan, BodyComposerTests.Scope(("node.summary.content", "see *you* soon")), BodyComposerTests.Placed("snippet.closing", "snippet.sign_off"));

        Composed(outcome);
        Assert.Equal(
            CanonicalJson.Write(new JsonArray(
                new JsonObject { ["site"] = "snippet.closing", ["hash"] = CanonicalJson.HashText("**Next steps:** see \\*you\\* soon") },
                new JsonObject { ["site"] = "snippet.sign_off", ["hash"] = CanonicalJson.HashText("Dr A\\. Sample\\, see \\*you\\* soon") })),
            CanonicalJson.Write(outcome.Detail!["units"]));
    }

    [Fact]
    public void ANestedUnit_IsRecordedAsSpliced_ALineStartOnlyWhereItStartsALine()
    {
        // R9 (Task 15's review): a unit placed inside a plain-text snippet escaped whole is recorded by the hash of its text
        // as it stands in the escaped snippet. Placed mid-line, its leading space continues the line and stays; placed at a
        // line start, its leading space is the entity.
        var plan = RunFixture.Plan(WithNote("markdown_block", "## Plan\n{{~ snippet.sign_off.content }}\n")
            .WithSnippet("sign_off", "Seen by{{ snippet.doctor.content }}\n{{ snippet.clinic.content }}")
            .WithSnippet("doctor", " Dr A. Sample")
            .WithSnippet("clinic", " Sample Oncology Clinic"));

        var outcome = Render(plan, BodyComposerTests.Scope(), BodyComposerTests.Placed("snippet.sign_off", "snippet.doctor", "snippet.clinic"));

        Assert.Equal("## Plan\nSeen by Dr A\\. Sample\n&#32;Sample Oncology Clinic\n", Composed(outcome));
        Assert.Equal(
            CanonicalJson.Write(new JsonArray(
                new JsonObject { ["site"] = "snippet.sign_off", ["hash"] = CanonicalJson.HashText("Seen by Dr A\\. Sample\n&#32;Sample Oncology Clinic") },
                new JsonObject { ["site"] = "snippet.doctor", ["hash"] = CanonicalJson.HashText(" Dr A\\. Sample") },
                new JsonObject { ["site"] = "snippet.clinic", ["hash"] = CanonicalJson.HashText("&#32;Sample Oncology Clinic") })),
            CanonicalJson.Write(outcome.Detail!["units"]));
    }

    [Fact]
    public void APlainRead_InAPlacedInlineSnippet_IsEscaped()
    {
        var plan = RunFixture.Plan(WithNote("markdown_block", "Next: {{ snippet.next.content }}.\n")
            .WithSnippet("next", "_see_ {{ node.summary.content }}", "markdown_inline"));
        Assert.Equal("[{\"at\":\"1:7\",\"unit\":\"snippet.next\"}]", CanonicalJson.Write(plan.Entries[Note].Json["escapes"]));

        var text = Composed(Render(plan, BodyComposerTests.Scope(("node.summary.content", "Dr A. Sample")), BodyComposerTests.Placed("snippet.next")));

        Assert.Equal("Next: _see_ Dr A\\. Sample.\n", text);
    }

    [Fact]
    public void NumbersAndBooleans_AreEscapedAsWritten()
    {
        // D13 writes the value, then D7 escapes it: a minus sign and a decimal point are ASCII punctuation; true is not.
        var plan = RunFixture.Plan(WithNote("markdown_block", "{{ node.count.value }} / {{ node.rate.value }} / {{ node.urgent.value }}\n")
            .WithComputed("count", "\"number\"", "{ \"-\": [0, 3] }")
            .WithComputed("rate", "\"number\"", "{ \"/\": [3, 2] }")
            .WithComputed("urgent", "\"boolean\"", "{ \"===\": [1, 1] }"));

        var text = Composed(Render(plan, BodyComposerTests.Scope(
            ("node.count.value", JsonValue.Create(-3)), ("node.rate.value", JsonNode.Parse("1.5")), ("node.urgent.value", true))));

        Assert.Equal("\\-3 / 1\\.5 / true\n", text);
    }

    [Fact]
    public void Escaping_PastTheOutputLimit_IsS05014()
    {
        // The plain text fits the limit; escaped, each '.' is two characters, and the text passes it.
        var plan = RunFixture.Plan(WithNote("markdown_block", "{{ node.summary.content }}"));
        var plain = new string('.', (BodyComposer.OutputLimit / 2) + 1);
        Assert.True(plain.Length <= BodyComposer.OutputLimit);

        var outcome = Render(plan, BodyComposerTests.Scope(("node.summary.content", plain)));

        Assert.Null(outcome.Text);
        Assert.Equal(RunSentences.OutputPassedLimit("Deliverable 'note'"), outcome.Failure);
        V31Contract.AssertSentence(new SourceRefusal("S05-014", outcome.Failure!, ""));
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
    public void ADeliverable_EscapesLeadingIndentation()
    {
        // #1505 comment 4, ruling 1 (R9): a plain line led by four spaces or a tab would be a code block; its first space
        // or tab is written as its entity.
        var plan = RunFixture.Plan(WithNote("markdown_block", "{{ node.summary.content }}\n"));

        var text = Composed(Render(plan, BodyComposerTests.Scope(("node.summary.content", "    indented\n\tx"))));

        Assert.Equal("&#32;   indented\n&#9;x\n", text);
    }

    [Fact]
    public void APlacedPlainTextSnippet_WithARunSpot_EncodesOnlyTheLinesLeadingSpace()
    {
        // The snippet is escaped whole, piece by piece around its run spot, and the spot's run-neutral text around its
        // marker: a piece that continues a line (" by", "\t(clinic)") is not a line start, so the sent and run-neutral texts
        // still differ only at the marker; the snippet's own indented second line is a line start.
        var plan = RunFixture.Plan(WithNote("markdown_block", "## Plan\n{{~ snippet.sign_off.content }}\n")
            .WithRendered("stamp", "Seen {{ run.date }}\t(clinic).").RunDependent("nodes/stamp.md")
            .WithSnippet("sign_off", "{{ node.stamp.content }} by Dr A. Sample\n    Sample Oncology Clinic"));
        var scope = BodyComposerTests.Scope(("node.stamp.content", "Seen 2026-11-21\t(clinic)."), ("node.stamp.neutral", "Seen {{run:1}}\t(clinic)."));

        var outcome = Render(plan, scope, BodyComposerTests.Placed("snippet.sign_off"));

        var text = Composed(outcome);
        Assert.Equal("## Plan\nSeen 2026\\-11\\-21\t\\(clinic\\)\\. by Dr A\\. Sample\n&#32;   Sample Oncology Clinic\n", text);
        Assert.Equal("## Plan\nSeen {{run:1}}\t\\(clinic\\)\\. by Dr A\\. Sample\n&#32;   Sample Oncology Clinic\n", outcome.NeutralText);
        Assert.Equal(text, outcome.NeutralText!.Replace("{{run:1}}", "2026\\-11\\-21", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Seen by", " Dr A. Sample", " today.", "Seen by", " Dr A\\. Sample", " today\\.")]
    [InlineData("Plan:\n", " FBC", "", "Plan\\:\n", "&#32;FBC", "")]
    [InlineData("Plan:\n", "\tFBC\n", " review", "Plan\\:\n", "&#9;FBC\n", "&#32;review")]
    [InlineData("", "  Margaret Okafor", "\tseen", "", "&#32; Margaret Okafor", "\tseen")]
    public void APlacedTextEscapedPieceByPiece_IsTheEscapeOfTheWholeText(
        string before, string output, string after, string escapedBefore, string escapedOutput, string escapedAfter)
    {
        // R9 against escaping by pieces (Task 7's finding): a spot's output, and the text after it, starts a line only where
        // it starts a line of the whole placed text, so the pieces escaped are the whole text escaped.
        var spot = new BodyComposer.FilledSpot(new BodyComposer.PlannedSpot(1, "node.stamp.content", false), before.Length, output.Length, output, "{{run:1}}", "hash");
        var placed = new BodyComposer.PlacedText(before + output + after, [spot]);

        var escaped = BodyComposer.Escaped(placed);

        Assert.Equal(escapedBefore + escapedOutput + escapedAfter, escaped.Text);
        Assert.Equal(MarkdownEscape.Escape(placed.Text), escaped.Text);
        var moved = Assert.Single(escaped.Spots);
        Assert.Equal(escapedOutput, moved.Output);
        Assert.Equal(escapedOutput, escaped.Text.Substring(moved.Start, moved.Length));
        Assert.Equal("{{run:1}}", moved.Neutral);
    }

    [Theory]
    [InlineData("Seen {{run:1}} by Dr A. Sample", true, "Seen {{run:1}} by Dr A\\. Sample")]
    [InlineData(" {{run:1}} (clinic)\n seen", true, "&#32;{{run:1}} \\(clinic\\)\n&#32;seen")]
    [InlineData(" {{run:1}}\t(clinic)", false, " {{run:1}}\t\\(clinic\\)")]
    [InlineData("{{run:1}}\n\t{{run:2}} x", true, "{{run:1}}\n&#9;{{run:2}} x")]
    public void ARunNeutralText_IsEscapedLineByLine_AroundItsMarkers(string neutral, bool startsLine, string expected)
    {
        // The piece after a marker continues the marker's line: its leading space or tab stays, as in the sent text.
        Assert.Equal(expected, BodyComposer.EscapedNeutral(neutral, startsLine));
    }

    [Fact]
    public void ATextSpot_InAPlacedPlainTextSnippet_IsALineStartOnlyWhereItStartsALine()
    {
        // Mid-line, the spot's output (and its run-neutral text) keeps its leading space; at a line start, its tab is the entity.
        var plan = RunFixture.Plan(WithNote("markdown_block", "{{~ snippet.sign_off.content }}\n")
            .WithRendered("stamp", "Seen {{ run.date }}.").RunDependent("nodes/stamp.md")
            .WithRendered("visit", "Seen {{ run.date }}.").RunDependent("nodes/visit.md")
            .WithSnippet("sign_off", "Margaret Okafor{{ node.stamp.content }}\n{{ node.visit.content }} by Dr A. Sample"));
        var scope = BodyComposerTests.Scope(
            ("node.stamp.content", " seen 2026-11-21."), ("node.stamp.neutral", " seen {{run:1}}."),
            ("node.visit.content", "\tSeen 2026-11-21."), ("node.visit.neutral", "\tSeen {{run:1}}."));

        var outcome = Render(plan, scope, BodyComposerTests.Placed("snippet.sign_off"));

        var text = Composed(outcome);
        Assert.Equal("Margaret Okafor seen 2026\\-11\\-21\\.\n&#9;Seen 2026\\-11\\-21\\. by Dr A\\. Sample\n", text);
        Assert.Equal("Margaret Okafor seen {{run:1}}\\.\n&#9;Seen {{run:1}}\\. by Dr A\\. Sample\n", outcome.NeutralText);
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
    public void AnObjectDeliverable_PastTheOutputLimit_IsS05014()
    {
        var plan = RunFixture.Plan(WithNote("section_plan", "{{ node.plan.value }}\n"));
        var value = new JsonObject { ["sections"] = new JsonArray(), ["note"] = new string('x', BodyComposer.OutputLimit) };

        var outcome = Render(plan, BodyComposerTests.Scope(("node.plan.value", value)));

        Assert.Null(outcome.Text);
        Assert.Equal(RunSentences.OutputPassedLimit("Deliverable 'note'"), outcome.Failure);
        V31Contract.AssertSentence(new SourceRefusal("S05-014", outcome.Failure!, ""));
    }

    [Fact]
    public void AnObjectDeliverable_WhoseValueIsAbsent_Fails()
    {
        // A scope that lacks the read is what strict variables fail in any body: A06-008, never a throw.
        var plan = RunFixture.Plan(WithNote("section_plan", "{{ node.plan.value }}\n"));

        var outcome = Render(plan, new JsonObject());

        Assert.Null(outcome.Text);
        Assert.Equal("Deliverable 'note' couldn't be rendered.", outcome.Failure);
        V31Contract.AssertSentence(new SourceRefusal("A06-008", outcome.Failure!, ""));
    }

    [Theory]
    [InlineData("{{ node.plan.value }}", true)]
    [InlineData("{{ node.plan.value }}\r\n", true)]
    [InlineData("{{~ node.plan.value ~}}\n", true)]
    [InlineData("Plan: {{ node.plan.value }}\n", false)]
    [InlineData("{{ node.plan.value }}\nThe plan.\n", false)]
    [InlineData(" {{ node.plan.value }}\n", false)]
    [InlineData("{{ node.plan.value }}\n\n", false)]
    [InlineData("{{ node.plan.value }}{{ node.plan.value }}\n", false)]
    [InlineData("{{ node.plan.content }}\n", false)]
    [InlineData("{{ node.plan.value | renderer.plan_text }}\n", false)]
    [InlineData("{{ if true }}{{ node.plan.value }}{{ end }}", false)]
    public void AnObjectBody_IsHeldToPublishsForm_AtRunTime(string body, bool isForm)
    {
        // D8's form has one definition (SourceTemplates.OneValueRead): a document whose object deliverable's body is not of
        // it (forged: publish refuses it, S06-023) fails with S07-006; one that is composes its value.
        var (_, compiled) = CompiledPackage.Publish(WithNote("section_plan", "{{ node.plan.value }}\n"), ResolveFixture.Published(), CompiledGoldenTests.Fixed);
        var document = JsonNode.Parse(compiled!.Resolve)!;
        document["deliverables"]![Note]!["body"] = body;
        var (plan, _) = RunPlan.Build(document.ToJsonString(), compiled.ResolveHash, compiled.LockHash, null);
        Assert.Equal(isForm, SourceTemplates.OneValueRead(TemplateSyntax.Parse(body)) is not null);

        var outcome = Render(plan!, BodyComposerTests.Scope(("node.plan.value", JsonNode.Parse("{ \"sections\": [] }"))));

        if (isForm)
        {
            Assert.Equal("{\"sections\":[]}", Composed(outcome));
            return;
        }

        Assert.Null(outcome.Text);
        Assert.Equal(ResolveSentences.ResolveDoesNotMatch(plan!.Package, plan.Version).Sentence, outcome.Failure);
        V31Contract.AssertSentence(new SourceRefusal("S07-006", outcome.Failure!, ""));
    }

    [Theory]
    [InlineData("\"judgement\"", null)]
    [InlineData("\"concept_list\"", null)]
    [InlineData("\"classification\"", null)]
    [InlineData("\"plaintext\"", null)]
    [InlineData("\"markdown_block\"", null)]
    [InlineData("\"probe\"", "{ \"type\": \"object\", \"required\": [\"a\"], \"additionalProperties\": false, \"properties\": { \"a\": { \"$ref\": \"plaintext\" } } }")]
    [InlineData("\"probe\"", "{ \"type\": [\"object\", \"null\"], \"required\": [\"a\"], \"additionalProperties\": false, \"properties\": { \"a\": { \"$ref\": \"plaintext\" } } }")]
    [InlineData("\"probe\"", "{ \"type\": \"array\", \"items\": { \"$ref\": \"plaintext\" } }")]
    public void TheRun_ReadsAFormatsShape_AsPublishDoes(string format, string? probe)
    {
        // One reading of a format's shape: publish's (FormatTypes.Shape, through names) and the run's (the same reading of
        // the bundle's expansion) agree, so the run's object deliverable is publish's FormatShape.Object.
        var files = Package().WithAgent("probe", "Describe the plan for Margaret Okafor.\n\n{{ node.plan.content }}\n", format);
        if (probe != null)
            files.Listed("formats/index.json", "probe").With("formats/probe.json", Json("{ \"$schema\": \"urn:consultologist:spec:31:dialect\", " + probe[1..]));
        var registry = ResolveFixture.Published();
        var package = SourceParser.Parse(files, registry.Names).Package!;
        var plan = RunFixture.Plan(files);
        var publish = FormatTypes.Shape(JsonNode.Parse(format), FormatSet.ForPackage(registry, package.Definitions.Formats.Items));
        var run = plan.Entries["node.probe"].Format;
        Assert.NotEqual(FormatShape.Unknown, publish);

        Assert.Equal(publish, FormatTypes.ExpandedShape(plan.Formats.Expand(run)));
        Assert.Equal(publish == FormatShape.Object, plan.Formats.IsObject(run));
    }

    [Fact]
    public void TheRun_ReadsANestedFormatsShape_AsPublishDoes()
    {
        // Inside a format: a reference with a description beside it, a nullable reference and a union of two, each read
        // the same by publish (the source's names) and by the run (the bundle's expansion of the entry).
        const string probe = "{ \"$schema\": \"urn:consultologist:spec:31:dialect\", \"type\": \"object\", "
            + "\"required\": [\"described\", \"maybe\", \"either\", \"words\"], \"additionalProperties\": false, \"properties\": { "
            + "\"described\": { \"$ref\": \"section_plan\", \"description\": \"The plan.\" }, "
            + "\"maybe\": { \"anyOf\": [{ \"$ref\": \"verdict\" }, { \"type\": \"null\" }] }, "
            + "\"either\": { \"anyOf\": [{ \"$ref\": \"section_plan\" }, { \"$ref\": \"verdict\" }] }, "
            + "\"words\": { \"type\": [\"array\", \"null\"], \"items\": { \"$ref\": \"plaintext\" } } } }";
        var files = Package().WithAgent("probe", "Describe the plan for Margaret Okafor.\n\n{{ node.plan.content }}\n", "\"probe\"")
            .Listed("formats/index.json", "probe").With("formats/probe.json", Json(probe));
        var registry = ResolveFixture.Published();
        var package = SourceParser.Parse(files, registry.Names).Package!;
        var set = FormatSet.ForPackage(registry, package.Definitions.Formats.Items);
        var plan = RunFixture.Plan(files);
        var expanded = plan.Formats.Expand(plan.Entries["node.probe"].Format);
        var source = JsonNode.Parse(probe)!["properties"]!.AsObject();

        var shapes = source.Select(p => (p.Key, FormatTypes.Shape(p.Value, set))).ToList();

        Assert.Equal(
            [("described", FormatShape.Object), ("maybe", FormatShape.Object), ("either", FormatShape.Union), ("words", FormatShape.List)],
            shapes);
        Assert.Equal(shapes, source.Select(p => (p.Key, FormatTypes.ExpandedShape(expanded["properties"]![p.Key]))).ToList());
    }

    [Fact]
    public void AReferenceWithADescription_IsAnObjectFormat_AsPublishReadsIt()
    {
        // Publish's shape follows a reference with annotations beside it; the bundle's expansion wraps the entry it inlines
        // in an allOf beside them, and the run reads it as the same object format. (A package's format file is an object or
        // a list at its top, A03-001, so a bundle entry is never itself a bare reference.)
        var bundle = JsonNode.Parse(
            "{ \"$defs\": { "
            + "\"plan\": { \"$id\": \"urn:example:plan\", \"type\": \"object\", \"properties\": { \"a\": { \"type\": \"string\" } } }, "
            + "\"words\": { \"$id\": \"urn:example:words\", \"type\": \"array\", \"items\": { \"type\": \"string\" } } } }");
        var formats = new PlanFormats(bundle);

        var annotated = JsonNode.Parse("{ \"$ref\": \"#/$defs/plan\", \"description\": \"The plan, as data.\" }");
        Assert.IsType<JsonArray>(formats.Expand(annotated)["allOf"]);
        Assert.True(formats.IsObject(annotated));
        Assert.True(formats.IsObject(JsonNode.Parse("{ \"$ref\": \"#/$defs/plan\" }")));
        Assert.True(formats.IsObject(JsonNode.Parse(
            "{ \"anyOf\": [{ \"$ref\": \"#/$defs/plan\", \"title\": \"Plan\" }, { \"type\": \"null\" }] }")));
        Assert.False(formats.IsObject(JsonNode.Parse("{ \"$ref\": \"#/$defs/words\" }")));
        Assert.False(formats.IsObject(JsonNode.Parse("{ \"$ref\": \"#/$defs/absent\" }")));
    }

    [Theory]
    [InlineData("section_plan", "plan")]
    [InlineData("concept_list", "terms")]
    public void ANamedObjectFormat_IsAnObjectFormat_AtRunTime(string format, string node)
    {
        var plan = RunFixture.Plan(WithNote(format, $"{{{{ node.{node}.value }}}}\n")
            .WithAgent("terms", "List the clinical terms in the draft.\n\n{{ input.consult_draft.content }}\n", "\"concept_list\""));

        Assert.True(plan.Formats.IsObject(plan.Entries[Note].Format));
        Assert.False(plan.Formats.IsObject(plan.Entries["node.summary"].Format));
    }

    [Fact]
    public async Task ADocumentWithoutEscapes_ComposesAsBefore()
    {
        // The macros run's package with its deliverable made plaintext, which lists no escapes, composes exactly what the
        // markdown_block one composed before area 08: the deliverable's inputHash macros.run.txt pinned then. (The four
        // compiled goldens' runs render on FakeRunHost and compose nothing.)
        const string before = "sha256:203d6cc06b4759876c05d587d247d3d8bfbd4a3fd79be9050fb4bb25c6821bd9";
        var plan = RunFixture.Plan(RunGoldenTests.Macros()
            .With(NoteFile, Md(Deliverable("Consultation note", Plain, "null", "hold"), "{{ node.draft.content }}\n")));
        Assert.Equal("[]", CanonicalJson.Write(plan.Entries[Note].Json["escapes"]));

        var (result, recorder) = await MacrosRun(plan);

        var draft = (string?)result.Values["node.draft"]!["content"];
        Assert.Equal(draft + "\n", (string?)result.Values[Note]!["content"]);
        Assert.Equal(before, (string?)recorder.Row(Note).Detail["inputHash"]);
    }

    [Fact]
    public async Task TheMacrosDeliverable_IsItsDraftEscaped()
    {
        // macros.run.txt's markdown_block deliverable reads the plain-text draft, so it composes the draft escaped (its
        // ';' and '.' are ASCII punctuation), and its pinned inputHash is the hash of that escaped text.
        var plan = RunFixture.Plan(RunGoldenTests.Macros());
        Assert.Equal("[{\"at\":\"1:1\",\"unit\":null}]", CanonicalJson.Write(plan.Entries[Note].Json["escapes"]));
        var golden = File.ReadAllLines(Path.Combine(RunGoldenTests.GoldenFolder(), "macros.run.txt"));
        var pinned = JsonNode.Parse(golden.Single(l => l.StartsWith(Note + " | {", StringComparison.Ordinal))[(Note.Length + 3)..])!;

        var (result, recorder) = await MacrosRun(plan);

        var draft = (string?)result.Values["node.draft"]!["content"];
        var content = (string?)result.Values[Note]!["content"];
        Assert.Contains("Dr A\\. Sample\\.", content, StringComparison.Ordinal);
        Assert.Equal(MarkdownEscape.Escape(draft!) + "\n", content);
        Assert.Equal(CanonicalJson.HashText(content!), (string?)recorder.Row(Note).Detail["inputHash"]);
        Assert.Equal((string?)pinned["inputHash"], (string?)recorder.Row(Note).Detail["inputHash"]);
    }

    /// <summary>The macros run (<see cref="RunGoldenTests.Macros"/>'s hosts and inputs) of <paramref name="plan"/>, asserted completed.</summary>
    private static async Task<(RunResult Result, ListRecorder Recorder)> MacrosRun(RunPlan plan)
    {
        var host = new ComposingRunHost(plan, (call, prompt) =>
            call.EntryKey == "node.plan" ? PlacementDecisionsTests.Sections
            : prompt.TextReply ? ComposingRunHost.EchoMarkers(call, prompt)
            : ComposingRunHost.Judgement(true));
        var recorder = new ListRecorder(() => host.Calls.Count);
        var inputs = RunFixture.GoldenInputs() with { RunFacts = new JsonObject { ["date"] = "2026-11-21" } };
        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(inputs, FakeRunHost.Start);
        Assert.Equal(RunState.Completed, result.State);
        return (result, recorder);
    }
}
