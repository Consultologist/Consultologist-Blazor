using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using Consultologist.Web.Services.Workflow;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #561: the snippet road into the setup form. No picker when the library
/// is empty; choosing appends as ordinary typed text — verbatim into an
/// empty field, blank-line separated into a typed one — and rides the
/// submitted inputs like typing.
/// </summary>
public class ConsultsSnippetTests : ClientRenderTestContext
{
    private void WithSnippets(string? value)
    {
        // The init try-block reads Me before the snippet row; an unstubbed
        // account would abort the block and leave the set empty for the
        // wrong reason.
        AccountService.GetCurrentAccountAsync().Returns(new AccountMeResponse(
            "user-1", "A Clinician", "clinician@example.com", "Active",
            new AccountIdentity("entra-external-id", "https://login.microsoftonline.com/x", "sub-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            Array.Empty<AccountIdentity>()));
        AccountService.GetSettingAsync(Snippets.SettingKey).Returns(
            value == null
                ? (AccountSettingResponse?)null
                : new AccountSettingResponse(Snippets.SettingKey, value, Snippets.ContentType, DateTimeOffset.UtcNow));
    }

    private IRenderedComponent<Consults> RenderWithDraftField()
    {
        WithPinnedPackage(
            blocks: new[] { Block("consult_note:hpi", "hpi") },
            inputs: new[] { new WorkflowPackageInputResponse("consult_draft", "Consult draft", true) });

        return Render<Consults>();
    }

    [Fact]
    public void AnEmptyLibrary_RendersNoPicker()
    {
        WithSnippets(null);

        var page = RenderWithDraftField();

        Assert.Empty(page.FindAll(".snippet-picker"));
    }

    [Fact]
    public async Task ThePicker_ListsSnippetNames()
    {
        WithSnippets(SnippetsModelTests.Wire);

        var page = RenderWithDraftField();
        await page.Find(".snippet-picker__trigger").ClickAsync(new());

        Assert.Contains("Normal exam", page.Find(".snippet-picker__item").TextContent);
    }

    [Fact]
    public async Task ChoosingIntoAnEmptyField_IsTheTextVerbatim()
    {
        WithSnippets(SnippetsModelTests.Wire);

        var page = RenderWithDraftField();
        await page.Find(".snippet-picker__trigger").ClickAsync(new());
        await page.Find(".snippet-picker__item").ClickAsync(new());

        Assert.Equal(
            "Cardiovascular and respiratory examination unremarkable.",
            page.Find("fluent-text-area").GetAttribute("value"));
    }

    [Fact]
    public async Task ChoosingIntoTypedText_AppendsBlankLineSeparated()
    {
        WithSnippets(SnippetsModelTests.Wire);

        var page = RenderWithDraftField();
        await page.Find("fluent-text-area").ChangeAsync(new() { Value = "Typed so far." });
        await page.Find(".snippet-picker__trigger").ClickAsync(new());
        await page.Find(".snippet-picker__item").ClickAsync(new());

        Assert.Equal(
            "Typed so far.\n\nCardiovascular and respiratory examination unremarkable.",
            page.Find("fluent-text-area").GetAttribute("value"));
    }
}
