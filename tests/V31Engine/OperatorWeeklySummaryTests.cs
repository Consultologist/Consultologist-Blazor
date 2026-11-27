using System.Reflection;
using Consultologist.Api.Agents;
using Consultologist.Api.Auth;
using Consultologist.Api.Email;
using Consultologist.Api.Jobs;
using Consultologist.Api.Tests.ByteEquivalence;
using Consultologist.Api.Tests.V31Source;
using Consultologist.Api.V31;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PublishedTypeRegistry = Consultologist.Api.V31.PublishedTypeRegistry;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1505 (specVersion 31, area 08; part 09 § 3.9; D14): the weekly summary: the week's jobs, its failures, the p50 and p90 of
/// their durations (nearest rank), and the two lines areas 09 and 10 fill. It runs on <c>%OperatorWeeklySummarySchedule%</c>,
/// off when that is unset (as <see cref="TextRetentionSweep"/>'s timer is), and reads nothing unless the engine runs 31.
/// #1507 (area 10; R11, D22): the hand-edits line counts what <see cref="WorkflowPackageMigration30To31.Migrate"/> gives
/// every account's active package below 31, computed by the summary itself; a figure it cannot read is an error line
/// (A10-058), never a zero.
/// </summary>
public class OperatorWeeklySummaryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheSummary_CountsAndRanks()
    {
        var jobs = Enumerable.Range(1, 10)
            .Select(minutes => (Status: minutes is 3 or 7 ? ConsultGenerationJobStatuses.Failed : ConsultGenerationJobStatuses.Completed,
                Duration: (TimeSpan?)TimeSpan.FromMinutes(minutes)))
            .Reverse()
            .ToList();

        var (subject, body) = OperatorSummary.Compose(jobs, MigrationFigures.Counted(3, 2, 1));

        Assert.Equal("Consultologist weekly summary", subject);
        Assert.Equal(
            "Jobs: 10\n" +
            "Failures: 2\n" +
            "p50 duration: 5 minutes\n" +
            "p90 duration: 9 minutes\n" +
            "Largest record: none\n" +
            "Outstanding migration hand edits: 3 in 2 packages; 1 packages do not migrate\n",
            body);

        // a job with no duration (it never started) counts, and is not ranked; no durations at all read none
        var none = MigrationFigures.Counted(0, 0, 0);
        Assert.Contains("p50 duration: 1 minute\n", OperatorSummary.Compose([("Completed", TimeSpan.FromMinutes(1)), ("Failed", null)], none).Body, StringComparison.Ordinal);
        Assert.StartsWith("Jobs: 0\nFailures: 0\np50 duration: none\np90 duration: none\n", OperatorSummary.Compose([], none).Body, StringComparison.Ordinal);
        Assert.Contains("p50 duration: 1.5 minutes\n", OperatorSummary.Compose([("Completed", TimeSpan.FromSeconds(90))], none).Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheWeek_IsReadFromEachAccountsJobs_AndMailedToTheOperator()
    {
        var accounts = Substitute.For<IAccountStore>();
        accounts.ListAsync(Arg.Any<CancellationToken>()).Returns([new AccountSummary("user-1", "Active"), new AccountSummary("user-2", "Active")]);
        var index = Substitute.For<IConsultGenerationJobIndexStore>();
        index.ListAllAsync("user-1", Arg.Any<CancellationToken>()).Returns(
        [
            Job("a", ConsultGenerationJobStatuses.Completed, Now.AddDays(-1), minutes: 4),
            Job("b", ConsultGenerationJobStatuses.Running, null, minutes: null),
            Job("c", ConsultGenerationJobStatuses.Completed, Now.AddDays(-8), minutes: 30),
        ]);
        index.ListAllAsync("user-2", Arg.Any<CancellationToken>()).Returns([Job("d", ConsultGenerationJobStatuses.Failed, Now.AddHours(-2), minutes: 2)]);
        var mail = Substitute.For<IGraphMailClient>();

        var summary = Summary(accounts, index: index, mail: mail);
        Assert.True(await summary.RunOnceAsync(Now, CancellationToken.None));

        // both accounts run a 31 package, so nothing is migrated: none outstanding
        var (_, expected) = OperatorSummary.Compose([("Completed", TimeSpan.FromMinutes(4)), ("Failed", TimeSpan.FromMinutes(2))], MigrationFigures.Counted(0, 0, 0));
        await mail.Received(1).SendMailAsync("consults@example.com", "operator@example.com", "Consultologist weekly summary", expected,
            Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<GraphMailAttachment>?>());
    }

    [Fact]
    public async Task WithNoSchedule_TheTimerIsOff()
    {
        // the pattern TextRetentionSweep uses: a %…% binding expression on a flat setting, so with it unset the function fails
        // indexing and is disabled, the host unaffected
        var method = typeof(OperatorWeeklySummary).GetMethod(nameof(OperatorWeeklySummary.RunAsync))!;
        Assert.Equal("OperatorWeeklySummary", method.GetCustomAttribute<FunctionAttribute>()!.Name);
        var trigger = method.GetParameters().Select(p => p.GetCustomAttribute<TimerTriggerAttribute>()).Single(a => a != null)!;
        Assert.Equal($"%{OperatorWeeklySummary.ScheduleSetting}%", trigger.Schedule);
        Assert.Equal("OperatorWeeklySummarySchedule", OperatorWeeklySummary.ScheduleSetting);
        var sweep = typeof(TextRetentionFunctions).GetMethod(nameof(TextRetentionFunctions.RunAsync))!
            .GetParameters().Select(p => p.GetCustomAttribute<TimerTriggerAttribute>()).Single(a => a != null)!;
        Assert.Equal("%TextRetentionSweepSchedule%", sweep.Schedule);

        // and with the preview setting unset, a run that does fire reads nothing and sends nothing
        var accounts = Substitute.For<IAccountStore>();
        var index = Substitute.For<IConsultGenerationJobIndexStore>();
        var mail = Substitute.For<IGraphMailClient>();
        var pins = Substitute.For<IWorkflowPackagePinResolver>();
        var sources = Substitute.For<IWorkflowPackageSources>();
        var off = Summary(accounts, pins, sources, index: index, mail: mail, admission: SpecVersionAdmissionTests.With(null));

        Assert.False(await off.RunOnceAsync(Now, CancellationToken.None));
        Assert.Empty(accounts.ReceivedCalls());
        Assert.Empty(index.ReceivedCalls());
        Assert.Empty(mail.ReceivedCalls());
        Assert.Empty(pins.ReceivedCalls());
        Assert.Empty(sources.ReceivedCalls());
    }

    [Fact]
    public async Task TheMigrationLine_CountsHandEditsOfPackagesBelow31()
    {
        // two account packages at 30, one with hand edits (composer-sources), one clean (general), and one at 31; two
        // accounts share composer-sources, which is one package
        var (accounts, pins) = Accounts(("user-1", "composer-sources@v2026.11.1"), ("user-2", "general@latest"), ("user-3", "acct-0123456789ab-letters@v2026.11.2"), ("user-4", "composer-sources@v2026.11.1"));
        var sources = Substitute.For<IWorkflowPackageSources>();
        Source(sources, "composer-sources@v2026.11.1", Fixture("composer-sources", "composer-sources@v2026.11.1"));
        Source(sources, "general@latest", Fixture("general", "general@v2026.11.3"));
        Source(sources, "acct-0123456789ab-letters@v2026.11.2", At31("acct-0123456789ab-letters@v2026.11.2"));
        var mail = Substitute.For<IGraphMailClient>();

        Assert.True(await Summary(accounts, pins, sources, mail: mail).RunOnceAsync(Now, CancellationToken.None));

        var expectedHandEdits = Migrate("composer-sources").HandEdits.Count;
        Assert.True(expectedHandEdits > 0);
        Assert.Empty(Migrate("general").HandEdits);
        var body = SentBody(mail);
        Assert.EndsWith($"Largest record: none\nOutstanding migration hand edits: {expectedHandEdits} in 1 packages; 0 packages do not migrate\n", body, StringComparison.Ordinal);

        // the line carries counts only: no package, no account, no notice
        Assert.DoesNotContain("composer", body, StringComparison.Ordinal);
        Assert.DoesNotContain("general", body, StringComparison.Ordinal);
        Assert.DoesNotContain("acct-", body, StringComparison.Ordinal);
        Assert.DoesNotContain("user-", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Hand edit", body, StringComparison.Ordinal);
        await sources.Received(1).ReadSourceAsync(new WorkflowPackageRef("composer-sources", "v2026.11.1"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheMigrationLine_CountsAtTheConfiguredJobInstances()
    {
        // example-nested-note opens 120,814 instances (its loops' items stripped by nodes of their own since #1510): past the
        // default 10,000 (A10-081's hand edit), within 200,000
        const string pin = "example-nested-note@v2026.11.1";
        var (accounts, pins) = Accounts(("user-1", pin));
        var sources = Substitute.For<IWorkflowPackageSources>();
        Source(sources, pin, Fixture("example-nested-note", pin));

        var atTheDefault = Substitute.For<IGraphMailClient>();
        Assert.True(await Summary(accounts, pins, sources, mail: atTheDefault).RunOnceAsync(Now, CancellationToken.None));
        Assert.EndsWith("Outstanding migration hand edits: 1 in 1 packages; 0 packages do not migrate\n", SentBody(atTheDefault), StringComparison.Ordinal);

        var configured = new EngineLimitSettings(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConsultGeneration:JobInstances"] = "200000" })
            .Build());
        var atTheLimit = Substitute.For<IGraphMailClient>();
        Assert.True(await Summary(accounts, pins, sources, mail: atTheLimit, limits: configured).RunOnceAsync(Now, CancellationToken.None));
        Assert.EndsWith("Outstanding migration hand edits: 0 in 0 packages; 0 packages do not migrate\n", SentBody(atTheLimit), StringComparison.Ordinal);
    }

    [Fact]
    public async Task APackageThatDoesNotMigrate_IsCountedApart()
    {
        // a manifest below 5 (A10-012) and one that does not read (A10-016) are refusals: each a package that does not
        // migrate, beside the hand edits, and neither a failure to gather the figures
        var (accounts, pins) = Accounts(("user-1", "old@v2026.10.1"), ("user-2", "broken@v2026.10.1"), ("user-3", "composer-sources@v2026.11.1"));
        var sources = Substitute.For<IWorkflowPackageSources>();
        Source(sources, "old@v2026.10.1", new WorkflowPackageSource("old@v2026.10.1", 4, "{\"specVersion\": 4}", new Dictionary<string, string>(), null));
        Source(sources, "broken@v2026.10.1", new WorkflowPackageSource("broken@v2026.10.1", null, "{ not json", new Dictionary<string, string>(), null));
        Source(sources, "composer-sources@v2026.11.1", Fixture("composer-sources", "composer-sources@v2026.11.1"));
        var mail = Substitute.For<IGraphMailClient>();

        Assert.True(await Summary(accounts, pins, sources, mail: mail).RunOnceAsync(Now, CancellationToken.None));

        Assert.EndsWith(
            $"Largest record: none\nOutstanding migration hand edits: {Migrate("composer-sources").HandEdits.Count} in 1 packages; 2 packages do not migrate\n",
            SentBody(mail),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pin")]
    [InlineData("package")]
    public async Task OneUnreadablePinOrPackage_IsNotCounted_TheOthersAre(string failing)
    {
        // a stale or broken pin, or a package the store cannot read, is a normal state: it fails alone, the others are
        // counted on D22's line, and A10-059 says how many were not
        var (accounts, pins) = Accounts(("user-1", "composer-sources@v2026.11.1"), ("user-2", "general@latest"), ("user-3", "acct-0123456789ab-stale@v2026.10.1"));
        var sources = Substitute.For<IWorkflowPackageSources>();
        Source(sources, "composer-sources@v2026.11.1", Fixture("composer-sources", "composer-sources@v2026.11.1"));
        Source(sources, "general@latest", Fixture("general", "general@v2026.11.3"));
        if (failing == "pin")
        {
            pins.ResolvePinAsync("user-3", Arg.Any<CancellationToken>()).Returns<WorkflowPackageRef>(_ => throw new InvalidOperationException("table down"));
        }
        else
        {
            sources.ReadSourceAsync(new WorkflowPackageRef("acct-0123456789ab-stale", "v2026.10.1"), Arg.Any<CancellationToken>())
                .Returns<WorkflowPackageSource>(_ => throw new InvalidOperationException("blob down"));
        }

        var mail = Substitute.For<IGraphMailClient>();
        Assert.True(await Summary(accounts, pins, sources, mail: mail).RunOnceAsync(Now, CancellationToken.None));

        var body = SentBody(mail);
        const string notCounted = "Outstanding migration hand edits: 1 of the accounts' active packages could not be read and are not counted.";
        Assert.EndsWith(
            $"Outstanding migration hand edits: {Migrate("composer-sources").HandEdits.Count} in 1 packages; 0 packages do not migrate\n{notCounted}\n",
            body,
            StringComparison.Ordinal);
        Assert.DoesNotContain("table down", body, StringComparison.Ordinal);
        Assert.DoesNotContain("blob down", body, StringComparison.Ordinal);
        Assert.DoesNotContain("stale", body, StringComparison.Ordinal);
        V31Contract.AssertSentence(new SourceRefusal("A10-059", notCounted, ""));
    }

    [Theory]
    [InlineData("pins")]
    [InlineData("packages")]
    [InlineData("registry")]
    public async Task WhenThePackagesCannotBeRead_TheLineSaysSo_NeverZero(string failing)
    {
        var (accounts, pins) = Accounts(("user-1", "composer-sources@v2026.11.1"), ("user-2", "general@latest"));
        var sources = Substitute.For<IWorkflowPackageSources>();
        Source(sources, "composer-sources@v2026.11.1", Fixture("composer-sources", "composer-sources@v2026.11.1"));
        Source(sources, "general@latest", Fixture("general", "general@v2026.11.3"));
        var registry = Registry();
        switch (failing)
        {
            case "pins":
                pins.ResolvePinAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns<WorkflowPackageRef>(_ => throw new InvalidOperationException("table down"));
                break;
            case "packages":
                sources.ReadSourceAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
                    .Returns<WorkflowPackageSource>(_ => throw new InvalidOperationException("blob down"));
                break;
            default:
                registry = Missing();
                break;
        }

        var mail = Substitute.For<IGraphMailClient>();
        Assert.True(await Summary(accounts, pins, sources, registry, mail: mail).RunOnceAsync(Now, CancellationToken.None));

        // every read failing: no D22 line with zeros, the error line alone; the registry unread: A10-058 alone
        var body = SentBody(mail);
        var (id, line) = failing == "registry"
            ? ("A10-058", "Outstanding migration hand edits: not counted; the type registry could not be read.")
            : ("A10-059", "Outstanding migration hand edits: 2 of the accounts' active packages could not be read and are not counted.");
        Assert.EndsWith("Largest record: none\n" + line + "\n", body, StringComparison.Ordinal);
        Assert.DoesNotContain(" in 0 packages", body, StringComparison.Ordinal);
        Assert.DoesNotContain("do not migrate", body, StringComparison.Ordinal);
        Assert.DoesNotContain("table down", body, StringComparison.Ordinal);
        Assert.DoesNotContain("blob down", body, StringComparison.Ordinal);
        V31Contract.AssertSentence(new SourceRefusal(id, line, ""));
    }

    [Fact]
    public void WhenNothingCanBeGathered_A10_058_StandsAlone()
    {
        var line = "Outstanding migration hand edits: not counted; the accounts' active packages could not be read.";
        Assert.Equal([line], MigrationFigures.NotRead(MigrationFigures.ThePackages).Lines);
        V31Contract.AssertSentence(new SourceRefusal("A10-058", line, ""));

        // nothing unread has no figure: not a zero
        var none = MigrationFigures.NotRead(MigrationFigures.TheRegistry);
        Assert.Null(none.HandEdits);
        Assert.Null(none.Packages);
        Assert.Null(none.DoNotMigrate);
        Assert.Equal(0, none.NotCounted);
        var noneRead = MigrationFigures.NoneRead(3);
        Assert.Null(noneRead.HandEdits);
        Assert.Equal(["Outstanding migration hand edits: 3 of the accounts' active packages could not be read and are not counted."], noneRead.Lines);
    }

    [Fact]
    public async Task TheRegistry_IsReadOnlyWhenAPackageIsMigrated()
    {
        // every account on a 31 package: nothing is migrated, so a registry that cannot be read is never read
        var (accounts, pins) = Accounts(("user-1", "acct-0123456789ab-letters@v2026.11.2"));
        var sources = Substitute.For<IWorkflowPackageSources>();
        Source(sources, "acct-0123456789ab-letters@v2026.11.2", At31("acct-0123456789ab-letters@v2026.11.2"));
        var mail = Substitute.For<IGraphMailClient>();

        Assert.True(await Summary(accounts, pins, sources, Missing(), mail: mail).RunOnceAsync(Now, CancellationToken.None));

        Assert.EndsWith("Outstanding migration hand edits: 0 in 0 packages; 0 packages do not migrate\n", SentBody(mail), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLargestRecordLine_IsTheWeeksLargestSealedRecord()
    {
        // the week's sealed records: the largest is job "big"; a larger one ended before the week, and a 5–30 job has none
        var accounts = Substitute.For<IAccountStore>();
        accounts.ListAsync(Arg.Any<CancellationToken>()).Returns([new AccountSummary("user-1", "Active"), new AccountSummary("user-2", "Active")]);
        var index = Substitute.For<IConsultGenerationJobIndexStore>();
        index.ListAllAsync("user-1", Arg.Any<CancellationToken>()).Returns(
        [
            Job("small", ConsultGenerationJobStatuses.Completed, Now.AddDays(-1), minutes: 4, recordBytes: 900, recordRows: 7),
            Job("old", ConsultGenerationJobStatuses.Completed, Now.AddDays(-8), minutes: 4, recordBytes: 99_999_999, recordRows: 9_999),
            Job("thirty", ConsultGenerationJobStatuses.Completed, Now.AddDays(-1), minutes: 4),
        ]);
        index.ListAllAsync("user-2", Arg.Any<CancellationToken>()).Returns(
            [Job("big", ConsultGenerationJobStatuses.Failed, Now.AddHours(-2), minutes: 2, recordBytes: 1_234_567, recordRows: 4_210)]);
        var mail = Substitute.For<IGraphMailClient>();

        Assert.True(await Summary(accounts, index: index, mail: mail).RunOnceAsync(Now, CancellationToken.None));

        var body = SentBody(mail);
        Assert.Contains("\nLargest record: 1,234,567 bytes, 4,210 rows (job big)\nOutstanding migration hand edits: ", body, StringComparison.Ordinal);
        Assert.DoesNotContain("not read", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithNoSealedRecord_TheLineSaysNone()
    {
        // a week of 5–30 jobs only holds no sealed record; so does a week with no jobs
        var accounts = Substitute.For<IAccountStore>();
        accounts.ListAsync(Arg.Any<CancellationToken>()).Returns([new AccountSummary("user-1", "Active")]);
        var index = Substitute.For<IConsultGenerationJobIndexStore>();
        index.ListAllAsync("user-1", Arg.Any<CancellationToken>()).Returns([Job("thirty", ConsultGenerationJobStatuses.Completed, Now.AddDays(-1), minutes: 4)]);
        var mail = Substitute.For<IGraphMailClient>();

        Assert.True(await Summary(accounts, index: index, mail: mail).RunOnceAsync(Now, CancellationToken.None));

        Assert.Contains("\nLargest record: none\nOutstanding migration hand edits: ", SentBody(mail), StringComparison.Ordinal);
        Assert.Contains("\nLargest record: none\n", OperatorSummary.Compose([], MigrationFigures.Counted(0, 0, 0)).Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhenTheSizesCannotBeRead_TheLineSaysSo_NeverZero()
    {
        // a sealed job whose row holds no size (sealed before sizes were written, or not sealed yet) is not measured
        var accounts = Substitute.For<IAccountStore>();
        accounts.ListAsync(Arg.Any<CancellationToken>()).Returns([new AccountSummary("user-1", "Active")]);
        var index = Substitute.For<IConsultGenerationJobIndexStore>();
        index.ListAllAsync("user-1", Arg.Any<CancellationToken>()).Returns(
        [
            Job("a", ConsultGenerationJobStatuses.Completed, Now.AddDays(-1), minutes: 4, recordStorage: 12),
            Job("b", ConsultGenerationJobStatuses.Failed, Now.AddDays(-2), minutes: 4, recordStorage: 12),
        ]);
        var mail = Substitute.For<IGraphMailClient>();
        Assert.True(await Summary(accounts, index: index, mail: mail).RunOnceAsync(Now, CancellationToken.None));

        const string notRead = "Largest record: not measured; the sizes of 2 sealed records could not be read.";
        var body = SentBody(mail);
        Assert.Contains("\n" + notRead + "\nOutstanding migration hand edits: ", body, StringComparison.Ordinal);
        Assert.DoesNotContain("0 bytes", body, StringComparison.Ordinal);

        // some read: the largest of those, then how many were not counted
        index.ListAllAsync("user-1", Arg.Any<CancellationToken>()).Returns(
        [
            Job("a", ConsultGenerationJobStatuses.Completed, Now.AddDays(-1), minutes: 4, recordStorage: 12),
            Job("b", ConsultGenerationJobStatuses.Failed, Now.AddDays(-2), minutes: 4, recordBytes: 50, recordRows: 3),
        ]);
        mail.ClearReceivedCalls();
        Assert.True(await Summary(accounts, index: index, mail: mail).RunOnceAsync(Now, CancellationToken.None));
        Assert.Contains(
            "\nLargest record: 50 bytes, 3 rows (job b)\nLargest record: 1 sealed records could not be read and are not counted.\nOutstanding migration hand edits: ",
            SentBody(mail), StringComparison.Ordinal);
    }

    private static readonly Lazy<PublishedTypeRegistry> Bundled = new(() => new PublishedTypeRegistry(AppContext.BaseDirectory, OutputContractCatalog.ResolveDirectory()));

    private static PublishedTypeRegistry Registry() => Bundled.Value;

    private static PublishedTypeRegistry Missing()
    {
        var missing = Path.Combine(Path.GetTempPath(), "consultologist-no-registry-" + Guid.NewGuid().ToString("N"));
        return new PublishedTypeRegistry(missing, missing);
    }

    /// <summary>The summary over <paramref name="accounts"/>; by default every account runs a 31 package, so nothing is migrated.</summary>
    private static OperatorWeeklySummary Summary(
        IAccountStore accounts,
        IWorkflowPackagePinResolver? pins = null,
        IWorkflowPackageSources? sources = null,
        PublishedTypeRegistry? registry = null,
        IConsultGenerationJobIndexStore? index = null,
        IGraphMailClient? mail = null,
        SpecVersionAdmission? admission = null,
        EngineLimitSettings? limits = null)
    {
        if (pins == null)
        {
            pins = Substitute.For<IWorkflowPackagePinResolver>();
            pins.ResolvePinAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new WorkflowPackageRef("acct-0123456789ab-letters", "v2026.11.2"));
        }

        if (sources == null)
        {
            sources = Substitute.For<IWorkflowPackageSources>();
            sources.ReadSourceAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>()).Returns(At31("acct-0123456789ab-letters@v2026.11.2"));
        }

        if (index == null)
        {
            index = Substitute.For<IConsultGenerationJobIndexStore>();
            index.ListAllAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        }

        var handEdits = new OutstandingHandEdits(
            pins, sources, registry ?? Registry(), V31Job.Toolchain.Value, TestFormats.Catalog, OutputContractCatalog.Load(),
            limits ?? new EngineLimitSettings(new ConfigurationBuilder().Build()), NullLogger<OutstandingHandEdits>.Instance);
        return new OperatorWeeklySummary(
            admission ?? SpecVersionAdmissionTests.With("31"), accounts, index, handEdits, mail ?? Substitute.For<IGraphMailClient>(),
            Configuration(), NullLogger<OperatorWeeklySummary>.Instance);
    }

    private static (IAccountStore Accounts, IWorkflowPackagePinResolver Pins) Accounts(params (string User, string Pin)[] pinned)
    {
        var accounts = Substitute.For<IAccountStore>();
        accounts.ListAsync(Arg.Any<CancellationToken>()).Returns(pinned.Select(p => new AccountSummary(p.User, "Active")).ToList());
        var pins = Substitute.For<IWorkflowPackagePinResolver>();
        foreach (var (user, pin) in pinned)
        {
            Assert.True(WorkflowPackageRef.TryParse(pin, out var parsed));
            pins.ResolvePinAsync(user, Arg.Any<CancellationToken>()).Returns(parsed!);
        }

        return (accounts, pins);
    }

    private static void Source(IWorkflowPackageSources sources, string pin, WorkflowPackageSource source)
    {
        Assert.True(WorkflowPackageRef.TryParse(pin, out var parsed));
        sources.ReadSourceAsync(parsed!, Arg.Any<CancellationToken>()).Returns(source);
    }

    /// <summary>A 30 fixture package as the store reads it: its stored manifest and files, the engine's formats catalogue.</summary>
    private static WorkflowPackageSource Fixture(string package, string resolvedRef)
    {
        var (manifestJson, files) = ByteEquivalenceFixtures.ReadPackage(package);
        return new WorkflowPackageSource(resolvedRef, 30, manifestJson, files, TestFormats.Catalog);
    }

    private static WorkflowPackageSource At31(string resolvedRef) =>
        new(resolvedRef, 31, "{\"specVersion\": 31}", new Dictionary<string, string>(), null);

    /// <summary><paramref name="package"/> migrated as the summary migrates an account's package (no hand-chosen renames, every deliverable "release").</summary>
    private static Migration31Result Migrate(string package)
    {
        var (manifestJson, files) = ByteEquivalenceFixtures.ReadPackage(package);
        return WorkflowPackageMigration30To31.Migrate(manifestJson, files, new Migration31Options(
            V31Contract.PublishedTypes(), V31Job.Toolchain.Value.Current(), TestFormats.Catalog,
            schema => OutputContractCatalog.Load().TryResolveContract(schema, out var id) ? id : null));
    }

    private static string SentBody(IGraphMailClient mail)
    {
        var call = Assert.Single(mail.ReceivedCalls());
        return (string)call.GetArguments()[3]!;
    }

    private static ConsultGenerationJobIndexEntry Job(
        string id, string status, DateTimeOffset? completed, int? minutes, long? recordBytes = null, int? recordRows = null, int? recordStorage = null) =>
        new(id, "user", status, Now.AddDays(-9), completed is { } c && minutes is { } m ? c.AddMinutes(-m) : null, completed, 1, 1, 0,
            RecordStorage: recordStorage ?? (recordBytes != null ? 12 : null), RecordBytes: recordBytes, RecordRows: recordRows);

    private static IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        [OperatorAlerts.AddressSetting] = "operator@example.com",
        ["EmailIntake:MailboxAddress"] = "consults@example.com",
    }).Build();
}
