using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// #991 (B): the Inputs pane's "Derive from a sample…" door under a fields
/// editor — a pasted sample object adds the fields the owner lacks (optional,
/// typed from the sample), names what it added, never retypes an existing
/// field, and refuses by key; the result publishes through the validator.
/// </summary>
public class TemplatesFieldsFromSampleTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor()
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V25());
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        var page = Render<Templates>();
        page.FindAll("button.editor-nav__item").First(button => button.TextContent.Replace("●", string.Empty).Trim() == "Inputs").Click();
        return page;
    }

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

    private static void OpenSample(IRenderedComponent<Templates> page, string owner) =>
        page.Find($"[data-fields-for='{owner}'] fluent-button[aria-label='Derive fields for {owner} from a sample']").Click();

    private static void Derive(IRenderedComponent<Templates> page, string owner, string sample)
    {
        page.Find($"fluent-text-area[aria-label='Sample for {owner}']").Change(sample);
        page.Find($"fluent-button[aria-label='Derive fields for {owner}']").Click();
    }

    private static IReadOnlyList<string?> FieldPaths(IRenderedComponent<Templates> page) =>
        page.FindAll("li.declared-field").Select(row => row.GetAttribute("data-field")).ToList();

    private static void Publish(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

    [Fact]
    public void TheToggle_OpensASampleTextAreaForTheOwner()
    {
        var page = RenderEditor();
        Assert.Empty(page.FindAll("fluent-text-area[aria-label='Sample for intake']"));

        OpenSample(page, "intake");

        Assert.Single(page.FindAll("fluent-text-area[aria-label='Sample for intake']"));
    }

    [Fact]
    public void DerivingFromASample_AddsTheMissingFields_AndPublishesValid()
    {
        var page = RenderEditor();
        CapturePublish();
        OpenSample(page, "intake");

        Derive(page, "intake", """{ "allergies": ["latex"], "on_letrozole": true, "smoker": false, "last_visit": "2026-09-01" }""");

        Assert.Equal(new[] { "intake.allergies", "intake.on_letrozole", "intake.smoker", "intake.last_visit" }, FieldPaths(page));
        Assert.Equal("boolean", page.Find("fluent-select[aria-label='Type for field intake.smoker']").GetAttribute("current-value") ?? page.Find("fluent-select[aria-label='Type for field intake.smoker']").GetAttribute("value"));
        Assert.Equal("date", page.Find("fluent-select[aria-label='Type for field intake.last_visit']").GetAttribute("current-value") ?? page.Find("fluent-select[aria-label='Type for field intake.last_visit']").GetAttribute("value"));
        Assert.Contains("Added 'smoker', 'last_visit' from the sample", page.Find(".fields-sample__note").TextContent);
        Assert.Empty(page.FindAll(".editor-warning"));

        Publish(page);

        Assert.NotNull(sent);
        var fields = JsonDocument.Parse(sent!.Manifest.GetRawText()).RootElement.GetProperty("inputs").EnumerateArray()
            .Single(input => input.GetProperty("id").GetString() == "intake").GetProperty("fields").EnumerateArray().ToList();
        Assert.Equal(new[] { "allergies", "on_letrozole", "smoker", "last_visit" }, fields.Select(field => field.GetProperty("id").GetString()));
        Assert.False(fields[2].GetProperty("required").GetBoolean());
        Assert.Equal("date", fields[3].GetProperty("type").GetString());
        var result = Validated();
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void DerivingDoesNotRetypeAnExistingField()
    {
        var page = RenderEditor();
        OpenSample(page, "intake");

        Derive(page, "intake", """{ "on_letrozole": "yes" }""");

        Assert.Equal("boolean", page.Find("fluent-select[aria-label='Type for field intake.on_letrozole']").GetAttribute("current-value") ?? page.Find("fluent-select[aria-label='Type for field intake.on_letrozole']").GetAttribute("value"));
        Assert.Contains("Nothing to add", page.Find(".fields-sample__note").TextContent);
    }

    [Fact]
    public void ANestedObject_DerivesANestedFieldsEditor()
    {
        var page = RenderEditor();
        OpenSample(page, "intake");

        Derive(page, "intake", """{ "visit": { "date": "2026-09-01", "ward": "B2" } }""");

        Assert.Single(page.FindAll("[data-fields-for='intake.visit']"));
        Assert.Contains("intake.visit.date", FieldPaths(page));
        Assert.Contains("intake.visit.ward", FieldPaths(page));
    }

    [Fact]
    public void ANonSnakeCaseKey_IsRefusedInTheDesk_AndNothingChanges()
    {
        var page = RenderEditor();
        OpenSample(page, "intake");
        var before = FieldPaths(page);

        Derive(page, "intake", """{ "onLetrozole": true, "age": 65 }""");

        Assert.Contains("Key 'onLetrozole' is not snake_case", page.Find(".editor-warning").TextContent);
        Assert.Equal(before, FieldPaths(page));
        Assert.Empty(page.FindAll(".fields-sample__note"));
    }

    [Fact]
    public void ANonJsonSample_IsRefused()
    {
        var page = RenderEditor();
        OpenSample(page, "intake");

        Derive(page, "intake", "{ not json");

        Assert.Equal("The sample for 'intake' is not JSON.", page.Find(".editor-warning").TextContent.Trim());
    }
}
