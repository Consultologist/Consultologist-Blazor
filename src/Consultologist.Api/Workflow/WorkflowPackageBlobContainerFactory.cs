using Azure.Core;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Consultologist.Api.Jobs;
using Consultologist.PackageFormat;
namespace Consultologist.Api.Workflow;

/// <summary>
/// Builds the workflow-package registry's container clients, shared by the
/// read-side store and the registry writer. Two registries since the ownership
/// split (Milestone 6, #92): repo-owned packages live in the PUBLIC account's
/// container (anonymous read — their one and only home); acct-* forks live on
/// the private account. Since #602 the private side is an org/personal pair
/// chosen by AccountKind (storage-separation.md § 2.6 — the same pairing as
/// text): the writer targets the kind's container, and context-free readers
/// try both, which is safe because a fork name derives from one appUserId and
/// so lives in exactly one of them. Private side is Entra ID first: when a
/// blob service URI is configured, authenticate with the app's managed
/// identity (reads need Storage Blob Data Reader; publishing needs
/// Contributor); the connection-string path remains only as the local-dev
/// fallback (Azurite has no Entra endpoint). When the public URI is unset
/// (local dev), repo-owned names route to the private pair, as before the
/// ownership split.
/// </summary>
public sealed class WorkflowPackageBlobContainerFactory
{
    /// <summary>The PUBLIC registry's container — the private pair below replaced it on the private side (#602).</summary>
    public const string ContainerName = "workflow-packages";

    public const string OrganisationContainerName = "org-account-packages";
    public const string PersonalContainerName = "personal-account-packages";

    /// <summary>The private container the account's kind names — TextBlobNaming's rule: null falls to personal.</summary>
    public static string ContainerFor(string? accountKind) =>
        TextBlobNaming.ContainerFor(accountKind, OrganisationContainerName, PersonalContainerName);

    private readonly BlobContainerClient _privateOrganisationContainer;
    private readonly BlobContainerClient _privatePersonalContainer;
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
            var privateService = new BlobServiceClient(new Uri(serviceUri), credential);
            _privateOrganisationContainer = privateService.GetBlobContainerClient(OrganisationContainerName);
            _privatePersonalContainer = privateService.GetBlobContainerClient(PersonalContainerName);
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

        _privateOrganisationContainer = new BlobContainerClient(connectionString, OrganisationContainerName);
        _privatePersonalContainer = new BlobContainerClient(connectionString, PersonalContainerName);
        logger.LogWarning("Workflow package registry using connection-string auth (local-dev fallback, containers {Org} and {Personal}). Prefer WorkflowPackages__BlobServiceUri with managed identity.", OrganisationContainerName, PersonalContainerName);
    }

    /// <summary>The kind's private container — the registry writer's target (acct-* only).</summary>
    public BlobContainerClient GetContainer(string? accountKind) =>
        ContainerFor(accountKind) == OrganisationContainerName
            ? _privateOrganisationContainer
            : _privatePersonalContainer;

    /// <summary>Both private containers, fixed order — for kind-blind sweeps and deletes.</summary>
    public IReadOnlyList<BlobContainerClient> GetPrivateContainers() =>
        new[] { _privateOrganisationContainer, _privatePersonalContainer };

    /// <summary>The public container when configured (#452: the sweep lists it whole).</summary>
    public BlobContainerClient? GetPublicContainer() => _publicContainer;

    public bool HasPublicContainer => _publicContainer != null;

    /// <summary>
    /// The candidate containers a package name resolves from, in try order:
    /// repo-owned names from the public one (when configured), acct-* forks
    /// from the private pair. A fork lives in exactly one of the pair, so the
    /// first hit is the only hit.
    /// </summary>
    public IReadOnlyList<BlobContainerClient> GetContainersFor(string packageName) =>
        _publicContainer != null && !WorkflowPackageNaming.IsAccountPackage(packageName)
            ? new[] { _publicContainer }
            : new[] { _privateOrganisationContainer, _privatePersonalContainer };
}
