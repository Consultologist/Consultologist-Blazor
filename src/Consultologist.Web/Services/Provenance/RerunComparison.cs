using Consultologist.Web.Services.AI;

namespace Consultologist.Web.Services.Provenance;

/// <summary>
/// #549: the per-stage table a rerun's History shows against its source. The
/// records alone carry everything compared — per-node inputHash/outputHash
/// with their hashVersion, per-deliverable documentHash, and the
/// effective-input hash the rerun must reproduce by construction. Honest
/// guards over false verdicts: an outputHash is compared only when the
/// inputHash AND hashVersion match (hash-definitions § 4 — across records a
/// per-node hash supports no other conclusion); otherwise the row says which
/// precondition failed instead of "different".
/// </summary>
public static class RerunComparison
{
    /// <summary>The origin kind the server stamps on every effective slot of a rerun.</summary>
    public const string OriginKind = "rerun";

    public const string Same = "same";
    public const string Different = "different";
    public const string InputsDiffer = "inputs differ";
    public const string HashVersionDiffers = "hash version differs";
    public const string NotOnSource = "not on the source run";

    /// <summary>
    /// The run this record replays — the job-level field since #582, the slot
    /// origins for #549-era reruns; null for an ordinary run.
    /// </summary>
    public static string? SourceJobIdOf(ConsultGenerationJobResponse detail) =>
        detail.RerunOf
        ?? detail.InputOrigins?.Values
            .SelectMany(origins => origins)
            .FirstOrDefault(origin => origin.Kind == OriginKind)
            ?.SourceJobId;

    /// <summary>
    /// #582: the stamped verdict as a sentence, or null when the record has
    /// none (an ordinary run, a #549-era rerun, a rerun that did not
    /// complete) — no line is shown rather than a guessed one.
    /// </summary>
    public static string? DescribeVerdict(string? verdict, string? divergence) => verdict switch
    {
        "pass" => "Verdict: pass — every reproducible stage matched the source",
        "fail" when divergence == "effective-inputs" =>
            "Verdict: fail — the effective inputs differ from the source; this is a bug",
        "fail" => $"Verdict: fail — first divergence at {divergence}",
        "no-reproducible-stages" =>
            "Verdict: no reproducible stages to hold — the package claims none, or none were comparable",
        _ => null
    };

    /// <summary>One comparison row: a stage (node or fanned item) or a deliverable.
    /// Reproducible marks the stages the verdict counts (#550's claim).</summary>
    public sealed record Row(
        string Key,
        string Label,
        bool IsItem,
        string? SourceHash,
        string? RerunHash,
        string Verdict,
        bool Reproducible = false);

    /// <summary>
    /// Equal by construction — the rerun resubmitted the source's own
    /// supplied values under the same definition. False is a bug, and the
    /// panel says so loudly rather than tabulating around it.
    /// </summary>
    public static bool EffectiveInputsAgree(ConsultGenerationJobResponse rerun, ConsultGenerationJobResponse source) =>
        rerun.EffectiveInputHash != null
        && string.Equals(rerun.EffectiveInputHash, source.EffectiveInputHash, StringComparison.Ordinal)
        && rerun.EffectiveInputHashVersion == source.EffectiveInputHashVersion;

    /// <summary>
    /// The stage rows, in the rerun's manifest order: node-level entries and
    /// fanned items, each against the source's same-keyed entry.
    /// </summary>
    public static IReadOnlyList<Row> Stages(ConsultGenerationJobResponse rerun, ConsultGenerationJobResponse source)
    {
        if (rerun.NodeOutputs is not { Count: > 0 } outputs)
        {
            return Array.Empty<Row>();
        }

        var rows = new List<Row>();

        void Add(string key, string label, bool isItem, ConsultGenerationNodeStatus entry, bool reproducible)
        {
            source.NodeOutputs!.TryGetValue(key, out var sourceEntry);
            rows.Add(new Row(key, label, isItem, sourceEntry?.OutputHash, entry.OutputHash, Verdict(sourceEntry, entry), reproducible));
        }

        if (source.NodeOutputs is not { Count: > 0 })
        {
            return outputs
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new Row(pair.Key, pair.Value.Label, pair.Key.Contains(':'), null, pair.Value.OutputHash, NotOnSource))
                .ToList();
        }

        // Manifest order when the record names its nodes; key order otherwise
        // (a record shape from before the descriptors).
        if (rerun.Nodes is { Count: > 0 } nodes)
        {
            foreach (var descriptor in nodes)
            {
                var reproducible = descriptor.Reproducible == true;

                if (outputs.TryGetValue(descriptor.Id, out var nodeLevel))
                {
                    Add(descriptor.Id, descriptor.Label, isItem: false, nodeLevel, reproducible);
                }

                foreach (var (key, entry) in outputs
                    .Where(pair => pair.Key.StartsWith(descriptor.Id + ":", StringComparison.Ordinal))
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    Add(key, key[(descriptor.Id.Length + 1)..], isItem: true, entry, reproducible);
                }
            }
        }
        else
        {
            foreach (var (key, entry) in outputs.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                Add(key, entry.Label, key.Contains(':'), entry, reproducible: false);
            }
        }

        return rows;
    }

    /// <summary>The deliverable rows, by result id, over documentHash.</summary>
    public static IReadOnlyList<Row> Deliverables(ConsultGenerationJobResponse rerun, ConsultGenerationJobResponse source)
    {
        if (rerun.AssembledDocuments is not { Count: > 0 } documents)
        {
            return Array.Empty<Row>();
        }

        return documents
            .Select(document =>
            {
                var sourceDocument = source.AssembledDocuments?.FirstOrDefault(d => d.ResultId == document.ResultId);
                var verdict = sourceDocument?.DocumentHash == null || document.DocumentHash == null
                    ? NotOnSource
                    : string.Equals(sourceDocument.DocumentHash, document.DocumentHash, StringComparison.Ordinal)
                        ? Same
                        : Different;
                return new Row(document.ResultId, document.Label, IsItem: false, sourceDocument?.DocumentHash, document.DocumentHash, verdict);
            })
            .ToList();
    }

    private static string Verdict(ConsultGenerationNodeStatus? sourceEntry, ConsultGenerationNodeStatus rerunEntry)
    {
        if (sourceEntry?.OutputHash == null || rerunEntry.OutputHash == null)
        {
            return NotOnSource;
        }

        if (sourceEntry.HashVersion != rerunEntry.HashVersion)
        {
            return HashVersionDiffers;
        }

        if (!string.Equals(sourceEntry.InputHash, rerunEntry.InputHash, StringComparison.Ordinal))
        {
            return InputsDiffer;
        }

        return string.Equals(sourceEntry.OutputHash, rerunEntry.OutputHash, StringComparison.Ordinal)
            ? Same
            : Different;
    }
}
