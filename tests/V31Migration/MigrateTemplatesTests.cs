using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Migration;
using Consultologist.PackageFormat.V31.Templates;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 1.2 the template rows, § 1.4; part 05 §§ 2, 3, 6, 7; R6): the templates pass
/// over whole packages. Every prompt and <c>rendered</c> body, snippet body, folder item's body and macro body of the
/// sixteen packages is in the 31 dialect, or carries a hand edit naming its file; an agent's lifted <c>if</c>s are its
/// <c>parts</c>; a macro body is kept for the macros pass.
/// </summary>
public class MigrateTemplatesTests
{
    /// <summary><paramref name="package"/>'s working set after the templates pass.</summary>
    private static Migrated Templated(string package)
    {
        var m = TemplateRewriterTests.Working(package);
        Migrate.Templates(m);
        return m;
    }

    [Fact]
    public void AnAgentsParts_AreItsLiftedIfs_OrNull()
    {
        var result = MigrateNodesTests.Migrated("example-conditional-documents");

        var (header, _) = MigrateNodesTests.Md(result, "nodes/sections_consult_note_section/consult_note_section.md");
        Assert.Equal(["when_1"], header["parts"]!.AsObject().Select(p => p.Key));

        // A prompt with no lifted if keeps parts: null; the member order is SourceShapes'.
        var (plain, _) = MigrateNodesTests.Md(MigrateNodesTests.Migrated("example-macros"), "nodes/summary.md");
        Assert.Null(plain["parts"]);
        Assert.Equal(
            ["kind", "producer", "label", "description", "format", "reproducible", "modelDerived", "runDependent", "profileCarrying",
             "if", "onInvalid", "failIfEmpty", "parts", "ordered"],
            header.Select(m => m.Key));
    }

    [Fact]
    public void SnippetBodies_AndFolderItems_AreRewritten()
    {
        var m = Templated("example-macros");

        // style.md places a macro: 30's .prompt is the placement.
        Assert.Equal(
            "Write in the first person plural, past tense, without abbreviations the patient would not know.\n{{ macro.house_style }}\n",
            m.Sources["snippets/style.md"].Body);

        // test/general's assessment_plan item tests an input in a body if, which a snippet's body cannot lift (A10-035).
        var general = Templated("test/general");
        Assert.EndsWith("{{ if input.note_type.value == \"consult\" }}\n{{ macro.letrozole_prescription_text }}\n{{ end }}", general.Sources["snippets/standards/assessment_plan.md"].Body, StringComparison.Ordinal);
        var edit = Assert.Single(general.HandEditNotices, h => h.Contains("snippets/standards/assessment_plan.md", StringComparison.Ordinal));
        V31Contract.AssertSentence(new SourceRefusal("S10-002", edit, "snippets/standards/assessment_plan.md"));
        Assert.EndsWith(": the body's if at line 5 decides what a snippet body shows, which has no parts; write each text as a snippet with its own if, placed where the text was.", edit, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(MigrateLayoutTests.Packages), MemberType = typeof(MigrateLayoutTests))]
    public void EveryMacroBody_IsKeptForTheMacrosPass(string package)
    {
        var m = Templated(package);

        var macros = m.Manifest30.Macros ?? [];
        foreach (var macro in macros.Where(x => !x.File.EndsWith('/')))
            Assert.True(m.MacroBodies.ContainsKey(macro.File), $"{macro.File} has no rewritten body.");
        Assert.All(m.MacroBodies.Keys, path => Assert.StartsWith("macros/", path, StringComparison.Ordinal));
        if (package == "example-macros")
            Assert.Contains("macros/notes/red_flags.md", m.MacroBodies.Keys);
    }

    [Theory]
    [MemberData(nameof(MigrateLayoutTests.Packages), MemberType = typeof(MigrateLayoutTests))]
    public void EveryVendoredPrompt_RewritesOrSaysWhy(string package)
    {
        var m = Templated(package);

        var bodies = new List<(string File, string Text)>();
        foreach (var (path, file) in m.Sources)
        {
            if (file.Body is not { } body || !(path.StartsWith("nodes/", StringComparison.Ordinal) || path.StartsWith("snippets/", StringComparison.Ordinal)))
                continue;
            if (path.StartsWith("nodes/", StringComparison.Ordinal) && (string?)file.Json["kind"] is not ("agent" or "rendered"))
                continue;
            bodies.Add((path, body));
            foreach (var (_, part) in file.Json["parts"] as JsonObject ?? [])
                bodies.Add((path, string.Join('\n', part!["text"]!.AsArray().Select(l => (string)l!))));
        }

        bodies.AddRange(m.MacroBodies.Select(b => (b.Key, b.Value)));
        Assert.NotEmpty(bodies);

        foreach (var (file, text) in bodies)
        {
            var problems = Problems(text);
            if (problems.Count > 0)
            {
                Assert.True(
                    m.HandEditNotices.Any(h => h.StartsWith($"Hand edit needed in {file}, ", StringComparison.Ordinal)),
                    $"{file} breaks the 31 dialect with no hand edit: {string.Join("; ", problems)}\n{text}");
            }
        }

        Assert.All(m.HandEditNotices, h => V31Contract.AssertSentence(new SourceRefusal("S10-002", h, "")));
    }

    [Fact]
    public void TheVendoredBodies_ThatNeedNoEdit_PassTheDialect()
    {
        // The check above is not vacuous: most bodies need no edit and pass.
        var m = Templated("example-two-documents");
        var file = m.Sources["nodes/extract_patient_concepts.md"].Body!;
        Assert.Empty(Problems(file));
        Assert.Contains("Bare node", string.Join(";", Problems("{{ node.extract_patient_concepts }}")), StringComparison.Ordinal);
        Assert.Contains("placement", string.Join(";", Problems("{{ if for.first }}{{ macro.x }}{{ end }}")), StringComparison.Ordinal);
        Assert.Contains("body if", string.Join(";", Problems("{{ if input.x.value }}a{{ end }}")), StringComparison.Ordinal);
    }

    /// <summary>
    /// What the dialect refuses in <paramref name="text"/> by its form alone: a statement fault (<see cref="TemplateSyntax"/>),
    /// a placement inside an <c>if</c> or a <c>for</c> (A07-009), a body <c>if</c> testing something other than a recorded
    /// fact (S05-007), a bare <c>node.x</c> or <c>input.x</c> (A07-008), 30's <c>.prompt</c> and its renderer names.
    /// </summary>
    private static List<string> Problems(string text)
    {
        var syntax = TemplateSyntax.Parse(text);
        var problems = syntax.Faults.Select(f => $"{f.Id} at line {f.Line}").ToList();
        foreach (var output in syntax.Outputs)
        {
            if (output.Depth > 0 && output.Path is { } path && output.Pipes.Count == 0
                && (path.Split('.') is ["part", _] or ["macro", _] or ["macro", _, not "content"] or ["snippet", _, "content"] or ["snippet", _, _, "content"]))
                problems.Add($"placement {path} inside {string.Join(" ", output.Enclosing)}");
            foreach (var read in output.Paths)
            {
                if (read.Split('.') is ["node" or "input", _])
                    problems.Add($"Bare node or input {read}");
                if (read.EndsWith(".prompt", StringComparison.Ordinal) && read.StartsWith("macro.", StringComparison.Ordinal))
                    problems.Add($"30's {read}");
            }

            if (output.Pipes.Any(p => p.Name is "concept_bullets" or "concept_context"))
                problems.Add("30's renderer pipe");
        }

        foreach (var chain in syntax.Ifs)
        {
            foreach (var test in chain.Tests.Where(t => DialectRules.TestForm(t) == BodyTest.Refused))
                problems.Add($"body if tests {test.Text}");
        }

        return problems;
    }
}
