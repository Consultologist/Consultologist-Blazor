using Bunit;
using Xunit;

namespace Consultologist.Web.Tests;

/// <summary>
/// #685: the public Help page explains the consult flow and — the product's
/// thesis — what verifiability means, in plain language, with a glossary.
/// </summary>
public class HelpPageTests : ClientRenderTestContext
{
    [Fact]
    public void Help_ExplainsVerifiability_AndCarriesAGlossary()
    {
        var page = Render<Consultologist.Web.Pages.Help>();

        Assert.NotEmpty(page.FindAll("h1"));
        // What a Verify match proves, in plain language.
        Assert.Contains("byte-for-byte", page.Markup);
        // The glossary and its anchors the in-panel links point at.
        Assert.Contains("Workflow package", page.Markup);
        Assert.NotNull(page.Find("#verify"));
        Assert.NotNull(page.Find("#glossary"));
    }

    [Fact]
    public void Help_StatesTheCopyBackPath_IntoAnyEmr()
    {
        // #188: the copy-back parity, surfaced plainly — the note copies to the
        // clipboard and pastes into any EMR, no integration required.
        var page = Render<Consultologist.Web.Pages.Help>();

        Assert.NotNull(page.Find("#chart"));
        Assert.Contains("Copy note", page.Markup);
        Assert.Contains("any EMR", page.Markup);
        // The head-to-head comparison stays on the marketing site, not in-app.
        Assert.DoesNotContain("cleverconsult", page.Markup, StringComparison.OrdinalIgnoreCase);
    }
}
