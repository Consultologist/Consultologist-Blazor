using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Consultologist.Web.Tests;

/// <summary>
/// #790: the consults area moved to /create; the old /consults routes redirect
/// there (preserving the run id) so existing bookmarks and shared run links keep
/// working.
/// </summary>
public class RedirectToCreateTests : BunitContext
{
    [Fact]
    public void TheOldConsultsRoute_RedirectsToCreate()
    {
        Render<Consultologist.Web.Pages.RedirectToCreate>();

        Assert.EndsWith("/create", Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public void TheOldConsultsRunRoute_RedirectsToCreate_PreservingTheJobId()
    {
        Render<Consultologist.Web.Pages.RedirectToCreate>(parameters =>
            parameters.Add(p => p.JobId, "job-42"));

        Assert.EndsWith("/create/job-42", Services.GetRequiredService<NavigationManager>().Uri);
    }
}
