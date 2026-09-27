using System.Text.Json;
using System.Text.Json.Serialization;
using Consultologist.Api.Agents;
using Consultologist.Api.Auth;
using Microsoft.Extensions.Logging;

using Consultologist.PackageFormat;
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
    private const int MaxPublishAttempts = 3;

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
    private readonly IWorkflowPackageOwnership _ownership;
    private readonly OutputContractCatalog _catalog;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WorkflowPackagePublisher> _logger;

    public WorkflowPackagePublisher(
        IWorkflowPackageStore packageStore,
        IWorkflowPackageRegistryWriter writer,
        IAccountSettingsStore settingsStore,
        OutputContractCatalog catalog,
        TimeProvider timeProvider,
        ILogger<WorkflowPackagePublisher> logger,
        IWorkflowPackageOwnership ownership)
    {
        _packageStore = packageStore;
        _writer = writer;
        _settingsStore = settingsStore;
        _catalog = catalog;
        _timeProvider = timeProvider;
        _logger = logger;
        _ownership = ownership;
    }

    public async Task<WorkflowPackagePublishResult> PublishAsync(
        string appUserId,
        string? accountKind,
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

        // #851: an uploaded package carries its own body, so the Source need not
        // be a package this account can access (there is no live source to fork).
        if (!request.FromUpload && !await _ownership.CanAccessAsync(sourceRef!.Name, appUserId, cancellationToken))
        {
            return new WorkflowPackagePublishResult(
                null,
                new[] { "Source package is not accessible from this account." },
                Forbidden: true);
        }

        // #447: where it goes, decided before anything is read or written.
        var target = await ResolveTargetNameAsync(appUserId, accountKind, request, cancellationToken);
        if (target.Result != null)
        {
            return target.Result;
        }

        var manifest = request.Manifest!;
        var files = request.Files!;

        WorkflowPackageContentValidation.ValidateFilePaths(files, errors);
        WorkflowPackageContentValidation.ValidateFileClosure(manifest, files, errors);

        if (errors.Count > 0)
        {
            return new WorkflowPackagePublishResult(null, errors);
        }

        // The fork origin must actually exist and be executable — resolving it
        // applies the registry 404, spec-floor, and validation gates (and is
        // usually a cache hit, since the editor just loaded it). #851: an
        // uploaded package has no live source to resolve, so skip it — the
        // uploaded files are validated in full below regardless.
        WorkflowPackage? parent = null;

        if (!request.FromUpload)
        {
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
        }

        var name = target.Name!;
        // Recorded before a byte is written: an orphan record for a publish
        // that then fails is harmless (the listing shows only names with
        // blobs), while a package with no record is unreachable.
        await _ownership.RecordAsync(appUserId, name, cancellationToken);
        var nowUtc = _timeProvider.GetUtcNow();
        var version = CalVerVersion.AssignNext(await ReadLatestAsync(accountKind, name, cancellationToken), nowUtc);

        // Server stamp: name, version, and lineage are asserted by the registry
        // writer, never by the client.
        var stamped = manifest with
        {
            Name = name,
            Version = version.ToString(),
            // #851: an uploaded package is a new ROOT — its declared origin is
            // another account's package we cannot verify, and stamping it would
            // strand the lineage walker. A live fork records its source as ever.
            DerivedFrom = request.FromUpload ? null : sourceRef.ToString()
        };

        // v9 § 4 (#432): a fork across names starts with no title — and no
        // description, both being the parent's words about the parent. A
        // republish of the same package keeps them. Decided on the validated
        // source, never the client's manifest name, which is ignored above.
        // #851: an uploaded package keeps its own title/description/tags — it is
        // a complete package the author is saving, not a fork masquerading as a
        // parent's words. The clearing is only for a live cross-name fork.
        if (!request.FromUpload && !string.Equals(sourceRef.Name, name, StringComparison.Ordinal))
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

        // #851: the relabel warning compares against the live source; an upload
        // has none, so there is nothing to compare.
        if (parent is not null)
        {
            validation.Warnings.AddRange(RelabelledWithoutContentChange(parent, stamped, files));
        }

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
                await _writer.UploadFileAsync(accountKind, name, stamped.Version, path, content, cancellationToken);
            }

            // The stamp before the manifest, so no version is ever reachable
            // without it; per attempt, since a conflict moves the version.
            await _writer.UploadFileAsync(accountKind, name, stamped.Version, WorkflowPackageStamp.FileName, stampJson, cancellationToken);

            try
            {
                // Manifest last, conditional create: the version is invisible until
                // this commits, so files under a conflicted candidate stay orphaned
                // but unreachable.
                await _writer.CreateManifestAsync(
                    accountKind,
                    name,
                    stamped.Version,
                    JsonSerializer.Serialize(stamped, ManifestJsonOptions),
                    cancellationToken);
                break;
            }
            catch (WorkflowPackageVersionConflictException) when (attempt < MaxPublishAttempts)
            {
                var next = CalVerVersion.AssignNext(await ReadLatestAsync(accountKind, name, cancellationToken), nowUtc);

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

        await _writer.SetLatestPointerAsync(accountKind, name, stamped.Version, cancellationToken);

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

    private async Task<CalVerVersion?> ReadLatestAsync(string? accountKind, string name, CancellationToken cancellationToken)
    {
        var latestText = await _writer.ReadLatestVersionAsync(accountKind, name, cancellationToken);
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

    private readonly record struct TargetName(string? Name, WorkflowPackagePublishResult? Result);

    /// <summary>
    /// #447: Target (a new version of a package the account owns), or
    /// NewPackageSlug (a package it does not have yet), or neither (the
    /// derived first-package name every older client publishes to).
    /// </summary>
    private async Task<TargetName> ResolveTargetNameAsync(
        string appUserId,
        string? accountKind,
        WorkflowPackagePublishRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Target != null && request.NewPackageSlug != null)
        {
            return new(null, new WorkflowPackagePublishResult(null, new[] { "Target and NewPackageSlug are exclusive: name the package to republish, or the slug of a new one." }));
        }

        if (request.NewPackageSlug != null)
        {
            if (!WorkflowPackageNaming.IsValidPath(request.NewPackageSlug))
            {
                return new(null, new WorkflowPackagePublishResult(null, new[]
                {
                    $"NewPackageSlug must be a slug or a folder path of slugs (oncology/breast): lowercase letters, digits and hyphens per segment, each starting with a letter or digit, not ending with a hyphen, at most {WorkflowPackageNaming.MaxSlugLength} characters, at most {WorkflowPackageNaming.MaxPathSegments} segments."
                }));
            }

            var name = WorkflowPackageNaming.ForAccount(appUserId, request.NewPackageSlug);

            if (await _writer.ReadLatestVersionAsync(accountKind, name, cancellationToken) != null
                || await _ownership.OwnsAsync(appUserId, name, cancellationToken))
            {
                return new(null, new WorkflowPackagePublishResult(null, new[] { $"A package named {name} already exists; choose another slug, or publish a new version of it." }));
            }

            return new(name, null);
        }

        if (request.Target != null)
        {
            if (!WorkflowPackageRef.TryParse($"{request.Target}@latest", out _) || !WorkflowPackageNaming.IsAccountPackage(request.Target))
            {
                return new(null, new WorkflowPackagePublishResult(null, new[] { "Target must be one of this account's packages." }));
            }

            if (!await _ownership.CanAccessAsync(request.Target, appUserId, cancellationToken))
            {
                return new(null, new WorkflowPackagePublishResult(null, new[] { "Target package is not owned by this account." }, Forbidden: true));
            }

            return new(request.Target, null);
        }

        return new(WorkflowPackageNaming.ForAccount(appUserId), null);
    }
}
