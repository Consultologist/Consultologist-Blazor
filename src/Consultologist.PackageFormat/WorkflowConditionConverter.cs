using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Migration;

namespace Consultologist.PackageFormat;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 2.1): the converter from 30's Scriban conditions and <c>let</c>s to
/// canonical JSON Logic, the surface the migration and (at Milestone 41) the Editor share, so the two never disagree.
/// The text is parsed as 30 parses it (<see cref="WorkflowConditionAt25.TryParse"/>) and written row by row of § 2.2's
/// table (<c>V31.Migration.ConditionTree</c>); anything outside the table gives <see cref="ConversionResult.Logic"/>
/// <c>null</c> and one notice per reason. Pure and deterministic; never throws on package text.
/// </summary>
public static class WorkflowConditionConverter
{
    /// <summary>30's Scriban condition or <c>let</c> <paramref name="expression"/> → canonical JSON Logic over what <paramref name="scope"/> can read.</summary>
    public static ConversionResult FromScriban(string expression, ConversionScope scope)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(scope);

        var notices = new List<string>();
        var reads = new List<string>();
        var text = expression.Trim();

        if (text.Contains("{{", StringComparison.Ordinal) || text.Contains("{%", StringComparison.Ordinal))
        {
            notices.Add(MigrationSentences.NoJsonLogicForm(expression, "a template"));
            return new ConversionResult(null, null, reads, notices);
        }

        if (!WorkflowConditionAt25.TryParse(text, out var parsed, out var error) || parsed is null)
        {
            var construct = error switch
            {
                "is blank." => "nothing",
                "is not one expression; a condition is one boolean expression." => "more than one statement",
                _ => "text that does not parse as one expression",
            };
            notices.Add(MigrationSentences.NoJsonLogicForm(expression, construct));
            return new ConversionResult(null, null, reads, notices);
        }

        var logic = ConditionTree.Write(parsed, expression, scope, notices);
        if (notices.Count > 0)
        {
            return new ConversionResult(null, null, reads, notices);
        }

        CollectReads(logic, reads);
        return new ConversionResult(logic, null, reads, notices);
    }

    /// <summary>Each <c>var</c> path of <paramref name="node"/>, once, in written order.</summary>
    private static void CollectReads(JsonNode? node, List<string> reads)
    {
        switch (node)
        {
            case JsonObject obj when obj.Count == 1:
                var (name, operands) = obj.First();
                if (name == "var")
                {
                    if (operands is JsonValue path && path.TryGetValue<string>(out var read) && !reads.Contains(read, StringComparer.Ordinal))
                    {
                        reads.Add(read);
                    }
                }
                else if (name == "object" && operands is JsonObject fields)
                {
                    foreach (var (_, field) in fields)
                    {
                        CollectReads(field, reads);
                    }
                }
                else
                {
                    CollectReads(operands, reads);
                }

                break;
            case JsonArray array:
                foreach (var element in array)
                {
                    CollectReads(element, reads);
                }

                break;
        }
    }
}

/// <summary>
/// #1507 (part 10 § 2.1): what <see cref="WorkflowConditionConverter.FromScriban"/> gives: the canonical JSON Logic
/// (<c>null</c> when it can't convert), the type the checker gives it (<c>boolean</c> for a condition), what it reads
/// (<c>input.x.value</c>, <c>node.x.value</c>, <c>item</c>, <c>pass</c>, <c>run.x</c>, in written order) and one
/// sentence per reason it can't convert.
/// </summary>
public sealed record ConversionResult(JsonNode? Logic, string? ResultFormat, IReadOnlyList<string> Reads, IReadOnlyList<string> Notices);

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
