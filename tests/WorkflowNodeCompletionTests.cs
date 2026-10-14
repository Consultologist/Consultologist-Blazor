using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// B5 #1004 (design §§ 6–7): a node's output composed at completion — slots
/// filled where the model placed their markers, before/after instances around
/// the result, attribution in output order, every removal named.
/// </summary>
public class WorkflowNodeCompletionTests
{
    private static readonly IReadOnlyDictionary<string, string> NoFills = new Dictionary<string, string>();

    private static string Unsanctioned(string id) => $"no read in this node's prompt sanctions [[slot:{id}]]";

    [Fact]
    public void NothingToCompose_ReturnsTheReplyBytes()
    {
        var result = WorkflowNodeCompletion.Compose("The plan.\n", Array.Empty<WorkflowNodeCompletion.Instance>(), NoFills, Unsanctioned);

        Assert.Equal("The plan.\n", result.Text);
        Assert.Empty(result.Appended);
        Assert.Empty(result.Removals);
    }

    [Fact]
    public void Instances_ComposeAroundTheReply_InTheirOrder()
    {
        // The registry's worked example (hash-definitions.md § 4, definition 6):
        // a reply with one after-instance.
        var result = WorkflowNodeCompletion.Compose(
            "Hello",
            new[] { new WorkflowNodeCompletion.Instance("closing", After: true, "Bye") },
            NoFills,
            Unsanctioned);

        Assert.Equal("Hello\n\nBye", result.Text);
        Assert.Equal(new[] { new WorkflowNodeCompletion.Placed("macro", "closing") }, result.Appended);
    }

    [Fact]
    public void BeforeBlocks_Reply_AfterBlocks_OneBlankLineBetween_EachBlockTrailingTrimmed()
    {
        var result = WorkflowNodeCompletion.Compose(
            "The plan.",
            new[]
            {
                new WorkflowNodeCompletion.Instance("b1", After: false, "First.\n"),
                new WorkflowNodeCompletion.Instance("a1", After: true, "Last.\n\n"),
                new WorkflowNodeCompletion.Instance("b2", After: false, "Second."),
            },
            NoFills,
            Unsanctioned);

        Assert.Equal("First.\n\nSecond.\n\nThe plan.\n\nLast.", result.Text);
        Assert.Equal(new[] { "macro:b1", "macro:b2", "macro:a1" }, result.Appended.Select(p => $"{p.Kind}:{p.Id}"));
    }

    [Fact]
    public void ASanctionedMarker_IsFilledInPlace_AndAttributedInOutputOrder()
    {
        var result = WorkflowNodeCompletion.Compose(
            "Plan. [[slot:letrozole]] End. [[slot:closing]]",
            new[] { new WorkflowNodeCompletion.Instance("before", After: false, "B."), new WorkflowNodeCompletion.Instance("after", After: true, "A.") },
            new Dictionary<string, string> { ["letrozole"] = "Letrozole counselling.", ["closing"] = "Closing." },
            Unsanctioned);

        Assert.Equal("B.\n\nPlan. Letrozole counselling. End. Closing.\n\nA.", result.Text);
        Assert.Equal(new[] { "macro:before", "slot:letrozole", "slot:closing", "macro:after" }, result.Appended.Select(p => $"{p.Kind}:{p.Id}"));
        Assert.Empty(result.Removals);
    }

    [Fact]
    public void AMarkerNoFillSanctions_IsRemoved_AndNamedWithItsReason()
    {
        var result = WorkflowNodeCompletion.Compose(
            "Plan. [[slot:stray]] End.",
            Array.Empty<WorkflowNodeCompletion.Instance>(),
            NoFills,
            Unsanctioned);

        Assert.Equal("Plan.  End.", result.Text);
        Assert.Empty(result.Appended);
        Assert.Equal(new[] { new WorkflowNodeCompletion.Removed("slot", "stray", "no read in this node's prompt sanctions [[slot:stray]]") }, result.Removals);
    }

    [Fact]
    public void TheSameMarkerTwice_FillsTwice_AndIsAttributedTwice()
    {
        var result = WorkflowNodeCompletion.Compose(
            "[[slot:x]] and [[slot:x]]",
            Array.Empty<WorkflowNodeCompletion.Instance>(),
            new Dictionary<string, string> { ["x"] = "X" },
            Unsanctioned);

        Assert.Equal("X and X", result.Text);
        Assert.Equal(2, result.Appended.Count);
    }
}
