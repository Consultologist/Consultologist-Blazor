using Consultologist.PackageFormat;

namespace Consultologist.Web.Services.Workflow;

/// <summary>
/// v10 step (g) (#498): the editor's view of a document's condition — a tree
/// of groups joined by and/or and clauses edited by the three pickers, each
/// negatable, a clause's left side optionally one arithmetic term. Read from
/// the format's own parser and written back through its own writer, so the
/// whitespace and grouping rules live once (WorkflowResultCondition.Text).
/// A loaded expression the tree cannot hold is verbatim: shown, never
/// rewritten, clearable.
/// </summary>
public abstract record ConditionNode(bool Not);

/// <summary>
/// One clause. An empty operand is a clause not yet chosen (explicit
/// initialisation): the tree cannot be written until it is.
/// </summary>
public sealed record ConditionClause(
    bool Not,
    string Operand,
    string? Operator,
    string Literal,
    ConditionArithmetic? Arithmetic = null) : ConditionNode(Not)
{
    public static ConditionClause Empty => new(false, string.Empty, null, string.Empty);
    public bool IsEmpty => Operand.Length == 0;
    public bool IsBare => Operator is null;
}

/// <summary>The one arithmetic shape the builder authors: operand op (operand | literal), on the left of the comparison.</summary>
public sealed record ConditionArithmetic(char Op, string Right, bool RightIsLiteral);

public sealed record ConditionGroup(bool Not, string Join, IReadOnlyList<ConditionNode> Items) : ConditionNode(Not)
{
    public const string And = "and";
    public const string Or = "or";

    public static ConditionGroup OfOne(ConditionNode item) => new(false, And, new[] { item });
    public static ConditionGroup Fresh => OfOne(ConditionClause.Empty);
}

public static class ConditionBuilderModel
{
    /// <summary>
    /// Reads a condition into the tree. Blank is a root with one empty clause
    /// (the null condition, "always produced"). Returns false with the text
    /// in <paramref name="verbatim"/> when the parser refuses it, or when the
    /// tree cannot hold it: a double negation, or arithmetic wider than one
    /// term on the left with a literal on the right.
    /// </summary>
    public static bool TryFromText(string? when, out ConditionGroup root, out string? verbatim)
    {
        verbatim = null;
        if (string.IsNullOrWhiteSpace(when))
        {
            root = ConditionGroup.Fresh;
            return true;
        }

        if (!WorkflowResultConditions.TryParseExpression(when, out var expression, out _) || expression is null
            || FromExpression(expression) is not { } node)
        {
            root = ConditionGroup.Fresh;
            verbatim = when.Trim();
            return false;
        }

        root = node as ConditionGroup ?? ConditionGroup.OfOne(node);
        return true;
    }

    private static ConditionNode? FromExpression(WorkflowConditionExpression expression)
    {
        switch (expression)
        {
            case WorkflowClauseExpression { Clause: var clause }:
                return FromClause(clause);
            case WorkflowNotExpression { Inner: WorkflowNotExpression }:
                return null;
            case WorkflowNotExpression { Inner: var inner }:
                return FromExpression(inner) is { } negated ? negated with { Not = !negated.Not } : null;
            case WorkflowAndExpression and_:
                return Flatten(and_, ConditionGroup.And);
            case WorkflowOrExpression or_:
                return Flatten(or_, ConditionGroup.Or);
            default:
                return null;
        }
    }

    /// <summary>A binary chain of one join reads as one n-ary group; a nested chain of the other join as a group beneath it.</summary>
    private static ConditionGroup? Flatten(WorkflowConditionExpression expression, string join)
    {
        var items = new List<ConditionNode>();
        return Collect(expression) ? new ConditionGroup(false, join, items) : null;

        bool Collect(WorkflowConditionExpression e)
        {
            if (join == ConditionGroup.And && e is WorkflowAndExpression a)
            {
                return Collect(a.Left) && Collect(a.Right);
            }

            if (join == ConditionGroup.Or && e is WorkflowOrExpression o)
            {
                return Collect(o.Left) && Collect(o.Right);
            }

            if (FromExpression(e) is not { } item)
            {
                return false;
            }

            items.Add(item);
            return true;
        }
    }

    private static ConditionClause? FromClause(WorkflowResultCondition clause)
    {
        if (!clause.IsArithmetic)
        {
            return new ConditionClause(false, clause.Operand, clause.Operator, clause.Literal ?? string.Empty);
        }

        // Representable arithmetic: `operand op (operand | literal)` compared to a literal.
        if (clause.Left is WorkflowBinaryTerm { Left: WorkflowOperandTerm left, Right: var right } term
            && clause.Right is WorkflowLiteralTerm literal
            && right is WorkflowOperandTerm or WorkflowLiteralTerm)
        {
            return new ConditionClause(false, left.Operand.Operand, clause.Ordering ?? (clause.Negated ? "!=" : "=="), literal.Literal,
                new ConditionArithmetic(term.Op, right.Text, right is WorkflowLiteralTerm));
        }

        return null;
    }

    /// <summary>Every clause, in document order.</summary>
    public static IEnumerable<ConditionClause> Clauses(ConditionGroup root)
    {
        foreach (var item in root.Items)
        {
            if (item is ConditionClause clause)
            {
                yield return clause;
            }
            else if (item is ConditionGroup group)
            {
                foreach (var nested in Clauses(group))
                {
                    yield return nested;
                }
            }
        }
    }

    public static bool IsComplete(ConditionGroup root) => Clauses(root).All(clause => !clause.IsEmpty);

    /// <summary>
    /// The condition's text, through the format's writer; null when a clause
    /// is not yet chosen, or when a clause's own text does not parse (a
    /// literal with a space). A root holding one empty clause is the null
    /// condition.
    /// </summary>
    public static string? ToText(ConditionGroup root)
    {
        if (root.Items.Count == 1 && root.Items[0] is ConditionClause { IsEmpty: true, Not: false } && !root.Not)
        {
            return null;
        }

        return ToExpression(root)?.Text;
    }

    public static WorkflowConditionExpression? ToExpression(ConditionNode node)
    {
        WorkflowConditionExpression? inner = node switch
        {
            ConditionClause clause => ClauseExpression(clause),
            ConditionGroup group => GroupExpression(group),
            _ => null
        };

        return inner is null ? null : node.Not ? new WorkflowNotExpression(inner) : inner;
    }

    private static WorkflowConditionExpression? GroupExpression(ConditionGroup group)
    {
        WorkflowConditionExpression? folded = null;
        foreach (var item in group.Items)
        {
            if (ToExpression(item) is not { } next)
            {
                return null;
            }

            folded = folded is null
                ? next
                : group.Join == ConditionGroup.Or ? new WorkflowOrExpression(folded, next) : new WorkflowAndExpression(folded, next);
        }

        return folded;
    }

    /// <summary>One clause, spelled and re-read so the parser derives its path, count or node.</summary>
    private static WorkflowConditionExpression? ClauseExpression(ConditionClause clause)
    {
        if (clause.IsEmpty)
        {
            return null;
        }

        var left = clause.Arithmetic is { } arithmetic ? $"{clause.Operand} {arithmetic.Op} {arithmetic.Right}" : clause.Operand;
        var text = clause.Operator is null ? left : $"{left} {clause.Operator} {clause.Literal}".TrimEnd();
        return WorkflowResultConditions.TryParseExpression(text, out var expression, out _) ? expression : null;
    }

    // ----- the tree, edited by index path -------------------------------

    /// <summary>The node at an index path from the root (empty path = the root).</summary>
    public static ConditionNode? At(ConditionGroup root, IReadOnlyList<int> path)
    {
        ConditionNode current = root;
        foreach (var index in path)
        {
            if (current is not ConditionGroup group || index < 0 || index >= group.Items.Count)
            {
                return null;
            }

            current = group.Items[index];
        }

        return current;
    }

    /// <summary>The root with the node at the path replaced (null removes it; an emptied nested group goes with it).</summary>
    public static ConditionGroup Replace(ConditionGroup root, IReadOnlyList<int> path, ConditionNode? replacement)
    {
        if (path.Count == 0)
        {
            return replacement as ConditionGroup ?? (replacement is null ? ConditionGroup.Fresh : ConditionGroup.OfOne(replacement));
        }

        return (ConditionGroup)Rewrite(root, 0);

        ConditionNode Rewrite(ConditionNode node, int depth)
        {
            var group = (ConditionGroup)node;
            var items = new List<ConditionNode>(group.Items);
            var index = path[depth];
            if (depth == path.Count - 1)
            {
                if (replacement is null)
                {
                    items.RemoveAt(index);
                }
                else
                {
                    items[index] = replacement;
                }
            }
            else
            {
                var rewritten = Rewrite(items[index], depth + 1);
                if (rewritten is ConditionGroup { Items.Count: 0 })
                {
                    items.RemoveAt(index);
                }
                else
                {
                    items[index] = rewritten;
                }
            }

            return group with { Items = items };
        }
    }

    public static ConditionGroup Append(ConditionGroup root, IReadOnlyList<int> groupPath, ConditionNode item)
    {
        var group = (ConditionGroup)At(root, groupPath)!;
        return Replace(root, groupPath, group with { Items = group.Items.Append(item).ToList() });
    }
}
