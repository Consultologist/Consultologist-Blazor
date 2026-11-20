using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1500 (specVersion 31, area 03): where a format a scope can name comes from: the built-in
/// formats the spec publishes, the agents catalogue's, the package's own <c>formats/</c>, or a
/// function's. Later tasks give the set its lookup; the origin is what the sentences name.
/// </summary>
public enum FormatOrigin
{
    /// <summary>One of the nine formats of <c>v31/formats/</c>.</summary>
    BuiltIn,

    /// <summary>A format of the agents catalogue (<c>classification</c>, <c>concept_list</c>).</summary>
    Catalogue,

    /// <summary>A file of the package's <c>formats/</c> folder.</summary>
    Package,

    /// <summary>A file of one function's <c>formats/</c> folder.</summary>
    Function,
}

/// <summary>A format a scope can name: its id, where it comes from, and its schema as written.</summary>
public sealed record NamedFormat(string Id, FormatOrigin Origin, JsonObject Schema);

/// <summary>
/// #1500 (specVersion 31, area 03): the formats a scope can name (part 02 § 4, § 6.3) and the
/// lookup every later service consumes. A package's set finds its own <c>formats/</c> first, then
/// the catalogue's, then the built-ins; a function's set finds its own formats, then the package
/// formats it <c>uses</c>, then the package formats those reach through their names (a name written
/// inside a package format belongs to the package's scope), then the catalogue and built-ins.
/// What the function itself may write is narrower (<see cref="MayName"/>): a reached package format
/// it does not list is found, but naming it is S02-017. Also the loops through named references
/// (S02-032). Never throws on package content.
/// </summary>
public sealed class FormatSet
{
    /// <summary>Deeper than the reader's 32 levels of nesting; a walk stops here instead of recursing on.</summary>
    private const int WalkDepth = 64;

    private readonly TypeRegistry _registry;
    private readonly Dictionary<string, SourceFormat> _package;
    private readonly Dictionary<string, SourceFormat> _function;
    private readonly Dictionary<string, SourceFormat> _usedPackage;
    private readonly Dictionary<string, SourceFormat> _reachedPackage;

    /// <summary>
    /// The formats whose files this scope checks for loops, in the order given: the package's in
    /// file order; a function's own, then the used ones in <c>uses</c> order, then the reached
    /// ones in the order first reached. Refusals never follow a dictionary's enumeration order.
    /// </summary>
    private readonly List<SourceFormat> _local;
    private readonly bool _forFunction;
    private HashSet<JsonObject>? _cyclic;
    private List<(SourceFormat Format, string Pointer)>? _loops;

    private FormatSet(
        TypeRegistry registry,
        Dictionary<string, SourceFormat> package,
        Dictionary<string, SourceFormat> function,
        Dictionary<string, SourceFormat> usedPackage,
        Dictionary<string, SourceFormat> reachedPackage,
        List<SourceFormat> local,
        bool forFunction)
    {
        _reachedPackage = reachedPackage;
        _local = local;
        _forFunction = forFunction;
        _registry = registry;
        _package = package;
        _function = function;
        _usedPackage = usedPackage;
    }

    /// <summary>The set a package's own scope sees.</summary>
    public static FormatSet ForPackage(TypeRegistry registry, IEnumerable<SourceFormat> package)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(package);
        var index = Index(package, out var order);
        return new FormatSet(registry, index, [], [], [], order, false);
    }

    /// <summary>
    /// The set one function sees: its <paramref name="own"/> formats (origin <see cref="FormatOrigin.Function"/>),
    /// then the package formats <paramref name="uses"/> names, then the package formats those reach through
    /// their names, at any depth (origin <see cref="FormatOrigin.Package"/>). Any other package format is not
    /// found; a reached one is found but not <see cref="MayName">nameable</see> by the function (part 02 § 6.3).
    /// </summary>
    public FormatSet ForFunction(IEnumerable<SourceFormat> own, IReadOnlyCollection<string> uses)
    {
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(uses);
        var function = Index(own, out var local);
        var used = new Dictionary<string, SourceFormat>(StringComparer.Ordinal);
        var pending = new Queue<SourceFormat>();
        foreach (var name in uses)
        {
            if (name != null && _package.TryGetValue(name, out var format) && used.TryAdd(name, format))
            {
                local.Add(format);
                pending.Enqueue(format);
            }
        }

        var reached = new Dictionary<string, SourceFormat>(StringComparer.Ordinal);
        while (pending.Count > 0)
        {
            foreach (var (name, _) in References(pending.Dequeue().Schema))
            {
                if (!used.ContainsKey(name) && _package.TryGetValue(name, out var target) && reached.TryAdd(name, target))
                {
                    local.Add(target);
                    pending.Enqueue(target);
                }
            }
        }

        return new FormatSet(_registry, _package, function, used, reached, local, true);
    }

    /// <summary>The format a name gives in this scope, or null.</summary>
    public NamedFormat? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_function.TryGetValue(name, out var own))
        {
            return new NamedFormat(name, FormatOrigin.Function, own.Schema);
        }

        if (_usedPackage.TryGetValue(name, out var used))
        {
            return new NamedFormat(name, FormatOrigin.Package, used.Schema);
        }

        if ((_forFunction ? _reachedPackage : _package).TryGetValue(name, out var package))
        {
            return new NamedFormat(name, FormatOrigin.Package, package.Schema);
        }

        if (_registry.CatalogueFormats.TryGetValue(name, out var catalogue))
        {
            return new NamedFormat(name, FormatOrigin.Catalogue, catalogue);
        }

        return _registry.BuiltInFormats.TryGetValue(name, out var builtIn)
            ? new NamedFormat(name, FormatOrigin.BuiltIn, builtIn)
            : null;
    }

    /// <summary>
    /// Whether this scope's own declarations may write <paramref name="name"/>: any name <see cref="Find"/>
    /// finds in a package's scope; in a function's, its own formats, the package formats it <c>uses</c>,
    /// the catalogue's and the built-ins, but not a package format it only reaches through a used one.
    /// </summary>
    public bool MayName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _forFunction
            ? _function.ContainsKey(name) || _usedPackage.ContainsKey(name)
              || _registry.CatalogueFormats.ContainsKey(name) || _registry.BuiltInFormats.ContainsKey(name)
            : Find(name) != null;
    }

    /// <summary>
    /// What <paramref name="format"/> is: a name or a <c>{ "$ref": name }</c>-only object gives the
    /// named schema (null when no such format); any other object gives itself; anything else null.
    /// </summary>
    public JsonObject? Resolve(JsonNode? format)
    {
        var name = NameOf(format);
        if (name != null)
        {
            return Find(name)?.Schema;
        }

        return format as JsonObject;
    }

    /// <summary>
    /// The name <paramref name="format"/> refers to: the text itself, or the bare-name <c>$ref</c> of
    /// an object whose only other keywords are <c>title</c> and <c>description</c>; otherwise null.
    /// </summary>
    public static string? NameOf(JsonNode? format)
    {
        if (format is JsonValue value)
        {
            return BareName(value);
        }

        if (format is not JsonObject obj || !obj.ContainsKey("$ref"))
        {
            return null;
        }

        foreach (var key in obj.Select(p => p.Key))
        {
            if (key is not ("$ref" or "title" or "description"))
            {
                return null;
            }
        }

        return BareName(obj["$ref"]);
    }

    /// <summary>S02-017 for every bare name (the format itself, or a <c>$ref</c> at any depth) this scope <see cref="MayName">may not name</see>, once per name, and for a format that is the empty text.</summary>
    /// <param name="format">The format as written.</param>
    /// <param name="subject">The format's owner (D11); S02-017's words name the missing format instead.</param>
    /// <param name="path">The file the refusals carry.</param>
    public IReadOnlyList<SourceRefusal> CheckReferences(JsonNode? format, string subject, string path)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(path);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var refusals = new List<SourceRefusal>();

        // "format": "" names no format; an empty $ref inside a schema is the dialect's (A03-002).
        if (format is JsonValue value && value.TryGetValue<string>(out var text) && text.Length == 0)
        {
            refusals.Add(FormatSentences.UnknownFormat(text, path));
        }

        foreach (var (name, _) in References(format))
        {
            if (!MayName(name) && seen.Add(name))
            {
                refusals.Add(FormatSentences.UnknownFormat(name, path));
            }
        }

        return refusals;
    }

    /// <summary>
    /// S02-032 once for each package or function format of this scope that reaches itself through
    /// named references (<c>properties</c>, <c>items</c>, <c>anyOf</c>, <c>$defs</c>); the path is
    /// the format's file and the sentence's path the pointer of the first <c>$ref</c> that starts a loop.
    /// </summary>
    public IReadOnlyList<SourceRefusal> CheckCycles()
    {
        Analyse();
        return _loops!.Select(l => FormatSentences.RefersToItself($"Format '{l.Format.Id}'", l.Pointer, l.Format.Path)).ToList();
    }

    /// <summary>Whether <paramref name="name"/> is a package or function format of this scope that reaches itself.</summary>
    public bool IsCyclic(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Analyse();
        var found = Find(name);
        return found != null && found.Origin is FormatOrigin.Package or FormatOrigin.Function && _cyclic!.Contains(found.Schema);
    }

    /// <summary>The formats by id, the first of an id kept; <paramref name="order"/> holds the kept ones in the order given.</summary>
    private static Dictionary<string, SourceFormat> Index(IEnumerable<SourceFormat> formats, out List<SourceFormat> order)
    {
        var index = new Dictionary<string, SourceFormat>(StringComparer.Ordinal);
        order = [];
        foreach (var format in formats)
        {
            if (index.TryAdd(format.Id, format))
            {
                order.Add(format);
            }
        }

        return index;
    }

    private static string? BareName(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 && s[0] != '#' ? s : null;

    private void Analyse()
    {
        if (_cyclic != null)
        {
            return;
        }

        var nodes = _local;
        var edges = new Dictionary<JsonObject, List<(JsonObject Target, string Pointer)>>(ReferenceEqualityComparer.Instance);
        foreach (var format in nodes)
        {
            var list = new List<(JsonObject, string)>();
            foreach (var (name, pointer) in References(format.Schema))
            {
                var target = Find(name);
                if (target is { Origin: FormatOrigin.Package or FormatOrigin.Function })
                {
                    list.Add((target.Schema, pointer));
                }
            }

            edges[format.Schema] = list;
        }

        _cyclic = new HashSet<JsonObject>(ReferenceEqualityComparer.Instance);
        _loops = [];
        foreach (var format in nodes)
        {
            foreach (var (target, pointer) in edges[format.Schema])
            {
                if (Reaches(edges, target, format.Schema))
                {
                    _cyclic.Add(format.Schema);
                    _loops.Add((format, pointer));
                    break;
                }
            }
        }
    }

    private static bool Reaches(Dictionary<JsonObject, List<(JsonObject Target, string Pointer)>> edges, JsonObject from, JsonObject goal)
    {
        var seen = new HashSet<JsonObject>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<JsonObject>();
        stack.Push(from);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (ReferenceEquals(node, goal))
            {
                return true;
            }

            if (seen.Add(node) && edges.TryGetValue(node, out var next))
            {
                foreach (var (target, _) in next)
                {
                    stack.Push(target);
                }
            }
        }

        return false;
    }

    /// <summary>Every bare name written as a schema or as a <c>$ref</c>, with the pointer of the schema that holds it, in document order.</summary>
    internal static List<(string Name, string Pointer)> References(JsonNode? format)
    {
        var found = new List<(string, string)>();
        Walk(format, string.Empty, 0, found);
        return found;
    }

    private static void Walk(JsonNode? node, string pointer, int depth, List<(string, string)> found)
    {
        if (depth > WalkDepth)
        {
            return;
        }

        if (node is JsonValue value)
        {
            if (BareName(value) is { } text)
            {
                found.Add((text, pointer));
            }

            return;
        }

        if (node is not JsonObject obj)
        {
            return;
        }

        if (BareName(obj["$ref"]) is { } name)
        {
            found.Add((name, pointer));
        }

        if (obj["properties"] is JsonObject properties)
        {
            foreach (var (key, member) in properties)
            {
                Walk(member, $"{pointer}/properties/{FormatDialect.Escape(key)}", depth + 1, found);
            }
        }

        Walk(obj["items"], $"{pointer}/items", depth + 1, found);
        if (obj["anyOf"] is JsonArray members)
        {
            for (var i = 0; i < members.Count; i++)
            {
                Walk(members[i], $"{pointer}/anyOf/{i}", depth + 1, found);
            }
        }

        if (obj["$defs"] is JsonObject defs)
        {
            foreach (var (key, member) in defs)
            {
                Walk(member, $"{pointer}/$defs/{FormatDialect.Escape(key)}", depth + 1, found);
            }
        }
    }
}
