using Consultologist.Api.Agents;
using Consultologist.Api.Workflow;

using Consultologist.PackageFormat;
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
    public void Compute_RecordsAReferencedSchema_ByItsNamedContract_AndTheStampShapeIsUnchanged()
    {
        // v21 (#923): a referenced schema names its catalog contract directly —
        // Compute records it with no body to load or match, and the stamp byte
        // shape stays {catalogRef, contracts} with no new member.
        var (manifest, files) = V21Fixtures.SchemaRef();
        var errors = new List<string>();

        var stamp = WorkflowPackageStamp.Compute(manifest, files, Catalog, errors);

        Assert.Empty(errors);
        Assert.Equal(new Dictionary<string, string> { ["concept-list"] = "concept-list" }, stamp.Contracts);
        Assert.Contains("\"contracts\": {", stamp.ToJson());
        Assert.DoesNotContain("schemaRef", stamp.ToJson());
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

    // ----- v25 (B2 #1001): the formats members --------------------------------------

    [Fact]
    public void A25Package_StampsTheFormatsCatalogue_AndEveryFormatItNames()
    {
        var (manifest, files) = V25Package.Minimal();
        var formats = V25Package.Catalog();
        var errors = new List<string>();

        var stamp = WorkflowPackageStamp.Compute(manifest, files, Catalog, formats, errors);

        Assert.Empty(errors);
        Assert.Equal(Catalog.ResolvedRef, stamp.CatalogRef);
        Assert.Empty(stamp.Contracts);
        Assert.Equal("formats@v2026.11.1", stamp.FormatsRef);
        Assert.Equal(new[] { "classification", "concept_list", "prose", "terms_subset" }, stamp.Formats!.Keys.Order());

        var prose = stamp.Formats["prose"];
        Assert.Equal("attested", prose.Producer);
        Assert.Equal("consult-prose", prose.AgentName); // the fixture catalogue (the design's example)
        Assert.Equal(3, prose.AgentVersion);
        Assert.Null(prose.Op);
        Assert.Equal(WorkflowFormatSpecJson.Sha256Hex(formats.Formats["prose"].CanonicalJson), prose.FormatHash);
        Assert.Equal(64, prose.FormatHash.Length);

        var check = stamp.Formats["terms_subset"];
        Assert.Equal("computed", check.Producer);
        Assert.Equal("terms_subset", check.Op);
        Assert.Null(check.AgentName);

        // concept_list rides in through terms_subset's parameters, not a node.
        Assert.DoesNotContain(manifest.Nodes!, n => n.Format == "concept_list" && n.Id == "coverage");
        Assert.Equal("concept-extraction", stamp.Formats["concept_list"].AgentName);
    }

    [Fact]
    public void A25Package_StampsALocalFormat_AsCustom_WithTheGenericAgent()
    {
        var (manifest, files) = V25Package.Minimal();
        manifest = manifest with
        {
            Formats = new List<WorkflowFormatReference> { new("medication_list", "formats/medication_list.json") },
            Nodes = manifest.Nodes!.Append(new WorkflowNodeSpec("meds", "Meds", Prompt: "extract", Format: "medication_list", OnInvalid: "fail")).ToList(),
        };
        files["formats/medication_list.json"] = FormatFixtures.LocalCustom;
        var errors = new List<string>();

        var stamp = WorkflowPackageStamp.Compute(manifest, files, Catalog, V25Package.Catalog(), errors);

        Assert.Empty(errors);
        var meds = stamp.Formats!["medication_list"];
        Assert.Equal("custom", meds.Producer);
        Assert.Equal("custom-json", meds.AgentName);
        Assert.Equal(1, meds.AgentVersion);
        Assert.Equal(WorkflowFormatSpecJson.Sha256Hex(WorkflowFormatSpecJson.CanonicalJson(FormatFixtures.LocalCustom)), meds.FormatHash);
    }

    [Fact]
    public void A25Package_WithoutACatalogue_CannotBeStamped()
    {
        var (manifest, files) = V25Package.Minimal();
        var errors = new List<string>();

        WorkflowPackageStamp.Compute(manifest, files, Catalog, null, errors);

        Assert.Equal(new[] { "The format catalogue is not available on this engine; a specVersion 25 package cannot be stamped." }, errors);
    }

    [Fact]
    public void A24Stamp_IsByteIdentical_WithOrWithoutTheCatalogue()
    {
        var (manifest, files) = V24Fixtures.Base();
        var errors = new List<string>();

        var without = WorkflowPackageStamp.Compute(manifest, files, Catalog, errors).ToJson();
        var with = WorkflowPackageStamp.Compute(manifest, files, Catalog, V25Package.Catalog(), errors).ToJson();

        Assert.Empty(errors);
        Assert.Equal(without, with);
        Assert.DoesNotContain("formats", with);
    }

    [Fact]
    public void A25Stamp_ToJson_IsTheDocumentedBytes_AndRoundTrips()
    {
        var stamp = new WorkflowPackageStamp(
            "output-contracts@v2026.07.2",
            new Dictionary<string, string>(),
            "formats@v2026.10.1",
            new Dictionary<string, WorkflowStampedFormat>
            {
                ["terms_subset"] = new(new string('b', 64), "computed", Op: "terms_subset"),
                ["prose"] = new(new string('a', 64), "attested", "test-json", 48),
            });

        var json = stamp.ToJson();

        Assert.Equal(
            "{\n  \"catalogRef\": \"output-contracts@v2026.07.2\",\n  \"contracts\": {},\n  \"formatsRef\": \"formats@v2026.10.1\",\n  \"formats\": {\n    \"prose\": {\n      \"formatHash\": \"" + new string('a', 64) + "\",\n      \"producer\": \"attested\",\n      \"agentName\": \"test-json\",\n      \"agentVersion\": 48\n    },\n    \"terms_subset\": {\n      \"formatHash\": \"" + new string('b', 64) + "\",\n      \"producer\": \"computed\",\n      \"op\": \"terms_subset\"\n    }\n  }\n}\n",
            json);

        var read = WorkflowPackageStamp.Read(json, "general@v2026.11.1");
        Assert.Equal("formats@v2026.10.1", read.FormatsRef);
        Assert.Equal(48, read.Formats!["prose"].AgentVersion);
        Assert.Equal(json, read.ToJson());
    }

    [Theory]
    [InlineData("""{ "catalogRef": "output-contracts@v2026.07.2", "contracts": {}, "formatsRef": "formats@latest", "formats": {} }""",
        "Workflow package general@v2026.11.1 publish.json must declare formatsRef as formats@vYYYY.MM.N.")]
    [InlineData("""{ "catalogRef": "output-contracts@v2026.07.2", "contracts": {}, "formatsRef": "output-contracts@v2026.10.1", "formats": {} }""",
        "Workflow package general@v2026.11.1 publish.json must declare formatsRef as formats@vYYYY.MM.N.")]
    [InlineData("""{ "catalogRef": "output-contracts@v2026.07.2", "contracts": {}, "formatsRef": "formats@v2026.10.1" }""",
        "Workflow package general@v2026.11.1 publish.json must declare formatsRef and formats together (a specVersion 25 publish writes both).")]
    [InlineData("""{ "catalogRef": "output-contracts@v2026.07.2", "contracts": {}, "formats": {} }""",
        "Workflow package general@v2026.11.1 publish.json must declare formatsRef and formats together (a specVersion 25 publish writes both).")]
    [InlineData("""{ "catalogRef": "output-contracts@v2026.07.2", "contracts": {}, "formatsRef": "formats@v2026.10.1", "formats": { "prose": { "formatHash": "abc", "producer": "attested", "agentName": "a", "agentVersion": 1 } } }""",
        "Workflow package general@v2026.11.1 publish.json format 'prose' must carry a formatHash (64 hex characters).")]
    [InlineData("""{ "catalogRef": "output-contracts@v2026.07.2", "contracts": {}, "formatsRef": "formats@v2026.10.1", "formats": { "prose": { "formatHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "producer": "magic" } } }""",
        "Workflow package general@v2026.11.1 publish.json format 'prose' producer 'magic' is not one of attested, custom, rendered, computed.")]
    [InlineData("""{ "catalogRef": "output-contracts@v2026.07.2", "contracts": {}, "formatsRef": "formats@v2026.10.1", "formats": { "prose": { "formatHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "producer": "attested", "agentName": "a" } } }""",
        "Workflow package general@v2026.11.1 publish.json format 'prose' must carry agentName and agentVersion together.")]
    [InlineData("""{ "catalogRef": "output-contracts@v2026.07.2", "contracts": {}, "formatsRef": "formats@v2026.10.1", "formats": { "prose": { "formatHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "producer": "attested", "agentName": "a", "agentVersion": 1, "op": "x" } } }""",
        "Workflow package general@v2026.11.1 publish.json format 'prose' names both an agent and an op.")]
    [InlineData("""{ "catalogRef": "output-contracts@v2026.07.2", "contracts": {}, "formatsRef": "formats@v2026.10.1", "formats": { "prose": { "formatHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "producer": "attested", "agentName": "a", "agentVersion": "1" } } }""",
        "Workflow package general@v2026.11.1 publish.json property '$.formats.prose.agentVersion' is not part of the publication stamp.")]
    public void Read_RefusesTheFormatsMembers_ByName(string json, string expected)
    {
        var exception = Assert.Throws<WorkflowPackageContentException>(() => WorkflowPackageStamp.Read(json, "general@v2026.11.1"));

        Assert.Equal(expected, exception.Message);
    }
}
