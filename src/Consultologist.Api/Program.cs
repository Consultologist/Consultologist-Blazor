extern alias AzureIdentity;

using Consultologist.Api;
using Consultologist.Api.Agents;
using Consultologist.Api.Auth;
using Consultologist.Api.Documents;
using Consultologist.Api.Email;
using Consultologist.Api.Jobs;
using Consultologist.Api.Probes;
using Consultologist.Api.RateLimiting;
using Consultologist.Api.Workflow;
using Azure.Core;
using AzureIdentity::Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Consultologist.PackageFormat;
var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Logging.AddConsole();

builder.Services.AddApplicationInsightsTelemetryWorkerService();
builder.Services.ConfigureFunctionsApplicationInsights();

builder.Services.AddSingleton<TokenCredential>(sp =>
{
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Consultologist.Api.StartupDiagnostics");
    var azureClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
    var isRunningInAzure = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID"));

    Console.Error.WriteLine(
        $"[Api.StartupDiagnostics] Creating TokenCredential. Utc={DateTimeOffset.UtcNow:O}; IsRunningInAzure={isRunningInAzure}; HasAzureClientId={!string.IsNullOrWhiteSpace(azureClientId)}");

    logger.LogInformation(
        "Creating TokenCredential. IsRunningInAzure={IsRunningInAzure}, HasAzureClientId={HasAzureClientId}",
        isRunningInAzure,
        !string.IsNullOrWhiteSpace(azureClientId));

    var credentialOptions = new DefaultAzureCredentialOptions
    {
        ExcludeInteractiveBrowserCredential = true,
        ExcludeVisualStudioCredential = true,
        ExcludeVisualStudioCodeCredential = true,
        ExcludeAzurePowerShellCredential = true,
        Retry = { NetworkTimeout = TimeSpan.FromSeconds(10) }
    };

    if (!string.IsNullOrWhiteSpace(azureClientId))
    {
        credentialOptions.ManagedIdentityClientId = azureClientId;
    }

    var credential = new DefaultAzureCredential(credentialOptions);

    Console.Error.WriteLine($"[Api.StartupDiagnostics] TokenCredential created. Utc={DateTimeOffset.UtcNow:O}");
    logger.LogInformation("TokenCredential created.");

    return credential;
});

builder.Services.AddSingleton(_ =>
{
    // Azure: the catalog is content, loaded from the public registry at the
    // pinned version (publish + pin bump + restart, no redeploy). Local dev:
    // the bundled agents/ directory. Both fail loud — no coherent catalog, no
    // jobs (#93).
    var publicUri = Environment.GetEnvironmentVariable("WorkflowPackages__PublicBlobServiceUri");
    var pin = Environment.GetEnvironmentVariable("OutputContracts__Pin") ?? $"{OutputContractCatalog.RegistryName}@latest";

    return string.IsNullOrWhiteSpace(publicUri)
        ? OutputContractCatalog.Load()
        : OutputContractCatalog.LoadFromRegistryAsync(new Uri(publicUri), pin).GetAwaiter().GetResult();
});
builder.Services.AddScoped<AgentSectionGenerator>();
builder.Services.AddSingleton<IBearerTokenValidator, BearerTokenValidator>();
builder.Services.AddSingleton<IAccountStore, AccountStore>();
builder.Services.AddSingleton<IAccountSettingsStore, AccountSettingsStore>();
builder.Services.AddSingleton<IConsultGenerationJobEventStore, TableConsultGenerationJobEventStore>();
builder.Services.AddSingleton<IConsultGenerationJobIndexStore, TableConsultGenerationJobIndexStore>();
// #546: the links index — the lineage graph, ids only, records account.
builder.Services.AddSingleton<IConsultGenerationLinkStore, TableConsultGenerationLinkStore>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<WorkflowPackageBlobContainerFactory>();
builder.Services.AddSingleton<IWorkflowPackageStore, WorkflowPackageStore>();
builder.Services.AddSingleton<IWorkflowPackageRegistryWriter, WorkflowPackageRegistryWriter>();
builder.Services.AddSingleton<WorkflowPackagePublisher>();
builder.Services.AddSingleton<WorkflowPackageLineageResolver>();
builder.Services.AddSingleton<IWorkflowPackageOwnership, WorkflowPackageOwnership>();
builder.Services.AddSingleton<IWorkflowPackagePinResolver, WorkflowPackagePinResolver>();
builder.Services.AddSingleton<PublicRegistryReader>();
builder.Services.AddSingleton<PublicChain>();
// #449/#398: the build's attestation, computed once — served at Public/Engine
// and stamped on every record's packageFormatRef/provenanceRef.
builder.Services.AddSingleton(sp => EngineAttestation.Current(
    sp.GetRequiredService<OutputContractCatalog>(),
    sp.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()[EngineAttestation.ApiHostSetting]));
builder.Services.AddSingleton<PublicEngine>();
// #403: what the terminology server says about itself, read once per cache window.
builder.Services.AddSingleton<ITerminologyAttestationSource, TerminologyAttestationClient>();
builder.Services.AddScoped<WorkflowPackages>();
builder.Services.AddHttpClient();
builder.Services.AddHostedService<AgentAttestationService>();
builder.Services.AddSingleton<PinHealthReporter>();
// #557: the text account's outputs blobs — written at completion, read on
// GET, deleted by the retention drop.
builder.Services.AddSingleton<IJobOutputsBlobStore, JobOutputsBlobStore>();
// #547: the held inputs — written by the starter, read by History and the
// rerun, deleted by the retention drop.
builder.Services.AddSingleton<IJobInputsBlobStore, JobInputsBlobStore>();
// #368: the retention sweep — the text is deleted N days after completion.
builder.Services.AddSingleton<IJobTextPurger, JobTextPurger>();
// #557: the events-move transition's old-table delete; removed by #558.
builder.Services.AddSingleton<ILegacyJobEventDelete, LegacyJobEventDelete>();
builder.Services.AddSingleton<TextRetentionSweep>();
builder.Services.AddSingleton<IWorkflowPackageRegistryReader, WorkflowPackageRegistryReader>();
builder.Services.AddSingleton<CatalogStrandSweeper>();
builder.Services.AddSingleton<OperatorCatalogStrands>();
builder.Services.AddSingleton<OperatorPinHealth>();
builder.Services.AddHostedService<PinHealthStartupService>();
builder.Services.AddTransient<ConsultGenerationJobEntity>();
builder.Services.AddSingleton<ILinkedInLinkStateStore, LinkedInLinkStateStore>();
builder.Services.AddSingleton<ILinkedInTokenClient, LinkedInTokenClient>();
builder.Services.AddSingleton<ILinkedInIdTokenValidator, LinkedInIdTokenValidator>();
builder.Services.AddSingleton<ILinkedInVerificationClient, LinkedInVerificationClient>();
builder.Services.AddSingleton<ILinkedInLinkService, LinkedInLinkService>();
builder.Services.AddScoped<IAccountAuthorizer, AccountAuthorizer>();
builder.Services.AddScoped<Account>();
builder.Services.AddScoped<AccountLinkedIn>();
builder.Services.AddScoped<Diagnostics>();
builder.Services.AddSingleton<IAccountRateLimiter, TableAccountRateLimiter>();
builder.Services.AddSingleton<IConsultGenerationJobStarter, ConsultGenerationJobStarter>();
builder.Services.AddSingleton<IGraphMailClient, GraphMailClient>();
builder.Services.AddSingleton<IEmailSenderResolver, TableEmailSenderResolver>();
builder.Services.AddSingleton<IEmailIntakeClaimStore, TableEmailIntakeClaimStore>();
builder.Services.AddSingleton<EmailIntakeProcessor>();
builder.Services.AddScoped<EmailIntakeFunctions>();
builder.Services.AddScoped<SendEmailIntakeReplyActivity>();
builder.Services.AddScoped<ConsultGenerationJobs>();
// #235: the parser itself is static and needs no configuration; only its
// Function class is resolved.
builder.Services.AddScoped<DocumentExtractions>();
builder.Services.AddScoped<RunPromptNodeActivity>();
// v10 (#496): the boundary.
builder.Services.AddScoped<DecideActivity>();

builder.Build().Run();
