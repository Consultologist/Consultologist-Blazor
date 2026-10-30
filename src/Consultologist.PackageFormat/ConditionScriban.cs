using System.Globalization;
using System.Text.RegularExpressions;
using Scriban.Syntax;

namespace Consultologist.PackageFormat;

/// <summary>
/// C6 (#1012, design § 14): the ≤24 condition grammar spelled as a Scriban
/// condition — the 24→25 migration's <c>{{ if … }}</c> around a prompt-side
/// macro read and, since #1037 (design § 16), every <c>when</c> at 25. The
/// inverse, <see cref="TryUnspell"/>, reads the subset this writes back into
/// the ≤24 tree, so the ≤24 evaluator, the diagram and the editor's builder
/// serve both spellings. <c>node:x</c> reads <c>node.x.value</c>
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

    // ----- #1037: the inverse -------------------------------------------------

    /// <summary>
    /// Whether a condition text is in the object spelling — a reserved object
    /// read (<c>input.</c>, <c>node.</c>, <c>item.</c>), a Scriban connective or a
    /// quoted literal — rather than the ≤24 grammar.
    /// </summary>
    public static bool LooksLikeObjectSpelling(string? text) =>
        text is not null && ObjectSpelling.IsMatch(text);

    // A reserved object read as a root (not `node:x`, not `x.node.y`), a Scriban
    // connective, or date.parse. A quoted literal is NOT a tell: ≤24 quotes too.
    // #1101: deliverable.<id> is the 26 object spelling too.
    private static readonly Regex ObjectSpelling = new(@"(^|[^a-z0-9_:.])(input|node|item|deliverable)\.[a-z0-9_]|&&|\|\||^!\s*\(|date\.parse", RegexOptions.Compiled);

    /// <summary>
    /// Reads a condition in the object spelling back into the ≤24 tree — the
    /// subset <see cref="Spell"/> writes: <c>node.x.value</c>, <c>input.&lt;path&gt;</c>,
    /// <c>(input.x | array.size)</c>, <c>item.id</c>, literals (a quoted string, a
    /// number, true/false, <c>date.parse "…"</c>), <c>&amp;&amp;</c>, <c>||</c>, <c>!</c>,
    /// parentheses, the comparisons and the arithmetic operators. Anything else a
    /// 25 condition may read (a node's text, a snippet, a fact) has no ≤24 twin:
    /// false, with the sentence's tail.
    /// </summary>
    public static bool TryUnspell(string? text, out WorkflowConditionExpression? expression, out string? error)
    {
        expression = null;
        error = null;

        if (!WorkflowConditionAt25.TryParse(text?.Trim() ?? string.Empty, out var parsed, out var parseError) || parsed is null)
        {
            error = parseError;
            return false;
        }

        string legacy;
        try
        {
            legacy = Unspell(parsed);
        }
        catch (UnspellException ex)
        {
            error = ex.Message;
            return false;
        }

        return WorkflowResultConditions.TryParseLegacy(legacy, out expression, out error);
    }

    private sealed class UnspellException(string message) : Exception(message);

    private static string Unspell(ScriptExpression expression) => expression switch
    {
        ScriptNestedExpression nested => $"({Unspell(nested.Expression)})",
        ScriptBinaryExpression { Operator: ScriptBinaryOperator.And } b => $"{Unspell(b.Left)} and {Unspell(b.Right)}",
        ScriptBinaryExpression { Operator: ScriptBinaryOperator.Or } b => $"{Unspell(b.Left)} or {Unspell(b.Right)}",
        ScriptBinaryExpression b when Comparison(b.Operator) is { } op => UnspellComparison(b.Left, op, b.Right),
        ScriptBinaryExpression b when Arithmetic(b.Operator) is { } op => $"{UnspellTerm(b.Left)} {op} {UnspellTerm(b.Right)}",
        ScriptUnaryExpression { Operator: ScriptUnaryOperator.Not } u => $"not {Unspell(u.Right)}",
        _ => UnspellTerm(expression)
    };

    private static string UnspellComparison(ScriptExpression left, string op, ScriptExpression right)
    {
        // The ≤24 clause puts the operand on the left; a literal on the left swaps sides.
        if (IsLiteral(left) && !IsLiteral(right))
        {
            return $"{UnspellTerm(right)} {Flip(op)} {UnspellLiteral(left)}";
        }

        return $"{UnspellTerm(left)} {op} {(IsLiteral(right) ? UnspellLiteral(right) : UnspellTerm(right))}";
    }

    private static string UnspellTerm(ScriptExpression expression)
    {
        switch (expression)
        {
            case ScriptNestedExpression nested:
                // (input.x | array.size) is count(x); any other parenthesised term is arithmetic.
                return nested.Expression is ScriptPipeCall ? UnspellTerm(nested.Expression) : $"({UnspellTerm(nested.Expression)})";
            case ScriptBinaryExpression b when Arithmetic(b.Operator) is { } op:
                return $"{UnspellTerm(b.Left)} {op} {UnspellTerm(b.Right)}";
            case ScriptUnaryExpression { Operator: ScriptUnaryOperator.Negate } u:
                return $"- ({UnspellTerm(u.Right)})";
            case ScriptPipeCall pipe:
                if (Chain(pipe.To is ScriptFunctionCall call ? call.Target : pipe.To) is ["array", "size"]
                    && Chain(pipe.From) is [TemplateObjects.Input, .. var counted] && counted.Length > 0)
                {
                    return $"count({string.Join('.', counted)})";
                }

                throw new UnspellException("pipes a value; a condition counts an array as (input.<id> | array.size) and reads nothing else through a pipe.");
            case ScriptLiteral:
            case ScriptFunctionCall:
                return UnspellLiteral(expression);
        }

        var chain = Chain(expression);

        return chain switch
        {
            [TemplateObjects.Node, var node, TemplateMembers.Value] => $"node:{node}",
            [TemplateObjects.Node, var node, var member] => throw new UnspellException($"reads node.{node}.{member}; the builder reads an enum node's value (node.{node}.value)."),
            [TemplateObjects.Item, var member] => $"item:{member}",
            // #1236 (v27 design § 1.2): an array input's element field — item.value.<field>, the builder's item:value.<field>.
            [TemplateObjects.Item, TemplateMembers.Value, var field] => $"item:{TemplateMembers.Value}.{field}",
            [TemplateObjects.Input, .. var path] when path.Length > 0 => string.Join('.', path),
            [var root, ..] => throw new UnspellException($"reads {string.Join('.', chain)}; the builder reads input.<path>, node.<id>.value, item.id and item.value.<field> ({root} is beyond it)."),
            _ => throw new UnspellException("has a term the builder cannot read.")
        };
    }

    private static string UnspellLiteral(ScriptExpression expression) => expression switch
    {
        ScriptLiteral { Value: string s } => $"\"{s}\"",
        ScriptLiteral { Value: bool b } => b ? "true" : "false",
        ScriptLiteral { Value: { } v } => Convert.ToString(v, CultureInfo.InvariantCulture) ?? throw new UnspellException("has a literal of no value."),
        ScriptFunctionCall call when Chain(call.Target) is ["date", "parse"] && call.Arguments is [ScriptLiteral { Value: string date }] => date,
        _ => throw new UnspellException($"has '{expression}' where a literal was expected.")
    };

    private static bool IsLiteral(ScriptExpression expression) =>
        expression is ScriptLiteral || (expression is ScriptFunctionCall call && Chain(call.Target) is ["date", "parse"]);

    /// <summary>A member chain as its names, root first; empty when the expression is not one.</summary>
    private static string[] Chain(ScriptExpression? expression)
    {
        var names = new List<string>();
        var current = expression;

        while (true)
        {
            switch (current)
            {
                case ScriptMemberExpression member when member.Member is not null:
                    names.Insert(0, member.Member.Name);
                    current = member.Target;
                    continue;
                case ScriptIndexerExpression { Index: ScriptLiteral { Value: string name } } indexer:
                    names.Insert(0, name);
                    current = indexer.Target;
                    continue;
                case ScriptVariableGlobal variable:
                    names.Insert(0, variable.Name);
                    return names.ToArray();
                default:
                    return Array.Empty<string>();
            }
        }
    }

    private static string? Comparison(ScriptBinaryOperator op) => op switch
    {
        ScriptBinaryOperator.CompareEqual => "==",
        ScriptBinaryOperator.CompareNotEqual => "!=",
        ScriptBinaryOperator.CompareLess => "<",
        ScriptBinaryOperator.CompareGreater => ">",
        ScriptBinaryOperator.CompareLessOrEqual => "<=",
        ScriptBinaryOperator.CompareGreaterOrEqual => ">=",
        _ => null
    };

    private static string? Arithmetic(ScriptBinaryOperator op) => op switch
    {
        ScriptBinaryOperator.Add => "+",
        ScriptBinaryOperator.Subtract => "-",
        ScriptBinaryOperator.Multiply => "*",
        ScriptBinaryOperator.Divide => "/",
        _ => null
    };

    private static string Flip(string op) => op switch
    {
        "<" => ">",
        ">" => "<",
        "<=" => ">=",
        ">=" => "<=",
        _ => op
    };
}
