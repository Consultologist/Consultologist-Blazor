using System.Text.Json;
using System.Text.Json.Nodes;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// #1266 (§ 2.6): a loop instance gated on items only is edited as the case it is — item chips with ✕ and an
/// "add an item" pick writing the bare item.id or-chain; the last ✕ and every item chosen both fall back to
/// Always with a note; "edit as a condition" opens the builder and "gate by item" comes back; an ungated
/// instance offers "only for an item…"; any other gate is the builder's alone.
/// </summary>
public class TemplatesV26InstanceCaseTests : ClientRenderTestContext
{
    private const string Macro = "macros/tool_note.md";
    private const string Node = "node:section_notes";

    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse fixture)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture);
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        return Render<Templates>();
    }

    private void Select(IRenderedComponent<Templates> page, string key)
    {
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = key;
        page.Render();
    }

    private WorkflowPackagePublishRequest? sent;

    private void CapturePublish() =>
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.10.2", "acct-1234567890ab@v2026.10.2"),
                Array.Empty<string>()));

    private static void Publish(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

    /// <summary>The published section_notes instance's if, or null when it carries none.</summary>
    private string? PublishedIf()
    {
        var root = JsonNode.Parse(sent!.Manifest.GetRawText())!;
        var node = root["nodes"]!.AsArray().First(n => n!["id"]!.GetValue<string>() == "section_notes")!;
        return node["macros"]![0]!["if"]?.GetValue<string>();
    }

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

    private static IReadOnlyList<string> Chips(IRenderedComponent<Templates> page) =>
        page.FindAll(".instance-case__chip").Select(c => c.TextContent.Replace("✕", "").Trim()).ToList();

    private static IReadOnlyList<string> AddChoices(IRenderedComponent<Templates> page, string selector) =>
        page.Find(selector).QuerySelectorAll("fluent-option").Select(o => o.TextContent.Trim()).Skip(1).ToList();

    private static void Drop(IRenderedComponent<Templates> page, string label) =>
        page.Find($"fluent-button.instance-case__chip-remove[aria-label^='Drop {label} from']").Click();

    /// <summary>V26Gated with the tool_note instance's if set (null removes it) and, optionally, a third item Exam.</summary>
    private static WorkflowPackageContentResponse Gated(string? toolNoteIf, bool threeItems = false)
    {
        var gated = EditorFixtures.V26Gated();
        var root = JsonNode.Parse(gated.Manifest.GetRawText())!.AsObject();
        var instance = root["nodes"]!.AsArray().First(n => n!["id"]!.GetValue<string>() == "section_notes")!["macros"]![0]!.AsObject();
        if (toolNoteIf is null)
        {
            instance.Remove("if");
        }
        else
        {
            instance["if"] = toolNoteIf;
        }

        var files = new Dictionary<string, string>(gated.Files, StringComparer.Ordinal);
        if (threeItems)
        {
            // At 26 a folder index is a bare list and an item's label sits in its file's header.
            var index = JsonNode.Parse(files["snippets/sections/index.json"])!.AsArray();
            index.Add(JsonNode.Parse("""{ "id": "exam", "file": "exam.md" }"""));
            files["snippets/sections/index.json"] = index.ToJsonString();
            files["snippets/sections/exam.md"] = "{\n  \"label\": \"Exam\"\n}\nExam.";
        }

        return new(gated.Name, gated.Version, 26, JsonDocument.Parse(root.ToJsonString()).RootElement.Clone(), files);
    }

    // ---- the chips ----------------------------------------------------------------------

    [Fact]
    public void TheMigratedOneItemGate_IsOneChip_WithNoBuilder_AndOffersTheOtherItems()
    {
        var page = RenderEditor(EditorFixtures.V26Gated());
        Select(page, Macro);

        Assert.Equal(new[] { "HPI" }, Chips(page));
        var row = page.Find(".macro-instance[data-instance-node='section_notes']");
        Assert.Empty(row.QuerySelectorAll(".condition-group"));
        Assert.Empty(row.QuerySelectorAll(".condition-field__resting"));
        Assert.Equal(new[] { "Plan" }, AddChoices(page, "fluent-select.instance-case__add"));
        Assert.Contains("else skipped", page.Find("[data-instance-case]").TextContent);
    }

    [Fact]
    public void AddingAnItem_WritesTheBareOrChain_InItemOrder_AndValidates()
    {
        CapturePublish();
        var page = RenderEditor(Gated("item.id == \"plan\"", threeItems: true));
        Select(page, Macro);

        Assert.Equal(new[] { "HPI", "Exam" }, AddChoices(page, "fluent-select.instance-case__add"));
        page.Find("fluent-select.instance-case__add").Change("hpi");

        Assert.Equal(new[] { "HPI", "Plan" }, Chips(page));
        // The pick recreated the select: it rests, offering what is left.
        Assert.Equal(new[] { "Exam" }, AddChoices(page, "fluent-select.instance-case__add"));

        Publish(page);
        Assert.Equal("item.id == \"hpi\" || item.id == \"plan\"", PublishedIf());
        Assert.Empty(Validated().Errors);
    }

    [Fact]
    public void DroppingAChip_RewritesTheGate()
    {
        CapturePublish();
        var page = RenderEditor(Gated("item.id == \"hpi\" || item.id == \"plan\"", threeItems: true));
        Select(page, Macro);

        Drop(page, "HPI");
        Assert.Equal(new[] { "Plan" }, Chips(page));

        Publish(page);
        Assert.Equal("item.id == \"plan\"", PublishedIf());
    }

    // ---- the edges ----------------------------------------------------------------------

    [Fact]
    public void TheLastChipsCross_FallsBackToAlways_WithANote()
    {
        CapturePublish();
        var page = RenderEditor(EditorFixtures.V26Gated());
        Select(page, Macro);

        Drop(page, "HPI");

        Assert.Empty(page.FindAll("[data-instance-case]"));
        Assert.Single(page.FindAll(".macro-instance .condition-field__resting"));
        Assert.Equal("No item left — the macro runs for every item (Always).", page.Find("[data-instance-case-note]").TextContent.Trim());

        Publish(page);
        Assert.Null(PublishedIf());
    }

    [Fact]
    public void ChoosingEveryItem_FallsBackToAlways_WithANote()
    {
        CapturePublish();
        var page = RenderEditor(EditorFixtures.V26Gated());
        Select(page, Macro);

        page.Find("fluent-select.instance-case__add").Change("plan");

        Assert.Empty(page.FindAll("[data-instance-case]"));
        Assert.Equal("Every item chosen — the macro runs for every item (Always).", page.Find("[data-instance-case-note]").TextContent.Trim());

        Publish(page);
        Assert.Null(PublishedIf());
    }

    // ---- getting in ---------------------------------------------------------------------

    [Fact]
    public void AnUngatedLoopInstance_OffersOnlyForAnItem_AndAPickStartsTheChips()
    {
        CapturePublish();
        var page = RenderEditor(Gated(null));
        Select(page, Macro);

        Assert.Equal(new[] { "HPI", "Plan" }, AddChoices(page, "fluent-select.instance-case__start"));
        page.Find("fluent-select.instance-case__start").Change("plan");

        Assert.Equal(new[] { "Plan" }, Chips(page));
        Assert.Empty(page.FindAll("fluent-select.instance-case__start"));

        Publish(page);
        Assert.Equal("item.id == \"plan\"", PublishedIf());
    }

    [Fact]
    public void AnInstanceInNoLoop_OffersNoItemPick()
    {
        var page = RenderEditor(EditorFixtures.V26Gated());
        Select(page, "node:assessment");

        Assert.Empty(page.FindAll("fluent-select.instance-case__start"));
        Assert.Empty(page.FindAll("[data-instance-case]"));
    }

    // ---- the switch ---------------------------------------------------------------------

    [Fact]
    public void EditAsACondition_ShowsTheBuilder_AndGateByItemComesBack()
    {
        var page = RenderEditor(EditorFixtures.V26Gated());
        Select(page, Macro);

        page.Find("fluent-button.instance-case__as-condition").Click();
        Assert.Empty(page.FindAll("[data-instance-case]"));
        var row = page.Find(".macro-instance[data-instance-node='section_notes']");
        Assert.NotEmpty(row.QuerySelectorAll(".condition-group"));

        page.Find("fluent-button.instance-case__as-chips").Click();
        Assert.Equal(new[] { "HPI" }, Chips(page));
        Assert.Empty(page.FindAll("fluent-button.instance-case__as-chips"));
    }

    [Theory]
    [InlineData("item.id == \"hpi\" && input.kind == \"new\"")]
    [InlineData("item.id == \"hpi\" || input.kind == \"new\"")]
    [InlineData("item.id == \"unknown\"")]
    public void AGateThatIsNotItemsOnly_IsTheBuildersAlone(string gate)
    {
        var page = RenderEditor(Gated(gate));
        Select(page, Macro);

        Assert.Empty(page.FindAll("[data-instance-case]"));
        Assert.Empty(page.FindAll("fluent-button.instance-case__as-chips"));
        Assert.Empty(page.FindAll("fluent-select.instance-case__start"));
        Assert.NotEmpty(page.FindAll(".macro-instance .condition-field"));
    }

    // ---- both panes ---------------------------------------------------------------------

    [Fact]
    public void TheNodePane_ShowsTheSameChips_AndAnEditOnOneShowsOnTheOther()
    {
        var page = RenderEditor(Gated("item.id == \"hpi\"", threeItems: true));

        Select(page, Node);
        Assert.Equal(new[] { "HPI" }, Chips(page));
        page.Find("fluent-select.instance-case__add").Change("exam");
        Assert.Equal(new[] { "HPI", "Exam" }, Chips(page));

        Select(page, Macro);
        Assert.Equal(new[] { "HPI", "Exam" }, Chips(page));
    }

    [Fact]
    public void Undo_BringsTheChipBack()
    {
        var page = RenderEditor(Gated("item.id == \"hpi\" || item.id == \"plan\"", threeItems: true));
        Select(page, Macro);

        Drop(page, "Plan");
        Assert.Equal(new[] { "HPI" }, Chips(page));

        page.Find(".undo-button").Click();
        Select(page, Macro);
        Assert.Equal(new[] { "HPI", "Plan" }, Chips(page));
    }

    [Fact]
    public void TheFallbackNote_IsGone_OnceUndoBringsTheChipBack_OnBothPanes()
    {
        var page = RenderEditor(EditorFixtures.V26Gated());
        Select(page, Macro);
        Drop(page, "HPI");
        Assert.Single(page.FindAll("[data-instance-case-note]"));

        page.Find(".undo-button").Click();
        Select(page, Macro);
        Assert.Equal(new[] { "HPI" }, Chips(page));
        Assert.Empty(page.FindAll("[data-instance-case-note]"));

        Select(page, Node);
        Assert.Equal(new[] { "HPI" }, Chips(page));
        Assert.Empty(page.FindAll("[data-instance-case-note]"));
    }

    [Fact]
    public void TheItemPick_SitsOnAlwayssLine()
    {
        var page = RenderEditor(Gated(null));
        Select(page, Macro);

        var field = page.Find("fluent-select.instance-case__start").ParentElement!;
        Assert.Contains("condition-field", field.ClassList);
        Assert.NotNull(field.QuerySelector(".condition-field__resting"));

        // "Always" still opens the builder, and the pick steps aside.
        page.Find(".macro-instance .condition-field__resting").Click();
        Assert.NotEmpty(page.FindAll(".macro-instance .condition-group"));
        Assert.Empty(page.FindAll("fluent-select.instance-case__start"));
    }

    [Fact]
    public void At25_NoChips()
    {
        var page = RenderEditor(EditorFixtures.V25());
        Select(page, Macro);

        Assert.Empty(page.FindAll("[data-instance-case]"));
        Assert.Empty(page.FindAll("fluent-select.instance-case__start"));
    }
}
