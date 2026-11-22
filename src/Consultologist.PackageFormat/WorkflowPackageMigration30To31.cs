using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Migration;

namespace Consultologist.PackageFormat;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 1.1, D1): what the migration is given besides the package.
/// <paramref name="Registry"/> and <paramref name="Toolchain"/> are 31's (the published types the migrated package is
/// checked against, and the toolchain <c>lock.json</c> records); <paramref name="Formats30"/> is 30's catalogue (each
/// format's shape, a classifier's values), which the 30 package is read with. <paramref name="ResolveContract"/> is the
/// 24 → 25 rung's, for a package older than 25 (<see cref="WorkflowPackageMigrationChain"/>). <paramref name="Renames"/>
/// (D6, § 1.9) maps a reserved 30 id to its hand-chosen 31 id; an id with no entry takes its kind as a suffix.
/// <paramref name="WhenEmpty"/> (D7, § 1.10) maps a deliverable id to its <c>whenEmpty</c>; the default is
/// <c>"release"</c>.
/// </summary>
public sealed record Migration31Options(
    V31.TypeRegistry Registry,
    V31.Toolchain Toolchain,
    WorkflowFormatCatalog Formats30,
    Func<JsonNode, string?>? ResolveContract = null,
    IReadOnlyDictionary<string, string>? Renames = null,
    IReadOnlyDictionary<string, string>? WhenEmpty = null);

/// <summary>
/// #1507 (part 10 § 1.1, D1): what the 30 → 31 migration produced, in the earlier rewriters' shape so that every caller
/// treats every rung alike: the 31 manifest, the 31 files (without <c>manifest.json</c>), every notice, and the
/// refusals that stopped it (a refusal writes nothing: the result holds the input's manifest and files).
/// <see cref="HandEdits"/> are the S10-002 notices, a subset of <see cref="Migration31Result.Notices"/>;
/// <see cref="Report"/> is what the migration report lists of the rest; <see cref="Keys"/> maps each 30 node id to its
/// 31 rooted key and each deliverable id to <c>deliverable.&lt;id&gt;</c>.
/// </summary>
public sealed record Migration31Result(
    string ManifestJson,
    Dictionary<string, string> Files,
    IReadOnlyList<string> Notices,
    IReadOnlyList<string> Refusals)
{
    /// <summary>Whether the migration wrote a package: no refusal.</summary>
    public bool Succeeded => Refusals.Count == 0;

    /// <summary>The hand-edit notices (S10-002, § 1.4), in <see cref="Notices"/>' order.</summary>
    public IReadOnlyList<string> HandEdits { get; init; } = [];

    /// <summary>The notices that need no edit, by what the migration report lists them as (D9).</summary>
    public MigrationNotes Report { get; init; } = MigrationNotes.None;

    /// <summary>Each 30 node id to its 31 rooted key, each deliverable id to <c>deliverable.&lt;id&gt;</c>.</summary>
    public IReadOnlyDictionary<string, string> Keys { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>
/// #1507 (part 10 § 3.2, D1, D9): the notices that need no edit, as the migration report lists them: the renames
/// (A10-006, S10-006), the macro choices now inputs with a <c>_macro</c> suffix, the <c>!x</c> converted to
/// <c>blank(x)</c> (A10-008), the suggested text formats (A10-009), and the <c>reproducible</c> claims that differed
/// (A10-010).
/// </summary>
public sealed record MigrationNotes(
    IReadOnlyList<string> Renames,
    IReadOnlyList<string> MacroInputs,
    IReadOnlyList<string> BlankConversions,
    IReadOnlyList<string> Suggestions,
    IReadOnlyList<string> Reproducible)
{
    /// <summary>No notes.</summary>
    public static MigrationNotes None { get; } = new([], [], [], [], []);
}

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 1): the migration of a workflow package from specVersion 30 to 31, beside
/// the 24 → 25, 25 → 26 and 27 → 28 rewriters. It reads specVersion 30; an older package is first raised through the
/// existing chain in the same run (<see cref="WorkflowPackageMigrationChain.To30"/>). Its gates (§ 1.4, D2): a manifest
/// that does not read (A10-016), a version below 5 (A10-012) and a package already at 31 or past it (A10-015) are
/// refused, as is a rung's own refusal (A10-017); a refusal writes nothing. It never throws on package content and is
/// deterministic.
/// </summary>
public static class WorkflowPackageMigration30To31
{
    /// <summary>The version the migration writes.</summary>
    public const int Target = 31;

    /// <summary>
    /// <paramref name="manifestJson"/> and <paramref name="files"/> (without <c>manifest.json</c>) migrated to 31 with
    /// <paramref name="options"/>. The rewrite itself is built pass by pass (Tasks 6 to 12); until then a 30 package's
    /// manifest has its <c>specVersion</c> moved and its files are kept.
    /// </summary>
    public static Migration31Result Migrate(string manifestJson, IReadOnlyDictionary<string, string> files, Migration31Options options)
    {
        ArgumentNullException.ThrowIfNull(manifestJson);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(options);

        if (!WorkflowPackageMigrationChain.TryReadVersion(manifestJson, out _, out var from))
        {
            return Refused(manifestJson, files, [MigrationSentences.ManifestDoesNotRead]);
        }

        if (from >= Target)
        {
            return Refused(manifestJson, files, [MigrationSentences.AlreadyAt(from)]);
        }

        var at30 = WorkflowPackageMigrationChain.To30(manifestJson, files, options.ResolveContract);
        if (!at30.Succeeded)
        {
            return Refused(manifestJson, files, at30.Refusals);
        }

        var root = JsonNode.Parse(at30.ManifestJson)!.AsObject();
        root["specVersion"] = Target;
        return new Migration31Result(
            root.ToJsonString(WorkflowPackageManifestJson.WriteOptions),
            new Dictionary<string, string>(at30.Files, StringComparer.Ordinal),
            at30.Notices.ToList(),
            []);
    }

    private static Migration31Result Refused(string manifestJson, IReadOnlyDictionary<string, string> files, IReadOnlyList<string> refusals) =>
        new(manifestJson, new Dictionary<string, string>(files, StringComparer.Ordinal), [], refusals);
}
