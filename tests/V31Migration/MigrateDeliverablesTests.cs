using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.ByteEquivalence;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.Api.Tests.V31Templates;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Migration;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 1.2 the deliverable rows, §§ 1.4, 1.10; part 06 §§ 6.1, 6.3 to 6.6, 7, 8, 10;
/// D3, D4, D7, D8): the deliverables pass. Each 30 deliverable becomes <c>deliverables/&lt;id&gt;.md</c> with part 06
/// § 6.1's header and the composition template D8 names, one test per composer rule of the fixture README's
/// <c>composer-*</c> sections (whether a template gives 30's bytes is Task 15's to measure). A deliverable macro with no
/// condition is a snippet read; one with a condition is a hand edit written in a form the 31 checker refuses, as is a
/// placed macro's read of another deliverable. Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class MigrateDeliverablesTests
{
    private static readonly JsonSerializerOptions Unescaped = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string Header(Migration31Result result, string path) => MigrateNodesTests.Md(result, path).Header.ToJsonString(Unescaped);

    private static string Template(Migration31Result result, string id) => MigrateNodesTests.Md(result, $"deliverables/{id}.md").Body;

    private static void AssertHandEdit(Migration31Result result, string file, string member, string id, string reason)
    {
        var edit = $"Hand edit needed in {file}, {member}: {reason}";
        Assert.Single(result.HandEdits, e => e == edit);
        Assert.Contains(edit, result.Notices);
        V31Contract.AssertSentence(new SourceRefusal("S10-002", edit, file));
        V31Contract.AssertSentence(new SourceRefusal(id, reason, ""));
    }

    private static string Conditioned(string macro) =>
        $"macro '{macro}' is placed on this deliverable with a condition; place its text as a snippet with an if, or write the condition in the template.";

    /// <summary>The refusals of a 31 package whose deliverable <c>note</c> has <paramref name="body"/>, the macro <c>review_note</c> and the snippet <c>echo</c> (<paramref name="snippet"/>) beside it.</summary>
    private static IReadOnlyList<SourceRefusal> RefusalsOfDeliverable(string body, string snippet = "Seen with Margaret Okafor.\n") =>
        TemplateFixture.RefusalsOf(TemplateFixture.Package()
            .WithMacro("review_note", "Seen again for review.\n")
            .WithAgent("draft", "Draft the plan for Margaret Okafor.\n{{ macro.review_note }}\n")
            .WithSnippet("echo", snippet, "markdown_block")
            .WithDeliverable(body));

    [Theory]
    [MemberData(nameof(MigratedPackagesTests.Packages), MemberType = typeof(MigratedPackagesTests))]
    public void AMigratedPackage_DeliversPdf(string package)
    {
        // #1507 (R8, ruled by the owner on #1507, 2026-11-22, 1): 30 emailed a job's documents as password-protected PDFs,
        // falling back to the link-only email, so the root deliverables folder delivers pdf; 30 has no destination folder,
        // so the migration writes none: every deliverable is at the root.
        var result = MigratedPackagesTests.Migrated(package);

        var root = MigrateNodesTests.Json(result, "deliverables/index.json");
        Assert.Equal("""["pdf"]""", root["delivery"]!.ToJsonString());
        Assert.Null(root["bundle"]);
        Assert.DoesNotContain(result.Files.Keys, path =>
            path.StartsWith("deliverables/", StringComparison.Ordinal) && path.Count(c => c == '/') > 1);
    }

    [Fact]
    public void ADeliverable_BecomesItsFile()
    {
        var gates = MigrateNodesTests.Migrated("composer-gates");

        // Part 06 § 6.1's members, in the Deliverable shape's order: no if, the gate as check.<id>, whenEmpty's default.
        Assert.Equal(
            """{"label":"Clinic note","kind":"rendered","description":null,"format":"markdown_block","if":null,"check":"check.note_coverage","whenEmpty":"release"}""",
            Header(gates, "deliverables/note.md"));

        // The if converted (part 10 § 2), ungated: check null.
        Assert.Equal(
            """{"label":"Patient copy","kind":"rendered","description":null,"format":"markdown_block","if":{"===":[{"var":"input.audience.value"},"patient"]},"check":null,"whenEmpty":"release"}""",
            Header(gates, "deliverables/patient_copy.md"));

        // The root delivery folder lists them in 30's order (part 06 § 8; no destination folder), delivering pdf (R8).
        Assert.Equal("""{"description":null,"items":["note","record_copy","patient_copy","cover_letter"],"delivery":["pdf"],"bundle":null}""",
            MigrateNodesTests.Json(gates, "deliverables/index.json").ToJsonString());

        // Keys: each deliverable by its 30 key; a node of the same id keeps its own entry (composer-sources' plan).
        Assert.Equal("deliverable.note", gates.Keys["deliverable.note"]);
        var sources = MigrateNodesTests.Migrated("composer-sources");
        Assert.Equal("deliverable.plan", sources.Keys["deliverable.plan"]);
        Assert.Equal("node.plan", sources.Keys["plan"]);

        // A renamed deliverable (§ 1.9) keeps its 30 key in Keys, its value the 31 key.
        var renamed = MigrateNodesTests.Migrated("composer-gates", root =>
            root["deliverables"]!.AsArray().OfType<JsonObject>().Single(d => (string?)d["id"] == "cover_letter")["id"] = "value");
        Assert.Equal("deliverable.value_deliverable", renamed.Keys["deliverable.value"]);
        Assert.True(renamed.Files.ContainsKey("deliverables/value_deliverable.md"));

        // § 1.10, D7: whenEmpty from the options; the hand-chosen examples.
        Assert.Equal("hold", (string?)MigrateNodesTests.Md(MigrateNodesTests.Migrated("test/general"), "deliverables/consult.md").Header["whenEmpty"]);
        Assert.Equal("fail", (string?)MigrateNodesTests.Md(MigrateNodesTests.Migrated("example-nested-note"), "deliverables/note.md").Header["whenEmpty"]);

        // An if the converter refuses (string.upcase has no JSON Logic form; string.strip compared with "" is blank since
        // fix round 1 of Task 12, part 05 § 7) stays 30's text with one hand edit, through the one condition helper.
        var refused = MigrateNodesTests.Migrated("composer-gates", root =>
            root["deliverables"]!.AsArray().OfType<JsonObject>().Single(d => (string?)d["id"] == "patient_copy")["if"] = "(input.consult_draft | string.upcase) == \"\"");
        Assert.Equal("(input.consult_draft | string.upcase) == \"\"", (string?)MigrateNodesTests.Md(refused, "deliverables/patient_copy.md").Header["if"]);
        var edit = Assert.Single(refused.HandEdits, e => e.StartsWith("Hand edit needed in deliverables/patient_copy.md, if: ", StringComparison.Ordinal));
        V31Contract.AssertSentence(new SourceRefusal("S10-002", edit, "deliverables/patient_copy.md"));

        // Written so, the 31 checker refuses the if until the edit is made: a JSON string is no condition.
        var refusal = Assert.Single(TemplateFixture.RefusalsOf(TemplateFixture.Package().With("deliverables/note.md", SourceFixture.Md(
            "{ \"label\": \"Consultation note\", \"kind\": \"rendered\", \"description\": null, \"format\": \"markdown_block\", \"if\": "
            + MigrateNodesTests.Md(refused, "deliverables/patient_copy.md").Header["if"]!.ToJsonString() + ", \"check\": null, \"whenEmpty\": \"release\" }",
            "{{ input.consult_draft.content }}"))));
        Assert.Equal(("S03-025", "deliverables/note.md"), (refusal.Id, refusal.Path));
        V31Contract.AssertSentence(refusal);
    }

    [Theory]
    [InlineData("single", "{{ node.opening.content | string.strip }}")]                                     // one scalar source
    [InlineData("pair", "{{ node.opening.content | string.strip }}\n\n{{ node.closing.content | string.strip }}")]          // two, one blank line between
    [InlineData("topics", "{{ node.topic_loop.content | string.strip }}")]                                   // a for: its .content, 30's blocks
    [InlineData("sections", "{{ node.section_loop.content | string.strip }}")]                               // a for with a for inside
    [InlineData("summary", "{{ node.refine.content | string.strip }}")]                                      // a while
    [InlineData("plan", "{{ node.plan_gate.content | string.strip }}")]                                      // an if
    [InlineData("tone_line", "{{ node.tone_case.content | string.strip }}")]                                 // a case
    [InlineData("tally", "{{ node.topic_tally.content | string.strip }}")]                                   // a let of type text
    [InlineData("summary_then_opening", "{{ node.refine.content | string.strip }}\n\n{{ node.opening.content | string.strip }}")]
    [InlineData("topics_then_opening", "{{ node.topic_loop.content | string.strip }}\n\n{{ node.opening.content | string.strip }}")]
    public void EachSource_IsReadAsItsContent_InSourcesOrder(string deliverable, string template)
    {
        var result = MigrateNodesTests.Migrated("composer-sources");

        Assert.Equal(template, Template(result, deliverable));
        Assert.Equal("markdown_block", (string?)MigrateNodesTests.Md(result, $"deliverables/{deliverable}.md").Header["format"]);
    }

    [Fact]
    public void ASnippetPiece_IsNotStripped()
    {
        // #1508 (item 3, F-D8): a macro placed bare stays R5's snippet read; only the source is stripped.
        Assert.Equal("{{ snippet.letterhead.content }}\n\n{{ node.letter_body.content | string.strip }}",
            Template(MigrateNodesTests.Migrated("composer-macros"), "prepended"));
    }

    [Fact]
    public void AGatedSource_AddsNoStateTerm()
    {
        // #1508 (item 1, F-D4): opening is gated by the deliverables that need it, single among them, so it runs whenever
        // single applies; R15 judges it by 30's always-valued, and single keeps 30's if with no .state term (no A10-079).
        var result = MigrateNodesTests.Migrated("composer-sources", root =>
        {
            MigrateNodesTests.DeliverableIf(root, "single", "input.tone == \"formal\"");
            MigrateNodesTests.DeliverableIf(root, "pair", "input.tone == \"plain\"");
            MigrateNodesTests.DeliverableIf(root, "summary_then_opening", "input.include_plan == true");
            MigrateNodesTests.DeliverableIf(root, "topics_then_opening", "input.redraft == true");
        });

        var opening = MigrateNodesTests.Md(result, "nodes/opening.md").Header["if"]!;
        var single = MigrateNodesTests.Md(result, "deliverables/single.md").Header["if"]!;
        Assert.Equal(opening["or"]![0]!.ToJsonString(), single.ToJsonString());
        Assert.DoesNotContain(".state", single.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain(result.Notices, n => n.StartsWith("deliverables/single.md, if: written to skip", StringComparison.Ordinal));
        Assert.DoesNotContain(result.HandEdits, e => e.Contains("deliverables/single.md", StringComparison.Ordinal));
    }

    [Fact]
    public void ADeliverableWhoseSourcesMayNotRun_GetsAnIf()
    {
        // #1508 (ruled by the owner, Q3 of 2026-11-26; R15): 30 skipped a deliverable none of whose sources ran; at 31 it
        // would be composed empty and released, so the migration writes the if that says "a source ran".
        var result = MigrateNodesTests.Migrated("composer-sources");
        static string Ran(string read) => $$"""{"===":[{"var":"{{read}}.state"},"completed"]}""";

        // An if with no else (plan_gate) and a case with an empty branch (tone_case: tone none) may not run, so the
        // deliverable is skipped with it, as at 30.
        Assert.Equal(Ran("node.plan_gate"), MigrateNodesTests.Md(result, "deliverables/plan.md").Header["if"]!.ToJsonString());
        Assert.Equal(Ran("node.tone_case"), MigrateNodesTests.Md(result, "deliverables/tone_line.md").Header["if"]!.ToJsonString());

        // A source that always runs: no if. Nor a while that checks first (refine): 30 composed one that ran no pass as
        // empty text and released the deliverable, as 31 does. Beside another source (summary_then_opening) its separator
        // stays, an expected difference (while-no-pass-separator) that AWhileWithNoPass_KeepsItsSeparator pins.
        foreach (var always in new[] { "single", "pair", "topics", "sections", "summary", "tally", "summary_then_opening", "topics_then_opening" })
            Assert.Null(MigrateNodesTests.Md(result, $"deliverables/{always}.md").Header["if"]);

        // The notice, once per deliverable given an if.
        const string notice = "deliverables/plan.md, if: written to skip the deliverable unless node.plan_gate ran, as 30 skipped a deliverable none of whose sources ran.";
        Assert.Single(result.Notices, n => n == notice);
        V31Contract.AssertSentence(new SourceRefusal("A10-079", notice, ""));
        Assert.Equal(2, result.Notices.Count(n => n.Contains(" as 30 skipped a deliverable none of whose sources ran.", StringComparison.Ordinal)));
        Assert.DoesNotContain(result.HandEdits, e => e.Contains(", if: ", StringComparison.Ordinal));

        // With 30's own condition, the two are joined by and; with two sources that may not run, they are joined by or.
        var joined = MigrateNodesTests.Migrated("composer-sources", root =>
        {
            var plan = root["deliverables"]!.AsArray().OfType<JsonObject>().Single(d => (string?)d["id"] == "plan");
            plan["if"] = "input.tone == \"formal\"";
            plan["sources"] = new JsonArray("node.plan_gate", "node.tone_case");
        });
        Assert.Equal(
            """{"and":[{"===":[{"var":"input.tone.value"},"formal"]},{"or":[""" + Ran("node.plan_gate") + "," + Ran("node.tone_case") + "]}]}",
            MigrateNodesTests.Md(joined, "deliverables/plan.md").Header["if"]!.ToJsonString());
        Assert.Contains(
            "deliverables/plan.md, if: written to skip the deliverable unless node.plan_gate or node.tone_case ran, as 30 skipped a deliverable none of whose sources ran.",
            joined.Notices);

        // An if the converter refuses stays 30's text with its hand edit: nothing is joined to a condition not yet written.
        var refused = MigrateNodesTests.Migrated("composer-sources", root =>
            root["deliverables"]!.AsArray().OfType<JsonObject>().Single(d => (string?)d["id"] == "plan")["if"] = "(input.consult_draft | string.upcase) == \"\"");
        Assert.Equal("(input.consult_draft | string.upcase) == \"\"", (string?)MigrateNodesTests.Md(refused, "deliverables/plan.md").Header["if"]);

        // The migrated package (its hand edits made) publishes, and A11-010 warns only of summary, which 30 released empty
        // too: none of the deliverables the migration gave an if.
        var (refusals, compiled) = CompiledPackage.Publish(MigratedPackagesTests.Upload(MigrationFixture.Edited("composer-sources")), V31Contract.PublishedTypes(), CompiledGoldenTests.Fixed);
        Assert.True(compiled is not null, string.Join("\n", refusals.Select(r => r.Sentence)));
        Assert.Equal(["deliverables/summary.md"], compiled!.Warnings.Where(w => w.Id == "A11-010").Select(w => w.Path));
    }

    [Fact]
    public void ARefusedConditionWhoseSourcesMayNotRun_IsAlsoAHandEditNamingTheTermToJoin()
    {
        // #1508 (final review of Part F, finding 2; Q3, R15): when 30's condition is a hand edit, nothing is joined to it, so
        // the hand edit names the term A10-079 would have joined (A10-084), for the operator to write 30's condition "and" it;
        // otherwise, once the condition is written, the deliverable has an if, A11-010 is silent, and 31 releases it empty
        // where 30 skipped it.
        const string unconvertible = "(input.consult_draft | string.upcase) == \"\"";
        var refused = MigrateNodesTests.Migrated("composer-sources", root =>
            root["deliverables"]!.AsArray().OfType<JsonObject>().Single(d => (string?)d["id"] == "plan")["if"] = unconvertible);

        Assert.Equal(unconvertible, (string?)MigrateNodesTests.Md(refused, "deliverables/plan.md").Header["if"]);
        const string term = """{"===":[{"var":"node.plan_gate.state"},"completed"]}""";
        const string reason = "every source may not run, and 30 skipped the deliverable when none ran; join " + term + " to 30's condition by and.";
        var edits = refused.HandEdits.Where(e => e.StartsWith("Hand edit needed in deliverables/plan.md, if: ", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, edits.Count);
        Assert.Equal("Hand edit needed in deliverables/plan.md, if: " + reason, edits[1]);
        V31Contract.AssertSentence(new SourceRefusal("S10-002", edits[1], "deliverables/plan.md"));
        V31Contract.AssertSentence(new SourceRefusal("A10-084", reason, ""));
        Assert.DoesNotContain(refused.Notices, n => n.StartsWith("deliverables/plan.md, if: written to skip", StringComparison.Ordinal));

        // Two sources that may not run: the term is their "or", as A10-079 joins it.
        var two = MigrateNodesTests.Migrated("composer-sources", root =>
        {
            var plan = root["deliverables"]!.AsArray().OfType<JsonObject>().Single(d => (string?)d["id"] == "plan");
            plan["if"] = unconvertible;
            plan["sources"] = new JsonArray("node.plan_gate", "node.tone_case");
        });
        Assert.Contains(
            "Hand edit needed in deliverables/plan.md, if: every source may not run, and 30 skipped the deliverable when none ran; join "
            + """{"or":[{"===":[{"var":"node.plan_gate.state"},"completed"]},{"===":[{"var":"node.tone_case.state"},"completed"]}]}"""
            + " to 30's condition by and.",
            two.HandEdits);

        // The near miss: a source that always runs keeps today's single hand edit, unchanged.
        var always = MigrateNodesTests.Migrated("composer-sources", root =>
            root["deliverables"]!.AsArray().OfType<JsonObject>().Single(d => (string?)d["id"] == "single")["if"] = unconvertible);
        var only = Assert.Single(always.HandEdits, e => e.StartsWith("Hand edit needed in deliverables/single.md, if: ", StringComparison.Ordinal));
        Assert.DoesNotContain("30 skipped the deliverable", only, StringComparison.Ordinal);
    }

    [Fact]
    public void RanTerm_PrefersState_ThenTheValue_ThenNothing()
    {
        // #1508 (2026-11-26, item 8; F-D12): R15's term is the source's state where the deliverable's if can read it, else
        // its value not null (the fallback), else none: the source is one the migration does not read.
        Assert.Equal("""{"===":[{"var":"node.x.state"},"completed"]}""", Migrate.RanTerm("node.x", _ => true)!.ToJsonString());
        Assert.Equal(
            """{"!==":[{"var":"node.x.value"},null]}""",
            Migrate.RanTerm("node.x", t => !t.ToJsonString().Contains(".state", StringComparison.Ordinal))!.ToJsonString());
        Assert.Null(Migrate.RanTerm("node.x", _ => false));
    }

    [Fact]
    public void TheStateTerm_TypesAtADeliverableIf_AndACheckReadDoesNot()
    {
        // F-D12: the migration's typing scope reads a node's .state as ScopeTree types it, and a deliverable's if may read it
        // (ExpressionChecker's site rule refuses only .content and check.* there, S03's).
        var scope = TemplateRewriterTests.Working("composer-sources").Scope((string?)null);
        Assert.True(ConditionTyping.ReadsAtDeliverableIf(JsonNode.Parse("""{"===":[{"var":"node.plan_gate.state"},"completed"]}""")!, scope));
        Assert.True(ConditionTyping.ReadsAtDeliverableIf(JsonNode.Parse("""{"!==":[{"var":"node.plan_gate.value"},null]}""")!, scope));
        Assert.False(ConditionTyping.ReadsAtDeliverableIf(JsonNode.Parse("""{"===":[{"var":"check.note_coverage.state"},"completed"]}""")!, scope));

        // Near misses: a read of .content is refused at the site, and a state that is not compared is no condition.
        Assert.False(ConditionTyping.ReadsAtDeliverableIf(JsonNode.Parse("""{"===":[{"var":"node.plan_gate.content"},"x"]}""")!, scope));
        Assert.False(ConditionTyping.ReadsAtDeliverableIf(JsonNode.Parse("""{"var":"node.plan_gate.state"}""")!, scope));
    }

    /// <summary>
    /// <c>composer-sources</c> with <c>plan</c>'s 30 condition and sources set, migrated through the deliverables pass with
    /// <paramref name="unread"/> taken out of <see cref="Migrated.NodeReads"/> first: the seam where a source is one the
    /// migration does not read (30 refuses a source naming no node, so no package given reaches it).
    /// </summary>
    private static Migration31Result WithUnread(string? condition, string[] sources, params string[] unread)
    {
        var (json, files) = ByteEquivalenceFixtures.ReadPackage("composer-sources");
        var root = JsonNode.Parse(json)!.AsObject();
        var plan = root["deliverables"]!.AsArray().OfType<JsonObject>().Single(d => (string?)d["id"] == "plan");
        plan["if"] = condition;
        plan["sources"] = new JsonArray([.. sources.Select(s => (JsonNode)s)]);
        var (given, refusals) = Migrated.Read(root.ToJsonString(), new Dictionary<string, string>(files, StringComparer.Ordinal), MigrationFixture.Options("composer-sources"));
        Assert.True(given is not null, string.Join("\n", refusals));
        var (m, renames) = Migrate.Renames.Apply(given!);
        Assert.True(m is not null, string.Join("\n", renames));
        Migrate.Layout(m!);
        Migrate.Inputs(m!);
        Migrate.Nodes(m!);
        Migrate.Templates(m!);
        Migrate.Macros(m!);
        Migrate.Checks(m!);
        foreach (var id in unread)
            Assert.True(m!.NodeReads.Remove(id));
        Migrate.Deliverables(m!);
        return m!.Result([]);
    }

    [Fact]
    public void ASourceTheMigrationDoesNotRead_IsAHandEdit()
    {
        // #1508 (2026-11-26, item 8; F-D13): every source may not run and the migration cannot read whether one ran, so it
        // writes no if that would leave it out: a hand edit (A10-086), so nothing is released empty without a notice. 30's
        // condition is kept as converted, and the term of the sources it can read is named for the operator to join.
        const string start = "Hand edit needed in deliverables/plan.md, if: ";
        var two = WithUnread("input.tone == \"formal\"", ["node.plan_gate", "node.tone_case"], "tone_case");
        Assert.Equal("""{"===":[{"var":"input.tone.value"},"formal"]}""", MigrateNodesTests.Md(two, "deliverables/plan.md").Header["if"]!.ToJsonString());
        AssertHandEdit(two, "deliverables/plan.md", "if", "A10-086",
            "every source may not run, and the migration cannot read whether node.tone_case ran; write the if that skips the deliverable "
            + """when none of its sources ran, as 30 skipped it, joining {"===":[{"var":"node.plan_gate.state"},"completed"]} for the others.""");
        Assert.Single(two.HandEdits, e => e.StartsWith(start, StringComparison.Ordinal));
        Assert.DoesNotContain(two.Notices, n => n.StartsWith("deliverables/plan.md, if: written to skip", StringComparison.Ordinal));

        // Every source unread: the clause for the others is not said, and the if stays as 30 wrote it (none).
        var all = WithUnread(null, ["node.plan_gate", "node.tone_case"], "plan_gate", "tone_case");
        Assert.Null(MigrateNodesTests.Md(all, "deliverables/plan.md").Header["if"]);
        AssertHandEdit(all, "deliverables/plan.md", "if", "A10-086",
            "every source may not run, and the migration cannot read whether node.plan_gate, node.tone_case ran; write the if that "
            + "skips the deliverable when none of its sources ran, as 30 skipped it.");

        // The near miss: beside a source that always runs, the deliverable is never empty: no if, no hand edit, as today.
        var always = WithUnread(null, ["node.plan_gate", "node.tone_case", "node.opening"], "tone_case");
        Assert.Null(MigrateNodesTests.Md(always, "deliverables/plan.md").Header["if"]);
        Assert.DoesNotContain(always.HandEdits, e => e.StartsWith(start, StringComparison.Ordinal));

        // Every source read, through the same seam: today's if, no hand edit.
        var read = WithUnread(null, ["node.plan_gate", "node.tone_case"]);
        Assert.Equal(
            """{"or":[{"===":[{"var":"node.plan_gate.state"},"completed"]},{"===":[{"var":"node.tone_case.state"},"completed"]}]}""",
            MigrateNodesTests.Md(read, "deliverables/plan.md").Header["if"]!.ToJsonString());
        Assert.DoesNotContain(read.HandEdits, e => e.StartsWith(start, StringComparison.Ordinal));
    }

    [Fact]
    public void NoPackage30Accepts_HasASourceTheMigrationDoesNotRead()
    {
        // Why ASourceTheMigrationDoesNotRead_IsAHandEdit works at the seam: 30 refuses a source that names no node and one
        // that is a check (whose check.* read no deliverable if can hold), and every other source is a node whose .state the
        // deliverable's if reads. So A10-086 and R15's .value fallback guard what 30 never let through (item 8, F-D12).
        static string Refused(string source)
        {
            var (json, files) = ByteEquivalenceFixtures.ReadPackage("composer-gates");
            var root = JsonNode.Parse(json)!.AsObject();
            root["deliverables"]!.AsArray().OfType<JsonObject>().Single(d => (string?)d["id"] == "patient_copy")["sources"] =
                new JsonArray("node.patient_line", source);
            var result = WorkflowPackageMigration30To31.Migrate(root.ToJsonString(), new Dictionary<string, string>(files, StringComparer.Ordinal), MigrationFixture.Options("composer-gates"));
            Assert.False(result.Succeeded);
            return string.Join("\n", result.Refusals);
        }

        Assert.Contains("Deliverable 'patient_copy' source names unknown node 'nowhere'.", Refused("node.nowhere"), StringComparison.Ordinal);
        Assert.Contains("Deliverable 'patient_copy' source node.note_coverage is a check;", Refused("node.note_coverage"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWhileWithNoPass_KeepsItsSeparator()
    {
        // #1508 (R15; PR #1532's difference 8, now expected under part 10 § 3.2's while-no-pass-separator): with every branch not taken, 30 skipped plan and tone_line and
        // 31 now skips them too (their if), so neither is compared. A while that ran no pass is empty text on both sides
        // (summary is equal), but beside opening 31 keeps the blank line between the two (summary_then_opening): no if
        // can drop a separator and keep the text, so the controller ruled it an expected difference, while-no-pass-separator.
        var (_, _, texts) = await EquivalenceFixture.Compared("composer-sources", "branches-not-taken");
        string Site(string id) => $"composer-sources/branches-not-taken#deliverables/deliverable.{id}";

        Assert.DoesNotContain(texts, t => t.Site == Site("plan") || t.Site == Site("tone_line"));
        Assert.True(texts.Single(t => t.Site == Site("summary")).Equal);
        var kept = texts.Single(t => t.Site == Site("summary_then_opening"));
        Assert.Equal((false, true, false), (kept.Equal, kept.Expected, kept.Unexplained));
        Assert.Equal(["while-no-pass-separator"], kept.Rules);
    }

    [Fact]
    public void ASignature_IsTheProfilesSignatureAfterABlankLine()
    {
        var result = MigrateNodesTests.Migrated("composer-macros");

        Assert.Equal("{{ node.letter_body.content | string.strip }}\n\n{{ profile.signature }}", Template(result, "signed_only"));
        // After an appended macro: the signature is last.
        Assert.Equal("{{ node.letter_body.content | string.strip }}\n\n{{ snippet.cc_line.content }}\n\n{{ profile.signature }}", Template(result, "signed_letter"));
        Assert.DoesNotContain(result.HandEdits, e => e.Contains("deliverables/signed", StringComparison.Ordinal));
    }

    [Fact]
    public void UnconditionalMacros_AreSnippetReads_InPlacementOrder()
    {
        var result = MigrateNodesTests.Migrated("composer-macros");

        // D4: a prepend before the sources, an append after them, each where 30 placed it.
        Assert.Equal("{{ snippet.letterhead.content }}\n\n{{ node.letter_body.content | string.strip }}", Template(result, "prepended"));
        Assert.Equal("{{ node.letter_body.content | string.strip }}\n\n{{ snippet.closing_line.content }}", Template(result, "appended"));
        Assert.Equal(
            "{{ snippet.letterhead.content }}\n\n{{ snippet.reference_line.content }}\n\n{{ node.letter_body.content | string.strip }}\n\n"
            + "{{ snippet.closing_line.content }}\n\n{{ snippet.second_closing.content }}",
            Template(result, "both"));
        // A deliverable's own if does not make its macros conditional (review_summary's letterhead).
        Assert.Equal("{{ snippet.letterhead.content }}\n\n{{ node.letter_body.content | string.strip }}", Template(result, "review_summary"));
        Assert.Equal("{{ node.section_loop.content | string.strip }}", Template(result, "sections_note"));
        // Task 15 fix round 1 (R5, D4): a snippet only deliverables read keeps 30's bytes, its trailing white space trimmed
        // as 30's composer trimmed a placed macro (ConsultMacroExpander's TrimEnd()), where 31 reads a snippet whole.
        Assert.Equal("Riverside Clinic (fictional)", MigrateNodesTests.Md(result, "snippets/letterhead.md").Body);
        Assert.DoesNotContain(result.HandEdits, e => e.Contains("deliverables/both.md", StringComparison.Ordinal) || e.Contains("deliverables/prepended.md", StringComparison.Ordinal));

        // A snippet read in a deliverable is one 31 accepts.
        Assert.Empty(RefusalsOfDeliverable("{{ snippet.echo.content }}\n\n{{ input.consult_draft.content }}"));

        // A deliverable macro that stays a macro (a prompt also places it) is read as a snippet written from its body, its
        // macro file kept (D4): example-macros' kept_note.
        var placed = MigrateNodesTests.Migrated("example-macros", _ => { }, files => files["prompts/summary.md"] += "{{ macro.kept_note.prompt }}\n");
        Assert.Contains("\n\n{{ snippet.kept_note.content }}\n\n", Template(placed, "consult_note"), StringComparison.Ordinal);
        Assert.True(placed.Files.ContainsKey("macros/kept_note.md"));
        Assert.Equal("""{"label":"Kept note","description":null,"if":null,"format":"markdown_block"}""", Header(placed, "snippets/kept_note.md"));
        Assert.Equal(MigrateNodesTests.Md(placed, "macros/kept_note.md").Body.TrimEnd(), MigrateNodesTests.Md(placed, "snippets/kept_note.md").Body);
        Assert.Single(MigrateNodesTests.Json(placed, "snippets/index.json")["items"]!.AsArray(), i => (string?)i == "kept_note");
    }

    [Fact]
    public void ADeliverableMacroWithACondition_IsAHandEdit()
    {
        var result = MigrateNodesTests.Migrated("composer-macros");

        // A macro if (new_visit_note), a placement if (review_note); review_leaflet and closing_line have none.
        Assert.Equal(
            "{{ node.letter_body.content | string.strip }}\n\n{{ macro.new_visit_note.fixed_text }}\n\n{{ macro.review_note.fixed_text }}\n\n"
            + "{{ snippet.review_leaflet.content }}\n\n{{ snippet.closing_line.content }}",
            Template(result, "gated"));
        AssertHandEdit(result, "deliverables/gated.md", "body", "A10-002", Conditioned("new_visit_note"));
        AssertHandEdit(result, "deliverables/gated.md", "body", "A10-002", Conditioned("review_note"));

        // Optional macros, default on and default off.
        Assert.Equal("{{ node.letter_body.content | string.strip }}\n\n{{ macro.followup_offer.fixed_text }}\n\n{{ macro.leaflet_offer.fixed_text }}", Template(result, "optional"));
        AssertHandEdit(result, "deliverables/optional.md", "body", "A10-002", Conditioned("followup_offer"));
        AssertHandEdit(result, "deliverables/optional.md", "body", "A10-002", Conditioned("leaflet_offer"));

        // The macro file stays for the operator (its if, its choice input).
        Assert.True(result.Files.ContainsKey("macros/review_note.md"));
        Assert.True(result.Files.ContainsKey("macros/followup_offer.md"));

        // example-macros: a placement if (header_line, prepended), a macro if and a placement if (followup_only), a
        // macro if (brief_plan), an optional macro (follow_up_reminder); closing is fixed text.
        var macros = MigrateNodesTests.Migrated("example-macros");
        var consult = Template(macros, "consult_note");
        Assert.StartsWith("{{ macro.header_line.fixed_text }}\n\n{{ node.requested_sections.content | string.strip }}\n\n", consult, StringComparison.Ordinal);
        Assert.EndsWith("{{ macro.followup_only.fixed_text }}\n\n{{ macro.brief_plan.fixed_text }}\n\n{{ macro.follow_up_reminder.fixed_text }}", consult, StringComparison.Ordinal);
        foreach (var macro in new[] { "header_line", "followup_only", "brief_plan", "follow_up_reminder" })
            AssertHandEdit(macros, "deliverables/consult_note.md", "body", "A10-002", Conditioned(macro));
        Assert.Contains("\n\n{{ snippet.closing.content }}\n\n", consult, StringComparison.Ordinal);

        // Written so, the 31 checker refuses the template until the edit is made (D3: a hand edit has a refusal behind it).
        // S05-047: a deliverable places no macro.
        var refusal = Assert.Single(RefusalsOfDeliverable("{{ input.consult_draft.content }}\n\n{{ macro.review_note.fixed_text }}"));
        Assert.Equal(("S05-047", "deliverables/note.md"), (refusal.Id, refusal.Path));
        V31Contract.AssertSentence(refusal);
        Assert.Empty(RefusalsOfDeliverable("{{ input.consult_draft.content }}"));
    }

    [Fact]
    public void TheTestMacro_OfTestGeneral_BecomesASnippetReadAtTheEnd()
    {
        var result = MigrateNodesTests.Migrated("test/general");

        // D6: the macro test, fixed text appended, is the snippet test_snippet, read last.
        Assert.Equal("{{ node.standards.content | string.strip }}\n\n{{ snippet.test_snippet.content }}", Template(result, "consult"));
        Assert.True(result.Files.ContainsKey("snippets/test_snippet.md"));
        Assert.False(result.Files.ContainsKey("macros/test.md"));
        Assert.Equal(
            """{"label":"Consultation note","kind":"rendered","description":null,"format":"markdown_block","if":null,"check":null,"whenEmpty":"hold"}""",
            Header(result, "deliverables/consult.md"));
    }

    [Fact]
    public void ADeliverableThatReadsAnotherDeliverable_IsAHandEdit()
    {
        // composer-macros' quoted_review (optional) reads deliverable.review_summary.content: the placement is A10-002, and
        // the macro body's read, which part 06 § 6.3 refuses, is A10-039 on the macro file.
        var result = MigrateNodesTests.Migrated("composer-macros");
        Assert.EndsWith("{{ macro.quoted_review.fixed_text }}", Template(result, "echoes"), StringComparison.Ordinal);
        AssertHandEdit(result, "deliverables/echoes.md", "body", "A10-002", Conditioned("quoted_review"));
        AssertHandEdit(result, "macros/quoted_review.md", "body", "A10-039",
            "the template reads deliverable.review_summary.content at line 1, which a 31 macro body doesn't read; rewrite it.");
        Assert.Contains("[{{ deliverable.review_summary.content }}]", MigrateNodesTests.Md(result, "macros/quoted_review.md").Body, StringComparison.Ordinal);

        // Not optional, it is fixed text: a snippet the deliverable reads, the read left where it is, said on the snippet.
        var fixedText = MigrateNodesTests.Migrated("composer-macros", _ => { }, files =>
            files["macros/quoted_review.md"] = files["macros/quoted_review.md"].Replace("  \"optional\": true,\n  \"default\": false\n", "  \"optional\": false\n", StringComparison.Ordinal));
        Assert.EndsWith("{{ snippet.quoted_review.content }}", Template(fixedText, "echoes"), StringComparison.Ordinal);
        AssertHandEdit(fixedText, "snippets/quoted_review.md", "body", "A10-039",
            "the template reads deliverable.review_summary.content at line 1, which a 31 deliverable doesn't read; rewrite it.");
        var line = MigrateNodesTests.Md(fixedText, "snippets/quoted_review.md").Body;
        Assert.Contains("[{{ deliverable.review_summary.content }}]", line, StringComparison.Ordinal);

        // Both are refused by the 31 checker: the snippet placed in a deliverable (S05-022), and the macro body (S05-020).
        var inSnippet = Assert.Single(RefusalsOfDeliverable("{{ input.consult_draft.content }}\n\n{{ snippet.echo.content }}", line));
        Assert.Equal(("S05-022", "deliverables/note.md"), (inSnippet.Id, inSnippet.Path));
        var inMacro = Assert.Single(TemplateFixture.RefusalsOf(TemplateFixture.Package()
            .WithMacro("review_note", MigrateNodesTests.Md(result, "macros/quoted_review.md").Body.Replace("review_summary", "note", StringComparison.Ordinal))
            .WithAgent("draft", "Draft the plan for Margaret Okafor.\n{{ macro.review_note }}\n")));
        Assert.Equal(("S05-020", "macros/review_note.md"), (inMacro.Id, inMacro.Path));
    }

    [Fact]
    public void ADeliverableMacroThatIsNoSingleMacro_Is30sToRefuse()
    {
        // A placement naming no single macro (a folder's item, a folder) would be A10-047 at 31; 30 refuses it first, so no
        // package reaches that path. This pins 30's refusal.
        foreach (var id in new[] { "notes/red_flags", "notes" })
        {
            var (json, files) = ByteEquivalenceFixtures.ReadPackage("example-macros");
            var root = JsonNode.Parse(json)!.AsObject();
            root["deliverables"]![0]!["macros"]!.AsArray().Add(new JsonObject { ["id"] = id, ["at"] = "append" });
            var result = WorkflowPackageMigration30To31.Migrate(root.ToJsonString(), files, MigrationFixture.Options("example-macros"));
            Assert.False(result.Succeeded);
            Assert.Contains(result.Refusals, r => r.StartsWith("At specVersion 30: Deliverable 'consult_note' macro '" + id + "'", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ASnippetCopy_TakesAFreeId_AndCarriesTheMacroBodysHandEdits()
    {
        // example-macros-warnings' kept_note has a body if (A10-035); placed by a prompt too, it stays a macro, and its
        // deliverable reads a copy of its body, which holds the same text: the edit is said on each file.
        var warned = MigrateNodesTests.Migrated("example-macros-warnings", _ => { }, files => files["prompts/summary.md"] += "{{ macro.kept_note.prompt }}\n");
        var onMacro = Assert.Single(warned.HandEdits, e => e.StartsWith("Hand edit needed in macros/kept_note.md, body: ", StringComparison.Ordinal));
        Assert.Single(warned.HandEdits, e => e == onMacro.Replace("macros/kept_note.md", "snippets/kept_note.md", StringComparison.Ordinal));
        // The copy only a deliverable reads is the macro's body with 30's trailing trim (Task 15 fix round 1, R5); the
        // macro file keeps it whole (31 drops its one final line ending).
        Assert.Equal(MigrateNodesTests.Md(warned, "macros/kept_note.md").Body.TrimEnd(), MigrateNodesTests.Md(warned, "snippets/kept_note.md").Body);
        Assert.NotEqual(MigrateNodesTests.Md(warned, "macros/kept_note.md").Body, MigrateNodesTests.Md(warned, "snippets/kept_note.md").Body);

        // A snippet that already has the macro's id keeps it; the copy takes <id>_snippet, said with A10-048.
        var taken = MigrateNodesTests.Migrated("example-macros",
            root => root["snippets"]!.AsArray().Add((JsonNode)"kept_note"),
            files =>
            {
                files["prompts/summary.md"] += "{{ macro.kept_note.prompt }}\n{{ snippet.kept_note.content }}\n";
                files["snippets/kept_note.md"] = "{\n  \"label\": \"Kept snippet\"\n}\nA snippet of Dr A. Sample's own.\n";
            });
        Assert.Contains("\n\n{{ snippet.kept_note_snippet.content }}\n\n", Template(taken, "consult_note"), StringComparison.Ordinal);
        Assert.Equal("A snippet of Dr A. Sample's own.\n", MigrateNodesTests.Md(taken, "snippets/kept_note.md").Body);
        Assert.Equal(MigrateNodesTests.Md(taken, "macros/kept_note.md").Body.TrimEnd(), MigrateNodesTests.Md(taken, "snippets/kept_note_snippet.md").Body);
        var notice = "Macro 'kept_note' is read on deliverables as the snippet 'kept_note_snippet': a snippet 'kept_note' already exists.";
        Assert.Contains(notice, taken.Notices);
        V31Contract.AssertSentence(new SourceRefusal("A10-048", notice, ""));
    }

    [Theory]
    [MemberData(nameof(MigrateLayoutTests.Packages), MemberType = typeof(MigrateLayoutTests))]
    public void EveryDeliverableFile_PassesItsShape(string package)
    {
        var result = MigrateNodesTests.Migrated(package);
        var (json30, _) = ByteEquivalenceFixtures.ReadPackage(package);
        var ids = JsonNode.Parse(json30)!["deliverables"]!.AsArray().Select(d => (string)d!["id"]!).ToList();
        var refusals = new List<SourceRefusal>();

        var index = MigrateNodesTests.Json(result, "deliverables/index.json");
        SourceMembers.Check(index, SourceShapes.DeliverablesIndex, SourceShapes.DeliverablesIndex.Name, "deliverables/index.json", refusals);
        Assert.Equal(SourceShapes.DeliverablesIndex.Members.Select(s => s.Name), index.Select(m => m.Key));
        Assert.Equal(ids, index["items"]!.AsArray().Select(i => (string)i!));

        foreach (var id in ids)
        {
            var path = $"deliverables/{id}.md";
            var (header, body) = MigrateNodesTests.Md(result, path);
            SourceMembers.Check(header, SourceShapes.Deliverable, SourceShapes.Deliverable.Name, path, refusals);
            Assert.Equal(SourceShapes.Deliverable.Members.Select(s => s.Name), header.Select(m => m.Key));
            Assert.Equal("rendered", (string?)header["kind"]);
            Assert.Equal("markdown_block", (string?)header["format"]);
            Assert.Contains((string?)header["whenEmpty"], new[] { "release", "hold", "fail" });
            Assert.True(header["check"] is null || ((string?)header["check"])!.StartsWith("check.", StringComparison.Ordinal));
            Assert.False(string.IsNullOrWhiteSpace(body), $"{path}: the template is empty.");
            Assert.Equal($"deliverable.{id}", result.Keys[$"deliverable.{id}"]);
        }

        Assert.Equal(ids.Count, result.Files.Keys.Count(k => k.StartsWith("deliverables/", StringComparison.Ordinal) && k.EndsWith(".md", StringComparison.Ordinal)));
        Assert.Empty(refusals);
    }

    /// <summary>
    /// Part 05 § 8.3 (the macro's format follows its placers'), with R9: the macros <c>example-macros</c>' <c>summary</c>
    /// places (in its prompt, or through the snippet <c>style</c>) take its format, <c>markdown_block</c> while
    /// <c>consult_note</c> composes it, <c>plaintext</c> when only a prompt reads it.
    /// </summary>
    [Fact]
    public void AMacroPlacedOnAComposedNode_TakesItsFormat()
    {
        string[] placedBySummary = ["preamble", "sign_off", "wrap_up", "house_style"];
        var composed = MigrateNodesTests.Migrated("example-macros");
        Assert.Equal("markdown_block", (string?)MigrateNodesTests.Md(composed, "nodes/summary.md").Header["format"]);
        foreach (var macro in placedBySummary)
            Assert.True("markdown_block" == (string?)MigrateNodesTests.Md(composed, $"macros/{macro}.md").Header["format"], $"{macro} is not markdown_block.");

        // 30 refuses a node that feeds no deliverable, so the patient line's prompt reads the summary instead.
        var uncomposed = MigrateNodesTests.Migrated("example-macros", root =>
            root["deliverables"]![0]!["sources"]!.AsArray().Remove(root["deliverables"]![0]!["sources"]!.AsArray().Single(s => (string?)s == "node.summary")),
            files => files["prompts/patient_line.md"] += "\n{{ node.summary.content }}\n");
        Assert.Equal("plaintext", (string?)MigrateNodesTests.Md(uncomposed, "nodes/summary.md").Header["format"]);
        foreach (var macro in placedBySummary)
            Assert.True("plaintext" == (string?)MigrateNodesTests.Md(uncomposed, $"macros/{macro}.md").Header["format"], $"{macro} is not plaintext.");

        // A macro placed by a composed agent and an uncomposed one is plaintext (escaped in the Markdown agent): counselling,
        // placed by standing_draft (composed by consult_note) and patient_line, once patient_letter no longer composes it.
        Assert.Equal("markdown_block", (string?)MigrateNodesTests.Md(composed, "macros/counselling.md").Header["format"]);
        var mixed = MigrateNodesTests.Migrated("example-macros", root =>
            root["deliverables"]!.AsArray().Remove(root["deliverables"]!.AsArray().OfType<JsonObject>().Single(d => (string?)d["id"] == "patient_letter")),
            files => files["prompts/summary.md"] += "\n{{ node.patient_line.content }}\n");
        Assert.Equal("markdown_block", (string?)MigrateNodesTests.Md(mixed, "nodes/standing_sections/standing_draft.md").Header["format"]);
        Assert.Equal("plaintext", (string?)MigrateNodesTests.Md(mixed, "nodes/patient_line.md").Header["format"]);
        Assert.Equal("plaintext", (string?)MigrateNodesTests.Md(mixed, "macros/counselling.md").Header["format"]);
    }

    /// <summary>
    /// D20 gives no union of composed and uncomposed prose: 30 composes only a top-level node (a source inside a flow is
    /// refused, "name node.&lt;owner&gt;"), so a flow is composed with every leaf its <c>ret</c>s reach, or none is.
    /// <c>composer-sources</c>' <c>tone_case</c> is one <c>markdown_block</c> while <c>tone_line</c> composes it, one
    /// <c>plaintext</c> when only a prompt reads it, never an <c>anyOf</c>; both publish with no refusal but the package's two hand edits.
    /// </summary>
    [Fact]
    public void AFlowOfComposedAndUncomposedProse_IsNotAUnion()
    {
        var (json, files) = ByteEquivalenceFixtures.ReadPackage("composer-sources");
        var inside = JsonNode.Parse(json)!.AsObject();
        inside["deliverables"]!.AsArray().OfType<JsonObject>().Single(d => (string?)d["id"] == "single")["sources"] = new JsonArray("node.formal_line");
        var refused = WorkflowPackageMigration30To31.Migrate(inside.ToJsonString(), files, MigrationFixture.Options("composer-sources"));
        Assert.False(refused.Succeeded);
        Assert.Contains(refused.Refusals, r => r.StartsWith("At specVersion 30: ", StringComparison.Ordinal) && r.Contains("source node.formal_line runs inside", StringComparison.Ordinal));

        var composed = MigrateNodesTests.Migrated("composer-sources");
        // 30 refuses a node that feeds no deliverable, so the opening's prompt reads the case instead.
        var uncomposed = MigrateNodesTests.Migrated("composer-sources", root =>
            root["deliverables"]!.AsArray().Remove(root["deliverables"]!.AsArray().OfType<JsonObject>().Single(d => (string?)d["id"] == "tone_line")),
            files => files["prompts/opening.md"] += "\n{{ node.tone_case.content }}\n");
        foreach (var (result, format) in new[] { (composed, "markdown_block"), (uncomposed, "plaintext") })
        {
            Assert.Equal(JsonValue.Create(format).ToJsonString(), MigrateNodesTests.Json(result, "nodes/tone_case/index.json")["format"]!.ToJsonString());
            Assert.Equal(format, (string?)MigrateNodesTests.Md(result, "nodes/tone_case/formal_line.md").Header["format"]);
            Assert.Equal(format, (string?)MigrateNodesTests.Md(result, "nodes/tone_case/plain_line.md").Header["format"]);
            // The package's two hand edits (topic_tally's expression, draft_summary's body if) are refused at publish where
            // they sit; nothing else is, the case and its leaves least of all.
            Assert.All(result.HandEdits, e => Assert.Matches("^Hand edit needed in nodes/(topic_tally.md, expression|refine/draft_summary.md, body): ", e));
            Assert.All(MigratedPackagesTests.Publish(result), r => Assert.True(r.Path is "nodes/topic_tally.md" or "nodes/refine/draft_summary.md", $"{r.Id} {r.Path}: {r.Sentence}"));
        }
    }

    /// <summary>
    /// D20 with an <c>if</c> or <c>case</c> whose leaves mix 30 prose with a <c>let</c> of <c>text</c>, a <c>rendered</c>
    /// node or <c>agent_plaintext</c> (each stays <c>plaintext</c>): composer-sources' <c>tone_case</c> with
    /// <c>plain_line</c> made one. 30 refuses a deliverable that names such a flow as its source (its branches disagree; "a
    /// deliverable composes prose"). Read only by a prompt, the flow composes nothing, its prose leaf stays <c>plaintext</c>
    /// and its format is the one <c>plaintext</c>; publish refuses nothing but the package's two hand edits. A loop or a
    /// while over such a flow is <see cref="MixedBranches_UnderAFor_KeepTheirUnion_UnderAWhile_AreAHandEdit"/>.
    /// </summary>
    [Theory]
    [InlineData("let")]
    [InlineData("rendered")]
    [InlineData("agent_plaintext")]
    public void AFlowMixingProseWithALetOrARenderedLeaf_IsNeverAUnion(string kind)
    {
        var (json, read) = ByteEquivalenceFixtures.ReadPackage("composer-sources");
        var options = MigrateNodesTests.WithAgentPlaintext("composer-sources");

        var composedRoot = JsonNode.Parse(json)!.AsObject();
        var composedFiles = new Dictionary<string, string>(read, StringComparer.Ordinal);
        MakePlainLeaf(composedRoot, composedFiles, "plain_line", kind);
        var refused = WorkflowPackageMigration30To31.Migrate(composedRoot.ToJsonString(), composedFiles, options);
        Assert.False(refused.Succeeded);
        Assert.Contains(refused.Refusals, r => r.StartsWith("At specVersion 30: Deliverable 'tone_line' source node.tone_case returns ", StringComparison.Ordinal)
            && r.EndsWith("; a deliverable composes prose.", StringComparison.Ordinal));

        var root = JsonNode.Parse(json)!.AsObject();
        var files = new Dictionary<string, string>(read, StringComparer.Ordinal);
        MakePlainLeaf(root, files, "plain_line", kind);
        root["deliverables"]!.AsArray().Remove(root["deliverables"]!.AsArray().OfType<JsonObject>().Single(d => (string?)d["id"] == "tone_line"));
        files["prompts/opening.md"] += "\n{{ node.tone_case.content }}\n";
        var promptOnly = WorkflowPackageMigration30To31.Migrate(root.ToJsonString(), files, options);
        Assert.True(promptOnly.Succeeded, string.Join("\n", promptOnly.Refusals));

        Assert.Equal("\"plaintext\"", MigrateNodesTests.Json(promptOnly, "nodes/tone_case/index.json")["format"]!.ToJsonString());
        Assert.Equal("plaintext", (string?)MigrateNodesTests.Md(promptOnly, "nodes/tone_case/formal_line.md").Header["format"]);
        Assert.Equal("plaintext", (string?)MigrateNodesTests.Md(promptOnly, "nodes/tone_case/plain_line.md").Header["format"]);
        Assert.Equal(KindOf(kind), (string?)MigrateNodesTests.Md(promptOnly, "nodes/tone_case/plain_line.md").Header["kind"]);
        Assert.All(promptOnly.HandEdits, e => Assert.Matches("^Hand edit needed in nodes/(topic_tally.md, expression|refine/draft_summary.md, body): ", e));
        Assert.All(MigratedPackagesTests.Publish(promptOnly), r => Assert.True(r.Path is "nodes/topic_tally.md" or "nodes/refine/draft_summary.md", $"{r.Id} {r.Path}: {r.Sentence}"));
    }

    /// <summary>
    /// R9, D20 (fix round 2 of Task 21), D23 (Task 24's fix round 1): 30 does accept a deliverable composing a <c>for</c>
    /// or a <c>while</c> whose <c>ret</c> is an <c>if</c> mixing 30 prose with a <c>let</c> of text, a <c>rendered</c> node
    /// or <c>agent_plaintext</c> (only a flow named as a source is held to one format). The <c>if</c> is
    /// <c>markdown_block | plaintext</c>. Under a <c>for</c> the deliverable reads the loop's <c>.content</c>, the standard
    /// rendering typed <c>markdown_block</c>, which is never escaped: the union is written as derived, with no hand edit,
    /// publish refuses nothing and the read lists no escape, so 30's bytes are kept. Under a <c>while</c> the deliverable
    /// reads its last pass's <c>ret</c>'s <c>.content</c>, the union, which it would escape as one string, the prose's
    /// Markdown with it, so it is a hand edit (A10-053): the <c>if</c> is written <c>markdown_block</c>, which publish
    /// refuses against the union its branches give (S02-015), and the while keeps the format derived from the branches.
    /// composer-sources without its two hand edits (<c>refine</c>, <c>topic_tally</c>), so the edit is the package's only one.
    /// </summary>
    [Theory]
    [InlineData("for", "let")]
    [InlineData("for", "rendered")]
    [InlineData("for", "agent_plaintext")]
    [InlineData("while", "let")]
    [InlineData("while", "rendered")]
    [InlineData("while", "agent_plaintext")]
    public void MixedBranches_UnderAFor_KeepTheirUnion_UnderAWhile_AreAHandEdit(string shape, string kind)
    {
        var (json, read) = ByteEquivalenceFixtures.ReadPackage("composer-sources");
        var root = JsonNode.Parse(json)!.AsObject();
        var files = new Dictionary<string, string>(read, StringComparer.Ordinal);
        foreach (var id in new[] { "refine", "draft_summary", "judge", "topic_tally" })
            root["nodes"]!.AsArray().Remove(MigrateNodesTests.Node(root, id));
        foreach (var deliverable in root["deliverables"]!.AsArray().OfType<JsonObject>().Where(d => (string?)d["id"] is "summary" or "tally" or "summary_then_opening").ToList())
            root["deliverables"]!.AsArray().Remove(deliverable);

        string gate, flow, prose;
        root["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "left_out", ["label"] = "A left-out line" });
        MakePlainLeaf(root, files, "left_out", kind);
        if (shape == "for")
        {
            // topic_loop's if gains an else returning the plain-text leaf.
            MigrateNodesTests.Node(root, "topic_gate")["else"] = new JsonObject { ["nodes"] = new JsonArray("left_out"), ["ret"] = "node.left_out" };
            (gate, flow, prose) = ("nodes/topic_loop/topic_gate/index.json", "nodes/topic_loop/index.json", "nodes/topic_loop/topic_gate/draft_topic.md");
        }
        else
        {
            root["nodes"]!.AsArray().Add(new JsonObject
            {
                ["id"] = "redo", ["label"] = "Redoing the line", ["while"] = "input.redraft == true", ["checkFirst"] = true, ["max"] = 2,
                ["nodes"] = new JsonArray("redo_gate"), ["ret"] = "node.redo_gate",
            });
            root["nodes"]!.AsArray().Add(new JsonObject
            {
                ["id"] = "redo_gate", ["label"] = "Drafted or left out", ["if"] = "input.include_plan == true",
                ["then"] = new JsonObject { ["nodes"] = new JsonArray("redo_line"), ["ret"] = "node.redo_line" },
                ["else"] = new JsonObject { ["nodes"] = new JsonArray("left_out"), ["ret"] = "node.left_out" },
            });
            root["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "redo_line", ["label"] = "Redrafting the line", ["format"] = "prose" });
            files["prompts/redo_line.md"] = "Write one line from this draft.\n{{ input.consult_draft }}\nTopics so far: {{ node.topic_loop.content }}\n";
            root["deliverables"]!.AsArray().Add(new JsonObject { ["id"] = "redo", ["label"] = "A while over an if", ["sources"] = new JsonArray("node.redo") });
            (gate, flow, prose) = ("nodes/redo/redo_gate/index.json", "nodes/redo/index.json", "nodes/redo/redo_gate/redo_line.md");
        }

        var result = WorkflowPackageMigration30To31.Migrate(root.ToJsonString(), files, MigrateNodesTests.WithAgentPlaintext("composer-sources"));
        Assert.True(result.Succeeded, string.Join("\n", result.Refusals));

        const string union = """{"anyOf":[{"$ref":"markdown_block"},{"$ref":"plaintext"}]}""";
        Assert.Equal("markdown_block", (string?)MigrateNodesTests.Md(result, prose).Header["format"]);
        Assert.Equal("plaintext", (string?)MigrateNodesTests.Md(result, gate.Replace("index.json", "left_out.md", StringComparison.Ordinal)).Header["format"]);
        Assert.Equal(shape == "for" ? $$"""{"type":"array","items":{{union}}}""" : union, MigrateNodesTests.Json(result, flow)["format"]!.ToJsonString());

        if (shape == "for")
        {
            // under a for: the union as derived, no hand edit, nothing refused, and no escape on the loop's read
            Assert.Equal(union, MigrateNodesTests.Json(result, gate)["format"]!.ToJsonString());
            Assert.Empty(result.HandEdits);
            var check = SourceCheck.Run(MigratedPackagesTests.Upload(result), V31Contract.PublishedTypes());
            Assert.True(check.Refusals.Count == 0, string.Join("\n", check.Refusals.Select(r => $"{r.Id} {r.Path}: {r.Sentence}")));
            Assert.Empty(MigratedPackagesTests.Publish(result));
            Assert.StartsWith("{{ node.topic_loop.content | string.strip }}", Template(result, "topics"), StringComparison.Ordinal);
            Assert.Empty(check.Templates!.Escapes("deliverable.topics"));
            return;
        }

        Assert.Equal("\"markdown_block\"", MigrateNodesTests.Json(result, gate)["format"]!.ToJsonString());
        var reason = $"the if 'redo_gate' that a deliverable composes returns Markdown from one branch and plain text from another, "
            + "which the deliverable would escape as one string; give its branches one text format, then write that format here and on each flow that returns this one.";
        AssertHandEdit(result, gate, "format", "A10-053", reason);
        Assert.Single(result.HandEdits);
        Assert.Contains(MigratedPackagesTests.Publish(result), r => r.Id == "S02-015" && r.Path == gate);
    }

    /// <summary>
    /// Final review A5 (R9, D20, D23): a directly composed <c>if</c> (a while's <c>ret</c>) whose branches are a <c>for</c>
    /// and a plain-text leaf is <c>[list, plaintext]</c>, a union with no <c>markdown_block</c> member, but the deliverable
    /// would escape the loop's Markdown as one string. A <c>for</c>'s member counts as <c>markdown_block</c> (its
    /// <c>.content</c> is, D23), so it is A10-053, a hand edit.
    /// </summary>
    [Fact]
    public void AComposedIfWhoseBranchesAreAForAndPlainText_IsAHandEdit()
    {
        var (json, read) = ByteEquivalenceFixtures.ReadPackage("composer-sources");
        var root = JsonNode.Parse(json)!.AsObject();
        var files = new Dictionary<string, string>(read, StringComparer.Ordinal);
        foreach (var id in new[] { "refine", "draft_summary", "judge", "topic_tally" })
            root["nodes"]!.AsArray().Remove(MigrateNodesTests.Node(root, id));
        foreach (var deliverable in root["deliverables"]!.AsArray().OfType<JsonObject>().Where(d => (string?)d["id"] is "summary" or "tally" or "summary_then_opening").ToList())
            root["deliverables"]!.AsArray().Remove(deliverable);

        root["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "left_out", ["label"] = "A left-out line" });
        MakePlainLeaf(root, files, "left_out", "let");
        root["nodes"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "redo", ["label"] = "Redoing the topics", ["while"] = "input.redraft == true", ["checkFirst"] = true, ["max"] = 2,
            ["nodes"] = new JsonArray("redo_gate"), ["ret"] = "node.redo_gate",
        });
        root["nodes"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "redo_gate", ["label"] = "Drafted or left out", ["if"] = "input.include_plan == true",
            ["then"] = new JsonObject { ["nodes"] = new JsonArray("redo_topics"), ["ret"] = "node.redo_topics" },
            ["else"] = new JsonObject { ["nodes"] = new JsonArray("left_out"), ["ret"] = "node.left_out" },
        });
        root["nodes"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "redo_topics", ["label"] = "Redrafting the topics", ["for"] = "item in input.topics",
            ["nodes"] = new JsonArray("redo_topic"), ["ret"] = "node.redo_topic",
        });
        root["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "redo_topic", ["label"] = "Redrafting a topic", ["format"] = "prose" });
        files["prompts/redo_topic.md"] = "Write one paragraph on the topic \"{{ item.value.title }}\" from this draft.\n{{ input.consult_draft }}\n";
        root["deliverables"]!.AsArray().Add(new JsonObject { ["id"] = "redo", ["label"] = "A while over an if", ["sources"] = new JsonArray("node.redo") });

        var result = WorkflowPackageMigration30To31.Migrate(root.ToJsonString(), files, MigrateNodesTests.WithAgentPlaintext("composer-sources"));
        Assert.True(result.Succeeded, string.Join("\n", result.Refusals));

        const string gate = "nodes/redo/redo_gate/index.json";
        Assert.Equal("\"markdown_block\"", MigrateNodesTests.Json(result, gate)["format"]!.ToJsonString());
        var reason = "the if 'redo_gate' that a deliverable composes returns Markdown from one branch and plain text from another, "
            + "which the deliverable would escape as one string; give its branches one text format, then write that format here and on each flow that returns this one.";
        AssertHandEdit(result, gate, "format", "A10-053", reason);
        Assert.Single(result.HandEdits);
        Assert.NotEmpty(MigratedPackagesTests.Publish(result));
    }

    /// <summary>
    /// D8, D23 (Task 24): a migrated deliverable over a <c>for</c> reads the loop's <c>.content</c>, which is 30's blocks
    /// typed <c>markdown_block</c> (part 04 § 2.5, part 05 § 5.3), so the <c>markdown_block</c> deliverable writes it as it
    /// is: composer-sources' <c>topics</c> (a for over an input), <c>sections</c> (a for over a node's list, with a for
    /// inside) and <c>topics_then_opening</c> (a for, then prose) publish with no escape. composer-sources without its two
    /// hand edits (<c>refine</c>, <c>topic_tally</c>), so publish refuses nothing.
    /// </summary>
    [Fact]
    public void AMigratedLoopSource_ComposesAs30Did()
    {
        var (json, read) = ByteEquivalenceFixtures.ReadPackage("composer-sources");
        var root = JsonNode.Parse(json)!.AsObject();
        foreach (var id in new[] { "refine", "draft_summary", "judge", "topic_tally" })
            root["nodes"]!.AsArray().Remove(MigrateNodesTests.Node(root, id));
        foreach (var deliverable in root["deliverables"]!.AsArray().OfType<JsonObject>().Where(d => (string?)d["id"] is "summary" or "tally" or "summary_then_opening").ToList())
            root["deliverables"]!.AsArray().Remove(deliverable);

        var result = WorkflowPackageMigration30To31.Migrate(root.ToJsonString(), read, MigrationFixture.Options("composer-sources"));
        Assert.True(result.Succeeded, string.Join("\n", result.Refusals));
        Assert.Empty(result.HandEdits);
        var check = SourceCheck.Run(MigratedPackagesTests.Upload(result), V31Contract.PublishedTypes());
        Assert.True(check.Refusals.Count == 0, string.Join("\n", check.Refusals.Select(r => $"{r.Id} {r.Path}: {r.Sentence}")));

        foreach (var (id, loop) in new[] { ("topics", "topic_loop"), ("sections", "section_loop"), ("topics_then_opening", "topic_loop") })
        {
            Assert.StartsWith($"{{{{ node.{loop}.content | string.strip }}}}", Template(result, id), StringComparison.Ordinal);
            Assert.Empty(check.Templates!.Escapes($"deliverable.{id}"));
        }
    }

    /// <summary>The 30 node <paramref name="id"/> of <paramref name="root"/> made a plain-text leaf: a <c>let</c> of text, a <c>rendered</c> node or an <c>agent_plaintext</c> agent.</summary>
    private static void MakePlainLeaf(JsonObject root, Dictionary<string, string> files, string id, string kind)
    {
        var leaf = MigrateNodesTests.Node(root, id);
        leaf.Remove("format");
        switch (kind)
        {
            case "let":
                leaf["let"] = "\"A plain line.\"";
                leaf["type"] = "text";
                leaf["onInvalid"] = "fail";
                files.Remove($"prompts/{id}.md");
                break;
            case "rendered":
                leaf["format"] = "rendered";
                files[$"prompts/{id}.md"] = "A plain line for {{ input.consult_draft }}.\n";
                break;
            default:
                leaf["format"] = "agent_plaintext";
                files[$"prompts/{id}.md"] = "Write one plain line from this draft.\n{{ input.consult_draft }}\n";
                break;
        }
    }

    private static string KindOf(string kind) => kind switch { "let" => "computed", "rendered" => "rendered", _ => "agent" };
}
