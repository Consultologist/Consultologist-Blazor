using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1500 (specVersion 31, area 03): one attested agent a format is bound to for a producer
/// (part 02 § 7): its catalogue name and version, how it was found (<c>format</c> or
/// <c>custom</c>), and the keywords its provider's strict mode rejects.
/// </summary>
public sealed record AgentBinding(string AgentName, int AgentVersion, string Via, IReadOnlyList<string> StrictRejects);

/// <summary>
/// #1500 (specVersion 31, area 03; D21): the agents catalogue's <c>bindings.json</c> and
/// <c>agents.json</c> read together. A built-in or catalogue format is bound through
/// <c>formats.&lt;id&gt;.&lt;producer&gt;</c> (<c>via</c> <c>format</c>), a package's or function's
/// own format through <c>custom.&lt;producer&gt;</c> (<c>via</c> <c>custom</c>), never the other way.
/// The files are the deployment's, so <see cref="Read"/> throws <see cref="FormatException"/> on a
/// file that is not the published shape.
/// </summary>
public sealed class AgentBindings
{
    private static readonly Regex IdPattern = new("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant);

    private readonly Dictionary<string, Dictionary<string, AgentBinding>> _formats;
    private readonly Dictionary<string, AgentBinding> _custom;

    private AgentBindings(string version, Dictionary<string, Dictionary<string, AgentBinding>> formats, Dictionary<string, AgentBinding> custom)
    {
        Version = version;
        _formats = formats;
        _custom = custom;
    }

    /// <summary>The agents catalogue's version, which both files carry.</summary>
    public string Version { get; }

    /// <summary>Reads the two files, checking them against <c>bindings.schema.json</c> and <c>agents.schema.json</c>.</summary>
    /// <exception cref="FormatException">A file is not the published shape, a binding names an agent version <c>agents.json</c> lacks, or the two versions differ.</exception>
    public static AgentBindings Read(JsonObject bindings, JsonObject agents)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(agents);

        OnlyMembers(bindings, "bindings.json", "version", "formats", "custom");
        OnlyMembers(agents, "agents.json", "version", "agents");
        var version = Text(bindings, "version", "bindings.json");
        if (Text(agents, "version", "agents.json") != version)
        {
            throw new FormatException("bindings.json and agents.json carry different versions.");
        }

        var rejects = new Dictionary<(string Name, int Version), IReadOnlyList<string>>();
        foreach (var (name, versions) in Object(agents, "agents", "agents.json"))
        {
            var byVersion = versions as JsonObject ?? throw new FormatException($"agents.json: agents.{name} is not an object.");
            foreach (var (number, definition) in byVersion)
            {
                if (!int.TryParse(number, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n))
                {
                    throw new FormatException($"agents.json: agents.{name}.{number} is not a version number.");
                }

                var path = $"agents.{name}.{number}";
                var def = definition as JsonObject ?? throw new FormatException($"agents.json: {path} is not an object.");
                OnlyMembers(def, "agents.json: " + path, "strictRejects");
                var list = def["strictRejects"] as JsonArray ?? throw new FormatException($"agents.json: {path}.strictRejects is not a list.");
                rejects[(name, n)] = list
                    .Select(k => k is JsonValue v && v.TryGetValue<string>(out var s)
                        ? s
                        : throw new FormatException($"agents.json: {path}.strictRejects holds a non-text item."))
                    .ToList();
            }
        }

        AgentBinding Binding(JsonNode? node, string path, string via)
        {
            var o = node as JsonObject ?? throw new FormatException($"bindings.json: {path} is not an object.");
            OnlyMembers(o, "bindings.json: " + path, "agentName", "agentVersion");
            var name = Text(o, "agentName", "bindings.json: " + path);
            if (o["agentVersion"] is not JsonValue v || !v.TryGetValue<int>(out var n))
            {
                throw new FormatException($"bindings.json: {path}.agentVersion is not an integer.");
            }

            return rejects.TryGetValue((name, n), out var r)
                ? new AgentBinding(name, n, via, r)
                : throw new FormatException($"bindings.json: {path} names agent {name} version {n}, which agents.json does not hold.");
        }

        Dictionary<string, AgentBinding> Producers(JsonNode? node, string path, string via)
        {
            var o = node as JsonObject ?? throw new FormatException($"bindings.json: {path} is not an object.");
            var map = new Dictionary<string, AgentBinding>(StringComparer.Ordinal);
            foreach (var (producer, binding) in o)
            {
                if (producer != "generated")
                {
                    throw new FormatException($"bindings.json: {path} binds producer '{producer}'; at 31 only generated has bindings.");
                }

                map[producer] = Binding(binding, $"{path}.{producer}", via);
            }

            return map;
        }

        var formats = new Dictionary<string, Dictionary<string, AgentBinding>>(StringComparer.Ordinal);
        foreach (var (id, producers) in Object(bindings, "formats", "bindings.json"))
        {
            if (id.Length > 64 || !IdPattern.IsMatch(id))
            {
                throw new FormatException($"bindings.json: formats.{id} is not a format id.");
            }

            formats[id] = Producers(producers, $"formats.{id}", "format");
        }

        var custom = Producers(bindings["custom"] ?? throw new FormatException("bindings.json has no custom."), "custom", "custom");
        return new AgentBindings(version, formats, custom);
    }

    /// <summary>
    /// The agent that serves <paramref name="formatId"/> (of <paramref name="origin"/>) for
    /// <paramref name="producer"/>, or null when there is none.
    /// </summary>
    public AgentBinding? Find(string formatId, FormatOrigin origin, string producer)
    {
        ArgumentNullException.ThrowIfNull(formatId);
        ArgumentNullException.ThrowIfNull(producer);
        if (origin is FormatOrigin.Package or FormatOrigin.Function)
        {
            return _custom.GetValueOrDefault(producer);
        }

        return _formats.TryGetValue(formatId, out var producers) ? producers.GetValueOrDefault(producer) : null;
    }

    private static JsonObject Object(JsonObject parent, string member, string file) =>
        parent[member] as JsonObject ?? throw new FormatException($"{file} has no '{member}' object.");

    private static string Text(JsonObject parent, string member, string where) =>
        parent[member] is JsonValue v && v.TryGetValue<string>(out var s)
            ? s
            : throw new FormatException($"{where} has no text '{member}'.");

    private static void OnlyMembers(JsonObject o, string where, params string[] allowed)
    {
        foreach (var name in allowed)
        {
            if (!o.ContainsKey(name))
            {
                throw new FormatException($"{where} has no '{name}'.");
            }
        }

        foreach (var (key, _) in o)
        {
            if (!allowed.Contains(key))
            {
                throw new FormatException($"{where} has the member '{key}', which the published shape does not.");
            }
        }
    }
}
