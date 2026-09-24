using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #747: the shared-preludes surface. A prelude is free-form text in the
/// preludes map (id -> file), prepended to a prompt that names it. The pane
/// creates/edits/removes preludes; a prompt's pane selects one; removal is
/// refused while a prompt reads it; ComposeManifest writes both.
/// </summary>
public class TemplatesPreludesTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse fixture)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture);
        return Render<Templates>();
    }

    private static void Navigate(IRenderedComponent<Templates> page, string label) =>
        page.FindAll("button.editor-nav__item")
            .First(button => button.TextContent.Replace("●", string.Empty).Trim() == label)
            .Click();

    private static void Publish(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

    private static IReadOnlyList<string> Refusals(IRenderedComponent<Templates> page) =>
        page.FindAll(".fluent-messagebar-message li").Select(item => item.TextContent.Trim()).ToList();

    private WorkflowPackagePublishRequest? sent;

    private void CapturePublish() =>
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.08.2", "acct-1234567890ab@v2026.08.2"),
                Array.Empty<string>()));

    private Consultologist.PackageFormat.WorkflowPackageValidator.ValidationResult Validated()
    {
        var manifest = JsonSerializer.Deserialize<Consultologist.PackageFormat.WorkflowPackageManifest>(
            sent!.Manifest.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return Consultologist.PackageFormat.WorkflowPackageValidator.Validate(
            manifest, sent.Files.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), new Dictionary<string, string>(StringComparer.Ordinal));
    }

    private static JsonElement Manifest(WorkflowPackagePublishRequest request) =>
        JsonDocument.Parse(request.Manifest.GetRawText()).RootElement;

    private static JsonElement Prompt(WorkflowPackagePublishRequest request, string id) =>
        Manifest(request).GetProperty("prompts").EnumerateArray().Single(p => p.GetProperty("id").GetString() == id);

    [Fact]
    public void ThePane_ListsPreludes_AndShowsAnEditableBody()
    {
        var page = RenderEditor(EditorFixtures.V7Preludes());
        Navigate(page, "guidance");

        Assert.Contains("Use short SNOMED search terms.", page.Markup);
        Assert.Contains("preludes/guidance.md", page.Markup);
    }

    [Fact]
    public void EditingAPreludeBody_SendsTheNewTextAtItsPath()
    {
        var page = RenderEditor(EditorFixtures.V7Preludes());
        CapturePublish();
        Navigate(page, "guidance");

        page.Find("fluent-text-area").Change("Prefer active SNOMED concepts.");
        Publish(page);

        Assert.NotNull(sent);
        Assert.Equal("Prefer active SNOMED concepts.", sent!.Files["preludes/guidance.md"]);
        var result = Validated();
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void CreatingAPrelude_DeclaresItAndWritesItsFile()
    {
        var page = RenderEditor(EditorFixtures.V7Preludes());
        CapturePublish();

        Navigate(page, "+ Prelude");
        page.Find("fluent-text-field[placeholder='snomed_tool_guidance']").Change("closing_note");
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Create prelude")).Click();
        page.Find("fluent-text-area").Change("Signed by the consultant.");
        Publish(page);

        Assert.NotNull(sent);
        var preludes = Manifest(sent!).GetProperty("preludes");
        Assert.Equal("preludes/closing_note.md", preludes.GetProperty("closing_note").GetString());
        Assert.Equal("Signed by the consultant.", sent!.Files["preludes/closing_note.md"]);
        // An unreferenced prelude is tolerated by the validator.
        var result = Validated();
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    // #841: the usage list lives in the prelude's pane, not the nav.
    [Fact]
    public void PreludeUsage_ShowsInThePane_NotTheNav()
    {
        var page = RenderEditor(EditorFixtures.V7Preludes());

        // The old nav usage line ("used by '…'") is gone before a prelude is open.
        Assert.DoesNotContain("used by '", page.Markup);

        // A referenced prelude names its readers in the pane.
        Navigate(page, "guidance");
        Assert.Contains("Used by 'draft-section'", page.Markup);

        // An unused prelude gets the pane hint instead, and keeps its nav remove link.
        Navigate(page, "unused");
        Assert.Contains("Not yet used by any prompt", page.Markup);
        Assert.Contains(page.FindAll(".editor-nav__restore"), button => button.TextContent == "(remove)");
    }

    [Fact]
    public async Task RemovingAReferencedPrelude_IsRefused_AndTheOrphanRemovesCleanly()
    {
        var page = RenderEditor(EditorFixtures.V7Preludes());
        CapturePublish();

        // guidance is read by the draft-section prompt: its pane names the reader
        // (#841 moved this off the nav), and it has no remove link.
        Navigate(page, "guidance");
        Assert.Contains("Used by 'draft-section'", page.Markup);

        // unused is an orphan: remove it, and it drops from the map and files.
        page.FindAll(".editor-nav__restore").First(button => button.TextContent == "(remove)").Click();
        Publish(page);

        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        var preludes = Manifest(sent!).GetProperty("preludes");
        Assert.False(preludes.TryGetProperty("unused", out _));
        Assert.True(preludes.TryGetProperty("guidance", out _));
        Assert.DoesNotContain("preludes/unused.md", sent!.Files.Keys);
        var result = Validated();
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void APromptSelectsItsPrelude_AndClearingItDropsTheKey()
    {
        var page = RenderEditor(EditorFixtures.V7Preludes());
        CapturePublish();
        Navigate(page, "draft-section");

        var select = page.Find("select[aria-label='Prelude for prompt draft-section']");
        Assert.Contains("guidance", select.QuerySelectorAll("option").Select(option => option.GetAttribute("value")));
        Assert.Contains("unused", select.QuerySelectorAll("option").Select(option => option.GetAttribute("value")));

        // Point it at the other prelude, then publish.
        select.Change("unused");
        Publish(page);
        Assert.NotNull(sent);
        Assert.Equal("unused", Prompt(sent!, "draft-section").GetProperty("prelude").GetString());
    }

    [Fact]
    public void ClearingAPromptsPrelude_DropsThePreludeKey()
    {
        var page = RenderEditor(EditorFixtures.V7Preludes());
        CapturePublish();
        Navigate(page, "draft-section");

        page.Find("select[aria-label='Prelude for prompt draft-section']").Change(string.Empty);
        Publish(page);

        Assert.NotNull(sent);
        Assert.False(Prompt(sent!, "draft-section").TryGetProperty("prelude", out _));
        var result = Validated();
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }
}
