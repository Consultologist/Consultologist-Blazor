using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Consultologist.Web.Shared.WorkflowEditor;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// The picker marked no option as selected and relied on the select's value
/// attribute alone. It renders twice — empty, then populated once the registry
/// listing arrives — and when the option list is replaced a real browser falls
/// back to the first option, which is `general@latest`. So every choice
/// displayed as "latest" while the correct package was loaded underneath.
///
/// bUnit does not model that reset, which is why asserting the value attribute
/// passed against a browser that was visibly wrong. The selected attribute is
/// what actually survives a re-render, so that is what these assert.
/// </summary>
public class PickerSelectionDisplayTests : ClientRenderTestContext
{
    private void WithRegistry() =>
        WorkflowService.GetPublicChainAsync().Returns(new PublicChainView(
            new[]
            {
                new PublicPackageView("general", "v2026.08.1",
                    new List<string> { "v2026.07.10", "v2026.08.1" },
                    new Dictionary<string, int> { ["v2026.07.10"] = 7, ["v2026.08.1"] = 8 })
            },
            null));


    [Fact]
    public void ChangingTheSelection_ChangesWhatIsDisplayed()
    {
        // The reproducible half. OnInitializedAsync runs once, so reading the
        // parameter only there left the display on whatever it was first told —
        // correct only while the caller happened to force a new instance, which
        // is not something a parameter should depend on.
        WithRegistry();

        var picker = Render<WorkflowPackagePicker>(parameters => parameters
            .Add(p => p.WritesPin, false)
            .Add(p => p.Selected, "general@v2026.08.1"));
        PickerTree.Open(picker);

        picker.Render(parameters => parameters
            .Add(p => p.WritesPin, false)
            .Add(p => p.Selected, "general@v2026.07.10"));

        Assert.Equal("general@v2026.07.10", PickerTree.Shown(picker));
        Assert.Equal(new[] { "general@v2026.07.10" }, PickerTree.SelectedRefs(picker));
    }

    [Fact]
    public async Task AChosenVersion_IsTheOptionMarkedSelected()
    {
        WithRegistry();
        WorkflowService.GetCurrentPackageContentAsync(Arg.Any<string?>()).Returns(EditorFixtures.V7());
        var page = Render<Templates>();

        PickerTree.Open(page);
        await PickerTree.SelectAsync(page, "general@v2026.07.10");

        // Selecting closes the panel; the trigger shows the choice, and the
        // reopened tree marks it.
        Assert.Equal("general@v2026.07.10", PickerTree.Shown(page));
        PickerTree.Open(page);
        Assert.Equal(new[] { "general@v2026.07.10" }, PickerTree.SelectedRefs(page));
    }

    [Fact]
    public async Task ExactlyOneOption_IsEverMarkedSelected()
    {
        // Two would let the browser choose, which is how this went unnoticed.
        WithRegistry();
        WorkflowService.GetCurrentPackageContentAsync(Arg.Any<string?>()).Returns(EditorFixtures.V7());
        var page = Render<Templates>();

        PickerTree.Open(page);
        await PickerTree.SelectAsync(page, "general@v2026.08.1");
        PickerTree.Open(page);

        Assert.Single(PickerTree.SelectedRefs(page));
    }
}
