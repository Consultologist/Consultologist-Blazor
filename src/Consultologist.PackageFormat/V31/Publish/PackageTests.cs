using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Harness;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.PackageFormat.V31.Publish;

/// <summary>
/// #1508 (specVersion 31, area 11; part 07 § 9): one test's result as the publish report gives it. <paramref name="FileHash"/>
/// and <paramref name="OutcomeHash"/> are <c>sha256:</c> hashes; <paramref name="FirstMismatch"/> is null when the test passed
/// (and for a test that could not be read, or that stopped on a stub: its refusal says why).
/// </summary>
public sealed record TestResult(string Id, string FileHash, bool Pass, string OutcomeHash, TestMismatch? FirstMismatch, long DurationMs)
{
    /// <summary>Part 07 § 9's test result: every member present, <c>result</c> <c>"pass"</c> or <c>"fail"</c>.</summary>
    public JsonObject ToJson() => new()
    {
        ["id"] = Id,
        ["fileHash"] = FileHash,
        ["result"] = Pass ? "pass" : "fail",
        ["outcomeHash"] = OutcomeHash,
        ["firstMismatch"] = FirstMismatch is { } m
            ? new JsonObject { ["expectation"] = m.Expectation, ["key"] = m.Key, ["expected"] = m.Expected, ["actual"] = m.Actual }
            : null,
        ["durationMs"] = DurationMs,
    };
}

/// <summary>What running a package's tests gave: the results (id order), the refusals that stop publish, and the warnings that do not (S07-014).</summary>
public sealed record TestsOutcome(IReadOnlyList<TestResult> Results, IReadOnlyList<SourceRefusal> Refusals, IReadOnlyList<SourceRefusal> Warnings);

/// <summary>
/// #1508 (area 11; part 07 §§ 8.2 to 8.4; D6, D7, D9): runs the in-package tests offline against the compiled package, one at a
/// time in id order, with no stub generated. A test that cannot be read fails with its refusals; a missing stub (S07-008), an
/// invalid one (S07-009) or a first mismatch (S07-007) is said in place of the others; a stub that is never used is a warning
/// (S07-014). All tests together have <see cref="LimitSeconds"/>: the clock is read between tests and bounds a single test; past
/// it the outcome carries S07-013 alone and the tests run so far.
/// </summary>
public static class PackageTests
{
    /// <summary><c>constants.json</c>'s <c>limits.testsSeconds</c> (a test pins it).</summary>
    public const int LimitSeconds = 60;

    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(LimitSeconds);

    /// <summary>The empty outcome's hash, for a test that could not be read (D8).</summary>
    private static readonly string EmptyOutcomeHash = TestOutcome.Hash(new OfflineResult(null, null, [], [], [], [], new Dictionary<string, JsonNode?>(), [], [], []));

    public static async Task<TestsOutcome> RunAsync(
        RunPlan plan,
        IReadOnlyList<SourceTest> tests,
        IReadOnlyDictionary<string, string> files,
        TimeProvider clock,
        RunLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(tests);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(clock);

        var results = new List<TestResult>();
        var refusals = new List<SourceRefusal>();
        var warnings = new List<SourceRefusal>();
        var started = clock.GetTimestamp();
        TestsOutcome TookTooLong() => new(results, [PackagePublishSentences.TestsTookTooLong()], warnings);

        foreach (var test in tests.OrderBy(t => t.Id, StringComparer.Ordinal))
        {
            var remaining = Limit - clock.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero)
                return TookTooLong();

            var testStarted = clock.GetTimestamp();
            long Elapsed() => (long)clock.GetElapsedTime(testStarted).TotalMilliseconds;

            files.TryGetValue(test.Path, out var text);
            var (read, readRefusals) = PackageTestFiles.Read(test, text ?? "", plan);
            if (read == null)
            {
                refusals.AddRange(readRefusals);
                results.Add(new TestResult(test.Id, CanonicalJson.HashText((text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal)), false, EmptyOutcomeHash, null, Elapsed()));
                continue;
            }

            OfflineResult run;
            try
            {
                var request = limits == null ? read.Request : read.Request with { Limits = limits };
                var task = OfflineRun.RunAsync(plan, request);
                // The run is CPU-bound and bounded by RunLimits and has no cancellation: a runaway one is abandoned at the limit.
                _ = task.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                run = await task.WaitAsync(remaining, clock);
            }
            catch (TimeoutException)
            {
                return TookTooLong();
            }
            catch (Exception fault)
            {
                return new TestsOutcome(results, [PackagePublishSentences.EngineFailed(fault.GetType().Name)], warnings);
            }

            foreach (var key in run.UnusedOutputs.Order(StringComparer.Ordinal))
                warnings.Add(PackagePublishSentences.StubNotUsed(test.Id, key, test.Path));

            var mismatch = TestExpectations.First(plan, read.Expect, run);
            var missing = run.MissingOutputs.Order(StringComparer.Ordinal).FirstOrDefault();
            var invalid = run.InvalidOutputs.OrderBy(f => f.Key, StringComparer.Ordinal).FirstOrDefault();
            if (missing != null)
                refusals.Add(PackagePublishSentences.NoStub(test.Id, missing, test.Path));
            else if (invalid != null)
                refusals.Add(PackagePublishSentences.StubInvalid(test.Id, invalid.Key, invalid.Format, invalid.Error, test.Path));
            else if (mismatch != null)
                refusals.Add(PackagePublishSentences.TestFailed(test.Id, mismatch.Expectation, mismatch.Key, mismatch.Expected, mismatch.Actual, test.Path));

            var pass = mismatch == null && missing == null && invalid == null;
            results.Add(new TestResult(test.Id, read.FileHash, pass, TestOutcome.Hash(run), mismatch, Elapsed()));
        }

        return new TestsOutcome(results, refusals, warnings);
    }
}
