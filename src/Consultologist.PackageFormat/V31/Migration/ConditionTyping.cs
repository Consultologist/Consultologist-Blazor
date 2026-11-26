using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 2.3): the written JSON Logic of a 30 expression, typed as the 31 checker
/// will type it (<see cref="ExpressionChecker.Check"/>, part 03 §§ 5 to 7), over a <see cref="ConversionScope"/>.
/// <list type="bullet">
/// <item><c>!x</c> is decided by its operand's type first: on a text format it becomes <c>{ "blank": [x] }</c> (§ 2.3,
/// R1), and the operand is listed; on anything else it stays <c>!</c>, which the checker holds to a boolean
/// (S03-009).</item>
/// <item>#1508 (comment 5, item 2; 2026-11-26, item 6): text compared with a literal by <c>===</c> or <c>!==</c> (S03-002
/// as written) is membership in a list of that literal, <c>{ "in": [x, ["lit"]] }</c> or its negation, with each text read
/// of an input in <c>x</c> written <c>{ "trim": [read] }</c> (30 trimmed an input's text at its end; <c>trim</c> trims both),
/// and is listed; a literal that starts with white space compared with an input's text is refused (A10-085); with blank text
/// (<c>""</c> or white space), where 30 read an input's text not given or blank as <c>""</c> and 31 reads it as null or
/// as given, a text input read whole compared with <c>""</c> is <c>x === null or x in [""]</c> (listed), and any other comparison with
/// blank text that reads an input is refused (A10-076, a hand edit's reason); a comparison of the <c>size</c> of a list that can be null is written with the null test that
/// counts a null list 0, as 30 did.</item>
/// <item>A condition is checked at a site of kind <see cref="ExpressionSiteKind.NodeIf"/>, so it yields a boolean
/// (S03-025); a value (a <c>let</c>, a <c>case</c> subject) at an <see cref="ExpressionSiteKind.Expression"/> site,
/// held to its declared format when it has one (S03-026, D25).</item>
/// <item>The site's <c>&lt;site&gt;</c> is <c>The condition '&lt;expression&gt;'</c>, as S10-003 names the expression, or
/// <c>Computed node '&lt;expression&gt;'</c> for a value with a declared format (a <c>let</c>, a computed node at 31, whose
/// rows name one); its member is <c>if</c> for a condition and <c>expression</c> for a value. A refusal is the checker's
/// sentence, unchanged.</item>
/// </list>
/// Never throws on package text.
/// </summary>
internal static class ConditionTyping
{
    /// <summary>What typing found: the rule (with <c>blank</c> written for <c>!</c> on text), the check, and the <c>!x</c>s that became <c>blank</c>.</summary>
    public sealed record Typed(JsonNode? Logic, ExpressionCheck Check, IReadOnlyList<BlankConversion> Blanks)
    {
        /// <summary>Each <c>trim(x) == ""</c> written <c>blank(x)</c> whose answer can differ from 30's (<see cref="ConversionResult.TrimBlanks"/>).</summary>
        public IReadOnlyList<BlankConversion> TrimBlanks { get; init; } = [];

        /// <summary>Each text compared with a literal written as <c>in</c> (<see cref="ConversionResult.TextLiterals"/>).</summary>
        public IReadOnlyList<BlankConversion> TextLiterals { get; init; } = [];

        /// <summary>Each text input read whole compared with <c>""</c>, written with its null test (<see cref="ConversionResult.EmptyComparisons"/>).</summary>
        public IReadOnlyList<BlankConversion> EmptyComparisons { get; init; } = [];

        /// <summary>What the rewrite refuses, each as its id and sentence (A10-076), before the checker is asked.</summary>
        public IReadOnlyList<(string Id, string Sentence)> Refusals { get; init; } = [];
    }

    /// <summary>
    /// <paramref name="logic"/> (the tree's, owned by the caller and rewritten in place) typed at the site
    /// <paramref name="kind"/> gives, for the expression <paramref name="text"/> as written; <paramref name="nots"/> holds
    /// each written <c>!</c>'s operand as the author wrote it (the tree's).
    /// </summary>
    public static Typed Type(JsonNode? logic, string text, ConversionScope conversion, ConversionKind kind, JsonNode? declared, IReadOnlyDictionary<JsonObject, string> nots)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(conversion);

        var scope = new Scope(conversion);
        // A let is a computed node at 31 (part 02 § 11), whose sentences name one (S03-026 to S03-028, A04-009).
        var subject = kind == ConversionKind.Value && declared is not null ? $"Computed node '{text}'" : $"The condition '{text}'";
        var operand = new ExpressionSite(ExpressionSiteKind.Expression, subject, "expression", "", null, null);
        var site = kind == ConversionKind.Condition
            ? new ExpressionSite(ExpressionSiteKind.NodeIf, subject, "if", "", null, null)
            : new ExpressionSite(ExpressionSiteKind.Expression, subject, "expression", "", null, declared);

        var found = new Found(text);
        logic = Blank(logic, operand, scope, conversion.Formats, nots, found);
        return new Typed(logic, ExpressionChecker.Check(logic, site, scope, conversion.Formats), found.Blanks)
        {
            TrimBlanks = found.Trims,
            TextLiterals = found.Literals,
            EmptyComparisons = found.Empties,
            Refusals = found.Refusals,
        };
    }

    /// <summary>
    /// The type of <paramref name="logic"/> (a converted rule, or a part of one) at an expression site over
    /// <paramref name="conversion"/>, as the checker gives it: null when it does not type. What the migration asks of a
    /// <c>case</c>'s subject (its enum's values, whether it can be null: part 04 § 5.2) and of a <c>size</c>'s operand
    /// (whether the list can be null).
    /// </summary>
    public static ExpressionType? TypeOf(JsonNode? logic, ConversionScope conversion)
    {
        ArgumentNullException.ThrowIfNull(conversion);
        var site = new ExpressionSite(ExpressionSiteKind.Expression, "The read", "expression", "", null, null);
        var check = ExpressionChecker.Check(logic, site, new Scope(conversion), conversion.Formats);
        return check.Refusals.Count == 0 ? check.Type : null;
    }

    /// <summary>
    /// Each operand of a <c>size</c> in <paramref name="logic"/>, printed, whose list can be <c>null</c> over
    /// <paramref name="conversion"/> (a nullable format, an optional input, a node not always valued): there 31's
    /// <c>size</c> is <c>null</c> where 30 counted 0 (the Task 4 ruling), in written order, each once.
    /// </summary>
    public static IReadOnlyList<string> NullableSizes(JsonNode? logic, ConversionScope conversion)
    {
        ArgumentNullException.ThrowIfNull(conversion);
        var found = new List<string>();
        Walk(logic, 0);
        return found;

        void Walk(JsonNode? node, int depth)
        {
            if (depth > SourceBounds.JsonDepth)
                return;
            switch (node)
            {
                case JsonArray array:
                    foreach (var element in array)
                        Walk(element, depth + 1);
                    break;
                case JsonObject obj:
                    foreach (var (name, operands) in obj)
                    {
                        if (name == "size" && operands is JsonArray { Count: 1 } sized
                            && TypeOf(sized[0], conversion) is { } type
                            && (type.Nullable || FormatTypes.IsNullable(type.Format, conversion.Formats)))
                        {
                            var printed = JsonLogicPrinter.Print(sized[0]);
                            if (!found.Contains(printed, StringComparer.Ordinal))
                                found.Add(printed);
                        }

                        Walk(operands, depth + 1);
                    }

                    break;
            }
        }
    }

    /// <summary>The type as <see cref="TypeStrings"/> prints a format, with <c> | null</c> when it is nullable and its format is not; null when not known.</summary>
    public static string? Print(ExpressionType? type, FormatSet formats)
    {
        if (type?.Format is not { } format)
        {
            return null;
        }

        var printed = TypeStrings.Print(format, formats);
        return type.Nullable && !FormatTypes.IsNullable(format, formats) ? printed + " | null" : printed;
    }

    /// <summary>What the rewrite listed: the <c>!x</c>s that became <c>blank</c>, the trims compared with <c>""</c>, the texts compared with a literal.</summary>
    private sealed class Found(string text)
    {
        /// <summary>The expression as the author wrote it, which a refusal quotes.</summary>
        public string Text { get; } = text;

        public List<BlankConversion> Blanks { get; } = [];

        public List<BlankConversion> Trims { get; } = [];

        public List<BlankConversion> Literals { get; } = [];

        public List<BlankConversion> Empties { get; } = [];

        public List<(string Id, string Sentence)> Refusals { get; } = [];
    }

    /// <summary>
    /// <paramref name="node"/> with every <c>{ "!": [x] }</c> whose <c>x</c> is of a text format written
    /// <c>{ "blank": [x] }</c>, innermost first; each is added to <paramref name="found"/>, with <c>x</c> as written at 30
    /// (from <paramref name="nots"/>; its printed form when the tree did not say), printed at 31, and its reads. A trim
    /// compared with <c>""</c> (<see cref="TrimIsEmpty"/>), a text compared with a literal (<see cref="TextLiteral"/>)
    /// and a nullable list's size compared (<see cref="NullSafeSize"/>) are rewritten on the way.
    /// </summary>
    private static JsonNode? Blank(JsonNode? node, ExpressionSite operand, Scope scope, FormatSet formats, IReadOnlyDictionary<JsonObject, string> nots, Found found)
    {
        switch (node)
        {
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    var element = array[i];
                    var rewritten = Blank(element, operand, scope, formats, nots, found);
                    if (!ReferenceEquals(element, rewritten))
                    {
                        array[i] = rewritten;
                    }
                }

                return array;
            case JsonObject { Count: 1 } obj:
                var (name, operands) = obj.First();
                if (name == "var")
                {
                    return obj;
                }

                if (name == "object" && operands is JsonObject fields)
                {
                    foreach (var field in fields.Select(f => f.Key).ToList())
                    {
                        var value = fields[field];
                        var rewritten = Blank(value, operand, scope, formats, nots, found);
                        if (!ReferenceEquals(value, rewritten))
                        {
                            fields[field] = rewritten;
                        }
                    }

                    return obj;
                }

                Blank(operands, operand, scope, formats, nots, found);
                if (TrimIsEmpty(name, operands, operand, scope, formats, found.Trims) is { } blank)
                {
                    return blank;
                }

                if (TextLiteral(name, operands, operand, scope, formats, found) is { } member)
                {
                    return member;
                }

                if (NullSafeSize(obj, operand, scope, formats) is { } sized)
                {
                    return sized;
                }

                if (name == "!" && operands is JsonArray { Count: 1 } not
                    && ExpressionChecker.Check(not[0], operand, scope, formats) is { Refusals.Count: 0, Type.Format: { } format }
                    && FormatTypes.Shape(format, formats) == FormatShape.Text)
                {
                    var x = not[0];
                    not.RemoveAt(0);
                    var printed = JsonLogicPrinter.Print(x);
                    found.Blanks.Add(new BlankConversion(nots.GetValueOrDefault(obj, printed), printed, JsonLogicShape.Paths(x)));
                    return new JsonObject { ["blank"] = new JsonArray(x) };
                }

                return obj;
            default:
                return node;
        }
    }

    /// <summary>
    /// Part 05 § 7 (the migrated <c>test/general</c>'s part <c>if</c>; part 10 § 2.2's <c>string.strip</c> row): a
    /// <c>trim(x)</c> compared by <c>===</c> or <c>!==</c> with <c>""</c>, either side, for a text <c>x</c>, is
    /// <c>{ "blank": [x] }</c> or <c>{ "!": [{ "blank": [x] }] }</c>; null for anything else. 30 read a text input not given
    /// as <c>""</c> and a folder item's text is always valued, so those answer as 30 did; an <c>x</c> that can be null
    /// otherwise (a node that did not run, a field of an object input left out), which 30 compared as not empty, is listed
    /// in <paramref name="trims"/>: an expected difference (#1508, comment 5, item 8), said.
    /// </summary>
    private static JsonNode? TrimIsEmpty(string name, JsonNode? operands, ExpressionSite operand, Scope scope, FormatSet formats, List<BlankConversion> trims)
    {
        if (name is not ("===" or "!==") || operands is not JsonArray { Count: 2 } pair)
        {
            return null;
        }

        static bool IsEmpty(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) && text.Length == 0;
        var trim = IsEmpty(pair[1]) ? pair[0] : IsEmpty(pair[0]) ? pair[1] : null;
        if (trim is not JsonObject { Count: 1 } call || call["trim"] is not JsonArray { Count: 1 } trimmed
            || ExpressionChecker.Check(trimmed[0], operand, scope, formats) is not { Refusals.Count: 0, Type: { Format: { } format } type }
            || FormatTypes.Shape(format, formats) != FormatShape.Text)
        {
            return null;
        }

        var x = trimmed[0]!.DeepClone();
        var blank = new JsonObject { ["blank"] = new JsonArray(x) };
        JsonNode result = name == "===" ? blank : new JsonObject { ["!"] = new JsonArray(blank) };
        var reads = JsonLogicShape.Paths(x);
        if ((type.Nullable || FormatTypes.IsNullable(format, formats)) && !IsTextInputReadWhole(x))
        {
            trims.Add(new BlankConversion(JsonLogicPrinter.Print(new JsonObject { [name] = pair.DeepClone() }), JsonLogicPrinter.Print(result), reads));
        }

        return result;
    }

    /// <summary>
    /// #1508 (comment 5, item 2; 2026-11-26, item 6): a text <c>x</c> compared by <c>===</c> or <c>!==</c> with a string
    /// literal, either side (S03-002 as written), is <c>{ "in": [x', ["lit"]] }</c> or <c>{ "!": [{ "in": [x', ["lit"]] }] }</c>,
    /// listed in <paramref name="found"/>; null for anything else. <c>x'</c> is <c>x</c> with each text read of an input
    /// written <c>{ "trim": [read] }</c> (<see cref="TrimInputReads"/>): 30 compared an input's text with its end trimmed
    /// (<c>CanonicalText.Normalize</c>), and part 03 § 5.2's <c>trim</c>, the one trimming operation 31's JSON Logic has,
    /// trims both ends, so an input's text that starts with white space now matches where 30's did not (said in A10-074).
    /// A node's or an item's text, which 30 compared as it was, is not trimmed. Membership is otherwise exact, as 30's
    /// comparison was, and a null <c>x</c> is in no list of text (<c>trim</c> of null is null), as 30's null equalled no
    /// text. A literal that starts with white space, compared with a trimmed read, could match at 30 and never matches at
    /// 31, so no trim fits: it is refused (A10-085, a hand edit's reason). Blank text is the exception (Task 30 fix round 1),
    /// written as before, with no trim: 30 read an
    /// input's text not given or blank (white space alone, a field's too) as <c>""</c>, where 31's start leaves out a text
    /// input absent or blank, so it reads null, and keeps a field as given. A text input read whole compared with
    /// <c>""</c> is <c>x === null or x in [""]</c> (<c>x !== null and not (x in [""])</c> for <c>!==</c>), the null test
    /// written as for a nullable list's size (<see cref="NullSafeSize"/>), which answers as 30 did (listed, A10-075); any
    /// other comparison with blank text that reads an input (<c>lower(input.x) == ""</c>, <c>input.x == " "</c>,
    /// <c>input.x.y == ""</c>) is refused (A10-076): the migration asks for a hand edit rather than a different answer.
    /// </summary>
    private static JsonNode? TextLiteral(string name, JsonNode? operands, ExpressionSite operand, Scope scope, FormatSet formats, Found found)
    {
        if (name is not ("===" or "!==") || operands is not JsonArray { Count: 2 } pair)
        {
            return null;
        }

        static bool IsLiteral(JsonNode? node) => node is JsonValue value && value.GetValueKind() == JsonValueKind.String;
        var (x, literal) = IsLiteral(pair[1]) && !IsLiteral(pair[0]) ? (pair[0], pair[1])
            : IsLiteral(pair[0]) && !IsLiteral(pair[1]) ? (pair[1], pair[0])
            : (null, null);
        if (x is null || ExpressionChecker.Check(x, operand, scope, formats) is not { Refusals.Count: 0, Type.Format: { } format }
            || FormatTypes.Shape(format, formats) != FormatShape.Text)
        {
            return null;
        }

        var written = JsonLogicPrinter.Print(new JsonObject { [name] = pair.DeepClone() });
        var reads = JsonLogicShape.Paths(x);
        var text = literal!.GetValue<string>();
        if (text.EnumerateRunes().All(Rune.IsWhiteSpace))
        {
            if (text.Length == 0 && IsTextInputReadWhole(x))
            {
                var inEmpty = new JsonObject { ["in"] = new JsonArray(x.DeepClone(), new JsonArray("")) };
                JsonNode empty = name == "==="
                    ? new JsonObject { ["or"] = new JsonArray(new JsonObject { ["==="] = new JsonArray(x.DeepClone(), null) }, inEmpty) }
                    : new JsonObject
                    {
                        ["and"] = new JsonArray(
                            new JsonObject { ["!=="] = new JsonArray(x.DeepClone(), null) }, new JsonObject { ["!"] = new JsonArray(inEmpty) }),
                    };
                found.Empties.Add(new BlankConversion(written, JsonLogicPrinter.Print(empty), reads));
                return empty;
            }

            if (reads.Any(IsInputRead))
            {
                found.Refusals.Add((BlankTextId, MigrationSentences.ComparedWithBlankText(found.Text, JsonLogicPrinter.Print(x))));
                return null;
            }
        }

        // #1508, 2026-11-26, item 6: 30 trimmed an input's text at its end before it compared; the migration writes trim on
        // each text read of an input, which trims both ends, so a literal that starts with white space has no trim that fits.
        var trimmed = TrimInputReads(x.DeepClone(), operand, scope, formats, 0, out var trims);
        if (trims && text.Length > 0 && Rune.IsWhiteSpace(Rune.GetRuneAt(text, 0)))
        {
            found.Refusals.Add((LeadingWhiteSpaceId, MigrationSentences.ComparedWithLeadingWhiteSpace(written)));
            return null;
        }

        var member = new JsonObject { ["in"] = new JsonArray(trimmed, new JsonArray(literal.DeepClone())) };
        JsonNode result = name == "===" ? member : new JsonObject { ["!"] = new JsonArray(member) };
        found.Literals.Add(new BlankConversion(written, JsonLogicPrinter.Print(result), reads));
        return result;
    }

    private const string BlankTextId = "A10-076";

    private const string LeadingWhiteSpaceId = "A10-085";

    /// <summary>
    /// #1508, 2026-11-26, item 6 (F-D10): <paramref name="node"/> (a copy, rewritten in place) with each read of an input
    /// (<see cref="IsInputRead"/>) whose type is of a text format written <c>{ "trim": [read] }</c>, the trim around the
    /// read itself where an operation stands over it (<c>lower(trim(input.x))</c>); <paramref name="trimmed"/> says whether
    /// any was. A node's or an item's text, which 30 did not trim, is left as read, and so is what the author already
    /// trimmed (<c>input.x | string.strip</c>, a <c>trim</c> as written), which answers as 30's did.
    /// </summary>
    private static JsonNode? TrimInputReads(JsonNode? node, ExpressionSite operand, Scope scope, FormatSet formats, int depth, out bool trimmed)
    {
        trimmed = false;
        if (depth > SourceBounds.JsonDepth)
        {
            return node;
        }

        switch (node)
        {
            case JsonObject { Count: 1 } written when written.ContainsKey("trim"):
                return written;
            case JsonObject { Count: 1 } read when read["var"] is JsonValue path && path.TryGetValue<string>(out var p):
                if (IsInputRead(p)
                    && ExpressionChecker.Check(read, operand, scope, formats) is { Refusals.Count: 0, Type.Format: { } format }
                    && FormatTypes.Shape(format, formats) == FormatShape.Text)
                {
                    trimmed = true;
                    return new JsonObject { ["trim"] = new JsonArray(read.DeepClone()) };
                }

                return read;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    var element = array[i];
                    var rewritten = TrimInputReads(element, operand, scope, formats, depth + 1, out var any);
                    trimmed |= any;
                    if (!ReferenceEquals(element, rewritten))
                    {
                        array[i] = rewritten;
                    }
                }

                return array;
            case JsonObject obj:
                foreach (var key in obj.Select(f => f.Key).ToList())
                {
                    var value = obj[key];
                    var rewritten = TrimInputReads(value, operand, scope, formats, depth + 1, out var any);
                    trimmed |= any;
                    if (!ReferenceEquals(value, rewritten))
                    {
                        obj[key] = rewritten;
                    }
                }

                return obj;
            default:
                return node;
        }
    }

    /// <summary>Whether <paramref name="path"/> reads an input (<c>input.&lt;id&gt;.content</c>, <c>.value</c> or a field of it), whose blank text 30 read as <c>""</c> and whose text 30 trimmed at its end.</summary>
    private static bool IsInputRead(string path) => path.Split('.') is ["input", { Length: > 0 }, "content" or "value", ..];

    /// <summary>The comparisons a <c>size</c> may be an operand of, each as it holds for two numbers.</summary>
    private static readonly Dictionary<string, Func<double, double, bool>> Comparisons = new(StringComparer.Ordinal)
    {
        ["==="] = (a, b) => a == b,
        ["!=="] = (a, b) => a != b,
        ["<"] = (a, b) => a < b,
        ["<="] = (a, b) => a <= b,
        [">"] = (a, b) => a > b,
        [">="] = (a, b) => a >= b,
    };

    /// <summary>
    /// #1508 (comment 5, item 2; revisited in #1533): <paramref name="comparison"/>, a comparison of <c>size(x)</c> for a
    /// list <c>x</c> that can be null, written so that a null <c>x</c> counts 0, as 30 read an absent list as <c>[]</c>
    /// (31's <c>size(null)</c> is null). Against a number literal: a comparison that holds at 0 is
    /// <c>x === null or &lt;comparison&gt;</c>, one that does not <c>x !== null and &lt;comparison&gt;</c>, and
    /// <c>!= 0</c> is written <c>&gt; 0</c>; against anything else, <c>if x === null then &lt;the comparison with 0&gt; else
    /// &lt;comparison&gt;</c>. Null for anything else (a <c>size</c> not compared keeps A10-027 alone).
    /// </summary>
    private static JsonNode? NullSafeSize(JsonObject comparison, ExpressionSite operand, Scope scope, FormatSet formats)
    {
        var (name, operands) = comparison.First();
        if (!Comparisons.TryGetValue(name, out var holds) || operands is not JsonArray { Count: 2 } pair)
        {
            return null;
        }

        bool NullableList(JsonNode? node) =>
            node is JsonObject { Count: 1 } call && call["size"] is JsonArray { Count: 1 } sized
            && ExpressionChecker.Check(sized[0], operand, scope, formats) is { Refusals.Count: 0, Type: { Format: { } format } type }
            && FormatTypes.Shape(format, formats) == FormatShape.List
            && (type.Nullable || FormatTypes.IsNullable(format, formats));

        var at = NullableList(pair[0]) ? 0 : NullableList(pair[1]) ? 1 : -1;
        if (at < 0)
        {
            return null;
        }

        var size = pair[at]!;
        var list = size["size"]![0]!;
        JsonObject Null(string op) => new() { [op] = new JsonArray(list.DeepClone(), null) };
        if (pair[1 - at] is JsonValue literal && literal.GetValueKind() == JsonValueKind.Number
            && double.TryParse(literal.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
        {
            var atZero = at == 0 ? holds(0, n) : holds(n, 0);
            JsonNode written = name == "!==" && n == 0
                ? new JsonObject { [">"] = new JsonArray(size.DeepClone(), 0) }
                : comparison.DeepClone();
            return atZero
                ? new JsonObject { ["or"] = new JsonArray(Null("==="), written) }
                : new JsonObject { ["and"] = new JsonArray(Null("!=="), written) };
        }

        var zero = (JsonArray)pair.DeepClone();
        zero[at] = 0;
        return new JsonObject { ["if"] = new JsonArray(Null("==="), new JsonObject { [name] = zero }, comparison.DeepClone()) };
    }

    /// <summary>
    /// Whether <paramref name="x"/> is a text input read whole (<c>{ "var": "input.&lt;id&gt;.content" }</c> or
    /// <c>.value</c>): the one read 30 gave <c>""</c> when not given (part 10 § 2.3). A field of an object input is not
    /// one: an unsupplied object input is an empty object at 30, so its field read null there.
    /// </summary>
    private static bool IsTextInputReadWhole(JsonNode x) =>
        x is JsonObject { Count: 1 } read && read["var"] is JsonValue path && path.TryGetValue<string>(out var text)
        && text.Split('.') is ["input", { Length: > 0 }, "content" or "value"];

    /// <summary>
    /// #1507 (part 10 § 2.1): who a converted rule reads, at 31, as <see cref="IExpressionScope"/> heads:
    /// <list type="bullet">
    /// <item><c>input.&lt;id&gt;</c>, each input of the scope, read through <c>.value</c>, <c>.content</c> or
    /// <c>.title</c> of its format, nullable unless it is in <see cref="ConversionScope.RequiredInputs"/>;</item>
    /// <item>each node at its <see cref="ConversionRead.Path"/> (the longest that starts the path), read the same way,
    /// nullable unless it is <see cref="ConversionRead.AlwaysValued"/>;</item>
    /// <item><c>item.id</c> a <c>string</c> inside a loop; <c>item.value</c> the loop's element when the scope has one;
    /// <c>item.content</c> a snippet folder item's <c>markdown_block</c> (as area 05's <c>ScopeTree</c> has them);</item>
    /// <item><c>pass.number</c> an integer inside a <c>while</c>; <c>run.&lt;fact&gt;</c> as <see cref="PackageScope"/> types it.</item>
    /// </list>
    /// Anything else names nothing (S03-014).
    /// </summary>
    private sealed class Scope(ConversionScope conversion) : IExpressionScope
    {
        private static readonly ScopeHead Unknown = new(ScopeHeadKind.Unknown, 0, null, false, null);

        public ScopeHead Head(IReadOnlyList<string> segments)
        {
            ArgumentNullException.ThrowIfNull(segments);
            if (Node(segments) is { } node)
            {
                return node;
            }

            var second = segments.Count > 1 ? segments[1] : null;
            switch (segments[0])
            {
                case "input" when second is not null && conversion.Inputs.TryGetValue(second, out var format):
                    // An input not required reads null when it is not supplied (part 03 § 6.1), as PackageScope types it.
                    return new ScopeHead(ScopeHeadKind.Format, 2, format, !conversion.RequiredInputs.Contains(second), null);
                case "item" when conversion.Item is not null || conversion.ItemIsFolderItem:
                    return second switch
                    {
                        "id" => Value(JsonValue.Create("string")),
                        "value" when conversion.Item is { } element => Value(element),
                        "content" when conversion.ItemIsFolderItem => Value(JsonValue.Create("markdown_block")),
                        _ => Unknown,
                    };
                case "pass" when conversion.InWhile && second == "number":
                    return Value(new JsonObject { ["type"] = "integer" });
                case "run" when second is not null && JsonLogicOperations.RunFacts.Contains(second, StringComparer.Ordinal):
                    return Value(JsonValue.Create(second == "date" ? "date" : "string"));
                default:
                    return Unknown;
            }
        }

        /// <summary>A two-segment head already a value of <paramref name="format"/>.</summary>
        private static ScopeHead Value(JsonNode format) => new(ScopeHeadKind.Value, 2, format, false, null);

        /// <summary>The node whose path is the longest that starts <paramref name="segments"/>, as a head of that many segments.</summary>
        private ScopeHead? Node(IReadOnlyList<string> segments)
        {
            ScopeHead? found = null;
            foreach (var read in conversion.Nodes.Values)
            {
                var path = read.Path.Split('.');
                if (path.Length <= segments.Count && (found is null || path.Length > found.Length)
                    && path.Select((s, i) => string.Equals(s, segments[i], StringComparison.Ordinal)).All(same => same))
                {
                    // A node that may not have completed reads null (part 03 § 6.1), as ScopeTree types it.
                    found = new ScopeHead(ScopeHeadKind.Format, path.Length, read.Format, !read.AlwaysValued, null);
                }
            }

            return found;
        }
    }
}
