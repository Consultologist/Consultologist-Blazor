using System.Collections.Concurrent;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Consultologist.Api.Agents;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Workflow;

public interface IWorkflowPackageStore
{
    Task<WorkflowPackage> ResolveAsync(WorkflowPackageRef packageRef, CancellationToken cancellationToken);
}

/// <summary>
/// A well-formed package this engine will not run: pre-v5 (archived), or a
/// version validated and published ahead of the engine accepting it — which is
/// how v8 lands, validator gate first (package-format-v8-design.md § 8).
///
/// Distinct from a registry failure on purpose. Both used to arrive as a bare
/// InvalidOperationException, so the starter reported "the registry is
/// unavailable" for a package that was sitting there perfectly readable, and
/// logged it as an error. SpecVersionNotYetExecutable already existed for this
/// and was raised nowhere.
/// </summary>
public sealed class WorkflowPackageSpecVersionException : Exception
{
    public WorkflowPackageSpecVersionException(string packageRef, int specVersion, IReadOnlyList<int> supported)
        : base($"Workflow package {packageRef} is specVersion {specVersion}; this engine runs specVersion {string.Join(" or ", supported)}. Pre-v5 packages are archived and not executable.")
    {
        SpecVersion = specVersion;
    }

    public int SpecVersion { get; }
}

/// <summary>
/// A package that resolved and downloaded cleanly, and whose CONTENT this
/// engine will not accept: it fails validation, or a declared schema matches no
/// contract in the loaded catalog.
///
/// The second case is the one worth naming (#374). A published version is
/// immutable, but the schema-to-catalog match is re-evaluated on every load, so
/// a catalog change can strand a package that was valid when it was published —
/// nothing about the package having changed. Reported as "the registry is
/// unavailable" it sent an operator to look at storage, which is fine, for a
/// package which is also fine.
///
/// Same reasoning as WorkflowPackageSpecVersionException above, and the same
/// remedy: say what is actually wrong.
/// </summary>
public sealed class WorkflowPackageContentException : Exception
{
    public WorkflowPackageContentException(string message) : base(message)
    {
    }
}

public sealed class WorkflowPackageStore : IWorkflowPackageStore
{
    private const string ContainerName = WorkflowPackageBlobContainerFactory.ContainerName;

    /// <summary>A manifest declares the rule set it was validated under (package-format-v6-design.md § 9).</summary>
    public static readonly IReadOnlyList<int> SupportedSpecVersions = new[] { 5, 6, 7, 8 };
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
        var manifest = JsonSerializer.Deserialize<WorkflowPackageManifest>(manifestJson, JsonOptions)
            ?? throw new InvalidOperationException($"Workflow package manifest for {cacheKey} is empty or malformed.");

        // Pre-v5 registry versions remain archived artifacts but are not executable
        // (the v5-only rebase; see registry-operations.md).
        if (!SupportedSpecVersions.Contains(manifest.SpecVersion))
        {
            throw new WorkflowPackageSpecVersionException(cacheKey, manifest.SpecVersion, SupportedSpecVersions);
        }

        var loaded = await LoadPromptsAsync(packageRef.Name, version, manifest, cancellationToken);
        var prompts = loaded.Prompts;

        var nodes = manifest.Nodes;
        var results = ResolveResultSet(manifest);
        var resultNodeId = results is null
            ? manifest.Result![WorkflowNodeBindingSources.NodePrefix.Length..]
            : results.Count == 1 ? results[0].NodeId : null;
        var schemaContracts = loaded.SchemaContracts;

        var package = new WorkflowPackage(manifest, prompts, nodes, schemaContracts, loaded.Data, resultNodeId, loaded.Files, results);

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
                    WorkflowResultConditions.TryParse(result.When, out var condition, out _)
                        ? condition
                        : null))
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
    private async Task<(Dictionary<string, WorkflowPromptTemplate> Prompts, Dictionary<string, string> SchemaContracts, WorkflowPackageData? Data, Dictionary<string, string> Files)> LoadPromptsAsync(
        string name,
        string version,
        WorkflowPackageManifest manifest,
        CancellationToken cancellationToken)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var paths = (manifest.Prompts ?? new List<WorkflowPromptSpec>()).Select(p => p.File)
            .Concat((manifest.Preludes ?? new Dictionary<string, string>()).Values)
            .Concat((manifest.Schemas ?? new Dictionary<string, string>()).Values)
            .Distinct(StringComparer.Ordinal);

        foreach (var path in paths)
        {
            files[path] = await DownloadTextAsync(name, $"{name}/{version}/{path}", cancellationToken);
        }

        await GatherDataFilesAsync(name, version, manifest, files, cancellationToken);

        var catalogSchemas = _catalog.Entries.Values
            .Where(entry => entry.SchemaJson != null)
            .ToDictionary(entry => entry.ContractId, entry => entry.SchemaJson!, StringComparer.Ordinal);

        var result = WorkflowPackageValidator.Validate(manifest, files, catalogSchemas);

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

        var schemaContracts = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (schemaId, path) in manifest.Schemas ?? new Dictionary<string, string>())
        {
            if (!_catalog.TryResolveContract(System.Text.Json.Nodes.JsonNode.Parse(files[path]), out var contractId))
            {
                throw new WorkflowPackageContentException(
                    $"Workflow package {name}@{version} schema '{schemaId}' does not canonically match any contract in "
                        + $"{_catalog.ResolvedRef}. The package is unchanged and immutable; the catalog moved.");
            }

            schemaContracts[schemaId] = contractId;
        }

        // Post-validation resolve: the validator has already guaranteed integrity,
        // so this collects no errors.
        var data = WorkflowDataResolver.Resolve(manifest, files, new List<string>());

        return (prompts, schemaContracts, data, files);
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
        try
        {
            files[path] = await DownloadTextAsync(name, $"{name}/{version}/{path}", cancellationToken);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
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
