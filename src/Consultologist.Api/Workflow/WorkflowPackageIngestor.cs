using System.Text.Json;
using Consultologist.Api.Agents;
using Consultologist.Api.V31;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;

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
    string? Error,
    // #1508 (specVersion 31, area 11; D15): a 31 source refusal's every sentence, Error the first; null otherwise.
    IReadOnlyList<string>? Errors = null)
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
    private readonly SpecVersionAdmission _admission;
    private readonly PublishedTypeRegistry _types;

    public WorkflowPackageIngestor(
        OutputContractCatalog catalog,
        // #1508 (specVersion 31, area 11; D15): the gate on a 31 import and the registry its source is checked against.
        SpecVersionAdmission? admission = null,
        PublishedTypeRegistry? types = null)
    {
        _catalog = catalog;
        _admission = admission ?? new SpecVersionAdmission(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        _types = types ?? new PublishedTypeRegistry();
    }

    public WorkflowPackageIngestResult Ingest(byte[] zipBytes)
    {
        IReadOnlyDictionary<string, byte[]> entries;
        try
        {
            entries = WorkflowPackageArchive.UnzipBytes(zipBytes);
        }
        catch (InvalidDataException ex)
        {
            return new WorkflowPackageIngestResult(null, Array.Empty<string>(), ex.Message);
        }

        var unzipped = WorkflowPackageArchive.Decode(entries);
        if (V31PackagePublisher.IsV31(unzipped))
        {
            return IngestV31(entries);
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

        var content = WorkflowPackageContentResponse.For(manifest, files);
        return new WorkflowPackageIngestResult(content, findings, null);
    }

    /// <summary>
    /// #1508 (specVersion 31, area 11; part 07 § 7; D15): a 31 zip, its entries' bytes as uploaded. Not admitted (the
    /// preview setting unset): S09-001. Otherwise steps 0 to 2 in memory (<see cref="SourceCheck.Run"/>: intake, which drops
    /// the evidence unread, the reader and every rule): a refusal is every sentence; else the content read-only, with
    /// S07-016 once when the zip held <c>resolve.json</c> or <c>lock.json</c>. Nothing is compiled, tested or stored.
    /// </summary>
    private WorkflowPackageIngestResult IngestV31(IReadOnlyDictionary<string, byte[]> entries)
    {
        if (!_admission.Runs31)
        {
            return new WorkflowPackageIngestResult(null, Array.Empty<string>(), _admission.NotSupported(PublishedStamp.SpecVersion));
        }

        var check = SourceCheck.Run(entries, _types.Registry);
        if (check.Refusals.Count > 0)
        {
            var sentences = check.Refusals.Select(refusal => refusal.Sentence).ToList();
            return new WorkflowPackageIngestResult(null, Array.Empty<string>(), sentences[0], sentences);
        }

        return new WorkflowPackageIngestResult(V31PackageContent.Import(check), Array.Empty<string>(), null);
    }
}
