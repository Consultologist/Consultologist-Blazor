using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// v26 (#1206, design § 2.2, § 2.5, § 11): three desk notes the validator never refuses — "never
/// fires" on an instance row whose if and the macro's own if cannot both hold (judged over one enum
/// input), "unread" on a placed-only macro's pane, "empty prompt" on a macro read inline whose
/// instruction renders to nothing. Rendered through the #1209 seam; the 25 sentences are pinned.
/// </summary>
public class TemplatesV26DeskNotesTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse? fixture = null)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture ?? EditorFixtures.V26Contradiction());
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        return Render<Templates>(parameters => parameters.Add(p => p.AuthorsAboveCeiling, true));
    }

    private void Select(IRenderedComponent<Templates> page, string key)
    {
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = key;
        page.Render();
    }

    private static IReadOnlyList<string> Notes(IRenderedComponent<Templates> page) =>
        page.FindAll("[data-instance-note='never-fires']").Select(p => p.TextContent.Trim()).ToList();

    private static string? MacroNote(IRenderedComponent<Templates> page, string kind) =>
        page.FindAll($"[data-macro-note='{kind}']").SingleOrDefault()?.TextContent.Trim();

    private const string ClosingNeverFiresOnConsult = "never fires: the macro's if (input.kind == \"new\") and this instance's if (input.kind == \"follow_up\") cannot both hold";
    private const string ClosingNeverFiresOnAssessment = "never fires: the macro's if (input.kind == \"new\") and this instance's if (input.kind != \"new\") cannot both hold";

    // ---- never fires ----------------------------------------------------------------

    [Fact]
    public void TheDeliverablesCard_AndTheMacroPane_SayNeverFires_OnTheContradictingInstances()
    {
        var page = RenderEditor();
        Select(page, "results:consult");
        Assert.Equal(new[] { ClosingNeverFiresOnConsult }, Notes(page));

        Select(page, "macros/closing.md");
        var onPane = Notes(page);
        Assert.Contains(ClosingNeverFiresOnConsult, onPane);
        Assert.Contains(ClosingNeverFiresOnAssessment, onPane);
        Assert.Equal(2, onPane.Count);
    }

    [Fact]
    public void TheNodePane_SaysNeverFires_OnItsInstance_AndNotOnAnItemGate()
    {
        var page = RenderEditor();
        Select(page, "node:assessment");
        Assert.Equal(new[] { ClosingNeverFiresOnAssessment }, Notes(page));

        // tool_note's instance gate reads item.id — not one enum input; nothing is said.
        Select(page, "node:section_notes");
        Assert.Empty(Notes(page));
    }

    [Fact]
    public void AnInstanceWhoseIfCanHoldWithTheMacros_HasNoNote()
    {
        // V26Gated: the consult instance's if equals the header's (new / new); assessment's is ungated.
        var page = RenderEditor(EditorFixtures.V26Gated());
        Select(page, "macros/closing.md");
        Assert.Empty(Notes(page));

        // An or shape sharing a value with the header's if: a value satisfies both.
        Select(page, "results:consult");
        page.Find("fluent-select[aria-label='Condition value for consult macro closing']").Change("follow_up");
        Assert.Equal(new[] { ClosingNeverFiresOnConsult }, Notes(page));
        page.Find("fluent-button[aria-label='Add condition clause to consult macro closing']").Click();
        // The second clause's controls carry a position suffix.
        page.FindAll("fluent-select[aria-label^='Condition operand for consult macro closing']").Last().Change("kind");
        page.FindAll("fluent-select[aria-label^='Condition value for consult macro closing']").Last().Change("new");
        page.Find("fluent-select[aria-label='Condition join for consult macro closing']").Change("or");
        Assert.Empty(Notes(page));
    }

    [Fact]
    public void At25_NoInstanceNoteExists()
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V25());
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        var page = Render<Templates>();
        Select(page, "results:consult");
        Assert.Empty(page.FindAll(".instance-note"));
        Select(page, "macros/closing.md");
        Assert.Empty(page.FindAll(".instance-note"));
    }

    // ---- unread -----------------------------------------------------------------------

    [Fact]
    public void APlacedOnlyMacroWithAnInstruction_SaysUnread_AndAnInlineOneDoesNot()
    {
        var page = RenderEditor(EditorFixtures.V26Gated());
        Select(page, "macros/closing.md");
        Assert.Equal("unread: no prompt reads macro.closing.prompt; the instruction is kept, not shown.", MacroNote(page, "unread"));
        Assert.Null(MacroNote(page, "empty-prompt"));

        Select(page, "macros/tool_note.md");
        Assert.Null(MacroNote(page, "unread"));
        Assert.Empty(page.FindAll(".macro-prompt-preview__note"));
    }

    [Fact]
    public void APlacedOnlyMacroWithABlankInstruction_KeepsThe25Sentence()
    {
        var page = RenderEditor(EditorFixtures.V26Gated());
        Select(page, "macros/closing.md");
        page.Find("fluent-text-area[aria-label='Instruction for macro closing']").Change("");

        Assert.Null(MacroNote(page, "unread"));
        Assert.Contains("Read inline by nothing yet — placed instances do not use the instruction.", page.Find(".macro-prompt-preview__note").TextContent);
        Assert.Equal("No instruction yet.", page.Find(".macro-prompt-preview__text").TextContent.Trim());
    }

    [Fact]
    public void At25_APlacedOnlyMacro_KeepsThe25Sentence()
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V25());
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        var page = Render<Templates>();
        Select(page, "macros/closing.md");
        Assert.Null(MacroNote(page, "unread"));
        Assert.Contains("Read inline by nothing yet", page.Find(".macro-prompt-preview__note").TextContent);
    }

    // ---- empty prompt -----------------------------------------------------------------

    [Fact]
    public void AnInlineMacroWhoseInstructionRendersToNothing_SaysEmptyPrompt_NamingItsReaders()
    {
        var page = RenderEditor(EditorFixtures.V26Gated());
        Select(page, "macros/tool_note.md");
        page.Find("fluent-text-area[aria-label='Instruction for macro tool_note']").Change("{{ \"\" }}");

        Assert.Equal("empty prompt: macro.tool_note.prompt renders to nothing — the model is told nothing where it is read (Node 'assessment' prompt, Node 'section_notes' prompt).", MacroNote(page, "empty-prompt"));

        // Renders empty over the tokens but reads an input: a run may render it — nothing is said.
        page.Find("fluent-text-area[aria-label='Instruction for macro tool_note']").Change("{{ if input.kind == \"new\" }}Note the tools.{{ end }}");
        Assert.Null(MacroNote(page, "empty-prompt"));

        // Blank: the sentence beside "No instruction yet." (the desk refuses it at publish too).
        page.Find("fluent-text-area[aria-label='Instruction for macro tool_note']").Change("");
        Assert.Equal("No instruction yet.", page.Find(".macro-prompt-preview__text").TextContent.Trim());
        Assert.StartsWith("empty prompt: macro.tool_note.prompt renders to nothing", MacroNote(page, "empty-prompt"));
    }
}
