using Consultologist.Api;
using Consultologist.Api.Workflow;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// The registrations are ApiServices.AddApiServices (#1503), so a test builds the container the host builds.
var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Logging.AddConsole();

builder.Services.AddApplicationInsightsTelemetryWorkerService();
builder.Services.ConfigureFunctionsApplicationInsights();

builder.Services.AddApiServices();

var host = builder.Build();
// #1503: fail at startup, not at the first job, on a bad preview or limit setting (#1504: or 31 agents catalogue).
ApiServices.ResolveAtStartup(host.Services);
host.Run();
