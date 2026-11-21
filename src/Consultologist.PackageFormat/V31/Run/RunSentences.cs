using System.Globalization;

namespace Consultologist.PackageFormat.V31.Run;

/// <summary>
/// #1503 (specVersion 31, area 06): the run's sentences about a body that the runner and the engine's composer both say, built
/// in one place.
/// </summary>
public static class RunSentences
{
    /// <summary>
    /// Part 09 § 9's subject of an entry: <c>Node 'id'</c>, <c>Check 'id'</c> or <c>Deliverable 'id'</c>, with
    /// <c> in function 'f'</c> inside <paramref name="function"/>.
    /// </summary>
    public static string Subject(PlanEntry entry, string? function)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var word = entry.Kind == PlanKind.Deliverable ? "Deliverable"
            : entry.Key.StartsWith("check.", StringComparison.Ordinal) ? "Check"
            : "Node";
        return function == null ? $"{word} '{entry.Id}'" : $"{word} '{entry.Id}' in function '{function}'";
    }

    /// <summary>S05-062: a body passed its step budget.</summary>
    public static string StoppedRendering(string subject, int limit) =>
        $"{subject} stopped rendering: it took more than {limit.ToString("N0", CultureInfo.InvariantCulture)} steps.";

    /// <summary>A06-008: a body failed to render for another reason; never the template engine's message, which can quote a value.</summary>
    public static string CouldNotRender(string subject) => $"{subject} couldn't be rendered.";
}
