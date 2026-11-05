using Microsoft.Extensions.Configuration;

namespace Consultologist.Api.Jobs;

/// <summary>
/// #1302 (v29 design § 7): the switch that drains the engine before a release.
/// While <c>Jobs__HoldNewJobs</c> is true every door refuses a NEW job by name;
/// jobs already running — and scheduled jobs asleep on their timer — carry on.
/// </summary>
public interface IJobIntakeHold
{
    bool IsHeld { get; }
}

public sealed class JobIntakeHold : IJobIntakeHold
{
    /// <summary>The one sentence every door says. No PHI, so the email door may send it.</summary>
    public const string Sentence = "New consults are paused for an update; try again in a few minutes.";

    /// <summary>What the HTTP door sends as Retry-After.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);

    private readonly IConfiguration _configuration;

    public JobIntakeHold(IConfiguration configuration) => _configuration = configuration;

    // Read on every call, never cached: the switch is flipped on a live app.
    public bool IsHeld => _configuration.GetValue("Jobs:HoldNewJobs", false);
}
