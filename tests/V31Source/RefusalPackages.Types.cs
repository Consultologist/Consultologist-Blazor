using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Source;

/// <summary>
/// #1500 (specVersion 31, area 03): the <c>refusal-packages</c> cases of chapter 2, the types: one for
/// every sentence a package reaches through <see cref="Consultologist.PackageFormat.V31.SourceCheck.Run"/>
/// (the not-shipped ones, each with its reason, are <see cref="RefusalPackagesTests"/>'s). As area
/// 02's cases are, each side is <see cref="SourceFixture.Minimal"/> plus the one construct its rule
/// needs (a format file, a second input, a while, an if), and each invalid side is its valid side with
/// one text replacement, so it gives exactly one refusal. For a limit the valid side sits on it
/// (100 properties, 5 levels).
/// </summary>
internal static partial class RefusalPackages
{
    private static IEnumerable<RefusalCase> Types() =>
        NodeTypes().Concat(Dialect()).Concat(Enums()).Concat(FormatsFolder()).Concat(Inputs()).Concat(Limits());

    // ---- Chapter 2 §§ 2.1–2.2 and § 2.7: kinds, producers and what a node returns ---------------------

    private static IEnumerable<RefusalCase> NodeTypes()
    {
        const string plan = "nodes/plan.md";

        yield return new RefusalCase("S01-027",
            "A leaf whose reproducible is not its kind's (chapter 2 § 2.1.1): an agent node is not reproducible. Invalid: the "
            + "agent node plan declares \"reproducible\": true. Valid: false.",
            Minimal(),
            Minimal().Edit(plan, "\"reproducible\": false", "\"reproducible\": true"));

        yield return new RefusalCase("S02-008",
            "kind let, which is now kind computed (chapter 2 § 2.1). Invalid: nodes/plan.md declares \"kind\": \"let\". "
            + "Valid: \"agent\".",
            Minimal(),
            Minimal().Edit(plan, "\"kind\": \"agent\"", "\"kind\": \"let\""));

        yield return new RefusalCase("S02-010",
            "A producer reserved for a later version (chapter 2 § 2.1.3). Invalid: the agent node plan declares "
            + "\"producer\": \"grounded\". Valid: \"generated\".",
            Minimal(),
            Minimal().Edit(plan, "\"producer\": \"generated\"", "\"producer\": \"grounded\""));

        yield return new RefusalCase("S02-012",
            "An agent node whose format has no binding for its producer (chapter 2 § 2.7). Invalid: the agent node plan "
            + "returns check_result, which no agent serves for generated. Valid: it returns plaintext.",
            Minimal(),
            Minimal().Edit(plan, "\"format\": \"plaintext\"", "\"format\": \"check_result\""));

        yield return new RefusalCase("S02-013",
            "A rendered node that does not return text (chapter 2 § 2.2.2). Invalid: the rendered node billing_text, in the "
            + "if billing_gate, returns number. Valid: plaintext.",
            WithIf(),
            WithIf().Edit("nodes/billing_gate/billing_text.md", "\"format\": \"plaintext\"", "\"format\": \"number\""));

        yield return new RefusalCase("S02-014",
            "An agent node whose format is written inline (chapter 2 § 2.2): an agent's format is named, so that it has a "
            + "binding. Invalid: the agent node plan declares \"format\": { \"$ref\": \"plaintext\" }. Valid: \"plaintext\".",
            Minimal(),
            Minimal().Edit(plan, "\"format\": \"plaintext\"", "\"format\": { \"$ref\": \"plaintext\" }"));

        yield return new RefusalCase("S02-015",
            "A flow node whose declared format is not what its ret gives (chapter 2 § 2.2.2): a while is its ret's format. "
            + "Invalid: the while refine declares markdown_block, and its ret, draft_summary, returns plaintext. Valid: it declares plaintext.",
            WithWhile(),
            WithWhile().Edit("nodes/refine/index.json", "\"format\": \"plaintext\"", "\"format\": \"markdown_block\""));

        yield return new RefusalCase("S02-016",
            "A branch that returns a format outside the if's declared union (chapter 2 § 2.2.2). The if billing_gate's then "
            + "returns plaintext and its else markdown_inline. Invalid: it declares the union plaintext | markdown_block. "
            + "Valid: plaintext | markdown_inline.",
            WithIfElse(),
            WithIfElse().Edit("nodes/billing_gate/index.json", "{ \"$ref\": \"markdown_inline\" }", "{ \"$ref\": \"markdown_block\" }"));

        yield return new RefusalCase("S02-017",
            "A format name that is no built-in, catalogue or package format (chapter 2 § 2.6.3). Invalid: the agent node "
            + "plan returns plain_text, a misspelt name. Valid: plaintext.",
            Minimal(),
            Minimal().Edit(plan, "\"format\": \"plaintext\"", "\"format\": \"plain_text\""));
    }

    /// <summary>
    /// <see cref="RefusalPackages.WithIf"/> whose if also has an <c>else</c>, rendering a line as
    /// <c>markdown_inline</c>, and so declares the union of its two bodies.
    /// </summary>
    private static Dictionary<string, byte[]> WithIfElse() =>
        WithIf()
            .Edit("nodes/billing_gate/index.json", "\"format\": \"plaintext\"",
                "\"format\": { \"anyOf\": [{ \"$ref\": \"plaintext\" }, { \"$ref\": \"markdown_inline\" }] }")
            .Edit("nodes/billing_gate/index.json", "\"else\": null",
                "\"else\": { \"nodes\": [\"no_billing\"], \"ret\": \"node.billing_gate.no_billing\" }")
            .With("nodes/billing_gate/no_billing.md", Md(
                Node("rendered", "No billing text", "\"markdown_inline\"", true, false, "\"if\": null"),
                "*No billing for this consultation.*\n"));

    // ---- Chapter 2 §§ 2.3–2.6: the dialect -------------------------------------------------------------

    private const string Verdict = "{ \"type\": \"string\", \"enum\": [\"good\", \"revise\"] }";
    private const string Reason = "{ \"type\": \"string\" }";

    private static IEnumerable<RefusalCase> Dialect()
    {
        const string review = "formats/review.json";

        yield return new RefusalCase("S02-021",
            "A keyword outside the formats dialect (chapter 2 § 2.3.1). Invalid: the package format review's reason declares "
            + "\"maxLength\": 500. Valid: the reason is { \"type\": \"string\" }.",
            WithReview("reason", Reason),
            WithReview("reason", Reason).Edit(review, Reason, "{ \"type\": \"string\", \"maxLength\": 500 }"));

        yield return new RefusalCase("S02-022",
            "A format file that does not name the dialect (chapter 2 § 2.3.1). Invalid: formats/review.json has no $schema. "
            + "Valid: \"$schema\": \"urn:consultologist:spec:31:dialect\".",
            WithReview("reason", Reason),
            WithReview("reason", Reason).Edit(review, $"{SchemaMember}, ", ""));

        yield return new RefusalCase("S02-023",
            "A format file that names another dialect (chapter 2 § 2.3.1). Invalid: formats/review.json declares the dialect "
            + "of specVersion 30. Valid: urn:consultologist:spec:31:dialect.",
            WithReview("reason", Reason),
            WithReview("reason", Reason).Edit(review, "spec:31:dialect", "spec:30:dialect"));

        yield return new RefusalCase("S02-024",
            "An object whose additionalProperties is not false (chapter 2 § 2.3.3). Invalid: the package format review "
            + "declares \"additionalProperties\": true. Valid: false.",
            WithReview("reason", Reason),
            WithReview("reason", Reason).Edit(review, "\"additionalProperties\": false", "\"additionalProperties\": true"));

        yield return new RefusalCase("S02-025",
            "A required name that is no property (chapter 2 § 2.3.3). Invalid: the package format review lists 'notes' in "
            + "required and declares no such property. Valid: required lists its two properties.",
            WithReview("reason", Reason),
            WithReview("reason", Reason).Edit(review, "\"required\": [\"verdict\", \"reason\"]", "\"required\": [\"verdict\", \"reason\", \"notes\"]"));

        yield return new RefusalCase("S02-026",
            "A property left out of required (chapter 2 § 2.3.3): an optional property is written as nullable. Invalid: the "
            + "package format review's required lists only verdict. Valid: verdict and reason.",
            WithReview("reason", Reason),
            WithReview("reason", Reason).Edit(review, "\"required\": [\"verdict\", \"reason\"]", "\"required\": [\"verdict\"]"));

        const string nullable = "{ \"type\": [\"string\", \"null\"] }";
        yield return new RefusalCase("S02-028",
            "A nullable scalar written with anyOf (chapter 2 § 2.3.2). Invalid: the package format review's reason is "
            + "{ \"anyOf\": [{ \"type\": \"string\" }, { \"type\": \"null\" }] }. Valid: { \"type\": [\"string\", \"null\"] }.",
            WithReview("reason", nullable),
            WithReview("reason", nullable).Edit(review, nullable, "{ \"anyOf\": [{ \"type\": \"string\" }, { \"type\": \"null\" }] }"));

        yield return new RefusalCase("S02-029",
            "A type array that is not one type plus \"null\" (chapter 2 § 2.3.2). Invalid: the package format review's reason "
            + "declares \"type\": [\"string\", \"number\"]. Valid: [\"string\", \"null\"].",
            WithReview("reason", nullable),
            WithReview("reason", nullable).Edit(review, "[\"string\", \"null\"]", "[\"string\", \"number\"]"));

        yield return new RefusalCase("S02-030",
            "A format other than date (chapter 2 § 2.3.1). Invalid: the package format review's seen_on declares "
            + "\"format\": \"date-time\". Valid: \"format\": \"date\", on a string.",
            WithReview("seen_on", "{ \"type\": \"string\", \"format\": \"date\" }"),
            WithReview("seen_on", "{ \"type\": \"string\", \"format\": \"date\" }").Edit(review, "\"format\": \"date\"", "\"format\": \"date-time\""));

        const string titled = "{ \"$ref\": \"plaintext\", \"title\": \"Reason\" }";
        yield return new RefusalCase("S02-031",
            "A keyword beside $ref other than title and description (chapter 2 § 2.3.1). Invalid: the package format "
            + "review's reason writes \"type\": \"string\" beside its $ref. Valid: only a title beside it.",
            WithReview("reason", titled),
            WithReview("reason", titled).Edit(review, titled, "{ \"$ref\": \"plaintext\", \"title\": \"Reason\", \"type\": \"string\" }"));

        const string earlier = "{ \"anyOf\": [{ \"$ref\": \"plaintext\" }, { \"type\": \"null\" }] }";
        yield return new RefusalCase("S02-032",
            "A format that refers to itself (chapter 2 § 2.3.1). Invalid: the package format review's earlier is a nullable "
            + "review. Valid: a nullable plaintext.",
            WithReview("earlier", earlier),
            WithReview("earlier", earlier).Edit(review, "{ \"$ref\": \"plaintext\" }", "{ \"$ref\": \"review\" }"));

        yield return new RefusalCase("S02-033",
            "A package format that writes its own $id (chapter 2 § 2.6.2): it is written at publish. Invalid: "
            + "formats/review.json declares a $id. Valid: none.",
            WithReview("reason", Reason),
            WithReview("reason", Reason).Edit(review, $"{SchemaMember}, ",
                $"{SchemaMember}, \"$id\": \"urn:consultologist:spec:31:format:review\", "));

        const string list = "\"type\": \"array\", \"items\": { \"$ref\": \"plaintext\" }";
        yield return new RefusalCase("S02-034",
            "A package format that is a scalar (chapter 2 § 2.4): a package defines object and array formats. Invalid: the "
            + "package format points is { \"type\": \"string\" }. Valid: a list of plaintext.",
            WithFormatFile("points", list),
            WithFormatFile("points", list).Edit("formats/points.json", list, "\"type\": \"string\""));

        yield return new RefusalCase("A03-001",
            "A package format that is neither an object, a list nor a scalar (chapter 2 § 2.4; this implementation's "
            + "sentence). Invalid: the package format points is an anyOf of plaintext and number. Valid: a list of plaintext.",
            WithFormatFile("points", list),
            WithFormatFile("points", list).Edit("formats/points.json", list,
                "\"anyOf\": [{ \"$ref\": \"plaintext\" }, { \"$ref\": \"number\" }]"));

        yield return new RefusalCase("S02-037",
            "A text annotation where it does not apply (chapter 2 § 2.5.1): a package format refers to a text format. "
            + "Invalid: the package format review's reason declares \"contentMediaType\": \"text/plain\". Valid: without it.",
            WithReview("reason", Reason),
            WithReview("reason", Reason).Edit(review, Reason, "{ \"type\": \"string\", \"contentMediaType\": \"text/plain\" }"));

        yield return new RefusalCase("S02-038",
            "A text annotation in an inline format (chapter 2 § 2.5.3): text inside an inline format is a $ref to a text "
            + "format. Invalid: the input referral_letter's inline format declares \"contentMediaType\": \"text/plain\". "
            + "Valid: { \"type\": \"string\" }.",
            WithReferral(Reason),
            WithReferral(Reason).Edit("manifest.json", Reason, "{ \"type\": \"string\", \"contentMediaType\": \"text/plain\" }"));

        yield return new RefusalCase("A03-002",
            "A #/$defs reference to a definition the file does not have (chapter 2 § 2.6.3; this implementation's sentence). "
            + "Invalid: the package format review's reason refers to #/$defs/reasons. Valid: #/$defs/reason, which its $defs holds.",
            WithReview("reason", "{ \"$ref\": \"#/$defs/reason\" }", "\"$defs\": { \"reason\": { \"type\": \"string\" } }, "),
            WithReview("reason", "{ \"$ref\": \"#/$defs/reason\" }", "\"$defs\": { \"reason\": { \"type\": \"string\" } }, ")
                .Edit(review, "\"#/$defs/reason\"", "\"#/$defs/reasons\""));

        yield return new RefusalCase("A03-003",
            "A keyword of the wrong JSON type (chapter 2 § 2.3.1; this implementation's sentence). Invalid: the package "
            + "format review's reason writes \"title\": 1. Valid: \"title\": \"Reason\".",
            WithReview("reason", titled),
            WithReview("reason", titled).Edit(review, "\"title\": \"Reason\"", "\"title\": 1"));

        yield return new RefusalCase("A03-004",
            "A keyword on a schema of another type (chapter 2 § 2.3.1; this implementation's sentence): enum belongs on a "
            + "string. Invalid: the package format review's verdict is a number with an enum. Valid: a string.",
            WithReview("reason", Reason),
            WithReview("reason", Reason).Edit(review, "{ \"type\": \"string\", \"enum\"", "{ \"type\": \"number\", \"enum\""));

        yield return new RefusalCase("A03-005",
            "A schema that declares no type (chapter 2 § 2.3.1; this implementation's sentence). Invalid: the package format "
            + "review's reason is { \"title\": \"Reason\" }. Valid: it refers to plaintext.",
            WithReview("reason", titled),
            WithReview("reason", titled).Edit(review, "\"$ref\": \"plaintext\", ", ""));

        yield return new RefusalCase("A03-006",
            "An object without one of properties, required and additionalProperties (chapter 2 § 2.3.3; this "
            + "implementation's sentence). Invalid: the package format review does not write additionalProperties. "
            + "Valid: \"additionalProperties\": false.",
            WithReview("reason", Reason),
            WithReview("reason", Reason).Edit(review, "\"additionalProperties\": false, ", ""));

        yield return new RefusalCase("A03-007",
            "A list without items (chapter 2 § 2.3.1; this implementation's sentence). Invalid: the package format points "
            + "is { \"type\": \"array\" }. Valid: its items refer to plaintext.",
            WithFormatFile("points", list),
            WithFormatFile("points", list).Edit("formats/points.json", list, "\"type\": \"array\""));

        yield return new RefusalCase("A03-008",
            "An anyOf of fewer than two members (chapter 2 § 2.3.1; this implementation's sentence). Invalid: the package "
            + "format review's earlier is an anyOf of plaintext alone. Valid: of plaintext and null.",
            WithReview("earlier", earlier),
            WithReview("earlier", earlier).Edit(review, ", { \"type\": \"null\" }", ""));
    }

    // ---- Chapter 2 § 2.9 and § 2.3.2: enums -------------------------------------------------------------

    private static IEnumerable<RefusalCase> Enums()
    {
        const string review = "formats/review.json";
        const string labelled = "{ \"type\": \"string\", \"oneOf\": [{ \"const\": \"good\", \"title\": \"Good\" }, "
            + "{ \"const\": \"revise\", \"title\": \"Revise\" }] }";
        const string nullableEnum = "{ \"type\": [\"string\", \"null\"], \"enum\": [\"good\", \"revise\", null] }";
        const string nullableLabelled = "{ \"type\": [\"string\", \"null\"], \"oneOf\": [{ \"const\": \"good\", \"title\": \"Good\" }, "
            + "{ \"const\": \"revise\", \"title\": \"Revise\" }, { \"const\": null }] }";

        yield return new RefusalCase("S02-047",
            "An enum with one value (chapter 2 § 2.9): a constant, not a choice. Invalid: the package format review's verdict "
            + "lists good alone. Valid: good and revise.",
            WithReview("reason", Reason),
            WithReview("reason", Reason).Edit(review, "[\"good\", \"revise\"]", "[\"good\"]"));

        yield return new RefusalCase("S02-048",
            "An enum value written twice (chapter 2 § 2.9). Invalid: the package format review's verdict lists good, revise, "
            + "good. Valid: good, revise.",
            WithReview("reason", Reason),
            WithReview("reason", Reason).Edit(review, "[\"good\", \"revise\"]", "[\"good\", \"revise\", \"good\"]"));

        yield return new RefusalCase("S02-049",
            "An enum value that is not snake_case (chapter 2 § 2.9). Invalid: the package format review's verdict lists "
            + "Revise. Valid: revise.",
            WithReview("reason", Reason),
            WithReview("reason", Reason).Edit(review, "[\"good\", \"revise\"]", "[\"good\", \"Revise\"]"));

        yield return new RefusalCase("S02-050",
            "Two values of a labelled enum with one title (chapter 2 § 2.9). Invalid: the package format review's verdict "
            + "titles both good and revise 'Good'. Valid: 'Good' and 'Revise'.",
            WithReview("verdict", labelled, verdict: false),
            WithReview("verdict", labelled, verdict: false).Edit(review, "\"title\": \"Revise\"", "\"title\": \"Good\""));

        yield return new RefusalCase("S02-051",
            "A labelled enum's branch of another shape (chapter 2 § 2.9): each is exactly { \"const\", \"title\" }. Invalid: "
            + "the package format review's verdict writes its second branch without a title. Valid: with one.",
            WithReview("verdict", labelled, verdict: false),
            WithReview("verdict", labelled, verdict: false).Edit(review, ", \"title\": \"Revise\"", ""));

        yield return new RefusalCase("S02-052",
            "A string with both enum and oneOf (chapter 2 § 2.9). Invalid: the package format review's labelled verdict also "
            + "declares an enum of its values. Valid: the oneOf alone.",
            WithReview("verdict", labelled, verdict: false),
            WithReview("verdict", labelled, verdict: false).Edit(review, "\"type\": \"string\", \"oneOf\"",
                "\"type\": \"string\", \"enum\": [\"good\", \"revise\"], \"oneOf\""));

        yield return new RefusalCase("S02-053",
            "A nullable plain enum that does not list null (chapter 2 § 2.3.2). Invalid: the package format review's verdict "
            + "is [\"string\", \"null\"] and lists good and revise. Valid: it lists null as well.",
            WithReview("verdict", nullableEnum, verdict: false),
            WithReview("verdict", nullableEnum, verdict: false).Edit(review, ", null]", "]"));

        yield return new RefusalCase("S02-054",
            "A nullable labelled enum without a { \"const\": null } arm (chapter 2 § 2.3.2). Invalid: the package format "
            + "review's verdict is [\"string\", \"null\"] and its oneOf has no such arm. Valid: with the arm.",
            WithReview("verdict", nullableLabelled, verdict: false),
            WithReview("verdict", nullableLabelled, verdict: false).Edit(review, ", { \"const\": null }", ""));

        yield return new RefusalCase("A03-010",
            "An enum that lists null on a type that is not nullable (chapter 2 § 2.3.2; this implementation's sentence). "
            + "Invalid: the package format review's verdict is \"type\": \"string\" and lists null. Valid: [\"string\", \"null\"].",
            WithReview("verdict", nullableEnum, verdict: false),
            WithReview("verdict", nullableEnum, verdict: false).Edit(review, "[\"string\", \"null\"]", "\"string\""));
    }

    // ---- Chapter 2 § 2.6.1: the formats folder ----------------------------------------------------------

    private static IEnumerable<RefusalCase> FormatsFolder()
    {
        yield return new RefusalCase("S02-039",
            "formats/index.json lists an id with no file (chapter 2 § 2.6.1). Invalid: it lists verdict, and formats/ has no "
            + "verdict.json. Valid: the file is there.",
            WithFormat("verdict"),
            WithFormat("verdict").Without("formats/verdict.json"));
    }

    // ---- Chapter 2 § 2.8: inputs ------------------------------------------------------------------------

    private static IEnumerable<RefusalCase> Inputs()
    {
        const string block = "\"format\": \"markdown_block\", \"required\": false";
        const string reasons = "{ \"type\": \"object\", \"required\": [\"reason\"], \"additionalProperties\": false, "
            + "\"properties\": { \"reason\": { \"$ref\": \"plaintext\", \"title\": \"Reason for referral\" } } }";

        yield return new RefusalCase("S02-040",
            "An input that takes markdown_inline (chapter 2 § 2.8.1). Invalid: the input referral_letter takes "
            + "markdown_inline. Valid: markdown_block.",
            WithReferral("\"markdown_block\""),
            WithReferral("\"markdown_block\"").Edit("manifest.json", block, "\"format\": \"markdown_inline\", \"required\": false"));

        yield return new RefusalCase("S02-041",
            "An input that takes a choice of shapes (chapter 2 § 2.8.1). Invalid: the input referral_letter takes an anyOf of "
            + "markdown_block and null. Valid: markdown_block.",
            WithReferral("\"markdown_block\""),
            WithReferral("\"markdown_block\"").Edit("manifest.json", block,
                "\"format\": { \"anyOf\": [{ \"$ref\": \"markdown_block\" }, { \"type\": \"null\" }] }, \"required\": false"));

        yield return new RefusalCase("S02-042",
            "A channel that is none of the four (chapter 2 § 2.8.3). Invalid: the input referral_letter declares "
            + "\"channel\": \"audio\". Valid: \"transcript\".",
            WithReferral("\"markdown_block\"", "\"transcript\""),
            WithReferral("\"markdown_block\"", "\"transcript\"").Edit("manifest.json", "\"transcript\"", "\"audio\""));

        yield return new RefusalCase("S02-043",
            "A text channel on an input that is not text (chapter 2 § 2.8.3). Invalid: the input referral_letter takes "
            + "boolean with \"channel\": \"transcript\". Valid: it takes markdown_block.",
            WithReferral("\"markdown_block\"", "\"transcript\""),
            WithReferral("\"markdown_block\"", "\"transcript\"").Edit("manifest.json", block, "\"format\": \"boolean\", \"required\": false"));

        yield return new RefusalCase("S02-044",
            "The form channel on an input that is not an object with a property (chapter 2 § 2.8.3). Invalid: the input "
            + "referral_letter takes markdown_block with \"channel\": \"form\". Valid: it takes an object of one titled property.",
            WithReferral(reasons, "\"form\""),
            WithReferral(reasons, "\"form\"").Edit("manifest.json", reasons, "\"markdown_block\""));

        yield return new RefusalCase("S02-045",
            "A property of an object input with no title (chapter 2 § 2.8.1): the Create page labels each field by it. "
            + "Invalid: the input referral_letter's property reason has none. Valid: it is titled 'Reason for referral'.",
            WithReferral(reasons),
            WithReferral(reasons).Edit("manifest.json", ", \"title\": \"Reason for referral\"", ""));

        yield return new RefusalCase("S02-046",
            "An input that declares default (chapter 2 § 2.8): inputs have none at 31. Invalid: the input referral_letter "
            + "declares \"default\": \"Routine referral.\". Valid: without it.",
            WithReferral("\"markdown_block\""),
            WithReferral("\"markdown_block\"").Edit("manifest.json", block, block + ", \"default\": \"Routine referral.\""));
    }

    // ---- Chapter 2 § 2.3.1: what a format may grow to ---------------------------------------------------

    private static IEnumerable<RefusalCase> Limits()
    {
        yield return new RefusalCase("S02-055",
            "A format sent to a model with more than 100 properties (chapter 2 § 2.3.1), each key of each properties object "
            + "counted, a definition at each place it is referred to. The agent node plan returns the package format "
            + "findings: 99 properties, and $defs/finding of one more. Invalid: second refers to #/$defs/finding as first "
            + "does, 101. Valid: second is plaintext, 100, the limit.",
            WithFindings(),
            WithFindings().Edit("formats/findings.json", "\"second\": { \"$ref\": \"plaintext\" }", "\"second\": { \"$ref\": \"#/$defs/finding\" }"));

        yield return new RefusalCase("S02-056",
            "A format sent to a model nested more than 5 levels deep (chapter 2 § 2.3.1), the root object being level 1. The "
            + "agent node plan returns the package format nested_sections. Invalid: its deepest object is at level 6. "
            + "Valid: at level 5, the limit.",
            WithNestedSections(),
            WithNestedSections().Edit("formats/nested_sections.json", "\"a\": { \"$ref\": \"plaintext\" }",
                $"\"a\": {{ {ObjectOf(("a", "{ \"$ref\": \"plaintext\" }"))} }}"));

        yield return new RefusalCase("A03-011",
            "A format that expands to more than 10,000 schemas through the formats it refers to (chapter 2 § 2.3.1; this "
            + "implementation's sentence): the comparisons and hashes are defined on a format's full form. The package "
            + "format row has 100 plaintext properties; the package format table refers to row 49 times and has one more "
            + "property. Invalid: that property refers to row too. Valid: it is plaintext, under the limit. The count of "
            + "schemas is this implementation's: each schema object visited while the references are inlined, a referenced "
            + "format counted at each place it is referenced; so only the invalid side is meant, and the valid side is "
            + "under the limit, not on it.",
            WithTable(),
            WithTable().Edit("formats/table.json", "\"last\": { \"$ref\": \"plaintext\" }", "\"last\": { \"$ref\": \"row\" }"));
    }

    /// <summary>
    /// <see cref="SourceFixture.Minimal"/> whose agent node plan returns <c>findings</c>: 97 plaintext
    /// properties, <c>first</c> (a <c>#/$defs/finding</c> of one property) and <c>second</c> (plaintext).
    /// </summary>
    private static Dictionary<string, byte[]> WithFindings()
    {
        var properties = Enumerable.Range(1, 97).Select(i => ($"p{i}", "{ \"$ref\": \"plaintext\" }"))
            .Append(("first", "{ \"$ref\": \"#/$defs/finding\" }"))
            .Append(("second", "{ \"$ref\": \"plaintext\" }"))
            .ToArray();
        return WithFormatFile("findings",
                $"\"$defs\": {{ \"finding\": {{ {ObjectOf(("text", "{ \"$ref\": \"plaintext\" }"))} }} }}, {ObjectOf(properties)}")
            .Edit("nodes/plan.md", "\"format\": \"plaintext\"", "\"format\": \"findings\"");
    }

    /// <summary><see cref="SourceFixture.Minimal"/> whose agent node plan returns <c>nested_sections</c>: objects five levels deep.</summary>
    private static Dictionary<string, byte[]> WithNestedSections()
    {
        var schema = "{ \"$ref\": \"plaintext\" }";
        for (var level = 5; level > 1; level--)
            schema = $"{{ {ObjectOf(("a", schema))} }}";
        return WithFormatFile("nested_sections", ObjectOf(("a", schema)))
            .Edit("nodes/plan.md", "\"format\": \"plaintext\"", "\"format\": \"nested_sections\"");
    }

    /// <summary>
    /// <see cref="SourceFixture.Minimal"/> with two package formats: <c>row</c>, of 100 plaintext
    /// properties, and <c>table</c>, of 49 properties that refer to <c>row</c> and <c>last</c>, plaintext.
    /// </summary>
    private static Dictionary<string, byte[]> WithTable()
    {
        var row = Enumerable.Range(1, 100).Select(i => ($"cell{i}", "{ \"$ref\": \"plaintext\" }")).ToArray();
        var table = Enumerable.Range(1, 49).Select(i => ($"row{i}", "{ \"$ref\": \"row\" }"))
            .Append(("last", "{ \"$ref\": \"plaintext\" }"))
            .ToArray();
        return Minimal()
            .Edit("formats/index.json", "\"items\": []", "\"items\": [\"row\", \"table\"]")
            .With("formats/row.json", Json($"{{ {SchemaMember}, {ObjectOf(row)} }}"))
            .With("formats/table.json", Json($"{{ {SchemaMember}, {ObjectOf(table)} }}"));
    }

    // ---- The constructs these cases add to Minimal() ----------------------------------------------------

    private const string SchemaMember = "\"$schema\": \"urn:consultologist:spec:31:dialect\"";

    /// <summary>An object schema's members (no braces): <c>type</c>, <c>required</c> naming every property, <c>additionalProperties: false</c>, <c>properties</c>.</summary>
    private static string ObjectOf(params (string Name, string Schema)[] properties) =>
        "\"type\": \"object\", \"required\": [" + string.Join(", ", properties.Select(p => $"\"{p.Name}\""))
        + "], \"additionalProperties\": false, \"properties\": { "
        + string.Join(", ", properties.Select(p => $"\"{p.Name}\": {p.Schema}")) + " }";

    /// <summary><see cref="SourceFixture.Minimal"/> with one package format, <paramref name="id"/>: the dialect's <c>$schema</c>, then <paramref name="members"/>.</summary>
    private static Dictionary<string, byte[]> WithFormatFile(string id, string members) =>
        Minimal()
            .Edit("formats/index.json", "\"items\": []", $"\"items\": [\"{id}\"]")
            .With($"formats/{id}.json", Json($"{{ {SchemaMember}, {members} }}"));

    /// <summary>
    /// The package format <c>review</c>: a <c>verdict</c> (a plain enum of good and revise, unless
    /// <paramref name="verdict"/> is false) and one more property, <paramref name="name"/>, after
    /// <paramref name="before"/> (members written ahead of the object's, such as <c>$defs</c>).
    /// </summary>
    private static Dictionary<string, byte[]> WithReview(string name, string schema, string before = "", bool verdict = true) =>
        WithFormatFile("review", before + (verdict ? ObjectOf(("verdict", Verdict), (name, schema)) : ObjectOf((name, schema))));

    /// <summary><see cref="SourceFixture.Minimal"/> with a second, optional input, <c>referral_letter</c>, whose format and channel are JSON text.</summary>
    private static Dictionary<string, byte[]> WithReferral(string format, string channel = "null") =>
        Minimal().Edit("manifest.json", "\"channel\": null }\n  }",
            "\"channel\": null },\n"
            + $"    \"referral_letter\": {{ \"label\": \"Referral letter\", \"description\": null, \"format\": {format}, "
            + $"\"required\": false, \"channel\": {channel} }}\n  }}");
}
