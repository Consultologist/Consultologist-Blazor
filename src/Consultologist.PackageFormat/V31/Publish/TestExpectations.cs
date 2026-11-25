using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Harness;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.PackageFormat.V31.Publish;

/// <summary>
/// #1508 (specVersion 31, area 11; part 07 § 9; D7): the first thing a test's run did not do as its <c>expect</c> says.
/// <paramref name="Expectation"/> is the member (<c>run</c> when the run did not end as it should, else <c>ran</c>,
/// <c>skipped</c>, <c>decisions</c>, <c>loops</c>, <c>values</c>, <c>prompts</c>, <c>checks</c> or <c>deliverables</c>),
/// <paramref name="Key"/> the row key, decision id or deliverable key (<c>""</c> for <c>run</c>). <paramref name="Expected"/>
/// and <paramref name="Actual"/> are hashes for the text of a prompt or a deliverable, canonical JSON for a value, and plain
/// words otherwise: never a prompt's or a deliverable's text.
/// </summary>
public sealed record TestMismatch(string Expectation, string Key, string Expected, string Actual);

/// <summary>
/// #1508 (area 11; part 07 § 8.3; D7): checks a test's <c>expect</c> against its offline run and gives the first mismatch.
/// Order: a run that did not complete (unless <c>expect</c> says it fails through a deliverable's <c>fail</c>), then the
/// members <c>ran</c>, <c>skipped</c>, <c>decisions</c>, <c>loops</c>, <c>values</c>, <c>prompts</c>, <c>checks</c>,
/// <c>deliverables</c>, each by key in ordinal order. A member of a shape the grammar does not give is not checked (it never
/// throws; <see cref="PackageTestFiles"/> refuses such a file before it gets here).
/// </summary>
public static class TestExpectations
{
    private const string NotRun = "not run";

    /// <summary>The first mismatch of <paramref name="result"/> against <paramref name="expect"/>, or <c>null</c> when every listed expectation holds.</summary>
    public static TestMismatch? First(RunPlan plan, JsonObject expect, OfflineResult result)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(expect);
        ArgumentNullException.ThrowIfNull(result);

        var rows = new Dictionary<string, RunRow>(StringComparer.Ordinal);
        foreach (var row in result.Rows)
            rows[row.Key] = row;

        return RunEnd(expect, result)
            ?? RowStates(plan, expect, "ran", rows, ran: true)
            ?? RowStates(plan, expect, "skipped", rows, ran: false)
            ?? Decisions(expect, result)
            ?? Loops(expect, rows)
            ?? Values(expect, rows)
            ?? Prompts(expect, result)
            ?? Checks(expect, rows)
            ?? Deliverables(expect, result);
    }

    // ---- the run -------------------------------------------------------------------------------

    private static TestMismatch? RunEnd(JsonObject expect, OfflineResult result)
    {
        if (result.State == RunState.Completed)
            return null;
        if (result.State == RunState.Failed && FailsThroughExpectedDeliverable(expect, result))
            return null;
        var actual = result.State is { } state ? Word(state.ToString()) : "refused";
        return new TestMismatch("run", "", "completed", actual);
    }

    /// <summary>The run failed, and a deliverable that failed is one <c>expect.deliverables</c> says <c>failed</c> (part 07 § 8.3).</summary>
    private static bool FailsThroughExpectedDeliverable(JsonObject expect, OfflineResult result) =>
        expect["deliverables"] is JsonObject listed
        && result.Deliverables.Any(d => d.State == DeliverableState.Failed
            && listed[d.Key] is JsonObject entry && Text(entry["state"]) == "failed");

    // ---- ran, skipped --------------------------------------------------------------------------

    private static TestMismatch? RowStates(RunPlan plan, JsonObject expect, string member, Dictionary<string, RunRow> rows, bool ran)
    {
        if (expect[member] is not JsonArray keys)
            return null;
        foreach (var key in keys.Select(Text).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            rows.TryGetValue(key, out var row);
            // No row holds for skipped only when the key names an entry of the plan (a child of a skipped or not-run container).
            var holds = ran
                ? row?.State is RowState.Completed or RowState.Released
                : row == null ? IsPlanEntry(plan, key) : row.State is RowState.Skipped or RowState.NotRun;
            if (!holds)
                return new TestMismatch(member, key, member == "ran" ? "ran" : "skipped", RowWord(row));
        }

        return null;
    }

    private static string RowWord(RunRow? row) => row?.State switch
    {
        null or RowState.NotRun => NotRun,
        RowState.Completed or RowState.Released => "ran",
        { } state => Word(state.ToString()),
    };

    // ---- decisions -----------------------------------------------------------------------------

    private static TestMismatch? Decisions(JsonObject expect, OfflineResult result)
    {
        if (expect["decisions"] is not JsonObject listed)
            return null;
        var decided = new Dictionary<string, RunDecision>(StringComparer.Ordinal);
        foreach (var decision in result.Decisions)
            decided[decision.Id] = decision;

        foreach (var (id, wanted) in listed.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            decided.TryGetValue(id, out var found);
            string expected;
            bool holds;
            if (wanted is JsonValue v && v.TryGetValue<bool>(out var flag))
            {
                expected = Word(flag);
                holds = found is { Branch: null, Result: { } got } && got == flag;
            }
            else if (Text(wanted) is { } branch)
            {
                expected = branch;
                holds = found is { Branch: { } taken } && string.Equals(taken, branch, StringComparison.Ordinal);
            }
            else
                continue;

            if (!holds)
            {
                var actual = found == null ? NotRun
                    : found.Branch != null ? found.Branch
                    : found.Result is { } held ? Word(held)
                    : "undecided";
                return new TestMismatch("decisions", id, expected, actual);
            }
        }

        return null;
    }

    // ---- loops ---------------------------------------------------------------------------------

    private static TestMismatch? Loops(JsonObject expect, Dictionary<string, RunRow> rows)
    {
        if (expect["loops"] is not JsonObject listed)
            return null;
        foreach (var (key, wanted) in listed.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (wanted is not JsonObject shape)
                continue;
            rows.TryGetValue(key, out var row);
            if (shape["count"] is { } count && Integer(count) is { } n)
            {
                var expected = $"count {n}";
                var actual = row == null ? NotRun : Integer(row.Detail["count"]) is { } c ? $"count {c}" : "no count";
                if (expected != actual)
                    return new TestMismatch("loops", key, expected, actual);
            }
            else if (shape["passes"] is { } passes && Integer(passes) is { } p && Text(shape["stopped"]) is { } stopped)
            {
                var expected = $"passes {p}, stopped {stopped}";
                var actual = row == null ? NotRun
                    : Integer(row.Detail["passes"]) is { } made ? $"passes {made}, stopped {Text(row.Detail["stopped"]) ?? "none"}"
                    : "no passes";
                if (expected != actual)
                    return new TestMismatch("loops", key, expected, actual);
            }
        }

        return null;
    }

    // ---- values --------------------------------------------------------------------------------

    private static TestMismatch? Values(JsonObject expect, Dictionary<string, RunRow> rows)
    {
        if (expect["values"] is not JsonObject listed)
            return null;
        foreach (var (key, wanted) in listed.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var expected = CanonicalJson.Write(wanted);
            rows.TryGetValue(key, out var row);
            var actual = row == null ? NotRun
                : row.Kind != PlanKind.Computed ? "not computed"
                : TryValue(row, out var value) ? CanonicalJson.Write(value)
                : RowWord(row);
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                return new TestMismatch("values", key, expected, actual);
        }

        return null;
    }

    /// <summary>A row's settled value: the canonical JSON it kept when the value is not text, else its text; false when it has none.</summary>
    private static bool TryValue(RunRow row, out JsonNode? value)
    {
        value = null;
        if (row.State is not (RowState.Completed or RowState.Released) || row.Text == null)
            return false;
        if (Text(row.Text["value"]) is { } json)
        {
            try
            {
                value = JsonNode.Parse(json);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        if (Text(row.Text["content"]) is { } text)
        {
            value = JsonValue.Create(text);
            return true;
        }

        return false;
    }

    // ---- prompts -------------------------------------------------------------------------------

    private static TestMismatch? Prompts(JsonObject expect, OfflineResult result)
    {
        if (expect["prompts"] is not JsonObject listed)
            return null;
        foreach (var (key, wanted) in listed.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (wanted is not JsonObject entry)
                continue;
            var expected = Text(entry["text"]) is { } text ? CanonicalJson.HashText(text) : Text(entry["hash"]);
            if (expected == null)
                continue;
            var sent = result.Prompts.FirstOrDefault(p => p.Key == key);
            var actual = sent?.Hash ?? NotRun;
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                return new TestMismatch("prompts", key, expected, actual);
        }

        return null;
    }

    // ---- checks --------------------------------------------------------------------------------

    private static TestMismatch? Checks(JsonObject expect, Dictionary<string, RunRow> rows)
    {
        if (expect["checks"] is not JsonObject listed)
            return null;
        foreach (var (key, wanted) in listed.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (wanted is not JsonObject entry || entry["pass"] is not JsonValue v || !v.TryGetValue<bool>(out var pass))
                continue;
            rows.TryGetValue(key, out var row);
            var actual = row == null ? NotRun
                : row.Detail["pass"] is JsonValue got && got.TryGetValue<bool>(out var verdict) ? Word(verdict)
                : RowWord(row);
            if (Word(pass) != actual)
                return new TestMismatch("checks", key, Word(pass), actual);
        }

        return null;
    }

    // ---- deliverables --------------------------------------------------------------------------

    private static TestMismatch? Deliverables(JsonObject expect, OfflineResult result)
    {
        if (expect["deliverables"] is not JsonObject listed)
            return null;
        foreach (var (key, wanted) in listed.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (wanted is not JsonObject entry || Text(entry["state"]) is not { } state)
                continue;
            var outcome = result.Deliverables.FirstOrDefault(d => d.Key == key);
            if (outcome == null)
                return new TestMismatch("deliverables", key, state, NotRun);
            var actualState = Word(outcome.State.ToString());
            if (state != actualState)
                return new TestMismatch("deliverables", key, state, actualState);

            var expected = Text(entry["text"]) is { } text ? CanonicalJson.HashText(text) : Text(entry["hash"]);
            if (expected != null && !string.Equals(expected, outcome.ContentHash, StringComparison.Ordinal))
                return new TestMismatch("deliverables", key, expected, outcome.ContentHash ?? "none");
        }

        return null;
    }

    // ---- helpers -------------------------------------------------------------------------------

    /// <summary>
    /// Whether <paramref name="key"/> (a row key) is of an entry of <paramref name="plan"/>: the key before <c>:</c> with any
    /// <c>#n</c> removed is an entry's key, or goes through a call node into the function's own entries (<c>node.standards.section.patient_draft</c>).
    /// </summary>
    private static bool IsPlanEntry(RunPlan plan, string key)
    {
        var colon = key.IndexOf(':', StringComparison.Ordinal);
        var entry = System.Text.RegularExpressions.Regex.Replace(colon < 0 ? key : key[..colon], "#[0-9]+", "");
        return Resolves(plan, plan.Entries, entry);
    }

    private static bool Resolves(RunPlan plan, IReadOnlyDictionary<string, PlanEntry> entries, string key)
    {
        if (entries.ContainsKey(key))
            return true;
        foreach (var call in entries.Values)
        {
            if (call.Kind != PlanKind.Call || call.Function is not { } id || !plan.Functions.TryGetValue(id, out var function)
                || !key.StartsWith(call.Key + ".", StringComparison.Ordinal))
                continue;
            if (Resolves(plan, function.Entries, "node" + key[call.Key.Length..]))
                return true;
        }

        return false;
    }

    private static string Word(string name) => name.ToLowerInvariant();

    private static string Word(bool value) => value ? "true" : "false";

    private static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var text) ? text : null;

    private static long? Integer(JsonNode? node) =>
        node is JsonValue v && v.GetValueKind() == JsonValueKind.Number
        && long.TryParse(v.ToJsonString(), System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var n)
            ? n
            : null;
}
