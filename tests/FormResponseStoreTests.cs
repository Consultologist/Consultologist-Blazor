using Consultologist.Api.Forms;
using Consultologist.Api.Jobs;

namespace Consultologist.Api.Tests;

/// <summary>
/// #539: the naming and keys — the arithmetic, not the plumbing (the blob
/// and table round trips are untested by construction, the house rule).
/// </summary>
public class FormResponseStoreTests
{
    [Theory]
    [InlineData("organisation", "org-form-responses")]
    [InlineData("personal", "personal-form-responses")]
    [InlineData(null, "personal-form-responses")] // #517's own default
    public void TheContainer_FollowsTheAccountsKind(string? kind, string expected) =>
        Assert.Equal(expected, FormResponseBlobStore.ContainerFor(kind));

    [Fact]
    public void TheBlobName_IsTheRecordedGrammar()
    {
        // storage-separation.md § 2.1: {appUserId}/{formId}-{responseId}.json
        Assert.Equal("user-1/triage-intake-17.json", FormResponseBlobStore.NameFor("user-1", "triage-intake", "17"));
    }

    [Fact]
    public void TheRowKey_IsFormColonResponse()
    {
        Assert.Equal("triage-intake:17", TableFormResponseStore.RowKeyFor("triage-intake", "17"));
    }

    [Fact]
    public void AResponseIsDue_WhenSubmittedBeforeTheCutoff_AndStillResting()
    {
        var cutoff = new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);
        FormResponseRow Row(DateTimeOffset submitted, DateTimeOffset? deleted = null) =>
            new("user-1", "triage-intake", "17", submitted, new[] { "consult_draft" }, "org-form-responses", "user-1/triage-intake-17.json", deleted);

        Assert.True(TextRetentionSweep.IsResponseDue(Row(cutoff.AddDays(-1)), cutoff));
        Assert.False(TextRetentionSweep.IsResponseDue(Row(cutoff.AddHours(1)), cutoff));
        // Already swept (or discarded): never dropped twice.
        Assert.False(TextRetentionSweep.IsResponseDue(Row(cutoff.AddDays(-1), deleted: cutoff.AddDays(-1)), cutoff));
    }
}
