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

    [Theory]
    [InlineData("A11-015", "clinician")]
    [InlineData("A11-016", "operator")]
    [InlineData("A11-017", "operator")]
    public void TheBulkPublishsSentences_AreForTheOwnerAndTheOperator(string id, string audience)
    {
        // #1508 (Task 36): the deadline is told to the owner; a package no pin names is the operator's to see.
        Assert.Equal(audience, V31Contract.AreaSentenceAudiences[id]);
        Assert.Equal(audience, V31Contract.RefusalSentencesAudience(id));
    }

    [Fact]
    public void A12006_IsTheClinicians_InTheContractAndInRefusalSentences()
    {
        // #1509 (Task 9): the Create page says it of an input it has no field for.
        Assert.Equal("clinician", V31Contract.AreaSentenceAudiences["A12-006"]);
        Assert.Equal("clinician", V31Contract.RefusalSentencesAudience("A12-006"));
    }
}
