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
