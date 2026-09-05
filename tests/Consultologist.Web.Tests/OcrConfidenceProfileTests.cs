using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #239: the profile card for the OCR confidence gate. On by default — the
/// account opts out, not in — with a minimum percent when on.
/// </summary>
public class OcrConfidenceProfileTests : ClientRenderTestContext
{
    private static AccountIdentity Entra() =>
        new("entra-external-id", "https://login.microsoftonline.com/x", "sub-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private void WithOcr(string? gate, string? min)
    {
        AccountService.GetCurrentAccountAsync().Returns(new AccountMeResponse(
            "user-1", "A Clinician", "clinician@example.com", "Active", Entra(), new[] { Entra() }));
        AccountService.GetSettingAsync(OcrConfidencePreference.GateKey).Returns(gate == null
            ? null
            : new AccountSettingResponse(OcrConfidencePreference.GateKey, gate, "text/plain", DateTimeOffset.UtcNow));
        AccountService.GetSettingAsync(OcrConfidencePreference.MinKey).Returns(min == null
            ? null
            : new AccountSettingResponse(OcrConfidencePreference.MinKey, min, "text/plain", DateTimeOffset.UtcNow));
    }

    private IRenderedComponent<Profile> RenderProfile() => Render<Profile>();

    private static string State(IRenderedComponent<Profile> page) =>
        page.Find(".ocr-confidence-state").TextContent.Trim();

    private static string[] Buttons(IRenderedComponent<Profile> page) =>
        page.FindAll(".ocr-confidence-card fluent-button").Select(b => b.ClassName ?? "").ToArray();

    [Fact]
    public void Unset_IsOnAtTheEightyPercentDefault_WithTurnOffOffered()
    {
        WithOcr(gate: null, min: null);

        var page = RenderProfile();

        Assert.StartsWith("On — a scanned PDF is accepted only when its OCR confidence is at least 80%", State(page));
        Assert.Contains(Buttons(page), c => c.Contains("ocr-confidence-save"));
        Assert.Contains(Buttons(page), c => c.Contains("ocr-confidence-off"));
        Assert.DoesNotContain(Buttons(page), c => c.Contains("ocr-confidence-on"));
    }

    [Fact]
    public void TurnedOff_SaysSo_AndOffersTurnOn()
    {
        WithOcr(gate: "false", min: "80");

        var page = RenderProfile();

        Assert.StartsWith("Off — every readable scan is accepted", State(page));
        Assert.Contains(Buttons(page), c => c.Contains("ocr-confidence-on"));
        Assert.DoesNotContain(Buttons(page), c => c.Contains("ocr-confidence-off"));
    }

    [Fact]
    public void ACustomMinimum_IsShown()
    {
        WithOcr(gate: null, min: "90");

        Assert.StartsWith("On — a scanned PDF is accepted only when its OCR confidence is at least 90%", State(RenderProfile()));
    }

    [Fact]
    public async Task Saving_WritesTheMinimumAndTurnsTheGateOn()
    {
        WithOcr(gate: null, min: null);
        var page = RenderProfile();

        page.Find(".ocr-confidence-input").Change("90");
        await page.Find(".ocr-confidence-save").ClickAsync(new());

        await AccountService.Received(1).SaveSettingAsync(OcrConfidencePreference.MinKey, "90", "text/plain");
        await AccountService.Received(1).SaveSettingAsync(OcrConfidencePreference.GateKey, "true", "text/plain");
        Assert.Contains("below 90%", page.Find(".ocr-confidence-message").TextContent);
    }

    [Fact]
    public async Task TurningOff_WritesGateFalse()
    {
        WithOcr(gate: null, min: null);
        var page = RenderProfile();

        await page.Find(".ocr-confidence-off").ClickAsync(new());

        await AccountService.Received(1).SaveSettingAsync(OcrConfidencePreference.GateKey, "false", "text/plain");
        Assert.StartsWith("Off — ", State(page));
    }

    [Fact]
    public async Task TurningOn_WritesGateTrue()
    {
        WithOcr(gate: "false", min: "80");
        var page = RenderProfile();

        await page.Find(".ocr-confidence-on").ClickAsync(new());

        await AccountService.Received(1).SaveSettingAsync(OcrConfidencePreference.GateKey, "true", "text/plain");
        Assert.StartsWith("On — ", State(page));
    }
}
