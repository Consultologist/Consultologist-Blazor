using System.Reflection;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using NSubstitute;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Tests;

/// <summary>
/// #434: a job created already Failed because no deliverable applied. Born
/// terminal in one operation — nothing ran, nothing was spent, and the record
/// says so in its own field rather than by the shape of its absences.
/// </summary>
public class ConsultGenerationStartFailureRecordTests
{
    private static readonly PropertyInfo StateProperty = typeof(ConsultGenerationJobEntity)
        .GetProperty("State", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;

    private static ConsultGenerationJobState StateOf(ConsultGenerationJobEntity entity) =>
        (ConsultGenerationJobState)StateProperty.GetValue(entity)!;

    private const string Reason = "No document applies to these inputs. 'Patient letter' wanted billable = true; it was not supplied.";

    private static ConsultGenerationJobStartFailure Failure() => new(
        new ConsultGenerationJobInitialize(
            "job-1",
            "user-1",
            Array.Empty<IReadOnlyDictionary<string, string>>(),
            "general@v2026.08.1",
            "hash",
            EffectiveInputHashVersion: 4,
            CatalogRef: "output-contracts@v2026.07.2",
            Source: ConsultGenerationJobSources.Email,
            SkippedDocuments: new[] { new ConsultSkippedDocument("patient_letter", "Patient letter", "wanted billable = true; it was not supplied") },
            PackageSpecVersion: 8,
            PackageTitle: "Breast oncology consults",
            PackageTags: new[] { "oncology" },
            PackageFormatRef: "package-format@v2026.08.6",
            ProvenanceRef: "provenance@v2026.08.4"),
        Reason);

    [Fact]
    public async Task RecordStartFailure_IsBornTerminal_InOneOperation()
    {
        var index = Substitute.For<IConsultGenerationJobIndexStore>();
        var entity = new ConsultGenerationJobEntity(index);

        await entity.RecordStartFailure(Failure());

        var state = StateOf(entity);
        Assert.Equal(ConsultGenerationJobStatuses.Failed, state.Status);
        Assert.Equal(Reason, state.StartFailure);
        Assert.Null(state.FailureError);
        Assert.NotNull(state.CompletedAtUtc);
        Assert.Null(state.StartedAtUtc);
        Assert.True(ConsultGenerationJobEntity.IsTerminal(state.Status));
        // Its storage shape is the current one, as a job that ran would show.
        Assert.Equal(7, state.SchemaVersion);
        Assert.Single(state.History);
        Assert.Equal(("failure", "No document applies", Reason), (state.History[0].Kind, state.History[0].Label, state.History[0].Detail));

        // One write, and it already says what the row is.
        await index.Received(1).UpsertAsync(
            Arg.Is<ConsultGenerationJobIndexEntry>(entry =>
                entry.JobId == "job-1" && entry.Status == ConsultGenerationJobStatuses.Failed && entry.FailedAtStart && entry.TotalBlockCount == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheRecordCarriesProvenance_AndAStatedZero()
    {
        var entity = new ConsultGenerationJobEntity(Substitute.For<IConsultGenerationJobIndexStore>());

        await entity.RecordStartFailure(Failure());

        var response = StateOf(entity).ToResponse();
        Assert.Equal(0, response.TotalBlockCount);
        Assert.Empty(response.GeneratedBlocks);
        Assert.Equal(Reason, response.StartFailure);
        Assert.Null(response.RuntimeFailureError);
        Assert.Equal("general@v2026.08.1", response.WorkflowPackage);
        Assert.Equal("hash", response.EffectiveInputHash);
        Assert.Equal(4, response.EffectiveInputHashVersion);
        Assert.Equal("output-contracts@v2026.07.2", response.CatalogRef);
        Assert.Equal(8, response.PackageSpecVersion);
        Assert.Equal("Breast oncology consults", response.PackageTitle);
        Assert.Equal(new[] { "oncology" }, response.PackageTags);
        // #398: a born-Failed record names its rules too.
        Assert.Equal("package-format@v2026.08.6", response.PackageFormatRef);
        Assert.Equal("provenance@v2026.08.4", response.ProvenanceRef);
        Assert.Equal(ConsultGenerationJobSources.Email, response.Source);
        Assert.Equal("Patient letter", Assert.Single(response.SkippedDocuments!).Label);
        Assert.Null(response.Nodes);
        Assert.Null(response.ItemSteps);
    }

    [Fact]
    public async Task AJobThatStarted_HasNoStartFailure_AndTheIndexSaysSo()
    {
        var index = Substitute.For<IConsultGenerationJobIndexStore>();
        var entity = new ConsultGenerationJobEntity(index);

        await entity.Initialize(new ConsultGenerationJobInitialize(
            "job-2", "user-1", new[] { (IReadOnlyDictionary<string, string>)new Dictionary<string, string> { ["id"] = "hpi", ["name"] = "HPI" } }));

        Assert.Null(StateOf(entity).StartFailure);
        Assert.Null(StateOf(entity).ToResponse().StartFailure);
        await index.Received(1).UpsertAsync(Arg.Is<ConsultGenerationJobIndexEntry>(entry => !entry.FailedAtStart), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NothingMovesItAfterwards()
    {
        // Terminal is terminal (#202): a stray MarkRunning must not resurrect
        // a row that says nothing ran.
        var entity = new ConsultGenerationJobEntity(Substitute.For<IConsultGenerationJobIndexStore>());
        await entity.RecordStartFailure(Failure());

        await entity.MarkRunning();

        Assert.Equal(ConsultGenerationJobStatuses.Failed, StateOf(entity).Status);
        Assert.Null(StateOf(entity).StartedAtUtc);
    }
}
