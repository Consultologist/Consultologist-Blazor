using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.ByteEquivalence;
using Consultologist.Api.Tests.V31Run;
using Consultologist.Api.Tests.V31Source;
using Consultologist.Api.Tests.V31Templates;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Migration;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 §§ 1.2, 1.3, 1.4; part 05 §§ 8.1 to 8.4, 8.6; part 02 § 10; D3 to D5; R5, R7):
/// the macros pass. Each 30 macro becomes its file (label, <c>description: null</c>, its <c>if</c>, its format, its
/// instruction lines with 30's mechanics sentence, its body as 30 wrote it), a folder its index and items; an optional
/// macro becomes an optional boolean input its <c>if</c> reads (suffixed <c>_macro</c> when an input has its id); a
/// macro that is fixed text becomes a snippet; a node instance, a macro read both ways and a templated instruction are
/// hand edits. Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class MigrateMacrosTests
{
    private static readonly JsonSerializerOptions Unescaped = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string Mechanics(string marker) =>
        $"If so, write {{{{ macro.{marker}.content }}}} verbatim and nothing else at that place; do not write that content yourself — it is filled in afterward.";

    private static string Header(Migration31Result result, string path) => MigrateNodesTests.Md(result, path).Header.ToJsonString(Unescaped);

    private static void AssertHandEdit(Migration31Result result, string file, string member, string id, string reason)
    {
        var edit = $"Hand edit needed in {file}, {member}: {reason}";
        Assert.Contains(edit, result.HandEdits);
        Assert.Contains(edit, result.Notices);
        V31Contract.AssertSentence(new SourceRefusal("S10-002", edit, file));
        V31Contract.AssertSentence(new SourceRefusal(id, reason, ""));
    }

    /// <summary>The refusals of a 31 package placing the migrated macro file <paramref name="text"/> as <paramref name="id"/> in an agent's prompt.</summary>
    private static IReadOnlyList<SourceRefusal> RefusalsPlacing(string id, string text) =>
        TemplateFixture.RefusalsOf(TemplateFixture.Package()
            .Listed("macros/index.json", id)
            .With($"macros/{id}.md", text)
            .WithAgent("draft", $"Draft the plan for Margaret Okafor.\n{{{{ macro.{id} }}}}\n", FormatOf(text)));

    /// <summary>The <c>format</c> a macro file's header holds, as JSON: the agent placing it takes the same (part 05 § 8.3's format match).</summary>
    internal static string FormatOf(string macroFile) => JsonNode.Parse(FrontMatter.Split(macroFile)!.Header)!["format"]!.ToJsonString();

    /// <summary>
    /// The refusals of a 31 package with the macro <paramref name="macro"/> (<c>&lt;id&gt;</c>, or <c>&lt;folder&gt;/&lt;id&gt;</c>)
    /// placed by an agent whose prompt, or a snippet it places (<paramref name="inSnippet"/>), holds <paramref name="line"/>:
    /// a line the migration wrote.
    /// </summary>
    private static IReadOnlyList<SourceRefusal> RefusalsReading(string macro, string line, bool inSnippet)
    {
        var slash = macro.IndexOf('/', StringComparison.Ordinal);
        var files = TemplateFixture.Package();
        var placed = macro.Replace('/', '.');
        if (slash < 0)
        {
            files.WithMacro(macro, "Reviewed with Margaret Okafor.\n");
        }
        else
        {
            var (folder, id) = (macro[..slash], macro[(slash + 1)..]);
            files.Listed("macros/index.json", folder)
                .With($"macros/{folder}/index.json", SourceFixture.Index([id], extra: ", \"if\": null"))
                .With($"macros/{folder}/{id}.md", SourceFixture.Md(
                    $"{{ \"label\": \"Macro {id}\", \"description\": null, \"if\": null, \"format\": \"plaintext\", \"instruction\": [\"Place {{{{ macro.{placed}.content }}}} at the end.\"] }}",
                    "Reviewed with Margaret Okafor.\n"));
        }

        return TemplateFixture.RefusalsOf(inSnippet
            ? files.WithSnippet("echo", line + "\n").WithAgent("draft", $"Draft.\n{{{{ snippet.echo.content }}}}\n{{{{ macro.{placed} }}}}\n")
            : files.WithAgent("draft", $"Draft.\n{line}\n{{{{ macro.{placed} }}}}\n"));
    }

    /// <summary>
    /// A migrated macro file with its <c>if</c> written <c>null</c>, to be placed in another package: a macro 30's model chose
    /// reads its classifier's answer (R13), which that package has no node for.
    /// </summary>
    private static string Ungated(string macroFile)
    {
        var split = FrontMatter.Split(macroFile)!;
        var header = JsonNode.Parse(split.Header)!.AsObject();
        header["if"] = null;
        return "---\n" + header.ToJsonString(Unescaped) + "\n---\n" + split.Body;
    }

    /// <summary>The line of <paramref name="body"/> holding <paramref name="text"/>.</summary>
    private static string LineWith(string body, string text) => body.Split('\n').Single(l => l.Contains(text, StringComparison.Ordinal));

    /// <summary>
    /// <c>example-macros</c> with an input <c>letrozole</c> and the optional macro <c>letrozole</c> (default off), which
    /// <c>summary.md</c> places, and with <paramref name="inputs"/> more boolean inputs.
    /// </summary>
    internal static Migration31Result WithLetrozole(params string[] inputs) =>
        MigrateNodesTests.Migrated("example-macros",
            root =>
            {
                foreach (var id in (string[])["letrozole", .. inputs])
                    root["inputs"]!.AsArray().Add(new JsonObject { ["id"] = id, ["label"] = $"Input {id}", ["required"] = false, ["type"] = "boolean" });
                root["macros"]!.AsArray().Add((JsonNode)"letrozole");
            },
            files =>
            {
                files["macros/letrozole.md"] =
                    "{\n  \"label\": \"Letrozole counselling\",\n  \"instruction\": \"The letrozole counselling goes after the plan.\",\n  \"optional\": true,\n  \"default\": false\n}\n"
                    + "Margaret Okafor was counselled on letrozole's side effects.\n";
                files["prompts/summary.md"] += "{{ macro.letrozole.prompt }}\n";
            });

    [Fact]
    public void AMacro_BecomesItsFile()
    {
        var result = MigrateNodesTests.Migrated("example-macros");

        // Part 05 § 8.1's members in order; the instruction is 30's, an empty line, then 30's mechanics sentence (D5).
        Assert.Equal(
            "{\"label\":\"House-style reminder\",\"description\":null,\"if\":" + Answer("node.house_style_applies") + ",\"format\":\"markdown_block\",\"instruction\":["
            + "\"Where the summary departs from the house style above, the reminder goes as the last sentence; otherwise nothing.\",\"\","
            + JsonSerializer.Serialize(Mechanics("house_style"), Unescaped) + "]}",
            Header(result, "macros/house_style.md"));
        Assert.Equal("Reviewed against the house style.\n", MigrateNodesTests.Md(result, "macros/house_style.md").Body);

        // Its own if, converted.
        Assert.Equal("""{"===":[{"var":"input.note_type.value"},"consult"]}""", MigrateNodesTests.Md(result, "macros/followup_only.md").Header["if"]!.ToJsonString());

        // R5: the body is 30's byte for byte (31 drops one final line ending as it renders): test/general's has none.
        var general = MigrateNodesTests.Migrated("test/general");
        Assert.Equal(
            "Discussed side effects including: hot flashes, joint or muscle pain, fatigue, headache, nausea, and bone thinning. Advised to monitor side effects.",
            MigrateNodesTests.Md(general, "macros/letrozole_prescription_text.md").Body);

        // macros/index.json lists every macro that stays one, and each folder, in 30's order.
        Assert.Equal(
            """["counselling","house_style","silent_reminder","section_caveat","table_note","preamble","sign_off","wrap_up","header_line","followup_only","brief_plan","follow_up_reminder","notes"]""",
            MigrateNodesTests.Json(result, "macros/index.json")["items"]!.ToJsonString());

        // The format is the placing agents' (part 05 § 8.3). Every 30 agent that places a macro here is prose a deliverable
        // composes, so markdown_block (R9), and so is every macro an agent places; the deliverable macros no agent places
        // (hand edits, A10-002) keep plaintext. With the agents plain text, one returning Markdown makes the macros only it
        // places Markdown (wrap_up), and so does one placing the snippet that places it (house_style, through style.md,
        // which summary places).
        string[] onlyOnADeliverable = ["header_line", "followup_only", "brief_plan", "follow_up_reminder"];
        Assert.All(result.Files.Where(f => f.Key.StartsWith("macros/", StringComparison.Ordinal) && f.Key.EndsWith(".md", StringComparison.Ordinal)),
            f => Assert.Equal(
                onlyOnADeliverable.Any(id => f.Key == $"macros/{id}.md") ? "plaintext" : "markdown_block",
                (string?)MigrateNodesTests.Md(result, f.Key).Header["format"]));
        var m = TemplateRewriterTests.Working("example-macros");
        Migrate.Templates(m);
        foreach (var (path, node) in m.Sources.Where(f => f.Key.StartsWith("nodes/", StringComparison.Ordinal) && f.Key.EndsWith(".md", StringComparison.Ordinal)))
            node.Json["format"] = "plaintext";
        m.Sources["nodes/summary.md"].Json["format"] = "markdown_block";
        Migrate.Macros(m);
        Assert.Equal("markdown_block", (string?)m.Sources["macros/wrap_up.md"].Json["format"]);
        Assert.Equal("markdown_block", (string?)m.Sources["macros/house_style.md"].Json["format"]);
        Assert.Equal("plaintext", (string?)m.Sources["macros/counselling.md"].Json["format"]); // placed by other agents too
        var inline = TemplateRewriterTests.Working("example-macros");
        Migrate.Templates(inline);
        inline.Sources["nodes/summary.md"].Json["format"] = "markdown_inline";
        Migrate.Macros(inline);
        Assert.Equal("markdown_inline", (string?)inline.Sources["macros/wrap_up.md"].Json["format"]);
    }

    [Fact]
    public void ThePromptRead_BecomesAPlacement()
    {
        var result = MigrateNodesTests.Migrated("example-macros");

        var summary = MigrateNodesTests.Md(result, "nodes/summary.md").Body;
        Assert.Contains("- Red flags: {{ macro.notes.red_flags }}\n- Safety net: {{ macro.notes.safety_net }}\n\n{{ macro.wrap_up }}\n", summary, StringComparison.Ordinal);
        Assert.Contains("{{ macro.house_style }}", MigrateNodesTests.Md(result, "snippets/style.md").Body, StringComparison.Ordinal);
        foreach (var (path, text) in result.Files)
            Assert.DoesNotContain(".prompt }}", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRenderedInstruction_Has30sBytes()
    {
        // 30's .prompt of house_style, as the summary's prompt sent it (example-macros' capture).
        var capture = JsonNode.Parse(ByteEquivalenceFixtures.ReadText(Path.Combine(ByteEquivalenceFixtures.Root, "captures", "example-macros", "default.json")))!;
        var sent = (string)capture["prompts"]!.AsArray().Single(p => (string?)p!["key"] == "summary")!["text"]!;
        var prompt30 = "Where the summary departs from the house style above, the reminder goes as the last sentence; otherwise nothing.\n\n" + Mechanics("house_style");
        Assert.Contains("know.\n" + prompt30 + "\n\n\nReminders:", sent, StringComparison.Ordinal);

        // The migrated macro, placed by a 31 agent and composed as the engine composes it.
        var migrated = Ungated(MigrateNodesTests.Migrated("example-macros").Files["macros/house_style.md"]);
        var plan = RunFixture.Plan(TemplateFixture.Package()
            .Listed("macros/index.json", "house_style")
            .With("macros/house_style.md", migrated)
            .WithAgent("draft", "House style:\n{{ macro.house_style }}\nEnd.\n", FormatOf(migrated)));
        var composed = BodyComposerTests.Composed(BodyComposerTests.Compose(plan, "node.draft", new JsonObject(), BodyComposerTests.Placed("macro.house_style")));

        Assert.Equal("House style:\n" + prompt30 + "\nEnd.\n", composed);
    }

    [Fact]
    public void AnOptionalMacro_BecomesABooleanInput()
    {
        // Default on: offered while unset.
        var result = MigrateNodesTests.Migrated("example-macros");
        Assert.Equal(
            """{"!==":[{"var":"input.follow_up_reminder.value"},false]}""",
            MigrateNodesTests.Md(result, "macros/follow_up_reminder.md").Header["if"]!.ToJsonString());
        var manifest = JsonNode.Parse(result.ManifestJson)!;
        Assert.Equal(
            """{"label":"Follow-up reminder","description":null,"format":"boolean","required":false,"channel":null}""",
            manifest["inputs"]!["follow_up_reminder"]!.ToJsonString());
        Assert.Contains("Macro 'follow_up_reminder' is now offered by the input 'follow_up_reminder'.", result.Notices);
        V31Contract.AssertSentence(new SourceRefusal("A10-007", "Macro 'follow_up_reminder' is now offered by the input 'follow_up_reminder'.", ""));
        Assert.Empty(result.Report.MacroInputs); // no suffix was needed

        // Default off: not offered while unset.
        var macros = MigrateNodesTests.Migrated("composer-macros");
        Assert.Equal("""{"===":[{"var":"input.leaflet_offer.value"},true]}""", MigrateNodesTests.Md(macros, "macros/leaflet_offer.md").Header["if"]!.ToJsonString());
        Assert.Equal(
            """{"label":"Leaflet offer","description":null,"format":"boolean","required":false,"channel":null}""",
            JsonNode.Parse(macros.ManifestJson)!["inputs"]!["leaflet_offer"]!.ToJsonString());

        // Joined with and after the macro's own if, and after the condition its placements carry.
        var joined = MigrateNodesTests.Migrated("example-macros", _ => { }, files =>
        {
            files["macros/followup_only.md"] = files["macros/followup_only.md"].Replace("\"if\": \"input.note_type == \\\"consult\\\"\"", "\"if\": \"input.note_type == \\\"consult\\\"\",\n  \"optional\": true,\n  \"default\": false", StringComparison.Ordinal);
            files["prompts/summary.md"] += "{{ if input.include_counselling == true }}{{ macro.followup_only.prompt }}{{ end }}\n";
        });
        Assert.Equal(
            """{"and":[{"===":[{"var":"input.note_type.value"},"consult"]},{"===":[{"var":"input.include_counselling.value"},true]},{"===":[{"var":"input.followup_only.value"},true]},"""
            + Answer("node.followup_only_applies") + "]}",
            MigrateNodesTests.Md(joined, "macros/followup_only.md").Header["if"]!.ToJsonString());

        // Final review A3 (R7): that if reads include_counselling beside the choice, so a MacroChoices entry for the macro is
        // refused at a 31 start (A10-044); the migration says so, and the package still publishes (a notice, not a hand edit).
        const string choiceNotice = "Macro 'followup_only' is chosen per run by the input 'followup_only', but its if reads another boolean input, so a MacroChoices entry for 'followup_only' is refused at 31; send the input 'followup_only' in Inputs instead.";
        Assert.Contains(choiceNotice, joined.Notices);
        Assert.Contains(choiceNotice, joined.Report.MacroInputs);
        Assert.DoesNotContain(joined.HandEdits, e => e.Contains("MacroChoices", StringComparison.Ordinal));
        V31Contract.AssertSentence(new SourceRefusal("A10-067", choiceNotice, ""));
        // The near pair: a choice whose if reads no other boolean input (follow_up_reminder; note_type is an enum) is not said.
        Assert.DoesNotContain(result.Notices, n => n.Contains("chosen per run", StringComparison.Ordinal));
        Assert.DoesNotContain(joined.Notices, n => n.Contains("chosen per run by the input 'follow_up_reminder'", StringComparison.Ordinal));
    }

    [Fact]
    public void ACollidingChoice_TakesTheMacroSuffix()
    {
        var result = WithLetrozole();

        var inputs = JsonNode.Parse(result.ManifestJson)!["inputs"]!.AsObject();
        Assert.Equal(
            """{"label":"Letrozole counselling","description":null,"format":"boolean","required":false,"channel":null}""",
            inputs["letrozole_macro"]!.ToJsonString());
        Assert.Equal("Input letrozole", (string?)inputs["letrozole"]!["label"]); // the input of that id is untouched
        Assert.Equal("""{"and":[{"===":[{"var":"input.letrozole_macro.value"},true]},""" + Answer("node.letrozole_applies") + "]}", MigrateNodesTests.Md(result, "macros/letrozole.md").Header["if"]!.ToJsonString());
        const string notice = "Macro 'letrozole' is now offered by the input 'letrozole_macro'.";
        Assert.Equal([notice], result.Report.MacroInputs);
        Assert.Contains(notice, result.Notices);
        V31Contract.AssertSentence(new SourceRefusal("A10-007", notice, ""));

        // Review Focus 2: letrozole_macro taken too gives the first free number, said in S10-006's form.
        var taken = WithLetrozole("letrozole_macro");
        Assert.Equal("""{"and":[{"===":[{"var":"input.letrozole_macro_2.value"},true]},""" + Answer("node.letrozole_applies") + "]}", MigrateNodesTests.Md(taken, "macros/letrozole.md").Header["if"]!.ToJsonString());
        // The input is said to offer the macro too (A10-007), beside the rename.
        const string renamed = "Renamed 'letrozole' to 'letrozole_macro_2'; 'letrozole_macro' was already taken.";
        const string offered = "Macro 'letrozole' is now offered by the input 'letrozole_macro_2'.";
        Assert.Equal([renamed, offered], taken.Report.MacroInputs);
        Assert.Contains(offered, taken.Notices);
        V31Contract.AssertSentence(new SourceRefusal("S10-006", renamed, ""));
        V31Contract.AssertSentence(new SourceRefusal("A10-007", offered, ""));
        Assert.Equal("Input letrozole_macro", (string?)JsonNode.Parse(taken.ManifestJson)!["inputs"]!["letrozole_macro"]!["label"]);
    }

    [Fact]
    public void ANodeMacroPlacedBeforeOrAfter_IsAHandEdit()
    {
        var result = MigrateNodesTests.Migrated("example-macros");

        // D3: placed at the end of the prompt, the instruction written [] (refused at 31: no marker), one hand edit.
        Assert.EndsWith("{{ input.consult_draft.content }}\n{{ macro.table_note }}\n", MigrateNodesTests.Md(result, "nodes/requested_sections/route/table_draft.md").Body, StringComparison.Ordinal);
        var tableNote = MigrateNodesTests.Md(result, "macros/table_note.md");
        Assert.Equal("[]", tableNote.Header["instruction"]!.ToJsonString());
        AssertHandEdit(result, "macros/table_note.md", "instruction", "A10-003",
            "macro 'table_note' was placed before the reply by the engine; say in its instruction where the model writes its marker.");
        AssertHandEdit(result, "macros/sign_off.md", "instruction", "A10-003",
            "macro 'sign_off' was placed after the reply by the engine; say in its instruction where the model writes its marker.");
        Assert.Contains(RefusalsPlacing("table_note", result.Files["macros/table_note.md"]), r => r.Id == "S05-044");

        // A macro placed both before and after a reply has one instruction to write: one notice naming both sides.
        var sides = MigrateNodesTests.Migrated("example-macros", root =>
            MigrateNodesTests.Node(root, "narrative_draft")["macros"]!.AsArray().Add(new JsonObject { ["id"] = "table_note", ["at"] = "after" }));
        const string both = "macro 'table_note' was placed before and after the reply by the engine; say in its instruction where the model writes its marker.";
        AssertHandEdit(sides, "macros/table_note.md", "instruction", "A10-003", both);
        Assert.Single(sides.HandEdits, h => h.StartsWith("Hand edit needed in macros/table_note.md, instruction: ", StringComparison.Ordinal));

        // The instance's if is that placement's rule, so the macro's if.
        Assert.Equal("""{"===":[{"var":"input.note_type.value"},"consult"]}""", MigrateNodesTests.Md(result, "macros/section_caveat.md").Header["if"]!.ToJsonString());
        Assert.EndsWith("{{ macro.section_caveat }}\n", MigrateNodesTests.Md(result, "nodes/requested_sections/route/narrative_draft.md").Body, StringComparison.Ordinal);

        // Two instances on one node, in 30's order; a prompt that already places the macro (standing_draft.md, counselling)
        // is not given a second placement (a template places a macro once).
        Assert.EndsWith("{{ macro.preamble }}\n{{ macro.sign_off }}\n", MigrateNodesTests.Md(result, "nodes/summary.md").Body, StringComparison.Ordinal);
        var standing = MigrateNodesTests.Md(result, "nodes/standing_sections/standing_draft.md").Body;
        Assert.Equal(2, standing.Split("{{ macro.counselling }}").Length);
    }

    [Fact]
    public void AMacroReadAsFixedText_BecomesASnippet()
    {
        var result = MigrateNodesTests.Migrated("example-macros");

        // boilerplate is read as .content only; closing is placed bare on a deliverable. Each is a snippet, not a macro.
        Assert.Equal(
            """{"label":"Boilerplate disclaimer","description":null,"if":null,"format":"markdown_block"}""",
            Header(result, "snippets/boilerplate.md"));
        Assert.Equal("This note was drafted with software assistance and reviewed by the signing clinician.\n", MigrateNodesTests.Md(result, "snippets/boilerplate.md").Body);
        // Task 15 fix round 1 (R5): closing, which only a deliverable reads, is written with 30's trailing trim; boilerplate,
        // which a prompt reads as .content (the file whole at 30), keeps its final line ending.
        Assert.Equal("End of note.", MigrateNodesTests.Md(result, "snippets/closing.md").Body);
        Assert.False(result.Files.ContainsKey("macros/boilerplate.md"));
        Assert.False(result.Files.ContainsKey("macros/closing.md"));
        Assert.Equal("""["sections","style","boilerplate","closing","kept_note"]""", MigrateNodesTests.Json(result, "snippets/index.json")["items"]!.ToJsonString());
        Assert.Contains(
            "Disclaimer to keep in mind (do not copy it into the summary):\n{{ snippet.boilerplate.content }}\n",
            MigrateNodesTests.Md(result, "nodes/summary.md").Body, StringComparison.Ordinal);

        // One a prompt also places (its .prompt read) stays a macro: kept_note, placed bare on the deliverable. D4 keeps the
        // macro file, and the deliverables pass (Task 11) writes the snippet its deliverable reads beside it.
        var placed = MigrateNodesTests.Migrated("example-macros", _ => { }, files => files["prompts/summary.md"] += "{{ macro.kept_note.prompt }}\n");
        Assert.True(placed.Files.ContainsKey("macros/kept_note.md"));
        // The snippet only the deliverable reads has 30's trailing trim (Task 15 fix round 1, R5); the macro keeps its body whole.
        Assert.Equal(MigrateNodesTests.Md(placed, "macros/kept_note.md").Body.TrimEnd(), MigrateNodesTests.Md(placed, "snippets/kept_note.md").Body);

        // A macro body's fixed-text read of a macro that becomes a snippet reads the snippet.
        var echo = MigrateNodesTests.Migrated("example-macros", _ => { }, files => files["macros/followup_only.md"] += "{{ macro.closing.content }}\n");
        Assert.EndsWith("Seen in follow-up; interval history reviewed.\n{{ snippet.closing.content }}\n", MigrateNodesTests.Md(echo, "macros/followup_only.md").Body, StringComparison.Ordinal);

        // One of a macro that stays a macro (new_visit_note has an if) has no 31 form: a hand edit (A10-040) on the 31 file
        // that holds it, composer-macros' visit_echo, itself a snippet, written as macro.<id>.fixed_text, which 31 refuses.
        var echoes = MigrateNodesTests.Migrated("composer-macros");
        Assert.Equal("New-visit note as this macro reads it: [{{ macro.new_visit_note.fixed_text }}]", MigrateNodesTests.Md(echoes, "snippets/visit_echo.md").Body);
        AssertHandEdit(echoes, "snippets/visit_echo.md", "body", "A10-040",
            "macro 'new_visit_note' is read as fixed text at line 1, but it is offered under a condition or placed by the engine; place its text as a snippet with an if.");
        var written = LineWith(MigrateNodesTests.Md(echoes, "snippets/visit_echo.md").Body, "fixed_text");
        Assert.Contains(RefusalsReading("new_visit_note", written, inSnippet: true), r => r.Id == "S05-011");
        // #1508 (ruling 12): 30's read, a macro's marker outside its instruction, is refused too.
        Assert.Contains(RefusalsReading("new_visit_note", written.Replace("fixed_text", "content", StringComparison.Ordinal), inSnippet: true), r => r.Id == "S05-011");
    }

    [Fact]
    public void AMacroReadBothWays_IsAHandEdit()
    {
        // house_style is placed (style.md reads its .prompt) and now read as .content too: A10-004 at the read, written
        // macro.<id>.fixed_text, which 31 refuses (#1508, ruling 12: as it refuses 30's macro.<id>.content).
        var both = MigrateNodesTests.Migrated("example-macros", _ => { }, files => files["prompts/summary.md"] += "{{ macro.house_style.content }}\n");
        var bothBody = MigrateNodesTests.Md(both, "nodes/summary.md").Body;
        Assert.Contains("\n{{ macro.house_style.fixed_text }}\n", bothBody, StringComparison.Ordinal);
        AssertHandEdit(both, "nodes/summary.md", "body", "A10-004",
            "macro 'house_style' is read both as fixed text and as an instruction; split it into a snippet and a macro.");
        Assert.True(both.Files.ContainsKey("macros/house_style.md"));
        Assert.Contains(RefusalsReading("house_style", "{{ macro.house_style.fixed_text }}", inSnippet: false), r => r.Id == "S05-011");
        Assert.Contains(RefusalsReading("house_style", "{{ macro.house_style.content }}", inSnippet: false), r => r.Id == "S05-011");

        // A folder's macro is never a snippet: an unrolled .content read of it is a hand edit, A10-004 beside its .prompt.
        var folder = MigrateNodesTests.Migrated("example-macros", _ => { }, files =>
            files["prompts/summary.md"] = files["prompts/summary.md"].Replace("{{ m.prompt }}\n", "{{ m.prompt }} {{ m.content }}\n", StringComparison.Ordinal));
        var red = LineWith(MigrateNodesTests.Md(folder, "nodes/summary.md").Body, "- Red flags:");
        Assert.Equal("- Red flags: {{ macro.notes.red_flags }} {{ macro.notes.red_flags_fixed_text }}", red);
        AssertHandEdit(folder, "nodes/summary.md", "body", "A10-004",
            "macro 'notes/safety_net' is read both as fixed text and as an instruction; split it into a snippet and a macro.");
        Assert.Contains(RefusalsReading("notes/red_flags", "{{ macro.notes.red_flags_fixed_text }}", inSnippet: false), r => r.Id == "S05-011");
        Assert.Contains(RefusalsReading("notes/red_flags", "{{ macro.notes.red_flags_fixed_text }}", inSnippet: true), r => r.Id == "S05-011");
        // #1508 (ruling 12): a member past the item is refused too.
        Assert.Contains(RefusalsReading("notes/red_flags", "{{ macro.notes.red_flags.fixed_text }}", inSnippet: false), r => r.Id == "S05-011");

        // Alone in its loop, a folder macro's .content read is A10-045 (a folder's item is never fixed text at 31).
        var alone = MigrateNodesTests.Migrated("example-macros", _ => { }, files =>
            files["prompts/summary.md"] = files["prompts/summary.md"].Replace("{{ m.prompt }}\n", "{{ m.prompt }}\n{{ end }}{{ for n in macro.notes }}{{ n.content }}\n", StringComparison.Ordinal));
        var aloneBody = MigrateNodesTests.Md(alone, "nodes/summary.md").Body;
        Assert.Contains("{{ macro.notes.red_flags_fixed_text }}\n{{ macro.notes.safety_net_fixed_text }}\n", aloneBody, StringComparison.Ordinal);
        var at = aloneBody.Split('\n').ToList().FindIndex(l => l.Contains("macro.notes.red_flags_fixed_text", StringComparison.Ordinal)) + 1;
        AssertHandEdit(alone, "nodes/summary.md", "body", "A10-045",
            $"macro 'notes/red_flags' is read as fixed text at line {at}, but a macro folder's item is never fixed text at 31; place its text as a snippet.");

        // followup_only has an if, so it is no snippet, and its fixed-text read has no 31 form: A10-040.
        var conditional = MigrateNodesTests.Migrated("example-macros", _ => { }, files => files["prompts/summary.md"] += "{{ macro.followup_only.content }}\n");
        var line = MigrateNodesTests.Md(conditional, "nodes/summary.md").Body.Split('\n').ToList().FindIndex(l => l.Contains("macro.followup_only.fixed_text", StringComparison.Ordinal)) + 1;
        Assert.True(line > 0);
        AssertHandEdit(conditional, "nodes/summary.md", "body", "A10-040",
            $"macro 'followup_only' is read as fixed text at line {line}, but it is offered under a condition or placed by the engine; place its text as a snippet with an if.");
        Assert.Contains(RefusalsReading("followup_only", "{{ macro.followup_only.fixed_text }}", inSnippet: false), r => r.Id == "S05-011");
        Assert.Contains(RefusalsReading("followup_only", "{{ macro.followup_only.fixed_text }}", inSnippet: true), r => r.Id == "S05-011");
    }

    [Fact]
    public void ATemplatedInstruction_IsAHandEdit()
    {
        var result = MigrateNodesTests.Migrated("example-macros");
        const string reason = "the instruction is a template at 30, which a 31 instruction cannot be; write it as lines that say where the model writes the marker.";

        // D5: written as 30's lines alone, holding no marker of the macro's own: the checker refuses each (S05-044).
        AssertHandEdit(result, "macros/silent_reminder.md", "instruction", "A10-042", reason);
        Assert.Equal("[\"{{ \\\"\\\" }}\"]", MigrateNodesTests.Md(result, "macros/silent_reminder.md").Header["instruction"]!.ToJsonString(Unescaped));
        Assert.Contains(RefusalsPlacing("silent_reminder", result.Files["macros/silent_reminder.md"]), r => r.Id == "S05-044");

        AssertHandEdit(result, "macros/wrap_up.md", "instruction", "A10-042", reason);
        Assert.Equal(
            """["Close the summary with {{ macro.sign_off.content }} as its final line and write nothing after it."]""",
            MigrateNodesTests.Md(result, "macros/wrap_up.md").Header["instruction"]!.ToJsonString(Unescaped));

        // One that holds its own marker (30 rendered the macro's own text there) gains the sentence: the marker twice.
        var warnings = MigrateNodesTests.Migrated("example-macros-warnings");
        AssertHandEdit(warnings, "macros/self_read.md", "instruction", "A10-042", reason);
        Assert.Equal(
            "[\"Write {{ macro.self_read.content }} at the end of the summary.\",\"\"," + JsonSerializer.Serialize(Mechanics("self_read"), Unescaped) + "]",
            MigrateNodesTests.Md(warnings, "macros/self_read.md").Header["instruction"]!.ToJsonString(Unescaped));
        Assert.Contains(RefusalsPlacing("self_read", warnings.Files["macros/self_read.md"]), r => r.Id == "S05-044");

        // A plain instruction is no hand edit.
        Assert.DoesNotContain(result.HandEdits, h => h.StartsWith("Hand edit needed in macros/house_style.md", StringComparison.Ordinal));
        Assert.Empty(RefusalsPlacing("house_style", Ungated(result.Files["macros/house_style.md"])));
    }

    [Fact]
    public void AMacroFolder_KeepsItsItems()
    {
        var result = MigrateNodesTests.Migrated("example-macros");

        Assert.Equal("""{"description":null,"items":["red_flags","safety_net"],"if":null}""", MigrateNodesTests.Json(result, "macros/notes/index.json").ToJsonString());
        Assert.Equal(
            "{\"label\":\"Red flags\",\"description\":null,\"if\":" + Answer("node.red_flags_applies") + ",\"format\":\"markdown_block\",\"instruction\":["
            + "\"If the draft reviews red flags, the red-flags line goes in the summary; otherwise nothing.\",\"\","
            + JsonSerializer.Serialize(Mechanics("notes.red_flags"), Unescaped) + "]}",
            Header(result, "macros/notes/red_flags.md"));
        Assert.Equal("Advised to seek urgent care if symptoms worsen.\n", MigrateNodesTests.Md(result, "macros/notes/safety_net.md").Body);
    }

    [Fact]
    public void AMacroBodyThatPlacesAMacro_IsRefusedAt30()
    {
        // Part 05 § 8.3: a 31 macro body places no macro (S05-042), and 30 refuses one too, so no 30 package reaches the
        // templates pass's hand edit for it (A10-041), nor counts such a placement among the placed macro's.
        var (json, read) = ByteEquivalenceFixtures.ReadPackage("example-macros");
        var files = new Dictionary<string, string>(read, StringComparer.Ordinal);
        files["macros/brief_plan.md"] += "{{ if input.include_counselling == true }}{{ macro.house_style.prompt }}{{ end }}\n";

        var result = WorkflowPackageMigration30To31.Migrate(json, files, MigrationFixture.Options("example-macros"));

        Assert.Equal(["At specVersion 30: Macro 'brief_plan' text reaches macro.house_style.prompt; text placed after a reply instructs no model."], result.Refusals);
        V31Contract.AssertSentence(new SourceRefusal("A10-041", "the body places macro 'house_style', which a 31 macro body cannot; place it in the prompt instead.", ""));
    }

    [Fact]
    public void PlacementsThatDisagree_AreWrittenAsTextTheCheckerRefuses()
    {
        // A node instance's condition is counted with the prompt's placements, even where the prompt already places the
        // macro: preamble, placed always before summary's reply, and hoisted in summary.md under a condition, disagree.
        var instance = MigrateNodesTests.Migrated("example-macros", _ => { }, files => files["prompts/summary.md"] += "{{ if input.include_counselling == true }}{{ macro.preamble.prompt }}{{ end }}\n");
        Assert.Equal(2, MigrateNodesTests.Md(instance, "nodes/summary.md").Body.Split("{{ macro.preamble }}").Length);
        AssertHandEdit(instance, "macros/preamble.md", "if", "A10-036",
            "macro 'preamble' is placed under different conditions in nodes/summary.md; a macro has one if, so place it under one condition, or split it into one macro per condition.");
        Assert.Equal("placed under one of nodes/summary.md: input.include_counselling == true; nodes/summary.md: always", (string?)MigrateNodesTests.Md(instance, "macros/preamble.md").Header["if"]);

        // counselling: hoisted under two conditions (A10-036, said by the templates pass), and its instance's; its if is
        // never a join of them.
        var result = MigrateNodesTests.Migrated("example-macros");

        var written = MigrateNodesTests.Md(result, "macros/counselling.md").Header["if"]!;
        Assert.Equal(
            "input.note_type == \"consult\" && placed under one of nodes/standing_sections/standing_draft.md: input.include_counselling == true; nodes/patient_line.md: input.note_type == \"follow_up\"; nodes/standing_sections/standing_draft.md: item.id == \"assessment_plan\"",
            (string?)written);
        Assert.Single(result.HandEdits, h => h.StartsWith("Hand edit needed in macros/counselling.md, if: ", StringComparison.Ordinal));
        var text = result.Files["macros/counselling.md"].Replace("\"instruction\":[]", "\"instruction\":[\"Place {{ macro.counselling.content }} in the plan.\"]", StringComparison.Ordinal);
        Assert.Contains(RefusalsPlacing("counselling", text), r => r.Id == "S03-025"); // a condition that yields a string
    }

    // ---- #1508 (comment 6, item 1; R13): a macro the model used to choose is gated by a classifier -------------------

    /// <summary>The <c>if</c> that reads classifier <paramref name="key"/>'s answer, as canonical JSON.</summary>
    private static string Answer(string key) => $$"""{"===":[{"var":"{{key}}.value.answer"},true]}""";

    /// <summary>
    /// <c>test/general</c> with the letrozole macro placed by the loop's prompt, <c>section_instructions</c>, rather than by the
    /// snippet folder's item (which places no macro at 31, A10-051): the migration then knows the agent that places it.
    /// </summary>
    private static Migration31Result LetrozoleInTheLoop() =>
        MigrateNodesTests.Migrated("test/general", _ => { }, files =>
        {
            const string Placed = "{{ if input.note_type == \"consult\" }}\n{{ macro.letrozole_prescription_text.prompt }}\n{{ end }}";
            Assert.Contains(Placed, files["snippets/standards/assessment_plan.md"], StringComparison.Ordinal);
            files["snippets/standards/assessment_plan.md"] = files["snippets/standards/assessment_plan.md"].Replace(Placed, "", StringComparison.Ordinal);
            files["prompts/section_instructions.md"] += "\n{{ macro.letrozole_prescription_text.prompt }}\n";
        });

    /// <summary>
    /// <c>example-macros</c> with the macro <c>loop_note</c> (its own <c>if</c>, an instruction leaving the choice to the
    /// model) placed by <c>narrative_draft</c>, which runs in a <c>case</c> branch inside the loop <c>requested_sections</c>.
    /// </summary>
    private static Migration31Result WithLoopNote() =>
        MigrateNodesTests.Migrated("example-macros", root => root["macros"]!.AsArray().Add((JsonNode)"loop_note"), files =>
        {
            files["macros/loop_note.md"] =
                "{\n  \"label\": \"Medication note\",\n  \"instruction\": \"If the section discusses a medication change, the medication note goes at its end; otherwise nothing.\",\n"
                + "  \"if\": \"input.note_type == \\\"consult\\\"\"\n}\nThe medication change was discussed with Margaret Okafor.\n";
            files["prompts/narrative_draft.md"] += "{{ macro.loop_note.prompt }}\n";
        });

    [Fact]
    public void AModelChosenMacro_GetsAJudgementNode()
    {
        // house_style is placed through the snippet style, which summary places: its classifier is summary's sibling, just
        // before it, as are the folder's red_flags and safety_net (summary places each), in the order the macros are written.
        var result = MigrateNodesTests.Migrated("example-macros");
        Assert.Equal(
            """["standing_sections","requested_sections","house_style_applies","red_flags_applies","safety_net_applies","summary","patient_line"]""",
            MigrateNodesTests.Json(result, "nodes/index.json")["items"]!.ToJsonString());
        var (header, body) = MigrateNodesTests.Md(result, "nodes/house_style_applies.md");
        Assert.Equal(
            """{"kind":"agent","producer":"generated","label":"Whether House-style reminder applies","description":null,"format":"judgement","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":null,"parts":null,"ordered":false}""",
            header.ToJsonString());

        // Its prompt reads what summary's reads outside an if or a for, but its snippets (style places the macro itself, and
        // a snippet's text would stand for the question), then ends with an empty question line.
        Assert.Equal("{{ input.consult_draft.content }}\n\n", body);

        // Inside a loop and a case branch: the classifier is the placing node's sibling in that branch, its if the macro's
        // own offer condition.
        var loop = WithLoopNote();
        var route = MigrateNodesTests.Json(loop, "nodes/requested_sections/route/index.json");
        Assert.Equal("""["loop_note_applies","narrative_draft"]""", route["branches"]![0]!["nodes"]!.ToJsonString());
        var (inLoop, prompt) = MigrateNodesTests.Md(loop, "nodes/requested_sections/route/loop_note_applies.md");
        Assert.Equal("""{"===":[{"var":"input.note_type.value"},"consult"]}""", inLoop["if"]!.ToJsonString());
        Assert.Equal("judgement", (string?)inLoop["format"]);
        Assert.Equal("{{ item.value.title }}\n\n{{ input.consult_draft.content }}\n\n", prompt);
    }

    [Fact]
    public void TheMacrosIf_ReadsTheAnswer()
    {
        var result = MigrateNodesTests.Migrated("example-macros");
        Assert.Equal(Answer("node.house_style_applies"), MigrateNodesTests.Md(result, "macros/house_style.md").Header["if"]!.ToJsonString());
        Assert.Equal(Answer("node.red_flags_applies"), MigrateNodesTests.Md(result, "macros/notes/red_flags.md").Header["if"]!.ToJsonString());

        // The macro's own if first, then the answer, read as the sibling it is where the macro is placed (per item).
        var loop = WithLoopNote();
        Assert.Equal(
            """{"and":[{"===":[{"var":"input.note_type.value"},"consult"]},""" + Answer("node.loop_note_applies") + "]}",
            MigrateNodesTests.Md(loop, "macros/loop_note.md").Header["if"]!.ToJsonString());

        // The instruction is 30's, as area 10 wrote it: the "otherwise" clause is the operator's to remove.
        Assert.Equal(
            "If the section discusses a medication change, the medication note goes at its end; otherwise nothing.",
            (string?)MigrateNodesTests.Md(loop, "macros/loop_note.md").Header["instruction"]![0]);
    }

    [Fact]
    public void TheEmptyQuestion_IsAHandEdit_AndPublishRefusesIt()
    {
        var result = LetrozoleInTheLoop();
        const string File = "nodes/standards/letrozole_prescription_text_applies.md";
        AssertHandEdit(result, File, "body", "A10-077",
            "node 'letrozole_prescription_text_applies' decides whether macro 'letrozole_prescription_text' is offered, which 30's model chose; write its yes-or-no question on the prompt's last line, and remove the \"otherwise\" clause from the macro's instruction.");
        Assert.Equal(
            """["letrozole_prescription_text_applies","section_instructions","patient_section_draft","standard_section_draft","section_instructions_stripped"]""",
            MigrateNodesTests.Json(result, "nodes/standards/index.json")["nodes"]!.ToJsonString());
        Assert.Equal(Answer("node.letrozole_prescription_text_applies"), MigrateNodesTests.Md(result, "macros/letrozole_prescription_text.md").Header["if"]!.ToJsonString());

        // Published unedited, the question is missing (A11-013), and nothing else is refused.
        var refusal = Assert.Single(MigratedPackagesTests.Publish(result));
        Assert.Equal(("A11-013", File, "Node 'letrozole_prescription_text_applies' asks nothing: its prompt has no question."), (refusal.Id, refusal.Path, refusal.Sentence));
        V31Contract.AssertSentence(refusal);

        // The question written, it publishes.
        var asked = result with { Files = new Dictionary<string, string>(result.Files, StringComparer.Ordinal) { [File] = result.Files[File] + "Does the plan prescribe letrozole?\n" } };
        Assert.Empty(MigratedPackagesTests.Publish(asked));
    }

    [Fact]
    public void AMacroPlacedByTwoNodes_GetsAClassifierEach()
    {
        // patient_line places house_style too; its placement comes first (30's nodes, then the snippets), so its classifier
        // takes the id and summary's is numbered (S10-006). The macro's if reads both answers.
        var result = MigrateNodesTests.Migrated("example-macros", _ => { }, files => files["prompts/patient_line.md"] += "{{ macro.house_style.prompt }}\n");
        Assert.Equal(
            """["standing_sections","requested_sections","house_style_applies_2","red_flags_applies","safety_net_applies","summary","house_style_applies","patient_line"]""",
            MigrateNodesTests.Json(result, "nodes/index.json")["items"]!.ToJsonString());
        Assert.Contains("Renamed 'house_style_applies' to 'house_style_applies_2'; 'house_style_applies' was already taken.", result.Notices);
        Assert.Equal(
            """{"and":[""" + Answer("node.house_style_applies") + "," + Answer("node.house_style_applies_2") + "]}",
            MigrateNodesTests.Md(result, "macros/house_style.md").Header["if"]!.ToJsonString());
        Assert.Equal(2, result.HandEdits.Count(h => h.StartsWith("Hand edit needed in nodes/house_style_applies", StringComparison.Ordinal)));

        // Fix round 1: 30's two models chose each on its own, so the joined if is a hand edit (A10-078).
        AssertHandEdit(result, "macros/house_style.md", "if", "A10-078",
            "macro 'house_style' is placed by 2 nodes, with a classifier each (house_style_applies, house_style_applies_2), and its if needs every answer yes, where 30's model chose at each node on its own; split the macro into one per placing node, or choose its condition.");

        // One placing node: the if is no hand edit.
        Assert.DoesNotContain(MigrateNodesTests.Migrated("example-macros").HandEdits, h => h.StartsWith("Hand edit needed in macros/house_style.md, if:", StringComparison.Ordinal));
    }

    [Fact]
    public void AMacroWithNoInstructionOrATemplatedOne_IsUnchanged()
    {
        // silent_reminder's instruction is {{ "" }}, wrap_up's reads another macro, header_line has none, and the engine placed
        // preamble, sign_off, table_note, section_caveat and counselling (instruction []): no classifier, no answer read.
        var result = MigrateNodesTests.Migrated("example-macros");
        foreach (var id in (string[])["silent_reminder", "wrap_up", "header_line", "preamble", "sign_off", "table_note", "section_caveat", "counselling"])
        {
            Assert.DoesNotContain(result.Files.Keys, f => f.EndsWith($"/{id}_applies.md", StringComparison.Ordinal));
            Assert.DoesNotContain(".value.answer", MigrateNodesTests.Md(result, $"macros/{id}.md").Header["if"]?.ToJsonString() ?? "", StringComparison.Ordinal);
        }

        Assert.Null(MigrateNodesTests.Md(result, "macros/silent_reminder.md").Header["if"]);
    }

    [Fact]
    public void AnOptionalMacro_KeepsItsInput_AndGainsTheClassifier()
    {
        // Part 10 § 1.3: the choice input still offers it; the classifier is asked only when the choice holds.
        var result = WithLetrozole();
        const string Choice = """{"===":[{"var":"input.letrozole_macro.value"},true]}""";
        Assert.Equal("""{"and":[""" + Choice + "," + Answer("node.letrozole_applies") + "]}", MigrateNodesTests.Md(result, "macros/letrozole.md").Header["if"]!.ToJsonString());
        Assert.Equal(Choice, MigrateNodesTests.Md(result, "nodes/letrozole_applies.md").Header["if"]!.ToJsonString());
        Assert.NotNull(JsonNode.Parse(result.ManifestJson)!["inputs"]!["letrozole_macro"]);
    }
}
