using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Resolve;

/// <summary>
/// #1502 (specVersion 31, area 05): part 04 § 10.2's notes at publish (§ 8; D11), worked out against the engine's numbers
/// (<see cref="PublishLimits"/>): the agent calls that could run at once (S04-072, S04-073: the product of the caps
/// along each nesting path of loops and calls), a loop whose list could pass the items limit (S04-079), and the agent
/// calls one job could make (S04-080). Notes never refuse; with no limits there are none.
/// </summary>
public class PublishNotesTests
{
    private const string Plain = "\"plaintext\"";

    private static readonly PublishLimits Engine = new(16, 200, 500);

    private static IReadOnlyList<SourceRefusal> Notes(Dictionary<string, byte[]> files, PublishLimits limits)
    {
        var check = SourceCheck.Run(files, ResolveFixture.Published());
        Assert.True(check.Refusals.Count == 0, ResolveFixture.Said(check));
        var notes = PublishNotes.Of(check.Parse.Package!, check.Resolution!, limits);
        Assert.All(notes, V31Contract.AssertSentence);
        return notes;
    }

    private static string[] Said(IReadOnlyList<SourceRefusal> notes, string id) =>
        notes.Where(n => n.Id == id).Select(n => n.Sentence).ToArray();

    /// <summary>The required input <c>points</c>, a list of <c>plaintext</c>, beside the consult draft.</summary>
    private static Dictionary<string, byte[]> WithPoints(Dictionary<string, byte[]> files) =>
        files.WithJson("manifest.json", m => m["inputs"]!["points"] = JsonNode.Parse(
            "{ \"label\": \"Points\", \"description\": null, \"format\": " + ArrayOf(Plain) + ", \"required\": true, \"channel\": null }"));

    private static string Loop(string label, string format, string source, bool parallel, string maxParallel, string body, string ret) =>
        Json(SourceFixture.Node("for", label, format, false, true,
            $"\"in\": \"{source}\", \"parallel\": {(parallel ? "true" : "false")}, \"maxParallel\": {maxParallel}, \"until\": null, "
            + $"\"max\": null, \"itemLabel\": null, \"nodes\": [\"{body}\"], \"ret\": \"{ret}\""));

    /// <summary>Minimal with the loop <c>outer</c> (cap <paramref name="outer"/>) over the points, holding <c>inner</c> (cap <paramref name="inner"/>), holding the agent <c>draft</c>.</summary>
    private static Dictionary<string, byte[]> Nested(string outer, string inner) =>
        WithPoints(Minimal())
            .With("nodes/index.json", Index(["plan", "outer"]))
            .With("nodes/outer/index.json", Loop("Outer", ArrayOf(ArrayOf(Plain)), "input.points.value", true, outer, "inner", "node.outer.inner"))
            .With("nodes/outer/inner/index.json", Loop("Inner", ArrayOf(Plain), "input.points.value", true, inner, "draft", "node.outer.inner.draft"))
            .With("nodes/outer/inner/draft.md", Agent("Drafting a point", Plain, "Write one sentence for Dr A. Sample about {{ item.value }}.\n"));

    /// <summary><see cref="ResolveFixture.Functions"/> with a third standard in the folder, and the loop over it uncapped.</summary>
    private static Dictionary<string, byte[]> ThreeStandards() =>
        ResolveFixture.Functions()
            .WithJson("snippets/standards/index.json", i => i["items"]!.AsArray().Add("social_history"))
            .With("snippets/standards/social_history.md", Md("{ \"title\": \"Social History\" }", "Who Margaret Okafor lives with, and what she does.\n"))
            .WithJson("nodes/standards/index.json", h => h["maxParallel"] = null);

    [Fact]
    public void NestedCaps_Multiply()
    {
        var notes = Notes(Nested("4", "5"), Engine);
        Assert.Equal(["Up to 20 agent calls could run at once; this engine runs at most 16 per job, so the rest wait their turn."],
            Said(notes, "S04-072"));
        Assert.Empty(Said(notes, "S04-073"));
        Assert.All(notes.Where(n => n.Id is "S04-072" or "S04-073" or "S04-080"), n => Assert.Equal("", n.Path));

        Assert.Empty(Said(Notes(Nested("4", "5"), Engine with { Ceiling = 20 }), "S04-072"));
        Assert.Empty(Said(Notes(Nested("4", "null").WithJson("nodes/outer/inner/index.json", h => h["parallel"] = false), Engine), "S04-072"));
    }

    [Fact]
    public void AnUncappedLoop_OverAListKnownAtRunTime_IsUnbounded()
    {
        var notes = Notes(ResolveFixture.NestedNote(), Engine);
        Assert.Contains(
            "An unbounded number of agent calls could run at once (loop 'sections' has no cap over a list known only at run time); this engine runs at most 16 per job, so the rest wait their turn.",
            Said(notes, "S04-073"));
        Assert.Empty(Said(notes, "S04-072"));

        // A loop with no agent beneath it is not a note's concern.
        Assert.DoesNotContain(Said(Notes(ResolveFixture.Functions(), Engine), "S04-073"), s => s.Contains("section_coverage", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUncappedLoop_OverASnippetFolder_CountsItsLength()
    {
        var notes = Notes(ThreeStandards(), Engine with { Ceiling = 2 });
        Assert.Equal(["Up to 3 agent calls could run at once; this engine runs at most 2 per job, so the rest wait their turn."],
            Said(notes, "S04-072"));
        Assert.Empty(Said(notes, "S04-073"));
        Assert.Empty(Said(Notes(ThreeStandards(), Engine with { Ceiling = 3 }), "S04-072"));
    }

    [Fact]
    public void AListWithNoMaxItems_CouldPassTheItemsLimit()
    {
        var notes = Notes(ResolveFixture.NestedNote(), Engine).Where(n => n.Id == "S04-079").ToList();
        Assert.Contains(notes, n => (n.Sentence, n.Path) == (
            "Loop 'sections' iterates node.plan.value.sections, which could hold more than 200 items; this engine runs at most 200 per loop, and fails the loop beyond that.",
            "nodes/sections/index.json"));
        Assert.Contains(notes, n => n.Path == "nodes/sections/paragraphs/index.json");

        // A snippet folder's length is known; a check loop over .items has what the production loop ran.
        Assert.Empty(Said(Notes(ResolveFixture.Functions(), Engine), "S04-079"));
        Assert.Single(Said(Notes(ResolveFixture.Functions(), Engine with { ItemsPerLoop = 1 }), "S04-079"));
    }

    [Fact]
    public void TheCallsOfAJob_AreCountedThroughLoopsAndCalls()
    {
        // 3 standards × 200 points × one agent, beside 3 standards × the function's 3 agents and the 3 top-level agents.
        var files = WithPoints(ThreeStandards())
            .WithJson("nodes/index.json", i => i["items"]!.AsArray().Add("per_standard"))
            .With("nodes/per_standard/index.json", Loop("Per standard", ArrayOf(ArrayOf(Plain)), "snippet.standards", false, "null", "per_point", "node.per_standard.per_point"))
            .With("nodes/per_standard/per_point/index.json", Loop("Per point", ArrayOf(Plain), "input.points.value", false, "null", "point", "node.per_standard.per_point.point"))
            .With("nodes/per_standard/per_point/point.md", Agent("Drafting a point", Plain, "Write one sentence about {{ item.value }}.\n"));
        Assert.Equal(["Up to 612 agent calls could run in one job; this engine runs at most 500 per job, and fails the job beyond that."],
            Said(Notes(files, Engine), "S04-080"));
        Assert.Empty(Said(Notes(files, Engine with { CallsPerJob = 612 }), "S04-080"));
    }

    /// <summary>
    /// Minimal with the call <c>start</c> of <c>f01</c>; each of the <paramref name="functions"/> functions holds
    /// <paramref name="calls"/> calls of the next, and the last the agent <c>draft</c>.
    /// </summary>
    private static Dictionary<string, byte[]> Chain(int functions, int calls)
    {
        static string Id(int i) => $"f{i:00}";
        static string Call(string function) => Md(SourceFixture.Node("call", "Calling the next step", Plain, false, true,
            $"\"if\": null, \"function\": \"{function}\", \"with\": {{}}"), "");

        var files = Minimal()
            .With("nodes/index.json", Index(["plan", "start"]))
            .With("nodes/start.md", Call(Id(1)))
            .With("functions/index.json", Index(Enumerable.Range(1, functions).Select(Id)));
        for (var i = 1; i <= functions; i++)
        {
            var last = i == functions;
            var nodes = last ? ["draft"] : Enumerable.Range(1, calls).Select(c => $"c{c}").ToArray();
            files.With($"functions/{Id(i)}/index.json", Json(
                $"{{ \"label\": \"Step {i}\", \"description\": [\"One step of a chain of calls.\"], \"inputs\": {{}}, "
                + "\"uses\": { \"snippets\": [], \"macros\": [], \"formats\": [], \"renderers\": [] }, "
                + $"\"nodes\": [{string.Join(", ", nodes.Select(n => $"\"{n}\""))}], \"ret\": \"node.{nodes[0]}\", \"format\": \"plaintext\" }}"));
            foreach (var folder in new[] { "snippets", "macros", "formats", "renderers" })
                files.With($"functions/{Id(i)}/{folder}/index.json", Index([]));
            if (last)
                files.With($"functions/{Id(i)}/draft.md", Agent("Drafting a sentence", Plain, "Write one sentence for Dr A. Sample.\n"));
            else
            {
                foreach (var node in nodes)
                    files.With($"functions/{Id(i)}/{node}.md", Call(Id(i + 1)));
            }
        }

        return files;
    }

    [Fact]
    public void AWideChainOfCalls_IsWalkedOncePerFunction()
    {
        // 14 functions, each calling the next 6 times: 6^13 runs of the agent at the bottom (and the plan), one at a time. Walked again
        // at every call site this never returns; each function is worked out once.
        var files = Chain(14, 6);
        var check = SourceCheck.Run(files, ResolveFixture.Published());
        Assert.True(check.Refusals.Count == 0, ResolveFixture.Said(check));

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var notes = PublishNotes.Of(check.Parse.Package!, check.Resolution!, Engine);
        clock.Stop();
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"The notes took {clock.Elapsed}.");

        Assert.Empty(Said(notes, "S04-072"));
        Assert.Empty(Said(notes, "S04-073"));
        Assert.Equal(["Up to 13060694017 agent calls could run in one job; this engine runs at most 500 per job, and fails the job beyond that."],
            Said(notes, "S04-080"));
    }

    [Fact]
    public void NoLimits_NoNotes()
    {
        var check = SourceCheck.Run(ResolveFixture.NestedNote(), ResolveFixture.Published());
        var compiled = ResolveCompiler.Compile(check, ResolveFixture.Published(), "7.2.5");
        Assert.DoesNotContain(compiled.Warnings, w => w.Id is "S04-072" or "S04-073" or "S04-079" or "S04-080");

        var limited = ResolveCompiler.Compile(check, ResolveFixture.Published(), "7.2.5", Engine);
        Assert.Contains(limited.Warnings, w => w.Id == "S04-073");
        Assert.Contains(limited.Document!["warnings"]!.AsArray(), w => w!["at"] == null);
        Assert.Throws<ArgumentNullException>(() => PublishNotes.Of(check.Parse.Package!, check.Resolution!, null!));
    }
}
