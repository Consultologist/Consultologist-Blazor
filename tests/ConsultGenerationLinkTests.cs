using Consultologist.Api.Jobs;
using Consultologist.Api.Models;

namespace Consultologist.Api.Tests;

/// <summary>
/// #546: the lineage edges a start creates, and the keys that keep them.
/// Ids only, never text; one row per copied element, exactly one per replay.
/// </summary>
public class ConsultGenerationLinkTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PreviousRunOrigins_BecomeOneRowPerCopiedElement()
    {
        // One slot copying twice from the SAME source — both rows must
        // survive, which is what the element index in the key is for.
        var origins = new Dictionary<string, IReadOnlyList<ConsultInputOrigin>>
        {
            ["referrals"] = new[]
            {
                new ConsultInputOrigin("previous-run", TextSha256: "aa", SourceJobId: "source-1", SourceResultId: "note"),
                new ConsultInputOrigin("previous-run", TextSha256: "bb", SourceJobId: "source-1", SourceResultId: "letter")
            },
            ["consult_draft"] = new[]
            {
                new ConsultInputOrigin("document", Extractor: "pandoc/3", TextSha256: "cc")
            }
        };

        var links = ConsultGenerationJobStarter.LinksFrom("consumer-1", "user-1", origins, rerunOfJobId: null, Now);

        Assert.Equal(2, links.Count);
        Assert.Equal(new ConsultGenerationLink("source-1", "consumer-1", "previous-run", "referrals", "note", "user-1", Now, 0), links[0]);
        Assert.Equal(new ConsultGenerationLink("source-1", "consumer-1", "previous-run", "referrals", "letter", "user-1", Now, 1), links[1]);
        // The two rows keep distinct keys.
        Assert.NotEqual(
            TableConsultGenerationLinkStore.RowKeyFor(links[0]),
            TableConsultGenerationLinkStore.RowKeyFor(links[1]));
    }

    [Fact]
    public void ARerun_IsOneEdge_NeverOnePerSlotOrigin()
    {
        // The rerun stamps a rerun origin on EVERY effective slot; the edge
        // is still one replay of one run.
        var origins = new Dictionary<string, IReadOnlyList<ConsultInputOrigin>>
        {
            ["consult_draft"] = new[] { new ConsultInputOrigin("rerun", TextSha256: "aa", SourceJobId: "source-1") },
            ["billable"] = new[] { new ConsultInputOrigin("rerun", TextSha256: "bb", SourceJobId: "source-1") }
        };

        var links = ConsultGenerationJobStarter.LinksFrom("consumer-1", "user-1", origins, rerunOfJobId: "source-1", Now);

        var link = Assert.Single(links);
        Assert.Equal(new ConsultGenerationLink("source-1", "consumer-1", "rerun", null, null, "user-1", Now, 0), link);
        Assert.Equal("consumer-1", TableConsultGenerationLinkStore.RowKeyFor(link));
    }

    [Fact]
    public void APlainRun_CreatesNoEdges()
    {
        var origins = new Dictionary<string, IReadOnlyList<ConsultInputOrigin>>
        {
            ["consult_draft"] = new[] { new ConsultInputOrigin("document", Extractor: "pandoc/3") }
        };

        Assert.Empty(ConsultGenerationJobStarter.LinksFrom("consumer-1", "user-1", origins, null, Now));
        Assert.Empty(ConsultGenerationJobStarter.LinksFrom("consumer-1", "user-1", origins: null, null, Now));
    }

    [Fact]
    public void ThePreviousRunKey_CarriesSlotAndElement()
    {
        var link = new ConsultGenerationLink("source-1", "consumer-1", "previous-run", "referrals", "note", "user-1", Now, 1);

        Assert.Equal("consumer-1_referrals_1", TableConsultGenerationLinkStore.RowKeyFor(link));
    }

    [Fact]
    public void TheWireProjection_CarriesIdsAndWords_NeverTheAccount()
    {
        var links = new[]
        {
            new ConsultGenerationLink("source-1", "consumer-1", "previous-run", "referrals", "note", "user-1", Now, 0),
            new ConsultGenerationLink("source-1", "consumer-2", "rerun", null, null, "user-1", Now)
        };

        var usedBy = ConsultGenerationJobs.UsedByFrom(links);

        Assert.Equal(new ConsultJobLinkResponse("consumer-1", "previous-run", "referrals", "note"), usedBy[0]);
        Assert.Equal(new ConsultJobLinkResponse("consumer-2", "rerun"), usedBy[1]);
    }
}
