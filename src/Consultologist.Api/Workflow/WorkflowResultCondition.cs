using Consultologist.Api.Models;

namespace Consultologist.Api.Workflow;

/// <summary>
/// A parsed deliverable condition (package-format-v8-design.md § 5):
/// <c>when: billable</c>, <c>when: kind == follow_up</c>,
/// <c>when: kind != procedure</c>.
///
/// <c>Literal</c> null is the bare truthy form, which only a boolean input can
/// answer. Parsed once — at publish by the validator, at load by the store —
/// so the engine evaluates a structure and the grammar lives in one place.
/// </summary>
public sealed record WorkflowResultCondition(string InputId, string? Literal, bool Negated);

/// <summary>
/// The condition grammar, deliberately closed: one declared input, equality or
/// its negation, one literal. No and/or, no arithmetic, no ordering.
///
/// A manifest is content authors fork freely and an operator cannot review line
/// by line; an evaluator is a thing with an order of operations, and this format
/// does not need one.
///
/// **Syntax only.** Whether the id is declared, whether its type may be tested,
/// and whether the literal is admissible are vocabulary closures, and those
/// belong to the validator — the same split the binding parser follows.
/// </summary>
public static class WorkflowResultConditions
{
    public const string EqualsOperator = "==";
    public const string NotEqualsOperator = "!=";

    public static bool TryParse(string? when, out WorkflowResultCondition? condition, out string? error)
    {
        condition = null;
        error = null;

        var text = when?.Trim();

        if (string.IsNullOrEmpty(text))
        {
            error = "is blank.";
            return false;
        }

        var negated = false;
        var operatorIndex = text.IndexOf(EqualsOperator, StringComparison.Ordinal);

        if (operatorIndex < 0)
        {
            operatorIndex = text.IndexOf(NotEqualsOperator, StringComparison.Ordinal);
            negated = operatorIndex >= 0;
        }

        // The bare form: an input id and nothing else.
        if (operatorIndex < 0)
        {
            if (!WorkflowDeclaredIds.IsValid(text))
            {
                error = $"'{text}' is not an input id. Write 'when: <input>' or 'when: <input> == <value>'.";
                return false;
            }

            condition = new WorkflowResultCondition(text, null, false);
            return true;
        }

        var id = text[..operatorIndex].Trim();
        var literal = text[(operatorIndex + EqualsOperator.Length)..].Trim();

        if (!WorkflowDeclaredIds.IsValid(id))
        {
            error = $"'{id}' is not an input id.";
            return false;
        }

        if (literal.Length == 0)
        {
            error = "compares against nothing; write a value after the operator.";
            return false;
        }

        // A quoted literal is text. The parser accepts the full grammar even
        // where the validator will refuse the operand type, so widening later
        // is a validator change and never a parser one.
        if (literal.Length >= 2 && literal[0] == '"' && literal[^1] == '"')
        {
            literal = literal[1..^1];
        }
        else if (literal.Contains('"'))
        {
            error = $"literal '{literal}' has a stray quote.";
            return false;
        }

        condition = new WorkflowResultCondition(id, literal, negated);
        return true;
    }

    /// <summary>
    /// Whether a deliverable fires for these supplied inputs. Pure, and
    /// evaluated once at job start — the fire set is knowable before anything
    /// runs, which is what keeps TotalBlockCount a stored scalar (#176).
    ///
    /// A null condition always fires: a deliverable without a `when` is
    /// unconditional.
    /// </summary>
    public static bool Holds(
        WorkflowResultCondition? condition,
        IReadOnlyDictionary<string, ConsultInputValue>? suppliedInputs)
    {
        if (condition is null)
        {
            return true;
        }

        // Absence is not falsity. An optional input nobody supplied has not
        // answered the question, so the condition does not hold — including
        // the negated form, which would otherwise fire on every job that left
        // the slot blank (package-format-v8-design.md § 4).
        if (suppliedInputs is null
            || !suppliedInputs.TryGetValue(condition.InputId, out var value)
            || value.IsBlank)
        {
            return false;
        }

        var matches = condition.Literal is null
            ? value.Canonical == "true"
            : string.Equals(value.Canonical, condition.Literal, StringComparison.Ordinal);

        return condition.Negated ? !matches : matches;
    }

    /// <summary>
    /// Why a deliverable did not fire, in the words a reader needs: the input,
    /// what it was, and what the condition wanted. Safe on every surface —
    /// the label and enum values are authored package content and a boolean is
    /// true or false, so none of it is free text.
    /// </summary>
    public static string Explain(
        WorkflowResultCondition condition,
        IReadOnlyDictionary<string, ConsultInputValue>? suppliedInputs)
    {
        var supplied = suppliedInputs is not null
            && suppliedInputs.TryGetValue(condition.InputId, out var value)
            && !value.IsBlank
                ? $"'{value.Canonical}'"
                : "not supplied";

        var wanted = condition.Literal is null
            ? "true"
            : $"{(condition.Negated ? "not " : string.Empty)}'{condition.Literal}'";

        return $"needs {condition.InputId} to be {wanted}; it is {supplied}";
    }
}
