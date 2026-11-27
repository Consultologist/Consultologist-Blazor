using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Resolve;

/// <summary>
/// #1502 (specVersion 31, area 05): what the resolver derives over the resolved graph (part 02 §§ 1.1, 1.2; part 03
/// §§ 4.4 to 4.6, 11; part 04 § 1, § 4, § 7.7; part 07 § 2.5): each entry's reads, <c>reproducible</c> and the three
/// contagious flags, held against every header; the seam (<see cref="ITemplateReads"/>) through which area 07 says what
/// a body reads, stood in for by <see cref="StubTemplates"/>; and the rules that need it (D1). Each refusal is one
/// change to a <see cref="ResolveFixture"/> package, and is the only one said.
/// </summary>
public class SourceDerivedTests
{
    private const string Plain = "\"plaintext\"";

    /// <summary>The signature's format (#1543), and so that of what carries it through <see cref="Signed"/>'s <c>sign</c>.</summary>
    private const string Block = "\"markdown_block\"";

    private const string Call = "nodes/standards/section.md";

    private const string Function = "functions/draft_section/index.json";

    /// <summary>
    /// <see cref="SourceCheck.Run"/> with <paramref name="templates"/> as the derived step's seam; with none, a seam that
    /// knows no body, which the derived step reads as having none (#1504, D18: the dialect runs, and is the seam, unless
    /// a caller passes one; these tests of the derived step keep theirs).
    /// </summary>
    private static SourceCheckResult Run(Dictionary<string, byte[]> files, ITemplateReads? templates = null) =>
        SourceCheck.Run(files, ResolveFixture.Published(), templates ?? new StubTemplates(new Dictionary<string, string[]>(), everyBody: false));

    private static PackageDerived Accepted(Dictionary<string, byte[]> files, ITemplateReads? templates = null)
    {
        var result = Run(files, templates);
        Assert.True(result.Refusals.Count == 0, ResolveFixture.Said(result));
        Assert.NotNull(result.Derived);
        return result.Derived!;
    }

    private static SourceRefusal Refused(Dictionary<string, byte[]> files, ITemplateReads? templates = null)
    {
        var result = Run(files, templates);
        Assert.True(result.Refusals.Count == 1, ResolveFixture.Said(result));
        Assert.Null(result.Derived);
        V31Contract.AssertSentence(result.Refusals[0]);
        return result.Refusals[0];
    }

    private static void AssertRefusal(Dictionary<string, byte[]> files, ITemplateReads? templates, string id, string sentence, string path)
    {
        var refusal = Refused(files, templates);
        Assert.Equal((id, sentence, path), (refusal.Id, refusal.Sentence, refusal.Path));
    }

    /// <summary>What every body of <see cref="ResolveFixture.NestedNote"/> reads, as part 07 § 2.9 shows it.</summary>
    private static Dictionary<string, string[]> NestedReads() => new()
    {
        ["node.plan"] = ["input.consult_draft.content"],
        ["node.sections.outline"] = ["item.value.title"],
        ["node.sections.paragraphs.draft_paragraph"] = ["input.consult_draft.content", "item.parent.value.title", "item.value", "item.id"],
        ["node.refine.draft_summary"] = ["pass.previous.node.judge.content"],
        ["node.refine.judge"] = ["node.draft_summary.content"],
        ["deliverable.note"] = ["node.refine.content"],
    };

    private static StubTemplates NestedTemplates() => new(NestedReads(), everyBody: false);

    /// <summary><paramref name="files"/> with <paramref name="ids"/> appended to <c>nodes/index.json</c>.</summary>
    private static Dictionary<string, byte[]> Listing(Dictionary<string, byte[]> files, params string[] ids) =>
        files.WithJson("nodes/index.json", index =>
        {
            foreach (var id in ids)
                index["items"]!.AsArray().Add(id);
        });

    /// <summary>
    /// <see cref="ResolveFixture.NestedNote"/> with the <c>if</c> <c>gate</c>, deciding on <paramref name="condition"/>
    /// whether to measure the draft (its body, the computed <c>length</c>, reads only the input).
    /// </summary>
    private static Dictionary<string, byte[]> WithGate(Dictionary<string, byte[]> files, string condition) =>
        Listing(files, "gate")
            .With("nodes/gate/index.json", Json(SourceFixture.Node("if", "Measuring a planned draft", "\"number\"", true, false,
                $"\"if\": {condition}, \"then\": {{ \"nodes\": [\"length\"], \"ret\": \"node.gate.length\" }}, \"else\": null")))
            .With("nodes/gate/length.md", Md(SourceFixture.Node("computed", "Measuring the draft", "\"number\"", true, false,
                "\"if\": null, \"expression\": { \"size\": [{ \"var\": \"input.consult_draft.content\" }] }"), ""));

    private const string PlanHasText = "{ \"!\": [{ \"blank\": [{ \"var\": \"node.plan.content\" }] }] }";

    // ---- The flags -----------------------------------------------------------------------------

    [Fact]
    public void Flags_SpreadAlongContentAndEarlierPasses_NeverThroughACondition()
    {
        var derived = Accepted(WithGate(ResolveFixture.NestedNote(), PlanHasText), NestedTemplates());
        Assert.True(derived.Complete);

        string[] modelDerived =
        [
            "node.plan", "node.section_count", "node.sections", "node.sections.outline", "node.sections.paragraphs",
            "node.sections.paragraphs.draft_paragraph", "node.refine", "node.refine.draft_summary", "node.refine.judge",
        ];
        foreach (var key in modelDerived)
        {
            var entry = derived.Entries[key];
            Assert.True((entry.ModelDerived, entry.RunDependent, entry.ProfileCarrying, entry.FlagsKnown) == (true, false, false, true), key);
        }

        // Chosen by a model-derived if, reading only the input: neither is model-derived.
        Assert.False(derived.Entries["node.gate"].ModelDerived);
        Assert.False(derived.Entries["node.gate.length"].ModelDerived);
        Assert.True(derived.Entries["node.gate"].FlagsKnown);
        Assert.True(derived.Entries["deliverable.note"].ModelDerived);

        // A judge that reads run.date makes the while's decision run-dependent, and (#1505, R5) the summary that reads
        // its earlier pass and the while that returns the summary.
        var reads = NestedReads();
        reads["node.refine.judge"] = ["node.draft_summary.content", "run.date"];
        var files = ResolveFixture.NestedNote().WithJson("nodes/refine/judge.md", h => h["runDependent"] = true);
        Assert.Equal(["S03-036 nodes/refine/draft_summary.md", "S04-016 nodes/refine/index.json"],
            Run(files, new StubTemplates(reads, everyBody: false)).Refusals.Select(r => $"{r.Id} {r.Path}"));
        files.WithJson("nodes/refine/draft_summary.md", h => h["runDependent"] = true)
            .WithJson("nodes/refine/index.json", h => h["runDependent"] = true);
        derived = Accepted(files, new StubTemplates(reads, everyBody: false));
        Assert.True(derived.Entries["node.refine.judge"].RunDependent);
        Assert.True(derived.Entries["node.refine.draft_summary"].RunDependent);
        Assert.True(derived.Entries["node.refine"].RunDependent);
        Assert.Contains(derived.Warnings, w => w.Id == "S03-037"
            && w.Sentence == "While 'refine' while reads run.date; its decision can differ between runs with the same inputs.");
        Assert.Equal(["run.date"], derived.RunFacts);
    }

    /// <summary>
    /// <see cref="ResolveFixture.Functions"/> with <c>draft_section</c>'s parameter <c>today</c> (a <c>date</c>) and its
    /// computed node <c>stamp</c> reading it; the loop's call passes the input <c>visit_date</c>, and a second call,
    /// <c>dated_section</c>, passes <c>run.date</c>.
    /// </summary>
    private static Dictionary<string, byte[]> Dated() =>
        Listing(ResolveFixture.Functions(), "dated_section")
            .WithJson("manifest.json", m => m["inputs"]!["visit_date"] = JsonNode.Parse(
                "{ \"label\": \"Visit date\", \"description\": null, \"format\": \"date\", \"required\": true, \"channel\": null }"))
            .WithJson(Function, f =>
            {
                f["inputs"]!["today"] = JsonNode.Parse("{ \"label\": \"Today\", \"description\": null, \"format\": \"date\" }");
                f["nodes"]!.AsArray().Add("stamp");
            })
            .With("functions/draft_section/stamp.md", Md(SourceFixture.Node("computed", "Dating the section", "\"date\"", true, false,
                "\"if\": null, \"expression\": { \"var\": \"param.today.value\" }"), ""))
            .WithJson("functions/draft_section/stamp.md", h => h["runDependent"] = true)
            .WithJson(Call, h => h["with"]!["today"] = "input.visit_date.value")
            .With("nodes/dated_section.md", Md(SourceFixture.Node("call", "Drafting the dated section", Plain, false, true,
                "\"if\": null, \"function\": \"draft_section\", \"with\": { \"standard\": \"snippet.standards.hpi\", "
                + "\"draft\": \"input.consult_draft.content\", \"today\": \"run.date\" }"), ""))
            .WithJson("nodes/dated_section.md", h => h["runDependent"] = true);

    [Fact]
    public void ACallsResult_TakesItsArgumentsFlags()
    {
        var derived = Accepted(Dated(), StubTemplates.Empty());
        Assert.True(derived.Complete);

        var dated = derived.Entries["node.dated_section"];
        Assert.True(dated.RunDependent && dated.FlagsKnown);
        var section = derived.Entries["node.standards.section"];
        Assert.True(!section.RunDependent && section.FlagsKnown);
        Assert.True(derived.FunctionEntries["draft_section"]["node.stamp"].RunDependent);
        Assert.Equal(["run.date"], derived.RunFacts);

        // The function's own result, its parameters reading nothing: with the ret reading param.today, the ret is
        // run-dependent across its calls (dated_section passes run.date), but not of itself.
        var reading = Dated().WithJson(Function.Replace("index.json", "apply_instructions.md", StringComparison.Ordinal), h => h["runDependent"] = true);
        var stub = new StubTemplates(new Dictionary<string, string[]> { ["draft_section/node.apply_instructions"] = ["param.today.value"] });
        var withRet = Accepted(reading, stub);
        Assert.True(withRet.FunctionEntries["draft_section"]["node.apply_instructions"].RunDependent);
        var result = withRet.Functions["draft_section"];
        Assert.True(result.ModelDerived && !result.RunDependent && !result.ProfileCarrying && result.FlagsKnown);
        Assert.Empty(result.Reads);

        // Without the seam the call passing run.date is still known to be run-dependent; the other is not judged.
        derived = Accepted(Dated());
        Assert.True(derived.Entries["node.dated_section"].RunDependent);
        Assert.False(derived.Entries["node.standards.section"].FlagsKnown);
        Assert.False(derived.Complete);

        // The call that passes run.date and declares it clean is refused.
        AssertRefusal(Dated().WithJson("nodes/dated_section.md", h => h["runDependent"] = false), null, "S04-016",
            "Call 'dated_section' declares runDependent false; its body makes it true.", "nodes/dated_section.md");
    }

    /// <summary><see cref="ResolveFixture.NestedNote"/> with the loop <c>counts</c> over the plan's sections, of one computed node.</summary>
    private static Dictionary<string, byte[]> WithCounts() =>
        Listing(ResolveFixture.NestedNote(), "counts")
            .With("nodes/counts/index.json", Json(SourceFixture.Node("for", "Counting the titles", ArrayOf("\"number\""), true, true,
                "\"in\": \"node.plan.value.sections\", \"parallel\": false, \"maxParallel\": null, \"until\": null, "
                + "\"max\": null, \"itemLabel\": null, \"nodes\": [\"title_size\"], \"ret\": \"node.counts.title_size\"")))
            .With("nodes/counts/title_size.md", Md(SourceFixture.Node("computed", "Measuring the title", "\"number\"", true, true,
                "\"if\": null, \"expression\": { \"size\": [{ \"var\": \"item.value.title\" }] }"), ""));

    public static TheoryData<string, string, string, string> Headers() => new()
    {
        { "computed", "S03-036", "Node 'section_count' declares modelDerived false, but its reads make it true: node.plan.value.sections.", "nodes/section_count.md" },
        { "loop", "S04-016", "Loop 'sections' declares modelDerived false; its body makes it true.", "nodes/sections/index.json" },
        { "reproducible loop", "S04-015", "Loop 'counts' declares reproducible false; its body makes it true.", "nodes/counts/index.json" },
        { "reproducible call", "S04-015", "Call 'section' declares reproducible true; its body makes it false.", Call },
        { "reproducible inside", "S04-015", "Loop 'counts' declares reproducible true; its body makes it false.", "nodes/counts/index.json" },
    };

    [Theory]
    [MemberData(nameof(Headers))]
    public void HeaderChecks(string change, string id, string sentence, string path)
    {
        var files = change switch
        {
            "computed" => ResolveFixture.NestedNote().WithJson("nodes/section_count.md", h => h["modelDerived"] = false),
            "loop" => ResolveFixture.NestedNote().WithJson("nodes/sections/index.json", h => h["modelDerived"] = false),
            "reproducible loop" => WithCounts().WithJson("nodes/counts/index.json", h => h["reproducible"] = false),
            // part 04 § 1: what the loop's ret is made from inside its body counts, an agent the computed ret reads among it.
            "reproducible inside" => WithCounts()
                .WithJson("nodes/counts/index.json", h => h["nodes"]!.AsArray().Insert(0, "title_note"))
                .With("nodes/counts/title_note.md", Agent("Noting the title", Plain, "Note the title {{ item.value.title }}.\n"))
                .With("nodes/counts/title_size.md", Md(SourceFixture.Node("computed", "Measuring the note", "\"number\"", true, true,
                    "\"if\": null, \"expression\": { \"size\": [{ \"var\": \"node.title_note.content\" }] }"), "")),
            _ => ResolveFixture.Functions().WithJson(Call, h => h["reproducible"] = true),
        };
        AssertRefusal(files, null, id, sentence, path);
    }

    [Fact]
    public void TheValidHeaders_Pass()
    {
        Accepted(WithCounts());
        Accepted(ResolveFixture.Functions());
        Accepted(ResolveFixture.Conditional());
        Assert.True(Accepted(WithCounts()).Entries["node.counts"].Reproducible);
        Assert.False(Accepted(ResolveFixture.Functions()).Entries["node.standards.section"].Reproducible);
        Assert.Null(Accepted(ResolveFixture.Functions()).Entries["deliverable.emr.consult"].Reproducible);
    }

    [Fact]
    public void WithoutTheSeam_AFlagThatNeedsABody_IsNotJudged()
    {
        var files = Minimal().WithJson("nodes/plan.md", h => h["runDependent"] = true);
        var derived = Accepted(files);
        var plan = derived.Entries["node.plan"];
        Assert.False(plan.FlagsKnown);
        Assert.False(plan.BodyKnown);
        Assert.True(plan.RunDependent);
        Assert.False(derived.Complete);

        var reading = new StubTemplates(new Dictionary<string, string[]> { ["node.plan"] = ["input.consult_draft.content"] });
        AssertRefusal(files, reading, "S03-036",
            "Node 'plan' declares runDependent true, but its reads make it false: none.", "nodes/plan.md");

        var clean = Minimal().WithJson("nodes/plan.md", h => h["modelDerived"] = false);
        const string modelDerived = "Node 'plan' declares modelDerived false, but its reads make it true: none.";
        AssertRefusal(clean, null, "S03-036", modelDerived, "nodes/plan.md");
        AssertRefusal(clean, reading, "S03-036", modelDerived, "nodes/plan.md");
    }

    // ---- The profile ---------------------------------------------------------------------------

    private const string DraftHasText = "{ \"!\": [{ \"blank\": [{ \"var\": \"input.consult_draft.content\" }] }] }";

    private const string SignedHasText = "{ \"!\": [{ \"blank\": [{ \"var\": \"node.signed.content\" }] }] }";

    /// <summary>
    /// <see cref="SourceFixture.Minimal"/> with the function <c>sign</c> (its parameter <c>sig</c>, its computed
    /// <c>closing</c>), the call <c>signed</c> passing it <c>profile.signature</c>, and the computed
    /// <c>signed_length</c>, whose <c>if</c> is <paramref name="condition"/>.
    /// </summary>
    internal static Dictionary<string, byte[]> Signed(string condition)
    {
        var files = Listing(Minimal(), "signed", "signed_length")
            .With("nodes/signed.md", Md(SourceFixture.Node("call", "Signing the note", Block, true, false,
                "\"if\": null, \"function\": \"sign\", \"with\": { \"sig\": \"profile.signature\" }"), ""))
            .WithJson("nodes/signed.md", h => h["profileCarrying"] = true)
            .With("nodes/signed_length.md", Md(SourceFixture.Node("computed", "Measuring the draft", "\"number\"", true, false,
                $"\"if\": {condition}, \"expression\": {{ \"size\": [{{ \"var\": \"input.consult_draft.content\" }}] }}"), ""))
            .WithJson("functions/index.json", f => f["items"]!.AsArray().Add("sign"))
            .With("functions/sign/index.json", Json(
                "{ \"label\": \"Sign the note\", \"description\": null, "
                + "\"inputs\": { \"sig\": { \"label\": \"Signature\", \"description\": null, \"format\": \"markdown_block\" } }, "
                + "\"uses\": { \"snippets\": [], \"macros\": [], \"formats\": [], \"renderers\": [] }, "
                + "\"nodes\": [\"closing\"], \"ret\": \"node.closing\", \"format\": \"markdown_block\" }"))
            .With("functions/sign/closing.md", Md(SourceFixture.Node("computed", "The closing", Block, true, false,
                "\"if\": null, \"expression\": { \"var\": \"param.sig.content\" }"), ""))
            .WithJson("functions/sign/closing.md", h => h["profileCarrying"] = true);
        foreach (var folder in new[] { "snippets", "macros", "formats", "renderers" })
            files.With($"functions/sign/{folder}/index.json", SourceFixture.Index([]));
        return files;
    }

    [Fact]
    public void AConditionReadingProfileText_IsS03044()
    {
        var derived = Accepted(Signed(DraftHasText));
        Assert.True(derived.Entries["node.signed"].ProfileCarrying);
        Assert.True(derived.FunctionEntries["sign"]["node.closing"].ProfileCarrying);
        Assert.Equal(["signature"], derived.ProfileFields);

        AssertRefusal(Signed(SignedHasText), null, "S03-044",
            "Node 'signed_length' reads node.signed.content, which carries profile text; prompts, parts and conditions don't read profile text.",
            "nodes/signed_length.md");
    }

    [Fact]
    public void APromptReadingProfileText_IsS03044_ThroughTheSeam()
    {
        var files = Signed(DraftHasText).WithJson("nodes/plan.md", h => h["profileCarrying"] = true);
        Accepted(files);
        AssertRefusal(files, new StubTemplates(new Dictionary<string, string[]> { ["node.plan"] = ["node.signed.content"] }), "S03-044",
            "Node 'plan' reads node.signed.content, which carries profile text; prompts, parts and conditions don't read profile text.",
            "nodes/plan.md");
    }

    /// <summary><see cref="Signed"/> with <c>closing</c> an agent whose prompt reads the parameter.</summary>
    private static Dictionary<string, byte[]> SignedByAnAgent() =>
        Signed(DraftHasText)
            .With("functions/sign/closing.md", Agent("Writing the closing", Block, "Write a closing line.\n\n{{ param.sig.content }}\n"))
            .WithJson("functions/sign/closing.md", h => h["profileCarrying"] = true)
            .WithJson("nodes/signed.md", h => h["reproducible"] = false)
            .WithJson("nodes/signed.md", h => h["modelDerived"] = true);

    [Fact]
    public void APromptReadingAProfileParameter_IsS04069_ThroughTheSeam()
    {
        var files = SignedByAnAgent();
        Accepted(files);
        AssertRefusal(files, new StubTemplates(new Dictionary<string, string[]> { ["sign/node.closing"] = ["param.sig.content"] }), "S04-069",
            "Function 'sign' reads param.sig, which carries profile.signature, in a prompt; the profile is read only in deliverables and macro bodies.",
            "functions/sign/closing.md");
    }

    [Fact]
    public void APartReadingAProfileParameter_IsS04069() =>
        AssertRefusal(SignedByAnAgent().WithJson("functions/sign/closing.md", h => h["parts"] = JsonNode.Parse(
                "{ \"signed\": { \"if\": { \"!\": [{ \"blank\": [{ \"var\": \"param.sig.content\" }] }] }, \"text\": [\"Sign the line.\"] } }")),
            null, "S04-069",
            "Function 'sign' reads param.sig, which carries profile.signature, in a part; the profile is read only in deliverables and macro bodies.",
            "functions/sign/closing.md");

    [Fact]
    public void APlacedProfileMacro_StartsProfileCarrying()
    {
        var files = Minimal();
        var places = new StubTemplates(new Dictionary<string, string[]>(), bodies: new Dictionary<string, TemplateReads>
        {
            ["deliverable.note"] = new(["node.plan.content"], ["macro.signature"], [], [], true),
        });
        var derived = Accepted(files, places);
        Assert.True(derived.Entries["deliverable.note"].ProfileCarrying);
        Assert.False(derived.Entries["node.plan"].ProfileCarrying);
    }

    /// <summary>
    /// <see cref="Signed"/> with the computed <c>signed_size</c> (measuring the signed call, so profile-carrying), the
    /// computed <c>run_label</c> (reading <c>run.id</c>), and the package macro <c>sig_note</c>, whose <c>if</c> is
    /// <paramref name="condition"/>.
    /// </summary>
    private static Dictionary<string, byte[]> WithNoteMacro(string condition) =>
        Listing(Signed(DraftHasText), "signed_size", "run_label")
            .With("nodes/signed_size.md", Md(SourceFixture.Node("computed", "Measuring the signature", "\"number\"", true, false,
                "\"if\": null, \"expression\": { \"size\": [{ \"var\": \"node.signed.content\" }] }"), ""))
            .WithJson("nodes/signed_size.md", h => h["profileCarrying"] = true)
            .With("nodes/run_label.md", Md(SourceFixture.Node("computed", "Naming the run", "\"string\"", true, false,
                "\"if\": null, \"expression\": { \"var\": \"run.id\" }"), ""))
            .WithJson("nodes/run_label.md", h => h["runDependent"] = true)
            .WithJson("macros/index.json", m => m["items"]!.AsArray().Add("sig_note"))
            .With("macros/sig_note.md", Md(
                "{ \"label\": \"Signature note\", \"description\": null, \"if\": " + condition + ", \"format\": \"plaintext\", "
                + "\"instruction\": [\"Place {{ macro.sig_note.content }} at the end.\"] }",
                "Signed on behalf of the clinic.\n"));

    /// <summary>A seam where the plan places <c>sig_note</c> and every other body reads nothing.</summary>
    private static StubTemplates PlacingTheNote() =>
        new(new Dictionary<string, string[]>(), bodies: new Dictionary<string, TemplateReads>
        {
            ["node.plan"] = new(["input.consult_draft.content"], ["macro.sig_note"], [], [], false),
        });

    [Fact]
    public void APlacedMacrosIf_ReadingProfileText_IsS03044()
    {
        var files = WithNoteMacro("{ \">\": [{ \"var\": \"node.signed_size.value\" }, 0] }");
        Accepted(files);
        AssertRefusal(files, PlacingTheNote(), "S03-044",
            "Macro 'sig_note' reads node.signed_size.value, which carries profile text; prompts, parts and conditions don't read profile text.",
            "macros/sig_note.md");
        Accepted(files, StubTemplates.Empty());
    }

    [Fact]
    public void APlacedMacrosIf_ReadingARunDependentValue_IsS03037()
    {
        var files = WithNoteMacro("{ \"!\": [{ \"blank\": [{ \"var\": \"node.run_label.value\" }] }] }");
        const string sentence = "Macro 'sig_note' if reads run.id; its decision can differ between runs with the same inputs.";
        Assert.DoesNotContain(Accepted(files).Warnings, w => w.Sentence == sentence);
        Assert.Contains(Accepted(files, PlacingTheNote()).Warnings, w => (w.Id, w.Sentence, w.Path) == ("S03-037", sentence, "macros/sig_note.md"));
    }

    [Fact]
    public void AnEarlierPass_CountsTowardReproducible()
    {
        // The while returns a computed node that reads only the judge's earlier pass: an agent's output, so the
        // while is not reproducible, and (#1505, R5) model-derived.
        var files = ResolveFixture.NestedNote()
            .WithJson("nodes/refine/index.json", h => h["format"] = "boolean")
            .With("nodes/refine/draft_summary.md", Md(SourceFixture.Node("computed", "Checking the last judgement", "\"boolean\"", true, true,
                "\"if\": null, \"expression\": { \"blank\": [{ \"var\": \"pass.previous.node.judge.content\" }] }"), ""))
            .With("nodes/refine/judge.md", Agent("Judging the summary", "\"verdict\"",
                "Say whether the summary is good or needs another pass.\n\n{{ node.draft_summary.value }}\n"))
            .With("deliverables/note.md", Md(Deliverable("Nested consultation note", "\"markdown_block\"", "null", "hold"),
                "## Summary\n\n{{ node.refine.value }}\n"));
        Assert.False(Accepted(files).Entries["node.refine"].Reproducible);
        AssertRefusal(files.WithJson("nodes/refine/index.json", h => h["reproducible"] = true), null, "S04-015",
            "While 'refine' declares reproducible true; its body makes it false.", "nodes/refine/index.json");
    }

    // ---- An earlier pass passes the flags on (#1505, R5) ---------------------------------------

    private const string Twice = "{ \"<\": [{ \"var\": \"pass.number\" }, 2] }";

    private const string LastDraft = "pass.previous.node.draft.content";

    /// <summary>
    /// <paramref name="files"/> with the while <c>redraft</c> (its <c>while</c> rule <paramref name="condition"/>) of
    /// <c>draft</c>, written <paramref name="draft"/>, and the computed <c>probe</c>, its <c>ret</c>, which reads only
    /// <c>pass.previous.node.draft.content</c>. Each declares every flag false but <paramref name="flag"/>: the while
    /// declares it true, the probe <paramref name="declared"/>; the while declares <c>reproducible</c> <paramref name="reproducible"/>.
    /// </summary>
    private static Dictionary<string, byte[]> Redrafting(Dictionary<string, byte[]> files, string draft, string flag, bool declared,
        bool reproducible, string condition = Twice, string read = LastDraft) =>
        Listing(files, "redraft")
            .With("nodes/redraft/index.json", Json(SourceFixture.Node("while", "Redrafting the note", Yes, reproducible, false,
                $"\"while\": {condition}, \"max\": 3, \"until\": null, \"checkFirst\": false, "
                + "\"nodes\": [\"draft\", \"probe\"], \"ret\": \"node.redraft.probe\"")))
            .WithJson("nodes/redraft/index.json", h => h[flag] = true)
            .With("nodes/redraft/draft.md", draft)
            .With("nodes/redraft/probe.md", Computed("Probing the last draft", Yes, $"{{ \"blank\": [{{ \"var\": \"{read}\" }}] }}", false))
            .WithJson("nodes/redraft/probe.md", h => h[flag] = declared);

    /// <summary>
    /// The draft and what the probe reads of it: an agent (<c>.content</c>), a computed node reading <c>run.id</c>
    /// (<c>.value</c>), or a call of <see cref="Signed"/>'s <c>sign</c> passing <c>profile.signature</c> (<c>.content</c>).
    /// </summary>
    private static (Func<Dictionary<string, byte[]>> Files, string Draft, bool Reproducible, string Read) Drafted(string flag) => flag switch
    {
        "modelDerived" => (Minimal, Agent("Drafting the note", Plain, "Draft the note for Margaret Okafor.\n\n{{ input.consult_draft.content }}\n"),
            false, LastDraft),
        "runDependent" => (Minimal, Computed("Naming the run", Text, "{ \"var\": \"run.id\" }", true), true, "pass.previous.node.draft.value"),
        _ => (() => Signed(DraftHasText), Md(SourceFixture.Node("call", "Signing the draft", Block, true, false,
            "\"if\": null, \"function\": \"sign\", \"with\": { \"sig\": \"profile.signature\" }").Replace(
            "\"profileCarrying\": false", "\"profileCarrying\": true", StringComparison.Ordinal), ""), true, LastDraft),
    };

    /// <summary>
    /// The probe reading only the draft's earlier pass, declaring <paramref name="flag"/> false: S03-036, naming the read;
    /// declaring it true: published, the probe and the while carrying it and no other flag.
    /// </summary>
    private static void AnEarlierPassRead_Carries(string flag)
    {
        var (files, draft, reproducible, read) = Drafted(flag);
        AssertRefusal(Redrafting(files(), draft, flag, false, reproducible, read: read), null, "S03-036",
            $"Node 'probe' declares {flag} false, but its reads make it true: {read}.", "nodes/redraft/probe.md");

        var derived = Accepted(Redrafting(files(), draft, flag, true, reproducible, read: read));
        var flags = new Dictionary<string, Func<DerivedEntry, bool>>
        {
            ["modelDerived"] = e => e.ModelDerived,
            ["runDependent"] = e => e.RunDependent,
            ["profileCarrying"] = e => e.ProfileCarrying,
        };
        foreach (var (name, of) in flags)
        {
            Assert.Equal(name == flag, of(derived.Entries["node.redraft.probe"]));
            Assert.Equal(name == flag, of(derived.Entries["node.redraft"]));
        }
    }

    [Fact]
    public void AnEarlierPassRead_OfAnAgent_IsModelDerived() => AnEarlierPassRead_Carries("modelDerived");

    [Fact]
    public void AnEarlierPassRead_OfARunFact_IsRunDependent()
    {
        AnEarlierPassRead_Carries("runDependent");

        // The probe is a computed node: its run-dependence is warned of, naming the earlier pass's read.
        var (files, draft, reproducible, read) = Drafted("runDependent");
        Assert.Contains(Accepted(Redrafting(files(), draft, "runDependent", true, reproducible, read: read)).Warnings,
            w => (w.Id, w.Path) == ("S03-038", "nodes/redraft/probe.md") && w.Sentence.Contains(read, StringComparison.Ordinal));
    }

    [Fact]
    public void AnEarlierPassRead_OfProfileText_IsProfileCarrying() => AnEarlierPassRead_Carries("profileCarrying");

    [Fact]
    public void ACondition_ReadingProfileTextOfAnEarlierPass_IsRefused()
    {
        var (files, draft, reproducible, _) = Drafted("profileCarrying");
        AssertRefusal(Redrafting(files(), draft, "profileCarrying", true, reproducible,
                $"{{ \"!\": [{{ \"blank\": [{{ \"var\": \"{LastDraft}\" }}] }}] }}"), null, "S03-044",
            $"While 'redraft' reads {LastDraft}, which carries profile text; prompts, parts and conditions don't read profile text.",
            "nodes/redraft/index.json");
    }

    [Fact]
    public void APrompt_ReadingProfileTextOfAnEarlierPass_IsRefused()
    {
        // The probe is an agent whose prompt reads the signed draft of the pass before (through the seam): profile text
        // sent to a model.
        var (files, draft, _, _) = Drafted("profileCarrying");
        var prompting = Redrafting(files(), draft, "profileCarrying", true, false)
            .WithJson("nodes/redraft/index.json", h => h["format"] = "plaintext")
            .WithJson("nodes/redraft/index.json", h => h["modelDerived"] = true)
            .With("nodes/redraft/probe.md", Agent("Probing the last draft", Plain,
                $"Say in one line whether the last draft reads well.\n\n{{{{ {LastDraft} }}}}\n"))
            .WithJson("nodes/redraft/probe.md", h => h["profileCarrying"] = true);
        AssertRefusal(prompting, new StubTemplates(new Dictionary<string, string[]> { ["node.redraft.probe"] = [LastDraft] }), "S03-044",
            $"Node 'probe' reads {LastDraft}, which carries profile text; prompts, parts and conditions don't read profile text.",
            "nodes/redraft/probe.md");
    }

    [Fact]
    public void EarlierPasses_StayOutOfTheOrderAndTheCircles()
    {
        // The summary and its judge each read the other's earlier pass: no circle, and the order is the golden's.
        var golden = CompiledGoldenTests.Cases().Single(c => c.Name == "nested-note");
        var reading = golden with
        {
            Source = () => golden.Source()
                .With("nodes/refine/draft_summary.md", Agent("Drafting the summary", Plain,
                    "Summarise the note in two sentences for Dr A. Sample.\n\n{{ pass.previous.node.judge.content }}\n"))
                .With("nodes/refine/judge.md", Agent("Judging the summary", "\"verdict\"",
                    "Say whether the summary is good or needs another pass.\n\n{{ pass.previous.node.draft_summary.content }}\n")),
        };
        var compiled = CompiledGoldenTests.Published(reading);
        var order = JsonNode.Parse(compiled.Resolve)!["graph"]!["order"]!.ToJsonString();
        var expected = JsonNode.Parse(File.ReadAllText(Path.Combine(CompiledGoldenTests.GoldenFolder(), "nested-note.resolve.json")))!
            ["graph"]!["order"]!.ToJsonString();
        Assert.Equal(expected, order);
        Assert.Contains("pass.previous.node.judge.content", JsonNode.Parse(compiled.Resolve)!["nodes"]!["node.refine.draft_summary"]!["reads"]!.ToJsonString(),
            StringComparison.Ordinal);
    }

    // ---- A condition passes no flag on (part 07 § 2.5) -----------------------------------------

    [Fact]
    public void ANodeChosenByAModelDerivedCase_IsNotModelDerived()
    {
        // The loop kinds goes over the plan's sections (an agent's); its case routes each by its kind, and the narrative
        // branch measures the draft. The case's subject is model-derived; what it chooses is not, for that.
        var files = Listing(ResolveFixture.NestedNote(), "kinds")
            .With("nodes/kinds/index.json", Json(SourceFixture.Node("for", "Measuring each narrative section", ArrayOf("\"number\""), true, true,
                "\"in\": \"node.plan.value.sections\", \"parallel\": false, \"maxParallel\": null, \"until\": null, "
                + "\"max\": null, \"itemLabel\": null, \"nodes\": [\"route\"], \"ret\": \"node.kinds.route\"")))
            .With("nodes/kinds/route/index.json", Json(SourceFixture.Node("case", "Routing a section by its kind", "\"number\"", true, false,
                "\"case\": \"item.value.kind\", \"branches\": ["
                + "{ \"when\": [\"narrative\"], \"nodes\": [\"measure\"], \"ret\": \"node.kinds.route.measure\" }, "
                + "{ \"when\": [\"table\"], \"nodes\": [], \"ret\": null }, "
                + "{ \"when\": [\"omit\"], \"nodes\": [], \"ret\": null }], \"else\": null")))
            .With("nodes/kinds/route/measure.md", Md(SourceFixture.Node("computed", "Measuring the draft", "\"number\"", true, false,
                "\"if\": null, \"expression\": { \"size\": [{ \"var\": \"input.consult_draft.content\" }] }"), ""));
        var derived = Accepted(files);
        Assert.False(derived.Entries["node.kinds.route"].ModelDerived);
        Assert.False(derived.Entries["node.kinds.route.measure"].ModelDerived);
        Assert.True(derived.Entries["node.kinds.route"].FlagsKnown);

        // The loop iterates the plan's sections, its content: it is model-derived through its source, not its case.
        Assert.True(derived.Entries["node.kinds"].ModelDerived);
    }

    [Fact]
    public void AnItemOfALoopWhoseUntilIsRunDependent_IsNotRunDependent()
    {
        // The loop echoes goes over the input points until the run's label says so; each item is the point, and the
        // decision when to stop is run-dependent (S03-037), but neither the item read nor the loop is, for that.
        var files = Listing(Minimal(), "run_label", "echoes")
            .WithJson("manifest.json", m => m["inputs"]!["points"] = JsonNode.Parse(
                "{ \"label\": \"Points\", \"description\": null, \"format\": " + ArrayOf(Plain) + ", \"required\": true, \"channel\": null }"))
            .With("nodes/run_label.md", Computed("Naming the run", Text, "{ \"var\": \"run.id\" }", true))
            .With("nodes/echoes/index.json", Json(SourceFixture.Node("for", "Echoing each point", ArrayOf(Plain), true, false,
                $"\"in\": \"input.points.value\", \"parallel\": false, \"maxParallel\": null, \"until\": {Is("node.run_label.value", "stop")}, "
                + "\"max\": null, \"itemLabel\": null, \"nodes\": [\"echo\"], \"ret\": \"node.echoes.echo\"")))
            .With("nodes/echoes/echo.md", Computed("Echoing the point", Plain, "{ \"var\": \"item.value\" }", false));
        var derived = Accepted(files);
        Assert.False(derived.Entries["node.echoes.echo"].RunDependent);
        Assert.False(derived.Entries["node.echoes"].RunDependent);
        Assert.Contains(derived.Warnings, w => (w.Id, w.Sentence) == ("S03-037",
            "Loop 'echoes' until reads run.id; its decision can differ between runs with the same inputs."));
    }

    // ---- The facts of a run (D33) --------------------------------------------------------------

    private const string Text = "\"string\"";

    private const string Yes = "\"boolean\"";

    /// <summary>Whether <paramref name="path"/> is <paramref name="value"/>, as JSON Logic.</summary>
    private static string Is(string path, string value) => $"{{ \"===\": [{{ \"var\": \"{path}\" }}, \"{value}\"] }}";

    /// <summary>A computed node over <paramref name="expression"/>, of <paramref name="format"/>, declaring <paramref name="runDependent"/>.</summary>
    private static string Computed(string label, string format, string expression, bool runDependent) =>
        Md(SourceFixture.Node("computed", label, format, true, false,
            $"\"if\": null, \"expression\": {expression}").Replace("\"runDependent\": false", $"\"runDependent\": {(runDependent ? "true" : "false")}",
            StringComparison.Ordinal), "");

    /// <summary>
    /// <see cref="SourceFixture.Minimal"/> with the while <c>stamping</c> (its <c>while</c> rule <paramref name="condition"/>)
    /// of <c>stamp</c>, which reads <c>run.id</c>, and <c>probe</c>, whose expression is <paramref name="probe"/>.
    /// </summary>
    private static Dictionary<string, byte[]> Stamping(string condition, string probe) =>
        Listing(Minimal(), "stamping")
            .With("nodes/stamping/index.json", Json(SourceFixture.Node("while", "Stamping the run", Yes, true, false,
                $"\"while\": {condition}, \"max\": 3, \"until\": null, \"checkFirst\": false, "
                + "\"nodes\": [\"stamp\", \"probe\"], \"ret\": \"node.stamping.probe\"")))
            .With("nodes/stamping/stamp.md", Computed("Naming the run", Text, "{ \"var\": \"run.id\" }", true))
            .With("nodes/stamping/probe.md", Computed("Probing the stamp", Yes, probe, false));

    [Fact]
    public void ANodesState_ReadThroughPass_CarriesNoFlag()
    {
        // pass.node.stamp.state, as node.stamp.state, is the engine's fact about the run: no flag passes through it.
        var pass = Is("pass.node.stamp.state", "completed");
        var derived = Accepted(Stamping("{ \"<\": [{ \"var\": \"pass.number\" }, 2] }", pass));
        Assert.False(derived.Entries["node.stamping.probe"].RunDependent);
        Assert.False(derived.Entries["node.stamping"].RunDependent);

        // The while deciding on the stamp's state, this pass's or the last, is no run-dependent decision; on its value it is.
        foreach (var read in new[] { "pass.node.stamp.state", "pass.previous.node.stamp.state" })
        {
            Assert.DoesNotContain(Accepted(Stamping(Is(read, "skipped"), pass)).Warnings, w => w.Id == "S03-037");
        }

        Assert.Contains(Accepted(Stamping(Is("pass.node.stamp.value", "done"), pass)).Warnings,
            w => w.Id == "S03-037" && w.Sentence.StartsWith("While 'stamping' while reads run.id", StringComparison.Ordinal));
    }

    [Fact]
    public void ANodesState_ReadThroughItemNodes_CarriesNoFlag()
    {
        // The loop stamps reads run.id in each item; the loop states, over its .items, reads each stamp's state.
        var files = Listing(Minimal(), "stamps", "states")
            .WithJson("manifest.json", m => m["inputs"]!["points"] = JsonNode.Parse(
                "{ \"label\": \"Points\", \"description\": null, \"format\": " + ArrayOf(Plain) + ", \"required\": true, \"channel\": null }"))
            .With("nodes/stamps/index.json", Json(SourceFixture.Node("for", "Stamping each point", ArrayOf(Text), true, false,
                "\"in\": \"input.points.value\", \"parallel\": false, \"maxParallel\": null, \"until\": null, "
                + "\"max\": null, \"itemLabel\": null, \"nodes\": [\"stamp\"], \"ret\": \"node.stamps.stamp\"")))
            .WithJson("nodes/stamps/index.json", h => h["runDependent"] = true)
            .With("nodes/stamps/stamp.md", Computed("Naming the run", Text, "{ \"var\": \"run.id\" }", true))
            .With("nodes/states/index.json", Json(SourceFixture.Node("for", "Reading each stamp's state", ArrayOf(Yes), true, false,
                "\"in\": \"node.stamps.items\", \"parallel\": false, \"maxParallel\": null, \"until\": null, "
                + "\"max\": null, \"itemLabel\": null, \"nodes\": [\"state\"], \"ret\": \"node.states.state\"")))
            .WithJson("nodes/states/index.json", h => h["runDependent"] = true)
            .With("nodes/states/state.md", Computed("The stamp's state", Yes, Is("item.nodes.stamp.state", "completed"), false));
        var derived = Accepted(files);
        Assert.False(derived.Entries["node.states.state"].RunDependent);

        // The loop itself reads the stamps' .items, which hold their values: it is run-dependent through its source.
        Assert.True(derived.Entries["node.states"].RunDependent);

        // The stamp's value, read the same way, carries the flag.
        AssertRefusal(files.With("nodes/states/state.md", Computed("The stamp's value", Yes, Is("item.nodes.stamp.value", "done"), false)),
            null, "S03-036", "Node 'state' declares runDependent false, but its reads make it true: item.nodes.stamp.value.", "nodes/states/state.md");
    }

    [Fact]
    public void AVertexReadAfterTheFlagsWereDerived_HasItsOwnStartsOnly_NeverThrows()
    {
        // A read mapped after the derivation adds the profile field it names (Map's side effect); its flags are what it
        // starts with, as a vertex that reads nothing.
        var check = Run(Minimal());
        var resolution = check.Resolution!;
        var graph = DerivedGraph.Build(check.Parse.Package!, resolution, null);
        var derived = new Derivation(graph, resolution, check.Parse.Package!);
        var plan = graph.ById["node.plan"];

        var read = graph.Map("profile.closing", resolution.Tree.PackageRoot, plan, null);
        var field = graph.ById[read.Id!];
        Assert.True(derived.Flag(field, LineageFlag.ProfileCarrying));
        Assert.False(derived.Flag(field, LineageFlag.ModelDerived));
        Assert.False(derived.Value(field, LineageFlag.RunDependent).Value);
    }

    // ---- Circles -------------------------------------------------------------------------------

    /// <summary>The computed node <paramref name="id"/>, a number, one more than <paramref name="reads"/>' value.</summary>
    private static string Counter(string reads) =>
        Md(SourceFixture.Node("computed", "Counting", "\"number\"", true, false,
            $"\"if\": null, \"expression\": {{ \"+\": [{{ \"var\": \"node.{reads}.value\" }}, 1] }}"), "");

    public static TheoryData<string, string> Circles() => new()
    {
        { "two", "Node 'first' reads node 'second', which reads node 'first': nodes can't read each other in a circle. Break the circle." },
        { "self", "Node 'first' reads node 'first', which reads node 'first': nodes can't read each other in a circle. Break the circle." },
        { "three", "Node 'first' reads node 'second', which reads node 'third', which reads node 'first': nodes can't read each other in a circle. Break the circle." },
    };

    [Theory]
    [MemberData(nameof(Circles))]
    public void NodesReadingEachOther_AreRefused(string circle, string sentence)
    {
        var files = circle switch
        {
            "two" => Listing(Minimal(), "first", "second").With("nodes/first.md", Counter("second")).With("nodes/second.md", Counter("first")),
            "self" => Listing(Minimal(), "first").With("nodes/first.md", Counter("first")),
            _ => Listing(Minimal(), "first", "second", "third")
                .With("nodes/third.md", Counter("first")).With("nodes/first.md", Counter("second")).With("nodes/second.md", Counter("third")),
        };
        AssertRefusal(files, null, "A05-003", sentence, "nodes/first.md");
    }

    [Fact]
    public void ALoopIteratingItsOwnItems_IsACircle() =>
        AssertRefusal(WithCounts()
                .With("nodes/counts/title_size.md", Md(SourceFixture.Node("computed", "Measuring the draft", "\"number\"", true, false,
                    "\"if\": null, \"expression\": { \"size\": [{ \"var\": \"input.consult_draft.content\" }] }"), ""))
                .WithJson("nodes/counts/index.json", h => h["in"] = "node.counts.items"), null, "A05-003",
            "Node 'counts' reads node 'counts', which reads node 'counts': nodes can't read each other in a circle. Break the circle.",
            "nodes/counts/index.json");

    [Fact]
    public void AChainOfReads_IsNoCircle() =>
        Accepted(Listing(Minimal(), "first", "second", "third")
            .With("nodes/first.md", Counter("second")).With("nodes/second.md", Counter("third"))
            .With("nodes/third.md", Md(SourceFixture.Node("computed", "Counting", "\"number\"", true, false,
                "\"if\": null, \"expression\": { \"size\": [{ \"var\": \"input.consult_draft.content\" }] }"), "")));

    [Fact]
    public void AWhileReadingItsOwnPass_IsNoCircle()
    {
        // The summary reads the judge's earlier pass and the judge this pass's summary; the while reads the judge.
        Accepted(ResolveFixture.NestedNote(), NestedTemplates());

        var reads = NestedReads();
        reads["node.refine.judge"] = ["pass.node.draft_summary.content"];
        reads["node.refine.draft_summary"] = ["pass.previous.node.judge.content", "pass.number", "pass.history"];
        Accepted(ResolveFixture.NestedNote(), new StubTemplates(reads, everyBody: false));
    }

    // ---- Checks reading a deliverable (#1505, area 08; D1 to D3) -------------------------------

    private const string Consult = "deliverables/emr/consult.md";

    /// <summary><see cref="SourceCheck.Run"/> with the dialect as the seam (every body known): one refusal, asserted as written.</summary>
    private static void AssertDialectRefusal(Dictionary<string, byte[]> files, string id, string sentence, string path)
    {
        var result = ResolveFixture.Run(files);
        Assert.True(result.Refusals.Count == 1, ResolveFixture.Said(result));
        V31Contract.AssertSentence(result.Refusals[0]);
        Assert.Equal((id, sentence, path), (result.Refusals[0].Id, result.Refusals[0].Sentence, result.Refusals[0].Path));
    }

    /// <summary><see cref="SourceCheck.Run"/> with the dialect as the seam, asserted accepted.</summary>
    private static PackageDerived DialectAccepted(Dictionary<string, byte[]> files)
    {
        var result = ResolveFixture.Run(files);
        Assert.True(result.Refusals.Count == 0, ResolveFixture.Said(result));
        return result.Derived!;
    }

    /// <summary><paramref name="files"/> with <paramref name="id"/> listed last in <c>checks/</c>, its file <paramref name="file"/>.</summary>
    private static Dictionary<string, byte[]> WithCheck(Dictionary<string, byte[]> files, string id, string file) =>
        files.WithJson("checks/index.json", index => index["items"]!.AsArray().Add(id)).With($"checks/{id}.md", file);

    [Fact]
    public void ACheck_ReadingADeliverable_TakesItsFlags()
    {
        var derived = DialectAccepted(ResolveFixture.Proofread());

        var proofread = derived.Entries["check.proofread"];
        Assert.Contains("deliverable.emr.consult.content", proofread.Reads);
        Assert.Contains(proofread.ContentReads, r => (r.Path, r.Key) == ("deliverable.emr.consult.content", "deliverable.emr.consult"));
        Assert.True(proofread.ModelDerived);
        Assert.True(derived.Entries["deliverable.emr.consult"].ModelDerived);
        Assert.True(proofread.Reproducible);
        Assert.True(derived.Entries["check.consult_words"].ModelDerived);
    }

    [Fact]
    public void ACheck_OfAModelDerivedDeliverable_IsModelDerived() =>
        AssertDialectRefusal(ResolveFixture.Proofread().WithJson("checks/consult_words.md", h => h["modelDerived"] = false),
            "S03-036", "Node 'consult_words' declares modelDerived false, but its reads make it true: deliverable.emr.consult.content.",
            "checks/consult_words.md");

    public static TheoryData<string, string, string> CarriedFlags() => new()
    {
        { "runDependent", "{{ run.date }}", "Node 'consult_words' declares runDependent false, but its reads make it true: deliverable.emr.consult.content." },
        { "profileCarrying", "{{ profile.signature }}", "Node 'consult_words' declares profileCarrying false, but its reads make it true: deliverable.emr.consult.content." },
    };

    [Theory]
    [MemberData(nameof(CarriedFlags))]
    public void ACheck_OfADeliverable_TakesItsOtherFlags(string flag, string read, string sentence) =>
        // The note reads a run fact (or a profile field), so the gate proofread, declared so, and consult_words are too.
        AssertDialectRefusal(ResolveFixture.Proofread()
                .With(Consult, Md(Deliverable("Consult note", "\"markdown_block\"", "\"check.proofread\"", "hold"),
                    "## Sections\n\n{{ node.standards.content }}\n\n" + read + "\n"))
                .WithJson("checks/proofread.md", h => h[flag] = true),
            "S03-036", sentence, "checks/consult_words.md");

    [Fact]
    public void AGate_ReadingItsOwnDeliverablesState_IsRefused() =>
        AssertDialectRefusal(ResolveFixture.Proofread().WithJson("checks/proofread.md", h => h["expression"]!["object"]!["pass"] =
                JsonNode.Parse("{ \"===\": [{ \"var\": \"deliverable.emr.consult.state\" }, \"released\"] }")),
            "A08-003", "Check 'proofread' reads deliverable.emr.consult.state, and deliverable 'emr.consult' waits for check "
            + "'proofread' through its check 'proofread': read its .content, or read the state in a check it doesn't wait for.",
            "checks/proofread.md");

    [Fact]
    public void ACheckTheGateReachesThroughANode_IsOutsideTheGateSet_AndACircle() =>
        // D3 (decision 2): the gate set follows checks only. consult_words, read by the node noted that the gate reads,
        // waits for the note's outcome, which waits for the gate.
        AssertDialectRefusal(ResolveFixture.GateThroughANode(),
            "A05-003", "Node 'consult_words' reads node 'emr.consult', which reads node 'proofread', which reads node 'noted', "
            + "which reads node 'consult_words': nodes can't read each other in a circle. Break the circle.",
            "checks/consult_words.md");

    [Fact]
    public void AnIfAndACase_InChecks_MayReadADeliverable()
    {
        var derived = DialectAccepted(ResolveFixture.FlowReadingState());
        Assert.Contains(derived.Entries["check.when_released"].ConditionReads, r => r.Key == "deliverable.emr.consult");
        Assert.Contains(derived.Entries["check.by_state"].ConditionReads, r => r.Key == "deliverable.emr.consult");
    }

    [Fact]
    public void AComputedNode_ReadingADeliverable_IsStillRefused() =>
        AssertDialectRefusal(Listing(ResolveFixture.Functions(), "note_length")
                .With("nodes/note_length.md", Md(SourceFixture.Node("computed", "Measuring the note", "\"number\"", true, true,
                    "\"if\": null, \"expression\": { \"words\": [{ \"var\": \"deliverable.emr.consult.content\" }] }"), "")),
            "S03-019", "Computed node 'note_length' reads deliverable.emr.consult.content; JSON Logic reads input, node, check, item, pass, param and run.",
            "nodes/note_length.md");

    [Fact]
    public void ADeliverablesIf_ReadingADeliverable_IsStillRefused() =>
        AssertDialectRefusal(ResolveFixture.Proofread()
                .WithJson(Consult, h => h["if"] = JsonNode.Parse("{ \"===\": [{ \"var\": \"deliverable.emr.consult.state\" }, \"released\"] }")),
            "S03-019", "Deliverable 'consult' reads deliverable.emr.consult.state; JSON Logic reads input, node, check, item, pass, param and run.",
            Consult);

    [Fact]
    public void ACheck_ReadingAMemberADeliverableLacks_IsRefused() =>
        AssertDialectRefusal(WithCheck(ResolveFixture.Functions(), "listed",
                ResolveFixture.ObjectCheck("Listing the note", "{ \">\": [{ \"size\": [{ \"var\": \"deliverable.emr.consult.items\" }] }, 0] }")),
            "S03-014", "Check 'listed' reads deliverable.emr.consult.items, which doesn't exist here.", "checks/listed.md");

    [Fact]
    public void AGate_ReadingItsDeliverablesState_IsRefused() =>
        AssertDialectRefusal(ResolveFixture.Proofread().WithJson("checks/consult_words.md", h => h["expression"]!["object"]!["pass"] =
                JsonNode.Parse("{ \"===\": [{ \"var\": \"deliverable.emr.consult.state\" }, \"released\"] }")),
            "A08-003", "Check 'consult_words' reads deliverable.emr.consult.state, and deliverable 'emr.consult' waits for check "
            + "'consult_words' through its check 'proofread': read its .content, or read the state in a check it doesn't wait for.",
            "checks/consult_words.md");

    [Fact]
    public void ACheckOutsideTheGateSet_MayReadState()
    {
        // delivered reads the note's outcome; no gate waits for it, so it waits for the note's gate (D3).
        var derived = DialectAccepted(WithCheck(ResolveFixture.Proofread(), "delivered", ResolveFixture.ObjectCheck("The note went out",
            "{ \"===\": [{ \"var\": \"deliverable.emr.consult.state\" }, \"released\"] }", modelDerived: false)));
        Assert.False(derived.Entries["check.delivered"].ModelDerived);
        Assert.Equal(["deliverable.emr.consult.state"], derived.Entries["check.delivered"].Reads);
    }

    [Fact]
    public void ACheckOutsideTheGateSet_ReadingAGateOfItsDeliverable_IsNoCircle() =>
        DialectAccepted(WithCheck(ResolveFixture.Proofread(), "delivered", ResolveFixture.ObjectCheck("The note went out",
            $"{{ \"and\": [{ResolveFixture.NotBlank("deliverable.emr.consult.content")}, {{ \"var\": \"check.proofread.value.pass\" }}] }}")));

    [Fact]
    public void TwoGates_ReadingEachOthersDeliverable_AreACircle() =>
        AssertDialectRefusal(ResolveFixture.TwoGates("deliverable.emr.summary.content", "deliverable.emr.consult.content"),
            "A05-003", "Node 'gate_a' reads node 'emr.summary', which reads node 'gate_b', which reads node 'emr.consult', which reads "
            + "node 'gate_a': nodes can't read each other in a circle. Break the circle.",
            "checks/gate_a.md");

    [Fact]
    public void AGate_ReadingAnotherDeliverable_Publishes()
    {
        var derived = DialectAccepted(ResolveFixture.TwoGates("deliverable.emr.summary.content", "deliverable.emr.summary.content"));
        Assert.Contains("deliverable.emr.summary.content", derived.Entries["check.gate_a"].Reads);
    }

    [Fact]
    public void ACheckLoopsBody_ReadingItsGatesDeliverable_IsNoCircle() =>
        // The check loop is in the gate set of the consult note (all_sections, its gate, reads it): its body reads the
        // composed note, an ordinary edge.
        DialectAccepted(ResolveFixture.Functions().WithJson("checks/section_coverage/coverage.md", h => h["if"] = JsonNode.Parse(
            "{ \"and\": [{ \"===\": [{ \"var\": \"item.state\" }, \"completed\"] }, "
            + ResolveFixture.NotBlank("deliverable.emr.consult.content") + "] }")));

    // ---- Warnings ------------------------------------------------------------------------------

    [Fact]
    public void Warnings_NameEveryRunDependentDecision()
    {
        var files = WithGate(Listing(ResolveFixture.NestedNote(), "run_label"),
                "{ \"!\": [{ \"blank\": [{ \"var\": \"node.run_label.value\" }] }] }")
            .With("nodes/run_label.md", Md(SourceFixture.Node("computed", "Naming the run", "\"string\"", true, false,
                "\"if\": null, \"expression\": { \"var\": \"run.id\" }"), ""))
            .WithJson("nodes/run_label.md", h => h["runDependent"] = true);
        var derived = Accepted(files, NestedTemplates());

        Assert.Contains(derived.Warnings, w => (w.Id, w.Sentence, w.Path) == ("S03-037",
            "If 'gate' if reads run.id; its decision can differ between runs with the same inputs.", "nodes/gate/index.json"));
        Assert.Contains(derived.Warnings, w => (w.Id, w.Sentence, w.Path) == ("S03-038",
            "Computed node 'run_label' reads run.id, which is run-dependent; its value can differ between runs with the same inputs.",
            "nodes/run_label.md"));
        Assert.Equal(2, derived.Warnings.Count);
        Assert.All(derived.Warnings, V31Contract.AssertSentence);
        Assert.False(derived.Entries["node.gate"].RunDependent);
        Assert.Equal(["run.id"], derived.RunFacts);
    }

    // ---- Reads ---------------------------------------------------------------------------------

    [Fact]
    public void Reads_AreCanonical()
    {
        var derived = Accepted(ResolveFixture.NestedNote(), NestedTemplates());
        Assert.Equal(["input.consult_draft.content", "item.id", "item.parent.value.title", "item.value"],
            derived.Entries["node.sections.paragraphs.draft_paragraph"].Reads);
        Assert.Equal(["node.plan.value"], derived.Entries["node.section_count"].Reads);
        Assert.Equal(["node.plan.value"], derived.Entries["node.sections"].Reads);
        Assert.Equal(["node.sections.outline.value"], derived.Entries["node.sections.paragraphs"].Reads);
        Assert.Equal(["node.refine.judge.value"], derived.Entries["node.refine"].Reads);
        Assert.Equal(["node.refine.content"], derived.Entries["deliverable.note"].Reads);
        Assert.True(derived.Entries["node.sections.paragraphs.draft_paragraph"].BodyKnown);

        // Without the seam an agent's reads are its header's.
        derived = Accepted(ResolveFixture.NestedNote());
        Assert.Equal(["item.parent.value.title"], derived.Entries["node.sections.paragraphs.draft_paragraph"].Reads);
        Assert.False(derived.Entries["node.sections.paragraphs.draft_paragraph"].BodyKnown);
    }

    [Fact]
    public void Entries_HoldDefinitionsAndFunctions()
    {
        var derived = Accepted(ResolveFixture.Functions(), StubTemplates.Empty());
        Assert.Contains("snippet.standards.hpi", derived.Entries.Keys);
        Assert.Contains("check.section_coverage", derived.Entries.Keys);
        Assert.Equal(["node.apply_instructions", "node.patient_draft", "node.standard_draft"],
            derived.FunctionEntries["draft_section"].Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["input.consult_draft.content", "item"], derived.Entries["node.standards.section"].Reads);
    }

    // ---- The seam's rules ----------------------------------------------------------------------

    /// <summary><see cref="ResolveFixture.NestedNote"/> with a third agent in the while, <c>notes</c>.</summary>
    private static Dictionary<string, byte[]> WithNotes() =>
        ResolveFixture.NestedNote()
            .WithJson("nodes/refine/index.json", h => h["nodes"]!.AsArray().Add("notes"))
            .With("nodes/refine/notes.md", Agent("Noting the changes", Plain, "Note what changed in the summary.\n"));

    [Fact]
    public void AnUnreadWhileNode_IsS04035_OnceTheBodiesAreKnown()
    {
        Accepted(WithNotes());
        Accepted(WithNotes(), new StubTemplates(NestedReads(), everyBody: false));

        AssertRefusal(WithNotes(), new StubTemplates(NestedReads()), "S04-035",
            "Node 'notes' in while 'refine' is neither returned nor read; name it with ret, read it from a body node or from the check.",
            "nodes/refine/notes.md");

        var reads = NestedReads();
        reads["node.refine.judge"] = ["node.draft_summary.content", "node.notes.content"];
        Accepted(WithNotes(), new StubTemplates(reads));
    }

    /// <summary><see cref="ResolveFixture.Functions"/> with the package snippet <c>tone</c>, listed in <c>draft_section</c>'s uses when <paramref name="listed"/>.</summary>
    private static Dictionary<string, byte[]> WithTone(bool listed)
    {
        var files = ResolveFixture.Functions()
            .WithJson("snippets/index.json", s => s["items"]!.AsArray().Add("tone"))
            .With("snippets/tone.md", Md("{ \"label\": \"Tone\", \"description\": null, \"if\": null, \"format\": \"plaintext\" }",
                "Write warmly and plainly.\n"));
        return listed ? files.WithJson(Function, f => f["uses"]!["snippets"]!.AsArray().Add("tone")) : files;
    }

    private static StubTemplates ReadingTone() =>
        new(new Dictionary<string, string[]> { ["draft_section/node.standard_draft"] = ["param.standard.value.title", "snippet.tone.content"] });

    [Fact]
    public void ASnippetABodyUses_IsListed_S04052()
    {
        Accepted(WithTone(listed: false));
        AssertRefusal(WithTone(listed: false), ReadingTone(), "S04-052",
            "Function 'draft_section' uses snippet 'tone' but doesn't list it in uses.", "functions/draft_section/standard_draft.md");
        Assert.Empty(Accepted(WithTone(listed: true), ReadingTone()).Warnings);
    }

    [Fact]
    public void AListedSnippetNoBodyUses_IsS04074_OnceTheBodiesAreKnown()
    {
        Assert.DoesNotContain(Accepted(WithTone(listed: true)).Warnings, w => w.Id == "S04-074");
        var warning = Assert.Single(Accepted(WithTone(listed: true), StubTemplates.Empty()).Warnings);
        Assert.Equal(("S04-074", "Function 'draft_section' lists snippet 'tone' in uses but never uses it.", Function),
            (warning.Id, warning.Sentence, warning.Path));
        V31Contract.AssertSentence(warning);
    }

    [Fact]
    public void AFormatReachedThroughAnother_IsUsed()
    {
        var files = ResolveFixture.Functions()
            .WithJson("formats/index.json", f =>
            {
                f["items"]!.AsArray().Add("outline_point");
                f["items"]!.AsArray().Add("outline_points");
            })
            .With("formats/outline_point.json", Json(
                "{ \"$schema\": \"urn:consultologist:spec:31:dialect\", \"type\": \"object\", \"required\": [\"text\"], "
                + "\"additionalProperties\": false, \"properties\": { \"text\": { \"$ref\": \"plaintext\" } } }"))
            .With("formats/outline_points.json", Json(
                "{ \"$schema\": \"urn:consultologist:spec:31:dialect\", \"type\": \"object\", \"required\": [\"points\"], "
                + "\"additionalProperties\": false, \"properties\": { \"points\": { \"type\": \"array\", "
                + "\"items\": { \"$ref\": \"outline_point\" } } } }"))
            .WithJson(Function, f =>
            {
                f["uses"]!["formats"]!.AsArray().Add("outline_points");
                f["uses"]!["formats"]!.AsArray().Add("outline_point");
                f["inputs"]!["points"] = JsonNode.Parse("{ \"label\": \"Points\", \"description\": null, "
                    + "\"format\": { \"anyOf\": [{ \"$ref\": \"outline_points\" }, { \"type\": \"null\" }] } }");
            })
            .WithJson(Call, h => h["with"]!["points"] = null);
        var result = Run(files);
        Assert.True(result.Refusals.Count == 0, ResolveFixture.Said(result));
        Assert.Empty(result.Resolution!.Warnings);
    }

    [Fact]
    public void AMacroPlacedInAFunction_ReadsWhatIsThere_S04063()
    {
        var files = ResolveFixture.Functions()
            .WithJson("macros/index.json", m => m["items"]!.AsArray().Add("letrozole"))
            .With("macros/letrozole.md", Md(
                "{ \"label\": \"Letrozole advice\", \"description\": null, "
                + "\"if\": { \"var\": \"node.prescribes_letrozole.value\" }, \"format\": \"plaintext\", "
                + "\"instruction\": [\"Place {{ macro.letrozole.content }} in the plan.\"] }",
                "Take letrozole at the same time each day.\n"))
            .WithJson(Function, f => f["uses"]!["macros"]!.AsArray().Add("letrozole"));
        var placing = new StubTemplates(new Dictionary<string, string[]>(), bodies: new Dictionary<string, TemplateReads>
        {
            ["draft_section/node.standard_draft"] = new(["param.standard.value.title"], ["macro.letrozole"], [], [], false),
        });
        Accepted(files);
        AssertRefusal(files, placing, "S04-063",
            "Macro 'letrozole' is placed in function 'draft_section', where its if reads node.prescribes_letrozole, which doesn't exist there: "
            + "add a classifier with that id (pattern A), or pass the decision in and hold it in a computed node (pattern B).",
            "functions/draft_section/standard_draft.md");
    }

    /// <summary>A seam that says every body reads, places and renders hostile names.</summary>
    private sealed class HostileTemplates : ITemplateReads
    {
        private static readonly string[] Reads =
        [
            "", "node", "node..x", "node.plan", "node.plan.value.sections.0", "item", "item.parent.parent.parent.value",
            "pass", "pass.previous", "pass.previous.node.judge", "pass.node.nothing", "pass.parent.history", "run", "run.date",
            "profile", "profile.signature", "profile.nickname", "param.draft.content", "param.nothing", "check.coverage.value",
            "snippet", "snippet.standards", "snippet.standards.hpi.content", "macro.x.y.z", "renderer.nothing", "input.nothing.value",
            "deliverable.note", "é.ü", string.Join('.', Enumerable.Repeat("node", 300)),
        ];

        public TemplateReads? Of(object declaration, SourceFunction? function) =>
            new(Reads, ["macro", "macro.nothing", "macro.x.y", "", "snippet.standards"],
                [new TemplateRender(1, "node.plan.value", "nothing"), new TemplateRender(2, "", "")],
                [new TemplateRunSpot(1, "1:1", "run.date")], true);
    }

    [Fact]
    public void AnyBodyReads_AreJudgedOrRefused_NeverThrows()
    {
        var packages = new[] { ResolveFixture.NestedNote(), ResolveFixture.Conditional(), ResolveFixture.Functions(), Dated(), Signed(DraftHasText) }
            .Concat(RefusalPackages.All().Select(c => c.Valid));
        foreach (var files in packages)
        {
            foreach (var templates in new ITemplateReads?[] { null, StubTemplates.Empty(), new HostileTemplates() })
            {
                var result = Run(files, templates);
                Assert.Equal(result.Refusals.Count == 0 && result.Expressions != null, result.Derived != null);
            }
        }
    }

    [Fact]
    public void Check_TakesNoNull()
    {
        var (package, types, _) = ResolveFixture.Tree(Minimal());
        var (_, resolution) = SourceResolve.Check(package, types);
        var expressions = SourceExpressions.Check(package, types, resolution!.Scopes).Expressions!;
        Assert.Throws<ArgumentNullException>(() => SourceDerived.Check(null!, types, resolution, expressions));
        Assert.Throws<ArgumentNullException>(() => SourceDerived.Check(package, null!, resolution, expressions));
        Assert.Throws<ArgumentNullException>(() => SourceDerived.Check(package, types, null!, expressions));
        Assert.Throws<ArgumentNullException>(() => SourceDerived.Check(package, types, resolution, null!));
    }
}
