using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1502 (specVersion 31, area 05): <c>resolve.json</c> compiled (part 07 § 2). <paramref name="Document"/> is the
/// document, every member of § 2.2 present; <paramref name="Canonical"/> its RFC 8785 text
/// (<see cref="CanonicalJson.Write"/>), the bytes publish stores; <paramref name="Hash"/> its <c>sha256:</c>. All three
/// are null exactly when <paramref name="Refusals"/> is not empty: the check's, or the compiler's own (A05-004).
/// <paramref name="Warnings"/> are the document's <c>warnings</c>, in its order. <paramref name="Complete"/> is false
/// while a body's reads are not known (D1): the document is then written from the headers alone, and is not the one to
/// publish.
/// </summary>
public sealed record ResolveResult(
    JsonObject? Document,
    string? Canonical,
    string? Hash,
    IReadOnlyList<SourceRefusal> Refusals,
    IReadOnlyList<SourceRefusal> Warnings,
    bool Complete);

/// <summary>
/// #1502 (specVersion 31, area 05): writes <c>resolve.json</c>, the one document the engine runs (part 07 §§ 1 to 3),
/// from what <see cref="SourceCheck.Run"/> worked out: the members of § 2.2 in its order (this file); the inputs and the
/// flat maps of §§ 2.3 and 2.5, every header member as written and canonicalised (D3) with the derived members after
/// (<c>ResolveCompiler.Entries.cs</c>); the graph of § 2.7 (D7, <c>ResolveCompiler.Graph.cs</c>); <c>requires</c> and
/// <c>needs</c> of § 3 (D8, <c>ResolveCompiler.Requires.cs</c>). The formats, their hashes and the bindings are
/// <see cref="CompiledFormats"/>'s; <c>sourceHash</c> is <see cref="SourceHash"/>'s. No clock, no dictionary order: the
/// same source and the same toolchain give the same bytes. Never throws on package content.
/// </summary>
public static partial class ResolveCompiler
{
    /// <summary><c>resolve.json</c>'s own shape at 31 (part 07 § 2.2).</summary>
    public const int DocumentVersion = 1;

    /// <summary>The rules' version, the only one this compiler writes (part 07 § 2.2).</summary>
    public const int SpecVersion = 31;

    /// <summary>The companion's file name (part 07 § 1).</summary>
    public const string LockFile = "lock.json";

    /// <summary>
    /// <c>resolve.json</c> of the source <paramref name="check"/> accepted, its formats from <paramref name="registry"/>,
    /// <c>requires.scriban</c> the range of <paramref name="scribanVersion"/> (the locked Scriban version, D8), and part
    /// 04 § 10.2's notes among the warnings when <paramref name="limits"/> are given (D11). A check with refusals gives
    /// them back with no document. A null argument other than <paramref name="limits"/> is the caller's error.
    /// </summary>
    public static ResolveResult Compile(SourceCheckResult check, TypeRegistry registry, string scribanVersion, PublishLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(scribanVersion);

        if (check.Refusals.Count > 0)
            return new ResolveResult(null, null, null, check.Refusals, [], false);
        if (check.Parse.Package is not { } package || check.Types is not { } types || check.Resolution is not { } resolution
            || check.Derived is not { } derived)
        {
            return new ResolveResult(null, null, null, check.Refusals, [], false);
        }

        var builtins = BuiltInsUsed(derived, registry);
        var formats = CompiledFormats.Build(package, types, registry, builtins.SelectMany(b => new[] { b.Header["input"], b.Header["format"] }));
        if (formats.Refusals.Count > 0)
            return new ResolveResult(null, null, null, formats.Refusals, [], false);

        var context = new Context(check, package, types, resolution, derived, formats) { BuiltIns = builtins };
        var warnings = Warnings(context, limits);
        var document = Document(context, scribanVersion, warnings);
        var canonical = CanonicalJson.Write(document);
        return new ResolveResult(document, canonical, CanonicalJson.HashText(canonical), [], warnings, derived.Complete);
    }

    /// <summary>
    /// The members of part 07 § 2.2, in its order, with the manifest's <c>derivedFrom</c> after <c>package</c>: a member
    /// that only steers publish, carried under its own name (§ 2.2; part 01 § 3), the name@version or null.
    /// </summary>
    private static JsonObject Document(Context context, string scribanVersion, IReadOnlyList<SourceRefusal> warnings)
    {
        var manifest = context.Package.Manifest;
        var entries = new Entries(context);
        var functions = entries.Functions();
        var maps = entries.Maps();
        return new JsonObject
        {
            ["documentVersion"] = DocumentVersion,
            ["specVersion"] = SpecVersion,
            ["package"] = new JsonObject { ["name"] = manifest.Name, ["version"] = manifest.Version },
            ["derivedFrom"] = manifest.DerivedFrom,
            ["title"] = manifest.Title ?? $"{manifest.Name}@{manifest.Version}",
            ["description"] = manifest.Description == null ? null : string.Join('\n', manifest.Description),
            ["tags"] = new JsonArray(manifest.Tags.Select(t => (JsonNode?)t).ToArray()),
            ["sourceHash"] = SourceHash.Of(context.Check.Parse.Files),
            ["lock"] = LockFile,
            ["requires"] = Requires(context, scribanVersion),
            ["needs"] = new JsonObject { ["profile"] = Strings(context.Derived.ProfileFields) },
            ["inputs"] = entries.Inputs(),
            ["inputOrder"] = Strings(manifest.Inputs.Select(i => i.Id)),
            ["formats"] = context.Formats.Bundle.DeepClone(),
            ["formatHashes"] = context.Formats.FormatHashes.DeepClone(),
            ["typeHashes"] = context.Formats.TypeHashes.DeepClone(),
            ["bindings"] = context.Formats.Bindings.DeepClone(),
            ["nodes"] = maps.Nodes,
            ["checks"] = maps.Checks,
            ["functions"] = functions,
            ["renderers"] = maps.Renderers,
            ["snippets"] = maps.Snippets,
            ["macros"] = maps.Macros,
            ["deliverables"] = maps.Deliverables,
            ["graph"] = Graph(context),
            ["run"] = Strings(context.Derived.RunFacts),
            ["warnings"] = new JsonArray(warnings.Select(w => (JsonNode?)new JsonObject
            {
                ["sentence"] = w.Sentence,
                ["at"] = w.Path.Length == 0 ? null : w.Path,
            }).ToArray()),
        };
    }

    /// <summary>
    /// Every warning publish gives (part 07 § 2.8): the derived step's (S03-037 and S03-038 for every run-dependent
    /// decision and computed value, which say the expressions' own warnings for every site; S04-074 for definitions),
    /// the resolve step's (S04-074 for formats), the dialect's (#1504: S05-060 for each run spot, S05-061, each with its
    /// file), and the notes; ordered by path, the package's (<c>""</c>) first, then as found; each said once.
    /// </summary>
    private static List<SourceRefusal> Warnings(Context context, PublishLimits? limits)
    {
        var found = context.Derived.Warnings.Concat(context.Resolution.Warnings).Concat(context.Check.Templates?.Warnings ?? []);
        if (limits != null)
            found = found.Concat(PublishNotes.Of(context.Package, context.Resolution, limits));
        return found.Distinct().OrderBy(w => w.Path, StringComparer.Ordinal).ToList();
    }

    private static JsonArray Strings(IEnumerable<string> values) => new(values.Select(v => (JsonNode?)v).ToArray());

    /// <summary>
    /// #1504 (D6; part 05 § 5.3): each built-in renderer an entry pipes into (a package's or a function's), ordinal by
    /// id: <c>resolve.json</c> embeds it under <c>renderers</c>, and the formats it reads and returns are bundled.
    /// </summary>
    private static List<BuiltInRenderer> BuiltInsUsed(PackageDerived derived, TypeRegistry registry) =>
        AllEntries(derived).SelectMany(e => e.Body?.Renders ?? []).Select(r => r.Renderer)
            .Where(registry.BuiltInRendererIds.Contains).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(id => registry.BuiltInRenderers[id]).ToList();

    /// <summary>What one compilation reads: the check's results and the compiled formats.</summary>
    private sealed record Context(
        SourceCheckResult Check,
        SourcePackage Package,
        PackageTypes Types,
        Resolution Resolution,
        PackageDerived Derived,
        CompiledFormats Formats)
    {
        public ScopeTree Tree => Resolution.Tree;

        /// <summary>#1504 (D6): the built-in renderers the package pipes into, ordinal by id.</summary>
        public IReadOnlyList<BuiltInRenderer> BuiltIns { get; init; } = [];

        /// <summary>
        /// #1503 (D3): each checked site's <see cref="ExpressionCheck.PlainConverts"/>, by the declaration that holds it
        /// (by reference) and its member; #1504 (D2): an agent part's <c>if</c> under <c>parts.&lt;id&gt;.if</c>
        /// (<see cref="PartMember"/>), for its placement.
        /// </summary>
        public IReadOnlyDictionary<object, Dictionary<string, IReadOnlyList<bool>>> Plains { get; } = PlainsOf(Check.Expressions);

        /// <summary>The member a part's <c>if</c> is held under in <see cref="Plains"/>.</summary>
        public static string PartMember(string part) => $"parts.{part}.if";

        private static Dictionary<object, Dictionary<string, IReadOnlyList<bool>>> PlainsOf(PackageExpressions? expressions)
        {
            var plains = new Dictionary<object, Dictionary<string, IReadOnlyList<bool>>>(ReferenceEqualityComparer.Instance);
            foreach (var site in expressions?.Sites ?? [])
            {
                if (!plains.TryGetValue(site.Context.Declaration, out var members))
                    plains[site.Context.Declaration] = members = new Dictionary<string, IReadOnlyList<bool>>(StringComparer.Ordinal);
                members[site.Context.PartId is { } part ? PartMember(part) : site.Context.Site.Member] = site.Check.PlainConverts;
            }

            return plains;
        }

        /// <summary>The format scope of <paramref name="function"/>, or the package's.</summary>
        public FormatSet Scope(SourceFunction? function) =>
            function != null && Types.FunctionFormats.TryGetValue(function.Id, out var own) ? own : Types.Formats;

        /// <summary>The derived values of the entry <paramref name="key"/>, the package's or <paramref name="function"/>'s.</summary>
        public DerivedEntry? Derive(string key, SourceFunction? function) =>
            function == null
                ? Derived.Entries.GetValueOrDefault(key)
                : Derived.FunctionEntries.TryGetValue(function.Id, out var own) ? own.GetValueOrDefault(key) : null;
    }
}
