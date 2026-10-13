using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Consultologist.PackageFormat;

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
    IReadOnlyDictionary<string, string> Contracts,
    // v25 (package-format-v25-design.md § 3.7, B2 #1001): the formats catalogue
    // the package resolved its formats against (formats@vYYYY.MM.N) and, per
    // format a node or a parameter names, the hash of the format file's
    // canonical JSON and who stands behind it. Trailing optionals, omitted
    // when null, so every stamp written before 25 keeps its bytes; a writer
    // emits them only for a package at 25, which the reader accepts first
    // (the rule above).
    string? FormatsRef = null,
    IReadOnlyDictionary<string, WorkflowStampedFormat>? Formats = null)
{
    public const string FileName = "publish.json";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // v25: the formats members are absent below 25 — omitted, never null,
        // so every stamp written before them keeps its bytes.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
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
        IOutputContractResolver catalog,
        List<string> errors) =>
        Compute(manifest, files, catalog, null, errors);

    /// <summary>
    /// v25 (§ 3.7): at 25 the stamp records the formats catalogue version and,
    /// per format the package names, the file's canonical hash and its agent or
    /// operation — a catalogue format from <paramref name="formats"/>, a
    /// package-local one from its own file with the catalogue's custom binding.
    /// Below 25 the formats members stay null and the bytes stay as they were.
    /// </summary>
    public static WorkflowPackageStamp Compute(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string> files,
        IOutputContractResolver catalog,
        WorkflowFormatCatalog? formats,
        List<string> errors)
    {
        var contracts = new SortedDictionary<string, string>(StringComparer.Ordinal);

        if (manifest.SpecVersion >= 25)
        {
            return ComputeAt25(manifest, files, catalog, formats, errors);
        }

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

        // v21 (#923): a referenced schema names its catalog contract directly —
        // no body to load or match. The validator has already checked the
        // contract exists in the catalog; here it is recorded like any other,
        // so the stamp shape stays {catalogRef, contracts} with no new member.
        foreach (var (schemaId, contractId) in manifest.SchemaRefs ?? new Dictionary<string, string>())
        {
            contracts[schemaId] = contractId;
        }

        // v22 (#760): a custom schema records the reserved `custom` contract id —
        // no body match (custom is not a catalog contract). The stamp shape stays
        // {catalogRef, contracts} with no new member.
        foreach (var (schemaId, _) in manifest.CustomSchemas ?? new Dictionary<string, string>())
        {
            contracts[schemaId] = WorkflowNodeDefaults.CustomSchemaId;
        }

        return new WorkflowPackageStamp(catalog.ResolvedRef, contracts);
    }

    private static WorkflowPackageStamp ComputeAt25(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string> files,
        IOutputContractResolver catalog,
        WorkflowFormatCatalog? formats,
        List<string> errors)
    {
        if (formats is null)
        {
            errors.Add("The format catalogue is not available on this engine; a specVersion 25 package cannot be stamped.");
            return new WorkflowPackageStamp(catalog.ResolvedRef, new Dictionary<string, string>());
        }

        var stamped = new SortedDictionary<string, WorkflowStampedFormat>(StringComparer.Ordinal);
        var local = (manifest.Formats ?? new List<WorkflowFormatReference>()).ToDictionary(f => f.Id, f => f.File, StringComparer.Ordinal);

        // The catalogue ids the package names: on its nodes, and through the
        // parameters of those formats (a terms-subset names concept-list).
        var named = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var node in manifest.Nodes ?? new List<WorkflowNodeSpec>())
        {
            if (node.Format is { } id)
            {
                named.Add(id);
            }
        }

        foreach (var id in named.ToList())
        {
            if (!local.ContainsKey(id) && formats.TryGet(id, out var format))
            {
                foreach (var parameter in format!.Spec.Parameters?.Values ?? Enumerable.Empty<WorkflowFormatParameterSpec>())
                {
                    if (parameter.Format is { } referenced)
                    {
                        named.Add(referenced);
                    }
                }
            }
        }

        foreach (var id in named)
        {
            if (local.TryGetValue(id, out var path))
            {
                if (!files.TryGetValue(path, out var json))
                {
                    errors.Add($"Format '{id}' file '{path}' is missing from the package.");
                    continue;
                }

                string canonical;
                try
                {
                    canonical = WorkflowFormatSpecJson.CanonicalJson(json);
                }
                catch (JsonException ex)
                {
                    errors.Add($"Format '{id}' file '{path}' does not parse as JSON: {ex.Message}");
                    continue;
                }

                stamped[id] = new WorkflowStampedFormat(
                    WorkflowFormatSpecJson.Sha256Hex(canonical),
                    WorkflowFormatProducers.Custom,
                    formats.CustomProducer.AgentName,
                    formats.CustomProducer.AgentVersion);
                continue;
            }

            if (!formats.TryGet(id, out var catalogueFormat))
            {
                errors.Add($"Format '{id}' is neither a package format nor a format of {formats.ResolvedRef}.");
                continue;
            }

            var entry = catalogueFormat!.Entry;
            stamped[id] = new WorkflowStampedFormat(
                WorkflowFormatSpecJson.Sha256Hex(catalogueFormat.CanonicalJson),
                catalogueFormat.Spec.Producer,
                entry.AgentName,
                entry.AgentVersion,
                entry.Op);
        }

        return new WorkflowPackageStamp(catalog.ResolvedRef, new Dictionary<string, string>(), formats.ResolvedRef, stamped);
    }

    /// <summary>
    /// The byte form: camelCase, contract keys in ordinal order, two-space
    /// indent, LF newlines, one trailing newline — stable across writers.
    /// </summary>
    public string ToJson()
    {
        var document = new StampDocument(
            CatalogRef,
            new SortedDictionary<string, string>(Contracts.ToDictionary(pair => pair.Key, pair => pair.Value), StringComparer.Ordinal),
            FormatsRef,
            Formats is null ? null : new SortedDictionary<string, WorkflowStampedFormat>(Formats.ToDictionary(pair => pair.Key, pair => pair.Value), StringComparer.Ordinal));

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
            || !string.Equals(catalogRef.Name, OutputContractRegistry.Name, StringComparison.Ordinal))
        {
            throw new WorkflowPackageContentException(
                $"{prefix} must declare catalogRef as {OutputContractRegistry.Name}@vYYYY.MM.N.");
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

        // v25: the formats members, when present, must be whole — a stamp from
        // a 25 publish names its catalogue and every format it resolved.
        if (document.FormatsRef is not null
            && (!WorkflowPackageRef.TryParse(document.FormatsRef, out var formatsRef)
                || formatsRef!.IsLatest
                || !string.Equals(formatsRef.Name, WorkflowFormatRegistry.Name, StringComparison.Ordinal)))
        {
            throw new WorkflowPackageContentException(
                $"{prefix} must declare formatsRef as {WorkflowFormatRegistry.Name}@vYYYY.MM.N.");
        }

        if ((document.FormatsRef is null) != (document.Formats is null))
        {
            throw new WorkflowPackageContentException(
                $"{prefix} must declare formatsRef and formats together (a specVersion 25 publish writes both).");
        }

        foreach (var (formatId, format) in document.Formats ?? new SortedDictionary<string, WorkflowStampedFormat>(StringComparer.Ordinal))
        {
            if (format is null || string.IsNullOrWhiteSpace(format.FormatHash) || format.FormatHash.Length != 64 || !format.FormatHash.All(Uri.IsHexDigit))
            {
                throw new WorkflowPackageContentException($"{prefix} format '{formatId}' must carry a formatHash (64 hex characters).");
            }

            if (!WorkflowFormatProducers.IsKnown(format.Producer))
            {
                throw new WorkflowPackageContentException($"{prefix} format '{formatId}' producer '{format.Producer}' is not one of {string.Join(", ", WorkflowFormatProducers.All)}.");
            }

            if ((format.AgentName is null) != (format.AgentVersion is null))
            {
                throw new WorkflowPackageContentException($"{prefix} format '{formatId}' must carry agentName and agentVersion together.");
            }

            if (format.AgentName is not null && format.Op is not null)
            {
                throw new WorkflowPackageContentException($"{prefix} format '{formatId}' names both an agent and an op.");
            }
        }

        return new WorkflowPackageStamp(
            document.CatalogRef,
            new Dictionary<string, string>(document.Contracts, StringComparer.Ordinal),
            document.FormatsRef,
            document.Formats is null ? null : new Dictionary<string, WorkflowStampedFormat>(document.Formats, StringComparer.Ordinal));
    }

    private sealed record StampDocument(
        string? CatalogRef,
        SortedDictionary<string, string>? Contracts,
        string? FormatsRef = null,
        SortedDictionary<string, WorkflowStampedFormat>? Formats = null);
}

/// <summary>
/// v25 (§ 3.7): one stamped format — the hash of its file's canonical JSON
/// (the identity of a shape in the record is its bytes, not its name), its
/// producer, and the agent or the operation behind it. A package-local format
/// is <c>custom</c> and carries the catalogue's generic agent.
/// </summary>
public sealed record WorkflowStampedFormat(
    string FormatHash,
    string Producer,
    string? AgentName = null,
    int? AgentVersion = null,
    string? Op = null);
