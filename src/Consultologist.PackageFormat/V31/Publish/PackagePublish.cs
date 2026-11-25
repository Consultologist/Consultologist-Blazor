using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.PackageFormat.V31.Publish;

/// <summary>
/// #1508 (specVersion 31, area 11): what a publish is given beside the upload. <paramref name="Registry"/>,
/// <paramref name="Toolchain"/> and <paramref name="Limits"/> are the engine's; <paramref name="PublishedBy"/> is the report's
/// <c>publishedBy</c> (D4); <paramref name="Clock"/> gives the report's time and bounds the tests (D9);
/// <paramref name="Templates"/> is the seam for what each body reads (the dialect's when null); <paramref name="Diagram"/> turns
/// <c>resolve.json</c>'s canonical text into <c>dag.mmd</c>'s (D1: none is written when null); <paramref name="MigrationReport"/>
/// is <c>migration-report.json</c>'s text and <paramref name="MigratedFrom"/> the stamp's old version (D5);
/// <paramref name="MatchTimeLimit"/> bounds <c>matches</c> and <c>has_word</c> in the tests' plan.
/// </summary>
public sealed record PublishOptions(
    TypeRegistry Registry,
    Toolchain Toolchain,
    PublishLimits Limits,
    string PublishedBy,
    TimeProvider Clock,
    ITemplateReads? Templates = null,
    Func<string, string>? Diagram = null,
    string? MigrationReport = null,
    string? MigratedFrom = null,
    TimeSpan? MatchTimeLimit = null);

/// <summary>#1508 (area 11): one file to write into the version's folder: its path there and its bytes.</summary>
public sealed record PublishFile(string Path, byte[] Bytes);

/// <summary>
/// #1508 (area 11; part 07 §§ 5, 9): what a publish gave. <paramref name="Report"/> always; when published,
/// <paramref name="Files"/> in write order and <paramref name="Stamp"/>, which the writer writes after them; else no files and
/// no stamp. <paramref name="Compiled"/> is the pair when it was compiled. <paramref name="Dropped"/> are the evidence files
/// the upload held, dropped unread (for S07-015 and S07-016).
/// </summary>
public sealed record PublishOutcome(
    PublishReport Report,
    IReadOnlyList<PublishFile> Files,
    PublishedStamp? Stamp,
    CompiledPackage? Compiled,
    IReadOnlyList<string> Dropped)
{
    /// <summary>Whether the version is to be written: it has a stamp.</summary>
    public bool Published => Stamp != null;
}

/// <summary>
/// #1508 (specVersion 31, area 11; part 07 § 5; D1, D2, D10): publish as one call. Step 0 (intake drops the evidence and
/// normalises line endings), steps 1 and 2 (<see cref="CompiledPackage.Publish(IReadOnlyDictionary{string, byte[]}, TypeRegistry, Toolchain, PublishLimits?, ITemplateReads?, out SourceCheckResult)"/>),
/// step 3 (<see cref="PackageTests.RunAsync(RunPlan, IReadOnlyList{SourceTest}, IReadOnlyDictionary{string, string}, TimeProvider, RunLimits?)"/>
/// on the plan of the in-memory <c>resolve.json</c>, never the source), then step 4's report and files. The package's name
/// and version are read from the upload's <c>manifest.json</c> (A11-004 when they are not text). Any refusal gives a refused
/// report and nothing to write; an exception inside is A11-007 with its type in <see cref="SourceRefusal.EngineFault"/>,
/// never thrown. A null argument is the caller's error.
/// </summary>
public static class PackagePublish
{
    /// <summary>The diagram's file name in a version's folder (a view, D1).</summary>
    public const string DiagramFile = "dag.mmd";

    /// <summary>The operator's migration report's file name in a version's folder (D5).</summary>
    public const string MigrationReportFile = "migration-report.json";

    private const string ManifestFile = "manifest.json";

    private static readonly UTF8Encoding Utf8 = new(false);

    /// <summary>Steps 0 to 4.4: the report and, when published, the files to write and the stamp.</summary>
    public static Task<PublishOutcome> RunAsync(IReadOnlyDictionary<string, byte[]> upload, PublishOptions options) =>
        RunAsync(upload, options, write: true);

    /// <summary>Steps 0 to 3: the report with its outcome as it would be, no time, and never files or a stamp.</summary>
    public static Task<PublishOutcome> CheckAsync(IReadOnlyDictionary<string, byte[]> upload, PublishOptions options) =>
        RunAsync(upload, options, write: false);

    private static async Task<PublishOutcome> RunAsync(IReadOnlyDictionary<string, byte[]> upload, PublishOptions options, bool write)
    {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Registry);
        ArgumentNullException.ThrowIfNull(options.Toolchain);
        ArgumentNullException.ThrowIfNull(options.Limits);
        ArgumentNullException.ThrowIfNull(options.PublishedBy);
        ArgumentNullException.ThrowIfNull(options.Clock);

        // What is known so far, for a refused report at any step (D10).
        var (name, version) = ("", "");
        string? sourceHash = null;
        IReadOnlyList<string> normalised = [];
        IReadOnlyList<string> dropped = [];
        IReadOnlyList<SourceRefusal> warnings = [];
        IReadOnlyList<TestResult> tests = [];
        CompiledPackage? compiled = null;

        PublishReport Report(bool published, DateTimeOffset? at, IReadOnlyList<SourceRefusal> refusals) => new(
            name, version, published, at, options.PublishedBy,
            sourceHash ?? SourceHash.Of(new Dictionary<string, string>()),
            published ? compiled!.ResolveHash : null,
            published ? compiled!.LockHash : null,
            normalised, refusals, warnings, tests);

        PublishOutcome Refused(IReadOnlyList<SourceRefusal> refusals) => new(Report(false, null, refusals), [], null, compiled, dropped);

        try
        {
            var (refusals, built) = CompiledPackage.Publish(upload, options.Registry, options.Toolchain, options.Limits, options.Templates, out var check);
            var parse = check.Parse;
            normalised = parse.Normalised;
            dropped = parse.Dropped;
            sourceHash = built?.SourceHash ?? SourceHash.Of(parse.Files);

            var manifest = ReadManifest(parse.Files);
            (name, version) = (manifest.Name ?? "", manifest.Version ?? "");
            if (manifest.Refusal is { } unnamed)
                refusals = [unnamed, .. refusals];
            if (refusals.Count > 0 || built == null)
                return Refused(refusals);

            compiled = built;
            if (!compiled.Complete)
                return Refused([PackagePublishSentences.EngineFailed()]);
            warnings = compiled.Warnings;

            // Step 3 runs on what the engine will load: the plan of the in-memory resolve.json.
            var (plan, planRefusal) = RunPlan.Build(compiled.Resolve, compiled.ResolveHash, compiled.LockHash, options.MatchTimeLimit);
            if (plan == null)
                return Refused([PackagePublishSentences.EngineFailed(planRefusal?.Id)]);

            var run = await PackageTests.RunAsync(plan, parse.Package!.Tests.Items, parse.Files, options.Clock).ConfigureAwait(false);
            tests = run.Results;
            warnings = [.. compiled.Warnings, .. run.Warnings];
            if (run.Refusals.Count > 0)
                return Refused(run.Refusals);

            if (!write)
                return new PublishOutcome(Report(true, null, []), [], null, compiled, dropped);

            var diagram = options.Diagram?.Invoke(compiled.Resolve);
            var report = Report(true, options.Clock.GetUtcNow(), []);

            // Part 07 § 5 step 4.5: the source as normalised, the diagram, lock.json, the report, then resolve.json last.
            var files = parse.Files
                .Where(f => SourceHash.IsSource(f.Key))
                .OrderBy(f => f.Key, StringComparer.Ordinal)
                .Select(f => new PublishFile(f.Key, Utf8.GetBytes(f.Value)))
                .ToList();
            if (diagram != null)
                files.Add(new PublishFile(DiagramFile, Utf8.GetBytes(diagram)));
            files.Add(new PublishFile(ResolveCompiler.LockFile, Utf8.GetBytes(compiled.Lock)));
            files.Add(new PublishFile(PublishReport.FileName, Utf8.GetBytes(report.ToCanonical())));
            if (options.MigrationReport != null)
                files.Add(new PublishFile(MigrationReportFile, Utf8.GetBytes(options.MigrationReport)));
            files.Add(new PublishFile(CompiledPackage.ResolveFile, Utf8.GetBytes(compiled.Resolve)));

            return new PublishOutcome(report, files, new PublishedStamp(compiled.ResolveHash, compiled.LockHash, options.MigratedFrom), compiled, dropped);
        }
        catch (Exception fault)
        {
            return Refused([PackagePublishSentences.EngineFailed(fault.GetType().Name)]);
        }
    }

    /// <summary>
    /// <c>manifest.json</c>'s <c>name</c> and <c>version</c> (D2), each null unless it is text; A11-004 when the manifest is a JSON
    /// object without both. A manifest that is missing or not an object is the source check's to refuse.
    /// </summary>
    private static (string? Name, string? Version, SourceRefusal? Refusal) ReadManifest(IReadOnlyDictionary<string, string> files)
    {
        if (!files.TryGetValue(ManifestFile, out var text))
            return (null, null, null);

        try
        {
            if (JsonNode.Parse(text) is not JsonObject manifest)
                return (null, null, null);

            var name = SourceLayout.Text(manifest["name"]);
            var version = SourceLayout.Text(manifest["version"]);
            return (name, version, name == null || version == null ? PackagePublishSentences.ManifestNameAndVersion() : null);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException)
        {
            return (null, null, null);
        }
    }
}
