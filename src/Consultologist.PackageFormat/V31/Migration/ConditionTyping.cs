using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 2.3): the written JSON Logic of a 30 expression, typed as the 31 checker
/// will type it (<see cref="ExpressionChecker.Check"/>, part 03 §§ 5 to 7), over a <see cref="ConversionScope"/>.
/// <list type="bullet">
/// <item><c>!x</c> is decided by its operand's type first: on a text format it becomes <c>{ "blank": [x] }</c> (§ 2.3,
/// R1), and the operand is listed; on anything else it stays <c>!</c>, which the checker holds to a boolean
/// (S03-009).</item>
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

        var blanks = new List<BlankConversion>();
        var trims = new List<BlankConversion>();
        logic = Blank(logic, operand, scope, conversion.Formats, nots, blanks, trims);
        return new Typed(logic, ExpressionChecker.Check(logic, site, scope, conversion.Formats), blanks) { TrimBlanks = trims };
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

    /// <summary>
    /// <paramref name="node"/> with every <c>{ "!": [x] }</c> whose <c>x</c> is of a text format written
    /// <c>{ "blank": [x] }</c>, innermost first; each is added to <paramref name="blanks"/>, with <c>x</c> as written at 30
    /// (from <paramref name="nots"/>; its printed form when the tree did not say), printed at 31, and its reads.
    /// </summary>
    private static JsonNode? Blank(JsonNode? node, ExpressionSite operand, Scope scope, FormatSet formats, IReadOnlyDictionary<JsonObject, string> nots, List<BlankConversion> blanks, List<BlankConversion> trims)
    {
        switch (node)
        {
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    var element = array[i];
                    var rewritten = Blank(element, operand, scope, formats, nots, blanks, trims);
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
                        var rewritten = Blank(value, operand, scope, formats, nots, blanks, trims);
                        if (!ReferenceEquals(value, rewritten))
                        {
                            fields[field] = rewritten;
                        }
                    }

                    return obj;
                }

                Blank(operands, operand, scope, formats, nots, blanks, trims);
                if (TrimIsEmpty(name, operands, operand, scope, formats, trims) is { } blank)
                {
                    return blank;
                }

                if (name == "!" && operands is JsonArray { Count: 1 } not
                    && ExpressionChecker.Check(not[0], operand, scope, formats) is { Refusals.Count: 0, Type.Format: { } format }
                    && FormatTypes.Shape(format, formats) == FormatShape.Text)
                {
                    var x = not[0];
                    not.RemoveAt(0);
                    var printed = JsonLogicPrinter.Print(x);
                    blanks.Add(new BlankConversion(nots.GetValueOrDefault(obj, printed), printed, JsonLogicShape.Paths(x)));
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
    /// in <paramref name="trims"/>: an unruled difference, for the owner.
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
