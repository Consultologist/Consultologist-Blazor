using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Consultologist.Api.Agents;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #449: the deployed engine says what it is. Tested as statics because there
/// is no HttpRequestData harness in this repo; Public/Engine is one call to
/// EngineAttestation.Current.
/// </summary>
public class EngineAttestationTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public void CommitOf_ReadsTheFullShaTheBuildStamped()
    {
        Assert.Equal(Sha, EngineAttestation.CommitOf($"1.0.0+{Sha}"));
        // Lowercased: a checkout ref compares by bytes, and git prints lower.
        Assert.Equal(Sha, EngineAttestation.CommitOf($"1.0.0+{Sha.ToUpperInvariant()}"));
        // Other metadata tokens ride beside it.
        Assert.Equal(Sha, EngineAttestation.CommitOf($"1.0.0+build.7.{Sha}"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.0.0")]
    [InlineData("1.0.0+abc")]
    [InlineData("1.0.0+0123456789abcdef0123456789abcdef0123456")]
    [InlineData("1.0.0+0123456789abcdef0123456789abcdef012345678")]
    [InlineData("1.0.0+0123456789abcdef0123456789abcdef0123456g")]
    public void CommitOf_IsNullUnlessAFullShaIsThere(string? informationalVersion)
    {
        Assert.Null(EngineAttestation.CommitOf(informationalVersion));
    }

    [Fact]
    public void Describe_StatesTheBuildAndWhatItRuns()
    {
        var now = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
        var response = EngineAttestation.Describe($"1.0.0+{Sha}", "output-contracts@v2026.07.2", "v2026.08.6", "v2026.08.1", now);

        Assert.Equal(Sha, response.Commit);
        Assert.Equal("1.0.0", response.Version);
        Assert.Equal("output-contracts@v2026.07.2", response.OutputContracts);
        Assert.Equal("v2026.08.6", response.PackageFormat);
        Assert.Equal("v2026.08.1", response.Provenance);
        // By identity, not value: the two sets are equal today, so a swapped
        // pair would read the same until the day they differ.
        Assert.Same(WorkflowPackageValidator.AcceptedSpecVersions, response.AcceptedSpecVersions);
        Assert.Same(WorkflowPackageStore.SupportedSpecVersions, response.SupportedSpecVersions);
        // The same containment SpecVersionSetTests pins, asserted on the wire shape.
        Assert.True(response.SupportedSpecVersions.All(response.AcceptedSpecVersions.Contains));
        Assert.Equal(WorkflowPackageValidator.EngineScribanVersion.ToString(), response.Scriban);
        Assert.Matches(@"^\d+\.\d+\.\d+$", response.Scriban);
        Assert.Equal(now, response.GeneratedAtUtc);
    }

    [Fact]
    public void Describe_AnUnstampedBuild_SaysSoWithoutAnyCommit()
    {
        var response = EngineAttestation.Describe("1.0.0", "output-contracts@v2026.07.2", null, null, DateTimeOffset.UnixEpoch);
        Assert.Null(response.Commit);
        Assert.Equal("1.0.0", response.Version);
        Assert.Null(response.PackageFormat);

        Assert.Equal("unknown", EngineAttestation.Describe(null, "output-contracts@v2026.07.2", null, null, DateTimeOffset.UnixEpoch).Version);
    }

    [Fact]
    public void TheCopiedFormatIndex_IsTheVendoredOne()
    {
        // Proves the csproj copy: what the build output carries is the
        // submodule's spec-versions.json, version for version.
        var vendored = (string)JsonNode.Parse(File.ReadAllText(VendoredSpecVersionsPath()))!["version"]!;
        Assert.Matches(@"^v\d{4}\.\d{2}\.\d+$", vendored);
        Assert.Equal(vendored, EngineAttestation.PackageFormatVersionIn(AppContext.BaseDirectory));
    }

    [Fact]
    public void TheCopiedProvenanceIndex_IsTheVendoredOne()
    {
        var vendored = (string)JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "external", "consultologist-provenance", "provenance-versions.json")))!["version"]!;
        Assert.Matches(@"^v\d{4}\.\d{2}\.\d+$", vendored);
        Assert.Equal(vendored, EngineAttestation.ProvenanceVersionIn(AppContext.BaseDirectory));
    }

    [Fact]
    public void PackageFormatVersionIn_IsNullWhereTheIndexIsNot()
    {
        Assert.Null(EngineAttestation.PackageFormatVersionIn(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public void Current_ReadsTheRealCatalogAndAssembly()
    {
        var response = EngineAttestation.Current(OutputContractCatalog.Load(Path.Combine(RepoRoot(), "external", "consultologist-agents", "agents")));

        Assert.Matches(@"^output-contracts@v\d{4}\.\d{2}\.\d+$", response.OutputContracts);
        // A test build is unstamped; a CI build may not be. Either way the
        // commit is a full sha or absent — never a fragment.
        Assert.True(response.Commit == null || Regex.IsMatch(response.Commit, "^[0-9a-f]{40}$"));
    }

    [Fact]
    public void TheResponse_CarriesExactlyTheDeploymentFacts()
    {
        // Anonymous and no-PHI by construction: a new property has to be
        // added here, in the open, before it can reach the wire.
        var properties = typeof(EngineAttestationResponse).GetProperties().Select(p => p.Name).Order(StringComparer.Ordinal);
        Assert.Equal(
            new[] { "AcceptedSpecVersions", "Commit", "GeneratedAtUtc", "OutputContracts", "PackageFormat", "Provenance", "Scriban", "SupportedSpecVersions", "Version" },
            properties);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string VendoredSpecVersionsPath()
    {
        var path = Path.Combine(RepoRoot(), "external", "consultologist-package-format", "spec-versions.json");
        Assert.True(File.Exists(path), $"{path} is missing — the submodule is not checked out.");
        return path;
    }
}
