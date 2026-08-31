using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Provenance;

namespace Consultologist.Web.Tests;

/// <summary>
/// #549: the per-stage table's logic. Honest guards over false verdicts — an
/// outputHash is compared only when the inputHash and hashVersion match
/// (hash-definitions § 4); otherwise the row names the failed precondition.
/// </summary>
public class RerunComparisonTests
{
    private static ConsultGenerationJobResponse Job(
        IReadOnlyDictionary<string, ConsultGenerationNodeStatus>? nodeOutputs = null,
        IReadOnlyList<ConsultGenerationNodeDescriptor>? nodes = null,
        IReadOnlyList<ConsultGenerationResultDocumentResponse>? documents = null,
        IReadOnlyDictionary<string, IReadOnlyList<ConsultInputOrigin>>? inputOrigins = null,
        string? effectiveInputHash = "aaaa",
        int? effectiveInputHashVersion = 5) =>
        new("job-1", "user-1", "Completed", 1, 1, 0,
            new Dictionary<string, string>(), new Dictionary<string, string>(), true,
            Nodes: nodes,
            NodeOutputs: nodeOutputs,
            AssembledDocuments: documents,
            InputOrigins: inputOrigins,
            EffectiveInputHash: effectiveInputHash,
            EffectiveInputHashVersion: effectiveInputHashVersion);

    private static ConsultGenerationNodeStatus Node(string id, string? inputHash, string? outputHash, int? hashVersion = 5) =>
        new(id, id, "Completed", inputHash, outputHash, null, null, hashVersion);

    [Fact]
    public void TheSourceJobId_ComesFromTheRerunOrigin_AndOnlyThat()
    {
        var rerun = Job(inputOrigins: new Dictionary<string, IReadOnlyList<ConsultInputOrigin>>
        {
            ["consult_draft"] = new[] { new ConsultInputOrigin("rerun", TextSha256: "aa", SourceJobId: "source-1") }
        });
        var ordinary = Job(inputOrigins: new Dictionary<string, IReadOnlyList<ConsultInputOrigin>>
        {
            ["consult_draft"] = new[] { new ConsultInputOrigin("previous-run", SourceJobId: "other-1", SourceResultId: "note") }
        });

        Assert.Equal("source-1", RerunComparison.SourceJobIdOf(rerun));
        Assert.Null(RerunComparison.SourceJobIdOf(ordinary));
        Assert.Null(RerunComparison.SourceJobIdOf(Job()));
    }

    [Fact]
    public void StageVerdicts_CompareOnlyWhenThePreconditionsHold()
    {
        var source = Job(nodeOutputs: new Dictionary<string, ConsultGenerationNodeStatus>
        {
            ["extract"] = Node("extract", "in1", "outA"),
            ["draft"] = Node("draft", "in2", "outB"),
            ["polish"] = Node("polish", "in3", "outC"),
            ["ladder"] = Node("ladder", "in4", "outD", hashVersion: 4)
        });
        var rerun = Job(
            nodes: new[]
            {
                new ConsultGenerationNodeDescriptor("extract", "Extract"),
                new ConsultGenerationNodeDescriptor("draft", "Draft"),
                new ConsultGenerationNodeDescriptor("polish", "Polish"),
                new ConsultGenerationNodeDescriptor("ladder", "Ladder"),
                new ConsultGenerationNodeDescriptor("fresh", "Fresh")
            },
            nodeOutputs: new Dictionary<string, ConsultGenerationNodeStatus>
            {
                ["extract"] = Node("extract", "in1", "outA"),        // same
                ["draft"] = Node("draft", "in2", "outX"),            // different
                ["polish"] = Node("polish", "inY", "outC"),          // inputs differ
                ["ladder"] = Node("ladder", "in4", "outD"),          // hash version differs
                ["fresh"] = Node("fresh", "in5", "outE")             // not on the source
            });

        var rows = RerunComparison.Stages(rerun, source);

        Assert.Equal(
            new[]
            {
                ("Extract", RerunComparison.Same),
                ("Draft", RerunComparison.Different),
                ("Polish", RerunComparison.InputsDiffer),
                ("Ladder", RerunComparison.HashVersionDiffers),
                ("Fresh", RerunComparison.NotOnSource)
            },
            rows.Select(row => (row.Label, row.Verdict)).ToArray());
    }

    [Fact]
    public void FannedItems_CompareByTheirCompositeKeys_InOrder()
    {
        var source = Job(nodeOutputs: new Dictionary<string, ConsultGenerationNodeStatus>
        {
            ["section:hpi"] = Node("section:hpi", "in1", "outA"),
            ["section:plan"] = Node("section:plan", "in2", "outB")
        });
        var rerun = Job(
            nodes: new[] { new ConsultGenerationNodeDescriptor("section", "Sections", ForEach: "sections") },
            nodeOutputs: new Dictionary<string, ConsultGenerationNodeStatus>
            {
                ["section:plan"] = Node("section:plan", "in2", "outB"),
                ["section:hpi"] = Node("section:hpi", "in1", "outZ")
            });

        var rows = RerunComparison.Stages(rerun, source);

        Assert.Equal(new[] { "hpi", "plan" }, rows.Select(row => row.Label));
        Assert.All(rows, row => Assert.True(row.IsItem));
        Assert.Equal(new[] { RerunComparison.Different, RerunComparison.Same }, rows.Select(row => row.Verdict));
    }

    [Fact]
    public void Deliverables_CompareByResultId()
    {
        var source = Job(documents: new[]
        {
            new ConsultGenerationResultDocumentResponse("note", "Consultation note", null, "hashA"),
            new ConsultGenerationResultDocumentResponse("letter", "Patient letter", null, "hashB")
        });
        var rerun = Job(documents: new[]
        {
            new ConsultGenerationResultDocumentResponse("note", "Consultation note", null, "hashA"),
            new ConsultGenerationResultDocumentResponse("letter", "Patient letter", null, "hashX"),
            new ConsultGenerationResultDocumentResponse("summary", "Billing summary", null, "hashY")
        });

        var rows = RerunComparison.Deliverables(rerun, source);

        Assert.Equal(
            new[]
            {
                ("Consultation note", RerunComparison.Same),
                ("Patient letter", RerunComparison.Different),
                ("Billing summary", RerunComparison.NotOnSource)
            },
            rows.Select(row => (row.Label, row.Verdict)).ToArray());
    }

    [Fact]
    public void TheJobLevelField_WinsOverTheOrigins()
    {
        // #582: RerunOf is authoritative when present; #549-era reruns still
        // resolve through their slot origins.
        var stamped = Job(inputOrigins: new Dictionary<string, IReadOnlyList<ConsultInputOrigin>>
        {
            ["consult_draft"] = new[] { new ConsultInputOrigin("rerun", SourceJobId: "origin-source") }
        }) with { RerunOf = "stamped-source" };

        Assert.Equal("stamped-source", RerunComparison.SourceJobIdOf(stamped));
    }

    [Theory]
    [InlineData("pass", null, "Verdict: pass — every reproducible stage matched the source")]
    [InlineData("fail", "draft", "Verdict: fail — first divergence at draft")]
    [InlineData("fail", "effective-inputs", "Verdict: fail — the effective inputs differ from the source; this is a bug")]
    [InlineData("no-reproducible-stages", null, "Verdict: no reproducible stages to hold — the package claims none, or none were comparable")]
    [InlineData(null, null, null)]
    [InlineData("something-newer", null, null)]
    public void TheVerdictLine_SaysTheStampedJudgment_OrNothing(string? verdict, string? divergence, string? expected) =>
        Assert.Equal(expected, RerunComparison.DescribeVerdict(verdict, divergence));

    [Fact]
    public void StageRows_MarkTheCountedStages()
    {
        var source = Job(nodeOutputs: new Dictionary<string, ConsultGenerationNodeStatus>
        {
            ["extract"] = Node("extract", "in1", "outA"),
            ["draft"] = Node("draft", "in2", "outB")
        });
        var rerun = Job(
            nodes: new[]
            {
                new ConsultGenerationNodeDescriptor("extract", "Extract", Reproducible: true),
                new ConsultGenerationNodeDescriptor("draft", "Draft")
            },
            nodeOutputs: new Dictionary<string, ConsultGenerationNodeStatus>
            {
                ["extract"] = Node("extract", "in1", "outA"),
                ["draft"] = Node("draft", "in2", "outB")
            });

        var rows = RerunComparison.Stages(rerun, source);

        Assert.Equal(new[] { true, false }, rows.Select(row => row.Reproducible));
    }

    [Theory]
    [InlineData(1234, 567, "1,234/567")]
    [InlineData(0, 0, "0/0")] // a recorded zero is a real count, not an absence
    public void RecordedCounts_RenderAsInputSlashOutput(int input, int output, string expected) =>
        Assert.Equal(expected, RerunComparison.DescribeTokens(new ConsultTokenUsage(input, output)));

    [Fact]
    public void AnAbsentCount_IsADash_NeverZero() =>
        Assert.Equal("—", RerunComparison.DescribeTokens(null));

    [Fact]
    public void StageRows_CarryBothRunsCounts()
    {
        var source = Job(nodeOutputs: new Dictionary<string, ConsultGenerationNodeStatus>
        {
            ["extract"] = new("extract", "Extract", "Completed", "in1", "outA", null, null, 5, null, new ConsultTokenUsage(1234, 567))
        });
        var rerun = Job(nodeOutputs: new Dictionary<string, ConsultGenerationNodeStatus>
        {
            ["extract"] = new("extract", "Extract", "Completed", "in1", "outA", null, null, 5, null, new ConsultTokenUsage(1300, 580))
        });

        var row = Assert.Single(RerunComparison.Stages(rerun, source));

        Assert.Equal(new ConsultTokenUsage(1234, 567), row.SourceTokens);
        Assert.Equal(new ConsultTokenUsage(1300, 580), row.RerunTokens);
    }

    [Fact]
    public void EffectiveInputs_AgreeOnlyOnHashAndVersion()
    {
        Assert.True(RerunComparison.EffectiveInputsAgree(Job(), Job()));
        Assert.False(RerunComparison.EffectiveInputsAgree(Job(effectiveInputHash: "bbbb"), Job()));
        Assert.False(RerunComparison.EffectiveInputsAgree(Job(effectiveInputHashVersion: 4), Job()));
        // A record with no hash (pre-ladder) can claim no agreement.
        Assert.False(RerunComparison.EffectiveInputsAgree(Job(effectiveInputHash: null), Job(effectiveInputHash: null)));
    }
}
