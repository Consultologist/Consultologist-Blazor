using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Templates;
using static Consultologist.Api.Tests.V31Source.SourceFixture;
using static Consultologist.Api.Tests.V31Templates.TemplateFixture;

namespace Consultologist.Api.Tests.V31Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 3.1, § 2.3 strict variables; part 02 § 2.1; part 04 § 2.6; part 06 § 10.1):
/// what each site of this task reads (an agent's prompt, a <c>rendered</c> node, a deliverable, a renderer), and how a
/// read is typed: its head resolved where the body is, the rest read on the head's format.
/// </summary>
public class SiteReadsTests
{
    /// <summary>
    /// One row per cell of § 3.1's table for the prompt, the <c>rendered</c> node and the deliverable: the site, where the
    /// body is (<c>top</c>, <c>loop</c>, <c>while</c>, <c>function</c>), the body, and the sentence, or <c>null</c> for
    /// an allowed read. (A renderer's "everything else" is Task 4's S05-029 and S05-033.)
    /// </summary>
    public static TheoryData<string, string, string, string?> Cells => new()
    {
        // prompt: reads
        { "prompt", "top", "{{ node.plan.content }}", null },
        { "prompt", "top", "{{ input.consult_draft.content }}", null },
        { "prompt", "top", "{{ check.coverage.value.pass }}", null },
        { "prompt", "loop", "{{ item.value.title }}", null },
        { "prompt", "while", "{{ pass.number }}", null },
        { "prompt", "function", "{{ param.draft.content }}", null },
        { "prompt", "top", "{{~ snippet.tone.content }}", null },
        { "prompt", "top", "{{~ part.brief }}", null },
        { "prompt", "top", "{{~ macro.follow_up }}", null },
        { "prompt", "top", "{{ node.extract_input_terms.value | renderer.concept_bullets }}", null },
        { "prompt", "top", "{{ run.date }}", null },
        // prompt: never
        { "prompt", "top", "{{ profile.signature }}", "S05-021" },
        { "prompt", "top", "{{ deliverable.note.content }}", "S05-020" },
        // rendered node: reads
        { "rendered", "top", "{{ node.plan.content }}", null },
        { "rendered", "top", "{{ input.consult_draft.content }}", null },
        { "rendered", "top", "{{ check.coverage.value.pass }}", null },
        { "rendered", "loop", "{{ item.value.title }}", null },
        { "rendered", "while", "{{ pass.number }}", null },
        { "rendered", "function", "{{ param.draft.content }}", null },
        { "rendered", "top", "{{~ snippet.tone.content }}", null },
        { "rendered", "top", "{{ node.extract_input_terms.value | renderer.concept_bullets }}", null },
        { "rendered", "top", "{{ run.date }}", null },
        // rendered node: never
        { "rendered", "top", "{{ profile.signature }}", "S05-021" },
        { "rendered", "top", "{{~ part.brief }}", "S05-020" },
        { "rendered", "top", "{{~ macro.follow_up }}", "S05-047" },
        { "rendered", "top", "{{ deliverable.note.content }}", "S05-020" },
        // deliverable: reads
        { "deliverable", "top", "{{ node.plan.content }}", null },
        { "deliverable", "top", "{{ input.consult_draft.content }}", null },
        { "deliverable", "top", "{{~ snippet.tone.content }}", null },
        { "deliverable", "top", "{{ check.coverage.value.pass }}", null },
        { "deliverable", "top", "{{ profile.signature }}", null },
        { "deliverable", "top", "{{ run.date }}", null },
        { "deliverable", "top", "{{ node.extract_input_terms.value | renderer.concept_bullets }}", null },
        { "deliverable", "top", "{{ for s in node.plan.value.sections }}{{ s.title }}{{ end }}", null },
        // deliverable: never
        { "deliverable", "top", "{{ item.value.title }}", "S05-020" },
        { "deliverable", "top", "{{ pass.number }}", "S05-020" },
        { "deliverable", "top", "{{~ part.brief }}", "S05-020" },
        { "deliverable", "top", "{{~ macro.follow_up }}", "S05-047" },
        { "deliverable", "top", "{{ deliverable.note.content }}", "S05-020" },
    };

    [Theory]
    [MemberData(nameof(Cells))]
    public void EachCellOfTheTable_IsReadOrRefused(string site, string place, string body, string? id)
    {
        var (files, path) = AtSite(site, place, body + "\n");

        var check = Check(files);

        if (id == null)
        {
            Assert.True(check.Refusals.Count == 0, Said(check.Refusals));
            var reads = Body(check, path).Reads;
            if (body.Contains("part.", StringComparison.Ordinal) || body.Contains("macro.", StringComparison.Ordinal))
                Assert.Empty(reads);
            else
                Assert.All(reads, r => Assert.NotNull(r.Format));
            Assert.Equal(body.Contains("part.", StringComparison.Ordinal) || body.Contains("macro.", StringComparison.Ordinal), reads.Count == 0);
            return;
        }

        var refusal = TemplateFixture.Single(check);
        Assert.Equal(id, refusal.Id);
    }

    [Fact]
    public void ARootTheSiteNeverReads_NamesTheSiteAndWhatItReads()
    {
        var (files, _) = AtSite("rendered", "top", "Text.\n{{ deliverable.note.content }}\n");

        var refusal = TemplateFixture.Single(Check(files));

        Assert.Equal(
            "Node 'draft' reads deliverable.* at line 2; rendered node reads node.*, input.*, check.*, item, pass, param, snippet.*, the renderer. pipe, run.*.",
            refusal.Sentence);
        Assert.Equal("nodes/draft.md", refusal.Path);
    }

    [Fact]
    public void AProfileRead_InAPrompt_IsTheNodesSentence()
    {
        var (files, _) = AtSite("prompt", "top", "{{ profile.signature }}\n");

        var refusal = TemplateFixture.Single(Check(files));

        Assert.Equal("Node 'draft' reads profile.signature; the clinician's profile is never sent to a model. Read it in a deliverable or a macro body.", refusal.Sentence);
    }

    [Fact]
    public void AFieldOutsideTheProfile_IsRefused()
    {
        var files = Package().WithDeliverable("{{ profile.phone }}\n");

        var refusal = TemplateFixture.Single(Check(files));

        Assert.Equal("S06-028", refusal.Id);
        Assert.Equal("Deliverable 'note' reads profile.phone, which isn't a profile field; the fields are name, credentials, organisation, signature and closing.", refusal.Sentence);
    }

    [Theory]
    [InlineData("{{ nope.x }}", "Node 'draft' reads nope.x at line 1, which prompt doesn't have.")]
    [InlineData("{{ run.weather }}", "Node 'draft' reads run.weather at line 1, which run doesn't have.")]
    [InlineData("{{ node.nope.content }}", "Node 'draft' reads node.nope.content at line 1, which node doesn't have.")]
    [InlineData("{{ for.first }}", "Node 'draft' reads for.first at line 1, which for doesn't have.")]
    public void AnUnknownRootOrMember_IsRefused(string body, string sentence)
    {
        var files = Package().WithAgent("draft", body + "\n");

        var refusal = TemplateFixture.Single(Check(files));

        Assert.Equal("S05-011", refusal.Id);
        Assert.Equal(sentence, refusal.Sentence);
    }

    [Fact]
    public void AMemberTheFormatLacks_IsRefused()
    {
        var files = Package().WithAgent("draft", "Plan:\n{{ node.plan.value.nope }}\n");

        var refusal = TemplateFixture.Single(Check(files));

        Assert.Equal("S05-011", refusal.Id);
        Assert.Equal("Node 'draft' reads node.plan.value.nope at line 2, which section_plan doesn't have.", refusal.Sentence);
    }

    [Theory]
    [InlineData("{{ node.plan }}", "node.plan", ".value")]
    [InlineData("{{ input.consult_draft }}", "input.consult_draft", ".content")]
    [InlineData("{{ check.coverage }}", "check.coverage", ".value")]
    public void ABareRead_IsRefused(string body, string read, string member)
    {
        var files = Package().WithCheck().WithAgent("draft", body + "\n");

        var refusal = TemplateFixture.Single(Check(files));

        Assert.Equal("A07-008", refusal.Id);
        Assert.Equal($"Node 'draft' reads {read} at line 1; read {member} of it.", refusal.Sentence);
    }

    [Fact]
    public void ABareParameter_IsRefused()
    {
        var files = ResolveFixture.Functions().With("functions/draft_section/patient_draft.md",
            Agent("Drafting from the patient's facts", Plain, "{{ param.draft }}\n"));

        var refusal = TemplateFixture.Single(Check(files));

        Assert.Equal("A07-008", refusal.Id);
        Assert.Equal("functions/draft_section/patient_draft.md", refusal.Path);
    }

    [Theory]
    [InlineData("{{ snippet.tone }}", "S05-037", "Node 'draft' reads snippet.tone at line 1; read its text as snippet.tone.content.")]
    [InlineData("{{ node.plan.value }}", "A07-007", "Node 'draft' writes node.plan.value at line 1, which is an object; read a member, loop over it or pipe it into a renderer.")]
    [InlineData("{{ node.plan.value.sections }}", "A07-007", "Node 'draft' writes node.plan.value.sections at line 1, which is a list; read a member, loop over it or pipe it into a renderer.")]
    [InlineData("{{ [1, 2] }}", "A07-007", "Node 'draft' writes [1, 2] at line 1, which is a list; read a member, loop over it or pipe it into a renderer.")]
    [InlineData("{{ renderer.concept_bullets }}", "S05-030", "Node 'draft' calls renderer.concept_bullets at line 1; pipe into it: value | renderer.concept_bullets.")]
    [InlineData("{{ node.plan.value | renderer.concept_bullets(1) }}", "S05-030", "Node 'draft' calls renderer.concept_bullets at line 1; pipe into it: node.plan.value | renderer.concept_bullets.")]
    public void AReadThatIsNoText_IsRefused(string body, string id, string sentence)
    {
        var files = Package().WithSnippet("tone", "Write warmly.\n").WithAgent("draft", body + "\n");

        var refusal = TemplateFixture.Single(Check(files));

        Assert.Equal(id, refusal.Id);
        Assert.Equal(sentence, refusal.Sentence);
    }

    [Fact]
    public void ALocalName_ResolvesToItsRootedKey()
    {
        var files = ResolveFixture.NestedNote();

        var body = Body(Check(files), "nodes/refine/judge.md");

        var read = Assert.Single(body.Reads);
        Assert.Equal("node.draft_summary.content", read.Written);
        Assert.Equal("node.refine.draft_summary.content", read.Canonical);
        Assert.Equal("node.draft_summary", read.Head);
        Assert.Equal("node.refine.draft_summary", read.RootedHead);
        Assert.Equal("plaintext", read.Format!.GetValue<string>());
    }

    [Fact]
    public void AForVariable_IsTypedByItsList()
    {
        var files = Package().WithAgent("draft",
            "{{ for s in node.plan.value.sections }}\n{{ s.title }} ({{ s.kind }})\n{{ end }}\n");

        var body = Body(Check(files), "nodes/draft.md");

        Assert.Equal(["node.plan.value.sections", "s.title", "s.kind"], body.Reads.Select(r => r.Written));
        Assert.Equal("""{"$ref":"plaintext"}""", body.Reads[1].Format!.ToJsonString());
        Assert.Equal("""{"type":"string","enum":["narrative","table","omit"]}""", body.Reads[2].Format!.ToJsonString());
    }

    [Fact]
    public void AForVariable_LacksWhatItsElementLacks()
    {
        var files = Package().WithAgent("draft", "{{ for s in node.plan.value.sections }}\n{{ s.nope }}\n{{ end }}\n");

        var refusal = TemplateFixture.Single(Check(files));

        Assert.Equal("S05-011", refusal.Id);
        Assert.StartsWith("Node 'draft' reads s.nope at line 2, which ", refusal.Sentence);
    }

    [Fact]
    public void AnItemsElement_HasItsRecord()
    {
        var files = Package()
            .WithLoop("sections", "node.plan.value.sections", "outline", "Outline {{ item.value.title }}.\n")
            .WithDeliverable(
                "{{ for s in node.sections.items }}\n{{ s.item.id }}: {{ s.label }} {{ s.content }} {{ s.reason }} {{ s.nodes.outline.content }}\n{{ end }}\n");

        var body = Body(Check(files), "deliverables/note.md");

        Assert.Equal(["node.sections.items", "s.item.id", "s.label", "s.content", "s.reason", "s.nodes.outline.content"], body.Reads.Select(r => r.Written));
        Assert.All(body.Reads.Skip(1), r => Assert.NotNull(r.Format));
        Assert.True(body.Reads[4].Nullable || body.Reads[4].Format!.ToJsonString().Contains("null", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{{ for.index }}", "{\"type\":\"integer\"}")]
    [InlineData("{{ for.first }}", "\"boolean\"")]
    public void ALoopPosition_IsTyped(string read, string format)
    {
        var files = Package().WithAgent("draft", "{{ for s in node.plan.value.sections }}" + read + "{{ end }}\n");

        var body = Body(Check(files), "nodes/draft.md");

        Assert.Equal(format, body.Reads[1].Format!.ToJsonString());
    }

    [Fact]
    public void ARenderersValue_IsTypedByItsInput()
    {
        var files = Package().WithRenderer("terms", "\"concept_list\"",
            "{{~ for c in value.concepts ~}}\n- {{ c.term }}\n{{~ end ~}}\n");

        var body = Body(Check(files), "renderers/terms.md");

        Assert.Equal(["value.concepts", "c.term"], body.Reads.Select(r => r.Written));
        Assert.Equal("{\"type\":\"string\"}", body.Reads[1].Format!.ToJsonString());
    }

    [Fact]
    public void ARenderersValue_LacksWhatItsInputLacks()
    {
        var files = Package().WithRenderer("terms", "\"concept_list\"", "{{ value.nope }}\n");

        var refusal = TemplateFixture.Single(Check(files));

        Assert.Equal("Renderer 'terms' reads value.nope at line 1, which concept_list doesn't have.", refusal.Sentence);
    }

    [Fact]
    public void AFunctionsResolverRefusal_IsUsedAsGiven()
    {
        var files = ResolveFixture.Functions().With("functions/draft_section/patient_draft.md",
            Agent("Drafting from the patient's facts", Plain, "{{ input.consult_draft.content }}\n"));

        var refusal = TemplateFixture.Single(Check(files));

        Assert.Equal("S04-050", refusal.Id);
    }

    [Fact]
    public void ABodyWithASyntaxFault_IsReportedAndNotTyped()
    {
        var files = Package().WithAgent("draft", "{{ node.nope.value }}\n{{ x = 1 }}\n");

        var check = Check(files);

        var refusal = TemplateFixture.Single(check);
        Assert.Equal("S05-001", refusal.Id);
        Assert.Null(check.Templates);
    }

    [Theory]
    [InlineData("{{ for a in node.plan.value.sections }}{{ for b in a.title }}{{ b.c.d }}{{ end }}{{ end }}")]
    [InlineData("{{ item.parent.parent.value }} {{ pass.previous.node.x.content }} {{ $x }} {{ for.first.x }}")]
    [InlineData("{{ snippet }} {{ profile }} {{ run }} {{ node }} {{ part }} {{ macro }} {{ renderer }} {{ value }}")]
    [InlineData("{{ if }}")]
    [InlineData("{{ if s.state == \"completed\" && s.state != 'x' || !(for.index > \"a\") }}{{ end }}")]
    [InlineData("{{ snippet.nope.content }} {{ snippet.nope.x.y.z }} {{ part.nope }} {{ macro.a.b.c.d }}")]
    [InlineData("{{ node.plan.value.sections.title.title.title }} {{ input.consult_draft.content.x }}")]
    public void AnyBody_IsCheckedOrRefused_NeverThrows(string body)
    {
        foreach (var site in new[] { "prompt", "rendered", "deliverable" })
        {
            var (files, _) = AtSite(site, "top", body + "\n");

            var check = Check(files);

            Assert.True(check.Templates != null || check.Refusals.Count > 0);
            Assert.All(check.Refusals, V31Contract.AssertSentence);
        }
    }

    /// <summary>A package whose body <paramref name="body"/> is at <paramref name="site"/>, placed at <paramref name="place"/>; and that body's file.</summary>
    internal static (Dictionary<string, byte[]> Files, string Path) AtSite(string site, string place, string body)
    {
        if (place == "function")
        {
            var header = site == "rendered"
                ? SourceFixture.Node("rendered", "Drafting from the patient's facts", Plain, true, false, "\"if\": null")
                : null;
            var path = "functions/draft_section/patient_draft.md";
            var functions = ResolveFixture.Functions().With(path,
                header == null ? Agent("Drafting from the patient's facts", Plain, body) : Md(header, body));
            return (functions, path);
        }

        var files = Package()
            .WithSnippet("tone", "Write warmly for Margaret Okafor.\n")
            .WithMacro("follow_up", "Follow up with Dr A. Sample in six weeks.\n")
            .WithCheck();
        if (site == "deliverable")
            return (files.WithDeliverable(body), "deliverables/note.md");

        var file = place switch
        {
            "loop" => "nodes/sections/draft.md",
            "while" => "nodes/refine/draft.md",
            _ => "nodes/draft.md",
        };
        if (place == "loop")
            files.WithLoop("sections", "node.plan.value.sections", "draft", "");
        else if (place == "while")
            files.WithWhile("refine", "draft", "");
        else
            files.WithAgent("draft", "");

        files.With(file, site == "rendered"
            ? Md(SourceFixture.Node("rendered", "Rendered draft", Plain, true, false, "\"if\": null"), body)
            : Agent("Agent draft", Plain, body));
        if (site == "prompt" && body.Contains("part.", StringComparison.Ordinal))
        {
            files.WithJson(file, h => h["parts"] = JsonNode.Parse("{ \"brief\": { \"if\": null, \"text\": [\"Be brief.\"] } }"));
        }

        return (files, file);
    }
}
