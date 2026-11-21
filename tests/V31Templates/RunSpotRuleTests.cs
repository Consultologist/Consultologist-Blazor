using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Templates;
using static Consultologist.Api.Tests.V31Templates.TemplateFixture;

namespace Consultologist.Api.Tests.V31Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 4; D4): an output reading <c>run.*</c>, or a value declared run-dependent
/// whose producer is not an agent, is a run spot, numbered in source order, each with the warning S05-060; one that reads
/// anything else too is refused (S05-024, S05-025), and such a read cannot feed a <c>for</c> (S05-006) or an <c>if</c> (S05-007).
/// </summary>
public class RunSpotRuleTests
{
    private static Dictionary<string, byte[]> Today() =>
        Package().WithComputed("today", "\"date\"", "{ \"var\": \"run.date\" }", runDependent: true);

    [Fact]
    public void ARunFactPipedToABuiltIn_IsOneSpot()
    {
        var check = Check(Package().WithDeliverable("Seen on {{ run.date | date.to_string \"%d %B %Y\" }}.\n"));

        var body = Body(check, "deliverables/note.md");
        Assert.Equal([new TemplateRunSpot(1, "1:9", "run.date")], body.RunSpots);
        var warning = Assert.Single(check.Warnings);
        Assert.Equal("S05-060", warning.Id);
        Assert.Equal("Deliverable 'note' reads run.date at line 1. Its main hash differs between runs; runs are compared on the run-neutral hash.", warning.Sentence);
        Assert.Equal("deliverables/note.md", warning.Path);
        Consultologist.Api.Tests.V31Source.V31Contract.AssertSentence(warning);
    }

    [Fact]
    public void TwoRunFacts_AreTwoSpots_InSourceOrder()
    {
        var check = Check(Package().WithAgent("draft", "{{ run.date }} {{ run.id }}\n"));

        var body = Body(check, "nodes/draft.md");
        Assert.Equal([new TemplateRunSpot(1, "1:1", "run.date"), new TemplateRunSpot(2, "1:16", "run.id")], body.RunSpots);
        Assert.Equal(2, check.Warnings.Count);
    }

    [Fact]
    public void ARunDependentComputedValue_IsASpot()
    {
        var check = Check(Today().WithAgent("draft", "Today:\n{{ node.today.value }}\n"));

        var body = Body(check, "nodes/draft.md");
        Assert.Equal([new TemplateRunSpot(1, "2:1", "node.today.value")], body.RunSpots);
        Assert.Equal("Node 'draft' reads node.today.value at line 2. Its main hash differs between runs; runs are compared on the run-neutral hash.", Assert.Single(check.Warnings).Sentence);
    }

    [Fact]
    public void ARunDependentAgent_IsNoSpot()
    {
        var files = Package().WithAgent("dated", "Today is {{ run.date }}.\n").RunDependent("nodes/dated.md")
            .WithAgent("draft", "{{ node.dated.content }}\n");

        var body = Body(Check(files), "nodes/draft.md");

        Assert.Empty(body.RunSpots);
    }

    [Fact]
    public void AWhileReturningARunDependentAgent_IsNoSpot()
    {
        var files = Package().WithWhile("refine", "dated", "Today is {{ run.date }}.\n")
            .RunDependent("nodes/refine/dated.md").RunDependent("nodes/refine/index.json")
            .WithAgent("draft", "{{ node.refine.content }}\n");

        Assert.Empty(Body(Check(files), "nodes/draft.md").RunSpots);
    }

    [Fact]
    public void ARunFactTestedInAnIf_IsRefused()
    {
        var refusal = Single(Check(Package().WithAgent("draft", "{{ if run.date }}Dated.{{ end }}\n")));

        Assert.Equal("S05-007", refusal.Id);
        Assert.StartsWith("Node 'draft' tests run.date in a body if at line 1;", refusal.Sentence);
    }

    [Fact]
    public void ARunFactWithAnotherRead_IsRefused()
    {
        var refusal = Single(Check(Package().WithAgent("draft", "{{ input.consult_draft.content | string.upcase run.id }}\n")));

        Assert.Equal("S05-024", refusal.Id);
        Assert.Equal("Node 'draft' reads input.consult_draft.content and run.id in one output at line 1; an output that reads a run fact reads nothing else.", refusal.Sentence);
    }

    [Fact]
    public void ARunDependentValueWithAnotherRead_IsRefused()
    {
        var refusal = Single(Check(Today().WithAgent("draft", "{{ input.consult_draft.content | string.upcase node.today.value }}\n")));

        Assert.Equal("S05-025", refusal.Id);
        Assert.Equal("Node 'draft' reads input.consult_draft.content and the run-dependent node.today.value in one output at line 1; an output that reads a run fact reads nothing else.", refusal.Sentence);
    }

    [Fact]
    public void ARunDependentListAsAForsSource_IsRefused()
    {
        var files = Package()
            .WithComputed("days", "{ \"type\": \"array\", \"items\": { \"$ref\": \"date\" } }", "[{ \"var\": \"run.date\" }]", runDependent: true)
            .WithAgent("draft", "{{ for d in node.days.value }}{{ d }}{{ end }}\n");

        var check = Check(files);

        Assert.Contains(check.Refusals, r => r.Id == "S05-006" && r.Sentence == "Node 'draft' runs a for over node.days.value at line 1; a for runs over a list.");
    }
}
