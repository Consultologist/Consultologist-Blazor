using System.Security.Cryptography;
using System.Text;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Resolve;

/// <summary>
/// #1502 (specVersion 31, area 05): <c>sourceHash</c> (part 07 § 2.6): the SHA-256 of the RFC 8785 text of the
/// sorted listing of <c>{ path, hash }</c>, every source file's hash taken after line-ending normalisation, evidence
/// and <c>dag.mmd</c> left out.
/// </summary>
public class SourceHashTests
{
    private static IReadOnlyDictionary<string, string> Files(Dictionary<string, byte[]> files)
    {
        var parsed = files.Parse(ResolveFixture.Published().Names);
        Assert.True(parsed.Package != null, string.Join(" | ", parsed.Refusals.Select(r => $"{r.Id} {r.Sentence}")));
        return parsed.Files;
    }

    private static string Sha256(string text) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    [Fact]
    public void Listing()
    {
        var files = Files(Full());
        Assert.Contains("README.md", files.Keys);
        Assert.Contains("tests/note_basic.json", files.Keys);

        // RFC 8785 for an array of two-member objects of ASCII text with nothing to escape: members sorted
        // ("hash" before "path"), no whitespace. Every path of Full() is such text.
        Assert.All(files.Keys, p => Assert.Matches("^[A-Za-z0-9_./-]+$", p));
        var listing = "[" + string.Join(",", files
            .Where(f => f.Key != "dag.mmd")
            .OrderBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => $"{{\"hash\":\"{Sha256(f.Value)}\",\"path\":\"{f.Key}\"}}")) + "]";

        Assert.Equal(Sha256(listing), SourceHash.Of(files));
    }

    [Fact]
    public void SourceHash_IgnoresLineEndsAndEvidence()
    {
        var hash = SourceHash.Of(Files(Full()));

        var crlf = Full();
        foreach (var path in crlf.Keys.ToList())
            crlf.With(path, Text(crlf, path).Replace("\n", "\r\n", StringComparison.Ordinal));
        Assert.Equal(hash, SourceHash.Of(Files(crlf)));

        Assert.Equal(hash, SourceHash.Of(Files(Full().With("resolve.json", "{ \"documentVersion\": 1 }\n"))));
        Assert.Equal(hash, SourceHash.Of(Files(Full().With("lock.json", "{}\n").With("publish-report.json", "{}\n"))));
        Assert.Equal(hash, SourceHash.Of(Files(Full().With("dag.mmd", "flowchart TD\n  plan --> note\n"))));

        Assert.NotEqual(hash, SourceHash.Of(Files(Full().With("README.md", "# example-full\n\nSeen by Dr A. Sample.\n"))));
        Assert.NotEqual(hash, SourceHash.Of(Files(Full().WithJson("tests/note_basic.json", t => t["description"] = new System.Text.Json.Nodes.JsonArray("Two planned sections; the note is released.")))));
    }

    [Fact]
    public void SourceHash_OfTheCompiledCopies_LeavesThemOut()
    {
        // Handed a listing that still holds them (not SourceParseResult.Files), the hash leaves them out too.
        var files = Files(Full());
        var withEvidence = files.ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal);
        withEvidence["resolve.json"] = "{}\n";
        withEvidence["lock.json"] = "{}\n";
        withEvidence["publish-report.json"] = "{}\n";
        withEvidence["dag.mmd"] = "flowchart TD\n";
        withEvidence["nodes/plan.md"] = withEvidence["nodes/plan.md"].Replace("\n", "\r\n", StringComparison.Ordinal);

        Assert.Equal(SourceHash.Of(files), SourceHash.Of(withEvidence));
    }
}
