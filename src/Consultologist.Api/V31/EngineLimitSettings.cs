using System.Globalization;
using Consultologist.PackageFormat.V31.Run;
using Microsoft.Extensions.Configuration;

namespace Consultologist.Api.V31;

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 §§ 3.2, 3.4; R2; D8): the engine's limits for a 31 job, each an engine setting
/// under <c>ConsultGeneration__*</c>, not part of the format. Unset or empty is the spec's starting value; a value that is not
/// an integer of at least 1 stops the host at startup (Program.cs resolves this before the host runs), naming the setting.
/// The nine of <see cref="RunLimits"/> go in the job header's <c>limits</c>; the regex time limit sits beside them and is
/// not in the record (D8).
/// </summary>
public sealed class EngineLimitSettings(IConfiguration configuration)
{
    private const string Section = "ConsultGeneration";

    /// <summary>The spec's starting values, which an unset setting keeps.</summary>
    private static readonly RunLimits Defaults = new();

    /// <summary>R2's default: 2 seconds.</summary>
    private const int DefaultRegexMilliseconds = 2000;

    /// <summary>The limits recorded in the job header and counted against by the runner.</summary>
    public RunLimits Limits { get; } = new(
        MaxAgentCalls: Read(configuration, "MaxAgentCallsPerJob", Defaults.MaxAgentCalls),
        CapacityWaitMinutes: Read(configuration, "CapacityWaitMinutes", Defaults.CapacityWaitMinutes),
        ServiceAttempts: Read(configuration, "ServiceAttempts", Defaults.ServiceAttempts),
        JobMinutes: Read(configuration, "JobMinutes", Defaults.JobMinutes),
        DeliveryMinutes: Read(configuration, "DeliveryMinutes", Defaults.DeliveryMinutes),
        DeliveryAttempts: Read(configuration, "DeliveryAttempts", Defaults.DeliveryAttempts),
        LoopItems: Read(configuration, "LoopItems", Defaults.LoopItems),
        JobAgentCalls: Read(configuration, "JobAgentCalls", Defaults.JobAgentCalls),
        RenderSteps: Read(configuration, "RenderSteps", Defaults.RenderSteps));

    /// <summary>The longest regex time-out .NET accepts, so the evaluator's: <see cref="int.MaxValue"/> - 1 milliseconds.</summary>
    private const int MaxRegexMilliseconds = int.MaxValue - 1;

    /// <summary>
    /// R2: the time one evaluation of <c>matches</c> or <c>has_word</c> may take (<c>ConsultGeneration__RegexMilliseconds</c>),
    /// at most what the evaluator accepts as a match time-out.
    /// </summary>
    public TimeSpan RegexLimit { get; } = TimeSpan.FromMilliseconds(Read(configuration, "RegexMilliseconds", DefaultRegexMilliseconds, MaxRegexMilliseconds));

    private static int Read(IConfiguration configuration, string name, int fallback, int max = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var value = configuration[$"{Section}:{name}"];
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return int.TryParse(value.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed) && parsed >= 1 && parsed <= max
            ? parsed
            : throw new InvalidOperationException(max == int.MaxValue
                ? $"{Section}__{name} is '{value}'; it is an integer from 1 upward, or unset for {fallback.ToString(CultureInfo.InvariantCulture)}."
                : $"{Section}__{name} is '{value}'; it is an integer from 1 to {max.ToString(CultureInfo.InvariantCulture)}, or unset for {fallback.ToString(CultureInfo.InvariantCulture)}.");
    }
}
