using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Harness;
using Consultologist.PackageFormat.V31.Run;
using Consultologist.PackageFormat.V31.Templates;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1507 (specVersion 31, area 10, Task 15; part 10 § 3.2; "when in doubt, a difference is unexpected"): each rule of
/// <see cref="ExpectedDifferences"/> explains a pair of texts it declares, and not a near pair it does not; every rule names
/// the ruling or spec section it rests on; a difference no rule explains is unexpected. The contexts are made by hand on a
/// migrated package's real plan. Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class ExpectedDifferencesTests
{
    /// <summary>The rules, in order: no rule is added without its ruling or section (and its test here).</summary>
    private static readonly string[] RuleIds =
    [
        "empty-input-list-runs", "skipped-deliverable-reads-null", "required-blank-refused", "all-refused-completes",
        "refused-deliverable-withheld", "escaped-plain-text", "item-ids-from-one", "sent-format", "explicit-null-for-optional",
        "macro-trailing-whitespace", "blank-conversion",
    ];

    [Fact]
    public void EveryRule_NamesItsBasis()
    {
        Assert.Equal(RuleIds, ExpectedDifferences.Rules.Select(r => r.Id));
        Assert.All(ExpectedDifferences.Rules, rule =>
        {
            Assert.Matches(@"(ruled by the owner on #15\d\d|part \d\d §)", rule.Basis);
            Assert.False(string.IsNullOrWhiteSpace(rule.Explanation));
            Assert.Equal($"{rule.Id}: {rule.Explanation} ({rule.Basis})", rule.Said);
        });
        Assert.Equal(["blank"], ExpectedDifferences.ConditionReasons.Keys);
    }

    [Fact]
    public void ADifferenceNoRuleExplains_IsUnexpected()
    {
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, "Encounter date: 09 Nov 2026", "Encounter date: 2026-11-09", key31: "node.plan", key30: "plan")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Status, "Completed", "Failed")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, null, "Only 31 sent this.", key31: "node.plan")));
    }

    // ---- case rules ------------------------------------------------------------------------------------------

    [Fact]
    public void EmptyInputListRuns_ExplainsTheTwoNamedCaptures_Only()
    {
        // R2: refused at 30 (a fanned input with no entries), completed at 31; every text only 31 has.
        Assert.StartsWith("empty-input-list-runs: ", Explain(ComparedKind.Status, "Refused", "Completed", "example-prior-letters", "no-prior-letters"));
        Assert.StartsWith("empty-input-list-runs: ", Explain(ComparedKind.Deliverable, null, "## Assessment", "example-structured-intake", "all-optional-inputs-unset"));

        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Status, "Refused", "Completed", "example-conditional-documents", "encounter-kind-unset")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Status, "Refused", "Failed", "example-prior-letters", "no-prior-letters")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, "30's prompt", "31's prompt", "example-prior-letters", "no-prior-letters", key31: "node.assessment")));
    }

    [Fact]
    public void SkippedDeliverableReadsNull_ExplainsTheJobThatNoLongerFails()
    {
        // R4 item 1: 30 failed reading .content of a deliverable its if skipped; at 31 it reads null and the job completes.
        var capture = Capture("Failed", "Macro 'quoted_review' failed to render: <input>(1,71) : error : Cannot get the member deliverable.consult_note.content for a null object.");
        var skipped = Run(RunState.Completed, deliverables: [Outcome("deliverable.consult_note", DeliverableState.Skipped, null)]);
        Assert.StartsWith("skipped-deliverable-reads-null: ", Explain(Context(ComparedKind.Status, "Failed", "Completed", capture: capture, run: skipped, package: "example-conditional-documents")));
        Assert.StartsWith("skipped-deliverable-reads-null: ", Explain(Context(ComparedKind.Deliverable, null, "Text 31 composed after the read.", capture: capture, run: skipped, package: "example-conditional-documents", key31: "deliverable.progress_note")));

        // Near: the deliverable was released at 31 (not skipped); another failure; a text both sides have.
        var released = Run(RunState.Completed, deliverables: [Outcome("deliverable.consult_note", DeliverableState.Released, "Text.")]);
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Status, "Failed", "Completed", capture: capture, run: released, package: "example-conditional-documents")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Status, "Failed", "Completed", capture: Capture("Failed", "Node 'x' failed."), run: skipped, package: "example-conditional-documents")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Deliverable, "A", "B", capture: capture, run: skipped, package: "example-conditional-documents", key31: "deliverable.progress_note")));
    }

    [Fact]
    public void RequiredBlankRefused_ExplainsTheRefusedStart()
    {
        // R4 item 6: 30 accepted a required text supplied blank; 31's start refuses it (A06-011), and nothing runs.
        var refused = Run(null, refusals: [new SourceRefusal("A06-011", "Input 'consult_draft' is required; fill it in to run this workflow.", "")]);
        Assert.StartsWith("required-blank-refused: ", Explain(Context(ComparedKind.Status, "Completed", "Refused", run: refused)));
        Assert.StartsWith("required-blank-refused: ", Explain(Context(ComparedKind.Prompt, "30's prompt", null, run: refused, key30: "plan")));

        // Near: refused for a value that does not fit (A06-012); an input the package does not require; 30 refused too.
        var notFit = Run(null, refusals: [new SourceRefusal("A06-012", "Input 'consult_draft' was given a value that isn't markdown_block.", "")]);
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Status, "Completed", "Refused", run: notFit)));
        var unknown = Run(null, refusals: [new SourceRefusal("A06-011", "Input 'ghost' is required; fill it in to run this workflow.", "")]);
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Status, "Completed", "Refused", run: unknown)));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Status, "Refused", "Refused", capture: Capture("Refused", "NoApplicableDeliverable: …"), run: refused)));
    }

    [Fact]
    public void AllRefusedCompletes_ExplainsTheJobCompletedWithEveryDeliverableRefused()
    {
        // R4 item 7: 30 failed with a check's sentence because its checks refused every deliverable; 31 completes.
        var capture = Capture("Failed", "The acknowledgement does not cover the referral's clinical terms.");
        capture["deliverables"] = JsonNode.Parse("""
            [ { "id": "consult", "state": "failed", "reason": "The note does not cover every clinical term found in the referral.", "hash": null, "text": null },
              { "id": "ack_letter", "state": "failed", "reason": "The acknowledgement does not cover the referral's clinical terms.", "hash": null, "text": null } ]
            """);
        var blocked = Run(RunState.Completed, deliverables:
            [Outcome("deliverable.consult", DeliverableState.Blocked, "Text."), Outcome("deliverable.ack_letter", DeliverableState.Blocked, "Text.")]);
        Assert.StartsWith("all-refused-completes: ", Explain(Context(ComparedKind.Status, "Failed", "Completed", capture: capture, run: blocked)));

        // Near: one released at 31; 30 failed for another reason.
        var one = Run(RunState.Completed, deliverables:
            [Outcome("deliverable.consult", DeliverableState.Released, "Text."), Outcome("deliverable.ack_letter", DeliverableState.Blocked, "Text.")]);
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Status, "Failed", "Completed", capture: capture, run: one)));
        var other = (JsonObject)capture.DeepClone();
        other["failure"] = "Node 'extract' failed.";
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Status, "Failed", "Completed", capture: other, run: blocked)));
    }

    // ---- bytes rules -----------------------------------------------------------------------------------------

    [Fact]
    public void RefusedDeliverableWithheld_ExplainsAReaderOfTheRefusedText()
    {
        // R4 item 3: a deliverable its check refused reads as null at 31, where 30 read its composed text.
        var run = Run(RunState.Completed, deliverables: [Outcome("deliverable.note", DeliverableState.Blocked, "Diabetes paragraph.")]);
        Assert.StartsWith("refused-deliverable-withheld: ", Explain(Context(ComparedKind.Deliverable, "Cover.\n\nDiabetes paragraph.\nEnd.", "Cover.\n\n\nEnd.", run: run, key31: "deliverable.cover")));

        // Near: the reader differs otherwise too; the deliverable was released (not withheld).
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Deliverable, "Cover.\n\nDiabetes paragraph.\nEnd.", "Cover!\n\n\nEnd.", run: run, key31: "deliverable.cover")));
        var released = Run(RunState.Completed, deliverables: [Outcome("deliverable.note", DeliverableState.Released, "Diabetes paragraph.")]);
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Deliverable, "Cover.\n\nDiabetes paragraph.\nEnd.", "Cover.\n\n\nEnd.", run: released, key31: "deliverable.cover")));
    }

    [Fact]
    public void EscapedPlainText_ExplainsADeliverableThatUnescapesTo30sBytes()
    {
        // Part 06 § 6.4: a backslash before each ASCII punctuation character, a line's first space or tab as its reference.
        var old = "This determination (out_of_scope) was recorded on 2026-11-10.\n    Dr A. Sample\n\tInternal Medicine";
        var escaped = MarkdownEscape.Escape(old);
        Assert.Contains("&#32;", escaped, StringComparison.Ordinal);
        Assert.Equal(old, ExpectedDifferences.Unescape(escaped));
        Assert.StartsWith("escaped-plain-text: ", Explain(Context(ComparedKind.Deliverable, old, escaped, key31: "deliverable.note")));

        // Near: a prompt is never escaped; unescaping leaves a difference; 30's own backslash (\\* at 30 is \\\\\\* escaped).
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, old, escaped, key31: "node.plan")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Deliverable, old, escaped + "!", key31: "deliverable.note")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Deliverable, "a \\* b", "a \\* b!", key31: "deliverable.note")));
        Assert.StartsWith("escaped-plain-text: ", Explain(Context(ComparedKind.Deliverable, "a \\* b", MarkdownEscape.Escape("a \\* b"), key31: "deliverable.note")));
    }

    [Fact]
    public void ItemIdsFromOne_ExplainsTheNumberOfAnInputsItem()
    {
        // #1506 ruling 2: an item of a loop over an input's list is numbered from 1 at 31, where 30 numbered it from 0.
        const string Old = "What bears on the question:\nstub 1b171b8d\n\nPrior letter 0:\n2026-03-02, urate 0.55 mmol/L.\n";
        const string New = "What bears on the question:\nstub 1b171b8d\n\nPrior letter 1:\n2026-03-02, urate 0.55 mmol/L.\n";
        Assert.StartsWith("item-ids-from-one: ", Explain(LettersContext(Old, New, "letter_summary:0", "node.letters.letter_summary:1")));

        // Near: the number is not the item's (2 for item 0); a word differs too; a key under no input's loop.
        Assert.Null(ExpectedDifferences.Explain(LettersContext(Old, New.Replace("letter 1", "letter 2", StringComparison.Ordinal), "letter_summary:0", "node.letters.letter_summary:1")));
        Assert.Null(ExpectedDifferences.Explain(LettersContext(Old, New.Replace("bears", "bear", StringComparison.Ordinal), "letter_summary:0", "node.letters.letter_summary:1")));
        Assert.Null(ExpectedDifferences.Explain(LettersContext(Old, New, "assessment", "node.assessment")));
    }

    [Fact]
    public void SentFormat_ExplainsTheMigratedFormatAppendedToAPrompt()
    {
        // Part 10 § 1.8, part 02 § 3.5: every property required, an optional one nullable, descriptions kept, canonical order.
        const string Prompt = "Draft the plan for Margaret Okafor.\n\n" + AgentTurn.CustomTrailer + "\n";
        const string At30 = """{"type":"object","additionalProperties":false,"required":["summary"],"properties":{"summary":{"type":"string"},"note":{"type":"string"}}}""";
        const string At31 = """{"additionalProperties":false,"description":"A plan.","properties":{"note":{"type":["string","null"]},"summary":{"description":"One line.","type":"string"}},"required":["note","summary"],"type":"object"}""";
        Assert.StartsWith("sent-format: ", Explain(Context(ComparedKind.Prompt, Prompt + At30, Prompt + At31, key31: "node.plan", key30: "plan")));

        // Near: the optional property not made nullable; a property 30 did not have (concept_list's source); a required one
        // made nullable; the text before the format differs; a deliverable.
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, Prompt + At30, Prompt + At31.Replace("""["string","null"]""", "\"string\"", StringComparison.Ordinal), key31: "node.plan")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, Prompt + At30, Prompt + At31.Replace("""{"note":""", """{"source":{"type":"string"},"note":""", StringComparison.Ordinal), key31: "node.plan")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, Prompt + At30, Prompt + At31.Replace("""{"description":"One line.","type":"string"}""", """{"type":["string","null"]}""", StringComparison.Ordinal), key31: "node.plan")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, Prompt + At30, "Draft the note." + At31, key31: "node.plan")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Deliverable, Prompt + At30, Prompt + At31, key31: "deliverable.note")));
    }

    [Fact]
    public void ExplicitNullForOptional_ExplainsTheNullMembersAReplyNowCarries()
    {
        // Part 10 § 1.8: an optional field 30 left out is an explicit null at 31.
        Assert.StartsWith("explicit-null-for-optional: ", Explain(Context(ComparedKind.Prompt,
            """Plan: {"summary":"stub 8427a0bf"}""", """Plan: {"note":null,"summary":"stub 8427a0bf","tail":null}""", key31: "node.draft")));
        Assert.StartsWith("explicit-null-for-optional: ", Explain(Context(ComparedKind.Render, "Plan: {}", """Plan: {"note":null}""", key31: "node.draft")));

        // Near: a member with a value; a deliverable; null outside a member.
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, """Plan: {"summary":"x"}""", """Plan: {"note":"y","summary":"x"}""", key31: "node.draft")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Deliverable, """Plan: {"summary":"x"}""", """Plan: {"note":null,"summary":"x"}""", key31: "deliverable.note")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, "Plan: [1]", "Plan: [1,null]", key31: "node.draft")));
    }

    [Fact]
    public void MacroTrailingWhitespace_ExplainsAMacroBodys31Rule()
    {
        // R5: example-classifier-scope's closing macro, read on its deliverable as a snippet (D4), keeps its final line
        // ending at 31, where 30 trimmed it: one more line end before the signature.
        var migrated = EquivalenceFixture.Migrated("example-classifier-scope");
        Assert.Contains("snippets/closing.md", migrated.MacroBodies);
        const string Old = "please send an updated referral.\n\nDr A. Sample";
        Assert.StartsWith("macro-trailing-whitespace: ", Explain(ScopeContext(Old, "please send an updated referral.\n\n\nDr A. Sample")));

        // Near: two more line ends; a space where 30 had none; a package whose macro bodies keep no trailing white space.
        Assert.Null(ExpectedDifferences.Explain(ScopeContext(Old, "please send an updated referral.\n\n\n\nDr A. Sample")));
        Assert.Null(ExpectedDifferences.Explain(ScopeContext(Old, "please send an updated referral. \n\nDr A. Sample")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Deliverable, Old, "please send an updated referral.\n\n\nDr A. Sample", key31: "deliverable.note")));

        // With the escape (the closing text and the signature are plain text in a Markdown deliverable), both rules are named.
        var both = Explain(ScopeContext(Old, MarkdownEscape.Escape("please send an updated referral.\n\n\nDr A. Sample")));
        Assert.StartsWith("escaped-plain-text: ", both);
        Assert.Contains("; macro-trailing-whitespace: ", both, StringComparison.Ordinal);
    }

    [Fact]
    public void BlankConversion_ExplainsATextThatHangsOnABlankThatHeldForBlankText()
    {
        // R1: '!x' on text became blank(x), which held at 31 for an empty text, where 30's !x did not (x was not null).
        var notice = "nodes/plan.md, if: '!input.prior_notes' became blank(input.prior_notes.content); it now also holds for empty and white-space-only text.";
        var migrated = EquivalenceFixture.Migrated("example-two-documents") with { Report = new MigrationNotes([], [], [notice], [], []) };
        var held = Run(RunState.Completed, decisions: [Decision("node.identify_problem", true, CanonicalJson.Hash(JsonValue.Create("")))]);
        Assert.StartsWith("blank-conversion: ", Explain(Context(ComparedKind.Prompt, "30", "31", run: held, migrated: migrated, package: "example-two-documents", key31: "node.identify_problem")));
        Assert.StartsWith("blank-conversion: ", Explain(Context(ComparedKind.Deliverable, "30", "31", run: held, migrated: migrated, package: "example-two-documents", key31: "deliverable.consult_note")));

        // Near: x was null (30's !x held too); blank did not hold; A10-050's trim == "" (unruled); a text the decision is not
        // upstream of.
        var wasNull = Run(RunState.Completed, decisions: [Decision("node.identify_problem", true, CanonicalJson.Hash(null))]);
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, "30", "31", run: wasNull, migrated: migrated, package: "example-two-documents", key31: "node.identify_problem")));
        var notHeld = Run(RunState.Completed, decisions: [Decision("node.identify_problem", false, CanonicalJson.Hash(JsonValue.Create("")))]);
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, "30", "31", run: notHeld, migrated: migrated, package: "example-two-documents", key31: "node.identify_problem")));
        var trim = migrated with
        {
            Report = new MigrationNotes([], [], ["nodes/plan.md, if: '(input.prior_notes | string.strip) == \"\"' became blank(input.prior_notes.content); it now also holds when input.prior_notes.content is null."], [], []),
        };
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, "30", "31", run: held, migrated: trim, package: "example-two-documents", key31: "node.identify_problem")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, "30", "31", run: held, migrated: migrated, package: "example-two-documents", key31: "node.extract_patient_concepts")));
    }

    // ---- helpers ---------------------------------------------------------------------------------------------

    private static string Explain(DifferenceContext context) =>
        ExpectedDifferences.Explain(context) ?? throw new Xunit.Sdk.XunitException($"No rule explains {context.Site}.");

    private static string Explain(ComparedKind kind, string? old, string? now, string package, string scenario) =>
        Explain(Context(kind, old, now, package, scenario, key31: kind == ComparedKind.Status ? null : "deliverable.note"));

    private static DifferenceContext LettersContext(string old, string now, string key30, string key31) =>
        Context(ComparedKind.Prompt, old, now, "example-prior-letters", key30: key30, key31: key31);

    private static DifferenceContext ScopeContext(string old, string now) =>
        Context(ComparedKind.Deliverable, old, now, "example-classifier-scope", "out-of-scope", key31: "deliverable.decline_letter");

    private static DifferenceContext Context(
        ComparedKind kind, string? old, string? now, string package = "example-nested-note", string scenario = "default",
        JsonObject? capture = null, OfflineResult? run = null, Migration31Result? migrated = null, string? key30 = null, string? key31 = null)
    {
        // A package with hand edits has no plan: its context is made on example-prior-letters' (the case rules read none).
        var planned = MigratedPackagesTests.Migrated(package).HandEdits.Count == 0 ? package : "example-prior-letters";
        return new(package, scenario, $"{package}/{scenario}#{(kind == ComparedKind.Status ? "status" : key31 ?? key30)}", kind, key30, key31, old, now,
            capture ?? Capture("Completed", null), run ?? Run(RunState.Completed), migrated ?? EquivalenceFixture.Migrated(planned), EquivalenceFixture.Plan(planned));
    }

    private static JsonObject Capture(string status, string? failure) => new()
    {
        ["status"] = status,
        ["failure"] = failure,
        ["outputs"] = new JsonObject(),
        ["prompts"] = new JsonArray(),
        ["renders"] = new JsonArray(),
        ["deliverables"] = new JsonArray(),
    };

    private static OfflineResult Run(
        RunState? state, IReadOnlyList<SourceRefusal>? refusals = null, IReadOnlyList<DeliverableOutcome>? deliverables = null, IReadOnlyList<RunDecision>? decisions = null) =>
        new(state, null, refusals ?? [], [], [], deliverables ?? [], new Dictionary<string, JsonNode?>(StringComparer.Ordinal), [], decisions ?? [], []);

    private static DeliverableOutcome Outcome(string key, DeliverableState state, string? text) =>
        new(key, key, "markdown_block", "", state, null, null, text, text is null ? null : CanonicalJson.HashText(text), ["pdf"], null);

    private static RunDecision Decision(string owner, bool result, string readHash) =>
        new(owner, "if", """{"blank":[{"var":"input.prior_notes.content"}]}""", "sha256:0", result, null,
            [new RunRead("input.prior_notes.content", readHash)], false, false, new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc));
}
