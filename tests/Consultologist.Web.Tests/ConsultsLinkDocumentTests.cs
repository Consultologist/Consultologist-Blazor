using AngleSharp.Dom;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using Consultologist.Web.Services.Documents;
using Consultologist.Web.Services.Workflow;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #615: the OneDrive/SharePoint link road into the document slot. The
/// affordance is organisation-only and TELLS a personal account early —
/// rendered disabled with the reason in words, never silently hidden.
/// </summary>
public class ConsultsLinkDocumentTests : ClientRenderTestContext
{
    private void WithSignInKind(string kind)
    {
        AccountService.GetCurrentAccountAsync().Returns(new AccountMeResponse(
            "user-1", "A Clinician", "clinician@example.com", "Active",
            new AccountIdentity("entra-external-id", "https://login.microsoftonline.com/x", "sub-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            Array.Empty<AccountIdentity>(),
            SignInKind: kind));
    }

    private IRenderedComponent<Consults> RenderWithDocumentSlot()
    {
        WithPinnedPackage(
            blocks: new[] { Block("consult_note:hpi", "hpi") },
            inputs: new[] { new WorkflowPackageInputResponse("consult_draft", "Consult draft", true) });

        return Render<Consults>();
    }

    [Fact]
    public void AnOrganisationAccount_GetsTheLiveControl()
    {
        WithSignInKind("organisation");

        var page = RenderWithDocumentSlot();

        var link = page.Find(".input-field__link input");
        Assert.False(link.HasAttribute("disabled"));
        Assert.Empty(page.FindAll(".input-field__link-reason"));
    }

    [Fact]
    public void APersonalAccount_SeesItDisabled_WithTheReasonInWords()
    {
        // Tell the user early: the control renders, disabled, and the reason
        // stands beside it — never the silent hide.
        WithSignInKind("personal");

        var page = RenderWithDocumentSlot();

        Assert.True(page.Find(".input-field__link input").HasAttribute("disabled"));
        Assert.True(page.Find(".input-field__link-fetch").HasAttribute("disabled"));
        Assert.Contains("organisation account", page.Find(".input-field__link-reason").TextContent);
    }

    [Fact]
    public async Task RetrievingALink_FillsTheSlot_LikeAnUpload()
    {
        WithSignInKind("organisation");
        DocumentService.ExtractFromLinkAsync("https://1drv.ms/x").Returns(
            new LinkDocumentOutcome("The referral text.", "openxml/1", 2,
                new byte[] { 1, 2, 3 }, "referral.docx", null));

        var page = RenderWithDocumentSlot();
        await page.Find(".input-field__link input").InputAsync(new() { Value = "https://1drv.ms/x" });
        await page.Find(".input-field__link-fetch").ClickAsync(new());

        await DocumentService.Received(1).ExtractFromLinkAsync("https://1drv.ms/x");
        Assert.Contains("referral.docx", page.Find(".input-field__chip").TextContent);
    }

    [Fact]
    public async Task ARefusedLink_ShowsTheSentence_AndChangesNothing()
    {
        WithSignInKind("organisation");
        DocumentService.ExtractFromLinkAsync("https://1drv.ms/gone").Returns(
            LinkDocumentOutcome.Refused("That link does not point at a file any more — it may have been deleted or the sharing may have expired."));

        var page = RenderWithDocumentSlot();
        await page.Find(".input-field__link input").InputAsync(new() { Value = "https://1drv.ms/gone" });
        await page.Find(".input-field__link-fetch").ClickAsync(new());

        Assert.Contains("does not point at a file", page.Find(".input-field__file-error").TextContent);
        Assert.Empty(page.FindAll(".input-field__chip"));
    }

    // The word→sentence map, kept honest: every named word HAS words, and
    // none leaks the kebab-case token to a clinician.
    [Theory]
    [InlineData("personal-account", "organisation account")]
    [InlineData("obo-consent-required", "administrator")]
    [InlineData("obo-exchange-failed", "try again")]
    [InlineData("link-not-onedrive", "OneDrive and SharePoint")]
    [InlineData("link-not-found", "deleted")]
    [InlineData("link-forbidden", "share it with you")]
    [InlineData("link-too-large", "10 MB")]
    [InlineData("link-fetch-failed", "try again")]
    public void EveryNamedWord_HasWords(string word, string expectedFragment)
    {
        var sentence = DocumentEndpointService.LinkErrorSentence(word);

        Assert.NotNull(sentence);
        Assert.Contains(expectedFragment, sentence, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(word, sentence, StringComparison.Ordinal);
    }
}
