using System.Collections.Concurrent;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Consultologist.Api.Agents;
using Consultologist.Api.Models;
using Microsoft.Extensions.Logging;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Workflow;

public interface IWorkflowPackageStore
{
    Task<WorkflowPackage> ResolveAsync(WorkflowPackageRef packageRef, CancellationToken cancellationToken);
}

public sealed class WorkflowPackageStore : IWorkflowPackageStore
{
    /// <summary>A manifest declares the rule set it was validated under (package-format-v6-design.md § 9).</summary>
    // v12 (#623): the engine runs twelve — the last rung of the v12 ladder
    // (package-format-v12-design.md § 12), after the format registry
    // published the v12 document, schema and conformance suite as one
    // version.
    // v13 (#728): declarable input content channels — transcript and form.
    // v18 (#822): a node's own when — condition-gated node inclusion, cascade.
    // v20 (#863): inline macro slots — a section may emit [[slot:id]], filled at assembly.
    // v21 (#923): schemaRefs — an output schema may reference a catalog contract by name.
    // v22 (#760): customSchemas — an inline user-defined output shape, run by a
    // content-addressed generic no-tool agent (unattested).
    // v23 (#955): node macros — a library macro composed into a node's prompt,
    // before/after, gated by the node-level when; a classifier-gated one waits
    // for its classifier (scheduling dependency).
    public static readonly IReadOnlyList<int> SupportedSpecVersions = new[] { 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23 };
    private static readonly TimeSpan LatestPointerCacheDuration = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly WorkflowPackageBlobContainerFactory _containers;
    private readonly OutputContractCatalog _catalog;
    private readonly CatalogResolver _catalogResolver;
    private readonly ILogger<WorkflowPackageStore> _logger;

    // Published package versions are immutable, so resolved packages cache forever;
    // only the mutable latest-pointers expire.
    private readonly ConcurrentDictionary<string, WorkflowPackage> _packageCache = new();
    private readonly ConcurrentDictionary<string, (string Version, DateTimeOffset FetchedAt)> _latestCache = new();

    public WorkflowPackageStore(
        WorkflowPackageBlobContainerFactory containerFactory,
        OutputContractCatalog catalog,
        CatalogResolver catalogResolver,
        ILogger<WorkflowPackageStore> logger)
    {
        _catalog = catalog;
        _catalogResolver = catalogResolver;
        _logger = logger;
        _containers = containerFactory;
    }

    public async Task<WorkflowPackage> ResolveAsync(WorkflowPackageRef packageRef, CancellationToken cancellationToken)
    {
        var version = packageRef.IsLatest
            ? await ResolveLatestVersionAsync(packageRef.Name, cancellationToken)
            : packageRef.Version;

        var cacheKey = $"{packageRef.Name}@{version}";
        if (_packageCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var manifestJson = await DownloadTextAsync(packageRef.Name, $"{packageRef.Name}/{version}/manifest.json", cancellationToken);

        // Version first, then shape (#416). The rule lives in
        // WorkflowPackageManifestJson because it is testable there and was not
        // testable here.
        var manifest = WorkflowPackageManifestJson.Read(manifestJson, cacheKey, SupportedSpecVersions);

        var loaded = await LoadPromptsAsync(packageRef.Name, version, manifest, cancellationToken);
        var prompts = loaded.Prompts;

        var nodes = manifest.Nodes;
        var results = ResolveResultSet(manifest);
        var resultNodeId = results is null
            ? manifest.Result![WorkflowNodeBindingSources.NodePrefix.Length..]
            : results.Count == 1 ? results[0].NodeId : null;
        var schemaContracts = loaded.SchemaContracts;

        var package = new WorkflowPackage(manifest, prompts, nodes, schemaContracts, loaded.Data, resultNodeId, loaded.Files, results, loaded.Stamp, loaded.ContractAgents, loaded.CatalogRef);

        _packageCache.TryAdd(cacheKey, package);
        _logger.LogInformation("Workflow package resolved. Package={Package}, SpecVersion={SpecVersion}, Prompts={PromptCount}", cacheKey, manifest.SpecVersion, prompts?.Count ?? 0);
        return package;
    }

    /// <summary>
    /// The v7 result set: declared entries, or the string result as one-entry
    /// sugar (id "consult" keeps single-result delivery filenames identical to
    /// v6's, package-format-v7.md § 3). v5/v6 resolve null — ResultNodeId is
    /// their contract. Runs after validation, so the declarations are
    /// well-formed; ResultNodeId is populated only for a single-entry set, so
    /// a not-yet-migrated consumer fails loud on multi-deliverable packages.
    /// </summary>
    internal static IReadOnlyList<WorkflowResolvedResult>? ResolveResultSet(WorkflowPackageManifest manifest)
    {
        if (manifest.SpecVersion < 7)
        {
            return null;
        }

        if (manifest.Results is { Count: > 0 })
        {
            return manifest.Results
                .Select(result => new WorkflowResolvedResult(
                    result.Id,
                    result.Node[WorkflowNodeBindingSources.NodePrefix.Length..],
                    result.Label,
                    // Parsed once here; the engine evaluates the structure.
                    // No `when` fails to parse and lands as null, which is
                    // exactly right: a deliverable without a condition always
                    // fires. Anything malformed was refused at publish, so a
                    // parse failure here means an unconditional deliverable
                    // either way.
                    WorkflowResultConditions.TryParseExpression(result.When, out var condition, out _)
                        ? condition
                        : null,
                    // v12 #619: ids in declared order for the append set, and
                    // the placed entries' anchors beside them — null when
                    // every entry is the bare v11 form, the control's bytes.
                    result.Macros?.Select(entry => entry.Id).ToList(),
                    result.Signature,
                    PlacementsOf(result.Macros),
                    result.Check,
                    MacroConditionsOf(result.Macros)))
                .ToList();
        }

        var nodeId = manifest.Result![WorkflowNodeBindingSources.NodePrefix.Length..];
        var label = manifest.Nodes?.FirstOrDefault(node => node.Id == nodeId)?.Label ?? nodeId;
        return new List<WorkflowResolvedResult> { new("consult", nodeId, label) };
    }

    /// <summary>
    /// Downloads and validates the files of a specVersion-2+ package, and resolves
    /// declared schemas to catalog contract ids. Data gathering is two-stage: the
    /// manifest's data table names scalar files and collection index.json files, and
    /// each index names its item files. Missing data blobs are omitted (the validator
    /// reports them coherently); everything else fails loud on 404. Validation
    /// failures throw — the engine's fail-loud enforcement point.
    /// </summary>
    private async Task<(Dictionary<string, WorkflowPromptTemplate> Prompts, Dictionary<string, string> SchemaContracts, WorkflowPackageData? Data, Dictionary<string, string> Files, WorkflowPackageStamp? Stamp, IReadOnlyDictionary<string, OutputContractEntry> ContractAgents, string CatalogRef)> LoadPromptsAsync(
        string name,
        string version,
        WorkflowPackageManifest manifest,
        CancellationToken cancellationToken)
    {
        var packageRef = $"{name}@{version}";
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var paths = (manifest.Prompts ?? new List<WorkflowPromptSpec>()).Select(p => p.File)
            .Concat((manifest.Preludes ?? new Dictionary<string, string>()).Values)
            .Concat((manifest.Schemas ?? new Dictionary<string, string>()).Values)
            // v22 #760: a custom schema's inline body is a package file like a
            // schema — it must be downloaded so the load-time validator finds it
            // and the starter can snapshot it into SourceFiles for the content-
            // addressed agent. (schemaRefs name a catalog contract, no file.)
            .Concat((manifest.CustomSchemas ?? new Dictionary<string, string>()).Values)
            // v11 #513: macro templates are package files like prompts — they
            // must be in SourceFiles for the starter to snapshot.
            .Concat((manifest.Macros ?? new List<WorkflowMacroSpec>()).Select(m => m.File))
            .Distinct(StringComparer.Ordinal);

        foreach (var path in paths)
        {
            files[path] = await DownloadTextAsync(name, $"{name}/{version}/{path}", cancellationToken);
        }

        await GatherDataFilesAsync(name, version, manifest, files, cancellationToken);

        // #433: the publication stamp, when the version has one — every version
        // published before it has none, and those keep re-matching as they
        // did. Held here and on the package, NEVER in `files`: that map is
        // SourceFiles, which the editor round-trips into its next publish, and
        // the publisher refuses a root-level file by path.
        var stampJson = await TryDownloadTextAsync(name, $"{name}/{version}/{WorkflowPackageStamp.FileName}", cancellationToken);
        var stamp = stampJson is null ? null : WorkflowPackageStamp.Read(stampJson, packageRef);

        // #923 phase 2: a stamped package resolves against the catalog version
        // its stamp names (immutable, fetchable forever) — so a global pin bump
        // can never strand it, and it reproducibly runs that version's agents.
        // Unstamped (pre-#433) packages keep re-matching the global catalog.
        var catalog = stamp is null
            ? _catalog
            : await _catalogResolver.GetAsync(stamp.CatalogRef, cancellationToken);

        var catalogSchemas = catalog.Entries.Values
            .Where(entry => entry.SchemaJson != null)
            .ToDictionary(entry => entry.ContractId, entry => entry.SchemaJson!, StringComparer.Ordinal);

        var result = WorkflowPackageValidator.Validate(manifest, files, catalogSchemas, stamp?.Contracts);

        foreach (var warning in result.Warnings)
        {
            _logger.LogWarning("Workflow package {Name}@{Version}: {Warning}", name, version, warning);
        }

        if (!result.IsValid)
        {
            throw new WorkflowPackageContentException(
                $"Workflow package {name}@{version} failed specVersion-{manifest.SpecVersion} validation: {string.Join(" | ", result.Errors)}");
        }

        var prompts = manifest.Prompts!.ToDictionary(
            prompt => prompt.Id,
            prompt => new WorkflowPromptTemplate(
                prompt.Id,
                files[prompt.File],
                prompt.Variables,
                prompt.Prelude is null ? null : files[manifest.Preludes![prompt.Prelude]],
                // v15 (#731): a raw prompt renders verbatim.
                prompt.Raw == true),
            StringComparer.Ordinal);

        var schemaContracts = ResolveContracts(packageRef, manifest, files, stamp, catalog);

        if (manifest.Schemas is { Count: > 0 } || manifest.CustomSchemas is { Count: > 0 })
        {
            _logger.LogInformation(
                "Workflow package {Package} contracts resolved from {Source}.",
                packageRef,
                stamp is null ? $"schema match against {catalog.ResolvedRef}" : $"publication stamp ({stamp.CatalogRef})");
        }

        // Post-validation resolve: the validator has already guaranteed integrity,
        // so this collects no errors.
        var data = WorkflowDataResolver.Resolve(manifest, files, new List<string>());

        return (prompts, schemaContracts, data, files, stamp, catalog.Entries, catalog.ResolvedRef);
    }

    /// <summary>
    /// What each declared schema runs as (#433). Unstamped — every version
    /// published before the stamp existed — re-matches the embedded schema
    /// against the running catalog, and is stranded when it moved. Stamped, the
    /// match was made once at publish under the catalog the stamp names; what
    /// is checked here is only that each stamped contract still exists, so the
    /// activity's lookup cannot fail later. A stamp entry for a schema the
    /// manifest does not declare is inert: the manifest is the declaration.
    /// </summary>
    public static Dictionary<string, string> ResolveContracts(
        string packageRef,
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string> files,
        WorkflowPackageStamp? stamp,
        OutputContractCatalog catalog)
    {
        var schemaContracts = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (schemaId, path) in manifest.Schemas ?? new Dictionary<string, string>())
        {
            if (stamp is null)
            {
                if (!catalog.TryResolveContract(System.Text.Json.Nodes.JsonNode.Parse(files[path]), out var matched))
                {
                    throw WorkflowPackageContentException.SchemaUnmatched(packageRef, schemaId, catalog.ResolvedRef);
                }

                schemaContracts[schemaId] = matched;
                continue;
            }

            if (!stamp.Contracts.TryGetValue(schemaId, out var stampedId))
            {
                throw WorkflowPackageContentException.StampIncomplete(packageRef, schemaId, stamp.CatalogRef);
            }

            if (!catalog.Entries.ContainsKey(stampedId))
            {
                throw WorkflowPackageContentException.StampedContractUnknown(
                    packageRef, schemaId, stampedId, stamp.CatalogRef, catalog.ResolvedRef);
            }

            schemaContracts[schemaId] = stampedId;
        }

        // v21 (#923): a referenced schema names its catalog contract directly —
        // resolved by id, not by matching an embedded body. Phase 1 (#923)
        // resolves against the running catalog, checking only that the named
        // contract still exists (as the stamped path does); per-package pinning
        // is phase 2. The manifest is the declaration, so the ref is read from
        // it (the stamp records the same id→contract, but is not re-read here).
        foreach (var (schemaId, contractId) in manifest.SchemaRefs ?? new Dictionary<string, string>())
        {
            if (!catalog.Entries.ContainsKey(contractId))
            {
                throw WorkflowPackageContentException.StampedContractUnknown(
                    packageRef, schemaId, contractId, stamp?.CatalogRef ?? catalog.ResolvedRef, catalog.ResolvedRef);
            }

            schemaContracts[schemaId] = contractId;
        }

        // v22 (#760): a custom schema resolves to the reserved `custom` contract —
        // a routing marker, NOT a catalog entry (custom has no catalog agent; the
        // agent is content-addressed and provisioned per run). So there is no
        // catalog existence check here, unlike the stamped/referenced paths.
        foreach (var (schemaId, _) in manifest.CustomSchemas ?? new Dictionary<string, string>())
        {
            schemaContracts[schemaId] = OutputContracts.Custom;
        }

        return schemaContracts;
    }

    /// <summary>
    /// Stage one: the data table's scalar files and collection indexes; stage two:
    /// each parseable index's item files. Unparseable indexes and missing blobs are
    /// left to the validator.
    /// </summary>
    private async Task GatherDataFilesAsync(
        string name,
        string version,
        WorkflowPackageManifest manifest,
        Dictionary<string, string> files,
        CancellationToken cancellationToken)
    {
        foreach (var (_, path) in manifest.Data ?? new Dictionary<string, string>())
        {
            if (!path.EndsWith('/'))
            {
                await TryAddBlobAsync(files, name, version, path, cancellationToken);
                continue;
            }

            var indexPath = path + WorkflowDataResolver.IndexFileName;

            if (!await TryAddBlobAsync(files, name, version, indexPath, cancellationToken))
            {
                continue;
            }

            WorkflowDataIndexFile? index;
            try
            {
                index = JsonSerializer.Deserialize<WorkflowDataIndexFile>(files[indexPath], JsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            foreach (var item in index?.Items ?? new List<WorkflowDataIndexItem>())
            {
                if (!string.IsNullOrWhiteSpace(item.File))
                {
                    await TryAddBlobAsync(files, name, version, path + item.File, cancellationToken);
                }
            }
        }
    }

    private async Task<bool> TryAddBlobAsync(
        Dictionary<string, string> files,
        string name,
        string version,
        string path,
        CancellationToken cancellationToken)
    {
        var content = await TryDownloadTextAsync(name, $"{name}/{version}/{path}", cancellationToken);

        if (content is null)
        {
            return false;
        }

        files[path] = content;
        return true;
    }

    /// <summary>A blob that may legitimately be absent: null on 404, everything else as DownloadTextAsync.</summary>
    private async Task<string?> TryDownloadTextAsync(string packageName, string blobPath, CancellationToken cancellationToken)
    {
        try
        {
            return await DownloadTextAsync(packageName, blobPath, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private async Task<string> ResolveLatestVersionAsync(string name, CancellationToken cancellationToken)
    {
        if (_latestCache.TryGetValue(name, out var cached)
            && DateTimeOffset.UtcNow - cached.FetchedAt < LatestPointerCacheDuration)
        {
            return cached.Version;
        }

        var pointerJson = await DownloadTextAsync(name, $"{name}/latest.json", cancellationToken);
        var pointer = JsonSerializer.Deserialize<LatestPointer>(pointerJson, JsonOptions);

        if (pointer is null || !CalVerVersion.TryParse(pointer.Version, out _))
        {
            throw new InvalidOperationException($"Latest pointer for workflow package '{name}' is missing or holds an invalid version.");
        }

        _latestCache[name] = (pointer.Version, DateTimeOffset.UtcNow);
        return pointer.Version;
    }

    /// <summary>
    /// v12 #619: the placed entries only — a bare id carries no anchor and no
    /// entry here. Null (never empty) when nothing is placed.
    /// </summary>

    /// <summary>
    /// v12 #631 (§ 14): the when-gated entries, parsed once — null when no
    /// entry carries when, the control's bytes. A clause that fails to parse
    /// was refused at publish; here it resolves to no gate, the same
    /// always-fires reading the result Condition takes.
    /// </summary>
    private static IReadOnlyList<WorkflowResolvedMacroCondition>? MacroConditionsOf(
        IReadOnlyList<WorkflowResultMacroSpec>? macros)
    {
        var conditions = (macros ?? Array.Empty<WorkflowResultMacroSpec>())
            .Where(entry => entry.When != null)
            .Select(entry => WorkflowResultConditions.TryParseExpression(entry.When, out var condition, out _)
                ? new WorkflowResolvedMacroCondition(entry.Id, condition!)
                : null)
            .Where(entry => entry != null)
            .Select(entry => entry!)
            .ToList();
        return conditions.Count == 0 ? null : conditions;
    }

    private static IReadOnlyList<ConsultMacroPlacement>? PlacementsOf(IReadOnlyList<WorkflowResultMacroSpec>? entries)
    {
        // An anchor, not the object form, is what makes a placement: a
        // when-only entry (§ 14) is an object and IsBare said "placed", so
        // the composer counted its id as placed, matched its null anchor
        // against nothing, and the held arm silently left the document
        // (#623's demo caught it live). Gated-and-appended stays the § 14
        // contract: when alone appends after the sections like the bare form.
        //
        // #863 (v20): a slot entry (Slot == true, no anchor) also carries a
        // placement — the slot filler needs to know which macros this
        // deliverable authorized for inline [[slot:<id>]] filling. It is not
        // "placed" in the composer's sense (Compose skips it); it rides here
        // only to reach the assembly-time filler.
        var placed = entries?
            .Where(entry => entry.Before != null || entry.After != null || entry.Slot == true)
            .Select(entry => new ConsultMacroPlacement(entry.Id, entry.Before, entry.After, entry.ForItem, entry.Slot == true ? true : null))
            .ToList();
        return placed is { Count: > 0 } ? placed : null;
    }

    private async Task<string> DownloadTextAsync(string packageName, string blobPath, CancellationToken cancellationToken)
    {
        // Ownership split: repo-owned names resolve from the public container,
        // acct-* forks from the private pair (#92, #602) — a fork lives in
        // exactly one of the pair, so the first hit is the only hit.
        var candidates = _containers.GetContainersFor(packageName);
        RequestFailedException? lastMiss = null;

        foreach (var container in candidates)
        {
            try
            {
                var response = await container.GetBlobClient(blobPath).DownloadContentAsync(cancellationToken);
                return response.Value.Content.ToString();
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                lastMiss = ex;
            }
        }

        throw new InvalidOperationException(
            $"Workflow package blob '{blobPath}' was not found in {string.Join(", ", candidates.Select(c => $"container '{c.Name}'"))}.", lastMiss);
    }

    private sealed record LatestPointer(string Version);
}
