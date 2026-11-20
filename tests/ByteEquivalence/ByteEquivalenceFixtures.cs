using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests.ByteEquivalence;

/// <summary>
/// #1498: one package as the index records it — the tag it was vendored from,
/// the commit the tag resolved to, and the SHA-256 of every file's bytes.
/// </summary>
internal sealed record VendoredPackage(string Name, string Tag, string Commit, IReadOnlyDictionary<string, string> Files);

/// <summary>
/// #1498: the byte-equivalence fixtures — where they live, how their bytes are
/// read and hashed, and how a package in them is loaded by today's engine.
/// Everything is read from the checked-in folder; no test reads a sibling checkout.
/// </summary>
internal static class ByteEquivalenceFixtures
{
    private static readonly UTF8Encoding Strict = new(false, true);

    /// <summary>The fixture root, found from this source file.</summary>
    public static string Root { get; } = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourceFile())!, "..", "Fixtures", "ByteEquivalence"));

    /// <summary>The thirteen public packages, vendored at their pinned tags.</summary>
    public static IReadOnlyList<VendoredPackage> Vendored { get; } = ReadIndex();

    /// <summary>The names of the hand-written packages under <c>fixture-packages/</c>; empty when the folder is absent.</summary>
    public static IReadOnlyList<string> FixturePackages { get; } = ListFixturePackages();

    /// <summary>A file's text, strictly decoded: invalid UTF-8 throws, a BOM is kept as a character, newlines are untouched.</summary>
    public static string ReadText(string path) => Strict.GetString(File.ReadAllBytes(path));

    /// <summary><c>sha256:</c> and the lowercase hex of SHA-256 over the text's UTF-8 bytes.</summary>
    public static string HashOf(string text) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Strict.GetBytes(text)));

    /// <summary>
    /// A package's manifest text and its other files keyed by path relative to the package, looked
    /// for in <c>vendored/</c> and then <c>fixture-packages/</c>. <c>manifest.json</c> and the
    /// derived <c>dag.mmd</c> are not package files, so they are left out of <c>Files</c>.
    /// </summary>
    public static (string ManifestJson, Dictionary<string, string> Files) ReadPackage(string name)
    {
        var directory = new[] { "vendored", "fixture-packages" }
            .Select(folder => Path.Combine(Root, folder, name))
            .FirstOrDefault(Directory.Exists)
            ?? throw new DirectoryNotFoundException($"No package '{name}' under {Root}.");

        var files = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, '/');

            if (relative is "manifest.json" or "dag.mmd")
            {
                continue;
            }

            files[relative] = ReadText(path);
        }

        return (ReadText(Path.Combine(directory, "manifest.json")), files);
    }

    /// <summary>Today's engine's loading of a package: the manifest read, then the files materialised against the bundled catalogues.</summary>
    public static WorkflowPackage Load(string name)
    {
        var (manifestJson, files) = ReadPackage(name);
        return Load(name, manifestJson, files);
    }

    /// <summary>The same loading, of a package held in memory — a test's own manifest and files.</summary>
    public static WorkflowPackage Load(string name, string manifestJson, Dictionary<string, string> files)
    {
        using var document = JsonDocument.Parse(manifestJson);
        var version = document.RootElement.GetProperty("version").GetString()!;
        var packageRef = $"{name}@{version}";

        var manifest = WorkflowPackageManifestJson.Read(manifestJson, packageRef, WorkflowPackageValidator.AcceptedSpecVersions);

        return WorkflowPackageStore.PackageOf(WorkflowPackageStore.Materialise(
            packageRef, manifest, files, null, V29Catalog.Catalog, TestFormats.Catalog, WorkflowPackageValidator.AcceptedSpecVersions));
    }

    private static IReadOnlyList<VendoredPackage> ReadIndex()
    {
        var path = Path.Combine(Root, "vendored", "index.json");

        if (!File.Exists(path))
        {
            return Array.Empty<VendoredPackage>();
        }

        using var document = JsonDocument.Parse(ReadText(path));

        return document.RootElement.GetProperty("packages").EnumerateArray()
            .Select(package => new VendoredPackage(
                package.GetProperty("name").GetString()!,
                package.GetProperty("tag").GetString()!,
                package.GetProperty("commit").GetString()!,
                package.GetProperty("files").EnumerateObject()
                    .ToDictionary(file => file.Name, file => file.Value.GetString()!, StringComparer.Ordinal)))
            .ToList();
    }

    private static IReadOnlyList<string> ListFixturePackages()
    {
        var folder = Path.Combine(Root, "fixture-packages");

        return Directory.Exists(folder)
            ? Directory.EnumerateDirectories(folder).Select(directory => Path.GetFileName(directory)).OrderBy(name => name, StringComparer.Ordinal).ToList()
            : Array.Empty<string>();
    }

    private static string SourceFile([CallerFilePath] string path = "") => path;
}
