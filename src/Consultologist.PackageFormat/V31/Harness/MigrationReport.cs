using System.Text;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.PackageFormat.V31.Harness;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 3.2, D9; <c>migration-report.schema.json</c>): <c>migration-report.json</c>:
/// the version migrated from and to, the hand edits made, every notice that needs no edit, the choices the package was
/// migrated with (<see cref="Options"/>, #1508 R18: its <c>options.json</c>, <see cref="Migration31Options.ToJson"/>'s
/// shape), and one entry per compared text (<see cref="ComparedText"/>). Reported, never a gate. <see cref="ToJson"/> writes it canonical (RFC 8785,
/// <see cref="CanonicalJson"/>), as the other derived files are; <see cref="Of"/> builds it from a migration and its
/// comparisons. No member carries a value from an input, a model or a profile: ids, package text, sentences and hashes only.
/// </summary>
public sealed record MigrationReport(
    string OldVersion,
    string NewVersion,
    IReadOnlyList<string> HandEdits,
    IReadOnlyList<string> Notices,
    JsonObject Options,
    IReadOnlyList<ComparedText> Texts)
{
    /// <summary>The report's file name in a version's folder, beside the publish report (part 10 § 3.2).</summary>
    public const string FileName = "migration-report.json";

    /// <summary>
    /// The report as its schema has it, every member present (<c>explanation</c> <c>null</c> unless <c>expected</c>), as
    /// canonical JSON text (RFC 8785: members in code-point order, no white space).
    /// </summary>
    public string ToJson()
    {
        static JsonArray Strings(IEnumerable<string> values) => new([.. values.Select(v => (JsonNode?)JsonValue.Create(v))]);
        var texts = new JsonArray();
        foreach (var text in Texts)
        {
            texts.Add(new JsonObject
            {
                ["site"] = text.Site,
                ["oldHash"] = text.OldHash,
                ["newHash"] = text.NewHash,
                ["equal"] = text.Equal,
                ["expected"] = text.Expected,
                ["explanation"] = text.Expected ? text.Explanation : null,
            });
        }

        return CanonicalJson.Write(new JsonObject
        {
            ["oldVersion"] = OldVersion,
            ["newVersion"] = NewVersion,
            ["handEdits"] = Strings(HandEdits),
            ["notices"] = Strings(Notices),
            ["options"] = Options.DeepClone(),
            ["texts"] = texts,
        });
    }

    /// <summary>
    /// The report of <paramref name="migrated"/> (from <paramref name="oldVersion"/>; the new version is its manifest's),
    /// migrated with the choices <paramref name="options"/> (R18: the package's <c>options.json</c>, recorded as given),
    /// over <paramref name="texts"/>: <c>handEdits</c> its hand-edit notices (S10-002, the edits an operator makes);
    /// <c>notices</c> every other notice of the migration, in its order, then A10-063 when the package has more than one
    /// deliverable (R8: each is delivered in a message of its own, where 30 sent one for the job; no compared text carries
    /// it), then <paramref name="worstCase"/> when given (<see cref="WorstCaseNote"/>, D14).
    /// </summary>
    public static MigrationReport Of(
        string oldVersion, Migration31Result migrated, JsonObject options, IReadOnlyList<ComparedText> texts, string? worstCase = null)
    {
        ArgumentNullException.ThrowIfNull(oldVersion);
        ArgumentNullException.ThrowIfNull(migrated);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(texts);

        var handEdits = migrated.HandEdits.ToHashSet(StringComparer.Ordinal);
        var notices = migrated.Notices.Where(n => !handEdits.Contains(n)).ToList();
        if (Deliverables(migrated) > 1)
            notices.Add(OneMessageEach);
        if (worstCase != null)
            notices.Add(worstCase);

        var newVersion = StrictJson.Parse(migrated.ManifestJson) is { Fault: null, Value: JsonObject manifest }
            ? SourceLayout.Text(manifest["version"]) ?? ""
            : "";
        return new MigrationReport(oldVersion, newVersion, [.. migrated.HandEdits], notices, (JsonObject)options.DeepClone(), [.. texts]);
    }

    /// <summary>
    /// A10-063 (R8, ruled by the owner on #1507, 2026-11-22; part 10 § 3.2): a migrated package delivers each deliverable by
    /// <c>pdf</c> in a message of its own, where 30's reply was one message for the job: an expected difference no compared
    /// text carries, so the report says it as a notice.
    /// </summary>
    public const string OneMessageEach = "Each deliverable is now delivered by pdf in a message of its own, where 30 sent one message for the job.";

    /// <summary>
    /// A10-014 (part 10 § 3.2, D14): <c>&lt;package&gt; could run for about &lt;n&gt; minutes in the worst case; this engine
    /// stops a job after &lt;limit&gt; minutes.</c>, said only when the estimate passes <paramref name="limits"/>'
    /// <see cref="RunLimits.JobMinutes"/>. The estimate: part 09 § 3.4 and <see cref="PublishNotes"/> give the worst case's
    /// agent calls (S04-080's count: each agent once for every run of the bodies around it, a loop's items its known length,
    /// the items limit or its <c>max</c>, whichever is least), and no duration, so one minute per round of
    /// <see cref="RunLimits.MaxAgentCalls"/> calls: the calls divided by the ceiling, rounded up. Null when the package does
    /// not check (its hand edits remain) or the estimate is within the limit.
    /// </summary>
    public static string? WorstCaseNote(Migration31Result migrated, TypeRegistry registry, RunLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(migrated);
        ArgumentNullException.ThrowIfNull(registry);
        var run = limits ?? new RunLimits();

        var upload = migrated.Files.ToDictionary(f => f.Key, f => Encoding.UTF8.GetBytes(f.Value), StringComparer.Ordinal);
        upload["manifest.json"] = Encoding.UTF8.GetBytes(migrated.ManifestJson);
        var check = SourceCheck.Run(upload, registry);
        if (check.Refusals.Count > 0 || check.Parse.Package is not { } package || check.Resolution is not { } resolution)
            return null;

        // #1508 (area 11; D16): the estimate is publish's own (S09-016, the same words), so the two never disagree.
        var publish = new PublishLimits(run.MaxAgentCalls, run.LoopItems, run.JobAgentCalls, JobMinutes: run.JobMinutes);
        return PublishNotes.MinutesNote(package, resolution, publish)?.Sentence;
    }

    /// <summary>A10-014's words: S09-016's (#1508, D16).</summary>
    public static string WorstCase(string package, long minutes, int limit) =>
        ResolveSentences.WorstCaseMinutes(package, minutes, limit).Sentence;

    /// <summary>The deliverables the migrated package declares (its <c>deliverables/</c> files, any folder).</summary>
    private static int Deliverables(Migration31Result migrated) =>
        migrated.Files.Keys.Count(path => path.StartsWith("deliverables/", StringComparison.Ordinal) && path.EndsWith(".md", StringComparison.Ordinal));
}
