using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Run;
using Consultologist.Api.Tests.V31Source;
using Consultologist.Api.V31;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1505 (specVersion 31, area 08; R6; part 09 § 3.1): <c>evaluate-rule-v31</c> evaluates the entry's compiled rule (a
/// condition to its boolean, an <c>expression</c> to its value) over the call's scope, in the job's plan loaded as the
/// render activity loads it; a plan that is not the job's, or an entry or a rule it lacks, is
/// <see cref="RuleTurn.PlanFailure"/>. Its telemetry names the call, never the scope. Content is fictional (Margaret Okafor,
/// Dr A. Sample).
/// </summary>
public class EvaluateRuleV31ActivityTests
{
    private const string Pattern = "{ \"matches\": [{ \"var\": \"input.consult_draft.content\" }, \"Okafor\"] }";

    [Fact]
    public async Task TheActivity_EvaluatesTheCompiledRule()
    {
        var (activity, log, job) = Activity();

        var held = await activity.RunAsync(Input(job, new RuleCall("node.plan", "node.plan", null, "if", Scope(PlanRunnerLeavesTests.Draft))), CancellationToken.None);
        var other = await activity.RunAsync(Input(job, new RuleCall("node.plan", "node.plan", null, "if", Scope("Dr A. Sample's clinic list (fictional)."))), CancellationToken.None);
        var named = await activity.RunAsync(Input(job, new RuleCall("node.named", "node.named", null, "expression", Scope(PlanRunnerLeavesTests.Draft))), CancellationToken.None);

        Assert.Equal((true, (string?)null), ((bool)held.Value!, held.FailureKind));
        Assert.Equal((false, (string?)null), ((bool)other.Value!, other.FailureKind));
        Assert.Equal("{\"named\":true}", ((JsonObject)named.Value!).ToJsonString());

        // the call is logged, never its scope
        Assert.Contains("Member=if", log.Everything, StringComparison.Ordinal);
        Assert.DoesNotContain("Okafor", log.Everything, StringComparison.Ordinal);
        Assert.DoesNotContain("clinic list", log.Everything, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheActivity_TimesAPattern_AtTheJobsRecordedLimit_NotTheSettingsNow()
    {
        // the store's plan is built with the engine's setting now (2000 ms); the job's start recorded 1 ms, and that limit
        // is the one the pattern is timed at (A06-001 says it)
        var (activity, _, job) = Activity();
        var text = new string('a', 1 << 20);

        var outcome = await activity.RunAsync(Input(job, new RuleCall("node.slow", "node.slow", null, "expression", Scope(text))) with { RegexMilliseconds = 1 },
            CancellationToken.None);

        Assert.Equal(new RuleOutcome(null, "MatchTooSlow", 0.001, "matches"), outcome);
    }

    [Fact]
    public async Task TheActivity_OfAPlanThatIsNotTheJobs_Fails()
    {
        var (activity, _, job) = Activity();
        var call = new RuleCall("node.plan", "node.plan", null, "if", Scope(PlanRunnerLeavesTests.Draft));

        var otherHash = await activity.RunAsync(Input(job, call) with { ResolveHash = "sha256:" + new string('1', 64) }, CancellationToken.None);
        var noEntry = await activity.RunAsync(Input(job, call with { Key = "node.letter", EntryKey = "node.letter" }), CancellationToken.None);
        var noRule = await activity.RunAsync(Input(job, call with { Member = "until" }), CancellationToken.None);

        foreach (var outcome in new[] { otherHash, noEntry, noRule })
            Assert.Equal(new RuleOutcome(null, RuleTurn.PlanFailure, null), outcome);
    }

    // ---- fixtures -----------------------------------------------------------------------------

    /// <summary>
    /// The activity over a published package: <see cref="SourceFixture.Minimal"/> whose plan is gated on a pattern, with the
    /// computed <c>named</c>, an object whose <c>named</c> is the same pattern, and the computed <c>slow</c>, a pattern that
    /// backtracks on a long run of one letter.
    /// </summary>
    private static (EvaluateRuleV31Activity Activity, CapturingLogger<EvaluateRuleV31Activity> Log, EvaluateRuleV31Input Job) Activity()
    {
        var golden = new CompiledGoldenTests.GoldenCase("pattern-rules", () => SourceFixture.Minimal()
            .WithJson("nodes/plan.md", h => h["if"] = JsonNode.Parse(Pattern))
            .With("nodes/index.json", SourceFixture.Index(["plan", "named", "slow"]))
            .With("nodes/slow.md", SourceFixture.Md(SourceFixture.Node("computed", "Testing the draft slowly", "\"boolean\"", true, false,
                "\"if\": null, \"expression\": { \"matches\": [{ \"var\": \"input.consult_draft.content\" }, \"(.*a.{100}){3}b\"] }"), ""))
            .With("nodes/named.md", SourceFixture.Md(SourceFixture.Node("computed", "Naming the patient",
                "{ \"type\": \"object\", \"properties\": { \"named\": { \"type\": \"boolean\" } }, \"required\": [\"named\"], \"additionalProperties\": false }",
                true, false, $"\"if\": null, \"expression\": {{ \"object\": {{ \"named\": {Pattern} }} }}"), "")),
            "the minimal note, its plan gated on a pattern, with a computed object holding one");
        var registry = new FakeRegistry();
        var (packageRef, pair) = registry.Put(golden);
        var log = new CapturingLogger<EvaluateRuleV31Activity>();
        var job = new EvaluateRuleV31Input(packageRef.Name, packageRef.Version, pair.ResolveHash, 2000, new RuleCall("", "", null, "", []));
        return (new EvaluateRuleV31Activity(V31Job.StoreOver(registry), log), log, job);
    }

    /// <summary>The job's input for <paramref name="call"/>.</summary>
    private static EvaluateRuleV31Input Input(EvaluateRuleV31Input job, RuleCall call) => job with { Call = call };

    private static JsonObject Scope(string draft) =>
        new() { ["input"] = new JsonObject { ["consult_draft"] = new JsonObject { ["content"] = draft } } };
}
