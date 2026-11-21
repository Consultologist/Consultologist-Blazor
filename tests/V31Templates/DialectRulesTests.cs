using System.Text;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Templates;

namespace Consultologist.Api.Tests.V31Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 2.1, 2.2, 2.3, 2.6, 5.2; R2): the statement-level rules
/// <see cref="TemplateSyntax.Parse"/> finds from the syntax alone, each a <see cref="DialectFault"/> whose
/// sentence (<see cref="DialectSentences.Of"/>) is its row, word for word.
/// </summary>
public class DialectRulesTests
{
    private const string Subject = "Prompt of node 'draft'";
    private const string File = "nodes/draft.md";

    /// <summary>One refused form of § 2.1 and § 2.2 (and § 2.6's <c>-</c>, § 5.2's call form) per row, on line 2 of its body; fills joined by <c>§</c>.</summary>
    public static TheoryData<string, string, int, string> Refused => new()
    {
        { "{{ x = 1 }}", "S05-001", 2, "=" },
        { "{{ x += 1 }}", "S05-001", 2, "+=" },
        { "{{ capture c }}x{{ end }}", "S05-001", 2, "capture" },
        { "{{ func f }}x{{ end }}", "S05-001", 2, "func" },
        { "{{ include \"a\" }}", "S05-001", 2, "include" },
        { "{{ import node }}", "S05-001", 2, "import" },
        { "{{ with node }}x{{ end }}", "S05-001", 2, "with" },
        { "{{ wrap node.a.content }}x{{ end }}", "S05-001", 2, "wrap" },
        { "{{ while for.first }}x{{ end }}", "S05-001", 2, "while" },
        { "{{ for s in node.a.items }}{{ break }}{{ end }}", "S05-001", 2, "break" },
        { "{{ for s in node.a.items }}{{ continue }}{{ end }}", "S05-001", 2, "continue" },
        { "{{ case node.a.value }}{{ when \"b\" }}x{{ end }}", "S05-001", 2, "case" },
        { "{{ tablerow s in node.a.items }}x{{ end }}", "S05-001", 2, "tablerow" },
        { "{{ node.a.value + 1 }}", "S05-001", 2, "+" },
        { "{{ node.a.value - 1 }}", "S05-001", 2, "-" },
        { "{{ node.a.value * 2 }}", "S05-001", 2, "*" },
        { "{{ node.a.value / 2 }}", "S05-001", 2, "/" },
        { "{{ node.a.value % 2 }}", "S05-001", 2, "%" },
        { "{{ node.a.value // 2 }}", "S05-001", 2, "//" },
        { "{{ \"Dr \" + node.a.content }}", "S05-001", 2, "+" },
        { "{{ for i in 1..3 }}x{{ end }}", "S05-006", 2, "1..3" },
        { "{{ for i in [\"a\", \"b\"] }}x{{ end }}", "S05-006", 2, "[\"a\", \"b\"]" },
        { "{{ for i in { a: 1 } }}x{{ end }}", "S05-006", 2, "{ a: 1 }" },
        { "{{ for i in node.a.items | array.size }}x{{ end }}", "S05-006", 2, "node.a.items | array.size" },
        { "{{ for i in (node.a.value | renderer.concept_bullets) }}x{{ end }}", "S05-006", 2, "(node.a.value | renderer.concept_bullets)" },
        { "{{ for s in node.a.items limit:2 }}x{{ end }}", "A07-004", 2, "limit:" },
        { "{{ for s in node.a.items offset:1 }}x{{ end }}", "A07-004", 2, "offset:" },
        { "{{ for s in node.a.items reversed }}x{{ end }}", "A07-004", 2, "reversed" },
        { "{{ for s in node.a.items }}x{{ else }}y{{ end }}", "A07-004", 2, "else" },
        { "{{ for s in node.a.items }}{{ for.rindex }}{{ end }}", "A07-004", 2, "for.rindex" },
        { "{{ for s in node.a.items }}{{ for.length }}{{ end }}", "A07-004", 2, "for.length" },
        { "{{ for s in node.a.items }}{{ if for.changed }}x{{ end }}{{ end }}", "A07-004", 2, "for.changed" },
        { "{{ node.plan.value.sections[2] }}", "S05-063", 2, "node.plan.value.sections§2" },
        { "{{ renderer.concept_bullets(node.x.value) }}", "S05-030", 2, "concept_bullets§node.x.value" },
        { "{{ node.a.content | string.replace \"a\" \"b\" }}", "S05-002", 2, "string.replace" },
        { "{{ date.now }}", "S05-002", 2, "date.now" },
        { "{{ math.random 1 10 }}", "S05-002", 2, "math.random" },
        { "{{ string.upcase node.a.content }}", "S05-002", 2, "string.upcase" },
        { "{{ node.a.content | string.truncate 10 }}", "S05-003", 2, "" },
        { "{{ node.a.content | object.default \"none\" }}", "S05-004", 2, "" },
        { "{{ node.d.value | date.to_string }}", "S05-005", 2, "" },
        { "{{ node.d.value | date.to_string node.p.content }}", "S05-005", 2, "" },
        { "{{- node.a.content }}", "S05-019", 2, "" },
        { "{{ if for.first -}}x{{ end }}", "S05-019", 2, "" },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void ARefusedForm_IsOneFault_WithItsSentence(string tag, string id, int line, string fills)
    {
        var syntax = TemplateSyntax.Parse("Write for Margaret Okafor.\n" + tag + "\n");

        var fault = Assert.Single(syntax.Faults);
        Assert.Equal(id, fault.Id);
        Assert.Equal(line, fault.Line);
        Assert.Equal(fills.Length == 0 ? [] : fills.Split('§'), fault.Fill);

        var refusal = DialectSentences.Of(fault, Subject, File);
        Assert.Equal(id, refusal.Id);
        Assert.Equal(File, refusal.Path);
        Assert.StartsWith(Subject + " ", refusal.Sentence, StringComparison.Ordinal);
        V31Contract.AssertSentence(refusal);
    }

    /// <summary>Every allowed form of § 2.1 and § 2.2 (R2: a pipe to an allowed built-in inside an if).</summary>
    public static TheoryData<string> Allowed => new()
    {
        "{{ node.plan.value.sections }}",
        "{{ input.consult_draft.content }}",
        "{{ for s in node.a.items }}{{ s.value }}{{ end }}",
        "{{ for s in node.a.items }}{{ if for.first || for.last || for.even || for.odd }}{{ for.index }}{{ end }}{{ end }}",
        "{{ if s.state == \"completed\" }}a{{ else if s.state != \"skipped\" }}b{{ else }}c{{ end }}",
        "{{ if check.coverage.value.pass && !(for.first) }}a{{ end }}",
        "{{ node.a.content | string.upcase }}",
        "{{ node.a.content | string.downcase }}",
        "{{ node.a.content | string.capitalize }}",
        "{{ node.a.content | string.strip }}",
        "{{ node.a.value | array.join \", \" }}",
        "{{ node.a.value | array.size }}",
        "{{ node.d.value | date.to_string \"%d %B %Y\" }}",
        "{{ node.a.value | renderer.concept_bullets }}",
        "{{ node.a.value | renderer.concept_list | renderer.bullets }}",
        "{{ run.date | renderer.long_date }}",
        "{{ if (c.support | string.strip) != \"\" }}x{{ end }}",
        "{{ if value.concepts.size == 0 }}(none){{ end }}",
        "{{ if c.support != null }}x{{ end }}",
        "{{# a note for Dr A. Sample }}",
        "{%{ {{ node.x.content }} }%}",
        "{{~ if for.first ~}}\nx\n{{~ end ~}}",
        "{{~ snippet.standards.content }}",
        "diabetes{{ part.history }}.",
    };

    [Theory]
    [MemberData(nameof(Allowed))]
    public void AnAllowedForm_GivesNoFault(string body)
    {
        var syntax = TemplateSyntax.Parse("Write for Margaret Okafor.\n" + body + "\n");

        Assert.Empty(syntax.Faults);
    }

    [Fact]
    public void NestingOfFifteen_IsAllowed_SixteenIsNot()
    {
        static string Nest(int depth) =>
            string.Concat(Enumerable.Range(0, depth).Select(i => i % 2 == 0 ? "{{ for s in node.a.items }}\n" : "{{ if for.first }}\n"))
            + "x\n"
            + string.Concat(Enumerable.Repeat("{{ end }}\n", depth));

        Assert.Empty(TemplateSyntax.Parse(Nest(15)).Faults);

        var fault = Assert.Single(TemplateSyntax.Parse(Nest(16)).Faults);
        Assert.Equal("S05-013", fault.Id);
        Assert.Equal(16, fault.Line);
        Assert.Equal(["16"], fault.Fill);
        V31Contract.AssertSentence(DialectSentences.Of(fault, Subject, File));

        var deeper = Assert.Single(TemplateSyntax.Parse(Nest(18)).Faults);
        Assert.Equal(["18"], deeper.Fill);
        Assert.Equal(18, deeper.Line);

        // Far past the limit but within the walk's bound, on a 1 MB stack: still the nesting sentence.
        var far = Assert.Single(ParseOnASmallStack(Nest(100)).Faults);
        Assert.Equal("S05-013", far.Id);
        Assert.Equal(["100"], far.Fill);
    }

    /// <summary>
    /// <see cref="TemplateSyntax.Parse"/> on a thread with a 1 MB stack, so a walk that recurses without bound fails
    /// here rather than passing on the test runner's larger main stack.
    /// </summary>
    internal static TemplateSyntax ParseOnASmallStack(string body)
    {
        TemplateSyntax? syntax = null;
        var thread = new Thread(() => syntax = TemplateSyntax.Parse(body), 1024 * 1024);
        thread.Start();
        thread.Join();
        return syntax!;
    }

    [Fact]
    public void AnElseIf_IsNoDeeper()
    {
        var chain = "{{ if for.first }}a" + string.Concat(Enumerable.Repeat("{{ else if for.last }}b", 20)) + "{{ else }}c{{ end }}";

        Assert.Empty(TemplateSyntax.Parse(chain).Faults);
    }

    /// <summary>
    /// Text Scriban can't read, and a body never closed, is one A07-005 at the first error's line (line 0: no fault, the
    /// text is no template at all); nothing throws.
    /// </summary>
    public static TheoryData<string, int> Broken => new()
    {
        { "Summary\n{{ node.x.content", 2 },
        { "Summary\n{{ if for.first }}\nx", 2 },
        { "{{", 1 },
        { new string('{', 100_000), 1 },
        { "", 0 },
        { "}}", 0 },
        { "{{ node.a.value | }}", 1 },
        { "a\nb\n{{ end }}", 3 },
        { "a }} b {%{ c", 0 },
        { "{{ node" + string.Concat(Enumerable.Repeat(".a", 100_000)) + " }}", 1 },
        { "Summary\n{{ node.a" + string.Concat(Enumerable.Repeat("[0]", 100_000)) + " }}", 2 },
        { "{{ (node.a)" + string.Concat(Enumerable.Repeat(".b", 100_000)) + " }}", 1 },
        { "{{ if for.first }}{{ node" + string.Concat(Enumerable.Repeat(".a", 100_000)) + " }}{{ end }}", 1 },
    };

    [Theory]
    [MemberData(nameof(Broken))]
    public void BrokenTemplates_NeverThrow(string body, int line)
    {
        var syntax = ParseOnASmallStack(body);

        if (line == 0)
        {
            Assert.Empty(syntax.Faults);
            Assert.Empty(syntax.Tags);
            Assert.Empty(syntax.Outputs);
            return;
        }

        var fault = Assert.Single(syntax.Faults);
        Assert.Equal("A07-005", fault.Id);
        Assert.Equal(line, fault.Line);
        Assert.Empty(fault.Fill);
        V31Contract.AssertSentence(DialectSentences.Of(fault, Subject, File));
    }

    /// <summary>Every body of the resolver's fixture packages (brief step 4): none breaks a rule this step finds.</summary>
    [Fact]
    public void EveryResolveFixtureBody_HasNoFault()
    {
        var packages = new[] { SourceFixture.Minimal(), ResolveFixture.NestedNote(), ResolveFixture.Conditional(), ResolveFixture.Functions() };
        var bodies = packages
            .SelectMany(p => p)
            .Where(f => f.Key.EndsWith(".md", StringComparison.Ordinal))
            .Select(f => (f.Key, Body: FrontMatter.Split(Encoding.UTF8.GetString(f.Value).ReplaceLineEndings("\n"))?.Body))
            .ToList();

        Assert.NotEmpty(bodies);
        Assert.All(bodies, b =>
        {
            Assert.NotNull(b.Body);
            Assert.True(TemplateSyntax.Parse(b.Body).Faults.Count == 0, b.Key);
        });
    }

    [Fact]
    public void EachFault_IsInSourceOrder()
    {
        var syntax = TemplateSyntax.Parse("{{ node.a.value[0] }}\n{{- x = 1 }}\n{{ node.a.content | string.truncate 3 }}");

        Assert.Equal(["S05-063", "S05-019", "S05-001", "S05-003"], syntax.Faults.Select(f => f.Id));
        Assert.Equal([1, 2, 2, 3], syntax.Faults.Select(f => f.Line));
    }

    [Theory]
    [InlineData("UsesConstruct", "S05-001")]
    [InlineData("CallsBuiltIn", "S05-002")]
    [InlineData("CallsTruncate", "S05-003")]
    [InlineData("CallsObjectDefault", "S05-004")]
    [InlineData("DateWithoutPattern", "S05-005")]
    [InlineData("ForOverNotAList", "S05-006")]
    [InlineData("NestsTooDeep", "S05-013")]
    [InlineData("UsesDash", "S05-019")]
    [InlineData("CallsRenderer", "S05-030")]
    [InlineData("ReadsPosition", "S05-063")]
    [InlineData("ForForm", "A07-004")]
    [InlineData("CannotBeRead", "A07-005")]
    [InlineData("TestsInBodyIf", "S05-007")]
    [InlineData("BranchesWithoutEveryState", "S05-009")]
    [InlineData("ComparesStateWith", "S05-010")]
    [InlineData("ReadsWhatIsNotThere", "S05-011")]
    [InlineData("PlacesWithTildeRight", "S05-015")]
    [InlineData("PlacesWithDash", "S05-016")]
    [InlineData("PlacesBlockInsideALine", "S05-017")]
    [InlineData("TextAfterBlock", "S05-018")]
    [InlineData("ReadsRoot", "S05-020")]
    [InlineData("ReadsProfileInPrompt", "S05-021")]
    [InlineData("ReadsRunWithMore", "S05-024")]
    [InlineData("ReadsRunDependentWithMore", "S05-025")]
    [InlineData("ReadsBareSnippet", "S05-037")]
    [InlineData("ReadsNoProfileField", "S06-028")]
    [InlineData("WritesAnObjectOrList", "A07-007")]
    [InlineData("ReadsBare", "A07-008")]
    [InlineData("PlacesInsideAnIfOrFor", "A07-009")]
    [InlineData("ReadsRunFact", "S05-060")]
    public void EachSentenceMethod_SaysItsRow(string method, string id)
    {
        var info = typeof(DialectSentences).GetMethod(method)!;
        var arguments = info.GetParameters()
            .Select(p => p.ParameterType == typeof(int) ? (object)7
                : p.ParameterType == typeof(IReadOnlyList<string>) ? new[] { "x", "y" }
                : p.Name == "subject" ? Subject : p.Name == "path" ? File : "x")
            .ToArray();

        var refusal = (PackageFormat.V31.SourceRefusal)info.Invoke(null, arguments)!;

        Assert.Equal(id, refusal.Id);
        V31Contract.AssertSentence(refusal);
    }
}
