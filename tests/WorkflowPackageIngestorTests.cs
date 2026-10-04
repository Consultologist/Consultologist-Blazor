using System.Text.Json;
using System.Text.Json.Serialization;
using Consultologist.Api.Agents;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Xunit;

namespace Consultologist.Api.Tests;

/// <summary>
/// #858: the server-side import ingest — unzip an uploaded package, parse its
/// manifest, and run the same content validation publish runs (paths + closure +
/// the validator against the real catalog), returning normalized content and
/// advisory findings. Unreadable bytes are a parse error; a parseable package
/// with findings still loads (import is non-blocking; publish is the gate).
/// </summary>
public class WorkflowPackageIngestorTests
{
    // The publisher's wire form: camelCase, nulls omitted — the shape a real
    // manifest.json carries in a package .zip.
    private static readonly JsonSerializerOptions Wire = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static OutputContractCatalog LoadCatalog()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "external", "consultologist-agents", "agents", "output-contracts.json")))
        {
            dir = dir.Parent;
        }

        return OutputContractCatalog.Load(Path.Combine(dir!.FullName, "external", "consultologist-agents", "agents"));
    }

    private static WorkflowPackageIngestor Ingestor() => new(LoadCatalog());

    private static byte[] Zip(IReadOnlyDictionary<string, string> files, string manifestJson)
    {
        var all = new Dictionary<string, string>(files, StringComparer.Ordinal)
        {
            ["manifest.json"] = manifestJson
        };
        return WorkflowPackageArchive.Zip(all);
    }

    private static byte[] ValidPackageZip(WorkflowPackageManifest manifest) =>
        Zip(V5Fixtures.Files(manifest), JsonSerializer.Serialize(manifest, Wire));

    [Fact]
    public void AValidPackage_Ingests_WithNormalizedContentAndNoFindings()
    {
        var manifest = V5Fixtures.Manifest();

        var result = Ingestor().Ingest(ValidPackageZip(manifest));

        Assert.True(result.Parsed);
        Assert.Null(result.Error);
        Assert.Empty(result.Findings);
        Assert.NotNull(result.Content);
        Assert.Equal(manifest.Name, result.Content!.Name);
        Assert.Equal(manifest.Version, result.Content.Version);
        Assert.Equal(manifest.SpecVersion, result.Content.SpecVersion);
        // manifest.json is split out — the content carries only package files.
        Assert.DoesNotContain("manifest.json", result.Content.Files.Keys);
    }

    [Fact]
    public void ABadZip_IsAParseError_NotContent()
    {
        var result = Ingestor().Ingest(new byte[] { 1, 2, 3, 4 });

        Assert.False(result.Parsed);
        Assert.Null(result.Content);
        Assert.Contains("not a valid .zip", result.Error);
    }

    [Fact]
    public void AZipWithoutAManifest_IsAParseError()
    {
        var zip = WorkflowPackageArchive.Zip(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["prompts/draft.md"] = "no manifest here"
        });

        var result = Ingestor().Ingest(zip);

        Assert.False(result.Parsed);
        Assert.Contains("no manifest.json", result.Error);
    }

    [Fact]
    public void AMalformedManifest_IsAParseError()
    {
        var zip = Zip(new Dictionary<string, string>(StringComparer.Ordinal), "{ not valid json");

        var result = Ingestor().Ingest(zip);

        Assert.False(result.Parsed);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void AnUnreferencedFile_IsAFinding_ButThePackageStillLoads()
    {
        var manifest = V5Fixtures.Manifest();
        var files = V5Fixtures.Files(manifest);
        files["prompts/orphan.md"] = "not referenced by the manifest";

        var result = Ingestor().Ingest(Zip(files, JsonSerializer.Serialize(manifest, Wire)));

        Assert.True(result.Parsed); // non-blocking: it loads
        Assert.Contains(result.Findings, f => f.Contains("orphan.md") && f.Contains("not referenced by the manifest"));
    }

    [Fact]
    public void ACustomSchemaFile_IsReferenced_NotAStray()
    {
        // #760: a customSchemas body file is referenced by the manifest (like a
        // schemas file), so the content check must not flag it as an unreferenced
        // stray. (Regression: the referenced-files set omitted customSchemas, so a
        // custom-schema package could not be published.)
        var (manifest, files) = V22Fixtures.Custom();

        var result = Ingestor().Ingest(Zip(
            new Dictionary<string, string>(files, StringComparer.Ordinal),
            JsonSerializer.Serialize(manifest, Wire)));

        Assert.True(result.Parsed);
        Assert.DoesNotContain(
            result.Findings,
            f => f.Contains(V22Fixtures.CustomSchemaPath) && f.Contains("not referenced"));
    }

    [Fact]
    public void ADisallowedFilePath_IsAFinding()
    {
        var manifest = V5Fixtures.Manifest();
        var files = V5Fixtures.Files(manifest);
        files["notes.txt"] = "a top-level file is not an allowed package path";

        var result = Ingestor().Ingest(Zip(files, JsonSerializer.Serialize(manifest, Wire)));

        Assert.True(result.Parsed);
        Assert.Contains(result.Findings, f => f.Contains("notes.txt") && f.Contains("is not allowed"));
    }
}
