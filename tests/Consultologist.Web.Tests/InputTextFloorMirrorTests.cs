using ApiFloor = Consultologist.Api.Jobs.InputContent;
using WebFloor = Consultologist.Web.Services.Documents.InputTextFloor;

namespace Consultologist.Web.Tests;

/// <summary>
/// #800: the client warns at attach time when an extracted document carries too
/// little prose to be a referral — the same floor the engine refuses on at Create
/// (#290/#291). Web holds no reference to the Api's internals in production, so
/// the floor is replicated by hand in <see cref="WebFloor"/>; this project
/// references both assemblies, which is what makes the mirror provable (the same
/// arrangement as <c>ConsultInputValueMirrorTests</c>).
///
/// One table, both implementations: the client's meaningful-length must equal
/// the server's on every row. A drift here is a client that warns about a
/// referral the server accepts, or stays silent on one it refuses.
/// </summary>
public class InputTextFloorMirrorTests
{
    public static TheoryData<string> TheProseTable => new()
    {
        "",
        "   \t\n  ",
        // A bare cloud-storage URL — the #290 failing case, reduces to nothing.
        "https://contoso.sharepoint.com/sites/x/Shared%20Documents/referral.docx",
        "See the file: https://1drv.ms/w/abc123  Regards",
        // A genuinely terse but real referral — must clear the floor on both.
        "65M, newly diagnosed adenocarcinoma of the lung, stage IIIA, for consideration of chemoradiation. PMHx HTN.",
        // Around the 40-char boundary once whitespace is stripped.
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLM",   // 39
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMN",  // 40
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNO", // 41
        // Whitespace between tokens must not count.
        "one   two\tthree\nfour five six seven eight nine ten",
        // The autolinked forms the regex also strips.
        "www.example.com/some/very/long/path?q=1 and www.other.org/x",
        "mailto:dr.x@example.com please advise",
        "ftp://files.example.com/scan.pdf",
        // Unicode prose (counted by char, as the server does).
        "Pré-évaluation oncologique pour un patient de 65 ans, adénocarcinome."
    };

    [Theory]
    [MemberData(nameof(TheProseTable))]
    public void TheClientMeaningfulLength_EqualsTheServer(string text)
    {
        Assert.Equal(ApiFloor.MeaningfulLength(text), WebFloor.MeaningfulLength(text));
    }

    [Fact]
    public void TheClientFloor_MatchesTheServerDefault()
    {
        // The server floor is an app setting (Inputs__MinimumRequiredCharacters)
        // defaulting to 40; a WASM client cannot read it and warns on the
        // default. With the setting unset (as under test) the two coincide.
        var webFloor = WebFloor.MinimumCharacters;
        Assert.Equal(40, webFloor);
        Assert.Equal(WebFloor.MinimumCharacters, ApiFloor.MinimumCharacters);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("short", true)]
    [InlineData("https://contoso.sharepoint.com/x/referral.docx", true)]
    [InlineData("65M, adenocarcinoma of the lung stage IIIA, for chemoradiation. PMHx HTN.", false)]
    public void IsBelowFloor_TracksMeaningfulLength(string text, bool below)
    {
        Assert.Equal(below, WebFloor.IsBelowFloor(text));
    }

    [Theory]
    [InlineData("referral.gdoc", true)]
    [InlineData("Referral.GDOC", true)]
    [InlineData("plan.gsheet", true)]
    [InlineData("deck.gslides", true)]
    [InlineData("bookmark.url", true)]
    [InlineData("note.webloc", true)]
    [InlineData("referral.docx", false)]
    [InlineData("scan.pdf", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LooksLikeShortcut_MatchesTheLinkFileKinds(string? fileName, bool expected)
    {
        Assert.Equal(expected, WebFloor.LooksLikeShortcut(fileName));
    }
}
