using System.IO;
using System.IO.Compression;
using System.Text;

namespace Consultologist.Web.Services.Workflow.V31;

/// <summary>
/// #1509 (Task 23; part 09 § 8.1; D14): what the Editor's download of a specVersion 31 package holds — the stored
/// <c>manifest.json</c>, every file the content response carries, and, when the content holds them, the published
/// version's <c>resolve.json</c>, <c>lock.json</c> and <c>publish-report.json</c>. Each is written from the response
/// member's raw text (<c>JsonElement.GetRawText()</c>), never re-serialised, so the manifest keeps its <c>null</c>s.
/// </summary>
public static class PackageDownload31
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The entries, <c>manifest.json</c> first, then the files by path (ordinal), then the evidence present.</summary>
    public static IReadOnlyList<(string Path, byte[] Bytes)> Entries(WorkflowPackageContentResponse content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var entries = new List<(string Path, byte[] Bytes)> { ("manifest.json", Utf8.GetBytes(content.Manifest.GetRawText())) };
        entries.AddRange(content.Files
            .OrderBy(file => file.Key, StringComparer.Ordinal)
            .Select(file => (file.Key, Utf8.GetBytes(file.Value))));

        foreach (var (path, member) in new[] { ("resolve.json", content.Resolve), ("lock.json", content.Lock), ("publish-report.json", content.PublishReport) })
        {
            if (member is { } element)
            {
                entries.Add((path, Utf8.GetBytes(element.GetRawText())));
            }
        }

        return entries;
    }

    /// <summary>The entries zipped, one entry per path with its bytes as they are.</summary>
    public static byte[] Zip(WorkflowPackageContentResponse content)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, bytes) in Entries(content))
            {
                using var stream = archive.CreateEntry(path, CompressionLevel.Optimal).Open();
                stream.Write(bytes);
            }
        }

        return buffer.ToArray();
    }
}
