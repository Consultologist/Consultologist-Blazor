using System.Text.Json.Nodes;
using Consultologist.Api.Agents;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.Api.V31;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 § 1.2 step 4; part 07 §§ 3.1, 4.4; D2): what this engine runs, as
/// <see cref="RequiresCheck.Engine"/> holds a package to it: the evaluator, Scriban and the locked libraries as loaded,
/// the operations and built-ins at their versions, the kinds and features this build runs, the attested agents the
/// engine's catalogues name, and no break points.
/// </summary>
public class EngineCapabilitiesFactoryTests
{
    private static EngineCapabilities Current() => V31Job.Factory().Current();

    [Theory]
    [MemberData(nameof(CompiledGoldenTests.Names), MemberType = typeof(CompiledGoldenTests))]
    public void TheGoldens_AreAdmitted(string name)
    {
        var pair = CompiledGoldenTests.Published(CompiledGoldenTests.Cases().Single(c => c.Name == name));
        var resolve = JsonNode.Parse(pair.Resolve)!.AsObject();
        var engine = Current();

        // The agents the golden binds are among the engine's.
        foreach (var (agent, version) in resolve["requires"]!["agents"]!.AsObject())
            Assert.Contains(int.Parse((string)version!, System.Globalization.CultureInfo.InvariantCulture), engine.Agents[agent]);

        var refusals = RequiresCheck.Engine(resolve, JsonNode.Parse(pair.Lock)!.AsObject(), engine);

        Assert.True(refusals.Count == 0, string.Join(" | ", refusals.Select(r => $"{r.Id} {r.Sentence}")));
    }

    [Fact]
    public void Kinds_AndFeatures_AreTheContracts()
    {
        var engine = Current();
        var kinds = V31Contract.Json("constants.json")["kinds"]!.AsArray().Select(k => (string)k!).ToList();
        var features = V31Contract.Json("constants.json")["features"]!.AsArray().Select(f => (string)f!).ToList();

        Assert.Equal(kinds.Order(StringComparer.Ordinal), engine.Kinds.Order(StringComparer.Ordinal));

        // Every feature of the contract is run here, or named as not run with its reason; never both, nothing else.
        // #1504 (area 07; D15): parts, macro markers and run spots are run, so nothing is left unrun.
        Assert.Equal(["parallel", "check-loops", "parts", "macro-markers", "run-spots"], EngineCapabilitiesFactory.Features);
        Assert.Empty(EngineCapabilitiesFactory.FeaturesNotRun);
        Assert.Empty(engine.Features.Intersect(EngineCapabilitiesFactory.FeaturesNotRun.Keys));
        Assert.Equal(
            features.Order(StringComparer.Ordinal),
            engine.Features.Concat(EngineCapabilitiesFactory.FeaturesNotRun.Keys).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheVersions_AreThisBuilds()
    {
        var engine = Current();
        var toolchain = Toolchain.Current("package-format@test", "formats@test", "agents@test");
        var constants = V31Contract.Json("constants.json");

        Assert.Equal(JsonLogicOperations.EvaluatorVersion, engine.Evaluator);
        Assert.Equal(toolchain.Scriban, engine.Scriban);
        Assert.Equal(
            toolchain.Libraries.Where(l => l.Key != "Scriban").OrderBy(l => l.Key, StringComparer.Ordinal),
            engine.Libraries.OrderBy(l => l.Key, StringComparer.Ordinal));
        Assert.Equal(
            constants["operations"]!.AsObject().Select(o => KeyValuePair.Create(o.Key, (string)o.Value!)).OrderBy(o => o.Key, StringComparer.Ordinal),
            engine.Operations.OrderBy(o => o.Key, StringComparer.Ordinal));
        Assert.Equal(
            constants["builtins"]!.AsObject().Select(b => KeyValuePair.Create(b.Key, (string)b.Value!)).OrderBy(b => b.Key, StringComparer.Ordinal),
            engine.Builtins.OrderBy(b => b.Key, StringComparer.Ordinal));
        Assert.Empty(engine.BreakPoints);
        Assert.Same(EngineBreakPoints.All, engine.BreakPoints);
    }

    [Fact]
    public void TheAgents_AreTheAttestedCatalogues()
    {
        var contracts = OutputContractCatalog.Load();
        var formats = FormatCatalogLoader.Load();
        var v31 = V31AgentsCatalogue.Load();

        var agents = new EngineCapabilitiesFactory(contracts, formats, v31).Current().Agents;

        foreach (var entry in contracts.Entries.Values)
            Assert.Contains(int.Parse(entry.AgentVersion, System.Globalization.CultureInfo.InvariantCulture), agents[entry.AgentName]);
        foreach (var format in formats.Formats.Values.Where(f => f.Entry.AgentName != null))
            Assert.Contains(format.Entry.AgentVersion!.Value, agents[format.Entry.AgentName!]);
        Assert.Contains(formats.CustomProducer.AgentVersion, agents[formats.CustomProducer.AgentName]);
        foreach (var (name, version) in v31.Agents)
            Assert.Contains(version, agents[name]);
        Assert.Equal(
            contracts.Entries.Values.Select(e => e.AgentName)
                .Concat(formats.Formats.Values.Select(f => f.Entry.AgentName).OfType<string>())
                .Append(formats.CustomProducer.AgentName)
                .Concat(v31.Agents.Select(a => a.Name))
                .Distinct().Order(StringComparer.Ordinal),
            agents.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheEngine_ListsThe31CataloguesAgents()
    {
        // #1504 ruling 3 (D9): the 31 agents catalogue is a source of the engine's agents; judgement exists only there.
        var thirty = new EngineCapabilitiesFactory(OutputContractCatalog.Load(), FormatCatalogLoader.Load(), V31AgentsCatalogue.Empty).Current();
        Assert.False(thirty.Agents.ContainsKey("judgement"));

        var engine = Current();

        Assert.Contains(1, engine.Agents["judgement"]);
        Assert.Equal(
            [("classification", 1), ("concept-extraction", 1), ("custom-json", 1), ("judgement", 1), ("test-json", 48)],
            V31AgentsCatalogue.Load().Agents.OrderBy(a => a.Name, StringComparer.Ordinal));
    }

    [Fact]
    public void TheCatalogue_GivesEachAgentsStrictRejects()
    {
        var catalogue = V31AgentsCatalogue.Load();

        Assert.Equal("v2026.12.2", catalogue.Version);
        Assert.Equal(["format"], catalogue.StrictRejects("judgement", 1));
        Assert.Equal(["format"], catalogue.StrictRejects("test-json", 48));
        Assert.Empty(catalogue.StrictRejects("judgement", 2));
        Assert.Empty(catalogue.StrictRejects("prose-writer", 1));
    }

    [Fact]
    public void ACatalogueWhoseAgentHasNoManifestAtItsVersion_StopsTheLoad()
    {
        // attested: each agent version of agents.json is its bundled manifest's (agents/<name>.yaml); else the engine
        // does not start, as for the catalogues it already loads
        var folder = Directory.CreateTempSubdirectory("v31-agents-").FullName;
        try
        {
            var bundled = OutputContractCatalog.ResolveDirectory();
            Directory.CreateDirectory(Path.Combine(folder, "v31"));
            foreach (var file in new[] { "bindings.json", "agents.json" })
                File.Copy(Path.Combine(bundled, "v31", file), Path.Combine(folder, "v31", file));
            foreach (var yaml in Directory.GetFiles(bundled, "*.yaml"))
                File.Copy(yaml, Path.Combine(folder, Path.GetFileName(yaml)));
            Assert.Contains(("judgement", 1), V31AgentsCatalogue.Load(folder).Agents);

            File.WriteAllText(Path.Combine(folder, "judgement.yaml"),
                File.ReadAllText(Path.Combine(bundled, "judgement.yaml")).Replace("version: \"1\"", "version: \"2\"", StringComparison.Ordinal));
            Assert.Contains("judgement", Assert.Throws<InvalidOperationException>(() => V31AgentsCatalogue.Load(folder)).Message);

            File.Delete(Path.Combine(folder, "judgement.yaml"));
            Assert.Contains("judgement", Assert.Throws<InvalidOperationException>(() => V31AgentsCatalogue.Load(folder)).Message);

            File.Delete(Path.Combine(folder, "v31", "agents.json"));
            Assert.Throws<InvalidOperationException>(() => V31AgentsCatalogue.Load(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task TheEngine_RunsPartsMacroMarkersAndRunSpots()
    {
        // A package needing each of the three features loads through the store: none is refused S07-004.
        var registry = new FakeRegistry();
        var (packageRef, pair) = registry.Put(AgentTurnActivityTests.MacrosGolden);
        var features = JsonNode.Parse(pair.Resolve)!["requires"]!["features"]!.AsArray().Select(f => (string)f!).ToList();
        Assert.Superset(new HashSet<string>(["parts", "macro-markers", "run-spots"]), features.ToHashSet());

        var load = await V31Job.StoreOver(registry).TryLoadAsync(packageRef, CancellationToken.None);

        Assert.True(load!.Plan != null, $"{load.RefusalId} {load.OperatorDetail}");
        Assert.Null(load.RefusalId);
    }

    [Fact]
    public async Task APackageBindingJudgement1_Loads_AndIsNotRefusedS07005()
    {
        // #1504 ruling 3: an agent that exists only in agents/v31 (judgement) was refused S07-005 before the 31 catalogue
        // was a source of the engine's agents.
        var registry = new FakeRegistry();
        var (packageRef, pair) = registry.Put(AgentTurnActivityTests.MacrosGolden);
        Assert.Equal("1", (string?)JsonNode.Parse(pair.Resolve)!["requires"]!["agents"]!["judgement"]);

        var load = await V31Job.StoreOver(registry).TryLoadAsync(packageRef, CancellationToken.None);
        Assert.True(load!.Plan != null, $"{load.RefusalId} {load.OperatorDetail}");

        var thirty = new EngineCapabilitiesFactory(OutputContractCatalog.Load(), FormatCatalogLoader.Load(), V31AgentsCatalogue.Empty).Current();
        Assert.Contains(RequiresCheck.Engine(JsonNode.Parse(pair.Resolve)!.AsObject(), JsonNode.Parse(pair.Lock)!.AsObject(), thirty),
            r => r.Id == "S07-005");
    }
}
