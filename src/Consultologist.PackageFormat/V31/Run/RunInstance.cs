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

    /// <summary>
    /// #1504 (area 07; D3): for an agent, a rendered node or a deliverable whose gate held, each placement of its entry the
    /// run decided, to its result (a placement with no rule, true); null before.
    /// </summary>
    public IReadOnlyDictionary<string, bool>? Placed { get; set; }

    /// <summary>#1504 (area 07; part 08 §§ 4, 10): the row detail its last call's outcome gave (part 08's names), or null.</summary>
    public JsonObject? CallDetail { get; set; }

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
/// parent frame. The top level is <see cref="Top"/>; the flow nodes open the others through <see cref="Open"/> (a loop
/// item, a while pass), <see cref="OpenCall"/> (a function's body) and <see cref="OpenBranch"/> (the body an <c>if</c> or a <c>case</c> took). A frame
/// sees its own instances, those of the branches opened in it (a branch is no list: its nodes are read from outside it,
/// part 04 § 4), and its ancestors' that share its map: a function's frame is sealed from its caller's, as its keys are
/// local to it.
/// </summary>
internal sealed class RunFrame
{
    private readonly Dictionary<string, RunInstance> instances = new(StringComparer.Ordinal);
    private readonly List<RunInstance> inOrder = [];

    /// <summary>The branch frames opened in this one, in the order opened.</summary>
    private readonly List<RunFrame> branches = [];

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
        RunInstance? owner,
        bool call = false)
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
        if (call && parent != null && owner != null)
        {
            // A call's frame: its keys go through the call node, its instances rank under the call, one call deeper.
            Depth = parent.Depth + 1;
            KeyPrefix = parent.Rooted(owner.Entry.Key);
            CallOrders = [.. parent.CallOrders, owner.Entry.Order];
        }
        else if (parent != null)
        {
            Depth = parent.Depth;
            KeyPrefix = parent.KeyPrefix;
            CallOrders = parent.CallOrders;
        }
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

    /// <summary>
    /// How many call frames it is, or is inside (the package's level is 0): the depth of a <c>call</c> made from it
    /// (part 04 § 7.8, part 08 § 7: 0 outside every function, <i>d</i>+1 inside a function reached at depth <i>d</i>).
    /// </summary>
    public int Depth { get; }

    /// <summary>The rooted key of the call node its keys go through (part 04 § 7.9), or null at the package's level.</summary>
    public string? KeyPrefix { get; }

    /// <summary>
    /// The <see cref="PlanEntry.Order"/> of each call it runs under, outermost first (empty at the package's level): the
    /// node part of its instances' rank, before their own entry's order (part 09 § 3.2: the node's place, then the item
    /// path, then the passes).
    /// </summary>
    public IReadOnlyList<int> CallOrders { get; } = [];

    /// <summary>Its instances, in the order they were added (body order).</summary>
    public IReadOnlyList<RunInstance> Instances => inOrder;

    /// <summary>The top level of <paramref name="plan"/>, empty.</summary>
    public static RunFrame Top(RunPlan plan) =>
        new(null, plan.Entries, null, [], [], [], null, null, null, null);

    /// <summary>A frame opened by <paramref name="owner"/> inside this one, empty (a loop item, a while pass).</summary>
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

    /// <summary>
    /// The frame of <paramref name="function"/>'s body run by the call <paramref name="owner"/> inside this one, empty: its
    /// map is the function's own (so it is sealed from this one: <see cref="Find"/> and <see cref="Binding"/> stop at it),
    /// it binds <c>param</c> to <paramref name="parameters"/> and no <c>item</c> or <c>pass</c>, its keys go through the
    /// call node with this frame's items and passes, and its instances rank under the call's order.
    /// </summary>
    public RunFrame OpenCall(RunInstance owner, PlanFunction function, IReadOnlyDictionary<string, JsonNode?> parameters) =>
        new(this, function.Entries, function.Id, ItemPath, ItemPositions, Passes, null, null, parameters, owner, call: true);

    /// <summary>
    /// <paramref name="key"/> of this frame's map as a record key writes it, without items: through the call node inside a
    /// function (<c>node.patient_draft</c> in the call <c>node.standards.section</c> is <c>node.standards.section.patient_draft</c>).
    /// </summary>
    public string Rooted(string key)
    {
        if (KeyPrefix == null)
            return key;
        var dot = key.IndexOf('.');
        return dot < 0 ? $"{KeyPrefix}.{key}" : KeyPrefix + key[dot..];
    }

    /// <summary>
    /// The frame of the body <paramref name="owner"/> (an <c>if</c> or a <c>case</c>) took, inside this one, empty: its
    /// keys, items and passes are this frame's, it binds nothing, and its instances are visible from here.
    /// </summary>
    public RunFrame OpenBranch(RunInstance owner)
    {
        var branch = new RunFrame(this, Map, Function, ItemPath, ItemPositions, Passes, null, null, null, owner);
        branches.Add(branch);
        return branch;
    }

    /// <summary>
    /// A new instance of <paramref name="entry"/> in this frame, keyed by the frame's items and passes (through the call
    /// node inside a function), and ranked as part 09 § 3.2 orders: the node's place (the orders of the calls it runs
    /// under, then its entry's order within its function's body), then its whole item path (outermost first, the
    /// caller's then the function's own), then its passes.
    /// </summary>
    public RunInstance Add(PlanEntry entry)
    {
        var rank = new List<int>(CallOrders.Count + 1 + ItemPositions.Count + Passes.Count);
        rank.AddRange(CallOrders);
        rank.Add(entry.Order);
        rank.AddRange(ItemPositions);
        rank.AddRange(Passes.Select(p => p.Pass));
        var instance = new RunInstance(entry, this, RunKeys.Of(Rooted(entry.Key), ItemPath, Passes), rank);
        instances.Add(entry.Key, instance);
        inOrder.Add(instance);
        return instance;
    }

    /// <summary>
    /// The instance of <paramref name="key"/> visible from here: this frame's or its branches', else the nearest ancestor's
    /// (or its branches') with the same map; null for none.
    /// </summary>
    public RunInstance? Find(string key)
    {
        for (var frame = this; frame != null && ReferenceEquals(frame.Map, Map); frame = frame.Parent)
        {
            if (frame.Own(key) is { } found)
                return found;
        }

        return null;
    }

    /// <summary>The instance of <paramref name="key"/> in this frame or a branch opened in it (at any depth; bounded by the body's nesting).</summary>
    private RunInstance? Own(string key)
    {
        if (instances.TryGetValue(key, out var found))
            return found;
        foreach (var branch in branches)
        {
            if (branch.Own(key) is { } inner)
                return inner;
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
