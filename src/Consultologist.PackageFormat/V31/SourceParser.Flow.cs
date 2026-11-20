using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1499 (specVersion 31, area 02): the flow nodes of <c>nodes/</c>, <c>checks/</c> and a function's
/// body — <c>for</c>, <c>while</c>, <c>if</c> and <c>case</c> — each a folder read through its
/// <c>index.json</c> (part 04 §§ 1–6). Only what a node's own shape says is judged here (D16): the
/// member rules, with part 04's own sentence in place of the generic one where it has one, the
/// bounds of <c>maxParallel</c> and <c>max</c>, and the bodies' lists. Whether <c>ret</c> names a
/// child, what <c>in</c> and <c>case</c> read and every type are later areas'.
/// </summary>
public static partial class SourceParser
{
    /// <summary>
    /// The members a flow kind owns that another kind's index may hold by mistake, and whose they are
    /// (S04-010). <c>nodes</c>, <c>ret</c>, <c>until</c> and <c>max</c> are named as a for's, and
    /// <c>else</c> as an if's, wherever the kind does not have them itself.
    /// </summary>
    private static readonly Dictionary<string, string> FlowOwners = new(StringComparer.Ordinal)
    {
        ["in"] = "a for",
        ["parallel"] = "a for",
        ["maxParallel"] = "a for",
        ["itemLabel"] = "a for",
        ["nodes"] = "a for",
        ["ret"] = "a for",
        ["until"] = "a for",
        ["max"] = "a for",
        ["while"] = "a while",
        ["checkFirst"] = "a while",
        ["if"] = "an if",
        ["then"] = "an if",
        ["else"] = "an if",
        ["case"] = "a case",
        ["branches"] = "a case",
    };

    /// <summary>One body of a flow node or a function: the subject its sentences name, its list's name, the list and its <c>ret</c>.</summary>
    private sealed record BodyList(string Subject, string Name, JsonNode? Ids, string? Ret, IReadOnlyList<string>? When = null);

    /// <summary>
    /// A flow node's folder. Its <c>kind</c> is read first, and a fault there ends the read with the
    /// folder's children left unsaid; then the members of the kind's shape and the kind's own rules;
    /// then each body's list (flat in the one folder, so an id is listed once across them all) and its
    /// children, in order. Null only when the folder cannot be read at all (no index, or a refused <c>kind</c>).
    /// </summary>
    private static SourceNode? ReadFlowNode(SourceWalk walk, string root, string enclosing, ListedMember member)
    {
        var folder = member.Path;
        var id = member.Id;
        var index = SourceLayout.ReadIndex(walk, folder);
        if (index == null)
            return null;

        var path = SourceLayout.Join(folder, "index.json");
        var isCheck = root == "check";
        if (ReadKind(walk, index, isCheck ? "Check" : "Node", id, path, inFolder: true) is not { } kind)
        {
            walk.MarkUnreadable(folder);
            return null;
        }

        var word = kind switch { "for" => "Loop", "while" => "While", "if" => "If", _ => "Case" };
        var name = $"{word} '{id}'";
        var shape = kind switch
        {
            "for" => SourceShapes.FlowFor,
            "while" => SourceShapes.FlowWhile,
            "if" => SourceShapes.FlowIf,
            _ => SourceShapes.FlowCase,
        };
        var key = Key(root, enclosing, id);

        // The members whose fault part 04 names in its own words.
        var foreign = index.Select(m => m.Key)
            .Where(m => FlowOwners.ContainsKey(m) && shape.Members.All(s => s.Name != m))
            .ToList();
        List<string> leave = ["label", .. foreign];
        if (kind is "for" or "while")
            leave.Add("ret");
        if (kind == "while")
            leave.Add("max");
        if (kind == "if")
            leave.Add("then");
        SourceMembers.Check(index, shape, name, path, walk.Refusals, leave);

        if (index.TryGetPropertyValue("label", out var label))
        {
            if (label is null)
                walk.Refuse(SourceSentences.FlowLabelNull(word, id, path));
            else
                SourceMembers.CheckType(label, MemberType.Text, name, "label", path, walk.Refusals);
        }

        foreach (var other in foreign)
            walk.Refuse(SourceSentences.MemberOfAnotherKind(word, id, other, FlowOwners[other], path));

        if (SourceLayout.Text(index["producer"]) is { } producer && producer != "computed")
            walk.Refuse(SourceSentences.ProducerDisagrees(isCheck ? "Check" : "Node", key, kind, producer, "computed", path));

        var bodies = new List<BodyList>();
        var complete = true;
        int? maxParallel = null;
        long? max = null;
        int? whileMax = null;
        switch (kind)
        {
            case "for":
                maxParallel = ReadMaxParallel(walk, index, id, name, path);
                max = ReadLoopMax(walk, index, id, name, path);
                if (index["until"] is not null && Boolean(index["parallel"]) == true)
                    walk.Refuse(SourceSentences.UntilWhenParallel(id, path));
                if (index["nodes"] is JsonArray { Count: 0 })
                    walk.Refuse(SourceSentences.LoopHasNoBody(id, path));
                CheckRet(walk, index, name, path);
                bodies.Add(new BodyList(name, "nodes", index["nodes"], SourceLayout.Text(index["ret"])));
                break;
            case "while":
                whileMax = ReadWhileMax(walk, index, id, name, path);
                if (index["nodes"] is JsonArray { Count: 0 })
                    walk.Refuse(SourceSentences.WhileHasNoBody(id, path));
                CheckRet(walk, index, name, path);
                bodies.Add(new BodyList(name, "nodes", index["nodes"], SourceLayout.Text(index["ret"])));
                break;
            case "if":
                if (index.TryGetPropertyValue("then", out var then) && then is null)
                    walk.Refuse(SourceSentences.ThenNull(id, path));
                else
                    SourceMembers.CheckType(then, MemberType.Object, name, "then", path, walk.Refusals);
                complete &= AddBody(walk, bodies, then, $"{name} then", "then.nodes", path, nullable: false);
                complete &= AddBody(walk, bodies, index["else"], $"{name} else", "else.nodes", path, nullable: true);
                break;
            default:
                complete &= AddBranches(walk, bodies, index, id, name, path);
                complete &= AddBody(walk, bodies, index["else"], $"{name} else", "else.nodes", path, nullable: true);
                break;
        }

        // The bodies' lists, one folder: an id listed by an earlier body is listed twice (S04-013).
        var listed = new HashSet<string>(StringComparer.Ordinal);
        var lists = new List<IReadOnlyList<ListedMember>>();
        foreach (var body in bodies)
        {
            if (body.Ids is not JsonArray)
            {
                complete = false;
                lists.Add([]);
                continue;
            }

            var subject = body.Subject;
            lists.Add(SourceLayout.ReadList(
                walk, folder, $"{path} {body.Name}", body.Ids, MemberForm.FileOrFolder,
                (listed, child => SourceSentences.BodyListsTwice(subject, child, path))));
        }

        if (!complete)
            walk.MarkUnreadable(folder);

        var inner = enclosing.Length == 0 ? id : $"{enclosing}.{id}";
        var blocks = new List<SourceBlock>();
        for (var i = 0; i < bodies.Count; i++)
        {
            // S04-012 is judged against the ids the body lists, not the children that survived reading.
            if (bodies[i].Ret is { } ret && WrittenIds(bodies[i].Ids) is { Count: > 0 } written)
            {
                SourceScopes.CheckReturn(
                    bodies[i].Subject, ret, written.Select(child => Key(root, inner, child)).ToList(), path, walk.Refusals);
            }

            blocks.Add(new SourceBlock(ReadBody(walk, lists[i], root, inner), bodies[i].Ret));
        }

        // The node is built even when it has a fault of its own, with placeholders for what could not
        // be read: SourceScopes judges whatever was built, so a scope fault is reported together with
        // independent faults elsewhere (the package itself is withheld whenever anything was refused).
        SourceBlock? Named(string body) => bodies.FindIndex(b => b.Name == body) is var at and >= 0 ? blocks[at] : null;
        var empty = new SourceBlock([], null);
        switch (kind)
        {
            case "for":
                return new SourceFor(
                    id, key, folder, kind, index,
                    SourceLayout.Text(index["in"]) ?? "", Boolean(index["parallel"]) ?? false, maxParallel,
                    index["until"], max, SourceLayout.Text(index["itemLabel"]), blocks[0]);
            case "while":
                return new SourceWhile(
                    id, key, folder, kind, index,
                    index["while"] ?? JsonValue.Create(false), whileMax ?? 0, index["until"], Boolean(index["checkFirst"]) ?? false, blocks[0]);
            case "if":
                return new SourceIf(
                    id, key, folder, kind, index, index["if"] ?? JsonValue.Create(false), Named("then.nodes") ?? empty, Named("else.nodes"));
            default:
            {
                var branches = Enumerable.Range(0, bodies.Count)
                    .Where(i => bodies[i].When != null)
                    .Select(i => new SourceBranch(bodies[i].When!, blocks[i]))
                    .ToList();
                return new SourceCase(
                    id, key, folder, kind, index, SourceLayout.Text(index["case"]) ?? "", branches, Named("else.nodes"));
            }
        }
    }

    /// <summary>
    /// The ids a body's list writes, in order, each once (one trailing <c>/</c> dropped, as the listing does): what S04-012
    /// judges <c>ret</c> against, whether or not the listing kept the entry (a missing file, an id the
    /// rules refuse, a repeat). Null when the list is not an array.
    /// </summary>
    private static List<string>? WrittenIds(JsonNode? ids) => ids is JsonArray array
        ? array.Select(SourceLayout.Text).OfType<string>().Select(id => id.EndsWith('/') ? id[..^1] : id).Distinct(StringComparer.Ordinal).ToList()
        : null;

    /// <summary>A rooted key: the root, the enclosing flow nodes' ids, then the id (part 01 § 9.3).</summary>
    private static string Key(string root, string enclosing, string id) =>
        enclosing.Length == 0 ? $"{root}.{id}" : $"{root}.{enclosing}.{id}";

    /// <summary>
    /// A <c>then</c> or <c>else</c> body: held to <see cref="SourceShapes.Body"/> (its <c>ret</c> null is
    /// S04-011, its <c>nodes</c> <c>[]</c> A02-008 under D22) and added to <paramref name="bodies"/>.
    /// False when the folder's list cannot be read through it: not an object, or null where it may not be.
    /// </summary>
    private static bool AddBody(
        SourceWalk walk, List<BodyList> bodies, JsonNode? node, string subject, string list, string path, bool nullable)
    {
        if (node is null)
            return nullable;
        if (node is not JsonObject body)
            return false;

        SourceMembers.Check(body, SourceShapes.Body, subject, path, walk.Refusals, ["ret"]);
        CheckRet(walk, body, subject, path);
        if (body["nodes"] is JsonArray { Count: 0 })
            walk.Refuse(SourceSentences.NoBody(subject, path));
        bodies.Add(new BodyList(subject, list, body["nodes"], SourceLayout.Text(body["ret"])));
        return true;
    }

    /// <summary>
    /// A <c>case</c>'s <c>branches</c>: a list (A02-001), not empty (S04-042), of objects (A02-001 on
    /// <c>branches.&lt;n&gt;</c>, from 1), each held to <see cref="SourceShapes.Branch"/> and its own
    /// rules (S04-043, S04-076, S04-048), and added to <paramref name="bodies"/> in order. False when
    /// a branch's list cannot be read.
    /// </summary>
    private static bool AddBranches(SourceWalk walk, List<BodyList> bodies, JsonObject index, string id, string name, string path)
    {
        var node = index["branches"];
        if (node is null)
            return false;
        if (node is not JsonArray branches)
        {
            walk.Refuse(SourceSentences.MemberWrongType(name, "branches", SourceMembers.Describe(node), "a list", path));
            return false;
        }

        if (branches.Count == 0)
            walk.Refuse(SourceSentences.NoBranches(id, path));

        var complete = true;
        for (var i = 0; i < branches.Count; i++)
        {
            var n = i + 1;
            if (branches[i] is not JsonObject branch)
            {
                walk.Refuse(SourceSentences.MemberWrongType(name, $"branches.{n}", SourceMembers.Describe(branches[i]), "an object", path));
                complete = false;
                continue;
            }

            var subject = $"{name} branch {n}";
            SourceMembers.Check(branch, SourceShapes.Branch, subject, path, walk.Refusals, ["when"]);
            CheckWhen(walk, branch, id, n, subject, path);
            CheckEmptyBranch(walk, branch, id, n, path);
            bodies.Add(new BodyList(
                subject, $"branch {n} nodes", branch["nodes"], SourceLayout.Text(branch["ret"]), SourceLayout.Lines(branch["when"]) ?? []));
        }

        return complete;
    }

    /// <summary>A branch's <c>when</c>: one or more values (S04-043), never <c>null</c> (S04-076), each text (A02-001).</summary>
    private static void CheckWhen(SourceWalk walk, JsonObject branch, string id, int n, string subject, string path)
    {
        if (!branch.TryGetPropertyValue("when", out var when))
            return;
        if (when is null)
        {
            walk.Refuse(SourceSentences.MemberNull(subject, "when", path));
            return;
        }

        if (when is not JsonArray values)
        {
            SourceMembers.CheckType(when, MemberType.Ids, subject, "when", path, walk.Refusals);
            return;
        }

        if (values.Count == 0)
            walk.Refuse(SourceSentences.BranchListsNoValue(id, n, path));
        if (values.Any(v => v is null))
            walk.Refuse(SourceSentences.BranchListsNull(id, n, path));
        if (values.Any(v => v is not null && SourceLayout.Text(v) is null))
            walk.Refuse(SourceSentences.MemberWrongType(subject, "when", "a list", "a list of text", path));
    }

    /// <summary>
    /// S04-048: a branch whose <c>nodes</c> is <c>[]</c> with a <c>ret</c>, or whose <c>nodes</c> lists
    /// ids with a <c>null</c> <c>ret</c>; both together are the empty branch. Said only when both
    /// members have a type this rule can read (their other faults are the member rules').
    /// </summary>
    private static void CheckEmptyBranch(SourceWalk walk, JsonObject branch, string id, int n, string path)
    {
        if (SourceLayout.Lines(branch["nodes"]) is not { } nodes || !branch.TryGetPropertyValue("ret", out var ret))
            return;
        var written = SourceLayout.Text(ret);
        if (ret is not null && written is null)
            return;

        if (nodes.Count == 0 && written is not null)
            walk.Refuse(SourceSentences.EmptyBranchHalfWritten(id, n, "[]", written, path));
        else if (nodes.Count > 0 && ret is null)
            walk.Refuse(SourceSentences.EmptyBranchHalfWritten(id, n, string.Join(", ", nodes), "null", path));
    }

    /// <summary>A body's <c>ret</c>, left out of the member rules: <c>null</c> is S04-011, any other non-text A02-001.</summary>
    private static void CheckRet(SourceWalk walk, JsonObject body, string subject, string path)
    {
        if (!body.TryGetPropertyValue("ret", out var ret))
            return;
        if (ret is null)
            walk.Refuse(SourceSentences.ReturnsNothing(subject, path));
        else
            SourceMembers.CheckType(ret, MemberType.Text, subject, "ret", path, walk.Refusals);
    }

    /// <summary>
    /// A <c>for</c>'s <c>maxParallel</c> (part 04 § 2.3), the first rule that fails: not a number
    /// (A02-001); not whole, or below 1 (S04-021); any value with <c>parallel</c> false (S04-022);
    /// 1 (S04-019 with <c>parallel</c> true, else S04-021); above <see cref="FormatBounds.Parallel"/>
    /// (S01-005). The value when it passes; null otherwise, and for <c>null</c>.
    /// </summary>
    private static int? ReadMaxParallel(SourceWalk walk, JsonObject index, string id, string name, string path)
    {
        var node = index["maxParallel"];
        if (node is null)
            return null;
        if (Number(node) is not { } number)
        {
            walk.Refuse(SourceSentences.MemberWrongType(name, "maxParallel", SourceMembers.Describe(node), "a whole number", path));
            return null;
        }

        var parallel = Boolean(index["parallel"]);
        if (!number.Whole || number.Value < 1)
            walk.Refuse(SourceSentences.MaxParallelTooLow(id, number.Written, path));
        else if (parallel == false)
            walk.Refuse(SourceSentences.MaxParallelWhenSequential(id, number.Written, path));
        else if (number.Value == 1)
            walk.Refuse(parallel == true ? SourceSentences.ParallelOfOne(id, path) : SourceSentences.MaxParallelTooLow(id, number.Written, path));
        else if (number.Value > FormatBounds.Parallel)
            walk.Refuse(SourceSentences.MaxParallelTooHigh(id, number.Written, path));
        else
            return (int)number.Value;
        return null;
    }

    /// <summary>
    /// A <c>for</c>'s <c>max</c> (part 04 § 2.4): <c>null</c> passes; not a whole number, or one past
    /// what a <see cref="long"/> holds, is A02-001; below 1 is S04-024, whose sentence is fixed. No
    /// upper bound (§ 6): the value is held as written.
    /// </summary>
    private static long? ReadLoopMax(SourceWalk walk, JsonObject index, string id, string name, string path)
    {
        var node = index["max"];
        if (node is null)
            return null;
        if (Number(node) is not { Whole: true } || WholeLong(node) is not { } value)
        {
            walk.Refuse(SourceSentences.MemberWrongType(name, "max", SourceMembers.Describe(node), "a whole number", path));
            return null;
        }

        if (value < 1)
        {
            walk.Refuse(SourceSentences.LoopMaxTooLow(id, path));
            return null;
        }

        return value;
    }

    /// <summary>A JSON number that is whole and fits a <see cref="long"/>, exactly; null otherwise.</summary>
    private static long? WholeLong(JsonNode? node) =>
        node is JsonValue number && number.TryGetValue<decimal>(out var exact)
        && exact == decimal.Truncate(exact) && exact >= long.MinValue && exact <= long.MaxValue
            ? (long)exact
            : null;

    /// <summary>
    /// A <c>while</c>'s <c>max</c> (part 04 § 3.1), left out of the member rules: <c>null</c> is S04-031;
    /// not a whole number A02-001; above <see cref="FormatBounds.Passes"/> S01-003; below 1 S04-033.
    /// </summary>
    private static int? ReadWhileMax(SourceWalk walk, JsonObject index, string id, string name, string path)
    {
        if (!index.TryGetPropertyValue("max", out var node))
            return null;
        if (node is null)
        {
            walk.Refuse(SourceSentences.WhileMaxNull(id, path));
            return null;
        }

        if (Number(node) is not { Whole: true } number)
        {
            walk.Refuse(SourceSentences.MemberWrongType(name, "max", SourceMembers.Describe(node), "a whole number", path));
            return null;
        }

        if (number.Value > FormatBounds.Passes)
            walk.Refuse(SourceSentences.WhileMaxTooHigh(id, number.Written, path));
        else if (number.Value < 1)
            walk.Refuse(SourceSentences.WhileMaxTooLow(id, number.Written, path));
        else
            return (int)number.Value;
        return null;
    }

    /// <summary>
    /// A JSON number: its value, whether it is whole, and its text as the JSON wrote it (invariant).
    /// Null for anything else. A number past <see cref="decimal"/>'s range is read as a double, and
    /// one past a double's as a whole number of that sign.
    /// </summary>
    private static (double Value, bool Whole, string Written)? Number(JsonNode? node)
    {
        if (node is not JsonValue value || value.GetValueKind() != JsonValueKind.Number)
            return null;
        var written = value.ToJsonString();
        if (value.TryGetValue<decimal>(out var exact))
            return ((double)exact, exact == decimal.Truncate(exact), written);
        if (double.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture, out var near))
            return (near, !double.IsFinite(near) || Math.Floor(near) == near, written);
        return null;
    }

    /// <summary><c>true</c> or <c>false</c>, or null for anything else.</summary>
    private static bool? Boolean(JsonNode? node) => node is JsonValue value
        ? value.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        }
        : null;
}
