using Consultologist.Web.Services.AI;

namespace Consultologist.Web.Tests;

/// <summary>#1303 (v29 design § 1.3–§ 1.4): the shared words for a loop whose count is not yet known.</summary>
public class LoopCountTests
{
    [Fact]
    public void AResolvedLoop_ReTotals_AndIsNoLongerPending()
    {
        var pending = new HashSet<string>(StringComparer.Ordinal) { "problems", "concepts" };

        var (total, next) = LoopCount.Apply(pending, "problems", 5);

        Assert.Equal(5, total);
        Assert.Equal(new[] { "concepts" }, next);
        Assert.Equal(2, pending.Count); // the old set is not changed underneath the page
    }

    [Theory]
    [InlineData(5, false, "5")]
    [InlineData(5, true, "5+")]
    public void TheTotal_IsAFloor_WhileALoopIsPending(int total, bool pending, string expected) =>
        Assert.Equal(expected, LoopCount.Total(total, pending));

    [Fact]
    public void OnlyANodeValuedLoopWithNoCount_IsUnknown()
    {
        Assert.True(LoopCount.IsUnknown(new ConsultGenerationNodeDescriptor("problems", "Problems", Kind: "for", Collection: "node.find_problems.value.problems")));
        Assert.False(LoopCount.IsUnknown(new ConsultGenerationNodeDescriptor("problems", "Problems", Kind: "for", Collection: "node.find_problems.value.problems", Count: 2)));
        Assert.False(LoopCount.IsUnknown(new ConsultGenerationNodeDescriptor("sections", "Sections", Kind: "for", Collection: "snippet.sections")));
        Assert.Equal("counting…", LoopCount.Describe(finished: false));
        Assert.Equal("count never known", LoopCount.Describe(finished: true));
    }

    [Fact]
    public void AnInstancesCount_ReTotals_ButTheLoopStaysPending_UntilTheSnapshotSaysSo()
    {
        // v30 (#1345): a loop inside a loop settles per instance — another may yet open.
        var pending = new HashSet<string>(StringComparer.Ordinal) { "paragraphs" };

        var (total, next) = LoopCount.Apply(pending, "paragraphs", 7, "1");

        Assert.Equal(7, total);
        Assert.Equal(new[] { "paragraphs" }, next);
        Assert.Equal(new[] { "problems" }.Length, LoopCount.Apply(new HashSet<string> { "problems", "x" }, "problems", 3, null).Pending.Count);
    }

    [Fact]
    public void ASettledCount_IsKeptByTheLoop_OrByTheLoopsInstance()
    {
        Assert.Equal("problems", LoopCount.SettledKey("problems", null));
        Assert.Equal("paragraphs:1", LoopCount.SettledKey("paragraphs", "1"));
        Assert.Equal("sentences:1/2", LoopCount.SettledKey("sentences", "1/2"));
    }
}
