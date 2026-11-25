using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1502 (specVersion 31, area 05): a package published as its pair (part 07 §§ 1, 5): <paramref name="Resolve"/> and
/// <paramref name="Lock"/> are the canonical texts of <c>resolve.json</c> and <c>lock.json</c> (RFC 8785, the bytes
/// stored are their UTF-8), <paramref name="ResolveHash"/> and <paramref name="LockHash"/> their <c>sha256:</c>, and
/// <paramref name="SourceHash"/> the source's (§ 2.6), the three hashes the publish report and the stamp record.
/// <paramref name="Warnings"/> are <c>resolve.json</c>'s. <paramref name="Complete"/> is false while a body's reads are
/// not known (D1): the pair is then written from the headers alone and is not the one to store.
/// </summary>
public sealed record CompiledPackage(
    string Resolve,
    string ResolveHash,
    string Lock,
    string LockHash,
    string SourceHash,
    IReadOnlyList<SourceRefusal> Warnings,
    bool Complete)
{
    /// <summary>The compiled document's file name in a version's folder (§ 6).</summary>
    public const string ResolveFile = "resolve.json";

    /// <summary>
    /// The deepest nesting <see cref="Verify"/> reads; <see cref="CanonicalJson"/> writes nothing deeper, so a document
    /// past it is not the one published.
    /// </summary>
    private const int MaxDepth = 256;

    private static readonly UTF8Encoding Utf8 = new(false, true);

    /// <summary>
    /// Steps 1, 2 and 4.1 to 4.3 of part 07 § 5 on <paramref name="upload"/>: the check (<see cref="SourceCheck.Run"/>,
    /// with what <paramref name="templates"/> says each body reads), <c>resolve.json</c> compiled
    /// (<see cref="ResolveCompiler.Compile"/>, Scriban's range from <paramref name="toolchain"/>, the notes when
    /// <paramref name="limits"/> are given) and its hash, then <c>lock.json</c> built with that hash
    /// (<see cref="LockCompiler.Compile"/>) and its hash. Any refusal is given back with nothing compiled. The tests,
    /// the publish report and writing are not here (area 11's). A null argument other than the last two is the caller's error.
    /// </summary>
    public static (IReadOnlyList<SourceRefusal> Refusals, CompiledPackage? Compiled) Publish(
        IReadOnlyDictionary<string, byte[]> upload,
        TypeRegistry registry,
        Toolchain toolchain,
        PublishLimits? limits = null,
        ITemplateReads? templates = null) =>
        Publish(upload, registry, toolchain, limits, templates, out _);

    /// <summary>
    /// The same, with the check's result given out as <paramref name="check"/> whatever the outcome (#1508, area 11): the
    /// files as normalised, the paths normalised and dropped, and the parsed tests, which publish's later steps read.
    /// </summary>
    public static (IReadOnlyList<SourceRefusal> Refusals, CompiledPackage? Compiled) Publish(
        IReadOnlyDictionary<string, byte[]> upload,
        TypeRegistry registry,
        Toolchain toolchain,
        PublishLimits? limits,
        ITemplateReads? templates,
        out SourceCheckResult check)
    {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(toolchain);

        check = SourceCheck.Run(upload, registry, templates);
        if (check.Refusals.Count > 0)
            return (check.Refusals, null);

        var resolve = ResolveCompiler.Compile(check, registry, toolchain.Scriban, limits);
        if (resolve.Refusals.Count > 0 || resolve.Document == null || resolve.Canonical == null || resolve.Hash == null)
            return (resolve.Refusals, null);

        var lockText = CanonicalJson.Write(LockCompiler.Compile(resolve.Document, resolve.Hash, toolchain));
        return ([], new CompiledPackage(
            resolve.Canonical,
            resolve.Hash,
            lockText,
            CanonicalJson.HashText(lockText),
            SourceLayout.Text(resolve.Document["sourceHash"]) ?? "",
            resolve.Warnings,
            resolve.Complete));
    }

    /// <summary>
    /// The engine's check at load (part 07 § 5, § 10; part 09 § 1.2) of the pair stored for <paramref name="package"/>
    /// at <paramref name="version"/> against the version's stamp: S07-006 when <paramref name="resolve"/> is not the
    /// canonical text of itself (UTF-8, no byte-order mark, RFC 8785) or does not hash to
    /// <paramref name="stampedResolveHash"/>; else S07-017 when <paramref name="lock"/> is not, or does not hash to
    /// <paramref name="stampedLockHash"/>, or its <c>resolve.hash</c> is not the compiled document's hash, or the
    /// document's <c>lock</c> is not <c>lock.json</c>; null when all hold. Never throws on the bytes; a null argument is
    /// the caller's error.
    /// </summary>
    public static SourceRefusal? Verify(string package, string version, byte[] resolve, byte[] @lock, string stampedResolveHash, string stampedLockHash)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(resolve);
        ArgumentNullException.ThrowIfNull(@lock);
        ArgumentNullException.ThrowIfNull(stampedResolveHash);
        ArgumentNullException.ThrowIfNull(stampedLockHash);

        var resolveDocument = Canonical(resolve, out var resolveHash);
        if (resolveDocument == null || !string.Equals(resolveHash, stampedResolveHash, StringComparison.Ordinal))
            return ResolveSentences.ResolveDoesNotMatch(package, version);

        var lockDocument = Canonical(@lock, out var lockHash);
        if (lockDocument == null
            || !string.Equals(lockHash, stampedLockHash, StringComparison.Ordinal)
            || SourceLayout.Text(LockCompiler.Member(LockCompiler.Member(lockDocument, "resolve"), "hash")) != resolveHash
            || SourceLayout.Text(LockCompiler.Member(resolveDocument, "lock")) != ResolveCompiler.LockFile)
        {
            return ResolveSentences.LockDoesNotMatch(package, version);
        }

        return null;
    }

    /// <summary>
    /// The document <paramref name="bytes"/> hold when they are exactly the UTF-8 of its canonical text, with
    /// <paramref name="hash"/> that text's; else null (not UTF-8, not JSON, too deep, or any other bytes).
    /// </summary>
    private static JsonNode? Canonical(byte[] bytes, out string? hash)
    {
        hash = null;
        if (bytes.Length == 0)
            return null;

        string text;
        string canonical;
        JsonNode? document;
        try
        {
            text = Utf8.GetString(bytes);
            document = JsonNode.Parse(bytes, documentOptions: new JsonDocumentOptions { MaxDepth = MaxDepth });

            // A repeated member surfaces when the object is first read, so the canonical text is written in here too.
            canonical = CanonicalJson.Write(document);
        }
        catch (Exception e) when (e is JsonException or DecoderFallbackException or ArgumentException or InvalidOperationException)
        {
            return null;
        }

        if (document == null || !string.Equals(canonical, text, StringComparison.Ordinal))
            return null;

        hash = CanonicalJson.HashText(canonical);
        return document;
    }
}
