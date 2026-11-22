using System.Text;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Templates;

namespace Consultologist.PackageFormat.V31.Run;

/// <summary>
/// #1506 (specVersion 31, area 09; part 08 §§ 4, 7, 8.2; D2): a row in part 08's shape, by its section. The runner fills a
/// row's <see cref="RunRow.Detail"/> with what only it knows (the entry's facts, the call's sub-rows, a loop's or a call's own
/// members); this writes every member part 08 names, in its order, each absent one with its default (<c>[]</c> for a list,
/// <c>null</c> otherwise), then the detail's other members (a loop's, a while's, a pass's and a call's own) as they are.
/// Never a value from an input or a model: only what the detail and the row hold. A check's row is <see cref="Check"/>
/// (part 08 § 6), with a computed value's two hashes (<see cref="ComputedInputHash"/>, <see cref="ComputedNeutralHash"/>,
/// D9) used by computed node rows too; a deliverable's (Task 6) is added beside them.
/// </summary>
internal static class RecordRows
{
    /// <summary>Part 08 § 4's <c>hashVersion</c> of a 31 node row.</summary>
    public const int NodeHashVersion = AgentTurn.HashVersion;

    /// <summary>
    /// The members of a node row, in order: storage 11's, part 08 § 4's and area 06's (<c>function</c>, <c>depth</c>), each
    /// with its default when the detail does not give it.
    /// </summary>
    private static readonly (string Name, Func<JsonNode?> Default)[] NodeMembers =
    [
        ("label", () => null),
        ("producer", () => "computed"),
        ("inputHash", () => null),
        ("hashVersion", () => NodeHashVersion),
        ("tokens", () => null),
        ("formatHash", () => null),
        ("typeHash", () => null),
        ("sentFormatHash", () => null),
        ("notSent", () => new JsonArray()),
        ("agent", () => null),
        ("itemName", () => null),
        ("itemNameHash", () => null),
        ("positionName", () => false),
        ("runDependent", () => false),
        ("modelDerived", () => false),
        ("profileCarrying", () => false),
        ("runNeutralHash", () => null),
        ("runSpots", () => new JsonArray()),
        ("renders", () => new JsonArray()),
        ("macros", () => new JsonArray()),
        ("units", () => new JsonArray()),
        ("invalid", () => null),
        ("retries", () => new JsonArray()),
        ("profileReads", () => new JsonArray()),
        ("function", () => null),
        ("depth", () => 0),
    ];

    /// <summary>The names <see cref="Node"/> writes before the detail's own members.</summary>
    private static readonly HashSet<string> Written =
        new(NodeMembers.Select(m => m.Name).Concat(["nodeId", "itemId", "kind", "status", "outputHash", "stateReason"]), StringComparer.Ordinal);

    /// <summary>
    /// A node row (part 08 § 4): <c>nodeId</c> (the rooted key: the row key without its items and passes), <c>itemId</c> (the
    /// innermost item's id, or <c>null</c>), <c>kind</c>, <c>status</c> (<c>Completed</c>, <c>Skipped</c>, <c>Failed</c>,
    /// <c>NotRun</c>, <c>Cancelled</c>), <c>stateReason</c> the row's reason, <c>outputHash</c> the hash of its
    /// <c>.content</c>, then every other member of part 08 § 4 from the detail or its default, then the detail's own.
    /// </summary>
    public static JsonObject Node(RunRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var detail = row.Detail;
        var json = new JsonObject
        {
            ["nodeId"] = NodeId(row.Key),
            ["itemId"] = ItemId(row.Key),
            ["kind"] = KindWord(row.Kind),
            ["status"] = Status(row.State),
            ["stateReason"] = row.Reason,
            ["outputHash"] = row.ValueHash,
        };
        foreach (var (name, fallback) in NodeMembers)
        {
            json[name] = detail.TryGetPropertyValue(name, out var given) ? given?.DeepClone() : fallback();
        }

        foreach (var (name, member) in detail)
        {
            if (!Written.Contains(name))
                json[name] = member?.DeepClone();
        }

        return json;
    }

    /// <summary>
    /// The members of a check row after <c>key</c>, <c>kind</c>, <c>label</c> and <c>status</c> (part 08 § 6), in order, each
    /// with its default when the detail does not give it; <c>outputHash</c> and <c>stateReason</c> are the row's own.
    /// </summary>
    private static readonly (string Name, Func<JsonNode?> Default)[] CheckMembers =
    [
        ("itemName", () => null),
        ("itemNameHash", () => null),
        ("alignedWith", () => null),
        ("checked", () => null),
        ("reads", () => new JsonArray()),
        ("inputHash", () => null),
        ("outputHash", () => null),
        ("runNeutralHash", () => null),
        ("producer", () => "computed"),
        ("modelDerived", () => false),
        ("runDependent", () => false),
        ("profileCarrying", () => false),
        ("pass", () => null),
        ("failWith", () => null),
        ("passed", () => null),
        ("total", () => null),
        ("uncoveredHash", () => null),
        ("uncoveredSize", () => null),
        ("untestedHash", () => null),
        ("untestedSize", () => null),
        ("effect", () => null),
        ("count", () => null),
    ];

    /// <summary>
    /// #1506 (area 09; part 08 § 6; part 06 §§ 2.1, 5; D7, D8): a check row, its members in part 08 § 6's order and no
    /// others: <c>key</c>, <c>kind</c> (<c>computed</c>, or <c>for</c> for a check loop's own row and its items), <c>label</c>,
    /// <c>status</c>, then from the detail (or the default) <c>itemName</c>, <c>itemNameHash</c>, <c>alignedWith</c>,
    /// <c>checked</c>, <c>reads</c>, <c>inputHash</c>, <c>outputHash</c> (the row's hash of its <c>.content</c>),
    /// <c>runNeutralHash</c>, <c>producer</c>, the three flags, the <c>check_result</c>'s members (<see cref="CheckResult"/>),
    /// <c>effect</c> and <c>count</c>, and last <c>stateReason</c>, the row's reason.
    /// </summary>
    public static JsonObject Check(RunRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var detail = row.Detail;
        var json = new JsonObject
        {
            ["key"] = row.Key,
            ["kind"] = KindWord(row.Kind),
            ["label"] = detail["label"]?.DeepClone(),
            ["status"] = Status(row.State),
        };
        foreach (var (name, fallback) in CheckMembers)
        {
            if (name == "outputHash")
                json[name] = row.ValueHash;
            else
                json[name] = detail.TryGetPropertyValue(name, out var given) ? given?.DeepClone() : fallback();
        }

        json["stateReason"] = row.Reason;
        return json;
    }

    /// <summary>
    /// #1506 (area 09; part 08 §§ 6, 11; part 06 § 2.1; D2): a <c>check_result</c>'s members on its row's
    /// <paramref name="detail"/>: <c>pass</c> its verdict (<c>null</c> when <paramref name="value"/> is not one: the check was
    /// skipped or failed to run), <c>failWith</c> its <c>message</c> only when it did not pass, <c>passed</c> and
    /// <c>total</c> its counts, and its <c>uncovered</c> and <c>untested</c> lists by canonical hash and element count
    /// (<c>null</c> both for a list that is <c>null</c>). The lists themselves come from inputs, so they are returned for
    /// the settled text and never written to the detail; <c>null</c> when there is neither.
    /// </summary>
    public static JsonObject? CheckResult(JsonNode? value, JsonObject detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        var result = value as JsonObject;
        bool? pass = result?["pass"] is JsonValue verdict && verdict.TryGetValue<bool>(out var passed) ? passed : null;
        detail["pass"] = pass;
        detail["failWith"] = pass == false ? SourceLayout.Text(result!["message"]) : null;
        detail["passed"] = Count(result?["passed"]);
        detail["total"] = Count(result?["total"]);
        JsonObject? text = null;
        foreach (var list in new[] { "uncovered", "untested" })
        {
            var items = result?[list] as JsonArray;
            detail[$"{list}Hash"] = items == null ? null : CanonicalJson.Hash(items);
            detail[$"{list}Size"] = items?.Count;
            if (items != null)
            {
                text ??= [];
                text[list] = items.DeepClone();
            }
        }

        return text;
    }

    /// <summary>A <c>check_result</c>'s count as a whole number, or <c>null</c> when it is not one.</summary>
    private static int? Count(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == System.Text.Json.JsonValueKind.Number && value.TryGetValue<int>(out var whole) ? whole : null;

    /// <summary>
    /// #1506 (area 09; part 08 § 4; D9): a computed row's (and a check's) <c>inputHash</c>: the hash of the canonical JSON
    /// <c>{ "expression": &lt;rule&gt;, "reads": [ { "path", "hash" } … ] }</c>, <paramref name="rule"/> the rule as
    /// <c>resolve.json</c> writes it, <paramref name="reads"/> the values it read by hash, in path order.
    /// </summary>
    public static string ComputedInputHash(JsonNode? rule, IReadOnlyList<RunRead> reads) => ComputedNeutralHash(rule, reads, []);

    /// <summary>
    /// #1506 (area 09; part 08 § 4; D9): a run-dependent computed row's <c>runNeutralHash</c>: <see cref="ComputedInputHash"/>
    /// with the hashes of the reads at <paramref name="runDependentPaths"/> left out (their paths stay), so it covers the
    /// expression and its other reads, not the run-dependent values.
    /// </summary>
    public static string ComputedNeutralHash(JsonNode? rule, IReadOnlyList<RunRead> reads, IEnumerable<string> runDependentPaths)
    {
        ArgumentNullException.ThrowIfNull(reads);
        ArgumentNullException.ThrowIfNull(runDependentPaths);
        var left = new HashSet<string>(runDependentPaths, StringComparer.Ordinal);
        var list = new JsonArray();
        foreach (var read in reads.OrderBy(r => r.Path, StringComparer.Ordinal))
        {
            var item = new JsonObject { ["path"] = read.Path };
            if (!left.Contains(read.Path))
                item["hash"] = read.Hash;
            list.Add(item);
        }

        return CanonicalJson.Hash(new JsonObject { ["expression"] = rule?.DeepClone(), ["reads"] = list });
    }

    /// <summary>Part 08 § 2.1's rooted key of a row key: the key up to its first <c>:</c> (its items) or <c>#</c> (a pass).</summary>
    public static string NodeId(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var cut = key.IndexOfAny([':', '#']);
        return cut < 0 ? key : key[..cut];
    }

    /// <summary>Storage 11's <c>itemId</c>: the innermost item's id in a row key (<c>1</c> of <c>node.a.b:2/1</c>), or <c>null</c> outside every loop item.</summary>
    public static string? ItemId(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var colon = key.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
            return null;
        var last = key[(colon + 1)..].Split('/')[^1];
        var pass = last.IndexOf('#', StringComparison.Ordinal);
        return pass < 0 ? last : last[..pass];
    }

    /// <summary>Part 08 § 4's <c>kind</c>: <c>agent</c>, <c>rendered</c>, <c>computed</c>, <c>for</c>, <c>while</c>, <c>if</c>, <c>case</c> or <c>call</c> (a deliverable is <c>rendered</c>).</summary>
    public static string KindWord(PlanKind kind) => kind switch
    {
        PlanKind.Agent => "agent",
        PlanKind.Rendered => "rendered",
        PlanKind.Computed => "computed",
        PlanKind.For => "for",
        PlanKind.While => "while",
        PlanKind.If => "if",
        PlanKind.Case => "case",
        PlanKind.Call => "call",
        PlanKind.Deliverable => "rendered",
        _ => kind.ToString().ToLowerInvariant(),
    };

    /// <summary>
    /// Storage 11's <c>status</c> words (<c>ConsultNodeOutputState.Status</c>: <c>Completed</c>, <c>Skipped</c>,
    /// <c>Failed</c>) and the two that are new at 12, <c>NotRun</c> and <c>Cancelled</c>; a deliverable's own state as named.
    /// </summary>
    public static string Status(RowState state) => state switch
    {
        RowState.Completed => "Completed",
        RowState.Skipped => "Skipped",
        RowState.Failed => "Failed",
        RowState.NotRun => "NotRun",
        RowState.Cancelled => "Cancelled",
        _ => state.ToString(),
    };

    /// <summary>
    /// #1506 (D2): the outputs of the run spots in <paramref name="runSpots"/> whose row <c>output</c> is <c>null</c> (they
    /// follow 7-day retention), by spot id, each sliced from <paramref name="text"/> (the text they were written into) at
    /// its <c>start</c> and <c>length</c> in runes; <c>null</c> when there is none or no text.
    /// </summary>
    public static JsonObject? SpotTexts(JsonNode? runSpots, string? text)
    {
        if (text == null || runSpots is not JsonArray spots)
            return null;
        JsonObject? texts = null;
        foreach (var spot in spots.OfType<JsonObject>())
        {
            if (spot["output"] != null || SourceLayout.Text(spot["id"]) is not { } id
                || spot["start"] is not JsonValue s || !s.TryGetValue<int>(out var start)
                || spot["length"] is not JsonValue l || !l.TryGetValue<int>(out var length)
                || RuneSlice(text, start, length) is not { } output)
            {
                continue;
            }

            texts ??= [];
            texts[id] = output;
        }

        return texts;
    }

    /// <summary>The <paramref name="length"/> runes of <paramref name="text"/> from rune <paramref name="start"/>, or <c>null</c> when they are not there.</summary>
    public static string? RuneSlice(string text, int start, int length)
    {
        if (start < 0 || length < 0)
            return null;
        var slice = new StringBuilder();
        var at = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (at >= start + length)
                break;
            if (at >= start)
                slice.Append(rune.ToString());
            at++;
        }

        return at < start + length ? null : slice.ToString();
    }
}
