using System.Globalization;
using Consultologist.Api.Agents;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.V31;

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 § 1.2 step 4; part 07 §§ 3.1, 4.4): what this engine runs, as
/// <see cref="RequiresCheck.Engine"/> holds a 31 package to it. The evaluator's version; Scriban and the other locked
/// libraries as their loaded assemblies say (<see cref="Toolchain.Current"/>'s reading); each custom operation and each
/// built-in at the version this build writes into <c>lock.json</c>; the kinds and features this build runs; the attested
/// agents of the catalogues the engine loads and attests at startup (the output contracts' and the formats catalogue's,
/// its custom producer included); and <see cref="EngineBreakPoints.All"/>.
/// </summary>
public sealed class EngineCapabilitiesFactory(OutputContractCatalog contracts, WorkflowFormatCatalog formats)
{
    /// <summary>The node kinds this build runs: every kind of <c>constants.json</c>.</summary>
    public static readonly IReadOnlyList<string> Kinds = ["agent", "rendered", "computed", "for", "while", "if", "case", "call"];

    /// <summary>The features of <c>constants.json</c> this build runs: the runner's.</summary>
    public static readonly IReadOnlyList<string> Features = ["parallel", "check-loops"];

    /// <summary>The features of <c>constants.json</c> this build does not run yet, each with its reason; a package needing one is refused (S07-004).</summary>
    public static readonly IReadOnlyDictionary<string, string> FeaturesNotRun = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["parts"] = "a deliverable's parts are composed by the body composer, which area 07 builds",
        ["macro-markers"] = "macro markers are placed by the body composer, which area 07 builds",
        ["run-spots"] = "run spots are filled by the body composer, which area 07 builds",
    };

    private readonly OutputContractCatalog _contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
    private readonly WorkflowFormatCatalog _formats = formats ?? throw new ArgumentNullException(nameof(formats));

    /// <summary>What this engine runs now.</summary>
    public EngineCapabilities Current()
    {
        // Only the library versions are read from the toolchain; the release and catalogue names are not used here.
        var toolchain = Toolchain.Current(packageFormat: "", formatsCatalogue: "", agentsCatalogue: "");
        return new EngineCapabilities(
            JsonLogicOperations.EvaluatorVersion,
            toolchain.Scriban,
            JsonLogicOperations.All.Where(o => o.Custom)
                .ToDictionary(o => o.Name, _ => JsonLogicOperations.OperationVersion, StringComparer.Ordinal),
            Kinds.ToHashSet(StringComparer.Ordinal),
            Features.ToHashSet(StringComparer.Ordinal),
            Agents(),
            EngineBreakPoints.All,
            toolchain.Libraries.Where(l => l.Key != "Scriban").ToDictionary(l => l.Key, l => l.Value, StringComparer.Ordinal),
            new Dictionary<string, string>(LockCompiler.BuiltinVersions, StringComparer.Ordinal));
    }

    /// <summary>Each attested agent's versions, by name: every agent the startup attestation checks.</summary>
    private Dictionary<string, IReadOnlySet<int>> Agents()
    {
        var agents = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        void Add(string name, int version)
        {
            if (!agents.TryGetValue(name, out var versions))
                agents[name] = versions = [];
            versions.Add(version);
        }

        foreach (var entry in _contracts.Entries.Values)
        {
            if (int.TryParse(entry.AgentVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version))
                Add(entry.AgentName, version);
        }

        foreach (var format in _formats.Formats.Values)
        {
            if (format.Entry is { AgentName: { } name, AgentVersion: { } version })
                Add(name, version);
        }

        Add(_formats.CustomProducer.AgentName, _formats.CustomProducer.AgentVersion);
        return agents.ToDictionary(a => a.Key, a => (IReadOnlySet<int>)a.Value, StringComparer.Ordinal);
    }
}
