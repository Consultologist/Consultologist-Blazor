using Consultologist.Api.Agents;
using Consultologist.Api.Workflow;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Tests;

/// <summary>
/// #433: what load proves. Unstamped, it re-matches the embedded schema
/// against the running catalog and is stranded when the catalog moved.
/// Stamped, it takes the stamp's word and checks only that each stamped
/// contract still exists — the match was made once, at publish.
/// </summary>
public class WorkflowPackageStoreResolveContractsTests
{
    private const string PackageRef = "general@v2026.08.1";

    private static readonly OutputContractCatalog Catalog = LoadCatalog();

    private static OutputContractCatalog LoadCatalog()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
        {
            dir = dir.Parent;
        }

        return OutputContractCatalog.Load(Path.Combine(dir!.FullName, "external", "consultologist-agents", "agents"));
    }

    private static Dictionary<string, string> DriftedFiles(WorkflowPackageManifest manifest)
    {
        var files = V5Fixtures.Files(manifest);
        files[V5Fixtures.SchemaPath] = TestOutputContracts.ConceptListSchema.TrimEnd().TrimEnd('}') + ", \"x-drift\": true }";
        return files;
    }

    private static WorkflowPackageStamp Stamp(params (string SchemaId, string ContractId)[] contracts) =>
        new("output-contracts@v2026.07.2", contracts.ToDictionary(pair => pair.SchemaId, pair => pair.ContractId, StringComparer.Ordinal));

    [Fact]
    public void Stamped_TakesTheContractFromTheStamp_EvenWhenTheSchemaTextNoLongerMatches()
    {
        // The catalog moved (here: the package's copy drifted, which is the
        // same disagreement). Unstamped that strands; stamped it runs.
        var manifest = V5Fixtures.Manifest();
        var files = DriftedFiles(manifest);

        var unstamped = Assert.Throws<WorkflowPackageContentException>(() =>
            WorkflowPackageStore.ResolveContracts(PackageRef, manifest, files, null, Catalog));
        Assert.Equal(
            WorkflowPackageContentException.SchemaUnmatched(PackageRef, "concept-list", Catalog.ResolvedRef).Message,
            unstamped.Message);

        var stamped = WorkflowPackageStore.ResolveContracts(PackageRef, manifest, files, Stamp(("concept-list", "concept-list")), Catalog);

        Assert.Equal(new Dictionary<string, string> { ["concept-list"] = "concept-list" }, stamped);
    }

    [Fact]
    public void Stamped_AContractTheCatalogNoLongerCarries_IsTheStampedStrandingSentence()
    {
        var manifest = V5Fixtures.Manifest();

        var exception = Assert.Throws<WorkflowPackageContentException>(() =>
            WorkflowPackageStore.ResolveContracts(
                PackageRef, manifest, V5Fixtures.Files(manifest), Stamp(("concept-list", "concept-list-v2")), Catalog));

        Assert.Equal(
            WorkflowPackageContentException.StampedContractUnknown(
                PackageRef, "concept-list", "concept-list-v2", "output-contracts@v2026.07.2", Catalog.ResolvedRef).Message,
            exception.Message);
        Assert.Contains("the catalog moved", exception.Message);
    }

    [Fact]
    public void Stamped_ASchemaTheStampOmits_IsRefused()
    {
        var manifest = V5Fixtures.Manifest();
        var schemas = new Dictionary<string, string>(manifest.Schemas!) { ["second"] = "schemas/second.json" };
        manifest = manifest with { Schemas = schemas };
        var files = V5Fixtures.Files(manifest);
        files["schemas/second.json"] = TestOutputContracts.ConceptListSchema;

        var exception = Assert.Throws<WorkflowPackageContentException>(() =>
            WorkflowPackageStore.ResolveContracts(PackageRef, manifest, files, Stamp(("concept-list", "concept-list")), Catalog));

        Assert.Equal(
            WorkflowPackageContentException.StampIncomplete(PackageRef, "second", "output-contracts@v2026.07.2").Message,
            exception.Message);
    }

    [Fact]
    public void Stamped_ExtraEntriesAreIgnored()
    {
        // The manifest is the declaration; evidence about nothing declared is inert.
        var manifest = V5Fixtures.Manifest();

        var resolved = WorkflowPackageStore.ResolveContracts(
            PackageRef, manifest, V5Fixtures.Files(manifest),
            Stamp(("concept-list", "concept-list"), ("retired", "something-gone")), Catalog);

        Assert.Equal(new[] { "concept-list" }, resolved.Keys);
    }

    [Fact]
    public void Unstamped_RederivesAsToday()
    {
        var manifest = V5Fixtures.Manifest();

        var resolved = WorkflowPackageStore.ResolveContracts(PackageRef, manifest, V5Fixtures.Files(manifest), null, Catalog);

        Assert.Equal(new Dictionary<string, string> { ["concept-list"] = "concept-list" }, resolved);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoSchemas_ResolvesEmpty_StampedOrNot(bool stamped)
    {
        var manifest = V9Fixtures.Minimal() with { Schemas = null };

        var resolved = WorkflowPackageStore.ResolveContracts(
            PackageRef, manifest, new Dictionary<string, string>(), stamped ? Stamp() : null, Catalog);

        Assert.Empty(resolved);
    }
}
