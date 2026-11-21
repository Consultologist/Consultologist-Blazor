using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Run;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 3.2, 6.2; D12): a snippet folder's item read by a loop. Where the entry stands
/// in a loop over <c>snippet.&lt;folder&gt;</c>, its <c>item.content</c> (or <c>item.parent.….content</c> of such a loop) is
/// that item's body rendered in the composition's own data, where it is read: the package's text for the item, from the
/// plan's <c>snippets</c> (the function's own folder before the package's, as the runner reads it), the item being the one
/// whose body the scope's <c>item.content</c> is. That body counts against the composition's steps, its <c>for</c>
/// limit and its output. A scope whose <c>item.content</c> is no item of the folder, or a body publish would not have let
/// through (one placing a unit, piping into a renderer, or reading a folder item's body in turn), is S07-006; the value
/// itself is never parsed.
/// </summary>
public static partial class BodyComposer
{
    internal sealed partial class Composing
    {
        private readonly Dictionary<string, TextPlan?> folderTexts = new(StringComparer.Ordinal);
        private bool inFolderItem;

        /// <summary>
        /// The loops around the entry, innermost first (the one <c>item</c> names, then <c>item.parent</c>'s, …): for each,
        /// its folder key (<c>snippet.&lt;folder&gt;</c>) when it is over a snippet folder, else <c>null</c>. Bounded by the
        /// entries of the entry's map.
        /// </summary>
        public IReadOnlyList<string?> FolderLoops()
        {
            var map = function?.Entries ?? RunPlan.Entries;
            var loops = new List<string?>();
            var steps = 0;
            for (var at = Entry.Parent; at != null && steps++ <= map.Count && map.TryGetValue(at, out var parent); at = parent.Parent)
            {
                if (parent.Kind == PlanKind.For)
                    loops.Add(SourceLayout.Text(parent.Json["in"]) is { } source && source.Split('.') is ["snippet", _] ? source : null);
            }

            return loops;
        }

        /// <summary>
        /// The body of the item of <paramref name="folder"/> whose body is <paramref name="content"/> (the scope's
        /// <c>item.content</c>), rendered over the composition's data; S07-006 when the folder holds no such item, the
        /// body is not what publish wrote, or the read stands inside a folder item's body.
        /// </summary>
        public string FolderItemContent(string folder, JsonNode? content)
        {
            if (inFolderItem || SourceLayout.Text(content) is not { } given || FolderText(folder, given) is not { } text)
                throw Stopped(new ComposeStop(NotPublished));
            inFolderItem = true;
            try
            {
                return Render(text, Globals());
            }
            finally
            {
                inFolderItem = false;
            }
        }

        /// <summary>
        /// The text planned for the item of <paramref name="folder"/> whose body is <paramref name="body"/>, once per
        /// composition; <c>null</c> when there is none, or when its body places a unit or pipes into a renderer.
        /// </summary>
        private TextPlan? FolderText(string folder, string body)
        {
            var key = folder + "\n" + body;
            if (folderTexts.TryGetValue(key, out var known))
                return known;
            TextPlan? plan = null;
            if (Snippet(folder)?["items"] is JsonArray ids)
            {
                foreach (var id in ids)
                {
                    var site = $"{folder}.{SourceLayout.Text(id)}";
                    if (SourceLayout.Text(id) is not null && SourceLayout.Text(Snippet(site)?["body"]) == body)
                    {
                        plan = PlainText(body, site);
                        break;
                    }
                }
            }

            folderTexts[key] = plan;
            return plan;
        }

        /// <summary><paramref name="text"/>, the text of <paramref name="site"/>, planned when it places no unit and pipes into no renderer; else <c>null</c>.</summary>
        private TextPlan? PlainText(string text, string site)
        {
            if (Parse(text) is not { } read)
                return null;
            foreach (var statement in Outputs(read.Template.Page!))
            {
                if (statement.Expression is { } written
                    && (PlacementOf(DialectRules.Unwrap(written)) is not null || RendererSteps(DialectRules.Unwrap(written)).Any()))
                {
                    return null;
                }
            }

            return new TextPlan(site, read.Template, read.Syntax);
        }
    }

    /// <summary>
    /// <c>item</c> (or a <c>parent</c> of it, <paramref name="level"/> loops out) as a body reads it where that loop is over
    /// a snippet folder: its members as the run gave them, but <c>.content</c> is the item's body rendered
    /// (<see cref="Composing.FolderItemContent"/>), and <c>.parent</c> is read the same way. A member it does not hold
    /// fails the render, as for any data (<see cref="DataObject"/>).
    /// </summary>
    internal sealed class FolderItemObject(Composing composing, DataObject data, IReadOnlyList<string?> loops, int level) : ScriptObject
    {
        /// <summary>The item as the run gave it. Read-only.</summary>
        public DataObject Data { get; } = data;

        /// <summary>The <c>item</c> global for <paramref name="value"/> (the scope's <c>item</c>), wrapped when some loop around the entry is over a snippet folder.</summary>
        public static object? Of(Composing composing, object? value)
        {
            var loops = composing.FolderLoops();
            return value is DataObject data && loops.Any(l => l != null) ? new FolderItemObject(composing, data, loops, 0) : value;
        }

        public override bool TryGetValue(TemplateContext context, SourceSpan span, string member, out object? value)
        {
            if (member == "content" && level < loops.Count && loops[level] is { } folder)
            {
                value = composing.FolderItemContent(folder, Data.Json["content"]);
                return true;
            }

            Data.TryGetValue(context, span, member, out value);
            if (member == "parent" && value is DataObject parent && level + 1 < loops.Count)
                value = new FolderItemObject(composing, parent, loops, level + 1);
            return true;
        }
    }
}
