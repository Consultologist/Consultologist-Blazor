using System.Globalization;
using System.Text.Json.Nodes;
using Consultologist.Api.Agents;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.V31;

/// <summary>
/// #1504 (specVersion 31, area 07; part 02 § 3.5 step 5; part 09 § 1.2 step 4; D9; #1504 ruling 3): the 31 agents catalogue
/// the engine runs with: <c>agents/v31/bindings.json</c> and <c>agents/v31/agents.json</c> of the bundled agents directory
/// (<see cref="OutputContractCatalog.ResolveDirectory"/>, the pinned <c>external/consultologist-agents/agents/</c>), read
/// together as publish reads them (<see cref="AgentBindings.Read"/>), and attested as the engine's other catalogues are
/// against git: each agent version it holds is the version of its bundled manifest (<c>agents/&lt;name&gt;.yaml</c>). Its
/// agents are among <see cref="EngineCapabilities.Agents"/>, and an agent call's sent format leaves out what the agent's
/// provider's strict mode rejects (<see cref="StrictRejects"/>). Any defect is a startup failure
/// (<see cref="InvalidOperationException"/>, thrown when <see cref="ApiServices.ResolveAtStartup"/> resolves it): an engine without a coherent catalogue must not accept jobs.
/// </summary>
public sealed class V31AgentsCatalogue
{
    /// <summary>The catalogue's folder under the agents directory.</summary>
    public const string Folder = "v31";

    private readonly Dictionary<(string Name, int Version), IReadOnlyList<string>> _agents;

    private V31AgentsCatalogue(string version, Dictionary<(string Name, int Version), IReadOnlyList<string>> agents)
    {
        Version = version;
        _agents = agents;
    }

    /// <summary>A catalogue holding no agent (for a test of the catalogues before this one).</summary>
    public static V31AgentsCatalogue Empty { get; } = new("", []);

    /// <summary>The catalogue's version, which both files carry (<c>v2026.12.2</c>).</summary>
    public string Version { get; }

    /// <summary>Each agent version of <c>agents.json</c>, ordinal by name, then by version.</summary>
    public IEnumerable<(string Name, int Version)> Agents =>
        _agents.Keys.OrderBy(a => a.Name, StringComparer.Ordinal).ThenBy(a => a.Version);

    /// <summary>The keywords the strict mode of <paramref name="agentName"/>@<paramref name="agentVersion"/>'s provider rejects; none for an agent the catalogue does not hold.</summary>
    public IReadOnlyList<string> StrictRejects(string agentName, int agentVersion) =>
        _agents.GetValueOrDefault((agentName, agentVersion)) ?? [];

    /// <summary>Whether the catalogue holds <paramref name="agentName"/>@<paramref name="agentVersion"/>.</summary>
    public bool Holds(string agentName, int agentVersion) => _agents.ContainsKey((agentName, agentVersion));

    /// <summary>Loads and attests the catalogue under <paramref name="directory"/> (the bundled agents directory by default).</summary>
    /// <exception cref="InvalidOperationException">A file is missing or not the published shape, or an agent's manifest is missing or at another version.</exception>
    public static V31AgentsCatalogue Load(string? directory = null)
    {
        directory ??= OutputContractCatalog.ResolveDirectory();
        var bindings = Read(Path.Combine(directory, Folder, "bindings.json"));
        var agents = Read(Path.Combine(directory, Folder, "agents.json"));

        AgentBindings checkedBindings;
        try
        {
            checkedBindings = AgentBindings.Read(bindings, agents);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException($"The 31 agents catalogue at '{Path.Combine(directory, Folder)}' is not the published shape: {ex.Message}", ex);
        }

        // AgentBindings.Read has held agents.json to its shape: agents.<name>.<version>.strictRejects, a list of text.
        var held = new Dictionary<(string Name, int Version), IReadOnlyList<string>>();
        foreach (var (name, versions) in agents["agents"]!.AsObject())
        {
            foreach (var (number, definition) in versions!.AsObject())
            {
                var version = int.Parse(number, NumberStyles.None, CultureInfo.InvariantCulture);
                Attest(directory, name, version);
                held[(name, version)] = definition!["strictRejects"]!.AsArray().Select(k => k!.GetValue<string>()).ToList();
            }
        }

        return new V31AgentsCatalogue(checkedBindings.Version, held);
    }

    /// <summary>The bundled manifest of <paramref name="name"/> is there and declares <paramref name="version"/>.</summary>
    private static void Attest(string directory, string name, int version)
    {
        var path = Path.Combine(directory, $"{name}.yaml");
        if (!File.Exists(path))
            throw new InvalidOperationException($"The 31 agents catalogue holds {name}@{version}, which has no git manifest at '{path}'.");
        var manifest = AttestedAgentManifest.Load(File.ReadAllText(path));
        if (!string.Equals(manifest.Version, version.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            throw new InvalidOperationException($"The 31 agents catalogue holds {name}@{version}, but its git manifest '{path}' is version {manifest.Version}.");
    }

    private static JsonObject Read(string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"The 31 agents catalogue file '{path}' was not found.");
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is JsonObject document)
                return document;
        }
        catch (System.Text.Json.JsonException)
        {
        }

        throw new InvalidOperationException($"The 31 agents catalogue file '{path}' is not a JSON object.");
    }
}
