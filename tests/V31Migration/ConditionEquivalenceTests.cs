using System.Text.Json.Nodes;
using Consultologist.Api.Tests.ByteEquivalence;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Harness;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 2.4, R1): semantic equivalence with 30's evaluator. Every expression of
/// <c>conditions/expressions.json</c> that converts is evaluated by 31's evaluator on each case of
/// <c>conditions/vectors.json</c> (area 01's: what 30 answered), over the 31 data of that case
/// (<see cref="MigrationFixture.DataOf"/>), and must answer as 30 did: a condition decided as the engine decides an
/// <c>if</c>, a <c>case</c> subject or a <c>let</c> evaluated, compared as canonical JSON. A case 30 answered with an
/// error is not compared: 31's answer is listed beside 30's sentence. A difference is listed in
/// <c>conditions/equivalence.json</c> with its reason: <c>blank</c> (R1) or another reason of
/// <see cref="ExpectedDifferences.ConditionReasons"/>, else <c>unruled</c>, which is pinned here and goes to the owner.
/// An expression with no logic is listed as not converted, with its notice ids, and not compared. With
/// <c>GOLDEN_UPDATE_V31=1</c> the golden is rewritten; a rewritten golden is read before it is committed.
/// </summary>
public class ConditionEquivalenceTests
{
    private const string GoldenPath = "conditions/equivalence.json";

    /// <summary>The reason a difference gets when no ruling or spec section explains it.</summary>
    private const string Unruled = "unruled";

    private static readonly CorvusJsonLogicEvaluator Evaluator = new();

    /// <summary>
    /// The expressions § 2.2's table maps (and the package expressions) that give no logic once typed, each with its
    /// notice ids (part 10 § 2.3 as written: text compared with a literal is S03-002, part 03 § 7; a list mixing formats
    /// is A04-003; an optional read into a non-nullable field is A04-009; builtins with no row are S10-003). The § 2.3
    /// refusals (<c>refuse.*</c>) are not listed here: every one of them gives no logic by design.
    /// </summary>
    private static readonly Dictionary<string, string[]> NotConverted = new(StringComparer.Ordinal)
    {
        ["composer-macros.section_draft.section_close.if"] = ["S03-002"],
        ["composer-sources.topic_tally.let"] = ["S10-003", "S10-003"],
        ["row.read.node-content"] = ["S03-002"],
        ["row.input.x"] = ["S03-002"],
        ["row.input.x-y"] = ["S03-002"],
        ["row.downcase"] = ["S03-002"],
        ["row.let-object"] = ["A04-009"],
        ["row.let-array"] = ["A04-003"],
    };

    /// <summary>The differences no ruling or spec section explains (<c>&lt;id&gt;#&lt;case&gt;</c>), for the owner.</summary>
    /// <remarks>
    /// <c>row.size.member#0</c>: <c>input.problems.size == 0</c> with the optional list input absent. At 30 an absent
    /// list input reads <c>[]</c> (the effective map's <c>""</c>), so its size is 0 and the condition holds; at 31 it is
    /// not given and reads <c>null</c> (part 03 § 6.1), <c>size</c> of <c>null</c> is <c>null</c> and <c>===</c> compares
    /// <c>null</c> with 0 as a value (part 03 § 6.2): <c>false</c>. No ruling covers it; the package expression of the same
    /// shape (<c>example-structured-intake.notes_index.if</c>, <c>&gt; 2</c>) agrees on every case.
    /// <c>row.strip.node#0</c>: <c>(x | string.strip) == ""</c>, written <c>blank(x)</c> (part 05 § 7), with <c>x</c> null:
    /// a node with no output. 30's comparison is <c>false</c> on a null
    /// text, 31's <c>blank(null)</c> is <c>true</c>. No ruling covers it; the migration lists each such conversion
    /// (A10-050), and no public package's condition has that shape (its <c>strip == ""</c> reads are a text input whole
    /// and a folder item's text, which agree).
    /// </remarks>
    private static readonly string[] UnruledDifferences = ["row.size.member#0", "row.strip.node#0"];

    [Fact]
    public void EveryConvertedExpression_AnswersAs30Does()
    {
        var golden = Golden();
        var failures = new List<string>();
        foreach (var expression in Run())
        {
            foreach (var run in expression.Cases.Where(c => c.Error30 is null && !c.Agrees))
            {
                var listed = Listed(golden, expression.Id, run.Index);
                var reason = (string?)listed?["reason"];
                if (listed is null || reason is null)
                {
                    failures.Add($"{expression.Id}#{run.Index}: 31 gives {run.Answer31}, 30 gave {Json(run.Result30)}, and equivalence.json lists no reason.");
                }
                else if (reason == "blank" && !BlankApplies(expression, run))
                {
                    failures.Add($"{expression.Id}#{run.Index}: listed blank, but no blank conversion reads empty, white-space-only text or an input not given.");
                }
                else if (reason != Unruled && !ExpectedDifferences.ConditionReasons.ContainsKey(reason))
                {
                    failures.Add($"{expression.Id}#{run.Index}: '{reason}' is no reason of ExpectedDifferences.ConditionReasons.");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void ACaseWith30sError_IsListedNotCompared()
    {
        var golden = Golden();
        var errors = 0;
        foreach (var expression in Run())
        {
            foreach (var run in expression.Cases.Where(c => c.Error30 is not null))
            {
                errors++;
                var listed = Listed(golden, expression.Id, run.Index);
                Assert.True(listed is not null, $"{expression.Id}#{run.Index}: 30 erred and equivalence.json does not list it.");
                Assert.Equal(run.Error30, (string?)listed!["error30"]);
                Assert.Equal(run.Answer31, (string?)listed["at31"]);
                Assert.Null(listed["reason"]);
            }
        }

        Assert.True(errors > 0, "No case of a converted expression has an error at 30; the test checks nothing.");
    }

    [Fact]
    public void TheUnconvertedExpressions_AreTheseOnes()
    {
        var unconverted = Run().Where(e => !e.Converted).ToList();

        Assert.All(unconverted, e => Assert.Empty(e.Cases));
        Assert.Equal(
            NotConverted.Keys.Order(StringComparer.Ordinal),
            unconverted.Select(e => e.Id).Where(id => !id.StartsWith("refuse.", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        foreach (var expression in unconverted.Where(e => NotConverted.ContainsKey(e.Id)))
        {
            Assert.Equal(NotConverted[expression.Id], expression.NoticeIds);
        }

        Assert.Equal(
            MigrationFixture.Expressions().Select(e => (string)e["id"]!).Where(id => id.StartsWith("refuse.", StringComparison.Ordinal)).Order(StringComparer.Ordinal),
            unconverted.Select(e => e.Id).Where(id => id.StartsWith("refuse.", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheUnruledDifferences_AreTheseOnes()
    {
        var unruled = Run()
            .SelectMany(e => e.Cases.Where(c => c.Reason == Unruled).Select(c => $"{e.Id}#{c.Index}"))
            .ToList();

        Assert.Equal(UnruledDifferences, unruled);
    }

    /// <summary>
    /// R1's cases, pinned: <c>!node.summary.content</c> on <c>""</c> and <c>" "</c> (false at 30, a text is truthy), and
    /// <c>!input.reason</c> on an input absent, <c>""</c> and <c>" "</c> (false at 30, where each reads <c>""</c>; not
    /// given at 31). The absent node agrees (both true), as does <c>"lorem"</c> (both false).
    /// </summary>
    [Fact]
    public void TheBlankDifferences_AreR1sCases()
    {
        var blank = Run()
            .SelectMany(e => e.Cases.Where(c => c.Reason == "blank").Select(c => $"{e.Id}#{c.Index}"))
            .ToList();

        Assert.Equal(["row.not-text.node#1", "row.not-text.node#2", "row.not-text.input#0", "row.not-text.input#1", "row.not-text.input#2"], blank);
    }

    [Fact]
    public void DataOf_PassesInputsThrough31sStart_AndPlacesEachReadWhere31ReadsIt()
    {
        var reads = JsonNode.Parse("""
            {"input.reason":"text","input.age":"number","node.summary":"text",
             "node.scope":{"type":"classification","values":["yes","no"]},"pass.number":"number","run.date":"date"}
            """)!.AsObject();

        Assert.Equal(
            """{"node":{"scope":{"content":"{\"value\":\"yes\"}","value":{"value":"yes"}},"summary":{"content":null,"value":null}},"pass":{"number":2},"run":{"date":"2026-11-10"}}""",
            CanonicalJson.Write(MigrationFixture.DataOf(Scope("""{"input.reason":" ","input.age":"<absent>","node.summary":"<absent>","node.scope":"yes","pass.number":2,"run.date":"2026-11-10"}"""), reads)));
        Assert.Equal(
            """{"input":{"age":{"content":null,"value":0},"reason":{"content":"lorem","value":"lorem"}},"node":{"scope":{"content":null,"value":null},"summary":{"content":"","value":""}}}""",
            CanonicalJson.Write(MigrationFixture.DataOf(Scope("""{"input.reason":"lorem","input.age":0,"node.summary":"","node.scope":"<absent>"}"""), reads)));

        static JsonObject Scope(string json) => JsonNode.Parse(json)!.AsObject();
    }

    [Fact]
    public void TheGolden_IsWhatTheRunGives()
    {
        var text = ByteEquivalenceFixtures.WriteJson(GoldenOf(Run()));
        var path = Path.Combine(ByteEquivalenceFixtures.Root, "conditions", "equivalence.json");
        if (Environment.GetEnvironmentVariable("GOLDEN_UPDATE_V31") == "1")
        {
            File.WriteAllText(path, text);
        }

        Assert.True(File.Exists(path), $"{GoldenPath} is missing: run with GOLDEN_UPDATE_V31=1, then read it.");
        Assert.Equal(ByteEquivalenceFixtures.ReadText(path), text);
    }

    // ---- the run ----

    /// <summary>One case: 30's answer (<paramref name="Result30"/>, or <paramref name="Error30"/>) and 31's (<paramref name="Answer31"/>, the value's canonical JSON or the failure).</summary>
    private sealed record CaseRun(int Index, JsonObject Scope, JsonObject Data, JsonNode? Result30, string? Error30, string Answer31, bool Agrees, string? Reason);

    /// <summary>One expression: whether it converted, its notice ids, its blank conversions, how many cases 30 answered, and the cases run at 31 (none when it did not convert).</summary>
    private sealed record ExpressionRun(string Id, string Kind, bool Converted, IReadOnlyList<string> NoticeIds, IReadOnlyList<string> BlankConversions, int Cases30, IReadOnlyList<CaseRun> Cases);

    private static readonly Lazy<IReadOnlyList<ExpressionRun>> Runs = new(RunAll);

    private static IReadOnlyList<ExpressionRun> Run() => Runs.Value;

    private static IReadOnlyList<ExpressionRun> RunAll()
    {
        var vectors = JsonNode.Parse(ByteEquivalenceFixtures.ReadText(Path.Combine(ByteEquivalenceFixtures.Root, "conditions", "vectors.json")))!
            ["vectors"]!.AsArray().Select(v => v!.AsObject()).ToDictionary(v => (string)v["id"]!, StringComparer.Ordinal);
        var runs = new List<ExpressionRun>();
        foreach (var entry in MigrationFixture.Expressions())
        {
            var id = (string)entry["id"]!;
            var kind = (string)entry["kind"]!;
            var reads = entry["reads"]!.AsObject();
            var result = WorkflowConditionConverter.FromScriban(
                (string)entry["expression"]!, MigrationFixture.ScopeOf(reads), MigrationFixture.KindOf(entry), MigrationFixture.DeclaredOf(entry));
            var vectorCases = vectors[id]["cases"]!.AsArray().Select(c => c!.AsObject()).ToList();
            if (result.Logic is null)
            {
                runs.Add(new ExpressionRun(id, kind, false, result.NoticeIds, result.BlankConversions, vectorCases.Count, []));
                continue;
            }

            var rule = Evaluator.Compile(result.Logic);
            var cases = new List<CaseRun>();
            var index = 0;
            foreach (var vectorCase in vectorCases)
            {
                var scope = vectorCase["scope"]!.AsObject();
                var data = MigrationFixture.DataOf(scope, reads);
                var answer = kind == "condition" ? Decided(rule, data) : Evaluated(rule, data);
                var result30 = vectorCase["result"];
                var error30 = (string?)vectorCase["error"];
                var agrees = error30 is null && answer == Json(result30);
                var run = new CaseRun(index++, scope, data, result30, error30, answer, agrees, null);
                cases.Add(run with { Reason = error30 is null && !agrees ? ReasonOf(result.BlankConversions, run) : null });
            }

            runs.Add(new ExpressionRun(id, kind, true, result.NoticeIds, result.BlankConversions, vectorCases.Count, cases));
        }

        return runs;
    }

    /// <summary>A condition as the engine decides an <c>if</c> (part 03 § 6.4): its boolean, or the failure that fails its node.</summary>
    private static string Decided(IJsonLogicRule rule, JsonObject data)
    {
        var decision = rule.Decide(data);
        return decision.Failure is { } failure ? Failure(failure) : Json(JsonValue.Create(decision.Result));
    }

    /// <summary>A <c>case</c> subject or a <c>let</c>: its value as canonical JSON, or its failure.</summary>
    private static string Evaluated(IJsonLogicRule rule, JsonObject data)
    {
        var value = rule.Evaluate(data);
        return value.Failure is { } failure ? Failure(failure) : Json(value.Value);
    }

    private static string Failure(JsonLogicFailure failure) => $"fails: {failure.Kind}: {failure.Detail}";

    private static string Json(JsonNode? node) => CanonicalJson.Write(node);

    /// <summary>Why a case differs: <c>blank</c> when R1 explains it, else <c>unruled</c>.</summary>
    private static string ReasonOf(IReadOnlyList<string> blankConversions, CaseRun run) =>
        BlankApplies(blankConversions, run.Data) ? "blank" : Unruled;

    private static bool BlankApplies(ExpressionRun expression, CaseRun run) => BlankApplies(expression.BlankConversions, run.Data);

    /// <summary>
    /// R1: a blank conversion's path reads empty or white-space-only text here, or reads an input not given (31's start
    /// leaves out an input absent or blank; at 30 it read <c>""</c>, which <c>!x</c> holds false).
    /// </summary>
    private static bool BlankApplies(IReadOnlyList<string> blankConversions, JsonObject data) =>
        blankConversions.Any(path =>
        {
            var segments = path.Split('.');
            if (segments[0] == "input" && segments.Length > 1 && data["input"]?[segments[1]] is null)
            {
                return true;
            }

            JsonNode? at = data;
            foreach (var segment in segments)
            {
                at = at is JsonObject obj ? obj[segment] : null;
            }

            return at is JsonValue value && value.TryGetValue<string>(out var text) && text.EnumerateRunes().All(System.Text.Rune.IsWhiteSpace);
        });

    // ---- the golden ----

    private static JsonObject GoldenOf(IReadOnlyList<ExpressionRun> runs)
    {
        var expressions = new JsonArray();
        foreach (var run in runs)
        {
            var listed = new JsonArray();
            foreach (var c in run.Cases.Where(c => !c.Agrees))
            {
                listed.Add(new JsonObject
                {
                    ["case"] = c.Index,
                    ["scope"] = c.Scope.DeepClone(),
                    ["at30"] = c.Result30?.DeepClone(),
                    ["error30"] = c.Error30,
                    ["at31"] = c.Answer31,
                    ["reason"] = c.Reason,
                });
            }

            expressions.Add(new JsonObject
            {
                ["id"] = run.Id,
                ["kind"] = run.Kind,
                ["converted"] = run.Converted,
                ["noticeIds"] = new JsonArray(run.NoticeIds.Select(n => (JsonNode?)JsonValue.Create(n)).ToArray()),
                ["cases"] = run.Cases30,
                ["agreed"] = run.Converted ? run.Cases.Count(c => c.Agrees) : null,
                ["listed"] = listed,
            });
        }

        var reasons = new JsonObject();
        foreach (var (reason, ruling) in ExpectedDifferences.ConditionReasons.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            reasons[reason] = ruling;
        }

        return new JsonObject { ["reasons"] = reasons, ["equivalence"] = expressions };
    }

    private static JsonObject? Golden()
    {
        var path = Path.Combine(ByteEquivalenceFixtures.Root, "conditions", "equivalence.json");
        return File.Exists(path) ? JsonNode.Parse(ByteEquivalenceFixtures.ReadText(path))!.AsObject() : null;
    }

    private static JsonObject? Listed(JsonObject? golden, string id, int index) =>
        golden?["equivalence"]?.AsArray()
            .Select(e => e!.AsObject())
            .SingleOrDefault(e => (string?)e["id"] == id)?["listed"]?.AsArray()
            .Select(l => l!.AsObject())
            .SingleOrDefault(l => (int?)l["case"] == index);
}
