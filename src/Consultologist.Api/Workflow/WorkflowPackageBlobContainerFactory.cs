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
        var publicUri = configuration["WorkflowPackages:PublicBlobServiceUri"];
        if (!string.IsNullOrWhiteSpace(publicUri))
        {
            // Public containers are anonymous-read by design; no credential.
            _publicContainer = new BlobServiceClient(new Uri(publicUri)).GetBlobContainerClient(ContainerName);
            logger.LogInformation("Workflow package registry public side configured. PublicBlobServiceUri={PublicBlobServiceUri}", publicUri);
        }
        else
        {
            logger.LogWarning("Workflow package registry has no public side (WorkflowPackages__PublicBlobServiceUri unset); repo-owned packages resolve from the private container.");
        }

        var serviceUri = configuration["WorkflowPackages:BlobServiceUri"];
        if (!string.IsNullOrWhiteSpace(serviceUri))
        {
            _privateContainer = new BlobServiceClient(new Uri(serviceUri), credential).GetBlobContainerClient(ContainerName);
            logger.LogInformation("Workflow package registry using Entra ID auth. BlobServiceUri={BlobServiceUri}", serviceUri);
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

    /// <summary>
    /// The container a package name resolves from: acct-* forks from the private
    /// container, repo-owned names from the public one (when configured).
    /// </summary>
    public BlobContainerClient GetContainerFor(string packageName) =>
        _publicContainer != null && !WorkflowPackageNaming.IsAccountPackage(packageName)
            ? _publicContainer
            : _privateContainer;
}
