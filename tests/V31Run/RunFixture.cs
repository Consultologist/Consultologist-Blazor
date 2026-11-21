using Consultologist.Api.Tests.V31Resolve;
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
    /// <summary>The plan of one of the three goldens.</summary>
    public static RunPlan Plan(CompiledGoldenTests.GoldenCase golden, TimeSpan? matchTimeLimit = null) =>
        Plan(golden.Source(), golden.Reads(), matchTimeLimit);

    /// <summary>The plan of the golden named <paramref name="name"/>.</summary>
    public static RunPlan Plan(string name, TimeSpan? matchTimeLimit = null) =>
        Plan(Golden(name), matchTimeLimit);

    /// <summary>The golden case named <paramref name="name"/>.</summary>
    public static CompiledGoldenTests.GoldenCase Golden(string name) =>
        CompiledGoldenTests.Cases().Single(c => c.Name == name);

    /// <summary>The plan of <paramref name="files"/>, whose bodies read what <paramref name="reads"/> says.</summary>
    public static RunPlan Plan(Dictionary<string, byte[]> files, ITemplateReads reads, TimeSpan? matchTimeLimit = null)
    {
        var (refusals, compiled) = CompiledPackage.Publish(files, ResolveFixture.Published(), CompiledGoldenTests.Fixed, templates: reads);
        Assert.True(refusals.Count == 0, string.Join(" | ", refusals.Select(r => $"{r.Id} {r.Path} {r.Sentence}")));
        Assert.NotNull(compiled);

        var (plan, refusal) = RunPlan.Build(compiled!.Resolve, compiled.ResolveHash, compiled.LockHash, matchTimeLimit);
        Assert.True(refusal == null, refusal?.Sentence);
        Assert.NotNull(plan);
        return plan!;
    }
}
