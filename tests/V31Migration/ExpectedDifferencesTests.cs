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
        // R4 item 6: 30 accepted a required text supplied blank; 31's start refuses it (A06-011), and nothing runs. The
        // request shows the input was supplied blank.
        var refused = Run(null, refusals: [new SourceRefusal("A06-011", "Input 'consult_draft' is required; fill it in to run this workflow.", "")]);
        var blank = Request(" \n");
        Assert.StartsWith("required-blank-refused: ", Explain(Context(ComparedKind.Status, "Completed", "Refused", run: refused, request: blank)));
        Assert.StartsWith("required-blank-refused: ", Explain(Context(ComparedKind.Prompt, "30's prompt", null, run: refused, key30: "plan", request: blank)));
        Assert.StartsWith("required-blank-refused: ", Explain(Context(ComparedKind.Status, "Completed", "Refused", run: refused, request: Request(""))));

        // Near: the request lost the input (a fault, not R4 item 6) or gave it null; no request; refused for a value that
        // does not fit (A06-012); an input the package does not require; 30 refused too.
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Status, "Completed", "Refused", run: refused, request: Request(null))));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Status, "Completed", "Refused", run: refused, request: Request(null, drop: true))));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Status, "Completed", "Refused", run: refused)));
        var notFit = Run(null, refusals: [new SourceRefusal("A06-012", "Input 'consult_draft' was given a value that isn't markdown_block.", "")]);
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Status, "Completed", "Refused", run: notFit, request: blank)));
        var unknown = Run(null, refusals: [new SourceRefusal("A06-011", "Input 'ghost' is required; fill it in to run this workflow.", "")]);
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Status, "Completed", "Refused", run: unknown, request: blank)));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Status, "Refused", "Refused", capture: Capture("Refused", "NoApplicableDeliverable: …"), run: refused, request: blank)));
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
        // Part 06 § 6.4: a backslash before each ASCII punctuation character, a line's first space or tab as its reference,
        // undone only where 31's text is the escape of a value the run read (Task 15 fix round 1): here an input and the
        // profile's signature, read into a deliverable whose own text ("Decision letter.") is left as 31 wrote it.
        var value = "This determination (out_of_scope) was recorded on 2026-11-10.\n    Dr A. Sample\n\tInternal Medicine";
        var escaped = MarkdownEscape.Escape(value);
        Assert.Contains("&#32;", escaped, StringComparison.Ordinal);
        Assert.Equal(value, ExpectedDifferences.Unescape(escaped));
        var request = Request(value, signature: "Dr A. Sample");
        const string Head = "Decision letter.\n\n";
        var old = Head + value + "\n\nSigned: Dr A. Sample";
        var now = Head + escaped + "\n\nSigned: " + MarkdownEscape.Escape("Dr A. Sample", startsLine: false);
        Assert.StartsWith("escaped-plain-text: ", Explain(Context(ComparedKind.Deliverable, old, now, key31: "deliverable.note", request: request)));

        // Near: the template's own text escaped too (no value the run read gives "Decision letter\."); the request not
        // passed (the input and the signature are unknown); a prompt is never escaped; unescaping leaves a difference.
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Deliverable, old, "Decision letter\\.\n\n" + now[Head.Length..], key31: "deliverable.note", request: request)));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Deliverable, old, now, key31: "deliverable.note")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, old, now, key31: "node.plan", request: request)));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Deliverable, old, now + "!", key31: "deliverable.note", request: request)));

        // 30's own backslash in a value (\* at 30 is \\\* escaped) is undone once, never twice.
        Assert.StartsWith("escaped-plain-text: ", Explain(Context(ComparedKind.Deliverable, "a \\* b", MarkdownEscape.Escape("a \\* b"), key31: "deliverable.note", request: Request("a \\* b"))));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Deliverable, "a \\* b", "a \\* b!", key31: "deliverable.note", request: Request("a \\* b"))));
    }

    [Fact]
    public void ItemIdsFromOne_ExplainsTheNumberOfAnInputsItem()
    {
        // #1506 ruling 2: an item of a loop over an input's list is numbered from 1 at 31, where 30 numbered it from 0, where
        // the 31 body writes {{ item.id }} ("Prior letter {{ item.id }}:" in example-prior-letters' letter_summary.md).
        const string Old = "What bears on the question:\nstub 1b171b8d\n\nPrior letter 0:\n2026-03-02, urate 0.55 mmol/L.\n";
        const string New = "What bears on the question:\nstub 1b171b8d\n\nPrior letter 1:\n2026-03-02, urate 0.55 mmol/L.\n";
        Assert.StartsWith("item-ids-from-one: ", Explain(LettersContext(Old, New, "letter_summary:0", "node.letters.letter_summary:1")));

        // Near: an unrelated number moves by one too (urate 0.55 → 1.55), away from the item's id; the number is not the
        // item's (2 for item 0); a word differs too; a key under no input's loop.
        Assert.Null(ExpectedDifferences.Explain(LettersContext(Old, New.Replace("urate 0.55", "urate 1.55", StringComparison.Ordinal), "letter_summary:0", "node.letters.letter_summary:1")));
        Assert.Null(ExpectedDifferences.Explain(LettersContext(Old.Replace("urate 0.55", "urate 0", StringComparison.Ordinal), New.Replace("urate 0.55", "urate 1", StringComparison.Ordinal), "letter_summary:0", "node.letters.letter_summary:1")));
        Assert.Null(ExpectedDifferences.Explain(LettersContext(Old, New.Replace("letter 1", "letter 2", StringComparison.Ordinal), "letter_summary:0", "node.letters.letter_summary:1")));
        Assert.Null(ExpectedDifferences.Explain(LettersContext(Old, New.Replace("bears", "bear", StringComparison.Ordinal), "letter_summary:0", "node.letters.letter_summary:1")));
        Assert.Null(ExpectedDifferences.Explain(LettersContext(Old, New, "assessment", "node.assessment")));
    }

    [Fact]
    public void SentFormat_ExplainsTheMigratedFormatAppendedToAPrompt()
    {
        // Part 10 § 1.8 only: every property required, an optional one nullable, in canonical order.
        const string Prompt = "Draft the plan for Margaret Okafor.\n\n" + AgentTurn.CustomTrailer + "\n";
        const string At30 = """{"type":"object","additionalProperties":false,"required":["summary"],"properties":{"summary":{"type":"string"},"note":{"type":"string"}}}""";
        const string At31 = """{"additionalProperties":false,"properties":{"note":{"type":["string","null"]},"summary":{"type":"string"}},"required":["note","summary"],"type":"object"}""";
        Assert.StartsWith("sent-format: ", Explain(Context(ComparedKind.Prompt, Prompt + At30, Prompt + At31, key31: "node.plan", key30: "plan")));

        // Near (the controller's ruling): 31 also sends a description, at the top or on a property, or a title, $schema or
        // $id: no ruling declares it, so the text stays unexpected even with § 1.8's change beside it.
        foreach (var (member, at) in new[] { ("\"description\":\"A plan.\",", 1), ("\"title\":\"Plan\",", 1), ("\"$id\":\"urn:x\",", 1) })
            Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, Prompt + At30, Prompt + At31.Insert(at, member), key31: "node.plan")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, Prompt + At30, Prompt + At31.Replace("""{"type":"string"}}""", """{"description":"One line.","type":"string"}}""", StringComparison.Ordinal), key31: "node.plan")));

        // Near: the optional property not made nullable; a property 30 did not have (concept_list's source); a required one
        // made nullable; the text before the format differs; a deliverable.
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, Prompt + At30, Prompt + At31.Replace("""["string","null"]""", "\"string\"", StringComparison.Ordinal), key31: "node.plan")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, Prompt + At30, Prompt + At31.Replace("""{"note":""", """{"source":{"type":"string"},"note":""", StringComparison.Ordinal), key31: "node.plan")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, Prompt + At30, Prompt + At31.Replace("""{"type":"string"}}""", """{"type":["string","null"]}}""", StringComparison.Ordinal), key31: "node.plan")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, Prompt + At30, "Draft the note." + At31, key31: "node.plan")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Deliverable, Prompt + At30, Prompt + At31, key31: "deliverable.note")));
    }

    [Fact]
    public void ExplicitNullForOptional_ExplainsTheNullMembersAReplyNowCarries()
    {
        // Part 10 § 1.8: an optional field 30 left out is an explicit null at 31: in general's plan, concept_list's id and
        // support are nullable.
        Assert.StartsWith("explicit-null-for-optional: ", Explain(Context(ComparedKind.Prompt,
            """Concepts: {"term":"stub 8427a0bf"}""", """Concepts: {"id":null,"support":null,"term":"stub 8427a0bf"}""", "general", key31: "node.draft")));
        Assert.StartsWith("explicit-null-for-optional: ", Explain(Context(ComparedKind.Render, "Concepts: {}", """Concepts: {"support":null}""", "general", key31: "node.draft")));

        // Near: a null member § 1.8 does not explain (no format of the package makes "note" nullable); a member with a
        // value; a deliverable; null outside a member.
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, """Plan: {"summary":"x"}""", """Plan: {"note":null,"summary":"x"}""", "general", key31: "node.draft")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, """Plan: {"summary":"x"}""", """Plan: {"support":"y","summary":"x"}""", "general", key31: "node.draft")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Deliverable, """Plan: {"summary":"x"}""", """Plan: {"support":null,"summary":"x"}""", "general", key31: "deliverable.note")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, "Plan: [1]", "Plan: [1,null]", "general", key31: "node.draft")));
    }

    [Fact]
    public void MacroTrailingWhitespace_ExplainsAMacroBodys31Rule()
    {
        // R5: a macro body ending "…here.\n\n" keeps one line end at 31 (its final one dropped), where 30 trimmed it: one
        // more line end right after the macro's last word.
        var migrated = WithMacro("macros/closing.md", "Please send an updated referral here.\n\n");
        const string Old = "Letter.\n\nPlease send an updated referral here.\n\nDr A. Sample";
        Assert.StartsWith("macro-trailing-whitespace: ", Explain(MacroContext(migrated, Old, "Letter.\n\nPlease send an updated referral here.\n\n\nDr A. Sample")));

        // Near: the extra line end away from the macro's text (after "Letter."); two more line ends; a space; a snippet
        // written from a macro (D4: the migration trims it as 30 did, so it is never R5's); a body ending in a tag.
        Assert.Null(ExpectedDifferences.Explain(MacroContext(migrated, Old, "Letter.\n\n\nPlease send an updated referral here.\n\nDr A. Sample")));
        Assert.Null(ExpectedDifferences.Explain(MacroContext(migrated, Old, "Letter.\n\nPlease send an updated referral here.\n\n\n\nDr A. Sample")));
        Assert.Null(ExpectedDifferences.Explain(MacroContext(migrated, Old, "Letter.\n\nPlease send an updated referral here. \n\nDr A. Sample")));
        Assert.Null(ExpectedDifferences.Explain(MacroContext(WithMacro("snippets/closing.md", "Please send an updated referral here.\n"), Old, "Letter.\n\nPlease send an updated referral here.\n\n\nDr A. Sample")));
        Assert.Null(ExpectedDifferences.Explain(MacroContext(WithMacro("macros/closing.md", "Please send {{ snippet.x.content }}\n\n"), Old, "Letter.\n\nPlease send an updated referral here.\n\n\nDr A. Sample")));

        // With the escape (the signature, read from the profile, is plain text in a Markdown deliverable), both rules are named.
        var both = Explain(MacroContext(migrated, Old, "Letter.\n\nPlease send an updated referral here.\n\n\n" + MarkdownEscape.Escape("Dr A. Sample"), Request(null, signature: "Dr A. Sample")));
        Assert.StartsWith("escaped-plain-text: ", both);
        Assert.Contains("; macro-trailing-whitespace: ", both, StringComparison.Ordinal);
    }

    [Fact]
    public void BlankConversion_ExplainsATextOnlyTheGateLets31Have()
    {
        // R1: '!x' on text became blank(x), which held at 31 for an empty text, where 30's !x did not (x was not null): the
        // gated node ran at 31 only, so the text is 31's alone.
        var notice = "nodes/identify_problem.md, if: '!input.prior_notes' became blank(input.prior_notes.content); it now also holds for empty and white-space-only text.";
        var migrated = EquivalenceFixture.Migrated("example-two-documents") with { Report = new MigrationNotes([], [], [notice], [], []) };
        var held = Run(RunState.Completed, decisions: [Decision("node.identify_problem", true, CanonicalJson.Hash(JsonValue.Create("")))]);
        Assert.StartsWith("blank-conversion: ", Explain(Context(ComparedKind.Prompt, null, "31", run: held, migrated: migrated, package: "example-two-documents", key31: "node.identify_problem")));
        var deliverable = Run(RunState.Completed, decisions: [Decision("deliverable.consult_note", true, CanonicalJson.Hash(JsonValue.Create(" ")))]);
        Assert.StartsWith("blank-conversion: ", Explain(Context(ComparedKind.Deliverable, null, "31", run: deliverable, migrated: migrated, package: "example-two-documents", key31: "deliverable.consult_note")));

        // Near: a text both sides have, whose bytes the gate does not decide (a deliverable, a prompt); the status; x was null
        // (30's !x held too); blank did not hold; a gate that is more than blank(x); A10-050's trim == "" (unruled); a text
        // the gate is not on.
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Deliverable, "30", "31", run: deliverable, migrated: migrated, package: "example-two-documents", key31: "deliverable.consult_note")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, "30", "31", run: held, migrated: migrated, package: "example-two-documents", key31: "node.identify_problem")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Status, "Completed", "Failed", run: held, migrated: migrated, package: "example-two-documents")));
        var wasNull = Run(RunState.Completed, decisions: [Decision("node.identify_problem", true, CanonicalJson.Hash(null))]);
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, null, "31", run: wasNull, migrated: migrated, package: "example-two-documents", key31: "node.identify_problem")));
        var notHeld = Run(RunState.Completed, decisions: [Decision("node.identify_problem", false, CanonicalJson.Hash(JsonValue.Create("")))]);
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, null, "31", run: notHeld, migrated: migrated, package: "example-two-documents", key31: "node.identify_problem")));
        var more = Run(RunState.Completed, decisions: [Decision("node.identify_problem", true, CanonicalJson.Hash(JsonValue.Create("")), "sha256:other")]);
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, null, "31", run: more, migrated: migrated, package: "example-two-documents", key31: "node.identify_problem")));
        var trim = migrated with
        {
            Report = new MigrationNotes([], [], ["nodes/identify_problem.md, if: '(input.prior_notes | string.strip) == \"\"' became blank(input.prior_notes.content); it now also holds when input.prior_notes.content is null."], [], []),
        };
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, null, "31", run: held, migrated: trim, package: "example-two-documents", key31: "node.identify_problem")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, null, "31", run: held, migrated: migrated, package: "example-two-documents", key31: "node.extract_patient_concepts")));
    }

    // ---- helpers ---------------------------------------------------------------------------------------------

    private static string Explain(DifferenceContext context) =>
        ExpectedDifferences.Explain(context) ?? throw new Xunit.Sdk.XunitException($"No rule explains {context.Site}.");

    private static string Explain(ComparedKind kind, string? old, string? now, string package, string scenario) =>
        Explain(Context(kind, old, now, package, scenario, key31: kind == ComparedKind.Status ? null : "deliverable.note"));

    private static DifferenceContext LettersContext(string old, string now, string key30, string key31) =>
        Context(ComparedKind.Prompt, old, now, "example-prior-letters", key30: key30, key31: key31);

    private static DifferenceContext MacroContext(Migration31Result migrated, string old, string now, OfflineRequest? request = null) =>
        Context(ComparedKind.Deliverable, old, now, "example-classifier-scope", "out-of-scope", migrated: migrated, key31: "deliverable.decline_letter", request: request);

    /// <summary>example-classifier-scope as migrated, with one more file holding a 30 macro's body (<see cref="Migration31Result.MacroBodies"/>).</summary>
    private static Migration31Result WithMacro(string path, string body)
    {
        var migrated = EquivalenceFixture.Migrated("example-classifier-scope");
        var files = new Dictionary<string, string>(migrated.Files, StringComparer.Ordinal)
        {
            [path] = "---\n{\"label\":\"Closing\",\"description\":null,\"if\":null,\"format\":\"markdown_block\",\"instruction\":[]}\n---\n" + body,
        };
        return migrated with { Files = files, MacroBodies = [path] };
    }

    private static OfflineRequest Request(string? draft, bool drop = false, string? signature = null)
    {
        var inputs = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        if (!drop)
            inputs["consult_draft"] = draft;
        var profile = new Dictionary<string, string?>(StringComparer.Ordinal) { ["signature"] = signature };
        return new OfflineRequest(inputs, profile, new JsonObject(), new Dictionary<string, JsonNode?>(StringComparer.Ordinal), Consultologist.PackageFormat.V31.Harness.ByteEquivalence.CapturedAtUtc);
    }

    private static DifferenceContext Context(
        ComparedKind kind, string? old, string? now, string package = "example-nested-note", string scenario = "default",
        JsonObject? capture = null, OfflineResult? run = null, Migration31Result? migrated = null, string? key30 = null, string? key31 = null,
        OfflineRequest? request = null)
    {
        // A package with hand edits has no plan: its context is made on example-prior-letters' (the case rules read none).
        var planned = MigratedPackagesTests.Migrated(package).HandEdits.Count == 0 ? package : "example-prior-letters";
        return new(package, scenario, $"{package}/{scenario}#{(kind == ComparedKind.Status ? "status" : key31 ?? key30)}", kind, key30, key31, old, now,
            capture ?? Capture("Completed", null), run ?? Run(RunState.Completed), migrated ?? EquivalenceFixture.Migrated(planned), EquivalenceFixture.Plan(planned), request);
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

    private static RunDecision Decision(string owner, bool result, string readHash, string? expressionHash = null) =>
        new(owner, "self", "blank(input.prior_notes.content)",
            expressionHash ?? CanonicalJson.Hash(JsonNode.Parse("""{"blank":[{"var":"input.prior_notes.content"}]}""")), result, null,
            [new RunRead("input.prior_notes.content", readHash)], false, false, new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc));
}
