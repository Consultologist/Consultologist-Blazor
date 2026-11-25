using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure;
using Azure.Storage.Blobs.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.V31;

/// <summary>
/// #1503 (specVersion 31, area 06): the registry's blobs, read whole. #1508 (area 11): and written, each change in the
/// private container the account's kind picks, as the 5–30 writer's.
/// </summary>
public interface IRegistryFiles
{
    /// <summary>
    /// The bytes of the blob at <paramref name="path"/> (<c>{name}/{version}/{file}</c>) in the container that holds
    /// <paramref name="packageName"/>, or null when it is absent.
    /// </summary>
    Task<byte[]?> TryReadAsync(string packageName, string path, CancellationToken ct);

    /// <summary>Writes <paramref name="bytes"/> at <paramref name="path"/> with <paramref name="contentType"/>, replacing any blob there.</summary>
    Task WriteAsync(string? accountKind, string path, byte[] bytes, string contentType, CancellationToken ct);

    /// <summary>
    /// Creates the blob at <paramref name="path"/> only if none is there (If-None-Match <c>*</c>): false, and nothing
    /// written, when one is.
    /// </summary>
    Task<bool> TryCreateAsync(string? accountKind, string path, byte[] bytes, string contentType, CancellationToken ct);

    /// <summary>Deletes every blob whose path starts with <paramref name="prefix"/>.</summary>
    Task DeleteAllAsync(string? accountKind, string prefix, CancellationToken ct);
}

/// <summary>
/// #1503: <see cref="IRegistryFiles"/> over the registry's containers: the public one for a repo-owned name, the private
/// pair for an <c>acct-*</c> one (<see cref="WorkflowPackageBlobContainerFactory.GetContainersFor"/>), the first hit the
/// only one, as the 5–30 store reads them. #1508 (area 11): a change goes to the kind's private container
/// (<see cref="WorkflowPackageBlobContainerFactory.GetContainer"/>, the 5–30 writer's), its client taken per call.
/// </summary>
public sealed class BlobRegistryFiles(WorkflowPackageBlobContainerFactory containers) : IRegistryFiles
{
    public async Task WriteAsync(string? accountKind, string path, byte[] bytes, string contentType, CancellationToken ct) =>
        await containers.GetContainer(accountKind).GetBlobClient(path).UploadAsync(
            BinaryData.FromBytes(bytes),
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            ct);

    public async Task<bool> TryCreateAsync(string? accountKind, string path, byte[] bytes, string contentType, CancellationToken ct)
    {
        try
        {
            await containers.GetContainer(accountKind).GetBlobClient(path).UploadAsync(
                BinaryData.FromBytes(bytes),
                new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                    Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                },
                ct);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412)
        {
            return false;
        }
    }

    public async Task DeleteAllAsync(string? accountKind, string prefix, CancellationToken ct)
    {
        var container = containers.GetContainer(accountKind);
        await foreach (var blob in container.GetBlobsAsync(prefix: prefix, cancellationToken: ct))
            await container.GetBlobClient(blob.Name).DeleteIfExistsAsync(cancellationToken: ct);
    }

    public async Task<byte[]?> TryReadAsync(string packageName, string path, CancellationToken ct)
    {
        foreach (var container in containers.GetContainersFor(packageName))
        {
            try
            {
                var response = await container.GetBlobClient(path).DownloadContentAsync(ct);
                return response.Value.Content.ToArray();
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
            }
        }

        return null;
    }
}

/// <summary>
/// #1503: a 31 load: the plan, or the refusal a start failure records (D5): <paramref name="Refusal"/> the sentence the
/// job shows under <paramref name="RefusalId"/>, <paramref name="OperatorDetail"/> what the operator's log keeps.
/// <paramref name="Unreadable"/>: the registry could not be read (the version's pointer, or a file of the folder), so
/// nothing is known of the version; a start then takes today's path, which meets the same registry and answers as it does
/// with 31 off. <paramref name="OperatorDetail"/> says what failed (the package, the version, the failure's type and status).
/// #1506 (area 09; R4): with a plan, <paramref name="Lock"/> is the text of the version's verified <c>lock.json</c>, which a
/// job's <c>versionsRan</c> is compared with member by member.
/// </summary>
public sealed record CompiledLoad(RunPlan? Plan, string? Refusal, string? RefusalId, string? OperatorDetail, bool Unreadable = false, string? Lock = null);

/// <summary>#1503 (specVersion 31, area 06; part 09 § 1.2 steps 1 to 5): loads a 31 version from its stamped pair.</summary>
public interface ICompiledPackageStore
{
    /// <summary>null: the version has no 31 stamp, so it is today's path's. Otherwise the plan, or the refusal a start failure records.</summary>
    Task<CompiledLoad?> TryLoadAsync(WorkflowPackageRef packageRef, CancellationToken ct);
}

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 §§ 1.1, 1.2 steps 1 to 5; part 07 §§ 3, 4.4, 5; D1, D2, D4, D5): the 31 load.
/// Nothing is read unless this deployment runs 31 (<see cref="SpecVersionAdmission.Runs31"/>). The ref's version is
/// resolved as the 5–30 store resolves it (its pointer read and cache); then <c>publish.json</c> is read, and a version
/// without a 31 stamp is today's path's (null) after that one read. Otherwise <c>resolve.json</c> and <c>lock.json</c>
/// are read (either absent: A06-004), verified against the stamp (<see cref="CompiledPackage.Verify"/>: S07-006 shown as
/// S09-009, S07-017 as S09-014, the S07 sentence for the operator), held to D4 (<c>requires.spec</c> and
/// <c>specVersion</c> 31, else S09-001; <c>package</c> naming the folder read, else S07-006 as S09-009), to this engine
/// (<see cref="RequiresCheck.Engine"/>: the first sentence on the job, all of them for the operator), and built into a
/// plan with the engine's regex limit. A plan is cached in memory per <c>name@version</c>, holding its
/// <c>resolveHash</c>; a stamped folder never changes, so a second load reads nothing. A refusal is not cached. Safe to
/// call concurrently: two first loads may both build, and the first plan stored is the one both return. The engine's
/// capabilities are read only here, for a stamped version, so a host that does not run 31 never builds them, nor the 31
/// agents catalogue they hold (#1505 R15).
/// </summary>
public sealed class CompiledPackageStore(
    IRegistryFiles files,
    IWorkflowPackageVersions versions,
    Lazy<EngineCapabilities> engine,
    SpecVersionAdmission admission,
    EngineLimitSettings limits) : ICompiledPackageStore
{
    private const string S07HashId = "S07-006";
    private const string S09HashId = "S09-009";
    private const string S09LockId = "S09-014";
    private const string NotSupportedId = "S09-001";
    private const string NotPublishedId = "A06-004";

    /// <summary>The deepest JSON read here; <see cref="CanonicalJson"/> writes nothing deeper than <see cref="CompiledPackage.Verify"/> reads.</summary>
    private const int MaxDepth = 256;

    private static readonly UTF8Encoding Utf8 = new(false, true);

    private readonly ConcurrentDictionary<string, (RunPlan Plan, string Lock)> _plans = new(StringComparer.Ordinal);

    public async Task<CompiledLoad?> TryLoadAsync(WorkflowPackageRef packageRef, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(packageRef);
        if (!admission.Runs31)
            return null;

        var name = packageRef.Name;
        var version = packageRef.Version;
        PublishedStamp? stamp;
        byte[]? resolveBytes, lockBytes;
        try
        {
            version = await versions.ResolveVersionAsync(packageRef, ct);
            if (_plans.TryGetValue($"{name}@{version}", out var cached))
                return Loaded(cached);

            var folder = $"{name}/{version}/";
            stamp = PublishedStamp.TryRead(Text(await files.TryReadAsync(name, folder + PublishedStamp.FileName, ct)));
            if (stamp == null)
                return null;

            resolveBytes = await files.TryReadAsync(name, folder + CompiledPackage.ResolveFile, ct);
            lockBytes = await files.TryReadAsync(name, folder + ResolveCompiler.LockFile, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The registry could not be read: a pointer missing or invalid, a storage failure. Nothing is known of the
            // version, so nothing is refused; the failure's type and status are the operator's (never its message, which
            // can carry a URI).
            return new CompiledLoad(null, null, null,
                $"The registry could not be read for {name}@{version}: {ex.GetType().Name}{(ex is RequestFailedException failed ? $" {failed.Status}" : "")}.",
                Unreadable: true);
        }

        var key = $"{name}@{version}";
        if (resolveBytes == null || lockBytes == null)
            return Refused(NotPublishedId, NotPublished(name, version), null);

        if (CompiledPackage.Verify(name, version, resolveBytes, lockBytes, stamp.ResolveHash, stamp.LockHash) is { } mismatch)
            return Shown(mismatch, name, version);

        // Verified: both are the canonical UTF-8 of a JSON document. Neither can be anything but an object here: Verify
        // reads the lock's resolve.hash and the document's lock member, which a non-object lacks, and refuses it (S07-017).
        // The check stays as the guard of the casts below.
        if (Parse(resolveBytes) is not JsonObject resolve || Parse(lockBytes) is not JsonObject @lock)
            return Shown(ResolveSentences.ResolveDoesNotMatch(name, version), name, version);

        // D4: a document declaring another spec version is S09-001; one not declaring it as publish writes is not publish's.
        if (!Declares(resolve, out var other))
            return Shown(ResolveSentences.ResolveDoesNotMatch(name, version), name, version);
        if (other is { } declared)
            return Refused(NotSupportedId, admission.NotSupported(declared), null);

        if (!Names(resolve, name, version) || !Names(@lock, name, version))
            return Shown(ResolveSentences.ResolveDoesNotMatch(name, version), name, version);

        // #1504 (R6): a document that is not what publish wrote (S07-006, an agent's version not an integer) is shown as S09-009.
        var lacking = RequiresCheck.Engine(resolve, @lock, engine.Value);
        if (lacking.FirstOrDefault(r => r.Id == S07HashId) is { } unwritten)
            return Shown(unwritten, name, version);
        if (lacking.Count > 0)
            return Refused(lacking[0].Id, lacking[0].Sentence, string.Join("\n", lacking.Select(r => r.Sentence)));

        var (plan, refusal) = RunPlan.Build(Utf8.GetString(resolveBytes), stamp.ResolveHash, stamp.LockHash, limits.RegexLimit);
        if (plan == null)
            return Shown(refusal ?? ResolveSentences.ResolveDoesNotMatch(name, version), name, version);

        return Loaded(_plans.GetOrAdd(key, (plan, Utf8.GetString(lockBytes))));
    }

    /// <summary>A plan as a load answers it, with its lock's text.</summary>
    private static CompiledLoad Loaded((RunPlan Plan, string Lock) loaded) => new(loaded.Plan, null, null, null, Lock: loaded.Lock);

    private static CompiledLoad Refused(string id, string sentence, string? operatorDetail) => new(null, sentence, id, operatorDetail);

    /// <summary>D5: part 07's S07-006 and S07-017 are shown on the job as part 09's S09-009 and S09-014, kept for the operator as they are.</summary>
    private static CompiledLoad Shown(SourceRefusal refusal, string name, string version) => refusal.Id switch
    {
        "S07-017" => Refused(S09LockId, LockMismatch(name, version), refusal.Sentence),
        _ => Refused(S09HashId, HashMismatch(name, version), refusal.Sentence),
    };

    /// <summary>A06-004 for <paramref name="name"/>@<paramref name="version"/>: stamped 31 without its compiled documents.</summary>
    public static string NotPublished(string name, string version) => $"Workflow {name}@{version} isn't published.";

    /// <summary>S09-009 for <paramref name="name"/>@<paramref name="version"/>.</summary>
    public static string HashMismatch(string name, string version) =>
        $"The compiled workflow {name}@{version} doesn't match its published hash, so it can't be run. Tell the operator.";

    /// <summary>S09-014 for <paramref name="name"/>@<paramref name="version"/>.</summary>
    public static string LockMismatch(string name, string version) =>
        $"The compiled workflow {name}@{version} doesn't match its lock, so it can't be run. Tell the operator.";

    /// <summary>
    /// D4: what the document declares of its spec version, <c>requires.spec</c> (text) first, then <c>specVersion</c>
    /// (a number): false when either does not declare an integer in publish's form (absent, of another kind, or not
    /// written as the integer itself), so the document is not what publish wrote; true otherwise, with
    /// <paramref name="other"/> the first that declares a version other than 31, or null when both declare 31.
    /// </summary>
    private static bool Declares(JsonObject resolve, out int? other)
    {
        other = null;
        var spec = LockCompiler.Member(LockCompiler.Member(resolve, "requires"), "spec");
        if (spec is not JsonValue s || s.GetValueKind() != JsonValueKind.String
            || !int.TryParse(s.GetValue<string>(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var required)
            || required.ToString(CultureInfo.InvariantCulture) != s.GetValue<string>())
        {
            return false;
        }

        var specVersion = LockCompiler.Member(resolve, "specVersion");
        if (specVersion is not JsonValue v || v.GetValueKind() != JsonValueKind.Number || !v.TryGetValue<int>(out var declared))
            return false;

        other = required != PublishedStamp.SpecVersion ? required
            : declared != PublishedStamp.SpecVersion ? declared
            : null;
        return true;
    }

    /// <summary>D4: whether the document's <c>package</c> is the folder it was read from.</summary>
    private static bool Names(JsonObject document, string name, string version)
    {
        var package = LockCompiler.Member(document, "package");
        return SourceLayout.Text(LockCompiler.Member(package, "name")) == name
            && SourceLayout.Text(LockCompiler.Member(package, "version")) == version;
    }

    private static JsonNode? Parse(byte[] bytes)
    {
        try
        {
            return JsonNode.Parse(bytes, documentOptions: new JsonDocumentOptions { MaxDepth = MaxDepth });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The UTF-8 text of <paramref name="bytes"/>, or null when there are none or they are not UTF-8.</summary>
    private static string? Text(byte[]? bytes)
    {
        if (bytes == null)
            return null;
        try
        {
            return Utf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }
}
