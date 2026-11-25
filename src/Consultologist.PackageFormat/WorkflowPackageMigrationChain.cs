using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Migration;

namespace Consultologist.PackageFormat;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 1.1, D2): the existing chain, which raises a package older than 30 to 30
/// in the same run as the 30 → 31 migration (<see cref="WorkflowPackageMigration30To31"/>). It runs the rungs the script
/// (<c>scripts/migrate-workflow-package.cs</c>) and the Editor's upgrade run today, in their order: the stage to 24
/// (<see cref="WorkflowPackageMigrationTo24"/>, the script's), the 24 → 25 rewrite with the caller's contract resolver,
/// the 25 → 26 rewrite, the 27 raise, the 27 → 28 rewrite, the 29 and 30 raises
/// (<see cref="WorkflowPackageVersionRaise"/>). Each rung runs only below its version. The result is the rungs' shape
/// (<see cref="Migration26Result"/>): the 30 manifest, the files, every rung's notices in order. A manifest that does not
/// read, a version below 5 or past 30 is refused; a rung's own refusal, its validation errors (when 30's catalogue is
/// given) and a failure on the package's content refuse the run, naming the rung (A10-017, A10-018), and a
/// refusal writes nothing: the result holds the input's manifest and files.
/// </summary>
public static class WorkflowPackageMigrationChain
{
    /// <summary>The lowest version a rung starts from: pre-v5 packages are archived.</summary>
    public const int Oldest = 5;

    /// <summary>The version the chain raises a package to.</summary>
    public const int Target = 30;

    /// <summary>
    /// <paramref name="manifestJson"/> and <paramref name="files"/> (without <c>manifest.json</c>) raised to 30;
    /// <paramref name="resolveContract"/> is the 24 → 25 rung's (a declared schema to a catalogue contract id, or
    /// <c>null</c> for a package format). With <paramref name="formats"/> (30's catalogue), each rung's output is validated
    /// as the script validates it (<see cref="WorkflowPackageValidator.Validate(WorkflowPackageManifest, IReadOnlyDictionary{string, string}, IReadOnlyDictionary{string, string}, IReadOnlyDictionary{string, string}?, WorkflowFormatCatalog?)"/>;
    /// every rung writes 25 or later, where the output-contract schemas are not read, so none are given), and an error
    /// refuses the run naming the rung (A10-017). A rung that fails on the package's content refuses the run naming the
    /// rung (A10-018). A 30 package passes through unchanged.
    /// </summary>
    public static Migration26Result To30(
        string manifestJson,
        IReadOnlyDictionary<string, string> files,
        Func<JsonNode, string?>? resolveContract,
        WorkflowFormatCatalog? formats = null)
    {
        ArgumentNullException.ThrowIfNull(manifestJson);
        ArgumentNullException.ThrowIfNull(files);

        if (!TryReadVersion(manifestJson, out var root, out var from))
        {
            return Refused(manifestJson, files, MigrationSentences.ManifestDoesNotRead);
        }

        if (from < Oldest)
        {
            return Refused(manifestJson, files, MigrationSentences.BelowFive(from));
        }

        if (from > Target)
        {
            return Refused(manifestJson, files, MigrationSentences.AlreadyAt(from));
        }

        var notices = new List<string>();
        var json = manifestJson;
        var current = new Dictionary<string, string>(files, StringComparer.Ordinal);
        var rung = 24;
        try
        {
            // The stage to 24 runs on everything below 25, as the script runs it: preludes are made macros at any
            // version, and the number moves only below 24.
            if (from < 25)
            {
                var (stageNotices, stageRefusals) = WorkflowPackageMigrationTo24.StageTo24(root, current, from);
                if (stageRefusals.Count > 0)
                {
                    return Refused(manifestJson, files, stageRefusals.Select(r => MigrationSentences.AtRung(24, r)));
                }

                notices.AddRange(stageNotices);
                json = root.ToJsonString();
            }

            var rungs = new (int Version, Func<string, Dictionary<string, string>, (string Json, Dictionary<string, string> Files, IReadOnlyList<string> Notices, IReadOnlyList<string> Refusals)> Run)[]
            {
                (25, (j, f) => Of(WorkflowPackageMigration24To25.Migrate(j, f, resolveContract))),
                (26, (j, f) => Of(WorkflowPackageMigration25To26.Migrate(j, f))),
                (27, (j, f) => Of(WorkflowPackageVersionRaise.To(j, f, 27))),
                (28, (j, f) => Of(WorkflowPackageMigration27To28.Migrate(j, f))),
                (29, (j, f) => Of(WorkflowPackageVersionRaise.To(j, f, 29))),
                (30, (j, f) => Of(WorkflowPackageVersionRaise.To(j, f, 30))),
            };

            foreach (var (version, run) in rungs)
            {
                if (from >= version)
                {
                    continue;
                }

                rung = version;
                var result = run(json, current);
                if (result.Refusals.Count > 0)
                {
                    return Refused(manifestJson, files, result.Refusals.Select(r => MigrationSentences.AtRung(version, r)));
                }

                if (formats is not null)
                {
                    var errors = ValidationErrors(version, result.Json, result.Files, formats);
                    if (errors.Count > 0)
                    {
                        return Refused(manifestJson, files, errors.Select(e => MigrationSentences.AtRung(version, e)));
                    }
                }

                notices.AddRange(result.Notices);
                json = result.Json;
                current = result.Files;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // An earlier rung's code is the Editor's and the script's, called as they call it; on content it was not
            // written for it may throw. The migration never throws on package content: the run is refused.
            return Refused(manifestJson, files, MigrationSentences.RungFailed(rung));
        }

        return new Migration26Result(json, current, notices, []);
    }

    /// <summary>
    /// The script's validation of a rung's output: the manifest read as the script reads it at that version (deserialized
    /// at 25 to 27, <see cref="WorkflowPackageManifestJson.Read"/> from 28), then validated against 30's catalogue.
    /// </summary>
    private static IReadOnlyList<string> ValidationErrors(int version, string json, Dictionary<string, string> files, WorkflowFormatCatalog formats)
    {
        var manifest = version < 28
            ? JsonSerializer.Deserialize<WorkflowPackageManifest>(json, WorkflowPackageManifestJson.ReadOptions)
                ?? throw new JsonException("The migrated manifest is empty.")
            : WorkflowPackageManifestJson.Read(json, "migrated", WorkflowPackageValidator.AcceptedSpecVersions);
        return WorkflowPackageValidator.Validate(manifest, files, NoCatalogSchemas, formats: formats).Errors;
    }

    private static readonly IReadOnlyDictionary<string, string> NoCatalogSchemas = new Dictionary<string, string>(StringComparer.Ordinal);

    private static readonly JsonDocumentOptions StrictDocument = new() { AllowDuplicateProperties = false };

    /// <summary>
    /// Whether <paramref name="manifestJson"/> reads as a package: a JSON object, with no member written twice at any
    /// depth, whose <c>specVersion</c> is a whole number written as one (<c>30</c>, not <c>30.0</c> or <c>3e1</c>):
    /// <paramref name="version"/>. Package content never throws here.
    /// </summary>
    internal static bool TryReadVersion(string manifestJson, out JsonObject root, out int version)
    {
        root = null!;
        version = 0;
        try
        {
            if (JsonNode.Parse(manifestJson, documentOptions: StrictDocument) is not JsonObject parsed
                || !parsed.TryGetPropertyValue("specVersion", out var specVersion)
                || specVersion is not JsonValue value
                || value.GetValueKind() != JsonValueKind.Number
                || !value.TryGetValue(out version))
            {
                version = 0;
                return false;
            }

            root = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static (string Json, Dictionary<string, string> Files, IReadOnlyList<string> Notices, IReadOnlyList<string> Refusals) Of(Migration25Result result) =>
        (result.ManifestJson, result.Files, result.Notices, result.Refusals);

    private static (string Json, Dictionary<string, string> Files, IReadOnlyList<string> Notices, IReadOnlyList<string> Refusals) Of(Migration26Result result) =>
        (result.ManifestJson, result.Files, result.Notices, result.Refusals);

    private static Migration26Result Refused(string manifestJson, IReadOnlyDictionary<string, string> files, params IEnumerable<string> refusals) =>
        new(manifestJson, new Dictionary<string, string>(files, StringComparer.Ordinal), [], refusals.ToList());
}
