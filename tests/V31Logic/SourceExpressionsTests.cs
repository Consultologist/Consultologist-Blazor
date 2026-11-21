using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Logic;

/// <summary>
/// #1501: every expression site of a package source (part 03 § 3; D2, D4, D5, D28), checked as the third step of
/// <see cref="SourceCheck.Run"/>: each kind of site under its subject and file, the inputs and run facts typed by
/// <see cref="PackageScope"/>, the other roots and everything inside a function deferred, a scope from outside used,
/// and the run-fact warnings of § 11.2. #1502 (area 05): <see cref="SourceCheck.Run"/> now gives area 05's scope, so the
/// deferral is seen through <see cref="SourceExpressions.Check"/> with none.
/// </summary>
public class SourceExpressionsTests
{
    private static readonly Lazy<TypeRegistry> Registry = new(V31Contract.PublishedTypes);

    private const string Equal = """{"==":[1,1]}""";

    private static SourceCheckResult Run(Dictionary<string, byte[]> files) => SourceCheck.Run(files, Registry.Value);

    private static Dictionary<string, byte[]> Set(Dictionary<string, byte[]> files, string path, string member, string rule) =>
        files.WithJson(path, h => h[member] = JsonNode.Parse(rule));

    private static string Said(SourceCheckResult result) =>
        string.Join(" | ", result.Refusals.Select(r => $"{r.Id} {r.Path} {r.Sentence}"));

    [Fact]
    public void TheFullFixture_HasNoRefusal_AndEverySiteIsListed()
    {
        var result = Run(SourceFixture.Full());

        Assert.True(result.Refusals.Count == 0, Said(result));
        Assert.NotNull(result.Types);
        var sites = result.Expressions!.Sites;
        Assert.Contains(sites, s => s.Context.Site.Subject == "Computed node 'section_count'" && s.Context.Site.Member == "expression");
        Assert.Contains(sites, s => s.Context.Site.Subject == "While 'refine'" && s.Context.Site.Member == "while");
        var gate = Assert.Single(sites, s => s.Context.Site.Subject == "If 'billing_gate'" && s.Context.Site.Member == "if");
        Assert.Equal("input.include_billing.value = true", gate.Check.Printed);
        Assert.Equal("nodes/billing_gate/index.json", gate.Context.Site.Path);
        Assert.IsType<SourceIf>(gate.Context.Declaration);
        Assert.Empty(result.Expressions.Warnings);

        // The computed check's site is on the check, with its declared format; the coverage check's if is a Check's.
        var coverage = Assert.Single(sites, s => s.Context.Site.Subject == "Computed node 'coverage'");
        Assert.Equal("check_result", coverage.Context.Site.Declared!.GetValue<string>());
        Assert.Contains(sites, s => s.Context.Site.Subject == "Check 'coverage'" && s.Context.Site.Kind == ExpressionSiteKind.CheckIf);
    }

    public static TheoryData<string, string, string, string> Sites => new()
    {
        { "nodes/plan.md", "if", "Node 'plan'", "nodes/plan.md" },
        { "nodes/plan.md", "part", "Part 'tone' of node 'plan'", "nodes/plan.md" },
        { "nodes/section_count.md", "expression", "Computed node 'section_count'", "nodes/section_count.md" },
        { "nodes/billing_gate/index.json", "if", "If 'billing_gate'", "nodes/billing_gate/index.json" },
        { "nodes/refine/index.json", "while", "While 'refine'", "nodes/refine/index.json" },
        { "nodes/refine/index.json", "until", "While 'refine'", "nodes/refine/index.json" },
        { "nodes/sections/index.json", "until", "Loop 'sections'", "nodes/sections/index.json" },
        { "checks/all_sections.md", "if", "Check 'all_sections'", "checks/all_sections.md" },
        { "macros/follow_up.md", "if", "Macro 'follow_up'", "macros/follow_up.md" },
        { "macros/phrases/index.json", "if", "Macro 'phrases'", "macros/phrases/index.json" },
        { "macros/phrases/thanks.md", "if", "Macro 'phrases.thanks'", "macros/phrases/thanks.md" },
        { "snippets/closing_line.md", "if", "Snippet 'closing_line'", "snippets/closing_line.md" },
        { "deliverables/note.md", "if", "Deliverable 'note'", "deliverables/note.md" },
        { "functions/draft_section/standard_draft.md", "if", "Node 'standard_draft' in function 'draft_section'", "functions/draft_section/standard_draft.md" },
    };

    [Theory]
    [MemberData(nameof(Sites))]
    public void EachKindOfSite_IsChecked(string file, string member, string subject, string path)
    {
        var files = SourceFixture.Full();
        if (member == "part")
            files.WithJson(file, h => h["parts"] = new JsonObject { ["tone"] = new JsonObject { ["if"] = JsonNode.Parse(Equal) } });
        else if (file == "nodes/sections/index.json")
            files.WithJson(file, h => { h["parallel"] = false; h["until"] = JsonNode.Parse(Equal); });
        else
            Set(files, file, member, Equal);

        var result = Run(files);

        var refusal = Assert.Single(result.Refusals);
        Assert.Equal("S03-004", refusal.Id);
        Assert.Equal(path, refusal.Path);
        Assert.Equal($"{subject} uses ==; equality is written === (shown as =).", refusal.Sentence);
        Assert.Null(result.Expressions);
        Assert.NotNull(result.Types);
    }

    /// <summary>Exactly one refusal: S03-004 for <see cref="Equal"/>, on <paramref name="path"/>, under <paramref name="subject"/>.</summary>
    private static void OnlyLooseEquality(SourceCheckResult result, string path, string subject)
    {
        var refusal = Assert.Single(result.Refusals);
        Assert.Equal("S03-004", refusal.Id);
        Assert.Equal(path, refusal.Path);
        Assert.Equal($"{subject} uses ==; equality is written === (shown as =).", refusal.Sentence);
    }

    [Theory]
    [InlineData("nodes/billing_gate/billing_text.md", "Node 'billing_text'")] // in an if's then
    [InlineData("nodes/sections/outline.md", "Node 'outline'")] // in a for's body
    [InlineData("nodes/sections/paragraphs/draft_paragraph.md", "Node 'draft_paragraph'")] // in a for in a for
    public void ALeafNestedInAFlowBody_IsChecked(string file, string subject) =>
        OnlyLooseEquality(Run(Set(SourceFixture.Full(), file, "if", Equal)), file, subject);

    [Theory]
    [InlineData("macros", "sign_off", "Macro 'sign_off' in function 'draft_section'")]
    [InlineData("snippets", "closing", "Snippet 'closing' in function 'draft_section'")]
    public void AMacroOrSnippetInsideAFunction_IsChecked(string folder, string id, string subject)
    {
        var file = $"functions/draft_section/{folder}/{id}.md";
        var instruction = folder == "macros" ? ", \"instruction\": [\"Place {{ macro." + id + ".content }} last.\"]" : "";
        var files = SourceFixture.Full()
            .With($"functions/draft_section/{folder}/index.json", SourceFixture.Index([id]))
            .With(file, SourceFixture.Md(
                $"{{ \"label\": \"Sign-off\", \"description\": null, \"if\": {Equal}, \"format\": \"plaintext\"{instruction} }}",
                "Dr A. Sample\n"));

        OnlyLooseEquality(Run(files), file, subject);
    }

    [Fact]
    public void ADeliverableInADeliveryFolder_IsChecked() =>
        OnlyLooseEquality(Run(Set(SourceFixture.Full(), "deliverables/emr/consult.md", "if", Equal)),
            "deliverables/emr/consult.md", "Deliverable 'consult'");

    /// <summary>
    /// A computed node inside a function builds a format declared only in that function's <c>formats/</c>: it
    /// type-checks, so the function's own <see cref="FormatSet"/> is the one its sites are checked against.
    /// </summary>
    [Fact]
    public void AFunctionsSite_UsesTheFunctionsOwnFormats()
    {
        const string function = "functions/draft_section";
        Dictionary<string, byte[]> WithTally(string expression) => SourceFixture.Full()
            .WithJson($"{function}/index.json", f => f["nodes"]!.AsArray().Add("tally"))
            .With($"{function}/formats/index.json", SourceFixture.Index(["paragraph_close"]))
            .With($"{function}/formats/paragraph_close.json", SourceFixture.Json(
                "{ \"$schema\": \"urn:consultologist:spec:31:dialect\", \"type\": \"object\", \"required\": [\"line\"], "
                + "\"additionalProperties\": false, \"properties\": { \"line\": { \"$ref\": \"plaintext\" } } }"))
            .With($"{function}/tally.md", SourceFixture.Md(
                SourceFixture.Node("computed", "Closing the section", "\"paragraph_close\"", true, false,
                    $"\"if\": null, \"expression\": {expression}"),
                ""));

        var result = Run(WithTally("""{ "object": { "line": "Thank you." } }"""));
        Assert.True(result.Refusals.Count == 0, Said(result));
        var site = Assert.Single(result.Expressions!.Sites, s => s.Context.Site.Path == $"{function}/tally.md");
        Assert.Equal("draft_section", site.Context.Function!.Id);
        Assert.Empty(site.Check.Refusals);

        // the field is held to the function's format: a number where it declares plaintext is refused
        var wrong = Run(WithTally("""{ "object": { "line": 5 } }"""));
        var refusal = Assert.Single(wrong.Refusals);
        Assert.Equal($"{function}/tally.md", refusal.Path);
        Assert.Contains("plaintext", refusal.Sentence, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"===":[{"var":"input.consult_draft.content"},"x"]}""", "S03-002")]
    [InlineData("""{"var":"input.nope.value"}""", "S03-014")]
    [InlineData("""{"===":[{"var":"run.date"},"2026-11-20"]}""", null)]
    [InlineData("""{"var":"run.weather"}""", "S03-014")]
    [InlineData("""{"var":"run"}""", "S03-014")]
    public void InputsAndRunFacts_AreTyped(string rule, string? id)
    {
        var result = Run(Set(SourceFixture.Minimal(), "nodes/plan.md", "if", rule));

        if (id == null)
        {
            Assert.True(result.Refusals.Count == 0, Said(result));
        }
        else
        {
            var refusal = Assert.Single(result.Refusals);
            Assert.Equal(id, refusal.Id);
            Assert.Equal("nodes/plan.md", refusal.Path);
            V31Contract.AssertSentence(refusal);
        }
    }

    [Fact]
    public void AnInputNotRequired_IsNullable()
    {
        // include_billing is not required: its value is boolean | null, which a condition accepts as a boolean.
        var result = Run(Set(SourceFixture.Full(), "nodes/plan.md", "if", """{"+":[{"var":"input.include_billing.value"},1]}"""));

        var refusal = Assert.Single(result.Refusals);
        Assert.Contains("(boolean | null)", refusal.Sentence, StringComparison.Ordinal);
    }

    /// <summary>
    /// Without a scope from outside, <see cref="PackageScope"/> defers the other roots; <see cref="SourceCheck.Run"/>
    /// gives area 05's (#1502), which resolves them.
    /// </summary>
    [Fact]
    public void OtherRoots_AreDeferred()
    {
        var files = Set(SourceFixture.Minimal(), "nodes/plan.md", "if",
            """{"===":[{"var":"node.anything.value.x"},{"var":"item.value.kind"}]}""");
        var parsed = SourceParser.Parse(files, Registry.Value.Names);
        var types = SourceTypes.Check(parsed.Package!, Registry.Value);

        var deferred = SourceExpressions.Check(parsed.Package!, types.Types!);
        Assert.Empty(deferred.Refusals);

        var resolved = Run(files);
        Assert.Equal(["S03-014", "S03-014"], resolved.Refusals.Select(r => r.Id));
    }

    [Fact]
    public void InsideAFunction_InputsAreDeferred()
    {
        const string file = "functions/draft_section/standard_draft.md";

        // PackageScope defers an input read inside a function; area 05 says S04-050 for it (#1502).
        var files = Set(SourceFixture.Full(), file, "if", """{"var":"input.nope.value"}""");
        var parsed = SourceParser.Parse(files, Registry.Value.Names);
        var deferred = SourceExpressions.Check(parsed.Package!, SourceTypes.Check(parsed.Package!, Registry.Value).Types!);
        Assert.Empty(deferred.Refusals);
        var site = Assert.Single(deferred.Expressions!.Sites, s => s.Context.Site.Path == file);
        Assert.Equal("draft_section", site.Context.Function!.Id);
        var input = Run(files);
        Assert.Equal("S04-050", Assert.Single(input.Refusals).Id);

        var profile = Run(Set(SourceFixture.Full(), file, "if", """{"var":"profile.name"}"""));
        var refusal = Assert.Single(profile.Refusals);
        Assert.Equal("S03-018", refusal.Id);
        Assert.Equal(file, refusal.Path);
        Assert.StartsWith("Node 'standard_draft' in function 'draft_section' reads profile.name;", refusal.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void AComputedNode_IsHeldToItsFormat()
    {
        var result = Run(Set(SourceFixture.Full(), "nodes/section_count.md", "expression",
            """{"blank":[{"var":"input.consult_draft.content"}]}"""));

        var refusal = Assert.Single(result.Refusals);
        Assert.Equal("S03-026", refusal.Id);
        Assert.Equal("nodes/section_count.md", refusal.Path);
        Assert.Equal("Computed node 'section_count' yields boolean; it declares number.", refusal.Sentence);
    }

    [Fact]
    public void RunFactReads_Warn()
    {
        var files = SourceFixture.Minimal().WithJson("manifest.json", m => m["inputs"]!["date_of_birth"] = JsonNode.Parse(
            """{ "label": "Date of birth", "description": null, "format": "date", "required": true, "channel": null }"""));
        Set(files, "nodes/plan.md", "if", """{">=":[{"years_between":[{"var":"input.date_of_birth.value"},{"var":"run.date"}]},65]}""");

        var decision = Run(files);

        Assert.True(decision.Refusals.Count == 0, Said(decision));
        var warning = Assert.Single(decision.Expressions!.Warnings);
        Assert.Equal("S03-037", warning.Id);
        Assert.Equal("nodes/plan.md", warning.Path);
        Assert.Equal("Node 'plan' if reads run.date; its decision can differ between runs with the same inputs.", warning.Sentence);
        V31Contract.AssertSentence(warning);

        // A computed node reading a run fact; a fact read twice is said once.
        var stamped = SourceFixture.Minimal()
            .With("nodes/index.json", SourceFixture.Index(["plan", "stamp"]))
            .With("nodes/stamp.md", SourceFixture.Md(SourceFixture.Node("computed", "Stamping the run", "\"string\"", true, false,
                "\"if\": null, \"expression\": { \"if\": [{ \"blank\": [{ \"var\": \"run.id\" }] }, \"none\", { \"var\": \"run.id\" }] }"), ""));

        var value = Run(stamped);

        Assert.True(value.Refusals.Count == 0, Said(value));
        warning = Assert.Single(value.Expressions!.Warnings);
        Assert.Equal("S03-038", warning.Id);
        Assert.Equal("nodes/stamp.md", warning.Path);
        Assert.Equal(
            "Computed node 'stamp' reads run.id, which is run-dependent; its value can differ between runs with the same inputs.",
            warning.Sentence);
        V31Contract.AssertSentence(warning);
    }

    [Fact]
    public void AScopeFromOutside_IsUsed()
    {
        var full = Run(SourceFixture.Full());

        var result = SourceExpressions.Check(full.Parse.Package!, full.Types!, new JudgeUnknown());

        var refusal = Assert.Single(result.Refusals);
        Assert.Equal("S03-014", refusal.Id);
        Assert.Equal("nodes/refine/index.json", refusal.Path);
        Assert.Equal("While 'refine' reads node.judge.value.verdict, which doesn't exist here.", refusal.Sentence);
        Assert.Null(result.Expressions);
    }

    [Fact]
    public void TypesRefused_ExpressionsNotRun()
    {
        var files = SourceFixture.Minimal().WithJson("nodes/plan.md", h => h["format"] = "check_result");
        Set(files, "deliverables/note.md", "if", Equal);

        var result = Run(files);

        Assert.Equal("S02-012", Assert.Single(result.Refusals).Id);
        Assert.Null(result.Types);
        Assert.Null(result.Expressions);
    }

    [Fact]
    public void Refusals_AreOrderedByPath_AndSaidOnce()
    {
        var files = SourceFixture.Full();
        Set(files, "nodes/plan.md", "if", Equal);
        Set(files, "deliverables/note.md", "if", Equal);
        Set(files, "nodes/billing_gate/index.json", "if", """{"!=":[1,1]}""");

        var result = Run(files);

        Assert.Equal(
            ["deliverables/note.md", "nodes/billing_gate/index.json", "nodes/plan.md"],
            result.Refusals.Select(r => r.Path));
    }

    [Fact]
    public void Check_TakesNoNull()
    {
        var full = Run(SourceFixture.Full());
        Assert.Throws<ArgumentNullException>(() => SourceExpressions.Check(null!, full.Types!));
        Assert.Throws<ArgumentNullException>(() => SourceExpressions.Check(full.Parse.Package!, null!));
    }

    /// <summary>Area 05's place: a scope that knows no <c>node.judge</c> and defers everything else.</summary>
    private sealed class JudgeUnknown : IExpressionScopes
    {
        public IExpressionScope For(SiteContext context) => new Scope();

        private sealed class Scope : IExpressionScope
        {
            public ScopeHead Head(IReadOnlyList<string> segments) =>
                segments is ["node", "judge", ..]
                    ? new ScopeHead(ScopeHeadKind.Unknown, 0, null, false, null)
                    : new ScopeHead(ScopeHeadKind.Deferred, 0, null, false, null);
        }
    }
}
