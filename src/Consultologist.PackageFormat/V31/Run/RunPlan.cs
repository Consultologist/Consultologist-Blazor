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
    /// its body; a body that holds an ancestor or the entry itself; a key missing from <c>graph.order</c>; a rule the
    /// evaluator declines) is not what publish wrote, and is refused with S07-006 for its package. Never throws on the
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
        IReadOnlyList<string> Reads)
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

            // What a call calls, and what gates a deliverable, are there.
            var allDrafts = drafts.Values.Concat(functions.Values.SelectMany(f => f.Drafts.Values));
            if (allDrafts.Any(d => d.Kind == PlanKind.Call && (d.Function == null || !functions.ContainsKey(d.Function)))
                || drafts.Values.Any(d => d.Check != null && (!drafts.TryGetValue(d.Check, out var check) || !check.Key.StartsWith("check.", StringComparison.Ordinal))))
            {
                return false;
            }

            var entries = drafts.ToDictionary(p => p.Key, p => Plan(p.Value, position[p.Key], drafts), StringComparer.Ordinal);
            var planFunctions = new Dictionary<string, PlanFunction>(StringComparer.Ordinal);
            foreach (var (id, function) in functions)
            {
                if (Walk(function.Body, function.Drafts) is not { } walk)
                    return false;
                var local = function.Drafts.ToDictionary(p => p.Key, p => Plan(p.Value, walk[p.Key], function.Drafts), StringComparer.Ordinal);
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

            var allReads = reads.Concat(extra).Concat(rules.Keys.SelectMany(m => VarPaths(json[m])))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();
            return new Draft(key, id, kind, parent, function, json, rules, body, branches, ret, check, allReads);
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

        /// <summary>The entry of <paramref name="draft"/>, with what it depends on in <paramref name="map"/>.</summary>
        private static PlanEntry Plan(Draft draft, int order, Dictionary<string, Draft> map)
        {
            var depends = draft.Reads
                .Select(path => Named(path, map))
                .OfType<string>()
                .Where(k => k != draft.Key)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();
            return new PlanEntry(
                draft.Key, draft.Id, draft.Kind, draft.Parent, draft.Function, order, draft.Json, draft.Rules,
                draft.Body, draft.Branches, draft.Ret, draft.Reads, depends);
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
