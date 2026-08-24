using Bunit;
using Consultologist.Web.Services.Workflow;
using Consultologist.Web.Shared.WorkflowEditor;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>#432, v9 § 4: a titled version reads as its title with the version beside it; the ref stays in view.</summary>
public class PickerTitleTests : ClientRenderTestContext
{
    private void WithRegistry() =>
        WorkflowService.GetPublicChainAsync().Returns(new PublicChainView(
            new[]
            {
                new PublicPackageView("general", "v2026.09.1",
                    new List<string> { "v2026.01.1", "v2026.08.1", "v2026.09.1" },
                    new Dictionary<string, int> { ["v2026.01.1"] = 2, ["v2026.08.1"] = 8, ["v2026.09.1"] = 9 },
                    new Dictionary<string, string> { ["v2026.09.1"] = "Breast oncology consults", ["v2026.01.1"] = "Old and titled" })
            },
            null));


    [Fact]
    public void ATitledVersion_ReadsAsItsTitleWithTheVersionBeside()
    {
        WithRegistry();
        var picker = Render<WorkflowPackagePicker>(parameters => parameters.Add(p => p.WritesPin, false).Add(p => p.Selected, "general@v2026.09.1"));
        PickerTree.Open(picker);

        Assert.Equal("Breast oncology consults — v2026.09.1", PickerTree.LabelOf(picker, "general@v2026.09.1"));
        Assert.Equal("v2026.08.1", PickerTree.LabelOf(picker, "general@v2026.08.1"));
        Assert.Equal("latest — follows updates", PickerTree.LabelOf(picker, "general@latest"));
        // Unsupported wins over titled: the version cannot be chosen whatever it is called.
        Assert.Equal("v2026.01.1 — unsupported (spec 2)", PickerTree.LabelOf(picker, "general@v2026.01.1"));
        // #448: the leaf is the package, under the Provided root; the trigger keeps the ref.
        Assert.Equal(new[] { "Breast oncology consults (general)" }, PickerTree.Packages(picker));
        Assert.Equal("general@v2026.09.1", PickerTree.Shown(picker));
    }

    [Fact]
    public void LatestUnderMine_IsNamedForYourOwnPublish()
    {
        // #406: a public package's latest follows a reviewed release; your
        // own package's latest follows your Publish button. Same node, told
        // apart by the root it sits under.
        WithRegistry();
        WorkflowService.GetMyPackagesAsync().Returns(new[]
        {
            new PublicPackageView("acct-1234567890ab/oncology/breast", "v2026.09.1", new List<string> { "v2026.09.1" }, new Dictionary<string, int> { ["v2026.09.1"] = 9 })
        });
        var picker = Render<WorkflowPackagePicker>(parameters => parameters.Add(p => p.WritesPin, false).Add(p => p.Selected, "general@v2026.09.1"));
        PickerTree.Open(picker);

        Assert.Equal("latest — your newest publish", PickerTree.LabelOf(picker, "acct-1234567890ab/oncology/breast@latest"));
        Assert.Equal("latest — follows updates", PickerTree.LabelOf(picker, "general@latest"));
    }
}
