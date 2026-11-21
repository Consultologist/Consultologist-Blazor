using System.Globalization;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.V31;

/// <summary>
/// #1503 (specVersion 31, area 06; part 03 § 4.3; part 09 § 7.2; D6): the run facts a 31 job's plan reads that follow from
/// the plan and the job's start, as <see cref="RunInputs.RunFacts"/> holds them (by name, <c>run.</c> dropped):
/// <c>run.date</c>, the ISO date of <c>startedAtUtc</c> in the account's IANA time zone (UTC with no zone or one this
/// machine does not know; the job's start refuses a job without a zone before it gets here), and <c>run.package</c>,
/// <c>&lt;name&gt;@&lt;version&gt;</c>. A fact the plan does not read is not given; <c>run.id</c> and <c>run.engine</c> are
/// the job's and the engine's, which the job's start supplies.
/// </summary>
public static class V31RunFacts
{
    public static JsonObject Of(RunPlan plan, DateTimeOffset startedAtUtc, string? timeZone)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var facts = new JsonObject();
        foreach (var fact in plan.RunFacts)
        {
            switch (fact)
            {
                case "run.date":
                    facts["date"] = DateIn(startedAtUtc, timeZone);
                    break;
                case "run.package":
                    facts["package"] = $"{plan.Package}@{plan.Version}";
                    break;
            }
        }

        return facts;
    }

    /// <summary>The ISO date (<c>yyyy-MM-dd</c>) of <paramref name="at"/> in <paramref name="timeZone"/>, or in UTC.</summary>
    public static string DateIn(DateTimeOffset at, string? timeZone)
    {
        var local = timeZone is { Length: > 0 } && TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var zone)
            ? TimeZoneInfo.ConvertTime(at, zone)
            : at.ToUniversalTime();
        return local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
