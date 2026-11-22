using System.Text.Json.Nodes;
using Consultologist.Api.Tests.ByteEquivalence;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 §§ 2.1, 2.2): the converter's tree. Every row of § 2.2's table, as
/// <c>conditions/expressions.json</c> writes one or more expressions for it, converts to its JSON Logic over the scope
/// its <c>reads</c> describe (<see cref="MigrationFixture.ScopeOf"/>); the output is canonical and what the printer
/// reads back; and <c>conditions/converted.json</c> holds what the converter gives for every expression, in order (with
/// <c>GOLDEN_UPDATE_V31=1</c> the test rewrites it instead; a rewritten golden is read before it is committed).
/// </summary>
public class ConditionConverterTests
{
    private const string GoldenPath = "conditions/converted.json";

    public static TheoryData<string> RowIds() =>
        new(MigrationFixture.Expressions().Select(e => (string)e["id"]!).Where(id => id.StartsWith("row.", StringComparison.Ordinal)));

    public static TheoryData<string> AllIds() =>
        new(MigrationFixture.Expressions().Select(e => (string)e["id"]!));

    private static ConversionResult Convert(string id)
    {
        var entry = MigrationFixture.Expression(id);
        return WorkflowConditionConverter.FromScriban((string)entry["expression"]!, MigrationFixture.ScopeOf(entry["reads"]!.AsObject()));
    }

    [Fact]
    public void TheRowsOf2_2_AreFortyOne() => Assert.Equal(41, RowIds().Count);

    [Theory]
    [MemberData(nameof(RowIds))]
    public void EveryRowOf2_2_ConvertsToItsLogic(string id)
    {
        var result = Convert(id);

        // `!x` on text is Task 3's (blank, § 2.3); its logic is asserted there.
        if (id.StartsWith("row.not-text.", StringComparison.Ordinal))
        {
            return;
        }

        Assert.True(result.Logic != null, $"{id}: {string.Join(" | ", result.Notices)}");
        Assert.Empty(result.Notices);
    }

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
    [InlineData("row.read.node-value", """{"===":[{"var":"node.triage.value.priority"},"high"]}""")]
    [InlineData("row.read.item-id", """{"===":[{"var":"item.id"},"plan"]}""")]
    [InlineData("row.read.item-value", """{"===":[{"var":"item.value.kind"},"table"]}""")]
    [InlineData("row.read.pass-number", """{">=":[{"var":"pass.number"},2]}""")]
    [InlineData("row.read.run-date", """{"===":[{"var":"run.date"},"2026-11-10"]}""")]
    [InlineData("example-classifier-scope.plan.if", """{"===":[{"var":"node.scope.value.value"},"in_scope"]}""")]
    [InlineData("example-macros.brief_plan.md.if", """{"and":[{"or":[{"===":[{"var":"input.note_type.value"},"consult"]},{"===":[{"var":"input.note_type.value"},"follow_up"]}]},{"!==":[{"var":"input.include_counselling.value"},true]}]}""")]
    public void ARow_IsPinned(string id, string expected)
    {
        var result = Convert(id);

        Assert.Equal(expected, CanonicalJson.Write(result.Logic));
    }

    [Fact]
    public void AnInputRead_IsContentOnTextAndValueOtherwise()
    {
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
            return CanonicalJson.Write(WorkflowConditionConverter.FromScriban(expression, scope).Logic);
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
        Dictionary<string, ConversionRead>? nodes = null) =>
        new(
            inputs ?? new Dictionary<string, JsonNode>(StringComparer.Ordinal),
            nodes ?? new Dictionary<string, ConversionRead>(StringComparer.Ordinal),
            null,
            false,
            false,
            MigrationFixture.Formats());
}
