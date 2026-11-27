using System.Globalization;
using System.Text.Json;
using Consultologist.PackageFormat.V31.Diagram;
using Consultologist.Web.Shared.History31;

namespace Consultologist.Web.Services.Provenance.V31;

/// <summary>
/// #1510 (specVersion 31, area 13; part 09 § 10.6; D10, D11, D12): a storage 12 record laid over the package's drawing.
/// The drawing is <see cref="DiagramModel.Build"/>'s, written by <see cref="DiagramWriter"/>; this only builds the overlay
/// (label lines after a box's own, a class, a History anchor), so no box, frame or edge is added, removed or renamed. A
/// box's rows are the node or check rows of its key; the rows of the box's own instance are those whose key holds one
/// item or pass per <c>for</c> or <c>while</c> the entry sits in (a loop's item and pass rows hold one more).
/// <para>
/// With no row a box shows nothing and is <c>ranNot</c> (<c>running</c> while the job runs and a row inside it is
/// written); a loop with no row of its own but item or pass rows (a running job, or one that failed in it: the engine
/// writes the loop's row when it ends) lists them when they are of one instance and shows their tally when they are of
/// several (a nested loop in outer items), and is <c>running</c> while the job runs, once it has ended the class of the
/// worst item (D10's order below). A loop with one own row lists that instance's items only. With several rows (a box inside a loop) it shows a tally of their words (<c>3 Completed · 1 Skipped</c>) and the
/// class of the worst (Failed, then Cancelled, Skipped, NotRun, Completed; D10). With exactly one, its status as stored;
/// a node's output hash and retry count; a loop's items (a <c>while</c>'s passes), one line each, the first
/// <see cref="Listed"/>, then <c>… and &lt;k&gt; more</c>; an <c>if</c>'s or <c>case</c>'s decision; a classifier's answer
/// (an agent node returning <c>judgement</c> or <c>classification</c>; D11: the result of a decision whose only read is
/// the node's value); a call's argument and result hashes and, when
/// <c>linkCalls</c>, its row's anchor (D12); a check's <c>pass</c> or <c>fail</c>, its counts and its <c>failWith</c>; the
/// renderer and macro boxes their sub-rows. A deliverable shows its state. A pill and a function's boxes (keys no row
/// carries) show nothing: the shared frame has no per-call status. Labels carry stored words, counts and hashes
/// (shortened as History shortens them, <see cref="ProvenanceHashes.HashPrefix"/>) only; no row text is read. Pure.
/// </para>
/// </summary>
public static class RunDiagram31
{
    /// <summary>The lines a list in one box holds before <c>… and &lt;k&gt; more</c> (D10).</summary>
    public const int Listed = 12;

    /// <summary>The run's classes, <see cref="RunDagDiagram"/>'s colours verbatim, so <c>RunDagOverlay</c>'s legend holds.</summary>
    public static readonly IReadOnlyList<(string Name, string Style)> Classes =
    [
        ("ranNot", "fill:#f7f9fc,stroke:#5d6a7c,color:#5d6a7c,opacity:0.6"),
        ("skipped", "fill:#f7f9fc,stroke:#5d6a7c,color:#5d6a7c,stroke-dasharray:4 3"),
        ("done", "fill:#f7f9fc,stroke:#107c10,color:#107c10,stroke-width:2px"),
        ("failed", "fill:#f7f9fc,stroke:#a4262c,color:#a4262c,stroke-width:2px"),
        ("running", "fill:#eaf3fb,stroke:#0078d4,color:#0078d4,stroke-width:2.5px"),
    ];

    /// <summary>The run diagram of <paramref name="record"/> over the drawing of <paramref name="resolve"/>; never throws on either's content.</summary>
    public static DiagramText Build(string resolve, Record31 record, bool linkCalls)
    {
        ArgumentNullException.ThrowIfNull(record);
        var model = DiagramModel.Build(resolve);
        var (kinds, classifiers) = Entries(resolve);
        var overlay = new Overlay(kinds, classifiers, record, linkCalls).Of(model);
        return DiagramWriter.Write(model, overlay, Classes);
    }

    /// <summary>
    /// The <c>kind</c> of each entry of <c>nodes</c> and <c>checks</c>, by key, and the keys of the classifiers: the
    /// <c>agent</c> nodes returning the built-in <c>judgement</c> or the catalogue's <c>classification</c> (part 02 § 6,
    /// part 05 § 8.2, part 07), their <c>format</c> <c>#/$defs/judgement</c> or <c>#/$defs/classification</c> (no package
    /// format may take either name). Both empty for a text that is not a JSON object.
    /// </summary>
    private static (Dictionary<string, string> Kinds, HashSet<string> Classifiers) Entries(string resolve)
    {
        var kinds = new Dictionary<string, string>(StringComparer.Ordinal);
        var classifiers = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(resolve ?? "");
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return (kinds, classifiers);
            foreach (var map in new[] { "nodes", "checks" })
            {
                if (!document.RootElement.TryGetProperty(map, out var entries) || entries.ValueKind != JsonValueKind.Object)
                    continue;
                foreach (var entry in entries.EnumerateObject())
                {
                    if (entry.Value.ValueKind != JsonValueKind.Object || !entry.Value.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String)
                        continue;
                    kinds.TryAdd(entry.Name, kind.GetString()!);
                    if (map == "nodes" && kind.GetString() == "agent" && entry.Value.TryGetProperty("format", out var format)
                        && format.ValueKind == JsonValueKind.Object && format.TryGetProperty("$ref", out var reference)
                        && reference.ValueKind == JsonValueKind.String && reference.GetString() is "#/$defs/judgement" or "#/$defs/classification")
                        classifiers.Add(entry.Name);
                }
            }
        }
        catch (JsonException)
        {
        }

        return (kinds, classifiers);
    }

    private sealed class Overlay(Dictionary<string, string> kinds, HashSet<string> classifiers, Record31 record, bool linkCalls)
    {
        private readonly bool terminal = JobLiveness31.IsTerminal(record.Header.Status);
        private readonly Dictionary<string, List<Row31>> instances = InstancesOf(record);

        public Dictionary<string, BoxOverlay> Of(DiagramModel model)
        {
            var overlay = new Dictionary<string, BoxOverlay>(StringComparer.Ordinal);
            foreach (var box in model.AllBoxes())
            {
                if (box.Shape == BoxShape.Pill || box.Key.StartsWith("function.", StringComparison.Ordinal))
                    continue;
                if (Box(box.Key) is { } over && (over.Lines.Count > 0 || over.Class != null || over.Anchor != null))
                    overlay[box.Key] = over;
            }

            return overlay;
        }

        private BoxOverlay? Box(string key)
        {
            var sub = key.IndexOf('#', StringComparison.Ordinal);
            if (sub >= 0)
                return SubBox(key[..sub], key[(sub + 1)..]);
            if (key.StartsWith("deliverable.", StringComparison.Ordinal))
                return Deliverable(record.Find(key));

            var kind = kinds.GetValueOrDefault(key);
            var all = instances.GetValueOrDefault(key) ?? [];
            var depth = OwnDepth(key);
            var own = kind is "for" or "while" ? all.Where(r => Tokens(r.Key) == depth).ToList() : all;
            if (own.Count == 0)
            {
                // a loop's own row is written when it ends (none while it runs, nor when the job failed in it): its items still list
                var listed = kind is "for" or "while" ? all.Where(r => Tokens(r.Key) == depth + 1).ToList() : [];
                if (listed.Count > 0)
                {
                    // the items of one instance list; of several (a nested loop in outer items) they are a tally, as a box in a body (D10)
                    var cls = terminal ? Worst(listed) : "running";
                    return listed.Select(r => Instance(r.Key)).Distinct(StringComparer.Ordinal).Count() == 1
                        ? new BoxOverlay([.. Items(listed, passes: kind == "while")], cls, null)
                        : Tally(listed) with { Class = cls };
                }

                var inside = all.Count > 0 || record.Nodes.Concat(record.Checks).Any(r => r.Key.StartsWith(key + ".", StringComparison.Ordinal));
                return new BoxOverlay([], !terminal && inside ? "running" : "ranNot", null);
            }

            if (own.Count > 1)
                return Tally(own);

            var row = own[0];
            // a nested loop's one own row is one instance's: only that instance's items are its
            var items = all.Where(r => Tokens(r.Key) == depth + 1 && Instance(r.Key) == row.Key).ToList();
            return One(row, kind ?? row.Text("kind"), key, items);
        }

        /// <summary>The overlay of a box with one row.</summary>
        private BoxOverlay One(Row31 row, string? kind, string key, List<Row31> items)
        {
            var word = Word(row);
            var lines = new List<string>();
            if (word.Length > 0)
                lines.Add(word);
            string? anchor = null;
            if (row.Section == "check")
            {
                if (row.Int("passed") is { } passed && row.Int("total") is { } total)
                    lines.Add(string.Create(CultureInfo.InvariantCulture, $"{passed} of {total}"));
                if (row.Flag("pass") == false && row.Text("failWith") is { Length: > 0 } failWith)
                    lines.Add(failWith);
                if (kind == "for")
                    lines.AddRange(Items(items, passes: false));
                return new BoxOverlay(lines, ClassOf(word), null);
            }

            if (kind == "call")
            {
                foreach (var argument in row.List("arguments"))
                {
                    if (Member(argument, "param") is { } param)
                        lines.Add($"in {param} {ProvenanceHashes.HashPrefix(Member(argument, "hash"))}");
                }

                if (linkCalls)
                    anchor = RowLinks31.Anchor(row.Key);
            }

            if (row.Text("outputHash") is { Length: > 0 } output)
                lines.Add("out " + ProvenanceHashes.HashPrefix(output));
            if (row.List("retries").Count is > 0 and var retries)
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"retries {retries}"));

            switch (kind)
            {
                case "for" or "while":
                    lines.AddRange(Items(items, passes: kind == "while"));
                    break;
                case "if":
                    lines.AddRange(Decided(row.Key, "self", result => result));
                    break;
                case "case":
                    lines.AddRange(Decided(row.Key, "case", result => result == "else" ? "else" : "arm " + result));
                    break;
                case "call":
                    break;
                default:
                    if (classifiers.Contains(key))
                        lines.AddRange(Bounded(Answers(key, row.Key)));
                    break;
            }

            return new BoxOverlay(lines, ClassOf(word), anchor);
        }

        /// <summary>A loop's items (<c>&lt;n&gt; · &lt;item name&gt;: &lt;status&gt;</c>, the name only when the row keeps it) or a while's passes (<c>pass &lt;n&gt;: &lt;status&gt;</c>), bounded.</summary>
        private static IEnumerable<string> Items(List<Row31> items, bool passes) =>
            Bounded(items.Select(item =>
            {
                var step = RowKey31.Parse(item.Key).Steps.LastOrDefault();
                if (passes)
                    return string.Create(CultureInfo.InvariantCulture, $"pass {step?.Pass ?? item.Int("pass")}: {Word(item)}");
                var n = step?.Items.LastOrDefault() ?? item.Text("itemId") ?? "?";
                return item.Text("itemName") is { Length: > 0 } name ? $"{n} · {name}: {Word(item)}" : $"{n}: {Word(item)}";
            }).ToList());

        /// <summary>The decisions of <paramref name="owner"/> at <paramref name="site"/>: the printed condition as stored and its result.</summary>
        private IEnumerable<string> Decided(string owner, string site, Func<string, string> result) =>
            record.DecisionsOwnedBy(owner).Where(d => d.Text("site") == site && d.Text("condition") != null && Result(d) != null)
                .Select(d => $"{d.Text("condition")}: {result(Result(d)!)}");

        /// <summary>
        /// D11: a classifier's answers, each distinct decision (printed condition and result) with one read: a read of the
        /// classifier's value by its rooted <paramref name="key"/>, or the <c>if</c> of a macro whose row names the
        /// classifier's row (<paramref name="rowKey"/>) as its <c>classifier</c> (part 08 § 4.3), however that read names it
        /// (a loop's body names a sibling by its bare id).
        /// </summary>
        private List<string> Answers(string key, string rowKey)
        {
            var macroIfs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var macro in record.Nodes.SelectMany(r => r.List("macros")).Where(m => Member(m, "classifier") == rowKey))
            {
                // the macro's own if, <placing row key>/if:macro.<id>, among the decisions its offer depended on
                if (Member(macro, "id") is { } id && id.LastIndexOf("/macro:", StringComparison.Ordinal) is >= 0 and var at && Member(macro, "macro") is { } site
                    && macro.TryGetProperty("decisions", out var decisions) && decisions.ValueKind == JsonValueKind.Array
                    && decisions.EnumerateArray().Any(d => d.ValueKind == JsonValueKind.String && d.GetString() == $"{id[..at]}/if:{site}"))
                    macroIfs.Add($"{id[..at]}/if:{site}");
            }

            return record.Decisions.Where(d => d.List("reads") is [var read] && Member(read, "path") is { } path
                    && (path == key + ".value" || path.StartsWith(key + ".value.", StringComparison.Ordinal) || macroIfs.Contains(d.Key))
                    && d.Text("condition") != null && Result(d) != null)
                .Select(d => $"{d.Text("condition")}: {Result(d)}").Distinct(StringComparer.Ordinal).ToList();
        }

        /// <summary>A renderer or macro box (<c>&lt;entry&gt;#render:&lt;n&gt;</c>, <c>&lt;entry&gt;#macro:&lt;id&gt;</c>): its sub-rows, from the entry's one row (D10).</summary>
        private BoxOverlay? SubBox(string entry, string sub)
        {
            List<Row31> rows;
            if (entry.StartsWith("deliverable.", StringComparison.Ordinal))
                rows = record.Find(entry) is { Section: "deliverable" } deliverable ? [deliverable] : [];
            else
            {
                var all = instances.GetValueOrDefault(entry) ?? [];
                var depth = OwnDepth(entry);
                rows = kinds.GetValueOrDefault(entry) is "for" or "while" ? all.Where(r => Tokens(r.Key) == depth).ToList() : all;
            }

            if (rows.Count == 0)
                return new BoxOverlay([], "ranNot", null);
            if (rows.Count > 1)
                return null;

            var row = rows[0];
            if (sub.StartsWith("render:", StringComparison.Ordinal))
            {
                var uses = row.List("renders").Where(r => Member(r, "id") == $"{row.Key}/{sub}").ToList();
                var lines = Bounded(uses.Select(use =>
                {
                    var pass = use.TryGetProperty("pass", out var p) && p.ValueKind == JsonValueKind.Array
                        ? "pass " + string.Join('/', p.EnumerateArray().Select(n => n.ToString())) + " · "
                        : "";
                    return $"{pass}in {ProvenanceHashes.HashPrefix(Member(use, "inputHash"))} · out {ProvenanceHashes.HashPrefix(Member(use, "outputHash"))}";
                }).ToList());
                return new BoxOverlay(lines, uses.Count > 0 ? "done" : "ranNot", null);
            }

            if (sub.StartsWith("macro:", StringComparison.Ordinal))
            {
                var macro = row.List("macros").FirstOrDefault(m => Member(m, "id") == $"{row.Key}/{sub}");
                if (macro.ValueKind != JsonValueKind.Object)
                    return new BoxOverlay([], "ranNot", null);
                var offered = Flag(macro, "offered");
                var placed = Flag(macro, "placed");
                var lines = new List<string> { offered ? placed ? "offered · placed" : "offered · not placed" : "not offered" };
                if (Member(macro, "contentHash") is { Length: > 0 } content)
                    lines.Add("content " + ProvenanceHashes.HashPrefix(content));
                return new BoxOverlay(lines, placed ? "done" : offered ? "failed" : "skipped", null);
            }

            return null;
        }

        private static BoxOverlay Deliverable(Row31? row)
        {
            if (row is not { Section: "deliverable" } || row.Text("state") is not { Length: > 0 } state)
                return new BoxOverlay([], "ranNot", null);
            var cls = state switch
            {
                "released" => "done",
                "held" or "skipped" => "skipped",
                "failed" or "blocked" => "failed",
                _ => "ranNot",
            };
            return new BoxOverlay([state], cls, null);
        }

        /// <summary>D10: the rows' words counted (<c>3 Completed · 1 Skipped</c>), best first, and the class of the worst.</summary>
        private static BoxOverlay Tally(List<Row31> rows)
        {
            var counts = rows.Select(Word).Where(w => w.Length > 0).GroupBy(w => w, StringComparer.Ordinal)
                .OrderBy(g => Rank(g.Key) == 0 ? int.MaxValue : Rank(g.Key)).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
            var line = string.Join(" · ", counts.Select(g => string.Create(CultureInfo.InvariantCulture, $"{g.Count()} {g.Key}")));
            return new BoxOverlay(line.Length > 0 ? [line] : [], Worst(rows), null);
        }

        /// <summary>D10: the class of the worst of the rows' words; <c>ranNot</c> when none has a word.</summary>
        private static string Worst(List<Row31> rows) =>
            rows.Select(Word).Where(w => w.Length > 0).OrderByDescending(Rank).FirstOrDefault() is { } worst ? ClassOf(worst) : "ranNot";

        /// <summary>A row's word: a check's verdict (<c>pass</c>, <c>fail</c>) when it has one, else the status as stored.</summary>
        private static string Word(Row31 row) =>
            row.Section == "check" && row.Flag("pass") is { } pass ? pass ? "pass" : "fail" : row.Text("status") ?? "";

        /// <summary>D10's order, worst highest; 0 for a word it does not name.</summary>
        private static int Rank(string word) => word switch
        {
            "Failed" or "fail" => 5,
            "Cancelled" => 4,
            "Skipped" => 3,
            "NotRun" => 2,
            "Completed" or "pass" => 1,
            _ => 0,
        };

        private static string ClassOf(string word) => word switch
        {
            "Completed" or "pass" => "done",
            "Skipped" => "skipped",
            "Failed" or "fail" or "Cancelled" => "failed",
            _ => "ranNot",
        };

        private static List<string> Bounded(List<string> lines) =>
            lines.Count <= Listed
                ? lines
                : [.. lines.Take(Listed), string.Create(CultureInfo.InvariantCulture, $"… and {lines.Count - Listed} more")];

        /// <summary>The number of <c>for</c> and <c>while</c> entries <paramref name="key"/> sits in: the items and passes its own rows' keys hold.</summary>
        private int OwnDepth(string key)
        {
            var depth = 0;
            for (var dot = key.IndexOf('.', StringComparison.Ordinal); dot >= 0; dot = key.IndexOf('.', dot + 1))
            {
                if (dot > key.IndexOf('.', StringComparison.Ordinal) && kinds.GetValueOrDefault(key[..dot]) is "for" or "while")
                    depth++;
            }

            return depth;
        }

        /// <summary>An item's or pass's row key up to its last item or pass: the key of the loop instance it is one of.</summary>
        private static string Instance(string rowKey) => rowKey[..rowKey.LastIndexOfAny([':', '/', '#'])];

        /// <summary>The items and passes a row key holds: each starts with <c>:</c>, <c>/</c> or <c>#</c> after the entry.</summary>
        private static int Tokens(string rowKey) => rowKey[RowKey31.InstanceStart(rowKey)..].Count(c => c is ':' or '/' or '#');

        /// <summary>The node and check rows by their entry's key (the row key up to its items and passes), in key order.</summary>
        private static Dictionary<string, List<Row31>> InstancesOf(Record31 record)
        {
            var byEntry = new Dictionary<string, List<Row31>>(StringComparer.Ordinal);
            foreach (var row in record.Nodes.Concat(record.Checks))
            {
                var entry = row.Key[..RowKey31.InstanceStart(row.Key)];
                if (!byEntry.TryGetValue(entry, out var rows))
                    byEntry[entry] = rows = [];
                rows.Add(row);
            }

            return byEntry;
        }

        private static string? Result(Row31 decision) =>
            decision.Flag("result") is { } flag ? flag ? "true" : "false" : decision.Text("result");

        private static string? Member(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        private static bool Flag(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    }
}
