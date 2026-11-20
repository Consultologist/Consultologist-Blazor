using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1500 (specVersion 31, area 03): the type rules of a package's nodes (part 02 §§ 1–2, § 7), on
/// every node of <c>nodes/</c>, <c>checks/</c> and each function's body, in its scope: an agent's
/// binding (S02-012, D21), its union root (S02-027) and the strict-mode limits of what it is sent
/// (S02-055, S02-056, D3); a rendered node's text (S02-013, D15); a leaf's <c>reproducible</c>
/// (S01-027, D5); a flow node's declared format against what its <c>ret</c> gives (S02-015,
/// S02-016, D4, D24, D25). A <c>call</c>'s format, a computed node's expression, a check's
/// <c>check_result</c> and a loop's <c>in</c> are later areas'.
/// </summary>
public static partial class SourceTypes
{
    /// <summary>
    /// One package's walk over its nodes. A node is judged only when its own <c>format</c> passed the
    /// format members' rules and reaches no refused format file; a flow node also needs each
    /// <c>ret</c> child it reads to be sound (judged, with no refusal about its format). Holds what
    /// <see cref="PackageTypes"/> reports: each node's declared format and each agent's binding.
    /// </summary>
    private sealed class NodeJudge(
        TypeRegistry registry,
        Dictionary<JsonObject, SourceFormat> files,
        HashSet<string> refused,
        HashSet<SourceNode> passed,
        List<SourceRefusal> refusals)
    {
        private const string Producer = "generated";

        private readonly AgentBindings bindings = AgentBindings.Read(registry.Bindings, registry.Agents);

        /// <summary>The format names whose sent copy was held to the strict-mode limits, once each across the package.</summary>
        private readonly HashSet<string> limited = new(StringComparer.Ordinal);

        /// <summary>The nodes whose format a parent may derive from.</summary>
        private readonly HashSet<SourceNode> sound = new(ReferenceEqualityComparer.Instance);

        public Dictionary<string, JsonNode> NodeFormats { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, AgentBinding> Bindings { get; } = new(StringComparer.Ordinal);

        /// <summary>Each node of a body, in listed order, its bodies first.</summary>
        public void Body(FormatSet set, IReadOnlyList<SourceNode> nodes)
        {
            foreach (var node in nodes)
            {
                foreach (var body in Bodies(node))
                    Body(set, body.Nodes);

                if (node.Header["format"] is not { } format)
                    continue;
                NodeFormats.TryAdd(node.Path, format);
                if (!passed.Contains(node) || ReachesRefused(set, format, files, refused))
                    continue;

                var ok = node is SourceLeaf leaf ? Leaf(set, leaf, format) : Flow(set, node, format);
                if (ok)
                    sound.Add(node);
            }
        }

        /// <summary>A leaf's rules; true when none was about its format (S01-027 is not).</summary>
        private bool Leaf(FormatSet set, SourceLeaf leaf, JsonNode format)
        {
            var isCheck = leaf.Key.StartsWith("check.", StringComparison.Ordinal);
            var before = refusals.Count;
            switch (leaf.Kind)
            {
                case "agent":
                    Agent(set, leaf, format);
                    break;
                case "rendered" when !isCheck && FormatTypes.TextKind(format, set) == null:
                    refusals.Add(FormatSentences.RenderedNotText(leaf.Id, TypeStrings.Print(format, set), leaf.Path));
                    break;
            }

            var ok = refusals.Count == before;

            // D5: an agent is not reproducible, a rendered or computed node is; a call's is area 05's.
            bool? kinds = leaf.Kind switch
            {
                "agent" => false,
                "rendered" or "computed" => true,
                _ => null,
            };
            if (kinds is { } derived && leaf.Header["reproducible"] is JsonValue value
                && value.TryGetValue<bool>(out var declared) && declared != derived)
            {
                refusals.Add(FormatSentences.ReproducibleNotTheKinds(Word(leaf), leaf.Key, declared, leaf.Path));
            }

            return ok;
        }

        /// <summary>
        /// An agent (whose format the reader holds to a name, D15): its binding for <c>generated</c>
        /// (D21), a union at the root of what it returns, and, the first time the format's name is
        /// met in the package, the strict-mode limits of the copy it is sent with its agent's rejects.
        /// </summary>
        private void Agent(FormatSet set, SourceLeaf leaf, JsonNode format)
        {
            if (FormatSet.NameOf(format) is not { } name || set.Find(name) is not { } found)
                return;

            var binding = bindings.Find(name, found.Origin, Producer);
            if (binding == null)
                refusals.Add(FormatSentences.NoBinding(leaf.Id, name, Producer, leaf.Path));
            else
                Bindings[leaf.Path] = binding;

            if (found.Schema.ContainsKey("anyOf"))
                refusals.Add(FormatSentences.AnyOfAtRoot(leaf.Id, TypeStrings.Print(format, set), leaf.Path));

            if (binding != null && FormatTypes.Expands(format, set) && limited.Add(name))
                refusals.AddRange(SentFormat.CheckLimits(name, SentFormat.Prepare(format, set, binding.StrictRejects), leaf.Path));
        }

        /// <summary>
        /// D24: a <c>for</c> is a list of its <c>ret</c> child's declared format, a <c>while</c> that
        /// format, an <c>if</c> or <c>case</c> the union of its non-empty bodies' (in body order).
        /// Compared with the declared format by type equality: S02-016 for each body outside a
        /// declared union (D25), else S02-015. Not judged when a <c>ret</c> child is not sound,
        /// or no body returns anything. True when it said nothing.
        /// </summary>
        private bool Flow(FormatSet set, SourceNode node, JsonNode declared)
        {
            var rets = new List<(int N, JsonNode Format)>();
            var n = 0;
            foreach (var body in Bodies(node))
            {
                n++;
                if (body.Ret == null)
                    continue;
                var child = body.Nodes.FirstOrDefault(c => c.Key == body.Ret);
                if (child == null || !sound.Contains(child) || child.Header["format"] is not { } format)
                    return false;
                rets.Add((n, format));
            }

            var derived = node switch
            {
                SourceFor when rets.Count > 0 => FormatTypes.ListOf(rets[0].Format),
                SourceWhile when rets.Count > 0 => rets[0].Format,
                SourceIf or SourceCase => FormatTypes.UnionOf(rets.Select(r => r.Format).ToList(), set),
                _ => null,
            };
            if (derived == null || FormatTypes.Same(declared, derived, set))
                return true;

            var path = FilePath(node);
            if (node is SourceIf or SourceCase && FormatTypes.TypeForm(declared, set) is JsonObject { } form
                && form["anyOf"] is JsonArray members)
            {
                var union = members.Select(m => CanonicalJson.Write(m)).ToHashSet(StringComparer.Ordinal);
                var outside = rets.Where(r => !union.Contains(CanonicalJson.Write(FormatTypes.TypeForm(r.Format, set)))).ToList();
                foreach (var (branch, format) in outside)
                {
                    refusals.Add(FormatSentences.BranchNotInUnion(
                        branch, node.Kind, node.Id, TypeStrings.Print(format, set), TypeStrings.Print(declared, set), path));
                }

                if (outside.Count > 0)
                    return false;
            }

            refusals.Add(FormatSentences.FlowFormatDiffers(
                Word(node), node.Id, TypeStrings.Print(declared, set), TypeStrings.Print(derived, set), path));
            return false;
        }
    }
}
