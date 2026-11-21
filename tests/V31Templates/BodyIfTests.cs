using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31.Templates;
using static Consultologist.Api.Tests.V31Templates.TemplateFixture;

namespace Consultologist.Api.Tests.V31Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 2.5; part 06 § 6.2; R2): a body <c>if</c> tests only recorded facts (a loop
/// position, a state compared with a literal of the closed set, whether a check passed), and a chain branching on a
/// state handles every state or ends with an <c>else</c>; a renderer's body may test its own <c>value</c>.
/// </summary>
public class BodyIfTests
{
    private const string Example =
        "{{~ for s in node.sections.items ~}}\n"
        + "{{~ if s.state == \"completed\" ~}}\n"
        + "{{ s.value }}\n"
        + "{{~ else if s.state == \"skipped\" ~}}\n"
        + "{{~ else ~}}\n"
        + "_{{ s.reason }}_\n"
        + "{{~ end ~}}\n"
        + "{{~ end ~}}\n";

    private static Dictionary<string, byte[]> WithSections(string deliverable) =>
        Package().WithCheck()
            .WithLoop("sections", "node.plan.value.sections", "outline", "Outline {{ item.value.title }}.\n")
            .WithDeliverable(deliverable);

    [Fact]
    public void TheSpecsExample_IsValid()
    {
        var check = Check(WithSections(Example));

        Assert.True(check.Refusals.Count == 0, Said(check.Refusals));
    }

    [Theory]
    [InlineData("{{ for s in node.sections.items }}{{ if for.first }}First: {{ end }}{{ s.content }}{{ end }}")]
    [InlineData("{{ for s in node.sections.items }}{{ s.content }}{{ if !for.last }}, {{ end }}{{ end }}")]
    [InlineData("{{ for s in node.sections.items }}{{ if for.index == 0 }}First: {{ end }}{{ end }}")]
    [InlineData("{{ if check.coverage.value.pass }}Covered.{{ else }}Not covered.{{ end }}")]
    [InlineData("{{ if !check.coverage.value.pass && node.sections.state == \"completed\" }}Check it.{{ else }}{{ end }}")]
    [InlineData("{{ for s in node.sections.items }}{{ if s.state != \"skipped\" }}{{ s.content }}{{ else }}{{ end }}{{ end }}")]
    [InlineData("{{ for s in node.sections.items }}{{ if s.state == \"completed\" }}a{{ else if s.state == \"skipped\" }}b{{ else if s.state == \"not_run\" }}c{{ end }}{{ end }}")]
    public void ARecordedFact_MayBeTested(string body)
    {
        var check = Check(WithSections(body + "\n"));

        Assert.True(check.Refusals.Count == 0, Said(check.Refusals));
    }

    [Theory]
    [InlineData("{{ if input.consult_draft.content }}x{{ end }}", "input.consult_draft.content")]
    [InlineData("{{ if node.plan.value.sections | array.size }}x{{ end }}", "node.plan.value.sections | array.size")]
    [InlineData("{{ for s in node.plan.value.sections }}{{ if s.kind == \"table\" }}x{{ end }}{{ end }}", "s.kind == \"table\"")]
    [InlineData("{{ for s in node.sections.items }}{{ if for.index }}x{{ end }}{{ end }}", "for.index")]
    [InlineData("{{ for s in node.sections.items }}{{ if s.reason == null }}x{{ end }}{{ end }}", "s.reason == null")]
    public void AnythingElse_IsRefused(string body, string test)
    {
        var refusal = TemplateFixture.Single(Check(WithSections("Note.\n" + body + "\n")));

        Assert.Equal("S05-007", refusal.Id);
        Assert.StartsWith($"Deliverable 'note' tests {test} in a body if at line 2;", refusal.Sentence);
    }

    [Fact]
    public void AStateOutsideTheClosedSet_IsRefused()
    {
        var body = "{{ for s in node.sections.items }}\n{{ if s.state == \"done\" }}x{{ else }}y{{ end }}\n{{ end }}\n";

        var refusal = TemplateFixture.Single(Check(WithSections(body)));

        Assert.Equal("S05-010", refusal.Id);
        Assert.Equal("Deliverable 'note' compares s.state with \"done\" at line 2; a state is completed, skipped or not_run.", refusal.Sentence);
    }

    [Fact]
    public void TwoStatesAndNoElse_AreRefused()
    {
        var body = "{{ for s in node.sections.items }}{{ if s.state == \"completed\" }}{{ s.content }}{{ else if s.state == \"skipped\" }}{{ end }}{{ end }}\n";

        var refusal = TemplateFixture.Single(Check(WithSections(body)));

        Assert.Equal("S05-009", refusal.Id);
        Assert.Equal("Deliverable 'note' branches on s.state and handles completed and skipped, but not not_run: add it or an else.", refusal.Sentence);
    }

    [Theory]
    [InlineData("concept_bullets.md")]
    [InlineData("concept_context.md")]
    public void ARendererTestingItsValue_IsValid(string builtIn)
    {
        // A built-in renderer's body, as a package renderer: `value.concepts.size == 0`, `c.isSnomedConcept`, a pipe in a test.
        var text = File.ReadAllText(V31Contract.Path("renderers", builtIn));
        var body = text[(text.IndexOf("\n---\n", 4, StringComparison.Ordinal) + 5)..];
        var files = Package().WithRenderer("terms", "\"concept_list\"", body);

        var check = Check(files);

        Assert.True(check.Refusals.Count == 0, Said(check.Refusals));
    }

    [Fact]
    public void ARendererTestingTheSizeOfItsList_IsValid()
    {
        var files = Package().WithRenderer("terms", "\"concept_list\"", "{{~ if value.concepts.size == 0 ~}}\n(none)\n{{~ else ~}}\nSome.\n{{~ end ~}}\n");

        var body = Body(Check(files), "renderers/terms.md");

        Assert.Equal("{\"type\":\"integer\"}", Assert.Single(body.Reads).Format!.ToJsonString());
    }
}
