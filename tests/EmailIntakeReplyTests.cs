using Consultologist.Api.Email;
using Consultologist.Api.Models;

namespace Consultologist.Api.Tests;

public class EmailIntakeReplyTests
{
    [Fact]
    public void Compose_Completed_HasFixedSubjectAndDeepLink()
    {
        var (subject, body) = EmailIntakeReply.Compose("https://app.example.com", "job-1", "Completed");

        Assert.Equal("Your consult is ready", subject);
        Assert.Contains("https://app.example.com/history/job-1", body);
        Assert.Contains("no clinical content", body);
    }

    [Fact]
    public void Compose_NamesADeliverableThatDidNotFire()
    {
        // #315: one fewer attachment with no explanation reads as a document
        // that failed. The label and the reason are authored package content
        // and declared values, so this stays within the no-clinical-content
        // rule the reply is built around.
        var (_, body) = EmailIntakeReply.Compose(
            "https://app.example.com", "job-1", "Completed",
            new[] { "Consultation note" },
            skippedDocuments: new[]
            {
                new ConsultSkippedDocument("billing_summary", "Billing summary",
                    "needs billable to be true; it is 'false'")
            });

        Assert.Contains("Billing summary was not produced", body);
        Assert.Contains("needs billable to be true", body);
        Assert.Contains("no clinical content", body);
    }

    [Fact]
    public void Compose_WithNothingSkipped_IsUnchanged()
    {
        var (_, withNone) = EmailIntakeReply.Compose(
            "https://app.example.com", "job-1", "Completed", new[] { "Consultation note" });
        var (_, withEmpty) = EmailIntakeReply.Compose(
            "https://app.example.com", "job-1", "Completed", new[] { "Consultation note" },
            skippedDocuments: Array.Empty<ConsultSkippedDocument>());

        Assert.Equal(withNone, withEmpty);
        Assert.DoesNotContain("was not produced", withNone);
    }

    [Fact]
    public void Compose_Failed_HasFixedSubjectAndDeepLink()
    {
        var (subject, body) = EmailIntakeReply.Compose("https://app.example.com", "job-1", "Failed");

        Assert.Equal("Your consult run did not complete", subject);
        Assert.Contains("https://app.example.com/history/job-1", body);
    }

    [Fact]
    public void Compose_TrailingSlashBaseUrl_ProducesCleanLink()
    {
        var (_, body) = EmailIntakeReply.Compose("https://app.example.com/", "job-1", "Completed");

        Assert.Contains("https://app.example.com/history/job-1", body);
        Assert.DoesNotContain("com//history", body);
    }

    [Fact]
    public void Compose_WithAttachment_MentionsTheEncryptedDocument()
    {
        var (subject, body) = EmailIntakeReply.Compose(
            "https://app.example.com", "job-1", "Completed", new[] { "Consultation note" });

        Assert.Equal("Your consult is ready", subject);
        Assert.Contains("The consult document is attached", body);
        Assert.Contains("encrypted with your delivery password", body);
        Assert.Contains("https://app.example.com/history/job-1", body);
    }

    [Fact]
    public void Compose_WithSeveralAttachments_NamesEachDocument()
    {
        // Authored package labels are never patient data, so the body names
        // them: the recipient can see the set is complete before decrypting.
        var (subject, body) = EmailIntakeReply.Compose(
            "https://app.example.com", "job-1", "Completed",
            new[] { "Consultation note", "Patient letter" });

        Assert.Equal("Your consult is ready", subject);
        Assert.Contains("Consultation note, Patient letter are attached", body);
        Assert.Contains("encrypted with your delivery password", body);
    }

    [Fact]
    public void Compose_OmittedForSize_SaysSoWithoutClaimingAnAttachment()
    {
        var (subject, body) = EmailIntakeReply.Compose(
            "https://app.example.com", "job-1", "Completed", Array.Empty<string>(), omittedForSize: true);

        Assert.Equal("Your consult is ready", subject);
        Assert.Contains("too large to send by email", body);
        Assert.Contains("https://app.example.com/history/job-1", body);
        // The no-attachment pin below must keep holding for this branch too.
        Assert.DoesNotContain("attached", body);
    }

    [Fact]
    public void Compose_WithoutAttachment_DoesNotMentionOne()
    {
        var (_, body) = EmailIntakeReply.Compose("https://app.example.com", "job-1", "Completed");

        Assert.DoesNotContain("attached", body);
    }

    [Fact]
    public void Compose_NeverEchoesCallerContent()
    {
        // The only caller-varying inputs are the base URL and job id; a hostile
        // job id must appear only inside the link, and inbound subject/body
        // have no path into Compose at all.
        var hostile = "job<script>alert(1)</script>";
        var (subject, body) = EmailIntakeReply.Compose("https://app.example.com", hostile, "Completed");

        Assert.Equal("Your consult is ready", subject);
        Assert.Contains($"/history/{hostile}", body);
    }
}
