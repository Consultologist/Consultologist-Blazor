using System.Globalization;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1505 (specVersion 31, area 08; part 06 § 6.4): what one output of a body writes: the tag's output, the chain's final
/// value as written (<paramref name="Expression"/>: the path or literal, a built-in step appended, the last renderer's
/// <c>renderer.&lt;id&gt;</c> after a renderer step), its <paramref name="Format"/> (<c>null</c> when it cannot be said) and
/// whether a renderer made it (<paramref name="Rendered"/>: its text is the renderer's format, never escaped, D7).
/// </summary>
public sealed record WrittenOutput(TemplateOutput Output, string Expression, JsonNode? Format, bool Rendered);

/// <summary>
/// #1505 (specVersion 31, area 08; part 06 § 6.4; D7, D8, D9): a deliverable's strict formats, held once the dialect has
/// typed its body and expanded the snippets it places. A <c>markdown_block</c> deliverable writes no object or list
/// (S06-021, <c>Use a renderer or a node</c>), and each output of plain text, a string, a number or a boolean it writes,
/// in its body or in a Markdown snippet it places, is escaped, as is a plain-text snippet it places, once, whole (D7's
/// <c>escapes</c>). A <c>plaintext</c> deliverable writes no Markdown, read (S06-021, <c>Convert it in a node</c>; a
/// Markdown snippet it places too) or in its own text (S06-022, D9), and no object or list (S06-021). An object
/// deliverable's body is one tag writing <c>node.&lt;x&gt;.value</c> of its format and nothing else (S06-023, D8). A
/// deliverable whose <c>format</c> is none of these is S06-020's (<see cref="SourceDeliverables"/>) and is not judged
/// here. Never throws on package content.
/// </summary>
public static partial class SourceTemplates
{
    /// <summary>The first non-space characters of a line that make it Markdown in a <c>plaintext</c> deliverable (D9).</summary>
    private const string MarkdownStarts = "#-*>";

    /// <summary>How much of a line S06-022 quotes (D9).</summary>
    private const int QuotedLine = 40;

    private sealed partial class Context
    {
        /// <summary>
        /// <paramref name="body"/>, the body of <paramref name="deliverable"/> with its units, held to its format: its
        /// refusals added, and, for a <c>markdown_block</c> deliverable, its <see cref="CheckedBody.Escapes"/> set.
        /// </summary>
        private CheckedBody Deliverable(CheckedBody body, SourceDeliverable deliverable)
        {
            var formats = resolution.Tree.FormatsOf(body.Host.Frame);
            var format = deliverable.Header["format"];
            switch (SourceLayout.Text(format))
            {
                case "markdown_block":
                    Reads(body, deliverable, "markdown_block", formats);
                    var escapes = new List<(string? Unit, string At)>();
                    Escapes(body, null, formats, escapes, 0);
                    return body with { Escapes = escapes };
                case "plaintext":
                    Reads(body, deliverable, "plaintext", formats);
                    MarkdownLines(body.Syntax, deliverable);
                    return body;
                case var _ when FormatTypes.Shape(format, formats) == FormatShape.Object:
                    if (!OneValue(body, format, formats))
                        Refusals.Add(DeliverableSentences.IsObject(deliverable.Id, TypeStrings.Print(format, formats), deliverable.Path));
                    return body;
                default:
                    return body;
            }
        }

        /// <summary>
        /// S06-021 for each output of the deliverable's own text (a placement is the snippet's format) its format
        /// <paramref name="name"/> does not take: an object or a list in either, Markdown in <c>plaintext</c>.
        /// </summary>
        private void Reads(CheckedBody body, SourceDeliverable deliverable, string name, FormatSet formats)
        {
            foreach (var write in body.Writes)
            {
                if (write.Format is not { } format)
                    continue;
                var fix = FormatTypes.Shape(format, formats) is FormatShape.Object or FormatShape.List ? "Use a renderer or a node"
                    : name == "plaintext" && FormatTypes.TextKind(format, formats) is "markdown_block" or "markdown_inline" ? "Convert it in a node"
                    : null;
                if (fix is not null)
                {
                    Refusals.Add(DeliverableSentences.ReadsFormat(
                        deliverable.Id, name, write.Expression, TypeStrings.Print(format, formats), write.Output.Tag.Line, fix, deliverable.Path));
                }
            }
        }

        /// <summary>
        /// D7: the outputs of <paramref name="body"/> (whose unit is <paramref name="self"/>, <c>null</c> for the
        /// deliverable's own text) that are escaped, in order, each Markdown snippet it places walked where it is placed
        /// and each plain-text one escaped at its placement, whole. Bounded as the units are (the snippet chain's limit).
        /// </summary>
        private static void Escapes(CheckedBody body, string? self, FormatSet formats, List<(string? Unit, string At)> escapes, int depth)
        {
            if (depth > DialectRules.NestingLimit)
                return;
            var direct = body.Units.Where(u => u.Within == self).ToList();
            int placement = 0, unit = 0;
            foreach (var write in body.Writes)
            {
                var output = write.Output;
                var at = string.Create(CultureInfo.InvariantCulture, $"{output.Tag.Line}:{output.Tag.Column}");
                if (placement < body.Placements.Count && body.Placements[placement] is { } placed
                    && output is { Pipes.Count: 0, Paths.Count: 1 } && output.Path == placed.Unit && output.Tag.Line == placed.Line)
                {
                    placement++;
                    var site = placed.Kind == "snippet" && placed.Unit.EndsWith(".content", StringComparison.Ordinal)
                        ? placed.Unit[..^".content".Length]
                        : placed.Unit;
                    var inner = unit < direct.Count && direct[unit].Site == site ? direct[unit++] : null;
                    switch (FormatTypes.TextKind(write.Format, formats))
                    {
                        case "plaintext":
                            escapes.Add((self, at));
                            break;
                        case "markdown_block" or "markdown_inline" when inner is not null:
                            Escapes(inner.Body, inner.Site, formats, escapes, depth + 1);
                            break;
                    }

                    continue;
                }

                if (!write.Rendered && write.Format is { } format && Escaped(format, formats))
                    escapes.Add((self, at));
            }
        }

        /// <summary>D7: whether a value of <paramref name="format"/> is escaped where it enters Markdown: plain text and data (a string, an enum, a date, a number, a boolean, a union of them); never Markdown, an object, a list or <c>null</c>.</summary>
        private static bool Escaped(JsonNode format, FormatSet formats) =>
            FormatTypes.Shape(format, formats) switch
            {
                FormatShape.Text => FormatTypes.TextKind(format, formats) == "plaintext",
                FormatShape.String or FormatShape.Enum or FormatShape.Date or FormatShape.Number or FormatShape.Integer
                    or FormatShape.Boolean or FormatShape.Union => true,
                _ => false,
            };

        /// <summary>
        /// D8: <paramref name="body"/> is exactly one output tag writing <c>node.&lt;x&gt;.value</c>, unpiped, whose format
        /// is <paramref name="format"/> by type equality; nothing before the tag, and at most a final line end after it.
        /// </summary>
        private static bool OneValue(CheckedBody body, JsonNode? format, FormatSet formats)
        {
            var syntax = body.Syntax;
            if (syntax.Tags.Count != 1 || syntax.Outputs.Count != 1 || body.Writes.Count != 1)
                return false;
            var output = syntax.Outputs[0];
            if (output is not { Pipes.Count: 0, Paths.Count: 1, Path: { } path } || path.Split('.') is not ["node", _, .., "value"])
                return false;
            var after = syntax.Text[Math.Clamp(output.Tag.End, 0, syntax.Text.Length)..];
            if (output.Tag.Start != 0 || after is not ("" or "\n" or "\r\n"))
                return false;
            return body.Writes[0].Format is { } written && FormatTypes.Same(written, format, formats);
        }

        /// <summary>
        /// S06-022 (D9) for each line of a <c>plaintext</c> deliverable's own text whose first non-space character, outside
        /// a tag, is <c>#</c>, <c>-</c>, <c>*</c> or <c>&gt;</c>, or that holds two <c>**</c> outside its tags (raw blocks
        /// and comments are text here, as they are no tags); quoted trimmed, cut to 40 characters.
        /// </summary>
        private void MarkdownLines(TemplateSyntax syntax, SourceDeliverable deliverable)
        {
            var text = syntax.Text;
            var inTag = new bool[text.Length];
            foreach (var tag in syntax.Tags)
            {
                for (var i = Math.Clamp(tag.Start, 0, text.Length); i < Math.Clamp(tag.End, 0, text.Length); i++)
                    inTag[i] = true;
            }

            var (start, line) = (0, 1);
            while (start <= text.Length)
            {
                var end = text.IndexOf('\n', start);
                if (end < 0)
                    end = text.Length;
                if (WritesMarkdown(text, inTag, start, end))
                {
                    var quoted = text[start..end].Trim();
                    Refusals.Add(DeliverableSentences.WritesMarkdown(
                        deliverable.Id, line, quoted.Length > QuotedLine ? quoted[..QuotedLine] : quoted, deliverable.Path));
                }

                (start, line) = (end + 1, line + 1);
            }
        }

        /// <summary>Whether the line <c>[start, end)</c> of <paramref name="text"/> writes Markdown by D9.</summary>
        private static bool WritesMarkdown(string text, bool[] inTag, int start, int end)
        {
            var first = start;
            while (first < end && text[first] is ' ' or '\t')
                first++;
            if (first < end && !inTag[first] && MarkdownStarts.Contains(text[first], StringComparison.Ordinal))
                return true;

            var pairs = 0;
            for (var i = start; i + 1 < end; i++)
            {
                if (!inTag[i] && !inTag[i + 1] && text[i] == '*' && text[i + 1] == '*')
                {
                    pairs++;
                    i++;
                }
            }

            return pairs >= 2;
        }
    }
}
