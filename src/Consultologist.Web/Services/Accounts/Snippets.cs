using System.Text.Json;

namespace Consultologist.Web.Services.Accounts;

/// <summary>
/// #561: the profile's snippet library — clinician-owned canned text (the
/// SmartPhrase/dot-phrase sense of "macro", the personal one), inserted
/// into the setup form's text inputs where it becomes ordinary typed text:
/// part of the effective inputs, hashed and recorded like anything typed,
/// nothing new on the record. Deliberately outside the package grammar
/// (package-format-v11-design.md § 11 names it out) and distinct from the
/// signature blocks beside it. One JSON row on the generic settings routes
/// (profile.snippets), the SignatureBlocks shape; the Api reads the same
/// shape (Auth/Snippets.cs) and the wire format is pinned in both suites.
///
/// Explicit initialisation: a new account has none, and the setup form
/// shows no picker until a snippet exists.
/// </summary>
public static class Snippets
{
    public const string SettingKey = "profile.snippets";

    public const string ContentType = "application/json";

    // 12 × 2000 nominal ≈ 24K serialized — comfortably under the settings
    // routes' 32,000-char value cap even with JSON escaping; the 413 stays
    // the backstop.
    public const int MaxSnippets = 12;

    public const int MaxNameLength = 80;

    public const int MaxTextLength = 2000;

    public const int MaxIdLength = 40;

    public sealed record Snippet(string Id, string Name, string Text, DateTimeOffset UpdatedAtUtc);

    // No ChosenId: nothing is "in use" here — a snippet acts only in the
    // moment it is inserted.
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

    public static string Serialize(SnippetSet set) => JsonSerializer.Serialize(set);

    /// <summary>
    /// The snippet's id: a slug of its name — lowercase, non-alphanumeric
    /// runs become one dash, capped, never empty, collisions suffixed -2, -3, …
    /// (the SignatureBlocks rules, verbatim).
    /// </summary>
    public static string SlugFor(string name, IEnumerable<string> existingIds)
    {
        var builder = new System.Text.StringBuilder();
        var pendingDash = false;

        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (builder.Length >= MaxIdLength)
            {
                break;
            }

            if (char.IsLetterOrDigit(ch))
            {
                if (pendingDash && builder.Length > 0)
                {
                    builder.Append('-');
                }

                pendingDash = false;
                builder.Append(ch);
            }
            else
            {
                pendingDash = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length == 0)
        {
            slug = "snippet";
        }

        var taken = new HashSet<string>(existingIds, StringComparer.Ordinal);
        if (!taken.Contains(slug))
        {
            return slug;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{slug}-{suffix}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>The state line — the empty state says where a snippet would act.</summary>
    public static string Describe(SnippetSet set) =>
        set.Items.Count == 0
            ? "No snippets yet — create one to insert it on the setup form"
            : set.Items.Count == 1
                ? "1 snippet — offered on the setup form's text inputs"
                : $"{set.Items.Count} snippets — offered on the setup form's text inputs";

    /// <summary>
    /// #561: append, as chosen — the snippet lands after the typed text,
    /// blank-line separated, and is ordinary typed text from that moment.
    /// Extracted so it can be asserted directly.
    /// </summary>
    public static string Insert(string? current, string snippetText) =>
        string.IsNullOrWhiteSpace(current)
            ? snippetText
            : current.TrimEnd() + "\n\n" + snippetText;
}
