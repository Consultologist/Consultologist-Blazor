using System.Globalization;
using System.Text;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1503 (specVersion 31, area 06): a whole run as one text, for a golden. Four parts (#1504, #1506: five, with the rows' details),
/// each under its heading: the agent calls in the order dispatched (row keys); every row in the order recorded, as <c>key | state | reason | value hash</c>
/// (<c>-</c> for none); every decision in the order recorded, as <c>id | condition | result | reads | flags</c> (#1506: a
/// <c>case</c>'s result its arm taken, its reads their paths, its flags <c>runDependent</c> and <c>modelDerived</c>
/// when true); and the result: its state, sentence, failed key, agent calls, peak in flight, and each
/// top-level entry's read form as a hash. No clock value: a decision's time is the host's and is left out. LF line ends,
/// a final line end.
/// </summary>
internal static class RunTrace
{
    public static string Of(FakeRunHost host, ListRecorder recorder, RunResult result) => Of(host.Dispatched, recorder, result);

    /// <summary>
    /// The trace of a run whose agent calls went out as <paramref name="dispatched"/>. #1504: a fifth part follows the
    /// decisions, <c># details</c>; #1506 (area 09): every row, in the order recorded, as <c>key | &lt;the row in part 08's
    /// shape (<see cref="RunRow.ToJson"/>) as canonical JSON&gt;</c> (ids, keys, hashes, lengths and package text only).
    /// </summary>
    public static string Of(IReadOnlyList<string> dispatched, ListRecorder recorder, RunResult result)
    {
        var text = new StringBuilder();
        text.Append("# dispatched\n");
        foreach (var key in dispatched)
            text.Append(key).Append('\n');

        text.Append("\n# rows\n");
        foreach (var row in recorder.Rows)
            text.Append(Row(row)).Append('\n');

        text.Append("\n# decisions\n");
        foreach (var decision in recorder.Decisions)
            text.Append(Decision(decision)).Append('\n');

        if (recorder.Rows.Count > 0)
        {
            text.Append("\n# details\n");
            foreach (var row in recorder.Rows)
                text.Append(row.Key).Append(" | ").Append(CanonicalJson.Write(row.ToJson())).Append('\n');
        }

        text.Append("\n# result\n");
        text.Append("state | ").Append(Word(result.State.ToString())).Append('\n');
        text.Append("sentence | ").Append(result.Sentence ?? "-").Append('\n');
        text.Append("failed | ").Append(result.FailedKey ?? "-").Append('\n');
        text.Append("agentCalls | ").Append(result.AgentCalls.ToString(CultureInfo.InvariantCulture)).Append('\n');
        text.Append("peakInFlight | ").Append(result.PeakInFlight.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (var (key, read) in result.Values.OrderBy(v => v.Key, StringComparer.Ordinal))
            text.Append("value | ").Append(key).Append(" | ").Append(CanonicalJson.Hash(read)).Append('\n');
        return text.ToString();
    }

    /// <summary>A row as a trace line: <c>key | state | reason | value hash</c>.</summary>
    public static string Row(RunRow row) =>
        $"{row.Key} | {Word(row.State.ToString())} | {row.Reason ?? "-"} | {row.ValueHash ?? "-"}";

    /// <summary>
    /// #1506: a decision as a trace line: <c>id | condition | result | reads | flags</c>, a <c>case</c>'s result its arm
    /// (<c>branch 2</c>, <c>branch else</c>), its reads' paths in order (their hashes are the values', which the trace never
    /// shows apart from a row's), and its flags that hold (<c>-</c> for none of either).
    /// </summary>
    public static string Decision(RunDecision decision)
    {
        var flags = new List<string>();
        if (decision.RunDependent)
            flags.Add("runDependent");
        if (decision.ModelDerived)
            flags.Add("modelDerived");
        return $"{decision.Id} | {decision.Condition} | "
            + (decision.Result is { } result ? (result ? "true" : "false") : $"branch {decision.Branch ?? "-"}")
            + $" | {(decision.Reads.Count == 0 ? "-" : string.Join(", ", decision.Reads.Select(r => r.Path)))}"
            + $" | {(flags.Count == 0 ? "-" : string.Join(", ", flags))}";
    }

    /// <summary>An enum member as the record words it: <c>NotRun</c> is <c>not_run</c>.</summary>
    private static string Word(string member)
    {
        var word = new StringBuilder();
        foreach (var c in member)
        {
            if (char.IsUpper(c) && word.Length > 0)
                word.Append('_');
            word.Append(char.ToLowerInvariant(c));
        }

        return word.ToString();
    }
}
