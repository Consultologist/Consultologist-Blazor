using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Source;

/// <summary>
/// #1502 (specVersion 31, area 05): the <c>refusal-packages</c> cases of the resolver: one for every sentence of
/// <see cref="Consultologist.PackageFormat.V31.ResolveSentences"/> a package reaches through
/// <see cref="Consultologist.PackageFormat.V31.SourceCheck.Run"/>. As the earlier areas' cases are, each side is
/// <see cref="SourceFixture.Minimal"/> plus only what its rule needs (the while <c>refine</c>, a loop over a list
/// input, a function and its call), and each invalid side is its valid side with one text replacement, so it gives
/// exactly one refusal.
/// </summary>
internal static partial class RefusalPackages
{
    private static IEnumerable<RefusalCase> Resolve() => Passes().Concat(Levels()).Concat(Sealed()).Concat(Sources()).Concat(Labels()).Concat(Unused()).Concat(Cases());

    // ---- Chapter 4 § 4.2.2: what a loop iterates -----------------------------------------------------------

    private const string SectionsLoop = "nodes/sections/index.json";

    private const string SectionsIn = "\"in\": \"input.sections.value\"";

    private static IEnumerable<RefusalCase> Sources()
    {
        yield return new RefusalCase("A05-001",
            "A loop's in is a reference: a root, then names joined by dots (chapter 4 § 4.2.2). The for sections outlines each "
            + "section of the input sections. Invalid: its in is input..sections.value. Valid: input.sections.value.",
            WithLoop(),
            WithLoop().Edit(SectionsLoop, SectionsIn, "\"in\": \"input..sections.value\""));

        yield return new RefusalCase("S02-018",
            "An input is read through .value or .content, never bare (chapter 2 § 2.2.1), in a loop's in too. The for sections "
            + "outlines each section of the input sections. Invalid: its in is input.sections. Valid: input.sections.value.",
            WithLoop(),
            WithLoop().Edit(SectionsLoop, SectionsIn, "\"in\": \"input.sections\""));

        yield return new RefusalCase("S04-017",
            "A loop's in names one readable thing; it is never an expression (chapter 4 § 4.2.2). The for sections outlines "
            + "each section of the input sections. Invalid: its in is { \"var\": \"input.sections.value\" }. Valid: the "
            + "reference input.sections.value.",
            WithLoop(),
            WithLoop().Edit(SectionsLoop, SectionsIn, "\"in\": { \"var\": \"input.sections.value\" }"));

        yield return new RefusalCase("S04-025",
            "Only a list can be iterated (chapter 4 § 4.2.2). The for sections outlines each section of the input sections. "
            + "Invalid: it iterates input.consult_draft.value, a markdown_block. Valid: input.sections.value, a list.",
            WithLoop(),
            WithLoop().Edit(SectionsLoop, SectionsIn, "\"in\": \"input.consult_draft.value\""));

        yield return new RefusalCase("S04-026",
            "item.value names the enclosing loop's item (chapter 4 § 4.2.2). The for sections outlines each section of the "
            + "input sections. Invalid: it iterates item.value.points, but it runs inside no loop. Valid: input.sections.value.",
            WithLoop(),
            WithLoop().Edit(SectionsLoop, SectionsIn, "\"in\": \"item.value.points\""));

        const string recap = "nodes/recap/index.json";
        yield return new RefusalCase("S04-027",
            "A loop iterates a node outside it, or in an enclosing loop's body (chapter 4 § 4.2.2). Beside the for sections, "
            + "the for recap writes one line per section. Invalid: recap iterates node.outline.value.points, but outline runs "
            + "inside sections. Valid: input.sections.value.",
            WithRecap(),
            WithRecap().Edit(recap, SectionsIn, "\"in\": \"node.outline.value.points\""));
    }

    // ---- Chapter 4 § 4.2.7: itemLabel ----------------------------------------------------------------------

    private static IEnumerable<RefusalCase> Labels()
    {
        const string label = "\"itemLabel\": \"title\"";
        yield return new RefusalCase("S04-028",
            "itemLabel names a field of the elements (chapter 4 § 4.2.7). The for sections names each section by its title. "
            + "Invalid: its itemLabel is heading, which the sections do not have. Valid: title.",
            WithLoop(),
            WithLoop().Edit(SectionsLoop, label, "\"itemLabel\": \"heading\""));

        yield return new RefusalCase("S04-029",
            "itemLabel names a field every element has, never null (chapter 4 § 4.2.7). The for sections names each section by "
            + "its title. Invalid: the input sections' title may be null. Valid: it may not.",
            WithLoop(),
            WithLoop().Edit("manifest.json", "\"title\": { \"type\": \"string\", \"title\": \"Title\" }",
                "\"title\": { \"type\": [\"string\", \"null\"], \"title\": \"Title\" }"));

        yield return new RefusalCase("S04-030",
            "itemLabel names a string field (chapter 4 § 4.2.7). The for sections names each section by its title. Invalid: "
            + "its itemLabel is points, a list. Valid: title.",
            WithLoop(),
            WithLoop().Edit(SectionsLoop, label, "\"itemLabel\": \"points\""));
    }

    // ---- Chapter 4 § 4.5.2: what a case switches on --------------------------------------------------------

    private static IEnumerable<RefusalCase> Cases()
    {
        const string route = "nodes/route/index.json";
        const string subject = "\"case\": \"input.note_kind.value\"";
        const string narrative = "\"when\": [\"narrative\"]";
        const string omit = ", { \"when\": [\"omit\"], \"nodes\": [], \"ret\": null }";

        yield return new RefusalCase("S04-044",
            "A case switches on an enumerated string (chapter 4 § 4.5.2). The case route drafts the note by the input "
            + "note_kind. Invalid: it switches on input.consult_draft.value, a markdown_block. Valid: input.note_kind.value.",
            WithCase(),
            WithCase().Edit(route, subject, "\"case\": \"input.consult_draft.value\""));

        yield return new RefusalCase("S04-045",
            "A case's values are its subject's (chapter 4 § 4.5.2). The case route drafts the note by the input note_kind, "
            + "narrative or omit. Invalid: its first branch lists narrative and prose. Valid: narrative.",
            WithCase(),
            WithCase().Edit(route, narrative, "\"when\": [\"narrative\", \"prose\"]"));

        yield return new RefusalCase("S04-046",
            "A value belongs to one branch (chapter 4 § 4.5.2). The case route drafts the note by the input note_kind. "
            + "Invalid: its second, empty branch lists omit and narrative, which the first lists. Valid: omit.",
            WithCase(),
            WithCase().Edit(route, "\"when\": [\"omit\"]", "\"when\": [\"omit\", \"narrative\"]"));

        yield return new RefusalCase("S04-047",
            "Every case is exhaustive: a branch for every value, or an else (chapter 4 § 4.5.2). The case route drafts the note "
            + "by the input note_kind. Invalid: it has no branch for omit and no else. Valid: an empty branch for omit.",
            WithCase(),
            WithCase().Edit(route, omit, ""));

        yield return new RefusalCase("S04-075",
            "A subject that can be null requires an else (chapter 4 § 4.5.2). The case route drafts the note by the input "
            + "note_kind. Invalid: note_kind may be null. Valid: it may not.",
            WithCase(),
            WithCase().Edit("manifest.json", NoteKinds,
                "{ \"type\": [\"string\", \"null\"], \"enum\": [\"narrative\", \"omit\", null] }"));
    }

    // ---- Chapter 4 § 4.10.1: a while's body nodes are used -------------------------------------------------

    private static IEnumerable<RefusalCase> Unused()
    {
        const string tally = "nodes/tally/index.json";
        yield return new RefusalCase("S04-035",
            "Every node in a while is returned or read (chapter 4 § 4.10.1). The while tally of two computed nodes runs while "
            + "count is below 3. Invalid: it returns count, and nothing reads double. Valid: it returns double.",
            WithTally(),
            WithTally().Edit(tally, "\"ret\": \"node.tally.double\"", "\"ret\": \"node.tally.count\""));
    }

    /// <summary><see cref="WithLoop"/> with a second loop, <c>recap</c>, writing one line per section of the input with the agent node <c>recap_line</c>.</summary>
    private static Dictionary<string, byte[]> WithRecap() =>
        WithLoop()
            .Edit("nodes/index.json", "\"items\": [\"plan\", \"sections\"]", "\"items\": [\"plan\", \"sections\", \"recap\"]")
            .With("nodes/recap/index.json", Json(Node("for", "Recap", ArrayOf(Plain), false, true,
                SectionsIn + ", \"parallel\": true, \"maxParallel\": null, \"until\": null, \"max\": null, "
                + "\"itemLabel\": null, \"nodes\": [\"recap_line\"], \"ret\": \"node.recap.recap_line\"")))
            .With("nodes/recap/recap_line.md", Agent("Writing a recap line", Plain,
                "Write one line for Dr A. Sample recapping this section of the note.\n"));

    /// <summary>
    /// A <c>while</c>, <c>tally</c>, of two computed nodes, <c>count</c> (the pass number, read by its check) and
    /// <c>double</c> (twice it), returning <c>double</c>.
    /// </summary>
    private static Dictionary<string, byte[]> WithTally() =>
        MinimalListing("tally")
            .With("nodes/tally/index.json", Json(Node("while", "Counting the passes", "\"number\"", true, false,
                "\"while\": { \"<\": [{ \"var\": \"node.count.value\" }, 3] }, \"max\": 3, \"until\": null, "
                + "\"checkFirst\": false, \"nodes\": [\"count\", \"double\"], \"ret\": \"node.tally.double\"")))
            .With("nodes/tally/count.md", Md(Node("computed", "Counting", "\"number\"", true, false,
                "\"if\": null, \"expression\": { \"+\": [{ \"var\": \"pass.number\" }, 0] }"), ""))
            .With("nodes/tally/double.md", Md(Node("computed", "Doubling", "\"number\"", true, false,
                "\"if\": null, \"expression\": { \"*\": [{ \"var\": \"pass.number\" }, 2] }"), ""));

    // ---- Chapter 4 § 4.3.3: what a pass reads --------------------------------------------------------------

    private static IEnumerable<RefusalCase> Passes()
    {
        yield return new RefusalCase("S04-036",
            "pass is a while's pass (chapter 4 § 4.3.3). The while refine drafts a summary until its judge is satisfied. "
            + "Invalid: the agent node plan, outside it, runs only when pass.number is over 1. Valid: when refine's .passes is.",
            WithWhile().Edit(PlanFile, "\"if\": null", If("""{ ">": [{ "var": "node.refine.passes" }, 1] }""")),
            WithWhile().Edit(PlanFile, "\"if\": null", If("""{ ">": [{ "var": "pass.number" }, 1] }""")));

        const string judge = "nodes/refine/judge.md";
        yield return new RefusalCase("S04-037",
            "pass.parent is the enclosing while's pass (chapter 4 § 4.3.3). The judge of the while refine runs from its "
            + "second pass. Invalid: it reads pass.parent.number, but refine runs inside no while. Valid: pass.number.",
            WithWhile().Edit(judge, "\"if\": null", If("""{ ">": [{ "var": "pass.number" }, 1] }""")),
            WithWhile().Edit(judge, "\"if\": null", If("""{ ">": [{ "var": "pass.parent.number" }, 1] }""")));

        const string draft = "nodes/refine/draft_summary.md";
        yield return new RefusalCase("S04-038",
            "A pass reads the while's own nodes (chapter 4 § 4.3.3). The summary of the while refine is drafted again only "
            + "when the previous pass's judge had something to say. Invalid: it reads pass.previous.node.plan, which is not "
            + "in refine. Valid: pass.previous.node.judge.",
            WithWhile().Edit(draft, "\"if\": null", If("""{ "!": [{ "blank": [{ "var": "pass.previous.node.judge.content" }] }] }""")),
            WithWhile().Edit(draft, "\"if\": null", If("""{ "!": [{ "blank": [{ "var": "pass.previous.node.plan.content" }] }] }""")));
    }

    // ---- Chapter 4 § 4.4: reading across levels ------------------------------------------------------------

    private static IEnumerable<RefusalCase> Levels()
    {
        yield return new RefusalCase("S04-039",
            "A node inside a loop is a list from outside it: read the loop (chapter 4 § 4.4). The loop notes summarises "
            + "each prior note. Invalid: the agent node plan's if reads node.note_summary, which runs inside notes. "
            + "Valid: node.notes.",
            WithNotesLoop().Edit(PlanFile, "\"if\": null", If("""{ "!": [{ "blank": [{ "var": "node.notes.content" }] }] }""")),
            WithNotesLoop().Edit(PlanFile, "\"if\": null", If("""{ "!": [{ "blank": [{ "var": "node.note_summary.content" }] }] }""")));

        const string summary = "nodes/notes/note_summary.md";
        var written = If("""{ "!": [{ "blank": [{ "var": "item.value" }] }] }""");
        yield return new RefusalCase("S04-040",
            "item.parent is the enclosing loop's item (chapter 4 § 4.2.6). The loop notes summarises each prior note that "
            + "has text. Invalid: its note_summary reads item.parent.value, but notes runs inside no loop. Valid: item.value.",
            WithNotesLoop().Edit(summary, "\"if\": null", written),
            WithNotesLoop().Edit(summary, "\"if\": null", written).Edit(summary, "\"var\": \"item.value\"", "\"var\": \"item.parent.value\""));
    }

    // ---- Chapter 4 § 4.7.4: a function is sealed -----------------------------------------------------------

    private static IEnumerable<RefusalCase> Sealed()
    {
        const string patient = "functions/draft_section/patient_draft.md";
        var written = If("""{ "!": [{ "blank": [{ "var": "param.draft.content" }] }] }""");

        yield return new RefusalCase("S04-050",
            "A function reads only its parameters (chapter 4 § 4.7.4). The function draft_section drafts from the "
            + "parameter draft, when it has text. Invalid: its node patient_draft's if reads the input consult_draft. "
            + "Valid: the parameter draft.",
            WithFunction().Edit(patient, "\"if\": null", written),
            WithFunction().Edit(patient, "\"if\": null", written).Edit(patient, "param.draft.content\" }", "input.consult_draft.content\" }"));

        yield return new RefusalCase("S04-051",
            "A function reads only its parameters, never a node outside it (chapter 4 § 4.7.4). The function draft_section "
            + "drafts from the parameter draft, when it has text. Invalid: its node patient_draft's if reads node.plan, a "
            + "top-level node. Valid: the parameter draft.",
            WithFunction().Edit(patient, "\"if\": null", written),
            WithFunction().Edit(patient, "\"if\": null", written).Edit(patient, "param.draft.content\" }", "node.plan.content\" }"));
    }

    /// <summary>A header's <c>if</c> member, <paramref name="rule"/> its value: what replaces <c>"if": null</c>.</summary>
    private static string If(string rule) => "\"if\": " + rule;

    /// <summary><see cref="WithPriorNotes"/> with the loop <c>notes</c>, summarising each prior note with the agent node <c>note_summary</c>.</summary>
    private static Dictionary<string, byte[]> WithNotesLoop() =>
        WithPriorNotes()
            .Edit("nodes/index.json", "\"items\": [\"plan\"]", "\"items\": [\"plan\", \"notes\"]")
            .With("nodes/notes/index.json", Json(Node("for", "Prior notes", ArrayOf(Plain), false, true,
                "\"in\": \"input.prior_notes.value\", \"parallel\": true, \"maxParallel\": null, \"until\": null, \"max\": null, "
                + "\"itemLabel\": null, \"nodes\": [\"note_summary\"], \"ret\": \"node.notes.note_summary\"")))
            .With("nodes/notes/note_summary.md", Agent("Summarising a prior note", Plain,
                "Summarise the prior note below in one sentence for Dr A. Sample.\n"));
}
