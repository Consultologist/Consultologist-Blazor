using Consultologist.PackageFormat;

namespace Consultologist.Api.Workflow;

/// <summary>
/// #384: every account's pinned package, resolved through the store with the
/// loaded catalog, grouped by the ref it resolved to. Package refs, schema
/// ids and catalog refs are the content — no PHI. AppUserIds appear only on
/// entries that need a remedy, which is what an operator holds them for.
/// The property set is pinned by test.
/// </summary>
public sealed record PinHealthResponse(
    string Catalog,
    string? Engine,
    DateTimeOffset GeneratedAtUtc,
    int Accounts,
    IReadOnlyList<PinHealthEntry> Pins);

public sealed record PinHealthEntry(
    string Ref,
    string Status,
    string? Reason,
    int Accounts,
    IReadOnlyList<string>? AppUserIds);

public static class PinHealthStatuses
{
    /// <summary>The store resolved it: a consult on this pin would start.</summary>
    public const string Healthy = "Healthy";

    /// <summary>The store refused the content — the catalog moved, or the version cannot run here. Reason is the store's own sentence.</summary>
    public const string Stranded = "Stranded";

    /// <summary>The store could not read it at all. Reason names the failure's type only.</summary>
    public const string Unreadable = "Unreadable";
}

public static class PinHealth
{
    /// <summary>The ref an account lands under when its own pin could not be resolved.</summary>
    public const string UnresolvedRef = "(unresolved)";

    public static PinHealthResponse Assemble(
        string catalogRef,
        string? engineCommit,
        IReadOnlyList<(string AppUserId, string Ref)> pins,
        IReadOnlyDictionary<string, (string Status, string? Reason)> outcomes,
        DateTimeOffset now)
    {
        var entries = pins
            .GroupBy(pin => pin.Ref, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var (status, reason) = outcomes.TryGetValue(group.Key, out var outcome)
                    ? outcome
                    : (PinHealthStatuses.Unreadable, "no outcome recorded");
                var healthy = string.Equals(status, PinHealthStatuses.Healthy, StringComparison.Ordinal);

                return new PinHealthEntry(
                    group.Key,
                    status,
                    healthy ? null : reason,
                    group.Count(),
                    healthy ? null : group.Select(pin => pin.AppUserId).Order(StringComparer.Ordinal).ToList());
            })
            .ToList();

        return new PinHealthResponse(catalogRef, engineCommit, now, pins.Count, entries);
    }

    /// <summary>
    /// A content refusal is Stranded with the sentence the starter would log
    /// (authored: package ref, schema id, catalog ref). Anything else is
    /// Unreadable, named by type only — a storage message can carry a URL.
    /// </summary>
    public static (string Status, string? Reason) Classify(Exception exception) =>
        exception is WorkflowPackageContentException or WorkflowPackageSpecVersionException
            ? (PinHealthStatuses.Stranded, exception.Message)
            : (PinHealthStatuses.Unreadable, exception.GetType().Name);
}
