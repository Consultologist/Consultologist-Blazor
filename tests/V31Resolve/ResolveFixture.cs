using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Resolve;

/// <summary>
/// #1502 (specVersion 31, area 05): the packages the resolver's tests read, each built on
/// <see cref="SourceFixture.Minimal"/>: <see cref="NestedNote"/> (part 04 § 2.8's two loops and § 3.5's while, with
/// the formats of part 07 § 2.9), <see cref="Conditional"/> (§ 5.4's <c>case</c> and <c>if</c> over an array input)
/// and <see cref="Functions"/> (§ 7.11's <c>draft_section</c>, its call in a loop over a snippet folder, and the check
/// loop over <c>.items</c>); and the steps a test runs on one (the published registry, read off the submodules).
/// Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
internal static class ResolveFixture
{
    private const string Plain = "\"plaintext\"";

    private static readonly Lazy<TypeRegistry> Registry = new(V31Contract.PublishedTypes);

    /// <summary>The published registry.</summary>
    public static TypeRegistry Published() => Registry.Value;

    /// <summary><see cref="SourceCheck.Run"/> on <paramref name="files"/> against the published registry.</summary>
    public static SourceCheckResult Run(Dictionary<string, byte[]> files) => SourceCheck.Run(files, Registry.Value);

    /// <summary>The refusals of <paramref name="result"/>, one line, for an assertion's message.</summary>
    public static string Said(SourceCheckResult result) =>
        string.Join(" | ", result.Refusals.Select(r => $"{r.Id} {r.Path} {r.Sentence}"));

    /// <summary>
    /// The package parsed and typed (both asserted to accept it), and its <see cref="ScopeTree"/>.
    /// </summary>
    public static (SourcePackage Package, PackageTypes Types, ScopeTree Tree) Tree(Dictionary<string, byte[]> files)
    {
        var parsed = SourceParser.Parse(files, Registry.Value.Names);
        Assert.True(parsed.Package != null, "The reader refused: " + string.Join(" | ", parsed.Refusals.Select(r => $"{r.Id} {r.Sentence}")));
        var types = SourceTypes.Check(parsed.Package!, Registry.Value);
        Assert.True(types.Types != null, "The type rules refused: " + string.Join(" | ", types.Refusals.Select(r => $"{r.Id} {r.Sentence}")));
        return (parsed.Package!, types.Types!, ScopeTree.Build(parsed.Package!, types.Types!));
    }

    /// <summary>The node whose key is <paramref name="key"/>, in <c>nodes/</c>, <c>checks/</c> or <paramref name="function"/>'s body.</summary>
    public static SourceNode Node(SourcePackage package, string key, string? function = null)
    {
        var top = function == null
            ? package.Nodes.Nodes.Concat(package.Checks.Nodes)
            : package.Functions.Items.Single(f => f.Id == function).Body.Nodes;
        return Walk(top).Single(n => n.Key == key);
    }

    /// <summary>Every node under <paramref name="nodes"/>, a flow node before its children.</summary>
    public static IEnumerable<SourceNode> Walk(IEnumerable<SourceNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            IEnumerable<SourceBlock> blocks = node switch
            {
                SourceFor loop => [loop.Body],
                SourceWhile loop => [loop.Body],
                SourceIf branch => branch.Else == null ? [branch.Then] : [branch.Then, branch.Else],
                SourceCase choice => choice.Branches.Select(b => b.Body).Concat(choice.Else == null ? [] : [choice.Else]),
                _ => [],
            };
            foreach (var block in blocks)
                foreach (var inner in Walk(block.Nodes))
                    yield return inner;
        }
    }

    // ---- NestedNote ----------------------------------------------------------------------------

    /// <summary>
    /// <c>example-nested-note</c>: <c>plan</c> (a <c>section_plan</c>), <c>section_count</c>, the loop <c>sections</c>
    /// over the plan's sections holding <c>outline</c> and the loop <c>paragraphs</c> over the outline's points
    /// (holding <c>draft_paragraph</c>), and the while <c>refine</c> of <c>draft_summary</c> and <c>judge</c>.
    /// </summary>
    public static Dictionary<string, byte[]> NestedNote()
    {
        var files = WithFormats(Minimal(), "section_plan", "outline", "verdict")
            .With("nodes/index.json", Index(["plan", "section_count", "sections", "refine"]))
            .With("nodes/plan.md", Agent("Planning the sections", "\"section_plan\"",
                "Plan the sections of a consultation note for the draft below, one section for each heading.\n\n"
                + "{{ input.consult_draft.content }}\n"))
            .With("nodes/section_count.md", Md(
                SourceFixture.Node("computed", "Counting the sections", "\"number\"", true, true,
                    "\"if\": null, \"expression\": { \"size\": [{ \"var\": \"node.plan.value.sections\" }] }"),
                ""))
            .With("nodes/sections/index.json", Json(SourceFixture.Node("for", "Sections", ArrayOf(ArrayOf(Plain)), false, true,
                "\"in\": \"node.plan.value.sections\", \"parallel\": true, \"maxParallel\": null, \"until\": null, "
                + "\"max\": null, \"itemLabel\": \"title\", \"nodes\": [\"outline\", \"paragraphs\"], "
                + "\"ret\": \"node.sections.paragraphs\"")))
            .With("nodes/sections/outline.md", Agent("Outlining the section", "\"outline\"",
                "Outline the section '{{ item.value.title }}' of the note as a short list of points.\n"))
            .With("nodes/sections/paragraphs/index.json", Json(SourceFixture.Node("for", "Paragraphs", ArrayOf(Plain), false, true,
                "\"in\": \"node.outline.value.points\", \"parallel\": true, \"maxParallel\": null, \"until\": null, "
                + "\"max\": null, \"itemLabel\": null, \"nodes\": [\"draft_paragraph\"], "
                + "\"ret\": \"node.sections.paragraphs.draft_paragraph\"")))
            .With("nodes/sections/paragraphs/draft_paragraph.md", Md(
                SourceFixture.Node("agent", "Drafting a paragraph", Plain, false, true,
                    "\"if\": { \"!\": [{ \"blank\": [{ \"var\": \"item.parent.value.title\" }] }] }, "
                    + "\"onInvalid\": \"retry\", \"failIfEmpty\": null, \"parts\": null, \"ordered\": false"),
                "Write one paragraph of the section '{{ item.parent.value.title }}' about this point: {{ item.value }}\n"))
            .With("nodes/refine/index.json", Json(SourceFixture.Node("while", "Refining the summary", Plain, false, true,
                "\"while\": { \"!==\": [{ \"var\": \"node.judge.value.verdict\" }, \"good\"] }, \"max\": 3, "
                + "\"until\": null, \"checkFirst\": false, \"nodes\": [\"draft_summary\", \"judge\"], "
                + "\"ret\": \"node.refine.draft_summary\"")))
            .With("nodes/refine/draft_summary.md", Agent("Drafting the summary", Plain,
                "Summarise the note in two sentences for Dr A. Sample.\n"))
            .With("nodes/refine/judge.md", Agent("Judging the summary", "\"verdict\"",
                "Say whether the summary is good or needs another pass.\n\n{{ node.draft_summary.content }}\n"));
        return files.With("deliverables/note.md", Md(
            Deliverable("Nested consultation note", "\"markdown_block\"", "null", "hold"),
            "## Summary\n\n{{ node.refine.content }}\n"));
    }

    // ---- Conditional ---------------------------------------------------------------------------

    /// <summary>
    /// <c>example-conditional-sections</c>: the required input <c>sections</c>, a list of <c>section_request</c>
    /// (<c>title</c>, and <c>kind</c> of narrative, table or omit), and <c>include_billing</c>; the loop
    /// <c>sections_draft</c> routing each section by a <c>case</c> (the omit branch empty), the loop
    /// <c>patient_summaries</c> holding the <c>if</c> <c>narrative_only</c>, and the loop <c>standing_sections</c>
    /// holding the <c>if</c> <c>applies</c>.
    /// </summary>
    public static Dictionary<string, byte[]> Conditional()
    {
        var files = WithFormats(Minimal(), "section_request")
            .WithJson("manifest.json", m =>
            {
                var inputs = m["inputs"]!.AsObject();
                inputs["sections"] = System.Text.Json.Nodes.JsonNode.Parse(
                    "{ \"label\": \"Sections\", \"description\": null, "
                    + "\"format\": { \"type\": \"array\", \"items\": { \"$ref\": \"section_request\" } }, \"required\": true, \"channel\": null }");
                inputs["include_billing"] = System.Text.Json.Nodes.JsonNode.Parse(
                    "{ \"label\": \"Include the billing section\", \"description\": null, \"format\": \"boolean\", \"required\": false, \"channel\": null }");
            })
            .With("nodes/index.json", Index(["plan", "sections_draft", "patient_summaries", "standing_sections"]));

        files.With("nodes/sections_draft/index.json", Json(SourceFixture.Node("for", "Sections: drafting each by its kind",
                ArrayOf(Plain), false, true,
                "\"in\": \"input.sections.value\", \"parallel\": true, \"maxParallel\": null, \"until\": null, "
                + "\"max\": null, \"itemLabel\": \"title\", \"nodes\": [\"route\"], \"ret\": \"node.sections_draft.route\"")))
            .With("nodes/sections_draft/route/index.json", Json(SourceFixture.Node("case", "Routing a section by its kind", Plain, false, true,
                "\"case\": \"item.value.kind\", \"branches\": ["
                + "{ \"when\": [\"narrative\"], \"nodes\": [\"narrative_draft\"], \"ret\": \"node.sections_draft.route.narrative_draft\" }, "
                + "{ \"when\": [\"table\"], \"nodes\": [\"table_draft\"], \"ret\": \"node.sections_draft.route.table_draft\" }, "
                + "{ \"when\": [\"omit\"], \"nodes\": [], \"ret\": null }], \"else\": null")))
            .With("nodes/sections_draft/route/narrative_draft.md", Agent("Drafting a narrative section", Plain,
                "Write the section '{{ item.value.title }}' as prose.\n"))
            .With("nodes/sections_draft/route/table_draft.md", Agent("Drafting a table section", Plain,
                "Write the section '{{ item.value.title }}' as a table.\n"));

        files.With("nodes/patient_summaries/index.json", Json(SourceFixture.Node("for", "Patient summaries", ArrayOf(Plain), false, true,
                "\"in\": \"input.sections.value\", \"parallel\": true, \"maxParallel\": null, \"until\": null, "
                + "\"max\": null, \"itemLabel\": \"title\", \"nodes\": [\"narrative_only\"], \"ret\": \"node.patient_summaries.narrative_only\"")))
            .With("nodes/patient_summaries/narrative_only/index.json", Json(SourceFixture.Node("if", "Narrative sections only", Plain, false, true,
                "\"if\": { \"===\": [{ \"var\": \"item.value.kind\" }, \"narrative\"] }, "
                + "\"then\": { \"nodes\": [\"patient_summary\"], \"ret\": \"node.patient_summaries.narrative_only.patient_summary\" }, "
                + "\"else\": null")))
            .With("nodes/patient_summaries/narrative_only/patient_summary.md", Agent("Summarising for the patient", Plain,
                "Summarise the section '{{ item.value.title }}' for Margaret Okafor in plain words.\n"));

        files.With("nodes/standing_sections/index.json", Json(SourceFixture.Node("for", "Standing sections", ArrayOf(Plain), false, true,
                "\"in\": \"input.sections.value\", \"parallel\": true, \"maxParallel\": null, \"until\": null, "
                + "\"max\": null, \"itemLabel\": \"title\", \"nodes\": [\"applies\"], \"ret\": \"node.standing_sections.applies\"")))
            .With("nodes/standing_sections/applies/index.json", Json(SourceFixture.Node("if",
                "Holding the billing section back unless asked", Plain, false, true,
                "\"if\": { \"or\": [{ \"!==\": [{ \"var\": \"item.id\" }, \"billing\"] }, "
                + "{ \"===\": [{ \"var\": \"input.include_billing.value\" }, true] }] }, "
                + "\"then\": { \"nodes\": [\"standing_draft\"], \"ret\": \"node.standing_sections.applies.standing_draft\" }, "
                + "\"else\": null")))
            .With("nodes/standing_sections/applies/standing_draft.md", Agent("Drafting a standing section", Plain,
                "Write the standing section '{{ item.value.title }}'.\n"));
        return files;
    }

    // ---- Functions -----------------------------------------------------------------------------

    /// <summary>
    /// <c>example-functions</c>: the snippet folder <c>standards</c> (format <c>standard_section</c>), the function
    /// <c>draft_section</c> (parameters <c>standard</c> and <c>draft</c>), the loop <c>standards</c> over the folder whose
    /// body is the call <c>section</c>, the agents <c>extract_input_terms</c> and <c>extract_note_terms</c>, the check loop
    /// <c>section_coverage</c> over <c>node.standards.items</c>, the check <c>all_sections</c>, and the deliverable
    /// <c>deliverable.emr.consult</c> it gates, in the delivery folder <c>emr</c> (after part 04 § 7.11, which has
    /// neither <c>plan</c> nor <c>extract_note_terms</c>).
    /// </summary>
    public static Dictionary<string, byte[]> Functions()
    {
        var files = WithFormats(Minimal(), "standard_section")
            .With("nodes/index.json", Index(["plan", "standards", "extract_input_terms", "extract_note_terms"]))
            .With("nodes/standards/index.json", Json(SourceFixture.Node("for", "Standards", ArrayOf(Plain), false, true,
                "\"in\": \"snippet.standards\", \"parallel\": true, \"maxParallel\": 4, \"until\": null, \"max\": null, "
                + "\"itemLabel\": \"title\", \"nodes\": [\"section\"], \"ret\": \"node.standards.section\"")))
            .With("nodes/standards/section.md", Md(
                SourceFixture.Node("call", "Drafting the section", Plain, false, true,
                    "\"if\": null, \"function\": \"draft_section\", "
                    + "\"with\": { \"standard\": \"item\", \"draft\": \"input.consult_draft.content\" }"),
                ""))
            .With("nodes/extract_input_terms.md", Agent("Extracting terms from the draft", "\"concept_list\"",
                "List the clinical terms in the consult draft.\n\n{{ input.consult_draft.content }}\n"))
            .With("nodes/extract_note_terms.md", Agent("Extracting terms from the note", "\"concept_list\"",
                "List the clinical terms in the sections drafted so far.\n\n{{ node.standards.content }}\n"));

        files.With("snippets/index.json", Index(["standards"]))
            .With("snippets/standards/index.json", Index(["hpi", "assessment_plan"],
                description: "The standard sections of a consult note, in order.", extra: ", \"format\": \"standard_section\""))
            .With("snippets/standards/hpi.md", Md(
                "{ \"title\": \"History of Present Illness\" }",
                "Chronological prose narrative. Open with a one-sentence orientation to the patient.\n"))
            .With("snippets/standards/assessment_plan.md", Md(
                "{ \"title\": \"Assessment and Plan\" }",
                "One assessment, then a numbered plan.\n"));

        files.With("functions/index.json", Index(["draft_section"]))
            .With("functions/draft_section/index.json", Json(
                "{ \"label\": \"Draft a section\", "
                + "\"description\": [\"Drafts one consult section from a standard, then applies the patient's facts.\"], "
                + "\"inputs\": { "
                + "\"standard\": { \"label\": \"Standard\", \"description\": null, \"format\": \"standard_section\" }, "
                + "\"draft\": { \"label\": \"Consult draft\", \"description\": null, \"format\": \"markdown_block\" } }, "
                + "\"uses\": { \"snippets\": [], \"macros\": [], \"formats\": [\"standard_section\"], \"renderers\": [] }, "
                + "\"nodes\": [\"standard_draft\", \"patient_draft\", \"apply_instructions\"], "
                + "\"ret\": \"node.apply_instructions\", \"format\": \"plaintext\" }"))
            .With("functions/draft_section/standard_draft.md", Md(
                SourceFixture.Node("agent", "Drafting from the standard", Plain, false, true,
                    "\"if\": { \"!\": [{ \"blank\": [{ \"var\": \"param.standard.value.title\" }] }] }, "
                    + "\"onInvalid\": \"retry\", \"failIfEmpty\": null, \"parts\": null, \"ordered\": false"),
                "Draft the section '{{ param.standard.value.title }}' following these instructions.\n\n"
                + "{{ param.standard.content }}\n"))
            .With("functions/draft_section/patient_draft.md", Agent("Drafting from the patient's facts", Plain,
                "Using the consult draft below, write what it says about Margaret Okafor for this section.\n\n"
                + "{{ param.draft.content }}\n"))
            .With("functions/draft_section/apply_instructions.md", Agent("Applying the instructions", Plain,
                "Combine the two drafts into one section.\n\n{{ node.standard_draft.content }}\n\n{{ node.patient_draft.content }}\n"));
        foreach (var folder in new[] { "snippets", "macros", "formats", "renderers" })
            files.With($"functions/draft_section/{folder}/index.json", Index([]));

        files.With("checks/index.json", Index(["section_coverage", "all_sections"]))
            .With("checks/section_coverage/index.json", Json(SourceFixture.Node("for", "Section coverage", ArrayOf("check_result"), true, true,
                "\"in\": \"node.standards.items\", \"parallel\": true, \"maxParallel\": null, \"until\": null, "
                + "\"max\": null, \"itemLabel\": \"label\", \"nodes\": [\"coverage\"], "
                + "\"ret\": \"check.section_coverage.coverage\"")))
            .With("checks/section_coverage/coverage.md", Md(
                SourceFixture.Node("computed", "Terms covered", "\"check_result\"", true, true,
                    "\"if\": { \"===\": [{ \"var\": \"item.state\" }, \"completed\"] }, "
                    + "\"expression\": { \"terms_subset\": [{ \"var\": \"node.extract_input_terms.value\" }, "
                    + "{ \"var\": \"node.extract_note_terms.value\" }, "
                    + "\"The section does not cover every clinical term found in the draft.\"] }"),
                ""))
            .With("checks/all_sections.md", Md(
                SourceFixture.Node("computed", "All sections covered", "\"check_result\"", true, true,
                    "\"if\": null, \"expression\": { \"all_pass\": [{ \"var\": \"check.section_coverage.items\" }, "
                    + "\"Not every section covers its planned terms.\"] }"),
                ""));

        // § 7.11's deliverable: deliverable.emr.consult, gated by the check all_sections, in the delivery folder emr.
        files.Without("deliverables/note.md")
            .With("deliverables/index.json", Index(["emr"], extra: ", \"delivery\": [], \"bundle\": null"))
            .With("deliverables/emr/index.json", Index(["consult"], extra: ", \"delivery\": [\"emr\", \"email\"], \"bundle\": \"zip\""))
            .With("deliverables/emr/consult.md", Md(
                Deliverable("Consult note", "\"markdown_block\"", "\"check.all_sections\"", "hold"),
                "## Sections\n\n{{ node.standards.content }}\n"));
        return files;
    }

    // ---- Checks reading a deliverable (#1505, area 08; D1 to D3) ---------------------------------

    /// <summary>
    /// A check of <paramref name="label"/> whose <c>pass</c> is <paramref name="pass"/> (a JSON Logic boolean), built with
    /// <c>object</c>; <paramref name="modelDerived"/> as its header declares it.
    /// </summary>
    internal static string ObjectCheck(string label, string pass, bool modelDerived = true) =>
        Md(SourceFixture.Node("computed", label, "\"check_result\"", true, modelDerived,
            $"\"if\": null, \"expression\": {{ \"object\": {{ \"pass\": {pass}, \"message\": null, \"uncovered\": null, "
            + "\"untested\": null, \"passed\": null, \"total\": null } }"), "");

    /// <summary>A JSON Logic test that <paramref name="path"/> (a deliverable's <c>.content</c>) is not blank.</summary>
    internal static string NotBlank(string path) => $"{{ \"!\": [{{ \"blank\": [{{ \"var\": \"{path}\" }}] }}] }}";

    /// <summary>
    /// <see cref="Functions"/> with the consult note gated by the check <c>proofread</c> instead, which reads the note's
    /// <c>.content</c> (not blank), the check <c>consult_words</c> (the note has words: an aggregate the gate reads, which
    /// reads the note too) and <c>all_sections</c>' pass.
    /// </summary>
    public static Dictionary<string, byte[]> Proofread()
    {
        var files = Functions()
            .With("checks/index.json", Index(["section_coverage", "all_sections", "consult_words", "proofread"]))
            .With("checks/consult_words.md", ObjectCheck("The note has words",
                "{ \">\": [{ \"words\": [{ \"var\": \"deliverable.emr.consult.content\" }] }, 0] }"))
            .With("checks/proofread.md", ObjectCheck("Proofreading the note",
                $"{{ \"and\": [{NotBlank("deliverable.emr.consult.content")}, {{ \"var\": \"check.consult_words.value.pass\" }}, "
                + "{ \"var\": \"check.all_sections.value.pass\" }] }"));
        return files.WithJson("deliverables/emr/consult.md", h => h["check"] = "check.proofread");
    }

    /// <summary><paramref name="files"/> with the check <paramref name="id"/> listed last in <c>checks/</c>, its file <paramref name="file"/>.</summary>
    internal static Dictionary<string, byte[]> WithCheck(Dictionary<string, byte[]> files, string id, string file) =>
        files.WithJson("checks/index.json", index => index["items"]!.AsArray().Add(id)).With($"checks/{id}.md", file);

    /// <summary>
    /// <see cref="Functions"/> with a second note, <c>deliverable.emr.summary</c>, and two gates: the consult note's
    /// <c>gate_a</c> reads <paramref name="aReads"/>, the summary's <c>gate_b</c> reads <paramref name="bReads"/> (each a
    /// deliverable's <c>.content</c> it must not find blank).
    /// </summary>
    internal static Dictionary<string, byte[]> TwoGates(string aReads, string bReads)
    {
        var files = Functions()
            .WithJson("deliverables/emr/index.json", index => index["items"]!.AsArray().Add("summary"))
            .With("deliverables/emr/summary.md", Md(
                Deliverable("Summary note", "\"markdown_block\"", "\"check.gate_b\"", "hold"), "## Summary\n\n{{ node.standards.content }}\n"))
            .WithJson("deliverables/emr/consult.md", h => h["check"] = "check.gate_a");
        files = WithCheck(files, "gate_a", ObjectCheck("Gating the consult note", NotBlank(aReads)));
        return WithCheck(files, "gate_b", ObjectCheck("Gating the summary", NotBlank(bReads)));
    }

    /// <summary>
    /// <see cref="Proofread"/> whose gate reads the note and the production node <c>noted</c> (in place of the check
    /// <c>consult_words</c>), and <c>noted</c> reads <c>consult_words</c>: a check the gate reaches only through a node,
    /// so outside the note's gate set (D3), waiting for the gate that waits for it.
    /// </summary>
    internal static Dictionary<string, byte[]> GateThroughANode() =>
        Proofread()
            .WithJson("nodes/index.json", index => index["items"]!.AsArray().Add("noted"))
            .With("nodes/noted.md", Md(SourceFixture.Node("computed", "Whether the note has words", "\"boolean\"", true, true,
                "\"if\": null, \"expression\": { \"var\": \"check.consult_words.value.pass\" }"), ""))
            .With("checks/proofread.md", ObjectCheck("Proofreading the note",
                $"{{ \"and\": [{NotBlank("deliverable.emr.consult.content")}, {{ \"var\": \"node.noted.value\" }}] }}"));

    /// <summary>
    /// <see cref="Proofread"/> with an <c>if</c> and a <c>case</c> under <c>checks/</c>, each deciding on the note's
    /// <c>.state</c> (D1: a flow node's rule and subject there read a deliverable): <c>when_released</c> holds the check
    /// <c>fine</c>, <c>by_state</c> routes <c>released</c> to <c>ok</c> and anything else to <c>other</c>.
    /// </summary>
    internal static Dictionary<string, byte[]> FlowReadingState()
    {
        const string Result = "\"check_result\"";
        var files = Proofread()
            .WithJson("checks/index.json", index =>
            {
                index["items"]!.AsArray().Add("when_released");
                index["items"]!.AsArray().Add("by_state");
            })
            .With("checks/when_released/index.json", Json(SourceFixture.Node("if", "When the note went out", Result, true, false,
                "\"if\": { \"===\": [{ \"var\": \"deliverable.emr.consult.state\" }, \"released\"] }, "
                + "\"then\": { \"nodes\": [\"fine\"], \"ret\": \"check.when_released.fine\" }, \"else\": null")))
            .With("checks/when_released/fine.md", ObjectCheck("Fine", "true", modelDerived: false))
            .With("checks/by_state/index.json", Json(SourceFixture.Node("case", "By the note's state", Result, true, false,
                "\"case\": \"deliverable.emr.consult.state\", \"branches\": ["
                + "{ \"when\": [\"released\"], \"nodes\": [\"ok\"], \"ret\": \"check.by_state.ok\" }], "
                + "\"else\": { \"nodes\": [\"other\"], \"ret\": \"check.by_state.other\" }")))
            .With("checks/by_state/ok.md", ObjectCheck("Released", "true", modelDerived: false))
            .With("checks/by_state/other.md", ObjectCheck("Not released", "false", modelDerived: false));
        return files;
    }

    // ---- CheckedNote ---------------------------------------------------------------------------

    /// <summary>
    /// <c>example-checked-note</c>, part 06 § 3.4's package cut down: the agent <c>plan</c> (a <c>section_plan</c>), the
    /// loop <c>sections</c> over its sections (each with its planned terms, its draft and the terms the draft covers), the
    /// check loop <c>part_coverage</c> over <c>node.sections.items</c>, the aggregate <c>all_sections_covered</c> over its
    /// <c>.items</c>, and the check <c>proofread</c> gating the note: the note's <c>.content</c> is not blank and every
    /// section is covered. The note (<c>markdown_block</c>, in <c>deliverables/</c>, delivered to the EMR) and the
    /// ungated patient letter (<c>plaintext</c>, in <c>deliverables/patient</c>, delivered by email and PDF, held when
    /// empty). Fictional (Margaret Okafor, Dr A. Sample).
    /// </summary>
    public static Dictionary<string, byte[]> CheckedNote()
    {
        var files = WithFormats(Minimal(), "section_plan")
            .With("nodes/index.json", Index(["plan", "sections"]))
            .With("nodes/plan.md", Agent("Planning the sections", "\"section_plan\"",
                "Plan the sections of Margaret Okafor's consultation note for the draft below, one section for each heading.\n\n"
                + "{{ input.consult_draft.content }}\n"))
            .With("nodes/sections/index.json", Json(SourceFixture.Node("for", "Sections", ArrayOf(Plain), false, true,
                "\"in\": \"node.plan.value.sections\", \"parallel\": true, \"maxParallel\": null, \"until\": null, "
                + "\"max\": null, \"itemLabel\": \"title\", \"nodes\": [\"planned_terms\", \"draft\", \"section_terms\"], "
                + "\"ret\": \"node.sections.draft\"")))
            .With("nodes/sections/planned_terms.md", Agent("The terms the plan names", "\"concept_list\"",
                "List the clinical terms the plan names for the section '{{ item.value.title }}'.\n"))
            .With("nodes/sections/draft.md", Agent("Drafting the section", Plain,
                "Write the section '{{ item.value.title }}' of the note for Dr A. Sample.\n\n{{ input.consult_draft.content }}\n"))
            .With("nodes/sections/section_terms.md", Agent("The terms the draft covers", "\"concept_list\"",
                "List the clinical terms this section covers.\n\n{{ node.draft.content }}\n"));

        files.With("checks/index.json", Index(["part_coverage", "all_sections_covered", "proofread"]))
            .With("checks/part_coverage/index.json", Json(SourceFixture.Node("for", "Section coverage", ArrayOf("check_result"), true, true,
                "\"in\": \"node.sections.items\", \"parallel\": true, \"maxParallel\": null, \"until\": null, "
                + "\"max\": null, \"itemLabel\": \"label\", \"nodes\": [\"coverage\"], \"ret\": \"check.part_coverage.coverage\"")))
            .With("checks/part_coverage/coverage.md", Md(
                SourceFixture.Node("computed", "Section covers its planned terms", "\"check_result\"", true, true,
                    "\"if\": { \"===\": [{ \"var\": \"item.state\" }, \"completed\"] }, "
                    + "\"expression\": { \"terms_subset\": [{ \"var\": \"item.nodes.planned_terms.value\" }, "
                    + "{ \"var\": \"item.nodes.section_terms.value\" }, \"The section does not cover every term its plan names.\"] }"),
                ""))
            .With("checks/all_sections_covered.md", Md(
                SourceFixture.Node("computed", "Every section covers its planned terms", "\"check_result\"", true, true,
                    "\"if\": null, \"expression\": { \"all_pass\": [{ \"var\": \"check.part_coverage.items\" }, "
                    + "\"Not every section covers its planned terms.\"] }"),
                ""))
            .With("checks/proofread.md", ObjectCheck("Proofreading the note",
                $"{{ \"and\": [{NotBlank("deliverable.note.content")}, {{ \"var\": \"check.all_sections_covered.value.pass\" }}] }}"));

        files.Without("deliverables/note.md")
            .With("deliverables/index.json", Index(["note", "patient"], extra: ", \"delivery\": [\"emr\"], \"bundle\": null"))
            .With("deliverables/note.md", Md(
                Deliverable("Consultation note", "\"markdown_block\"", "\"check.proofread\"", "fail"),
                "## Sections\n\n{{ node.sections.content }}\n"))
            .With("deliverables/patient/index.json", Index(["patient_letter"], extra: ", \"delivery\": [\"email\", \"pdf\"], \"bundle\": null"))
            .With("deliverables/patient/patient_letter.md", Md(
                Deliverable("Letter to the patient", Plain, "null", "hold"),
                "Dear Margaret Okafor,\n\nThis is what we talked about at your visit.\n\n{{ node.sections.content }}\n\nDr A. Sample\n"));
        return files;
    }

    // ---- Formats -------------------------------------------------------------------------------

    private static readonly Dictionary<string, string> FormatFiles = new(StringComparer.Ordinal)
    {
        ["section_plan"] =
            "{ \"$schema\": \"urn:consultologist:spec:31:dialect\", \"type\": \"object\", \"required\": [\"sections\"], "
            + "\"additionalProperties\": false, \"properties\": { \"sections\": { \"type\": \"array\", \"items\": { "
            + "\"type\": \"object\", \"required\": [\"title\", \"kind\"], \"additionalProperties\": false, "
            + "\"properties\": { \"title\": { \"$ref\": \"plaintext\" }, "
            + "\"kind\": { \"type\": \"string\", \"enum\": [\"narrative\", \"table\", \"omit\"] } } } } } }",
        ["outline"] =
            "{ \"$schema\": \"urn:consultologist:spec:31:dialect\", \"type\": \"object\", \"required\": [\"points\"], "
            + "\"additionalProperties\": false, \"properties\": { \"points\": { \"type\": \"array\", "
            + "\"items\": { \"$ref\": \"plaintext\" } } } }",
        ["verdict"] =
            "{ \"$schema\": \"urn:consultologist:spec:31:dialect\", \"type\": \"object\", \"required\": [\"verdict\"], "
            + "\"additionalProperties\": false, \"properties\": { "
            + "\"verdict\": { \"type\": \"string\", \"enum\": [\"good\", \"revise\"] } } }",
        ["standard_section"] =
            "{ \"$schema\": \"urn:consultologist:spec:31:dialect\", \"type\": \"object\", \"required\": [\"title\"], "
            + "\"additionalProperties\": false, \"properties\": { \"title\": { \"$ref\": \"plaintext\" } } }",
        ["section_request"] =
            "{ \"$schema\": \"urn:consultologist:spec:31:dialect\", \"type\": \"object\", \"required\": [\"title\", \"kind\"], "
            + "\"additionalProperties\": false, \"properties\": { \"title\": { \"type\": \"string\", \"title\": \"Title\" }, "
            + "\"kind\": { \"type\": \"string\", \"enum\": [\"narrative\", \"table\", \"omit\"], \"title\": \"Kind\" } } }",
    };

    /// <summary><paramref name="files"/> with the package formats <paramref name="ids"/> (listed in that order).</summary>
    internal static Dictionary<string, byte[]> WithFormats(Dictionary<string, byte[]> files, params string[] ids)
    {
        files.With("formats/index.json", Index(ids));
        foreach (var id in ids)
            files.With($"formats/{id}.json", Json(FormatFiles[id]));
        return files;
    }
}
