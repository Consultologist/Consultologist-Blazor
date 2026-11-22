using System.Text;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 1.2 the node-headers row; part 02 §§ 1.1, 1.2, § 11's last bullet; D15): the
/// derived-flags pass, last. The earlier passes wrote every node and check header with placeholder flags (Task 7: a 30
/// claim or the kind's default for <c>reproducible</c>, <c>false</c> for the three contagious flags) and each flow's
/// format from its <c>ret</c>s. This pass reads the 31 set as the checker reads it, works out every header's values with
/// <see cref="SourceDerived.Values"/> (the derivation without the header checks), and writes them.
/// </summary>
internal static partial class Migrate
{
    /// <summary>
    /// The derived-flags pass. The 31 files are read as an upload (<see cref="SourceParser.Parse"/>), then held to the
    /// shape and type rules and resolved (<see cref="SourceResolve.Check"/>), whose results the derivation reads; the
    /// bodies are read by the dialect (<see cref="V31.Templates.SourceTemplates.Check"/>) when it refuses nothing, and are
    /// otherwise unknown. A hand edit is written in a form the checker refuses: one the reader, the shape or type rules or
    /// the resolver refuse leaves every header's placeholders as they are; one the expressions or the dialect refuse still
    /// lets the flags be derived, a body the dialect refused counting as unknown (a flag that is then <c>false</c> is the
    /// placeholder, which is <c>false</c> too). A <c>reproducible</c> with no rule for its kind keeps its placeholder.
    /// Each 30 node whose <c>reproducible</c> claim differs from the value written is listed (A10-010,
    /// <see cref="Migrated.Reproducible"/>). Never throws on package content.
    /// </summary>
    public static void Derived(Migrated m)
    {
        ArgumentNullException.ThrowIfNull(m);
        var values = DerivedValues(m);
        foreach (var (path, flags) in values)
        {
            if (!m.Sources.TryGetValue(path, out var file))
                continue;
            if (flags.Reproducible is { } reproducible)
                file.Json["reproducible"] = reproducible;
            file.Json["modelDerived"] = flags.ModelDerived;
            file.Json["runDependent"] = flags.RunDependent;
            file.Json["profileCarrying"] = flags.ProfileCarrying;
        }

        // Part 02 § 11: each 30 node whose reproducible claim differs from the value written, in 30's order.
        var tree = new NodeTree(m.Manifest30.Nodes ?? []);
        foreach (var (id, node) in tree.ById)
        {
            if (node.Reproducible is not { } claimed || HeaderOf(m, tree, id) is not { } header
                || header["reproducible"] is not JsonValue value || !value.TryGetValue<bool>(out var written) || written == claimed)
            {
                continue;
            }

            var notice = MigrationSentences.ReproducibleDiffered(id, claimed, written);
            m.Notice(notice);
            m.Reproducible.Add(notice);
        }
    }

    /// <summary>The header node <paramref name="id"/> was written with: its check's, its flow's <c>index.json</c> or its leaf's; null when it has none.</summary>
    private static JsonObject? HeaderOf(Migrated m, NodeTree tree, string id)
    {
        var node = tree.ById[id];
        var path = IsCheck(m, node) ? $"checks/{id}.md"
            : WorkflowImpliedPaths.IsFlow(node) ? FlowFile(tree, id)
            : $"{FolderOf(tree, id)}/{id}.md";
        return m.Sources.GetValueOrDefault(path)?.Json;
    }

    /// <summary>
    /// The derived values of the 31 set as written so far, by header file; empty when a step before the derivation's
    /// inputs refuses it (a hand edit there).
    /// </summary>
    private static IReadOnlyDictionary<string, DerivedFlags> DerivedValues(Migrated m)
    {
        var none = new Dictionary<string, DerivedFlags>(StringComparer.Ordinal);
        var upload = m.Files().ToDictionary(f => f.Key, f => Encoding.UTF8.GetBytes(f.Value), StringComparer.Ordinal);
        upload["manifest.json"] = Encoding.UTF8.GetBytes(Migrated.Json(m.Manifest));

        var registry = m.Options.Registry;
        var parse = SourceParser.Parse(upload, registry.Names);
        if (parse.Package is not { } package || SourceChecks.Check(package, null).Count > 0)
            return none;
        var types = SourceTypes.Check(package, registry);
        if (types.Types is not { } typed)
            return none;
        var (_, resolution) = SourceResolve.Check(package, typed);
        if (resolution is null)
            return none;
        var bodies = V31.Templates.SourceTemplates.Check(package, typed, resolution, registry).Templates;
        return SourceDerived.Values(package, resolution, bodies, bodies);
    }
}
