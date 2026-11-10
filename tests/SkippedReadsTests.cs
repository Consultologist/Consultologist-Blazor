using Consultologist.Api.Jobs;
using Consultologist.Api.Models;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1356 (v26 design § 1.3, v27 § 1.4): what an instance's render reads as null — the
/// flows that took no branch, and for this item the sibling inner flows it skipped.
/// </summary>
public class SkippedReadsTests
{
    private static readonly Dictionary<string, ConsultNodeDescriptor> Nodes = new(StringComparer.Ordinal)
    {
        ["problems"] = new("problems", "Problems", Kind: "for", Collection: "node.find_problems.value.problems", Body: new[] { "draft_problem", "triage" }, Ret: "draft_problem"),
        ["triage"] = new("triage", "Triage", ForEach: "node.find_problems.value.problems", Kind: "if", Body: new[] { "escalate" }, In: "problems"),
        ["draft_problem"] = new("draft_problem", "Draft problem", ForEach: "node.find_problems.value.problems", In: "problems", Reads: new[] { "triage", "find_problems" }),
        ["find_problems"] = new("find_problems", "Find problems"),
    };

    private static IReadOnlyDictionary<string, string> Item(string id) => new Dictionary<string, string> { ["id"] = id, ["name"] = $"Problems {id}" };

    private static readonly HashSet<string> None = new(StringComparer.Ordinal);

    [Fact]
    public void ASiblingSkippedForThisItem_ReadsNull()
    {
        var skipped = ConsultRenderPayload.SkippedReadsFor(Nodes["draft_problem"], Item("1"), Nodes, None, new HashSet<string>(StringComparer.Ordinal) { "triage:1" });
        Assert.Equal(new[] { "triage" }, skipped);
    }

    [Fact]
    public void ASiblingSkippedForAnotherItem_IsRead()
    {
        Assert.Null(ConsultRenderPayload.SkippedReadsFor(Nodes["draft_problem"], Item("2"), Nodes, None, new HashSet<string>(StringComparer.Ordinal) { "triage:1" }));
    }

    [Fact]
    public void TheNodeLevelSkips_AreKept_AsEver()
    {
        var skipped = ConsultRenderPayload.SkippedReadsFor(Nodes["draft_problem"], Item("1"), Nodes,
            new HashSet<string>(StringComparer.Ordinal) { "gate", "another" }, new HashSet<string>(StringComparer.Ordinal) { "triage:1" });
        Assert.Equal(new[] { "another", "gate", "triage" }, skipped);
    }

    [Fact]
    public void NothingSkipped_IsNull() =>
        Assert.Null(ConsultRenderPayload.SkippedReadsFor(Nodes["draft_problem"], Item("1"), Nodes, None, None));

    [Fact]
    public void ADecisionsSkippedReads_RideTheDurablePayload_AndAreAbsentWhenNone()
    {
        var options = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
        var run = new Consultologist.PackageFormat.TemplateRunFacts("2026-11-10", "abcdef12", "general@v1", "api.example.test");
        var profile = new Consultologist.PackageFormat.TemplateProfileFacts("Dr Example", "Dr Example");
        var none = new ConsultDecideFlowItemInput("general@v1", "followup", "node.find_problems.value.problems", Item("1"), null,
            new Dictionary<string, string>(), null, run, profile);
        var some = none with { SkippedNodes = new[] { "triage" } };

        Assert.DoesNotContain("SkippedNodes", System.Text.Json.JsonSerializer.Serialize(none, options), StringComparison.Ordinal);
        var replayed = System.Text.Json.JsonSerializer.Deserialize<ConsultDecideFlowItemInput>(System.Text.Json.JsonSerializer.Serialize(some, options), options)!;
        Assert.Equal(new[] { "triage" }, replayed.SkippedNodes);
    }
}
