using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Run;

/// <summary>#1503 (specVersion 31, area 06): where an instance is in its run.</summary>
internal enum InstancePhase
{
    /// <summary>Not yet settled, and not yet ready or not yet looked at.</summary>
    Pending,

    /// <summary>Its <c>if</c> held: an agent or render call waiting for its turn (a slot, for an agent).</summary>
    Waiting,

    /// <summary>Its call is in flight.</summary>
    Running,

    /// <summary>It has its row state and its read form.</summary>
    Settled,
}

/// <summary>
/// #1503 (specVersion 31, area 06): one running of one entry in one frame: its row key (<see cref="RunKeys.Of"/>), its place
/// in the fixed order (<see cref="Rank"/>), where it is, and once settled, its row state and the read form
/// (<c>{ value, content, state }</c>) its readers see.
/// </summary>
internal sealed class RunInstance
{
    public RunInstance(PlanEntry entry, RunFrame frame, string key, IReadOnlyList<int> rank)
    {
        Entry = entry;
        Frame = frame;
        Key = key;
        Rank = rank;
    }

    /// <summary>The entry this is an instance of.</summary>
    public PlanEntry Entry { get; }

    /// <summary>The frame it runs in.</summary>
    public RunFrame Frame { get; }

    /// <summary>Its row key.</summary>
    public string Key { get; }

    /// <summary>
    /// Its place in the fixed order of part 09 § 3.2, compared element by element (<see cref="RankComparer"/>): its entry's
    /// position in <c>graph.order</c>, then its item positions, outermost first, then its passes.
    /// </summary>
    public IReadOnlyList<int> Rank { get; }

    /// <summary>Where it is.</summary>
    public InstancePhase Phase { get; set; }

    /// <summary>Its row state, once settled.</summary>
    public RowState? State { get; private set; }

    /// <summary>What a reader sees, once settled: <c>{ value, content, state }</c>. Read-only once set.</summary>
    public JsonObject? Read { get; private set; }

    /// <summary>The attempt its call is on (from 1); 0 before its first call.</summary>
    public int Attempt { get; set; }

    /// <summary>Its row's reason once settled (the printed condition of a skip, a not-run reason, a failure), or null.</summary>
    public string? Reason { get; private set; }

    /// <summary>Its place among the run's instances in the order they were made: the fixed order's tie-break.</summary>
    public int Sequence { get; set; }

    /// <summary>Whether the run has found it ready once: readiness never goes back, so the run asks no more.</summary>
    public bool WasReady { get; set; }

    /// <summary>Whether it has settled.</summary>
    public bool IsSettled => Phase == InstancePhase.Settled;

    /// <summary><see cref="Entry"/>'s dependencies other than its own descendants, worked out once.</summary>
    private IReadOnlyList<string>? outside;

    /// <summary>
    /// Whether it is ready: for every key its entry depends on, other than its own descendants (a flow node's rule reading
    /// its body) and <paramref name="except"/> (a loop over that loop's <c>.items</c>, which follows it item by item), the
    /// instance that stands for that key as seen from its frame has settled. That instance is the key's own
    /// when one is visible; otherwise the nearest ancestor of the key (along its <c>Parent</c> chain) that has a visible
    /// instance, since the key's own lives in a frame not yet opened, and a flow node settles only after its frames do. A
    /// key with neither (outside every frame the reader sees) does not hold it back, and reads <c>not_run</c>.
    /// </summary>
    public bool IsReady(string? except = null)
    {
        var map = Frame.Map;
        outside ??= Entry.Depends.Where(key => !IsDescendant(map, key, Entry.Key)).ToList();
        foreach (var key in outside)
        {
            if (key == except)
                continue;

            RunInstance? standing = null;
            var steps = 0;
            for (var at = key; at != null && standing == null && steps++ <= map.Count; at = map.GetValueOrDefault(at)?.Parent)
                standing = Frame.Find(at);
            if (standing is { IsSettled: false })
                return false;
        }

        return true;
    }

    /// <summary>Whether <paramref name="key"/>'s chain of parents in <paramref name="map"/> reaches <paramref name="ancestor"/> (bounded by the map's size).</summary>
    private static bool IsDescendant(IReadOnlyDictionary<string, PlanEntry> map, string key, string ancestor)
    {
        var steps = 0;
        for (var at = map.GetValueOrDefault(key)?.Parent; at != null && steps++ <= map.Count; at = map.GetValueOrDefault(at)?.Parent)
        {
            if (at == ancestor)
                return true;
        }

        return false;
    }

    /// <summary>Settles it as <paramref name="state"/>, read as <paramref name="read"/>, with its row's <paramref name="reason"/>.</summary>
    public void Settle(RowState state, JsonObject read, string? reason = null)
    {
        Phase = InstancePhase.Settled;
        State = state;
        Read = read;
        Reason = reason;
    }
}

/// <summary>#1503 (specVersion 31, area 06): the fixed order of instances: <see cref="RunInstance.Rank"/>, then the order they were made.</summary>
internal sealed class FixedOrder : IComparer<RunInstance>
{
    public static readonly FixedOrder Instance = new();

    public int Compare(RunInstance? x, RunInstance? y)
    {
        if (ReferenceEquals(x, y))
            return 0;
        if (x is null)
            return -1;
        if (y is null)
            return 1;
        var order = RankComparer.Instance.Compare(x.Rank, y.Rank);
        return order != 0 ? order : x.Sequence.CompareTo(y.Sequence);
    }
}

/// <summary>#1503 (specVersion 31, area 06): orders <see cref="RunInstance.Rank"/>s element by element, a shorter rank first on a tie.</summary>
internal sealed class RankComparer : IComparer<IReadOnlyList<int>>
{
    public static readonly RankComparer Instance = new();

    public int Compare(IReadOnlyList<int>? x, IReadOnlyList<int>? y)
    {
        if (ReferenceEquals(x, y))
            return 0;
        if (x is null)
            return -1;
        if (y is null)
            return 1;
        for (var i = 0; i < Math.Min(x.Count, y.Count); i++)
        {
            var order = x[i].CompareTo(y[i]);
            if (order != 0)
                return order;
        }

        return x.Count.CompareTo(y.Count);
    }
}

/// <summary>
/// #1503 (specVersion 31, area 06): one running of a body: the top level, a loop item, a while pass, a branch, a call. It
/// holds an instance per body entry (in body order), its <c>item</c> and <c>pass</c> bindings, its parameters, and its
/// parent frame. The top level is <see cref="Top"/>; the flow nodes open the others (a loop item, Tasks 7 and 8 the rest) through
/// <see cref="Open"/>. A frame sees its own instances and its ancestors' that share its map: a function's frame is sealed
/// from its caller's, as its keys are local to it.
/// </summary>
internal sealed class RunFrame
{
    private readonly Dictionary<string, RunInstance> instances = new(StringComparer.Ordinal);
    private readonly List<RunInstance> inOrder = [];

    private RunFrame(
        RunFrame? parent,
        IReadOnlyDictionary<string, PlanEntry> map,
        string? function,
        IReadOnlyList<string> itemPath,
        IReadOnlyList<int> itemPositions,
        IReadOnlyList<(int Position, int Pass)> passes,
        JsonNode? item,
        JsonNode? pass,
        IReadOnlyDictionary<string, JsonNode?>? parameters,
        RunInstance? owner)
    {
        Parent = parent;
        Map = map;
        Function = function;
        ItemPath = itemPath;
        ItemPositions = itemPositions;
        Passes = passes;
        Item = item;
        Pass = pass;
        Parameters = parameters;
        Owner = owner;
        Depth = parent == null ? 0 : parent.Depth + 1;
    }

    /// <summary>The frame this one runs inside, or null for the top level.</summary>
    public RunFrame? Parent { get; }

    /// <summary>The entries its keys name: the package's, or its function's.</summary>
    public IReadOnlyDictionary<string, PlanEntry> Map { get; }

    /// <summary>The function whose body this is (or is inside), or null at the package's level.</summary>
    public string? Function { get; }

    /// <summary>The item ids of the loops it is inside, outermost first (<see cref="RunKeys.Of"/>).</summary>
    public IReadOnlyList<string> ItemPath { get; }

    /// <summary>The items' positions in their lists, outermost first: the item path's part of the fixed order.</summary>
    public IReadOnlyList<int> ItemPositions { get; }

    /// <summary>The passes of the whiles it is inside, each at its position in the item path (<see cref="RunKeys.Of"/>).</summary>
    public IReadOnlyList<(int Position, int Pass)> Passes { get; }

    /// <summary>What <c>item</c> reads in this frame, when the frame binds it (a loop item); else null.</summary>
    public JsonNode? Item { get; }

    /// <summary>What <c>pass</c> reads in this frame, when the frame binds it (a while pass); else null.</summary>
    public JsonNode? Pass { get; }

    /// <summary>What <c>param.&lt;name&gt;</c> reads, when the frame is a call's; else null.</summary>
    public IReadOnlyDictionary<string, JsonNode?>? Parameters { get; }

    /// <summary>The flow instance that opened the frame, or null for the top level.</summary>
    public RunInstance? Owner { get; }

    /// <summary>How many frames it is inside (the top level is 0).</summary>
    public int Depth { get; }

    /// <summary>Its instances, in the order they were added (body order).</summary>
    public IReadOnlyList<RunInstance> Instances => inOrder;

    /// <summary>The top level of <paramref name="plan"/>, empty.</summary>
    public static RunFrame Top(RunPlan plan) =>
        new(null, plan.Entries, null, [], [], [], null, null, null, null);

    /// <summary>A frame opened by <paramref name="owner"/> inside this one, empty (a loop item; the flow nodes of Tasks 7 and 8).</summary>
    public RunFrame Open(
        RunInstance owner,
        IReadOnlyDictionary<string, PlanEntry> map,
        string? function,
        IReadOnlyList<string> itemPath,
        IReadOnlyList<int> itemPositions,
        IReadOnlyList<(int Position, int Pass)> passes,
        JsonNode? item,
        JsonNode? pass,
        IReadOnlyDictionary<string, JsonNode?>? parameters) =>
        new(this, map, function, itemPath, itemPositions, passes, item, pass, parameters, owner);

    /// <summary>A new instance of <paramref name="entry"/> in this frame, keyed and ranked by the frame's items and passes.</summary>
    public RunInstance Add(PlanEntry entry)
    {
        var rank = new List<int>(1 + ItemPositions.Count + Passes.Count) { entry.Order };
        rank.AddRange(ItemPositions);
        rank.AddRange(Passes.Select(p => p.Pass));
        var instance = new RunInstance(entry, this, RunKeys.Of(entry.Key, ItemPath, Passes), rank);
        instances.Add(entry.Key, instance);
        inOrder.Add(instance);
        return instance;
    }

    /// <summary>The instance of <paramref name="key"/> visible from here: this frame's, else the nearest ancestor's with the same map; null for none.</summary>
    public RunInstance? Find(string key)
    {
        for (var frame = this; frame != null && ReferenceEquals(frame.Map, Map); frame = frame.Parent)
        {
            if (frame.instances.TryGetValue(key, out var found))
                return found;
        }

        return null;
    }

    /// <summary>The nearest frame from here, with the same map, whose <paramref name="bound"/> is not null; null for none.</summary>
    public RunFrame? Binding(Func<RunFrame, bool> bound)
    {
        for (var frame = this; frame != null && ReferenceEquals(frame.Map, Map); frame = frame.Parent)
        {
            if (bound(frame))
                return frame;
        }

        return null;
    }
}
