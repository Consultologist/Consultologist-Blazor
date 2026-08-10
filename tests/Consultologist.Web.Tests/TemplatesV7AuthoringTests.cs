using AngleSharp.Dom;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// The v7 authoring surfaces (#218): declared inputs, the document list, and
/// the repairs that made v7 editable at all.
/// </summary>
public class TemplatesV7AuthoringTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(bool v7 = true)
    {
        WorkflowService.GetCurrentPackageContentAsync()
            .Returns(v7 ? EditorFixtures.V7() : EditorFixtures.V6());

        return Render<Templates>();
    }

    private static void Navigate(IRenderedComponent<Templates> page, string label) =>
        page.FindAll("button.editor-nav__item")
            .First(button => button.TextContent.Replace("\u25CF", string.Empty).Trim() == label)
            .Click();

    private static IReadOnlyList<IElement> Rows(IRenderedComponent<Templates> page) =>
        page.FindAll("li.declared-row");

    [Fact]
    public void LegacyPackage_HasNoDeclaredSections()
    {
        var page = RenderEditor(v7: false);

        var navLabels = page.FindAll("button.editor-nav__item").Select(b => b.TextContent.Trim()).ToList();
        Assert.DoesNotContain("Inputs", navLabels);
        Assert.DoesNotContain("Documents", navLabels);
    }

    [Fact]
    public void InputsPane_RendersOneRowPerDeclaredSlot()
    {
        var page = RenderEditor();
        Navigate(page, "Inputs");

        var rows = Rows(page);
        Assert.Equal(2, rows.Count);
        Assert.Equal("consult_draft", rows[0].QuerySelector("input.declared-row__id")!.GetAttribute("value"));
        Assert.Equal("prior_notes", rows[1].QuerySelector("input.declared-row__id")!.GetAttribute("value"));
        // The optional slot's checkbox is unchecked.
        Assert.False(rows[1].QuerySelector("input[type=checkbox]")!.HasAttribute("checked"));
    }

    [Fact]
    public void RenamingAnInput_CascadesIntoBindingsThatUsedIt()
    {
        var page = RenderEditor();
        Navigate(page, "Inputs");

        Rows(page)[0].QuerySelector("input.declared-row__id")!.Change("referral");

        // The fan node bound input:consult_draft; after the rename its binding
        // must point at the new id, or publishing would fail on an undeclared
        // input the author never touched.
        Navigate(page, "Graph");
        var sources = page.FindAll(".binding-row__select")
            .Select(select => select.GetAttribute("value"))
            .ToList();

        Assert.Contains("input:referral", sources);
        Assert.DoesNotContain("input:consult_draft", sources);
    }

    [Fact]
    public void DuplicateInputId_IsRefusedAtTheDesk()
    {
        var page = RenderEditor();
        Navigate(page, "Inputs");

        page.Find(".add-variable__form input.node-field__input").Change("consult_draft");
        page.Find(".add-variable__form button").Click();

        Assert.Contains("Duplicate input id 'consult_draft'", page.Markup);
        Assert.Equal(2, Rows(page).Count);
    }

    [Fact]
    public void MalformedInputId_IsRefusedWithTheServersWording()
    {
        var page = RenderEditor();
        Navigate(page, "Inputs");

        page.Find(".add-variable__form input.node-field__input").Change("Prior-Notes");
        page.Find(".add-variable__form button").Click();

        Assert.Contains("must be snake_case", page.Markup);
        Assert.Equal(2, Rows(page).Count);
    }

    [Fact]
    public void DocumentsPane_RendersTheDeclaredResultsWithAggregatorCandidates()
    {
        var page = RenderEditor();
        Navigate(page, "Documents");

        var row = Assert.Single(Rows(page));
        Assert.Equal("consult_note", row.QuerySelector("input.declared-row__id")!.GetAttribute("value"));

        // Candidates are aggregators only — a forEach node is not a valid v7
        // deliverable, which is the bug this pane replaced.
        var options = row.QuerySelectorAll("option").Select(o => o.GetAttribute("value")).ToArray();
        Assert.Equal(new[] { "node:assemble-note" }, options);
    }

    [Fact]
    public void AddingASecondDocument_NeedsAFreeAggregator()
    {
        var page = RenderEditor();
        Navigate(page, "Documents");

        page.Find(".add-variable__form input.node-field__input").Change("patient_letter");
        page.Find(".add-variable__form button").Click();

        // The fixture has one aggregator and it is already spoken for.
        Assert.Contains("already owns a document", page.Markup);
        Assert.Single(Rows(page));
    }

    [Fact]
    public void DeclaringDocuments_MarksThePackageDirty()
    {
        var page = RenderEditor();
        Navigate(page, "Documents");

        Rows(page)[0].QuerySelector("input.node-field__input:not(.declared-row__id)")!.Change("Consult letter");

        Assert.Contains("documents", page.Markup);
        Assert.False(
            page.FindAll("fluent-button").First(b => b.TextContent.Contains("Publish")).HasAttribute("disabled"),
            "a pending document edit should enable Publish");
    }
}

/// <summary>
/// The v8 authoring surfaces (#316): a type on an input, values on an enum, and
/// a condition on a document. The literal control follows the chosen input's
/// type, which is what makes #314's closed grammar authorable rather than a
/// string to get wrong.
/// </summary>
public class TemplatesV8AuthoringTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor()
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V7());
        return Render<Templates>();
    }

    private static void Navigate(IRenderedComponent<Templates> page, string label) =>
        page.FindAll("button.editor-nav__item")
            .First(button => button.TextContent.Replace("\u25CF", string.Empty).Trim() == label)
            .Click();

    /// <summary>Types consult_draft as an enum with two values.</summary>
    private static void DeclareEnum(IRenderedComponent<Templates> page, string id = "prior_notes")
    {
        Navigate(page, "Inputs");
        var row = page.FindAll("select.declared-row__type")[1];
        row.Change(WorkflowInputTypes.Enum);
        page.Find("li.declared-row__values input").Change("new_patient");
        page.Find("li.declared-row__values input").Change("follow_up");
    }

    [Fact]
    public void AnInputCanBeTyped_AndTextIsWrittenAsAbsence()
    {
        var page = RenderEditor();
        Navigate(page, "Inputs");

        Assert.Equal(2, page.FindAll("select.declared-row__type").Count);
        // Every v7 input opens as text, which is the default the format states.
        Assert.All(page.FindAll("select.declared-row__type"),
            select => Assert.Equal(WorkflowInputTypes.Text, select.GetAttribute("value")));
    }

    [Fact]
    public void AnEnumAuthorsItsValues()
    {
        var page = RenderEditor();
        DeclareEnum(page);

        var chips = page.FindAll("[data-enum-value]").Select(c => c.GetAttribute("data-enum-value")).ToList();
        Assert.Equal(new[] { "new_patient", "follow_up" }, chips);
    }

    [Fact]
    public void AValueBreakingTheIdRule_IsRefusedInline()
    {
        // The validator is the authority; the editor is the early warning.
        var page = RenderEditor();
        Navigate(page, "Inputs");
        page.FindAll("select.declared-row__type")[1].Change(WorkflowInputTypes.Enum);
        page.Find("li.declared-row__values input").Change("Follow Up");

        Assert.Contains("must be snake_case", page.Markup);
    }

    [Fact]
    public void OnlyEnumAndBooleanInputsAreOfferedAsConditions()
    {
        // #314's narrowing, surfaced: a date or text input cannot be tested, so
        // it never appears in the picker.
        var page = RenderEditor();
        DeclareEnum(page);
        Navigate(page, "Documents");

        var picker = page.Find("li.declared-row__when select");
        var options = picker.QuerySelectorAll("option").Select(o => o.GetAttribute("value")).ToList();

        Assert.Equal(new[] { "", "prior_notes" }, options);
    }

    [Fact]
    public void WithNoTestableInput_TheRowSaysSoRatherThanOfferingNothing()
    {
        var page = RenderEditor();
        Navigate(page, "Documents");

        Assert.Contains("Declare an enum or boolean input", page.Markup);
        Assert.Empty(page.FindAll("li.declared-row__when select"));
    }

    [Fact]
    public void ChoosingAnInput_ComposesAConditionWithATypedLiteral()
    {
        var page = RenderEditor();
        DeclareEnum(page);
        Navigate(page, "Documents");

        page.Find("li.declared-row__when select").Change("prior_notes");

        // Three controls now: input, operator, literal — and the literal offers
        // exactly the enum's declared values.
        var selects = page.FindAll("li.declared-row__when select");
        Assert.Equal(3, selects.Count);

        var literals = selects[2].QuerySelectorAll("option").Select(o => o.GetAttribute("value")).ToList();
        Assert.Equal(new[] { "new_patient", "follow_up" }, literals);
    }

    [Fact]
    public void AnInputAConditionReads_CannotBeRetypedOrRemoved()
    {
        // Mirrors #321's delete guard: the alternative is composing a package
        // the validator rejects and finding out at publish.
        var page = RenderEditor();
        DeclareEnum(page);
        Navigate(page, "Documents");
        page.Find("li.declared-row__when select").Change("prior_notes");

        Navigate(page, "Inputs");
        page.FindAll("select.declared-row__type")[1].Change(WorkflowInputTypes.Date);

        Assert.Contains("is tested by", page.Markup);
        // Still an enum: the retype was refused, not applied and warned about.
        Assert.Equal(WorkflowInputTypes.Enum, page.FindAll("select.declared-row__type")[1].GetAttribute("value"));
    }
}
