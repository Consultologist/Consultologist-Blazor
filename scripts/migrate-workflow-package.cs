#!/usr/bin/env dotnet
#:project ../src/Consultologist.Api/Consultologist.Api.csproj
// The Functions host publishes trimmed, which turns reflection-based
// System.Text.Json off; this script is neither trimmed nor AOT'd, and the
// manifest model has no source-generated context.
#:property JsonSerializerIsReflectionEnabledByDefault=true

// Migrates a workflow package directory on disk to specVersion 25 (the default), 26, 27, 28, 29, 30 or 31, offline.
//
// Usage:
//   dotnet run -v q --file scripts/migrate-workflow-package.cs -- <package-dir>                   # dry run: notices, refusals, the file plan, the 25 validation
//   dotnet run -v q --file scripts/migrate-workflow-package.cs -- <package-dir> --write           # apply: files, manifest.json, dag.mmd (drawn as --dag draws it: with the reads, #1244)
//   dotnet run -v q --file scripts/migrate-workflow-package.cs -- <package-dir> --to 26 [--write] # #1152: the 25 → 26 stage (WorkflowPackageMigration25To26)
//   dotnet run -v q --file scripts/migrate-workflow-package.cs -- <package-dir> --to 27 [--write] # #1237: the 26 → 27 raise (WorkflowPackageVersionRaise; manifest.json and dag.mmd alone)
//   dotnet run -v q --file scripts/migrate-workflow-package.cs -- <package-dir> --to 28 [--write] # #1286: the 27 → 28 rewrite (WorkflowPackageMigration27To28; every file renamed to its id)
//   dotnet run -v q --file scripts/migrate-workflow-package.cs -- <package-dir> --to 29 [--write] # #1307: the 28 → 29 raise (WorkflowPackageVersionRaise; manifest.json and dag.mmd alone)
//   dotnet run -v q --file scripts/migrate-workflow-package.cs -- <package-dir> --to 30 [--write] # #1347: the 29 → 30 raise (WorkflowPackageVersionRaise; manifest.json and dag.mmd alone)
//   dotnet run -v q --file scripts/migrate-workflow-package.cs -- <package-dir> --to 31 [--write] [--rename <old>=<new>]... [--when-empty <deliverable>=release|hold|fail]... [--folder-format <folder>=<format>]...
//                                                                                                # #1507: the 30 → 31 migration (WorkflowPackageMigration30To31; a package from 5 to 29 raised to 30 in the same run)
//   dotnet run -v q --file scripts/migrate-workflow-package.cs -- <package-dir> --to 31 [--write] --options <file>
//                                                                                                # #1508: the same, its choices read from the package's options.json (R18), in place of the three flags
//   dotnet run -v q --file scripts/migrate-workflow-package.cs -- <package-dir> --to 31 --snapshot [--write]
//                                                                                                # #1507: a 31 package's snapshot test, once its hand edits are made (tests/migration_snapshot.json)
//
// WHY THIS EXISTS. The editor upgrades a package in the browser: below 23 it
// turns preludes into node macros and bumps the number (formats up to 24 are
// additive), then hands the 24 package to the library's 24 → 25 rewriter
// (WorkflowPackageMigration24To25). The public packages repository has no
// browser in its loop, so this script does the same two steps on a folder:
// the stage to 24 (WorkflowPackageMigrationTo24.StageTo24, in the library since
// #1507) is a port of the editor's MigratePreludes + ComposeManifest bump
// (src/Consultologist.Web/Pages/Templates.razor), and the rewriter is the
// library's own call with the editor's contract resolver. Nothing here decides
// a format question; the library does.
//
// What it will not do: write the hand edits 25 asks for. A macro that is still
// placed on an output needs an `instruction`, a custom format needs a
// description — the rewriter names each as a notice, the validator reports
// each as an error, and --write still applies so the edits can be made on the
// written files. Exit 1 says "written; hand edits remain".
//
// Exit codes: 0 migrated and valid; 1 refused, or migrated with validation
// errors remaining; 2 usage or environment.
//
// --to 31 (#1507, part 10 §§ 1.1, 1.4) is the library's Migration31Script: the
// dry run prints the notices and the file plan; --write leaves the folder holding
// exactly the 31 files (manifest.json and the nine folders; 30's prompts/ and
// flat macros deleted; dag.mmd the 31 drawing when the migrated package compiles,
// else deleted until its hand edits are done, #1510 D13; --snapshot leaves a
// 31 folder's own dag.mmd alone). --rename, --when-empty and
// --folder-format choose a reserved id's 31 id, a deliverable's whenEmpty (the
// default is release) and a snippet folder's item format (the default is
// <folder>_item). --options <file> (#1508, R18) reads all three from a package's
// options.json, { "renames": {…}, "whenEmpty": {…}, "folderFormats": {…} } ({} for
// none), the file kept beside its hand edits; it is not given with the three flags
// or --snapshot, and a file the migration cannot read is exit 2 (A10-070 to A10-072).
// The write puts new and changed files first and deletes last.
// Its exits are part 10's: 0 migrated cleanly (or the
// snapshot test written); 1 "written; hand edits remain"; 2 refused, writing
// nothing (an unreadable manifest, a version below 5, any refusal, a path that
// would leave the folder), or usage or environment (a write that fails part-way).

using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Agents;
using Consultologist.PackageFormat;

var toIndex = System.Array.IndexOf(args, "--to");
var to = toIndex >= 0 && toIndex + 1 < args.Length && int.TryParse(args[toIndex + 1], out var parsedTo) ? parsedTo : 25;
var packageDir = Migration31Script.PackageDirectory(args);
var write = args.Contains("--write", StringComparer.Ordinal);

if (packageDir is null)
{
    Console.Error.WriteLine("usage: dotnet run -v q --file scripts/migrate-workflow-package.cs -- <package-dir> [--write]");
    return 2;
}

var root = Path.GetFullPath(packageDir);
var manifestPath = Path.Combine(root, "manifest.json");

if (!File.Exists(manifestPath))
{
    Console.Error.WriteLine($"error: no manifest.json in {root}");
    return 2;
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

var agentsDir = Path.Combine(repo.FullName, "external", "consultologist-agents", "agents");
OutputContractCatalog catalog;
WorkflowFormatCatalog formats;
try
{
    catalog = OutputContractCatalog.Load(agentsDir);
    formats = FormatCatalogLoader.Load(agentsDir);
}
catch (Exception ex) when (ex is InvalidOperationException or IOException)
{
    Console.Error.WriteLine($"error: the agents catalogue could not be loaded from {agentsDir}: {ex.Message}");
    return 2;
}

var catalogSchemas = catalog.Entries.Values
    .Where(entry => entry.SchemaJson != null)
    .ToDictionary(entry => entry.ContractId, entry => entry.SchemaJson!, StringComparer.Ordinal);

// #1507: the 31 stage — a 30 package (an older one raised through the chain in
// the same run, D2) through the library's 30 → 31 migration, or with --snapshot a
// 31 package's snapshot test. The registry is the published one under external/
// (as the tests' V31Contract.PublishedTypes reads it); the toolchain is this
// process's, with the pinned package format release and catalogues.
if (to == 31)
{
    var (arguments31, usage31) = Migration31Script.Parse(args);
    if (arguments31 is null)
    {
        Console.Error.WriteLine($"error: {usage31}");
        Console.Error.WriteLine(Migration31Script.Usage);
        return 2;
    }

    var contract31 = Path.Combine(repo.FullName, "external", "consultologist-package-format", "v31");
    var agents31 = Path.Combine(agentsDir, Consultologist.Api.V31.V31AgentsCatalogue.Folder);
    Consultologist.PackageFormat.V31.TypeRegistry registry31;
    Consultologist.PackageFormat.V31.Toolchain toolchain31;
    try
    {
        registry31 = Consultologist.PackageFormat.V31.PublishedTypeRegistry.Read(contract31, agents31);
        var specVersions = Path.Combine(repo.FullName, "external", "consultologist-package-format", "spec-versions.json");
        var release = (string?)JsonNode.Parse(File.ReadAllText(specVersions))?["version"]
            ?? throw new InvalidOperationException($"{specVersions} names no version.");
        toolchain31 = Consultologist.PackageFormat.V31.Toolchain.Current(
            Consultologist.Api.Workflow.EngineAttestation.RefOf(Consultologist.Api.Workflow.EngineAttestation.PackageFormatRegistry, release)!,
            formats.ResolvedRef,
            $"{Consultologist.Api.V31.EngineToolchain.AgentsRegistry}@{Consultologist.Api.V31.V31AgentsCatalogue.Load(agentsDir).Version}");
    }
    catch (Exception ex) when (ex is FormatException or InvalidOperationException or IOException or JsonException)
    {
        Console.Error.WriteLine($"error: the 31 type registry or toolchain could not be read under {repo.FullName}/external: {ex.Message}");
        return 2;
    }

    return Migration31Script.Run(
        root, arguments31, new Migration31Options(registry31, toolchain31, formats, ResolveContract), Console.Out, Console.Error);
}

// #1244: the diagram --dag draws — the declarations and, from 25 (#1227), the reads the
// package's templates make, walked over its files and the formats catalogue. A walk
// that cannot be made leaves the declarations alone and says so (the GET endpoint's idiom).
string DiagramOf(WorkflowPackageManifest m, IReadOnlyDictionary<string, string> f)
{
    WorkflowDerivedEdges.Walk? walk = null;
    if (m.SpecVersion >= 25)
    {
        try
        {
            walk = WorkflowDerivedEdges.WalkOf(m, f, formats);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException or JsonException)
        {
            Console.Out.WriteLine($"notice: the diagram's walk failed ({ex.Message}); dag.mmd is drawn from the declarations alone.");
        }
    }

    return WorkflowDagDiagram.Generate(m, walk);
}

var (manifestJson, before) = MigrationRunner.ReadPackage(root);

JsonObject staged;
try
{
    staged = JsonNode.Parse(manifestJson)!.AsObject();
}
catch (JsonException ex)
{
    Console.Error.WriteLine($"error: manifest.json does not parse: {ex.Message}");
    return 1;
}

var from = MigrationRunner.Get(staged, "specVersion") is JsonValue v && v.TryGetValue<int>(out var n) ? n : 0;
var name = MigrationRunner.Str(staged, "name") ?? "?";
var version = MigrationRunner.Str(staged, "version") ?? "?";

// #1152: the 26 stage — a 25 package through the library's 25 → 26 rewriter,
// validated at 26 in memory as the registry would (#1149: the public Validate
// accepts 26), before any write.
if (to == 26)
{
    if (from != 25)
    {
        Console.Error.WriteLine($"error: {name}@{version} is at specVersion {from}; the 26 stage takes a 25 package (migrate to 25 first).");
        return 1;
    }

    var result26 = WorkflowPackageMigration25To26.Migrate(staged.ToJsonString(), before);

    foreach (var notice in result26.Notices)
    {
        Console.Out.WriteLine($"notice: {notice}");
    }

    if (!result26.Succeeded)
    {
        foreach (var refusal in result26.Refusals)
        {
            Console.Error.WriteLine($"error: {refusal}");
        }

        Console.Error.WriteLine($"{name}@{version}: specVersion 25 → 26 refused; nothing written.");
        return 1;
    }

    var removed26 = before.Keys.Except(result26.Files.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
    var added26 = result26.Files.Keys.Except(before.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
    var rewritten26 = result26.Files
        .Where(pair => before.TryGetValue(pair.Key, out var old) && !string.Equals(old, pair.Value, StringComparison.Ordinal))
        .Select(pair => pair.Key).Order(StringComparer.Ordinal).ToList();

    foreach (var path in removed26) Console.Out.WriteLine($"delete:  {path}");
    foreach (var path in added26) Console.Out.WriteLine($"add:     {path}");
    foreach (var path in rewritten26) Console.Out.WriteLine($"rewrite: {path}");

    var manifest26 = JsonSerializer.Deserialize<WorkflowPackageManifest>(result26.ManifestJson, WorkflowPackageManifestJson.ReadOptions)
        ?? throw new InvalidOperationException("The migrated manifest deserialized to null.");
    var validation26 = WorkflowPackageValidator.Validate(manifest26, result26.Files, catalogSchemas, formats: formats);
    foreach (var warning in validation26.Warnings) Console.Out.WriteLine($"warning: {warning}");
    foreach (var error in validation26.Errors) Console.Error.WriteLine($"error: {error}");
    if (validation26.Errors.Count > 0)
    {
        Console.Error.WriteLine($"{name}@{version}: the 26 package does not validate; nothing written.");
        return 1;
    }

    Console.Out.WriteLine($"{name}@{version}: specVersion 25 → 26 validates ({validation26.Errors.Count} errors, {validation26.Warnings.Count} warnings).");

    if (write)
    {
        MigrationRunner.Write(root, result26.ManifestJson, before, result26.Files, DiagramOf(manifest26, result26.Files));
    }

    Console.Out.WriteLine(
        $"{name}@{version}: specVersion 25 → 26, {added26.Count} added, {rewritten26.Count} rewritten, {removed26.Count} deleted"
            + (write ? ", written" : " (dry run; pass --write to apply)"));
    return 0;
}

// #1237: the 27 stage — a version raise (design v27 § 7): 27 permits what 26
// refused, so nothing is rewritten; the manifest's specVersion moves, every
// file stays, and the raised package is validated at 27 in memory as the
// registry would before any write. The write touches manifest.json and dag.mmd.
if (to == 27)
{
    if (from != 26)
    {
        Console.Error.WriteLine($"error: {name}@{version} is at specVersion {from}; the 27 stage takes a 26 package (migrate to 26 first).");
        return 1;
    }

    var result27 = WorkflowPackageVersionRaise.To(staged.ToJsonString(), before, 27);

    foreach (var notice in result27.Notices)
    {
        Console.Out.WriteLine($"notice: {notice}");
    }

    if (!result27.Succeeded)
    {
        foreach (var refusal in result27.Refusals)
        {
            Console.Error.WriteLine($"error: {refusal}");
        }

        Console.Error.WriteLine($"{name}@{version}: specVersion 26 → 27 refused; nothing written.");
        return 1;
    }

    var manifest27 = JsonSerializer.Deserialize<WorkflowPackageManifest>(result27.ManifestJson, WorkflowPackageManifestJson.ReadOptions)
        ?? throw new InvalidOperationException("The raised manifest deserialized to null.");
    var validation27 = WorkflowPackageValidator.Validate(manifest27, result27.Files, catalogSchemas, formats: formats);
    foreach (var warning in validation27.Warnings) Console.Out.WriteLine($"warning: {warning}");
    foreach (var error in validation27.Errors) Console.Error.WriteLine($"error: {error}");
    if (validation27.Errors.Count > 0)
    {
        Console.Error.WriteLine($"{name}@{version}: the 27 package does not validate; nothing written.");
        return 1;
    }

    Console.Out.WriteLine($"{name}@{version}: specVersion 26 → 27 validates ({validation27.Errors.Count} errors, {validation27.Warnings.Count} warnings).");

    if (write)
    {
        MigrationRunner.Write(root, result27.ManifestJson, before, before, DiagramOf(manifest27, before));
    }

    Console.Out.WriteLine(
        $"{name}@{version}: specVersion 26 → 27, nothing added, rewritten or deleted"
            + (write ? "; manifest.json and dag.mmd written" : " (dry run; pass --write to apply)"));
    return 0;
}

// #1286: the 28 stage — a 27 package through the library's 27 → 28 rewrite
// (design § 8): every prompt, macro, snippet and folder item renamed to its id
// (a shared prompt copied per node), the manifest's paths dropped, each folder
// index { items }; validated at 28 in memory before any write. The write
// deletes the renamed files, adds their new names and rewrites manifest.json,
// the indexes and dag.mmd.
if (to == 28)
{
    if (from != 27)
    {
        Console.Error.WriteLine($"error: {name}@{version} is at specVersion {from}; the 28 stage takes a 27 package (migrate to 27 first).");
        return 1;
    }

    var result28 = WorkflowPackageMigration27To28.Migrate(staged.ToJsonString(), before);

    foreach (var notice in result28.Notices)
    {
        Console.Out.WriteLine($"notice: {notice}");
    }

    if (!result28.Succeeded)
    {
        foreach (var refusal in result28.Refusals)
        {
            Console.Error.WriteLine($"error: {refusal}");
        }

        Console.Error.WriteLine($"{name}@{version}: specVersion 27 → 28 refused; nothing written.");
        return 1;
    }

    var removed28 = before.Keys.Where(path => !result28.Files.ContainsKey(path)).Order(StringComparer.Ordinal).ToList();
    var added28 = result28.Files.Keys.Where(path => !before.ContainsKey(path)).Order(StringComparer.Ordinal).ToList();
    var rewritten28 = result28.Files.Where(pair => before.TryGetValue(pair.Key, out var was) && was != pair.Value).Select(pair => pair.Key).Order(StringComparer.Ordinal).ToList();
    foreach (var path in removed28) Console.Out.WriteLine($"delete: {path}");
    foreach (var path in added28) Console.Out.WriteLine($"add: {path}");
    foreach (var path in rewritten28) Console.Out.WriteLine($"rewrite: {path}");

    var manifest28 = WorkflowPackageManifestJson.Read(result28.ManifestJson, $"{name}@{version}", WorkflowPackageValidator.AcceptedSpecVersions);
    var validation28 = WorkflowPackageValidator.Validate(manifest28, result28.Files, catalogSchemas, formats: formats);
    foreach (var warning in validation28.Warnings) Console.Out.WriteLine($"warning: {warning}");
    foreach (var error in validation28.Errors) Console.Error.WriteLine($"error: {error}");
    if (validation28.Errors.Count > 0)
    {
        Console.Error.WriteLine($"{name}@{version}: the 28 package does not validate; nothing written.");
        return 1;
    }

    Console.Out.WriteLine($"{name}@{version}: specVersion 27 → 28 validates ({validation28.Errors.Count} errors, {validation28.Warnings.Count} warnings).");

    if (write)
    {
        // The diagram walks the implied prompts, as the engine reads them.
        MigrationRunner.Write(root, result28.ManifestJson, before, result28.Files,
            DiagramOf(WorkflowImpliedPaths.WithImpliedPrompts(manifest28, result28.Files), result28.Files));
    }

    Console.Out.WriteLine(
        $"{name}@{version}: specVersion 27 → 28, {added28.Count} added, {rewritten28.Count} rewritten, {removed28.Count} deleted"
            + (write ? "; written" : " (dry run; pass --write to apply)"));
    return 0;
}

// #1307: the 29 stage, and #1347 the 30 stage — a version raise (design v29 § 9,
// v30 § 13): each permits what the version below refused, so nothing is
// rewritten; the manifest's specVersion moves, every file stays, and the raised
// package is validated at the new version in memory before any write. The write
// touches manifest.json and dag.mmd.
if (to is 29 or 30)
{
    var below = to - 1;
    if (from != below)
    {
        Console.Error.WriteLine($"error: {name}@{version} is at specVersion {from}; the {to} stage takes a {below} package (migrate to {below} first).");
        return 1;
    }

    var raised = WorkflowPackageVersionRaise.To(staged.ToJsonString(), before, to);

    foreach (var notice in raised.Notices)
    {
        Console.Out.WriteLine($"notice: {notice}");
    }

    if (!raised.Succeeded)
    {
        foreach (var refusal in raised.Refusals)
        {
            Console.Error.WriteLine($"error: {refusal}");
        }

        Console.Error.WriteLine($"{name}@{version}: specVersion {below} → {to} refused; nothing written.");
        return 1;
    }

    var raisedManifest = WorkflowPackageManifestJson.Read(raised.ManifestJson, $"{name}@{version}", WorkflowPackageValidator.AcceptedSpecVersions);
    var raisedValidation = WorkflowPackageValidator.Validate(raisedManifest, raised.Files, catalogSchemas, formats: formats);
    foreach (var warning in raisedValidation.Warnings) Console.Out.WriteLine($"warning: {warning}");
    foreach (var error in raisedValidation.Errors) Console.Error.WriteLine($"error: {error}");
    if (raisedValidation.Errors.Count > 0)
    {
        Console.Error.WriteLine($"{name}@{version}: the {to} package does not validate; nothing written.");
        return 1;
    }

    Console.Out.WriteLine($"{name}@{version}: specVersion {below} → {to} validates ({raisedValidation.Errors.Count} errors, {raisedValidation.Warnings.Count} warnings).");

    if (write)
    {
        MigrationRunner.Write(root, raised.ManifestJson, before, before,
            DiagramOf(WorkflowImpliedPaths.WithImpliedPrompts(raisedManifest, before), before));
    }

    Console.Out.WriteLine(
        $"{name}@{version}: specVersion {below} → {to}, nothing added, rewritten or deleted"
            + (write ? "; manifest.json and dag.mmd written" : " (dry run; pass --write to apply)"));
    return 0;
}

if (from >= 25)
{
    Console.Error.WriteLine($"error: {name}@{version} is already at specVersion {from}.");
    return 1;
}

if (!WorkflowPackageValidator.AcceptedSpecVersions.Contains(from))
{
    Console.Error.WriteLine($"error: specVersion {from} is not one the engine accepts ({string.Join(", ", WorkflowPackageValidator.AcceptedSpecVersions)}).");
    return 1;
}

// Stage to 24: the editor's prelude migration and additive bump, on a copy of the files.
var stagedFiles = new Dictionary<string, string>(before, StringComparer.Ordinal);
var (stageNotices, stageRefusals) = WorkflowPackageMigrationTo24.StageTo24(staged, stagedFiles, from);

foreach (var refusal in stageRefusals)
{
    Console.Error.WriteLine($"error: {refusal}");
}

if (stageRefusals.Count > 0)
{
    return 1;
}

foreach (var notice in stageNotices)
{
    Console.Out.WriteLine($"notice: {notice}");
}

// The editor's contract resolver: a declared schema body that canonically
// matches a catalogue contract becomes that catalogue format (concept_list);
// anything else becomes a custom package format.
string? ResolveContract(JsonNode schema)
{
    var canonical = WorkflowPackageValidator.CanonicalizeSchema(schema);
    return catalogSchemas
        .Where(pair => WorkflowPackageValidator.CanonicalizeSchema(JsonNode.Parse(pair.Value)) == canonical)
        .Select(pair => pair.Key)
        .FirstOrDefault();
}

var result = WorkflowPackageMigration24To25.Migrate(staged.ToJsonString(), stagedFiles, ResolveContract);

foreach (var notice in result.Notices)
{
    Console.Out.WriteLine($"notice: {notice}");
}

if (!result.Succeeded)
{
    foreach (var refusal in result.Refusals)
    {
        Console.Error.WriteLine($"error: {refusal}");
    }

    Console.Error.WriteLine($"{name}@{version}: specVersion {from} → 25 refused; nothing written.");
    return 1;
}

// Validate the 25 package in memory, as the registry would, before any write.
var manifest25 = JsonSerializer.Deserialize<WorkflowPackageManifest>(result.ManifestJson, WorkflowPackageManifestJson.ReadOptions)
    ?? throw new InvalidOperationException("The migrated manifest deserialized to null.");
var validation = WorkflowPackageValidator.Validate(manifest25, result.Files, catalogSchemas, formats: formats);
var dag = DiagramOf(manifest25, result.Files);

foreach (var warning in validation.Warnings)
{
    Console.Out.WriteLine($"warning: {warning}");
}

foreach (var error in validation.Errors)
{
    Console.Error.WriteLine($"error: {error}");
}

var removed = before.Keys.Except(result.Files.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
var added = result.Files.Keys.Except(before.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
var rewritten = result.Files
    .Where(pair => before.TryGetValue(pair.Key, out var old) && !string.Equals(old, pair.Value, StringComparison.Ordinal))
    .Select(pair => pair.Key).Order(StringComparer.Ordinal).ToList();

foreach (var path in removed) Console.Out.WriteLine($"delete:  {path}");
foreach (var path in added) Console.Out.WriteLine($"add:     {path}");
foreach (var path in rewritten) Console.Out.WriteLine($"rewrite: {path}");

if (write)
{
    MigrationRunner.Write(root, result.ManifestJson, before, result.Files, dag);
}

Console.Out.WriteLine(
    $"{name}@{version}: specVersion {from} → 25, {added.Count} added, {rewritten.Count} rewritten, {removed.Count} deleted, "
        + $"{validation.Errors.Count} errors, {validation.Warnings.Count} warnings"
        + (write ? (validation.IsValid ? ", written" : ", written; hand edits required") : " (dry run; pass --write to apply)"));

return validation.IsValid ? 0 : 1;

static class MigrationRunner
{
    /// <summary>Package-relative, forward-slash keys; manifest.json, dag.mmd and the stamp are not package files.</summary>
    public static (string ManifestJson, Dictionary<string, string> Files) ReadPackage(string root)
    {
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

        return (File.ReadAllText(Path.Combine(root, "manifest.json")), files);
    }

    /// <summary>Applies the plan: removed files deleted, new and changed files written, manifest.json and dag.mmd replaced, empty folders pruned.</summary>
    public static void Write(string root, string manifestJson, IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after, string dag)
    {
        foreach (var path in before.Keys.Where(k => !after.ContainsKey(k)))
        {
            File.Delete(Path.Combine(root, path));
        }

        foreach (var (path, content) in after)
        {
            if (before.TryGetValue(path, out var old) && string.Equals(old, content, StringComparison.Ordinal))
            {
                continue;
            }

            var full = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        File.WriteAllText(Path.Combine(root, "manifest.json"), manifestJson + "\n");
        File.WriteAllText(Path.Combine(root, "dag.mmd"), dag);

        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
            }
        }
    }

    // The rewriter's casing-tolerant helpers: a manifest written by the worker may be PascalCase.
    private static string Pascal(string key) => char.ToUpperInvariant(key[0]) + key[1..];

    public static JsonNode? Get(JsonObject o, string key) => o.TryGetPropertyValue(key, out var v) ? v : o.TryGetPropertyValue(Pascal(key), out var p) ? p : null;

    public static string? Str(JsonObject o, string key) => Get(o, key) is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
