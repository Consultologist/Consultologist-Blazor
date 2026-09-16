using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Consultologist.Admin;
using Consultologist.Admin.Services.Operators;
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

// #733: the admin app talks to one API base (from appsettings), not the
// clinician SPA's multi-region picker — it is a single-deployment ops tool.
// The server gate (CanUseApp && Operators.IsOperator) is the real boundary;
// this client only asks, with the operator's token.
builder.Services.AddHttpClient<IOperatorEndpointService, OperatorEndpointService>();

await builder.Build().RunAsync();
