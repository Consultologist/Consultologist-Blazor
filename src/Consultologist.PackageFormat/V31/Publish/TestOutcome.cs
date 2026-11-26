using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Harness;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.PackageFormat.V31.Publish;

/// <summary>
/// #1508 (specVersion 31, area 11; part 07 § 9 <c>outcomeHash</c>; D8): the hash that lets two runs of one test be compared.
/// </summary>
public static class TestOutcome
{
    /// <summary>
    /// SHA-256 of the canonical JSON <c>{ "state", "rows": [{ "key", "state" }…], "decisions": [{ "id", "result" }…],
    /// "values": { key: value… }, "prompts": { key: hash… }, "deliverables": { key: { "state", "hash" }… } }</c>, rows and
    /// decisions in ordinal key order. <c>values</c> are the settled values of computed rows. No duration or time is in it, so
    /// the same test gives the same hash on any run; a stub that changes a prompt changes it.
    /// </summary>
    public static string Hash(OfflineResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var rows = new JsonArray();
        var values = new JsonObject();
        foreach (var row in result.Rows.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            rows.Add(new JsonObject { ["key"] = row.Key, ["state"] = Word(row.State.ToString()) });
            if (row.Kind == PlanKind.Computed && row.State == RowState.Completed && row.Text != null)
                values[row.Key] = ValueOf(row.Text);
        }

        var decisions = new JsonArray();
        foreach (var decision in result.Decisions.OrderBy(d => d.Id, StringComparer.Ordinal))
        {
            JsonNode? outcome = decision.Branch != null ? decision.Branch : decision.Result is { } flag ? flag : null;
            decisions.Add(new JsonObject { ["id"] = decision.Id, ["result"] = outcome });
        }

        var prompts = new JsonObject();
        foreach (var prompt in result.Prompts)
            prompts[prompt.Key] = prompt.Hash;

        var deliverables = new JsonObject();
        foreach (var deliverable in result.Deliverables)
            deliverables[deliverable.Key] = new JsonObject { ["state"] = Word(deliverable.State.ToString()), ["hash"] = deliverable.ContentHash };

        return CanonicalJson.Hash(new JsonObject
        {
            ["state"] = result.State is { } state ? Word(state.ToString()) : null,
            ["rows"] = rows,
            ["decisions"] = decisions,
            ["values"] = values,
            ["prompts"] = prompts,
            ["deliverables"] = deliverables,
        });
    }

    private static JsonNode? ValueOf(JsonObject text)
    {
        if (text["value"] is JsonValue kept && kept.TryGetValue<string>(out var json))
        {
            try
            {
                return JsonNode.Parse(json);
            }
            catch (System.Text.Json.JsonException)
            {
                return json;
            }
        }

        return text["content"] is JsonValue content && content.TryGetValue<string>(out var s) ? s : null;
    }

    private static string Word(string name) => name.ToLowerInvariant();
}
