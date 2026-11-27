using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Publish;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.V31;

/// <summary>
/// #1508 (specVersion 31, area 11; part 09 § 8.2; part 07 §§ 6, 7; D5, D14, D15): the content response of a 31 package.
/// <see cref="ReadAsync"/> serves a stamped version from the registry's folder; <see cref="Import"/> reads an uploaded one,
/// read-only. Either way <c>Files</c> holds every source file by path but <c>manifest.json</c>, which is <c>Manifest</c>,
/// parsed from its bytes with no member added or dropped.
/// </summary>
public static class V31PackageContent
{
    private const string ManifestFile = "manifest.json";

    private const int MaxDepth = 256;

    private static readonly UTF8Encoding Utf8 = new(false);

    /// <summary>
    /// The stored version <paramref name="name"/>@<paramref name="version"/> (D14): its source files and the stored
    /// <c>dag.mmd</c> (never the evidence, <c>migration-report.json</c> or the lock blob beside the folder), <c>MigrationReport</c> (D10: the stored
    /// <c>migration-report.json</c> parsed, null when there is none or it is not JSON, which is logged as a warning naming the
    /// package and version only), <c>Resolve</c>,
    /// <c>Lock</c> and <c>PublishReport</c> parsed from the stored bytes, and <c>MigratedFrom</c> from the stamp (D5). Null
    /// when the folder has no 31 stamp or no <c>manifest.json</c> that is a JSON object: not a version for any reader. A
    /// registry failure is thrown, as the read threw it.
    /// </summary>
    public static async Task<WorkflowPackageContentResponse?> ReadAsync(IRegistryFiles registry, string name, string version, CancellationToken ct, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(version);

        var folder = $"{name}/{version}/";
        var paths = await registry.ListAsync(name, folder, ct);
        var read = await Task.WhenAll(paths.Select(async path => (Path: path[folder.Length..], Bytes: await registry.TryReadAsync(name, path, ct))));
        var stored = read.Where(f => f.Bytes != null).ToDictionary(f => f.Path, f => f.Bytes!, StringComparer.Ordinal);

        var stampText = stored.TryGetValue(PublishedStamp.FileName, out var stampBytes) ? Text(stampBytes) : null;
        if (PublishedStamp.TryRead(stampText) == null || Element(stored, ManifestFile) is not { ValueKind: JsonValueKind.Object } manifest)
            return null;

        var files = stored
            .Where(f => f.Key != ManifestFile && (SourceHash.IsSource(f.Key) || f.Key == PackagePublish.DiagramFile))
            .OrderBy(f => f.Key, StringComparer.Ordinal)
            .ToDictionary(f => f.Key, f => Text(f.Value), StringComparer.Ordinal);

        var migrationReport = Element(stored, PackagePublish.MigrationReportFile);
        if (migrationReport == null && stored.ContainsKey(PackagePublish.MigrationReportFile))
            logger?.LogWarning("The stored migration report is not JSON and is not served. Package={Package}, Version={Version}", name, version);

        return new WorkflowPackageContentResponse(
            name, version, PublishedStamp.SpecVersion, manifest, files,
            Resolve: Element(stored, CompiledPackage.ResolveFile),
            Lock: Element(stored, ResolveCompiler.LockFile),
            PublishReport: Element(stored, PublishReport.FileName),
            MigratedFrom: MigratedFrom(stampText!),
            MigrationReport: migrationReport);
    }

    /// <summary>
    /// An uploaded 31 package read whole (D15; part 07 § 7): <paramref name="check"/> is <see cref="SourceCheck.Run"/> of it,
    /// with no refusal. Its source files as intake normalised them (an uploaded <c>dag.mmd</c> is a view and is not
    /// kept), <c>Resolve</c>, <c>Lock</c> and <c>PublishReport</c> null, and <c>Notices</c> S07-016 once when the upload held
    /// <c>resolve.json</c> or <c>lock.json</c> (<see cref="ResolveSentences.ImportIgnored"/>), else null.
    /// </summary>
    public static WorkflowPackageContentResponse Import(SourceCheckResult check)
    {
        ArgumentNullException.ThrowIfNull(check);
        var parse = check.Parse;
        var manifestText = parse.Files.TryGetValue(ManifestFile, out var text) ? text : "{}";
        JsonElement manifest;
        using (var document = JsonDocument.Parse(manifestText, new JsonDocumentOptions { MaxDepth = MaxDepth }))
            manifest = document.RootElement.Clone();

        var files = parse.Files
            .Where(f => f.Key != ManifestFile && SourceHash.IsSource(f.Key))
            .ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal);

        return new WorkflowPackageContentResponse(
            Member(manifest, "name") ?? "", Member(manifest, "version") ?? "", PublishedStamp.SpecVersion, manifest, files,
            Notices: ResolveSentences.ImportIgnored(parse.Dropped) is { } ignored ? [ignored.Sentence] : null);
    }

    /// <summary>The stamp's <c>migratedFrom</c>, when it is text (D5; <see cref="PublishedStamp.TryRead"/> ignores it).</summary>
    private static string? MigratedFrom(string stampText) =>
        JsonNode.Parse(stampText) is JsonObject stamp ? SourceLayout.Text(stamp["migratedFrom"]) : null;

    private static string? Member(JsonElement manifest, string name) =>
        manifest.ValueKind == JsonValueKind.Object && manifest.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>The file at <paramref name="path"/> parsed as JSON, or null when it is absent or is not JSON.</summary>
    private static JsonElement? Element(IReadOnlyDictionary<string, byte[]> stored, string path)
    {
        if (!stored.TryGetValue(path, out var bytes))
            return null;
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = MaxDepth });
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Text(byte[] bytes) => Utf8.GetString(bytes);
}
