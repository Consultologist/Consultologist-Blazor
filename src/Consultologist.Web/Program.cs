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

builder.Services.AddMsalAuthentication(options =>
{
    builder.Configuration.Bind("AzureAd", options.ProviderOptions.Authentication);
    options.ProviderOptions.LoginMode = "redirect";
});

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
builder.Services.AddScoped<Consultologist.Web.Services.AI.ConsultJobSession>();
// #412: one watcher per tab; UpdateBanner in MainLayout starts it.
builder.Services.AddScoped<IAppUpdateService, AppUpdateService>();
builder.Services.AddScoped<Consultologist.Web.Services.Workflow.WorkflowEditorSession>();

await builder.Build().RunAsync();
