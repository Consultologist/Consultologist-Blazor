using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 6.2; part 04 §§ 2.6, 7.7): a folder item's body leaving its loop unrendered.
/// Read in a template as <c>item.content</c> a folder item's body is rendered where it is read
/// (<see cref="CheckedBody.FolderItems"/>). Two routes carry it out of the loop as text, where nothing renders it: a
/// call's argument (<c>with</c>), which the function reads as <c>param.&lt;p&gt;.content</c> (or as a field of its
/// <c>.value</c>), and a computed node's <c>expression</c>, whose value is what it reads, as it is. Each route carries a
/// body when it reads the item (<c>item</c>, <c>item.parent…</c> of a loop over a folder, or its <c>.content</c>), a
/// folder item (<c>snippet.&lt;folder&gt;.&lt;item&gt;</c>), a folder loop's <c>.items</c> (each element's <c>item</c> holds
/// the body), or the <c>item</c> of a loop over such <c>.items</c>. On either route each body carried must be plain text,
/// no tag and no raw block, so that its text and its rendering are one (A07-013). A condition (an <c>if</c>, a
/// <c>case</c> subject) only decides, and a check's expression gives a check result, so neither carries the text. A
/// template reaches no body but through <c>item.content</c>: a bare <c>item</c> is A07-008, a <c>.items</c> element's
/// <c>item</c> is typed without <c>content</c>, and a renderer reads only its input's typed fields. Every walk is
/// bounded: a loop chain by <see cref="RouteDepth"/>, an expression by its nesting.
/// </summary>
public static partial class SourceTemplates
{
    /// <summary>How deep an expression or a chain of loops over <c>.items</c> is walked (past any package publish accepts).</summary>
    private const int RouteDepth = 64;

    private sealed partial class Context
    {
        /// <summary>A07-013 for each folder item body a call's argument or a computed node's expression carries out of its loop that is not plain text.</summary>
        public void FolderRoutes()
        {
            var leaves = ScopeTree.Walk(package.Nodes.Nodes)
                .Concat(package.Functions.Items.SelectMany(f => ScopeTree.Walk(f.Body.Nodes)))
                .OfType<SourceLeaf>();
            foreach (var leaf in leaves)
            {
                var frame = resolution.Tree.FrameOf(leaf);
                switch (leaf.Kind)
                {
                    case "call" when leaf.Header["with"] is JsonObject with:
                        var function = SourceLayout.Text(leaf.Header["function"]) ?? string.Empty;
                        foreach (var (parameter, argument) in with)
                        {
                            if (SourceLayout.Text(argument) is { } reference)
                                Carry(Carried(reference, frame), $"Call '{leaf.Id}'", $"passes it to function '{function}' as param.{parameter}", leaf.Path);
                        }

                        break;
                    case "computed":
                        foreach (var reference in Vars(leaf.Header["expression"]))
                            Carry(Carried(reference, frame), $"Node '{leaf.Id}'", "reads it in its expression", leaf.Path);
                        break;
                }
            }
        }

        /// <summary>A07-013 for each of <paramref name="items"/> whose body is not plain text, at the line of its first tag.</summary>
        private void Carry(IEnumerable<(SourceTextFolder Folder, SourceText Item)> items, string subject, string route, string path)
        {
            foreach (var (folder, item) in items)
            {
                if (FirstTag(item.Body) is { } line)
                    Refusals.Add(DialectSentences.FolderItemCarried($"{folder.Id}/{item.Id}", line, subject, route, path));
            }
        }

        /// <summary>The 1-based line of the first tag (<c>{{</c>) or raw block (<c>{%{</c>) of <paramref name="body"/>; <c>null</c> when it is plain text.</summary>
        private static int? FirstTag(string body)
        {
            var tag = body.IndexOf("{{", StringComparison.Ordinal);
            var raw = body.IndexOf("{%{", StringComparison.Ordinal);
            var at = tag < 0 ? raw : raw < 0 ? tag : Math.Min(tag, raw);
            return at < 0 ? null : 1 + body.AsSpan(0, at).Count('\n');
        }

        /// <summary>
        /// The folder items whose bodies <paramref name="reference"/>, read at <paramref name="frame"/>, carries as they are
        /// written: <c>item(.parent)*</c> of a loop over a folder, or its <c>.content</c>;
        /// <c>snippet.&lt;folder&gt;.&lt;item&gt;</c> (or its <c>.content</c>); a folder loop's
        /// <c>node|check.&lt;loop&gt;.items</c>; and <c>item(.parent)*.item</c> of a loop over such <c>.items</c>. Every item of
        /// the folder, or the one named.
        /// </summary>
        private IEnumerable<(SourceTextFolder, SourceText)> Carried(string reference, ScopeFrame frame)
        {
            var s = reference.Split('.');
            if (s[0] == "item")
            {
                var up = 0;
                while (1 + up < s.Length && s[1 + up] == "parent")
                    up++;
                var rest = s[(1 + up)..];
                if (frame.Loop(up) is not { } loop)
                    return [];
                var source = resolution.Tree.Source(loop);
                if (source.KnownLength != null)
                    return rest is [] or ["content"] ? All(FolderOf(loop)) : [];
                return source.OverItems && rest is ["item"] or ["item", "content"] ? All(ItemsFolder(source.Production)) : [];
            }

            if (s.Length is 3 or 4 && s[0] == "snippet" && (s.Length == 3 || s[3] == "content")
                && Folder(s[1], frame.Function) is { } named && named.Items.FirstOrDefault(i => i.Id == s[2]) is { } item)
            {
                return [(named, item)];
            }

            return s is ["node" or "check", var loopId, "items"] ? All(ItemsFolder(frame.Visible(s[0], loopId) as SourceFor)) : [];
        }

        private static IEnumerable<(SourceTextFolder, SourceText)> All(SourceTextFolder? folder) =>
            folder?.Items.Select(i => (folder, i)) ?? [];

        /// <summary>The folder a loop over a snippet folder iterates, the loop's function's own before the package's; <c>null</c> for any other loop.</summary>
        private SourceTextFolder? FolderOf(SourceFor loop) =>
            resolution.Tree.Source(loop) is { KnownLength: not null, Reference: var reference } && reference.Split('.') is ["snippet", var id]
                ? Folder(id, resolution.Tree.FrameOf(loop).Function)
                : null;

        /// <summary>The folder whose items the <c>.items</c> of <paramref name="loop"/> hold: its own, or through the loops over <c>.items</c> it follows.</summary>
        private SourceTextFolder? ItemsFolder(SourceFor? loop)
        {
            for (var depth = 0; loop != null && depth < RouteDepth; depth++)
            {
                if (FolderOf(loop) is { } folder)
                    return folder;
                loop = resolution.Tree.Source(loop) is { OverItems: true, Production: var production } ? production : null;
            }

            return null;
        }

        private SourceTextFolder? Folder(string id, SourceFunction? function) =>
            (function?.Definitions.Snippets.Items.FirstOrDefault(e => e.Id == id)
                ?? package.Definitions.Snippets.Items.FirstOrDefault(e => e.Id == id)) as SourceTextFolder;
    }

    /// <summary>Every <c>var</c> path of the JSON Logic <paramref name="rule"/>, in document order; bounded by <see cref="RouteDepth"/>.</summary>
    private static IEnumerable<string> Vars(JsonNode? rule)
    {
        var pending = new Stack<(JsonNode? Node, int Depth)>();
        pending.Push((rule, 0));
        while (pending.TryPop(out var at))
        {
            if (at.Depth > RouteDepth)
                continue;
            switch (at.Node)
            {
                case JsonObject { Count: 1 } single when single.First().Key == "var":
                    var path = single["var"] is JsonArray { Count: > 0 } list ? list[0] : single["var"];
                    if (SourceLayout.Text(path) is { } text)
                        yield return text;
                    break;
                case JsonObject obj:
                    foreach (var (_, value) in obj.Reverse())
                        pending.Push((value, at.Depth + 1));
                    break;
                case JsonArray array:
                    for (var i = array.Count - 1; i >= 0; i--)
                        pending.Push((array[i], at.Depth + 1));
                    break;
            }
        }
    }
}
