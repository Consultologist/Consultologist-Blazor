using System.IO.Compression;
using System.Text;
using Consultologist.Api.Jobs;
using Consultologist.PackageFormat.V31;
using Microsoft.Azure.Functions.Worker;

namespace Consultologist.Api.V31;

/// <summary>#1503 (specVersion 31, area 06): the job's package version and the two hashes its start checked.</summary>
public sealed record LoadPlanV31Input(string PackageName, string PackageVersion, string ResolveHash, string LockHash);

/// <summary>
/// #1503: the verified <c>resolve.json</c> (<see cref="LoadPlanV31Activity.Pack"/>'s gzip then base64), or the sentence the job
/// fails with.
/// </summary>
public sealed record LoadPlanV31Result(string? Resolve, string? Refusal);

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 § 1.2 steps 2 to 5): the plan's text for a 31 orchestration, replay-safely. The
/// store loads and verifies the version again (its cache after the first load), so a job never runs bytes other than the
/// ones its start checked: the plan's <c>resolveHash</c> must equal the job's, else S09-009, and its <c>lockHash</c>, else S09-014 (D5). The text is
/// the plan's document written canonically, which is the verified bytes; its one copy in the history counts toward the 10 MB
/// gate (index § 5).
/// </summary>
public sealed class LoadPlanV31Activity(ICompiledPackageStore store)
{
    [Function(ConsultGenerationActivityNames.LoadPlanV31)]
    public async Task<LoadPlanV31Result> RunAsync([ActivityTrigger] LoadPlanV31Input input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (plan, refusal) = await V31Plans.LoadAsync(store, input.PackageName, input.PackageVersion, input.ResolveHash, cancellationToken);
        if (plan == null)
            return new LoadPlanV31Result(null, refusal);
        if (!string.Equals(plan.LockHash, input.LockHash, StringComparison.Ordinal))
            return new LoadPlanV31Result(null, CompiledPackageStore.LockMismatch(input.PackageName, input.PackageVersion));
        return new LoadPlanV31Result(Pack(CanonicalJson.Write(plan.Document)), null);
    }

    /// <summary><paramref name="text"/>'s UTF-8, gzipped, as base64.</summary>
    public static string Pack(string text)
    {
        using var packed = new MemoryStream();
        using (var gzip = new GZipStream(packed, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(Encoding.UTF8.GetBytes(text));
        return Convert.ToBase64String(packed.ToArray());
    }

    /// <summary>The text <see cref="Pack"/> packed.</summary>
    public static string Unpack(string packed)
    {
        using var gzip = new GZipStream(new MemoryStream(Convert.FromBase64String(packed)), CompressionMode.Decompress);
        using var text = new MemoryStream();
        gzip.CopyTo(text);
        return Encoding.UTF8.GetString(text.ToArray());
    }
}
