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
        "refused-deliverable-withheld", "escaped-plain-text", "item-ids-from-one", "position-names-from-the-loop", "sent-format", "explicit-null-for-optional",
        "macro-trailing-whitespace", "macro-choice-classifier", "blank-conversion",
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
        Assert.Equal(["blank", "blank-conversion"], ExpectedDifferences.ConditionReasons.Keys);
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

        // Final review B3: R2 rules that the runs complete; a text only 31 has, in a 31 run that failed, is unexpected.
        var failed = Run(RunState.Failed);
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, null, "31's prompt", "example-structured-intake", "all-optional-inputs-unset", run: failed, key31: "node.assessment")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Deliverable, null, "## Assessment", "example-structured-intake", "all-optional-inputs-unset", run: failed, key31: "deliverable.note")));
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

        // Final review B3: the 31 run did not complete; a prompt only 31 has (30's failure, composing deliverables, came after
        // every node, so it left out no prompt).
        var failed = Run(RunState.Failed, deliverables: [Outcome("deliverable.consult_note", DeliverableState.Skipped, null)]);
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Deliverable, null, "Text 31 composed after the read.", capture: capture, run: failed, package: "example-conditional-documents", key31: "deliverable.progress_note")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, null, "A prompt only 31 sent.", capture: capture, run: skipped, package: "example-conditional-documents", key31: "node.plan")));
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
    public void RefusedDeliverableWithheld_HoldsOnlyInARowThatReadsTheRefusedDeliverable()
    {
        // R4 item 3: a deliverable its check refused reads as null at 31, where 30 read its composed text; only in a row that
        // reads it (final review B1). Part 06 § 6.7 lets only checks read a deliverable at 31, and a plan whose prompt,
        // rendered node or deliverable reads a checked deliverable does not build (it would wait on a check that runs after
        // it), so no compared row of a published package reads one and the rule explains no such pair.
        var run = Run(RunState.Completed, deliverables: [Outcome("deliverable.note", DeliverableState.Blocked, "Diabetes paragraph.")]);

        // The review's scenario: composer-gates' note and record_copy both compose [body], byte-identical at 30; note is
        // blocked at 31 and a fault leaves record_copy composing "". record_copy does not read note: the lost text is
        // unexpected, as is a text that lost the blocked one's bytes in its middle.
        Assert.Null(ExpectedDifferences.Explain(GatesContext(ComparedKind.Deliverable, "Diabetes paragraph.", "", "deliverable.record_copy", run)));
        Assert.Null(ExpectedDifferences.Explain(GatesContext(ComparedKind.Deliverable, "Cover.\n\nDiabetes paragraph.\nEnd.", "Cover.\n\n\nEnd.", "deliverable.record_copy", run)));
        // Nor a key the plan has no row for (the rule's reads are the row's own).
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Deliverable, "Cover.\n\nDiabetes paragraph.\nEnd.", "Cover.\n\n\nEnd.", run: run, key31: "deliverable.cover")));
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
    public void PositionNamesFromTheLoop_ExplainsTheNameOfAnInputsItem()
    {
        // Part 04 § 2.7 (decided on #1392): an item named by its position is "<loop label> <n>" at 31, where 30 named an
        // input loop's item by the input's label. composer-gates' loop problem_loop ("Problems") runs over the input problems
        // ("Problem list"), with no itemLabel; its .content heads each item "## <name>".
        const string Old = "Extract the clinical terms: ## Problem list 1\n\nStub one.\n\n## Problem list 2\n\nStub two.\n\nA closing line.";
        const string New = "Extract the clinical terms: ## Problems 1\n\nStub one.\n\n## Problems 2\n\nStub two.\n\nA closing line.";
        const string Heads = "## Problem list 1\n\nStub one.\n\n## Problem list 2\n\nStub two.";
        Assert.StartsWith("position-names-from-the-loop: ", Explain(GatesContext(ComparedKind.Prompt, Old, New, "node.cover_note")));
        Assert.StartsWith("position-names-from-the-loop: ", Explain(GatesContext(ComparedKind.Deliverable, Heads, Heads.Replace("Problem list", "Problems", StringComparison.Ordinal), "deliverable.note")));

        // Near: the position differs too; the heading carries more than the name; the name stands outside a heading line; a
        // word differs elsewhere; another loop's label; a package whose input loops' labels are the inputs' own.
        Assert.Null(ExpectedDifferences.Explain(GatesContext(ComparedKind.Prompt, Old, New.Replace("## Problems 2", "## Problems 3", StringComparison.Ordinal), "node.cover_note")));
        Assert.Null(ExpectedDifferences.Explain(GatesContext(ComparedKind.Prompt, Old.Replace("list 2", "list 2 (draft)", StringComparison.Ordinal), New.Replace("Problems 2", "Problems 2 (draft)", StringComparison.Ordinal), "node.cover_note")));
        Assert.Null(ExpectedDifferences.Explain(GatesContext(ComparedKind.Prompt, "See Problem list 1.", "See Problems 1.", "node.cover_note")));
        Assert.Null(ExpectedDifferences.Explain(GatesContext(ComparedKind.Prompt, Old, New.Replace("closing", "final", StringComparison.Ordinal), "node.cover_note")));
        Assert.Null(ExpectedDifferences.Explain(GatesContext(ComparedKind.Prompt, Old, New.Replace("Problems", "Points", StringComparison.Ordinal), "node.cover_note")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, "## Prior letters 1", "## Letters 1", "example-prior-letters", key31: "node.assessment")));

        // Near: a loop with an itemLabel (composer-sources' topic_loop, "Topics" over "Requested topics", itemLabel title)
        // names its items by the field; its position name is only the fallback for an empty one, which is not explained.
        Assert.Null(ExpectedDifferences.Explain(EditedContext("composer-sources", "item-names-and-bounds", ComparedKind.Deliverable,
            "## History\n\nStub.\n\n## Requested topics 4\n\nStub four.", "## History\n\nStub.\n\n## Topics 4\n\nStub four.", "deliverable.topics")));
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

    /// <summary>
    /// #1508 (R12; comment 5, item 7, #3, format descriptions sent; the controller's ruling on Task 31): a classifier's
    /// format is a package format of its answers, bound <c>custom</c>, so 31 ends its prompt with the custom trailer and
    /// that format after 30's own text, which listed the same values ("Answer with exactly one of: …").
    /// </summary>
    [Fact]
    public void SentFormat_ExplainsAClassifiersAnswerFormatAppendedToItsPrompt()
    {
        const string At30 = "Decide whether the referral for Margaret Okafor is in scope.\n\nAnswer with exactly one of: in_scope, out_of_scope.";
        const string Answers = """{"additionalProperties":false,"properties":{"value":{"enum":["in_scope","out_of_scope"],"type":"string"}},"required":["value"],"type":"object"}""";
        const string At31 = At30 + "\n\n" + AgentTurn.CustomTrailer + "\n" + Answers;
        Assert.StartsWith("sent-format: ", Explain(Context(ComparedKind.Prompt, At30, At31, key31: "node.scope", key30: "scope")));

        // Near: other values, or another order, than 30 listed; 30 listed none; a format that is not the answers' shape (a
        // description sent, another member); the text before it differs; a deliverable.
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, At30, At31.Replace("\"out_of_scope\"]", "\"needs_information\"]", StringComparison.Ordinal), key31: "node.scope")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, At30, At31.Replace("[\"in_scope\",\"out_of_scope\"]", "[\"out_of_scope\",\"in_scope\"]", StringComparison.Ordinal), key31: "node.scope")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, "Decide.", "Decide.\n\n" + AgentTurn.CustomTrailer + "\n" + Answers, key31: "node.scope")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, At30, At31.Replace("{\"additionalProperties\"", "{\"description\":\"One value.\",\"additionalProperties\"", StringComparison.Ordinal), key31: "node.scope")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, At30, At31.Replace("\"type\":\"string\"}}", "\"type\":\"string\"},\"reason\":{\"type\":\"string\"}}", StringComparison.Ordinal), key31: "node.scope")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, At30, At31.Replace("{\"value\":{\"enum\":[\"in_scope\",\"out_of_scope\"],\"type\":\"string\"}}", "[]", StringComparison.Ordinal), key31: "node.scope")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, At30, At31.Replace("Margaret Okafor", "the patient", StringComparison.Ordinal), key31: "node.scope")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Deliverable, At30, At31, key31: "deliverable.note")));
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
        // more line end right after the macro's last word. composer-macros (edited): sections_note reads section_loop,
        // whose section_draft places macro.section_close, so its body can stand in the deliverable (final review B4).
        var migrated = WithMacro("composer-macros", "macros/section_close.md", "Please send an updated referral here.\n\n");
        const string Old = "Letter.\n\nPlease send an updated referral here.\n\nDr A. Sample";
        Assert.StartsWith("macro-trailing-whitespace: ", Explain(MacroContext(migrated, Old, "Letter.\n\nPlease send an updated referral here.\n\n\nDr A. Sample")));

        // Near: the extra line end away from the macro's text (after "Letter."); two more line ends; a space; a snippet
        // written from a macro (D4: the migration trims it as 30 did, so it is never R5's); a body ending in a tag.
        Assert.Null(ExpectedDifferences.Explain(MacroContext(migrated, Old, "Letter.\n\n\nPlease send an updated referral here.\n\nDr A. Sample")));
        Assert.Null(ExpectedDifferences.Explain(MacroContext(migrated, Old, "Letter.\n\nPlease send an updated referral here.\n\n\n\nDr A. Sample")));
        Assert.Null(ExpectedDifferences.Explain(MacroContext(migrated, Old, "Letter.\n\nPlease send an updated referral here. \n\nDr A. Sample")));
        Assert.Null(ExpectedDifferences.Explain(MacroContext(WithMacro("composer-macros", "snippets/section_close.md", "Please send an updated referral here.\n"), Old, "Letter.\n\nPlease send an updated referral here.\n\n\nDr A. Sample")));
        Assert.Null(ExpectedDifferences.Explain(MacroContext(WithMacro("composer-macros", "macros/section_close.md", "Please send {{ snippet.x.content }}\n\n"), Old, "Letter.\n\nPlease send an updated referral here.\n\n\nDr A. Sample")));

        // Near (B4): a row whose text cannot hold the macro's body (signed_only reads letter_body, which places no macro),
        // and a macro no row places (example-classifier-scope's decline_letter, a closing macro added by hand).
        Assert.Null(ExpectedDifferences.Explain(MacroContext(migrated, Old, "Letter.\n\nPlease send an updated referral here.\n\n\nDr A. Sample", key31: "deliverable.signed_only")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Deliverable, Old, "Letter.\n\nPlease send an updated referral here.\n\n\nDr A. Sample", "example-classifier-scope", "out-of-scope",
            migrated: WithMacro("example-classifier-scope", "macros/closing.md", "Please send an updated referral here.\n\n"), key31: "deliverable.decline_letter")));

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
        // (30's !x held too); blank did not hold; a gate that is more than blank(x); A10-050's trim == "" (a condition reason only); a text
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

        // Final review B2: a gate decided in one flow instance explains a text of that instance (or one inside it), never
        // another instance's.
        var inOne = Run(RunState.Completed, decisions: [Decision("node.identify_problem:1", true, CanonicalJson.Hash(JsonValue.Create("")))]);
        Assert.StartsWith("blank-conversion: ", Explain(Context(ComparedKind.Prompt, null, "31", run: inOne, migrated: migrated, package: "example-two-documents", key31: "node.identify_problem:1")));
        Assert.StartsWith("blank-conversion: ", Explain(Context(ComparedKind.Prompt, null, "31", run: inOne, migrated: migrated, package: "example-two-documents", key31: "node.identify_problem.inner:1/2")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, null, "31", run: inOne, migrated: migrated, package: "example-two-documents", key31: "node.identify_problem:2")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, null, "31", run: inOne, migrated: migrated, package: "example-two-documents", key31: "node.identify_problem.inner:2/1")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, null, "31", run: inOne, migrated: migrated, package: "example-two-documents", key31: "node.identify_problem.inner:10")));
    }

    // ---- helpers ---------------------------------------------------------------------------------------------

    // ---- macro-choice-classifier (#1508, comment 6, items 1 and 2; R13, R14) ------------------------------------

    private const string Placing = "node.standards.section_instructions:assessment_plan";

    private const string Classifier = "node.standards.letrozole_prescription_text_applies:assessment_plan";

    /// <summary>A prompt of test/general (edited: its classifier written with its question), the classifier having answered <paramref name="yes"/>.</summary>
    private static DifferenceContext ChoiceContext(string? old, string now, bool? yes, string key31 = Placing)
    {
        var outputs = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        if (yes is { } answer)
            outputs[Classifier] = new JsonObject { ["answer"] = answer, ["reason"] = Consultologist.PackageFormat.V31.Harness.ByteEquivalence.ChoiceReason };
        var run = new OfflineResult(RunState.Completed, null, [], [], [], [], outputs, [], [], []);
        return new("test/general", "default", $"test/general/default#prompts/{key31}", ComparedKind.Prompt, null, key31, old, now,
            Capture("Completed", null), run, EquivalenceFixture.Migrated("test/general"), EquivalenceFixture.Plan("test/general"));
    }

    [Fact]
    public void MacroChoiceClassifier_ExplainsTheClassifiersPrompt_AndTheInstructionItGates()
    {
        var classifier = Assert.Single(ChoiceClassifiers.Of(EquivalenceFixture.Migrated("test/general")));
        Assert.Equal(("node.standards.letrozole_prescription_text_applies", "node.standards.section_instructions", "letrozole_prescription_text"),
            (classifier.Key, classifier.Placing, classifier.Macro));
        var now31 = classifier.Instruction;
        var was30 = now31.Replace("assessment and plan.", "assessment and plan; otherwise nothing.", StringComparison.Ordinal);
        Assert.NotEqual(now31, was30);

        // The classifier's prompt, which only 31 sends.
        Assert.StartsWith("macro-choice-classifier: ", Explain(ChoiceContext(null, "Does the plan prescribe letrozole?\n", true, Classifier)));

        // Answered yes: 30's instruction, its "otherwise" clause removed at 31.
        Assert.StartsWith("macro-choice-classifier: ", Explain(ChoiceContext("Standard:\n" + was30 + "Margaret Okafor.", "Standard:\n" + now31 + "Margaret Okafor.", true)));

        // Answered no: the macro is not offered, so 31's prompt lacks 30's instruction.
        Assert.StartsWith("macro-choice-classifier: ", Explain(ChoiceContext("Standard:\n" + was30 + "Margaret Okafor.", "Standard:\nMargaret Okafor.", false)));
    }

    [Fact]
    public void MacroChoiceClassifier_DoesNotExplainANearPair()
    {
        var now31 = Assert.Single(ChoiceClassifiers.Of(EquivalenceFixture.Migrated("test/general"))).Instruction;
        var was30 = now31.Replace("assessment and plan.", "assessment and plan; otherwise nothing.", StringComparison.Ordinal);

        // A prompt only 31 sent that is no classifier's.
        Assert.Null(ExpectedDifferences.Explain(ChoiceContext(null, "Only 31 sent this.", true)));

        // The instruction missing where the classifier answered yes, or kept where it answered no.
        Assert.Null(ExpectedDifferences.Explain(ChoiceContext("Standard:\n" + was30 + "Margaret Okafor.", "Standard:\nMargaret Okafor.", true)));
        Assert.Null(ExpectedDifferences.Explain(ChoiceContext("Standard:\n" + was30 + "Margaret Okafor.", "Standard:\n" + now31 + "Margaret Okafor.", false)));

        // With no answer (the classifier did not run), nothing is explained.
        Assert.Null(ExpectedDifferences.Explain(ChoiceContext("Standard:\n" + was30 + "Margaret Okafor.", "Standard:\nMargaret Okafor.", null)));

        // More than the "otherwise" clause changed, or text beside the instruction.
        var reworded = now31.Replace("side-effect counselling", "counselling", StringComparison.Ordinal);
        Assert.Null(ExpectedDifferences.Explain(ChoiceContext("Standard:\n" + was30 + "Margaret Okafor.", "Standard:\n" + reworded + "Margaret Okafor.", true)));
        Assert.Null(ExpectedDifferences.Explain(ChoiceContext("Standard:\n" + was30 + "Margaret Okafor.", "Standard:\n" + now31 + "Dr A. Sample.", true)));
    }

    private static string Explain(DifferenceContext context) =>
        ExpectedDifferences.Explain(context) ?? throw new Xunit.Sdk.XunitException($"No rule explains {context.Site}.");

    private static string Explain(ComparedKind kind, string? old, string? now, string package, string scenario) =>
        Explain(Context(kind, old, now, package, scenario, key31: kind == ComparedKind.Status ? null : "deliverable.note"));

    private static DifferenceContext LettersContext(string old, string now, string key30, string key31) =>
        Context(ComparedKind.Prompt, old, now, "example-prior-letters", key30: key30, key31: key31);

    /// <summary>A context on composer-gates with its hand edits made (Task 16), so its own plan is read.</summary>
    private static DifferenceContext GatesContext(ComparedKind kind, string old, string now, string key31, OfflineResult? run = null, RunPlan? plan = null) =>
        new("composer-gates", "clinician-checks-pass", $"composer-gates/clinician-checks-pass#{key31}", kind, null, key31, old, now,
            Capture("Completed", null), run ?? Run(RunState.Completed), EquivalenceFixture.Migrated("composer-gates"), plan ?? EquivalenceFixture.Plan("composer-gates"));

    /// <summary>A context on a package with its hand edits made (Task 16), so its own plan is read.</summary>
    private static DifferenceContext EditedContext(string package, string scenario, ComparedKind kind, string old, string now, string key31) =>
        new(package, scenario, $"{package}/{scenario}#{key31}", kind, null, key31, old, now,
            Capture("Completed", null), Run(RunState.Completed), EquivalenceFixture.Migrated(package), EquivalenceFixture.Plan(package));

    /// <summary>A deliverable of composer-macros (edited, Task 16), so its own plan is read.</summary>
    private static DifferenceContext MacroContext(Migration31Result migrated, string old, string now, OfflineRequest? request = null, string key31 = "deliverable.sections_note") =>
        new("composer-macros", "default", $"composer-macros/default#{key31}", ComparedKind.Deliverable, null, key31, old, now,
            Capture("Completed", null), Run(RunState.Completed), migrated, EquivalenceFixture.Plan("composer-macros"), request);

    /// <summary><paramref name="package"/> as compared, with <paramref name="path"/> holding a 30 macro's body (<see cref="Migration31Result.MacroBodies"/>).</summary>
    private static Migration31Result WithMacro(string package, string path, string body)
    {
        var migrated = EquivalenceFixture.Migrated(package);
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
