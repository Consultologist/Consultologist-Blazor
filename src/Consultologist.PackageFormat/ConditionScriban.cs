using System.Globalization;
using System.Text.RegularExpressions;

namespace Consultologist.PackageFormat;

/// <summary>
/// C6 (#1012, design § 14): the ≤24 condition grammar spelled as a Scriban
/// condition, for the 24→25 migration's <c>{{ if … }}</c> around a prompt-side
/// macro read. The grammar itself is unchanged at 25 for <c>when</c>; only the
/// migrated template needs this spelling. <c>node:x</c> reads <c>node.x.value</c>
/// (an enum format's chosen value); a bare input reads <c>input.x</c> and a path
/// <c>input.x.y</c>; <c>count(x)</c> is <c>(input.x | array.size)</c>;
/// <c>item:id</c> is <c>item.id</c>. Literals: a number bare, <c>true</c>/<c>false</c>
/// bare, a date <c>date.parse "YYYY-MM-DD"</c>, anything else a quoted string —
/// unless <paramref name="inputTypeOf"/> says the input is text or enum, when the
/// literal is always a string.
/// </summary>
public static class ConditionScriban
{
    private static readonly Regex DatePattern = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);

    public static string Spell(WorkflowConditionExpression expression, Func<string, string?>? inputTypeOf = null) => expression switch
    {
        WorkflowClauseExpression clause => SpellClause(clause.Clause, inputTypeOf),
        WorkflowNotExpression not => $"!({Spell(not.Inner, inputTypeOf)})",
        WorkflowAndExpression and => $"({Spell(and.Left, inputTypeOf)} && {Spell(and.Right, inputTypeOf)})",
        WorkflowOrExpression or => $"({Spell(or.Left, inputTypeOf)} || {Spell(or.Right, inputTypeOf)})",
        _ => throw new ArgumentOutOfRangeException(nameof(expression), expression.GetType().Name, "An unknown condition expression.")
    };

    private static string SpellClause(WorkflowResultCondition clause, Func<string, string?>? inputTypeOf)
    {
        if (clause.IsArithmetic)
        {
            var left = clause.Left is { } l ? SpellTerm(l, inputTypeOf) : SpellOperand(clause);
            var right = clause.Right is { } r ? SpellTerm(r, inputTypeOf) : SpellLiteral(clause, inputTypeOf);
            return $"{left} {clause.Operator ?? WorkflowResultConditions.EqualsOperator} {right}";
        }

        var operand = SpellOperand(clause);

        if (clause.IsBare)
        {
            return clause.Negated ? $"!({operand})" : operand;
        }

        return $"{operand} {clause.Operator} {SpellLiteral(clause, inputTypeOf)}";
    }

    private static string SpellTerm(WorkflowConditionTerm term, Func<string, string?>? inputTypeOf) => term switch
    {
        WorkflowOperandTerm operand => SpellOperand(operand.Operand),
        WorkflowLiteralTerm literal => SpellLiteralText(literal.Literal, forceString: false),
        WorkflowBinaryTerm binary => $"({SpellTerm(binary.Left, inputTypeOf)} {binary.Op} {SpellTerm(binary.Right, inputTypeOf)})",
        WorkflowNegateTerm negate => $"-({SpellTerm(negate.Inner, inputTypeOf)})",
        _ => throw new ArgumentOutOfRangeException(nameof(term), term.GetType().Name, "An unknown condition term.")
    };

    /// <summary>The operand as a Scriban object read.</summary>
    public static string SpellOperand(WorkflowResultCondition clause)
    {
        if (clause.NodeId is { } nodeId)
        {
            return $"{TemplateObjects.Node}.{nodeId}.{TemplateMembers.Value}";
        }

        if (clause.ItemField is { } itemField)
        {
            return $"{TemplateObjects.Item}.{itemField}";
        }

        var path = $"{TemplateObjects.Input}.{clause.PathText}";
        return clause.IsCount ? $"({path} | array.size)" : path;
    }

    private static string SpellLiteral(WorkflowResultCondition clause, Func<string, string?>? inputTypeOf)
    {
        var literal = clause.Literal ?? string.Empty;

        // A node's value and an item's id are closed string sets; a text or enum
        // input compares as a string however the literal looks.
        var forceString = clause.NodeId is not null
            || clause.ItemField is not null
            || (clause.PathDepth == 0 && !clause.IsCount && inputTypeOf?.Invoke(clause.InputId) is WorkflowInputTypes.Text or WorkflowInputTypes.Enum);

        return SpellLiteralText(literal, forceString);
    }

    private static string SpellLiteralText(string literal, bool forceString)
    {
        if (!forceString)
        {
            if (literal is "true" or "false")
            {
                return literal;
            }

            if (decimal.TryParse(literal, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            {
                return literal;
            }

            if (DatePattern.IsMatch(literal))
            {
                return $"date.parse \"{literal}\"";
            }
        }

        return $"\"{literal.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
    }
}
