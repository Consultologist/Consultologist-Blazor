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
            "plain text read into a Markdown deliverable is escaped; unescaping the values the run read gives 30's bytes")
        {
            Bytes = (c, old, now) => c.Kind == ComparedKind.Deliverable ? EscapedReads(c, old, now) : null,
        },
        new("item-ids-from-one",
            "ruled by the owner on #1506, 2026-11-21, 2 (item numbers are 1-based everywhere users see them; 30 counted an array input's from 0); D12",
            "an item of a loop over an input's list is numbered from 1, where 30 numbered it from 0")
        {
            Bytes = ItemIds,
        },
        new("position-names-from-the-loop",
            "part 04 § 2.7 (decided on #1392: with itemLabel null an item is named by its loop's label and its 1-based position, where 30 named an input loop's item by the input's label); part 10 § 3.2",
            "an item of a loop over an input's list with no itemLabel is named by the loop's label and its position at 31, where 30 gave it the input's label")
        {
            Bytes = PositionNames,
        },
        new("sent-format",
            "part 10 § 1.8 (every property required, an optional one nullable); part 10 § 3.2",
            "the format appended to the prompt is 30's as § 1.8 rewrites it: every property required, an optional one nullable, written in canonical order")
        {
            Bytes = SentFormat,
        },
        new("explicit-null-for-optional",
            "part 10 § 1.8; part 10 § 3.2",
            "an optional field 30 left out is an explicit null at 31: the texts differ only by null members of the fields § 1.8 made nullable in a reply's or an input's format")
        {
            Bytes = (c, old, now) => c.Kind is ComparedKind.Prompt or ComparedKind.Render && WithoutNullMembers(now, OptionalMembers(c.Plan)) is { } fewer && fewer != now ? (old, fewer) : null,
        },
        new("macro-trailing-whitespace",
            "R5, ruled by the owner on #1507, 2026-11-22, 1; part 05 § 8.6; part 10 § 3.2",
            "a macro body keeps 31's rule (one final line ending dropped), where 30 trimmed all its trailing white space")
        {
            Bytes = MacroTrailingWhitespace,
        },
        new("blank-conversion",
            "R1, ruled by the owner on #1507, 2026-11-20; part 10 §§ 2.3, 3.2",
            "the text is gated by a '!x' converted to blank(x), which held at 31 for an empty or white-space-only text where 30's !x did not, so only 31 has it")
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
    /// R4 item 6: 31's start refused the run, every refusal A06-011 for an input the plan requires, each such input supplied
    /// in the 31 request (<see cref="DifferenceContext.Request"/>) as blank text (<c>""</c> or white space only, as
    /// <c>blank()</c> judges), and 30 started the job (its status is not <c>Refused</c>). With no request, nothing is
    /// explained: an input the request lost would read as refused alike.
    /// </summary>
    private static bool RequiredBlankRefused(DifferenceContext c)
    {
        if (c.Run31.State != null || c.Request is not { } request || SourceLayout.Text(c.Capture30["status"]) is null or ByteEquivalence.Refused)
            return false;
        var refused = c.Run31.StartRefusals;
        return refused.Count > 0 && refused.All(r => r.Id == "A06-011" && RequiredInputOf(r.Sentence) is { } id
            && c.Plan.Inputs.TryGetValue(id, out var input) && input["required"] is JsonValue required && required.TryGetValue<bool>(out var isRequired) && isRequired
            && request.Inputs.GetValueOrDefault(id) is JsonValue given && SourceLayout.Text(given) is { } text
            && text.EnumerateRunes().All(Rune.IsWhiteSpace));
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
    /// deliverable's composed text (31's, kept for the record) taken out. Only a deliverable composed with a text, and only
    /// one the compared row reads at 31 (<c>deliverable.&lt;id&gt;.content</c> among <see cref="RowReads"/>; final review
    /// B1): a text equal to the blocked one that a row composes on its own is not withheld, and a row that lost it is
    /// unexpected. Part 06 § 6.7 lets only checks read a deliverable at 31, so a published package's compared rows never do.
    /// </summary>
    private static (string Old, string New)? Withheld(DifferenceContext c, string old, string now)
    {
        var reads = RowReads(c);
        var withheld = old;
        foreach (var blocked in c.Run31.Deliverables.Where(d => d.State == DeliverableState.Blocked && d.Key != c.Key31
                     && reads.Any(r => r == d.Key + ".content" || r.StartsWith(d.Key + ".content.", StringComparison.Ordinal))))
        {
            if (blocked.Text is { Length: > 0 } text && withheld.Contains(text, StringComparison.Ordinal))
                withheld = withheld.Replace(text, "", StringComparison.Ordinal);
        }

        return withheld == old ? null : (withheld, now);
    }

    /// <summary>
    /// Every path the compared row reads at 31: its plan entry's reads (<see cref="PlanEntry.Reads"/>), and the reads the
    /// compiled document records for each snippet or macro it places (<see cref="PlanEntry.Placements"/>, nested ones
    /// included). Empty for the status, or a key the plan has no entry for.
    /// </summary>
    private static HashSet<string> RowReads(DifferenceContext c)
    {
        var reads = new HashSet<string>(StringComparer.Ordinal);
        if (c.Key31 is not { } key || !c.Plan.Entries.TryGetValue(EntryOf(key), out var entry))
            return reads;
        reads.UnionWith(entry.Reads);
        foreach (var placement in entry.Placements)
        {
            var placed = c.Plan.Document["snippets"]?[placement.Site] ?? c.Plan.Document["macros"]?[placement.Site];
            foreach (var read in placed?["reads"] as JsonArray ?? [])
            {
                if (SourceLayout.Text(read) is { } path)
                    reads.Add(path);
            }
        }

        return reads;
    }

    /// <summary>The entry of a row key: the key before its first <c>:</c> (an instance) or <c>#</c> (a part).</summary>
    private static string EntryOf(string rowKey) => rowKey.IndexOfAny([':', '#']) is var end and >= 0 ? rowKey[..end] : rowKey;

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

    /// <summary>
    /// Part 06 § 6.4, held to the text that came from a read (Task 15 fix round 1): each place in 31's deliverable text that
    /// is the escape (<see cref="MarkdownEscape.Escape(string, bool)"/>, at a line's start or inside one) of a value the run
    /// held (<see cref="RunValues"/>) is put back as that value; every other byte, the template's own text included, is
    /// left as 31 wrote it. Longer values are placed first and no two places overlap, so 30's own backslash inside a value
    /// is never undone a second time. With no request (<see cref="DifferenceContext.Request"/>) the inputs and the profile
    /// are not known, and only the run's own values count.
    /// </summary>
    private static (string Old, string New)? EscapedReads(DifferenceContext c, string old, string now)
    {
        var spans = new List<(int Start, int Length, string Plain)>();
        foreach (var value in RunValues(c))
        {
            foreach (var escaped in new[] { MarkdownEscape.Escape(value, startsLine: true), MarkdownEscape.Escape(value, startsLine: false) }.Distinct(StringComparer.Ordinal))
            {
                if (escaped == value)
                    continue;
                for (var at = now.IndexOf(escaped, StringComparison.Ordinal); at >= 0; at = now.IndexOf(escaped, at + 1, StringComparison.Ordinal))
                {
                    if (!spans.Any(s => at < s.Start + s.Length && s.Start < at + escaped.Length))
                        spans.Add((at, escaped.Length, value));
                }
            }
        }

        if (spans.Count == 0)
            return null;
        var undone = new StringBuilder(now.Length);
        var from = 0;
        foreach (var (start, length, plain) in spans.OrderBy(s => s.Start))
        {
            undone.Append(now, from, start - from).Append(plain);
            from = start + length;
        }

        undone.Append(now, from, now.Length - from);
        return (old, undone.ToString());
    }

    /// <summary>
    /// Every text value the 31 run held that a template may read as plain text: each string or number in the inputs, the
    /// profile and the run facts it was given, in each reply it served, and each rendered node's text; longest first, then
    /// ordinal, none empty.
    /// </summary>
    private static List<string> RunValues(DifferenceContext c)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        void Leaves(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject members:
                    foreach (var (_, member) in members)
                        Leaves(member);
                    break;
                case JsonArray items:
                    foreach (var item in items)
                        Leaves(item);
                    break;
                case JsonValue value when value.TryGetValue<string>(out var text):
                    values.Add(text);
                    break;
                case JsonValue number when number.GetValueKind() == System.Text.Json.JsonValueKind.Number:
                    values.Add(number.ToJsonString());
                    break;
            }
        }

        if (c.Request is { } request)
        {
            foreach (var (_, input) in request.Inputs)
                Leaves(input);
            foreach (var (_, field) in request.Profile)
            {
                if (field is not null)
                    values.Add(field);
            }

            Leaves(request.RunFacts);
        }

        foreach (var (_, output) in c.Run31.Outputs)
            Leaves(output);
        foreach (var render in c.Run31.Renders)
            values.Add(render.Text);
        values.Remove("");
        return [.. values.OrderByDescending(v => v.Length).ThenBy(v => v, StringComparer.Ordinal)];
    }

    private static bool IsAsciiPunctuation(char c) =>
        c is >= '!' and <= '/' or >= ':' and <= '@' or >= '[' and <= '`' or >= '{' and <= '~';

    /// <summary>
    /// #1506 ruling 2: in a prompt or a rendered node's text whose nearest loop iterates an input's list, a number that is the
    /// item's number at 31 (<c>n + 1</c>) where 30's text has 30's (<c>n</c>), only where the entry's 31 body writes the
    /// item's id (<c>{{ item.id }}</c>): the changed number stands in 30's text between the body's own text before and after
    /// that tag on its line (<c>Prior letter </c> and <c>:</c>). The texts are read as runs of digits and of other characters;
    /// every other run is the same.
    /// </summary>
    private static (string Old, string New)? ItemIds(DifferenceContext c, string old, string now)
    {
        if (c.Kind is not (ComparedKind.Prompt or ComparedKind.Render) || c.Key30 is not { } key30 || c.Key31 is not { } key31)
            return null;
        if (ItemPair(key30, key31, c) is not { } pair || ItemIdAnchors(c, key31) is not { Count: > 0 } anchors)
            return null;

        var a = DigitRuns(old);
        var b = DigitRuns(now);
        if (a.Count != b.Count)
            return null;
        var undone = new StringBuilder(now.Length);
        var at = 0;
        for (var i = 0; i < a.Count; i++)
        {
            var (digits, x) = a[i];
            var (_, y) = b[i];
            if (x == y)
                undone.Append(y);
            else if (digits && (x, y) == pair && anchors.Any(anchor =>
                         old.AsSpan(0, at).EndsWith(anchor.Before, StringComparison.Ordinal)
                         && old.AsSpan(at + x.Length).StartsWith(anchor.After, StringComparison.Ordinal)))
                undone.Append(x);
            else
                return null;
            at += x.Length;
        }

        return (old, undone.ToString());
    }

    /// <summary>
    /// Part 04 § 2.7: in any compared text but the status, the heading <c>## &lt;loop label&gt; &lt;n&gt;</c> (<c>n</c>
    /// digits, ending its line; a template may place the loop's <c>.content</c> after text on its first line) that a loop over
    /// an input's list with <c>itemLabel: null</c> writes in its <c>.content</c>, every item of it named by its position, where
    /// the loop's label is not the input's, put back as 30 wrote it, <c>## &lt;input label&gt; &lt;n&gt;</c>. The position is
    /// the same at both (30's position names count from 1 too); nothing else on the line, and no other line, is touched. A loop
    /// with an <c>itemLabel</c> is not read: its position name is only the fallback for an item whose name is empty, which the
    /// text alone cannot tell from a name that reads so, so that fallback is left unexplained.
    /// </summary>
    private static (string Old, string New)? PositionNames(DifferenceContext c, string old, string now)
    {
        if (c.Kind == ComparedKind.Status)
            return null;

        var names = new List<(string Loop, string Input)>();
        foreach (var entry in c.Plan.Entries.Values.OrderBy(e => e.Order))
        {
            if (entry.Kind != PlanKind.For || entry.Json["itemLabel"] is not null || SourceLayout.Text(entry.Json["in"]) is not { } source
                || !source.StartsWith("input.", StringComparison.Ordinal) || !source.EndsWith(".value", StringComparison.Ordinal))
                continue;
            var id = source["input.".Length..^".value".Length];
            if (SourceLayout.Text(entry.Json["label"]) is { } loop && c.Plan.Inputs.TryGetValue(id, out var input)
                && SourceLayout.Text(input["label"]) is { } label && loop != label)
                names.Add((loop, label));
        }

        if (names.Count == 0)
            return null;
        var lines = now.Split('\n');
        var changed = false;
        for (var i = 0; i < lines.Length; i++)
        {
            foreach (var (loop, label) in names)
            {
                var head = "## " + loop + " ";
                var at = lines[i].LastIndexOf(head, StringComparison.Ordinal);
                if (at >= 0 && lines[i].Length > at + head.Length && lines[i][(at + head.Length)..].All(char.IsAsciiDigit))
                {
                    lines[i] = lines[i][..at] + "## " + label + " " + lines[i][(at + head.Length)..];
                    changed = true;
                    break;
                }
            }
        }

        return changed ? (old, string.Join('\n', lines)) : null;
    }

    /// <summary>
    /// The (30 number, 31 number) of the item <c>item</c> names on the way to <paramref name="key31"/>: the nearest <c>for</c>
    /// around the entry, when it iterates an input's list; else null.
    /// </summary>
    private static (string, string)? ItemPair(string key30, string key31, DifferenceContext c)
    {
        var colon = key30.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
            return null;
        var segments30 = key30[(colon + 1)..].Split('/');
        var end = key31.IndexOfAny([':', '#']);
        if (end < 0 || !c.Plan.Entries.TryGetValue(key31[..end], out var entry))
            return null;

        var loops = new List<PlanEntry>();
        for (var parent = entry.Parent; parent != null && c.Plan.Entries.TryGetValue(parent, out var flow); parent = flow.Parent)
        {
            if (flow.Kind is PlanKind.For or PlanKind.While)
                loops.Insert(0, flow);
        }

        if (loops.Count != segments30.Length)
            return null;
        var nearest = loops.FindLastIndex(l => l.Kind == PlanKind.For);
        return nearest >= 0
            && SourceLayout.Text(loops[nearest].Json["in"]) is { } source && source.StartsWith("input.", StringComparison.Ordinal)
            && int.TryParse(segments30[nearest], NumberStyles.None, CultureInfo.InvariantCulture, out var n)
                ? (segments30[nearest], (n + 1).ToString(CultureInfo.InvariantCulture))
                : null;
    }

    private static readonly System.Text.RegularExpressions.Regex ItemIdTag =
        new(@"\{\{(?<l>[~-]?)\s*item\.id\s*(?<r>[~-]?)\}\}", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Each <c>{{ item.id }}</c> of the 31 body of <paramref name="key31"/>'s entry (<c>nodes/&lt;path&gt;.md</c>), with the
    /// body's own text before it and after it on its line, up to another tag (one side may be empty, not both).
    /// </summary>
    private static List<(string Before, string After)> ItemIdAnchors(DifferenceContext c, string key31)
    {
        var anchors = new List<(string, string)>();
        var end = key31.IndexOfAny([':', '#']);
        var entry = end < 0 ? key31 : key31[..end];
        if (!entry.StartsWith("node.", StringComparison.Ordinal)
            || !c.Migrated.Files.TryGetValue("nodes/" + entry["node.".Length..].Replace('.', '/') + ".md", out var file)
            || FrontMatter.Split(file)?.Body is not { } body)
            return anchors;

        foreach (System.Text.RegularExpressions.Match tag in ItemIdTag.Matches(body))
        {
            var lineStart = body.LastIndexOf('\n', Math.Max(0, tag.Index - 1)) + 1;
            var before = body[lineStart..tag.Index];
            var open = before.LastIndexOf("}}", StringComparison.Ordinal);
            if (open >= 0)
                before = before[(open + 2)..];
            var after = body[(tag.Index + tag.Length)..];
            var stop = after.IndexOfAny(['\n', '\r']);
            if (stop >= 0)
                after = after[..stop];
            var next = after.IndexOf("{{", StringComparison.Ordinal);
            if (next >= 0)
                after = after[..next];
            if (tag.Groups["l"].Length > 0)
                before = before.TrimEnd();
            if (tag.Groups["r"].Length > 0)
                after = after.TrimStart();
            if (before.Length + after.Length > 0)
                anchors.Add((before, after));
        }

        return anchors;
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
    /// Part 10 § 1.8: both prompts end with the custom trailer (<see cref="AgentTurn.CustomTrailer"/>, a line end, a format's
    /// JSON), and 30's format, written as § 1.8 rewrites it (every property listed in <c>required</c>, one 30 did not require
    /// made nullable), is 31's member for member, in canonical JSON's order (<c>required</c> and <c>type</c> lists compared as
    /// sets). A description, a title, <c>$schema</c> or <c>$id</c> 31 sends and 30 did not is a difference no ruling declares,
    /// so the text stays unexpected.
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

    /// <summary>
    /// A format with its <c>required</c> and <c>type</c> lists sorted (they are sets): every other member, a description,
    /// a title, <c>$schema</c> and <c>$id</c> included, is compared as it is (the controller's ruling on Task 15's review: only
    /// § 1.8's change is declared; 31 sending a description where 30 sent the schema alone is not).
    /// </summary>
    private static JsonNode Shape(JsonNode schema)
    {
        Walk(schema, node =>
        {
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
    /// canonical JSON writes it) and whose name is one of <paramref name="optional"/> taken out with its comma; null when
    /// there is none.
    /// </summary>
    private static string? WithoutNullMembers(string text, IReadOnlySet<string> optional)
    {
        var result = new StringBuilder(text.Length);
        var changed = false;
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '"' && NullMember(text, i) is { } end && optional.Contains(text[(i + 1)..(end - "\":null".Length)]))
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

    /// <summary>
    /// The names of the members § 1.8 made nullable: each property that takes <c>null</c> in the format of an agent entry (a
    /// reply's) or of an input of <paramref name="plan"/>, at any depth.
    /// </summary>
    private static HashSet<string> OptionalMembers(RunPlan plan)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var formats = plan.Entries.Values.Where(e => e.Kind == PlanKind.Agent).Select(e => e.Format)
            .Concat(plan.Inputs.Values.Select(i => i["format"]));
        foreach (var format in formats)
        {
            if (plan.Formats.IsText(format))
                continue;
            Walk(plan.Formats.Expand(format), node =>
            {
                foreach (var (name, property) in node["properties"] as JsonObject ?? [])
                {
                    if (TakesNull(property))
                        names.Add(name);
                }
            });
        }

        return names;
    }

    private static bool TakesNull(JsonNode? schema) => schema is JsonObject shape && (
        (shape["type"] is JsonValue one && SourceLayout.Text(one) == "null")
        || (shape["type"] is JsonArray many && many.Any(t => SourceLayout.Text(t) == "null"))
        || (shape["enum"] is JsonArray members && members.Any(m => m is null))
        || new[] { shape["anyOf"], shape["oneOf"] }.Any(arms => (arms as JsonArray ?? []).Any(TakesNull)));

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
    /// R5: each run of white space where the texts differ is 31's, a macro body's trailing white space that 31 keeps (a
    /// macro's body, <c>macros/**</c> in <see cref="Migration31Result.MacroBodies"/>, after its one final line ending), before
    /// 30's run at that place, and it follows that body's own last text: in 30's text the characters before the run end with
    /// the body's last word (its text after its last white space; a body that ends in a tag has no word to find, and explains
    /// nothing). Every other run, and all the text between, is the same. A snippet written from a macro (D4) is not a macro
    /// body at 31: the migration writes it with 30's trimming, so it is never explained here.
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
            else if (space && i > 0 && kept.Any(k => y == k.Trailing + x && a[i - 1].Text.EndsWith(k.LastWord, StringComparison.Ordinal)))
                undone.Append(x);
            else
                return null;
        }

        return (old, undone.ToString());
    }

    /// <summary>
    /// Each macro body's trailing white space that 31 keeps and 30's <c>TrimEnd()</c> took away (none empty), with the body's
    /// last word, the text it follows.
    /// </summary>
    private static List<(string Trailing, string LastWord)> KeptTrailingWhitespace(Migration31Result migrated)
    {
        var kept = new List<(string, string)>();
        foreach (var path in migrated.MacroBodies.Where(p => p.StartsWith("macros/", StringComparison.Ordinal)))
        {
            if (!migrated.Files.TryGetValue(path, out var file) || FrontMatter.Split(file)?.Body is not { } body)
                continue;
            if (body.EndsWith('\n'))
                body = body[..^1];
            var text = body.TrimEnd();
            var trailing = body[text.Length..];
            var word = text[(text.LastIndexOfAny([' ', '\t', '\n', '\r']) + 1)..];
            if (trailing.Length > 0 && word.Length > 0 && !word.EndsWith("}}", StringComparison.Ordinal))
                kept.Add((trailing, word));
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
    /// R1, held to the one effect such a decision can have: a text only 31 has (a prompt, a rendered node's text or a
    /// deliverable), whose own gate decided at 31 was exactly <c>blank(x)</c> for a <c>!x</c> the migration converted (an
    /// A10-008 notice in <see cref="MigrationNotes.BlankConversions"/>; A10-050's <c>trim(x) == ""</c> is unruled and never
    /// counts), and held while <c>x</c> was not <c>null</c> (its read's hash is not the hash of <c>null</c>): an empty or
    /// white-space-only text, where 30's <c>!x</c> was false, so 30 skipped what 31 ran. The gate is the <c>if</c>
    /// (<c>self</c>) of the text's own row or of a flow around it (its expression's hash that of <c>blank(x)</c> alone). A
    /// text both sides have, or the job's status, is never explained here: what the gate changed inside it cannot be told
    /// from the bytes.
    /// </summary>
    private static bool BlankConversion(DifferenceContext c)
    {
        if (c.Kind == ComparedKind.Status || c.OldText is not null || c.NewText is null || c.Key31 is not { } key)
            return false;

        const string Became = "' became blank(";
        const string Holds = "); it now also holds for empty and white-space-only text.";
        var gates = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var notice in c.Migrated.Report.BlankConversions)
        {
            var at = notice.IndexOf(Became, StringComparison.Ordinal);
            if (at >= 0 && notice.EndsWith(Holds, StringComparison.Ordinal))
            {
                var path = notice[(at + Became.Length)..^Holds.Length];
                gates[CanonicalJson.Hash(new JsonObject { ["blank"] = new JsonArray(new JsonObject { ["var"] = path }) })] = path;
            }
        }

        if (gates.Count == 0)
            return false;
        var nullHash = CanonicalJson.Hash(null);
        return c.Run31.Decisions.Any(d => d.Result == true && d.Site == "self"
            && gates.TryGetValue(d.ExpressionHash, out var path)
            && d.Reads.Any(r => r.Path == path && r.Hash != nullHash)
            && Gates(d.Owner, key));
    }

    /// <summary>Whether a gate decided for row <paramref name="owner"/> gates the row <paramref name="key"/>: it is that row, or a flow instance around it.</summary>
    private static bool Gates(string owner, string key)
    {
        return owner == key || EntryOf(key).StartsWith(EntryOf(owner) + ".", StringComparison.Ordinal);
    }
}
