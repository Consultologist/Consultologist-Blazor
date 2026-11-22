using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Run;

/// <summary>
/// #1506 (specVersion 31, area 09; part 08 § 10; D6): replay of a deliverable's substitutions, by position and never by
/// search. Each substitution says where its text sits in the composed text (<c>start</c>, for <c>size</c> runes); replay
/// holds when each slice hashes to its <c>hash</c> and the substitutions stand in order, none overlapping another.
/// </summary>
public static class RecordReplay
{
    /// <summary>
    /// Whether <paramref name="substitutions"/> (a deliverable row's <c>substitutions</c>) replay against
    /// <paramref name="composedText"/>: each in order after the one before it, its <c>size</c> runes from rune
    /// <paramref name="composedText"/>'s <c>start</c> within the text, and that slice hashing (<see cref="CanonicalJson.HashText"/>)
    /// to its <c>hash</c>. When not, <paramref name="fault"/> names the first that fails: <c>site &lt;n&gt;</c>, with
    /// <c> pass &lt;numbers joined by ,&gt;</c> when it is in a template <c>for</c>, then what failed; else <c>null</c>.
    /// Never throws on the array's content.
    /// </summary>
    public static bool Verify(string composedText, JsonArray substitutions, out string? fault)
    {
        ArgumentNullException.ThrowIfNull(composedText);
        ArgumentNullException.ThrowIfNull(substitutions);

        var runes = composedText.EnumerateRunes().ToList();
        var end = 0;
        for (var i = 0; i < substitutions.Count; i++)
        {
            var substitution = substitutions[i] as JsonObject;
            var name = Name(substitution, i);
            if (Whole(substitution?["start"]) is not { } start || Whole(substitution?["size"]) is not { } size || start < 0 || size < 0
                || SourceLayout.Text(substitution?["hash"]) is not { } hash)
            {
                fault = $"{name}: it does not say where its text sits";
                return false;
            }

            if (start < end)
            {
                fault = $"{name}: it overlaps the substitution before it, or stands before it";
                return false;
            }

            if ((long)start + size > runes.Count)
            {
                fault = $"{name}: it runs past the composed text";
                return false;
            }

            var slice = new StringBuilder();
            for (var r = start; r < start + size; r++)
                slice.Append(runes[r].ToString());
            if (!string.Equals(CanonicalJson.HashText(slice.ToString()), hash, StringComparison.Ordinal))
            {
                fault = $"{name}: its text does not hash to its hash";
                return false;
            }

            end = start + size;
        }

        fault = null;
        return true;
    }

    /// <summary>A substitution as a fault names it: <c>site &lt;n&gt;</c> and its pass, or its index when it has no site.</summary>
    private static string Name(JsonObject? substitution, int index)
    {
        var name = Whole(substitution?["site"]) is { } site
            ? string.Create(CultureInfo.InvariantCulture, $"site {site}")
            : string.Create(CultureInfo.InvariantCulture, $"substitution {index + 1}");
        if (substitution?["pass"] is JsonArray pass && pass.Count > 0)
            name += " pass " + string.Join(',', pass.Select(n => Whole(n)?.ToString(CultureInfo.InvariantCulture) ?? "?"));
        return name;
    }

    private static int? Whole(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == System.Text.Json.JsonValueKind.Number && value.TryGetValue<int>(out var whole) ? whole : null;
}
