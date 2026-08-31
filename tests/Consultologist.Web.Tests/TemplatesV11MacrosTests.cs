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
}
