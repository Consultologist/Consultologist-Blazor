using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #1063: the profile card for the speech-to-text confidence gate — the OCR
/// card's twin: on by default, a minimum percent when on.
/// </summary>
public class SttConfidenceProfileTests : ClientRenderTestContext
{
    private static AccountIdentity Entra() =>
        new("entra-external-id", "https://login.microsoftonline.com/x", "sub-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private void WithStt(string? gate, string? min)
    {
        AccountService.GetCurrentAccountAsync().Returns(new AccountMeResponse(
            "user-1", "A Clinician", "clinician@example.com", "Active", Entra(), new[] { Entra() }));
        AccountService.GetSettingAsync(SttConfidencePreference.GateKey).Returns(gate == null
            ? null
            : new AccountSettingResponse(SttConfidencePreference.GateKey, gate, "text/plain", DateTimeOffset.UtcNow));
        AccountService.GetSettingAsync(SttConfidencePreference.MinKey).Returns(min == null
            ? null
            : new AccountSettingResponse(SttConfidencePreference.MinKey, min, "text/plain", DateTimeOffset.UtcNow));
    }

    private IRenderedComponent<Profile> RenderProfile() => Render<Profile>();

    private static string State(IRenderedComponent<Profile> page) =>
        page.Find(".stt-confidence-state").TextContent.Trim();

    private static string[] Buttons(IRenderedComponent<Profile> page) =>
        page.FindAll(".stt-confidence-card fluent-button").Select(b => b.ClassName ?? "").ToArray();

    [Fact]
    public void Unset_IsOnAtTheEightyPercentDefault_WithTurnOffOffered()
    {
        WithStt(gate: null, min: null);

        var page = RenderProfile();

        Assert.StartsWith("On — a recording is accepted only when its transcription confidence is at least 80%", State(page));
        Assert.Contains(Buttons(page), c => c.Contains("stt-confidence-save"));
        Assert.Contains(Buttons(page), c => c.Contains("stt-confidence-off"));
        Assert.DoesNotContain(Buttons(page), c => c.Contains("stt-confidence-on"));
    }

    [Fact]
    public void TurnedOff_SaysSo_AndOffersTurnOn()
    {
        WithStt(gate: "false", min: "80");

        var page = RenderProfile();

        Assert.StartsWith("Off — every transcribed recording is accepted", State(page));
        Assert.Contains(Buttons(page), c => c.Contains("stt-confidence-on"));
        Assert.DoesNotContain(Buttons(page), c => c.Contains("stt-confidence-off"));
    }

    [Fact]
    public void ACustomMinimum_IsShown()
    {
        WithStt(gate: null, min: "90");

        Assert.StartsWith("On — a recording is accepted only when its transcription confidence is at least 90%", State(RenderProfile()));
    }

    [Fact]
    public async Task Saving_WritesTheMinimumAndTurnsTheGateOn()
    {
        WithStt(gate: null, min: null);
        var page = RenderProfile();

        page.Find(".stt-confidence-input").Change("90");
        await page.Find(".stt-confidence-save").ClickAsync(new());

        await AccountService.Received(1).SaveSettingAsync(SttConfidencePreference.MinKey, "90", "text/plain");
        await AccountService.Received(1).SaveSettingAsync(SttConfidencePreference.GateKey, "true", "text/plain");
        Assert.Contains("below 90%", page.Find(".stt-confidence-message").TextContent);
    }

    [Fact]
    public async Task TurningOff_WritesGateFalse()
    {
        WithStt(gate: null, min: null);
        var page = RenderProfile();

        await page.Find(".stt-confidence-off").ClickAsync(new());

        await AccountService.Received(1).SaveSettingAsync(SttConfidencePreference.GateKey, "false", "text/plain");
        Assert.StartsWith("Off — ", State(page));
    }

    [Fact]
    public async Task TurningOn_WritesGateTrue()
    {
        WithStt(gate: "false", min: "80");
        var page = RenderProfile();

        await page.Find(".stt-confidence-on").ClickAsync(new());

        await AccountService.Received(1).SaveSettingAsync(SttConfidencePreference.GateKey, "true", "text/plain");
        Assert.StartsWith("On — ", State(page));
    }
}
