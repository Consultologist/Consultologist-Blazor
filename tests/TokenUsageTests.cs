// The Responses SDK marks its types evaluation-only; the Api project
// suppresses OPENAI001 project-wide for the same reason.
#pragma warning disable OPENAI001
using System.ClientModel.Primitives;
using Consultologist.Api.Agents;
using Consultologist.Api.Models;
using OpenAI.Responses;

namespace Consultologist.Api.Tests;

/// <summary>
/// #551: the capture seam — the provider's usage object becomes the record's
/// counts, and absence stays absence, never zero.
/// </summary>
public class TokenUsageTests
{
    [Fact]
    public void TheProvidersCounts_MapInputToInput_OutputToOutput()
    {
        // The SDK type has no public constructor; its own wire reader builds
        // it from the Responses contract's required shape.
        var usage = ModelReaderWriter.Read<ResponseTokenUsage>(BinaryData.FromString("""
            {"input_tokens":1234,"output_tokens":567,"total_tokens":1801,
             "input_tokens_details":{"cached_tokens":0},
             "output_tokens_details":{"reasoning_tokens":0}}
            """))!;

        Assert.Equal(new ConsultTokenUsage(1234, 567), AgentSectionGenerator.UsageOf(usage));
    }

    [Fact]
    public void NoUsage_IsNull_NeverZero()
    {
        Assert.Null(AgentSectionGenerator.UsageOf(null));
    }

    // ----- the record: per stage, and the total stamped once -----

    private static readonly System.Reflection.PropertyInfo StateProperty =
        typeof(Consultologist.Api.Jobs.ConsultGenerationJobEntity)
            .GetProperty("State", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!;

    private static (Consultologist.Api.Jobs.ConsultGenerationJobEntity Entity, Func<Consultologist.Api.Jobs.ConsultGenerationJobState> State) Entity()
    {
        var entity = new Consultologist.Api.Jobs.ConsultGenerationJobEntity(
            NSubstitute.Substitute.For<Consultologist.Api.Jobs.IConsultGenerationJobIndexStore>(),
            NSubstitute.Substitute.For<Consultologist.Api.Jobs.IJobOutputsBlobStore>(),
            NSubstitute.Substitute.For<Consultologist.Api.Jobs.IJobInputsBlobStore>());
        StateProperty.SetValue(entity, Consultologist.Api.Jobs.ConsultGenerationJobState.Create("job-1", "user-1", new[]
        {
            new Dictionary<string, string> { ["id"] = "note:draft", ["name"] = "Consultation note" }
        }));
        return (entity, () => (Consultologist.Api.Jobs.ConsultGenerationJobState)StateProperty.GetValue(entity)!);
    }

    [Fact]
    public async Task EveryModelStageRecordsItsCost_AndTheTotalSumsThemOnce()
    {
        var (entity, state) = Entity();

        // Two fanned items with usage, the forEach roll-up without (no model
        // ran at node level), an aggregate without — the exact shape a
        // fan-heavy run has.
        entity.MarkNodeItemCompleted(new Consultologist.Api.Jobs.ConsultGenerationNodeItemUpdate(
            "section", "Sections", "hpi", "HPI", null, "in1", "out1", 1, 2, 5, new ConsultTokenUsage(1000, 300)));
        entity.MarkNodeItemCompleted(new Consultologist.Api.Jobs.ConsultGenerationNodeItemUpdate(
            "section", "Sections", "plan", "Plan", null, "in2", "out2", 2, 2, 5, new ConsultTokenUsage(1100, 350)));
        entity.MarkNodeCompleted(new Consultologist.Api.Jobs.ConsultGenerationNodeUpdate(
            "section", "Sections", null, null, null, 1, 2));
        entity.MarkNodeCompleted(new Consultologist.Api.Jobs.ConsultGenerationNodeUpdate(
            "assemble", "Assemble", null, "inA", "outA", 2, 2, 5));

        await entity.CompleteBlock(new BlockGenerationResult("note:draft", "Consultation note", true, "Consultation note", null));
        await entity.CompleteResultDocument(new Consultologist.Api.Jobs.ConsultGenerationResultDocument("note", "Consultation note", "Consultation note", 0));
        await entity.FinalizeJob(new Consultologist.Api.Jobs.ConsultGenerationJobFinalize(Consultologist.Api.Jobs.ConsultGenerationJobStatuses.Completed));

        var s = state();
        Assert.Equal(new ConsultTokenUsage(1000, 300), s.NodeOutputs!["section:hpi"].Tokens);
        Assert.Null(s.NodeOutputs["section"].Tokens);
        Assert.Null(s.NodeOutputs["assemble"].Tokens);
        // The total is the items' sum alone — roll-up and aggregate add nothing.
        Assert.Equal(new ConsultTokenUsage(2100, 650), s.Tokens);

        var response = s.ToResponse();
        Assert.Equal(new ConsultTokenUsage(2100, 650), response.Tokens);
        Assert.Equal(new ConsultTokenUsage(1100, 350), response.NodeOutputs!["section:plan"].Tokens);
        Assert.Null(response.NodeOutputs["assemble"].Tokens);
    }

    [Fact]
    public async Task ARunThatRecordedNoUsage_StampsNothing_NeverZero()
    {
        var (entity, state) = Entity();
        entity.MarkNodeCompleted(new Consultologist.Api.Jobs.ConsultGenerationNodeUpdate(
            "assemble", "Assemble", null, "inA", "outA", 1, 1, 5));
        await entity.CompleteBlock(new BlockGenerationResult("note:draft", "Consultation note", true, "Consultation note", null));
        await entity.CompleteResultDocument(new Consultologist.Api.Jobs.ConsultGenerationResultDocument("note", "Consultation note", "Consultation note", 0));

        await entity.FinalizeJob(new Consultologist.Api.Jobs.ConsultGenerationJobFinalize(Consultologist.Api.Jobs.ConsultGenerationJobStatuses.Completed));

        Assert.Null(state().Tokens);
        Assert.Null(state().ToResponse().Tokens);
    }

    [Fact]
    public async Task AFailedRun_StillTotalsWhatItSpent()
    {
        var (entity, state) = Entity();
        entity.MarkNodeCompleted(new Consultologist.Api.Jobs.ConsultGenerationNodeUpdate(
            "extract", "Extract", null, "in1", "out1", 1, 2, 5, null, new ConsultTokenUsage(900, 200)));

        await entity.FinalizeJob(new Consultologist.Api.Jobs.ConsultGenerationJobFinalize(Consultologist.Api.Jobs.ConsultGenerationJobStatuses.Failed, "boom"));

        Assert.Equal(new ConsultTokenUsage(900, 200), state().Tokens);
    }

    [Fact]
    public async Task TheRetentionDrop_LeavesTheCountsStanding()
    {
        var (entity, state) = Entity();
        entity.MarkNodeCompleted(new Consultologist.Api.Jobs.ConsultGenerationNodeUpdate(
            "extract", "Extract", null, "in1", "out1", 1, 1, 5, null, new ConsultTokenUsage(900, 200)));
        await entity.CompleteBlock(new BlockGenerationResult("note:draft", "Consultation note", true, "Consultation note", null));
        await entity.CompleteResultDocument(new Consultologist.Api.Jobs.ConsultGenerationResultDocument("note", "Consultation note", "Consultation note", 0));
        await entity.FinalizeJob(new Consultologist.Api.Jobs.ConsultGenerationJobFinalize(Consultologist.Api.Jobs.ConsultGenerationJobStatuses.Completed));

        await entity.DropText(new Consultologist.Api.Jobs.ConsultGenerationTextDrop(DateTimeOffset.UtcNow));

        // Numbers, not text: what #552's usage store depends on.
        Assert.Equal(new ConsultTokenUsage(900, 200), state().Tokens);
        Assert.Equal(new ConsultTokenUsage(900, 200), state().NodeOutputs!["extract"].Tokens);
    }
}
