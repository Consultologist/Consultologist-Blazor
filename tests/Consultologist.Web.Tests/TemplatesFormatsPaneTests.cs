using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// v25 C1 (#1007, design § 3.6/§ 13): the Formats pane — the package's own custom
/// formats with a schema editor (producer fixed to custom), and the catalogue's
/// formats read-only with their detail line. Rendered at 25 alone; the Schemas
/// group stays below 25. Inert on the live editor until A5.
/// </summary>
public class TemplatesFormatsPaneTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse? fixture = null, bool withCatalogue = true)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture ?? EditorFixtures.V25());
        if (withCatalogue)
        {
            WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        }

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

    private static IReadOnlyList<string> Groups(IRenderedComponent<Templates> page) =>
        page.FindAll(".editor-nav__group").Select(g => g.TextContent.Trim()).ToList();

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

    private static IReadOnlyList<string> Formats(WorkflowPackagePublishRequest request) =>
        JsonDocument.Parse(request.Manifest.GetRawText()).RootElement.TryGetProperty("formats", out var formats)
            ? formats.EnumerateArray().Select(f => f.GetProperty("id").GetString()!).ToList()
            : Array.Empty<string>();

    private const string PlanShapeBody = """
        {
          "type": "object",
          "producer": "custom",
          "description": "The plan as a list of steps.",
          "schema": {
            "type": "object",
            "additionalProperties": false,
            "required": ["steps"],
            "properties": { "steps": { "type": "array", "items": { "type": "string" } } }
          }
        }
        """;

    // #1097: each format is a nav entry opening its own pane; "+ Format" is a pane; the catalogue a collapsed folder.
    private WorkflowEditorSession Session => Services.GetRequiredService<WorkflowEditorSession>();

    private void ShowNode(IRenderedComponent<Templates> page, string nodeId)
    {
        Session.SelectedKey = $"node:{nodeId}";
        page.Render();
    }

    private static string SelectedNav(IRenderedComponent<Templates> page) =>
        page.Find("button.editor-nav__item--selected").TextContent.Replace("●", string.Empty).Trim();

    private static void CreateFormat(IRenderedComponent<Templates> page, string id)
    {
        Navigate(page, "+ Format");
        page.Find("fluent-text-field[aria-label='New format id']").Change(id);
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create format")).Click();
    }

    [Fact]
    public void TheFormatsGroup_RendersAt25_InSchemasPlace()
    {
        var page = RenderEditor();

        var groups = Groups(page);
        Assert.Contains("Formats", groups);
        Assert.DoesNotContain("Schemas", groups);
        // C2 (#1008): the Data group is the Snippets group at 25.
        // #1098: Renderers follows Formats — a renderer is a format's rendering.
        Assert.Equal(new[] { "Workflow", "Nodes", "Prompts", "Macros", "Formats", "Renderers", "Snippets" }, groups);
    }

    [Fact]
    public void Below25_TheSchemasGroupStays_AndNoFormatsGroup()
    {
        var page = RenderEditor(EditorFixtures.V12());

        var groups = Groups(page);
        Assert.Contains("Schemas", groups);
        Assert.DoesNotContain("Formats", groups);
    }

    [Fact]
    public void TheFormatsGroup_ListsThePackagesFormats_ThenAdd_ThenTheCatalogueFolder()
    {
        var page = RenderEditor();

        var labels = page.FindAll("button.editor-nav__item").Select(b => b.TextContent.Replace("●", string.Empty).Trim()).ToList();
        Assert.DoesNotContain("Formats", labels);
        var medication = labels.IndexOf("medication_list");
        var add = labels.IndexOf("+ Format");
        Assert.True(medication >= 0 && add == medication + 1, string.Join(", ", labels));

        var folder = page.Find("details.editor-nav__folder--catalogue");
        Assert.Null(folder.GetAttribute("open"));
        Assert.Contains("catalogue (5)", folder.QuerySelector("summary")!.TextContent);
        // The catalogue's own order (alphabetical in the index).
        Assert.Equal(
            new[] { "classification", "concept_list", "prose", "rendered", "terms_subset" },
            folder.QuerySelectorAll("button.editor-nav__item--nested").Select(b => b.TextContent.Trim()));
    }

    [Fact]
    public void APackageFormatsPane_HasTheStrip_TheUsedByRow_AndTheBody()
    {
        var page = RenderEditor();
        Navigate(page, "medication_list");

        Assert.Equal("format:medication_list", Session.SelectedKey);
        var meds = page.Find("[data-format='medication_list']");
        Assert.Equal("medication_list", meds.QuerySelector("h3.panel-title")!.TextContent.Trim());
        var identity = meds.QuerySelector(".pane-identity")!.TextContent;
        Assert.Contains("formats/medication_list.json", identity);
        Assert.Contains("producer custom", identity);

        var users = page.Find(".template-read-rows[aria-label='Nodes using medication_list']");
        var row = Assert.Single(users.QuerySelectorAll(".template-read-row"));
        Assert.Equal("Node 'Medications'", row.GetAttribute("data-read-reader"));
        Assert.Contains("produces it", row.TextContent);
        Assert.Contains("\"producer\": \"custom\"", meds.QuerySelector("fluent-text-area[aria-label='Format body for medication_list']")!.GetAttribute("current-value"));
        Assert.NotNull(meds.QuerySelector("fluent-button[aria-label='Remove format medication_list'][disabled]"));
        Assert.Empty(meds.QuerySelectorAll(".format-error"));
        Assert.Contains("How formats work", meds.TextContent);

        row.QuerySelector(".template-read-row__reader")!.Click();
        Assert.Equal("node:meds", Session.SelectedKey);
    }

    [Fact]
    public void ACatalogueFormatsPane_IsReadOnly_WithItsDetailLine()
    {
        var page = RenderEditor();
        Navigate(page, "classification");

        Assert.Equal("catalogue:classification", Session.SelectedKey);
        var classification = page.Find("[data-catalogue-format='classification']");
        Assert.Contains("enum · attested", classification.QuerySelector(".pane-identity")!.TextContent);
        Assert.Contains("formats@v2026.10.2", classification.QuerySelector(".pane-identity")!.TextContent);
        Assert.Contains("served by classification@1, attested", classification.QuerySelector(".format-catalogue__detail")!.TextContent);
        Assert.Contains("parameters: values (values)", classification.TextContent);
        Assert.Equal("Node 'Scope'", page.Find(".template-read-rows[aria-label='Nodes using classification'] .template-read-row").GetAttribute("data-read-reader"));
        // Read-only: no editor, no remove, and never the agent's yaml.
        Assert.Empty(classification.QuerySelectorAll("fluent-text-area"));
        Assert.Empty(classification.QuerySelectorAll("fluent-button[aria-label^='Remove']"));
        Assert.DoesNotContain("instructions:", classification.TextContent);

        Navigate(page, "terms_subset");
        var termsSubset = page.Find("[data-catalogue-format='terms_subset']");
        Assert.Contains("computed by the engine (terms_subset)", termsSubset.TextContent);
        Assert.Contains("of (node of concept_list)", termsSubset.TextContent);
        Assert.NotNull(termsSubset.QuerySelector("pre.schema-body"));

        Navigate(page, "rendered");
        Assert.Contains("rendered by the engine", page.Find("[data-catalogue-format='rendered']").TextContent);
        Assert.Contains("No node produces", page.Find("[data-catalogue-format='rendered']").TextContent);
        Navigate(page, "prose");
        Assert.Contains("served by test-json@48, attested", page.Find("[data-catalogue-format='prose']").TextContent);
    }

    [Fact]
    public void WithoutTheCatalogue_TheFolderSaysSo_AndThePackageFormatStillOpens()
    {
        var page = RenderEditor(withCatalogue: false);

        var folder = page.Find("details.editor-nav__folder--catalogue");
        Assert.Contains("unavailable", folder.QuerySelector("summary")!.TextContent);
        Assert.Empty(folder.QuerySelectorAll("button.editor-nav__item--nested"));
        Assert.Empty(page.FindAll("[data-catalogue-format]"));

        Navigate(page, "medication_list");
        Assert.NotEmpty(page.FindAll("[data-format='medication_list'] fluent-text-area"));

        Navigate(page, "+ Format");
        Assert.Contains("The format catalogue is unavailable", page.Find(".editor-pane").TextContent);
    }

    [Fact]
    public void CreatingAFormat_LandsOnItsPane_WithADot_AndPublishesTheFile()
    {
        var page = RenderEditor();
        CapturePublish();

        CreateFormat(page, "plan_shape");

        Assert.Equal("format:plan_shape", Session.SelectedKey);
        Assert.Equal("plan_shape", SelectedNav(page));
        Assert.Contains("●", page.Find("button.editor-nav__item--selected").TextContent);
        var created = page.Find("[data-format='plan_shape']");
        Assert.Contains("Not yet used by any node", created.QuerySelector(".format-users__empty")!.TextContent);
        Assert.Contains("\"producer\": \"custom\"", created.QuerySelector("fluent-text-area")!.GetAttribute("current-value"));
        // Explicit init: the skeleton's blank description is refused under the editor until written.
        Assert.Contains(created.QuerySelectorAll(".format-error").Select(e => e.TextContent), e => e.Contains("description must not be blank"));

        page.Find("[data-format='plan_shape'] fluent-text-area").Change(PlanShapeBody);
        Assert.Empty(page.Find("[data-format='plan_shape']").QuerySelectorAll(".format-error"));

        // The node pane's picker now offers it.
        ShowNode(page, "meds");
        Assert.Contains("plan_shape", page.Find("fluent-select.node-format").QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value")));
        page.Find("fluent-select.node-format").Change("plan_shape");
        page.Find("fluent-select.node-on-invalid").Change("fail");

        // C4 (#1010): the desk now says "every reference resolves" — the assessment prompt
        // read node.meds.value.medications, a field the plan shape does not declare; read its steps.
        Navigate(page, "assessment");
        page.Find(".editor-pane fluent-text-area").Change("Assess.\n{{ snippet.standards.content }}\n{{ macro.tool_note.prompt }}\n{{ input.notes }}\n{{ for c in node.extract.value.concepts }}- {{ c }}\n{{ end }}\n{{ for s in node.meds.value.steps }}- {{ s }}\n{{ end }}");

        Publish(page);

        Assert.Empty(Refusals(page));
        Assert.Equal(new[] { "medication_list", "plan_shape" }, Formats(sent!));
        Assert.Equal(PlanShapeBody, sent!.Files["formats/plan_shape.json"]);
        Assert.Equal("plan_shape", JsonDocument.Parse(sent.Manifest.GetRawText()).RootElement.GetProperty("nodes").EnumerateArray()
            .Single(n => n.GetProperty("id").GetString() == "meds").GetProperty("format").GetString());
        var validated = Validated();
        Assert.DoesNotContain(validated.Errors, e => e.StartsWith("Format 'plan_shape'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Plan-Shape", "must be snake_case")]
    [InlineData("prose", "is a catalogue id and cannot be declared locally")]
    [InlineData("medication_list", "already exists")]
    public void CreatingAFormat_RefusesABadId(string id, string refusal)
    {
        var page = RenderEditor();

        CreateFormat(page, id);

        Assert.Equal("add-format", Session.SelectedKey);
        Assert.Contains(page.FindAll(".fluent-messagebar-message"), bar => bar.TextContent.Contains(refusal));
        Assert.Empty(page.FindAll($"[data-format='{id}'] fluent-text-area").Where(_ => id != "medication_list"));
    }

    [Theory]
    [InlineData("{ nope", "is not valid JSON")]
    [InlineData("""{ "type": "text", "producer": "attested", "description": "x" }""", "is package-local and must declare producer custom")]
    [InlineData("""{ "type": "object", "producer": "custom", "description": "x", "schema": { "type": "object", "additionalProperties": false, "properties": { "a": { "type": "string", "pattern": "^a" } }, "required": ["a"] } }""", "pattern")]
    public void EditingABody_ShowsTheRegistrysSentenceInline(string body, string sentence)
    {
        var page = RenderEditor();
        Navigate(page, "medication_list");

        page.Find("[data-format='medication_list'] fluent-text-area").Change(body);

        Assert.Contains(page.Find("[data-format='medication_list']").QuerySelectorAll(".format-error").Select(e => e.TextContent), e => e.Contains(sentence));
        Assert.Contains("●", page.Find("button.editor-nav__item--selected").TextContent);
    }

    [Fact]
    public void RemovingAFormat_IsRefusedWhileANodeNamesIt_ThenLandsOnAddFormat_AndTheNavStrikesIt()
    {
        var page = RenderEditor();
        CapturePublish();
        Navigate(page, "medication_list");
        Assert.NotNull(page.Find("fluent-button[aria-label='Remove format medication_list']").GetAttribute("disabled"));

        // Re-pick the reader onto a catalogue format; the medications read in the
        // assessment prompt goes with it (the author's two steps).
        ShowNode(page, "meds");
        page.Find("fluent-select.node-format").Change("prose");
        Session.SelectedKey = "prompts/assessment.md";
        page.Render();
        page.Find("fluent-text-area").Change("Assess.\n{{ input.notes }}\n{{ node.meds.content }}\n{{ for c in node.extract.value.concepts }}- {{ c }}\n{{ end }}");

        Navigate(page, "medication_list");
        Assert.Null(page.Find("fluent-button[aria-label='Remove format medication_list']").GetAttribute("disabled"));
        page.Find("fluent-button[aria-label='Remove format medication_list']").Click();

        Assert.Equal("add-format", Session.SelectedKey);
        Assert.NotEmpty(page.FindAll("[data-format-removed='medication_list'] .editor-nav__strike"));
        Assert.NotNull(page.Find("button[aria-label='Restore format medication_list']"));
        Assert.DoesNotContain("medication_list", page.FindAll("button.editor-nav__item").Select(b => b.TextContent.Replace("●", string.Empty).Trim()));
        Assert.Empty(page.FindAll("[data-format='medication_list']"));

        Publish(page);

        Assert.Empty(Refusals(page));
        Assert.Empty(Formats(sent!));
        Assert.False(sent!.Files.ContainsKey("formats/medication_list.json"));
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join(" | ", validated.Errors));
    }

    [Fact]
    public void ARemovedFormat_Restores_FromTheNav()
    {
        var page = RenderEditor();
        ShowNode(page, "meds");
        page.Find("fluent-select.node-format").Change("prose");
        Navigate(page, "medication_list");
        page.Find("fluent-button[aria-label='Remove format medication_list']").Click();

        page.Find("button[aria-label='Restore format medication_list']").Click();

        Assert.Empty(page.FindAll("[data-format-removed]"));
        Navigate(page, "medication_list");
        Assert.NotEmpty(page.FindAll("[data-format='medication_list'] fluent-text-area"));
    }

    [Fact]
    public void APendingFormat_RemovesOutright_AndLandsOnAddFormat()
    {
        var page = RenderEditor();
        CreateFormat(page, "plan_shape");

        page.Find("fluent-button[aria-label='Remove format plan_shape']").Click();

        Assert.Equal("add-format", Session.SelectedKey);
        Assert.Empty(page.FindAll("[data-format='plan_shape']"));
        Assert.Empty(page.FindAll("[data-format-removed]"));
        Assert.DoesNotContain("●", page.Find("nav").TextContent);
    }

    [Fact]
    public void AFormatsPane_SurvivesAReload_WithItsDraft()
    {
        var page = RenderEditor();
        CreateFormat(page, "plan_shape");
        page.Find("[data-format='plan_shape'] fluent-text-area").Change(PlanShapeBody);
        Navigate(page, "medication_list");
        page.Find("[data-format='medication_list'] fluent-text-area").Change("{ nope");

        var saved = JSInterop.Invocations["localStorage.setItem"]
            .Where(i => i.Arguments[0] is string key && key.StartsWith("workflow-editor-draft:", StringComparison.Ordinal))
            .Select(i => (Key: (string)i.Arguments[0]!, Json: (string)i.Arguments[1]!))
            .Last();
        JSInterop.Setup<string?>("localStorage.getItem", saved.Key).SetResult(saved.Json);

        var again = Render<Templates>();

        // The remembered pane is the format's own, and both edits came back.
        Assert.Equal("medication_list", again.Find("button.editor-nav__item--selected").TextContent.Replace("●", string.Empty).Trim());
        Assert.Equal("{ nope", again.Find("[data-format='medication_list'] fluent-text-area").GetAttribute("current-value"));
        Navigate(again, "plan_shape");
        Assert.Equal(PlanShapeBody, again.Find("[data-format='plan_shape'] fluent-text-area").GetAttribute("current-value"));
    }

    [Fact]
    public void DiscardingAFormatsPane_DropsItsEditsAlone()
    {
        var page = RenderEditor();
        CreateFormat(page, "plan_shape");
        Navigate(page, "medication_list");
        page.Find("[data-format='medication_list'] fluent-text-area").Change("{ nope");

        page.Find(".pane-discard-button").Click();
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Confirm discard")).Click();

        // This format's body is back; the pending format on its own pane is untouched.
        Assert.Contains("\"producer\": \"custom\"", page.Find("[data-format='medication_list'] fluent-text-area").GetAttribute("current-value"));
        Assert.DoesNotContain("●", page.Find("button.editor-nav__item--selected").TextContent);
        Assert.Contains("plan_shape", page.FindAll("button.editor-nav__item").Select(b => b.TextContent.Replace("●", string.Empty).Trim()));

        // A pending format's own discard drops it and lands on + Format.
        Navigate(page, "plan_shape");
        page.Find(".pane-discard-button").Click();
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Confirm discard")).Click();
        Assert.Equal("add-format", Session.SelectedKey);
        Assert.DoesNotContain("plan_shape", page.FindAll("button.editor-nav__item").Select(b => b.TextContent.Replace("●", string.Empty).Trim()));
    }

    [Fact]
    public void TheNodePanesFormatPicker_OpensTheFormatsPane()
    {
        var page = RenderEditor();

        ShowNode(page, "meds");
        page.Find("fluent-button[aria-label='Open format medication_list']").Click();
        Assert.Equal("format:medication_list", Session.SelectedKey);
        Assert.Equal("medication_list", SelectedNav(page));

        ShowNode(page, "scope");
        page.Find("fluent-button[aria-label='Open format classification']").Click();
        Assert.Equal("catalogue:classification", Session.SelectedKey);
        Assert.Equal("classification", SelectedNav(page));
    }
}
