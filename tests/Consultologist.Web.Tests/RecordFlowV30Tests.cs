using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Provenance;

namespace Consultologist.Web.Tests;

/// <summary>
/// v30 (#1345): the record's flow reading at 30 — a while is a repeat as a for is, the
/// repeat a node runs under is the nearest one through if/case rows, an if anywhere under
/// a for or a while is inner, a let is read by its operation, and instance rows come in run
/// order (2 before 10).
/// </summary>
public class RecordFlowV30Tests
{
    private static readonly ConsultGenerationNodeDescriptor[] Nodes =
    {
        new("parts", "Parts", Kind: "for", Collection: "node.plan.value.sections", Body: new[] { "refine" }),
        new("refine", "Refining", Kind: "while", Expression: "true", Body: new[] { "gate" }, In: "parts", Max: 3),
        new("gate", "Gate", Kind: "if", Expression: "pass.number > 1", Body: new[] { "inner" }, In: "refine"),
        new("inner", "Inner", Kind: "if", Expression: "true", Body: new[] { "draft" }, In: "gate"),
        new("draft", "Draft", In: "inner"),
        new("top_if", "Top", Kind: "if", Expression: "true", Body: new[] { "x" }),
        new("count", "Count", Producer: "computed", Op: "let", LetType: "number"),
    };

    private static ConsultGenerationNodeDescriptor N(string id) => Nodes.Single(n => n.Id == id);

    [Fact]
    public void AWhile_IsARepeat_AndTheNearestRepeat_IsFoundThroughIfs()
    {
        Assert.True(RecordFlow.IsWhile(N("refine")) && RecordFlow.IsRepeat(N("refine")) && RecordFlow.IsRepeat(N("parts")));
        Assert.Equal("refine", RecordFlow.RepeatOf(N("draft"), Nodes)?.Id);
        Assert.Equal("parts", RecordFlow.RepeatOf(N("refine"), Nodes)?.Id);
        Assert.Null(RecordFlow.RepeatOf(N("parts"), Nodes));
        // LoopOf stays the nearest for — a collection's reader — and does not reach through a while.
        Assert.Null(RecordFlow.LoopOf(N("draft"), Nodes));
    }

    [Fact]
    public void AnIfAnywhereUnderARepeat_IsInner_AndATopLevelIfIsNot()
    {
        Assert.True(RecordFlow.IsInner(N("gate"), Nodes));
        Assert.True(RecordFlow.IsInner(N("inner"), Nodes));
        Assert.False(RecordFlow.IsInner(N("top_if"), Nodes));
    }

    [Fact]
    public void ALet_IsReadByItsOperation() =>
        Assert.True(RecordFlow.IsLet(N("count")) && !RecordFlow.IsLet(N("draft")));

    [Fact]
    public void InstanceRows_ComeInRunOrder_NumbersAsNumbers()
    {
        var at = DateTimeOffset.UnixEpoch;
        var rows = new[] { "10", "2", "1/10", "1/2", "1" }.Select(path => new ConsultLoopInstanceRow("refine", path, 1, at))
            .Append(new ConsultLoopInstanceRow("other", "3", 1, at)).ToList();

        Assert.Equal(new[] { "1", "1/2", "1/10", "2", "10" }, RecordFlow.InstancesOf(N("refine"), rows).Select(r => r.Path));
    }
}
