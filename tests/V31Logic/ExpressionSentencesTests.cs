using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Logic;

/// <summary>
/// #1501: every sentence the expression checker says (part 03 § 11.1, § 11.3, and the area's A04 rows) is its
/// row with its placeholders filled in, and each fault of the written form has its sentence.
/// </summary>
public class ExpressionSentencesTests
{
    private static readonly ExpressionSite Gate = LogicFixture.Site(ExpressionSiteKind.NodeIf);
    private static readonly ExpressionSite Total = LogicFixture.Site(ExpressionSiteKind.Expression);
    private static readonly ExpressionSite Note = LogicFixture.Site(ExpressionSiteKind.DeliverableIf);
    private static readonly ExpressionSite Footer = LogicFixture.Site(ExpressionSiteKind.MacroIf);

    private static IEnumerable<SourceRefusal> Every() =>
    [
        ExpressionSentences.ComparesTextWithLiteral(Gate, "input.consult_draft.content", "plaintext"),
        ExpressionSentences.ComparesTextWithText(Gate, "input.consult_draft.content", "plaintext", "node.draft.content", "markdown_block"),
        ExpressionSentences.LooseEquality(Gate),
        ExpressionSentences.LooseInequality(Gate),
        ExpressionSentences.UnknownOperation(Gate, "map"),
        ExpressionSentences.Substr(Gate),
        ExpressionSentences.Orders(Gate, "input.kind.value", "string {narrative, table}"),
        ExpressionSentences.NotOnText(Gate, "input.consult_draft.content", "plaintext"),
        ExpressionSentences.Arity(Gate, "<", 3, "2"),
        ExpressionSentences.BareOperand(Gate, "!"),
        ExpressionSentences.VarDefault(Gate),
        ExpressionSentences.Position(Gate, "node.rows.value.0"),
        ExpressionSentences.DoesNotExist(Gate, "input.nope.value"),
        ExpressionSentences.BareInput(Gate, "kind"),
        ExpressionSentences.TitleOfPlainEnum(Gate, "input.kind", "string {narrative, table}"),
        ExpressionSentences.NotATitle(Gate, "input.specialty", "Surgery", "Medical oncology, Radiation oncology"),
        ExpressionSentences.ReadsProfile(Gate, "name"),
        ExpressionSentences.NotAJsonLogicRoot(Gate, "snippet.standards"),
        ExpressionSentences.DeliverableReadsContent(Note, "draft"),
        ExpressionSentences.DeliverableReadsCheck(Note, "coverage"),
        ExpressionSentences.MacroReadsParam(Footer, "standard"),
        ExpressionSentences.NotAValue(Gate, "input.kind.value", "graph", "narrative, table"),
        ExpressionSentences.NotEveryMember(Gate, "input.choice", "pick.dose", "{ dose: number } | { route: string }"),
        ExpressionSentences.NotABoolean(Gate, "number"),
        ExpressionSentences.YieldsNotDeclared(Total, "string", "number"),
        ExpressionSentences.BuildsWithout(Total, "{ a: number, b: string }", "b"),
        ExpressionSentences.BuildsUndeclared(Total, "{ a: number }", "c"),
        ExpressionSentences.ComputedLiteral(Gate, "matches", "pattern"),
        ExpressionSentences.CountPassMin(Gate, "-1"),
        ExpressionSentences.PatternNotIRegexp(Gate, "\\d", "'\\d' at character 1 is not an escape of I-Regexp"),
        ExpressionSentences.PatternTooLong(Gate, "aaa", 201),
        ExpressionSentences.BlankWord(Gate),
        ExpressionSentences.IfWithoutElse(Gate),
        ExpressionSentences.CatsMarkdown(Gate, "input.summary.content", "markdown_block"),
        ExpressionSentences.PatternRepeats(Gate, "a{101}", 101),
        ExpressionSentences.PatternDoesNotCompile(Gate, "(a{100}){100}", "it needs too large an automaton"),
        ExpressionSentences.OperandNotTaken(Gate, "+", "input.kind.value", "string {narrative, table}", "numbers"),
        ExpressionSentences.ComparesTwoTypes(Gate, "input.age.value", "number", "input.kind.value", "string {narrative, table}"),
        ExpressionSentences.Mixes(Gate, "number", "string", "the branches of an if"),
        ExpressionSentences.NotARule(Gate, 2),
        ExpressionSentences.VarNotText(Gate, "a number"),
        ExpressionSentences.NotAPath(Gate, "input..value"),
        ExpressionSentences.ObjectOperand(Gate, "a list"),
        ExpressionSentences.ObjectWithoutFormat(Gate),
        ExpressionSentences.BuildsFieldAs(Total, "{ a: number }", "a", "string", "number"),
        ExpressionSentences.LiteralNotText(Gate, "matches", "pattern", "a number"),
        ExpressionSentences.RunDependentDecision(Gate, "date"),
        ExpressionSentences.RunDependentValue("total", "run.id", "nodes/total.md"),
    ];

    private static IEnumerable<string> Ids(string prefix, int from, int to) =>
        Enumerable.Range(from, to - from + 1).Select(n => $"{prefix}-{n:000}");

    [Fact]
    public void EverySentence_IsItsRow()
    {
        var every = Every().ToList();

        Assert.All(every, V31Contract.AssertSentence);
        Assert.All(every, r => Assert.Equal(r.Id == "S03-020" || r.Id == "S03-021" ? "deliverables/note.md"
            : r.Id == "S03-022" ? "macros/footer.md"
            : r.Id is "S03-026" or "S03-027" or "S03-028" or "S03-038" or "A04-009" ? "nodes/total.md" : "nodes/gate.md", r.Path));
        Assert.Equal(
            Ids("S03", 2, 35).Concat(["S03-037", "S03-038", "S03-042", "S03-043"]).Concat(Ids("A04", 1, 10)).Order(StringComparer.Ordinal),
            every.Select(r => r.Id).Order(StringComparer.Ordinal));
    }

    public static TheoryData<string, string> Faults => new()
    {
        { """{"==":[1,1]}""", "S03-004" },
        { """{"!=":[1,1]}""", "S03-005" },
        { """{"map":[[1],1]}""", "S03-006" },
        { """{"substr":["a",1]}""", "S03-007" },
        { """{"<":[1,2,3]}""", "S03-010" },
        { """{"!":true}""", "S03-011" },
        { """{"var":["input.a.value",0]}""", "S03-012" },
        { """{"var":"node.rows.value.0"}""", "S03-013" },
        { """{"matches":[{"var":"a.b"},{"var":"c.d"}]}""", "S03-029" },
        { """{"count_pass":[{"var":"a.b"},-1,"x"]}""", "S03-030" },
        { """{"matches":[{"var":"a.b"},"\\d"]}""", "S03-031" },
        { $$"""{"matches":[{"var":"a.b"},"{{new string('a', 201)}}"]}""", "S03-032" },
        { """{"has_word":[{"var":"a.b"},"  "]}""", "S03-033" },
        { """{"if":[true,1]}""", "S03-034" },
        { """{"matches":[{"var":"a.b"},"a{101}"]}""", "S03-042" },
        { """{"matches":[{"var":"a.b"},"(a{100}){100}"]}""", "S03-043" },
        { """{"a":1,"b":2}""", "A04-004" },
        { """{"var":5}""", "A04-005" },
        { """{"var":"input..value"}""", "A04-006" },
        { """{"object":[1]}""", "A04-007" },
        { """{"missing":[5]}""", "A04-010" },
    };

    [Theory]
    [MemberData(nameof(Faults))]
    public void EachShapeFault_IsItsSentence(string json, string id)
    {
        var fault = Assert.Single(JsonLogicShape.Check(LogicFixture.Rule(json)));

        var refusal = ExpressionSentences.Shape(Gate, fault);

        Assert.Equal(id, refusal.Id);
        Assert.Equal("nodes/gate.md", refusal.Path);
        V31Contract.AssertSentence(refusal);
    }

    [Fact]
    public void ARuleTooDeep_IsTheJsonDepthSentence()
    {
        JsonNode node = JsonValue.Create(true);
        for (var i = 0; i < 80; i++)
            node = new JsonObject { ["!"] = new JsonArray(node) };
        var fault = Assert.Single(JsonLogicShape.Check(node));

        var refusal = ExpressionSentences.Shape(Gate, fault, node);

        Assert.Equal("S01-041", refusal.Id);
        V31Contract.AssertSentence(refusal);
        Assert.Equal(
            "File 'nodes/gate.md' nests JSON more than 32 levels deep at " + string.Concat(Enumerable.Repeat("/!/0", 16)) + "; the most is SourceBounds.jsonDepth (32).",
            refusal.Sentence);
    }

    public static TheoryData<string, string> ExactFaults => new()
    {
        { """{"==":[{"var":"input.nope.value"},1]}""", "Node 'gate' uses ==; equality is written === (shown as =)." },
        { """{"<":[1,2,3]}""", "Node 'gate' gives < 3 operands; it takes 2." },
        { """{"map":[[1],1]}""", "Node 'gate' uses map, which is not a JSON Logic operation of this format (var, missing, if, ===, !==, <, <=, >, >=, and, or, !, in, cat, +, -, *, /, %, min, max, size, blank, words, lines, lower, trim, starts_with, ends_with, plain, object, matches, has_word, terms_subset, all_pass, count_pass, days_between, years_between, add_days)." },
        { """{"matches":[{"var":"input.consult_draft.content"},"\\d"]}""", "Node 'gate' pattern \"\\d\" is not I-Regexp (RFC 9485): '\\d' at character 1 is not an escape of I-Regexp." },
        { """{"matches":[{"var":"input.consult_draft.content"},{"var":"input.kind.value"}]}""", "Node 'gate' gives matches a computed pattern; pattern must be written as a literal." },
    };

    [Theory]
    [MemberData(nameof(ExactFaults))]
    public void Exact(string json, string sentence)
    {
        var fault = Assert.Single(JsonLogicShape.Check(LogicFixture.Rule(json)));

        Assert.Equal(sentence, ExpressionSentences.Shape(Gate, fault).Sentence);
    }

    [Fact]
    public void RunTime()
    {
        Assert.Equal(
            "Node 'gate' if could not be decided: matches was given 1.1 MiB of text; the most is 1 MiB.",
            ExpressionSentences.RunTime("Node 'gate'", "if", new JsonLogicFailure(JsonLogicFailureKind.MatchesTextTooLong, "1.1")));
        Assert.Equal(
            "Node 'gate' if yields a number; a condition yields a boolean.",
            ExpressionSentences.RunTime("Node 'gate'", "if", new JsonLogicFailure(JsonLogicFailureKind.NotABoolean, "a number")));
        Assert.Equal(
            "Node 'gate' if could not be evaluated: it uses map, which is not an operation of this format.",
            ExpressionSentences.RunTime("Node 'gate'", "if", new JsonLogicFailure(JsonLogicFailureKind.Declined, "it uses map, which is not an operation of this format")));
        Assert.Equal(
            "Node 'gate' while could not be evaluated: the evaluator failed.",
            ExpressionSentences.RunTime("Node 'gate'", "while", new JsonLogicFailure(JsonLogicFailureKind.Fault, "the evaluator failed")));
    }

    [Fact]
    public void RunTime_HoldsToItsRows()
    {
        var cases = new[]
        {
            ("S03-040", new JsonLogicFailure(JsonLogicFailureKind.MatchesTextTooLong, "1.1")),
            ("S03-041", new JsonLogicFailure(JsonLogicFailureKind.NotABoolean, "text")),
            ("A04-011", new JsonLogicFailure(JsonLogicFailureKind.Declined, "it is not a rule of this format")),
            ("A04-011", new JsonLogicFailure(JsonLogicFailureKind.Fault, "the evaluator failed")),
        };

        foreach (var (id, failure) in cases)
            V31Contract.AssertSentence(new SourceRefusal(id, ExpressionSentences.RunTime("Computed node 'total'", "expression", failure), ""));
    }
}
