using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Auth;

public enum IdentityLinkOutcome
{
    Linked,
    AlreadyLinkedToSelf,
    ConflictOtherUser
}

public interface IAccountStore
{
    Task<AppAccount> ResolveOrCreateAsync(AuthenticatedUser user, CancellationToken cancellationToken);

    Task<IdentityLinkOutcome> LinkIdentityAsync(
        string appUserId,
        string provider,
        string issuer,
        string subject,
        string? displayName,
        string? email,
        string? pictureUrl,
        string? verifiedCategories,
        CancellationToken cancellationToken);

    /// <summary>#195: remove a linked identity from the caller's own account.</summary>
    Task UnlinkIdentityAsync(string appUserId, string provider, CancellationToken cancellationToken);

    /// <summary>
    /// #384: every account, id and status only. A partition scan of AppUsers —
    /// fine at current account counts, as the email sender gate's is; the
    /// noted follow-up if that changes is the same index table.
    /// </summary>
    Task<IReadOnlyList<AccountSummary>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// v11 #513: one account's display name, or null when the account does not
    /// exist — the starter snapshots it at job start for macro expansion
    /// (profile:name), the way EmailRequested is chosen at start.
    /// </summary>
    Task<string?> GetDisplayNameAsync(string appUserId, CancellationToken cancellationToken);

    /// <summary>#557: the account's stored kind, for the outputs container choice at job start.</summary>
    Task<string?> GetAccountKindAsync(string appUserId, CancellationToken cancellationToken);

    /// <summary>#559: one account's status, or null when it does not exist — the closure gate reads it fresh.</summary>
    Task<string?> TryGetStatusAsync(string appUserId, CancellationToken cancellationToken);

    /// <summary>
    /// #559: the closure's final steps — every identity pair (walked from
    /// UserIdentityLinks, the only place the IdentityLinks keys are
    /// recoverable; no provider guard, unlike #195's unlink) and then the
    /// AppUsers row, so the sign-in that created the account starts a new,
    /// Pending one if it returns. Returns how many identity links went.
    /// </summary>
    Task<int> DeleteAccountAsync(string appUserId, CancellationToken cancellationToken);

    /// <summary>
    /// #553: every account with the display name it already carries — the
    /// same AppUsers partition scan as ListAsync, projection widened. The
    /// operator panel's join; numbers and ids elsewhere, names only from
    /// here.
    /// </summary>
    Task<IReadOnlyList<AccountDirectoryEntry>> ListDirectoryAsync(CancellationToken cancellationToken);

    /// <summary>
    /// #553: the tenant of the account's Entra identity, parsed from the
    /// stored issuer — null when no identity row resolves to one. One
    /// identity-partition read per account; fine at current counts, the
    /// ListAsync argument.
    /// </summary>
    Task<string?> GetTenantIdAsync(string appUserId, CancellationToken cancellationToken);
}

public sealed record AccountSummary(string AppUserId, string Status);

/// <summary>#553: an account as the operator panel joins it — id, the stored display name, status, kind.</summary>
public sealed record AccountDirectoryEntry(string AppUserId, string DisplayName, string Status, string? AccountKind);

public sealed class AccountStore : IAccountStore
{
    private const string AppUsersTableName = "AppUsers";
    private const string IdentityLinksTableName = "IdentityLinks";
    private const string UserIdentityLinksTableName = "UserIdentityLinks";
    private readonly TableClient _appUsers;
    private readonly TableClient _identityLinks;
    private readonly TableClient _userIdentityLinks;
    private readonly ILogger<AccountStore> _logger;

    public AccountStore(IConfiguration configuration, TokenCredential credential, ILogger<AccountStore> logger)
    {
        _logger = logger;
        _appUsers = StorageTables.CreateClient(configuration, credential, AppUsersTableName, "AccountStorage");
        _identityLinks = StorageTables.CreateClient(configuration, credential, IdentityLinksTableName, "AccountStorage");
        _userIdentityLinks = StorageTables.CreateClient(configuration, credential, UserIdentityLinksTableName, "AccountStorage");
    }

    public async Task<IReadOnlyList<AccountSummary>> ListAsync(CancellationToken cancellationToken)
    {
        await EnsureTablesAsync(cancellationToken);

        var accounts = new List<AccountSummary>();
        await foreach (var entity in _appUsers.QueryAsync<AppUserEntity>(
                           user => user.PartitionKey == "app-user",
                           select: new[] { "RowKey", "Status" },
                           cancellationToken: cancellationToken))
        {
            accounts.Add(new AccountSummary(entity.RowKey, entity.Status));
        }

        return accounts;
    }

    public async Task<IReadOnlyList<AccountDirectoryEntry>> ListDirectoryAsync(CancellationToken cancellationToken)
    {
        await EnsureTablesAsync(cancellationToken);

        var accounts = new List<AccountDirectoryEntry>();
        await foreach (var entity in _appUsers.QueryAsync<AppUserEntity>(
                           user => user.PartitionKey == "app-user",
                           select: new[] { "RowKey", "DisplayName", "Status", "AccountKind" },
                           cancellationToken: cancellationToken))
        {
            accounts.Add(new AccountDirectoryEntry(entity.RowKey, entity.DisplayName, entity.Status, entity.AccountKind));
        }

        return accounts;
    }

    public async Task<string?> GetTenantIdAsync(string appUserId, CancellationToken cancellationToken)
    {
        await EnsureTablesAsync(cancellationToken);

        await foreach (var link in _userIdentityLinks.QueryAsync<UserIdentityLinkEntity>(
                           row => row.PartitionKey == appUserId,
                           cancellationToken: cancellationToken))
        {
            if (string.Equals(link.Provider, IdentityProviders.EntraExternalId, StringComparison.Ordinal)
                && TenantIds.TenantIdOf(link.Issuer) is { } tenantId)
            {
                return tenantId;
            }
        }

        return null;
    }

    public async Task<AppAccount> ResolveOrCreateAsync(AuthenticatedUser user, CancellationToken cancellationToken)
    {
        await EnsureTablesAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var subjectHash = CreateSubjectHash(user.Provider, user.Issuer, user.Subject);
        var linkPartitionKey = user.Provider;
        var linkRowKey = subjectHash;
        IdentityLinkEntity? identityLink = null;

        try
        {
            var identityLinkResponse = await _identityLinks.GetEntityAsync<IdentityLinkEntity>(
                linkPartitionKey,
                linkRowKey,
                cancellationToken: cancellationToken);
            identityLink = identityLinkResponse.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            var appUserId = Guid.NewGuid().ToString("N");
            var newAppUser = new AppUserEntity
            {
                RowKey = appUserId,
                DisplayName = user.DisplayName,
                Email = user.Email,
                Status = AccountStatuses.Pending,
                // #556: decided once, from the tenant that signed this first
                // sign-in (storage-separation.md § 2.5).
                AccountKind = KindFor(user),
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                LastSeenAtUtc = now
            };

            identityLink = new IdentityLinkEntity
            {
                PartitionKey = linkPartitionKey,
                RowKey = linkRowKey,
                AppUserId = appUserId,
                Provider = user.Provider,
                Issuer = user.Issuer,
                Subject = user.Subject,
                SubjectHash = subjectHash,
                LinkedAtUtc = now,
                LastSeenAtUtc = now
            };

            await _appUsers.UpsertEntityAsync(newAppUser, TableUpdateMode.Replace, cancellationToken);
            await _identityLinks.UpsertEntityAsync(identityLink, TableUpdateMode.Replace, cancellationToken);
            await _userIdentityLinks.UpsertEntityAsync(ToUserIdentityLink(identityLink), TableUpdateMode.Replace, cancellationToken);

            _logger.LogInformation(
                "Created app user from identity. AppUserId={AppUserId}, Provider={Provider}, Issuer={Issuer}",
                appUserId,
                user.Provider,
                user.Issuer);
        }

        identityLink.LastSeenAtUtc = now;
        await _identityLinks.UpsertEntityAsync(identityLink, TableUpdateMode.Replace, cancellationToken);
        await _userIdentityLinks.UpsertEntityAsync(ToUserIdentityLink(identityLink), TableUpdateMode.Replace, cancellationToken);

        var appUserEntity = await _appUsers.GetEntityAsync<AppUserEntity>(
            "app-user",
            identityLink.AppUserId,
            cancellationToken: cancellationToken);

        var appUser = appUserEntity.Value;
        appUser.DisplayName = string.IsNullOrWhiteSpace(appUser.DisplayName) ? user.DisplayName : appUser.DisplayName;
        appUser.Email = appUser.Email ?? user.Email;
        // #556: the lazy back-fill — a pre-#556 row gains its kind at the next
        // sign-in; a stamped kind is never overwritten (the account cannot
        // change tenant, and the store keys containers on this).
        appUser.AccountKind = StampedKind(appUser.AccountKind, user);
        appUser.UpdatedAtUtc = now;
        appUser.LastSeenAtUtc = now;

        await _appUsers.UpsertEntityAsync(appUser, TableUpdateMode.Replace, cancellationToken);

        var linkedIdentities = await GetLinkedIdentitiesAsync(appUser.RowKey, cancellationToken);
        var currentIdentity = new AccountIdentity(
            identityLink.Provider,
            identityLink.Issuer,
            identityLink.Subject,
            identityLink.LinkedAtUtc,
            identityLink.LastSeenAtUtc);

        return new AppAccount(
            appUser.RowKey,
            appUser.DisplayName,
            appUser.Email,
            appUser.Status,
            currentIdentity,
            linkedIdentities,
            appUser.AccountKind);
    }

    /// <summary>
    /// #556: the account's kind, decided from the signing tenant — the one
    /// rule (#517's): the consumers tenant, or no tenant, is personal;
    /// everything else is an organisation. The same words SignInKinds owns,
    /// because the two agree by construction for a single-identity account.
    /// </summary>
    internal static string KindFor(AuthenticatedUser user) => DeliveryAddress.SignInKindOf(user);

    /// <summary>#556: the back-fill's whole rule — fill once, never overwrite.</summary>
    internal static string StampedKind(string? existing, AuthenticatedUser user) => existing ?? KindFor(user);

    public async Task<IdentityLinkOutcome> LinkIdentityAsync(
        string appUserId,
        string provider,
        string issuer,
        string subject,
        string? displayName,
        string? email,
        string? pictureUrl,
        string? verifiedCategories,
        CancellationToken cancellationToken)
    {
        await EnsureTablesAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var subjectHash = CreateSubjectHash(provider, issuer, subject);
        IdentityLinkEntity? existing = null;

        try
        {
            var response = await _identityLinks.GetEntityAsync<IdentityLinkEntity>(
                provider,
                subjectHash,
                cancellationToken: cancellationToken);
            existing = response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
        }

        var outcome = DecideLinkOutcome(existing, appUserId);

        if (outcome == IdentityLinkOutcome.ConflictOtherUser)
        {
            _logger.LogWarning(
                "Identity link refused: already attached to another account. Provider={Provider}, AppUserId={AppUserId}",
                provider,
                appUserId);
            return outcome;
        }

        var identityLink = new IdentityLinkEntity
        {
            PartitionKey = provider,
            RowKey = subjectHash,
            AppUserId = appUserId,
            Provider = provider,
            Issuer = issuer,
            Subject = subject,
            SubjectHash = subjectHash,
            LinkedAtUtc = existing?.LinkedAtUtc ?? now,
            LastSeenAtUtc = now,
            DisplayName = displayName,
            Email = email,
            PictureUrl = pictureUrl,
            VerifiedCategories = verifiedCategories
        };

        await _identityLinks.UpsertEntityAsync(identityLink, TableUpdateMode.Replace, cancellationToken);
        await _userIdentityLinks.UpsertEntityAsync(ToUserIdentityLink(identityLink), TableUpdateMode.Replace, cancellationToken);

        await ApplyStatusAsync(appUserId, StatusAfterLink, cancellationToken);

        _logger.LogInformation(
            "Linked identity to app user. AppUserId={AppUserId}, Provider={Provider}, Outcome={Outcome}",
            appUserId,
            provider,
            outcome);

        return outcome;
    }

    public async Task UnlinkIdentityAsync(string appUserId, string provider, CancellationToken cancellationToken)
    {
        // The credential identity is how the account signs in. Removing it
        // would orphan the account from its own sign-in with no way back, so
        // this is a guard here rather than a caller responsibility.
        if (string.Equals(provider, IdentityProviders.EntraExternalId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The sign-in identity cannot be unlinked; removing it would orphan the account.");
        }

        await EnsureTablesAsync(cancellationToken);

        // UserIdentityLinks is the only place the pair of keys can be derived
        // from: IdentityLinks is keyed by the subject hash, which nothing else
        // records against the user.
        var prefix = $"{provider}-";
        var links = _userIdentityLinks.QueryAsync<UserIdentityLinkEntity>(
            link => link.PartitionKey == appUserId, cancellationToken: cancellationToken);

        var removed = 0;

        await foreach (var link in links)
        {
            if (!link.RowKey.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            // A missing row is success: the caller asked for it to be gone.
            await _identityLinks.DeleteEntityAsync(provider, link.SubjectHash, cancellationToken: cancellationToken);
            await _userIdentityLinks.DeleteEntityAsync(appUserId, link.RowKey, cancellationToken: cancellationToken);
            removed++;
        }

        if (removed == 0)
        {
            return;
        }

        await ApplyStatusAsync(appUserId, StatusAfterUnlink, cancellationToken);

        _logger.LogInformation(
            "Unlinked identity from app user. AppUserId={AppUserId}, Provider={Provider}, Rows={Rows}",
            appUserId,
            provider,
            removed);
    }

    /// <summary>
    /// #195: linking is the activation signal, so a successful link activates.
    /// Pending as well as Unverified — the manual operator flip becomes the
    /// fallback rather than the path (docs/ACCOUNTS.md). Disabled is not
    /// something a user may lift by linking.
    /// </summary>
    internal static string StatusAfterLink(string current) =>
        current is AccountStatuses.Pending or AccountStatuses.Unverified
            ? AccountStatuses.Active
            : current;

    /// <summary>
    /// #195: withdrawing the evidence withdraws the activation it justified,
    /// but not the account. Only from Active: an account that was never
    /// activated does not become Unverified by unlinking, and Disabled stays.
    /// </summary>
    public async Task<string?> GetDisplayNameAsync(string appUserId, CancellationToken cancellationToken)
    {
        var response = await _appUsers.GetEntityIfExistsAsync<AppUserEntity>(
            "app-user", appUserId, cancellationToken: cancellationToken);

        return response.HasValue ? response.Value!.DisplayName : null;
    }

    /// <summary>
    /// #557: the account's stored kind, or null for an account that does not
    /// exist — the starter snapshots it at job start so the outputs blob
    /// lands in the container the kind names (storage-separation.md § 2.5).
    /// </summary>
    public async Task<string?> GetAccountKindAsync(string appUserId, CancellationToken cancellationToken)
    {
        var response = await _appUsers.GetEntityIfExistsAsync<AppUserEntity>(
            "app-user", appUserId, cancellationToken: cancellationToken);

        return response.HasValue ? response.Value!.AccountKind : null;
    }

    public async Task<string?> TryGetStatusAsync(string appUserId, CancellationToken cancellationToken)
    {
        var response = await _appUsers.GetEntityIfExistsAsync<AppUserEntity>(
            "app-user", appUserId, cancellationToken: cancellationToken);

        return response.HasValue ? response.Value!.Status : null;
    }

    public async Task<int> DeleteAccountAsync(string appUserId, CancellationToken cancellationToken)
    {
        // Both link rows per identity, no provider filter: closure deletes
        // the sign-in identity deliberately — the one thing #195's unlink
        // exists to prevent.
        var deleted = 0;
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {appUserId}");
        await foreach (var link in _userIdentityLinks.QueryAsync<UserIdentityLinkEntity>(filter, cancellationToken: cancellationToken))
        {
            try
            {
                await _identityLinks.DeleteEntityAsync(link.Provider, link.SubjectHash, cancellationToken: cancellationToken);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
            }

            try
            {
                await _userIdentityLinks.DeleteEntityAsync(link.PartitionKey, link.RowKey, cancellationToken: cancellationToken);
                deleted++;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
            }
        }

        try
        {
            await _appUsers.DeleteEntityAsync("app-user", appUserId, cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
        }

        _logger.LogInformation("Account row and identities deleted. AppUserId={AppUserId}, IdentityLinks={Count}", appUserId, deleted);
        return deleted;
    }

    internal static string StatusAfterUnlink(string current) =>
        current == AccountStatuses.Active ? AccountStatuses.Unverified : current;

    private async Task ApplyStatusAsync(
        string appUserId, Func<string, string> transition, CancellationToken cancellationToken)
    {
        var response = await _appUsers.GetEntityAsync<AppUserEntity>(
            "app-user", appUserId, cancellationToken: cancellationToken);

        var appUser = response.Value;
        var next = transition(appUser.Status);

        if (string.Equals(next, appUser.Status, StringComparison.Ordinal))
        {
            return;
        }

        _logger.LogInformation(
            "Account status changed. AppUserId={AppUserId}, From={From}, To={To}",
            appUserId,
            appUser.Status,
            next);

        appUser.Status = next;
        appUser.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _appUsers.UpsertEntityAsync(appUser, TableUpdateMode.Replace, cancellationToken);
    }

    internal static IdentityLinkOutcome DecideLinkOutcome(IdentityLinkEntity? existing, string appUserId)
    {
        if (existing == null)
        {
            return IdentityLinkOutcome.Linked;
        }

        return string.Equals(existing.AppUserId, appUserId, StringComparison.Ordinal)
            ? IdentityLinkOutcome.AlreadyLinkedToSelf
            : IdentityLinkOutcome.ConflictOtherUser;
    }

    private async Task EnsureTablesAsync(CancellationToken cancellationToken)
    {
        await _appUsers.CreateIfNotExistsAsync(cancellationToken);
        await _identityLinks.CreateIfNotExistsAsync(cancellationToken);
        await _userIdentityLinks.CreateIfNotExistsAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<AccountIdentity>> GetLinkedIdentitiesAsync(
        string appUserId,
        CancellationToken cancellationToken)
    {
        var identities = new List<AccountIdentity>();

        await foreach (var entity in _userIdentityLinks.QueryAsync<UserIdentityLinkEntity>(
                           link => link.PartitionKey == appUserId,
                           cancellationToken: cancellationToken))
        {
            identities.Add(new AccountIdentity(
                entity.Provider,
                entity.Issuer,
                entity.SubjectHash,
                entity.LinkedAtUtc,
                entity.LastSeenAtUtc,
                entity.DisplayName,
                entity.Email,
                entity.PictureUrl,
                entity.VerifiedCategories));
        }

        return identities;
    }

    private static UserIdentityLinkEntity ToUserIdentityLink(IdentityLinkEntity identityLink)
    {
        return new UserIdentityLinkEntity
        {
            PartitionKey = identityLink.AppUserId,
            RowKey = $"{identityLink.Provider}-{identityLink.SubjectHash}",
            Provider = identityLink.Provider,
            Issuer = identityLink.Issuer,
            SubjectHash = identityLink.SubjectHash,
            LinkedAtUtc = identityLink.LinkedAtUtc,
            LastSeenAtUtc = identityLink.LastSeenAtUtc,
            DisplayName = identityLink.DisplayName,
            Email = identityLink.Email,
            PictureUrl = identityLink.PictureUrl,
            VerifiedCategories = identityLink.VerifiedCategories
        };
    }

    internal static string CreateSubjectHash(string provider, string issuer, string subject)
    {
        var input = $"{provider}|{issuer}|{subject}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
