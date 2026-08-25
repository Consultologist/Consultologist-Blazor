using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Workflow;

/// <summary>
/// #384 at startup: the report runs once the catalog is loaded and says, per
/// pin that needs a remedy, what the store said. Never fails the host — a
/// stranded pin is one account's problem, and every other account's consults
/// still run.
/// </summary>
public sealed class PinHealthStartupService : IHostedService
{
    private readonly PinHealthReporter _reporter;
    private readonly ILogger<PinHealthStartupService> _logger;

    public PinHealthStartupService(PinHealthReporter reporter, ILogger<PinHealthStartupService> logger)
    {
        _reporter = reporter;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var report = await _reporter.RunAsync(cancellationToken);
            var unhealthy = report.Pins.Where(pin => pin.Status != PinHealthStatuses.Healthy).ToList();

            foreach (var pin in unhealthy)
            {
                _logger.LogWarning(
                    "Pinned package {Ref} is {Status}: {Reason} ({Accounts} accounts)",
                    pin.Ref, pin.Status, pin.Reason, pin.Accounts);
            }

            var summary = $"{report.Accounts} accounts on {report.Pins.Count} pins against {report.Catalog}, {unhealthy.Count} need attention";
            _logger.LogInformation("Pin health: {Summary}", summary);
            Console.Error.WriteLine($"[PinHealth] {summary}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Local dev without account storage lands here; so does a storage
            // outage at startup. The report is a report, not a gate.
            _logger.LogWarning(ex, "Pin health could not run.");
            Console.Error.WriteLine($"[PinHealth] could not run: {ex.GetType().Name}");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
