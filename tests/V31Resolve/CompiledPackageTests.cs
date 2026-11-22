using System.Text;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Resolve;

/// <summary>
/// #1502 (specVersion 31, area 05): the published pair (part 07 §§ 1, 5; D10): <see cref="CompiledPackage.Publish"/>
/// checks, compiles <c>resolve.json</c>, hashes it, builds <c>lock.json</c> naming that hash and hashes it; and
/// <see cref="CompiledPackage.Verify"/>, the engine's check at load (§ 10), accepts only the exact bytes published,
/// refusing the compiled document with S07-006 and the lock with S07-017.
/// </summary>
public class CompiledPackageTests
{
    private const string Package = "example-nested-note";
    private const string Version = "v2026.12.1";

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private static Dictionary<string, byte[]> Source() =>
        ResolveFixture.NestedNote().WithJson("manifest.json", m => m["name"] = Package);

    private static CompiledPackage Published()
    {
        var (refusals, compiled) = CompiledPackage.Publish(Source(), ResolveFixture.Published(), LockCompilerTests.Example);
        Assert.Empty(refusals);
        Assert.NotNull(compiled);
        return compiled!;
    }

    private static byte[] Bytes(string text) => Utf8NoBom.GetBytes(text);

    private static SourceRefusal? Verify(CompiledPackage p, byte[] resolve, byte[] @lock, string? resolveHash = null, string? lockHash = null) =>
        CompiledPackage.Verify(Package, Version, resolve, @lock, resolveHash ?? p.ResolveHash, lockHash ?? p.LockHash);

    /// <summary>The same document re-serialised indented: the same JSON, not the same bytes.</summary>
    private static byte[] Indented(string text) => Bytes(JsonNode.Parse(text)!.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

    /// <summary>The first two members of the top-level object written the other way round, as text.</summary>
    private static byte[] Swapped(string text)
    {
        var document = JsonNode.Parse(text)!.AsObject();
        var members = document.Select(p => (p.Key, Text: CanonicalJson.Write(p.Value))).ToList();
        (members[0], members[1]) = (members[1], members[0]);
        return Bytes("{" + string.Join(",", members.Select(m => CanonicalJson.Write(JsonValue.Create(m.Key)) + ":" + m.Text)) + "}");
    }

    /// <summary>One byte inside a string changed (a letter of the package name).</summary>
    private static byte[] OneByte(string text)
    {
        var bytes = Bytes(text);
        var at = text.IndexOf("example-nested-note", StringComparison.Ordinal);
        Assert.True(at >= 0);
        bytes[Encoding.UTF8.GetByteCount(text[..at])] = (byte)'E';
        return bytes;
    }

    private static IEnumerable<(string What, byte[] Bytes)> Touched(string text) =>
    [
        ("re-indented", Indented(text)),
        ("with a trailing newline", Bytes(text + "\n")),
        ("with two members swapped", Swapped(text)),
        ("with one byte changed", OneByte(text)),
        ("empty", []),
        ("not JSON", Bytes("not json at all")),
        ("with a byte-order mark", [0xEF, 0xBB, 0xBF, .. Bytes(text)]),
        ("not UTF-8", [0xFF, .. Bytes(text)]),
    ];

    [Fact]
    public void Publish_PairsTheDocuments()
    {
        var published = Published();

        var resolve = JsonNode.Parse(published.Resolve)!.AsObject();
        var lockDocument = JsonNode.Parse(published.Lock)!.AsObject();
        Assert.Equal(published.ResolveHash, (string)lockDocument["resolve"]!["hash"]!);
        Assert.Equal("lock.json", (string)resolve["lock"]!);
        Assert.Equal(CanonicalJson.HashText(published.Resolve), published.ResolveHash);
        Assert.Equal(CanonicalJson.HashText(published.Lock), published.LockHash);
        Assert.Equal(CanonicalJson.Write(resolve), published.Resolve);
        Assert.Equal(CanonicalJson.Write(lockDocument), published.Lock);
        Assert.Equal((string)resolve["sourceHash"]!, published.SourceHash);
        Assert.Equal("7.2.5", (string)lockDocument["libraries"]!["Scriban"]!);
        Assert.Equal("^7.2.5", (string)resolve["requires"]!["scriban"]!);

        var again = Published();
        Assert.Equal(published.Resolve, again.Resolve);
        Assert.Equal(published.Lock, again.Lock);
        Assert.Equal(published.ResolveHash, again.ResolveHash);
        Assert.Equal(published.LockHash, again.LockHash);
    }

    [Fact]
    public void Publish_IsComplete_OnlyWhenEveryBodyIsKnown()
    {
        // #1504 (area 07, D18): the dialect says what every body reads; a seam that knows none leaves the pair incomplete.
        Assert.True(Published().Complete);

        var unknown = new StubTemplates(new Dictionary<string, string[]>(), everyBody: false);
        var (_, partial) = CompiledPackage.Publish(Source(), ResolveFixture.Published(), LockCompilerTests.Example, templates: unknown);
        Assert.False(partial!.Complete);
    }

    [Fact]
    public void Publish_ARefusedSource_GivesItsRefusals_AndNothingCompiled()
    {
        var source = Source().With("nodes/index.json", Index(["plan", "nowhere"]));
        var (refusals, compiled) = CompiledPackage.Publish(source, ResolveFixture.Published(), LockCompilerTests.Example);

        Assert.NotEmpty(refusals);
        Assert.Null(compiled);
        Assert.Equal(SourceCheck.Run(source, ResolveFixture.Published()).Refusals, refusals);
    }

    [Fact]
    public void Publish_DropsUploadedCopies_AndWritesItsOwn()
    {
        var source = Source();
        source["resolve.json"] = Bytes("{\"lock\": \"elsewhere.json\"}");
        source["lock.json"] = Bytes("{}");

        var (refusals, compiled) = CompiledPackage.Publish(source, ResolveFixture.Published(), LockCompilerTests.Example);

        Assert.Empty(refusals);
        Assert.Equal(Published().Resolve, compiled!.Resolve);
    }

    [Fact]
    public void Verify_AcceptsOnlyTheBytesPublished()
    {
        var p = Published();
        var resolve = Bytes(p.Resolve);
        var @lock = Bytes(p.Lock);

        Assert.Null(Verify(p, resolve, @lock));

        foreach (var (what, bytes) in Touched(p.Resolve))
        {
            var refusal = Verify(p, bytes, @lock);
            Assert.True(refusal?.Id == "S07-006", $"resolve.json {what}: {refusal?.Id} {refusal?.Sentence}");
            V31Contract.AssertSentence(refusal!);
            Assert.Equal(
                "Workflow example-nested-note@v2026.12.1 can't run: its compiled document doesn't match the hash published for it.",
                refusal!.Sentence);
        }

        Assert.Equal("S07-006", Verify(p, resolve, @lock, resolveHash: "sha256:" + new string('0', 64))?.Id);
        Assert.Equal("S07-006", Verify(p, resolve, @lock, resolveHash: "")?.Id);

        foreach (var (what, bytes) in Touched(p.Lock))
        {
            var refusal = Verify(p, resolve, bytes);
            Assert.True(refusal?.Id == "S07-017", $"lock.json {what}: {refusal?.Id} {refusal?.Sentence}");
            V31Contract.AssertSentence(refusal!);
        }

        Assert.Equal("S07-017", Verify(p, resolve, @lock, lockHash: "sha256:" + new string('0', 64))?.Id);
    }

    [Fact]
    public void Verify_RefusesALockOfAnotherResolve()
    {
        var p = Published();

        // Another package's lock, stamped as published: canonical and matching its stamp, but naming another resolve.
        var (_, other) = CompiledPackage.Publish(
            ResolveFixture.Conditional(), ResolveFixture.Published(), LockCompilerTests.Example);
        var refusal = Verify(p, Bytes(p.Resolve), Bytes(other!.Lock), lockHash: other.LockHash);
        Assert.Equal("S07-017", refusal?.Id);
        Assert.Equal(
            "Workflow example-nested-note@v2026.12.1 can't run: its lock doesn't match the hash published for it, or doesn't belong to its compiled document.",
            refusal!.Sentence);

        // This resolve's own lock, its resolve.hash edited and the stamp re-computed.
        var edited = JsonNode.Parse(p.Lock)!.AsObject();
        edited["resolve"]!["hash"] = "sha256:" + new string('1', 64);
        var text = CanonicalJson.Write(edited);
        Assert.Equal("S07-017", Verify(p, Bytes(p.Resolve), Bytes(text), lockHash: CanonicalJson.HashText(text))?.Id);
    }

    [Fact]
    public void Verify_RefusesAResolveWhoseLockMemberWasEdited()
    {
        var p = Published();
        var resolve = JsonNode.Parse(p.Resolve)!.AsObject();
        resolve["lock"] = "other-lock.json";
        var resolveText = CanonicalJson.Write(resolve);
        var resolveHash = CanonicalJson.HashText(resolveText);

        // The lock re-built for the edited resolve and both stamps re-computed: only the lock member is wrong.
        var lockDocument = JsonNode.Parse(p.Lock)!.AsObject();
        lockDocument["resolve"]!["hash"] = resolveHash;
        var lockText = CanonicalJson.Write(lockDocument);

        var refusal = Verify(p, Bytes(resolveText), Bytes(lockText), resolveHash, CanonicalJson.HashText(lockText));
        Assert.Equal("S07-017", refusal?.Id);
        V31Contract.AssertSentence(refusal!);
    }

    [Fact]
    public void Verify_RefusesARepeatedTopLevelMember_WithItsStampRecomputed()
    {
        // Each document with one of its top-level members written a second time at the end, the same value, and the stamp
        // re-computed over those bytes: the text is not the canonical text of any document, so it is not the one published.
        var p = Published();
        var resolve = p.Resolve[..^1] + ",\"lock\":\"lock.json\"}";
        var refusal = Verify(p, Bytes(resolve), Bytes(p.Lock), resolveHash: CanonicalJson.HashText(resolve));
        Assert.Equal("S07-006", refusal?.Id);
        V31Contract.AssertSentence(refusal!);

        var @lock = p.Lock[..^1] + ",\"culture\":\"invariant\"}";
        refusal = Verify(p, Bytes(p.Resolve), Bytes(@lock), lockHash: CanonicalJson.HashText(@lock));
        Assert.Equal("S07-017", refusal?.Id);
        V31Contract.AssertSentence(refusal!);
    }

    [Fact]
    public void Verify_TakesNoNull()
    {
        var p = Published();
        Assert.Throws<ArgumentNullException>(() => CompiledPackage.Verify(Package, Version, null!, Bytes(p.Lock), p.ResolveHash, p.LockHash));
        Assert.Throws<ArgumentNullException>(() => CompiledPackage.Verify(Package, Version, Bytes(p.Resolve), null!, p.ResolveHash, p.LockHash));
    }
}
