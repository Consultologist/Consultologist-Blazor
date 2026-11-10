using Consultologist.PackageFormat;

namespace Consultologist.Api.Jobs;

/// <summary>
/// #1341 (package-format-v30-design.md § 2.3): the instances of the loops that run
/// INSIDE a loop — one per outer item (the loop id and the outer path), each settling
/// its own list on its own. A top-level loop is never here (it keeps the 29 maps), so
/// a 26–29 job never touches this. Pure: the orchestrator asks and records.
/// </summary>
internal sealed class ConsultLoopInstances
{
    private readonly Dictionary<string, Dictionary<string, Instance>> _byLoop = new(StringComparer.Ordinal);

    /// <summary>One instance: its items (each carrying its path), the reason an empty list gives, or the sentence it failed with.</summary>
    public sealed record Instance(IReadOnlyList<IReadOnlyDictionary<string, string>> Items, string? Reason, string? Failure)
    {
        public bool Failed => Failure is not null;
    }

    /// <summary>Whether the instance of <paramref name="loopId"/> under <paramref name="outerPath"/> has settled (resolved or failed).</summary>
    public bool Has(string loopId, string outerPath) =>
        _byLoop.TryGetValue(loopId, out var instances) && instances.ContainsKey(outerPath);

    public Instance? Get(string loopId, string outerPath) =>
        _byLoop.TryGetValue(loopId, out var instances) && instances.TryGetValue(outerPath, out var instance) ? instance : null;

    public void Resolve(string loopId, string outerPath, IReadOnlyList<IReadOnlyDictionary<string, string>> items, string? reason) =>
        Settle(loopId, outerPath, new Instance(items, reason, null));

    public void Fail(string loopId, string outerPath, string failure) =>
        Settle(loopId, outerPath, new Instance(Array.Empty<IReadOnlyDictionary<string, string>>(), null, failure));

    /// <summary>The settled items of every instance, in the outer items' order, then each list's.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, string>> ItemsIn(string loopId, IEnumerable<string> outerPaths) =>
        _byLoop.TryGetValue(loopId, out var instances)
            ? outerPaths.SelectMany(path => instances.TryGetValue(path, out var instance) ? instance.Items : Array.Empty<IReadOnlyDictionary<string, string>>()).ToList()
            : Array.Empty<IReadOnlyDictionary<string, string>>();

    private void Settle(string loopId, string outerPath, Instance instance)
    {
        if (!_byLoop.TryGetValue(loopId, out var instances))
        {
            _byLoop[loopId] = instances = new Dictionary<string, Instance>(StringComparer.Ordinal);
        }

        instances[outerPath] = instance;
    }
}
