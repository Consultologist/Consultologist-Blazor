using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.PackageFormat.V31.Diagram;

namespace Consultologist.Api.Tests.V31Diagram;

/// <summary>
/// #1510 (specVersion 31, area 13): what the diagram's tests draw from: a resolve golden's text
/// (<c>tests/V31Resolve/Golden/&lt;golden&gt;.resolve.json</c>), or a small <c>resolve.json</c> built in the test holding
/// only the members the model reads. Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
internal static class DiagramFixture
{
    /// <summary>The five resolve goldens.</summary>
    public static readonly string[] Goldens = ["nested-note", "conditional", "functions", "checked-note", "macros"];

    /// <summary>The text of the resolve golden <paramref name="golden"/>.</summary>
    public static string Resolve(string golden) =>
        File.ReadAllText(Path.Combine(CompiledGoldenTests.GoldenFolder(), golden + ".resolve.json"));

    /// <summary>The diagram of <paramref name="resolve"/>, its lines.</summary>
    public static string[] Lines(string resolve) => PackageDiagram.Generate(resolve).Split('\n');

    /// <summary>A <c>resolve.json</c> of <paramref name="entries"/>: each <c>node.*</c> into <c>nodes</c>, <c>deliverable.*</c> into <c>deliverables</c>, the inputs read declared.</summary>
    public static string Document(params (string Key, JsonObject Entry)[] entries)
    {
        var nodes = new JsonObject();
        var deliverables = new JsonObject();
        var inputs = new JsonObject();
        foreach (var (key, entry) in entries)
        {
            (key.StartsWith("deliverable.", StringComparison.Ordinal) ? deliverables : nodes)[key] = entry;
            foreach (var read in (entry["reads"] as JsonArray ?? []).Select(r => r!.GetValue<string>()))
            {
                if (read.Split('.') is ["input", var id, ..])
                    inputs[id] = new JsonObject { ["label"] = id };
            }
        }

        return new JsonObject
        {
            ["inputs"] = inputs,
            ["nodes"] = nodes,
            ["deliverables"] = deliverables,
            ["graph"] = new JsonObject { ["edges"] = new JsonArray(), ["order"] = new JsonArray(entries.Select(e => (JsonNode?)e.Key).ToArray()) },
        }.ToJsonString();
    }

    /// <summary>An agent node's entry.</summary>
    public static JsonObject Agent(string id, string label, string format, params string[] reads) =>
        Leaf("agent", id, label, format, "generated", reads);

    /// <summary>A leaf entry of <paramref name="kind"/>.</summary>
    public static JsonObject Leaf(string kind, string id, string? label, string format, string? producer, params string[] reads) => new()
    {
        ["id"] = id,
        ["kind"] = kind,
        ["label"] = label,
        ["producer"] = producer,
        ["format"] = new JsonObject { ["$ref"] = "#/$defs/" + format },
        ["modelDerived"] = kind == "agent",
        ["reads"] = Strings(reads),
    };

    /// <summary>A deliverable's entry.</summary>
    public static JsonObject Deliverable(string id, string label, params string[] reads) => new()
    {
        ["id"] = id,
        ["kind"] = "rendered",
        ["label"] = label,
        ["format"] = new JsonObject { ["$ref"] = "#/$defs/markdown_block" },
        ["reads"] = Strings(reads),
    };

    /// <summary>A <c>for</c> over <paramref name="list"/> whose body is <paramref name="nodes"/>, returning <paramref name="ret"/>.</summary>
    public static JsonObject For(string id, string label, string list, string ret, params string[] nodes) => new()
    {
        ["id"] = id,
        ["kind"] = "for",
        ["label"] = label,
        ["producer"] = "computed",
        ["in"] = list,
        ["nodes"] = Strings(nodes),
        ["ret"] = ret,
        ["until"] = null,
        ["reads"] = Strings([string.Join('.', list.Split('.').Take(3))]),
    };

    /// <summary>The entry with <paramref name="parent"/> set.</summary>
    public static JsonObject In(this JsonObject entry, string parent)
    {
        entry["parent"] = parent;
        return entry;
    }

    public static JsonArray Strings(IEnumerable<string> values) => new(values.Select(v => (JsonNode?)v).ToArray());
}
