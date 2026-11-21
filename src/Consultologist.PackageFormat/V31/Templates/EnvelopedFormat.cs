using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 02 § 3.5 steps 5 and 6; part 08 § 4; D8): the format as a model is sent it.
/// <paramref name="Sent"/> is <see cref="SentFormat.Prepare"/>'s copy, or, when that copy's root is not an object, the
/// engine's envelope around it (<c>{ "type": "object", "required": ["value"], "additionalProperties": false,
/// "properties": { "value": &lt;copy&gt; } }</c>, <paramref name="Wrapped"/>), so a text or a list format can be sent in
/// strict mode. <paramref name="Hash"/> is the hash of the canonical JSON sent, envelope included (<c>sentFormatHash</c>);
/// <paramref name="NotSent"/> the keywords strict mode rejects that were removed, as pointers into the format.
/// </summary>
public sealed record EnvelopedFormat(JsonNode Sent, bool Wrapped, string Hash, IReadOnlyList<string> NotSent)
{
    /// <summary>The one member of the envelope, whose value is what is validated.</summary>
    public const string Member = "value";

    /// <summary>
    /// <paramref name="format"/> (a name or a schema, in <paramref name="formats"/>' scope) as it is sent with
    /// <paramref name="strictRejects"/> removed, enveloped when its root is not an object. Never throws on a format.
    /// </summary>
    public static EnvelopedFormat Of(JsonNode? format, FormatSet formats, IReadOnlyList<string> strictRejects)
    {
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(strictRejects);
        var prepared = SentFormat.Prepare(format, formats, strictRejects);
        if (SourceLayout.Text(prepared.Schema["type"]) == "object")
            return new EnvelopedFormat(prepared.Schema, false, prepared.Hash, prepared.NotSent);

        var envelope = new JsonObject
        {
            ["type"] = "object",
            ["required"] = new JsonArray(Member),
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject { [Member] = prepared.Schema },
        };
        return new EnvelopedFormat(envelope, true, CanonicalJson.Hash(envelope), prepared.NotSent);
    }

    /// <summary>
    /// The reply as its format's value is read from: for an enveloped format, the JSON text of the reply's
    /// <see cref="Member"/>, or <c>null</c> when the reply is not an object holding that one member; otherwise the reply
    /// itself. Never throws on a reply.
    /// </summary>
    public string? Unwrap(string reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        if (!Wrapped)
            return reply;
        try
        {
            if (JsonNode.Parse(reply, documentOptions: new JsonDocumentOptions { MaxDepth = PlanFormats.MaxReplyDepth }) is not JsonObject envelope)
                return null;
            var members = envelope.ToList(); // a repeated member surfaces here
            return members is [{ Key: Member, Value: var value }] ? value?.ToJsonString() ?? "null" : null;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }
}
