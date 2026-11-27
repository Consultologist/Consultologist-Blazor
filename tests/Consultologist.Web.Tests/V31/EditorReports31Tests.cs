using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Consultologist.Web.Shared.WorkflowEditor.V31;
using NSubstitute;

namespace Consultologist.Web.Tests.V31;

/// <summary>
/// Task 24 (#1509; part 09 § 8.1, § 6.2; D10, D11): the Editor's 31 view shows a version's publish report and, for a
/// migrated version, its migration report; a publish answer's report shows beside the page's warnings and errors. The
/// reports are read leniently and show sentences, sites, ids and hashes only.
/// </summary>
public class EditorReports31Tests : ClientRenderTestContext
{
    private const string PublishedReport = """
        {"documentVersion":1,"package":{"name":"acct-1234567890ab","version":"v2026.12.1"},"outcome":"published",
         "publishedAtUtc":"2026-12-01T09:30:00Z","publishedBy":"acct-1234567890ab",
         "sourceHash":"sha256:5001","resolveHash":"sha256:5002","lockHash":"sha256:5003",
         "normalised":["nodes/summary.md","deliverables/note.md"],
         "refusals":[],
         "warnings":[
           {"sentence":"summary could run for about 12 minutes in the worst case.","at":"nodes/summary.md"},
           {"sentence":"Nothing reads the input consult_draft.","at":null}],
         "tests":[
           {"id":"happy","fileHash":"sha256:6001","result":"pass","outcomeHash":"sha256:6002","firstMismatch":null,"durationMs":14},
           {"id":"edge","fileHash":"sha256:6003","result":"fail","outcomeHash":"sha256:6004",
            "firstMismatch":{"expectation":"deliverables","key":"deliverable.note","expected":"released","actual":"blocked"},"durationMs":9}]}
        """;

    private const string RefusedReport = """
        {"documentVersion":1,"package":{"name":"acct-1234567890ab","version":"v2026.12.2"},"outcome":"refused",
         "publishedAtUtc":null,"publishedBy":"acct-1234567890ab","sourceHash":"sha256:7001","resolveHash":null,"lockHash":null,
         "normalised":[],
         "refusals":[{"sentence":"nodes/summary.md reads an input that is not declared.","at":"nodes/summary.md"},
                     {"sentence":"The package has no deliverable.","at":null}],
         "warnings":[],"tests":[]}
        """;

    private const string MigrationJson = """
        {"oldVersion":"v2026.11.1","newVersion":"v2026.12.1",
         "handEdits":["Hand edit needed in nodes/cover_note.md, body: the template reads deliverable.note.content at line 2; rewrite it."],
         "notices":["Node 'note_coverage' declared onInvalid at 30, which 31 has no member for; it was dropped."],
         "options":{"folderFormats":{},"renames":{},"whenEmpty":{}},
         "counts":{"explained":1,"partlyExplained":1,"unexplained":2},
         "texts":[
           {"site":"general/default#status","oldHash":"sha256:a1","newHash":"sha256:a1","equal":true,"expected":false,"explanation":null,"rules":[],"unexplained":false},
           {"site":"general/default#prompts/node.summary","oldHash":"sha256:b1","newHash":"sha256:b2","equal":false,"expected":true,"explanation":"The 31 prompt names the input by its id.","rules":["inputs"],"unexplained":false},
           {"site":"general/default#prompts/node.cover","oldHash":"sha256:c1","newHash":"sha256:c2","equal":false,"expected":false,"explanation":null,"rules":[],"unexplained":true}]}
        """;

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private IRenderedComponent<PublishReport31> RenderPublish(string json) =>
        Render<PublishReport31>(p => p.Add(x => x.Report, Json(json)));

    private IRenderedComponent<MigrationReport31> RenderMigration(string json, string? from = "v2026.11.1") =>
        Render<MigrationReport31>(p => p.Add(x => x.Report, Json(json)).Add(x => x.OldVersion, from));

    private static IReadOnlyList<string> Items(IRenderedComponent<PublishReport31> view, string selector) =>
        view.FindAll(selector).Select(item => item.TextContent.Trim()).ToList();

    [Fact]
    public void ThePublishReport_ShowsItsOutcome_Time_AndPublishedBy()
    {
        var view = RenderPublish(PublishedReport);

        Assert.Equal("published", view.Find(".publish-report31__outcome").TextContent.Trim());
        Assert.Equal("2026-12-01T09:30:00Z", view.Find(".publish-report31__time").TextContent.Trim());
        Assert.Equal("acct-1234567890ab", view.Find(".publish-report31__by").TextContent.Trim());
        Assert.Contains("sha256:5001", view.Markup);
        Assert.Contains("sha256:5002", view.Markup);
        Assert.Contains("sha256:5003", view.Markup);
    }

    [Fact]
    public void ThePublishReport_ListsWarningsAndNotes_EachWithWhereItIs()
    {
        var view = RenderPublish(PublishedReport);

        var warnings = view.FindAll(".publish-report31__warnings li");
        Assert.Equal(2, warnings.Count);
        Assert.Equal("summary could run for about 12 minutes in the worst case.", warnings[0].QuerySelector(".publish-report31__sentence")!.TextContent.Trim());
        Assert.Equal("nodes/summary.md", warnings[0].QuerySelector(".publish-report31__at")!.TextContent.Trim());
        Assert.Equal("Nothing reads the input consult_draft.", warnings[1].QuerySelector(".publish-report31__sentence")!.TextContent.Trim());
        Assert.Null(warnings[1].QuerySelector(".publish-report31__at"));
        Assert.Single(view.FindAll("h4"), h => h.TextContent == "Warnings and notes");
    }

    [Fact]
    public void ThePublishReport_ListsEachTest_PassOrFail_WithItsFirstMismatch()
    {
        var view = RenderPublish(PublishedReport);

        var tests = view.FindAll(".publish-report31__test");
        Assert.Equal(2, tests.Count);
        Assert.Equal("happy", tests[0].QuerySelector(".publish-report31__test-id")!.TextContent);
        Assert.Equal("pass", tests[0].QuerySelector(".publish-report31__test-result")!.TextContent);
        Assert.Null(tests[0].QuerySelector(".publish-report31__mismatch"));
        Assert.Equal("fail", tests[1].QuerySelector(".publish-report31__test-result")!.TextContent);
        var mismatch = tests[1].QuerySelector(".publish-report31__mismatch")!.TextContent;
        Assert.Contains("deliverables", mismatch);
        Assert.Contains("deliverable.note", mismatch);
        Assert.Contains("released", mismatch);
        Assert.Contains("blocked", mismatch);
    }

    [Fact]
    public void ThePublishReport_ListsWhatWasNormalised()
    {
        var view = RenderPublish(PublishedReport);

        Assert.Equal(["nodes/summary.md", "deliverables/note.md"], Items(view, ".publish-report31__normalised li"));
    }

    [Fact]
    public void ARefusedReport_ListsItsRefusals()
    {
        var view = RenderPublish(RefusedReport);

        Assert.Equal("refused", view.Find(".publish-report31__outcome").TextContent.Trim());
        Assert.Empty(view.FindAll(".publish-report31__time"));
        var refusals = view.FindAll(".publish-report31__refusals li");
        Assert.Equal(2, refusals.Count);
        Assert.Equal("nodes/summary.md reads an input that is not declared.", refusals[0].QuerySelector(".publish-report31__sentence")!.TextContent.Trim());
        Assert.Equal("nodes/summary.md", refusals[0].QuerySelector(".publish-report31__at")!.TextContent.Trim());
        Assert.Empty(view.FindAll(".publish-report31__warnings"));
    }

    [Fact]
    public void AMigratedVersion_SaysMigratedByTheOperatorFromItsOldVersion()
    {
        var view = RenderMigration(MigrationJson);

        Assert.Equal("migrated by the operator from v2026.11.1", view.Find(".migration-report31__from").TextContent.Trim());
    }

    [Fact]
    public void TheMigrationView_ListsHandEdits_Notices_AndTheCounts()
    {
        var view = RenderMigration(MigrationJson);

        Assert.Single(view.FindAll(".migration-report31__hand-edits li"));
        Assert.StartsWith("Hand edit needed in nodes/cover_note.md", view.Find(".migration-report31__hand-edits li").TextContent);
        Assert.StartsWith("Node 'note_coverage'", view.Find(".migration-report31__notices li").TextContent);
        Assert.Equal("1", view.Find(".migration-report31__count-explained").TextContent);
        Assert.Equal("1", view.Find(".migration-report31__count-partlyExplained").TextContent);
        Assert.Equal("2", view.Find(".migration-report31__count-unexplained").TextContent);
    }

    [Fact]
    public void TheMigrationView_ListsEachComparedText_EqualExpectedOrUnexplained_WithItsExplanation()
    {
        var view = RenderMigration(MigrationJson);

        var texts = view.FindAll(".migration-report31__text");
        Assert.Equal(3, texts.Count);
        Assert.Equal("general/default#status", texts[0].QuerySelector(".migration-report31__site")!.TextContent);
        Assert.Equal("equal", texts[0].QuerySelector(".migration-report31__state")!.TextContent);
        Assert.Null(texts[0].QuerySelector(".migration-report31__explanation"));
        Assert.Equal("expected", texts[1].QuerySelector(".migration-report31__state")!.TextContent);
        Assert.Equal("sha256:b1", texts[1].QuerySelector(".migration-report31__old-hash")!.TextContent);
        Assert.Equal("sha256:b2", texts[1].QuerySelector(".migration-report31__new-hash")!.TextContent);
        Assert.Equal("The 31 prompt names the input by its id.", texts[1].QuerySelector(".migration-report31__explanation")!.TextContent);
        Assert.Equal("unexplained", texts[2].QuerySelector(".migration-report31__state")!.TextContent);
    }

    [Fact]
    public void AVersionThatWasNotMigrated_HasNoMigrationEntry()
    {
        var view = Render<V31PackageView>(p => p.Add(x => x.Content, EditorFixtures.V31()).Add(x => x.Owned, true));

        var entries = view.FindAll(".v31-package__file").Select(b => b.TextContent.Trim()).ToList();
        Assert.Contains("Publish report", entries);
        Assert.DoesNotContain("Migration", entries);
    }

    [Fact]
    public void AMigratedVersion_HasAMigrationEntry_ThatOpensTheReport()
    {
        var content = EditorFixtures.V31Migrated() with { MigrationReport = Json(MigrationJson) };
        var view = Render<V31PackageView>(p => p.Add(x => x.Content, content).Add(x => x.Owned, true));

        view.FindAll(".v31-package__file").Single(b => b.TextContent.Trim() == "Migration").Click();

        Assert.Contains("migrated by the operator from v2026.11.1", view.Find(".migration-report31").TextContent);
    }

    [Fact]
    public void ThePublishReportEntry_OpensTheReport()
    {
        var content = EditorFixtures.V31() with { PublishReport = Json(PublishedReport) };
        var view = Render<V31PackageView>(p => p.Add(x => x.Content, content).Add(x => x.Owned, true));

        view.FindAll(".v31-package__file").Single(b => b.TextContent.Trim() == "Publish report").Click();

        Assert.Equal("published", view.Find(".publish-report31__outcome").TextContent.Trim());
    }

    [Fact]
    public void APublishAnswerCarryingAReport_ShowsIt()
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V7Preludes());
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        WorkflowService.GetCatalogSchemasAsync().Returns(EditorCatalogSchemas.CatalogSchemas);
        WorkflowService.PublishPackageAsync(Arg.Any<WorkflowPackagePublishRequest>()).Returns(new WorkflowPublishOutcome(
            new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.12.1", "acct-1234567890ab@v2026.12.1", ["Worth a look."], Json(PublishedReport)),
            Array.Empty<string>(),
            Json(PublishedReport)));
        var page = Render<Templates>();
        page.FindAll("button.editor-nav__item").First(b => b.TextContent.Replace("●", "").Trim() == "guidance").Click();
        page.Find("fluent-text-area").Change("Prefer active SNOMED concepts.");

        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Publish")).Click();

        Assert.Contains("Worth a look.", page.Find(".publish-warnings").TextContent);
        Assert.Equal("published", page.Find(".publish-report31__outcome").TextContent.Trim());
    }

    [Fact]
    public void ARefusedPublishAnswer_ShowsItsErrorsAndItsReport()
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V7Preludes());
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        WorkflowService.GetCatalogSchemasAsync().Returns(EditorCatalogSchemas.CatalogSchemas);
        WorkflowService.PublishPackageAsync(Arg.Any<WorkflowPackagePublishRequest>()).Returns(new WorkflowPublishOutcome(
            null, ["The package has no deliverable."], Json(RefusedReport)));
        var page = Render<Templates>();
        page.FindAll("button.editor-nav__item").First(b => b.TextContent.Replace("●", "").Trim() == "guidance").Click();
        page.Find("fluent-text-area").Change("Prefer active SNOMED concepts.");

        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Publish")).Click();

        Assert.Contains("The package has no deliverable.", page.Markup);
        Assert.Equal("refused", page.Find(".publish-report31__outcome").TextContent.Trim());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"outcome":7,"warnings":"x","tests":[1,{"id":3,"firstMismatch":"y"}],"refusals":[null,{"sentence":5}],"normalised":[1,"a.md"]}""")]
    [InlineData("[]")]
    [InlineData("null")]
    public void AReportWithAMemberMissing_StillRenders(string json)
    {
        var publish = RenderPublish(json);
        var migration = RenderMigration(json, from: null);

        Assert.NotNull(publish.Find(".publish-report31"));
        Assert.NotNull(migration.Find(".migration-report31"));
    }
}
