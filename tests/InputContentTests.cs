using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Tests;

/// <summary>
/// #290: the floor that stops a consult being generated from a referral that
/// never arrived.
///
/// The two directions are not symmetric, and these tests are weighted
/// accordingly. Refusing a real terse referral is annoying, visible and
/// recoverable. Letting an empty one through produced a complete oncology
/// consult in which every section read "not documented" — delivered, with
/// nothing signalling it.
/// </summary>
public class InputContentTests
{
    /// <summary>The case that must always generate.</summary>
    private const string TerseReferral =
        "65M, newly diagnosed adenocarcinoma of the lung, stage IIIA, for consideration of chemoradiation. PMHx HTN.";

    /// <summary>What #290's message actually carried.</summary>
    private const string LinkOnlyBody =
        "https://consultologist-my.sharepoint.com/:w:/g/personal/user_consultologist_ai/EX9fLk2mQ_dHqB7wZ8vNc1kBqL3rT6yPmA2sK4uW0nXeVg";

    [Fact]
    public void ATerseButRealReferral_Counts()
    {
        // The regression that matters most in the other direction.
        Assert.True(InputContent.MeaningfulLength(TerseReferral) >= InputContent.MinimumCharacters);
    }

    [Fact]
    public void ABodyThatIsOnlyALink_CountsAsNothing()
    {
        // Longer than the terse referral in raw characters, which is exactly
        // why raw length cannot be the rule.
        Assert.True(LinkOnlyBody.Length > TerseReferral.Length);
        Assert.Equal(0, InputContent.MeaningfulLength(LinkOnlyBody));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t  ")]
    public void NothingAtAll_CountsAsNothing(string? text)
    {
        Assert.Equal(0, InputContent.MeaningfulLength(text));
    }

    [Theory]
    [InlineData("https://example.com/a")]
    [InlineData("http://example.com/a")]
    [InlineData("www.example.com/a")]
    [InlineData("ftp://example.com/a")]
    [InlineData("mailto:someone@example.com")]
    public void EveryLinkFormAMailClientProduces_IsStripped(string link)
    {
        Assert.Equal(0, InputContent.MeaningfulLength(link));
    }

    [Fact]
    public void ProseAroundALink_StillCounts()
    {
        // A clinician may legitimately include a link beside a real referral.
        var text = $"See {LinkOnlyBody} — {TerseReferral}";

        Assert.True(InputContent.MeaningfulLength(text) >= InputContent.MinimumCharacters);
    }

    [Fact]
    public void WhitespaceIsNotContent()
    {
        // Forty spaces must not clear a forty-character floor.
        Assert.Equal(0, InputContent.MeaningfulLength(new string(' ', 100)));
    }

    // --- which input gets named ---

    private static WorkflowPackageManifest V7() => V7Fixtures.MultiDeliverable();

    [Fact]
    public void V7_ARequiredInputWithoutContent_IsNamed()
    {
        var offending = InputContent.FindInputWithoutContent(
            new ConsultGenerationRequest(null),
            V7(),
            new Dictionary<string, ConsultInputValue> { ["consult_draft"] = LinkOnlyBody, ["prior_notes"] = TerseReferral },
            40);

        Assert.Equal("consult_draft", offending);
    }

    [Fact]
    public void V7_AnOptionalInputWithoutContent_IsNotNamed()
    {
        // prior_notes is optional; absent or thin is its normal state and must
        // never block a consult.
        var offending = InputContent.FindInputWithoutContent(
            new ConsultGenerationRequest(null),
            V7(),
            new Dictionary<string, ConsultInputValue> { ["consult_draft"] = TerseReferral, ["prior_notes"] = "" },
            40);

        Assert.Null(offending);
    }

    [Fact]
    public void ARequiredArrayOfText_IsMeasuredAsTheSumOfItsElements()
    {
        // #428: one thin note among several is not an empty referral; several
        // thin ones are — the slot is measured, not the element.
        var manifest = V9Fixtures.WithInput(new("prior_notes", "Prior notes", Required: true, Type: WorkflowInputTypes.Array, Items: WorkflowInputTypes.Text));

        Assert.Null(InputContent.FindInputWithoutContent(
            new ConsultGenerationRequest(null),
            manifest,
            new Dictionary<string, ConsultInputValue>
            {
                ["consult_draft"] = TerseReferral,
                ["prior_notes"] = ConsultInputValue.OfArray(new[] { ConsultInputValue.OfText("Thin."), ConsultInputValue.OfText(TerseReferral) })
            },
            40));

        Assert.Equal("prior_notes", InputContent.FindInputWithoutContent(
            new ConsultGenerationRequest(null),
            manifest,
            new Dictionary<string, ConsultInputValue>
            {
                ["consult_draft"] = TerseReferral,
                ["prior_notes"] = ConsultInputValue.OfArray(Enumerable.Repeat(ConsultInputValue.OfText("Thin."), 4))
            },
            40));
    }

    [Fact]
    public void AnArrayOfObjects_IsNotMeasured()
    {
        // Structure is not prose: its fields are typed, and each already
        // constrains itself more tightly than a length could.
        var manifest = V9Fixtures.WithInput(new("medications", "Medications", Required: true,
            Type: WorkflowInputTypes.Array, Items: WorkflowInputTypes.Object,
            Fields: new List<WorkflowFieldSpec> { new("name", "Drug") }));

        Assert.Null(InputContent.FindInputWithoutContent(
            new ConsultGenerationRequest(null),
            manifest,
            new Dictionary<string, ConsultInputValue>
            {
                ["consult_draft"] = TerseReferral,
                ["medications"] = ConsultInputValue.OfArray(new[] { ConsultInputValue.OfObject(new[] { new ConsultInputEntry("name", ConsultInputValue.OfText("x")) }) })
            },
            40));
    }

    [Fact]
    public void V5_TheDraftIsTheInput()
    {
        var offending = InputContent.FindInputWithoutContent(
            new ConsultGenerationRequest(LinkOnlyBody),
            V5Fixtures.Manifest(),
            null,
            40);

        Assert.Equal("consult_draft", offending);
    }

    [Fact]
    public void V5_ARealDraftPasses()
    {
        Assert.Null(InputContent.FindInputWithoutContent(
            new ConsultGenerationRequest(TerseReferral),
            V5Fixtures.Manifest(),
            null,
            40));
    }

    // --- #291: the referral is behind a link we cannot open ---
    //
    // #290's floor shipped and was defeated the same afternoon: a body of
    // "here it is <onedrive link> Regards, Dr X" cleared forty characters and
    // generated a consult. The link is the signal the floor cannot see.

    private const string OneDriveLink =
        "https://consultologist-my.sharepoint.com/:w:/g/personal/u/EX9fLk2mQ_dHqB7wZ8vNc1kBqL3rT6yPmA2sK4uW0nXeVg";

    private static readonly Dictionary<string, IReadOnlyList<ConsultInputOrigin>> FromADocument =
        new() { ["consult_draft"] = new[] { new ConsultInputOrigin("document", "openxml/3.5.1") } };

    [Fact]
    public void ABodyThatIsOnlyALinkAndASignature_IsRefused()
    {
        // The exact message that defeated the floor at 17:32 on 2026-08-04.
        var body = $"Hi, here is the referral. {OneDriveLink} Regards, Dr X, Oncology";

        Assert.True(InputContent.MeaningfulLength(body) >= 40, "the floor alone does not catch this");
        Assert.Equal("consult_draft", InputContent.FindInputBehindACloudLink(
            new ConsultGenerationRequest(body), V5Fixtures.Manifest(), null, null));
    }

    [Fact]
    public void ALinkBesideAnInputThatCameFromARealDocument_IsIgnored()
    {
        // The attachment supplied the input; the link is incidental.
        Assert.Null(InputContent.FindInputBehindACloudLink(
            new ConsultGenerationRequest($"See also {OneDriveLink}"),
            V5Fixtures.Manifest(),
            null,
            FromADocument));
    }

    [Fact]
    public void ALinkInAnInputCopiedFromAPreviousRun_IsStillNamed()
    {
        // #510: a previous run's text was typed by someone once, and its
        // links are as unreachable as any. Only a document exempts a slot.
        var fromARun = new Dictionary<string, IReadOnlyList<ConsultInputOrigin>>
        {
            ["consult_draft"] = new[] { new ConsultInputOrigin(ConsultInputOriginKinds.PreviousRun, SourceJobId: "0123456789abcdef0123456789abcdef", SourceResultId: "consult") }
        };

        Assert.Equal("consult_draft", InputContent.FindInputBehindACloudLink(
            new ConsultGenerationRequest($"See also {OneDriveLink}"),
            V5Fixtures.Manifest(),
            null,
            fromARun));
    }

    [Fact]
    public void AnOrdinaryLinkIsNotACloudFile()
    {
        // A guideline or a journal article is clinical prose, not a document
        // the sender thinks they attached.
        Assert.False(InputContent.HasCloudStorageLink(
            $"{TerseReferral} Per https://www.nccn.org/guidelines/category_1"));
    }

    [Theory]
    [InlineData("https://x.sharepoint.com/a")]
    [InlineData("https://1drv.ms/w/s!abc")]
    [InlineData("https://onedrive.live.com/a")]
    [InlineData("https://drive.google.com/file/d/a")]
    [InlineData("https://www.dropbox.com/s/a")]
    public void EveryCloudStorageHostWeKnowOf_IsRecognised(string link)
    {
        Assert.True(InputContent.HasCloudStorageLink($"Referral attached: {link}"));
    }

    [Fact]
    public void V7_OnlyRequiredInputsAreChecked()
    {
        // prior_notes being a link is not grounds to refuse a consult whose
        // referral arrived properly.
        Assert.Null(InputContent.FindInputBehindACloudLink(
            new ConsultGenerationRequest(null),
            V7Fixtures.MultiDeliverable(),
            new Dictionary<string, string> { ["consult_draft"] = TerseReferral, ["prior_notes"] = OneDriveLink },
            null));
    }

    [Fact]
    public void V7_ARequiredInputBehindALink_IsNamed()
    {
        Assert.Equal("consult_draft", InputContent.FindInputBehindACloudLink(
            new ConsultGenerationRequest(null),
            V7Fixtures.MultiDeliverable(),
            new Dictionary<string, string> { ["consult_draft"] = $"Referral: {OneDriveLink} Regards", ["prior_notes"] = "" },
            null));
    }

    [Fact]
    public void AZeroMinimum_DisablesTheFloorEntirely()
    {
        // The escape hatch, if the floor ever turns out to be wrong in
        // production and has to be dropped without a deploy.
        Assert.Null(InputContent.FindInputWithoutContent(
            new ConsultGenerationRequest(""),
            V5Fixtures.Manifest(),
            null,
            0));
    }
}
