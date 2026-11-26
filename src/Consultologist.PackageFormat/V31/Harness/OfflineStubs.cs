using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Run;
using Consultologist.PackageFormat.V31.Templates;

namespace Consultologist.PackageFormat.V31.Harness;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 3.1 "the stubs"; part 07 § 8.2; D12): the reply an offline run serves an agent
/// call. A pinned output is served as given; a missing one is generated from the entry's format alone by
/// <see cref="WorkflowStubOutputs"/>, seeded by the seed given (the row key, or 30's key in a comparison): a text format's
/// <see cref="WorkflowStubOutputs.Text"/> (one line, so it fits <c>markdown_inline</c> too, D21), another format's
/// <see cref="WorkflowStubOutputs.FromSchema"/> of its expanded schema. Then, for a text reply it generated (#1508, comment 6
/// item 2; R14: a pinned reply is served exactly as written, so a marker it leaves out gives the node's <c>onInvalid</c>
/// outcome, as live), each macro the prompt offers whose marker the reply does not already hold has its marker appended, in
/// prompt order, in the form its instruction asks (a block marker alone on a line of its own, an inline one after a space),
/// so an <c>ordered</c> node's markers stand in order and none is written twice. A reply that does not fit its format is
/// served as it is: the run fails its node with the runner's sentence.
/// </summary>
public static class OfflineStubs
{
    /// <summary>
    /// The reply for <paramref name="call"/>, whose prompt <paramref name="prepared"/> was sent: <paramref name="pinned"/> when
    /// <paramref name="given"/>, exactly as written, else generated from <paramref name="seed"/> with D12's markers appended
    /// to a text reply (R14).
    /// Returns the value served (a text format's as a JSON string) and the reply's text as the model would send it (a
    /// text as it is, another value as canonical JSON, inside the engine's envelope when the format sent was wrapped).
    /// </summary>
    public static (JsonNode? Value, string Reply) Reply(RunPlan plan, AgentCall call, PreparedPrompt prepared, bool given, JsonNode? pinned, string seed)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(seed);

        var entries = call.Function == null ? plan.Entries : plan.Functions.GetValueOrDefault(call.Function)?.Entries;
        var format = entries?.GetValueOrDefault(call.EntryKey)?.Format;
        var value = given ? pinned?.DeepClone() : Generate(plan, format, seed);

        if (!given && prepared.TextReply && value is JsonValue text && text.TryGetValue<string>(out var reply))
            value = JsonValue.Create(WithMarkers(reply, prepared.Composition.Macros));

        var body = value is JsonValue s && s.TryGetValue<string>(out var plain) && prepared.TextReply ? plain : CanonicalJson.Write(value);
        if (prepared.Format is { Wrapped: true })
            body = CanonicalJson.Write(new JsonObject { [EnvelopedFormat.Member] = value?.DeepClone() });
        return (value, body);
    }

    /// <summary>A reply generated for <paramref name="format"/> (in <paramref name="plan"/>'s bundle) from <paramref name="seed"/>.</summary>
    public static JsonNode? Generate(RunPlan plan, JsonNode? format, string seed)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(seed);
        return plan.Formats.IsText(format)
            ? JsonValue.Create(WorkflowStubOutputs.Text(seed))
            : WorkflowStubOutputs.FromSchema(plan.Formats.Expand(format), seed);
    }

    /// <summary>
    /// <paramref name="reply"/> with the marker of each of <paramref name="offered"/> (in prompt order) it does not hold in
    /// either form appended: <c>\n{{~ marker }}\n</c> for a block, <c> {{ marker }}</c> for an inline one.
    /// </summary>
    public static string WithMarkers(string reply, IReadOnlyList<OfferedMacro> offered)
    {
        ArgumentNullException.ThrowIfNull(reply);
        ArgumentNullException.ThrowIfNull(offered);
        foreach (var macro in offered)
        {
            if (reply.Contains($"{{{{ {macro.Marker} }}}}", StringComparison.Ordinal) || reply.Contains($"{{{{~ {macro.Marker} }}}}", StringComparison.Ordinal))
                continue;
            reply += macro.Block ? $"\n{{{{~ {macro.Marker} }}}}\n" : $" {{{{ {macro.Marker} }}}}";
        }

        return reply;
    }
}
