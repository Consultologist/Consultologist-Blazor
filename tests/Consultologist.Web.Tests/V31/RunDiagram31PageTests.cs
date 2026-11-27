using System.Text.Json;
using Bunit;
using Consultologist.PackageFormat.V31.Diagram;
using Consultologist.PackageFormat.V31.Run;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Provenance;
using Consultologist.Web.Services.Provenance.V31;
using Consultologist.Web.Services.Workflow;
using Consultologist.Web.Shared.WorkflowEditor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NSubstitute;

namespace Consultologist.Web.Tests.V31;

/// <summary>
/// Task 10 (#1510; part 09 § 10.6; D12): the run diagram of a storage 12 job on History and on the Create page. It is
/// <see cref="RunDiagram31.Build"/>'s text over the job's <c>resolve.json</c>; on History a call box is a link to that
/// call's row (the app binds it after rendering, Mermaid staying strict), on the Create page it is not. A storage 2–11 job
/// keeps <see cref="RunDagDiagram.Build"/>'s text. The SVG itself is the browser suite's (<c>RunDiagram31E2ETests</c>).
/// </summary>
public class RunDiagram31PageTests : ClientRenderTestContext
{
    private const string JobId = "31bb31bb31bb31bb31bb31bb31bb31bb";
    private const string PackageRef = "test/general@v2026.12.1";
    private const string CallKey = "node.standards.section:1";
    private const string Draw = "consultologistMermaid.render";
    private const string Bind = "consultologistMermaid.bind";
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(5);
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 16, 0, 0, TimeSpan.Zero);

    public RunDiagram31PageTests()
    {
        Services.AddSingleton<TimeProvider>(new FixedClock(Now));
        JSInterop.Setup<string>(Draw, _ => true).SetResult("<svg id=\"workflow-dag-test\"></svg>");
    }

    private static string FunctionsResolve =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "V31Resolve", "functions.resolve.json"));

    private static ConsultGenerationJobResponse Header() => Record31Fixtures.Header() with
    {
        JobId = JobId,
        CreatedAtUtc = Now.AddMinutes(-20),
        StartedAtUtc = Now.AddMinutes(-19),
        CompletedAtUtc = Now.AddMinutes(-10),
    };

    /// <summary>The functions golden's loop, its item, and the call in it: one row, so the call box links.</summary>
    private static IReadOnlyList<RecordRowRef> CallRows() =>
    [
        Record31Fixtures.NodeRow("node.standards", PlanKind.For, RowState.Completed, detail: """{ "label": "Standards", "count": 1 }"""),
        Record31Fixtures.NodeRow("node.standards:1", PlanKind.For, RowState.Completed, detail: """{ "label": "Standards" }"""),
        Record31Fixtures.NodeRow(CallKey, PlanKind.Call, RowState.Completed, hash: "sha256:5555555555", detail: """
            { "function": "draft_section", "arguments": [ { "param": "standard", "hash": "sha256:3333333333" } ], "depth": 0 }
            """),
    ];

    private static WorkflowPackageContentResponse Content(string? resolve) => new(
        "test/general", "v2026.12.1", 31,
        JsonDocument.Parse("""{"specVersion":31}""").RootElement.Clone(),
        new Dictionary<string, string>(),
        Resolve: resolve is null ? null : JsonDocument.Parse(resolve).RootElement.Clone());

    private void OnHistory(ConsultGenerationJobResponse header, IReadOnlyList<RecordRowRef> rows, string? resolve)
    {
        AccountService.GetJobsAsync(Arg.Any<int>(), Arg.Any<string?>()).Returns(new AccountJobsResponse(
            [new AccountJobSummaryResponse(header.JobId, header.Status, header.CreatedAtUtc ?? Now, header.StartedAtUtc, header.CompletedAtUtc, 6, 6, 0)],
            null));
        AIService.GetConsultGenerationJobAsync(header.JobId).Returns(header);
        AIService.GetJobRowsAsync(header.JobId).Returns(rows);
        WorkflowService.GetCurrentPackageContentAsync(PackageRef).Returns(Content(resolve));
    }

    private IRenderedComponent<History> OpenHistory(string jobId = JobId) =>
        Render<History>(parameters => parameters.Add(p => p.JobId, jobId));

    private string RenderedText() => (string)JSInterop.Invocations[Draw].Last().Arguments[0]!;

    [Fact]
    public void AStorage12Job_OffersTheRunDiagram()
    {
        var header = Header();
        OnHistory(header, CallRows(), FunctionsResolve);

        var page = OpenHistory();
        page.WaitForElement(".history31-run-diagram").Click();

        var expected = RunDiagram31.Build(FunctionsResolve, Record31.From(header, CallRows()), linkCalls: true);
        page.WaitForAssertion(() => Assert.Equal(expected.Mermaid, RenderedText()), Settle);
        Assert.Contains("running", page.Find(".run-dag-legend").TextContent);

        // the call box's anchor is handed to the binding, which History's rows answer
        page.WaitForAssertion(() => Assert.Single(JSInterop.Invocations[Bind]), Settle);
        var bound = JSInterop.Invocations[Bind].Single();
        var anchors = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(bound.Arguments[1]);
        Assert.Equal(new Dictionary<string, string> { ["node__standards__section"] = "row-" + CallKey }, anchors);
    }

    [Fact]
    public async Task FollowingACallBox_ClosesTheDialog_AndOpensThatCallsRow()
    {
        OnHistory(Header(), CallRows(), FunctionsResolve);

        var page = OpenHistory();
        page.WaitForElement(".history31-run-diagram").Click();
        page.WaitForAssertion(() => Assert.Single(JSInterop.Invocations[Bind]), Settle);
        var bound = JSInterop.Invocations[Bind].Single();
        var view = Assert.IsType<DotNetObjectReference<WorkflowDagView>>(bound.Arguments[2]);
        Assert.Empty(page.FindAll($"[id='row-{CallKey}']"));

        await page.InvokeAsync(() => view.Value.Follow("row-" + CallKey));

        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll(".run-dag-panel"));
            Assert.Equal("true", page.Find($"[id='row-{CallKey}'] .history31-node__toggle").GetAttribute("aria-expanded"));
        }, Settle);
        // the binding is let go with the dialog
        Assert.Single(JSInterop.Invocations["consultologistMermaid.unbind"]);
    }

    [Fact]
    public void TheOverlay_DrawsRunDiagram31sText()
    {
        var text = RunDiagram31.Build(FunctionsResolve, Record31.From(Header(), CallRows()), linkCalls: true);

        var overlay = Render<RunDagOverlay>(parameters => parameters.Add(p => p.Diagram31, text));

        overlay.WaitForAssertion(() => Assert.Equal(text.Mermaid, RenderedText()), Settle);
        // its legend is the 5–30 one: the five classes are RunDagDiagram's colours
        Assert.Equal("yet to run · skipped · completed · failed · running",
            string.Join(" ", overlay.Find(".run-dag-legend").TextContent.Split((char[])[' ', '\n'], StringSplitOptions.RemoveEmptyEntries)));
    }

    [Fact]
    public void AStorage11Job_KeepsTodaysRunDiagram()
    {
        var header = new ConsultGenerationJobResponse(
            JobId, "user-1", "Completed",
            TotalBlockCount: 1, CompletedBlockCount: 1, FailedBlockCount: 0,
            GeneratedBlocks: new Dictionary<string, string>(), FailedBlocks: new Dictionary<string, string>(),
            Success: true,
            Nodes: [new ConsultGenerationNodeDescriptor("draft", "Drafting", "p")],
            NodeOutputs: new Dictionary<string, ConsultGenerationNodeStatus> { ["draft"] = new("draft", "Drafting", "Completed", "i", "o", null, null) },
            SchemaVersion: 11);
        OnHistory(header, [], FunctionsResolve);

        var page = OpenHistory();
        page.WaitForElement(".run-dag-button").Click();

        page.WaitForAssertion(() => Assert.Equal(RunDagDiagram.Build(header), RenderedText()), Settle);
        Assert.Empty(page.FindAll(".history31-run-diagram"));
        Assert.False(JSInterop.Invocations.Any(invocation => invocation.Identifier == Bind));
    }

    [Fact]
    public void TheCreatePage_OffersTheRunDiagram_AndFetchesTheRowsOnOpening()
    {
        var job = Header();
        OnCreate(job, FunctionsResolve);

        var page = Render<Consults>(parameters => parameters.Add(p => p.JobId, JobId));
        var button = page.WaitForElement(".run31-run-diagram");
        // listed once on attaching; the diagram asks again when it opens
        AIService.Received(1).GetJobRowsAsync(JobId);

        button.Click();

        page.WaitForAssertion(() => AIService.Received(2).GetJobRowsAsync(JobId), Settle);
        var expected = RunDiagram31.Build(FunctionsResolve, Record31.From(job, CallRows()), linkCalls: false);
        page.WaitForAssertion(() => Assert.Equal(expected.Mermaid, RenderedText()), Settle);
        Assert.NotEmpty(page.FindAll(".run-dag-panel"));
        // the Create page has no History rows: no call box is a link
        Assert.Empty(expected.Anchors);
        Assert.False(JSInterop.Invocations.Any(invocation => invocation.Identifier == Bind));

        page.Find(".run-dag-close").Click();
        Assert.Empty(page.FindAll(".run-dag-panel"));
    }

    [Fact]
    public void WithoutTheCompiledDocument_TheButtonIsNotShown()
    {
        OnHistory(Header(), CallRows(), resolve: null);
        var history = OpenHistory();
        history.WaitForElement(".history31");
        Assert.Empty(history.FindAll(".history31-run-diagram"));
        Assert.Empty(history.FindAll(".run-dag-button"));

        OnCreate(Header(), resolve: null);
        var create = Render<Consults>(parameters => parameters.Add(p => p.JobId, JobId));
        create.WaitForElement(".run31-rail");
        Assert.Empty(create.FindAll(".run31-run-diagram"));
    }

    private void OnCreate(ConsultGenerationJobResponse job, string? resolve)
    {
        WorkflowService.GetCurrentPackageAsync().Returns(Package31Fixtures.Package(Package31Fixtures.Input("referral", "Referral", true, Package31Fixtures.Plaintext)));
        WorkflowService.GetCurrentPackageContentAsync()
            .Returns<Task<WorkflowPackageContentResponse>>(_ => throw new InvalidOperationException("content unavailable"));
        WorkflowService.GetCurrentPackageContentAsync(PackageRef).Returns(Content(resolve));
        AIService.GetConsultGenerationJobAsync(JobId).Returns(job);
        AIService.GetJobRowsAsync(JobId).Returns(CallRows());
    }
}
