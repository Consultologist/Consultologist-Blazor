using System.Reflection;
using Consultologist.Api.Auth;
using Consultologist.Api.Email;
using Consultologist.Api.Jobs;
using Consultologist.Api.V31;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1505 (specVersion 31, area 08; part 09 § 3.9; D14): the weekly summary: the week's jobs, its failures, the p50 and p90 of
/// their durations (nearest rank), and the two lines areas 09 and 10 fill. It runs on <c>%OperatorWeeklySummarySchedule%</c>,
/// off when that is unset (as <see cref="TextRetentionSweep"/>'s timer is), and reads nothing unless the engine runs 31.
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

        var (subject, body) = OperatorSummary.Compose(jobs);

        Assert.Equal("Consultologist weekly summary", subject);
        Assert.Equal(
            "Jobs: 10\n" +
            "Failures: 2\n" +
            "p50 duration: 5 minutes\n" +
            "p90 duration: 9 minutes\n" +
            "Largest record: not measured in this build\n" +
            "Outstanding migration hand edits: not measured in this build\n",
            body);

        // a job with no duration (it never started) counts, and is not ranked; no durations at all read none
        Assert.Contains("p50 duration: 1 minute\n", OperatorSummary.Compose([("Completed", TimeSpan.FromMinutes(1)), ("Failed", null)]).Body, StringComparison.Ordinal);
        Assert.StartsWith("Jobs: 0\nFailures: 0\np50 duration: none\np90 duration: none\n", OperatorSummary.Compose([]).Body, StringComparison.Ordinal);
        Assert.Contains("p50 duration: 1.5 minutes\n", OperatorSummary.Compose([("Completed", TimeSpan.FromSeconds(90))]).Body, StringComparison.Ordinal);
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

        var summary = new OperatorWeeklySummary(SpecVersionAdmissionTests.With("31"), accounts, index, mail, Configuration(), NullLogger<OperatorWeeklySummary>.Instance);
        Assert.True(await summary.RunOnceAsync(Now, CancellationToken.None));

        var (_, expected) = OperatorSummary.Compose([("Completed", TimeSpan.FromMinutes(4)), ("Failed", TimeSpan.FromMinutes(2))]);
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
        var off = new OperatorWeeklySummary(SpecVersionAdmissionTests.With(null), accounts, index, mail, Configuration(), NullLogger<OperatorWeeklySummary>.Instance);

        Assert.False(await off.RunOnceAsync(Now, CancellationToken.None));
        Assert.Empty(accounts.ReceivedCalls());
        Assert.Empty(index.ReceivedCalls());
        Assert.Empty(mail.ReceivedCalls());
    }

    private static ConsultGenerationJobIndexEntry Job(string id, string status, DateTimeOffset? completed, int? minutes) =>
        new(id, "user", status, Now.AddDays(-9), completed is { } c && minutes is { } m ? c.AddMinutes(-m) : null, completed, 1, 1, 0);

    private static IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        [OperatorAlerts.AddressSetting] = "operator@example.com",
        ["EmailIntake:MailboxAddress"] = "consults@example.com",
    }).Build();
}
