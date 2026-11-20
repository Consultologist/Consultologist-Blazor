using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Source;

/// <summary>
/// #1499 (specVersion 31, area 02): the valid package every test of the source reader edits, as
/// the upload a reader receives (path to bytes). Every member of every declaration is written,
/// as part 01 § 5 requires; the shapes follow the published schemas under
/// <c>external/consultologist-package-format/v31/schemas</c>, and the content is fictional
/// (Margaret Okafor, Dr A. Sample). Files are listed in a fixed order, so a failing test reads
/// the same every time.
/// </summary>
/// <remarks>
/// <see cref="Minimal"/> is the smallest package: a manifest, the nine folders, one agent node and
/// one deliverable. <see cref="Full"/> is modelled on part 04 § 7.11's <c>example-functions</c> and
/// part 01 § 3's trees, and holds one of everything. The helpers edit the dictionary they are
/// given and return it, so a test can chain them or call them one by one.
/// </remarks>
internal static class SourceFixture
{
    private static readonly JsonSerializerOptions Relaxed = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions RelaxedIndented = new(Relaxed) { WriteIndented = true };

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private const string Plain = "\"plaintext\"";

    private const string AgentTail =
        "\"if\": null, \"onInvalid\": \"retry\", \"failIfEmpty\": null, \"parts\": null, \"ordered\": false";

    // ---- Helpers a test uses -------------------------------------------------------------------

    /// <summary>The bytes of <paramref name="text"/> as UTF-8 with no byte-order mark.</summary>
    public static byte[] Utf8(string text) => Utf8NoBom.GetBytes(text);

    /// <summary>The text of the file at <paramref name="path"/>.</summary>
    public static string Text(IReadOnlyDictionary<string, byte[]> files, string path) =>
        Utf8NoBom.GetString(files[path]);

    /// <summary>Sets (adds or replaces) the file at <paramref name="path"/>; returns <paramref name="files"/>.</summary>
    public static Dictionary<string, byte[]> With(this Dictionary<string, byte[]> files, string path, string text)
    {
        files[path] = Utf8(text);
        return files;
    }

    /// <summary>Removes the file at <paramref name="path"/>; returns <paramref name="files"/>.</summary>
    public static Dictionary<string, byte[]> Without(this Dictionary<string, byte[]> files, string path)
    {
        files.Remove(path);
        return files;
    }

    /// <summary>
    /// Edits the JSON object at <paramref name="path"/>: the whole of a plain JSON file, or the
    /// header of a <c>.md</c> file, whose body is kept as it was. Returns <paramref name="files"/>.
    /// </summary>
    public static Dictionary<string, byte[]> WithJson(
        this Dictionary<string, byte[]> files, string path, Action<JsonObject> edit)
    {
        var text = Text(files, path);
        if (path.EndsWith(".md", StringComparison.Ordinal))
        {
            var split = FrontMatter.Split(text)
                ?? throw new InvalidOperationException($"{path} has no front matter to edit.");
            var header = JsonNode.Parse(split.Header)!.AsObject();
            edit(header);
            return files.With(path, Md(header.ToJsonString(Relaxed), split.Body));
        }

        var json = JsonNode.Parse(text)!.AsObject();
        edit(json);
        return files.With(path, json.ToJsonString(RelaxedIndented) + "\n");
    }

    // ---- Minimal -------------------------------------------------------------------------------

    /// <summary>A manifest, the nine folders with empty indexes except one agent node and one deliverable.</summary>
    public static Dictionary<string, byte[]> Minimal()
    {
        var files = new Dictionary<string, byte[]>();
        files.With("manifest.json", ManifestJson(
            "example-minimal", "Minimal note demo", ["Consult draft"], ["consult_draft"]));
        files.With("nodes/index.json", Index(["plan"]));
        files.With("nodes/plan.md", Agent("Planning the sections", Plain,
            "Plan the sections of a consultation note for the draft below.\n\n{{ input.consult_draft.content }}\n"));
        AddEmptyIndexes(files, "checks", "functions", "renderers", "snippets", "macros", "formats");
        files.With("deliverables/index.json", Index(["note"], extra: ", \"delivery\": [], \"bundle\": null"));
        files.With("deliverables/note.md", Md(
            Deliverable("Consultation note", Plain, "null", "hold"),
            "{{ node.plan.content }}\n"));
        AddEmptyIndexes(files, "tests");
        return files;
    }

    // ---- Full ----------------------------------------------------------------------------------

    /// <summary>
    /// Every kind of file: nodes of each kind, a <c>for</c> inside a <c>for</c>, a <c>while</c>,
    /// an <c>if</c>, a <c>case</c> with an empty branch, a function with a <c>call</c>, a check
    /// loop, a snippet folder with a format, a macro folder, a renderer, a delivery folder, a
    /// test and a <c>README.md</c>.
    /// </summary>
    public static Dictionary<string, byte[]> Full()
    {
        var files = new Dictionary<string, byte[]>();
        files.With("manifest.json", ManifestJson(
            "example-full", "Full consult note demo",
            ["Consult draft", "Include the billing section"], ["consult_draft", "include_billing"]));
        files.With("README.md",
            "# example-full\n\nA fictional package that uses every kind of file once. Its patient is Margaret Okafor,\n"
            + "seen by Dr A. Sample; nothing here is clinical advice.\n");
        AddNodes(files);
        AddChecks(files);
        AddFunctions(files);
        AddRenderers(files);
        AddSnippets(files);
        AddMacros(files);
        AddFormats(files);
        AddDeliverables(files);
        AddTests(files);
        return files;
    }

    private static void AddNodes(Dictionary<string, byte[]> files)
    {
        files.With("nodes/index.json", Index([
            "plan", "section_count", "sections", "sections_draft", "refine", "billing_gate",
            "standards", "extract_input_terms", "extract_note_terms"]));

        files.With("nodes/plan.md", Agent("Planning the sections", "\"section_plan\"",
            "Plan the sections of a consultation note for the draft below, one section for each heading.\n\n"
            + "{{ input.consult_draft.content }}\n"));
        files.With("nodes/section_count.md", Md(
            Node("computed", "Counting the sections", "\"number\"", true, true,
                "\"if\": null, \"expression\": { \"size\": [{ \"var\": \"node.plan.value.sections\" }] }"),
            ""));

        // A for inside a for.
        files.With("nodes/sections/index.json", Json(Node("for", "Sections", Nested(Plain), false, true,
            "\"in\": \"node.plan.value.sections\", \"parallel\": true, \"maxParallel\": null, \"until\": null, "
            + "\"max\": null, \"itemLabel\": \"title\", \"nodes\": [\"outline\", \"paragraphs\"], "
            + "\"ret\": \"node.sections.paragraphs\"")));
        files.With("nodes/sections/outline.md", Agent("Outlining the section", "\"outline\"",
            "Outline the section '{{ item.value.title }}' of the note as a short list of points.\n"));
        files.With("nodes/sections/paragraphs/index.json", Json(Node("for", "Paragraphs", ArrayOf(Plain), false, true,
            "\"in\": \"node.outline.value.points\", \"parallel\": true, \"maxParallel\": null, \"until\": null, "
            + "\"max\": null, \"itemLabel\": null, \"nodes\": [\"draft_paragraph\"], "
            + "\"ret\": \"node.sections.paragraphs.draft_paragraph\"")));
        files.With("nodes/sections/paragraphs/draft_paragraph.md", Agent("Drafting a paragraph", Plain,
            "Write one paragraph of the section '{{ item.parent.value.title }}' about this point: {{ item.value }}\n"));

        // A for over the plan's sections, routing each by its kind with a case (one branch empty).
        files.With("nodes/sections_draft/index.json", Json(Node("for", "Sections: drafting each by its kind",
            ArrayOf(Plain), false, true,
            "\"in\": \"node.plan.value.sections\", \"parallel\": true, \"maxParallel\": null, \"until\": null, "
            + "\"max\": null, \"itemLabel\": \"title\", \"nodes\": [\"route\"], "
            + "\"ret\": \"node.sections_draft.route\"")));
        files.With("nodes/sections_draft/route/index.json", Json(Node("case", "Routing a section by its kind", Plain, false, true,
            "\"case\": \"item.value.kind\", \"branches\": ["
            + "{ \"when\": [\"narrative\"], \"nodes\": [\"narrative_draft\"], \"ret\": \"node.sections_draft.route.narrative_draft\" }, "
            + "{ \"when\": [\"table\"], \"nodes\": [\"table_draft\"], \"ret\": \"node.sections_draft.route.table_draft\" }, "
            + "{ \"when\": [\"omit\"], \"nodes\": [], \"ret\": null }], \"else\": null")));
        files.With("nodes/sections_draft/route/narrative_draft.md", Agent("Drafting a narrative section", Plain,
            "Write the section '{{ item.value.title }}' as prose.\n"));
        files.With("nodes/sections_draft/route/table_draft.md", Agent("Drafting a table section", Plain,
            "Write the section '{{ item.value.title }}' as a table.\n"));

        // A while.
        files.With("nodes/refine/index.json", Json(Node("while", "Refining the summary", Plain, false, true,
            "\"while\": { \"!==\": [{ \"var\": \"node.judge.value.verdict\" }, \"good\"] }, \"max\": 3, "
            + "\"until\": null, \"checkFirst\": false, \"nodes\": [\"draft_summary\", \"judge\"], "
            + "\"ret\": \"node.refine.draft_summary\"")));
        files.With("nodes/refine/draft_summary.md", Agent("Drafting the summary", Plain,
            "Summarise the note in two sentences for Dr A. Sample.\n"));
        files.With("nodes/refine/judge.md", Agent("Judging the summary", "\"verdict\"",
            "Say whether the summary is good or needs another pass.\n\n{{ node.draft_summary.content }}\n"));

        // An if, with a rendered node in its branch.
        files.With("nodes/billing_gate/index.json", Json(Node("if", "Holding the billing text back unless asked", Plain, true, false,
            "\"if\": { \"===\": [{ \"var\": \"input.include_billing.value\" }, true] }, "
            + "\"then\": { \"nodes\": [\"billing_text\"], \"ret\": \"node.billing_gate.billing_text\" }, "
            + "\"else\": null")));
        files.With("nodes/billing_gate/billing_text.md", Md(
            Node("rendered", "Billing text", Plain, true, false, "\"if\": null"),
            "Consultation with Margaret Okafor, 45 minutes.\n"));

        // A for over a snippet folder whose body is a call.
        files.With("nodes/standards/index.json", Json(Node("for", "Standards", ArrayOf(Plain), false, true,
            "\"in\": \"snippet.standards\", \"parallel\": true, \"maxParallel\": 4, \"until\": null, \"max\": null, "
            + "\"itemLabel\": \"title\", \"nodes\": [\"section\"], \"ret\": \"node.standards.section\"")));
        files.With("nodes/standards/section.md", Md(
            Node("call", "Drafting the section", Plain, false, true,
                "\"if\": null, \"function\": \"draft_section\", "
                + "\"with\": { \"standard\": \"item\", \"draft\": \"input.consult_draft.content\" }"),
            ""));

        files.With("nodes/extract_input_terms.md", Agent("Extracting terms from the draft", "\"concept_list\"",
            "List the clinical terms in the consult draft.\n\n{{ input.consult_draft.content }}\n"));
        files.With("nodes/extract_note_terms.md", Agent("Extracting terms from the note", "\"concept_list\"",
            "List the clinical terms in the sections drafted so far.\n\n{{ node.sections_draft.content }}\n"));
    }

    private static void AddChecks(Dictionary<string, byte[]> files)
    {
        files.With("checks/index.json", Index(["section_coverage", "all_sections"]));
        files.With("checks/section_coverage/index.json", Json(Node("for", "Section coverage", ArrayOf("check_result"), true, true,
            "\"in\": \"node.standards.items\", \"parallel\": true, \"maxParallel\": null, \"until\": null, "
            + "\"max\": null, \"itemLabel\": \"label\", \"nodes\": [\"coverage\"], "
            + "\"ret\": \"check.section_coverage.coverage\"")));
        files.With("checks/section_coverage/coverage.md", Md(
            Node("computed", "Terms covered", "\"check_result\"", true, true,
                "\"if\": { \"===\": [{ \"var\": \"item.state\" }, \"completed\"] }, "
                + "\"expression\": { \"terms_subset\": [{ \"var\": \"node.extract_input_terms.value\" }, "
                + "{ \"var\": \"node.extract_note_terms.value\" }, "
                + "\"The section does not cover every clinical term found in the draft.\"] }"),
            ""));
        files.With("checks/all_sections.md", Md(
            Node("computed", "All sections covered", "\"check_result\"", true, true,
                "\"if\": null, \"expression\": { \"all_pass\": [{ \"var\": \"check.section_coverage.items\" }, "
                + "\"Not every section covers its planned terms.\"] }"),
            ""));
    }

    private static void AddFunctions(Dictionary<string, byte[]> files)
    {
        files.With("functions/index.json", Index(["draft_section"]));
        files.With("functions/draft_section/index.json", Json(
            "{ \"label\": \"Draft a section\", "
            + "\"description\": [\"Drafts one consult section from a standard, then applies the patient's facts.\"], "
            + "\"inputs\": { "
            + "\"standard\": { \"label\": \"Standard\", \"description\": null, \"format\": \"standard_section\" }, "
            + "\"draft\": { \"label\": \"Consult draft\", \"description\": null, \"format\": \"markdown_block\" } }, "
            + "\"uses\": { \"snippets\": [], \"macros\": [], \"formats\": [\"standard_section\"], \"renderers\": [] }, "
            + "\"nodes\": [\"standard_draft\", \"patient_draft\", \"apply_instructions\"], "
            + "\"ret\": \"node.apply_instructions\", \"format\": \"plaintext\" }"));
        files.With("functions/draft_section/standard_draft.md", Agent("Drafting from the standard", Plain,
            "Draft the section '{{ param.standard.value.title }}' following these instructions.\n\n"
            + "{{ param.standard.content }}\n"));
        files.With("functions/draft_section/patient_draft.md", Agent("Drafting from the patient's facts", Plain,
            "Using the consult draft below, write what it says about Margaret Okafor for this section.\n\n"
            + "{{ param.draft.content }}\n"));
        files.With("functions/draft_section/apply_instructions.md", Agent("Applying the instructions", Plain,
            "Combine the two drafts into one section.\n\n{{ node.standard_draft.content }}\n\n{{ node.patient_draft.content }}\n"));
        AddEmptyIndexes(files, "functions/draft_section/snippets", "functions/draft_section/macros",
            "functions/draft_section/formats", "functions/draft_section/renderers");
    }

    private static void AddRenderers(Dictionary<string, byte[]> files)
    {
        files.With("renderers/index.json", Index(["term_bullets"]));
        files.With("renderers/term_bullets.md", Md(
            "{ \"label\": \"Terms as bullets\", \"kind\": \"rendered\", "
            + "\"description\": [\"One bullet for each term.\"], \"input\": \"concept_list\", \"format\": \"markdown_block\" }",
            "{{~ for c in value.concepts ~}}\n- {{ c.term }}\n{{~ end ~}}\n"));
    }

    private static void AddSnippets(Dictionary<string, byte[]> files)
    {
        files.With("snippets/index.json", Index(["closing_line", "standards"]));
        files.With("snippets/closing_line.md", Md(
            "{ \"label\": \"Closing line\", \"description\": null, \"if\": null, \"format\": \"plaintext\" }",
            "Thank you for seeing Margaret Okafor.\n"));
        files.With("snippets/standards/index.json", Index(["hpi", "assessment_plan"],
            description: "The standard sections of a consult note, in order.", extra: ", \"format\": \"standard_section\""));
        files.With("snippets/standards/hpi.md", Md(
            "{ \"title\": \"History of Present Illness\" }",
            "Chronological prose narrative. Open with a one-sentence orientation to the patient.\n"));
        files.With("snippets/standards/assessment_plan.md", Md(
            "{ \"title\": \"Assessment and Plan\" }",
            "One assessment, then a numbered plan.\n"));
    }

    private static void AddMacros(Dictionary<string, byte[]> files)
    {
        files.With("macros/index.json", Index(["follow_up", "phrases"]));
        files.With("macros/follow_up.md", Md(
            MacroHeader("Follow-up sentence", "null",
                "[\"Place {{ macro.follow_up.content }} at the end of the plan.\"]"),
            "Follow up with Dr A. Sample in six weeks.\n"));
        files.With("macros/phrases/index.json", Index(["thanks"], extra: ", \"if\": null"));
        files.With("macros/phrases/thanks.md", Md(
            MacroHeader("Thanks", "null",
                "[\"Place {{ macro.phrases.thanks.content }} before the signature.\"]"),
            "Thank you for the referral.\n"));
    }

    private static void AddFormats(Dictionary<string, byte[]> files)
    {
        files.With("formats/index.json", Index(["section_plan", "outline", "verdict", "standard_section"]));
        files.With("formats/section_plan.json", Json(
            "{ \"$schema\": \"urn:consultologist:spec:31:dialect\", \"type\": \"object\", \"required\": [\"sections\"], "
            + "\"additionalProperties\": false, \"properties\": { \"sections\": { \"type\": \"array\", \"items\": { "
            + "\"type\": \"object\", \"required\": [\"title\", \"kind\"], \"additionalProperties\": false, "
            + "\"properties\": { \"title\": { \"$ref\": \"plaintext\" }, "
            + "\"kind\": { \"type\": \"string\", \"enum\": [\"narrative\", \"table\", \"omit\"] } } } } } }"));
        files.With("formats/outline.json", Json(
            "{ \"$schema\": \"urn:consultologist:spec:31:dialect\", \"type\": \"object\", \"required\": [\"points\"], "
            + "\"additionalProperties\": false, \"properties\": { \"points\": { \"type\": \"array\", "
            + "\"items\": { \"$ref\": \"plaintext\" } } } }"));
        files.With("formats/verdict.json", Json(
            "{ \"$schema\": \"urn:consultologist:spec:31:dialect\", \"type\": \"object\", \"required\": [\"verdict\"], "
            + "\"additionalProperties\": false, \"properties\": { "
            + "\"verdict\": { \"type\": \"string\", \"enum\": [\"good\", \"revise\"] } } }"));
        files.With("formats/standard_section.json", Json(
            "{ \"$schema\": \"urn:consultologist:spec:31:dialect\", \"type\": \"object\", \"required\": [\"title\"], "
            + "\"additionalProperties\": false, \"properties\": { \"title\": { \"$ref\": \"plaintext\" } } }"));
    }

    private static void AddDeliverables(Dictionary<string, byte[]> files)
    {
        files.With("deliverables/index.json", Index(["note", "patient"],
            extra: ", \"delivery\": [\"emr\"], \"bundle\": null"));
        files.With("deliverables/note.md", Md(
            Deliverable("Consultation note", "\"markdown_block\"", "\"check.all_sections\"", "hold"),
            "## Summary\n\n{{ node.refine.content }}\n"));
        files.With("deliverables/patient/index.json", Index(["letter"],
            extra: ", \"delivery\": [\"email\"], \"bundle\": null"));
        files.With("deliverables/patient/letter.md", Md(
            Deliverable("Letter to the patient", Plain, "null", "release"),
            "Dear Ms Okafor,\n\n{{ node.refine.content }}\n\nDr A. Sample\n"));
    }

    private static void AddTests(Dictionary<string, byte[]> files)
    {
        files.With("tests/index.json", Index(["note_basic"]));
        files.With("tests/note_basic.json", Json(
            "{ \"description\": [\"One planned section; the note is released.\"], "
            + "\"inputs\": { \"consult_draft\": \"Margaret Okafor, 58, referred for fatigue and a raised TSH (fictional).\", "
            + "\"include_billing\": false }, "
            + "\"profile\": { \"signature\": \"Dr A. Sample\" }, "
            + "\"run\": { \"date\": { \"value\": \"2026-11-16\", \"timeZone\": \"America/Toronto\" } }, "
            + "\"outputs\": { \"node.plan\": { \"sections\": [{ \"title\": \"History\", \"kind\": \"narrative\" }] } }, "
            + "\"expect\": { \"ran\": [\"node.plan\"] } }"));
    }

    // ---- Builders ------------------------------------------------------------------------------

    /// <summary>A file with front matter: the fence, the header, the fence, the body (part 01 § 4).</summary>
    private static string Md(string header, string body) => $"---\n{header}\n---\n{body}";

    /// <summary>A plain JSON file: <paramref name="json"/> and the final line end.</summary>
    private static string Json(string json) => json + "\n";

    /// <summary>
    /// An <c>index.json</c>: <c>description</c> (<c>null</c> when none) and <c>items</c>, then any
    /// <paramref name="extra"/> members written as <c>, "name": value</c>.
    /// </summary>
    private static string Index(IEnumerable<string> items, string? description = null, string extra = "") =>
        Json("{ \"description\": " + (description == null ? "null" : "[" + Quote(description) + "]")
            + ", \"items\": [" + string.Join(", ", items.Select(Quote)) + "]" + extra + " }");

    private static void AddEmptyIndexes(Dictionary<string, byte[]> files, params string[] folders)
    {
        foreach (var folder in folders)
        {
            files.With($"{folder}/index.json", folder == "deliverables"
                ? Index([], extra: ", \"delivery\": [], \"bundle\": null")
                : Index([]));
        }
    }

    private static string ManifestJson(string name, string title, string[] labels, string[] ids)
    {
        var inputs = string.Join(",\n", ids.Select((id, i) =>
            $"    {Quote(id)}: {{ \"label\": {Quote(labels[i])}, \"description\": null, "
            + $"\"format\": {(id == "include_billing" ? "\"boolean\"" : "\"markdown_block\"")}, "
            + $"\"required\": {(id == "include_billing" ? "false" : "true")}, \"channel\": null }}"));
        return "{\n"
            + $"  \"name\": {Quote(name)},\n"
            + "  \"version\": \"v2026.12.1\",\n"
            + "  \"specVersion\": 31,\n"
            + "  \"derivedFrom\": null,\n"
            + $"  \"title\": {Quote(title)},\n"
            + "  \"description\": [\"A fictional package for tests.\", \"Not intended for clinical use.\"],\n"
            + "  \"tags\": [\"demo\", \"fictional\"],\n"
            + "  \"inputs\": {\n" + inputs + "\n  }\n"
            + "}\n";
    }

    /// <summary>
    /// A node header: the nine members every node and flow node shares (part 01 § 5.1, D18),
    /// then <paramref name="tail"/>, the kind's own members. <c>format</c> is JSON text.
    /// </summary>
    private static string Node(
        string kind, string label, string format, bool reproducible, bool modelDerived, string tail) =>
        $"{{ \"kind\": \"{kind}\", \"producer\": \"{(kind == "agent" ? "generated" : "computed")}\", "
        + $"\"label\": {Quote(label)}, \"description\": null, \"format\": {format}, "
        + $"\"reproducible\": {Bool(reproducible)}, \"modelDerived\": {Bool(modelDerived)}, "
        + $"\"runDependent\": false, \"profileCarrying\": false, {tail} }}";

    private static string Agent(string label, string format, string prompt) =>
        Md(Node("agent", label, format, false, true, AgentTail), prompt);

    private static string Deliverable(string label, string format, string check, string whenEmpty) =>
        $"{{ \"label\": {Quote(label)}, \"kind\": \"rendered\", \"description\": null, \"format\": {format}, "
        + $"\"if\": null, \"check\": {check}, \"whenEmpty\": \"{whenEmpty}\" }}";

    private static string MacroHeader(string label, string condition, string instruction) =>
        $"{{ \"label\": {Quote(label)}, \"description\": null, \"if\": {condition}, \"format\": \"plaintext\", "
        + $"\"instruction\": {instruction} }}";

    private static string ArrayOf(string format) =>
        format.StartsWith('{') ? $"{{ \"type\": \"array\", \"items\": {format} }}"
            : $"{{ \"type\": \"array\", \"items\": {{ \"$ref\": {(format.StartsWith('"') ? format : Quote(format))} }} }}";

    private static string Nested(string format) => ArrayOf(ArrayOf(format));

    private static string Bool(bool value) => value ? "true" : "false";

    private static string Quote(string text) => JsonValue.Create(text)!.ToJsonString(Relaxed);
}
