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
    private static EngineCapabilities Current() =>
        new EngineCapabilitiesFactory(OutputContractCatalog.Load(), FormatCatalogLoader.Load()).Current();

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
        Assert.Equal(["parallel", "check-loops"], EngineCapabilitiesFactory.Features);
        Assert.Equal(["macro-markers", "parts", "run-spots"], EngineCapabilitiesFactory.FeaturesNotRun.Keys.Order(StringComparer.Ordinal));
        Assert.All(EngineCapabilitiesFactory.FeaturesNotRun.Values, reason => Assert.False(string.IsNullOrWhiteSpace(reason)));
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

        var agents = new EngineCapabilitiesFactory(contracts, formats).Current().Agents;

        foreach (var entry in contracts.Entries.Values)
            Assert.Contains(int.Parse(entry.AgentVersion, System.Globalization.CultureInfo.InvariantCulture), agents[entry.AgentName]);
        foreach (var format in formats.Formats.Values.Where(f => f.Entry.AgentName != null))
            Assert.Contains(format.Entry.AgentVersion!.Value, agents[format.Entry.AgentName!]);
        Assert.Contains(formats.CustomProducer.AgentVersion, agents[formats.CustomProducer.AgentName]);
        Assert.Equal(
            contracts.Entries.Values.Select(e => e.AgentName)
                .Concat(formats.Formats.Values.Select(f => f.Entry.AgentName).OfType<string>())
                .Append(formats.CustomProducer.AgentName)
                .Distinct().Order(StringComparer.Ordinal),
            agents.Keys.Order(StringComparer.Ordinal));
    }
}
