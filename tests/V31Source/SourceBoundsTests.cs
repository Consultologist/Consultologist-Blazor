using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Source;

/// <summary>#1499: the constants of the 31 source reader are the ones bounds.json publishes.</summary>
public class SourceBoundsTests
{
    private static readonly Dictionary<string, int> Source = new()
    {
        ["files"] = SourceBounds.Files,
        ["fileBytes"] = SourceBounds.FileBytes,
        ["totalBytes"] = SourceBounds.TotalBytes,
        ["pathLength"] = SourceBounds.PathLength,
        ["idLength"] = SourceBounds.IdLength,
        ["jsonDepth"] = SourceBounds.JsonDepth,
        ["expressionNodes"] = SourceBounds.ExpressionNodes,
    };

    private static readonly Dictionary<string, int> Format = new()
    {
        ["passes"] = FormatBounds.Passes,
        ["depth"] = FormatBounds.Depth,
        ["parallel"] = FormatBounds.Parallel,
    };

    [Fact]
    public void Bounds_AreThePublishedOnes()
    {
        var bounds = V31Contract.Json("bounds.json");
        AssertSame(bounds["sourceBounds"]!.AsObject(), Source);
        AssertSame(bounds["formatBounds"]!.AsObject(), Format);
        Assert.Equal(
            bounds["windowsReservedIds"]!.AsArray().Select(v => (string)v!),
            ReservedIds.Windows);
        Assert.Equal(
            bounds["reservedIds"]!.AsArray().Select(v => (string)v!),
            ReservedIds.Words);
    }

    private static void AssertSame(JsonObject published, Dictionary<string, int> constants)
    {
        Assert.Equal(published.Select(p => p.Key).Order(), constants.Keys.Order());
        foreach (var (name, value) in constants)
        {
            Assert.Equal((int)published[name]!, value);
        }
    }
}
