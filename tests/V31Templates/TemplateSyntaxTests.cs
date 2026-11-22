using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31.Templates;

namespace Consultologist.Api.Tests.V31Templates;

/// <summary>#1504 (specVersion 31, area 07; part 05 §§ 2.1, 2.5, 2.6): <see cref="TemplateSyntax.Parse"/> reads one body: its tags, outputs, pipes, fors and ifs.</summary>
public class TemplateSyntaxTests
{
    [Fact]
    public void ATag_KnowsItsMarksAndItsLine()
    {
        var syntax = TemplateSyntax.Parse("a\n  {{~ snippet.x.content }}\nb {{ node.y.content -}} c");

        Assert.Equal(2, syntax.Tags.Count);
        var first = syntax.Tags[0];
        Assert.True(first.TildeLeft);
        Assert.False(first.TildeRight);
        Assert.False(first.DashLeft);
        Assert.False(first.DashRight);
        Assert.True(first.AloneOnLine);
        Assert.False(first.TextBefore);
        Assert.False(first.TextAfter);
        Assert.Equal(2, first.Line);
        Assert.Equal(3, first.Column);
        Assert.Equal(4, first.Start);
        Assert.Equal(28, first.End);

        var second = syntax.Tags[1];
        Assert.True(second.DashRight);
        Assert.False(second.DashLeft);
        Assert.False(second.TildeLeft);
        Assert.True(second.TextBefore);
        Assert.True(second.TextAfter);
        Assert.False(second.AloneOnLine);
        Assert.Equal(3, second.Line);
        Assert.Equal("{{ node.y.content -}}", syntax.Text[second.Start..second.End]);

        Assert.Equal(["snippet.x.content", "node.y.content"], syntax.Outputs.Select(o => o.Path));
        Assert.Same(first, syntax.Outputs[0].Tag);
    }

    [Fact]
    public void AnOutput_GivesItsPathAndPipes()
    {
        var syntax = TemplateSyntax.Parse("{{ node.e.value | renderer.list | renderer.bullets }}");

        var output = Assert.Single(syntax.Outputs);
        Assert.Equal("node.e.value", output.Path);
        Assert.Equal(["renderer.list", "renderer.bullets"], output.Pipes.Select(p => p.Name));
        Assert.All(output.Pipes, p => Assert.Empty(p.Arguments));
        Assert.All(output.Pipes, p => Assert.Equal(1, p.Line));
        Assert.Equal(["node.e.value"], output.Paths);
        Assert.Equal(0, output.Depth);
        Assert.Empty(output.Enclosing);
        Assert.Empty(syntax.Faults);
    }

    [Fact]
    public void APipe_KeepsItsArgumentsAsWritten()
    {
        var syntax = TemplateSyntax.Parse("{{ node.d.value | date.to_string \"%d %B %Y\" }} {{ node.l.value | array.join \", \" }}");

        Assert.Equal(["\"%d %B %Y\""], syntax.Outputs[0].Pipes.Single().Arguments);
        Assert.Equal(["\", \""], syntax.Outputs[1].Pipes.Single().Arguments);
        Assert.Empty(syntax.Faults);
    }

    [Fact]
    public void AForVariable_IsNamedWithItsSource()
    {
        var syntax = TemplateSyntax.Parse("{{ for s in node.sections.items }}\n{{ if for.first }}{{ s.value }}{{ end }}\n{{ end }}");

        var loop = Assert.Single(syntax.Fors);
        Assert.Equal("s", loop.Variable);
        Assert.Equal("node.sections.items", loop.Source);
        Assert.Equal(0, loop.Depth);
        Assert.Equal(1, loop.Tag.Line);

        var output = Assert.Single(syntax.Outputs);
        Assert.Equal("s.value", output.Path);
        Assert.Equal(2, output.Depth);
        Assert.Equal(["for", "if"], output.Enclosing);
        Assert.Equal(1, Assert.Single(syntax.Ifs).Depth);
        Assert.Empty(syntax.Faults);
    }

    [Fact]
    public void ACommentAndARawBlock_GiveNothing()
    {
        var syntax = TemplateSyntax.Parse("a {{# node.x.content }} b\n{{## a\nlonger note ##}}\n{%{ {{ node.y.content }} }%}\n");

        Assert.Empty(syntax.Tags);
        Assert.Empty(syntax.Outputs);
        Assert.Empty(syntax.Fors);
        Assert.Empty(syntax.Ifs);
        Assert.Empty(syntax.Faults);
    }

    [Fact]
    public void AnIfChain_GivesEachTest()
    {
        const string body = """
            {{~ for s in node.sections.items ~}}
            {{~ if s.state == "completed" ~}}
            {{ s.value }}
            {{~ else if s.state == "skipped" ~}}
            {{~ else ~}}
            _{{ s.reason }}_
            {{~ end ~}}
            {{~ end ~}}
            """;
        var syntax = TemplateSyntax.Parse(body);

        var chain = Assert.Single(syntax.Ifs);
        Assert.True(chain.HasElse);
        Assert.Equal(2, chain.Tag.Line);
        Assert.Equal(1, chain.Depth);
        Assert.Equal(["s.state == \"completed\"", "s.state == \"skipped\""], chain.Tests.Select(t => t.Text));
        Assert.All(chain.Tests, t => Assert.Equal("s.state", t.Path));
        Assert.All(chain.Tests, t => Assert.Equal("==", t.Operator));
        Assert.Equal(["\"completed\"", "\"skipped\""], chain.Tests.Select(t => t.Literal));
        Assert.Equal([2, 4], chain.Tests.Select(t => t.Line));
        Assert.Equal(["s.value", "s.reason"], syntax.Outputs.Select(o => o.Path));
        Assert.All(syntax.Outputs, o => Assert.Equal(["for", "if"], o.Enclosing));
        Assert.Empty(syntax.Faults);
    }

    [Fact]
    public void ATestCombination_IsSplitIntoItsTests()
    {
        var syntax = TemplateSyntax.Parse("{{ if !for.last && (check.coverage.value.pass || value.concepts.size == 0) }}x{{ end }}");

        var chain = Assert.Single(syntax.Ifs);
        Assert.False(chain.HasElse);
        Assert.Equal(["for.last", "check.coverage.value.pass", "value.concepts.size == 0"], chain.Tests.Select(t => t.Text));
        Assert.Equal(["for.last", "check.coverage.value.pass", "value.concepts.size"], chain.Tests.Select(t => t.Path));
        Assert.Equal([null, null, "=="], chain.Tests.Select(t => t.Operator));
        Assert.Equal([null, null, "0"], chain.Tests.Select(t => t.Literal));
    }

    [Fact]
    public void APipeInsideAnIf_IsRead()
    {
        var syntax = TemplateSyntax.Parse(RendererBody("concept_context"));

        Assert.Empty(syntax.Faults);
        var support = syntax.Ifs.SelectMany(i => i.Tests).Single(t => t.Text.Contains("string.strip", StringComparison.Ordinal));
        Assert.Equal("c.support", support.Path);
        Assert.Equal("!=", support.Operator);
        Assert.Equal("\"\"", support.Literal);
        Assert.Equal("string.strip", Assert.Single(support.Pipes).Name);
    }

    [Theory]
    [InlineData("concept_bullets")]
    [InlineData("concept_context")]
    public void EachBuiltInRendererBody_HasNoFault(string id)
    {
        var syntax = TemplateSyntax.Parse(RendererBody(id));

        Assert.Empty(syntax.Faults);
        Assert.Equal("value.concepts", Assert.Single(syntax.Fors).Source);
    }

    // ---- a fault of the engine (#1505 ruling 6, R14) ------------------------------------------------

    private const string Summary = "Summary for Margaret Okafor\n{{ node.plan.content }}\n";

    [Fact]
    public void AnUnexpectedParserException_IsAnEngineFault()
    {
        // The parser fails in a way Scriban never does for text: the fault is the engine's (A08-006), not the author's
        // (A07-005), and it carries the exception's type only, never its message or the body.
        var syntax = TemplateSyntax.Parse(Summary, text => throw new InvalidOperationException("Margaret Okafor: " + text));

        var fault = Assert.Single(syntax.Faults);
        Assert.Equal(("A08-006", 1), (fault.Id, fault.Line));
        Assert.Equal(["System.InvalidOperationException"], fault.Fill);
        Assert.Empty(syntax.Tags);
        Assert.Empty(syntax.Outputs);
        var refusal = DialectSentences.Of(fault, "Prompt of node 'plan'", "nodes/plan.md");
        V31Contract.AssertSentence(refusal);
        Assert.Equal("Prompt of node 'plan' couldn't be checked: the engine failed reading its template. "
            + "This is a fault in the engine, not in the package; report it.", refusal.Sentence);
        Assert.DoesNotContain("Margaret", refusal.Sentence, StringComparison.Ordinal);

        Assert.Equal("A08-006", Assert.Single(TemplateSyntax.Parse(Summary, _ => throw new NullReferenceException()).Faults).Id);
        Assert.Throws<OutOfMemoryException>(() => TemplateSyntax.Parse(Summary, _ => throw new OutOfMemoryException()));
    }

    [Fact]
    public void WhatScribanSaysOfUnreadableText_IsStillTheAuthors()
    {
        // Scriban's own exception for text it cannot read, and a stack too shallow for the text, are A07-005 at line 1.
        var scriban = Assert.Single(TemplateSyntax.Parse(Summary, _ => throw new Scriban.Syntax.ScriptRuntimeException(default, "unreadable")).Faults);
        Assert.Equal(("A07-005", 1), (scriban.Id, scriban.Line));
        Assert.Empty(scriban.Fill);
        var stack = Assert.Single(TemplateSyntax.Parse(Summary, _ => throw new InsufficientExecutionStackException()).Faults);
        Assert.Equal("A07-005", stack.Id);

        // Scriban's expected parse errors (messages, not exceptions) and the tree's depth limit are A07-005 as before.
        Assert.Equal("A07-005", Assert.Single(TemplateSyntax.Parse("{{ node.plan.value | }}").Faults).Id);
        Assert.Equal("A07-005", Assert.Single(TemplateSyntax.Parse("{{ node" + string.Concat(Enumerable.Repeat(".a", 100_000)) + " }}").Faults).Id);
    }

    /// <summary>A built-in renderer's body: everything after the second <c>---</c> line of <c>v31/renderers/&lt;id&gt;.md</c>.</summary>
    internal static string RendererBody(string id)
    {
        var text = File.ReadAllText(V31Contract.Path("renderers", id + ".md")).ReplaceLineEndings("\n");
        var first = text.IndexOf("---\n", StringComparison.Ordinal);
        var second = text.IndexOf("\n---\n", first + 4, StringComparison.Ordinal);
        return text[(second + "\n---\n".Length)..];
    }
}
