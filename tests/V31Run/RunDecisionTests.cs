using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.Api.Tests.V31Templates;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using static Consultologist.Api.Tests.V31Source.SourceFixture;
using static Consultologist.Api.Tests.V31Templates.TemplateFixture;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1506 (specVersion 31, area 09; part 08 § 5; R5, R6, D3, D4): a decision the runner records has part 08 § 5.1's members:
/// its id <c>&lt;owner&gt;/if:&lt;site&gt;</c>, its owner (the row key) and site (the site word), the printed condition, the
/// hash of its canonical JSON Logic, its result (a <c>case</c>'s arm by 1-based position, or <c>else</c>), the paths its own
/// rule reads each with the hash of the value read, and whether it is run-dependent or model-derived, followed through
/// entries, <c>item</c>, <c>pass</c> (earlier passes included) and <c>param</c>. Content is fictional (Margaret Okafor,
/// Dr A. Sample).
/// </summary>
public class RunDecisionTests
{
    private const string TableIf = "{ \"===\": [{ \"var\": \"input.kind.value\" }, \"table\"] }";

    // ---- the header if ------------------------------------------------------------------------------

    [Fact]
    public async Task AHeaderIf_RecordsItsReadsByHash()
    {
        var plan = RunFixture.Plan(KindGated());
        var (result, _, recorder) = await RunAsync(plan, new FakeRunHost(auto: true), WithInput(PlanRunnerLeavesTests.Inputs(), "kind", JsonValue.Create("narrative")));

        Assert.True(result.State == RunState.Completed, result.Sentence);
        var decision = Assert.Single(recorder.Decisions);
        Assert.Equal("node.plan", decision.Owner);
        Assert.Equal("self", decision.Site);
        Assert.Equal("node.plan/if:self", decision.Id);
        Assert.Equal("input.kind.value = \"table\"", decision.Condition);
        Assert.False(decision.Result);
        Assert.Null(decision.Branch);
        Assert.Equal([new RunRead("input.kind.value", CanonicalJson.Hash(JsonValue.Create("narrative")))], decision.Reads);
        Assert.Equal(CanonicalJson.Hash(plan.Entries["node.plan"].Json["if"]), decision.ExpressionHash);
        Assert.False(decision.RunDependent);
        Assert.False(decision.ModelDerived);
        Assert.Equal(FakeRunHost.Start, decision.DecidedAtUtc);

        // an input not supplied reads null, and hashes as null
        var (_, _, absent) = await RunAsync(plan, new FakeRunHost(auto: true), PlanRunnerLeavesTests.Inputs());
        Assert.Equal([new RunRead("input.kind.value", CanonicalJson.Hash(null))], Assert.Single(absent.Decisions).Reads);
    }

    [Fact]
    public async Task ADecisionReadingAnAgent_IsModelDerived()
    {
        var plan = RunFixture.Plan(Package()
            .WithAgent("draft", "Draft the note for Margaret Okafor.\n")
            .WithJson("nodes/draft.md", h => h["if"] = JsonNode.Parse("{ \"!==\": [{ \"var\": \"node.plan.value.sections\" }, null] }")));
        var host = new FakeRunHost(auto: true).Reply("node.plan", PlacementDecisionsTests.Sections);

        var (result, _, recorder) = await RunAsync(plan, host);

        Assert.True(result.State == RunState.Completed, result.Sentence);
        var decision = Assert.Single(recorder.Decisions);
        Assert.Equal("node.draft/if:self", decision.Id);
        Assert.Equal([new RunRead("node.plan.value.sections", CanonicalJson.Hash(JsonNode.Parse(PlacementDecisionsTests.Sections)!["sections"]))], decision.Reads);
        Assert.True(decision.ModelDerived);
        Assert.False(decision.RunDependent);
    }

    [Fact]
    public async Task ADecisionReadingRunDate_IsRunDependent()
    {
        var plan = RunFixture.Plan(Package()
            .WithRendered("stamp", "Seen {{ run.date }} (clinic).\n").RunDependent("nodes/stamp.md")
            .WithAgent("dated", "Date the note for Margaret Okafor.\n")
            .WithJson("nodes/dated.md", h => h["if"] = JsonNode.Parse("{ \"!==\": [{ \"var\": \"run.date\" }, null] }"))
            .WithAgent("stamped", "Stamp the note for Margaret Okafor.\n")
            .WithJson("nodes/stamped.md", h => h["if"] = JsonNode.Parse("{ \"!\": [{ \"blank\": [{ \"var\": \"node.stamp.content\" }] }] }")));
        var inputs = PlanRunnerLeavesTests.Inputs() with { RunFacts = new JsonObject { ["date"] = "2026-11-22" } };

        var (result, _, recorder) = await RunAsync(plan, RunFixture.Stubbed(plan), inputs);

        Assert.True(result.State == RunState.Completed, result.Sentence);

        // run.* itself
        var dated = Assert.Single(recorder.Decisions, d => d.Owner == "node.dated");
        Assert.Equal([new RunRead("run.date", CanonicalJson.Hash(JsonValue.Create("2026-11-22")))], dated.Reads);
        Assert.True(dated.RunDependent);
        Assert.False(dated.ModelDerived);

        // an entry whose runDependent is true (a rendered node: not model-derived)
        var stamped = Assert.Single(recorder.Decisions, d => d.Owner == "node.stamped");
        Assert.Equal(["node.stamp.content"], stamped.Reads.Select(r => r.Path));
        Assert.True(stamped.RunDependent);
        Assert.False(stamped.ModelDerived);
    }

    [Fact]
    public async Task ADecisionReadingAnEarlierPassOfAnAgent_IsModelDerived()
    {
        // R5: the while checked first on pass.previous.node.judge (an agent); pass 1 has no previous pass and reads null
        var plan = RunFixture.Plan(ResolveFixture.NestedNote()
            .WithJson("nodes/refine/index.json", j =>
            {
                j["checkFirst"] = true;
                j["while"] = JsonNode.Parse("""{ "!==": [{ "var": "pass.previous.node.judge.value.verdict" }, "good"] }""");
            }));
        var host = NestedHost().Reply("node.refine.judge", """{"verdict":"good"}""");

        var (result, _, recorder) = await RunAsync(plan, host);

        Assert.True(result.State == RunState.Completed, result.Sentence);
        var decisions = recorder.Decisions.Where(d => d.Owner == "node.refine").ToList();
        Assert.Equal(["node.refine/if:pass.1", "node.refine/if:pass.2"], decisions.Select(d => d.Id));
        Assert.Equal([true, false], decisions.Select(d => d.Result));
        Assert.Equal(
            [CanonicalJson.Hash(null), CanonicalJson.Hash(JsonValue.Create("good"))],
            decisions.Select(d => Assert.Single(d.Reads, r => r.Path == "pass.previous.node.judge.value.verdict").Hash));
        Assert.All(decisions, d => Assert.True(d.ModelDerived));
        Assert.All(decisions, d => Assert.False(d.RunDependent));
    }

    [Fact]
    public async Task ADecisionReadingAnItemOfAModelList_IsModelDerived()
    {
        // a loop over the plan's sections (an agent's), its body gated on the item
        var plan = RunFixture.Plan(Package()
            .WithLoop("sections", "node.plan.value.sections", "draft", "Draft the section '{{ item.value.title }}'.\n")
            .WithJson("nodes/sections/draft.md", h => h["if"] = JsonNode.Parse("{ \"!\": [{ \"blank\": [{ \"var\": \"item.value.title\" }] }] }")));
        var host = new FakeRunHost(auto: true).Reply("node.plan", PlacementDecisionsTests.Sections);

        var (result, _, recorder) = await RunAsync(plan, host);

        Assert.True(result.State == RunState.Completed, result.Sentence);
        var decisions = recorder.Decisions.Where(d => d.Owner.StartsWith("node.sections.draft:", StringComparison.Ordinal)).ToList();
        Assert.Equal(["node.sections.draft:1/if:self", "node.sections.draft:2/if:self"], decisions.Select(d => d.Id));
        Assert.Equal(
            [CanonicalJson.Hash(JsonValue.Create("History")), CanonicalJson.Hash(JsonValue.Create("Plan"))],
            decisions.Select(d => Assert.Single(d.Reads).Hash));
        Assert.All(decisions, d => Assert.True(d.ModelDerived));
        Assert.All(decisions, d => Assert.False(d.RunDependent));

        // a loop over an input's list: neither
        var conditional = RunFixture.Plan("conditional");
        var (_, _, input) = await RunAsync(conditional, RunFixture.Stubbed(conditional), RunFixture.GoldenInputs());
        var summaries = input.Decisions.Where(d => d.Owner.StartsWith("node.patient_summaries.narrative_only:", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, summaries.Count);
        Assert.All(summaries, d => Assert.False(d.ModelDerived || d.RunDependent));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ADecisionReadingAParamGivenAModelValue_IsModelDerived(bool model)
    {
        // the function's patient_draft is gated on its parameter draft, given the plan (an agent's text) or the typed draft
        var files = ResolveFixture.Functions()
            .WithJson("functions/draft_section/patient_draft.md",
                h => h["if"] = JsonNode.Parse("{ \"!\": [{ \"blank\": [{ \"var\": \"param.draft.content\" }] }] }"));
        if (model)
        {
            files.WithJson("functions/draft_section/index.json", f => f["inputs"]!["draft"]!["format"] = "plaintext")
                .WithJson("nodes/standards/section.md", h => h["with"]!["draft"] = "node.plan.content");
        }

        var plan = RunFixture.Plan(files);
        var (result, _, recorder) = await RunAsync(plan, RunFixture.Stubbed(plan));

        Assert.True(result.State == RunState.Completed, result.Sentence);
        var decisions = recorder.Decisions.Where(d => d.Owner.StartsWith("node.standards.section.patient_draft:", StringComparison.Ordinal)).ToList();
        Assert.Equal(["node.standards.section.patient_draft:assessment_plan/if:self", "node.standards.section.patient_draft:hpi/if:self"],
            decisions.Select(d => d.Id).Order(StringComparer.Ordinal));
        Assert.All(decisions, d => Assert.Equal(["param.draft.content"], d.Reads.Select(r => r.Path)));
        Assert.All(decisions, d => Assert.Equal(model, d.ModelDerived));
        Assert.All(decisions, d => Assert.False(d.RunDependent));

        // the standard (an item of a snippet folder: package text) is neither
        Assert.All(recorder.Decisions.Where(d => d.Owner.StartsWith("node.standards.section.standard_draft:", StringComparison.Ordinal)),
            d => Assert.False(d.ModelDerived || d.RunDependent));
    }

    // ---- case ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("clinic", "1")]
    [InlineData("ward", "2")]
    [InlineData("telephone", "else")]
    public async Task ACase_RecordsItsArmByPosition(string kind, string arm)
    {
        var plan = RunFixture.Plan(Encounter());
        var (result, _, recorder) = await RunAsync(plan, new FakeRunHost(auto: true), WithInput(PlanRunnerLeavesTests.Inputs(), "encounter_kind", JsonValue.Create(kind)));

        Assert.True(result.State == RunState.Completed, result.Sentence);
        var decision = Assert.Single(recorder.Decisions);
        Assert.Equal("node.opening/if:case", decision.Id);
        Assert.Equal("case", decision.Site);
        Assert.Equal("input.encounter_kind.value", decision.Condition);
        Assert.Null(decision.Result);
        Assert.Equal(arm, decision.Branch);
        Assert.Equal(arm, (string?)decision.ToJson()["result"]);
        Assert.Equal([new RunRead("input.encounter_kind.value", CanonicalJson.Hash(JsonValue.Create(kind)))], decision.Reads);
        Assert.Equal(CanonicalJson.Hash(new JsonObject { ["var"] = "input.encounter_kind.value" }), decision.ExpressionHash);
        Assert.False(decision.RunDependent || decision.ModelDerived);
    }

    // ---- placements ---------------------------------------------------------------------------------

    [Fact]
    public async Task APlacement_RecordsItsOwnRule()
    {
        var plan = RunFixture.Plan(PlacementDecisionsTests.Decided());
        var (result, _, recorder) = await RunAsync(plan, PlacementDecisionsTests.Host(plan, _ => ComposingRunHost.Judgement(true)));

        Assert.True(result.State == RunState.Completed, result.Sentence);
        var macro = Assert.Single(recorder.Decisions, d => d.Id == "node.draft/if:macro.f.m");
        Assert.Equal("node.draft", macro.Owner);
        Assert.Equal("macro.f.m", macro.Site);

        // the macro's rule reads the answer only; the entry (through its part and folder) reads the reason too
        Assert.Equal(["node.classify.value.answer"], macro.Reads.Select(r => r.Path));
        Assert.Contains("node.classify.value.reason", plan.Entries["node.draft"].Reads);
        Assert.Equal(CanonicalJson.Hash(JsonValue.Create(true)), macro.Reads[0].Hash);
        var rule = plan.Entries["node.draft"].Json["placements"]!.AsArray().Single(p => (string?)p!["site"] == "macro.f.m")!["if"];
        Assert.Equal(CanonicalJson.Hash(rule), macro.ExpressionHash);
        Assert.True(macro.ModelDerived);
        Assert.False(macro.RunDependent);
        Assert.Equal(["node.classify.value.reason"], Assert.Single(recorder.Decisions, d => d.Id == "node.draft/if:part.p").Reads.Select(r => r.Path));
    }

    [Fact]
    public async Task AGatedInNode_NeverCalled_StillHasItsPlacementDecisions()
    {
        // R6: placements are decided at the gate; the job's call limit stops the draft before its call
        var plan = RunFixture.Plan(PlacementDecisionsTests.Decided());
        var host = PlacementDecisionsTests.Host(plan, _ => ComposingRunHost.Judgement(true));

        var (result, _, recorder) = await RunAsync(plan, host, limits: new RunLimits(JobAgentCalls: 2));

        Assert.Equal(RunState.Failed, result.State);
        Assert.DoesNotContain("node.draft", host.Dispatched);
        Assert.Equal(
            ["node.draft/if:part.p", "node.draft/if:macro-folder.f", "node.draft/if:macro.f.m", "node.draft/if:snippet.s"],
            recorder.Decisions.Where(d => d.Owner == "node.draft").Select(d => d.Id));
    }

    // ---- a rule the host decides --------------------------------------------------------------------

    [Fact]
    public async Task APatternRule_DecidedByTheHost_RecordsTheReadsOfTheScopeItSent()
    {
        const string pattern = "{ \"matches\": [{ \"var\": \"input.consult_draft.content\" }, \"Okafor\"] }";
        var plan = RunFixture.Plan(Minimal().WithJson("nodes/plan.md", h => h["if"] = JsonNode.Parse(pattern)));
        var host = new FakeRunHost(auto: true, plan);

        var (result, _, recorder) = await RunAsync(plan, host);

        Assert.True(result.State == RunState.Completed, result.Sentence);
        Assert.Single(host.Rules);
        var decision = Assert.Single(recorder.Decisions);
        Assert.True(decision.Result);
        Assert.Equal([new RunRead("input.consult_draft.content", CanonicalJson.Hash(JsonValue.Create(PlanRunnerLeavesTests.Draft)))], decision.Reads);
        Assert.False(decision.RunDependent || decision.ModelDerived);
    }

    // ---- the record's form --------------------------------------------------------------------------

    [Fact]
    public void ToJson_HasExactlyPart08sMembers()
    {
        var read = new RunRead("input.kind.value", CanonicalJson.Hash(JsonValue.Create("narrative")));
        var expression = CanonicalJson.Hash(JsonNode.Parse(TableIf));
        var at = new DateTime(2026, 11, 22, 9, 30, 15, DateTimeKind.Utc);
        var decision = new RunDecision("node.plan", "self", "input.kind.value = \"table\"", expression, false, null, [read], false, true, at);

        var json = decision.ToJson();

        Assert.Equal(
            ["id", "owner", "site", "condition", "expressionHash", "result", "reads", "runDependent", "modelDerived", "decidedAtUtc"],
            json.Select(p => p.Key));
        Assert.Equal("node.plan/if:self", (string?)json["id"]);
        Assert.Equal("node.plan", (string?)json["owner"]);
        Assert.Equal("self", (string?)json["site"]);
        Assert.Equal("input.kind.value = \"table\"", (string?)json["condition"]);
        Assert.Equal(expression, (string?)json["expressionHash"]);
        Assert.False((bool)json["result"]!);
        Assert.Equal($$"""[{"path":"input.kind.value","hash":"{{read.Hash}}"}]""", json["reads"]!.ToJsonString());
        Assert.False((bool)json["runDependent"]!);
        Assert.True((bool)json["modelDerived"]!);
        Assert.Equal("2026-11-22T09:30:15.0000000Z", (string?)json["decidedAtUtc"]);

        // a case's result is its arm, as text
        var arm = new RunDecision("node.opening", "case", "input.encounter_kind.value", expression, null, "else", [], false, false, at);
        Assert.Equal("else", (string?)arm.ToJson()["result"]);
        Assert.Equal("node.opening/if:case", arm.Id);
    }

    // ---- fixtures -----------------------------------------------------------------------------------

    private static async Task<(RunResult Result, FakeRunHost Host, ListRecorder Recorder)> RunAsync(
        RunPlan plan, FakeRunHost host, RunInputs? inputs = null, RunLimits? limits = null)
    {
        var recorder = new ListRecorder();
        var result = await new PlanRunner(plan, limits ?? new RunLimits(), host, recorder).RunAsync(inputs ?? PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);
        return (result, host, recorder);
    }

    /// <summary><paramref name="inputs"/> with the input <paramref name="id"/> supplied as <paramref name="value"/>.</summary>
    private static RunInputs WithInput(RunInputs inputs, string id, JsonNode value) => inputs with
    {
        Inputs = new Dictionary<string, JsonNode?>(inputs.Inputs, StringComparer.Ordinal)
        {
            [id] = new JsonObject { ["value"] = value, ["content"] = null, ["title"] = null },
        },
    };

    /// <summary>The minimal package with the optional input <c>kind</c> (narrative or table), its plan gated on <see cref="TableIf"/>.</summary>
    private static Dictionary<string, byte[]> KindGated() => Minimal()
        .WithJson("manifest.json", m => m["inputs"]!["kind"] = JsonNode.Parse(
            "{ \"label\": \"Kind\", \"description\": null, \"format\": { \"type\": \"string\", \"enum\": [\"narrative\", \"table\"] }, "
            + "\"required\": false, \"channel\": null }"))
        .WithJson("nodes/plan.md", h => h["if"] = JsonNode.Parse(TableIf));

    /// <summary>The input <c>encounter_kind</c> and the case <c>opening</c> on it: an arm for clinic, one for ward, and an else.</summary>
    private static Dictionary<string, byte[]> Encounter() => Minimal()
        .WithJson("manifest.json", m => m["inputs"]!["encounter_kind"] = JsonNode.Parse(
            "{ \"label\": \"Encounter kind\", \"description\": null, "
            + "\"format\": { \"type\": \"string\", \"enum\": [\"clinic\", \"ward\", \"telephone\"] }, \"required\": false, \"channel\": null }"))
        .With("nodes/index.json", Index(["plan", "opening"]))
        .With("nodes/opening/index.json", Json(SourceFixture.Node("case", "Opening the letter", "\"plaintext\"", false, true,
            "\"case\": \"input.encounter_kind.value\", \"branches\": ["
            + "{ \"when\": [\"clinic\"], \"nodes\": [\"clinic_opening\"], \"ret\": \"node.opening.clinic_opening\" }, "
            + "{ \"when\": [\"ward\"], \"nodes\": [\"ward_opening\"], \"ret\": \"node.opening.ward_opening\" }], "
            + "\"else\": { \"nodes\": [\"general_opening\"], \"ret\": \"node.opening.general_opening\" }")))
        .With("nodes/opening/clinic_opening.md", Agent("Opening after a clinic visit", "\"plaintext\"", "Open the letter after the clinic visit.\n"))
        .With("nodes/opening/ward_opening.md", Agent("Opening after a ward visit", "\"plaintext\"", "Open the letter after the ward visit.\n"))
        .With("nodes/opening/general_opening.md", Agent("Opening the letter", "\"plaintext\"", "Open the letter.\n"))
        .With("deliverables/note.md", Md(Deliverable("Consultation note", "\"plaintext\"", "null", "hold"), "{{ node.opening.content }}\n"));

    /// <summary>A host for the nested note: its plan has the sections History and Plan, each outline one or two points.</summary>
    private static FakeRunHost NestedHost() => new FakeRunHost(auto: true)
        .Reply("node.plan", """{"sections":[{"title":"History","kind":"narrative"},{"title":"Plan","kind":"narrative"}]}""")
        .Reply("node.sections.outline:1", """{"points":["Onset"]}""")
        .Reply("node.sections.outline:2", """{"points":["Tests","Follow-up"]}""");
}
