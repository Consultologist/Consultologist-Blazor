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
