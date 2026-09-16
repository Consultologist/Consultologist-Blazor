using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Consultologist.Web;
using Consultologist.Web.Services.Accounts;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.AppUpdate;
using Consultologist.Web.Services.Diagnostics;
using Consultologist.Web.Services.Documents;
using Consultologist.Web.Services.Workflow;
using Microsoft.FluentUI.AspNetCore.Components;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddFluentUIComponents();

#if E2E
// #710: a test-only fake auth path so a headless browser can reach the
// [Authorize]-gated pages. Compiled ONLY under -p:E2E=true (never the prod
// build). The endpoint services request a token per call, so the token
// provider is faked too; Playwright mocks the API responses.
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider, Consultologist.Web.E2eAuth.StateProvider>();
builder.Services.AddScoped<IAccessTokenProvider, Consultologist.Web.E2eAuth.TokenProvider>();
#else
builder.Services.AddMsalAuthentication(options =>
{
    builder.Configuration.Bind("AzureAd", options.ProviderOptions.Authentication);
    options.ProviderOptions.LoginMode = "redirect";
});
#endif

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Register AI Endpoint Service with separate HttpClient (no Graph auth handler)
var agentProxyTimeoutSeconds = builder.Configuration.GetValue<int?>("AzureFunction:TimeoutSeconds") ?? 240;
builder.Services.AddHttpClient<IAIEndpointService, AIEndpointService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(agentProxyTimeoutSeconds);
});

// #515: the location the app talks to — read from this device before the
// first call; every endpoint service builds its URLs on it.
builder.Services.AddScoped<Consultologist.Web.Services.Locations.IApiLocations, Consultologist.Web.Services.Locations.ApiLocations>();
builder.Services.AddHttpClient<IAccountEndpointService, AccountEndpointService>();
builder.Services.AddHttpClient<ISseDiagnosticsService, SseDiagnosticsService>();
builder.Services.AddHttpClient<IWorkflowEndpointService, WorkflowEndpointService>();
builder.Services.AddHttpClient<IDocumentEndpointService, DocumentEndpointService>();
// #733: the operator panel moved to the standalone admin app (Consultologist.Admin).
// #540: the held form responses — the setup form's picker reads them.
builder.Services.AddHttpClient<Consultologist.Web.Services.Forms.IFormsIntakeEndpointService, Consultologist.Web.Services.Forms.FormsIntakeEndpointService>();
builder.Services.AddScoped<Consultologist.Web.Services.AI.ConsultJobSession>();
// #412: one watcher per tab; UpdateBanner in MainLayout starts it.
builder.Services.AddScoped<IAppUpdateService, AppUpdateService>();
builder.Services.AddScoped<Consultologist.Web.Services.Workflow.WorkflowEditorSession>();

await builder.Build().RunAsync();
