using System;
using System.IO;
using System.Linq;
using System.Text.Json;
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

    /// <summary>
    /// #1543 (ruling 7, D15): the same diagram (Mermaid 12, ELK layout) drawn while the document carries the
    /// <c>content-security-policy</c> of <c>src/Consultologist.Web/staticwebapp.config.json</c>, read from the file at run
    /// time. The local server does not send that header, so the navigation request is fulfilled with it added (a
    /// <c>&lt;meta http-equiv&gt;</c> would not be the same: it ignores some directives). Any <c>securitypolicyviolation</c>
    /// fails the test. The Api calls are answered by Playwright routes; the app's own Api origin is listed in the
    /// policy's <c>connect-src</c>, so the policy is used as it is.
    /// </summary>
    [Fact]
    public async Task TheDiagram_DrawsUnderTheProductionContentSecurityPolicy()
    {
        var policy = ProductionPolicy();
        var (page, document, violations) = await OpenUnderPolicyAsync(policy, draw: true);

        Assert.Equal(policy, (await document.AllHeadersAsync())["content-security-policy"]);
        Assert.Equal("elk", await page.EvaluateAsync<string>("() => window.mermaid.mermaidAPI.getConfig().layout"));
        Assert.True(violations.Length == 0, "Content-Security-Policy violations: " + string.Join("; ", violations));

        // A late violation (after the drawing) is not missed: read the list again once the network is idle
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var late = await ReadViolationsAsync(page);
        Assert.True(late.Length == 0, "Content-Security-Policy violations after settling: " + string.Join("; ", late));
    }

    /// <summary>
    /// Proves the detector of the fact above: the same steps under a policy stricter than production's (its value with
    /// <c>'unsafe-eval'</c> removed from <c>script-src</c>) must put at least one <c>securitypolicyviolation</c> on the list,
    /// naming <c>script-src</c>. The app may not start under it, so the list is read after the navigation, not after the drawing.
    /// </summary>
    [Fact]
    public async Task TheDetector_ReportsAViolation_UnderAStricterPolicy()
    {
        var policy = ProductionPolicy().Replace(" 'unsafe-eval'", "");
        Assert.DoesNotContain("'unsafe-eval'", policy);
        var (page, _, _) = await OpenUnderPolicyAsync(policy, draw: false);

        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var violations = await ReadViolationsAsync(page);
        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.StartsWith("script-src", StringComparison.Ordinal));
    }

    private static string ProductionPolicy()
    {
        var config = Path.Combine(BrowserFixture.RepoRoot(), "src", "Consultologist.Web", "staticwebapp.config.json");
        return JsonDocument.Parse(File.ReadAllText(config)).RootElement
            .GetProperty("globalHeaders").GetProperty("content-security-policy").GetString()!;
    }

    private static Task<string[]> ReadViolationsAsync(IPage page) => page.EvaluateAsync<string[]>("() => window.__cspViolations");

    // Navigates to the history page with the policy on the document response and the collector installed; with draw, also draws the diagram
    private async Task<(IPage Page, IResponse Document, string[] Violations)> OpenUnderPolicyAsync(string policy, bool draw)
    {
        var page = await _fixture.Browser.NewPageAsync(new() { ViewportSize = new() { Width = 1280, Height = 900 } });
        await ApiMock.Install31Async(page, Api());
        await page.RouteAsync(request => true, async route =>
        {
            if (route.Request.ResourceType != "document" || !route.Request.IsNavigationRequest)
            {
                await route.FallbackAsync();
                return;
            }

            var response = await route.FetchAsync();
            var headers = response.Headers.ToDictionary(h => h.Key, h => h.Value, StringComparer.OrdinalIgnoreCase);
            headers["content-security-policy"] = policy;
            await route.FulfillAsync(new() { Response = response, Headers = headers });
        });
        await page.AddInitScriptAsync("""
            window.__cspViolations = [];
            document.addEventListener('securitypolicyviolation', e =>
                window.__cspViolations.push(e.violatedDirective + ' blocked=' + e.blockedURI + ' source=' + e.sourceFile));
            """);

        var document = await page.GotoAsync($"{_fixture.BaseUrl}/history/{Fixtures31.JobId}", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        if (draw)
        {
            await page.ClickAsync(".history31-run-diagram", new() { Timeout = 30_000 });
            await page.WaitForSelectorAsync(".run-dag-panel .dag-view__canvas svg", new() { Timeout = 30_000 });
            await page.WaitForSelectorAsync(".run-dag-panel [role=link]", new() { Timeout = 30_000 });
        }

        return (page, document!, await ReadViolationsAsync(page));
    }
}
