#!/usr/bin/env dotnet
#:project ../src/Consultologist.Api/Consultologist.Api.csproj
// Same reason as validate-workflow-package.cs: this script is neither trimmed
// nor AOT'd, and the manifest model has no source-generated context.
#:property JsonSerializerIsReflectionEnabledByDefault=true

// Writes a package's publication stamp — publish.json — into its directory,
// the way the registry's publisher writes one for an account fork (#433).
//
// Usage:
//   dotnet run -v q --file scripts/stamp-workflow-package.cs -- <package-dir>
//
// WHY THIS EXISTS. Public packages are published by the consultologist-workflows
// repo's CI, which uploads a version folder and never runs the app's
// publisher. The stamp records what each declared schema resolved to, under
// which catalog, at the moment it was validated — and the engine's load path
// takes that record's word instead of re-matching the embedded schema against
// whatever catalog is running (the stranding #374 guards against). Without
// this script a public version is unstamped and loads exactly as it always
// did; with it, a catalog edit can no longer strand the version.
//
// It validates with the bundled catalog first — the same validator the
// registry runs, with no stamp, which is the publish-time proof — and only
// then resolves. The stamp's bytes are the Api's (WorkflowPackageStamp.ToJson),
// so a stamp written here is byte-identical to one the publisher writes.
//
// This is the one script in this directory that writes, and it writes one
// file into the directory it was given. An existing publish.json that already
// says the same thing is left alone; one that differs is refused — delete it
// and re-run, so "check this" and "change this" stay separate acts.
// The directory plumbing below is validate-workflow-package.cs's, duplicated:
// a file-based app cannot include a sibling source, and the logic whose bytes
// matter lives in the Api.

using System.Text.Json;
using Consultologist.Api.Agents;
using Consultologist.Api.Workflow;

var packageDir = args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal));

if (packageDir is null)
{
    Console.Error.WriteLine("usage: dotnet run --file scripts/stamp-workflow-package.cs -- <package-dir>");
    return 2;
}

var root = Path.GetFullPath(packageDir);
var manifestPath = Path.Combine(root, "manifest.json");

if (!File.Exists(manifestPath))
{
    Console.Error.WriteLine($"error: no manifest.json in {root}");
    return 2;
}

WorkflowPackageManifest? manifest;
try
{
    manifest = JsonSerializer.Deserialize<WorkflowPackageManifest>(
        File.ReadAllText(manifestPath),
        WorkflowPackageManifestJson.ReadOptions);
}
catch (JsonException ex)
{
    Console.Error.WriteLine($"error: manifest.json does not parse: {ex.Message}");
    return 1;
}

if (manifest is null)
{
    Console.Error.WriteLine("error: manifest.json deserialized to null.");
    return 1;
}

// Package-relative, forward-slash keys. manifest.json is not a package file,
// dag.mmd is a derived artifact, and the stamp is what this writes.
var files = new Dictionary<string, string>(StringComparer.Ordinal);

foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
{
    var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    if (relative is "manifest.json" or "dag.mmd" || relative == WorkflowPackageStamp.FileName)
    {
        continue;
    }

    files[relative] = File.ReadAllText(path);
}

var repo = new DirectoryInfo(AppContext.BaseDirectory);
while (repo != null && !File.Exists(Path.Combine(repo.FullName, "Consultologist.sln")))
{
    repo = repo.Parent;
}

if (repo is null)
{
    Console.Error.WriteLine("error: could not find Consultologist.sln above the build output.");
    return 2;
}

var catalog = OutputContractCatalog.Load(Path.Combine(repo.FullName, "external", "consultologist-agents", "agents"));
var catalogSchemas = catalog.Entries.Values
    .Where(entry => entry.SchemaJson != null)
    .ToDictionary(entry => entry.ContractId, entry => entry.SchemaJson!, StringComparer.Ordinal);

// No stamp passed: this is where the match is proved, not where it is trusted.
var result = WorkflowPackageValidator.Validate(manifest, files, catalogSchemas);

foreach (var error in result.Errors)
{
    Console.Error.WriteLine($"error: {error}");
}

if (!result.IsValid)
{
    Console.Error.WriteLine($"error: {manifest.Name}@{manifest.Version} does not validate; nothing stamped.");
    return 1;
}

var stampErrors = new List<string>();
var stamp = WorkflowPackageStamp.Compute(manifest, files, catalog, stampErrors);

foreach (var error in stampErrors)
{
    Console.Error.WriteLine($"error: {error}");
}

if (stampErrors.Count > 0)
{
    return 1;
}

var stampPath = Path.Combine(root, WorkflowPackageStamp.FileName);
var stampJson = stamp.ToJson();

if (File.Exists(stampPath))
{
    if (string.Equals(File.ReadAllText(stampPath), stampJson, StringComparison.Ordinal))
    {
        Console.Out.WriteLine($"{manifest.Name}@{manifest.Version}: already stamped under {stamp.CatalogRef}.");
        return 0;
    }

    Console.Error.WriteLine(
        $"error: {stampPath} already exists and differs from what {catalog.ResolvedRef} resolves; delete it and re-run.");
    return 1;
}

File.WriteAllText(stampPath, stampJson);
Console.Out.Write(stampJson);
Console.Out.WriteLine($"{manifest.Name}@{manifest.Version}: stamped under {stamp.CatalogRef} ({stamp.Contracts.Count} contract(s)).");
return 0;
