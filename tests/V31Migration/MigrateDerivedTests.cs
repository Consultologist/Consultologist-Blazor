using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using static Consultologist.Api.Tests.V31Migration.MigrateNodesTests;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 1.2 the node-headers row; part 02 §§ 1.1, 1.2, § 11's last bullet; part 04
/// § 4; D15): the derived-flags pass. Every node and check header writes the <c>reproducible</c>, <c>modelDerived</c>,
/// <c>runDependent</c> and <c>profileCarrying</c> the derivation gives, so the checker refuses none of them; a 30
/// <c>reproducible</c> claim that differs is listed (A10-010); a flow's format comes from its <c>ret</c>, and the loops of
/// part 04 § 2.8 are now the spec's JSON exactly. A hand edit the reader refuses leaves the placeholders; one the
/// expressions or the dialect refuse still lets the flags be derived.
/// </summary>
public class MigrateDerivedTests
{
    private static readonly string[] FlagRefusals = ["S03-036", "S04-015", "S04-016"];

    private static readonly string[] Flags = ["reproducible", "modelDerived", "runDependent", "profileCarrying"];

    [Theory]
    [MemberData(nameof(MigratedPackagesTests.Packages), MemberType = typeof(MigratedPackagesTests))]
    public void TheFlags_AreTheDerivedOnes(string package)
    {
        var result = MigratedPackagesTests.Migrated(package);
        var check = SourceCheck.Run(MigratedPackagesTests.Upload(result), V31Contract.PublishedTypes());

        Assert.DoesNotContain(check.Refusals, r => FlagRefusals.Contains(r.Id));
        if (result.HandEdits.Count > 0)
            return;

        // With no hand edit the checker derives every entry: each header says exactly what it derived.
        var derived = check.Derived!;
        var package31 = check.Parse.Package!;
        foreach (var node in ScopeTree.Walk(package31.Nodes.Nodes).Concat(ScopeTree.Walk(package31.Checks.Nodes)))
        {
            var entry = derived.Entries[node.Key];
            Assert.Equal(entry.Reproducible, node.Header["reproducible"]!.GetValue<bool>());
            Assert.Equal(entry.ModelDerived, node.Header["modelDerived"]!.GetValue<bool>());
            Assert.Equal(entry.RunDependent, node.Header["runDependent"]!.GetValue<bool>());
            Assert.Equal(entry.ProfileCarrying, node.Header["profileCarrying"]!.GetValue<bool>());
        }
    }

    [Fact]
    public void TheFlags_FollowWhatAValueIsMadeOf()
    {
        // Part 02 § 1.2: an agent is model-derived; a computed node over its output is too, and reproducible (§ 1.1); a loop
        // over it carries the flag out through its ret.
        var result = MigratedPackagesTests.Migrated("example-nested-note");

        AssertFlags(Md(result, "nodes/plan.md").Header, reproducible: false, modelDerived: true);
        AssertFlags(Md(result, "nodes/section_count.md").Header, reproducible: true, modelDerived: true);
        AssertFlags(Json(result, "nodes/sections/index.json"), reproducible: false, modelDerived: true);
        AssertFlags(Json(result, "nodes/refine/index.json"), reproducible: false, modelDerived: true);
    }

    [Fact]
    public void ACheck_IsDerivedToo()
    {
        // composer-gates' checks read agents' concept lists: model-derived, reproducible (a computed check).
        var result = MigratedPackagesTests.Migrated("composer-gates");

        AssertFlags(Md(result, "checks/note_coverage.md").Header, reproducible: true, modelDerived: true);
        AssertFlags(Md(result, "checks/copy_coverage.md").Header, reproducible: true, modelDerived: true);
    }

    [Fact]
    public void A30ClaimThatDiffers_IsListed()
    {
        // Part 02 § 11's last bullet: plan is an agent node, which reads a model's output by being one; its 30 claim
        // reproducible: true is not what its reads give (§ 1.1), so false is written and the claim is listed.
        var result = Migrated("example-nested-note", root => Node(root, "plan")["reproducible"] = true);

        Assert.False(Md(result, "nodes/plan.md").Header["reproducible"]!.GetValue<bool>());
        const string notice = "Node 'plan' declared reproducible true at 30; false is written, as its reads give it.";
        Assert.Equal([notice], result.Report.Reproducible);
        Assert.Single(result.Notices, n => n == notice);
        Assert.Empty(result.HandEdits.Where(e => !MigratedPackagesTests.PastTheInstanceLimit(e))); // #1508 (R11): nested-note's one edit, the instances limit
        V31Contract.AssertSentence(new SourceRefusal("A10-010", notice, ""));

        // A claim the derivation agrees with is not listed.
        var agrees = Migrated("example-nested-note", root => Node(root, "plan")["reproducible"] = false);
        Assert.Empty(agrees.Report.Reproducible);
        Assert.DoesNotContain(agrees.Notices, n => n.Contains("declared reproducible", StringComparison.Ordinal));
    }

    [Fact]
    public void AFlowsFormat_ComesFromItsRet()
    {
        // Part 04 § 2.8, now exactly: the format a list of the ret's (a list of lists for a loop around a loop), and the flags.
        var nested = MigratedPackagesTests.Migrated("example-nested-note");
        AssertExactly("""
            { "kind": "for", "producer": "computed", "label": "Sections", "description": null,
              "format": { "type": "array", "items": { "type": "array", "items": { "$ref": "markdown_block" } } },
              "reproducible": false, "modelDerived": true, "runDependent": false, "profileCarrying": false,
              "in": "node.plan.value.sections",
              "parallel": true, "maxParallel": null, "until": null, "max": null,
              "itemLabel": "title",
              "nodes": ["outline", "paragraphs"], "ret": "node.sections.paragraphs" }
            """, Json(nested, "nodes/sections/index.json"));
        AssertExactly("""
            { "kind": "for", "producer": "computed", "label": "Paragraphs", "description": null,
              "format": { "type": "array", "items": { "$ref": "markdown_block" } },
              "reproducible": false, "modelDerived": true, "runDependent": false, "profileCarrying": false,
              "in": "node.outline.value.points",
              "parallel": true, "maxParallel": null, "until": null, "max": null,
              "itemLabel": null,
              "nodes": ["draft_paragraph"], "ret": "node.sections.paragraphs.draft_paragraph" }
            """, Json(nested, "nodes/sections/paragraphs/index.json"));
        AssertExactly("""
            { "kind": "for", "producer": "computed", "label": "Sections — drafting each requested section by its kind", "description": null,
              "format": { "type": "array", "items": { "$ref": "markdown_block" } },
              "reproducible": false, "modelDerived": true, "runDependent": false, "profileCarrying": false,
              "in": "input.sections.value",
              "parallel": true, "maxParallel": null, "until": null, "max": null,
              "itemLabel": "title",
              "nodes": ["route"], "ret": "node.sections_draft.route" }
            """, Json(MigratedPackagesTests.Migrated("example-conditional-sections"), "nodes/sections_draft/index.json"));

        // test/general has hand edits elsewhere (its parts' conditions, refused by the expressions): its flags are still derived.
        AssertExactly("""
            { "kind": "for", "producer": "computed", "label": "Standards", "description": null,
              "format": { "type": "array", "items": { "$ref": "markdown_block" } },
              "reproducible": false, "modelDerived": true, "runDependent": false, "profileCarrying": false,
              "in": "snippet.standards",
              "parallel": true, "maxParallel": null, "until": null, "max": null,
              "itemLabel": "title",
              "nodes": ["section_instructions", "patient_section_draft", "standard_section_draft"],
              "ret": "node.standards.section_instructions" }
            """, Json(MigratedPackagesTests.Migrated("test/general"), "nodes/standards/index.json"));
    }

    [Fact]
    public void AHandEditTheDialectRefuses_StillLetsTheFlagsBeDerived()
    {
        // composer-gates' prompts read deliverables (A10-039, refused by the dialect): the bodies are unknown, and the
        // flags that are true whatever they read are written.
        var result = MigratedPackagesTests.Migrated("composer-gates");
        Assert.NotEmpty(result.HandEdits);

        AssertFlags(Md(result, "nodes/extract_note_terms.md").Header, reproducible: false, modelDerived: true);
        AssertFlags(Json(result, "nodes/problem_loop/index.json"), reproducible: false, modelDerived: true);
    }

    [Fact]
    public void ABodyTheDialectRefuses_LeavesTheOtherBodiesRead()
    {
        // example-macros: the dialect refuses header_line's case, the deliverable's fixed_text reads and several macros'
        // instructions, never summary.md itself. summary places sign_off, whose body reads profile.name (part 03 § 4.6):
        // it is profile-carrying (part 02 § 1.2), as the checker will hold it once the listed edits are made.
        var result = MigratedPackagesTests.Migrated("example-macros");
        Assert.NotEmpty(result.HandEdits);

        var summary = Md(result, "nodes/summary.md").Header;
        Assert.True(summary["profileCarrying"]!.GetValue<bool>());
        Assert.True(summary["modelDerived"]!.GetValue<bool>());
        Assert.True(Md(MigratedPackagesTests.Migrated("example-macros-warnings"), "nodes/summary.md").Header["profileCarrying"]!.GetValue<bool>());

        // sign_off's instruction is refused (A10-003), its text is not: summary is known, and nothing is said of its flags.
        Assert.DoesNotContain(result.Notices, n => n.StartsWith("nodes/summary.md: ", StringComparison.Ordinal));
    }

    [Fact]
    public void AHeaderMadeOfARefusedBody_SaysWhichFlagsAreNotKnown()
    {
        // composer-gates' extract_note_terms reads deliverable.note (A10-039, refused by the dialect): its own body is not
        // known, so its false flags are not known to be false, and the check reading it is in the same case.
        var result = MigratedPackagesTests.Migrated("composer-gates");

        const string notice = "nodes/extract_note_terms.md: runDependent and profileCarrying are written false without the bodies that need a hand edit; check them once the edits are made.";
        Assert.Single(result.Notices, n => n == notice);
        Assert.Contains("checks/note_coverage.md: runDependent and profileCarrying are written false without the bodies that need a hand edit; check them once the edits are made.", result.Notices);
        Assert.DoesNotContain(result.HandEdits, n => n == notice);
        V31Contract.AssertSentence(new SourceRefusal("A10-049", notice, ""));

        // A package whose bodies are all read says none.
        Assert.DoesNotContain(MigratedPackagesTests.Migrated("example-nested-note").Notices, n => n.Contains("written false without", StringComparison.Ordinal));
    }

    [Fact]
    public void AHandEditTheReaderRefuses_LeavesThePlaceholders()
    {
        // A check left in a flow's list (A10-046) names no file there, which the reader refuses (S01-009): nothing is
        // derived, and every header keeps Task 7's placeholders.
        var result = MigrateChecksTests.WithCheckInAnIf();

        AssertFlags(Md(result, "nodes/extract_note_terms.md").Header, reproducible: false, modelDerived: false);
        AssertFlags(Md(result, "checks/note_coverage.md").Header, reproducible: true, modelDerived: false);
    }

    private static void AssertFlags(JsonObject header, bool reproducible, bool modelDerived)
    {
        Assert.Equal(reproducible, header["reproducible"]!.GetValue<bool>());
        Assert.Equal(modelDerived, header["modelDerived"]!.GetValue<bool>());
        Assert.False(header["runDependent"]!.GetValue<bool>());
        Assert.False(header["profileCarrying"]!.GetValue<bool>());
    }

    /// <summary>Asserts <paramref name="actual"/> is <paramref name="expected"/>, every member equal (the flags among them), in <see cref="SourceShapes.FlowFor"/>' order.</summary>
    private static void AssertExactly(string expected, JsonObject actual)
    {
        var example = JsonNode.Parse(expected)!.AsObject();
        Assert.Equal(SourceShapes.FlowFor.Members.Select(m => m.Name), actual.Select(m => m.Key));
        Assert.Equal(example.Select(m => m.Key).Order(StringComparer.Ordinal), actual.Select(m => m.Key).Order(StringComparer.Ordinal));
        foreach (var (member, value) in example)
            Assert.True(JsonNode.DeepEquals(value, actual[member]), $"{member}: expected {value?.ToJsonString()}, was {actual[member]?.ToJsonString()}");
        Assert.All(Flags, flag => Assert.True(actual.ContainsKey(flag)));
    }
}
