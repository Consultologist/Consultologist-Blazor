namespace Consultologist.Web.Services.Provenance.V31;

/// <summary>
/// #1509 (Task 14): the sentences History says at storage 12, word for word as <c>refusal-sentences.md</c> holds them
/// (<c>SentenceMirror31Tests</c> pins each). A sentence carries only labels, ids, keys, counts and package text: no value
/// from an input, a model or the profile. A check's detail inside a deliverable's <c>stateReason</c> is written by the engine
/// and is shown as stored.
/// </summary>
public static class HistorySentences31
{
    /// <summary>S09-019: a job whose deliverables are being sent.</summary>
    public const string Delivering = "Delivering…";

    /// <summary>One vocabulary (R6): a job's status as shown, its stored word, but <c>delivering</c> reads <see cref="Delivering"/>.</summary>
    public static string StatusText(string status) =>
        string.Equals(status, JobLiveness31.DeliveringStatus, StringComparison.Ordinal) ? Delivering : status;

    /// <summary>R1: the heading over a blocked deliverable's text.</summary>
    public const string RefusedHeading = "Refused, not delivered";

    /// <summary>A12-001: a job that is not finished long after it began has no record.</summary>
    public const string Stopped = "This job stopped before it finished, so it has no record. Run the workflow again.";

    /// <summary>A12-002 (part 08 § 11): a row's text after the retention sweep; the Create page says the same.</summary>
    public const string TextDeleted = "The text was deleted 7 days after the job finished. Its hash is kept.";

    /// <summary>A12-003: a body whose composed and stored hashes differ because a run fact is part of it.</summary>
    public const string RunFactsDiffer = "Part of this text comes from run facts, such as the date, so it differs from run to run.";

    private const string BlockedPrefix = "blocked: ";
    private const string HeldPrefix = "held: ";
    private const string FailedPrefix = "failed: ";

    /// <summary>
    /// S06-003, S06-033, S06-037: <c>&lt;label&gt; was blocked: check '&lt;id&gt;' failed (&lt;detail&gt;).</c>, from the row's
    /// <c>blocked: check '&lt;id&gt;' failed (&lt;detail&gt;)</c> (or <c>could not run (&lt;error&gt;)</c>), the detail as stored.
    /// </summary>
    public static string Blocked(string label, string? stateReason)
    {
        var reason = After(stateReason, BlockedPrefix);
        return reason.Length == 0 ? $"{label} was blocked." : $"{label} was blocked: {EndWithStop(reason)}";
    }

    /// <summary>S09-017: a destination whose retries ran out; <paramref name="destination"/> as the engine words a kind (<c>Email</c>).</summary>
    public static string CouldNotSend(string destination) => $"Couldn't send to {destination}. Copy the note instead.";

    /// <summary>Part 08's last bullet: the stored half of S09-010 to S09-012, and what History adds.</summary>
    public static string RunAgainLater(string error) => $"{error} Run the workflow again later.";

    private const string AgainLater = " Run the workflow again later.";

    /// <summary>
    /// A row's <c>stateReason</c> as shown: as stored, but a failure by the model service's limits or the job's time limit
    /// (S09-010 to S09-012) that holds only the stored half (part 08's last bullet) is followed by <see cref="RunAgainLater"/>'s
    /// words. The engine may store the whole sentence already; it is then shown once, as stored.
    /// </summary>
    public static string StateReason(string reason)
    {
        var text = reason.Trim();
        var limit = text.StartsWith("The model service ", StringComparison.Ordinal)
                    || text.StartsWith("The job didn't finish within ", StringComparison.Ordinal);
        return limit && !text.EndsWith(AgainLater, StringComparison.Ordinal) ? RunAgainLater(text) : reason;
    }

    /// <summary>
    /// A12-005: Verify could not read the record or a deliverable's text; <paramref name="what"/> names it in plain words
    /// (<c>the record file</c>, <c>the text of '&lt;label&gt;'</c>).
    /// </summary>
    public static string RecordNotChecked(string what) => $"The record couldn't be checked: {what}.";

    /// <summary>
    /// The record routes' own sentence (the Api's <c>RecordRoutes.NotAvailable</c>), said when the rows cannot be listed
    /// and the failure carries no words of the route's.
    /// </summary>
    public const string RecordNotAvailable = "The record isn't available yet. Try again.";

    /// <summary>S09-004: a job whose package the operator migrated.</summary>
    public static string MigratedFrom(string oldVersion) => $"migrated by the operator from {oldVersion}";

    /// <summary>Part 08 § 7: the keywords removed from a sent format, as JSON pointers.</summary>
    public static string NotSentToTheModel(IEnumerable<string> pointers) => $"Not sent to the model: {string.Join(", ", pointers)}";

    /// <summary>Part 08 § 7: a row whose value is computed from a model's output.</summary>
    public static string ComputedFromModelText(IEnumerable<string> reads) => $"computed from model text: {string.Join(", ", reads)}";

    /// <summary>Part 08 § 4 (ruling 4 of #1506): a unit whose text was substituted into a body not sent to the model (a macro's).</summary>
    public const string SubstitutedNotSent = "substituted, not sent";

    /// <summary>R4 (part 08 § 4.3): a macro row whose <c>classifier</c> is <c>null</c>, its <c>if</c> reading no one node.</summary>
    public const string NoSingleClassifier = "no single classifier";

    /// <summary>A held deliverable: <c>&lt;label&gt; was held: empty</c>, the words after <c>held: </c> as stored.</summary>
    internal static string Held(string label, string? stateReason)
    {
        var reason = After(stateReason, HeldPrefix);
        return reason.Length == 0 ? $"{label} was held." : $"{label} was held: {reason}";
    }

    /// <summary>A failed deliverable: <c>&lt;label&gt; failed: &lt;reason&gt;</c>, the reason as stored, without its own <c>failed: </c>.</summary>
    internal static string Failed(string label, string? stateReason)
    {
        var reason = After(stateReason, FailedPrefix);
        return reason.Length == 0 ? $"{label} failed." : $"{label} failed: {reason}";
    }

    /// <summary>A destination not sent: <c>&lt;label&gt; was &lt;reason&gt;</c>, the reason (<c>not sent: …</c>) as stored.</summary>
    internal static string NotSent(string label, string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? $"{label} was not sent." : $"{label} was {reason.Trim()}";

    private static string After(string? reason, string prefix)
    {
        var text = (reason ?? "").Trim();
        return text.StartsWith(prefix, StringComparison.Ordinal) ? text[prefix.Length..].Trim() : text;
    }

    private static string EndWithStop(string text) => text.EndsWith('.') ? text : text + ".";
}
