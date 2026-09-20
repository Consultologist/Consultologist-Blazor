using System.IO;
using System.IO.Compression;

namespace Consultologist.Web.Services.Workflow;

/// <summary>
/// #808/#806: zips a package's files — a path → text map — into the bytes a
/// browser download saves. One entry per file at its own path, ordered, UTF-8.
/// Shared by the editor's single-package download and Profile's registry export
/// (which prefixes each package's paths with a per-package subfolder), so the
/// zip building lives in one testable place rather than on the editor component.
/// </summary>
internal static class WorkflowPackageArchive
{
    internal static byte[] Zip(IReadOnlyDictionary<string, string> files)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, text) in files.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(text);
            }
        }

        return buffer.ToArray();
    }
}
