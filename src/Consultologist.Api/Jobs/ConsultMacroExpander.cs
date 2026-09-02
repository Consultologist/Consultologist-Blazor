using System.Globalization;
using Consultologist.Api.Models;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Jobs;

/// <summary>
/// v11 #513 (package-format-v11-design.md § 4): expands a deliverable's macros
/// and appends them to the assembled document — after
/// ConsultAggregateRenderer.Render returns, verbatim, blank-line separated, no
/// invented heading, in results[].macros order. Substitution only: no model,
/// no Scriban, no recursion — deterministic by construction. The validator
/// guaranteed at publish that every placeholder resolves (the one grammar,
/// WorkflowMacroPlaceholders); a token that fails here is a broken snapshot
/// and fails the job loud rather than producing a silently wrong document.
/// </summary>
internal static class ConsultMacroExpander
{
    /// <summary>
    /// The run: and profile: facts (v11 § 4). Date is the replay-safe
    /// orchestration clock; JobId travels whole and renders as its first 8;
    /// ApiHost and ProfileName render empty when absent — data absence on the
    /// deployment or account, not grammar failure.
    /// </summary>
    internal sealed record RunFacts(
        DateTime UtcNow,
        string JobId,
        string PackageRef,
        string? ApiHost,
        string? ProfileName);

    /// <summary>
    /// The § 4 append rule. Returns the rendered text untouched with no
    /// entries when the deliverable names no macros — the control: a package
    /// using none writes the bytes it always wrote.
    /// </summary>
    public static (string Text, IReadOnlyList<ConsultAppendedEntry>? Appended) Append(
        string rendered,
        IReadOnlyList<string>? macroIds,
        IReadOnlyDictionary<string, string>? macroTexts,
        IReadOnlyDictionary<string, string> inputs,
        IReadOnlyDictionary<string, string>? dataScalars,
        IReadOnlyDictionary<string, string> classifications,
        RunFacts facts)
    {
        if (macroIds is not { Count: > 0 })
        {
            return (rendered, null);
        }

        var pieces = new List<string>(1 + macroIds.Count) { rendered };
        var appended = new List<ConsultAppendedEntry>(macroIds.Count);

        foreach (var macroId in macroIds)
        {
            if (macroTexts is null || !macroTexts.TryGetValue(macroId, out var template))
            {
                throw new InvalidOperationException($"Macro '{macroId}' has no snapshotted template.");
            }

            pieces.Add(Expand(template, inputs, dataScalars, classifications, facts));
            appended.Add(new ConsultAppendedEntry(ConsultAppendedKinds.Macro, macroId));
        }

        return (string.Join("\n\n", pieces), appended);
    }

    /// <summary>
    /// v12 #619 (design § 4): the placement-aware composition — per aggregate
    /// source, in order: macros placed before it (in results[].macros order),
    /// the source's rendered bytes, macros placed after it; then every
    /// unplaced macro, in order; blank-line separated throughout. The
    /// appended entries emit in DOCUMENT order (§ 6). With nothing placed the
    /// bytes equal Append(Render(parts), …)'s exactly — the join is
    /// associative, and the parity is pinned. The caller hashes
    /// Render(parts) separately, before this runs: composition never touches
    /// the aggregator's outputHash or its downstream binds (§ 7).
    /// </summary>
    public static (string Text, IReadOnlyList<ConsultAppendedEntry>? Appended) Compose(
        IReadOnlyList<string> sourceRefs,
        IReadOnlyList<ConsultAggregateRenderer.Part> parts,
        IReadOnlyList<string>? macroIds,
        IReadOnlyList<ConsultMacroPlacement>? placements,
        IReadOnlyDictionary<string, string>? macroTexts,
        IReadOnlyDictionary<string, string> inputs,
        IReadOnlyDictionary<string, string>? dataScalars,
        IReadOnlyDictionary<string, string> classifications,
        RunFacts facts)
    {
        if (sourceRefs.Count != parts.Count)
        {
            // The engine's success path builds one part per aggregate source;
            // a mismatch here is a broken invariant upstream, and placing a
            // macro against the wrong section is worse than failing the job.
            throw new InvalidOperationException(
                $"Aggregate composition received {parts.Count} parts for {sourceRefs.Count} sources.");
        }

        if (macroIds is not { Count: > 0 })
        {
            return (ConsultAggregateRenderer.Render(parts), null);
        }

        // A placement whose macro is not in the id list places nothing — the
        // filters keep the two in lockstep, and this is the belt to that
        // suspender.
        var active = (placements ?? Array.Empty<ConsultMacroPlacement>())
            .Where(placement => macroIds.Contains(placement.Id, StringComparer.Ordinal))
            .ToList();
        var placedIds = active.Select(placement => placement.Id).ToHashSet(StringComparer.Ordinal);

        string ExpandId(string macroId)
        {
            if (macroTexts is null || !macroTexts.TryGetValue(macroId, out var template))
            {
                throw new InvalidOperationException($"Macro '{macroId}' has no snapshotted template.");
            }

            return Expand(template, inputs, dataScalars, classifications, facts);
        }

        var pieces = new List<string>(parts.Count + macroIds.Count);
        var appended = new List<ConsultAppendedEntry>(macroIds.Count);

        void Emit(ConsultMacroPlacement placement)
        {
            pieces.Add(ExpandId(placement.Id));
            appended.Add(new ConsultAppendedEntry(ConsultAppendedKinds.Macro, placement.Id));
        }

        for (var i = 0; i < parts.Count; i++)
        {
            var sourceRef = sourceRefs[i];

            foreach (var placement in active.Where(p => string.Equals(p.Before, sourceRef, StringComparison.Ordinal)))
            {
                Emit(placement);
            }

            pieces.Add(ConsultAggregateRenderer.RenderPart(parts[i]));

            foreach (var placement in active.Where(p => string.Equals(p.After, sourceRef, StringComparison.Ordinal)))
            {
                Emit(placement);
            }
        }

        foreach (var macroId in macroIds.Where(id => !placedIds.Contains(id)))
        {
            pieces.Add(ExpandId(macroId));
            appended.Add(new ConsultAppendedEntry(ConsultAppendedKinds.Macro, macroId));
        }

        return (string.Join("\n\n", pieces), appended.Count > 0 ? appended : null);
    }

    /// <summary>One template, expanded — substitution over the closed namespaces.</summary>
    public static string Expand(
        string template,
        IReadOnlyDictionary<string, string> inputs,
        IReadOnlyDictionary<string, string>? dataScalars,
        IReadOnlyDictionary<string, string> classifications,
        RunFacts facts)
    {
        return WorkflowMacroPlaceholders.Pattern.Replace(template, match =>
        {
            var token = WorkflowMacroPlaceholders.TokenOf(match);

            if (WorkflowMacroPlaceholders.TryParse(token, out var ns, out var id))
            {
                switch (ns)
                {
                    case "input":
                        // The effective map carries every declared id; an
                        // absent optional input is already the empty string.
                        if (inputs.TryGetValue(id, out var inputValue))
                        {
                            return inputValue;
                        }

                        break;
                    case "data":
                        if (dataScalars != null && dataScalars.TryGetValue(id, out var dataValue))
                        {
                            return dataValue;
                        }

                        break;
                    case "classification":
                        if (classifications.TryGetValue(id, out var answer))
                        {
                            return answer;
                        }

                        break;
                    case "run":
                        switch (id)
                        {
                            case "date":
                                return facts.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                            case "job":
                                return facts.JobId.Length <= 8 ? facts.JobId : facts.JobId[..8];
                            case "package":
                                return facts.PackageRef;
                            case "host":
                                return facts.ApiHost ?? string.Empty;
                        }

                        break;
                    case "profile":
                        if (id == "name")
                        {
                            return facts.ProfileName ?? string.Empty;
                        }

                        break;
                }
            }

            throw new InvalidOperationException($"Macro placeholder '{{{{{token}}}}}' does not resolve.");
        });
    }
}
