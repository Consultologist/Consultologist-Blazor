using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Templates;
using static Consultologist.Api.Tests.V31Templates.TemplateFixture;

namespace Consultologist.Api.Tests.V31Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; D1, D18; part 05 § 3.2; part 07 § 2.5): the dialect as the sixth step of
/// <see cref="SourceCheck.Run"/>, and <see cref="PackageTemplates"/> as the real <see cref="ITemplateReads"/>: what a body
/// reads is its own reads and those of what it places (a part's text, a snippet's body, a macro's instruction and body),
/// a macro reading the profile makes its placing node profile-carrying, each written head is mapped to its rooted key,
/// and the dialect runs after the expressions and before the derived step. Content is fictional (Margaret Okafor,
/// Dr A. Sample).
/// </summary>
public class PackageTemplatesTests
{
    private const string Letrozole = "letrozole_prescription_text";

    /// <summary>
    /// Part 05 § 8.4's letrozole example, with a read in the macro's instruction (the draft), a snippet placed in its
    /// body (<c>monitoring</c>), and a read in the snippet (the classifier's reason).
    /// </summary>
    private static Dictionary<string, byte[]> LetrozoleFiles(string body = "Advised to monitor side effects.\n{{ snippet.monitoring.content }}\n") =>
        Package()
            .WithAgent("prescribes_letrozole", "Does the plan for Margaret Okafor prescribe letrozole?\n\n{{ node.plan.content }}\n", "\"judgement\"")
            .WithSnippet("monitoring", "Agreed because: {{ node.prescribes_letrozole.value.reason }}\n")
            .WithMacroOf(Letrozole,
                "Discussed side effects including hot flashes, joint pain and fatigue.\n" + body,
                ["Place the following marker, alone on its own line, in the assessment and plan,",
                 "after the medication discussion of {{ input.consult_draft.content }}:",
                 $"{{{{~ macro.{Letrozole}.content }}}}"],
                condition: "{ \"===\": [ { \"var\": \"node.prescribes_letrozole.value.answer\" }, true ] }")
            .WithAgent("assessment", "Write the assessment and plan for Dr A. Sample.\n\n{{ node.plan.content }}\n{{~ macro." + Letrozole + " }}\n", "\"markdown_block\"");

    private static SourceCheckResult Run(Dictionary<string, byte[]> files) => SourceCheck.Run(files, ResolveFixture.Published());

    private static SourceCheckResult Accepted(Dictionary<string, byte[]> files)
    {
        var check = Run(files);
        Assert.True(check.Refusals.Count == 0, ResolveFixture.Said(check));
        Assert.NotNull(check.Templates);
        return check;
    }

    private static SourceLeaf Leaf(SourceCheckResult check, string key) => (SourceLeaf)ResolveFixture.Node(check.Parse.Package!, key);

    [Fact]
    public void TheSeam_SaysWhatABodyReads()
    {
        var check = Accepted(LetrozoleFiles());
        var reads = check.Templates!.Of(Leaf(check, "node.assessment"), null);

        Assert.NotNull(reads);
        Assert.Equal(
            ["node.plan.content", "input.consult_draft.content", "snippet.monitoring.content", "node.prescribes_letrozole.value.reason"],
            reads!.Reads);
        Assert.Equal([$"macro.{Letrozole}"], reads.Places);
        Assert.Empty(reads.Renders);
        Assert.Empty(reads.RunSpots);
        Assert.False(reads.PlacesProfileMacro);

        // The derived step was told the same: the node reads what its macro's body and instruction read.
        var entry = check.Derived!.Entries["node.assessment"];
        Assert.Equal(
            ["input.consult_draft.content", "node.plan.content", "node.prescribes_letrozole.value", "snippet.monitoring.content"],
            entry.Reads);
        Assert.True(check.Derived.Complete);

        // A body that places nothing reads only its own reads.
        Assert.Equal(["input.consult_draft.content"], check.Templates.Of(Leaf(check, "node.plan"), null)!.Reads);
        Assert.Empty(check.Templates.Units(Leaf(check, "node.plan"), null));
        Assert.Equal([$"macro.{Letrozole}", "snippet.monitoring"],
            check.Templates.Units(Leaf(check, "node.assessment"), null).Select(u => u.Site).Concat(
                check.Templates.Units(Leaf(check, "node.assessment"), null).Single().MacroBody!.Units.Select(u => u.Site)));
    }

    [Fact]
    public void AProfileReadingMacro_MakesItsNodeProfileCarrying()
    {
        static Dictionary<string, byte[]> Reading() => LetrozoleFiles("Signed: {{ profile.signature }}\n");

        var declared = Accepted(Reading().WithJson("nodes/assessment.md", h => h["profileCarrying"] = true));
        Assert.True(declared.Templates!.Of(Leaf(declared, "node.assessment"), null)!.PlacesProfileMacro);
        var entry = declared.Derived!.Entries["node.assessment"];
        Assert.True(entry.ProfileCarrying);
        Assert.Contains("profile.signature", entry.Reads);
        Assert.Equal(["signature"], declared.Derived.ProfileFields);

        var undeclared = Run(Reading());
        var refusal = Assert.Single(undeclared.Refusals);
        Assert.Equal("S03-036", refusal.Id);
        Assert.Equal("nodes/assessment.md", refusal.Path);
        V31Contract.AssertSentence(refusal);
    }

    [Fact]
    public void Names_MapLocalHeadsToRootedKeys()
    {
        // judge reads node.draft_summary only through the snippet it places; the deliverable reads a rooted key.
        var files = ResolveFixture.NestedNote()
            .WithSnippet("draft_line", "The summary: {{ node.draft_summary.content }}\n")
            .With("nodes/refine/judge.md", SourceFixture.Agent("Judging the summary", "\"verdict\"",
                "Say whether the summary is good or needs another pass.\n{{~ snippet.draft_line.content }}\n"));
        var check = Accepted(files);

        Assert.Equal(
            new Dictionary<string, string> { ["node.draft_summary"] = "node.refine.draft_summary" },
            check.Templates!.Names(Leaf(check, "node.refine.judge"), null));
        Assert.Empty(check.Templates.Names(Leaf(check, "node.plan"), null));
        var deliverable = check.Parse.Package!.Deliverables.Deliverables.Single();
        Assert.Empty(check.Templates.Names(deliverable, null));
    }

    [Fact]
    public void SourceCheck_RunsTheDialectBetweenExpressionsAndDerived()
    {
        const string include = "{{ include 'closing' }}\n";
        static Dictionary<string, byte[]> Template() => Package().WithAgent("draft", "Draft the note.\n" + include);
        var expression = Template().WithJson("nodes/draft.md", h => h["if"] = JsonNode.Parse("{ \"no_such_operation\": [1] }"));
        var derived = Template().WithJson("nodes/draft.md", h => h["modelDerived"] = false);

        // An expression and a template fault: the expressions refuse first, and the dialect is not run.
        var first = Run(expression);
        Assert.NotEmpty(first.Refusals);
        Assert.All(first.Refusals, r => Assert.False(r.Id.StartsWith("S05-", StringComparison.Ordinal) || r.Id.StartsWith("A07-", StringComparison.Ordinal), r.Id));
        Assert.Null(first.Templates);

        // A template and a derived fault: the dialect refuses first, and the derived step is not run.
        var second = Run(derived);
        Assert.NotEmpty(second.Refusals);
        Assert.All(second.Refusals, r => Assert.True(r.Id.StartsWith("S05-", StringComparison.Ordinal) || r.Id.StartsWith("A07-", StringComparison.Ordinal), r.Id));
        Assert.Null(second.Templates);
        Assert.Null(second.Derived);
        Assert.NotNull(second.Expressions);

        // The derived fault alone: the dialect accepted, its templates stay set.
        var third = Run(Package().WithAgent("draft", "Draft the note.\n").WithJson("nodes/draft.md", h => h["modelDerived"] = false));
        Assert.Equal("S03-036", Assert.Single(third.Refusals).Id);
        Assert.NotNull(third.Templates);

        // Accepted: the derived step read what the dialect says, unless a caller passes its own seam.
        var accepted = Accepted(Package());
        var plan = Leaf(accepted, "node.plan");
        Assert.Equal(accepted.Templates!.Of(plan, null), accepted.Derived!.Entries["node.plan"].Body);
        var stubbed = SourceCheck.Run(Package(), ResolveFixture.Published(), StubTemplates.Empty());
        Assert.Equal(TemplateReads.None, stubbed.Derived!.Entries["node.plan"].Body);
        Assert.NotNull(stubbed.Templates);
    }

    [Fact]
    public void AClassifierReadOnlyByAPlacedMacrosIf_IsRead()
    {
        // In a while, the classifier is read by nothing but the if of the macro the draft places (S04-035 otherwise).
        var files = Package()
            .WithMacroOf("follow_up", "Follow up with Dr A. Sample in six weeks.\n", ["Close with {{ macro.follow_up.content }}"],
                condition: "{ \"===\": [ { \"var\": \"node.classify.value.answer\" }, true ] }")
            .Listed("nodes/index.json", "refine")
            .With("nodes/refine/index.json", SourceFixture.Json(SourceFixture.Node("while", "Refining", Plain, false, true,
                "\"while\": { \"<\": [{ \"var\": \"pass.number\" }, 2] }, \"max\": 3, \"until\": null, \"checkFirst\": false, "
                + "\"nodes\": [\"classify\", \"draft\"], \"ret\": \"node.refine.draft\"")))
            .With("nodes/refine/classify.md", SourceFixture.Agent("Classifying", "\"judgement\"", "Does the plan need a follow-up?\n\n{{ node.plan.content }}\n"))
            .With("nodes/refine/draft.md", SourceFixture.Agent("Drafting", Plain, "Draft the note.\n{{~ macro.follow_up }}\n"));

        Accepted(files);
    }
}
