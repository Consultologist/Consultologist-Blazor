using System.Text;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 § 3.2; Review Focus 3): whole runs of the three compiled goldens, pinned. Each
/// golden's plan, run on <see cref="RunFixture.GoldenInputs"/> with part 10 § 3.1's stubs seeded by each row key
/// (<see cref="RunFixture.StubReply"/>) answered at once, gives exactly <c>Golden/&lt;name&gt;.run.txt</c>
/// (<see cref="RunTrace"/>); with <c>GOLDEN_UPDATE_V31=1</c> the test rewrites it instead, to be read before it is committed.
/// And a run does not depend on the order its replies come back in: what it settles is the same, and every dispatch is in
/// the fixed order among the calls waiting then (<see cref="DispatchOracle"/>).
/// </summary>
public class RunGoldenTests
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private static readonly int[] Seeds = [1, 31, 1503, 2026, 4242];

    /// <summary>The folder of the run goldens in the source tree (<c>tests/V31Run/Golden</c>).</summary>
    internal static string GoldenFolder() =>
        Path.GetFullPath(Path.Combine(CompiledGoldenTests.GoldenFolder(), "..", "..", "V31Run", "Golden"));

    [Theory]
    [MemberData(nameof(CompiledGoldenTests.Names), MemberType = typeof(CompiledGoldenTests))]
    public async Task AGoldenRun_IsWhatTheRunnerGives(string name)
    {
        var plan = RunFixture.Plan(name);
        var run = await Run(plan, new RunLimits(), seed: null);
        Assert.True(run.Result.State == RunState.Completed, $"{run.Result.State}: {run.Result.FailedKey} {run.Result.Sentence}");

        // every stub reply is a value of its entry's format, so no call was repaired or retried
        foreach (var call in run.Host.Calls)
        {
            var format = (call.Function == null ? plan.Entries : plan.Functions[call.Function].Entries)[call.EntryKey].Format;
            var (value, parsed) = plan.Formats.Read(RunFixture.StubReply(plan, call), format);
            Assert.True(parsed && plan.Formats.Validate(value, format).Valid, $"{call.Key}: the stub is not a value of its format.");
            Assert.Equal(1, call.Attempt);
        }

        var text = RunTrace.Of(run.Host, run.Recorder, run.Result);
        var path = Path.Combine(GoldenFolder(), $"{name}.run.txt");

        if (Environment.GetEnvironmentVariable("GOLDEN_UPDATE_V31") == "1")
        {
            Directory.CreateDirectory(GoldenFolder());
            File.WriteAllText(path, text, Utf8NoBom);
        }

        Assert.True(File.Exists(path), $"{name}.run.txt is missing: run with GOLDEN_UPDATE_V31=1, then read it.");
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.AsSpan().SequenceEqual(Utf8NoBom.GetBytes(text)), $"{name}.run.txt is not what the runner gives.");
        Assert.DoesNotContain((byte)'\r', bytes);
    }

    [Fact]
    public void TheGoldenFolder_HoldsARunPerCompiledGolden()
    {
        Assert.Equal(
            CompiledGoldenTests.Cases().Select(c => $"{c.Name}.run.txt").Order(StringComparer.Ordinal),
            Directory.GetFiles(GoldenFolder(), "*.run.txt").Select(f => Path.GetFileName(f)!).Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(CompiledGoldenTests.Names), MemberType = typeof(CompiledGoldenTests))]
    public async Task ARun_IsTheSame_WhateverOrderRepliesArriveIn(string name)
    {
        var plan = RunFixture.Plan(name);
        foreach (var ceiling in new[] { 1, 16 })
        {
            var limits = new RunLimits(MaxAgentCalls: ceiling);
            var auto = await Run(plan, limits, seed: null);
            Assert.Equal(RunState.Completed, auto.Result.State);
            DispatchOracle.Holds(plan, auto.Host.Calls, auto.Recorder, auto.Result);

            foreach (var seed in Seeds)
            {
                var shuffled = await Run(plan, limits, seed);
                var again = await Run(plan, limits, seed);
                // what it settles: the same rows, decisions and result, whatever order the answers came in
                Assert.Equal(Settled(auto), Settled(shuffled));
                Assert.Equal(Result(auto), Result(shuffled));
                Assert.InRange(shuffled.Host.InFlightPeak, 1, ceiling);

                // each dispatch the first waiting call in the fixed order; a replay with the same order the same dispatches
                DispatchOracle.Holds(plan, shuffled.Host.Calls, shuffled.Recorder, shuffled.Result);
                Assert.Equal(shuffled.Host.Dispatched, again.Host.Dispatched);
                Assert.Equal(RunTrace.Of(shuffled.Host, shuffled.Recorder, shuffled.Result), RunTrace.Of(again.Host, again.Recorder, again.Result));

                // at a ceiling of 1 one agent call is in flight at a time, and no agent in these plans waits on a render (which
                // takes no slot): so the calls go out, and everything but a render settles, in the auto run's order; only
                // where a render's row falls among the others depends on when its answer came
                if (ceiling == 1)
                {
                    Assert.Equal(auto.Host.Dispatched, shuffled.Host.Dispatched);
                    Assert.Equal(Unrendered(auto), Unrendered(shuffled));
                    Assert.Equal(auto.Recorder.Decisions.Select(RunTrace.Decision), shuffled.Recorder.Decisions.Select(RunTrace.Decision));
                }
            }
        }
    }

    /// <summary>A run, its host and its recorder.</summary>
    internal sealed record Traced(FakeRunHost Host, ListRecorder Recorder, RunResult Result);

    /// <summary>
    /// Runs <paramref name="plan"/> on the golden inputs with the stubbed host: answered at once when <paramref name="seed"/> is
    /// null, else every answer held and released in the order a <see cref="Random"/> of that seed chooses. Each row is
    /// stamped with the agent calls made before it.
    /// </summary>
    internal static async Task<Traced> Run(RunPlan plan, RunLimits limits, int? seed)
    {
        var host = RunFixture.Stubbed(plan, auto: seed == null);
        var recorder = new ListRecorder(() => host.Calls.Count);
        var runner = new PlanRunner(plan, limits, host, recorder);
        var result = seed is { } chosen
            ? await FakeRunHost.Drive(() =>
            {
                var run = runner.RunAsync(RunFixture.GoldenInputs(), FakeRunHost.Start);
                host.ReleaseInOrder(new Random(chosen));
                return run;
            })
            : await runner.RunAsync(RunFixture.GoldenInputs(), FakeRunHost.Start);
        return new Traced(host, recorder, result);
    }

    /// <summary>The rows and decisions, each sorted: what was settled, not the order it settled in.</summary>
    private static string Settled(Traced run) =>
        string.Join("\n", run.Recorder.Rows.Select(RunTrace.Row).Order(StringComparer.Ordinal))
        + "\n--\n"
        + string.Join("\n", run.Recorder.Decisions.Select(RunTrace.Decision).Order(StringComparer.Ordinal));

    /// <summary>The rows but a render's (a rendered node's or a deliverable's), in the order recorded.</summary>
    private static IEnumerable<string> Unrendered(Traced run) =>
        run.Recorder.Rows.Where(r => r.Kind is not (PlanKind.Rendered or PlanKind.Deliverable)).Select(RunTrace.Row);

    /// <summary>The trace's result part.</summary>
    private static string Result(Traced run)
    {
        var text = RunTrace.Of(run.Host, run.Recorder, run.Result);
        return text[text.IndexOf("# result", StringComparison.Ordinal)..].Replace(
            $"peakInFlight | {run.Result.PeakInFlight}\n", "", StringComparison.Ordinal);
    }
}
