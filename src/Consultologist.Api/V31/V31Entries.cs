using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.V31;

/// <summary>#1503 (specVersion 31, area 06): an entry of a plan by the key a call names.</summary>
internal static class V31Entries
{
    /// <summary>The entry <paramref name="entryKey"/>: in <paramref name="function"/>'s entries inside one, else the package's; null when absent.</summary>
    public static PlanEntry? Find(RunPlan plan, string entryKey, string? function)
    {
        if (function == null)
            return plan.Entries.GetValueOrDefault(entryKey);
        return plan.Functions.TryGetValue(function, out var found) ? found.Entries.GetValueOrDefault(entryKey) : null;
    }
}
