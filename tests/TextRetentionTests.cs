using System.Reflection;
using Consultologist.Api.Auth;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Consultologist.Api.Tests;

/// <summary>#368: the text is deleted after its retention period; the record stays.</summary>
public class TextRetentionTests
{
    private static readonly PropertyInfo StateProperty =
        typeof(ConsultGenerationJobEntity).GetProperty("State", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;

    private static (ConsultGenerationJobEntity Entity, Func<ConsultGenerationJobState> State, IConsultGenerationJobIndexStore Index) CompletedV7Job()
    {
        var index = Substitute.For<IConsultGenerationJobIndexStore>();
        var entity = new ConsultGenerationJobEntity(index, Substitute.For<IJobOutputsBlobStore>(), Substitute.For<IJobInputsBlobStore>(), Substitute.For<IAccountUsageStore>());
        var state = ConsultGenerationJobState.Create("job-1", "user-1", new[]
        {
            new Dictionary<string, string> { ["id"] = "note:draft", ["name"] = "Consultation note" },
            new Dictionary<string, string> { ["id"] = "letter:draft", ["name"] = "Patient letter" }
        });
        StateProperty.SetValue(entity, state);
        return (entity, () => (ConsultGenerationJobState)StateProperty.GetValue(entity)!, index);
    }

    // #557: these tests run with a bare (unconfigured) outputs store whose
    // WriteAsync returns a null pointer — the finalize then deliberately
    // keeps the entity text, so every test below models a pre-#557 record,
    // which is exactly the shape they always asserted. The migrated shape is
    // JobOutputsWriteTests' subject.
    private static async Task RunToCompletionAsync(ConsultGenerationJobEntity entity)
    {
        await entity.CompleteBlock(new BlockGenerationResult("note:draft", "Consultation note", true, "Consultation note", null));
        await entity.CompleteBlock(new BlockGenerationResult("letter:draft", "Patient letter", true, "Patient letter", null));
        entity.MarkNodeCompleted(new ConsultGenerationNodeUpdate("extract", "Extract", new[] { new ClinicalConcept("Chest pain", "finding", "29857009", true, true, "draft") }, "in", "out", 1, 3, 5));
        await entity.CompleteResultDocument(new ConsultGenerationResultDocument("note", "Consultation note", "Consultation note", 0));
        await entity.CompleteResultDocument(new ConsultGenerationResultDocument("letter", "Patient letter", "Patient letter", 1));
        await entity.FinalizeJob(new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Completed));
    }

    [Fact]
    public async Task Completion_StoresTheOutputHashes_EqualToWhatWasDerivedBefore()
    {
        var (entity, state, _) = CompletedV7Job();
        await RunToCompletionAsync(entity);

        // The published worked example (hash-definitions.md § 3, definition 3).
        Assert.Equal("c8f784623550f4da6037fa84eab103b246880be1be5ed5cb8ba061d08c45d6e5", state().WorkflowOutputHash);
        Assert.Equal(3, state().WorkflowOutputHashVersion);
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex("Consultation note"), state().AssembledDocuments!.Single(d => d.ResultId == "note").DocumentHash);
        var response = state().ToResponse();
        Assert.Equal(state().WorkflowOutputHash, response.WorkflowOutputHash);
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex("Patient letter"), response.AssembledDocuments!.Single(d => d.ResultId == "letter").DocumentHash);
    }

    [Fact]
    public async Task ARecordFromBeforeTheStamp_StillDerivesOnRead()
    {
        var (entity, state, _) = CompletedV7Job();
        await RunToCompletionAsync(entity);
        // Simulate a record completed before 2026-08-25: no stored hashes.
        state().WorkflowOutputHash = null;
        state().WorkflowOutputHashVersion = null;
        foreach (var document in state().AssembledDocuments!) { document.DocumentHash = null; }

        var response = state().ToResponse();
        Assert.Equal("c8f784623550f4da6037fa84eab103b246880be1be5ed5cb8ba061d08c45d6e5", response.WorkflowOutputHash);
        Assert.Equal(3, response.WorkflowOutputHashVersion);
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex("Patient letter"), response.AssembledDocuments!.Single(d => d.ResultId == "letter").DocumentHash);
    }

    [Fact]
    public async Task DropText_DeletesEveryTextSpecies_AndKeepsEverythingElse()
    {
        var (entity, state, index) = CompletedV7Job();
        await RunToCompletionAsync(entity);
        var before = state().ToResponse();
        var droppedAt = new DateTimeOffset(2026, 9, 1, 3, 0, 0, TimeSpan.Zero);

        await entity.DropText(new ConsultGenerationTextDrop(droppedAt));

        var s = state();
        Assert.Equal(droppedAt, s.TextDroppedAtUtc);
        Assert.Null(s.AssembledDocument);
        Assert.All(s.AssembledDocuments!, d => Assert.Null(d.Text));
        Assert.All(s.Blocks.Values, b => Assert.Null(b.GeneratedText));
        Assert.All(s.NodeOutputs!.Values, n => Assert.Null(n.Concepts));
        // Kept: every hash, the nodes, the labels, the status.
        Assert.Equal(before.WorkflowOutputHash, s.WorkflowOutputHash);
        Assert.All(s.AssembledDocuments!, d => Assert.NotNull(d.DocumentHash));
        Assert.Equal("in", s.NodeOutputs!["extract"].InputHash);
        Assert.Equal(ConsultGenerationJobStatuses.Completed, s.Status);
        Assert.Contains(s.History, h => h.Kind == "retention");

        var after = s.ToResponse();
        Assert.Equal(droppedAt, after.TextDroppedAtUtc);
        Assert.Empty(after.GeneratedBlocks);
        Assert.All(after.AssembledDocuments!, d => Assert.Null(d.Text));
        Assert.Equal(before.WorkflowOutputHash, after.WorkflowOutputHash);
        Assert.Equal(before.AssembledDocuments!.Select(d => d.DocumentHash), after.AssembledDocuments!.Select(d => d.DocumentHash));
        Assert.Equal(before.TotalBlockCount, after.TotalBlockCount);
        await index.Received().UpsertAsync(Arg.Is<ConsultGenerationJobIndexEntry>(e => e.TextDroppedAtUtc == droppedAt), Arg.Any<CancellationToken>());

        // Idempotent: a second drop changes nothing, not even the date.
        await entity.DropText(new ConsultGenerationTextDrop(droppedAt.AddDays(1)));
        Assert.Equal(droppedAt, state().TextDroppedAtUtc);
    }

    [Fact]
    public async Task DropText_RefusesANonTerminalJob()
    {
        var (entity, state, _) = CompletedV7Job();
        await entity.CompleteBlock(new BlockGenerationResult("note:draft", "Consultation note", true, "Consultation note", null));

        await entity.DropText(new ConsultGenerationTextDrop(DateTimeOffset.UtcNow));

        Assert.Null(state().TextDroppedAtUtc);
        Assert.Equal("Consultation note", state().Blocks["note:draft"].GeneratedText);
    }

    [Fact]
    public async Task DropText_OnARecordFromBefore_StampsTheHashesFirst()
    {
        var (entity, state, _) = CompletedV7Job();
        await RunToCompletionAsync(entity);
        state().WorkflowOutputHash = null; state().WorkflowOutputHashVersion = null;
        foreach (var document in state().AssembledDocuments!) { document.DocumentHash = null; }

        await entity.DropText(new ConsultGenerationTextDrop(DateTimeOffset.UtcNow));

        Assert.Equal("c8f784623550f4da6037fa84eab103b246880be1be5ed5cb8ba061d08c45d6e5", state().ToResponse().WorkflowOutputHash);
    }

    [Fact]
    public void IsDue_TerminalCompletedBeforeCutoffWithTextPresent()
    {
        var cutoff = new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.Zero);
        ConsultGenerationJobIndexEntry Entry(string status, DateTimeOffset? completed, DateTimeOffset? dropped = null) =>
            new("j", "u", status, completed ?? cutoff, null, completed, 1, 1, 0, TextDroppedAtUtc: dropped);

        Assert.True(TextRetentionSweep.IsDue(Entry("Completed", cutoff.AddDays(-1)), cutoff));
        Assert.True(TextRetentionSweep.IsDue(Entry("Failed", cutoff.AddDays(-1)), cutoff));
        Assert.True(TextRetentionSweep.IsDue(Entry("Cancelled", cutoff.AddDays(-1)), cutoff));
        Assert.False(TextRetentionSweep.IsDue(Entry("Running", cutoff.AddDays(-1)), cutoff));
        Assert.False(TextRetentionSweep.IsDue(Entry("Completed", cutoff.AddHours(1)), cutoff));
        Assert.False(TextRetentionSweep.IsDue(Entry("Completed", null), cutoff));
        Assert.False(TextRetentionSweep.IsDue(Entry("Completed", cutoff.AddDays(-1), cutoff.AddDays(-1)), cutoff));
    }

    private static IAccountStore Accounts(params string[] appUserIds)
    {
        var accounts = Substitute.For<IAccountStore>();
        accounts.ListAsync(Arg.Any<CancellationToken>()).Returns(appUserIds.Select(id => new AccountSummary(id, "Active")).ToList());
        return accounts;
    }

    // #548: an unstubbed pair reads as not chosen — the deployment default.
    private static IAccountSettingsStore Settings(params (string User, string Key, string Value)[] rows)
    {
        var settings = Substitute.For<IAccountSettingsStore>();
        foreach (var (user, key, value) in rows)
        {
            settings.GetAsync(user, key, Arg.Any<CancellationToken>())
                .Returns(new AccountSetting(key, value, "text/plain", DateTimeOffset.UtcNow));
        }

        return settings;
    }

    private static readonly IReadOnlyList<ConsultGenerationJobIndexEntry> NoJobs = new List<ConsultGenerationJobIndexEntry>();

    [Fact]
    public async Task TheSweep_PurgesEveryDueJob_AndKeepsGoingPastAFailure()
    {
        var accounts = Accounts("a", "b");
        var index = Substitute.For<IConsultGenerationJobIndexStore>();
        var now = new DateTimeOffset(2026, 9, 2, 3, 0, 0, TimeSpan.Zero);
        index.ListDueForTextDropAsync("a", now.AddDays(-7), Arg.Any<CancellationToken>()).Returns(new List<ConsultGenerationJobIndexEntry>
        {
            new("j1", "a", "Completed", now.AddDays(-9), null, now.AddDays(-8), 1, 1, 0),
            new("j2", "a", "Completed", now.AddDays(-9), null, now.AddDays(-8), 1, 1, 0)
        });
        index.ListDueForTextDropAsync("b", now.AddDays(-7), Arg.Any<CancellationToken>()).Returns(NoJobs);
        var purger = Substitute.For<IJobTextPurger>();
        purger.PurgeAsync(Arg.Any<DurableTaskClient>(), "j1", now, Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("storage")));
        var client = Substitute.For<DurableTaskClient>("test");

        var (a, due, dropped, inputsDropped) = await new TextRetentionSweep(accounts, index, purger, Settings(), NullLogger<TextRetentionSweep>.Instance)
            .RunOnceAsync(client, now, 7, CancellationToken.None);

        Assert.Equal((2, 2, 1, 0), (a, due, dropped, inputsDropped));
        await purger.Received(1).PurgeAsync(client, "j2", now, Arg.Any<CancellationToken>());
        // Equal clocks (nothing chosen): the inputs leg never runs.
        await index.DidNotReceiveWithAnyArgs().ListDueForInputsDropAsync(default!, default, default);
    }

    // ----- #548: the clocks are the account's -----

    [Fact]
    public void IsInputsDue_TerminalHeldNotYetDroppedBeforeCutoff()
    {
        var cutoff = new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.Zero);
        ConsultGenerationJobIndexEntry Entry(string status, DateTimeOffset? completed, bool held = true, DateTimeOffset? dropped = null) =>
            new("j", "u", status, completed ?? cutoff, null, completed, 1, 1, 0, InputsHeld: held, InputsDroppedAtUtc: dropped);

        Assert.True(TextRetentionSweep.IsInputsDue(Entry("Completed", cutoff.AddDays(-1)), cutoff));
        Assert.True(TextRetentionSweep.IsInputsDue(Entry("Failed", cutoff.AddDays(-1)), cutoff));
        Assert.False(TextRetentionSweep.IsInputsDue(Entry("Running", cutoff.AddDays(-1)), cutoff));
        Assert.False(TextRetentionSweep.IsInputsDue(Entry("Completed", cutoff.AddHours(1)), cutoff));
        Assert.False(TextRetentionSweep.IsInputsDue(Entry("Completed", null), cutoff));
        // Unheld (a pre-#547 row, or a run that held nothing): never signalled.
        Assert.False(TextRetentionSweep.IsInputsDue(Entry("Completed", cutoff.AddDays(-1), held: false), cutoff));
        // Already dropped — by its own clock or a full drop.
        Assert.False(TextRetentionSweep.IsInputsDue(Entry("Completed", cutoff.AddDays(-1), dropped: cutoff.AddDays(-1)), cutoff));
    }

    [Fact]
    public async Task TheSweep_ReadsEachAccountsClock()
    {
        var accounts = Accounts("a", "b");
        var settings = Settings(("a", AccountSettingKeys.RetentionOutputDays, "3"));
        var index = Substitute.For<IConsultGenerationJobIndexStore>();
        var now = new DateTimeOffset(2026, 9, 2, 3, 0, 0, TimeSpan.Zero);
        index.ListDueForTextDropAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(NoJobs);
        index.ListDueForInputsDropAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(NoJobs);
        var client = Substitute.For<DurableTaskClient>("test");

        await new TextRetentionSweep(accounts, index, Substitute.For<IJobTextPurger>(), settings, NullLogger<TextRetentionSweep>.Instance)
            .RunOnceAsync(client, now, 7, CancellationToken.None);

        // The chosen clock for a, the deployment default for b — exact cutoffs.
        await index.Received(1).ListDueForTextDropAsync("a", now.AddDays(-3), Arg.Any<CancellationToken>());
        await index.Received(1).ListDueForTextDropAsync("b", now.AddDays(-7), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AShorterInputsClock_SignalsTheInputsDropAlone()
    {
        var accounts = Accounts("a");
        var settings = Settings(("a", AccountSettingKeys.RetentionInputDays, "2"));
        var index = Substitute.For<IConsultGenerationJobIndexStore>();
        var now = new DateTimeOffset(2026, 9, 2, 3, 0, 0, TimeSpan.Zero);
        index.ListDueForTextDropAsync("a", now.AddDays(-7), Arg.Any<CancellationToken>()).Returns(NoJobs);
        index.ListDueForInputsDropAsync("a", now.AddDays(-2), Arg.Any<CancellationToken>()).Returns(new List<ConsultGenerationJobIndexEntry>
        {
            new("j1", "a", "Completed", now.AddDays(-4), null, now.AddDays(-3), 1, 1, 0, InputsHeld: true)
        });
        var purger = Substitute.For<IJobTextPurger>();
        var client = Substitute.For<DurableTaskClient>("test");

        var (_, due, dropped, inputsDropped) = await new TextRetentionSweep(accounts, index, purger, settings, NullLogger<TextRetentionSweep>.Instance)
            .RunOnceAsync(client, now, 7, CancellationToken.None);

        Assert.Equal((0, 0, 1), (due, dropped, inputsDropped));
        await purger.Received(1).DropInputsAsync(client, "j1", now, Arg.Any<CancellationToken>());
        // The inputs clock never takes the full path: no purge, no events delete.
        await purger.DidNotReceiveWithAnyArgs().PurgeAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task AJobPastBothClocks_TakesTheFullPathOnce()
    {
        var accounts = Accounts("a");
        var settings = Settings(("a", AccountSettingKeys.RetentionInputDays, "2"));
        var index = Substitute.For<IConsultGenerationJobIndexStore>();
        var now = new DateTimeOffset(2026, 9, 2, 3, 0, 0, TimeSpan.Zero);
        index.ListDueForTextDropAsync("a", now.AddDays(-7), Arg.Any<CancellationToken>()).Returns(new List<ConsultGenerationJobIndexEntry>
        {
            new("j1", "a", "Completed", now.AddDays(-10), null, now.AddDays(-9), 1, 1, 0, InputsHeld: true)
        });
        // The inputs list is built after the outputs leg: the full drop just
        // stamped InputsDroppedAtUtc, so the re-query no longer returns j1.
        index.ListDueForInputsDropAsync("a", now.AddDays(-2), Arg.Any<CancellationToken>()).Returns(NoJobs);
        var purger = Substitute.For<IJobTextPurger>();
        var client = Substitute.For<DurableTaskClient>("test");

        var (_, due, dropped, inputsDropped) = await new TextRetentionSweep(accounts, index, purger, settings, NullLogger<TextRetentionSweep>.Instance)
            .RunOnceAsync(client, now, 7, CancellationToken.None);

        Assert.Equal((1, 1, 0), (due, dropped, inputsDropped));
        await purger.Received(1).PurgeAsync(client, "j1", now, Arg.Any<CancellationToken>());
        await purger.DidNotReceiveWithAnyArgs().DropInputsAsync(default!, default!, default, default);
        Received.InOrder(() =>
        {
            index.ListDueForTextDropAsync("a", now.AddDays(-7), Arg.Any<CancellationToken>());
            index.ListDueForInputsDropAsync("a", now.AddDays(-2), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task ABrokenSettingsRead_FallsBackToTheDefault_AndTheSweepGoesOn()
    {
        var accounts = Accounts("a");
        var settings = Substitute.For<IAccountSettingsStore>();
        settings.GetAsync("a", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<AccountSetting?>(_ => throw new InvalidOperationException("storage blip"));
        var index = Substitute.For<IConsultGenerationJobIndexStore>();
        var now = new DateTimeOffset(2026, 9, 2, 3, 0, 0, TimeSpan.Zero);
        index.ListDueForTextDropAsync("a", now.AddDays(-7), Arg.Any<CancellationToken>()).Returns(NoJobs);
        var client = Substitute.For<DurableTaskClient>("test");

        await new TextRetentionSweep(accounts, index, Substitute.For<IJobTextPurger>(), settings, NullLogger<TextRetentionSweep>.Instance)
            .RunOnceAsync(client, now, 7, CancellationToken.None);

        await index.Received(1).ListDueForTextDropAsync("a", now.AddDays(-7), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailedInputsSignal_DoesNotStopTheRun()
    {
        var accounts = Accounts("a");
        var settings = Settings(("a", AccountSettingKeys.RetentionInputDays, "2"));
        var index = Substitute.For<IConsultGenerationJobIndexStore>();
        var now = new DateTimeOffset(2026, 9, 2, 3, 0, 0, TimeSpan.Zero);
        index.ListDueForTextDropAsync("a", now.AddDays(-7), Arg.Any<CancellationToken>()).Returns(NoJobs);
        index.ListDueForInputsDropAsync("a", now.AddDays(-2), Arg.Any<CancellationToken>()).Returns(new List<ConsultGenerationJobIndexEntry>
        {
            new("j1", "a", "Completed", now.AddDays(-4), null, now.AddDays(-3), 1, 1, 0, InputsHeld: true),
            new("j2", "a", "Completed", now.AddDays(-4), null, now.AddDays(-3), 1, 1, 0, InputsHeld: true)
        });
        var purger = Substitute.For<IJobTextPurger>();
        purger.DropInputsAsync(Arg.Any<DurableTaskClient>(), "j1", now, Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("storage")));
        var client = Substitute.For<DurableTaskClient>("test");

        var (_, _, _, inputsDropped) = await new TextRetentionSweep(accounts, index, purger, settings, NullLogger<TextRetentionSweep>.Instance)
            .RunOnceAsync(client, now, 7, CancellationToken.None);

        Assert.Equal(1, inputsDropped);
        await purger.Received(1).DropInputsAsync(client, "j2", now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ThePurger_SignalsTheEntity_PurgesTheOrchestration_DeletesTheEvents_InThatOrder()
    {
        var events = Substitute.For<IConsultGenerationJobEventStore>();
        var legacyEvents = Substitute.For<ILegacyJobEventDelete>();
        var client = Substitute.For<DurableTaskClient>("test");
        var entities = Substitute.For<DurableEntityClient>("test");
        client.Entities.Returns(entities);
        var now = DateTimeOffset.UtcNow;

        await new JobTextPurger(events, legacyEvents).PurgeAsync(client, "0123456789abcdef0123456789abcdef", now, CancellationToken.None);

        Received.InOrder(() =>
        {
            entities.SignalEntityAsync(
                Arg.Is<EntityInstanceId>(id => id.Key == "0123456789abcdef0123456789abcdef" && id.Name == nameof(ConsultGenerationJobEntity).ToLowerInvariant()),
                nameof(ConsultGenerationJobEntity.DropText), Arg.Any<object?>(), Arg.Any<SignalEntityOptions?>(), Arg.Any<CancellationToken>());
            client.PurgeInstanceAsync("0123456789abcdef0123456789abcdef", Arg.Any<CancellationToken>());
            events.DeleteJobAsync("0123456789abcdef0123456789abcdef", Arg.Any<CancellationToken>());
            // #557: the transition's trailing leg — the old table's partition.
            legacyEvents.DeleteJobAsync("0123456789abcdef0123456789abcdef", Arg.Any<CancellationToken>());
        });
        // The entity's own instance is never purged.
        Assert.NotEqual(new EntityInstanceId(nameof(ConsultGenerationJobEntity), "x").ToString(), JobTextPurger.OrchestrationInstanceId("x"));
    }

    [Fact]
    public void AfterTheDrop_ARecordCheck_SaysNotCheckable_NeverMismatch()
    {
        var record = new ConsultGenerationJobResponse("j", "u", "Completed", 2, 2, 0, new Dictionary<string, string>(), new Dictionary<string, string>(), true,
            WorkflowOutputHash: "c8f7", WorkflowOutputHashVersion: 3,
            AssembledDocuments: new[] { new ConsultGenerationResultDocumentResponse("note", "Consultation note", null, "abcd") },
            TextDroppedAtUtc: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));

        var checks = ProvenanceRecordCheck.Check(record);

        Assert.All(checks, c => Assert.Null(c.Matches));
        Assert.All(checks, c => Assert.Contains("text deleted on 2026-09-01", c.Note));
        Assert.Equal(2, checks.Count);
    }
}
