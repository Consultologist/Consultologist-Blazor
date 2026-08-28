using Microsoft.Extensions.Configuration;
using Bunit;
using Bunit.TestDoubles;
using Consultologist.Web.Services.Accounts;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.AppUpdate;
using Consultologist.Web.Services.Diagnostics;
using Consultologist.Web.Services.Documents;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// The minimum host a Consultologist page needs to reach a rendered state
/// (#224). Everything here is a requirement discovered by tracing the pages'
/// initialization, not defensive scaffolding — see the comments.
/// </summary>
public abstract class ClientRenderTestContext : BunitContext
{
    protected IAIEndpointService AIService { get; } = Substitute.For<IAIEndpointService>();

    protected IWorkflowEndpointService WorkflowService { get; } = Substitute.For<IWorkflowEndpointService>();

    protected IAccountEndpointService AccountService { get; } = Substitute.For<IAccountEndpointService>();

    protected IDocumentEndpointService DocumentService { get; } = Substitute.For<IDocumentEndpointService>();

    // #412: the layout's UpdateBanner asks this whether a newer build waits.
    protected IAppUpdateService AppUpdate { get; } = Substitute.For<IAppUpdateService>();

    protected ConsultJobSession JobSession { get; } = new();

    protected WorkflowEditorSession EditorSession { get; } = new();

    // #515: one location, unchosen — the shipped state.
    protected FakeApiLocations Locations { get; } = new();

    protected ClientRenderTestContext()
    {
        // Fluent components resolve LibraryConfiguration from DI and fail
        // activation without it.
        Services.AddFluentUIComponents();

        // FluentButton, FluentTabs/FluentTab and FluentInputLabel import their
        // .razor.js modules on first render; strict mode would throw on the
        // import rather than on anything we wrote.
        JSInterop.Mode = JSRuntimeMode.Loose;

        // Both pages wrap their content in <AuthorizeView> with no
        // <NotAuthorized> fragment, so an unauthorized render yields an empty
        // body and asserts nothing.
        AddAuthorization().SetAuthorized("clinician@example.com");

        Services.AddSingleton(AIService);
        Services.AddSingleton(WorkflowService);
        Services.AddSingleton(AccountService);
        Services.AddSingleton(DocumentService);
        Services.AddSingleton(AppUpdate);
        Services.AddSingleton(Substitute.For<ISseDiagnosticsService>());
        Services.AddSingleton(JobSession);
        Services.AddSingleton<Consultologist.Web.Services.Locations.IApiLocations>(Locations);

        // One per test context, the same way it is one per browser tab: the
        // editor keeps its open pane here so a page switch returns to it.
        Services.AddSingleton(EditorSession);

        // History.LoadAgentNamesAsync and WorkflowPackagePicker.OnInitializedAsync
        // have no try/catch around these, so a throwing substitute fails the
        // render outright. Null is a value both handle.
        WorkflowService.GetPublicChainAsync().Returns((PublicChainView?)null);
        WorkflowService.GetEngineAsync().Returns((EngineView?)null);

        // History resolves a record's version numbers against the public
        // registry; the base is what production's appsettings carries.
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AzureFunction:PublicRegistryBaseUrl"] = "https://consultologistpublic.blob.core.windows.net" })
            .Build());
        WorkflowService.GetMyPackagesAsync().Returns((IReadOnlyList<PublicPackageView>?)null);
    }

    /// <summary>
    /// The pinned package Consults renders its setup form from. Inputs and
    /// Results null = the frozen v5/v6 shape.
    ///
    /// The version is a parameter because #360 is a version-mismatch bug: with
    /// one hard-coded ref, "sent the current pin" and "sent the previous job's"
    /// are the same string and no test can tell them apart.
    /// </summary>
    protected void WithPinnedPackage(
        IReadOnlyList<WorkflowPackageBlockResponse>? blocks = null,
        IReadOnlyList<WorkflowPackageInputResponse>? inputs = null,
        IReadOnlyList<WorkflowPackageResultResponse>? results = null,
        string version = "v2026.07.10",
        // #429: v9 declares shapes the form renders; the default keeps the
        // v6/v7 reading every earlier test relies on.
        int? specVersion = null,
        // #432: the package's title, shown beside the picker when it has one.
        string? title = null)
    {
        WorkflowService.GetCurrentPackageAsync().Returns(new WorkflowPackageResponse(
            "general", version, specVersion ?? (inputs is null ? 6 : 7), blocks, inputs, results, title));

        // The run rail's enrichment; failures here are swallowed by the page,
        // so a rejected task is a legitimate "content endpoint unavailable".
        WorkflowService.GetCurrentPackageContentAsync()
            .Returns<Task<WorkflowPackageContentResponse>>(_ => throw new InvalidOperationException("not needed for these tests"));
    }

    protected static WorkflowPackageBlockResponse Block(string id, string name) => new(id, name);
}
