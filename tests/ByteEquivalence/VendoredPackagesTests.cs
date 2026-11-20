using System.Security.Cryptography;
using System.Text.Json;

namespace Consultologist.Api.Tests.ByteEquivalence;

/// <summary>
/// #1498: the thirteen public packages are vendored from their spec-30 tags, so every later
/// byte-equivalence capture runs against the bytes today's engine was shown. This pins that the
/// index names the right tags, that the files on disk are the files the index hashes (and no
/// others), and that every package is at 30 and loads.
/// </summary>
public class VendoredPackagesTests
{
    private static readonly (string Name, string Tag)[] Pinned =
    [
        ("example-classifier-scope", "example-classifier-scope-v2026.11.3"),
        ("example-conditional-documents", "example-conditional-documents-v2026.11.3"),
        ("example-conditional-sections", "example-conditional-sections-v2026.11.3"),
        ("example-macros", "example-macros-v2026.11.3"),
        ("example-macros-warnings", "example-macros-warnings-v2026.11.3"),
        ("example-nested-note", "example-nested-note-v2026.11.2"),
        ("example-prior-letters", "example-prior-letters-v2026.11.1"),
        ("example-problem-list", "example-problem-list-v2026.11.2"),
        ("example-structured-intake", "example-structured-intake-v2026.11.3"),
        ("example-two-documents", "example-two-documents-v2026.11.3"),
        ("example-v12-constructs", "example-v12-constructs-v2026.11.3"),
        ("general", "general-v2026.11.3"),
        ("test/general", "test/general-v2026.11.3"),
    ];

    public static TheoryData<string> Names()
    {
        var names = new TheoryData<string>();

        foreach (var (name, _) in Pinned)
        {
            names.Add(name);
        }

        return names;
    }

    [Fact]
    public void TheIndex_NamesTheThirteenPublicPackages_AtTheirPinnedTags()
    {
        var vendored = ByteEquivalenceFixtures.Vendored;

        Assert.Equal(Pinned, vendored.Select(package => (package.Name, package.Tag)));
        Assert.All(vendored, package => Assert.Matches("^[0-9a-f]{40}$", package.Commit));
    }

    [Fact]
    public void EveryVendoredFile_HasTheBytesTheIndexRecords()
    {
        Assert.NotEmpty(ByteEquivalenceFixtures.Vendored);

        foreach (var package in ByteEquivalenceFixtures.Vendored)
        {
            foreach (var (relative, hash) in package.Files)
            {
                var path = Path.Combine(ByteEquivalenceFixtures.Root, "packages", package.Name, relative);

                Assert.True(File.Exists(path), $"{package.Name}/{relative} is indexed but not on disk.");
                Assert.Equal(hash, "sha256:" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
            }
        }
    }

    [Fact]
    public void NoFileOnDisk_IsMissingFromTheIndex()
    {
        var packages = Path.Combine(ByteEquivalenceFixtures.Root, "packages");

        var onDisk = Directory.EnumerateFiles(packages, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(packages, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(relative => relative != "index.json")
            .Order(StringComparer.Ordinal)
            .ToList();

        var indexed = ByteEquivalenceFixtures.Vendored
            .SelectMany(package => package.Files.Keys.Select(relative => $"{package.Name}/{relative}"))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(indexed, onDisk);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void EveryVendoredPackage_IsAtSpecVersion30_AndLoads(string name)
    {
        var tag = Pinned.Single(pin => pin.Name == name).Tag;
        var (manifestJson, _) = ByteEquivalenceFixtures.ReadPackage(name);
        using var document = JsonDocument.Parse(manifestJson);
        var manifest = document.RootElement;

        Assert.Equal(30, manifest.GetProperty("specVersion").GetInt32());
        Assert.Equal(name, manifest.GetProperty("name").GetString());
        Assert.Equal(tag, $"{name}-{manifest.GetProperty("version").GetString()}");

        ByteEquivalenceFixtures.Load(name);
    }
}
