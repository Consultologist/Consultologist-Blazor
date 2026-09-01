using Consultologist.Api.Agents;
using Consultologist.Api.Auth;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Consultologist.Api.Tests;

/// <summary>#452: would this catalog strand a published version? Both registries, the store's premises.</summary>
public class CatalogStrandsTests
{
    private const string Ref = "general@v2026.08.1";
    private const string SchemaPath = "schemas/concept-list.json";
    private static readonly OutputContractCatalog Catalog = OutputContractCatalog.Load(Path.Combine(RepoRoot(), "external", "consultologist-agents", "agents"));
    private static readonly int[] Supported = { 5, 6, 7, 8, 9 };

    private static string Manifest(int spec = 9, bool schemas = true) =>
        schemas
            ? $$"""{"name":"general","version":"v2026.08.1","specVersion":{{spec}},"schemas":{"concept-list":"{{SchemaPath}}"},"sectionSteps":[]}"""
            : $$"""{"name":"general","version":"v2026.08.1","specVersion":{{spec}}}""";

    private static string Drifted => TestOutputContracts.ConceptListSchema.TrimEnd().TrimEnd('}') + ", \"x-drift\": true }";

    private static CatalogStrandVersion? Check(string manifest, string? stamp, Func<string, string?> read, out string? skip, out bool stamped) =>
        CatalogStrands.Check(Ref, manifest, stamp, read, Catalog, Supported, out skip, out stamped);

    [Fact]
    public void AnUnstampedVersionWhoseCopyStillMatches_IsHealthy()
    {
        // The manifest carries retired vocabulary (sectionSteps): the read is permissive on purpose.
        var result = Check(Manifest(), null, _ => TestOutputContracts.ConceptListSchema, out var skip, out var stamped);
        Assert.Null(result);
        Assert.Null(skip);
        Assert.False(stamped);
    }

    [Fact]
    public void AnUnstampedVersionWhoseCopyDrifted_IsStrandedWithTheStoresSentence()
    {
        var result = Check(Manifest(), null, _ => Drifted, out _, out _);

        Assert.NotNull(result);
        Assert.Equal(PinHealthStatuses.Stranded, result!.Status);
        var schema = Assert.Single(result.Schemas);
        Assert.Equal("concept-list", schema.SchemaId);
        Assert.Equal(WorkflowPackageContentException.SchemaUnmatched(Ref, "concept-list", Catalog.ResolvedRef).Message, schema.Reason);
    }

    [Fact]
    public void AStampedVersion_IsJudgedByItsContractId_NotItsCopy()
    {
        // The copy drifted, but the stamp names a contract the candidate carries: healthy.
        const string kept = """{"catalogRef":"output-contracts@v2026.07.1","contracts":{"concept-list":"concept-list"}}""";
        Assert.Null(Check(Manifest(), kept, _ => Drifted, out _, out var stamped));
        Assert.True(stamped);

        const string retired = """{"catalogRef":"output-contracts@v2026.07.1","contracts":{"concept-list":"concept-list-v2"}}""";
        var result = Check(Manifest(), retired, _ => TestOutputContracts.ConceptListSchema, out _, out _);
        Assert.Equal(
            WorkflowPackageContentException.StampedContractUnknown(Ref, "concept-list", "concept-list-v2", "output-contracts@v2026.07.1", Catalog.ResolvedRef).Message,
            Assert.Single(result!.Schemas).Reason);

        const string incomplete = """{"catalogRef":"output-contracts@v2026.07.1","contracts":{}}""";
        Assert.Contains("has no contract in its publication stamp", Assert.Single(Check(Manifest(), incomplete, _ => null, out _, out _)!.Schemas).Reason);
    }

    [Fact]
    public void SkipsAreNamed_AndComeBeforeAnySchemaQuestion()
    {
        Assert.Null(Check(Manifest(spec: 4), null, _ => Drifted, out var skip, out _));
        Assert.Equal(CatalogStrandSkips.UnsupportedSpec, skip);

        Assert.Null(Check(Manifest(schemas: false), null, _ => Drifted, out skip, out _));
        Assert.Equal(CatalogStrandSkips.NoSchema, skip);

        // Both at once: the spec gate speaks first, as the store's does.
        Assert.Null(Check(Manifest(spec: 4, schemas: false), null, _ => Drifted, out skip, out _));
        Assert.Equal(CatalogStrandSkips.UnsupportedSpec, skip);
    }

    [Fact]
    public void WhatCannotBeRead_IsUnreadable_NotStranded()
    {
        var missing = Check(Manifest(), null, _ => null, out _, out _);
        Assert.Equal(PinHealthStatuses.Unreadable, missing!.Status);
        Assert.Contains(SchemaPath, Assert.Single(missing.Schemas).Reason);

        var garbage = Check("{ not json", null, _ => null, out _, out _);
        Assert.Equal(PinHealthStatuses.Unreadable, garbage!.Status);
        Assert.Equal("(manifest)", Assert.Single(garbage.Schemas).SchemaId);
    }

    [Theory]
    [InlineData(null, "must be output-contracts@vYYYY.MM.N")]
    [InlineData("output-contracts@latest", "not @latest")]
    [InlineData("general@v2026.08.1", "names 'general'")]
    public void ACandidate_MustBeAConcreteCatalogVersion(string? candidate, string expected)
    {
        Assert.Contains(expected, CatalogStrands.ValidateCandidate(candidate));
        Assert.Null(CatalogStrands.ValidateCandidate("output-contracts@v2026.07.2"));
    }

    [Fact]
    public async Task TheSweep_ReadsBothRegistries_AndCountsWhoIsPinnedToWhatWouldStrand()
    {
        var registry = new FakeRegistryReader();
        registry.Put("general", "v2026.08.1", "manifest.json", Manifest());
        registry.Put("general", "v2026.08.1", SchemaPath, TestOutputContracts.ConceptListSchema);
        registry.Put("general", "v2026.01.1", "manifest.json", Manifest(spec: 2));
        registry.Put("acct-1234567890ab", "v2026.08.3", "manifest.json", Manifest().Replace("\"general\"", "\"acct-1234567890ab\"").Replace("v2026.08.1", "v2026.08.3"));
        registry.Put("acct-1234567890ab", "v2026.08.3", SchemaPath, Drifted);
        registry.Blobs["acct-1234567890ab/latest.json"] = """{"version":"v2026.08.3"}""";

        var accounts = Substitute.For<IAccountStore>();
        accounts.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<AccountSummary>
        {
            new("user-a", AccountStatuses.Active), new("user-b", AccountStatuses.Active), new("user-c", AccountStatuses.Active)
        });
        var settings = new FakeSettingsStore();
        await settings.SaveAsync("user-a", WorkflowPackagePinResolver.PackagePinSettingKey, "acct-1234567890ab@v2026.08.3", "text/plain", CancellationToken.None);
        await settings.SaveAsync("user-b", WorkflowPackagePinResolver.PackagePinSettingKey, "acct-1234567890ab@latest", "text/plain", CancellationToken.None);
        var ownership = new FakeOwnership();
        ownership.Records.Add(("user-a", "acct-1234567890ab"));
        ownership.Records.Add(("user-b", "acct-1234567890ab"));
        var resolver = new WorkflowPackagePinResolver(settings, ownership, NullLogger<WorkflowPackagePinResolver>.Instance);

        var report = await new CatalogStrandSweeper(registry, accounts, resolver).RunAsync(Catalog, CancellationToken.None);

        Assert.Equal(Catalog.ResolvedRef, report.Candidate);
        Assert.Equal(3, report.Counts.Versions);
        Assert.Equal(2, report.Counts.Checked);
        Assert.Equal(1, report.Counts.SkippedUnsupportedSpec);
        Assert.Equal(0, report.Counts.SkippedNoSchema);
        Assert.True(report.Counts.PublicRegistryRead);

        var stranded = Assert.Single(report.Versions);
        Assert.Equal("acct-1234567890ab@v2026.08.3", stranded.Ref);
        Assert.Equal(PinHealthStatuses.Stranded, stranded.Status);
        // user-a by concrete pin, user-b through @latest; user-c is on the default.
        Assert.Equal(2, stranded.PinnedBy);
    }

    [Fact]
    public void TheResponse_CarriesExactlyTheReportFields()
    {
        static string[] Names(Type t) => t.GetProperties().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "Candidate", "Counts", "Engine", "GeneratedAtUtc", "Versions" }, Names(typeof(CatalogStrandResponse)));
        Assert.Equal(new[] { "Checked", "PublicRegistryRead", "SkippedNoSchema", "SkippedUnsupportedSpec", "Stamped", "Versions" }, Names(typeof(CatalogStrandCounts)));
        Assert.Equal(new[] { "PinnedBy", "Ref", "Schemas", "Status" }, Names(typeof(CatalogStrandVersion)));
        Assert.Equal(new[] { "Reason", "SchemaId" }, Names(typeof(CatalogStrandSchema)));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }
}

/// <summary>Blobs keyed name/version/path, as FakeRegistryWriter keys them; acct-* names are the private registry — since #602 the union of the org/personal pair, whose membership is exactly the acct-* names, so the prefix model stays truthful.</summary>
internal sealed class FakeRegistryReader : IWorkflowPackageRegistryReader
{
    public Dictionary<string, string> Blobs { get; } = new(StringComparer.Ordinal);

    public bool HasPublicRegistry => true;

    public void Put(string name, string version, string path, string content) => Blobs[$"{name}/{version}/{path}"] = content;

    public Task<IReadOnlyList<string>> ListBlobNamesAsync(bool privateRegistry, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(Blobs.Keys.Where(k => k.StartsWith("acct-", StringComparison.Ordinal) == privateRegistry).ToList());

    public Task<string?> TryDownloadAsync(string packageName, string blobPath, CancellationToken cancellationToken) =>
        Task.FromResult(Blobs.TryGetValue(blobPath, out var content) ? content : null);
}
