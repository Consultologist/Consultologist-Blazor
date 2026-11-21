using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1503 (specVersion 31, area 06; D1): the stamp of a 31 version, <c>publish.json</c> in its folder (part 09 § 1.2 step 3):
/// <c>{ "specVersion": 31, "resolveHash": "sha256:…", "lockHash": "sha256:…" }</c>, written canonical, read leniently
/// (other members ignored) and never with a throw: anything that is not such a stamp reads as none.
/// </summary>
public class PublishedStampTests
{
    private const string ResolveHash = "sha256:410dfe4b2b98f57840bb462558ad2cf0dfbdb3c69e54cd8056a43221ab429594";
    private const string LockHash = "sha256:e3b3c6b497934e9bb3fa426f5d524d6378668123b5e28a661e5d93a025fba548";

    [Fact]
    public void RoundTrips()
    {
        var stamp = new PublishedStamp(ResolveHash, LockHash);
        var json = stamp.ToJson();

        Assert.Equal("publish.json", PublishedStamp.FileName);
        Assert.Equal(31, PublishedStamp.SpecVersion);
        Assert.Equal($"{{\"lockHash\":\"{LockHash}\",\"resolveHash\":\"{ResolveHash}\",\"specVersion\":31}}", json);
        Assert.Equal(stamp, PublishedStamp.TryRead(json));

        // Area 11 may add members; a reader ignores them.
        var more = $"{{\"lockHash\":\"{LockHash}\",\"publishedBy\":\"Dr A. Sample\",\"resolveHash\":\"{ResolveHash}\",\"specVersion\":31}}";
        Assert.Equal(stamp, PublishedStamp.TryRead(more));
    }

    public static TheoryData<string?> NotStamps() => new(
        null,
        "",
        "[]",
        new WorkflowPackageStamp("output-contracts@v2026.12.1", new Dictionary<string, string>()).ToJson(),
        $"{{\"lockHash\":\"{LockHash}\",\"resolveHash\":\"{ResolveHash}\",\"specVersion\":30}}",
        $"{{\"lockHash\":\"{LockHash}\",\"resolveHash\":\"{ResolveHash[..^1]}\",\"specVersion\":31}}",
        $"{{\"lockHash\":\"{LockHash}\",\"resolveHash\":\"{ResolveHash.ToUpperInvariant().Replace("SHA256:", "sha256:", StringComparison.Ordinal)}\",\"specVersion\":31}}",
        "not json",
        $"{{\"lockHash\":\"{LockHash}\",\"specVersion\":31}}",
        $"{{\"lockHash\":\"{LockHash}\",\"resolveHash\":\"{ResolveHash}\",\"specVersion\":\"31\"}}",
        $"{{\"lockHash\":\"{LockHash}\",\"lockHash\":\"{LockHash}\",\"resolveHash\":\"{ResolveHash}\",\"specVersion\":31}}");

    [Theory]
    [MemberData(nameof(NotStamps))]
    public void TryRead_RefusesAnythingElse(string? json) =>
        Assert.Null(PublishedStamp.TryRead(json));
}
