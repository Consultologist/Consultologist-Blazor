#!/usr/bin/env dotnet
#:project ../src/Consultologist.Api/Consultologist.Api.csproj
// The Functions host publishes trimmed, which turns reflection-based
// System.Text.Json off; this script is neither trimmed nor AOT'd, and the
// manifest model has no source-generated context.
#:property JsonSerializerIsReflectionEnabledByDefault=true

// Runs the registry's own validator over a package directory on disk, before
// anything is tagged (#345).
//
// Usage:
//   dotnet run --file scripts/validate-workflow-package.cs -- <package-dir>
//   dotnet run --file scripts/validate-workflow-package.cs -- <package-dir> --dag
//
// WHY THIS EXISTS. The content repo's CI globs packages/*/manifest.json and
// checks the CalVer regex and that every referenced file exists. It never reads
// specVersion, and never traverses nodes, results or when — so a condition
// naming an undeclared enum value, a deliverable whose node is not an
// aggregator, or a prompt that fails strict rendering all pass CI and publish
// cleanly. The failure then lands at pin resolve, where the store throws and
// the job start answers "Workflow package registry is unavailable" — a message
// that points at the wrong thing entirely. And versions are immutable, so the
// remedy is a new version number rather than a fix.
//
// This is not a second opinion. WorkflowPackageValidator.Validate is the same
// method the registry runs on every account publish, and WorkflowDagDiagram is
// the same generator whose output the packages check in. Its own correctness is
// pinned by tests/WorkflowV8Tests.cs and tests/WorkflowDagDiagramTests.cs.
//
// Read-only: it reads a directory and prints. --dag writes nothing either;
// redirect it yourself, which keeps "check this" and "change this" separate.

using System.Text.Json;
using Consultologist.Api.Agents;
using Consultologist.Api.Workflow;

var packageDir = args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal));
var wantsDiagram = args.Contains("--dag", StringComparer.Ordinal);

if (packageDir is null)
{
    Console.Error.WriteLine("usage: dotnet run --file scripts/validate-workflow-package.cs -- <package-dir> [--dag]");
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
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
}
catch (JsonException ex)
{
    // The store deserializes with the same options, so a manifest that fails
    // here would fail at pin resolve too.
    Console.Error.WriteLine($"error: manifest.json does not parse: {ex.Message}");
    return 1;
}

if (manifest is null)
{
    Console.Error.WriteLine("error: manifest.json deserialized to null.");
    return 1;
}

if (wantsDiagram)
{
    // Before validation, so a package that is still being worked on can still
    // have its graph drawn — the diagram is a projection of nodes alone.
    Console.Out.Write(WorkflowDagDiagram.Generate(manifest));
    return 0;
}

// Package-relative, forward-slash keys: the shape Validate and
// WorkflowDataResolver expect, and what the store builds from blob names.
// manifest.json is not a package file, and dag.mmd is a derived artifact the
// manifest never references.
var files = new Dictionary<string, string>(StringComparer.Ordinal);

foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
{
    var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    if (relative is "manifest.json" or "dag.mmd")
    {
        continue;
    }

    files[relative] = File.ReadAllText(path);
}

// The bundled catalog, loaded the way tests/TestOutputContracts.cs loads it:
// every declared package schema must canonically match a contract that ships.
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

var result = WorkflowPackageValidator.Validate(manifest, files, catalogSchemas);

foreach (var warning in result.Warnings)
{
    Console.Out.WriteLine($"warning: {warning}");
}

foreach (var error in result.Errors)
{
    Console.Error.WriteLine($"error: {error}");
}

Console.Out.WriteLine(
    $"{manifest.Name}@{manifest.Version} (specVersion {manifest.SpecVersion}): "
        + $"{files.Count} files, {result.Errors.Count} errors, {result.Warnings.Count} warnings");

return result.IsValid ? 0 : 1;
