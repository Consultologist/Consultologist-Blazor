using Scriban;
using Scriban.Syntax;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 2.1, 5.2): one pipe step of an output or a test: <paramref name="Name"/>
/// as written (<c>string.strip</c>, <c>renderer.bullets</c>), its arguments as source text, and its 1-based line.
/// </summary>
public sealed record TemplatePipe(string Name, IReadOnlyList<string> Arguments, int Line);

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 2.1, 2.6): an output statement and the tag it stands in.
/// <paramref name="Path"/> is the piped head when it is one access path (<c>node.e.value</c>), else <c>null</c>;
/// <paramref name="Paths"/> every access path the expression reads (pipe arguments included, built-in names not);
/// <paramref name="Depth"/> the number of <c>if</c> and <c>for</c> around it and <paramref name="Enclosing"/> their
/// kinds (<c>"if"</c>, <c>"for"</c>), outermost first. A path through a <c>for</c> variable keeps the variable (<c>s.value</c>).
/// Parentheses around the head, or around a piped head (<c>(x | f) | g</c>), are taken off, so <c>(node.e.value)</c> is
/// the path <c>node.e.value</c>. <paramref name="Head"/> is the head as written without them; <paramref name="HeadType"/>
/// the built-in type of a head that is a literal or a test (<c>"string"</c>, <c>"integer"</c>, <c>"number"</c>,
/// <c>"boolean"</c>, <c>"null"</c>), else <c>null</c>.
/// </summary>
public sealed record TemplateOutput(
    TemplateTag Tag,
    string? Path,
    IReadOnlyList<TemplatePipe> Pipes,
    IReadOnlyList<string> Paths,
    int Depth,
    IReadOnlyList<string> Enclosing,
    string Head,
    string? HeadType);

/// <summary>#1504 (specVersion 31, area 07; part 05 § 2.1): a <c>for</c>: its variable, what it iterates as written, its tag, and the number of <c>if</c> and <c>for</c> around it.</summary>
public sealed record TemplateFor(string Variable, string Source, TemplateTag Tag, int Depth);

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 2.5; R2): one comparison or bare truth test of an <c>if</c> chain,
/// <c>!</c>, <c>&amp;&amp;</c>, <c>||</c> and parentheses taken apart. <paramref name="Text"/> is the test as written;
/// <paramref name="Path"/> the access path it tests (after any pipes: <c>(c.support | string.strip) != ""</c> tests
/// <c>c.support</c> through <paramref name="Pipes"/>); <paramref name="Operator"/> and <paramref name="Literal"/> the
/// comparison's operator and literal as written (<c>==</c>, <c>"completed"</c>), <c>null</c> for a bare test;
/// <paramref name="Start"/> its offset in the body, so that an <c>else if</c>'s test is placed after the branch before it.
/// </summary>
public sealed record TemplateTest(string Text, string? Path, string? Operator, string? Literal, IReadOnlyList<TemplatePipe> Pipes, int Line, int Start);

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 2.5): an <c>if</c> chain (<c>if</c>, its <c>else if</c>s, its
/// <c>else</c>): the tag of its <c>if</c>, every test of every branch in order, whether it ends with an
/// <c>else</c>, and the number of <c>if</c> and <c>for</c> around it.
/// </summary>
public sealed record TemplateIf(TemplateTag Tag, IReadOnlyList<TemplateTest> Tests, bool HasElse, int Depth);

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 2.1–2.3, 2.6, 5.2; <c>scriban-dialect.md</c> §§ 2, 3, 5, 10): one
/// template body as the dialect reads it: its tags, outputs, fors and ifs in source order, and the statement-level
/// rules it breaks (<see cref="DialectRules"/>), in source order. Comments and raw blocks give nothing.
/// </summary>
public sealed record TemplateSyntax(
    string Text,
    IReadOnlyList<TemplateTag> Tags,
    IReadOnlyList<TemplateOutput> Outputs,
    IReadOnlyList<TemplateFor> Fors,
    IReadOnlyList<TemplateIf> Ifs,
    IReadOnlyList<DialectFault> Faults)
{
    /// <summary>
    /// Reads <paramref name="text"/> with Scriban 7.2.5's parser (the default language) and its lexer for the tags.
    /// Never throws on any text: a body Scriban cannot parse, or one with a <c>{{</c> never closed, is one A07-005
    /// fault at the first error's line and nothing else. Scriban's parser bounds its own depth, so a body nested a few
    /// hundred statements or parentheses deep is A07-005 rather than S05-013, and so is any body whose syntax tree is
    /// deeper than <see cref="TreeDepthLimit"/> (a member or indexer chain thousands of segments long).
    /// #1505 ruling 6 (R14): an exception no text explains, a fault of the engine and not of the package, is one A08-006
    /// fault instead, its <see cref="DialectFault.Fill"/> the exception's type alone (this library has no logger: the
    /// caller logs it), never its message or the body.
    /// </summary>
    public static TemplateSyntax Parse(string text) => Parse(text, static body => Template.Parse(body));

    /// <summary><see cref="Parse(string)"/> with <paramref name="parse"/> in Scriban's place: the seam through which a test makes the parser fail.</summary>
    internal static TemplateSyntax Parse(string text, Func<string, Template> parse)
    {
        ArgumentNullException.ThrowIfNull(text);

        try
        {
            var scan = TemplateTags.Scan(text);
            var template = parse(text);

            if (scan.UnclosedLine is { } unclosed)
            {
                return Unreadable(text, unclosed);
            }

            if (template.HasErrors || template.Page is null)
            {
                var first = template.Messages.FirstOrDefault(m => m.Type == Scriban.Parsing.ParserMessageType.Error);
                return Unreadable(text, first is null ? 1 : Math.Max(first.Span.Start.Line, 0) + 1);
            }

            // Scriban builds member and indexer chains in a loop, so its own depth bound never meets them; the walk
            // below recurses, so it is bounded here, before it starts.
            if (TooDeep(template.Page) is { } deep)
            {
                return Unreadable(text, deep);
            }

            var reader = new Reader(text, scan.Tags);
            reader.Read(template.Page);
            return reader.ToSyntax();
        }
        catch (Exception exception) when (exception is ScriptRuntimeException or InsufficientExecutionStackException)
        {
            // Scriban reports a parse error as a message; what it throws for text it cannot read, or a stack too shallow
            // for the text, is still the text's.
            return Unreadable(text, 1);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Anything else is the engine's: said as such, so that an engine bug never reads as the author's fault.
            return new(text, [], [], [], [], [new DialectFault("A08-006", 1, [exception.GetType().FullName ?? exception.GetType().Name])]);
        }
    }

    /// <summary>
    /// How deep the syntax tree may be before the body is refused unread (A07-005): far past any template the dialect
    /// allows (15 levels of <c>if</c> and <c>for</c>, short access paths), and shallow enough for the recursive walk to
    /// fit a 1 MB stack.
    /// </summary>
    internal const int TreeDepthLimit = 1_000;

    /// <summary>The 1-based line of the first node found past <see cref="TreeDepthLimit"/>, walked without recursion; <c>null</c> when none is.</summary>
    private static int? TooDeep(ScriptNode root)
    {
        var pending = new Stack<(ScriptNode Node, int Depth)>();
        pending.Push((root, 1));
        while (pending.TryPop(out var top))
        {
            if (top.Depth > TreeDepthLimit)
            {
                return Math.Max(top.Node.Span.Start.Line, 0) + 1;
            }

            foreach (var child in top.Node.Children)
            {
                if (child is not null)
                {
                    pending.Push((child, top.Depth + 1));
                }
            }
        }

        return null;
    }

    private static TemplateSyntax Unreadable(string text, int line) =>
        new(text, [], [], [], [], [new DialectFault("A07-005", line, [])]);

    /// <summary>The walk: records outputs, fors and ifs with their tags, and asks <see cref="DialectRules"/> of every node.</summary>
    private sealed class Reader : ScriptVisitor
    {
        private readonly string _text;
        private readonly IReadOnlyList<TemplateTag> _tags;
        private readonly List<DialectFault> _faults = [];
        private readonly List<TemplateOutput> _outputs = [];
        private readonly List<TemplateFor> _fors = [];
        private readonly List<TemplateIf> _ifs = [];
        private readonly List<string> _enclosing = [];
        private int _deepest;
        private int _deepestLine;

        public Reader(string text, IReadOnlyList<TemplateTag> tags)
        {
            _text = text;
            _tags = tags;

            foreach (var tag in tags.Where(t => t.DashLeft || t.DashRight))
            {
                _faults.Add(new DialectFault("S05-019", tag.Line, []));
            }
        }

        public void Read(ScriptPage page) => Walk(page);

        public TemplateSyntax ToSyntax()
        {
            if (DialectRules.Nesting(_deepest, _deepestLine) is { } nesting)
            {
                _faults.Add(nesting);
            }

            return new TemplateSyntax(_text, _tags, _outputs, _fors, _ifs, _faults.OrderBy(f => f.Line).ToList());
        }

        // ----- statements ----------------------------------------------------------

        protected override void DefaultVisit(ScriptNode? node)
        {
            if (node is not null && DialectRules.RefusedStatement(node) is { } keyword)
            {
                Fault("S05-001", node, keyword);
            }

            base.DefaultVisit(node);
        }

        public override void Visit(ScriptExpressionStatement node)
        {
            if (node.Expression is { } expression and not ScriptAssignExpression)
            {
                var (head, steps) = Decompose(expression);
                var paths = new List<string>();
                CollectPaths(expression, paths);
                _outputs.Add(new TemplateOutput(
                    TagOf(node), Chain(head), steps.Select(Pipe).ToList(), paths, _enclosing.Count, [.. _enclosing], Slice(head), TypeOf(head)));
            }

            Walk(node.Expression);
        }

        public override void Visit(ScriptTableRowStatement node)
        {
            Fault("S05-001", node, "tablerow");
            Nest("for", node, () => Walk(node.Body));
        }

        public override void Visit(ScriptForStatement node)
        {
            var variable = node.Variable is ScriptVariable named ? named.Name : Slice(node.Variable);
            _fors.Add(new TemplateFor(variable, Slice(node.Iterator), TagOf(node), _enclosing.Count));

            if (node.Iterator is { } iterator && DialectRules.IsNoList(iterator))
            {
                Fault("S05-006", iterator, Slice(iterator));
            }
            else
            {
                Walk(node.Iterator);
            }

            if (node.NamedArguments is { } options)
            {
                foreach (var option in options)
                {
                    _faults.Add(DialectRules.ForOption(option, LineOf(option)));
                }
            }

            Nest("for", node, () =>
            {
                Walk(node.Body);
                if (node.Else is { } otherwise)
                {
                    // A for's else renders when the list is empty: a choice of text, which the dialect leaves to a header.
                    Fault("A07-004", otherwise, "else");
                    Walk(otherwise.Body);
                }
            });
        }

        public override void Visit(ScriptIfStatement node)
        {
            var tests = new List<TemplateTest>();
            var bodies = new List<ScriptNode?>();
            var hasElse = false;

            for (ScriptConditionStatement? branch = node; branch is not null;)
            {
                if (branch is ScriptIfStatement test)
                {
                    if (test.Condition is { } condition)
                    {
                        Split(condition, tests);
                        Walk(condition);
                    }

                    bodies.Add(test.Then);
                    branch = test.Else;
                }
                else
                {
                    if (branch is ScriptElseStatement otherwise)
                    {
                        hasElse = true;
                        bodies.Add(otherwise.Body);
                    }
                    else
                    {
                        bodies.Add(branch);
                    }

                    branch = null;
                }
            }

            _ifs.Add(new TemplateIf(TagOf(node), tests, hasElse, _enclosing.Count));
            Nest("if", node, () => bodies.ForEach(Walk));
        }

        // ----- expressions -----------------------------------------------------------

        public override void Visit(ScriptAssignExpression node)
        {
            Fault("S05-001", node, node.EqualToken is { } written ? Slice(written) : "=");
            Walk(node.Value);
        }

        public override void Visit(ScriptBinaryExpression node)
        {
            if (DialectRules.RefusedOperator(node) is { } written)
            {
                Fault("S05-001", node, written);
            }

            Walk(node.Left);
            Walk(node.Right);
        }

        public override void Visit(ScriptUnaryExpression node)
        {
            if (DialectRules.RefusedOperator(node) is { } written)
            {
                Fault("S05-001", node, written);
            }

            Walk(node.Right);
        }

        public override void Visit(ScriptIndexerExpression node)
        {
            Fault("S05-063", node, Slice(node.Target), Slice(node.Index));
            Walk(node.Target);
            Walk(node.Index);
        }

        public override void Visit(ScriptMemberExpression node)
        {
            if (Chain(node) is { } chain)
            {
                Read(chain, node);
            }
            else
            {
                Walk(node.Target);
            }
        }

        public override void Visit(ScriptVariableGlobal node) => Read(node.Name, node);

        public override void Visit(ScriptVariableLocal node) => Read(node.Name, node);

        public override void Visit(ScriptPipeCall node)
        {
            var (head, steps) = Decompose(node);
            Walk(head);

            foreach (var step in steps)
            {
                var (target, arguments) = Parts(step);
                if (DialectRules.PipeStep(Chain(target), Slice(target), arguments, LineOf(step)) is { } fault)
                {
                    _faults.Add(fault);
                }

                arguments.ForEach(Walk);
            }
        }

        public override void Visit(ScriptFunctionCall node)
        {
            var arguments = node.Arguments?.ToList() ?? [];
            _faults.Add(DialectRules.Call(Chain(node.Target), Slice(node.Target), arguments.Select(Slice).ToList(), LineOf(node)));
            arguments.ForEach(Walk);
        }

        // ----- reading -----------------------------------------------------------------

        private void Read(string chain, ScriptNode node)
        {
            if (DialectRules.Read(chain, LineOf(node)) is { } fault)
            {
                _faults.Add(fault);
            }
        }

        private void Nest(string kind, ScriptNode node, Action body)
        {
            _enclosing.Add(kind);
            if (_enclosing.Count > _deepest)
            {
                _deepest = _enclosing.Count;
                _deepestLine = LineOf(node);
            }

            body();
            _enclosing.RemoveAt(_enclosing.Count - 1);
        }

        private void Walk(ScriptNode? node)
        {
            if (node is not null)
            {
                base.Visit(node);
            }
        }

        /// <summary>The tests of one condition: <c>!</c>, <c>&amp;&amp;</c>, <c>||</c> and parentheses taken apart.</summary>
        private void Split(ScriptExpression? condition, List<TemplateTest> tests)
        {
            if (condition is null)
            {
                return;
            }

            var expression = DialectRules.Unwrap(condition);
            switch (expression)
            {
                case ScriptBinaryExpression { Operator: ScriptBinaryOperator.And or ScriptBinaryOperator.Or } both:
                    Split(both.Left, tests);
                    Split(both.Right, tests);
                    return;
                case ScriptUnaryExpression { Operator: ScriptUnaryOperator.Not, Right: { } negated }:
                    Split(negated, tests);
                    return;
                case ScriptBinaryExpression { Operator: ScriptBinaryOperator.CompareEqual or ScriptBinaryOperator.CompareNotEqual
                    or ScriptBinaryOperator.CompareLess or ScriptBinaryOperator.CompareGreater
                    or ScriptBinaryOperator.CompareLessOrEqual or ScriptBinaryOperator.CompareGreaterOrEqual } comparison:
                {
                    var sides = new[] { comparison.Left, comparison.Right }.OfType<ScriptExpression>().Select(DialectRules.Unwrap).ToList();
                    var literal = sides.FirstOrDefault(s => s is ScriptLiteral);
                    var tested = sides.FirstOrDefault(s => s is not ScriptLiteral);
                    var (head, steps) = tested is null ? (null, []) : Decompose(tested);
                    tests.Add(new TemplateTest(
                        Slice(comparison),
                        Chain(head),
                        comparison.OperatorToken?.Value ?? comparison.OperatorAsText,
                        literal is null ? null : Slice(literal),
                        steps.Select(Pipe).ToList(),
                        LineOf(comparison),
                        Math.Max(comparison.Span.Start.Offset, 0)));
                    return;
                }

                default:
                {
                    var (head, steps) = Decompose(expression);
                    tests.Add(new TemplateTest(
                        Slice(expression), Chain(head), null, null, steps.Select(Pipe).ToList(), LineOf(expression), Math.Max(expression.Span.Start.Offset, 0)));
                    return;
                }
            }
        }

        /// <summary>Every access path <paramref name="node"/> reads; a pipe step's or a call's own name is no read.</summary>
        private void CollectPaths(ScriptNode? node, List<string> paths)
        {
            switch (node)
            {
                case null or ScriptLiteral:
                    return;
                case ScriptExpression expression when Chain(expression) is { } chain:
                    if (!DialectRules.IsBuiltInName(chain))
                    {
                        paths.Add(chain);
                    }

                    return;
                case ScriptPipeCall pipe:
                {
                    var (head, steps) = Decompose(pipe);
                    CollectPaths(head, paths);
                    foreach (var step in steps)
                    {
                        Parts(step).Arguments.ForEach(a => CollectPaths(a, paths));
                    }

                    return;
                }

                case ScriptFunctionCall call:
                    foreach (var argument in call.Arguments ?? [])
                    {
                        CollectPaths(argument, paths);
                    }

                    return;
                case ScriptMemberExpression member:
                    CollectPaths(member.Target, paths);
                    return;
                default:
                    foreach (var child in node.Children)
                    {
                        CollectPaths(child, paths);
                    }

                    return;
            }
        }

        private TemplatePipe Pipe(ScriptExpression step)
        {
            var (target, arguments) = Parts(step);
            return new TemplatePipe(Chain(target) ?? Slice(target), arguments.Select(Slice).ToList(), LineOf(step));
        }

        private static (ScriptExpression Target, List<ScriptExpression> Arguments) Parts(ScriptExpression step) =>
            step is ScriptFunctionCall { Target: { } target } call ? (target, call.Arguments?.ToList() ?? []) : (step, []);

        /// <summary>
        /// The head of a pipeline and its steps in order, parentheses taken off. Scriban reads <c>x | f | g</c> as a pipe
        /// from <c>x</c> to a pipe from <c>f</c> to <c>g</c>; <c>(x | f) | g</c> is the same pipeline.
        /// </summary>
        private static (ScriptExpression? Head, List<ScriptExpression> Steps) Decompose(ScriptExpression? expression)
        {
            if (expression is not null)
            {
                expression = DialectRules.Unwrap(expression);
            }

            if (expression is not ScriptPipeCall pipe)
            {
                return (expression, []);
            }

            var (head, steps) = Decompose(pipe.From);
            Steps(pipe.To, steps);
            return (head, steps);
        }

        private static void Steps(ScriptExpression? to, List<ScriptExpression> steps)
        {
            if (to is ScriptPipeCall next)
            {
                Steps(next.From, steps);
                Steps(next.To, steps);
            }
            else if (to is not null)
            {
                steps.Add(to);
            }
        }

        /// <summary>The built-in type of a head that is a literal or a test (a comparison, <c>&amp;&amp;</c>, <c>||</c>, <c>!</c>); <c>null</c> for anything else.</summary>
        private static string? TypeOf(ScriptExpression? head) => head switch
        {
            ScriptLiteral { Value: null } => "null",
            ScriptLiteral { Value: string } => "string",
            ScriptLiteral { Value: bool } => "boolean",
            ScriptLiteral { Value: int or long or uint or ulong or short or byte or System.Numerics.BigInteger } => "integer",
            ScriptLiteral { Value: double or float or decimal } => "number",
            ScriptBinaryExpression { Operator: ScriptBinaryOperator.And or ScriptBinaryOperator.Or
                or ScriptBinaryOperator.CompareEqual or ScriptBinaryOperator.CompareNotEqual
                or ScriptBinaryOperator.CompareLess or ScriptBinaryOperator.CompareGreater
                or ScriptBinaryOperator.CompareLessOrEqual or ScriptBinaryOperator.CompareGreaterOrEqual } => "boolean",
            ScriptUnaryExpression { Operator: ScriptUnaryOperator.Not } => "boolean",
            _ => null,
        };

        /// <summary>The access path an expression is, dotted as written (<c>node.e.value</c>), or <c>null</c>.</summary>
        private static string? Chain(ScriptExpression? expression)
        {
            var members = new List<string>();
            while (expression is ScriptMemberExpression { Member: { } member } access)
            {
                members.Add(member.Name);
                expression = access.Target;
            }

            if (expression is not ScriptVariable variable)
            {
                return null;
            }

            members.Add(variable.Name);
            members.Reverse();
            return string.Join('.', members);
        }

        private TemplateTag TagOf(ScriptNode node)
        {
            var offset = node.Span.Start.Offset;
            int low = 0, high = _tags.Count - 1;
            while (low <= high)
            {
                var middle = (low + high) / 2;
                var tag = _tags[middle];
                if (offset < tag.Start)
                {
                    high = middle - 1;
                }
                else if (offset >= tag.End)
                {
                    low = middle + 1;
                }
                else
                {
                    return tag;
                }
            }

            // Every statement stands in a tag the lexer found; this keeps the record whole if one ever does not.
            var line = LineOf(node);
            return new TemplateTag(line, Math.Max(node.Span.Start.Column, 0) + 1, offset, offset, false, false, false, false, false, true, true);
        }

        private void Fault(string id, ScriptNode node, params string[] fill) => _faults.Add(new DialectFault(id, LineOf(node), fill));

        private static int LineOf(ScriptNode node) => Math.Max(node.Span.Start.Line, 0) + 1;

        private string Slice(ScriptNode? node)
        {
            if (node is null)
            {
                return string.Empty;
            }

            var start = Math.Clamp(node.Span.Start.Offset, 0, _text.Length);
            var end = Math.Clamp(node.Span.End.Offset + 1, start, _text.Length);
            return _text[start..end];
        }
    }
}
