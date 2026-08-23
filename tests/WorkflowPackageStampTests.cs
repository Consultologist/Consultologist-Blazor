using Consultologist.Api.Agents;
using Consultologist.Api.Workflow;

namespace Consultologist.Api.Tests;

/// <summary>
/// #433: the publication stamp — what each declared schema resolved to, under
/// which catalog, recorded once at publish. Computed against the real bundled
/// catalog, as the publisher computes it.
/// </summary>
public class WorkflowPackageStampTests
{
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

    /// <summary>The concept-list schema with a property the catalog's does not have: canonically a different schema.</summary>
    private static string DriftedSchema() =>
        TestOutputContracts.ConceptListSchema.TrimEnd().TrimEnd('}') + ", \"x-drift\": true }";

    [Fact]
    public void Compute_ResolvesEveryDeclaredSchema_ToItsContract()
    {
        var manifest = V5Fixtures.Manifest();
        var errors = new List<string>();

        var stamp = WorkflowPackageStamp.Compute(manifest, V5Fixtures.Files(manifest), Catalog, errors);

        Assert.Empty(errors);
        Assert.Equal(Catalog.ResolvedRef, stamp.CatalogRef);
        Assert.StartsWith("output-contracts@v", stamp.CatalogRef);
        Assert.Equal(new Dictionary<string, string> { ["concept-list"] = "concept-list" }, stamp.Contracts);
    }

    [Fact]
    public void Compute_NoSchemas_IsAnEmptyMapUnderTheCatalogRef()
    {
        // The catalog ref is still evidence: it says what the package was
        // validated against, schemas or not.
        var manifest = V9Fixtures.Minimal() with { Schemas = null };
        var errors = new List<string>();

        var stamp = WorkflowPackageStamp.Compute(manifest, new Dictionary<string, string>(), Catalog, errors);

        Assert.Empty(errors);
        Assert.Equal(Catalog.ResolvedRef, stamp.CatalogRef);
        Assert.Empty(stamp.Contracts);
    }

    [Fact]
    public void Compute_ADeclaredSchemaMatchingNothing_IsAnError_NotAThrow()
    {
        // Every declared schema, not only the ones a node references: the
        // validator closes the referenced ones, and an unreferenced one that
        // matched nothing used to publish and strand at load.
        var manifest = V5Fixtures.Manifest();
        var schemas = new Dictionary<string, string>(manifest.Schemas!) { ["extra"] = "schemas/extra.json" };
        manifest = manifest with { Schemas = schemas };
        var files = V5Fixtures.Files(manifest);
        files["schemas/extra.json"] = DriftedSchema();
        var errors = new List<string>();

        var stamp = WorkflowPackageStamp.Compute(manifest, files, Catalog, errors);

        Assert.Equal(new[] { $"Schema 'extra' matches no contract in {Catalog.ResolvedRef}." }, errors);
        Assert.Equal(new[] { "concept-list" }, stamp.Contracts.Keys);
    }

    [Fact]
    public void Compute_AMissingOrUnparseableSchemaFile_IsAnError()
    {
        var manifest = V5Fixtures.Manifest();
        var missing = V5Fixtures.Files(manifest);
        missing.Remove(V5Fixtures.SchemaPath);
        var errors = new List<string>();
        WorkflowPackageStamp.Compute(manifest, missing, Catalog, errors);
        Assert.Equal(new[] { $"Schema 'concept-list' file '{V5Fixtures.SchemaPath}' is missing from the package." }, errors);

        var unparseable = V5Fixtures.Files(manifest);
        unparseable[V5Fixtures.SchemaPath] = "{ not json";
        errors.Clear();
        WorkflowPackageStamp.Compute(manifest, unparseable, Catalog, errors);
        Assert.Single(errors);
        Assert.StartsWith("Schema 'concept-list' does not parse as JSON:", errors[0]);
    }

    [Fact]
    public void ToJson_IsTheDocumentedBytes_WithContractsInOrdinalOrder()
    {
        var stamp = new WorkflowPackageStamp(
            "output-contracts@v2026.07.2",
            new Dictionary<string, string> { ["problem-list"] = "problem-list", ["concept-list"] = "concept-list" });

        Assert.Equal(
            "{\n  \"catalogRef\": \"output-contracts@v2026.07.2\",\n  \"contracts\": {\n    \"concept-list\": \"concept-list\",\n    \"problem-list\": \"problem-list\"\n  }\n}\n",
            stamp.ToJson());
    }

    [Fact]
    public void ToJson_EmptyContracts_IsTheDocumentedBytes()
    {
        var stamp = new WorkflowPackageStamp("output-contracts@v2026.07.2", new Dictionary<string, string>());

        Assert.Equal("{\n  \"catalogRef\": \"output-contracts@v2026.07.2\",\n  \"contracts\": {}\n}\n", stamp.ToJson());
    }

    [Fact]
    public void Read_RoundTripsToJson_ByteStable()
    {
        var original = new WorkflowPackageStamp(
            "output-contracts@v2026.07.2",
            new Dictionary<string, string> { ["concept-list"] = "concept-list" }).ToJson();

        var read = WorkflowPackageStamp.Read(original, "general@v2026.08.1");

        Assert.Equal("output-contracts@v2026.07.2", read.CatalogRef);
        Assert.Equal("concept-list", read.Contracts["concept-list"]);
        Assert.Equal(original, read.ToJson());
    }

    [Theory]
    [InlineData("""{ "catalogRef": "output-contracts@v2026.07.2", "contracts": {}, "publishedBy": "x" }""",
        "Workflow package general@v2026.08.1 publish.json property '$.publishedBy' is not part of the publication stamp.")]
    [InlineData("""{ "catalogRef": "output-contracts@latest", "contracts": {} }""",
        "Workflow package general@v2026.08.1 publish.json must declare catalogRef as output-contracts@vYYYY.MM.N.")]
    [InlineData("""{ "catalogRef": "general@v2026.07.2", "contracts": {} }""",
        "Workflow package general@v2026.08.1 publish.json must declare catalogRef as output-contracts@vYYYY.MM.N.")]
    [InlineData("""{ "contracts": {} }""",
        "Workflow package general@v2026.08.1 publish.json must declare catalogRef as output-contracts@vYYYY.MM.N.")]
    [InlineData("""{ "catalogRef": "output-contracts@v2026.07.2" }""",
        "Workflow package general@v2026.08.1 publish.json must declare contracts (schema id to contract id; empty when the package declares no schemas).")]
    [InlineData("""{ "catalogRef": "output-contracts@v2026.07.2", "contracts": { "concept-list": " " } }""",
        "Workflow package general@v2026.08.1 publish.json contract for schema 'concept-list' is blank.")]
    [InlineData("{ not json", "Workflow package general@v2026.08.1 publish.json is not valid JSON.")]
    public void Read_RefusesWhatItCannotTrust_ByName(string json, string expected)
    {
        var exception = Assert.Throws<WorkflowPackageContentException>(() => WorkflowPackageStamp.Read(json, "general@v2026.08.1"));

        Assert.Equal(expected, exception.Message);
    }

    [Fact]
    public void TheStampedStrandingSentence_NamesBothCatalogs()
    {
        var message = WorkflowPackageContentException.StampedContractUnknown(
            "general@v2026.08.1", "concept-list", "concept-list", "output-contracts@v2026.07.2", "output-contracts@v2026.09.1").Message;

        Assert.Equal(
            "Workflow package general@v2026.08.1 schema 'concept-list' was published as contract 'concept-list' under "
            + "output-contracts@v2026.07.2, which output-contracts@v2026.09.1 no longer carries. The package is unchanged and immutable; the catalog moved.",
            message);
    }

    [Fact]
    public void TheIncompleteStampSentence_NamesTheSchema()
    {
        Assert.Equal(
            "Workflow package general@v2026.08.1 schema 'concept-list' has no contract in its publication stamp "
            + "(output-contracts@v2026.07.2). The stamp was written at publish and is incomplete.",
            WorkflowPackageContentException.StampIncomplete("general@v2026.08.1", "concept-list", "output-contracts@v2026.07.2").Message);
    }

    // ----- the validator's closure, satisfied by a stamp --------------------

    [Fact]
    public void Validate_WithAStampCoveringTheSchema_SkipsTheMatch_ButStillRequiresParseAndFile()
    {
        var manifest = V5Fixtures.Manifest();
        var stamped = new Dictionary<string, string> { ["concept-list"] = "concept-list" };

        // Drifted text matches nothing — refused unstamped, accepted stamped.
        var drifted = V5Fixtures.Files(manifest);
        drifted[V5Fixtures.SchemaPath] = DriftedSchema();
        Assert.Contains(
            "Schema 'concept-list' must canonically match a catalog output contract (modulo title/description).",
            WorkflowPackageValidator.Validate(manifest, drifted, TestOutputContracts.CatalogSchemas).Errors);
        var withStamp = WorkflowPackageValidator.Validate(manifest, drifted, TestOutputContracts.CatalogSchemas, stamped);
        Assert.True(withStamp.IsValid, string.Join(" | ", withStamp.Errors));

        // The stamp is evidence about the match, not about the file.
        var missing = V5Fixtures.Files(manifest);
        missing.Remove(V5Fixtures.SchemaPath);
        Assert.Contains(
            $"Schema 'concept-list' file '{V5Fixtures.SchemaPath}' is missing from the package.",
            WorkflowPackageValidator.Validate(manifest, missing, TestOutputContracts.CatalogSchemas, stamped).Errors);

        var unparseable = V5Fixtures.Files(manifest);
        unparseable[V5Fixtures.SchemaPath] = "{ not json";
        Assert.Contains(
            WorkflowPackageValidator.Validate(manifest, unparseable, TestOutputContracts.CatalogSchemas, stamped).Errors,
            error => error.StartsWith("Schema 'concept-list' does not parse as JSON:", StringComparison.Ordinal));

        // A stamp covering some other id says nothing about this one.
        Assert.Contains(
            "Schema 'concept-list' must canonically match a catalog output contract (modulo title/description).",
            WorkflowPackageValidator.Validate(manifest, drifted, TestOutputContracts.CatalogSchemas,
                new Dictionary<string, string> { ["other"] = "concept-list" }).Errors);
    }
}
