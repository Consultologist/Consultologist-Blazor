using Bunit;
using System.Text.Json;
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

    private static Consultologist.PackageFormat.WorkflowPackageValidator.ValidationResult ValidateSent(WorkflowPackagePublishRequest request)
    {
        var manifest = System.Text.Json.JsonSerializer.Deserialize<Consultologist.PackageFormat.WorkflowPackageManifest>(
            request.Manifest.GetRawText(), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        return Consultologist.PackageFormat.WorkflowPackageValidator.Validate(
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
    public void ThePackagePane_IsOfferedFromNine_AndNotBelow()
    {
        Assert.Contains("Package", NavLabels(RenderEditor(EditorFixtures.V9Structured())));

        var v8 = RenderEditor(EditorFixtures.V8());
        Assert.DoesNotContain("Package", NavLabels(v8));
        UpgradeTo(v8, 10);
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
            "Published acct-1234567890ab@v2026.08.2 (\"Breast oncology consults\") and switched your consults to it.",
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
            "The package declares a title, which requires specVersion 9. Use \"Upgrade to specVersion 10\" and publish.",
            Refusals(page));

        WithPublishAccepted();
        UpgradeTo(page, 10);
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
        Assert.Contains(ForeignNoticeBullets(titled), bullet => bullet.StartsWith("Its title, description and tags are not carried over", StringComparison.Ordinal));

        // #453: tags alone are enough to warrant the bullet; an empty set is not.
        var tagged = RenderEditor(EditorFixtures.WithTags(EditorFixtures.NotMine() with { SpecVersion = 9 }, "oncology"));
        Assert.Contains(ForeignNoticeBullets(tagged), bullet => bullet.Contains("not carried over", StringComparison.Ordinal));
        var untagged = RenderEditor(EditorFixtures.WithTags(EditorFixtures.NotMine() with { SpecVersion = 9 }));
        Assert.DoesNotContain(ForeignNoticeBullets(untagged), bullet => bullet.Contains("not carried over", StringComparison.Ordinal));

        var plain = RenderEditor(EditorFixtures.NotMine());
        Assert.DoesNotContain(ForeignNoticeBullets(plain), bullet => bullet.Contains("not carried over", StringComparison.Ordinal));
    }

    // ----- #453: tags ---------------------------------------------------------

    private static IReadOnlyList<string> TagTexts(IRenderedComponent<Templates> page) =>
        page.FindAll(".package-tag__text").Select(tag => tag.TextContent.Trim()).ToList();

    private static void AddTag(IRenderedComponent<Templates> page, string tag)
    {
        page.Find("input[aria-label='New tag']").Input(tag);
        page.FindAll("button").First(button => button.TextContent.Trim() == "Add tag").Click();
    }

    private static string TagsHint(IRenderedComponent<Templates> page) =>
        page.Find("input[aria-label='New tag']").ParentElement!.NextElementSibling!.TextContent.Trim();

    [Fact]
    public void AddingTags_StagesOnePendingChange_AndPublishesThemInOrder()
    {
        WithPublishAccepted();
        var page = RenderEditor(EditorFixtures.V9Structured());
        Navigate(page, "Package");
        Assert.Contains("No tags.", page.Markup, StringComparison.Ordinal);

        AddTag(page, "oncology");
        AddTag(page, "  Breast  ");
        AddTag(page, "new-patient");

        Assert.Equal(new[] { "oncology", "Breast", "new-patient" }, TagTexts(page));
        Assert.Contains("●", page.FindAll("button.editor-nav__item").First(button => button.TextContent.Contains("Package")).TextContent);
        Assert.Equal(string.Empty, page.Find("input[aria-label='New tag']").GetAttribute("value"));

        Publish(page);

        var manifest = SentManifest(sent!);
        Assert.Equal(new[] { "oncology", "Breast", "new-patient" }, manifest.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()));
        var result = ValidateSent(sent);
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void RemovingAndReordering_AreTheAuthorsToDo_AndBackToLoadedIsNoChange()
    {
        WithPublishAccepted();
        var page = RenderEditor(EditorFixtures.WithTags(EditorFixtures.V9Structured(), "oncology", "breast", "new-patient"));
        Navigate(page, "Package");
        Assert.Equal(new[] { "oncology", "breast", "new-patient" }, TagTexts(page));
        Assert.True(page.Find("button[aria-label='Move tag oncology earlier']").HasAttribute("disabled"));
        Assert.True(page.Find("button[aria-label='Move tag new-patient later']").HasAttribute("disabled"));

        page.Find("button[aria-label='Move tag new-patient earlier']").Click();
        Assert.Equal(new[] { "oncology", "new-patient", "breast" }, TagTexts(page));
        Assert.Contains("●", page.FindAll("button.editor-nav__item").First(button => button.TextContent.Contains("Package")).TextContent);

        // Back to the loaded order: nothing pending, as with a retyped title.
        page.Find("button[aria-label='Move tag new-patient later']").Click();
        Assert.Equal(new[] { "oncology", "breast", "new-patient" }, TagTexts(page));
        Assert.DoesNotContain("●", page.FindAll("button.editor-nav__item").First(button => button.TextContent.Contains("Package")).TextContent);

        page.Find("button[aria-label='Remove tag breast']").Click();
        Publish(page);

        Assert.Equal(new[] { "oncology", "new-patient" }, SentManifest(sent!).GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()));
    }

    [Fact]
    public void RemovingTheLastTag_PublishesAnEmptyArray_NotAnAbsence()
    {
        // The empty set is a value v9 requires; the key stays.
        WithPublishAccepted();
        var page = RenderEditor(EditorFixtures.WithTags(EditorFixtures.V9Structured(), "oncology"));
        Navigate(page, "Package");

        page.Find("button[aria-label='Remove tag oncology']").Click();
        Assert.Contains("No tags.", page.Markup, StringComparison.Ordinal);
        Publish(page);

        var tags = SentManifest(sent!).GetProperty("tags");
        Assert.Equal(JsonValueKind.Array, tags.ValueKind);
        Assert.Equal(0, tags.GetArrayLength());
        Assert.True(ValidateSent(sent).IsValid);
    }

    [Theory]
    [InlineData("   ", "A tag must not be empty.")]
    [InlineData("ONCOLOGY", "That tag is already declared (tags are distinct ignoring case).")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "A tag must be at most 32 characters.")]
    public void ATagTheServerWouldRefuse_IsRefusedAtTheAddButton(string tag, string expected)
    {
        var page = RenderEditor(EditorFixtures.WithTags(EditorFixtures.V9Structured(), "oncology"));
        Navigate(page, "Package");

        AddTag(page, tag);

        Assert.Equal(new[] { "oncology" }, TagTexts(page));
        Assert.Equal(expected, TagsHint(page));
        Assert.DoesNotContain("●", page.FindAll("button.editor-nav__item").First(button => button.TextContent.Contains("Package")).TextContent);
    }

    [Fact]
    public void TheTwentyFirstTag_IsRefused()
    {
        var twenty = Enumerable.Range(1, 20).Select(i => $"tag-{i}").ToArray();
        var page = RenderEditor(EditorFixtures.WithTags(EditorFixtures.V9Structured(), twenty));
        Navigate(page, "Package");

        Assert.True(page.Find("input[aria-label='New tag']").HasAttribute("disabled"));
        Assert.Equal(20, TagTexts(page).Count);
    }

    [Fact]
    public void Tags_RoundTripThroughTheDraft()
    {
        var package = EditorFixtures.V9Structured();
        WithDraft(package, """{ "Version": 13, "Tags": ["oncology", "breast"] }""");

        var page = RenderEditor(package);
        Navigate(page, "Package");

        Assert.Equal(new[] { "oncology", "breast" }, TagTexts(page));
        Assert.Contains("●", page.FindAll("button.editor-nav__item").First(button => button.TextContent.Contains("Package")).TextContent);
    }

    [Fact]
    public void ADraftWithABadTag_IsRefusedAtTheDesk_InTheServersWords()
    {
        // The Add button never lets these through; a draft can carry them.
        WithPublishAccepted();
        var package = EditorFixtures.V9Structured();
        WithDraft(package, """{ "Version": 13, "Tags": ["oncology", " Oncology "] }""");
        var page = RenderEditor(package);

        Publish(page);

        Assert.Null(sent);
        Assert.Contains("tags[1] must not begin or end with whitespace.", Refusals(page));
        Assert.Contains("tags[1] repeats tags[0]; tags are distinct ignoring case.", Refusals(page));
    }

    [Fact]
    public void TagsOnAV8Package_AreRefusedAtTheDesk_UntilUpgraded()
    {
        // A v8 package has no pane; a draft's tags restored onto one are named
        // as needing the version (the #429 posture), and upgrading lets them
        // through — with the upgrade's own empty array replaced by the edit.
        WithPublishAccepted();
        var package = EditorFixtures.V8();
        WithDraft(package, """{ "Version": 13, "Tags": ["too-early"] }""");
        var page = RenderEditor(package);

        Publish(page);
        Assert.Null(sent);
        Assert.Contains(Refusals(page), refusal => refusal.StartsWith("The package declares tags, which requires specVersion 9.", StringComparison.Ordinal));

        UpgradeTo(page, 10);
        Publish(page);
        Assert.Equal(new[] { "too-early" }, SentManifest(sent!).GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()));
        Assert.True(ValidateSent(sent).IsValid, string.Join(" | ", ValidateSent(sent).Errors));
    }

    [Fact]
    public void OnAForeignPackage_TheTagControlsAreDisabled()
    {
        var page = RenderEditor(EditorFixtures.WithTags(EditorFixtures.NotMine() with { SpecVersion = 9 }, "theirs"));
        Navigate(page, "Package");

        Assert.True(page.Find("input[aria-label='New tag']").HasAttribute("disabled"));
        Assert.True(page.Find("button[aria-label='Remove tag theirs']").HasAttribute("disabled"));
    }
}
