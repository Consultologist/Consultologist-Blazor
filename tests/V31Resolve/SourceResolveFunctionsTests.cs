using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Resolve;

/// <summary>
/// #1502 (specVersion 31, area 05): every <c>call</c> bound to its function, its arguments typed against the
/// parameters, a function's <c>ret</c> and <c>uses</c> held to part 04 § 7, recursion refused and call depth measured
/// (§ 7.8; § 6 <c>depth</c>; § 10.1's "Functions and calls" rows). Each refusal is one change to
/// <see cref="ResolveFixture.Functions"/> (the loop <c>standards</c> calling <c>draft_section</c> once per standard) or
/// to a chain of functions built here, and is the only one said.
/// </summary>
public class SourceResolveFunctionsTests
{
    private const string Call = "nodes/standards/section.md";

    private const string Function = "functions/draft_section/index.json";

    private const string Plain = "\"plaintext\"";

    /// <summary>The one refusal <see cref="SourceCheck.Run"/> says of <paramref name="files"/>, held to its row.</summary>
    private static SourceRefusal Refused(Dictionary<string, byte[]> files, ITemplateReads? templates = null)
    {
        var result = templates == null ? ResolveFixture.Run(files) : SourceCheck.Run(files, ResolveFixture.Published(), templates);
        Assert.True(result.Refusals.Count == 1, ResolveFixture.Said(result));
        var refusal = result.Refusals[0];
        V31Contract.AssertSentence(refusal);
        return refusal;
    }

    private static void AssertRefusal(Dictionary<string, byte[]> files, string id, string sentence, string path, ITemplateReads? templates = null)
    {
        var refusal = Refused(files, templates);
        Assert.Equal((id, sentence, path), (refusal.Id, refusal.Sentence, refusal.Path));
    }

    private static Resolution AssertAccepted(Dictionary<string, byte[]> files, ITemplateReads? templates = null)
    {
        var result = templates == null ? ResolveFixture.Run(files) : SourceCheck.Run(files, ResolveFixture.Published(), templates);
        Assert.True(result.Refusals.Count == 0, ResolveFixture.Said(result));
        return result.Resolution!;
    }

    /// <summary><see cref="ResolveFixture.Functions"/> with the call's <c>with</c> member <paramref name="param"/> set to <paramref name="json"/>.</summary>
    private static Dictionary<string, byte[]> With(string param, string json) =>
        ResolveFixture.Functions().WithJson(Call, h => h["with"]![param] = JsonNode.Parse(json));

    /// <summary>
    /// <see cref="ResolveFixture.Functions"/> with <c>draft_section</c>'s parameter <c>draft</c> of <paramref name="format"/>,
    /// passed <paramref name="argument"/>; an argument read from the profile or a run fact makes the call profile-carrying
    /// or run-dependent (#1502, Task 5), and the loop that returns it, the check loop over its items and the check that
    /// reads that, so their headers say so. #1504 (area 07): <c>patient_draft</c> reads the parameter's <c>.value</c>, which
    /// every format has, so its prompt is a valid template whatever the format.
    /// </summary>
    private static Dictionary<string, byte[]> Draft(string format, string argument)
    {
        var files = ResolveFixture.Functions()
            .WithJson(Function, f => f["inputs"]!["draft"]!["format"] = JsonNode.Parse(format))
            .WithJson(Call, h => h["with"]!["draft"] = JsonValue.Create(argument))
            .With("functions/draft_section/patient_draft.md", Agent("Drafting from the patient's facts", Plain,
                "Using the draft below, write what it says about Margaret Okafor for this section.\n\n{{ param.draft.value }}\n"));
        var flag = argument.StartsWith("profile.", StringComparison.Ordinal) ? "profileCarrying"
            : argument.StartsWith("run.", StringComparison.Ordinal) ? "runDependent" : null;
        if (flag == null)
            return files;
        foreach (var path in new[] { Call, "nodes/standards/index.json", "checks/section_coverage/index.json", "checks/all_sections.md" })
            files.WithJson(path, h => h[flag] = true);
        return files;
    }

    // ---- The example ---------------------------------------------------------------------------

    [Fact]
    public void TheExample_Passes()
    {
        var resolution = AssertAccepted(ResolveFixture.Functions());

        var (call, binding) = Assert.Single(resolution.Calls);
        Assert.Equal("node.standards.section", call.Key);
        Assert.Equal("draft_section", binding.Function.Id);
        Assert.Equal(0, binding.Depth);
        Assert.Equal(["draft", "standard"], binding.Arguments.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("input.consult_draft", binding.Arguments["draft"]!.Key);
        Assert.NotNull(binding.Arguments["standard"]);
        Assert.Empty(resolution.Warnings);
    }

    // ---- The call ------------------------------------------------------------------------------

    [Fact]
    public void AnUnknownFunction_IsS04064() =>
        AssertRefusal(ResolveFixture.Functions().WithJson(Call, h => h["function"] = "draft_sections"), "S04-064",
            "Call 'section' names function 'draft_sections', which the package doesn't define.", Call);

    [Fact]
    public void AParameterNotPassed_IsS04065() =>
        AssertRefusal(ResolveFixture.Functions().WithJson(Call, h => h["with"]!.AsObject().Remove("draft")), "S04-065",
            "Call 'section' passes no draft; function 'draft_section' takes standard, draft.", Call);

    [Fact]
    public void AnExtraParameter_IsS04066() =>
        AssertRefusal(With("tone", "\"item\""), "S04-066",
            "Call 'section' passes tone, which function 'draft_section' doesn't take (standard, draft).", Call);

    [Theory]
    [InlineData("draft", "item", "standard_section", "markdown_block")]
    [InlineData("standard", "input.consult_draft.content", "markdown_block", "standard_section")]
    [InlineData("draft", "node.plan", "plaintext", "markdown_block")]
    public void AnArgumentOfAnotherType_IsS04068(string param, string reference, string format, string expected) =>
        AssertRefusal(With(param, JsonValue.Create(reference)!.ToJsonString()), "S04-068",
            $"Call 'section' passes {reference} ({format}) as {param}; function 'draft_section' takes {expected}.", Call);

    [Fact]
    public void AnArgumentThatMayBeNull_NeedsANullableParameter()
    {
        // plan, given an if, may be skipped and its value read as null (D14).
        var files = Draft(Plain, "node.plan")
            .WithJson("nodes/plan.md", h => h["if"] = JsonNode.Parse("{ \"!\": [{ \"blank\": [{ \"var\": \"input.consult_draft.content\" }] }] }"));
        AssertRefusal(files, "S04-068",
            "Call 'section' passes node.plan (plaintext | null) as draft; function 'draft_section' takes plaintext.", Call);

        AssertAccepted(Draft("{ \"anyOf\": [{ \"$ref\": \"plaintext\" }, { \"type\": \"null\" }] }", "node.plan")
            .WithJson("nodes/plan.md", h => h["if"] = JsonNode.Parse("{ \"!\": [{ \"blank\": [{ \"var\": \"input.consult_draft.content\" }] }] }")));
    }

    [Fact]
    public void ACallsFormat_IsTheFunctions_S04070()
    {
        var files = ResolveFixture.Functions()
            .WithJson(Call, h => h["format"] = "markdown_block")
            .WithJson("nodes/standards/index.json", h => h["format"] = JsonNode.Parse(ArrayOf("\"markdown_block\"")));
        AssertRefusal(files, "S04-070", "Call 'section' declares format markdown_block; function 'draft_section' returns plaintext.", Call);
    }

    [Fact]
    public void ACallWithABody_IsTheReadersS01020()
    {
        // D2: the reader already refuses a call's body, so S04-071 is not said.
        var files = ResolveFixture.Functions().With(Call, Text(ResolveFixture.Functions(), Call) + "Draft it carefully.\n");
        var refusal = Refused(files);
        Assert.Equal(("S01-020", Call), (refusal.Id, refusal.Path));
    }

    [Theory]
    [InlineData("standard", "item")]
    [InlineData("standard", "item.value")]
    [InlineData("standard", "snippet.standards.hpi")]
    [InlineData("draft", "input.consult_draft.value")]
    public void Arguments_OfTheExample_AreTyped(string param, string reference) =>
        AssertAccepted(With(param, JsonValue.Create(reference)!.ToJsonString()));

    [Theory]
    [InlineData(Plain, "node.plan")]
    [InlineData(Plain, "node.plan.content")]
    [InlineData("\"markdown_block\"", "profile.signature")]
    [InlineData("\"markdown_block\"", "profile.closing")]
    [InlineData("\"string\"", "profile.name")]
    [InlineData("\"date\"", "run.date")]
    [InlineData("\"string\"", "item.id")]
    [InlineData("\"plaintext\"", "item.value.title")]
    [InlineData("\"plaintext\"", "snippet.standards.hpi.value.title")]
    public void Arguments_OfEveryHead_AreTyped(string format, string reference) =>
        AssertAccepted(Draft(format, reference), BodiesUnknown);

    /// <summary>
    /// #1504 (area 07, D18): a seam that knows no body, so that what the arguments carry into the function's prompts (a
    /// profile field, a run fact) is not judged: the resolver's typing is what these tests hold.
    /// </summary>
    private static StubTemplates BodiesUnknown => new(new Dictionary<string, string[]>(), everyBody: false);

    [Theory]
    [InlineData("profile.nickname")]
    [InlineData("snippet.standards.nothing")]
    [InlineData("snippet.standards")]
    [InlineData("node.nothing")]
    [InlineData("item.value.heading")]
    public void AnArgumentThatNamesNothing_IsS03014(string reference) =>
        AssertRefusal(Draft(Plain, reference), "S03-014", $"Call 'section' reads {reference}, which doesn't exist here.", Call);

    [Fact]
    public void ABareInput_IsS02018()
    {
        var refusal = Refused(Draft(Plain, "input.consult_draft"));
        Assert.Equal(("S02-018", Call), (refusal.Id, refusal.Path));
    }

    [Theory]
    [InlineData("5", "A05-005", "Call 'section' with draft is a whole number; an argument is a reference, written as text, or null.")]
    [InlineData("2.5", "A05-005", "Call 'section' with draft is a number; an argument is a reference, written as text, or null.")]
    [InlineData("true", "A05-005", "Call 'section' with draft is true or false; an argument is a reference, written as text, or null.")]
    [InlineData("{}", "S04-017", "Call 'section' with draft is an expression; it names one readable thing: compute the value in a computed node and name that.")]
    [InlineData("[]", "S04-017", "Call 'section' with draft is an expression; it names one readable thing: compute the value in a computed node and name that.")]
    [InlineData("{ \"var\": \"input.consult_draft.value\" }", "S04-017", "Call 'section' with draft is an expression; it names one readable thing: compute the value in a computed node and name that.")]
    [InlineData("\"\"", "A05-001", "Call 'section' with draft '' is not a reference; a reference is a root, then names joined by dots.")]
    [InlineData("\"input..consult_draft\"", "A05-001", "Call 'section' with draft 'input..consult_draft' is not a reference; a reference is a root, then names joined by dots.")]
    [InlineData("\"node\"", "S03-014", "Call 'section' reads node, which doesn't exist here.")]
    [InlineData("null", "S04-068", "Call 'section' passes null (null) as draft; function 'draft_section' takes markdown_block.")]
    public void WithValues_OfAnyJsonKind(string json, string id, string sentence)
    {
        var result = ResolveFixture.Run(With("draft", json));

        Assert.True(result.Refusals.Count == 1, ResolveFixture.Said(result));
        var refusal = result.Refusals[0];
        Assert.Equal((id, sentence, Call), (refusal.Id, refusal.Sentence, refusal.Path));
        if (json != "\"\"")
            V31Contract.AssertSentence(refusal); // an empty <text> cannot be matched by the row's pattern (Task 2)
    }

    [Fact]
    public void WithNull_ForANullableParameter_Passes()
    {
        var resolution = AssertAccepted(ResolveFixture.Functions()
            .WithJson(Function, f => f["inputs"]!["draft"]!["format"] = JsonNode.Parse(
                "{ \"anyOf\": [{ \"$ref\": \"markdown_block\" }, { \"type\": \"null\" }] }"))
            .WithJson(Call, h => h["with"]!["draft"] = null));

        var binding = Assert.Single(resolution.Calls).Value;
        Assert.True(binding.Arguments.ContainsKey("draft"));
        Assert.Null(binding.Arguments["draft"]);
    }

    // ---- The function: its ret -----------------------------------------------------------------

    /// <summary><see cref="ResolveFixture.Functions"/> with <c>draft_section</c> returning the top-level <paramref name="ret"/>, written in <paramref name="files"/>.</summary>
    private static Dictionary<string, byte[]> Returning(string ret, params (string Path, string Text)[] files)
    {
        var package = ResolveFixture.Functions().WithJson(Function, f =>
        {
            f["nodes"]!.AsArray().Add(ret);
            f["ret"] = $"node.{ret}";
        });
        foreach (var (path, text) in files)
            package.With($"functions/draft_section/{path}", text);
        return package;
    }

    private const string ParamHasText = "{ \"!\": [{ \"blank\": [{ \"var\": \"param.draft.content\" }] }] }";

    private static (string, string) Final(string id, string format = Plain, string condition = "null") =>
        ($"checked/{id}.md", Md(SourceFixture.Node("agent", "Writing the section", format, false, true,
            $"\"if\": {condition}, \"onInvalid\": \"retry\", \"failIfEmpty\": null, \"parts\": null, \"ordered\": false"),
            "Write the section for Margaret Okafor.\n\n{{ param.draft.content }}\n"));

    private static (string, string) Checked(string format, string otherwise) =>
        ("checked/index.json", Json(SourceFixture.Node("if", "Writing when there is a draft", format, false, true,
            $"\"if\": {ParamHasText}, \"then\": {{ \"nodes\": [\"final\"], \"ret\": \"node.checked.final\" }}, \"else\": {otherwise}")));

    private const string Else = "{ \"nodes\": [\"fallback\"], \"ret\": \"node.checked.fallback\" }";

    [Fact]
    public void ARetOfAnotherFormat_IsS04061() =>
        AssertRefusal(ResolveFixture.Functions().WithJson("functions/draft_section/apply_instructions.md", h => h["format"] = "markdown_block"),
            "S04-061", "Function 'draft_section' declares format plaintext; its ret 'apply_instructions' returns markdown_block.", Function);

    [Fact]
    public void ARetThatIsAnIfWithNoElse_IsS04057()
    {
        AssertRefusal(Returning("checked", Checked(Plain, "null"), Final("final")), "S04-057",
            "Function 'draft_section' returns node.checked, an if with no else: add an else, or return a node that always runs.", Function);
        AssertAccepted(Returning("checked", Checked(Plain, Else), Final("final"), Final("fallback")));
    }

    [Fact]
    public void ARetThatMayBeSkippedInside_IsS04058()
    {
        AssertRefusal(Returning("checked", Checked(Plain, Else), Final("final", condition: ParamHasText), Final("fallback")), "S04-058",
            "Function 'draft_section' returns node.checked.final, which runs inside if 'checked' and may be skipped: return a node that always runs.",
            Function);

        // A ret skipped by its own if runs inside that if.
        AssertRefusal(ResolveFixture.Functions().WithJson("functions/draft_section/apply_instructions.md", h => h["if"] = JsonNode.Parse(ParamHasText)),
            "S04-058",
            "Function 'draft_section' returns node.apply_instructions, which runs inside if 'apply_instructions' and may be skipped: return a node that always runs.",
            Function);
    }

    [Fact]
    public void ARetThatIsACaseWithAnEmptyBranch_IsS04059()
    {
        var route = ("route/index.json", Json(SourceFixture.Node("case", "Routing by the standard's draft", Plain, false, true,
            "\"case\": \"node.standard_draft.state\", \"branches\": ["
            + "{ \"when\": [\"completed\"], \"nodes\": [\"final\"], \"ret\": \"node.route.final\" }, "
            + "{ \"when\": [\"skipped\", \"not_run\"], \"nodes\": [], \"ret\": null }], \"else\": null")));
        var final = ("route/final.md", Final("final").Item2);
        AssertRefusal(Returning("route", route, final), "S04-059",
            "Function 'draft_section' returns node.route, a case with an empty branch for \"skipped\", \"not_run\": return a node that always runs.",
            Function);
    }

    [Fact]
    public void ARetWhoseBranchesReturnTwoFormats_IsS04060()
    {
        const string union = "{ \"anyOf\": [{ \"$ref\": \"plaintext\" }, { \"$ref\": \"markdown_block\" }] }";
        AssertRefusal(Returning("checked", Checked(union, Else), Final("final"), Final("fallback", "\"markdown_block\"")), "S04-060",
            "Function 'draft_section' returns node.checked, whose branches return plaintext and markdown_block: a function returns one format. Wrap them in an object format.",
            Function);
    }

    // ---- The function: uses --------------------------------------------------------------------

    [Fact]
    public void AnOwnDefinitionSharingAnIdWithUses_IsS04053()
    {
        var files = ResolveFixture.Functions()
            .WithJson(Function, f => f["uses"]!["snippets"] = new JsonArray("standards"))
            .With("functions/draft_section/snippets/index.json", Index(["standards"]))
            .With("functions/draft_section/snippets/standards.md",
                "---\n{ \"label\": \"Standards\", \"description\": null, \"if\": null, \"format\": \"plaintext\" }\n---\nWrite plainly for Dr A. Sample.\n");
        AssertRefusal(files, "S04-053", "Function 'draft_section' has its own snippet 'standards' and also uses the package's: rename one.", Function);
    }

    /// <summary><see cref="ResolveFixture.Functions"/> with a loop <c>each</c> in <c>draft_section</c> over the package's snippet folder <c>standards</c>.</summary>
    private static Dictionary<string, byte[]> WithStandardsInside() =>
        ResolveFixture.Functions()
            .WithJson(Function, f => f["nodes"]!.AsArray().Add("each"))
            .With("functions/draft_section/each/index.json", Json(SourceFixture.Node("for", "Each standard", ArrayOf(Plain), false, true,
                "\"in\": \"snippet.standards\", \"parallel\": false, \"maxParallel\": null, \"until\": null, \"max\": null, "
                + "\"itemLabel\": \"title\", \"nodes\": [\"note\"], \"ret\": \"node.each.note\"")))
            .With("functions/draft_section/each/note.md", Agent("Noting the standard", Plain,
                "Note the standard '{{ item.value.title }}' in one line.\n"));

    [Fact]
    public void APackageSnippetNotInUses_IsS04052()
    {
        AssertRefusal(WithStandardsInside(), "S04-052",
            "Function 'draft_section' uses snippet 'standards' but doesn't list it in uses.", "functions/draft_section/each/index.json");
        AssertAccepted(WithStandardsInside().WithJson(Function, f => f["uses"]!["snippets"] = new JsonArray("standards")));
    }

    [Fact]
    public void AFormatListedAndNeverNamed_IsTheWarningS04074()
    {
        var resolution = AssertAccepted(ResolveFixture.Functions()
            .WithJson(Function, f => f["uses"]!["formats"] = new JsonArray("standard_section", "concept_list")));

        var warning = Assert.Single(resolution.Warnings);
        Assert.Equal(("S04-074", "Function 'draft_section' lists format 'concept_list' in uses but never uses it.", Function),
            (warning.Id, warning.Sentence, warning.Path));
        V31Contract.AssertSentence(warning);
    }

    // ---- The call graph ------------------------------------------------------------------------

    /// <summary>
    /// <see cref="SourceFixture.Minimal"/> with the top-level call <c>start</c> of <c>f01</c> and the functions
    /// <paramref name="ids"/>: each takes <c>draft</c> and returns its one node, a call <c>next</c> of the function
    /// <paramref name="calls"/> names, or, when it names none, the agent <c>write</c>.
    /// </summary>
    private static Dictionary<string, byte[]> Calling(IReadOnlyList<string> ids, Func<string, string?> calls)
    {
        var files = Minimal()
            .With("nodes/index.json", Index(["plan", "start"]))
            .With("nodes/start.md", CallNode(ids[0], "input.consult_draft.value"))
            .With("functions/index.json", Index(ids));
        foreach (var id in ids)
        {
            var next = calls(id);
            var node = next == null ? "write" : "next";
            files.With($"functions/{id}/index.json", Json(
                    "{ \"label\": \"Drafting\", \"description\": null, "
                    + "\"inputs\": { \"draft\": { \"label\": \"Consult draft\", \"description\": null, \"format\": \"markdown_block\" } }, "
                    + "\"uses\": { \"snippets\": [], \"macros\": [], \"formats\": [], \"renderers\": [] }, "
                    + $"\"nodes\": [\"{node}\"], \"ret\": \"node.{node}\", \"format\": \"plaintext\" }}"))
                .With($"functions/{id}/{node}.md", next == null
                    ? Agent("Writing", Plain, "Write the section for Margaret Okafor.\n\n{{ param.draft.content }}\n")
                    : CallNode(next, "param.draft.value"));
            foreach (var folder in new[] { "snippets", "macros", "formats", "renderers" })
                files.With($"functions/{id}/{folder}/index.json", Index([]));
        }

        return files;
    }

    private static string CallNode(string function, string draft) =>
        Md(SourceFixture.Node("call", "Calling the next function", Plain, false, true,
            $"\"if\": null, \"function\": \"{function}\", \"with\": {{ \"draft\": \"{draft}\" }}"), "");

    private static string[] Ids(int n) => Enumerable.Range(1, n).Select(i => $"f{i:00}").ToArray();

    /// <summary>A chain f01 → f02 → … → f<paramref name="n"/>.</summary>
    private static Dictionary<string, byte[]> Chain(int n)
    {
        var ids = Ids(n);
        return Calling(ids, id => Array.IndexOf(ids, id) is var i && i + 1 < n ? ids[i + 1] : null);
    }

    [Fact]
    public void CallRings_AreRefused()
    {
        AssertRefusal(Calling(["f01"], _ => "f01"), "S04-055",
            "Function 'f01' calls 'f01': functions may not call themselves.", "functions/f01/next.md");

        AssertRefusal(Calling(["f01", "f02"], id => id == "f01" ? "f02" : "f01"), "S04-054",
            "Function 'f01' calls 'f02', which calls 'f01': functions may not call themselves.", "functions/f01/next.md");

        // The ring is named from its ordinal-first member, whichever the top-level call reaches first.
        AssertRefusal(Calling(["f02", "f03", "f01"], id => id switch { "f01" => "f02", "f02" => "f03", _ => "f01" }), "S04-054",
            "Function 'f01' calls 'f02', which calls 'f03', which calls 'f01': functions may not call themselves.", "functions/f01/next.md");

        var forty = Ids(40);
        var refusal = Refused(Calling(forty, id => forty[(Array.IndexOf(forty, id) + 1) % 40]));
        Assert.Equal("S04-054", refusal.Id);
        Assert.StartsWith("Function 'f01' calls 'f02', which calls 'f03', ", refusal.Sentence, StringComparison.Ordinal);
        Assert.EndsWith("which calls 'f40', which calls 'f01': functions may not call themselves.", refusal.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void Depth_IsMeasured()
    {
        var resolution = AssertAccepted(Chain(16));
        var depths = resolution.Calls.OrderBy(c => c.Value.Depth).Select(c => (c.Value.Function.Id, c.Value.Depth)).ToList();
        Assert.Equal(Ids(16).Select((id, i) => (id, i)).ToList(), depths);

        AssertRefusal(Chain(17), "S04-056", "Call 'start' reaches depth 16; calls nest at most FormatBounds.depth (15) deep.", "nodes/start.md");
    }

    [Fact]
    public void ACallInsideAFunction_ReadsNoProfile() =>
        AssertRefusal(Calling(["f01", "f02"], id => id == "f01" ? "f02" : null)
                .WithJson("functions/f01/next.md", h => h["with"]!["draft"] = "profile.signature"), "S04-051",
            "Node 'next' in function 'f01' reads profile.signature; a function reads only its parameters: add a parameter and pass it in with.",
            "functions/f01/next.md");

    // ---- The profile ---------------------------------------------------------------------------

    /// <summary><see cref="ResolveFixture.Functions"/> with <c>draft_section</c>'s <c>draft</c> a markdown_block bound to <c>profile.signature</c>, and <c>standard_draft</c>'s <c>if</c> reading it.</summary>
    private static Dictionary<string, byte[]> ProfileRead(string condition) =>
        Draft("\"markdown_block\"", "profile.signature")
            .WithJson("functions/draft_section/standard_draft.md", h => h["if"] = JsonNode.Parse(condition));

    [Fact]
    public void AProfileParameter_IsNotReadByACondition()
    {
        AssertRefusal(ProfileRead(ParamHasText), "S04-069",
            "Function 'draft_section' reads param.draft, which carries profile.signature, in a condition; the profile is read only in deliverables and macro bodies.",
            "functions/draft_section/standard_draft.md", BodiesUnknown);

        // Read only in a prompt and passed on, it is not the condition's; through the dialect's reads the prompt's
        // read is S04-069 (#1504, area 07).
        AssertAccepted(Draft("\"markdown_block\"", "profile.signature"), BodiesUnknown);
        Assert.Contains(ResolveFixture.Run(Draft("\"markdown_block\"", "profile.signature")).Refusals, r => r.Id == "S04-069" && r.Sentence ==
            "Function 'draft_section' reads param.draft, which carries profile.signature, in a prompt; the profile is read only in deliverables and macro bodies.");
    }

    [Fact]
    public void AParameterPassedAProfileParameter_CarriesTheProfile()
    {
        var files = Calling(["f01", "f02"], id => id == "f01" ? "f02" : null)
            .WithJson("functions/f01/index.json", f => f["inputs"]!["draft"]!["format"] = "markdown_block")
            .WithJson("functions/f02/index.json", f => f["inputs"]!["draft"]!["format"] = "markdown_block")
            .WithJson("nodes/start.md", h => h["with"]!["draft"] = "profile.closing")
            .WithJson("functions/f02/index.json", f => f["nodes"]!.AsArray().Add("length"))
            .With("functions/f02/length.md", Md(SourceFixture.Node("computed", "Measuring the draft", "\"number\"", true, false,
                $"\"if\": {ParamHasText}, \"expression\": {{ \"size\": [{{ \"var\": \"param.draft.content\" }}] }}"), ""));
        AssertRefusal(files, "S04-069",
            "Function 'f02' reads param.draft, which carries profile.closing, in a condition; the profile is read only in deliverables and macro bodies.",
            "functions/f02/length.md");
    }

    [Fact]
    public void ProfileFields_AreTheContracts()
    {
        var fields = V31Contract.Json("profile.json")["fields"]!.AsObject()
            .Select(p => (p.Key, (string)p.Value!["format"]!)).ToList();
        Assert.Equal(fields, SourceResolve.ProfileFields.Select(p => (p.Key, p.Value)).ToList());
    }

    // ---- Branches, carried from Task 3 ---------------------------------------------------------

    [Fact]
    public void AValueListedTwiceInOneBranch_IsS04046() =>
        AssertRefusal(ResolveFixture.Conditional().WithJson("nodes/sections_draft/route/index.json",
                h => h["branches"]![1]!["when"] = new JsonArray("table", "table")), "S04-046",
            "Case 'route' lists \"table\" in branches 2 and 2; a value belongs to one branch.", "nodes/sections_draft/route/index.json");
}
