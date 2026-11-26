using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Harness;

/// <summary>
/// #1508 (comment 6, items 1 and 2; R13, R14): one <c>judgement</c> node that gates a macro 30's model chose, as the 31 source
/// holds it: the classifier's rooted entry key (<c>node.standards.letrozole_prescription_text_applies</c>), the entry key of
/// the agent it stands before and which places the macro (<c>node.standards.section_instructions</c>), the macro as a
/// placement names it (<c>letrozole_prescription_text</c>, <c>notes.red_flags</c>) and the macro's instruction as it renders
/// (its lines joined with <c>\n</c>; its marker renders as its own text).
/// </summary>
public sealed record ChoiceClassifier(string Key, string Placing, string Macro, string Instruction);

/// <summary>
/// #1508 (R13, R14): the macro-choice classifiers of a migrated package, read from its files, so a hand edit that writes one
/// is read alike: each macro whose <c>if</c> holds <c>{ "===": [ { "var": "node.&lt;id&gt;.value.answer" }, true ] }</c>, where
/// <c>&lt;id&gt;</c> is a <c>judgement</c> agent listed immediately before an agent whose prompt places the macro (as the
/// migration writes it, part 10 § 1.2). Never throws on package content: a file that does not read gives nothing.
/// </summary>
public static class ChoiceClassifiers
{
    /// <summary>Every macro-choice classifier of <paramref name="migrated"/>, in the macros' path order, then the order their <c>if</c>s read them.</summary>
    public static IReadOnlyList<ChoiceClassifier> Of(Migration31Result migrated)
    {
        ArgumentNullException.ThrowIfNull(migrated);
        var found = new List<ChoiceClassifier>();
        foreach (var (path, text) in migrated.Files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            if (!path.StartsWith("macros/", StringComparison.Ordinal) || !path.EndsWith(".md", StringComparison.Ordinal) || Header(text) is not { } macro)
                continue;
            var name = path["macros/".Length..^".md".Length].Replace('/', '.');
            var instruction = string.Join("\n", (macro["instruction"] as JsonArray ?? []).Select(l => SourceLayout.Text(l) ?? ""));
            foreach (var id in Answers(macro["if"]))
            {
                if (Classifier(migrated, id, name) is { } at)
                    found.Add(new ChoiceClassifier(at.Key, at.Placing, name, instruction));
            }
        }

        return found;
    }

    /// <summary>The ids whose answer <paramref name="rule"/> tests as <c>=== true</c>, in order.</summary>
    private static IEnumerable<string> Answers(JsonNode? rule)
    {
        if (rule is JsonObject { Count: 1 } test && test["==="] is JsonArray { Count: 2 } pair
            && pair[0] is JsonObject { Count: 1 } read && SourceLayout.Text(read["var"]) is { } var
            && pair[1] is JsonValue yes && yes.TryGetValue<bool>(out var holds) && holds
            && var.Split('.') is ["node", var id, "value", "answer"])
        {
            yield return id;
            yield break;
        }

        foreach (var child in rule switch { JsonObject o => o.Select(p => p.Value), JsonArray a => a.AsEnumerable(), _ => [] })
        {
            foreach (var answered in Answers(child))
                yield return answered;
        }
    }

    /// <summary>The classifier <paramref name="id"/>'s key and its placing agent's, when the files hold it as § 1.2 writes it.</summary>
    private static (string Key, string Placing)? Classifier(Migration31Result migrated, string id, string macro)
    {
        foreach (var (path, text) in migrated.Files)
        {
            if (!path.StartsWith("nodes/", StringComparison.Ordinal) || !path.EndsWith($"/{id}.md", StringComparison.Ordinal)
                || Header(text) is not { } header || SourceLayout.Text(header["kind"]) != "agent" || SourceLayout.Text(header["format"]) != "judgement")
            {
                continue;
            }

            var folder = path[..path.LastIndexOf('/')];
            var index = folder == "nodes" ? Json(migrated.Files.GetValueOrDefault("nodes/index.json")) : Json(migrated.Files.GetValueOrDefault(folder + "/index.json"));
            if (index is null)
                return null;
            var lists = new[] { index["items"], index["nodes"], index["then"]?["nodes"], index["else"]?["nodes"] }
                .Concat((index["branches"] as JsonArray ?? []).Select(b => b?["nodes"]))
                .OfType<JsonArray>();
            foreach (var list in lists)
            {
                var ids = list.Select(SourceLayout.Text).ToList();
                var at = ids.IndexOf(id);
                if (at < 0 || at + 1 >= ids.Count || ids[at + 1] is not { } placing)
                    continue;
                var placingText = migrated.Files.GetValueOrDefault($"{folder}/{placing}.md") ?? "";
                if (!placingText.Contains($"macro.{macro} }}}}", StringComparison.Ordinal))
                    return null;
                var root = folder == "nodes" ? "node" : "node." + folder["nodes/".Length..].Replace('/', '.');
                return ($"{root}.{id}", $"{root}.{placing}");
            }

            return null;
        }

        return null;
    }

    private static JsonObject? Header(string text) => FrontMatter.Split(text) is { } split ? Json(split.Header) : null;

    private static JsonObject? Json(string? text)
    {
        if (text is null)
            return null;
        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
