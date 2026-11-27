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
        "failed-job-dispatch-order", "flag-becomes-retry", "reply-never-a-template", "escaped-plain-text", "item-ids-from-one", "position-names-from-the-loop",
        "sent-format", "explicit-null-for-optional", "macro-trailing-whitespace", "input-trailing-whitespace", "before-after-macro-marker",
        "while-no-pass-separator", "loop-item-not-run", "chosen-macro-final-line-ending", "empty-read-trailing-space",
        "macro-choice-classifier", "blank-conversion",
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

        // #1508 (comment 5, item 4): a loop with an itemLabel (composer-sources' topic_loop, "Topics" over "Requested topics",
        // itemLabel title) names an item whose field is empty by its position, the loop's label at 31 where 30 gave the
        // input's: accepted. An item named by its field is the same at both, so a heading 30 wrote "## Requested topics 4"
        // is the fallback on both sides.
        Assert.StartsWith("position-names-from-the-loop: ", Explain(EditedContext("composer-sources", "item-names-and-bounds", ComparedKind.Deliverable,
            "## History\n\nStub.\n\n## Requested topics 4\n\nStub four.", "## History\n\nStub.\n\n## Topics 4\n\nStub four.", "deliverable.topics")));
        Assert.Null(ExpectedDifferences.Explain(EditedContext("composer-sources", "item-names-and-bounds", ComparedKind.Deliverable,
            "## History\n\nStub.\n\n## Requested topics 4\n\nStub four.", "## History\n\nStub.\n\n## Topics 5\n\nStub four.", "deliverable.topics")));
    }

    [Fact]
    public void SentFormat_ExplainsTheMigratedFormatAppendedToAPrompt()
    {
        // Part 10 § 1.8 only: every property required, an optional one nullable, in canonical order.
        const string Prompt = "Draft the plan for Margaret Okafor.\n\n" + AgentTurn.CustomTrailer + "\n";
        const string At30 = """{"type":"object","additionalProperties":false,"required":["summary"],"properties":{"summary":{"type":"string"},"note":{"type":"string"}}}""";
        const string At31 = """{"additionalProperties":false,"properties":{"note":{"type":["string","null"]},"summary":{"type":"string"}},"required":["note","summary"],"type":"object"}""";
        Assert.StartsWith("sent-format: ", Explain(Context(ComparedKind.Prompt, Prompt + At30, Prompt + At31, key31: "node.plan", key30: "plan")));

        // #1508 (comment 5, item 7, #3): 31 also sends the format's description, at the top or on a property, where 30's had
        // none there.
        Assert.StartsWith("sent-format: ", Explain(Context(ComparedKind.Prompt, Prompt + At30, Prompt + At31.Insert(1, "\"description\":\"A plan.\","), key31: "node.plan")));
        Assert.StartsWith("sent-format: ", Explain(Context(ComparedKind.Prompt, Prompt + At30, Prompt + At31.Replace("""{"type":"string"}}""", """{"description":"One line.","type":"string"}}""", StringComparison.Ordinal), key31: "node.plan")));

        // Near: a title, $schema or $id 31 sends (no ruling declares them), and a description that differs from the one 30
        // sent, stay unexpected even with § 1.8's change beside them.
        foreach (var (member, at) in new[] { ("\"title\":\"Plan\",", 1), ("\"$id\":\"urn:x\",", 1), ("\"$schema\":\"urn:x\",", 1) })
            Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, Prompt + At30, Prompt + At31.Insert(at, member), key31: "node.plan")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, Prompt + At30.Insert(1, "\"description\":\"A plan.\","), Prompt + At31.Insert(1, "\"description\":\"The plan.\","), key31: "node.plan")));

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
    public void ADroppedAnswerLine_IsCreditedToSentFormat()
    {
        const string Body = "Decide whether the referral for Margaret Okafor is in scope.";
        const string At30 = Body + "\n\nAnswer with exactly one of: in_scope, out_of_scope.";
        const string Answers = """{"additionalProperties":false,"properties":{"value":{"enum":["in_scope","out_of_scope"],"type":"string"}},"required":["value"],"type":"object"}""";
        const string At31 = Body + "\n\n" + AgentTurn.CustomTrailer + "\n" + Answers;
        Assert.StartsWith("sent-format: ", Explain(Context(ComparedKind.Prompt, At30, At31, key31: "node.scope", key30: "scope")));

        // #1508 (comment 5, item 7, #3): a description sent with the answers' format.
        Assert.StartsWith("sent-format: ", Explain(Context(ComparedKind.Prompt, At30, At31.Replace("{\"additionalProperties\"", "{\"description\":\"One value.\",\"additionalProperties\"", StringComparison.Ordinal), key31: "node.scope")));

        // Near: a 30 text that does not end with the line; other values, or another order, than 30 listed; 30 listed none; a format that is not the answers' shape (a
        // title sent, another member); the text before it differs; a deliverable.
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, At30 + " More.", At31, key31: "node.scope")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, At30, At31.Replace("\"out_of_scope\"]", "\"needs_information\"]", StringComparison.Ordinal), key31: "node.scope")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, At30, At31.Replace("[\"in_scope\",\"out_of_scope\"]", "[\"out_of_scope\",\"in_scope\"]", StringComparison.Ordinal), key31: "node.scope")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, "Decide.", "Decide.\n\n" + AgentTurn.CustomTrailer + "\n" + Answers, key31: "node.scope")));
        Assert.Null(ExpectedDifferences.Explain(Context(ComparedKind.Prompt, At30, At31.Replace("{\"additionalProperties\"", "{\"title\":\"One value.\",\"additionalProperties\"", StringComparison.Ordinal), key31: "node.scope")));
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

    // ---- each difference credited on its own (#1508, comment 5, item 5; R19) -----------------------------------

    [Fact]
    public void ARuledDifferenceBesideAnUnexplainedOne_IsStillCredited()
    {
        // The item's number moved by one (item-ids-from-one) and a word changed that no rule explains: the rule is credited,
        // and the text is unexplained, so it is not expected.
        const string Old = "What bears on the question:\nstub 1b171b8d\n\nPrior letter 0:\n2026-03-02, urate 0.55 mmol/L.\n";
        const string New = "What bear on the question:\nstub 1b171b8d\n\nPrior letter 1:\n2026-03-02, urate 0.55 mmol/L.\n";
        var credited = ExpectedDifferences.Credit(LettersContext(Old, New, "letter_summary:0", "node.letters.letter_summary:1"));

        Assert.Equal(["item-ids-from-one"], credited.Rules.Select(r => r.Id));
        Assert.True(credited.Unexplained);
        Assert.False(credited.Expected);
        Assert.Null(ExpectedDifferences.Explain(LettersContext(Old, New, "letter_summary:0", "node.letters.letter_summary:1")));

        // A rule is credited only where it brings 31's text nearer 30's: the item's number standing where 30's text has
        // another number is not the rule's.
        var far = ExpectedDifferences.Credit(LettersContext(Old.Replace("letter 0", "letter 7", StringComparison.Ordinal), New, "letter_summary:0", "node.letters.letter_summary:1"));
        Assert.Equal((0, true), (far.Rules.Count, far.Unexplained));

        // No rule: nothing credited, unexplained; equal after the rules: explained.
        var none = ExpectedDifferences.Credit(Context(ComparedKind.Prompt, "Encounter date: 09 Nov 2026", "Encounter date: 2026-11-09", key31: "node.plan", key30: "plan"));
        Assert.Equal((0, true, false, (string?)null), (none.Rules.Count, none.Unexplained, none.Expected, none.Explanation));
    }

    [Fact]
    public void TwoRuledDifferences_AreBothCredited()
    {
        // An item's number from 1 and a description sent with the format: both rules, in Rules' order, and nothing is left.
        const string Trailer = "\n\n" + AgentTurn.CustomTrailer + "\n";
        const string At30 = """{"type":"object","additionalProperties":false,"required":["note"],"properties":{"note":{"type":"string"}}}""";
        const string At31 = """{"additionalProperties":false,"description":"What bears on the question.","properties":{"note":{"type":"string"}},"required":["note"],"type":"object"}""";
        const string Old = "What bears on the question:\n\nPrior letter 0:\n2026-03-02, urate 0.55 mmol/L." + Trailer + At30;
        const string New = "What bears on the question:\n\nPrior letter 1:\n2026-03-02, urate 0.55 mmol/L." + Trailer + At31;
        var credited = ExpectedDifferences.Credit(LettersContext(Old, New, "letter_summary:0", "node.letters.letter_summary:1"));

        Assert.Equal(["item-ids-from-one", "sent-format"], credited.Rules.Select(r => r.Id));
        Assert.False(credited.Unexplained);
        Assert.True(credited.Expected);
        Assert.Equal(string.Join("; ", credited.Rules.Select(r => r.Said)), credited.Explanation);
    }

    // ---- the rules of #1508, comment 5, item 7 -----------------------------------------------------------------

    [Fact]
    public void ReplyNeverATemplate_ExplainsAReplyThat30RenderedAsATemplate()
    {
        // #5: 30 rendered a reply as a template before it composed; 31 never does, so 31's text holds the reply as written
        // where 30's holds it rendered (composer-sources' draft_plan, its reply holding {{ 3 * 2 }} and {{ "{{" }}).
        const string Reply = "Repeat the echo in {{ 3 * 2 }} months.\nThe braces {{ \"{{\" }} and {{ \"}}\" }} stay literal.";
        const string Rendered = "Repeat the echo in 6 months.\nThe braces {{ and }} stay literal.";
        var run = Run(RunState.Completed, outputs: new() { ["node.plan_gate.draft_plan"] = Reply });
        Assert.StartsWith("reply-never-a-template: ", Explain(SourcesContext(ComparedKind.Deliverable, "# Plan\n\n" + Rendered, "# Plan\n\n" + Reply, "deliverable.plan", run)));

        // The reply as a migrated deliverable reads it, stripped (#1508, 2026-11-26, item 3: `| string.strip` on each source):
        // the reply served ends with a line ending, which neither text holds.
        var served = Run(RunState.Completed, outputs: new() { ["node.plan_gate.draft_plan"] = "# Plan\n\n" + Reply + "\n" });
        var stripped = ExpectedDifferences.Credit(SourcesContext(ComparedKind.Deliverable, "# Plan\n\n" + Rendered, "# Plan\n\n" + Reply, "deliverable.plan", served));
        Assert.Equal(["reply-never-a-template"], stripped.Rules.Select(r => r.Id));
        Assert.True(stripped.Expected);

        // Near: the reply served is stripped at 31, but 30's text kept something else than the reply rendered and stripped.
        Assert.Null(ExpectedDifferences.Explain(SourcesContext(ComparedKind.Deliverable, "# Plan\n\nSomething else kept.", "# Plan\n\n" + Reply, "deliverable.plan", served)));

        // Near: a reply the run did not serve; 30's text is not the rendered reply (another number); a reply that does not
        // render at 30 (a variable its scope lacks), written the same at 31.
        Assert.Null(ExpectedDifferences.Explain(SourcesContext(ComparedKind.Deliverable, "# Plan\n\n" + Rendered, "# Plan\n\n" + Reply, "deliverable.plan")));
        Assert.Null(ExpectedDifferences.Explain(SourcesContext(ComparedKind.Deliverable, "# Plan\n\n" + Rendered.Replace("6", "7", StringComparison.Ordinal), "# Plan\n\n" + Reply, "deliverable.plan", run)));
        var unknown = Run(RunState.Completed, outputs: new() { ["node.plan_gate.draft_plan"] = "In {{ months }} months." });
        Assert.Null(ExpectedDifferences.Explain(SourcesContext(ComparedKind.Deliverable, "In 6 months.", "In {{ months }} months.", "deliverable.plan", unknown)));

        // The job 30 failed because its reply did not render (unparseable-reply), which 31 completes: the status, and each
        // deliverable only 31 composed.
        var failure = Capture("Failed", "Drafting the plan failed: Node 'draft_plan' reply failed to render: <input>(1,23) : error : The variable or function `months` was not found");
        var completed = Run(RunState.Completed, rows: [Row("node.plan_gate.draft_plan", PlanKind.Agent, RowState.Completed)]);
        Assert.StartsWith("reply-never-a-template: ", Explain(SourcesContext(ComparedKind.Status, "Failed", "Completed", null, completed, failure)));
        Assert.StartsWith("reply-never-a-template: ", Explain(SourcesContext(ComparedKind.Deliverable, null, "In {{ months }} months.", "deliverable.plan", completed, failure)));

        // Near: 31 failed too; 30 failed otherwise; the node did not complete at 31; a prompt only 31 sent.
        Assert.Null(ExpectedDifferences.Explain(SourcesContext(ComparedKind.Status, "Failed", "Completed", null, Run(RunState.Failed, rows: [Row("node.plan_gate.draft_plan", PlanKind.Agent, RowState.Completed)]), failure)));
        Assert.Null(ExpectedDifferences.Explain(SourcesContext(ComparedKind.Status, "Failed", "Completed", null, completed, Capture("Failed", "Node 'draft_plan' failed."))));
        Assert.Null(ExpectedDifferences.Explain(SourcesContext(ComparedKind.Status, "Failed", "Completed", null, Run(RunState.Completed, rows: [Row("node.plan_gate.draft_plan", PlanKind.Agent, RowState.Failed)]), failure)));
        Assert.Null(ExpectedDifferences.Explain(SourcesContext(ComparedKind.Prompt, null, "A prompt.", "node.closing", completed, failure)));
    }

    [Fact]
    public void InputTrailingWhitespace_ExplainsAnInputs31WhiteSpace()
    {
        // #6: 31 keeps an input's trailing white space where 30 trimmed it (composer-sources' "  Padded history  ", and a
        // title of white space alone).
        var request = Inputs(new JsonObject { ["topics"] = new JsonArray(new JsonObject { ["title"] = "  Padded history  " }, new JsonObject { ["title"] = "   " }) });
        Assert.StartsWith("input-trailing-whitespace: ", Explain(SourcesContext(ComparedKind.Prompt,
            "On the topic \"  Padded history\" from this draft.", "On the topic \"  Padded history  \" from this draft.", "node.topic_loop.topic_gate.draft_topic:1", request: request)));
        Assert.StartsWith("input-trailing-whitespace: ", Explain(SourcesContext(ComparedKind.Prompt,
            "On the topic \"\" from this draft.\n   Indented.", "On the topic \"   \" from this draft.\n   Indented.", "node.topic_loop.topic_gate.draft_topic:4", request: request)));

        // Near: 30 trimmed the leading white space too; white space no input holds; the request not passed.
        Assert.Null(ExpectedDifferences.Explain(SourcesContext(ComparedKind.Prompt,
            "On the topic \"Padded history\" from this draft.", "On the topic \"  Padded history  \" from this draft.", "node.topic_loop.topic_gate.draft_topic:1", request: request)));
        Assert.Null(ExpectedDifferences.Explain(SourcesContext(ComparedKind.Prompt,
            "On the topic \"History\" from this draft.", "On the topic \"History \" from this draft.", "node.topic_loop.topic_gate.draft_topic:1", request: request)));
        Assert.Null(ExpectedDifferences.Explain(SourcesContext(ComparedKind.Prompt,
            "On the topic \"  Padded history\" from this draft.", "On the topic \"  Padded history  \" from this draft.", "node.topic_loop.topic_gate.draft_topic:1")));
    }

    [Fact]
    public void FailedJobDispatchOrder_ExplainsATextOneSideSentBeforeTheFailure()
    {
        // #9: both jobs failed at the same reply (draft_topic's third item came back empty, so its failIfEmpty failed it),
        // and each sent what its dispatch order reached first: a text only one side has is explained.
        var capture = Capture("Failed", "A for over an input could not assemble: topic_gate:2 did not complete.");
        capture["outputs"]!["draft_topic:2"] = "";
        var failed = Run(RunState.Failed, rows: [Row("node.topic_loop.topic_gate.draft_topic:3", PlanKind.Agent, RowState.Failed, "A drafted topic came back empty.")]);
        Assert.StartsWith("failed-job-dispatch-order: ", Explain(SourcesContext(ComparedKind.Prompt, "List the points.", null, "node.section_loop.outline:1", failed, capture)));
        Assert.StartsWith("failed-job-dispatch-order: ", Explain(SourcesContext(ComparedKind.Deliverable, "## Assessment", null, "deliverable.sections", failed, capture)));
        Assert.StartsWith("failed-job-dispatch-order: ", Explain(SourcesContext(ComparedKind.Prompt, null, "Only 31 reached this.", "node.closing", failed, capture)));

        // Near: a text both sides have; 31 completed; 31 failed for another reason; 30's reply was not empty; 30 completed.
        Assert.Null(ExpectedDifferences.Explain(SourcesContext(ComparedKind.Prompt, "List the points.", "List the points!", "node.section_loop.outline:1", failed, capture)));
        Assert.Null(ExpectedDifferences.Explain(SourcesContext(ComparedKind.Prompt, "List the points.", null, "node.section_loop.outline:1", Run(RunState.Completed), capture)));
        var other = Run(RunState.Failed, rows: [Row("node.topic_loop.topic_gate.draft_topic:3", PlanKind.Agent, RowState.Failed, "The reply has no marker for macro 'x'.")]);
        Assert.Null(ExpectedDifferences.Explain(SourcesContext(ComparedKind.Prompt, "List the points.", null, "node.section_loop.outline:1", other, capture)));
        var answered = (JsonObject)capture.DeepClone();
        answered["outputs"]!["draft_topic:2"] = "Examination paragraph.";
        Assert.Null(ExpectedDifferences.Explain(SourcesContext(ComparedKind.Prompt, "List the points.", null, "node.section_loop.outline:1", failed, answered)));
        var completed30 = (JsonObject)capture.DeepClone();
        completed30["status"] = "Completed";
        Assert.Null(ExpectedDifferences.Explain(SourcesContext(ComparedKind.Prompt, "List the points.", null, "node.section_loop.outline:1", failed, completed30)));
    }

    [Fact]
    public void FailedJobDispatchOrder_ExplainsAReplyItsFormatRefusedAtBoth()
    {
        // #9; #1508 (comment 6, item 3, edge-answer-outside-set): triage answered outside its values, which 30 refused (onInvalid:
        // fail) and 31's enum refuses too; 30 stopped there, 31 had sent the problem prompts beside it.
        var capture = Capture("Failed", "Is the referral routine? failed: Node 'triage' reply does not conform to format 'classification' (onInvalid: fail): Classifier 'triage' answered outside its values.");
        capture["outputs"]!["triage"] = JsonNode.Parse("""{ "value": "soon" }""");
        JsonObject Invalid(string action, string reason = "Node 'triage' gave a reply that isn't triage_answer.") =>
            new() { ["invalid"] = new JsonObject { ["reason"] = reason, ["action"] = action, ["attempts"] = 1 } };
        var failed = Run(RunState.Failed, rows: [Row("node.triage", PlanKind.Agent, RowState.Failed, "Node 'triage' gave a reply that isn't triage_answer.", Invalid("fail"))]);
        DifferenceContext Outside(OfflineResult run, JsonObject captured, string? old = null) =>
            Context(ComparedKind.Prompt, old, "Write one paragraph.", "edge-answer-outside-set", "answer-outside-set", captured, run, key30: "problem_draft:0", key31: "node.problem_loop.problem_draft:1");
        Assert.StartsWith("failed-job-dispatch-order: ", Explain(Outside(failed, capture)));

        // Near: a text both sides have; 31's row failed another way (no invalid reply); 30 failed on another node; 30 holds no reply.
        Assert.Null(ExpectedDifferences.Explain(Outside(failed, capture, old: "Write one paragraph!")));
        Assert.Null(ExpectedDifferences.Explain(Outside(Run(RunState.Failed, rows: [Row("node.triage", PlanKind.Agent, RowState.Failed, "The call could not be made.")]), capture)));
        // Nor a reply 31 refused for its markers (S05-053's short form), not its format.
        Assert.Null(ExpectedDifferences.Explain(Outside(Run(RunState.Failed, rows: [Row("node.triage", PlanKind.Agent, RowState.Failed, "x", Invalid("fail", "macro follow_up: missing"))]), capture)));
        var other = (JsonObject)capture.DeepClone();
        other["failure"] = "Drafting a problem failed: Node 'problem_draft' reply does not conform to format 'prose' (onInvalid: fail).";
        Assert.Null(ExpectedDifferences.Explain(Outside(failed, other)));
        var unanswered = (JsonObject)capture.DeepClone();
        unanswered["outputs"]!.AsObject().Remove("triage");
        Assert.Null(ExpectedDifferences.Explain(Outside(failed, unanswered)));
    }

    [Fact]
    public void FlagBecomesRetry_ExplainsTheRetry_TheStatus_AndWhat30RanOnTheKeptReply()
    {
        // #1508 (comment 5, item 3; comment 6, item 3; edge-flag): summary declared onInvalid: flag, so 30 kept a reply that did
        // not fit and composed the note; at 31 it is retry, asked once more with the validation error, then failed.
        var capture = Capture("Completed", null);
        JsonObject Invalid(string action, int attempts) => new() { ["invalid"] = new JsonObject { ["reason"] = "Node 'summary' gave a reply that isn't summary.", ["action"] = action, ["attempts"] = attempts } };
        OfflineResult Failed(string action = "retry", int attempts = 2, string key = "node.summary") =>
            Run(RunState.Failed, rows: [Row(key, PlanKind.Agent, RowState.Failed, "Node 'summary' gave a reply that isn't summary.", Invalid(action, attempts))]);
        DifferenceContext Flag(ComparedKind kind, string? old, string? now, string? key30, string? key31, OfflineResult run, JsonObject? captured = null, Migration31Result? migrated = null) =>
            Context(kind, old, now, "edge-flag", "reply-does-not-fit", captured ?? capture, run, migrated, key30, key31);

        Assert.StartsWith("flag-becomes-retry: ", Explain(Flag(ComparedKind.Status, "Completed", "Failed", null, null, Failed())));
        // The retried prompt: only the retry's text is this rule's (R19), so a difference beside it is left to the other rules.
        const string Retry = "\n\nYour last reply could not be used: it did not fit the required format. Write the reply again.";
        Assert.StartsWith("flag-becomes-retry: ", Explain(Flag(ComparedKind.Prompt, "Summarise.\n", "Summarise." + Retry, "summary", "node.summary", Failed())));
        var beside = ExpectedDifferences.Credit(Flag(ComparedKind.Prompt, "Summarise.\n", "Summarise!" + Retry, "summary", "node.summary", Failed()));
        Assert.Equal(["flag-becomes-retry"], beside.Rules.Select(r => r.Id));
        Assert.True(beside.Unexplained);
        Assert.Null(ExpectedDifferences.Explain(Flag(ComparedKind.Prompt, "Summarise.\n", "Summarise.\n\nAgain.", "summary", "node.summary", Failed())));
        Assert.StartsWith("flag-becomes-retry: ", Explain(Flag(ComparedKind.Prompt, "Write one paragraph.", null, "problem_draft:0", "node.problem_loop.problem_draft:1", Failed())));
        Assert.StartsWith("flag-becomes-retry: ", Explain(Flag(ComparedKind.Deliverable, "## Problem list 1", null, null, "deliverable.note", Failed())));

        // Near: a text only 31 has; another prompt both have; the node was not asked again (fail), or asked once; 30 failed too;
        // the node was never flag at 30 (its notice not among the migration's).
        Assert.Null(ExpectedDifferences.Explain(Flag(ComparedKind.Prompt, null, "Only 31.", "problem_draft:0", "node.problem_loop.problem_draft:1", Failed())));
        Assert.Null(ExpectedDifferences.Explain(Flag(ComparedKind.Prompt, "Write one paragraph.", "Write one paragraph!", "problem_draft:0", "node.problem_loop.problem_draft:1", Failed())));
        Assert.Null(ExpectedDifferences.Explain(Flag(ComparedKind.Status, "Completed", "Failed", null, null, Failed(action: "fail", attempts: 1))));
        Assert.Null(ExpectedDifferences.Explain(Flag(ComparedKind.Status, "Completed", "Failed", null, null, Failed(attempts: 1))));
        Assert.Null(ExpectedDifferences.Explain(Flag(ComparedKind.Status, "Failed", "Failed", null, null, Failed(), Capture("Failed", "x"))));
        var notFlag = EquivalenceFixture.Migrated("edge-flag") with { Notices = [] };
        Assert.Null(ExpectedDifferences.Explain(Flag(ComparedKind.Status, "Completed", "Failed", null, null, Failed(), migrated: notFlag)));
    }

    [Fact]
    public void BeforeAfterMacroMarker_ExplainsTheInstructionAndTheBodysPlace()
    {
        // #10: composer-macros' section_intro and section_close, which 30's engine placed before and after section_draft's
        // reply, are markers at 31: the placing prompt holds each offered macro's instruction (the hand edit's), and the
        // texts that hold the bodies differ only in the white space around them, where 30 joined body and reply by a blank
        // line and 31 has what the model wrote around its marker.
        const string Prompt = "Write the section \"History\" of a clinic letter from this draft.\nDraft. (Fictional.)\n";
        const string Intro = "Write the following marker alone on the first line of your reply, before the section's text:\n{{~ macro.section_intro.content }}";
        const string Close = "Write the following marker alone on the last line of your reply, after the section's text:\n{{~ macro.section_close.content }}";
        Assert.StartsWith("before-after-macro-marker: ", Explain(MacrosContext(ComparedKind.Prompt, Prompt, Prompt + Intro + "\n" + Close + "\n", "node.section_loop.section_draft:1")));
        Assert.StartsWith("before-after-macro-marker: ", Explain(MacrosContext(ComparedKind.Prompt, Prompt, Prompt + Intro + "\n\n", "node.section_loop.section_draft:2")));

        const string Note30 = "## History\n\nThe History section follows.\n\nHistory paragraph.\n\nEnd of the History section.\n\n## Plan";
        const string Note31 = "## History\n\nThe History section follows.\nHistory paragraph.\n\n\nEnd of the History section.\n\n## Plan";
        Assert.StartsWith("before-after-macro-marker: ", Explain(MacrosContext(ComparedKind.Deliverable, Note30, Note31, "deliverable.sections_note")));

        // Near: an instruction of a macro the engine did not place; the prompt differs before the instructions; white space
        // away from the bodies; a row that cannot hold the bodies (signed_only reads letter_body, which places no macro).
        Assert.Null(ExpectedDifferences.Explain(MacrosContext(ComparedKind.Prompt, Prompt, Prompt + "Write the cc line.\n", "node.section_loop.section_draft:1")));
        Assert.Null(ExpectedDifferences.Explain(MacrosContext(ComparedKind.Prompt, Prompt, Prompt.Replace("History", "Plan", StringComparison.Ordinal) + Intro + "\n", "node.section_loop.section_draft:1")));
        Assert.Null(ExpectedDifferences.Explain(MacrosContext(ComparedKind.Deliverable, Note30, Note31.Replace("## History\n\n", "## History\n", StringComparison.Ordinal), "deliverable.sections_note")));
        Assert.Null(ExpectedDifferences.Explain(MacrosContext(ComparedKind.Deliverable, Note30, Note31, "deliverable.signed_only")));
    }

    [Fact]
    public void EscapedPlainText_ExplainsAComputedTextValue_AndALoopsContentAPromptReads()
    {
        // #13: a let of text (composer-sources' topic_tally) is escaped in a Markdown deliverable; its value, the run's own,
        // unescapes it.
        var tally = Run(RunState.Completed, rows: [Row("node.topic_tally", PlanKind.Computed, RowState.Completed, text: new JsonObject { ["content"] = "Topic section length: 131" })]);
        Assert.StartsWith("escaped-plain-text: ", Explain(SourcesContext(ComparedKind.Deliverable, "Topic section length: 131", "Topic section length\\: 131", "deliverable.tally", tally)));
        Assert.Null(ExpectedDifferences.Explain(SourcesContext(ComparedKind.Deliverable, "Topic section length: 131", "Topic section length\\: 131", "deliverable.tally")));

        // #1508 (comment 5, item 8, D23): a prompt that reads a Markdown loop's content (example-v12-constructs'
        // extract_letter_terms reads standards_letter_body, for its check) holds its plain-text items escaped; a prompt that
        // reads no escaped text is never escaped.
        const string Letter = "We have received this referral and will be in touch with an appointment.";
        var replied = Run(RunState.Completed, outputs: new() { ["node.ack_letter_body"] = Letter });
        Assert.StartsWith("escaped-plain-text: ", Explain(V12Context("Extract: " + Letter, "Extract: " + MarkdownEscape.Escape(Letter, startsLine: false), "node.extract_letter_terms", replied)));
        Assert.Null(ExpectedDifferences.Explain(V12Context("Extract: " + Letter, "Extract: " + MarkdownEscape.Escape(Letter, startsLine: false), "node.tone", replied)));
    }

    [Fact]
    public void WhileNoPassSeparator_ExplainsTheSeparatorBesideAWhileThatRanNoPass()
    {
        // The controller's ruling (Task 33's review): a while that ran no pass reads as empty text at both, but 30's composer
        // dropped its part, and 31's template keeps the blank line between it and the next source (summary_then_opening:
        // "{{ node.refine.content }}\n\n{{ node.opening.content }}"); no if can drop a separator and keep the text.
        var noPass = Run(RunState.Completed, rows: [Row("node.refine", PlanKind.While, RowState.Skipped, detail: new JsonObject { ["passes"] = 0 })]);
        Assert.StartsWith("while-no-pass-separator: ", Explain(SourcesContext(ComparedKind.Deliverable, "Opening line.", "\n\nOpening line.", "deliverable.summary_then_opening", noPass)));

        // Near: the while ran a pass; the blank line stands elsewhere; a deliverable whose template has no while beside a
        // separator (opening alone); a prompt.
        var onePass = Run(RunState.Completed, rows: [Row("node.refine", PlanKind.While, RowState.Completed, detail: new JsonObject { ["passes"] = 1 })]);
        Assert.Null(ExpectedDifferences.Explain(SourcesContext(ComparedKind.Deliverable, "Opening line.", "\n\nOpening line.", "deliverable.summary_then_opening", onePass)));
        Assert.Null(ExpectedDifferences.Explain(SourcesContext(ComparedKind.Deliverable, "Opening line.", "Opening line.\n\n", "deliverable.summary_then_opening", noPass)));
        Assert.Null(ExpectedDifferences.Explain(SourcesContext(ComparedKind.Deliverable, "Opening line.", "\n\nOpening line.", "deliverable.single", noPass)));
        Assert.Null(ExpectedDifferences.Explain(SourcesContext(ComparedKind.Prompt, "Opening line.", "\n\nOpening line.", "node.closing", noPass)));
    }

    [Fact]
    public async Task ALoopItemThatDidNotRun_IsExpected()
    {
        // #1508, 2026-11-26, item 4 (Q4 stands): edge-items-not-run's loop has three items and max 2. 30's template loop
        // wrote its fixed text for the third with its reads empty; 31's R16 guard writes nothing for it. The deliverable reads
        // the rendered node as Markdown, so its plain text is escaped as well.
        var (_, _, texts) = await EquivalenceFixture.Compared("edge-items-not-run", "third-problem-not-run");
        var render = texts.Single(t => t.Site == "edge-items-not-run/third-problem-not-run#renders/node.problem_lines");
        var deliverable = texts.Single(t => t.Site == "edge-items-not-run/third-problem-not-run#deliverables/deliverable.problem_list");
        Assert.Equal((false, true, false), (render.Equal, render.Expected, render.Unexplained));
        Assert.Equal(["loop-item-not-run"], render.Rules);
        Assert.Equal((false, true, false), (deliverable.Equal, deliverable.Expected, deliverable.Unexplained));
        Assert.Equal(["escaped-plain-text", "loop-item-not-run"], deliverable.Rules);
    }

    [Fact]
    public void ALoopItemThatDidNotRun_DoesNotExplainANearPair()
    {
        // Hand-made pairs on edge-items-not-run's plan: problem_lines guards each item of problem_loop on its state.
        const string Ran = "- Type 2 diabetes: Diabetes paragraph.\n- Metformin continued: Medication paragraph.\n";
        const string Third = "- Blood pressure of 148/92: \n";
        var notRun = ItemsRun(RowState.Completed, RowState.Completed, RowState.NotRun);
        Assert.StartsWith("loop-item-not-run: ", Explain(ItemsContext(ComparedKind.Render, Ran + Third, Ran, "node.problem_lines", notRun)));
        Assert.StartsWith("loop-item-not-run: ", Explain(ItemsContext(ComparedKind.Render, Third + Ran, Ran, "node.problem_lines", notRun)));

        // A 30-only line that lacks the guarded body's literal text ("- ", ": ").
        Assert.Null(ExpectedDifferences.Explain(ItemsContext(ComparedKind.Render, Ran + "Blood pressure of 148/92\n", Ran, "node.problem_lines", notRun)));
        Assert.Null(ExpectedDifferences.Explain(ItemsContext(ComparedKind.Render, Ran + "Blood pressure: 148/92 -\n", Ran, "node.problem_lines", notRun)));

        // Every item of the loop completed.
        var allRan = ItemsRun(RowState.Completed, RowState.Completed, RowState.Completed);
        Assert.Null(ExpectedDifferences.Explain(ItemsContext(ComparedKind.Render, Ran + Third, Ran, "node.problem_lines", allRan)));

        // One item did not run, but 30 has two lines only it wrote, apart.
        Assert.Null(ExpectedDifferences.Explain(ItemsContext(ComparedKind.Render, Third + Ran + Third, Ran, "node.problem_lines", notRun)));

        // A template loop that is not the migration's guarded form credits nothing.
        var unguarded = EquivalenceFixture.Migrated("edge-items-not-run");
        var files = new Dictionary<string, string>(unguarded.Files, StringComparer.Ordinal);
        var lines = files["nodes/problem_lines.md"];
        files["nodes/problem_lines.md"] = lines.Replace("{{ if e.state == \"completed\" }}", "", StringComparison.Ordinal).Replace("{{ else }}{{ end }}", "", StringComparison.Ordinal);
        Assert.NotEqual(lines, files["nodes/problem_lines.md"]);
        Assert.Null(ExpectedDifferences.Explain(ItemsContext(ComparedKind.Render, Ran + Third, Ran, "node.problem_lines", notRun, unguarded with { Files = files })));

        // A status is never explained.
        Assert.Null(ExpectedDifferences.Explain(ItemsContext(ComparedKind.Status, "Completed", "Failed", null, notRun)));
    }

    [Fact]
    public async Task AChosenMacrosFinalLineEnding_IsExpected()
    {
        // #1508, 2026-11-26, item 5 (no migration change): follow_up, a macro the model chose (R13), ends its body in a line
        // ending, which 31 drops as it renders a body (R5), where 30 wrote the body whole where the reply's marker stood.
        var (_, _, texts) = await EquivalenceFixture.Compared("edge-unruled-whitespace", "default");
        var letter = texts.Single(t => t.Site == "edge-unruled-whitespace/default#deliverables/deliverable.letter");
        Assert.Equal((false, true, false), (letter.Equal, letter.Expected, letter.Unexplained));
        Assert.Equal(["chosen-macro-final-line-ending"], letter.Rules);
    }

    [Fact]
    public void AChosenMacrosFinalLineEnding_DoesNotExplainANearPair()
    {
        // Hand-made pairs on edge-unruled-whitespace's plan (its hand edits made).
        const string Opening = "Margaret Okafor's diabetes was reviewed today; her HbA1c is 71 mmol/mol.\n";
        const string FollowUp = "A follow-up visit is booked in three months.";
        Assert.StartsWith("chosen-macro-final-line-ending: ", Explain(WhitespaceContext(
            Opening + FollowUp + "\n\nYours sincerely, Dr A. Sample", Opening + FollowUp + "\nYours sincerely, Dr A. Sample", "deliverable.letter")));

        // A line ending lost after text that is no chosen macro's body.
        Assert.Null(ExpectedDifferences.Explain(WhitespaceContext(
            Opening + "\n" + FollowUp + "\nYours sincerely, Dr A. Sample", Opening + FollowUp + "\nYours sincerely, Dr A. Sample", "deliverable.letter")));

        // More than one line ending lost after the body.
        Assert.Null(ExpectedDifferences.Explain(WhitespaceContext(
            Opening + FollowUp + "\n\n\nYours sincerely, Dr A. Sample", Opening + FollowUp + "\nYours sincerely, Dr A. Sample", "deliverable.letter")));

        // A macro the engine placed: problem_close's line ending lost is not this rule's; its kept white space (R5) is still
        // macro-trailing-whitespace's.
        const string Close = "Diabetes paragraph: HbA1c 71 mmol/mol.\nReviewed today (fictional).";
        Assert.Null(ExpectedDifferences.Explain(WhitespaceContext(Close + "\n\n## Problem list 2", Close + "\n## Problem list 2", "deliverable.problems")));
        var kept = ExpectedDifferences.Credit(WhitespaceContext(Close + "\n\n## Problem list 2", Close + "\n\n\n\n## Problem list 2", "deliverable.problems"));
        Assert.True(kept.Expected);
        Assert.Equal(["macro-trailing-whitespace"], kept.Rules.Select(r => r.Id));
    }

    [Fact]
    public async Task AnEmptyReadsTrailingSpace_IsExpected()
    {
        // #1508, 2026-11-26, item 5 (no migration change): clinic_line, a 30 macro written as a snippet (D4), ends its line
        // with a read of the unset clinic; 31's snippet keeps "Seen at: " where 30 trimmed the rendered macro. problem_close's
        // white space in the same deliverable is R5's and the engine placement's, credited beside it.
        var (_, _, texts) = await EquivalenceFixture.Compared("edge-unruled-whitespace", "default");
        var problems = texts.Single(t => t.Site == "edge-unruled-whitespace/default#deliverables/deliverable.problems");
        Assert.Equal((false, true, false), (problems.Equal, problems.Expected, problems.Unexplained));
        Assert.Equal(["macro-trailing-whitespace", "before-after-macro-marker", "empty-read-trailing-space"], problems.Rules);
    }

    [Fact]
    public void AnEmptyReadsTrailingSpace_DoesNotExplainANearPair()
    {
        // Hand-made pairs on edge-unruled-whitespace's plan: deliverable.problems places the snippet clinic_line,
        // "Seen at: {{ input.clinic.content }}"; deliverable.letter places none.
        Assert.StartsWith("empty-read-trailing-space: ", Explain(WhitespaceContext("Seen at:\n\n## Problem list 1", "Seen at: \n\n## Problem list 1", "deliverable.problems")));
        Assert.StartsWith("empty-read-trailing-space: ", Explain(WhitespaceContext("Seen at:", "Seen at: ", "deliverable.problems")));

        // A trailing space after literal text, not a read.
        Assert.Null(ExpectedDifferences.Explain(WhitespaceContext(
            "Seen at:\n\nDiabetes paragraph.\n", "Seen at:\n\nDiabetes paragraph. \n", "deliverable.problems")));

        // A space 31 has inside a line; a row that places no such snippet.
        Assert.Null(ExpectedDifferences.Explain(WhitespaceContext("Seen at:Clinic A\n", "Seen at: Clinic A\n", "deliverable.problems")));
        Assert.Null(ExpectedDifferences.Explain(WhitespaceContext("Seen at:\n\nYours", "Seen at: \n\nYours", "deliverable.letter")));
    }

    // ---- helpers ---------------------------------------------------------------------------------------------

    /// <summary>A deliverable of edge-unruled-whitespace (its hand edits made), so its own plan is read.</summary>
    private static DifferenceContext WhitespaceContext(string old, string now, string key31) =>
        new("edge-unruled-whitespace", "default", $"edge-unruled-whitespace/default#deliverables/{key31}", ComparedKind.Deliverable, null, key31, old, now,
            Capture("Completed", null), Run(RunState.Completed), EquivalenceFixture.Migrated("edge-unruled-whitespace"), EquivalenceFixture.Plan("edge-unruled-whitespace"));

    /// <summary>A 31 run of edge-items-not-run whose problem_loop items ended as <paramref name="states"/>, in order.</summary>
    private static OfflineResult ItemsRun(params RowState[] states) =>
        Run(RunState.Completed, rows: [.. states.Select((s, i) => Row($"node.problem_loop:{i + 1}", PlanKind.For, s))]);

    /// <summary>A context on edge-items-not-run (its hand edits made), so its own plan is read.</summary>
    private static DifferenceContext ItemsContext(ComparedKind kind, string old, string now, string? key31, OfflineResult run, Migration31Result? migrated = null) =>
        new("edge-items-not-run", "third-problem-not-run", $"edge-items-not-run/third-problem-not-run#{key31 ?? "status"}", kind, null, key31, old, now,
            Capture("Completed", null), run, migrated ?? EquivalenceFixture.Migrated("edge-items-not-run"), EquivalenceFixture.Plan("edge-items-not-run"));

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

    /// <summary>
    /// <paramref name="package"/> as compared, with <paramref name="path"/> holding a 30 macro's body
    /// (<see cref="Migration31Result.MacroBodies"/>), and no hand edit, so no macro reads as placed by 30's engine
    /// (before-after-macro-marker) and R5's rule is read alone.
    /// </summary>
    private static Migration31Result WithMacro(string package, string path, string body)
    {
        var migrated = EquivalenceFixture.Migrated(package);
        var files = new Dictionary<string, string>(migrated.Files, StringComparer.Ordinal)
        {
            [path] = "---\n{\"label\":\"Closing\",\"description\":null,\"if\":null,\"format\":\"markdown_block\",\"instruction\":[]}\n---\n" + body,
        };
        return migrated with { Files = files, MacroBodies = [path], HandEdits = [] };
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
        RunState? state, IReadOnlyList<SourceRefusal>? refusals = null, IReadOnlyList<DeliverableOutcome>? deliverables = null, IReadOnlyList<RunDecision>? decisions = null,
        Dictionary<string, JsonNode?>? outputs = null, IReadOnlyList<RunRow>? rows = null) =>
        new(state, null, refusals ?? [], [], [], deliverables ?? [], outputs ?? new Dictionary<string, JsonNode?>(StringComparer.Ordinal), rows ?? [], decisions ?? [], []);

    private static RunRow Row(string key, PlanKind kind, RowState state, string? reason = null, JsonObject? detail = null, JsonObject? text = null)
    {
        var end = key.IndexOfAny([':', '#']);
        return new(key, end < 0 ? key : key[..end], kind, state, reason, null, detail ?? new JsonObject(), text);
    }

    /// <summary>A context on composer-sources (edited, Task 16), so its own plan is read.</summary>
    private static DifferenceContext SourcesContext(
        ComparedKind kind, string? old, string? now, string? key31, OfflineResult? run = null, JsonObject? capture = null, OfflineRequest? request = null) =>
        new("composer-sources", "default", $"composer-sources/default#{key31 ?? "status"}", kind, null, key31, old, now,
            capture ?? Capture("Completed", null), run ?? Run(RunState.Completed), EquivalenceFixture.Migrated("composer-sources"), EquivalenceFixture.Plan("composer-sources"), request);

    /// <summary>A context on composer-macros (edited, Task 16).</summary>
    private static DifferenceContext MacrosContext(ComparedKind kind, string old, string now, string key31) =>
        new("composer-macros", "default", $"composer-macros/default#{key31}", kind, null, key31, old, now,
            Capture("Completed", null), Run(RunState.Completed), EquivalenceFixture.Migrated("composer-macros"), EquivalenceFixture.Plan("composer-macros"));

    /// <summary>A prompt of example-v12-constructs (no hand edit), so its own plan is read.</summary>
    private static DifferenceContext V12Context(string old, string now, string key31, OfflineResult run) =>
        new("example-v12-constructs", "default", $"example-v12-constructs/default#{key31}", ComparedKind.Prompt, null, key31, old, now,
            Capture("Completed", null), run, EquivalenceFixture.Migrated("example-v12-constructs"), EquivalenceFixture.Plan("example-v12-constructs"));

    private static OfflineRequest Inputs(JsonObject inputs) =>
        new(inputs.ToDictionary(i => i.Key, i => i.Value?.DeepClone(), StringComparer.Ordinal), new Dictionary<string, string?>(StringComparer.Ordinal),
            new JsonObject(), new Dictionary<string, JsonNode?>(StringComparer.Ordinal), Consultologist.PackageFormat.V31.Harness.ByteEquivalence.CapturedAtUtc);

    private static DeliverableOutcome Outcome(string key, DeliverableState state, string? text) =>
        new(key, key, "markdown_block", "", state, null, null, text, text is null ? null : CanonicalJson.HashText(text), ["pdf"], null);

    private static RunDecision Decision(string owner, bool result, string readHash, string? expressionHash = null) =>
        new(owner, "self", "blank(input.prior_notes.content)",
            expressionHash ?? CanonicalJson.Hash(JsonNode.Parse("""{"blank":[{"var":"input.prior_notes.content"}]}""")), result, null,
            [new RunRead("input.prior_notes.content", readHash)], false, false, new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc));
}
