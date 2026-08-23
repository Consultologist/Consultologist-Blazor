using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #429, PR B: the editor authors v9 — the three types and an element type,
/// an object's fields, the six operators over paths and counts, and a node
/// fanning an input. The desk refuses each v9 shape on a publish below 9,
/// and at 9 refuses what the server would, in the server's words.
/// </summary>
public class TemplatesV9AuthoringTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse fixture)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture);
        return Render<Templates>();
    }

    private static void Navigate(IRenderedComponent<Templates> page, string label) =>
        page.FindAll("button.editor-nav__item")
            .First(button => button.TextContent.Replace("\u25CF", string.Empty).Trim() == label)
            .Click();

    private static void Publish(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

    /// <summary>The desk's refusals, one per line, tags stripped.</summary>
    private static IReadOnlyList<string> Refusals(IRenderedComponent<Templates> page) =>
        page.FindAll(".fluent-messagebar-message li").Select(item => item.TextContent.Trim()).ToList();

    /// <summary>A condition the editor cannot yet compose, seeded through the draft the way a reload would.</summary>
    private void WithDraftedCondition(WorkflowPackageContentResponse package, string when) =>
        JSInterop.Setup<string?>("localStorage.getItem", $"workflow-editor-draft:{package.Ref}")
            .SetResult($$"""
                {
                  "Version": 11,
                  "Results": [ { "Id": "consult_note", "Node": "node:assemble-note", "Label": "Consultation note", "When": "{{when}}" } ]
                }
                """);

    private static void UpgradeTo(IRenderedComponent<Templates> page, int target) =>
        page.FindAll("fluent-button")
            .First(button => button.TextContent.Contains($"Upgrade to specVersion {target}", StringComparison.Ordinal))
            .Click();

    private void WithPublishAccepted() =>
        WorkflowService.PublishPackageAsync(Arg.Any<WorkflowPackagePublishRequest>())
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.08.2", "acct-1234567890ab@v2026.08.2"),
                Array.Empty<string>()));

    // ----- the inputs editor at 9 -------------------------------------------

    [Fact]
    public async Task DeclaringAnArray_StartsWithNoEntryType_AndIsRefusedUntilChosen()
    {
        var page = RenderEditor(EditorFixtures.V8());
        UpgradeTo(page, 9);
        Navigate(page, "Inputs");
        page.FindAll("select.declared-row__type")[1].Change(WorkflowInputTypes.Array);

        var items = page.Find("select.declared-row__items");
        Assert.Equal("", items.GetAttribute("value"));
        Assert.Equal("entries of…", items.QuerySelector("option")!.TextContent);

        Publish(page);

        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        Assert.Contains("Input 'prior_notes' is an array and must declare what its entries are.", Refusals(page));
    }

    [Fact]
    public void ChoosingObjectEntries_OpensTheFieldsEditor_AndTheShapePublishes()
    {
        var page = RenderEditor(EditorFixtures.V8());
        UpgradeTo(page, 9);
        Navigate(page, "Inputs");
        page.FindAll("select.declared-row__type")[1].Change(WorkflowInputTypes.Array);
        page.Find("select.declared-row__items").Change(WorkflowInputTypes.Object);

        var editor = page.Find("li.declared-row__fields[data-fields-for=prior_notes]");
        Assert.Empty(editor.QuerySelectorAll("li.declared-field"));

        page.Find("input[aria-label='New field id for prior_notes']").Change("name");
        page.FindAll("button").First(button => button.TextContent.Trim() == "+ Field").Click();
        page.Find("input[aria-label='New field id for prior_notes']").Change("kind");
        page.FindAll("button").First(button => button.TextContent.Trim() == "+ Field").Click();
        page.Find("select[aria-label='Type for field prior_notes.kind']").Change(WorkflowInputTypes.Enum);
        page.Find("input[aria-label='Add a value to prior_notes.kind']").Change("clinic");
        page.Find("input[aria-label='Add a value to prior_notes.kind']").Change("ward");
        page.Find("input[aria-label='Required for field prior_notes.kind']").Change(false);

        Assert.Equal(
            new[] { "prior_notes.name", "prior_notes.kind" },
            page.FindAll("li.declared-field").Select(row => row.GetAttribute("data-field")));
        Assert.Equal(new[] { "clinic", "ward" }, page.FindAll("[data-field-enum-value]").Select(chip => chip.GetAttribute("data-field-enum-value")));

        WorkflowPackagePublishRequest? sent = null;
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.08.2", "acct-1234567890ab@v2026.08.2"),
                Array.Empty<string>()));
        Publish(page);

        Assert.NotNull(sent);
        var input = System.Text.Json.JsonDocument.Parse(sent!.Manifest.GetRawText()).RootElement.GetProperty("inputs")[1];
        Assert.Equal("object", input.GetProperty("items").GetString());
        var fields = input.GetProperty("fields").EnumerateArray().ToList();
        Assert.Equal(new[] { "name", "kind" }, fields.Select(f => f.GetProperty("id").GetString()));
        Assert.False(fields[0].TryGetProperty("type", out _));
        Assert.Equal("enum", fields[1].GetProperty("type").GetString());
        Assert.False(fields[1].GetProperty("required").GetBoolean());
        Assert.Equal(new[] { "clinic", "ward" }, fields[1].GetProperty("values").EnumerateArray().Select(v => v.GetString()));

        var manifest = System.Text.Json.JsonSerializer.Deserialize<Consultologist.Api.Workflow.WorkflowPackageManifest>(
            sent.Manifest.GetRawText(), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        var result = Consultologist.Api.Workflow.WorkflowPackageValidator.Validate(
            manifest, sent.Files.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), new Dictionary<string, string>(StringComparer.Ordinal));
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnObjectWithNoFields_IsRefusedAtTheDesk(bool emptied)
    {
        // Reached two ways: retyping to object (fields null → []), and adding
        // a field then removing it (fields []). Both are refused.
        var page = RenderEditor(EditorFixtures.V8());
        UpgradeTo(page, 9);
        Navigate(page, "Inputs");
        page.FindAll("select.declared-row__type")[1].Change(WorkflowInputTypes.Object);

        if (emptied)
        {
            page.Find("input[aria-label='New field id for prior_notes']").Change("age");
            page.FindAll("button").First(button => button.TextContent.Trim() == "+ Field").Click();
            page.Find("button[title='Remove field']").Click();
        }

        Publish(page);

        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        Assert.Contains("Input 'prior_notes' is an object and must declare at least one field.", Refusals(page));
    }

    [Fact]
    public void AnArrayOfEnums_KeepsInputLevelValues()
    {
        var page = RenderEditor(EditorFixtures.V8());
        UpgradeTo(page, 9);
        Navigate(page, "Inputs");
        page.FindAll("select.declared-row__type")[1].Change(WorkflowInputTypes.Array);
        page.Find("select.declared-row__items").Change(WorkflowInputTypes.Enum);
        page.Find("li.declared-row__values input").Change("clinic");
        page.Find("li.declared-row__values input").Change("ward");

        Assert.Equal(new[] { "clinic", "ward" }, page.FindAll("[data-enum-value]").Select(chip => chip.GetAttribute("data-enum-value")));
        Assert.Empty(page.FindAll("li.declared-row__fields"));
    }

    [Fact]
    public void RenamingAField_CarriesThePathCondition()
    {
        var package = EditorFixtures.V9Structured();
        WithDraftedCondition(package, "patient.age >= 65");
        WorkflowPackagePublishRequest? sent = null;
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.08.2", "acct-1234567890ab@v2026.08.2"),
                Array.Empty<string>()));
        var page = RenderEditor(package);
        Navigate(page, "Inputs");

        page.Find("input[aria-label='Id for field patient.age']").Change("years");
        Publish(page);

        Assert.NotNull(sent);
        var results = System.Text.Json.JsonDocument.Parse(sent!.Manifest.GetRawText()).RootElement.GetProperty("results");
        Assert.Equal("patient.years >= 65", results[0].GetProperty("when").GetString());
    }

    [Fact]
    public void AFieldAConditionReads_CannotBeRetypedOrRemoved()
    {
        var package = EditorFixtures.V9Structured();
        WithDraftedCondition(package, "patient.age >= 65");
        var page = RenderEditor(package);
        Navigate(page, "Inputs");

        Assert.True(page.Find("li.declared-field[data-field='patient.age'] button[title='Remove field']").HasAttribute("disabled"));
        page.Find("select[aria-label='Type for field patient.age']").Change(WorkflowInputTypes.Text);

        Assert.Contains("Field 'patient.age' is tested by 'Consultation note'; change that document's condition first.", page.Find("p.editor-warning").TextContent);
        Assert.Equal(WorkflowInputTypes.Number, page.Find("select[aria-label='Type for field patient.age']").GetAttribute("value"));
    }

    [Fact]
    public void TheValueAFieldConditionTestsFor_CannotBeRemoved()
    {
        var package = EditorFixtures.V9Structured();
        WithDraftedCondition(package, "patient.sex == female");
        var page = RenderEditor(package);
        Navigate(page, "Inputs");

        page.Find("[data-field-enum-value='female'] button").Click();

        Assert.Contains("Value 'female' is what 'Consultation note' tests for", page.Find("p.editor-warning").TextContent);
        Assert.Equal(new[] { "female", "male" }, page.FindAll("[data-field-enum-value]").Select(chip => chip.GetAttribute("data-field-enum-value")));
    }

    [Theory]
    [InlineData("count(prior_notes) > 1")]
    [InlineData("prior_notes")]
    public void AnArrayAConditionReads_CannotBeRetyped(string when)
    {
        var package = EditorFixtures.V9Structured();
        WithDraftedCondition(package, when);
        var page = RenderEditor(package);
        Navigate(page, "Inputs");

        page.FindAll("select.declared-row__type")[1].Change(WorkflowInputTypes.Text);

        Assert.Contains("Input 'prior_notes' is tested by 'Consultation note'", page.Find("p.editor-warning").TextContent);
        Assert.Equal(WorkflowInputTypes.Array, page.FindAll("select.declared-row__type")[1].GetAttribute("value"));
    }

    // ----- the gate below 9 ------------------------------------------------

    [Fact]
    public async Task AV9TypeOnAV8Package_IsRefusedAtTheDesk()
    {
        var page = RenderEditor(EditorFixtures.V8());
        Navigate(page, "Inputs");
        page.FindAll("select.declared-row__type")[1].Change(WorkflowInputTypes.Array);

        Publish(page);

        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        Assert.Contains(
            "Input 'prior_notes' declares a type that requires specVersion 9. Use \"Upgrade to specVersion 9\" and publish.",
            Refusals(page));
    }

    [Fact]
    public async Task AV9TypeOnAV7Package_SaysNineAndNotEight()
    {
        // The two gates never both fire for one input: a v9 shape gets v9 advice.
        var page = RenderEditor(EditorFixtures.V7());
        Navigate(page, "Inputs");
        page.FindAll("select.declared-row__type")[1].Change(WorkflowInputTypes.Array);

        Publish(page);

        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        var refusals = Refusals(page);
        Assert.Contains(refusals, text => text.Contains("requires specVersion 9"));
        Assert.DoesNotContain(refusals, text => text.Contains("'prior_notes' declares a type or values"));
    }

    [Fact]
    public async Task AV9ConditionOnAV8Package_IsRefusedAtTheDesk()
    {
        var package = EditorFixtures.V8();
        WithDraftedCondition(package, "count(prior_notes) > 1");
        var page = RenderEditor(package);

        Publish(page);

        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        Assert.Contains(
            "Document 'consult_note' declares a condition form that requires specVersion 9. Use \"Upgrade to specVersion 9\" and publish.",
            Refusals(page));
    }

    // ----- the desk at 9 ---------------------------------------------------

    [Theory]
    [InlineData("count(prior_notes) > x", "Result 'consult_note' condition compares 'count(prior_notes)' to 'x', which is not a whole number.")]
    [InlineData("patient.age >=", "Result 'consult_note' condition compares against nothing; write a value after the operator.")]
    [InlineData("patient.weight > 90", "Result 'consult_note' condition reads field 'weight' of 'patient', which it does not declare.")]
    [InlineData("patient.age", "Result 'consult_note' condition 'patient.age' tests a number for truth; only a boolean or an array can be tested bare.")]
    [InlineData("patient.sex > female", "Result 'consult_note' condition compares 'patient.sex' with >, which is a enum; ordering operators apply to a number or a date.")]
    [InlineData("count(patient) > 1", "Result 'consult_note' condition counts 'patient', which is a object; only an array has a count.")]
    public async Task AConditionTheServerWouldRefuse_IsRefusedAtTheDeskInItsWords(string when, string expected)
    {
        var package = EditorFixtures.V9Structured();
        WithDraftedCondition(package, when);
        var page = RenderEditor(package);

        Publish(page);

        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        Assert.Contains(expected, Refusals(page));
    }

    [Theory]
    [InlineData("patient.age >= 65")]
    [InlineData("count(prior_notes) > 1")]
    [InlineData("prior_notes")]
    [InlineData("patient.sex != male")]
    public async Task ASoundV9Condition_Publishes(string when)
    {
        var package = EditorFixtures.V9Structured();
        WithDraftedCondition(package, when);
        WorkflowService.PublishPackageAsync(Arg.Any<WorkflowPackagePublishRequest>())
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.08.2", "acct-1234567890ab@v2026.08.2"),
                Array.Empty<string>()));
        var page = RenderEditor(package);

        Publish(page);

        await WorkflowService.Received(1).PublishPackageAsync(Arg.Any<WorkflowPackagePublishRequest>());
    }
}
