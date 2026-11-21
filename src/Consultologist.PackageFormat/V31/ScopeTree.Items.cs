using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1502 (specVersion 31, area 05): what a loop iterates (part 04 § 2.2), read outside the loop. <paramref name="Resolved"/>
/// is its <c>in</c> resolved (<see cref="ScopeHeadKind.Refused"/>, with the sentence, when the source is refused;
/// <see cref="ScopeHeadKind.Deferred"/> when it reads a loop whose own source is refused or reads itself), and
/// <paramref name="Reference"/> the <c>in</c> as written. <paramref name="Element"/> is the list's <c>items</c> schema (a
/// snippet folder's <c>format</c> for <c>snippet.&lt;folder&gt;</c>, the element of § 2.6 over a loop's <c>.items</c>),
/// null exactly when the source is refused or deferred. <paramref name="KnownLength"/> is a snippet folder's item count,
/// known at publish (null for every other source). <paramref name="OverItems"/> is set, with the loop whose
/// <c>.items</c> it iterates as <paramref name="Production"/>, when the source is <c>node.&lt;l&gt;.items</c> or
/// <c>check.&lt;l&gt;.items</c>.
/// </summary>
public sealed record LoopSource(Resolved Resolved, string Reference, JsonNode? Element, int? KnownLength, bool OverItems, SourceFor? Production);

/// <summary>#1502 (specVersion 31, area 05): a loop's source and its item (part 04 §§ 2.2, 2.6), and the schema of a loop's <c>.items</c> element.</summary>
public sealed partial class ScopeTree
{
    private readonly Dictionary<SourceFor, LoopSource> sources = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<SourceFor> typing = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// What <paramref name="loop"/> iterates: its <c>in</c> resolved at the body the loop is listed in (the frame outside
    /// it), typed, or refused with the one sentence part 04 § 10.1 says of it:
    /// <list type="bullet">
    /// <item>not a reference (D13): A05-001;</item>
    /// <item><c>snippet.&lt;folder&gt;</c>: a list of the folder's <c>format</c>, of as many items as it holds (the
    /// function's own folder, else the package's); a single snippet, or one item of a folder, is S04-025; anything else
    /// under <c>snippet</c> is S03-014;</item>
    /// <item><c>item.value.&lt;field&gt;</c> with no loop around it, outside a function: S04-026;</item>
    /// <item><c>node.&lt;y&gt;.value.&lt;z&gt;</c>, <c>y</c> inside a loop (or a while) the loop is not in: S04-027;</item>
    /// <item>otherwise <see cref="Resolve"/>'s refusal (S04-039, S04-040, S04-050, S04-051), S03-014 for what names
    /// nothing or a member or field that does not read, S02-018 for an input read bare, and S04-025 for what is not a
    /// list.</item>
    /// </list>
    /// Cached. A loop whose source reads itself, while it is being typed, is <see cref="ScopeHeadKind.Deferred"/>. A
    /// null argument is the caller's error; a loop not of this package too (<see cref="ArgumentException"/>).
    /// </summary>
    public LoopSource Source(SourceFor loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        if (sources.TryGetValue(loop, out var known))
            return known;
        var frame = FrameOf(loop);
        if (!typing.Add(loop))
            return new LoopSource(Deferred, loop.In, null, null, false, null);

        var source = TypeSource(loop, frame);
        typing.Remove(loop);
        sources[loop] = source;
        return source;
    }

    private LoopSource TypeSource(SourceFor loop, ScopeFrame frame)
    {
        var path = SourceLayout.Join(loop.Path, "index.json");
        LoopSource Refuse(SourceRefusal refusal) => new(Refused(refusal), loop.In, null, null, false, null);

        if (ReferenceString.Parse(loop.In) is not { } reference)
            return Refuse(ResolveSentences.NotAReference("Loop", loop.Id, "in", loop.In, path));
        var s = reference.Segments;
        var subject = $"Loop '{loop.Id}'";
        var formats = FormatsOf(frame);

        if (s[0] == "snippet")
            return Snippet(loop, reference, frame, formats, path);
        if (s is ["item", "value", _, ..] && frame.Loop(0) == null && frame.Function == null)
            return Refuse(ResolveSentences.IteratesItemOutsideLoop(loop.Id, string.Join('.', s.Skip(2)), path));
        if (s is ["node", var id, "value", _, ..] && HiddenFrom(frame, "node", id) is { } hidden)
            return Refuse(ResolveSentences.IteratesNodeInsideLoop(loop.Id, id, string.Join('.', s.Skip(3)), hidden.Loop.Id, path));

        var (head, list, refusal) = Typed(reference, frame, subject, path, loop);
        if (refusal != null)
            return Refuse(refusal);
        if (list == null)
            return new LoopSource(head, loop.In, null, null, false, null);
        if (FormatTypes.Shape(list.Format, formats) != FormatShape.List
            || ExpressionChecker.ItemsOf(list.Format, formats) is not { } element)
        {
            return Refuse(ResolveSentences.IteratesNoList(loop.Id, reference.Text, TypeStrings.Print(list.Format, formats), path));
        }

        var production = head.Length >= s.Count && head.Kind == ScopeHeadKind.Value && s is [_, var named, "items"]
            ? frame.Visible(s[0], named) as SourceFor
            : null;
        return new LoopSource(head, reference.Text, element, null, production != null, production);
    }

    /// <summary><c>snippet.&lt;folder&gt;</c>, a list of the folder's format; a single snippet, or a folder's item, is no list.</summary>
    private LoopSource Snippet(SourceFor loop, ReferenceString reference, ScopeFrame frame, FormatSet formats, string path)
    {
        var s = reference.Segments;
        var entry = s.Count is 2 or 3 ? SnippetEntry(frame.Function, s[1]) : null;
        JsonNode? single = (entry, s.Count) switch
        {
            (SourceSingleText { Text: var text }, 2) => text.Header["format"],
            (SourceTextFolder folder, 3) when folder.Items.Any(i => i.Id == s[2]) => folder.Index["format"],
            _ => null,
        };
        if (single != null)
            return Refuse(ResolveSentences.IteratesNoList(loop.Id, reference.Text, TypeStrings.Print(single, formats), path));
        if (entry is not SourceTextFolder { Index: var index } list || s.Count != 2 || index["format"] is not { } format)
            return Refuse(ResolveSentences.DoesNotExist($"Loop '{loop.Id}'", reference.Text, path));

        var head = Head(ScopeHeadKind.Value, null, 2, FormatTypes.ListOf(Schema(format)), false);
        return new LoopSource(head, reference.Text, format.DeepClone(), list.Items.Count, false, null);

        LoopSource Refuse(SourceRefusal refusal) => new(Refused(refusal), loop.In, null, null, false, null);
    }

    /// <summary>The snippet (a file or a folder) <paramref name="id"/>: the function's own, else the package's.</summary>
    private SourceTextEntry? SnippetEntry(SourceFunction? function, string id) =>
        function?.Definitions.Snippets.Items.FirstOrDefault(f => f.Id == id)
        ?? package.Definitions.Snippets.Items.FirstOrDefault(f => f.Id == id);

    /// <summary>
    /// The element of <paramref name="loop"/>'s <c>.items</c> (part 04 § 2.6; D14), an inline object schema:
    /// <c>item</c> (its <c>id</c>, and its <c>value</c> when the source is typed), <c>label</c>, <c>state</c>,
    /// <c>reason</c>, <c>value</c> and <c>content</c> (of the loop's element, nullable), and <c>nodes</c>, each body
    /// node's <c>value</c>, <c>content</c> (nullable) and <c>state</c>.
    /// </summary>
    private JsonObject Element(SourceFor loop)
    {
        var formats = FormatsOf(FrameOf(loop));
        var item = new JsonObject { ["id"] = new JsonObject { ["type"] = "string" } };
        if (Source(loop) is { Element: { } source })
            item["value"] = Schema(source);

        var properties = new JsonObject
        {
            ["item"] = Object(item),
            ["label"] = new JsonObject { ["type"] = "string" },
            ["state"] = Enum(States),
            ["reason"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
        };
        if (FormatOf(loop) is { } declared && ExpressionChecker.ItemsOf(declared, formats) is { } value)
        {
            properties["value"] = OrNull(value, formats);
            properties["content"] = OrNull(ContentOf(value, formats), formats);
        }

        var nodes = new JsonObject();
        foreach (var node in loop.Body.Nodes)
        {
            var outputs = new JsonObject();
            if (FormatOf(node) is { } format)
            {
                outputs["value"] = OrNull(format, formats);
                outputs["content"] = OrNull(ContentOf(format, formats), formats);
            }

            outputs["state"] = Enum(States);
            nodes[node.Id] = Object(outputs);
        }

        properties["nodes"] = Object(nodes);
        return Object(properties);
    }
}
