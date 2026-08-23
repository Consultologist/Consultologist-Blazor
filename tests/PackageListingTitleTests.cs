using Consultologist.Api.Workflow;

namespace Consultologist.Api.Tests;

/// <summary>#432, v9 § 4: the listings carry each version's title beside its spec version.</summary>
public class PackageListingTitleTests
{
    [Theory]
    [InlineData("""{ "specVersion": 9, "title": "Breast oncology consults" }""", "Breast oncology consults")]
    [InlineData("""{ "specVersion": 9 }""", null)]
    [InlineData("""{ "specVersion": 9, "title": "" }""", null)]
    [InlineData("""{ "specVersion": 9, "title": "   " }""", null)]
    [InlineData("""{ "specVersion": 9, "title": 7 }""", null)]
    [InlineData("{ not json", null)]
    public void ReadTitle_ParsesOrDegrades(string manifestJson, string? expected)
    {
        Assert.Equal(expected, AccountPackageListing.ReadTitle(manifestJson));
    }

    [Fact]
    public void Build_CarriesTitles()
    {
        var summary = AccountPackageListing.Build(
            "acct-0123456789ab",
            new[] { "acct-0123456789ab/v2026.08.1/manifest.json", "acct-0123456789ab/v2026.09.1/manifest.json" },
            """{ "version": "v2026.09.1" }""",
            new Dictionary<string, int> { ["v2026.08.1"] = 8, ["v2026.09.1"] = 9 },
            new Dictionary<string, string> { ["v2026.09.1"] = "Breast oncology consults" });

        Assert.Equal("Breast oncology consults", summary.Titles!["v2026.09.1"]);
        Assert.False(summary.Titles.ContainsKey("v2026.08.1"));
    }

    [Fact]
    public void Assemble_MapsTitlesPerPackage_AndLeavesThemNullWithoutAny()
    {
        var blobs = new[]
        {
            "general/v2026.08.1/manifest.json",
            "general/v2026.09.1/manifest.json",
            "acct-0123456789ab/v2026.09.1/manifest.json"
        };
        var titles = new Dictionary<string, string>
        {
            ["general/v2026.09.1"] = "General consults",
            ["acct-0123456789ab/v2026.09.1"] = "Breast oncology consults"
        };

        var withTitles = PublicRegistryReader.Assemble(
            blobs, Array.Empty<string>(), Array.Empty<string>(), new Dictionary<string, string>(),
            DateTimeOffset.UtcNow, null, titles);

        var general = withTitles.Packages.Single(p => p.Name == "general");
        Assert.Equal(new[] { "v2026.09.1" }, general.Titles!.Keys);
        Assert.Equal("General consults", general.Titles["v2026.09.1"]);
        Assert.Equal("Breast oncology consults", withTitles.Packages.Single(p => p.Name == "acct-0123456789ab").Titles!["v2026.09.1"]);

        var without = PublicRegistryReader.Assemble(
            blobs, Array.Empty<string>(), Array.Empty<string>(), new Dictionary<string, string>(), DateTimeOffset.UtcNow);
        Assert.All(without.Packages, package => Assert.Null(package.Titles));
    }
}
