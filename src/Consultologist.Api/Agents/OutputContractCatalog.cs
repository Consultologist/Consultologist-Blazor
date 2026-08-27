using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Azure;
using Azure.Storage.Blobs;
using Consultologist.Api.Workflow;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Agents;

/// <summary>
/// Well-known output-contract ids. The ids name engine-recognized output shapes;
/// packages reference shapes by declaring canonically-matching schemas, never by
/// naming agents (docs/customizable-workflow/output-contract-catalog.md).
/// </summary>
public static class OutputContracts
{
    /// <summary>The prose default: nodes without an output schema.</summary>
    public const string Text = "text";

    /// <summary>The concept-list shape ClinicalConcept consumption is keyed to.</summary>
    public const string ConceptList = "concept-list";

    /// <summary>
    /// v10 (#495): a classifying node's one answer from its declared values —
    /// an object with a single required string `value`. Implied by the
    /// node's kind, never declared by schema id.
    /// </summary>
    public const string Classification = "classification";
}

/// <summary>One catalog entry: the attested agent pinned for an output shape.</summary>
public sealed record OutputContractEntry(
    string ContractId,
    string AgentName,
    string AgentVersion,
    string? SchemaJson);

/// <summary>
/// The output-contract catalog: the engine-side registry mapping schema ids to
/// attested agent pins. Since #93 the catalog is itself a versioned registry
/// artifact: in Azure it loads from the public registry at the pinned version
/// (publish + pin bump + restart changes it, no redeploy), and every job record
/// stamps the resolved concrete ref. The bundled, git-tracked
/// agents/output-contracts.json remains the local-dev source and the attestation
/// baseline. The package declares the contract, the catalog declares the
/// executor, and startup attestation verifies every entry.
/// </summary>
public sealed class OutputContractCatalog : IOutputContractResolver
{
    /// <summary>The registry artifact's name and container: output-contracts@vYYYY.MM.N — the format's constant, aliased.</summary>
    public const string RegistryName = OutputContractRegistry.Name;
    public const string CatalogFileName = "output-contracts.json";

    private readonly Dictionary<string, OutputContractEntry> _entries;

    private OutputContractCatalog(Dictionary<string, OutputContractEntry> entries, string resolvedRef)
    {
        _entries = entries;
        ResolvedRef = resolvedRef;
    }

    public IReadOnlyDictionary<string, OutputContractEntry> Entries => _entries;

    /// <summary>
    /// The concrete ref this catalog instance was built from
    /// ("output-contracts@vYYYY.MM.N") — stamped into every job record beside
    /// agentVersions (docs/customizable-workflow/provenance.md).
    /// </summary>
    public string ResolvedRef { get; }

    /// <summary>
    /// Resolves a contract id to its agent pin. Unknown ids are configuration errors
    /// (fail fast, non-retryable), not anomalies: the validator guarantees package
    /// schemas match catalog entries before any node runs.
    /// </summary>
    public OutputContractEntry GetEntry(string contractId)
    {
        if (!_entries.TryGetValue(contractId, out var entry))
        {
            throw new InvalidOperationException(
                $"Output contract '{contractId}' is not in the catalog. Known contracts: {string.Join(", ", _entries.Keys)}.");
        }

        return entry;
    }

    /// <summary>
    /// Finds the catalog entry whose schema canonically matches (sorted keys,
    /// title/description stripped — the validator's identity rules).
    /// </summary>
    public bool TryResolveContract(JsonNode? schema, out string contractId)
    {
        var canonical = WorkflowPackageValidator.CanonicalizeSchema(schema);

        foreach (var entry in _entries.Values)
        {
            if (entry.SchemaJson is not null
                && WorkflowPackageValidator.CanonicalizeSchema(JsonNode.Parse(entry.SchemaJson)) == canonical)
            {
                contractId = entry.ContractId;
                return true;
            }
        }

        contractId = string.Empty;
        return false;
    }

    /// <summary>
    /// The directory holding output-contracts.json and the agent manifests: the same
    /// AgentAttestation__ManifestDirectory convention the attestation service uses.
    /// </summary>
    public static string ResolveDirectory()
        => Environment.GetEnvironmentVariable("AgentAttestation__ManifestDirectory")
            ?? Path.Combine(AppContext.BaseDirectory, "agents");

    /// <summary>
    /// Loads and validates the catalog from the bundled directory (local dev, tests,
    /// and the attestation baseline). Any defect is a startup failure: an engine
    /// without a coherent catalog must not accept jobs.
    /// </summary>
    public static OutputContractCatalog Load(string? directory = null)
    {
        directory ??= ResolveDirectory();
        var catalogPath = Path.Combine(directory, CatalogFileName);

        if (!File.Exists(catalogPath))
        {
            throw new InvalidOperationException($"Output-contract catalog not found at '{catalogPath}'.");
        }

        return Build(
            File.ReadAllText(catalogPath),
            schemaFile =>
            {
                var schemaPath = Path.Combine(directory, schemaFile);
                return File.Exists(schemaPath) ? File.ReadAllText(schemaPath) : null;
            },
            $"'{catalogPath}'");
    }

    /// <summary>
    /// Loads the catalog from the public registry at the pinned version — the Azure
    /// runtime path. The pin is a ref ("output-contracts@latest" or
    /// "output-contracts@vYYYY.MM.N"); latest resolves through the mutable pointer,
    /// and the returned catalog's ResolvedRef is always concrete.
    /// </summary>
    public static async Task<OutputContractCatalog> LoadFromRegistryAsync(
        Uri publicBlobServiceUri,
        string pin,
        CancellationToken cancellationToken = default)
    {
        if (!WorkflowPackageRef.TryParse(pin, out var pinRef) || !string.Equals(pinRef!.Name, RegistryName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"OutputContracts__Pin '{pin}' is not a valid catalog ref (expected {RegistryName}@latest or {RegistryName}@vYYYY.MM.N).");
        }

        var container = new BlobServiceClient(publicBlobServiceUri).GetBlobContainerClient(RegistryName);

        var version = pinRef.IsLatest
            ? await ResolveLatestVersionAsync(container, cancellationToken)
            : pinRef.Version;

        var catalogJson = await DownloadTextAsync(container, $"{version}/{CatalogFileName}", cancellationToken);

        // Pre-fetch the schema files the catalog references; Build stays synchronous.
        var schemaFiles = new Dictionary<string, string>(StringComparer.Ordinal);
        var file = ParseCatalogFile(catalogJson, $"registry blob '{version}/{CatalogFileName}'");

        foreach (var dto in (file.Contracts ?? new Dictionary<string, CatalogEntryDto>()).Values)
        {
            if (!string.IsNullOrWhiteSpace(dto.SchemaFile) && !schemaFiles.ContainsKey(dto.SchemaFile))
            {
                schemaFiles[dto.SchemaFile] = await DownloadTextAsync(container, $"{version}/{dto.SchemaFile}", cancellationToken);
            }
        }

        var catalog = Build(
            catalogJson,
            schemaFile => schemaFiles.GetValueOrDefault(schemaFile),
            $"registry {RegistryName}@{version}");

        // The artifact must self-describe as the version it was fetched as —
        // integrity between the blob path and the content.
        if (!string.Equals(catalog.ResolvedRef, $"{RegistryName}@{version}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Catalog registry blob '{version}/{CatalogFileName}' declares version '{catalog.ResolvedRef}' — path and content disagree.");
        }

        return catalog;
    }

    /// <summary>Shared core: parse, validate, and assemble entries from catalog JSON and a schema-file reader.</summary>
    internal static OutputContractCatalog Build(string catalogJson, Func<string, string?> readSchemaFile, string source)
    {
        var file = ParseCatalogFile(catalogJson, source);

        if (file.Contracts is null || file.Contracts.Count == 0)
        {
            throw new InvalidOperationException($"Output-contract catalog at {source} declares no contracts.");
        }

        if (string.IsNullOrWhiteSpace(file.Version) || !CalVerVersion.TryParse(file.Version, out _))
        {
            throw new InvalidOperationException(
                $"Output-contract catalog at {source} must declare a CalVer version (\"vYYYY.MM.N\") — the catalog is a versioned registry artifact (#93).");
        }

        var entries = new Dictionary<string, OutputContractEntry>(StringComparer.Ordinal);

        foreach (var (contractId, dto) in file.Contracts)
        {
            if (string.IsNullOrWhiteSpace(contractId)
                || string.IsNullOrWhiteSpace(dto.AgentName)
                || string.IsNullOrWhiteSpace(dto.AgentVersion))
            {
                throw new InvalidOperationException(
                    $"Output-contract catalog entry '{contractId}' must declare agentName and agentVersion.");
            }

            string? schemaJson = null;

            if (!string.IsNullOrWhiteSpace(dto.SchemaFile))
            {
                schemaJson = readSchemaFile(dto.SchemaFile)
                    ?? throw new InvalidOperationException(
                        $"Output-contract catalog entry '{contractId}' references missing schema file '{dto.SchemaFile}' ({source}).");

                try
                {
                    _ = JsonNode.Parse(schemaJson);
                }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException(
                        $"Schema file '{dto.SchemaFile}' for output contract '{contractId}' is not valid JSON: {ex.Message}", ex);
                }
            }

            entries[contractId] = new OutputContractEntry(contractId, dto.AgentName!, dto.AgentVersion!, schemaJson);
        }

        if (!entries.ContainsKey(OutputContracts.Text))
        {
            throw new InvalidOperationException(
                $"Output-contract catalog at {source} must declare the '{OutputContracts.Text}' entry (the prose default).");
        }

        return new OutputContractCatalog(entries, $"{RegistryName}@{file.Version}");
    }

    private static CatalogFile ParseCatalogFile(string catalogJson, string source)
    {
        try
        {
            return JsonSerializer.Deserialize<CatalogFile>(catalogJson)
                ?? throw new InvalidOperationException($"Output-contract catalog at {source} is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Output-contract catalog at {source} is not valid JSON: {ex.Message}", ex);
        }
    }

    private static async Task<string> ResolveLatestVersionAsync(BlobContainerClient container, CancellationToken cancellationToken)
    {
        var pointerJson = await DownloadTextAsync(container, "latest.json", cancellationToken);
        var pointer = JsonSerializer.Deserialize<LatestPointer>(pointerJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (pointer is null || !CalVerVersion.TryParse(pointer.Version, out _))
        {
            throw new InvalidOperationException($"Latest pointer for the '{RegistryName}' registry is missing or holds an invalid version.");
        }

        return pointer.Version!;
    }

    private static async Task<string> DownloadTextAsync(BlobContainerClient container, string blobPath, CancellationToken cancellationToken)
    {
        try
        {
            var response = await container.GetBlobClient(blobPath).DownloadContentAsync(cancellationToken);
            return response.Value.Content.ToString();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new InvalidOperationException(
                $"Catalog registry blob '{blobPath}' was not found in container '{RegistryName}'.", ex);
        }
    }

    private sealed record LatestPointer(string? Version);

    private sealed record CatalogFile(
        [property: JsonPropertyName("version")] string? Version,
        [property: JsonPropertyName("contracts")] Dictionary<string, CatalogEntryDto>? Contracts);

    private sealed record CatalogEntryDto(
        [property: JsonPropertyName("agentName")] string? AgentName,
        [property: JsonPropertyName("agentVersion")] string? AgentVersion,
        [property: JsonPropertyName("schemaFile")] string? SchemaFile);
}
