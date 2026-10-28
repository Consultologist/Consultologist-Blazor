using Consultologist.Web.Pages;

namespace Consultologist.Web.Tests;

/// <summary>
/// #1227: Mermaid numbers links by document order for <c>linkStyle</c>, every arrow
/// counted — the pending-edit decoration must count the dotted edges a predicate,
/// a gate or a case draws, or every index after one lands on the wrong edge.
/// </summary>
public class TemplatesDiagramDecorationTests
{
    [Fact]
    public void DecoratePendingDiagram_CountsEveryLinkLine_ForTheLinkStyleIndices()
    {
        const string diagram = "flowchart TD\n    subgraph x[\"x\"]\n        a[\"a\"]\n    end\n    a -.->|\"case\"| b\n    a -->|\"x\"| c\n";

        var decorated = Templates.DecoratePendingDiagram(
            diagram,
            changed: new[] { ("c", "x") },
            removed: new[] { ("node:a", "c", "y", false) },
            pendingNodeIds: Array.Empty<string>());

        // The dotted case edge is link 0, the changed binding link 1, the appended removed edge link 2.
        Assert.Contains("    linkStyle 1 stroke:#0078d4", decorated);
        Assert.Contains("    a -. \"y (removed)\" .-> c\n", decorated);
        Assert.Contains("    linkStyle 2 stroke:#a4262c", decorated);
        Assert.DoesNotContain("linkStyle 0", decorated);
    }

    [Theory]
    [InlineData("    a -->|\"x\"| b", true)]
    [InlineData("    a --> b", true)]
    [InlineData("    a -.->|\"if\"| b", true)]
    [InlineData("    a -. \"x (removed)\" .-> b", true)]
    [InlineData("    subgraph for_x[\"x: per snippet.x item\"]", false)]
    [InlineData("    classDef pending stroke:#0078d4", false)]
    [InlineData("    class a,b pending", false)]
    [InlineData("    linkStyle 0 stroke:#0078d4", false)]
    [InlineData("    a[\"a<br/>A\"]", false)]
    public void IsLinkLine_CountsArrowsAlone(string line, bool isLink) =>
        Assert.Equal(isLink, Templates.IsLinkLine(line));
}
