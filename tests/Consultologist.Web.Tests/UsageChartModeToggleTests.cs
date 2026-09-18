using Bunit;
using Consultologist.UI.Charts;
using Consultologist.UI.Components;

namespace Consultologist.Web.Tests;

/// <summary>
/// #743: the bars↔lines toggle — two buttons, the current mode marked pressed,
/// and a click raising ModeChanged with the chosen mode.
/// </summary>
public class UsageChartModeToggleTests : ClientRenderTestContext
{
    [Fact]
    public void Renders_BothOptions()
    {
        var toggle = Render<UsageChartModeToggle>(parameters => parameters
            .Add(p => p.Mode, UsageChartMode.Bar));

        var buttons = toggle.FindAll("fluent-button");
        Assert.Equal(2, buttons.Count);
        var labels = buttons.Select(b => b.TextContent.Trim()).ToList();
        Assert.Contains("Bars", labels);
        Assert.Contains("Lines", labels);
    }

    [Fact]
    public async Task ClickingLines_RaisesModeChanged_WithLine()
    {
        UsageChartMode? raised = null;
        var toggle = Render<UsageChartModeToggle>(parameters => parameters
            .Add(p => p.Mode, UsageChartMode.Bar)
            .Add(p => p.ModeChanged, (UsageChartMode m) => raised = m));

        var lines = toggle.FindAll("fluent-button").First(b => b.TextContent.Contains("Lines"));
        await lines.ClickAsync(new());

        Assert.Equal(UsageChartMode.Line, raised);
    }
}
