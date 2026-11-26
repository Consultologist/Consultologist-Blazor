using System.Text.Json;
using Consultologist.Web.Services.AI;

namespace Consultologist.Web.Services.Provenance.V31;

/// <summary>
/// #1509 (Task 13; part 08 §§ 3 to 7, 10): one stored row, read leniently. A row written by an earlier build may lack a
/// member, hold <c>null</c> where an array is expected, or carry a member this page does not know; every accessor answers
/// what is there and otherwise nothing (null, or an empty list), and never throws. A member of the wrong kind reads as absent.
/// </summary>
public sealed class Row31
{
    private readonly JsonElement _json;

    public Row31(string key, string section, JsonElement json)
    {
        Key = key ?? "";
        Section = section ?? "";
        _json = json;
    }

    /// <summary>The row's key as the listing gives it (a decision's is its id).</summary>
    public string Key { get; }

    /// <summary><c>node</c>, <c>check</c>, <c>deliverable</c>, <c>delivery</c> or <c>decision</c>.</summary>
    public string Section { get; }

    /// <summary>The row's JSON as stored.</summary>
    public JsonElement Json => _json;

    /// <summary>A string member; null when absent, <c>null</c> or not a string.</summary>
    public string? Text(string member) => Member(member, JsonValueKind.String) is { } value ? value.GetString() : null;

    /// <summary>A boolean member; null when absent, <c>null</c> or not a boolean.</summary>
    public bool? Flag(string member) => Member(member) is { ValueKind: JsonValueKind.True or JsonValueKind.False } value ? value.GetBoolean() : null;

    /// <summary>A whole-number member; null when absent, <c>null</c> or not a whole number that fits.</summary>
    public int? Int(string member) =>
        Member(member, JsonValueKind.Number) is { } value && value.TryGetInt32(out var number) ? number : null;

    /// <summary>An array member's elements; empty when absent, <c>null</c> or not an array.</summary>
    public IReadOnlyList<JsonElement> List(string member) =>
        Member(member, JsonValueKind.Array) is { } value ? value.EnumerateArray().ToList() : [];

    /// <summary>An object member; null when absent, <c>null</c> or not an object.</summary>
    public JsonElement? Object(string member) => Member(member, JsonValueKind.Object);

    private JsonElement? Member(string member, JsonValueKind? kind = null) =>
        _json.ValueKind == JsonValueKind.Object
        && _json.TryGetProperty(member, out var value)
        && (kind is null || value.ValueKind == kind)
            ? value
            : null;
}

/// <summary>
/// #1509 (Task 13; part 08 § 1.2): a 31 job's record as History reads it: the job's header and its rows, sorted into
/// sections once, from the one listing (<c>GET ConsultGenerationJobs/{jobId}/Rows</c>); nothing here fetches a row.
/// Nodes, checks and deliverables are in key order as a person counts (<see cref="RowKeyOrder31"/>: item 10 after item 9);
/// decisions in ordinal order of their id (§ 1.2); a deliverable's deliveries in key order. A row with no key or no known
/// section is left out. A decision may be owned by a node that never got a row: <see cref="DecisionsOwnedBy"/> finds it
/// all the same. Pure.
/// </summary>
public sealed class Record31
{
    private readonly Dictionary<string, Row31> _byKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Row31>> _deliveries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Row31>> _decisions = new(StringComparer.Ordinal);

    private Record31(ConsultGenerationJobResponse header, List<Row31> nodes, List<Row31> checks, List<Row31> deliverables, List<Row31> decisions)
    {
        Header = header;
        Nodes = nodes;
        Checks = checks;
        Deliverables = deliverables;
        Decisions = decisions;
    }

    /// <summary>The job's header, as the job response gives it.</summary>
    public ConsultGenerationJobResponse Header { get; }

    public IReadOnlyList<Row31> Nodes { get; }

    public IReadOnlyList<Row31> Checks { get; }

    public IReadOnlyList<Row31> Deliverables { get; }

    public IReadOnlyList<Row31> Decisions { get; }

    public static Record31 From(ConsultGenerationJobResponse header, IReadOnlyList<RecordRowRef> rows)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(rows);

        var nodes = new List<Row31>();
        var checks = new List<Row31>();
        var deliverables = new List<Row31>();
        var decisions = new List<Row31>();
        var deliveries = new List<Row31>();
        var all = new List<Row31>(rows.Count);
        foreach (var listed in rows)
        {
            if (listed?.Key is null || listed.Section is null)
            {
                continue;
            }

            var row = new Row31(listed.Key, listed.Section, listed.Row);
            var section = listed.Section switch
            {
                "node" => nodes,
                "check" => checks,
                "deliverable" => deliverables,
                "decision" => decisions,
                "delivery" => deliveries,
                _ => null,
            };
            if (section is null)
            {
                continue;
            }

            section.Add(row);
            all.Add(row);
        }

        nodes.Sort(ByKey);
        checks.Sort(ByKey);
        deliverables.Sort(ByKey);
        decisions.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        deliveries.Sort(ByKey);

        var record = new Record31(header, nodes, checks, deliverables, decisions);
        foreach (var row in all)
        {
            record._byKey.TryAdd(row.Key, row);
        }

        foreach (var delivery in deliveries)
        {
            Group(record._deliveries, delivery.Text("deliverable") ?? DeliverableOf(delivery.Key), delivery);
        }

        foreach (var decision in decisions)
        {
            if (decision.Text("owner") is { } owner)
            {
                Group(record._decisions, owner, decision);
            }
        }

        return record;
    }

    /// <summary>The delivery rows of a deliverable, in key order; empty for none.</summary>
    public IReadOnlyList<Row31> DeliveriesOf(string deliverableKey) =>
        deliverableKey != null && _deliveries.TryGetValue(deliverableKey, out var rows) ? rows : [];

    /// <summary>The decisions a row, check or deliverable owns, in ordinal order of id, whether or not it has a row.</summary>
    public IReadOnlyList<Row31> DecisionsOwnedBy(string ownerKey) =>
        ownerKey != null && _decisions.TryGetValue(ownerKey, out var rows) ? rows : [];

    /// <summary>The row stored under <paramref name="key"/>, in any section, or null.</summary>
    public Row31? Find(string key) => key != null && _byKey.TryGetValue(key, out var row) ? row : null;

    private static int ByKey(Row31 a, Row31 b) => RowKeyOrder31.Instance.Compare(a.Key, b.Key);

    private static void Group(Dictionary<string, List<Row31>> groups, string key, Row31 row)
    {
        if (!groups.TryGetValue(key, out var list))
        {
            groups[key] = list = [];
        }

        list.Add(row);
    }

    /// <summary>The deliverable a delivery key names (<c>delivery:&lt;deliverable&gt;/&lt;destination&gt;</c>), for a row with no <c>deliverable</c> member.</summary>
    private static string DeliverableOf(string key)
    {
        var parsed = RowKey31.Parse(key);
        var slash = parsed.Path.LastIndexOf('/');
        return string.Equals(parsed.Root, "delivery", StringComparison.Ordinal) && slash > 0 ? parsed.Path[..slash] : "";
    }
}
