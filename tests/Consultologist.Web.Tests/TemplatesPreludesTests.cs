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
    public void UpgradingPast23_MigratesPreludesToNodeMacros_ByteForByte()
    {
        var page = RenderEditor(EditorFixtures.V7Preludes());
        CapturePublish();

        UpgradeTo(page, 24);

        // Said once, up front: where guidance went, and that unused was dropped.
        var notice = page.Find(".prelude-migration-notice").TextContent;
        Assert.Contains("'guidance' → macro 'guidance' before 'draft-section'", notice);
        Assert.Contains("Prelude 'unused' is used by no prompt and was dropped.", notice);
        // The Preludes group retires with its last prelude; the macro is in the library.
        Assert.DoesNotContain("Preludes", NavGroups(page));
        Assert.Contains(page.FindAll("button.editor-nav__item"), button => button.TextContent.Contains("guidance"));

        Publish(page);

        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        var manifest = Manifest(sent!);
        Assert.False(manifest.TryGetProperty("preludes", out _));
        Assert.False(Prompt(sent!, "draft-section").TryGetProperty("prelude", out _));
        var macro = manifest.GetProperty("macros").EnumerateArray().Single();
        Assert.Equal("guidance", macro.GetProperty("id").GetString());
        Assert.Equal("macros/guidance.md", macro.GetProperty("file").GetString());
        // The bare form, first: before, always — the prelude's exact composition.
        Assert.Equal("guidance", Node(sent!, "draft-section").GetProperty("macros").EnumerateArray().Single().GetString());
        // The text moved to macros/ (the publish door accepts no preludes/ path).
        Assert.Equal("Use short SNOMED search terms.", sent!.Files["macros/guidance.md"]);
        Assert.DoesNotContain("preludes/guidance.md", sent.Files.Keys);
        Assert.DoesNotContain("preludes/unused.md", sent.Files.Keys);
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

        UpgradeTo(page, 24);

        Assert.Contains("Prelude 'guidance' has the id of a declared macro; rename one, then upgrade.", page.Find(".upgrade-refusal").TextContent);
        // Nothing staged: the rung is still on offer, the preludes untouched.
        Assert.Contains(page.FindAll("fluent-button"), button => button.TextContent.Contains("Upgrade to specVersion 24"));
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
