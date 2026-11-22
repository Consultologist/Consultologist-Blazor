using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Consultologist.PackageFormat.V31.Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 1.2 the <c>terms_subset</c> row; part 06 §§ 1, 1.1, 2; part 03 § 7's
/// migrated check): the checks pass. Each 30 <c>terms_subset</c> node (a format of the <c>computed</c> producer, which the
/// nodes pass wrote no file for) becomes <c>checks/&lt;id&gt;.md</c>, listed in <c>checks/index.json</c> in 30's order:
/// <c>kind: computed</c>, <c>producer: computed</c>, its label, <c>description: null</c>, <c>format: check_result</c>,
/// the flags (placeholders until Task 12), <c>if: null</c> (30 checks are not gated), and <c>expression</c> the operation
/// over its two operands, each read at its <c>.value</c>, with <c>failWith</c> the 30 sentence as a literal. Its body is
/// empty. Every reader of <c>node.&lt;id&gt;</c> already reads <c>check.&lt;id&gt;</c>: the nodes pass registered the read
/// (<see cref="Migrated.NodeReads"/>, <see cref="Migrated.Keys"/>), which the converter and the template rewriter follow.
/// </summary>
internal static partial class Migrate
{
    private static readonly Regex BareNodeRead = new(@"^node\.[A-Za-z0-9_]+$", RegexOptions.CultureInvariant);

    /// <summary>The checks pass, after the nodes pass (it reads the checks' 31 reads), before the deliverables pass.</summary>
    public static void Checks(Migrated m)
    {
        ArgumentNullException.ThrowIfNull(m);
        var tree = new NodeTree(m.Manifest30.Nodes ?? []);
        foreach (var id in tree.Top)
        {
            var node = tree.ById[id];
            if (!IsCheck(m, node))
                continue;

            var file = $"checks/{id}.md";
            var operation = node.Format == "terms_subset" ? "terms_subset" : node.Format!;
            m.Put(file, new JsonObject
            {
                ["kind"] = "computed",
                ["producer"] = "computed",
                ["label"] = node.Label,
                ["description"] = null,
                ["format"] = "check_result",
                ["reproducible"] = true,
                ["modelDerived"] = false,
                ["runDependent"] = false,
                ["profileCarrying"] = false,
                ["if"] = null,
                ["expression"] = new JsonObject
                {
                    [operation] = new JsonArray(
                        Operand(m, file, "of", node.With?.GetValueOrDefault("of"), node.Of),
                        Operand(m, file, "in", node.With?.GetValueOrDefault("in"), node.In),
                        FailWith(node)),
                },
            }, string.Empty);
            m.Items("checks").Add(id);

            // D16: a check is computed at 31, with no reply to judge; 30's onInvalid and failIfEmpty have no member there.
            if (node.OnInvalid is not null)
                m.Notice(MigrationSentences.MemberDropped(id, "onInvalid"));
            if (node.FailIfEmpty is not null)
                m.Notice(MigrationSentences.MemberDropped(id, "failIfEmpty"));
        }
    }

    /// <summary>
    /// One operand of a 30 check (<c>with.of</c>, <c>with.in</c>: a <c>node.&lt;id&gt;</c> reference, or the v12
    /// <c>node:&lt;id&gt;</c> members) as a 31 read: the node's <c>.value</c>, converted over the package's top level
    /// (<see cref="Condition"/>, a <c>concept_list</c> declared), so a read 31 cannot type stays 30's text with one hand edit
    /// on <c>expression</c>.
    /// </summary>
    private static JsonNode Operand(Migrated m, string file, string name, JsonNode? with, string? legacy)
    {
        var text = with is JsonValue value && value.TryGetValue<string>(out var written) ? written
            : legacy is not null ? "node." + legacy.Replace("node:", "", StringComparison.Ordinal)
            : with?.ToJsonString() ?? "null";
        if (BareNodeRead.IsMatch(text))
            text += ".value";
        return Condition(m, file, "expression", text, m.Scope((string?)null), ConversionKind.Value, JsonValue.Create("concept_list"));
    }

    /// <summary>The 30 check's <c>failWith</c> (part 06 § 2.2: a literal string), as 30 wrote it.</summary>
    private static JsonNode? FailWith(WorkflowNodeSpec node) =>
        node.With?.GetValueOrDefault("failWith")?.DeepClone() ?? (node.FailWith is { } sentence ? JsonValue.Create(sentence) : null);
}
