using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Logic;

/// <summary>
/// #1501: the expression checker's first steps (part 03 §§ 3, 4, 7, 11.1; D2, D4, D26): the written form,
/// every read typed through the scope and the format it reaches, the site rules, and a condition that
/// yields no boolean. Operations are typed in Task 7; here only literals and reads have a type.
/// </summary>
public class ExpressionCheckerReadsTests
{
    private const string Choice = """
        {"type":"object","properties":{"pick":{"anyOf":[
          {"type":"object","properties":{"dose":{"type":"number"}},"required":["dose"],"additionalProperties":false},
          {"type":"object","properties":{"route":{"type":"string"}},"required":["route"],"additionalProperties":false}]}},
         "required":["pick"],"additionalProperties":false}
        """;

    private static readonly SourceRefusal Withheld = new("S04-050", "Node 'gate' reads pass.secret, which a function can't read.", "nodes/gate.md");

    private static readonly FormatSet Formats = LogicFixture.Formats();

    private static readonly LogicFixture.MapScope Scope = new(new Dictionary<string, ScopeHead>
    {
        ["input.kind"] = LogicFixture.FormatHead("""{"type":"string","enum":["narrative","table"]}"""),
        ["input.specialty"] = LogicFixture.FormatHead(
            """{"type":"string","oneOf":[{"const":"med_onc","title":"Medical oncology"},{"const":"rad_onc","title":"Radiation oncology"}]}"""),
        ["input.consult_draft"] = LogicFixture.FormatHead("plaintext"),
        ["input.age"] = LogicFixture.FormatHead("number"),
        ["input.choice"] = LogicFixture.FormatHead(Choice),
        ["input.include_billing"] = LogicFixture.FormatHead("boolean"),
        ["node.draft"] = LogicFixture.Deferred(),
        ["item"] = LogicFixture.Deferred(),
        ["run.date"] = LogicFixture.ValueHead("date"),
        ["pass.secret"] = LogicFixture.Refused(Withheld),
    });

    private static ExpressionCheck Check(string rule, ExpressionSiteKind kind = ExpressionSiteKind.Expression) =>
        ExpressionChecker.Check(LogicFixture.Rule(rule), LogicFixture.Site(kind), Scope, Formats);

    [Fact]
    public void AShapeFault_IsItsSentence_AndStopsTheCheck()
    {
        var check = Check("""{"==":[{"var":"input.nope.value"},1]}""");

        var refusal = Assert.Single(check.Refusals);
        Assert.Equal("S03-004", refusal.Id);
        Assert.Null(check.Type);
        Assert.Equal("==(input.nope.value, 1)", check.Printed);
    }

    public static TheoryData<string, string, string> ReadRows => new()
    {
        { """{"var":"input.nope.value"}""", "S03-014", "Computed node 'total' reads input.nope.value, which doesn't exist here." },
        { """{"var":"input.kind"}""", "S03-015", "Computed node 'total' reads input.kind; an input is read as input.kind.value, .content or .title." },
        { """{"var":"input.kind.title"}""", "S03-016", "Computed node 'total' reads input.kind.title, but string {narrative, table} is a plain enum with no titles; read .value, or give the format oneOf with const and title." },
        { """{"var":"input.choice.value.pick.dose"}""", "S03-024", "Computed node 'total' reads input.choice.value.pick.dose, which not every member of { dose: number } | { route: string } declares." },
        { """{"var":"profile.name"}""", "S03-018", "Computed node 'total' reads profile.name; profile fields are read only in deliverable templates and macro bodies, never in conditions or computed expressions." },
        { """{"var":"snippet.standards"}""", "S03-019", "Computed node 'total' reads snippet.standards; JSON Logic reads input, node, check, item, pass, param and run." },
        { """{"var":"wibble.x"}""", "S03-014", "Computed node 'total' reads wibble.x, which doesn't exist here." },
        { """{"var":"input.age.value.x"}""", "S03-014", "Computed node 'total' reads input.age.value.x, which doesn't exist here." },
        { """{"var":"input.age.content"}""", "S03-014", "Computed node 'total' reads input.age.content, which doesn't exist here." },
        { """{"var":"run.weather"}""", "S03-014", "Computed node 'total' reads run.weather, which doesn't exist here." },
        { """{"missing":["input.nope.value"]}""", "S03-014", "Computed node 'total' reads input.nope.value, which doesn't exist here." },
    };

    /// <summary>A root alone, and every other root S03-019 names (D34): judged before the scope is asked.</summary>
    public static TheoryData<string, string, string> RootRows => new()
    {
        { "profile", "S03-014", "Computed node 'total' reads profile, which doesn't exist here." },
        { "input", "S03-014", "Computed node 'total' reads input, which doesn't exist here." },
        { "wibble", "S03-014", "Computed node 'total' reads wibble, which doesn't exist here." },
        { "snippet", "S03-014", "Computed node 'total' reads snippet, which doesn't exist here." },
        { "part.x", "S03-019", "Computed node 'total' reads part.x; JSON Logic reads input, node, check, item, pass, param and run." },
        { "macro.x", "S03-019", "Computed node 'total' reads macro.x; JSON Logic reads input, node, check, item, pass, param and run." },
        { "renderer.x", "S03-019", "Computed node 'total' reads renderer.x; JSON Logic reads input, node, check, item, pass, param and run." },
        { "deliverable.x.y", "S03-019", "Computed node 'total' reads deliverable.x.y; JSON Logic reads input, node, check, item, pass, param and run." },
    };

    [Theory]
    [MemberData(nameof(RootRows))]
    public void Roots(string path, string id, string sentence) =>
        Reads($$"""{"var":"{{path}}"}""", id, sentence);

    /// <summary>A bare <c>node</c>, <c>check</c> or <c>param</c> is the scope's to judge (D2, D34).</summary>
    [Theory]
    [InlineData("node")]
    [InlineData("check")]
    [InlineData("param")]
    public void ABareScopeRoot_IsTheScopes(string root)
    {
        // this test's MapScope knows no bare root: Unknown
        Reads($$"""{"var":"{{root}}"}""", "S03-014", $"Computed node 'total' reads {root}, which doesn't exist here.");

        // a scope that defers it: no refusal
        var deferring = new LogicFixture.MapScope(new Dictionary<string, ScopeHead> { [root] = LogicFixture.Deferred() });
        var check = ExpressionChecker.Check(LogicFixture.Rule($$"""{"var":"{{root}}"}"""), LogicFixture.Site(ExpressionSiteKind.Expression), deferring, Formats);
        Assert.Empty(check.Refusals);
    }

    [Theory]
    [MemberData(nameof(ReadRows))]
    public void Reads(string rule, string id, string sentence)
    {
        var check = Check(rule);

        var refusal = Assert.Single(check.Refusals);
        Assert.Equal(id, refusal.Id);
        Assert.Equal(sentence, refusal.Sentence);
        Assert.Equal("nodes/total.md", refusal.Path);
        V31Contract.AssertSentence(refusal);
        Assert.Null(check.Type);
    }

    public static TheoryData<ExpressionSiteKind, string, string, string> SiteRows => new()
    {
        { ExpressionSiteKind.DeliverableIf, """{"===":[{"var":"node.draft.content"},"x"]}""", "S03-020", "Deliverable 'note' if reads node.draft.content; a deliverable's if reads data (.value), not text." },
        { ExpressionSiteKind.DeliverableIf, """{"var":"check.coverage.value.pass"}""", "S03-021", "Deliverable 'note' if reads check.coverage; a deliverable's existence can't depend on a check." },
        { ExpressionSiteKind.MacroIf, """{"var":"param.standard.value"}""", "S03-022", "Macro 'footer' if reads param.standard; a macro's if is shared, so it can't depend on one function's parameter." },
    };

    [Theory]
    [MemberData(nameof(SiteRows))]
    public void SiteRules(ExpressionSiteKind kind, string rule, string id, string sentence)
    {
        var refusal = Assert.Single(Check(rule, kind).Refusals);
        Assert.Equal(id, refusal.Id);
        Assert.Equal(sentence, refusal.Sentence);
        V31Contract.AssertSentence(refusal);

        Assert.DoesNotContain(Check(rule, ExpressionSiteKind.NodeIf).Refusals, r => r.Id is "S03-020" or "S03-021" or "S03-022");
    }

    [Fact]
    public void SiteRules_ADeliverableReadsANodesValue_AndASnippetReadsAParameter()
    {
        Assert.Empty(Check("""{"===":[{"var":"node.draft.value"},"x"]}""", ExpressionSiteKind.DeliverableIf).Refusals);
        Assert.DoesNotContain(Check("""{"var":"param.standard.value"}""", ExpressionSiteKind.SnippetIf).Refusals, r => r.Id == "S03-022");
    }

    [Fact]
    public void TypedReads()
    {
        var age = Check("""{"var":"input.age.value"}""");
        Assert.Empty(age.Refusals);
        Assert.Equal("number", TypeStrings.Print(age.Type!.Format, Formats));
        Assert.False(age.Type.Nullable);

        Assert.Equal("date", TypeStrings.Print(Check("""{"var":"run.date"}""").Type!.Format, Formats));

        var title = Check("""{"var":"input.specialty.title"}""").Type!;
        Assert.Equal("string", TypeStrings.Print(title.Format, Formats));
        Assert.Equal(["Medical oncology", "Radiation oncology"], title.Titles!);

        var deferred = Check("""{"var":"node.draft.value"}""");
        Assert.Empty(deferred.Refusals);
        Assert.Null(deferred.Type!.Format);

        Assert.Same(Withheld, Assert.Single(Check("""{"var":"pass.secret.value"}""").Refusals));
    }

    [Fact]
    public void TypedReads_ANullableHeadMakesItsReadNullable()
    {
        var scope = new LogicFixture.MapScope(new Dictionary<string, ScopeHead>
        {
            ["input.age"] = LogicFixture.FormatHead("number", nullable: true),
        });

        var check = ExpressionChecker.Check(LogicFixture.Rule("""{"var":"input.age.value"}"""), LogicFixture.Site(ExpressionSiteKind.Expression), scope, Formats);

        Assert.True(check.Type!.Nullable);
    }

    [Fact]
    public void AConditionYieldsABoolean()
    {
        var gate = Assert.Single(Check("""{"var":"input.age.value"}""", ExpressionSiteKind.NodeIf).Refusals);
        Assert.Equal("S03-025", gate.Id);
        Assert.Equal("Node 'gate' if yields number; a condition yields a boolean.", gate.Sentence);
        V31Contract.AssertSentence(gate);

        Assert.Equal(
            "Node 'gate' while yields number; a condition yields a boolean.",
            Assert.Single(Check("""{"var":"input.age.value"}""", ExpressionSiteKind.While).Refusals).Sentence);

        Assert.Empty(Check("""{"missing":["input.age.value"]}""", ExpressionSiteKind.NodeIf).Refusals);
        Assert.Empty(Check("""{"var":"node.draft.value"}""", ExpressionSiteKind.NodeIf).Refusals);
        Assert.Empty(Check("""{"var":"input.include_billing.value"}""", ExpressionSiteKind.NodeIf).Refusals);

        var always = Check("null", ExpressionSiteKind.NodeIf);
        Assert.Empty(always.Refusals);
        Assert.Equal("boolean", TypeStrings.Print(always.Type!.Format, Formats));
    }

    [Fact]
    public void AConditionYieldsABoolean_ANullableReadSaysSo()
    {
        var scope = new LogicFixture.MapScope(new Dictionary<string, ScopeHead>
        {
            ["input.age"] = LogicFixture.FormatHead("number", nullable: true),
        });

        var check = ExpressionChecker.Check(LogicFixture.Rule("""{"var":"input.age.value"}"""), LogicFixture.Site(ExpressionSiteKind.Until), scope, Formats);

        Assert.Equal("Node 'gate' until yields number | null; a condition yields a boolean.", Assert.Single(check.Refusals).Sentence);
    }

    [Fact]
    public void Reads_AndPrinted_AreReported()
    {
        var check = Check(
            """{ "or": [ { "!==": [ { "var": "item.id" }, "billing" ] }, { "===": [ { "var": "input.include_billing.value" }, true ] } ] }""",
            ExpressionSiteKind.NodeIf);

        Assert.Empty(check.Refusals);
        Assert.Equal(["item.id", "input.include_billing.value"], check.Reads);
        Assert.Equal("item.id ≠ \"billing\" or input.include_billing.value = true", check.Printed);
        Assert.Empty(check.PlainConverts);
    }

    [Fact]
    public void ARefusedRead_IsSaidOnce_AndTheOthersStillRead()
    {
        var check = Check("""{"and":[{"var":"wibble.x"},{"var":"wibble.x"},{"var":"profile.name"}]}""", ExpressionSiteKind.NodeIf);

        Assert.Equal(["S03-014", "S03-018"], check.Refusals.Select(r => r.Id));
    }

    [Fact]
    public void ANullArgument_IsTheOnlyThrow()
    {
        var site = LogicFixture.Site(ExpressionSiteKind.NodeIf);
        Assert.Throws<ArgumentNullException>(() => ExpressionChecker.Check(null, null!, Scope, Formats));
        Assert.Throws<ArgumentNullException>(() => ExpressionChecker.Check(null, site, null!, Formats));
        Assert.Throws<ArgumentNullException>(() => ExpressionChecker.Check(null, site, Scope, null!));
    }

    [Theory]
    [MemberData(nameof(JsonLogicShapeTests.Canonical), MemberType = typeof(JsonLogicShapeTests))]
    public void AnyRule_NeverThrows(string json)
    {
        foreach (var mutant in LogicFixture.Mutants(LogicFixture.Rule(json)))
        {
            foreach (var kind in new[] { ExpressionSiteKind.NodeIf, ExpressionSiteKind.Expression })
            {
                var check = ExpressionChecker.Check(mutant, LogicFixture.Site(kind), Scope, Formats);

                Assert.NotNull(check.Printed);
                Assert.Equal(check.Refusals.Count == 0, check.Type != null);
            }
        }
    }
}
