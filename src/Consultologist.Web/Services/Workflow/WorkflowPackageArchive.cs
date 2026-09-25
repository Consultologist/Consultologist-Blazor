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

    // #847: import guards. A package body is text-only and small; these bound an
    // untrusted upload before anything is read into memory (a zip bomb only harms
    // the uploader's own browser, but the caps keep it cheap and mirror the
    // server's publish limits). Per-file/total match WorkflowPackagePublisher.
    private const int MaxEntries = 512;
    private const int MaxEntryBytes = 256 * 1024;
    private const int MaxTotalBytes = 2 * 1024 * 1024;

    /// <summary>
    /// #847: the reverse of <see cref="Zip"/> — reads a package zip's entries
    /// (path → UTF-8 text) into an ordinal map. Throws <see cref="InvalidDataException"/>
    /// with a caller-surfaceable message on a directory/traversal entry or a cap
    /// breach; the caller (the editor import) shows it at the desk.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> Unzip(byte[] bytes)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        using var buffer = new MemoryStream(bytes);

        ZipArchive archive;
        try
        {
            archive = new ZipArchive(buffer, ZipArchiveMode.Read);
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException("That file is not a valid .zip package.");
        }

        using (archive)
        {
            if (archive.Entries.Count > MaxEntries)
            {
                throw new InvalidDataException($"The package has too many files (more than {MaxEntries}).");
            }

            long total = 0;
            foreach (var entry in archive.Entries)
            {
                var path = entry.FullName;

                // A directory entry (trailing slash, zero length) carries no file.
                if (path.EndsWith('/'))
                {
                    continue;
                }

                if (path.Length == 0 || path.StartsWith('/') || path.Contains("..") || path.Contains('\\'))
                {
                    throw new InvalidDataException($"The package has an unsafe file path: '{path}'.");
                }

                if (entry.Length > MaxEntryBytes)
                {
                    throw new InvalidDataException($"File '{path}' is larger than {MaxEntryBytes / 1024} KB.");
                }

                total += entry.Length;
                if (total > MaxTotalBytes)
                {
                    throw new InvalidDataException($"The package is larger than {MaxTotalBytes / (1024 * 1024)} MB uncompressed.");
                }

                using var reader = new StreamReader(entry.Open());
                files[path] = reader.ReadToEnd();
            }
        }

        return files;
    }
}
