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
}
