using Consultologist.Web.Services.Provenance.V31;

namespace Consultologist.Web.Tests.V31;

/// <summary>Task 14 (#1509): History's sentences, word for word.</summary>
public class HistorySentences31Tests
{
    [Fact]
    public void TheFixedSentences_AreTheRegistrysWords()
    {
        Assert.Equal("Delivering…", HistorySentences31.Delivering);
        Assert.Equal("Refused, not delivered", HistorySentences31.RefusedHeading);
        Assert.Equal("This job stopped before it finished, so it has no record. Run the workflow again.", HistorySentences31.Stopped);
        Assert.Equal("The text was deleted 7 days after the job finished. Its hash is kept.", HistorySentences31.TextDeleted);
        Assert.Equal("Part of this text comes from run facts, such as the date, so it differs from run to run.", HistorySentences31.RunFactsDiffer);
    }

    [Fact]
    public void Blocked_SaysTheLabelAndTheStoredReasonWithoutItsPrefix()
    {
        Assert.Equal(
            "Consultation note was blocked: check 'coverage' failed (10 of 12 terms present).",
            HistorySentences31.Blocked("Consultation note", "blocked: check 'coverage' failed (10 of 12 terms present)"));
        Assert.Equal(
            "Consultation note was blocked: check 'coverage' could not run (the engine failed).",
            HistorySentences31.Blocked("Consultation note", "blocked: check 'coverage' could not run (the engine failed)"));
    }

    [Fact]
    public void Blocked_WithNoReason_StillSaysItWasBlocked()
    {
        Assert.Equal("Consultation note was blocked.", HistorySentences31.Blocked("Consultation note", null));
    }

    [Fact]
    public void CouldNotSend_NamesTheDestination() =>
        Assert.Equal("Couldn't send to email. Copy the note instead.", HistorySentences31.CouldNotSend("email"));

    [Fact]
    public void RunAgainLater_FollowsTheStoredError() =>
        Assert.Equal(
            "The model service stayed busy for 5 minutes. Run the workflow again later.",
            HistorySentences31.RunAgainLater("The model service stayed busy for 5 minutes."));

    [Fact]
    public void MigratedFrom_NamesTheOldVersion() =>
        Assert.Equal("migrated by the operator from 30", HistorySentences31.MigratedFrom("30"));

    [Fact]
    public void NotSentToTheModel_ListsThePointers() =>
        Assert.Equal(
            "Not sent to the model: /properties/onset/format, /properties/site/format",
            HistorySentences31.NotSentToTheModel(["/properties/onset/format", "/properties/site/format"]));

    [Fact]
    public void ComputedFromModelText_ListsTheReads() =>
        Assert.Equal("computed from model text: node.draft", HistorySentences31.ComputedFromModelText(["node.draft"]));
}
