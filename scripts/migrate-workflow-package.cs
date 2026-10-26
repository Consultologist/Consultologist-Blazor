#!/usr/bin/env dotnet
#:project ../src/Consultologist.Api/Consultologist.Api.csproj
// The Functions host publishes trimmed, which turns reflection-based
// System.Text.Json off; this script is neither trimmed nor AOT'd, and the
// manifest model has no source-generated context.
#:property JsonSerializerIsReflectionEnabledByDefault=true

// Migrates a workflow package directory on disk to specVersion 25 (the default) or 26, offline.
//
// Usage:
//   dotnet run -v q --file scripts/migrate-workflow-package.cs -- <package-dir>                   # dry run: notices, refusals, the file plan, the 25 validation
//   dotnet run -v q --file scripts/migrate-workflow-package.cs -- <package-dir> --write           # apply: files, manifest.json, dag.mmd
//   dotnet run -v q --file scripts/migrate-workflow-package.cs -- <package-dir> --to 26 [--write] # #1152: the 25 → 26 stage (WorkflowPackageMigration25To26)
//
// WHY THIS EXISTS. The editor upgrades a package in the browser: below 23 it
// turns preludes into node macros and bumps the number (formats up to 24 are
// additive), then hands the 24 package to the library's 24 → 25 rewriter
// (WorkflowPackageMigration24To25). The public packages repository has no
// browser in its loop, so this script does the same two steps on a folder:
// StageTo24 is a port of the editor's MigratePreludes + ComposeManifest bump
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

using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Agents;
using Consultologist.PackageFormat;

var toIndex = System.Array.IndexOf(args, "--to");
var to = toIndex >= 0 && toIndex + 1 < args.Length && int.TryParse(args[toIndex + 1], out var parsedTo) ? parsedTo : 25;
var packageDir = args.Where((argument, i) => !argument.StartsWith("--", StringComparison.Ordinal) && i != toIndex + 1).FirstOrDefault();
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

// #1152: the 26 stage — a 25 package through the library's 25 → 26 rewriter.
// Validation at 26 waits for the bump (#1149): the public Validate refuses 26
// until AcceptedSpecVersions carries it; the test suite validates through the seam.
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
    Console.Out.WriteLine("note: validation at specVersion 26 waits for the bump (#1149); the test suite validates the rewriter through the seam.");

    if (write)
    {
        var manifest26 = JsonSerializer.Deserialize<WorkflowPackageManifest>(result26.ManifestJson, WorkflowPackageManifestJson.ReadOptions)
            ?? throw new InvalidOperationException("The migrated manifest deserialized to null.");
        MigrationRunner.Write(root, result26.ManifestJson, before, result26.Files, WorkflowDagDiagram.Generate(manifest26));
    }

    Console.Out.WriteLine(
        $"{name}@{version}: specVersion 25 → 26, {added26.Count} added, {rewritten26.Count} rewritten, {removed26.Count} deleted"
            + (write ? ", written" : " (dry run; pass --write to apply)"));
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
var (stageNotices, stageRefusals) = MigrationRunner.StageTo24(staged, stagedFiles, from);

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
var dag = WorkflowDagDiagram.Generate(manifest25);

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

    /// <summary>
    /// The editor's stage to 24 (Templates.razor MigratePreludes + ComposeManifest):
    /// every prelude becomes a library macro composed before the prompts that
    /// named it (a bare node macro at index 0), and the number moves to 24 with
    /// tags present. Two deliberate differences from the editor: a hyphenated
    /// prelude id is snake_cased here (the 24 → 25 rewriter renames node, prompt,
    /// data and schema ids but not macro ids, and a macro id must be snake_case
    /// at 24), and a dropped prelude's readers lose their `prelude` too, so no
    /// prompt references a prelude that no longer exists.
    /// </summary>
    public static (List<string> Notices, List<string> Refusals) StageTo24(JsonObject root, Dictionary<string, string> files, int from)
    {
        var notices = new List<string>();
        var refusals = new List<string>();

        if (Get(root, "preludes") is JsonObject preludes && preludes.Count > 0)
        {
            var prompts = Array(root, "prompts").OfType<JsonObject>().ToList();
            var nodes = Array(root, "nodes").OfType<JsonObject>().ToList();
            var macroIds = Array(root, "macros").OfType<JsonObject>().Select(m => Str(m, "id")).Where(id => id != null).ToHashSet(StringComparer.Ordinal)!;
            var renames = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var (id, _) in preludes.ToList())
            {
                var macroId = WorkflowDeclaredIds.IsValid(id) ? id : id.Replace('-', '_');
                if (!WorkflowDeclaredIds.IsValid(macroId))
                {
                    refusals.Add($"Prelude '{id}' needs a snake_case id to become a macro; rename it, then upgrade.");
                }
                else if (macroIds.Contains(macroId) || renames.ContainsValue(macroId))
                {
                    refusals.Add($"Prelude '{id}' has the id of a declared macro; rename one, then upgrade.");
                }
                else if (files.ContainsKey($"macros/{macroId}.md"))
                {
                    refusals.Add($"The package already has a file at 'macros/{macroId}.md'; move it, then upgrade.");
                }

                renames[id] = macroId;
            }

            if (refusals.Count > 0)
            {
                return (notices, refusals);
            }

            foreach (var (id, fileNode) in preludes.ToList())
            {
                var macroId = renames[id];
                var file = fileNode is JsonValue fv && fv.TryGetValue<string>(out var f) ? f : string.Empty;
                var readers = prompts.Where(p => string.Equals(Str(p, "prelude"), id, StringComparison.Ordinal)).ToList();
                var text = file.Length > 0 ? files.GetValueOrDefault(file) : null;

                if (readers.Count == 0)
                {
                    notices.Add($"Prelude '{id}' is used by no prompt and was dropped.");
                }
                else if (string.IsNullOrWhiteSpace(text))
                {
                    notices.Add($"Prelude '{id}' is empty and was dropped.");
                }
                else
                {
                    if (Get(root, "macros") is not JsonArray macros)
                    {
                        macros = new JsonArray();
                        Set(root, "macros", macros);
                    }

                    macros.Add(new JsonObject { ["id"] = macroId, ["label"] = macroId, ["file"] = $"macros/{macroId}.md" });
                    files[$"macros/{macroId}.md"] = text;

                    var homes = new List<string>();
                    foreach (var prompt in readers)
                    {
                        var promptId = Str(prompt, "id");
                        foreach (var node in nodes.Where(n => string.Equals(Str(n, "prompt"), promptId, StringComparison.Ordinal)))
                        {
                            if (Get(node, "macros") is not JsonArray list)
                            {
                                list = new JsonArray();
                                Set(node, "macros", list);
                            }

                            var present = list.Any(entry => entry switch
                            {
                                JsonValue value when value.TryGetValue<string>(out var s) => s == macroId,
                                JsonObject o => Str(o, "id") == macroId,
                                _ => false,
                            });
                            if (!present)
                            {
                                list.Insert(0, JsonValue.Create(macroId));
                            }

                            homes.Add(Str(node, "id") ?? "?");
                        }
                    }

                    notices.Add($"'{id}' → macro '{macroId}' before {(homes.Count == 0 ? "no node (its prompt is unused)" : string.Join(", ", homes.Select(h => $"'{h}'")))}.");
                    if (macroId != id)
                    {
                        notices.Add($"Prelude '{id}' became macro '{macroId}': a macro id is snake_case.");
                    }
                }

                foreach (var prompt in readers)
                {
                    Remove(prompt, "prelude");
                }

                if (file.Length > 0)
                {
                    files.Remove(file);
                }

                preludes.Remove(id);
            }

            if (preludes.Count == 0)
            {
                Remove(root, "preludes");
            }
        }

        if (from < 24)
        {
            Set(root, "specVersion", 24);
            if (Get(root, "tags") is null)
            {
                Set(root, "tags", new JsonArray());
            }

            notices.Add($"Staged specVersion {from} → 24 (the additive bumps: the number, and tags when absent).");
        }

        return (notices, refusals);
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

    public static IEnumerable<JsonNode?> Array(JsonObject o, string key) => Get(o, key) is JsonArray a ? a : Enumerable.Empty<JsonNode?>();

    public static void Set(JsonObject o, string key, JsonNode? value)
    {
        var target = o.ContainsKey(key) ? key : o.ContainsKey(Pascal(key)) ? Pascal(key) : key;
        o[target] = value;
    }

    public static void Remove(JsonObject o, string key)
    {
        o.Remove(key);
        o.Remove(Pascal(key));
    }
}
