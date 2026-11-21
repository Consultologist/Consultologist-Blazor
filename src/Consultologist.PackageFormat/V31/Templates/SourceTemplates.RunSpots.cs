using System.Globalization;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 4; D4): run spots. An output that reads <c>run.*</c>, a value declared
/// run-dependent whose producer is not an agent, or (in a function) a parameter some call binds to either, is a run spot,
/// numbered in source order, with what stands for it in the run-neutral render (<see cref="TemplateRunSpot.Neutral"/>),
/// and warns S05-060; one that also reads another path, another run fact or run-dependent value included, is S05-024 when
/// its first such read is a run fact and S05-025 when it is a run-dependent value; such a read as a <c>for</c>'s source is
/// S05-006 (in an <c>if</c> it is S05-007, said by the body-<c>if</c> rule).
/// </summary>
public static partial class SourceTemplates
{
    private sealed partial class BodyCheck
    {
        private void RunSpots()
        {
            if (host.Site == TemplateSite.Renderer)
                return;

            foreach (var output in syntax.Outputs)
            {
                var scope = Scope(output.Tag.Start, output.Depth);
                var paths = output.Paths.Distinct(StringComparer.Ordinal).ToList();
                if (paths.Select(p => (Path: p, Neutral: Spot(p, scope))).FirstOrDefault(p => p.Neutral != null) is not { Neutral: { } neutral, Path: var reads })
                    continue;

                // Part 05 § 4: the spot's read is the only thing the output reads; any other path (another run fact or
                // run-dependent value included) is "something else", said by what the first spot read is.
                var line = output.Tag.Line;
                if (paths.FirstOrDefault(p => p != reads) is { } other)
                {
                    Refusals.Add(IsRunFact(reads, scope)
                        ? DialectSentences.ReadsRunWithMore(host.Subject, other, reads["run.".Length..].Split('.')[0], line, host.Path)
                        : DialectSentences.ReadsRunDependentWithMore(host.Subject, other, reads, line, host.Path));
                    continue;
                }

                var at = string.Create(CultureInfo.InvariantCulture, $"{line}:{output.Tag.Column}");
                spots.Add(new TemplateRunSpot(spots.Count + 1, at, reads, null, neutral));
                Warnings.Add(DialectSentences.ReadsRunFact(host.Subject, reads, line, host.Path));
            }

            foreach (var loop in syntax.Fors)
            {
                var scope = Scope(loop.Tag.Start, loop.Depth);
                if (Path(loop.Source) is { } source && Spot(source, scope) != null)
                    Refusals.Add(DialectSentences.ForOverNotAList(host.Subject, source, loop.Tag.Line, host.Path));
            }
        }

        /// <summary>
        /// Whether <paramref name="path"/> is a run spot's read, and if so what stands for it in the run-neutral render
        /// (D4): a run fact (<see cref="TemplateRunSpot.Marker"/>), a run-dependent value (<see cref="IsRunDependent"/>),
        /// or a parameter some call binds to either; <c>null</c> when it is none.
        /// </summary>
        private string? Spot(string path, List<ForVariable> scope)
        {
            if (IsRunFact(path, scope))
                return TemplateRunSpot.Marker;
            if (IsRunDependent(path, scope) is { } neutral)
                return neutral;
            var s = path.Split('.');
            return s.Length >= 2 && s[0] == "param" && host.Function is { } function && scope.All(v => v.Name != "param")
                ? producers.Parameter(function, s[1])
                : null;
        }

        /// <summary><c>run.&lt;fact&gt;</c> (one of the four), not a <c>for</c> variable named <c>run</c>.</summary>
        private static bool IsRunFact(string path, List<ForVariable> scope)
        {
            var s = path.Split('.');
            return s.Length >= 2 && s[0] == "run" && JsonLogicOperations.RunFacts.Contains(s[1]) && scope.All(v => v.Name != "run");
        }

        /// <summary>
        /// A read of a node or check visible here whose header declares <c>runDependent</c> true and whose value is not a
        /// model's (<see cref="Producers.Generated"/>): what stands for it in the run-neutral render
        /// (<see cref="Producers.Neutral"/>); <c>null</c> for any other read.
        /// </summary>
        private string? IsRunDependent(string path, List<ForVariable> scope)
        {
            var s = path.Split('.');
            if (s.Length < 2 || s[0] is not ("node" or "check") || scope.Any(v => v.Name == s[0]))
                return null;
            return host.Frame.Visible(s[0], s[1]) is { } node && producers.RunDependent(node) ? producers.Neutral(node) : null;
        }
    }

    /// <summary>
    /// #1504 (part 05 § 4's table, D4): whether a node's value is a model's output, which has no run-neutral form, so a
    /// read of it is no run spot: an agent's; a <c>call</c>'s when its function's <c>ret</c> is; a <c>while</c>'s, an
    /// <c>if</c>'s or a <c>case</c>'s when every <c>ret</c> of its bodies is. A <c>for</c>'s value, a <c>rendered</c> or a
    /// <c>computed</c> node's is not. And what stands for a run-dependent read in the run-neutral render
    /// (<see cref="Neutral"/>), and which parameters are run-dependent (<see cref="Parameter"/>). Each node and parameter
    /// is worked out once, and a ring (calls are refused in one earlier) ends false.
    /// </summary>
    private sealed class Producers(Resolution resolution)
    {
        private readonly Dictionary<SourceNode, bool> known = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<SourceNode, string> neutrals = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<(SourceFunction Function, string Id), string?> parameters = [];

        public bool Generated(SourceNode node)
        {
            if (known.TryGetValue(node, out var value))
                return value;
            known[node] = false;
            value = node switch
            {
                SourceLeaf { Kind: "agent" } => true,
                SourceLeaf { Kind: "call" } call => resolution.Calls.TryGetValue(call, out var binding)
                    && Returned(binding.Function.Body) is { } ret && Generated(ret),
                SourceWhile or SourceIf or SourceCase => ScopeTree.Blocks(node).Where(b => b.Ret != null).ToList() is { Count: > 0 } blocks
                    && blocks.All(b => Returned(b) is { } ret && Generated(ret)),
                _ => false,
            };
            known[node] = value;
            return value;
        }

        /// <summary>Whether <paramref name="node"/>'s header declares <c>runDependent</c> true and its value is not a model's: a read of it is a run spot.</summary>
        public bool RunDependent(SourceNode node) =>
            node.Header["runDependent"] is JsonValue declared && declared.TryGetValue<bool>(out var flag) && flag && !Generated(node);

        /// <summary>
        /// D4: what stands for a run-dependent read of <paramref name="node"/> in the run-neutral render: a
        /// <c>rendered</c> node's own run-neutral text (<see cref="TemplateRunSpot.Text"/>); a <c>call</c>'s, a
        /// <c>while</c>'s, an <c>if</c>'s and a <c>case</c>'s as their <c>ret</c>'s (text when any <c>ret</c> that is no
        /// model's gives text); anything else the marker.
        /// </summary>
        public string Neutral(SourceNode node)
        {
            if (neutrals.TryGetValue(node, out var value))
                return value;
            neutrals[node] = TemplateRunSpot.Marker;
            IEnumerable<SourceNode?> rets = node switch
            {
                SourceLeaf { Kind: "call" } call when resolution.Calls.TryGetValue(call, out var binding) => [Returned(binding.Function.Body)],
                SourceWhile or SourceIf or SourceCase => ScopeTree.Blocks(node).Select(Returned),
                _ => [],
            };
            value = node is SourceLeaf { Kind: "rendered" }
                || rets.Any(r => r is not null && !Generated(r) && Neutral(r) == TemplateRunSpot.Text)
                ? TemplateRunSpot.Text
                : TemplateRunSpot.Marker;
            neutrals[node] = value;
            return value;
        }

        /// <summary>
        /// D4: whether parameter <paramref name="id"/> of <paramref name="function"/> is run-dependent: some call binds it
        /// to <c>run.*</c>, to a node it sees that is run-dependent (<see cref="RunDependent"/>), or to a parameter of its
        /// own function that is; what stands for it in the run-neutral render (the text only when every such binding gives
        /// text), or <c>null</c> when it is not.
        /// </summary>
        public string? Parameter(SourceFunction function, string id)
        {
            if (parameters.TryGetValue((function, id), out var value))
                return value;
            parameters[(function, id)] = null;
            var found = new List<string>();
            foreach (var (call, binding) in resolution.Calls)
            {
                if (!ReferenceEquals(binding.Function, function) || call.Header["with"] is not JsonObject with
                    || SourceLayout.Text(with[id]) is not { } argument)
                {
                    continue;
                }

                var s = argument.Split('.');
                var frame = resolution.Tree.FrameOf(call);
                if (s.Length >= 2 && s[0] == "run" && JsonLogicOperations.RunFacts.Contains(s[1]))
                    found.Add(TemplateRunSpot.Marker);
                else if (s.Length >= 2 && s[0] is "node" or "check" && frame.Visible(s[0], s[1]) is { } node && RunDependent(node))
                    found.Add(Neutral(node));
                else if (s.Length >= 2 && s[0] == "param" && frame.Function is { } caller && Parameter(caller, s[1]) is { } inner)
                    found.Add(inner);
            }

            value = found.Count == 0 ? null : found.All(n => n == TemplateRunSpot.Text) ? TemplateRunSpot.Text : TemplateRunSpot.Marker;
            parameters[(function, id)] = value;
            return value;
        }

        /// <summary>The node <paramref name="block"/>'s <c>ret</c> names, by its key.</summary>
        private static SourceNode? Returned(SourceBlock block) =>
            block.Ret is { } ret ? ScopeTree.Walk(block.Nodes).FirstOrDefault(n => n.Key == ret) : null;
    }
}
