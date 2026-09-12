using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Consultologist.Web.E2E.Tests;

/// <summary>
/// #710: stubs the app's API so the authed pages render from fixtures. A
/// catch-all aborts any other call to the real API host (kept hermetic — no
/// live network in CI), while the specific routes fulfill the fixtures the run
/// rail / run-diagram need. The app fails open on the aborted advisory calls.
/// </summary>
internal static class ApiMock
{
    public const string JobId = "0123456789abcdef0123456789abcdef";
    private const string ApiHost = "https://east.ca.api.consultologist.ai";

    private static readonly JsonSerializerOptions Camel = new(JsonSerializerDefaults.Web);

    private static string Json(object value) => JsonSerializer.Serialize(value, Camel);

    private static Task Fulfill(IRoute route, string body) =>
        route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = body });

    /// <summary>Register routes; call before navigating.</summary>
    public static async Task InstallAsync(IPage page)
    {
        // Catch-all first (lowest precedence): abort any other API call.
        await page.RouteAsync($"{ApiHost}/**", route => route.AbortAsync());

        await page.RouteAsync("**/Account/Jobs*", route => Fulfill(route, JobsJson()));
        await page.RouteAsync("**/ConsultGenerationJobs/*", route => Fulfill(route, DetailJson()));
        await page.RouteAsync("**/WorkflowPackages/Current", route => Fulfill(route, PackageJson()));
    }

    private static string JobsJson() => Json(new
    {
        jobs = new[]
        {
            new
            {
                jobId = JobId,
                status = "Completed",
                createdAtUtc = "2026-09-01T10:00:00Z",
                startedAtUtc = "2026-09-01T10:00:00Z",
                completedAtUtc = "2026-09-01T10:03:00Z",
                totalBlockCount = 1,
                completedBlockCount = 1,
                failedBlockCount = 0,
            },
        },
        continuationToken = (string?)null,
    });

    private static string DetailJson() => Json(new
    {
        jobId = JobId,
        appUserId = "u1",
        status = "Completed",
        totalBlockCount = 1,
        completedBlockCount = 1,
        failedBlockCount = 0,
        generatedBlocks = new Dictionary<string, string> { ["hpi"] = "History text." },
        failedBlocks = new Dictionary<string, string>(),
        success = true,
        nodes = new[]
        {
            new { id = "draft", label = "Drafting", promptId = (string?)"p", outputContract = (string?)null, forEach = (string?)null },
            new { id = "assemble", label = "Assembling", promptId = (string?)null, outputContract = (string?)null, forEach = (string?)null },
        },
        nodeOutputs = new Dictionary<string, object>
        {
            ["draft"] = new { nodeId = "draft", label = "Drafting", status = "Completed", inputHash = (string?)null, outputHash = (string?)null, completedAtUtc = (string?)null, error = (string?)null },
            ["assemble"] = new { nodeId = "assemble", label = "Assembling", status = "Completed", inputHash = (string?)null, outputHash = (string?)null, completedAtUtc = (string?)null, error = (string?)null },
        },
        workflowOutputHash = "abc123",
        assembledDocument = "The note.",
    });

    private static string PackageJson() => Json(new
    {
        name = "general",
        version = "v2026.07.10",
        specVersion = 6,
        blocks = new[] { new { id = "section-instructions:hpi", name = "hpi" } },
    });
}
