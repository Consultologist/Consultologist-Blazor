using System.Reflection;
using System.Text.Json;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using NSubstitute;

namespace Consultologist.Api.Tests;

/// <summary>#1341 B2c (v30 design § 7, § 2.3): record storage 11 — a 30 job's stamp, a nested loop's instance rows, and a count-resolved per instance.</summary>
public class LoopInstanceRowsTests
{
    private static readonly PropertyInfo StateProperty =
        typeof(ConsultGenerationJobEntity).GetProperty("State", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

    private static readonly DateTimeOffset At = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private static IReadOnlyDictionary<string, string> Item(string id) => new Dictionary<string, string> { ["id"] = id, ["name"] = id };

    private static (ConsultGenerationJobEntity Entity, Func<ConsultGenerationJobState> State) Job(int specVersion)
    {
        var entity = new ConsultGenerationJobEntity(Substitute.For<IConsultGenerationJobIndexStore>(), Substitute.For<IJobOutputsBlobStore>(), Substitute.For<IJobInputsBlobStore>(), Substitute.For<IAccountUsageStore>());
        StateProperty.SetValue(entity, ConsultGenerationJobState.Create("job-1", "user-1", new[] { Item("hpi") }));
        entity.Initialize(new ConsultGenerationJobInitialize("job-1", "user-1", new[] { Item("hpi") }, PackageSpecVersion: specVersion, UnresolvedLoops: new[] { "paragraphs" })).GetAwaiter().GetResult();
        return (entity, () => (ConsultGenerationJobState)StateProperty.GetValue(entity)!);
    }

    [Theory]
    [InlineData(30, 11)]
    [InlineData(29, 10)]
    public void AJob_IsStampedByItsSpecVersion(int specVersion, int storage)
    {
        var (entity, state) = Job(specVersion);
        entity.MarkNodeCompleted(new ConsultGenerationNodeUpdate("plan", "Plan", null, "i", "o", 1, 3));
        Assert.Equal(storage, state().SchemaVersion);
    }

    [Fact]
    public async Task EachInstance_HasItsRow_AndTheLoopSettlesOnce()
    {
        var (entity, state) = Job(30);

        await entity.ResolveLoop(new ConsultGenerationLoopResolution("paragraphs", "node.outline.value.points",
            new[] { new ConsultCollectionItem("hpi/1", "Paragraphs 1"), new ConsultCollectionItem("hpi/2", "Paragraphs 2") },
            new[] { new ConsultCollectionItem("consult:paragraphs:hpi/1", "Paragraphs 1"), new ConsultCollectionItem("consult:paragraphs:hpi/2", "Paragraphs 2") },
            3, At, Path: "hpi"));
        await entity.ResolveLoop(new ConsultGenerationLoopResolution("paragraphs", "node.outline.value.points",
            Array.Empty<ConsultCollectionItem>(), Array.Empty<ConsultCollectionItem>(), 3, At, "no items: node.outline.value.points was empty", Path: "plan"));
        await entity.ResolveLoop(new ConsultGenerationLoopResolution("paragraphs", "node.outline.value.points",
            Array.Empty<ConsultCollectionItem>(), Array.Empty<ConsultCollectionItem>(), 3, At, Path: "assessment", Failure: "Paragraphs could not read its list."));

        Assert.Equal(new[] { ("hpi", (int?)2, (string?)null, (string?)null), ("plan", 0, "no items: node.outline.value.points was empty", null), ("assessment", null, null, "Paragraphs could not read its list.") },
            state().LoopInstances!.Select(r => (r.Path, r.Count, r.Reason, r.Failure)));
        Assert.Contains("paragraphs", state().UnresolvedLoops!);
        Assert.Contains(state().History, e => e.Kind == "loop-failed" && e.Label == "paragraphs (assessment): Paragraphs could not read its list.");

        await entity.ResolveLoop(new ConsultGenerationLoopResolution("paragraphs", "node.outline.value.points",
            new[] { new ConsultCollectionItem("hpi/1", "Paragraphs 1"), new ConsultCollectionItem("hpi/2", "Paragraphs 2") }, Array.Empty<ConsultCollectionItem>(), 3, At, Settled: true));
        Assert.Null(state().UnresolvedLoops is { Count: > 0 } ? state().UnresolvedLoops : null);
        Assert.Equal(3, state().LoopInstances!.Count);
    }

    [Fact]
    public async Task ASettledInstance_RaisesItsOwnCountResolved_AFailedOneNone()
    {
        var (entity, state) = Job(30);
        await entity.ResolveLoop(new ConsultGenerationLoopResolution("paragraphs", "node.outline.value.points",
            new[] { new ConsultCollectionItem("hpi/1", "Paragraphs 1") }, Array.Empty<ConsultCollectionItem>(), 1, At, Path: "hpi"));
        await entity.ResolveLoop(new ConsultGenerationLoopResolution("paragraphs", "node.outline.value.points",
            Array.Empty<ConsultCollectionItem>(), Array.Empty<ConsultCollectionItem>(), 1, At, Path: "plan", Failure: "Paragraphs could not read its list."));

        var events = ConsultGenerationJobs.CreateSemanticEventCandidates(state().ToResponse()).Where(c => c.EventType == "count-resolved").ToList();
        var single = Assert.Single(events);
        Assert.Equal("count-resolved:paragraphs:hpi", single.EventKey);
        Assert.Contains("\"Path\":\"hpi\"", single.PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public void ATopLevelLoopsEvent_AndA29Resolution_KeepTheirBytes()
    {
        Assert.Equal("""{"JobId":"j","LoopId":"problems","Count":2,"Total":3,"Pending":0}""",
            JsonSerializer.Serialize(new ConsultGenerationCountResolvedEvent("j", "problems", 2, 3, 0)));

        var resolution = JsonSerializer.Serialize(new ConsultGenerationLoopResolution("problems", "node.find_problems.value.problems",
            Array.Empty<ConsultCollectionItem>(), Array.Empty<ConsultCollectionItem>(), 3, At));
        Assert.DoesNotContain("Path", resolution, StringComparison.Ordinal);
        Assert.DoesNotContain("Settled", resolution, StringComparison.Ordinal);
        Assert.DoesNotContain("Failure", resolution, StringComparison.Ordinal);
    }
}
