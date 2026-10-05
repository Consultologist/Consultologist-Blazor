using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.FluentUI.AspNetCore.Components;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #940: a macro's attachment — placement, gated-when, attach/detach — is edited
/// from the macro's own pane as well as from the Documents pane, a transposed
/// per-macro view of the same relationship. Per-document ordering (↑↓) stays in
/// the Documents pane only. UI only: the same attachment methods are driven, so
/// the manifest round-trips identically to editing in Documents.
/// </summary>
public class TemplatesMacroAttachmentTests : ClientRenderTestContext
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

    private WorkflowPackagePublishRequest? sent;

    private void CapturePublish() =>
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.08.2", "acct-1234567890ab@v2026.08.2"),
                Array.Empty<string>()));

    private static JsonElement Result(WorkflowPackagePublishRequest request) =>
        JsonDocument.Parse(request.Manifest.GetRawText()).RootElement.GetProperty("results")[0];

    private static IReadOnlyList<string?> MacroIds(JsonElement result) =>
        result.TryGetProperty("macros", out var macros)
            ? macros.EnumerateArray()
                .Select(m => m.ValueKind == JsonValueKind.String ? m.GetString() : m.GetProperty("id").GetString())
                .ToList()
            : Array.Empty<string?>();

    private static WorkflowPackageContentResponse WithoutMacroAttachment(WorkflowPackageContentResponse package)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(package.Manifest.GetRawText())!.AsObject();
        root["results"]![0]!.AsObject().Remove("macros");
        return package with { Manifest = JsonDocument.Parse(root.ToJsonString()).RootElement.Clone() };
    }

    [Fact]
    public void TheMacroPane_ShowsAnAttachmentRow_PerAttachedDocument()
    {
        var page = RenderEditor(EditorFixtures.V12());
        Navigate(page, "disclaimer");

        var section = Assert.Single(page.FindAll(".macro-attachments"));
        var row = Assert.Single(section.QuerySelectorAll(".result-macro"));
        Assert.Contains("consult_note", row.TextContent);
        Assert.NotEmpty(row.QuerySelectorAll(".result-macro-placement"));
        Assert.NotEmpty(row.QuerySelectorAll(".result-macro-detach"));
    }

    [Fact]
    public void TheMacroPane_HasNoReorderControls()
    {
        // Ordering is a document concern — it needs the sibling macros, which the
        // macro pane does not show. It stays in the Documents pane only.
        var page = RenderEditor(EditorFixtures.V12());
        Navigate(page, "disclaimer");

        Assert.Empty(page.FindAll(".result-macro-up"));
        Assert.Empty(page.FindAll(".result-macro-down"));
    }

    [Fact]
    public void ChangingPlacementFromTheMacroPane_RoundTrips()
    {
        var page = RenderEditor(EditorFixtures.V12());
        CapturePublish();
        Navigate(page, "disclaimer");

        // Same method as the Documents pane — the entry gains its anchor.
        page.Find(".result-macro-placement").Change("after|node:draft-section");
        Publish(page);

        var placed = Result(sent!).GetProperty("macros").EnumerateArray().Single();
        Assert.Equal(JsonValueKind.Object, placed.ValueKind);
        Assert.Equal("node:draft-section", placed.GetProperty("after").GetString());
    }

    [Fact]
    public void EditingTheGatedWhenFromTheMacroPane_RoundTrips()
    {
        var page = RenderEditor(EditorFixtures.V12());
        CapturePublish();
        Navigate(page, "disclaimer");

        // The same ConditionField, addressed by the same target subject as the
        // Documents pane — it rests collapsed until opened.
        page.Find("fluent-button[aria-label='Add a condition for consult_note macro disclaimer']").Click();
        page.Find("fluent-select[aria-label^='Condition operand for consult_note macro disclaimer']").Change("node:scope");
        page.Find("fluent-select[aria-label^='Condition value for consult_note macro disclaimer']").Change("in_scope");
        Publish(page);

        var entry = Result(sent!).GetProperty("macros").EnumerateArray().Single();
        Assert.Equal("node:scope == in_scope", entry.GetProperty("when").GetString());
    }

    [Fact]
    public void AttachingToADocumentFromTheMacroPane_AddsTheMacro()
    {
        var page = RenderEditor(WithoutMacroAttachment(EditorFixtures.V12()));
        CapturePublish();
        Navigate(page, "disclaimer");

        // Not attached anywhere → the attach picker offers the document.
        page.Find(".result-macro-attach-doc").Change("consult_note");
        Publish(page);

        Assert.Contains("disclaimer", MacroIds(Result(sent!)));
    }

    [Fact]
    public void DetachingFromTheMacroPane_RemovesTheAttachment()
    {
        // Detaching the only reference leaves the macro declared but orphaned —
        // which the validator refuses (#571), so assert the detach itself on the
        // UI rather than through a (correctly) refused publish.
        var page = RenderEditor(EditorFixtures.V12());
        Navigate(page, "disclaimer");
        Assert.Single(page.FindAll(".macro-attachments .result-macro"));

        page.Find(".result-macro-detach").Click();

        Assert.Empty(page.FindAll(".macro-attachments .result-macro"));
        Assert.Contains("Not attached to any document", page.Find(".macro-attachments").TextContent);
    }

    // ----- #941: the slot-instruction snippet generator -----

    private static WorkflowPackageContentResponse V20Slot(string? when = "node:scope == in_scope")
    {
        var package = EditorFixtures.V11Macro();
        var root = System.Text.Json.Nodes.JsonNode.Parse(package.Manifest.GetRawText())!.AsObject();
        root["specVersion"] = 20;
        var macro = new System.Text.Json.Nodes.JsonObject { ["id"] = "disclaimer", ["slot"] = true };
        if (when != null)
        {
            macro["when"] = when;
        }

        root["results"]!.AsArray()[0]!.AsObject()["macros"] = new System.Text.Json.Nodes.JsonArray(macro);
        return package with { SpecVersion = 20, Manifest = JsonDocument.Parse(root.ToJsonString()).RootElement.Clone() };
    }

    [Fact]
    public void SlotInstructionButton_Shows_ForASlotAttachment()
    {
        var page = RenderEditor(V20Slot());
        Navigate(page, "disclaimer");

        Assert.Contains(page.FindAll("fluent-button"), b => b.TextContent.Contains("Slot instruction"));
    }

    [Fact]
    public void SlotInstructionButton_IsAbsent_ForANonSlotAttachment()
    {
        var page = RenderEditor(EditorFixtures.V12());
        Navigate(page, "disclaimer");

        Assert.DoesNotContain(page.FindAll("fluent-button"), b => b.TextContent.Contains("Slot instruction"));
    }

    [Fact]
    public void SlotInstructionSnippet_CarriesTheMarker_TheConditionProse_AndTheGuard()
    {
        var page = RenderEditor(V20Slot());
        Navigate(page, "disclaimer");

        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Slot instruction")).Click();

        var snippet = page.FindComponent<FluentTextArea>().Instance.Value ?? string.Empty;
        Assert.Contains("[[slot:disclaimer]]", snippet);
        Assert.Contains("scope is in_scope", snippet);
        Assert.Contains("verbatim", snippet);
    }

    [Fact]
    public void SlotInstructionSnippet_UsesTheUngatedVariant_WhenNoGate()
    {
        var page = RenderEditor(V20Slot(when: null));
        Navigate(page, "disclaimer");

        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Slot instruction")).Click();

        var snippet = page.FindComponent<FluentTextArea>().Instance.Value ?? string.Empty;
        Assert.Contains("[[slot:disclaimer]]", snippet);
        Assert.Contains("Where appropriate", snippet);
        Assert.DoesNotContain("If ", snippet);
    }

    [Fact]
    public void CopySlotInstruction_WritesTheEditedText_ToTheClipboard()
    {
        JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true).SetVoidResult();
        var page = RenderEditor(V20Slot());
        Navigate(page, "disclaimer");

        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Slot instruction")).Click();

        var textArea = page.FindComponent<FluentTextArea>();
        textArea.InvokeAsync(() => textArea.Instance.ValueChanged.InvokeAsync("Edited slot instruction.")).GetAwaiter().GetResult();
        page.Find(".result-slot-copy").Click();

        var invocation = JSInterop.Invocations["navigator.clipboard.writeText"].Last();
        Assert.Equal("Edited slot instruction.", invocation.Arguments[0]);
    }
}
