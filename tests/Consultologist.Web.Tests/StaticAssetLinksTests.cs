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
    public void AppCss_HasNoPackageImport()
    {
        // An @import of a _content path inside /css/app.css resolves under
        // /css/ and misses; the root-relative index.html link is the one home.
        Assert.DoesNotContain("@import \"_content", File.ReadAllText(Path.Combine(WwwRoot(), "css", "app.css")));
    }
}
