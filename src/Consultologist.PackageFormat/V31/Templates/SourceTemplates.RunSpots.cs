using System.Globalization;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 4; D4): run spots. An output that reads <c>run.*</c>, or a value declared
/// run-dependent whose producer is not an agent, is a run spot, numbered in source order, and warns S05-060; one that
/// also reads another path, another run fact or run-dependent value included, is S05-024 when its first such read is a
/// run fact and S05-025 when it is a run-dependent value; such a read as a <c>for</c>'s source is
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
                if (paths.FirstOrDefault(p => IsRunFact(p, scope) || IsRunDependent(p, scope)) is not { } reads)
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
                spots.Add(new TemplateRunSpot(spots.Count + 1, at, reads));
                Warnings.Add(DialectSentences.ReadsRunFact(host.Subject, reads, line, host.Path));
            }

            foreach (var loop in syntax.Fors)
            {
                var scope = Scope(loop.Tag.Start, loop.Depth);
                if (Path(loop.Source) is { } source && (IsRunFact(source, scope) || IsRunDependent(source, scope)))
                    Refusals.Add(DialectSentences.ForOverNotAList(host.Subject, source, loop.Tag.Line, host.Path));
            }
        }

        /// <summary><c>run.&lt;fact&gt;</c> (one of the four), not a <c>for</c> variable named <c>run</c>.</summary>
        private static bool IsRunFact(string path, List<ForVariable> scope)
        {
            var s = path.Split('.');
            return s.Length >= 2 && s[0] == "run" && JsonLogicOperations.RunFacts.Contains(s[1]) && scope.All(v => v.Name != "run");
        }

        /// <summary>A read of a node or check visible here whose header declares <c>runDependent</c> true and whose value is not a model's (<see cref="Producers.Generated"/>).</summary>
        private bool IsRunDependent(string path, List<ForVariable> scope)
        {
            var s = path.Split('.');
            if (s.Length < 2 || s[0] is not ("node" or "check") || scope.Any(v => v.Name == s[0]))
                return false;
            return host.Frame.Visible(s[0], s[1]) is { } node
                && node.Header["runDependent"] is JsonValue declared && declared.TryGetValue<bool>(out var flag) && flag
                && !producers.Generated(node);
        }
    }

    /// <summary>
    /// #1504 (part 05 § 4's table, D4): whether a node's value is a model's output, which has no run-neutral form, so a
    /// read of it is no run spot: an agent's; a <c>call</c>'s when its function's <c>ret</c> is; a <c>while</c>'s, an
    /// <c>if</c>'s or a <c>case</c>'s when every <c>ret</c> of its bodies is. A <c>for</c>'s value, a <c>rendered</c> or a
    /// <c>computed</c> node's is not. Each node is worked out once, and a ring (calls are refused in one earlier) ends false.
    /// </summary>
    private sealed class Producers(Resolution resolution)
    {
        private readonly Dictionary<SourceNode, bool> known = new(ReferenceEqualityComparer.Instance);

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

        /// <summary>The node <paramref name="block"/>'s <c>ret</c> names, by its key.</summary>
        private static SourceNode? Returned(SourceBlock block) =>
            block.Ret is { } ret ? ScopeTree.Walk(block.Nodes).FirstOrDefault(n => n.Key == ret) : null;
    }
}
