using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Consultologist.PackageFormat.V31.Run;
using Microsoft.Playwright;
using Xunit;

namespace Consultologist.Web.E2E.Tests;

/// <summary>
/// #1510 (area 13, Task 10; part 09 § 10.6; D12): the run diagram of a storage 12 job on the mocked 31 History page
/// (area 12's <see cref="History31E2ETests"/> scenario), drawn over the <c>functions</c> resolve golden with a call that
/// has one row. Mermaid stays at <c>securityLevel: "strict"</c>, so the call box is made a link by the app after
/// rendering: following it, by the mouse or by the keyboard, closes the dialog and brings that call's row into view.
/// </summary>
[Collection(BrowserCollection.Name)]
public class RunDiagram31E2ETests
{
    private const string CallKey = "node.standards.section:1";

    private readonly BrowserFixture _fixture;

    public RunDiagram31E2ETests(BrowserFixture fixture) => _fixture = fixture;

    /// <summary>The functions golden's loop, its one item and the call in it, written by the engine's own writer.</summary>
    private static readonly RunRow[] CallRows =
    [
        new("node.standards", "node.standards", PlanKind.For, RowState.Completed, null, "sha256:aa", Obj("""{ "label": "Standards", "count": 1 }""")),
        new("node.standards:1", "node.standards", PlanKind.For, RowState.Completed, null, "sha256:aa", Obj("""{ "label": "Standards" }""")),
        new(CallKey, "node.standards.section", PlanKind.Call, RowState.Completed, null, "sha256:5555555555",
            Obj("""{ "function": "draft_section", "arguments": [ { "param": "standard", "hash": "sha256:3333333333" } ], "depth": 0 }""")),
    ];

    private static JsonObject Obj(string json) => JsonNode.Parse(json)!.AsObject();

    private static Api31 Api()
    {
        var content = JsonNode.Parse(Fixtures31.Content)!.AsObject();
        content["resolve"] = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "V31Resolve", "functions.resolve.json")));

        var rows = JsonNode.Parse(Fixtures31.Rows)!.AsObject();
        foreach (var row in CallRows)
        {
            rows["rows"]!.AsArray().Add(new JsonObject { ["key"] = row.Key, ["section"] = row.Section, ["row"] = row.ToJson() });
        }

        return new Api31 { Content = content.ToJsonString(), Rows = rows.ToJsonString() };
    }

    private async Task<IPage> OpenDiagramAsync()
    {
        var page = await _fixture.Browser.NewPageAsync(new() { ViewportSize = new() { Width = 1280, Height = 900 } });
        await ApiMock.Install31Async(page, Api());
        await page.GotoAsync($"{_fixture.BaseUrl}/history/{Fixtures31.JobId}", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.ClickAsync(".history31-run-diagram", new() { Timeout = 30_000 });
        await page.WaitForSelectorAsync(".run-dag-panel .dag-view__canvas svg", new() { Timeout = 30_000 });
        await page.WaitForSelectorAsync(".run-dag-panel [role=link]", new() { Timeout = 30_000 });
        return page;
    }

    private static ILocator CallBox(IPage page) => page.Locator(".run-dag-panel [role=link]");

    private static ILocator CallRow(IPage page) => page.Locator($"#history31-nodes [id=\"row-{CallKey}\"]");

    [Fact]
    public async Task TheRunDiagramOfA31Job_Renders()
    {
        var page = await OpenDiagramAsync();

        Assert.Empty(await page.Locator(".dag-view .muted-text").AllAsync());
        var svg = page.Locator(".run-dag-panel .dag-view__canvas svg");
        await Assertions.Expect(svg).ToContainTextAsync("Completed");

        // the one link is the call's box, bound by the id Mermaid 12 gives the node: flowchart-<box id>-<n>
        await Assertions.Expect(CallBox(page)).ToHaveCountAsync(1);
        await Assertions.Expect(CallBox(page)).ToContainTextAsync("call draft_section");
        Assert.Matches(new Regex("(^|-)flowchart-node__standards__section-[0-9]+$"), await CallBox(page).GetAttributeAsync("id"));
        Assert.Equal("0", await CallBox(page).GetAttributeAsync("tabindex"));
        Assert.Equal("pointer", await CallBox(page).EvaluateAsync<string>("box => getComputedStyle(box).cursor"));
    }

    [Fact]
    public async Task ClickingACallBox_ClosesTheDialogAndShowsThatCallsRows()
    {
        var page = await OpenDiagramAsync();

        await CallBox(page).ClickAsync();

        await page.WaitForSelectorAsync(".run-dag-panel", new() { State = WaitForSelectorState.Detached, Timeout = 30_000 });
        await Assertions.Expect(CallRow(page)).ToBeFocusedAsync();
        await Assertions.Expect(CallRow(page)).ToBeInViewportAsync();
        await Assertions.Expect(CallRow(page).Locator(".history31-node__toggle").First).ToHaveAttributeAsync("aria-expanded", "true");
        Assert.Equal($"/history/{Fixtures31.JobId}", new Uri(page.Url).AbsolutePath);
    }

    [Fact]
    public async Task ACallBox_IsReachableByKeyboard()
    {
        var page = await OpenDiagramAsync();

        // Tab from where the dialog put focus until the call box holds it; the dialog keeps Tab inside it
        for (var step = 0; step < 10 && !await page.EvaluateAsync<bool>("() => document.activeElement?.getAttribute('role') === 'link'"); step++)
        {
            await page.Keyboard.PressAsync("Tab");
        }

        await Assertions.Expect(CallBox(page)).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Enter");

        await page.WaitForSelectorAsync(".run-dag-panel", new() { State = WaitForSelectorState.Detached, Timeout = 30_000 });
        await Assertions.Expect(CallRow(page)).ToBeFocusedAsync();
        await Assertions.Expect(CallRow(page)).ToBeInViewportAsync();
    }
}
