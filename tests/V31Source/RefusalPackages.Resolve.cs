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
    private static IEnumerable<RefusalCase> Resolve() => Passes().Concat(Levels()).Concat(Sealed()).Concat(Sources()).Concat(Labels()).Concat(Unused()).Concat(Cases())
        .Concat(Calls()).Concat(Rets()).Concat(Uses()).Concat(Graph());

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
        const string note = "functions/draft_section/draft_note.md";
        var written = If("""{ "!": [{ "blank": [{ "var": "param.draft.content" }] }] }""");

        yield return new RefusalCase("S04-050",
            "A function reads only its parameters (chapter 4 § 4.7.4). The function draft_section notes the parameter draft, "
            + "when it has text. Invalid: its node draft_note's if reads the input consult_draft. Valid: the parameter draft.",
            WithDraftNote().Edit(note, "\"if\": null", written),
            WithDraftNote().Edit(note, "\"if\": null", written).Edit(note, "param.draft.content\" }", "input.consult_draft.content\" }"));

        yield return new RefusalCase("S04-051",
            "A function reads only its parameters, never a node outside it (chapter 4 § 4.7.4). The function draft_section "
            + "notes the parameter draft, when it has text. Invalid: its node draft_note's if reads node.plan, a top-level "
            + "node. Valid: the parameter draft.",
            WithDraftNote().Edit(note, "\"if\": null", written),
            WithDraftNote().Edit(note, "\"if\": null", written).Edit(note, "param.draft.content\" }", "node.plan.content\" }"));
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

    // ---- Chapter 4 § 4.7.7: the call node ------------------------------------------------------------------

    private const string CallFile = "nodes/section.md";

    private const string DraftArgument = "\"with\": { \"draft\": \"input.consult_draft.content\" }";

    private static IEnumerable<RefusalCase> Calls()
    {
        yield return new RefusalCase("S04-064",
            "A call names a function the package defines (chapter 4 § 4.7.7). The call section runs draft_section. Invalid: it "
            + "names draft_sections. Valid: draft_section.",
            WithFunction(),
            WithFunction().Edit(CallFile, "\"function\": \"draft_section\"", "\"function\": \"draft_sections\""));

        yield return new RefusalCase("S04-065",
            "A call passes every parameter (chapter 4 § 4.7.7). The call section runs draft_section, whose parameter is draft. "
            + "Invalid: its with is {}. Valid: it passes draft.",
            WithFunction(),
            WithFunction().Edit(CallFile, DraftArgument, "\"with\": {}"));

        yield return new RefusalCase("S04-066",
            "A call passes only the function's parameters (chapter 4 § 4.7.7). The call section runs draft_section, whose "
            + "parameter is draft. Invalid: it also passes tone. Valid: draft only.",
            WithFunction(),
            WithFunction().Edit(CallFile, DraftArgument,
                "\"with\": { \"draft\": \"input.consult_draft.content\", \"tone\": \"input.consult_draft.content\" }"));

        yield return new RefusalCase("S04-068",
            "Each argument is typed against its parameter (chapter 4 § 4.7.7). The call section passes draft_section's draft, a "
            + "markdown_block. Invalid: it passes node.plan, a plaintext. Valid: input.consult_draft.content.",
            WithFunction(),
            WithFunction().Edit(CallFile, DraftArgument, "\"with\": { \"draft\": \"node.plan\" }"));

        yield return new RefusalCase("S04-070",
            "A call's format is its function's (chapter 4 § 4.7.7). The call section runs draft_section, which returns "
            + "plaintext. Invalid: the call declares markdown_block. Valid: plaintext.",
            WithFunction(),
            WithFunction().Edit(CallFile, "\"format\": \"plaintext\"", "\"format\": \"markdown_block\""));

        yield return new RefusalCase("A05-005",
            "An argument is a reference string, or null (chapter 4 § 4.7.7). The call section passes draft_section's draft. "
            + "Invalid: it passes the number 5. Valid: input.consult_draft.content.",
            WithFunction(),
            WithFunction().Edit(CallFile, DraftArgument, "\"with\": { \"draft\": 5 }"));

        const string note = "functions/draft_section/draft_note.md";
        var profiled = WithDraftNote()
            .Edit(FunctionIndex, "\"format\": \"markdown_block\" }", "\"format\": \"plaintext\" }")
            .Edit(CallFile, "input.consult_draft.content", "node.plan.content")
            .Edit(note, "\"if\": null", If("""{ "!": [{ "blank": [{ "var": "param.draft.content" }] }] }"""));
        yield return new RefusalCase("S04-069",
            "A parameter bound to the profile carries it, and no condition reads it (chapter 4 § 4.7.7). The call section "
            + "passes draft_section's draft, which its node draft_note's if reads. Invalid: the call passes "
            + "profile.signature. Valid: node.plan.content.",
            profiled,
            WithDraftNote()
                .Edit(FunctionIndex, "\"format\": \"markdown_block\" }", "\"format\": \"plaintext\" }")
                .Edit(CallFile, "input.consult_draft.content", "profile.signature")
                .Edit(note, "\"if\": null", If("""{ "!": [{ "blank": [{ "var": "param.draft.content" }] }] }""")));
    }

    // ---- Chapter 4 § 4.7.6: one ret, always valued, one format ---------------------------------------------

    private const string FunctionIndex = "functions/draft_section/index.json";

    private const string PatientRet = "\"ret\": \"node.patient_draft\"";

    private static IEnumerable<RefusalCase> Rets()
    {
        yield return new RefusalCase("S04-061",
            "A function's format is its ret's (chapter 4 § 4.7.6). The function draft_section returns patient_draft and "
            + "declares plaintext. Invalid: patient_draft returns markdown_block. Valid: plaintext.",
            WithFunction(),
            WithFunction().Edit("functions/draft_section/patient_draft.md", "\"format\": \"plaintext\"", "\"format\": \"markdown_block\""));

        var noElse = WithBranchInFunction("checked", Checked("null"), ("checked/final.md", Final(null)));
        yield return new RefusalCase("S04-057",
            "A function's ret always has a value (chapter 4 § 4.7.6). The function draft_section holds the if checked, which "
            + "has no else. Invalid: it returns checked. Valid: it returns patient_draft.",
            noElse,
            WithBranchInFunction("checked", Checked("null"), ("checked/final.md", Final(null)))
                .Edit(FunctionIndex, PatientRet, "\"ret\": \"node.checked\""));

        (string, string)[] skippable = [Checked(CheckedElse), ("checked/final.md", Final(HasText)), ("checked/fallback.md", Final(null, "Writing a fallback"))];
        yield return new RefusalCase("S04-058",
            "A function's ret always has a value (chapter 4 § 4.7.6). The function draft_section holds the if checked, whose "
            + "then returns final, which has an if of its own. Invalid: it returns checked. Valid: it returns patient_draft.",
            WithBranchInFunction("checked", skippable),
            WithBranchInFunction("checked", skippable).Edit(FunctionIndex, PatientRet, "\"ret\": \"node.checked\""));

        var route = ("route/index.json", Json(Node("case", "Routing by the draft's state", Plain, false, true,
            "\"case\": \"node.patient_draft.state\", \"branches\": ["
            + "{ \"when\": [\"completed\"], \"nodes\": [\"final\"], \"ret\": \"node.route.final\" }, "
            + "{ \"when\": [\"skipped\", \"not_run\"], \"nodes\": [], \"ret\": null }], \"else\": null")));
        (string, string)[] routed = [route, ("route/final.md", Final(null))];
        yield return new RefusalCase("S04-059",
            "A function's ret always has a value (chapter 4 § 4.7.6). The function draft_section holds the case route, whose "
            + "branch for skipped and not_run is empty. Invalid: it returns route. Valid: it returns patient_draft.",
            WithBranchInFunction("route", routed),
            WithBranchInFunction("route", routed).Edit(FunctionIndex, PatientRet, "\"ret\": \"node.route\""));

        (string, string)[] twoFormats =
        [
            Checked(CheckedElse, "{ \"anyOf\": [{ \"$ref\": \"plaintext\" }, { \"$ref\": \"markdown_block\" }] }"),
            ("checked/final.md", Final(null)),
            ("checked/fallback.md", Final(null, "Writing a fallback", "\"markdown_block\"")),
        ];
        yield return new RefusalCase("S04-060",
            "A function returns one format (chapter 4 § 4.7.6). The function draft_section holds the if checked, whose then "
            + "returns plaintext and whose else markdown_block. Invalid: it returns checked. Valid: it returns patient_draft.",
            WithBranchInFunction("checked", twoFormats),
            WithBranchInFunction("checked", twoFormats).Edit(FunctionIndex, PatientRet, "\"ret\": \"node.checked\""));
    }

    private const string HasText = """{ "!": [{ "blank": [{ "var": "param.draft.content" }] }] }""";

    private const string CheckedElse = "{ \"nodes\": [\"fallback\"], \"ret\": \"node.checked.fallback\" }";

    /// <summary>The if <c>checked</c> of <see cref="WithBranchInFunction"/>: when the parameter draft has text, <c>final</c>; <paramref name="otherwise"/> its <c>else</c>.</summary>
    private static (string, string) Checked(string otherwise, string format = Plain) =>
        ("checked/index.json", Json(Node("if", "Writing when there is a draft", format, false, true,
            $"\"if\": {HasText}, \"then\": {{ \"nodes\": [\"final\"], \"ret\": \"node.checked.final\" }}, \"else\": {otherwise}")));

    /// <summary>An agent node of <see cref="Checked"/>'s bodies, with the <c>if</c> <paramref name="condition"/> (none when null).</summary>
    private static string Final(string? condition, string label = "Writing the section", string format = Plain) =>
        Md(Node("agent", label, format, false, true,
                $"\"if\": {condition ?? "null"}, \"onInvalid\": \"retry\", \"failIfEmpty\": null, \"parts\": null, \"ordered\": false"),
            "Write the section for Margaret Okafor.\n\n{{ param.draft.content }}\n");

    /// <summary><see cref="WithFunction"/> with the flow node <paramref name="id"/>, written by <paramref name="files"/> (under the function's folder), beside <c>patient_draft</c>, which it returns.</summary>
    private static Dictionary<string, byte[]> WithBranchInFunction(string id, params (string Path, string Text)[] files)
    {
        var package = WithFunction().Edit(FunctionIndex, "\"nodes\": [\"patient_draft\"]", $"\"nodes\": [\"patient_draft\", \"{id}\"]");
        foreach (var (path, text) in files)
            package.With($"functions/draft_section/{path}", text);
        return package;
    }

    /// <summary><see cref="WithFunction"/> with a second agent node, <c>draft_note</c>, beside <c>patient_draft</c>, which it returns.</summary>
    private static Dictionary<string, byte[]> WithDraftNote() =>
        WithFunction()
            .Edit(FunctionIndex, "\"nodes\": [\"patient_draft\"]", "\"nodes\": [\"patient_draft\", \"draft_note\"]")
            .With("functions/draft_section/draft_note.md", Agent("Noting the draft", Plain,
                "Note in one line what the consult draft says about Margaret Okafor.\n\n{{ param.draft.content }}\n"));

    // ---- Chapter 4 § 4.7.3: uses ---------------------------------------------------------------------------

    private static IEnumerable<RefusalCase> Uses()
    {
        const string listed = "\"uses\": { \"snippets\": [\"standards\"]";
        yield return new RefusalCase("S04-052",
            "A function uses a package snippet only if it lists it in uses (chapter 4 § 4.7.3). The function draft_section "
            + "notes each standard of the package's snippet folder standards. Invalid: its uses lists no snippet. Valid: it "
            + "lists standards.",
            WithStandardsInFunction(),
            WithStandardsInFunction().Edit(FunctionIndex, listed, "\"uses\": { \"snippets\": []"));

        yield return new RefusalCase("S04-053",
            "A function's own definition does not share an id with one it uses (chapter 4 § 4.7.3). The package and the "
            + "function draft_section each have a snippet tone. Invalid: the function also lists the package's tone in uses. "
            + "Valid: it lists none.",
            WithOwnTone(),
            WithOwnTone().Edit(FunctionIndex, "\"uses\": { \"snippets\": []", "\"uses\": { \"snippets\": [\"tone\"]"));
    }

    /// <summary><see cref="WithFunction"/> and <see cref="WithSnippetFolder"/>, the function's loop <c>each</c> noting each standard; its <c>uses</c> lists the folder.</summary>
    private static Dictionary<string, byte[]> WithStandardsInFunction()
    {
        var files = WithFunction()
            .Edit(FunctionIndex, "\"nodes\": [\"patient_draft\"]", "\"nodes\": [\"patient_draft\", \"each\"]")
            .Edit(FunctionIndex, "\"uses\": { \"snippets\": []", "\"uses\": { \"snippets\": [\"standards\"]")
            .With("functions/draft_section/each/index.json", Json(Node("for", "Each standard", ArrayOf(Plain), false, true,
                "\"in\": \"snippet.standards\", \"parallel\": false, \"maxParallel\": null, \"until\": null, \"max\": null, "
                + "\"itemLabel\": null, \"nodes\": [\"note\"], \"ret\": \"node.each.note\"")))
            .With("functions/draft_section/each/note.md", Agent("Noting the standard", Plain,
                "Note this standard in one line.\n\n{{ item.content }}\n"));
        foreach (var (path, bytes) in WithSnippetFolder().Where(f => f.Key.StartsWith("snippets/", StringComparison.Ordinal)))
            files[path] = bytes;
        return files;
    }

    /// <summary><see cref="WithFunction"/> with a package snippet <c>tone</c> and the function's own snippet <c>tone</c>.</summary>
    private static Dictionary<string, byte[]> WithOwnTone()
    {
        const string tone = "---\n{ \"label\": \"Tone\", \"description\": null, \"if\": null, \"format\": \"plaintext\" }\n---\nWrite plainly for Dr A. Sample.\n";
        return WithFunction()
            .Edit("snippets/index.json", "\"items\": []", "\"items\": [\"tone\"]")
            .With("snippets/tone.md", tone)
            .Edit("functions/draft_section/snippets/index.json", "\"items\": []", "\"items\": [\"tone\"]")
            .With("functions/draft_section/snippets/tone.md", tone);
    }

    // ---- Chapter 4 § 4.7.8: depth and recursion ------------------------------------------------------------

    private static IEnumerable<RefusalCase> Graph()
    {
        yield return new RefusalCase("S04-055",
            "Functions may not call themselves (chapter 4 § 4.7.8). The call start runs f01, which calls f02. Invalid: f01 "
            + "calls f01. Valid: f02.",
            WithChain(2),
            WithChain(2).Edit("functions/f01/next.md", "\"function\": \"f02\"", "\"function\": \"f01\""));

        yield return new RefusalCase("S04-054",
            "Functions may not call each other in a ring (chapter 4 § 4.7.8). The call start runs f01, which calls f02, which "
            + "calls f03. Invalid: f02 calls f01. Valid: f03.",
            WithChain(3),
            WithChain(3).Edit("functions/f02/next.md", "\"function\": \"f03\"", "\"function\": \"f01\""));

        yield return new RefusalCase("S04-056",
            "Calls nest at most FormatBounds.depth (15) deep (chapter 4 § 4.7.8). The functions f01 to f17 each call the "
            + "next. Invalid: the call start runs f01, sixteen calls deep. Valid: it runs f02, fifteen deep.",
            WithChain(17).Edit("nodes/start.md", "\"function\": \"f01\"", "\"function\": \"f02\""),
            WithChain(17));
    }

    /// <summary>
    /// <see cref="SourceFixture.Minimal"/> with the call <c>start</c> of <c>f01</c> and the functions <c>f01</c> to
    /// <c>f&lt;n&gt;</c>, each of one parameter, <c>draft</c>: each calls the next with a call <c>next</c>, and the last
    /// writes with the agent <c>write</c>.
    /// </summary>
    private static Dictionary<string, byte[]> WithChain(int n)
    {
        var ids = Enumerable.Range(1, n).Select(i => $"f{i:00}").ToList();
        var files = MinimalListing("start")
            .With("nodes/start.md", CallOf("f01", "input.consult_draft.value"))
            .Edit("functions/index.json", "\"items\": []", $"\"items\": [{string.Join(", ", ids.Select(id => $"\"{id}\""))}]");
        for (var i = 0; i < n; i++)
        {
            var node = i + 1 < n ? "next" : "write";
            files.With($"functions/{ids[i]}/index.json", Json(
                    "{ \"label\": \"Drafting\", \"description\": null, "
                    + "\"inputs\": { \"draft\": { \"label\": \"Consult draft\", \"description\": null, \"format\": \"markdown_block\" } }, "
                    + "\"uses\": { \"snippets\": [], \"macros\": [], \"formats\": [], \"renderers\": [] }, "
                    + $"\"nodes\": [\"{node}\"], \"ret\": \"node.{node}\", \"format\": \"plaintext\" }}"))
                .With($"functions/{ids[i]}/{node}.md", i + 1 < n
                    ? CallOf(ids[i + 1], "param.draft.value")
                    : Agent("Writing the section", Plain, "Write the section for Margaret Okafor.\n\n{{ param.draft.content }}\n"));
            foreach (var folder in new[] { "snippets", "macros", "formats", "renderers" })
                files.With($"functions/{ids[i]}/{folder}/index.json", SourceFixture.Index([]));
        }

        return files;
    }

    private static string CallOf(string function, string draft) =>
        Md(Node("call", "Calling the next function", Plain, false, true,
            $"\"if\": null, \"function\": \"{function}\", \"with\": {{ \"draft\": \"{draft}\" }}"), "");
}
