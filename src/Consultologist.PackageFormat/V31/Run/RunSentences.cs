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

    /// <summary>#1506 (area 09; R2, D1) A09-001: the job opened more instances than the engine allows one job.</summary>
    public static string JobInstances(int limit) =>
        $"The job reached {limit.ToString("N0", CultureInfo.InvariantCulture)} steps, the most this engine runs for one job.";

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

    /// <summary>#1505 (area 08; part 06 § 6.6; part 08 § 10) A08-005: a deliverable composed empty under <c>whenEmpty: fail</c>.</summary>
    public static string EmptyDeliverable(string id, string label) =>
        $"Deliverable '{id}' was empty, and the workflow fails an empty {label}.";

    /// <summary>#1505 (part 06 § 6.6) S08-003: a deliverable composed empty under <c>whenEmpty: hold</c>.</summary>
    public const string HeldEmpty = "held: empty";

    /// <summary>#1505 (part 06 § 7; D6) S08-007: a deliverable's gate check returned <c>pass: false</c>.</summary>
    public static string BlockedByCheck(string check, string detail) => $"blocked: check '{check}' failed ({detail})";

    /// <summary>#1505 (part 06 § 7; D4, D6) S08-008: a deliverable's gate check failed to run; <paramref name="error"/> its sentence.</summary>
    public static string BlockedCouldNotRun(string check, string error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return $"blocked: check '{check}' could not run ({(error.EndsWith('.') ? error[..^1] : error)})";
    }

    /// <summary>#1505 (part 06 § 7) S06-006: a deliverable released because its gate check was skipped by its own <c>if</c>.</summary>
    public static string ReleasedCheckSkipped(string check) => $"released: check '{check}' didn't apply (its if was false)";

    /// <summary>
    /// #1505 (part 06 § 5; D6) S08-007's <c>&lt;detail&gt;</c>, from a <c>check_result</c> alone and never its
    /// <c>message</c>: with <c>passed</c> and <c>total</c>, <c>&lt;passed&gt; of &lt;total&gt; passed</c>; else with
    /// <c>uncovered</c>, <c>&lt;n&gt; terms missing</c> (<c>1 term missing</c>); else <c>it did not pass</c>.
    /// </summary>
    public static string CheckDetail(int? passed, int? total, int? uncovered) =>
        passed is { } p && total is { } t ? $"{p.ToString(CultureInfo.InvariantCulture)} of {t.ToString(CultureInfo.InvariantCulture)} passed"
        : uncovered is { } n ? (n == 1 ? "1 term missing" : $"{n.ToString(CultureInfo.InvariantCulture)} terms missing")
        : "it did not pass";
}
