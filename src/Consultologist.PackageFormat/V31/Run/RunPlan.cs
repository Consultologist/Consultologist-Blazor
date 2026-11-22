using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Run;

/// <summary>
/// #1503 (specVersion 31, area 06; part 07 § 2.5): one function of the plan: its parameters in written order, the keys of
/// its body's top level, the key it returns, its own map of entries (keys local to it, a sealed scope) and its format.
/// </summary>
public sealed class PlanFunction
{
    internal PlanFunction(string id, IReadOnlyList<string> inputs, IReadOnlyList<string> body, string? ret, IReadOnlyDictionary<string, PlanEntry> entries, JsonObject json)
    {
        Id = id;
        Inputs = inputs;
        Body = body;
        Ret = ret;
        Entries = entries;
        Json = json;
    }

    /// <summary>The function's id.</summary>
    public string Id { get; }

    /// <summary>Its parameters' ids, in written order (<c>inputOrder</c>).</summary>
    public IReadOnlyList<string> Inputs { get; }

    /// <summary>The keys of its body's top level, in order.</summary>
    public IReadOnlyList<string> Body { get; }

    /// <summary>The key of the node it returns, or null.</summary>
    public string? Ret { get; }

    /// <summary>Its body's entries by local key.</summary>
    public IReadOnlyDictionary<string, PlanEntry> Entries { get; }

    /// <summary>The function as <c>resolve.json</c> holds it. Read-only.</summary>
    public JsonObject Json { get; }

    /// <summary>Its <c>format</c> as written.</summary>
    public JsonNode? Format => Json["format"];
}

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 § 1.2 step 5): the in-memory plan of a published version, derived from its
/// <c>resolve.json</c> alone and never stored: every node, check and deliverable by key with its rules compiled, its
/// functions, its inputs, what it needs, and its formats. It owns its evaluator (04-D47): one per compiled package version,
/// never shared, with the engine's time limit on <c>matches</c> and <c>has_word</c> (R2).
/// </summary>
public sealed class RunPlan
{
    private RunPlan(
        JsonObject document,
        string package,
        string version,
        string resolveHash,
        string lockHash,
        IReadOnlyDictionary<string, PlanEntry> entries,
        IReadOnlyList<string> topLevel,
        IReadOnlyDictionary<string, PlanFunction> functions,
        IReadOnlyDictionary<string, JsonObject> inputs,
        IReadOnlyList<string> inputOrder,
        IReadOnlyList<string> needsProfile,
        IReadOnlyList<string> runFacts,
        IReadOnlyDictionary<string, JsonObject> snippets,
        CorvusJsonLogicEvaluator evaluator)
    {
        Document = document;
        Package = package;
        Version = version;
        ResolveHash = resolveHash;
        LockHash = lockHash;
        Entries = entries;
        TopLevel = topLevel;
        Functions = functions;
        Inputs = inputs;
        InputOrder = inputOrder;
        NeedsProfile = needsProfile;
        RunFacts = runFacts;
        Snippets = snippets;
        Formats = new PlanFormats(document["formats"]);
        Evaluator = evaluator;
    }

    /// <summary>The package's name (<c>package.name</c>).</summary>
    public string Package { get; }

    /// <summary>The package's version tag (<c>package.version</c>).</summary>
    public string Version { get; }

    /// <summary>The stamped hash of <c>resolve.json</c>.</summary>
    public string ResolveHash { get; }

    /// <summary>The stamped hash of <c>lock.json</c>.</summary>
    public string LockHash { get; }

    /// <summary>The compiled document. Read-only: clone before placing a part of it in another tree.</summary>
    public JsonObject Document { get; }

    /// <summary>Every node, check and deliverable of the package, by key.</summary>
    public IReadOnlyDictionary<string, PlanEntry> Entries { get; }

    /// <summary>The keys of the entries with no parent, in <c>graph.order</c>.</summary>
    public IReadOnlyList<string> TopLevel { get; }

    /// <summary>The package's functions, by id.</summary>
    public IReadOnlyDictionary<string, PlanFunction> Functions { get; }

    /// <summary>The package's inputs, by id, as <c>resolve.json</c> holds them.</summary>
    public IReadOnlyDictionary<string, JsonObject> Inputs { get; }

    /// <summary>The input ids in written order.</summary>
    public IReadOnlyList<string> InputOrder { get; }

    /// <summary>The profile fields the account must provide (<c>needs.profile</c>).</summary>
    public IReadOnlyList<string> NeedsProfile { get; }

    /// <summary>The <c>run.*</c> facts read anywhere (<c>run</c>).</summary>
    public IReadOnlyList<string> RunFacts { get; }

    /// <summary>Every snippet and snippet folder, by key, as <c>resolve.json</c> holds them.</summary>
    public IReadOnlyDictionary<string, JsonObject> Snippets { get; }

    /// <summary>The format bundle.</summary>
    public PlanFormats Formats { get; }

    /// <summary>This plan's own evaluator (04-D47), every rule of the plan compiled on it.</summary>
    public CorvusJsonLogicEvaluator Evaluator { get; }

    /// <summary>
    /// The plan of <paramref name="resolve"/>, the text of a <c>resolve.json</c> the caller has already verified against
    /// <paramref name="resolveHash"/> and <paramref name="lockHash"/> (<see cref="CompiledPackage.Verify"/>), with an
    /// evaluator of its own whose <c>matches</c> and <c>has_word</c> stop at <paramref name="matchTimeLimit"/>. A document
    /// it cannot make a plan from (not an object; a key named and absent; a member of the wrong kind; a <c>ret</c> outside
    /// its body; a body that holds an ancestor or the entry itself; a key missing from <c>graph.order</c>; a read under
    /// <c>node.</c>, <c>check.</c> or <c>deliverable.</c> that names no entry of its scope; dependencies that form a circle, or
    /// an entry depending on its own ancestor; a function that calls itself, directly or through others; a call whose
    /// <c>with</c> is not exactly its function's parameters; a deliverable inside a flow node; an input not in
    /// <c>inputOrder</c>; a rule the evaluator declines; #1504, a <c>placements</c> entry not as publish writes it; #1505, a
    /// deliverable read by an entry outside <c>checks</c>, or its <c>.state</c> read in its gate set) is not what publish wrote, and is refused with S07-006 for its package. Never throws on the
    /// document; a null argument is the caller's error, and so is a time limit the evaluator refuses.
    /// </summary>
    public static (RunPlan? Plan, SourceRefusal? Refusal) Build(string resolve, string resolveHash, string lockHash, TimeSpan? matchTimeLimit)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        ArgumentNullException.ThrowIfNull(resolveHash);
        ArgumentNullException.ThrowIfNull(lockHash);

        var evaluator = new CorvusJsonLogicEvaluator(matchTimeLimit);
        var document = Parse(resolve);
        var package = document?["package"] as JsonObject;
        var name = SourceLayout.Text(package?["name"]);
        var version = SourceLayout.Text(package?["version"]);
        if (document == null || name == null || version == null)
            return (null, ResolveSentences.ResolveDoesNotMatch(name ?? "unknown", version ?? "unknown"));

        var reader = new Reader(document, evaluator);
        if (!reader.Read())
            return (null, ResolveSentences.ResolveDoesNotMatch(name, version));

        return (new RunPlan(
            document,
            name,
            version,
            resolveHash,
            lockHash,
            reader.Entries,
            reader.TopLevel,
            reader.Functions,
            reader.Inputs,
            reader.InputOrder,
            reader.NeedsProfile,
            reader.RunFacts,
            reader.Snippets,
            evaluator), null);
    }

    /// <summary>The document <paramref name="text"/> holds when it is a JSON object (every object read, so a repeated member is refused here), else null.</summary>
    private static JsonObject? Parse(string text)
    {
        try
        {
            if (JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { MaxDepth = Reader.MaxDepth }) is not JsonObject document)
                return null;

            var pending = new Stack<JsonNode?>();
            pending.Push(document);
            while (pending.Count > 0)
            {
                switch (pending.Pop())
                {
                    case JsonObject obj:
                        foreach (var (_, child) in obj)
                            pending.Push(child);
                        break;
                    case JsonArray array:
                        foreach (var child in array)
                            pending.Push(child);
                        break;
                }
            }

            return document;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>An entry while the plan is read: everything but what it depends on, which needs its whole map.</summary>
    private sealed record Draft(
        string Key,
        string Id,
        PlanKind Kind,
        string? Parent,
        string? Function,
        JsonObject Json,
        IReadOnlyDictionary<string, IJsonLogicRule> Rules,
        IReadOnlyList<string> Body,
        IReadOnlyList<PlanBranch> Branches,
        string? Ret,
        string? Check,
        IReadOnlyList<string> Reads,
        IReadOnlyList<PlanPlacement> Placements,
        IReadOnlyList<string> NeutralReads)
    {
        /// <summary>Every child's key: the body's, then each branch's.</summary>
        public IEnumerable<string> Children => Body.Concat(Branches.SelectMany(b => b.Nodes));
    }

    /// <summary>One reading of a document into a plan; <see cref="Read"/> is false at the first thing that is not publish's.</summary>
    private sealed class Reader(JsonObject document, IJsonLogicEvaluator evaluator)
    {
        /// <summary>The deepest JSON read: what <see cref="CompiledPackage.Verify"/> reads.</summary>
        public const int MaxDepth = 256;

        private static readonly string[] RuleMembers = ["if", "expression", "while", "until"];

        /// <summary>The roots whose reads name no entry of a map.</summary>
        private static readonly HashSet<string> Unnamed = new(StringComparer.Ordinal) { "item", "pass", "param", "run", "input", "profile", "snippet" };

        private static readonly Dictionary<string, PlanKind> NodeKinds = new(StringComparer.Ordinal)
        {
            ["agent"] = PlanKind.Agent,
            ["rendered"] = PlanKind.Rendered,
            ["computed"] = PlanKind.Computed,
            ["for"] = PlanKind.For,
            ["while"] = PlanKind.While,
            ["if"] = PlanKind.If,
            ["case"] = PlanKind.Case,
            ["call"] = PlanKind.Call,
        };

        public IReadOnlyDictionary<string, PlanEntry> Entries { get; private set; } = new Dictionary<string, PlanEntry>();

        public IReadOnlyList<string> TopLevel { get; private set; } = [];

        public IReadOnlyDictionary<string, PlanFunction> Functions { get; private set; } = new Dictionary<string, PlanFunction>();

        public IReadOnlyDictionary<string, JsonObject> Inputs { get; private set; } = new Dictionary<string, JsonObject>();

        public IReadOnlyList<string> InputOrder { get; private set; } = [];

        public IReadOnlyList<string> NeedsProfile { get; private set; } = [];

        public IReadOnlyList<string> RunFacts { get; private set; } = [];

        public IReadOnlyDictionary<string, JsonObject> Snippets { get; private set; } = new Dictionary<string, JsonObject>();

        public bool Read()
        {
            if (Objects(document["inputs"]) is not { } inputs
                || Strings(document["inputOrder"]) is not { } inputOrder
                || inputOrder.Any(i => !inputs.ContainsKey(i))
                || inputOrder.Distinct(StringComparer.Ordinal).Count() != inputOrder.Count
                || inputOrder.Count != inputs.Count
                || Strings((document["needs"] as JsonObject)?["profile"]) is not { } profile
                || Strings(document["run"]) is not { } run
                || Objects(document["snippets"]) is not { } snippets
                || document["formats"] is not JsonObject { } formats
                || formats["$defs"] is not JsonObject
                || Strings((document["graph"] as JsonObject)?["order"]) is not { } order
                || Objects(document["functions"]) is not { } functionsJson)
            {
                return false;
            }

            var position = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < order.Count; i++)
            {
                if (!position.TryAdd(order[i], i))
                    return false;
            }

            // The package's map.
            var drafts = new Dictionary<string, Draft>(StringComparer.Ordinal);
            foreach (var (member, root, deliverables) in new[] { ("nodes", "node.", false), ("checks", "check.", false), ("deliverables", "deliverable.", true) })
            {
                if (Objects(document[member]) is not { } map)
                    return false;
                foreach (var (key, json) in map)
                {
                    if (!key.StartsWith(root, StringComparison.Ordinal) || Entry(key, json, deliverables) is not { } draft)
                        return false;
                    drafts.Add(key, draft);
                }
            }

            foreach (var key in order)
            {
                var dot = key.IndexOf('.');
                var rootOf = dot < 0 ? key : key[..dot];
                if (rootOf is "node" or "check" or "deliverable" && !drafts.ContainsKey(key))
                    return false; // the order names an entry that is not there
            }

            if (drafts.Keys.Any(k => !position.ContainsKey(k)) || !Structure(drafts))
                return false;

            // Each function's map.
            var functions = new Dictionary<string, (JsonObject Json, Dictionary<string, Draft> Drafts, IReadOnlyList<string> Body, string? Ret, IReadOnlyList<string> Inputs)>(StringComparer.Ordinal);
            foreach (var (id, json) in functionsJson)
            {
                if (SourceLayout.Text(json["id"]) != id
                    || Objects(json["nodes"]) is not { } map
                    || Strings(json["body"]) is not { } body
                    || !NullOrText(json, "ret", out var ret)
                    || Strings(json["inputOrder"]) is not { } parameters
                    || Objects(json["inputs"]) is not { } parameterMap
                    || parameters.Any(p => !parameterMap.ContainsKey(p)))
                {
                    return false;
                }

                var local = new Dictionary<string, Draft>(StringComparer.Ordinal);
                foreach (var (key, entryJson) in map)
                {
                    if (!key.StartsWith("node.", StringComparison.Ordinal) || Entry(key, entryJson, deliverables: false) is not { } draft)
                        return false;
                    local.Add(key, draft);
                }

                if (!Structure(local)
                    || body.Distinct(StringComparer.Ordinal).Count() != body.Count
                    || body.Any(k => !local.TryGetValue(k, out var top) || top.Parent != null)
                    || (ret != null && !body.Contains(ret, StringComparer.Ordinal)))
                {
                    return false;
                }

                functions.Add(id, (json, local, body, ret, parameters));
            }

            // What a call calls, with exactly its parameters, and what gates a deliverable, are there; a deliverable is top level.
            var allDrafts = drafts.Values.Concat(functions.Values.SelectMany(f => f.Drafts.Values));
            if (allDrafts.Any(d => d.Kind == PlanKind.Call
                    && (d.Function == null || !functions.TryGetValue(d.Function, out var called) || !Arguments(d, called.Inputs)))
                || drafts.Values.Any(d => d.Check != null && (!drafts.TryGetValue(d.Check, out var check) || !check.Key.StartsWith("check.", StringComparison.Ordinal)))
                || drafts.Values.Any(d => d.Kind == PlanKind.Deliverable && d.Parent != null))
            {
                return false;
            }

            if (!Acyclic(functions.ToDictionary(
                    p => p.Key,
                    p => (IEnumerable<string>)p.Value.Drafts.Values.Where(d => d.Kind == PlanKind.Call).Select(d => d.Function!),
                    StringComparer.Ordinal)))
            {
                return false; // a function that calls itself, directly or through others
            }

            if (Scope(drafts, k => position[k]) is not { } entries)
                return false;
            var planFunctions = new Dictionary<string, PlanFunction>(StringComparer.Ordinal);
            foreach (var (id, function) in functions)
            {
                if (Walk(function.Body, function.Drafts) is not { } walk || Scope(function.Drafts, k => walk[k]) is not { } local)
                    return false;
                planFunctions.Add(id, new PlanFunction(id, function.Inputs, function.Body, function.Ret, local, function.Json));
            }

            Entries = entries;
            TopLevel = entries.Values.Where(e => e.Parent == null).OrderBy(e => e.Order).Select(e => e.Key).ToList();
            Functions = planFunctions;
            Inputs = inputs;
            InputOrder = inputOrder;
            NeedsProfile = profile;
            RunFacts = run;
            Snippets = snippets;
            return true;
        }

        /// <summary>One entry read, or null when it is not one publish writes.</summary>
        private Draft? Entry(string key, JsonObject json, bool deliverables)
        {
            if (SourceLayout.Text(json["id"]) is not { } id
                || SourceLayout.Text(json["kind"]) is not { } kindText
                || !NullOrText(json, "parent", out var parent)
                || Strings(json["reads"]) is not { } reads)
            {
                return null;
            }

            PlanKind kind;
            if (deliverables)
            {
                if (kindText != "rendered")
                    return null;
                kind = PlanKind.Deliverable;
            }
            else if (!NodeKinds.TryGetValue(kindText, out kind))
            {
                return null;
            }

            // The rules, each compiled with its plainConverts list (D3).
            var rules = new Dictionary<string, IJsonLogicRule>(StringComparer.Ordinal);
            var plainConverts = json["plainConverts"] as JsonObject;
            foreach (var member in RuleMembers)
            {
                if (json[member] is not { } rule)
                    continue;
                if (plainConverts?[member] is not JsonArray list || Booleans(list) is not { } plains)
                    return null;
                var compiled = evaluator.Compile(rule, plains);
                if (compiled.Declined != null)
                    return null;
                rules.Add(member, compiled);
            }

            var required = kind switch
            {
                PlanKind.Computed => "expression",
                PlanKind.If => "if",
                _ => null,
            };
            if (required != null && !rules.ContainsKey(required))
                return null;

            IReadOnlyList<string> body = [];
            IReadOnlyList<PlanBranch> branches = [];
            string? ret = null;
            string? function = null;
            string? check = null;
            var extra = new List<string>();
            switch (kind)
            {
                case PlanKind.For or PlanKind.While:
                    if (Strings(json["nodes"]) is not { } nodes || !NullOrText(json, "ret", out ret)
                        || (ret != null && !nodes.Contains(ret, StringComparer.Ordinal)))
                    {
                        return null;
                    }

                    body = nodes;
                    if (kind == PlanKind.For)
                    {
                        if (SourceLayout.Text(json["in"]) is not { } source)
                            return null;
                        extra.Add(source);
                    }

                    break;

                case PlanKind.If:
                    if (Branch(json["then"], when: false) is not { } then || !OptionalBranch(json["else"], out var otherwise))
                        return null;
                    branches = [then, otherwise];
                    break;

                case PlanKind.Case:
                {
                    if (SourceLayout.Text(json["case"]) is not { } subject || json["branches"] is not JsonArray list || !OptionalBranch(json["else"], out var caseElse))
                        return null;
                    var read = new List<PlanBranch>();
                    foreach (var item in list)
                    {
                        if (Branch(item, when: true) is not { } branch)
                            return null;
                        read.Add(branch);
                    }

                    read.Add(caseElse);
                    branches = read;
                    extra.Add(subject);
                    break;
                }

                case PlanKind.Call:
                    if (SourceLayout.Text(json["function"]) is not { } called || json["with"] is not JsonObject arguments)
                        return null;
                    foreach (var (_, argument) in arguments)
                    {
                        if (argument is null)
                            continue; // the one literal an argument may be (part 04 § 7.2.1)
                        if (SourceLayout.Text(argument) is not { } path)
                            return null;
                        extra.Add(path);
                    }

                    function = called;
                    break;

                case PlanKind.Deliverable:
                    if (!NullOrText(json, "check", out check))
                        return null;
                    break;
            }

            // #1504 (D2, D3): the units a templated body places, each rule compiled with its plainConverts; its reads join the entry's.
            IReadOnlyList<PlanPlacement> placements = [];
            IReadOnlyList<string> neutralReads = [];
            if (kind is PlanKind.Agent or PlanKind.Rendered or PlanKind.Deliverable)
            {
                if (Placements(json["placements"], extra) is not { } compiled)
                    return null;
                placements = compiled;
                neutralReads = NeutralReads(json);
            }

            var allReads = reads.Concat(extra).Concat(rules.Keys.SelectMany(m => VarPaths(json[m])))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();
            return new Draft(key, id, kind, parent, function, json, rules, body, branches, ret, check, allReads, placements, neutralReads);
        }

        /// <summary>
        /// #1504 (D2, D3): an entry's <c>placements</c>, each <c>{ site, within, if, plainConverts, form }</c> as publish
        /// writes it (a site under <c>part.</c>, <c>snippet.</c>, <c>macro.</c> or <c>macro-folder.</c>; <c>within</c>
        /// <c>null</c> or the site of an entry before it; <c>if</c> a rule the evaluator compiles with its
        /// <c>plainConverts</c>, or <c>null</c>; <c>form</c> <c>block</c>, <c>inline</c> or <c>null</c>), with each rule's
        /// <c>var</c>s added to <paramref name="reads"/>; none when the member is absent; null when it is not that.
        /// </summary>
        private List<PlanPlacement>? Placements(JsonNode? member, List<string> reads)
        {
            var placements = new List<PlanPlacement>();
            if (member is null)
                return placements;
            if (member is not JsonArray list)
                return null;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in list)
            {
                if (item is not JsonObject placement
                    || SourceLayout.Text(placement["site"]) is not { } site
                    || !PlacementRoots.Any(r => site.StartsWith(r, StringComparison.Ordinal) && site.Length > r.Length)
                    || !NullOrText(placement, "within", out var within)
                    || (within != null && !seen.Contains(within))
                    || !NullOrText(placement, "form", out var form)
                    || form is not (null or "block" or "inline")
                    || !placement.TryGetPropertyValue("if", out var rule))
                {
                    return null;
                }

                IJsonLogicRule? compiled = null;
                string? printed = null;
                if (rule is not null)
                {
                    if (placement["plainConverts"] is not JsonArray converts || Booleans(converts) is not { } plains)
                        return null;
                    compiled = evaluator.Compile(rule, plains);
                    if (compiled.Declined != null)
                        return null;
                    printed = JsonLogicPrinter.Print(rule);
                    reads.AddRange(VarPaths(rule));
                }

                seen.Add(site);
                placements.Add(new PlanPlacement(site, within, compiled, printed, form));
            }

            return placements;
        }

        /// <summary>The roots a placement's site has (D2).</summary>
        private static readonly string[] PlacementRoots = ["part.", "snippet.", "macro.", "macro-folder."];

        /// <summary>
        /// #1504 (D4): <c>&lt;rooted head&gt;.neutral</c> for each of the entry's <c>runSpots</c> whose <c>neutral</c> is
        /// <c>"text"</c>, the head (the first two segments of its <c>reads</c>) rooted through <c>names</c> (D1); a spot not
        /// read so is left out, as the composer leaves it.
        /// </summary>
        private static IReadOnlyList<string> NeutralReads(JsonObject json)
        {
            var names = json["names"] as JsonObject;
            var paths = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var spot in (json["runSpots"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (SourceLayout.Text(spot["neutral"]) != "text" || SourceLayout.Text(spot["reads"])?.Split('.') is not { Length: >= 3 } segments)
                    continue;
                var head = $"{segments[0]}.{segments[1]}";
                paths.Add((SourceLayout.Text(names?[head]) ?? head) + ".neutral");
            }

            return [.. paths];
        }

        /// <summary>
        /// Whether the parents and children of <paramref name="drafts"/> agree: every child is there, is not its own parent,
        /// names its container as its parent, and is listed once; every entry with a parent is one of its children; no
        /// chain of parents comes back to where it began.
        /// </summary>
        private static bool Structure(Dictionary<string, Draft> drafts)
        {
            foreach (var draft in drafts.Values)
            {
                var children = draft.Children.ToList();
                if (children.Distinct(StringComparer.Ordinal).Count() != children.Count)
                    return false;
                foreach (var child in children)
                {
                    if (child == draft.Key || !drafts.TryGetValue(child, out var found) || found.Parent != draft.Key)
                        return false;
                }

                if (draft.Parent != null
                    && (!drafts.TryGetValue(draft.Parent, out var parent) || !parent.Children.Contains(draft.Key, StringComparer.Ordinal)))
                {
                    return false;
                }

                // A chain of parents is at most as long as the map.
                var steps = 0;
                for (var at = draft.Parent; at != null; at = drafts.TryGetValue(at, out var up) ? up.Parent : null)
                {
                    if (at == draft.Key || ++steps > drafts.Count)
                        return false;
                }
            }

            return true;
        }

        /// <summary>Each key's position in the walk of <paramref name="body"/> (each node, then its children); null when an entry is not reached.</summary>
        private static Dictionary<string, int>? Walk(IReadOnlyList<string> body, Dictionary<string, Draft> drafts)
        {
            var positions = new Dictionary<string, int>(StringComparer.Ordinal);
            var pending = new Stack<string>(body.Reverse());
            while (pending.Count > 0)
            {
                var key = pending.Pop();
                if (!positions.TryAdd(key, positions.Count))
                    return null;
                foreach (var child in drafts[key].Children.Reverse())
                    pending.Push(child);
            }

            return positions.Count == drafts.Count ? positions : null;
        }

        /// <summary>
        /// The entries of one scope (the package's map, or a function's), each with what it depends on; null when a read
        /// under <c>node.</c>, <c>check.</c> or <c>deliverable.</c> names no entry of the scope, when an entry depends on one
        /// of its ancestors, or when the dependencies, with each flow node depending on its children, form a circle.
        /// #1505 (area 08; D1, D3): and, for the package's map, when an entry outside <c>checks</c> reads a deliverable,
        /// or a check in a deliverable's gate set reads its <c>.state</c>; each entry is told its <c>Check</c>,
        /// <c>Gates</c> and <c>ReadsComposed</c>.
        /// </summary>
        private static Dictionary<string, PlanEntry>? Scope(Dictionary<string, Draft> map, Func<string, int> order)
        {
            var edges = new Dictionary<string, IEnumerable<string>>(StringComparer.Ordinal);
            var dependsOf = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            foreach (var draft in map.Values)
            {
                var depends = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var path in draft.Reads)
                {
                    var dot = path.IndexOf('.');
                    var root = dot < 0 ? path : path[..dot];
                    if (root == "pass")
                    {
                        // part 04 § 3.3: this pass's node is a dependency edge; an earlier pass's (previous, history) is none.
                        // A node reading a flow node that encloses it in this form (publish refuses it, A05-003, as it
                        // does node.<id>; #1504 ruling 7) is no edge, which could never be met; the read gives the
                        // ancestor not yet settled (not_run).
                        if (PassNode(path, draft, map) is { } node && node != draft.Key
                            && !RunScope.Inside(map, d => d.Parent, draft.Key, node))
                        {
                            depends.Add(node);
                        }
                        continue;
                    }

                    if (Unnamed.Contains(root))
                        continue;
                    var named = Named(path, map);
                    if (named == null)
                    {
                        if (root is "node" or "check" or "deliverable")
                            return null; // a key named and absent
                        continue;
                    }

                    // #1505 (D1, D2): only a check reads a deliverable (S03-019 at publish elsewhere, S05-020 in a template),
                    // and only through .content, .value or .state.
                    if (map[named].Kind == PlanKind.Deliverable
                        && (!draft.Key.StartsWith("check.", StringComparison.Ordinal) || !DeliverableMember(path[named.Length..])))
                    {
                        return null;
                    }

                    if (named != draft.Key)
                        depends.Add(named);
                }

                // An entry reading one of its ancestors is a circle (A05-003 at publish); the containment edges below find it.
                edges[draft.Key] = depends.Concat(draft.Children).ToList();
                dependsOf[draft.Key] = depends;
            }

            // D3: the wait of a reader outside a deliverable's gate set, for the gate, is among the edges a circle is sought in.
            if (Gating(map, dependsOf, edges) is not { } gating || !Acyclic(edges))
                return null;

            var gatedBy = map.Values.Where(d => d.Check != null).ToLookup(d => d.Check!, StringComparer.Ordinal);
            var entries = new Dictionary<string, PlanEntry>(StringComparer.Ordinal);
            foreach (var draft in map.Values)
            {
                var gates = gatedBy[draft.Key].Select(d => d.Key).OrderBy(order).ToList();
                entries.Add(draft.Key, new PlanEntry(
                    draft.Key, draft.Id, draft.Kind, draft.Parent, draft.Function, order(draft.Key), draft.Json, draft.Rules,
                    draft.Body, draft.Branches, draft.Ret, draft.Reads, dependsOf[draft.Key].ToList(), draft.Placements, draft.NeutralReads,
                    draft.Check, gates, gating.GetValueOrDefault(draft.Key) ?? new HashSet<string>(StringComparer.Ordinal)));
            }

            return entries;
        }

        /// <summary>
        /// D3: for each entry, the deliverables it reads composed: those it reads (depends on) whose gate set holds it. A
        /// deliverable's gate set is its <c>check</c> and every check that check depends on, transitively, over the checks
        /// entries (each flow node with its children). Null when a check in a gate set reads that deliverable's
        /// <c>.state</c> (A08-003 at publish). Each check outside a gate set that reads the deliverable waits for its
        /// outcome, so for its gate: that wait is added to <paramref name="edges"/> (A05-003 at publish when it closes a circle).
        /// </summary>
        private static Dictionary<string, HashSet<string>>? Gating(
            Dictionary<string, Draft> map, Dictionary<string, SortedSet<string>> dependsOf, Dictionary<string, IEnumerable<string>> edges)
        {
            var composed = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var deliverable in map.Values.Where(d => d.Kind == PlanKind.Deliverable && d.Check != null).OrderBy(d => d.Key, StringComparer.Ordinal))
            {
                var set = new HashSet<string>(StringComparer.Ordinal) { deliverable.Check! };
                var pending = new Queue<string>([deliverable.Check!]);
                while (pending.Count > 0)
                {
                    var at = pending.Dequeue();
                    if (!map.TryGetValue(at, out var draft))
                        continue;
                    foreach (var next in dependsOf[at].Concat(draft.Children))
                    {
                        if (next.StartsWith("check.", StringComparison.Ordinal) && set.Add(next))
                            pending.Enqueue(next);
                    }
                }

                foreach (var reader in dependsOf.Where(p => p.Value.Contains(deliverable.Key) && !set.Contains(p.Key)).Select(p => p.Key))
                    edges[reader] = edges[reader].Append(deliverable.Check!).ToList();

                foreach (var key in set.Where(k => map.ContainsKey(k) && dependsOf[k].Contains(deliverable.Key)))
                {
                    if (map[key].Reads.Any(path => Named(path, map) == deliverable.Key && path[deliverable.Key.Length..] is var rest
                            && (rest == ".state" || rest.StartsWith(".state.", StringComparison.Ordinal))))
                        return null;
                    if (!composed.TryGetValue(key, out var reads))
                        composed[key] = reads = new HashSet<string>(StringComparer.Ordinal);
                    reads.Add(deliverable.Key);
                }
            }

            return composed;
        }

        /// <summary>D2: whether <paramref name="rest"/>, a read's path after a deliverable's key, is <c>.content</c>, <c>.value</c> or <c>.state</c> (or a path under one).</summary>
        private static bool DeliverableMember(string rest) =>
            new[] { ".content", ".value", ".state" }.Any(m => rest == m || rest.StartsWith(m + ".", StringComparison.Ordinal));

        /// <summary>Whether the directed graph <paramref name="edges"/> (each key to the keys it needs) has no circle; iterative (Kahn's).</summary>
        private static bool Acyclic(Dictionary<string, IEnumerable<string>> edges)
        {
            var waiting = edges.Keys.ToDictionary(k => k, _ => 0, StringComparer.Ordinal);
            var needed = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var (key, targets) in edges)
            {
                foreach (var target in targets.Distinct(StringComparer.Ordinal))
                {
                    if (!waiting.ContainsKey(target))
                        continue;
                    waiting[key]++;
                    if (!needed.TryGetValue(target, out var by))
                        needed[target] = by = [];
                    by.Add(key);
                }
            }

            var ready = new Queue<string>(waiting.Where(p => p.Value == 0).Select(p => p.Key));
            var done = 0;
            while (ready.Count > 0)
            {
                var key = ready.Dequeue();
                done++;
                if (!needed.TryGetValue(key, out var by))
                    continue;
                foreach (var waiter in by)
                {
                    if (--waiting[waiter] == 0)
                        ready.Enqueue(waiter);
                }
            }

            return done == waiting.Count;
        }

        /// <summary>Whether a call's <c>with</c> names exactly the called function's parameters.</summary>
        private static bool Arguments(Draft call, IReadOnlyList<string> parameters) =>
            call.Json["with"] is JsonObject arguments
            && arguments.Count == parameters.Count
            && parameters.All(arguments.ContainsKey);

        /// <summary>
        /// The key <c>pass.(parent.)*node.&lt;id&gt;</c> names for <paramref name="reader"/>: the node of that id at any depth
        /// in the while whose pass it is (a while's own rules read its own pass; a body node, its enclosing while's; each
        /// <c>parent</c> the next while outward), the first in ordinal order; null for any other read under <c>pass</c>.
        /// </summary>
        private static string? PassNode(string path, Draft reader, Dictionary<string, Draft> map)
        {
            var s = path.Split('.');
            var at = 1;
            while (at < s.Length && s[at] == "parent")
                at++;
            if (at + 1 >= s.Length || s[at] != "node")
                return null;

            var up = at - 1;
            var steps = 0;
            string? loop = null;
            for (var key = reader.Kind == PlanKind.While ? reader.Key : reader.Parent; key != null && steps++ <= map.Count; key = map.GetValueOrDefault(key)?.Parent)
            {
                if (map.GetValueOrDefault(key)?.Kind == PlanKind.While && up-- == 0)
                {
                    loop = key;
                    break;
                }
            }

            return loop == null ? null : RunScope.PassNode(map, d => d.Id, d => d.Parent, loop, s[at + 1]);
        }

        /// <summary>The longest key of <paramref name="map"/> that <paramref name="path"/> is, or begins with before a dot; null for none.</summary>
        private static string? Named(string path, Dictionary<string, Draft> map)
        {
            var dot = path.IndexOf('.');
            if (Unnamed.Contains(dot < 0 ? path : path[..dot]))
                return null;

            for (var candidate = path; ; candidate = candidate[..candidate.LastIndexOf('.')])
            {
                if (map.ContainsKey(candidate))
                    return candidate;
                if (!candidate.Contains('.', StringComparison.Ordinal))
                    return null;
            }
        }

        /// <summary>A branch: <c>{ nodes, ret }</c>, with <c>when</c> for a case's; <c>ret</c> null or one of its nodes.</summary>
        private static PlanBranch? Branch(JsonNode? node, bool when)
        {
            if (node is not JsonObject branch || Strings(branch["nodes"]) is not { } nodes || !NullOrText(branch, "ret", out var ret)
                || (ret != null && !nodes.Contains(ret, StringComparer.Ordinal)))
            {
                return null;
            }

            if (!when)
                return new PlanBranch(nodes, ret, null);
            return branch["when"] is JsonArray values ? new PlanBranch(nodes, ret, values.ToList()) : null;
        }

        /// <summary>An <c>else</c>: <c>null</c> is a branch with no nodes; false when it is neither that nor a branch.</summary>
        private static bool OptionalBranch(JsonNode? node, out PlanBranch branch)
        {
            branch = new PlanBranch([], null, null);
            if (node is null)
                return true;
            if (Branch(node, when: false) is not { } found)
                return false;
            branch = found;
            return true;
        }

        /// <summary>Every <c>var</c> path of <paramref name="rule"/>.</summary>
        private static IEnumerable<string> VarPaths(JsonNode? rule)
        {
            var paths = new List<string>();
            var pending = new Stack<JsonNode?>();
            pending.Push(rule);
            while (pending.Count > 0)
            {
                switch (pending.Pop())
                {
                    case JsonObject { Count: 1 } obj when obj.ContainsKey("var"):
                        if (SourceLayout.Text(obj["var"]) is { } path)
                            paths.Add(path);
                        break;
                    case JsonObject obj:
                        foreach (var (_, child) in obj)
                            pending.Push(child);
                        break;
                    case JsonArray array:
                        foreach (var child in array)
                            pending.Push(child);
                        break;
                }
            }

            return paths;
        }

        /// <summary>Whether <paramref name="obj"/> has <paramref name="member"/> as a string or <c>null</c>, given in <paramref name="text"/>.</summary>
        private static bool NullOrText(JsonObject obj, string member, out string? text)
        {
            text = null;
            if (!obj.TryGetPropertyValue(member, out var value))
                return false;
            if (value is null)
                return true;
            text = SourceLayout.Text(value);
            return text != null;
        }

        /// <summary>An array of strings, or null.</summary>
        private static IReadOnlyList<string>? Strings(JsonNode? node)
        {
            if (node is not JsonArray array)
                return null;
            var items = new List<string>(array.Count);
            foreach (var item in array)
            {
                if (SourceLayout.Text(item) is not { } text)
                    return null;
                items.Add(text);
            }

            return items;
        }

        /// <summary>An array of booleans, or null.</summary>
        private static IReadOnlyList<bool>? Booleans(JsonArray array)
        {
            var items = new List<bool>(array.Count);
            foreach (var item in array)
            {
                if (item is not JsonValue value || value.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False))
                    return null;
                items.Add(value.GetValueKind() == JsonValueKind.True);
            }

            return items;
        }

        /// <summary>An object whose every member is an object, or null.</summary>
        private static Dictionary<string, JsonObject>? Objects(JsonNode? node)
        {
            if (node is not JsonObject obj)
                return null;
            var members = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            foreach (var (key, value) in obj)
            {
                if (value is not JsonObject member)
                    return null;
                members.Add(key, member);
            }

            return members;
        }
    }
}
