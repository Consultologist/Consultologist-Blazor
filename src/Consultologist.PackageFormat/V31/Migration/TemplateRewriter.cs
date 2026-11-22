using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Templates;
using Scriban;
using Scriban.Syntax;

namespace Consultologist.PackageFormat.V31.Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 05 § 3.1): where a 30 body is rewritten. <paramref name="Site"/> is its 31 site
/// (<see cref="TemplateSite.Prompt"/>, <see cref="TemplateSite.Rendered"/>, <see cref="TemplateSite.SnippetBody"/> or
/// <see cref="TemplateSite.MacroBody"/>); <paramref name="File"/> the 31 file a hand edit names; <paramref name="OwnerId"/>
/// the node whose prompt or <c>rendered</c> body it is (its 30 id, as renamed), whose place its conditions are converted
/// at, <c>null</c> for a snippet or a macro; <paramref name="ItemLabel"/> the <c>itemLabel</c> of the innermost loop the
/// node runs in (30's <c>item.label</c> is <c>item.value.&lt;itemLabel&gt;</c> at 31), <c>null</c> when it names none.
/// </summary>
internal sealed record TemplateSite30(TemplateSite Site, string File, string? OwnerId, string? ItemLabel = null);

/// <summary>
/// #1507 (specVersion 31, area 10): a 30 body in the 31 dialect. <paramref name="Text"/> is the body;
/// <paramref name="Parts"/> the node's <c>parts</c> member (part id → <c>{ "if", "text" }</c>, part 05 § 7.1), empty when
/// no <c>if</c> was lifted; <paramref name="HandEdits"/> the hand-edit notices said for this body, in order (each is also in
/// the run's notices). <see cref="Macros"/> are the macro placements the body makes, in order, each with the condition
/// hoisted onto it.
/// </summary>
internal sealed record RewrittenBody(string Text, JsonObject Parts, IReadOnlyList<string> HandEdits)
{
    public IReadOnlyList<MacroPlacement> Macros { get; init; } = [];
}

/// <summary>
/// #1507 (specVersion 31, area 10; R6): one macro placement a 30 body makes at 31: the macro (its id; a folder macro's
/// <c>&lt;folder&gt;/&lt;id&gt;</c>), the node whose prompt it is in (<c>null</c> in a snippet's body), the 31 file it is in,
/// and the body <c>if</c> hoisted off it (<c>null</c>: placed with no condition, a part's text and an unrolled folder
/// included).
/// </summary>
internal sealed record MacroPlacement(string Macro, string? Owner, string File, PlacementRule? Rule);

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 1.2, its bare-read, renderer, whitespace, lifted-<c>if</c>, unroll and hoist
/// rows; § 1.4; part 05 §§ 2, 3, 5.2, 6.3, 7; part 04 § 2.6; R6): a 30 template body rewritten into the 31 dialect.
/// Scriban's parser gives each tag's tree; the reads it holds are rewritten (a bare <c>input.x</c> to its <c>.content</c>
/// or <c>.value</c>, <c>input.x.f</c> to <c>input.x.value.f</c>, a bare node to its member, a classifier's
/// <c>.value</c> to <c>.value.value</c>, a check's <c>node.</c> to <c>check.</c>, <c>item.label</c> to its loop's
/// <c>itemLabel</c> field, <c>macro.x.prompt</c> to the placement <c>macro.x</c>, <c>macro.x.content</c> of a macro
/// that becomes a snippet to <c>snippet.x.content</c> (D4), a pipe to a 30 renderer to the
/// <c>renderer.</c> pipe of the node's <c>.value</c>, a <c>for</c> over a loop node to its <c>.items</c> with
/// <c>.skipped</c> its <c>state</c>), and the tag scanner (<see cref="TemplateTags"/>) gives the whitespace marks, a
/// <c>-</c> becoming a <c>~</c> and a placement keeping only the forms § 2.6 allows. A body <c>if</c> that tests a value is
/// lifted, in a prompt, into parts (<c>when_&lt;n&gt;</c>, with its negation for an <c>else</c>), or hoisted off the one
/// macro it places onto that placement (<see cref="RewrittenBody.Macros"/>); a <c>for</c> over a macro folder is unrolled. Every edit is
/// made by source span, last to first, so text outside tags keeps its bytes. What cannot be decided stays as 30 wrote it,
/// with one hand edit each (A10-001, A10-004, A10-005, A10-035, A10-037 to A10-040, S10-004); a body that still breaks a statement
/// rule of the dialect (<see cref="TemplateSyntax"/>) has one hand edit per fault. Never throws on package content.
/// </summary>
internal static class TemplateRewriter
{
    /// <summary>
    /// D3, D4: the word a 30 fixed-text read of a macro that stays a macro is written with: <c>macro.&lt;id&gt;.fixed_text</c>,
    /// a member no 31 macro has, or for a folder's item <c>macro.&lt;folder&gt;.&lt;id&gt;_fixed_text</c>, an item the folder
    /// lacks. The checker refuses either read (S05-011) until the hand edit is made, and the operator sees what 30 read there.
    /// </summary>
    internal const string FixedText = "fixed_text";

    /// <summary>
    /// <paramref name="body"/> (a 30 body, its header taken off) rewritten at <paramref name="site"/>; its conditions'
    /// notices, its hand edits and its hoisted placement rules are said on <paramref name="migrated"/>.
    /// </summary>
    public static RewrittenBody Rewrite(string body, TemplateSite30 site, Migrated migrated)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(migrated);

        var said = migrated.HandEditNotices.Count;
        var run = new Run(body, site, migrated);
        var text = run.Rewrite();
        return new RewrittenBody(text, run.Parts, migrated.HandEditNotices.Skip(said).ToList()) { Macros = run.Macros };
    }

    /// <summary>One edit of the source: <c>[Start, End)</c> replaced by <paramref name="Text"/>; a span edit replaces a whole construct (its tags are not marked again).</summary>
    internal readonly record struct Edit(int Start, int End, string Text, bool Span);

    /// <summary>
    /// <paramref name="edits"/> in source order, those that would overlap one kept before them set apart (an insertion at a
    /// replacement's start comes first). None overlaps by construction; one that would is said, never applied half.
    /// </summary>
    internal static (List<Edit> Kept, List<Edit> Dropped) Order(IEnumerable<Edit> edits)
    {
        var kept = new List<Edit>();
        var dropped = new List<Edit>();
        foreach (var edit in edits.OrderBy(e => e.Start).ThenBy(e => e.End))
        {
            if (kept.Count > 0 && edit.Start < kept[^1].End)
                dropped.Add(edit);
            else
                kept.Add(edit);
        }

        return (kept, dropped);
    }

    /// <summary>Something the rewrite cannot decide, found at <paramref name="Offset"/> of the 30 body; its reason is made from the line it lands on in the 31 text.</summary>
    private sealed record Issue(int Offset, Func<int, string> Reason);

    /// <summary>
    /// The edits and issues of one range of the 30 body: the body itself, a lifted part's text (its own file member), or
    /// a hoisted or unrolled construct, whose issues land where the construct stood (<paramref name="redirect"/>).
    /// </summary>
    private sealed class Sink(int from, int to, Sink? redirect = null, int at = 0)
    {
        public int From { get; } = from;

        public int To { get; } = to;

        public List<Edit> Edits { get; } = [];

        public List<Issue> Issues { get; } = [];

        public void Add(int start, int end, string text) => Edits.Add(new Edit(start, end, text, false));

        public void Replace(int start, int end, string text) => Edits.Add(new Edit(start, end, text, true));

        public void Say(int offset, Func<int, string> reason)
        {
            if (redirect is not null)
                redirect.Say(at, reason);
            else
                Issues.Add(new Issue(offset, reason));
        }
    }

    /// <summary>A <c>for</c> variable in scope: the 30 loop node it iterates the elements of (part 04 § 2.6's <c>.items</c>), or none.</summary>
    private sealed record Variable(string? Loop);

    /// <summary>A member chain as written: its root variable, and each step's name (<c>null</c> for a computed index) and token.</summary>
    private sealed record Chain(ScriptVariable Root, IReadOnlyList<(string? Name, ScriptNode? Token)> Steps, IReadOnlyList<ScriptExpression> Indexes)
    {
        public string Name => Root.Name;

        public IReadOnlyList<string?> Names => Steps.Select(s => s.Name).ToList();
    }

    /// <summary>What an output's chain is at 31: a placement (<c>macro</c> or <c>snippet</c>, the macro's key), or a value written whole (its 31 path and whether a list).</summary>
    private sealed record Written(string? Placement, string? Macro, string? WholePath, bool WholeList);

    private static readonly Written Nothing = new(null, null, null, false);

    /// <summary>Part 05 § 2.1: the loop positions a body <c>if</c> may test.</summary>
    private static readonly IReadOnlySet<string> Positions = new HashSet<string>(StringComparer.Ordinal) { "first", "last", "index", "even", "odd" };

    /// <summary>The 30 renderers (C#), each a built-in renderer of the registry at 31 (part 05 § 5.3).</summary>
    private static readonly IReadOnlySet<string> Renderers30 = new HashSet<string>(StringComparer.Ordinal) { "concept_bullets", "concept_context" };

    private sealed class Run
    {
        private readonly string text;
        private readonly TemplateSite30 site;
        private readonly Migrated m;
        private readonly Dictionary<string, WorkflowNodeSpec> nodes30 = new(StringComparer.Ordinal);
        private readonly HashSet<int> placements = [];
        private readonly Lazy<ConversionScope> scope;
        private IReadOnlyList<TemplateTag> tags = [];
        private int nextPart = 1;

        // Inside an if already said as a hand edit, whose placements are that edit's.
        private int said;

        // The condition hoisted onto the placement being rewritten, while a hoisted branch is walked.
        private PlacementRule? hoisting;

        public Run(string text, TemplateSite30 site, Migrated m)
        {
            this.text = text;
            this.site = site;
            this.m = m;
            foreach (var node in m.Manifest30.Nodes ?? [])
                nodes30.TryAdd(node.Id, node);
            scope = new Lazy<ConversionScope>(() => m.Scope(site.OwnerId));
        }

        public JsonObject Parts { get; } = [];

        public List<MacroPlacement> Macros { get; } = [];

        private FormatSet Formats => scope.Value.Formats;

        /// <summary>The 31 body: unreadable text is kept as it is with one hand edit (A10-038).</summary>
        public string Rewrite()
        {
            try
            {
                var (found, unclosed) = TemplateTags.Scan(text);
                if (unclosed is { } line)
                {
                    m.HandEdit(site.File, "body", MigrationSentences.Unreadable(line));
                    return text;
                }

                var template = Template.Parse(text);
                if (template.HasErrors || template.Page is null)
                {
                    var first = template.Messages.FirstOrDefault(e => e.Type == Scriban.Parsing.ParserMessageType.Error);
                    m.HandEdit(site.File, "body", MigrationSentences.Unreadable(first is null ? 1 : Math.Max(first.Span.Start.Line, 0) + 1));
                    return text;
                }

                tags = found;
                var root = new Sink(0, text.Length);
                Block(template.Page.Body, ImmutableDictionary<string, Variable>.Empty, root, 0);
                return Finish(root, "body");
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // The parser is Scriban's, on text it may not have been written for: the body is kept as 30 wrote it.
                Parts.Clear();
                m.HandEdit(site.File, "body", MigrationSentences.Unreadable(1));
                return text;
            }
        }

        // ----- statements -------------------------------------------------------------------------------------

        private void Block(ScriptBlockStatement? block, ImmutableDictionary<string, Variable> vars, Sink sink, int depth)
        {
            if (block is null)
                return;
            foreach (var statement in block.Statements)
                Statement(statement, vars, sink, depth);
        }

        private void Statement(ScriptStatement? statement, ImmutableDictionary<string, Variable> vars, Sink sink, int depth)
        {
            switch (statement)
            {
                case null or ScriptRawStatement or ScriptEscapeStatement or ScriptEndStatement or ScriptNopStatement:
                    return;
                case ScriptExpressionStatement output:
                    Output(output, vars, sink, depth);
                    return;
                case ScriptIfStatement chain:
                    If(chain, vars, sink, depth);
                    return;
                case ScriptForStatement loop:
                    For(loop, vars, sink, depth);
                    return;
                default:
                    Visit(statement, vars, sink, depth);
                    return;
            }
        }

        /// <summary>Any other statement (a <c>case</c>, a <c>while</c>, a <c>capture</c>): its reads rewritten; the dialect's faults are said at the end.</summary>
        private void Visit(ScriptNode node, ImmutableDictionary<string, Variable> vars, Sink sink, int depth)
        {
            foreach (var child in node.Children)
            {
                switch (child)
                {
                    case null:
                        break;
                    case ScriptBlockStatement block:
                        Block(block, vars, sink, depth + 1);
                        break;
                    case ScriptStatement statement:
                        Statement(statement, vars, sink, depth);
                        break;
                    case ScriptExpression expression:
                        Expr(expression, vars, sink, false);
                        break;
                    default:
                        Visit(child, vars, sink, depth);
                        break;
                }
            }
        }

        /// <summary>
        /// One output: its reads rewritten; a placement noted (its tag's marks, the macro it places), and said when it stands
        /// inside an <c>if</c> or a <c>for</c> that stays (A07-009 refuses it there: A10-005); a value written whole said (A10-037).
        /// </summary>
        private void Output(ScriptExpressionStatement output, ImmutableDictionary<string, Variable> vars, Sink sink, int depth)
        {
            if (output.Expression is not { } expression)
                return;
            var head = DialectRules.Unwrap(expression);
            if (head is not (ScriptMemberExpression or ScriptVariable or ScriptIndexerExpression) || Flatten(head) is not { } chain)
            {
                Expr(expression, vars, sink, false);
                return;
            }

            var written = Read(chain, head, vars, sink, false);
            if (written.Placement is not null && TagAt(output.Span.Start.Offset) is { } tag)
                placements.Add(tag.Start);
            if (written.Macro is { } macro)
                Macros.Add(new MacroPlacement(macro, site.OwnerId, site.File, hoisting));
            if (written.Placement is not null && depth > 0 && said == 0)
                sink.Say(Start(head), line => MigrationSentences.NotInDialect("a placement inside an if or a for", line));
            if (written.WholePath is { } path)
                sink.Say(Start(head), line => MigrationSentences.WritesWhole(path, line, written.WholeList));
        }

        // ----- if ------------------------------------------------------------------------------------------------

        private enum Test
        {
            Fact,
            Value,
            Mixed,
        }

        /// <summary>
        /// Part 05 § 2.5: an <c>if</c> chain that tests only recorded facts stays in the body, its reads rewritten. One that
        /// tests a value is, in a prompt, hoisted off the one macro it places or lifted into parts when it is simple (at the
        /// top level, on its own lines, no <c>else if</c>, its branches only text and outputs); otherwise it stays as 30
        /// wrote it, a hand edit (A10-001; A10-035 in a body that has no parts).
        /// </summary>
        private void If(ScriptIfStatement node, ImmutableDictionary<string, Variable> vars, Sink sink, int depth)
        {
            var conditions = new List<ScriptExpression?>();
            var bodies = new List<ScriptBlockStatement?>();
            ScriptElseStatement? otherwise = null;
            for (ScriptConditionStatement? branch = node; branch is not null;)
            {
                if (branch is ScriptIfStatement test)
                {
                    conditions.Add(test.Condition);
                    bodies.Add(test.Then);
                    branch = test.Else;
                }
                else
                {
                    otherwise = branch as ScriptElseStatement;
                    branch = null;
                }
            }

            var tests = conditions.Select(c => Classify(c, vars)).ToList();
            if (tests.All(t => t == Test.Fact))
            {
                // 30's skipped is a state test at 31, which must handle every state or end with an else (S05-009): an
                // empty else keeps 30's bytes. It takes the end tag's left mark, so the branch before it is trimmed as before.
                if (otherwise is null && conditions.Any(c => HasSkipped(c, vars)) && TagAt(End(node)) is { } last)
                    sink.Add(last.Start, last.Start, "{{" + (last.TildeLeft || last.DashLeft ? "~" : string.Empty) + " else }}");
                Keep(conditions, bodies, otherwise, vars, sink, depth);
                return;
            }

            if (TagAt(Start(node)) is not { } ifTag || TagAt(End(node)) is not { } endTag)
            {
                // Every statement stands in a tag the lexer found; if one ever does not, the if is still an edit.
                sink.Say(Start(node), MigrationSentences.IfTooComplex);
                Said(() => Keep(conditions, bodies, otherwise, vars, sink, depth));
                return;
            }

            if (site.Site != TemplateSite.Prompt)
            {
                var words = site.Site switch
                {
                    TemplateSite.SnippetBody => "snippet",
                    TemplateSite.MacroBody => "macro",
                    TemplateSite.Rendered => "rendered node",
                    _ => "template",
                };
                sink.Say(ifTag.Start, line => MigrationSentences.IfWithoutParts(line, words));
                Said(() => Keep(conditions, bodies, otherwise, vars, sink, depth));
                return;
            }

            var elseTag = otherwise is null ? null : TagAt(Start(otherwise));
            var simple = depth == 0 && conditions.Count == 1 && tests[0] == Test.Value && ifTag.Start != endTag.Start
                && (otherwise is null || (elseTag is not null && elseTag.Start != ifTag.Start && elseTag.Start != endTag.Start))
                && !ifTag.TextBefore && !endTag.TextAfter
                && Plain(node.Then) && (otherwise is null || Plain(otherwise.Body));
            if (!simple)
            {
                sink.Say(ifTag.Start, MigrationSentences.IfTooComplex);
                Said(() => Keep(conditions, bodies, otherwise, vars, sink, depth));
                return;
            }

            var condition = Slice(conditions[0]!);
            if (otherwise is null && Hoistable(node.Then, vars) is { } placed)
            {
                Hoist(node, placed, condition, ifTag, endTag, vars, sink, depth);
                return;
            }

            Lift(node, otherwise, condition, ifTag, elseTag, endTag, vars, sink, depth);
        }

        /// <summary>Runs <paramref name="walk"/> inside a construct already said as a hand edit.</summary>
        private void Said(Action walk)
        {
            said++;
            try
            {
                walk();
            }
            finally
            {
                said--;
            }
        }

        /// <summary>An <c>if</c> chain left where it is: its tests and its branches' reads rewritten.</summary>
        private void Keep(
            List<ScriptExpression?> conditions, List<ScriptBlockStatement?> bodies, ScriptElseStatement? otherwise,
            ImmutableDictionary<string, Variable> vars, Sink sink, int depth)
        {
            for (var i = 0; i < conditions.Count; i++)
            {
                Expr(conditions[i], vars, sink, false);
                Block(bodies[i], vars, sink, depth + 1);
            }

            Block(otherwise?.Body, vars, sink, depth + 1);
        }

        /// <summary>
        /// R6: the body <c>if</c> around the one macro placement becomes the placement alone (<c>{{ macro.x }}</c>), and its
        /// condition, converted at the node's place, is that placement's rule (<see cref="MacroPlacement.Rule"/>).
        /// </summary>
        private void Hoist(
            ScriptIfStatement node, string macro, string condition, TemplateTag ifTag, TemplateTag endTag,
            ImmutableDictionary<string, Variable> vars, Sink sink, int depth)
        {
            var logic = Migrate.Condition(m, MacroFile(macro), "if", condition, scope.Value, ConversionKind.Condition);
            var (from, to) = Inner(ifTag, endTag);
            var inner = new Sink(from, to, sink, ifTag.Start);
            hoisting = new PlacementRule(logic, condition);
            try
            {
                Block(node.Then, vars, inner, depth);
            }
            finally
            {
                hoisting = null;
            }

            var (start, end) = Outer(ifTag, endTag);
            sink.Replace(start, end, Apply(inner).Text);
        }

        /// <summary>
        /// Part 05 § 7: the body <c>if</c> becomes a part placed where it stood, its text the branch's (30's bytes, the
        /// whitespace its marks removed taken off), its <c>if</c> the condition converted at the node's place; with an
        /// <c>else</c>, a second part of the negation, placed after it.
        /// </summary>
        private void Lift(
            ScriptIfStatement node, ScriptElseStatement? otherwise, string condition, TemplateTag ifTag, TemplateTag? elseTag, TemplateTag endTag,
            ImmutableDictionary<string, Variable> vars, Sink sink, int depth)
        {
            var placed = new StringBuilder();
            placed.Append(Part(node.Then, condition, ifTag, elseTag ?? endTag, vars, depth));
            if (otherwise is not null)
                placed.Append(Part(otherwise.Body, $"!({condition})", elseTag!, endTag, vars, depth));
            var (start, end) = Outer(ifTag, endTag);
            sink.Replace(start, end, placed.ToString());
        }

        private string Part(ScriptBlockStatement? body, string condition, TemplateTag open, TemplateTag close, ImmutableDictionary<string, Variable> vars, int depth)
        {
            var id = NextPartId();
            var logic = Migrate.Condition(m, site.File, $"parts.{id}.if", condition, scope.Value, ConversionKind.Condition);
            var (from, to) = Inner(open, close);
            // A part's text is a template of its own, placed at the top level: its outputs stand at depth 0.
            var inner = new Sink(from, to);
            Block(body, vars, inner, 0);
            var lines = Finish(inner, $"parts.{id}.text").Split('\n');
            Parts[id] = new JsonObject { ["if"] = logic, ["text"] = new JsonArray([.. lines.Select(l => (JsonNode?)JsonValue.Create(l))]) };
            return "{{ part." + id + " }}";
        }

        /// <summary>Part 05 § 7.1: <c>when_&lt;n&gt;</c>, the first <c>n</c> from the last that no part has and the body does not read as <c>part.&lt;id&gt;</c>.</summary>
        private string NextPartId()
        {
            while (true)
            {
                var id = "when_" + nextPart.ToString(System.Globalization.CultureInfo.InvariantCulture);
                nextPart++;
                // A whole token only: part.when_1 is not taken by part.when_10.
                if (!Parts.ContainsKey(id) && !System.Text.RegularExpressions.Regex.IsMatch(text, @"(?<![A-Za-z0-9_.])part\." + id + "(?![A-Za-z0-9_])"))
                    return id;
            }
        }

        /// <summary>The macro a branch places, when it holds that one placement (<c>macro.x.prompt</c> at 30) and white space only.</summary>
        private string? Hoistable(ScriptBlockStatement? body, ImmutableDictionary<string, Variable> vars)
        {
            string? macro = null;
            foreach (var statement in body?.Statements ?? [])
            {
                switch (statement)
                {
                    case ScriptRawStatement raw when string.IsNullOrWhiteSpace(Slice(raw)):
                    case ScriptEscapeStatement or ScriptEndStatement or ScriptNopStatement:
                        continue;
                    case ScriptExpressionStatement { Expression: { } expression } when macro is null
                        && Flatten(DialectRules.Unwrap(expression)) is { } chain && !vars.ContainsKey(chain.Name) && MacroOf(chain) is { } named:
                        macro = named;
                        continue;
                    default:
                        return null;
                }
            }

            return macro;
        }

        /// <summary>Whether a branch holds only text and outputs (no statement to lift with it).</summary>
        private static bool Plain(ScriptBlockStatement? body) =>
            (body?.Statements ?? []).All(s => s is null or ScriptRawStatement or ScriptEscapeStatement or ScriptEndStatement or ScriptNopStatement or ScriptExpressionStatement);

        /// <summary>
        /// Part 05 § 2.5: whether a test is of recorded facts only (a loop position; an item's state, which 30 wrote as
        /// <c>.skipped</c>; whether a check passed), of values only, or of both.
        /// </summary>
        private Test Classify(ScriptExpression? condition, ImmutableDictionary<string, Variable> vars)
        {
            var facts = new List<bool>();
            Leaves(condition, vars, facts);
            return facts.Count == 0 || facts.All(f => !f) ? Test.Value : facts.All(f => f) ? Test.Fact : Test.Mixed;
        }

        private void Leaves(ScriptExpression? expression, ImmutableDictionary<string, Variable> vars, List<bool> facts)
        {
            if (expression is null)
                return;
            switch (DialectRules.Unwrap(expression))
            {
                case ScriptBinaryExpression { Operator: ScriptBinaryOperator.And or ScriptBinaryOperator.Or } both:
                    Leaves(both.Left, vars, facts);
                    Leaves(both.Right, vars, facts);
                    return;
                case ScriptUnaryExpression { Operator: ScriptUnaryOperator.Not } not:
                    Leaves(not.Right, vars, facts);
                    return;
                case ScriptBinaryExpression { Operator: ScriptBinaryOperator.CompareEqual or ScriptBinaryOperator.CompareNotEqual } compared:
                {
                    var left = compared.Left is null ? null : DialectRules.Unwrap(compared.Left);
                    var right = compared.Right is null ? null : DialectRules.Unwrap(compared.Right);
                    var (tested, literal) = left is ScriptLiteral l ? (right, l) : (left, right as ScriptLiteral);
                    facts.Add(literal is not null && Flatten(tested) is { } chain && Fact(chain, vars, literal));
                    return;
                }

                case var other:
                    facts.Add(Flatten(other) is { } bare && Fact(bare, vars, null));
                    return;
            }
        }

        private bool Fact(Chain chain, ImmutableDictionary<string, Variable> vars, ScriptLiteral? literal)
        {
            var names = chain.Names;
            if (chain.Name == "for")
                return names is [{ } position] && Positions.Contains(position) && (position != "index" || literal is { Value: not string });
            if (vars.TryGetValue(chain.Name, out var variable))
            {
                if (variable.Loop is not null && names is ["skipped"])
                    return literal is null;
                return names.Count > 0 && names[^1] == "state" && literal is { Value: string };
            }

            return chain.Name == "node" && names is [{ } id, "value", "pass"] && IsCheck(id) && literal is null or { Value: bool };
        }

        // ----- for -----------------------------------------------------------------------------------------------

        private void For(ScriptForStatement node, ImmutableDictionary<string, Variable> vars, Sink sink, int depth)
        {
            var source = node.Iterator is null ? null : DialectRules.Unwrap(node.Iterator);
            var chain = Flatten(source);
            if (chain is not null && !vars.ContainsKey(chain.Name) && chain.Name == "macro" && chain.Names is [{ } folder] && FolderOf(folder) is { } items)
            {
                Unroll(node, folder, items, vars, sink, depth);
                return;
            }

            string? loop = null;
            if (chain is not null && !vars.ContainsKey(chain.Name) && chain.Name == "node" && chain.Names is [{ } id]
                && nodes30.TryGetValue(id, out var node30) && node30.For is not null && chain.Steps[0].Token is { } token)
            {
                // Part 04 § 2.6: a 30 for over a loop node iterated its items; at 31 that is its .items.
                sink.Add(End(token), End(token), ".items");
                loop = id;
            }
            else
            {
                Expr(source, vars, sink, false);
            }

            foreach (var option in node.NamedArguments ?? [])
                Expr(option.Value, vars, sink, false);

            var inner = node.Variable is ScriptVariable named ? vars.SetItem(named.Name, new Variable(loop)) : vars;
            Block(node.Body, inner, sink, depth + 1);
            Block(node.Else?.Body, inner, sink, depth + 1);
        }

        /// <summary>
        /// R6: a <c>for</c> over a macro folder is written out once per folder macro in the folder's order, its variable's
        /// <c>.label</c> (or <c>.name</c>) and <c>.id</c> as the macro's text, <c>.prompt</c> as the placement
        /// <c>macro.&lt;folder&gt;.&lt;id&gt;</c> and <c>.content</c> as <c>macro.&lt;folder&gt;.&lt;id&gt;.content</c>. One it
        /// cannot write out (not at a prompt's top level, a statement or another read of the variable in its body) is a hand
        /// edit (A10-005).
        /// </summary>
        private void Unroll(
            ScriptForStatement node, string folder, IReadOnlyList<(string Id, string Label)> items, ImmutableDictionary<string, Variable> vars, Sink sink, int depth)
        {
            var variable = node.Variable is ScriptVariable named ? named.Name : null;
            var forTag = TagAt(Start(node));
            var endTag = TagAt(End(node));
            var outputs = (node.Body?.Statements ?? []).OfType<ScriptExpressionStatement>().ToList();
            var ok = site.Site == TemplateSite.Prompt && depth == 0 && variable is not null && forTag is not null && endTag is not null
                && forTag.Start != endTag.Start && node.Else is null && (node.NamedArguments?.Count ?? 0) == 0 && Plain(node.Body)
                && outputs.All(o => Member(o, variable) is null or "label" or "name" or "id" or "prompt" or "content")
                && outputs.All(o => Member(o, variable) is not null || !Reads(o, variable));
            if (!ok)
            {
                sink.Say(Start(node), line => MigrationSentences.NotInDialect($"a for over macro.{folder}", line));
                Said(() => Block(node.Body, variable is null ? vars : vars.SetItem(variable, new Variable(null)), sink, depth + 1));
                return;
            }

            var (from, to) = Inner(forTag!, endTag!);
            var unrolled = new StringBuilder();
            foreach (var (id, label) in items)
            {
                var inner = new Sink(from, to, sink, forTag!.Start);
                foreach (var output in outputs)
                {
                    var expression = output.Expression!;
                    switch (Member(output, variable!))
                    {
                        case "label" or "name":
                            Literal(inner, output, label);
                            break;
                        case "id":
                            Literal(inner, output, id);
                            break;
                        case "prompt":
                            inner.Add(Start(expression), End(expression), $"macro.{folder}.{id}");
                            if (TagAt(Start(output)) is { } tag)
                                placements.Add(tag.Start);
                            Macros.Add(new MacroPlacement($"{folder}/{id}", site.OwnerId, site.File, null));
                            break;
                        case "content":
                            // D4: a folder macro never becomes a snippet, so its fixed-text read has no 31 form: written
                            // macro.<folder>.<id>_fixed_text, an item the folder lacks, which 31 refuses (S05-011), with a
                            // hand edit. (A member after a folder item, macro.<folder>.<id>.fixed_text, is not checked by
                            // 31's reads, so it would pass.)
                            var unread = $"{id}_{FixedText}";
                            while (items.Any(i => i.Id == unread))
                                unread += "_" + FixedText;
                            inner.Add(Start(expression), End(expression), $"macro.{folder}.{unread}");
                            var key = $"{folder}/{id}";
                            var both = outputs.Any(o => Member(o, variable!) == "prompt");
                            inner.Say(Start(expression), line => both ? MigrationSentences.ReadBothWays(key) : MigrationSentences.FolderReadAsFixedText(key, line));
                            break;
                        default:
                            Output(output, vars, inner, depth);
                            break;
                    }
                }

                unrolled.Append(Apply(inner).Text);
            }

            var (start, end) = Outer(forTag!, endTag!);
            sink.Replace(start, end, unrolled.ToString());
        }

        /// <summary>The whole tag of <paramref name="output"/> written as <paramref name="value"/>: as text, or as a string output when it holds a brace pair.</summary>
        private void Literal(Sink sink, ScriptExpressionStatement output, string value)
        {
            if (TagAt(Start(output)) is not { } tag)
                return;
            var braces = value.Contains("{{", StringComparison.Ordinal) || value.Contains("}}", StringComparison.Ordinal)
                || value.Contains("{%", StringComparison.Ordinal) || value.Contains("%}", StringComparison.Ordinal);
            sink.Replace(tag.Start, tag.End, braces ? "{{ \"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\" }}" : value);
        }

        /// <summary>The member an output reads of <paramref name="variable"/> when it is exactly <c>&lt;variable&gt;.&lt;member&gt;</c>, else null.</summary>
        private static string? Member(ScriptExpressionStatement output, string variable) =>
            output.Expression is { } expression && Flatten(DialectRules.Unwrap(expression)) is { } chain && chain.Name == variable && chain.Names is [{ } member]
                ? member
                : null;

        /// <summary>Whether an output reads <paramref name="variable"/> anywhere.</summary>
        private static bool Reads(ScriptNode node, string variable) => node switch
        {
            ScriptMemberExpression access => access.Target is { } target && Reads(target, variable),
            ScriptVariable read => read.Name == variable,
            _ => node.Children.Any(c => c is not null && Reads(c, variable)),
        };

        // ----- expressions ---------------------------------------------------------------------------------------

        private void Expr(ScriptNode? node, ImmutableDictionary<string, Variable> vars, Sink sink, bool wrap)
        {
            switch (node)
            {
                case null or ScriptLiteral:
                    return;
                case ScriptNestedExpression nested:
                    Expr(nested.Expression, vars, sink, false);
                    return;
                case ScriptPipeCall pipe:
                    Pipe(pipe, vars, sink);
                    return;
                case ScriptUnaryExpression { Operator: ScriptUnaryOperator.Not, Right: { } right } not
                    when Flatten(DialectRules.Unwrap(right)) is { } chain && Skipped(chain, vars):
                    // Part 04 § 2.6: 30's !e.skipped is the item's state at 31.
                    sink.Add(Start(not), End(not), Wrapped($"{chain.Name}.state != \"skipped\"", wrap));
                    return;
                case ScriptUnaryExpression unary:
                    Expr(unary.Right, vars, sink, true);
                    return;
                case ScriptBinaryExpression binary:
                    var logical = binary.Operator is ScriptBinaryOperator.And or ScriptBinaryOperator.Or;
                    Expr(binary.Left, vars, sink, !logical);
                    Expr(binary.Right, vars, sink, !logical);
                    return;
                case ScriptMemberExpression or ScriptVariable or ScriptIndexerExpression when Flatten((ScriptExpression)node) is { } read:
                    Read(read, (ScriptExpression)node, vars, sink, wrap);
                    return;
                case ScriptMemberExpression access:
                    // A member of what is no read (a call's result): its name is no read either.
                    Expr(access.Target, vars, sink, true);
                    return;
                case ScriptIndexerExpression indexer:
                    Expr(indexer.Target, vars, sink, true);
                    Expr(indexer.Index, vars, sink, false);
                    return;
                case ScriptFunctionCall call:
                    // A call's target is a name, not a read; a call outside a pipe is the dialect's to refuse.
                    foreach (var argument in call.Arguments ?? [])
                        Expr(argument, vars, sink, true);
                    return;
                default:
                    foreach (var child in node.Children)
                        Expr(child, vars, sink, wrap);
                    return;
            }
        }

        /// <summary>
        /// Part 05 § 5.2: a pipe into a 30 renderer is a pipe into <c>renderer.&lt;id&gt;</c> of the node's <c>.value</c>;
        /// every other pipe keeps its steps, its head read as any read is.
        /// </summary>
        private void Pipe(ScriptPipeCall pipe, ImmutableDictionary<string, Variable> vars, Sink sink)
        {
            var (head, steps) = Decompose(pipe);
            var renderer = false;
            foreach (var step in steps)
            {
                var (target, arguments) = step is ScriptFunctionCall { Target: { } called } call ? (called, call.Arguments?.ToList() ?? []) : (step, []);
                if (target is ScriptVariable { Name: var name } && Renderers30.Contains(name))
                {
                    sink.Add(Start(target), End(target), "renderer." + name);
                    renderer = true;
                }

                foreach (var argument in arguments)
                    Expr(argument, vars, sink, true);
            }

            if (renderer && Flatten(head) is { Name: "node" } chain && !vars.ContainsKey("node") && chain.Steps is [({ } id, { } token), ..] && m.NodeReads.ContainsKey(id))
            {
                switch (chain.Steps)
                {
                    case [_]:
                        sink.Add(End(token), End(token), ".value");
                        return;
                    case [_, ("content", { } content)]:
                        sink.Add(Start(content), End(content), "value");
                        return;
                }
            }

            Expr(head, vars, sink, true);
        }

        /// <summary>
        /// One read, rewritten as part 10 § 1.2 and part 05 say (see the class), or said as a hand edit when 31 has no form
        /// for it (<c>item.label</c> with no <c>itemLabel</c>, a 30 run fact 31 lacks: A10-005; a prompt's
        /// <c>profile.*</c>: S10-004; a prompt's, a <c>rendered</c> node's or a macro body's <c>deliverable.*</c>, or a <c>rendered</c> node's <c>profile.*</c>: A10-039).
        /// </summary>
        private Written Read(Chain chain, ScriptExpression whole, ImmutableDictionary<string, Variable> vars, Sink sink, bool wrap)
        {
            foreach (var index in chain.Indexes)
                Expr(index, vars, sink, false);

            var names = chain.Names;
            var steps = chain.Steps;
            if (vars.TryGetValue(chain.Name, out var variable))
            {
                if (Skipped(chain, vars))
                    sink.Add(Start(whole), End(whole), Wrapped($"{chain.Name}.state == \"skipped\"", wrap));
                else if (variable.Loop is { } loop && steps.Count > 0 && steps[0] is ({ } member, { } token) && BodyOf(loop).Contains(member))
                    sink.Add(Start(token), Start(token), "nodes.");
                return Nothing;
            }

            switch (chain.Name)
            {
                case "input" when steps.Count > 0 && steps[0] is ({ } id, { } token) && (m.Manifest["inputs"]?[id]) is JsonObject input:
                {
                    if (steps.Count > 1)
                    {
                        sink.Add(End(token), End(token), ".value");
                        return Nothing;
                    }

                    var format = input["format"];
                    var text = format is not null && FormatTypes.TextKind(format, Formats) is not null;
                    sink.Add(End(token), End(token), text ? ".content" : ".value");
                    return text ? Nothing : WholeOf($"input.{id}.value", format);
                }

                case "node" when steps.Count > 0 && steps[0] is ({ } id, { } token) && m.NodeReads.TryGetValue(id, out var read):
                {
                    if (read.Path.StartsWith("check.", StringComparison.Ordinal))
                        sink.Add(Start(chain.Root), End(chain.Root), "check");
                    var root = read.Path.StartsWith("check.", StringComparison.Ordinal) ? "check" : "node";
                    if (nodes30.TryGetValue(id, out var node30) && node30.Format == "classification")
                    {
                        // Part 10 § 2.2: 30's classification read as its text; at 31 it is { value: string }.
                        if (steps.Count == 1)
                            sink.Add(End(token), End(token), ".value.value");
                        else if (steps is [_, ("value", { } value)])
                            sink.Add(End(value), End(value), ".value");
                        return Nothing;
                    }

                    if (steps.Count > 1)
                        return Nothing;
                    var loop = nodes30.TryGetValue(id, out var flow) && (flow.For is not null || flow.While is not null);
                    var content = loop || FormatTypes.TextKind(read.Format, Formats) is not null;
                    sink.Add(End(token), End(token), content ? ".content" : ".value");
                    return content ? Nothing : WholeOf($"{root}.{id}.value", read.Format);
                }

                case "item" when names is ["label"]:
                    if (site.ItemLabel is { } field && steps[0].Token is { } label)
                        sink.Add(Start(label), End(label), "value." + field);
                    else
                        sink.Say(Start(whole), line => MigrationSentences.NotInDialect("item.label", line));
                    return Nothing;
                case "item" when names.Count > 1 && names[^1] == "label" && names.Take(names.Count - 1).All(n => n == "parent"):
                    sink.Say(Start(whole), line => MigrationSentences.NotInDialect(string.Join('.', ["item", .. names]), line));
                    return Nothing;
                case "profile" when site.Site == TemplateSite.Prompt:
                    var field30 = names.Count > 0 ? names[0] ?? string.Empty : string.Empty;
                    sink.Say(Start(whole), _ => MigrationSentences.ProfileInPrompt(field30));
                    return Nothing;
                case "profile" or "deliverable" when site.Site is TemplateSite.Prompt or TemplateSite.Rendered
                    || (chain.Name == "deliverable" && site.Site == TemplateSite.MacroBody):
                    // Part 06 § 6.3: a macro body never reads a deliverable, nor does a deliverable read another, so a 30
                    // deliverable macro's read (written as a snippet when D4 makes it one) stays, said where it is.
                    var path = string.Join('.', [chain.Name, .. names.Select(n => n ?? "[]")]);
                    var words = site.Site switch
                    {
                        TemplateSite.Prompt => "prompt",
                        TemplateSite.Rendered => "rendered node",
                        _ => site.File.StartsWith("snippets/", StringComparison.Ordinal) ? "deliverable" : "macro body",
                    };
                    sink.Say(Start(whole), line => MigrationSentences.ReadNotAllowed(path, line, words));
                    return Nothing;
                case "run" when names is [{ } fact, ..] && fact is "job" or "host":
                    sink.Say(Start(whole), line => MigrationSentences.NotInDialect("run." + fact, line));
                    return Nothing;
                case "snippet" when steps.Count == 1 && steps[0] is ({ } id, { } token) && m.Manifest30.Snippets?.Any(s => s.Key == id && !s.Value.EndsWith('/')) == true:
                    // Part 05 § 6.3: a snippet's text is read as .content.
                    sink.Add(End(token), End(token), ".content");
                    return new Written("snippet", null, null, false);
                case "snippet" when names is [_, "content"] or [_, _, "content"]:
                    return new Written("snippet", null, null, false);
                case "macro" when chain.Names is [{ } fixedText, "content"] && chain.Steps.All(s => s.Token is not null)
                    && m.Manifest30.Macros?.Any(x => x.Id == fixedText && !x.File.EndsWith('/')) == true:
                    // D4: 30's fixed-text read. A macro that becomes a snippet is read as one; any other is a marker at 31.
                    if (Migrate.Renames.BecomesSnippet(m, fixedText))
                    {
                        sink.Add(Start(chain.Root), End(chain.Root), "snippet");
                        return new Written("snippet", null, null, false);
                    }

                    // D3: no 31 form, so it is written macro.<id>.fixed_text, which 31 refuses (S05-011) until the edit is made;
                    // left as macro.<id>.content it would pass as a read.
                    sink.Add(End(steps[^2].Token!), End(steps[^1].Token!), "." + FixedText);
                    var both = m.Walk30.Templates.Any(t => t.References.MacroPrompts.Contains(fixedText));
                    sink.Say(Start(whole), line => both ? MigrationSentences.ReadBothWays(fixedText) : MigrationSentences.ReadAsFixedText(fixedText, line));
                    return Nothing;
                case "macro" when MacroOf(chain) is { } macro:
                    // Part 05 § 10: 30's {{ macro.x.prompt }} is the placement {{ macro.x }}.
                    sink.Add(End(steps[^2].Token!), End(steps[^1].Token!), string.Empty);
                    return new Written("macro", macro, null, false);
                default:
                    return Nothing;
            }
        }

        /// <summary>A bare read written whole: said when it is an object or a list at 31 (A10-037).</summary>
        private Written WholeOf(string path, JsonNode? format) => FormatTypes.Shape(format, Formats) switch
        {
            FormatShape.List => new Written(null, null, path, true),
            FormatShape.Object => new Written(null, null, path, false),
            _ => Nothing,
        };

        /// <summary>The macro a 30 <c>macro.&lt;id&gt;.prompt</c> or <c>macro.&lt;folder&gt;.&lt;id&gt;.prompt</c> places (<c>&lt;id&gt;</c>, <c>&lt;folder&gt;/&lt;id&gt;</c>), else null.</summary>
        private static string? MacroOf(Chain chain) => chain.Name != "macro" || chain.Steps.Any(s => s.Token is null) ? null : chain.Names switch
        {
            [{ } id, "prompt"] => id,
            [{ } folder, { } id, "prompt"] => $"{folder}/{id}",
            _ => null,
        };

        private static bool Skipped(Chain chain, ImmutableDictionary<string, Variable> vars) =>
            vars.TryGetValue(chain.Name, out var variable) && variable.Loop is not null && chain.Names is ["skipped"];

        /// <summary>Whether a test reads a loop element's 30 <c>skipped</c> anywhere.</summary>
        private static bool HasSkipped(ScriptNode? node, ImmutableDictionary<string, Variable> vars) => node switch
        {
            null => false,
            ScriptExpression expression when Flatten(expression) is { } chain => Skipped(chain, vars),
            _ => node.Children.Any(c => HasSkipped(c, vars)),
        };

        private static string Wrapped(string test, bool wrap) => wrap ? "(" + test + ")" : test;

        // ----- the package -----------------------------------------------------------------------------------------

        /// <summary>A 30 loop's direct body ids, which its elements read by id (at 31 through <c>.nodes</c>).</summary>
        private IReadOnlyList<string> BodyOf(string loop) => nodes30.TryGetValue(loop, out var node) ? node.Nodes ?? [] : [];

        private bool IsCheck(string id) => m.NodeReads.TryGetValue(id, out var read) && read.Path.StartsWith("check.", StringComparison.Ordinal);

        /// <summary>A 30 macro folder's items in its index's order, each with its label (its header's, else its id); null when <paramref name="folder"/> names none.</summary>
        private IReadOnlyList<(string Id, string Label)>? FolderOf(string folder)
        {
            if (m.Manifest30.Macros?.FirstOrDefault(x => x.Id == folder && x.File.EndsWith('/')) is not { } spec
                || !m.Files30.TryGetValue(spec.File + WorkflowDataResolver.IndexFileName, out var index))
                return null;
            var read = WorkflowTextHeaders.ReadIndex(index, folder, "Macro", m.Manifest30.SpecVersion, []);
            return read?.Items.Select(i => (i.Id, m.Resolved30.Headers.GetValueOrDefault(spec.File + i.File)?.Label ?? i.Id)).ToList();
        }

        /// <summary>The 31 file of macro <paramref name="key"/> (<c>&lt;id&gt;</c> or <c>&lt;folder&gt;/&lt;id&gt;</c>), as 30's.</summary>
        private string MacroFile(string key)
        {
            var slash = key.IndexOf('/', StringComparison.Ordinal);
            if (slash < 0)
                return m.Manifest30.Macros?.FirstOrDefault(x => x.Id == key && !x.File.EndsWith('/'))?.File ?? $"macros/{key}.md";
            return $"macros/{key}.md";
        }

        // ----- text ------------------------------------------------------------------------------------------------

        /// <summary>
        /// A sink's range of the 30 body with its edits applied (a <c>-</c> on a tag that stays made a <c>~</c>, a placement
        /// given the form § 2.6 allows), each issue said as a hand edit on <paramref name="member"/> at the line it lands
        /// on, and each fault the dialect still finds (<see cref="TemplateSyntax"/>) one more (A10-005, A10-038).
        /// </summary>
        private string Finish(Sink sink, string member)
        {
            var (result, map) = Apply(sink);
            foreach (var issue in sink.Issues)
                m.HandEdit(site.File, member, issue.Reason(LineOf(result, map(issue.Offset))));

            foreach (var fault in TemplateSyntax.Parse(result).Faults)
            {
                var reason = fault.Id is "A07-005" or "A08-006"
                    ? MigrationSentences.Unreadable(fault.Line)
                    : MigrationSentences.NotInDialect(Construct(fault), fault.Line);
                m.HandEdit(site.File, member, reason);
            }

            return result;
        }

        /// <summary>The construct a dialect fault names, as written (<c>case</c>, <c>string.replace</c>, <c>truncate</c>).</summary>
        private static string Construct(DialectFault fault)
        {
            string Fill(int i) => fault.Fill.Count > i ? fault.Fill[i] : string.Empty;
            return fault.Id switch
            {
                "S05-001" or "S05-002" or "A07-004" => Fill(0),
                "S05-003" => "truncate",
                "S05-004" => "object.default",
                "S05-005" => "date.to_string without a pattern",
                "S05-006" => "a for over " + Fill(0),
                "S05-013" => "if and for nested " + Fill(0) + " deep",
                "S05-019" => "-",
                "S05-030" => $"renderer.{Fill(0)} called as a function",
                "S05-063" => $"{Fill(0)}[{Fill(1)}]",
                _ => fault.Id,
            };
        }

        /// <summary>A sink's text with its edits and its tags' marks applied, and where each 30 offset in it lands.</summary>
        private (string Text, Func<int, int> Map) Apply(Sink sink)
        {
            var edits = new List<Edit>(sink.Edits);
            var spans = sink.Edits.Where(e => e.Span).ToList();
            foreach (var tag in tags.Where(t => t.Start >= sink.From && t.End <= sink.To && !spans.Any(s => t.Start < s.End && t.End > s.Start)))
                Marks(tag, edits);

            var (kept, dropped) = Order(edits);
            foreach (var edit in dropped)
            {
                // Its 30 construct stays as it was: a hand edit, said where it stands.
                sink.Say(edit.Start, line => MigrationSentences.NotInDialect("a tag the migration could not rewrite", line));
            }

            var result = new StringBuilder();
            var at = sink.From;
            foreach (var edit in kept)
            {
                result.Append(text, at, edit.Start - at).Append(edit.Text);
                at = edit.End;
            }

            result.Append(text, at, sink.To - at);
            var length = result.Length;
            int Map(int offset)
            {
                // An offset inside a replaced span lands at its start; every edit wholly before it moves it.
                var moved = 0;
                foreach (var edit in kept)
                {
                    if (edit.Start <= offset && offset < edit.End)
                        return Math.Clamp(edit.Start - sink.From + moved, 0, length);
                    if (edit.End <= offset)
                        moved += edit.Text.Length - (edit.End - edit.Start);
                }

                return Math.Clamp(offset - sink.From + moved, 0, length);
            }

            return (result.ToString(), Map);
        }

        /// <summary>
        /// Part 05 § 2.6: <c>-</c> is refused on every tag, so a statement's or an ordinary output's becomes <c>~</c>; a
        /// placement keeps <c>{{~</c> only alone on its line and never <c>~}}</c>.
        /// </summary>
        private void Marks(TemplateTag tag, List<Edit> edits)
        {
            var placement = placements.Contains(tag.Start);
            var left = tag.Start + 2;
            var right = tag.End - 3;
            if (tag.DashLeft || tag.TildeLeft)
            {
                if (placement && (tag.TextBefore || tag.TextAfter))
                    edits.Add(new Edit(left, left + 1, string.Empty, false));
                else if (tag.DashLeft)
                    edits.Add(new Edit(left, left + 1, "~", false));
            }

            if ((tag.DashRight || tag.TildeRight) && right > left)
            {
                if (placement)
                    edits.Add(new Edit(right, right + 1, string.Empty, false));
                else if (tag.DashRight)
                    edits.Add(new Edit(right, right + 1, "~", false));
            }
        }

        /// <summary>The text between two tags, less what their marks remove (Scriban 7.2.5: <c>~</c> spaces and tabs, on the right also one newline; <c>-</c> all white space).</summary>
        private (int From, int To) Inner(TemplateTag open, TemplateTag close)
        {
            var from = open.End;
            if (open.DashRight)
            {
                while (from < close.Start && char.IsWhiteSpace(text[from]))
                    from++;
            }
            else if (open.TildeRight)
            {
                while (from < close.Start && text[from] is ' ' or '\t')
                    from++;
                if (from < close.Start && text[from] == '\n')
                    from++;
            }

            var to = close.Start;
            if (close.DashLeft)
            {
                while (to > from && char.IsWhiteSpace(text[to - 1]))
                    to--;
            }
            else if (close.TildeLeft)
            {
                while (to > from && text[to - 1] is ' ' or '\t')
                    to--;
            }

            return (from, Math.Max(from, to));
        }

        /// <summary>A construct's whole span, from its first tag to its last, with what their outer marks remove.</summary>
        private (int Start, int End) Outer(TemplateTag open, TemplateTag close)
        {
            var start = open.Start;
            if (open.DashLeft)
            {
                while (start > 0 && char.IsWhiteSpace(text[start - 1]) && !InTag(start - 1))
                    start--;
            }
            else if (open.TildeLeft)
            {
                while (start > 0 && text[start - 1] is ' ' or '\t')
                    start--;
            }

            var end = close.End;
            if (close.DashRight)
            {
                while (end < text.Length && char.IsWhiteSpace(text[end]) && !InTag(end))
                    end++;
            }
            else if (close.TildeRight)
            {
                while (end < text.Length && text[end] is ' ' or '\t')
                    end++;
                if (end < text.Length && text[end] == '\n')
                    end++;
            }

            return (start, end);
        }

        private bool InTag(int offset) => tags.Any(t => t.Start <= offset && offset < t.End);

        private TemplateTag? TagAt(int offset) => tags.FirstOrDefault(t => t.Start <= offset && offset < t.End);

        private string Slice(ScriptNode node) => text[Math.Clamp(Start(node), 0, text.Length)..Math.Clamp(End(node), Start(node), text.Length)];

        private static int Start(ScriptNode node) => Math.Max(node.Span.Start.Offset, 0);

        private static int End(ScriptNode node) => Math.Max(node.Span.End.Offset + 1, Start(node));

        private static int LineOf(string text, int offset)
        {
            var line = 1;
            for (var i = 0; i < offset && i < text.Length; i++)
            {
                if (text[i] == '\n')
                    line++;
            }

            return line;
        }

        /// <summary>A member chain (<c>node.x.value</c>, <c>input.x[0]</c>), or null when the expression is none.</summary>
        private static Chain? Flatten(ScriptExpression? expression)
        {
            var steps = new List<(string?, ScriptNode?)>();
            var indexes = new List<ScriptExpression>();
            var current = expression;
            while (true)
            {
                switch (current)
                {
                    case ScriptMemberExpression { Member: { } member } access:
                        steps.Insert(0, (member.Name, member));
                        current = access.Target;
                        continue;
                    case ScriptIndexerExpression indexer:
                        steps.Insert(0, (null, null));
                        if (indexer.Index is { } index)
                            indexes.Add(index);
                        current = indexer.Target;
                        continue;
                    case ScriptVariable variable:
                        return new Chain(variable, steps, indexes);
                    default:
                        return null;
                }
            }
        }

        /// <summary>The head of a pipeline and its steps in order (Scriban reads <c>x | f | g</c> as a pipe from <c>x</c> to a pipe from <c>f</c> to <c>g</c>).</summary>
        private static (ScriptExpression? Head, List<ScriptExpression> Steps) Decompose(ScriptExpression expression)
        {
            expression = DialectRules.Unwrap(expression);
            if (expression is not ScriptPipeCall pipe)
                return (expression, []);
            var (head, steps) = pipe.From is { } from ? Decompose(from) : (null, []);
            Collect(pipe.To, steps);
            return (head, steps);

            static void Collect(ScriptExpression? to, List<ScriptExpression> steps)
            {
                if (to is ScriptPipeCall next)
                {
                    Collect(next.From, steps);
                    Collect(next.To, steps);
                }
                else if (to is not null)
                {
                    steps.Add(to);
                }
            }
        }
    }
}
