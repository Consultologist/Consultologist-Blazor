using System.IO;
using System.IO.Compression;
using Consultologist.Web.Services.Workflow;
using Xunit;

namespace Consultologist.Web.Tests;

/// <summary>
/// #806/#808: the shared zip primitive both the single-package download and the
/// registry export build on — a path -> text map to zip bytes, one entry each.
/// </summary>
public class WorkflowPackageArchiveTests
{
    [Fact]
    public void Zip_WritesOneEntryPerFile_RoundTripping()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["manifest.json"] = """{ "name": "acct-a" }""",
            ["prompts/draft.md"] = "Draft {{ x }}.",
            ["data/standards/index.json"] = """{ "items": [] }"""
        };

        using var archive = new ZipArchive(new MemoryStream(WorkflowPackageArchive.Zip(files)), ZipArchiveMode.Read);
        var read = archive.Entries.ToDictionary(
            entry => entry.FullName,
            entry =>
            {
                using var reader = new StreamReader(entry.Open());
                return reader.ReadToEnd();
            },
            StringComparer.Ordinal);

        Assert.Equal(files, read);
    }

    // #847: Unzip is the exact reverse of Zip — the import round-trip.
    [Fact]
    public void UnzipOfZip_ReturnsTheSameFiles()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["manifest.json"] = """{ "name": "acct-a", "version": "v1", "specVersion": 5 }""",
            ["prompts/draft.md"] = "Draft {{ x }}.",
            ["data/standards/index.json"] = """{ "items": [] }""",
            ["data/specialty.txt"] = "cardiology"
        };

        var read = WorkflowPackageArchive.Unzip(WorkflowPackageArchive.Zip(files));

        Assert.Equal(files, read);
    }

    [Fact]
    public void Unzip_RejectsANonZip()
    {
        Assert.Throws<InvalidDataException>(() => WorkflowPackageArchive.Unzip(new byte[] { 1, 2, 3, 4 }));
    }

    [Fact]
    public void Unzip_RejectsATraversalEntry()
    {
        var bomb = ZipOf(("../escape.md", "nope"));

        var ex = Assert.Throws<InvalidDataException>(() => WorkflowPackageArchive.Unzip(bomb));
        Assert.Contains("unsafe file path", ex.Message);
    }

    [Fact]
    public void Unzip_RejectsAnOversizeEntry()
    {
        var big = ZipOf(("prompts/big.md", new string('x', 300 * 1024))); // > 256 KB per-entry cap

        Assert.Throws<InvalidDataException>(() => WorkflowPackageArchive.Unzip(big));
    }

    [Fact]
    public void Unzip_SkipsDirectoryEntries()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("prompts/");                                  // a bare directory entry
            using var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open());
            writer.Write("{}");
        }

        var read = WorkflowPackageArchive.Unzip(buffer.ToArray());

        Assert.Equal(new[] { "manifest.json" }, read.Keys);
    }

    private static byte[] ZipOf(params (string Path, string Text)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, text) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open());
                writer.Write(text);
            }
        }

        return buffer.ToArray();
    }
}
