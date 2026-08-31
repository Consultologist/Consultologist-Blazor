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
        var entity = new ConsultGenerationJobEntity(index, Substitute.For<IJobOutputsBlobStore>(), Substitute.For<IJobInputsBlobStore>());
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

    [Fact]
    public async Task TheSweep_PurgesEveryDueJob_AndKeepsGoingPastAFailure()
    {
        var accounts = Substitute.For<IAccountStore>();
        accounts.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<AccountSummary> { new("a", "Active"), new("b", "Active") });
        var index = Substitute.For<IConsultGenerationJobIndexStore>();
        var now = new DateTimeOffset(2026, 9, 2, 3, 0, 0, TimeSpan.Zero);
        index.ListDueForTextDropAsync("a", now.AddDays(-7), Arg.Any<CancellationToken>()).Returns(new List<ConsultGenerationJobIndexEntry>
        {
            new("j1", "a", "Completed", now.AddDays(-9), null, now.AddDays(-8), 1, 1, 0),
            new("j2", "a", "Completed", now.AddDays(-9), null, now.AddDays(-8), 1, 1, 0)
        });
        index.ListDueForTextDropAsync("b", now.AddDays(-7), Arg.Any<CancellationToken>()).Returns(new List<ConsultGenerationJobIndexEntry>());
        var purger = Substitute.For<IJobTextPurger>();
        purger.PurgeAsync(Arg.Any<DurableTaskClient>(), "j1", now, Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("storage")));
        var client = Substitute.For<DurableTaskClient>("test");

        var (a, due, dropped) = await new TextRetentionSweep(accounts, index, purger, NullLogger<TextRetentionSweep>.Instance)
            .RunOnceAsync(client, now, 7, CancellationToken.None);

        Assert.Equal((2, 2, 1), (a, due, dropped));
        await purger.Received(1).PurgeAsync(client, "j2", now, Arg.Any<CancellationToken>());
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
