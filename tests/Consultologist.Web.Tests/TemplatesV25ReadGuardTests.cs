using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// #1088 (Milestone 31): a guarded inline read set from the row — the "Included when"
/// builder on a macro.&lt;id&gt;.prompt read row, loaded with the {{ if … }} the walker finds
/// around the read, rewriting the owning text in place with the canonical one-liner;
/// read-only where the shape is not canonical, where the member is read more than once,
/// and on the panes that do not edit the text (the node pane, an instruction).
/// </summary>
public class TemplatesV25ReadGuardTests : ClientRenderTestContext
{
    private const string Read = "{{ macro.tool_note.prompt }}";
    private const string HpiFile = "snippets/sections/hpi.md";
    private const string HpiSubject = "macro.tool_note.prompt in " + HpiFile;
    private const string HpiRow = ".template-read-rows[aria-label='Reads of " + HpiFile + "'] .template-read-row[data-read-member='macro.tool_note.prompt']";
    private const string GuardedHpi = "History: {{ input.notes }}\n{{~ if input.kind == \"new\" ~}}" + Read + "{{~ end ~}}\n";

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

    // A subject may carry the walk's single-quoted spelling (Snippet 'sections/hpi'), so the attribute values are double-quoted.
    private static string AddGuard(string subject) => $"fluent-button[aria-label=\"Add a condition for {subject}\"]";
    private static string Operand(string subject) => $"fluent-select[aria-label^=\"Condition operand for {subject}\"]";
    private static string Value(string subject) => $"fluent-select[aria-label^=\"Condition value for {subject}\"]";
    private static string Writes(string subject) => $"code[aria-label=\"Line written for {subject}\"]";

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
    public void ALoadedGuard_ShowsInTheBuilder_AndTheLineItWrites()
    {
        var page = RenderEditor(WithFile(HpiFile, GuardedHpi));
        Navigate(page, "HPI");

        var row = page.Find(HpiRow);
        Assert.Contains("Included when", row.TextContent);
        Assert.Equal("kind", row.QuerySelector(Operand(HpiSubject))!.GetAttribute("current-value"));
        Assert.Equal("new", row.QuerySelector(Value(HpiSubject))!.GetAttribute("current-value"));
        Assert.Equal("{{~ if input.kind == \"new\" ~}}" + Read + "{{~ end ~}}", row.QuerySelector(Writes(HpiSubject))!.TextContent.Trim());
        Assert.DoesNotContain("edit in the text", row.TextContent);
    }

    [Fact]
    public void EditingTheGuard_RewritesTheText_InPlace()
    {
        var page = RenderEditor(WithFile(HpiFile, GuardedHpi));
        Navigate(page, "HPI");

        page.Find(Value(HpiSubject)).Change("follow_up");

        Assert.Equal("History: {{ input.notes }}\n{{~ if input.kind == \"follow_up\" ~}}" + Read + "{{~ end ~}}\n", EditorText(page));
        Assert.Contains("●", page.Find("button.editor-nav__item--selected").TextContent);
        Assert.Equal("{{~ if input.kind == \"follow_up\" ~}}" + Read + "{{~ end ~}}", page.Find(Writes(HpiSubject)).TextContent.Trim());
    }

    [Fact]
    public void ChoosingAlways_RemovesTheWrapper()
    {
        var page = RenderEditor(WithFile(HpiFile, GuardedHpi));
        Navigate(page, "HPI");

        page.Find(Operand(HpiSubject)).Change("");

        Assert.Equal("History: {{ input.notes }}\n" + Read + "\n", EditorText(page));
        Assert.Equal(Read, page.Find(Writes(HpiSubject)).TextContent.Trim());
    }

    [Fact]
    public void AnUnguardedRead_IsWrapped_InTheCanonicalShape_AndTheRegistryAccepts()
    {
        var page = RenderEditor();
        CapturePublish();
        Navigate(page, "assessment");
        const string subject = "macro.tool_note.prompt in prompts/assessment.md";
        var original = EditorFixtures.V25().Files["prompts/assessment.md"];

        page.Find(AddGuard(subject)).Click();
        page.Find(Operand(subject)).Change("kind");
        page.Find(Value(subject)).Change("new");

        Assert.Equal(original.Replace(Read, "{{~ if input.kind == \"new\" ~}}" + Read + "{{~ end ~}}"), EditorText(page));

        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Publish")).Click();
        Assert.NotNull(sent);
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join("\n", validated.Errors));
    }

    [Fact]
    public void AHandWrittenThreeLineGuard_IsCanonical_AndNormalisesOnEdit()
    {
        var page = RenderEditor(WithFile(HpiFile, "{{ if input.kind == \"new\" }}\n" + Read + "\n{{ end }}\n"));
        Navigate(page, "HPI");

        Assert.Equal("kind", page.Find(Operand(HpiSubject)).GetAttribute("current-value"));
        Assert.Equal("new", page.Find(Value(HpiSubject)).GetAttribute("current-value"));

        page.Find(Value(HpiSubject)).Change("follow_up");

        Assert.Equal("{{~ if input.kind == \"follow_up\" ~}}" + Read + "{{~ end ~}}\n", EditorText(page));
    }

    [Fact]
    public void ANonCanonicalGuard_IsReadOnly()
    {
        var page = RenderEditor(WithFile(HpiFile, "{{ if input.kind == \"new\" }}" + Read + " extra{{ end }}"));
        Navigate(page, "HPI");

        var row = page.Find(HpiRow);
        Assert.Contains("guarded by", row.TextContent);
        Assert.Contains("input.kind == \"new\"", row.TextContent);
        Assert.Contains("edit in the text", row.TextContent);
        Assert.Empty(row.QuerySelectorAll(AddGuard(HpiSubject)));
        Assert.Empty(row.QuerySelectorAll(Operand(HpiSubject)));
    }

    [Fact]
    public void AReadTwice_OffersNoBuilder()
    {
        var page = RenderEditor(WithFile(HpiFile, Read + "\n" + Read));
        Navigate(page, "HPI");

        var row = page.Find(HpiRow);
        Assert.Contains("read 2 times", row.TextContent);
        Assert.Contains("edit the guards in the text", row.TextContent);
        Assert.Empty(row.QuerySelectorAll(AddGuard(HpiSubject)));
    }

    [Fact]
    public void TheMacroPanesReadByRow_CarriesTheBuilder_AndRewritesTheReadersFile()
    {
        var page = RenderEditor(WithFile(HpiFile, GuardedHpi));
        Navigate(page, "tool_note");
        const string subject = "macro.tool_note.prompt in Snippet 'sections/hpi'";

        var row = page.Find(".template-read-rows[aria-label='Readers of tool_note'] .template-read-row[data-read-reader=\"Snippet 'sections/hpi'\"]");
        Assert.Equal("new", row.QuerySelector(Value(subject))!.GetAttribute("current-value"));

        page.Find(Value(subject)).Change("follow_up");
        Navigate(page, "HPI");

        Assert.Equal("History: {{ input.notes }}\n{{~ if input.kind == \"follow_up\" ~}}" + Read + "{{~ end ~}}\n", EditorText(page));
    }

    [Fact]
    public void TheNodePane_ShowsTheGuard_ReadOnly()
    {
        var page = RenderEditor(WithFile(HpiFile, GuardedHpi));
        ShowNode(page, "section_notes");

        var row = page.Find(".template-read-rows[aria-label='Inline macros of section_notes'] .template-read-row[data-read-member='macro.tool_note.prompt']");
        Assert.Contains("snippet 'sections/hpi'", row.TextContent);
        Assert.Contains("included when", row.TextContent);
        Assert.Contains("input.kind == \"new\"", row.TextContent);
        Assert.Empty(row.QuerySelectorAll("fluent-select"));
        Assert.Empty(row.QuerySelectorAll("fluent-button[aria-label^='Add a condition']"));
    }

    [Fact]
    public void AnInstructionsRead_ShowsTheGuard_ReadOnly()
    {
        var page = RenderEditor();
        Navigate(page, "closing");
        page.Find("fluent-text-area[aria-label='Instruction for macro closing']").Change("{{~ if input.kind == \"new\" ~}}" + Read + "{{~ end ~}}");

        var row = page.Find(".template-read-rows[aria-label='Reads of instruction of macro closing'] .template-read-row[data-read-member='macro.tool_note.prompt']");
        Assert.Contains("included when", row.TextContent);
        Assert.Contains("input.kind == \"new\"", row.TextContent);
        Assert.Empty(row.QuerySelectorAll("fluent-button[aria-label^='Add a condition']"));
    }

    // ----- #1092: every read kind -----

    private const string SnippetRead = "{{ snippet.standards.content }}";
    private const string GuardedAssessment = "Assess.\n{{~ if input.kind == \"new\" ~}}" + SnippetRead + "{{~ end ~}}\n{{ input.notes }}";

    private static string Row(string list, string member) => $".template-read-rows[aria-label=\"{list}\"] .template-read-row[data-read-member='{member}']";

    [Fact]
    public void ASnippetRead_OnThePromptPane_CarriesTheBuilder_AndIsWrapped()
    {
        var page = RenderEditor();
        CapturePublish();
        Navigate(page, "assessment");
        const string subject = "snippet.standards.content in prompts/assessment.md";
        var original = EditorFixtures.V25().Files["prompts/assessment.md"];

        page.Find(AddGuard(subject)).Click();
        page.Find(Operand(subject)).Change("kind");
        page.Find(Value(subject)).Change("new");

        Assert.Equal(original.Replace(SnippetRead, "{{~ if input.kind == \"new\" ~}}" + SnippetRead + "{{~ end ~}}"), EditorText(page));
        Assert.Contains(Read, EditorText(page));
        Assert.Equal("{{~ if input.kind == \"new\" ~}}" + SnippetRead + "{{~ end ~}}", page.Find(Writes(subject)).TextContent.Trim());

        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Publish")).Click();
        Assert.NotNull(sent);
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join("\n", validated.Errors));
    }

    [Fact]
    public void AMacroContentRead_CarriesTheBuilder_OnTheReadersPane_AndOnTheMacroPane()
    {
        var page = RenderEditor(WithFile("snippets/standards.md", "Follow the local standards. {{ macro.tool_note.content }}"));
        Navigate(page, "standards");

        Assert.NotNull(page.Find(Row("Reads of snippets/standards.md", "macro.tool_note.content")).QuerySelector(AddGuard("macro.tool_note.content in snippets/standards.md")));

        Navigate(page, "tool_note");
        const string subject = "macro.tool_note.content in Snippet 'standards'";
        var row = page.Find(".template-read-rows[aria-label='Readers of tool_note'] .template-read-row[data-read-reader=\"Snippet 'standards'\"]");
        Assert.Equal("macro.tool_note.content", row.QuerySelector(".template-read-row__member")!.TextContent.Trim());
        row.QuerySelector(AddGuard(subject))!.Click();
        page.Find(Operand(subject)).Change("kind");
        page.Find(Value(subject)).Change("follow_up");

        Navigate(page, "standards");
        Assert.Equal("Follow the local standards. {{~ if input.kind == \"follow_up\" ~}}{{ macro.tool_note.content }}{{~ end ~}}", EditorText(page));
    }

    [Fact]
    public void ANodeRead_CarriesTheBuilder()
    {
        var page = RenderEditor(WithFile("prompts/scope.md", "Is this in scope? {{ node.meds.value }}"));
        Navigate(page, "scope");
        const string subject = "node.meds.value in prompts/scope.md";

        var row = page.Find(Row("Reads of prompts/scope.md", "node.meds.value"));
        Assert.Equal("Node 'Medications'", row.GetAttribute("data-read-reader"));
        row.QuerySelector(AddGuard(subject))!.Click();
        page.Find(Operand(subject)).Change("kind");
        page.Find(Value(subject)).Change("new");

        Assert.Equal("Is this in scope? {{~ if input.kind == \"new\" ~}}{{ node.meds.value }}{{~ end ~}}", EditorText(page));
    }

    [Fact]
    public void TheSnippetPanesReadByRow_CarriesTheBuilder_AndRewritesTheReadersFile()
    {
        var page = RenderEditor(WithFile("prompts/assessment.md", GuardedAssessment));
        Navigate(page, "standards");
        const string subject = "snippet.standards.content in Prompt 'assessment'";

        var row = page.Find(".template-read-rows[aria-label='Readers of standards'] .template-read-row[data-read-reader=\"Prompt 'assessment'\"]");
        Assert.Equal("kind", row.QuerySelector(Operand(subject))!.GetAttribute("current-value"));
        Assert.Equal("new", row.QuerySelector(Value(subject))!.GetAttribute("current-value"));

        page.Find(Value(subject)).Change("follow_up");
        Navigate(page, "assessment");

        Assert.Equal("Assess.\n{{~ if input.kind == \"follow_up\" ~}}" + SnippetRead + "{{~ end ~}}\n{{ input.notes }}", EditorText(page));
    }

    [Fact]
    public void TheNodePanesReadsSection_ShowsASnippetReadsGuard_ReadOnly()
    {
        var page = RenderEditor(WithFile("prompts/assessment.md", GuardedAssessment));
        ShowNode(page, "assessment");

        var row = page.Find(Row("Reads of assessment", "snippet.standards.content"));
        Assert.Contains("included when", row.TextContent);
        Assert.Contains("input.kind == \"new\"", row.TextContent);
        Assert.Empty(row.QuerySelectorAll("fluent-select"));
    }

    [Fact]
    public void AnInputRead_AFactRead_AndAFolderIteration_CarryNoBuilder()
    {
        var page = RenderEditor();
        Navigate(page, "assessment");
        Assert.Empty(page.FindAll(AddGuard("input.notes in prompts/assessment.md")));
        Assert.Empty(page.Find(Row("Reads of prompts/assessment.md", "input.notes")).QuerySelectorAll(".template-read-row__below"));

        Navigate(page, "closing");
        Assert.Empty(page.Find(Row("Reads of macros/closing.md", "profile.name")).QuerySelectorAll(".template-read-row__below"));

        var looping = RenderEditor(WithFile("prompts/scope.md", "{{ for s in snippet.sections }}{{ s.content }}{{ end }}"));
        Navigate(looping, "scope");
        Assert.Empty(looping.Find(Row("Reads of prompts/scope.md", "snippet.sections")).QuerySelectorAll(".template-read-row__below"));
        Assert.Empty(looping.FindAll("fluent-button[aria-label^='Add a condition for']"));
    }

    // ----- #1121: the if … else extension — "otherwise" on Included when -----

    private const string ClosingRead = "{{ macro.closing.prompt }}";
    private const string IfElseHpi = "History: {{ input.notes }}\n{{~ if input.kind == \"new\" ~}}" + Read + "{{~ else ~}}" + ClosingRead + "{{~ end ~}}\n";
    private const string IfElseLine = "{{~ if input.kind == \"new\" ~}}" + Read + "{{~ else ~}}" + ClosingRead + "{{~ end ~}}";
    private static string Otherwise(string subject) => $"fluent-select[aria-label=\"Otherwise for {subject}\"]";
    private const string HpiReader = ".template-read-rows[aria-label='Readers of tool_note'] .template-read-row[data-read-reader=\"Snippet 'sections/hpi'\"]";

    [Fact]
    public void AHandWrittenIfElse_LoadsTheOtherwise_AndTheLineItWrites()
    {
        var page = RenderEditor(WithFile(HpiFile, IfElseHpi));
        Navigate(page, "HPI");

        var row = page.Find(HpiRow);
        Assert.Equal("kind", row.QuerySelector(Operand(HpiSubject))!.GetAttribute("current-value"));
        Assert.Equal("new", row.QuerySelector(Value(HpiSubject))!.GetAttribute("current-value"));
        Assert.Equal("macro.closing.prompt", row.QuerySelector(Otherwise(HpiSubject))!.GetAttribute("current-value"));
        Assert.Equal(IfElseLine, row.QuerySelector(Writes(HpiSubject))!.TextContent.Trim());
        Assert.DoesNotContain("guarded by", row.TextContent);
        Assert.DoesNotContain("edit in the text", row.TextContent);
    }

    [Fact]
    public void PickingAnOtherwise_WritesTheOneLiner_AndTheRegistryAccepts()
    {
        var page = RenderEditor(WithFile(HpiFile, GuardedHpi));
        CapturePublish();
        Navigate(page, "HPI");

        page.Find(Otherwise(HpiSubject)).Change("macro.closing.prompt");

        Assert.Equal(IfElseHpi, EditorText(page));
        Assert.Equal(IfElseLine, page.Find(Writes(HpiSubject)).TextContent.Trim());

        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Publish")).Click();

        var validated = Validated();
        Assert.True(validated.IsValid, string.Join("\n", validated.Errors));
    }

    [Fact]
    public void ClearingTheOtherwise_WritesTheGuardShape()
    {
        var page = RenderEditor(WithFile(HpiFile, IfElseHpi));
        Navigate(page, "HPI");

        page.Find(Otherwise(HpiSubject)).Change("");

        Assert.Equal(GuardedHpi, EditorText(page));
    }

    [Fact]
    public void EditingTheCondition_KeepsTheOtherwise()
    {
        var page = RenderEditor(WithFile(HpiFile, IfElseHpi));
        Navigate(page, "HPI");

        page.Find(Value(HpiSubject)).Change("follow_up");

        Assert.Equal("History: {{ input.notes }}\n{{~ if input.kind == \"follow_up\" ~}}" + Read + "{{~ else ~}}" + ClosingRead + "{{~ end ~}}\n", EditorText(page));
    }

    [Fact]
    public void ClearingToAlways_IsRefused_WhileAnOtherwiseIsSet()
    {
        var page = RenderEditor(WithFile(HpiFile, IfElseHpi));
        Navigate(page, "HPI");

        page.Find(Operand(HpiSubject)).Change("");

        Assert.Equal(IfElseHpi, EditorText(page));
        var row = page.Find(HpiRow);
        Assert.Contains("set otherwise to nothing first", row.TextContent);
        // The builder still shows the condition the text keeps.
        Assert.Equal("kind", row.QuerySelector(Operand(HpiSubject))!.GetAttribute("current-value"));
        Assert.Equal("new", row.QuerySelector(Value(HpiSubject))!.GetAttribute("current-value"));

        page.Find(Otherwise(HpiSubject)).Change("");
        Assert.DoesNotContain("set otherwise to nothing first", page.Find(HpiRow).TextContent);
        page.Find(Operand(HpiSubject)).Change("");

        Assert.Equal("History: {{ input.notes }}\n" + Read + "\n", EditorText(page));
    }

    [Fact]
    public void AnUnguardedRead_OffersNoOtherwise_UntilAConditionIsSet()
    {
        var page = RenderEditor();
        Navigate(page, "assessment");
        const string subject = "macro.tool_note.prompt in prompts/assessment.md";

        Assert.Empty(page.FindAll(Otherwise(subject)));

        page.Find(AddGuard(subject)).Click();
        page.Find(Operand(subject)).Change("kind");
        page.Find(Value(subject)).Change("new");

        Assert.Equal("", page.Find(Otherwise(subject)).GetAttribute("current-value"));
    }

    [Fact]
    public void TheElseReadsRow_SaysOtherwiseOf_AndCarriesNoBuilder()
    {
        var page = RenderEditor(WithFile(HpiFile, IfElseHpi));
        Navigate(page, "HPI");

        var row = page.Find(Row("Reads of " + HpiFile, "macro.closing.prompt"));
        Assert.Contains("otherwise of input.kind == \"new\"", row.TextContent);
        Assert.Empty(row.QuerySelectorAll("fluent-button[aria-label^='Add a condition for']"));
        Assert.Empty(row.QuerySelectorAll("fluent-select"));
    }

    [Fact]
    public void TheMacroPanesReadByRow_ShowsTheTwoLineTable_AndRewritesTheReadersFile()
    {
        var page = RenderEditor(WithFile(HpiFile, IfElseHpi));
        Navigate(page, "tool_note");
        const string where = "Snippet 'sections/hpi'";

        var row = page.Find(HpiReader);
        var table = row.QuerySelector(".branch-table[aria-label=\"If over input.kind == \\\"new\\\" in " + where + "\"]");
        Assert.NotNull(table);
        var when = row.QuerySelector("fluent-select[aria-label=\"When of input.kind == \\\"new\\\" in " + where + "\"]")!;
        var otherwise = row.QuerySelector("fluent-select[aria-label=\"Otherwise of input.kind == \\\"new\\\" in " + where + "\"]")!;
        Assert.Equal("macro.tool_note.prompt", when.GetAttribute("current-value"));
        Assert.Equal("macro.closing.prompt", otherwise.GetAttribute("current-value"));
        Assert.Equal(IfElseLine, row.QuerySelector("code[aria-label=\"Line written for input.kind == \\\"new\\\" in " + where + "\"]")!.TextContent.Trim());

        otherwise.Change("");
        Navigate(page, "HPI");

        Assert.Equal(GuardedHpi, EditorText(page));
    }

    [Fact]
    public void TheNodePane_ShowsTheIfElse_AsASentence()
    {
        var page = RenderEditor(WithFile(HpiFile, IfElseHpi));
        ShowNode(page, "section_notes");

        var row = page.Find(".template-read-rows[aria-label='Inline macros of section_notes'] .template-read-row[data-read-member='macro.tool_note.prompt']");
        Assert.Contains("if input.kind == \"new\": when → Macro 'Tool note'; otherwise → Macro 'Closing'", row.TextContent);
        Assert.DoesNotContain("included when", row.TextContent);
        Assert.Empty(row.QuerySelectorAll("fluent-select"));
    }

    [Fact]
    public void AnElseOnlyIf_PutsTheNoteOnTheElseRead_AndTheTableShowsWhenAtNothing()
    {
        var page = RenderEditor(WithFile(HpiFile, "History: {{ input.notes }}\n{{~ if input.kind == \"new\" ~}}{{~ else ~}}" + Read + "{{~ end ~}}\n"));
        Navigate(page, "HPI");

        var row = page.Find(HpiRow);
        Assert.Contains("otherwise of input.kind == \"new\"", row.TextContent);
        Assert.Empty(row.QuerySelectorAll("fluent-select"));

        Navigate(page, "tool_note");
        var reader = page.Find(HpiReader);
        Assert.Equal("", reader.QuerySelector("fluent-select[aria-label=\"When of input.kind == \\\"new\\\" in Snippet 'sections/hpi'\"]")!.GetAttribute("current-value"));
        Assert.Equal("macro.tool_note.prompt", reader.QuerySelector("fluent-select[aria-label=\"Otherwise of input.kind == \\\"new\\\" in Snippet 'sections/hpi'\"]")!.GetAttribute("current-value"));
    }
}
