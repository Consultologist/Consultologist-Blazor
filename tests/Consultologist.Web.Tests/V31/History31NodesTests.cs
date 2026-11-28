using System.Text.Json;
using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using Consultologist.PackageFormat.V31.Run;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Provenance.V31;
using Consultologist.Web.Services.Workflow;
using Consultologist.Web.Shared.History31;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Consultologist.Web.Tests.V31;

/// <summary>
/// Task 18 (#1509; part 09 § 6.4 "Nodes", part 08 §§ 4, 5, 7; R4, R8): History's Nodes section at storage 12. One row per
/// node by its key with root; loops per item, a <c>while</c> per pass, a <c>call</c> into its own rows; decisions under the
/// node they concern (one that never ran too); renderer, macro, unit and run spot sub-rows; the agent, keywords not sent,
/// model-derived values, retries and the <c>onInvalid</c> outcome on the node's row. Rows render collapsed, and a row's
/// text is fetched once, when it is first opened; after the drop a row shows its hashes and A12-002.
/// </summary>
public class History31NodesTests : ClientRenderTestContext
{
    private const string JobId = "31aa31aa31aa31aa31aa31aa31aa31aa";
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 16, 0, 0, TimeSpan.Zero);

    public History31NodesTests()
    {
        Services.AddSingleton<TimeProvider>(new FixedClock(Now));
    }

    private static RecordRowRef Ref(string key, string section, string json) =>
        new(key, section, JsonSerializer.SerializeToElement(JsonNode.Parse(json)!));

    private IRenderedComponent<RecordView31> Render31(IEnumerable<RecordRowRef>? rows = null, string? resolve = null)
    {
        var header = Record31Fixtures.Header() with { JobId = JobId };
        var content = resolve is null
            ? null
            : new WorkflowPackageContentResponse("test/general", "v2026.12.1", 31, JsonSerializer.SerializeToElement(new { }),
                new Dictionary<string, string>(), Resolve: JsonSerializer.SerializeToElement(JsonNode.Parse(resolve)!));
        return Render<RecordView31>(parameters => parameters
            .Add(p => p.Detail, header)
            .Add(p => p.Record, Record31.From(header, (rows ?? Record31Fixtures.Job().Rows).ToList()))
            .Add(p => p.Texts, new RowText31(AIService, JobId))
            .Add(p => p.Content, content));
    }

    private static IElement RowOf(IRenderedComponent<RecordView31> page, string key) =>
        page.Find($"#history31-nodes [id=\"row-{key}\"]");

    private static bool HasRow(IRenderedComponent<RecordView31> page, string key) =>
        page.FindAll($"#history31-nodes [id=\"row-{key}\"]").Count > 0;

    /// <summary>The row's own line: its title, key, kind, status and reason.</summary>
    private static IElement LineOf(IRenderedComponent<RecordView31> page, string key) =>
        RowOf(page, key).QuerySelector(".history31-node__line")!;

    /// <summary>The row's own body, once opened (not a child's).</summary>
    private static IElement BodyOf(IRenderedComponent<RecordView31> page, string key) =>
        RowOf(page, key).Children.Single(child => child.ClassList.Contains("history31-node__body"));

    private static void Open(IRenderedComponent<RecordView31> page, string key) =>
        LineOf(page, key).QuerySelector(".history31-node__toggle")!.Click();

    private void TextOf(string key, string? content, bool deleted = false, string? itemName = null)
    {
        JsonElement? text = deleted ? null : JsonSerializer.SerializeToElement(new JsonObject
        {
            ["content"] = content,
            ["itemName"] = itemName,
        });
        AIService.GetJobRowAsync(JobId, key).Returns(new RecordRowText(key, "node", JsonSerializer.SerializeToElement(new { }), text, deleted));
    }

    private int RowFetches() =>
        AIService.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IAIEndpointService.GetJobRowAsync));

    // ----- rows, loops, passes, calls -------------------------------------------------------------------------------

    [Fact]
    public void EachNode_IsARow_ByItsKeyWithRoot()
    {
        var page = Render31();

        foreach (var key in new[] { "node.plan", "node.billing", "node.sections", "node.refine", "node.hpi_section", "node.route", "node.addendum", "node.never_ran" })
        {
            Assert.Equal(key, LineOf(page, key).QuerySelector(".history31-node__key")!.TextContent.Trim());
        }

        // checks and deliverables are other sections' rows; nested rows wait for their row to open
        Assert.Empty(page.FindAll("#history31-nodes [id^=\"row-check.\"]"));
        Assert.Empty(page.FindAll("#history31-nodes [id^=\"row-deliverable.\"]"));
        Assert.False(HasRow(page, "node.sections:1"));
        Assert.Equal("Plan for Margaret Okafor", LineOf(page, "node.plan").QuerySelector(".history31-node__title")!.TextContent.Trim());
        Assert.Equal("Completed", LineOf(page, "node.plan").QuerySelector(".history31-node__status")!.TextContent.Trim());
        Assert.Equal("false", LineOf(page, "node.plan").QuerySelector(".history31-node__toggle")!.GetAttribute("aria-expanded"));
    }

    [Fact]
    public void ALoop_ExpandsPerItem_NamedByItsLabel_AndByPositionAfterTheDrop()
    {
        TextOf("node.sections:1", null, itemName: "Background");
        TextOf("node.sections:2", null, deleted: true);
        var page = Render31();

        Open(page, "node.sections");

        Assert.Equal("true", LineOf(page, "node.sections").QuerySelector(".history31-node__toggle")!.GetAttribute("aria-expanded"));
        Assert.Equal("Sections 1", LineOf(page, "node.sections:1").QuerySelector(".history31-node__title")!.TextContent.Trim());
        Assert.Equal("Sections 2", LineOf(page, "node.sections:2").QuerySelector(".history31-node__title")!.TextContent.Trim());

        // opened, an item takes the name its retained text holds; after the drop, its position
        Open(page, "node.sections:1");
        page.WaitForAssertion(() => Assert.Equal("Background", LineOf(page, "node.sections:1").QuerySelector(".history31-node__title")!.TextContent.Trim()));
        Open(page, "node.sections:2");
        page.WaitForAssertion(() => Assert.Contains(HistorySentences31.TextDeleted, BodyOf(page, "node.sections:2").TextContent));
        Assert.Equal("Sections 2", LineOf(page, "node.sections:2").QuerySelector(".history31-node__title")!.TextContent.Trim());

        // the rows inside an item are under it
        Assert.True(HasRow(page, "node.sections.draft:1"));
        Assert.Contains(BodyOf(page, "node.sections:1").QuerySelectorAll("[id]"), e => e.Id == "row-node.sections.draft:1");
    }

    [Fact]
    public void AnItemWhoseNameIsPackageText_IsNamedWithoutAFetch()
    {
        var page = Render31([
            Record31Fixtures.NodeRow("node.parts", PlanKind.For, RowState.Completed, detail: """{ "label": "Parts", "count": 1, "itemLabel": "title" }"""),
            Record31Fixtures.NodeRow("node.parts:1", PlanKind.For, RowState.Completed, detail: """{ "label": "Parts", "itemName": "Intake summary" }"""),
        ]);

        Open(page, "node.parts");

        Assert.Equal("Intake summary", LineOf(page, "node.parts:1").QuerySelector(".history31-node__title")!.TextContent.Trim());
    }

    [Fact]
    public void AWhile_ExpandsPerPass()
    {
        var page = Render31();

        Open(page, "node.refine");

        Assert.Equal("Pass 1", LineOf(page, "node.refine#1").QuerySelector(".history31-node__title")!.TextContent.Trim());
        Assert.Equal("Pass 2", LineOf(page, "node.refine#2").QuerySelector(".history31-node__title")!.TextContent.Trim());
        Assert.False(HasRow(page, "node.refine.judge#2"));

        Open(page, "node.refine#2");

        Assert.True(HasRow(page, "node.refine.judge#2"));
        Assert.True(HasRow(page, "node.refine.sections#2"));
        Assert.False(HasRow(page, "node.refine.judge#1"));
    }

    [Fact]
    public void ANotRunItem_ShowsItsStatusAndReason()
    {
        var page = Render31();

        Open(page, "node.sections");

        var line = LineOf(page, "node.sections:2");
        Assert.Equal("NotRun", line.QuerySelector(".history31-node__status")!.TextContent.Trim());
        Assert.Equal("not run: beyond max 1", line.QuerySelector(".history31-node__reason")!.TextContent.Trim());
    }

    [Fact]
    public void ACall_IsOneRowThatExpands_AndNamesItsFunction()
    {
        var page = Render31();

        Assert.Contains("hpi", LineOf(page, "node.hpi_section").QuerySelector(".history31-node__function")!.TextContent);
        Assert.False(HasRow(page, "node.hpi_section.standard_draft"));

        Open(page, "node.hpi_section");

        var body = BodyOf(page, "node.hpi_section");
        Assert.True(HasRow(page, "node.hpi_section.standard_draft"));
        var argument = body.QuerySelector(".history31-node__argument")!.TextContent;
        Assert.Contains("history", argument);
        Assert.Contains("sha256:03", argument);
    }

    // ----- decisions ------------------------------------------------------------------------------------------------

    [Fact]
    public void ADecision_ShowsThePrintedCondition_ItsResult_AndTheSkipSentence()
    {
        var page = Render31();

        var line = LineOf(page, "node.addendum");
        Assert.Equal("Skipped", line.QuerySelector(".history31-node__status")!.TextContent.Trim());
        Assert.Equal("skipped: input.kind.value = \"table\" is false", line.QuerySelector(".history31-node__reason")!.TextContent.Trim());

        Open(page, "node.addendum");

        var decision = RowOf(page, "node.addendum/if:self");
        Assert.Equal("input.kind.value = \"table\"", decision.QuerySelector(".history31-decision__condition")!.TextContent.Trim());
        Assert.Equal("false", decision.QuerySelector(".history31-decision__result")!.TextContent.Trim());
        Assert.Contains("self", decision.QuerySelector(".history31-decision__site")!.TextContent);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("else")]
    public void ACaseDecision_ShowsTheArmTaken_AsStored(string arm)
    {
        var page = Render31([
            Record31Fixtures.NodeRow("node.route", PlanKind.Case, RowState.Completed),
            Record31Fixtures.DecisionRow("node.route", "case", "input.kind.value", null, arm),
        ]);

        Open(page, "node.route");

        var decision = RowOf(page, "node.route/if:case");
        Assert.Equal(arm, decision.QuerySelector(".history31-decision__result")!.TextContent.Trim());
        Assert.Equal("input.kind.value", decision.QuerySelector(".history31-decision__condition")!.TextContent.Trim());
    }

    [Fact]
    public void ARunDependentDecision_IsMarked_AndWhatItChoseIsNot()
    {
        var page = Render31([
            Record31Fixtures.NodeRow("node.addendum", PlanKind.Agent, RowState.Completed, detail: """{ "label": "Addendum" }"""),
            Record31Fixtures.DecisionRow("node.addendum", "self", "run.date.value > \"2026-11-01\"", true, runDependent: true),
        ]);

        Open(page, "node.addendum");

        var decision = RowOf(page, "node.addendum/if:self");
        Assert.NotNull(decision.QuerySelector(".history31-decision__run-dependent"));
        Assert.Null(LineOf(page, "node.addendum").QuerySelector(".history31-node__run-dependent"));
        Assert.Null(BodyOf(page, "node.addendum").QuerySelector(".history31-node__run-dependent"));
    }

    [Fact]
    public void ARunDependentRow_IsMarked()
    {
        var page = Render31();

        Assert.NotNull(LineOf(page, "node.billing").QuerySelector(".history31-node__run-dependent"));
        Assert.Null(LineOf(page, "node.plan").QuerySelector(".history31-node__run-dependent"));
    }

    [Fact]
    public void DecisionsOfANodeThatNeverRan_AreUnderIt_AndItSaysNotRun()
    {
        var page = Render31();

        Assert.Equal("NotRun", LineOf(page, "node.never_ran").QuerySelector(".history31-node__status")!.TextContent.Trim());

        Open(page, "node.never_ran");

        var body = BodyOf(page, "node.never_ran");
        var ids = body.QuerySelectorAll(".history31-decision").Select(d => d.Id).ToList();
        Assert.Equal(["row-node.never_ran/if:part.intro", "row-node.never_ran/if:self"], ids);
        // an owner with no row has no text to fetch
        Assert.Equal(0, RowFetches());
    }

    // ----- sub-rows -------------------------------------------------------------------------------------------------

    [Fact]
    public void RendererSubRows_AreUnderTheirNode_ById_WithRenderer_InputHash_OutputHash_AndPass()
    {
        var page = Render31();

        Open(page, "node.plan");

        var renders = BodyOf(page, "node.plan").QuerySelectorAll(".history31-render");
        Assert.Equal(2, renders.Length);
        Assert.Equal("row-node.plan/render:1", renders[0].Id);
        Assert.Equal("row-node.plan/render:2", renders[1].Id);
        Assert.Contains("renderer.concept_list", renders[0].TextContent);
        Assert.Contains("sha256:i1", renders[0].TextContent);
        Assert.Contains("sha256:01", renders[0].TextContent);
        Assert.Contains("2", renders[0].QuerySelector(".history31-render__pass")!.TextContent);
        Assert.Contains("renderer.bullets", renders[1].TextContent);
        Assert.Contains("node.plan/render:1", renders[1].QuerySelector(".history31-render__source")!.TextContent);
    }

    [Fact]
    public void RendererSubRows_KeepAGapInTheirNumbering()
    {
        var page = Render31([
            Record31Fixtures.NodeRow("node.summary", PlanKind.Rendered, RowState.Completed, detail: """
                { "label": "Summary", "renders": [
                    { "id": "node.summary/render:1", "renderer": "renderer.bullets", "templateHash": "sha256:t2", "source": "node.plan.value",
                      "inputHash": "sha256:i5", "pass": null, "inputSize": null, "outputHash": "sha256:05", "outputLength": 40, "runDependent": false },
                    { "id": "node.summary/render:3", "renderer": "renderer.bullets", "templateHash": "sha256:t2", "source": "node.plan.value",
                      "inputHash": "sha256:i6", "pass": null, "inputSize": null, "outputHash": "sha256:06", "outputLength": 41, "runDependent": false } ] }
                """),
        ]);

        Open(page, "node.summary");

        var ids = BodyOf(page, "node.summary").QuerySelectorAll(".history31-render").Select(r => r.Id).ToList();
        Assert.Equal(["row-node.summary/render:1", "row-node.summary/render:3"], ids);
    }

    [Fact]
    public void AMacroRow_ShowsOfferedAndPlaced_LinksItsDecisions_AndItsContentHash()
    {
        var page = Render31();

        Open(page, "node.plan");

        var macro = RowOf(page, "node.plan/macro:letrozole_text");
        Assert.Contains("macro.letrozole_text", macro.TextContent);
        Assert.Equal("offered", macro.QuerySelector(".history31-macro__offered")!.TextContent.Trim());
        Assert.Equal("placed", macro.QuerySelector(".history31-macro__placed")!.TextContent.Trim());
        Assert.Contains("sha256:c1", macro.QuerySelector(".history31-macro__content-hash")!.TextContent);
        var link = macro.QuerySelector(".history31-macro__decision a")!;
        Assert.Equal("/history/" + JobId + "#row-node.plan/if:macro.letrozole_text", link.GetAttribute("href"));

        // the decision it links to is under the same node
        Assert.True(HasRow(page, "node.plan/if:macro.letrozole_text"));
    }

    [Fact]
    public void AMacroNotOffered_SaysSo()
    {
        var page = Render31([
            Record31Fixtures.NodeRow("node.plan", PlanKind.Agent, RowState.Completed, detail: """
                { "label": "Plan", "macros": [ { "id": "node.plan/macro:letrozole_text", "macro": "macro.letrozole_text", "offered": false,
                    "decisions": [], "classifier": null, "placed": false, "form": null, "contentHash": null, "runNeutralHash": null, "runSpots": [] } ] }
                """),
        ]);

        Open(page, "node.plan");

        var macro = RowOf(page, "node.plan/macro:letrozole_text");
        Assert.Equal("not offered", macro.QuerySelector(".history31-macro__offered")!.TextContent.Trim());
        Assert.Equal("not placed", macro.QuerySelector(".history31-macro__placed")!.TextContent.Trim());
        Assert.Null(macro.QuerySelector(".history31-macro__content-hash"));
    }

    [Fact]
    public void AMacroWithNoClassifier_SaysNoSingleClassifier()
    {
        var page = Render31();

        Open(page, "node.plan");

        var macro = RowOf(page, "node.plan/macro:letrozole_text");
        Assert.Equal(HistorySentences31.NoSingleClassifier, macro.QuerySelector(".history31-macro__classifier")!.TextContent.Trim());
        Assert.Null(macro.QuerySelector(".history31-macro__classifier a"));
    }

    [Fact]
    public void AMacroWithAClassifier_LinksToThatRow()
    {
        var page = Render31();

        Open(page, "node.plan");

        var macro = RowOf(page, "node.plan/macro:follow_up_text");
        var link = macro.QuerySelector(".history31-macro__classifier a")!;
        Assert.Equal("/history/" + JobId + "#row-node.classify", link.GetAttribute("href"));
        Assert.Contains("node.classify", link.TextContent);
        Assert.DoesNotContain(HistorySentences31.NoSingleClassifier, macro.TextContent);
    }

    [Fact]
    public void AUnitNotSent_SaysSubstitutedNotSent()
    {
        var page = Render31();

        Open(page, "node.billing");

        var units = BodyOf(page, "node.billing").QuerySelectorAll(".history31-unit");
        Assert.Equal(2, units.Length);
        var sent = units.Single(u => u.TextContent.Contains("snippet.clinic_header"));
        var notSent = units.Single(u => u.TextContent.Contains("snippet.macro_body"));
        Assert.DoesNotContain(HistorySentences31.SubstitutedNotSent, sent.TextContent);
        Assert.Contains(HistorySentences31.SubstitutedNotSent, notSent.TextContent);
        Assert.Contains("sha256:u2", notSent.TextContent);
    }

    [Fact]
    public void ARunSpot_IsUnderItsNode_WithItsSiteReadAndOutput()
    {
        var page = Render31();

        Open(page, "node.billing");

        var spot = BodyOf(page, "node.billing").QuerySelector(".history31-node-spot")!;
        Assert.Equal("row-node.billing/spot:1", spot.Id);
        Assert.Contains("run.date", spot.TextContent);
        Assert.Contains("16 November 2026", spot.TextContent);
        Assert.Contains("sha256:o1", spot.TextContent);
    }

    // ----- the node's own row ---------------------------------------------------------------------------------------

    [Fact]
    public void AnInvalidOutcome_IsOnTheNodesRow()
    {
        var page = Render31();

        Open(page, "node.billing");

        var invalid = BodyOf(page, "node.billing").QuerySelector(".history31-node__invalid")!;
        Assert.Contains("macro letrozole_text: missing", invalid.TextContent);
        Assert.Contains("retry", invalid.TextContent);
        Assert.Contains("2", invalid.TextContent);
        Assert.Null(invalid.Closest(".history31-macro"));
    }

    [Fact]
    public void AnAgentRow_NamesItsDefinitionAndVersion_AndTheBinding()
    {
        var page = Render31();

        Open(page, "node.plan");

        var agent = BodyOf(page, "node.plan").QuerySelector(".history31-node__agent")!.TextContent;
        Assert.Contains("consult-prose", agent);
        Assert.Contains("4", agent);
        Assert.Contains("markdown_block", agent);
        Assert.Contains("generated", agent);
        Assert.Contains("format", agent);
    }

    [Fact]
    public void KeywordsRemoved_AreListed_AsNotSentToTheModel()
    {
        var page = Render31();

        Open(page, "node.billing");
        Open(page, "node.plan");

        Assert.Equal(HistorySentences31.NotSentToTheModel(["/properties/onset/format"]),
            BodyOf(page, "node.billing").QuerySelector(".history31-node__not-sent")!.TextContent.Trim());
        Assert.Null(BodyOf(page, "node.plan").QuerySelector(".history31-node__not-sent"));
    }

    private const string ResolveWithEdges = """
        { "nodes": { "node.draft": { "modelDerived": true }, "node.summary": { "modelDerived": true } },
          "graph": { "edges": [ { "from": "input.consult_draft", "to": "node.draft", "kind": "reads" },
                                { "from": "node.draft", "to": "node.summary", "kind": "reads" },
                                { "from": "input.consult_draft", "to": "node.summary", "kind": "reads" } ] } }
        """;

    private static RecordRowRef[] ModelDerivedRows() =>
    [
        Record31Fixtures.NodeRow("node.draft", PlanKind.Agent, RowState.Completed, detail: """{ "label": "Draft", "producer": "generated", "modelDerived": true }"""),
        Record31Fixtures.NodeRow("node.summary", PlanKind.Computed, RowState.Completed, detail: """{ "label": "Summary", "modelDerived": true }"""),
    ];

    [Fact]
    public void AModelDerivedComputedRow_SaysComputedFromModelText()
    {
        var page = Render31(ModelDerivedRows(), ResolveWithEdges);

        Open(page, "node.summary");
        Open(page, "node.draft");

        Assert.Equal(HistorySentences31.ComputedFromModelText(["node.draft"]),
            BodyOf(page, "node.summary").QuerySelector(".history31-node__model-derived")!.TextContent.Trim());
        // an agent row is model text itself: it says it is model-derived, from nothing
        Assert.DoesNotContain("computed from model text", BodyOf(page, "node.draft").TextContent);
    }

    [Fact]
    public void AModelDerivedRow_WithoutThePackagesGraph_IsStillMarked()
    {
        var page = Render31(ModelDerivedRows());

        Open(page, "node.summary");

        var mark = BodyOf(page, "node.summary").QuerySelector(".history31-node__model-derived")!;
        Assert.DoesNotContain("computed from model text", mark.TextContent);
        Assert.Contains("model", mark.TextContent);
    }

    [Fact]
    public void Retries_ShowHowMany_OfWhichKind_AndTheTotalWait()
    {
        var page = Render31();

        Open(page, "node.billing");

        var retries = BodyOf(page, "node.billing").QuerySelector(".history31-node__retries")!.TextContent;
        Assert.Contains("2 retries", retries);
        Assert.Contains("1 capacity", retries);
        Assert.Contains("1 service", retries);
        Assert.Contains("timeout", retries);
        Assert.Contains("6 s", retries);
    }

    [Theory]
    [InlineData("The model service stayed busy for 10 minutes. Run the workflow again later.")]
    [InlineData("The model service stayed busy for 10 minutes.")]
    [InlineData("The model service timed out 5 times in a row.")]
    public void AFailedNode_ShowsItsStoredReason_AndRunAgainLater_ForTheModelServicesLimits(string stored)
    {
        var page = Render31([Record31Fixtures.NodeRow("node.plan", PlanKind.Agent, RowState.Failed, stored, hash: null)]);

        var line = LineOf(page, "node.plan");
        var expected = stored.EndsWith("Run the workflow again later.", StringComparison.Ordinal) ? stored : HistorySentences31.RunAgainLater(stored);
        Assert.Equal("Failed", line.QuerySelector(".history31-node__status")!.TextContent.Trim());
        Assert.Equal(expected, line.QuerySelector(".history31-node__reason")!.TextContent.Trim());
    }

    [Fact]
    public void AFailedNode_ForAnotherReason_ShowsItAsStored()
    {
        const string stored = "Node 'node.plan' failed: macro 'letrozole_text' was missing in the reply.";
        var page = Render31([Record31Fixtures.NodeRow("node.plan", PlanKind.Agent, RowState.Failed, stored, hash: null)]);

        Assert.Equal(stored, LineOf(page, "node.plan").QuerySelector(".history31-node__reason")!.TextContent.Trim());
    }

    // ----- text, retention and a large record -----------------------------------------------------------------------

    [Fact]
    public void OpeningARow_FetchesItsTextOnce()
    {
        TextOf("node.plan", "Plan for Margaret Okafor: letrozole 2.5 mg daily.");
        var page = Render31();

        Open(page, "node.plan");
        page.WaitForAssertion(() => Assert.Contains("letrozole 2.5 mg", BodyOf(page, "node.plan").QuerySelector(".history31-node__text")!.TextContent));
        Open(page, "node.plan");
        Assert.False(HasRow(page, "node.plan/render:1"));
        Open(page, "node.plan");
        page.WaitForAssertion(() => Assert.Contains("letrozole 2.5 mg", BodyOf(page, "node.plan").QuerySelector(".history31-node__text")!.TextContent));

        AIService.Received(1).GetJobRowAsync(JobId, "node.plan");
        Assert.Equal(1, RowFetches());
    }

    [Fact]
    public void NoTextIsFetchedBeforeARowIsOpened()
    {
        var rows = Record31Fixtures.Job().Rows.ToList();
        rows.Add(Record31Fixtures.NodeRow("node.big", PlanKind.For, RowState.Completed, detail: """{ "label": "Big", "count": 200 }"""));
        for (var item = 1; item <= 200; item++)
        {
            rows.Add(Record31Fixtures.NodeRow($"node.big:{item}", PlanKind.For, RowState.Completed, detail: """{ "label": "Big" }"""));
            rows.Add(Record31Fixtures.NodeRow($"node.big.draft:{item}", PlanKind.Agent, RowState.Completed));
        }

        var page = Render31(rows);

        Assert.Equal(0, RowFetches());
        Assert.True(HasRow(page, "node.big"));
        Assert.Empty(page.FindAll("#history31-nodes [id^=\"row-node.big:\"]"));
        Assert.DoesNotContain("Margaret", page.Find("#history31-nodes").TextContent.Replace("Plan for Margaret Okafor", ""));

        Open(page, "node.big");

        // opening a loop fetches its own row's text, none of its items'
        Assert.Equal(200, page.FindAll("#history31-nodes [id^=\"row-node.big:\"]").Count);
        Assert.Equal(1, RowFetches());
    }

    [Fact]
    public void ARowWhoseTextWasDeleted_ShowsItsHashes_AndA12002()
    {
        TextOf("node.plan", null, deleted: true);
        var page = Render31();

        Open(page, "node.plan");

        page.WaitForAssertion(() => Assert.Contains(HistorySentences31.TextDeleted, BodyOf(page, "node.plan").TextContent));
        var body = BodyOf(page, "node.plan").TextContent;
        Assert.Contains("sha256:aa", body);
        Assert.Contains("sha256:f0", body);
        Assert.Contains("sha256:f2", body);
        Assert.Empty(BodyOf(page, "node.plan").QuerySelectorAll(".history31-node__text"));
    }

    [Fact]
    public void ARowWhoseTextCannotBeFetched_SaysSo_AndKeepsItsHashes()
    {
        // Final review D3: a failed fetch opens to the hashes and the record routes' sentence, as a deliverable's does.
        AIService.GetJobRowAsync(JobId, "node.plan").Returns(Task.FromException<RecordRowText?>(new HttpRequestException("down")));
        var page = Render31();

        Open(page, "node.plan");

        page.WaitForAssertion(() => Assert.Contains(HistorySentences31.RecordNotAvailable, BodyOf(page, "node.plan").TextContent));
        Assert.Contains("sha256:aa", BodyOf(page, "node.plan").TextContent);
        Assert.Empty(BodyOf(page, "node.plan").QuerySelectorAll(".history31-node__text"));
    }

    [Fact]
    public void ARowWithMissingAndUnknownMembers_StillRenders()
    {
        var page = Render31([
            Ref("node.odd", "node", """{ "kind": "agent", "renders": null, "macros": 5, "units": [ 3, { "site": null } ], "retries": {}, "agent": "x", "wholly_new": 1 }"""),
        ]);

        Open(page, "node.odd");

        Assert.True(HasRow(page, "node.odd"));
        Assert.Equal("node.odd", LineOf(page, "node.odd").QuerySelector(".history31-node__key")!.TextContent.Trim());
    }

    // ----- links ----------------------------------------------------------------------------------------------------

    [Fact]
    public void ALinkToARow_OpensItsAncestors()
    {
        var page = Render31();
        var navigation = Services.GetRequiredService<NavigationManager>();

        navigation.NavigateTo("#row-node.sections.paragraphs.draft_paragraph:1/1");

        page.WaitForAssertion(() => Assert.True(HasRow(page, "node.sections.paragraphs.draft_paragraph:1/1")));
        foreach (var key in new[] { "node.sections", "node.sections:1", "node.sections.paragraphs:1", "node.sections.paragraphs:1/1" })
        {
            Assert.Equal("true", LineOf(page, key).QuerySelector(".history31-node__toggle")!.GetAttribute("aria-expanded"));
        }
    }

    [Fact]
    public void ALinkToASubRow_OpensItsNode()
    {
        var page = Render31();
        var navigation = Services.GetRequiredService<NavigationManager>();

        navigation.NavigateTo("#row-node.plan/macro:letrozole_text");

        page.WaitForAssertion(() => Assert.True(HasRow(page, "node.plan/macro:letrozole_text")));
    }

    [Fact]
    public void ALinkToADecision_OpensItsOwner_ThatNeverRan()
    {
        var page = Render31();
        var navigation = Services.GetRequiredService<NavigationManager>();

        navigation.NavigateTo("#row-node.never_ran/if:self");

        page.WaitForAssertion(() => Assert.True(HasRow(page, "node.never_ran/if:self")));
    }

    [Fact]
    public void APageOpenedAtARow_OpensItsAncestors()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/history/" + JobId + "#row-node.refine.judge#2");

        var page = Render31();

        page.WaitForAssertion(() => Assert.True(HasRow(page, "node.refine.judge#2")));
    }

    // ----- output used in a refused deliverable (#1543, ruling 4, D12) ----------------------------------------------------

    private static RecordRowRef BlockedDeliverable(string key, params string[] reads) =>
        Record31Fixtures.NodeRow(key, PlanKind.Deliverable, RowState.Blocked, "blocked: check 'gate' failed (no)",
            detail: "{ \"label\": \"Refused\", \"sites\": [" + string.Join(", ", reads.Select((read, n) => $"{{ \"site\": {n + 1}, \"at\": \"1:1\", \"expr\": \"{read}\", \"loop\": null }}")) + "],"
                    + " \"substitutions\": [" + string.Join(", ", reads.Select((read, n) => $"{{ \"site\": {n + 1}, \"pass\": null, \"reads\": \"{read}\", \"hash\": \"sha256:s\", \"start\": 0, \"size\": 1, \"renders\": null }}")) + "] }");

    private static IEnumerable<RecordRowRef> WithDeliverables(params RecordRowRef[] deliverables) =>
        Record31Fixtures.Job().Rows.Where(row => row.Section != "deliverable").Concat(deliverables).ToList();

    private static string? MarkOf(IRenderedComponent<RecordView31> page, string key) =>
        LineOf(page, key).QuerySelector(".history31-node__refused-use")?.TextContent.Trim();

    [Fact]
    public void ANodeABlockedDeliverableRead_IsMarked_AndItsOutputHasNoCopyControl()
    {
        TextOf("node.plan", "Plan for Margaret Okafor: letrozole 2.5 mg daily.");
        var page = Render31(WithDeliverables(BlockedDeliverable("deliverable.refused", "node.plan.content")));

        Assert.Equal("Used in a refused deliverable: refused", MarkOf(page, "node.plan"));
        Assert.Null(MarkOf(page, "node.billing"));

        Open(page, "node.plan");
        page.WaitForAssertion(() => Assert.Contains("letrozole 2.5 mg", BodyOf(page, "node.plan").TextContent));

        var wrap = BodyOf(page, "node.plan").QuerySelector(".history31-node__refused-text")!;
        Assert.True(wrap.HasAttribute("blazor:oncopy:preventdefault"));
        Assert.True(wrap.HasAttribute("blazor:oncut:preventdefault"));
        Assert.NotNull(wrap.QuerySelector(".history31-node__text"));
        Assert.DoesNotContain(BodyOf(page, "node.plan").QuerySelectorAll("button"), button => button.TextContent.Contains("Copy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheMarkedOutput_CannotBeSelected()
    {
        var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Consultologist.Web", "Shared", "History31", "NodeRow31.razor.css"));

        Assert.Matches(@"\.history31-node__refused-text\s*\{[^}]*user-select:\s*none", css);
    }

    [Fact]
    public void TheOutputIsStillShown()
    {
        TextOf("node.plan", "Plan for Margaret Okafor: letrozole 2.5 mg daily.");
        var page = Render31(WithDeliverables(BlockedDeliverable("deliverable.refused", "node.plan.content")));

        Open(page, "node.plan");

        page.WaitForAssertion(() => Assert.Equal("Plan for Margaret Okafor: letrozole 2.5 mg daily.",
            BodyOf(page, "node.plan").QuerySelector(".history31-node__refused-text .history31-node__text")!.TextContent));
    }

    [Fact]
    public void ALoopsItemRows_UnderAReadLoop_AreMarked()
    {
        TextOf("node.sections:1", "Background text", itemName: "Background");
        var page = Render31(WithDeliverables(BlockedDeliverable("deliverable.refused", "node.sections.items")));

        Assert.Equal("Used in a refused deliverable: refused", MarkOf(page, "node.sections"));
        Open(page, "node.sections");
        Assert.Equal("Used in a refused deliverable: refused", MarkOf(page, "node.sections:1"));
        Open(page, "node.sections:1");
        Assert.Equal("Used in a refused deliverable: refused", MarkOf(page, "node.sections.draft:1"));
        Assert.Null(MarkOf(page, "node.plan"));
    }

    [Fact]
    public void ANodeOnlyAReleasedDeliverableRead_IsNotMarked()
    {
        TextOf("node.plan", "Plan text");
        var page = Render31();

        Assert.Null(MarkOf(page, "node.plan"));
        Open(page, "node.plan");
        page.WaitForAssertion(() => Assert.Contains("Plan text", BodyOf(page, "node.plan").TextContent));
        Assert.Empty(BodyOf(page, "node.plan").QuerySelectorAll(".history31-node__refused-text"));
        Assert.Empty(page.FindAll(".history31-node__refused-use"));
    }

    [Fact]
    public void ANodeTwoBlockedDeliverablesRead_NamesBoth()
    {
        var page = Render31(WithDeliverables(
            BlockedDeliverable("deliverable.zeta", "node.plan.content"),
            BlockedDeliverable("deliverable.alpha", "node.plan.content", "node.billing.content")));

        Assert.Equal("Used in a refused deliverable: alpha, zeta", MarkOf(page, "node.plan"));
        Assert.Equal("Used in a refused deliverable: alpha", MarkOf(page, "node.billing"));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
