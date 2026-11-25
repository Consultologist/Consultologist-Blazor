using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1503 (specVersion 31, area 06): the plans the run's tests read. A plan is built as the engine builds it: the package
/// published with <see cref="CompiledGoldenTests.Fixed"/> (<see cref="CompiledPackage.Publish"/>), then
/// <see cref="RunPlan.Build"/> on the compiled document and its two hashes, both asserted to succeed.
/// Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
internal static class RunFixture
{
    /// <summary>The plan of one of the compiled goldens.</summary>
    public static RunPlan Plan(CompiledGoldenTests.GoldenCase golden, TimeSpan? matchTimeLimit = null) =>
        Plan(golden.Source(), null, matchTimeLimit, golden.Limits);

    /// <summary>The plan of the golden named <paramref name="name"/>.</summary>
    public static RunPlan Plan(string name, TimeSpan? matchTimeLimit = null) =>
        Plan(Golden(name), matchTimeLimit);

    /// <summary>The golden case named <paramref name="name"/>.</summary>
    public static CompiledGoldenTests.GoldenCase Golden(string name) =>
        CompiledGoldenTests.Cases().Single(c => c.Name == name);

    /// <summary>
    /// The plan of <paramref name="files"/>, whose bodies read what <paramref name="reads"/> says, or (none) what the
    /// dialect says (#1504, D18), compiled against <paramref name="limits"/> (#1508: a golden's own, so its plan is its golden's).
    /// </summary>
    public static RunPlan Plan(Dictionary<string, byte[]> files, ITemplateReads? reads = null, TimeSpan? matchTimeLimit = null, PublishLimits? limits = null)
    {
        var (refusals, compiled) = CompiledPackage.Publish(files, ResolveFixture.Published(), CompiledGoldenTests.Fixed, limits, templates: reads);
        Assert.True(refusals.Count == 0, string.Join(" | ", refusals.Select(r => $"{r.Id} {r.Path} {r.Sentence}")));
        Assert.NotNull(compiled);

        var (plan, refusal) = RunPlan.Build(compiled!.Resolve, compiled.ResolveHash, compiled.LockHash, matchTimeLimit);
        Assert.True(refusal == null, refusal?.Sentence);
        Assert.NotNull(plan);
        return plan!;
    }

    /// <summary>The inputs every golden needs: the consult draft, three sections (a narrative, a table, one omitted), and billing.</summary>
    public static RunInputs GoldenInputs()
    {
        var given = PlanRunnerLeavesTests.Inputs(includeBilling: true);
        return given with
        {
            Inputs = new Dictionary<string, JsonNode?>(given.Inputs, StringComparer.Ordinal)
            {
                ["sections"] = new JsonObject
                {
                    ["value"] = JsonNode.Parse("""[{"title":"History","kind":"narrative"},{"title":"Results","kind":"table"},{"title":"Coding","kind":"omit"}]"""),
                    ["content"] = null,
                    ["title"] = null,
                },
            },
        };
    }

    /// <summary>
    /// A host whose every agent of <paramref name="plan"/> (the package's and its functions') replies <see cref="StubReply"/>:
    /// answered at once when <paramref name="auto"/>, else held until released.
    /// </summary>
    public static FakeRunHost Stubbed(RunPlan plan, bool auto = true) =>
        new FakeRunHost(auto, plan).Stub(call => StubReply(plan, call));

    /// <summary>
    /// Part 10 § 3.1's deterministic stub for <paramref name="call"/> (<see cref="WorkflowStubOutputs"/>), seeded by the
    /// instance's row key: for a text format <c>Stub reply for &lt;key&gt; (&lt;hex&gt;).</c>, else a value of the entry's format
    /// made from its schema alone, as canonical JSON. So a reply holds no text but the key's.
    /// </summary>
    public static string StubReply(RunPlan plan, AgentCall call)
    {
        var entries = call.Function == null ? plan.Entries : plan.Functions[call.Function].Entries;
        var format = entries[call.EntryKey].Format;
        return plan.Formats.IsText(format)
            ? WorkflowStubOutputs.Text(call.Key)
            : CanonicalJson.Write(WorkflowStubOutputs.FromSchema(plan.Formats.Expand(format), call.Key));
    }
}
