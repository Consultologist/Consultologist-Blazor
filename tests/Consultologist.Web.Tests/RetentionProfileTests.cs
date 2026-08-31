using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #548: the profile card for the retention clocks. Empty until chosen, each
/// unset line says what unset means, and a bad pair is refused by name before
/// anything is written.
/// </summary>
public class RetentionProfileTests : ClientRenderTestContext
{
    private static AccountIdentity Entra() =>
        new("entra-external-id", "https://login.microsoftonline.com/x", "sub-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private void WithStored(string? outputDays, string? inputDays)
    {
        AccountService.GetCurrentAccountAsync().Returns(new AccountMeResponse(
            "user-1", "A Clinician", "clinician@example.com", "Active", Entra(), new[] { Entra() }));
        AccountService.GetSettingAsync(RetentionPreference.OutputDaysKey).Returns(outputDays == null
            ? null
            : new AccountSettingResponse(RetentionPreference.OutputDaysKey, outputDays, "text/plain", DateTimeOffset.UtcNow));
        AccountService.GetSettingAsync(RetentionPreference.InputDaysKey).Returns(inputDays == null
            ? null
            : new AccountSettingResponse(RetentionPreference.InputDaysKey, inputDays, "text/plain", DateTimeOffset.UtcNow));
    }

    private IRenderedComponent<Profile> RenderProfile() => Render<Profile>();

    private static string OutputState(IRenderedComponent<Profile> page) => page.Find(".retention-output-state").TextContent.Trim();
    private static string InputState(IRenderedComponent<Profile> page) => page.Find(".retention-input-state").TextContent.Trim();

    private static async Task SaveAsync(IRenderedComponent<Profile> page, string? outputs, string? inputs)
    {
        if (outputs != null)
        {
            page.Find(".retention-output-input").Change(outputs);
        }

        if (inputs != null)
        {
            page.Find(".retention-input-input").Change(inputs);
        }

        await page.Find(".retention-save").ClickAsync(new());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("7", 7)]
    [InlineData(" 30 ", 30)]
    [InlineData("seven", null)]
    [InlineData("7.5", null)]
    [InlineData("-3", null)]
    public void Parse_ReadsPlainWholeNumbersOnly(string? stored, int? expected) =>
        Assert.Equal(expected, RetentionPreference.Parse(stored));

    [Fact]
    public void NotChosen_SaysTheDefault_ForBothClocks_WithNoClearOffered()
    {
        WithStored(null, null);

        var page = RenderProfile();

        Assert.Equal("Not chosen — kept 7 days, as today", OutputState(page));
        Assert.Equal("Not chosen — kept 7 days, as today", InputState(page));
        Assert.Empty(page.FindAll(".retention-output-clear"));
        Assert.Empty(page.FindAll(".retention-input-clear"));
        // The card says when a change takes effect.
        Assert.Contains("at the next nightly sweep", page.Find(".retention-card").TextContent);
    }

    [Fact]
    public void ChosenClocks_AreShown_WithTheirClears()
    {
        WithStored("10", "3");

        var page = RenderProfile();

        Assert.Equal("Kept 10 days after completion", OutputState(page));
        Assert.Equal("Kept 3 days after completion", InputState(page));
        Assert.Single(page.FindAll(".retention-output-clear"));
        Assert.Single(page.FindAll(".retention-input-clear"));
    }

    [Fact]
    public void OneDay_ReadsSingular()
    {
        WithStored(null, "1");

        Assert.Equal("Kept 1 day after completion", InputState(RenderProfile()));
    }

    [Fact]
    public async Task SavingBoth_WritesBoth_AsPlainText_AndSaysWhenItApplies()
    {
        WithStored(null, null);
        var page = RenderProfile();

        await SaveAsync(page, "10", "3");

        await AccountService.Received(1).SaveSettingAsync(RetentionPreference.OutputDaysKey, "10", "text/plain");
        await AccountService.Received(1).SaveSettingAsync(RetentionPreference.InputDaysKey, "3", "text/plain");
        Assert.Equal("Kept 10 days after completion", OutputState(page));
        Assert.Equal("Kept 3 days after completion", InputState(page));
        Assert.Contains("next nightly sweep", page.Find(".retention-message").TextContent);
    }

    [Fact]
    public async Task LoweringBoth_WritesInputsFirst_SoTheServerNeverSeesABadPair()
    {
        WithStored("5", "5");
        var page = RenderProfile();

        await SaveAsync(page, "3", "2");

        Received.InOrder(() =>
        {
            AccountService.SaveSettingAsync(RetentionPreference.InputDaysKey, "2", "text/plain");
            AccountService.SaveSettingAsync(RetentionPreference.OutputDaysKey, "3", "text/plain");
        });
    }

    [Fact]
    public async Task RaisingInputsPastTheStoredOutputs_WritesOutputsFirst()
    {
        WithStored("5", "2");
        var page = RenderProfile();

        await SaveAsync(page, "12", "10");

        Received.InOrder(() =>
        {
            AccountService.SaveSettingAsync(RetentionPreference.OutputDaysKey, "12", "text/plain");
            AccountService.SaveSettingAsync(RetentionPreference.InputDaysKey, "10", "text/plain");
        });
    }

    [Theory]
    [InlineData("0", null, "Outputs must be a whole number of days between 1 and 30.")]
    [InlineData("31", null, "Outputs must be a whole number of days between 1 and 30.")]
    [InlineData("ten", null, "Outputs must be a whole number of days between 1 and 30.")]
    [InlineData(null, "0", "Inputs must be a whole number of days between 1 and 30.")]
    [InlineData(null, "31", "Inputs must be a whole number of days between 1 and 30.")]
    [InlineData("3", "5", "Inputs (5) cannot be kept longer than outputs (3).")]
    public async Task ABadSave_IsRefusedByName_AndNothingIsWritten(string? outputs, string? inputs, string expected)
    {
        WithStored(null, null);
        var page = RenderProfile();

        await SaveAsync(page, outputs, inputs);

        Assert.Contains(expected, page.Find(".retention-message").TextContent);
        await AccountService.DidNotReceiveWithAnyArgs().SaveSettingAsync(default!, default!, default!);
    }

    [Fact]
    public async Task ShorteningInputsAlone_IsCheckedAgainstTheStoredOutputs()
    {
        WithStored("3", null);
        var page = RenderProfile();

        await SaveAsync(page, null, "5");

        Assert.Contains("Inputs (5) cannot be kept longer than outputs (3).", page.Find(".retention-message").TextContent);
        await AccountService.DidNotReceiveWithAnyArgs().SaveSettingAsync(default!, default!, default!);
    }

    [Fact]
    public async Task ClearingOneClock_DeletesItsSettingAlone()
    {
        WithStored("10", "3");
        var page = RenderProfile();

        await page.Find(".retention-input-clear").ClickAsync(new());

        await AccountService.Received(1).DeleteSettingAsync(RetentionPreference.InputDaysKey);
        await AccountService.DidNotReceive().DeleteSettingAsync(RetentionPreference.OutputDaysKey);
        Assert.Equal("Not chosen — kept 7 days, as today", InputState(page));
        Assert.Equal("Kept 10 days after completion", OutputState(page));
    }
}
