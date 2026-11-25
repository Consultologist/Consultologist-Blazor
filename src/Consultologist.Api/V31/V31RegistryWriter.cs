using System.Text;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Publish;

namespace Consultologist.Api.V31;

/// <summary>
/// #1508 (specVersion 31, area 11; part 07 § 5 steps 4.5 and 4.6; D2; Review Focus 1 and 2): <see cref="IV31RegistryWriter"/>
/// over the registry's blobs (<see cref="IRegistryFiles"/>), each change in the private container the account's kind picks.
/// A version is stamped when its folder has a <c>publish.json</c>, of any shape (the 5–30 stamp has the same name), and a
/// stamped folder is never written to. An unstamped folder is no version: it is cleared whole (a publish that died before
/// its stamp leaves nothing behind for the retry), then the files are written in the order given (the source, then
/// <c>lock.json</c>, the publish report and <c>resolve.json</c> last), each with its media type, and then the stamp, created
/// only if absent (If-None-Match <c>*</c>), so of two publishes racing for one version exactly one stamps. A rival's files may
/// have overwritten ours before then, so <c>resolve.json</c> and <c>lock.json</c> are read back and verified against the
/// stamp about to be written (<see cref="CompiledPackage.Verify"/>, the engine's own check at load): on a mismatch nothing is
/// stamped and the write is lost. The engine's load-time check remains the last line of defence. Constructing it touches no
/// storage.
/// </summary>
public sealed class V31RegistryWriter(IRegistryFiles registry) : IV31RegistryWriter
{
    /// <summary>The media type of a file that is not one of the compiled documents nor markdown.</summary>
    public const string JsonMediaType = "application/json";

    /// <summary>The media type of a <c>.md</c> file.</summary>
    public const string MarkdownMediaType = "text/markdown";

    private static readonly UTF8Encoding Utf8 = new(false);

    public async Task<bool> IsPublishedAsync(string? accountKind, string name, string version, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(version);
        return await registry.TryReadAsync(name, Folder(name, version) + PublishedStamp.FileName, ct) != null;
    }

    public async Task<bool> WriteAsync(
        string? accountKind, string name, string version, IReadOnlyList<PublishFile> files, PublishedStamp stamp, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(stamp);
        if (await IsPublishedAsync(accountKind, name, version, ct))
            return false;

        var folder = Folder(name, version);
        await registry.DeleteAllAsync(accountKind, folder, ct);
        foreach (var file in files)
            await registry.WriteAsync(accountKind, folder + file.Path, file.Bytes, MediaTypeOf(file.Path), ct);

        var resolve = await registry.TryReadAsync(name, folder + CompiledPackage.ResolveFile, ct);
        var @lock = await registry.TryReadAsync(name, folder + ResolveCompiler.LockFile, ct);
        if (resolve == null || @lock == null
            || CompiledPackage.Verify(name, version, resolve, @lock, stamp.ResolveHash, stamp.LockHash) != null)
        {
            return false;
        }

        return await registry.TryCreateAsync(accountKind, folder + PublishedStamp.FileName, Utf8.GetBytes(stamp.ToJson()), JsonMediaType, ct);
    }

    /// <summary>The media type a file of the version's folder is written with, by its path there.</summary>
    public static string MediaTypeOf(string path) => path switch
    {
        CompiledPackage.ResolveFile => CompiledPackage.ResolveMediaType,
        ResolveCompiler.LockFile => CompiledPackage.LockMediaType,
        PublishReport.FileName => PublishReport.MediaType,
        _ when path.EndsWith(".md", StringComparison.Ordinal) => MarkdownMediaType,
        _ => JsonMediaType,
    };

    private static string Folder(string name, string version) => $"{name}/{version}/";
}
