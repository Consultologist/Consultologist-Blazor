using System.Reflection;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using NSubstitute;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Tests;

/// <summary>
/// Pins the aggregator's normative bytes (package-format-v6-design.md § 3):
/// the rendering feeds hashes and downstream prompt inputs, so the exact
/// output is spec, not implementation detail.
/// </summary>
public class AggregateRendererTests
{
    [Fact]
    public void ForEachSource_RendersLabeledBlocksInOrder()
    {
        var rendered = ConsultAggregateRenderer.Render(new ConsultAggregateRenderer.Part[]
        {
            new ConsultAggregateRenderer.ForEachPart(new[]
            {
                ("A", "alpha"),
                ("B", "beta")
            })
        });

        Assert.Equal("## A\n\nalpha\n\n## B\n\nbeta", rendered);
    }

    [Fact]
    public void ScalarSource_RendersVerbatim()
    {
        var rendered = ConsultAggregateRenderer.Render(new ConsultAggregateRenderer.Part[]
        {
            new ConsultAggregateRenderer.ScalarPart("context text")
        });

        Assert.Equal("context text", rendered);
    }

    [Fact]
    public void MixedSources_JoinInDeclaredOrderWithBlankLines()
    {
        var rendered = ConsultAggregateRenderer.Render(new ConsultAggregateRenderer.Part[]
        {
            new ConsultAggregateRenderer.ForEachPart(new[] { ("A", "alpha") }),
            new ConsultAggregateRenderer.ScalarPart("closing remarks")
        });

        Assert.Equal("## A\n\nalpha\n\nclosing remarks", rendered);
    }

    [Fact]
    public void AssembledDocumentHash_IsSha256OfTheBytes()
    {
        // Pinned independently (Python hashlib) so the definition, not the
        // implementation, is the reference — the workflowOutputHash precedent.
        Assert.Equal(
            "5fc2a6c6c2663bddd1b949569e814b23d31c16dfc4ee17312d0c5a56721dd10b",
            ConsultGenerationProvenance.ComputeAssembledDocumentHash("## A\n\nalpha\n\n## B\n\nbeta"));
        Assert.Equal(2, ConsultGenerationProvenance.AssembledDocumentHashVersion);
    }

    [Fact]
    public void AggregateInputHash_IsCanonicalJsonArrayOfSourceHashes()
    {
        // sha256 of ["h1","h2"] — pinned independently (Python hashlib/json).
        Assert.Equal(
            "9fa6f4450d9e5f6c47b1820a9d25337a500e36414b7ed673ae2c398e194056e0",
            ConsultGenerationProvenance.ComputeAggregateInputHash(new[] { "h1", "h2" }));
    }
}

public class AssembledDocumentEntityTests
{
    private static readonly PropertyInfo StateProperty = typeof(ConsultGenerationJobEntity)
        .GetProperty("State", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;

    private static (ConsultGenerationJobEntity Entity, Func<ConsultGenerationJobState> State) CreateEntity()
    {
        var entity = new ConsultGenerationJobEntity(Substitute.For<IConsultGenerationJobIndexStore>(), Substitute.For<IJobOutputsBlobStore>());
        var state = ConsultGenerationJobState.Create(
            "job-1", "user-1",
            new[] { (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(StringComparer.Ordinal) { ["id"] = "b1", ["name"] = "Block one" } });
        StateProperty.SetValue(entity, state);
        return (entity, () => (ConsultGenerationJobState)StateProperty.GetValue(entity)!);
    }

    [Fact]
    public async Task CompleteDocument_StoresTheDeliverable()
    {
        var (entity, state) = CreateEntity();

        await entity.CompleteDocument("## Block one\n\ntext");

        Assert.Equal("## Block one\n\ntext", state().AssembledDocument);
    }

    [Fact]
    public async Task ToResponse_CompletedV6Job_CarriesDocumentAndHashV2()
    {
        var (entity, state) = CreateEntity();
        await entity.CompleteDocument("## Block one\n\ntext");
        state().Status = ConsultGenerationJobStatuses.Completed;

        var response = state().ToResponse();

        Assert.Equal("## Block one\n\ntext", response.AssembledDocument);
        Assert.Equal(2, response.WorkflowOutputHashVersion);
        Assert.Equal(
            ConsultGenerationProvenance.ComputeAssembledDocumentHash("## Block one\n\ntext"),
            response.WorkflowOutputHash);
    }

    [Fact]
    public async Task ToResponse_RunningJob_WithholdsTheDocument()
    {
        var (entity, state) = CreateEntity();
        await entity.CompleteDocument("partial");
        state().Status = ConsultGenerationJobStatuses.Running;

        var response = state().ToResponse();

        Assert.Null(response.AssembledDocument);
        Assert.Null(response.WorkflowOutputHash);
    }

    [Fact]
    public void ToResponse_SeparatesBlocksFromItemProgress()
    {
        var (entity, state) = CreateEntity();
        // The split model (#175): blocks carry the deliverable, item progress
        // carries the prose ticks — neither leaks into the other's response
        // fields, and the total is the block count from the stored scalar.
        state().GetOrAddBlock("b1", "Block one").Status = ConsultGenerationBlockStatuses.Completed;
        state().GetOrAddItemProgress("item-1", "Item one").CompletedStepCount = 2;

        var response = state().ToResponse();

        Assert.Equal(1, response.TotalBlockCount);
        Assert.Equal(1, response.CompletedBlockCount);
        Assert.Equal(new[] { "b1" }, response.GeneratedBlocks.Keys.ToArray());
        Assert.Equal(new[] { "item-1" }, response.ItemProgress!.Keys.ToArray());
    }

    [Fact]
    public void ToResponse_CompletedV5Job_KeepsHashV1()
    {
        var (entity, state) = CreateEntity();
        state().GetOrAddBlock("b1", "Block one").Status = ConsultGenerationBlockStatuses.Completed;
        state().GetOrAddBlock("b1", "Block one").GeneratedText = "text";
        state().Status = ConsultGenerationJobStatuses.Completed;

        var response = state().ToResponse();

        Assert.Null(response.AssembledDocument);
        Assert.Equal(1, response.WorkflowOutputHashVersion);
    }

    [Fact]
    public async Task CompleteResultDocument_OrdersByOrdinalAndUpsertsByResultId()
    {
        var (entity, state) = CreateEntity();

        // Aggregators complete in data-dependent order; a replayed signal for
        // the same result replaces, never duplicates.
        await entity.CompleteResultDocument(new ConsultGenerationResultDocument("patient_letter", "Patient letter", "Letter.", 1));
        await entity.CompleteResultDocument(new ConsultGenerationResultDocument("consult_note", "Consultation note", "Stale.", 0));
        await entity.CompleteResultDocument(new ConsultGenerationResultDocument("consult_note", "Consultation note", "Note.", 0));

        Assert.Equal(7, state().SchemaVersion);
        Assert.Null(state().AssembledDocument);
        Assert.Equal(
            new[] { ("consult_note", "Note."), ("patient_letter", "Letter.") },
            state().AssembledDocuments!.Select(d => (d.ResultId, d.Text)).ToArray());
    }

    [Fact]
    public async Task ToResponse_CompletedV7Job_CarriesDocumentSetAndHashV3()
    {
        var (entity, state) = CreateEntity();
        await entity.CompleteResultDocument(new ConsultGenerationResultDocument("consult_note", "Consultation note", "Note.", 0));
        await entity.CompleteResultDocument(new ConsultGenerationResultDocument("patient_letter", "Patient letter", "Letter.", 1));
        state().Status = ConsultGenerationJobStatuses.Completed;

        var response = state().ToResponse();

        Assert.Null(response.AssembledDocument);
        Assert.Equal(
            new[]
            {
                new ConsultGenerationResultDocumentResponse("consult_note", "Consultation note", "Note.",
                    ConsultGenerationProvenance.Sha256Hex("Note.")),
                new ConsultGenerationResultDocumentResponse("patient_letter", "Patient letter", "Letter.",
                    ConsultGenerationProvenance.Sha256Hex("Letter."))
            },
            response.AssembledDocuments!.ToArray());
        // The per-document digests are exactly what the v3 aggregate covers.
        Assert.Equal(
            ConsultGenerationProvenance.ComputeResultSetHash(
                response.AssembledDocuments.ToDictionary(d => d.ResultId, d => d.Text)),
            response.WorkflowOutputHash);
        Assert.Equal(3, response.WorkflowOutputHashVersion);
        Assert.Equal(
            ConsultGenerationProvenance.ComputeResultSetHash(new Dictionary<string, string>
            {
                ["consult_note"] = "Note.",
                ["patient_letter"] = "Letter."
            }),
            response.WorkflowOutputHash);
    }

    [Fact]
    public async Task ToResponse_RunningV7Job_WithholdsTheDocumentSet()
    {
        var (entity, state) = CreateEntity();
        await entity.CompleteResultDocument(new ConsultGenerationResultDocument("consult_note", "Consultation note", "Partial.", 0));
        state().Status = ConsultGenerationJobStatuses.Running;

        var response = state().ToResponse();

        Assert.Null(response.AssembledDocuments);
        Assert.Null(response.WorkflowOutputHash);
    }
}
