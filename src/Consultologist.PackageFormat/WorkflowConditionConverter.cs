using System.Globalization;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Migration;

namespace Consultologist.PackageFormat;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 §§ 2.1 to 2.3): the converter from 30's Scriban conditions and <c>let</c>s
/// to canonical JSON Logic, the surface the migration and (at Milestone 41) the Editor share, so the two never
/// disagree. The text is parsed as 30 parses it (<see cref="WorkflowConditionAt25.TryParse"/>), written row by row of
/// § 2.2's table (<c>V31.Migration.ConditionTree</c>), then typed as the 31 checker types it
/// (<c>V31.Migration.ConditionTyping</c>): <c>!x</c> on text becomes <c>blank(x)</c>, and a rule the checker refuses
/// is refused with the checker's own sentence. Anything outside the table, a template outside a string literal, and
/// a rule nested deeper than a 31 file holds (<see cref="V31.SourceBounds.JsonDepth"/>, counting the header that
/// holds it) give <see cref="ConversionResult.Logic"/> <c>null</c> and one S10-003 notice per reason. Pure and
/// deterministic; never throws on package text.
/// </summary>
public static class WorkflowConditionConverter
{
    /// <summary>
    /// 30's Scriban condition or <c>let</c> <paramref name="expression"/> → canonical JSON Logic over what
    /// <paramref name="scope"/> can read. A <see cref="ConversionKind.Condition"/> must yield a boolean (S03-025); a
    /// <see cref="ConversionKind.Value"/> (a <c>let</c>, a <c>case</c> subject) may yield any type, and is held to
    /// <paramref name="declared"/> when given (a <c>let</c>'s format at 31: S03-026; an <c>object</c> needs one, A04-008).
    /// </summary>
    public static ConversionResult FromScriban(string expression, ConversionScope scope, ConversionKind kind = ConversionKind.Condition, JsonNode? declared = null)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(scope);

        var text = expression.Trim();
        if (HasTemplate(text))
        {
            return Refused(MigrationSentences.NoJsonLogicForm(expression, "a template"));
        }

        if (!WorkflowConditionAt25.TryParse(text, out var parsed, out var error) || parsed is null)
        {
            var construct = error switch
            {
                "is blank." => "nothing",
                "is not one expression; a condition is one boolean expression." => "more than one statement",
                _ => "text that does not parse as one expression",
            };
            return Refused(MigrationSentences.NoJsonLogicForm(expression, construct));
        }

        var notices = new List<string>();
        var ids = new List<string>();
        var written = new Dictionary<JsonObject, string>(ReferenceEqualityComparer.Instance);
        var logic = ConditionTree.Write(parsed, expression, scope, notices, written, ids);
        if (notices.Count > 0)
        {
            return new ConversionResult(null, null, [], notices) { NoticeIds = ids };
        }

        // The rule sits in a header member: the header object is one level of the file (S01-041).
        if (ExpressionSentences.Depth(logic) + 1 > SourceBounds.JsonDepth)
        {
            return Refused(MigrationSentences.NoJsonLogicForm(
                expression, $"nesting deeper than {SourceBounds.JsonDepth.ToString(CultureInfo.InvariantCulture)} levels"));
        }

        var typed = ConditionTyping.Type(logic, expression, scope, kind, declared, written);
        if (typed.Refusals.Count > 0)
        {
            return new ConversionResult(null, null, [], [.. typed.Refusals.Select(r => r.Sentence)]) { NoticeIds = [.. typed.Refusals.Select(r => r.Id)] };
        }

        if (typed.Check.Refusals.Count > 0)
        {
            return new ConversionResult(null, null, [], [.. typed.Check.Refusals.Select(r => r.Sentence)])
            {
                NoticeIds = [.. typed.Check.Refusals.Select(r => r.Id)],
            };
        }

        return new ConversionResult(typed.Logic, ConditionTyping.Print(typed.Check.Type, scope.Formats), typed.Check.Reads, [])
        {
            BlankConversions = [.. typed.Blanks.SelectMany(b => b.Reads).Distinct(StringComparer.Ordinal)],
            Blanks = typed.Blanks,
            TrimBlanks = typed.TrimBlanks,
            TextLiterals = typed.TextLiterals,
            EmptyComparisons = typed.EmptyComparisons,
        };
    }

    private const string NoJsonLogicFormId = "S10-003";

    private static ConversionResult Refused(string notice) =>
        new(null, null, [], [notice]) { NoticeIds = [NoJsonLogicFormId] };

    /// <summary>
    /// Whether <paramref name="text"/> holds <c>{{</c> or <c>{%</c> outside a string literal (Scriban's <c>"…"</c> and
    /// <c>'…'</c> with backslash escapes, and verbatim <c>`…`</c>): a template, not an expression.
    /// </summary>
    private static bool HasTemplate(string text)
    {
        char? quote = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote is { } open)
            {
                if (c == '\\' && open != '`')
                {
                    i++;
                }
                else if (c == open)
                {
                    quote = null;
                }
            }
            else if (c is '"' or '\'' or '`')
            {
                quote = c;
            }
            else if (c == '{' && i + 1 < text.Length && text[i + 1] is '{' or '%')
            {
                return true;
            }
        }

        return false;
    }

}

/// <summary>
/// #1507 (part 10 § 2.1): what <see cref="WorkflowConditionConverter.FromScriban"/> gives: the canonical JSON Logic
/// (<c>null</c> when it can't convert), the type the checker gives it as <see cref="V31.TypeStrings"/> prints it
/// (<c>boolean</c> for a condition; <c>null</c> when it can't convert), what it reads as the checker lists it
/// (<c>input.x.value</c>, <c>node.x.value</c>, <c>item.id</c>, <c>pass.number</c>, <c>run.x</c>, each once, in written
/// order; none when it can't convert) and one sentence per reason it can't convert.
/// </summary>
public sealed record ConversionResult(JsonNode? Logic, string? ResultFormat, IReadOnlyList<string> Reads, IReadOnlyList<string> Notices)
{
    /// <summary>
    /// #1507 (§ 2.3, R1): the 31 paths read by the operands of the 30 <c>!x</c>s on text that became <c>blank(x)</c>
    /// (<c>input.reason.content</c>), each once, in written order: the reads whose empty or white-space-only value
    /// makes 31's answer differ from 30's.
    /// </summary>
    public IReadOnlyList<string> BlankConversions { get; init; } = [];

    /// <summary>#1507 (§ 2.3, R1): each <c>!x</c> on text that became <c>blank(x)</c>, in written order: what the migration lists (A10-008).</summary>
    public IReadOnlyList<BlankConversion> Blanks { get; init; } = [];

    /// <summary>
    /// #1507 (part 05 § 7; part 10 § 2.2's <c>string.strip</c> row): each <c>trim(x) == ""</c> (or <c>!=</c>) on a text
    /// written <c>blank(x)</c> (or <c>!blank(x)</c>) whose <c>x</c> can be null at 31 where 30 read text (a node that did
    /// not run, a field of an object input left out): <see cref="BlankConversion.Written"/> the 30 comparison as the printer shows it, <c>Printed</c> the 31 one.
    /// What the migration lists (A10-050); its answer for a null <c>x</c> differs from 30's, which no ruling covers
    /// (expected: #1508, comment 5, item 8). A text input read whole, which 30 read as <c>""</c> when not given, answers as 30's did
    /// and is not listed; a field of an object input is.
    /// </summary>
    public IReadOnlyList<BlankConversion> TrimBlanks { get; init; } = [];

    /// <summary>
    /// #1508 (comment 5, item 2): each text compared with a literal by <c>==</c> or <c>!=</c> (S03-002 at 31) written as
    /// membership, <c>{ "in": [x, ["lit"]] }</c> or its negation, which answers as 30's comparison did (a blank literal
    /// is the exception: <see cref="EmptyComparisons"/>, or a refusal):
    /// <see cref="BlankConversion.Written"/> the comparison as the printer shows it, <c>Printed</c> the 31 rule. What the
    /// migration says (A10-074).
    /// </summary>
    public IReadOnlyList<BlankConversion> TextLiterals { get; init; } = [];

    /// <summary>
    /// #1508 (comment 5, item 2; Task 30 fix round 1): each text input read whole compared with <c>""</c> written
    /// <c>x === null or x in [""]</c> (or <c>x !== null and not (x in [""])</c>): 30 read the input not given or blank as
    /// <c>""</c>, 31's start leaves it out and it reads null, so the null test answers as 30's comparison did. What the migration says (A10-075).
    /// </summary>
    public IReadOnlyList<BlankConversion> EmptyComparisons { get; init; } = [];

    /// <summary>The id of each notice's sentence, in the order of <see cref="Notices"/>: <c>S10-003</c>, or the 31 checker's own (<c>S03-008</c>, …).</summary>
    public IReadOnlyList<string> NoticeIds { get; init; } = [];
}

/// <summary>
/// #1507 (§ 2.3, R1): one 30 <c>!x</c> on text written <c>blank(x)</c>: <paramref name="Written"/> is <c>x</c> as the
/// author wrote it at 30 (<c>input.reason</c>, package text), <paramref name="Printed"/> the 31 operand as the printer
/// shows it (<c>input.reason.content</c>, <c>trim(input.reason.content)</c>), and <paramref name="Reads"/> the 31 paths it
/// reads. A10-008 says <c>'!&lt;Written&gt;' became blank(&lt;Printed&gt;)</c>.
/// </summary>
public sealed record BlankConversion(string Written, string Printed, IReadOnlyList<string> Reads);

/// <summary>#1507 (part 10 § 2.1): what a converted expression is at 31.</summary>
public enum ConversionKind
{
    /// <summary>A condition (<c>if</c>, <c>while</c>, <c>until</c>): it yields a boolean (S03-025).</summary>
    Condition,

    /// <summary>A <c>let</c>, or a <c>case</c>'s subject: a value of any type, or of its declared format.</summary>
    Value,
}

/// <summary>
/// #1507 (part 10 § 2.1): what an expression can read at its place, at 31: each input's 31 format (a name or a
/// schema), each 30 node in scope and where 31 reads it, the loop element's format (<c>null</c> outside a loop),
/// whether the item is a snippet folder's (<c>item.id</c> and <c>item.content</c>), whether <c>pass.number</c> is
/// readable (inside a <c>while</c>), and the package's formats. <see cref="RequiredInputs"/> names the inputs whose
/// <c>required</c> is <c>true</c>; every other input reads as nullable, as 31's own scope reads it (an unsupplied
/// optional input reads <c>null</c>, part 03 § 6.1).
/// </summary>
public sealed record ConversionScope(
    IReadOnlyDictionary<string, JsonNode> Inputs,
    IReadOnlyDictionary<string, ConversionRead> Nodes,
    JsonNode? Item,
    bool ItemIsFolderItem,
    bool InWhile,
    V31.FormatSet Formats)
{
    /// <summary>The ids of the inputs that are required; none by default, so every input is typed nullable unless named here.</summary>
    public IReadOnlySet<string> RequiredInputs { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}

/// <summary>
/// #1507 (part 10 § 2.1): where 31 reads a 30 node (a reference resolving outward by bare id: <c>node.outline</c>, <c>check.coverage</c>)
/// and its 31 format. <see cref="AlwaysValued"/> is set when the node has a value wherever the expression is judged
/// (it always completes before it: area 05's <c>AlwaysValued</c>); otherwise its reads are nullable (a node that did not
/// complete reads <c>null</c>, part 03 § 6.1).
/// </summary>
public sealed record ConversionRead(string Path, JsonNode Format)
{
    /// <summary>Whether the node always has a value here; false by default, so its reads are typed nullable.</summary>
    public bool AlwaysValued { get; init; }

    /// <summary>
    /// #1508 (R12): whether the node is a 30 classifier, whose 30 <c>.value</c> is its answer's text, read at 31 as
    /// <c>.value.value</c> whatever its format's name (<c>classification</c>, or the package format of its answers).
    /// </summary>
    public bool Classifier { get; init; }
}
