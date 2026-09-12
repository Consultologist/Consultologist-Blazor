using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace Consultologist.Web.E2E.Tests;

/// <summary>
/// #707: one headless Chromium for the a11y suite. Set HEADED=1 to watch a run.
/// </summary>
public sealed class BrowserFixture : IAsyncLifetime
{
    private IPlaywright _playwright = default!;
    public IBrowser Browser { get; private set; } = default!;

    /// <summary>The served app under test; CI sets E2E_BASE_URL, dev defaults to the DevServer.</summary>
    public string BaseUrl { get; } =
        Environment.GetEnvironmentVariable("E2E_BASE_URL")?.TrimEnd('/') ?? "http://localhost:5000";

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new()
        {
            Headless = Environment.GetEnvironmentVariable("HEADED") != "1",
        });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }
        _playwright?.Dispose();
    }

    /// <summary>
    /// The repo root, found by walking up from the test assembly to the solution —
    /// so a test can load the actually-shipped wwwroot files by path.
    /// </summary>
    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repo root (Consultologist.sln).");
    }
}

[CollectionDefinition(Name)]
public sealed class BrowserCollection : ICollectionFixture<BrowserFixture>
{
    public const string Name = "browser";
}
