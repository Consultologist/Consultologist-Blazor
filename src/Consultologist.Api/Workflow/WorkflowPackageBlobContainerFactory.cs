using Azure.Core;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Workflow;

/// <summary>
/// Builds the workflow-package registry's container clients, shared by the
/// read-side store and the registry writer. Two registries since the ownership
/// split (Milestone 6, #92): repo-owned packages live in the PUBLIC account's
/// container (anonymous read — their one and only home); acct-* forks live in
/// the private account's container. Private side is Entra ID first: when a blob
/// service URI is configured, authenticate with the app's managed identity
/// (reads need Storage Blob Data Reader; publishing needs Contributor); the
/// connection-string path remains only as the local-dev fallback (Azurite has
/// no Entra endpoint). When the public URI is unset (local dev), everything
/// routes to the private container, as before the split.
/// </summary>
public sealed class WorkflowPackageBlobContainerFactory
{
    public const string ContainerName = "workflow-packages";

    private readonly BlobContainerClient _privateContainer;
    private readonly BlobContainerClient? _publicContainer;

    public WorkflowPackageBlobContainerFactory(
        IConfiguration configuration,
        TokenCredential credential,
        ILogger<WorkflowPackageBlobContainerFactory> logger)
    {
        var explicitPublicUri = configuration["WorkflowPackages:PublicBlobServiceUri"];
        var publicUri = !string.IsNullOrWhiteSpace(explicitPublicUri)
            ? explicitPublicUri
            : StorageAccounts.DerivedUri(configuration, StorageAccounts.PublicRole, "blob"); // #596
        if (!string.IsNullOrWhiteSpace(publicUri))
        {
            // Public containers are anonymous-read by design; no credential.
            _publicContainer = new BlobServiceClient(new Uri(publicUri)).GetBlobContainerClient(ContainerName);
            logger.LogInformation(
                "Workflow package registry public side configured ({Rung}). PublicBlobServiceUri={PublicBlobServiceUri}",
                string.IsNullOrWhiteSpace(explicitPublicUri) ? "derived from Storage__Region" : "explicit setting",
                publicUri);
        }
        else
        {
            logger.LogWarning("Workflow package registry has no public side (WorkflowPackages__PublicBlobServiceUri and Storage__Region unset); repo-owned packages resolve from the private container.");
        }

        var explicitUri = configuration["WorkflowPackages:BlobServiceUri"];
        var serviceUri = !string.IsNullOrWhiteSpace(explicitUri)
            ? explicitUri
            : StorageAccounts.DerivedUri(configuration, StorageAccounts.RecordsRole, "blob"); // #596
        if (!string.IsNullOrWhiteSpace(serviceUri))
        {
            _privateContainer = new BlobServiceClient(new Uri(serviceUri), credential).GetBlobContainerClient(ContainerName);
            logger.LogInformation(
                "Workflow package registry using Entra ID auth ({Rung}). BlobServiceUri={BlobServiceUri}",
                string.IsNullOrWhiteSpace(explicitUri) ? "derived from Storage__Region" : "explicit setting",
                serviceUri);
            return;
        }

        var connectionStringName = configuration["WorkflowPackages:ConnectionStringName"] ?? "AzureWebJobsStorage";
        var connectionString = configuration[connectionStringName]
            ?? Environment.GetEnvironmentVariable(connectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Workflow package storage is not configured: set WorkflowPackages__BlobServiceUri (Entra ID) or {connectionStringName} (local dev).");
        }

        _privateContainer = new BlobContainerClient(connectionString, ContainerName);
        logger.LogWarning("Workflow package registry using connection-string auth (local-dev fallback). Prefer WorkflowPackages__BlobServiceUri with managed identity.");
    }

    /// <summary>The private container — the registry writer's target (acct-* only).</summary>
    public BlobContainerClient GetContainer() => _privateContainer;

    /// <summary>The public container when configured (#452: the sweep lists it whole).</summary>
    public BlobContainerClient? GetPublicContainer() => _publicContainer;

    public bool HasPublicContainer => _publicContainer != null;

    /// <summary>
    /// The container a package name resolves from: acct-* forks from the private
    /// container, repo-owned names from the public one (when configured).
    /// </summary>
    public BlobContainerClient GetContainerFor(string packageName) =>
        _publicContainer != null && !WorkflowPackageNaming.IsAccountPackage(packageName)
            ? _publicContainer
            : _privateContainer;
}
