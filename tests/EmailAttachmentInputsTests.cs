using Consultologist.Api.Email;
using Consultologist.Api.Workflow;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Tests;

/// <summary>
/// Where an email's body and attachments land (#210). The rule refuses
/// ambiguity rather than guessing, because a no-PHI reply can never tell the
/// sender which file went where.
/// </summary>
public class EmailAttachmentInputsTests
{
    private static readonly WorkflowInputSpec[] TwoSlots =
    {
        new("consult_draft", "Consult draft"),
        new("prior_notes", "Prior notes", Required: false)
    };

    // #428: the same two slots with prior_notes declared an array of text —
    // the one kind of slot that takes several documents.
    private static readonly WorkflowInputSpec[] NotesAsArray =
    {
        new("consult_draft", "Consult draft"),
        new("prior_notes", "Prior notes", Required: false, Type: WorkflowInputTypes.Array, Items: WorkflowInputTypes.Text)
    };

    // #237: the bytes are never read here — the parser reads them at job
    // start. The text is only so a reader can see what a fixture stands for.
    private static EmailInputAttachment File(string name, string text) =>
        new(name, "text/plain", System.Text.Encoding.UTF8.GetBytes(text));

    private static EmailAttachmentInputs.Resolution Resolve(
        IReadOnlyList<WorkflowInputSpec> slots,
        string? body,
        params EmailInputAttachment[] attachments) =>
        EmailAttachmentInputs.Resolve(slots, body, attachments);

    [Fact]
    public void BodyOnly_FillsTheDraftSlot()
    {
        var result = Resolve(TwoSlots, "Referral body.");

        Assert.Null(result.RejectReason);
        Assert.Equal(new Dictionary<string, string> { ["consult_draft"] = "Referral body." }, result.Inputs);
        Assert.Empty(result.Files!);
    }

    [Fact]
    public void FilenameStem_ClaimsItsSlotRegardlessOfOrder()
    {
        var result = Resolve(TwoSlots, "Referral body.", File("prior_notes.txt", "Old records."));

        Assert.Null(result.RejectReason);
        Assert.Equal(new Dictionary<string, string> { ["consult_draft"] = "Referral body." }, result.Inputs);
        Assert.Equal("prior_notes.txt", result.Files!["prior_notes"].Single().FileName);
    }

    [Fact]
    public void StemMatch_IsCaseInsensitiveAndIgnoresExtension()
    {
        var result = Resolve(TwoSlots, "Body.", File("Prior_Notes.MD", "Records."));

        Assert.Null(result.RejectReason);
        Assert.Equal("Prior_Notes.MD", result.Files!["prior_notes"].Single().FileName);
    }

    [Fact]
    public void NamedAttachment_OutranksTheBodyForItsSlot()
    {
        // Most mail clients append a signature, so a sender who types nothing
        // still produces a body. It must not compete with a file they
        // deliberately named — this combination used to reject outright.
        var result = Resolve(
            TwoSlots,
            "Dr. Lee | Oncology | Clinic",
            File("consult_draft.txt", "The referral."),
            File("prior_notes.txt", "Old records."));

        Assert.Null(result.RejectReason);
        Assert.Equal("consult_draft.txt", result.Files!["consult_draft"].Single().FileName);
        Assert.Equal("prior_notes.txt", result.Files["prior_notes"].Single().FileName);
        // The body lost the slot it would otherwise have taken.
        Assert.Empty(result.Inputs!);
    }

    [Fact]
    public void NamedDraftAttachment_WinsEvenWhenItIsTheOnlyFile()
    {
        var result = Resolve(TwoSlots, "Please see the attached referral.", File("consult_draft.md", "The referral."));

        Assert.Null(result.RejectReason);
        Assert.Equal("consult_draft.md", result.Files!["consult_draft"].Single().FileName);
        Assert.False(result.Files.ContainsKey("prior_notes"));
        Assert.Empty(result.Inputs!);
    }

    [Fact]
    public void OneUnnamedAttachment_FillsTheOneFreeSlot()
    {
        // The ordinary case: body is the referral, the file is whatever else
        // the package asked for.
        var result = Resolve(TwoSlots, "Referral body.", File("scan001.txt", "Old records."));

        Assert.Null(result.RejectReason);
        Assert.Equal("scan001.txt", result.Files!["prior_notes"].Single().FileName);
        Assert.Equal("Referral body.", result.Inputs!["consult_draft"]);
    }

    [Fact]
    public void BlankBody_LetsTheAttachmentBecomeTheDraft()
    {
        // The referral-as-attachment shape, and what a fax bridge will look
        // like once PDFs are readable.
        var result = Resolve(TwoSlots, "   ", File("fax_20260728.txt", "The referral."));

        Assert.Null(result.RejectReason);
        Assert.Equal("fax_20260728.txt", result.Files!["consult_draft"].Single().FileName);
        Assert.False(result.Files.ContainsKey("prior_notes"));
    }

    [Fact]
    public void TwoUnnamedAttachments_AreRefusedRatherThanGuessed()
    {
        // Two files, two free slots, and no way to confirm the assignment back
        // to the sender — a swap here would be silent wrong data.
        var result = Resolve(TwoSlots, null, File("a.txt", "One."), File("b.txt", "Two."));

        Assert.Null(result.Inputs);
        Assert.Contains("Name each file", result.RejectReason);
    }

    [Fact]
    public void MoreAttachmentsThanSlots_AreRefused()
    {
        var result = Resolve(
            TwoSlots,
            "Referral body.",
            File("a.txt", "One."),
            File("b.txt", "Two."));

        Assert.Null(result.Inputs);
        Assert.Contains("More attachments", result.RejectReason);
    }

    [Fact]
    public void TwoFilesForOneSlot_AreRefused()
    {
        var result = Resolve(
            TwoSlots,
            "Body.",
            File("prior_notes.txt", "One."),
            File("prior_notes.md", "Two."));

        Assert.Null(result.Inputs);
        Assert.Contains("More than one input", result.RejectReason);
    }

    // ----- #428: numbered stems for a slot that takes several ---------------

    [Fact]
    public void NumberedStems_FillTheArraySlotInNumericOrder()
    {
        // Ten files supplied in reverse, named -10 down to -1: the sender's
        // numbers are the order, read as numbers — "10" after "2", not
        // before it.
        var attachments = Enumerable.Range(1, 10).Reverse()
            .Select(n => File($"prior_notes-{n}.txt", $"Note {n}."))
            .ToArray();

        var result = Resolve(NotesAsArray, "Referral body.", attachments);

        Assert.Null(result.RejectReason);
        Assert.Equal(
            Enumerable.Range(1, 10).Select(n => $"prior_notes-{n}.txt"),
            result.Files!["prior_notes"].Select(file => file.FileName));
    }

    [Fact]
    public void APlainStemForAnArraySlot_IsAOneElementList()
    {
        var result = Resolve(NotesAsArray, "Referral body.", File("prior_notes.txt", "One."));

        Assert.Null(result.RejectReason);
        Assert.Equal("prior_notes.txt", result.Files!["prior_notes"].Single().FileName);
    }

    [Fact]
    public void ANumberedStemForATextSlot_IsRefused()
    {
        var result = Resolve(NotesAsArray, null, File("consult_draft-1.docx", "Referral."));

        Assert.Null(result.Inputs);
        Assert.Equal(
            "Input 'consult_draft' takes one document and cannot be numbered. Name the file 'consult_draft' instead.",
            result.RejectReason);
    }

    [Fact]
    public void ANumberedStemForAnArrayOfSomethingElse_IsRefused()
    {
        var slots = new[]
        {
            new WorkflowInputSpec("consult_draft", "Consult draft"),
            new WorkflowInputSpec("medications", "Medications", Required: false,
                Type: WorkflowInputTypes.Array, Items: WorkflowInputTypes.Object,
                Fields: new List<WorkflowFieldSpec> { new("name", "Drug") })
        };

        var result = Resolve(slots, "Referral body.", File("medications-1.txt", "Aspirin."));

        Assert.Contains("'medications' takes one document and cannot be numbered", result.RejectReason);
    }

    [Fact]
    public void AGapInTheNumbering_IsRefused()
    {
        var result = Resolve(NotesAsArray, "Body.", File("prior_notes-1.txt", "One."), File("prior_notes-3.txt", "Three."));

        Assert.Equal("Input 'prior_notes' is numbered with gaps. Number its files 1 to 2 without gaps.", result.RejectReason);
    }

    [Fact]
    public void NumberingThatStartsPastOne_IsAGap()
    {
        var result = Resolve(NotesAsArray, "Body.", File("prior_notes-2.txt", "Two."));

        Assert.Equal("Input 'prior_notes' is numbered with gaps. Number its files 1 to 1 without gaps.", result.RejectReason);
    }

    [Fact]
    public void ADuplicateNumber_IsRefused()
    {
        var result = Resolve(NotesAsArray, "Body.", File("prior_notes-1.txt", "One."), File("prior_notes-1.md", "Again."));

        Assert.Equal("Input 'prior_notes' has more than one document with the same number. Number each file once.", result.RejectReason);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PlainAndNumbered_ForTheSameSlot_IsRefused(bool plainFirst)
    {
        // Whichever arrives first: "prior_notes.pdf" beside "prior_notes-1.pdf"
        // is two readings of one slot.
        var plain = File("prior_notes.pdf", "One.");
        var numbered = File("prior_notes-1.pdf", "Two.");

        var result = plainFirst
            ? Resolve(NotesAsArray, "Body.", plain, numbered)
            : Resolve(NotesAsArray, "Body.", numbered, plain);

        Assert.Equal(
            "Input 'prior_notes' was supplied both as 'prior_notes' and as numbered files. Number every file for it, or send one.",
            result.RejectReason);
    }

    [Fact]
    public void TwoPlainStemsForAnArraySlot_AreStillRefusedWithTodaysSentence()
    {
        // Unnumbered, their order is not something a no-PHI reply can confirm.
        var result = Resolve(NotesAsArray, "Body.", File("prior_notes.txt", "One."), File("prior_notes.md", "Two."));

        Assert.Equal("More than one input was supplied for 'prior_notes'.", result.RejectReason);
    }

    [Fact]
    public void ANumberedStemForAnUndeclaredName_IsJustAnUnmatchedFile()
    {
        // "fax-2.pdf" is not numbering; alone, it fills the one free slot as
        // it always did.
        var result = Resolve(TwoSlots, null, File("fax-2.pdf", "Referral."));

        Assert.Null(result.RejectReason);
        Assert.Equal("fax-2.pdf", result.Files!["consult_draft"].Single().FileName);
    }

    [Theory]
    [InlineData("prior_notes-1.txt", "prior_notes-3.txt")]
    [InlineData("prior_notes-1.txt", "prior_notes-1.md")]
    [InlineData("prior_notes.txt", "prior_notes-1.md")]
    [InlineData("consult_draft-1.txt", "prior_notes-1.md")]
    public void ANumberedRefusal_NamesNoFilename(string first, string second)
    {
        const string Sentinel = "SENTINEL";
        var result = Resolve(NotesAsArray, "Body.",
            File(first.Replace(".", $"{Sentinel}."), "One."),
            File(second.Replace(".", $"{Sentinel}."), "Two."));

        // The sentinel rides in the filename's stem, which the stem match
        // cannot see — so these files are unmatched, and the refusal, which
        // is the "name each file" one or a numbered one, carries no filename.
        Assert.NotNull(result.RejectReason);
        Assert.DoesNotContain(Sentinel, result.RejectReason);
        Assert.DoesNotContain(".txt", result.RejectReason);
        Assert.DoesNotContain(".md", result.RejectReason);
    }

    [Fact]
    public void NothingUsable_IsRefused()
    {
        var result = Resolve(TwoSlots, "  ");

        Assert.Null(result.Inputs);
        Assert.Contains("neither a usable body nor an attachment", result.RejectReason);
    }

    [Fact]
    public void LegacyPackage_RefusesAnAttachment()
    {
        // v5/v6 declare no slots, so one implicit slot and nowhere for a file
        // to go. This used to concatenate the attachment into the body, which
        // only worked while email decoded files itself (#237).
        var result = Resolve(Array.Empty<WorkflowInputSpec>(), "Referral body.", File("extra.txt", "Old records."));

        Assert.Null(result.Inputs);
        Assert.Contains("accepts a single input", result.RejectReason);
    }

    [Fact]
    public void LegacyPackage_BodyOnly_StillWorks()
    {
        var result = Resolve(Array.Empty<WorkflowInputSpec>(), "The referral.", Array.Empty<EmailInputAttachment>());

        Assert.Null(result.RejectReason);
        Assert.Equal("The referral.", result.Inputs!["consult_draft"]);
    }

    // #294 — the body is dropped whenever a named attachment already claimed
    // the slot it could have filled. Correct, and silent until now. A dropped
    // body carrying a cloud link is the one case that is evidence rather than
    // noise: it produced a clinically detailed consult whose Medications
    // section read "not documented" while reading as complete.

    private const string OneDriveLink =
        "https://consultologist-my.sharepoint.com/:w:/g/personal/u/EX9fLk2mQ_dHqB7wZ8vNc1kBqL3rT6yPmA2sK4uW0nXeVg";

    [Fact]
    public void ADiscardedBodyHoldingACloudLink_IsRefused()
    {
        // The exact #294 reproduction: consult_draft.docx attached directly,
        // prior_notes.docx "attached" as a OneDrive link in the body.
        var result = Resolve(
            TwoSlots,
            $"Hi, referral attached, prior notes here: {OneDriveLink} Regards, Dr X",
            File("consult_draft.docx", "The referral."));

        Assert.NotNull(result.RejectReason);
        Assert.Contains("attach the document", result.RejectReason);
        // A filename can itself be PHI.
        Assert.DoesNotContain(".docx", result.RejectReason);
    }

    [Fact]
    public void ADiscardedCoveringNote_IsCountedNotRefused()
    {
        // The false positive that must never happen: a covering note is a
        // legitimate thing to drop.
        const string note = "Thanks — referral attached. She is anxious about the scan.";

        var result = Resolve(TwoSlots, note, File("consult_draft.docx", "The referral."));

        Assert.Null(result.RejectReason);
        Assert.Equal(note.Length, result.DiscardedBodyCharacters);
    }

    [Fact]
    public void ABodyThatIsActuallyUsed_IsNotDiscardedEvenWithALink()
    {
        // No attachment claimed consult_draft, so the body fills it. A link in
        // a body that is *used* is #291's case at job start, not this one --
        // refusing here too would double-refuse and confuse the reason.
        var result = Resolve(
            TwoSlots,
            $"Referral text. See also {OneDriveLink}",
            File("prior_notes.docx", "Old notes."));

        Assert.Null(result.RejectReason);
        Assert.Equal(0, result.DiscardedBodyCharacters);
        Assert.Equal("Referral text. See also " + OneDriveLink, result.Inputs!["consult_draft"]);
    }

    [Fact]
    public void AGuidelineLinkInADiscardedBody_IsNotACloudFile()
    {
        var result = Resolve(
            TwoSlots,
            "Referral attached. Per https://www.nccn.org/guidelines/category_1",
            File("consult_draft.docx", "The referral."));

        Assert.Null(result.RejectReason);
        Assert.True(result.DiscardedBodyCharacters > 0);
    }

    [Fact]
    public void NoBodyAtAll_DiscardsNothing()
    {
        var result = Resolve(TwoSlots, null, File("consult_draft.docx", "The referral."));

        Assert.Null(result.RejectReason);
        Assert.Equal(0, result.DiscardedBodyCharacters);
    }
}
