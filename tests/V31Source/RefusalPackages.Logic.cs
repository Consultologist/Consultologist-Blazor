using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Source;

/// <summary>
/// #1501 (specVersion 31, area 04): the <c>refusal-packages</c> cases of chapter 3, the expressions: one for every
/// sentence a package reaches through <see cref="Consultologist.PackageFormat.V31.SourceCheck.Run"/> (the not-shipped
/// ones, each with its reason, are <see cref="RefusalPackagesTests"/>'s). As areas 02 and 03's cases are, each side is
/// <see cref="SourceFixture.Minimal"/> plus only what its rule needs (a second input of the needed format, a package
/// format, a computed node, a check, a macro), and each invalid side is its valid side with one text replacement, so it
/// gives exactly one refusal. Every rule reads inputs (and, through the checks' own operations, checks): reads through
/// <c>node</c>, <c>check</c>, <c>item</c>, <c>pass</c> and <c>param</c> are not typed until area 05 (D2), so only part
/// 03's own site rules are judged on them.
/// </summary>
internal static partial class RefusalPackages
{
    /// <summary>The read every case's rule starts from: the required input <c>consult_draft</c>, a <c>markdown_block</c>.</summary>
    private const string Draft = """{ "var": "input.consult_draft.content" }""";

    private const string PlanFile = "nodes/plan.md";

    private static IEnumerable<RefusalCase> Logic() =>
        WrittenForm().Concat(Operations()).Concat(Reads()).Concat(SiteRules()).Concat(Results()).Concat(Patterns());

    // ---- Chapter 3 § 3.2: the written form ---------------------------------------------------------

    private static IEnumerable<RefusalCase> WrittenForm()
    {
        const string blank = $$"""{ "blank": [{{Draft}}] }""";

        yield return PlanIfCase("S03-011",
            "Operands written as an array, even one (chapter 3 § 3.2.2). Invalid: the agent node plan's if writes "
            + "{ \"!\": { \"blank\": … } }. Valid: { \"!\": [{ \"blank\": … }] }.",
            $$"""{ "!": [{{blank}}] }""", $"[{blank}]", blank);

        yield return PlanIfCase("S03-012",
            "var takes one path, with no default (chapter 3 § 3.2.2). Invalid: the agent node plan's if reads "
            + "{ \"var\": [\"input.consult_draft.content\", \"\"] }. Valid: { \"var\": \"input.consult_draft.content\" }.",
            blank, "\"var\": \"input.consult_draft.content\"", "\"var\": [\"input.consult_draft.content\", \"\"]");

        yield return PlanIfCase("S03-013",
            "A path has no segment of digits: a list is not read by position (chapter 3 § 3.2.2). The input prior_notes is "
            + "a list of plaintext. Invalid: the agent node plan's if reads input.prior_notes.value.0. Valid: it tests the "
            + "size of input.prior_notes.value.",
            """{ "===": [{ "size": [{ "var": "input.prior_notes.value" }] }, 0] }""",
            """{ "size": [{ "var": "input.prior_notes.value" }] }""", """{ "blank": [{ "var": "input.prior_notes.value.0" }] }""",
            WithPriorNotes());

        yield return PlanIfCase("A04-004",
            "An operation is an object with one member (chapter 3 § 3.2.1; this implementation's sentence). Invalid: the "
            + "agent node plan's if is one object of two operations, ! and <. Valid: the and of the two.",
            $$"""{ "and": [{ "!": [{{blank}}] }, { "<": [{ "size": [{{Draft}}] }, 5000] }] }""",
            $$"""{ "and": [{ "!": [{{blank}}] }, { "<": [{ "size": [{{Draft}}] }, 5000] }] }""",
            $$"""{ "!": [{{blank}}], "<": [{ "size": [{{Draft}}] }, 5000] }""");

        yield return PlanIfCase("A04-005",
            "var takes one path, written as text (chapter 3 § 3.2.2; this implementation's sentence). Invalid: the agent "
            + "node plan's if writes { \"var\": null }. Valid: the path input.consult_draft.content.",
            blank, "\"var\": \"input.consult_draft.content\"", "\"var\": null");

        yield return PlanIfCase("A04-006",
            "A path is a root, then names joined by dots (chapter 3 § 3.2.2; this implementation's sentence). Invalid: the "
            + "agent node plan's if reads 'input.consult_draft..content', with an empty name. Valid: input.consult_draft.content.",
            blank, "\"input.consult_draft.content\"", "\"input.consult_draft..content\"");

        const string facts = """{ "object": { "words": { "words": [{ "var": "input.consult_draft.content" }] }, "empty": { "blank": [{ "var": "input.consult_draft.content" }] } } }""";
        yield return new RefusalCase("A04-007",
            "object takes one object, from field names to rules (chapter 3 § 3.2.2; this implementation's sentence). The "
            + "computed node draft_facts returns the package format draft_facts. Invalid: its expression writes object with "
            + "a list of the two field rules. Valid: with an object of them.",
            WithDraftFacts(facts),
            WithDraftFacts(facts).Edit("nodes/draft_facts.md",
                """{ "words": { "words": [{ "var": "input.consult_draft.content" }] }, "empty": { "blank": [{ "var": "input.consult_draft.content" }] } }""",
                """[{ "words": [{ "var": "input.consult_draft.content" }] }, { "blank": [{ "var": "input.consult_draft.content" }] }]"""));
    }

    // ---- Chapter 3 § 3.5: the operations ---------------------------------------------------------------

    private static IEnumerable<RefusalCase> Operations()
    {
        const string blank = $$"""{ "blank": [{{Draft}}] }""";
        const string size = $$"""{ "size": [{{Draft}}] }""";

        yield return PlanIfCase("S03-004",
            "Equality is written === (chapter 3 § 3.5.1). Invalid: the agent node plan's if tests the draft's size with ==. "
            + "Valid: with ===.",
            $$"""{ "===": [{{size}}, 0] }""", "\"===\"", "\"==\"");

        yield return PlanIfCase("S03-005",
            "Inequality is written !== (chapter 3 § 3.5.1). Invalid: the agent node plan's if tests the draft's size with !=. "
            + "Valid: with !==.",
            $$"""{ "!==": [{{size}}, 0] }""", "\"!==\"", "\"!=\"");

        yield return PlanIfCase("S03-006",
            "Only the operations of chapter 3 § 3.5 are allowed (chapter 3 § 3.5.1): !! is refused. Invalid: the agent node "
            + "plan's if writes { \"!!\": [{ \"blank\": … }] }. Valid: { \"!\": [{ \"blank\": … }] }.",
            $$"""{ "!": [{{blank}}] }""", "\"!\"", "\"!!\"");

        yield return PlanIfCase("S03-007",
            "substr is refused (chapter 3 § 3.5.1). Invalid: the agent node plan's if searches substr of the draft for "
            + "letrozole. Valid: it searches trim of the draft.",
            $$"""{ "in": ["letrozole", { "trim": [{{Draft}}] }] }""",
            $$"""{ "trim": [{{Draft}}] }""", $$"""{ "substr": [{{Draft}}, 0, 500] }""");

        yield return PlanIfCase("S03-008",
            "<, >, <= and >= compare numbers or dates (chapter 3 § 3.5.1). Invalid: the agent node plan's if orders the "
            + "draft's text against 5000. Valid: the draft's size.",
            $$"""{ ">": [{{size}}, 5000] }""", size, Draft);

        yield return PlanIfCase("S03-009",
            "! takes a boolean, not text (chapter 3 § 3.5.1). Invalid: the agent node plan's if writes ! of the draft's "
            + "text. Valid: ! of blank of it.",
            $$"""{ "!": [{{blank}}] }""", blank, Draft);

        yield return PlanIfCase("S03-010",
            "An operation takes its count of operands: a comparison takes two, so the three-operand between is refused "
            + "(chapter 3 § 3.5.1). Invalid: the agent node plan's if writes { \"<\": [100, size, 5000] }. Valid: "
            + "{ \"<\": [size, 5000] }.",
            $$"""{ "<": [{{size}}, 5000] }""", $"[{size}", $"[100, {size}");

        yield return PlanIfCase("S03-034",
            "if has an else: its operands are an odd count (chapter 3 § 3.5.1). Invalid: the agent node plan's if is "
            + "{ \"if\": [blank, false] }. Valid: { \"if\": [blank, false, true] }.",
            $$"""{ "if": [{{blank}}, false, true] }""", "false, true", "false");

        yield return new RefusalCase("S03-035",
            "cat takes strings, numbers and plain text; Markdown is converted with plain first (chapter 3 § 3.5.1). The "
            + "computed node draft_line returns plaintext. Invalid: its expression cats the draft's markdown_block. "
            + "Valid: it cats plain of it.",
            WithComputedNode("draft_line", "Writing the draft as one line", Plain,
                $$"""{ "cat": ["Draft: ", { "plain": [{{Draft}}] }] }"""),
            WithComputedNode("draft_line", "Writing the draft as one line", Plain,
                $$"""{ "cat": ["Draft: ", { "plain": [{{Draft}}] }] }""").Edit("nodes/draft_line.md", $$"""{ "plain": [{{Draft}}] }""", Draft));

        // #1508 (Task 30 fix round 1): the case pins that text is a member of a written list of string literals (the form
        // the migration gives 30's text == "lit") and of no list read from the data; arithmetic on text is the library's.
        yield return PlanIfCase("A04-001",
            "Each operand is of a type its operation takes: arithmetic takes numbers, and text is a member only of a "
            + "written list of string literals (chapter 3 § 3.5.1; this implementation's sentence). The input prior_notes "
            + "is a list of plaintext. Valid: the agent node plan's if multiplies the draft's count of words by 2 and tests "
            + "the draft in the written list [\"Reviewed in clinic.\"]. Invalid: the draft in the list input.prior_notes.value.",
            $$"""{ "and": [{ ">": [{ "*": [{ "words": [{{Draft}}] }, 2] }, 5000] }, { "in": [{{Draft}}, ["Reviewed in clinic."]] }] }""",
            """["Reviewed in clinic."]""", """{ "var": "input.prior_notes.value" }""", WithPriorNotes());

        yield return PlanIfCase("A04-002",
            "= and ≠ compare two values of one type (chapter 3 § 3.5.1; this implementation's sentence). Invalid: the "
            + "agent node plan's if compares the draft's size with the string \"0\". Valid: with the number 0.",
            $$"""{ "===": [{{size}}, 0] }""", ", 0]", ", \"0\"]");

        const string facts = """{ "object": { "words": { "words": [{ "var": "input.consult_draft.content" }] }, "empty": { "blank": [{ "var": "input.consult_draft.content" }] } } }""";
        const string empty = """, "empty": { "blank": [{ "var": "input.consult_draft.content" }] }""";

        yield return new RefusalCase("S03-027",
            "object gives every property of the computed node's format (chapter 3 § 3.5.4). The computed node draft_facts "
            + "returns the package format draft_facts, of words and empty. Invalid: its object gives words alone. Valid: both.",
            WithDraftFacts(facts),
            WithDraftFacts(facts).Edit("nodes/draft_facts.md", empty, ""));

        yield return new RefusalCase("S03-028",
            "object gives no field its format does not declare (chapter 3 § 3.5.4). The computed node draft_facts returns "
            + "the package format draft_facts, of words and empty. Invalid: its object also gives lines. Valid: words and empty.",
            WithDraftFacts(facts),
            WithDraftFacts(facts).Edit("nodes/draft_facts.md", empty,
                empty + """, "lines": { "lines": [{ "var": "input.consult_draft.content" }] }"""));

        yield return new RefusalCase("A04-008",
            "object builds the value of a computed node, where a format gives its fields (chapter 3 § 3.5.4; this "
            + "implementation's sentence). Invalid: the agent node plan's if tests blank of an object built from the "
            + "draft. Valid: blank of the draft.",
            PlanIf(blank),
            PlanIf(blank).Edit(PlanFile, $"[{Draft}]", $$"""[{ "object": { "text": {{Draft}} } }]"""));

        yield return new RefusalCase("A04-009",
            "Each field of an object fits its property (chapter 3 § 3.5.4; this implementation's sentence). The computed "
            + "node draft_facts returns the package format draft_facts, whose empty is a boolean. Invalid: its object "
            + "gives empty the draft's count of lines, an integer. Valid: blank of the draft.",
            WithDraftFacts(facts),
            WithDraftFacts(facts).Edit("nodes/draft_facts.md", "\"empty\": { \"blank\": ", "\"empty\": { \"lines\": "));

        yield return new RefusalCase("S03-030",
            "count_pass's min is a whole number, 0 or more (chapter 3 § 3.5.4). The check loop notes_checked checks each "
            + "prior note, and the check enough_notes counts those that pass. Invalid: its min is -1. Valid: 1.",
            WithCheckLoop(),
            WithCheckLoop().Edit("checks/enough_notes.md", "\"check.notes_checked.items\" }, 1,", "\"check.notes_checked.items\" }, -1,"));
    }

    // ---- Chapter 3 §§ 3.4 and 3.7: what a site reads, and typing -------------------------------------

    private static IEnumerable<RefusalCase> Reads()
    {
        const string blank = $$"""{ "blank": [{{Draft}}] }""";

        yield return PlanIfCase("S03-002",
            "Text is not compared with a literal; it is tested by blank, size or in (chapter 3 § 3.7). Invalid: the agent "
            + "node plan's if compares the draft with \"\" by ===. Valid: it tests blank of the draft.",
            blank, blank, $$"""{ "===": [{{Draft}}, ""] }""");

        const string sizes = """{ "===": [{ "words": [{ "var": "input.consult_draft.content" }] }, { "words": [{ "var": "input.referral_letter.content" }] }] }""";
        yield return PlanIfCase("S03-003",
            "Text is not compared with text (chapter 3 § 3.7). The optional input referral_letter is a markdown_block. "
            + "Invalid: the agent node plan's if compares the draft with the letter by ===. Valid: their counts of words.",
            sizes, sizes, """{ "===": [{ "var": "input.consult_draft.content" }, { "var": "input.referral_letter.content" }] }""",
            WithInput("referral_letter"));

        yield return PlanIfCase("S03-014",
            "A read names what the site can see (chapter 3 § 3.7). Invalid: the agent node plan's if reads "
            + "input.consult_drafts.content, an input the manifest does not declare. Valid: input.consult_draft.content.",
            blank, "\"input.consult_draft.content\"", "\"input.consult_drafts.content\"");

        yield return PlanIfCase("S03-015",
            "An input is read as input.<id>.value, .content or .title (chapter 3 § 3.4.1). Invalid: the agent node plan's "
            + "if reads input.consult_draft bare. Valid: input.consult_draft.content.",
            blank, "\"input.consult_draft.content\"", "\"input.consult_draft\"");

        const string kind = """{ "===": [{ "var": "input.note_kind.value" }, "table"] }""";
        yield return PlanIfCase("S03-016",
            ".title is not read on a plain enum (chapter 3 § 3.4.1). The input note_kind is a plain enum of narrative and "
            + "table. Invalid: the agent node plan's if reads input.note_kind.title. Valid: input.note_kind.value.",
            kind, "input.note_kind.value", "input.note_kind.title", WithNoteKind());

        const string specialty = """{ "===": [{ "var": "input.specialty.title" }, "Medical oncology"] }""";
        yield return PlanIfCase("S03-017",
            "A literal compared with a .title is one of the format's titles (chapter 3 § 3.4.1). The input specialty is a "
            + "labelled enum titled Medical oncology and Radiation oncology. Invalid: the agent node plan's if compares "
            + "its title with \"Surgery\". Valid: with \"Medical oncology\".",
            specialty, "\"Medical oncology\"", "\"Surgery\"", WithSpecialty());

        yield return PlanIfCase("S03-018",
            "No condition or computed expression reads the profile (chapter 3 § 3.4.1). Invalid: the agent node plan's if "
            + "tests blank of profile.signature. Valid: blank of the draft.",
            blank, "\"input.consult_draft.content\"", "\"profile.signature\"");

        yield return PlanIfCase("S03-019",
            "JSON Logic reads input, node, check, item, pass, param and run, never snippet, part, macro, renderer or "
            + "deliverable (chapter 3 § 3.4.1). Invalid: the agent node plan's if tests blank of snippet.closing_line.content. "
            + "Valid: blank of the draft.",
            blank, "\"input.consult_draft.content\"", "\"snippet.closing_line.content\"");

        yield return PlanIfCase("S03-023",
            "A literal compared with an enum is one of its values (chapter 3 § 3.7). The input note_kind is a plain enum of "
            + "narrative and table. Invalid: the agent node plan's if compares it with \"letter\". Valid: with \"table\".",
            kind, "\"table\"", "\"letter\"", WithNoteKind());

        const string dose = """{ ">": [{ "var": "input.medication_order.value.choice.dose" }, 0] }""";
        yield return PlanIfCase("S03-024",
            "A field read through a union is declared by every member (chapter 3 § 3.7). The input medication_order's "
            + "choice is a union of { dose, route } and { dose }. Invalid: the agent node plan's if reads choice.route. "
            + "Valid: choice.dose, which both declare.",
            dose, "choice.dose", "choice.route", WithMedicationOrder());
    }

    // ---- Chapter 3 § 3.4.2: the rules of a deliverable's and a macro's if ------------------------------

    private static IEnumerable<RefusalCase> SiteRules()
    {
        const string written = """{ "!": [{ "blank": [{ "var": "node.plan.value" }] }] }""";

        yield return new RefusalCase("S03-020",
            "A deliverable's if reads a node's data (.value), not its text (chapter 3 § 3.4.2). Invalid: the deliverable "
            + "note's if tests blank of node.plan.content. Valid: of node.plan.value.",
            DeliverableIf(written),
            DeliverableIf(written).Edit("deliverables/note.md", "node.plan.value", "node.plan.content"));

        const string draft = $$"""{ "!": [{ "blank": [{{Draft}}] }] }""";
        yield return new RefusalCase("S03-021",
            "A deliverable's existence does not depend on a check (chapter 3 § 3.4.2). The check draft_written passes when "
            + "the draft is not blank. Invalid: the deliverable note's if reads check.draft_written.value.pass. Valid: it "
            + "tests the draft itself.",
            DeliverableIf(draft, WithDraftWritten()),
            DeliverableIf(draft, WithDraftWritten()).Edit("deliverables/note.md", draft,
                """{ "===": [{ "var": "check.draft_written.value.pass" }, true] }"""));

        yield return new RefusalCase("S03-022",
            "A macro's if is shared, so it reads no function's parameter (chapter 3 § 3.4.2). The macro footer is placed "
            + "at the end of the note. Invalid: its if tests blank of param.draft.content. Valid: of the input consult_draft.",
            WithFooterMacro(draft),
            WithFooterMacro(draft).Edit("macros/footer.md", "input.consult_draft.content", "param.draft.content"));
    }

    // ---- Chapter 3 §§ 3.3 and 3.7: what a rule yields -----------------------------------------------------

    private static IEnumerable<RefusalCase> Results()
    {
        const string size = $$"""{ "size": [{{Draft}}] }""";

        yield return PlanIfCase("S03-025",
            "A condition yields a boolean (chapter 3 § 3.3). Invalid: the agent node plan's if is the draft's size, an "
            + "integer. Valid: whether it is over 5000.",
            $$"""{ ">": [{{size}}, 5000] }""", $$"""{ ">": [{{size}}, 5000] }""", size);

        yield return new RefusalCase("S03-026",
            "A computed node's expression yields its declared format (chapter 3 § 3.3). The computed node draft_length "
            + "declares number. Invalid: its expression is blank of the draft, a boolean. Valid: the draft's size, an "
            + "integer, which is a number.",
            WithComputedNode("draft_length", "Measuring the draft", "\"number\"", size),
            WithComputedNode("draft_length", "Measuring the draft", "\"number\"", size).Edit("nodes/draft_length.md", "\"size\"", "\"blank\""));

        const string branches = $$"""{ "if": [{ "blank": [{{Draft}}] }, 0, {{size}}] }""";
        yield return new RefusalCase("A04-003",
            "The branches of an if have one type (chapter 3 § 3.7; this implementation's sentence). The computed node "
            + "draft_length declares number. Invalid: its if gives \"none\" for a blank draft and the size otherwise. "
            + "Valid: 0 and the size.",
            WithComputedNode("draft_length", "Measuring the draft", "\"number\"", branches),
            WithComputedNode("draft_length", "Measuring the draft", "\"number\"", branches).Edit("nodes/draft_length.md", "}, 0, {", "}, \"none\", {"));
    }

    // ---- Chapter 3 § 3.5.3: matches and has_word ----------------------------------------------------------

    private static IEnumerable<RefusalCase> Patterns()
    {
        static string Matches(string pattern) => $$"""{ "matches": [{{Draft}}, "{{pattern}}"] }""";

        yield return PlanIfCase("S03-029",
            "A pattern is written as a literal, never computed (chapter 3 § 3.5.3). Invalid: the agent node plan's if "
            + "gives matches the pattern lower(\"LETROZOLE\"). Valid: the literal \"letrozole\".",
            Matches("letrozole"), "\"letrozole\"", """{ "lower": ["LETROZOLE"] }""");

        yield return PlanIfCase("S03-031",
            "A pattern is I-Regexp, RFC 9485 (chapter 3 § 3.5.3): it has no \\d. Invalid: the agent node plan's if matches "
            + "the draft against \"\\d{4}\". Valid: \"[0-9]{4}\".",
            Matches("[0-9]{4}"), "[0-9]{4}", "\\\\d{4}");

        yield return PlanIfCase("S03-032",
            "A pattern is at most 200 code points long (chapter 3 § 3.5.3). Invalid: the agent node plan's if matches the "
            + "draft against an alternation of drug names 201 code points long. Valid: 200, the limit.",
            Matches(DrugNames(200)), DrugNames(200), DrugNames(201));

        yield return PlanIfCase("S03-033",
            "has_word finds one word or phrase, not a blank one (chapter 3 § 3.5.3). Invalid: the agent node plan's if "
            + "gives has_word two spaces. Valid: \"chest pain\".",
            $$"""{ "has_word": [{{Draft}}, "chest pain"] }""", "\"chest pain\"", "\"  \"");

        yield return PlanIfCase("S03-042",
            "A counted repetition is at most 100 (chapter 3 § 3.5.3). Invalid: the agent node plan's if matches the draft "
            + "against \"[0-9]{101}\". Valid: \"[0-9]{100}\", the limit.",
            Matches("[0-9]{100}"), "{100}", "{101}");

        yield return PlanIfCase("S03-043",
            "A pattern compiles with the engine's options, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant "
            + "(chapter 3 § 3.5.3). Invalid: the agent node plan's if matches the draft against \"(a{100}){100}\", within "
            + "the other bounds, which .NET refuses as too large an automaton. Valid: \"(a{10}){10}\". The sentence's "
            + "reason is this implementation's: it needs too large an automaton, or .NET does not accept it.",
            Matches("(a{10}){10}"), "(a{10}){10}", "(a{100}){100}");

        yield return PlanIfCase("A04-010",
            "A pattern is written as text (chapter 3 § 3.5.3; this implementation's sentence). Invalid: the agent node "
            + "plan's if gives matches the number 5 as its pattern. Valid: the text \"5\".",
            Matches("5"), "\"5\"", "5");
    }

    /// <summary>
    /// <paramref name="length"/> code points of an alternation of drug names (<c>letrozole|femara|…</c>); for 200 and 201
    /// it ends in a letter, never a <c>|</c>.
    /// </summary>
    private static string DrugNames(int length) => Fit(string.Concat(Enumerable.Repeat("letrozole|femara|", 12)), length);

    // ---- The constructs these cases add to Minimal() ------------------------------------------------------

    /// <summary>
    /// A case on the agent node plan's <c>if</c>: the valid side is <paramref name="files"/> (Minimal() when none) with
    /// that <c>if</c> set to <paramref name="rule"/>; the invalid side is the same with <paramref name="old"/> replaced by
    /// <paramref name="replacement"/> in <c>nodes/plan.md</c>.
    /// </summary>
    private static RefusalCase PlanIfCase(string id, string about, string rule, string old, string replacement,
        Dictionary<string, byte[]>? files = null) =>
        new(id, about, PlanIf(rule, files), PlanIf(rule, files).Edit(PlanFile, old, replacement));

    /// <summary><paramref name="files"/> (Minimal() when none; a fresh copy each call) with the agent node plan's <c>if</c> set to <paramref name="rule"/>.</summary>
    private static Dictionary<string, byte[]> PlanIf(string rule, Dictionary<string, byte[]>? files = null) =>
        new Dictionary<string, byte[]>(files ?? Minimal()).Edit(PlanFile, "\"if\": null", $"\"if\": {rule}");

    /// <summary><paramref name="files"/> (Minimal() when none) with the deliverable note's <c>if</c> set to <paramref name="rule"/>.</summary>
    private static Dictionary<string, byte[]> DeliverableIf(string rule, Dictionary<string, byte[]>? files = null) =>
        (files ?? Minimal()).Edit("deliverables/note.md", "\"if\": null", $"\"if\": {rule}");

    /// <summary><paramref name="files"/> with one more required input, <paramref name="id"/>, whose format is the JSON <paramref name="format"/>.</summary>
    private static Dictionary<string, byte[]> WithRequiredInput(Dictionary<string, byte[]> files, string id, string label, string format) =>
        files.Edit("manifest.json", "\"channel\": null }\n  }",
            "\"channel\": null },\n"
            + $"    \"{id}\": {{ \"label\": \"{label}\", \"description\": null, \"format\": {format}, "
            + "\"required\": true, \"channel\": null }\n  }");

    /// <summary>Minimal() with the input <c>prior_notes</c>, a list of plaintext.</summary>
    private static Dictionary<string, byte[]> WithPriorNotes() =>
        WithRequiredInput(Minimal(), "prior_notes", "Prior notes", """{ "type": "array", "items": { "$ref": "plaintext" } }""");

    /// <summary>Minimal() with the input <c>note_kind</c>, a plain enum of narrative and table.</summary>
    private static Dictionary<string, byte[]> WithNoteKind() =>
        WithRequiredInput(Minimal(), "note_kind", "Kind of note", """{ "type": "string", "enum": ["narrative", "table"] }""");

    /// <summary>Minimal() with the input <c>specialty</c>, a labelled enum.</summary>
    private static Dictionary<string, byte[]> WithSpecialty() =>
        WithRequiredInput(Minimal(), "specialty", "Specialty",
            """{ "type": "string", "oneOf": [{ "const": "medical_oncology", "title": "Medical oncology" }, """
            + """{ "const": "radiation_oncology", "title": "Radiation oncology" }] }""");

    /// <summary>Minimal() with the input <c>medication_order</c>, whose <c>choice</c> is a union of <c>{ dose, route }</c> and <c>{ dose }</c>.</summary>
    private static Dictionary<string, byte[]> WithMedicationOrder()
    {
        const string dose = "\"dose\": { \"type\": \"number\", \"title\": \"Dose\" }";
        var choice = "{ \"anyOf\": ["
            + "{ \"type\": \"object\", \"required\": [\"dose\", \"route\"], \"additionalProperties\": false, "
            + $"\"properties\": {{ {dose}, \"route\": {{ \"type\": \"string\", \"title\": \"Route\" }} }} }}, "
            + $"{{ \"type\": \"object\", \"required\": [\"dose\"], \"additionalProperties\": false, \"properties\": {{ {dose} }} }}], \"title\": \"Choice\" }}";
        return WithRequiredInput(Minimal(), "medication_order", "Medication order",
            $"{{ \"type\": \"object\", \"required\": [\"choice\"], \"additionalProperties\": false, \"properties\": {{ \"choice\": {choice} }} }}");
    }

    /// <summary><paramref name="files"/> (Minimal() when none) with a computed node, <paramref name="id"/>, listed after plan.</summary>
    private static Dictionary<string, byte[]> WithComputedNode(string id, string label, string format, string expression,
        Dictionary<string, byte[]>? files = null) =>
        (files ?? Minimal())
            .Edit("nodes/index.json", "\"items\": [\"plan\"]", $"\"items\": [\"plan\", \"{id}\"]")
            .With($"nodes/{id}.md", Md(Node("computed", label, format, true, false, $"\"if\": null, \"expression\": {expression}"), ""));

    /// <summary>The computed node <c>draft_facts</c>, returning the package format <c>draft_facts</c> (<c>words</c>, a number; <c>empty</c>, a boolean).</summary>
    private static Dictionary<string, byte[]> WithDraftFacts(string expression) =>
        WithComputedNode("draft_facts", "Measuring the draft", "\"draft_facts\"", expression,
            WithFormatFile("draft_facts", ObjectOf(("words", "{ \"type\": \"number\" }"), ("empty", "{ \"type\": \"boolean\" }"))));

    /// <summary>A <c>check_result</c> that passes when <paramref name="pass"/> is true, written with object.</summary>
    private static string CheckResult(string pass) =>
        $"{{ \"object\": {{ \"pass\": {pass}, \"message\": null, \"uncovered\": null, \"untested\": null, \"passed\": null, \"total\": null }} }}";

    /// <summary>Minimal() with the check <c>draft_written</c>, which passes when the draft is not blank.</summary>
    private static Dictionary<string, byte[]> WithDraftWritten() =>
        Minimal()
            .Edit("checks/index.json", "\"items\": []", "\"items\": [\"draft_written\"]")
            .With("checks/draft_written.md", Md(Node("computed", "The draft is written", "\"check_result\"", true, false,
                "\"if\": null, \"expression\": " + CheckResult($$"""{ "!": [{ "blank": [{{Draft}}] }] }""")), ""));

    /// <summary>
    /// <see cref="WithPriorNotes"/> with the loop <c>notes</c>, which takes each prior note as it is, the check loop
    /// <c>notes_checked</c> over its <c>.items</c> (#1505: a check loop runs over a production loop's <c>.items</c>, S06-014),
    /// a check for each prior note that passes when it is not blank, and the check <c>enough_notes</c>, which counts those
    /// that pass (at least 1).
    /// </summary>
    private static Dictionary<string, byte[]> WithCheckLoop() =>
        WithPriorNotes()
            .Edit("nodes/index.json", "\"items\": [\"plan\"]", "\"items\": [\"plan\", \"notes\"]")
            .With("nodes/notes/index.json", Json(Node("for", "Prior notes", ArrayOf("plaintext"), true, false,
                "\"in\": \"input.prior_notes.value\", \"parallel\": true, \"maxParallel\": null, \"until\": null, \"max\": null, "
                + "\"itemLabel\": null, \"nodes\": [\"note\"], \"ret\": \"node.notes.note\"")))
            .With("nodes/notes/note.md", Md(Node("computed", "A prior note", "\"plaintext\"", true, false,
                "\"if\": null, \"expression\": { \"var\": \"item.value\" }"), ""))
            .Edit("checks/index.json", "\"items\": []", "\"items\": [\"notes_checked\", \"enough_notes\"]")
            .With("checks/notes_checked/index.json", Json(Node("for", "Prior notes checked", ArrayOf("check_result"), true, false,
                "\"in\": \"node.notes.items\", \"parallel\": true, \"maxParallel\": null, \"until\": null, \"max\": null, "
                + "\"itemLabel\": null, \"nodes\": [\"note_written\"], \"ret\": \"check.notes_checked.note_written\"")))
            .With("checks/notes_checked/note_written.md", Md(Node("computed", "The note has text", "\"check_result\"", true, false,
                "\"if\": null, \"expression\": " + CheckResult("""{ "!": [{ "blank": [{ "var": "item.item.value" }] }] }""")), ""))
            .With("checks/enough_notes.md", Md(Node("computed", "Enough prior notes", "\"check_result\"", true, false,
                "\"if\": null, \"expression\": { \"count_pass\": [{ \"var\": \"check.notes_checked.items\" }, 1, "
                + "\"No prior note has any text.\"] }"), ""));

    /// <summary>Minimal() with the macro <c>footer</c>, whose <c>if</c> is <paramref name="rule"/>.</summary>
    private static Dictionary<string, byte[]> WithFooterMacro(string rule) =>
        Minimal()
            .Edit("macros/index.json", "\"items\": []", "\"items\": [\"footer\"]")
            .With("macros/footer.md", Md(
                $"{{ \"label\": \"Footer\", \"description\": null, \"if\": {rule}, \"format\": \"plaintext\", "
                + "\"instruction\": [\"Place {{ macro.footer.content }} at the end of the note.\"] }",
                "Seen with Dr A. Sample.\n"));
}
