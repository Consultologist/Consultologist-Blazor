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
    private static IEnumerable<RefusalCase> Resolve() => Passes().Concat(Levels()).Concat(Sealed());

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
