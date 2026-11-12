using Consultologist.PackageFormat;

namespace Consultologist.Web.Services.AI;

/// <summary>
/// v29 (#1303, package-format-v29-design.md § 1.3–§ 1.4): the words for a loop
/// over a node's value whose count is not yet known — shared by Create, History
/// and the run diagram so they never disagree. While any such loop is pending
/// the job's total is a floor, never final; a job that ended with one pending
/// says the count was never known, never zero.
/// </summary>
public static class LoopCount
{
    /// <summary>The stream's event for a loop that settled (mirrors the Api's ConsultGenerationCountResolvedEvent).</summary>
    public const string EventName = "count-resolved";

    public const string Counting = "counting…";

    public const string NeverKnown = "count never known";

    /// <summary>True for a fan or collection that is a node's value (<c>node.&lt;id&gt;.value…</c>, or <c>snippet:</c>-free as a forEach spells it).</summary>
    public static bool IsNodeValue(string? fanOrCollection) =>
        TemplateObjectReference.TryParse(fanOrCollection, out var obj, out _, out _) && obj == TemplateObjects.Node;

    /// <summary>A loop row whose count is not yet known: a loop over a node's value that has not settled.</summary>
    public static bool IsUnknown(ConsultGenerationNodeDescriptor row) =>
        row.Kind == "for" && row.Count is null && IsNodeValue(row.Collection);

    /// <summary>"counting…" while the job runs, "count never known" once it ended.</summary>
    public static string Describe(bool finished) => finished ? NeverKnown : Counting;

    /// <summary>"5", or "5+" while a loop's count is still pending — a floor, never final.</summary>
    public static string Total(int total, bool pending) => pending ? $"{total}+" : total.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>What a count-resolved event does to the page's count: the total it carries, the loop no longer pending.</summary>
    public static (int Total, IReadOnlySet<string> Pending) Apply(IReadOnlySet<string> pending, string loopId, int total)
    {
        var next = new HashSet<string>(pending, StringComparer.Ordinal);
        next.Remove(loopId);
        return (total, next);
    }

    /// <summary>
    /// v30 (#1345): what a count-resolved event does when it names an instance (a loop inside a
    /// loop or a while, by its outer path): the total re-totals, but the loop stays pending —
    /// another instance may yet open; the snapshot's unresolved loops say when it is done. A
    /// top-level event (no path) is <see cref="Apply(IReadOnlySet{string}, string, int)"/>, as at 29.
    /// </summary>
    public static (int Total, IReadOnlySet<string> Pending) Apply(IReadOnlySet<string> pending, string loopId, int total, string? path) =>
        path is null ? Apply(pending, loopId, total) : (total, pending);

    /// <summary>v30 (#1345): where a settled count is kept — the loop, or the loop's instance at a path (loop:path).</summary>
    public static string SettledKey(string loopId, string? path) =>
        path is null ? loopId : WorkflowItemPath.Key(loopId, path);
}
