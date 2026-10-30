using Consultologist.Api.Jobs;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1235 (package-format-v27-design.md § 1.3, § 1.4): the per-item decision —
/// an if/case inside a loop decided over the item in hand (a folder item, an
/// array input's element), the inputs, the earlier body nodes' outputs for the
/// item; the branch the record names, the taken body and its returned node;
/// when none, a reason that is the condition's text and never a value.
/// </summary>
public class DecideFlowItemActivityTests
{
    private static readonly TemplateRunFacts Run = new("2026-10-29", "job00000000", "test/general@v2026.10.1", "api.example.test");
    private static readonly TemplateProfileFacts Profile = new("Dr Example", "Dr Example, FRCPC");

    private static ConsultDecideFlowItemInput Input(
        string flowId, string collection, IReadOnlyDictionary<string, string> item,
        IReadOnlyDictionary<string, string>? nodeTexts = null, IReadOnlyDictionary<string, string>? effective = null) =>
        new("test/general@v2026.10.1", flowId, collection, item, effective ?? V26JobStartTests.Effective(V26JobStartTests.NewPatient),
            nodeTexts ?? new Dictionary<string, string>(StringComparer.Ordinal), null, Run, Profile);

    private static IReadOnlyDictionary<string, string> Folder(string id, string label, string content) =>
        new Dictionary<string, string>(StringComparer.Ordinal) { ["id"] = id, ["label"] = label, ["content"] = content };

    [Fact]
    public void AnIfOverTheItem_TakesThen_Else_OrNone_PerItem()
    {
        var package = V27JobStartTests.Triaged();

        var then = DecideFlowItemActivity.Decide(package, Input("triage", "snippet.sections", Folder("hpi", "HPI", "History.")));
        Assert.Equal(("triage", "hpi", "then", "patient_draft", false), (then.FlowId, then.ItemId, then.Branch, then.Ret, then.Skipped));
        Assert.Equal(new[] { "patient_draft" }, then.Taken);
        Assert.Null(then.Reason);

        var @else = DecideFlowItemActivity.Decide(package, Input("triage", "snippet.sections", Folder("plan", "Plan", "Plan.")));
        Assert.Equal(("else", "table_draft"), (@else.Branch, @else.Ret));
        Assert.Equal(new[] { "table_draft" }, @else.Taken);

        // Without an else the else body's node would be unreachable: drop it from the package.
        var noElse = V27JobStartTests.Package(b =>
        {
            var (m, files) = V27Package.WithTriageInLoop(b);
            var withoutElse = V26Package.WithNode(m, "triage", n => n with { Else = null });
            return (withoutElse with { Nodes = withoutElse.Nodes!.Where(n => n.Id != "table_draft").ToList() }, files);
        });
        var none = DecideFlowItemActivity.Decide(noElse, Input("triage", "snippet.sections", Folder("plan", "Plan", "Plan.")));
        Assert.Equal(("none", null, true), (none.Branch, none.Ret, none.Skipped));
        Assert.Empty(none.Taken);
        // #369: the condition's text in the fixed form, never the value it read.
        Assert.Equal("needs `item.id == \"hpi\"`; it does not hold", none.Reason);
    }

    [Fact]
    public void ACaseOverTheItemsEnumField_TakesTheBranchListingTheValue_OrNoneWithoutAValue()
    {
        var package = V27JobStartTests.Package(b => V27Package.WithRouteInLoop(b));
        static IReadOnlyDictionary<string, string> Topic(string id, string kind) =>
            new Dictionary<string, string>(StringComparer.Ordinal) { ["id"] = id, ["name"] = $"Topics {id}", ["value"] = $$$"""{"title":"T{{{id}}}","kind":"{{{kind}}}"}""" };

        var narrative = DecideFlowItemActivity.Decide(package, Input("route", "input.topics", Topic("0", "narrative")));
        Assert.Equal(("narrative", "plan"), (narrative.Branch, narrative.Ret));
        var table = DecideFlowItemActivity.Decide(package, Input("route", "input.topics", Topic("1", "table")));
        Assert.Equal(("table", "letter"), (table.Branch, table.Ret));

        var oneBranch = V27JobStartTests.Package(b => V27Package.WithRouteInLoop(b, "item.value.kind",
            new WorkflowCaseBranchSpec(new List<string> { "narrative" }, new List<string> { "plan" }, "node.plan")) is var r
            ? (r.Manifest with { Nodes = r.Manifest.Nodes!.Where(n => n.Id != "letter").ToList() }, r.Files) : b);
        var none = DecideFlowItemActivity.Decide(oneBranch, Input("route", "input.topics", Topic("1", "table")));
        Assert.Equal(("none", true), (none.Branch, none.Skipped));
        Assert.Equal("switches on `item.value.kind`; no branch lists its value", none.Reason);
        Assert.DoesNotContain("table", none.Reason);
    }

    [Fact]
    public void TheCondition_ReadsAnEarlierBodyNodesOutput_ForTheItem()
    {
        var package = V27JobStartTests.Triaged("node.section_notes.content != \"\"");

        var taken = DecideFlowItemActivity.Decide(package, Input("triage", "snippet.sections", Folder("hpi", "HPI", "History."),
            nodeTexts: new Dictionary<string, string> { ["section_notes"] = "Notes for hpi." }));
        Assert.Equal("then", taken.Branch);

        var empty = DecideFlowItemActivity.Decide(package, Input("triage", "snippet.sections", Folder("hpi", "HPI", "History."),
            nodeTexts: new Dictionary<string, string> { ["section_notes"] = "" }));
        Assert.Equal("else", empty.Branch);
    }

    [Fact]
    public void ATopLevelFlowNode_OrALoop_IsRefused()
    {
        var package = V26JobStartTests.Package(m => V26JobStartTests.WithTriage(m));
        var at27 = V27JobStartTests.Triaged();

        Assert.Throws<InvalidOperationException>(() => DecideFlowItemActivity.Decide(at27, Input("sections", "snippet.sections", Folder("hpi", "HPI", "History."))));
        Assert.Contains("runs in no loop", Assert.Throws<InvalidOperationException>(() =>
            DecideFlowItemActivity.Decide(V27JobStartTests.Package(b => (V26JobStartTests.WithTriage(b.Manifest), b.Files)), Input("triage", "snippet.sections", Folder("hpi", "HPI", "History.")))).Message);
        _ = package;
    }
}
