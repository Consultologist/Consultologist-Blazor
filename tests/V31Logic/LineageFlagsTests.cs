using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Logic;

/// <summary>#1501 (part 03 §§ 4.4 to 4.6): the three contagious flags over a graph of reads, and their sentences.</summary>
public class LineageFlagsTests
{
    private static LineageRead Read(string path, string? key = null) => new(path, key);

    private static LineageNode Node(string key, bool model = false, bool profile = false, params LineageRead[] reads) =>
        new(key, model, profile, reads);

    [Fact]
    public void TheSpecsExample()
    {
        var derived = LineageFlags.Derive([
            Node("draft", model: true),
            Node("names_drug", reads: Read("node.draft.content", "draft")),
        ]);

        Assert.Equal(new LineageValue(true, "node.draft.content"), derived["names_drug"][LineageFlag.ModelDerived]);
        Assert.False(derived["names_drug"][LineageFlag.RunDependent].Value);
        Assert.False(derived["names_drug"][LineageFlag.ProfileCarrying].Value);
        Assert.Equal(new LineageValue(true, null), derived["draft"][LineageFlag.ModelDerived]);
    }

    [Fact]
    public void RunDependence_Spreads()
    {
        var derived = LineageFlags.Derive([
            Node("age", reads: [Read("run.date"), Read("input.date_of_birth.value")]),
            Node("banner", reads: Read("node.age.value", "age")),
            Node("plain", reads: Read("input.date_of_birth.value")),
        ]);

        Assert.Equal(new LineageValue(true, "run.date"), derived["age"][LineageFlag.RunDependent]);
        Assert.Equal(new LineageValue(true, "node.age.value"), derived["banner"][LineageFlag.RunDependent]);
        Assert.Equal(new LineageValue(false, null), derived["plain"][LineageFlag.RunDependent]);
    }

    [Fact]
    public void ADecision_DoesNotPassItOn()
    {
        var derived = LineageFlags.Derive([
            Node("age", reads: Read("run.date")),
            Node("gate"),
        ]);
        Assert.False(derived["gate"][LineageFlag.RunDependent].Value);

        var decision = LineageFlags.OfReads([Read("node.age.value", "age")], derived);

        Assert.Equal(new LineageValue(true, "node.age.value"), decision[LineageFlag.RunDependent]);
        Assert.False(decision[LineageFlag.ModelDerived].Value);
    }

    [Fact]
    public void ProfileCarrying_StartsInText()
    {
        var derived = LineageFlags.Derive([
            Node("letterhead", profile: true),
            Node("letter", reads: Read("node.letterhead.content", "letterhead")),
        ]);

        Assert.Equal(new LineageValue(true, null), derived["letterhead"][LineageFlag.ProfileCarrying]);
        Assert.Equal(new LineageValue(true, "node.letterhead.content"), derived["letter"][LineageFlag.ProfileCarrying]);
        Assert.False(derived["letter"][LineageFlag.ModelDerived].Value);
    }

    [Fact]
    public void ACycle_Ends()
    {
        var derived = LineageFlags.Derive([
            Node("a", reads: Read("node.b.value", "b")),
            Node("b", reads: [Read("node.a.value", "a"), Read("run.id")]),
        ]);

        Assert.True(derived["a"][LineageFlag.RunDependent].Value);
        Assert.True(derived["b"][LineageFlag.RunDependent].Value);
        // Because is the first read, in written order, that makes it true once the graph settles: for b that is its read of a.
        Assert.Equal("node.b.value", derived["a"][LineageFlag.RunDependent].Because);
        Assert.Equal("node.a.value", derived["b"][LineageFlag.RunDependent].Because);
    }

    [Fact]
    public void AReadOfAnUnknownKey_IsIgnored()
    {
        var derived = LineageFlags.Derive([Node("a", reads: Read("node.ghost.value", "ghost"))]);

        Assert.All(derived["a"].Values, v => Assert.Equal(new LineageValue(false, null), v));
    }

    [Fact]
    public void Disagreement()
    {
        var a = LineageFlags.Disagreement("names_drug", LineageFlag.ModelDerived, false, new LineageValue(true, "node.draft.content"), "nodes/names_drug.md");
        var b = LineageFlags.Disagreement("names_drug", LineageFlag.RunDependent, true, new LineageValue(false, null), "nodes/names_drug.md");

        Assert.Equal("S03-036", a!.Id);
        Assert.Equal("Node 'names_drug' declares modelDerived false, but its reads make it true: node.draft.content.", a.Sentence);
        Assert.Equal("nodes/names_drug.md", a.Path);
        Assert.Equal("Node 'names_drug' declares runDependent true, but its reads make it false: none.", b!.Sentence);
        V31Contract.AssertSentence(a);
        V31Contract.AssertSentence(b);
        Assert.Null(LineageFlags.Disagreement("n", LineageFlag.ProfileCarrying, true, new LineageValue(true, null), "nodes/n.md"));
    }

    [Fact]
    public void Members()
    {
        Assert.Equal(["modelDerived", "runDependent", "profileCarrying"],
            Enum.GetValues<LineageFlag>().Select(LineageFlags.Member));
    }

    [Fact]
    public void Notes()
    {
        Assert.Equal("computed from model text: node.draft", ExpressionSentences.ModelDerivedNote("draft"));
        V31Contract.AssertSentence(new SourceRefusal("S03-039", ExpressionSentences.ModelDerivedNote("draft"), ""));

        var refusal = ExpressionSentences.ReadsProfileText(LogicFixture.Site(ExpressionSiteKind.NodeIf), "node.letterhead.content");
        Assert.Equal("Node 'gate' reads node.letterhead.content, which carries profile text; prompts, parts and conditions don't read profile text.", refusal.Sentence);
        V31Contract.AssertSentence(refusal);
    }
}
