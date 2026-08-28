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
        UpgradeTo(page, 10);
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
        UpgradeTo(page, 10);
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

        var manifest = System.Text.Json.JsonSerializer.Deserialize<Consultologist.PackageFormat.WorkflowPackageManifest>(
            sent.Manifest.GetRawText(), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        var result = Consultologist.PackageFormat.WorkflowPackageValidator.Validate(
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
        UpgradeTo(page, 10);
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
        UpgradeTo(page, 10);
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

    // ----- the results editor at 9 -----------------------------------------

    private static IReadOnlyList<string?> Options(IRenderedComponent<Templates> page, string selector) =>
        page.Find(selector).QuerySelectorAll("option").Select(option => option.GetAttribute("value")).ToList();

    [Fact]
    public void TheOperandPicker_OffersEveryTestableForm()
    {
        // consult_draft is text (never testable); labs.name is text and labs is
        // an array, whose element fields are not read by path — only an
        // object's are. So labs offers its count and its bare form only.
        var page = RenderEditor(EditorFixtures.V9Structured());
        Navigate(page, "Documents");

        // Declaration order: prior_notes, patient, labs.
        Assert.Equal(
            new[] { "", "count(prior_notes)", "prior_notes", "patient.age", "patient.sex", "count(labs)", "labs" },
            Options(page, "select[aria-label='Condition operand for consult_note']"));
    }

    [Fact]
    public void ChoosingANumberPath_OffersOrderingAndADecimalInput()
    {
        var page = RenderEditor(EditorFixtures.V9Structured());
        Navigate(page, "Documents");

        page.Find("select[aria-label='Condition operand for consult_note']").Change("patient.age");

        var operators = page.Find("select[aria-label='Condition operator for consult_note']");
        Assert.Equal(new[] { "==", "!=", ">", "<", ">=", "<=" }, operators.QuerySelectorAll("option").Select(o => o.GetAttribute("value")));
        Assert.Equal(
            new[] { "is", "is not", "is more than", "is less than", "is at least", "is at most" },
            operators.QuerySelectorAll("option").Select(o => o.TextContent));
        Assert.Equal("decimal", page.Find("input[aria-label='Condition value for consult_note']").GetAttribute("inputmode"));
    }

    [Theory]
    [InlineData("patient.age", ">=", "65", "patient.age >= 65")]
    [InlineData("count(prior_notes)", ">", "1", "count(prior_notes) > 1")]
    [InlineData("patient.sex", "!=", "male", "patient.sex != male")]
    [InlineData("prior_notes", null, null, "prior_notes")]
    public async Task AComposedCondition_PublishesThroughTheValidator(string operand, string? op, string? literal, string expected)
    {
        WorkflowPackagePublishRequest? sent = null;
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.08.2", "acct-1234567890ab@v2026.08.2"),
                Array.Empty<string>()));
        var page = RenderEditor(EditorFixtures.V9Structured());
        Navigate(page, "Documents");

        page.Find("select[aria-label='Condition operand for consult_note']").Change(operand);
        if (op != null)
        {
            page.Find("select[aria-label='Condition operator for consult_note']").Change(op);
            page.Find("[aria-label='Condition value for consult_note']").Change(literal!);
        }

        Publish(page);

        await WorkflowService.Received(1).PublishPackageAsync(Arg.Any<WorkflowPackagePublishRequest>());
        var when = System.Text.Json.JsonDocument.Parse(sent!.Manifest.GetRawText()).RootElement.GetProperty("results")[0].GetProperty("when").GetString();
        Assert.Equal(expected, when);

        var manifest = System.Text.Json.JsonSerializer.Deserialize<Consultologist.PackageFormat.WorkflowPackageManifest>(
            sent.Manifest.GetRawText(), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        var result = Consultologist.PackageFormat.WorkflowPackageValidator.Validate(
            manifest, sent.Files.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), new Dictionary<string, string>(StringComparer.Ordinal));
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void ADateInput_OffersADateLiteral()
    {
        var page = RenderEditor(EditorFixtures.V9Structured());
        Navigate(page, "Inputs");
        page.Find("input[aria-label='New input id']").Change("seen_on");
        page.FindAll("button").First(button => button.TextContent.Trim() == "+ Input").Click();
        page.FindAll("select.declared-row__type").Last().Change(WorkflowInputTypes.Date);
        Navigate(page, "Documents");

        page.Find("select[aria-label='Condition operand for consult_note']").Change("seen_on");
        page.Find("select[aria-label='Condition operator for consult_note']").Change(">=");
        page.Find("input[aria-label='Condition value for consult_note']").Change("2026-01-01");

        Assert.Equal("date", page.Find("input[aria-label='Condition value for consult_note']").GetAttribute("type"));
        Assert.Contains("when seen_on", page.Find("select[aria-label='Condition operand for consult_note']").TextContent);
    }

    [Fact]
    public void OnAV8Package_OnlyTheV8FormsAreOffered()
    {
        // A v8 publish can only carry v8 conditions: the picker offers plain
        // enum and boolean ids, and is/is not.
        var page = RenderEditor(EditorFixtures.V8());
        Navigate(page, "Inputs");
        page.FindAll("select.declared-row__type")[1].Change(WorkflowInputTypes.Enum);
        page.Find("li.declared-row__values input").Change("new_patient");
        page.Find("li.declared-row__values input").Change("follow_up");
        Navigate(page, "Documents");

        Assert.Equal(new[] { "", "prior_notes" }, Options(page, "select[aria-label='Condition operand for consult_note']"));
        page.Find("select[aria-label='Condition operand for consult_note']").Change("prior_notes");
        Assert.Equal(new[] { "==", "!=" }, Options(page, "select[aria-label='Condition operator for consult_note']"));
    }

    [Fact]
    public void OnAV8Package_AnObjectsFieldsAreNotOfferedAsOperands()
    {
        // The mutant this pins: an object's paths offered below 9 would let a
        // v8 publish carry a form the engine refuses there.
        var page = RenderEditor(EditorFixtures.V8());
        Navigate(page, "Inputs");
        page.FindAll("select.declared-row__type")[1].Change(WorkflowInputTypes.Object);
        page.Find("input[aria-label='New field id for prior_notes']").Change("kind");
        page.FindAll("button").First(button => button.TextContent.Trim() == "+ Field").Click();
        page.Find("select[aria-label='Type for field prior_notes.kind']").Change(WorkflowInputTypes.Enum);
        page.Find("input[aria-label='Add a value to prior_notes.kind']").Change("clinic");
        page.Find("input[aria-label='Add a value to prior_notes.kind']").Change("ward");
        Navigate(page, "Documents");

        Assert.DoesNotContain("prior_notes.kind", page.Find("li.declared-row__when").TextContent);
        Assert.Empty(page.FindAll("select[aria-label='Condition operand for consult_note']"));
    }

    [Fact]
    public void SwitchingOperand_CoercesTheLiteral()
    {
        var package = EditorFixtures.V9Structured();
        WithDraftedCondition(package, "patient.age >= 65");
        var page = RenderEditor(package);
        Navigate(page, "Documents");

        page.Find("select[aria-label='Condition operand for consult_note']").Change("patient.sex");

        // >= is not an enum's operator and 65 is not one of its values: the
        // condition becomes equality to the first declared value.
        Assert.Equal("==", page.Find("select[aria-label='Condition operator for consult_note']").GetAttribute("value"));
        Assert.Equal("female", page.Find("select[aria-label='Condition value for consult_note']").GetAttribute("value"));
    }

    [Fact]
    public void ALoadedConditionNamingSomethingNotOffered_IsShownAsItself()
    {
        var package = EditorFixtures.V9Structured();
        WithDraftedCondition(package, "patient.family_name == Smith");
        var page = RenderEditor(package);
        Navigate(page, "Documents");

        var picker = page.Find("select[aria-label='Condition operand for consult_note']");
        Assert.Equal("patient.family_name", picker.GetAttribute("value"));
        Assert.Contains("patient.family_name", Options(page, "select[aria-label='Condition operand for consult_note']"));
    }

    // ----- the nodes editor at 9 -------------------------------------------

    private static IReadOnlyList<string?> ForEachOptions(IRenderedComponent<Templates> page) =>
        page.FindAll("select[aria-label='Node forEach collection']").First()
            .QuerySelectorAll("option").Select(option => option.GetAttribute("value")).ToList();

    [Fact]
    public void TheForEachPicker_OffersArrayInputsAtNine()
    {
        var page = RenderEditor(EditorFixtures.V9Structured());
        Navigate(page, "Graph");

        var options = ForEachOptions(page);
        Assert.Contains("input:prior_notes", options);
        Assert.Contains("input:labs", options);
        Assert.DoesNotContain("input:patient", options);
        Assert.Contains("data:standards", options);
    }

    [Fact]
    public void TheForEachPicker_OffersNoInputsBelowNine()
    {
        var page = RenderEditor(EditorFixtures.V8());
        Navigate(page, "Inputs");
        page.FindAll("select.declared-row__type")[1].Change(WorkflowInputTypes.Array);
        Navigate(page, "Graph");

        Assert.DoesNotContain(ForEachOptions(page), option => option!.StartsWith("input:", StringComparison.Ordinal));
    }

    [Fact]
    public void FanningAnInput_OffersItsItemFields()
    {
        var page = RenderEditor(EditorFixtures.V9Structured());
        Navigate(page, "Graph");

        page.FindAll("select[aria-label='Node forEach collection']").First().Change("input:prior_notes");

        var sources = Options(page, "select[aria-label='Source for section_name']");
        Assert.Contains("item:id", sources);
        Assert.Contains("item:name", sources);
        Assert.Contains("item:value", sources);
        Assert.DoesNotContain("item:content", sources);

        page.FindAll("select[aria-label='Node forEach collection']").First().Change("data:standards");

        Assert.Contains("item:content", Options(page, "select[aria-label='Source for section_name']"));
        Assert.DoesNotContain("item:value", Options(page, "select[aria-label='Source for section_name']"));
    }

    [Fact]
    public void TheNewNodeForm_OffersInputFans()
    {
        var page = RenderEditor(EditorFixtures.V9Structured());
        Navigate(page, "+ Node");

        Assert.Contains("input:prior_notes", Options(page, "select[aria-label='New node forEach']"));
    }

    [Fact]
    public async Task ANodeFanningAnInput_PublishesThroughTheValidator()
    {
        WorkflowPackagePublishRequest? sent = null;
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.08.2", "acct-1234567890ab@v2026.08.2"),
                Array.Empty<string>()));
        var page = RenderEditor(EditorFixtures.V9Structured());
        Navigate(page, "Graph");

        page.FindAll("select[aria-label='Node forEach collection']").First().Change("input:prior_notes");
        page.Find("select[aria-label='Source for section_name']").Change("item:value");
        Publish(page);

        await WorkflowService.Received(1).PublishPackageAsync(Arg.Any<WorkflowPackagePublishRequest>());
        var manifest = System.Text.Json.JsonSerializer.Deserialize<Consultologist.PackageFormat.WorkflowPackageManifest>(
            sent!.Manifest.GetRawText(), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.Equal("input:prior_notes", manifest.Nodes![0].ForEach);
        var result = Consultologist.PackageFormat.WorkflowPackageValidator.Validate(
            manifest, sent.Files.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), new Dictionary<string, string>(StringComparer.Ordinal));
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
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
            "Input 'prior_notes' declares a type that requires specVersion 9. Use \"Upgrade to specVersion 10\" and publish.",
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
            "Document 'consult_note' declares a condition form that requires specVersion 9. Use \"Upgrade to specVersion 10\" and publish.",
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
