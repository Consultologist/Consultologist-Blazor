using System.Globalization;
using System.Text.Json.Nodes;
using Scriban.Syntax;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1506 (specVersion 31, area 09; part 08 §§ 9, 10; part 06 § 9; R10, D5, D6): sites and substitutions. Each output tag of a
/// text is a site, numbered in the text's source order (the body's numbers are the composition's <see cref="Composition.Sites"/>).
/// Each time one is evaluated it records a substitution where its text is written, as run spots are: in the text being
/// written, moved by where a placed unit is spliced, and counted in runes in the composed text at the end. A placement
/// records none of its own: the tags of the unit it places stand at its site. So substitutions are in the order of the text
/// and never overlap, and each one's slice of the composed text is exactly what it wrote, after escaping.
/// </summary>
public static partial class BodyComposer
{
    internal sealed partial class Composing
    {
        /// <summary>
        /// The site the output <paramref name="statement"/> of <paramref name="plan"/>'s text is: its 1-based number among the
        /// text's outputs, the path it reads (its first, its head rooted through <c>names</c>, D1; its head as written when it
        /// reads none), and whether it places a unit; <c>null</c> when it is no output tag (an assignment).
        /// </summary>
        public SiteTag? SiteOf(TextPlan plan, ScriptExpressionStatement statement)
        {
            var offset = Offset(statement);
            if (plan.Sites.TryGetValue(offset, out var known))
                return known;

            SiteTag? site = null;
            var outputs = plan.Syntax.Outputs;
            for (var i = 0; i < outputs.Count; i++)
            {
                if (outputs[i].Tag.Start <= offset && offset < outputs[i].Tag.End)
                {
                    var placement = statement.Expression is { } expression && plan.Placements.ContainsKey(Offset(DialectRules.Unwrap(expression)));
                    site = new SiteTag(i + 1, ReadOf(outputs[i]), placement);
                    break;
                }
            }

            plan.Sites[offset] = site;
            return site;
        }

        /// <summary>What <paramref name="output"/> reads, as a substitution names it: its first path, its head rooted (D1).</summary>
        private string ReadOf(TemplateOutput output)
        {
            if (output.Paths.Count == 0)
                return output.Head;
            var segments = output.Paths[0].Split('.');
            return segments.Length < 2 ? output.Paths[0] : string.Join('.', [Rooted(string.Join('.', segments[..2])), .. segments[2..]]);
        }
    }

    /// <summary>An output tag as a site: its number in its text, what it reads, and whether it places a unit.</summary>
    internal sealed record SiteTag(int Site, string Reads, bool Placement);

    /// <summary>
    /// A substitution written: its site and pass, what it read, where its text stands in the text it was written to (in
    /// UTF-16 code units, as the text is written) and the last renderer step of its chain (<c>render:&lt;n&gt;</c>).
    /// </summary>
    internal sealed record WrittenSubstitution(int Site, IReadOnlyList<int>? Pass, string Reads, int Start, int Length, string? Renders);

    /// <summary>
    /// Part 08 § 10's sites of <paramref name="syntax"/>: each output tag in source order, its line and column, its expression
    /// as written (between the braces, less any whitespace mark and the spaces around it) and the nearest <c>for</c> around it.
    /// </summary>
    internal static IReadOnlyList<ComposedSite> Sites(TemplateSyntax syntax)
    {
        var sites = new List<ComposedSite>(syntax.Outputs.Count);
        foreach (var output in syntax.Outputs)
        {
            var tag = output.Tag;
            var start = Math.Clamp(tag.Start + 2, 0, syntax.Text.Length);
            var end = Math.Clamp(tag.End - 2, start, syntax.Text.Length);
            var expression = syntax.Text[start..end].Trim();
            if (expression.StartsWith('~') || expression.StartsWith('-'))
                expression = expression[1..];
            if (expression.EndsWith('~') || expression.EndsWith('-'))
                expression = expression[..^1];
            sites.Add(new ComposedSite(
                sites.Count + 1, string.Create(CultureInfo.InvariantCulture, $"{tag.Line}:{tag.Column}"), expression.Trim(), output.Loop));
        }

        return sites;
    }

    /// <summary>
    /// <paramref name="written"/> (in the order of <paramref name="text"/>, none overlapping) as part 08 § 10's substitutions
    /// (R10): each one's start the runes of the text before it, its size its text's runes, its hash and value that text's.
    /// </summary>
    internal static IReadOnlyList<Substitution> InRunes(string text, IReadOnlyList<WrittenSubstitution> written)
    {
        var substitutions = new List<Substitution>(written.Count);
        int at = 0, runes = 0;
        foreach (var s in written)
        {
            if (s.Start < at)
                (at, runes) = (0, 0); // never, for substitutions in the order of the text: counted again from the start
            var start = Math.Clamp(s.Start, at, text.Length);
            runes += Runes(text, at, start - at);
            var length = Math.Clamp(s.Length, 0, text.Length - start);
            var value = text.Substring(start, length);
            var size = Runes(text, start, length);
            substitutions.Add(new Substitution(s.Site, s.Pass, s.Reads, CanonicalJson.HashText(value), runes, size, s.Renders, value));
            (at, runes) = (start, runes);
        }

        return substitutions;
    }

    /// <summary>The object deliverable's one substitution: its whole <paramref name="text"/>, read from <paramref name="reads"/>.</summary>
    internal static Substitution Whole(string text, string reads) =>
        new(1, null, reads, CanonicalJson.HashText(text), 0, Runes(text, 0, text.Length), null, text);

    /// <summary>The row's substitutions (<c>{ site, pass, reads, hash, start, size, renders }</c>), a renderer step named by <paramref name="key"/>; never a value.</summary>
    internal static JsonArray SubstitutionRows(string key, IReadOnlyList<Substitution> substitutions) =>
        new([.. substitutions.Select(s => (JsonNode)new JsonObject
        {
            ["site"] = s.Site,
            ["pass"] = s.Pass == null ? null : new JsonArray([.. s.Pass.Select(n => (JsonNode)n)]),
            ["reads"] = s.Reads,
            ["hash"] = s.Hash,
            ["start"] = s.Start,
            ["size"] = s.Size,
            ["renders"] = s.Renders == null ? null : $"{key}/{s.Renders}",
        })]);

    /// <summary>Part 08 § 10's sites as the row writes them: <c>{ site, at, expr, loop }</c>.</summary>
    internal static JsonArray SiteRows(IReadOnlyList<ComposedSite> sites) =>
        new([.. sites.Select(s => (JsonNode)new JsonObject { ["site"] = s.Site, ["at"] = s.At, ["expr"] = s.Expr, ["loop"] = s.Loop })]);
}
