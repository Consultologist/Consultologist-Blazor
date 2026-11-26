using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Templates;

/// <summary>
/// #1508 (comment 6, item 1; R13; part 05 § 8.2): a <c>judgement</c> node answers one yes-or-no question, so its prompt asks
/// one. A prompt whose text outside its tags (its parts' text with it) is white space only asks nothing: A11-013. The
/// migration writes a classifier so, its question line empty for the operator. Content is fictional (Margaret Okafor).
/// </summary>
public class ClassifierQuestionTests
{
    private const string Judgement = "\"judgement\"";

    private const string Reads = "{{ input.consult_draft.content }}\n\n{{ node.plan.content }}\n\n";

    [Fact]
    public void AJudgementWithOnlyReads_AsksNothing()
    {
        var refusal = Assert.Single(TemplateFixture.RefusalsOf(TemplateFixture.Package().WithAgent("applies", Reads, Judgement)));

        Assert.Equal(("A11-013", "nodes/applies.md"), (refusal.Id, refusal.Path));
        Assert.Equal("Node 'applies' asks nothing: its prompt has no question.", refusal.Sentence);
        V31Contract.AssertSentence(refusal);
    }

    [Fact]
    public void AJudgementThatAsks_IsAccepted()
    {
        Assert.Empty(TemplateFixture.RefusalsOf(TemplateFixture.Package()
            .WithAgent("applies", Reads + "Does the plan prescribe letrozole for Margaret Okafor?\n", Judgement)));

        // The question may stand in a part's text.
        var inAPart = TemplateFixture.Package().WithAgent("applies", Reads + "{{ part.question }}\n", Judgement)
            .WithJson("nodes/applies.md", h => h["parts"] = System.Text.Json.Nodes.JsonNode.Parse(
                """{ "question": { "if": null, "text": ["Does the plan prescribe letrozole?"] } }"""));
        Assert.Empty(TemplateFixture.RefusalsOf(inAPart));
    }

    [Fact]
    public void AQuestionInAPlacedSnippet_IsAsked()
    {
        // The snippet's text is sent where it is placed, so a question there is asked.
        Assert.Empty(TemplateFixture.RefusalsOf(TemplateFixture.Package()
            .WithSnippet("question", "Does the plan prescribe letrozole for Margaret Okafor?\n")
            .WithAgent("applies", Reads + "{{ snippet.question.content }}\n", Judgement)));

        // A snippet that only reads asks nothing either.
        var refusal = Assert.Single(TemplateFixture.RefusalsOf(TemplateFixture.Package()
            .WithSnippet("question", "{{ input.consult_draft.content }}\n")
            .WithAgent("applies", Reads + "{{ snippet.question.content }}\n", Judgement)));
        Assert.Equal("A11-013", refusal.Id);
    }

    [Fact]
    public void AnotherFormat_IsNotHeldToIt()
    {
        // Only a judgement asks a question; a text agent that only reads is not this rule.
        Assert.DoesNotContain(TemplateFixture.RefusalsOf(TemplateFixture.Package().WithAgent("echo", Reads)), r => r.Id == "A11-013");
    }
}
