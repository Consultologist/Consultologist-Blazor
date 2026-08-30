using System.Text.Json;

namespace Consultologist.Web.Services.Accounts;

/// <summary>
/// #516: the profile's signature blocks — named texts (name, credentials,
/// contact) that belong to the clinician, appended to deliverables a v11
/// package marks signed. One JSON row on the generic settings routes
/// (profile.signatures), PascalCase like PendingDeliveryAddress; the Api
/// reads the same shape (Auth/SignatureBlocks.cs) and the wire format is
/// pinned in both test suites.
///
/// Explicit initialisation: a new account has none, and nothing is appended
/// until a block exists AND is chosen. A dangling ChosenId chooses nobody.
/// </summary>
public static class SignatureBlocks
{
    public const string SettingKey = "profile.signatures";

    public const string ContentType = "application/json";

    public const int MaxBlocks = 5;

    public const int MaxNameLength = 80;

    public const int MaxTextLength = 4000;

    public const int MaxIdLength = 40;

    public sealed record SignatureBlock(string Id, string Name, string Text, DateTimeOffset UpdatedAtUtc);

    public sealed record SignatureBlockSet(List<SignatureBlock> Blocks, string? ChosenId);

    public static SignatureBlockSet Empty() => new(new List<SignatureBlock>(), null);

    /// <summary>Tolerant: absent, blank, or unreadable is an empty set, never an error.</summary>
    public static SignatureBlockSet Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Empty();
        }

        try
        {
            var set = JsonSerializer.Deserialize<SignatureBlockSet>(value);
            return set is { Blocks: not null } ? set : Empty();
        }
        catch (JsonException)
        {
            return Empty();
        }
    }

    public static string Serialize(SignatureBlockSet set) => JsonSerializer.Serialize(set);

    /// <summary>Null unless a block exists AND is chosen — a dangling ChosenId chooses nobody.</summary>
    public static SignatureBlock? Chosen(SignatureBlockSet set) =>
        set.ChosenId == null ? null : set.Blocks.FirstOrDefault(block => block.Id == set.ChosenId);

    /// <summary>
    /// The block's id: a slug of its name — lowercase, non-alphanumeric runs
    /// become one dash, capped, never empty, collisions suffixed -2, -3, …
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
            slug = "signature";
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

    /// <summary>The state line — the unchosen states name their consequence.</summary>
    public static string Describe(SignatureBlockSet set) =>
        Chosen(set) is { } chosen
            ? $"In use: {chosen.Name} — appended to deliverables a package marks signed"
            : set.Blocks.Count > 0
                ? "Not chosen — deliverables a package marks signed are produced unsigned"
                : "No signature blocks — deliverables a package marks signed are produced unsigned";
}
