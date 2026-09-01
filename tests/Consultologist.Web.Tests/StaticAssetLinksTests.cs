namespace Consultologist.Web.Tests;

/// <summary>
/// #533: the app's static links point only at files that exist. Nothing else
/// pins index.html or app.css, so a link to an asset a package stopped
/// shipping — or a package path made relative by an @import under /css/ —
/// only ever surfaced as console noise. These read the shipped sources.
/// </summary>
public class StaticAssetLinksTests
{
    private static string WwwRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Consultologist.Web", "wwwroot")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, "src", "Consultologist.Web", "wwwroot");
    }

    [Fact]
    public void IndexHtml_DoesNotLink_TheDeadFluentCss()
    {
        // fluent.css left the Fluent package; the components' styles ship in
        // the scoped bundle, and only reboot.css is a real file.
        Assert.DoesNotContain("fluent.css", File.ReadAllText(Path.Combine(WwwRoot(), "index.html")));
    }

    [Fact]
    public void IndexHtml_LinksAFavicon_ThatShips()
    {
        var html = File.ReadAllText(Path.Combine(WwwRoot(), "index.html"));
        Assert.Contains("rel=\"icon\"", html);
        Assert.True(File.Exists(Path.Combine(WwwRoot(), "favicon.ico")), "wwwroot/favicon.ico must ship — the browser probes /favicon.ico regardless of links.");
    }

    [Fact]
    public void TheHeaderLogo_IsAFileThatShips()
    {
        // The header rendered a 1024px, 712 KB icon.png at 28px until the
        // swap to icon-192.png; this pins both that the referenced file
        // exists and that the oversized original stays gone.
        var header = File.ReadAllText(Path.Combine(WwwRoot(), "..", "Shared", "Header.razor"));
        var src = System.Text.RegularExpressions.Regex.Match(header, "<img src=\"([^\"]+)\"").Groups[1].Value;
        Assert.True(File.Exists(Path.Combine(WwwRoot(), src)), $"Header.razor's logo '{src}' must exist in wwwroot.");
        Assert.False(File.Exists(Path.Combine(WwwRoot(), "icon.png")), "wwwroot/icon.png was the 712 KB original — it must not return.");
    }

    [Fact]
    public void AppCss_HasNoPackageImport()
    {
        // An @import of a _content path inside /css/app.css resolves under
        // /css/ and misses; the root-relative index.html link is the one home.
        Assert.DoesNotContain("@import \"_content", File.ReadAllText(Path.Combine(WwwRoot(), "css", "app.css")));
    }
}
