using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// #1290: renaming a loaded macro or snippet at 26+ — every read and instance follows, the file moves
/// to the path the new id implies (at 26 the map points at it; at 28 it is the id), a pending edit
/// made before the rename survives on the moved file, a refusal is said in the row, and undo, the
/// pane's Discard and a reload behave as for a node rename (#1225).
/// </summary>
public class TemplatesRenameMacroSnippetTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse? fixture = null)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture ?? EditorFixtures.V26Gated());
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        WorkflowService.GetCatalogSchemasAsync().Returns(EditorCatalogSchemas.CatalogSchemas);
        return Render<Templates>();
    }

    private void Select(IRenderedComponent<Templates> page, string key)
    {
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = key;
        page.Render();
    }

    private string SelectedKey => Services.GetRequiredService<WorkflowEditorSession>().SelectedKey!;

    private void Rename(IRenderedComponent<Templates> page, string noun, string path, string from, string to)
    {
        Select(page, path);
        page.Find($"fluent-button[aria-label='Rename {noun} {from}']").Click();
        page.Find($"fluent-text-field[aria-label='New id for {noun} {from}']").Change(to);
        page.Find("fluent-button.text-rename__ok").Click();
    }

    private static string? RenameError(IRenderedComponent<Templates> page) =>
        page.FindAll(".text-rename .field-error").SingleOrDefault()?.TextContent.Trim();

    private static void Publish(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

    private static IReadOnlyList<string> Refusals(IRenderedComponent<Templates> page) =>
        page.FindAll(".fluent-messagebar-message li").Select(item => item.TextContent.Trim()).ToList();

    private WorkflowPackagePublishRequest? sent;

    private void CapturePublish() =>
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.11.2", "acct-1234567890ab@v2026.11.2"),
                Array.Empty<string>()));

    private JsonElement Manifest => JsonDocument.Parse(sent!.Manifest.GetRawText()).RootElement;

    private PF.WorkflowPackageValidator.ValidationResult Validated() =>
        PF.WorkflowPackageValidator.Validate(
            PF.WorkflowPackageManifestJson.Read(sent!.Manifest.GetRawText(), "sent", PF.WorkflowPackageValidator.AcceptedSpecVersions),
            sent.Files.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            null,
            EditorFormatsCatalog.Catalog);

    private static string AssessmentInstance(JsonElement manifest) =>
        manifest.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("id").GetString() == "assessment")
            .GetProperty("macros")[0] is var m && m.ValueKind == JsonValueKind.String ? m.GetString()! : m.GetProperty("id").GetString()!;

    [Fact]
    public void At26_RenamingAMacro_RewritesItsReadsAndInstance_MovesItsFile_AndPublishesClean()
    {
        CapturePublish();
        var page = RenderEditor();

        Rename(page, "macro", "macros/closing.md", "closing", "sign_off");

        Assert.Null(RenameError(page));
        Assert.Equal("macros/sign_off.md", SelectedKey);
        Assert.Equal("closing", page.Find("[data-text-renamed]").GetAttribute("data-text-renamed"));

        Publish(page);
        Assert.Empty(Refusals(page));
        Assert.NotNull(sent);
        Assert.Equal("macros/sign_off.md", Manifest.GetProperty("macros").GetProperty("sign_off").GetString());
        Assert.False(Manifest.GetProperty("macros").TryGetProperty("closing", out _));
        Assert.Equal("sign_off", AssessmentInstance(Manifest));
        Assert.Contains("macro.sign_off.content", sent.Files["prompts/assessment.md"]);
        Assert.True(sent.Files.ContainsKey("macros/sign_off.md"));
        Assert.False(sent.Files.ContainsKey("macros/closing.md"));
        Assert.Empty(Validated().Errors);
    }

    [Fact]
    public void At28_RenamingASnippet_RewritesItsReaders_TheIdList_AndMovesItsFile()
    {
        CapturePublish();
        var page = RenderEditor(EditorFixtures.V28());

        Rename(page, "snippet", "snippets/standards.md", "standards", "house_standards");

        Assert.Equal("snippets/house_standards.md", SelectedKey);
        Publish(page);
        Assert.Empty(Refusals(page));
        Assert.NotNull(sent);
        var snippets = Manifest.GetProperty("snippets").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("house_standards", snippets);
        Assert.DoesNotContain("standards", snippets);
        Assert.Contains("snippet.house_standards.content", sent.Files["prompts/assessment.md"]);
        Assert.True(sent.Files.ContainsKey("snippets/house_standards.md"));
        Assert.Empty(Validated().Errors);
    }

    [Fact]
    public void APendingEdit_FollowsTheRename_ToTheMovedFile()
    {
        CapturePublish();
        var page = RenderEditor();
        Select(page, "macros/closing.md");
        page.Find(".pane-editor fluent-text-area").Change("Closing, edited: {{ profile.name }}.");

        Rename(page, "macro", "macros/closing.md", "closing", "sign_off");
        Publish(page);

        Assert.NotNull(sent);
        Assert.EndsWith("Closing, edited: {{ profile.name }}.", sent!.Files["macros/sign_off.md"]);
        Assert.Empty(Validated().Errors);
    }

    [Theory]
    [InlineData("Sign-Off", "'Sign-Off' is not a valid id — a lowercase letter, then lowercase letters, digits or underscores (a template reads it as a Scriban member).")]
    [InlineData("tool_note", "A macro or macro folder 'tool_note' already exists.")]
    public void ARename_IsRefused_InTheRow_AndNothingMoves(string to, string refusal)
    {
        var page = RenderEditor();
        Rename(page, "macro", "macros/closing.md", "closing", to);

        Assert.Equal(refusal, RenameError(page));
        Assert.Equal("macros/closing.md", SelectedKey);
        Assert.Empty(page.FindAll("[data-text-renamed]"));
    }

    [Fact]
    public void Undo_TakesTheRenameBack()
    {
        var page = RenderEditor();
        Rename(page, "macro", "macros/closing.md", "closing", "sign_off");

        page.Find(".undo-button").Click();

        Select(page, "macros/closing.md");
        Assert.Empty(page.FindAll("[data-text-renamed]"));
        Assert.NotNull(page.Find("fluent-button[aria-label='Rename macro closing']"));
    }

    [Fact]
    public void ThePanesDiscard_RenamesItBack()
    {
        var page = RenderEditor();
        Rename(page, "snippet", "snippets/standards.md", "standards", "house_standards");

        page.Find(".pane-discard-button").Click();
        page.Find(".pane-discard-button").Click();

        Assert.Equal("snippets/standards.md", SelectedKey);
        Assert.Empty(page.FindAll("[data-text-renamed]"));
    }

    [Fact]
    public void AReload_RestoresTheRename_WithItsDraft()
    {
        var page = RenderEditor();
        Rename(page, "macro", "macros/closing.md", "closing", "sign_off");

        var saved = JSInterop.Invocations["localStorage.setItem"]
            .Where(i => i.Arguments[0] is string key && key.StartsWith("workflow-editor-draft:", StringComparison.Ordinal))
            .Select(i => (Key: (string)i.Arguments[0]!, Json: (string)i.Arguments[1]!))
            .Last();
        JSInterop.Setup<string?>("localStorage.getItem", saved.Key).SetResult(saved.Json);
        var again = Render<Templates>();

        Select(again, "macros/sign_off.md");
        Assert.Equal("closing", again.Find("[data-text-renamed]").GetAttribute("data-text-renamed"));
    }

    [Fact]
    public void Below26_ThereIsNoRename()
    {
        var page = RenderEditor(EditorFixtures.V25());
        Select(page, "macros/closing.md");
        Assert.Empty(page.FindAll(".text-rename"));
    }
}
