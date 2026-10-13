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
/// Pipes and functions are opaque here; the strict render probe stays the
/// second net.
/// </summary>
public static class TemplateReferenceWalker
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

        var visitor = new Visitor(subject, context, collector);
        visitor.Visit(template.Page);
        return collector.ToReferences();
    }

    /// <summary>A code block that opens and never closes, by the lexer's own tokens; the line is one-based.</summary>
    private static bool TryFindUnclosedCode(string templateText, out int line)
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

    /// <summary><c>node.&lt;id&gt;</c>, awaiting <c>.text</c> or <c>.value</c>.</summary>
    private sealed record NodeTarget(string Id, TemplateNodeShape Shape) : Target;

    /// <summary>A snippet — by id, or an item of a folder.</summary>
    private sealed record SnippetTarget(string? Id, string? Folder) : Target;

    /// <summary>A macro — by id, or an item of a folder.</summary>
    private sealed record MacroTarget(string? Id, string? Folder) : Target;

    /// <summary>A snippet or macro folder: iterable, never a value.</summary>
    private sealed record FolderTarget(string Object, string Folder) : Target;

    /// <summary>The fan item, awaiting a member.</summary>
    private sealed record ItemTarget : Target;

    /// <summary>A typed value (null shape = untyped, members pass).</summary>
    private sealed record ShapeTarget(TemplateValueShape? Shape) : Target;

    /// <summary>A Scriban builtin, a function result, anything the walker does not check.</summary>
    private sealed record OpaqueTarget : Target;

    /// <summary>A chain already refused; further steps stay silent.</summary>
    private sealed record RefusedTarget : Target;

    /// <summary>How a chain is used: as a value, as something iterated or piped (a folder is fine), or as a `with` object (a bare node, snippet, macro or item is fine).</summary>
    private enum ChainUse { Value, Iterable, With }

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

        public Visitor(string subject, TemplateReferenceContext context, TemplateReferenceCollector refs)
        {
            _subject = subject;
            _context = context;
            _refs = refs;
        }

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
                Errors.Add($"{_subject} carries the placeholder '{{{{{named.Name?.Name}:{named.Value}}}}}'; at specVersion 25 references are objects — input.<id>, node.<id>.text, snippet.<id>.text, item.<member>, macro.<id>.text.");
                return;
            }

            base.Visit(node);
        }

        public override void Visit(ScriptNamedArgument node) => ResolveExpression(node.Value, ChainUse.Iterable);

        public override void Visit(ScriptObjectMember node) => ResolveExpression(node.Value, ChainUse.Value);

        public override void Visit(ScriptPipeCall node)
        {
            // The piped value may be a folder (snippet.sections | array.size).
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
                        segments.Insert(0, new IndexSegment(indexer.Index));
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
                    if (_context.Fan is null)
                    {
                        var member = segments.Count > 0 && segments[0] is MemberSegment m ? $".{m.Name}" : string.Empty;
                        Errors.Add($"{_subject} reads item{member}, but the node does not fan.");
                        return Refused;
                    }

                    return new ItemTarget();

                case TemplateObjects.RetiredData:
                    Errors.Add($"{_subject} reads 'data'; at specVersion 25 data is 'snippet' — read snippet.<id>.text.");
                    return Refused;

                case "bindings":
                case "variables":
                    Errors.Add($"{_subject} reads '{root}'; at specVersion 25 a template reads objects (input.<id>, node.<id>.text, snippet.<id>.text, item.<member>, macro.<id>.text), and bindings are derived from those reads.");
                    return Refused;
            }

            if (TemplateObjects.ScribanBuiltins.Contains(root))
            {
                return Opaque;
            }

            Errors.Add($"{_subject} reads '{root}', which is not an object (input, node, snippet, item, macro), a loop variable, or a Scriban builtin.");
            return Refused;
        }

        private static string Example(string root) => root switch
        {
            TemplateObjects.Input => "input.<id>",
            TemplateObjects.Node => "node.<id>.text or node.<id>.value",
            TemplateObjects.Snippet => "snippet.<id>.text",
            _ => "macro.<id>.text or macro.<id>.prompt"
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
                        Errors.Add($"{_subject} reads {walked}, which is not a member (a snippet has .id, .name and .text).");
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
                        Errors.Add($"{_subject} reads {walked}, which is not a member (a macro has .id, .name, .text and .prompt).");
                        return Refused;
                    }

                    if (macro.Id is not null)
                    {
                        if (name == TemplateMembers.Text)
                        {
                            _refs.ReadMacroText(macro.Id);
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
            if (name == TemplateMembers.Text)
            {
                _refs.ReadNode(node.Id, text: true, value: false);

                if (node.Shape.HasValue)
                {
                    _refs.Warnings.Add($"{_subject} reads {before}.text of format '{node.Shape.Format}' ({node.Shape.Type}); the JSON renders as text — read {before}.value for the fields.");
                }

                return new ShapeTarget(TemplateValueShape.Text);
            }

            if (name == TemplateMembers.Value)
            {
                if (!node.Shape.HasValue)
                {
                    Errors.Add($"{_subject} reads {before}.value, but format '{node.Shape.Format}' is text and has no value; read {before}.text.");
                    return Refused;
                }

                _refs.ReadNode(node.Id, text: false, value: true);
                return new ShapeTarget(node.Shape.Type == WorkflowInputTypes.Enum && node.Shape.Schema is null
                    ? new TemplateValueShape(WorkflowInputTypes.Enum, null, null, null)
                    : TemplateValueShape.FromSchema(node.Shape.Schema));
            }

            Errors.Add($"{_subject} reads {before}.{name}, which is not a member (node.<id> has .text and .value).");
            return Refused;
        }

        private Target StepItem(string name, StringBuilder walked)
        {
            switch (_context.Fan)
            {
                case SnippetFolderFan:
                    if (!TemplateMembers.SnippetFanItem.Contains(name, StringComparer.Ordinal))
                    {
                        Errors.Add($"{_subject} reads {walked}, which a snippet fan item does not carry (it carries: id, name, text).");
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
                    return Refused;
            }
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

        /// <summary>A chain's last word: a node needs .text or .value, a snippet or macro a member, a folder a loop.</summary>
        private Target Finalize(Target target, string walked, ChainUse use)
        {
            if (use == ChainUse.With && target is NodeTarget or SnippetTarget or MacroTarget or ItemTarget)
            {
                return target;
            }

            switch (target)
            {
                case NodeTarget node:
                    Errors.Add($"{_subject} reads node '{node.Id}' bare; read {walked}.text (its composed output) or {walked}.value.");
                    return Refused;

                case SnippetTarget { Id: { } id }:
                    Errors.Add($"{_subject} reads snippet '{id}' bare; read {walked}.text.");
                    return Refused;

                case MacroTarget { Id: { } id }:
                    Errors.Add($"{_subject} reads macro '{id}' bare; read {walked}.text or {walked}.prompt.");
                    return Refused;

                case ItemTarget:
                    Errors.Add($"{_subject} reads item bare; read a member ({(_context.Fan is InputArrayFan ? "item.id, item.name, item.value" : "item.id, item.name, item.text")}).");
                    return Refused;

                case FolderTarget folder when use == ChainUse.Value:
                    Errors.Add($"{_subject} reads {walked} as a value; a folder is iterated: {{{{ for s in {folder.Object}.{folder.Folder} }}}}.");
                    return Refused;

                default:
                    return target;
            }
        }

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
                : "the expression";
    }
}
