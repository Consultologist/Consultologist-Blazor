using System.Globalization;
using System.Text;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1503 (specVersion 31, area 06): a whole run as one text, for a golden. Four parts, each under its heading: the agent
/// calls in the order dispatched (row keys); every row in the order recorded, as <c>key | state | reason | value hash</c>
/// (<c>-</c> for none); every decision in the order recorded, as <c>site | member | printed condition | result</c> (a
/// <c>case</c>'s branch taken); and the result: its state, sentence, failed key, agent calls, peak in flight, and each
/// top-level entry's read form as a hash. No clock value: a decision's time is the host's and is left out. LF line ends,
/// a final line end.
/// </summary>
internal static class RunTrace
{
    public static string Of(FakeRunHost host, ListRecorder recorder, RunResult result)
    {
        var text = new StringBuilder();
        text.Append("# dispatched\n");
        foreach (var key in host.Dispatched)
            text.Append(key).Append('\n');

        text.Append("\n# rows\n");
        foreach (var row in recorder.Rows)
            text.Append(Row(row)).Append('\n');

        text.Append("\n# decisions\n");
        foreach (var decision in recorder.Decisions)
            text.Append(Decision(decision)).Append('\n');

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

    /// <summary>A decision as a trace line: <c>site | member | printed | result</c>, a <c>case</c>'s result its branch.</summary>
    public static string Decision(RunDecision decision) =>
        $"{decision.Site} | {decision.Member} | {decision.Printed} | "
        + (decision.Result is { } result ? (result ? "true" : "false") : $"branch {decision.Branch ?? "-"}");

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
