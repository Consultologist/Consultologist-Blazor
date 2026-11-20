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
    /// One listed child of a body. A file is a leaf and is read here. A folder is a flow node: its
    /// layout is walked, and it is not yet in the model (the flow nodes build it, from the index they
    /// read); the hook is here so that they only wrap what <see cref="ReadFlowFolder"/> reads.
    /// </summary>
    private static SourceNode? ReadChild(SourceWalk walk, string root, string enclosing, ListedMember member)
    {
        if (member.IsFolder)
        {
            ReadFlowFolder(walk, member.Path, root, enclosing.Length == 0 ? member.Id : $"{enclosing}.{member.Id}");
            return null;
        }

        return ReadLeaf(walk, root, enclosing, member);
    }

    /// <summary>
    /// A leaf file: split and parsed as every header file; then <c>kind</c> (missing, null, not text,
    /// unknown, or a flow kind ends the read); then the members of the kind's shape, the producer, the
    /// kind's own members and the body.
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
        var key = enclosing.Length == 0 ? $"{root}.{member.Id}" : $"{root}.{enclosing}.{member.Id}";

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

        if (FlowKinds.Contains(kind))
        {
            walk.Refuse(SourceSentences.FlowKindInFile(member.Id, kind, path));
            return null;
        }

        if (!LeafKinds.Contains(kind))
        {
            walk.Refuse(SourceSentences.UnknownKind(member.Id, kind, path));
            return null;
        }

        var shape = kind switch
        {
            "agent" => SourceShapes.NodeAgent,
            "rendered" => SourceShapes.NodeRendered,
            "computed" => isCheck ? SourceShapes.Check : SourceShapes.NodeComputed,
            _ => SourceShapes.NodeCall,
        };
        SourceMembers.Check(header, shape, declaration, path, walk.Refusals);

        if (SourceLayout.Text(header["producer"]) is { } producer)
        {
            var expected = kind == "agent" ? "generated" : "computed";
            if (producer != expected)
                walk.Refuse(SourceSentences.ProducerDisagrees(word, key, kind, producer, expected, path));
        }

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
            foreach (var (param, value) in with)
            {
                if (value is not null && SourceLayout.Text(value) is null)
                {
                    walk.Refuse(SourceSentences.MemberWrongType(
                        declaration, $"with.{param}", SourceMembers.Describe(value), "text", path));
                }
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
