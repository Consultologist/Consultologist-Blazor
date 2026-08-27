using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// v10 step (g), PR 3 (#498): the condition expression builder. One clause is
/// the v9 editor exactly; at 10 clauses join, group and negate, and a number
/// may carry one arithmetic term; what is built publishes through the
/// validator as the format's own spelling; what the tree cannot hold is
/// verbatim; the desk judges each clause.
/// </summary>
public class TemplatesV10ConditionBuilderTests : ClientRenderTestContext
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

    private static string? When(WorkflowPackagePublishRequest request) =>
        JsonDocument.Parse(request.Manifest.GetRawText()).RootElement.GetProperty("results")[0].TryGetProperty("when", out var when) ? when.GetString() : null;

    private void WithDraftedCondition(WorkflowPackageContentResponse package, string when) =>
        JSInterop.Setup<string?>("localStorage.getItem", $"workflow-editor-draft:{package.Ref}")
            .SetResult($$"""
                {
                  "Version": 11,
                  "Results": [ { "Id": "consult_note", "Node": "node:assemble-note", "Label": "Consultation note", "When": "{{when}}" } ]
                }
                """);

    private const string Doc = "consult_note";

    private static string Sel(string control, string suffix = "") => $"[aria-label='Condition {control} for {Doc}{suffix}']";

    private IRenderedComponent<Templates> At10()
    {
        // V10Nested has no classifier: patient-less, so this fixture's operands
        // are the nested package's — family_history (count/bare) and grid.
        // V10Classifier carries patient.age / patient.sex and node:scope.
        var page = RenderEditor(EditorFixtures.V10Classifier());
        CapturePublish();
        Navigate(page, "Documents");
        return page;
    }

    private void PublishAndExpect(IRenderedComponent<Templates> page, string when)
    {
        Publish(page);
        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        Assert.Equal(when, When(sent!));
        var result = Validated();
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    // ----- rendering and gating --------------------------------------------

    [Fact]
    public void Below10_TheRowIsTheV9Editor_WithNoBuilderAffordances()
    {
        var page = RenderEditor(EditorFixtures.V9Conditional());
        Navigate(page, "Documents");

        Assert.NotEmpty(page.FindAll("select[aria-label^='Condition operand for']"));
        Assert.Empty(page.FindAll("[aria-label^='Add condition clause']"));
        Assert.Empty(page.FindAll("[aria-label^='Add condition group']"));
        Assert.Empty(page.FindAll("[aria-label^='Negate condition']"));
        Assert.Empty(page.FindAll("[aria-label^='Condition arithmetic']"));
        Assert.Empty(page.FindAll("[aria-label^='Condition join']"));
    }

    [Fact]
    public void At10_TheSingleClause_KeepsItsLabels_AndGainsTheAffordances()
    {
        var page = At10();

        Assert.Equal("node:scope", page.Find(Sel("operand")).GetAttribute("value"));
        Assert.Equal("==", page.Find(Sel("operator")).GetAttribute("value"));
        Assert.Equal("in_scope", page.Find(Sel("value")).GetAttribute("value"));
        Assert.NotNull(page.Find("[aria-label='Add condition clause to consult_note']"));
        Assert.NotNull(page.Find("[aria-label='Add condition group to consult_note']"));
        Assert.NotNull(page.Find("[aria-label='Negate condition for consult_note']"));
        // One clause: no join, no group controls, no removal.
        Assert.Empty(page.FindAll(Sel("join")));
        Assert.Empty(page.FindAll("[aria-label^='Remove condition clause']"));
    }

    // ----- composition -----------------------------------------------------

    [Fact]
    public async Task ANewClause_StartsEmpty_AndHoldsThePublish()
    {
        var page = At10();
        page.Find("[aria-label='Add condition clause to consult_note']").Click();

        Assert.Equal(string.Empty, page.Find(Sel("operand", " clause 2")).GetAttribute("value"));
        Assert.Contains("choose…", page.Find(Sel("operand", " clause 2")).TextContent);
        Publish(page);
        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        Assert.Contains("Document 'consult_note' condition has a clause with no operand chosen.", Refusals(page));
        Assert.Contains("1 condition in progress", page.Markup);
    }

    [Fact]
    public void TwoClauses_JoinByAnd_ThenByOr()
    {
        var page = At10();
        page.Find("[aria-label='Add condition clause to consult_note']").Click();
        page.Find(Sel("operand", " clause 2")).Change("patient.age");
        page.Find(Sel("operator", " clause 2")).Change(">=");
        page.Find(Sel("value", " clause 2")).Change("65");
        Assert.Equal("and", page.Find(Sel("join")).GetAttribute("value"));

        page.Find(Sel("join")).Change("or");
        PublishAndExpect(page, "node:scope == in_scope or patient.age >= 65");
    }

    [Fact]
    public void ANestedGroup_SpellsItsParentheses()
    {
        var page = At10();
        page.Find("[aria-label='Add condition group to consult_note']").Click();
        page.Find(Sel("operand", " clause 2")).Change("patient.age");
        page.Find(Sel("operator", " clause 2")).Change(">=");
        page.Find(Sel("value", " clause 2")).Change("65");
        page.Find("[aria-label='Add condition clause to consult_note 1']").Click();
        page.Find(Sel("operand", " clause 3")).Change("patient.sex");
        page.Find(Sel("value", " clause 3")).Change("female");
        page.Find(Sel("join", " 1")).Change("or");

        PublishAndExpect(page, "node:scope == in_scope and (patient.age >= 65 or patient.sex == female)");
    }

    [Fact]
    public void Not_OnAClause_AndOnAGroup()
    {
        var page = At10();
        page.Find("[aria-label='Negate condition for consult_note']").Change(true);
        page.Find("[aria-label='Add condition group to consult_note']").Click();
        page.Find(Sel("operand", " clause 2")).Change("patient.sex");
        page.Find(Sel("value", " clause 2")).Change("female");
        page.Find("[aria-label='Add condition clause to consult_note 1']").Click();
        page.Find(Sel("operand", " clause 3")).Change("patient.age");
        page.Find(Sel("operator", " clause 3")).Change("<");
        page.Find(Sel("value", " clause 3")).Change("18");
        page.Find("[aria-label='Negate condition group for consult_note 1']").Change(true);

        PublishAndExpect(page, "not node:scope == in_scope and not (patient.sex == female and patient.age < 18)");
    }

    [Fact]
    public void AnArithmeticTerm_OnANumber_WithAnOperandOrANumber()
    {
        var page = At10();
        page.Find(Sel("operand")).Change("patient.age");
        page.Find(Sel("arithmetic")).Change("+");
        page.Find(Sel("arithmetic value")).Change("5");
        page.Find(Sel("operator")).Change(">");
        page.Find(Sel("value")).Change("70");
        PublishAndExpect(page, "patient.age + 5 > 70");
    }

    [Fact]
    public void AnArithmeticTerm_MayTakeAnotherOperand()
    {
        var page = At10();
        page.Find(Sel("operand")).Change("patient.age");
        page.Find(Sel("arithmetic")).Change("*");
        page.Find(Sel("arithmetic operand")).Change("patient.age");
        page.Find(Sel("operator")).Change(">");
        page.Find(Sel("value")).Change("70");
        Assert.Empty(page.FindAll(Sel("arithmetic value")));
        PublishAndExpect(page, "patient.age * patient.age > 70");
    }

    [Fact]
    public void SwitchingTheOperandToAnEnum_DropsTheArithmetic_AndAnEnumOffersNone()
    {
        var page = At10();
        page.Find(Sel("operand")).Change("patient.age");
        page.Find(Sel("arithmetic")).Change("+");
        page.Find(Sel("arithmetic value")).Change("5");
        page.Find(Sel("operand")).Change("patient.sex");

        Assert.Empty(page.FindAll(Sel("arithmetic")));
        PublishAndExpect(page, "patient.sex == female");
    }

    [Fact]
    public void RemovingClauses_CollapsesGroups_AndTheLastClearsTheCondition()
    {
        var page = At10();
        page.Find("[aria-label='Add condition group to consult_note']").Click();
        page.Find(Sel("operand", " clause 2")).Change("patient.sex");
        page.Find(Sel("value", " clause 2")).Change("female");

        page.Find("[aria-label='Remove condition clause for consult_note clause 2']").Click();
        Assert.Empty(page.FindAll("[data-condition-path='1']"));

        page.Find("[aria-label='Add condition clause to consult_note']").Click();
        page.Find("[aria-label='Remove condition clause for consult_note']").Click();
        Assert.Equal(string.Empty, page.Find(Sel("operand")).GetAttribute("value"));
        Publish(page);
        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        Assert.Null(When(sent!));
    }

    [Fact]
    public async Task ChoosingNothing_ClearsTheSoleClause_AndEmptiesASecond()
    {
        // Explicit initialisation: an empty choice is the null condition on
        // the sole clause and an unchosen clause beside another — never the
        // first operand on offer.
        var page = At10();
        page.Find(Sel("operand")).Change(string.Empty);
        Publish(page);
        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        Assert.Null(When(sent!));

        sent = null;
        page.Find(Sel("operand")).Change("patient.sex");
        page.Find("[aria-label='Add condition clause to consult_note']").Click();
        page.Find(Sel("operand", " clause 2")).Change("patient.age");
        page.Find(Sel("value", " clause 2")).Change("65");
        page.Find(Sel("operand", " clause 2")).Change(string.Empty);
        Assert.Equal(string.Empty, page.Find(Sel("operand", " clause 2")).GetAttribute("value"));
        Publish(page);
        // Still the one publish from above: this one was held.
        await WorkflowService.Received(1).PublishPackageAsync(Arg.Any<WorkflowPackagePublishRequest>());
        Assert.Contains("Document 'consult_note' condition has a clause with no operand chosen.", Refusals(page));
    }

    // ----- loading ---------------------------------------------------------

    [Theory]
    [InlineData("patient.age >= 65 and patient.sex == female and node:scope == in_scope", 3, 0)]
    [InlineData("patient.age >= 65 and (patient.sex == female or node:scope == in_scope)", 3, 1)]
    [InlineData("not (patient.age >= 65 or node:scope == in_scope)", 2, 0)]
    public void ALoadedExpression_ShowsItsClauses_AndPublishesUntouchedAsItself(string when, int clauses, int nestedGroups)
    {
        var package = EditorFixtures.V10Classifier();
        WithDraftedCondition(package, when);
        var page = RenderEditor(package);
        CapturePublish();
        Navigate(page, "Documents");

        Assert.Equal(clauses, page.FindAll("select[aria-label^='Condition operand for consult_note']").Count);
        Assert.Equal(nestedGroups, page.FindAll(".condition-group--nested").Count);
        PublishAndExpect(page, when);
    }

    [Theory]
    [InlineData("not not node:scope == in_scope")]
    [InlineData("patient.age + 1 + 2 > 70")]
    [InlineData("1 + patient.age > 70")]
    public void AnExpressionTheTreeCannotHold_IsVerbatim_AndClearable(string when)
    {
        var package = EditorFixtures.V10Classifier();
        WithDraftedCondition(package, when);
        var page = RenderEditor(package);
        CapturePublish();
        Navigate(page, "Documents");

        Assert.Equal(when, page.Find("[aria-label='Condition for consult_note']").TextContent);
        Assert.Empty(page.FindAll("select[aria-label^='Condition operand for consult_note']"));
        Publish(page);
        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        Assert.Equal(when, When(sent!));

        page.Find("[aria-label='Clear condition for consult_note']").Click();
        Assert.NotEmpty(page.FindAll("select[aria-label^='Condition operand for consult_note']"));
        Publish(page);
        Assert.Null(When(sent!));
    }

    [Fact]
    public async Task Below10_ALoadedExpression_IsVerbatim_WithTheUpgradeSentence()
    {
        var package = EditorFixtures.V9Structured();
        WithDraftedCondition(package, "patient.age >= 65 and count(prior_notes) > 1");
        var page = RenderEditor(package);
        CapturePublish();
        Navigate(page, "Documents");

        // Shown as its two clauses, without the builder's affordances.
        Assert.Equal(2, page.FindAll("select[aria-label^='Condition operand for consult_note']").Count);
        Assert.Empty(page.FindAll("[aria-label^='Add condition clause']"));
        Publish(page);
        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        Assert.Contains("Document 'consult_note' declares a condition expression that requires specVersion 10. Use \"Upgrade to specVersion 10\" and publish.", Refusals(page));
    }

    // ----- the desk --------------------------------------------------------

    [Fact]
    public async Task TheDesk_JudgesEachClause_InTheServersWords()
    {
        var package = EditorFixtures.V10Classifier();
        WithDraftedCondition(package, "patient.age >= 65 and patient.sex == purple and node:scope == elsewhere");
        var page = RenderEditor(package);
        CapturePublish();
        Publish(page);

        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        var refusals = Refusals(page);
        Assert.Contains("Result 'consult_note' condition compares 'patient.sex' to 'purple', which it does not declare.", refusals);
        Assert.Contains("Result 'consult_note' condition compares 'node:scope' to 'elsewhere', which it does not declare (values: in_scope, out_of_scope).", refusals);
        Assert.DoesNotContain(refusals, r => r.Contains("patient.age"));
    }

    [Fact]
    public async Task TheDesk_NamesAParseError_InTheParsersWords()
    {
        var package = EditorFixtures.V10Classifier();
        WithDraftedCondition(package, "patient.age >= 65 and");
        var page = RenderEditor(package);
        CapturePublish();
        Publish(page);

        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        Assert.Contains(Refusals(page), r => r.StartsWith("Result 'consult_note' condition ", StringComparison.Ordinal));
    }

    [Fact]
    public void TheDesk_LeavesArithmeticTyping_ToTheServer()
    {
        var package = EditorFixtures.V10Classifier();
        WithDraftedCondition(package, "patient.sex + 1 > 2");
        var page = RenderEditor(package);
        CapturePublish();
        Publish(page);

        Assert.NotNull(sent);
        Assert.False(Validated().IsValid);
    }

    // ----- the authoring surfaces read any clause --------------------------

    [Fact]
    public void RenamingAnInputOrAField_ReachesEveryClause()
    {
        var package = EditorFixtures.V10Classifier();
        WithDraftedCondition(package, "patient.age >= 65 and (patient.age + 1 > 66 or node:scope == in_scope)");
        var page = RenderEditor(package);
        CapturePublish();
        Navigate(page, "Inputs");
        page.Find("input[aria-label='Id for field patient.age']").Change("years");

        Publish(page);
        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        Assert.Equal("patient.years >= 65 and (patient.years + 1 > 66 or node:scope == in_scope)", When(sent!));
    }

    [Fact]
    public void AFieldReadDeepInAnExpression_IsLocked()
    {
        var package = EditorFixtures.V10Classifier();
        WithDraftedCondition(package, "node:scope == in_scope and (patient.age >= 65 or patient.sex == female)");
        var page = RenderEditor(package);
        Navigate(page, "Inputs");

        Assert.True(page.Find("li.declared-field[data-field='patient.sex'] button[title='Remove field']").HasAttribute("disabled"));
        page.Find("li.declared-field__values[data-values-for='patient.sex'] button[title='Remove value']").Click();
        Assert.Contains("is what 'Consultation note' tests for", page.Find("p.editor-warning").TextContent);
    }
}
