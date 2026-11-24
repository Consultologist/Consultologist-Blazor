using System.Text.Json.Nodes;
using Consultologist.Api.Auth;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Run;
using Consultologist.Api.Tests.V31Source;
using Consultologist.Api.Tests.V31Templates;
using Consultologist.Api.V31;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 §§ 1.2 step 6, 5.3, 7.2; part 02 § 8; part 06 § 10.4; R1, R4, R5; D6): what a 31
/// job's start checks, as the Create page will: the inputs against the plan (required, blank, formats, numeric enums, empty
/// lists), the profile fields <c>needs.profile</c> lists and the account's time zone, and the run facts that follow from the
/// start; and (#1507, R7) a <c>MacroChoices</c> entry, keyed by a macro, set as the boolean input that macro's <c>if</c> reads.
/// Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class V31StartChecksTests
{
    private static readonly DateTimeOffset LateEvening = new(2026, 11, 17, 3, 30, 0, TimeSpan.Zero);

    private static readonly IReadOnlyDictionary<string, string?> NoProfile = new Dictionary<string, string?>(StringComparer.Ordinal);

    internal const string Draft = "Margaret Okafor, 61, referred for a second opinion on her anaemia. (Fictional.)";

    [Fact]
    public void AMissingRequiredInput_IsRefused()
    {
        var supplied = Valid();
        supplied.Remove("consult_draft");

        var refusal = Assert.Single(Refusals(supplied));

        Assert.Equal("A06-011", refusal.Id);
        Assert.Equal("Input 'consult_draft' is required; fill it in to run this workflow.", refusal.Sentence);
        V31Contract.AssertSentence(refusal);
    }

    [Fact]
    public void ARequiredInputSuppliedNull_IsRefused_AsMissing()
    {
        var supplied = Valid();
        supplied["sections"] = null;

        Assert.Equal("A06-011", Assert.Single(Refusals(supplied)).Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("\n\t")]
    public void ABlankRequiredText_IsRefused(string blank)
    {
        var supplied = Valid();
        supplied["consult_draft"] = blank;
        supplied["code"] = blank;

        var refusals = Refusals(supplied);

        Assert.Equal(["A06-011", "A06-011"], refusals.Select(r => r.Id));
        Assert.Equal(
            ["Input 'consult_draft' is required; fill it in to run this workflow.", "Input 'code' is required; fill it in to run this workflow."],
            refusals.Select(r => r.Sentence));
    }

    [Fact]
    public void ARequiredTextOfOneCharacter_IsAccepted()
    {
        var supplied = Valid();
        supplied["consult_draft"] = "x";
        supplied["code"] = "x";

        var (inputs, refusals) = Check(supplied);

        Assert.Empty(refusals);
        Assert.Equal("x", (string?)inputs!.Inputs["consult_draft"]!["content"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankOptionalText_ReadsNull(string blank)
    {
        var supplied = Valid();
        supplied["notes"] = blank;

        var (inputs, refusals) = Check(supplied);

        Assert.Empty(refusals);
        Assert.Null(inputs!.Inputs.GetValueOrDefault("notes"));
        Assert.False(inputs.Inputs.ContainsKey("notes"));
    }

    [Fact]
    public void AnOptionalInputLeftOut_ReadsNull()
    {
        var (inputs, refusals) = Check(Valid());

        Assert.Empty(refusals);
        Assert.Equal(["consult_draft", "sections", "code"], inputs!.Inputs.Keys);
    }

    [Fact]
    public void AnEmptyRequiredList_IsAccepted()
    {
        // R1: at 31 an empty list is never refused at the start; the loop settles at 0 items
        var supplied = Valid();
        supplied["sections"] = new JsonArray();

        var (inputs, refusals) = Check(supplied);

        Assert.Empty(refusals);
        Assert.Equal("[]", inputs!.Inputs["sections"]!["value"]!.ToJsonString());
    }

    [Fact]
    public void ANumericEnumInput_IsAccepted_AndAnUnlistedNumberRefused()
    {
        // R4: an enum of numbers
        var supplied = Valid();
        supplied["dose"] = 5;
        var (inputs, accepted) = Check(supplied);
        Assert.Empty(accepted);
        Assert.Equal("5", inputs!.Inputs["dose"]!["value"]!.ToJsonString());

        supplied["dose"] = 3;
        var refusal = Assert.Single(Refusals(supplied));

        Assert.Equal("A06-012", refusal.Id);
        Assert.Equal("Input 'dose' was given a value that isn't its format.", refusal.Sentence);
        V31Contract.AssertSentence(refusal);
    }

    [Fact]
    public void AValueOfTheWrongShape_IsRefused()
    {
        var supplied = Valid();
        supplied["include_billing"] = "yes";
        supplied["sections"] = JsonNode.Parse("""[{"title":"History"}]""");

        var refusals = Refusals(supplied);

        Assert.Equal(["A06-012", "A06-012"], refusals.Select(r => r.Id));
        Assert.Equal(
            ["Input 'sections' was given a value that isn't its format.", "Input 'include_billing' was given a value that isn't boolean."],
            refusals.Select(r => r.Sentence));
        Assert.All(refusals, V31Contract.AssertSentence);
    }

    [Theory]
    [InlineData("Seen by **Dr A. Sample** in clinic.", null)]
    [InlineData("Seen by **Dr A. Sample**\nin clinic.", "Input 'headline' was given a value that isn't its format: markdown_inline text sits inside a line and holds no line ending.")]
    [InlineData("Seen by **Dr A. Sample**\r\n", "Input 'headline' was given a value that isn't its format: markdown_inline text sits inside a line and holds no line ending.")]
    public void AMarkdownInlineValue_IsOneLine(string line, string? sentence)
    {
        // #1507, 2026-11-22 (2), 3 (R10) and D21: a markdown_inline value holds no line ending (part 02 § 5), at any depth
        // of an input's value; one that holds one does not fit its format and refuses the start with A10-052.
        var supplied = Valid();
        supplied["headline"] = new JsonObject { ["line"] = line };

        var (inputs, refusals) = V31StartChecks.Inputs(Plan(headline: true), supplied, new JsonObject(), NoProfile);

        if (sentence is null)
        {
            Assert.Empty(refusals);
            Assert.Equal(line, (string?)inputs!.Inputs["headline"]!["value"]!["line"]);
            return;
        }

        Assert.Null(inputs);
        var refusal = Assert.Single(refusals);
        Assert.Equal(("A10-052", sentence), (refusal.Id, refusal.Sentence));
        V31Contract.AssertSentence(refusal);
    }

    [Fact]
    public void AnUndeclaredInput_IsRefused_AsTodaysStartRefusesIt()
    {
        var supplied = Valid();
        supplied["referral_letter"] = Draft;

        var refusal = Assert.Single(Refusals(supplied));

        Assert.Equal("A06-013", refusal.Id);
        Assert.Equal(
            "Unknown input(s) 'referral_letter' (declared: code, consult_draft, dose, include_billing, kind, notes, sections).",
            refusal.Sentence);
        V31Contract.AssertSentence(refusal);
    }

    [Fact]
    public void EachInput_ReadsItsValue_ItsTextAsContent_AndItsChoicesTitle()
    {
        var supplied = Valid();
        supplied["kind"] = "follow_up";
        supplied["include_billing"] = true;

        var (inputs, refusals) = Check(supplied);

        Assert.Empty(refusals);
        Assert.Equal(
            $$"""{"value":"{{Draft}}","content":"{{Draft}}","title":null}""",
            inputs!.Inputs["consult_draft"]!.ToJsonString());
        Assert.Equal("""{"value":"follow_up","content":null,"title":"Follow-up"}""", inputs.Inputs["kind"]!.ToJsonString());
        Assert.Equal("""{"value":true,"content":null,"title":null}""", inputs.Inputs["include_billing"]!.ToJsonString());
        Assert.Equal("""{"value":"ABC-1","content":null,"title":null}""", inputs.Inputs["code"]!.ToJsonString());
    }

    [Fact]
    public void TheRunInputs_CarryTheRunFactsAndTheProfileGiven()
    {
        var profile = new Dictionary<string, string?>(StringComparer.Ordinal) { ["name"] = "Dr A. Sample" };

        var (inputs, _) = V31StartChecks.Inputs(Plan(), Valid(), new JsonObject { ["id"] = "job-1" }, profile);

        Assert.Equal("""{"id":"job-1"}""", inputs!.RunFacts.ToJsonString());
        Assert.Equal("Dr A. Sample", inputs.Profile["name"]);
    }

    [Fact]
    public void NoRefusalSentence_QuotesASuppliedValue()
    {
        var supplied = new Dictionary<string, JsonNode?>(StringComparer.Ordinal)
        {
            ["consult_draft"] = "   ",
            ["sections"] = "Margaret Okafor's history",
            ["include_billing"] = "Okafor",
            ["dose"] = 77,
            ["kind"] = "margaret",
            ["Margaret_Okafor_referral"] = Draft,
        };

        var refusals = Refusals(supplied);

        Assert.NotEmpty(refusals);
        Assert.All(refusals, r =>
        {
            Assert.DoesNotContain("Okafor", r.Sentence.Replace("'Margaret_Okafor_referral'", ""), StringComparison.Ordinal);
            Assert.DoesNotContain("argaret", r.Sentence.Replace("'Margaret_Okafor_referral'", ""), StringComparison.Ordinal);
            Assert.DoesNotContain("77", r.Sentence, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Mars/Phobos")]
    public void NoTimeZone_IsS06008(string? zone)
    {
        var refusal = Assert.Single(V31StartChecks.Account(Plan(), NoProfile, zone));

        Assert.Equal("S06-008", refusal.Id);
        V31Contract.AssertSentence(refusal);
    }

    [Fact]
    public void AnIanaZone_IsAccepted()
    {
        Assert.Empty(V31StartChecks.Account(Plan(), NoProfile, "America/Toronto"));
    }

    [Fact]
    public void AMissingSignature_IsS06031()
    {
        var plan = Plan(needs: ["signature"]);

        foreach (var profile in new[] { NoProfile, Profile(("signature", " \n ")) })
        {
            var refusal = Assert.Single(V31StartChecks.Account(plan, profile, "America/Toronto"));
            Assert.Equal("S06-031", refusal.Id);
            V31Contract.AssertSentence(refusal);
        }

        Assert.Empty(V31StartChecks.Account(plan, Profile(("signature", "Dr A. Sample\nInternal Medicine")), "America/Toronto"));
    }

    [Fact]
    public void AMissingProfileField_IsS06032()
    {
        var plan = Plan(needs: ["credentials"]);

        var refusal = Assert.Single(V31StartChecks.Account(plan, Profile(("credentials", "  "), ("organisation", null)), "America/Toronto"));

        Assert.Equal("S06-032", refusal.Id);
        Assert.Equal("This workflow needs your credentials. Set it in Profile, under Letter details.", refusal.Sentence);
        V31Contract.AssertSentence(refusal);
    }

    [Fact]
    public void AProfileFieldThePackageDoesNotRead_IsNotChecked()
    {
        Assert.Empty(V31StartChecks.Account(Plan(), NoProfile, "America/Toronto"));
    }

    [Fact]
    public void TheAccountsRefusals_AreTheProfilesThenTheZones()
    {
        var refusals = V31StartChecks.Account(Plan(needs: ["signature", "organisation"]), NoProfile, null);

        Assert.Equal(["S06-031", "S06-032", "S06-008"], refusals.Select(r => r.Id));
    }

    [Fact]
    public void RunDate_IsTheStartInTheAccountsZone()
    {
        var plan = Plan(facts: ["run.date", "run.package"]);

        var facts = V31StartChecks.RunFacts(plan, LateEvening, "America/Toronto");

        Assert.Equal(new JsonObject { ["date"] = "2026-11-16", ["package"] = $"{plan.Package}@{plan.Version}" }.ToJsonString(), facts.ToJsonString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Not/A_Zone")]
    public void WithNoZone_OrOneUnknown_RunDate_IsUtcs(string? zone)
    {
        Assert.Equal("2026-11-17", (string?)V31StartChecks.RunFacts(Plan(facts: ["run.date"]), LateEvening, zone)["date"]);
    }

    [Fact]
    public void AFactThePlanDoesNotRead_IsNotGiven()
    {
        Assert.Empty(V31StartChecks.RunFacts(Plan(), LateEvening, "America/Toronto"));
        Assert.Equal(["package"], V31StartChecks.RunFacts(Plan(facts: ["run.package", "run.id"]), LateEvening, "America/Toronto").Select(f => f.Key));
    }

    // ---- the signature block and R8 (#1505, area 08; D15, R8) --------------------------------------------------

    [Theory]
    [InlineData(null, "sig-1", "default")]
    [InlineData("", "sig-1", "default")]
    [InlineData("sig-1", "sig-1", "default")]
    [InlineData("sig-2", "sig-2", "job")]
    public void TheSignature_IsTheBlockNamed_ElseTheChosenOne(string? requested, string block, string chosen)
    {
        var (found, how, refusal) = V31StartChecks.Signature(Blocks("sig-1"), requested);

        Assert.Equal((block, chosen), (found!.Id, how));
        Assert.Null(refusal);
    }

    [Fact]
    public void ASignatureNamedThatIsNotTheAccounts_IsA08004()
    {
        var (found, how, refusal) = V31StartChecks.Signature(Blocks("sig-1"), "sig-9");

        Assert.Equal(((SignatureBlocks.SignatureBlock?)null, (string?)null), (found, how));
        V31Contract.AssertSentence(new SourceRefusal("A08-004", refusal!, ""));
    }

    [Fact]
    public void WithNoBlockChosen_AndNoneNamed_ThereIsNoSignature()
    {
        Assert.Equal(((SignatureBlocks.SignatureBlock?)null, (string?)null, (string?)null), V31StartChecks.Signature(Blocks(null), null));
    }

    [Fact]
    public void ADeliverableWhoseIfReadsARunFactTheStartDoesNotGive_CanExist()
    {
        // run.date is worked out when the orchestration starts (D6), not at the start: the run decides
        var plan = Plan(deliverableIf: """{ "===": [{ "var": "run.date" }, "2026-11-17"] }""");
        var (inputs, _) = V31StartChecks.Inputs(plan, Valid(), new JsonObject { ["id"] = "job-1" }, NoProfile);

        Assert.False(V31StartChecks.NoDeliverableCanExist(plan, inputs!));

        // given (as a caller that knows it may), it decides
        var (dated, _) = V31StartChecks.Inputs(plan, Valid(), new JsonObject { ["date"] = "2026-11-16" }, NoProfile);
        Assert.True(V31StartChecks.NoDeliverableCanExist(plan, dated!));
    }

    [Fact]
    public void ADeliverableWhoseIfIsTrue_OrHasNone_CanExist()
    {
        var plan = Plan(deliverableIf: """{ "===": [{ "var": "input.code.value" }, "ABC-1"] }""");
        var (inputs, _) = V31StartChecks.Inputs(plan, Valid(), new JsonObject(), NoProfile);
        Assert.False(V31StartChecks.NoDeliverableCanExist(plan, inputs!));

        var (unconditional, _) = V31StartChecks.Inputs(Plan(), Valid(), new JsonObject(), NoProfile);
        Assert.False(V31StartChecks.NoDeliverableCanExist(Plan(), unconditional!));
    }

    private static SignatureBlocks.SignatureBlockSet Blocks(string? chosen) => new(
        [
            new("sig-1", "Clinic", "Dr A. Sample\nSample Oncology Clinic", DateTimeOffset.UnixEpoch),
            new("sig-2", "Ward", "Dr A. Sample\nWard 4", DateTimeOffset.UnixEpoch),
        ],
        chosen);

    private static IReadOnlyList<SourceRefusal> Refusals(Dictionary<string, JsonNode?> supplied)
    {
        var (inputs, refusals) = Check(supplied);
        Assert.Null(inputs);
        return refusals;
    }

    private static (RunInputs? Inputs, IReadOnlyList<SourceRefusal> Refusals) Check(Dictionary<string, JsonNode?> supplied) =>
        V31StartChecks.Inputs(Plan(), supplied, new JsonObject(), NoProfile);

    private static Dictionary<string, string?> Profile(params (string Field, string? Value)[] fields) =>
        fields.ToDictionary(f => f.Field, f => f.Value, StringComparer.Ordinal);

    /// <summary>Every required input supplied, valid; every optional one left out.</summary>
    internal static Dictionary<string, JsonNode?> Valid() => new(StringComparer.Ordinal)
    {
        ["consult_draft"] = Draft,
        ["sections"] = JsonNode.Parse("""[{"title":"History","kind":"narrative"}]"""),
        ["code"] = "ABC-1",
    };

    /// <summary>
    /// The conditional golden's plan with four more inputs: <c>notes</c> (optional plaintext), <c>code</c> (a required
    /// <c>string</c>), <c>dose</c> (an optional enum of numbers, R4) and <c>kind</c> (an optional labelled choice), and with
    /// <paramref name="needs"/> as its <c>needs.profile</c> and <paramref name="facts"/> as its <c>run</c>.
    /// </summary>
    internal static RunPlan Plan(string[]? needs = null, string[]? facts = null, string? deliverableIf = null, bool headline = false)
    {
        var pair = CompiledGoldenTests.Published(CompiledGoldenTests.Cases().Single(c => c.Name == "conditional"));
        var document = JsonNode.Parse(pair.Resolve)!.AsObject();
        document["formats"]!["$defs"]!["string"] = JsonNode.Parse(
            """{ "$id": "urn:consultologist:spec:31:format:string", "$schema": "urn:consultologist:spec:31:dialect", "type": "string" }""");
        var inputs = document["inputs"]!.AsObject();
        inputs["notes"] = Input("Notes", """{ "$ref": "#/$defs/plaintext" }""", required: false);
        inputs["code"] = Input("Referral code", """{ "$ref": "#/$defs/string" }""", required: true);
        inputs["dose"] = Input("Dose", """{ "type": "integer", "enum": [1, 2, 5] }""", required: false);
        inputs["kind"] = Input(
            "Kind of visit",
            """{ "type": "string", "oneOf": [ { "const": "new", "title": "New patient" }, { "const": "follow_up", "title": "Follow-up" } ] }""",
            required: false);
        document["inputOrder"] = new JsonArray("consult_draft", "sections", "include_billing", "notes", "code", "dose", "kind");
        if (headline)
        {
            // R10, D21: an object input with a markdown_inline field (an input is never markdown_inline itself, S02-040).
            document["formats"]!["$defs"]!["markdown_inline"] = JsonNode.Parse(
                """{ "$id": "urn:consultologist:spec:31:format:markdown_inline", "$schema": "urn:consultologist:spec:31:dialect", "type": "string", "contentMediaType": "text/markdown", "x-markdown": "inline" }""");
            inputs["headline"] = Input(
                "Headline",
                """{ "type": "object", "properties": { "line": { "$ref": "#/$defs/markdown_inline", "title": "Line" } }, "required": ["line"], "additionalProperties": false }""",
                required: false);
            document["inputOrder"]!.AsArray().Add("headline");
        }

        document["needs"] = new JsonObject { ["profile"] = new JsonArray((needs ?? []).Select(n => (JsonNode?)n).ToArray()) };
        document["run"] = new JsonArray((facts ?? []).Select(f => (JsonNode?)f).ToArray());
        if (deliverableIf != null)
        {
            var note = document["deliverables"]!["deliverable.note"]!.AsObject();
            note["if"] = JsonNode.Parse(deliverableIf);
            note["plainConverts"]!["if"] = new JsonArray();
        }

        var text = CanonicalJson.Write(document);
        var (plan, refusal) = RunPlan.Build(text, CanonicalJson.HashText(text), pair.LockHash, null);
        Assert.True(plan != null, refusal?.Sentence);
        return plan!;
    }

    // ---- MacroChoices (#1507, ruled by the owner on #1507, 2026-11-22, 3; part 10 § 1.3; R7) ---------------------

    /// <summary>
    /// A 31 package holding what the migration wrote for <c>example-macros</c> with an input <c>letrozole</c> and the
    /// optional macro <c>letrozole</c> (<see cref="V31Migration.MigrateMacrosTests.WithLetrozole"/>): the inputs
    /// <c>letrozole</c> and <c>letrozole_macro</c>, the macro file whose <c>if</c> reads <c>letrozole_macro</c>, and an agent
    /// placing it; with <paramref name="more"/> macros besides, each <c>(id, its if)</c>.
    /// </summary>
    internal static RunPlan Migrated(params (string Id, string If)[] more)
    {
        var migrated = V31Migration.MigrateMacrosTests.WithLetrozole();
        var inputs = JsonNode.Parse(migrated.ManifestJson)!["inputs"]!;
        var files = V31Templates.TemplateFixture.Package()
            .WithJson("manifest.json", m =>
            {
                foreach (var id in new[] { "letrozole", "letrozole_macro", "include_counselling" })
                    m["inputs"]![id] = inputs[id]!.DeepClone();
            })
            .Listed("macros/index.json", "letrozole")
            .With("macros/letrozole.md", migrated.Files["macros/letrozole.md"]);
        // The agent takes the migrated macro's format (markdown_block: summary is prose a deliverable composes, R9), and so do the others.
        var format = V31Migration.MigrateMacrosTests.FormatOf(migrated.Files["macros/letrozole.md"]);
        var placements = "{{ macro.letrozole }}\n";
        foreach (var (id, condition) in more)
        {
            files.Listed("macros/index.json", id).With($"macros/{id}.md", SourceFixture.Md(
                $"{{ \"label\": \"Macro {id}\", \"description\": null, \"if\": {condition}, \"format\": {format}, "
                + $"\"instruction\": [\"Place {{{{ macro.{id}.content }}}} at the end of the plan.\"] }}",
                "Reviewed with Margaret Okafor.\n"));
            placements += $"{{{{ macro.{id} }}}}\n";
        }

        return RunFixture.Plan(V31Templates.TemplateFixture.WithAgent(files, "draft", "Draft the plan for Margaret Okafor.\n" + placements, format));
    }

    private static readonly IReadOnlyDictionary<string, JsonNode?> NothingSent = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AChoiceKeyedByTheMacro_SetsTheInputItsIfReads(bool choice)
    {
        var plan = Migrated();
        Assert.Equal("""{"===":[{"var":"input.letrozole_macro.value"},true]}""", plan.Document["macros"]!["macro.letrozole"]!["if"]!.ToJsonString());

        var (supplied, refusals) = V31StartChecks.Choices(plan, NothingSent, new Dictionary<string, bool> { ["letrozole"] = choice });

        Assert.Empty(refusals);
        Assert.Equal(choice, (bool?)supplied["letrozole_macro"]);
        Assert.False(supplied.ContainsKey("letrozole")); // the input of the macro's id is left alone

        // Sent in Inputs too: the same value is accepted; another is A07-016, naming the input the entry sets.
        var same = new Dictionary<string, JsonNode?>(StringComparer.Ordinal) { ["letrozole_macro"] = choice };
        Assert.Empty(V31StartChecks.Choices(plan, same, new Dictionary<string, bool> { ["letrozole"] = choice }).Refusals);
        var other = new Dictionary<string, JsonNode?>(StringComparer.Ordinal) { ["letrozole_macro"] = !choice };
        var refusal = Assert.Single(V31StartChecks.Choices(plan, other, new Dictionary<string, bool> { ["letrozole"] = choice }).Refusals);
        Assert.Equal(("A07-016", "Input 'letrozole_macro' is given one value in Inputs and another in MacroChoices; send it once."), (refusal.Id, refusal.Sentence));
        V31Contract.AssertSentence(refusal);

        // The merged inputs start the run with the input the macro reads.
        var (inputs, inputRefusals) = V31StartChecks.Inputs(
            plan, new Dictionary<string, JsonNode?>(supplied, StringComparer.Ordinal) { ["consult_draft"] = Draft }, new JsonObject(), NoProfile);
        Assert.Empty(inputRefusals);
        Assert.Equal(choice, (bool?)inputs!.Inputs["letrozole_macro"]!["value"]);
        Assert.False(inputs.Inputs.ContainsKey("letrozole"));
    }

    [Theory]
    [InlineData("null")]                                                                                                     // no if
    [InlineData("""{ "!==": [{ "var": "input.consult_draft.content" }, null] }""")]                                         // a text input, no boolean
    [InlineData("""{ "and": [{ "===": [{ "var": "input.letrozole.value" }, true] }, { "===": [{ "var": "input.include_counselling.value" }, true] }] }""")] // two
    public void AChoiceForAMacroWhoseIfReadsNoOrTwoBooleanInputs_IsRefused(string condition)
    {
        var plan = Migrated(("tamoxifen", condition));

        var (supplied, refusals) = V31StartChecks.Choices(plan, NothingSent, new Dictionary<string, bool> { ["tamoxifen"] = true });

        var refusal = Assert.Single(refusals);
        Assert.Equal(
            ("A10-044", "MacroChoices names macro 'tamoxifen', whose if does not read exactly one boolean input; send the input in Inputs instead."),
            (refusal.Id, refusal.Sentence));
        V31Contract.AssertSentence(refusal);
        Assert.Empty(supplied);
    }

    [Theory]
    [InlineData("anastrozole")]       // no macro, no input
    [InlineData("letrozole_macro")]   // a boolean input, but no macro
    public void AChoiceNamingNoMacro_IsRefused(string id)
    {
        var plan = Migrated();

        var (supplied, refusals) = V31StartChecks.Choices(plan, NothingSent, new Dictionary<string, bool> { [id] = true });

        var refusal = Assert.Single(refusals);
        Assert.Equal(("A10-043", $"MacroChoices names '{id}', which is not a macro of the package."), (refusal.Id, refusal.Sentence));
        V31Contract.AssertSentence(refusal);
        Assert.Empty(supplied);
    }

    private static JsonObject Input(string label, string format, bool required) => new()
    {
        ["channel"] = null,
        ["description"] = null,
        ["format"] = JsonNode.Parse(format),
        ["label"] = label,
        ["required"] = required,
    };
}
