using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Agents;
using Consultologist.Api.Auth;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Harness;
using Consultologist.PackageFormat.V31.Migration;
using Consultologist.PackageFormat.V31.Publish;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.V31;

/// <summary>
/// #1508 (area 11, Task 36; R21): what <c>POST Admin/MigratePackage</c> is asked: the account (<paramref name="Owner"/>,
/// its app user id), its active package (<paramref name="Name"/>), the package's <c>options.json</c> (<paramref name="Options"/>,
/// null for none: the defaults), the operator's hand edits (<paramref name="HandEdits"/>, path to text, null deleting the
/// file as a <c>.deleted</c> file does; <c>manifest.json</c> the manifest) and whether to publish (<paramref name="Publish"/>;
/// false is a dry run).
/// </summary>
public sealed record MigratePackageRequest(
    string Owner, string Name, JsonObject? Options, IReadOnlyDictionary<string, string?> HandEdits, bool Publish)
{
    private const string ManifestFile = "manifest.json";

    /// <summary>
    /// The request in <paramref name="body"/>: strict JSON (no member written twice), an object whose <c>owner</c> and
    /// <c>name</c> are text, <c>options</c> an object or null (or absent), <c>handEdits</c> an object of text or null (or
    /// absent: none) that does not delete <c>manifest.json</c>, and <c>publish</c> a boolean (or absent: false). Or null and
    /// why not, a sentence naming the member, never quoting a value.
    /// </summary>
    public static (MigratePackageRequest? Request, string? Error) Read(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (StrictJson.Parse(body) is not { Fault: null, Value: JsonObject json })
            return (null, "The body must be a JSON object, no member written twice.");

        if (json.Select(p => p.Key).FirstOrDefault(k => k is not ("owner" or "name" or "options" or "handEdits" or "publish")) is { } unknown)
            return (null, $"The body has the member '{unknown}'; its members are owner, name, options, handEdits and publish.");
        if (Text(json["owner"]) is not { Length: > 0 } owner)
            return (null, "owner must be the account's app user id, as text.");
        if (Text(json["name"]) is not { Length: > 0 } name)
            return (null, "name must be the account's package, as text.");

        JsonObject? options = null;
        if (json["options"] is JsonObject given)
            options = given;
        else if (json["options"] is not null)
            return (null, "options must be the package's options.json, an object, or null.");

        var edits = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (json["handEdits"] is JsonObject handEdits)
        {
            foreach (var (path, value) in handEdits)
            {
                if (path.Length == 0)
                    return (null, "handEdits names an empty path.");
                if (value is null)
                {
                    if (path == ManifestFile)
                        return (null, "handEdits cannot delete manifest.json.");
                    edits[path] = null;
                }
                else if (Text(value) is { } text)
                {
                    edits[path] = text;
                }
                else
                {
                    return (null, $"handEdits must give '{path}' as text, or null to delete it.");
                }
            }
        }
        else if (json["handEdits"] is not null)
        {
            return (null, "handEdits must be an object of paths to text (null deletes a file), or null.");
        }

        var publish = false;
        if (json["publish"] is JsonValue flag && flag.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
            publish = flag.GetValue<bool>();
        else if (json["publish"] is not null)
            return (null, "publish must be true or false.");

        return (new MigratePackageRequest(owner, name, options, edits, publish), null);
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
}

/// <summary>#1508 (Task 36): one row of <c>GET Admin/MigratePackage</c>: an account, its active account package below 31, its version and <c>specVersion</c> (null when its manifest does not read).</summary>
public sealed record AccountPackageBelow31(string Owner, string Name, string Version, int? SpecVersion);

/// <summary>
/// #1508 (specVersion 31, area 11, Task 36; part 10 § 1.5; R11, R18, R21; ruled on #1508, comments 5 to 8): the operator's
/// migrate-and-publish of one account package, which <c>scripts/bulk-publish-v31.cs</c> drives over every account.
/// <see cref="ListAsync"/> names every account's active account package below 31 (<see cref="ActivePackages"/>, as the
/// weekly summary reads them). <see cref="RunAsync"/> migrates the package the account's pin names, as stored, with the
/// package's <c>options.json</c> (or the defaults, A10-083, §§ 1.9, 1.10), lays the operator's hand edits over it and, when
/// edits were given, writes its snapshot test anew (<see cref="WorkflowPackageMigration30To31.Snapshot"/>, at the engine's
/// configured limits, so a package past them is refused there); then publishes it through <see cref="V31PackagePublisher"/>
/// as the account's next version (D5: with its migration report, so the stamp has <c>migratedFrom</c>, the report's
/// <c>publishedBy</c> is <c>operator-migration</c> and the pin moves as any publish moves it), or with <c>publish: false</c>
/// runs that publish in memory. A package it cannot publish (a pin that does not name it, A11-016; a stored package that
/// cannot be read, A11-007; options it cannot read, A10-070 to A10-072 and A10-082; a migration refusal, A10-015 for a package
/// at 31 already among them; a hand edit left, S10-002, which is every hand edit whose file is not among the edits given,
/// edited or deleted; a publish refusal, A11-001 among them) is
/// <c>not migrated: &lt;first sentence&gt;</c> and nothing is written: its 30 version and its pin are as they were
/// (comment 8). The answer carries ids, versions, sentences, the publish report and the choices only: no input, model or
/// profile text. A failure reading the pin, the stored package, the type registry or the migration itself is answered
/// (A11-007, logged by its type); a failure of the account store or of the registry's storage while publishing is not
/// caught here, and reaches the route as a 500: before the stamp nothing is published (an unstamped folder does not
/// exist for any reader); after it the version is stamped, and the pointer or the pin may not have moved.
/// </summary>
public sealed class MigratePackage(
    IAccountStore accounts,
    IWorkflowPackagePinResolver pins,
    IWorkflowPackageSources sources,
    V31PackagePublisher publisher,
    PublishedTypeRegistry registry,
    EngineToolchain toolchain,
    WorkflowFormatCatalog formats,
    OutputContractCatalog contracts,
    EngineLimitSettings limits,
    ILogger<MigratePackage> logger)
{
    /// <summary>The outcome of a package published at 31.</summary>
    public const string Published = "published";

    /// <summary>The outcome of a dry run whose publish would succeed.</summary>
    public const string WouldPublish = "would publish";

    /// <summary>The outcome of a package left at 30 begins so; its reason, the first sentence, follows.</summary>
    public const string NotMigrated = "not migrated: ";

    private const string ManifestFile = "manifest.json";

    /// <summary>Every account's active account package below 31, by owner then name, and how many pins or packages could not be read.</summary>
    public async Task<(IReadOnlyList<AccountPackageBelow31> Packages, int NotRead)> ListAsync(CancellationToken ct)
    {
        var listed = await accounts.ListAsync(ct);
        var (read, notRead) = await ActivePackages.ReadAsync(pins, sources, listed.Select(a => a.AppUserId), logger, ct);
        var rows = new List<AccountPackageBelow31>();
        foreach (var package in read.Where(p => ActivePackages.IsBelow31(p.Source)))
        {
            var (name, version) = Split(package.Source.Ref);
            if (!WorkflowPackageNaming.IsAccountPackage(name))
                continue;
            rows.AddRange(package.Owners.Select(owner => new AccountPackageBelow31(owner, name, version, package.Source.SpecVersion)));
        }

        return (rows.OrderBy(r => r.Owner, StringComparer.Ordinal).ThenBy(r => r.Name, StringComparer.Ordinal).ToList(), notRead);
    }

    /// <summary>The answer to <paramref name="request"/>, as <c>POST Admin/MigratePackage</c> gives it.</summary>
    public async Task<JsonObject> RunAsync(MigratePackageRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var answer = new Answer(request.Owner, request.Name);

        // the package the account's pin names, as stored
        WorkflowPackageSource source;
        try
        {
            var pin = await pins.ResolvePinAsync(request.Owner, ct);
            if (!WorkflowPackageNaming.IsAccountPackage(request.Name) || pin.Name != request.Name)
                return answer.NotMigrated(PackagePublishSentences.NotTheActivePackage(request.Name));
            source = await sources.ReadSourceAsync(pin, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError("Bulk publish: could not read an account's active package. Package={Package}, ExceptionType={ExceptionType}", request.Name, ex.GetType().FullName);
            return answer.NotMigrated(PackagePublishSentences.EngineFailed().Sentence);
        }

        answer.OldVersion = Split(source.Ref).Version;

        // the choices: the package's options.json, or the defaults (said)
        TypeRegistry types;
        Toolchain current;
        try
        {
            types = registry.Registry;
            current = toolchain.Current();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError("Bulk publish: could not read the type registry. ExceptionType={ExceptionType}", ex.GetType().FullName);
            return answer.NotMigrated(PackagePublishSentences.EngineFailed().Sentence);
        }

        var formats30 = source.Formats ?? formats;
        Migration31Options options;
        if (request.Options is { } given)
        {
            var (read, refusal) = Migration31Options.FromJson(given, types, current, formats30);
            if (read is null)
                return answer.NotMigrated(refusal!);
            options = read;
        }
        else
        {
            options = new Migration31Options(types, current, formats30);
            answer.Defaults = MigrationOptionsFile.Defaults;
        }

        // the engine's configured limits: the migration counts its package (A10-081) and the snapshot publishes at them
        options = options with
        {
            ResolveContract = schema => contracts.TryResolveContract(schema, out var id) ? id : null,
            Limits = limits.PublishLimits,
            RunLimits = limits.Limits,
        };
        answer.Options = options.ToJson();

        // the migration, the operator's edits laid over it, and the snapshot written anew when edits were made
        Migration31Result migrated;
        try
        {
            migrated = WorkflowPackageMigration30To31.Migrate(source.ManifestJson, source.Files, options);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            logger.LogError("Bulk publish: the migration failed on a package. Package={Package}, ExceptionType={ExceptionType}", request.Name, ex.GetType().FullName);
            return answer.NotMigrated(PackagePublishSentences.EngineFailed().Sentence);
        }

        var handEdits = migrated.HandEdits.ToHashSet(StringComparer.Ordinal);
        answer.Notices = [.. migrated.Notices.Where(n => !handEdits.Contains(n))];
        if (!migrated.Succeeded)
            return answer.NotMigrated(migrated.Refusals[0]);

        // a hand edit is made only when the file it names is among the edits (edited, or deleted); any other is left
        var left = migrated.HandEdits
            .Where((_, i) => i >= migrated.HandEditFiles.Count || !request.HandEdits.ContainsKey(migrated.HandEditFiles[i]))
            .ToList();
        if (left.Count > 0)
        {
            answer.HandEditsLeft = left;
            return answer.NotMigrated(left[0]);
        }

        var edited = migrated;
        if (request.HandEdits.Count > 0)
        {
            edited = Overlay(migrated, request.HandEdits);
            var snapshot = WorkflowPackageMigration30To31.Snapshot(edited.ManifestJson, edited.Files, options);
            if (!snapshot.Succeeded)
                return answer.NotMigrated(snapshot.Refusals[0]);
            edited = edited with
            {
                Files = snapshot.Files,
                Notices = [.. edited.Notices.Where(n => n != MigrationSentences.NoSnapshotHandEditsRemain)],
            };
        }

        var report = MigrationReport.Of(answer.OldVersion, edited, options.ToJson(), [], MigrationReport.WorstCaseNote(edited, types, limits.Limits));
        answer.Notices = [.. report.Notices];

        // the publish, as the account's next version, or its dry run
        using var migration = JsonDocument.Parse(report.ToJson());
        var files = new Dictionary<string, string>(edited.Files, StringComparer.Ordinal) { [ManifestFile] = edited.ManifestJson };
        var publishRequest = new WorkflowPackagePublishRequest(
            Source: null, Manifest: null, Files: files, Target: request.Name, NewPackageSlug: null, FromUpload: false,
            MigrationReport: migration.RootElement.Clone());
        var kind = await accounts.GetAccountKindAsync(request.Owner, ct);
        if (string.IsNullOrWhiteSpace(kind))
            kind = null;

        if (!request.Publish)
        {
            var (version, checkedReport) = await publisher.CheckAsync(kind, publishRequest, request.Name, ct);
            answer.Report = JsonNode.Parse(checkedReport.ToCanonical());
            if (!checkedReport.Published)
                return answer.NotMigrated(FirstOf(checkedReport));
            answer.NewVersion = version;
            return answer.As(WouldPublish);
        }

        var (published, publishReport) = await publisher.PublishAsync(request.Owner, kind, publishRequest, request.Name, ct);
        answer.Report = JsonNode.Parse(publishReport.ToCanonical());
        if (published is null)
            return answer.NotMigrated(FirstOf(publishReport));

        logger.LogInformation(
            "Bulk publish: an account package was migrated and published. Package={Package}, From={From}, To={To}",
            request.Name, answer.OldVersion, published.Version);
        answer.NewVersion = published.Version;
        return answer.As(Published);
    }

    /// <summary>
    /// <paramref name="migrated"/> with <paramref name="edits"/> laid over it, as the fixtures' <c>hand-edits/</c> are: text
    /// replaces or adds its path (<c>manifest.json</c> the manifest), null removes it (a path the migration did not write
    /// is left absent).
    /// </summary>
    private static Migration31Result Overlay(Migration31Result migrated, IReadOnlyDictionary<string, string?> edits)
    {
        var manifest = migrated.ManifestJson;
        var files = new Dictionary<string, string>(migrated.Files, StringComparer.Ordinal);
        foreach (var (path, text) in edits.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            if (text is null)
                files.Remove(path);
            else if (path == ManifestFile)
                manifest = text;
            else
                files[path] = text;
        }

        return migrated with { ManifestJson = manifest, Files = files };
    }

    private static string FirstOf(PublishReport report) =>
        report.Refusals.Count > 0 ? report.Refusals[0].Sentence : PackagePublishSentences.EngineFailed().Sentence;

    /// <summary><c>name@version</c> split at its last <c>@</c>.</summary>
    private static (string Name, string Version) Split(string reference)
    {
        var at = reference.LastIndexOf('@');
        return at < 0 ? (reference, "") : (reference[..at], reference[(at + 1)..]);
    }

    /// <summary>The answer as it is built: every member present, in the order the route writes them.</summary>
    private sealed class Answer(string owner, string name)
    {
        public string? OldVersion { get; set; }
        public string? NewVersion { get; set; }
        public IReadOnlyList<string> HandEditsLeft { get; set; } = [];
        public IReadOnlyList<string> Notices { get; set; } = [];
        public JsonNode? Report { get; set; }
        public JsonObject? Options { get; set; }
        public string? Defaults { get; set; }

        public JsonObject NotMigrated(string sentence) => As(MigratePackage.NotMigrated + sentence);

        public JsonObject As(string outcome) => new()
        {
            ["owner"] = owner,
            ["name"] = name,
            ["oldVersion"] = OldVersion,
            ["newVersion"] = outcome is Published or WouldPublish ? NewVersion : null,
            ["outcome"] = outcome,
            ["handEditsLeft"] = new JsonArray([.. HandEditsLeft.Select(s => (JsonNode?)JsonValue.Create(s))]),
            ["notices"] = new JsonArray([.. Notices.Select(s => (JsonNode?)JsonValue.Create(s))]),
            ["report"] = Report?.DeepClone(),
            ["options"] = Options?.DeepClone(),
            ["defaults"] = Defaults,
        };
    }
}
