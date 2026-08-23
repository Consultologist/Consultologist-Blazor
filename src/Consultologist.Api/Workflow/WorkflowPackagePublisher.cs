using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Consultologist.Api.Agents;
using Consultologist.Api.Auth;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Workflow;

/// <summary>
/// The outcome of a publish attempt: a success payload, a 400-able error list
/// for inline display in the editor, or a forbidden marker (foreign source).
/// </summary>
public sealed record WorkflowPackagePublishResult(
    WorkflowPackagePublishResponse? Response,
    IReadOnlyList<string> Errors,
    bool Forbidden = false)
{
    public bool Succeeded => Response != null;
}

/// <summary>
/// Publishes a new immutable version of the account's package and activates it
/// by flipping the pin to the concrete ref
/// (docs/customizable-workflow/in-app-editing.md). The server assigns name
/// (acct-*, owner-derived), version (next CalVer), and derivedFrom (the
/// validated Source ref) — the client's manifest values for those fields are
/// ignored, so publishing to a foreign package is impossible by construction.
/// All content passes the same validator as repo publishes.
/// </summary>
public sealed class WorkflowPackagePublisher
{
    private const int MaxFileBytes = 256 * 1024;
    private const int MaxTotalBytes = 2 * 1024 * 1024;
    private const int MaxPublishAttempts = 3;

    private static readonly Regex RootFilePattern = new("^(prompts|schemas)/[A-Za-z0-9._-]+$", RegexOptions.Compiled);
    private static readonly Regex DataFilePattern = new("^data/[a-z0-9-]+/[A-Za-z0-9._-]+$", RegexOptions.Compiled);

    /// <summary>
    /// A data path with no directory segment is a **single-value** entry —
    /// <c>data/specialty.txt</c> — which <c>WorkflowDataResolver</c> reads as a
    /// scalar and nodes bind as <c>data:&lt;id&gt;</c>. One segment, so it matches
    /// neither pattern above and this door refused it (#318) while the content
    /// repo's publish script, which has no allowlist, let repo packages through.
    ///
    /// Underscores are allowed deliberately: <c>note_type</c> is published and
    /// running. The no-underscore rule belongs to the *directory* segment above,
    /// and a value is a file.
    ///
    /// This pattern matches <c>data/..</c> — the traversal guard below is what
    /// rejects that, and it has to keep doing so.
    /// </summary>
    private static readonly Regex DataValuePattern = new("^data/[A-Za-z0-9._-]+$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions IndexJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // Registry manifests are written camelCase and without null members, matching
    // the repo package sources; the store reads case-insensitively either way.
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly IWorkflowPackageStore _packageStore;
    private readonly IWorkflowPackageRegistryWriter _writer;
    private readonly IAccountSettingsStore _settingsStore;
    private readonly OutputContractCatalog _catalog;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WorkflowPackagePublisher> _logger;

    public WorkflowPackagePublisher(
        IWorkflowPackageStore packageStore,
        IWorkflowPackageRegistryWriter writer,
        IAccountSettingsStore settingsStore,
        OutputContractCatalog catalog,
        TimeProvider timeProvider,
        ILogger<WorkflowPackagePublisher> logger)
    {
        _packageStore = packageStore;
        _writer = writer;
        _settingsStore = settingsStore;
        _catalog = catalog;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<WorkflowPackagePublishResult> PublishAsync(
        string appUserId,
        WorkflowPackagePublishRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        if (request.Manifest is null)
        {
            errors.Add("Manifest is required.");
        }

        if (request.Files is not { Count: > 0 })
        {
            errors.Add("Files are required.");
        }

        WorkflowPackageRef? sourceRef = null;
        if (!WorkflowPackageRef.TryParse(request.Source, out sourceRef))
        {
            errors.Add("Source must be a valid package reference (name@vYYYY.MM.N).");
        }
        else if (sourceRef!.IsLatest)
        {
            errors.Add("Source must be a concrete version, not @latest.");
        }

        if (errors.Count > 0)
        {
            return new WorkflowPackagePublishResult(null, errors);
        }

        if (!WorkflowPackageNaming.CanAccess(sourceRef!.Name, appUserId))
        {
            return new WorkflowPackagePublishResult(
                null,
                new[] { "Source package is not accessible from this account." },
                Forbidden: true);
        }

        var manifest = request.Manifest!;
        var files = request.Files!;

        ValidateFilePaths(files, errors);
        ValidateFileClosure(manifest, files, errors);

        if (errors.Count > 0)
        {
            return new WorkflowPackagePublishResult(null, errors);
        }

        // The fork origin must actually exist and be executable — resolving it
        // applies the registry 404, spec-floor, and validation gates (and is
        // usually a cache hit, since the editor just loaded it).
        WorkflowPackage parent;

        try
        {
            parent = await _packageStore.ResolveAsync(sourceRef, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Publish rejected: source package could not be resolved. Source={Source}", sourceRef);
            return new WorkflowPackagePublishResult(
                null,
                new[] { $"Source package {sourceRef} could not be resolved: it is not in the registry or is not executable." });
        }

        var name = WorkflowPackageNaming.ForAccount(appUserId);
        var nowUtc = _timeProvider.GetUtcNow();
        var version = CalVerVersion.AssignNext(await ReadLatestAsync(name, cancellationToken), nowUtc);

        // Server stamp: name, version, and lineage are asserted by the registry
        // writer, never by the client.
        var stamped = manifest with
        {
            Name = name,
            Version = version.ToString(),
            DerivedFrom = sourceRef.ToString()
        };

        // v9 § 4 (#432): a fork across names starts with no title — and no
        // description, both being the parent's words about the parent. A
        // republish of the same package keeps them. Decided on the validated
        // source, never the client's manifest name, which is ignored above.
        if (!string.Equals(sourceRef.Name, name, StringComparison.Ordinal))
        {
            // #453: tags likewise — cleared to the empty set, not to null,
            // because a v9 manifest must state them and the stamped manifest
            // is validated next. A pre-v9 source has none to clear.
            stamped = stamped with { Title = null, Description = null, Tags = stamped.Tags is null ? null : new List<string>() };
        }

        var catalogSchemas = _catalog.Entries.Values
            .Where(entry => entry.SchemaJson != null)
            .ToDictionary(entry => entry.ContractId, entry => entry.SchemaJson!, StringComparer.Ordinal);

        var validation = WorkflowPackageValidator.Validate(stamped, files, catalogSchemas);

        if (!validation.IsValid)
        {
            return new WorkflowPackagePublishResult(null, validation.Errors);
        }

        validation.Warnings.AddRange(RelabelledWithoutContentChange(parent, stamped, files));

        // #433: the publication stamp — what each declared schema resolved to,
        // under this catalog — recorded once, here, where the match is first
        // made. Every declared schema, not only the referenced ones the
        // validator closed; a declared schema matching nothing is refused at
        // this desk rather than published to strand at load.
        var stampErrors = new List<string>();
        var stamp = WorkflowPackageStamp.Compute(stamped, files, _catalog, stampErrors);

        if (stampErrors.Count > 0)
        {
            return new WorkflowPackagePublishResult(null, stampErrors);
        }

        var stampJson = stamp.ToJson();

        for (var attempt = 1; ; attempt++)
        {
            foreach (var (path, content) in files)
            {
                await _writer.UploadFileAsync(name, stamped.Version, path, content, cancellationToken);
            }

            // The stamp before the manifest, so no version is ever reachable
            // without it; per attempt, since a conflict moves the version.
            await _writer.UploadFileAsync(name, stamped.Version, WorkflowPackageStamp.FileName, stampJson, cancellationToken);

            try
            {
                // Manifest last, conditional create: the version is invisible until
                // this commits, so files under a conflicted candidate stay orphaned
                // but unreachable.
                await _writer.CreateManifestAsync(
                    name,
                    stamped.Version,
                    JsonSerializer.Serialize(stamped, ManifestJsonOptions),
                    cancellationToken);
                break;
            }
            catch (WorkflowPackageVersionConflictException) when (attempt < MaxPublishAttempts)
            {
                var next = CalVerVersion.AssignNext(await ReadLatestAsync(name, cancellationToken), nowUtc);

                // A stale latest pointer can lag the conflicted manifest; never
                // retry at or below the version that just collided.
                if (CalVerVersion.TryParse(stamped.Version, out var conflicted) && next.CompareTo(conflicted) <= 0)
                {
                    next = conflicted with { Counter = conflicted.Counter + 1 };
                }

                _logger.LogWarning(
                    "Publish version conflict; retrying. Package={Package}, Conflicted={Conflicted}, Next={Next}",
                    name,
                    stamped.Version,
                    next);
                stamped = stamped with { Version = next.ToString() };
            }
        }

        await _writer.SetLatestPointerAsync(name, stamped.Version, cancellationToken);

        // Publish activates, always: the pin flips to the concrete new version.
        var concreteRef = $"{name}@{stamped.Version}";
        await _settingsStore.SaveAsync(
            appUserId,
            WorkflowPackagePinResolver.PackagePinSettingKey,
            concreteRef,
            "text/plain",
            cancellationToken);

        _logger.LogInformation(
            "Published account workflow package and flipped the pin. Ref={Ref}, DerivedFrom={DerivedFrom}, Stamp={Stamp}",
            concreteRef,
            stamped.DerivedFrom,
            stamp.CatalogRef);

        return new WorkflowPackagePublishResult(
            new WorkflowPackagePublishResponse(name, stamped.Version, concreteRef, validation.Warnings),
            Array.Empty<string>());
    }

    private async Task<CalVerVersion?> ReadLatestAsync(string name, CancellationToken cancellationToken)
    {
        var latestText = await _writer.ReadLatestVersionAsync(name, cancellationToken);
        return latestText != null && CalVerVersion.TryParse(latestText, out var latest) ? latest : null;
    }

    /// <summary>
    /// A fork whose values were **relabelled** while its data collections were
    /// not (#310).
    ///
    /// Package values name the note a package produces — specialty, note_type —
    /// but the knowledge lives in the collections. So a fork that sets
    /// specialty to "cardiology" and stops produces prompts saying cardiology
    /// over oncology standards: coherent English, wrong at the source. Before
    /// values existed, forgetting to edit a prompt left the old wording visible
    /// in the output; values removed that smell, which is what this replaces.
    ///
    /// **Relabelled means one non-blank text became a different non-blank
    /// text.** Adding a value has no prior and filling an empty one has a blank
    /// prior — both are ordinary authoring, and warning on them was measured
    /// against three real publishes before this rule was narrowed. A warning
    /// that fires on normal work is not there when it matters.
    ///
    /// A warning rather than a rejection: relabelling first and replacing
    /// standards next is legitimate staged authoring, and versions are
    /// immutable so it cannot be staged inside one.
    /// </summary>
    internal static IEnumerable<string> RelabelledWithoutContentChange(
        WorkflowPackage parent,
        WorkflowPackageManifest stamped,
        IReadOnlyDictionary<string, string> files)
    {
        if (parent.Data is not { } was)
        {
            return Array.Empty<string>();
        }

        var now = WorkflowDataResolver.Resolve(stamped, files, new List<string>());

        var relabelled = now.Scalars
            .Where(pair => was.Scalars.TryGetValue(pair.Key, out var before)
                && !string.IsNullOrWhiteSpace(before)
                && !string.IsNullOrWhiteSpace(pair.Value)
                && !string.Equals(before, pair.Value, StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .Order(StringComparer.Ordinal)
            .ToList();

        if (relabelled.Count == 0 || !CollectionsMatch(was, now))
        {
            return Array.Empty<string>();
        }

        return new[]
        {
            $"Value{(relabelled.Count == 1 ? "" : "s")} {string.Join(", ", relabelled.Select(id => $"'{id}'"))} "
            + "changed but no data collection did — the package now describes itself differently while the standards it "
            + "draws from are unchanged. If that is deliberate, nothing is wrong; if not, the prompts will name a "
            + "specialty the standards do not carry."
        };
    }

    /// <summary>
    /// Whether the two resolved data tables carry the same collections, item
    /// for item. Compares the resolved form rather than raw files because the
    /// parent arrives resolved — its items already carry their content.
    /// </summary>
    private static bool CollectionsMatch(WorkflowPackageData was, WorkflowPackageData now)
    {
        if (was.Collections.Count != now.Collections.Count
            || !was.Collections.Keys.OrderBy(k => k, StringComparer.Ordinal)
                .SequenceEqual(now.Collections.Keys.OrderBy(k => k, StringComparer.Ordinal), StringComparer.Ordinal))
        {
            return false;
        }

        foreach (var (id, before) in was.Collections)
        {
            var after = now.Collections[id];

            if (!before.Fields.SequenceEqual(after.Fields, StringComparer.Ordinal)
                || before.Items.Count != after.Items.Count)
            {
                return false;
            }

            for (var i = 0; i < before.Items.Count; i++)
            {
                if (!string.Equals(before.Items[i].Id, after.Items[i].Id, StringComparison.Ordinal)
                    || before.Items[i].Fields.Count != after.Items[i].Fields.Count
                    || before.Items[i].Fields.Any(field =>
                        !after.Items[i].Fields.TryGetValue(field.Key, out var value)
                        || !string.Equals(field.Value, value, StringComparison.Ordinal)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static void ValidateFilePaths(IReadOnlyDictionary<string, string> files, List<string> errors)
    {
        long totalBytes = 0;

        foreach (var (path, content) in files)
        {
            // Blob names are a flat namespace, so dot segments carry no traversal
            // semantics there — this is defense in depth for any future
            // filesystem-backed consumer of registry paths.
            if ((!RootFilePattern.IsMatch(path) && !DataFilePattern.IsMatch(path) && !DataValuePattern.IsMatch(path))
                || path.Split('/').Any(segment => segment.Trim('.').Length == 0))
            {
                errors.Add($"File path '{path}' is not allowed: expected prompts/<file>, schemas/<file>, data/<collection>/<file>, or data/<file>.");
                continue;
            }

            var bytes = Encoding.UTF8.GetByteCount(content ?? string.Empty);

            if (bytes > MaxFileBytes)
            {
                errors.Add($"File '{path}' exceeds the {MaxFileBytes / 1024} KB per-file limit.");
            }

            totalBytes += bytes;
        }

        if (totalBytes > MaxTotalBytes)
        {
            errors.Add($"Package content exceeds the {MaxTotalBytes / 1024} KB total limit.");
        }
    }

    /// <summary>
    /// The reverse half of the manifest↔files closure: every uploaded file must be
    /// referenced by the manifest (prompts, preludes, schemas, data scalars, or a
    /// collection's index.json and its item files). The forward half — every
    /// referenced file present — is the validator's, with its own messages.
    /// </summary>
    private static void ValidateFileClosure(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string> files,
        List<string> errors)
    {
        var referenced = new HashSet<string>(StringComparer.Ordinal);

        foreach (var prompt in manifest.Prompts ?? new List<WorkflowPromptSpec>())
        {
            referenced.Add(prompt.File);
        }

        foreach (var path in (manifest.Preludes ?? new Dictionary<string, string>()).Values)
        {
            referenced.Add(path);
        }

        foreach (var path in (manifest.Schemas ?? new Dictionary<string, string>()).Values)
        {
            referenced.Add(path);
        }

        foreach (var (_, path) in manifest.Data ?? new Dictionary<string, string>())
        {
            if (!path.EndsWith('/'))
            {
                referenced.Add(path);
                continue;
            }

            var indexPath = path + WorkflowDataResolver.IndexFileName;
            referenced.Add(indexPath);

            if (!files.TryGetValue(indexPath, out var indexJson))
            {
                continue;
            }

            WorkflowDataIndexFile? index;
            try
            {
                index = JsonSerializer.Deserialize<WorkflowDataIndexFile>(indexJson, IndexJsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            foreach (var item in index?.Items ?? new List<WorkflowDataIndexItem>())
            {
                if (!string.IsNullOrWhiteSpace(item.File))
                {
                    referenced.Add(path + item.File);
                }
            }
        }

        foreach (var stray in files.Keys.Where(path => !referenced.Contains(path)).Order(StringComparer.Ordinal))
        {
            errors.Add($"File '{stray}' is not referenced by the manifest.");
        }
    }
}
