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
    public void NullEmptyNonNumericAndSubRowIds_DoNotThrow_AndOrderConsistently()
    {
        var comparer = RowKeyOrder31.Instance;
        Assert.Equal(0, comparer.Compare(null, null));
        Assert.True(comparer.Compare(null, "") < 0);
        Assert.True(comparer.Compare("", null) > 0);
        Assert.True(comparer.Compare("", "node.a") < 0);
        Assert.Equal(0, comparer.Compare("", ""));

        string?[] keys =
        [
            "node.a:x/2", "node.a:x/10", "node.a:2/1/render:1", "node.a:2/1", "node.a#x", "node.a:", "", null,
            "node.standards.assessment:7/if:macro.letrozole_prescription_text", "node.a:2/1/render:10", "node.a:2/1/render:2",
        ];

        var sorted = keys.OrderBy(key => key, comparer).ToList();
        Assert.Equal(sorted, keys.Reverse().OrderBy(key => key, comparer).ToList());
        Assert.Equal([null, ""], sorted.Take(2));
        Assert.True(comparer.Compare("node.a:x/2", "node.a:x/10") < 0);
        Assert.True(comparer.Compare("node.a:2/1/render:2", "node.a:2/1/render:10") < 0);
        Assert.True(comparer.Compare("node.a:2/1", "node.a:2/1/render:1") < 0);

        // antisymmetric over every pair
        foreach (var x in keys)
        {
            foreach (var y in keys)
            {
                Assert.Equal(Math.Sign(comparer.Compare(x, y)), -Math.Sign(comparer.Compare(y, x)));
            }
        }
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
