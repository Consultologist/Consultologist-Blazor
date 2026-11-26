using Consultologist.Web.Services.Provenance.V31;

namespace Consultologist.Web.Tests.V31;

/// <summary>
/// Task 12 fix round 1 (#1509): row keys in the order a person counts them. The rooted key compares as text; the item
/// path's segments and the pass numbers after it compare as numbers, so item 10 comes after item 9.
/// </summary>
public class RowKeyOrder31Tests
{
    private static List<string> Sorted(params string[] keys) => keys.OrderBy(key => key, RowKeyOrder31.Instance).ToList();

    [Fact]
    public void ItemPaths_CompareSegmentByNumber()
    {
        Assert.Equal(
            ["node.s.d:1", "node.s.d:2", "node.s.d:2/9", "node.s.d:2/10", "node.s.d:9", "node.s.d:10"],
            Sorted("node.s.d:10", "node.s.d:2/10", "node.s.d:9", "node.s.d:1", "node.s.d:2/9", "node.s.d:2"));
    }

    [Fact]
    public void Passes_CompareByNumber_ThenTheirItems()
    {
        Assert.Equal(
            ["node.refine.judge#2", "node.refine.judge#9", "node.refine.judge#10", "node.refine.sections#2:3", "node.refine.sections#2:10", "node.refine.sections#10:1"],
            Sorted("node.refine.judge#10", "node.refine.sections#10:1", "node.refine.judge#9", "node.refine.sections#2:10", "node.refine.judge#2", "node.refine.sections#2:3"));
    }

    [Fact]
    public void TheRootedKey_ComparesAsText_AndAKeyBeforeItsInstances()
    {
        Assert.Equal(
            ["node.plan", "node.plan:1", "node.planning", "node.sections"],
            Sorted("node.sections", "node.planning", "node.plan:1", "node.plan"));
        Assert.Equal(0, RowKeyOrder31.Instance.Compare("node.a:2", "node.a:2"));
    }
}
