using System.Text;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Run;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Harness;
using Consultologist.PackageFormat.V31.Publish;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.Tests.V31Publish;

/// <summary>
/// #1508 (specVersion 31, area 11, Task 13; part 07 §§ 8.2 to 8.4, 9; D6, D7, D9; Review Focus 3 and 5): a package's in-package
/// tests run offline (<see cref="PackageTests.RunAsync"/>) against the compiled nested-note package, whose passing test is the
/// migration snapshot's shape written from its own run (<see cref="SnapshotTest.Write"/>). Content is fictional (Margaret
/// Okafor, Dr A. Sample); the stub text a test must not leak is a made-up string in no sentence.
/// </summary>
public class PackageTestsTests
{
    private const string Secret = "Margaret Okafor SECRET-STUB-TEXT";

    private static readonly RunPlan Plan = RunFixture.Plan(ResolveFixture.NestedNote());

    /// <summary>A passing test file for <see cref="Plan"/>: its inputs, replies and hashes from one offline run.</summary>
    private static async Task<JsonObject> PassingFile()
    {
        var request = SnapshotTest.Fictional(Plan);
        var result = await OfflineRun.RunAsync(Plan, request);
        return SnapshotTest.Write(Plan, request, result);
    }

    private static string Path(string id) => $"tests/{id}.json";

    private static (SourceTest Test, string Text) Test(string id, JsonObject json) =>
        (new SourceTest(id, Path(id), json), json.ToJsonString());

    private static Task<TestsOutcome> RunAsync(TimeProvider? clock = null, params (SourceTest Test, string Text)[] tests) =>
        PackageTests.RunAsync(
            Plan,
            tests.Select(t => t.Test).ToList(),
            tests.ToDictionary(t => t.Test.Path, t => t.Text, StringComparer.Ordinal),
            clock ?? TimeProvider.System);

    private static void AssertSentences(TestsOutcome outcome)
    {
        foreach (var refusal in outcome.Refusals.Concat(outcome.Warnings))
        {
            V31Contract.AssertSentence(refusal);
            Assert.DoesNotContain("SECRET-STUB-TEXT", refusal.Sentence, StringComparison.Ordinal);
            Assert.DoesNotContain("Okafor", refusal.Sentence, StringComparison.Ordinal);
        }
    }

    private static string FirstOutput(JsonObject file) => file["outputs"]!.AsObject().First().Key;

    /// <summary>A clock that moves <paramref name="step"/> on every reading.</summary>
    private sealed class SteppingClock(TimeSpan step) : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Add(ref _ticks, step.Ticks);
    }

    // ---- passing and failing ---------------------------------------------------------------------------------------

    [Fact]
    public async Task APassingTest_Passes_WithNoRefusalOrWarning_AndEveryMemberOfItsResult()
    {
        var outcome = await RunAsync(null, Test("snapshot", await PassingFile()));

        var result = Assert.Single(outcome.Results);
        Assert.True(result.Pass);
        Assert.Null(result.FirstMismatch);
        Assert.Empty(outcome.Refusals);
        Assert.Empty(outcome.Warnings);
        var json = result.ToJson();
        Assert.Equal(["id", "fileHash", "result", "outcomeHash", "firstMismatch", "durationMs"], json.Select(p => p.Key));
        Assert.Equal("snapshot", (string?)json["id"]);
        Assert.Equal("pass", (string?)json["result"]);
        Assert.Null(json["firstMismatch"]);
        Assert.Matches("^sha256:[0-9a-f]{64}$", (string?)json["fileHash"]);
        Assert.Matches("^sha256:[0-9a-f]{64}$", (string?)json["outcomeHash"]);
        Assert.True((long?)json["durationMs"] >= 0);
    }

    [Fact]
    public async Task AFailingTest_SaysS07007_WithTheMismatch_WordForWord()
    {
        var file = await PassingFile();
        file["expect"]!.AsObject()["ran"] = new JsonArray("node.nope");

        var outcome = await RunAsync(null, Test("failing", file));

        var result = Assert.Single(outcome.Results);
        Assert.False(result.Pass);
        Assert.Equal(new TestMismatch("ran", "node.nope", "ran", "not run"), result.FirstMismatch);
        var refusal = Assert.Single(outcome.Refusals);
        Assert.Equal("S07-007", refusal.Id);
        Assert.Equal("Test 'failing' failed: ran for node.nope expected ran, got not run. Publish stopped.", refusal.Sentence);
        Assert.Equal(Path("failing"), refusal.Path);
        Assert.Equal("fail", (string?)result.ToJson()["result"]);
        Assert.NotNull(result.ToJson()["firstMismatch"]);
        AssertSentences(outcome);
    }

    [Fact]
    public async Task APromptsMismatch_ShowsHashesOnly_NeverTheTextOfThePrompt()
    {
        var file = await PassingFile();
        var key = file["expect"]!["prompts"]!.AsObject().First().Key;
        file["expect"]!["prompts"]![key] = new JsonObject { ["text"] = Secret };

        var outcome = await RunAsync(null, Test("prompt", file));

        var refusal = Assert.Single(outcome.Refusals);
        Assert.Equal("S07-007", refusal.Id);
        Assert.Contains("sha256:", refusal.Sentence, StringComparison.Ordinal);
        AssertSentences(outcome);
        Assert.DoesNotContain("SECRET-STUB-TEXT", Assert.Single(outcome.Results).ToJson().ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryFailingTest_IsSaid_AndResultsAreInIdOrder()
    {
        var bad = await PassingFile();
        bad["expect"]!.AsObject()["ran"] = new JsonArray("node.nope");
        var worse = await PassingFile();
        worse["expect"]!.AsObject()["skipped"] = new JsonArray("node.plan");

        var outcome = await RunAsync(null, Test("c_pass", await PassingFile()), Test("b_worse", worse), Test("a_bad", bad));

        Assert.Equal(["a_bad", "b_worse", "c_pass"], outcome.Results.Select(r => r.Id));
        Assert.Equal([false, false, true], outcome.Results.Select(r => r.Pass));
        Assert.Equal(["a_bad", "b_worse"], outcome.Refusals.Select(r => r.Path[6..^5]));
        Assert.All(outcome.Refusals, r => Assert.Equal("S07-007", r.Id));
        AssertSentences(outcome);
    }

    // ---- stubs -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AMissingStub_SaysS07008_ForTheFirstMissingKey_InPlaceOfS07007()
    {
        var file = await PassingFile();
        var key = FirstOutput(file);
        file["outputs"]!.AsObject().Remove(key);

        var outcome = await RunAsync(null, Test("no_stub", file));

        Assert.False(Assert.Single(outcome.Results).Pass);
        Assert.Equal(new TestMismatch("run", "", "completed", "failed"), Assert.Single(outcome.Results).FirstMismatch);
        var refusal = Assert.Single(outcome.Refusals);
        Assert.Equal("S07-008", refusal.Id);
        Assert.Equal($"Test 'no_stub' has no stub for {key}, which runs. Add it to outputs.", refusal.Sentence);
        AssertSentences(outcome);
    }

    [Fact]
    public async Task AnInvalidStub_SaysS07009_WithAPathAndAKeyword_NeverTheValue()
    {
        var file = await PassingFile();
        var key = FirstOutput(file);
        file["outputs"]![key] = new JsonObject { ["sections"] = Secret };

        var outcome = await RunAsync(null, Test("bad_stub", file));

        Assert.False(Assert.Single(outcome.Results).Pass);
        var refusal = Assert.Single(outcome.Refusals);
        Assert.Equal("S07-009", refusal.Id);
        Assert.Equal("Test 'bad_stub' stubs node.plan with a value that isn't section_plan: /sections type.", refusal.Sentence);
        Assert.Equal(new TestMismatch("run", "", "completed", "failed"), Assert.Single(outcome.Results).FirstMismatch);
        AssertSentences(outcome);
    }

    [Fact]
    public async Task AnUnusedStub_IsAWarningS07014_AndTheTestStillPasses()
    {
        var file = await PassingFile();
        file["outputs"]!.AsObject()["node.extra"] = new JsonObject { ["sections"] = new JsonArray() };

        var outcome = await RunAsync(null, Test("extra", file));

        Assert.True(Assert.Single(outcome.Results).Pass);
        Assert.Empty(outcome.Refusals);
        var warning = Assert.Single(outcome.Warnings);
        Assert.Equal("S07-014", warning.Id);
        Assert.Equal("Test 'extra' stubs node.extra, which doesn't run.", warning.Sentence);
        AssertSentences(outcome);
    }

    // ---- a test that cannot be read --------------------------------------------------------------------------------

    [Fact]
    public async Task ATestThatCannotBeRead_Fails_WithTheEmptyOutcomeHash_AndItsRefusals()
    {
        var file = await PassingFile();
        file["inputs"] = new JsonObject();
        var hostile = await PassingFile();
        hostile["expect"] = new JsonArray();

        var outcome = await RunAsync(null, Test("unread", file), Test("hostile", hostile), Test("snapshot", await PassingFile()));

        Assert.Equal(["hostile", "snapshot", "unread"], outcome.Results.Select(r => r.Id));
        var unread = outcome.Results.Single(r => r.Id == "unread");
        var hostileResult = outcome.Results.Single(r => r.Id == "hostile");
        Assert.False(unread.Pass);
        Assert.Null(unread.FirstMismatch);
        Assert.Equal(unread.OutcomeHash, hostileResult.OutcomeHash);
        Assert.NotEqual(unread.OutcomeHash, outcome.Results.Single(r => r.Id == "snapshot").OutcomeHash);
        Assert.Contains(outcome.Refusals, r => r.Id == "S07-010" && r.Path == Path("unread"));
        Assert.Contains(outcome.Refusals, r => r.Id == "A11-005" && r.Path == Path("hostile"));
        AssertSentences(outcome);
    }

    // ---- the 60 seconds --------------------------------------------------------------------------------------------

    [Fact]
    public void TheLimit_IsThePinnedConstant_AndTheSentenceSaysIt()
    {
        var pinned = (int)V31Contract.Json("constants.json")["limits"]!["testsSeconds"]!;

        Assert.Equal(PackageTests.LimitSeconds, pinned);
        Assert.Equal($"Tests took longer than {pinned} seconds in all. Publish stopped.", PackagePublishSentences.TestsTookTooLong().Sentence);
        V31Contract.AssertSentence(PackagePublishSentences.TestsTookTooLong());
    }

    [Fact]
    public async Task PastTheLimit_PublishStopsWithS07013Alone_AndLaterTestsAreNotRun()
    {
        var failing = await PassingFile();
        failing["expect"]!.AsObject()["ran"] = new JsonArray("node.nope");
        // Each reading of the clock is 25 s: the first test starts at 50 s of its 60 and the third is not begun.
        var clock = new SteppingClock(TimeSpan.FromSeconds(25));

        var outcome = await RunAsync(clock, Test("a", failing), Test("b", await PassingFile()), Test("c", await PassingFile()));

        var refusal = Assert.Single(outcome.Refusals);
        Assert.Equal("S07-013", refusal.Id);
        Assert.Equal("", refusal.Path);
        Assert.Equal("a", Assert.Single(outcome.Results).Id);
        Assert.DoesNotContain(outcome.Refusals, r => r.Id == "S07-007");
        V31Contract.AssertSentence(refusal);
    }

    [Fact]
    public async Task WithinTheLimit_NoTimeSentenceIsSaid()
    {
        var clock = new SteppingClock(TimeSpan.FromMilliseconds(1));

        var outcome = await RunAsync(clock, Test("a", await PassingFile()), Test("b", await PassingFile()));

        Assert.Equal(2, outcome.Results.Count);
        Assert.Empty(outcome.Refusals);
    }

    /// <summary>A clock whose timers fire once <see cref="Expire"/> has been called, whether armed before or after.</summary>
    private sealed class ExpiringClock : TimeProvider
    {
        private readonly object _gate = new();
        private readonly List<(TimerCallback Callback, object? State)> _armed = [];
        private bool _expired;

        public void Expire()
        {
            List<(TimerCallback Callback, object? State)> due;
            lock (_gate)
            {
                _expired = true;
                due = [.. _armed];
            }

            foreach (var (callback, state) in due)
                ThreadPool.QueueUserWorkItem(_ => callback(state));
        }

        private bool Arm(TimerCallback callback, object? state)
        {
            lock (_gate)
            {
                if (!_expired)
                {
                    _armed.Add((callback, state));
                    return false;
                }
            }

            return true;
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            // The wait arms its timer with its due time here; an armed timer fires on the pool once the clock expires.
            if (dueTime != Timeout.InfiniteTimeSpan && Arm(callback, state))
                ThreadPool.QueueUserWorkItem(_ => callback(state));
            return new Timer(this, callback, state);
        }

        private sealed class Timer(ExpiringClock clock, TimerCallback callback, object? state) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (dueTime != Timeout.InfiniteTimeSpan && clock.Arm(callback, state))
                    ThreadPool.QueueUserWorkItem(_ => callback(state));
                return true;
            }

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task ARunThatNeverEnds_IsAbandonedAtTheLimit_WithS07013Alone_AndTheResultsSoFarKept()
    {
        var tests = new[] { Test("a", await PassingFile()), Test("b", await PassingFile()), Test("c", await PassingFile()) };
        var clock = new ExpiringClock();
        var calls = 0;
        Task<OfflineResult> Run(RunPlan plan, OfflineRequest request)
        {
            if (Interlocked.Increment(ref calls) == 1)
                return OfflineRun.RunAsync(plan, request);
            // The second run never ends; the clock passes the remaining time while it is waited on.
            clock.Expire();
            return new TaskCompletionSource<OfflineResult>().Task;
        }

        var outcome = await PackageTests.RunAsync(
            Plan,
            tests.Select(t => t.Test).ToList(),
            tests.ToDictionary(t => t.Test.Path, t => t.Text, StringComparer.Ordinal),
            clock,
            null,
            Run);

        Assert.Equal(2, calls);
        Assert.Equal("a", Assert.Single(outcome.Results).Id);
        var refusal = Assert.Single(outcome.Refusals);
        Assert.Equal("S07-013", refusal.Id);
        Assert.Equal("", refusal.Path);
    }

    [Fact]
    public async Task ATestWhoseFileIsNotInTheFiles_IsReadFromItsJson_AndItsFileHashIsThatOfEmptyText()
    {
        var test = Test("absent", await PassingFile()).Test;

        var outcome = await PackageTests.RunAsync(Plan, [test], new Dictionary<string, string>(StringComparer.Ordinal), TimeProvider.System);

        var result = Assert.Single(outcome.Results);
        Assert.True(result.Pass);
        Assert.Equal(CanonicalJson.HashText(""), result.FileHash);
        Assert.Empty(outcome.Refusals);
    }

    // ---- area 10's snapshot ----------------------------------------------------------------------------------------

    [Fact]
    public async Task AMigrationVectorsSnapshot_PassesOnItsOwnPackage()
    {
        var folder = System.IO.Path.Combine(V31Contract.ConformanceFolder("migration-vectors"), "example-conditional-sections", "expected");
        Assert.True(Directory.Exists(folder), $"The migration vector {folder} is missing.");
        var upload = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(
                f => System.IO.Path.GetRelativePath(folder, f).Replace('\\', '/'),
                f => File.ReadAllBytes(f),
                StringComparer.Ordinal);
        Assert.Contains("tests/migration_snapshot.json", upload.Keys);
        var plan = RunFixture.Plan(upload);
        var text = Encoding.UTF8.GetString(upload["tests/migration_snapshot.json"]);
        var test = new SourceTest(SnapshotTest.Id, SnapshotTest.File, JsonNode.Parse(text)!.AsObject());

        var outcome = await PackageTests.RunAsync(plan, [test], new Dictionary<string, string>(StringComparer.Ordinal) { [SnapshotTest.File] = text }, TimeProvider.System);

        Assert.True(Assert.Single(outcome.Results).Pass, string.Join("\n", outcome.Refusals.Select(r => r.Sentence)));
        Assert.Empty(outcome.Refusals);
        Assert.Empty(outcome.Warnings);
    }
}
