using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Consultologist.Api.Jobs;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1503 (part 09 § 3.6; D9): the drain's state in the engine's own storage, over an in-memory blob fake. The flag is
/// <c>drain/state.json</c> and each parked job its own <c>drain/parked/&lt;jobId&gt;.json</c>; every call reads the blobs, so
/// two instances over one store agree. A parked job is handed back by every end until it is forgotten (its orchestration
/// scheduled), oldest first.
/// </summary>
public class EngineDrainTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Begin_ThenEnd_RoundTrips()
    {
        var blobs = new MemoryDrainBlobs();
        var drain = new EngineDrain(blobs);

        Assert.False(await drain.IsDrainingAsync(CancellationToken.None));
        Assert.Empty(blobs.Documents);

        await drain.BeginAsync(CancellationToken.None);
        Assert.True(await drain.IsDrainingAsync(CancellationToken.None));
        Assert.True(await new EngineDrain(blobs).IsDrainingAsync(CancellationToken.None)); // read every call, never cached
        Assert.True(blobs.Documents.ContainsKey(EngineDrain.StateBlob));

        Assert.Empty(await drain.EndAsync(CancellationToken.None));
        Assert.False(await drain.IsDrainingAsync(CancellationToken.None));
        Assert.False(await new EngineDrain(blobs).IsDrainingAsync(CancellationToken.None));

        // a second drain begins as the first did
        await drain.BeginAsync(CancellationToken.None);
        Assert.True(await drain.IsDrainingAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Park_KeepsJobsInOrder()
    {
        var blobs = new MemoryDrainBlobs();
        var drain = new EngineDrain(blobs);
        await drain.BeginAsync(CancellationToken.None);

        var late = Parked("job-c", At.AddMinutes(3));
        var early = Parked("job-b", At.AddMinutes(1));
        var same = Parked("job-a", At.AddMinutes(1));
        foreach (var job in new[] { late, early, same })
            await drain.ParkAsync(job, CancellationToken.None);

        Assert.True(blobs.Documents.ContainsKey(EngineDrain.ParkedPrefix + "job-c.json"));
        Assert.Equal(3, await drain.ParkedCountAsync(CancellationToken.None));

        var ended = await new EngineDrain(blobs).EndAsync(CancellationToken.None);

        // oldest first; a tie is broken by the job id, ordinally
        Assert.Equal([same, early, late], ended);
        Assert.False(await drain.IsDrainingAsync(CancellationToken.None));
    }

    [Fact]
    public async Task End_Twice_ReturnsNothingTheSecondTime()
    {
        var blobs = new MemoryDrainBlobs();
        var drain = new EngineDrain(blobs);
        await drain.BeginAsync(CancellationToken.None);
        await drain.ParkAsync(Parked("job-a", At), CancellationToken.None);
        await drain.ParkAsync(Parked("job-b", At.AddSeconds(1)), CancellationToken.None);

        var first = await drain.EndAsync(CancellationToken.None);
        Assert.Equal(["job-a", "job-b"], first.Select(job => job.JobId));

        // Resume forgets each job once its orchestration is scheduled
        foreach (var job in first)
            await drain.ForgetAsync(job.JobId, CancellationToken.None);

        Assert.Empty(await drain.EndAsync(CancellationToken.None));
        Assert.Equal(0, await drain.ParkedCountAsync(CancellationToken.None));
        Assert.Equal([EngineDrain.StateBlob], blobs.Documents.Keys);
    }

    [Fact]
    public async Task AJobNotYetForgotten_IsReturnedAgain()
    {
        // a Resume that stopped before scheduling a job leaves its blob: the next Resume still finds it
        var blobs = new MemoryDrainBlobs();
        var drain = new EngineDrain(blobs);
        await drain.BeginAsync(CancellationToken.None);
        await drain.ParkAsync(Parked("job-a", At), CancellationToken.None);

        Assert.Single(await drain.EndAsync(CancellationToken.None));
        Assert.Equal("job-a", Assert.Single(await drain.EndAsync(CancellationToken.None)).JobId);
    }

    internal static ParkedJob Parked(string jobId, DateTimeOffset at) =>
        new(jobId, nameof(ConsultGenerationOrchestrator), $$"""{"JobId":"{{jobId}}"}""", at);

    /// <summary>An in-memory <see cref="IDrainBlobs"/>: each blob a JSON document, by name.</summary>
    internal sealed class MemoryDrainBlobs : IDrainBlobs
    {
        public ConcurrentDictionary<string, string> Documents { get; } = new(StringComparer.Ordinal);

        public Task<JsonObject?> ReadAsync(string name, CancellationToken ct) =>
            Task.FromResult(Documents.TryGetValue(name, out var text) ? JsonNode.Parse(text)!.AsObject() : null);

        public Task WriteAsync(string name, JsonObject document, CancellationToken ct)
        {
            Documents[name] = document.ToJsonString();
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string name, CancellationToken ct)
        {
            Documents.TryRemove(name, out _);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>(Documents.Keys.Where(name => name.StartsWith(prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToList());
    }
}
