#!/usr/bin/env dotnet
#:project ../src/Consultologist.Api/Consultologist.Api.csproj
// Same reason as validate-workflow-package.cs: this script is neither trimmed
// nor AOT'd, and the manifest model has no source-generated context.
#:property JsonSerializerIsReflectionEnabledByDefault=true

// Answers one question before a catalog version is published: would it strand
// an already-published package? (#374)
//
// Usage:
//   dotnet run -v q --file scripts/check-catalog-strands-packages.cs -- <candidate-catalog-dir>
//   dotnet run -v q --file scripts/check-catalog-strands-packages.cs -- <candidate-catalog-dir> --registry <blob-service-uri>
//
// <candidate-catalog-dir> is the agents repo's agents/ directory — the catalog
// as it would be published, read before anything is uploaded.
//
// WHY THIS EXISTS. A published package version is immutable; whether it still
// LOADS is not. A package carries a COPY of each schema, and
// WorkflowPackageStore re-matches that copy against the live catalog on every
// load. Change or retire a contract and an already-published package stops
// loading, with nothing about the package having changed. It surfaces as
// "Workflow package registry is unavailable" — infrastructure blamed for a
// content publish — and the remedy is a new version number, never a fix.
//
// The agents repo publishes on merge to main, uploading the version and moving
// latest.json in one run, so there is no moment after the fact at which to
// notice. This is that moment, moved before it.
//
// WHAT IT CHECKS, AND WHAT IT DELIBERATELY DOES NOT. Only whether each declared
// schema still canonically matches SOME contract in the candidate catalog —
// TryResolveContract, which is exactly what WorkflowPackageStore calls. Not the
// full validator: published `general` spans specVersion 1 through 8, and
// anything below 5 is already unloadable whatever the catalog says, so a full
// validate would report old versions as failures for a reason that has nothing
// to do with this publish. A package declaring no schemas is immune outright —
// nothing consults the catalog for it.
//
// Read-only. It lists and downloads from the PUBLIC registry anonymously and
// writes nothing anywhere.

using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Storage.Blobs;
using Consultologist.Api.Agents;
using Consultologist.Api.Workflow;
using Microsoft.Extensions.Configuration;

const string DefaultRegistry = "https://consultologistpublic.blob.core.windows.net";
const string PackagesContainer = "workflow-packages";

var positional = args.Where(argument => !argument.StartsWith("--", StringComparison.Ordinal)).ToList();
var registryIndex = Array.IndexOf(args, "--registry");
var registryUri = registryIndex >= 0 && registryIndex + 1 < args.Length
    ? args[registryIndex + 1]
    : DefaultRegistry;

// --registry's value is positional-looking; drop it from the candidate slot.
var catalogDir = positional.FirstOrDefault(value => !string.Equals(value, registryUri, StringComparison.Ordinal));

if (catalogDir is null)
{
    Console.Error.WriteLine(
        "usage: dotnet run --file scripts/check-catalog-strands-packages.cs -- <candidate-catalog-dir> [--registry <uri>]");
    return 2;
}

if (!File.Exists(Path.Combine(catalogDir, OutputContractCatalog.CatalogFileName)))
{
    Console.Error.WriteLine($"error: no {OutputContractCatalog.CatalogFileName} in {Path.GetFullPath(catalogDir)}");
    return 2;
}

OutputContractCatalog candidate;
try
{
    candidate = OutputContractCatalog.Load(catalogDir);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: candidate catalog does not load: {ex.Message}");
    return 2;
}

Console.Out.WriteLine($"candidate: {candidate.ResolvedRef} ({candidate.Entries.Count} contracts)");

// The same reader the public chain endpoint uses, so the path shapes and the
// per-version specVersion come from one place rather than being re-derived.
var reader = new PublicRegistryReader(new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["WorkflowPackages:PublicBlobServiceUri"] = registryUri
    })
    .Build());

PublicChainResponse chain;
try
{
    chain = await reader.BuildChainAsync(CancellationToken.None);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: could not read the public registry at {registryUri}: {ex.Message}");
    return 2;
}

var packages = new BlobServiceClient(new Uri(registryUri)).GetBlobContainerClient(PackagesContainer);

var stranded = new List<string>();
var checkedCount = 0;
var skipped = 0;

foreach (var package in chain.Packages.OrderBy(p => p.Name, StringComparer.Ordinal))
{
    foreach (var version in package.Versions)
    {
        var reference = $"{package.Name}@{version}";
        var spec = package.SpecVersions?.GetValueOrDefault(version);

        // Already unloadable whatever this catalog says: the store refuses the
        // spec version before it ever consults a contract.
        if (spec is not int specVersion || !WorkflowPackageStore.SupportedSpecVersions.Contains(specVersion))
        {
            skipped++;
            continue;
        }

        WorkflowPackageManifest? manifest;
        try
        {
            var manifestJson = await DownloadAsync($"{package.Name}/{version}/manifest.json");
            // Permissive, unlike the load and publish paths (#416): this sweeps
            // every published version to ask whether a catalog would strand one,
            // and a manifest-shape complaint is not that question. A read
            // failure aborts the whole sweep, which would be a disproportionate
            // answer to a stray property in one historical version.
            manifest = JsonSerializer.Deserialize<WorkflowPackageManifest>(
                manifestJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {reference} manifest could not be read: {ex.Message}");
            return 2;
        }

        var schemas = manifest?.Schemas;

        // No declared schema, nothing to strand: the catalog is never consulted
        // for this package.
        if (schemas is not { Count: > 0 })
        {
            skipped++;
            continue;
        }

        checkedCount++;

        foreach (var (schemaId, path) in schemas)
        {
            JsonNode? schema;
            try
            {
                schema = JsonNode.Parse(await DownloadAsync($"{package.Name}/{version}/{path}"));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: {reference} schema '{schemaId}' could not be read: {ex.Message}");
                return 2;
            }

            if (candidate.TryResolveContract(schema, out var contractId))
            {
                Console.Out.WriteLine($"  {reference} schema '{schemaId}' -> {contractId}");
                continue;
            }

            // The sentence the store would throw at pin resolve, said here
            // instead — where the catalog can still be changed.
            Console.Error.WriteLine(
                $"error: {reference} schema '{schemaId}' would no longer match any contract in {candidate.ResolvedRef}.");
            stranded.Add($"{reference} ({schemaId})");
        }
    }
}

Console.Out.WriteLine(
    $"{checkedCount} package version(s) declare a schema, {skipped} skipped (unsupported specVersion or no schema), "
        + $"{stranded.Count} would be stranded");

if (stranded.Count > 0)
{
    Console.Error.WriteLine(
        "error: publishing this catalog would strand: " + string.Join(", ", stranded)
            + ". Published versions are immutable, so the remedy would be a new package version, not a fix.");
    return 1;
}

return 0;

async Task<string> DownloadAsync(string blobPath)
{
    var response = await packages.GetBlobClient(blobPath).DownloadContentAsync();
    return response.Value.Content.ToString();
}
