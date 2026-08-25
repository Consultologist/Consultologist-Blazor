using System.Reflection;
using Consultologist.Api.Agents;
using Consultologist.Api.Auth;

namespace Consultologist.Api.Workflow;

/// <summary>
/// Runs the #384 report: every account's effective pin (the resolver's own
/// answer, fallbacks included), each distinct ref resolved once through the
/// store exactly as a consult would. Reads only; nothing is changed by
/// running it.
/// </summary>
public sealed class PinHealthReporter
{
    private readonly IAccountStore _accounts;
    private readonly IWorkflowPackagePinResolver _pins;
    private readonly IWorkflowPackageStore _store;
    private readonly OutputContractCatalog _catalog;

    public PinHealthReporter(
        IAccountStore accounts,
        IWorkflowPackagePinResolver pins,
        IWorkflowPackageStore store,
        OutputContractCatalog catalog)
    {
        _accounts = accounts;
        _pins = pins;
        _store = store;
        _catalog = catalog;
    }

    public async Task<PinHealthResponse> RunAsync(CancellationToken cancellationToken)
    {
        var pins = new List<(string AppUserId, string Ref)>();
        var outcomes = new Dictionary<string, (string Status, string? Reason)>(StringComparer.Ordinal);
        var refs = new Dictionary<string, PackageFormat.WorkflowPackageRef>(StringComparer.Ordinal);

        foreach (var account in await _accounts.ListAsync(cancellationToken))
        {
            try
            {
                var pin = await _pins.ResolvePinAsync(account.AppUserId, cancellationToken);
                pins.Add((account.AppUserId, pin.ToString()));
                refs.TryAdd(pin.ToString(), pin);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                pins.Add((account.AppUserId, PinHealth.UnresolvedRef));
                outcomes[PinHealth.UnresolvedRef] = PinHealth.Classify(ex);
            }
        }

        foreach (var (value, packageRef) in refs)
        {
            try
            {
                await _store.ResolveAsync(packageRef, cancellationToken);
                outcomes[value] = (PinHealthStatuses.Healthy, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                outcomes[value] = PinHealth.Classify(ex);
            }
        }

        return PinHealth.Assemble(
            _catalog.ResolvedRef,
            EngineAttestation.CommitOf(typeof(PinHealthReporter).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion),
            pins,
            outcomes,
            DateTimeOffset.UtcNow);
    }
}
