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
    /// <param name="leave">
    /// Members whose fault has a sentence of its own, which the caller says instead (part 04 § 10.1:
    /// a flow node's <c>null</c> label, a member of another flow kind, a parameter's <c>required</c>).
    /// For a member of the shape, only its absence is still said here (S01-021); its <c>null</c> and
    /// its type are the caller's (<see cref="CheckType"/>). A member outside the shape is not said
    /// to be unknown (S01-023).
    /// </param>
    public static bool Check(
        JsonObject declaration, DeclarationShape shape, string name, string path, List<SourceRefusal> refusals,
        IReadOnlyCollection<string>? leave = null)
    {
        var before = refusals.Count;
        leave ??= [];

        foreach (var member in shape.Members)
        {
            if (!declaration.TryGetPropertyValue(member.Name, out var value))
            {
                refusals.Add(SourceSentences.MemberMissing(name, member.Name, path));
            }
            else if (leave.Contains(member.Name))
            {
                continue;
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
            if (shape.Members.All(m => m.Name != key) && !leave.Contains(key))
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

    /// <summary>
    /// The type rule alone (A02-001) for one member the caller left out of <see cref="Check"/>:
    /// nothing for <c>null</c> or a missing member, which the caller judges. True when it added nothing.
    /// </summary>
    public static bool CheckType(JsonNode? value, MemberType type, string name, string member, string path, List<SourceRefusal> refusals)
    {
        if (value is null || Fits(value, type))
            return true;
        refusals.Add(SourceSentences.MemberWrongType(name, member, Describe(value), MustBe(type), path));
        return false;
    }

    // A string with a lone surrogate escape ("\ud800") cannot be read back; it counts as one replacement
    // character, which is neither blank nor long enough to matter. StrictJson refuses such a string, so
    // the parser never reaches this; Check is public, though, and may be given an object that
    // System.Text.Json parsed (SourceMembersTests.ALoneSurrogate_InAnObjectNotReadByStrictJson_IsOneCharacter).
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

    /// <summary>
    /// A number with no fraction, judged on what the reader holds exactly: a value <see cref="decimal"/>
    /// holds (so <c>1.0</c> and <c>2e0</c> are whole), or one written without a point or an exponent.
    /// <c>1e30</c> is past both, so it is not a whole number. The flow reader judges its bounds by this too.
    /// </summary>
    internal static bool IsWhole(JsonNode value)
    {
        if (value is JsonValue v && v.TryGetValue<decimal>(out var d))
            return d == decimal.Truncate(d);
        var written = value.ToJsonString();
        return written.IndexOfAny(['.', 'e', 'E']) < 0;
    }

    /// <summary>A02-001's <c>&lt;what it is&gt;</c>; <c>null</c> for a JSON <c>null</c>.</summary>
    internal static string Describe(JsonNode? value) => value switch
    {
        null => "null",
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
