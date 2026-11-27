using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Auth;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Publish;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.V31;

/// <summary>
/// #1508 (specVersion 31, area 11; part 07 § 5 step 4.5; D2): writes a 31 version's folder in the account's private
/// container (the kind picks it, as the 5–30 writer's does), the stamp last.
/// </summary>
public interface IV31RegistryWriter
{
    /// <summary>Whether <paramref name="name"/>@<paramref name="version"/> has a stamped folder (<c>publish.json</c>).</summary>
    Task<bool> IsPublishedAsync(string? accountKind, string name, string version, CancellationToken ct);

    /// <summary>
    /// Writes <paramref name="files"/> in their order into the version's folder, then <paramref name="stamp"/>. False: lost the
    /// race (the version was stamped by another publish), and nothing of ours is stamped.
    /// </summary>
    Task<bool> WriteAsync(string? accountKind, string name, string version, IReadOnlyList<PublishFile> files, PublishedStamp stamp, CancellationToken ct);
}

/// <summary>
/// #1508 (specVersion 31, area 11; part 07 §§ 5, 9; D2 to D5): <c>WorkflowPackages/Publish</c> for a 31 package, entered by
/// <see cref="WorkflowPackagePublisher"/> only where the preview setting admits 31, with the target name it decided as the
/// 5–30 path decides it. The version is the next CalVer, as at 5–30; the assigned name and version are written into
/// <c>manifest.json</c> (a member replacement keeping every other member, its order and its nulls) before the pipeline
/// (<see cref="PackagePublish.RunAsync"/>) runs, so <c>sourceHash</c> covers the manifest as stored. A manifest whose
/// <c>name</c> or <c>version</c> is not text is passed as uploaded, and the pipeline refuses it (A11-004). The pipeline runs
/// with the engine's type registry, toolchain, limits, clock and match time limit, and no diagram (D1). A published outcome
/// is written (<see cref="IV31RegistryWriter"/>) unless its version is stamped already; a stamped or lost version is tried
/// again on the next CalVer, three times in all as at 5–30, then A11-006. Only after the stamp are the latest pointer set
/// and the account's pin flipped, as at 5–30. A refusal writes nothing: each engine fault is logged (its type or sentence
/// id, never returned) and the operator is alerted (<see cref="OperatorAlertKinds.PublishFailed"/>: the package's name and
/// the number of refusals).
/// </summary>
public sealed class V31PackagePublisher(
    IV31RegistryWriter registry,
    IWorkflowPackageRegistryWriter writer,
    IAccountSettingsStore settings,
    IWorkflowPackageOwnership ownership,
    PublishedTypeRegistry types,
    EngineToolchain toolchain,
    EngineLimitSettings limits,
    IOperatorAlerts alerts,
    TimeProvider clock,
    ILogger<V31PackagePublisher> logger,
    // Tests name the operators; the engine reads Operators__AppUserIds, as Admin/Drain does.
    IReadOnlySet<string>? operators = null)
{
    /// <summary>D4's <c>publishedBy</c>: a request carrying the Editor's <c>Source</c>.</summary>
    public const string ByEditor = "editor";

    /// <summary>D4's <c>publishedBy</c>: a request with no <c>Source</c> and no migration report.</summary>
    public const string ByApi = "api";

    /// <summary>D4's <c>publishedBy</c>: a request carrying the operator's migration report (D5).</summary>
    public const string ByOperatorMigration = "operator-migration";

    private const int MaxPublishAttempts = 3;
    private const string ManifestFile = "manifest.json";

    private static readonly UTF8Encoding Utf8 = new(false);

    private static readonly JsonSerializerOptions ManifestWrite = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IReadOnlySet<string> _operators = operators ?? Operators.Parse(Environment.GetEnvironmentVariable(Operators.SettingName));

    /// <summary>The pipeline; a test's seam.</summary>
    internal Func<IReadOnlyDictionary<string, byte[]>, PublishOptions, Task<PublishOutcome>> Pipeline { get; init; } = PackagePublish.RunAsync;

    /// <summary>D3: whether the request's files are a 31 package: <c>manifest.json</c> parses as an object whose <c>specVersion</c> is the integer 31.</summary>
    public static bool IsV31(IReadOnlyDictionary<string, string>? files)
    {
        if (files == null || !files.TryGetValue(ManifestFile, out var text) || text == null)
            return false;

        try
        {
            return JsonNode.Parse(text) is JsonObject manifest
                && manifest["specVersion"] is JsonValue value
                && value.GetValueKind() == JsonValueKind.Number
                && value.TryGetValue<int>(out var specVersion)
                && specVersion == 31;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException or FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// D5: null when the request may go on. A migration report from a caller who is not the operator is today's forbidden
    /// answer; one without <c>oldVersion</c> and <c>newVersion</c> as text is refused, and so is one that writes a member
    /// twice in any object (the final review of Part F: its <c>newVersion</c> could not be rewritten). Reads nothing.
    /// </summary>
    public WorkflowPackagePublishResult? Admit(string appUserId, WorkflowPackagePublishRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MigrationReport is not { } migration)
            return null;

        if (!_operators.Contains(appUserId))
            return new WorkflowPackagePublishResult(null, ["A migration report is accepted only from the operator."], Forbidden: true);

        if (MigratedFrom(migration) == null)
            return new WorkflowPackagePublishResult(null, ["MigrationReport must give oldVersion and newVersion as text."]);
        return WritesAMemberTwice(migration)
            ? new WorkflowPackagePublishResult(null, ["MigrationReport must be a JSON object with each member written once."])
            : null;
    }

    /// <summary>Whether an object in <paramref name="value"/>, at any depth, writes a member name twice (ordinal).</summary>
    private static bool WritesAMemberTwice(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                {
                    if (!names.Add(property.Name) || WritesAMemberTwice(property.Value))
                        return true;
                }

                return false;
            case JsonValueKind.Array:
                return value.EnumerateArray().Any(WritesAMemberTwice);
            default:
                return false;
        }
    }

    /// <summary>
    /// Publishes the request's files as <paramref name="targetName"/>'s next version: the response and the report when
    /// published, else no response and the refused report. <see cref="Admit"/> has said yes.
    /// </summary>
    public async Task<(WorkflowPackagePublishResponse? Published, PublishReport Report)> PublishAsync(
        string appUserId, string? accountKind, WorkflowPackagePublishRequest request, string targetName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(targetName);
        var files = request.Files ?? new Dictionary<string, string>();
        var name = targetName;
        var options = OptionsOf(request);

        var nowUtc = clock.GetUtcNow();
        var version = CalVerVersion.AssignNext(await ReadLatestAsync(accountKind, name, ct), nowUtc);
        var recorded = false;

        for (var attempt = 1; ; attempt++)
        {
            var outcome = await Pipeline(Upload(files, name, version.ToString()), OnVersion(options, version.ToString()));
            if (!outcome.Published)
                return (null, await RefusedAsync(name, outcome.Report, ct));

            if (!await registry.IsPublishedAsync(accountKind, name, version.ToString(), ct))
            {
                if (!recorded)
                {
                    // Recorded before a byte is written, as at 5–30: a package with no record is unreachable.
                    await ownership.RecordAsync(appUserId, name, ct);
                    recorded = true;
                }

                if (await registry.WriteAsync(accountKind, name, version.ToString(), outcome.Files, outcome.Stamp!, ct))
                    return (await ActivateAsync(appUserId, accountKind, name, version.ToString(), outcome.Report, ct), outcome.Report);
            }

            if (attempt == MaxPublishAttempts)
            {
                var lost = outcome.Report with
                {
                    Published = false,
                    PublishedAtUtc = null,
                    ResolveHash = null,
                    LockHash = null,
                    Refusals = [PackagePublishSentences.AlreadyPublished(name, version.ToString())],
                };
                return (null, await RefusedAsync(name, lost, ct));
            }

            var next = CalVerVersion.AssignNext(await ReadLatestAsync(accountKind, name, ct), nowUtc);
            // A stale latest pointer can lag the stamped version; never retry at or below it.
            if (next.CompareTo(version) <= 0)
                next = version with { Counter = version.Counter + 1 };

            logger.LogWarning(
                "31 publish version taken; retrying. Package={Package}, Conflicted={Conflicted}, Next={Next}", name, version, next);
            version = next;
        }
    }

    /// <summary>
    /// #1508 (Task 36): the publish of <see cref="PublishAsync"/> run once in memory at the version it would assign first,
    /// nothing written, no pointer or pin moved, no alert raised: the version and the report as they would be.
    /// <see cref="Admit"/> has said yes.
    /// </summary>
    public async Task<(string Version, PublishReport Report)> CheckAsync(
        string? accountKind, WorkflowPackagePublishRequest request, string targetName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(targetName);
        var version = CalVerVersion.AssignNext(await ReadLatestAsync(accountKind, targetName, ct), clock.GetUtcNow()).ToString();
        var outcome = await Pipeline(
            Upload(request.Files ?? new Dictionary<string, string>(), targetName, version), OnVersion(OptionsOf(request), version));
        return (version, outcome.Report);
    }

    /// <summary>The pipeline's options for <paramref name="request"/>: the engine's registry, toolchain, limits and clock, D4's <c>publishedBy</c>, D5's report.</summary>
    private PublishOptions OptionsOf(WorkflowPackagePublishRequest request)
    {
        var publishedBy = request.MigrationReport != null ? ByOperatorMigration : request.Source != null ? ByEditor : ByApi;
        return new PublishOptions(
            types.Registry, toolchain.Current(), limits.PublishLimits, publishedBy, clock,
            MigrationReport: request.MigrationReport?.GetRawText(),
            MigratedFrom: request.MigrationReport is { } migration ? MigratedFrom(migration) : null,
            MatchTimeLimit: limits.RegexLimit,
            RunLimits: limits.Limits);
    }

    /// <summary>
    /// #1508 (Task 36): <paramref name="options"/> for a publish at <paramref name="version"/>: a migration report whose
    /// <c>newVersion</c> is not that version gets it (a member replacement, written canonical, RFC 8785), so the stored report
    /// names the version it is stored in; one that names it already is stored as sent, and so is one that does not read as
    /// an object of distinct members (<see cref="Admit"/> refuses it first; never an exception here).
    /// </summary>
    private static PublishOptions OnVersion(PublishOptions options, string version)
    {
        try
        {
            if (options.MigrationReport is not { } text
                || JsonNode.Parse(text) is not JsonObject report
                || report["newVersion"] is JsonValue newVersion && newVersion.TryGetValue<string>(out var named) && named == version)
            {
                return options;
            }

            report["newVersion"] = version;
            return options with { MigrationReport = CanonicalJson.Write(report) };
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException or FormatException)
        {
            return options;
        }
    }

    /// <summary>The report as the response and the 400's body carry it.</summary>
    public static JsonElement ReportElement(PublishReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        using var document = JsonDocument.Parse(report.ToCanonical());
        return document.RootElement.Clone();
    }

    /// <summary>The latest pointer and the pin, after the stamp, as at 5–30; the response.</summary>
    private async Task<WorkflowPackagePublishResponse> ActivateAsync(
        string appUserId, string? accountKind, string name, string version, PublishReport report, CancellationToken ct)
    {
        await writer.SetLatestPointerAsync(accountKind, name, version, ct);
        var concreteRef = $"{name}@{version}";
        await settings.SaveAsync(appUserId, WorkflowPackagePinResolver.PackagePinSettingKey, concreteRef, "text/plain", ct);

        logger.LogInformation(
            "Published a 31 workflow package and flipped the pin. Ref={Ref}, ResolveHash={ResolveHash}, PublishedBy={PublishedBy}",
            concreteRef, report.ResolveHash, report.PublishedBy);

        return new WorkflowPackagePublishResponse(
            name, version, concreteRef, report.Warnings.Select(warning => warning.Sentence).ToList(), ReportElement(report));
    }

    /// <summary>Logs each engine fault (its type or sentence id) and alerts the operator; nothing is written.</summary>
    private async Task<PublishReport> RefusedAsync(string name, PublishReport report, CancellationToken ct)
    {
        foreach (var refusal in report.Refusals.Where(refusal => refusal.EngineFault != null))
        {
            logger.LogError(
                "31 publish engine fault. Package={Package}, Version={Version}, Sentence={Sentence}, Fault={Fault}",
                name, report.Version, refusal.Id, refusal.EngineFault);
        }

        logger.LogInformation(
            "31 publish refused. Package={Package}, Version={Version}, Refusals={Refusals}, First={First}",
            name, report.Version, report.Refusals.Count, report.Refusals.Count > 0 ? report.Refusals[0].Id : null);

        try
        {
            await alerts.RaiseAsync(new OperatorAlert(OperatorAlertKinds.PublishFailed, null, name, report.Refusals.Count), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                "Operator alert failed. Kind={Kind}, ExceptionType={ExceptionType}", OperatorAlertKinds.PublishFailed, ex.GetType().FullName);
        }

        return report;
    }

    private async Task<CalVerVersion?> ReadLatestAsync(string? accountKind, string name, CancellationToken ct)
    {
        var latestText = await writer.ReadLatestVersionAsync(accountKind, name, ct);
        return latestText != null && CalVerVersion.TryParse(latestText, out var latest) ? latest : null;
    }

    /// <summary>The request's files as UTF-8 without a byte-order mark (D3), <c>manifest.json</c> naming <paramref name="name"/>@<paramref name="version"/> (D2).</summary>
    private static Dictionary<string, byte[]> Upload(IReadOnlyDictionary<string, string> files, string name, string version)
    {
        var upload = files.ToDictionary(file => file.Key, file => Utf8.GetBytes(file.Value ?? string.Empty), StringComparer.Ordinal);
        if (files.TryGetValue(ManifestFile, out var text) && text != null && Named(text, name, version) is { } named)
            upload[ManifestFile] = Utf8.GetBytes(named);
        return upload;
    }

    /// <summary>
    /// D2: <paramref name="text"/> with its <c>name</c> and <c>version</c> replaced in place, every other member as it was; null
    /// (the manifest is stored as uploaded, and refused) unless it is an object whose <c>name</c> and <c>version</c> are text.
    /// </summary>
    private static string? Named(string text, string name, string version)
    {
        try
        {
            if (JsonNode.Parse(text) is not JsonObject manifest
                || manifest["name"] is not JsonValue uploadedName || uploadedName.GetValueKind() != JsonValueKind.String
                || manifest["version"] is not JsonValue uploadedVersion || uploadedVersion.GetValueKind() != JsonValueKind.String)
            {
                return null;
            }

            manifest["name"] = name;
            manifest["version"] = version;
            var named = manifest.ToJsonString(ManifestWrite);
            return text.EndsWith('\n') ? named + "\n" : named;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException or FormatException)
        {
            return null;
        }
    }

    /// <summary>D5: the migration report's <c>oldVersion</c>, or null unless it is an object with <c>oldVersion</c> and <c>newVersion</c> as text.</summary>
    private static string? MigratedFrom(JsonElement migration) =>
        migration.ValueKind == JsonValueKind.Object
        && migration.TryGetProperty("oldVersion", out var oldVersion) && oldVersion.ValueKind == JsonValueKind.String
        && migration.TryGetProperty("newVersion", out var newVersion) && newVersion.ValueKind == JsonValueKind.String
            ? oldVersion.GetString()
            : null;
}
