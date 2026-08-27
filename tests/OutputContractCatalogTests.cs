using System.Text.Json.Nodes;
using Consultologist.Api.Agents;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Tests;

public class OutputContractCatalogTests
{
    private static string RepoAgentsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, "external", "consultologist-agents", "agents");
    }

    [Fact]
    public void Load_RealCatalog_HasTextAndConceptListEntries()
    {
        var catalog = OutputContractCatalog.Load(RepoAgentsDirectory());

        var text = catalog.GetEntry(OutputContracts.Text);
        Assert.False(string.IsNullOrWhiteSpace(text.AgentName));
        Assert.False(string.IsNullOrWhiteSpace(text.AgentVersion));
        Assert.Null(text.SchemaJson);

        var conceptList = catalog.GetEntry(OutputContracts.ConceptList);
        Assert.False(string.IsNullOrWhiteSpace(conceptList.AgentName));
        Assert.False(string.IsNullOrWhiteSpace(conceptList.AgentVersion));
        Assert.NotNull(conceptList.SchemaJson);
    }

    [Fact]
    public void Load_RealCatalog_ConceptListSchemaMatchesAgentManifest()
    {
        // The catalog's schema file and the schema welded into the pinned agent's
        // published definition must be the same document: the agent enforces at
        // generation time exactly the shape the catalog claims for the contract.
        var agentsDir = RepoAgentsDirectory();
        var catalog = OutputContractCatalog.Load(agentsDir);
        var entry = catalog.GetEntry(OutputContracts.ConceptList);

        var manifest = AttestedAgentManifest.Load(
            File.ReadAllText(Path.Combine(agentsDir, $"{entry.AgentName}.yaml")));

        Assert.Equal(entry.AgentVersion, manifest.Version);
        Assert.Equal("json_schema", manifest.Definition.Text?.Format?.Type);
        Assert.Equal("concept_list", manifest.Definition.Text?.Format?.Name);
        Assert.Equal("true", manifest.Definition.Text?.Format?.Strict);

        var manifestSchema = JsonNode.Parse(manifest.Definition.Text!.Format!.Schema!)!;
        var catalogSchema = JsonNode.Parse(entry.SchemaJson!)!;

        Assert.Equal(catalogSchema.ToJsonString(), manifestSchema.ToJsonString());
    }

    // v10 (#495): the third contract, with its own attested agent.
    [Fact]
    public void Load_RealCatalog_HasAClassificationEntry_WhoseSchemaMatchesItsAgent()
    {
        var catalog = OutputContractCatalog.Load(RepoAgentsDirectory());

        var entry = catalog.GetEntry(OutputContracts.Classification);
        Assert.Equal("classification", entry.AgentName);
        Assert.NotNull(entry.SchemaJson);

        var manifest = AttestedAgentManifest.Load(File.ReadAllText(Path.Combine(RepoAgentsDirectory(), "classification.yaml")));
        Assert.Equal(entry.AgentVersion, manifest.Version);
        Assert.Equal("json_schema", manifest.Definition.Text!.Format!.Type);
        Assert.Equal("classification", manifest.Definition.Text.Format.Name);
        Assert.Equal("true", manifest.Definition.Text.Format.Strict);
        Assert.Equal(
            System.Text.Json.Nodes.JsonNode.Parse(entry.SchemaJson!)!.ToJsonString(),
            System.Text.Json.Nodes.JsonNode.Parse(manifest.Definition.Text.Format.Schema!)!.ToJsonString());

        // The one shape a package may not declare: an object with one string value.
        Assert.True(catalog.TryResolveContract(System.Text.Json.Nodes.JsonNode.Parse("""{"type":"object","additionalProperties":false,"required":["value"],"properties":{"value":{"type":"string"}}}"""), out var id));
        Assert.Equal(OutputContracts.Classification, id);
    }

    [Fact]
    public void TryResolveContract_MatchesModuloTitleAndDescription()
    {
        var catalog = OutputContractCatalog.Load(RepoAgentsDirectory());
        var schema = JsonNode.Parse(catalog.GetEntry(OutputContracts.ConceptList).SchemaJson!)!.AsObject();
        schema["title"] = "Concept list";
        schema["description"] = "Authored copy";

        Assert.True(catalog.TryResolveContract(schema, out var contractId));
        Assert.Equal(OutputContracts.ConceptList, contractId);
    }

    [Fact]
    public void TryResolveContract_RejectsPropertyDrift()
    {
        var catalog = OutputContractCatalog.Load(RepoAgentsDirectory());
        var drifted = JsonNode.Parse(
            catalog.GetEntry(OutputContracts.ConceptList).SchemaJson!.Replace("\"isActive\"", "\"active\""))!;

        Assert.False(catalog.TryResolveContract(drifted, out _));
    }

    [Fact]
    public void GetEntry_UnknownContract_Throws()
    {
        var catalog = OutputContractCatalog.Load(RepoAgentsDirectory());

        var ex = Assert.Throws<InvalidOperationException>(() => catalog.GetEntry("unknown-shape"));
        Assert.Contains("unknown-shape", ex.Message);
    }

    [Theory]
    [InlineData(null, "catalog not found")]
    [InlineData("not json", "not valid JSON")]
    [InlineData("""{ "contracts": {} }""", "declares no contracts")]
    [InlineData("""{ "contracts": { "text": { "agentName": "a", "agentVersion": "1" } } }""", "must declare a CalVer version")]
    [InlineData("""{ "version": "2026.07", "contracts": { "text": { "agentName": "a", "agentVersion": "1" } } }""", "must declare a CalVer version")]
    [InlineData("""{ "version": "v2026.07.1", "contracts": { "text": { "agentName": "a" } } }""", "agentName and agentVersion")]
    [InlineData("""{ "version": "v2026.07.1", "contracts": { "concept-list": { "agentName": "a", "agentVersion": "1" } } }""", "must declare the 'text' entry")]
    [InlineData("""{ "version": "v2026.07.1", "contracts": { "text": { "agentName": "a", "agentVersion": "1", "schemaFile": "missing.json" } } }""", "missing schema file")]
    public void Load_DefectiveCatalog_FailsFast(string? catalogJson, string expectedMessagePart)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"catalog-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        try
        {
            if (catalogJson != null)
            {
                File.WriteAllText(Path.Combine(dir, "output-contracts.json"), catalogJson);
            }

            var ex = Assert.Throws<InvalidOperationException>(() => OutputContractCatalog.Load(dir));
            Assert.Contains(expectedMessagePart, ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

public class CatalogRegistryIdentityTests
{
    private static string RepoAgents()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, "external", "consultologist-agents", "agents");
    }

    private const string MinimalCatalog = """
        { "version": "v2026.07.1", "contracts": { "text": { "agentName": "a", "agentVersion": "1" } } }
        """;

    [Fact]
    public void Load_ExposesConcreteResolvedRef()
    {
        var catalog = OutputContractCatalog.Load(RepoAgents());

        Assert.StartsWith("output-contracts@v", catalog.ResolvedRef);
        Assert.True(Consultologist.PackageFormat.CalVerVersion.TryParse(
            catalog.ResolvedRef["output-contracts@".Length..], out _));
    }

    [Fact]
    public void CompareCatalogToBundled_IdenticalCatalogs_NoMismatches()
    {
        var runtime = OutputContractCatalog.Build(MinimalCatalog, _ => null, "test");
        var bundled = OutputContractCatalog.Build(MinimalCatalog, _ => null, "test");

        Assert.Empty(AgentAttestationService.CompareCatalogToBundled(runtime, bundled));
    }

    [Fact]
    public void CompareCatalogToBundled_VersionDrift_Fails()
    {
        var runtime = OutputContractCatalog.Build(MinimalCatalog, _ => null, "test");
        var bundled = OutputContractCatalog.Build(MinimalCatalog.Replace("v2026.07.1", "v2026.07.2"), _ => null, "test");

        Assert.Contains(
            AgentAttestationService.CompareCatalogToBundled(runtime, bundled),
            m => m.Contains("publish/pin/deploy drift", StringComparison.Ordinal));
    }

    [Fact]
    public void CompareCatalogToBundled_PinDrift_Fails()
    {
        var runtime = OutputContractCatalog.Build(MinimalCatalog, _ => null, "test");
        var bundled = OutputContractCatalog.Build(MinimalCatalog.Replace("\"agentVersion\": \"1\"", "\"agentVersion\": \"2\""), _ => null, "test");

        Assert.Contains(
            AgentAttestationService.CompareCatalogToBundled(runtime, bundled),
            m => m.Contains("pins a@1 (runtime) != a@2 (bundled)", StringComparison.Ordinal));
    }

    [Fact]
    public void CompareCatalogToBundled_MissingEntry_Fails()
    {
        var runtime = OutputContractCatalog.Build(MinimalCatalog, _ => null, "test");
        var bundled = OutputContractCatalog.Build(
            """{ "version": "v2026.07.1", "contracts": { "text": { "agentName": "a", "agentVersion": "1" }, "extra": { "agentName": "b", "agentVersion": "3" } } }""",
            _ => null,
            "test");

        Assert.Contains(
            AgentAttestationService.CompareCatalogToBundled(runtime, bundled),
            m => m.Contains("'extra' is not in the runtime catalog", StringComparison.Ordinal));
    }

}
