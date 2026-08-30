using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #516: the profile's signature card. Explicit initialisation — a new
/// account has none, the unchosen states name their consequence, and every
/// mutation is one write of the one profile.signatures row.
/// </summary>
public class SignatureProfileTests : ClientRenderTestContext
{
    /// <summary>
    /// The wire format, pinned verbatim. The Api reads this same JSON with
    /// its own SignatureBlocks class (tests/SignatureBlocksTests.cs pins the
    /// identical literal): a casing drift on either side would silently read
    /// "none chosen", which tolerant parsing would never surface.
    /// </summary>
    public const string Wire =
        """{"Blocks":[{"Id":"clinic-letters","Name":"Clinic letters","Text":"Taylor Reyes, MD\nDept. of Medicine","UpdatedAtUtc":"2026-08-30T12:00:00+00:00"}],"ChosenId":"clinic-letters"}""";

    private static AccountIdentity Entra() =>
        new("entra-external-id", "https://login.microsoftonline.com/x", "sub-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private void WithSignatures(string? stored)
    {
        AccountService.GetCurrentAccountAsync().Returns(new AccountMeResponse(
            "user-1", "A Clinician", "clinician@example.com", "Active", Entra(), new[] { Entra() }));
        AccountService.GetSettingAsync(SignatureBlocks.SettingKey).Returns(stored == null
            ? null
            : new AccountSettingResponse(SignatureBlocks.SettingKey, stored, SignatureBlocks.ContentType, DateTimeOffset.UtcNow));
    }

    private IRenderedComponent<Profile> RenderProfile() => Render<Profile>();

    private static string State(IRenderedComponent<Profile> page) => page.Find(".signature-state").TextContent.Trim();

    private static string[] Buttons(IRenderedComponent<Profile> page) =>
        page.FindAll(".signature-card fluent-button").Select(b => b.ClassName ?? "").ToArray();

    private static SignatureBlocks.SignatureBlockSet TwoBlocks(string? chosenId) => new(
        new List<SignatureBlocks.SignatureBlock>
        {
            new("clinic-letters", "Clinic letters", "Taylor Reyes, MD", new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero)),
            new("brief", "Brief", "T. Reyes", new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero))
        },
        chosenId);

    // --- the pure rules ---

    [Fact]
    public void TheWireFormat_IsPinned_BothWays()
    {
        var set = SignatureBlocks.Parse(Wire);

        var chosen = SignatureBlocks.Chosen(set);
        Assert.Equal("clinic-letters", chosen!.Id);
        Assert.Equal("Taylor Reyes, MD\nDept. of Medicine", chosen.Text);
        Assert.Equal(new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero), chosen.UpdatedAtUtc);
        Assert.Equal(Wire, SignatureBlocks.Serialize(set));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{\"Blocks\":null}")]
    public void AnUnreadableRow_IsAnEmptySet_NeverAnError(string? stored)
    {
        var set = SignatureBlocks.Parse(stored);

        Assert.Empty(set.Blocks);
        Assert.Null(SignatureBlocks.Chosen(set));
    }

    [Fact]
    public void ADanglingChosenId_ChoosesNobody()
    {
        Assert.Null(SignatureBlocks.Chosen(TwoBlocks("ghost")));
    }

    [Theory]
    [InlineData("Clinic letters", "clinic-letters")]
    [InlineData("  Dr.  Reyes / MD  ", "dr-reyes-md")]
    [InlineData("!!!", "signature")]
    public void TheSlug_IsLowercaseDashed_NeverEmpty(string name, string expected)
    {
        Assert.Equal(expected, SignatureBlocks.SlugFor(name, Array.Empty<string>()));
    }

    [Fact]
    public void ASlugCollision_IsSuffixed()
    {
        Assert.Equal("brief-2", SignatureBlocks.SlugFor("Brief", new[] { "brief" }));
        Assert.Equal("brief-3", SignatureBlocks.SlugFor("Brief", new[] { "brief", "brief-2" }));
    }

    // --- the card ---

    [Fact]
    public void ANewAccount_HasNone_AndTheLineNamesTheConsequence()
    {
        WithSignatures(null);

        var page = RenderProfile();

        Assert.Equal("No signature blocks — deliverables a package marks signed are produced unsigned", State(page));
        Assert.DoesNotContain(Buttons(page), c => c.Contains("signature-use"));
        Assert.DoesNotContain(Buttons(page), c => c.Contains("signature-clear"));
    }

    [Fact]
    public void BlocksWithoutAChoice_SayNotChosen_AndOfferUse()
    {
        WithSignatures(SignatureBlocks.Serialize(TwoBlocks(null)));

        var page = RenderProfile();

        Assert.Equal("Not chosen — deliverables a package marks signed are produced unsigned", State(page));
        Assert.Equal(2, Buttons(page).Count(c => c.Contains("signature-use")));
        Assert.DoesNotContain(Buttons(page), c => c.Contains("signature-clear"));
    }

    [Fact]
    public void TheChosenBlock_IsNamedInUse_AndOnlyTheOtherOffersUse()
    {
        WithSignatures(Wire);

        var page = RenderProfile();

        Assert.StartsWith("In use: Clinic letters", State(page));
        Assert.DoesNotContain(Buttons(page), c => c.Contains("signature-use"));
        Assert.Contains(Buttons(page), c => c.Contains("signature-clear"));
    }

    [Fact]
    public async Task AddingABlock_SavesTheRow_WithASlugId()
    {
        WithSignatures(null);
        var page = RenderProfile();

        page.Find(".signature-name-input").Change("Clinic letters");
        page.Find(".signature-text-input").Change("Taylor Reyes, MD");
        await page.Find(".signature-save").ClickAsync(new());

        await AccountService.Received(1).SaveSettingAsync(
            SignatureBlocks.SettingKey,
            Arg.Is<string>(json =>
                SignatureBlocks.Parse(json).Blocks.Single().Id == "clinic-letters"
                && SignatureBlocks.Parse(json).Blocks.Single().Text == "Taylor Reyes, MD"
                && SignatureBlocks.Parse(json).ChosenId == null),
            SignatureBlocks.ContentType);
        Assert.StartsWith("Not chosen", State(page));
    }

    [Fact]
    public async Task Use_ChoosesTheBlock_InOneWrite()
    {
        WithSignatures(SignatureBlocks.Serialize(TwoBlocks(null)));
        var page = RenderProfile();

        await page.Find(".signature-use").ClickAsync(new());

        await AccountService.Received(1).SaveSettingAsync(
            SignatureBlocks.SettingKey,
            Arg.Is<string>(json => SignatureBlocks.Parse(json).ChosenId == "clinic-letters"),
            SignatureBlocks.ContentType);
        Assert.StartsWith("In use: Clinic letters", State(page));
    }

    [Fact]
    public async Task BackToNoneChosen_ClearsTheChoice_AndNamesTheConsequence()
    {
        WithSignatures(Wire);
        var page = RenderProfile();

        await page.Find(".signature-clear").ClickAsync(new());

        await AccountService.Received(1).SaveSettingAsync(
            SignatureBlocks.SettingKey,
            Arg.Is<string>(json => SignatureBlocks.Parse(json).ChosenId == null && SignatureBlocks.Parse(json).Blocks.Count == 1),
            SignatureBlocks.ContentType);
        Assert.StartsWith("Not chosen", State(page));
    }

    [Fact]
    public async Task Remove_ArmsFirst_ThenActs()
    {
        WithSignatures(SignatureBlocks.Serialize(TwoBlocks(null)));
        var page = RenderProfile();

        await page.Find(".signature-remove").ClickAsync(new());
        await AccountService.DidNotReceive().SaveSettingAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
        Assert.Contains("Confirm remove", page.Find(".signature-remove").TextContent);

        await page.Find(".signature-remove").ClickAsync(new());
        await AccountService.Received(1).SaveSettingAsync(
            SignatureBlocks.SettingKey,
            Arg.Is<string>(json => SignatureBlocks.Parse(json).Blocks.Single().Id == "brief"),
            SignatureBlocks.ContentType);
    }

    [Fact]
    public async Task RemovingTheChosenBlock_ClearsTheChoice_InTheSameSave()
    {
        WithSignatures(SignatureBlocks.Serialize(TwoBlocks("clinic-letters")));
        var page = RenderProfile();

        var remove = page.FindAll(".signature-remove")[0];
        await remove.ClickAsync(new());
        await page.FindAll(".signature-remove")[0].ClickAsync(new());

        await AccountService.Received(1).SaveSettingAsync(
            SignatureBlocks.SettingKey,
            Arg.Is<string>(json => SignatureBlocks.Parse(json).ChosenId == null && SignatureBlocks.Parse(json).Blocks.Single().Id == "brief"),
            SignatureBlocks.ContentType);
        Assert.StartsWith("Not chosen", State(page));
    }

    [Fact]
    public async Task RemovingTheLastBlock_DeletesTheRow()
    {
        WithSignatures(Wire);
        var page = RenderProfile();

        await page.Find(".signature-remove").ClickAsync(new());
        await page.Find(".signature-remove").ClickAsync(new());

        await AccountService.Received(1).DeleteSettingAsync(SignatureBlocks.SettingKey);
        Assert.StartsWith("No signature blocks", State(page));
    }

    [Fact]
    public async Task Edit_PrefillsTheForm_AndSavesOverTheSameId()
    {
        WithSignatures(Wire);
        var page = RenderProfile();

        await page.Find(".signature-edit").ClickAsync(new());
        page.Find(".signature-text-input").Change("Taylor Reyes, MD, FRCPC");
        await page.Find(".signature-save").ClickAsync(new());

        await AccountService.Received(1).SaveSettingAsync(
            SignatureBlocks.SettingKey,
            Arg.Is<string>(json =>
                SignatureBlocks.Parse(json).Blocks.Single().Id == "clinic-letters"
                && SignatureBlocks.Parse(json).Blocks.Single().Text == "Taylor Reyes, MD, FRCPC"
                && SignatureBlocks.Parse(json).ChosenId == "clinic-letters"),
            SignatureBlocks.ContentType);
    }

    [Fact]
    public void TheSaveButton_IsDisabled_UntilBothFieldsHaveText()
    {
        WithSignatures(null);
        var page = RenderProfile();

        Assert.NotNull(page.Find(".signature-save").GetAttribute("disabled"));

        page.Find(".signature-name-input").Change("Clinic letters");
        page.Find(".signature-text-input").Change("Taylor Reyes, MD");

        Assert.Null(page.Find(".signature-save").GetAttribute("disabled"));
    }
}
