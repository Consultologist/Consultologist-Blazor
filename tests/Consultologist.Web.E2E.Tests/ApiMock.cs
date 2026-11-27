using System;
using System.Collections.Concurrent;
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

    /// <summary>
    /// #1509 (Task 25): the specVersion 31 scenario, registered over the default one (a later route wins in
    /// Playwright), so a test that does not ask for it sees the default routes unchanged. Every 31 route counts its
    /// hits in <paramref name="api"/>.
    /// </summary>
    public static async Task Install31Async(IPage page, Api31 api)
    {
        await InstallAsync(page);

        await page.RouteAsync("**/WorkflowPackages/Current", route => Answer(route, api, "Current", api.Package));
        await page.RouteAsync("**/WorkflowPackages/Current/StartChecks*", route => Answer(route, api, "StartChecks", api.StartChecks));
        await page.RouteAsync("**/WorkflowPackages/Current/Content*", route => Answer(route, api, "Content", api.Content));
        await page.RouteAsync("**/Account/Me", route => Answer(route, api, "Me", api.AccountMe));
        await page.RouteAsync("**/Account/Settings/*", route =>
        {
            var key = Uri.UnescapeDataString(new Uri(route.Request.Url).Segments[^1]);
            return Answer(route, api, "Settings/" + key, api.Settings.TryGetValue(key, out var value) ? SettingJson(key, value) : null);
        });
        await page.RouteAsync("**/Account/Jobs*", route => Answer(route, api, "Jobs", api.Jobs));
        await page.RouteAsync("**/ConsultGenerationJobs/*", route => Answer(route, api, "Job", api.Job));
        await page.RouteAsync("**/ConsultGenerationJobs/*/Rows*", route =>
        {
            var query = new Uri(route.Request.Url).Query;
            var key = query.StartsWith("?key=", StringComparison.Ordinal) ? Uri.UnescapeDataString(query["?key=".Length..]) : null;
            return key is null
                ? Answer(route, api, "Rows", api.Rows)
                : Answer(route, api, "Rows?key=" + key, api.RowText(key));
        });
        await page.RouteAsync("**/ConsultGenerationJobs/*/Record", route => Answer(route, api, "Record", api.Record));
    }

    /// <summary>A 200 with the body, or a 404 when there is none (an unset setting, a key the job has no row for).</summary>
    private static Task Answer(IRoute route, Api31 api, string name, string? body)
    {
        var method = route.Request.Method;
        api.Hits.AddOrUpdate(method == "GET" ? name : method + " " + name, 1, (_, count) => count + 1);
        return body is null
            ? route.FulfillAsync(new() { Status = 404, ContentType = "text/plain", Body = "" })
            : Fulfill(route, body);
    }

    private static string SettingJson(string key, string value) => Json(new
    {
        key,
        value,
        contentType = "text/plain",
        updatedAtUtc = "2026-11-16T10:00:00Z",
    });

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

/// <summary>
/// #1509 (Task 25): what the 31 scenario answers, route by route (a null body is a 404), and how often each route was
/// asked. A test changes a member before navigating, or between steps (the start checks' answer, say); the route
/// handlers read them on Playwright's threads, so each body is a volatile field and the settings a concurrent map.
/// </summary>
internal sealed class Api31
{
    private volatile string? _package = Fixtures31.Package;
    private volatile string? _startChecks = Fixtures31.NoRefusals;
    private volatile string? _content = Fixtures31.Content;
    private volatile string? _accountMe = Fixtures31.AccountMe;
    private volatile string? _jobs = Fixtures31.Jobs;
    private volatile string? _job = Fixtures31.Job;
    private volatile string? _rows = Fixtures31.Rows;
    private volatile string? _record;

    public string? Package { get => _package; set => _package = value; }
    public string? StartChecks { get => _startChecks; set => _startChecks = value; }
    public string? Content { get => _content; set => _content = value; }
    public string? AccountMe { get => _accountMe; set => _accountMe = value; }
    public string? Jobs { get => _jobs; set => _jobs = value; }
    public string? Job { get => _job; set => _job = value; }
    public string? Rows { get => _rows; set => _rows = value; }
    public string? Record { get => _record; set => _record = value; }

    /// <summary>Saved account settings by key; any other key is unset (404).</summary>
    public ConcurrentDictionary<string, string> Settings { get; } = new(StringComparer.Ordinal);

    /// <summary>Hits by route: <c>Current</c>, <c>StartChecks</c>, <c>Content</c>, <c>Rows</c>, <c>Rows?key=&lt;key&gt;</c>, …; a method other than GET is named first (<c>PUT Settings/&lt;key&gt;</c>).</summary>
    public ConcurrentDictionary<string, int> Hits { get; } = new(StringComparer.Ordinal);

    public string? RowText(string key) => Fixtures31.RowText(key);

    public int HitsOf(string name) => Hits.TryGetValue(name, out var count) ? count : 0;

    /// <summary>Hits of every route whose name matches (every <c>PUT</c>, say).</summary>
    public int HitsWhere(Func<string, bool> name) => Hits.Where(hit => name(hit.Key)).Sum(hit => hit.Value);
}
