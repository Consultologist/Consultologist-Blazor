using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #936: the Documents pane reorganised — each deliverable a card of labeled
/// sub-sections ("Produced when", "Appended content", "Checks"), the condition
/// builders quieted to a resting "Always produced" until opened, each appended
/// macro a link into the Macros pane, and the Macros pane naming where a macro
/// is attached. UI only: the attachment model, the condition grammar and the
/// publish path are untouched, which the surrounding suites already guard.
/// </summary>
public class TemplatesDocumentsPaneReorgTests : ClientRenderTestContext
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

    /// <summary>The V12 macro package with consult_note's `when` stripped — an
    /// always-produced deliverable, to prove the resting state.</summary>
    private static WorkflowPackageContentResponse WithoutDocumentWhen(WorkflowPackageContentResponse package)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(package.Manifest.GetRawText())!.AsObject();
        root["results"]![0]!.AsObject().Remove("when");
        return package with { Manifest = JsonDocument.Parse(root.ToJsonString()).RootElement.Clone() };
    }

    /// <summary>The V12 macro package with the macro unhooked from every result.</summary>
    private static WorkflowPackageContentResponse WithoutMacroAttachment(WorkflowPackageContentResponse package)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(package.Manifest.GetRawText())!.AsObject();
        root["results"]![0]!.AsObject().Remove("macros");
        return package with { Manifest = JsonDocument.Parse(root.ToJsonString()).RootElement.Clone() };
    }

    [Fact]
    public void TheDocumentCard_IsSplitIntoLabeledSubSections()
    {
        var page = RenderEditor(EditorFixtures.V12());
        Navigate(page, "Documents");

        var card = Assert.Single(page.FindAll("li.document-card"));
        var heads = card.QuerySelectorAll(".document-section__head").Select(h => h.TextContent.Trim()).ToList();
        Assert.Equal(new[] { "Produced when", "Appended content", "Checks" }, heads);
    }

    [Fact]
    public void AConditionalDocument_ShowsItsBuilderOpen()
    {
        // consult_note carries `when: node:scope == in_scope`, so the field is not
        // blank and reads directly — no "Always produced" rest to click through.
        var page = RenderEditor(EditorFixtures.V12());
        Navigate(page, "Documents");

        Assert.Empty(page.FindAll("[data-when-for] fluent-button.condition-field__resting"));
        Assert.NotEmpty(page.FindAll("[data-when-for] fluent-select[aria-label^='Condition operand for consult_note']"));
    }

    [Fact]
    public void AnAlwaysProducedDocument_RestsQuiet_UntilOpened()
    {
        var page = RenderEditor(WithoutDocumentWhen(EditorFixtures.V12()));
        Navigate(page, "Documents");

        // At rest: the quiet control, no operand builder.
        var resting = Assert.Single(page.FindAll("[data-when-for] fluent-button.condition-field__resting"));
        Assert.Equal("Always produced", resting.TextContent.Trim());
        Assert.Empty(page.FindAll("[data-when-for] fluent-select[aria-label^='Condition operand for consult_note']"));

        // Opening it reveals the builder.
        resting.Click();
        Assert.NotEmpty(page.FindAll("[data-when-for] fluent-select[aria-label^='Condition operand for consult_note']"));
    }

    [Fact]
    public void AnAppendedMacro_LinksToItsPane_AndGatesQuietly()
    {
        var page = RenderEditor(EditorFixtures.V12());
        Navigate(page, "Documents");

        // The macro id is a button that carries the macro's name.
        var id = Assert.Single(page.FindAll("[data-v11-for] fluent-button.result-macro__id"));
        Assert.Equal("disclaimer", id.TextContent.Trim());

        // Its own "Gated when" rests collapsed, scoped to this attachment.
        Assert.NotEmpty(page.FindAll("fluent-button[aria-label='Add a condition for consult_note macro disclaimer']"));
        Assert.Empty(page.FindAll("fluent-select[aria-label^='Condition operand for consult_note macro disclaimer']"));
    }

    [Fact]
    public void OpeningAMacroGate_RevealsItsOwnBuilder_NotTheDocuments()
    {
        var page = RenderEditor(EditorFixtures.V12());
        Navigate(page, "Documents");

        page.Find("fluent-button[aria-label='Add a condition for consult_note macro disclaimer']").Click();

        Assert.NotEmpty(page.FindAll("fluent-select[aria-label^='Condition operand for consult_note macro disclaimer']"));
        // The document's own condition is independent — it reads its `when`, not
        // dragged open by the macro's gate.
        Assert.NotEmpty(page.FindAll("[data-when-for] fluent-select[aria-label^='Condition operand for consult_note']"));
    }

    [Fact]
    public void ClickingAMacroId_OpensThatMacroInTheMacrosPane()
    {
        var page = RenderEditor(EditorFixtures.V12());
        Navigate(page, "Documents");

        page.Find("[data-v11-for] fluent-button.result-macro__id").Click();

        // The Macros pane for `disclaimer` is now shown — its Attachments section
        // only renders there.
        var attached = Assert.Single(page.FindAll(".macro-attachments"));
        Assert.Contains("consult_note", attached.TextContent);
    }

    [Fact]
    public void TheSignedToggle_LivesUnderChecks_NotTheMacros()
    {
        var page = RenderEditor(EditorFixtures.V12());
        Navigate(page, "Documents");

        var checks = page.FindAll("li.document-card .document-section")
            .Single(section => section.QuerySelector(".document-section__head")!.TextContent.Trim() == "Checks");
        Assert.NotEmpty(checks.QuerySelectorAll(".result-signed"));
    }

    [Fact]
    public void TheMacrosPane_NamesWhereTheMacroIsAttached()
    {
        var page = RenderEditor(EditorFixtures.V12());
        Navigate(page, "disclaimer");

        var attached = Assert.Single(page.FindAll(".macro-attachments"));
        Assert.Contains("Attached to", attached.TextContent);
        Assert.Contains("consult_note", attached.TextContent);
    }

    [Fact]
    public void TheMacrosPane_SaysSo_WhenAMacroIsAttachedToNothing()
    {
        var page = RenderEditor(WithoutMacroAttachment(EditorFixtures.V12()));
        Navigate(page, "disclaimer");

        var attached = Assert.Single(page.FindAll(".macro-attachments"));
        Assert.Contains("Not attached to any document", attached.TextContent);
    }
}
