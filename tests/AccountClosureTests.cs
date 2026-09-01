using Consultologist.Api.Auth;
using Consultologist.Api.Forms;
using Consultologist.Api.Jobs;
using Consultologist.Api.Workflow;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Consultologist.Api.Tests;

/// <summary>
/// #559 (storage-separation § 2.6): the closure's ladder and order. What
/// stays — usage, rate limits, the email ledger — is structurally safe:
/// those stores are not dependencies of the service at all.
/// </summary>
public class AccountClosureTests
{
    private const string User = "7bca2dcc1ed446f298e4931e3865c283";
    private const string Operator = "operator-1";

    private readonly IAccountStore _accounts = Substitute.For<IAccountStore>();
    private readonly IAccountSettingsStore _settings = Substitute.For<IAccountSettingsStore>();
    private readonly IConsultGenerationJobIndexStore _index = Substitute.For<IConsultGenerationJobIndexStore>();
    private readonly IConsultGenerationJobEventStore _events = Substitute.For<IConsultGenerationJobEventStore>();
    private readonly ILegacyJobEventDelete _legacyEvents = Substitute.For<ILegacyJobEventDelete>();
    private readonly IConsultGenerationLinkStore _links = Substitute.For<IConsultGenerationLinkStore>();
    private readonly IFormResponseStore _formResponses = Substitute.For<IFormResponseStore>();
    private readonly IJobOutputsBlobStore _outputsBlobs = Substitute.For<IJobOutputsBlobStore>();
    private readonly IJobInputsBlobStore _inputsBlobs = Substitute.For<IJobInputsBlobStore>();
    private readonly IFormResponseBlobStore _formResponseBlobs = Substitute.For<IFormResponseBlobStore>();
    private readonly IWorkflowPackageOwnership _ownership = Substitute.For<IWorkflowPackageOwnership>();
    private readonly IWorkflowPackageRegistryWriter _packageRegistry = Substitute.For<IWorkflowPackageRegistryWriter>();
    private readonly IClosedAccountsStore _closed = Substitute.For<IClosedAccountsStore>();
    private readonly DurableTaskClient _client = Substitute.For<DurableTaskClient>("test");
    private readonly DurableEntityClient _entities = Substitute.For<DurableEntityClient>("test");

    private AccountClosure CreateClosure()
    {
        _client.Entities.Returns(_entities);
        return new AccountClosure(
            _accounts, _settings, _index, _events, _legacyEvents, _links, _formResponses,
            _outputsBlobs, _inputsBlobs, _formResponseBlobs, _ownership, _packageRegistry,
            _closed, NullLogger<AccountClosure>.Instance);
    }

    private static ConsultGenerationJobIndexEntry Job(string jobId, string status) =>
        new(jobId, User, status, DateTimeOffset.UtcNow.AddDays(-1), null, null, 1, 0, 0);

    private void WithDisabledAccount(params ConsultGenerationJobIndexEntry[] jobs)
    {
        _accounts.TryGetStatusAsync(User, Arg.Any<CancellationToken>()).Returns(AccountStatuses.Disabled);
        _index.ListAllAsync(User, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<ConsultGenerationJobIndexEntry>)jobs.ToList());
        _ownership.ListAsync(User, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<string>)new List<string>());
    }

    [Fact]
    public async Task AnAccountThatIsNotDisabled_IsRefused_NamingItsStatus()
    {
        _accounts.TryGetStatusAsync(User, Arg.Any<CancellationToken>()).Returns(AccountStatuses.Active);

        var outcome = await CreateClosure().CloseAsync(_client, User, Operator, CancellationToken.None);

        Assert.Contains("Active", outcome.Refusal);
        Assert.Contains("disable it first", outcome.Refusal);
        await _accounts.DidNotReceiveWithAnyArgs().DeleteAccountAsync(default!, default);
        await _index.DidNotReceiveWithAnyArgs().DeleteAllAsync(default!, default);
    }

    [Fact]
    public async Task AMissingAccount_IsNotFound()
    {
        _accounts.TryGetStatusAsync(User, Arg.Any<CancellationToken>()).Returns((string?)null);

        var outcome = await CreateClosure().CloseAsync(_client, User, Operator, CancellationToken.None);

        Assert.True(outcome.NotFound);
    }

    [Fact]
    public async Task ASecondCall_FindsTheClosureRow_AndDeletesNothing()
    {
        var record = new ClosedAccountRecord(User, DateTimeOffset.UtcNow, Operator, 1, 2, 3, 4, 5, 6, 7);
        _closed.TryGetAsync(User, Arg.Any<CancellationToken>()).Returns(record);

        var outcome = await CreateClosure().CloseAsync(_client, User, Operator, CancellationToken.None);

        Assert.True(outcome.AlreadyClosed);
        Assert.Equal(record, outcome.Closed);
        await _accounts.DidNotReceiveWithAnyArgs().TryGetStatusAsync(default!, default);
        await _index.DidNotReceiveWithAnyArgs().DeleteAllAsync(default!, default);
        await _accounts.DidNotReceiveWithAnyArgs().DeleteAccountAsync(default!, default);
    }

    [Fact]
    public async Task TheOrder_IsTheRecords_AndTheAccountRowGoesLast()
    {
        WithDisabledAccount(Job("job-1", "Completed"));

        await CreateClosure().CloseAsync(_client, User, Operator, CancellationToken.None);

        Received.InOrder(() =>
        {
            _events.DeleteJobAsync("job-1", Arg.Any<CancellationToken>());
            _outputsBlobs.DeleteAccountAsync(User, Arg.Any<CancellationToken>());
            _ownership.DeleteAllAsync(User, Arg.Any<CancellationToken>());
            _links.DeleteForJobAsync("job-1", Arg.Any<CancellationToken>());
            _formResponses.DeleteAllAsync(User, Arg.Any<CancellationToken>());
            _index.DeleteAllAsync(User, Arg.Any<CancellationToken>());
            _settings.DeleteAllAsync(User, Arg.Any<CancellationToken>());
            _accounts.DeleteAccountAsync(User, Arg.Any<CancellationToken>());
            _closed.RecordAsync(Arg.Any<ClosedAccountRecord>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task OnlyANonTerminalJob_IsTerminated()
    {
        WithDisabledAccount(Job("job-live", "Running"), Job("job-done", "Completed"));

        await CreateClosure().CloseAsync(_client, User, Operator, CancellationToken.None);

        await _client.Received(1).TerminateInstanceAsync("job-live", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _client.DidNotReceive().TerminateInstanceAsync("job-done", Arg.Any<string>(), Arg.Any<CancellationToken>());
        // Both are purged and both entities get the delete signal.
        await _client.Received(1).PurgeInstanceAsync("job-live", Arg.Any<CancellationToken>());
        await _client.Received(1).PurgeInstanceAsync("job-done", Arg.Any<CancellationToken>());
        await _entities.Received(2).SignalEntityAsync(Arg.Any<EntityInstanceId>(), "delete", cancellation: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheCounts_LandOnTheAuditRow()
    {
        WithDisabledAccount(Job("job-1", "Completed"), Job("job-2", "Completed"));
        _ownership.ListAsync(User, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<string>)new List<string> { "acct-7bca2dcc1ed4" });
        _packageRegistry.DeletePackageAsync("acct-7bca2dcc1ed4", Arg.Any<CancellationToken>()).Returns(5);
        _outputsBlobs.DeleteAccountAsync(User, Arg.Any<CancellationToken>()).Returns(2);
        _inputsBlobs.DeleteAccountAsync(User, Arg.Any<CancellationToken>()).Returns(1);
        _formResponseBlobs.DeleteAccountAsync(User, Arg.Any<CancellationToken>()).Returns(1);
        _links.DeleteForJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(1);
        _formResponses.DeleteAllAsync(User, Arg.Any<CancellationToken>()).Returns(3);
        _settings.DeleteAllAsync(User, Arg.Any<CancellationToken>()).Returns(4);
        _accounts.DeleteAccountAsync(User, Arg.Any<CancellationToken>()).Returns(2);

        var outcome = await CreateClosure().CloseAsync(_client, User, Operator, CancellationToken.None);

        var record = outcome.Closed!;
        Assert.Equal(2, record.Jobs);
        Assert.Equal(4, record.Blobs);
        Assert.Equal(5, record.Packages);
        Assert.Equal(2, record.Links);
        Assert.Equal(3, record.Responses);
        Assert.Equal(4, record.Settings);
        Assert.Equal(2, record.Identities);
        Assert.Equal(Operator, record.OperatorAppUserId);
        await _closed.Received(1).RecordAsync(record, Arg.Any<CancellationToken>());
    }
}
