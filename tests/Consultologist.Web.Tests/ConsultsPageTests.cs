using Bunit;

using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using Consultologist.Web.Services.Workflow;

using NSubstitute;

using Xunit;

namespace Consultologist.Web.Tests;

/// <summary>
/// #766: Consults is the signed-in landing (`/` redirects here), so it now
/// carries the account-status nudge that used to live only on the Home page —
/// a not-yet-active or disabled account meets an activation path here rather
/// than a consult form it cannot run.
/// </summary>
public class ConsultsPageTests : ClientRenderTestContext
{
    private static readonly IReadOnlyList<WorkflowPackageBlockResponse> Sections =
        new[] { Block("section-instructions:hpi", "hpi") };

    private static AccountMeResponse Me(string status) => new(
        "user-1", "A Clinician", "clinician@example.com", status,
        new AccountIdentity("entra-external-id", "https://login.microsoftonline.com/x", "sub-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        new[] { new AccountIdentity("entra-external-id", "https://login.microsoftonline.com/x", "sub-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow) });

    [Fact]
    public void PendingAccount_ShowsActivateNudge_NotTheConsultForm()
    {
        AccountService.GetCurrentAccountAsync().Returns(Me("Pending"));
        WithPinnedPackage(blocks: Sections);

        var page = Render<Consults>();

        Assert.Contains("Activate your account", page.Markup);
        Assert.Contains("href=\"profile\"", page.Markup);
        Assert.DoesNotContain("Consult details", page.Markup);
    }

    [Fact]
    public void DisabledAccount_ShowsTheDisabledPanel_NotTheConsultForm()
    {
        AccountService.GetCurrentAccountAsync().Returns(Me("Disabled"));
        WithPinnedPackage(blocks: Sections);

        var page = Render<Consults>();

        Assert.Contains("Account disabled", page.Markup);
        Assert.DoesNotContain("Consult details", page.Markup);
    }

    [Fact]
    public void ActiveAccount_ShowsTheConsultForm_NotTheActivationNudge()
    {
        AccountService.GetCurrentAccountAsync().Returns(Me("Active"));
        WithPinnedPackage(blocks: Sections);

        var page = Render<Consults>();

        Assert.Contains("Consult details", page.Markup);
        Assert.DoesNotContain("Activate your account", page.Markup);
    }
}
