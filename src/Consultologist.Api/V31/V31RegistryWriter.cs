using System.Text;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Publish;

namespace Consultologist.Api.V31;

/// <summary>
/// #1508 (specVersion 31, area 11; part 07 § 5 steps 4.5 and 4.6; D2; Review Focus 1 and 2): <see cref="IV31RegistryWriter"/>
/// over the registry's blobs (<see cref="IRegistryFiles"/>), each change in the private container the account's kind picks.
/// A version is stamped when its folder has a <c>publish.json</c>, of any shape (the 5–30 stamp has the same name).
/// <para>
/// One writer at a time holds a version's folder: a write first takes a lease on the version's lock blob
/// (<see cref="LeasePath"/>, beside the folder, never in it), and a write that cannot take it is lost (false), writing
/// nothing. The holder checks the stamp (a stamped folder is never written to), clears the folder (every blob but the
/// stamp, so a publish that died before its stamp leaves nothing behind for the retry), writes the files in the order
/// given (the source, then <c>lock.json</c>, the publish report and <c>resolve.json</c> last), each with its media type,
/// reads <c>resolve.json</c> and <c>lock.json</c> back and verifies them against the stamp about to be written
/// (<see cref="CompiledPackage.Verify"/>, the engine's own check at load), and creates the stamp only if absent
/// (If-None-Match <c>*</c>); then it releases the lease. The lease is renewed before each write and before the stamp; a
/// lease lost (it lapsed and another writer took it) stops the write, unstamped. A dead writer's lease lapses on its own
/// after <see cref="IRegistryFiles.LeaseSeconds"/>, and the retry then clears and rewrites the unstamped folder.
/// </para>
/// <para>
/// The lease guarantees, among writers of this class, that no two write a folder at once and none writes a stamped one.
/// What it cannot cover (a step outliving its renewed lease, a writer outside this class) the create-if-absent stamp, the
/// clear that keeps the stamp, the read-back, and at last the engine's load-time <see cref="CompiledPackage.Verify"/>
/// backstop. Constructing it touches no storage.
/// </para>
/// </summary>
public sealed class V31RegistryWriter(IRegistryFiles registry) : IV31RegistryWriter
{
    /// <summary>The media type of a file that is not one of the compiled documents, markdown or the diagram.</summary>
    public const string JsonMediaType = "application/json";

    /// <summary>The media type of a <c>.md</c> file.</summary>
    public const string MarkdownMediaType = "text/markdown";

    /// <summary>The media type of the diagram, <c>dag.mmd</c> (D1).</summary>
    public const string DiagramMediaType = "text/plain";

    /// <summary>The lock blob's suffix: <c>{name}/{version}.lease</c>.</summary>
    public const string LeaseSuffix = ".lease";

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
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(stamp);

        var leasePath = LeasePath(name, version);
        if (await registry.TryAcquireLeaseAsync(accountKind, leasePath, ct) is not { } lease)
            return false;

        try
        {
            if (await IsPublishedAsync(accountKind, name, version, ct))
                return false;

            var folder = Folder(name, version);
            var stampPath = folder + PublishedStamp.FileName;
            await registry.DeleteAllAsync(accountKind, folder, stampPath, ct);
            foreach (var file in files)
            {
                if (!await registry.RenewLeaseAsync(accountKind, leasePath, lease, ct))
                    return false;
                await registry.WriteAsync(accountKind, folder + file.Path, file.Bytes, MediaTypeOf(file.Path), ct);
            }

            // Read through the name's containers, which for an acct-* name are the private pair: the kind's container,
            // written above, is one of them, and the version lives in that one only.
            var resolve = await registry.TryReadAsync(name, folder + CompiledPackage.ResolveFile, ct);
            var @lock = await registry.TryReadAsync(name, folder + ResolveCompiler.LockFile, ct);
            if (resolve == null || @lock == null
                || CompiledPackage.Verify(name, version, resolve, @lock, stamp.ResolveHash, stamp.LockHash) != null)
            {
                return false;
            }

            return await registry.RenewLeaseAsync(accountKind, leasePath, lease, ct)
                && await registry.TryCreateAsync(accountKind, stampPath, Utf8.GetBytes(stamp.ToJson()), JsonMediaType, ct);
        }
        finally
        {
            await registry.ReleaseLeaseAsync(accountKind, leasePath, lease, CancellationToken.None);
        }
    }

    /// <summary>The media type a file of the version's folder is written with, by its path there.</summary>
    public static string MediaTypeOf(string path) => path switch
    {
        CompiledPackage.ResolveFile => CompiledPackage.ResolveMediaType,
        ResolveCompiler.LockFile => CompiledPackage.LockMediaType,
        PublishReport.FileName => PublishReport.MediaType,
        PackagePublish.DiagramFile => DiagramMediaType,
        _ when path.EndsWith(".md", StringComparison.Ordinal) => MarkdownMediaType,
        _ => JsonMediaType,
    };

    /// <summary>
    /// The version's lock blob, <c>{name}/{version}.lease</c>: under the package's prefix (so the account-closure sweep takes
    /// it) and beside the version's folder, not in it, so no reader of a folder's files takes it for one, and no listing
    /// takes it for a version (a version is listed by its <c>{name}/{version}/manifest.json</c>).
    /// </summary>
    public static string LeasePath(string name, string version) => $"{name}/{version}{LeaseSuffix}";

    private static string Folder(string name, string version) => $"{name}/{version}/";
}
