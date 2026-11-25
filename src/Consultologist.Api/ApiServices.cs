extern alias AzureIdentity;

using Azure.Core;
using AzureIdentity::Azure.Identity;
using Consultologist.Api.Agents;
using Consultologist.Api.Auth;
using Consultologist.Api.Documents;
using Consultologist.Api.Email;
using Consultologist.Api.Jobs;
using Consultologist.Api.Probes;
using Consultologist.Api.RateLimiting;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api;

/// <summary>
/// The host's service registrations, as <c>Program.cs</c> makes them: one method, so a test can build the same
/// container without starting the host (#1503: <c>CompositionTests</c>). The host adds Application Insights and its
/// own configuration and logging around it.
/// </summary>
public static class ApiServices
{
    /// <summary>
    /// What <c>Program.cs</c> resolves after the host is built and before it runs, so a bad setting or catalogue fails at
    /// startup, not at the first job: #1503's <see cref="SpecVersionAdmission"/> and
    /// <see cref="Consultologist.Api.V31.EngineLimitSettings"/>, and #1504's <see cref="Consultologist.Api.V31.V31AgentsCatalogue"/>
    /// when <see cref="SpecVersionAdmission.Runs31"/>, so a defect in it stops the host rather than failing a 31 job. With the
    /// preview setting unset it is not read at all (#1505 R15): its consumers take it lazily (the 31 store's engine
    /// capabilities, the 31 agent activity's catalogue), and only a 31 load or a 31 agent call reads it.
    /// </summary>
    public static void ResolveAtStartup(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var admission = services.GetRequiredService<SpecVersionAdmission>();
        services.GetRequiredService<Consultologist.Api.V31.EngineLimitSettings>();
        // #1505 (R15): only a host that admits 31 resolves the 31 catalogue; a 5–30 host never touches agents/v31.
        if (admission.Runs31)
        {
            services.GetRequiredService<Consultologist.Api.V31.V31AgentsCatalogue>();
        }
    }

    public static IServiceCollection AddApiServices(this IServiceCollection services)
    {
        services.AddSingleton<TokenCredential>(sp =>
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

        services.AddSingleton(_ =>
        {
            // Azure: the catalog is content, loaded from the public registry at the
            // pinned version (publish + pin bump + restart, no redeploy). Local dev:
            // the bundled agents/ directory. Both fail loud — no coherent catalog, no
            // jobs (#93).
            var publicUri = Environment.GetEnvironmentVariable("WorkflowPackages__PublicBlobServiceUri");
            if (string.IsNullOrWhiteSpace(publicUri))
            {
                publicUri = StorageAccounts.DerivedUriFromEnvironment(StorageAccounts.PublicRole, "blob"); // #596
            }

            var pin = Environment.GetEnvironmentVariable("OutputContracts__Pin") ?? $"{OutputContractCatalog.RegistryName}@latest";

            return string.IsNullOrWhiteSpace(publicUri)
                ? OutputContractCatalog.Load()
                : OutputContractCatalog.LoadFromRegistryAsync(new Uri(publicUri), pin).GetAwaiter().GetResult();
        });
        // #923 phase 2: loads a stamped package's own catalog version on demand (cached
        // forever — versions are immutable), so stamped packages resolve against the
        // catalog they were published under rather than the global pin. The singleton
        // above stays the engine default.
        services.AddSingleton<CatalogResolver>();
        // v25 (B2 #1001): the formats catalogue — the same shape of plumbing as the
        // output contracts: content loaded from the public registry at Formats__Pin
        // (default formats@latest), the bundled agents/ directory locally, both fail
        // loud; a per-ref resolver for stamped packages beside the global singleton.
        services.AddSingleton(_ =>
        {
            var publicUri = Environment.GetEnvironmentVariable("WorkflowPackages__PublicBlobServiceUri");
            if (string.IsNullOrWhiteSpace(publicUri))
            {
                publicUri = StorageAccounts.DerivedUriFromEnvironment(StorageAccounts.PublicRole, "blob");
            }

            var pin = Environment.GetEnvironmentVariable(FormatCatalogLoader.PinSetting) ?? FormatCatalogLoader.DefaultPin;

            return string.IsNullOrWhiteSpace(publicUri)
                ? FormatCatalogLoader.Load()
                : FormatCatalogLoader.LoadFromRegistryAsync(new Uri(publicUri), pin).GetAwaiter().GetResult();
        });
        services.AddSingleton<FormatCatalogResolver>();
        services.AddScoped<AgentSectionGenerator>();
        // #239: OCR for image-only PDFs. Singleton so the Document Intelligence client
        // is built once and reused; off unless DocumentExtraction__OcrEndpoint is set.
        services.AddSingleton<Consultologist.Api.Documents.IDocumentOcr, Consultologist.Api.Documents.AzureDocumentIntelligenceOcr>();
        // #1063: speech-to-text for recordings. Singleton as OCR is; off unless
        // SpeechToText__Endpoint is set.
        services.AddSingleton<Consultologist.Api.Documents.ISpeechToText, Consultologist.Api.Documents.AzureSpeechToText>();
        services.AddSingleton<IBearerTokenValidator, BearerTokenValidator>();
        services.AddSingleton<IAccountStore, AccountStore>();
        services.AddSingleton<IAccountSettingsStore, AccountSettingsStore>();
        services.AddSingleton<IConsultGenerationJobEventStore, TableConsultGenerationJobEventStore>();
        // #1506 (D10): a 31 job's record rows; the table is created on a first write.
        services.AddSingleton<Consultologist.Api.V31.IRecordRowStore, Consultologist.Api.V31.TableRecordRowStore>();
        services.AddSingleton<IConsultGenerationJobIndexStore, TableConsultGenerationJobIndexStore>();
        // #546: the links index — the lineage graph, ids only, records account.
        services.AddSingleton<IConsultGenerationLinkStore, TableConsultGenerationLinkStore>();
        // #559: the closure audit — one row per closed account, counts only.
        services.AddSingleton<Consultologist.Api.Auth.IClosedAccountsStore, Consultologist.Api.Auth.TableClosedAccountsStore>();
        services.AddSingleton<Consultologist.Api.Auth.AccountClosure>();
        services.AddScoped<Consultologist.Api.Auth.OperatorAccounts>();
        // #552: the derived usage store — day counts, written once at completion.
        services.AddSingleton<IAccountUsageStore, TableAccountUsageStore>();
        // #539: held form responses — values on the text account, the list row on records.
        services.AddSingleton<Consultologist.Api.Forms.IFormResponseBlobStore, Consultologist.Api.Forms.FormResponseBlobStore>();
        services.AddSingleton<Consultologist.Api.Forms.IFormResponseStore, Consultologist.Api.Forms.TableFormResponseStore>();
        services.AddSingleton<Consultologist.Api.Forms.IFormResponsePurger, Consultologist.Api.Forms.FormResponsePurger>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<WorkflowPackageBlobContainerFactory>();
        // #1503: one store, forwarded to both its interfaces (one instance, one pointer cache).
        services.AddSingleton<WorkflowPackageStore>();
        services.AddSingleton<IWorkflowPackageStore>(sp => sp.GetRequiredService<WorkflowPackageStore>());
        services.AddSingleton<IWorkflowPackageVersions>(sp => sp.GetRequiredService<WorkflowPackageStore>());
        services.AddSingleton<IWorkflowPackageRegistryWriter, WorkflowPackageRegistryWriter>();
        services.AddSingleton<WorkflowPackagePublisher>();
        services.AddSingleton<WorkflowPackageIngestor>();
        services.AddSingleton<WorkflowPackageLineageResolver>();
        services.AddSingleton<IWorkflowPackageOwnership, WorkflowPackageOwnership>();
        services.AddSingleton<IWorkflowPackagePinResolver, WorkflowPackagePinResolver>();
        services.AddSingleton<PublicRegistryReader>();
        services.AddSingleton<PublicChain>();
        // #1503 (part 09 § 2.1): which spec versions this engine admits — the compiled-in
        // list, and beside it WorkflowPackages__PreviewSpecVersions (dev only). The 31
        // engine's limits (ConsultGeneration__*). Both are resolved below, before the host
        // runs, so a bad value stops the host at startup.
        services.AddSingleton<SpecVersionAdmission>();
        services.AddSingleton<Consultologist.Api.V31.EngineLimitSettings>();
        // #1503 (part 09 § 1.2): the 31 load — the stamped resolve.json and lock.json, verified,
        // held to what this engine runs (its catalogues' attested agents among it), the plan
        // cached. The version is resolved through the 5–30 store's own latest pointer and cache
        // (IWorkflowPackageVersions, registered with the store above).
        services.AddSingleton<Consultologist.Api.V31.IRegistryFiles, Consultologist.Api.V31.BlobRegistryFiles>();
        // #1504 (D9): the 31 agents catalogue (agents/v31, bundled from the pinned agents repo), loaded and attested once:
        // its agents are among what this engine runs, and an agent call's sent format leaves out their strictRejects.
        // #1505 (R15): its consumers take it, and the capabilities built from it, lazily, so a host that does not run 31
        // never reads agents/v31 (only ResolveAtStartup, when 31 is admitted, reads it before the first job).
        services.AddSingleton(_ => Consultologist.Api.V31.V31AgentsCatalogue.Load());
        services.AddSingleton(sp => new Lazy<Consultologist.Api.V31.V31AgentsCatalogue>(
            () => sp.GetRequiredService<Consultologist.Api.V31.V31AgentsCatalogue>()));
        services.AddSingleton<Consultologist.Api.V31.EngineCapabilitiesFactory>();
        services.AddSingleton(sp => sp.GetRequiredService<Consultologist.Api.V31.EngineCapabilitiesFactory>().Current());
        services.AddSingleton(sp => new Lazy<Consultologist.PackageFormat.V31.EngineCapabilities>(
            () => sp.GetRequiredService<Consultologist.PackageFormat.V31.EngineCapabilities>()));
        services.AddSingleton<Consultologist.Api.V31.ICompiledPackageStore, Consultologist.Api.V31.CompiledPackageStore>();
        // #1503 (part 09 §§ 3.1, 3.4): a 31 job's orchestration — one attempt per agent activity
        // (the orchestration retries; #1504: its prompt composed by the template library), and the
        // inputs held by reference as the job's held inputs (JobInputsBlobStore's containers and names).
        services.AddScoped<IAgentAttempts>(sp => sp.GetRequiredService<AgentSectionGenerator>());
        services.AddSingleton<Consultologist.Api.V31.IRunInputsStore, Consultologist.Api.V31.RunInputsBlobStore>();
        // #1506 (area 09; R4): the toolchain a 31 job runs with, for its versionsRan (load-run-inputs-v31's); the 31 agents
        // catalogue is read only when a 31 job asks.
        services.AddSingleton<Consultologist.Api.V31.EngineToolchain>();
        services.AddSingleton<Consultologist.Api.V31.V31JobStarter>();
        // #1505 (part 06 § 8; D10): a 31 job's delivery channels, email and pdf, over the mailbox and the account's
        // delivery settings, as the 5–30 reply reads them (deliver-v31's).
        services.AddSingleton<Consultologist.Api.V31.DeliveryChannels>();
        // #1505 (part 09 § 3.9; D14): alerts for the operator, logged and mailed from the mailbox to Operators__AlertAddress when
        // both are set (alert-operator-v31's); the weekly summary's function reads the job index on its own schedule.
        services.AddSingleton<Consultologist.Api.V31.IOperatorAlerts, Consultologist.Api.V31.OperatorAlerts>();
        // #1507 (area 10; R11, D22): the summary's hand-edits line migrates each account's active package as stored (the
        // store's raw read), against the bundled 31 type registry, read on first use (area 11's publish path reuses it).
        services.AddSingleton<IWorkflowPackageSources>(sp => sp.GetRequiredService<WorkflowPackageStore>());
        services.AddSingleton(_ => new Consultologist.Api.V31.PublishedTypeRegistry());
        services.AddSingleton<Consultologist.Api.V31.OutstandingHandEdits>();
        // #1508 (area 11): WorkflowPackages/Publish's 31 branch, entered only where the preview setting admits 31; its
        // registry writer is a placeholder until Task 17's.
        services.AddSingleton<Consultologist.Api.V31.IV31RegistryWriter, Consultologist.Api.V31.PendingV31RegistryWriter>();
        services.AddSingleton<Consultologist.Api.V31.V31PackagePublisher>();
        // #1503 (part 09 § 3.6; D9): the drain before a deploy — its state on the text account (drain/ in the job outputs'
        // container), read on every call; both starters park behind it; Admin/Drain, Admin/Resume and GET Admin/Drain drive it.
        services.AddSingleton<IDrainBlobs, TextDrainBlobs>();
        services.AddSingleton<IEngineDrain, EngineDrain>();
        services.AddScoped<AdminDrain>();
        // #449/#398: the build's attestation, computed once — served at Public/Engine
        // and stamped on every record's packageFormatRef/provenanceRef.
        services.AddSingleton(sp => EngineAttestation.Current(
            sp.GetRequiredService<OutputContractCatalog>(),
            sp.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()[EngineAttestation.ApiHostSetting],
            // #1063: the deployment's capabilities — the Create page's door reads them.
            new EngineFeatures(sp.GetRequiredService<Consultologist.Api.Documents.ISpeechToText>().IsConfigured),
            // #1503: the versions WorkflowPackages__PreviewSpecVersions admits, attested apart.
            sp.GetRequiredService<SpecVersionAdmission>().Preview));
        services.AddSingleton<PublicEngine>();
        // #403: what the terminology server says about itself, read once per cache window.
        services.AddSingleton<ITerminologyAttestationSource, TerminologyAttestationClient>();
        services.AddScoped<WorkflowPackages>();
        services.AddHttpClient();
        services.AddHostedService<AgentAttestationService>();
        services.AddSingleton<PinHealthReporter>();
        // #557: the text account's outputs blobs — written at completion, read on
        // GET, deleted by the retention drop.
        services.AddSingleton<IJobOutputsBlobStore, JobOutputsBlobStore>();
        // #1506 (D10): a 31 job's retained text and canonical record, beside the outputs.
        services.AddSingleton<Consultologist.Api.V31.IRecordBlobStore, Consultologist.Api.V31.BlobRecordStore>();
        // #1506 (D13): the record's sealer over a function's Durable client (seal-record-v31, the cancel route).
        services.AddSingleton<Consultologist.Api.V31.RecordSealers>();
        // #1506 (D16): a 31 job's rows and record over HTTP (GET ConsultGenerationJobs/{jobId}/Rows and /Record).
        services.AddScoped<Consultologist.Api.V31.RecordRoutes>();
        // #547: the held inputs — written by the starter, read by History and the
        // rerun, deleted by the retention drop.
        services.AddSingleton<JobInputsBlobStore>();
        services.AddSingleton<IJobInputsBlobStore>(sp => sp.GetRequiredService<JobInputsBlobStore>());
        // #368: the retention sweep — the text is deleted N days after completion.
        services.AddSingleton<Consultologist.Api.V31.IRecordTextDrop, Consultologist.Api.V31.RecordTextDrop>();
        services.AddSingleton<IJobTextPurger, JobTextPurger>();
        // #557: the events-move transition's old-table delete; removed by #558.
        services.AddSingleton<ILegacyJobEventDelete, LegacyJobEventDelete>();
        services.AddSingleton<TextRetentionSweep>();
        services.AddSingleton<IWorkflowPackageRegistryReader, WorkflowPackageRegistryReader>();
        services.AddSingleton<CatalogStrandSweeper>();
        services.AddSingleton<OperatorCatalogStrands>();
        services.AddSingleton<OperatorPinHealth>();
        // v25 (B6 #1005): the formats catalogue read endpoint for the editor.
        services.AddSingleton<WorkflowFormats>();
        // #553: the operator usage panel — reads only the derived store.
        services.AddSingleton<OperatorUsage>();
        services.AddHostedService<PinHealthStartupService>();
        services.AddTransient<ConsultGenerationJobEntity>();
        services.AddSingleton<ILinkedInLinkStateStore, LinkedInLinkStateStore>();
        services.AddSingleton<ILinkedInTokenClient, LinkedInTokenClient>();
        services.AddSingleton<ILinkedInIdTokenValidator, LinkedInIdTokenValidator>();
        // #654: the Epic SMART id_token validator for the identity-link flow.
        services.AddSingleton<IEpicIdTokenValidator, EpicIdTokenValidator>();
        services.AddSingleton<ICernerIdTokenValidator, CernerIdTokenValidator>();
        services.AddSingleton<ILinkedInVerificationClient, LinkedInVerificationClient>();
        services.AddSingleton<ILinkedInLinkService, LinkedInLinkService>();
        services.AddScoped<IAccountAuthorizer, AccountAuthorizer>();
        services.AddScoped<Account>();
        services.AddScoped<AccountLinkedIn>();
        services.AddScoped<AccountEpic>();
        services.AddScoped<AccountCerner>();
        // #669: the marketplace activation door — the resolve (landing) + webhook
        // endpoints and the SaaS Fulfillment API client (WIF token + raw REST + the
        // webhook JWT validator).
        services.AddScoped<AccountMarketplace>();
        services.AddSingleton<Consultologist.Api.Marketplace.IMarketplaceTokenClient, Consultologist.Api.Marketplace.MarketplaceTokenClient>();
        services.AddSingleton<Consultologist.Api.Marketplace.IMarketplaceFulfillmentClient, Consultologist.Api.Marketplace.MarketplaceFulfillmentClient>();
        services.AddSingleton<Consultologist.Api.Marketplace.IMarketplaceJwtValidator, Consultologist.Api.Marketplace.MarketplaceJwtValidator>();
        // #725: the paid personal-account door — the Stripe Checkout (landing) + webhook
        // endpoints, the raw-REST Stripe client (static secret key, no S2S exchange), and the
        // Stripe-Signature HMAC validator.
        services.AddScoped<AccountStripe>();
        services.AddSingleton<Consultologist.Api.Stripe.IStripeClient, Consultologist.Api.Stripe.StripeClient>();
        services.AddSingleton<Consultologist.Api.Stripe.IStripeSignatureValidator, Consultologist.Api.Stripe.StripeSignatureValidator>();
        services.AddScoped<Diagnostics>();
        services.AddSingleton<IAccountRateLimiter, TableAccountRateLimiter>();
        // #1302: the release hold (Jobs__HoldNewJobs), read on every start.
        services.AddSingleton<IJobIntakeHold, JobIntakeHold>();
        services.AddSingleton<IConsultGenerationJobStarter, ConsultGenerationJobStarter>();
        services.AddSingleton<IGraphMailClient, GraphMailClient>();
        // #615: the On-Behalf-Of exchange — the engine reaching outward as the user.
        services.AddSingleton<IOnBehalfOfTokenClient, OnBehalfOfTokenClient>();
        services.AddSingleton<IGraphDocumentFetcher, GraphDocumentFetcher>();
        services.AddSingleton<IEmailSenderResolver, TableEmailSenderResolver>();
        services.AddSingleton<IEmailIntakeClaimStore, TableEmailIntakeClaimStore>();
        services.AddSingleton<EmailIntakeProcessor>();
        services.AddScoped<EmailIntakeFunctions>();
        services.AddScoped<Consultologist.Api.Forms.FormsIntake>();
        services.AddScoped<SendEmailIntakeReplyActivity>();
        services.AddScoped<ConsultGenerationJobs>();
        // #235: the parser itself is static and needs no configuration; only its
        // Function class is resolved.
        services.AddScoped<DocumentExtractions>();
        services.AddScoped<RunPromptNodeActivity>();
        // v10 (#496): the boundary.
        services.AddScoped<DecideActivity>();

        return services;
    }
}
