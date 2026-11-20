#!/usr/bin/env dotnet
// The index is written with reflection-based System.Text.Json, which a file-based app turns off.
#:property JsonSerializerIsReflectionEnabledByDefault=true

// Vendors the thirteen public workflow packages, each at the tag that pins it, from a checkout of
// the content repo into the byte-equivalence fixtures (#1498).
//
// Usage:
//   dotnet run -v q --file scripts/vendor-public-packages.cs -- <path to consultologist-workflows> [<fixture root>]
//
// The fixture root defaults to tests/Fixtures/ByteEquivalence under the repo this script sits in.
// It writes vendored/<name>/... (the files exactly as git holds them) and vendored/index.json
// (name, tag, commit, and the sha256 of every file). vendored/ is deleted first so a file removed
// upstream cannot linger. A tag that does not resolve stops the script before anything is written.
//
// WHY THE TAGS ARE HERE. These are each package's latest tag at specVersion 30, the engine this
// fixture set freezes. Moving a pin is a deliberate edit to this list, then a re-run.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("usage: dotnet run -v q --file scripts/vendor-public-packages.cs -- <path to consultologist-workflows> [<fixture root>]");
    return 2;
}

string[] tags =
[
    "example-classifier-scope-v2026.11.3",
    "example-conditional-documents-v2026.11.3",
    "example-conditional-sections-v2026.11.3",
    "example-macros-v2026.11.3",
    "example-macros-warnings-v2026.11.3",
    "example-nested-note-v2026.11.2",
    "example-prior-letters-v2026.11.1",
    "example-problem-list-v2026.11.2",
    "example-structured-intake-v2026.11.3",
    "example-two-documents-v2026.11.3",
    "example-v12-constructs-v2026.11.3",
    "general-v2026.11.3",
    "test/general-v2026.11.3",
];

var repo = Path.GetFullPath(args[0]);
var fixtureRoot = args.Length == 2
    ? Path.GetFullPath(args[1])
    : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ScriptFile())!, "..", "tests", "Fixtures", "ByteEquivalence"));

var packages = new List<(string Name, string Tag, string Commit, SortedDictionary<string, (byte[] Bytes, string Hash)> Files)>();

// Everything is read before anything is written, so a missing tag leaves the folder as it was.
foreach (var tag in tags)
{
    var commit = Git(repo, $"rev-parse --verify --quiet {tag}^{{commit}}");

    if (commit.ExitCode != 0)
    {
        Console.Error.WriteLine($"Tag '{tag}' is not in {repo}: fetch the tags there and run again.");
        return 1;
    }

    // The package name is the tag up to the last "-v"; it may hold a slash (test/general).
    var name = tag[..tag.LastIndexOf("-v", StringComparison.Ordinal)];
    var prefix = $"packages/{name}/";
    var listing = Git(repo, $"ls-tree -r --name-only {tag} {prefix}");
    var files = new SortedDictionary<string, (byte[], string)>(StringComparer.Ordinal);

    foreach (var path in Encoding.UTF8.GetString(listing.Output).Split('\n', StringSplitOptions.RemoveEmptyEntries))
    {
        var bytes = Git(repo, $"show {tag}:{path}").Output;
        files[path[prefix.Length..]] = (bytes, "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    if (files.Count == 0)
    {
        Console.Error.WriteLine($"Tag '{tag}' holds no files under {prefix}.");
        return 1;
    }

    packages.Add((name, tag, Encoding.UTF8.GetString(commit.Output).Trim(), files));
}

var vendoredDir = Path.Combine(fixtureRoot, "vendored");

if (Directory.Exists(vendoredDir))
{
    Directory.Delete(vendoredDir, recursive: true);
}

foreach (var package in packages)
{
    foreach (var (relative, file) in package.Files)
    {
        var target = Path.Combine(vendoredDir, package.Name, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllBytes(target, file.Bytes);
    }
}

var index = new
{
    packages = packages.Select(package => new
    {
        name = package.Name,
        tag = package.Tag,
        commit = package.Commit,
        files = package.Files.ToDictionary(file => file.Key, file => file.Value.Hash),
    }),
};

var json = JsonSerializer.Serialize(index, new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    NewLine = "\n",
});

File.WriteAllBytes(Path.Combine(vendoredDir, "index.json"), new UTF8Encoding(false).GetBytes(json + "\n"));
Console.WriteLine($"Vendored {packages.Count} packages into {vendoredDir}.");
return 0;

// Raw stdout bytes: text decoding would change a CRLF or a BOM, and the bytes are the point.
static (int ExitCode, byte[] Output) Git(string repo, string arguments)
{
    var start = new ProcessStartInfo("git", $"-C \"{repo}\" {arguments}")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };

    using var process = Process.Start(start)!;
    using var buffer = new MemoryStream();
    var error = process.StandardError.ReadToEndAsync();
    process.StandardOutput.BaseStream.CopyTo(buffer);
    process.WaitForExit();
    error.Wait();
    return (process.ExitCode, buffer.ToArray());
}

static string ScriptFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
