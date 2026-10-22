using System.Text;
using Scriban;
using Scriban.Parsing;
using Scriban.Syntax;

namespace Consultologist.PackageFormat;

/// <summary>
/// The v25 template reference walker (package-format-v25-design.md §§ 1–2):
/// parses a template with Scriban and walks its syntax tree, classifying every
/// chain rooted at one of the five objects — <c>input</c>, <c>node</c>,
/// <c>snippet</c>, <c>item</c>, <c>macro</c> — against the closed member sets,
/// typing <c>.value</c> and input paths against their shapes, and returning the
/// reads, the edges and the refusals. Static member access only: a dynamic
/// index on a reserved chain is refused; a literal integer index on an array
/// path is static and steps into the element. Loop, <c>with</c> and
/// <c>capture</c> variables are typed from what they bind, so the closed sets
/// hold inside a folder iteration too. Reads inside a branch count the same.
/// Pipes and functions are opaque here, except the renderer pipes (§ 1,
/// #1058): a renderer renders the <c>.content</c> of a node of its format, and
/// an operand the walk can type is judged against that — another format, a
/// <c>.value</c>, an input's or a snippet's text is refused with the remedy;
/// what the walk cannot type (an assigned or loop variable, a nested
/// expression) is left to the strict render probe, the second net.
/// </summary>
public static partial class TemplateReferenceWalker
{
    /// <summary>
    /// Walks one template. <paramref name="subject"/> opens every sentence
    /// ("Prompt 'assessment'", "Snippet 'standards'", "Macro 'closing'").
    /// </summary>
    public static TemplateReferences Walk(string subject, string templateText, TemplateReferenceContext context)
    {
        var collector = new TemplateReferenceCollector();
        templateText ??= string.Empty;

        // A `{{` never closed swallows the rest of the file as code; Scriban
        // then reports the prose as bad tokens or, if it happens to lex, says
        // nothing at all. Name the line of the `{{` instead.
        if (TryFindUnclosedCode(templateText, out var unclosedLine))
        {
            collector.Errors.Add($"{subject} does not parse at line {unclosedLine}: '{{{{' is not closed; escape a literal '{{{{' as {{{{ \"{{{{\" }}}} or wrap it in {{%{{ … }}%}}.");
            return collector.ToReferences();
        }

        var template = Template.Parse(templateText);

        if (template.HasErrors)
        {
            foreach (var message in template.Messages)
            {
                var at = message.Span.Start.Line >= 0 ? $" at line {message.Span.Start.Line + 1}" : string.Empty;
                collector.Errors.Add($"{subject} does not parse{at}: {message.Message}");
            }

            return collector.ToReferences();
        }

        var visitor = new Visitor(subject, context, collector, text: templateText);
        visitor.Visit(template.Page);
        return collector.ToReferences();
    }

    /// <summary>
    /// #1037 (design § 16): walks one bare expression — a <c>when</c> at 25 — in
    /// condition mode: every chain is resolved and recorded exactly as a prompt's
    /// would be, and the comparisons are typed (an enum's value against its answer
    /// set, <c>item.id</c> against the folder's items, ordering on numbers and dates,
    /// truth on booleans), the ≤24 clause rules re-spelled; the whole expression must
    /// be one a condition can test for truth.
    /// </summary>
    internal static TemplateReferences WalkExpression(string subject, ScriptExpression expression, TemplateReferenceContext context)
    {
        var collector = new TemplateReferenceCollector();
        var visitor = new Visitor(subject, context, collector, condition: true);
        visitor.RequireTruth(visitor.Typed(expression));
        return collector.ToReferences();
    }

    /// <summary>A code block that opens and never closes, by the lexer's own tokens; the line is one-based.</summary>
    internal static bool TryFindUnclosedCode(string templateText, out int line)
    {
        line = 0;
        var open = -1;

        foreach (var token in new Lexer(templateText))
        {
            switch (token.Type)
            {
                case TokenType.CodeEnter:
                    open = token.Start.Line;
                    break;
                case TokenType.CodeExit:
                    open = -1;
                    break;
                case TokenType.Eof when open >= 0:
                    line = open + 1;
                    return true;
            }
        }

        return false;
    }

    // ----- chains -----------------------------------------------------------

    private abstract record Segment;

    private sealed record MemberSegment(string Name) : Segment;

    private sealed record IndexSegment(ScriptExpression Index) : Segment;

    /// <summary>What a chain, or a prefix of one, denotes while the walker steps through it.</summary>
    private abstract record Target;

    /// <summary>A bare object name with no id yet (<c>node</c>).</summary>
    private sealed record ObjectTarget(string Object) : Target;

    /// <summary><c>node.&lt;id&gt;</c>, awaiting <c>.content</c> or <c>.value</c>.</summary>
    private sealed record NodeTarget(string Id, TemplateNodeShape Shape) : Target;

    /// <summary>A snippet — by id, or an item of a folder.</summary>
    private sealed record SnippetTarget(string? Id, string? Folder) : Target;

    /// <summary>A macro — by id, or an item of a folder.</summary>
    private sealed record MacroTarget(string? Id, string? Folder) : Target;

    /// <summary>A snippet or macro folder: iterable, never a value.</summary>
    private sealed record FolderTarget(string Object, string Folder) : Target;

    /// <summary>The fan item, awaiting a member.</summary>
    private sealed record ItemTarget : Target;

    /// <summary><c>run</c> or <c>profile</c>, awaiting a fact name.</summary>
    private sealed record FactsTarget(string Object) : Target;

    /// <summary>
    /// A typed value (null shape = untyped, members pass). Not sealed: a node's
    /// <c>.content</c> and <c>.value</c> are shapes that remember whose they
    /// are (#1058, the renderer pipes); every shape pattern matches them.
    /// </summary>
    private record ShapeTarget(TemplateValueShape? Shape) : Target;

    /// <summary>#1058: <c>node.&lt;id&gt;.content</c> — a text that remembers its node; <c>Path</c> is the chain up to the node ("node.extract"), as the sentences spell it; <c>Id</c> is the node's id, for the rendering record (#1098).</summary>
    private sealed record NodeContentTarget(string Path, string Id, TemplateNodeShape Node) : ShapeTarget(TemplateValueShape.Text);

    /// <summary>#1058: <c>node.&lt;id&gt;.value</c> — a typed value that remembers it is a node's parsed output.</summary>
    private sealed record NodeValueTarget(string Path, TemplateValueShape Value) : ShapeTarget(Value);

    /// <summary>A Scriban builtin, a function result, anything the walker does not check.</summary>
    private sealed record OpaqueTarget : Target;

    /// <summary>A chain already refused; further steps stay silent.</summary>
    private sealed record RefusedTarget : Target;

    /// <summary>
    /// How a chain is used: as a value; as something iterated or piped on (a
    /// folder is fine); as a `with` object (a bare node, snippet, macro or item
    /// is fine); or (#1058) as the operand of a renderer pipe, judged against
    /// the format the pipe renders. A parameter rather than a field so a
    /// nested resolution (an index expression, a function argument) carries
    /// its own use and nothing leaks.
    /// </summary>
    private readonly record struct ChainUse(ChainUseKind Kind, string? Pipe = null)
    {
        public static readonly ChainUse Value = new(ChainUseKind.Value);
        public static readonly ChainUse Iterable = new(ChainUseKind.Iterable);
        public static readonly ChainUse With = new(ChainUseKind.With);

        public static ChainUse Rendered(string pipe) => new(ChainUseKind.Rendered, pipe);
    }

    private enum ChainUseKind { Value, Iterable, With, Rendered }

    private static readonly Target Opaque = new OpaqueTarget();
    private static readonly Target Refused = new RefusedTarget();
    private static readonly Target Untyped = new ShapeTarget(null);

    private sealed class Visitor : ScriptVisitor
    {
        private readonly string _subject;
        private readonly TemplateReferenceContext _context;
        private readonly TemplateReferenceCollector _refs;
        private readonly List<Dictionary<string, Target>> _scopes = new() { new Dictionary<string, Target>(StringComparer.Ordinal) };
        private readonly List<(Target Target, string Text)> _withTargets = new();
        private readonly bool _condition;
        private readonly string _text;

        public Visitor(string subject, TemplateReferenceContext context, TemplateReferenceCollector refs, bool condition = false, string text = "")
        {
            _subject = subject;
            _context = context;
            _refs = refs;
            _condition = condition;
            _text = text;
        }

        // ----- #1088: read occurrences and the if around them --------------------

        /// <summary>
        /// A statement that is one plain object chain is a read occurrence: its block's
        /// span, and the nearest enclosing <c>if</c> — its condition as written, whether
        /// the shape is the canonical one the editor rewrites, and the guard block's span.
        /// </summary>
        private void RecordOccurrence(ScriptExpressionStatement node)
        {
            if (!TryFlatten(node.Expression, out var root, out var segments, out _) || segments.Any(s => s is not MemberSegment))
            {
                return;
            }

            var member = root + string.Concat(segments.Cast<MemberSegment>().Select(s => "." + s.Name));
            var (start, end) = BlockSpan(node);
            var condition = NearestCondition(node);

            // #1091: a read in a case's branch belongs to the case, not to an outer if.
            if (condition is ScriptWhenStatement or ScriptCaseStatement || (condition is ScriptElseStatement && _cases.Count > 0 && condition.Parent is not ScriptIfStatement))
            {
                _refs.ReadOccurrence(new TemplateReadOccurrence(member, start, end, null, false, -1, -1, _cases.Count > 0 ? _cases.Peek() : -1));
                return;
            }

            if (condition is not ScriptIfStatement guard)
            {
                _refs.ReadOccurrence(new TemplateReadOccurrence(member, start, end, null, false, -1, -1));
                return;
            }

            var guardText = guard.Condition is { } c ? Slice(c).Trim() : string.Empty;
            var (guardStart, guardEnd) = BlockSpanToEnd(guard);
            _refs.ReadOccurrence(new TemplateReadOccurrence(member, start, end, guardText, IsCanonical(guard, node), guardStart, guardEnd));
        }

        /// <summary>The nearest enclosing condition statement — an if, a when, a case, or an else (of either).</summary>
        private static ScriptNode? NearestCondition(ScriptNode node)
        {
            for (var parent = node.Parent; parent is not null; parent = parent.Parent)
            {
                if (parent is ScriptIfStatement or ScriptWhenStatement or ScriptCaseStatement or ScriptElseStatement)
                {
                    // An else's owner decides: an if's else is the if's branch; a case's else is the case's.
                    return parent is ScriptElseStatement elseStatement && elseStatement.Parent is ScriptIfStatement owner ? owner : parent;
                }
            }

            return null;
        }

        // ----- #1091: case/when branch blocks --------------------------------------

        private readonly Stack<int> _cases = new();

        public override void Visit(ScriptCaseStatement node)
        {
            var index = _refs.ReserveBranchBlock();
            _cases.Push(index);
            base.Visit(node);
            _cases.Pop();
            _refs.SetBranchBlock(index, BranchBlockOf(node));
        }

        /// <summary>
        /// A case as written: its subject chain, its branches, whether the shape is the
        /// canonical one the editor's row rewrites (a declared enum subject, each when one
        /// string literal among its values and seen once, each body at most one plain read
        /// plus whitespace, the else last), and the whole block's span.
        /// </summary>
        private TemplateBranchBlock BranchBlockOf(ScriptCaseStatement node)
        {
            var canonical = true;
            string subject;
            IReadOnlyList<string>? declared = null;

            if (node.Value is { } value && TryFlatten(value, out var root, out var segments, out _) && segments.All(s => s is MemberSegment))
            {
                var names = segments.Cast<MemberSegment>().Select(s => s.Name).ToList();
                subject = root + string.Concat(names.Select(n => "." + n));
                declared = EnumValuesOf(root, names);
            }
            else
            {
                subject = node.Value is { } v ? Slice(v).Trim() : string.Empty;
            }

            canonical &= declared is not null;

            var branches = new List<TemplateBranch>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            ScriptConditionStatement? current = null;

            foreach (var statement in node.Body?.Statements ?? Enumerable.Empty<ScriptStatement>())
            {
                switch (statement)
                {
                    case ScriptWhenStatement first when current is null:
                        current = first;
                        break;
                    case ScriptEscapeStatement or ScriptEndStatement:
                        break;
                    case ScriptRawStatement raw when string.IsNullOrWhiteSpace(raw.Text.ToString()):
                        break;
                    default:
                        canonical = false;
                        break;
                }
            }

            while (current is not null)
            {
                switch (current)
                {
                    case ScriptWhenStatement when_:
                        string? literal = null;

                        if (when_.Values is { Count: 1 } && when_.Values[0] is ScriptLiteral { Value: string lit })
                        {
                            literal = lit;
                            canonical &= declared is not null && declared.Contains(lit, StringComparer.Ordinal) && seen.Add(lit);
                        }
                        else
                        {
                            literal = when_.Values is { Count: > 0 } ? string.Join(", ", when_.Values.Select(v => Slice(v).Trim())) : string.Empty;
                            canonical = false;
                        }

                        branches.Add(new TemplateBranch(literal, BranchMemberOf(when_.Body, ref canonical)));
                        current = when_.Next;
                        break;
                    case ScriptElseStatement else_:
                        branches.Add(new TemplateBranch(null, BranchMemberOf(else_.Body, ref canonical)));
                        current = null;
                        break;
                    default:
                        canonical = false;
                        current = null;
                        break;
                }
            }

            var (start, end) = BlockSpanToEnd(node);
            return new TemplateBranchBlock(subject, branches, canonical, start, end);
        }

        /// <summary>The one plain read a branch body holds (null when empty); anything else makes the case non-canonical.</summary>
        private static string? BranchMemberOf(ScriptBlockStatement? body, ref bool canonical)
        {
            string? member = null;

            foreach (var statement in body?.Statements ?? Enumerable.Empty<ScriptStatement>())
            {
                switch (statement)
                {
                    case ScriptExpressionStatement read when member is null
                        && TryFlatten(read.Expression, out var root, out var segments, out _) && segments.All(s => s is MemberSegment):
                        member = root + string.Concat(segments.Cast<MemberSegment>().Select(s => "." + s.Name));
                        break;
                    case ScriptEscapeStatement or ScriptEndStatement:
                        break;
                    case ScriptRawStatement raw when string.IsNullOrWhiteSpace(raw.Text.ToString()):
                        break;
                    default:
                        canonical = false;
                        break;
                }
            }

            return member;
        }

        /// <summary>A declared enum's values: an enum input's own, or an enum-format node's answer set read as <c>.value</c>.</summary>
        private IReadOnlyList<string>? EnumValuesOf(string root, IReadOnlyList<string> names)
        {
            if (root == TemplateObjects.Input && names.Count == 1 && _context.Inputs.TryGetValue(names[0], out var input)
                && input.Type == WorkflowInputTypes.Enum && input.Values is { Count: > 0 })
            {
                return input.Values;
            }

            if (root == TemplateObjects.Node && names.Count == 2 && names[1] == TemplateMembers.Value && _context.Nodes.TryGetValue(names[0], out var node)
                && node.Type == WorkflowInputTypes.Enum && node.Values is { Count: > 0 })
            {
                return node.Values;
            }

            return null;
        }

        /// <summary>
        /// The canonical shape: the if's own body holds this read and nothing else but the
        /// delimiters (Scriban keeps <c>{{</c> / <c>}}</c> and <c>end</c> as statements), no
        /// else, not an else-if, whitespace-only prose allowed.
        /// </summary>
        private static bool IsCanonical(ScriptIfStatement guard, ScriptExpressionStatement read) =>
            guard.Else is null
            && !guard.IsElseIf
            && guard.Then is { } then
            && then.Statements.Any(s => ReferenceEquals(s, read))
            && then.Statements.All(s => ReferenceEquals(s, read)
                || s is ScriptEscapeStatement or ScriptEndStatement
                || (s is ScriptRawStatement raw && string.IsNullOrWhiteSpace(raw.Text.ToString())));

        private string Slice(ScriptNode node) =>
            node.Span.End.Offset >= node.Span.Start.Offset && node.Span.End.Offset < _text.Length
                ? _text[node.Span.Start.Offset..(node.Span.End.Offset + 1)]
                : string.Empty;

        /// <summary>The whole <c>{{ … }}</c> block around a node whose span may exclude the delimiters and the whitespace markers.</summary>
        private (int Start, int End) BlockSpan(ScriptNode node)
        {
            var start = Math.Clamp(node.Span.Start.Offset, 0, _text.Length);
            var end = Math.Clamp(node.Span.End.Offset + 1, start, _text.Length);
            return (WidenToOpen(start), WidenToClose(end));
        }

        private int WidenToOpen(int start)
        {
            if (start >= 2 && _text[start - 2] == '{' && _text[start - 1] == '{')
            {
                return start - 2;
            }

            var s = start;

            while (s > 0 && IsSlack(_text[s - 1]))
            {
                s--;
            }

            return s >= 2 && _text[s - 2] == '{' && _text[s - 1] == '{' ? s - 2 : start;
        }

        private int WidenToClose(int end)
        {
            if (end >= 2 && _text[end - 2] == '}' && _text[end - 1] == '}')
            {
                return end;
            }

            var e = end;

            while (e < _text.Length && IsSlack(_text[e]))
            {
                e++;
            }

            return e + 1 < _text.Length && _text[e] == '}' && _text[e + 1] == '}' ? e + 2 : end;
        }

        private static bool IsSlack(char c) => c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '~' || c == '-';

        /// <summary>The whole <c>{{ if … }}…{{ end }}</c> (or <c>case</c>) block: the statement's span widened to its delimiters, then to the <c>end</c> block's close when the span stops short of it.</summary>
        private (int Start, int End) BlockSpanToEnd(ScriptNode guard)
        {
            var (start, end) = BlockSpan(guard);

            if (EndsWithEndBlock(end))
            {
                return (start, end);
            }

            // Scan forward for the next {{ end }} block (markers and slack allowed).
            var from = Math.Max(end, start);

            while (from < _text.Length)
            {
                var open = _text.IndexOf("{{", from, StringComparison.Ordinal);

                if (open < 0)
                {
                    break;
                }

                var close = _text.IndexOf("}}", open + 2, StringComparison.Ordinal);

                if (close < 0)
                {
                    break;
                }

                if (_text[(open + 2)..close].Trim(' ', '\t', '\r', '\n', '~', '-') == "end")
                {
                    return (start, close + 2);
                }

                from = close + 2;
            }

            return (start, end);
        }

        private bool EndsWithEndBlock(int end)
        {
            if (end < 2 || _text[end - 2] != '}' || _text[end - 1] != '}')
            {
                return false;
            }

            var open = _text.LastIndexOf("{{", end - 2, StringComparison.Ordinal);
            return open >= 0 && _text[(open + 2)..(end - 2)].Trim(' ', '\t', '\r', '\n', '~', '-') == "end";
        }

        // ----- #1037: condition mode — comparisons typed -----------------------

        public override void Visit(ScriptBinaryExpression node)
        {
            if (_condition) { Typed(node); } else { base.Visit(node); }
        }

        public override void Visit(ScriptUnaryExpression node)
        {
            if (_condition) { Typed(node); } else { base.Visit(node); }
        }

        /// <summary>What a condition operand is, for the typing sentences.</summary>
        internal sealed record Operand(string Kind, string Text, IReadOnlyList<string>? Values = null, string? Literal = null, bool IsLiteral = false)
        {
            public const string Truth = "truth";
            public const string ItemId = "item_id";
            public const string Any = TemplateValueShape.Unknown;

            public bool IsEnum => Kind == WorkflowInputTypes.Enum;
            public bool IsNumber => Kind == WorkflowInputTypes.Number;
            public bool IsDate => Kind == WorkflowInputTypes.Date;
            public bool IsAny => Kind == Any;

            public string Describe() => Kind switch
            {
                Truth => "a boolean",
                ItemId => "an item id",
                WorkflowInputTypes.Enum => "an enum",
                WorkflowInputTypes.Array => "an array",
                WorkflowInputTypes.Object => "an object",
                _ => $"a {Kind}"
            };
        }

        private static readonly Operand Unknown = new(Operand.Any, "the expression");
        private static readonly Operand Boolean = new(Operand.Truth, "the comparison");

        /// <summary>Resolves and types one operand of a condition; chains are recorded as reads on the way.</summary>
        internal Operand Typed(ScriptExpression? expression)
        {
            switch (expression)
            {
                case null:
                    return Unknown;

                case ScriptNestedExpression nested:
                    return Typed(nested.Expression) with { };

                case ScriptLiteral literal:
                    return literal.Value switch
                    {
                        string s => new Operand(WorkflowInputTypes.Text, $"'{s}'", Literal: s, IsLiteral: true),
                        bool b => new Operand(WorkflowInputTypes.Boolean, b ? "true" : "false", Literal: b ? "true" : "false", IsLiteral: true),
                        null => Unknown,
                        _ => new Operand(WorkflowInputTypes.Number, Convert.ToString(literal.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "", Literal: Convert.ToString(literal.Value, System.Globalization.CultureInfo.InvariantCulture), IsLiteral: true)
                    };

                case ScriptFunctionCall call when IsDateParse(call.Target):
                    foreach (var argument in call.Arguments)
                    {
                        ResolveExpression(argument, ChainUse.Value);
                    }

                    return new Operand(WorkflowInputTypes.Date, Describe(call.Target) + " …", IsLiteral: true);

                case ScriptPipeCall pipe:
                    Visit(pipe);
                    return TryFlatten(pipe.To is ScriptFunctionCall pipeCall ? pipeCall.Target : pipe.To, out var pipeRoot, out var pipeSegments, out _)
                        && pipeRoot == "array" && pipeSegments is [MemberSegment { Name: "size" }]
                        ? new Operand(WorkflowInputTypes.Number, $"({Describe(pipe.From)} | array.size)")
                        : Unknown;

                case ScriptBinaryExpression binary:
                    return TypedBinary(binary);

                case ScriptUnaryExpression unary:
                    return TypedUnary(unary);
            }

            if (TryFlatten(expression, out var root, out var segments, out _))
            {
                var text = Describe(expression);

                if (root == TemplateObjects.Item && segments is [MemberSegment { Name: TemplateMembers.Id }])
                {
                    ResolveExpression(expression, ChainUse.Value);
                    return new Operand(Operand.ItemId, text, _context.FanItemIds);
                }

                return ResolveExpression(expression, ChainUse.Value) switch
                {
                    ShapeTarget { Shape: { } shape } => new Operand(shape.Kind, text, shape.Values),
                    _ => Unknown with { Text = text }
                };
            }

            VisitNode(expression);
            return Unknown;
        }

        private static bool IsDateParse(ScriptExpression? target) =>
            TryFlatten(target, out var root, out var segments, out _) && root == "date" && segments is [MemberSegment { Name: "parse" }];

        private Operand TypedBinary(ScriptBinaryExpression binary)
        {
            switch (binary.Operator)
            {
                case ScriptBinaryOperator.And:
                case ScriptBinaryOperator.Or:
                    RequireTruth(Typed(binary.Left));
                    RequireTruth(Typed(binary.Right));
                    return Boolean;

                case ScriptBinaryOperator.CompareEqual:
                case ScriptBinaryOperator.CompareNotEqual:
                    TypeEquality(Typed(binary.Left), Typed(binary.Right));
                    return Boolean;

                case ScriptBinaryOperator.CompareLess:
                case ScriptBinaryOperator.CompareGreater:
                case ScriptBinaryOperator.CompareLessOrEqual:
                case ScriptBinaryOperator.CompareGreaterOrEqual:
                    TypeOrdering(Typed(binary.Left), Typed(binary.Right), OperatorText(binary.Operator));
                    return Boolean;

                case ScriptBinaryOperator.Add:
                case ScriptBinaryOperator.Subtract:
                case ScriptBinaryOperator.Multiply:
                case ScriptBinaryOperator.Divide:
                case ScriptBinaryOperator.DivideRound:
                case ScriptBinaryOperator.Modulus:
                    var left = Typed(binary.Left);
                    var right = Typed(binary.Right);
                    var dateArithmetic = binary.Operator is ScriptBinaryOperator.Add or ScriptBinaryOperator.Subtract && (left.IsDate || right.IsDate);
                    RequireNumber(left, dateArithmetic);
                    RequireNumber(right, dateArithmetic);
                    return dateArithmetic ? new Operand(WorkflowInputTypes.Date, Describe(binary)) : new Operand(WorkflowInputTypes.Number, Describe(binary));

                default:
                    Typed(binary.Left);
                    Typed(binary.Right);
                    Errors.Add($"{_subject} uses '{OperatorText(binary.Operator)}', which is not a condition operator (&&, ||, !, ==, !=, <, >, <=, >=, + - * /).");
                    return Unknown;
            }
        }

        private Operand TypedUnary(ScriptUnaryExpression unary)
        {
            switch (unary.Operator)
            {
                case ScriptUnaryOperator.Not:
                    RequireTruth(Typed(unary.Right));
                    return Boolean;

                case ScriptUnaryOperator.Negate:
                case ScriptUnaryOperator.Plus:
                    var inner = Typed(unary.Right);
                    RequireNumber(inner, false);
                    return new Operand(WorkflowInputTypes.Number, Describe(unary));

                default:
                    Typed(unary.Right);
                    Errors.Add($"{_subject} uses a unary operator that is not a condition operator (! or -).");
                    return Unknown;
            }
        }

        /// <summary>A condition, a side of && / ||, the operand of ! — something a condition tests for truth.</summary>
        internal void RequireTruth(Operand operand)
        {
            switch (operand.Kind)
            {
                case Operand.Truth:
                case Operand.Any:
                case WorkflowInputTypes.Boolean:
                    return;
                case Operand.ItemId:
                    Errors.Add($"{_subject} tests item.id for truth; compare it to one of the folder's item ids instead.");
                    return;
                case WorkflowInputTypes.Enum:
                    Errors.Add($"{_subject} tests {operand.Text} for truth; compare it to one of its values instead.");
                    return;
                case WorkflowInputTypes.Text:
                    Errors.Add($"{_subject} tests {operand.Text} for truth; a text cannot be tested — compare it to a value.");
                    return;
                case WorkflowInputTypes.Array:
                    Errors.Add($"{_subject} tests {operand.Text} for truth; test its size: ({operand.Text} | array.size) > 0.");
                    return;
                case WorkflowInputTypes.Object:
                    Errors.Add($"{_subject} tests {operand.Text} for truth; compare one of its fields.");
                    return;
                default:
                    Errors.Add($"{_subject} tests {operand.Text} for truth; compare it to {operand.Describe()}.");
                    return;
            }
        }

        private void TypeEquality(Operand left, Operand right)
        {
            // The ≤24 rule, both ways round: a closed set is compared to one of its members.
            foreach (var (side, other) in new[] { (left, right), (right, left) })
            {
                if (!other.IsLiteral)
                {
                    continue;
                }

                switch (side.Kind)
                {
                    case Operand.ItemId when side.Values is { } items && !items.Contains(other.Literal ?? string.Empty, StringComparer.Ordinal):
                        Errors.Add($"{_subject} compares item.id to '{other.Literal}', which the folder does not contain (items: {string.Join(", ", items)}).");
                        return;
                    case WorkflowInputTypes.Enum when side.Values is { } values && !values.Contains(other.Literal ?? string.Empty, StringComparer.Ordinal):
                        Errors.Add($"{_subject} compares {side.Text} to '{other.Literal}', which it does not declare (values: {string.Join(", ", values)}).");
                        return;
                    case WorkflowInputTypes.Number when !other.IsNumber && !other.IsAny:
                        Errors.Add($"{_subject} compares {side.Text} to {other.Text}, which is not a number.");
                        return;
                    case WorkflowInputTypes.Date when !other.IsDate && !other.IsAny:
                        Errors.Add($"{_subject} compares {side.Text} to {other.Text}, which is not a date; write date.parse \"YYYY-MM-DD\".");
                        return;
                    case WorkflowInputTypes.Boolean when other.Kind != WorkflowInputTypes.Boolean && !other.IsAny:
                        Errors.Add($"{_subject} compares {side.Text} to {other.Text}; use true or false.");
                        return;
                }
            }

            foreach (var side in new[] { left, right })
            {
                if (side.Kind is WorkflowInputTypes.Array or WorkflowInputTypes.Object)
                {
                    Errors.Add($"{_subject} compares {side.Text}, which is {side.Describe()}; compare one of its fields, or its size.");
                    return;
                }
            }
        }

        private void TypeOrdering(Operand left, Operand right, string op)
        {
            foreach (var side in new[] { left, right })
            {
                switch (side.Kind)
                {
                    case Operand.ItemId:
                        Errors.Add($"{_subject} compares item.id with {op}; an item id is compared with == or != only.");
                        return;
                    case WorkflowInputTypes.Enum:
                        Errors.Add($"{_subject} compares {side.Text} with {op}; an enum's value is compared with == or != only.");
                        return;
                    case WorkflowInputTypes.Text when !side.IsLiteral:
                    case WorkflowInputTypes.Boolean:
                    case WorkflowInputTypes.Array:
                    case WorkflowInputTypes.Object:
                    case Operand.Truth:
                        Errors.Add($"{_subject} orders {side.Text}, which is {side.Describe()}; ordering operators apply to a number or a date.");
                        return;
                    case WorkflowInputTypes.Text:
                        Errors.Add($"{_subject} orders {side.Text}, a text literal; ordering operators apply to a number or a date.");
                        return;
                }
            }

            if ((left.IsNumber && right.IsDate) || (left.IsDate && right.IsNumber))
            {
                Errors.Add($"{_subject} compares {left.Text} ({left.Describe()}) with {right.Text} ({right.Describe()}); both sides must be numbers or both dates.");
            }
        }

        private void RequireNumber(Operand operand, bool dateAllowed)
        {
            if (operand.IsNumber || operand.IsAny || (dateAllowed && operand.IsDate))
            {
                return;
            }

            Errors.Add($"{_subject} computes with {operand.Text}, which is {operand.Describe()}; arithmetic takes numbers.");
        }

        private static string OperatorText(ScriptBinaryOperator op) => op switch
        {
            ScriptBinaryOperator.CompareEqual => "==",
            ScriptBinaryOperator.CompareNotEqual => "!=",
            ScriptBinaryOperator.CompareLess => "<",
            ScriptBinaryOperator.CompareGreater => ">",
            ScriptBinaryOperator.CompareLessOrEqual => "<=",
            ScriptBinaryOperator.CompareGreaterOrEqual => ">=",
            ScriptBinaryOperator.And => "&&",
            ScriptBinaryOperator.Or => "||",
            _ => op.ToString()
        };

        private List<string> Errors => _refs.Errors;

        // ----- the chain entry points ------------------------------------------

        public override void Visit(ScriptMemberExpression node) => ResolveExpression(node, ChainUse.Value);

        public override void Visit(ScriptIndexerExpression node) => ResolveExpression(node, ChainUse.Value);

        public override void Visit(ScriptVariableGlobal node) => ResolveExpression(node, ChainUse.Value);

        public override void Visit(ScriptExpressionStatement node)
        {
            if (node.Expression is ScriptNamedArgument named)
            {
                // {{input:x}} — the v24 placeholder grammar, which Scriban reads
                // as a named argument. Name it rather than let it pass as noise.
                Errors.Add($"{_subject} carries the placeholder '{{{{{named.Name?.Name}:{named.Value}}}}}'; at specVersion 25 references are objects — input.<id>, node.<id>.content, snippet.<id>.content, item.<member>, macro.<id>.content.");
                return;
            }

            base.Visit(node);

            if (!_condition)
            {
                RecordOccurrence(node);
            }
        }

        public override void Visit(ScriptNamedArgument node) => ResolveExpression(node.Value, ChainUse.Iterable);

        public override void Visit(ScriptObjectMember node) => ResolveExpression(node.Value, ChainUse.Value);

        public override void Visit(ScriptPipeCall node)
        {
            // Scriban parses `x | r` with To = a call of r, and `x | r | g` with
            // To = Pipe(From: call of r, To: call of g) — the destination is the
            // inner From, and the rest is piped on.
            var (destination, rest) = node.To is ScriptPipeCall chained ? (chained.From, chained.To) : (node.To, null);
            var call = destination as ScriptFunctionCall;
            var pipeTarget = call?.Target ?? destination;

            // #1058: a renderer pipe (§ 1: {{ node.x.content | concept_context }}) is the
            // closed set the engine provides, and each renders the text of a node of ONE
            // format — the operand is judged in Finalize under Rendered (C4 #1010 had
            // accepted the pipes as opaque; before that they were refused as an unknown
            // root). A literal cannot be a node's text and is not flattenable, so it is
            // named here.
            if (pipeTarget is ScriptVariable pipe && TemplatePipes.FormatOf(pipe.Name) is not null)
            {
                if (node.From is ScriptLiteral)
                {
                    Errors.Add(NotANodesText(Describe(node.From), pipe.Name));
                }
                else
                {
                    ResolveExpression(node.From, ChainUse.Rendered(pipe.Name));
                }

                if (call is not null)
                {
                    foreach (var argument in call.Arguments)
                    {
                        ResolveExpression(argument, ChainUse.Iterable);
                    }
                }

                VisitNode(rest);
                return;
            }

            // Anything else: the piped value may be a folder (snippet.sections | array.size).
            ResolveExpression(node.From, ChainUse.Iterable);
            VisitNode(node.To);
        }

        public override void Visit(ScriptFunctionCall node)
        {
            ResolveExpression(node.Target, ChainUse.Value);

            foreach (var argument in node.Arguments)
            {
                ResolveExpression(argument, ChainUse.Iterable);
            }
        }

        public override void Visit(ScriptForStatement node)
        {
            var element = ElementOf(node.Iterator);
            var variable = (node.Variable as ScriptVariable)?.Name;

            PushScope();

            if (variable is not null)
            {
                if (TemplateObjects.IsReserved(variable))
                {
                    Errors.Add($"{_subject} names a loop variable '{variable}', a reserved object.");
                }
                else
                {
                    Current[variable] = element;
                }
            }

            VisitNode(node.Body);
            PopScope();
            VisitNode(node.Else);
        }

        public override void Visit(ScriptWithStatement node)
        {
            var target = ResolveExpression(node.Name, ChainUse.With);
            _withTargets.Add((target, Describe(node.Name)));
            PushScope();
            VisitNode(node.Body);
            PopScope();
            _withTargets.RemoveAt(_withTargets.Count - 1);
        }

        public override void Visit(ScriptCaptureStatement node)
        {
            var variable = (node.Target as ScriptVariable)?.Name;

            if (variable is not null)
            {
                if (TemplateObjects.IsReserved(variable))
                {
                    Errors.Add($"{_subject} captures into '{variable}', a reserved object.");
                }
                else
                {
                    Current[variable] = new ShapeTarget(TemplateValueShape.Text);
                }
            }

            VisitNode(node.Body);
        }

        public override void Visit(ScriptAssignExpression node)
        {
            if (TryFlatten(node.Target, out var root, out _, out _) && TemplateObjects.IsReserved(root))
            {
                Errors.Add($"{_subject} assigns to '{root}', a reserved object.");
            }
            else if (node.Target is ScriptVariableGlobal variable)
            {
                Current[variable.Name] = Untyped;
            }
            else
            {
                ResolveExpression(node.Target, ChainUse.Value);
            }

            ResolveExpression(node.Value, ChainUse.Value);
        }

        public override void Visit(ScriptFunction node)
        {
            // A `func` is not part of the grammar: its body is walked for the
            // reads it makes; a call to it resolves like any unknown name.
            PushScope();
            VisitNode(node.Body);
            PopScope();
        }

        // ----- resolution ------------------------------------------------------

        private Dictionary<string, Target> Current => _scopes[^1];

        private void PushScope() => _scopes.Add(new Dictionary<string, Target>(StringComparer.Ordinal));

        private void PopScope() => _scopes.RemoveAt(_scopes.Count - 1);

        private void VisitNode(ScriptNode? node)
        {
            if (node is not null)
            {
                Visit(node);
            }
        }

        private bool TryLookupScope(string name, out Target target)
        {
            for (var i = _scopes.Count - 1; i >= 0; i--)
            {
                if (_scopes[i].TryGetValue(name, out target!))
                {
                    return true;
                }
            }

            target = Opaque;
            return false;
        }

        /// <summary>
        /// Resolves an expression: a chain is classified here in one place; any
        /// other expression is visited so the chains inside it are.
        /// </summary>
        private Target ResolveExpression(ScriptExpression? expression, ChainUse use)
        {
            if (expression is null)
            {
                return Opaque;
            }

            if (!TryFlatten(expression, out var root, out var segments, out var rootNode))
            {
                VisitNode(expression);
                return Opaque;
            }

            return ResolveChain(root, segments, use, rootNode);
        }

        private static bool TryFlatten(ScriptExpression? expression, out string root, out List<Segment> segments, out ScriptVariableGlobal? rootNode)
        {
            segments = new List<Segment>();
            root = string.Empty;
            rootNode = null;
            var current = expression;

            while (true)
            {
                switch (current)
                {
                    case ScriptMemberExpression member when member.Member is not null:
                        segments.Insert(0, new MemberSegment(member.Member.Name));
                        current = member.Target;
                        continue;
                    case ScriptIndexerExpression indexer:
                        // A string literal index is a member spelled the only
                        // way Scriban allows a hyphen: node["identify-problem"].
                        segments.Insert(0, indexer.Index is ScriptLiteral { Value: string name }
                            ? new MemberSegment(name)
                            : new IndexSegment(indexer.Index));
                        current = indexer.Target;
                        continue;
                    case ScriptVariableGlobal variable:
                        root = variable.Name;
                        rootNode = variable;
                        return true;
                    default:
                        return false;
                }
            }
        }

        private Target ResolveChain(string root, List<Segment> segments, ChainUse use, ScriptVariableGlobal rootNode)
        {
            var walked = new StringBuilder(root);
            var target = ResolveRoot(root, segments, walked, out var consumed, rootNode);

            for (var i = consumed; i < segments.Count && target is not RefusedTarget; i++)
            {
                target = Step(target, segments[i], walked);
            }

            return Finalize(target, walked.ToString(), use);
        }

        /// <summary>The root and, for the object roots, the id it needs.</summary>
        private Target ResolveRoot(string root, List<Segment> segments, StringBuilder walked, out int consumed, ScriptVariableGlobal rootNode)
        {
            consumed = 0;

            if (TryLookupScope(root, out var bound))
            {
                return bound;
            }

            // Inside `with x`, a bare name is first a member of x.
            for (var i = _withTargets.Count - 1; i >= 0; i--)
            {
                if (HasMember(_withTargets[i].Target, root))
                {
                    walked.Clear().Append(_withTargets[i].Text);
                    return Step(_withTargets[i].Target, new MemberSegment(root), walked);
                }
            }

            switch (root)
            {
                case TemplateObjects.Input:
                case TemplateObjects.Node:
                case TemplateObjects.Snippet:
                case TemplateObjects.Macro:
                    if (segments.Count == 0 || segments[0] is not MemberSegment first)
                    {
                        if (segments.Count > 0)
                        {
                            Errors.Add($"{_subject} accesses {root} dynamically ({root}[…]); static member access only — name the id: {root}.<id>.");
                        }
                        else
                        {
                            Errors.Add($"{_subject} reads '{root}' bare; name the object: {Example(root)}.");
                        }

                        return Refused;
                    }

                    consumed = 1;
                    walked.Append('.').Append(first.Name);
                    return ResolveObjectId(root, first.Name);

                case TemplateObjects.Item:
                    if (_context.Fan is null && !_context.ItemDeferred)
                    {
                        var member = segments.Count > 0 && segments[0] is MemberSegment m ? $".{m.Name}" : string.Empty;
                        Errors.Add($"{_subject} reads item{member}, but the node does not fan.");
                        return Refused;
                    }

                    return new ItemTarget();

                case TemplateObjects.Run:
                case TemplateObjects.Profile:
                    return new FactsTarget(root);

                case TemplateObjects.RetiredData:
                    Errors.Add($"{_subject} reads 'data'; at specVersion 25 data is 'snippet' — read snippet.<id>.content.");
                    return Refused;

                case "bindings":
                case "variables":
                    Errors.Add($"{_subject} reads '{root}'; at specVersion 25 a template reads objects (input.<id>, node.<id>.content, snippet.<id>.content, item.<member>, macro.<id>.content), and bindings are derived from those reads.");
                    return Refused;
            }

            if (TemplateObjects.ScribanBuiltins.Contains(root))
            {
                return Opaque;
            }

            Errors.Add($"{_subject} reads '{root}', which is not an object (input, node, snippet, item, macro, run, profile), a loop variable, or a Scriban builtin.");
            return Refused;
        }

        private static string Example(string root) => root switch
        {
            TemplateObjects.Input => "input.<id>",
            TemplateObjects.Node => "node.<id>.content or node.<id>.value",
            TemplateObjects.Snippet => "snippet.<id>.content",
            TemplateObjects.Macro => "macro.<id>.content or macro.<id>.prompt",
            _ => root
        };

        private Target ResolveObjectId(string root, string id)
        {
            switch (root)
            {
                case TemplateObjects.Input:
                    if (!_context.Inputs.TryGetValue(id, out var declaration))
                    {
                        Errors.Add($"{_subject} reads input '{id}', which the package does not declare.");
                        return Refused;
                    }

                    _refs.ReadInput(id);
                    return new ShapeTarget(TemplateValueShape.FromDeclaration(declaration));

                case TemplateObjects.Node:
                    if (!_context.Nodes.TryGetValue(id, out var shape))
                    {
                        Errors.Add($"{_subject} reads node '{id}', which the package does not declare.");
                        return Refused;
                    }

                    return new NodeTarget(id, shape);

                case TemplateObjects.Snippet:
                    if (_context.Snippets.Contains(id))
                    {
                        _refs.ReadSnippet(id);
                        return new SnippetTarget(id, null);
                    }

                    if (_context.SnippetFolders.Contains(id))
                    {
                        _refs.ReadSnippetFolder(id);
                        return new FolderTarget(root, id);
                    }

                    Errors.Add($"{_subject} reads snippet '{id}', which the package does not declare.");
                    return Refused;

                default:
                    if (_context.Macros.Contains(id))
                    {
                        return new MacroTarget(id, null);
                    }

                    if (_context.MacroFolders.Contains(id))
                    {
                        _refs.ReadMacroFolder(id);
                        return new FolderTarget(root, id);
                    }

                    Errors.Add($"{_subject} reads macro '{id}', which the package does not declare.");
                    return Refused;
            }
        }

        private static bool HasMember(Target target, string name) => target switch
        {
            SnippetTarget => TemplateMembers.Snippet.Contains(name, StringComparer.Ordinal),
            MacroTarget => TemplateMembers.Macro.Contains(name, StringComparer.Ordinal),
            NodeTarget => TemplateMembers.Node.Contains(name, StringComparer.Ordinal),
            FactsTarget { Object: TemplateObjects.Run } => TemplateMembers.Run.Contains(name, StringComparer.Ordinal),
            FactsTarget { Object: TemplateObjects.Profile } => TemplateMembers.Profile.Contains(name, StringComparer.Ordinal),
            ShapeTarget { Shape: { IsObject: true } shape } => shape.Fields?.ContainsKey(name) ?? false,
            _ => false
        };

        /// <summary>One segment applied to a target; <paramref name="walked"/> is the chain so far, for sentences.</summary>
        private Target Step(Target target, Segment segment, StringBuilder walked)
        {
            var before = walked.ToString();

            if (segment is IndexSegment index)
            {
                return StepIndex(target, index, walked, before);
            }

            var name = ((MemberSegment)segment).Name;
            walked.Append('.').Append(name);

            switch (target)
            {
                case RefusedTarget:
                    return Refused;

                case OpaqueTarget:
                    return Opaque;

                case NodeTarget node:
                    return StepNode(node, name, before);

                case SnippetTarget snippet:
                    if (!TemplateMembers.Snippet.Contains(name, StringComparer.Ordinal))
                    {
                        Errors.Add($"{_subject} reads {walked}, which is not a member (a snippet has .id, .name and .content).");
                        return Refused;
                    }

                    if (snippet.Folder is not null)
                    {
                        _refs.ReadSnippetFolder(snippet.Folder);
                    }

                    return new ShapeTarget(TemplateValueShape.Text);

                case MacroTarget macro:
                    if (!TemplateMembers.Macro.Contains(name, StringComparer.Ordinal))
                    {
                        Errors.Add($"{_subject} reads {walked}, which is not a member (a macro has .id, .name, .content and .prompt).");
                        return Refused;
                    }

                    if (macro.Id is not null)
                    {
                        if (name == TemplateMembers.Content)
                        {
                            _refs.ReadMacroContent(macro.Id);
                        }
                        else if (name == TemplateMembers.Prompt)
                        {
                            _refs.ReadMacroPrompt(macro.Id);
                        }
                    }

                    return new ShapeTarget(TemplateValueShape.Text);

                case FolderTarget folder:
                    Errors.Add($"{_subject} reads {walked}; a folder is iterated: {{{{ for s in {folder.Object}.{folder.Folder} }}}}.");
                    return Refused;

                case ItemTarget:
                    return StepItem(name, walked);

                case FactsTarget facts:
                    return StepFacts(facts.Object, name, walked);

                case ObjectTarget:
                    return Refused;

                case ShapeTarget { Shape: null }:
                    return Untyped;

                case ShapeTarget { Shape: { } shape }:
                    return StepShape(shape, name, before, walked);

                default:
                    return Opaque;
            }
        }

        private Target StepNode(NodeTarget node, string name, string before)
        {
            if (name == TemplateMembers.Content)
            {
                // Recorded before any judgement: a refused pipe still names its edge.
                // The bare-read warning is Finalize's (#1058) — it knows the use.
                _refs.ReadNode(node.Id, content: true, value: false);
                return new NodeContentTarget(before, node.Id, node.Shape);
            }

            if (name == TemplateMembers.Value)
            {
                if (node.Shape.Aggregates)
                {
                    // #1065: an aggregator's text is its composed sections — nothing parses it.
                    Errors.Add($"{_subject} reads {before}.value, but node '{node.Id}' aggregates — its composed text has no value; read {before}.content.");
                    return Refused;
                }

                if (!node.Shape.HasValue)
                {
                    Errors.Add($"{_subject} reads {before}.value, but format '{node.Shape.Format}' is text and has no value; read {before}.content.");
                    return Refused;
                }

                _refs.ReadNode(node.Id, content: false, value: true);
                return new NodeValueTarget(before, node.Shape.Type == WorkflowInputTypes.Enum && node.Shape.Schema is null
                    ? new TemplateValueShape(WorkflowInputTypes.Enum, null, null, node.Shape.Values)
                    : TemplateValueShape.FromSchema(node.Shape.Schema));
            }

            Errors.Add($"{_subject} reads {before}.{name}, which is not a member (node.<id> has .content and .value).");
            return Refused;
        }

        private Target StepItem(string name, StringBuilder walked)
        {
            switch (_context.Fan)
            {
                case SnippetFolderFan:
                    if (!TemplateMembers.SnippetFanItem.Contains(name, StringComparer.Ordinal))
                    {
                        Errors.Add($"{_subject} reads {walked}, which a snippet fan item does not carry (it carries: id, name, content).");
                        return Refused;
                    }

                    _refs.ReadItem(name);
                    return new ShapeTarget(TemplateValueShape.Text);

                case InputArrayFan input:
                    if (!TemplateMembers.InputFanItem.Contains(name, StringComparer.Ordinal))
                    {
                        Errors.Add($"{_subject} reads {walked}, which an input fan item does not carry (it carries: id, name, value).");
                        return Refused;
                    }

                    _refs.ReadItem(name);
                    return new ShapeTarget(name == TemplateMembers.Value
                        ? TemplateValueShape.FromDeclaration(input.Element)
                        : TemplateValueShape.Text);

                default:
                    // C6 (#1012): a snippet or macro text, walked with no fan of its own —
                    // any fan item member, typed loosely; the reading node's fan decides.
                    if (_context.ItemDeferred)
                    {
                        if (!TemplateMembers.AnyFanItem.Contains(name, StringComparer.Ordinal))
                        {
                            Errors.Add($"{_subject} reads {walked}, which a fan item does not carry (it carries: id, name, content or value).");
                            return Refused;
                        }

                        _refs.ReadItem(name);
                        return new ShapeTarget(name == TemplateMembers.Value
                            ? new TemplateValueShape(TemplateValueShape.Unknown, null, null, null)
                            : TemplateValueShape.Text);
                    }

                    return Refused;
            }
        }

        private Target StepFacts(string obj, string name, StringBuilder walked)
        {
            var members = obj == TemplateObjects.Run ? TemplateMembers.Run : TemplateMembers.Profile;

            if (!members.Contains(name, StringComparer.Ordinal))
            {
                Errors.Add($"{_subject} reads {walked}, which is not a member ({obj} has {Describe(members)}).");
                return Refused;
            }

            if (obj == TemplateObjects.Run)
            {
                _refs.ReadRunFact(name);
            }
            else
            {
                _refs.ReadProfileFact(name);
            }

            return new ShapeTarget(TemplateValueShape.Text);

            static string Describe(IReadOnlyList<string> members) =>
                string.Join(", ", members.Take(members.Count - 1).Select(m => "." + m)) + " and ." + members[^1];
        }

        private Target StepShape(TemplateValueShape shape, string name, string before, StringBuilder walked)
        {
            if (shape.IsUnknown)
            {
                return Untyped;
            }

            if (!shape.IsObject)
            {
                Errors.Add($"{_subject} reads field '{name}' of '{before}', which is {shape.Describe()}, not an object.");
                return Refused;
            }

            if (shape.Fields is null || !shape.Fields.TryGetValue(name, out var field))
            {
                Errors.Add($"{_subject} reads field '{name}' of '{before}', which it does not declare (fields: {string.Join(", ", shape.Fields?.Keys ?? Enumerable.Empty<string>())}).");
                return Refused;
            }

            return new ShapeTarget(field);
        }

        private Target StepIndex(Target target, IndexSegment index, StringBuilder walked, string before)
        {
            var literal = index.Index is ScriptLiteral { Value: int or long } lit ? lit.Value : null;
            walked.Append('[').Append(literal?.ToString() ?? "…").Append(']');

            switch (target)
            {
                case RefusedTarget:
                    return Refused;

                case OpaqueTarget:
                    VisitNode(index.Index);
                    return Opaque;

                default:
                    if (literal is null)
                    {
                        Errors.Add($"{_subject} accesses {before} dynamically ({before}[…]); static member access only.");
                        VisitNode(index.Index);
                        return Refused;
                    }

                    if (target is ShapeTarget { Shape: null } or ShapeTarget { Shape.IsUnknown: true })
                    {
                        return Untyped;
                    }

                    if (target is ShapeTarget { Shape: { IsArray: true } array })
                    {
                        return array.Items is null ? Untyped : new ShapeTarget(array.Items);
                    }

                    if (target is FolderTarget folder)
                    {
                        return folder.Object == TemplateObjects.Snippet
                            ? new SnippetTarget(null, folder.Folder)
                            : new MacroTarget(null, folder.Folder);
                    }

                    Errors.Add($"{_subject} indexes {before}, which is not an array.");
                    return Refused;
            }
        }

        /// <summary>A chain's last word: a node needs .content or .value, a snippet or macro a member, a folder a loop; a renderer pipe's operand is judged against the pipe (#1058).</summary>
        private Target Finalize(Target target, string walked, ChainUse use)
        {
            if (use.Kind == ChainUseKind.Rendered)
            {
                return FinalizeRendered(target, walked, use.Pipe!);
            }

            // #1058: a shaped node's text read bare — the JSON renders as text. (The
            // operand of a renderer pipe is judged above instead; a chain that steps
            // past .content is refused by the step and not also warned about.)
            if (target is NodeContentTarget { Node.HasValue: true } content)
            {
                _refs.Warnings.Add($"{_subject} reads {content.Path}.content of format '{content.Node.Format}' ({content.Node.Type}); the JSON renders as text — read {content.Path}.value for the fields.");
            }

            if (use == ChainUse.With && target is NodeTarget or SnippetTarget or MacroTarget or ItemTarget or FactsTarget)
            {
                return target;
            }

            switch (target)
            {
                case NodeTarget node:
                    Errors.Add($"{_subject} reads node '{node.Id}' bare; read {walked}.content (its composed output) or {walked}.value.");
                    return Refused;

                case SnippetTarget { Id: { } id }:
                    Errors.Add($"{_subject} reads snippet '{id}' bare; read {walked}.content.");
                    return Refused;

                case MacroTarget { Id: { } id }:
                    Errors.Add($"{_subject} reads macro '{id}' bare; read {walked}.content or {walked}.prompt.");
                    return Refused;

                case FactsTarget facts:
                    Errors.Add($"{_subject} reads '{facts.Object}' bare; name the fact: {facts.Object}.{string.Join(" or " + facts.Object + ".", facts.Object == TemplateObjects.Run ? TemplateMembers.Run : TemplateMembers.Profile)}.");
                    return Refused;

                case ItemTarget:
                    Errors.Add($"{_subject} reads item bare; read a member ({(_context.Fan is InputArrayFan ? "item.id, item.name, item.value" : "item.id, item.name, item.content")}).");
                    return Refused;

                case FolderTarget folder when use == ChainUse.Value:
                    Errors.Add($"{_subject} reads {walked} as a value; a folder is iterated: {{{{ for s in {folder.Object}.{folder.Folder} }}}}.");
                    return Refused;

                default:
                    return target;
            }
        }

        /// <summary>
        /// #1058: the operand of a renderer pipe — a node's text of the pipe's
        /// format passes silently; anything else the walk can type is refused
        /// with the remedy; what it cannot type (an assigned or loop variable,
        /// a builtin's result, a nested expression) is judged when it renders
        /// (TemplateRenderer.ApplyPipe), as before.
        /// </summary>
        private Target FinalizeRendered(Target target, string walked, string pipe)
        {
            var format = TemplatePipes.FormatOf(pipe)!;

            switch (target)
            {
                case RefusedTarget:
                    return Refused;

                case NodeContentTarget content when string.Equals(content.Node.Format, format, StringComparison.Ordinal):
                    // #1098: the accepted rendering — the editor's "Used by" row for the pipe.
                    _refs.ReadRendered(content.Id, pipe);
                    return content;

                case NodeContentTarget { Node.Aggregates: true } content:
                    // #1065: composed sections are no format's text.
                    Errors.Add($"{_subject} pipes {walked} through {pipe}, but {content.Path} aggregates; {pipe} renders a {format} node's text (node.<id>.content).");
                    return Refused;

                case NodeContentTarget { Node.HasValue: false } content:
                    Errors.Add($"{_subject} pipes {walked} through {pipe}, but format '{content.Node.Format}' is text; {pipe} renders a {format} node's text (node.<id>.content).");
                    return Refused;

                case NodeContentTarget content:
                    Errors.Add($"{_subject} pipes {walked} through {pipe}, but format '{content.Node.Format}' ({content.Node.Type}) is not {format}; {pipe} renders a {format} node's text (node.<id>.content).");
                    return Refused;

                case NodeValueTarget value:
                    Errors.Add($"{_subject} pipes {walked} through {pipe}; a renderer pipe renders a node's text — pipe {value.Path}.content.");
                    return Refused;

                case ShapeTarget { Shape: { } }:
                case FolderTarget:
                    Errors.Add(NotANodesText(walked, pipe));
                    return Refused;

                default:
                    return Finalize(target, walked, ChainUse.Value);
            }
        }

        private string NotANodesText(string what, string pipe) =>
            $"{_subject} pipes {what} through {pipe}; a renderer pipe renders a node's text (node.<id>.content).";

        /// <summary>What a loop variable is bound to, from the iterated expression.</summary>
        private Target ElementOf(ScriptExpression? iterator)
        {
            var target = ResolveExpression(iterator, ChainUse.Iterable);

            switch (target)
            {
                case FolderTarget folder:
                    return folder.Object == TemplateObjects.Snippet
                        ? new SnippetTarget(null, folder.Folder)
                        : new MacroTarget(null, folder.Folder);

                case ShapeTarget { Shape: { IsArray: true } array }:
                    return array.Items is null ? Untyped : new ShapeTarget(array.Items);

                case ShapeTarget { Shape: null } or ShapeTarget { Shape.IsUnknown: true }:
                    return Untyped;

                case ShapeTarget { Shape: { } shape }:
                    Errors.Add($"{_subject} iterates {Describe(iterator)}, which is {shape.Describe()}, not an array.");
                    return Refused;

                default:
                    return Opaque;
            }
        }

        private static string Describe(ScriptExpression? expression) =>
            TryFlatten(expression, out var root, out var segments, out _)
                ? root + string.Concat(segments.Select(s => s is MemberSegment m ? "." + m.Name : "[…]"))
                : expression is ScriptNestedExpression nested ? $"({Describe(nested.Expression)})"
                : expression is ScriptBinaryExpression binary ? $"{Describe(binary.Left)} {OperatorText(binary.Operator)} {Describe(binary.Right)}"
                : expression is ScriptUnaryExpression unary ? $"{(unary.Operator == ScriptUnaryOperator.Not ? "!" : "-")}{Describe(unary.Right)}"
                : expression is ScriptLiteral { Value: string s } ? $"'{s}'"
                : expression is ScriptLiteral literal ? Convert.ToString(literal.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "the expression"
                : "the expression";
    }
}
