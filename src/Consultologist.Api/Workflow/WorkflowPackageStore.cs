using System.Collections.Concurrent;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Consultologist.Api.Agents;
using Microsoft.Extensions.Logging;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Workflow;

public interface IWorkflowPackageStore
{
    Task<WorkflowPackage> ResolveAsync(WorkflowPackageRef packageRef, CancellationToken cancellationToken);
}

public sealed class WorkflowPackageStore : IWorkflowPackageStore
{
    private const string ContainerName = WorkflowPackageBlobContainerFactory.ContainerName;

    /// <summary>A manifest declares the rule set it was validated under (package-format-v6-design.md § 9).</summary>
    // v11 (#566): the engine runs eleven — the last rung of the v11 ladder
    // (package-format-v11-design.md § 12), after the format registry
    // published the v11 document, schema and conformance suite.
    public static readonly IReadOnlyList<int> SupportedSpecVersions = new[] { 5, 6, 7, 8, 9, 10, 11 };
    private static readonly TimeSpan LatestPointerCacheDuration = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly WorkflowPackageBlobContainerFactory _containers;
    private readonly OutputContractCatalog _catalog;
    private readonly ILogger<WorkflowPackageStore> _logger;

    // Published package versions are immutable, so resolved packages cache forever;
    // only the mutable latest-pointers expire.
    private readonly ConcurrentDictionary<string, WorkflowPackage> _packageCache = new();
    private readonly ConcurrentDictionary<string, (string Version, DateTimeOffset FetchedAt)> _latestCache = new();

    public WorkflowPackageStore(
        WorkflowPackageBlobContainerFactory containerFactory,
        OutputContractCatalog catalog,
        ILogger<WorkflowPackageStore> logger)
    {
        _catalog = catalog;
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

        var package = new WorkflowPackage(manifest, prompts, nodes, schemaContracts, loaded.Data, resultNodeId, loaded.Files, results, loaded.Stamp);

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
    private static IReadOnlyList<WorkflowResolvedResult>? ResolveResultSet(WorkflowPackageManifest manifest)
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
                    result.Macros,
                    result.Signature))
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
    private async Task<(Dictionary<string, WorkflowPromptTemplate> Prompts, Dictionary<string, string> SchemaContracts, WorkflowPackageData? Data, Dictionary<string, string> Files, WorkflowPackageStamp? Stamp)> LoadPromptsAsync(
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

        var catalogSchemas = _catalog.Entries.Values
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
                prompt.Prelude is null ? null : files[manifest.Preludes![prompt.Prelude]]),
            StringComparer.Ordinal);

        var schemaContracts = ResolveContracts(packageRef, manifest, files, stamp, _catalog);

        if (manifest.Schemas is { Count: > 0 })
        {
            _logger.LogInformation(
                "Workflow package {Package} contracts resolved from {Source}.",
                packageRef,
                stamp is null ? $"schema match against {_catalog.ResolvedRef}" : $"publication stamp ({stamp.CatalogRef})");
        }

        // Post-validation resolve: the validator has already guaranteed integrity,
        // so this collects no errors.
        var data = WorkflowDataResolver.Resolve(manifest, files, new List<string>());

        return (prompts, schemaContracts, data, files, stamp);
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

    private async Task<string> DownloadTextAsync(string packageName, string blobPath, CancellationToken cancellationToken)
    {
        try
        {
            // Ownership split: repo-owned names resolve from the public container,
            // acct-* forks from the private one (#92).
            var blob = _containers.GetContainerFor(packageName).GetBlobClient(blobPath);
            var response = await blob.DownloadContentAsync(cancellationToken);
            return response.Value.Content.ToString();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new InvalidOperationException($"Workflow package blob '{blobPath}' was not found in container '{ContainerName}'.", ex);
        }
    }

    private sealed record LatestPointer(string Version);
}
