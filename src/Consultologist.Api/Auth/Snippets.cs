using System.Text.Json;

namespace Consultologist.Api.Auth;

/// <summary>
/// #561: the read half of the profile's snippet library. The Web owns the
/// row (profile.snippets, one JSON value, PascalCase — the SignatureBlocks
/// precedent); nothing on the Api acts on a snippet today — a snippet acts
/// only in the moment the setup form inserts it, becoming ordinary typed
/// text. This mirror exists so the wire format is pinned verbatim in both
/// test suites (tests/SnippetsTests.cs and the Web's model tests) — a
/// casing drift on either side would silently read "no snippets".
/// </summary>
public static class Snippets
{
    public sealed record Snippet(string Id, string Name, string Text, DateTimeOffset UpdatedAtUtc);

    public sealed record SnippetSet(List<Snippet> Items);

    public static SnippetSet Empty() => new(new List<Snippet>());

    /// <summary>Tolerant: absent, blank, or unreadable is an empty set, never an error.</summary>
    public static SnippetSet Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Empty();
        }

        try
        {
            var set = JsonSerializer.Deserialize<SnippetSet>(value);
            return set is { Items: not null } ? set : Empty();
        }
        catch (JsonException)
        {
            return Empty();
        }
    }
}
