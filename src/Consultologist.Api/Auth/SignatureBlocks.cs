using System.Text.Json;

namespace Consultologist.Api.Auth;

/// <summary>
/// #516: the read half of the profile's signature blocks. The Web owns the
/// row (profile.signatures, one JSON value, PascalCase — the
/// PendingDeliveryAddress precedent); the job starter reads it at start to
/// snapshot the chosen block onto a job whose package marks a deliverable
/// signed. The wire format is pinned verbatim in both test suites
/// (tests/SignatureBlocksTests.cs and the Web's SignatureProfileTests) —
/// a casing drift on either side would silently read "none chosen".
///
/// Tolerant like every profile setting: absent, blank, or unreadable is an
/// empty set; a dangling ChosenId chooses nobody — explicit initialisation,
/// never a fallback to the first block.
/// </summary>
public static class SignatureBlocks
{
    public sealed record SignatureBlock(string Id, string Name, string Text, DateTimeOffset UpdatedAtUtc);

    public sealed record SignatureBlockSet(List<SignatureBlock> Blocks, string? ChosenId);

    public static SignatureBlockSet Empty() => new(new List<SignatureBlock>(), null);

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

    public static SignatureBlock? Chosen(SignatureBlockSet set) =>
        set.ChosenId == null ? null : set.Blocks.FirstOrDefault(block => block.Id == set.ChosenId);
}
