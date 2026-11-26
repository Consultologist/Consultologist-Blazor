using System.Text.Json;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Provenance.V31;

namespace Consultologist.Web.Tests.V31;

/// <summary>Task 14 (#1509; part 09 § 6.3): the banners at the top of History, one per refusal.</summary>
public class Banners31Tests
{
    private static RecordRowRef Raw(string key, string section, string json) =>
        new(key, section, JsonDocument.Parse(json).RootElement.Clone());

    private static Record31 Record(params RecordRowRef[] rows) =>
        Record31.From(Record31Fixtures.Header(), rows);

    private static RecordRowRef Deliverable(string key, string state, string? reason, string? label = null) =>
        Raw(key, "deliverable", JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["key"] = key, ["state"] = state, ["stateReason"] = reason, ["label"] = label,
        }));

    private static RecordRowRef Delivery(string deliverable, string kind, string result, string? reason) =>
        Raw($"delivery:{deliverable}/{kind}", "delivery", JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["key"] = $"delivery:{deliverable}/{kind}", ["deliverable"] = deliverable, ["kind"] = kind, ["result"] = result, ["reason"] = reason,
        }));

    [Fact]
    public void ABlockedDeliverable_SaysItWasBlocked_WithTheChecksStoredWords()
    {
        var banners = Banners31.Of(Record(Deliverable("deliverable.note", "blocked", "blocked: check 'coverage' failed (10 of 12 terms present)", "Consultation note")));

        var banner = Assert.Single(banners);
        Assert.Equal("blocked", banner.Kind);
        Assert.Equal("Consultation note was blocked: check 'coverage' failed (10 of 12 terms present).", banner.Text);
        Assert.Equal("deliverable.note", banner.LinkKey);
    }

    [Fact]
    public void ABlockedDeliverable_WhoseCheckCouldNotRun_SaysSo()
    {
        var banner = Assert.Single(Banners31.Of(Record(Deliverable("deliverable.note", "blocked", "blocked: check 'coverage' could not run (the engine failed)", "Consultation note"))));

        Assert.Equal("Consultation note was blocked: check 'coverage' could not run (the engine failed).", banner.Text);
    }

    [Fact]
    public void AHeldDeliverable_SaysItWasHeld_WithTheStoredReason()
    {
        var banner = Assert.Single(Banners31.Of(Record(Deliverable("deliverable.summary", "held", "held: empty", "Summary"))));

        Assert.Equal("held", banner.Kind);
        Assert.Equal("Summary was held: empty", banner.Text);
        Assert.Equal("deliverable.summary", banner.LinkKey);
    }

    [Fact]
    public void AFailedDeliverable_SaysItFailed_WithTheStoredReason()
    {
        var banner = Assert.Single(Banners31.Of(Record(Deliverable("deliverable.note", "failed", "failed: the job didn't finish within 30 minutes", "Consultation note"))));

        Assert.Equal("failed", banner.Kind);
        Assert.Equal("Consultation note failed: the job didn't finish within 30 minutes", banner.Text);
    }

    [Fact]
    public void ANotSentDelivery_SaysTheLabelWasTheStoredReason()
    {
        var banner = Assert.Single(Banners31.Of(Record(
            Deliverable("deliverable.note", "released", null, "Consultation note"),
            Delivery("deliverable.note", "emr", "not sent", "not sent: EMR isn't set up for this account"))));

        Assert.Equal("not-sent", banner.Kind);
        Assert.Equal("Consultation note was not sent: EMR isn't set up for this account", banner.Text);
        Assert.Equal("delivery:deliverable.note/emr", banner.LinkKey);
    }

    [Fact]
    public void AFailedDelivery_SaysCouldNotSend_ToTheDestinationAsTheEngineNamesIt()
    {
        var banner = Assert.Single(Banners31.Of(Record(
            Deliverable("deliverable.note", "released", null, "Consultation note"),
            Delivery("deliverable.note", "email", "failed", "failed: timeout"))));

        Assert.Equal("delivery-failed", banner.Kind);
        Assert.Equal("Couldn't send to Email. Copy the note instead.", banner.Text);
        Assert.Equal("delivery:deliverable.note/email", banner.LinkKey);
    }

    [Fact]
    public void AReleasedDeliverable_WhoseEveryDestinationWasSent_HasNoBanner()
    {
        Assert.Empty(Banners31.Of(Record(
            Deliverable("deliverable.note", "released", null, "Consultation note"),
            Delivery("deliverable.note", "email", "sent", null))));
    }

    [Fact]
    public void ASkippedDeliverable_HasNoBanner()
    {
        Assert.Empty(Banners31.Of(Record(Deliverable("deliverable.letter", "skipped", "skipped: input.kind.value = \"letter\" is false", "Letter"))));
    }

    [Fact]
    public void ALabelIsNeverReplacedByAKey_UnlessTheRowHasNone()
    {
        var withLabel = Assert.Single(Banners31.Of(Record(Deliverable("deliverable.note", "held", "held: empty", "Consultation note"))));
        var without = Assert.Single(Banners31.Of(Record(Deliverable("deliverable.note", "held", "held: empty"))));

        Assert.StartsWith("Consultation note ", withLabel.Text);
        Assert.StartsWith("deliverable.note ", without.Text);
    }

    [Fact]
    public void TheFixtureJob_HasABannerForEachRefusal_InDeliverableThenDeliveryOrder()
    {
        var job = Record31Fixtures.Job();

        var banners = Banners31.Of(Record31.From(job.Header, job.Rows));

        Assert.Equal(
            ["delivery:deliverable.emr.note/emr", "delivery:deliverable.emr.note/pdf", "deliverable.failed_note", "deliverable.held_summary", "deliverable.refused"],
            banners.Select(b => b.LinkKey ?? "").ToArray());
        Assert.Equal(["not-sent", "delivery-failed", "failed", "held", "blocked"], banners.Select(b => b.Kind).ToArray());
        Assert.Equal("EMR note was not sent: EMR isn't set up for this account", banners[0].Text);
        Assert.Equal("Refused note was blocked: check 'gate' failed (The plan is not complete.).", banners[4].Text);
    }
}
