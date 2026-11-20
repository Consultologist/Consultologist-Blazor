using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.Api.Tests.ByteEquivalence;

/// <summary>
/// #1498: the capture document, <c>captures/&lt;package&gt;/&lt;scenario&gt;.json</c> — what one
/// scenario of one package did on today's engine: its status and failure, every stub served,
/// every prompt sent, every render, and every deliverable's final text, each text beside its
/// hash. Written one way, so two runs of the same scenario are the same bytes.
/// </summary>
internal static class PackageCapture
{
    private static readonly JsonSerializerOptions Style = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NewLine = "\n",
    };

    /// <summary>The capture document, in the capture JSON style: members in order, <c>\n</c> newlines, one trailing <c>\n</c>.</summary>
    public static string Write(string package, string? tag, string scenario, CaptureRun run)
    {
        var outputs = new JsonObject();
        foreach (var (key, value) in run.Outputs)
        {
            outputs.Add(key, value?.DeepClone());
        }

        var document = new JsonObject
        {
            ["package"] = package,
            ["tag"] = tag,
            ["scenario"] = scenario,
            ["status"] = run.Status,
            ["failure"] = run.Failure,
            ["outputs"] = outputs,
            ["prompts"] = Texts(run.Prompts),
            ["renders"] = Texts(run.Renders),
            ["deliverables"] = new JsonArray(run.Deliverables.Select(deliverable => (JsonNode)new JsonObject
            {
                ["id"] = deliverable.Id,
                ["state"] = deliverable.State,
                ["reason"] = deliverable.Reason,
                ["hash"] = deliverable.Text is null ? null : ByteEquivalenceFixtures.HashOf(deliverable.Text),
                ["text"] = deliverable.Text,
            }).ToArray()),
        };

        return document.ToJsonString(Style) + "\n";
    }

    private static JsonArray Texts(IReadOnlyList<(string Key, string Text)> texts) =>
        new(texts.Select(entry => (JsonNode)new JsonObject
        {
            ["key"] = entry.Key,
            ["hash"] = ByteEquivalenceFixtures.HashOf(entry.Text),
            ["text"] = entry.Text,
        }).ToArray());
}
