using Azure.Core;
using Azure.Data.Tables;
using Consultologist.Api.Auth;
using Microsoft.Extensions.Configuration;

namespace Consultologist.Api.Email;

public enum EmailSenderMatchOutcome
{
    Matched,
    NoMatch,
    Ambiguous,
    NotActive
}

public sealed record EmailSenderMatch(
    EmailSenderMatchOutcome Outcome,
    string? AppUserId = null);

public interface IEmailSenderResolver
{
    Task<EmailSenderMatch> ResolveAsync(string fromAddress, CancellationToken cancellationToken);
}

/// <summary>
/// The sender gate (#158): the From address must match exactly one AppUser,
/// and that account must be Active. Account emails come from token claims and
/// are NOT unique, so ambiguity is an explicit rejection, never a guess. This
/// is a partition scan — fine at current account counts; an EmailIndex table
/// is the noted follow-up if that changes (docs/CONFIGURATION.md).
/// </summary>
public sealed class TableEmailSenderResolver : IEmailSenderResolver
{
    private readonly TableClient _appUsers;

    public TableEmailSenderResolver(IConfiguration configuration, TokenCredential credential)
    {
        _appUsers = StorageTables.CreateClient(configuration, credential, "AppUsers", "AccountStorage");
    }

    public async Task<EmailSenderMatch> ResolveAsync(string fromAddress, CancellationToken cancellationToken)
    {
        var users = new List<(string AppUserId, string? Email, string Status)>();

        await foreach (var entity in _appUsers.QueryAsync<AppUserEntity>(
                           user => user.PartitionKey == "app-user",
                           select: new[] { "RowKey", "Email", "Status" },
                           cancellationToken: cancellationToken))
        {
            users.Add((entity.RowKey, entity.Email, entity.Status));
        }

        return Decide(users, fromAddress);
    }

    internal static EmailSenderMatch Decide(
        IReadOnlyList<(string AppUserId, string? Email, string Status)> users,
        string fromAddress)
    {
        var normalized = Normalize(fromAddress);

        if (normalized.Length == 0)
        {
            return new EmailSenderMatch(EmailSenderMatchOutcome.NoMatch);
        }

        var candidates = users
            .Where(user => Normalize(user.Email) == normalized)
            .DistinctBy(user => user.AppUserId, StringComparer.Ordinal)
            .ToList();

        if (candidates.Count == 0)
        {
            return new EmailSenderMatch(EmailSenderMatchOutcome.NoMatch);
        }

        if (candidates.Count > 1)
        {
            return new EmailSenderMatch(EmailSenderMatchOutcome.Ambiguous);
        }

        var candidate = candidates[0];

        // #195: the other consult-creation door, and it asks the same question
        // the HTTP one now asks — an Unverified account may read what it made
        // and may not make more, whichever way it arrives.
        return string.Equals(candidate.Status, AccountStatuses.Active, StringComparison.Ordinal)
            ? new EmailSenderMatch(EmailSenderMatchOutcome.Matched, candidate.AppUserId)
            : new EmailSenderMatch(EmailSenderMatchOutcome.NotActive);
    }

    private static string Normalize(string? email) => email?.Trim().ToLowerInvariant() ?? string.Empty;
}
