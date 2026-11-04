using System.Text.Json.Nodes;
using Scriban.Syntax;

namespace Consultologist.PackageFormat;

/// <summary>
/// #1225: a node id renamed everywhere a 26/27 package names it — by token, never by text search.
/// The walk records node reads without positions (and a loop element's <c>e.&lt;id&gt;</c> under the
/// loop's id), so a rename re-parses each template with Scriban and replaces only the identifier
/// tokens that name the node: <c>node.&lt;id&gt;</c> chains (pipes, conditions, arguments included),
/// <c>node["&lt;id&gt;"]</c>, <c>node.&lt;loop&gt;[i].&lt;id&gt;</c>, and <c>&lt;v&gt;.&lt;id&gt;</c> where
/// <c>&lt;v&gt;</c> iterates a loop whose body holds the node. Prose, string literals and another
/// loop's members are left alone; a text that does not parse is returned unchanged.
/// </summary>
public static class WorkflowNodeRename
{
    /// <summary>The template with every token naming <paramref name="oldId"/> as a node renamed; Changed false when nothing named it.</summary>
    public static (string Text, bool Changed, bool Parsed) RenameInTemplate(string text, string oldId, string newId, IReadOnlySet<string> loopsOver)
    {
        if (string.IsNullOrEmpty(text) || oldId == newId || !text.Contains(oldId, StringComparison.Ordinal))
        {
            return (text, false, true);
        }

        // An unclosed {{ or a parse error leaves the text alone (WorkflowTemplateTokens.Rewrite).
        return WorkflowTemplateTokens.Rewrite(text, newId, source => new Renamer(source, oldId, loopsOver));
    }

    /// <summary>A bare gate expression (an <c>if</c>, a case subject) with the node renamed.</summary>
    public static (string Text, bool Changed) RenameInCondition(string condition, string oldId, string newId, IReadOnlySet<string> loopsOver) =>
        WorkflowTemplateTokens.RewriteCondition(condition, text => RenameInTemplate(text, oldId, newId, loopsOver));

    /// <summary>A snippet or macro file — its body and its header's <c>if</c> — with the node renamed.</summary>
    public static (string Text, bool Changed) RenameInFile(string fileText, string oldId, string newId, IReadOnlySet<string> loopsOver) =>
        WorkflowTemplateTokens.RewriteFile(fileText, text => RenameInTemplate(text, oldId, newId, loopsOver));

    /// <summary>The loops a node's element reads go through: the loop whose body holds it, or (27) holds the flow it is a branch of.</summary>
    public static IReadOnlySet<string> LoopsHolding(JsonObject manifest, string nodeId)
    {
        var loops = new HashSet<string>(StringComparer.Ordinal);
        if (manifest["nodes"] is not JsonArray nodes)
        {
            return loops;
        }

        var flowsHolding = nodes.OfType<JsonObject>()
            .Where(n => BodyIdsOf(n).Contains(nodeId))
            .Select(n => n["id"]?.GetValue<string>())
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        foreach (var node in nodes.OfType<JsonObject>())
        {
            if (node["for"] is null || node["id"]?.GetValue<string>() is not { } loopId || node["nodes"] is not JsonArray body)
            {
                continue;
            }

            var ids = body.Select(b => b?.GetValue<string>()).OfType<string>().ToList();
            if (ids.Contains(nodeId) || ids.Any(flowsHolding.Contains))
            {
                loops.Add(loopId);
            }
        }

        return loops;
    }

    /// <summary>
    /// Every structural site of a 26/27 manifest that names the node renamed: its id; a loop's
    /// <c>nodes[]</c> and <c>ret</c>; an if's then/else and a case's branches (<c>nodes[]</c>, <c>ret</c>);
    /// a case subject; gate texts (flow, instance, deliverable, deliverable instance); <c>with</c>
    /// node parameters; a deliverable's <c>sources</c> and <c>check</c>; and, when given, its prompt path.
    /// </summary>
    public static bool RenameInManifest(JsonObject manifest, string oldId, string newId, string? newPromptPath = null)
    {
        var loops = LoopsHolding(manifest, oldId);
        var changed = false;
        var bare = $"node.{oldId}";

        void Bare(JsonObject owner, string key)
        {
            if (owner[key] is JsonValue value && value.TryGetValue<string>(out var s) && s == bare)
            {
                owner[key] = $"node.{newId}";
                changed = true;
            }
        }

        void Gate(JsonObject owner, string key)
        {
            if (owner[key] is JsonValue value && value.TryGetValue<string>(out var s)
                && RenameInCondition(s, oldId, newId, loops) is (var renamed, true))
            {
                owner[key] = renamed;
                changed = true;
            }
        }

        void Ids(JsonObject owner, string key)
        {
            if (owner[key] is not JsonArray array)
            {
                return;
            }

            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] is JsonValue v && v.TryGetValue<string>(out var s) && s == oldId)
                {
                    array[i] = newId;
                    changed = true;
                }
            }
        }

        void BareList(JsonObject owner, string key)
        {
            if (owner[key] is not JsonArray array)
            {
                return;
            }

            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] is JsonValue v && v.TryGetValue<string>(out var s) && s == bare)
                {
                    array[i] = $"node.{newId}";
                    changed = true;
                }
            }
        }

        void Body(JsonObject? body)
        {
            if (body is null)
            {
                return;
            }

            Ids(body, "nodes");
            Bare(body, "ret");
        }

        void InstanceGates(JsonObject owner)
        {
            if (owner["macros"] is JsonArray instances)
            {
                foreach (var instance in instances.OfType<JsonObject>())
                {
                    Gate(instance, "if");
                }
            }
        }

        if (manifest["nodes"] is JsonArray nodes)
        {
            foreach (var node in nodes.OfType<JsonObject>())
            {
                if (node["id"] is JsonValue idValue && idValue.TryGetValue<string>(out var id) && id == oldId)
                {
                    node["id"] = newId;
                    changed = true;
                    if (newPromptPath is not null && node["prompt"] is not null)
                    {
                        node["prompt"] = newPromptPath;
                    }
                }

                Ids(node, "nodes");
                Bare(node, "ret");
                Gate(node, "if");
                Gate(node, "case");
                Body(node["then"] as JsonObject);
                Body(node["else"] as JsonObject);
                if (node["branches"] is JsonArray branches)
                {
                    foreach (var branch in branches.OfType<JsonObject>())
                    {
                        Body(branch);
                    }
                }

                if (node["with"] is JsonObject with)
                {
                    foreach (var key in with.Select(p => p.Key).ToList())
                    {
                        Bare(with, key);
                    }
                }

                InstanceGates(node);
            }
        }

        if (manifest["deliverables"] is JsonArray deliverables)
        {
            foreach (var deliverable in deliverables.OfType<JsonObject>())
            {
                BareList(deliverable, "sources");
                Bare(deliverable, "check");
                Gate(deliverable, "if");
                InstanceGates(deliverable);
            }
        }

        return changed;
    }

    private static IEnumerable<string> BodyIdsOf(JsonObject flow)
    {
        IEnumerable<string> Of(JsonObject? body) => body?["nodes"] is JsonArray a ? a.Select(x => x?.GetValue<string>()).OfType<string>() : Array.Empty<string>();
        var ids = Of(flow["then"] as JsonObject).Concat(Of(flow["else"] as JsonObject));
        if (flow["branches"] is JsonArray branches)
        {
            ids = ids.Concat(branches.OfType<JsonObject>().SelectMany(Of));
        }

        return ids;
    }

    private sealed class Renamer : WorkflowTemplateTokens.EditingVisitor
    {
        private readonly IReadOnlySet<string> _loopsOver;
        private readonly List<HashSet<string>> _loopVariables = new();

        public Renamer(string text, string oldId, IReadOnlySet<string> loopsOver) : base(text, oldId)
        {
            _loopsOver = loopsOver;
        }

        public override void Visit(ScriptMemberExpression node)
        {
            Consider(node);
            base.Visit(node);
        }

        public override void Visit(ScriptIndexerExpression node)
        {
            Consider(node);
            base.Visit(node);
        }

        public override void Visit(ScriptForStatement node)
        {
            Visit(node.Iterator);
            var bound = new HashSet<string>(StringComparer.Ordinal);
            if (node.Variable is ScriptVariable variable && IteratesALoopOver(node.Iterator))
            {
                bound.Add(variable.Name);
            }

            _loopVariables.Add(bound);
            Visit(node.Body);
            _loopVariables.RemoveAt(_loopVariables.Count - 1);
            Visit(node.Else);
        }

        private bool IteratesALoopOver(ScriptExpression? iterator) =>
            WorkflowTemplateTokens.Flatten(iterator) is { Root: "node", Segments: { Count: 1 } segments } && segments[0].Name is { } loop && _loopsOver.Contains(loop);

        private bool IsLoopVariable(string name) => _loopVariables.Any(scope => scope.Contains(name));

        private void Consider(ScriptExpression expression)
        {
            if (WorkflowTemplateTokens.Flatten(expression) is not { } chain || chain.Segments.Count == 0)
            {
                return;
            }

            var segments = chain.Segments;
            if (chain.Root == "node")
            {
                // node.<old>… and node["<old>"]…
                Take(segments[0]);

                // node.<loop>[i].<old>
                if (segments.Count >= 3 && segments[0].Name is { } loop && _loopsOver.Contains(loop) && segments[1].Name is null)
                {
                    Take(segments[2]);
                }
            }
            else if (IsLoopVariable(chain.Root))
            {
                // e.<old> inside for e in node.<loop>
                Take(segments[0]);
            }
        }
    }
}
