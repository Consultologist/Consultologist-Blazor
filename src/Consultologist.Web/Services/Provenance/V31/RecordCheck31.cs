using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Web.Services.Provenance.V31;

/// <summary>One line of a record check: <paramref name="Ok"/> is null when nothing could be recomputed (attested, not a failure).</summary>
public sealed record RecordCheckLine(string What, bool? Ok, string Detail);

/// <summary><paramref name="Ok"/> holds when no line failed.</summary>
public sealed record RecordCheckResult(bool Ok, IReadOnlyList<RecordCheckLine> Lines);

/// <summary>
/// #1509 (Task 6; part 08 § 1.2, § 10): a storage 12 record, checked in the browser through its sealed document, as the
/// Api's <c>ProvenanceRecordCheck.CheckRecord</c> does: <c>recordHash</c> over the bytes, then per deliverable its
/// <c>composedHash</c> over the retained text and the replay of its substitutions (<see cref="RecordReplay.Verify"/>).
/// A text that is <c>null</c> (dropped) is attested, not a failure. Pure: no I/O.
/// </summary>
public static class RecordCheck31
{
    public const string TextDeleted = "text deleted; hash kept";

    public static RecordCheckResult Check(string? recordHash, byte[] recordBytes, IReadOnlyDictionary<string, string?> composedTexts)
    {
        ArgumentNullException.ThrowIfNull(recordBytes);
        ArgumentNullException.ThrowIfNull(composedTexts);
        var lines = new List<RecordCheckLine>();

        var recomputed = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(recordBytes));
        lines.Add(recordHash == null
            ? new RecordCheckLine("recordHash", null, "the job carries no recordHash")
            : string.Equals(recordHash, recomputed, StringComparison.Ordinal)
                ? new RecordCheckLine("recordHash", true, recomputed)
                : new RecordCheckLine("recordHash", false, $"recorded {recordHash}, recomputed {recomputed}"));

        JsonNode? document;
        try
        {
            document = JsonNode.Parse(recordBytes);
        }
        catch (JsonException)
        {
            document = null;
        }

        if (document is not JsonObject root || root["deliverables"] is not JsonArray deliverables)
        {
            lines.Add(new RecordCheckLine("deliverables", false, "the record is not a canonical document with a deliverables array"));
            return Result(lines);
        }

        foreach (var node in deliverables)
        {
            if (node is not JsonObject row || Str(row["key"]) is not { } key)
            {
                lines.Add(new RecordCheckLine("deliverables[?]", false, "a deliverable row without a key"));
                continue;
            }

            var composedName = $"deliverables[{key}].composedHash";
            var recorded = Str(row["composedHash"]);
            if (!composedTexts.TryGetValue(key, out var text) || text == null)
            {
                lines.Add(new RecordCheckLine(composedName, null, TextDeleted));
                continue;
            }

            var textHash = CanonicalJson.HashText(text);
            lines.Add(recorded == null
                ? new RecordCheckLine(composedName, null, "the row carries no composedHash")
                : string.Equals(recorded, textHash, StringComparison.Ordinal)
                    ? new RecordCheckLine(composedName, true, textHash)
                    : new RecordCheckLine(composedName, false, $"recorded {recorded}, recomputed {textHash}"));

            var replays = RecordReplay.Verify(text, row["substitutions"] as JsonArray ?? [], out var fault);
            lines.Add(new RecordCheckLine($"deliverables[{key}].replay", replays, replays ? "substitutions replay" : fault ?? "replay failed"));
        }

        return Result(lines);
    }

    private static string? Str(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static RecordCheckResult Result(List<RecordCheckLine> lines) => new(lines.All(l => l.Ok != false), lines);
}
