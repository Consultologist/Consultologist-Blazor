using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Migration;
using Consultologist.PackageFormat.V31.Templates;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 1.2 the template rows, § 1.4; part 05 §§ 2, 3, 6, 7; R6): the templates pass
/// over whole packages. Every prompt and <c>rendered</c> body, snippet body, folder item's body and macro body of the
/// fixture packages (#1508: the edge cases too) is in the 31 dialect, or carries a hand edit naming its file; an agent's lifted <c>if</c>s are its
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

        // test/general's assessment_plan item places a macro under a body if: the if is hoisted (R6), so the macro takes its
        // condition, the line ends between the tags staying as in a prompt. A folder item's body read in a loop places no
        // macro at 31 (part 05 § 6.2, A07-012), so the placement itself is the hand edit (A10-051): it moves to the prompt.
        var general = Templated("test/general");
        Assert.EndsWith("deferred.\n\n\n{{ macro.letrozole_prescription_text }}\n", general.Sources["snippets/standards/assessment_plan.md"].Body, StringComparison.Ordinal);
        Assert.Equal("""{"===":[{"var":"input.note_type.value"},"consult"]}""", general.PlacementRules["letrozole_prescription_text"].Logic.ToJsonString());
        var edit = Assert.Single(general.HandEditNotices, h => h.Contains("snippets/standards/assessment_plan.md", StringComparison.Ordinal));
        V31Contract.AssertSentence(new SourceRefusal("S10-002", edit, "snippets/standards/assessment_plan.md"));
        const string reason = "macro 'letrozole_prescription_text' is placed at line 5, but a snippet folder's item read in a loop places no macro at 31; place it in the prompt that loops over the folder.";
        Assert.Equal("Hand edit needed in snippets/standards/assessment_plan.md, body: " + reason, edit);
        V31Contract.AssertSentence(new SourceRefusal("A10-051", reason, ""));
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

        var bodies = new List<(string File, string Member, TemplateSite Site, string Text)>();
        foreach (var (path, file) in m.Sources)
        {
            if (file.Body is not { } body || !(path.StartsWith("nodes/", StringComparison.Ordinal) || path.StartsWith("snippets/", StringComparison.Ordinal)))
                continue;
            var site = path.StartsWith("snippets/", StringComparison.Ordinal) ? TemplateSite.SnippetBody
                : (string?)file.Json["kind"] switch { "agent" => TemplateSite.Prompt, "rendered" => TemplateSite.Rendered, _ => (TemplateSite?)null };
            if (site is not { } kind)
                continue;
            bodies.Add((path, "body", kind, body));
            foreach (var (id, part) in file.Json["parts"] as JsonObject ?? [])
                bodies.Add((path, $"parts.{id}.text", TemplateSite.Prompt, string.Join('\n', part!["text"]!.AsArray().Select(l => (string)l!))));
        }

        // A macro that becomes a snippet (D4) is written, and its hand edits said, at snippets/<id>.md.
        string Written(string path) =>
            m.Manifest30.Macros?.FirstOrDefault(x => x.File == path) is { } macro && Migrate.Renames.BecomesSnippet(m, macro.Id) ? $"snippets/{macro.Id}.md" : path;
        bodies.AddRange(m.MacroBodies.Select(b => (Written(b.Key), "body", TemplateSite.MacroBody, b.Value)));
        Assert.NotEmpty(bodies);

        foreach (var (file, member, site, text) in bodies)
        {
            // A hand edit excuses only the construct it names: one on this file and member whose reason is that rule's.
            var edits = m.HandEditNotices.Where(h => h.StartsWith($"Hand edit needed in {file}, {member}: ", StringComparison.Ordinal)).ToList();
            foreach (var (problem, excuses) in Problems(text, site))
            {
                Assert.True(
                    excuses.Any(e => edits.Any(h => h.Contains(e, StringComparison.Ordinal))),
                    $"{file}, {member} breaks the 31 dialect with no hand edit for it: {problem}\n{text}");
            }
        }

        Assert.All(m.HandEditNotices, h => V31Contract.AssertSentence(new SourceRefusal("S10-002", h, "")));
    }

    [Fact]
    public void TheVendoredBodies_ThatNeedNoEdit_PassTheDialect()
    {
        // The check above is not vacuous: most bodies need no edit and pass, and each rule it holds finds its construct.
        var m = Templated("example-two-documents");
        Assert.Empty(Problems(m.Sources["nodes/extract_patient_concepts.md"].Body!, TemplateSite.Prompt));
        string Said(string text, TemplateSite site = TemplateSite.Prompt) => string.Join(";", Problems(text, site).Select(p => p.Problem));
        Assert.Contains("bare node or input", Said("{{ node.extract_patient_concepts }}"), StringComparison.Ordinal);
        Assert.Contains("placement", Said("{{ if for.first }}{{ macro.x }}{{ end }}"), StringComparison.Ordinal);
        Assert.Contains("body if", Said("{{ if input.x.value == \"a\" }}a{{ end }}"), StringComparison.Ordinal);
        // #1508 (R17): a bare test is the guard A11-009 asks for, typed at publish, not here.
        Assert.Empty(Said("{{ if input.x.value }}a{{ end }}"));
        Assert.Contains("S05-009", Said("{{ for s in node.x.items }}{{ if s.state != \"skipped\" }}a{{ end }}{{ end }}"), StringComparison.Ordinal);
        Assert.Contains("S05-010", Said("{{ for s in node.x.items }}{{ if s.state == \"done\" }}a{{ else }}{{ end }}{{ end }}"), StringComparison.Ordinal);
        Assert.Contains("profile", Said("{{ profile.name }}"), StringComparison.Ordinal);
        Assert.Empty(Problems("{{ profile.name }}", TemplateSite.MacroBody));
        Assert.Contains("deliverable", Said("{{ deliverable.note.content }}"), StringComparison.Ordinal);
        Assert.Contains("S05-001", Said("{{ case input.x.value }}{{ when \"a\" }}a{{ end }}", TemplateSite.MacroBody), StringComparison.Ordinal);
    }

    /// <summary>
    /// What the dialect refuses in <paramref name="text"/> at <paramref name="site"/> by its form, each with the reasons of
    /// the hand edits that may excuse it: a statement fault (<see cref="TemplateSyntax"/>: A10-005, A10-038); a placement
    /// inside an <c>if</c> or a <c>for</c> (A07-009: A10-005's placement, or the <c>if</c>'s A10-001 or A10-035); a body
    /// <c>if</c> test outside § 2.5 (S05-007, <see cref="DialectRules.TestForm"/>), a state test that misses a state with no
    /// <c>else</c> (S05-009) or compares with a value outside the closed set (S05-010) (A10-001, A10-035); a bare
    /// <c>node.x</c> or <c>input.x</c> (A07-008: never excused); a prompt's <c>profile.*</c> (S10-004) or a prompt's or
    /// <c>rendered</c> body's <c>deliverable.*</c> (A10-039); 30's <c>.prompt</c> and renderer names (never excused).
    /// </summary>
    private static List<(string Problem, string[] Excuses)> Problems(string text, TemplateSite site)
    {
        string[] ifEdits = ["the body's if at line", "decides what a"];
        var syntax = TemplateSyntax.Parse(text);
        var problems = syntax.Faults.Select(f => ($"{f.Id} at line {f.Line}", new[] { "the template uses ", "can't be read" })).ToList();
        foreach (var output in syntax.Outputs)
        {
            if (output.Depth > 0 && output.Path is { } path && output.Pipes.Count == 0
                && (path.Split('.') is ["part", _] or ["macro", _] or ["macro", _, not "content"] or ["snippet", _, "content"] or ["snippet", _, _, "content"]))
                problems.Add(($"placement {path} inside {string.Join(" ", output.Enclosing)}", ["a placement inside an if or a for", .. ifEdits]));
            foreach (var read in output.Paths)
            {
                if (read.Split('.') is ["node" or "input", _])
                    problems.Add(($"bare node or input {read}", []));
                if (read.EndsWith(".prompt", StringComparison.Ordinal) && read.StartsWith("macro.", StringComparison.Ordinal))
                    problems.Add(($"30's {read}", []));
                if (read.StartsWith("profile.", StringComparison.Ordinal) && site == TemplateSite.Prompt)
                    problems.Add(($"a prompt reads {read}", ["which a 31 prompt doesn't read"]));
                if (read.StartsWith("deliverable.", StringComparison.Ordinal) && site is TemplateSite.Prompt or TemplateSite.Rendered)
                    problems.Add(($"reads {read}", ["which a 31 prompt doesn't read", "which a 31 rendered node doesn't read"]));
            }

            if (output.Pipes.Any(p => p.Name is "concept_bullets" or "concept_context"))
                problems.Add(("30's renderer pipe", []));
        }

        foreach (var chain in syntax.Ifs)
        {
            var handled = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var test in chain.Tests)
            {
                switch (DialectRules.TestForm(test))
                {
                    case BodyTest.Refused when test.Asserts && test.Pipes.Count == 0:
                        // #1508 (R17, Task 26): a bare or != null test asserts an object that can be null is there, the guard
                        // A11-009 asks for; whether its path is such an object is the typed check's (publish), not this one's.
                        break;
                    case BodyTest.Refused:
                        problems.Add(($"body if tests {test.Text}", ifEdits));
                        break;
                    case BodyTest.State:
                        var literal = test.Literal![1..^1];
                        if (!DialectRules.States.Contains(literal, StringComparer.Ordinal))
                        {
                            problems.Add(($"S05-010: {test.Text}", ifEdits));
                            break;
                        }

                        if (!handled.TryGetValue(test.Path!, out var states))
                            handled[test.Path!] = states = new HashSet<string>(StringComparer.Ordinal);
                        states.UnionWith(test.Operator == "==" ? [literal] : DialectRules.States.Where(s => s != literal));
                        break;
                }
            }

            if (!chain.HasElse)
            {
                foreach (var (path, states) in handled.Where(h => DialectRules.States.Any(s => !h.Value.Contains(s))))
                    problems.Add(($"S05-009: {path} misses a state", ifEdits));
            }
        }

        return problems;
    }
}
