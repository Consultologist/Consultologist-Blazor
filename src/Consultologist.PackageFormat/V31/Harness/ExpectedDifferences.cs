using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Run;
using Consultologist.PackageFormat.V31.Templates;

namespace Consultologist.PackageFormat.V31.Harness;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 2.4, § 3.2): the differences from 30 that a ruling or a spec section
/// declares, so a comparison may mark them expected. Anything not named here is unexpected (the plan's "when in doubt,
/// a difference is unexpected"). Task 4 holds the reasons a converted condition may answer otherwise than 30's
/// evaluator (<see cref="ConditionReasons"/>); Task 15 the rules of the byte-equivalence comparison (<see cref="Rules"/>),
/// each with its id, the ruling or section it rests on, the explanation the report gives, and what it explains.
/// <para>
/// A rule explains a difference exactly or not at all. Two kinds: a <b>case</b> rule explains a whole compared text by
/// where it is and how the two runs ended (a status 30 refused and 31 completed for the two captures R2 names; a text only
/// 31 has there); a <b>bytes</b> rule undoes one declared difference between the two texts (unescaping 31's escaped plain
/// text, say). <see cref="Explain"/> takes the first case rule that holds; else it applies the bytes rules in
/// <see cref="Rules"/>' order, each to the texts the earlier left, again until none changes anything (each at most once),
/// and the difference is expected only when the texts are then the same, explained by the rules that changed something,
/// named in <see cref="Rules"/>' order.
/// </para>
/// </summary>
public static class ExpectedDifferences
{
    /// <summary>
    /// The reasons a converted condition, <c>case</c> or <c>let</c> may answer otherwise than 30 did on the same case,
    /// each id beside the ruling or spec section it rests on.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ConditionReasons { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["blank"] = "R1 (#1507, 2026-11-20); part 10 § 2.3: '!x' on text is blank(x), which also holds for empty and white-space-only text and for an input not given.",
    };

    /// <summary>
    /// One rule of the comparison: its <paramref name="Id"/>, the ruling or spec section it rests on
    /// (<paramref name="Basis"/>), and what the report says of a difference it explains (<paramref name="Explanation"/>).
    /// </summary>
    public sealed record Rule(string Id, string Basis, string Explanation)
    {
        internal Func<DifferenceContext, bool>? Case { get; init; }

        internal Func<DifferenceContext, string, string, (string Old, string New)?>? Bytes { get; init; }

        /// <summary>The report's explanation: <c>&lt;id&gt;: &lt;explanation&gt; (&lt;basis&gt;)</c>.</summary>
        public string Said => $"{Id}: {Explanation} ({Basis})";
    }

    /// <summary>The two captures R2 names, refused at 30 because a fanned input had no entries, which complete at 31.</summary>
    public static IReadOnlyList<(string Package, string Scenario)> EmptyInputListCaptures { get; } =
    [
        ("example-prior-letters", "no-prior-letters"),
        ("example-structured-intake", "all-optional-inputs-unset"),
    ];

    /// <summary>The rules, case rules first, then the bytes rules in the order <see cref="Explain"/> applies them.</summary>
    public static IReadOnlyList<Rule> Rules { get; } =
    [
        new("empty-input-list-runs",
            "R2, ruled by the owner on #1507, 2026-11-20; part 10 § 3.2; part 04 § 2.4",
            "a fanned input with no entries settles at 0 items and the job runs, where 30 refused the start; the texts it then gives are 31's alone")
        {
            Case = c => EmptyInputListCaptures.Contains((c.Package, c.Scenario))
                && (IsStatus(c, ByteEquivalence.Refused, nameof(RunState.Completed)) || (c.Kind != ComparedKind.Status && c.OldText is null)),
        },
        new("skipped-deliverable-reads-null",
            "R4 item 1, ruled by the owner on #1507, 2026-11-21; part 10 § 3.2; part 04",
            "reading .content of a deliverable skipped by its if reads null and no longer fails the job; the texts after the read are 31's alone")
        {
            Case = c => ReadSkippedDeliverable(c) && (IsStatus(c, nameof(RunState.Failed), nameof(RunState.Completed)) || (c.Kind != ComparedKind.Status && c.OldText is null)),
        },
        new("required-blank-refused",
            "R4 item 6, ruled by the owner on #1507, 2026-11-21; part 10 § 3.2",
            "a required text input supplied blank is refused at the start, as a missing one is; nothing after the start runs")
        {
            Case = c => RequiredBlankRefused(c) && ((c.Kind == ComparedKind.Status && c.NewText == ByteEquivalence.Refused) || (c.Kind != ComparedKind.Status && c.NewText is null)),
        },
        new("all-refused-completes",
            "R4 item 7, ruled by the owner on #1507, 2026-11-21; part 10 § 3.2",
            "when checks refuse every deliverable the job is Completed, each deliverable refused with its sentence, where 30 failed it")
        {
            Case = c => IsStatus(c, nameof(RunState.Failed), nameof(RunState.Completed)) && AllRefused(c),
        },
        new("refused-deliverable-withheld",
            "R4 item 3, ruled by the owner on #1507, 2026-11-21; part 10 § 3.2",
            "a deliverable its check refused is withheld, not erased: the rest of the job reads it as null, where 30 read its composed text")
        {
            Bytes = Withheld,
        },
        new("escaped-plain-text",
            "part 06 § 6.4; part 10 § 3.2; D8",
            "plain text read into a Markdown deliverable is escaped; unescaping it gives 30's bytes")
        {
            Bytes = (c, old, now) => c.Kind == ComparedKind.Deliverable ? (old, Unescape(now)) : null,
        },
        new("item-ids-from-one",
            "ruled by the owner on #1506, 2026-11-21, 2 (item numbers are 1-based everywhere users see them; 30 counted an array input's from 0); D12",
            "an item of a loop over an input's list is numbered from 1, where 30 numbered it from 0")
        {
            Bytes = ItemIds,
        },
        new("sent-format",
            "part 10 § 1.8 (every property required, an optional one nullable); part 10 § 1.2 (formats); part 02 § 3.5 (what a model receives)",
            "the format appended to the prompt is the migrated format as 31 sends it: every property required, an optional one nullable, descriptions kept, in canonical order")
        {
            Bytes = SentFormat,
        },
        new("explicit-null-for-optional",
            "part 10 § 1.8; part 10 § 3.2",
            "an optional field 30 left out is an explicit null at 31: the texts differ only by the null members a reply now carries")
        {
            Bytes = (c, old, now) => c.Kind is ComparedKind.Prompt or ComparedKind.Render && WithoutNullMembers(now) is { } fewer && fewer != now ? (old, fewer) : null,
        },
        new("macro-trailing-whitespace",
            "R5, ruled by the owner on #1507, 2026-11-22, 1; part 05 § 8.6; part 10 § 3.2",
            "a macro body keeps 31's rule (one final line ending dropped, a body read as a snippet kept whole), where 30 trimmed all its trailing white space")
        {
            Bytes = MacroTrailingWhitespace,
        },
        new("blank-conversion",
            "R1, ruled by the owner on #1507, 2026-11-20; part 10 §§ 2.3, 3.2",
            "the text hangs on a '!x' converted to blank(x), which held at 31 for an empty or white-space-only text where 30's !x did not")
        {
            Case = BlankConversion,
        },
    ];

    /// <summary>
    /// Why the difference <paramref name="context"/> describes is expected (each rule that explains it, as
    /// <see cref="Rule.Said"/>, joined by <c>"; "</c>), or <c>null</c> when no rule explains it exactly.
    /// </summary>
    public static string? Explain(DifferenceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var rule in Rules)
        {
            if (rule.Case?.Invoke(context) == true)
                return rule.Said;
        }

        if (context.OldText is not { } old || context.NewText is not { } now || context.Kind == ComparedKind.Status)
            return null;

        // Each rule undoes its own difference where the texts are otherwise alike, so one may only apply once another has
        // (an item number inside a prompt whose format also differs): the rules are applied in turn until none changes
        // anything, each at most once.
        var applied = new List<Rule>();
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var rule in Rules)
            {
                if (applied.Contains(rule) || rule.Bytes?.Invoke(context, old, now) is not { } undone || (undone.Old == old && undone.New == now))
                    continue;
                (old, now) = undone;
                applied.Add(rule);
                changed = true;
            }
        }

        applied = [.. Rules.Where(applied.Contains)];

        return applied.Count > 0 && string.Equals(old, now, StringComparison.Ordinal) ? string.Join("; ", applied.Select(r => r.Said)) : null;
    }

    private static bool IsStatus(DifferenceContext c, string old, string now) =>
        c.Kind == ComparedKind.Status && c.OldText == old && c.NewText == now;

    /// <summary>
    /// R4 item 1: 30's job failed reading <c>.content</c> of a deliverable skipped by its <c>if</c>
    /// (<c>Cannot get the member deliverable.&lt;id&gt;.content for a null object</c>), and at 31 that deliverable was
    /// skipped.
    /// </summary>
    private static bool ReadSkippedDeliverable(DifferenceContext c)
    {
        const string Head = "Cannot get the member deliverable.";
        const string Tail = ".content for a null object";
        if (SourceLayout.Text(c.Capture30["status"]) != nameof(RunState.Failed) || SourceLayout.Text(c.Capture30["failure"]) is not { } failure)
            return false;
        var at = failure.IndexOf(Head, StringComparison.Ordinal);
        var end = at < 0 ? -1 : failure.IndexOf(Tail, at, StringComparison.Ordinal);
        if (end < 0)
            return false;
        var id = failure[(at + Head.Length)..end];
        return KeyMap.To31("deliverable." + id, c.Migrated, c.Plan) is { } key
            && c.Run31.Deliverables.Any(d => d.Key == key && d.State == DeliverableState.Skipped);
    }

    /// <summary>
    /// R4 item 6: 31's start refused the run, every refusal A06-011 for a required input (31 judges a value left blank:
    /// absent, <c>null</c>, or text <c>blank()</c> holds for), and 30 started it (its status is not <c>Refused</c>). 30's
    /// start refuses a required input not given, and a capture never freezes that (the fixture README: such a start
    /// throws), so 30 was given each such input, as blank text.
    /// </summary>
    private static bool RequiredBlankRefused(DifferenceContext c)
    {
        if (c.Run31.State != null || SourceLayout.Text(c.Capture30["status"]) is null or ByteEquivalence.Refused)
            return false;
        var refused = c.Run31.StartRefusals;
        return refused.Count > 0 && refused.All(r => r.Id == "A06-011" && RequiredInputOf(r.Sentence) is { } id
            && c.Plan.Inputs.TryGetValue(id, out var input) && input["required"] is JsonValue required && required.TryGetValue<bool>(out var isRequired) && isRequired);
    }

    /// <summary>The input A06-011 (<c>Input '&lt;id&gt;' is required; …</c>) names, or null.</summary>
    private static string? RequiredInputOf(string sentence)
    {
        const string Head = "Input '";
        if (!sentence.StartsWith(Head, StringComparison.Ordinal))
            return null;
        var end = sentence.IndexOf('\'', Head.Length);
        return end > Head.Length ? sentence[Head.Length..end] : null;
    }

    /// <summary>
    /// R4 item 7: 30 failed the job with a check's sentence because its checks refused every deliverable (each row
    /// <c>failed</c> or <c>skipped</c>, at least one <c>failed</c>, the failure one of the <c>failed</c> rows' reasons), and
    /// at 31 every deliverable is blocked or skipped, at least one blocked.
    /// </summary>
    private static bool AllRefused(DifferenceContext c)
    {
        var rows = (c.Capture30["deliverables"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var failed = rows.Where(r => SourceLayout.Text(r["state"]) == "failed").ToList();
        var failure = SourceLayout.Text(c.Capture30["failure"]);
        return failed.Count > 0
            && rows.All(r => SourceLayout.Text(r["state"]) is "failed" or "skipped")
            && failed.Any(r => SourceLayout.Text(r["reason"]) is { } reason && reason == failure)
            && c.Run31.Deliverables.Count > 0
            && c.Run31.Deliverables.All(d => d.State is DeliverableState.Blocked or DeliverableState.Skipped)
            && c.Run31.Deliverables.Any(d => d.State == DeliverableState.Blocked);
    }

    /// <summary>
    /// R4 item 3: a 31 deliverable its check refused (blocked) reads as <c>null</c> in the rest of the job, so 30's text,
    /// where it held that deliverable's composed text, is 31's with that text left out: 30's text with each such
    /// deliverable's composed text (31's, kept for the record) taken out. Only a deliverable composed with a text.
    /// </summary>
    private static (string Old, string New)? Withheld(DifferenceContext c, string old, string now)
    {
        var withheld = old;
        foreach (var blocked in c.Run31.Deliverables.Where(d => d.State == DeliverableState.Blocked && d.Key != c.Key31))
        {
            if (blocked.Text is { Length: > 0 } text && withheld.Contains(text, StringComparison.Ordinal))
                withheld = withheld.Replace(text, "", StringComparison.Ordinal);
        }

        return withheld == old ? null : (withheld, now);
    }

    /// <summary>
    /// Part 06 § 6.4's escape undone (<see cref="MarkdownEscape"/>): a backslash before an ASCII punctuation character
    /// removed, <c>&amp;#32;</c> and <c>&amp;#9;</c> written as the space and the tab they stand for.
    /// </summary>
    public static string Unescape(string escaped)
    {
        ArgumentNullException.ThrowIfNull(escaped);
        var text = new StringBuilder(escaped.Length);
        for (var i = 0; i < escaped.Length; i++)
        {
            var c = escaped[i];
            if (c == '\\' && i + 1 < escaped.Length && IsAsciiPunctuation(escaped[i + 1]))
            {
                text.Append(escaped[++i]);
            }
            else if (string.CompareOrdinal(escaped, i, "&#32;", 0, 5) == 0)
            {
                text.Append(' ');
                i += 4;
            }
            else if (string.CompareOrdinal(escaped, i, "&#9;", 0, 4) == 0)
            {
                text.Append('\t');
                i += 3;
            }
            else
            {
                text.Append(c);
            }
        }

        return text.ToString();
    }

    private static bool IsAsciiPunctuation(char c) =>
        c is >= '!' and <= '/' or >= ':' and <= '@' or >= '[' and <= '`' or >= '{' and <= '~';

    /// <summary>
    /// #1506 ruling 2: in a prompt or a rendered node's text under a loop over an input's list, each number that is the
    /// item's number at 31 (<c>n + 1</c>) where 30's text has 30's (<c>n</c>) at the same place, the rest the same: the
    /// texts are read as runs of digits and of other characters, the other runs equal, each digit run equal or that pair.
    /// </summary>
    private static (string Old, string New)? ItemIds(DifferenceContext c, string old, string now)
    {
        if (c.Kind is not (ComparedKind.Prompt or ComparedKind.Render) || c.Key30 is not { } key30 || c.Key31 is not { } key31)
            return null;
        var pairs = ItemPairs(key30, key31, c);
        if (pairs.Count == 0)
            return null;

        var a = DigitRuns(old);
        var b = DigitRuns(now);
        if (a.Count != b.Count)
            return null;
        var undone = new StringBuilder(now.Length);
        for (var i = 0; i < a.Count; i++)
        {
            var (digits, x) = a[i];
            var (_, y) = b[i];
            if (x == y)
                undone.Append(y);
            else if (digits && pairs.Contains((x, y)))
                undone.Append(x);
            else
                return null;
        }

        return (old, undone.ToString());
    }

    /// <summary>The (30 number, 31 number) of each item of an input's list on the way to <paramref name="key31"/>.</summary>
    private static HashSet<(string, string)> ItemPairs(string key30, string key31, DifferenceContext c)
    {
        var pairs = new HashSet<(string, string)>();
        var colon = key30.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
            return pairs;
        var segments30 = key30[(colon + 1)..].Split('/');
        var end = key31.IndexOfAny([':', '#']);
        if (end < 0 || !c.Plan.Entries.TryGetValue(key31[..end], out var entry))
            return pairs;

        var loops = new List<PlanEntry>();
        for (var parent = entry.Parent; parent != null && c.Plan.Entries.TryGetValue(parent, out var flow); parent = flow.Parent)
        {
            if (flow.Kind is PlanKind.For or PlanKind.While)
                loops.Insert(0, flow);
        }

        if (loops.Count != segments30.Length)
            return pairs;
        for (var i = 0; i < loops.Count; i++)
        {
            if (loops[i].Kind == PlanKind.For && SourceLayout.Text(loops[i].Json["in"]) is { } source && source.StartsWith("input.", StringComparison.Ordinal)
                && int.TryParse(segments30[i], NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            {
                pairs.Add((segments30[i], (n + 1).ToString(CultureInfo.InvariantCulture)));
            }
        }

        return pairs;
    }

    private static List<(bool Digits, string Text)> DigitRuns(string text)
    {
        var runs = new List<(bool, string)>();
        var start = 0;
        for (var i = 1; i <= text.Length; i++)
        {
            if (i == text.Length || char.IsAsciiDigit(text[i]) != char.IsAsciiDigit(text[start]))
            {
                runs.Add((char.IsAsciiDigit(text[start]), text[start..i]));
                start = i;
            }
        }

        return text.Length == 0 ? [] : runs;
    }

    /// <summary>
    /// Part 10 § 1.8, part 02 § 3.5: both prompts end with the custom trailer (<see cref="AgentTurn.CustomTrailer"/>, a line
    /// end, a format's JSON), and 30's format, written as § 1.8 rewrites it (every property listed in <c>required</c>, one
    /// 30 did not require made nullable), is 31's with its descriptions, titles and <c>$schema</c>/<c>$id</c> set aside and
    /// its member and list order as canonical JSON orders them (<c>required</c> and <c>type</c> lists compared as sets).
    /// The 31 format is then put back as 30's, so the bytes rules after this one compare the rest.
    /// </summary>
    private static (string Old, string New)? SentFormat(DifferenceContext c, string old, string now)
    {
        if (c.Kind != ComparedKind.Prompt)
            return null;
        var marker = AgentTurn.CustomTrailer + "\n";
        var at30 = old.LastIndexOf(marker, StringComparison.Ordinal);
        var at31 = now.LastIndexOf(marker, StringComparison.Ordinal);
        if (at30 < 0 || at31 < 0)
            return null;
        var schema30 = old[(at30 + marker.Length)..];
        var schema31 = now[(at31 + marker.Length)..];
        if (StrictJson.Parse(schema30) is not { Fault: null, Value: JsonObject s30 } || StrictJson.Parse(schema31) is not { Fault: null, Value: JsonObject s31 })
            return null;
        if (CanonicalJson.Write(Shape(As31(s30))) != CanonicalJson.Write(Shape(s31.DeepClone())))
            return null;
        return (old, now[..(at31 + marker.Length)] + schema30);
    }

    /// <summary>30's format written as part 10 § 1.8 rewrites it: each object's every property required, one 30 did not require nullable.</summary>
    private static JsonNode As31(JsonNode schema)
    {
        var copy = schema.DeepClone();
        Walk(copy, node =>
        {
            if (node["properties"] is not JsonObject properties)
                return;
            var required = (node["required"] as JsonArray ?? []).Select(SourceLayout.Text).ToHashSet(StringComparer.Ordinal);
            foreach (var (name, property) in properties)
            {
                if (!required.Contains(name) && property is JsonObject optional)
                    Nullable(optional);
            }

            node["required"] = new JsonArray([.. properties.Select(p => (JsonNode?)JsonValue.Create(p.Key))]);
        });
        return copy;
    }

    /// <summary>A property made nullable as § 1.8 writes it: <c>"null"</c> added to its <c>type</c>, <c>null</c> to its <c>enum</c>.</summary>
    private static void Nullable(JsonObject property)
    {
        switch (property["type"])
        {
            case JsonValue one when SourceLayout.Text(one) is { } type && type != "null":
                property["type"] = new JsonArray(type, "null");
                break;
            case JsonArray many when !many.Any(t => SourceLayout.Text(t) == "null"):
                many.Add("null");
                break;
        }

        if (property["enum"] is JsonArray members && !members.Any(m => m is null))
            members.Add(null);
    }

    /// <summary>A format's shape alone: descriptions, titles, <c>$schema</c> and <c>$id</c> dropped; <c>required</c> and <c>type</c> lists sorted.</summary>
    private static JsonNode Shape(JsonNode schema)
    {
        Walk(schema, node =>
        {
            foreach (var annotation in new[] { "description", "title", "$schema", "$id" })
                node.Remove(annotation);
            foreach (var set in new[] { "required", "type" })
            {
                if (node[set] is JsonArray list)
                    node[set] = new JsonArray([.. list.Select(SourceLayout.Text).Order(StringComparer.Ordinal).Select(t => (JsonNode?)JsonValue.Create(t))]);
            }
        });
        return schema;
    }

    /// <summary>Calls <paramref name="visit"/> on every object of <paramref name="node"/>, outermost first (a property named like a keyword is a property, never a keyword: only schema positions are walked).</summary>
    private static void Walk(JsonNode node, Action<JsonObject> visit)
    {
        if (node is not JsonObject schema)
            return;
        visit(schema);
        if (schema["properties"] is JsonObject properties)
        {
            foreach (var (_, property) in properties)
            {
                if (property is not null)
                    Walk(property, visit);
            }
        }

        if (schema["items"] is { } items)
            Walk(items, visit);
        foreach (var keyword in new[] { "anyOf", "oneOf", "allOf" })
        {
            foreach (var arm in (schema[keyword] as JsonArray ?? []).OfType<JsonObject>())
                Walk(arm, visit);
        }

        if (schema["$defs"] is JsonObject defs)
        {
            foreach (var (_, def) in defs)
            {
                if (def is not null)
                    Walk(def, visit);
            }
        }
    }

    /// <summary>
    /// Part 10 § 1.8: <paramref name="text"/> with every JSON member whose value is <c>null</c> (<c>"name":null</c>, as
    /// canonical JSON writes it) taken out with its comma; null when there is none.
    /// </summary>
    private static string? WithoutNullMembers(string text)
    {
        var result = new StringBuilder(text.Length);
        var changed = false;
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '"' && NullMember(text, i) is { } end)
            {
                var before = result.Length > 0 ? result[^1] : '\0';
                if (before == ',')
                {
                    result.Length--;
                    i = end;
                    changed = true;
                    continue;
                }

                if (before == '{' && end < text.Length && text[end] == ',')
                {
                    i = end + 1;
                    changed = true;
                    continue;
                }

                if (before == '{' && end < text.Length && text[end] == '}')
                {
                    i = end;
                    changed = true;
                    continue;
                }
            }

            result.Append(text[i]);
            i++;
        }

        return changed ? result.ToString() : null;
    }

    /// <summary>The end of a <c>"name":null</c> member starting at <paramref name="start"/> (a quote), or null.</summary>
    private static int? NullMember(string text, int start)
    {
        var i = start + 1;
        while (i < text.Length && text[i] != '"')
            i += text[i] == '\\' ? 2 : 1;
        const string Null = "\":null";
        return i < text.Length && string.CompareOrdinal(text, i, Null, 0, Null.Length) == 0 ? i + Null.Length : null;
    }

    /// <summary>
    /// R5: each run of white space where the texts differ is 31's, a macro body's trailing white space that 31 keeps
    /// (<see cref="Migration31Result.MacroBodies"/>: a macro's body after its one final line ending, a snippet's whole),
    /// before 30's run at that place; every other run, and all the text between, the same.
    /// </summary>
    private static (string Old, string New)? MacroTrailingWhitespace(DifferenceContext c, string old, string now)
    {
        var kept = KeptTrailingWhitespace(c.Migrated);
        if (kept.Count == 0)
            return null;
        var a = WhiteSpaceRuns(old);
        var b = WhiteSpaceRuns(now);
        if (a.Count != b.Count)
            return null;
        var undone = new StringBuilder(now.Length);
        for (var i = 0; i < a.Count; i++)
        {
            var (space, x) = a[i];
            var (_, y) = b[i];
            if (x == y)
                undone.Append(y);
            else if (space && kept.Any(t => y == t + x))
                undone.Append(x);
            else
                return null;
        }

        return (old, undone.ToString());
    }

    /// <summary>The trailing white space each macro body keeps at 31 that 30's <c>TrimEnd()</c> took away (none empty).</summary>
    private static HashSet<string> KeptTrailingWhitespace(Migration31Result migrated)
    {
        var kept = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in migrated.MacroBodies)
        {
            if (!migrated.Files.TryGetValue(path, out var file) || FrontMatter.Split(file)?.Body is not { } body)
                continue;
            if (path.StartsWith("macros/", StringComparison.Ordinal) && body.EndsWith('\n'))
                body = body[..^1];
            var trailing = body[body.TrimEnd().Length..];
            if (trailing.Length > 0)
                kept.Add(trailing);
        }

        return kept;
    }

    private static List<(bool Space, string Text)> WhiteSpaceRuns(string text)
    {
        var runs = new List<(bool, string)>();
        var start = 0;
        for (var i = 1; i <= text.Length; i++)
        {
            if (i == text.Length || char.IsWhiteSpace(text[i]) != char.IsWhiteSpace(text[start]))
            {
                runs.Add((char.IsWhiteSpace(text[start]), text[start..i]));
                start = i;
            }
        }

        return text.Length == 0 ? [] : runs;
    }

    /// <summary>
    /// R1: the text hangs on a decision of the 31 run whose rule holds a <c>!x</c> converted to <c>blank(x)</c> (an A10-008
    /// notice in <see cref="MigrationNotes.BlankConversions"/>; A10-050's <c>trim(x) == ""</c> is unruled and never counts),
    /// that held (<c>blank(x)</c> true) while <c>x</c> was not <c>null</c> (its read's hash is not the hash of
    /// <c>null</c>): an empty or white-space-only text, where 30's <c>!x</c> was false. A text hangs on a decision when the
    /// decision's row is the text's entry or a flow around it, or an entry the text's entry depends on; a deliverable's
    /// text and the job's status hang on every decision of the run.
    /// </summary>
    private static bool BlankConversion(DifferenceContext c)
    {
        const string Became = "' became blank(";
        const string Holds = "); it now also holds for empty and white-space-only text.";
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var notice in c.Migrated.Report.BlankConversions)
        {
            var at = notice.IndexOf(Became, StringComparison.Ordinal);
            if (at >= 0 && notice.EndsWith(Holds, StringComparison.Ordinal))
                paths.Add(notice[(at + Became.Length)..^Holds.Length]);
        }

        if (paths.Count == 0)
            return false;
        var nullHash = CanonicalJson.Hash(null);
        return c.Run31.Decisions.Any(d => d.Result == true
            && d.Reads.Any(r => paths.Contains(r.Path) && r.Hash != nullHash)
            && HangsOn(c, d.Owner));
    }

    private static bool HangsOn(DifferenceContext c, string owner)
    {
        if (c.Kind is ComparedKind.Status or ComparedKind.Deliverable || c.Key31 is not { } key)
            return true;
        static string EntryOf(string rowKey) => rowKey.IndexOfAny([':', '#']) is var end and >= 0 ? rowKey[..end] : rowKey;
        var entry = EntryOf(key);
        var decided = EntryOf(owner);
        return entry == decided
            || entry.StartsWith(decided + ".", StringComparison.Ordinal)
            || (c.Plan.Entries.TryGetValue(entry, out var reader) && reader.Depends.Contains(decided));
    }
}
