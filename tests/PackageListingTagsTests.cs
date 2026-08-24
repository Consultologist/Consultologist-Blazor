using Consultologist.Api.Workflow;

namespace Consultologist.Api.Tests;

/// <summary>#453: the listings carry each version's tags beside its title, for the picker to filter by.</summary>
public class PackageListingTagsTests
{
    [Fact]
    public void ReadTags_ReadsTheArrayInAuthoredOrder()
    {
        Assert.Equal(
            new[] { "oncology", "Breast", "new-patient" },
            AccountPackageListing.ReadTags("""{ "specVersion": 9, "tags": ["oncology", "Breast", "new-patient"] }"""));
    }

    [Fact]
    public void ReadTags_AnEmptyArray_IsAStatedNone_NotAbsence()
    {
        var tags = AccountPackageListing.ReadTags("""{ "specVersion": 9, "tags": [] }""");

        Assert.NotNull(tags);
        Assert.Empty(tags!);
    }

    [Theory]
    [InlineData("""{ "specVersion": 8 }""")]
    [InlineData("""{ "specVersion": 9, "tags": null }""")]
    [InlineData("""{ "specVersion": 9, "tags": "oncology" }""")]
    [InlineData("{ not json")]
    public void ReadTags_DegradesToNull(string manifestJson)
    {
        Assert.Null(AccountPackageListing.ReadTags(manifestJson));
    }

    [Fact]
    public void ReadTags_SkipsWhatIsNotAString()
    {
        // A listing, not the validator: the publish refused this already, and
        // a stray number in a hand-edited public manifest should not blank
        // the picker.
        Assert.Equal(new[] { "oncology" }, AccountPackageListing.ReadTags("""{ "tags": ["oncology", 7, null] }"""));
    }

    [Fact]
    public void Build_AndAssemble_ReadANestedNameAsItself()
    {
        // #448: a nested package lists with its own versions; a flat one
        // beside it is untouched, and neither swallows the other.
        var blobs = new[]
        {
            "oncology/breast/v2026.08.1/manifest.json",
            "oncology/breast/v2026.08.2/manifest.json",
            "oncology/breast/latest.json",
            "oncology/v2026.08.1/manifest.json"
        };

        var nested = AccountPackageListing.Build("oncology/breast", blobs, """{ "version": "v2026.08.2" }""");
        Assert.Equal(new[] { "v2026.08.1", "v2026.08.2" }, nested.Versions);
        Assert.Equal("v2026.08.2", nested.Latest);
        Assert.Equal(new[] { "v2026.08.1" }, AccountPackageListing.Build("oncology", blobs, null).Versions);

        var chain = PublicRegistryReader.Assemble(
            blobs, Array.Empty<string>(), Array.Empty<string>(),
            new Dictionary<string, string> { ["workflow-packages/oncology/breast/latest.json"] = """{ "version": "v2026.08.2" }""" },
            DateTimeOffset.UtcNow,
            new Dictionary<string, int> { ["oncology/breast/v2026.08.2"] = 9 },
            new Dictionary<string, string> { ["oncology/breast/v2026.08.2"] = "Breast" },
            new Dictionary<string, IReadOnlyList<string>> { ["oncology/breast/v2026.08.2"] = new[] { "oncology" } });
        Assert.Equal(new[] { "oncology", "oncology/breast" }, chain.Packages.Select(p => p.Name));
        var breast = chain.Packages.Single(p => p.Name == "oncology/breast");
        Assert.Equal("v2026.08.2", breast.Latest);
        Assert.Equal(9, breast.SpecVersions!["v2026.08.2"]);
        Assert.Equal("Breast", breast.Titles!["v2026.08.2"]);
        Assert.Equal(new[] { "oncology" }, breast.Tags!["v2026.08.2"]);
    }

    [Fact]
    public void Build_CarriesTags()
    {
        var summary = AccountPackageListing.Build(
            "acct-0123456789ab",
            new[] { "acct-0123456789ab/v2026.08.1/manifest.json", "acct-0123456789ab/v2026.09.1/manifest.json" },
            """{ "version": "v2026.09.1" }""",
            new Dictionary<string, int> { ["v2026.08.1"] = 8, ["v2026.09.1"] = 9 },
            null,
            new Dictionary<string, IReadOnlyList<string>> { ["v2026.09.1"] = new[] { "oncology" } });

        Assert.Equal(new[] { "oncology" }, summary.Tags!["v2026.09.1"]);
        Assert.False(summary.Tags.ContainsKey("v2026.08.1"));
        Assert.Null(summary.Titles);
    }

    [Fact]
    public void Assemble_MapsTagsPerPackage_AndLeavesThemNullWithoutAny()
    {
        var blobs = new[]
        {
            "general/v2026.08.1/manifest.json",
            "general/v2026.09.1/manifest.json",
            "acct-0123456789ab/v2026.09.1/manifest.json"
        };
        var tags = new Dictionary<string, IReadOnlyList<string>>
        {
            ["general/v2026.09.1"] = new[] { "general" },
            ["acct-0123456789ab/v2026.09.1"] = new[] { "oncology", "breast" }
        };

        var with = PublicRegistryReader.Assemble(
            blobs, Array.Empty<string>(), Array.Empty<string>(), new Dictionary<string, string>(),
            DateTimeOffset.UtcNow, null, null, tags);

        var general = with.Packages.Single(p => p.Name == "general");
        Assert.Equal(new[] { "v2026.09.1" }, general.Tags!.Keys);
        Assert.Equal(new[] { "general" }, general.Tags["v2026.09.1"]);
        Assert.Equal(new[] { "oncology", "breast" }, with.Packages.Single(p => p.Name == "acct-0123456789ab").Tags!["v2026.09.1"]);

        var without = PublicRegistryReader.Assemble(
            blobs, Array.Empty<string>(), Array.Empty<string>(), new Dictionary<string, string>(), DateTimeOffset.UtcNow);
        Assert.All(without.Packages, package => Assert.Null(package.Tags));
    }
}
