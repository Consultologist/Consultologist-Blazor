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
        // A5 (#1018): the one upgrade is the 24→25 rewrite; the migrator maps schemas
        // through the catalogue schemas and the desk at 25 resolves formats.
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        WorkflowService.GetCatalogSchemasAsync().Returns(EditorCatalogSchemas.CatalogSchemas);
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
            manifest, sent.Files.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), new Dictionary<string, string>(StringComparer.Ordinal), null, EditorFormatsCatalog.Catalog);
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
    public async Task CreatingAPrelude_Below23_IsRefusedByName()
    {
        // #978: the editor used to write a new prelude at preludes/<id>.md, which
        // the publish door never accepted. Creation is retired: the door stays
        // visible below 23 and says where shared text goes now; nothing is staged.
        var page = RenderEditor(EditorFixtures.V7Preludes());
        CapturePublish();

        Navigate(page, "+ Prelude");

        Assert.Contains("A prelude publishes only as a node macro from specVersion 23.", page.Find(".prelude-retired").TextContent);
        Assert.Contains("Upgrade to specVersion 26", page.Find(".prelude-retired").TextContent);
        Assert.Empty(page.FindAll("fluent-text-field[placeholder='snomed_tool_guidance']"));
        Assert.DoesNotContain(page.FindAll("fluent-button"), button => button.TextContent.Contains("Create prelude"));
        // Nothing pending: Publish has nothing to send.
        Publish(page);
        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
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

        // An unused prelude gets the pane hint and a Remove prelude button in its
        // pane; the old nav remove link is gone.
        Navigate(page, "unused");
        Assert.Contains("Not yet used by any prompt", page.Markup);
        Assert.Contains(page.FindAll("fluent-button"), button => button.TextContent.Contains("Remove prelude"));
        Assert.DoesNotContain(page.FindAll(".editor-nav__restore"), button => button.TextContent == "(remove)");
    }

    [Fact]
    public async Task RemovingAReferencedPrelude_IsRefused_AndTheOrphanRemovesCleanly()
    {
        var page = RenderEditor(EditorFixtures.V7Preludes());
        CapturePublish();

        // guidance is read by the draft-section prompt: its pane names the reader
        // (#841 moved this off the nav), and offers no Remove button while referenced.
        Navigate(page, "guidance");
        Assert.Contains("Used by 'draft-section'", page.Markup);
        Assert.DoesNotContain(page.FindAll("fluent-button"), button => button.TextContent.Contains("Remove prelude"));

        // unused is an orphan: remove it from its pane, and it drops from the map and files.
        Navigate(page, "unused");
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Remove prelude")).Click();
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

        var select = page.Find("fluent-select[aria-label='Prelude for prompt draft-section']");
        Assert.Contains("guidance", select.QuerySelectorAll("option, fluent-option").Select(option => option.GetAttribute("value")));
        Assert.Contains("unused", select.QuerySelectorAll("option, fluent-option").Select(option => option.GetAttribute("value")));

        // Point it at the other prelude, then publish.
        select.Change("unused");
        Publish(page);
        Assert.NotNull(sent);
        Assert.Equal("unused", Prompt(sent!, "draft-section").GetProperty("prelude").GetString());
    }

    // ----- #963: the migration to node macros ---------------------------------

    private static void UpgradeTo(IRenderedComponent<Templates> page, int target) =>
        page.FindAll("fluent-button")
            .First(button => button.TextContent.Contains($"Upgrade to specVersion {target}", StringComparison.Ordinal))
            .Click();

    private static JsonElement Node(WorkflowPackagePublishRequest request, string id) =>
        Manifest(request).GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("id").GetString() == id);

    private static IReadOnlyList<string> NavGroups(IRenderedComponent<Templates> page) =>
        page.FindAll(".editor-nav__group").Select(group => group.TextContent.Trim()).ToList();

    /// <summary>The V7Preludes shape at a version, with an optional library macro declared (for the collision) and an optional existing node macro on draft-section.</summary>
    private static WorkflowPackageContentResponse PreludesAt(int specVersion, bool declareGuidanceMacro = false, bool existingNodeMacro = false)
    {
        var macros = declareGuidanceMacro
            ? """, "macros": [ { "id": "guidance", "label": "Guidance", "file": "macros/guidance.md" } ]"""
            : existingNodeMacro
                ? """, "macros": [ { "id": "tool_guidance", "label": "Tool guidance", "file": "macros/tool_guidance.md" } ]"""
                : "";
        var nodeMacros = existingNodeMacro ? """, "macros": [ "tool_guidance" ]""" : "";
        var manifest = $$"""
            {
              "name": "acct-1234567890ab",
              "version": "v2026.07.1",
              "specVersion": {{specVersion}},
              "tags": [],
              "templating": { "engine": "scriban", "engineVersion": "7.2.5" },
              "preludes": { "guidance": "preludes/guidance.md", "unused": "preludes/unused.md" }{{macros}},
              "inputs": [ { "id": "consult_draft", "label": "Consult draft", "required": true } ],
              "data": { "standards": "data/standards/" },
              "prompts": [
                { "id": "draft-section", "file": "prompts/draft-section.md", "variables": ["section_name", "consult_draft"], "prelude": "guidance" }
              ],
              "results": [ { "id": "consult_note", "node": "node:assemble-note", "label": "Consultation note" } ],
              "nodes": [
                { "id": "draft-section", "forEach": "data:standards", "label": "Drafting section", "prompt": "draft-section",
                  "bindings": { "section_name": "item:name", "consult_draft": "input:consult_draft" }{{nodeMacros}} },
                { "id": "assemble-note", "label": "Assembling note", "aggregate": ["node:draft-section"] }
              ]
            }
            """;
        var files = new List<(string, string)> { ("preludes/guidance.md", "Use short SNOMED search terms."), ("preludes/unused.md", "An unused prelude.") };
        if (declareGuidanceMacro) files.Add(("macros/guidance.md", "A macro already called guidance."));
        if (existingNodeMacro) files.Add(("macros/tool_guidance.md", "Tool guidance."));
        return EditorFixtures.Package(manifest, specVersion, files.ToArray());
    }

    [Fact]
    public void UpgradingPast23_MigratesPreludesToNodeMacros_ThenOnToSnippetsAt25()
    {
        // #963 made the 23 step; A5 (#1018) chains it into the 25 rewrite, where
        // a macro placed on no output becomes a snippet the prompt reads inline.
        // (The byte-for-byte 24 composition — the bare macro first — is proved by
        // MigratingOnDemandAt23_KeepsThePreludeAheadOfExistingNodeMacros.)
        var page = RenderEditor(EditorFixtures.V7Preludes());
        CapturePublish();

        UpgradeTo(page, Templates.NewestSpecVersion);

        // Said once, up front: the 24 step, then every 25 rewrite.
        var notice = page.Find(".migration-notice").TextContent;
        Assert.Contains("Through 24: Preludes migrated to node macros. 'guidance' → macro 'guidance' before 'draft-section'.", notice);
        Assert.Contains("Prelude 'unused' is used by no prompt and was dropped.", notice);
        Assert.Contains("now the snippet 'guidance'", notice);
        // The Preludes group retires with its last prelude; guidance is a snippet now.
        Assert.DoesNotContain("Preludes", NavGroups(page));
        Assert.Contains("Snippets", NavGroups(page));
        Assert.Contains(page.FindAll("button.editor-nav__item"), button => button.TextContent.Contains("guidance"));

        Publish(page);

        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        var manifest = Manifest(sent!);
        Assert.Equal(26, manifest.GetProperty("specVersion").GetInt32());
        Assert.False(manifest.TryGetProperty("preludes", out _));
        Assert.False(manifest.TryGetProperty("macros", out _));
        // #1164: the chain lands at 26 — prompts[] retired, the node names its file; the snippet opens with its header.
        Assert.False(manifest.TryGetProperty("prompts", out _));
        Assert.Equal("prompts/draft-section.md", manifest.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("id").GetString() == "draft_section").GetProperty("prompt").GetString());
        Assert.Equal("snippets/guidance.md", manifest.GetProperty("snippets").GetProperty("guidance").GetString());
        Assert.EndsWith("}\nUse short SNOMED search terms.", sent!.Files["snippets/guidance.md"]);
        Assert.StartsWith("{{ snippet.guidance.content }}", sent.Files["prompts/draft-section.md"]);
        Assert.DoesNotContain(sent.Files.Keys, path => path.StartsWith("preludes/", StringComparison.Ordinal));
        var result = Validated();
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void MigratingOnDemandAt23_KeepsThePreludeAheadOfExistingNodeMacros()
    {
        // A package already at 23 with both a prelude and a node macro: the
        // Preludes pane offers the migration, and the migrated entry lands FIRST
        // — the order PromptTemplateRenderer.Compose gave the prelude.
        var page = RenderEditor(PreludesAt(23, existingNodeMacro: true));
        CapturePublish();
        Assert.Contains("Preludes", NavGroups(page));
        Navigate(page, "guidance");

        page.Find("fluent-button[aria-label='Migrate preludes to node macros']").Click();
        Assert.DoesNotContain("Preludes", NavGroups(page));
        Publish(page);

        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        var entries = Node(sent!, "draft-section").GetProperty("macros").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(new[] { "guidance", "tool_guidance" }, entries);
        var result = Validated();
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public async Task AnIdCollision_RefusesTheUpgrade_AndStagesNothing()
    {
        var page = RenderEditor(PreludesAt(7, declareGuidanceMacro: true));
        CapturePublish();

        UpgradeTo(page, Templates.NewestSpecVersion);

        Assert.Contains("Prelude 'guidance' has the id of a declared macro; rename one, then upgrade.", page.Find(".upgrade-refusal").TextContent);
        // Nothing staged: the rung is still on offer, the preludes untouched.
        Assert.Contains(page.FindAll("fluent-button"), button => button.TextContent.Contains("Upgrade to specVersion 26"));
        Assert.Contains("Preludes", NavGroups(page));
        Publish(page);
        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
    }

    [Fact]
    public void At24_WithNoPreludes_ThePreludesGroupIsGone()
    {
        var page = RenderEditor(EditorFixtures.V11Macro() with { SpecVersion = 24, Manifest = JsonDocument.Parse(EditorFixtures.V11Macro().Manifest.GetRawText().Replace("\"specVersion\": 11", "\"specVersion\": 24")).RootElement.Clone() });

        Assert.DoesNotContain("Preludes", NavGroups(page));
        Assert.DoesNotContain(page.FindAll("button.editor-nav__item"), button => button.TextContent.Contains("+ Prelude"));
    }

    [Fact]
    public void At23_WithPreludes_TheGroupStays_ButOffersNoNewPrelude()
    {
        var page = RenderEditor(PreludesAt(23));

        Assert.Contains("Preludes", NavGroups(page));
        Assert.DoesNotContain(page.FindAll("button.editor-nav__item"), button => button.TextContent.Contains("+ Prelude"));
    }

    [Fact]
    public void ClearingAPromptsPrelude_DropsThePreludeKey()
    {
        var page = RenderEditor(EditorFixtures.V7Preludes());
        CapturePublish();
        Navigate(page, "draft-section");

        page.Find("fluent-select[aria-label='Prelude for prompt draft-section']").Change(string.Empty);
        Publish(page);

        Assert.NotNull(sent);
        Assert.False(Prompt(sent!, "draft-section").TryGetProperty("prelude", out _));
        var result = Validated();
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }
}
