using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using static Consultologist.Api.Tests.V31Source.SourceFixture;
using static Consultologist.Api.Tests.V31Templates.TemplateFixture;

namespace Consultologist.Api.Tests.V31Templates;

/// <summary>
/// #1508 (ruled by the owner, Q5 of 2026-11-26; R17): a template read that steps through a nullable object before its last
/// step fails its render when that object is null (<see cref="V31Run.NullableObjectRunTests"/>), so publish refuses it
/// (A11-009) unless it stands inside <c>{{ if &lt;that object&gt; }}</c> or <c>{{ if &lt;that object&gt; != null }}</c>.
/// The three kinds of nullable object: an optional object input left unset, a nullable property that is an object, and a
/// node that may be skipped, read as <c>.value.&lt;member&gt;</c>; in a prompt and in a deliverable. Content is fictional
/// (Margaret Okafor, Dr A. Sample).
/// </summary>
public class NullableReadTests
{
    /// <summary>Each kind of nullable object: the read through it and the object it steps through.</summary>
    public static TheoryData<string, string, string> Kinds => new()
    {
        { "an unset optional object input", "input.patient.value.name", "input.patient.value" },
        { "a nullable property that is an object", "input.referrer.value.contact.phone", "input.referrer.value.contact" },
        { "a node that may be skipped", "node.verdict_check.value.verdict", "node.verdict_check.value" },
    };

    [Theory]
    [MemberData(nameof(Kinds))]
    public void AMemberReadThroughANullableObject_IsRefused(string kind, string read, string through)
    {
        foreach (var site in new[] { "prompt", "deliverable" })
        {
            var (files, path, subject, line) = NullableFixture.At(site, "{{ " + read + " }}");

            var refusals = Publish(files);

            var refusal = Assert.Single(refusals);
            V31Contract.AssertSentence(refusal);
            Assert.Equal(("A11-009", path), (refusal.Id, refusal.Path));
            Assert.True(
                $"{subject} reads {read} at line {line} through {through}, which can be null. Put the read inside {{{{ if {through} }}}}." == refusal.Sentence,
                $"{kind}, {site}: {refusal.Sentence}");
        }
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void AMemberReadThroughANullableObject_InsideAnIfOnThatObject_IsAccepted(string kind, string read, string through)
    {
        foreach (var site in new[] { "prompt", "deliverable" })
        {
            foreach (var test in new[] { through, through + " != null" })
            {
                var (files, _, _, _) = NullableFixture.At(site, $"{{{{ if {test} }}}}{{{{ {read} }}}}{{{{ end }}}}");

                var refusals = Publish(files);

                Assert.True(refusals.Count == 0, $"{kind}, {site}, if {test}: " + Said(refusals));
            }
        }
    }

    [Fact]
    public void AnElseBranch_IsNotGuarded()
    {
        var (files, _, _, _) = NullableFixture.At("prompt",
            "{{ if input.patient.value }}Known.{{ else }}{{ input.patient.value.name }}{{ end }}");

        Assert.Equal(["A11-009"], Publish(files).Select(r => r.Id));
    }

    [Fact]
    public void AnElseIfBranch_IsGuardedByItsOwnTestOnly()
    {
        var (guarded, _, _, _) = NullableFixture.At("prompt",
            "{{ if input.patient.value }}{{ input.patient.value.name }}{{ else if input.referrer.value.contact }}{{ input.referrer.value.contact.phone }}{{ end }}");
        var unguarded = NullableFixture.At("prompt",
            "{{ if input.patient.value }}Known.{{ else if input.referrer.value.contact }}{{ input.patient.value.name }}{{ end }}").Files;

        Assert.Empty(Publish(guarded));
        Assert.Equal(["A11-009"], Publish(unguarded).Select(r => r.Id));
    }

    [Theory]
    [InlineData("{{ if input.referrer.value }}{{ input.patient.value.name }}{{ end }}")]
    [InlineData("{{ if input.patient.value.name }}{{ input.patient.value.name }}{{ end }}")]
    [InlineData("{{ if !input.patient.value }}{{ input.patient.value.name }}{{ end }}")]
    [InlineData("{{ if input.patient.value || input.referrer.value }}{{ input.patient.value.name }}{{ end }}")]
    [InlineData("{{ if input.patient.value == null }}{{ input.patient.value.name }}{{ end }}")]
    public void AGuardOnADifferentPath_OrNotAssertingIt_IsNoGuard(string body)
    {
        var (files, _, _, _) = NullableFixture.At("prompt", body);

        Assert.Contains("A11-009", Publish(files).Select(r => r.Id));
    }

    [Fact]
    public void ANestedObject_NeedsItsOwnGuard_OrTheConjunction()
    {
        // input.patient is optional and its contact is a nullable object: two objects that can be null.
        const string read = "input.patient.value.contact.phone";
        var outer = NullableFixture.At("prompt", $"{{{{ if input.patient.value }}}}{{{{ {read} }}}}{{{{ end }}}}");
        var both = NullableFixture.At("prompt",
            $"{{{{ if input.patient.value }}}}{{{{ if input.patient.value.contact }}}}{{{{ {read} }}}}{{{{ end }}}}{{{{ end }}}}").Files;
        var conjunction = NullableFixture.At("prompt",
            $"{{{{ if input.patient.value && input.patient.value.contact != null }}}}{{{{ {read} }}}}{{{{ end }}}}").Files;
        var inner = NullableFixture.At("prompt", $"{{{{ if input.patient.value.contact }}}}{{{{ {read} }}}}{{{{ end }}}}").Files;

        var refusal = Assert.Single(Publish(outer.Files));
        Assert.Equal(
            $"Node 'draft' reads {read} at line 2 through input.patient.value.contact, which can be null. Put the read inside {{{{ if input.patient.value.contact }}}}.",
            refusal.Sentence);
        Assert.Empty(Publish(both));
        Assert.Empty(Publish(conjunction));
        // The guard's own test reads through input.patient.value, which nothing guards.
        var said = Publish(inner);
        Assert.Equal(["A11-009", "A11-009"], said.Select(r => r.Id));
        Assert.All(said, r => Assert.Contains(" through input.patient.value, ", r.Sentence, StringComparison.Ordinal));
    }

    [Fact]
    public void AForSource_ReadThroughANullableObject_IsRefusedUnlessGuarded()
    {
        var bare = NullableFixture.At("prompt", "{{ for a in input.patient.value.allergies }}{{ a }}\n{{ end }}");
        var guarded = NullableFixture.At("prompt",
            "{{ if input.patient.value }}{{ for a in input.patient.value.allergies }}{{ a }}\n{{ end }}{{ end }}").Files;

        var refusal = Assert.Single(Publish(bare.Files));
        Assert.Equal(
            "Node 'draft' reads input.patient.value.allergies at line 2 through input.patient.value, which can be null. Put the read inside {{ if input.patient.value }}.",
            refusal.Sentence);
        Assert.Empty(Publish(guarded));
    }

    [Fact]
    public void ARequiredObject_NeedsNoGuard()
    {
        // input.referrer is required; only its contact can be null.
        var (files, _, _, _) = NullableFixture.At("prompt", "{{ input.referrer.value.name }}");

        Assert.Empty(Publish(files));
    }

    [Theory]
    [InlineData("{{ input.referrer.value.nickname }}")]
    [InlineData("{{ input.patient.content }}")]
    [InlineData("{{ node.verdict_check.content }}")]
    [InlineData("{{ if input.patient.value }}{{ input.patient.value.nickname }}{{ end }}")]
    public void TheLastStepBeingNullable_IsNotThisRule(string body)
    {
        // A nullable value read as the last step renders nothing by the dialect's own rule (part 05 § 2.3).
        var (files, _, _, _) = NullableFixture.At("prompt", body);

        Assert.Empty(Publish(files));
    }

    [Fact]
    public void AGuardOnARequiredObject_IsStillATestOfData()
    {
        // The new body-if fact is whether a nullable object is there; testing a value that is always there stays S05-007.
        var (files, _, _, _) = NullableFixture.At("prompt", "{{ if input.referrer.value }}{{ input.referrer.value.name }}{{ end }}");

        Assert.Equal(["S05-007"], Publish(files).Select(r => r.Id));
    }

    [Theory]
    // A union whose every member is an object (or null) is an object that can be null: testing it is the guard.
    [InlineData("{ \"type\": \"object\", \"required\": [\"ward\"], \"additionalProperties\": false, \"properties\": { \"ward\": { \"type\": \"string\", \"title\": \"Ward\" } } }", true)]
    // A union with a member that is no object is a data value: testing it stays S05-007.
    [InlineData("{ \"type\": \"string\" }", false)]
    public void APresenceTest_OnAUnion_IsTheGuardOnlyWhenEveryMemberIsAnObject(string other, bool presence)
    {
        var where = "{ \"type\": \"object\", \"required\": [\"where\"], \"additionalProperties\": false, \"properties\": { \"where\": { \"title\": \"Where\", \"anyOf\": ["
            + "{ \"type\": \"object\", \"required\": [\"clinic\"], \"additionalProperties\": false, \"properties\": { \"clinic\": { \"type\": \"string\", \"title\": \"Clinic\" } } }, "
            + other + ", { \"type\": \"null\" }] } } }";
        var (files, _, _, _) = NullableFixture.At("prompt", "{{ if input.stay.value.where }}Known.{{ end }}");
        files.WithJson("manifest.json", m => m["inputs"]!["stay"] = JsonNode.Parse(
            $"{{ \"label\": \"Stay\", \"description\": null, \"format\": {where}, \"required\": true, \"channel\": null }}"));

        var said = Publish(files);

        Assert.Equal(presence ? [] : ["S05-007"], said.Select(r => r.Id));
    }

    [Theory]
    [InlineData("{ \"!==\": [{ \"var\": \"input.patient.value.name\" }, null] }", true)]
    [InlineData("{ \"===\": [{ \"var\": \"input.patient.value.name\" }, \"Margaret Okafor\"] }", true)]
    [InlineData("{ \"and\": [{ \"var\": \"input.include_billing.value\" }, { \"!==\": [{ \"var\": \"input.patient.value\" }, null] }] }", true)]
    [InlineData("{ \"!==\": [{ \"var\": \"input.patient.value.name\" }, \"Margaret Okafor\"] }", false)]
    [InlineData("{ \"or\": [{ \"var\": \"input.include_billing.value\" }, { \"!==\": [{ \"var\": \"input.patient.value\" }, null] }] }", false)]
    [InlineData("{ \"!==\": [{ \"var\": \"input.referrer.value\" }, null] }", false)]
    public void APartsOwnIf_GuardsItsText_WhenItAssertsTheObject(string rule, bool guards)
    {
        // A part's text holds no if (A07-002); its header if decides whether it renders at all, so it is the guard there.
        var (files, _, _, _) = NullableFixture.At("prompt", "{{ part.about }}");
        files.WithJson("nodes/draft.md", h => h["parts"] = JsonNode.Parse(
            $"{{ \"about\": {{ \"if\": {rule}, \"text\": [\"Patient: {{{{ input.patient.value.name }}}}\"] }} }}"));

        var said = Publish(files);

        if (guards)
        {
            Assert.True(said.Count == 0, Said(said));
            return;
        }

        var refusal = Assert.Single(said);
        Assert.Equal(
            "Part 'about' of node 'draft' reads input.patient.value.name at line 1 through input.patient.value, which can be null. Put the read inside {{ if input.patient.value }}.",
            refusal.Sentence);
    }

    [Fact]
    public void AGuardOnAForVariable_GuardsItsOwnBinding()
    {
        var (files, _, _, _) = NullableFixture.At("prompt",
            "{{ for e in input.visits.value }}{{ if e.patient }}{{ for s in input.stays.value }}{{ e.patient.name }}\n{{ end }}{{ end }}{{ end }}");

        Assert.Empty(Publish(files));
    }

    [Theory]
    // A guard inside the loop on the loop's own variable.
    [InlineData("{{ for e in input.visits.value }}{{ if e.patient }}{{ e.patient.name }}{{ end }}\n{{ end }}")]
    // An inner loop that binds the name again, with its own guard on its own variable.
    [InlineData("{{ for e in input.visits.value }}{{ if e.patient }}{{ for e in input.stays.value }}{{ if e.patient }}{{ e.patient.name }}{{ end }}\n{{ end }}{{ end }}{{ end }}")]
    // A loop variable named like a root, guarded on the loop's own item.
    [InlineData("{{ for input in input.visits.value }}{{ if input.patient }}{{ input.patient.name }}{{ end }}\n{{ end }}")]
    public void AGuardInsideTheLoop_OnTheLoopsOwnVariable_IsAccepted(string body)
    {
        var (files, _, _, _) = NullableFixture.At("prompt", body);

        Assert.True(Publish(files).Count == 0, Said(Publish(files)));
    }

    [Theory]
    // An outer guard on e, and an inner loop that binds e again.
    [InlineData("{{ for e in input.visits.value }}{{ if e.patient }}{{ for e in input.stays.value }}{{ e.patient.name }}\n{{ end }}{{ end }}{{ end }}", "e.patient")]
    // A body guard on the input patient, and a loop variable named input whose item has a patient too.
    [InlineData("{{ if input.patient.value }}{{ for input in input.charts.value }}{{ input.patient.value.name }}\n{{ end }}{{ end }}", "input.patient.value")]
    public void AnOuterGuard_UnderALoopThatBindsItsRootAgain_IsNoGuard(string body, string through)
    {
        var (files, _, _, _) = NullableFixture.At("prompt", body);

        var refusal = Assert.Single(Publish(files));
        Assert.Equal("A11-009", refusal.Id);
        Assert.Contains($" through {through}, which can be null.", refusal.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void AHeaderGuard_GuardsNothingUnderAForThatBindsItsRoot()
    {
        // The header rule reads the input patient; under `for input in …` the read's input is the loop's item.
        var (files, _, _, _) = NullableFixture.At("prompt",
            "{{ for input in input.visits.value }}{{ input.patient.value.name }}\n{{ end }}");
        files.WithJson("nodes/draft.md", h => h["if"] = JsonNode.Parse("{ \"!==\": [{ \"var\": \"input.patient.value.name\" }, null] }"));
        files.With("deliverables/note.md", Md(Deliverable("Consultation note", "\"markdown_block\"", "null", "hold"), "{{ node.plan.content }}\n"));

        var refusal = Assert.Single(Publish(files));
        Assert.Equal(
            "Node 'draft' reads input.patient.value.name at line 2 through input.patient, which can be null. Put the read inside {{ if input.patient }}.",
            refusal.Sentence);
    }

    [Fact]
    public void ASnippetsMacrosAndDeliverablesOwnIf_GuardTheirText()
    {
        const string rule = "{ \"!==\": [{ \"var\": \"input.patient.value\" }, null] }";
        var snippet = NullableFixture.At("prompt", "{{ snippet.who.content }}").Files
            .WithSnippet("who", "Patient: {{ input.patient.value.name }}\n")
            .WithJson("snippets/who.md", h => h["if"] = JsonNode.Parse(rule));
        var macro = NullableFixture.At("prompt", "{{~ macro.who }}").Files
            .WithMacro("who", "Patient: {{ input.patient.value.name }}\n")
            .WithJson("macros/who.md", h => h["if"] = JsonNode.Parse(rule));
        var deliverable = NullableFixture.At("deliverable", "{{ input.patient.value.name }}").Files
            .WithJson("deliverables/note.md", h => h["if"] = JsonNode.Parse(rule));

        foreach (var (name, files) in new[] { ("snippet", snippet), ("macro", macro), ("deliverable", deliverable) })
        {
            Assert.True(Publish(files).Count == 0, name + ": " + Said(Publish(files)));
            var header = name == "deliverable" ? "deliverables/note.md" : $"{name}s/who.md";
            var other = new Dictionary<string, byte[]>(files)
                .WithJson(header, h => h["if"] = JsonNode.Parse("{ \"!==\": [{ \"var\": \"input.referrer.value.contact\" }, null] }"));
            Assert.Equal(["A11-009"], Publish(other).Select(r => r.Id));
            var unguarded = files.WithJson(header, h => h["if"] = null);
            Assert.Equal(["A11-009"], Publish(unguarded).Select(r => r.Id));
        }
    }

    // ---- pass.previous (form 3) -------------------------------------------------------------------

    /// <summary>
    /// <see cref="ResolveFixture.NestedNote"/> with <c>draft_summary</c>'s part <c>revision</c> of <paramref name="rule"/>
    /// whose text reads <c>pass.previous.node.judge.value.verdict</c>; <c>judge</c> given <paramref name="judgeIf"/>.
    /// </summary>
    private static Dictionary<string, byte[]> Revision(string rule, string judgeIf = "null") =>
        ResolveFixture.NestedNote()
            .With("nodes/refine/draft_summary.md", Agent("Drafting the summary", Plain,
                "Summarise the note in two sentences for Dr A. Sample.\n{{ part.revision }}\n"))
            .WithJson("nodes/refine/draft_summary.md", h => h["parts"] = JsonNode.Parse(
                $"{{ \"revision\": {{ \"if\": {rule}, \"text\": [\"The reviewer said: {{{{ pass.previous.node.judge.value.verdict }}}}\"] }} }}"))
            .WithJson("nodes/refine/judge.md", h => h["if"] = JsonNode.Parse(judgeIf));

    private const string Through = " through pass.previous.node.judge.value, which can be null.";

    [Theory]
    [InlineData("{ \">\": [{ \"var\": \"pass.number\" }, 1] }")]
    [InlineData("{ \">=\": [{ \"var\": \"pass.number\" }, 2] }")]
    [InlineData("{ \"and\": [{ \">\": [{ \"var\": \"pass.number\" }, 2] }, { \">\": [{ \"var\": \"node.section_count.value\" }, 0] }] }")]
    public void APassPastItsFirst_GuardsThePreviousPassOfANodeThatAlwaysRuns(string rule)
    {
        var files = Revision(rule);

        Assert.True(Publish(files).Count == 0, Said(Publish(files)));
    }

    [Theory]
    [InlineData("{ \">=\": [{ \"var\": \"pass.number\" }, 1] }")]
    [InlineData("{ \">\": [{ \"var\": \"pass.number\" }, 0] }")]
    [InlineData("{ \"or\": [{ \">\": [{ \"var\": \"pass.number\" }, 1] }, { \">\": [{ \"var\": \"node.section_count.value\" }, 0] }] }")]
    // Other comparisons are no guard, even where they exclude the first pass: only `>` and `>=` with the count on the left.
    [InlineData("{ \"<\": [1, { \"var\": \"pass.number\" }] }")]
    [InlineData("{ \"===\": [{ \"var\": \"pass.number\" }, 2] }")]
    [InlineData("{ \"!==\": [{ \"var\": \"pass.number\" }, 1] }")]
    [InlineData("{ \">\": [{ \"var\": \"pass.number\" }, 0.5] }")]
    public void APassThatMayBeTheFirst_GuardsNothing(string rule)
    {
        var refusal = Assert.Single(Publish(Revision(rule)));

        Assert.Equal("A11-009", refusal.Id);
        Assert.Contains(Through, refusal.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void APassPastItsFirst_DoesNotGuardANodeWithItsOwnIf()
    {
        // judge may be skipped in any pass, so its previous value can be null on the second pass too.
        var files = Revision("{ \">\": [{ \"var\": \"pass.number\" }, 1] }",
            "{ \"!\": [{ \"blank\": [{ \"var\": \"node.draft_summary.content\" }] }] }");

        var said = Publish(files);
        var refusal = Assert.Single(said, r => r.Id == "A11-009");
        Assert.Contains(Through, refusal.Sentence, StringComparison.Ordinal);
    }

    /// <summary><see cref="ResolveFixture.NestedNote"/> with <paramref name="prompt"/> as <c>draft_summary</c>'s prompt.</summary>
    private static Dictionary<string, byte[]> Drafting(string prompt) =>
        ResolveFixture.NestedNote().With("nodes/refine/draft_summary.md", Agent("Drafting the summary", Plain,
            "Summarise the note in two sentences for Dr A. Sample.\n" + prompt + "\n"));

    [Fact]
    public void APreviousPassRead_InABodyIf_IsGuardedInItsBranchOnly()
    {
        var guarded = Drafting("{{ if pass.previous.node.judge.value }}{{ pass.previous.node.judge.value.verdict }}{{ end }}");
        var otherwise = Drafting("{{ if pass.previous.node.judge.value }}Again.{{ else }}{{ pass.previous.node.judge.value.verdict }}{{ end }}");

        Assert.True(Publish(guarded).Count == 0, Said(Publish(guarded)));
        var refusal = Assert.Single(Publish(otherwise));
        Assert.Equal("A11-009", refusal.Id);
        Assert.Contains(Through, refusal.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void APassCountInABodyIf_IsNoGuard()
    {
        // A body if may not test a run fact (S05-007), and the count guards only as a header rule.
        var said = Publish(Drafting("{{ if pass.number > 1 }}{{ pass.previous.node.judge.value.verdict }}{{ end }}"));

        Assert.Contains(said, r => r.Id == "S05-007");
        var refusal = Assert.Single(said, r => r.Id == "A11-009");
        Assert.Contains(Through, refusal.Sentence, StringComparison.Ordinal);
    }

    /// <summary>
    /// The while <c>rounds</c> (two passes) around <c>verdict_round</c> (a <c>verdict</c>) and <see cref="ResolveFixture.NestedNote"/>'s
    /// <c>refine</c>, whose <c>draft_summary</c> has the part <c>revision</c> of <paramref name="rule"/> reading <paramref name="read"/>.
    /// </summary>
    private static Dictionary<string, byte[]> Rounds(string rule, string read)
    {
        var nested = ResolveFixture.NestedNote();
        var files = nested.Where(f => !f.Key.StartsWith("nodes/refine/", StringComparison.Ordinal))
            .ToDictionary(f => f.Key, f => f.Value);
        foreach (var (path, bytes) in nested.Where(f => f.Key.StartsWith("nodes/refine/", StringComparison.Ordinal)))
            files["nodes/rounds/" + path["nodes/".Length..]] = bytes;
        return files
            .WithJson("nodes/index.json", i => i["items"] = JsonNode.Parse("[\"plan\", \"section_count\", \"sections\", \"rounds\"]"))
            .With("nodes/rounds/index.json", Json(SourceFixture.Node("while", "Rounds", Plain, false, true,
                "\"while\": { \"<\": [{ \"var\": \"pass.number\" }, 2] }, \"max\": 2, \"until\": null, \"checkFirst\": false, "
                + "\"nodes\": [\"verdict_round\", \"refine\"], \"ret\": \"node.rounds.refine\"")))
            .WithJson("nodes/rounds/refine/index.json", h => h["ret"] = "node.rounds.refine.draft_summary")
            .With("nodes/rounds/refine/judge.md", Agent("Judging the summary", "\"verdict\"",
                "Say whether the summary is good or needs another pass.\n\n{{ node.draft_summary.content }}\n{{ node.verdict_round.content }}\n"))
            .With("nodes/rounds/verdict_round.md", Agent("Judging the round", "\"verdict\"", "Judge the round for Dr A. Sample.\n"))
            .With("nodes/rounds/refine/draft_summary.md", Agent("Drafting the summary", Plain,
                "Summarise the note in two sentences for Dr A. Sample.\n{{ part.revision }}\n"))
            .WithJson("nodes/rounds/refine/draft_summary.md", h => h["parts"] = JsonNode.Parse(
                $"{{ \"revision\": {{ \"if\": {rule}, \"text\": [\"Said before: {{{{ {read} }}}}\"] }} }}"))
            .With("deliverables/note.md", Md(Deliverable("Nested consultation note", "\"markdown_block\"", "null", "hold"),
                "## Summary\n\n{{ node.rounds.content }}\n"));
    }

    private const string Inner = "pass.previous.node.judge.value.verdict";

    private const string Outer = "pass.parent.previous.node.verdict_round.value.verdict";

    [Theory]
    [InlineData("pass.number", Inner, true)]
    [InlineData("pass.parent.number", Outer, true)]
    [InlineData("pass.parent.number", Inner, false)]
    [InlineData("pass.number", Outer, false)]
    public void APassPastItsFirst_GuardsOnlyItsOwnWhilesPreviousPass(string counted, string read, bool guards)
    {
        var said = Publish(Rounds($"{{ \">\": [{{ \"var\": \"{counted}\" }}, 1] }}", read));

        if (guards)
        {
            Assert.True(said.Count == 0, Said(said));
            return;
        }

        var refusal = Assert.Single(said);
        Assert.Equal("A11-009", refusal.Id);
        Assert.StartsWith($"Part 'revision' of node 'draft_summary' reads {read} at line 1 through ", refusal.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void ANodesOwnIf_GuardsItsPrompt()
    {
        var (files, _, _, _) = NullableFixture.At("prompt", "{{ input.patient.value.name }}");
        files.WithJson("nodes/draft.md", h => h["if"] = JsonNode.Parse("{ \"!==\": [{ \"var\": \"input.patient.value\" }, null] }"));
        files.With("deliverables/note.md", Md(Deliverable("Consultation note", "\"markdown_block\"", "null", "hold"), "{{ node.plan.content }}\n"));

        Assert.Empty(Publish(files));
    }

    private static IReadOnlyList<SourceRefusal> Publish(Dictionary<string, byte[]> files) =>
        CompiledPackage.Publish(files, ResolveFixture.Published(), CompiledGoldenTests.Fixed).Refusals;
}

/// <summary>
/// #1508 (Q5, R17): the package the nullable-object tests read: <see cref="TemplateFixture.Package"/> with the optional
/// object input <c>patient</c> (<c>name</c>, a nullable <c>nickname</c>, a nullable object <c>contact</c>, a list
/// <c>allergies</c>), the required object input <c>referrer</c> (<c>name</c>, a nullable <c>nickname</c>, a nullable
/// object <c>contact</c>), the optional boolean <c>include_billing</c>, the agent <c>verdict_check</c> (a <c>verdict</c>),
/// run only when billing is included, the agent <c>draft</c> and the deliverable <c>note</c>.
/// </summary>
internal static class NullableFixture
{
    private const string Contact =
        "\"contact\": { \"title\": \"Contact\", \"anyOf\": [{ \"type\": \"object\", \"required\": [\"phone\"], \"additionalProperties\": false, "
        + "\"properties\": { \"phone\": { \"type\": \"string\", \"title\": \"Phone\" } } }, { \"type\": \"null\" }] }";

    private static string Person(string extra) =>
        "{ \"type\": \"object\", \"required\": [\"name\", \"nickname\", \"contact\"" + (extra.Length > 0 ? ", \"allergies\"" : "") + "], \"additionalProperties\": false, "
        + "\"properties\": { \"name\": { \"type\": \"string\", \"title\": \"Name\" }, "
        + "\"nickname\": { \"type\": [\"string\", \"null\"], \"title\": \"Nickname\" }, " + Contact + extra + " } }";

    /// <summary>A list of visits, each with a nullable object <c>patient</c> (its <c>name</c>, and a <c>value</c> holding a <c>name</c>).</summary>
    public const string Visits =
        "{ \"type\": \"array\", \"items\": { \"type\": \"object\", \"required\": [\"patient\"], \"additionalProperties\": false, \"properties\": { "
        + "\"patient\": { \"title\": \"Patient\", \"anyOf\": [{ \"type\": \"object\", \"required\": [\"name\", \"value\"], \"additionalProperties\": false, "
        + "\"properties\": { \"name\": { \"type\": \"string\", \"title\": \"Name\" }, \"value\": { \"type\": \"object\", \"title\": \"Value\", "
        + "\"required\": [\"name\"], \"additionalProperties\": false, \"properties\": { \"name\": { \"type\": \"string\", \"title\": \"Name\" } } } } }, "
        + "{ \"type\": \"null\" }] } } } }";

    /// <summary>A list of charts, each with an object <c>patient</c> whose <c>value</c> is a nullable object (its <c>name</c>): read as <c>input</c> in a loop, the same written path as the input patient's.</summary>
    public const string Charts =
        "{ \"type\": \"array\", \"items\": { \"type\": \"object\", \"required\": [\"patient\"], \"additionalProperties\": false, \"properties\": { "
        + "\"patient\": { \"type\": \"object\", \"title\": \"Patient\", \"required\": [\"value\"], \"additionalProperties\": false, \"properties\": { "
        + "\"value\": { \"title\": \"Value\", \"anyOf\": [{ \"type\": \"object\", \"required\": [\"name\"], \"additionalProperties\": false, "
        + "\"properties\": { \"name\": { \"type\": \"string\", \"title\": \"Name\" } } }, { \"type\": \"null\" }] } } } } } }";

    private const string Allergies = ", \"allergies\": { \"type\": \"array\", \"title\": \"Allergies\", \"items\": { \"type\": \"string\" } }";

    /// <summary>The package with <paramref name="prompt"/> as <c>draft</c>'s prompt and <paramref name="deliverable"/> as <c>note</c>'s body.</summary>
    public static Dictionary<string, byte[]> Files(string prompt, string deliverable) =>
        Package()
            .WithJson("manifest.json", m =>
            {
                var inputs = m["inputs"]!.AsObject();
                inputs["patient"] = Input("Patient", Person(Allergies), required: false);
                inputs["referrer"] = Input("Referrer", Person(""), required: true);
                inputs["include_billing"] = Input("Include billing", "\"boolean\"", required: false);
                inputs["visits"] = Input("Visits", Visits, required: false);
                inputs["stays"] = Input("Stays", Visits, required: false);
                inputs["charts"] = Input("Charts", Charts, required: false);
            })
            .Listed("nodes/index.json", "verdict_check")
            .With("nodes/verdict_check.md", Md(SourceFixture.Node("agent", "Judging the draft", "\"verdict\"", false, true,
                "\"if\": { \"===\": [{ \"var\": \"input.include_billing.value\" }, true] }, \"onInvalid\": \"fail\", \"failIfEmpty\": null, "
                + "\"parts\": null, \"ordered\": false"),
                "Judge the draft for Dr A. Sample.\n\n{{ input.consult_draft.content }}\n"))
            .WithAgent("draft", prompt)
            .WithDeliverable(deliverable);

    /// <summary>
    /// The package with <paramref name="body"/> on line 2 of <c>draft</c>'s prompt (<paramref name="site"/> <c>prompt</c>) or
    /// on line 1 of the deliverable <c>note</c> (<c>deliverable</c>); the file, the subject and the line of the body.
    /// </summary>
    public static (Dictionary<string, byte[]> Files, string Path, string Subject, int Line) At(string site, string body) => site switch
    {
        "prompt" => (Files("Draft the note for Margaret Okafor.\n" + body + "\n", "{{ node.draft.content }}\n"), "nodes/draft.md", "Node 'draft'", 2),
        _ => (Files("Draft the note for Margaret Okafor.\n{{ node.plan.content }}\n", body + "\n{{ node.draft.content }}\n"), "deliverables/note.md", "Deliverable 'note'", 1),
    };

    private static JsonNode Input(string label, string format, bool required) => JsonNode.Parse(
        $"{{ \"label\": \"{label}\", \"description\": null, \"format\": {format}, \"required\": {(required ? "true" : "false")}, \"channel\": null }}")!;
}
