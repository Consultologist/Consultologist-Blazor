using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 7; part 01 § 5): parts. An agent's <c>parts</c> (<c>null</c> or <c>{}</c>
/// for none) holds, per part id, exactly <c>if</c> (a rule, or <c>null</c>) and <c>text</c> (a list of strings); a part
/// wrong in kind or missing a member is refused with the reader's member sentences (S01-021, S01-022, S01-023,
/// A02-001), which the reader leaves to this step. The text, its lines joined with <c>\n</c>, is a template at the
/// node's place, read as a part's text: it may place snippets (under the guard) and a macro, and has no <c>if</c> and
/// places no <c>part.*</c> (A07-002). The node's body, and only its body, places each declared part exactly once
/// (S05-038, S05-039, S05-040). Only agent nodes have parts (S05-041, where the reader's S01-023 does not stand first).
/// </summary>
public static partial class SourceTemplates
{
    /// <summary>A part's members (part 05 § 7.1): <c>if</c>, a rule or <c>null</c>; <c>text</c>, a list of strings.</summary>
    private static readonly DeclarationShape PartShape = new("Part",
    [
        new MemberShape("if", MemberType.Condition, true),
        new MemberShape("text", MemberType.Lines, false),
    ]);

    /// <summary>A declared part: its text checked (<c>null</c> when it has no text to check) and its <c>if</c> as written.</summary>
    private sealed record PartUnit(CheckedBody? Body, JsonNode? If);

    private sealed partial class Context
    {
        /// <summary>
        /// The parts <paramref name="leaf"/> declares, by id, each held to its members and its text checked at
        /// <paramref name="prompt"/>'s place, with the units the text places.
        /// </summary>
        private Dictionary<string, PartUnit> Parts(SourceLeaf leaf, TemplateHost prompt)
        {
            var declared = new Dictionary<string, PartUnit>(StringComparer.Ordinal);
            if (leaf.Header["parts"] is not JsonObject parts)
                return declared; // null is none; any other kind is the reader's A02-001.

            foreach (var (id, value) in parts)
            {
                if (value is not JsonObject part)
                {
                    Refusals.Add(value is null
                        ? SourceSentences.MemberNull($"Node '{leaf.Id}'", $"parts.{id}", leaf.Path)
                        : SourceSentences.MemberWrongType($"Node '{leaf.Id}'", $"parts.{id}", SourceMembers.Describe(value), "an object", leaf.Path));
                    declared[id] = new PartUnit(null, null);
                    continue;
                }

                var subject = $"Part '{id}' of node '{leaf.Id}'";
                SourceMembers.Check(part, PartShape, subject, leaf.Path, Refusals);
                CheckedBody? text = null;
                if (part["text"] is JsonArray lines && lines.Select(l => l is null ? null : SourceLayout.Text(l)).ToList() is var strings
                    && strings.All(l => l is not null))
                {
                    var host = new TemplateHost(TemplateSite.PartText, subject, leaf.Path, leaf, prompt.Function, prompt.Frame, id);
                    if (Body(host, TemplateSyntax.Parse(string.Join("\n", strings)), null) is { } body)
                        text = body with { Units = Expand(body, $"part.{id}", TemplateSite.PartText, null, 0, null, null) };
                }

                declared[id] = new PartUnit(text, part["if"]);
            }

            return declared;
        }

        /// <summary>
        /// Part 05 § 7.2: <paramref name="body"/> places each part of <paramref name="declared"/> exactly once (S05-039,
        /// S05-040) and no other (S05-038); placements in its parts' text and its snippets do not count.
        /// </summary>
        private void CountParts(CheckedBody body, SourceLeaf leaf, IReadOnlyDictionary<string, PartUnit> declared)
        {
            var placed = body.Placements.Where(p => p.Kind == "part")
                .GroupBy(p => p.Unit["part.".Length..], StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            foreach (var (id, count) in placed)
            {
                if (!declared.ContainsKey(id))
                    Refusals.Add(DialectSentences.PlacesUndeclaredPart(leaf.Id, id, leaf.Path));
                else if (count > 1)
                    Refusals.Add(DialectSentences.PartPlacedTwice(leaf.Id, id, count, leaf.Path));
            }

            foreach (var id in declared.Keys.Where(id => !placed.ContainsKey(id)))
                Refusals.Add(DialectSentences.PartNeverPlaced(leaf.Id, id, leaf.Path));
        }

        /// <summary>S05-041: a node other than an agent declaring parts (anything but <c>null</c> or <c>{}</c>), in <c>nodes/</c>, <c>checks/</c> and each function.</summary>
        public void PartsOffAgents()
        {
            var leaves = ScopeTree.Walk(package.Nodes.Nodes).Concat(ScopeTree.Walk(package.Checks.Nodes))
                .Concat(package.Functions.Items.SelectMany(f => ScopeTree.Walk(f.Body.Nodes)));
            foreach (var leaf in leaves.OfType<SourceLeaf>())
            {
                if (leaf.Kind != "agent" && leaf.Header["parts"] is { } parts && parts is not JsonObject { Count: 0 })
                    Refusals.Add(DialectSentences.DeclaresPartsNotAgent(leaf.Id, leaf.Path));
            }
        }
    }
}
