using Consultologist.Api.Jobs;
using NSubstitute;
using Consultologist.Api.Models;

namespace Consultologist.Api.Tests;

/// <summary>
/// #557: hydration fills exactly what the entity shed — deliverable texts,
/// block texts, the single v6 document — and touches nothing else.
/// </summary>
public class JobOutputsHydrationTests
{
    private static JobOutputsPayload Payload() => new(
        JobOutputsPayload.CurrentVersion,
        null,
        new[] { new JobOutputsDocument("note", "Consultation note\n\nAppended.", "hash-note") },
        new Dictionary<string, string> { ["note:hpi"] = "History text." },
        null);

    [Fact]
    public void TheResponse_GainsTheTexts_AndKeepsEverythingElse()
    {
        var response = new ConsultGenerationJobResponse(
            "job-1", "user-1", "Completed", 1, 1, 0,
            new Dictionary<string, string> { ["note:hpi"] = "" },
            new Dictionary<string, string>(),
            true,
            AssembledDocuments: new[]
            {
                new ConsultGenerationResultDocumentResponse("note", "Consultation note", null, "hash-note", null, null)
            },
            WorkflowOutputHash: "outer-hash");

        var hydrated = JobOutputsHydration.Apply(response, Payload());

        Assert.Equal("Consultation note\n\nAppended.", hydrated.AssembledDocuments!.Single().Text);
        Assert.Equal("hash-note", hydrated.AssembledDocuments!.Single().DocumentHash);
        Assert.Equal("History text.", hydrated.GeneratedBlocks["note:hpi"]);
        Assert.Equal("outer-hash", hydrated.WorkflowOutputHash);
        Assert.Equal("Completed", hydrated.Status);
    }

    [Fact]
    public void TheStateOverload_FillsDocumentsAndBlocks()
    {
        var state = ConsultGenerationJobState.Create("job-1", "user-1", new[]
        {
            (IReadOnlyDictionary<string, string>)new Dictionary<string, string> { ["id"] = "note:hpi", ["name"] = "History" }
        });
        state.AssembledDocuments = new List<ConsultGenerationResultDocumentState>
        {
            new() { ResultId = "note", Label = "Consultation note", Text = null, Ordinal = 0, DocumentHash = "hash-note" }
        };

        JobOutputsHydration.Apply(state, Payload());

        Assert.Equal("Consultation note\n\nAppended.", state.AssembledDocuments.Single().Text);
        Assert.Equal("History text.", state.Blocks["note:hpi"].GeneratedText);
    }

    [Fact]
    public void ADocumentTheBlobDoesNotName_IsLeftAlone()
    {
        var response = new ConsultGenerationJobResponse(
            "job-1", "user-1", "Completed", 1, 1, 0,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            true,
            AssembledDocuments: new[]
            {
                new ConsultGenerationResultDocumentResponse("other", "Other", "entity text", "h", null, null)
            });

        var hydrated = JobOutputsHydration.Apply(response, Payload());

        Assert.Equal("entity text", hydrated.AssembledDocuments!.Single().Text);
    }

    // ----- the transport's hydration condition -----

    private static ConsultGenerationJobs Transport(IJobOutputsBlobStore store) => new(
        Microsoft.Extensions.Logging.Abstractions.NullLogger<ConsultGenerationJobs>.Instance,
        Substitute.For<Consultologist.Api.Auth.IAccountAuthorizer>(),
        Substitute.For<IConsultGenerationJobEventStore>(),
        Substitute.For<IConsultGenerationJobStarter>(),
        Substitute.For<Consultologist.Api.Email.IGraphMailClient>(),
        new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
        Substitute.For<Consultologist.Api.Auth.IAccountSettingsStore>(),
        store);

    private static ConsultGenerationJobResponse Terminal(ConsultOutputsBlobPointer? pointer, DateTimeOffset? dropped) => new(
        "job-1", "user-1", "Completed", 1, 1, 0,
        new Dictionary<string, string>(),
        new Dictionary<string, string>(),
        true,
        TextDroppedAtUtc: dropped,
        OutputsBlob: pointer);

    [Fact]
    public async Task NoPointer_IsAPreMigrationRecord_ServedAsIs()
    {
        var store = Substitute.For<IJobOutputsBlobStore>();
        var response = Terminal(null, null);

        Assert.Same(response, await Transport(store).HydrateOutputsAsync(response, CancellationToken.None));
        await store.DidNotReceiveWithAnyArgs().ReadAsync(default!, default);
    }

    [Fact]
    public async Task ADroppedRecord_IsNeverHydrated()
    {
        var store = Substitute.For<IJobOutputsBlobStore>();
        var response = Terminal(new ConsultOutputsBlobPointer("org-job-outputs", "u/j.json"), DateTimeOffset.UtcNow);

        Assert.Same(response, await Transport(store).HydrateOutputsAsync(response, CancellationToken.None));
        await store.DidNotReceiveWithAnyArgs().ReadAsync(default!, default);
    }

    [Fact]
    public async Task ALivePointer_WithNoBlob_IsABrokenInvariant_NeverEmptyText()
    {
        var store = Substitute.For<IJobOutputsBlobStore>();
        var response = Terminal(new ConsultOutputsBlobPointer("org-job-outputs", "u/j.json"), null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Transport(store).HydrateOutputsAsync(response, CancellationToken.None));
    }
}
