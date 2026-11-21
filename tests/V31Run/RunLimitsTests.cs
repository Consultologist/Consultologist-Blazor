using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1503 (specVersion 31, area 06; part 08 § 3; D8): a job's limits travel in its orchestration input as the header's
/// <c>limits</c> and are rebuilt from it, so a replay never reads a setting that changed since.
/// </summary>
public class RunLimitsTests
{
    [Fact]
    public void FromJson_IsToJsonsInverse()
    {
        var limits = new RunLimits(MaxAgentCalls: 2, CapacityWaitMinutes: 3, ServiceAttempts: 4, JobMinutes: 5, DeliveryMinutes: 6,
            DeliveryAttempts: 7, LoopItems: 8, JobAgentCalls: 9, RenderSteps: 10);

        Assert.Equal(limits, RunLimits.FromJson(limits.ToJson()));
        Assert.Equal(new RunLimits(), RunLimits.FromJson(new RunLimits().ToJson()));
    }

    [Theory]
    [InlineData("renderSteps", null)]
    [InlineData("maxAgentCalls", "\"16\"")]
    [InlineData("jobMinutes", "1.5")]
    public void AMemberThatIsNotAnInteger_IsTheEnginesError(string member, string? json)
    {
        var limits = new RunLimits().ToJson();
        if (json == null)
            limits.Remove(member);
        else
            limits[member] = JsonNode.Parse(json);

        var refused = Assert.Throws<ArgumentException>(() => RunLimits.FromJson(limits));
        Assert.Contains(member, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ALimitBelowOne_IsRefusedAsTheConstructorRefusesIt()
    {
        var limits = new RunLimits().ToJson();
        limits["maxAgentCalls"] = 0;

        Assert.Throws<ArgumentOutOfRangeException>(() => RunLimits.FromJson(limits));
    }
}
