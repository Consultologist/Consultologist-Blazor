using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// #1039 (design § 16): at 25 a hand-written when the builder's tree cannot hold is
/// shown verbatim beside an edit-as-text door; what the author types is written as
/// typed and judged by the registry at the desk — never rewritten. Below 25 the
/// verbatim branch is unchanged (code + clear, no input). The builder's operand
/// options name the 25 spelling while their values stay the tree's keys.
/// </summary>
public class TemplatesV25ConditionTextTests : ClientRenderTestContext
{
    private const string Condition = "[aria-label='Condition for consult']";
    private const string ConditionText = "input[aria-label='Condition text for consult']";

    /// <summary>The deliverable's own operand pickers — not its macro's ("… for consult macro closing …").</summary>
    private static IReadOnlyList<AngleSharp.Dom.IElement> OperandSelects(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-select[aria-label^='Condition operand for consult']")
            .Where(select => !select.GetAttribute("aria-label")!.Contains(" macro ", StringComparison.Ordinal))
            .ToList();

    private IRenderedComponent<Templates> RenderEditor(string? deliverableWhen)
    {
        var package = EditorFixtures.V25();
        var root = System.Text.Json.Nodes.JsonNode.Parse(package.Manifest.GetRawText())!.AsObject();
        if (deliverableWhen is not null)
        {
            root["deliverables"]!.AsArray().First()!.AsObject()["when"] = deliverableWhen;
        }

        package = package with { Manifest = JsonDocument.Parse(root.ToJsonString()).RootElement.Clone() };
        WorkflowService.GetCurrentPackageContentAsync().Returns(package);
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        var page = Render<Templates>();
        page.FindAll("button.editor-nav__item").First(b => b.TextContent.Replace("●", string.Empty).Trim() == "Deliverables").Click();
        return page;
    }

    private WorkflowPackagePublishRequest? sent;

    private void CapturePublish() =>
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.10.2", "acct-1234567890ab@v2026.10.2"),
                Array.Empty<string>()));

    private static void Publish(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

    private static IReadOnlyList<string> Refusals(IRenderedComponent<Templates> page) =>
        page.FindAll(".fluent-messagebar-message li").Select(item => item.TextContent.Trim()).ToList();

    private static string? When(WorkflowPackagePublishRequest request) =>
        JsonDocument.Parse(request.Manifest.GetRawText()).RootElement.GetProperty("deliverables").EnumerateArray().First()
            .TryGetProperty("when", out var when) ? when.GetString() : null;

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

    [Fact]
    public void AHandWrittenWhen_IsVerbatim_WithTheTextDoor_AndPublishesAsTyped()
    {
        var page = RenderEditor("node.assessment.text != \"\"");
        CapturePublish();

        Assert.Equal("node.assessment.text != \"\"", page.Find(Condition).TextContent);
        Assert.Equal("node.assessment.text != \"\"", page.Find(ConditionText).GetAttribute("value"));
        Assert.Empty(OperandSelects(page));

        page.Find(ConditionText).Change("node.assessment.text != \"\" && input.kind == \"new\"");
        Publish(page);

        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        Assert.Equal("node.assessment.text != \"\" && input.kind == \"new\"", When(sent!));
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join("\n", validated.Errors));
    }

    [Fact]
    public void ATypedWhen_TheRegistryRefuses_IsRefusedAtTheDesk_InItsWords()
    {
        var page = RenderEditor("node.assessment.text != \"\"");
        CapturePublish();

        page.Find(ConditionText).Change("input.notes");
        Publish(page);

        Assert.Contains("Deliverable 'consult' condition tests input.notes for truth; a text cannot be tested — compare it to a value.", Refusals(page));
        Assert.Null(sent);
    }

    [Fact]
    public void ATypedWhen_InThe24Grammar_GetsTheSpelledHint()
    {
        var page = RenderEditor("node.assessment.text != \"\"");
        CapturePublish();

        page.Find(ConditionText).Change("node:scope == in_scope");
        Publish(page);

        Assert.Contains(
            "Deliverable 'consult' condition 'node:scope == in_scope' is the ≤24 condition grammar; at specVersion 25 a when is a Scriban expression over the objects a template reads — write node.scope.value == \"in_scope\".",
            Refusals(page));
        Assert.Null(sent);
    }

    [Fact]
    public void ClearingAVerbatimWhen_StillWorks_AndABlankTextClears()
    {
        var page = RenderEditor("node.assessment.text != \"\"");
        CapturePublish();

        page.Find("[aria-label='Clear condition for consult']").Click();
        Assert.Empty(page.FindAll(ConditionText));
        Publish(page);
        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        Assert.Null(When(sent!));

        sent = null;
        page = RenderEditor("node.assessment.text != \"\"");
        CapturePublish();
        page.Find(ConditionText).Change("   ");
        Assert.Empty(page.FindAll(ConditionText));
        Publish(page);
        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        Assert.Null(When(sent!));
    }

    [Fact]
    public void TheBuilder_NamesOperands_InThe25Spelling_WithTheTreesKeysAsValues()
    {
        var page = RenderEditor(null);
        OpenCondition(page);

        var options = OperandSelects(page).Single().QuerySelectorAll("fluent-option")
            .Select(o => (Value: o.GetAttribute("value"), Text: o.TextContent.Trim()))
            .ToList();
        Assert.Contains(("kind", "when input.kind"), options);
        Assert.Contains(("node:scope", "when node.scope.value"), options);
        Assert.Contains(("intake.on_letrozole", "when input.intake.on_letrozole"), options);
    }

    [Fact]
    public void Below25_TheVerbatimBranch_HasNoTextDoor()
    {
        var package = EditorFixtures.V10Classifier();
        var root = System.Text.Json.Nodes.JsonNode.Parse(package.Manifest.GetRawText())!.AsObject();
        root["results"]!.AsArray().First()!.AsObject()["when"] = "not not node:scope == in_scope";
        package = package with { Manifest = JsonDocument.Parse(root.ToJsonString()).RootElement.Clone() };
        WorkflowService.GetCurrentPackageContentAsync().Returns(package);
        var page = Render<Templates>();
        page.FindAll("button.editor-nav__item").First(b => b.TextContent.Replace("●", string.Empty).Trim() == "Deliverables").Click();

        Assert.Equal("not not node:scope == in_scope", page.Find("[aria-label='Condition for consult_note']").TextContent);
        Assert.Empty(page.FindAll("input[aria-label^='Condition text for']"));
    }
}
