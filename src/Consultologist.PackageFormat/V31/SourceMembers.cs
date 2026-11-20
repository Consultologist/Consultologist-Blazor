using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1499 (specVersion 31, area 02): the "every member is written" rules of part 01 § 5 for one
/// declaration against its <see cref="DeclarationShape"/>: a missing member, a <c>null</c> where
/// none is allowed, a wrong type, an unknown member, then the description and label rules. It
/// never throws, whatever the object holds.
/// </summary>
public static class SourceMembers
{
    /// <summary>
    /// Checks <paramref name="declaration"/> against <paramref name="shape"/> and adds its refusals to
    /// <paramref name="refusals"/>. <paramref name="name"/> is the <c>&lt;Declaration&gt;</c> of D9 and
    /// <paramref name="path"/> the file. Returns true when it added nothing.
    /// </summary>
    public static bool Check(JsonObject declaration, DeclarationShape shape, string name, string path, List<SourceRefusal> refusals)
    {
        var before = refusals.Count;

        foreach (var member in shape.Members)
        {
            if (!declaration.TryGetPropertyValue(member.Name, out var value))
            {
                refusals.Add(SourceSentences.MemberMissing(name, member.Name, path));
            }
            else if (value is null)
            {
                if (!member.Nullable)
                    refusals.Add(SourceSentences.MemberNull(name, member.Name, path));
            }
            else if (!Fits(value, member.Type))
            {
                refusals.Add(SourceSentences.MemberWrongType(name, member.Name, Describe(value), MustBe(member.Type), path));
            }
        }

        foreach (var (key, _) in declaration)
        {
            if (shape.Members.All(m => m.Name != key))
                refusals.Add(SourceSentences.MemberUnknown(name, path, key, Closest(shape, key)));
        }

        if (declaration.TryGetPropertyValue("description", out var description)
            && shape.Members.Any(m => m.Name == "description" && m.Type == MemberType.Lines)
            && description is JsonArray lines && Fits(lines, MemberType.Lines))
        {
            if (lines.Count == 0)
            {
                refusals.Add(SourceSentences.DescriptionEmpty(name, path));
            }
            else
            {
                var count = CodePoints(string.Join("\n", lines.Select(l => ReadText(l!))));
                if (count > 500)
                    refusals.Add(SourceSentences.DescriptionTooLong(name, count, path));
            }
        }

        if (declaration.TryGetPropertyValue("label", out var label)
            && shape.Members.Any(m => m.Name == "label" && m.Type == MemberType.Text)
            && label is JsonValue && Kind(label) == JsonValueKind.String && string.IsNullOrWhiteSpace(ReadText(label)))
        {
            refusals.Add(SourceSentences.LabelEmpty(name, path));
        }

        return refusals.Count == before;
    }

    // A string with a lone surrogate escape ("\ud800") parses but cannot be read back; it counts as one
    // replacement character, which is neither blank nor long enough to matter.
    private static string ReadText(JsonNode node)
    {
        try
        {
            return node.GetValue<string>();
        }
        catch (InvalidOperationException)
        {
            return "\uFFFD";
        }
    }

    private static int CodePoints(string text)
    {
        var count = 0;
        foreach (var _ in text.EnumerateRunes())
            count++;
        return count;
    }

    private static bool Fits(JsonNode value, MemberType type) => type switch
    {
        MemberType.Any or MemberType.Condition => true,
        MemberType.Text or MemberType.Reference => Kind(value) == JsonValueKind.String,
        MemberType.Boolean => Kind(value) is JsonValueKind.True or JsonValueKind.False,
        MemberType.Integer => Kind(value) == JsonValueKind.Number && IsWhole(value),
        MemberType.Object => value is JsonObject,
        MemberType.Format => value is JsonObject || Kind(value) == JsonValueKind.String,
        MemberType.Lines or MemberType.Ids =>
            value is JsonArray items && items.All(i => i is not null && Kind(i) == JsonValueKind.String),
        _ => true,
    };

    private static JsonValueKind Kind(JsonNode node) => node.GetValueKind();

    private static bool IsWhole(JsonNode value)
    {
        if (value is JsonValue v && v.TryGetValue<decimal>(out var d))
            return d == decimal.Truncate(d);
        var written = value.ToJsonString();
        return written.IndexOfAny(['.', 'e', 'E']) < 0;
    }

    /// <summary>A02-001's <c>&lt;what it is&gt;</c>.</summary>
    private static string Describe(JsonNode value) => value switch
    {
        JsonArray => "a list",
        JsonObject => "an object",
        _ => Kind(value) switch
        {
            JsonValueKind.String => "text",
            JsonValueKind.True or JsonValueKind.False => "true or false",
            JsonValueKind.Number => IsWhole(value) ? "a whole number" : "a number",
            _ => "null",
        },
    };

    /// <summary>A02-001's <c>&lt;what it must be&gt;</c>.</summary>
    private static string MustBe(MemberType type) => type switch
    {
        MemberType.Text or MemberType.Reference => "text",
        MemberType.Boolean => "true or false",
        MemberType.Integer => "a whole number",
        MemberType.Lines or MemberType.Ids => "a list of text",
        MemberType.Object => "an object",
        MemberType.Format => "text or an object",
        _ => "text",
    };

    /// <summary>D20: the member with the smallest Levenshtein distance, the first in table order on a tie.</summary>
    private static string Closest(DeclarationShape shape, string unknown)
    {
        var best = shape.Members[0].Name;
        var bestDistance = int.MaxValue;
        foreach (var member in shape.Members)
        {
            var distance = Levenshtein(member.Name, unknown);
            if (distance < bestDistance)
            {
                best = member.Name;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static int Levenshtein(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
