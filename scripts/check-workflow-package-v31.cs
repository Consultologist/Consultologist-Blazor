#!/usr/bin/env dotnet
#:project ../src/Consultologist.Api/Consultologist.Api.csproj
// The Functions host publishes trimmed, which turns reflection-based System.Text.Json off; this script
// is neither trimmed nor AOT'd.
#:property JsonSerializerIsReflectionEnabledByDefault=true

// Checks, or publishes from CI, a specVersion 31 workflow package folder, offline (#1508, part 07 §§ 5, 9).
//
// Usage:
//   dotnet run -v q --file scripts/check-workflow-package-v31.cs -- <folder>
//       # steps 0 to 3 (PackagePublish.CheckAsync): the report as it would be; nothing is written
//   dotnet run -v q --file scripts/check-workflow-package-v31.cs -- <folder> --write <out folder> [--version <tag>] [--published-by "ci <tag>"]
//       # steps 0 to 4 (PackagePublish.RunAsync): the version's files in write order, then publish.json last,
//       # into <out folder> (new or empty), for CI to upload and attach as Release assets
//
// The folder is read as an upload: every file under it by its path with "/" (a ".git" folder is skipped).
// A symbolic link (or any reparse point), file or folder, is skipped and never followed, so --write never copies a link's
// target into the version's files (and the Release assets made from them). Without --version
// the manifest's own name and version are used; with it, manifest.json's version is replaced by <tag> (the name is kept) in
// place before step 0, as the engine does for the version it assigns (D2), so sourceHash covers the manifest as written.
// --published-by is the report's publishedBy (D4); it defaults to "ci <tag>" with --version and "ci" without.
//
// stdout: the report's canonical JSON (RFC 8785), alone. stderr: S07-015 for each compiled copy or stamp found in the
// folder (dropped unread), then each refusal's sentence, then each warning's, then one summary line.
//
// The type registry, the toolchain and the limits are the engine's, built as scripts/migrate-workflow-package.cs --to 31 builds them:
//   registry   TypeRegistry.Read over external/consultologist-package-format/v31 (formats, renderers/index.json) and
//              external/consultologist-agents/agents/v31 (formats, bindings.json, agents.json)
//   toolchain  Toolchain.Current(package format release from external/consultologist-package-format/spec-versions.json,
//              the formats catalogue's ResolvedRef, "agents@<agents catalogue version>"), no output contracts catalogue
//              (the same as EngineToolchain.Current())
//   limits     EngineLimitSettings over an empty configuration: the spec's starting values (the engine's unset settings)
//   clock      the system's; no diagram (D1)
//
// Exit codes: 0 published (or, without --write, would publish); 1 refused; 2 usage or environment (an unreadable folder,
// the registry, or an out folder that is not empty or holds a stamp).

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Agents;
using Consultologist.Api.V31;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Publish;
using Microsoft.Extensions.Configuration;

const string Usage = "usage: dotnet run -v q --file scripts/check-workflow-package-v31.cs -- <folder> [--write <out folder> [--version <tag>] [--published-by \"ci <tag>\"]]";

string? folder = null, outFolder = null, tag = null, publishedBy = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--write" when i + 1 < args.Length:
            outFolder = args[++i];
            break;
        case "--version" when i + 1 < args.Length:
            tag = args[++i];
            break;
        case "--published-by" when i + 1 < args.Length:
            publishedBy = args[++i];
            break;
        default:
            if (args[i].StartsWith("--", StringComparison.Ordinal) || folder != null)
            {
                Console.Error.WriteLine($"error: unexpected argument '{args[i]}'");
                Console.Error.WriteLine(Usage);
                return 2;
            }

            folder = args[i];
            break;
    }
}

if (folder == null || ((tag != null || publishedBy != null) && outFolder == null))
{
    Console.Error.WriteLine(Usage);
    return 2;
}

var root = Path.GetFullPath(folder);
if (!Directory.Exists(root))
{
    Console.Error.WriteLine($"error: no folder {root}");
    return 2;
}

// ---- the engine's registry, toolchain and limits ------------------------------------------------------------------------

var repo = FindRepo(AppContext.BaseDirectory) ?? FindRepo(Directory.GetCurrentDirectory());
if (repo == null)
{
    Console.Error.WriteLine("error: could not find Consultologist.sln above the build output or the working directory.");
    return 2;
}

PublishOptions options;
try
{
    var agentsDir = Path.Combine(repo, "external", "consultologist-agents", "agents");
    var contract = Path.Combine(repo, "external", "consultologist-package-format", "v31");
    var agents = Path.Combine(agentsDir, V31AgentsCatalogue.Folder);
    var registry = Consultologist.PackageFormat.V31.PublishedTypeRegistry.Read(contract, agents);
    var specVersions = Path.Combine(repo, "external", "consultologist-package-format", "spec-versions.json");
    var release = (string?)JsonNode.Parse(File.ReadAllText(specVersions))?["version"]
        ?? throw new InvalidOperationException($"{specVersions} names no version.");
    var toolchain = Toolchain.Current(
        Consultologist.Api.Workflow.EngineAttestation.RefOf(Consultologist.Api.Workflow.EngineAttestation.PackageFormatRegistry, release)!,
        FormatCatalogLoader.Load(agentsDir).ResolvedRef,
        $"{EngineToolchain.AgentsRegistry}@{V31AgentsCatalogue.Load(agentsDir).Version}");
    var limits = new EngineLimitSettings(new ConfigurationBuilder().Build());
    options = new PublishOptions(
        registry, toolchain, limits.PublishLimits,
        publishedBy ?? (tag != null ? $"ci {tag}" : "ci"), TimeProvider.System, MatchTimeLimit: limits.RegexLimit);
}
catch (Exception ex) when (ex is FormatException or InvalidOperationException or IOException or JsonException)
{
    Console.Error.WriteLine($"error: the 31 type registry or toolchain could not be read under {repo}/external: {ex.Message}");
    return 2;
}

// ---- the folder as an upload --------------------------------------------------------------------------------------------

var upload = new Dictionary<string, byte[]>(StringComparer.Ordinal);
foreach (var file in FilesUnder(new DirectoryInfo(root)))
{
    var path = Path.GetRelativePath(root, file.FullName).Replace(Path.DirectorySeparatorChar, '/');
    if (path == ".git" || path.StartsWith(".git/", StringComparison.Ordinal))
        continue;
    upload[path] = File.ReadAllBytes(file.FullName);
}

if (tag != null && upload.TryGetValue("manifest.json", out var manifestBytes) && Versioned(manifestBytes, tag) is { } versioned)
    upload["manifest.json"] = versioned;

if (outFolder != null)
{
    var outRoot = OutRoot(outFolder);
    if (File.Exists(Path.Combine(outRoot, PublishedStamp.FileName)))
    {
        Console.Error.WriteLine($"error: {outRoot} holds a stamp ({PublishedStamp.FileName}); a stamped version is never written into.");
        return 2;
    }

    if (Directory.Exists(outRoot) && Directory.EnumerateFileSystemEntries(outRoot).Any())
    {
        Console.Error.WriteLine($"error: {outRoot} is not empty; write into a new or empty folder.");
        return 2;
    }
}

// ---- run ----------------------------------------------------------------------------------------------------------------

var outcome = outFolder != null
    ? await PackagePublish.RunAsync(upload, options)
    : await PackagePublish.CheckAsync(upload, options);

Console.Out.WriteLine(outcome.Report.ToCanonical());
foreach (var path in outcome.Dropped)
    Console.Error.WriteLine(PackagePublishSentences.EvidenceCommitted(path).Sentence);
foreach (var refusal in outcome.Report.Refusals)
    Console.Error.WriteLine($"refused: {refusal.Sentence}" + (refusal.Path.Length > 0 ? $" ({refusal.Path})" : ""));
foreach (var warning in outcome.Report.Warnings)
    Console.Error.WriteLine($"warning: {warning.Sentence}" + (warning.Path.Length > 0 ? $" ({warning.Path})" : ""));

if (!outcome.Report.Published)
{
    Console.Error.WriteLine($"refused: {outcome.Report.Refusals.Count} refusal(s); nothing written.");
    return 1;
}

if (outFolder == null)
{
    Console.Error.WriteLine($"ok: {outcome.Report.Name}@{outcome.Report.Version} would publish ({outcome.Report.Tests.Count} test(s) passed); nothing written.");
    return 0;
}

try
{
    var outRoot = OutRoot(outFolder);
    foreach (var file in outcome.Files)
        WriteInto(outRoot, file.Path, file.Bytes);

    // The stamp last: a folder without it is not a version.
    WriteInto(outRoot, PublishedStamp.FileName, new UTF8Encoding(false).GetBytes(outcome.Stamp!.ToJson()));
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
{
    Console.Error.WriteLine($"error: the version could not be written: {ex.Message}");
    return 2;
}

Console.Error.WriteLine($"published: {outcome.Report.Name}@{outcome.Report.Version} written to {OutRoot(outFolder)} ({outcome.Files.Count + 1} files, {PublishedStamp.FileName} last).");
return 0;

// ---- helpers ------------------------------------------------------------------------------------------------------------

// Every file under the folder, to any depth. A symbolic link (or any reparse point), file or folder, is skipped (never
// followed).
static IEnumerable<FileInfo> FilesUnder(DirectoryInfo folder)
{
    foreach (var entry in folder.EnumerateFileSystemInfos())
    {
        if (IsLink(entry))
            continue;
        if (entry is DirectoryInfo child)
        {
            foreach (var file in FilesUnder(child))
                yield return file;
        }
        else if (entry is FileInfo file)
        {
            yield return file;
        }
    }
}

static bool IsLink(FileSystemInfo entry) =>
    (entry.Attributes & FileAttributes.ReparsePoint) != 0 || entry.LinkTarget != null;

static string? FindRepo(string start)
{
    var dir = new DirectoryInfo(start);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
        dir = dir.Parent;
    return dir?.FullName;
}

/// <summary>The out folder's full path without a trailing separator, so "out/" and "out" are one folder and the containment check below holds.</summary>
static string OutRoot(string outFolder) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(outFolder));

static void WriteInto(string outRoot, string relative, byte[] bytes)
{
    var target = Path.GetFullPath(Path.Combine(outRoot, relative));
    if (!target.StartsWith(outRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        throw new InvalidOperationException($"'{relative}' would leave the out folder.");
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    File.WriteAllBytes(target, bytes);
}

/// <summary>The manifest with its <c>version</c> replaced in place (every other member and its order kept); null when it is not an object with a text name and version (the pipeline refuses it as uploaded).</summary>
static byte[]? Versioned(byte[] manifestBytes, string tag)
{
    try
    {
        var text = new UTF8Encoding(false, true).GetString(manifestBytes);
        if (JsonNode.Parse(text) is not JsonObject manifest
            || manifest["name"] is not JsonValue name || name.GetValueKind() != JsonValueKind.String
            || manifest["version"] is not JsonValue version || version.GetValueKind() != JsonValueKind.String)
        {
            return null;
        }

        manifest["version"] = tag;
        var written = manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        return new UTF8Encoding(false).GetBytes(text.EndsWith('\n') ? written + "\n" : written);
    }
    catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException or FormatException)
    {
        return null;
    }
}
