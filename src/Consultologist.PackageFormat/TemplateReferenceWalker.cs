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
    /// <summary>#1100: EdgeId is the node a read is recorded under — a body node reached through a loop's element records the loop.</summary>
private sealed record NodeTarget(string Id, TemplateNodeShape Shape, string? EdgeId = null) : Target
{
    public string ReadId => EdgeId ?? Id;
}

    /// <summary>A snippet — by id, or an item of a folder.</summary>
    private sealed record SnippetTarget(string? Id, string? Folder) : Target;

    /// <summary>A macro — by id, or an item of a folder.</summary>
    private sealed record MacroTarget(string? Id, string? Folder) : Target;

    /// <summary>A snippet or macro folder: iterable, never a value.</summary>
    private sealed record FolderTarget(string Object, string Folder) : Target;

    /// <summary>The fan item, awaiting a member.</summary>
    /// <summary>#1100: a loop element's <c>item</c> carries the loop's fan; the root <c>item</c> reads the context's.</summary>
private sealed record ItemTarget(TemplateFan? Fan = null) : Target;

/// <summary>#1100 (v26 design § 1.3): one element of a loop — <c>item</c>, the returned node's <c>content</c>/<c>value</c>, and every body node by id.</summary>
private sealed record LoopElementTarget(string LoopId, TemplateFlowShape Flow) : Target;

    /// <summary><c>run</c> or <c>profile</c>, awaiting a fact name.</summary>
    private sealed record FactsTarget(string Object) : Target;

/// <summary>#1101 (v26 design § 3.1): a deliverable — its composed text, <c>.content</c>, and nothing else.</summary>
private sealed record DeliverableTarget(string Id) : Target;

    /// <summary>
    /// A typed value (null shape = untyped, members pass). Not sealed: a node's
    /// <c>.content</c> and <c>.value</c> are shapes that remember whose they
    /// are (#1058, the renderer pipes); every shape pattern matches them.
    /// </summary>
    private record ShapeTarget(TemplateValueShape? Shape) : Target;

    /// <summary>#1058: <c>node.&lt;id&gt;.content</c> — a text that remembers its node; <c>Path</c> is the chain up to the node ("node.extract"), as the sentences spell it; <c>Id</c> is the node's id, for the rendering record (#1098).</summary>
    // #1059: ElementRet — set for a loop element's .content (the returned body node's id), so the 26
    // replacement names the node to pipe (e.<ret> | pipe).
    private sealed record NodeContentTarget(string Path, string Id, TemplateNodeShape Node, string? ElementRet = null) : ShapeTarget(TemplateValueShape.Text);

    /// <summary>#1058: <c>node.&lt;id&gt;.value</c> — a typed value that remembers it is a node's parsed output.</summary>
    private sealed record NodeValueTarget(string Path, TemplateValueShape Value) : ShapeTarget(Value);

/// <summary>#1101: a deliverable's composed text read — text; a renderer pipe is refused (composed sections are no format's text).</summary>
private sealed record DeliverableContentTarget(string Path, string Id) : ShapeTarget(TemplateValueShape.Text);

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
    /// its own use and nothing leaks. #1138: a rendered use carries the if
    /// around the pipe, for the rendered fact.
    /// </summary>
    private readonly record struct ChainUse(ChainUseKind Kind, string? Pipe = null, string? Guard = null, bool GuardCanonical = false)
    {
        public static readonly ChainUse Value = new(ChainUseKind.Value);
        public static readonly ChainUse Iterable = new(ChainUseKind.Iterable);
        public static readonly ChainUse With = new(ChainUseKind.With);

        public static ChainUse Rendered(string pipe, string? guard, bool guardCanonical) => new(ChainUseKind.Rendered, pipe, guard, guardCanonical);
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

            // #1121: a read in a canonical if … else belongs to the if-block — the then-read keeps the
            // condition as its (canonical) guard over the whole block, the else-read has none; both index it.
            if (_ifBlocks.TryGetValue(guard, out var ifIndex))
            {
                var inElse = InElseOf(node, guard);
                _refs.ReadOccurrence(inElse
                    ? new TemplateReadOccurrence(member, start, end, null, false, -1, -1, ifIndex)
                    : new TemplateReadOccurrence(member, start, end, guardText, true, guardStart, guardEnd, ifIndex));
                return;
            }

            _refs.ReadOccurrence(new TemplateReadOccurrence(member, start, end, guardText, IsCanonical(guard, node), guardStart, guardEnd));
        }

        /// <summary>
        /// #1138: the if around a piped read, as RecordOccurrence records it for a plain one — the
        /// nearest enclosing condition when it is an if (an else's owner; a when or a case's else is
        /// no guard), its condition as written, and canonical when the pipe's own statement is the
        /// if's only content (IsCanonical). A canonical if … else never holds a pipe (BranchMemberOf
        /// makes it non-canonical), so RecordOccurrence's if-block branch does not arise here.
        /// </summary>
        private (string? Guard, bool Canonical) GuardOf(ScriptPipeCall pipe)
        {
            if (NearestCondition(pipe) is not ScriptIfStatement guard)
            {
                return (null, false);
            }

            var guardText = guard.Condition is { } c ? Slice(c).Trim() : string.Empty;
            return (guardText, pipe.Parent is ScriptExpressionStatement read && IsCanonical(guard, read));
        }

        /// <summary>Does the read sit in the if's else (an else statement between the read and its owning if)?</summary>
        private static bool InElseOf(ScriptNode read, ScriptIfStatement owner)
        {
            for (var parent = read.Parent; parent is not null && !ReferenceEquals(parent, owner); parent = parent.Parent)
            {
                if (parent is ScriptElseStatement elseStatement && ReferenceEquals(elseStatement.Parent, owner))
                {
                    return true;
                }
            }

            return false;
        }

        // ----- #1121: if … else branch blocks -------------------------------------

        /// <summary>The canonical if … else statements being walked, by the slot their reads index.</summary>
        private readonly Dictionary<ScriptIfStatement, int> _ifBlocks = new(ReferenceEqualityComparer.Instance);

        public override void Visit(ScriptIfStatement node)
        {
            if (IfBlockOf(node) is not { } block)
            {
                base.Visit(node);
                return;
            }

            var index = _refs.ReserveBranchBlock();
            _ifBlocks[node] = index;
            base.Visit(node);
            _refs.SetBranchBlock(index, block);
        }

        /// <summary>
        /// An if … else as a two-branch block when canonical: a top-level if (not an else-if) whose else is a
        /// plain else, each body at most one plain read plus whitespace, at least one read between them.
        /// Anything else — no else, an else-if, prose, a nested statement — is no block, and its reads keep
        /// #1088's guard (non-canonical when the else exists).
        /// </summary>
        private TemplateBranchBlock? IfBlockOf(ScriptIfStatement node)
        {
            if (node.IsElseIf || node.Condition is not { } condition || node.Then is not { } then || node.Else is not ScriptElseStatement else_)
            {
                return null;
            }

            var canonical = true;
            var thenMember = BranchMemberOf(then, ref canonical);
            var elseMember = BranchMemberOf(else_.Body, ref canonical);

            if (!canonical || (thenMember is null && elseMember is null))
            {
                return null;
            }

            var subject = Slice(condition).Trim();
            var (start, end) = BlockSpanToEnd(node);
            return new TemplateBranchBlock(
                subject,
                new[] { new TemplateBranch(subject, thenMember), new TemplateBranch(null, elseMember) },
                true,
                start,
                end,
                TemplateBranchKind.If);
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

                            // #1122: a when the enum never answers — a branch that never runs.
                            if (declared is not null && !declared.Contains(lit, StringComparer.Ordinal))
                            {
                                WarnUndeclaredLiteral(subject, lit, declared, "that branch never runs");
                            }
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
            if (_condition)
            {
                Typed(node);
                return;
            }

            base.Visit(node);
            WarnUndeclaredComparison(node);
        }

        // ----- #1122: a literal outside an enum's declared values ----------------

        /// <summary>
        /// A template's <c>==</c> / <c>!=</c> between an enum side (an enum input, an enum-format node's
        /// <c>.value</c>) and a string literal the enum does not declare — a warning, since a template's
        /// if is prose (a manifest when with the same literal is refused by the condition walk).
        /// </summary>
        private void WarnUndeclaredComparison(ScriptBinaryExpression node)
        {
            if (node.Operator is not (ScriptBinaryOperator.CompareEqual or ScriptBinaryOperator.CompareNotEqual))
            {
                return;
            }

            foreach (var (side, other) in new[] { (node.Left, node.Right), (node.Right, node.Left) })
            {
                if (other is ScriptLiteral { Value: string literal } && EnumSideOf(side) is var (chain, values)
                    && !values.Contains(literal, StringComparer.Ordinal))
                {
                    WarnUndeclaredLiteral(chain, literal, values,
                        node.Operator == ScriptBinaryOperator.CompareEqual ? "that branch never runs" : "that test is always true");
                    return;
                }
            }
        }

        private (string Chain, IReadOnlyList<string> Values)? EnumSideOf(ScriptExpression? side)
        {
            if (!TryFlatten(side, out var root, out var segments, out _) || !segments.All(s => s is MemberSegment))
            {
                return null;
            }

            var names = segments.Cast<MemberSegment>().Select(s => s.Name).ToList();
            return EnumValuesOf(root, names) is { } values ? (root + string.Concat(names.Select(n => "." + n)), values) : null;
        }

        private void WarnUndeclaredLiteral(string chain, string literal, IReadOnlyList<string> values, string consequence) =>
            _refs.Warnings.Add($"{_subject} compares {chain} to '{literal}', which it does not declare (values: {string.Join(", ", values)}); {consequence}.");

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
                    var (guard, guardCanonical) = GuardOf(node);
                    ResolveExpression(node.From, ChainUse.Rendered(pipe.Name, guard, guardCanonical));
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
                // #1101: the sixth object exists where the context carries deliverables
                // (26); below, the word is no object and meets the sentence below.
                case TemplateObjects.Deliverable when _context.Deliverables is null:
                    break;

                case TemplateObjects.Deliverable when _condition:
                    Errors.Add($"{_subject} reads {root}{(segments.Count > 0 && segments[0] is MemberSegment d ? "." + d.Name : string.Empty)}; a condition decides before the sections compose — read a scalar node.");
                    return Refused;

                case TemplateObjects.Input:
                case TemplateObjects.Node:
                case TemplateObjects.Snippet:
                case TemplateObjects.Macro:
                case TemplateObjects.Deliverable:
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
            TemplateObjects.Deliverable => "deliverable.<id>.content",
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

                case TemplateObjects.Deliverable:
                    if (_context.Deliverables is null || !_context.Deliverables.Contains(id))
                    {
                        Errors.Add($"{_subject} reads deliverable '{id}', which the package does not declare.");
                        return Refused;
                    }

                    return new DeliverableTarget(id);

                case TemplateObjects.Node:
                    // #1100 (v26 design § 1.2): a body node is read by bare id only from
                    // inside its body — the flow node is the only name the outside sees.
                    // A deferred text (a snippet, a macro) is judged at its reading node.
                    if (!_context.ItemDeferred
                        && _context.Bodies is { } bodies
                        && bodies.TryGetValue(id, out var scope)
                        && !InScope(scope.Owner, bodies))
                    {
                        Errors.Add($"{_subject} reads node '{id}', which runs inside {scope.Inside}; read node.{scope.Owner}.");
                        return Refused;
                    }

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
                        // #1116: the folder is recorded where it is iterated (ElementOf, an
                        // index) — a direct item read records the item alone.
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
                        return new FolderTarget(root, id);
                    }

                    Errors.Add($"{_subject} reads macro '{id}', which the package does not declare.");
                    return Refused;
            }
        }

        private bool HasMember(Target target, string name) => target switch
        {
            SnippetTarget => SnippetMembers.Contains(name, StringComparer.Ordinal),
            MacroTarget => MacroMembers.Contains(name, StringComparer.Ordinal),
            NodeTarget => TemplateMembers.Node.Contains(name, StringComparer.Ordinal),
            DeliverableTarget => TemplateMembers.Deliverable.Contains(name, StringComparer.Ordinal),
            LoopElementTarget element => TemplateMembers.LoopElement.Contains(name, StringComparer.Ordinal) || (element.Flow.Body?.ContainsKey(name) ?? false),
            FactsTarget { Object: TemplateObjects.Run } => TemplateMembers.Run.Contains(name, StringComparer.Ordinal),
            FactsTarget { Object: TemplateObjects.Profile } => TemplateMembers.Profile.Contains(name, StringComparer.Ordinal),
            ShapeTarget { Shape: { IsObject: true } shape } => shape.Fields?.ContainsKey(name) ?? false,
            _ => false
        };

        // #1105 (v26 design § 5.2): the member sets by version — at 26 .label for .name, a macro's .instruction.
        private string NameMember => _context.Labelled ? TemplateMembers.Label : TemplateMembers.Name;
        private IReadOnlyList<string> SnippetMembers => _context.Labelled ? TemplateMembers.SnippetAt26 : TemplateMembers.Snippet;
        private IReadOnlyList<string> MacroMembers => _context.Labelled ? TemplateMembers.MacroAt26 : TemplateMembers.Macro;
        private IReadOnlyList<string> SnippetFanItemMembers => _context.Labelled ? TemplateMembers.SnippetFanItemAt26 : TemplateMembers.SnippetFanItem;
        private IReadOnlyList<string> InputFanItemMembers => _context.Labelled ? TemplateMembers.InputFanItemAt26 : TemplateMembers.InputFanItem;
        private IReadOnlyList<string> AnyFanItemMembers => _context.Labelled ? TemplateMembers.AnyFanItemAt26 : TemplateMembers.AnyFanItem;

        /// <summary>At 26 a .name read is refused with its replacement before the member sentence.</summary>
        private bool RefusesName(string name, string walked)
        {
            if (_context.Labelled && name == TemplateMembers.Name)
            {
                Errors.Add($"{_subject} reads {walked}; at specVersion 26 the member is .label.");
                return true;
            }

            return false;
        }

        private static string Listed(IReadOnlyList<string> members) =>
            members.Count <= 2 ? string.Join(" and ", members.Select(m => "." + m)) : string.Join(", ", members.Take(members.Count - 1).Select(m => "." + m)) + " and ." + members[^1];

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
                    if (RefusesName(name, walked.ToString()))
                    {
                        return Refused;
                    }

                    if (!SnippetMembers.Contains(name, StringComparer.Ordinal))
                    {
                        Errors.Add($"{_subject} reads {walked}, which is not a member (a snippet has {Listed(SnippetMembers)}).");
                        return Refused;
                    }

                    if (snippet.Folder is not null)
                    {
                        _refs.ReadSnippetFolder(snippet.Folder);
                    }

                    return new ShapeTarget(TemplateValueShape.Text);

                case MacroTarget macro:
                    if (RefusesName(name, walked.ToString()))
                    {
                        return Refused;
                    }

                    if (!MacroMembers.Contains(name, StringComparer.Ordinal))
                    {
                        Errors.Add($"{_subject} reads {walked}, which is not a member (a macro has {Listed(MacroMembers)}).");
                        return Refused;
                    }

                    if (macro.Id is not null)
                    {
                        if (name == TemplateMembers.Content)
                        {
                            _refs.ReadMacroContent(macro.Id);
                        }
                        else if (name == TemplateMembers.Prompt || name == TemplateMembers.Instruction)
                        {
                            // #1105: the instruction is what .prompt is told — the same read, the same edge.
                            _refs.ReadMacroPrompt(macro.Id);
                        }
                    }
                    else if (macro.Folder is not null)
                    {
                        // #1099 (v26 design § 5.3): a folder element's read is a read of
                        // every item — the walk expands it to the folder's items.
                        if (name == TemplateMembers.Content)
                        {
                            _refs.ReadMacroFolderContent(macro.Folder);
                        }
                        else if (name == TemplateMembers.Prompt || name == TemplateMembers.Instruction)
                        {
                            _refs.ReadMacroFolderPrompt(macro.Folder);
                        }
                    }

                    return new ShapeTarget(TemplateValueShape.Text);

                case FolderTarget folder:
                    // #1116 (v26 design § 5.4): at 26 a declared item is a member of its
                    // folder, read directly with the folder's member set and recorded
                    // under the item's key — an edge, a reach, an inline read like the
                    // scalar's. Below 26 a folder is iterated only.
                    var folderItems = folder.Object == TemplateObjects.Snippet ? _context.SnippetFolderItems : _context.MacroFolderItems;
                    if (_context.Labelled && folderItems is not null && folderItems.TryGetValue(folder.Folder, out var itemIds))
                    {
                        if (!itemIds.Contains(name, StringComparer.Ordinal))
                        {
                            Errors.Add($"{_subject} reads {walked}, which folder '{folder.Folder}' does not contain (items: {string.Join(", ", itemIds)}).");
                            return Refused;
                        }

                        var itemKey = WorkflowDerivedEdges.SnippetItemKey(folder.Folder, name);
                        if (folder.Object == TemplateObjects.Snippet)
                        {
                            _refs.ReadSnippet(itemKey);
                            return new SnippetTarget(itemKey, null);
                        }

                        return new MacroTarget(itemKey, null);
                    }

                    Errors.Add($"{_subject} reads {walked}; a folder is iterated: {{{{ for s in {folder.Object}.{folder.Folder} }}}}.");
                    return Refused;

                case LoopElementTarget element:
                    return StepLoopElement(element, name, walked, before);

                case DeliverableTarget deliverable:
                    if (name != TemplateMembers.Content)
                    {
                        Errors.Add($"{_subject} reads {walked}, which is not a member (deliverable.<id> has .content).");
                        return Refused;
                    }

                    _refs.ReadDeliverable(deliverable.Id);
                    return new DeliverableContentTarget(before, deliverable.Id);

                case ItemTarget item:
                    return StepItem(name, walked, item.Fan ?? _context.Fan);

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

        /// <summary>
        /// #1234 (v27 design § 4): a body node is in scope when it runs in the
        /// reader's own body, or — one level up — in the body the reader's flow
        /// node runs in: a branch node, and an inner if/case's condition (walked
        /// under the flow node's own id), read the loop's body nodes by bare id.
        /// Never down: the outside of a branch reads its flow node.
        /// </summary>
        private bool InScope(string targetOwner, IReadOnlyDictionary<string, TemplateBodyScope> bodies) =>
            _context.Scope is { } mine
            && (string.Equals(targetOwner, mine, StringComparison.Ordinal)
                || (bodies.TryGetValue(mine, out var parent) && string.Equals(targetOwner, parent.Owner, StringComparison.Ordinal)));

        private Target StepNode(NodeTarget node, string name, string before)
        {
            if (name == TemplateMembers.Content)
            {
                // Recorded before any judgement: a refused pipe still names its edge.
                // The bare-read warning is Finalize's (#1058) — it knows the use.
                _refs.ReadNode(node.ReadId, content: true, value: false);
                return new NodeContentTarget(before, node.ReadId, node.Shape);
            }

            if (name == TemplateMembers.Value)
            {
                // #1100 (v26 design § 1.3): a loop's value is the array of its returned
                // node's; an if/case's is its returned node's when the branches agree.
                if (node.Shape.Flow is { } flow)
                {
                    if (flow.Kind == TemplateFlowKind.For)
                    {
                        if (flow.Ret is { HasValue: true } ret)
                        {
                            _refs.ReadNode(node.ReadId, content: false, value: true);
                            return new NodeValueTarget(before, new TemplateValueShape(WorkflowInputTypes.Array, null, ValueShapeOf(ret), null));
                        }

                        // #1234 (v27 design § 1.5): a loop returning an if/case whose branches disagree — the list has no one format.
                        if (flow.Ret?.Flow is { Kind: not TemplateFlowKind.For, Ret: null } branches)
                        {
                            Errors.Add($"{_subject} reads {before}.value, but loop '{node.Id}' returns '{flow.RetId}', whose branches return {branches.DescribeDisagreement()}; {before}.value needs one format — read {before}.content.");
                            return Refused;
                        }

                        Errors.Add($"{_subject} reads {before}.value, but loop '{node.Id}' returns '{flow.RetId}' of format '{flow.Ret?.Format}', which is text and has no value; read {before}.content or iterate {before}.");
                        return Refused;
                    }

                    if (flow.Ret is null)
                    {
                        Errors.Add($"{_subject} reads {before}.value, but branches of '{node.Id}' return {flow.DescribeDisagreement()}; read {before}.content.");
                        return Refused;
                    }
                }

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

                _refs.ReadNode(node.ReadId, content: false, value: true);
                return new NodeValueTarget(before, ValueShapeOf(node.Shape));
            }

            Errors.Add($"{_subject} reads {before}.{name}, which is not a member (node.<id> has .content and .value).");
            return Refused;
        }

        /// <summary>The shape of a node's <c>.value</c>: an enum format's answer set when it has no schema, else its schema's.</summary>
        private static TemplateValueShape ValueShapeOf(TemplateNodeShape shape) =>
            shape.Type == WorkflowInputTypes.Enum && shape.Schema is null
                ? new TemplateValueShape(WorkflowInputTypes.Enum, null, null, shape.Values)
                : TemplateValueShape.FromSchema(shape.Schema);

        /// <summary>
        /// #1100 (v26 design § 1.3): a loop element's members — <c>item</c> typed by
        /// the loop's fan; <c>content</c> and <c>value</c> the returned node's, recorded
        /// as reads of the loop; a body node by id, likewise recorded as the loop's
        /// (the flow node is the only name the outside sees).
        /// </summary>
        private Target StepLoopElement(LoopElementTarget element, string name, StringBuilder walked, string before)
        {
            var flow = element.Flow;

            switch (name)
            {
                case TemplateMembers.Item:
                    return new ItemTarget(flow.Fan);

                case TemplateMembers.Content:
                    _refs.ReadNode(element.LoopId, content: true, value: false);
                    return new NodeContentTarget(before, element.LoopId, flow.Ret ?? TemplateNodeShape.Aggregator(), flow.RetId);

                case TemplateMembers.Value:
                    if (flow.Ret is { HasValue: true } ret)
                    {
                        _refs.ReadNode(element.LoopId, content: false, value: true);
                        return new NodeValueTarget(before, ValueShapeOf(ret));
                    }

                    // #1234 (v27 design § 1.5): the element of a loop returning an if/case whose branches disagree.
                    if (flow.Ret?.Flow is { Kind: not TemplateFlowKind.For, Ret: null } branches)
                    {
                        Errors.Add($"{_subject} reads {walked}, but loop '{element.LoopId}' returns '{flow.RetId}', whose branches return {branches.DescribeDisagreement()}; {walked} needs one format — read {before}.content.");
                        return Refused;
                    }

                    Errors.Add($"{_subject} reads {walked}, but loop '{element.LoopId}' returns '{flow.RetId}' of format '{flow.Ret?.Format}', which is text and has no value; read {before}.content.");
                    return Refused;

                default:
                    if (flow.Body is { } body && body.TryGetValue(name, out var shape))
                    {
                        return new NodeTarget(name, shape, element.LoopId);
                    }

                    Errors.Add($"{_subject} reads {walked}, which is not a member (a loop element has item, content, value and the body nodes: {string.Join(", ", (flow.Body?.Keys ?? Enumerable.Empty<string>()).Order(StringComparer.Ordinal))}).");
                    return Refused;
            }
        }

        private Target StepItem(string name, StringBuilder walked, TemplateFan? fan)
        {
            switch (fan)
            {
                case SnippetFolderFan:
                    if (RefusesName(name, walked.ToString()))
                    {
                        return Refused;
                    }

                    if (!SnippetFanItemMembers.Contains(name, StringComparer.Ordinal))
                    {
                        Errors.Add($"{_subject} reads {walked}, which a snippet fan item does not carry (it carries: {string.Join(", ", SnippetFanItemMembers)}).");
                        return Refused;
                    }

                    _refs.ReadItem(name);
                    return new ShapeTarget(TemplateValueShape.Text);

                case InputArrayFan input:
                    if (RefusesName(name, walked.ToString()))
                    {
                        return Refused;
                    }

                    if (!InputFanItemMembers.Contains(name, StringComparer.Ordinal))
                    {
                        Errors.Add($"{_subject} reads {walked}, which an input fan item does not carry (it carries: {string.Join(", ", InputFanItemMembers)}).");
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
                        if (RefusesName(name, walked.ToString()))
                        {
                            return Refused;
                        }

                        if (!AnyFanItemMembers.Contains(name, StringComparer.Ordinal))
                        {
                            Errors.Add($"{_subject} reads {walked}, which a fan item does not carry (it carries: {(_context.Labelled ? "id, label, content or value" : "id, name, content or value")}).");
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

                    if (target is NodeTarget { Shape.Flow.Kind: TemplateFlowKind.For } loop)
                    {
                        // #1100: node.<loop>[0] is one element of the list.
                        _refs.ReadNode(loop.ReadId, content: true, value: true);
                        return new LoopElementTarget(loop.Id, loop.Shape.Flow!);
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
                        ReadFolder(folder);
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
                return FinalizeRendered(target, walked, use);
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
                // #1100 (v26 design § 1.3): a loop is iterable — its element carries item,
                // the returned node's content and value, and the body nodes by id.
                case NodeTarget { Shape.Flow.Kind: TemplateFlowKind.For } loop when use == ChainUse.Iterable:
                    _refs.ReadNode(loop.ReadId, content: true, value: true);
                    return new LoopElementTarget(loop.Id, loop.Shape.Flow!);

                case NodeTarget { Shape.Flow.Kind: TemplateFlowKind.For } loop:
                    Errors.Add($"{_subject} reads node '{loop.Id}' bare; read {walked}.content, {walked}.value, or iterate it ({{{{ for e in {walked} }}}}).");
                    return Refused;

                case NodeTarget node:
                    Errors.Add($"{_subject} reads node '{node.Id}' bare; read {walked}.content (its composed output) or {walked}.value.");
                    return Refused;

                case LoopElementTarget element when use == ChainUse.Value:
                    Errors.Add($"{_subject} reads {walked} bare; read a member ({walked}.item, {walked}.content, {walked}.value or a body node: {string.Join(", ", (element.Flow.Body?.Keys ?? Enumerable.Empty<string>()).Order(StringComparer.Ordinal))}).");
                    return Refused;

                case SnippetTarget { Id: { } id }:
                    Errors.Add($"{_subject} reads snippet '{id}' bare; read {walked}.content.");
                    return Refused;

                case MacroTarget { Id: { } id }:
                    Errors.Add($"{_subject} reads macro '{id}' bare; read {walked}.content or {walked}.prompt.");
                    return Refused;

                case DeliverableTarget deliverable:
                    Errors.Add($"{_subject} reads deliverable '{deliverable.Id}' bare; read {walked}.content.");
                    return Refused;

                case FactsTarget facts:
                    Errors.Add($"{_subject} reads '{facts.Object}' bare; name the fact: {facts.Object}.{string.Join(" or " + facts.Object + ".", facts.Object == TemplateObjects.Run ? TemplateMembers.Run : TemplateMembers.Profile)}.");
                    return Refused;

                case ItemTarget:
                    Errors.Add($"{_subject} reads item bare; read a member ({(_context.Fan is InputArrayFan ? $"item.id, item.{NameMember}, item.value" : $"item.id, item.{NameMember}, item.content")}).");
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
        private Target FinalizeRendered(Target target, string walked, ChainUse use)
        {
            var pipe = use.Pipe!;
            var format = TemplatePipes.FormatOf(pipe)!;

            if (_context.Labelled)
            {
                return FinalizeRenderedAt26(target, walked, use, pipe, format);
            }

            switch (target)
            {
                case RefusedTarget:
                    return Refused;

                // #1100 (v26 design § 1.3, § 7.1): a loop is a list — no one text to render;
                // an if/case whose branches disagree has no one format.
                case NodeContentTarget { Node.Flow.Kind: TemplateFlowKind.For } content:
                    Errors.Add($"{_subject} pipes {walked} through {pipe}, but {content.Path} loops — a list has no one text; pipe the returned node inside the loop ({{{{ for e in {content.Path} }}}}{{{{ e.content | {pipe} }}}}{{{{ end }}}}) or read {content.Path}.content.");
                    return Refused;

                case DeliverableContentTarget composed:
                    Errors.Add($"{_subject} pipes {walked} through {pipe}, but {composed.Path} composes sections; {pipe} renders a {format} node's text (node.<id>.content).");
                    return Refused;

                case NodeContentTarget { Node.Flow: { Ret: null } } content:
                    Errors.Add($"{_subject} pipes {walked} through {pipe}, but branches of '{content.Id}' return {content.Node.Flow!.DescribeDisagreement()}; {pipe} renders a {format} node's text (node.<id>.content).");
                    return Refused;

                case NodeContentTarget content when string.Equals(content.Node.Format, format, StringComparison.Ordinal):
                    // #1098: the accepted rendering — the editor's "Used by" row for the pipe; #1138: with its if.
                    _refs.ReadRendered(content.Id, pipe, use.Guard, use.GuardCanonical);
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

        /// <summary>
        /// #1059 (v26 design § 7.1): at 26 a renderer pipe takes the NODE —
        /// {{ node.extract | concept_context }}; the .content spelling is refused
        /// with the replacement, .value too (no node identity); an if/case node
        /// passes when its returned node's format is the pipe's; a loop is a list
        /// — pipe a body node inside it (decided 2026-10-26: e.&lt;ret&gt; | pipe, never
        /// e.content | pipe). The edge and the rendering are recorded on the node.
        /// </summary>
        private Target FinalizeRenderedAt26(Target target, string walked, ChainUse use, string pipe, string format)
        {
            switch (target)
            {
                case RefusedTarget:
                    return Refused;

                case NodeTarget { Shape.Flow.Kind: TemplateFlowKind.For } loop:
                    Errors.Add($"{_subject} pipes {walked} through {pipe}; a loop is a list — pipe a body node inside it ({{{{ for e in {walked} }}}}{{{{ e.{loop.Shape.Flow!.RetId ?? "<ret>"} | {pipe} }}}}{{{{ end }}}}), or read {walked}.content.");
                    return Refused;

                case NodeTarget { Shape.Flow: { Ret: null } } node:
                    Errors.Add($"{_subject} pipes {walked} through {pipe}, but branches of '{node.Id}' return {node.Shape.Flow!.DescribeDisagreement()}; {pipe} renders a {format} node (node.<id>).");
                    return Refused;

                case NodeTarget node when string.Equals(node.Shape.Format, format, StringComparison.Ordinal):
                    // The pipe reads the node's text: the edge, then the rendering (#1098; #1138 with its if).
                    _refs.ReadNode(node.ReadId, content: true, value: false);
                    _refs.ReadRendered(node.ReadId, pipe, use.Guard, use.GuardCanonical);
                    return new ShapeTarget(TemplateValueShape.Text);

                case NodeTarget { Shape.HasValue: false } node:
                    _refs.ReadNode(node.ReadId, content: true, value: false);
                    Errors.Add($"{_subject} pipes {walked} through {pipe}, but format '{node.Shape.Format}' is text; {pipe} renders a {format} node (node.<id>).");
                    return Refused;

                case NodeTarget node:
                    _refs.ReadNode(node.ReadId, content: true, value: false);
                    Errors.Add($"{_subject} pipes {walked} through {pipe}, but format '{node.Shape.Format}' ({node.Shape.Type}) is not {format}; {pipe} renders a {format} node (node.<id>).");
                    return Refused;

                case NodeContentTarget content:
                    Errors.Add($"{_subject} pipes {walked} through {pipe}; at specVersion 26 a renderer pipe takes the node — {content.Path}{(content.ElementRet is null ? string.Empty : "." + content.ElementRet)} | {pipe}.");
                    return Refused;

                case NodeValueTarget value:
                    Errors.Add($"{_subject} pipes {walked} through {pipe}; a renderer pipe takes the node — {value.Path} | {pipe}.");
                    return Refused;

                case DeliverableContentTarget composed:
                    Errors.Add($"{_subject} pipes {walked} through {pipe}, but {composed.Path} composes sections; {pipe} renders a {format} node (node.<id>).");
                    return Refused;

                case LoopElementTarget:
                case ShapeTarget { Shape: { } }:
                case FolderTarget:
                    Errors.Add($"{_subject} pipes {walked} through {pipe}; a renderer pipe takes a node (node.<id>).");
                    return Refused;

                default:
                    return Finalize(target, walked, ChainUse.Value);
            }
        }

        /// <summary>What a loop variable is bound to, from the iterated expression.</summary>
        /// <summary>The folder read as a folder — iterated or indexed (#1116: a direct item read is the item's alone).</summary>
        private void ReadFolder(FolderTarget folder)
        {
            if (folder.Object == TemplateObjects.Snippet)
            {
                _refs.ReadSnippetFolder(folder.Folder);
            }
            else
            {
                _refs.ReadMacroFolder(folder.Folder);
            }
        }

        private Target ElementOf(ScriptExpression? iterator)
        {
            var target = ResolveExpression(iterator, ChainUse.Iterable);

            switch (target)
            {
                case LoopElementTarget element:
                    return element;

                case FolderTarget folder:
                    ReadFolder(folder);
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
