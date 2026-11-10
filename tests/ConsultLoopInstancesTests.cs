using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>#1341 B2b (v30 design § 2.3): the instances of a nested loop, the depth a node's instances are keyed under, and an outer item's field read as a list.</summary>
public class ConsultLoopInstancesTests
{
    private static IReadOnlyDictionary<string, string> Item(string id) => new Dictionary<string, string> { ["id"] = id, ["name"] = $"Paragraphs {id}" };

    [Fact]
    public void AnInstance_SettlesPerOuterPath_AndItsItemsCarryTheirPaths()
    {
        var instances = new ConsultLoopInstances();
        Assert.False(instances.Has("paragraphs", "hpi"));

        instances.Resolve("paragraphs", "plan", ConsultNodeValueLoops.WithPath(new[] { Item("1") }, "plan"), null);
        instances.Resolve("paragraphs", "hpi", ConsultNodeValueLoops.WithPath(new[] { Item("1"), Item("2") }, "hpi"), null);

        Assert.True(instances.Has("paragraphs", "hpi"));
        Assert.Equal(new[] { "hpi/1", "hpi/2", "plan/1" },
            instances.ItemsIn("paragraphs", new[] { "hpi", "plan" }).Select(item => WorkflowItemPath.OfItem(item)));
        Assert.Equal("2", instances.ItemsIn("paragraphs", new[] { "hpi" })[1]["id"]);
    }

    [Fact]
    public void AFailedInstance_IsSettled_WithNoItems()
    {
        var instances = new ConsultLoopInstances();
        instances.Fail("paragraphs", "hpi", "Paragraphs could not read its list.");

        Assert.True(instances.Has("paragraphs", "hpi"));
        Assert.True(instances.Get("paragraphs", "hpi")!.Failed);
        Assert.Empty(instances.ItemsIn("paragraphs", new[] { "hpi" }));
    }

    [Fact]
    public void ALoopNeverSeen_HasNoItems() => Assert.Empty(new ConsultLoopInstances().ItemsIn("paragraphs", new[] { "hpi" }));

    private static readonly Dictionary<string, ConsultNodeDescriptor> Rows = new(StringComparer.Ordinal)
    {
        ["parts"] = new("parts", "Parts", Kind: "for", Collection: "node.plan.value.sections", Body: new[] { "outline", "paragraphs" }, Ret: "paragraphs"),
        ["outline"] = new("outline", "Outline", ForEach: "node.plan.value.sections", In: "parts"),
        ["paragraphs"] = new("paragraphs", "Paragraphs", ForEach: "node.plan.value.sections", Kind: "for", Collection: "node.outline.value.points", Body: new[] { "draft" }, Ret: "draft", In: "parts"),
        ["draft"] = new("draft", "Draft", ForEach: "node.outline.value.points", In: "paragraphs"),
        ["plan"] = new("plan", "Plan"),
        ["legacy"] = new("legacy", "Legacy", ForEach: "snippet.sections"),
    };

    [Theory]
    [InlineData("plan", 0)]
    [InlineData("parts", 0)]
    [InlineData("outline", 1)]
    [InlineData("paragraphs", 1)]
    [InlineData("draft", 2)]
    [InlineData("legacy", 1)]
    public void ANodesDepth_IsTheLoopsUpItsChain(string id, int depth) =>
        Assert.Equal(depth, ConsultNodeScheduler.LoopDepthOf(Rows[id], Rows));

    [Fact]
    public void AReadAcrossLevels_AlignsOnTheCommonPrefix()
    {
        Assert.Equal("hpi", ConsultNodeScheduler.AlignedPath(Rows["outline"], "hpi/2", Rows));
        Assert.Null(ConsultNodeScheduler.AlignedPath(Rows["plan"], "hpi/2", Rows));
        Assert.Equal("hpi/2", ConsultNodeScheduler.AlignedPath(Rows["draft"], "hpi/2", Rows));
    }

    [Fact]
    public void AnOuterItemsField_IsReadAsAList_WithTheNodeValueSentences()
    {
        var outer = new Dictionary<string, string> { ["id"] = "1", ["value"] = """{ "title": "History", "topics": [ { "topic": "onset" } ] }""", ["valueKind"] = "json" };

        var resolution = ConsultNodeValueLoops.ItemsOfOuterItem(outer, "item.value.topics", "Paragraphs");
        Assert.Null(resolution.Failure);
        Assert.Equal("Paragraphs 1", resolution.Items.Single()["name"]);
        Assert.Contains("onset", resolution.Items.Single()["value"], StringComparison.Ordinal);

        Assert.Equal("Paragraphs could not read its list: item.value.title is not a list.",
            ConsultNodeValueLoops.ItemsOfOuterItem(outer, "item.value.title", "Paragraphs").Failure);
        Assert.Equal("Paragraphs could not read its list: item.value has no field 'missing'.",
            ConsultNodeValueLoops.ItemsOfOuterItem(outer, "item.value.missing", "Paragraphs").Failure);
    }
}
