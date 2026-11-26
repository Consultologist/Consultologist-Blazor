using System.Text.Json.Nodes;
using Consultologist.Api.Tests.ByteEquivalence;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Migration;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 §§ 2.1 to 2.3): the converter. Every row of § 2.2's table, as
/// <c>conditions/expressions.json</c> writes one or more expressions for it, is written as its JSON Logic over the scope
/// its <c>reads</c> describe (<see cref="MigrationFixture.ScopeOf"/>); the written logic is then typed as the 31
/// checker types it (Task 3): <c>!x</c> on text becomes <c>blank</c>, and what the checker refuses (text compared with a
/// literal, for one) is refused with its sentence; the output is canonical and what the printer reads back; and
/// <c>conditions/converted.json</c> holds what the converter gives for every expression, in order (with
/// <c>GOLDEN_UPDATE_V31=1</c> the test rewrites it instead; a rewritten golden is read before it is committed).
/// </summary>
public class ConditionConverterTests
{
    private const string GoldenPath = "conditions/converted.json";

    public static TheoryData<string> RowIds() =>
        new(MigrationFixture.Expressions().Select(e => (string)e["id"]!).Where(id => id.StartsWith("row.", StringComparison.Ordinal)));

    public static TheoryData<string> AllIds() =>
        new(MigrationFixture.Expressions().Select(e => (string)e["id"]!));

    /// <summary>The entry <paramref name="id"/> converted as its kind says, over the scope its reads describe.</summary>
    private static ConversionResult Convert(string id)
    {
        var entry = MigrationFixture.Expression(id);
        return WorkflowConditionConverter.FromScriban(
            (string)entry["expression"]!, MigrationFixture.ScopeOf(entry["reads"]!.AsObject()), MigrationFixture.KindOf(entry), MigrationFixture.DeclaredOf(entry));
    }

    /// <summary>
    /// The entry <paramref name="id"/> as § 2.2's table writes it, before it is typed (<c>ConditionTree</c>): a row's
    /// mapping, which the typing of § 2.3 may still refuse (text compared with a literal, S03-002).
    /// </summary>
    private static JsonNode? Written(string id)
    {
        var entry = MigrationFixture.Expression(id);
        var text = (string)entry["expression"]!;
        Assert.True(WorkflowConditionAt25.TryParse(text.Trim(), out var parsed, out var error), $"{id}: {error}");
        var notices = new List<string>();
        var logic = ConditionTree.Write(parsed!, text, MigrationFixture.ScopeOf(entry["reads"]!.AsObject()), notices);
        Assert.True(logic != null, $"{id}: {string.Join(" | ", notices)}");
        return logic;
    }

    [Fact]
    public void TheRowsOf2_2_AreFortyTwo() => Assert.Equal(42, RowIds().Count);

    [Theory]
    [MemberData(nameof(RowIds))]
    public void EveryRowOf2_2_ConvertsToItsLogic(string id) => Assert.NotNull(Written(id));

    [Theory]
    [InlineData("row.eq", """{"===":[{"var":"input.kind.value"},"new"]}""")]
    [InlineData("row.ne", """{"!==":[{"var":"input.kind.value"},"new"]}""")]
    [InlineData("row.and3", """{"and":[{"var":"input.urgent.value"},{"===":[{"var":"input.kind.value"},"new"]},{">":[{"var":"input.age.value"},65]}]}""")]
    [InlineData("row.or3", """{"or":[{"var":"input.urgent.value"},{"===":[{"var":"input.kind.value"},"review"]},{"<":[{"var":"input.age.value"},18]}]}""")]
    [InlineData("row.not-boolean", """{"!":[{"var":"input.urgent.value"}]}""")]
    [InlineData("row.size.pipe", """{">":[{"size":[{"var":"input.problems.value"}]},1]}""")]
    [InlineData("row.size.member", """{"===":[{"size":[{"var":"input.problems.value"}]},0]}""")]
    [InlineData("row.downcase", """{"===":[{"lower":[{"var":"input.reason.content"}]},"breathlessness"]}""")]
    [InlineData("row.strip.input", """{"===":[{"trim":[{"var":"input.reason.content"}]},""]}""")]
    [InlineData("row.strip.node", """{"===":[{"trim":[{"var":"node.summary.content"}]},""]}""")]
    [InlineData("row.strip.input-field", """{"===":[{"trim":[{"var":"input.patient.value.name"}]},""]}""")]
    [InlineData("row.starts-with", """{"starts_with":[{"var":"input.reason.content"},"Breath"]}""")]
    [InlineData("row.ends-with", """{"ends_with":[{"var":"input.reason.content"},"ness"]}""")]
    [InlineData("row.contains", """{"in":["less",{"var":"input.reason.content"}]}""")]
    [InlineData("row.date-parse.lt", """{"<":[{"var":"input.seen_on.value"},"2026-11-10"]}""")]
    [InlineData("row.date-parse.eq", """{"===":[{"var":"input.seen_on.value"},"2026-11-10"]}""")]
    [InlineData("row.divide.half", """{"===":[{"/":[{"var":"input.age.value"},2]},1.5]}""")]
    [InlineData("row.modulo", """{"===":[{"%":[{"var":"input.age.value"},2]},0]}""")]
    [InlineData("row.subtract", """{"<":[{"-":[{"var":"input.age.value"},1]},18]}""")]
    [InlineData("row.let-object", """{"object":{"age":{"var":"input.age.value"},"priority":{"var":"node.triage.value.priority"}}}""")]
    [InlineData("row.let-array", """{"object":{"reasons":[{"var":"input.reason.content"},{"var":"node.summary.content"}]}}""")]
    [InlineData("row.input.x", """{"===":[{"var":"input.reason.content"},"Breathlessness"]}""")]
    [InlineData("row.input.x-y", """{"===":[{"var":"input.patient.value.name"},"Margaret Okafor"]}""")]
    [InlineData("row.literal.number", """{"===":[{"var":"input.age.value"},65]}""")]
    [InlineData("row.literal.false", """{"===":[{"var":"input.urgent.value"},false]}""")]
    [InlineData("row.read.node-content", """{"===":[{"var":"node.summary.content"},"Reviewed in clinic."]}""")]
    [InlineData("row.read.node-value", """{"===":[{"var":"node.triage.value.priority"},"high"]}""")]
    [InlineData("row.read.item-id", """{"===":[{"var":"item.id"},"plan"]}""")]
    [InlineData("row.read.item-value", """{"===":[{"var":"item.value.kind"},"table"]}""")]
    [InlineData("row.read.pass-number", """{">=":[{"var":"pass.number"},2]}""")]
    [InlineData("row.read.run-date", """{"===":[{"var":"run.date"},"2026-11-10"]}""")]
    [InlineData("example-classifier-scope.plan.if", """{"===":[{"var":"node.scope.value.value"},"in_scope"]}""")]
    [InlineData("example-macros.brief_plan.md.if", """{"and":[{"or":[{"===":[{"var":"input.note_type.value"},"consult"]},{"===":[{"var":"input.note_type.value"},"follow_up"]}]},{"!==":[{"var":"input.include_counselling.value"},true]}]}""")]
    public void ARow_IsPinned(string id, string expected) => Assert.Equal(expected, CanonicalJson.Write(Written(id)));

    [Fact]
    public void AnInputRead_IsContentOnTextAndValueOtherwise()
    {
        // Read as a value (a case's subject): a bare read is no condition.
        Assert.Equal("""{"var":"input.reason.content"}""", Bare("input.reason", "markdown_block"));
        Assert.Equal("""{"var":"input.reason.content"}""", Bare("input.reason", "plaintext"));
        Assert.Equal("""{"var":"input.reason.content"}""", Bare("input.reason", "markdown_inline"));
        Assert.Equal("""{"var":"input.reason.value"}""", Bare("input.reason", "string"));
        Assert.Equal("""{"var":"input.reason.value"}""", Bare("input.reason", "number"));
        Assert.Equal("""{"var":"input.reason.value"}""", Bare("input.reason", "date"));
        Assert.Equal("""{"var":"input.reason.value"}""", Bare("input.reason", """{"type":"string","enum":["a","b"]}"""));

        static string Bare(string expression, string format)
        {
            var scope = Scope(inputs: new() { ["reason"] = Format(format) });
            return CanonicalJson.Write(WorkflowConditionConverter.FromScriban(expression, scope, ConversionKind.Value).Logic);
        }
    }

    [Fact]
    public void AClassifierRead_GoesThroughItsPath_AndKeepsItsFormat()
    {
        var scope = Scope(nodes: new() { ["scope"] = new ConversionRead("node.triage_flow.scope", JsonValue.Create("classification")) });

        var result = WorkflowConditionConverter.FromScriban("node.scope.value == \"yes\"", scope);

        Assert.Equal("""{"===":[{"var":"node.triage_flow.scope.value.value"},"yes"]}""", CanonicalJson.Write(result.Logic));
        Assert.Equal(["node.triage_flow.scope.value.value"], result.Reads);
    }

    [Fact]
    public void ANodeOrInputNotInScope_IsANotice()
    {
        var result = WorkflowConditionConverter.FromScriban("node.missing.value == \"yes\" || input.gone == true", Scope());

        Assert.Null(result.Logic);
        Assert.Equal(2, result.Notices.Count);
        Assert.All(result.Notices, n => Assert.StartsWith("The condition 'node.missing.value == \"yes\" || input.gone == true' uses ", n, StringComparison.Ordinal));
    }

    [Fact]
    public void AConstructOutsideTheTable_IsNullWithS10_003()
    {
        var result = Convert("refuse.builtin.upcase");

        Assert.Null(result.Logic);
        Assert.Equal(
            ["The condition '(string.upcase input.reason) == \"BREATHLESSNESS\"' uses string.upcase, which has no JSON Logic form at 31: rewrite it by hand."],
            result.Notices);
    }

    [Theory]
    [MemberData(nameof(AllIds))]
    public void TheOutput_IsCanonical(string id)
    {
        var first = Convert(id);
        var second = Convert(id);
        if (first.Logic is null)
        {
            return;
        }

        var canonical = CanonicalJson.Write(first.Logic);
        Assert.Equal(canonical, CanonicalJson.Write(JsonNode.Parse(canonical)));
        Assert.Equal(canonical, CanonicalJson.Write(second.Logic));
        AssertMembersInCanonicalOrder(first.Logic);
    }

    [Theory]
    [MemberData(nameof(AllIds))]
    public void ThePrinter_ReadsWhatTheConverterWrites(string id)
    {
        var result = Convert(id);
        if (result.Logic is null)
        {
            return;
        }

        var printed = JsonLogicPrinter.Print(result.Logic);

        Assert.True(JsonLogicPrinter.TryParse(printed, out var parsed), $"{id}: the printer does not read back '{printed}'.");
        Assert.Equal(CanonicalJson.Write(result.Logic), CanonicalJson.Write(parsed));
    }

    [Fact]
    public void TheGolden_IsWhatTheConverterGives()
    {
        var converted = new JsonArray();
        foreach (var entry in MigrationFixture.Expressions())
        {
            var id = (string)entry["id"]!;
            var result = Convert(id);
            converted.Add(new JsonObject
            {
                ["id"] = id,
                ["logic"] = result.Logic?.DeepClone(),
                ["resultFormat"] = result.ResultFormat,
                ["reads"] = new JsonArray(result.Reads.Select(r => (JsonNode?)JsonValue.Create(r)).ToArray()),
                ["blankConversions"] = new JsonArray(result.BlankConversions.Select(r => (JsonNode?)JsonValue.Create(r)).ToArray()),
                ["blanks"] = new JsonArray(result.Blanks.Select(b => (JsonNode?)new JsonObject { ["written"] = b.Written, ["printed"] = b.Printed }).ToArray()),
                ["trimBlanks"] = new JsonArray(result.TrimBlanks.Select(b => (JsonNode?)new JsonObject { ["written"] = b.Written, ["printed"] = b.Printed }).ToArray()),
                ["notices"] = new JsonArray(result.Notices.Select(n => (JsonNode?)JsonValue.Create(n)).ToArray()),
            });
        }

        var text = ByteEquivalenceFixtures.WriteJson(new JsonObject { ["converted"] = converted });
        var path = Path.Combine(ByteEquivalenceFixtures.Root, "conditions", "converted.json");
        if (Environment.GetEnvironmentVariable("GOLDEN_UPDATE_V31") == "1")
        {
            File.WriteAllText(path, text);
        }

        Assert.True(File.Exists(path), $"{GoldenPath} is missing: run with GOLDEN_UPDATE_V31=1, then read it.");
        Assert.Equal(ByteEquivalenceFixtures.ReadText(path), text);
    }

    // ---- Task 3: the types, `!x` on text, what is refused (§ 2.3) ----

    public static TheoryData<string> PackageIds() =>
        new(MigrationFixture.Expressions().Select(e => (string)e["id"]!)
            .Where(id => !id.StartsWith("row.", StringComparison.Ordinal) && !id.StartsWith("refuse.", StringComparison.Ordinal)));

    /// <summary>
    /// The package expressions 31 cannot hold, each with why (recorded in the plan's Task 3): builtins § 2.2's table has
    /// no row for. (A text field compared with a literal, <c>composer-macros</c>' <c>section_close.if</c>, converts to
    /// <c>in</c> since #1508, comment 5, item 2.)
    /// </summary>
    private static readonly Dictionary<string, string[]> PackageRefusals = new(StringComparer.Ordinal)
    {
        ["composer-sources.topic_tally.let"] =
        [
            "The condition '\"Topic section length: \" | string.append (node.topic_loop.content | string.size)' uses string.append, which has no JSON Logic form at 31: rewrite it by hand.",
            "The condition '\"Topic section length: \" | string.append (node.topic_loop.content | string.size)' uses string.size, which has no JSON Logic form at 31: rewrite it by hand.",
        ],
    };

    [Fact]
    public void ThePackageExpressions_AreFortyFour() => Assert.Equal(44, PackageIds().Count);

    [Theory]
    [MemberData(nameof(PackageIds))]
    public void EveryPackageExpression_Converts(string id)
    {
        var result = Convert(id);

        if (PackageRefusals.TryGetValue(id, out var refused))
        {
            Assert.Null(result.Logic);
            Assert.Equal(refused, result.Notices);
            return;
        }

        Assert.True(result.Logic != null, $"{id}: {string.Join(" | ", result.Notices)}");
        Assert.Empty(result.Notices);
        Assert.NotNull(result.ResultFormat);
        if (MigrationFixture.KindOf(MigrationFixture.Expression(id)) == ConversionKind.Condition)
        {
            Assert.Equal("boolean", result.ResultFormat);
        }
    }

    [Fact]
    public void NotOnText_BecomesBlank()
    {
        var node = Convert("row.not-text.node");
        var input = Convert("row.not-text.input");
        var boolean = Convert("row.not-boolean");

        Assert.Equal("""{"blank":[{"var":"node.summary.content"}]}""", CanonicalJson.Write(node.Logic));
        Assert.Equal(["node.summary.content"], node.BlankConversions);
        AssertBlank(("node.summary.content", "node.summary.content", ["node.summary.content"]), node.Blanks.Single());
        Assert.Equal("""{"blank":[{"var":"input.reason.content"}]}""", CanonicalJson.Write(input.Logic));
        Assert.Equal(["input.reason.content"], input.BlankConversions);
        AssertBlank(("input.reason", "input.reason.content", ["input.reason.content"]), input.Blanks.Single());
        Assert.Equal("boolean", input.ResultFormat);
        Assert.Equal("""{"!":[{"var":"input.urgent.value"}]}""", CanonicalJson.Write(boolean.Logic));
        Assert.Empty(boolean.BlankConversions);
    }

    [Fact]
    public void NotOnTextInsideAnOperation_BecomesBlank_AndNotOnAStringIsTheCheckersSentence()
    {
        var scope = Scope(
            inputs: new() { ["reason"] = Format("markdown_block"), ["urgent"] = Format("boolean"), ["code"] = Format("string") },
            required: ["reason", "urgent", "code"]);

        var inside = WorkflowConditionConverter.FromScriban("input.urgent && !(string.strip input.reason)", scope);
        var onString = WorkflowConditionConverter.FromScriban("!input.code", scope);

        Assert.Equal("""{"and":[{"var":"input.urgent.value"},{"blank":[{"trim":[{"var":"input.reason.content"}]}]}]}""", CanonicalJson.Write(inside.Logic));
        Assert.Equal(["input.reason.content"], inside.BlankConversions);
        AssertBlank(("(string.strip input.reason)", "trim(input.reason.content)", ["input.reason.content"]), inside.Blanks.Single());
        Assert.Null(onString.Logic);
        Assert.Equal(["S03-009"], onString.NoticeIds);
        Assert.Equal(["The condition '!input.code' tests input.code.value (string) with not; not takes a boolean — test text with blank."], onString.Notices);
    }

    [Fact]
    public void ATrimComparedWithEmptyText_IsBlank()
    {
        // Part 05 § 7 (the migrated test/general section_instructions.md): "the test moves to the part's if, using the blank
        // operation". (x | string.strip) == "" is blank(x), its != and its ! are !blank(x), for a text x; R1 is not involved.
        var scope = Scope(
            inputs: new() { ["reason"] = Format("markdown_block") },
            nodes: new() { ["summary"] = new ConversionRead("node.summary", Format("plaintext")) });

        var input = WorkflowConditionConverter.FromScriban("(input.reason | string.strip) == \"\"", scope);
        var notEqual = WorkflowConditionConverter.FromScriban("(string.strip input.reason) != \"\"", scope);
        var not = WorkflowConditionConverter.FromScriban("!((input.reason | string.strip) == \"\")", scope);
        var node = WorkflowConditionConverter.FromScriban("\"\" == (node.summary.content | string.strip)", scope);
        var literal = WorkflowConditionConverter.FromScriban("(input.reason | string.strip) == \"none\"", scope);

        Assert.Equal("""{"blank":[{"var":"input.reason.content"}]}""", CanonicalJson.Write(input.Logic));
        Assert.Equal("boolean", input.ResultFormat);
        Assert.Equal("""{"!":[{"blank":[{"var":"input.reason.content"}]}]}""", CanonicalJson.Write(notEqual.Logic));
        Assert.Equal("""{"!":[{"blank":[{"var":"input.reason.content"}]}]}""", CanonicalJson.Write(not.Logic));
        Assert.Equal("""{"blank":[{"var":"node.summary.content"}]}""", CanonicalJson.Write(node.Logic));
        Assert.All(new[] { input, notEqual, not, node }, r => Assert.Empty(r.Blanks));
        Assert.All(new[] { input, notEqual, not, node }, r => Assert.Empty(r.BlankConversions));

        // An input not given read "" at 30, so the input forms answer as 30 did; a node that did not run is null at 31, which
        // blank holds and 30's comparison did not (row.strip.node#0): that conversion is listed.
        Assert.Empty(input.TrimBlanks);
        Assert.Empty(notEqual.TrimBlanks);
        var listed = Assert.Single(node.TrimBlanks);
        Assert.Equal("\"\" = trim(node.summary.content)", listed.Written);
        Assert.Equal("blank(node.summary.content)", listed.Printed);
        Assert.Equal(["node.summary.content"], listed.Reads);

        // Only a text input read whole (input.<id>.content) read "" at 30 when not given: a field of an object input is
        // null at 30 when it or its object is absent (an unsupplied object input is an empty object), which 30's
        // comparison holds false and blank holds true (row.strip.input-field#0 and #2), so that conversion is listed too.
        var field = Convert("row.strip.input-field");
        Assert.Equal("""{"blank":[{"var":"input.patient.value.name"}]}""", CanonicalJson.Write(field.Logic));
        var fieldListed = Assert.Single(field.TrimBlanks);
        Assert.Equal("blank(input.patient.value.name)", fieldListed.Printed);
        Assert.Equal(["input.patient.value.name"], fieldListed.Reads);
        Assert.Empty(Convert("row.strip.input").TrimBlanks);

        // Any other literal is text compared with a literal, written as membership (#1508, comment 5, item 2).
        Assert.Equal("""{"in":[{"trim":[{"var":"input.reason.content"}]},["none"]]}""", CanonicalJson.Write(literal.Logic));
        Assert.Empty(literal.TrimBlanks);
    }

    /// <summary>
    /// #1508 (comment 5, item 2): 30 read an absent list as <c>[]</c> and counted 0, where 31's <c>size(null)</c> is null;
    /// the converter writes the null test that keeps 30's answer: a comparison that holds at 0 is
    /// <c>x === null or …</c>, one that does not is <c>x !== null and …</c> (<c>!= 0</c> written <c>&gt; 0</c>); with an
    /// operand not a number literal, an <c>if</c> on the null test compares 0. A list that cannot be null is untouched.
    /// </summary>
    [Theory]
    [InlineData("input.problems.size == 0", """{"or":[{"===":[{"var":"input.problems.value"},null]},{"===":[{"size":[{"var":"input.problems.value"}]},0]}]}""")]
    [InlineData("(input.problems | array.size) > 0", """{"and":[{"!==":[{"var":"input.problems.value"},null]},{">":[{"size":[{"var":"input.problems.value"}]},0]}]}""")]
    [InlineData("input.problems.size != 0", """{"and":[{"!==":[{"var":"input.problems.value"},null]},{">":[{"size":[{"var":"input.problems.value"}]},0]}]}""")]
    [InlineData("input.problems.size > 2", """{"and":[{"!==":[{"var":"input.problems.value"},null]},{">":[{"size":[{"var":"input.problems.value"}]},2]}]}""")]
    [InlineData("input.problems.size < 3", """{"or":[{"===":[{"var":"input.problems.value"},null]},{"<":[{"size":[{"var":"input.problems.value"}]},3]}]}""")]
    [InlineData("0 == input.problems.size", """{"or":[{"===":[{"var":"input.problems.value"},null]},{"===":[0,{"size":[{"var":"input.problems.value"}]}]}]}""")]
    [InlineData("input.problems.size >= input.age", """{"if":[{"===":[{"var":"input.problems.value"},null]},{">=":[0,{"var":"input.age.value"}]},{">=":[{"size":[{"var":"input.problems.value"}]},{"var":"input.age.value"}]}]}""")]
    [InlineData("input.listed.size == 0", """{"===":[{"size":[{"var":"input.listed.value"}]},0]}""")]
    public void TheSizeOfANullableList_Keeps30sMeaning(string expression, string logic)
    {
        var list = Format("""{"type":"array","items":{"$ref":"markdown_block"}}""");
        var scope = Scope(inputs: new() { ["problems"] = list, ["listed"] = list.DeepClone(), ["age"] = Format("number") }, required: ["listed", "age"]);

        var result = WorkflowConditionConverter.FromScriban(expression, scope);

        Assert.True(result.Logic != null, string.Join(" | ", result.Notices));
        Assert.Equal(logic, CanonicalJson.Write(result.Logic));
        Assert.Equal("boolean", result.ResultFormat);
        // A10-027 stays the notice: the size is still of a list that can be null.
        Assert.Equal(expression.Contains("problems", StringComparison.Ordinal) ? ["input.problems.value"] : [], ConditionTyping.NullableSizes(result.Logic, scope));
    }

    /// <summary>
    /// #1508 (comment 5, item 2): text compared with a literal (S03-002 at 31) is written as membership, which answers as
    /// 30's comparison did: <c>x == "lit"</c> is <c>x in ["lit"]</c>, <c>!=</c> its negation, the literal on either side;
    /// each is listed for the migration's notice (A10-074). Text compared with text stays S03-003.
    /// </summary>
    [Fact]
    public void TextComparedWithALiteral_BecomesIn()
    {
        var input = Convert("row.input.x");
        var field = Convert("row.input.x-y");
        var lower = Convert("row.downcase");
        var node = Convert("row.read.node-content");
        var item = Convert("composer-macros.section_draft.section_close.if");
        var reversed = WorkflowConditionConverter.FromScriban("\"Breathlessness\" != input.reason", NearScope());
        var texts = WorkflowConditionConverter.FromScriban("input.reason == node.summary.content", NearScope());

        Assert.Equal("""{"in":[{"var":"input.reason.content"},["Breathlessness"]]}""", CanonicalJson.Write(input.Logic));
        Assert.Equal("""{"in":[{"var":"input.patient.value.name"},["Margaret Okafor"]]}""", CanonicalJson.Write(field.Logic));
        Assert.Equal("""{"in":[{"lower":[{"var":"input.reason.content"}]},["breathlessness"]]}""", CanonicalJson.Write(lower.Logic));
        Assert.Equal("""{"in":[{"var":"node.summary.content"},["Reviewed in clinic."]]}""", CanonicalJson.Write(node.Logic));
        Assert.Equal("""{"!":[{"in":[{"var":"item.value.title"},["Plan"]]}]}""", CanonicalJson.Write(item.Logic));
        Assert.Equal("""{"!":[{"in":[{"var":"input.reason.content"},["Breathlessness"]]}]}""", CanonicalJson.Write(reversed.Logic));
        Assert.All(new[] { input, field, lower, node, item, reversed }, r => Assert.Equal("boolean", r.ResultFormat));
        Assert.All(new[] { input, field, lower, node, item, reversed }, r => Assert.Empty(r.Blanks));

        var listed = Assert.Single(item.TextLiterals);
        Assert.Equal("item.value.title ≠ \"Plan\"", listed.Written);
        Assert.Equal("not (item.value.title in [\"Plan\"])", listed.Printed);
        Assert.Equal(["item.value.title"], listed.Reads);
        var sentence = MigrationSentences.TextLiteralBecameIn("macros/section_close.md", "if", listed);
        Assert.Equal("macros/section_close.md, if: 'item.value.title ≠ \"Plan\"' became not (item.value.title in [\"Plan\"]); text is compared with a literal by in at 31.", sentence);
        V31Contract.AssertSentence(new SourceRefusal("A10-074", sentence, ""));

        Assert.Null(texts.Logic);
        Assert.Equal(["S03-003"], texts.NoticeIds);
    }

    private static void AssertBlank((string Written, string Printed, string[] Reads) expected, BlankConversion actual)
    {
        Assert.Equal(expected.Written, actual.Written);
        Assert.Equal(expected.Printed, actual.Printed);
        Assert.Equal(expected.Reads, actual.Reads);
    }

    [Fact]
    public void EveryRefusalOf2_3_GivesNoLogicAndASentence()
    {
        var expected = new Dictionary<string, (string Id, string Notice)>(StringComparer.Ordinal)
        {
            ["refuse.builtin.upcase"] = ("S10-003", "The condition '(string.upcase input.reason) == \"BREATHLESSNESS\"' uses string.upcase, which has no JSON Logic form at 31: rewrite it by hand."),
            ["refuse.builtin.round"] = ("S10-003", "The condition '(math.round input.age) == 65' uses math.round, which has no JSON Logic form at 31: rewrite it by hand."),
            ["refuse.builtin.first"] = ("S10-003", "The condition '(input.problems | array.first) == \"\"' uses array.first, which has no JSON Logic form at 31: rewrite it by hand."),
            ["refuse.text-less-than"] = ("S03-008", "The condition 'input.reason < \"m\"' orders input.reason.content (markdown_block | null); <, >, ≤ and ≥ compare numbers or dates."),
            ["refuse.profile"] = ("S03-018", "The condition 'profile.name == \"Dr A. Sample\"' reads profile.name; profile fields are read only in deliverable templates and macro bodies, never in conditions or computed expressions."),
            ["refuse.assignment"] = ("S10-003", "The condition 'x = 1' uses an assignment, which has no JSON Logic form at 31: rewrite it by hand."),
            ["refuse.template"] = ("S10-003", "The condition '{{ input.kind }}' uses a template, which has no JSON Logic form at 31: rewrite it by hand."),
            ["refuse.two-statements"] = ("S10-003", "The condition 'input.urgent; input.kind == \"new\"' uses more than one statement, which has no JSON Logic form at 31: rewrite it by hand."),
        };

        var ids = MigrationFixture.Expressions().Select(e => (string)e["id"]!).Where(id => id.StartsWith("refuse.", StringComparison.Ordinal)).ToList();
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), ids.Order(StringComparer.Ordinal));
        foreach (var id in ids)
        {
            var result = Convert(id);

            Assert.Null(result.Logic);
            Assert.Null(result.ResultFormat);
            Assert.Equal([expected[id].Notice], result.Notices);
            Assert.Equal([expected[id].Id], result.NoticeIds);
        }
    }

    [Fact]
    public void ACondition_YieldsABoolean()
    {
        var scope = Scope(inputs: new() { ["age"] = Format("number") }, required: ["age"]);

        var condition = WorkflowConditionConverter.FromScriban("input.age", scope);
        var value = WorkflowConditionConverter.FromScriban("input.age", scope, ConversionKind.Value);

        Assert.Null(condition.Logic);
        Assert.Equal(["S03-025"], condition.NoticeIds);
        Assert.Equal(["The condition 'input.age' if yields number; a condition yields a boolean."], condition.Notices);
        Assert.Equal("""{"var":"input.age.value"}""", CanonicalJson.Write(value.Logic));
        Assert.Equal("number", value.ResultFormat);
        Assert.Empty(value.Notices);
    }

    [Fact]
    public void AValue_IsHeldToItsDeclaredFormat()
    {
        var scope = Scope(inputs: new() { ["age"] = Format("number") }, required: ["age"]);

        var fits = WorkflowConditionConverter.FromScriban("input.age + 1", scope, ConversionKind.Value, JsonValue.Create("number"));
        var misfit = WorkflowConditionConverter.FromScriban("input.age + 1", scope, ConversionKind.Value, JsonValue.Create("boolean"));
        var noFormat = WorkflowConditionConverter.FromScriban("{ age: input.age }", scope, ConversionKind.Value);

        Assert.Equal("number", fits.ResultFormat);
        Assert.Null(misfit.Logic);
        Assert.Equal(["S03-026"], misfit.NoticeIds);
        Assert.Null(noFormat.Logic);
        Assert.Equal(["A04-008"], noFormat.NoticeIds);
    }

    [Fact]
    public void AnOptionalRead_IsNullable_AsTheCheckerHoldsIt()
    {
        // A 30 let declared number reading an optional input yields number | null at 31: publish refuses it (S03-026), so
        // the converter does too; over a required input, or a node that always has a value, it converts. (An operation's
        // result is never nullable for typing, D25: input.age + 1 is number either way.)
        var inputs = new Dictionary<string, JsonNode> { ["age"] = Format("number") };
        var nodes = new Dictionary<string, ConversionRead> { ["score"] = new ConversionRead("node.score", JsonValue.Create("number")) };
        var optional = Scope(inputs: inputs, nodes: nodes);
        var required = Scope(inputs: inputs, nodes: nodes, required: ["age"]);
        var valued = Scope(nodes: new() { ["score"] = new ConversionRead("node.score", JsonValue.Create("number")) { AlwaysValued = true } });
        var number = JsonValue.Create("number");

        var overOptional = WorkflowConditionConverter.FromScriban("input.age", optional, ConversionKind.Value, number);
        var overRequired = WorkflowConditionConverter.FromScriban("input.age", required, ConversionKind.Value, number);
        var sum = WorkflowConditionConverter.FromScriban("input.age + 1", optional, ConversionKind.Value, number);
        var overNode = WorkflowConditionConverter.FromScriban("node.score.value", required, ConversionKind.Value, number);
        var overValuedNode = WorkflowConditionConverter.FromScriban("node.score.value", valued, ConversionKind.Value, number);

        Assert.Null(overOptional.Logic);
        Assert.Equal(["S03-026"], overOptional.NoticeIds);
        V31Contract.AssertSentence(new SourceRefusal(overOptional.NoticeIds[0], overOptional.Notices[0], ""));
        Assert.Equal("Computed node 'input.age' yields number | null; it declares number.", overOptional.Notices[0]);
        Assert.Equal("""{"var":"input.age.value"}""", CanonicalJson.Write(overRequired.Logic));
        Assert.Equal("number", overRequired.ResultFormat);
        Assert.Equal("number", sum.ResultFormat);
        Assert.Equal(["S03-026"], overNode.NoticeIds);
        Assert.Equal("number", overValuedNode.ResultFormat);
    }

    [Theory]
    [MemberData(nameof(AllIds))]
    public void TheReads_AreWhatTheLogicReads(string id)
    {
        var result = Convert(id);

        Assert.Equal(result.Logic is null ? [] : JsonLogicShape.Paths(result.Logic), result.Reads);
    }

    [Fact]
    public void TheReads_AreInWrittenOrder_EachOnce()
    {
        Assert.Equal(["input.urgent.value", "input.kind.value", "input.age.value"], Convert("row.and3").Reads);
        // An object's fields are written in canonical order, so its reads are too.
        var scope = Scope(
            inputs: new() { ["age"] = Format("number"), ["kind"] = Format("""{"type":"string","enum":["a","b"]}""") },
            required: ["age", "kind"]);
        var declared = JsonNode.Parse("""{"type":"object","additionalProperties":false,"required":["priority","age"],"properties":{"priority":{"type":"string","enum":["a","b"]},"age":{"type":"number"}}}""");
        Assert.Equal(["input.age.value", "input.kind.value"], WorkflowConditionConverter.FromScriban("{ priority: input.kind, age: input.age }", scope, ConversionKind.Value, declared).Reads);
        Assert.Equal(["input.note_type.value", "input.include_counselling.value"], Convert("example-macros.brief_plan.md.if").Reads);
    }

    [Theory]
    [MemberData(nameof(AllIds))]
    public void EveryNotice_IsItsSentence(string id)
    {
        var result = Convert(id);

        Assert.Equal(result.Notices.Count, result.NoticeIds.Count);
        for (var i = 0; i < result.Notices.Count; i++)
        {
            V31Contract.AssertSentence(new SourceRefusal(result.NoticeIds[i], result.Notices[i], ""));
        }
    }

    [Fact]
    public void ABlankConversion_IsSaidWithA10_008()
    {
        var blank = Convert("row.not-text.input").Blanks.Single();
        var sentence = MigrationSentences.BecameBlank("nodes/gate.md", "if", blank);

        Assert.Equal("nodes/gate.md, if: '!input.reason' became blank(input.reason.content); it now also holds for empty and white-space-only text.", sentence);
        V31Contract.AssertSentence(new SourceRefusal("A10-008", sentence, ""));
    }

    /// <summary>
    /// Review Focus 1, and the edges of the table: each expression with the logic it converts to, or <c>null</c> with the
    /// ids of its notices, in order. The scope: an optional boolean <c>flag</c>, an enum <c>kind</c> (<c>new</c>,
    /// <c>review</c>), a text <c>reason</c>, a number <c>age</c>, a list of text <c>problems</c>, a string <c>notes</c> (all
    /// but <c>flag</c> required),
    /// a node <c>summary</c> of plain text, a snippet folder's item and a <c>while</c>'s pass.
    /// </summary>
    [Theory]
    [InlineData("!(input.kind == \"new\")", """{"!":[{"===":[{"var":"input.kind.value"},"new"]}]}""", "")]
    [InlineData("!input.flag", """{"!":[{"var":"input.flag.value"}]}""", "")]
    [InlineData("input.reason == \"\"", """{"in":[{"var":"input.reason.content"},[""]]}""", "")]
    [InlineData("input.kind == \"old\"", null, "S03-023")]
    [InlineData("input.age - -1 > 65", """{">":[{"-":[{"var":"input.age.value"},-1]},65]}""", "")]
    [InlineData("input.age == -1.5", """{"===":[{"var":"input.age.value"},-1.5]}""", "")]
    [InlineData("(input.problems | array.size) > 0", """{">":[{"size":[{"var":"input.problems.value"}]},0]}""", "")]
    [InlineData("input.notes == \"say \\\"hi\\\"\"", """{"===":[{"var":"input.notes.value"},"say \"hi\""]}""", "")]
    [InlineData("input.notes == \"a\\nb\"", """{"===":[{"var":"input.notes.value"},"a\nb"]}""", "")]
    [InlineData("((input.age > 1) && ((input.flag)))", """{"and":[{">":[{"var":"input.age.value"},1]},{"var":"input.flag.value"}]}""", "")]
    [InlineData("input.reason.size > 3", null, "S10-003")]
    [InlineData("input.problems.size > 3", """{">":[{"size":[{"var":"input.problems.value"}]},3]}""", "")]
    [InlineData("input.notes == \"{{\"", """{"===":[{"var":"input.notes.value"},"{{"]}""", "")]
    [InlineData("input.notes == '{% x'", """{"===":[{"var":"input.notes.value"},"{% x"]}""", "")]
    [InlineData("input.notes == \"{{\" || {{ x }}", null, "S10-003")]
    [InlineData("input.kind == null", null, "S10-003")]
    [InlineData("item.foo == \"x\"", null, "S10-003")]
    [InlineData("item.value.kind == \"x\"", null, "S03-014")]
    [InlineData("item.id == \"plan\" && item.content == \"\"", """{"and":[{"===":[{"var":"item.id"},"plan"]},{"in":[{"var":"item.content"},[""]]}]}""", "")]
    [InlineData("pass.foo == 1", null, "S10-003")]
    [InlineData("pass.previous.number == 1", null, "S10-003")]
    [InlineData("pass.node.summary.content == \"x\"", null, "S10-003")]
    [InlineData("node.summary.foo == \"x\"", null, "S10-003")]
    [InlineData("node.summary.state == \"completed\"", null, "S10-003")]
    [InlineData("(node.summary.items | array.size) > 0", null, "S10-003")]
    [InlineData("node.summary.value.foo == \"x\"", null, "S03-014")]
    [InlineData("input.kind.foo == \"x\"", null, "S03-014")]
    [InlineData("snippet.x.content == \"x\"", null, "S10-003")]
    [InlineData("string.append \"a\" (input.reason | string.size)", null, "S10-003 S10-003")]
    [InlineData("(string.upcase (math.round input.age)) == \"X\"", null, "S10-003 S10-003")]
    [InlineData("input.age == 1 && input.age == 2 && input.notes < 3", null, "S03-008")]
    public void AnExpressionNearTheTable_ConvertsOrRefuses_NeverThrows(string expression, string? logic, string ids)
    {
        var result = WorkflowConditionConverter.FromScriban(expression, NearScope());

        Assert.Equal(logic, result.Logic is null ? null : CanonicalJson.Write(result.Logic));
        Assert.Equal(ids, string.Join(' ', result.NoticeIds));
        Assert.Equal(logic is null, result.Notices.Count > 0);
        for (var i = 0; i < result.Notices.Count; i++)
        {
            V31Contract.AssertSentence(new SourceRefusal(result.NoticeIds[i], result.Notices[i], ""));
        }
    }

    [Theory]
    [InlineData("{ a: input.age, a: input.notes }", null, "S10-003")]
    [InlineData("{ \"a\": input.age }", null, "A04-008")]
    // A computed field name: Scriban's parser takes only an identifier or a string there, so the text does not parse.
    [InlineData("{ (input.notes): input.age }", null, "S10-003")]
    [InlineData("[input.age, -1]", """[{"var":"input.age.value"},-1]""", "")]
    [InlineData("null", null, "S10-003")]
    public void AValueNearTheTable_ConvertsOrRefuses_NeverThrows(string expression, string? logic, string ids)
    {
        var result = WorkflowConditionConverter.FromScriban(expression, NearScope(), ConversionKind.Value);

        Assert.Equal(logic, result.Logic is null ? null : CanonicalJson.Write(result.Logic));
        Assert.Equal(ids, string.Join(' ', result.NoticeIds));
        for (var i = 0; i < result.Notices.Count; i++)
        {
            V31Contract.AssertSentence(new SourceRefusal(result.NoticeIds[i], result.Notices[i], ""));
        }
    }

    [Fact]
    public void ADuplicateField_IsNamed()
    {
        var result = WorkflowConditionConverter.FromScriban("{ a: input.age, a: input.notes }", NearScope(), ConversionKind.Value);

        Assert.Equal(["The condition '{ a: input.age, a: input.notes }' uses the object field 'a' twice, which has no JSON Logic form at 31: rewrite it by hand."], result.Notices);
    }

    [Fact]
    public void ALogicNestedDeeperThanAFileHolds_IsRefused()
    {
        // Each ! writes two levels ({"!": [ … ]}); the header object that holds the rule is one more (SourceBounds.JsonDepth).
        static string Nots(int count) => string.Concat(Enumerable.Repeat("!(", count)) + "input.flag" + new string(')', count);

        var fits = WorkflowConditionConverter.FromScriban(Nots(15), NearScope());
        var tooDeep = WorkflowConditionConverter.FromScriban(Nots(16), NearScope());

        Assert.NotNull(fits.Logic);
        Assert.Null(tooDeep.Logic);
        Assert.Equal([$"The condition '{Nots(16)}' uses nesting deeper than {SourceBounds.JsonDepth} levels, which has no JSON Logic form at 31: rewrite it by hand."], tooDeep.Notices);
    }

    [Fact]
    public void ParenthesesAloneAddNoDepth_AndNoNestingThrows()
    {
        var parens = WorkflowConditionConverter.FromScriban(new string('(', 100) + "input.flag" + new string(')', 100), NearScope());
        var deep = WorkflowConditionConverter.FromScriban(string.Concat(Enumerable.Repeat("!(", 5000)) + "input.flag" + new string(')', 5000), NearScope());

        Assert.Equal("""{"var":"input.flag.value"}""", CanonicalJson.Write(parens.Logic));
        Assert.Null(deep.Logic);
        Assert.Single(deep.Notices);
    }

    private static ConversionScope NearScope() =>
        new(
            new Dictionary<string, JsonNode>(StringComparer.Ordinal)
            {
                ["flag"] = Format("""{"type":["boolean","null"]}"""),
                ["kind"] = Format("""{"type":"string","enum":["new","review"]}"""),
                ["reason"] = Format("markdown_block"),
                ["age"] = Format("number"),
                ["problems"] = Format("""{"type":"array","items":{"$ref":"markdown_block"}}"""),
                ["notes"] = Format("string"),
            },
            new Dictionary<string, ConversionRead>(StringComparer.Ordinal) { ["summary"] = new ConversionRead("node.summary", JsonValue.Create("plaintext")) },
            null,
            true,
            true,
            MigrationFixture.Formats())
        {
            RequiredInputs = new HashSet<string>(["kind", "reason", "age", "problems", "notes"], StringComparer.Ordinal),
        };

    private static void AssertMembersInCanonicalOrder(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var keys = obj.Select(p => p.Key).ToList();
                Assert.Equal(keys.Order(StringComparer.Ordinal), keys);
                foreach (var (_, value) in obj)
                {
                    AssertMembersInCanonicalOrder(value);
                }

                break;
            case JsonArray array:
                foreach (var element in array)
                {
                    AssertMembersInCanonicalOrder(element);
                }

                break;
        }
    }

    private static JsonNode Format(string format) =>
        format.StartsWith('{') ? JsonNode.Parse(format)! : JsonValue.Create(format);

    private static ConversionScope Scope(
        Dictionary<string, JsonNode>? inputs = null,
        Dictionary<string, ConversionRead>? nodes = null,
        string[]? required = null) =>
        new(
            inputs ?? new Dictionary<string, JsonNode>(StringComparer.Ordinal),
            nodes ?? new Dictionary<string, ConversionRead>(StringComparer.Ordinal),
            null,
            false,
            false,
            MigrationFixture.Formats())
        {
            RequiredInputs = new HashSet<string>(required ?? [], StringComparer.Ordinal),
        };
}
