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
