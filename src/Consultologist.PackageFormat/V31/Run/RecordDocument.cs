using System.Globalization;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Run;

/// <summary>
/// #1506 (specVersion 31, area 09; D10): one stored row of the record: its <paramref name="Section"/> (<c>node</c>,
/// <c>check</c>, <c>deliverable</c>, <c>delivery</c>, <c>decision</c>), its key, and its canonical JSON.
/// </summary>
public sealed record RecordRow(string Section, string Key, string Json);

/// <summary>
/// #1506 (specVersion 31, area 09; part 08 §§ 1.1, 2, 3; D13): the canonical record document, assembled from the header and
/// the rows. A pure function of its inputs: the same rows in any order give the same bytes.
/// </summary>
public static class RecordDocument
{
    /// <summary>The members <see cref="Assemble"/> adds to the header.</summary>
    private static readonly string[] Sections = ["decisions", "nodeOutputs", "checks", "deliverables"];

    /// <summary>What changes after the end of a job, so it is never in the sealed bytes (D13).</summary>
    private static readonly string[] AfterTheEnd = ["recordHash", "textDroppedAtUtc", "history"];

    /// <summary>The stored row of <paramref name="row"/>, in its <see cref="RunRow.Section"/>.</summary>
    public static RecordRow Of(RunRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new RecordRow(row.Section, row.Key, CanonicalJson.Write(row.ToJson()));
    }

    /// <summary>The stored row of <paramref name="decision"/>: section <c>decision</c>, keyed by its id.</summary>
    public static RecordRow Of(RunDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        return new RecordRow("decision", decision.Id, CanonicalJson.Write(decision.ToJson()));
    }

    /// <summary>The stored row of <paramref name="row"/>: section <c>delivery</c>.</summary>
    public static RecordRow Of(DeliveryRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new RecordRow("delivery", row.Key, CanonicalJson.Write(row.ToJson()));
    }

    /// <summary>
    /// The canonical text of the record: <paramref name="header"/>'s members, then <c>decisions</c> (an array by id),
    /// <c>nodeOutputs</c> and <c>checks</c> (objects by row key) and <c>deliverables</c> (an array by key, each with its
    /// <c>deliveries</c> by key). The same row twice is one; the same key with other JSON is an engine fault
    /// (<see cref="InvalidOperationException"/>), as is a delivery whose deliverable has no row. A header that already holds
    /// one of the four names, or a member that changes after the end (<c>recordHash</c>, <c>textDroppedAtUtc</c>,
    /// <c>history</c>), is refused (<see cref="ArgumentException"/>).
    /// </summary>
    public static string Assemble(JsonObject header, IEnumerable<RecordRow> rows)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(rows);
        foreach (var name in Sections.Concat(AfterTheEnd))
        {
            if (header.ContainsKey(name))
                throw new ArgumentException($"The record's header may not hold '{name}'.", nameof(header));
        }

        foreach (var row in rows.ToList())
        {
            if (row.Section is not ("node" or "check" or "deliverable" or "delivery" or "decision"))
                throw new InvalidOperationException($"The record has no section '{row.Section}' (row '{row.Key}').");
        }

        var kept = new Dictionary<(string Section, string Key), string>();
        foreach (var row in rows)
        {
            if (kept.TryGetValue((row.Section, row.Key), out var json))
            {
                if (!string.Equals(json, row.Json, StringComparison.Ordinal))
                    throw new InvalidOperationException($"The record holds two different rows for {row.Section} '{row.Key}'.");
                continue;
            }

            kept[(row.Section, row.Key)] = row.Json;
        }

        JsonObject Parsed(string json) => (JsonObject)JsonNode.Parse(json)!;
        IEnumerable<(string Key, string Json)> In(string section) =>
            kept.Where(k => k.Key.Section == section).Select(k => (k.Key.Key, k.Value)).OrderBy(k => k.Key, StringComparer.Ordinal);

        var deliveries = new Dictionary<string, JsonArray>(StringComparer.Ordinal);
        foreach (var (_, json) in In("delivery"))
        {
            var delivery = Parsed(json);
            var owner = delivery["deliverable"]?.GetValue<string>()
                ?? throw new InvalidOperationException("A delivery row names no deliverable.");
            if (!kept.ContainsKey(("deliverable", owner)))
                throw new InvalidOperationException($"The delivery '{delivery["key"]}' is of '{owner}', which has no row.");
            if (!deliveries.TryGetValue(owner, out var list))
                deliveries[owner] = list = [];
            list.Add(delivery);
        }

        var document = new JsonObject();
        foreach (var (name, value) in header)
            document[name] = value?.DeepClone();

        var decisions = new JsonArray();
        foreach (var (_, json) in In("decision"))
            decisions.Add(Parsed(json));
        document["decisions"] = decisions;

        var outputs = new JsonObject();
        foreach (var (key, json) in In("node"))
            outputs[key] = Parsed(json);
        document["nodeOutputs"] = outputs;

        var checks = new JsonObject();
        foreach (var (key, json) in In("check"))
            checks[key] = Parsed(json);
        document["checks"] = checks;

        var deliverables = new JsonArray();
        foreach (var (key, json) in In("deliverable"))
        {
            var deliverable = Parsed(json);
            deliverable["deliveries"] = deliveries.TryGetValue(key, out var own) ? own : new JsonArray();
            deliverables.Add(deliverable);
        }

        document["deliverables"] = deliverables;
        return CanonicalJson.Write(document);
    }

    /// <summary>The <c>recordHash</c> of <paramref name="document"/>: <c>sha256:</c> and its hex over its UTF-8.</summary>
    public static string Hash(string document) => CanonicalJson.HashText(document);
}
