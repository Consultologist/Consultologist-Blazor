using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// v11 rung (e) (#564): the editor authors macros, the deliverable's list and
/// signed flag, and the reproducible claim. This file starts with the
/// carriage: the results writer and node writers must know the v11 keys, or
/// the first edit erases what a v11 manifest declares.
/// </summary>
public class TemplatesV11MacrosTests : ClientRenderTestContext
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

    private static IReadOnlyList<string> Refusals(IRenderedComponent<Templates> page) =>
        page.FindAll(".fluent-messagebar-message li").Select(item => item.TextContent.Trim()).ToList();

    private WorkflowPackagePublishRequest? sent;

    private void CapturePublish() =>
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.08.2", "acct-1234567890ab@v2026.08.2"),
                Array.Empty<string>()));

    private Consultologist.PackageFormat.WorkflowPackageValidator.ValidationResult Validated()
    {
        var manifest = JsonSerializer.Deserialize<Consultologist.PackageFormat.WorkflowPackageManifest>(
            sent!.Manifest.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return Consultologist.PackageFormat.WorkflowPackageValidator.Validate(
            manifest, sent.Files.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), new Dictionary<string, string>(StringComparer.Ordinal));
    }

    private static JsonElement Result(WorkflowPackagePublishRequest request) =>
        JsonDocument.Parse(request.Manifest.GetRawText()).RootElement.GetProperty("results")[0];

    private static JsonElement Node(WorkflowPackagePublishRequest request, string id) =>
        JsonDocument.Parse(request.Manifest.GetRawText()).RootElement.GetProperty("nodes").EnumerateArray()
            .Single(node => node.GetProperty("id").GetString() == id);

    private void WithDraft(WorkflowPackageContentResponse package, string payloadJson) =>
        JSInterop.Setup<string?>("localStorage.getItem", $"workflow-editor-draft:{package.Ref}")
            .SetResult(payloadJson);

    // ----- the carriage: nothing the editor touches erases v11 shapes -----

    [Fact]
    public void AResultsEdit_DoesNotErase_TheMacroListOrTheSignature()
    {
        var package = EditorFixtures.V11Macro();
        WithDraft(package, """
            {
              "Version": 14,
              "Results": [ { "Id": "consult_note", "Node": "node:assemble-note", "Label": "Renamed note", "When": "node:scope == in_scope", "Macros": ["disclaimer"], "Signature": true } ]
            }
            """);
        CapturePublish();
        var page = RenderEditor(package);

        Publish(page);

        Assert.NotNull(sent);
        var result = Result(sent!);
        Assert.Equal("Renamed note", result.GetProperty("label").GetString());
        Assert.Equal(new[] { "disclaimer" }, result.GetProperty("macros").EnumerateArray().Select(m => m.GetString()));
        Assert.True(result.GetProperty("signature").GetBoolean());
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join(" | ", validated.Errors));
    }

    [Fact]
    public void ANodeEdit_KeepsTheReproducibleClaim_InPlace()
    {
        var package = EditorFixtures.V11Macro();
        WithDraft(package, """
            {
              "Version": 14,
              "NodeEdits": [ { "NodeId": "scope", "Label": "Renamed scope", "ForEach": null, "OutputSchema": null, "FailIfEmpty": null, "Prompt": "classify", "Values": ["in_scope", "out_of_scope"], "Reproducible": true } ]
            }
            """);
        CapturePublish();
        var page = RenderEditor(package);

        Publish(page);

        var scope = Node(sent!, "scope");
        Assert.Equal("Renamed scope", scope.GetProperty("label").GetString());
        Assert.True(scope.GetProperty("reproducible").GetBoolean());
        Assert.Equal("classifier", scope.GetProperty("kind").GetString());
    }

    [Fact]
    public void TurningReproducibleOff_RemovesTheKey()
    {
        var package = EditorFixtures.V11Macro();
        WithDraft(package, """
            {
              "Version": 14,
              "NodeEdits": [ { "NodeId": "scope", "Label": "Is the referral in scope?", "ForEach": null, "OutputSchema": null, "FailIfEmpty": null, "Prompt": "classify", "Values": ["in_scope", "out_of_scope"], "Reproducible": false } ]
            }
            """);
        CapturePublish();
        var page = RenderEditor(package);

        Publish(page);

        Assert.False(Node(sent!, "scope").TryGetProperty("reproducible", out _));
    }

    [Fact]
    public void TheControl_AV10Edit_WritesTheBytesItAlwaysWrote()
    {
        var package = EditorFixtures.V10Classifier();
        WithDraft(package, """
            {
              "Version": 14,
              "Results": [ { "Id": "consult_note", "Node": "node:assemble-note", "Label": "Renamed note", "When": "node:scope == in_scope" } ],
              "NodeEdits": [ { "NodeId": "scope", "Label": "Renamed scope", "ForEach": null, "OutputSchema": null, "FailIfEmpty": null, "Prompt": "classify", "Values": ["in_scope", "out_of_scope"] } ]
            }
            """);
        CapturePublish();
        var page = RenderEditor(package);

        Publish(page);

        var result = Result(sent!);
        Assert.Equal(new[] { "id", "node", "label", "when" }, result.EnumerateObject().Select(p => p.Name));
        Assert.False(Node(sent!, "scope").TryGetProperty("reproducible", out _));
    }

    [Fact]
    public void ADraftRoundTrip_KeepsTheV11Fields()
    {
        // The persisted draft carries Macros/Signature/Reproducible — a
        // reload restores them into resultsEdit/nodeEdits (the restore
        // mappings under test are the ones the erasure tests publish from).
        var package = EditorFixtures.V11Macro();
        WithDraft(package, """
            {
              "Version": 14,
              "Results": [ { "Id": "consult_note", "Node": "node:assemble-note", "Label": "Renamed note", "When": "node:scope == in_scope", "Macros": ["disclaimer"], "Signature": true } ],
              "NodeEdits": [ { "NodeId": "scope", "Label": "Is the referral in scope?", "ForEach": null, "OutputSchema": null, "FailIfEmpty": null, "Prompt": "classify", "Values": ["in_scope", "out_of_scope"], "Reproducible": false } ]
            }
            """);
        CapturePublish();
        var page = RenderEditor(package);

        // Both drafts restored as pending: the publish reflects both.
        Publish(page);

        Assert.True(Result(sent!).GetProperty("signature").GetBoolean());
        Assert.False(Node(sent!, "scope").TryGetProperty("reproducible", out _));
    }

    // ----- the Macros pane -----

    [Fact]
    public void TheMacrosGroup_OffersAdd_At11AndNotBelow()
    {
        var eleven = RenderEditor(EditorFixtures.V11());
        Assert.Contains(eleven.FindAll("button.editor-nav__item"), b => b.TextContent.Contains("+ Macro"));

        var ten = RenderEditor(EditorFixtures.V10Classifier());
        Assert.DoesNotContain(ten.FindAll("button.editor-nav__item"), b => b.TextContent.Contains("+ Macro"));
        Assert.DoesNotContain("Macros", ten.FindAll(".editor-nav__group").Select(g => g.TextContent.Trim()));
    }

    [Fact]
    public void ADashedId_IsRefused_MacroIdsAreSnakeCase()
    {
        var page = RenderEditor(EditorFixtures.V11());
        Navigate(page, "+ Macro");

        page.Find("fluent-text-field[placeholder='closing_paragraph']").Change("closing-paragraph");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create macro")).Click();

        Assert.Contains("Macro ids are snake_case", page.Markup);
    }

    [Fact]
    public void AddingAMacro_CreatesTheArray_TheDeclaration_AndTheFile()
    {
        // The bare fixture has no macros key at all — the writer creates it.
        var package = EditorFixtures.V11();
        CapturePublish();
        var page = RenderEditor(package);

        Navigate(page, "+ Macro");
        page.Find("fluent-text-field[placeholder='closing_paragraph']").Change("closing_paragraph");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create macro")).Click();
        // Landed on the new macro's pane; write its text.
        page.Find("fluent-text-area").Change("Thank you for this referral.");
        Publish(page);

        Assert.NotNull(sent);
        var declared = JsonDocument.Parse(sent!.Manifest.GetRawText()).RootElement.GetProperty("macros")[0];
        Assert.Equal("closing_paragraph", declared.GetProperty("id").GetString());
        Assert.Equal("Closing paragraph", declared.GetProperty("label").GetString());
        Assert.Equal("macros/closing_paragraph.md", declared.GetProperty("file").GetString());
        Assert.Equal("Thank you for this referral.", sent.Files["macros/closing_paragraph.md"]);
    }

    [Fact]
    public void ADeclaredMacrosText_EditsLikeAnyFile()
    {
        var package = EditorFixtures.V11Macro();
        CapturePublish();
        var page = RenderEditor(package);

        Navigate(page, "disclaimer");
        page.Find("fluent-text-area").Change("Rewritten disclaimer, no placeholders.");
        Publish(page);

        Assert.Equal("Rewritten disclaimer, no placeholders.", sent!.Files["macros/disclaimer.md"]);
    }

    [Fact]
    public void TheHelpPanel_ListsOneTokenOfEachSense()
    {
        var page = RenderEditor(EditorFixtures.V11Macro());
        Navigate(page, "disclaimer");

        var help = page.Find(".macro-placeholders").TextContent;
        Assert.Contains("{{input:consult_draft}}", help);
        Assert.Contains("{{data:intro}}", help);
        Assert.Contains("{{classification:scope}}", help);
        Assert.Contains("{{run:date}}", help);
        Assert.Contains("{{profile:name}}", help);
        // consult_draft is required — no optional annotation anywhere here.
        Assert.DoesNotContain("optional — renders as empty", help);
    }

    [Fact]
    public void AnOptionalInput_IsAnnotated_InTheHelp()
    {
        var page = RenderEditor(EditorFixtures.V11Macros_WithOptionalInput());
        Navigate(page, "disclaimer");

        Assert.Contains("(optional — renders as empty when not supplied)", page.Find(".macro-placeholders").TextContent);
    }

    [Fact]
    public void RemovingAPendingMacro_ClearsIt()
    {
        var page = RenderEditor(EditorFixtures.V11());
        Navigate(page, "+ Macro");
        page.Find("fluent-text-field[placeholder='closing_paragraph']").Change("closing_paragraph");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create macro")).Click();
        Assert.Contains(page.FindAll("button.editor-nav__item"), b => b.TextContent.Contains("closing_paragraph"));

        page.Find(".editor-nav__restore").Click();

        Assert.DoesNotContain(page.FindAll("button.editor-nav__item"), b => b.TextContent.Contains("closing_paragraph"));
    }

    // ----- the Documents pane: macro list + signed toggle -----

    [Fact]
    public void TheDocumentsRow_IsOffered_At11AndNotBelow()
    {
        var eleven = RenderEditor(EditorFixtures.V11Macro());
        Navigate(eleven, "Documents");
        Assert.NotEmpty(eleven.FindAll(".declared-row__v11"));

        var ten = RenderEditor(EditorFixtures.V10Classifier());
        Navigate(ten, "Documents");
        Assert.Empty(ten.FindAll(".declared-row__v11"));
    }

    [Fact]
    public void SigningADocument_PublishesTrue_AndValidates()
    {
        var package = EditorFixtures.V11();
        CapturePublish();
        var page = RenderEditor(package);
        Navigate(page, "Documents");

        page.Find(".result-signed input[type=checkbox]").Change(true);
        Publish(page);

        Assert.True(Result(sent!).GetProperty("signature").GetBoolean());
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join(" | ", validated.Errors));
    }

    [Fact]
    public void Unchecking_ReturnsToAbsence_NeverFalse()
    {
        var package = EditorFixtures.V11Macro();
        CapturePublish();
        var page = RenderEditor(package);
        Navigate(page, "Documents");

        page.Find(".result-signed input[type=checkbox]").Change(false);
        Publish(page);

        var result = Result(sent!);
        Assert.False(result.TryGetProperty("signature", out _));
        // The macro list is untouched by the signature toggle.
        Assert.Equal(new[] { "disclaimer" }, result.GetProperty("macros").EnumerateArray().Select(m => m.GetString()));
    }

    [Fact]
    public void ACarriedFalse_SurvivesOtherEdits_AsRead()
    {
        // Presence, not truth, is refused below 11 — an authored false is
        // carried as read and written back, never silently promoted or
        // dropped by an unrelated edit.
        var package = EditorFixtures.V11Macro();
        var root = System.Text.Json.Nodes.JsonNode.Parse(package.Manifest.GetRawText())!.AsObject();
        root["results"]![0]!["signature"] = false;
        package = package with { Manifest = JsonDocument.Parse(root.ToJsonString()).RootElement.Clone() };
        CapturePublish();
        var page = RenderEditor(package);
        Navigate(page, "Documents");

        page.Find("input[aria-label='Label for document consult_note']").Change("Renamed note");
        Publish(page);

        Assert.False(Result(sent!).GetProperty("signature").GetBoolean());
    }

    [Fact]
    public void TheFullPath_AddReferenceReorder_PublishesTheOrderedList_Validated()
    {
        var package = EditorFixtures.V11Macro();
        CapturePublish();
        var page = RenderEditor(package);

        // A second macro, born in the pane…
        Navigate(page, "+ Macro");
        page.Find("fluent-text-field[placeholder='closing_paragraph']").Change("closing_paragraph");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create macro")).Click();
        page.Find("fluent-text-area").Change("Thank you for this referral.");

        // …referenced on the deliverable, then moved first.
        Navigate(page, "Documents");
        page.Find(".result-macro-append").Change("closing_paragraph");
        page.FindAll(".result-macro-up")[1].Click();
        Publish(page);

        var result = Result(sent!);
        Assert.Equal(new[] { "closing_paragraph", "disclaimer" },
            result.GetProperty("macros").EnumerateArray().Select(m => m.GetString()));
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join(" | ", validated.Errors));
    }

    [Fact]
    public void RemovingTheLastMacro_OmitsTheKey()
    {
        var package = EditorFixtures.V11Macro();
        CapturePublish();
        var page = RenderEditor(package);
        Navigate(page, "Documents");

        page.Find(".result-macro-remove").Click();
        Publish(page);

        Assert.False(Result(sent!).TryGetProperty("macros", out _));
    }
}
