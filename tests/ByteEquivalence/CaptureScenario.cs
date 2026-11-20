using System.Text.Json.Nodes;

namespace Consultologist.Api.Tests.ByteEquivalence;

/// <summary>
/// #1498: one scenario file, <c>scenarios/&lt;package&gt;/&lt;scenario&gt;.json</c> — 07 § 8.1's shape
/// at 30's keys, every member present. <see cref="Inputs"/> holds the raw value per declared
/// input (null leaves it unset); <see cref="Outputs"/> pins a reply by instance key (<c>node</c>
/// or <c>node:path</c>): a JSON string for a text format, else the JSON value.
/// </summary>
internal sealed record CaptureScenario(
    IReadOnlyList<string>? Description,
    IReadOnlyDictionary<string, JsonNode?> Inputs,
    CaptureProfile Profile,
    IReadOnlyDictionary<string, bool> MacroChoices,
    IReadOnlyDictionary<string, JsonNode?> Outputs)
{
    private static readonly string[] Members = ["description", "inputs", "profile", "macroChoices", "outputs"];

    private static readonly string[] ProfileMembers = ["name", "signature"];

    /// <summary>The scenario in <paramref name="json"/>; a missing or unknown member is refused by name.</summary>
    public static CaptureScenario Read(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("A scenario is a JSON object.");
        Exactly(root, Members, "The scenario");
        var profile = root["profile"] as JsonObject ?? throw new InvalidDataException("The scenario's profile is an object.");
        Exactly(profile, ProfileMembers, "The scenario's profile");

        return new CaptureScenario(
            root["description"] switch
            {
                null => null,
                JsonArray lines => lines.Select((line, i) => Text(line, $"description[{i}]") ?? throw new InvalidDataException($"The scenario's description[{i}] is a string.")).ToList(),
                _ => throw new InvalidDataException("The scenario's description is an array of lines, or null."),
            },
            Map(root, "inputs", (_, value) => value?.DeepClone()),
            new CaptureProfile(Text(profile["name"], "profile.name"), Text(profile["signature"], "profile.signature")),
            Map(root, "macroChoices", (id, value) => value is JsonValue choice && choice.TryGetValue(out bool chosen)
                ? chosen
                : throw new InvalidDataException($"The scenario's macroChoices.{id} is true or false.")),
            Map<JsonNode?>(root, "outputs", (key, value) => value?.DeepClone()
                ?? throw new InvalidDataException($"The scenario's outputs.{key} is null; pin a value or remove the key.")));
    }

    /// <summary>A string member, or null; anything else is refused by name.</summary>
    private static string? Text(JsonNode? value, string member) => value switch
    {
        null => null,
        JsonValue text when text.TryGetValue(out string? s) => s,
        _ => throw new InvalidDataException($"The scenario's {member} is a string or null."),
    };

    private static void Exactly(JsonObject obj, string[] members, string what)
    {
        foreach (var member in members)
        {
            if (!obj.ContainsKey(member))
            {
                throw new InvalidDataException($"{what} has no member '{member}'; every member is present.");
            }
        }

        foreach (var (member, _) in obj)
        {
            if (!members.Contains(member, StringComparer.Ordinal))
            {
                throw new InvalidDataException($"{what} has an unknown member '{member}'.");
            }
        }
    }

    private static IReadOnlyDictionary<string, T> Map<T>(JsonObject root, string member, Func<string, JsonNode?, T> read) =>
        (root[member] as JsonObject ?? throw new InvalidDataException($"The scenario's {member} is an object."))
            .ToDictionary(pair => pair.Key, pair => read(pair.Key, pair.Value), StringComparer.Ordinal);
}

/// <summary>#1498: the fixed fictional profile a scenario runs under — the display name and the chosen signature block's text.</summary>
internal sealed record CaptureProfile(string? Name, string? Signature);
