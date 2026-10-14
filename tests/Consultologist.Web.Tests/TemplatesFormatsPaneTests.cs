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
            EditorFormatsCatalog.Catalog,
            EditorFormatsCatalog.AcceptedWith25);
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

    [Fact]
    public void TheFormatsGroup_RendersAt25_InSchemasPlace()
    {
        var page = RenderEditor();

        var groups = Groups(page);
        Assert.Contains("Formats", groups);
        Assert.DoesNotContain("Schemas", groups);
        // C2 (#1008): the Data group is the Snippets group at 25.
        Assert.Equal(new[] { "Workflow", "Nodes", "Prompts", "Macros", "Formats", "Snippets" }, groups);
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
    public void TheCatalogueFormats_ReadOnly_WithTheirDetailLine()
    {
        var page = RenderEditor();
        Navigate(page, "Formats");

        var classification = page.Find("[data-catalogue-format='classification']");
        Assert.Contains("served by classification@1, attested", classification.QuerySelector(".format-catalogue__detail")!.TextContent);
        Assert.Contains("enum · attested", classification.TextContent);
        Assert.Contains("parameters: values (values)", classification.TextContent);

        var termsSubset = page.Find("[data-catalogue-format='terms-subset']");
        Assert.Contains("computed by the engine (terms-subset)", termsSubset.TextContent);
        Assert.Contains("of (node of concept-list)", termsSubset.TextContent);
        Assert.NotNull(termsSubset.QuerySelector("pre.schema-body"));

        Assert.Contains("rendered by the engine", page.Find("[data-catalogue-format='rendered']").TextContent);
        Assert.Contains("served by test-json@48, attested", page.Find("[data-catalogue-format='prose']").TextContent);

        // Read-only: no editor, no remove, and never the agent's yaml.
        Assert.Empty(page.FindAll(".format-catalogue fluent-text-area"));
        Assert.Empty(page.FindAll(".format-catalogue fluent-button"));
        Assert.DoesNotContain("instructions:", page.Find(".editor-pane").TextContent);
        Assert.Contains("formats@v2026.10.1", page.Find(".editor-pane").TextContent);
    }

    [Fact]
    public void ThePackagesOwnFormat_IsEditable_AndNamesItsReader()
    {
        var page = RenderEditor();
        Navigate(page, "Formats");

        var meds = page.Find("[data-format='medication_list']");
        Assert.Contains("formats/medication_list.json · producer custom", meds.TextContent);
        Assert.Contains("Used by 'meds'.", meds.TextContent);
        Assert.Contains("\"producer\": \"custom\"", meds.QuerySelector("fluent-text-area")!.GetAttribute("current-value"));
        Assert.NotNull(meds.QuerySelector("fluent-button[aria-label='Remove format medication_list'][disabled]"));
        Assert.Empty(meds.QuerySelectorAll(".format-error"));
    }

    [Fact]
    public void WithoutTheCatalogue_ThePaneSaysSo()
    {
        var page = RenderEditor(withCatalogue: false);
        Navigate(page, "Formats");

        Assert.Contains("The format catalogue is unavailable", page.Find(".editor-pane").TextContent);
        Assert.Empty(page.FindAll("[data-catalogue-format]"));
        // The package's own format still edits.
        Assert.NotEmpty(page.FindAll("[data-format='medication_list'] fluent-text-area"));
    }

    [Fact]
    public void CreatingAFormat_LandsPending_EditsItsBody_AndPublishesTheFile()
    {
        var page = RenderEditor();
        CapturePublish();
        Navigate(page, "Formats");

        page.Find("fluent-text-field[aria-label='New format id']").Change("plan_shape");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create format")).Click();

        var created = page.Find("[data-format='plan_shape']");
        Assert.Contains("Not yet used by any node", created.TextContent);
        Assert.Contains("\"producer\": \"custom\"", created.QuerySelector("fluent-text-area")!.GetAttribute("current-value"));
        // Explicit init: the skeleton's blank description is refused under the editor until written.
        Assert.Contains(created.QuerySelectorAll(".format-error").Select(e => e.TextContent), e => e.Contains("description must not be blank"));
        Assert.Contains("●", page.Find("button.editor-nav__item--selected").TextContent);

        page.Find("[data-format='plan_shape'] fluent-text-area").Change(PlanShapeBody);
        Assert.Empty(page.Find("[data-format='plan_shape']").QuerySelectorAll(".format-error"));

        // The node pane's picker now offers it.
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = "node:meds";
        page.Render();
        Assert.Contains("plan_shape", page.Find("fluent-select.node-format").QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value")));
        page.Find("fluent-select.node-format").Change("plan_shape");
        page.Find("fluent-select.node-on-invalid").Change("fail");

        Publish(page);

        Assert.Empty(Refusals(page));
        Assert.Equal(new[] { "medication_list", "plan_shape" }, Formats(sent!));
        Assert.Equal(PlanShapeBody, sent!.Files["formats/plan_shape.json"]);
        Assert.Equal("plan_shape", JsonDocument.Parse(sent.Manifest.GetRawText()).RootElement.GetProperty("nodes").EnumerateArray()
            .Single(n => n.GetProperty("id").GetString() == "meds").GetProperty("format").GetString());
        var validated = Validated();
        // medication_list is now unused and meds reads a plan shape the assessment prompt does not — the
        // registry's words are about those reads, not the format file: it parsed, resolved and validated.
        Assert.DoesNotContain(validated.Errors, e => e.StartsWith("Format 'plan_shape'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Plan-Shape", "must be snake_case")]
    [InlineData("prose", "is a catalogue id and cannot be declared locally")]
    [InlineData("medication_list", "already exists")]
    public void CreatingAFormat_RefusesABadId(string id, string refusal)
    {
        var page = RenderEditor();
        Navigate(page, "Formats");

        page.Find("fluent-text-field[aria-label='New format id']").Change(id);
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create format")).Click();

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
        Navigate(page, "Formats");

        page.Find("[data-format='medication_list'] fluent-text-area").Change(body);

        Assert.Contains(page.Find("[data-format='medication_list']").QuerySelectorAll(".format-error").Select(e => e.TextContent), e => e.Contains(sentence));
    }

    [Fact]
    public void RemovingAFormat_IsRefusedWhileANodeNamesIt_ThenDropsItsEntryAndFile()
    {
        var page = RenderEditor();
        CapturePublish();
        Navigate(page, "Formats");
        Assert.NotNull(page.Find("fluent-button[aria-label='Remove format medication_list']").GetAttribute("disabled"));

        // Re-pick the reader onto a catalogue format; the medications read in the
        // assessment prompt goes with it (the author's two steps).
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = "node:meds";
        page.Render();
        page.Find("fluent-select.node-format").Change("prose");
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = "prompts/assessment.md";
        page.Render();
        page.Find("fluent-text-area").Change("Assess.\n{{ input.notes }}\n{{ node.meds.text }}\n{{ for c in node.extract.value.concepts }}- {{ c }}\n{{ end }}");

        Navigate(page, "Formats");
        Assert.Null(page.Find("fluent-button[aria-label='Remove format medication_list']").GetAttribute("disabled"));
        page.Find("fluent-button[aria-label='Remove format medication_list']").Click();

        Assert.NotEmpty(page.FindAll("[data-format-removed='medication_list']"));
        Assert.Empty(page.FindAll("[data-format='medication_list']"));

        Publish(page);

        Assert.Empty(Refusals(page));
        Assert.Empty(Formats(sent!));
        Assert.False(sent!.Files.ContainsKey("formats/medication_list.json"));
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join(" | ", validated.Errors));
    }

    [Fact]
    public void ARemovedFormat_Restores()
    {
        var page = RenderEditor();
        Navigate(page, "Formats");
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = "node:meds";
        page.Render();
        page.Find("fluent-select.node-format").Change("prose");
        Navigate(page, "Formats");
        page.Find("fluent-button[aria-label='Remove format medication_list']").Click();

        page.Find("button[aria-label='Restore format medication_list']").Click();

        Assert.NotEmpty(page.FindAll("[data-format='medication_list']"));
        Assert.Empty(page.FindAll("[data-format-removed]"));
    }

    [Fact]
    public void APendingFormat_RemovesOutright()
    {
        var page = RenderEditor();
        Navigate(page, "Formats");
        page.Find("fluent-text-field[aria-label='New format id']").Change("plan_shape");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create format")).Click();

        page.Find("fluent-button[aria-label='Remove format plan_shape']").Click();

        Assert.Empty(page.FindAll("[data-format='plan_shape']"));
        Assert.Empty(page.FindAll("[data-format-removed]"));
        Assert.DoesNotContain("●", page.Find("button.editor-nav__item--selected").TextContent);
    }

    [Fact]
    public void TheFormatsPane_SurvivesAReload_WithItsDraft()
    {
        var page = RenderEditor();
        Navigate(page, "Formats");
        page.Find("fluent-text-field[aria-label='New format id']").Change("plan_shape");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create format")).Click();
        page.Find("[data-format='plan_shape'] fluent-text-area").Change(PlanShapeBody);
        page.Find("[data-format='medication_list'] fluent-text-area").Change("{ nope");

        var saved = JSInterop.Invocations["localStorage.setItem"]
            .Where(i => i.Arguments[0] is string key && key.StartsWith("workflow-editor-draft:", StringComparison.Ordinal))
            .Select(i => (Key: (string)i.Arguments[0]!, Json: (string)i.Arguments[1]!))
            .Last();
        JSInterop.Setup<string?>("localStorage.getItem", saved.Key).SetResult(saved.Json);

        var again = Render<Templates>();

        // The remembered pane is the Formats pane, and both edits came back.
        Assert.Equal("Formats", again.Find("button.editor-nav__item--selected").TextContent.Replace("●", string.Empty).Trim());
        Assert.Equal(PlanShapeBody, again.Find("[data-format='plan_shape'] fluent-text-area").GetAttribute("current-value"));
        Assert.Equal("{ nope", again.Find("[data-format='medication_list'] fluent-text-area").GetAttribute("current-value"));
    }

    [Fact]
    public void DiscardingThePane_DropsItsEdits()
    {
        var page = RenderEditor();
        Navigate(page, "Formats");
        page.Find("fluent-text-field[aria-label='New format id']").Change("plan_shape");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create format")).Click();
        page.Find("[data-format='medication_list'] fluent-text-area").Change("{ nope");

        page.Find(".pane-discard-button").Click();
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Confirm discard")).Click();

        Assert.Empty(page.FindAll("[data-format='plan_shape']"));
        Assert.Contains("\"producer\": \"custom\"", page.Find("[data-format='medication_list'] fluent-text-area").GetAttribute("current-value"));
        Assert.DoesNotContain("●", page.Find("button.editor-nav__item--selected").TextContent);
    }
}
