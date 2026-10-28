using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// v26 (#1205, design § 12): the rendered preview judges the three gates over a pick — a suppressed
/// prompt read, an inert macro, a gated-off snippet or folder item — only once every read a gate makes
/// is picked, and names each gate it applied or waits on. With it, the node instance's gate is `if` at
/// 26 (read, labelled, written). Rendered through the #1209 seam; the 25 preview is pinned unchanged.
/// </summary>
public class TemplatesV26PreviewGatesTests : ClientRenderTestContext
{
    private const string KindSelect = "fluent-select[aria-label='Preview with input.kind']";
    private const string ItemSelect = "fluent-select.rendered-preview__item-pick";

    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse? fixture = null)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture ?? EditorFixtures.V26Gated());
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        return Render<Templates>();
    }

    private void Select(IRenderedComponent<Templates> page, string key)
    {
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = key;
        page.Render();
    }

    private static string Rendered(IRenderedComponent<Templates> page) =>
        page.Find(".pane-preview pre.rendered-preview__text").TextContent;

    private static IReadOnlyList<(string Kind, string Key, string Text, bool Waiting)> Gates(IRenderedComponent<Templates> page) =>
        page.FindAll(".rendered-preview__gate")
            .Select(li => (li.GetAttribute("data-gate-kind")!, li.GetAttribute("data-gate-key")!, li.TextContent.Trim(), li.ClassList.Contains("rendered-preview__gate--waiting")))
            .ToList();

    private static void Publish(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

    private WorkflowPackagePublishRequest? sent;

    private void CapturePublish() =>
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.10.2", "acct-1234567890ab@v2026.10.2"),
                Array.Empty<string>()));

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

    private static JsonElement NodeMacro(WorkflowPackagePublishRequest request, string nodeId, int index) =>
        JsonDocument.Parse(request.Manifest.GetRawText()).RootElement.GetProperty("nodes").EnumerateArray()
            .Single(n => n.GetProperty("id").GetString() == nodeId).GetProperty("macros")[index];

    // ---- the preview over a pick --------------------------------------------------------

    [Fact]
    public void WithNoPick_TheBytesAreUnchanged_AndEveryGateWaits_NamingThePick()
    {
        var page = RenderEditor();
        Select(page, "node:assessment");

        var gated = Rendered(page);
        Assert.Contains("Closing: ⟨profile.name⟩.", gated);
        Assert.Contains("Follow the local standards.", gated);
        Assert.Contains("HPI,Plan,", gated);

        var gates = Gates(page);
        Assert.All(gates, g => Assert.True(g.Waiting));
        // The pane's own reads: the closing macro, the iterated macro folder's gated item, the snippet, the folder's gated item.
        Assert.Equal(new[] { "closing", "notes/plan", "standards", "sections/plan" }, gates.Select(g => g.Key));
        Assert.All(gates, g => Assert.StartsWith("pick input.kind to judge", g.Text));
        // The gate's input earns a control although the text itself never reads it.
        Assert.NotNull(page.Find(KindSelect));
    }

    [Fact]
    public void WithAFailingPick_TheThreeGatesApply_AndAreNamed()
    {
        var page = RenderEditor();
        Select(page, "node:assessment");

        page.Find(KindSelect).Change("follow_up");

        var rendered = Rendered(page);
        Assert.DoesNotContain("Closing:", rendered);
        Assert.DoesNotContain("Follow the local standards.", rendered);
        Assert.Contains("HPI,", rendered);
        Assert.DoesNotContain("Plan,", rendered);

        var gates = Gates(page);
        Assert.All(gates, g => Assert.False(g.Waiting));
        Assert.Equal(new[] { ("macro", "closing"), ("macro", "notes/plan"), ("snippet", "standards"), ("item", "sections/plan") }, gates.Select(g => (g.Kind, g.Key)));
        Assert.Equal("macro 'closing' is inert for this pick: its if (input.kind == \"new\") does not hold — .prompt and .content render empty", gates[0].Text);
        Assert.Equal("snippet 'standards' renders empty for this pick: its if (input.kind == \"new\") does not hold", gates[2].Text);
        Assert.Equal("item 'plan' of folder 'sections' is out of the loop for this pick (its direct read renders empty): its if (input.kind == \"new\") does not hold", gates[3].Text);
        // The iterated macro folder's inert item: its content empty in the loop.
        Assert.DoesNotContain("Plan notes.", rendered);
        Assert.Contains("History notes.", rendered);

        page.Find(KindSelect).Change("new");
        Assert.Contains("Closing: ⟨profile.name⟩.", Rendered(page));
        Assert.Contains("HPI,Plan,", Rendered(page));
        Assert.Empty(Gates(page));
    }

    [Fact]
    public void AnInstanceGate_IsJudgedOnTheReaderNodesPane_OverTheItemPick()
    {
        var page = RenderEditor();
        Select(page, "node:section_notes");

        // The migrated instance (forItem hpi → if item.id == "hpi") waits on the item pick.
        var waiting = Assert.Single(Gates(page));
        Assert.Equal(("instance", "tool_note", true), (waiting.Kind, waiting.Key, waiting.Waiting));
        Assert.Equal("pick an item to judge node 'section_notes''s instance if of macro 'tool_note' (item.id == \"hpi\")", waiting.Text);

        page.Find(ItemSelect).Change("plan");
        var applied = Assert.Single(Gates(page));
        Assert.Equal("the read of macro.tool_note.prompt is suppressed for this pick: node 'section_notes''s instance if (item.id == \"hpi\") does not hold", applied.Text);
        Assert.DoesNotContain("Tools used", Rendered(page));

        page.Find(ItemSelect).Change("hpi");
        Assert.Empty(Gates(page));
        Assert.Contains(PF.WorkflowMacroMechanics.Prompt("tool_note", "Note the tools used when a procedure is described."), Rendered(page));
    }

    [Fact]
    public void ASnippetPanesPreview_JudgesTheTextGates_NeverAnInstance()
    {
        var page = RenderEditor();
        Select(page, "snippets/sections/hpi.md");
        page.Find(".pane-editor fluent-text-area").Change("{{ snippet.standards.content }} {{ macro.closing.content }} {{ input.notes }}");

        page.Find(KindSelect).Change("follow_up");

        Assert.Equal(new[] { "closing", "standards" }, Gates(page).Select(g => g.Key));
        Assert.Equal("  ⟨input.notes⟩", Rendered(page));
    }

    [Fact]
    public void At25_ThePreview_HasNoGateNotes()
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V25());
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        var page = Render<Templates>();
        Select(page, "snippets/sections/plan.md");
        page.Find(".pane-editor fluent-text-area").Change("{{ if input.kind == \"new\" }}NEW{{ end }} {{ snippet.standards.content }}");
        page.Find(KindSelect).Change("follow_up");

        Assert.Empty(page.FindAll(".rendered-preview__gate"));
        Assert.Contains("Follow the local standards.", Rendered(page));
    }

    // ---- the node instance's if at 26 ----------------------------------------------------

    [Fact]
    public void TheInstanceRow_ReadsIf_ShowsTheMigratedGate_AndWritesIf()
    {
        CapturePublish();
        var page = RenderEditor();
        Select(page, "node:section_notes");

        var value = page.Find("[aria-label='Condition value for section_notes macro tool_note']");
        Assert.Equal("hpi", value.GetAttribute("current-value"));
        Assert.Equal("if", value.Closest(".condition-field")!.QuerySelector(".condition-field__label")!.TextContent.Trim());

        Select(page, "node:assessment");
        // The closing instance's own if (resting "Always"), in the node pane's Macros section.
        page.FindAll(".node-section").Single(s => s.QuerySelector(".node-section__head")?.TextContent.Trim() == "Macros")
            .QuerySelector(".condition-field__resting")!.Click();
        page.Find("fluent-select[aria-label='Condition operand for assessment macro closing']").Change("kind");
        page.Find("fluent-select[aria-label='Condition value for assessment macro closing']").Change("new");
        Publish(page);

        Assert.NotNull(sent);
        var instance = NodeMacro(sent!, "assessment", 0);
        Assert.Equal("input.kind == \"new\"", instance.GetProperty("if").GetString());
        Assert.False(instance.TryGetProperty("when", out _));
        Assert.False(instance.TryGetProperty("forItem", out _));
        Assert.Empty(Validated().Errors);
    }
}
