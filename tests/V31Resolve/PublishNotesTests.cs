using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
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

    /// <summary>
    /// <paramref name="files"/> with the function <paramref name="id"/>: its <paramref name="inputs"/> (JSON members),
    /// <paramref name="nodes"/> (path under the function to file text), its top-level <paramref name="body"/>, and its
    /// <paramref name="ret"/> of <paramref name="format"/>.
    /// </summary>
    private static Dictionary<string, byte[]> WithFunction(
        Dictionary<string, byte[]> files, string id, string inputs, string[] body, string ret, string format, Dictionary<string, string> nodes)
    {
        files.With("functions/index.json", Index([id]))
            .With($"functions/{id}/index.json", Json(
                $"{{ \"label\": \"Drafting\", \"description\": null, \"inputs\": {{ {inputs} }}, "
                + "\"uses\": { \"snippets\": [], \"macros\": [], \"formats\": [], \"renderers\": [] }, "
                + $"\"nodes\": [{string.Join(", ", body.Select(n => $"\"{n}\""))}], \"ret\": \"{ret}\", \"format\": {format} }}"));
        foreach (var folder in new[] { "snippets", "macros", "formats", "renderers" })
            files.With($"functions/{id}/{folder}/index.json", Index([]));
        foreach (var (path, text) in nodes)
            files.With($"functions/{id}/{path}", text);
        return files;
    }

    private static string CallOf(string function, string with = "{}") =>
        Md(SourceFixture.Node("call", "Drafting through the function", Plain, false, true,
            $"\"if\": null, \"function\": \"{function}\", \"with\": {with}"), "");

    [Fact]
    public void OneFunction_CalledUnderDifferentCaps_TakesEachSitesOwn()
    {
        // draft_one holds one agent. It is called inside loop a (cap 3) and inside b2 (cap 5) inside b (cap 2): the
        // largest product is the second site's, 10, though the function was first worked out at the first.
        var files = WithFunction(WithPoints(Minimal()), "draft_one", "", ["draft"], "node.draft", Plain,
                new() { ["draft.md"] = Agent("Drafting a sentence", Plain, "Write one sentence for Dr A. Sample.\n") })
            .With("nodes/index.json", Index(["plan", "a", "b"]))
            .With("nodes/a/index.json", Loop("A", ArrayOf(Plain), "input.points.value", true, "3", "call_a", "node.a.call_a"))
            .With("nodes/a/call_a.md", CallOf("draft_one"))
            .With("nodes/b/index.json", Loop("B", ArrayOf(ArrayOf(Plain)), "input.points.value", true, "2", "b2", "node.b.b2"))
            .With("nodes/b/b2/index.json", Loop("B2", ArrayOf(Plain), "input.points.value", true, "5", "call_b", "node.b.b2.call_b"))
            .With("nodes/b/b2/call_b.md", CallOf("draft_one"));
        Assert.Equal(["Up to 10 agent calls could run at once; this engine runs at most 9 per job, so the rest wait their turn."],
            Said(Notes(files, Engine with { Ceiling = 9 }), "S04-072"));
        Assert.Empty(Said(Notes(files, Engine with { Ceiling = 10 }), "S04-072"));

        // The other way round: the first site's product is the larger.
        var swapped = files.WithJson("nodes/a/index.json", h => h["maxParallel"] = 12);
        Assert.Equal(["Up to 12 agent calls could run at once; this engine runs at most 9 per job, so the rest wait their turn."],
            Said(Notes(swapped, Engine with { Ceiling = 9 }), "S04-072"));
    }

    [Fact]
    public void ACalleesUnboundedLoop_IsNamedAtTheCall()
    {
        // draft_points loops, uncapped, over the points it is passed, an agent beneath: the call makes it S04-073.
        var files = WithFunction(WithPoints(Minimal()), "draft_points",
                "\"points\": { \"label\": \"Points\", \"description\": null, \"format\": " + ArrayOf(Plain) + " }",
                ["each"], "node.each", ArrayOf(Plain), new()
                {
                    ["each/index.json"] = Loop("Each point", ArrayOf(Plain), "param.points.value", true, "null", "draft", "node.each.draft"),
                    ["each/draft.md"] = Agent("Drafting a point", Plain, "Write one sentence about {{ item.value }}.\n"),
                })
            .With("nodes/index.json", Index(["plan", "outer"]))
            .With("nodes/outer/index.json", Loop("Outer", ArrayOf(ArrayOf(Plain)), "input.points.value", true, "null", "drafted", "node.outer.drafted"))
            .With("nodes/outer/drafted.md", CallOf("draft_points", "{ \"points\": \"input.points.value\" }")
                .Replace(Plain + ", \"reproducible\"", ArrayOf(Plain) + ", \"reproducible\"", StringComparison.Ordinal));
        var notes = Notes(files, Engine);
        Assert.Equal(
        [
            "An unbounded number of agent calls could run at once (loop 'outer' has no cap over a list known only at run time); this engine runs at most 16 per job, so the rest wait their turn.",
            "An unbounded number of agent calls could run at once (loop 'each' has no cap over a list known only at run time); this engine runs at most 16 per job, so the rest wait their turn.",
        ], Said(notes, "S04-073"));
        Assert.Empty(Said(notes, "S04-072"));
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

    // ---- #1508 (area 11; R1): the instances one job could open (A11-001) ------------------------------------

    private static readonly PublishLimits Counted = Engine with { Instances = 10_000 };

    /// <summary>The A11-001 refusals of <paramref name="files"/> against <paramref name="limits"/>, each its area sentence.</summary>
    private static IReadOnlyList<SourceRefusal> Refused(Dictionary<string, byte[]> files, PublishLimits limits)
    {
        var check = SourceCheck.Run(files, ResolveFixture.Published());
        Assert.True(check.Refusals.Count == 0, ResolveFixture.Said(check));
        var refusals = PublishNotes.Refusals(check.Parse.Package!, check.Resolution!, limits);
        Assert.All(refusals, V31Contract.AssertSentence);
        return refusals;
    }

    /// <summary>A sequential loop over the points with <paramref name="max"/>, its body the one node <paramref name="body"/>.</summary>
    private static string PointsLoop(string label, string format, string max, string body, string ret, bool agentBeneath = false) =>
        Json(SourceFixture.Node("for", label, format, !agentBeneath, agentBeneath,
            $"\"in\": \"input.points.value\", \"parallel\": false, \"maxParallel\": null, \"until\": null, "
            + $"\"max\": {max}, \"itemLabel\": null, \"nodes\": [\"{body}\"], \"ret\": \"{ret}\""));

    /// <summary>
    /// Minimal with three loops over the points nested in one another (<c>outer</c>, <c>middle</c>, <c>inner</c>), each
    /// of <paramref name="max"/>, and at the bottom the computed node <c>echo</c>: no agent anywhere beneath them.
    /// </summary>
    private static Dictionary<string, byte[]> ThreeDeep(string max) =>
        WithPoints(Minimal())
            .With("nodes/index.json", Index(["plan", "outer"]))
            .With("nodes/outer/index.json", PointsLoop("Outer", ArrayOf(ArrayOf(ArrayOf(Plain))), max, "middle", "node.outer.middle"))
            .With("nodes/outer/middle/index.json", PointsLoop("Middle", ArrayOf(ArrayOf(Plain)), max, "inner", "node.outer.middle.inner"))
            .With("nodes/outer/middle/inner/index.json", PointsLoop("Inner", ArrayOf(Plain), max, "echo", "node.outer.middle.inner.echo"))
            .With("nodes/outer/middle/inner/echo.md", Md(SourceFixture.Node("computed", "Repeating a point", Plain, true, false,
                "\"if\": null, \"expression\": { \"var\": \"item.value\" }"), ""));

    private static string TooManySteps(long n, int limit) =>
        $"Up to {n:N0} steps could run in one job, through node.outer; this engine runs at most {limit:N0} for one job. Lower a loop's max or split the workflow.";

    [Fact]
    public void ThreeNestedLoopsOf200_PassTheInstancesLimit_AndAreRefused()
    {
        // Every instance the runner opens counts, not only agent calls: the plan and the note, and each loop, its 200
        // items and each item's body: inner 1 + 200 × (1 + 1) = 401; middle 1 + 200 × (1 + 401); outer 1 + 200 × (1 + 80,401).
        var refusal = Assert.Single(Refused(ThreeDeep("200"), Counted));
        Assert.Equal("A11-001", refusal.Id);
        Assert.Equal(TooManySteps(16_080_403, 10_000), refusal.Sentence);
        Assert.Equal("nodes/outer/index.json", refusal.Path);

        // With no instances limit nothing is measured.
        Assert.Empty(Refused(ThreeDeep("200"), Engine));
    }

    [Fact]
    public void APackageAtExactlyTheInstancesLimit_IsNotRefused()
    {
        // inner 1 + 2 × 2 = 5; middle 1 + 2 × 6 = 13; outer 1 + 2 × 14 = 29; the plan and the note: 31.
        Assert.Empty(Refused(ThreeDeep("2"), Engine with { Instances = 31 }));
        Assert.Equal(TooManySteps(31, 30), Assert.Single(Refused(ThreeDeep("2"), Engine with { Instances = 30 })).Sentence);
    }

    [Fact]
    public void ALoopWithNoMax_OverAListKnownOnlyAtRunTime_IsBoundedByTheItemsLimit()
    {
        // No loop is unbounded for this count: one with no max over a list known only at run time runs at most the items
        // limit, which fails the loop beyond it.
        Assert.Equal(TooManySteps(16_080_403, 10_000), Assert.Single(Refused(ThreeDeep("null"), Counted)).Sentence);
        Assert.Empty(Refused(ThreeDeep("null"), Counted with { ItemsPerLoop = 2, Instances = 31 }));
        Assert.Single(Refused(ThreeDeep("null"), Counted with { ItemsPerLoop = 2, Instances = 30 }));
    }

    [Fact]
    public void TheHeaviestPath_IsNamedThroughAnIf_AtItsOutermostLoop()
    {
        // The loops sit in an if's then: the outermost loop on the heaviest path is named, not the if.
        var files = ThreeDeep("200")
            .WithJson("manifest.json", m => m["inputs"]!["include_billing"] = JsonNode.Parse(
                "{ \"label\": \"Include billing\", \"description\": null, \"format\": \"boolean\", \"required\": false, \"channel\": null }"))
            .With("nodes/index.json", Index(["plan", "gate"]))
            .With("nodes/gate/index.json", Json(SourceFixture.Node("if", "Only with billing", ArrayOf(ArrayOf(ArrayOf(Plain))), true, false,
                "\"if\": { \"===\": [{ \"var\": \"input.include_billing.value\" }, true] }, \"then\": { \"nodes\": [\"outer\"], \"ret\": \"node.gate.outer\" }, \"else\": null")));
        foreach (var path in files.Keys.Where(p => p.StartsWith("nodes/outer/", StringComparison.Ordinal)).ToList())
        {
            files["nodes/gate/" + path["nodes/".Length..]] = files[path];
            files.Remove(path);
        }

        files.WithJson("nodes/gate/outer/index.json", h => h["ret"] = "node.gate.outer.middle")
            .WithJson("nodes/gate/outer/middle/index.json", h => h["ret"] = "node.gate.outer.middle.inner")
            .WithJson("nodes/gate/outer/middle/inner/index.json", h => h["ret"] = "node.gate.outer.middle.inner.echo");
        var refusal = Assert.Single(Refused(files, Counted));
        Assert.Equal(TooManySteps(16_080_404, 10_000).Replace("node.outer", "node.gate.outer", StringComparison.Ordinal), refusal.Sentence);
        Assert.Equal("nodes/gate/outer/index.json", refusal.Path);
    }

    /// <summary>
    /// Minimal with the points; the sequential loop <c>outer</c> over them holding <c>inner</c>, over them too, holding a
    /// call of <c>draft_one</c> (one agent); and the while <c>again</c>, its check first, holding until its <c>max</c> of 2
    /// and passing the computed <c>count</c>.
    /// </summary>
    private static Dictionary<string, byte[]> EveryKindOfStep() =>
        WithFunction(WithPoints(Minimal()), "draft_one", "", ["draft"], "node.draft", Plain,
                new() { ["draft.md"] = Agent("Drafting a sentence", Plain, "Write one sentence for Dr A. Sample.\n") })
            .With("nodes/index.json", Index(["plan", "outer", "again"]))
            .With("nodes/outer/index.json", PointsLoop("Outer", ArrayOf(ArrayOf(Plain)), "null", "inner", "node.outer.inner", agentBeneath: true))
            .With("nodes/outer/inner/index.json", PointsLoop("Inner", ArrayOf(Plain), "null", "drafted", "node.outer.inner.drafted", agentBeneath: true))
            .With("nodes/outer/inner/drafted.md", CallOf("draft_one"))
            .With("nodes/again/index.json", Json(SourceFixture.Node("while", "Counting the passes", "\"number\"", true, false,
                "\"while\": { \"<\": [{ \"var\": \"pass.number\" }, 99] }, \"max\": 2, \"until\": null, \"checkFirst\": true, "
                + "\"nodes\": [\"count\"], \"ret\": \"node.again.count\"")))
            .With("nodes/again/count.md", Md(SourceFixture.Node("computed", "The pass's number", "\"number\"", true, false,
                "\"if\": null, \"expression\": { \"var\": \"pass.number\" }"), ""));

    [Fact]
    public async Task TheCount_IsWhatTheRunnerOpens()
    {
        // outer 1 + 2 × (1 + inner), inner 1 + 2 × (1 + the call's 2) = 7: 17. The while opens a third pass whose check
        // first finds it past max: 1 + 3 + 2 × 1 = 6. With the plan and the note: 25.
        var files = EveryKindOfStep();
        var limits = Engine with { ItemsPerLoop = 2 };
        Assert.Empty(Refused(files, limits with { Instances = 25 }));
        Assert.Equal("Up to 25 steps could run in one job, through node.outer; this engine runs at most 24 for one job. Lower a loop's max or split the workflow.",
            Assert.Single(Refused(files, limits with { Instances = 24 })).Sentence);

        // The runner, given two points, opens exactly that many: at 25 the job completes, at 24 it reaches A09-001.
        var plan = V31Run.RunFixture.Plan(files);
        var points = new JsonArray("Margaret Okafor's blood pressure", "Margaret Okafor's medicines");
        var given = V31Run.PlanRunnerLeavesTests.Inputs();
        var inputs = given with
        {
            Inputs = new Dictionary<string, JsonNode?>(given.Inputs, StringComparer.Ordinal)
            {
                ["points"] = new JsonObject { ["value"] = points, ["content"] = null, ["title"] = null },
            },
        };

        async Task<RunResult> Run(int instances) =>
            await new PlanRunner(plan, new RunLimits(JobInstances: instances),
                new V31Run.FakeRunHost(auto: true), new V31Run.ListRecorder()).RunAsync(inputs, V31Run.FakeRunHost.Start);

        Assert.Equal(RunState.Completed, (await Run(25)).State);
        var under = await Run(24);
        Assert.Equal(RunState.Failed, under.State);
        Assert.Equal("The job reached 24 steps, the most this engine runs for one job.", under.Sentence);
    }

    [Fact]
    public void Compile_RefusesPastTheInstancesLimit_AndMeasuresNothingWithoutIt()
    {
        var check = SourceCheck.Run(ThreeDeep("200"), ResolveFixture.Published());
        var refused = ResolveCompiler.Compile(check, ResolveFixture.Published(), "7.2.5", Counted);
        Assert.Null(refused.Document);
        Assert.Equal(["A11-001"], refused.Refusals.Select(r => r.Id));

        Assert.NotNull(ResolveCompiler.Compile(check, ResolveFixture.Published(), "7.2.5", Engine).Document);
        Assert.NotNull(ResolveCompiler.Compile(check, ResolveFixture.Published(), "7.2.5").Document);
        Assert.Throws<ArgumentNullException>(() => PublishNotes.Refusals(check.Parse.Package!, check.Resolution!, null!));
    }

    // ---- #1508 (area 11; D16): the worst case's minutes (S09-016) ---------------------------------------------

    [Fact]
    public void TheWorstCasesMinutes_PastTheJobLimit_AreANote_AfterTheCalls()
    {
        // 612 agent calls, 16 at a time: 39 rounds of a minute.
        var files = WithPoints(ThreeStandards())
            .WithJson("nodes/index.json", i => i["items"]!.AsArray().Add("per_standard"))
            .With("nodes/per_standard/index.json", Loop("Per standard", ArrayOf(ArrayOf(Plain)), "snippet.standards", false, "null", "per_point", "node.per_standard.per_point"))
            .With("nodes/per_standard/per_point/index.json", Loop("Per point", ArrayOf(Plain), "input.points.value", false, "null", "point", "node.per_standard.per_point.point"))
            .With("nodes/per_standard/per_point/point.md", Agent("Drafting a point", Plain, "Write one sentence about {{ item.value }}.\n"));
        var name = (string)JsonNode.Parse(files["manifest.json"])!["name"]!;

        var notes = Notes(files, Engine with { JobMinutes = 30 });
        Assert.Equal([$"{name} could run for about 39 minutes in the worst case; this engine stops a job after 30 minutes."], Said(notes, "S09-016"));
        Assert.Equal(["S04-080", "S09-016"], notes.Select(n => n.Id).TakeLast(2));
        Assert.Equal("", notes[^1].Path);

        Assert.Empty(Said(Notes(files, Engine with { JobMinutes = 39 }), "S09-016"));
        Assert.Empty(Said(Notes(files, Engine), "S09-016"));
    }

    // ---- #1508 (area 11; R4, R5): the formats publish refuses (A11-002, A11-003) -----------------------------

    private const string Dialect = "\"$schema\": \"urn:consultologist:spec:31:dialect\"";

    /// <summary>Minimal with the package formats <paramref name="formats"/> and the optional input <c>results</c> of the first.</summary>
    private static Dictionary<string, byte[]> WithFormatFiles(params (string Id, string Schema)[] formats)
    {
        var files = Minimal()
            .With("formats/index.json", Index(formats.Select(f => f.Id)))
            .WithJson("manifest.json", m => m["inputs"]!["results"] = JsonNode.Parse(
                $"{{ \"label\": \"Results\", \"description\": null, \"format\": \"{formats[0].Id}\", \"required\": false, \"channel\": null }}"));
        foreach (var (id, schema) in formats)
            files.With($"formats/{id}.json", Json(schema));
        return files;
    }

    [Fact]
    public void AFormatThatNamesAFormat_AndDefinesTheSameNameItself_IsRefused()
    {
        const string b = "{ " + Dialect + ", \"type\": \"object\", \"required\": [\"slot\"], \"additionalProperties\": false, "
            + "\"properties\": { \"slot\": { \"title\": \"Slot\", \"type\": \"string\", \"enum\": [\"morning\", \"afternoon\"] } } }";
        static string A(string reference, string defs) =>
            "{ " + Dialect + ", \"type\": \"object\", \"required\": [\"when\"], \"additionalProperties\": false, "
            + $"\"properties\": {{ \"when\": {{ \"title\": \"When\", \"$ref\": \"{reference}\" }} }}, \"$defs\": {{ {defs} }} }}";
        const string own = "\"b\": { \"type\": \"string\", \"enum\": [\"today\", \"next_visit\"] }";

        var check = SourceCheck.Run(WithFormatFiles(("a", A("b", own)), ("b", b)), ResolveFixture.Published());
        var refusal = Assert.Single(check.Refusals);
        Assert.Equal(
            ("A11-002", "Format 'a' names format 'b' and also defines 'b' in its own $defs. Rename the definition or remove it.", "formats/a.json"),
            (refusal.Id, refusal.Sentence, refusal.Path));
        V31Contract.AssertSentence(refusal);

        // A definition no reference names, or one the file reaches only as its own, is as before.
        Assert.Empty(SourceCheck.Run(WithFormatFiles(("a", A("b", "\"c\": { \"type\": \"string\" }")), ("b", b)), ResolveFixture.Published()).Refusals);
        Assert.Empty(SourceCheck.Run(WithFormatFiles(("a", A("#/$defs/b", own)), ("b", b)), ResolveFixture.Published()).Refusals);
    }

    /// <summary>
    /// The format <c>chart</c>: <paramref name="fields"/> fields, each the format <c>codes</c>, an object whose one field is an enum of 2,000 values, so
    /// its expansion writes about 2,000 nodes a field.
    /// </summary>
    private static Dictionary<string, byte[]> ManyLargeEnums(int fields)
    {
        var codes = "{ " + Dialect + ", \"type\": \"object\", \"required\": [\"code\"], \"additionalProperties\": false, "
            + "\"properties\": { \"code\": { \"title\": \"Code\", \"type\": \"string\", \"enum\": ["
            + string.Join(", ", Enumerable.Range(0, 2000).Select(i => $"\"code_{i}\"")) + "] } } }";
        var names = Enumerable.Range(0, fields).Select(i => $"f{i}").ToList();
        var chart = "{ " + Dialect + ", \"type\": \"object\", \"required\": [" + string.Join(", ", names.Select(n => $"\"{n}\"")) + "], "
            + "\"additionalProperties\": false, \"properties\": { " + string.Join(", ", names.Select(n => $"\"{n}\": {{ \"title\": \"Field {n}\", \"$ref\": \"codes\" }}")) + " } }";
        return WithFormatFiles(("chart", chart), ("codes", codes));
    }

    [Fact]
    public void AFormatPastTheRunsExpansionBudget_IsRefusedAtPublish()
    {
        // 600 fields of a 2,000-value enum are 1.2 million nodes, past the budget the run expands a format to.
        Assert.True(600 * 2000 > PlanFormats.NodeBudget);
        var check = SourceCheck.Run(ManyLargeEnums(600), ResolveFixture.Published());
        Assert.True(check.Refusals.Count == 0, ResolveFixture.Said(check));
        var refused = ResolveCompiler.Compile(check, ResolveFixture.Published(), "7.2.5");
        Assert.Null(refused.Document);
        var refusal = Assert.Single(refused.Refusals);
        Assert.Equal(
            ("A11-003", $"Format 'chart' expands to more than {PlanFormats.NodeBudget:N0} parts, more than this engine can run. Make it smaller.", "formats/chart.json"),
            (refusal.Id, refusal.Sentence, refusal.Path));
        V31Contract.AssertSentence(refusal);

        // Within it, the same shape publishes, and the run expands it: publish and run hold one budget.
        var plan = V31Run.RunFixture.Plan(ManyLargeEnums(100));
        var expanded = plan.Formats.Expand(new JsonObject { ["$ref"] = "#/$defs/chart" });
        Assert.Equal(100, (expanded["properties"] as JsonObject)?.Count);
    }
}
