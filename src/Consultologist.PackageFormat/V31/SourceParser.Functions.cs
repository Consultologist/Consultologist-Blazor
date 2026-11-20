using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1499 (specVersion 31, area 02): <c>functions/</c> (part 04 § 7): each listed function folder,
/// read through its <c>index.json</c> into a <see cref="SourceFunction"/>: its parameters, its
/// <c>uses</c>, its body (keyed from the function's own root, <c>node.apply_instructions</c>) and its
/// four definition folders. What a function reads, calls and returns is judged by later areas (D16).
/// </summary>
public static partial class SourceParser
{
    /// <summary>The top-level folders a function may not hold (S04-062).</summary>
    private static readonly string[] TopLevelOnly = ["checks", "deliverables"];

    /// <summary>The package input members a parameter does not have (S04-067).</summary>
    private static readonly string[] InputOnly = ["required", "channel"];

    /// <summary><c>functions/</c>: its index, then each listed function, in listed order.</summary>
    private static SourceIndex<SourceFunction> ReadFunctions(SourceWalk walk)
    {
        var read = Open(walk, "functions", SourceShapes.Index, MemberForm.Folder);
        if (read == null)
            return new SourceIndex<SourceFunction>("functions", null, []);

        var functions = new List<SourceFunction>();
        foreach (var member in read.Members)
        {
            if (ReadFunction(walk, member) is { } function)
                functions.Add(function);
        }

        return new SourceIndex<SourceFunction>("functions", SourceLayout.Lines(read.Index["description"]), functions);
    }

    /// <summary>
    /// One function's folder (part 04 §§ 7.1–7.2.1): a <c>checks/</c> or <c>deliverables/</c> folder in it
    /// (S04-062, its files then not strays); its index by <see cref="SourceShapes.FunctionIndex"/> (a
    /// <c>null</c> <c>ret</c> is S04-011, <c>nodes</c> <c>[]</c> A02-008, an id listed twice S04-013);
    /// each parameter's id and shape (a <c>required</c> or <c>channel</c> is S04-067); <c>uses</c> by
    /// <see cref="SourceShapes.Uses"/>; the body; and the four definition folders, read in full as the
    /// package's are (S01-007 when one is missing). Null when the function has a fault of its own.
    /// </summary>
    private static SourceFunction? ReadFunction(SourceWalk walk, ListedMember member)
    {
        var folder = member.Path;
        var id = member.Id;
        var name = $"Function '{id}'";
        var before = walk.Refusals.Count;

        foreach (var top in TopLevelOnly)
        {
            var inner = SourceLayout.Join(folder, top);
            if (!walk.HasFolder(inner))
                continue;
            walk.Refuse(SourceSentences.FunctionHoldsTopLevelFolder(id, top, inner));
            walk.AccountTree(inner);
        }

        var index = SourceLayout.ReadIndex(walk, folder);
        SourceBlock? body = null;
        var inputs = new List<SourceParameter>();
        if (index != null)
        {
            var path = SourceLayout.Join(folder, "index.json");
            SourceMembers.Check(index, SourceShapes.FunctionIndex, name, path, walk.Refusals, ["ret"]);
            CheckRet(walk, index, name, path);
            ReadParameters(walk, index["inputs"], id, name, path, inputs);
            if (index["uses"] is JsonObject uses)
                SourceMembers.Check(uses, SourceShapes.Uses, $"{name} uses", path, walk.Refusals);
            if (index["nodes"] is JsonArray { Count: 0 })
                walk.Refuse(SourceSentences.NoBody(name, path));

            var members = SourceLayout.ReadList(
                walk, folder, $"{path} nodes", index["nodes"], MemberForm.FileOrFolder,
                (new HashSet<string>(StringComparer.Ordinal), child => SourceSentences.BodyListsTwice(name, child, path)));
            var faulty = walk.Refusals.Count != before;
            var nodes = ReadBody(walk, members, "node", "");
            if (!faulty)
                body = new SourceBlock(nodes, SourceLayout.Text(index["ret"]));
        }

        var definitions = ReadDefinitions(walk, folder);
        return index != null && body != null
            ? new SourceFunction(id, folder, index, inputs, body, definitions)
            : null;
    }

    /// <summary>
    /// A function's <c>inputs</c> (part 04 § 7.2.1), in written order: each id held to the id rules,
    /// each value an object (A02-001 on <c>inputs.&lt;id&gt;</c>) of <see cref="SourceShapes.Parameter"/>'s
    /// shape, where a package input's <c>required</c> or <c>channel</c> is S04-067 rather than unknown.
    /// </summary>
    private static void ReadParameters(
        SourceWalk walk, JsonNode? node, string function, string name, string path, List<SourceParameter> parameters)
    {
        if (node is not JsonObject declared)
            return;

        foreach (var (id, value) in declared)
        {
            if (SourceIds.Check(id, path) is { } bad)
                walk.Refuse(bad);
            if (value is not JsonObject parameter)
            {
                walk.Refuse(SourceSentences.MemberWrongType(name, $"inputs.{id}", SourceMembers.Describe(value), "an object", path));
                continue;
            }

            SourceMembers.Check(parameter, SourceShapes.Parameter, $"{name} parameter '{id}'", path, walk.Refusals, InputOnly);
            foreach (var member in InputOnly.Where(parameter.ContainsKey))
                walk.Refuse(SourceSentences.ParameterDeclaresInputMember(function, id, member, path));
            parameters.Add(new SourceParameter(id, parameter));
        }
    }
}
