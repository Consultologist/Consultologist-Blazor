using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// #1091 (Milestone 31): a case/when read row over an enum — the table on the case
/// subject's own read row (one line per declared value with a closed macro dropdown,
/// "otherwise", the canonical one-liner written), loaded from a hand-written case,
/// rewriting the owning text in place; the macro pane's "Read by" row shows the whole
/// table; the node pane a sentence; a non-canonical case is read-only.
/// </summary>
public class TemplatesV25BranchTableTests : ClientRenderTestContext
{
    private const string File = "prompts/assessment.md";
    private const string ToolNote = "{{ macro.tool_note.prompt }}";
    private const string Closing = "{{ macro.closing.prompt }}";
    private const string Case = "{{~ case input.kind ~}}{{~ when \"new\" ~}}" + ToolNote + "{{~ when \"follow_up\" ~}}" + Closing + "{{~ else ~}}{{~ end ~}}";
    private const string Written = "{{~ case input.kind ~}}{{~ when \"new\" ~}}" + ToolNote + "{{~ when \"follow_up\" ~}}" + Closing + "{{~ end ~}}";

    // The fixture's assessment prompt with its bare inline read replaced by the case — the graph (meds, extract) stays fed.
    private static string Original => EditorFixtures.V25().Files[File];
    private static string Prompt => Original.Replace(ToolNote, Case);

    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse? fixture = null)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture ?? EditorFixtures.V25());
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        return Render<Templates>();
    }

    private static WorkflowPackageContentResponse WithFile(string path, string text)
    {
        var package = EditorFixtures.V25();
        var files = new Dictionary<string, string>(package.Files, StringComparer.Ordinal) { [path] = text };
        return package with { Files = files };
    }

    private static void Navigate(IRenderedComponent<Templates> page, string label) =>
        page.FindAll("button.editor-nav__item")
            .First(button => button.TextContent.Replace("●", string.Empty).Trim() == label)
            .Click();

    private void ShowNode(IRenderedComponent<Templates> page, string nodeId)
    {
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = $"node:{nodeId}";
        page.Render();
    }

    private static string EditorText(IRenderedComponent<Templates> page) =>
        page.Find(".editor-pane fluent-text-area:not(.macro-instruction)").GetAttribute("current-value") ?? string.Empty;

    private static string Row(string list, string member) => $".template-read-rows[aria-label=\"{list}\"] .template-read-row[data-read-member='{member}']";
    private static string Pick(string value, string subject = "input.kind", string where = File) => $"fluent-select[aria-label=\"Branch for {value} of {subject} in {where}\"]";
    private static string Otherwise(string where = File) => $"fluent-select[aria-label=\"Otherwise of input.kind in {where}\"]";
    private static string Line(string where = File) => $"code[aria-label=\"Line written for input.kind in {where}\"]";

    private WorkflowPackagePublishRequest? sent;

    private void CapturePublish() =>
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.10.2", "acct-1234567890ab@v2026.10.2"),
                Array.Empty<string>()));

    private PF.WorkflowPackageValidator.ValidationResult Validated()
    {
        var manifest = JsonSerializer.Deserialize<PF.WorkflowPackageManifest>(
            sent!.Manifest.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return PF.WorkflowPackageValidator.Validate(
            manifest,
            sent.Files.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            null,
            EditorFormatsCatalog.Catalog);
    }

    [Fact]
    public void TheTable_LoadsFromAHandWrittenCase_OnTheEnumsRow()
    {
        var page = RenderEditor(WithFile(File, Prompt));
        Navigate(page, "assessment");

        var kindRow = page.Find(Row("Reads of " + File, "input.kind"));
        Assert.Equal("macro.tool_note.prompt", kindRow.QuerySelector(Pick("new"))!.GetAttribute("current-value"));
        Assert.Equal("macro.closing.prompt", kindRow.QuerySelector(Pick("follow_up"))!.GetAttribute("current-value"));
        Assert.Equal("", kindRow.QuerySelector(Otherwise())!.GetAttribute("current-value") ?? "");
        // #1093: both readable kinds, prefixed by kind in the rows' own spelling.
        Assert.Equal(
            new[] { "nothing", "Macro 'Closing'", "Macro 'Tool note'", "Snippet 'standards'" },
            kindRow.QuerySelector(Pick("new"))!.QuerySelectorAll("fluent-option").Select(o => o.TextContent.Trim()));
        Assert.Equal(Written, kindRow.QuerySelector(Line())!.TextContent.Trim());

        // A read chosen by the case says so on its own row, with no guard builder.
        var toolNoteRow = page.Find(Row("Reads of " + File, "macro.tool_note.prompt"));
        Assert.Contains("chosen by input.kind", toolNoteRow.TextContent);
        Assert.Empty(toolNoteRow.QuerySelectorAll("fluent-select"));
        Assert.Empty(toolNoteRow.QuerySelectorAll("fluent-button[aria-label^='Add a condition']"));
    }

    [Fact]
    public void ChangingABranch_RewritesTheBlock_AndTheRegistryAccepts()
    {
        var page = RenderEditor(WithFile(File, Prompt));
        CapturePublish();
        Navigate(page, "assessment");

        page.Find(Pick("new")).Change("macro.closing.prompt");

        Assert.Equal(Original.Replace(ToolNote, "{{~ case input.kind ~}}{{~ when \"new\" ~}}" + Closing + "{{~ when \"follow_up\" ~}}" + Closing + "{{~ end ~}}"), EditorText(page));
        Assert.Contains("●", page.Find("button.editor-nav__item--selected").TextContent);

        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Publish")).Click();
        Assert.True(sent is not null, string.Join(" | ", page.FindAll(".fluent-messagebar-message li").Select(item => item.TextContent.Trim())));
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join("\n", validated.Errors));
    }

    [Fact]
    public void SettingAndClearingOtherwise()
    {
        var page = RenderEditor(WithFile(File, Prompt));
        Navigate(page, "assessment");

        page.Find(Otherwise()).Change("macro.tool_note.prompt");
        Assert.Contains("{{~ else ~}}" + ToolNote + "{{~ end ~}}", EditorText(page));

        page.Find(Otherwise()).Change("");
        Assert.DoesNotContain("else", EditorText(page));
        Assert.Equal(Original.Replace(ToolNote, Written), EditorText(page));
    }

    [Fact]
    public void ClearingEveryBranch_RemovesTheBlock()
    {
        var page = RenderEditor(WithFile(File, Prompt));
        Navigate(page, "assessment");

        page.Find(Pick("new")).Change("");
        page.Find(Pick("follow_up")).Change("");

        Assert.Equal(Original.Replace(ToolNote, ""), EditorText(page));
        Assert.Empty(page.FindAll(".branch-table"));
    }

    [Fact]
    public void AHandWrittenMultiLineCase_IsCanonical_AndNormalisesOnEdit()
    {
        var page = RenderEditor(WithFile(File, "{{ case input.kind }}\n{{ when \"new\" }}\n" + ToolNote + "\n{{ end }}\n"));
        Navigate(page, "assessment");

        Assert.Equal("macro.tool_note.prompt", page.Find(Pick("new")).GetAttribute("current-value"));
        Assert.Equal("", page.Find(Pick("follow_up")).GetAttribute("current-value") ?? "");

        page.Find(Pick("follow_up")).Change("macro.closing.prompt");

        Assert.Equal(Written + "\n", EditorText(page));
    }

    [Fact]
    public void ANonCanonicalCase_IsReadOnly()
    {
        var bogus = RenderEditor(WithFile(File, "{{ case input.kind }}{{ when \"bogus\" }}" + ToolNote + "{{ end }}"));
        Navigate(bogus, "assessment");
        var row = bogus.Find(Row("Reads of " + File, "input.kind"));
        Assert.Contains("chooses by", row.TextContent);
        Assert.Contains("edit in the text", row.TextContent);
        Assert.Empty(row.QuerySelectorAll(".branch-table__pick"));

        var prose = RenderEditor(WithFile(File, "{{ case input.kind }}{{ when \"new\" }}" + ToolNote + " extra{{ end }}"));
        Navigate(prose, "assessment");
        Assert.Empty(prose.Find(Row("Reads of " + File, "input.kind")).QuerySelectorAll(".branch-table__pick"));
    }

    [Fact]
    public void TheMacroPanesReadByRow_ShowsTheWholeTable_AndRewritesTheReadersFile()
    {
        var page = RenderEditor(WithFile(File, Prompt));
        Navigate(page, "tool_note");

        var row = page.Find(".template-read-rows[aria-label='Readers of tool_note'] .template-read-row[data-read-reader=\"Prompt 'assessment'\"]");
        Assert.Equal("macro.closing.prompt", row.QuerySelector(Pick("follow_up", where: "Prompt 'assessment'"))!.GetAttribute("current-value"));

        page.Find(Pick("follow_up", where: "Prompt 'assessment'")).Change("macro.tool_note.prompt");
        Navigate(page, "assessment");

        Assert.Contains("{{~ when \"follow_up\" ~}}" + ToolNote, EditorText(page));
    }

    [Fact]
    public void TheNodePane_ShowsTheTable_AsASentence()
    {
        var page = RenderEditor(WithFile(File, Prompt));
        ShowNode(page, "assessment");

        var row = page.Find(Row("Reads of assessment", "input.kind"));
        Assert.Contains("by", row.TextContent);
        Assert.Contains("new → Macro 'Tool note'", row.TextContent);
        Assert.Contains("follow_up → Macro 'Closing'", row.TextContent);
        Assert.Contains("otherwise → nothing", row.TextContent);
        Assert.Empty(row.QuerySelectorAll("fluent-select"));
    }

    [Fact]
    public void AnEnumNodeSubject_CarriesTheTable()
    {
        var page = RenderEditor(WithFile(File, "{{~ case node.scope.value ~}}{{~ when \"in_scope\" ~}}" + ToolNote + "{{~ end ~}}"));
        Navigate(page, "assessment");

        var row = page.Find(Row("Reads of " + File, "node.scope.value"));
        Assert.Equal("macro.tool_note.prompt", row.QuerySelector(Pick("in_scope", "node.scope.value"))!.GetAttribute("current-value"));
        Assert.Equal("", row.QuerySelector(Pick("out_of_scope", "node.scope.value"))!.GetAttribute("current-value") ?? "");
    }

    [Fact]
    public void AnUnlistedMemberInABranch_ShowsAsWritten()
    {
        var page = RenderEditor(WithFile(File, "{{~ case input.kind ~}}{{~ when \"new\" ~}}{{ macro.closing.content }}{{~ end ~}}"));
        Navigate(page, "assessment");

        var pick = page.Find(Pick("new"));
        Assert.Equal("macro.closing.content", pick.GetAttribute("current-value"));
        Assert.Contains("as written: macro.closing.content", pick.TextContent);
    }

    // ----- #1093: branches choosing snippets -----

    private const string Standards = "{{ snippet.standards.content }}";
    private const string SnippetCase = "{{~ case input.kind ~}}{{~ when \"new\" ~}}" + ToolNote + "{{~ when \"follow_up\" ~}}" + Standards + "{{~ end ~}}";
    private static string SnippetPrompt => Original.Replace(ToolNote, SnippetCase);

    [Fact]
    public void ABranchChoosingASnippet_LoadsAndWrites_AndTheRegistryAccepts()
    {
        var page = RenderEditor(WithFile(File, SnippetPrompt));
        CapturePublish();
        Navigate(page, "assessment");

        Assert.Equal("snippet.standards.content", page.Find(Pick("follow_up")).GetAttribute("current-value"));

        page.Find(Pick("new")).Change("snippet.standards.content");

        Assert.Equal(Original.Replace(ToolNote, "{{~ case input.kind ~}}{{~ when \"new\" ~}}" + Standards + "{{~ when \"follow_up\" ~}}" + Standards + "{{~ end ~}}"), EditorText(page));

        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Publish")).Click();
        Assert.True(sent is not null, string.Join(" | ", page.FindAll(".fluent-messagebar-message li").Select(item => item.TextContent.Trim())));
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join("\n", validated.Errors));
    }

    [Fact]
    public void TheSnippetPanesReadByRow_ShowsTheTable_AndRewritesTheReadersFile()
    {
        var page = RenderEditor(WithFile(File, SnippetPrompt));
        Navigate(page, "standards");

        var row = page.Find(".template-read-rows[aria-label='Readers of standards'] .template-read-row[data-read-reader=\"Prompt 'assessment'\"]");
        Assert.Equal("snippet.standards.content", row.QuerySelector(Pick("follow_up", where: "Prompt 'assessment'"))!.GetAttribute("current-value"));
        // The snippet's own Reads list (it reads nothing) carries no table.
        Assert.Empty(page.FindAll(".template-read-rows[aria-label='Reads of snippets/standards.md'] .branch-table"));

        page.Find(Pick("follow_up", where: "Prompt 'assessment'")).Change("macro.closing.prompt");
        Navigate(page, "assessment");

        Assert.Contains("{{~ when \"follow_up\" ~}}" + Closing, EditorText(page));
    }

    [Fact]
    public void TheNodePane_NamesASnippetBranch()
    {
        var page = RenderEditor(WithFile(File, SnippetPrompt));
        ShowNode(page, "assessment");

        var row = page.Find(Row("Reads of assessment", "input.kind"));
        Assert.Contains("follow_up → Snippet 'standards'", row.TextContent);
    }

    [Fact]
    public void AScalarSnippetsOwnBranches_OfferEveryOtherSource()
    {
        var page = RenderEditor(WithFile("snippets/standards.md", "{{~ case input.kind ~}}{{~ when \"new\" ~}}" + Closing + "{{~ end ~}}"));
        Navigate(page, "standards");

        var pick = page.Find(Pick("new", where: "snippets/standards.md"));
        Assert.Equal("macro.closing.prompt", pick.GetAttribute("current-value"));
        // A snippet may hold an inline read, so the macros take .prompt; no self-read; no other scalar in the fixture.
        Assert.Equal(
            new[] { "nothing", "Macro 'Closing'", "Macro 'Tool note'" },
            pick.QuerySelectorAll("fluent-option").Select(o => o.TextContent.Trim()));
    }

    // ----- #1120: starting a case from the row or the palette -----

    private const string KindRead = "{{ input.kind }}";
    private const string EmptyKindCase = "{{~ case input.kind ~}}{{~ end ~}}";
    private static string Start(string subject = "input.kind", string where = File) => $"fluent-button[aria-label=\"Start a case over {subject} in {where}\"]";
    private static string Collapse(string subject = "input.kind", string where = File) => $"fluent-button[aria-label=\"Collapse case over {subject} in {where}\"]";
    private static string Palette(string file = File) => $"fluent-select[aria-label='Reference to insert into {file}']";
    private static IReadOnlyList<string?> Choices(IRenderedComponent<Templates> page, string file = File) =>
        page.Find(Palette(file)).QuerySelectorAll("fluent-option").Where(o => o.GetAttribute("disabled") == null).Select(o => o.GetAttribute("value")).ToList();

    [Fact]
    public void AnEnumRowWithoutACase_CarriesTheRestingControl_AndNoTable()
    {
        var page = RenderEditor(WithFile(File, Original + "\n" + KindRead));
        Navigate(page, "assessment");

        var row = page.Find(Row("Reads of " + File, "input.kind"));
        Assert.Equal("choose a read by its value…", row.QuerySelector(Start())!.TextContent.Trim());
        Assert.Empty(row.QuerySelectorAll(".branch-table"));
        // A non-enum read row offers nothing.
        Assert.Empty(page.Find(Row("Reads of " + File, "input.notes")).QuerySelectorAll(".case-start__resting"));
    }

    [Fact]
    public void OpeningTheControl_ShowsAnEmptyTable_AndCollapsingWritesNothing()
    {
        var page = RenderEditor(WithFile(File, Original + "\n" + KindRead));
        Navigate(page, "assessment");
        var before = EditorText(page);

        page.Find(Start()).Click();

        var row = page.Find(Row("Reads of " + File, "input.kind"));
        Assert.NotNull(row.QuerySelector(".branch-table[aria-label=\"Case over input.kind in " + File + "\"]"));
        Assert.Equal("", page.Find(Pick("new")).GetAttribute("current-value"));
        Assert.Equal("", page.Find(Pick("follow_up")).GetAttribute("current-value"));
        Assert.Equal("", page.Find(Otherwise()).GetAttribute("current-value"));
        Assert.Equal(EmptyKindCase, page.Find(Line()).TextContent.Trim());
        Assert.Equal(before, EditorText(page));

        page.Find(Collapse()).Click();

        Assert.Empty(page.FindAll(".branch-table"));
        Assert.NotNull(page.Find(Start()));
        Assert.Equal(before, EditorText(page));
    }

    [Fact]
    public void TheFirstPick_AppendsTheOneLiner_AndTheRowCarriesTheRealTable()
    {
        var page = RenderEditor(WithFile(File, Original + "\n" + KindRead));
        CapturePublish();
        Navigate(page, "assessment");
        var before = EditorText(page);
        page.Find(Start()).Click();

        page.Find(Pick("new")).Change("macro.closing.prompt");

        Assert.Equal(before + "\n{{~ case input.kind ~}}{{~ when \"new\" ~}}" + Closing + "{{~ end ~}}", EditorText(page));
        // The walk owns the block now: the table persists from the text, the control is gone.
        Assert.Equal("macro.closing.prompt", page.Find(Pick("new")).GetAttribute("current-value"));
        Assert.Empty(page.FindAll(".case-start__resting"));
        Assert.Empty(page.FindAll(".case-start__collapse"));

        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Publish")).Click();

        var validated = Validated();
        Assert.True(validated.IsValid, string.Join("\n", validated.Errors));
    }

    [Fact]
    public void AnEnumNodeRow_StartsACase_BesideItsGuardBuilder()
    {
        var page = RenderEditor(WithFile(File, Original + "\n{{ node.scope.value }}"));
        Navigate(page, "assessment");

        var row = page.Find(Row("Reads of " + File, "node.scope.value"));
        Assert.NotNull(row.QuerySelector("fluent-button[aria-label=\"Add a condition for node.scope.value in " + File + "\"]"));
        Assert.NotNull(row.QuerySelector(Start("node.scope.value")));

        page.Find(Start("node.scope.value")).Click();

        Assert.Equal("", page.Find(Pick("in_scope", "node.scope.value")).GetAttribute("current-value"));
    }

    [Fact]
    public void ThePalette_OffersACaseOverEveryEnumSubject_NotYetCased_AfterProfile()
    {
        var page = RenderEditor();
        Navigate(page, "assessment");

        var choices = Choices(page);
        Assert.Equal(new[] { EmptyKindCase, "{{~ case node.scope.value ~}}{{~ end ~}}" }, choices.TakeLast(2));
        Assert.True(choices.ToList().IndexOf("{{ profile.name }}") < choices.ToList().IndexOf(EmptyKindCase));
        var options = page.Find(Palette()).QuerySelectorAll("fluent-option").Select(o => o.TextContent.Trim()).ToList();
        Assert.Contains("— cases —", options);
        Assert.Contains("case over input.kind", options);
        // Not read yet: no row to carry the control — the palette is the way in.
        Assert.Empty(page.FindAll(Row("Reads of " + File, "input.kind")));

        // The scope node's own prompt is never offered a case over its own value.
        Navigate(page, "scope");
        Assert.DoesNotContain("{{~ case node.scope.value ~}}{{~ end ~}}", Choices(page, "prompts/scope.md"));
        Assert.Contains(EmptyKindCase, Choices(page, "prompts/scope.md"));
    }

    [Fact]
    public void InsertingACaseFromThePalette_WritesTheEmptyBlock_AndTheRowAppears_AllNothing()
    {
        var page = RenderEditor();
        CapturePublish();
        Navigate(page, "assessment");

        page.Find(Palette()).Change(EmptyKindCase);
        page.Find($"fluent-button[aria-label='Insert reference into {File}']").Click();

        Assert.Equal(Original + "\n" + EmptyKindCase, EditorText(page));
        var row = page.Find(Row("Reads of " + File, "input.kind"));
        Assert.Empty(row.QuerySelectorAll(".case-start__resting"));
        Assert.Equal("", page.Find(Pick("new")).GetAttribute("current-value"));
        Assert.Equal("", page.Find(Otherwise()).GetAttribute("current-value"));
        Assert.Equal(EmptyKindCase, page.Find(Line()).TextContent.Trim());
        // The subject is cased now: the palette no longer offers it.
        Assert.DoesNotContain(EmptyKindCase, Choices(page));

        // A pick rewrites the block in place, not appended again.
        page.Find(Pick("follow_up")).Change("macro.closing.prompt");
        Assert.Equal(Original + "\n{{~ case input.kind ~}}{{~ when \"follow_up\" ~}}" + Closing + "{{~ end ~}}", EditorText(page));

        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Publish")).Click();

        var validated = Validated();
        Assert.True(validated.IsValid, string.Join("\n", validated.Errors));
    }

    [Fact]
    public void ThePalette_HidesACaseSubject_OnceTheTextHoldsOne()
    {
        var page = RenderEditor(WithFile(File, Prompt));
        Navigate(page, "assessment");

        var choices = Choices(page);
        Assert.DoesNotContain(EmptyKindCase, choices);
        Assert.Contains("{{~ case node.scope.value ~}}{{~ end ~}}", choices);
    }

    [Fact]
    public void TheInstructionPalette_OffersNoCase()
    {
        var page = RenderEditor();
        Navigate(page, "closing");

        var options = page.Find("fluent-select[aria-label='Reference to insert into instruction of macro closing']")
            .QuerySelectorAll("fluent-option").Select(o => o.TextContent.Trim()).ToList();
        Assert.DoesNotContain("— cases —", options);
        Assert.Contains("— cases —", page.Find("fluent-select[aria-label='Reference to insert into macros/closing.md']")
            .QuerySelectorAll("fluent-option").Select(o => o.TextContent.Trim()));
    }
}
