using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using static Consultologist.Api.Tests.V31Source.SourceFixture;
using static Consultologist.Api.Tests.V31Templates.TemplateFixture;

namespace Consultologist.Api.Tests.V31Templates;

/// <summary>
/// #1508 (ruled by the owner, ruling 12 of 2026-11-25; part 05 § 2.3 strict variables, S05-011): every member read is
/// checked at publish, at every step, ids and fields alike, beyond the first id. For each head a template may read
/// (<c>node</c>, <c>input</c>, <c>check</c>, <c>item</c>, <c>pass</c>, <c>param</c>, <c>run</c>, <c>profile</c>,
/// <c>snippet</c>, <c>macro</c>, <c>part</c>; <c>deliverable</c> is read only by a check's JSON Logic), a read whose last
/// step does not exist and one whose middle step does not exist are refused in a prompt, a deliverable, a snippet, a
/// macro body and a macro's instruction wherever that head is legal there, and none passes as untyped. The same reads
/// that exist publish (the scaffolding refuses nothing of its own). Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class MemberReadTests
{
    /// <summary>The sites of a read; <c>deliverable</c> only at the top.</summary>
    private static readonly string[] Sites = ["prompt", "snippet", "macro body", "instruction", "deliverable"];

    /// <summary>The macro folder <c>notes</c>, holding <c>hpi</c>, beside the package's own macro <c>follow_up</c>.</summary>
    private const string FolderMacro = "macros/notes/hpi.md";

    [Theory]
    [InlineData("macro.notes.hpi.anything")]
    [InlineData("macro.follow_up.content.x")]
    [InlineData("macro.notes.hpi.content.x")]
    [InlineData("part.brief.x")]
    public void AReadPastAMacrosMembers_IsRefused(string read)
    {
        var (files, path) = At("prompt", "top", read);

        var refusals = Publish(files);

        // A part read past is not its placement, so the part is also unplaced (S05-039): the read is S05-011 whatever else is said.
        var refusal = Assert.Single(refusals, r => r.Id == "S05-011");
        V31Contract.AssertSentence(refusal);
        Assert.Equal(("S05-011", path), (refusal.Id, refusal.Path));
        Assert.StartsWith($"Node 'draft' reads {read} at line 2, which ", refusal.Sentence, StringComparison.Ordinal);
    }

    /// <summary>
    /// Each head, where it is read (<c>top</c>, <c>loop</c>, <c>while</c>, <c>function</c>), a read of it that exists, one
    /// whose last step does not and one whose middle step does not, and the sites where that head is legal.
    /// </summary>
    public static TheoryData<string, string, string, string, string> Heads => new()
    {
        { "node", "top", "node.plan.content", "node.plan.value.nope", "node.plan.value.nope.title" },
        { "node", "top", "node.plan.content", "node.plan.nope", "node.nope.content" },
        { "input", "top", "input.consult_draft.content", "input.consult_draft.nope", "input.nope.content" },
        { "check", "top", "check.coverage.value.pass", "check.coverage.value.nope", "check.nope.value.pass" },
        { "item", "loop", "item.value.title", "item.value.nope", "item.nope.title" },
        { "pass", "while", "pass.number", "pass.nope", "pass.node.nope.content" },
        { "param", "function", "param.draft.content", "param.draft.nope", "param.nope.content" },
        { "run", "top", "run.date", "run.date.nope", "run.nope.value" },
        { "profile", "top", "profile.signature", "profile.signature.nope", "profile.nope.content" },
        { "snippet", "top", "snippet.tone.content", "snippet.tone.content.nope", "snippet.tone.nope.content" },
        { "macro", "top", "macro.follow_up", "macro.follow_up.content.x", "macro.follow_up.x.content" },
        { "macro", "top", "macro.notes.hpi", "macro.notes.hpi.content.x", "macro.notes.nope.content" },
        { "part", "top", "part.brief", "part.brief.x", "part.nope.x" },
    };

    [Theory]
    [MemberData(nameof(Heads))]
    public void EveryHead_IsCheckedAtEveryStep(string head, string place, string exists, string last, string middle)
    {
        foreach (var site in LegalSites(head, place))
        {
            // The scaffolding: the read that exists publishes at this site.
            var (valid, _) = At(site, place, exists);
            var said = Publish(valid);
            Assert.True(said.Count == 0, $"{head} {site} {exists}: " + Said(said));

            foreach (var read in new[] { last, middle })
            {
                var (files, _) = At(site, place, read);

                var refusals = Publish(files);

                Assert.True(refusals.Count > 0, $"{head} {site} {read} passed as untyped");
                Assert.All(refusals, V31Contract.AssertSentence);
                // The read is named whole by S05-011 (S05-022 where a snippet's placer is what lacks it); S06-028 names the field.
                Assert.True(refusals.Any(r => r.Id is "S05-011" or "S05-022"
                        ? r.Sentence.Contains($" reads {read} ", StringComparison.Ordinal) || r.Sentence.Contains($" reads {read},", StringComparison.Ordinal)
                        : r.Id == "S06-028" && r.Sentence.Contains($" reads {Named(read)},", StringComparison.Ordinal)),
                    $"{head} {site} {read}: " + Said(refusals));
            }
        }
    }

    /// <summary>The sites where <paramref name="head"/> is legal (part 05 § 3.1), at <paramref name="place"/>.</summary>
    private static IEnumerable<string> LegalSites(string head, string place) => Sites.Where(site => head switch
    {
        "profile" => site is "macro body" or "deliverable",
        "macro" => site is "prompt" or "snippet",
        "part" => site == "prompt",
        "item" or "pass" or "param" => site != "deliverable",
        _ => true,
    } && (place == "top" || site != "deliverable"));

    [Fact]
    public void AStepPastASingleMacro_IsSaidAsItsPlacement()
    {
        // macro.<id>.<x> has a folder macro's placement form, so the placement says it, once: no macro is named so.
        var (files, path) = At("prompt", "top", "macro.follow_up.x");

        var refusal = Assert.Single(Publish(files));

        V31Contract.AssertSentence(refusal);
        Assert.Equal(("S05-011", path, "Node 'draft' reads macro.follow_up.x at line 2, which macro.* doesn't have."),
            (refusal.Id, refusal.Path, refusal.Sentence));
    }

    [Theory]
    [InlineData("Note: {{ macro.follow_up | string.upcase }}", "macro.follow_up")]
    [InlineData("Note: {{ macro.notes.hpi | string.upcase }}", "macro.notes.hpi")]
    [InlineData("Note: {{ macro.follow_up | renderer.concept_bullets }}", "macro.follow_up")]
    [InlineData("Note: {{ part.brief | string.upcase }}", "part.brief")]
    [InlineData("Note: {{ node.plan.value.sections | array.join macro.follow_up }}", "macro.follow_up")]
    [InlineData("Note: {{ macro.follow_up == \"x\" }}", "macro.follow_up")]
    public void AUnitInAnExpression_IsRefused(string line, string unit)
    {
        // #1508 (ruling 12): a part or a macro is placed on its own; in an expression the run reads it as null.
        var (files, path) = At("prompt", "top", "node.plan.content");
        files.With(path, Agent("Agent draft", Plain, "Draft the note.\n" + line + "\n"))
            .WithJson(path, h => h["parts"] = JsonNode.Parse("{ \"brief\": { \"if\": null, \"text\": [\"Be brief.\"] } }"));

        var refusals = Publish(files);

        var refusal = Assert.Single(refusals, r => r.Id == "A11-012");
        V31Contract.AssertSentence(refusal);
        Assert.Equal((path, $"Node 'draft' uses {unit} at line 2 in an expression; a part or a macro is placed on its own, as {{{{ {unit} }}}}, never piped or used in an expression."),
            (refusal.Path, refusal.Sentence));
    }

    [Fact]
    public void AForVariableNamedLikeAUnit_IsReadAsTheVariable()
    {
        // A for variable named part shadows the root: its members are its element's, not a unit's (no S05-006, no A11-012).
        var (files, path) = At("prompt", "top", "node.plan.content");
        files.With(path, Agent("Agent draft", Plain,
            "Draft the note.\n{{ for part in node.plan.value.sections }}{{ for y in part.nope }}{{ y }}{{ end }}{{ part.title | string.upcase }}{{ end }}\n"));

        var refusal = Assert.Single(Publish(files));

        Assert.Equal(("S05-011", path), (refusal.Id, refusal.Path));
        Assert.StartsWith("Node 'draft' reads part.nope at line 2, which ", refusal.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void AMacrosMemberInASnippet_IsSaidOfTheSnippet()
    {
        // Not there whatever the place, so S05-011 of the snippet rather than S05-022 of where it is placed.
        var (files, path) = At("snippet", "top", "macro.notes.hpi.anything");

        var refusal = Assert.Single(Publish(files));

        V31Contract.AssertSentence(refusal);
        Assert.Equal(("S05-011", path, "Snippet 'probe' reads macro.notes.hpi.anything at line 2, which macro.notes.hpi doesn't have."),
            (refusal.Id, refusal.Path, refusal.Sentence));
    }

    [Theory]
    [InlineData("{{ for x in macro.follow_up }}{{ x.anything }}{{ end }}", "macro.follow_up")]
    [InlineData("{{ for x in part.brief }}{{ x.anything }}{{ end }}", "part.brief")]
    public void AForOverAPartOrAMacro_IsRefused(string body, string source)
    {
        var (files, path) = At("prompt", "top", "node.plan.content");
        files.With(path, Agent("Agent draft", Plain, body + "\n{{~ part.brief }}\n"))
            .WithJson(path, h => h["parts"] = JsonNode.Parse("{ \"brief\": { \"if\": null, \"text\": [\"Be brief.\"] } }"));

        var refusals = Publish(files);

        var refusal = Assert.Single(refusals, r => r.Id == "S05-006");
        V31Contract.AssertSentence(refusal);
        Assert.Contains(source, refusal.Sentence, StringComparison.Ordinal);
    }

    /// <summary>
    /// The JSON Logic heads (part 03 § 4.2; <c>deliverable</c> under <c>checks/</c> only), where each is read, a
    /// <c>var</c> path that exists, one whose last step does not and one whose middle step does not.
    /// </summary>
    public static TheoryData<string, string, string, string, string> VarHeads => new()
    {
        { "node", "top", "node.plan.value.sections", "node.plan.value.nope", "node.plan.value.nope.title" },
        { "node", "top", "node.plan.value", "node.plan.nope", "node.nope.value" },
        { "input", "top", "input.consult_draft.content", "input.consult_draft.nope", "input.nope.content" },
        { "check", "top", "check.coverage.value.pass", "check.coverage.value.nope", "check.nope.value.pass" },
        { "item", "loop", "item.value.title", "item.value.nope", "item.nope.title" },
        { "pass", "while", "pass.number", "pass.nope", "pass.node.nope.content" },
        { "param", "function", "param.draft.content", "param.draft.nope", "param.nope.content" },
        { "run", "top", "run.date", "run.date.nope", "run.nope.value" },
        { "deliverable", "top", "deliverable.note.content", "deliverable.note.nope", "deliverable.nope.content" },
    };

    [Theory]
    [MemberData(nameof(VarHeads))]
    public void EveryHead_IsCheckedAtEveryStep_InJsonLogic(string head, string place, string exists, string last, string middle)
    {
        foreach (var site in LegalRuleSites(head, place))
        {
            var (valid, _) = RuleAt(site, place, exists);
            var said = Publish(valid);
            Assert.True(said.Count == 0, $"{head} {site} {exists}: " + Said(said));

            foreach (var read in new[] { last, middle })
            {
                var (files, path) = RuleAt(site, place, read);

                var refusals = Publish(files);

                Assert.True(refusals.Count > 0, $"{head} {site} {read} passed as untyped");
                Assert.All(refusals, V31Contract.AssertSentence);
                Assert.True(refusals.Any(r => r.Id == "S03-014" && r.Path == path && r.Sentence.EndsWith($" reads {read}, which doesn't exist here.", StringComparison.Ordinal)),
                    $"{head} {site} {read}: " + Said(refusals));
            }
        }
    }

    /// <summary>The rule sites where <paramref name="head"/> is legal (part 03 § 4.2), at <paramref name="place"/>.</summary>
    private static IEnumerable<string> LegalRuleSites(string head, string place) =>
        new[] { "node if", "macro if", "deliverable if", "check if" }.Where(site => head switch
        {
            "deliverable" => site == "check if",
            "check" => site is "node if" or "macro if" or "check if",
            "param" => site == "node if",
            "item" or "pass" => site is "node if" or "macro if",
            _ => true,
        } && (place == "top" || site is "node if" or "macro if"));

    /// <summary>
    /// A package whose <paramref name="site"/>'s rule is <c>{ "!==": [{ "var": <paramref name="read"/> }, null] }</c>, the
    /// agent <c>draft</c> at <paramref name="place"/>, and the file that holds the rule: <c>draft</c>'s own <c>if</c>, the
    /// <c>if</c> of a macro <c>probe</c> it places, the deliverable <c>note</c>'s <c>if</c>, or the <c>if</c> of a check
    /// <c>probe</c>.
    /// </summary>
    private static (Dictionary<string, byte[]> Files, string Path) RuleAt(string site, string place, string read)
    {
        var rule = $"{{ \"!==\": [ {{ \"var\": \"{read}\" }}, null ] }}";
        var (files, prompt) = Placer(place);
        files.With(prompt, Agent("Agent draft", Plain, site == "macro if" ? "Draft the note.\n{{ macro.probe }}\n" : "Draft the note.\n"));
        switch (site)
        {
            case "node if":
                files.WithJson(prompt, h => h["if"] = JsonNode.Parse(rule));
                return (files, prompt);
            case "macro if":
                files.WithMacroOf("probe", "Follow up with Dr A. Sample in six weeks.\n", ["End with {{ macro.probe.content }}"], condition: rule);
                if (place == "function")
                    files.WithJson("functions/draft_section/index.json", f => f["uses"]!["macros"]!.AsArray().Add((JsonNode)"probe"));
                return (files, "macros/probe.md");
            case "deliverable if":
                files.WithDeliverable("Note: {{ node.plan.content }}\n").WithJson("deliverables/note.md", h => h["if"] = JsonNode.Parse(rule));
                return (files, "deliverables/note.md");
            default:
                files.WithDeliverable("Note: {{ node.plan.content }}\n")
                    .Listed("checks/index.json", "probe")
                    .With("checks/probe.md", ResolveFixture.ObjectCheck("Probe", "true", modelDerived: false))
                    .WithJson("checks/probe.md", h => h["if"] = JsonNode.Parse(rule));
                return (files, "checks/probe.md");
        }
    }

    /// <summary>The read's head and first step, which S06-028 names (<c>profile.nope</c> of <c>profile.nope.content</c>).</summary>
    private static string Named(string read) => string.Join('.', read.Split('.').Take(2));

    /// <summary>
    /// A package reading <c>{{ <paramref name="read"/> }}</c> (on its second line) at <paramref name="site"/>, placed at
    /// <paramref name="place"/>, and the file that holds the read. A snippet <c>probe</c> or a macro <c>probe</c> holding
    /// it is placed by the agent <c>draft</c>'s prompt; a macro's own marker is the first line of its instruction.
    /// </summary>
    internal static (Dictionary<string, byte[]> Files, string Path) At(string site, string place, string read)
    {
        var line = $"Write for Margaret Okafor.\n{Output(read)}\n";
        var (files, prompt) = Placer(place);
        if (site == "deliverable")
        {
            files.With(prompt, Agent("Agent draft", Plain, "Draft the note.\n"));
            return (files.WithDeliverable(line), "deliverables/note.md");
        }

        string body;
        string path;
        switch (site)
        {
            case "prompt":
                body = line;
                path = prompt;
                break;
            case "snippet":
                files.WithSnippet("probe", line);
                body = "Draft the note.\n{{~ snippet.probe.content }}\n";
                path = "snippets/probe.md";
                break;
            case "macro body":
                files.WithMacroOf("probe", line, ["End with {{ macro.probe.content }}"]);
                body = "Draft the note.\n{{ macro.probe }}\n";
                path = "macros/probe.md";
                break;
            default:
                files.WithMacroOf("probe", "Follow up with Dr A. Sample in six weeks.\n", ["End with {{ macro.probe.content }}", Output(read)]);
                body = "Draft the note.\n{{ macro.probe }}\n";
                path = "macros/probe.md";
                break;
        }

        files.With(prompt, Agent("Agent draft", Plain, body));
        if (body.Contains("part.", StringComparison.Ordinal) || line.Contains("part.", StringComparison.Ordinal))
            files.WithJson(prompt, h => h["parts"] = JsonNode.Parse("{ \"brief\": { \"if\": null, \"text\": [\"Be brief.\"] } }"));
        // What the read makes the placing node (part 03 § 4.6, S03-036), declared so.
        if (read.StartsWith("run.", StringComparison.Ordinal))
            files.RunDependent(prompt);
        if (read.StartsWith("profile.", StringComparison.Ordinal))
            files.WithJson(prompt, h => h["profileCarrying"] = true);
        if (place == "function")
        {
            files.WithJson("functions/draft_section/index.json", f =>
            {
                f["uses"]!["snippets"]!.AsArray().Add((JsonNode)"tone");
                if (site == "snippet")
                    f["uses"]!["snippets"]!.AsArray().Add((JsonNode)"probe");
                if (site is "macro body" or "instruction")
                    f["uses"]!["macros"]!.AsArray().Add((JsonNode)"probe");
            });
        }

        return (files, path);
    }

    /// <summary>The reads of <see cref="Heads"/> that exist and are <c>markdown_block</c>, which stand alone on their line (S05-066).</summary>
    private static readonly string[] BlockReads = ["input.consult_draft.content", "profile.signature"];

    /// <summary>
    /// <paramref name="read"/> as an output: a placement in its block form, a block read alone on its line (#1543, ruling
    /// 2), any other read inline.
    /// </summary>
    private static string Output(string read) =>
        read.Split('.') is ["part", _] or ["macro", _] or ["macro", _, not "content"] ? $"{{{{~ {read} }}}}"
        : BlockReads.Contains(read) ? $"{{{{ {read} }}}}"
        : $"Note: {{{{ {read} }}}}";

    /// <summary>
    /// The package with the snippet <c>tone</c>, the macro <c>follow_up</c>, the macro folder <c>notes</c> and the check
    /// <c>coverage</c>, and the file of the agent <c>draft</c> at <paramref name="place"/>.
    /// </summary>
    private static (Dictionary<string, byte[]> Files, string Prompt) Placer(string place)
    {
        if (place == "function")
            return (ResolveFixture.Functions().WithSnippet("tone", "Write warmly for Margaret Okafor.\n"), "functions/draft_section/patient_draft.md");

        var files = Package()
            .WithSnippet("tone", "Write warmly for Margaret Okafor.\n")
            .WithMacro("follow_up", "Follow up with Dr A. Sample in six weeks.\n")
            .Listed("macros/index.json", "notes")
            .With("macros/notes/index.json", Index(["hpi"], extra: ", \"if\": null"))
            .With(FolderMacro, Md(
                "{ \"label\": \"History\", \"description\": null, \"if\": null, \"format\": \"plaintext\", "
                + "\"instruction\": [\"Place the history's closing line alone:\", \"{{~ macro.notes.hpi.content }}\"] }",
                "History taken from Margaret Okafor and her daughter.\n"))
            .WithCheck();
        switch (place)
        {
            case "loop":
                return (files.WithLoop("sections", "node.plan.value.sections", "draft", ""), "nodes/sections/draft.md");
            case "while":
                return (files.WithWhile("refine", "draft", ""), "nodes/refine/draft.md");
            default:
                return (files.WithAgent("draft", ""), "nodes/draft.md");
        }
    }

    private static IReadOnlyList<SourceRefusal> Publish(Dictionary<string, byte[]> files) =>
        CompiledPackage.Publish(files, ResolveFixture.Published(), CompiledGoldenTests.Fixed).Refusals;
}
