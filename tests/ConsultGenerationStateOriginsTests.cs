using Consultologist.Api.Jobs;
using Consultologist.Api.Models;

namespace Consultologist.Api.Tests;

/// <summary>
/// #428: provenance per document sits in a field beside the #238-era one,
/// and the response has one map whichever the record holds.
/// </summary>
public class ConsultGenerationStateOriginsTests
{
    private static readonly ConsultInputOrigin Pdf = new(ConsultInputOriginKinds.Document, "pdfpig/0.1.15", 3);
    private static readonly ConsultInputOrigin Docx = new(ConsultInputOriginKinds.Document, "openxml/3.5.1", null, TrackedChangesResolved: true,
        FileSha256: "b6a313365b611c7ec0be83d67237876ae56d4fe5fac3b77e758985551f59037d", TextSha256: "52593837462725201bb86daf11e60f1aee9374ec207aaf234457c4713835032b");

    private static readonly List<IReadOnlyDictionary<string, string>> Items = new()
    {
        new Dictionary<string, string> { ["id"] = "hpi", ["name"] = "HPI" }
    };

    private static ConsultGenerationJobState CreateState() =>
        ConsultGenerationJobState.Create("job-1", "user-1", Items);

    [Fact]
    public void ARecordWithTheSingleOrigin_ProjectsAOneElementList()
    {
        // A job recorded before #428 read one document into the slot; the
        // reader sees it as a list of one, not as nothing.
        var state = CreateState();
        state.InputOrigins = new Dictionary<string, ConsultInputOrigin> { ["consult_draft"] = Pdf };

        var origins = Assert.Contains("consult_draft", state.ToResponse().InputOrigins!);

        Assert.Equal(new[] { Pdf }, origins);
    }

    [Fact]
    public void ARecordWithDocumentOrigins_ProjectsThemInOrder()
    {
        var state = CreateState();
        state.InputDocumentOrigins = new Dictionary<string, List<ConsultInputOrigin>>
        {
            ["prior_notes"] = new() { Pdf, Docx, Pdf }
        };

        var origins = Assert.Contains("prior_notes", state.ToResponse().InputOrigins!);

        Assert.Equal(new[] { Pdf, Docx, Pdf }, origins);
    }

    [Fact]
    public void ARecordWithNeither_ClaimsNothing()
    {
        Assert.Null(CreateState().ToResponse().InputOrigins);
    }

    [Fact]
    public async Task Initialize_StoresDocumentOriginsOnceAndNeverOverwrites()
    {
        var stateProperty = typeof(ConsultGenerationJobEntity)
            .GetProperty("State", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!;
        var entity = new ConsultGenerationJobEntity(NSubstitute.Substitute.For<IConsultGenerationJobIndexStore>(), NSubstitute.Substitute.For<IJobOutputsBlobStore>(), NSubstitute.Substitute.For<IJobInputsBlobStore>());

        await entity.Initialize(new ConsultGenerationJobInitialize(
            "job-1", "user-1", Items,
            InputDocumentOrigins: new Dictionary<string, IReadOnlyList<ConsultInputOrigin>> { ["prior_notes"] = new[] { Pdf, Docx } }));
        await entity.Initialize(new ConsultGenerationJobInitialize(
            "job-1", "user-1", Items,
            InputDocumentOrigins: new Dictionary<string, IReadOnlyList<ConsultInputOrigin>> { ["prior_notes"] = new[] { Docx } }));

        var state = (ConsultGenerationJobState)stateProperty.GetValue(entity)!;

        Assert.Equal(new[] { Pdf, Docx }, state.InputDocumentOrigins!["prior_notes"]);
        Assert.Null(state.InputOrigins);
    }
}
