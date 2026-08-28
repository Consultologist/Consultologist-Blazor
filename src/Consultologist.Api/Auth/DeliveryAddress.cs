using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Consultologist.Api.Auth;

/// <summary>
/// #486: the verified delivery address. An account sets it once and confirms
/// it with a six-digit code sent to it; nothing is ever sent to an address
/// nobody confirmed. <c>AppUsers.Email</c> is a token claim — unverified and
/// not unique — so it cannot be the address. Pure rules here; the
/// endpoints in <c>Account.cs</c> only sequence them.
/// </summary>
public static class DeliveryAddress
{
    public const int CodeLength = 6;
    public const int MaxAddressLength = 254;
    public const int MaxAttempts = 5;
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(15);
    /// <summary>A second code is not sent within this of the last one.</summary>
    public static readonly TimeSpan ResendInterval = TimeSpan.FromSeconds(60);

    /// <summary>Null when the address is usable; otherwise the reason.</summary>
    public static string? Validate(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return "Address is required.";
        }

        var trimmed = address.Trim();

        if (trimmed.Length > MaxAddressLength)
        {
            return "Address is too long.";
        }

        if (!MailAddress.TryCreate(trimmed, out var parsed)
            || !string.Equals(parsed.Address, trimmed, StringComparison.OrdinalIgnoreCase)
            || !parsed.Host.Contains('.'))
        {
            // A display name, a bare local part, or anything MailAddress
            // would quietly "repair" is refused — the string we store is the
            // string we send to.
            return "Address is not a valid email address.";
        }

        return null;
    }

    public static string Normalize(string address) => address.Trim().ToLowerInvariant();

    /// <summary>
    /// #517: the tenant every personal Microsoft account signs in under. Any
    /// other tenant is an organisation, whose sign-in has already verified
    /// that the mailbox in the token belongs to the signed-in person.
    /// </summary>
    public const string ConsumersTenantId = "9188040d-6c67-4c5b-b112-36a304b66dad";

    public static bool IsOrganisation(AuthenticatedUser user) =>
        !string.IsNullOrWhiteSpace(user.TenantId)
        && !string.Equals(user.TenantId.Trim(), ConsumersTenantId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// #517: may the signed-in email be taken as the delivery address without
    /// a code? Only on an organisation's token — a personal account's address
    /// is verified for Microsoft, not for this delivery channel, and a token
    /// with no tenant had no organisation vouch for it — and only when the
    /// token carries an address the code path would have accepted. The
    /// address is the token's; the caller never supplies one.
    /// </summary>
    public static SignedInDecision SignedInEligibility(AuthenticatedUser user)
    {
        if (!IsOrganisation(user))
        {
            return new SignedInDecision(SignedInOutcome.PersonalAccount, null);
        }

        if (Validate(user.Email) != null)
        {
            return new SignedInDecision(SignedInOutcome.NoEmailClaim, null);
        }

        return new SignedInDecision(SignedInOutcome.Eligible, Normalize(user.Email!));
    }

    /// <summary>The kind of account that signed the token, as Account/Me reports it.</summary>
    public static string SignInKindOf(AuthenticatedUser user) =>
        IsOrganisation(user) ? SignInKinds.Organisation : SignInKinds.Personal;

    /// <summary>Six digits from the CSPRNG, zero-padded. Never <see cref="Random"/>.</summary>
    public static string CreateCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    /// <summary>
    /// SHA-256 of the code salted with the account id, base64url — the same
    /// idiom as AccountStore.CreateSubjectHash. Six digits is small enough that
    /// an unsalted hash would be a lookup table; the attempt cap is the real
    /// defence, the salt just keeps the row meaningless on its own.
    /// </summary>
    public static string HashCode(string appUserId, string code) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(appUserId + ":" + code.Trim())))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool CodeMatches(string appUserId, string code, string codeHash) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(HashCode(appUserId, code)),
            Encoding.UTF8.GetBytes(codeHash));

    public static PendingDeliveryAddress CreatePending(string appUserId, string address, string code, DateTimeOffset now) =>
        new(Normalize(address), HashCode(appUserId, code), now, now + CodeLifetime, 0);

    /// <summary>
    /// The confirm rule. <see cref="ConfirmDecision.Wrong"/> carries the
    /// pending row with one more attempt spent; <see cref="ConfirmDecision.TooManyAttempts"/>
    /// and <see cref="ConfirmDecision.Expired"/> mean the row is to be deleted.
    /// </summary>
    public static ConfirmDecision Decide(PendingDeliveryAddress? pending, string appUserId, string? code, DateTimeOffset now)
    {
        if (pending is null)
        {
            return new ConfirmDecision(ConfirmOutcome.None, null);
        }

        if (now >= pending.ExpiresAtUtc)
        {
            return new ConfirmDecision(ConfirmOutcome.Expired, null);
        }

        if (pending.Attempts >= MaxAttempts)
        {
            return new ConfirmDecision(ConfirmOutcome.TooManyAttempts, null);
        }

        if (!string.IsNullOrWhiteSpace(code) && CodeMatches(appUserId, code, pending.CodeHash))
        {
            return new ConfirmDecision(ConfirmOutcome.Confirmed, pending);
        }

        var spent = pending with { Attempts = pending.Attempts + 1 };
        return spent.Attempts >= MaxAttempts
            ? new ConfirmDecision(ConfirmOutcome.TooManyAttempts, null)
            : new ConfirmDecision(ConfirmOutcome.Wrong, spent);
    }

    /// <summary>The one message the code travels in — the code and nothing about the account.</summary>
    public static (string Subject, string Body) ComposeCodeMessage(string code) =>
        ("Confirm your Consultologist delivery address",
         "Your confirmation code is:\n\n"
         + code + "\n\n"
         + $"Enter it on your Consultologist profile within {(int)CodeLifetime.TotalMinutes} minutes. "
         + "Once confirmed, this address will receive your completed consults.\n\n"
         + "If you did not ask for this, ignore this message — nothing will be sent to this address.");

    public static string Serialize(PendingDeliveryAddress pending) => JsonSerializer.Serialize(pending);

    public static PendingDeliveryAddress? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PendingDeliveryAddress>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed record PendingDeliveryAddress(
    string Address,
    string CodeHash,
    DateTimeOffset SentAtUtc,
    DateTimeOffset ExpiresAtUtc,
    int Attempts);

public enum ConfirmOutcome
{
    None,
    Expired,
    Wrong,
    TooManyAttempts,
    Confirmed
}

public sealed record ConfirmDecision(ConfirmOutcome Outcome, PendingDeliveryAddress? Pending);

public enum SignedInOutcome
{
    PersonalAccount,
    NoEmailClaim,
    Eligible
}

/// <summary>#517: the decision, and the normalised address when eligible.</summary>
public sealed record SignedInDecision(SignedInOutcome Outcome, string? Address);

public sealed record SaveDeliveryAddressRequest(string? Address);

public sealed record ConfirmDeliveryAddressRequest(string? Code);
