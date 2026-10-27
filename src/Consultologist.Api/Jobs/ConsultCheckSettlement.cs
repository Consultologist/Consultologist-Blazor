using Consultologist.Api.Models;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Jobs;

/// <summary>
/// v25 (B8 #1027): the pure decisions behind a deliverable's release — when
/// its document may be composed, and what its check said. A deliverable's
/// aggregator composes as ever; the document waits until every node its
/// macros read has settled and, when a check gates it, until that check has
/// settled: below 25 computed inline by the orchestrator over the operands'
/// concepts, at 25 recorded by the computed node's own activity. Consulting
/// the settled set at release time (not only at settle time) is what frees a
/// deliverable whose check settled first — the ≤24 hang B8 closes.
/// </summary>
internal static class ConsultCheckSettlement
{
    /// <summary>A 25 computed node's outcome, as its activity recorded it; null below 25 and until it ran.</summary>
    public static ConsultCheckOutcome? RecordedOutcomeOf(ConsultNodeDescriptor node, IReadOnlyDictionary<string, NodeRunResult> outputs) =>
        string.Equals(node.Producer, WorkflowFormatProducers.Computed, StringComparison.Ordinal) && outputs.TryGetValue(node.Id, out var result)
            ? result.Check
            : null;

    /// <summary>The sentence a failed check speaks: the v12 declaration's below 25, the with's failWith at 25.</summary>
    public static string FailWithOf(ConsultNodeDescriptor node) =>
        node.Check?.FailWith
        ?? node.With?.GetValueOrDefault("failWith")
        ?? $"Check '{node.Id}' did not pass.";

    /// <summary>
    /// Whether a stashed deliverable may be released now — every read in hand,
    /// its check (if any) settled — and, when it may, the check's outcome
    /// (null when nothing gates it).
    /// </summary>
    public static bool IsReleaseReady(
        ConsultDeliverables.Deliverable deliverable,
        IReadOnlyDictionary<string, NodeRunResult> outputs,
        IReadOnlyDictionary<string, ConsultCheckOutcome> settledChecks,
        out ConsultCheckOutcome? outcome,
        // v26 (#1155): a read naming a flow node that took no branch is settled
        // with nothing to read; a read of another deliverable (its macro's
        // deliverable.<id>.content) holds until that deliverable composes, or is
        // null when it is not in the run (skipped by its if).
        IReadOnlySet<string>? skipped = null,
        IReadOnlySet<string>? composedDeliverables = null,
        IReadOnlySet<string>? runDeliverables = null)
    {
        outcome = null;

        if (deliverable.Reads?.Any(read => !outputs.ContainsKey(read) && skipped?.Contains(read) != true) == true)
        {
            return false;
        }

        if (deliverable.DeliverableReads?.Any(id => runDeliverables?.Contains(id) == true && composedDeliverables?.Contains(id) != true) == true)
        {
            return false;
        }

        if (deliverable.CheckNodeId is null)
        {
            return true;
        }

        if (!settledChecks.TryGetValue(deliverable.CheckNodeId, out var settled))
        {
            return false;
        }

        outcome = settled;
        return true;
    }
}
