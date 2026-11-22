using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Run;

/// <summary>
/// #1505 (specVersion 31, area 08; R6; part 09 § 3.1): a rule that holds <c>matches</c> or <c>has_word</c>, evaluated for
/// the host (the engine's activity, the tests' hosts), and its outcome as the runner reads it back. One definition, so what
/// the activity stores is what an inline evaluation would have given at that moment.
/// </summary>
public static class RuleTurn
{
    /// <summary><see cref="RuleOutcome.FailureKind"/> when the plan is not the job's, or lacks the entry or its rule (S07-006).</summary>
    public const string PlanFailure = "plan";

    /// <summary>
    /// <paramref name="call"/>'s rule in <paramref name="plan"/>, evaluated over its scope: <see cref="IJsonLogicRule.Decide"/>
    /// for a condition, <see cref="IJsonLogicRule.Evaluate"/> for an <c>expression</c>. An entry or a rule the plan lacks is
    /// <see cref="PlanFailure"/>.
    /// </summary>
    public static RuleOutcome Evaluate(RunPlan plan, RuleCall call)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(call);

        var entries = call.Function == null ? plan.Entries : plan.Functions.GetValueOrDefault(call.Function)?.Entries;
        if (entries?.GetValueOrDefault(call.EntryKey) is not { } entry || Rule(entry, call.Member) is not { } rule)
            return new RuleOutcome(null, PlanFailure, null);

        if (call.Member == "expression")
        {
            var value = rule.Evaluate(call.Scope);
            return value.Failure is { } failed ? Failed(failed) : new RuleOutcome(value.Value?.DeepClone(), null, null);
        }

        var decision = rule.Decide(call.Scope);
        return decision.Failure is { } failure ? Failed(failure) : new RuleOutcome(JsonValue.Create(decision.Result), null, null);
    }

    /// <summary>The outcome of an <c>expression</c>, as <see cref="IJsonLogicRule.Evaluate"/> gives it.</summary>
    internal static JsonLogicValue ValueOf(RuleOutcome outcome) =>
        Failure(outcome) is { } failed ? new JsonLogicValue(null, failed) : new JsonLogicValue(outcome.Value?.DeepClone(), null);

    /// <summary>The outcome of a condition, as <see cref="IJsonLogicRule.Decide"/> gives it.</summary>
    internal static JsonLogicDecision DecisionOf(RuleOutcome outcome) =>
        Failure(outcome) is { } failed
            ? new JsonLogicDecision(false, failed)
            : new JsonLogicDecision(outcome.Value is JsonValue v && v.GetValueKind() == JsonValueKind.True, null);

    /// <summary>The failure an outcome that is <see cref="PlanFailure"/>'s (or of a kind this library does not name) reads as: compared by reference.</summary>
    internal static readonly JsonLogicFailure NotTheJobsPlan = new(JsonLogicFailureKind.Fault, "the plan is not the job's");

    private static JsonLogicFailure? Failure(RuleOutcome outcome)
    {
        if (outcome.FailureKind is not { } kind)
            return null;
        if (!Enum.GetNames<JsonLogicFailureKind>().Contains(kind, StringComparer.Ordinal))
            return NotTheJobsPlan;
        return new JsonLogicFailure(Enum.Parse<JsonLogicFailureKind>(kind), outcome.FailureDetail ?? "")
        {
            Limit = outcome.FailureLimitSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
        };
    }

    private static RuleOutcome Failed(JsonLogicFailure failure) =>
        new(null, failure.Kind.ToString(), failure.Limit?.TotalSeconds, failure.Detail);

    /// <summary>The rule <paramref name="member"/> names on <paramref name="entry"/>: one of its own, or a placement's <c>if</c> by site.</summary>
    private static IJsonLogicRule? Rule(PlanEntry entry, string member) => member switch
    {
        "if" => entry.If,
        "expression" => entry.Expression,
        "while" => entry.While,
        "until" => entry.Until,
        _ => entry.Placements.FirstOrDefault(p => string.Equals(p.Site, member, StringComparison.Ordinal))?.If,
    };
}
