using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using Consultologist.PackageFormat.V31.Templates;
using static Consultologist.Api.Tests.V31Run.BodyComposerTests;
using static Consultologist.Api.Tests.V31Templates.TemplateFixture;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 4; part 08 §§ 4, 8.2; part 03 § 4.4; D4, D5): run spots and the two hashes
/// at run time. One render of a body gives the sent text and the run-neutral text: each spot's output is recorded at the
/// position it was written (never found by searching), each <c>marker</c> spot stands as <c>{{run:&lt;n&gt;}}</c> in the
/// run-neutral text and each <c>text</c> spot as the same pipeline over the read node's run-neutral text. Every package is
/// published and loaded as the engine loads it (<see cref="RunFixture.Plan"/>); the scope is built by hand. Content is
/// fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class RunSpotsTests
{
    private const string Dated = "Dated {{ run.date | date.to_string \"%d %B %Y\" }}.";

    private static RunPlan Agent(string body, Func<Dictionary<string, byte[]>, Dictionary<string, byte[]>>? with = null) =>
        RunFixture.Plan((with ?? (f => f))(Package()).WithAgent("dated", body).RunDependent("nodes/dated.md"));

    /// <summary>The sent text rebuilt from <paramref name="composition"/>'s spots: each spot's output stands at its recorded place.</summary>
    private static void AssertEachSpotStandsWhereRecorded(Composition composition)
    {
        var text = composition.Text!;
        var at = 0;
        foreach (var spot in composition.Spots)
        {
            Assert.True(spot.Start >= at, "spots are in the order of the text");
            Assert.Equal(spot.OutputHash, CanonicalJson.HashText(text.Substring(spot.Start, spot.Length)));
            if (spot.Output != null)
                Assert.Equal(spot.Output, text.Substring(spot.Start, spot.Length));
            at = spot.Start + spot.Length;
        }
    }

    [Fact]
    public void ARunSpot_LeavesItsMarkerInTheNeutralText()
    {
        var plan = Agent(Dated);

        var composition = Compose(plan, "node.dated", Scope(("run.date", "2026-11-21")));

        Assert.Equal("Dated 21 November 2026.", Composed(composition));
        Assert.Equal("Dated {{run:1}}.", composition.NeutralText);
        var spot = Assert.Single(composition.Spots);
        Assert.Equal(1, spot.Site);
        Assert.Equal("run.date", spot.Reads);
        Assert.Equal(6, spot.Start);
        Assert.Equal("21 November 2026".Length, spot.Length);
        Assert.Equal("21 November 2026", spot.Output); // a raw run fact's output is kept
        Assert.Equal(CanonicalJson.HashText("21 November 2026"), spot.OutputHash);
        Assert.Equal(CanonicalJson.Hash(JsonValue.Create("2026-11-21")), spot.ValueHash);
        AssertEachSpotStandsWhereRecorded(composition);
    }

    [Fact]
    public void TheNeutralText_IsTheSameOnAnotherDate()
    {
        var plan = Agent(Dated);

        var first = Compose(plan, "node.dated", Scope(("run.date", "2026-11-21")));
        var second = Compose(plan, "node.dated", Scope(("run.date", "2027-01-04")));

        Assert.Equal("Dated 04 January 2027.", Composed(second));
        Assert.NotEqual(Composed(first), Composed(second));
        Assert.Equal(first.NeutralText, second.NeutralText);
        Assert.Equal(CanonicalJson.HashText(first.NeutralText!), CanonicalJson.HashText(second.NeutralText!));
        Assert.NotEqual(first.Spots[0].ValueHash, second.Spots[0].ValueHash);
    }

    [Fact]
    public void TwoSpots_AreNumberedInSourceOrder_ThroughAPlacedSnippet()
    {
        // D4: a placed snippet's spot is numbered where the snippet stands, between the body's two.
        var plan = Agent("Dated {{ run.date }}; {{ snippet.when.content }}; engine {{ run.engine }}.",
            f => f.WithSnippet("when", "run {{ run.id }}"));
        var scope = Scope(("run.date", "2026-11-21"), ("run.id", "job-0042"), ("run.engine", "31.0.0"));

        var composition = Compose(plan, "node.dated", scope, Placed("snippet.when"));

        Assert.Equal("Dated 2026-11-21; run job-0042; engine 31.0.0.", Composed(composition));
        Assert.Equal("Dated {{run:1}}; run {{run:2}}; engine {{run:3}}.", composition.NeutralText);
        Assert.Equal([1, 2, 3], composition.Spots.Select(s => s.Site));
        Assert.Equal(["run.date", "run.id", "run.engine"], composition.Spots.Select(s => s.Reads));
        Assert.Equal(["2026-11-21", "job-0042", "31.0.0"], composition.Spots.Select(s => s.Output));
        Assert.Equal("Dated 2026-11-21; run ".Length, composition.Spots[1].Start);
        AssertEachSpotStandsWhereRecorded(composition);

        // Not placed, the snippet's spot is not filled; the others keep their numbers.
        var unplaced = Compose(plan, "node.dated", scope);
        Assert.Equal("Dated 2026-11-21; ; engine 31.0.0.", Composed(unplaced));
        Assert.Equal("Dated {{run:1}}; ; engine {{run:3}}.", unplaced.NeutralText);
        Assert.Equal([1, 3], unplaced.Spots.Select(s => s.Site));
    }

    [Fact]
    public void ASpotInAFor_IsFilledOnEachIteration_WithTheSameSite()
    {
        var plan = Agent("{{ for s in node.plan.value.sections }}{{ s.title }} on {{ run.date }}\n{{ end }}");
        var sections = JsonNode.Parse("""[{ "title": "History", "guidance": "", "include": true }, { "title": "Plan", "guidance": "", "include": true }]""");

        var composition = Compose(plan, "node.dated", Scope(("node.plan.value", new JsonObject { ["sections"] = sections }), ("run.date", "2026-11-21")));

        Assert.Equal("History on 2026-11-21\nPlan on 2026-11-21\n", Composed(composition));
        Assert.Equal("History on {{run:1}}\nPlan on {{run:1}}\n", composition.NeutralText);
        Assert.Equal([1, 1], composition.Spots.Select(s => s.Site));
        Assert.Equal([11, 30], composition.Spots.Select(s => s.Start));
        AssertEachSpotStandsWhereRecorded(composition);
    }

    [Fact]
    public void ARunSpotMarkerInAValue_IsLeftAsItIs()
    {
        // Part 05 § 4: the text is never searched for {{run:<n>}}; an input holding those characters stays as it is.
        var plan = Agent("{{ input.consult_draft.content }} / {{ run.date }}");

        var composition = Compose(plan, "node.dated", Scope(("input.consult_draft.content", "{{run:1}}"), ("run.date", "2026-11-21")));

        Assert.Equal("{{run:1}} / 2026-11-21", Composed(composition));
        Assert.Equal("{{run:1}} / {{run:1}}", composition.NeutralText);
        var spot = Assert.Single(composition.Spots);
        Assert.Equal("{{run:1}} / ".Length, spot.Start);
        Assert.Equal(composition.Text![..spot.Start], composition.NeutralText![..spot.Start]);
    }

    [Fact]
    public void ASpotThroughARenderer_CoversThePipeline()
    {
        var plan = Agent("Dated {{ run.date | renderer.long_date }}; seen by {{ input.consult_draft.content | renderer.quote }}.",
            f => f.WithRenderer("long_date", "\"date\"", "{{ value | date.to_string \"%A %d %B %Y\" }}", "plaintext")
                .WithRenderer("quote", "\"markdown_block\"", "> {{ value }}"));

        var composition = Compose(plan, "node.dated", Scope(("run.date", "2026-11-21"), ("input.consult_draft.content", "Dr A. Sample")));

        Assert.Equal("Dated Saturday 21 November 2026; seen by > Dr A. Sample.", Composed(composition));
        Assert.Equal("Dated {{run:1}}; seen by > Dr A. Sample.", composition.NeutralText);
        var spot = Assert.Single(composition.Spots);
        Assert.Equal("Saturday 21 November 2026", spot.Output); // the marker stands for the pipeline's final output
        Assert.Equal([true, false], composition.Renders.Select(r => r.RunDependent));
    }

    [Fact]
    public void AComputedRunDependentValue_IsAMarker_AndItsOutputIsNotKept()
    {
        var plan = Agent("Due {{ node.today.value }}.",
            f => f.WithComputed("today", "\"date\"", "{ \"var\": \"run.date\" }", runDependent: true));

        var composition = Compose(plan, "node.dated", Scope(("node.today.value", "2026-11-21")));

        Assert.Equal("Due 2026-11-21.", Composed(composition));
        Assert.Equal("Due {{run:1}}.", composition.NeutralText);
        var spot = Assert.Single(composition.Spots);
        Assert.Equal("node.today.value", spot.Reads);
        Assert.Null(spot.Output); // part 08 § 8.2: kept only for a raw run fact; this follows 7-day retention
        Assert.Equal(CanonicalJson.HashText("2026-11-21"), spot.OutputHash);
        Assert.Equal(CanonicalJson.Hash(JsonValue.Create("2026-11-21")), spot.ValueHash);
        Assert.Equal(4, spot.Start);
    }

    [Fact]
    public void ARenderedNodesNeutralText_IsCarried()
    {
        // Part 05 § 4's table: a run-dependent rendered node's read stands as that node's run-neutral text, through the
        // same pipeline; with no neutral text in the scope, the node's content stands.
        var plan = Agent("Note: {{ node.r.content }}\nLoud: {{ node.r.content | string.upcase }}\n{{ node.r.content | renderer.quote }}\n",
            f => f.WithRendered("r", "Seen {{ run.date | date.to_string \"%d %B %Y\" }}", "\"markdown_block\"").RunDependent("nodes/r.md")
                .WithRenderer("quote", "\"markdown_block\"", "> {{ value }}"));

        var composition = Compose(plan, "node.dated", Scope(("node.r.content", "Seen 21 November 2026"), ("node.r.neutral", "Seen {{run:1}}")));

        Assert.Equal("Note: Seen 21 November 2026\nLoud: SEEN 21 NOVEMBER 2026\n> Seen 21 November 2026\n", Composed(composition));
        Assert.Equal("Note: Seen {{run:1}}\nLoud: SEEN {{RUN:1}}\n> Seen {{run:1}}\n", composition.NeutralText);
        Assert.Equal([1, 2, 3], composition.Spots.Select(s => s.Site));
        var render = Assert.Single(composition.Renders); // the pipeline over the run-neutral text gives no sub-row
        Assert.True(render.RunDependent);
        Assert.Equal(CanonicalJson.HashText("> Seen 21 November 2026"), render.OutputHash);
        Assert.All(composition.Spots, s => Assert.Null(s.Output));
        Assert.All(composition.Spots, s => Assert.Equal(CanonicalJson.Hash(JsonValue.Create("Seen 21 November 2026")), s.ValueHash));
        AssertEachSpotStandsWhereRecorded(composition);

        var bare = Compose(plan, "node.dated", Scope(("node.r.content", "Seen 21 November 2026")));
        Assert.Equal(Composed(bare), bare.NeutralText);
        Assert.Equal(3, bare.Spots.Count);
    }

    [Fact]
    public void ABodyWithNoSpot_HasNoNeutralText()
    {
        var plan = RunFixture.Plan(Package().WithAgent("draft", "Draft the note for Dr A. Sample.\n\n{{ input.consult_draft.content }}\n"));

        var composition = Compose(plan, "node.draft", Scope(("input.consult_draft.content", "Margaret Okafor, 58 (fictional).")));

        Assert.Equal("Draft the note for Dr A. Sample.\n\nMargaret Okafor, 58 (fictional).\n", Composed(composition));
        Assert.Null(composition.NeutralText);
        Assert.Empty(composition.Spots);

        // A spot in a snippet not placed is no spot of this render: one hash.
        var unplaced = Compose(Agent("Plain {{ snippet.when.content }}.", f => f.WithSnippet("when", "run {{ run.id }}")), "node.dated", new JsonObject());
        Assert.Equal("Plain .", Composed(unplaced));
        Assert.Null(unplaced.NeutralText);
        Assert.Empty(unplaced.Spots);
    }
}
