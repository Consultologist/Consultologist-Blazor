using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1502 (specVersion 31, area 05): writes <c>lock.json</c>, the exact toolchain that checked a package (part 07 § 4.1;
/// D9), from the <c>resolve.json</c> it belongs to, that document's hash and the <see cref="Toolchain"/>. Every member of
/// § 4.1 is present, in its order; there is no time. The operations are the names <c>requires.operations</c> lists, each
/// at its exact version; the built-ins are the machinery every package is checked with and each built-in renderer an
/// entry renders through (§ 4.3), at the versions <c>constants.json</c> declares. Never throws on the document: a member
/// it reads that is missing or of another shape gives null or nothing. A resolve with no <c>package</c> (or one whose
/// name or version is not text) is written <c>"package": { "name": null, "version": null }</c>, the missing one null;
/// with no <c>requires.operations</c>, <c>operations</c> is <c>{}</c>; with no entry rendering through a built-in
/// renderer, <c>builtins</c> is the machinery alone.
/// </summary>
public static class LockCompiler
{
    /// <summary><c>lock.json</c>'s own shape at 31 (§ 4.1).</summary>
    public const int DocumentVersion = 1;

    /// <summary>The culture every render and operation runs in (§ 4.1).</summary>
    public const string Culture = "invariant";

    /// <summary>The built-in machinery that is not an operation, at <c>constants.json</c>'s <c>builtins</c> versions (§ 4.3).</summary>
    private static readonly (string Name, string Version)[] Machinery =
    [
        ("formats", "1.0.0"),
        ("markdown_escape", "1.0.0"),
        ("plain_conversion", "1.0.0"),
        ("printer", "1.0.0"),
    ];

    /// <summary>The built-in renderers, whose ids are reserved (part 05), at <c>constants.json</c>'s <c>builtins</c> versions.</summary>
    private static readonly Dictionary<string, string> Renderers = new(StringComparer.Ordinal)
    {
        ["concept_bullets"] = "1.0.0",
        ["concept_context"] = "1.0.0",
    };

    /// <summary>Every name <c>builtins</c> may hold: the machinery and each built-in renderer.</summary>
    internal static IEnumerable<string> BuiltinNames => Machinery.Select(m => m.Name).Concat(Renderers.Keys);

    /// <summary>#1503: every built-in <c>builtins</c> may hold, at the version this library writes for it.</summary>
    internal static IReadOnlyDictionary<string, string> BuiltinVersions { get; } =
        Machinery.Concat(Renderers.Select(r => (Name: r.Key, Version: r.Value)))
            .ToDictionary(b => b.Name, b => b.Version, StringComparer.Ordinal);

    /// <summary>The flat maps of <c>resolve.json</c> whose entries may render (§ 2.5), and a function's own.</summary>
    private static readonly string[] Maps = ["nodes", "checks", "renderers", "snippets", "macros", "deliverables"];

    /// <summary>
    /// <c>lock.json</c> for <paramref name="resolve"/>, whose canonical bytes hash to <paramref name="resolveHash"/>,
    /// checked by <paramref name="toolchain"/>. A null argument is the caller's error.
    /// </summary>
    public static JsonObject Compile(JsonObject resolve, string resolveHash, Toolchain toolchain)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        ArgumentNullException.ThrowIfNull(resolveHash);
        ArgumentNullException.ThrowIfNull(toolchain);

        var package = Member(resolve, "package");
        var libraries = new JsonObject();
        foreach (var (name, version) in toolchain.Libraries.OrderBy(l => l.Key, StringComparer.Ordinal))
            libraries[name] = version;

        return new JsonObject
        {
            ["documentVersion"] = DocumentVersion,
            ["package"] = new JsonObject
            {
                ["name"] = SourceLayout.Text(Member(package, "name")),
                ["version"] = SourceLayout.Text(Member(package, "version")),
            },
            ["resolve"] = new JsonObject { ["hash"] = resolveHash },
            ["resolver"] = new JsonObject { ["packageFormat"] = toolchain.PackageFormat, ["dialect"] = toolchain.Dialect },
            ["libraries"] = libraries,
            ["evaluator"] = toolchain.Evaluator,
            ["operations"] = Operations(resolve),
            ["builtins"] = Builtins(resolve),
            ["culture"] = Culture,
            ["runtime"] = new JsonObject { ["dotnet"] = toolchain.DotNet, ["unicode"] = toolchain.Unicode },
            ["catalogues"] = new JsonObject
            {
                ["agents"] = toolchain.AgentsCatalogue,
                ["formats"] = toolchain.FormatsCatalogue,
                ["outputContracts"] = toolchain.OutputContracts,
            },
        };
    }

    /// <summary>The member <paramref name="name"/> of <paramref name="node"/> when it is an object; else null.</summary>
    internal static JsonNode? Member(JsonNode? node, string name) =>
        node is JsonObject o && o.TryGetPropertyValue(name, out var value) ? value : null;

    /// <summary>Each built-in custom operation <c>requires.operations</c> names, at its exact version, in the operations' order (as <c>requires</c> writes them).</summary>
    private static JsonObject Operations(JsonObject resolve)
    {
        var listed = Member(Member(resolve, "requires"), "operations") as JsonObject;
        var operations = new JsonObject();
        if (listed == null)
            return operations;
        foreach (var operation in JsonLogicOperations.All.Where(o => o.Custom && listed.ContainsKey(o.Name)))
            operations[operation.Name] = JsonLogicOperations.OperationVersion;
        return operations;
    }

    /// <summary>The machinery, and each built-in renderer any entry's <c>renders</c> names (<c>renderer.</c> and its id, or the id).</summary>
    private static JsonObject Builtins(JsonObject resolve)
    {
        var used = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var entry in Entries(resolve))
        {
            if (Member(entry, "renders") is not JsonArray renders)
                continue;
            foreach (var render in renders)
            {
                if (SourceLayout.Text(Member(render, "renderer")) is not { } id)
                    continue;
                var bare = id.StartsWith("renderer.", StringComparison.Ordinal) ? id["renderer.".Length..] : id;
                if (Renderers.ContainsKey(bare))
                    used.Add(bare);
            }
        }

        var builtins = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, version) in Machinery)
            builtins[name] = version;
        foreach (var name in used)
            builtins[name] = Renderers[name];

        var written = new JsonObject();
        foreach (var (name, version) in builtins)
            written[name] = version;
        return written;
    }

    /// <summary>Every entry of the flat maps, the package's and each function's own.</summary>
    private static IEnumerable<JsonNode?> Entries(JsonObject resolve)
    {
        var owners = new List<JsonNode?> { resolve };
        if (Member(resolve, "functions") is JsonObject functions)
            owners.AddRange(functions.Select(f => f.Value));
        foreach (var owner in owners)
        {
            foreach (var map in Maps)
            {
                if (Member(owner, map) is JsonObject entries)
                {
                    foreach (var (_, entry) in entries)
                        yield return entry;
                }
            }
        }
    }
}
