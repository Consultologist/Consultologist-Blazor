using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1340 (package-format-v30-design.md § 1): the item path helper. One level is
/// today's id, byte for byte; deeper paths are joined by <c>/</c> and every key
/// built on them still splits where today's did.
/// </summary>
public class WorkflowItemPathTests
{
    [Theory]
    [InlineData("hpi")]
    [InlineData("0")]
    [InlineData("12")]
    public void AOneSegmentPath_IsTodaysItemId(string id)
    {
        Assert.Equal(id, WorkflowItemPath.Single(id));
        Assert.Equal(id, WorkflowItemPath.Of(new[] { id }));
        Assert.Equal($"draft:{id}", WorkflowItemPath.Key("draft", WorkflowItemPath.Single(id)));
        Assert.Equal($"note:sections:{id}", WorkflowItemPath.BlockId("note:", "sections", id));
        Assert.Equal(1, WorkflowItemPath.Depth(id));
    }

    [Fact]
    public void ANodeOutsideEveryLoop_IsItsBareId() => Assert.Equal("extract", WorkflowItemPath.Key("extract", null));

    [Theory]
    [InlineData("draft_problem:2", "draft_problem", "2")]
    [InlineData("section_notes:hpi", "section_notes", "hpi")]
    [InlineData("draft_paragraph:hpi/2", "draft_paragraph", "hpi/2")]
    public void AKey_SplitsAtItsFirstColon_AndAgreesWithTodaysLastColonParse(string key, string node, string path)
    {
        Assert.True(WorkflowItemPath.TryParseKey(key, out var parsedNode, out var parsedPath));
        Assert.Equal((node, path), (parsedNode, parsedPath));
        Assert.Equal(node, WorkflowItemPath.NodeOf(key));
        Assert.Equal(path, WorkflowItemPath.PathFor(key, node));
        // Today's parses: last colon (the item-step events), first colon (the activities).
        Assert.Equal(node, key[..key.LastIndexOf(':')]);
        Assert.Equal(node, key.Split(':')[0]);
    }

    [Fact]
    public void ABareNodeId_IsNoItemKey()
    {
        Assert.False(WorkflowItemPath.TryParseKey("extract", out var node, out _));
        Assert.Equal("extract", node);
        Assert.Equal("extract", WorkflowItemPath.NodeOf("extract"));
        Assert.False(WorkflowItemPath.IsItemKey("extract"));
        Assert.True(WorkflowItemPath.IsItemKey("draft:hpi"));
    }

    [Theory]
    [InlineData("draft:hpi", "dra")]
    [InlineData("draft_x:hpi", "draft")]
    [InlineData("draft", "draft")]
    public void PathFor_NamesOnlyItsOwnNodesKeys(string key, string node) => Assert.Null(WorkflowItemPath.PathFor(key, node));

    [Theory]
    [InlineData("note:sections:plan", "plan", "sections:plan")]
    [InlineData("sections:plan", "plan", "sections:plan")]
    [InlineData("note:paragraphs:hpi/2", "hpi/2", "paragraphs:hpi/2")]
    public void ABlockId_NamesItsPathAfterItsLastColon(string blockId, string path, string loopItem)
    {
        Assert.Equal(path, WorkflowItemPath.PathOfBlock(blockId));
        Assert.Equal(loopItem, WorkflowItemPath.LoopItemOfBlock(blockId));
    }

    [Fact]
    public void ABlockNamingNoItem_HasNoLoopItem()
    {
        Assert.Null(WorkflowItemPath.LoopItemOfBlock("plan"));
        Assert.Equal("hpi", WorkflowItemPath.PathOfBlock("hpi"));
    }

    [Theory]
    [InlineData("hpi", 0, null)]
    [InlineData("hpi", 1, "hpi")]
    [InlineData("hpi/2", 1, "hpi")]
    [InlineData("hpi/2", 2, "hpi/2")]
    [InlineData("hpi/2/3", 2, "hpi/2")]
    public void ADependency_IsAlignedOnTheCommonPrefix(string path, int depth, string? aligned) =>
        Assert.Equal(aligned, WorkflowItemPath.Prefix(path, depth));

    [Fact]
    public void AReaderWithNoPath_AlignsNothing() => Assert.Null(WorkflowItemPath.Prefix(null, 1));

    [Fact]
    public void APath_IsItsIdsOutermostFirst()
    {
        Assert.Equal("hpi/2", WorkflowItemPath.Of(new[] { "hpi", "2" }));
        Assert.Equal(new[] { "hpi", "2" }, WorkflowItemPath.Segments("hpi/2"));
        Assert.Equal("2", WorkflowItemPath.Last("hpi/2"));
        Assert.Equal(2, WorkflowItemPath.Depth("hpi/2"));
    }

    [Fact]
    public void AnItemsPath_IsItsIdAtOneLevel()
    {
        Assert.Equal("2", WorkflowItemPath.OfItem(new Dictionary<string, string> { ["id"] = "2", ["name"] = "Problems 2" }));
        Assert.Equal("hpi", WorkflowItemPath.OfItem(new JsonObject { ["id"] = "hpi" }));
        Assert.Null(WorkflowItemPath.OfItem((IReadOnlyDictionary<string, string>?)null));
    }
}
