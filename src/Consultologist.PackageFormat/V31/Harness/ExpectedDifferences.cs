using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Migration;
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
/// Two kinds of rule: a <b>case</b> rule explains a whole compared text by where it is and how the two runs ended (a status
/// 30 refused and 31 completed for the two captures R2 names; a text only 31 has there); a <b>bytes</b> rule undoes its one
/// declared difference wherever it finds it between the two texts (unescaping 31's escaped plain text, say), whatever else
/// differs. #1508 (comment 5, item 5; R19): <see cref="Credit"/> credits rule by rule. It takes the first case rule that
/// holds; else it applies the bytes rules in <see cref="Rules"/>' order, each to the texts the earlier left, again until
/// none changes anything (each at most once), and keeps a rule's change only where it brings 31's text nearer 30's (fewer
/// tokens differ, <see cref="Distance"/>), so a rule is never credited for a change of its own making. Each rule kept is
/// credited, in <see cref="Rules"/>' order; what differs after them all is the unexplained remainder. A difference is
/// expected only when a rule is credited and nothing remains (<see cref="Explain"/>).
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
        ["blank-conversion"] = "#1508, comment 5, item 8 (2026-11-25); part 05 § 7; part 10 § 2.3: strip(x) == \"\" is blank(x), which also holds for a null x (a node that did not run, a field of an object input left out), where 30's comparison did not.",
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

    /// <summary>The rules, in the order <see cref="Credit"/> tries the case rules and applies the bytes rules, and names those it credits.</summary>
    public static IReadOnlyList<Rule> Rules { get; } =
    [
        new("empty-input-list-runs",
            "R2, ruled by the owner on #1507, 2026-11-20; part 10 § 3.2; part 04 § 2.4",
            "a fanned input with no entries settles at 0 items and the job runs, where 30 refused the start; the texts it then gives are 31's alone")
        {
            // R2 rules that these runs complete: a text only 31 has is explained only when the 31 run completed (final review B3).
            Case = c => EmptyInputListCaptures.Contains((c.Package, c.Scenario)) && c.Run31.State == RunState.Completed
                && (IsStatus(c, ByteEquivalence.Refused, nameof(RunState.Completed)) || (c.Kind != ComparedKind.Status && c.OldText is null)),
        },
        new("skipped-deliverable-reads-null",
            "R4 item 1, ruled by the owner on #1507, 2026-11-21; part 10 § 3.2; part 04",
            "reading .content of a deliverable skipped by its if reads null and no longer fails the job; the texts after the read are 31's alone")
        {
            // Final review B3: only when the 31 run completed, and for a text, only a deliverable only 31 has: 30 read a
            // deliverable's .content only while composing deliverables (a deliverable's macro), after every node had run,
            // so what its failure left out is the deliverables after the read; a prompt or a rendered text only 31 has is not.
            Case = c => ReadSkippedDeliverable(c) && c.Run31.State == RunState.Completed
                && (IsStatus(c, nameof(RunState.Failed), nameof(RunState.Completed)) || (c.Kind == ComparedKind.Deliverable && c.OldText is null)),
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
        new("failed-job-dispatch-order",
            "#1508, comment 5, item 7, #9, ruled by the owner on 2026-11-25; part 10 § 3.2",
            "both jobs failed at the same reply, which its node's failIfEmpty, or its node's format (onInvalid: fail), refused at both; each sent what its dispatch order reached first, so a text only one side has is the order's")
        {
            Case = FailedAlike,
        },
        new("flag-becomes-retry",
            "#1508, comment 5, item 3, ruled by the owner on 2026-11-25 (onInvalid: flag becomes retry); comment 6, item 3; part 05 § 1.2; part 10 §§ 1.2, 3.2",
            "a node that declared onInvalid: flag kept a reply that did not fit its format at 30, and the job ran on; at 31 the node asks once more with the validation error, then fails, so its prompt ends with the retry's text, the job fails, and a text only 30 has is what 30 ran on the kept reply")
        {
            Case = FlagRetried,
            Bytes = RetryText,
        },
        new("reply-never-a-template",
            "#1508, comment 5, item 7, #5, ruled by the owner on 2026-11-25; part 05 § 1; part 10 § 3.2",
            "a reply is never rendered as a template at 31, where 30 rendered one that held '{{': 31's text holds the reply as written, or stripped as a migrated deliverable reads a source (#1508, item 3), where 30's holds it rendered, and a job 30 failed because a reply did not render completes")
        {
            Case = ReplyNotRendered,
            Bytes = RenderedReplies,
        },
        new("escaped-plain-text",
            "part 06 § 6.4; part 10 § 3.2; D8; #1508, comment 5, items 7 (#13) and 8 (D23), ruled by the owner on 2026-11-25",
            "plain text read into a Markdown deliverable is escaped (a computed text value too), and so are plain-text items joined into a Markdown loop's content, which a prompt reads escaped; unescaping the values the run held gives 30's bytes")
        {
            Bytes = (c, old, now) => c.Kind == ComparedKind.Deliverable || (c.Kind is ComparedKind.Prompt or ComparedKind.Render && ReadsEscapedText(c)) ? EscapedReads(c, old, now) : null,
        },
        new("item-ids-from-one",
            "ruled by the owner on #1506, 2026-11-21, 2 (item numbers are 1-based everywhere users see them; 30 counted an array input's from 0); D12",
            "an item of a loop over an input's list is numbered from 1, where 30 numbered it from 0")
        {
            Bytes = ItemIds,
        },
        new("position-names-from-the-loop",
            "part 04 § 2.7 (decided on #1392: with itemLabel null an item is named by its loop's label and its 1-based position, where 30 named an input loop's item by the input's label); #1508, comment 5, item 4, ruled by the owner on 2026-11-25 (an empty itemLabel field falls back to the position name); part 10 § 3.2",
            "an item of a loop over an input's list named by its position (no itemLabel, or an empty field) is named by the loop's label and its position at 31, where 30 gave it the input's label")
        {
            Bytes = PositionNames,
        },
        new("sent-format",
            "part 10 § 1.8 (every property required, an optional one nullable); R12 and #1508, comment 5, item 7, #3, ruled by the owner on 2026-11-25 (a format's description sent; a classifier's answers sent as its format); #1508, 2026-11-26, item 7 (30's answer line dropped); part 10 § 3.2",
            "the format appended to the prompt is 30's as § 1.8 rewrites it: every property required, an optional one nullable, written in canonical order, with the descriptions 31 sends where 30's had none; or a classifier's answers sent as its format, where 30 listed them in its prompt's last line, which the migration drops")
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
        new("input-trailing-whitespace",
            "#1508, comment 5, item 7, #6, ruled by the owner on 2026-11-25; part 10 § 3.2",
            "an input's text keeps its trailing white space at 31, where 30 trimmed it: the value as 30 trimmed it stands where 31's text holds it whole")
        {
            Bytes = InputTrailingWhitespace,
        },
        new("before-after-macro-marker",
            "#1508, comment 5, item 7, #10, ruled by the owner on 2026-11-25; part 05 § 10; part 10 §§ 1.4, 3.2",
            "a macro 30's engine placed before or after a node's reply is a marker at 31: the placing prompt holds its instruction, and its body stands where the model wrote the marker, so only the white space between the body and the reply differs, where 30 joined them by a blank line")
        {
            Bytes = BeforeAfterMarker,
        },
        new("while-no-pass-separator",
            "the controller's ruling on #1508 (Task 33's review: covered as the owner ruled for a flow that ran nothing, or listed as expected); part 10 §§ 1.2, 3.2",
            "a while that ran no pass composes as empty text at both, but 30's composer dropped its part with its separator, where 31's template keeps the blank line between it and the source beside it")
        {
            Bytes = WhileNoPassSeparator,
        },
        new("loop-item-not-run",
            "#1508, 2026-11-26, item 4, ruled by the owner (Q4 stands; both differences expected); part 10 §§ 1.2, 3.2",
            "an item of a loop that did not run (past its max, or its body skipped) writes nothing at 31, where 30's template loop wrote its fixed text for it with its reads empty; that text is 30's alone")
        {
            Bytes = LoopItemNotRun,
        },
        new("chosen-macro-final-line-ending",
            "#1508, 2026-11-26, item 5, ruled by the owner; R5; part 05 § 8.6; part 10 § 3.2",
            "a macro the model chose whose body ends in a line ending loses that one line ending at 31, where 30 wrote the body whole")
        {
            Bytes = ChosenMacroLineEnding,
        },
        new("empty-read-trailing-space",
            "#1508, 2026-11-26, item 5, ruled by the owner; part 10 § 3.2",
            "a read that rendered empty at a line's end leaves the white space before it at 31, which 31's snippet keeps and 30's macro trimmed")
        {
            Bytes = EmptyReadTrailingSpace,
        },
        new("macro-choice-classifier",
            "#1508, comment 6, items 1 and 2, ruled by the owner on 2026-11-25 (R13, R14); part 10 §§ 1.2, 1.4, 3.2",
            "a macro 30's model chose is gated by a judgement node, answered from 30's reply: the node's prompt is 31's alone, and the placing prompt holds the macro's instruction, its \"otherwise\" clause removed, only where the node answered yes")
        {
            Case = ClassifierPrompt,
            Bytes = ChoiceInstruction,
        },
        new("blank-conversion",
            "R1, ruled by the owner on #1507, 2026-11-20; part 10 §§ 2.3, 3.2",
            "the text is gated by a '!x' converted to blank(x), which held at 31 for an empty or white-space-only text where 30's !x did not, so only 31 has it")
        {
            Case = BlankConversion,
        },
    ];

    /// <summary>
    /// #1508 (R19): what the rules credit in one difference: the rules credited, in <see cref="Rules"/>' order, and whether
    /// a remainder no rule explains is left (<paramref name="Unexplained"/>).
    /// </summary>
    public sealed record Credited(IReadOnlyList<Rule> Rules, bool Unexplained)
    {
        /// <summary>The difference is expected: a rule is credited and nothing remains.</summary>
        public bool Expected => Rules.Count > 0 && !Unexplained;

        /// <summary>Each rule credited, as <see cref="Rule.Said"/>, joined by <c>"; "</c>; null when none is.</summary>
        public string? Explanation => Rules.Count > 0 ? string.Join("; ", Rules.Select(r => r.Said)) : null;
    }

    /// <summary>
    /// #1508 (comment 5, item 5; R19): the rules that explain part or all of the difference <paramref name="context"/>
    /// describes, and whether anything is left (the class's remarks say how).
    /// </summary>
    public static Credited Credit(DifferenceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var rule in Rules)
        {
            if (rule.Case?.Invoke(context) == true)
                return new Credited([rule], false);
        }

        if (context.OldText is not { } old || context.NewText is not { } now || context.Kind == ComparedKind.Status)
            return new Credited([], true);

        // Each rule undoes its own difference, so one may only find it once another has (an item number inside a prompt
        // whose format also differs): the rules are applied in turn until none changes anything, each at most once, and a
        // change is kept only where it brings the texts nearer.
        var applied = new List<Rule>();
        var distance = Distance(old, now);
        for (var changed = true; changed && distance > 0;)
        {
            changed = false;
            foreach (var rule in Rules)
            {
                if (applied.Contains(rule) || rule.Bytes?.Invoke(context, old, now) is not { } undone || (undone.Old == old && undone.New == now))
                    continue;
                var nearer = Distance(undone.Old, undone.New);
                if (nearer >= distance)
                    continue;
                (old, now, distance) = (undone.Old, undone.New, nearer);
                applied.Add(rule);
                changed = true;
            }
        }

        return new Credited([.. Rules.Where(applied.Contains)], !string.Equals(old, now, StringComparison.Ordinal));
    }

    /// <summary>
    /// Why the difference <paramref name="context"/> describes is expected (<see cref="Credited.Explanation"/>), or
    /// <c>null</c> when it is not: no rule is credited, or a remainder is left (<see cref="Credit"/>).
    /// </summary>
    public static string? Explain(DifferenceContext context) => Credit(context) is { Expected: true } credited ? credited.Explanation : null;

    /// <summary>
    /// #1508 (R13): a prompt only 31 sent, of a macro-choice classifier (<see cref="ChoiceClassifiers"/>), which 30 had no
    /// node for.
    /// </summary>
    private static bool ClassifierPrompt(DifferenceContext c) =>
        c.Kind == ComparedKind.Prompt && c.OldText is null && c.NewText is not null && c.Key31 is { } key
        && ChoiceClassifiers.Of(c.Migrated).Any(k => k.Key == EntryOf(key));

    /// <summary>
    /// #1508 (R13, R14): the prompt of an agent a macro-choice classifier stands before, at an instance where the classifier
    /// answered (its reply in <see cref="OfflineResult.Outputs"/>): where it answered yes, 31's prompt is 30's with the
    /// macro's 30 instruction (<c>Y</c>) in place of its 31 instruction (<c>I</c>), where <c>I</c> is <c>Y</c> or <c>Y</c>
    /// with one "otherwise" clause removed (<see cref="WithoutAnOtherwise"/>); where it answered no, 31's prompt is 30's with
    /// such a <c>Y</c> taken out (the macro was not offered). Undone, 31's text is 30's there; anything else is not this rule.
    /// </summary>
    private static (string Old, string New)? ChoiceInstruction(DifferenceContext c, string old, string now)
    {
        if (c.Kind != ComparedKind.Prompt || c.Key31 is not { } key)
            return null;
        var entry = EntryOf(key);
        foreach (var classifier in ChoiceClassifiers.Of(c.Migrated).Where(k => k.Placing == entry))
        {
            if (c.Run31.Outputs.GetValueOrDefault(classifier.Key + key[entry.Length..]) is not JsonObject reply
                || reply["answer"] is not JsonValue answer || !answer.TryGetValue<bool>(out var yes))
            {
                continue;
            }

            var instruction = classifier.Instruction;
            if (yes)
            {
                for (var at = now.IndexOf(instruction, StringComparison.Ordinal); at >= 0 && instruction.Length > 0; at = now.IndexOf(instruction, at + 1, StringComparison.Ordinal))
                {
                    var (before, after) = (now[..at], now[(at + instruction.Length)..]);
                    if (old.Length >= before.Length + after.Length && old.StartsWith(before, StringComparison.Ordinal) && old.EndsWith(after, StringComparison.Ordinal)
                        && Restores(old[before.Length..(old.Length - after.Length)], instruction))
                    {
                        return (old, before + old[before.Length..(old.Length - after.Length)] + after);
                    }
                }

                continue;
            }

            // Not offered: 30's text is 31's with Y inserted at some place p, which the common prefix and suffix bound.
            var extra = old.Length - now.Length;
            if (extra <= 0)
                continue;
            var prefix = 0;
            while (prefix < now.Length && old[prefix] == now[prefix])
                prefix++;
            var suffix = 0;
            while (suffix < now.Length && old[old.Length - 1 - suffix] == now[now.Length - 1 - suffix])
                suffix++;
            for (var p = Math.Max(0, now.Length - suffix); p <= prefix; p++)
            {
                if (Restores(old.Substring(p, extra), instruction))
                    return (old, now[..p] + old.Substring(p, extra) + now[p..]);
            }
        }

        return null;
    }

    /// <summary>Whether 31's <paramref name="instruction"/> is 30's <paramref name="written"/>, or it with one "otherwise" clause removed.</summary>
    private static bool Restores(string written, string instruction) =>
        written == instruction || WithoutAnOtherwise(written).Contains(instruction, StringComparer.Ordinal);

    /// <summary>
    /// <paramref name="text"/> with one "otherwise" clause removed, for each one it holds: <c>; otherwise …</c> or
    /// <c>, otherwise …</c> up to the next full stop (kept), or a sentence <c>Otherwise ….</c> with the space before it.
    /// </summary>
    private static IEnumerable<string> WithoutAnOtherwise(string text)
    {
        foreach (var word in new[] { "otherwise", "Otherwise" })
        {
            for (var at = text.IndexOf(word, StringComparison.Ordinal); at >= 0; at = text.IndexOf(word, at + 1, StringComparison.Ordinal))
            {
                var stop = text.IndexOf('.', at);
                if (word == "otherwise" && at >= 2 && text[at - 1] == ' ' && text[at - 2] is ';' or ',')
                    yield return text[..(at - 2)] + (stop < 0 ? "" : text[stop..]);
                else if (word == "Otherwise" && at >= 2 && text[at - 1] == ' ' && text[at - 2] == '.' && stop >= 0)
                    yield return text[..(at - 1)] + text[(stop + 1)..];
            }
        }
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
    /// Part 06 § 6.4, held to the text that came from a read (Task 15 fix round 1): each place in 31's deliverable text (or
    /// a prompt's or rendered node's that reads text escaped before it, <see cref="ReadsEscapedText"/>) that
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
    /// profile and the run facts it was given, in each reply it served, each rendered node's text and each computed node's
    /// text (#13); longest first, then ordinal, none empty.
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
        // #1508, 2026-11-26, item 3: a deliverable composes each source stripped (| string.strip), so a rendered or computed
        // node's text counts trimmed as well as whole.
        foreach (var render in c.Run31.Renders)
        {
            values.Add(render.Text);
            values.Add(render.Text.Trim());
        }

        // #1508 (comment 5, item 7, #13): a computed node's text (a let of text), as the run held it.
        foreach (var row in c.Run31.Rows.Where(r => r.Kind == PlanKind.Computed))
        {
            if (SourceLayout.Text(row.Text?["content"]) is { } content)
            {
                values.Add(content);
                values.Add(content.Trim());
            }
        }
        values.Remove("");
        return [.. values.OrderByDescending(v => v.Length).ThenBy(v => v, StringComparer.Ordinal)];
    }

    private static bool IsAsciiPunctuation(char c) =>
        c is >= '!' and <= '/' or >= ':' and <= '@' or >= '[' and <= '`' or >= '{' and <= '~';

    /// <summary>
    /// #1506 ruling 2: in a prompt or a rendered node's text whose nearest loop iterates an input's list, a number that is the
    /// item's number at 31 (<c>n + 1</c>) where 30's text has 30's (<c>n</c>), only where the entry's 31 body writes the
    /// item's id (<c>{{ item.id }}</c>): the changed number stands in 30's text between the body's own text before and after
    /// that tag on its line (<c>Prior letter </c> and <c>:</c>). The texts are read as runs of digits and of other characters,
    /// aligned (<see cref="Aligned"/>); only such a run is put back, every other run is left as 31 wrote it (R19).
    /// </summary>
    private static (string Old, string New)? ItemIds(DifferenceContext c, string old, string now)
    {
        if (c.Kind is not (ComparedKind.Prompt or ComparedKind.Render) || c.Key30 is not { } key30 || c.Key31 is not { } key31)
            return null;
        if (ItemPair(key30, key31, c) is not { } pair || ItemIdAnchors(c, key31) is not { Count: > 0 } anchors)
            return null;

        var a = DigitRuns(old);
        var b = DigitRuns(now);
        return Undo(old, now, a, b, (i, j) =>
        {
            var (digits, x) = a[i];
            var at = a.Take(i).Sum(r => r.Text.Length);
            return digits && (x, b[j].Text) == pair && anchors.Any(anchor =>
                old.AsSpan(0, at).EndsWith(anchor.Before, StringComparison.Ordinal)
                && old.AsSpan(at + x.Length).StartsWith(anchor.After, StringComparison.Ordinal));
        });
    }

    /// <summary>
    /// 31's text <paramref name="now"/> rebuilt from its runs <paramref name="b"/>, each run aligned with 30's run
    /// (<paramref name="a"/>, of <paramref name="old"/>) that differs from it and that <paramref name="undoes"/> accepts (the
    /// indexes of the two) written as 30's run; null when none is.
    /// </summary>
    private static (string Old, string New)? Undo(
        string old, string now, List<(bool Kind, string Text)> a, List<(bool Kind, string Text)> b, Func<int, int, bool> undoes)
    {
        if (Aligned([.. a.Select(r => r.Text)], [.. b.Select(r => r.Text)]) is not { } pairs)
            return null;
        var undone = new StringBuilder(now.Length);
        var changed = false;
        foreach (var (i, j) in pairs)
        {
            if (j < 0)
                continue;
            if (i >= 0 && a[i].Text != b[j].Text && a[i].Kind == b[j].Kind && undoes(i, j))
            {
                undone.Append(a[i].Text);
                changed = true;
            }
            else
            {
                undone.Append(b[j].Text);
            }
        }

        return changed ? (old, undone.ToString()) : null;
    }

    /// <summary>
    /// Part 04 § 2.7: in any compared text but the status, the heading <c>## &lt;loop label&gt; &lt;n&gt;</c> (<c>n</c>
    /// digits, ending its line; a template may place the loop's <c>.content</c> after text on its first line) that a loop over
    /// an input's list with <c>itemLabel: null</c> writes in its <c>.content</c>, every item of it named by its position, where
    /// the loop's label is not the input's, put back as 30 wrote it, <c>## &lt;input label&gt; &lt;n&gt;</c>. The position is
    /// the same at both (30's position names count from 1 too); nothing else on the line, and no other line, is touched.
    /// #1508 (comment 5, item 4): a loop with an <c>itemLabel</c> too, whose position name is the fallback for an item whose
    /// field is empty: an item named by its field has the same name at both, so where 30 wrote the input's label and a
    /// position, both sides fell back.
    /// </summary>
    private static (string Old, string New)? PositionNames(DifferenceContext c, string old, string now)
    {
        if (c.Kind == ComparedKind.Status)
            return null;

        var names = new List<(string Loop, string Input)>();
        foreach (var entry in c.Plan.Entries.Values.OrderBy(e => e.Order))
        {
            if (entry.Kind != PlanKind.For || SourceLayout.Text(entry.Json["in"]) is not { } source
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
    /// sets). #1508 (comment 5, item 7, #3): a <c>description</c> 31 sends where 30's format had none at that place is set
    /// aside (<see cref="WithoutNewDescriptions"/>); one that differs from 30's, and a title, <c>$schema</c> or <c>$id</c> 31
    /// sends and 30 did not, are differences no ruling declares, so the text stays unexpected.
    /// The 31 format is then put back as 30's, so the bytes rules after this one compare the rest.
    /// #1508 (R12; comment 5, item 7, #3): a classifier's prompt, which 30 ended with its values (<see cref="AnswersSent"/>).
    /// </summary>
    private static (string Old, string New)? SentFormat(DifferenceContext c, string old, string now)
    {
        if (c.Kind != ComparedKind.Prompt)
            return null;
        var marker = AgentTurn.CustomTrailer + "\n";
        var at30 = old.LastIndexOf(marker, StringComparison.Ordinal);
        var at31 = now.LastIndexOf(marker, StringComparison.Ordinal);
        if (at30 < 0 && at31 >= 0)
            return AnswersSent(old, now, at31, marker);
        if (at30 < 0 || at31 < 0)
            return null;
        var schema30 = old[(at30 + marker.Length)..];
        var schema31 = now[(at31 + marker.Length)..];
        if (StrictJson.Parse(schema30) is not { Fault: null, Value: JsonObject s30 } || StrictJson.Parse(schema31) is not { Fault: null, Value: JsonObject s31 })
            return null;
        var sent = s31.DeepClone();
        WithoutNewDescriptions(sent, s30);
        if (CanonicalJson.Write(Shape(As31(s30))) != CanonicalJson.Write(Shape(sent)))
            return null;
        return (old, now[..(at31 + marker.Length)] + schema30);
    }

    /// <summary>
    /// #1508 (R12; comment 5, item 7, #3): 30 sent no format to a classifier and ended its prompt with its values
    /// (<c>Answer with exactly one of: &lt;values&gt;.</c>, a last line the migration drops, #1508 item 7); 31 sends its answers' format, bound
    /// <c>custom</c>, after the custom trailer (<see cref="WorkflowCustomPrompt.Compose"/>). Explained when that format is
    /// exactly the answers' (<c>{ value: enum }</c>, closed, nothing else but a description, #3: no title or <c>$id</c>) and
    /// its values are the ones 30's last line lists, in order; 30's line is then taken off 30's text, and the trailer and format off 31's.
    /// </summary>
    private static (string Old, string New)? AnswersSent(string old, string now, int at31, string marker)
    {
        var joint = "\n\n";
        if (at31 < joint.Length || !now.AsSpan(at31 - joint.Length, joint.Length).SequenceEqual(joint))
            return null;
        if (StrictJson.Parse(now[(at31 + marker.Length)..]) is not { Fault: null, Value: JsonObject sent })
            return null;
        WithoutNewDescriptions(sent, null);
        if (sent["properties"] is not JsonObject { } properties || properties["value"] is not JsonObject { } value
            || value["enum"] is not JsonArray values || values.Any(v => SourceLayout.Text(v) is null))
            return null;
        var answers = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("value"),
            ["properties"] = new JsonObject { ["value"] = new JsonObject { ["type"] = "string", ["enum"] = values.DeepClone() } },
        };
        if (CanonicalJson.Write(answers) != CanonicalJson.Write(sent))
            return null;
        var listed = $"Answer with exactly one of: {string.Join(", ", values.Select(SourceLayout.Text))}.";
        var before = now[..(at31 - joint.Length)];
        var line = joint + listed;
        // 31 composes the trailer after the rendered prompt with its end trimmed (WorkflowCustomPrompt.Compose), so 30's text
        // before its line is read with its end trimmed too: the prompt's own last line ending is not a difference.
        return old.EndsWith(line, StringComparison.Ordinal) ? (old[..^line.Length].TrimEnd(), before) : null;
    }

    /// <summary>
    /// #1508 (comment 5, item 7, #3): <paramref name="sent"/> (31's format) with each <c>description</c> taken out where
    /// <paramref name="written"/> (30's, read at the same place: a property by its name, <c>items</c>, an arm by its index, a
    /// definition by its name) has none; only schema positions are read, never a property named <c>description</c>.
    /// </summary>
    private static void WithoutNewDescriptions(JsonNode? sent, JsonNode? written)
    {
        if (sent is not JsonObject schema)
            return;
        var at30 = written as JsonObject;
        if (schema.ContainsKey("description") && at30?.ContainsKey("description") != true)
            schema.Remove("description");
        foreach (var named in new[] { "properties", "$defs" })
        {
            foreach (var (name, member) in schema[named] as JsonObject ?? [])
                WithoutNewDescriptions(member, (at30?[named] as JsonObject)?[name]);
        }

        WithoutNewDescriptions(schema["items"], at30?["items"]);
        foreach (var keyword in new[] { "anyOf", "oneOf", "allOf" })
        {
            var arms30 = at30?[keyword] as JsonArray;
            var arms = schema[keyword] as JsonArray ?? [];
            for (var i = 0; i < arms.Count; i++)
                WithoutNewDescriptions(arms[i], arms30 is not null && i < arms30.Count ? arms30[i] : null);
        }
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
    /// nothing). The runs are aligned (<see cref="Aligned"/>); every other run is left as 31 wrote it (R19). A snippet written from a macro (D4) is not a macro
    /// body at 31: the migration writes it with 30's trimming, so it is never explained here. Only a macro the compared row's
    /// text can hold counts (<see cref="PlacedMacros"/>; final review B4).
    /// </summary>
    private static (string Old, string New)? MacroTrailingWhitespace(DifferenceContext c, string old, string now)
    {
        var placed = PlacedMacros(c);
        var kept = KeptTrailingWhitespace(c.Migrated).Where(k => placed.Contains(k.Site)).ToList();
        if (kept.Count == 0)
            return null;
        var a = WhiteSpaceRuns(old);
        var b = WhiteSpaceRuns(now);
        return Undo(old, now, a, b, (i, j) =>
            a[i].Space && i > 0 && kept.Any(k => b[j].Text == k.Trailing + a[i].Text && a[i - 1].Text.EndsWith(k.LastWord, StringComparison.Ordinal)));
    }

    /// <summary>
    /// Each macro body's trailing white space that 31 keeps and 30's <c>TrimEnd()</c> took away (none empty), with the body's
    /// last word, the text it follows.
    /// </summary>
    private static List<(string Trailing, string LastWord, string Site)> KeptTrailingWhitespace(Migration31Result migrated)
    {
        var kept = new List<(string, string, string)>();
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
                kept.Add((trailing, word, "macro." + path["macros/".Length..^".md".Length].Replace('/', '.')));
        }

        return kept;
    }

    /// <summary>
    /// #1508, 2026-11-26, item 5 (no migration change): a macro the model chose (<see cref="ChoiceClassifiers"/>, R13) whose
    /// body ends in a line ending stands, at 31, where the reply's marker stood with that one line ending dropped (R5, part
    /// 05 § 8.6), where 30 wrote the body whole. Only such a macro the compared row's text can hold counts
    /// (<see cref="PlacedMacros"/>), whose file is a 30 macro's body (<c>macros/**</c> in
    /// <see cref="Migration31Result.MacroBodies"/>). A white-space run of 31's text that follows the body's last word (its
    /// text after its last white space; a body that ends in a tag has no word to find) and is 30's aligned run less that one
    /// line ending is written as 30's run (<see cref="Aligned"/>); every other run is left as 31 wrote it (R19).
    /// </summary>
    private static (string Old, string New)? ChosenMacroLineEnding(DifferenceContext c, string old, string now)
    {
        if (c.Kind == ComparedKind.Status)
            return null;
        var placed = PlacedMacros(c);
        var bodies = new List<(string Kept, string Ending, string LastWord)>();
        foreach (var site in ChoiceClassifiers.Of(c.Migrated).Select(k => "macro." + k.Macro).Distinct(StringComparer.Ordinal).Where(placed.Contains))
        {
            if (!c.Migrated.MacroBodies.Contains("macros/" + site["macro.".Length..].Replace('.', '/') + ".md")
                || MacroFile(c.Migrated, site)?.Body is not { } body || !body.EndsWith('\n'))
                continue;
            var ending = body.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var rendered = body[..^ending.Length];
            var text = rendered.TrimEnd();
            var word = text[(text.LastIndexOfAny([' ', '\t', '\n', '\r']) + 1)..];
            if (word.Length > 0 && !word.EndsWith("}}", StringComparison.Ordinal))
                bodies.Add((rendered[text.Length..], ending, word));
        }

        if (bodies.Count == 0)
            return null;
        var a = WhiteSpaceRuns(old);
        var b = WhiteSpaceRuns(now);
        return Undo(old, now, a, b, (i, j) =>
            b[j].Space && j > 0 && bodies.Any(k => b[j - 1].Text.EndsWith(k.LastWord, StringComparison.Ordinal)
                && b[j].Text.StartsWith(k.Kept, StringComparison.Ordinal)
                && a[i].Text == k.Kept + k.Ending + b[j].Text[k.Kept.Length..]));
    }

    /// <summary>
    /// #1508, 2026-11-26, item 5 (no migration change): a 30 macro written as a snippet (D4; <c>snippets/**</c> in
    /// <see cref="Migration31Result.MacroBodies"/>) that the compared row places, itself or through a rendered node it reads,
    /// with a line that ends in literal text, white space and one read tag (<c>Seen at: {{ input.clinic.content }}</c>):
    /// where the read rendered empty, 31's snippet keeps the white space before it, where 30 trimmed the rendered macro. In
    /// 31's text, that white space (spaces or tabs alone) after the line's literal text and just before a line ending or the
    /// text's end, absent at the aligned place of 30's (<see cref="Aligned"/>), is taken out; every other run is left as 31
    /// wrote it (R19).
    /// </summary>
    private static (string Old, string New)? EmptyReadTrailingSpace(DifferenceContext c, string old, string now)
    {
        if (c.Kind == ComparedKind.Status || EmptyReadLines(c) is not { Count: > 0 } lines)
            return null;

        static bool After(string text, (string Literal, string Space) line) => text.EndsWith(line.Literal, StringComparison.Ordinal);

        var undone = now;
        var a = WhiteSpaceRuns(old);
        var b = WhiteSpaceRuns(now);
        if (Undo(old, now, a, b, (i, j) => b[j].Space && j > 0 && lines.Any(line =>
                b[j].Text.StartsWith(line.Space, StringComparison.Ordinal) && b[j].Text[line.Space.Length..] is var rest
                && rest.Length > 0 && rest[0] is '\n' or '\r' && a[i].Text == rest
                && After(now[..b.Take(j).Sum(r => r.Text.Length)], line))) is { } inside)
            undone = inside.New;

        // At the text's end 30 has no run to align with.
        foreach (var line in lines)
        {
            if (undone.EndsWith(line.Space, StringComparison.Ordinal) && undone[..^line.Space.Length] is var before
                && After(before, line) && !old.EndsWith(line.Space, StringComparison.Ordinal))
            {
                undone = Nearest(old, undone, before);
                break;
            }
        }

        return undone == now ? null : (old, undone);
    }

    /// <summary>
    /// The lines of the 30 macros written as snippets that the compared row places (its own placements, nested ones
    /// included, and those of each rendered node it reads, in turn) which end in literal text, white space and one read
    /// tag: each line's literal text, trimmed, and its white space before the tag. None with no literal text.
    /// </summary>
    private static List<(string Literal, string Space)> EmptyReadLines(DifferenceContext c)
    {
        var lines = new List<(string, string)>();
        if (c.Key31 is not { } key || !c.Plan.Entries.TryGetValue(EntryOf(key), out var row))
            return lines;

        var sites = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal) { row.Key };
        var pending = new Queue<PlanEntry>([row]);
        while (pending.Count > 0)
        {
            var entry = pending.Dequeue();
            sites.UnionWith(entry.Placements.Select(p => p.Site).Where(s => s.StartsWith("snippet.", StringComparison.Ordinal)));
            foreach (var path in entry.Reads)
            {
                var named = c.Plan.Entries.Keys.Where(k => path == k || path.StartsWith(k + ".", StringComparison.Ordinal)).MaxBy(k => k.Length);
                if (named is not null && c.Plan.Entries[named].Kind == PlanKind.Rendered && seen.Add(named))
                    pending.Enqueue(c.Plan.Entries[named]);
            }
        }

        foreach (var site in sites.Order(StringComparer.Ordinal))
        {
            var path = "snippets/" + site["snippet.".Length..].Replace('.', '/') + ".md";
            if (!c.Migrated.MacroBodies.Contains(path) || !c.Migrated.Files.TryGetValue(path, out var file) || FrontMatter.Split(file)?.Body is not { } body)
                continue;
            foreach (var raw in body.Split('\n'))
            {
                var line = raw.TrimEnd('\r').TrimEnd(' ', '\t');
                var tags = Tag.Matches(line);
                if (tags.Count != 1 || tags[0].Index + tags[0].Length != line.Length)
                    continue;
                var before = line[..tags[0].Index];
                var literal = before.TrimEnd(' ', '\t');
                if (literal.Trim().Length > 0 && literal.Length < before.Length)
                    lines.Add((literal.Trim(), before[literal.Length..]));
            }
        }

        return lines;
    }

    /// <summary>
    /// Final review B4: the macros (<c>macro.&lt;id&gt;</c>, a folder's <c>macro.&lt;folder&gt;.&lt;item&gt;</c>) whose body
    /// the compared row's 31 text can hold, from the plan's reads and placements: each entry the row reads (the longest
    /// entry key a read names), a flow with every entry inside it, a rendered node with what it reads in turn; an agent
    /// reached so holds the bodies of the macros it places (its reply's markers are replaced by them). A prompt's own
    /// placements are its instructions, never a body, so the row's own are not counted. Empty for the status or a key
    /// the plan has no entry for.
    /// </summary>
    private static HashSet<string> PlacedMacros(DifferenceContext c)
    {
        var placed = new HashSet<string>(StringComparer.Ordinal);
        if (c.Key31 is not { } key || !c.Plan.Entries.TryGetValue(EntryOf(key), out var row))
            return placed;
        var seen = new HashSet<string>(StringComparer.Ordinal) { row.Key };
        var pending = new Queue<PlanEntry>();

        void Read(PlanEntry reader)
        {
            foreach (var path in reader.Reads)
            {
                var named = c.Plan.Entries.Keys.Where(k => path == k || path.StartsWith(k + ".", StringComparison.Ordinal)).MaxBy(k => k.Length);
                if (named is not null && seen.Add(named))
                    pending.Enqueue(c.Plan.Entries[named]);
            }
        }

        Read(row);
        while (pending.Count > 0)
        {
            var entry = pending.Dequeue();
            switch (entry.Kind)
            {
                case PlanKind.Agent:
                    placed.UnionWith(entry.Placements.Select(p => p.Site).Where(s => s.StartsWith("macro.", StringComparison.Ordinal)));
                    break;
                case PlanKind.Rendered:
                    Read(entry);
                    break;
                case PlanKind.For or PlanKind.While or PlanKind.If or PlanKind.Case:
                    foreach (var inner in c.Plan.Entries.Values.Where(e => e.Key.StartsWith(entry.Key + ".", StringComparison.Ordinal)))
                    {
                        if (seen.Add(inner.Key))
                            pending.Enqueue(inner);
                    }

                    break;
            }
        }

        return placed;
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
    /// A10-008 notice in <see cref="MigrationNotes.BlankConversions"/>; A10-050's <c>trim(x) == ""</c>, expected as a condition reason (#1508), never
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

    /// <summary>
    /// Whether a gate decided for row <paramref name="owner"/> gates the row <paramref name="key"/>: it is that row's entry,
    /// or a flow around it, and the same instance (final review B2): the owner's pass, when it has one, is the row's, and
    /// the owner's item ids are the first of the row's (part 08 § 1.1's row key: <c>#&lt;n&gt;</c> a pass,
    /// <c>:&lt;id&gt;/&lt;id&gt;</c> the items, outermost first). A gate outside every loop gates each instance inside it.
    /// </summary>
    private static bool Gates(string owner, string key)
    {
        var (entry, ownerEntry) = (EntryOf(key), EntryOf(owner));
        if (entry != ownerEntry && !entry.StartsWith(ownerEntry + ".", StringComparison.Ordinal))
            return false;
        var (ownerPass, ownerItems) = InstanceOf(owner);
        var (pass, items) = InstanceOf(key);
        return (ownerPass is null || ownerPass == pass)
            && ownerItems.Length <= items.Length && ownerItems.SequenceEqual(items.Take(ownerItems.Length), StringComparer.Ordinal);
    }

    /// <summary>A row key's pass (after <c>#</c>, up to <c>:</c>) and item ids (after <c>:</c>, split at <c>/</c>); none when it has none.</summary>
    private static (string? Pass, string[] Items) InstanceOf(string rowKey)
    {
        var colon = rowKey.IndexOf(':', StringComparison.Ordinal);
        var hash = rowKey.IndexOf('#', StringComparison.Ordinal);
        string? pass = hash < 0 || (colon >= 0 && colon < hash) ? null : rowKey[(hash + 1)..(colon < 0 ? rowKey.Length : colon)];
        var items = colon < 0 ? [] : rowKey[(colon + 1)..].Split('/');
        return (pass, items);
    }

    /// <summary>
    /// Whether the compared row reads a text the escape is written into before the row reads it: a deliverable's (part 06
    /// § 6.7 lets a node a check reads read one), or a loop's <c>.content</c>, whose plain-text items are escaped where they
    /// are joined (D23, signed off by the owner on #1508, comment 5, item 8).
    /// </summary>
    private static bool ReadsEscapedText(DifferenceContext c) =>
        RowReads(c).Any(r => r.StartsWith("deliverable.", StringComparison.Ordinal)
            || (r.EndsWith(".content", StringComparison.Ordinal) && c.Plan.Entries.TryGetValue(r[..^".content".Length], out var read)
                && read.Kind is PlanKind.For or PlanKind.While));

    // ---- #1508, comment 5, item 7 (ruled by the owner on 2026-11-25), and the controller's ruling on Task 33 ------------

    /// <summary>
    /// #9: a text only one side has, in a job that failed at both because the same reply was refused at both: every agent row
    /// 31 failed is a reply the capture holds under its 30 key, refused alike: by its node's <c>failIfEmpty</c>, the reply
    /// blank (<see cref="EmptyRefusedAlike"/>), or by its node's format under <c>onInvalid: fail</c> (#1508, comment 6, item 3:
    /// <see cref="FormatRefusedAlike"/>). Each engine dispatched in its own order, so each sent a text the other's failure came
    /// before. A text both sides have, or the status, is never explained here.
    /// </summary>
    private static bool FailedAlike(DifferenceContext c)
    {
        if (c.Kind == ComparedKind.Status || (c.OldText is null) == (c.NewText is null)
            || SourceLayout.Text(c.Capture30["status"]) != nameof(RunState.Failed) || c.Run31.State != RunState.Failed)
            return false;
        var outputs = c.Capture30["outputs"] as JsonObject;
        var failure = SourceLayout.Text(c.Capture30["failure"]);
        var failed = c.Run31.Rows.Where(r => r.State == RowState.Failed).ToList();
        return failed.Count > 0 && failed.All(r => r.Kind == PlanKind.Agent
            && KeyMap.To30(r.Key, c.Migrated, c.Plan) is { } key30 && outputs?.TryGetPropertyValue(key30, out var reply) == true
            && (EmptyRefusedAlike(c, r, reply) || FormatRefusedAlike(r, key30, failure)));
    }

    /// <summary>#9: the node's <c>failIfEmpty</c> refused it at 31, and 30's reply was empty or white space only.</summary>
    private static bool EmptyRefusedAlike(DifferenceContext c, RunRow row, JsonNode? reply) =>
        c.Plan.Entries.TryGetValue(row.EntryKey, out var entry) && SourceLayout.Text(entry.Json["failIfEmpty"]) is { } sentence && row.Reason == sentence
        && SourceLayout.Text(reply) is { } text && text.EnumerateRunes().All(Rune.IsWhiteSpace);

    /// <summary>
    /// #9, #1508 (comment 5, item 1; comment 6, item 3): the node's format refused its reply at 31 under <c>onInvalid: fail</c>
    /// (the row's <c>invalid</c> action), and 30's job failed with that node's own format sentence
    /// (<c>Node '&lt;id&gt;' reply does not conform to format</c>): an answer outside a classifier's values, say.
    /// </summary>
    private static bool FormatRefusedAlike(RunRow row, string key30, string? failure) =>
        SourceLayout.Text(row.Detail["invalid"]?["action"]) == "fail" && failure is not null
        && SourceLayout.Text(row.Detail["invalid"]?["reason"]) is { } reason && reason.Contains(FormatRefusal, StringComparison.Ordinal)
        && failure.Contains($"Node '{NodeOf30(key30)}' reply does not conform to format", StringComparison.Ordinal);

    /// <summary>What 31's runner says of a reply its format refused (<c>Node '&lt;id&gt;' gave a reply that isn't &lt;format&gt;</c>), never a marker fault.</summary>
    private const string FormatRefusal = "' gave a reply that isn't ";

    /// <summary>The text <c>AgentTurn</c> appends, after two newlines, to the prompt of a call that asks again after a reply that did not fit its format.</summary>
    private const string RetryAsk = "\n\nYour last reply could not be used: it did not fit the required format. Write the reply again.";

    /// <summary>A 30 instance key's node id: the key before its first <c>:</c>.</summary>
    private static string NodeOf30(string key30) => key30.IndexOf(':', StringComparison.Ordinal) is var colon and >= 0 ? key30[..colon] : key30;

    /// <summary>
    /// #1508 (comment 5, item 3; comment 6, item 3): 30 completed and 31 failed, at an agent row that asked again
    /// under <c>onInvalid: retry</c> (its row's <c>invalid</c>: action <c>retry</c>, two attempts) and failed, whose 30 node the
    /// migration turned from <c>onInvalid: flag</c> (<see cref="MigrationSentences.FlagBecomesRetry"/> among its notices): the
    /// status and each text only 30 has; the retried prompt's own retry text is <see cref="RetryText"/>'s.
    /// </summary>
    private static bool FlagRetried(DifferenceContext c)
    {
        var retried = FlagRetriedRows(c);
        return retried.Count > 0
            && (IsStatus(c, nameof(RunState.Completed), nameof(RunState.Failed))
                || (c.Kind != ComparedKind.Status && c.OldText is not null && c.NewText is null));
    }

    /// <summary>
    /// #1508 (R19): the retried row's own prompt (<see cref="FlagRetried"/>'s rows) with the retry's text taken off its end, and
    /// the trailing white space <c>AgentTurn</c> trimmed before it put back as 30's text has it; every other difference is
    /// left for the rules after this one (31 sends the format's description, say).
    /// </summary>
    private static (string Old, string New)? RetryText(DifferenceContext c, string old, string now)
    {
        if (c.Kind != ComparedKind.Prompt || !now.EndsWith(RetryAsk, StringComparison.Ordinal) || !FlagRetriedRows(c).Any(r => r.Key == c.Key31))
            return null;
        return (old, now[..^RetryAsk.Length] + old[old.TrimEnd().Length..]);
    }

    /// <summary>
    /// The agent rows of a job 30 completed and 31 failed that asked again under <c>onInvalid: retry</c> (the row's
    /// <c>invalid</c>: action <c>retry</c>, two attempts) and failed, each a node the migration turned from <c>onInvalid: flag</c>.
    /// </summary>
    private static List<RunRow> FlagRetriedRows(DifferenceContext c) =>
        SourceLayout.Text(c.Capture30["status"]) != nameof(RunState.Completed) || c.Run31.State != RunState.Failed
            ? []
            : [.. c.Run31.Rows.Where(r => r.State == RowState.Failed && r.Kind == PlanKind.Agent
                && SourceLayout.Text(r.Detail["invalid"]?["action"]) == "retry" && (int?)(r.Detail["invalid"]?["attempts"] as JsonValue) == 2
                && KeyMap.To30(r.Key, c.Migrated, c.Plan) is { } key30
                && c.Migrated.Notices.Contains(MigrationSentences.FlagBecomesRetry(NodeOf30(key30)), StringComparer.Ordinal))];

    /// <summary>
    /// #5: 30 failed the job because a node's reply did not render as a template (<c>Node '&lt;id&gt;' reply failed to
    /// render</c>), and at 31, where a reply is never a template, that node completed and so did the job: the status, and each
    /// deliverable only 31 composed (30 left it unsettled at the failure).
    /// </summary>
    private static bool ReplyNotRendered(DifferenceContext c)
    {
        const string Head = "Node '";
        const string Tail = "' reply failed to render";
        if (SourceLayout.Text(c.Capture30["status"]) != nameof(RunState.Failed) || c.Run31.State != RunState.Completed
            || SourceLayout.Text(c.Capture30["failure"]) is not { } failure)
            return false;
        var end = failure.IndexOf(Tail, StringComparison.Ordinal);
        var at = end < 0 ? -1 : failure.LastIndexOf(Head, end, StringComparison.Ordinal);
        if (at < 0 || c.Migrated.Keys.GetValueOrDefault(failure[(at + Head.Length)..end]) is not { } key)
            return false;
        var rows = c.Run31.Rows.Where(r => r.EntryKey == key).ToList();
        return rows.Count > 0 && rows.All(r => r.State == RowState.Completed)
            && (IsStatus(c, nameof(RunState.Failed), nameof(RunState.Completed)) || (c.Kind == ComparedKind.Deliverable && c.OldText is null && c.NewText is not null));
    }

    /// <summary>
    /// #5: each text reply the 31 run served that holds <c>{{</c> stands in 31's text as written, where 30's holds it as 30
    /// rendered it (<see cref="Rendered30"/>, with no variable: a reply that reads one did not render at 30, which
    /// <see cref="ReplyNotRendered"/> covers). Each place is put back as 30's: the reply whole, and the reply stripped
    /// as a migrated deliverable reads a source (<c>| string.strip</c>, #1508, 2026-11-26, item 3) by 30's render stripped.
    /// </summary>
    private static (string Old, string New)? RenderedReplies(DifferenceContext c, string old, string now)
    {
        if (c.Kind == ComparedKind.Status)
            return null;
        var undone = now;
        foreach (var reply in c.Run31.Outputs.Values.Select(v => v is JsonValue value && value.TryGetValue<string>(out var text) ? text : null)
                     .OfType<string>().Where(r => r.Contains("{{", StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).OrderByDescending(r => r.Length))
        {
            if (Rendered30(reply) is not { } rendered || rendered == reply)
                continue;
            undone = undone.Replace(reply, rendered, StringComparison.Ordinal);
            if (reply.Trim() is { Length: > 0 } strippedReply && strippedReply != reply)
                undone = undone.Replace(strippedReply, rendered.Trim(), StringComparison.Ordinal);
        }

        return undone == now ? null : (old, undone);
    }

    /// <summary>
    /// <paramref name="reply"/> rendered as 30's engine rendered a reply: as a Scriban template in 30's strict context
    /// (<see cref="PromptTemplateRenderer.RenderingContext"/>), with no variable; null when it does not parse or render.
    /// </summary>
    internal static string? Rendered30(string reply)
    {
        var template = Scriban.Template.Parse(reply);
        if (template.HasErrors)
            return null;
        try
        {
            return template.Render(new PromptTemplateRenderer.RenderingContext());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>
    /// #6: each text value of an input (a string at any depth of the request's inputs) that ends in white space stands in
    /// 31's text whole, where 30's holds it with its trailing white space trimmed: each place put back as 30's, where that
    /// brings the texts nearer (a value of white space alone is the empty text at 30, so only the places 30 trimmed count).
    /// With no request, nothing is explained.
    /// </summary>
    private static (string Old, string New)? InputTrailingWhitespace(DifferenceContext c, string old, string now)
    {
        if (c.Kind == ComparedKind.Status || c.Request is not { } request)
            return null;
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
                case JsonValue value when value.TryGetValue<string>(out var text) && text.Length > 0 && text.TrimEnd().Length < text.Length:
                    values.Add(text);
                    break;
            }
        }

        foreach (var (_, input) in request.Inputs)
            Leaves(input);
        var undone = now;
        foreach (var value in values.OrderByDescending(v => v.Length).ThenBy(v => v, StringComparer.Ordinal))
            undone = EachNearer(old, undone, value, value.TrimEnd());
        return undone == now ? null : (old, undone);
    }

    /// <summary>
    /// <paramref name="now"/> with each place of <paramref name="find"/> written as <paramref name="replace"/>, one at a time
    /// from the start, each kept only where it brings 31's text nearer <paramref name="old"/> (<see cref="Distance"/>).
    /// </summary>
    private static string EachNearer(string old, string now, string find, string replace)
    {
        var distance = Distance(old, now);
        for (var at = now.IndexOf(find, StringComparison.Ordinal); at >= 0 && distance > 0; at = now.IndexOf(find, at, StringComparison.Ordinal))
        {
            var candidate = now[..at] + replace + now[(at + find.Length)..];
            var nearer = Distance(old, candidate);
            if (nearer < distance)
            {
                (now, distance) = (candidate, nearer);
                at += replace.Length;
            }
            else
            {
                at++;
            }

            if (at > now.Length)
                break;
        }

        return now;
    }

    /// <summary>
    /// #10: the macros 30's engine placed before or after a node's reply (<see cref="ByteEquivalence.EnginePlaced"/>) are
    /// markers at 31. In the prompt of an agent that places one, its instruction (the hand edit's, its lines joined with
    /// <c>\n</c>) stands where 30's prompt had nothing: each is taken out, and the white space the placements left at the
    /// prompt's end (the migration placed them there, D3) is 30's. In a text that can hold such a body
    /// (<see cref="PlacedMacros"/>), each white space run where the texts differ that stands between the body and the
    /// reply (after the last word of a body placed before, before the first word of one placed after: a word that is a
    /// tag's is not read) is 30's, the blank line 30's engine joined them by; every other run is left as 31 wrote it.
    /// </summary>
    private static (string Old, string New)? BeforeAfterMarker(DifferenceContext c, string old, string now)
    {
        if (c.Kind == ComparedKind.Status || c.Key31 is not { } key || !c.Plan.Entries.TryGetValue(EntryOf(key), out var row))
            return null;
        var placed = ByteEquivalence.EnginePlaced(c.Migrated);
        if (placed.Count == 0)
            return null;

        var undone = now;
        if (c.Kind == ComparedKind.Prompt && row.Placements.Where(p => placed.ContainsKey(p.Site)).Select(p => p.Site).Distinct(StringComparer.Ordinal).ToList() is { Count: > 0 } own)
        {
            foreach (var site in own)
            {
                if (MacroHeader(c.Migrated, site) is { } header
                    && string.Join("\n", (header["instruction"] as JsonArray ?? []).Select(l => SourceLayout.Text(l) ?? "")) is { Length: > 0 } instruction)
                    undone = undone.Replace(instruction, "", StringComparison.Ordinal);
            }

            undone = undone.TrimEnd() + old[old.TrimEnd().Length..];
        }

        var words = new List<(string? First, string? Last)>();
        foreach (var site in PlacedMacros(c).Where(placed.ContainsKey).Order(StringComparer.Ordinal))
        {
            if (MacroBody(c.Migrated, site)?.Trim() is not { Length: > 0 } body)
                continue;
            var first = body[..(body.IndexOfAny([' ', '\t', '\n', '\r']) is var space and >= 0 ? space : body.Length)];
            var last = body[(body.LastIndexOfAny([' ', '\t', '\n', '\r']) + 1)..];
            words.Add(placed[site]
                ? (null, last.Contains("}}", StringComparison.Ordinal) ? null : last)
                : (first.Contains("{{", StringComparison.Ordinal) ? null : first, null));
        }

        if (words.Count > 0)
        {
            var a = WhiteSpaceRuns(old);
            var b = WhiteSpaceRuns(undone);
            if (Undo(old, undone, a, b, (i, j) => a[i].Space && words.Any(w =>
                    (w.Last is { } last && i > 0 && a[i - 1].Text.EndsWith(last, StringComparison.Ordinal))
                    || (w.First is { } first && i + 1 < a.Count && a[i + 1].Text.StartsWith(first, StringComparison.Ordinal)))) is { } around)
                undone = around.New;
        }

        return undone == now ? null : (old, undone);
    }

    /// <summary>The source file of the macro at <paramref name="site"/> (<c>macro.&lt;id&gt;</c>, <c>macro.&lt;folder&gt;.&lt;item&gt;</c>), split, or null.</summary>
    private static (string Header, string Body)? MacroFile(Migration31Result migrated, string site) =>
        site.StartsWith("macro.", StringComparison.Ordinal)
        && migrated.Files.TryGetValue("macros/" + site["macro.".Length..].Replace('.', '/') + ".md", out var file)
        && FrontMatter.Split(file) is { } split ? (split.Header, split.Body) : null;

    private static JsonObject? MacroHeader(Migration31Result migrated, string site)
    {
        if (MacroFile(migrated, site) is not { } file)
            return null;
        try
        {
            return JsonNode.Parse(file.Header) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>The body of the macro at <paramref name="site"/>, its one final line ending dropped as 31 renders it (R5), or null.</summary>
    private static string? MacroBody(Migration31Result migrated, string site) =>
        MacroFile(migrated, site)?.Body is { } body ? (body.EndsWith('\n') ? body[..^1] : body) : null;

    /// <summary>
    /// The controller's ruling on #1508 (Task 33's review): a deliverable whose template reads a <c>while</c> that ran no pass
    /// (its row's <c>passes</c> 0) as <c>{{ node.&lt;while&gt;.content }}</c> (stripped or not), with white space alone between that tag and the
    /// next or the one before (the separator): the while composes as the empty text at both, and 30's composer dropped its
    /// part with its separator, where 31's template keeps it. 31's text with that separator taken out where the tag stood (at
    /// the start, at the end, or one of two separators side by side between) is 30's, where that brings the texts nearer.
    /// </summary>
    private static (string Old, string New)? WhileNoPassSeparator(DifferenceContext c, string old, string now)
    {
        if (c.Kind != ComparedKind.Deliverable || c.Key31 is not { } key || !key.StartsWith("deliverable.", StringComparison.Ordinal)
            || !c.Migrated.Files.TryGetValue("deliverables/" + key["deliverable.".Length..].Replace('.', '/') + ".md", out var file)
            || FrontMatter.Split(file)?.Body is not { } body)
            return null;
        if (body.EndsWith('\n'))
            body = body[..^1];

        var undone = now;
        foreach (var loop in c.Run31.Rows.Where(r => r.Kind == PlanKind.While && r.Key == r.EntryKey
                     && r.Detail["passes"] is JsonValue passes && passes.TryGetValue<int>(out var count) && count == 0))
        {
            // The tag as the migration writes a source (#1508 item 3: stripped) or as a hand-written template reads it.
            foreach (var tag in new[] { $"{{{{ {loop.Key}.content | string.strip }}}}", $"{{{{ {loop.Key}.content }}}}" })
            {
                for (var at = body.IndexOf(tag, StringComparison.Ordinal); at >= 0; at = body.IndexOf(tag, at + tag.Length, StringComparison.Ordinal))
                {
                    var open = body.LastIndexOf("}}", at, StringComparison.Ordinal);
                    var before = body[(open < 0 ? 0 : open + 2)..at];
                    var next = body.IndexOf("{{", at + tag.Length, StringComparison.Ordinal);
                    var after = body[(at + tag.Length)..(next < 0 ? body.Length : next)];
                    bool Separator(string text) => text.Length > 0 && text.All(char.IsWhiteSpace);
                    if (open < 0 && before.Length == 0 && Separator(after) && undone.StartsWith(after, StringComparison.Ordinal))
                        undone = Nearest(old, undone, undone[after.Length..]);
                    else if (next < 0 && after.Length == 0 && Separator(before) && undone.EndsWith(before, StringComparison.Ordinal))
                        undone = Nearest(old, undone, undone[..^before.Length]);
                    else if (Separator(before) && Separator(after))
                        undone = EachNearer(old, undone, before + after, after);
                }
            }
        }

        return undone == now ? null : (old, undone);
    }

    /// <summary>
    /// R16's guard as the migration writes it on a 30 template loop over a loop node that needs <c>.items</c>
    /// (<c>TemplateRewriter</c>): the <c>for</c> tag (30's, with <c>.items</c>), the state test, the body, the empty
    /// <c>else</c>, and the loop's <c>end</c>.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex GuardedLoop = new(
        @"\{\{[~-]?\s*for\s+(?<v>\w+)\s+in\s+(?<loop>[\w.]+)\.items\b[^}]*\}\}\{\{ if \k<v>\.state == ""completed"" \}\}(?<body>.*?)\{\{ else \}\}\{\{ end \}\}\{\{[~-]?\s*end\s*[~-]?\}\}",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Singleline);

    private static readonly System.Text.RegularExpressions.Regex Tag =
        new(@"\{\{.*?\}\}", System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Singleline);

    /// <summary>
    /// #1508, 2026-11-26, item 4 (Q4 stands): in a prompt, a rendered node's text or a deliverable's, an item of a loop that
    /// did not run writes nothing at 31 (R16's guard), where 30's template loop wrote its fixed text with its reads empty.
    /// The templates the row is made from (its own source file and those of the rendered nodes it reads, in turn) are read
    /// for R16's guard (<see cref="GuardedLoop"/>), each with its body's literal fragments (the text outside its tags, each
    /// trimmed, empty ones dropped) and the number of the loop's items in the 31 run that did not complete. 30's and 31's
    /// lines are aligned (<see cref="Aligned"/>); each run of lines only 30 has that holds a guard's fragments in order, at
    /// most as many per loop as its items that did not complete, is taken out of 30's text with the line ending that joins
    /// it (the one before it; the one after, for a run at the start). Null when none is taken.
    /// </summary>
    private static (string Old, string New)? LoopItemNotRun(DifferenceContext c, string old, string now)
    {
        if (c.Kind is not (ComparedKind.Prompt or ComparedKind.Render or ComparedKind.Deliverable) || c.Key31 is not { } key
            || !c.Plan.Entries.TryGetValue(EntryOf(key), out var row))
            return null;

        var guards = new List<(string Loop, List<string> Fragments)>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { row.Key };
        var pending = new Queue<PlanEntry>([row]);
        while (pending.Count > 0)
        {
            var entry = pending.Dequeue();
            if (TemplateOf(c.Migrated, entry.Key) is { } template)
            {
                foreach (System.Text.RegularExpressions.Match guard in GuardedLoop.Matches(template))
                {
                    var fragments = Tag.Split(guard.Groups["body"].Value).Select(f => f.Trim()).Where(f => f.Length > 0).ToList();
                    if (fragments.Count > 0)
                        guards.Add((guard.Groups["loop"].Value, fragments));
                }
            }

            foreach (var path in entry.Reads)
            {
                var named = c.Plan.Entries.Keys.Where(k => path == k || path.StartsWith(k + ".", StringComparison.Ordinal)).MaxBy(k => k.Length);
                if (named is not null && c.Plan.Entries[named].Kind == PlanKind.Rendered && seen.Add(named))
                    pending.Enqueue(c.Plan.Entries[named]);
            }
        }

        var left = guards.Select(g => g.Loop).Distinct(StringComparer.Ordinal).ToDictionary(
            loop => loop,
            loop => c.Run31.Rows.Count(r => r.EntryKey == loop && r.Key.StartsWith(loop + ":", StringComparison.Ordinal) && r.State != RowState.Completed),
            StringComparer.Ordinal);
        if (left.Values.All(n => n == 0))
            return null;

        var a = old.Split('\n');
        var b = now.Split('\n');
        if (Aligned(a, b) is not { } pairs)
            return null;

        var taken = new HashSet<int>();
        for (var p = 0; p < pairs.Count; p++)
        {
            if (pairs[p].B >= 0)
                continue;
            var start = p;
            while (p + 1 < pairs.Count && pairs[p + 1].B < 0)
                p++;
            var lines = pairs.Skip(start).Take(p - start + 1).Select(q => q.A).ToList();
            var text = string.Join("\n", lines.Select(i => a[i]));
            var holder = guards.FirstOrDefault(g => left[g.Loop] > 0 && InOrder(text, g.Fragments));
            if (holder.Loop is null)
                continue;
            left[holder.Loop]--;
            taken.UnionWith(lines);
        }

        return taken.Count == 0 ? null : (string.Join("\n", a.Where((_, i) => !taken.Contains(i))), now);
    }

    /// <summary>The template body of the entry <paramref name="key"/> (<c>node.&lt;path&gt;</c> or <c>deliverable.&lt;path&gt;</c>), or null.</summary>
    private static string? TemplateOf(Migration31Result migrated, string key)
    {
        var path = key.StartsWith("node.", StringComparison.Ordinal) ? "nodes/" + key["node.".Length..]
            : key.StartsWith("deliverable.", StringComparison.Ordinal) ? "deliverables/" + key["deliverable.".Length..]
            : null;
        return path is not null && migrated.Files.TryGetValue(path.Replace('.', '/') + ".md", out var file) ? FrontMatter.Split(file)?.Body : null;
    }

    /// <summary>Whether <paramref name="text"/> holds each of <paramref name="fragments"/>, in order, none overlapping.</summary>
    private static bool InOrder(string text, IReadOnlyList<string> fragments)
    {
        var at = 0;
        foreach (var fragment in fragments)
        {
            var found = text.IndexOf(fragment, at, StringComparison.Ordinal);
            if (found < 0)
                return false;
            at = found + fragment.Length;
        }

        return true;
    }

    /// <summary><paramref name="candidate"/> when it is nearer <paramref name="old"/> than <paramref name="now"/> is, else <paramref name="now"/>.</summary>
    private static string Nearest(string old, string now, string candidate) =>
        Distance(old, candidate) < Distance(old, now) ? candidate : now;

    // ---- the alignment (#1508, R19) ------------------------------------------------------------------------------------

    /// <summary>The most token edits <see cref="Distance"/> counts exactly, and the most an alignment is made for.</summary>
    private const int MaxEdits = 20_000;

    /// <summary>The most edits <see cref="Aligned"/> traces (its memory grows with their square).</summary>
    private const int MaxAligned = 2_000;

    /// <summary>
    /// How far 31's text <paramref name="now"/> is from 30's <paramref name="old"/>: the tokens (a run of letters and digits,
    /// a run of white space, any other character alone) to insert and delete to make one the other, the fewest (Myers'
    /// difference, bounded by <see cref="MaxEdits"/>; past it, every token).
    /// </summary>
    internal static int Distance(string old, string now)
    {
        if (string.Equals(old, now, StringComparison.Ordinal))
            return 0;
        var a = Tokens(old);
        var b = Tokens(now);
        return Edits(a, b, MaxEdits) ?? a.Count + b.Count;
    }

    private static List<string> Tokens(string text)
    {
        var tokens = new List<string>();
        var i = 0;
        while (i < text.Length)
        {
            var start = i;
            if (char.IsLetterOrDigit(text[i]))
            {
                while (i < text.Length && char.IsLetterOrDigit(text[i]))
                    i++;
            }
            else if (char.IsWhiteSpace(text[i]))
            {
                while (i < text.Length && char.IsWhiteSpace(text[i]))
                    i++;
            }
            else
            {
                i++;
            }

            tokens.Add(text[start..i]);
        }

        return tokens;
    }

    /// <summary>The fewest inserts and deletes between <paramref name="a"/> and <paramref name="b"/> (Myers), or null past <paramref name="bound"/>.</summary>
    private static int? Edits(IReadOnlyList<string> a, IReadOnlyList<string> b, int bound)
    {
        var (n, m) = (a.Count, b.Count);
        var max = Math.Min(n + m, bound);
        var v = new int[(2 * max) + 3];
        var offset = max + 1;
        for (var d = 0; d <= max; d++)
        {
            for (var k = -d; k <= d; k += 2)
            {
                var x = k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1]) ? v[offset + k + 1] : v[offset + k - 1] + 1;
                var y = x - k;
                while (x < n && y < m && string.Equals(a[x], b[y], StringComparison.Ordinal))
                    (x, y) = (x + 1, y + 1);
                v[offset + k] = x;
                if (x >= n && y >= m)
                    return d;
            }
        }

        return null;
    }

    /// <summary>
    /// The runs of <paramref name="a"/> (30's) and <paramref name="b"/> (31's) aligned, in order: <c>(i, j)</c> for two runs
    /// that stand at one place, equal or each the other's replacement (a stretch where as many of 30's runs were replaced by
    /// as many of 31's pairs them in order), <c>(i, -1)</c> for a run 30 alone has and <c>(-1, j)</c> for one 31 alone has.
    /// Null past <see cref="MaxAligned"/> edits.
    /// </summary>
    private static List<(int A, int B)>? Aligned(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var (n, m) = (a.Count, b.Count);
        var max = Math.Min(n + m, MaxAligned);
        var v = new int[(2 * max) + 3];
        var offset = max + 1;
        var trace = new List<int[]>();
        var found = -1;
        for (var d = 0; d <= max && found < 0; d++)
        {
            trace.Add((int[])v.Clone());
            for (var k = -d; k <= d; k += 2)
            {
                var x = k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1]) ? v[offset + k + 1] : v[offset + k - 1] + 1;
                var y = x - k;
                while (x < n && y < m && string.Equals(a[x], b[y], StringComparison.Ordinal))
                    (x, y) = (x + 1, y + 1);
                v[offset + k] = x;
                if (x >= n && y >= m)
                {
                    found = d;
                    break;
                }
            }
        }

        if (found < 0)
            return null;

        // Back through the trace: each step one insert or delete, then the equal runs before it.
        var steps = new List<(int A, int B)>();
        var (px, py) = (n, m);
        for (var d = found; d > 0; d--)
        {
            var prior = trace[d];
            var k = px - py;
            var down = k == -d || (k != d && prior[offset + k - 1] < prior[offset + k + 1]);
            var pk = down ? k + 1 : k - 1;
            var sx = prior[offset + pk];
            var sy = sx - pk;
            var (mx, my) = down ? (sx, sy + 1) : (sx + 1, sy);
            while (px > mx && py > my)
                steps.Add((--px, --py));
            steps.Add(down ? (-1, --py) : (--px, -1));
            (px, py) = (sx, sy);
        }

        while (px > 0 && py > 0)
            steps.Add((--px, --py));
        steps.Reverse();

        // A stretch of deletes and inserts with as many of each pairs them in order.
        var pairs = new List<(int A, int B)>();
        for (var i = 0; i < steps.Count;)
        {
            if (steps[i].A >= 0 && steps[i].B >= 0)
            {
                pairs.Add(steps[i++]);
                continue;
            }

            var start = i;
            while (i < steps.Count && (steps[i].A < 0 || steps[i].B < 0))
                i++;
            var deleted = steps.Skip(start).Take(i - start).Where(s => s.B < 0).Select(s => s.A).ToList();
            var inserted = steps.Skip(start).Take(i - start).Where(s => s.A < 0).Select(s => s.B).ToList();
            if (deleted.Count == inserted.Count)
                pairs.AddRange(deleted.Zip(inserted));
            else
                pairs.AddRange(steps.Skip(start).Take(i - start));
        }

        return pairs;
    }
}
