using Consultologist.Api.Email;
using Consultologist.Api.V31;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1505 (specVersion 31, area 08; part 09 § 3.9; D14): an operator alert is always logged (<c>Operator alert. Kind=…</c>), and
/// mailed from the mailbox to <c>Operators__AlertAddress</c> when both are set. Its message holds ids, keys and counts only, and
/// a mail fault never reaches the caller, nor does the activity's.
/// </summary>
public class OperatorAlertsTests
{
    private const string Mailbox = "consults@example.com";
    private const string Address = "operator@example.com";

    private readonly IGraphMailClient _mail = Substitute.For<IGraphMailClient>();
    private readonly CapturingLogger<OperatorAlerts> _log = new();
    private readonly List<(string To, string Subject, string Body)> _sent = [];
    private Exception? _fault;

    public OperatorAlertsTests()
    {
        _mail.SendMailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(),
                Arg.Any<IReadOnlyList<GraphMailAttachment>?>())
            .Returns(call =>
            {
                if (_fault is { } fault)
                    return Task.FromException(fault);
                Assert.Equal(Mailbox, call.ArgAt<string>(0));
                _sent.Add((call.ArgAt<string>(1), call.ArgAt<string>(2), call.ArgAt<string>(3)));
                return Task.CompletedTask;
            });
    }

    [Fact]
    public async Task AnAlert_IsLogged_AndMailedWhenAnAddressIsSet()
    {
        await Alerts(Address, Mailbox).RaiseAsync(new OperatorAlert(OperatorAlertKinds.JobFailed, "job-31-0001", "node.plan", null), CancellationToken.None);

        var line = Assert.Single(_log.Entries);
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Equal("Operator alert. Kind=job-failed, JobId=job-31-0001, Key=node.plan, Number=(null)", line.Message);
        var (to, subject, body) = Assert.Single(_sent);
        Assert.Equal(Address, to);
        Assert.Equal("Consultologist operator alert: job-failed", subject);
        Assert.Equal("A job failed.\n\nKind: job-failed\nJob: job-31-0001\nKey: node.plan\n", body);
    }

    [Theory]
    [InlineData(null, Mailbox)]
    [InlineData("  ", Mailbox)]
    [InlineData(Address, null)]
    public async Task AnAlert_IsOnlyLogged_WhenNoneIs(string? address, string? mailbox)
    {
        await Alerts(address, mailbox).RaiseAsync(new OperatorAlert(OperatorAlertKinds.JobSlow, "job-31-0001", null, 24), CancellationToken.None);

        Assert.Equal("Operator alert. Kind=job-slow, JobId=job-31-0001, Key=(null), Number=24", Assert.Single(_log.Entries).Message);
        Assert.Empty(_sent);
        await _mail.DidNotReceiveWithAnyArgs().SendMailAsync(default!, default!, default!, default!, default, default);
    }

    [Fact]
    public void AnAlertsBody_HoldsIdsAndCountsOnly()
    {
        // every kind: a fixed sentence, then the kind, the job, the key and the number, each on its own line; nothing else
        foreach (var kind in new[]
                 {
                     OperatorAlertKinds.JobFailed, OperatorAlertKinds.CapacityWait, OperatorAlertKinds.JobSlow,
                     OperatorAlertKinds.DeliveryFailed, OperatorAlertKinds.PublishFailed, OperatorAlertKinds.ExporterWarning,
                 })
        {
            var (subject, body) = OperatorAlerts.Compose(new OperatorAlert(kind, "job-31-0001", "delivery:deliverable.note/email", 2));
            Assert.Equal($"Consultologist operator alert: {kind}", subject);
            var lines = body.Split('\n');
            Assert.EndsWith(".", lines[0], StringComparison.Ordinal);
            Assert.Equal(
                ["", $"Kind: {kind}", "Job: job-31-0001", "Key: delivery:deliverable.note/email", "Number: 2", ""],
                lines.Skip(1));
        }

        Assert.Equal(
            ["capacity-wait", "delivery-failed", "exporter-warning", "job-failed", "job-slow", "publish-failed"],
            typeof(OperatorAlertKinds).GetFields().Select(f => (string)f.GetValue(null)!).Order(StringComparer.Ordinal));

        // an alert with no job, key or number says none
        Assert.Equal("A publish failed.\n\nKind: publish-failed\n", OperatorAlerts.Compose(new OperatorAlert(OperatorAlertKinds.PublishFailed, null, null, null)).Body);
    }

    [Fact]
    public async Task AMailFault_NeverFailsTheCaller()
    {
        const string response = "Graph answered 503 for Margaret Okafor's referral (fictional)";
        _fault = new HttpRequestException(response);

        await Alerts(Address, Mailbox).RaiseAsync(new OperatorAlert(OperatorAlertKinds.DeliveryFailed, "job-31-0001", null, 1), CancellationToken.None);

        Assert.Equal(2, _log.Entries.Count);
        Assert.Equal("Operator mail failed. Kind=delivery-failed, ExceptionType=System.Net.Http.HttpRequestException", _log.Entries[1].Message);
        Assert.DoesNotContain("Margaret", _log.Everything, StringComparison.Ordinal);

        // nor does the activity's own caller, whatever the alerts do
        var failing = Substitute.For<IOperatorAlerts>();
        failing.RaiseAsync(default!, default).ThrowsAsyncForAnyArgs(new InvalidOperationException(response));
        var activityLog = new CapturingLogger<AlertOperatorV31Activity>();
        await new AlertOperatorV31Activity(failing, activityLog).RunAsync(new OperatorAlert(OperatorAlertKinds.JobFailed, "job-31-0001", null, null), CancellationToken.None);
        Assert.Equal("Operator alert failed. Kind=job-failed, JobId=job-31-0001, ExceptionType=System.InvalidOperationException", Assert.Single(activityLog.Entries).Message);
    }

    private OperatorAlerts Alerts(string? address, string? mailbox) =>
        new(_mail, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [OperatorAlerts.AddressSetting] = address,
            ["EmailIntake:MailboxAddress"] = mailbox,
        }).Build(), _log);
}
