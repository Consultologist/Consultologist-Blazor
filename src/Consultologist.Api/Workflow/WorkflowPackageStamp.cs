using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Consultologist.Api.Agents;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Workflow;

/// <summary>
/// The publication stamp (#433, #377): what each declared schema resolved to,
/// and the catalog it was resolved under, recorded once at publish as
/// <c>publish.json</c> beside the manifest. Load takes its word — checking
/// only that the stamped contracts still exist — instead of re-matching the
/// embedded schema against whatever catalog is running, which is what
/// stranded immutable packages when the catalog moved (#374).
///
/// Registry-layer, never format: the manifest stays the author's declaration
/// and byte-round-trippable through the editor; the stamp is evidence beside
/// it, the way <c>derivedFrom</c> is server-asserted. Written by the publisher
/// and by scripts/stamp-workflow-package.cs, never by a client — a request
/// carrying one is refused by path.
///
/// Read strictly, as the manifest is (#416): the same writer produces every
/// stamp, so an unknown member is a foreign writer or a deploy skew, and both
/// deserve a sentence. The rule that keeps strict safe: a new member lands on
/// this record and deploys before any writer emits it.
/// </summary>
public sealed record WorkflowPackageStamp(
    string CatalogRef,
    IReadOnlyDictionary<string, string> Contracts)
{
    public const string FileName = "publish.json";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // Byte-identical between the Function App and a developer's or CI's
        // machine: the default follows the OS.
        NewLine = "\n"
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>
    /// One contract per declared schema — every entry of <c>schemas</c>, not
    /// only the ones a node references. The validator closes only the
    /// referenced ones, so an unreferenced schema matching nothing used to
    /// publish and strand at load; here it is an error the publisher refuses.
    /// Errors rather than throws: this runs at the publish desk.
    /// </summary>
    public static WorkflowPackageStamp Compute(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string> files,
        OutputContractCatalog catalog,
        List<string> errors)
    {
        var contracts = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var (schemaId, path) in manifest.Schemas ?? new Dictionary<string, string>())
        {
            if (!files.TryGetValue(path, out var schemaText))
            {
                errors.Add($"Schema '{schemaId}' file '{path}' is missing from the package.");
                continue;
            }

            JsonNode? schema;
            try
            {
                schema = JsonNode.Parse(schemaText);
            }
            catch (JsonException ex)
            {
                errors.Add($"Schema '{schemaId}' does not parse as JSON: {ex.Message}");
                continue;
            }

            if (!catalog.TryResolveContract(schema, out var contractId))
            {
                errors.Add($"Schema '{schemaId}' matches no contract in {catalog.ResolvedRef}.");
                continue;
            }

            contracts[schemaId] = contractId;
        }

        return new WorkflowPackageStamp(catalog.ResolvedRef, contracts);
    }

    /// <summary>
    /// The byte form: camelCase, contract keys in ordinal order, two-space
    /// indent, LF newlines, one trailing newline — stable across writers.
    /// </summary>
    public string ToJson()
    {
        var document = new StampDocument(
            CatalogRef,
            new SortedDictionary<string, string>(Contracts.ToDictionary(pair => pair.Key, pair => pair.Value), StringComparer.Ordinal));

        return JsonSerializer.Serialize(document, WriteOptions) + "\n";
    }

    /// <summary>Strict read; every refusal is a <see cref="WorkflowPackageContentException"/> naming the package.</summary>
    public static WorkflowPackageStamp Read(string json, string packageRef)
    {
        var prefix = $"Workflow package {packageRef} {FileName}";
        StampDocument? document;

        try
        {
            document = JsonSerializer.Deserialize<StampDocument>(json, ReadOptions);
        }
        catch (JsonException ex)
        {
            // Malformed JSON fails at the root ("$"); an unknown member names
            // its own path below it.
            throw new WorkflowPackageContentException(string.IsNullOrWhiteSpace(ex.Path) || ex.Path == "$"
                ? $"{prefix} is not valid JSON."
                : $"{prefix} property '{ex.Path}' is not part of the publication stamp.");
        }

        if (document is null)
        {
            throw new WorkflowPackageContentException($"{prefix} is not valid JSON.");
        }

        if (document.CatalogRef is null
            || !WorkflowPackageRef.TryParse(document.CatalogRef, out var catalogRef)
            || catalogRef!.IsLatest
            || !string.Equals(catalogRef.Name, OutputContractCatalog.RegistryName, StringComparison.Ordinal))
        {
            throw new WorkflowPackageContentException(
                $"{prefix} must declare catalogRef as {OutputContractCatalog.RegistryName}@vYYYY.MM.N.");
        }

        if (document.Contracts is null)
        {
            throw new WorkflowPackageContentException(
                $"{prefix} must declare contracts (schema id to contract id; empty when the package declares no schemas).");
        }

        foreach (var (schemaId, contractId) in document.Contracts)
        {
            if (string.IsNullOrWhiteSpace(contractId))
            {
                throw new WorkflowPackageContentException($"{prefix} contract for schema '{schemaId}' is blank.");
            }
        }

        return new WorkflowPackageStamp(
            document.CatalogRef,
            new Dictionary<string, string>(document.Contracts, StringComparer.Ordinal));
    }

    private sealed record StampDocument(string? CatalogRef, SortedDictionary<string, string>? Contracts);
}
