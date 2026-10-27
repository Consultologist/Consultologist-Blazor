using Consultologist.Api.Jobs;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1154 (package-format-v26-design.md § 1.1, § 1.5): an if or case node
/// decided over the render scope — the body taken and the branch the record
/// names; a case over an input and over a node's value; none when nothing is
/// taken; loud on a subject that is no declared value; a loop never decided.
/// </summary>
public class WorkflowFlowDecisionTests
{
    private static WorkflowFlowNodes.Flow FlowOf(WorkflowPackage package, string id) =>
        ConsultGenerationJobStarter.FlowsOf(package).Flows[id];

    private static ConsultConditionScope ScopeWith(WorkflowPackage package, IReadOnlyDictionary<string, ConsultInputValue> supplied, IReadOnlyDictionary<string, string>? nodeTexts = null) =>
        new(
            V26JobStartTests.Effective(supplied),
            ConsultConditionScope.TypesOf(package),
            nodeTexts ?? new Dictionary<string, string>(StringComparer.Ordinal),
            null,
            new TemplateRunFacts("2026-01-02", "job", package.Ref, "host"),
            new TemplateProfileFacts("Dr Example", string.Empty),
            package);

    [Fact]
    public void AnIf_TakesThen_OrElse_OrNone()
    {
        var withElse = V26JobStartTests.Package(m => V26JobStartTests.WithTriage(m));
        var triage = FlowOf(withElse, "triage");

        var then = ScopeWith(withElse, V26JobStartTests.NewPatient).Decide(triage);
        Assert.Equal(("then", "full_workup", false), (then.Branch, then.Taken!.RetNodeId, then.Skipped));

        var @else = ScopeWith(withElse, V26JobStartTests.FollowUp).Decide(triage);
        Assert.Equal(("else", "brief_update"), (@else.Branch, @else.Taken!.RetNodeId));

        var withoutElse = V26JobStartTests.Package(m => V26JobStartTests.WithTriage(m, withElse: false));
        var none = ScopeWith(withoutElse, V26JobStartTests.FollowUp).Decide(FlowOf(withoutElse, "triage"));
        Assert.Equal(("none", true), (none.Branch, none.Skipped));
        Assert.Null(none.Taken);
    }

    [Fact]
    public void ACase_TakesTheBranchListingTheValue_ElseTheElse_ElseNone()
    {
        var twoBranches = V26JobStartTests.Package(m => V26JobStartTests.WithRoute(m));
        var route = FlowOf(twoBranches, "route");
        Assert.Equal("new", ScopeWith(twoBranches, V26JobStartTests.NewPatient).Decide(route).Branch);
        Assert.Equal(("follow_up", "letter"), ScopeWith(twoBranches, V26JobStartTests.FollowUp).Decide(route) is { } o ? (o.Branch, o.Taken!.RetNodeId) : default);

        var withElse = V26JobStartTests.Package(m => V26JobStartTests.WithRoute(m, withElse: true));
        var held = ScopeWith(withElse, V26JobStartTests.FollowUp).Decide(FlowOf(withElse, "route"));
        Assert.Equal(("else", "hold"), (held.Branch, held.Taken!.RetNodeId));

        // One branch, no else, the other value: none.
        var oneBranch = V26JobStartTests.Package(m => V26JobStartTests.WithRoute(m) with
        {
            Nodes = V26JobStartTests.WithRoute(m).Nodes!.Select(n => n.Id == "route" ? n with { Branches = n.Branches!.Take(1).ToList() } : n).Where(n => n.Id != "letter").ToList(),
        });
        var none = ScopeWith(oneBranch, V26JobStartTests.FollowUp).Decide(FlowOf(oneBranch, "route"));
        Assert.Equal(("none", true), (none.Branch, none.Skipped));
    }

    [Fact]
    public void ACaseOnANodeValue_ReadsTheNodesText_AndABranchListingTwoValuesIsNamedByBoth()
    {
        var package = V26JobStartTests.Package(m => V26JobStartTests.WithRoute(m, onNode: true) with
        {
            Nodes = V26JobStartTests.WithRoute(m, onNode: true).Nodes!
                .Select(n => n.Id == "route" ? n with { Branches = new List<WorkflowCaseBranchSpec> { new(new List<string> { "in_scope", "out_of_scope" }, new List<string> { "plan" }, "node.plan") } } : n)
                .Where(n => n.Id != "letter")
                .ToList(),
        });
        var route = FlowOf(package, "route");

        var outcome = ScopeWith(package, V26JobStartTests.NewPatient, new Dictionary<string, string> { ["scope"] = "out_of_scope" }).Decide(route);

        Assert.Equal(("in_scope, out_of_scope", "plan"), (outcome.Branch, outcome.Taken!.RetNodeId));
    }

    [Fact]
    public void ASubjectThatIsNoDeclaredValue_IsLoud_WithTheSubject()
    {
        var package = V26JobStartTests.Package(m => V26JobStartTests.WithRoute(m));
        var route = FlowOf(package, "route") with { Expression = "(input.notes | string.size)" };

        var ex = Assert.Throws<InvalidOperationException>(() => ScopeWith(package, V26JobStartTests.NewPatient).Decide(route));

        Assert.Equal("Node 'route' case must yield a declared value; it yields a number.", ex.Message);
    }

    [Fact]
    public void ALoop_IsNotDecided()
    {
        var package = V26JobStartTests.Package();

        var ex = Assert.Throws<InvalidOperationException>(() => ScopeWith(package, V26JobStartTests.NewPatient).Decide(FlowOf(package, "sections")));

        Assert.Equal("Loop 'sections' is not decided; a loop runs over its collection.", ex.Message);
    }

    [Fact]
    public void TheBranchLabel_IsTheValuesListed_OrTheBodysWord()
    {
        Assert.Equal("a, b", WorkflowFlowDecision.BranchLabel(new WorkflowFlowNodes.Body("branch 1", new List<string>(), null, new List<string> { "a", "b" })));
        Assert.Equal("else", WorkflowFlowDecision.BranchLabel(new WorkflowFlowNodes.Body("else", new List<string>(), null, null)));
        Assert.Equal("then", WorkflowFlowDecision.BranchLabel(new WorkflowFlowNodes.Body("then", new List<string>(), null, null)));
    }
}
