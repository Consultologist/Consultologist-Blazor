namespace Consultologist.Web.Services.AI;

/// <summary>
/// v10 (#496): the words for the boundary — shared by Consults and History so
/// the two never disagree. Mirrors the Api's ConsultGenerationDecisionFailureKinds.
/// </summary>
public static class DecisionState
{
    public const string CouldNotDecide = "could-not-decide";
    public const string NothingApplied = "nothing-applied";

    /// <summary>The list badge for a job that ended in the deciding stage, or #434's, or null.</summary>
    public static string? FailedAtStartBadge(bool failedAtStart, string? kind) => !failedAtStart
        ? null
        : kind switch
        {
            CouldNotDecide => "Failed — could not decide what to produce",
            NothingApplied => "Failed — nothing applied after classification",
            _ => "Failed — nothing applied"
        };

    /// <summary>The Done row's word for a job that ended before producing anything.</summary>
    public static string DoneLabel(string? kind) => kind switch
    {
        CouldNotDecide => "Could not decide",
        NothingApplied => "Nothing applied",
        _ => "Nothing produced"
    };

    /// <summary>"scope: out_of_scope, urgency: routine" — declared values, printable.</summary>
    public static string Describe(IReadOnlyDictionary<string, string>? classifications) =>
        classifications is { Count: > 0 }
            ? string.Join(", ", classifications.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}: {pair.Value}"))
            : string.Empty;
}
