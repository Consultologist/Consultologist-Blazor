using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1499 (specVersion 31, area 02): the leaves of <c>nodes/</c>, <c>checks/</c> and a function's
/// body: <c>agent</c>, <c>rendered</c>, <c>computed</c> and <c>call</c> files (part 01 §§ 4, 5.1, 5.2).
/// </summary>
public static partial class SourceParser
{
    private static readonly string[] LeafKinds = ["agent", "rendered", "computed", "call"];
    private static readonly string[] FlowKinds = ["for", "while", "if", "case"];

    /// <summary>
    /// #1500 (area 03, D14): the producers <c>constants.json</c> reserves for a later version
    /// (<c>producers.reserved</c>); a node that declares one is S02-010, not S01-028.
    /// </summary>
    internal static readonly string[] ReservedProducers = ["grounded", "extracted", "attention"];

    /// <summary>
    /// One listed child of a body: a file is a leaf, read here; a folder is a flow node, read through
    /// its <c>index.json</c> (<c>SourceParser.Flow.cs</c>). Null when the child was refused.
    /// </summary>
    private static SourceNode? ReadChild(SourceWalk walk, string root, string enclosing, ListedMember member) =>
        member.IsFolder ? ReadFlowNode(walk, root, enclosing, member) : ReadLeaf(walk, root, enclosing, member);

    /// <summary>
    /// A node's <c>kind</c>, read before anything else: missing (S01-021), <c>null</c> (S01-022), not text
    /// (A02-001), <c>let</c> (S02-008, area 03's D14), none of the eight (S02-007), or the wrong form
    /// for where it is written (a flow kind in a file, A02-006; a leaf kind in a folder, A02-007, D21).
    /// Each ends the read: null.
    /// </summary>
    private static string? ReadKind(SourceWalk walk, JsonObject header, string word, string id, string path, bool inFolder)
    {
        var declaration = $"{word} '{id}'";
        if (!header.TryGetPropertyValue("kind", out var kindNode))
        {
            walk.Refuse(SourceSentences.MemberMissing(declaration, "kind", path));
            return null;
        }

        if (kindNode is null)
        {
            walk.Refuse(SourceSentences.MemberNull(declaration, "kind", path));
            return null;
        }

        if (SourceLayout.Text(kindNode) is not { } kind)
        {
            walk.Refuse(SourceSentences.MemberWrongType(declaration, "kind", SourceMembers.Describe(kindNode), "text", path));
            return null;
        }

        if (kind == "let")
        {
            walk.Refuse(FormatSentences.KindLet(id, path));
            return null;
        }

        if (!LeafKinds.Contains(kind) && !FlowKinds.Contains(kind))
        {
            walk.Refuse(SourceSentences.UnknownKind(id, kind, path));
            return null;
        }

        if (!inFolder && FlowKinds.Contains(kind))
        {
            walk.Refuse(SourceSentences.FlowKindInFile(id, kind, path));
            return null;
        }

        if (inFolder && LeafKinds.Contains(kind))
        {
            walk.Refuse(SourceSentences.LeafKindInFolder(id, kind, path));
            return null;
        }

        return kind;
    }

    /// <summary>
    /// A leaf file: split and parsed as every header file; then <c>kind</c> (<see cref="ReadKind"/>);
    /// then the members of the kind's shape, the producer, the kind's own members and the body. Every kind is read
    /// under <c>checks/</c> too; which it may hold is <see cref="SourceChecks"/>' (#1505, area 08: S06-010, S06-011).
    /// </summary>
    private static SourceLeaf? ReadLeaf(SourceWalk walk, string root, string enclosing, ListedMember member)
    {
        if (SourceLayout.ReadHeaderFile(walk, member.Path) is not { } file)
            return null;
        var (header, body) = file;

        var isCheck = root == "check";
        var word = isCheck ? "Check" : "Node";
        var declaration = $"{word} '{member.Id}'";
        var path = member.Path;
        var key = Key(root, enclosing, member.Id);

        if (ReadKind(walk, header, word, member.Id, path, inFolder: false) is not { } kind)
            return null;

        var shape = kind switch
        {
            "agent" => SourceShapes.NodeAgent,
            "rendered" => SourceShapes.NodeRendered,
            "computed" => isCheck ? SourceShapes.Check : SourceShapes.NodeComputed,
            _ => SourceShapes.NodeCall,
        };
        // An inline format has part 02's own sentence, not A02-001 (area 03's D15): an agent's is S02-014
        // here; a rendered node's is read on, and the type rules say S02-013. A check's is the reader's A02-001
        // (a check's format is text: check_result, S06-009); a rendered leaf in checks/ is S06-010 (#1505, area 08: SourceChecks).
        var inlineFormat = header["format"] is JsonObject && (kind == "agent" || (kind == "rendered" && !isCheck));
        SourceMembers.Check(header, shape, declaration, path, walk.Refusals, inlineFormat ? ["format"] : null);
        if (inlineFormat && kind == "agent")
            walk.Refuse(FormatSentences.AgentInlineFormat(member.Id, path));

        if (SourceLayout.Text(header["producer"]) is { } producer)
        {
            var expected = kind == "agent" ? "generated" : "computed";
            if (ReservedProducers.Contains(producer))
                walk.Refuse(FormatSentences.ReservedProducer(member.Id, producer, path));
            else if (producer != expected)
                walk.Refuse(SourceSentences.ProducerDisagrees(word, key, kind, producer, expected, path));
        }

        // #1508 (Task 35; A11-014): onInvalid is one of the schema's two values; the member table only types it.
        if (kind == "agent" && SourceLayout.Text(header["onInvalid"]) is { } onInvalid && onInvalid is not ("retry" or "fail"))
            walk.Refuse(SourceSentences.OnInvalidValue(member.Id, onInvalid, path));

        if (kind == "agent" && header["parts"] is JsonObject parts)
        {
            foreach (var (partId, _) in parts)
            {
                if (SourceIds.Check(partId, path) is { } bad)
                    walk.Refuse(bad);
            }
        }

        if (kind == "call" && header["with"] is JsonObject with)
        {
            // #1502 (area 05, D13): an argument is a reference string or null; an object or a list is an expression
            // (S04-017), any other value no reference (A05-005), in place of A02-001.
            foreach (var (param, value) in with)
            {
                if (value is JsonObject or JsonArray)
                    walk.Refuse(ResolveSentences.IsAnExpression("Call", member.Id, $"with {param}", path));
                else if (value is not null && SourceLayout.Text(value) is null)
                    walk.Refuse(ResolveSentences.ArgumentNotAReference(member.Id, param, SourceMembers.Describe(value), path));
            }
        }

        var empty = FrontMatter.IsEmptyBody(body);
        switch (kind)
        {
            case "agent" or "rendered":
                if (empty)
                    walk.Refuse(SourceSentences.EmptyBody(declaration, kind == "agent" ? "prompt" : "template", path));
                break;
            default:
                if (!empty)
                {
                    var what = kind == "call" ? "call" : isCheck ? "check" : "computed node";
                    walk.Refuse(SourceSentences.HasBody(declaration, what, path));
                }

                break;
        }

        return new SourceLeaf(member.Id, key, path, kind, header, body);
    }
}
