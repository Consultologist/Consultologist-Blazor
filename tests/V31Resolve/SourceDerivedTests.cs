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

    private const string Call = "nodes/standards/section.md";

    private const string Function = "functions/draft_section/index.json";

    private static SourceCheckResult Run(Dictionary<string, byte[]> files, ITemplateReads? templates = null) =>
        SourceCheck.Run(files, ResolveFixture.Published(), templates);

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
    public void Flags_SpreadAlongContent_NeverThroughAConditionOrAnEarlierPass()
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

        // A judge that reads run.date makes the while's decision run-dependent, but neither the summary that reads its
        // earlier pass nor the while that returns the summary.
        var reads = NestedReads();
        reads["node.refine.judge"] = ["node.draft_summary.content", "run.date"];
        var files = ResolveFixture.NestedNote().WithJson("nodes/refine/judge.md", h => h["runDependent"] = true);
        derived = Accepted(files, new StubTemplates(reads, everyBody: false));
        Assert.True(derived.Entries["node.refine.judge"].RunDependent);
        Assert.False(derived.Entries["node.refine.draft_summary"].RunDependent);
        Assert.False(derived.Entries["node.refine"].RunDependent);
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
            .With("nodes/signed.md", Md(SourceFixture.Node("call", "Signing the note", Plain, true, false,
                "\"if\": null, \"function\": \"sign\", \"with\": { \"sig\": \"profile.signature\" }"), ""))
            .WithJson("nodes/signed.md", h => h["profileCarrying"] = true)
            .With("nodes/signed_length.md", Md(SourceFixture.Node("computed", "Measuring the draft", "\"number\"", true, false,
                $"\"if\": {condition}, \"expression\": {{ \"size\": [{{ \"var\": \"input.consult_draft.content\" }}] }}"), ""))
            .WithJson("functions/index.json", f => f["items"]!.AsArray().Add("sign"))
            .With("functions/sign/index.json", Json(
                "{ \"label\": \"Sign the note\", \"description\": null, "
                + "\"inputs\": { \"sig\": { \"label\": \"Signature\", \"description\": null, \"format\": \"plaintext\" } }, "
                + "\"uses\": { \"snippets\": [], \"macros\": [], \"formats\": [], \"renderers\": [] }, "
                + "\"nodes\": [\"closing\"], \"ret\": \"node.closing\", \"format\": \"plaintext\" }"))
            .With("functions/sign/closing.md", Md(SourceFixture.Node("computed", "The closing", Plain, true, false,
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
            .With("functions/sign/closing.md", Agent("Writing the closing", Plain, "Write a closing line.\n\n{{ param.sig.content }}\n"))
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
        // while is not reproducible, and, an earlier pass being no content, not model-derived either.
        var files = ResolveFixture.NestedNote()
            .WithJson("nodes/refine/index.json", h =>
            {
                h["format"] = "boolean";
                h["modelDerived"] = false;
            })
            .With("nodes/refine/draft_summary.md", Md(SourceFixture.Node("computed", "Checking the last judgement", "\"boolean\"", true, false,
                "\"if\": null, \"expression\": { \"blank\": [{ \"var\": \"pass.previous.node.judge.content\" }] }"), ""));
        Assert.False(Accepted(files).Entries["node.refine"].Reproducible);
        AssertRefusal(files.WithJson("nodes/refine/index.json", h => h["reproducible"] = true), null, "S04-015",
            "While 'refine' declares reproducible true; its body makes it false.", "nodes/refine/index.json");
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
