namespace Consultologist.Api.Tests.V31Source;

/// <summary>
/// #1508 (area 11; R8 item 4): who an area sentence is for. A08-006, the engine failing to read a template, is the
/// operator's (it is logged, never shown to an author); A11-008, its run-time sibling, is the clinician's, who is told to tell
/// the operator. The build's table and the published one say the same.
/// </summary>
public class AreaSentenceAudienceTests
{
    [Fact]
    public void A08006_IsTheOperators_InTheContractAndInRefusalSentences()
    {
        Assert.Equal("operator", V31Contract.AreaSentenceAudiences["A08-006"]);
        Assert.Equal("operator", V31Contract.RefusalSentencesAudience("A08-006"));
    }

    [Fact]
    public void A11008_IsTheClinicians_InTheContractAndInRefusalSentences()
    {
        Assert.Equal("clinician", V31Contract.AreaSentenceAudiences["A11-008"]);
        Assert.Equal("clinician", V31Contract.RefusalSentencesAudience("A11-008"));
    }
}
