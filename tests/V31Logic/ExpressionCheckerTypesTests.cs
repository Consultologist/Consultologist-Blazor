using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Logic;

/// <summary>
/// #1501: the expression checker's third and fourth steps (part 03 §§ 5, 7, 11.1; D14, D24 to D27): every
/// operation's operands and result typed against the formats, an <c>object</c> against the format it builds, and a
/// computed node's result held to its declared format.
/// </summary>
public class ExpressionCheckerTypesTests
{
    private const string NoteType = """{"type":"string","enum":["consult","follow_up"]}""";

    private const string SummaryRow = """
        {"type":"object","properties":{"title":{"type":"string"},"count":{"type":"number"},
         "note":{"anyOf":[{"$ref":"plaintext"},{"type":"null"}]}},
         "required":["title","count","note"],"additionalProperties":false}
        """;

    private const string PlainPair = """
        {"type":"object","properties":{"a":{"$ref":"plaintext"},"b":{"$ref":"plaintext"}},
         "required":["a","b"],"additionalProperties":false}
        """;

    /// <summary>A format whose property reads a <c>#/$defs</c> entry, which must travel with the property.</summary>
    private const string CodedRow = """
        {"$defs":{"code":{"type":"string","enum":["a","b"]}},
         "type":"object","properties":{"code":{"$ref":"#/$defs/code"},"n":{"type":"number"}},
         "required":["code","n"],"additionalProperties":false}
        """;

    /// <summary>A check loop's <c>.items</c> element (part 04 § 2.6), as much of it as <c>all_pass</c> reads.</summary>
    private const string CoverageItem = """
        {"type":"object","properties":{"label":{"type":"string"},
         "state":{"type":"string","enum":["completed","skipped","not_run"]},
         "value":{"anyOf":[{"$ref":"check_result"},{"type":"null"}]}},
         "required":["label","state","value"],"additionalProperties":false}
        """;

    private static readonly FormatSet Formats = LogicFixture.Formats(("summary_row", SummaryRow), ("plain_pair", PlainPair), ("coded_row", CodedRow));

    private static readonly LogicFixture.MapScope Scope = new(new Dictionary<string, ScopeHead>
    {
        ["input.kind"] = LogicFixture.FormatHead("""{"type":"string","enum":["narrative","table"]}"""),
        ["input.specialty"] = LogicFixture.FormatHead(
            """{"type":"string","oneOf":[{"const":"med_onc","title":"Medical oncology"},{"const":"rad_onc","title":"Radiation oncology"}]}"""),
        ["input.consult_draft"] = LogicFixture.FormatHead("plaintext"),
        ["input.optional_note"] = LogicFixture.FormatHead("plaintext", nullable: true),
        ["input.age"] = LogicFixture.FormatHead("number"),
        ["input.include_billing"] = LogicFixture.FormatHead("boolean"),
        ["input.note_type"] = LogicFixture.FormatHead(NoteType),
        ["input.prior_notes"] = LogicFixture.FormatHead("""{"type":"array","items":{"$ref":"plaintext"}}"""),
        ["input.date_of_birth"] = LogicFixture.FormatHead("date"),
        ["input.summary"] = LogicFixture.FormatHead("markdown_block"),
        ["input.flags"] = LogicFixture.FormatHead("""{"type":"array","items":{"type":"string"}}"""),
        ["input.codes"] = LogicFixture.FormatHead("""{"$defs":{"code":{"type":"string","enum":["a","b"]}},"type":"array","items":{"$ref":"#/$defs/code"}}"""),
        ["node.draft"] = LogicFixture.Deferred(),
        ["node.count"] = LogicFixture.Deferred(),
        ["node.extract_input_terms"] = LogicFixture.FormatHead("concept_list"),
        ["node.extract_note_terms"] = LogicFixture.FormatHead("concept_list"),
        ["check.coverage"] = LogicFixture.ValueHead(
            """{"type":"object","properties":{"items":{"type":"array","items":""" + CoverageItem
            + """},"value":{"type":"array","items":{"$ref":"check_result"}}}}"""),
        ["item"] = LogicFixture.Deferred(),
        ["run.date"] = LogicFixture.ValueHead("date"),
    });

    /// <summary>Every format an <c>Expression</c> of these tests declares (as JSON), for the mutants.</summary>
    private static readonly string[] Declared =
    [
        "\"check_result\"", "\"number\"", "\"markdown_block\"", "\"string\"", "\"plaintext\"", NoteType, "\"date\"", "\"boolean\"",
        """{"type":"integer"}""", """{"type":["number","null"]}""", "\"summary_row\"", """{"type":"array","items":{"$ref":"summary_row"}}""",
        "\"coded_row\"",
    ];

    private static ExpressionCheck Check(string rule, ExpressionSiteKind kind = ExpressionSiteKind.NodeIf, string? declared = null) =>
        ExpressionChecker.Check(LogicFixture.Rule(rule), Site(kind, declared), Scope, Formats);

    private static ExpressionSite Site(ExpressionSiteKind kind, string? declared) =>
        LogicFixture.Site(kind, declared is null ? null : JsonNode.Parse(declared));

    private static string TypeOf(ExpressionCheck check)
    {
        Assert.Empty(check.Refusals);
        return TypeStrings.Print(check.Type!.Format, Formats);
    }

    private static SourceRefusal OneRefusal(ExpressionCheck check, string id, string? sentence = null)
    {
        var refusal = Assert.Single(check.Refusals);
        Assert.Equal(id, refusal.Id);
        if (sentence is not null)
            Assert.Equal(sentence, refusal.Sentence);
        V31Contract.AssertSentence(refusal);
        Assert.Null(check.Type);
        return refusal;
    }

    [Fact]
    public void TheSpecsExamples_TypeCheck()
    {
        // § 2.3 (item deferred), § 4.5 (node.draft deferred), § 5.4, § 5.5, § 6.2's two.
        Assert.Equal("boolean", TypeOf(Check(
            """{ "or": [ { "!==": [ { "var": "item.id" }, "billing" ] }, { "===": [ { "var": "input.include_billing.value" }, true ] } ] }""")));
        Assert.Equal("boolean", TypeOf(Check(
            """{ "in": [ "letrozole", { "lower": [ { "var": "node.draft.content" } ] } ] }""", ExpressionSiteKind.Expression, "\"boolean\"")));
        Assert.Equal("check_result", TypeOf(Check(
            """{ "terms_subset": [ { "var": "node.extract_input_terms.value" }, { "var": "node.extract_note_terms.value" }, "The note does not cover every clinical term found in the referral." ] }""",
            ExpressionSiteKind.Expression, "\"check_result\"")));
        Assert.Equal("boolean", TypeOf(Check(
            """{ ">=": [ { "years_between": [ { "var": "input.date_of_birth.value" }, { "var": "run.date" } ] }, 65 ] }""")));
        Assert.Equal("boolean", TypeOf(Check("""{ "!": [ { ">": [ { "var": "input.age.value" }, 65 ] } ] }""")));
        Assert.Equal("boolean", TypeOf(Check(
            """{ "and": [ { "!==": [ { "var": "input.age.value" }, null ] }, { "<=": [ { "var": "input.age.value" }, 65 ] } ] }""")));
    }

    public static TheoryData<ExpressionSiteKind, string?, string, string, string?> RefusalRows => new()
    {
        { ExpressionSiteKind.NodeIf, null, """{"===":[{"var":"input.consult_draft.content"},"x"]}""", "S03-002",
            "Node 'gate' compares input.consult_draft.content (plaintext) with a literal; text is tested by whether it is blank, its size, or in." },
        { ExpressionSiteKind.NodeIf, null, """{"===":[{"var":"input.consult_draft.content"},{"var":"input.summary.content"}]}""", "S03-003",
            "Node 'gate' compares input.consult_draft.content (plaintext) with input.summary.content (markdown_block); text is tested by whether it is blank, its size, or in." },
        { ExpressionSiteKind.NodeIf, null, """{"<":[{"var":"input.note_type.value"},3]}""", "S03-008",
            "Node 'gate' orders input.note_type.value (string {consult, follow_up}); <, >, ≤ and ≥ compare numbers or dates." },
        { ExpressionSiteKind.NodeIf, null, """{"!":[{"var":"input.consult_draft.content"}]}""", "S03-009",
            "Node 'gate' tests input.consult_draft.content (plaintext) with not; not takes a boolean — test text with blank." },
        { ExpressionSiteKind.NodeIf, null, """{"===":[{"var":"input.note_type.value"},"consultation"]}""", "S03-023",
            "Node 'gate' compares input.note_type.value with \"consultation\", which is not one of its values (consult, follow_up)." },
        { ExpressionSiteKind.NodeIf, null, """{"in":[{"var":"input.note_type.value"},["consult","followup"]]}""", "S03-023",
            "Node 'gate' compares input.note_type.value with \"followup\", which is not one of its values (consult, follow_up)." },
        { ExpressionSiteKind.NodeIf, null, """{"===":[{"var":"input.specialty.title"},"Oncology"]}""", "S03-017",
            "Node 'gate' compares input.specialty.title with \"Oncology\", which is not one of its titles (Medical oncology, Radiation oncology)." },
        { ExpressionSiteKind.Expression, "\"plaintext\"", """{"cat":["Summary: ",{"var":"input.summary.content"}]}""", "S03-035",
            "Computed node 'total' cats input.summary.content (markdown_block); convert Markdown with plain first." },
        { ExpressionSiteKind.NodeIf, null, """{"+":[{"var":"input.note_type.value"},1]}""", "A04-001",
            "Node 'gate' gives + input.note_type.value (string {consult, follow_up}); + takes numbers." },
        { ExpressionSiteKind.NodeIf, null, """{"===":[{"var":"input.age.value"},"65"]}""", "A04-002",
            "Node 'gate' compares input.age.value (number) with \"65\" (string); = and ≠ compare two values of one type." },
        { ExpressionSiteKind.NodeIf, null, """{"===":[{"var":"input.prior_notes.value"},{"var":"input.flags.value"}]}""", "A04-002",
            "Node 'gate' compares input.prior_notes.value (list<plaintext>) with input.flags.value (list<string>); = and ≠ compare two values of one type." },
        { ExpressionSiteKind.Expression, null, """{"if":[{"var":"input.include_billing.value"},1,"one"]}""", "A04-003",
            "Computed node 'total' mixes integer and string in the branches of an if; they have one type." },
        { ExpressionSiteKind.Expression, null, """[1,"a"]""", "A04-003",
            "Computed node 'total' mixes integer and string in a list; they have one type." },
        { ExpressionSiteKind.NodeIf, null, """{"===":[{"object":{"a":1}},null]}""", "A04-008",
            "Node 'gate' builds an object where no format gives its fields; object builds the value of a computed node." },
        { ExpressionSiteKind.NodeIf, null, """{"and":[{"var":"input.age.value"},true]}""", "A04-001",
            "Node 'gate' gives and input.age.value (number); and takes booleans." },
        { ExpressionSiteKind.NodeIf, null, """{"size":[{"var":"input.age.value"}]}""", "A04-001",
            "Node 'gate' gives size input.age.value (number); size takes text or a list." },
        { ExpressionSiteKind.NodeIf, null, """{"days_between":[{"var":"input.age.value"},{"var":"run.date"}]}""", "A04-001",
            "Node 'gate' gives days_between input.age.value (number); days_between takes dates." },
        { ExpressionSiteKind.NodeIf, null, """{"all_pass":[{"var":"input.flags.value"},"Not all passed."]}""", "A04-001",
            "Node 'gate' gives all_pass input.flags.value (list<string>); all_pass takes a list of check results." },
        { ExpressionSiteKind.NodeIf, null, """{"terms_subset":[{"var":"input.flags.value"},{"var":"node.extract_note_terms.value"},"x"]}""", "A04-001",
            "Node 'gate' gives terms_subset input.flags.value (list<string>); terms_subset takes two concept_list values." },
        { ExpressionSiteKind.NodeIf, null, """{"in":[{"var":"input.age.value"},{"var":"input.consult_draft.content"}]}""", "A04-001",
            "Node 'gate' gives in input.age.value (number); in takes a string to find." },
        { ExpressionSiteKind.NodeIf, null, """{"in":["a",{"var":"input.age.value"}]}""", "A04-001",
            "Node 'gate' gives in input.age.value (number); in takes a list of its type, or text." },
        { ExpressionSiteKind.NodeIf, null, """{"in":[{"var":"input.date_of_birth.value"},["1960-06-15","1970-13-01"]]}""", "A04-001",
            "Node 'gate' gives in input.date_of_birth.value (date); in takes a list of its type, or text." },
        { ExpressionSiteKind.NodeIf, null, """{"in":["c",{"var":"input.codes.value"}]}""", "S03-023",
            "Node 'gate' compares input.codes.value with \"c\", which is not one of its values (a, b)." },
    };

    [Theory]
    [MemberData(nameof(RefusalRows))]
    public void Refusals(ExpressionSiteKind kind, string? declared, string rule, string id, string? sentence)
    {
        var refusal = OneRefusal(Check(rule, kind, declared), id, sentence);

        Assert.Equal(kind == ExpressionSiteKind.Expression ? "nodes/total.md" : "nodes/gate.md", refusal.Path);
    }

    public static TheoryData<ExpressionSiteKind, string?, string, string> AcceptedRows => new()
    {
        { ExpressionSiteKind.NodeIf, null, """{"===":[{"var":"input.note_type.value"},"consult"]}""", "boolean" },
        { ExpressionSiteKind.NodeIf, null, """{"in":[{"var":"input.note_type.value"},["consult","follow_up"]]}""", "boolean" },
        { ExpressionSiteKind.NodeIf, null, """{"===":[{"var":"input.date_of_birth.value"},"1960-06-15"]}""", "boolean" },
        { ExpressionSiteKind.NodeIf, null, """{"<":[{"var":"input.date_of_birth.value"},{"var":"run.date"}]}""", "boolean" },
        { ExpressionSiteKind.NodeIf, null, """{"<":[{"var":"input.age.value"},null]}""", "boolean" },
        { ExpressionSiteKind.NodeIf, null, """{"===":[{"var":"input.prior_notes.value"},null]}""", "boolean" },
        { ExpressionSiteKind.NodeIf, null, """{"blank":[{"var":"input.consult_draft.content"}]}""", "boolean" },
        { ExpressionSiteKind.NodeIf, null, """{"in":["letrozole",{"lower":[{"var":"input.consult_draft.content"}]}]}""", "boolean" },
        { ExpressionSiteKind.NodeIf, null, """{"in":["letrozole",{"plain":[{"var":"input.summary.content"}]}]}""", "boolean" },
        { ExpressionSiteKind.Expression, "\"check_result\"", """{"all_pass":[{"var":"check.coverage.items"},"x"]}""", "check_result" },
        { ExpressionSiteKind.Expression, "\"check_result\"", """{"all_pass":[{"var":"check.coverage.value"},"x"]}""", "check_result" },
        { ExpressionSiteKind.Expression, "\"number\"", """{"size":[{"var":"input.prior_notes.value"}]}""", "integer" },
        { ExpressionSiteKind.Expression, "\"markdown_block\"", """{"trim":[{"var":"input.summary.content"}]}""", "markdown_block" },
        { ExpressionSiteKind.Expression, "\"string\"", """{"cat":["a","b"]}""", "string" },
        { ExpressionSiteKind.Expression, "\"plaintext\"", """{"cat":["a",1]}""", "plaintext" },
        { ExpressionSiteKind.Expression, "\"plaintext\"", "\"Yes\"", "plaintext" },
        { ExpressionSiteKind.Expression, NoteType, """{"if":[{"var":"input.include_billing.value"},"consult","follow_up"]}""", "string {consult, follow_up}" },
        { ExpressionSiteKind.Expression, "\"date\"", """{"add_days":[{"var":"run.date"},7]}""", "date" },
        { ExpressionSiteKind.Expression, "\"number\"", """{"+":[{"var":"node.count.value"},1]}""", "number" },
        { ExpressionSiteKind.NodeIf, null, """{"in":[{"var":"input.date_of_birth.value"},["1960-06-15","1970-01-01"]]}""", "boolean" },
        { ExpressionSiteKind.NodeIf, null, """{"in":["a",{"var":"input.codes.value"}]}""", "boolean" },
        { ExpressionSiteKind.Expression, "\"string\"", """{"cat":["Seen ",{"var":"run.date"}]}""", "string" },
    };

    [Theory]
    [MemberData(nameof(AcceptedRows))]
    public void Accepted(ExpressionSiteKind kind, string? declared, string rule, string type) =>
        Assert.Equal(type, TypeOf(Check(rule, kind, declared)));

    [Fact]
    public void AComputedNodeYieldsWhatItDeclares()
    {
        OneRefusal(Check("""{"size":[{"var":"input.prior_notes.value"}]}""", ExpressionSiteKind.Expression, "\"boolean\""),
            "S03-026", "Computed node 'total' yields integer; it declares boolean.");
        OneRefusal(Check("""{"/":[1,2]}""", ExpressionSiteKind.Expression, """{"type":"integer"}"""),
            "S03-026", "Computed node 'total' yields number; it declares integer.");

        const string OneOrNull = """{"if":[{"var":"input.include_billing.value"},1,null]}""";
        OneRefusal(Check(OneOrNull, ExpressionSiteKind.Expression, "\"number\""),
            "S03-026", "Computed node 'total' yields integer | null; it declares number.");
        Assert.Equal("integer", TypeOf(Check(OneOrNull, ExpressionSiteKind.Expression, """{"type":["number","null"]}""")));

        // D25: a result that is only nullable is held as null; a deferred read (D2) is not held.
        OneRefusal(Check("""{"if":[{"var":"input.include_billing.value"},null,null]}""", ExpressionSiteKind.Expression, "\"string\""),
            "S03-026", "Computed node 'total' yields null; it declares string.");
        OneRefusal(Check("null", ExpressionSiteKind.Expression, "\"string\""),
            "S03-026", "Computed node 'total' yields null; it declares string.");
        Assert.Empty(Check("null", ExpressionSiteKind.Expression, """{"type":["string","null"]}""").Refusals);
        Assert.Empty(Check("""{"var":"node.count.value"}""", ExpressionSiteKind.Expression, "\"string\"").Refusals);

        // A literal that is no value of the declared enum leaves the branches strings: the result is held to the enum.
        OneRefusal(Check("""{"if":[{"var":"input.include_billing.value"},"consult","consultation"]}""", ExpressionSiteKind.Expression, NoteType),
            "S03-026", "Computed node 'total' yields string; it declares string {consult, follow_up}.");

        // The expectation is computed from FormatTypes.Fits: the checker holds an enum to string as area 03 does.
        var fits = FormatTypes.Fits(JsonNode.Parse(NoteType), JsonValue.Create("string"), Formats);
        var check = Check("""{"var":"input.note_type.value"}""", ExpressionSiteKind.Expression, "\"string\"");
        if (fits)
            Assert.Empty(check.Refusals);
        else
            OneRefusal(check, "S03-026", "Computed node 'total' yields string {consult, follow_up}; it declares string.");
    }

    /// <summary>D25: an operation's result is never nullable for typing, <c>lower</c> and <c>trim</c> as much as <c>plain</c>.</summary>
    [Theory]
    [InlineData("""{"trim":[{"var":"input.optional_note.content"}]}""")]
    [InlineData("""{"lower":[{"var":"input.optional_note.content"}]}""")]
    [InlineData("""{"plain":[{"var":"input.optional_note.content"}]}""")]
    public void AnOperationOnANullableRead_IsNotNullable(string rule)
    {
        var check = Check(rule, ExpressionSiteKind.Expression, "\"plaintext\"");
        Assert.Equal("plaintext", TypeOf(check));
        Assert.False(check.Type!.Nullable);

        // the read itself is nullable, and held to its declaration
        OneRefusal(Check("""{"var":"input.optional_note.content"}""", ExpressionSiteKind.Expression, "\"plaintext\""), "S03-026");
    }

    public static TheoryData<string, string, string?, string?> ObjectRows => new()
    {
        { "\"summary_row\"", """{"object":{"title":"Okafor","count":3,"note":{"var":"input.consult_draft.content"}}}""", null, null },
        { "\"summary_row\"", """{"object":{"title":"Okafor","note":null}}""",
            "S03-027", "Computed node 'total' builds summary_row without count; object must give every property of the format." },
        { "\"summary_row\"", """{"object":{"title":"Okafor","count":3,"note":null}}""", null, null },
        { "\"coded_row\"", """{"object":{"code":"a","n":1}}""", null, null },
        { "\"coded_row\"", """{"object":{"code":"c","n":1}}""",
            "A04-009", "Computed node 'total' builds coded_row with code as string; the format declares it string {a, b}." },
        { "\"summary_row\"", """{"object":{"title":"Okafor","count":3,"note":null,"extra":1}}""",
            "S03-028", "Computed node 'total' builds summary_row with extra, which the format doesn't declare." },
        { "\"summary_row\"", """{"object":{"title":"Okafor","count":"three","note":null}}""",
            "A04-009", "Computed node 'total' builds summary_row with count as string; the format declares it number." },
        { "\"summary_row\"", """{"object":{"title":null,"count":3,"note":null}}""",
            "A04-009", "Computed node 'total' builds summary_row with title as null; the format declares it string." },
        { "\"number\"", """{"object":{"title":"Okafor","count":3,"note":null}}""",
            "S03-026", "Computed node 'total' yields object; it declares number." },
        { """{"type":"array","items":{"$ref":"summary_row"}}""",
            """[{"object":{"title":"Okafor","count":3,"note":null}},{"object":{"title":"Sample","count":{"size":[{"var":"input.prior_notes.value"}]},"note":{"trim":[{"var":"input.consult_draft.content"}]}}}]""",
            null, null },
        { """{"type":"array","items":{"$ref":"summary_row"}}""",
            """[{"object":{"title":"Okafor","count":3,"note":null}},{"object":{"title":"Sample","count":"three","note":null}}]""",
            "A04-009", "Computed node 'total' builds summary_row with count as string; the format declares it number." },
    };

    [Theory]
    [MemberData(nameof(ObjectRows))]
    public void Object(string declared, string rule, string? id, string? sentence)
    {
        var check = Check(rule, ExpressionSiteKind.Expression, declared);

        if (id is null)
        {
            Assert.Empty(check.Refusals);
            Assert.True(FormatTypes.Fits(check.Type!.Format, JsonNode.Parse(declared), Formats));
        }
        else
            OneRefusal(check, id, sentence);
    }

    [Fact]
    public void PlainConverts_FollowsTheOperands()
    {
        var check = Check(
            """{"cat":[{"plain":[{"var":"input.summary.content"}]},{"plain":[{"var":"input.consult_draft.content"}]},{"plain":[{"var":"node.draft.content"}]}]}""",
            ExpressionSiteKind.Expression, "\"plaintext\"");

        Assert.Empty(check.Refusals);
        Assert.Equal([true, false, true], check.PlainConverts);
    }

    [Fact]
    public void PlainConverts_DrivesTheEvaluator_InItsOrder()
    {
        // Pre-order: a's plain; b's outer plain (its operand is a cat, plain text); the plain inside it (Markdown).
        var rule = LogicFixture.Rule("""
            {"object":{
              "a":{"plain":[{"var":"input.consult_draft.content"}]},
              "b":{"plain":[{"cat":[{"plain":[{"var":"input.summary.content"}]}," ",{"var":"input.consult_draft.content"}]}]}}}
            """);
        var check = ExpressionChecker.Check(rule, Site(ExpressionSiteKind.Expression, "\"plain_pair\""), Scope, Formats);
        Assert.Empty(check.Refusals);
        Assert.Equal([false, false, true], check.PlainConverts);

        var data = JsonNode.Parse("""{"input":{"consult_draft":{"content":"*kept*"},"summary":{"content":"**Bold**"}}}""");
        var value = new CorvusJsonLogicEvaluator().Compile(rule, check.PlainConverts).Evaluate(data);

        Assert.Null(value.Failure);
        Assert.Equal("""{"a":"*kept*","b":"Bold *kept*"}""", value.Value!.ToJsonString());
    }

    [Fact]
    public void OneMistake_OneSentence()
    {
        OneRefusal(Check("""{"and":[{"+":[{"var":"input.nope.value"},1]},true]}"""), "S03-014");
        OneRefusal(Check("""{"!":[{"+":[{"var":"input.note_type.value"},1]}]}"""), "A04-001");
    }

    [Fact]
    public void AnyMutation_IsRefusedOrTyped_NeverThrows()
    {
        var sites = Declared.Select(d => Site(ExpressionSiteKind.Expression, d)).Prepend(Site(ExpressionSiteKind.NodeIf, null)).ToList();
        var rules = AcceptedRows.Select(row => (string)row[2])
            .Concat(ObjectRows.Select(row => (string)row[1]))
            .Distinct(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            foreach (var mutant in LogicFixture.Mutants(LogicFixture.Rule(rule)))
            {
                foreach (var site in sites)
                {
                    var check = ExpressionChecker.Check(mutant, site, Scope, Formats);

                    Assert.NotNull(check.Printed);
                    Assert.Equal(check.Refusals.Count == 0, check.Type != null);
                }
            }
        }
    }
}
