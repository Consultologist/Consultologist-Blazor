using Consultologist.Api.Agents;
using Consultologist.Api.Auth;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1508 (specVersion 31, area 11; Global Constraints "An unstamped folder does not exist for any reader"): the listings
/// that enumerate versions by <c>{name}/{version}/manifest.json</c> (the account's package and History listing, the public
/// chain view, the catalog strand sweep) were written for 5–30, which writes the manifest last. The 31 writer writes the
/// source first and the stamp <c>publish.json</c> last, so a 31 manifest is listed long before its version exists. A
/// version whose manifest declares specVersion 31 is listed only once <c>{name}/{version}/publish.json</c> is in the same
/// listing; a 5–30 version (which has a <c>publish.json</c> of its own, or none before #433) is listed as before.
/// </summary>
public class UnstampedVersionListingTests
{
    private const string Name = "acct-0123456789ab/breast";

    private static string Manifest(int spec, string title) => $$"""{ "specVersion": {{spec}}, "title": "{{title}}", "tags": ["oncology"] }""";

    private static string Path(string version, string file) => $"{Name}/{version}/{file}";

    /// <summary>A 5–30 version with its stamp, a 5–30 version from before stamps, a stamped 31 and an unstamped 31.</summary>
    private static List<string> Listing(bool stampThe31 = false)
    {
        var blobs = new List<string>
        {
            Path("v2026.08.1", "manifest.json"),
            Path("v2026.08.1", "publish.json"),
            Path("v2026.08.2", "manifest.json"),
            Path("v2026.12.1", "manifest.json"),
            Path("v2026.12.1", "nodes/plan.md"),
            Path("v2026.12.1", "resolve.json"),
            Path("v2026.12.1", "publish.json"),
            Path("v2026.12.2", "manifest.json"),
            Path("v2026.12.2", "nodes/plan.md"),
            Path("v2026.12.2", "lock.json"),
            $"{Name}/latest.json",
        };
        if (stampThe31)
            blobs.Add(Path("v2026.12.2", "publish.json"));
        return blobs;
    }

    private static readonly Dictionary<string, string> Manifests = new(StringComparer.Ordinal)
    {
        [Path("v2026.08.1", "manifest.json")] = Manifest(9, "Stamped nine"),
        [Path("v2026.08.2", "manifest.json")] = Manifest(8, "Before stamps"),
        [Path("v2026.12.1", "manifest.json")] = Manifest(31, "Stamped thirty-one"),
        [Path("v2026.12.2", "manifest.json")] = Manifest(31, "In flight"),
    };

    private sealed class Reads
    {
        public List<string> Paths { get; } = [];

        public Task<string> ReadAsync(string path)
        {
            Paths.Add(path);
            return Task.FromResult(Manifests[path]);
        }
    }

    private static Dictionary<string, (int? SpecVersion, string? Title, IReadOnlyList<string>? Tags)> NewCache() => new(StringComparer.Ordinal);

    [Fact]
    public async Task AnUnstamped31Version_IsNotListed_NorItsSpecVersionTitleOrTags()
    {
        var summary = await AccountPackageListing.SummarizeAsync(Name, new HashSet<string>(Listing()), null, new Reads().ReadAsync, NewCache());

        Assert.NotNull(summary);
        Assert.Equal(["v2026.08.1", "v2026.08.2", "v2026.12.1"], summary!.Versions);
        Assert.False(summary.SpecVersions!.ContainsKey("v2026.12.2"));
        Assert.False(summary.Titles!.ContainsKey("v2026.12.2"));
        Assert.False(summary.Tags!.ContainsKey("v2026.12.2"));
    }

    [Fact]
    public async Task AStamped31Version_IsListed_AndA5To30Version_AsBefore()
    {
        var summary = await AccountPackageListing.SummarizeAsync(Name, new HashSet<string>(Listing(stampThe31: true)), null, new Reads().ReadAsync, NewCache());

        Assert.Equal(["v2026.08.1", "v2026.08.2", "v2026.12.1", "v2026.12.2"], summary!.Versions);
        Assert.Equal(31, summary.SpecVersions!["v2026.12.2"]);
        Assert.Equal(9, summary.SpecVersions["v2026.08.1"]);
        Assert.Equal(8, summary.SpecVersions["v2026.08.2"]);
        Assert.Equal("In flight", summary.Titles!["v2026.12.2"]);
        Assert.Equal("Before stamps", summary.Titles["v2026.08.2"]);
    }

    [Fact]
    public async Task AnUnstamped31Version_IsNeverCachedAsExisting_AndIsListedOnceStamped()
    {
        var cache = NewCache();
        var reads = new Reads();

        await AccountPackageListing.SummarizeAsync(Name, new HashSet<string>(Listing()), null, reads.ReadAsync, cache);
        Assert.Equal(
            [Path("v2026.08.1", "manifest.json"), Path("v2026.08.2", "manifest.json"), Path("v2026.12.1", "manifest.json")],
            cache.Keys.Order(StringComparer.Ordinal));

        // Listed again while still unstamped: only the unstamped manifest is read again (no read per 5–30 version), and
        // it is still not listed.
        reads.Paths.Clear();
        var still = await AccountPackageListing.SummarizeAsync(Name, new HashSet<string>(Listing()), null, reads.ReadAsync, cache);
        Assert.Equal([Path("v2026.12.2", "manifest.json")], reads.Paths);
        Assert.DoesNotContain("v2026.12.2", still!.Versions);

        // Stamped: listed, and cached from then on.
        reads.Paths.Clear();
        var stamped = await AccountPackageListing.SummarizeAsync(Name, new HashSet<string>(Listing(stampThe31: true)), null, reads.ReadAsync, cache);
        Assert.Contains("v2026.12.2", stamped!.Versions);
        Assert.Equal([Path("v2026.12.2", "manifest.json")], reads.Paths);
        Assert.True(cache.ContainsKey(Path("v2026.12.2", "manifest.json")));
    }

    [Fact]
    public async Task ANameWhoseOnlyVersionIsAnUnstamped31_IsNoPackage()
    {
        var blobs = new HashSet<string> { Path("v2026.12.2", "manifest.json"), Path("v2026.12.2", "nodes/plan.md") };

        Assert.Null(await AccountPackageListing.SummarizeAsync(Name, blobs, null, new Reads().ReadAsync, NewCache()));
    }

    [Fact]
    public void Build_DropsAnUnstamped31_AndKeepsA5To30WithOrWithoutItsStamp()
    {
        var specVersions = new Dictionary<string, int> { ["v2026.08.1"] = 9, ["v2026.08.2"] = 8, ["v2026.12.1"] = 31, ["v2026.12.2"] = 31 };
        var titles = new Dictionary<string, string> { ["v2026.08.1"] = "A", ["v2026.12.2"] = "In flight" };

        var summary = AccountPackageListing.Build(Name, Listing(), """{ "version": "v2026.12.1" }""", specVersions, titles);

        Assert.Equal(["v2026.08.1", "v2026.08.2", "v2026.12.1"], summary.Versions);
        Assert.Equal("v2026.12.1", summary.Latest);
        Assert.False(summary.SpecVersions!.ContainsKey("v2026.12.2"));
        Assert.Equal(["v2026.08.1"], summary.Titles!.Keys);

        // With no spec versions given (unknown), nothing is dropped: the listing is as it was before 31.
        Assert.Equal(["v2026.08.1", "v2026.08.2", "v2026.12.1", "v2026.12.2"], AccountPackageListing.Build(Name, Listing(), null).Versions);
    }

    [Fact]
    public void ThePublicChainView_DropsAnUnstamped31_AndKeepsTheRest()
    {
        var blobs = new[]
        {
            "general/v2026.08.1/manifest.json",
            "general/v2026.08.1/publish.json",
            "general/v2026.12.1/manifest.json",
            "general/v2026.12.1/publish.json",
            "general/v2026.12.2/manifest.json",
            "test/general/v2026.12.3/manifest.json",
        };
        var specVersions = new Dictionary<string, int>
        {
            ["general/v2026.08.1"] = 9,
            ["general/v2026.12.1"] = 31,
            ["general/v2026.12.2"] = 31,
            ["test/general/v2026.12.3"] = 31,
        };

        var chain = PublicRegistryReader.Assemble(
            blobs, Array.Empty<string>(), Array.Empty<string>(), new Dictionary<string, string>(), DateTimeOffset.UtcNow, specVersions);

        var general = Assert.Single(chain.Packages);
        Assert.Equal("general", general.Name);
        Assert.Equal(["v2026.08.1", "v2026.12.1"], general.Versions);
        Assert.Equal(["v2026.08.1", "v2026.12.1"], general.SpecVersions!.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheCatalogStrandSweep_DoesNotCountAnUnstamped31AsAVersion()
    {
        var registry = new FakeRegistryReader();
        const string Schemas = ""","schemas":{},"sectionSteps":[]""";
        registry.Put("general", "v2026.08.1", "manifest.json", """{"name":"general","version":"v2026.08.1","specVersion":9""" + Schemas + "}");
        registry.Put("acct-1234567890ab", "v2026.12.1", "manifest.json", """{"name":"acct-1234567890ab","version":"v2026.12.1","specVersion":31}""");
        registry.Put("acct-1234567890ab", "v2026.12.1", "publish.json", """{"specVersion":31}""");
        registry.Put("acct-1234567890ab", "v2026.12.2", "manifest.json", """{"name":"acct-1234567890ab","version":"v2026.12.2","specVersion":31}""");

        var accounts = Substitute.For<IAccountStore>();
        accounts.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<AccountSummary>());
        var resolver = new WorkflowPackagePinResolver(new FakeSettingsStore(), new FakeOwnership(), NullLogger<WorkflowPackagePinResolver>.Instance);
        var catalog = OutputContractCatalog.Load(System.IO.Path.Combine(RepoRoot(), "external", "consultologist-agents", "agents"));
        var catalogResolver = new CatalogResolver(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), (_, _) => Task.FromResult(catalog));

        var report = await new CatalogStrandSweeper(registry, accounts, resolver, catalogResolver).RunAsync(catalog, CancellationToken.None);

        // The 5–30 version and the stamped 31 (unsupported by the 5–30 sweep, as before); the unstamped 31 is no version.
        Assert.Equal(2, report.Counts.Versions);
        Assert.Equal(1, report.Counts.SkippedUnsupportedSpec);
        Assert.DoesNotContain(report.Versions, v => v.Ref == "acct-1234567890ab@v2026.12.2");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(System.IO.Path.Combine(dir.FullName, "Consultologist.sln")))
            dir = dir.Parent;
        return dir!.FullName;
    }
}
