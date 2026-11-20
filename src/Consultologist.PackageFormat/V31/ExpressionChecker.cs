using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1501 (specVersion 31, area 04): what the checker found of one rule at one site. <paramref name="Type"/> is null
/// when there is a refusal; <paramref name="Reads"/> is every <c>var</c> and <c>missing</c> path
/// (<see cref="JsonLogicShape.Paths"/>); <paramref name="Printed"/> the rule as people see it
/// (<see cref="JsonLogicPrinter.Print"/>); <paramref name="PlainConverts"/> D14's list, for each <c>plain</c> in the
/// order <see cref="CorvusJsonLogicEvaluator"/> numbers them (a <c>plain</c> before those in its operand, operands left
/// to right, an <c>object</c>'s fields as written), whether it converts: <c>true</c> for a Markdown operand or one whose
/// type is not known, <c>false</c> for plain text or a string.
/// </summary>
public sealed record ExpressionCheck(
    IReadOnlyList<SourceRefusal> Refusals,
    ExpressionType? Type,
    IReadOnlyList<string> Reads,
    string Printed,
    IReadOnlyList<bool> PlainConverts);

/// <summary>
/// #1501 (specVersion 31, area 04): types one rule at one site (part 03 §§ 3, 4, 7, 11.1; D2 to D5, D9, D26), in order:
/// (1) the written form, each <see cref="ShapeFault"/> its sentence, stopping there when there is any; (2) every read,
/// in tree order, through the root and site rules of part 03, then the <see cref="IExpressionScope"/>, then
/// <see cref="FormatReads.Read"/>; (3) every operation's operands and result (<c>ExpressionChecker.Operations.cs</c>);
/// (4) the result against the site: a condition yields a boolean (S03-025), a computed node its declared format
/// (S03-026, D25). Never throws on any rule.
/// </summary>
public static partial class ExpressionChecker
{
    /// <summary>The roots JSON Logic reads (part 03 § 4.1).</summary>
    private static readonly HashSet<string> Roots = new(StringComparer.Ordinal) { "input", "node", "check", "item", "pass", "param", "run" };

    /// <summary>The roots that are never read by JSON Logic, S03-019's (part 03 § 4.1); <c>profile</c> has S03-018.</summary>
    private static readonly HashSet<string> Unreadable = new(StringComparer.Ordinal) { "snippet", "part", "macro", "renderer", "deliverable" };

    private static readonly ExpressionType Unknown = new(null, false);

    /// <summary>
    /// The check of <paramref name="rule"/> at <paramref name="site"/>. A <c>null</c> rule at a condition
    /// site (a site that is <c>null</c>: always) gives no refusal and the type <c>boolean</c>; at an
    /// <see cref="ExpressionSiteKind.Expression"/> it is the <c>null</c> literal, held to the declared format (D25).
    /// </summary>
    public static ExpressionCheck Check(JsonNode? rule, ExpressionSite site, IExpressionScope scope, FormatSet formats)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(formats);

        var printed = JsonLogicPrinter.Print(rule);
        var paths = JsonLogicShape.Paths(rule);
        if (rule is null && site.Kind != ExpressionSiteKind.Expression)
            return new ExpressionCheck([], new ExpressionType(JsonValue.Create("boolean"), false), paths, printed, []);

        // (1) The written form.
        var faults = JsonLogicShape.Check(rule);
        if (faults.Count > 0)
            return new ExpressionCheck(faults.Select(f => ExpressionSentences.Shape(site, f, rule)).ToList(), null, paths, printed, []);

        // (2) The reads, each said once, in tree order.
        var context = new Context(site, scope, formats);
        foreach (var path in paths)
        {
            var said = context.Refusals.Count;
            context.Reads[path] = context.Read(path);
            if (context.Refusals.Count > said)
                context.RefusedReads.Add(path);
        }

        // (3) The operations; a computed node's format is the target its value is built for (D27).
        var type = TypeOf(rule, context, TopTarget(site, formats));

        // (4) The result.
        if (context.Refusals.Count == 0 && site.Kind != ExpressionSiteKind.Expression && type.Format is not null
            && FormatTypes.Shape(type.Format, formats) != FormatShape.Boolean && !IsMissing(rule))
        {
            context.Refusals.Add(ExpressionSentences.NotABoolean(site, context.TypeString(type)));
        }

        // D25: a result that is only nullable (a null literal, an if of null branches) is held as null; one not known (D2) is not held.
        if (context.Refusals.Count == 0 && site.Kind == ExpressionSiteKind.Expression && site.Declared is not null
            && context.Effective(type) is { } yields && !FormatTypes.Fits(yields, site.Declared, formats))
        {
            context.Refusals.Add(ExpressionSentences.YieldsNotDeclared(site, context.TypeString(type), TypeStrings.Print(site.Declared, formats)));
        }

        return context.Refusals.Count > 0
            ? new ExpressionCheck(context.Refusals, null, paths, printed, context.PlainConverts)
            : new ExpressionCheck([], type, paths, printed, context.PlainConverts);
    }

    /// <summary>D26: a condition may be a <c>missing</c> operation itself.</summary>
    private static bool IsMissing(JsonNode? rule) => rule is JsonObject { Count: 1 } obj && obj.First().Key == "missing";

    /// <summary>
    /// The type of <paramref name="node"/>, a rule with no fault of its written form: a literal's, a read's, or its
    /// operation's. A string literal where <paramref name="target"/> is an enum it is a value of, a date it is a valid
    /// date of, or a text format, has the target's type (D24).
    /// </summary>
    private static ExpressionType TypeOf(JsonNode? node, Context context, Target target)
    {
        switch (node)
        {
            case null:
                return new ExpressionType(null, true);
            case JsonValue value:
                return value.GetValueKind() switch
                {
                    JsonValueKind.String when target.Format is not null && LiteralFits(JsonLogicShape.TextOf(value), target.Format, context.Formats) =>
                        new ExpressionType(target.Format.DeepClone(), false),
                    JsonValueKind.String => new ExpressionType(JsonValue.Create("string"), false),
                    JsonValueKind.True or JsonValueKind.False => new ExpressionType(JsonValue.Create("boolean"), false),
                    JsonValueKind.Number => new ExpressionType(
                        IsWhole(value) ? new JsonObject { ["type"] = "integer" } : JsonValue.Create("number"), false),
                    _ => Unknown,
                };
            case JsonArray array:
                return Operation(null, array, context, target);
            case JsonObject obj:
                var (name, operands) = obj.First();
                if (name == "var")
                    return JsonLogicShape.TextOf(operands) is { } path && context.Reads.TryGetValue(path, out var read) ? read : Unknown;
                return Operation(name, operands, context, target);
            default:
                return Unknown;
        }
    }

    private static bool IsWhole(JsonValue value) =>
        double.TryParse(value.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)
        && double.IsFinite(d) && d == Math.Floor(d);

    /// <summary>One check's state: the site, the scope, the formats, the reads typed so far and the refusals said.</summary>
    private sealed partial class Context(ExpressionSite site, IExpressionScope scope, FormatSet formats)
    {
        public ExpressionSite Site { get; } = site;

        public FormatSet Formats { get; } = formats;

        public List<SourceRefusal> Refusals { get; } = [];

        /// <summary>Each read path's type; a refused read's is not known, so one mistake gives one sentence.</summary>
        public Dictionary<string, ExpressionType> Reads { get; } = new(StringComparer.Ordinal);

        /// <summary>The read paths that were refused: an operation over one has no type, and says nothing more.</summary>
        public HashSet<string> RefusedReads { get; } = new(StringComparer.Ordinal);

        public List<bool> PlainConverts { get; } = [];

        /// <summary>
        /// D4's <c>&lt;format&gt;</c>: the type string, with <c> | null</c> when the type is nullable and its format is
        /// not; a type that is only nullable (a <c>null</c> literal) is <c>null</c>.
        /// </summary>
        public string TypeString(ExpressionType type)
        {
            if (type.Format is null && type.Nullable)
                return "null";
            var printed = TypeStrings.Print(type.Format, Formats);
            return type.Nullable && !FormatTypes.IsNullable(type.Format, Formats) ? printed + " | null" : printed;
        }

        /// <summary>The type of the read <paramref name="path"/> (a path of the written form); a refusal is added when there is one.</summary>
        public ExpressionType Read(string path)
        {
            var segments = path.Split('.');
            if (Rule(path, segments) is { } refusal)
            {
                Refusals.Add(refusal);
                return Unknown;
            }

            var head = scope.Head(segments) ?? new ScopeHead(ScopeHeadKind.Unknown, 0, null, false, null);
            switch (head.Kind)
            {
                case ScopeHeadKind.Deferred:
                    return Unknown;
                case ScopeHeadKind.Refused:
                    Refusals.Add(head.Refusal ?? ExpressionSentences.DoesNotExist(Site, path));
                    return Unknown;
                case ScopeHeadKind.Format or ScopeHeadKind.Value:
                    break;
                default:
                    Refusals.Add(ExpressionSentences.DoesNotExist(Site, path));
                    return Unknown;
            }

            var length = Math.Clamp(head.Length, 0, segments.Length);
            var rest = segments.Skip(length).ToList();
            List<string> steps = head.Kind == ScopeHeadKind.Value ? ["value", .. rest] : rest;
            var read = FormatReads.Read(head.Format, steps, Formats);
            switch (read.Fault)
            {
                case ReadFault.None:
                    return new ExpressionType(read.Format, read.Nullable || head.Nullable, read.Titles);
                case ReadFault.TitleOfPlainEnum:
                    Refusals.Add(ExpressionSentences.TitleOfPlainEnum(
                        Site, path.EndsWith(".title", StringComparison.Ordinal) ? path[..^".title".Length] : path,
                        TypeStrings.Print(Before(head.Format, steps, steps.Count - 1), Formats)));
                    return Unknown;
                case ReadFault.NotEveryMember:
                    Refusals.Add(NotEveryMember(segments, length, head, steps));
                    return Unknown;
                default:
                    Refusals.Add(ExpressionSentences.DoesNotExist(Site, path));
                    return Unknown;
            }
        }

        /// <summary>Part 03's root and site rules, judged before the scope is asked (D2), or null.</summary>
        private SourceRefusal? Rule(string path, string[] segments)
        {
            var root = segments[0];
            var second = segments.Length > 1 ? segments[1] : null;
            if (root == "profile" && second is not null)
                return ExpressionSentences.ReadsProfile(Site, path["profile.".Length..]);
            if (Unreadable.Contains(root) && second is not null)
                return ExpressionSentences.NotAJsonLogicRoot(Site, path);
            if (!Roots.Contains(root))
                return ExpressionSentences.DoesNotExist(Site, path);

            switch (Site.Kind)
            {
                case ExpressionSiteKind.DeliverableIf when root == "check" && second is not null:
                    return ExpressionSentences.DeliverableReadsCheck(Site, second);
                case ExpressionSiteKind.DeliverableIf when root == "node" && second is not null && ReadsContent(segments):
                    return ExpressionSentences.DeliverableReadsContent(Site, second);
                case ExpressionSiteKind.MacroIf when root == "param" && second is not null:
                    return ExpressionSentences.MacroReadsParam(Site, second);
            }

            if (root == "input" && segments.Length < 3)
                return second is null ? ExpressionSentences.DoesNotExist(Site, path) : ExpressionSentences.BareInput(Site, second);
            return null;
        }

        /// <summary>A node read of text: a <c>content</c> segment after the id, before any <c>value</c> (after which it is a field).</summary>
        private static bool ReadsContent(string[] segments)
        {
            foreach (var segment in segments.Skip(2))
            {
                if (segment == "value")
                    return false;
                if (segment == "content")
                    return true;
            }

            return false;
        }

        /// <summary>
        /// S03-024: <c>&lt;path&gt;</c> is the head (the written path before its <c>.value</c>), <c>&lt;field&gt;</c> the
        /// steps after <c>value</c>, <c>&lt;format&gt;</c> the union: the longest run of steps that reads.
        /// </summary>
        private SourceRefusal NotEveryMember(string[] segments, int length, ScopeHead head, List<string> steps)
        {
            var union = head.Format;
            for (var count = steps.Count - 1; count > 1; count--)
            {
                if (FormatReads.Read(head.Format, steps[..count], Formats) is { Fault: ReadFault.None } prefix)
                {
                    union = prefix.Format;
                    break;
                }
            }

            return ExpressionSentences.NotEveryMember(
                Site, string.Join('.', segments.Take(length)), string.Join('.', steps.Skip(1)), TypeStrings.Print(union, Formats));
        }

        /// <summary>The format the first <paramref name="count"/> steps read, <paramref name="format"/> itself for none.</summary>
        private JsonNode? Before(JsonNode? format, List<string> steps, int count) =>
            count <= 0 ? format : FormatReads.Read(format, steps[..count], Formats).Format;
    }
}
