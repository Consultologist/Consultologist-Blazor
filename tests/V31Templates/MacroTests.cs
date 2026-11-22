using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Templates;
using static Consultologist.Api.Tests.V31Templates.TemplateFixture;

namespace Consultologist.Api.Tests.V31Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 8.1 to 8.5, 8.7; part 03 § 4.6; D17): macros at publish. Only an agent's
/// prompt, a part's text or a snippet placed in the prompt places a macro (S05-047), in a node returning a text format
/// (S05-049) that takes the macro's format (S05-046), at most once a template (S05-048). Its instruction holds its own
/// marker exactly once, qualified for a folder macro (S05-044), in an allowed form, no other macro's marker (S05-045)
/// and no placement (A07-003); its body places nothing (S05-042, S05-043), holds no marker (S05-051) and suits its
/// format (S05-050). A prompt reading a node that carries profile text through a macro is S05-064. Content is
/// fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class MacroTests
{
    private const string Letrozole = "letrozole_prescription_text";

    [Fact]
    public void TheLetrozoleExample_IsValid()
    {
        var check = Check(LetrozoleFiles());

        Assert.Empty(check.Refusals);
        var prompt = Body(check, "nodes/assessment.md");
        var unit = Assert.Single(prompt.Units);
        Assert.Equal($"macro.{Letrozole}", unit.Site);
        Assert.Null(unit.Within);
        Assert.Equal("{\"===\":[{\"var\":\"node.prescribes_letrozole.value.answer\"},true]}", unit.If!.ToJsonString());
        Assert.Equal(new MacroMarker(Letrozole, $"macro.{Letrozole}.content", true), unit.Marker);
        Assert.Same(unit.Body, unit.Instruction);
        Assert.Equal(TemplateSite.MacroInstruction, unit.Instruction!.Host.Site);
        Assert.Equal($"macros/{Letrozole}.md", unit.Instruction.Host.Path);
        Assert.Equal(
            "Place the following marker, alone on its own line, in the assessment and plan,\nafter the medication discussion:\n"
            + $"{{{{~ macro.{Letrozole}.content }}}}",
            unit.Instruction.Syntax.Text);
        Assert.Empty(unit.Instruction.Reads);
        Assert.Equal(TemplateSite.MacroBody, unit.MacroBody!.Host.Site);
        Assert.StartsWith("Discussed side effects", unit.MacroBody.Syntax.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("S05-042")]
    [InlineData("S05-042 through a snippet")]
    [InlineData("S05-043")]
    [InlineData("S05-044 none")]
    [InlineData("S05-044 two")]
    [InlineData("S05-045")]
    [InlineData("S05-046 block into inline")]
    [InlineData("S05-046 markdown into plaintext")]
    [InlineData("S05-047 rendered")]
    [InlineData("S05-047 deliverable through a snippet")]
    [InlineData("S05-048")]
    [InlineData("S05-049")]
    [InlineData("S05-050")]
    [InlineData("S05-051")]
    [InlineData("A07-003 macro")]
    [InlineData("A07-003 part")]
    [InlineData("S05-011 unknown macro")]
    [InlineData("S05-020 profile in the instruction")]
    [InlineData("S05-015 ~}} on the marker")]
    [InlineData("S05-016 - on the marker")]
    [InlineData("S05-017 block marker inside a line")]
    [InlineData("S05-018 text after a block marker")]
    public void EachMacroRule_IsRefusedWithItsSentence(string name)
    {
        var (files, sentence, path) = Invalid(name);

        var refusal = TemplateFixture.Single(Check(files));

        Assert.Equal(name.Split(' ')[0], refusal.Id);
        Assert.Equal(sentence, refusal.Sentence);
        Assert.Equal(path, refusal.Path);
    }

    [Fact]
    public void BothMarkerForms_AreRead()
    {
        var block = Unit(Check(Package().WithMacroOf("m", "Follow up with Dr A. Sample in six weeks.\n", ["Place it alone after the plan:", "{{~ macro.m.content }}"])
            .WithAgent("draft", "Draft the note.\n{{~ macro.m }}\n")), "nodes/draft.md");
        var inline = Unit(Check(Package().WithMacroOf("m", "Follow up with Dr A. Sample in six weeks.\n", ["Close the plan with {{ macro.m.content }} as its last sentence."])
            .WithAgent("draft", "Draft the note.\n{{~ macro.m }}\n")), "nodes/draft.md");

        Assert.Equal(new MacroMarker("m", "macro.m.content", true), block.Marker);
        Assert.Equal("{{~ macro.m.content }}", MacroMarkers.Open(block.Marker!));
        Assert.Equal(new MacroMarker("m", "macro.m.content", false), inline.Marker);
        Assert.Equal("{{ macro.m.content }}", MacroMarkers.Open(inline.Marker!));
    }

    [Fact]
    public void AFolderMacro_HasAQualifiedMarker()
    {
        var valid = Check(SectionNotes("{{~ macro.section_notes.hpi.content }}"));

        var unit = Unit(valid, "nodes/draft.md");
        Assert.Equal("macro.section_notes.hpi", unit.Site);
        Assert.Equal(new MacroMarker("section_notes.hpi", "macro.section_notes.hpi.content", true), unit.Marker);
        Assert.Equal("macros/section_notes/hpi.md", unit.Instruction!.Host.Path);

        // The bare id is not its marker: no marker of its own, and macro.hpi names no macro.
        var bare = Check(SectionNotes("{{~ macro.hpi.content }}"));
        Assert.All(bare.Refusals, V31Contract.AssertSentence);
        Assert.Contains(bare.Refusals, r => r.Id == "S05-044" && r.Path == "macros/section_notes/hpi.md" && r.Sentence ==
            "Macro 'section_notes.hpi' has 0 markers in its instruction; it must hold {{~ macro.section_notes.hpi.content }} or {{ macro.section_notes.hpi.content }} exactly once.");
    }

    [Theory]
    [InlineData("Place it here: {{# {{ macro.m.content }} }}")]
    [InlineData("Place it here: {%{{{ macro.m.content }}}%}")]
    public void AMarkerInACommentOrARawBlock_IsNoMarker(string line)
    {
        var none = TemplateFixture.Single(Check(Package().WithMacroOf("m", "Follow up.\n", [line]).WithAgent("draft", "Draft.\n{{~ macro.m }}\n")));
        Assert.Equal("S05-044", none.Id);
        Assert.Equal("Macro 'm' has 0 markers in its instruction; it must hold {{~ macro.m.content }} or {{ macro.m.content }} exactly once.", none.Sentence);

        var withOne = Check(Package().WithMacroOf("m", "Follow up.\n", [line, "Then: {{ macro.m.content }}"]).WithAgent("draft", "Draft.\n{{~ macro.m }}\n"));
        Assert.Empty(withOne.Refusals);
        Assert.Equal(new MacroMarker("m", "macro.m.content", false), Unit(withOne, "nodes/draft.md").Marker);
    }

    [Fact]
    public void AMacroPlacedThroughAPartAndThePrompt_IsPlacedTwice()
    {
        var files = Package()
            .WithMacro("m", "Follow up with Dr A. Sample in six weeks.\n")
            .WithAgent("draft", "Draft the note.\n{{~ part.closing }}\n{{~ macro.m }}\n")
            .WithJson("nodes/draft.md", h => h["parts"] = JsonNode.Parse("{ \"closing\": { \"if\": null, \"text\": [\"Close the note.\", \"{{~ macro.m }}\"] } }"));

        var refusal = TemplateFixture.Single(Check(files));

        Assert.Equal("S05-048", refusal.Id);
        Assert.Equal("Node 'draft' places macro.m twice, counting its parts' text; place a macro once in a template.", refusal.Sentence);
        Assert.Equal("nodes/draft.md", refusal.Path);

        var once = Body(Check(Package()
            .WithMacro("m", "Follow up with Dr A. Sample in six weeks.\n")
            .WithAgent("draft", "Draft the note.\n{{~ part.closing }}\n")
            .WithJson("nodes/draft.md", h => h["parts"] = JsonNode.Parse("{ \"closing\": { \"if\": null, \"text\": [\"Close the note.\", \"{{~ macro.m }}\"] } }"))),
            "nodes/draft.md");
        Assert.Equal([("part.closing", null), ("macro.m", "part.closing")], once.Units.Select(u => (u.Site, u.Within)));
    }

    [Theory]
    [InlineData("plaintext", "\"markdown_block\"")]
    [InlineData("plaintext", "\"markdown_inline\"")]
    [InlineData("markdown_inline", "\"markdown_block\"")]
    [InlineData("markdown_inline", "\"markdown_inline\"")]
    [InlineData("markdown_block", "\"markdown_block\"")]
    public void APlaintextMacroInAMarkdownNode_IsTaken(string macroFormat, string nodeFormat)
    {
        var files = Package()
            .WithMacroOf("m", "Follow up with Dr A. Sample in six weeks.\n", ["Close with {{ macro.m.content }}."], macroFormat)
            .WithAgent("draft", "Draft the note.\n{{~ macro.m }}\n", nodeFormat);

        Assert.Empty(Check(files).Refusals);
    }

    [Fact]
    public void AMacroBodyReadingProfile_IsValid_AndThePromptReadingItsNode_IsRefused()
    {
        static Dictionary<string, byte[]> Files() => Package()
            .WithMacro("signoff", "With thanks,\n{{ profile.signature }}\n")
            .WithAgent("draft", "Draft the note for Margaret Okafor.\n{{~ macro.signoff }}\n")
            .WithJson("nodes/draft.md", h => h["profileCarrying"] = true);

        var valid = Check(Files());
        Assert.Empty(valid.Refusals);
        Assert.Equal(["profile.signature"], Unit(valid, "nodes/draft.md").MacroBody!.Reads.Select(r => r.Written));

        var refusal = TemplateFixture.Single(Check(Files().WithAgent("refine", "Refine the note.\n\n{{ node.draft.content }}\n")));
        Assert.Equal("S05-064", refusal.Id);
        Assert.Equal(
            "Node 'refine' reads node.draft.content at line 3, which carries profile text through macro 'signoff'; a prompt or a part doesn't read profile text. Read it in a deliverable.",
            refusal.Sentence);
        Assert.Equal("nodes/refine.md", refusal.Path);

        // A deliverable may read it.
        Assert.Empty(Check(Files().WithDeliverable("{{ node.draft.content }}\n")).Refusals);
    }

    [Theory]
    [InlineData("Dose: 2.5 mg - once daily.\n", null)]
    [InlineData("{{ input.consult_draft.content }} - as discussed.\n", null)]
    [InlineData("Seen today.\n  > quoted\n", 2)]
    [InlineData("Seen today.\n- one\n", 2)]
    [InlineData("A **firm** plan.\n", 1)]
    [InlineData("A **{{ input.consult_draft.content }}** plan.\n", 1)]
    [InlineData("Seen.\n{%{# Plan}%}\n", 2)]
    [InlineData("Seen.{{# * not text }}\n", null)]
    [InlineData("{{ for s in node.plan.value.sections }}# {{ s.title }}\n{{ end }}", 1)]
    public void APlaintextBodysLiteralText_WritesNoMarkdown(string body, int? line)
    {
        var refusals = Check(Package().WithMacroOf("m", body, ["Close with {{ macro.m.content }}."]).WithAgent("draft", "Draft.\n{{~ macro.m }}\n")).Refusals;

        if (line is null)
        {
            Assert.Empty(refusals);
            return;
        }

        var refusal = Assert.Single(refusals);
        V31Contract.AssertSentence(refusal);
        Assert.Equal($"Macro 'm' has format plaintext, and its body writes Markdown at line {line}.", refusal.Sentence);
    }

    [Fact]
    public void AMacroThroughASnippetInThePrompt_IsPlacedWithinIt()
    {
        var files = Package()
            .WithMacro("m", "Follow up with Dr A. Sample in six weeks.\n")
            .WithSnippet("a", "Then: {{ macro.m }}\n")
            .WithAgent("draft", "Draft the note.\n{{~ snippet.a.content }}\n");

        var body = Body(Check(files), "nodes/draft.md");

        Assert.Equal([("snippet.a", null), ("macro.m", "snippet.a")], body.Units.Select(u => (u.Site, u.Within)));
    }

    [Fact]
    public void AMacrosInstructionAndBody_PlaceSnippets()
    {
        var files = Package()
            .WithSnippet("where", "in the assessment and plan")
            .WithSnippet("sign", "{{ profile.name }}")
            .WithMacroOf("m", "Reviewed by {{ snippet.sign.content }}.\n", ["Place {{ macro.m.content }} {{ snippet.where.content }}."])
            .WithAgent("draft", "Draft the note.\n{{~ macro.m }}\n");

        var body = Body(Check(files), "nodes/draft.md");
        var unit = body.Units[0];
        Assert.Equal([("macro.m", null), ("snippet.where", "macro.m")], body.Units.Select(u => (u.Site, u.Within)));
        var placed = Assert.Single(unit.MacroBody!.Units);
        Assert.Equal(("snippet.sign", "macro.m"), (placed.Site, placed.Within));
        Assert.Equal(["profile.name"], placed.Body.Reads.Select(r => r.Written));

        // Under the guard: a snippet in the instruction places no macro (A07-001).
        var guarded = TemplateFixture.Single(Check(Package()
            .WithMacro("other", "Other text.\n")
            .WithSnippet("where", "here {{ macro.other }}")
            .WithMacroOf("m", "Follow up.\n", ["Place {{ macro.m.content }} {{ snippet.where.content }}."])
            .WithAgent("draft", "Draft the note.\n{{~ macro.m }}\n")));
        Assert.Equal(
            "Snippet 'where' places macro.other, and Macro 'm' places it in a macro's instruction; a snippet placed there doesn't place parts or macros.",
            guarded.Sentence);
    }

    [Fact]
    public void ASnippetInAMacroBody_IsCheckedAsTheBody()
    {
        // Fix round 1 (I2): a snippet a macro's body places is held to the macro-body row: profile.* is read there, while the
        // same snippet in a prompt is refused (S05-022); placing a part or a macro through it is S05-042 or S05-043.
        var inBody = Check(Package()
            .WithSnippet("sign", "{{ profile.signature }}")
            .WithMacro("m", "With thanks, {{ snippet.sign.content }}\n")
            .WithAgent("draft", "Draft the note.\n{{~ macro.m }}\n")
            .WithJson("nodes/draft.md", h => h["profileCarrying"] = true));
        Assert.Empty(inBody.Refusals);
        Assert.Equal(["profile.signature"], Assert.Single(Unit(inBody, "nodes/draft.md").MacroBody!.Units).Body.Reads.Select(r => r.Written));

        var inPrompt = TemplateFixture.Single(Check(Package()
            .WithSnippet("sign", "{{ profile.signature }}")
            .WithAgent("draft", "Draft the note.\n{{~ snippet.sign.content }}\n")));
        Assert.Equal("S05-022", inPrompt.Id);

        var placing = TemplateFixture.Single(Check(Package()
            .WithMacro("other", "Other text.\n")
            .WithSnippet("sign", "{{ macro.other }}")
            .WithMacro("m", "With thanks, {{ snippet.sign.content }}\n")
            .WithAgent("draft", "Draft the note.\n{{~ macro.m }}\n")));
        Assert.Equal("S05-042", placing.Id);
        V31Contract.AssertSentence(placing);
    }

    [Fact]
    public void AMacro_IsCheckedOncePerPlace_NotPerPlacer()
    {
        var files = Package().WithMacro("m", string.Concat(Enumerable.Repeat("{{ input.consult_draft.content }}\n", 2_000)));
        for (var i = 0; i < 8; i++)
            files.WithAgent($"draft_{i}", "Draft.\n{{~ macro.m }}\n");

        var check = Check(files);

        Assert.Empty(check.Refusals);
        var units = check.Templates!.Bodies.Where(b => b.Host.Path.StartsWith("nodes/draft_", StringComparison.Ordinal)).SelectMany(b => b.Units).ToList();
        Assert.Equal(8, units.Count);
        Assert.Single(units.Select(u => u.Instruction).Distinct(ReferenceEqualityComparer.Instance));
        Assert.Single(units.Select(u => u.MacroBody).Distinct(ReferenceEqualityComparer.Instance));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[\"{{ macro.m.content }} {{ macro.m.content }}\"]")]
    [InlineData("[\"{{~ macro.m.content ~}}\"]")]
    [InlineData("[\"{{ x = 1 }} {{ macro.m.content }}\"]")]
    [InlineData("[\"{{ for s in node.plan.value.sections }}{{ macro.m.content }}{{ end }}\"]")]
    [InlineData("[\"{{ macro.m.content | string.upcase }} {{ part }} {{ macro }} {{ macro.a.b.c.d }}\"]")]
    [InlineData("[\"{{ if macro.m }}x{{ end }} {{ macro.m.content }}\"]")]
    public void AnyInstruction_IsCheckedOrRefused_NeverThrows(string instruction)
    {
        var files = Package()
            .WithMacroOf("m", "Follow up.\n", JsonNode.Parse(instruction)!.AsArray().Select(l => (string)l!).ToArray())
            .WithAgent("draft", "Draft.\n{{~ macro.m }}\n");

        var check = Check(files);

        Assert.NotEmpty(check.Refusals);
        Assert.All(check.Refusals, V31Contract.AssertSentence);
    }

    // ---- packages ------------------------------------------------------------------------------

    /// <summary>The refusing package, sentence and path of each row of <see cref="EachMacroRule_IsRefusedWithItsSentence"/>.</summary>
    private static (Dictionary<string, byte[]> Files, string Sentence, string Path) Invalid(string name)
    {
        const string Body = "Follow up with Dr A. Sample in six weeks.\n";
        var placing = Package().WithAgent("draft", "Draft the note.\n{{~ macro.m }}\n");
        var inline = new[] { "Close the plan with {{ macro.m.content }}." };
        const string MacroFile = "macros/m.md";
        const string Output = "a macro body is output text, written after the model replies. Put shared text in a snippet and place it in both.";

        return name switch
        {
            "S05-042" => (placing.WithMacro("other", Body).WithMacroOf("m", "See {{ macro.other }}.\n", inline),
                $"Macro 'm' places macro.other in its body; {Output}", MacroFile),
            "S05-042 through a snippet" => (placing.WithMacro("other", Body).WithSnippet("a", "{{ macro.other }}")
                    .WithMacroOf("m", "See {{ snippet.a.content }}.\n", inline),
                $"Macro 'm' places macro.other in its body; {Output}", MacroFile),
            "S05-043" => (placing.WithMacroOf("m", "{{ part.brief }}\n", inline),
                $"Macro 'm' places part.brief in its body; {Output}", MacroFile),
            "S05-044 none" => (placing.WithMacroOf("m", Body, ["Place it at the end of the plan."]),
                "Macro 'm' has 0 markers in its instruction; it must hold {{~ macro.m.content }} or {{ macro.m.content }} exactly once.", MacroFile),
            "S05-044 two" => (placing.WithMacroOf("m", Body, ["Place {{ macro.m.content }} here,", "or {{ macro.m.content }} there."]),
                "Macro 'm' has 2 markers in its instruction; it must hold {{~ macro.m.content }} or {{ macro.m.content }} exactly once.", MacroFile),
            "S05-045" => (placing.WithMacro("other", Body).WithMacroOf("m", Body, ["Place {{ macro.m.content }} after {{ macro.other.content }}."]),
                "Macro 'm' has the marker of macro 'other' in its instruction.", MacroFile),
            "S05-046 block into inline" => (Package().WithMacroOf("m", "**Follow up** in six weeks.\n", inline, "markdown_block")
                    .WithAgent("draft", "Draft the note.\n{{~ macro.m }}\n", "\"markdown_inline\""),
                "Node 'draft' places macro.m, whose format is markdown_block; an markdown_inline node takes plaintext and markdown_inline.", "nodes/draft.md"),
            "S05-046 markdown into plaintext" => (Package().WithMacroOf("m", "*Follow up* in six weeks.\n", inline, "markdown_inline")
                    .WithAgent("draft", "Draft the note.\n{{~ macro.m }}\n"),
                "Node 'draft' places macro.m, whose format is markdown_inline; an plaintext node takes plaintext.", "nodes/draft.md"),
            "S05-047 rendered" => (Package().WithMacro("m", Body).WithRendered("summary", "{{ node.plan.content }}\n{{~ macro.m }}\n"),
                "Node 'summary' places macro.m; only agent nodes place macros, in their prompt or a part's text.", "nodes/summary.md"),
            "S05-047 deliverable through a snippet" => (Package().WithMacro("m", Body).WithSnippet("a", "Then: {{ macro.m }}\n")
                    .WithDeliverable("{{ node.plan.content }}\n{{~ snippet.a.content }}\n"),
                "Deliverable 'note' places macro.m; only agent nodes place macros, in their prompt or a part's text.", "deliverables/note.md"),
            "S05-048" => (Package().WithMacro("m", Body).WithSnippet("a", "Then: {{ macro.m }}\n")
                    .WithAgent("draft", "Draft the note.\n{{~ macro.m }}\n{{~ snippet.a.content }}\n"),
                "Node 'draft' places macro.m twice, counting its parts' text; place a macro once in a template.", "nodes/draft.md"),
            "S05-049" => (Package().WithMacro("m", Body).WithAgent("draft", "Judge the plan.\n{{~ macro.m }}\n", "\"verdict\""),
                "Node 'draft' places macro.m, and returns verdict; macros are placed only in agent nodes returning a text format.", "nodes/draft.md"),
            "S05-050" => (placing.WithMacroOf("m", "Follow up in six weeks.\n# Plan\n", inline),
                "Macro 'm' has format plaintext, and its body writes Markdown at line 2.", MacroFile),
            "S05-051" => (placing.WithMacroOf("m", "Then {{ macro.m.content }}.\n", inline),
                "Macro 'm' has a marker in its body; a body is the text that replaces the marker.", MacroFile),
            "A07-003 macro" => (placing.WithMacro("other", Body).WithMacroOf("m", Body, ["Place {{ macro.m.content }}, then {{ macro.other }}."]),
                "Macro 'm' places macro.other in its instruction; combine instructions by placing each macro in the prompt.", MacroFile),
            "A07-003 part" => (placing.WithMacroOf("m", Body, ["Place {{ macro.m.content }} as {{ part.brief }} says."]),
                "Macro 'm' places part.brief in its instruction; combine instructions by placing each macro in the prompt.", MacroFile),
            "S05-011 unknown macro" => (Package().WithAgent("draft", "Draft the note.\n{{~ macro.nope }}\n"),
                "Node 'draft' reads macro.nope at line 2, which macro.* doesn't have.", "nodes/draft.md"),
            "S05-020 profile in the instruction" => (placing.WithMacroOf("m", Body, ["Place {{ macro.m.content }} for {{ profile.name }}."]),
                "Macro 'm' reads profile.* at line 1; macro instruction reads what the placing prompt reads, snippet.* included, except macro.* and part.*.", MacroFile),
            "S05-015 ~}} on the marker" => (placing.WithMacroOf("m", Body, ["Place it alone:", "{{ macro.m.content ~}}", "Then sign."]),
                "Macro 'm' places macro.m.content with ~}} at line 2; it removes the line's newline, joining two lines. Use {{~ macro.m.content }} alone on a line.", MacroFile),
            "S05-016 - on the marker" => (placing.WithMacroOf("m", Body, ["Place it alone:", "{{- macro.m.content }}"]),
                "Macro 'm' places macro.m.content with - at line 2; it removes blank lines between paragraphs.", MacroFile),
            "S05-017 block marker inside a line" => (placing.WithMacroOf("m", Body, ["Place {{~ macro.m.content }} at the end."]),
                "Macro 'm' places {{~ macro.m.content }} inside a line at line 1; it removes the space before it. Use {{ macro.m.content }}.", MacroFile),
            "S05-018 text after a block marker" => (placing.WithMacroOf("m", Body, ["{{~ macro.m.content }} goes at the end."]),
                "Macro 'm' has text after {{~ macro.m.content }} at line 1; a block placement stands alone on its line.", MacroFile),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
    }

    /// <summary>Part 05 § 8.4's files: the classifier <c>prescribes_letrozole</c> (format <c>judgement</c>), the macro, and <c>assessment</c> placing it.</summary>
    private static Dictionary<string, byte[]> LetrozoleFiles() =>
        Package()
            .WithAgent("prescribes_letrozole", "Does the plan for Margaret Okafor prescribe letrozole?\n\n{{ node.plan.content }}\n", "\"judgement\"")
            .WithMacroOf(Letrozole,
                "Discussed side effects including: hot flashes, joint or muscle pain, fatigue, headache, nausea,\nand bone thinning. Advised to monitor side effects.\n",
                ["Place the following marker, alone on its own line, in the assessment and plan,", "after the medication discussion:", $"{{{{~ macro.{Letrozole}.content }}}}"],
                condition: "{ \"===\": [ { \"var\": \"node.prescribes_letrozole.value.answer\" }, true ] }")
            .WithAgent("assessment", "Write the assessment and plan for Dr A. Sample.\n\n{{ node.plan.content }}\n{{~ macro." + Letrozole + " }}\n", "\"markdown_block\"");

    /// <summary>The macro folder <c>section_notes</c> holding <c>hpi</c>, whose instruction is <paramref name="marker"/>'s line, and <c>draft</c> placing it.</summary>
    private static Dictionary<string, byte[]> SectionNotes(string marker) =>
        Package()
            .Listed("macros/index.json", "section_notes")
            .With("macros/section_notes/index.json", SourceFixture.Index(["hpi"], extra: ", \"if\": null"))
            .With("macros/section_notes/hpi.md", SourceFixture.Md(
                "{ \"label\": \"History\", \"description\": null, \"if\": null, \"format\": \"plaintext\", "
                + $"\"instruction\": [\"Place the history's closing line alone:\", \"{marker}\"] }}",
                "History taken from Margaret Okafor and her daughter.\n"))
            .WithAgent("draft", "Draft the history.\n{{~ macro.section_notes.hpi }}\n");

    /// <summary>The one unit of the body of <paramref name="path"/>.</summary>
    private static PlacedUnit Unit(TemplateCheck check, string path) => Assert.Single(Body(check, path).Units);
}

/// <summary>#1504: a macro with its instruction, format and <c>if</c> as a test writes them.</summary>
internal static class MacroFixture
{
    /// <summary>Adds the macro <paramref name="id"/> with <paramref name="instruction"/>'s lines, of <paramref name="format"/>, decided by <paramref name="condition"/> (JSON text).</summary>
    public static Dictionary<string, byte[]> WithMacroOf(
        this Dictionary<string, byte[]> files, string id, string body, string[] instruction, string format = "plaintext", string condition = "null") =>
        files.Listed("macros/index.json", id).With($"macros/{id}.md", SourceFixture.Md(
            $"{{ \"label\": \"Macro {id}\", \"description\": null, \"if\": {condition}, \"format\": \"{format}\", "
            + $"\"instruction\": {new JsonArray(instruction.Select(l => (JsonNode)l!).ToArray()).ToJsonString()} }}", body));
}
