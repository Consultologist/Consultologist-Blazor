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

    /// <summary>
    /// A loop over the snippet folder <c>standards</c> whose items are <paramref name="hpi"/> and <paramref name="pmh"/>, its
    /// agent <c>section</c> a judgement whose prompt is <paramref name="reader"/>.
    /// </summary>
    private static Dictionary<string, byte[]> Checklist(string hpi, string pmh, string reader = "{{ item.content }}\n") =>
        FolderItemFixture.Folder(hpi, pmh)
            .Listed("nodes/index.json", "standards")
            .With("nodes/standards/index.json", Json(Node("for", "Loop standards", ArrayOf(Judgement), false, true,
                "\"in\": \"snippet.standards\", \"parallel\": true, \"maxParallel\": null, \"until\": null, \"max\": null, "
                + "\"itemLabel\": \"title\", \"nodes\": [\"section\"], \"ret\": \"node.standards.section\"")))
            .With("nodes/standards/section.md", Agent("Asking in standards", Judgement, reader));

    [Fact]
    public void AQuestionInAFolderItemsBody_IsAsked()
    {
        // Final review of Part F, finding 3: a judgement in a loop over a snippet folder whose prompt is {{ item.content }}
        // asks each item's body, package text rendered where it is read (part 05 § 6.2): a checklist, one question an item.
        Assert.Empty(TemplateFixture.RefusalsOf(Checklist(
            "Does the draft describe the history of the present illness for Margaret Okafor?\n",
            "Does the draft list her past conditions?\n")));

        // The near miss: a folder whose items are all empty or white space asks nothing.
        var refusal = Assert.Single(TemplateFixture.RefusalsOf(Checklist("", " \n\t\n")));
        Assert.Equal(("A11-013", "nodes/standards/section.md"), (refusal.Id, refusal.Path));
        V31Contract.AssertSentence(refusal);

        // And a question only an input gives at run time is no question the package asks.
        var fromAnInput = Assert.Single(TemplateFixture.RefusalsOf(Checklist(
            "{{ input.consult_draft.content }}\n", "{{ input.consult_draft.content }}\n")));
        Assert.Equal("A11-013", fromAnInput.Id);
    }

    [Fact]
    public void AnotherFormat_IsNotHeldToIt()
    {
        // Only a judgement asks a question; a text agent that only reads is not this rule.
        Assert.DoesNotContain(TemplateFixture.RefusalsOf(TemplateFixture.Package().WithAgent("echo", Reads)), r => r.Id == "A11-013");
    }
}
