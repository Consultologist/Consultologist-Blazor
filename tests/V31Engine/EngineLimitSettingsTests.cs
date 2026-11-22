using Consultologist.Api.V31;
using Consultologist.PackageFormat.V31.Run;
using Microsoft.Extensions.Configuration;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 §§ 3.2, 3.4; R2; D8): the engine's limits are settings under
/// <c>ConsultGeneration__*</c>, each an integer from 1 upward, unset or empty meaning the spec's starting value.
/// </summary>
public class EngineLimitSettingsTests
{
    private static EngineLimitSettings With(params (string Key, string? Value)[] settings) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => $"ConsultGeneration:{s.Key}", s => s.Value))
            .Build());

    [Fact]
    public void Defaults()
    {
        foreach (var settings in new[] { With(), With(("MaxAgentCallsPerJob", ""), ("RegexMilliseconds", "")) })
        {
            Assert.Equal(new RunLimits(), settings.Limits);
            Assert.Equal(16, settings.Limits.MaxAgentCalls);
            Assert.Equal(10, settings.Limits.CapacityWaitMinutes);
            Assert.Equal(5, settings.Limits.ServiceAttempts);
            Assert.Equal(30, settings.Limits.JobMinutes);
            Assert.Equal(15, settings.Limits.DeliveryMinutes);
            Assert.Equal(3, settings.Limits.DeliveryAttempts);
            Assert.Equal(200, settings.Limits.LoopItems);
            Assert.Equal(500, settings.Limits.JobAgentCalls);
            Assert.Equal(100_000, settings.Limits.RenderSteps);
            Assert.Equal(10_000, settings.Limits.JobInstances);
            Assert.Equal(TimeSpan.FromMilliseconds(2000), settings.RegexLimit);
        }
    }

    [Fact]
    public void AnOverride_IsRead()
    {
        var settings = With(
            ("MaxAgentCallsPerJob", "4"),
            ("CapacityWaitMinutes", "11"),
            ("ServiceAttempts", "6"),
            ("JobMinutes", "31"),
            ("DeliveryMinutes", "16"),
            ("DeliveryAttempts", "1"),
            ("LoopItems", "201"),
            ("JobAgentCalls", "501"),
            ("RenderSteps", "100001"),
            ("RegexMilliseconds", "250"));

        Assert.Equal(
            new RunLimits(MaxAgentCalls: 4, CapacityWaitMinutes: 11, ServiceAttempts: 6, JobMinutes: 31, DeliveryMinutes: 16,
                DeliveryAttempts: 1, LoopItems: 201, JobAgentCalls: 501, RenderSteps: 100_001),
            settings.Limits);
        Assert.Equal(TimeSpan.FromMilliseconds(250), settings.RegexLimit);
    }

    [Fact]
    public void JobInstances_IsRead()
    {
        Assert.Equal(250, With(("JobInstances", "250")).Limits.JobInstances);
        Assert.Throws<InvalidOperationException>(() => With(("JobInstances", "0")));
    }

    [Theory]
    [InlineData("MaxAgentCallsPerJob", "0")]
    [InlineData("CapacityWaitMinutes", "-1")]
    [InlineData("ServiceAttempts", "x")]
    [InlineData("JobMinutes", "1.5")]
    [InlineData("DeliveryMinutes", "0")]
    [InlineData("DeliveryAttempts", "0")]
    [InlineData("LoopItems", "two hundred")]
    [InlineData("JobAgentCalls", "99999999999")]
    [InlineData("RenderSteps", "0")]
    [InlineData("JobInstances", "0")]
    [InlineData("RegexMilliseconds", "0")]
    [InlineData("RegexMilliseconds", "2147483647")]
    public void AValueBelowOne_OrNotANumber_Throws(string key, string value)
    {
        var thrown = Assert.Throws<InvalidOperationException>(() => With((key, value)));

        Assert.Contains($"ConsultGeneration__{key}", thrown.Message, StringComparison.Ordinal);
        Assert.Contains($"'{value}'", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRegexLimit_IsCappedAtWhatTheEvaluatorAccepts()
    {
        var settings = With(("RegexMilliseconds", "2147483646"));

        Assert.Equal(TimeSpan.FromMilliseconds(2147483646), settings.RegexLimit);
        Assert.Equal(settings.RegexLimit, new Consultologist.PackageFormat.V31.CorvusJsonLogicEvaluator(settings.RegexLimit).MatchTimeLimit);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Consultologist.PackageFormat.V31.CorvusJsonLogicEvaluator(TimeSpan.FromMilliseconds(2147483647)));
    }
}
