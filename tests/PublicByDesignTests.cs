namespace Consultologist.Api.Tests;

/// <summary>
/// #397: this repository is public by design, and the operator's material
/// lives elsewhere. This is the tripwire: a real account id, the subscription
/// id, or a recipe against the private storage account landing in a public
/// page fails here rather than shipping.
/// </summary>
public class PublicByDesignTests
{
    private static readonly string[] Roots = { "docs", "scripts", "src", "README.md", "AGENTS.md" };

    // Settings documentation names the private account as a value to configure,
    // which is not a recipe; every other mention belongs in the operations repo.
    // #545's storage record names it as the records account the settings point
    // at — an inventory, not a recipe — and joins the list.
    private static readonly string[] PrivateAccountAllowed =
    {
        "docs/CONFIGURATION.md", "docs/STORAGE.md", "docs/ACCOUNTS.md", "docs/customizable-workflow/content-repos.md",
        "docs/NETWORK_HARDENING.md", "docs/customizable-workflow/storage-separation.md", "scripts/verify-rate-limit.sh"
    };

    [Theory]
    [InlineData("acct-7bca2dcc1ed4")]
    [InlineData("a11ce24e-a0c7-4d6f-9674-2264a87483d0")]
    public void NoPublicFile_CarriesARealIdentifier(string needle) =>
        Assert.Empty(Hits(needle, Array.Empty<string>()));

    [Fact]
    public void ThePrivateAccount_IsNamedOnlyAsASettingValue() =>
        Assert.Empty(Hits("consultjobrecscaeast", PrivateAccountAllowed));

    // #556: the text account joins the same discipline — a setting value and
    // an operator runbook, never a recipe scattered anywhere else.
    private static readonly string[] TextAccountAllowed =
    {
        "docs/CONFIGURATION.md", "docs/STORAGE.md", "docs/customizable-workflow/storage-separation.md"
    };

    [Fact]
    public void TheTextAccount_IsNamedOnlyAsASettingValue() =>
        Assert.Empty(Hits("consulttextcaeast", TextAccountAllowed));

    private static IReadOnlyList<string> Hits(string needle, string[] allowed)
    {
        var root = RepoRoot();
        var hits = new List<string>();
        foreach (var start in Roots)
        {
            var path = Path.Combine(root, start);
            var files = File.Exists(path) ? new[] { path }
                : Directory.Exists(path) ? Directory.GetFiles(path, "*", SearchOption.AllDirectories) : Array.Empty<string>();
            foreach (var file in files)
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (relative.Contains("/bin/") || relative.Contains("/obj/") || allowed.Contains(relative))
                {
                    continue;
                }

                if (File.ReadAllText(file).Contains(needle, StringComparison.Ordinal))
                {
                    hits.Add(relative);
                }
            }
        }

        return hits;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }
}
