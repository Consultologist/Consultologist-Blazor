using Scriban;
using Scriban.Syntax;

namespace Consultologist.PackageFormat;

/// <summary>
/// The token machinery a rename shares (#1225 for nodes, #1290 for macros, snippets, folders and
/// items): a template is parsed with Scriban, a visitor collects the spans of the identifier or
/// string-literal tokens to replace, and the edits are applied from the end so earlier offsets hold.
/// Prose is never touched; a text that does not parse (or holds an unclosed <c>{{</c>) is left alone.
/// </summary>
internal static class WorkflowTemplateTokens
{
    /// <summary>One replacement: the token's span and, for a quoted literal, the quote it keeps.</summary>
    internal readonly record struct Edit(int Start, int End, char? Quote);

    /// <summary>One step of a member chain: its name (null for a computed index), its token, and whether it was a quoted index.</summary>
    internal sealed record Segment(string? Name, ScriptNode? Token, bool Quoted);

    /// <summary>A member chain: its root variable and the steps after it (<c>macro.notes.hpi</c> → macro, [notes, hpi]).</summary>
    internal sealed record Chain(string Root, List<Segment> Segments);

    /// <summary>
    /// The template rewritten by the visitor's edits; Changed false when there were none, Parsed false
    /// when the text did not parse. The visitor is built over the text and returns its edits keyed by start.
    /// </summary>
    internal static (string Text, bool Changed, bool Parsed) Rewrite(string text, string newId, Func<string, EditingVisitor> visitor)
    {
        if (TemplateReferenceWalker.TryFindUnclosedCode(text, out _))
        {
            return (text, false, false);
        }

        var template = Template.Parse(text);
        if (template.HasErrors)
        {
            return (text, false, false);
        }

        var walker = visitor(text);
        walker.Visit(template.Page);
        if (walker.Edits.Count == 0)
        {
            return (text, false, true);
        }

        var result = new System.Text.StringBuilder(text);
        foreach (var edit in walker.Edits.Values.OrderByDescending(e => e.Start))
        {
            var replacement = edit.Quote is { } quote ? $"{quote}{newId}{quote}" : newId;
            result.Remove(edit.Start, edit.End - edit.Start).Insert(edit.Start, replacement);
        }

        return (result.ToString(), true, true);
    }

    /// <summary>A bare gate expression rewritten inside <c>{{ … }}</c> and unwrapped again.</summary>
    internal static (string Text, bool Changed) RewriteCondition(string condition, Func<string, (string Text, bool Changed, bool Parsed)> rewrite)
    {
        const string open = "{{ ";
        const string close = " }}";
        var (text, changed, _) = rewrite(open + condition + close);
        return changed ? (text[open.Length..^close.Length], true) : (condition, false);
    }

    /// <summary>A snippet, macro or item file — its body and its header's <c>if</c> — rewritten; a prompt file has no header.</summary>
    internal static (string Text, bool Changed) RewriteFile(string fileText, Func<string, (string Text, bool Changed, bool Parsed)> rewrite)
    {
        var (headerJson, body) = WorkflowTextHeaders.Split(fileText);
        var (newBody, bodyChanged, _) = rewrite(body);
        if (headerJson is null)
        {
            return bodyChanged ? (newBody, true) : (fileText, false);
        }

        var header = WorkflowTextHeaders.Parse(WorkflowTextKind.Macro, "rename", headerJson, new List<string>());
        if (header?.If is { } gate && RewriteCondition(gate, rewrite) is (var newIf, true))
        {
            return (WorkflowTextHeaders.Compose(header with { If = newIf }) + newBody, true);
        }

        return bodyChanged ? (headerJson + "\n" + newBody, true) : (fileText, false);
    }

    /// <summary>A member chain flattened to its root and steps; null when the expression is not a chain.</summary>
    internal static Chain? Flatten(ScriptExpression? expression)
    {
        var segments = new List<Segment>();
        var current = expression;
        while (true)
        {
            switch (current)
            {
                case ScriptMemberExpression { Member: { } member } m:
                    segments.Insert(0, new Segment(member.Name, member, false));
                    current = m.Target;
                    continue;
                case ScriptIndexerExpression indexer:
                    segments.Insert(0, indexer.Index is ScriptLiteral { Value: string name } literal
                        ? new Segment(name, literal, true)
                        : new Segment(null, null, false));
                    current = indexer.Target;
                    continue;
                case ScriptVariableGlobal global:
                    return new Chain(global.Name, segments);
                case ScriptVariableLocal local:
                    return new Chain(local.Name, segments);
                case ScriptVariable variable:
                    return new Chain(variable.Name, segments);
                default:
                    return null;
            }
        }
    }

    /// <summary>A visitor that collects edits; <see cref="Take"/> records one token naming the old id.</summary>
    internal abstract class EditingVisitor : ScriptVisitor
    {
        protected EditingVisitor(string text, string oldId)
        {
            Text = text;
            OldId = oldId;
        }

        protected string Text { get; }

        protected string OldId { get; }

        /// <summary>Edits keyed by token start, so a chain visited from its outer and inner members is renamed once.</summary>
        public Dictionary<int, Edit> Edits { get; } = new();

        /// <summary>The segment's token, when it names the old id: an identifier replaced as written, a quoted index with its quote.</summary>
        protected void Take(Segment segment)
        {
            if (segment.Name != OldId || segment.Token is not { } token)
            {
                return;
            }

            var start = token.Span.Start.Offset;
            var end = token.Span.End.Offset + 1;
            if (start < 0 || end > Text.Length || end <= start)
            {
                return;
            }

            if (segment.Quoted)
            {
                var quote = Text[start];
                Edits[start] = new Edit(start, end, quote is '"' or '\'' ? quote : '"');
            }
            else if (Text[start..end] == OldId)
            {
                Edits[start] = new Edit(start, end, null);
            }
        }

        /// <summary>A string literal equal to the old id, replaced with its quote kept.</summary>
        protected void TakeLiteral(ScriptLiteral literal)
        {
            if (literal.Value is not string value || value != OldId)
            {
                return;
            }

            var start = literal.Span.Start.Offset;
            var end = literal.Span.End.Offset + 1;
            if (start < 0 || end > Text.Length || end <= start)
            {
                return;
            }

            var quote = Text[start];
            Edits[start] = new Edit(start, end, quote is '"' or '\'' ? quote : '"');
        }
    }
}
