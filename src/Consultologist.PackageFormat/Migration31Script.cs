using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Migration;

namespace Consultologist.PackageFormat;

/// <summary>
/// #1507 (part 10 § 1.1, D6, D7): what <c>--to 31</c> of <c>scripts/migrate-workflow-package.cs</c> was given:
/// <paramref name="Write"/> (<c>--write</c>: apply the plan; without it a dry run), <paramref name="Snapshot"/>
/// (<c>--snapshot</c>: write the snapshot test of a 31 folder whose hand edits are made), <paramref name="Renames"/>
/// (<c>--rename &lt;old&gt;=&lt;new&gt;</c>, repeatable: a reserved 30 id to its hand-chosen 31 id) and
/// <paramref name="WhenEmpty"/> (<c>--when-empty &lt;deliverable&gt;=&lt;value&gt;</c>, repeatable) and
/// <paramref name="FolderFormats"/> (<c>--folder-format &lt;folder&gt;=&lt;format&gt;</c>, repeatable: the package format a
/// snippet folder's items' headers are values of; part 01 § 3); or, in place of the three, <paramref name="Options"/>
/// (<c>--options &lt;file&gt;</c>, #1508 R18: the path of a package's <c>options.json</c>, read with
/// <see cref="Migration31Options.FromJson"/> by <see cref="Migration31Script.Run"/>).
/// </summary>
public sealed record Migration31Arguments(
    bool Write,
    bool Snapshot,
    IReadOnlyDictionary<string, string> Renames,
    IReadOnlyDictionary<string, string> WhenEmpty,
    IReadOnlyDictionary<string, string> FolderFormats,
    string? Options = null)
{
    /// <summary><paramref name="options"/> with <see cref="Renames"/>, <see cref="WhenEmpty"/> and <see cref="FolderFormats"/> in place of its own.</summary>
    public Migration31Options Fill(Migration31Options options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options with { Renames = Renames, WhenEmpty = WhenEmpty, FolderFormats = FolderFormats };
    }
}

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 §§ 1.1, 1.4; D2): the <c>--to 31</c> run of
/// <c>scripts/migrate-workflow-package.cs</c> on a package folder, here so it is tested; the script builds the
/// <see cref="Migration31Options"/> (the registry and the toolchain) and calls <see cref="Run"/>. A 30 folder, or an older
/// one raised through the chain in the same run, is migrated (<see cref="WorkflowPackageMigration30To31.Migrate"/>); with
/// <c>--snapshot</c> a 31 folder gets its snapshot test (<see cref="WorkflowPackageMigration30To31.Snapshot"/>). It prints
/// every notice and the file plan, and with <c>--write</c> leaves the folder holding exactly the result's files: what the
/// result no longer names is deleted (30's <c>prompts/</c>, its flat macros, its <c>dag.mmd</c>, which 31 does not write
/// here). Exits <see cref="Clean"/>, <see cref="HandEditsLeft"/> (with S10-001) or <see cref="Refused"/> (nothing written).
/// </summary>
public static class Migration31Script
{
    /// <summary>Exit 0: migrated with no hand edit (or the snapshot test written).</summary>
    public const int Clean = 0;

    /// <summary>Exit 1: migrated, hand edits remain (part 10 § 1.4).</summary>
    public const int HandEditsLeft = 1;

    /// <summary>Exit 2: refused, no package written (an unreadable input, a version below 5, any refusal), or a usage error.</summary>
    public const int Refused = 2;

    /// <summary>S10-001: the summary's word when the package was written with hand edits left.</summary>
    public const string HandEditsRemain = MigrationSentences.WrittenHandEditsRemain;

    /// <summary>The one line that says 31's <c>dag.mmd</c> is not this script's (the 31 diagram is drawn by area 13's tools).</summary>
    public const string NoDiagram =
        "notice: dag.mmd is not written at specVersion 31 (area 13's tools draw the 31 diagram); a 30 dag.mmd is deleted.";

    /// <summary>The usage line of <c>--to 31</c>.</summary>
    public const string Usage =
        "usage: dotnet run -v q --file scripts/migrate-workflow-package.cs -- <package-dir> --to 31 [--write] "
        + "[--rename <old>=<new>]... [--when-empty <deliverable>=release|hold|fail]... [--folder-format <folder>=<format>]... "
        + "| <package-dir> --to 31 [--write] --options <file> "
        + "| <package-dir> --to 31 --snapshot [--write]";

    /// <summary>The <c>whenEmpty</c> values a deliverable takes (part 06).</summary>
    private static readonly string[] WhenEmptyValues = Migration31Options.WhenEmptyValues;

    /// <summary>The options that take a value.</summary>
    private static readonly string[] Valued = ["--to", "--rename", "--when-empty", "--folder-format", "--options"];

    private const string Manifest = "manifest.json";

    private const string Diagram = "dag.mmd";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The package directory of <paramref name="args"/>: the first argument that is not an option or an option's value; <c>null</c> when none.</summary>
    public static string? PackageDirectory(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        for (var i = 0; i < args.Count; i++)
        {
            if (Valued.Contains(args[i], StringComparer.Ordinal))
                i++;
            else if (!args[i].StartsWith("--", StringComparison.Ordinal))
                return args[i];
        }

        return null;
    }

    /// <summary>
    /// The <c>--to 31</c> arguments of <paramref name="args"/> (the package directory and <c>--to</c>'s value are the
    /// script's), or <c>null</c> and why they do not read: an unknown option, a second package directory, a
    /// <c>--rename</c>, <c>--when-empty</c> or <c>--folder-format</c> without <c>&lt;key&gt;=&lt;value&gt;</c> or naming
    /// its key twice, a <c>whenEmpty</c> that is not <c>release</c>, <c>hold</c> or <c>fail</c>, <c>--snapshot</c> with
    /// any of the three (a snapshot reads a 31 package, which they do not shape), or <c>--options</c> without a file, given
    /// twice, with any of the three (the file holds all three choices, R18) or with <c>--snapshot</c>.
    /// </summary>
    public static (Migration31Arguments? Arguments, string? Usage) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var write = false;
        var snapshot = false;
        var renames = new Dictionary<string, string>(StringComparer.Ordinal);
        var whenEmpty = new Dictionary<string, string>(StringComparer.Ordinal);
        var folderFormats = new Dictionary<string, string>(StringComparer.Ordinal);
        string? optionsFile = null;
        var directories = 0;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--write":
                    write = true;
                    break;
                case "--snapshot":
                    snapshot = true;
                    break;
                case "--to":
                    i++;
                    break;
                case "--options":
                    if (i + 1 >= args.Count || args[i + 1].Length == 0)
                        return (null, "--options takes the path of an options.json.");
                    if (optionsFile != null)
                        return (null, "--options is given twice.");
                    optionsFile = args[++i];
                    break;
                case "--rename" or "--when-empty" or "--folder-format":
                    var into = arg switch { "--rename" => renames, "--when-empty" => whenEmpty, _ => folderFormats };
                    if (i + 1 >= args.Count || !TryPair(args[++i], out var key, out var value))
                        return (null, $"{arg} takes <key>=<value>.");
                    if (!into.TryAdd(key, value))
                        return (null, $"{arg} names '{key}' twice.");
                    if (into == whenEmpty && !WhenEmptyValues.Contains(value, StringComparer.Ordinal))
                        return (null, $"--when-empty takes release, hold or fail, not '{value}'.");
                    break;
                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal))
                        return (null, $"{arg} is not an option of --to 31.");
                    if (++directories > 1)
                        return (null, $"'{arg}' is a second package directory.");
                    break;
            }
        }

        if (snapshot && (renames.Count > 0 || whenEmpty.Count > 0 || folderFormats.Count > 0))
            return (null, "--snapshot reads a 31 package: --rename, --when-empty and --folder-format choose how a 30 package migrates.");
        if (optionsFile != null && snapshot)
            return (null, "--snapshot reads a 31 package: --options chooses how a 30 package migrates.");
        if (optionsFile != null && (renames.Count > 0 || whenEmpty.Count > 0 || folderFormats.Count > 0))
            return (null, "--options gives renames, whenEmpty and folderFormats: --rename, --when-empty and --folder-format are not given with it.");

        return (new Migration31Arguments(write, snapshot, renames, whenEmpty, folderFormats, optionsFile), null);
    }

    private static bool TryPair(string text, out string key, out string value)
    {
        var at = text.IndexOf('=', StringComparison.Ordinal);
        key = at > 0 ? text[..at] : "";
        value = at > 0 ? text[(at + 1)..] : "";
        return key.Length > 0 && value.Length > 0;
    }

    /// <summary>
    /// Runs <paramref name="arguments"/> on the package folder <paramref name="folder"/> with <paramref name="options"/>
    /// (its <c>Renames</c>, <c>WhenEmpty</c> and <c>FolderFormats</c> replaced by the arguments', or by those of the
    /// <c>--options</c> file, R18), writing notices, the plan and the summary to
    /// <paramref name="output"/> and refusals to <paramref name="error"/>; returns the exit code. An options file that
    /// cannot be read, is not a JSON object or that <see cref="Migration31Options.FromJson"/> refuses (A10-070 to A10-072)
    /// is <see cref="Refused"/>, nothing read or written.
    /// </summary>
    public static int Run(string folder, Migration31Arguments arguments, Migration31Options options, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var chosen = arguments.Fill(options);
        if (arguments.Options is { } optionsFile)
        {
            if (ReadOptions(optionsFile, options, error) is not { } read)
                return Refused;
            chosen = read;
        }

        var root = Path.GetFullPath(folder);
        Dictionary<string, string> before;
        try
        {
            // The 30 stamp is not a package file: it is neither read nor touched.
            before = Read(root);
            before.Remove(WorkflowPackageStamp.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            error.WriteLine($"error: {root} could not be read: {ex.Message}");
            return Refused;
        }

        if (!before.TryGetValue(Manifest, out var manifestJson))
        {
            error.WriteLine($"error: no manifest.json in {root}");
            return Refused;
        }

        // The diagram is a view, not read. A migration does not write one at 31, so a 30 one is deleted; a snapshot leaves
        // a 31 folder's own diagram alone.
        var files = before
            .Where(f => f.Key is not (Manifest or Diagram))
            .ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal);
        if (arguments.Snapshot)
            before.Remove(Diagram);
        var label = Label(manifestJson, root);
        var what = arguments.Snapshot ? "the snapshot test" : "the migration to specVersion 31";
        var result = arguments.Snapshot
            ? WorkflowPackageMigration30To31.Snapshot(manifestJson, files, options)
            : WorkflowPackageMigration30To31.Migrate(manifestJson, files, chosen);

        foreach (var notice in result.Notices)
            output.WriteLine($"notice: {notice}");

        if (!result.Succeeded)
        {
            foreach (var refusal in result.Refusals)
                error.WriteLine($"error: {refusal}");
            error.WriteLine($"{label}: {what} refused; nothing written.");
            return Refused;
        }

        var after = new Dictionary<string, string>(result.Files, StringComparer.Ordinal) { [Manifest] = result.ManifestJson };
        if (OutsideOf(root, after.Keys) is { } outside)
        {
            error.WriteLine($"error: {outside} would be written outside {root}.");
            error.WriteLine($"{label}: {what} refused; nothing written.");
            return Refused;
        }

        var deleted = before.Keys.Where(p => !after.ContainsKey(p)).Order(StringComparer.Ordinal).ToList();
        var added = after.Keys.Where(p => !before.ContainsKey(p)).Order(StringComparer.Ordinal).ToList();
        var rewritten = after.Where(f => before.TryGetValue(f.Key, out var was) && !string.Equals(was, f.Value, StringComparison.Ordinal))
            .Select(f => f.Key).Order(StringComparer.Ordinal).ToList();
        foreach (var path in deleted) output.WriteLine($"delete:  {path}");
        foreach (var path in added) output.WriteLine($"add:     {path}");
        foreach (var path in rewritten) output.WriteLine($"rewrite: {path}");
        if (!arguments.Snapshot)
            output.WriteLine(NoDiagram);

        if (arguments.Write)
        {
            try
            {
                Apply(root, before, after, deleted);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The new and changed files are written first and the deletes come last, so a failure loses no 30 file the
                // plan did not rewrite; the folder may still hold part of the plan.
                error.WriteLine($"error: {root} could not be written: {ex.Message}");
                error.WriteLine($"{label}: {what} stopped part-way; restore the folder, then run again.");
                return Refused;
            }
        }

        var plan = string.Create(CultureInfo.InvariantCulture, $"{added.Count} added, {rewritten.Count} rewritten, {deleted.Count} deleted");
        var outcome = !arguments.Write ? " (dry run; pass --write to apply)"
            : result.HandEdits.Count > 0 ? $"; {HandEditsRemain}"
            : "; written";
        if (arguments.Snapshot)
        {
            output.WriteLine($"{label}: {what}, {plan}{outcome}");
            return Clean;
        }

        WorkflowPackageMigrationChain.TryReadVersion(Migrated.Normalise(manifestJson), out _, out var from);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{label}: specVersion {from} → 31, {plan}, {result.HandEdits.Count} hand edits{outcome}"));
        return result.HandEdits.Count > 0 ? HandEditsLeft : Clean;
    }

    /// <summary>
    /// <paramref name="options"/> with the choices of the <c>options.json</c> at <paramref name="path"/> in place of its
    /// own, or <c>null</c> once <paramref name="error"/> says why not: the file does not read as strict UTF-8 JSON, is not an
    /// object, or <see cref="Migration31Options.FromJson"/> refuses it (its sentence).
    /// </summary>
    private static Migration31Options? ReadOptions(string path, Migration31Options options, TextWriter error)
    {
        var full = Path.GetFullPath(path);
        string text;
        try
        {
            text = Utf8.GetString(File.ReadAllBytes(full));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            error.WriteLine($"error: {full} could not be read: {ex.Message}");
            return null;
        }

        if (V31.StrictJson.Parse(text) is not { Fault: null, Value: JsonObject json })
        {
            error.WriteLine($"error: {full} is not a JSON object.");
            return null;
        }

        var (read, refusal) = Migration31Options.FromJson(json, options.Registry, options.Toolchain, options.Formats30);
        if (read is null)
        {
            error.WriteLine($"error: {refusal}");
            return null;
        }

        return options with { Renames = read.Renames, WhenEmpty = read.WhenEmpty, FolderFormats = read.FolderFormats };
    }

    /// <summary>Every file under <paramref name="root"/> by its forward-slash path, read as strict UTF-8 (a byte-order mark kept, for the migration to normalise).</summary>
    private static Dictionary<string, string> Read(string root)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            files[Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/')] = Utf8.GetString(File.ReadAllBytes(path));
        return files;
    }

    /// <summary>
    /// The first of <paramref name="paths"/> (package-relative, forward-slash) that does not name a file inside
    /// <paramref name="root"/>: rooted, or leaving it through <c>..</c>; <c>null</c> when every one stays inside.
    /// </summary>
    internal static string? OutsideOf(string root, IEnumerable<string> paths)
    {
        var inside = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        return paths.Order(StringComparer.Ordinal).FirstOrDefault(path =>
            path.Length == 0
            || Path.IsPathRooted(path)
            || !Path.GetFullPath(Path.Combine([inside, .. path.Split('/')])).StartsWith(inside, StringComparison.Ordinal));
    }

    /// <summary>
    /// The plan applied: new files written, then changed ones (<c>manifest.json</c> last), then <paramref name="deleted"/>
    /// removed and folders left empty removed, so a write that fails part-way has deleted nothing.
    /// </summary>
    private static void Apply(string root, Dictionary<string, string> before, Dictionary<string, string> after, IReadOnlyList<string> deleted)
    {
        var writes = after
            .Where(f => !before.TryGetValue(f.Key, out var was) || !string.Equals(was, f.Value, StringComparison.Ordinal))
            .OrderBy(f => before.ContainsKey(f.Key))
            .ThenBy(f => f.Key == Manifest)
            .ThenBy(f => f.Key, StringComparer.Ordinal);
        foreach (var (path, text) in writes)
        {
            var full = Path.Combine([root, .. path.Split('/')]);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, Utf8.GetBytes(text));
        }

        foreach (var path in deleted)
            File.Delete(Path.Combine([root, .. path.Split('/')]));

        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir);
        }
    }

    /// <summary><c>name@version</c> of the manifest, or the folder's name when the manifest does not read.</summary>
    private static string Label(string manifestJson, string root)
    {
        try
        {
            if (JsonNode.Parse(manifestJson) is JsonObject manifest
                && manifest["name"] is JsonValue name && name.TryGetValue<string>(out var n)
                && manifest["version"] is JsonValue version && version.TryGetValue<string>(out var v))
                return $"{n}@{v}";
        }
        catch (JsonException)
        {
        }

        return Path.GetFileName(root);
    }
}
