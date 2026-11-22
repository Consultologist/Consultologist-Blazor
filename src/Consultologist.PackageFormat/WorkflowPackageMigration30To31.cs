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
/// <c>"release"</c>. <paramref name="FolderFormats"/> (part 01 § 3, part 05 § 6.2) maps a snippet folder's id to the
/// package format its items' headers are values of; a folder with no entry takes <c>&lt;folder&gt;_item</c>.
/// </summary>
public sealed record Migration31Options(
    V31.TypeRegistry Registry,
    V31.Toolchain Toolchain,
    WorkflowFormatCatalog Formats30,
    Func<JsonNode, string?>? ResolveContract = null,
    IReadOnlyDictionary<string, string>? Renames = null,
    IReadOnlyDictionary<string, string>? WhenEmpty = null,
    IReadOnlyDictionary<string, string>? FolderFormats = null);

/// <summary>
/// #1507 (part 10 § 1.1, D1): what the 30 → 31 migration produced, in the earlier rewriters' shape so that every caller
/// treats every rung alike: the 31 manifest, the 31 files (without <c>manifest.json</c>), every notice, and the
/// refusals that stopped it (a refusal writes nothing: the result holds the input's manifest and files).
/// <see cref="HandEdits"/> are the S10-002 notices, a subset of <see cref="Migration31Result.Notices"/>;
/// <see cref="Report"/> is what the migration report lists of the rest; <see cref="Keys"/> maps each 30 node id to its
/// 31 rooted key and each deliverable's 30 key <c>deliverable.&lt;id&gt;</c> to its 31 key.
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

    /// <summary>
    /// Each 30 node id to its 31 rooted key; each deliverable's 30 key <c>deliverable.&lt;id&gt;</c> to its 31 key (the
    /// root is kept in the deliverable's entry, so a node and a deliverable of one id, as <c>composer-sources</c>' <c>plan</c>,
    /// keep an entry each).
    /// </summary>
    public IReadOnlyDictionary<string, string> Keys { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>
/// #1507 (part 10 § 3.2, D1, D9): the notices that need no edit, as the migration report lists them: the renames
/// (A10-006, S10-006), the macro choices now inputs with a <c>_macro</c> suffix, the <c>!x</c> converted to
/// <c>blank(x)</c> (A10-008) and each <c>trim(x) == ""</c> whose <c>blank(x)</c> answers otherwise for a null <c>x</c>
/// (A10-050), the suggested text formats (A10-021), and the <c>reproducible</c> claims that differed (A10-010).
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
/// refused, as is a rung's own refusal or validation error (A10-017) and a rung's failure on the package (A10-018);
/// a refusal writes nothing. It never throws on package content and is
/// deterministic.
/// </summary>
public static class WorkflowPackageMigration30To31
{
    /// <summary>The version the migration writes.</summary>
    public const int Target = 31;

    /// <summary>
    /// <paramref name="manifestJson"/> and <paramref name="files"/> (without <c>manifest.json</c>) migrated to 31 with
    /// <paramref name="options"/>. The 30 package (raised by the chain when older) is read and checked at 30
    /// (<see cref="Migrated.Read"/>: a file that is not strict JSON, A10-019; a package 30 refuses, A10-017; a reader that
    /// fails on it, A10-020), its reserved and long ids renamed (<see cref="V31.Migration.Migrate.Renames"/>, § 1.9: a hand
    /// choice that cannot be made, A10-033), then rewritten pass by pass on one working set (<see cref="Migrated"/>): the layout, the
    /// manifest and the formats (<see cref="V31.Migration.Migrate.Layout"/>), the inputs
    /// (<see cref="V31.Migration.Migrate.Inputs"/>), the nodes, the flows and their conditions
    /// (<see cref="V31.Migration.Migrate.Nodes"/>), the template bodies (<see cref="V31.Migration.Migrate.Templates"/>), the
    /// macros and their choices (<see cref="V31.Migration.Migrate.Macros"/>), the checks
    /// (<see cref="V31.Migration.Migrate.Checks"/>), the deliverables (<see cref="V31.Migration.Migrate.Deliverables"/>) and,
    /// last, every header's derived flags (<see cref="V31.Migration.Migrate.Derived"/>, D15). The files are serialised once,
    /// at the end.
    /// </summary>
    public static Migration31Result Migrate(string manifestJson, IReadOnlyDictionary<string, string> files, Migration31Options options)
    {
        ArgumentNullException.ThrowIfNull(manifestJson);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(options);

        // Part 01 § 2.4: line endings and a byte-order mark are normalised first, as import does, so the gates and every
        // rung read the text a checkout's settings cannot change.
        var json = Migrated.Normalise(manifestJson);
        var normalised = files.ToDictionary(f => f.Key, f => Migrated.Normalise(f.Value), StringComparer.Ordinal);

        if (!WorkflowPackageMigrationChain.TryReadVersion(json, out _, out var from))
        {
            return Refused(manifestJson, files, [MigrationSentences.ManifestDoesNotRead]);
        }

        if (from >= Target)
        {
            return Refused(manifestJson, files, [MigrationSentences.AlreadyAt(from)]);
        }

        var at30 = WorkflowPackageMigrationChain.To30(json, normalised, options.ResolveContract, options.Formats30);
        if (!at30.Succeeded)
        {
            return Refused(manifestJson, files, at30.Refusals);
        }

        var (read, refusals) = Migrated.Read(at30.ManifestJson, at30.Files, options);
        if (read is null)
        {
            return Refused(manifestJson, files, refusals);
        }

        // § 1.9: the reserved and long ids are renamed on the 30 package before any 31 file is written.
        var (migrated, renameRefusals) = V31.Migration.Migrate.Renames.Apply(read);
        if (migrated is null)
        {
            return Refused(manifestJson, files, renameRefusals);
        }

        // A10-026: a lone \r became a line end (part 01 § 2.4 refuses one), so the file's bytes changed there.
        if (Migrated.HasLoneCarriageReturn(manifestJson))
        {
            migrated.Notice(MigrationSentences.LoneCarriageReturn("manifest.json"));
        }

        foreach (var file in files.Keys.Where(k => Migrated.HasLoneCarriageReturn(files[k])).Order(StringComparer.Ordinal))
        {
            migrated.Notice(MigrationSentences.LoneCarriageReturn(file));
        }

        V31.Migration.Migrate.Layout(migrated);
        V31.Migration.Migrate.Inputs(migrated);
        V31.Migration.Migrate.Nodes(migrated);
        V31.Migration.Migrate.Templates(migrated);
        V31.Migration.Migrate.Macros(migrated);
        V31.Migration.Migrate.Checks(migrated);
        V31.Migration.Migrate.Deliverables(migrated);
        V31.Migration.Migrate.Derived(migrated);
        return migrated.Result(at30.Notices);
    }

    private static Migration31Result Refused(string manifestJson, IReadOnlyDictionary<string, string> files, IReadOnlyList<string> refusals) =>
        new(manifestJson, new Dictionary<string, string>(files, StringComparer.Ordinal), [], refusals);
}
