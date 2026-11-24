using System.Globalization;
using Consultologist.Api.Agents;
using Consultologist.Api.Auth;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.V31;

/// <summary>
/// #1507 (specVersion 31, area 10; R11, D22; part 09 § 3.9, part 10 §§ 1.4, 1.5): the weekly summary's hand-edits line.
/// Counted, it reads D22's words, <c>Outstanding migration hand edits: &lt;n&gt; in &lt;p&gt; packages; &lt;q&gt; packages do not
/// migrate</c>; when a figure could not be read, it is the error line A10-058, never a zero. Counts only: no package, no
/// account, no notice.
/// </summary>
public sealed class MigrationFigures
{
    /// <summary>A10-058's <c>&lt;what&gt;</c> when the type registry (or this engine's toolchain) could not be read.</summary>
    public const string TheRegistry = "the type registry";

    /// <summary>A10-058's <c>&lt;what&gt;</c> when an account's active package, or which package that is, could not be read.</summary>
    public const string ThePackages = "the accounts' active packages";

    private MigrationFigures(int handEdits, int packages, int doNotMigrate, string? unread)
    {
        HandEdits = handEdits;
        Packages = packages;
        DoNotMigrate = doNotMigrate;
        Unread = unread;
    }

    /// <summary><c>&lt;n&gt;</c>: the hand-edit notices the migration gives, summed over the packages below 31.</summary>
    public int HandEdits { get; }

    /// <summary><c>&lt;p&gt;</c>: the packages below 31 with at least one hand edit.</summary>
    public int Packages { get; }

    /// <summary><c>&lt;q&gt;</c>: the packages below 31 the migration refused, or failed on.</summary>
    public int DoNotMigrate { get; }

    /// <summary>What could not be read (<see cref="TheRegistry"/>, <see cref="ThePackages"/>), or null when counted.</summary>
    public string? Unread { get; }

    /// <summary>The figures, counted.</summary>
    public static MigrationFigures Counted(int handEdits, int packages, int doNotMigrate)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(handEdits);
        ArgumentOutOfRangeException.ThrowIfNegative(packages);
        ArgumentOutOfRangeException.ThrowIfNegative(doNotMigrate);
        return new(handEdits, packages, doNotMigrate, null);
    }

    /// <summary>No figures: <paramref name="what"/> could not be read.</summary>
    public static MigrationFigures NotRead(string what)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(what);
        return new(0, 0, 0, what);
    }

    /// <summary>The summary's line, without its line ending: D22's when counted, A10-058 when not.</summary>
    public string Line => Unread is { } what
        ? $"Outstanding migration hand edits: not counted; {what} could not be read."
        : string.Create(
            CultureInfo.InvariantCulture,
            $"Outstanding migration hand edits: {HandEdits} in {Packages} packages; {DoNotMigrate} packages do not migrate");
}

/// <summary>
/// #1507 (specVersion 31, area 10; R11, D22): the weekly summary's migration figures, computed by the summary itself (no
/// store of migration reports exists until area 11's publish path). Each account's active package is the one a consult
/// would run (<see cref="IWorkflowPackagePinResolver"/>, as <see cref="PinHealthReporter"/> reads it); each distinct
/// package is read once as stored (<see cref="IWorkflowPackageSources"/>) and, when it is below 31, migrated by
/// <see cref="WorkflowPackageMigration30To31.Migrate"/> in memory, nothing written: its hand-edit notices are summed, and a
/// refusal, or a throw, counts it among the packages that do not migrate. A package at 31 or past it is not migrated. The
/// type registry (<see cref="PublishedTypeRegistry"/>) is read only when a package is migrated. What cannot be read (a pin,
/// a package, the registry) gives <see cref="MigrationFigures.NotRead"/>, never a partial count and never an exception
/// (cancellation aside).
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

        // the packages, each read once: a pin many accounts share (general@latest) is one read, and a latest ref and
        // its version are one package
        var read = new Dictionary<string, WorkflowPackageSource>(StringComparer.Ordinal);
        try
        {
            var refs = new Dictionary<string, WorkflowPackageRef>(StringComparer.Ordinal);
            foreach (var account in accounts)
            {
                var pin = await _pins.ResolvePinAsync(account.AppUserId, ct);
                refs.TryAdd(pin.ToString(), pin);
            }

            foreach (var packageRef in refs.Values)
            {
                var source = await _sources.ReadSourceAsync(packageRef, ct);
                read.TryAdd(source.Ref, source);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Operator weekly summary: could not read the accounts' active packages. ExceptionType={ExceptionType}", ex.GetType().FullName);
            return MigrationFigures.NotRead(MigrationFigures.ThePackages);
        }

        var below31 = read.Values
            .Where(source => source.SpecVersion is not { } version || version < WorkflowPackageMigration30To31.Target)
            .OrderBy(source => source.Ref, StringComparer.Ordinal)
            .ToList();
        if (below31.Count == 0)
            return MigrationFigures.Counted(0, 0, 0);

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
                types, current, source.Formats ?? _formats, schema => _contracts.TryResolveContract(schema, out var id) ? id : null);
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

        return MigrationFigures.Counted(handEdits, packages, doNotMigrate);
    }
}
