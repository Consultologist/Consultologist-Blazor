using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// B5 #1004 → #1044 (design §§ 6–7): a node's output composed at completion —
/// the reply already rendered (its slot reads are the render's trace), the
/// before/after instances around it, attribution in output order.
/// </summary>
public class WorkflowNodeCompletionTests
{
    private static readonly IReadOnlyList<string> NoSlots = Array.Empty<string>();

    [Fact]
    public void NothingToCompose_ReturnsTheReplyBytes()
    {
        var result = WorkflowNodeCompletion.Compose("The plan.\n", Array.Empty<WorkflowNodeCompletion.Instance>(), NoSlots);

        Assert.Equal("The plan.\n", result.Text);
        Assert.Empty(result.Appended);
    }

    [Fact]
    public void Instances_ComposeAroundTheReply_InTheirOrder()
    {
        // The registry's worked example (hash-definitions.md § 4, definition 6):
        // a reply with one after-instance.
        var result = WorkflowNodeCompletion.Compose(
            "Hello",
            new[] { new WorkflowNodeCompletion.Instance("closing", After: true, "Bye") },
            NoSlots);

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
            NoSlots);

        Assert.Equal("First.\n\nSecond.\n\nThe plan.\n\nLast.", result.Text);
        Assert.Equal(new[] { "macro:b1", "macro:b2", "macro:a1" }, result.Appended.Select(p => $"{p.Kind}:{p.Id}"));
    }

    [Fact]
    public void TheReplysSlotReads_AreAttributedBetweenTheInstances_InRenderOrder()
    {
        // #1044: the reply came in rendered; its trace says which macro texts it
        // read, in order — once per read, as the record names them.
        var result = WorkflowNodeCompletion.Compose(
            "Plan. Letrozole counselling. End. Closing.",
            new[] { new WorkflowNodeCompletion.Instance("before", After: false, "B."), new WorkflowNodeCompletion.Instance("after", After: true, "A.") },
            new[] { "letrozole", "closing" });

        Assert.Equal("B.\n\nPlan. Letrozole counselling. End. Closing.\n\nA.", result.Text);
        Assert.Equal(new[] { "macro:before", "slot:letrozole", "slot:closing", "macro:after" }, result.Appended.Select(p => $"{p.Kind}:{p.Id}"));
    }

    [Fact]
    public void TheSameReadTwice_IsAttributedTwice_WithNoInstance()
    {
        var result = WorkflowNodeCompletion.Compose("X and X", Array.Empty<WorkflowNodeCompletion.Instance>(), new[] { "x", "x" });

        Assert.Equal("X and X", result.Text);
        Assert.Equal(new[] { "slot:x", "slot:x" }, result.Appended.Select(p => $"{p.Kind}:{p.Id}"));
    }
}
