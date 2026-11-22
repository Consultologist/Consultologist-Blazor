using System.Text;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Run;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using Consultologist.PackageFormat.V31.Templates;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Templates;

/// <summary>
/// #1506 (specVersion 31, area 09; part 08 §§ 9, 10; part 06 § 9; R10, D5, D6; Review Focus 5): a deliverable's sites and
/// substitutions. Each output tag of its template is a site, numbered in source order with its line and column, its
/// expression as written and the <c>for</c> around it. Each evaluation of a site is a substitution: where its text sits in
/// the composed text (in runes), the hash of that text (after escaping), what it read, its pass, and the last renderer of
/// its chain. The values ride beside the row (<see cref="RenderOutcome.Produced"/>), never in it, and
/// <see cref="RecordReplay.Verify"/> checks the positions. Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class SubstitutionTests
{
    private const string Note = "deliverable.note";

    /// <summary>
    /// <see cref="SourceFixture.Minimal"/> (the plain-text agent <c>plan</c>) with the plain-text input <c>name</c>, the list
    /// input <c>lines</c> (nullable strings), the plain-text agent <c>summary</c>, and the deliverable <c>note</c> of
    /// <paramref name="format"/> and <paramref name="body"/>.
    /// </summary>
    private static Dictionary<string, byte[]> WithNote(string body, string format = "plaintext") =>
        Minimal()
            .WithJson("manifest.json", m =>
            {
                m["inputs"]!.AsObject()["name"] = JsonNode.Parse(
                    "{ \"label\": \"Name\", \"description\": null, \"format\": \"plaintext\", \"required\": true, \"channel\": null }");
                m["inputs"]!.AsObject()["lines"] = JsonNode.Parse(
                    "{ \"label\": \"Lines\", \"description\": null, "
                    + "\"format\": { \"type\": \"array\", \"items\": { \"type\": [\"string\", \"null\"] } }, \"required\": true, \"channel\": null }");
            })
            .WithAgent("summary", "Summarise the plan for Margaret Okafor.\n\n{{ node.plan.content }}\n")
            .With("deliverables/note.md", Md(Deliverable("Consultation note", $"\"{format}\"", "null", "hold"), body));

    private static Composition Compose(RunPlan plan, JsonObject scope, IReadOnlyDictionary<string, bool>? placed = null) =>
        BodyComposerTests.Compose(plan, Note, scope, placed);

    private static RenderOutcome Render(RunPlan plan, JsonObject scope, IReadOnlyDictionary<string, bool>? placed = null) =>
        RenderTurn.Render(plan, new RenderCall(Note, Note, null, scope, 100_000, placed));

    /// <summary>The <paramref name="size"/> runes of <paramref name="text"/> from rune <paramref name="start"/>.</summary>
    private static string Slice(string text, int start, int size)
    {
        var runes = text.EnumerateRunes().ToList();
        Assert.True(start + size <= runes.Count, $"runes {start}+{size} are past the text's {runes.Count}");
        var slice = new StringBuilder();
        foreach (var rune in runes.Skip(start).Take(size))
            slice.Append(rune.ToString());
        return slice.ToString();
    }

    /// <summary>Every substitution's slice of the composed text is its value, and hashes to its hash.</summary>
    private static void AssertPlaced(Composition composition)
    {
        var text = BodyComposerTests.Composed(composition);
        foreach (var substitution in composition.Substitutions)
        {
            var slice = Slice(text, substitution.Start, substitution.Size);
            Assert.Equal(substitution.Value, slice);
            Assert.Equal(CanonicalJson.HashText(slice), substitution.Hash);
        }
    }

    /// <summary>The row's substitutions of a render, verified against its text.</summary>
    private static JsonArray Verified(RenderOutcome outcome)
    {
        Assert.True(outcome.Failure == null, outcome.Failure);
        var substitutions = outcome.Detail!["substitutions"]!.AsArray();
        Assert.True(RecordReplay.Verify(outcome.Text!, substitutions, out var fault), fault);
        Assert.Null(fault);
        return substitutions;
    }

    // ---- sites --------------------------------------------------------------------------------

    [Fact]
    public void EachOutputTag_IsASite()
    {
        var plan = RunFixture.Plan(WithNote(
            "Dear {{ input.name.value }}\n{{ for s in input.lines.value }}- {{ s }}\n{{ end }}{{ node.plan.content }}\n"));

        var composition = Compose(plan, BodyComposerTests.Scope(
            ("input.name.value", "Margaret Okafor"), ("input.lines.value", new JsonArray("Rest")), ("node.plan.content", "Fluids.")));

        BodyComposerTests.Composed(composition);
        Assert.Equal(
            [
                new ComposedSite(1, "1:6", "input.name.value", null),
                new ComposedSite(2, "2:35", "s", "s in input.lines.value"),
                new ComposedSite(3, "3:10", "node.plan.content", null),
            ],
            composition.Sites);

        // the row's sites are the same, in part 08 § 10's names
        var detail = Render(plan, BodyComposerTests.Scope(
            ("input.name.value", "Margaret Okafor"), ("input.lines.value", new JsonArray("Rest")), ("node.plan.content", "Fluids."))).Detail!;
        Assert.Equal(
            "[{\"at\":\"1:6\",\"expr\":\"input.name.value\",\"loop\":null,\"site\":1},"
            + "{\"at\":\"2:35\",\"expr\":\"s\",\"loop\":\"s in input.lines.value\",\"site\":2},"
            + "{\"at\":\"3:10\",\"expr\":\"node.plan.content\",\"loop\":null,\"site\":3}]",
            CanonicalJson.Write(detail["sites"]));
    }

    // ---- where a substitution's text sits ----------------------------------------------------

    [Fact]
    public void ASubstitution_SaysWhereItsTextSits()
    {
        var plan = RunFixture.Plan(WithNote("Dear {{ input.name.value }} 🙂 {{ node.plan.content }}"));
        var scope = BodyComposerTests.Scope(("input.name.value", "Margaret Okafor"), ("node.plan.content", "Rest 🙂 and fluids."));

        var composition = Compose(plan, scope);

        var text = BodyComposerTests.Composed(composition);
        Assert.Equal("Dear Margaret Okafor 🙂 Rest 🙂 and fluids.", text);
        Assert.Equal(2, composition.Substitutions.Count);
        var (name, content) = (composition.Substitutions[0], composition.Substitutions[1]);
        Assert.Equal(new Substitution(1, null, "input.name.value", CanonicalJson.HashText("Margaret Okafor"), 5, 15, null, "Margaret Okafor"), name);
        // " 🙂 " is three runes (four UTF-16 units)
        Assert.Equal(new Substitution(2, null, "node.plan.content", CanonicalJson.HashText("Rest 🙂 and fluids."), 23, 18, null, "Rest 🙂 and fluids."), content);
        AssertPlaced(composition);

        // the row holds no value; the values ride beside it, aligned by index
        var outcome = Render(plan, scope);
        var rows = Verified(outcome);
        Assert.Equal(
            "[{\"hash\":\"" + name.Hash + "\",\"pass\":null,\"reads\":\"input.name.value\",\"renders\":null,\"site\":1,\"size\":15,\"start\":5},"
            + "{\"hash\":\"" + content.Hash + "\",\"pass\":null,\"reads\":\"node.plan.content\",\"renders\":null,\"site\":2,\"size\":18,\"start\":23}]",
            CanonicalJson.Write(rows));
        Assert.DoesNotContain("Margaret", CanonicalJson.Write(outcome.Detail), StringComparison.Ordinal);
        Assert.Equal("[\"Margaret Okafor\",\"Rest 🙂 and fluids.\"]", CanonicalJson.Write(outcome.Produced!["substitutions"]));
    }

    [Fact]
    public void ALoopOverNothing_HasNoSubstitutions()
    {
        var plan = RunFixture.Plan(WithNote("Plan:\n{{ for s in input.lines.value }}- {{ s }}\n{{ end }}"));

        var outcome = Render(plan, BodyComposerTests.Scope(("input.lines.value", new JsonArray())));

        Assert.Equal("Plan:\n", outcome.Text);
        Assert.Empty(Verified(outcome));
        Assert.Single(outcome.Detail!["sites"]!.AsArray());
        Assert.Equal("[]", CanonicalJson.Write(outcome.Produced!["substitutions"]));
    }

    [Fact]
    public void ANullRead_WritesNothing()
    {
        var plan = RunFixture.Plan(WithNote("Plan:\n{{ for s in input.lines.value }}<{{ s }}>\n{{ end }}"));

        var composition = Compose(plan, BodyComposerTests.Scope(("input.lines.value", new JsonArray("Rest", null, "Fluids"))));

        Assert.Equal("Plan:\n<Rest>\n<>\n<Fluids>\n", BodyComposerTests.Composed(composition));
        var empty = composition.Substitutions[1];
        Assert.Equal(0, empty.Size);
        Assert.Equal(14, empty.Start); // "Plan:\n<Rest>\n<" before it
        Assert.Equal(CanonicalJson.HashText(""), empty.Hash);
        Assert.Equal("", empty.Value);
        AssertPlaced(composition);
        Verified(Render(plan, BodyComposerTests.Scope(("input.lines.value", new JsonArray("Rest", null, "Fluids")))));
    }

    [Fact]
    public void TwoTagsSideBySide_DoNotOverlap()
    {
        var plan = RunFixture.Plan(WithNote("{{ input.name.value }}{{ node.plan.content }}"));
        var scope = BodyComposerTests.Scope(("input.name.value", "Margaret Okafor"), ("node.plan.content", "🙂"));

        var composition = Compose(plan, scope);

        var (first, second) = (composition.Substitutions[0], composition.Substitutions[1]);
        Assert.Equal((0, 15), (first.Start, first.Size));
        Assert.Equal((15, 1), (second.Start, second.Size));
        AssertPlaced(composition);
        Verified(Render(plan, scope));
    }

    [Fact]
    public void AnEscapedRead_IsRecordedAfterEscaping()
    {
        // Part 06 § 9: the plain-text summary is escaped where it is written into the Markdown note, and recorded so
        var plan = RunFixture.Plan(WithNote("## Plan\n{{ node.summary.content }}\n", "markdown_block"));
        var scope = BodyComposerTests.Scope(("node.summary.content", "see *you* soon"));

        var composition = Compose(plan, scope);

        Assert.Equal("## Plan\nsee \\*you\\* soon\n", BodyComposerTests.Composed(composition));
        var substitution = Assert.Single(composition.Substitutions);
        Assert.Equal("see \\*you\\* soon", substitution.Value);
        Assert.Equal(CanonicalJson.HashText("see \\*you\\* soon"), substitution.Hash);
        Assert.Equal((8, 16), (substitution.Start, substitution.Size));
        Verified(Render(plan, scope));
    }

    [Fact]
    public void ACombiningMark_CountsAsItsRunes()
    {
        // Review Focus 5: "Zoë" written with a combining diaeresis is four runes; the emoji after it one rune
        const string zoe = "Zoë";
        var plan = RunFixture.Plan(WithNote("Dear {{ input.name.value }} 🙂 {{ node.plan.content }}"));
        var scope = BodyComposerTests.Scope(("input.name.value", zoe), ("node.plan.content", "Rest."));

        var composition = Compose(plan, scope);

        Assert.Equal((5, 4), (composition.Substitutions[0].Start, composition.Substitutions[0].Size));
        Assert.Equal((12, 5), (composition.Substitutions[1].Start, composition.Substitutions[1].Size));
        AssertPlaced(composition);
        Verified(Render(plan, scope));
    }

    // ---- passes ---------------------------------------------------------------------------------

    [Fact]
    public void ALoopedSite_HasOnePassPerIteration()
    {
        var plan = RunFixture.Plan(WithNote("{{ for s in input.lines.value }}- {{ s }}\n{{ end }}"));

        var composition = Compose(plan, BodyComposerTests.Scope(("input.lines.value", new JsonArray("Rest", "Fluids"))));

        Assert.Equal([[1], [2]], composition.Substitutions.Select(s => s.Pass!.ToArray()));
        Assert.All(composition.Substitutions, s => Assert.Equal(1, s.Site));
        AssertPlaced(composition);

        // nested loops give one number per loop, outermost first
        var nested = RunFixture.Plan(WithNote(
            "{{ for s in input.lines.value }}{{ for t in input.lines.value }}{{ s }}{{ t }};{{ end }}{{ end }}"));
        var twice = Compose(nested, BodyComposerTests.Scope(("input.lines.value", new JsonArray("a", "b"))));
        Assert.Equal("aa;ab;ba;bb;", BodyComposerTests.Composed(twice));
        Assert.Equal(
            [[1, 1], [1, 1], [1, 2], [1, 2], [2, 1], [2, 1], [2, 2], [2, 2]],
            twice.Substitutions.Select(s => s.Pass!.ToArray()));
        Assert.Equal([1, 2, 1, 2, 1, 2, 1, 2], twice.Substitutions.Select(s => s.Site));
        Assert.Equal("t in input.lines.value", twice.Sites[1].Loop);
        AssertPlaced(twice);
        var outcome = Render(nested, BodyComposerTests.Scope(("input.lines.value", new JsonArray("a", "b"))));
        Assert.Equal("[2,1]", CanonicalJson.Write(Verified(outcome)[4]!["pass"]));
    }

    // ---- what it read and rendered ------------------------------------------------------------

    [Fact]
    public void ARenderedSubstitution_NamesItsLastRender()
    {
        var plan = RunFixture.Plan(WithNote("## Plan\n{{ node.summary.content | renderer.bullets | renderer.quoted }}", "markdown_block")
            .WithRenderer("bullets", "\"plaintext\"", "- {{ value }}")
            .WithRenderer("quoted", "\"markdown_block\"", "> {{ value }}"));
        var scope = BodyComposerTests.Scope(("node.summary.content", "Rest and fluids."));

        var composition = Compose(plan, scope);

        var substitution = Assert.Single(composition.Substitutions);
        Assert.Equal("render:2", substitution.Renders);
        Assert.Equal("node.summary.content", substitution.Reads);
        AssertPlaced(composition);
        var outcome = Render(plan, scope);
        Assert.Equal("deliverable.note/render:2", (string?)Verified(outcome)[0]!["renders"]);
        Assert.Equal("deliverable.note/render:2", (string?)outcome.Detail!["renders"]![1]!["id"]);
    }

    [Fact]
    public void AProfileRead_IsASubstitution()
    {
        const string signature = "Dr A. Sample, Sample Oncology Clinic";
        var plan = RunFixture.Plan(WithNote("Signed: {{ profile.signature }}\n"));
        var scope = BodyComposerTests.Scope(("profile.signature", signature));

        var outcome = Render(plan, scope);

        var substitution = Assert.Single(Verified(outcome))!;
        Assert.Equal("profile.signature", (string?)substitution["reads"]);
        Assert.Equal(CanonicalJson.HashText(signature), (string?)substitution["hash"]);
        Assert.Equal(
            "[{\"field\":\"signature\",\"hash\":\"" + CanonicalJson.HashText(signature) + "\"}]",
            CanonicalJson.Write(outcome.Detail!["profileReads"]));
        Assert.DoesNotContain("Sample Oncology", CanonicalJson.Write(outcome.Detail), StringComparison.Ordinal);
        Assert.Equal(signature, (string?)outcome.Produced!["substitutions"]![0]);
    }

    [Fact]
    public void ARunSpot_IsASubstitution()
    {
        var plan = RunFixture.Plan(WithNote("Dear {{ input.name.value }}, dated {{ run.date }}.\n"));
        var scope = BodyComposerTests.Scope(("input.name.value", "Zoë 🙂"), ("run.date", "2026-11-21"));

        var outcome = Render(plan, scope);

        var substitutions = Verified(outcome);
        var spot = Assert.Single(outcome.Detail!["runSpots"]!.AsArray())!;
        Assert.Equal("run.date", (string?)substitutions[1]!["reads"]);
        Assert.Equal((int)spot["start"]!, (int)substitutions[1]!["start"]!);
        Assert.Equal((int)spot["length"]!, (int)substitutions[1]!["size"]!);
        Assert.Equal((string?)spot["outputHash"], (string?)substitutions[1]!["hash"]);
    }

    [Fact]
    public void APlacedSnippetsTags_TakeThePlacingTagsSite()
    {
        // The snippet's tag is a substitution at the site of the tag that places it; a plain-text snippet in a Markdown note
        // is escaped whole, and its read recorded after escaping.
        var plan = RunFixture.Plan(WithNote("## Plan\n{{ node.plan.content }}\n{{~ snippet.sign_off.content }}\n", "markdown_block")
            .WithSnippet("sign_off", "Dr A. Sample, for {{ input.name.value }}"));
        var scope = BodyComposerTests.Scope(("node.plan.content", "Rest."), ("input.name.value", "Margaret *Okafor*"));

        var composition = Compose(plan, scope, BodyComposerTests.Placed("snippet.sign_off"));

        Assert.Equal(2, composition.Sites.Count);
        Assert.Equal([1, 2], composition.Substitutions.Select(s => s.Site));
        Assert.Equal("input.name.value", composition.Substitutions[1].Reads);
        Assert.Equal("Margaret \\*Okafor\\*", composition.Substitutions[1].Value);
        AssertPlaced(composition);
        Verified(Render(plan, scope, BodyComposerTests.Placed("snippet.sign_off")));
    }

    [Fact]
    public void AnObjectDeliverable_HasOneSubstitution_ItsWholeText()
    {
        var plan = RunFixture.Plan(TemplateFixture.Package()
            .With("deliverables/note.md", Md(Deliverable("Consultation note", "\"section_plan\"", "null", "hold"), "{{ node.plan.value }}\n")));
        var value = JsonNode.Parse("{\"sections\":[{\"kind\":\"narrative\",\"title\":\"History 🙂\"}]}");

        var outcome = Render(plan, BodyComposerTests.Scope(("node.plan.value", value)));

        var substitution = Assert.Single(Verified(outcome))!;
        Assert.Equal(0, (int)substitution["start"]!);
        Assert.Equal(outcome.Text!.EnumerateRunes().Count(), (int)substitution["size"]!);
        Assert.Equal("node.plan.value", (string?)substitution["reads"]);
        Assert.Equal(CanonicalJson.HashText(outcome.Text), (string?)substitution["hash"]);
        Assert.Equal("[{\"at\":\"1:1\",\"expr\":\"node.plan.value\",\"loop\":null,\"site\":1}]", CanonicalJson.Write(outcome.Detail!["sites"]));
    }

    [Fact]
    public void ARenderedNode_HasNoSitesOrSubstitutions()
    {
        var plan = RunFixture.Plan(TemplateFixture.Package().WithRendered("recap", "Recap: {{ input.consult_draft.content }}\n", "\"markdown_block\""));

        var outcome = RenderTurn.Render(
            plan, new RenderCall("node.recap", "node.recap", null, BodyComposerTests.Scope(("input.consult_draft.content", "Rest.")), 100_000));

        Assert.True(outcome.Failure == null, outcome.Failure);
        Assert.False(outcome.Detail!.ContainsKey("sites"));
        Assert.False(outcome.Detail.ContainsKey("substitutions"));
        Assert.False(outcome.Produced!.ContainsKey("substitutions"));
    }

    // ---- replay -------------------------------------------------------------------------------

    [Fact]
    public void Verify_RefusesAMovedSubstitution()
    {
        var plan = RunFixture.Plan(WithNote("{{ for s in input.lines.value }}Dear {{ s }} {{ node.plan.content }}\n{{ end }}"));
        var outcome = Render(plan, BodyComposerTests.Scope(("input.lines.value", new JsonArray("Margaret Okafor")), ("node.plan.content", "Rest.")));
        var substitutions = Verified(outcome);

        var moved = substitutions.DeepClone().AsArray();
        moved[1]!["start"] = (int)moved[1]!["start"]! + 1;

        Assert.False(RecordReplay.Verify(outcome.Text!, moved, out var fault));
        Assert.StartsWith("site 2 pass 1", fault, StringComparison.Ordinal);

        // two substitutions that overlap are refused even when each hashes
        var overlapping = new JsonArray(
            new JsonObject { ["site"] = 1, ["pass"] = null, ["start"] = 0, ["size"] = 4, ["hash"] = CanonicalJson.HashText("Dear") },
            new JsonObject { ["site"] = 2, ["pass"] = null, ["start"] = 2, ["size"] = 2, ["hash"] = CanonicalJson.HashText("ar") });
        Assert.False(RecordReplay.Verify("Dear", overlapping, out var overlap));
        Assert.StartsWith("site 2", overlap, StringComparison.Ordinal);

        // past the text's end
        var past = new JsonArray(new JsonObject { ["site"] = 1, ["pass"] = null, ["start"] = 3, ["size"] = 2, ["hash"] = CanonicalJson.HashText("r") });
        Assert.False(RecordReplay.Verify("Dear", past, out _));
    }
}
