using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Consultologist.Web.Tests;

/// <summary>
/// #788: the editor moved to /editor; the old /templates route redirects there
/// so existing bookmarks keep working.
/// </summary>
public class RedirectToEditorTests : BunitContext
{
    [Fact]
    public void TheOldTemplatesRoute_RedirectsToEditor()
    {
        Render<Consultologist.Web.Pages.RedirectToEditor>();

        Assert.EndsWith("/editor", Services.GetRequiredService<NavigationManager>().Uri);
    }
}
