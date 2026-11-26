using System.Globalization;
using Consultologist.Api.Agents;
using Consultologist.Api.Auth;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.V31;

/// <summary>
/// #1507 (specVersion 31, area 10; R11, D22; part 09 § 3.9, part 10 §§ 1.4, 1.5): the weekly summary's hand-edits lines.
/// What was read is counted on D22's line, <c>Outstanding migration hand edits: &lt;n&gt; in &lt;p&gt; packages; &lt;q&gt;
/// packages do not migrate</c>; the accounts' active packages that could not be read follow it as A10-059, a count, and
/// stand alone when none could be read. When nothing can be gathered at all, A10-058 stands alone. Never a zero for what
/// was not read. Counts only: no package, no account, no notice.
/// </summary>
public sealed class MigrationFigures
{
    /// <summary>A10-058's <c>&lt;what&gt;</c> when the type registry (or this engine's toolchain) could not be read.</summary>
    public const string TheRegistry = "the type registry";

    /// <summary>A10-058's <c>&lt;what&gt;</c> when the accounts' active packages could not be gathered at all.</summary>
    public const string ThePackages = "the accounts' active packages";

    private MigrationFigures(int? handEdits, int? packages, int? doNotMigrate, int notCounted, string? notGathered)
    {
        HandEdits = handEdits;
        Packages = packages;
        DoNotMigrate = doNotMigrate;
        NotCounted = notCounted;
        NotGathered = notGathered;
    }

    /// <summary><c>&lt;n&gt;</c>: the hand-edit notices the migration gives, summed over the packages read below 31; null when none was read.</summary>
    public int? HandEdits { get; }

    /// <summary><c>&lt;p&gt;</c>: the packages read below 31 with at least one hand edit; null when none was read.</summary>
    public int? Packages { get; }

    /// <summary><c>&lt;q&gt;</c>: the packages read below 31 the migration refused, or failed on; null when none was read.</summary>
    public int? DoNotMigrate { get; }

    /// <summary>A10-059's <c>&lt;k&gt;</c>: the accounts' active packages that could not be read (a pin or a package), not counted.</summary>
    public int NotCounted { get; }

    /// <summary>A10-058's <c>&lt;what&gt;</c> when nothing could be gathered (<see cref="TheRegistry"/>, <see cref="ThePackages"/>); else null.</summary>
    public string? NotGathered { get; }

    /// <summary>
    /// What was read, counted, with <paramref name="notCounted"/> of the accounts' active packages not read beside it.
    /// Some package must have been read, or none was unread: else <see cref="NoneRead"/>.
    /// </summary>
    public static MigrationFigures Counted(int handEdits, int packages, int doNotMigrate, int notCounted = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(handEdits);
        ArgumentOutOfRangeException.ThrowIfNegative(packages);
        ArgumentOutOfRangeException.ThrowIfNegative(doNotMigrate);
        ArgumentOutOfRangeException.ThrowIfNegative(notCounted);
        return new(handEdits, packages, doNotMigrate, notCounted, null);
    }

    /// <summary>No package read: <paramref name="notCounted"/> (at least one) could not be.</summary>
    public static MigrationFigures NoneRead(int notCounted)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(notCounted);
        return new(null, null, null, notCounted, null);
    }

    /// <summary>Nothing gathered: <paramref name="what"/> could not be read.</summary>
    public static MigrationFigures NotRead(string what)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(what);
        return new(null, null, null, 0, what);
    }

    /// <summary>The summary's lines, without line endings: A10-058 alone; or D22's when something was read, then A10-059 when something was not.</summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            if (NotGathered is { } what)
                return [$"Outstanding migration hand edits: not counted; {what} could not be read."];

            var lines = new List<string>();
            if (HandEdits is { } n && Packages is { } p && DoNotMigrate is { } q)
            {
                lines.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Outstanding migration hand edits: {n} in {p} packages; {q} packages do not migrate"));
            }

            if (NotCounted > 0)
            {
                lines.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Outstanding migration hand edits: {NotCounted} of the accounts' active packages could not be read and are not counted."));
            }

            return lines;
        }
    }
}

/// <summary>
/// #1507 (specVersion 31, area 10; R11, D22): the weekly summary's migration figures, computed by the summary itself (no
/// store of migration reports exists until area 11's publish path). Each account's active package is the one a consult
/// would run (<see cref="IWorkflowPackagePinResolver"/>, as <see cref="PinHealthReporter"/> reads it); each distinct
/// package is read once as stored (<see cref="IWorkflowPackageSources"/>) and, when it is below 31, migrated by
/// <see cref="WorkflowPackageMigration30To31.Migrate"/> in memory, nothing written and no snapshot test made
/// (<see cref="Migration31Options.WriteSnapshot"/> false): its hand-edit notices are summed, and a
/// refusal, or a throw, counts it among the packages that do not migrate. A package at 31 or past it is not migrated. The
/// type registry (<see cref="PublishedTypeRegistry"/>) is read only when a package is migrated. A pin or a package that
/// cannot be read fails alone: it is counted as not counted (A10-059) and the rest are counted. A registry or toolchain
/// that cannot be read gives <see cref="MigrationFigures.NotRead"/> (A10-058). Never an exception (cancellation aside).
/// </summary>
public sealed class OutstandingHandEdits(
    IWorkflowPackagePinResolver pins,
    IWorkflowPackageSources sources,
    PublishedTypeRegistry registry,
    EngineToolchain toolchain,
    WorkflowFormatCatalog formats,
    OutputContractCatalog contracts,
    ILogger<OutstandingHandEdits> logger)
{
    private readonly IWorkflowPackagePinResolver _pins = pins ?? throw new ArgumentNullException(nameof(pins));
    private readonly IWorkflowPackageSources _sources = sources ?? throw new ArgumentNullException(nameof(sources));
    private readonly PublishedTypeRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly EngineToolchain _toolchain = toolchain ?? throw new ArgumentNullException(nameof(toolchain));
    private readonly WorkflowFormatCatalog _formats = formats ?? throw new ArgumentNullException(nameof(formats));
    private readonly OutputContractCatalog _contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
    private readonly ILogger<OutstandingHandEdits> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>The figures over <paramref name="accounts"/>' active packages.</summary>
    public async Task<MigrationFigures> CountAsync(IReadOnlyList<AccountSummary> accounts, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        var (read, notCounted) = await ActivePackages.ReadAsync(_pins, _sources, accounts.Select(a => a.AppUserId), _logger, ct);
        if (read.Count == 0 && notCounted > 0)
            return MigrationFigures.NoneRead(notCounted);

        var below31 = read.Select(package => package.Source).Where(ActivePackages.IsBelow31).ToList();
        if (below31.Count == 0)
            return MigrationFigures.Counted(0, 0, 0, notCounted);

        Consultologist.PackageFormat.V31.TypeRegistry types;
        Consultologist.PackageFormat.V31.Toolchain current;
        try
        {
            types = _registry.Registry;
            current = _toolchain.Current();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Operator weekly summary: could not read the type registry. ExceptionType={ExceptionType}", ex.GetType().FullName);
            return MigrationFigures.NotRead(MigrationFigures.TheRegistry);
        }

        int handEdits = 0, packages = 0, doNotMigrate = 0;
        foreach (var source in below31)
        {
            ct.ThrowIfCancellationRequested();
            var options = new Migration31Options(
                types, current, source.Formats ?? _formats, schema => _contracts.TryResolveContract(schema, out var id) ? id : null,
                WriteSnapshot: false);
            try
            {
                var result = WorkflowPackageMigration30To31.Migrate(source.ManifestJson, source.Files, options);
                if (!result.Succeeded)
                {
                    doNotMigrate++;
                }
                else if (result.HandEdits.Count > 0)
                {
                    handEdits += result.HandEdits.Count;
                    packages++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                // the migration never throws on package content; if it does, the package does not migrate, and the rest
                // are still counted
                _logger.LogWarning("Operator weekly summary: the migration failed on a package. ExceptionType={ExceptionType}", ex.GetType().FullName);
                doNotMigrate++;
            }
        }

        return MigrationFigures.Counted(handEdits, packages, doNotMigrate, notCounted);
    }
}

/// <summary>
/// #1507 (R11, D22), shared by #1508 (area 11, Task 36): the accounts' active packages, each the one a consult would run
/// (<see cref="IWorkflowPackagePinResolver"/>), each distinct package read once as stored (<see cref="IWorkflowPackageSources"/>):
/// a pin many accounts share (<c>general@latest</c>) is one read, and a <c>latest</c> ref and its version are one package.
/// Read by the weekly summary's hand-edits line (<see cref="OutstandingHandEdits"/>) and by the operator's bulk publish
/// (<see cref="MigratePackage"/>, its list). Each pin and each read fails alone (logged with its exception's type, never
/// its message) and is counted as not read.
/// </summary>
public static class ActivePackages
{
    /// <summary>A package read: its source and the accounts whose pin names it, in the order they were given.</summary>
    public sealed record Read(WorkflowPackageSource Source, IReadOnlyList<string> Owners);

    /// <summary>
    /// The packages <paramref name="appUserIds"/>' pins name, in the ordinal order of their resolved refs, and how many
    /// pins or packages could not be read. Never an exception but cancellation.
    /// </summary>
    public static async Task<(IReadOnlyList<Read> Packages, int NotRead)> ReadAsync(
        IWorkflowPackagePinResolver pins, IWorkflowPackageSources sources, IEnumerable<string> appUserIds, ILogger logger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pins);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(appUserIds);
        ArgumentNullException.ThrowIfNull(logger);

        var notRead = 0;
        var refs = new Dictionary<string, (WorkflowPackageRef Ref, List<string> Owners)>(StringComparer.Ordinal);
        foreach (var appUserId in appUserIds)
        {
            try
            {
                var pin = await pins.ResolvePinAsync(appUserId, ct);
                if (!refs.TryGetValue(pin.ToString(), out var named))
                    refs[pin.ToString()] = named = (pin, []);
                named.Owners.Add(appUserId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Active packages: could not resolve an account's active package. ExceptionType={ExceptionType}", ex.GetType().FullName);
                notRead++;
            }
        }

        var read = new Dictionary<string, (WorkflowPackageSource Source, List<string> Owners)>(StringComparer.Ordinal);
        foreach (var (packageRef, owners) in refs.Values)
        {
            try
            {
                var source = await sources.ReadSourceAsync(packageRef, ct);
                if (read.TryGetValue(source.Ref, out var known))
                    known.Owners.AddRange(owners);
                else
                    read[source.Ref] = (source, [.. owners]);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Active packages: could not read an account's active package. ExceptionType={ExceptionType}", ex.GetType().FullName);
                notRead++;
            }
        }

        return (read.Values.OrderBy(p => p.Source.Ref, StringComparer.Ordinal).Select(p => new Read(p.Source, p.Owners)).ToList(), notRead);
    }

    /// <summary>Whether <paramref name="source"/> is below 31 (or its manifest does not read): the migration's to make.</summary>
    public static bool IsBelow31(WorkflowPackageSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.SpecVersion is not { } version || version < WorkflowPackageMigration30To31.Target;
    }
}
