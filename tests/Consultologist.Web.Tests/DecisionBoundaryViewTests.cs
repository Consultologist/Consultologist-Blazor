using AngleSharp.Dom;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using Consultologist.Web.Services.AI;
using NSubstitute;
using NSubstitute.Extensions;

namespace Consultologist.Web.Tests;

/// <summary>v10 step (e) (#496): what the user sees while a job decides, and after.</summary>
public class DecisionBoundaryViewTests : ClientRenderTestContext
{
    private const string JobId = "0123456789abcdef0123456789abcdef";

    private static IReadOnlyList<ConsultGenerationNodeDescriptor> ClassifierNodes() => new[]
    {
        new ConsultGenerationNodeDescriptor("scope", "Is the referral in scope?", "classify", OutputContract: "classification"),
        new ConsultGenerationNodeDescriptor("assemble-note", "Assembling note", Aggregate: new[] { "node:draft" })
    };

    private void WithJob(string status, bool deciding, DateTimeOffset? decidedAt, IReadOnlyDictionary<string, string>? classifications = null, string? failureKind = null, string? startFailure = null, int total = 0)
    {
        WithPinnedPackage(blocks: new[] { Block("section-instructions:hpi", "History of Present Illness") });
        WorkflowService.Configure().GetCurrentPackageContentAsync().Returns(EditorFixtures.V7());
        AIService.GetConsultGenerationJobAsync(JobId).Returns(new ConsultGenerationJobResponse(
            JobId, "user-1", status,
            TotalBlockCount: total, CompletedBlockCount: 0, FailedBlockCount: 0,
            GeneratedBlocks: new Dictionary<string, string>(),
            FailedBlocks: new Dictionary<string, string>(),
            Success: false,
            Nodes: ClassifierNodes(),
            NodeOutputs: classifications is null ? null : classifications.ToDictionary(
                pair => pair.Key,
                pair => new ConsultGenerationNodeStatus(pair.Key, "Is the referral in scope?", "Completed", null, null, null, null, 5, pair.Value),
                StringComparer.Ordinal),
            StartFailure: startFailure,
            Deciding: deciding,
            DecidedAtUtc: decidedAt,
            Classifications: classifications,
            DecisionFailureKind: failureKind));
    }

    private IRenderedComponent<Consults> RenderRail() => Render<Consults>(parameters => parameters.Add(page => page.JobId, JobId));

    private static IElement RowFor(IRenderedComponent<Consults> page, string label) =>
        page.FindAll(".node-row").First(row => row.QuerySelector(".node-row__label")?.TextContent.Trim() == label);

    [Fact]
    public void ADecidingJob_NamesTheStage_AndSaysTheCountIsNotYetDecided()
    {
        // Cancelled: terminal, so the page does not poll, and a scheduled
        // classifier job called off before it ran genuinely never decided.
        WithJob("Cancelled", deciding: true, decidedAt: null);

        var page = RenderRail();

        Assert.NotNull(RowFor(page, "Deciding what to produce"));
        Assert.Contains("Not yet decided", page.Markup);
        Assert.Contains("— sections, not yet decided", page.Markup);
        // The classifier sits under the stage, not among the nodes.
        Assert.Contains("Is the referral in scope?", page.Markup);
    }

    [Fact]
    public void ADecidedJob_ShowsTheAnswer_AndTheCount()
    {
        WithJob("Completed", deciding: true, decidedAt: DateTimeOffset.UtcNow, classifications: new Dictionary<string, string> { ["scope"] = "in_scope" }, total: 3);

        var page = RenderRail();

        Assert.Contains("scope: in_scope", page.Markup);
        Assert.DoesNotContain("Not yet decided", page.Markup);
        Assert.Contains("/3 sections", page.Markup);
    }

    [Fact]
    public void AJobThatEndedDeciding_IsNamedByKind()
    {
        WithJob("Failed", deciding: true, decidedAt: null, classifications: new Dictionary<string, string> { ["scope"] = "out_of_scope" },
            failureKind: DecisionState.NothingApplied, startFailure: "No document applies after classification. 'Consultation note' needs node:scope to be 'in_scope'; it is 'out_of_scope'.");

        var page = RenderRail();

        Assert.Contains("Nothing applied", string.Join(" | ", page.FindAll(".node-row__label").Select(l => l.TextContent.Trim())));
        Assert.Contains("scope: out_of_scope", page.Markup);
    }

    // --- History

    private void WithHistoryRow(string status, bool deciding, bool failedAtStart = false, string? kind = null)
    {
        AccountService.GetJobsAsync(Arg.Any<int>(), Arg.Any<string?>()).Returns(new AccountJobsResponse(
            new[]
            {
                new AccountJobSummaryResponse(JobId, status, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null,
                    TotalBlockCount: 0, CompletedBlockCount: 0, FailedBlockCount: 0,
                    FailedAtStart: failedAtStart, Deciding: deciding, DecisionFailureKind: kind)
            },
            null));
    }

    [Fact]
    public void HistoryRow_SaysDeciding_AndNoCount()
    {
        WithHistoryRow("Completed", deciding: true);

        var page = Render<History>();

        Assert.Contains("Deciding what to produce", page.Find(".job-status-badge").TextContent);
        Assert.Contains("— / — sections, not yet decided", page.Markup);
    }

    [Theory]
    [InlineData(DecisionState.CouldNotDecide, "Failed — could not decide what to produce")]
    [InlineData(DecisionState.NothingApplied, "Failed — nothing applied after classification")]
    [InlineData(null, "Failed — nothing applied")]
    public void HistoryRow_NamesAFailureAtStartByKind(string? kind, string expected)
    {
        WithHistoryRow("Failed", deciding: false, failedAtStart: true, kind: kind);

        Assert.Equal(expected, Render<History>().Find(".job-status-badge").TextContent.Trim());
    }

    [Fact]
    public void HistoryDetail_DatesTheDecision_WithTheAnswers()
    {
        WithHistoryRow("Completed", deciding: false);
        AIService.GetConsultGenerationJobAsync(JobId).Returns(new ConsultGenerationJobResponse(
            JobId, "user-1", "Completed", 3, 3, 0, new(), new(), true,
            EffectiveInputHash: "aaaa", EffectiveInputHashVersion: 6, WorkflowOutputHash: "bbbb", WorkflowOutputHashVersion: 3,
            Nodes: ClassifierNodes(),
            Deciding: true, DecidedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-2),
            Classifications: new Dictionary<string, string> { ["scope"] = "in_scope" }));

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        var row = page.Find(".provenance-decided").TextContent;
        Assert.Contains("scope: in_scope", row);
    }
}
