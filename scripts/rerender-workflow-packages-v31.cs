#!/usr/bin/env dotnet
#:project ../src/Consultologist.Api/Consultologist.Api.csproj
// The Functions host publishes trimmed, which turns reflection-based System.Text.Json off; this script
// is neither trimmed nor AOT'd.
#:property JsonSerializerIsReflectionEnabledByDefault=true

// Re-renders the published specVersion 31 corpus under this build's library (#1508, part 07 § 4.4; D11), offline.
//
// Usage:
//   dotnet run -v q --file scripts/rerender-workflow-packages-v31.cs -- <folder of name/version folders>
//
// For each <folder>/<name>/<version>/ that holds a publish.json whose specVersion is 31, the stored source is read as an
// upload, steps 0 to 3 are run in memory with this build's toolchain (PackagePublish.CheckAsync; nothing is written), and the
// result is compared with the stored publish-report.json: the resolveHash, and each test's outcomeHash (a test the stored
// report lacks, or the new run lacks, is a difference too). A version folder with no 31 stamp is not a 31 version and is
// skipped. Run under the current and then the candidate library version, this is § 4.4's check. Break points stay engine
// configuration (area 06).
//
// stdout: one line per difference, then a summary. The type registry, toolchain and limits are the engine's, built as
// scripts/check-workflow-package-v31.cs builds them (see its header).
//
// Exit codes: 0 no difference; 1 at least one difference; 2 usage or environment.

using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Agents;
using Consultologist.Api.V31;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Publish;
using Microsoft.Extensions.Configuration;

const string Usage = "usage: dotnet run -v q --file scripts/rerender-workflow-packages-v31.cs -- <folder of name/version folders>";

if (args.Length != 1 || args[0].StartsWith("--", StringComparison.Ordinal))
{
    Console.Error.WriteLine(Usage);
    return 2;
}

var corpus = Path.GetFullPath(args[0]);
if (!Directory.Exists(corpus))
{
    Console.Error.WriteLine($"error: no folder {corpus}");
    return 2;
}

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
    var registry = TypeRegistry.Read(
        Path.Combine(contract, "formats"),
        Path.Combine(agents, "formats"),
        Path.Combine(contract, "renderers", "index.json"),
        Path.Combine(agents, "bindings.json"),
        Path.Combine(agents, "agents.json"));
    var specVersions = Path.Combine(repo, "external", "consultologist-package-format", "spec-versions.json");
    var release = (string?)JsonNode.Parse(File.ReadAllText(specVersions))?["version"]
        ?? throw new InvalidOperationException($"{specVersions} names no version.");
    var toolchain = Toolchain.Current(
        Consultologist.Api.Workflow.EngineAttestation.RefOf(Consultologist.Api.Workflow.EngineAttestation.PackageFormatRegistry, release)!,
        FormatCatalogLoader.Load(agentsDir).ResolvedRef,
        $"{EngineToolchain.AgentsRegistry}@{V31AgentsCatalogue.Load(agentsDir).Version}");
    var limits = new EngineLimitSettings(new ConfigurationBuilder().Build());
    options = new PublishOptions(registry, toolchain, limits.PublishLimits, "ci", TimeProvider.System, MatchTimeLimit: limits.RegexLimit);
}
catch (Exception ex) when (ex is FormatException or InvalidOperationException or IOException or JsonException)
{
    Console.Error.WriteLine($"error: the 31 type registry or toolchain could not be read under {repo}/external: {ex.Message}");
    return 2;
}

// A view and an operator's record, not source: never part of what a publish was given.
string[] notSource = ["dag.mmd", PackagePublish.MigrationReportFile];

int checkedCount = 0, differences = 0;
foreach (var nameFolder in Directory.EnumerateDirectories(corpus).Order(StringComparer.Ordinal))
{
    foreach (var versionFolder in Directory.EnumerateDirectories(nameFolder).Order(StringComparer.Ordinal))
    {
        var label = $"{Path.GetFileName(nameFolder)}/{Path.GetFileName(versionFolder)}";
        var stampPath = Path.Combine(versionFolder, PublishedStamp.FileName);
        if (!File.Exists(stampPath) || !Is31(File.ReadAllText(stampPath)))
            continue;

        checkedCount++;
        var found = Compare(versionFolder).GetAwaiter().GetResult();
        foreach (var line in found)
            Console.WriteLine($"{label}: {line}");
        differences += found.Count;
    }
}

Console.WriteLine(differences == 0
    ? $"no difference: {checkedCount} stamped 31 version(s) re-rendered."
    : $"{differences} difference(s) in {checkedCount} stamped 31 version(s) re-rendered.");
return differences == 0 ? 0 : 1;

// ---- helpers ------------------------------------------------------------------------------------------------------------

async Task<List<string>> Compare(string versionFolder)
{
    var found = new List<string>();

    var storedOrNull = ReadReport(Path.Combine(versionFolder, PublishReport.FileName), out var problem);
    if (storedOrNull is not { } stored)
    {
        found.Add(problem!);
        return found;
    }

    var upload = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    foreach (var file in Directory.EnumerateFiles(versionFolder, "*", SearchOption.AllDirectories))
    {
        var path = Path.GetRelativePath(versionFolder, file).Replace(Path.DirectorySeparatorChar, '/');
        if (!notSource.Contains(path, StringComparer.Ordinal))
            upload[path] = File.ReadAllBytes(file);
    }

    var outcome = await PackagePublish.CheckAsync(upload, options);
    if (!outcome.Report.Published)
    {
        found.Add($"refused now ({outcome.Report.Refusals.Count} refusal(s)): " + string.Join(" | ", outcome.Report.Refusals.Select(r => r.Sentence)));
        return found;
    }

    if (!string.Equals(stored.ResolveHash, outcome.Report.ResolveHash, StringComparison.Ordinal))
        found.Add($"resolveHash was {stored.ResolveHash}, now {outcome.Report.ResolveHash}");

    var now = outcome.Report.Tests.ToDictionary(t => t.Id, t => t.OutcomeHash, StringComparer.Ordinal);
    foreach (var (id, hash) in stored.Tests)
    {
        if (!now.TryGetValue(id, out var current))
            found.Add($"test '{id}' is in the stored report and is not run now");
        else if (!string.Equals(hash, current, StringComparison.Ordinal))
            found.Add($"test '{id}' outcomeHash was {hash}, now {current}");
    }

    foreach (var id in now.Keys.Where(id => !stored.Tests.ContainsKey(id)).Order(StringComparer.Ordinal))
        found.Add($"test '{id}' runs now and is not in the stored report");

    return found;
}

static bool Is31(string stampText)
{
    try
    {
        return JsonNode.Parse(stampText) is JsonObject stamp
            && stamp["specVersion"] is JsonValue value
            && value.GetValueKind() == JsonValueKind.Number
            && value.TryGetValue<int>(out var specVersion)
            && specVersion == PublishedStamp.SpecVersion;
    }
    catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException or FormatException)
    {
        return false;
    }
}

static (string? ResolveHash, Dictionary<string, string> Tests)? ReadReport(string path, out string? problem)
{
    problem = null;
    if (!File.Exists(path))
    {
        problem = $"{PublishReport.FileName} is missing";
        return null;
    }

    try
    {
        if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject report)
        {
            problem = $"{PublishReport.FileName} is not a JSON object";
            return null;
        }

        var tests = new Dictionary<string, string>(StringComparer.Ordinal);
        if (report["tests"] is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is JsonObject test && (string?)test["id"] is { } id && (string?)test["outcomeHash"] is { } hash)
                    tests[id] = hash;
            }
        }

        return ((string?)report["resolveHash"], tests);
    }
    catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException or FormatException)
    {
        problem = $"{PublishReport.FileName} is not readable: {e.GetType().Name}";
        return null;
    }
}

static string? FindRepo(string start)
{
    var dir = new DirectoryInfo(start);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
        dir = dir.Parent;
    return dir?.FullName;
}
