using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Consultologist.Web.E2E.Tests;

/// <summary>
/// #1510 (area 13, Task 1): draws a Mermaid text through the app's own interop
/// (<c>window.consultologistMermaid.render</c>, the call every diagram on the app's pages makes) in a page of the
/// served app, so a test proves the shipped Mermaid and its settings draw that text.
/// </summary>
internal static class MermaidProbe
{
    /// <summary>
    /// The SVG Mermaid draws for <paramref name="diagram"/>. The page must have loaded the app (any route will do:
    /// <c>index.html</c> loads the interop). Throws <see cref="InvalidOperationException"/> holding Mermaid's own
    /// message when the text does not render.
    /// </summary>
    public static async Task<string> RenderAsync(IPage page, string diagram)
    {
        await page.WaitForFunctionAsync("() => !!window.consultologistMermaid", null, new() { Timeout = 30_000 });

        var result = await page.EvaluateAsync<JsonElement>(
            """
            async text => {
                try {
                    return { svg: await window.consultologistMermaid.render(text) };
                } catch (e) {
                    return { error: String(e && e.message ? e.message : e) };
                }
            }
            """,
            diagram);

        if (result.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException("Mermaid did not render the diagram: " + error.GetString());
        }
        return result.GetProperty("svg").GetString() ?? string.Empty;
    }
}
