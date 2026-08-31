using Consultologist.Api.Jobs;
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
}
