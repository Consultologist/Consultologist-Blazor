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
}
