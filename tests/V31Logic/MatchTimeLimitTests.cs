using System.Diagnostics;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Logic;

/// <summary>
/// #1503 (specVersion 31, area 06; R2): each evaluation of <c>matches</c> and of <c>has_word</c> has the evaluator's time
/// limit; past it the rule fails with A06-001. An evaluator without a limit behaves as before.
/// </summary>
public class MatchTimeLimitTests
{
    private const string SlowPattern = "(.*a.{100}){3}b";

    private static readonly string MebibyteOfA = new('a', IRegexp.MaxTextBytes);

    private static JsonNode Data(string text) => new JsonObject { ["t"] = text };

    private static JsonNode Matches(string pattern) =>
        new JsonObject { ["matches"] = new JsonArray(new JsonObject { ["var"] = "t" }, pattern) };

    [Fact]
    public void ASlowPattern_FailsWithA06001()
    {
        var evaluator = new CorvusJsonLogicEvaluator(TimeSpan.FromMilliseconds(50));
        var rule = evaluator.Compile(Matches(SlowPattern));

        var clock = Stopwatch.StartNew();
        var decision = rule.Decide(Data(MebibyteOfA));
        clock.Stop();

        Assert.False(decision.Result);
        Assert.NotNull(decision.Failure);
        Assert.Equal(JsonLogicFailureKind.MatchTooSlow, decision.Failure!.Kind);
        Assert.Equal("matches", decision.Failure.Detail);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"took {clock.Elapsed}");

        var sentence = ExpressionSentences.RunTime("Node 'x'", "if", decision.Failure);
        V31Contract.AssertSentence(new SourceRefusal("A06-001", sentence, ""));
        Assert.Equal(
            "Node 'x' if could not be decided: matches took longer than 0.05 seconds on this text. Use a simpler pattern.",
            sentence);
    }

    [Theory]
    [InlineData(2000, "2")]
    [InlineData(1500, "1.5")]
    [InlineData(1234, "1.234")]
    [InlineData(50, "0.05")]
    public void TheSentence_SaysTheLimitInSeconds(int milliseconds, string seconds)
    {
        var failure = new JsonLogicFailure(JsonLogicFailureKind.MatchTooSlow, "has_word") { Limit = TimeSpan.FromMilliseconds(milliseconds) };

        Assert.Equal(
            $"Computed node 'total' expression could not be decided: has_word took longer than {seconds} seconds on this text. Use a simpler pattern.",
            ExpressionSentences.RunTime("Computed node 'total'", "expression", failure));
    }

    [Fact]
    public void AFastPattern_IsUnchangedByALimit()
    {
        var limited = new CorvusJsonLogicEvaluator(TimeSpan.FromSeconds(2));
        var unlimited = new CorvusJsonLogicEvaluator();
        var compared = 0;

        foreach (var file in LogicVectors.All().Where(f => f.Family != LogicVectors.PrinterGoldens))
        {
            foreach (var logicCase in file.Cases.Where(c => c.Rule.Contains("\"matches\"", StringComparison.Ordinal)
                         || c.Rule.Contains("\"has_word\"", StringComparison.Ordinal)))
            {
                var with = limited.Compile(JsonNode.Parse(logicCase.Rule)).Evaluate(JsonNode.Parse(logicCase.Data));
                var without = unlimited.Compile(JsonNode.Parse(logicCase.Rule)).Evaluate(JsonNode.Parse(logicCase.Data));

                Assert.Equal(without.Failure, with.Failure);
                Assert.Equal(CanonicalJson.Write(without.Value), CanonicalJson.Write(with.Value));
                compared++;
            }
        }

        Assert.True(compared > 10, $"only {compared} cases of matches and has_word");
    }

    [Fact]
    public void NoLimit_NeverTimesOut()
    {
        Assert.Null(new CorvusJsonLogicEvaluator().MatchTimeLimit);
        Assert.Equal(TimeSpan.FromSeconds(2), new CorvusJsonLogicEvaluator(TimeSpan.FromSeconds(2)).MatchTimeLimit);
    }

    [Fact]
    public void ALimitARegexCannotTake_IsRefusedAtConstruction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CorvusJsonLogicEvaluator(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CorvusJsonLogicEvaluator(TimeSpan.FromDays(30)));
    }

    [Fact]
    public void HasWord_HonoursTheLimit()
    {
        var evaluator = new CorvusJsonLogicEvaluator(TimeSpan.FromTicks(1));
        var rule = evaluator.Compile(JsonNode.Parse("""{"has_word":[{"var":"t"},"a"]}"""));

        var value = rule.Evaluate(Data(MebibyteOfA));

        Assert.Null(value.Value);
        Assert.Equal(new JsonLogicFailure(JsonLogicFailureKind.MatchTooSlow, "has_word") { Limit = TimeSpan.FromTicks(1) }, value.Failure);

        // without a limit, the same search runs to its end
        Assert.Equal("false", new CorvusJsonLogicEvaluator().Compile(JsonNode.Parse("""{"has_word":[{"var":"t"},"a"]}"""))
            .Evaluate(Data(MebibyteOfA)).Value?.ToJsonString());
    }

    [Fact]
    public void TwoEvaluators_WithDifferentLimits_DoNotShareACache()
    {
        const string pattern = "(.*a.{50}){2}b";
        var text = new string('a', 200_000); // some 200 ms without a limit
        var limited = new CorvusJsonLogicEvaluator(TimeSpan.FromTicks(1));
        var unlimited = new CorvusJsonLogicEvaluator();

        // the limited instance compiles the pattern first, with its time-out
        var slow = limited.Compile(Matches(pattern)).Evaluate(Data(text));
        Assert.Equal(JsonLogicFailureKind.MatchTooSlow, slow.Failure?.Kind);

        var value = unlimited.Compile(Matches(pattern)).Evaluate(Data(text));
        Assert.Null(value.Failure);
        Assert.Equal("false", value.Value?.ToJsonString());

        // the engine's shared cache (the one publish-time checks use) still compiles without a time-out
        Assert.Equal(System.Text.RegularExpressions.Regex.InfiniteMatchTimeout, IRegexp.Check(pattern).Regex!.MatchTimeout);
    }
}
