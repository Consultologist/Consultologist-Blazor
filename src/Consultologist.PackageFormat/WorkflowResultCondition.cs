using System.Globalization;
using System.Text.RegularExpressions;

namespace Consultologist.PackageFormat;

/// <summary>
/// A parsed deliverable condition (package-format-v8-design.md § 5, v9 § 6):
/// <c>when: billable</c>, <c>when: kind == follow_up</c>,
/// <c>when: kind != procedure</c> — and from v9 <c>when: length_of_stay > 7</c>,
/// <c>when: patient.age >= 65</c>, <c>when: count(prior_notes) > 1</c>,
/// <c>when: prior_notes</c>.
///
/// <c>Literal</c> null is the bare truthy form. <c>Field</c> names one field of
/// an object input; <c>IsCount</c> asks how many elements an array holds;
/// <c>Ordering</c> is one of the four ordering operators, null for equality.
/// Trailing optionals, so the v8 shape — id, literal, negated — reads and
/// constructs exactly as it did. Parsed once — at publish by the validator,
/// at load by the store — so the engine evaluates a structure and the grammar
/// lives in one place.
/// </summary>
public sealed record WorkflowResultCondition(
    string InputId,
    string? Literal,
    bool Negated,
    string? Field = null,
    bool IsCount = false,
    string? Ordering = null,
    // v10 (#494, package-format-v10-design.md § 6): a path of any length —
    // Field is its first segment, kept so every v9 reader is unchanged; a
    // classifier's value, `node:<id>`; and, when a side of the comparison is
    // arithmetic rather than one operand and one literal, the two terms.
    // Trailing optionals: a v9 condition constructs and reads as it did.
    IReadOnlyList<string>? Path = null,
    string? NodeId = null,
    WorkflowConditionTerm? Left = null,
    WorkflowConditionTerm? Right = null)
{
    /// <summary>The operand as an author wrote it: <c>patient.age</c>, <c>count(prior_notes)</c>, <c>node:scope</c>.</summary>
    public string Operand => Left is not null
        ? Left.Text
        : NodeId is not null
            ? $"node:{NodeId}"
            : IsCount
                ? $"count({PathText})"
                : PathText;

    /// <summary>
    /// The segments past the input id. A v9 record built by hand carries its
    /// one field in Field alone, so the field is the path when Path is unset.
    /// </summary>
    public IReadOnlyList<string> Segments => Path ?? (Field is null ? Array.Empty<string>() : new[] { Field });

    /// <summary>The dotted path: the input id and every segment after it.</summary>
    public string PathText => Segments.Count > 0 ? $"{InputId}.{string.Join('.', Segments)}" : InputId;

    /// <summary>How many segments the path has past the input: 0 for a plain input, 1 for v9's one field.</summary>
    public int PathDepth => Segments.Count;

    public bool IsNodeValue => NodeId is not null;

    /// <summary>A side of the comparison is arithmetic (v10).</summary>
    public bool IsArithmetic => Left is not null || Right is not null;

    /// <summary>The comparison, or null for the bare form.</summary>
    public string? Operator => Literal is null
        ? null
        : Ordering ?? (Negated ? WorkflowResultConditions.NotEqualsOperator : WorkflowResultConditions.EqualsOperator);

    public bool IsBare => Literal is null;

    public bool IsOrdered => Ordering is not null;
}

/// <summary>
/// The condition expression (v10 § 6, #494): clauses joined by <c>and</c>,
/// <c>or</c>, negated by <c>not</c>, grouped by parentheses. A v9 condition is
/// one clause — the leaf — and parses to exactly the record it always did.
/// </summary>
public abstract record WorkflowConditionExpression
{
    /// <summary>Every clause in the tree, left to right.</summary>
    public abstract IEnumerable<WorkflowResultCondition> Leaves { get; }

    /// <summary>The one clause when the expression is nothing more — the v8/v9 form.</summary>
    public WorkflowResultCondition? SingleClause => this is WorkflowClauseExpression { Clause: var clause } ? clause : null;

    /// <summary>The first v10-only form in the tree, named for the version gate, or null.</summary>
    public abstract string? FirstV10Form { get; }

    public abstract string Text { get; }

    /// <summary>A v9 clause is an expression of one leaf — so every caller that built one still does.</summary>
    public static implicit operator WorkflowConditionExpression(WorkflowResultCondition clause) => new WorkflowClauseExpression(clause);
}

public sealed record WorkflowClauseExpression(WorkflowResultCondition Clause) : WorkflowConditionExpression
{
    public override IEnumerable<WorkflowResultCondition> Leaves => new[] { Clause };

    public override string? FirstV10Form =>
        Clause.IsArithmetic ? "arithmetic"
        : Clause.IsNodeValue ? $"'node:{Clause.NodeId}'"
        : Clause.PathDepth >= 2 ? $"a path of {Clause.PathDepth + 1} segments"
        : null;

    public override string Text => Clause.Left is not null
        ? $"{Clause.Left.Text} {Clause.Operator} {Clause.Right?.Text}"
        : Clause.IsBare ? Clause.Operand : $"{Clause.Operand} {Clause.Operator} {Clause.Literal}";
}

public sealed record WorkflowNotExpression(WorkflowConditionExpression Inner) : WorkflowConditionExpression
{
    public override IEnumerable<WorkflowResultCondition> Leaves => Inner.Leaves;
    public override string? FirstV10Form => "'not'";
    public override string Text => $"not {Wrap(Inner)}";
    internal static string Wrap(WorkflowConditionExpression e) => e is WorkflowClauseExpression ? e.Text : $"({e.Text})";
}

public sealed record WorkflowAndExpression(WorkflowConditionExpression Left, WorkflowConditionExpression Right) : WorkflowConditionExpression
{
    public override IEnumerable<WorkflowResultCondition> Leaves => Left.Leaves.Concat(Right.Leaves);
    public override string? FirstV10Form => "'and'";
    public override string Text => $"{Side(Left)} and {Side(Right)}";
    private static string Side(WorkflowConditionExpression e) => e is WorkflowOrExpression ? $"({e.Text})" : e.Text;
}

public sealed record WorkflowOrExpression(WorkflowConditionExpression Left, WorkflowConditionExpression Right) : WorkflowConditionExpression
{
    public override IEnumerable<WorkflowResultCondition> Leaves => Left.Leaves.Concat(Right.Leaves);
    public override string? FirstV10Form => "'or'";
    public override string Text => $"{Left.Text} or {Right.Text}";
}

/// <summary>
/// One side of a v10 comparison when it is more than an operand or a literal:
/// arithmetic over numbers and counts, a date plus or minus days. An operand
/// term carries the clause-shaped operand (path, count, node) it names.
/// </summary>
public abstract record WorkflowConditionTerm
{
    public abstract string Text { get; }
    public abstract IEnumerable<WorkflowResultCondition> Operands { get; }
}

public sealed record WorkflowOperandTerm(WorkflowResultCondition Operand) : WorkflowConditionTerm
{
    public override string Text => Operand.Operand;
    public override IEnumerable<WorkflowResultCondition> Operands => new[] { Operand };
}

public sealed record WorkflowLiteralTerm(string Literal) : WorkflowConditionTerm
{
    public override string Text => Literal;
    public override IEnumerable<WorkflowResultCondition> Operands => Enumerable.Empty<WorkflowResultCondition>();
}

public sealed record WorkflowBinaryTerm(char Op, WorkflowConditionTerm Left, WorkflowConditionTerm Right) : WorkflowConditionTerm
{
    public override string Text => $"{Wrap(Left, false)} {Op} {Wrap(Right, true)}";
    public override IEnumerable<WorkflowResultCondition> Operands => Left.Operands.Concat(Right.Operands);
    private string Wrap(WorkflowConditionTerm side, bool right) =>
        side is WorkflowBinaryTerm b && (Precedence(b.Op) < Precedence(Op) || (right && Precedence(b.Op) == Precedence(Op)))
            ? $"({side.Text})" : side.Text;
    internal static int Precedence(char op) => op is '*' or '/' ? 2 : 1;
}

public sealed record WorkflowNegateTerm(WorkflowConditionTerm Inner) : WorkflowConditionTerm
{
    public override string Text => $"- {Inner.Text}";
    public override IEnumerable<WorkflowResultCondition> Operands => Inner.Operands;
}

/// <summary>
/// The condition grammar. v8 gave it one operand, one operator, one literal;
/// v9 six operators, a path, count(); v10 (§ 6) an evaluator with a stated
/// order of operations — parentheses; unary minus; * /; + -; comparison;
/// not; and; or — the <c>node:</c> operand, paths of any length, and
/// count() of a path.
///
/// **Syntax only.** Whether the id is declared, whether its type may be tested
/// with this operator, and whether the literal is admissible are vocabulary
/// closures, and those belong to the validator — the same split the binding
/// parser follows. The parser accepts the full grammar whatever the version; a
/// manifest using a later form is refused by the validator by name.
///
/// **The whitespace rule (v10 § 6, amended #494).** The arithmetic operators
/// are tokens of their own only with whitespace on both sides. <c>-1</c> and
/// <c>2026-1-1</c> are one token — a literal — so the v9 sentences about
/// them ("not a whole number", "not a date written YYYY-MM-DD") are produced
/// by the v9 rules, byte for byte; <c>seen_on - 7</c> is subtraction.
/// </summary>
public static class WorkflowResultConditions
{
    public const string EqualsOperator = "==";
    public const string NotEqualsOperator = "!=";

    // Two-character operators first, so ">=" is never read as ">" followed
    // by a literal beginning with "=".
    private static readonly string[] Operators = { ">=", "<=", EqualsOperator, NotEqualsOperator, ">", "<" };

    private const string NodePrefix = "node:";

    /// <summary>
    /// The v8/v9 entry point, kept: the one clause of a one-clause condition.
    /// An expression (v10) is not one clause and answers false, naming it.
    /// </summary>
    public static bool TryParse(string? when, out WorkflowResultCondition? condition, out string? error)
    {
        condition = null;

        if (!TryParseExpression(when, out var expression, out error))
        {
            return false;
        }

        condition = expression!.SingleClause;

        if (condition is null)
        {
            error = $"'{when!.Trim()}' is an expression, not one clause.";
            return false;
        }

        return true;
    }

    /// <summary>The whole grammar. A one-clause text parses to the record v9 produced, byte for byte.</summary>
    public static bool TryParseExpression(string? when, out WorkflowConditionExpression? expression, out string? error)
    {
        expression = null;
        error = null;

        var text = when?.Trim();

        if (string.IsNullOrEmpty(text))
        {
            error = "is blank.";
            return false;
        }

        // The v9 path first: a text with none of v10's tokens is one clause,
        // read by the rules that produced every published sentence.
        if (!HasV10Tokens(text))
        {
            if (!TryParseClause(text, out var clause, out error))
            {
                return false;
            }

            expression = new WorkflowClauseExpression(clause!);
            return true;
        }

        var tokens = Tokenize(text);
        var parser = new Parser(tokens);

        try
        {
            expression = parser.ParseExpression();

            if (!parser.AtEnd)
            {
                error = $"has '{parser.Current}' where an operator or the end was expected.";
                expression = null;
                return false;
            }

            return true;
        }
        catch (ConditionSyntaxException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static readonly Regex TokenPattern = new(
        """\(|\)|>=|<=|==|!=|>|<|"[^"]*"|[^\s()<>=!]+""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> Words = new(StringComparer.Ordinal) { "and", "or", "not" };

    /// <summary>Whether the text uses anything past one clause: the words, parentheses beyond count(), spaced arithmetic, node:.</summary>
    private static bool HasV10Tokens(string text)
    {
        foreach (Match match in TokenPattern.Matches(text))
        {
            var token = match.Value;

            if (Words.Contains(token) || token == "(" || token == ")" || token is "+" or "-" or "*" or "/"
                || token.StartsWith(NodePrefix, StringComparison.Ordinal))
            {
                // count(x) is a v9 token when written without spaces; the
                // pattern above splits it into `count`, `(`, `x`, `)`.
                if (token == "(" && match.Index > 0 && text[..match.Index].EndsWith("count", StringComparison.Ordinal))
                {
                    continue;
                }

                if (token == ")" && IsCountClose(text, match.Index))
                {
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    private static bool IsCountClose(string text, int index)
    {
        var open = text.LastIndexOf('(', index);
        return open >= 5 && text[(open - 5)..open] == "count" && text.IndexOf('(', open + 1) is var next && (next < 0 || next > index);
    }

    private static List<string> Tokenize(string text) =>
        TokenPattern.Matches(text).Select(match => match.Value).ToList();

    private sealed class ConditionSyntaxException(string message) : Exception(message);

    /// <summary>Recursive descent over the tokens, precedence as § 6 states it.</summary>
    private sealed class Parser(List<string> tokens)
    {
        private int _index;

        public bool AtEnd => _index >= tokens.Count;
        public string? Current => AtEnd ? null : tokens[_index];
        private string? Peek(int ahead = 0) => _index + ahead < tokens.Count ? tokens[_index + ahead] : null;
        private string Take() => tokens[_index++];

        public WorkflowConditionExpression ParseExpression()
        {
            var left = ParseAnd();

            while (Current == "or")
            {
                Take();
                left = new WorkflowOrExpression(left, ParseAnd());
            }

            return left;
        }

        private WorkflowConditionExpression ParseAnd()
        {
            var left = ParseNot();

            while (Current == "and")
            {
                Take();
                left = new WorkflowAndExpression(left, ParseNot());
            }

            return left;
        }

        private WorkflowConditionExpression ParseNot()
        {
            if (Current == "not")
            {
                Take();
                return new WorkflowNotExpression(ParseNot());
            }

            return ParsePrimary();
        }

        private WorkflowConditionExpression ParsePrimary()
        {
            if (Current is null)
            {
                throw new ConditionSyntaxException("ends where a clause was expected.");
            }

            if (Current == "(")
            {
                // A parenthesised expression — or a parenthesised term at the
                // start of a clause, which is what a comparison after it means.
                var save = _index;
                Take();
                var inner = ParseExpression();
                Expect(")");

                if (Current is not null && IsComparison(Current) && inner is WorkflowClauseExpression { Clause: { IsBare: true } bare })
                {
                    _index = save;
                    return ParseClause();
                }

                if (Current is "+" or "-" or "*" or "/" && inner is WorkflowClauseExpression { Clause.IsBare: true })
                {
                    _index = save;
                    return ParseClause();
                }

                return inner;
            }

            return ParseClause();
        }

        private WorkflowConditionExpression ParseClause()
        {
            var left = ParseSum();

            if (Current is null || !IsComparison(Current))
            {
                // The bare form: one operand, nothing else.
                return new WorkflowClauseExpression(BareClause(left));
            }

            var op = Take();
            if (AtEnd)
            {
                throw new ConditionSyntaxException("compares against nothing; write a value after the operator.");
            }

            var right = ParseSum();
            return new WorkflowClauseExpression(Clause(left, op, right));
        }

        private WorkflowConditionTerm ParseSum()
        {
            var left = ParseProduct();

            while (Current is "+" or "-")
            {
                var op = Take()[0];
                left = new WorkflowBinaryTerm(op, left, ParseProduct());
            }

            return left;
        }

        private WorkflowConditionTerm ParseProduct()
        {
            var left = ParseAtom();

            while (Current is "*" or "/")
            {
                var op = Take()[0];
                left = new WorkflowBinaryTerm(op, left, ParseAtom());
            }

            return left;
        }

        private WorkflowConditionTerm ParseAtom()
        {
            if (Current is null)
            {
                throw new ConditionSyntaxException("ends where a value was expected.");
            }

            if (Current == "-")
            {
                Take();
                return new WorkflowNegateTerm(ParseAtom());
            }

            if (Current == "(")
            {
                Take();
                var inner = ParseSum();
                Expect(")");
                return inner;
            }

            var token = Take();

            if (Words.Contains(token) || IsComparison(token) || token is ")" or "+" or "*" or "/")
            {
                throw new ConditionSyntaxException($"has '{token}' where a value was expected.");
            }

            // count( x ) split into tokens by the pattern: reassemble.
            if (token == "count" && Current == "(")
            {
                Take();
                var inner = Take();
                Expect(")");
                token = $"count({inner})";
            }

            if (TryParseOperand(token, out var operand))
            {
                return new WorkflowOperandTerm(operand!);
            }

            var literal = token;
            if (literal.Length >= 2 && literal[0] == '"' && literal[^1] == '"')
            {
                literal = literal[1..^1];
            }
            else if (literal.Contains('"'))
            {
                throw new ConditionSyntaxException($"literal '{literal}' has a stray quote.");
            }

            return new WorkflowLiteralTerm(literal);
        }

        private void Expect(string token)
        {
            if (Current != token)
            {
                throw new ConditionSyntaxException(Current is null ? $"is missing a '{token}'." : $"has '{Current}' where '{token}' was expected.");
            }

            Take();
        }

        private static bool IsComparison(string token) => Array.IndexOf(Operators, token) >= 0;
    }

    /// <summary>A clause of one operand: the v9 record when the term is one operand; else an arithmetic clause with no comparison (refused by the validator).</summary>
    private static WorkflowResultCondition BareClause(WorkflowConditionTerm term) =>
        term is WorkflowOperandTerm { Operand: var operand } ? operand : Synthetic(term, null, null);

    /// <summary>
    /// A clause from two terms. One operand against one literal is the v9
    /// record exactly — so the sentences, the validator's arms and the editor
    /// see what they always saw; anything wider carries its terms.
    /// </summary>
    private static WorkflowResultCondition Clause(WorkflowConditionTerm left, string op, WorkflowConditionTerm right)
    {
        var ordering = op is EqualsOperator or NotEqualsOperator ? null : op;
        var negated = op == NotEqualsOperator;

        if (left is WorkflowOperandTerm { Operand: var operand })
        {
            // v9's reading of the right-hand side: a literal. A bare word there
            // is an enum value, true/false, a number or a date — never an
            // input — so `kind == follow_up` means what it always meant. Two
            // inputs are compared through arithmetic (`a - b > 0`) or a path.
            if (right is WorkflowLiteralTerm { Literal: var literal })
            {
                return operand with { Literal = literal, Negated = negated, Ordering = ordering };
            }

            if (right is WorkflowOperandTerm { Operand: { PathDepth: 0, IsCount: false, IsNodeValue: false } word })
            {
                return operand with { Literal = word.InputId, Negated = negated, Ordering = ordering };
            }
        }

        return Synthetic(left, op, right);
    }

    private static WorkflowResultCondition Synthetic(WorkflowConditionTerm left, string? op, WorkflowConditionTerm? right)
    {
        var first = left.Operands.FirstOrDefault() ?? right?.Operands.FirstOrDefault();
        var negated = op == NotEqualsOperator;
        var ordering = op is null or EqualsOperator or NotEqualsOperator ? null : op;

        return new WorkflowResultCondition(
            first?.InputId ?? string.Empty,
            right?.Text,
            negated,
            first?.Field,
            first?.IsCount ?? false,
            ordering,
            first?.Path,
            first?.NodeId,
            Left: left,
            Right: right);
    }

    /// <summary>The v9 clause parser, unchanged in what it says; the operand now admits a path of any length and node:.</summary>
    private static bool TryParseClause(string text, out WorkflowResultCondition? condition, out string? error)
    {
        condition = null;
        error = null;

        var (operatorIndex, op) = FindOperator(text);

        // The bare form: an operand and nothing else.
        if (operatorIndex < 0)
        {
            if (!TryParseOperand(text, out var bare))
            {
                error = $"'{text}' is not an input id. Write 'when: <input>' or 'when: <input> == <value>'.";
                return false;
            }

            condition = bare;
            return true;
        }

        var operandText = text[..operatorIndex].Trim();
        var literal = text[(operatorIndex + op!.Length)..].Trim();

        if (!TryParseOperand(operandText, out var operand))
        {
            error = $"'{operandText}' is not an input id.";
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

        condition = operand! with
        {
            Literal = literal,
            Negated = op == NotEqualsOperator,
            Ordering = op is EqualsOperator or NotEqualsOperator ? null : op
        };
        return true;
    }

    private static (int Index, string? Operator) FindOperator(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            foreach (var candidate in Operators)
            {
                if (string.CompareOrdinal(text, index, candidate, 0, candidate.Length) == 0)
                {
                    return (index, candidate);
                }
            }
        }

        return (-1, null);
    }

    /// <summary>
    /// <c>id</c>, <c>id.field</c>, <c>id.a.b</c> (v10), <c>count(path)</c> or
    /// <c>node:id</c> (v10), each part a declared id.
    /// </summary>
    private static bool TryParseOperand(string raw, out WorkflowResultCondition? operand)
    {
        operand = null;

        if (raw.StartsWith(NodePrefix, StringComparison.Ordinal))
        {
            var nodeId = raw[NodePrefix.Length..];

            if (!WorkflowDeclaredIds.IsValid(nodeId))
            {
                return false;
            }

            operand = new WorkflowResultCondition(nodeId, null, false, NodeId: nodeId);
            return true;
        }

        if (raw.StartsWith("count(", StringComparison.Ordinal) && raw.EndsWith(')'))
        {
            var inner = raw["count(".Length..^1].Trim();

            if (!TryParsePath(inner, out var id, out var path))
            {
                return false;
            }

            operand = new WorkflowResultCondition(id!, null, false, Field: path?[0], IsCount: true, Path: path);
            return true;
        }

        if (!TryParsePath(raw, out var inputId, out var segments))
        {
            return false;
        }

        operand = new WorkflowResultCondition(inputId!, null, false, Field: segments?[0], Path: segments);
        return true;
    }

    /// <summary>A dotted path: every segment a declared id. Path is null for a plain input, so a v9 record reads as it did.</summary>
    private static bool TryParsePath(string raw, out string? inputId, out IReadOnlyList<string>? path)
    {
        inputId = null;
        path = null;

        var parts = raw.Split('.');

        if (parts.Any(part => !WorkflowDeclaredIds.IsValid(part)))
        {
            return false;
        }

        inputId = parts[0];
        path = parts.Length > 1 ? parts[1..] : null;
        return true;
    }

    /// <summary>
    /// Whether a deliverable fires for these supplied inputs. Pure, and
    /// evaluated once — at job start, or (v10 § 5) at the boundary — so the
    /// fire set is knowable before anything is produced, which is what keeps
    /// TotalBlockCount a stored scalar (#176).
    ///
    /// A null condition always fires: a deliverable without a `when` is
    /// unconditional. Absence is not falsity: an optional input nobody supplied
    /// has not answered the question, so the condition does not hold —
    /// including the negated form (v8 § 4), and (v10) `not` over it. The one
    /// exception is count(): an absent array counts zero, because "no entries
    /// supplied" and "an empty list supplied" answer the same clinical
    /// question (v9 § 6). Never throws, whatever map it is handed.
    /// <paramref name="classifications"/> is what the classifiers answered
    /// (v10 § 4), by node id; null until the boundary.
    /// </summary>
    public static bool Holds(
        WorkflowResultCondition? condition,
        IReadOnlyDictionary<string, ConsultInputValue>? suppliedInputs,
        IReadOnlyDictionary<string, string>? classifications = null) =>
        condition is null || Evaluate(condition, suppliedInputs, classifications) == Outcome.Held;

    public static bool Holds(
        WorkflowConditionExpression? expression,
        IReadOnlyDictionary<string, ConsultInputValue>? suppliedInputs,
        IReadOnlyDictionary<string, string>? classifications = null) =>
        expression is null || Evaluate(expression, suppliedInputs, classifications) == Outcome.Held;

    /// <summary>
    /// Three-valued (v10 § 6): held, not held, or absent — a clause whose
    /// operand nobody supplied. Absent is never held, on either side of
    /// and/or, and stays absent under not.
    /// </summary>
    internal enum Outcome
    {
        Held,
        NotHeld,
        Absent
    }

    internal static Outcome Evaluate(
        WorkflowConditionExpression expression,
        IReadOnlyDictionary<string, ConsultInputValue>? inputs,
        IReadOnlyDictionary<string, string>? classifications) => expression switch
    {
        WorkflowClauseExpression clause => Evaluate(clause.Clause, inputs, classifications),
        WorkflowNotExpression not => Evaluate(not.Inner, inputs, classifications) switch
        {
            Outcome.Held => Outcome.NotHeld,
            Outcome.NotHeld => Outcome.Held,
            _ => Outcome.Absent
        },
        WorkflowAndExpression and => Both(Evaluate(and.Left, inputs, classifications), Evaluate(and.Right, inputs, classifications)),
        WorkflowOrExpression or => Either(Evaluate(or.Left, inputs, classifications), Evaluate(or.Right, inputs, classifications)),
        _ => Outcome.Absent
    };

    private static Outcome Both(Outcome left, Outcome right) =>
        left == Outcome.Held && right == Outcome.Held ? Outcome.Held
        : left == Outcome.NotHeld || right == Outcome.NotHeld ? Outcome.NotHeld
        : Outcome.Absent;

    private static Outcome Either(Outcome left, Outcome right) =>
        left == Outcome.Held || right == Outcome.Held ? Outcome.Held
        : left == Outcome.NotHeld && right == Outcome.NotHeld ? Outcome.NotHeld
        : Outcome.Absent;

    internal static Outcome Evaluate(
        WorkflowResultCondition condition,
        IReadOnlyDictionary<string, ConsultInputValue>? inputs,
        IReadOnlyDictionary<string, string>? classifications)
    {
        if (condition.IsArithmetic)
        {
            return EvaluateArithmetic(condition, inputs, classifications);
        }

        if (condition.IsNodeValue)
        {
            if (classifications is null || !classifications.TryGetValue(condition.NodeId!, out var answered))
            {
                return Outcome.Absent;
            }

            if (condition.IsBare)
            {
                return Outcome.NotHeld;
            }

            var same = string.Equals(answered, condition.Literal, StringComparison.Ordinal);
            return (condition.Negated ? !same : same) ? Outcome.Held : Outcome.NotHeld;
        }

        if (condition.IsCount)
        {
            if (condition.IsBare || !TryCount(condition, inputs, out var count))
            {
                return Outcome.NotHeld;
            }

            return int.TryParse(condition.Literal, NumberStyles.None, CultureInfo.InvariantCulture, out var wanted)
                && Compare(count.CompareTo(wanted), condition) ? Outcome.Held : Outcome.NotHeld;
        }

        if (!TryOperandValue(condition, inputs, out var value))
        {
            return Outcome.Absent;
        }

        if (condition.IsBare)
        {
            return value.Kind switch
            {
                ConsultInputKind.Boolean => value.Flag!.Value ? Outcome.Held : Outcome.NotHeld,
                ConsultInputKind.Array => value.Elements!.Count > 0 ? Outcome.Held : Outcome.NotHeld,
                _ => Outcome.NotHeld
            };
        }

        switch (value.Kind)
        {
            case ConsultInputKind.Number:
                return ConsultInputValue.TryParseNumber(condition.Literal!, out var literalNumber)
                    && Compare(value.NumberValue!.Value.CompareTo(literalNumber.NumberValue!.Value), condition)
                    ? Outcome.Held : Outcome.NotHeld;

            case ConsultInputKind.Text when condition.IsOrdered:
                return DateOnly.TryParseExact(value.Text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    && DateOnly.TryParseExact(condition.Literal, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var literalDate)
                    && Compare(date.CompareTo(literalDate), condition)
                    ? Outcome.Held : Outcome.NotHeld;

            case ConsultInputKind.Text:
            case ConsultInputKind.Boolean:
                var matches = string.Equals(value.Canonical, condition.Literal, StringComparison.Ordinal);
                return (condition.Negated ? !matches : matches) ? Outcome.Held : Outcome.NotHeld;

            default:
                // Structure compared to a literal answers nothing.
                return Outcome.NotHeld;
        }
    }

    /// <summary>
    /// A term's value: a number, or a date carried as its day number so ±
    /// days is arithmetic too. Null when an operand is absent or the term is
    /// not computable (division by zero, a non-numeric operand).
    /// </summary>
    private readonly record struct TermValue(decimal? Number, DateOnly? Date, bool Absent)
    {
        public static readonly TermValue Missing = new(null, null, true);
        public static readonly TermValue Undefined = new(null, null, false);
        public bool IsNumber => Number is not null;
        public bool IsDate => Date is not null;
    }

    private static Outcome EvaluateArithmetic(
        WorkflowResultCondition condition,
        IReadOnlyDictionary<string, ConsultInputValue>? inputs,
        IReadOnlyDictionary<string, string>? classifications)
    {
        if (condition.Right is null)
        {
            return Outcome.NotHeld; // a bare arithmetic term is refused at publish
        }

        var left = TermOf(condition.Left!, inputs, classifications);
        var right = TermOf(condition.Right, inputs, classifications);

        if (left.Absent || right.Absent)
        {
            return Outcome.Absent;
        }

        int comparison;

        if (left.IsNumber && right.IsNumber)
        {
            comparison = left.Number!.Value.CompareTo(right.Number!.Value);
        }
        else if (left.IsDate && right.IsDate)
        {
            comparison = left.Date!.Value.CompareTo(right.Date!.Value);
        }
        else
        {
            return Outcome.NotHeld;
        }

        return Compare(comparison, condition) ? Outcome.Held : Outcome.NotHeld;
    }

    private static TermValue TermOf(
        WorkflowConditionTerm term,
        IReadOnlyDictionary<string, ConsultInputValue>? inputs,
        IReadOnlyDictionary<string, string>? classifications)
    {
        switch (term)
        {
            case WorkflowLiteralTerm literal:
                if (ConsultInputValue.TryParseNumber(literal.Literal, out var number))
                {
                    return new TermValue(number.NumberValue, null, false);
                }

                return DateOnly.TryParseExact(literal.Literal, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? new TermValue(null, date, false)
                    : TermValue.Undefined;

            case WorkflowOperandTerm operand:
                if (operand.Operand.IsCount)
                {
                    return TryCount(operand.Operand, inputs, out var count) ? new TermValue(count, null, false) : TermValue.Undefined;
                }

                if (operand.Operand.IsNodeValue)
                {
                    return TermValue.Undefined; // a symbol has no arithmetic; refused at publish
                }

                if (!TryOperandValue(operand.Operand, inputs, out var value))
                {
                    return TermValue.Missing;
                }

                if (value.IsNumber)
                {
                    return new TermValue(value.NumberValue, null, false);
                }

                return value.Kind == ConsultInputKind.Text
                    && DateOnly.TryParseExact(value.Text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                    ? new TermValue(null, d, false)
                    : TermValue.Undefined;

            case WorkflowNegateTerm negate:
                var inner = TermOf(negate.Inner, inputs, classifications);
                return inner.IsNumber ? inner with { Number = -inner.Number } : inner.Absent ? inner : TermValue.Undefined;

            case WorkflowBinaryTerm binary:
                var l = TermOf(binary.Left, inputs, classifications);
                var r = TermOf(binary.Right, inputs, classifications);

                if (l.Absent || r.Absent)
                {
                    return TermValue.Missing;
                }

                // A date plus or minus whole days; everything else is numbers.
                if (l.IsDate && r.IsNumber && binary.Op is '+' or '-' && r.Number!.Value == decimal.Truncate(r.Number.Value))
                {
                    var days = (int)r.Number.Value;
                    return new TermValue(null, binary.Op == '+' ? l.Date!.Value.AddDays(days) : l.Date!.Value.AddDays(-days), false);
                }

                if (!l.IsNumber || !r.IsNumber)
                {
                    return TermValue.Undefined;
                }

                return binary.Op switch
                {
                    '+' => new TermValue(l.Number + r.Number, null, false),
                    '-' => new TermValue(l.Number - r.Number, null, false),
                    '*' => new TermValue(l.Number * r.Number, null, false),
                    // Division by zero answers nothing — absent, so `not` does
                    // not turn it into a held clause.
                    '/' => r.Number == 0 ? TermValue.Missing : new TermValue(l.Number / r.Number, null, false),
                    _ => TermValue.Undefined
                };

            default:
                return TermValue.Undefined;
        }
    }

    private static bool Compare(int comparison, WorkflowResultCondition condition) => condition.Ordering switch
    {
        ">" => comparison > 0,
        "<" => comparison < 0,
        ">=" => comparison >= 0,
        "<=" => comparison <= 0,
        _ => condition.Negated ? comparison != 0 : comparison == 0
    };

    /// <summary>The operand's value: the input, or the field a path of any length reaches. False on absence, a null along the way, or the wrong shape.</summary>
    private static bool TryOperandValue(
        WorkflowResultCondition condition,
        IReadOnlyDictionary<string, ConsultInputValue>? suppliedInputs,
        out ConsultInputValue value)
    {
        value = null!;

        if (suppliedInputs is null
            || !suppliedInputs.TryGetValue(condition.InputId, out var input)
            || input.IsBlank)
        {
            return false;
        }

        var current = input;

        foreach (var segment in condition.Segments)
        {
            if (!current.IsObject)
            {
                return false;
            }

            var field = current.Fields!.FirstOrDefault(entry => string.Equals(entry.Id, segment, StringComparison.Ordinal));

            if (field is null || field.Value.IsNull)
            {
                return false;
            }

            current = field.Value;
        }

        value = current;
        return true;
    }

    /// <summary>count(): an absent array counts zero; a present array its elements; anything else answers nothing.</summary>
    private static bool TryCount(
        WorkflowResultCondition condition,
        IReadOnlyDictionary<string, ConsultInputValue>? suppliedInputs,
        out int count)
    {
        count = 0;

        if (!TryOperandValue(condition, suppliedInputs, out var input))
        {
            // Absent at the top counts zero; a path that stops short is absent too.
            return suppliedInputs is null || !suppliedInputs.TryGetValue(condition.InputId, out var top) || top.IsBlank || condition.PathDepth > 0
                ? true
                : false;
        }

        if (!input.IsArray)
        {
            return false;
        }

        count = input.Elements!.Count;
        return true;
    }

    /// <summary>
    /// Why a deliverable did not fire, in the words a reader needs: the operand,
    /// what the condition wanted, and what it found. This sentence reaches
    /// History and the email door's reply, so it prints only what is safe on
    /// every surface: authored content — an enum value, the literal, a
    /// classifier's value — a boolean, and a count of entries. A number, a
    /// date or a field's value is the patient's, and is never printed: for
    /// those the sentence says what was needed and that it was not met.
    /// </summary>
    public static string Explain(
        WorkflowResultCondition condition,
        IReadOnlyDictionary<string, ConsultInputValue>? suppliedInputs,
        IReadOnlyDictionary<string, string>? classifications = null)
    {
        if (condition.IsArithmetic)
        {
            return $"needs {condition.Left!.Text} to be {Wanted(condition, null)}; it is {(Evaluate(condition, suppliedInputs, classifications) == Outcome.Absent ? "not supplied" : "not")}";
        }

        if (condition.IsNodeValue)
        {
            var answered = classifications is not null && classifications.TryGetValue(condition.NodeId!, out var a) ? $"'{a}'" : "not decided";
            return $"needs {condition.Operand} to be {Wanted(condition, null)}; it is {answered}";
        }

        if (condition.IsCount)
        {
            var has = TryCount(condition, suppliedInputs, out var count) ? count.ToString(CultureInfo.InvariantCulture) : "not a list";
            return $"needs {condition.Operand} to be {Wanted(condition, null)}; it is {has}";
        }

        var supplied = TryOperandValue(condition, suppliedInputs, out var value)
            ? Found(condition, value)
            : "not supplied";

        return $"needs {condition.Operand} to be {Wanted(condition, value)}; it is {supplied}";
    }

    /// <summary>
    /// The compound sentence (v10 § 6): one clause reads exactly as v9's did;
    /// several read as what each wanted, then what each found — the same
    /// no-PHI rule per clause.
    /// </summary>
    public static string Explain(
        WorkflowConditionExpression expression,
        IReadOnlyDictionary<string, ConsultInputValue>? suppliedInputs,
        IReadOnlyDictionary<string, string>? classifications = null)
    {
        if (expression.SingleClause is { } single)
        {
            return Explain(single, suppliedInputs, classifications);
        }

        var wanted = WantedText(expression);
        var found = expression.Leaves
            .Select(leaf => Explain(leaf, suppliedInputs, classifications))
            .Select(sentence => sentence[(sentence.IndexOf("; it is ", StringComparison.Ordinal) + "; it is ".Length)..])
            .Zip(expression.Leaves, (has, leaf) => $"{leaf.Operand} is {has}")
            .Distinct()
            .ToList();

        return $"needs ({wanted}); {string.Join(", ", found)}";
    }

    private static string WantedText(WorkflowConditionExpression expression) => expression switch
    {
        WorkflowClauseExpression clause => $"{clause.Clause.Operand} to be {Wanted(clause.Clause, null)}",
        WorkflowNotExpression not => $"not ({WantedText(not.Inner)})",
        WorkflowAndExpression and => $"{Side(and.Left)} and {Side(and.Right)}",
        WorkflowOrExpression or => $"{WantedText(or.Left)} or {WantedText(or.Right)}",
        _ => expression.Text
    };

    private static string Side(WorkflowConditionExpression e) => e is WorkflowOrExpression ? $"({WantedText(e)})" : WantedText(e);

    private static string Wanted(WorkflowResultCondition condition, ConsultInputValue? value)
    {
        if (condition.IsArithmetic)
        {
            return $"{condition.Operator} {condition.Right?.Text}";
        }

        if (condition.IsBare)
        {
            return value?.IsArray == true ? "non-empty" : "true";
        }

        return condition.IsOrdered
            ? $"{condition.Ordering} {condition.Literal}"
            : $"{(condition.Negated ? "not " : string.Empty)}'{condition.Literal}'";
    }

    private static string Found(WorkflowResultCondition condition, ConsultInputValue value)
    {
        if (value.IsArray)
        {
            var entries = value.Elements!.Count;
            return entries == 0 ? "empty" : entries == 1 ? "1 entry" : $"{entries.ToString(CultureInfo.InvariantCulture)} entries";
        }

        if (value.IsBoolean)
        {
            return $"'{value.Canonical}'";
        }

        // A field's value, a number, or a date is the patient's. An enum value
        // is authored — but a date arrives as text too, and the only thing
        // that tells them apart here is its spelling.
        var isPatientData = condition.Field is not null
            || condition.IsOrdered
            || value.IsNumber
            || value.IsStructured
            || DateOnly.TryParseExact(value.Text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

        return isPatientData
            ? "not"
            : value.HasCanonical ? $"'{value.Canonical}'" : value.Described;
    }
}
