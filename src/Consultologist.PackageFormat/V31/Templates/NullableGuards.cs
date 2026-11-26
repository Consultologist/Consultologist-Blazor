using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1508 (ruled by the owner, Q5 of 2026-11-26; R17): what a header rule (a JSON Logic <c>if</c> that decides whether a body
/// renders at all) asserts is there whenever it holds, so that a read in that body through those objects cannot meet null
/// (A11-009). Only forms that are false when the path read is null count: <c>{ "var": "x" }</c> alone (null is falsy),
/// <c>x !== null</c> or <c>x != null</c>, <c>x === v</c> or <c>x == v</c> with a literal <c>v</c> that is not null (either
/// side), and <c>!!x</c>; each asserts <c>x</c> and every shorter path of it (JSON Logic reads a member of null as null). A
/// <c>pass.number</c> (past any <c>parent</c>s) <c>&gt; n</c> with <c>n</c> at least 1, or <c>&gt;= n</c> with <c>n</c> at
/// least 2, asserts that pass's <c>.previous</c>. <c>and</c> asserts what each of its terms does; anything else asserts
/// nothing. Never throws on any rule; bounded by the rule's depth.
/// </summary>
internal static class NullableGuards
{
    /// <summary>The paths <paramref name="rule"/> asserts are there when it holds, each with its shorter paths.</summary>
    public static IReadOnlyList<string> Asserted(JsonNode? rule)
    {
        var found = new List<string>();
        Walk(rule, found, 0);
        return found.SelectMany(Prefixes).Distinct(StringComparer.Ordinal).ToList();
    }

    private static void Walk(JsonNode? rule, List<string> found, int depth)
    {
        if (depth > SourceBounds.JsonDepth || rule is not JsonObject { Count: 1 } term)
            return;
        var (op, argument) = term.First();
        var args = argument as JsonArray;
        switch (op)
        {
            case "and" when args is not null:
                foreach (var each in args)
                    Walk(each, found, depth + 1);
                return;
            case "var":
                if (Var(term) is { } path)
                    found.Add(path);
                return;
            case "!!" when args is [var inner] && Var(inner) is { } truthy:
                found.Add(truthy);
                return;
            case "!==" or "!=" when args is [var left, var right]:
                if (Var(left) is { } a && right is null)
                    found.Add(a);
                else if (Var(right) is { } b && left is null)
                    found.Add(b);
                return;
            case "===" or "==" when args is [var first, var second]:
                if (Var(first) is { } c && IsValue(second))
                    found.Add(c);
                else if (Var(second) is { } d && IsValue(first))
                    found.Add(d);
                return;
            case ">" or ">=" when args is [var left, JsonValue bound] && Var(left) is { } counted
                && counted.Split('.') is ["pass", .. var parents, "number"] && parents.All(p => p == "parent")
                && bound.TryGetValue<double>(out var n) && (op == ">" ? n >= 1 : n >= 2):
                found.Add(counted[..^"number".Length] + "previous");
                return;
        }
    }

    /// <summary>The path a <c>{ "var": "x" }</c> reads, with no default; null for anything else.</summary>
    private static string? Var(JsonNode? node) =>
        node is JsonObject { Count: 1 } obj && obj["var"] is JsonValue value && value.TryGetValue<string>(out var path) && path.Length > 0 ? path : null;

    /// <summary>Whether <paramref name="node"/> is a literal string, number or boolean (never null).</summary>
    private static bool IsValue(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False;

    /// <summary><paramref name="path"/> and each shorter path of it at a dot, down to two segments.</summary>
    private static IEnumerable<string> Prefixes(string path)
    {
        var segments = path.Split('.');
        for (var length = segments.Length; length >= 2; length--)
            yield return string.Join('.', segments, 0, length);
    }
}
