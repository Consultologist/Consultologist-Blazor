using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Scriban.Runtime;
using Scriban.Syntax;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 4; part 08 §§ 4, 8.2; part 03 § 4.4; D4, D5): run spots and the two hashes.
/// Each output tag of the entry's <c>runSpots</c> (matched by its <c>unit</c> and <c>at</c>, in the order of appearance the
/// plan walks) writes its output itself while the body renders, so the place it stands in the sent text is recorded as it
/// is written (in the body, or in a placed unit and moved by where that unit is spliced); the text is never searched.
/// The run-neutral text is the sent text with each spot's output replaced at that place: a <c>marker</c> spot by
/// <c>{{run:&lt;n&gt;}}</c>, a <c>text</c> spot by the same pipeline run over the read node's run-neutral text (the
/// scope's <c>&lt;rooted key&gt;.neutral</c>; the node's content when the scope has none). Both come from the one render.
/// A macro body's <c>run.*</c> reads are filled directly (D5): its text is planned under a site no run spot names.
/// </summary>
public static partial class BodyComposer
{
    internal sealed partial class Composing
    {
        private readonly HashSet<int> spotsPlanned = [];

        /// <summary>Whether a <c>text</c> spot's pipeline is being run over a run-neutral text: what it runs is recorded nowhere.</summary>
        public bool InNeutralPass { get; private set; }

        /// <summary>
        /// The entry's run spot the output <paramref name="statement"/> of <paramref name="plan"/>'s text is (the first not yet
        /// planned whose <c>unit</c> is the text's site and whose <c>at</c> is the tag's line and column), or <c>null</c>.
        /// </summary>
        private PlannedSpot? SpotOf(TextPlan plan, ScriptExpressionStatement statement)
        {
            var offset = Offset(statement);
            var tag = plan.Syntax.Tags.FirstOrDefault(t => t.Start <= offset && offset < t.End);
            if (tag == null || Entry.Json["runSpots"] is not JsonArray spots)
                return null;
            var at = string.Create(CultureInfo.InvariantCulture, $"{tag.Line}:{tag.Column}");
            for (var i = 0; i < spots.Count; i++)
            {
                if (spots[i] is JsonObject spot && SourceLayout.Text(spot["at"]) == at && SourceLayout.Text(spot["unit"]) == plan.Site
                    && spot["site"] is JsonValue site && site.TryGetValue<int>(out var n) && SourceLayout.Text(spot["reads"]) is { } reads
                    && spotsPlanned.Add(i))
                {
                    return new PlannedSpot(n, reads, SourceLayout.Text(spot["neutral"]) == TemplateRunSpot.Text);
                }
            }

            return null;
        }

        /// <summary>
        /// Writes the run spot <paramref name="spot"/> (its statement evaluated by <paramref name="evaluate"/>) to
        /// <paramref name="context"/>'s output and records where it stands, its output, what stands for it in the
        /// run-neutral text, and the hash of the value it read. #1505 (area 08, D7): with <paramref name="escape"/> its
        /// output is escaped, as written and as recorded, and so is a <c>text</c> spot's run-neutral output, its markers
        /// kept (<see cref="EscapedNeutral"/>); a <c>marker</c> spot's marker is not escaped.
        /// </summary>
        public void WriteSpot(ComposeContext context, PlannedSpot spot, Func<object?> evaluate, bool escape)
        {
            var output = TemplateValues.Written(evaluate()) ?? throw context.Stopped(null);
            if (escape)
                output = MarkdownEscape.Escape(output);
            var neutral = spot.Text
                ? (NeutralOutput(context, spot, evaluate) is { } other ? (escape ? EscapedNeutral(other) : other) : output)
                : string.Create(CultureInfo.InvariantCulture, $"{{{{run:{spot.Site}}}}}");
            var start = context.Written.Length;
            if (output.Length > 0)
                context.Write(output);
            context.Spots.Add(new FilledSpot(spot, start, output.Length, output, neutral, CanonicalJson.Hash(ValueAt(spot.Reads))));
        }

        /// <summary>
        /// A <c>text</c> spot's pipeline run again with its read giving the node's run-neutral text (the scope's
        /// <c>&lt;rooted key&gt;.neutral</c>); <c>null</c> when the scope carries none, so the node's content stands.
        /// The run is recorded nowhere (no renderer sub-row) and the value is put back after.
        /// </summary>
        private string? NeutralOutput(ComposeContext context, PlannedSpot spot, Func<object?> evaluate)
        {
            var segments = spot.Reads.Split('.');
            if (segments.Length < 3 || Json(Rooted(string.Join('.', segments[..2])) + ".neutral") is not { } neutral
                || !Find(Globals(), segments[..^1], out var found) || found is not ScriptObject holder)
            {
                return null;
            }

            var member = segments[^1];
            holder.TryGetValue(member, out var real);
            holder.SetValue(member, TemplateValues.FromJson(neutral), false);
            InNeutralPass = true;
            try
            {
                return TemplateValues.Written(evaluate()) ?? throw context.Stopped(null);
            }
            finally
            {
                InNeutralPass = false;
                holder.SetValue(member, real, false);
            }
        }

        /// <summary>The rooted key the written head <paramref name="head"/> (<c>node.x</c>) names (D1): <c>names</c>' entry, else the head itself.</summary>
        private string Rooted(string head) => SourceLayout.Text((Entry.Json["names"] as JsonObject)?[head]) ?? head;

        /// <summary>The value at the written path <paramref name="path"/>, its head rooted (D1), in the scope; <c>null</c> when absent.</summary>
        private JsonNode? ValueAt(string path)
        {
            var segments = path.Split('.');
            return segments.Length < 2 ? Json(path) : Json(string.Join('.', [Rooted(string.Join('.', segments[..2])), .. segments[2..]]));
        }

        /// <summary>The scope's value at the dotted <paramref name="path"/>, or <c>null</c>.</summary>
        private JsonNode? Json(string path)
        {
            JsonNode? at = Request.Scope;
            foreach (var segment in path.Split('.'))
            {
                if (at is not JsonObject obj || !obj.TryGetPropertyValue(segment, out at))
                    return null;
            }

            return at;
        }
    }

    /// <summary>The run spot <paramref name="Site"/> of the entry as planned at its tag: what it reads, and whether the node's run-neutral text stands for it (<c>neutral: "text"</c>) rather than its marker.</summary>
    internal sealed record PlannedSpot(int Site, string Reads, bool Text);

    /// <summary>A run spot written: where its output stands in the text it was written to, the output, what stands for it in the run-neutral text, and the hash of the value read.</summary>
    internal sealed record FilledSpot(PlannedSpot Spot, int Start, int Length, string Output, string Neutral, string ValueHash)
    {
        /// <summary>
        /// Part 08 § 8.2's run spot: the output kept only when the spot reads a raw run fact; a run-dependent value's follows
        /// 7-day retention.
        /// </summary>
        public ComposedSpot Composed() => new(
            Spot.Site, Spot.Reads, Start, Length, CanonicalJson.HashText(Output),
            Spot.Reads.StartsWith("run.", StringComparison.Ordinal) ? Output : null, ValueHash);
    }

    /// <summary>A placed unit's rendered text and the run spots written in it (at places in that text), as its placement writes it.</summary>
    internal sealed record PlacedText(string Text, IReadOnlyList<FilledSpot> Spots);

    /// <summary>
    /// #1505 (area 08, D7): <paramref name="placed"/> escaped whole as plain text in Markdown, each run spot in it with its
    /// output (and a <c>text</c> spot's run-neutral output) escaped and its place moved to where it now stands.
    /// </summary>
    internal static PlacedText Escaped(PlacedText placed)
    {
        var text = placed.Text;
        var escaped = new StringBuilder(text.Length);
        var spots = new List<FilledSpot>(placed.Spots.Count);
        var at = 0;
        foreach (var spot in placed.Spots)
        {
            escaped.Append(MarkdownEscape.Escape(text[at..spot.Start]));
            var output = MarkdownEscape.Escape(spot.Output);
            spots.Add(spot with { Start = escaped.Length, Length = output.Length, Output = output, Neutral = spot.Spot.Text ? EscapedNeutral(spot.Neutral) : spot.Neutral });
            escaped.Append(output);
            at = spot.Start + spot.Length;
        }

        return new PlacedText(escaped.Append(MarkdownEscape.Escape(text[at..])).ToString(), spots);
    }

    /// <summary>
    /// #1505 (area 08, D7): a run-neutral output <paramref name="neutral"/> escaped as its sent output is, each run marker
    /// in it (<c>{{run:&lt;n&gt;}}</c>, standing for a run-dependent value) kept as it is, so the two texts still differ
    /// only at the markers.
    /// </summary>
    internal static string EscapedNeutral(string neutral)
    {
        const string open = "{{run:";
        var escaped = new StringBuilder(neutral.Length);
        var at = 0;
        for (var found = neutral.IndexOf(open, StringComparison.Ordinal); found >= 0; found = neutral.IndexOf(open, found + 1, StringComparison.Ordinal))
        {
            var digits = found + open.Length;
            var close = digits;
            while (close < neutral.Length && char.IsAsciiDigit(neutral[close]))
                close++;
            if (close == digits || string.CompareOrdinal(neutral, close, "}}", 0, 2) != 0)
                continue;
            escaped.Append(MarkdownEscape.Escape(neutral[at..found])).Append(neutral, found, close + 2 - found);
            at = close + 2;
            found = close + 1;
        }

        return escaped.Append(MarkdownEscape.Escape(neutral[at..])).ToString();
    }

    /// <summary>
    /// The run-neutral text of <paramref name="text"/>: each of <paramref name="spots"/> (in the order of the text, none
    /// overlapping) replaced at its recorded place by what stands for it; <c>null</c> when there is none (one hash).
    /// </summary>
    private static string? NeutralText(string text, IReadOnlyList<FilledSpot> spots)
    {
        if (spots.Count == 0)
            return null;
        var neutral = new StringBuilder(text.Length);
        var at = 0;
        foreach (var spot in spots)
        {
            neutral.Append(text, at, spot.Start - at).Append(spot.Neutral);
            at = spot.Start + spot.Length;
        }

        return neutral.Append(text, at, text.Length - at).ToString();
    }
}
