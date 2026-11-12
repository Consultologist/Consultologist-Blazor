using Consultologist.Api.Jobs;
using Consultologist.Api.Tests.Engine;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1366: two top-level loops whose items share ids (both lists read off nodes, numbered from 1)
/// — at record storage 11 each item's progress is its loop's, kept apart by the collection, and
/// each loop's item-step events name their own item. Below 11 a record keys by the item id, as
/// those records always did.
/// </summary>
public class EngineRunItemProgressTests
{
    [Fact]
    public async Task TwoLoopsSharingItemIds_KeepTheirProgressApart_AndNameTheirOwnItems()
    {
        var run = await EngineRun.StartAsync(EngineRunNestedTests.Load(EngineRunNestedTests.TwoLevels()), EngineRunNestedTests.Notes, EngineRunNestedTests.Outputs);
        var response = run.State.ToResponse();

        Assert.Equal(11, response.SchemaVersion);
        var progress = response.ItemProgress!;
        Assert.Equal("Problems 1", progress["node.find_problems.value.problems|1"].ItemName);
        Assert.Equal("Parts 1", progress["node.plan.value.sections|1"].ItemName);
        Assert.DoesNotContain("1", progress.Keys);

        var steps = ConsultGenerationJobs.CreateSemanticEventCandidates(response)
            .Where(c => c.EventKey.StartsWith("item-step:1:", StringComparison.Ordinal))
            .ToDictionary(c => c.EventKey, c => System.Text.Json.JsonDocument.Parse(c.PayloadJson).RootElement.GetProperty("ItemName").GetString());
        Assert.Equal("Parts 1", steps["item-step:1:outline"]);
        Assert.Equal("Problems 1", steps["item-step:1:draft_problem"]);
    }
}
