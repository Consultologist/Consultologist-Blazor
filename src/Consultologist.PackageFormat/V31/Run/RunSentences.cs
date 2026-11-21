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

    /// <summary>#1504 (area 07; part 05 § 2.3, D12) S05-012: a <c>for</c> of a body, at its line in the text that holds it, passed 1,000 iterations.</summary>
    public static string ForReachedLimit(string subject, int line) =>
        $"{subject} stopped rendering: the for at line {line.ToString(CultureInfo.InvariantCulture)} reached 1,000 iterations.";

    /// <summary>#1504 (area 07; part 05 § 2.3, D12) S05-014: the text a body composed passed 1 MiB (1,048,576 bytes of UTF-8).</summary>
    public static string OutputPassedLimit(string subject) => $"{subject} stopped rendering: its output passed 1 MiB.";

    /// <summary>#1504 (area 07; part 05 § 2.2, D13) A07-006: <c>array.join</c> met a list holding <c>null</c>.</summary>
    public static string JoinMetNull(string subject) => $"{subject} stopped rendering: array.join met a list with an empty element.";

    /// <summary>A06-008: a body failed to render for another reason; never the template engine's message, which can quote a value.</summary>
    public static string CouldNotRender(string subject) => $"{subject} couldn't be rendered.";
}
