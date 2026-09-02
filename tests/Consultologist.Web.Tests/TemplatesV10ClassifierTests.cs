using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// v10 step (g), PR 2 (#498): the classifier kind and the node: operand. A
/// classifier is authored at 10 with the values it may answer; a document's
/// condition tests its answer with == or != against one of them; below 10
/// neither is offered and a loaded one is refused by name.
/// </summary>
public class TemplatesV10ClassifierTests : ClientRenderTestContext
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

    private static JsonElement Node(WorkflowPackagePublishRequest request, string id) =>
        JsonDocument.Parse(request.Manifest.GetRawText()).RootElement.GetProperty("nodes").EnumerateArray()
            .Single(node => node.GetProperty("id").GetString() == id);

    private static string? When(WorkflowPackagePublishRequest request) =>
        JsonDocument.Parse(request.Manifest.GetRawText()).RootElement.GetProperty("results")[0].TryGetProperty("when", out var when) ? when.GetString() : null;

    private static IEnumerable<string?> Options(IRenderedComponent<Templates> page, string selector) =>
        page.Find(selector).QuerySelectorAll("option").Select(option => option.GetAttribute("value"));

    private void WithDraftedCondition(WorkflowPackageContentResponse package, string when) =>
        JSInterop.Setup<string?>("localStorage.getItem", $"workflow-editor-draft:{package.Ref}")
            .SetResult($$"""
                {
                  "Version": 11,
                  "Results": [ { "Id": "consult_note", "Node": "node:assemble-note", "Label": "Consultation note", "When": "{{when}}" } ]
                }
                """);

    // ----- the classifier kind ----------------------------------------------

    [Fact]
    public void TheKindPicker_OffersClassifierAt10_AndNotBelow()
    {
        var at10 = RenderEditor(EditorFixtures.V10Nested());
        Navigate(at10, "+ Node");
        Assert.Contains("classifier", Options(at10, ".new-item-fields select"));

        var at9 = RenderEditor(EditorFixtures.V9Structured());
        Navigate(at9, "+ Node");
        Assert.DoesNotContain("classifier", Options(at9, ".new-item-fields select"));
    }

    [Fact]
    public async Task AddingAClassifier_StartsWithNoValues_AndPublishesWithTwo()
    {
        var page = RenderEditor(EditorFixtures.V10Nested());
        CapturePublish();
        Navigate(page, "+ Node");
        page.Find(".new-item-fields select").Change("classifier");
        // No forEach and no output for a classifier.
        Assert.Empty(page.FindAll("select[aria-label='New node forEach']"));
        page.Find("fluent-text-field[placeholder='summarize-guidelines']").Change("scope");
        page.Find("fluent-text-field[placeholder='Summarizing guidelines']").Change("Is it in scope?");
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Add node") || button.TextContent.Contains("Create")).Click();
        // The editor lands on the new prompt's text.
        page.Find("fluent-text-area").Change("Is this referral in scope for the clinic?");

        // Born with none: the desk refuses until two are declared.
        Publish(page);
        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        Assert.Contains("Node 'scope' declares 0 values; a classifier must declare at least two values it may answer.", Refusals(page));

        Navigate(page, "Graph");
        page.Find("input[aria-label='Add a value to node scope']").Change("in_scope");
        Publish(page);
        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        Assert.Contains("Node 'scope' declares 1 value; a classifier must declare at least two values it may answer.", Refusals(page));
        page.Find("input[aria-label='Add a value to node scope']").Change("out_of_scope");
        Assert.Equal(new[] { "in_scope", "out_of_scope" }, page.FindAll("[data-node-value]").Select(chip => chip.GetAttribute("data-node-value")));
        Assert.Contains("· classifier", page.Markup);

        // Bind the prompt's variable so the validator's closure is whole.
        Publish(page);

        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        var scope = Node(sent!, "scope");
        Assert.Equal("classifier", scope.GetProperty("kind").GetString());
        Assert.Equal(new[] { "in_scope", "out_of_scope" }, scope.GetProperty("values").EnumerateArray().Select(v => v.GetString()));
        Assert.False(scope.TryGetProperty("forEach", out _));
        Assert.False(scope.TryGetProperty("output", out _));
        var result = Validated();
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void ALoadedClassifier_ShowsItself_AndItsValuesEdit()
    {
        var page = RenderEditor(EditorFixtures.V10Classifier());
        CapturePublish();
        Navigate(page, "Graph");

        Assert.Contains("· classifier", page.Markup);
        Assert.Equal(new[] { "in_scope", "out_of_scope" }, page.FindAll("[data-node-value]").Select(chip => chip.GetAttribute("data-node-value")));
        // The classifier's own card carries no forEach and no output.
        var card = page.Find("[data-values-for='scope']").Closest(".node-fields")!;
        Assert.Empty(card.QuerySelectorAll("select[aria-label='Node forEach collection']"));
        Assert.Empty(card.QuerySelectorAll("select[aria-label='Node output contract']"));

        page.Find("input[aria-label='Add a value to node scope']").Change("unsure");
        Publish(page);

        Assert.NotNull(sent);
        Assert.Equal(new[] { "in_scope", "out_of_scope", "unsure" }, Node(sent!, "scope").GetProperty("values").EnumerateArray().Select(v => v.GetString()));
        var result = Validated();
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void AValuesEdit_SurvivesALaterLabelEdit()
    {
        // #572: NodeChange.Values was null-means-unchanged and the label
        // emitter omitted it, so a later label edit silently clobbered a
        // pending values edit — both in the chips and in the published
        // manifest. The effective list now rides every emission.
        var page = RenderEditor(EditorFixtures.V10Classifier());
        CapturePublish();
        Navigate(page, "Graph");

        page.Find("input[aria-label='Add a value to node scope']").Change("unsure");
        var card = page.Find("[data-values-for='scope']").Closest(".node-fields")!;
        card.QuerySelector("input[aria-label='Node label']")!.Change("Scope check");

        // The chips already carry all three before publish — the fold half.
        Assert.Equal(new[] { "in_scope", "out_of_scope", "unsure" }, page.FindAll("[data-node-value]").Select(chip => chip.GetAttribute("data-node-value")));

        Publish(page);

        Assert.NotNull(sent);
        var scope = Node(sent!, "scope");
        Assert.Equal("Scope check", scope.GetProperty("label").GetString());
        Assert.Equal(new[] { "in_scope", "out_of_scope", "unsure" }, scope.GetProperty("values").EnumerateArray().Select(v => v.GetString()));
        var result = Validated();
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void AnOlderDraft_WithNullValues_RestoresTheOriginals_NotAnEmptyList()
    {
        // #572's migration hazard: a draft persisted before the fix carries
        // "Values": null for a label-edited classifier. Restoring that as an
        // empty list would be destructive; it must coalesce to the loaded
        // package's own values.
        var package = EditorFixtures.V10Classifier();
        JSInterop.Setup<string?>("localStorage.getItem", $"workflow-editor-draft:{package.Ref}")
            .SetResult("""
                {
                  "Version": 14,
                  "NodeEdits": [ { "NodeId": "scope", "Label": "Scope check", "ForEach": null, "OutputSchema": null, "FailIfEmpty": null, "Prompt": "classify", "Values": null, "Reproducible": false } ]
                }
                """);
        CapturePublish();
        var page = RenderEditor(package);
        Navigate(page, "Graph");

        Assert.Equal(new[] { "in_scope", "out_of_scope" }, page.FindAll("[data-node-value]").Select(chip => chip.GetAttribute("data-node-value")));

        Publish(page);

        Assert.NotNull(sent);
        var scope = Node(sent!, "scope");
        Assert.Equal("Scope check", scope.GetProperty("label").GetString());
        Assert.Equal(new[] { "in_scope", "out_of_scope" }, scope.GetProperty("values").EnumerateArray().Select(v => v.GetString()));
        var result = Validated();
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public async Task Below10_ALoadedClassifier_IsRefusedByName()
    {
        // A v10 draft node onto a v9 package: refused with the rung it needs.
        var package = EditorFixtures.V9Structured();
        JSInterop.Setup<string?>("localStorage.getItem", $"workflow-editor-draft:{package.Ref}")
            .SetResult("""
                {
                  "Version": 11,
                  "AddedNodes": [ { "Id": "scope", "Label": "Scope", "PromptText": "Is it in scope?", "Kind": "classifier", "Values": ["in_scope", "out_of_scope"] } ]
                }
                """);
        var page = RenderEditor(package);
        CapturePublish();
        Publish(page);

        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        Assert.Contains("Node 'scope' is a classifier, which requires specVersion 10. Use \"Upgrade to specVersion 12\" and publish.", Refusals(page));
    }

    [Fact]
    public async Task AClassifierBindingAnythingButAnInputOrAClassifier_IsRefused()
    {
        var page = RenderEditor(EditorFixtures.V10Classifier());
        CapturePublish();
        Navigate(page, "Graph");
        page.Find("select[aria-label='Source for referral']").Change("data:standards");
        Publish(page);

        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        Assert.Contains("Classifier 'scope' binds 'referral' to 'data:standards'; a classifier may read inputs and classifiers only.", Refusals(page));
    }

    // ----- the node: operand ------------------------------------------------

    [Fact]
    public void TheOperandPicker_OffersEachClassifier_WithItsValuesAndTwoOperators()
    {
        var page = RenderEditor(EditorFixtures.V10Classifier());
        Navigate(page, "Documents");

        Assert.Contains("node:scope", Options(page, "select[aria-label='Condition operand for consult_note']"));
        Assert.Equal(new[] { "==", "!=" }, Options(page, "select[aria-label='Condition operator for consult_note']"));
        Assert.Equal(new[] { "in_scope", "out_of_scope" }, Options(page, "select[aria-label='Condition value for consult_note']"));
        Assert.Equal("in_scope", page.Find("select[aria-label='Condition value for consult_note']").GetAttribute("value"));
    }

    [Fact]
    public void AConditionOverAClassifier_ComposesFromThePickers_AndPublishes()
    {
        var page = RenderEditor(EditorFixtures.V10Classifier());
        CapturePublish();
        Navigate(page, "Documents");
        page.Find("select[aria-label='Condition operator for consult_note']").Change("!=");
        page.Find("select[aria-label='Condition value for consult_note']").Change("out_of_scope");
        Publish(page);

        Assert.NotNull(sent);
        Assert.Equal("node:scope != out_of_scope", When(sent!));
        var result = Validated();
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void Below10_NoClassifierIsOffered_AsAnOperand()
    {
        // A classifier drafted onto a v9 package is refused at the desk; it
        // is not offered to a condition either.
        var package = EditorFixtures.V9Structured();
        JSInterop.Setup<string?>("localStorage.getItem", $"workflow-editor-draft:{package.Ref}")
            .SetResult("""
                {
                  "Version": 11,
                  "AddedNodes": [ { "Id": "scope", "Label": "Scope", "PromptText": "Is it in scope?", "Kind": "classifier", "Values": ["in_scope", "out_of_scope"] } ]
                }
                """);
        var page = RenderEditor(package);
        Navigate(page, "Documents");
        Assert.DoesNotContain(Options(page, "select[aria-label='Condition operand for consult_note']"), key => key?.StartsWith("node:") == true);
    }

    [Theory]
    [InlineData("node:scope == elsewhere", "Result 'consult_note' condition compares 'node:scope' to 'elsewhere', which it does not declare (values: in_scope, out_of_scope).")]
    [InlineData("node:scope > in_scope", "Result 'consult_note' condition compares 'node:scope' with >; a classifier's value is compared with == or != only.")]
    [InlineData("node:draft-section == in_scope", "Result 'consult_note' condition reads 'node:draft-section', which is not a classifier (classifiers: scope).")]
    [InlineData("node:scope", "Result 'consult_note' condition 'node:scope' tests a classifier for truth; compare it to one of its values instead.")]
    public async Task ANodeClauseTheServerWouldRefuse_IsRefusedAtTheDeskInItsWords(string when, string sentence)
    {
        var package = EditorFixtures.V10Classifier();
        WithDraftedCondition(package, when);
        var page = RenderEditor(package);
        CapturePublish();
        Publish(page);

        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        Assert.Contains(sentence, Refusals(page));
    }

    [Fact]
    public async Task RemovingAClassifierAConditionReads_IsRefused()
    {
        var page = RenderEditor(EditorFixtures.V10Classifier());
        CapturePublish();
        Navigate(page, "Graph");
        // The cards follow the manifest's order; the classifier is first.
        page.FindAll("button").First(button => button.TextContent.Trim() == "Remove node").Click();
        Publish(page);

        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        Assert.Contains("Document 'consult_note' tests 'node:scope', which is removed; change that document's condition or restore the node.", Refusals(page));
    }
}
