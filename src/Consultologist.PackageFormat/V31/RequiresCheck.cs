using System.Globalization;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1502 (specVersion 31, area 05, D10): one of an engine's break points (part 07 § 4.4): the version of a locked
/// <paramref name="Component"/> (<c>Scriban</c>, <c>the evaluator</c>, an operation's name, or another library of
/// <c>lock.json</c>'s <c>libraries</c>) at which its output changed.
/// </summary>
public sealed record BreakPoint(string Component, string Version);

/// <summary>
/// #1502 (specVersion 31, area 05, D10): what an engine runs, as <see cref="RequiresCheck.Engine"/> holds a package to it:
/// the evaluator's and Scriban's exact versions, each operation's (name to version), the node kinds and features it
/// supports, each attested agent's versions (name to versions), its break points (part 07 § 4.4), and the exact
/// version of each library <c>lock.json</c> locks (<paramref name="Libraries"/>, by <c>lock.json</c>'s keys:
/// <c>Markdig</c>, <c>Corvus.Text.Json.JsonLogic</c>, <c>Corvus.JsonSchema</c>), which a break point of one is held
/// to. Engine configuration: area 06 builds it.
/// </summary>
public sealed record EngineCapabilities(
    string Evaluator,
    string Scriban,
    IReadOnlyDictionary<string, string> Operations,
    IReadOnlySet<string> Kinds,
    IReadOnlySet<string> Features,
    IReadOnlyDictionary<string, IReadOnlySet<int>> Agents,
    IReadOnlyList<BreakPoint> BreakPoints,
    IReadOnlyDictionary<string, string> Libraries);

/// <summary>
/// #1502 (specVersion 31, area 05, D10): what an engine checks before a job starts (part 07 § 3, § 4.2, § 4.4, § 10 "At
/// start"), as pure functions; calling them when the Create page asks and when the job starts is area 06's.
/// <see cref="Engine"/> holds <c>requires</c> and <c>lock.json</c>'s exact versions to what the engine runs and its
/// break points; <see cref="Profile"/> holds <c>needs.profile</c> to the account's profile. Never throws on a document:
/// a member it reads that is missing or of another shape is refused, with <c>unknown</c> where the document says nothing,
/// and a document that cannot be read (parsed from text that repeats a member) is refused whole.
/// </summary>
public static class RequiresCheck
{
    /// <summary>What a sentence says where the document does not say it.</summary>
    private const string Unknown = "unknown";

    /// <summary>What a sentence says of an operation the engine does not have.</summary>
    private const string None = "none";

    /// <summary>The components <see cref="Engine"/> names in its sentences, as part 07 § 10 writes them.</summary>
    private const string Scriban = "Scriban";

    private const string Evaluator = "the evaluator";

    /// <summary>The profile fields in <c>v31/profile.json</c>'s order, each with its label there.</summary>
    private static readonly (string Field, string Label)[] Fields =
    [
        ("name", "name"),
        ("credentials", "credentials"),
        ("organisation", "organisation"),
        ("signature", "signature"),
        ("closing", "closing line"),
    ];

    /// <summary>
    /// The refusals of starting a job of the package <paramref name="resolve"/> describes, locked by
    /// <paramref name="lock"/>, on <paramref name="engine"/>, in this order: S07-003 for Scriban, the evaluator, then
    /// each operation <c>requires.operations</c> names (ordinal), when the engine's version is outside the range, the
    /// operation is missing, the locked version is not a version, or the package is locked before one of the
    /// component's break points with the engine at or past it; S07-003 for another locked library (ordinal) locked before
    /// one of its break points (a lock that does not say the library is before all of them) with the engine's version of
    /// it (<see cref="EngineCapabilities.Libraries"/>) at or past it, or not given as a version, naming that library and
    /// the engine's version of it (or <c>unknown</c>); S07-004 for each kind, then
    /// each feature, the engine does not support; S07-005 for each agent (ordinal) at a version the engine does not have.
    /// At most one refusal per component. A document that cannot be read is one S07-003 naming <c>unknown</c>, on
    /// <c>resolve.json</c> when it is that one, else on <c>lock.json</c>. A null argument is the caller's error.
    /// </summary>
    public static IReadOnlyList<SourceRefusal> Engine(JsonObject resolve, JsonObject @lock, EngineCapabilities engine)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        ArgumentNullException.ThrowIfNull(@lock);
        ArgumentNullException.ThrowIfNull(engine);

        // A document parsed from text that repeats a member throws when first read: it is refused whole, as unknown.
        var resolveReadable = Readable(resolve);
        var lockReadable = Readable(@lock);
        if (!resolveReadable || !lockReadable)
        {
            var (name, at) = Named(resolveReadable ? resolve : [], lockReadable ? @lock : []);
            var path = resolveReadable ? ResolveCompiler.LockFile : CompiledPackage.ResolveFile;
            return [ResolveSentences.ComponentOutOfRange(name, at, Unknown, Unknown, Unknown, path)];
        }

        var (package, version) = Named(resolve, @lock);
        var requires = LockCompiler.Member(resolve, "requires");
        var libraries = LockCompiler.Member(@lock, "libraries");
        var refusals = new List<SourceRefusal>();

        void Component(string component, string? range, string? locked, string engineVersion)
        {
            var path = !CaretRange.Admits(range ?? "", engineVersion) ? CompiledPackage.ResolveFile
                : !CaretRange.IsVersion(locked) || BeforeABreakPoint(engine, component, locked, engineVersion) ? ResolveCompiler.LockFile
                : null;
            if (path != null)
                refusals.Add(ResolveSentences.ComponentOutOfRange(package, version, component, range ?? Unknown, engineVersion, path));
        }

        Component(Scriban, Text(requires, "scriban"), Text(libraries, "Scriban"), engine.Scriban ?? "");
        Component(Evaluator, Text(requires, "evaluator"), Text(@lock, "evaluator"), engine.Evaluator ?? "");

        var lockedOperations = LockCompiler.Member(@lock, "operations");
        if (LockCompiler.Member(requires, "operations") is JsonObject operations)
        {
            foreach (var (name, range) in operations.OrderBy(o => o.Key, StringComparer.Ordinal))
            {
                Component(name, SourceLayout.Text(range), Text(lockedOperations, name),
                    engine.Operations?.GetValueOrDefault(name) ?? None);
            }
        }
        else
        {
            refusals.Add(ResolveSentences.ComponentOutOfRange(package, version, "operations", Unknown, Unknown, CompiledPackage.ResolveFile));
        }

        // The other locked libraries (Markdig, the two Corvus libraries) have no range: only their break points hold them,
        // each against the engine's own version of the library; an engine that does not say it, or says what is no version,
        // is held to it, and so is a lock that does not say it (a missing locked version is before every break point).
        // A break point of Scriban, the evaluator or an operation was held above.
        var operationNames = JsonLogicOperations.All.Select(o => o.Name)
            .Concat(engine.Operations?.Keys ?? [])
            .Concat((LockCompiler.Member(requires, "operations") as JsonObject)?.Select(o => o.Key) ?? [])
            .ToHashSet(StringComparer.Ordinal);
        foreach (var point in (engine.BreakPoints ?? []).Where(p => p?.Component is { } c && c != Scriban && c != Evaluator && !operationNames.Contains(c))
                     .GroupBy(p => p.Component, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var locked = Text(libraries, point.Key);
            var running = engine.Libraries?.GetValueOrDefault(point.Key) is { } v && CaretRange.IsVersion(v) ? v : null;
            if (point.Any(p => CaretRange.Compare(locked ?? "", p.Version) < 0 && (running == null || CaretRange.Compare(running, p.Version) >= 0)))
            {
                refusals.Add(ResolveSentences.ComponentOutOfRange(
                    package, version, point.Key, locked == null ? Unknown : CaretRange.From(locked), running ?? Unknown, ResolveCompiler.LockFile));
            }
        }

        Supported(refusals, package, version, LockCompiler.Member(requires, "kinds"), "kind", engine.Kinds);
        Supported(refusals, package, version, LockCompiler.Member(requires, "features"), "feature", engine.Features);

        if (LockCompiler.Member(requires, "agents") is JsonObject agents)
        {
            foreach (var (name, written) in agents.OrderBy(a => a.Key, StringComparer.Ordinal))
            {
                var text = SourceLayout.Text(written);
                var number = text is { Length: > 0 and <= 9 } && text.All(char.IsAsciiDigit)
                    ? int.Parse(text, CultureInfo.InvariantCulture)
                    : (int?)null;
                if (number == null || engine.Agents?.GetValueOrDefault(name) is not { } versions || !versions.Contains(number.Value))
                    refusals.Add(ResolveSentences.AgentMissing(package, version, name, text ?? Unknown, CompiledPackage.ResolveFile));
            }
        }
        else
        {
            refusals.Add(ResolveSentences.AgentMissing(package, version, Unknown, Unknown, CompiledPackage.ResolveFile));
        }

        return refusals;
    }

    /// <summary>
    /// The refusals of starting a job of the package <paramref name="resolve"/> describes for an account whose profile is
    /// <paramref name="profile"/> (field to value; part 07 § 3.2, § 10): for each field <c>needs.profile</c> lists, in its
    /// order, whose value is missing, empty or only whitespace, S06-031 for the signature and S06-032 with the field's
    /// label (<c>v31/profile.json</c>) for any other. A <c>needs.profile</c> that is missing or is not a list of names is
    /// read as needing every field, so nothing unset gets through. A null argument is the caller's error.
    /// </summary>
    public static IReadOnlyList<SourceRefusal> Profile(JsonObject resolve, IReadOnlyDictionary<string, string?> profile)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        ArgumentNullException.ThrowIfNull(profile);

        var needs = Readable(resolve) ? LockCompiler.Member(LockCompiler.Member(resolve, "needs"), "profile") : null;
        var fields = Strings(needs) ?? Fields.Select(f => f.Field).ToList();
        var refusals = new List<SourceRefusal>();
        foreach (var field in fields.Distinct(StringComparer.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(profile.GetValueOrDefault(field)))
                continue;
            refusals.Add(field == "signature"
                ? ResolveSentences.SignatureNotSet()
                : ResolveSentences.ProfileFieldNotSet(Fields.FirstOrDefault(f => f.Field == field).Label ?? field));
        }

        return refusals;
    }

    /// <summary>S07-004 for each name of <paramref name="list"/> not in <paramref name="supported"/>; once, naming <c>unknown</c>, for each that is not text, or for a list that is not one.</summary>
    private static void Supported(
        List<SourceRefusal> refusals, string package, string version, JsonNode? list, string kindOrFeature, IReadOnlySet<string>? supported)
    {
        if (list is not JsonArray names)
        {
            refusals.Add(ResolveSentences.NotSupported(package, version, kindOrFeature, Unknown, CompiledPackage.ResolveFile));
            return;
        }

        foreach (var name in names.Select(SourceLayout.Text))
        {
            if (name == null || supported == null || !supported.Contains(name))
                refusals.Add(ResolveSentences.NotSupported(package, version, kindOrFeature, name ?? Unknown, CompiledPackage.ResolveFile));
        }
    }

    /// <summary>
    /// Whether every object under <paramref name="document"/> can be read: one parsed from text that repeats a member
    /// throws <see cref="ArgumentException"/> when it is first enumerated or looked into.
    /// </summary>
    private static bool Readable(JsonNode document)
    {
        var pending = new Stack<JsonNode?>([document]);
        try
        {
            while (pending.Count > 0)
            {
                switch (pending.Pop())
                {
                    case JsonObject o:
                        foreach (var (_, value) in o)
                            pending.Push(value);
                        break;
                    case JsonArray a:
                        foreach (var value in a)
                            pending.Push(value);
                        break;
                }
            }
        }
        catch (ArgumentException)
        {
            return false;
        }

        return true;
    }

    /// <summary>Whether <paramref name="locked"/> is before a break point of <paramref name="component"/> that <paramref name="engineVersion"/> is at or past.</summary>
    private static bool BeforeABreakPoint(EngineCapabilities engine, string component, string? locked, string engineVersion) =>
        (engine.BreakPoints ?? []).Any(p => p != null && p.Component == component
            && CaretRange.Compare(locked ?? "", p.Version) < 0 && CaretRange.Compare(engineVersion, p.Version) >= 0);

    /// <summary>The package's name and version, <c>resolve.json</c>'s, else <c>lock.json</c>'s, else <c>unknown</c>.</summary>
    private static (string Package, string Version) Named(JsonObject resolve, JsonObject @lock)
    {
        var fromResolve = LockCompiler.Member(resolve, "package");
        var fromLock = LockCompiler.Member(@lock, "package");
        return (
            Text(fromResolve, "name") ?? Text(fromLock, "name") ?? Unknown,
            Text(fromResolve, "version") ?? Text(fromLock, "version") ?? Unknown);
    }

    /// <summary>The member <paramref name="name"/> of <paramref name="node"/> when it is non-empty text; else null.</summary>
    private static string? Text(JsonNode? node, string name) =>
        SourceLayout.Text(LockCompiler.Member(node, name)) is { Length: > 0 } text ? text : null;

    /// <summary>The names of <paramref name="node"/> when it is a list of text; else null.</summary>
    private static List<string>? Strings(JsonNode? node)
    {
        if (node is not JsonArray list)
            return null;
        var names = new List<string>();
        foreach (var name in list.Select(SourceLayout.Text))
        {
            if (name == null)
                return null;
            names.Add(name);
        }

        return names;
    }
}
