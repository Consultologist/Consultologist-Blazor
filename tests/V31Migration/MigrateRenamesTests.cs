using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Consultologist.Api.Tests.ByteEquivalence;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Migration;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 1.9, part 01 §§ 6 and 7, D6): the renames pass. An id 31 reserves (a word,
/// a Windows name) or refuses for its length is renamed before any file is written. The new id is the hand choice, or
/// the id with its kind as a suffix, numbered when that is taken, and cut first so the whole is at most 64 characters.
/// Every rename is reported, and every reference to it, and nothing else, is rewritten. Object field names are never
/// renamed.
/// </summary>
public class MigrateRenamesTests
{
    /// <summary>Options with no hand choice: an account package's (§ 1.9, "the kind is added as a suffix").</summary>
    private static Migration31Options NoChoice => MigrationFixture.NoChoices;

    private static (string Json, Dictionary<string, string> Files) Package(string package)
    {
        var (json, files) = ByteEquivalenceFixtures.ReadPackage(package);
        return (json, new Dictionary<string, string>(files, StringComparer.Ordinal));
    }

    /// <summary>
    /// The 30 <paramref name="root"/> <paramref name="from"/> called <paramref name="to"/> in the package: its quoted id in
    /// the manifest and the index files, its reads (<c>&lt;root&gt;.&lt;from&gt;</c>) everywhere, and its file. A fixture
    /// edit for building a package with a reserved id; the migration itself renames by token.
    /// </summary>
    private static void Call(ref string json, Dictionary<string, string> files, string root, string from, string to, string? file = null)
    {
        var read = new Regex($@"\b{root}\.{from}\b");
        json = json.Replace($"\"{from}\"", $"\"{to}\"", StringComparison.Ordinal);
        json = read.Replace(json, $"{root}.{to}");
        foreach (var path in files.Keys.ToList())
        {
            var text = read.Replace(files[path], $"{root}.{to}");
            if (path.EndsWith("index.json", StringComparison.Ordinal))
                text = text.Replace($"\"{from}\"", $"\"{to}\"", StringComparison.Ordinal);
            files[path] = text;
        }

        if (file is not null && files.Remove(file, out var moved))
            files[file.Replace($"/{from}.md", $"/{to}.md", StringComparison.Ordinal)] = moved;
    }

    private static Migration31Result Run(string json, Dictionary<string, string> files, Migration31Options options)
    {
        var result = WorkflowPackageMigration30To31.Migrate(json, files, options);
        Assert.True(result.Succeeded, string.Join("\n", result.Refusals));
        return result;
    }

    private static IReadOnlyDictionary<(string Kind, string Id), string> PlanOf(string json, Dictionary<string, string> files, Migration31Options options)
    {
        var (m, refusals) = PackageFormat.V31.Migration.Migrated.Read(json, files, options);
        Assert.Empty(refusals);
        return Migrate.Renames.Plan(m!);
    }

    private static string Body(Migration31Result result, string path) => MigrateNodesTests.Md(result, path).Body;

    private static JsonObject Json(Migration31Result result, string path) => MigrateNodesTests.Json(result, path);

    [Fact]
    public void Parts_BecomesSections_InExampleNestedNote()
    {
        var result = MigrateNodesTests.Migrated("example-nested-note");

        Assert.Contains("sections", Json(result, "nodes/index.json")["items"]!.AsArray().Select(i => (string?)i));
        Assert.DoesNotContain("parts", Json(result, "nodes/index.json")["items"]!.AsArray().Select(i => (string?)i));
        Assert.Equal("node.sections.paragraphs", (string?)Json(result, "nodes/sections/index.json")["ret"]);
        Assert.Equal("node.sections.paragraphs.draft_paragraph", (string?)Json(result, "nodes/sections/paragraphs/index.json")["ret"]);
        Assert.Contains("nodes/sections/outline.md", result.Files.Keys);
        Assert.Contains("nodes/sections/paragraphs/draft_paragraph.md", result.Files.Keys);
        Assert.DoesNotContain(result.Files.Keys, k => k.StartsWith("nodes/parts", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Files.Values, t => t.Contains("node.parts", StringComparison.Ordinal));

        // Keys stay keyed by the 30 id (D1, D12); the 31 key reads the new one.
        Assert.Equal("node.sections", result.Keys["parts"]);
        Assert.Equal("node.sections.outline", result.Keys["outline"]);
        Assert.Equal("node.sections.paragraphs.draft_paragraph", result.Keys["draft_paragraph"]);
        Assert.False(result.Keys.ContainsKey("sections"));

        const string notice = "Renamed node 'parts' to 'sections': 'parts' is reserved at 31.";
        Assert.Equal([notice], result.Report.Renames);
        Assert.Contains(notice, result.Notices);
        Assert.Empty(result.HandEdits.Where(e => !MigratedPackagesTests.PastTheInstanceLimit(e))); // #1508 (R11): nested-note's one edit, the instances limit
        V31Contract.AssertSentence(new SourceRefusal("A10-006", notice, ""));
    }

    [Fact]
    public void Parts_BecomesSections_InEveryTemplateAndCondition()
    {
        // A template and a condition that read the loop: both follow the rename.
        var (json, files) = Package("example-nested-note");
        files["prompts/draft_summary.md"] += "\nSections: {{ node.parts.content }}\n";
        json = json.Replace("\"while\": \"node.judge.value.verdict != \\\"good\\\"\"",
            "\"while\": \"node.judge.value.verdict != \\\"good\\\" && (node.parts | array.size) > 0\"", StringComparison.Ordinal);
        Assert.Contains("(node.parts | array.size)", json, StringComparison.Ordinal);

        var result = Run(json, files, MigrationFixture.Options("example-nested-note"));

        Assert.Contains("Sections: {{ node.sections.content }}", Body(result, "nodes/refine/draft_summary.md"), StringComparison.Ordinal);
        var check = Json(result, "nodes/refine/index.json")["while"]!.ToJsonString();
        Assert.Contains("node.sections", check, StringComparison.Ordinal);
        Assert.DoesNotContain("node.parts", check, StringComparison.Ordinal);
    }

    [Fact]
    public void WithNoChoice_TheKindIsTheSuffix()
    {
        // A loop, a while, an agent, a Windows name, an input, a format and a deliverable, all reserved.
        var (json, files) = Package("example-nested-note");
        Call(ref json, files, "node", "refine", "pass");
        Call(ref json, files, "node", "judge", "check", "prompts/judge.md");
        Call(ref json, files, "node", "plan", "con", "prompts/plan.md");
        Call(ref json, files, "input", "consult_draft", "value");
        json = json.Replace("\"verdict\"", "\"format\"", StringComparison.Ordinal).Replace("\"note\"", "\"run\"", StringComparison.Ordinal);

        var plan = PlanOf(json, files, NoChoice);
        Assert.Equal(
            [
                (("input", "value"), "value_input"),
                (("node", "con"), "con_node"),
                (("node", "parts"), "parts_for"),
                (("node", "pass"), "pass_while"),
                (("node", "check"), "check_node"),
                (("format", "format"), "format_format"),
                (("deliverable", "run"), "run_deliverable"),
            ],
            plan.Select(p => (p.Key, p.Value)));

        var result = Run(json, files, NoChoice);
        Assert.Equal(["value_input"], JsonNode.Parse(result.ManifestJson)!["inputs"]!.AsObject().Select(i => i.Key));
        Assert.Equal("node.con_node.value.sections", (string?)Json(result, "nodes/parts_for/index.json")["in"]);
        Assert.Contains("node.check_node", Json(result, "nodes/pass_while/index.json")["while"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Equal("format_format", (string?)MigrateNodesTests.Md(result, "nodes/pass_while/check_node.md").Header["format"]);
        Assert.Contains("formats/format_format.json", result.Files.Keys);
        Assert.Contains("nodes/con_node.md", result.Files.Keys);
        // The pass reads are in the part the templates pass lifted the 30 if into: the whole file holds them.
        var summary = result.Files["nodes/pass_while/draft_summary.md"];
        Assert.Contains("{{ pass.previous.node.check_node.value.feedback }}", summary, StringComparison.Ordinal);
        Assert.Contains("{{ input.value_input.content }}", summary, StringComparison.Ordinal);
        Assert.Equal("node.pass_while", result.Keys["pass"]);
        Assert.Equal("node.pass_while.check_node", result.Keys["check"]);
        Assert.Equal("node.con_node", result.Keys["con"]);

        string[] notices =
        [
            "Renamed input 'value' to 'value_input': 'value' is reserved at 31.",
            "Renamed node 'con' to 'con_node': 'con' is reserved at 31.",
            "Renamed node 'parts' to 'parts_for': 'parts' is reserved at 31.",
            "Renamed node 'pass' to 'pass_while': 'pass' is reserved at 31.",
            "Renamed node 'check' to 'check_node': 'check' is reserved at 31.",
            "Renamed format 'format' to 'format_format': 'format' is reserved at 31.",
            "Renamed deliverable 'run' to 'run_deliverable': 'run' is reserved at 31.",
        ];
        Assert.Equal(notices, result.Report.Renames);
        foreach (var notice in notices)
            V31Contract.AssertSentence(new SourceRefusal("A10-006", notice, ""));
    }

    [Theory]
    [InlineData("judgement", "built-in")]
    [InlineData("check_result", "built-in")]
    public void APackageFormatNamedLikeARegistryFormat_IsRenamed_AndPublishes(string name, string registry)
    {
        // Final review A1: 30 admits a package format named like a 31 built-in or catalogue format, which 31 refuses
        // (S01-034); the format takes its kind as a suffix and every reference follows, so the package publishes.
        var (json, files) = Package("example-nested-note");
        json = json.Replace("\"id\": \"verdict\"", $"\"id\": \"{name}\"", StringComparison.Ordinal)
            .Replace("\"format\": \"verdict\"", $"\"format\": \"{name}\"", StringComparison.Ordinal);
        Assert.Contains($"\"format\": \"{name}\"", json, StringComparison.Ordinal);

        var options = MigrationFixture.Options("example-nested-note");
        Assert.Equal($"{name}_format", PlanOf(json, files, options)[("format", name)]);

        var result = Run(json, files, options);
        Assert.Contains($"formats/{name}_format.json", result.Files.Keys);
        Assert.DoesNotContain($"formats/{name}.json", result.Files.Keys);
        Assert.Equal($"{name}_format", (string?)MigrateNodesTests.Md(result, "nodes/refine/judge.md").Header["format"]);
        var notice = $"Renamed format '{name}' to '{name}_format': '{name}' is the name of a {registry} format at 31.";
        Assert.Contains(notice, result.Report.Renames);
        Assert.Contains(notice, result.Notices);
        V31Contract.AssertSentence(new SourceRefusal("A10-065", notice, ""));
        Assert.Empty(result.HandEdits.Where(e => !MigratedPackagesTests.PastTheInstanceLimit(e))); // #1508 (R11): nested-note's one edit, the instances limit
        Assert.Empty(MigratedPackagesTests.Publish(result));
    }

    [Fact]
    public void WithNoChoice_AMacroSnippetOrItem_TakesItsKind_AndAMacroThatBecomesASnippet_IsASnippet()
    {
        var (json, files) = Package("test/general");
        Call(ref json, files, "macro", "letrozole_prescription_text", "macro", "macros/letrozole_prescription_text.md");
        Call(ref json, files, "snippet", "snomed_tool_guidance", "snippet", "snippets/snomed_tool_guidance.md");
        Call(ref json, files, "item", "hpi", "item", "snippets/standards/hpi.md");

        var plan = PlanOf(json, files, NoChoice);
        Assert.Equal(
            [
                (("macro", "macro"), "macro_macro"),          // read as .prompt: stays a macro
                (("macro", "test"), "test_snippet"),          // placed on the deliverable, bare: a snippet (D4, D6)
                (("snippet", "snippet"), "snippet_snippet"),
                (("snippet item", "standards/item"), "item_snippet"),
            ],
            plan.Select(p => (p.Key, p.Value)));

        var result = Run(json, files, NoChoice);
        Assert.Contains("{{ snippet.snippet_snippet.content }}", Body(result, "nodes/identify_problem.md"), StringComparison.Ordinal);
        Assert.Contains("snippets/snippet_snippet.md", result.Files.Keys);
        Assert.Contains("snippets/standards/item_snippet.md", result.Files.Keys);
        Assert.Contains("item_snippet", Json(result, "snippets/standards/index.json")["items"]!.AsArray().Select(i => (string?)i));
        Assert.Contains("{{ macro.macro_macro }}", Body(result, "snippets/standards/assessment_plan.md"), StringComparison.Ordinal);

        string[] notices =
        [
            "Renamed macro 'macro' to 'macro_macro': 'macro' is reserved at 31.",
            "Renamed macro 'test' to 'test_snippet': 'test' is reserved at 31.",
            "Renamed snippet 'snippet' to 'snippet_snippet': 'snippet' is reserved at 31.",
            "Renamed snippet 'standards/item' to 'standards/item_snippet': 'standards/item' is reserved at 31.",
        ];
        Assert.Equal(notices, result.Report.Renames);
        foreach (var notice in notices)
            V31Contract.AssertSentence(new SourceRefusal("A10-006", notice, ""));

        // #1508 (R14; Task 32 fix round 1): each renamed macro's 30 id, which 30's model wrote in its marker (test, written as a snippet, D4, is listed too: it was a 30 macro).
        Assert.Equal([("macro_macro", "macro"), ("test_snippet", "test")], result.MacroIds30.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => (p.Key, p.Value)));
    }

    [Fact]
    public void TestGeneralsTestMacro_IsRenamedTestSnippet_WithItsChoices()
    {
        var result = MigrateNodesTests.Migrated("test/general");
        const string notice = "Renamed macro 'test' to 'test_snippet': 'test' is reserved at 31.";
        Assert.Equal([notice], result.Report.Renames);
    }

    [Fact]
    public void ACollidingRename_GetsANumber()
    {
        // A node parts_for beside the loop parts.
        var (json, files) = Package("example-nested-note");
        Call(ref json, files, "node", "plan", "parts_for", "prompts/plan.md");

        var result = Run(json, files, NoChoice);

        const string notice = "Renamed 'parts' to 'parts_for_2'; 'parts_for' was already taken.";
        Assert.Equal([notice], result.Report.Renames);
        V31Contract.AssertSentence(new SourceRefusal("S10-006", notice, ""));
        Assert.Contains("nodes/parts_for_2/index.json", result.Files.Keys);
        Assert.Contains("nodes/parts_for.md", result.Files.Keys);                                   // the other node is untouched
        Assert.Equal("node.parts_for.value.sections", (string?)Json(result, "nodes/parts_for_2/index.json")["in"]);
        Assert.Equal("node.parts_for_2", result.Keys["parts"]);
        Assert.Equal("node.parts_for", result.Keys["parts_for"]);
    }

    [Fact]
    public void ALongId_IsShortenedFirst()
    {
        var sixtyFour = new string('a', 64);
        Assert.Equal("parts_for", Migrate.Renames.Fit("parts", "_for"));
        Assert.Equal(new string('a', 60) + "_for", Migrate.Renames.Fit(sixtyFour, "_for"));
        Assert.Equal(new string('a', 58) + "_for_2", Migrate.Renames.Fit(sixtyFour, "_for_2"));
        Assert.Equal(64, Migrate.Renames.Fit(new string('b', 100), "_deliverable_10").Length);

        // 30 has no bound on an id's length; 31 holds it to 64 (S01-031). Two inputs alike in their first 64 characters.
        var (json, files) = Package("example-nested-note");
        var first = "consult_draft_" + new string('x', 56);                                          // 70 characters
        var second = "consult_draft_" + new string('x', 54) + "_y";                                   // 70, the same first 64
        Call(ref json, files, "input", "consult_draft", first);
        var root = JsonNode.Parse(json)!.AsObject();
        root["inputs"]!.AsArray().Add(new JsonObject { ["id"] = second, ["label"] = "Other draft", ["required"] = false });
        json = root.ToJsonString();

        var result = Run(json, files, NoChoice);

        var inputs = JsonNode.Parse(result.ManifestJson)!["inputs"]!.AsObject().Select(i => i.Key).ToList();
        var cut = first[..64];
        var numbered = first[..62] + "_2";
        Assert.Equal([cut, numbered], inputs);
        Assert.All(inputs, id => Assert.Null(SourceIds.Check(id, "manifest.json")));
        Assert.Contains($"{{{{ input.{cut}.content }}}}", Body(result, "nodes/refine/draft_summary.md"), StringComparison.Ordinal);
        string[] notices =
        [
            $"Renamed input '{first}' to '{cut}': an id is at most 64 characters at 31.",
            $"Renamed input '{second}' to '{numbered}': an id is at most 64 characters at 31.",
        ];
        Assert.Equal(notices, result.Report.Renames.Where(n => n.Contains("input", StringComparison.Ordinal)));
        foreach (var notice in notices)
            V31Contract.AssertSentence(new SourceRefusal("A10-034", notice, ""));
    }

    [Fact]
    public void ObjectFieldNames_AreNeverRenamed()
    {
        // A format's field value and an input's field value: fields have no reserved list (part 01 § 6).
        var (json, files) = Package("example-nested-note");
        files["formats/verdict.json"] = files["formats/verdict.json"].Replace("\"feedback\"", "\"value\"", StringComparison.Ordinal);
        files["prompts/draft_summary.md"] = files["prompts/draft_summary.md"].Replace("value.feedback", "value.value", StringComparison.Ordinal);
        var nested = Run(json, files, MigrationFixture.Options("example-nested-note"));

        Assert.Contains("value", Json(nested, "formats/verdict.json")["properties"]!.AsObject().Select(p => p.Key));
        Assert.Contains("{{ pass.previous.node.judge.value.value }}", nested.Files["nodes/refine/draft_summary.md"], StringComparison.Ordinal);
        Assert.DoesNotContain(nested.Report.Renames, n => n.Contains("'value'", StringComparison.Ordinal));

        var (intakeJson, intakeFiles) = Package("example-structured-intake");
        intakeJson = intakeJson.Replace("\"id\": \"age\"", "\"id\": \"title\"", StringComparison.Ordinal)
            .Replace("input.patient.age", "input.patient.title", StringComparison.Ordinal);
        foreach (var path in intakeFiles.Keys.ToList())
            intakeFiles[path] = intakeFiles[path].Replace("input.patient.age", "input.patient.title", StringComparison.Ordinal);
        var intake = Run(intakeJson, intakeFiles, MigrationFixture.Options("example-structured-intake"));

        var patient = JsonNode.Parse(intake.ManifestJson)!["inputs"]!["patient"]!.ToJsonString();
        Assert.Contains("\"title\"", patient, StringComparison.Ordinal);
        Assert.Contains("{{ input.patient.value.title }}", Body(intake, "nodes/sections_note_section/note_section.md"), StringComparison.Ordinal);
        Assert.Empty(intake.Report.Renames);
    }

    [Fact]
    public void ARename_TouchesReferencesOnly()
    {
        // Review Focus 2: the old id inside a longer id (counterparts), in string literals, and in prose.
        var (json, files) = Package("example-nested-note");
        Call(ref json, files, "node", "outline", "counterparts", "prompts/outline.md");
        files["prompts/draft_summary.md"] += "\nIts parts are listed below.\n{{ \"node.parts\" }} {{ \"parts\" }}\n{{ node.parts.content }}\n";
        files["prompts/counterparts.md"] += "\nThe parts of the plan: {{ node.plan.content }}\n";

        var result = Run(json, files, NoChoice);

        var summary = Body(result, "nodes/refine/draft_summary.md");
        Assert.EndsWith("\nIts parts are listed below.\n{{ \"node.parts\" }} {{ \"parts\" }}\n{{ node.parts_for.content }}\n", summary, StringComparison.Ordinal);
        Assert.EndsWith("\nThe parts of the plan: {{ node.plan.content }}\n", Body(result, "nodes/parts_for/counterparts.md"), StringComparison.Ordinal);
        Assert.Equal("node.counterparts.value.points", (string?)Json(result, "nodes/parts_for/paragraphs/index.json")["in"]);
        Assert.Equal(["counterparts", "paragraphs"], Json(result, "nodes/parts_for/index.json")["nodes"]!.AsArray().Select(i => (string?)i));
        Assert.Equal(["Renamed node 'parts' to 'parts_for': 'parts' is reserved at 31."], result.Report.Renames);

        // Byte for byte what the same package gives when its author had called the loop parts_for: nothing else changed.
        var (handJson, handFiles) = (json, new Dictionary<string, string>(files, StringComparer.Ordinal));
        Call(ref handJson, handFiles, "node", "parts", "parts_for");
        handFiles["prompts/draft_summary.md"] = handFiles["prompts/draft_summary.md"].Replace("{{ \"node.parts_for\" }}", "{{ \"node.parts\" }}", StringComparison.Ordinal);
        var hand = Run(handJson, handFiles, NoChoice);
        Assert.Empty(hand.Report.Renames);
        Assert.Equal(hand.ManifestJson, result.ManifestJson);
        Assert.Equal(hand.Files.OrderBy(f => f.Key, StringComparer.Ordinal), result.Files.OrderBy(f => f.Key, StringComparer.Ordinal));
        Assert.Equal(hand.Notices, result.Notices.Except(result.Report.Renames));
    }

    [Fact]
    public void ThirtysIds_AlreadyFitThe31Pattern()
    {
        // 30's id rule (WorkflowDeclaredIds, every kind since 28) is 31's pattern: no 30 id fails it.
        foreach (var id in new[] { "parts", "a", "a1_b", "Parts", "1parts", "_parts", "draft-summary", "a.b", "a b", "é" })
            Assert.Equal(SourceIds.IsPattern(id), WorkflowDeclaredIds.IsValid(id));

        var (json, files) = Package("example-nested-note");
        Call(ref json, files, "node", "draft_summary", "draft-summary", "prompts/draft_summary.md");
        var refused = WorkflowPackageMigration30To31.Migrate(json, files, NoChoice);
        Assert.False(refused.Succeeded);
    }

    [Theory]
    [InlineData("plan", true)]
    [InlineData("items", false)]
    [InlineData("Sections", false)]
    public void AHandChosenRenameThatCannotBeMade_IsRefused(string chosen, bool taken)
    {
        var (json, files) = Package("example-nested-note");
        var options = MigrationFixture.Options("example-nested-note") with { Renames = new Dictionary<string, string> { ["parts"] = chosen } };

        var result = WorkflowPackageMigration30To31.Migrate(json, files, options);

        var sentence = $"The chosen rename of 'parts' to '{chosen}' cannot be made: '{chosen}' {(taken ? "is already taken" : "is not a 31 id")}; choose another.";
        Assert.Equal([sentence], result.Refusals);
        Assert.Equal(json, result.ManifestJson);
        V31Contract.AssertSentence(new SourceRefusal("A10-033", sentence, ""));
    }

    [Theory]
    [MemberData(nameof(MigrateLayoutTests.Packages), MemberType = typeof(MigrateLayoutTests))]
    public void APackageWithNoReservedId_IsNotReadAgain(string package)
    {
        var (json, files) = Package(package);
        var (m, _) = PackageFormat.V31.Migration.Migrated.Read(json, files, MigrationFixture.Options(package));
        var plan = Migrate.Renames.Plan(m!);
        var applied = Migrate.Renames.Apply(m!).Migrated;
        if (plan.Count == 0)
            Assert.Same(m, applied);
        else
            Assert.NotSame(m, applied);
    }

    [Fact]
    public void AHeaderIsSplitOff_OnlyFromASnippetOrAMacroFile()
    {
        // A 30 prompt has no header: one that opens with a line '{' is all body, and a rename rewrites all of it. Only the
        // files whose headers 30 read (a snippet's, a macro's) have one split off. (An unread prompt file reaches the
        // rename: 30 refuses a header only on a prompt a node names.)
        var (json, files) = Package("example-nested-note");
        files["prompts/notes.md"] = "{\nSee {{ node.parts.content }}\n}\n{{ node.parts.content }}\n";
        var (read, refusals) = PackageFormat.V31.Migration.Migrated.Read(json, files, MigrationFixture.Options("example-nested-note"));
        Assert.Empty(refusals);

        var (renamed, renameRefusals) = PackageFormat.V31.Migration.Migrate.Renames.Apply(read!);

        Assert.Empty(renameRefusals);
        Assert.Equal("{\nSee {{ node.sections.content }}\n}\n{{ node.sections.content }}\n", renamed!.Files30["prompts/notes.md"]);
    }
}
