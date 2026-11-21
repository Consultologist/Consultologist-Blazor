using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1501 (specVersion 31, area 04): the third step of <see cref="ExpressionChecker.Check"/>, every operation's operands
/// and result typed against the formats (part 03 §§ 5, 7, 11.1; D24 to D27). An operand's <b>family</b> is its
/// <see cref="FormatShape"/> with <c>null</c> set aside; a string literal is a string, and a date when it is a valid one.
/// A type that is not known (D2), and a <c>null</c> literal, satisfy every operand rule (D25: nullable operands are not
/// refused at publish). An operation over a refused operand has no type and says nothing more, so one mistake gives one
/// sentence. Results are never nullable but an <c>if</c>'s with a nullable branch, and <c>lower</c>'s and <c>trim</c>'s
/// of a nullable operand (D25).
/// </summary>
public static partial class ExpressionChecker
{
    private static readonly JsonObject NullFormat = new() { ["type"] = "null" };

    /// <summary>An operand's family: its shape with <c>null</c> set aside; <see cref="Family.Free"/> satisfies every rule.</summary>
    private enum Family
    {
        Free,
        Text,
        String,
        Number,
        Boolean,
        Date,
        List,
        Object,
        Union,
    }

    /// <summary>
    /// Where a rule's value goes (D27): the <paramref name="Format"/> it is built for (null: none known), and the
    /// sentence an <c>object</c> there says when that format is no object (<paramref name="Misfit"/>, given the type
    /// string it yields; null when no format is expected there, A04-008). <paramref name="Quiet"/>: inside an
    /// <c>object</c> already refused, where nothing more is said of one.
    /// </summary>
    private sealed record Target(JsonNode? Format, Func<string, SourceRefusal>? Misfit, bool Quiet = false)
    {
        public static readonly Target None = new(null, null);

        public static readonly Target Silent = new(null, null, true);
    }

    /// <summary>One typed operand: its rule as written and its type; whether it is a <c>var</c>, a literal, a <c>missing</c>.</summary>
    private sealed record Operand(JsonNode? Rule, ExpressionType Type)
    {
        public string? Path => Rule is JsonObject { Count: 1 } obj && obj.First().Key == "var" ? JsonLogicShape.TextOf(obj["var"]) : null;

        public bool IsLiteral => Rule is null or JsonValue;

        public bool IsNull => Rule is null;

        public bool IsMissing => ExpressionChecker.IsMissing(Rule);

        /// <summary>A string literal still typed <c>string</c> (no target gave it an enum's, a date's or a text format's type).</summary>
        public string? StringLiteral => FormatSet.NameOf(Type.Format) == "string" ? JsonLogicShape.TextOf(Rule) : null;

        /// <summary>D4: a <c>var</c> shows its path; any other operand its printed text.</summary>
        public string Shown => Path ?? JsonLogicPrinter.Print(Rule);
    }

    /// <summary>The target at the top of a rule: a computed node's declared format (S03-026 for an <c>object</c> that is none), else none.</summary>
    private static Target TopTarget(ExpressionSite site, FormatSet formats) =>
        site.Kind == ExpressionSiteKind.Expression && site.Declared is { } declared
            ? new Target(declared, yields => ExpressionSentences.YieldsNotDeclared(site, yields, TypeStrings.Print(declared, formats)))
            : Target.None;

    /// <summary>
    /// The type of an operation (<paramref name="name"/>), or of an array of rules (<paramref name="name"/> null). Its
    /// operands are typed first, left to right (an <c>if</c>'s branches for <paramref name="target"/>), a <c>plain</c>
    /// taking its place in <see cref="Context.PlainConverts"/> before any inside it, as the evaluator numbers them.
    /// </summary>
    private static ExpressionType Operation(string? name, JsonNode? operands, Context context, Target target)
    {
        var said = context.Refusals.Count;
        if (name == "object")
            return BuildObject(operands as JsonObject ?? [], context, target);
        if (name == "missing")
            return new ExpressionType(FormatTypes.ListOf(JsonValue.Create("string")), false);

        var rules = operands as JsonArray ?? [];
        var plain = -1;
        if (name == "plain")
        {
            plain = context.PlainConverts.Count;
            context.PlainConverts.Add(true);
        }

        var operandTarget = name is null ? ElementTarget(target, context.Formats) : Target.None;
        var ops = new List<Operand>(rules.Count);
        for (var i = 0; i < rules.Count; i++)
        {
            var at = name == "if" && (i % 2 == 1 || i == rules.Count - 1) ? target : operandTarget;
            ops.Add(new Operand(rules[i], TypeOf(rules[i], context, at)));
        }

        if (plain >= 0 && ops.Count > 0)
            context.PlainConverts[plain] = Converts(ops[0], context);
        if (ops.Any(o => o.Path is { } path && context.RefusedReads.Contains(path)) || context.Refusals.Count > said)
            return Unknown;

        var type = name is null ? List(ops, context) : Judge(name, ops, context);
        return context.Refusals.Count > said ? Unknown : type;
    }

    /// <summary>The operand and result rules of each operation (the plan's Task 7 table), on operands already typed.</summary>
    private static ExpressionType Judge(string name, List<Operand> ops, Context c) => name switch
    {
        "if" => If(ops, c),
        "!" => Not(ops[0], c),
        "and" or "or" => Each(name, ops, c, o => c.Is(o, Family.Boolean), "booleans", Named("boolean")),
        "===" or "!==" => Equality(ops[0], ops[1], c),
        "<" or "<=" or ">" or ">=" => Ordering(ops[0], ops[1], c),
        "+" or "-" or "*" or "/" or "%" or "min" or "max" => Each(name, ops, c, o => c.Is(o, Family.Number), "numbers", Named("number")),
        "in" => In(ops[0], ops[1], c),
        "cat" => Cat(ops, c),
        "size" => Each(name, ops, c, o => c.TextLike(o) || c.Is(o, Family.List), "text or a list", Integer()),
        "blank" or "starts_with" or "ends_with" => Each(name, ops, c, c.TextLike, "text", Named("boolean")),
        "words" or "lines" => Each(name, ops, c, c.TextLike, "text", Integer()),
        "lower" or "trim" => LowerOrTrim(name, ops[0], c),
        "plain" => Each(name, ops, c, c.TextLike, "text", Named("plaintext")),
        "matches" or "has_word" => Each(name, ops.Take(1), c, c.TextLike, "text", Named("boolean")),
        "terms_subset" => Each(name, ops.Take(2), c, o => c.FitsAsideNull(o.Type.Format, JsonValue.Create("concept_list")), "two concept_list values", Named("check_result")),
        "all_pass" or "count_pass" => Each(name, ops.Take(1), c, c.ListOfCheckResults, "a list of check results", Named("check_result")),
        "days_between" or "years_between" => Each(name, ops, c, o => c.Is(o, Family.Date), "dates", Integer()),
        "add_days" => AddDays(ops[0], ops[1], c),
        _ => Unknown,
    };

    private static ExpressionType Named(string name) => new(JsonValue.Create(name), false);

    private static ExpressionType Integer() => new(new JsonObject { ["type"] = "integer" }, false);

    /// <summary>A04-001 for each operand of a known type that <paramref name="takes"/> refuses; the result otherwise.</summary>
    private static ExpressionType Each(string name, IEnumerable<Operand> ops, Context c, Func<Operand, bool> takes, string expected, ExpressionType result)
    {
        foreach (var o in ops.Where(o => c.FamilyOf(o) != Family.Free && !takes(o)))
            c.Refusals.Add(ExpressionSentences.OperandNotTaken(c.Site, name, o.Shown, c.TypeString(o.Type), expected));
        return result;
    }

    /// <summary><c>!</c>: a boolean (S03-009).</summary>
    private static ExpressionType Not(Operand o, Context c)
    {
        if (c.FamilyOf(o) != Family.Free && !c.Is(o, Family.Boolean))
            c.Refusals.Add(ExpressionSentences.NotOnText(c.Site, o.Shown, c.TypeString(o.Type)));
        return Named("boolean");
    }

    /// <summary><c>if</c>: each condition a boolean or a <c>missing</c> (D26; A04-001), the branches of one type.</summary>
    private static ExpressionType If(List<Operand> ops, Context c)
    {
        for (var i = 0; i < ops.Count - 1; i += 2)
        {
            var o = ops[i];
            if (c.FamilyOf(o) != Family.Free && !c.Is(o, Family.Boolean) && !o.IsMissing)
                c.Refusals.Add(ExpressionSentences.OperandNotTaken(c.Site, "if", o.Shown, c.TypeString(o.Type), "a condition"));
        }

        return OneType(ops.Where((_, i) => i % 2 == 1 || i == ops.Count - 1).ToList(), "the branches of an if", c);
    }

    /// <summary>An array of rules: its elements of one type, by <c>if</c>'s rule (A04-003); <c>list&lt;T&gt;</c>, and not known when empty.</summary>
    private static ExpressionType List(List<Operand> ops, Context c)
    {
        var element = OneType(ops, "a list", c);
        if (element.Format is null)
            return Unknown;
        var item = element.Nullable ? c.WithNull(element.Format) : element.Format;
        return new ExpressionType(FormatTypes.ListOf(item), false);
    }

    /// <summary>
    /// The one type of <paramref name="ops"/> (part 03 § 7 "the branches must have one type"; D24): all
    /// <see cref="FormatTypes.Same"/>; whole and other numbers give <c>number</c>; a string literal beside an enum gives
    /// the enum (S03-023 when it is none of its values), beside a date <c>date</c> (when it is a valid one), beside text
    /// the text format; otherwise A04-003. Nullable when any is; not known when any is not known.
    /// </summary>
    private static ExpressionType OneType(List<Operand> ops, string where, Context c)
    {
        Operand? one = null;
        var literals = new List<Operand>();
        var nullable = ops.Any(o => o.Type.Nullable);
        var unknown = false;
        foreach (var o in ops)
        {
            if (c.FamilyOf(o) == Family.Free)
            {
                unknown |= !o.IsNull;
                continue;
            }

            if (one is null)
            {
                one = o;
                if (o.StringLiteral is not null)
                    literals.Add(o);
                continue;
            }

            var onlyLiterals = literals.Count > 0 && ReferenceEquals(literals[0], one);
            if (FormatTypes.Same(one.Type.Format, o.Type.Format, c.Formats))
            {
                if (o.StringLiteral is not null)
                    literals.Add(o);
                else if (onlyLiterals)
                    (one, literals) = (o, []);
                continue;
            }

            if (c.FamilyOf(one) == Family.Number && c.FamilyOf(o) == Family.Number)
            {
                (one, literals) = (new Operand(o.Rule, Named("number")), []);
                continue;
            }

            if (onlyLiterals || o.StringLiteral is not null)
            {
                var other = onlyLiterals ? o : one;
                var strings = onlyLiterals ? literals : [o];
                if (JsonLogicShape.TextOf(other.Rule) is not null)
                {
                    // A literal the target typed beside one it could not: both stay strings, and the result is held to the target (S03-026).
                    if (!onlyLiterals)
                        (one, literals) = (o, [o]);
                    continue;
                }

                var shape = FormatTypes.Shape(other.Type.Format, c.Formats);
                if (shape is FormatShape.Enum or FormatShape.Text
                    || (shape == FormatShape.Date && strings.All(s => JsonLogicFunctions.Date(s.Rule) is not null)))
                {
                    if (shape == FormatShape.Enum && c.Values(other.Type.Format) is var values
                        && strings.FirstOrDefault(s => !values.Contains(s.StringLiteral!, StringComparer.Ordinal)) is { } stray)
                    {
                        c.Refusals.Add(ExpressionSentences.NotAValue(c.Site, other.Shown, stray.StringLiteral!, string.Join(", ", values)));
                        return Unknown;
                    }

                    (one, literals) = (other, []);
                    continue;
                }
            }

            c.Refusals.Add(ExpressionSentences.Mixes(c.Site, c.TypeString(one.Type), c.TypeString(o.Type), where));
            return Unknown;
        }

        if (one is null || unknown)
            return new ExpressionType(null, nullable);
        return new ExpressionType(one.Type.Format, one.Type.Nullable || nullable);
    }

    /// <summary>
    /// <c>===</c>, <c>!==</c>: text on either side is S03-002 (the other a literal) or S03-003; a list, an object, a
    /// union or two families is A04-002; a string literal is held to an enum's values (S03-023) and a <c>.title</c>'s
    /// titles (S03-017).
    /// </summary>
    private static ExpressionType Equality(Operand a, Operand b, Context c)
    {
        var boolean = Named("boolean");
        var (fa, fb) = (c.FamilyOf(a), c.FamilyOf(b));
        if (fa == Family.Free || fb == Family.Free)
            return boolean;

        if (fa == Family.Text || fb == Family.Text)
        {
            var (text, other) = fa == Family.Text ? (a, b) : (b, a);
            c.Refusals.Add(other.IsLiteral
                ? ExpressionSentences.ComparesTextWithLiteral(c.Site, text.Shown, c.TypeString(text.Type))
                : ExpressionSentences.ComparesTextWithText(c.Site, a.Shown, c.TypeString(a.Type), b.Shown, c.TypeString(b.Type)));
            return boolean;
        }

        if (fa == Family.Date && c.Is(b, Family.Date))
            fb = Family.Date;
        if (fb == Family.Date && c.Is(a, Family.Date))
            fa = Family.Date;
        if (fa != fb || fa is Family.List or Family.Object or Family.Union)
        {
            c.Refusals.Add(ExpressionSentences.ComparesTwoTypes(c.Site, a.Shown, c.TypeString(a.Type), b.Shown, c.TypeString(b.Type)));
            return boolean;
        }

        if (fa == Family.String)
        {
            HeldToValues(a, b, c);
            HeldToValues(b, a, c);
        }

        return boolean;
    }

    /// <summary>S03-023 when <paramref name="literal"/> is a string literal none of <paramref name="read"/>'s enum values; S03-017 none of its titles.</summary>
    private static void HeldToValues(Operand read, Operand literal, Context c)
    {
        if (literal.StringLiteral is not { } text)
            return;
        if (read.Type.Titles is { } titles)
        {
            if (!titles.Contains(text, StringComparer.Ordinal))
            {
                var shown = read.Shown.EndsWith(".title", StringComparison.Ordinal) ? read.Shown[..^".title".Length] : read.Shown;
                c.Refusals.Add(ExpressionSentences.NotATitle(c.Site, shown, text, string.Join(", ", titles)));
            }
        }
        else if (FormatTypes.Shape(read.Type.Format, c.Formats) == FormatShape.Enum && c.Values(read.Type.Format) is var values
            && !values.Contains(text, StringComparer.Ordinal))
        {
            c.Refusals.Add(ExpressionSentences.NotAValue(c.Site, read.Shown, text, string.Join(", ", values)));
        }
    }

    /// <summary><c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c>, <c>&gt;=</c>: two numbers or two dates; S03-008 on the first that is neither, or the second of a number and a date.</summary>
    private static ExpressionType Ordering(Operand a, Operand b, Context c)
    {
        var ordered = (Operand o) => c.FamilyOf(o) == Family.Free || c.Is(o, Family.Number) || c.Is(o, Family.Date);
        var wrong = !ordered(a) ? a
            : !ordered(b) ? b
            : c.FamilyOf(a) != Family.Free && c.FamilyOf(b) != Family.Free
              && !(c.Is(a, Family.Number) && c.Is(b, Family.Number)) && !(c.Is(a, Family.Date) && c.Is(b, Family.Date)) ? b
            : null;
        if (wrong is not null)
            c.Refusals.Add(ExpressionSentences.Orders(c.Site, wrong.Shown, c.TypeString(wrong.Type)));
        return Named("boolean");
    }

    /// <summary>
    /// <c>in</c>: a list and a needle of its element's family (a literal list's strings, or a literal needle, held to an
    /// enum's values), or text and a string to find; A04-001 otherwise.
    /// </summary>
    private static ExpressionType In(Operand needle, Operand haystack, Context c)
    {
        var boolean = Named("boolean");
        if (c.FamilyOf(haystack) == Family.Free)
            return boolean;

        if (c.Is(haystack, Family.List))
        {
            var items = ItemsOf(haystack.Type.Format, c.Formats);
            var element = c.FamilyOf(new Operand(null, new ExpressionType(items, false)));

            // A literal list of valid dates is a list of dates to a date needle, as a date literal is to = (D24).
            if (element == Family.String && c.FamilyOf(needle) == Family.Date && haystack.Rule is JsonArray dates
                && dates.All(rule => rule is null || JsonLogicFunctions.Date(rule) is not null))
            {
                element = Family.Date;
            }
            if (c.FamilyOf(needle) != Family.Free && element != Family.Free
                && (element is not (Family.String or Family.Number or Family.Boolean or Family.Date) || !c.Is(needle, element)))
            {
                c.Refusals.Add(ExpressionSentences.OperandNotTaken(c.Site, "in", needle.Shown, c.TypeString(needle.Type), "a list of its type, or text"));
                return boolean;
            }

            if (haystack.Rule is JsonArray written)
            {
                foreach (var literal in written.Where(rule => JsonLogicShape.TextOf(rule) is not null))
                    HeldToValues(needle, new Operand(literal, Named("string")), c);
            }
            else if (items is not null)
            {
                HeldToValues(new Operand(haystack.Rule, new ExpressionType(items, false)), needle, c);
            }

            return boolean;
        }

        if (c.TextLike(haystack))
        {
            if (c.FamilyOf(needle) is not (Family.Free or Family.String))
                c.Refusals.Add(ExpressionSentences.OperandNotTaken(c.Site, "in", needle.Shown, c.TypeString(needle.Type), "a string to find"));
            return boolean;
        }

        c.Refusals.Add(ExpressionSentences.OperandNotTaken(c.Site, "in", haystack.Shown, c.TypeString(haystack.Type), "a list of its type, or text"));
        return boolean;
    }

    /// <summary>
    /// <c>cat</c>: strings, numbers and plain text (Markdown is S03-035, anything else A04-001); a date is a string here
    /// (part 03 § 5.5: a <c>string</c> with <c>format: date</c>). <c>string</c> when every operand is a string, a date
    /// or a <c>null</c> literal (D24), not known when the rest are such and one is not known, <c>plaintext</c> otherwise.
    /// </summary>
    private static ExpressionType Cat(List<Operand> ops, Context c)
    {
        foreach (var o in ops)
        {
            switch (c.FamilyOf(o))
            {
                case Family.Free or Family.String or Family.Date or Family.Number:
                    break;
                case Family.Text when FormatTypes.KindOf(c.Core(o.Type.Format)) == "plaintext":
                    break;
                case Family.Text when FormatTypes.KindOf(c.Core(o.Type.Format)) is "markdown_block" or "markdown_inline":
                    c.Refusals.Add(ExpressionSentences.CatsMarkdown(c.Site, o.Shown, c.TypeString(o.Type)));
                    break;
                default:
                    c.Refusals.Add(ExpressionSentences.OperandNotTaken(c.Site, "cat", o.Shown, c.TypeString(o.Type), "strings, numbers and plain text"));
                    break;
            }
        }

        if (ops.Any(o => c.FamilyOf(o) is not (Family.Free or Family.String or Family.Date)))
            return Named("plaintext");
        return ops.Any(o => c.FamilyOf(o) == Family.Free && !o.IsNull) ? Unknown : Named("string");
    }

    /// <summary>
    /// <c>lower</c>, <c>trim</c>: text-like; the operand's format as written (an enum gives <c>string</c>). Never nullable
    /// for typing, whatever the operand (D25): like every operation's result, it can be <c>null</c> only at run time.
    /// </summary>
    private static ExpressionType LowerOrTrim(string name, Operand o, Context c)
    {
        if (c.FamilyOf(o) == Family.Free)
            return Unknown;
        if (!c.TextLike(o))
            return Each(name, [o], c, _ => false, "text", Unknown);
        var format = FormatTypes.Shape(o.Type.Format, c.Formats) == FormatShape.Enum ? JsonValue.Create("string") : o.Type.Format;
        return new ExpressionType(format, false);
    }

    /// <summary><c>add_days</c>: a date and a number (A04-001 for each that is not); <c>date</c>.</summary>
    private static ExpressionType AddDays(Operand date, Operand days, Context c)
    {
        foreach (var (o, family) in new[] { (date, Family.Date), (days, Family.Number) })
        {
            if (c.FamilyOf(o) != Family.Free && !c.Is(o, family))
                c.Refusals.Add(ExpressionSentences.OperandNotTaken(c.Site, "add_days", o.Shown, c.TypeString(o.Type), "a date and a number"));
        }

        return Named("date");
    }

    /// <summary>D14: a <c>plain</c> converts a Markdown operand, and one whose type is not known; not plain text or a string.</summary>
    private static bool Converts(Operand o, Context c) =>
        c.FamilyOf(o) == Family.Free || FormatTypes.KindOf(c.Core(o.Type.Format)) is "markdown_block" or "markdown_inline";

    /// <summary>An array's elements: built for the target's <c>items</c> when it is a list; a misfit there is the list's.</summary>
    private static Target ElementTarget(Target target, FormatSet formats)
    {
        if (target.Quiet)
            return Target.Silent;
        if (target.Misfit is not { } misfit)
            return Target.None;
        var items = FormatTypes.Shape(target.Format, formats) == FormatShape.List ? ItemsOf(target.Format, formats) : null;
        return new Target(items, yields => misfit($"list<{yields}>"));
    }

    /// <summary>
    /// <c>object</c> (part 03 § 5.4; D27): only where a format is expected (A04-008), and that format an object
    /// (otherwise the target's misfit: S03-026 at the top). Each field is typed for its property: a property with no
    /// field is S03-027, a field with no property S03-028, a field that does not fit, or is nullable where the
    /// property is not, A04-009. The result is the target format.
    /// </summary>
    private static ExpressionType BuildObject(JsonObject fields, Context c, Target target)
    {
        var said = c.Refusals.Count;
        var schema = FormatTypes.Shape(target.Format, c.Formats) == FormatShape.Object ? c.Core(target.Format) : null;
        if (target.Quiet || target.Misfit is null || schema is null)
        {
            if (!target.Quiet)
                c.Refusals.Add(target.Misfit is { } misfit ? misfit("object") : ExpressionSentences.ObjectWithoutFormat(c.Site));
            foreach (var (_, rule) in fields)
                TypeOf(rule, c, Target.Silent);
            return Unknown;
        }

        var built = TypeStrings.Print(target.Format, c.Formats);
        var properties = PropertiesOf(target.Format, c.Formats);
        foreach (var (field, rule) in fields)
        {
            if (!properties.TryGetValue(field, out var property))
            {
                TypeOf(rule, c, Target.Silent);
                c.Refusals.Add(ExpressionSentences.BuildsUndeclared(c.Site, built, field));
                continue;
            }

            var declared = TypeStrings.Print(property, c.Formats);
            var before = c.Refusals.Count;
            var type = TypeOf(rule, c, new Target(property, yields => ExpressionSentences.BuildsFieldAs(c.Site, built, field, yields, declared)));
            if (c.Refusals.Count == before && c.Effective(type) is { } given && !FormatTypes.Fits(given, property, c.Formats))
                c.Refusals.Add(ExpressionSentences.BuildsFieldAs(c.Site, built, field, c.TypeString(type), declared));
        }

        foreach (var property in properties.Keys.Where(p => !fields.ContainsKey(p)))
            c.Refusals.Add(ExpressionSentences.BuildsWithout(c.Site, built, property));

        return c.Refusals.Count > said ? Unknown : new ExpressionType(target.Format!.DeepClone(), false);
    }

    /// <summary>D24: a string literal fits an enum it is a value of, a <c>date</c> it is a valid date of, and a text format.</summary>
    private static bool LiteralFits(string? text, JsonNode format, FormatSet formats) => text is not null && FormatTypes.Shape(format, formats) switch
    {
        FormatShape.Enum => EnumValues(Core(format, formats)).Contains(text, StringComparer.Ordinal),
        FormatShape.Date => JsonLogicFunctions.Date(JsonValue.Create(text)) is not null,
        FormatShape.Text => true,
        _ => false,
    };

    /// <summary>
    /// The schema <paramref name="format"/> is, through names and <c>#/$defs</c>, with <c>null</c> set aside: an
    /// <c>anyOf</c> of one type and <c>null</c> gives that type's schema; null when there is no one schema.
    /// </summary>
    private static JsonObject? Core(JsonNode? format, FormatSet formats) => CoreWithDefs(format, formats).Schema;

    /// <summary>
    /// <see cref="Core"/>, with the <c>$defs</c> the schema's own <c>#/$defs</c> references read.
    /// #1502 (area 05): a loop's element is read the same way for its <c>itemLabel</c> (<see cref="SourceResolve"/>).
    /// </summary>
    internal static (JsonObject? Schema, JsonObject? Defs) CoreWithDefs(JsonNode? format, FormatSet formats)
    {
        var (schema, defs) = FormatTypes.Follow(format, FormatTypes.Defs(format), formats);
        if (schema?["anyOf"] is not JsonArray members)
            return (schema, defs);
        var others = members
            .Select(m => FormatTypes.Follow(m, defs, formats))
            .Where(m => m.Schema is null || FormatTypes.Text(m.Schema["type"]) != "null")
            .ToList();
        return others.Count == 1 ? others[0] : (null, null);
    }

    /// <summary>
    /// A list format's <c>items</c>, taken out so that it reads alone (its <c>#/$defs</c> carried, as <see cref="FormatReads"/> does); null when none.
    /// #1502 (area 05): a loop's element is read the same way (<see cref="ScopeTree"/>).
    /// </summary>
    internal static JsonNode? ItemsOf(JsonNode? format, FormatSet formats) =>
        CoreWithDefs(format, formats) is ({ } schema, var defs) && schema["items"] is { } items ? FormatReads.Carried(items, defs) : null;

    /// <summary>
    /// An object format's properties in written order, each taken out so that it reads alone (its <c>#/$defs</c> carried).
    /// #1502 (area 05): and a loop's element's, for its <c>itemLabel</c>.
    /// </summary>
    internal static OrderedDictionary<string, JsonNode?> PropertiesOf(JsonNode? format, FormatSet formats)
    {
        var properties = new OrderedDictionary<string, JsonNode?>(StringComparer.Ordinal);
        if (CoreWithDefs(format, formats) is ({ } schema, var defs) && schema["properties"] is JsonObject written)
        {
            foreach (var (name, property) in written)
                properties[name] = property is null ? null : FormatReads.Carried(property, defs);
        }

        return properties;
    }

    /// <summary>An enum's values, its <c>enum</c> or its <c>oneOf</c>'s <c>const</c>s, in order, <c>null</c> set aside.</summary>
    private static List<string> EnumValues(JsonObject? schema)
    {
        var values = schema?["enum"] as JsonArray
            ?? new JsonArray((schema?["oneOf"] as JsonArray ?? []).OfType<JsonObject>().Select(arm => arm["const"]?.DeepClone()).ToArray());
        return values.Select(JsonLogicShape.TextOf).OfType<string>().ToList();
    }

    /// <summary>The operand rules' view of the formats, on <see cref="Context"/>.</summary>
    private sealed partial class Context
    {
        public Family FamilyOf(Operand o) => o.Type.Format is null ? Family.Free : FormatTypes.Shape(o.Type.Format, Formats) switch
        {
            FormatShape.Text => Family.Text,
            FormatShape.String or FormatShape.Enum => Family.String,
            FormatShape.Number or FormatShape.Integer => Family.Number,
            FormatShape.Boolean => Family.Boolean,
            FormatShape.Date => Family.Date,
            FormatShape.List => Family.List,
            FormatShape.Object => Family.Object,
            FormatShape.Union => Family.Union,
            _ => Family.Free,
        };

        /// <summary>Whether <paramref name="o"/> is of <paramref name="family"/>; a string literal is a date too when it is a valid one.</summary>
        public bool Is(Operand o, Family family) =>
            FamilyOf(o) == family || (family == Family.Date && o.StringLiteral is not null && JsonLogicFunctions.Date(o.Rule) is not null);

        /// <summary>Text-like (part 03 § 5.2): a text format or a string.</summary>
        public bool TextLike(Operand o) => FamilyOf(o) is Family.Text or Family.String;

        public JsonObject? Core(JsonNode? format) => ExpressionChecker.Core(format, Formats);

        public List<string> Values(JsonNode? format) => EnumValues(Core(format));

        /// <summary><paramref name="format"/> with <c>null</c> admitted: itself when it already is nullable.</summary>
        public JsonNode WithNull(JsonNode format) =>
            FormatTypes.IsNullable(format, Formats) ? format : FormatTypes.UnionOf([format, NullFormat.DeepClone()], Formats)!;

        /// <summary>The format a type stands for under D25: <c>T | null</c> when it is nullable, <c>null</c> for a <c>null</c> literal, else null for not known.</summary>
        public JsonNode? Effective(ExpressionType type) =>
            type.Format is null ? (type.Nullable ? NullFormat.DeepClone() : null)
            : type.Nullable ? WithNull(type.Format) : type.Format;

        /// <summary>Whether <paramref name="format"/>, <c>null</c> set aside, fits <paramref name="expected"/> (D25: nullable operands are not refused).</summary>
        public bool FitsAsideNull(JsonNode? format, JsonNode expected) =>
            format is not null && FormatTypes.Fits(format, WithNull(expected), Formats);

        /// <summary><c>all_pass</c>'s and <c>count_pass</c>'s list: of <c>check_result</c>s, or of <c>.items</c> elements whose <c>value</c> is one (part 03 § 5.4).</summary>
        public bool ListOfCheckResults(Operand o)
        {
            if (FamilyOf(o) != Family.List || ItemsOf(o.Type.Format, Formats) is not { } items)
                return false;
            var checkResult = JsonValue.Create("check_result");
            if (FormatTypes.Fits(items, checkResult, Formats))
                return true;
            var properties = PropertiesOf(items, Formats);
            return properties.ContainsKey("state") && properties.GetValueOrDefault("value") is { } value && FitsAsideNull(value, checkResult);
        }
    }
}
