using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// #1225: renaming a node at 26+ — every read, gate and structural site follows, the prompt file
/// moves to prompts/&lt;new&gt;.md (#1267), pending edits made before the rename survive under the new
/// id, and undo and the pane's Discard take it back.
/// </summary>
public class TemplatesV26NodeRenameTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse? fixture = null)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture ?? EditorFixtures.V26());
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        return Render<Templates>();
    }

    private void Select(IRenderedComponent<Templates> page, string key)
    {
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = key;
        page.Render();
    }

    private string SelectedKey => Services.GetRequiredService<WorkflowEditorSession>().SelectedKey!;

    private void Rename(IRenderedComponent<Templates> page, string from, string to)
    {
        Select(page, $"node:{from}");
        page.Find($"fluent-button[aria-label='Rename node {from}']").Click();
        page.Find($"fluent-text-field[aria-label='New id for node {from}']").Change(to);
        page.Find("fluent-button.node-rename__ok").Click();
    }

    private static string? RenameError(IRenderedComponent<Templates> page) =>
        page.FindAll(".node-rename .field-error").SingleOrDefault()?.TextContent.Trim();

    private static void Publish(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

    private WorkflowPackagePublishRequest? sent;

    private void CapturePublish() =>
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.10.2", "acct-1234567890ab@v2026.10.2"),
                Array.Empty<string>()));

    private JsonElement Node(string id) =>
        JsonDocument.Parse(sent!.Manifest.GetRawText()).RootElement.GetProperty("nodes").EnumerateArray()
            .Single(n => n.GetProperty("id").GetString() == id);

    private bool HasNode(string id) =>
        JsonDocument.Parse(sent!.Manifest.GetRawText()).RootElement.GetProperty("nodes").EnumerateArray()
            .Any(n => n.GetProperty("id").GetString() == id);

    private PF.WorkflowPackageValidator.ValidationResult Validated()
    {
        var manifest = JsonSerializer.Deserialize<PF.WorkflowPackageManifest>(sent!.Manifest.GetRawText(), PF.WorkflowPackageManifestJson.ReadOptions)!;
        return PF.WorkflowPackageValidator.Validate(
            manifest,
            sent.Files.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            null,
            EditorFormatsCatalog.Catalog);
    }

    [Fact]
    public void RenamingANode_RewritesItsReaders_MovesItsPrompt_AndPublishesClean()
    {
        CapturePublish();
        var page = RenderEditor();

        Rename(page, "extract", "concept_extract");
        Assert.Equal("node:concept_extract", SelectedKey);
        Publish(page);

        Assert.NotNull(sent);
        Assert.False(HasNode("extract"));
        Assert.Equal("prompts/concept_extract.md", Node("concept_extract").GetProperty("prompt").GetString());
        Assert.True(sent!.Files.ContainsKey("prompts/concept_extract.md"));
        Assert.False(sent.Files.ContainsKey("prompts/extract.md"));
        Assert.Contains("node.concept_extract.value", sent.Files["prompts/assessment.md"]);
        Assert.DoesNotContain("node.extract.", sent.Files["prompts/assessment.md"]);
        Assert.Equal("node.concept_extract", Node("coverage").GetProperty("with").GetProperty("of").GetString());
        Assert.Empty(Validated().Errors);
    }

    [Fact]
    public void RenamingALoopBodyNode_UpdatesTheLoop_AndMovesItsMigratedPrompt()
    {
        CapturePublish();
        var page = RenderEditor();

        // The 25→26 migration kept section_notes' prompt as prompts/section.md; the rename brings it in step.
        Rename(page, "section_notes", "notes_per_section");
        Publish(page);

        var loop = Node("sections");
        Assert.Contains("notes_per_section", loop.GetProperty("nodes").EnumerateArray().Select(n => n.GetString()));
        Assert.Equal("node.notes_per_section", loop.GetProperty("ret").GetString());
        Assert.Equal("prompts/notes_per_section.md", Node("notes_per_section").GetProperty("prompt").GetString());
        Assert.False(sent!.Files.ContainsKey("prompts/section.md"));
        Assert.Empty(Validated().Errors);
    }

    [Fact]
    public void At27_RenamingABranchNode_UpdatesTheCaseBranch()
    {
        CapturePublish();
        var page = RenderEditor(EditorFixtures.V27Route());

        Rename(page, "plan_section", "narrative_section");
        Publish(page);

        var branch = Node("route").GetProperty("branches").EnumerateArray().First();
        Assert.Equal(new[] { "narrative_section" }, branch.GetProperty("nodes").EnumerateArray().Select(n => n.GetString()));
        Assert.Equal("node.narrative_section", branch.GetProperty("ret").GetString());
        Assert.Equal("prompts/narrative_section.md", Node("narrative_section").GetProperty("prompt").GetString());
        Assert.Empty(Validated().Errors);
    }

    [Theory]
    [InlineData("Bad-Id", "Node id 'Bad-Id' must be snake_case")]
    [InlineData("scope", "A node named 'scope' already exists.")]
    [InlineData("section", "A prompt file 'prompts/section.md' already exists — pick another node id.")]
    public void ARename_IsRefused_ForABadId_ADuplicate_OrAnExistingPromptFile(string to, string error)
    {
        var page = RenderEditor();

        Rename(page, "extract", to);

        Assert.StartsWith(error, RenameError(page));
        Assert.Equal("node:extract", SelectedKey);
    }

    [Fact]
    public void APendingPromptEdit_FollowsTheRename_ToTheMovedFile()
    {
        CapturePublish();
        var page = RenderEditor();
        Select(page, "node:extract");
        page.FindAll(".node-section").Single(s => s.QuerySelector(".node-section__head")?.TextContent.Trim() == "Prompt text")
            .QuerySelector("fluent-text-area")!.Change("Extract every concept from:\n{{ input.notes }}");

        Rename(page, "extract", "concept_extract");
        Publish(page);

        Assert.Equal("Extract every concept from:\n{{ input.notes }}", sent!.Files["prompts/concept_extract.md"]);
    }

    [Fact]
    public void TheRenamedPane_SaysWhatHappened()
    {
        var page = RenderEditor();

        Rename(page, "extract", "concept_extract");

        Assert.Equal(
            "renamed from extract — a rerun compares it as a different node; the prompt file moves to prompts/concept_extract.md.",
            page.Find("[data-node-renamed='extract']").TextContent.Trim());
    }

    [Fact]
    public void Undo_TakesTheRenameBack()
    {
        var page = RenderEditor();
        Rename(page, "extract", "concept_extract");

        page.Find(".undo-button").Click();

        Select(page, "node:extract");
        Assert.Contains("extract", page.Find(".pane-identity code").TextContent);
        Assert.Empty(page.FindAll("[data-node-renamed]"));
    }

    [Fact]
    public void ThePanesDiscard_RenamesItBack()
    {
        var page = RenderEditor();
        Rename(page, "extract", "concept_extract");

        // The pane's Discard arms, then acts.
        page.Find(".pane-discard-button").Click();
        page.Find(".pane-discard-button").Click();

        Select(page, "node:extract");
        Assert.Equal("extract", page.Find(".pane-identity code").TextContent.Trim());
        Assert.Empty(page.FindAll("[data-node-renamed]"));
    }

    [Fact]
    public void Below26_ThereIsNoRename()
    {
        var page = RenderEditor(EditorFixtures.V25());
        Select(page, "node:extract");

        Assert.Empty(page.FindAll("fluent-button.node-rename__start"));
    }
}
