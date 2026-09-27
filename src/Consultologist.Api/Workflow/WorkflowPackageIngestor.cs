using System.Text.Json;
using Consultologist.Api.Agents;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Workflow;

/// <summary>
/// The outcome of ingesting an uploaded package .zip (#858): either normalized
/// content plus advisory findings (a parseable package, whatever its findings —
/// import is non-blocking, publish is the gate), or an <see cref="Error"/> when
/// the bytes could not be read as a package at all (bad zip, no manifest.json,
/// unreadable manifest). The function turns <see cref="Error"/> into a 400 and a
/// content result into a 200.
/// </summary>
public sealed record WorkflowPackageIngestResult(
    WorkflowPackageContentResponse? Content,
    IReadOnlyList<string> Findings,
    string? Error)
{
    public bool Parsed => Content != null;
}

/// <summary>
/// Server-side package import (#858): unzip an uploaded package, parse its
/// manifest, and run the SAME content validation the publish pipeline runs
/// (<see cref="WorkflowPackageContentValidation"/> paths+closure plus
/// <see cref="WorkflowPackageValidator"/> against the real output-contract
/// catalog), returning the normalized content and the findings. It does not
/// mint, stamp, or store anything — that is publish. This single-sources the
/// unzip/validate the client used to do partially in the browser.
/// </summary>
public sealed class WorkflowPackageIngestor
{
    private readonly OutputContractCatalog _catalog;

    public WorkflowPackageIngestor(OutputContractCatalog catalog)
    {
        _catalog = catalog;
    }

    public WorkflowPackageIngestResult Ingest(byte[] zipBytes)
    {
        IReadOnlyDictionary<string, string> unzipped;
        try
        {
            unzipped = WorkflowPackageArchive.Unzip(zipBytes);
        }
        catch (InvalidDataException ex)
        {
            return new WorkflowPackageIngestResult(null, Array.Empty<string>(), ex.Message);
        }

        var files = new Dictionary<string, string>(unzipped, StringComparer.Ordinal);

        if (!files.TryGetValue("manifest.json", out var manifestText))
        {
            return new WorkflowPackageIngestResult(null, Array.Empty<string>(), "That .zip has no manifest.json — it is not a package.");
        }

        files.Remove("manifest.json");

        WorkflowPackageManifest manifest;
        try
        {
            manifest = WorkflowPackageManifestJson.Read(manifestText, "the imported package", WorkflowPackageValidator.AcceptedSpecVersions);
        }
        catch (JsonException ex)
        {
            return new WorkflowPackageIngestResult(null, Array.Empty<string>(), $"manifest.json is not valid JSON: {WorkflowPackageManifestJson.Describe(ex)}");
        }
        catch (Exception ex)
        {
            // Read throws content exceptions carrying an author-facing sentence
            // (unsupported specVersion, empty/malformed manifest) — surface it.
            return new WorkflowPackageIngestResult(null, Array.Empty<string>(), ex.Message);
        }

        // The same content validation publish runs, with the real catalog the
        // client could only fetch partially (#852). Findings are advisory: a
        // parseable package always loads; publish is the hard gate.
        var findings = new List<string>();
        WorkflowPackageContentValidation.ValidateFilePaths(files, findings);
        WorkflowPackageContentValidation.ValidateFileClosure(manifest, files, findings);

        var catalogSchemas = _catalog.Entries.Values
            .Where(entry => entry.SchemaJson != null)
            .ToDictionary(entry => entry.ContractId, entry => entry.SchemaJson!, StringComparer.Ordinal);

        findings.AddRange(WorkflowPackageValidator.Validate(manifest, files, catalogSchemas).Errors);

        var content = new WorkflowPackageContentResponse(manifest.Name, manifest.Version, manifest.SpecVersion, manifest, files);
        return new WorkflowPackageIngestResult(content, findings, null);
    }
}
