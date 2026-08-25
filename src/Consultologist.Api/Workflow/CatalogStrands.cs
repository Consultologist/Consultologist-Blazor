using System.Text.Json;
using Consultologist.Api.Agents;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Workflow;

/// <summary>
/// #452: would a candidate catalog strand a published package version? The
/// report lists only the versions that need attention; the counts say the
/// rest. Package refs, schema ids and catalog refs are the content — no PHI.
/// The property sets are pinned by test.
/// </summary>
public sealed record CatalogStrandResponse(
    string Candidate,
    string? Engine,
    DateTimeOffset GeneratedAtUtc,
    CatalogStrandCounts Counts,
    IReadOnlyList<CatalogStrandVersion> Versions);

public sealed record CatalogStrandCounts(
    int Versions,
    int Checked,
    int Stamped,
    int SkippedUnsupportedSpec,
    int SkippedNoSchema,
    bool PublicRegistryRead);

public sealed record CatalogStrandVersion(
    string Ref,
    string Status,
    IReadOnlyList<CatalogStrandSchema> Schemas,
    int PinnedBy);

public sealed record CatalogStrandSchema(string SchemaId, string Reason);

public static class CatalogStrandSkips
{
    public const string UnsupportedSpec = "unsupported-spec";
    public const string NoSchema = "no-schema";
}

public static class CatalogStrands
{
    /// <summary>
    /// Permissive, unlike the load and publish paths: this asks whether a
    /// catalog would strand a version, and a manifest-shape complaint is not
    /// that question (general@v2026.07.4 uses retired vocabulary and must
    /// still be answered for).
    /// </summary>
    private static readonly JsonSerializerOptions Permissive = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// One version against the candidate. Null means healthy or skipped
    /// (<paramref name="skip"/> says which); otherwise the version with one
    /// reason per schema that fails. <paramref name="readFile"/> fetches a
    /// declared schema file by manifest-relative path, null when missing.
    /// </summary>
    public static CatalogStrandVersion? Check(
        string packageRef,
        string manifestJson,
        string? stampJson,
        Func<string, string?> readFile,
        OutputContractCatalog candidate,
        IReadOnlyList<int> supported,
        out string? skip,
        out bool stamped)
    {
        skip = null;
        stamped = false;

        WorkflowPackageManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<WorkflowPackageManifest>(manifestJson, Permissive)
                ?? throw new JsonException("empty manifest");
        }
        catch (Exception ex)
        {
            return new CatalogStrandVersion(packageRef, PinHealthStatuses.Unreadable,
                new[] { new CatalogStrandSchema("(manifest)", $"manifest could not be read: {ex.GetType().Name}") }, 0);
        }

        // Already unloadable whatever this catalog says: the store refuses the
        // spec version before it ever consults a contract.
        if (!supported.Contains(manifest.SpecVersion))
        {
            skip = CatalogStrandSkips.UnsupportedSpec;
            return null;
        }

        if (manifest.Schemas is not { Count: > 0 })
        {
            skip = CatalogStrandSkips.NoSchema;
            return null;
        }

        WorkflowPackageStamp? stamp = null;
        if (stampJson != null)
        {
            try
            {
                stamp = WorkflowPackageStamp.Read(stampJson, packageRef);
                stamped = true;
            }
            catch (WorkflowPackageContentException ex)
            {
                return new CatalogStrandVersion(packageRef, PinHealthStatuses.Unreadable,
                    new[] { new CatalogStrandSchema("(stamp)", ex.Message) }, 0);
            }
        }

        var failures = new List<CatalogStrandSchema>();
        var status = PinHealthStatuses.Stranded;

        foreach (var (schemaId, path) in manifest.Schemas)
        {
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            if (stamp == null)
            {
                var text = readFile(path);
                if (text == null)
                {
                    failures.Add(new CatalogStrandSchema(schemaId, $"schema file '{path}' is missing from the published version"));
                    status = PinHealthStatuses.Unreadable;
                    continue;
                }

                files[path] = text;
            }

            try
            {
                // The store's own premises, one schema at a time so each gets its sentence.
                WorkflowPackageStore.ResolveContracts(
                    packageRef,
                    manifest with { Schemas = new Dictionary<string, string> { [schemaId] = path } },
                    files,
                    stamp,
                    candidate);
            }
            catch (WorkflowPackageContentException ex)
            {
                failures.Add(new CatalogStrandSchema(schemaId, ex.Message));
            }
            catch (Exception ex)
            {
                failures.Add(new CatalogStrandSchema(schemaId, ex.GetType().Name));
                status = PinHealthStatuses.Unreadable;
            }
        }

        return failures.Count == 0 ? null : new CatalogStrandVersion(packageRef, status, failures, 0);
    }

    /// <summary>A candidate must be a concrete, published catalog version.</summary>
    public static string? ValidateCandidate(string? candidate)
    {
        if (!WorkflowPackageRef.TryParse(candidate, out var parsed) || parsed == null)
        {
            return $"candidate must be {OutputContractCatalog.RegistryName}@vYYYY.MM.N";
        }

        if (!string.Equals(parsed.Name, OutputContractCatalog.RegistryName, StringComparison.Ordinal))
        {
            return $"candidate names '{parsed.Name}'; the catalog registry is {OutputContractCatalog.RegistryName}";
        }

        if (parsed.IsLatest)
        {
            return "candidate must be a concrete version, not @latest: the question is whether a specific catalog would strand";
        }

        return null;
    }
}
