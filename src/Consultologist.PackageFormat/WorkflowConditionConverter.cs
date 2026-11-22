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
        var logic = ConditionTree.Write(parsed, expression, scope, notices);
        if (notices.Count > 0)
        {
            return new ConversionResult(null, null, [], notices) { NoticeIds = [.. notices.Select(_ => NoJsonLogicFormId)] };
        }

        // The rule sits in a header member: the header object is one level of the file (S01-041).
        if (Depth(logic, SourceBounds.JsonDepth) + 1 > SourceBounds.JsonDepth)
        {
            return Refused(MigrationSentences.NoJsonLogicForm(
                expression, $"nesting deeper than {SourceBounds.JsonDepth.ToString(CultureInfo.InvariantCulture)} levels"));
        }

        var typed = ConditionTyping.Type(logic, expression, scope, kind, declared);
        if (typed.Check.Refusals.Count > 0)
        {
            return new ConversionResult(null, null, [], [.. typed.Check.Refusals.Select(r => r.Sentence)])
            {
                NoticeIds = [.. typed.Check.Refusals.Select(r => r.Id)],
            };
        }

        return new ConversionResult(typed.Logic, ConditionTyping.Print(typed.Check.Type, scope.Formats), typed.Check.Reads, [])
        {
            BlankConversions = typed.BlankConversions,
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

    /// <summary>How many container levels <paramref name="node"/> holds, itself included, counted no further than one past <paramref name="bound"/>.</summary>
    private static int Depth(JsonNode? node, int bound)
    {
        if (bound < 0 || node is not JsonObject and not JsonArray)
        {
            return 0;
        }

        var deepest = 0;
        var children = node is JsonObject obj ? obj.Select(p => p.Value) : (JsonArray)node;
        foreach (var child in children)
        {
            deepest = Math.Max(deepest, Depth(child, bound - 1));
        }

        return deepest + 1;
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
    /// #1507 (§ 2.3, R1): each operand of a 30 <c>!x</c> on text that became <c>blank(x)</c>, as the printer shows it
    /// (<c>input.reason.content</c>), in written order: the conversions the migration lists (A10-008).
    /// </summary>
    public IReadOnlyList<string> BlankConversions { get; init; } = [];

    /// <summary>The id of each notice's sentence, in the order of <see cref="Notices"/>: <c>S10-003</c>, or the 31 checker's own (<c>S03-008</c>, …).</summary>
    public IReadOnlyList<string> NoticeIds { get; init; } = [];
}

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
/// readable (inside a <c>while</c>), and the package's formats.
/// </summary>
public sealed record ConversionScope(
    IReadOnlyDictionary<string, JsonNode> Inputs,
    IReadOnlyDictionary<string, ConversionRead> Nodes,
    JsonNode? Item,
    bool ItemIsFolderItem,
    bool InWhile,
    V31.FormatSet Formats);

/// <summary>#1507 (part 10 § 2.1): where 31 reads a 30 node (its rooted key: <c>node.sections.outline</c>, <c>check.coverage</c>) and its 31 format.</summary>
public sealed record ConversionRead(string Path, JsonNode Format);
