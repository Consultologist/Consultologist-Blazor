using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Consultologist.Web.Shared.WorkflowEditor;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #447: an account holds many packages. The editor says which one a publish
/// goes to; the picker lists each of them.
/// </summary>
public class PublishTargetsTests : ClientRenderTestContext
{
    private const string Mine = "acct-1234567890ab";
    private const string Other = "acct-1234567890ab-breast-oncology";

    private WorkflowPackagePublishRequest? sent;

    private void WithPublishAccepted(string publishedRef = "acct-1234567890ab@v2026.07.2") =>
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse(publishedRef.Split('@')[0], publishedRef.Split('@')[1], publishedRef),
                Array.Empty<string>()));

    private void WithMine(params string[] names) =>
        WorkflowService.GetMyPackagesAsync().Returns(names
            .Select(name => new PublicPackageView(name, "v2026.07.1", new List<string> { "v2026.07.1" }, new Dictionary<string, int> { ["v2026.07.1"] = 7 }))
            .ToList());

    private static IReadOnlyList<string> Buttons(IRenderedComponent<Templates> page) =>
        page.FindAll(".editor-bar__actions fluent-button").Select(b => b.TextContent.Trim()).ToList();

    private static void MakePending(IRenderedComponent<Templates> page)
    {
        page.FindAll("button.editor-nav__item").First(b => b.TextContent.Contains("Inputs")).Click();
        page.Find(".add-variable__form input").Change("added_here");
        page.FindAll("button.variable-chips__add").First(b => b.TextContent.Contains("+ Input")).Click();
    }

    private static void Click(IRenderedComponent<Templates> page, string label) =>
        page.FindAll(".editor-bar__actions fluent-button").First(b => b.TextContent.Contains(label, StringComparison.Ordinal)).Click();

    [Fact]
    public void MyOwnPackage_OffersANewVersion_AndANewPackage()
    {
        WithMine(Mine, Other);
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V7());

        var page = Render<Templates>();

        Assert.Contains(Buttons(page), b => b.StartsWith("Publish new version", StringComparison.Ordinal));
        Assert.Contains(Buttons(page), b => b.StartsWith("Publish as a new package", StringComparison.Ordinal));
        Assert.DoesNotContain(Buttons(page), b => b.StartsWith("Publish as your package", StringComparison.Ordinal));
    }

    [Fact]
    public void PublishNewVersion_NamesThePackageAsTheTarget()
    {
        WithMine(Mine, Other);
        WithPublishAccepted();
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V7());
        var page = Render<Templates>();
        MakePending(page);

        Click(page, "Publish new version");

        Assert.NotNull(sent);
        Assert.Equal(Mine, sent!.Target);
        Assert.Null(sent.NewPackageSlug);
        Assert.Contains("Published acct-1234567890ab@v2026.07.2 and switched your consults to it.", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishAsANewPackage_TakesASlug_RefusesABadOne_AndSendsIt()
    {
        WithMine(Mine);
        WithPublishAccepted("acct-1234567890ab-breast-oncology@v2026.07.1");
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V7());
        var page = Render<Templates>();
        MakePending(page);

        Click(page, "Publish as a new package");
        var slug = page.Find("input[aria-label='New package path']");

        slug.Input("Breast Oncology");
        Assert.True(page.FindAll(".editor-bar__actions fluent-button").First(b => b.TextContent.Trim() == "Publish as new package").HasAttribute("disabled"));

        slug.Input("breast-oncology");
        Click(page, "Publish as new package");

        Assert.NotNull(sent);
        Assert.Null(sent!.Target);
        Assert.Equal("breast-oncology", sent.NewPackageSlug);
        Assert.Contains("Published acct-1234567890ab-breast-oncology@v2026.07.1 as a new package from acct-1234567890ab@v2026.07.1", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SomebodyElsesPackage_CanOnlyBecomeANewPackage_OnceIHaveAny()
    {
        WithMine(Mine);
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.NotMine());

        var page = Render<Templates>();

        Assert.DoesNotContain(Buttons(page), b => b.StartsWith("Publish new version", StringComparison.Ordinal));
        Assert.DoesNotContain(Buttons(page), b => b.StartsWith("Publish as your package", StringComparison.Ordinal));
        Assert.Contains(Buttons(page), b => b.StartsWith("Publish as a new package", StringComparison.Ordinal));
    }

    [Fact]
    public void WithNoPackageYet_TheFirstPublish_NeedsNoName()
    {
        // The derived name, as every publish before #447 — both target fields null.
        WithMine();
        WithPublishAccepted();
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.NotMine());
        var page = Render<Templates>();
        MakePending(page);

        Click(page, "Publish as your package");

        Assert.NotNull(sent);
        Assert.Null(sent!.Target);
        Assert.Null(sent.NewPackageSlug);
    }

    [Fact]
    public void ThePicker_ListsEachOfMyPackages_NamedByTitleWhenItHasOne()
    {
        WorkflowService.GetMyPackagesAsync().Returns(new[]
        {
            new PublicPackageView(Mine, "v2026.07.1", new List<string> { "v2026.07.1" }, new Dictionary<string, int> { ["v2026.07.1"] = 7 }),
            new PublicPackageView(Other, "v2026.09.1", new List<string> { "v2026.09.1" }, new Dictionary<string, int> { ["v2026.09.1"] = 9 },
                new Dictionary<string, string> { ["v2026.09.1"] = "Breast oncology consults" })
        });

        var picker = Render<WorkflowPackagePicker>(parameters => parameters.Add(p => p.WritesPin, false).Add(p => p.Selected, $"{Mine}@latest"));
        PickerTree.Open(picker);

        // #448: both are leaves under the Mine root — flat names, no folders.
        Assert.Equal(
            new[] { "acct-1234567890ab", "Breast oncology consults (acct-1234567890ab-breast-oncology)" },
            PickerTree.Packages(picker));
        Assert.Empty(PickerTree.Folders(picker));
    }

    [Fact]
    public void ANestedPackage_IsFiledUnderItsFolders_WithTheAccountRootHidden()
    {
        // #448: acct-<root>/oncology/breast draws as Mine ▸ oncology ▸ breast.
        WorkflowService.GetMyPackagesAsync().Returns(new[]
        {
            new PublicPackageView(Mine, "v2026.07.1", new List<string> { "v2026.07.1" }, new Dictionary<string, int> { ["v2026.07.1"] = 7 }),
            new PublicPackageView($"{Mine}/oncology/breast", "v2026.09.1", new List<string> { "v2026.09.1" }, new Dictionary<string, int> { ["v2026.09.1"] = 9 })
        });
        WorkflowService.GetPublicChainAsync().Returns(new PublicChainView(
            new[] { new PublicPackageView("oncology/lung", "v2026.09.1", new List<string> { "v2026.09.1" }, new Dictionary<string, int> { ["v2026.09.1"] = 9 }) },
            null));

        var picker = Render<WorkflowPackagePicker>(parameters => parameters.Add(p => p.WritesPin, false).Add(p => p.Selected, $"{Mine}/oncology/breast@latest"));
        PickerTree.Open(picker);

        Assert.Equal(new[] { "Provided", "Mine" }, PickerTree.Nodes(picker).Where(n => n.Id.StartsWith("root:", StringComparison.Ordinal)).Select(n => n.Label));
        Assert.Equal(new[] { "oncology", "oncology" }, PickerTree.Folders(picker));
        Assert.Equal(new[] { "lung", "acct-1234567890ab", "breast" }, PickerTree.Packages(picker));
        Assert.Contains($"{Mine}/oncology/breast@v2026.09.1", PickerTree.Refs(picker));
        Assert.Equal($"{Mine}/oncology/breast@latest", PickerTree.Shown(picker));
    }

    [Fact]
    public void PublishAsANewPackage_TakesAFolderPath()
    {
        WithMine(Mine);
        WithPublishAccepted("acct-1234567890ab/oncology/breast@v2026.07.1");
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V7());
        var page = Render<Templates>();
        MakePending(page);

        Click(page, "Publish as a new package");
        var path = page.Find("input[aria-label='New package path']");
        path.Input("oncology//breast");
        Assert.True(page.FindAll(".editor-bar__actions fluent-button").First(b => b.TextContent.Trim() == "Publish as new package").HasAttribute("disabled"));
        path.Input("oncology/breast");
        Click(page, "Publish as new package");

        Assert.Equal("oncology/breast", sent!.NewPackageSlug);
        Assert.Contains("Published acct-1234567890ab/oncology/breast@v2026.07.1 as a new package", page.Markup, StringComparison.Ordinal);
    }
}
