using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #432, v9 § 4: a package may declare a title and a description. The editor
/// says up front that a fork does not carry them, and (from commit 5) authors
/// them in a Package pane.
/// </summary>
public class TemplatesPackageMetadataTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse fixture)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture);
        return Render<Templates>();
    }

    private static void Navigate(IRenderedComponent<Templates> page, string label) =>
        page.FindAll("button.editor-nav__item")
            .First(button => button.TextContent.Replace("\u25CF", string.Empty).Trim() == label)
            .Click();

    private static IEnumerable<string> NavLabels(IRenderedComponent<Templates> page) =>
        page.FindAll("button.editor-nav__item").Select(button => button.TextContent.Replace("\u25CF", string.Empty).Trim());

    private static void Publish(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

    private static void UpgradeTo(IRenderedComponent<Templates> page, int target) =>
        page.FindAll("fluent-button")
            .First(button => button.TextContent.Contains($"Upgrade to specVersion {target}", StringComparison.Ordinal))
            .Click();

    private static IReadOnlyList<string> Refusals(IRenderedComponent<Templates> page) =>
        page.FindAll(".fluent-messagebar-message li").Select(item => item.TextContent.Trim()).ToList();

    private WorkflowPackagePublishRequest? sent;

    private void WithPublishAccepted() =>
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.08.2", "acct-1234567890ab@v2026.08.2"),
                Array.Empty<string>()));

    private static System.Text.Json.JsonElement SentManifest(WorkflowPackagePublishRequest request) =>
        System.Text.Json.JsonDocument.Parse(request.Manifest.GetRawText()).RootElement;

    private static Consultologist.Api.Workflow.WorkflowPackageValidator.ValidationResult ValidateSent(WorkflowPackagePublishRequest request)
    {
        var manifest = System.Text.Json.JsonSerializer.Deserialize<Consultologist.Api.Workflow.WorkflowPackageManifest>(
            request.Manifest.GetRawText(), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        return Consultologist.Api.Workflow.WorkflowPackageValidator.Validate(
            manifest, request.Files.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), new Dictionary<string, string>(StringComparer.Ordinal));
    }

    private string? draftJson;

    private void CaptureDraft() =>
        JSInterop.SetupVoid("localStorage.setItem", _ => true).SetVoidResult();

    private void WithDraft(WorkflowPackageContentResponse package, string json) =>
        JSInterop.Setup<string?>("localStorage.getItem", $"workflow-editor-draft:{package.Ref}").SetResult(json);

    private static string? PublishSuccess(IRenderedComponent<Templates> page) =>
        page.FindAll(".fluent-messagebar-message").Select(bar => bar.TextContent.Trim())
            .FirstOrDefault(text => text.StartsWith("Published", StringComparison.Ordinal));

    // ----- the pane ---------------------------------------------------------

    [Fact]
    public void ThePackagePane_IsOfferedAtNine_AndNotBelow()
    {
        Assert.Contains("Package", NavLabels(RenderEditor(EditorFixtures.V9Structured())));

        var v8 = RenderEditor(EditorFixtures.V8());
        Assert.DoesNotContain("Package", NavLabels(v8));
        UpgradeTo(v8, 9);
        Assert.Contains("Package", NavLabels(v8));
    }

    [Fact]
    public void TypingATitle_StagesAPendingChange_AndPublishesThroughTheValidator()
    {
        WithPublishAccepted();
        var page = RenderEditor(EditorFixtures.V9Structured());
        Navigate(page, "Package");

        // The ref is the stated fallback, shown where the title would go.
        Assert.StartsWith("acct-1234567890ab@v", page.Find("input[aria-label='Package title']").GetAttribute("placeholder"));
        page.Find("input[aria-label='Package title']").Change("Breast oncology consults");
        page.Find("textarea[aria-label='Package description']").Change("Referral triage for the breast clinic.");
        Assert.Contains("●", page.FindAll("button.editor-nav__item").First(button => button.TextContent.Contains("Package")).TextContent);

        Publish(page);

        Assert.NotNull(sent);
        var manifest = SentManifest(sent!);
        Assert.Equal("Breast oncology consults", manifest.GetProperty("title").GetString());
        Assert.Equal("Referral triage for the breast clinic.", manifest.GetProperty("description").GetString());
        var result = ValidateSent(sent);
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
        Assert.Equal(
            "Published acct-1234567890ab@v2026.08.2 (\"Breast oncology consults\") and switched your consults to it. It is not yet runnable.",
            PublishSuccess(page));
    }

    [Fact]
    public void ClearingTheTitle_RemovesTheKey()
    {
        WithPublishAccepted();
        var page = RenderEditor(EditorFixtures.WithTitle(EditorFixtures.V9Structured(), "Old name", "Old description."));
        Assert.Equal("Old name", page.Find(".editor-bar__title").TextContent.Trim());
        Navigate(page, "Package");

        page.Find("input[aria-label='Package title']").Change("");
        Publish(page);

        var manifest = SentManifest(sent!);
        Assert.False(manifest.TryGetProperty("title", out _));
        Assert.False(manifest.TryGetProperty("Title", out _));
        Assert.Equal("Old description.", manifest.GetProperty("description").GetString());
        Assert.StartsWith("Published acct-1234567890ab@v2026.08.2 and switched", PublishSuccess(page));
    }

    [Fact]
    public void ATitle_RoundTripsThroughTheDraft()
    {
        var package = EditorFixtures.V9Structured();
        WithDraft(package, """{ "Version": 12, "Title": "Seeded", "Description": "Seeded description." }""");
        var page = RenderEditor(package);
        Navigate(page, "Package");

        Assert.Equal("Seeded", page.Find("input[aria-label='Package title']").GetAttribute("value"));
        Assert.Equal("Seeded description.", page.Find("textarea[aria-label='Package description']").GetAttribute("value"));

        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Discard")).Click();
        Assert.Equal("", page.Find("input[aria-label='Package title']").GetAttribute("value"));
    }

    [Theory]
    [InlineData("title", "   ", "title must not be empty.")]
    [InlineData("title", "Breast\nclinic", "title must be a single line.")]
    [InlineData("title", "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx", "title must be at most 80 characters.")]
    [InlineData("description", "  ", "description must not be empty.")]
    public async Task AValueTheServerWouldRefuse_IsRefusedAtTheDesk(string field, string value, string expected)
    {
        var page = RenderEditor(EditorFixtures.V9Structured());
        Navigate(page, "Package");
        page.Find(field == "title" ? "input[aria-label='Package title']" : "textarea[aria-label='Package description']").Change(value);

        Publish(page);

        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        Assert.Contains(expected, Refusals(page));
    }

    [Fact]
    public async Task AnOverlongDescription_IsRefusedAtTheDesk()
    {
        var page = RenderEditor(EditorFixtures.V9Structured());
        Navigate(page, "Package");
        page.Find("textarea[aria-label='Package description']").Change(new string('x', 501));

        Publish(page);

        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        Assert.Contains("description must be at most 500 characters.", Refusals(page));
    }

    [Fact]
    public async Task ATitleOnAV8Package_IsRefusedAtTheDesk_UntilUpgraded()
    {
        var package = EditorFixtures.V8();
        WithDraft(package, """{ "Version": 12, "Title": "Too early" }""");
        var page = RenderEditor(package);

        Publish(page);

        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        Assert.Contains(
            "The package declares a title, which requires specVersion 9. Use \"Upgrade to specVersion 9\" and publish.",
            Refusals(page));

        WithPublishAccepted();
        UpgradeTo(page, 9);
        Publish(page);
        Assert.Equal("Too early", SentManifest(sent!).GetProperty("title").GetString());
    }

    [Fact]
    public void OnAForeignPackage_TheFieldsAreDisabled()
    {
        // The pane gates on the loaded version, which is the record's; the
        // fixture's manifest body is what the banner reads.
        var page = RenderEditor(EditorFixtures.WithTitle(EditorFixtures.NotMine() with { SpecVersion = 9 }, "Theirs"));
        Navigate(page, "Package");
        Assert.True(page.Find("input[aria-label='Package title']").HasAttribute("disabled"));
        Assert.True(page.Find("textarea[aria-label='Package description']").HasAttribute("disabled"));
    }

    private static IReadOnlyList<string> ForeignNoticeBullets(IRenderedComponent<Templates> page) =>
        page.FindAll(".fluent-messagebar-message li").Select(item => item.TextContent.Trim()).ToList();

    [Fact]
    public void TheForeignPackageNotice_SaysTheTitleIsNotCarried_OnlyWhenThereIsOne()
    {
        var titled = RenderEditor(EditorFixtures.WithTitle(EditorFixtures.NotMine(), "Breast oncology consults"));
        Assert.Contains(ForeignNoticeBullets(titled), bullet => bullet.StartsWith("Its title and description are not carried over", StringComparison.Ordinal));

        var plain = RenderEditor(EditorFixtures.NotMine());
        Assert.DoesNotContain(ForeignNoticeBullets(plain), bullet => bullet.Contains("not carried over", StringComparison.Ordinal));
    }
}
