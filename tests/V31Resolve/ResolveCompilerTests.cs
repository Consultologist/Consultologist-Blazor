using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Resolve;

/// <summary>
/// #1502 (specVersion 31, area 05): <c>resolve.json</c> (part 07 §§ 1, 2, 3; D3 to D8) compiled from a checked source:
/// every top-level member of § 2.2, the flat maps of § 2.5 with every header member as written and the derived ones,
/// the graph of § 2.7, <c>requires</c> and <c>needs</c> of § 3, canonical by RFC 8785 and the same bytes for the same
/// source. Part 07 § 2.9's example is held member by member where it is not elided.
/// </summary>
public class ResolveCompilerTests
{
    private const string Scriban = "7.2.5";

    private static readonly string[] Maps = ["nodes", "checks", "renderers", "snippets", "macros", "deliverables"];

    private static SourceCheckResult Check(Dictionary<string, byte[]> files, ITemplateReads? templates = null)
    {
        var check = SourceCheck.Run(files, ResolveFixture.Published(), templates);
        Assert.True(check.Refusals.Count == 0, ResolveFixture.Said(check));
        return check;
    }

    private static ResolveResult Compile(Dictionary<string, byte[]> files, ITemplateReads? templates = null, PublishLimits? limits = null)
    {
        var result = ResolveCompiler.Compile(Check(files, templates), ResolveFixture.Published(), Scriban, limits);
        Assert.True(result.Refusals.Count == 0, string.Join(" | ", result.Refusals.Select(r => $"{r.Id} {r.Sentence}")));
        Assert.NotNull(result.Document);
        return result;
    }

    private static string[] Keys(JsonNode? node) => node!.AsObject().Select(p => p.Key).ToArray();

    private static string[] Sorted(IEnumerable<string> keys) => keys.Order(StringComparer.Ordinal).ToArray();

    private static string[] Required(JsonNode schema) => schema["required"]!.AsArray().Select(r => (string)r!).ToArray();

    private static string[] Strings(JsonNode? node) => node!.AsArray().Select(n => (string)n!).ToArray();

    private static string Canonical(JsonNode? node) => CanonicalJson.Write(node);

    private static List<(string From, string To, string Kind)> Edges(JsonObject document) =>
        document["graph"]!["edges"]!.AsArray()
            .Select(e => ((string)e!["from"]!, (string)e["to"]!, (string)e["kind"]!)).ToList();

    /// <summary>
    /// What every body of the example reads, as § 2.9 shows it: the deliverable also reads the loop's <c>.items</c>, as
    /// the example's <c>reads</c> and its edge from <c>node.sections</c> say.
    /// </summary>
    private static StubTemplates ExampleTemplates() => new(new Dictionary<string, string[]>
    {
        ["node.plan"] = ["input.consult_draft.content"],
        ["node.sections.outline"] = ["item.value.title"],
        ["node.sections.paragraphs.draft_paragraph"] = ["input.consult_draft.content", "item.parent.value.title", "item.value", "item.id"],
        ["node.refine.draft_summary"] = ["pass.previous.node.judge.content"],
        ["node.refine.judge"] = ["node.draft_summary.content"],
        ["deliverable.note"] = ["node.refine.content", "node.sections.items"],
    }, everyBody: false);

    /// <summary><see cref="ResolveFixture.NestedNote"/> with § 2.9's manifest and the plan's <c>onInvalid</c>.</summary>
    private static Dictionary<string, byte[]> Example() =>
        ResolveFixture.NestedNote()
            .WithJson("manifest.json", m =>
            {
                m["name"] = "example-nested-note";
                m["title"] = "Nested note demo";
                m["description"] = new JsonArray("A demonstration of nesting, computed values and while. Not intended for clinical use.");
                m["tags"] = new JsonArray("demo", "nested");
            })
            .With("nodes/plan.md", Md(
                SourceFixture.Node("agent", "Planning the sections", "\"section_plan\"", false, true,
                    "\"if\": null, \"onInvalid\": \"fail\", \"failIfEmpty\": null, \"parts\": null, \"ordered\": false"),
                "You are planning the sections of a consultation note.\n\n{{ input.consult_draft.content }}\n"));

    /// <summary>Every flat-map entry of the document: the six maps, and each function's own nodes and definitions.</summary>
    private static IEnumerable<(string Key, JsonObject Entry)> Entries(JsonObject document)
    {
        foreach (var map in Maps)
            foreach (var (key, entry) in document[map]!.AsObject())
                yield return (key, entry!.AsObject());
        foreach (var (_, function) in document["functions"]!.AsObject())
        {
            foreach (var map in new[] { "nodes", "renderers", "snippets", "macros" })
                foreach (var (key, entry) in function![map]!.AsObject())
                    yield return (key, entry!.AsObject());
        }
    }

    // ---- The members --------------------------------------------------------------------------

    public static TheoryData<string> Packages() => new() { "NestedNote", "Conditional", "Functions" };

    private static Dictionary<string, byte[]> Package(string name) => name switch
    {
        "NestedNote" => ResolveFixture.NestedNote(),
        "Conditional" => ResolveFixture.Conditional(),
        _ => ResolveFixture.Functions(),
    };

    [Theory]
    [MemberData(nameof(Packages))]
    public void TopLevel_HasEveryMember(string name)
    {
        var document = Compile(Package(name)).Document!;
        var schema = V31Contract.Json("schemas", "resolve.schema.json");
        var defs = schema["$defs"]!;

        Assert.Equal(Sorted(Required(schema)), Sorted(Keys(document)));
        Assert.Equal(Sorted(Required(defs["requires"]!)), Sorted(Keys(document["requires"])));
        Assert.Equal(Sorted(Required(defs["needs"]!)), Sorted(Keys(document["needs"])));
        Assert.Equal(Sorted(Required(defs["package"]!)), Sorted(Keys(document["package"])));
        Assert.Equal(["edges", "order"], Sorted(Keys(document["graph"])));
        foreach (var (_, input) in document["inputs"]!.AsObject())
            Assert.Equal(Sorted(Required(defs["input"]!)), Sorted(Keys(input)));
        foreach (var (_, function) in document["functions"]!.AsObject())
            Assert.All(Required(defs["function"]!), member => Assert.True(function!.AsObject().ContainsKey(member), member));

        var entries = Entries(document).ToList();
        Assert.NotEmpty(entries);
        foreach (var (key, entry) in entries)
        {
            foreach (var member in Required(defs["entry"]!))
                Assert.True(entry.ContainsKey(member), $"{key} has no {member}");
            Assert.Equal(key.Split('.')[^1], (string?)entry["id"]);
        }
    }

    [Fact]
    public void Entries_HoldEveryHeaderMember()
    {
        var check = Check(ResolveFixture.Functions(), StubTemplates.Empty());
        var document = ResolveCompiler.Compile(check, ResolveFixture.Published(), Scriban).Document!;
        var package = check.Parse.Package!;

        foreach (var node in ResolveFixture.Walk(package.Nodes.Nodes).Concat(ResolveFixture.Walk(package.Checks.Nodes)))
        {
            var map = node.Key.StartsWith("check.", StringComparison.Ordinal) ? "checks" : "nodes";
            var entry = document[map]![node.Key]!.AsObject();
            foreach (var (member, _) in node.Header)
                Assert.True(entry.ContainsKey(member), $"{node.Key} has no {member}");
            Assert.True(entry.ContainsKey("body") && entry.ContainsKey("bodyHash") && entry.ContainsKey("renders")
                && entry.ContainsKey("runSpots") && entry.ContainsKey("formatHash") && entry.ContainsKey("typeHash"), node.Key);
        }

        // An agent's own members; a body-less kind's body is null.
        var agent = document["nodes"]!["node.extract_input_terms"]!.AsObject();
        Assert.Equal("{}", Canonical(agent["parts"]));
        Assert.Equal("[]", Canonical(agent["places"]));
        Assert.Equal(CanonicalJson.HashText((string)agent["body"]!), (string?)agent["bodyHash"]);
        var call = document["nodes"]!["node.standards.section"]!.AsObject();
        PresentAndNull(call, "body");
        PresentAndNull(call, "bodyHash");
        Assert.False(call.ContainsKey("parts"));
    }

    // ---- The example ---------------------------------------------------------------------------

    [Fact]
    public void NestedNote_IsTheSpecsExample()
    {
        var document = Compile(Example(), ExampleTemplates()).Document!;

        Assert.Equal(1, (int)document["documentVersion"]!);
        Assert.Equal(31, (int)document["specVersion"]!);
        Assert.Equal("{\"name\":\"example-nested-note\",\"version\":\"v2026.12.1\"}", Canonical(document["package"]));
        Assert.Equal("Nested note demo", (string?)document["title"]);
        Assert.Equal("A demonstration of nesting, computed values and while. Not intended for clinical use.", (string?)document["description"]);
        Assert.Equal(["demo", "nested"], Strings(document["tags"]));
        Assert.Matches("^sha256:[0-9a-f]{64}$", (string)document["sourceHash"]!);
        Assert.Equal("lock.json", (string?)document["lock"]);

        // requires: § 2.9's, except what the fixture adds (draft_paragraph's if calls blank) and the registry's agents.
        var requires = document["requires"]!;
        Assert.Equal("31", (string?)requires["spec"]);
        Assert.Equal("^1.0.0", (string?)requires["evaluator"]);
        Assert.Equal("^7.2.5", (string?)requires["scriban"]);
        Assert.Equal("{\"blank\":\"^1.0.0\",\"size\":\"^1.0.0\"}", Canonical(requires["operations"]));
        Assert.Equal(["agent", "computed", "for", "while"], Strings(requires["kinds"]));
        Assert.Equal(["parallel"], Strings(requires["features"]));
        Assert.Equal("{\"custom-json\":\"1\",\"test-json\":\"48\"}", Canonical(requires["agents"]));
        Assert.Equal("{\"profile\":[]}", Canonical(document["needs"]));

        Assert.Equal(
            "{\"consult_draft\":{\"channel\":null,\"description\":null,\"format\":{\"$ref\":\"#/$defs/markdown_block\"},"
            + "\"label\":\"Consult draft\",\"required\":true}}",
            Canonical(document["inputs"]));
        Assert.Equal(["consult_draft"], Strings(document["inputOrder"]));
        Assert.Equal(["markdown_block", "number", "outline", "plaintext", "section_plan", "verdict"], Sorted(Keys(document["formats"]!["$defs"])));
        Assert.Equal("urn:consultologist:package:example-nested-note@v2026.12.1:format:section_plan",
            (string?)document["formats"]!["$defs"]!["section_plan"]!["$id"]);
        Assert.Equal(Sorted(Keys(document["formats"]!["$defs"])), Sorted(Keys(document["formatHashes"])));
        Assert.Equal(Sorted(Keys(document["formats"]!["$defs"])), Sorted(Keys(document["typeHashes"])));
        Assert.Equal(["outline", "plaintext", "section_plan", "verdict"], Sorted(Keys(document["bindings"])));
        Assert.Equal("{\"generated\":{\"agentName\":\"custom-json\",\"agentVersion\":1,\"via\":\"custom\"}}", Canonical(document["bindings"]!["section_plan"]));

        var nodes = document["nodes"]!.AsObject();
        Assert.Equal(
            ["node.plan", "node.section_count", "node.sections", "node.sections.outline", "node.sections.paragraphs",
             "node.sections.paragraphs.draft_paragraph", "node.refine", "node.refine.draft_summary", "node.refine.judge"],
            Keys(nodes));

        var plan = nodes["node.plan"]!.AsObject();
        Assert.Equal(
            "{\"body\":\"You are planning the sections of a consultation note.\\n\\n{{ input.consult_draft.content }}\\n\","
            + $"\"bodyHash\":\"{CanonicalJson.HashText("You are planning the sections of a consultation note.\n\n{{ input.consult_draft.content }}\n")}\","
            + "\"format\":{\"$ref\":\"#/$defs/section_plan\"},\"id\":\"plan\",\"if\":null,\"kind\":\"agent\","
            + "\"label\":\"Planning the sections\",\"modelDerived\":true,\"onInvalid\":\"fail\",\"parent\":null,\"parts\":{},"
            + "\"places\":[],\"producer\":\"generated\",\"profileCarrying\":false,\"reads\":[\"input.consult_draft.content\"],"
            + "\"renders\":[],\"reproducible\":false,\"runDependent\":false,\"runSpots\":[]}",
            Canonical(Without(plan, "formatHash", "typeHash", "description", "failIfEmpty", "ordered")));
        Assert.Equal((string?)document["formatHashes"]!["section_plan"], (string?)plan["formatHash"]);
        Assert.Equal((string?)document["typeHashes"]!["section_plan"], (string?)plan["typeHash"]);

        Assert.Equal(
            "{\"body\":null,\"bodyHash\":null,\"expression\":{\"size\":[{\"var\":\"node.plan.value.sections\"}]},"
            + "\"format\":{\"$ref\":\"#/$defs/number\"},\"id\":\"section_count\",\"if\":null,\"kind\":\"computed\","
            + "\"label\":\"Counting the sections\",\"modelDerived\":true,\"parent\":null,\"producer\":\"computed\","
            + "\"profileCarrying\":false,\"reads\":[\"node.plan.value\"],\"renders\":[],\"reproducible\":true,"
            + "\"runDependent\":false,\"runSpots\":[]}",
            Canonical(Without(nodes["node.section_count"]!.AsObject(), "formatHash", "typeHash", "description")));
        Assert.Equal((string?)document["formatHashes"]!["number"], (string?)nodes["node.section_count"]!["formatHash"]);

        var sections = nodes["node.sections"]!.AsObject();
        Assert.Equal("sections", (string?)sections["id"]);
        Assert.Equal("Sections", (string?)sections["label"]);
        Assert.Equal("for", (string?)sections["kind"]);
        Assert.Equal("computed", (string?)sections["producer"]);
        PresentAndNull(sections, "parent");
        Assert.False((bool)sections["reproducible"]!);
        Assert.Equal("node.plan.value.sections", (string?)sections["in"]);
        Assert.Equal("title", (string?)sections["itemLabel"]);
        Assert.True((bool)sections["parallel"]!);
        PresentAndNull(sections, "maxParallel");
        PresentAndNull(sections, "until");
        Assert.Equal(["node.sections.outline", "node.sections.paragraphs"], Strings(sections["nodes"]));
        Assert.Equal("node.sections.paragraphs", (string?)sections["ret"]);
        Assert.Equal(["node.plan.value"], Strings(sections["reads"]));
        Assert.Equal((true, false, false), ((bool)sections["modelDerived"]!, (bool)sections["runDependent"]!, (bool)sections["profileCarrying"]!));
        PresentAndNull(sections, "body");
        PresentAndNull(sections, "bodyHash");
        Assert.Equal("[]", Canonical(sections["renders"]));
        Assert.Equal("[]", Canonical(sections["runSpots"]));

        Assert.Equal("node.sections", (string?)nodes["node.sections.outline"]!["parent"]);
        var paragraphs = nodes["node.sections.paragraphs"]!;
        Assert.Equal(("for", "computed", "node.sections"), ((string)paragraphs["kind"]!, (string)paragraphs["producer"]!, (string)paragraphs["parent"]!));
        Assert.Equal((true, false, false), ((bool)paragraphs["modelDerived"]!, (bool)paragraphs["runDependent"]!, (bool)paragraphs["profileCarrying"]!));
        Assert.Equal("node.sections.outline.value.points", (string?)paragraphs["in"]);
        Assert.Equal(["node.sections.paragraphs.draft_paragraph"], Strings(paragraphs["nodes"]));
        Assert.Equal("node.sections.paragraphs.draft_paragraph", (string?)paragraphs["ret"]);

        var draft = nodes["node.sections.paragraphs.draft_paragraph"]!;
        Assert.Equal(("agent", "generated", "node.sections.paragraphs"), ((string)draft["kind"]!, (string)draft["producer"]!, (string)draft["parent"]!));
        Assert.Equal("{\"$ref\":\"#/$defs/plaintext\"}", Canonical(draft["format"]));
        Assert.False((bool)draft["reproducible"]!);
        Assert.Equal((true, false, false), ((bool)draft["modelDerived"]!, (bool)draft["runDependent"]!, (bool)draft["profileCarrying"]!));
        Assert.Equal(["input.consult_draft.content", "item.id", "item.parent.value.title", "item.value"], Strings(draft["reads"]));

        var refine = nodes["node.refine"]!;
        Assert.Equal(("refine", "Refining the summary", "while", "computed"),
            ((string)refine["id"]!, (string)refine["label"]!, (string)refine["kind"]!, (string)refine["producer"]!));
        PresentAndNull(refine, "parent");
        Assert.Equal((true, false, false), ((bool)refine["modelDerived"]!, (bool)refine["runDependent"]!, (bool)refine["profileCarrying"]!));
        Assert.Equal("{\"!==\":[{\"var\":\"node.refine.judge.value.verdict\"},\"good\"]}", Canonical(refine["while"]));
        Assert.Equal(3, (int)refine["max"]!);
        Assert.Equal(["node.refine.draft_summary", "node.refine.judge"], Strings(refine["nodes"]));
        Assert.Equal("node.refine.draft_summary", (string?)refine["ret"]);

        Assert.Equal("{}", Canonical(document["checks"]));
        Assert.Equal("{}", Canonical(document["functions"]));
        Assert.Equal("{}", Canonical(document["renderers"]));
        Assert.Equal("{}", Canonical(document["snippets"]));
        Assert.Equal("{}", Canonical(document["macros"]));

        Assert.Equal(["deliverable.note"], Keys(document["deliverables"]));
        var note = document["deliverables"]!["deliverable.note"]!;
        Assert.Equal(("note", "Nested consultation note", "rendered"), ((string)note["id"]!, (string)note["label"]!, (string)note["kind"]!));
        Assert.Equal("{\"$ref\":\"#/$defs/markdown_block\"}", Canonical(note["format"]));
        PresentAndNull(note, "if");
        PresentAndNull(note, "check");
        Assert.Equal(["node.refine.content", "node.sections.items"], Strings(note["reads"]));
        Assert.Equal((true, false, false), ((bool)note["modelDerived"]!, (bool)note["runDependent"]!, (bool)note["profileCarrying"]!));
        Assert.Equal("## Summary\n\n{{ node.refine.content }}\n", (string?)note["body"]);
        Assert.Equal("[]", Canonical(note["renders"]));
        Assert.Equal("[]", Canonical(note["runSpots"]));

        var edges = Edges(document);
        Assert.Contains(("input.consult_draft", "node.plan", "reads"), edges);
        Assert.Contains(("node.plan", "node.section_count", "reads"), edges);
        Assert.Contains(("node.plan", "node.sections", "for"), edges);
        Assert.Contains(("node.sections", "deliverable.note", "reads"), edges);
        Assert.Contains(("node.refine", "deliverable.note", "reads"), edges);
        Assert.Contains(("node.sections.paragraphs", "node.sections", "ret"), edges);
        Assert.Contains(("node.refine.judge", "node.refine", "if"), edges);
        Assert.Equal(edges.Count, edges.Distinct().Count());

        Assert.Equal(
            ["input.consult_draft", "node.plan", "node.section_count", "node.sections", "node.sections.outline",
             "node.sections.paragraphs", "node.sections.paragraphs.draft_paragraph", "node.refine",
             "node.refine.draft_summary", "node.refine.judge", "deliverable.note"],
            Strings(document["graph"]!["order"]));
        Assert.Equal("[]", Canonical(document["run"]));
        Assert.Equal("[]", Canonical(document["warnings"]));
    }

    /// <summary><paramref name="member"/> is written, and is null (part 07 § 1: every member is present).</summary>
    private static void PresentAndNull(JsonNode? entry, string member)
    {
        Assert.True(entry!.AsObject().ContainsKey(member), $"no {member}");
        Assert.Null(entry[member]);
    }

    private static JsonObject Without(JsonObject entry, params string[] members)
    {
        var copy = entry.DeepClone().AsObject();
        foreach (var member in members)
            Assert.True(copy.Remove(member), $"no {member}");
        return copy;
    }

    // ---- Functions -----------------------------------------------------------------------------

    [Fact]
    public void Functions_AreSealedMaps()
    {
        var document = Compile(ResolveFixture.Functions(), StubTemplates.Empty()).Document!;

        Assert.Equal(["draft_section"], Keys(document["functions"]));
        var function = document["functions"]!["draft_section"]!.AsObject();
        Assert.Equal(["node.standard_draft", "node.patient_draft", "node.apply_instructions"], Keys(function["nodes"]));
        Assert.Equal(["node.standard_draft", "node.patient_draft", "node.apply_instructions"], Strings(function["body"]));
        Assert.Equal("node.apply_instructions", (string?)function["ret"]);
        Assert.Equal(["standard", "draft"], Strings(function["inputOrder"]));
        Assert.Equal("{\"$ref\":\"#/$defs/standard_section\"}", Canonical(function["inputs"]!["standard"]!["format"]));
        Assert.Equal("{\"$ref\":\"#/$defs/plaintext\"}", Canonical(function["format"]));
        Assert.Equal((string?)document["formatHashes"]!["plaintext"], (string?)function["formatHash"]);
        Assert.False((bool)function["reproducible"]!);
        Assert.Equal("Draft a section", (string?)function["label"]);
        Assert.True(function.ContainsKey("uses") && function.ContainsKey("description"));
        Assert.Equal("{}", Canonical(function["renderers"]));
        Assert.Equal("{}", Canonical(function["snippets"]));
        Assert.Equal("{}", Canonical(function["macros"]));

        var standard = function["nodes"]!["node.standard_draft"]!;
        PresentAndNull(standard, "parent");
        Assert.Equal("{\"!\":[{\"blank\":[{\"var\":\"param.standard.value.title\"}]}]}", Canonical(standard["if"]));

        // The call is written with its function, and its arguments as written roots.
        var call = document["nodes"]!["node.standards.section"]!;
        Assert.Equal("draft_section", (string?)call["function"]);
        Assert.Equal("{\"draft\":\"input.consult_draft.content\",\"standard\":\"item\"}", Canonical(call["with"]));
        Assert.Equal("snippet.standards", (string?)document["nodes"]!["node.standards"]!["in"]);
        Assert.Equal("node.standards.items", (string?)document["checks"]!["check.section_coverage"]!["in"]);
        Assert.Equal(["snippet.standards", "snippet.standards.assessment_plan", "snippet.standards.hpi"], Sorted(Keys(document["snippets"])));

        var edges = Edges(document);
        Assert.Contains(("function.draft_section", "node.standards.section", "calls"), edges);
        Assert.Contains(("node.standards", "check.section_coverage", "aligned"), edges);
        Assert.Contains(("node.standards.section", "node.standards", "ret"), edges);
        Assert.Contains(("input.consult_draft", "node.standards.section", "reads"), edges);
        Assert.Contains(("check.section_coverage", "check.all_sections", "reads"), edges);
        Assert.DoesNotContain(edges, e => e.From.StartsWith("param.", StringComparison.Ordinal) || e.To == "node.apply_instructions");

        var order = Strings(document["graph"]!["order"]).ToList();
        Assert.DoesNotContain(order, k => k.StartsWith("function.", StringComparison.Ordinal));
        Assert.True(order.IndexOf("node.extract_note_terms") > order.IndexOf("node.standards"));
        Assert.True(order.IndexOf("check.section_coverage") > order.IndexOf("node.extract_note_terms"));
        Assert.True(order.IndexOf("check.all_sections") > order.IndexOf("check.section_coverage"));
        Assert.Equal(order.Count, order.Distinct().Count());
    }

    [Fact]
    public void DerivedFrom_IsTheManifests()
    {
        var copied = Minimal().WithJson("manifest.json", m => m["derivedFrom"] = "example-base@v2026.11.1");
        var document = Compile(copied).Document!;
        Assert.Equal("example-base@v2026.11.1", (string?)document["derivedFrom"]);

        var original = Compile(Minimal()).Document!;
        PresentAndNull(original, "derivedFrom");
    }

    [Fact]
    public void Branches_KeepTheirWrittenShape_WithKeys()
    {
        var nodes = Compile(ResolveFixture.Conditional()).Document!["nodes"]!;

        var route = nodes["node.sections_draft.route"]!;
        Assert.Equal("case", (string?)route["kind"]);
        Assert.Equal("item.value.kind", (string?)route["case"]);
        Assert.Equal(
            "[{\"nodes\":[\"node.sections_draft.route.narrative_draft\"],\"ret\":\"node.sections_draft.route.narrative_draft\",\"when\":[\"narrative\"]},"
            + "{\"nodes\":[\"node.sections_draft.route.table_draft\"],\"ret\":\"node.sections_draft.route.table_draft\",\"when\":[\"table\"]},"
            + "{\"nodes\":[],\"ret\":null,\"when\":[\"omit\"]}]",
            Canonical(route["branches"]));
        PresentAndNull(route, "else");
        PresentAndNull(route, "ret");
        Assert.Equal(["node.sections_draft.route.narrative_draft", "node.sections_draft.route.table_draft"], Strings(route["nodes"]));
        Assert.Equal("node.sections_draft", (string?)route["parent"]);

        var applies = nodes["node.standing_sections.applies"]!;
        Assert.Equal("if", (string?)applies["kind"]);
        Assert.Equal(
            "{\"nodes\":[\"node.standing_sections.applies.standing_draft\"],\"ret\":\"node.standing_sections.applies.standing_draft\"}",
            Canonical(applies["then"]));
        PresentAndNull(applies, "else");
        PresentAndNull(applies, "ret");
        Assert.Equal(["node.standing_sections.applies.standing_draft"], Strings(applies["nodes"]));

        // A case whose subject reads a node is written with the node's key (D3): route, in the while, on the judge's verdict.
        var keyed = ResolveFixture.NestedNote()
            .WithJson("nodes/refine/index.json", w => w["nodes"]!.AsArray().Add("route"))
            .With("nodes/refine/route/index.json", Json(SourceFixture.Node("case", "Routing on the verdict", "\"plaintext\"", false, true,
                "\"case\": \"node.judge.value.verdict\", "
                + "\"branches\": [{ \"when\": [\"good\"], \"nodes\": [\"keep\"], \"ret\": \"node.refine.route.keep\" }], "
                + "\"else\": { \"nodes\": [\"redo\"], \"ret\": \"node.refine.route.redo\" }")))
            .With("nodes/refine/route/keep.md", Agent("Keeping the summary", "\"plaintext\"", "Keep the summary for Dr A. Sample.\n"))
            .With("nodes/refine/route/redo.md", Agent("Redoing the summary", "\"plaintext\"", "Redo the summary for Dr A. Sample.\n"));
        var routed = Compile(keyed).Document!["nodes"]!["node.refine.route"]!;
        Assert.Equal("node.refine.judge.value.verdict", (string?)routed["case"]);
        Assert.Equal("{\"nodes\":[\"node.refine.route.redo\"],\"ret\":\"node.refine.route.redo\"}", Canonical(routed["else"]));
        Assert.Equal(["node.refine.route.keep", "node.refine.route.redo"], Strings(routed["nodes"]));
        PresentAndNull(routed, "ret");
    }

    [Fact]
    public void Deliverables_CarryTheirFoldersDelivery()
    {
        // Delivery is declared on folders only, with no inheritance (part 06 § 8); the engine loads only resolve.json
        // (part 07 § 1), so each deliverable carries its own folder's delivery and bundle.
        var files = Minimal()
            .With("deliverables/index.json", Index(["note", "patient"], extra: ", \"delivery\": [\"emr\"], \"bundle\": null"))
            .With("deliverables/patient/index.json", Index(["letter"], extra: ", \"delivery\": [\"email\", \"pdf\"], \"bundle\": \"zip\""))
            .With("deliverables/patient/letter.md", Md(Deliverable("Letter for Margaret Okafor", "\"plaintext\"", "null", "hold"), "{{ node.plan.content }}\n"));
        var deliverables = Compile(files).Document!["deliverables"]!;

        Assert.Equal(["deliverable.note", "deliverable.patient.letter"], Keys(deliverables));
        var note = deliverables["deliverable.note"]!;
        Assert.Equal(["emr"], Strings(note["delivery"]));
        PresentAndNull(note, "bundle");
        var letter = deliverables["deliverable.patient.letter"]!;
        Assert.Equal(["email", "pdf"], Strings(letter["delivery"]));
        Assert.Equal("zip", (string?)letter["bundle"]);

        var silent = Compile(Minimal()).Document!["deliverables"]!["deliverable.note"]!;
        Assert.Equal("[]", Canonical(silent["delivery"]));
        PresentAndNull(silent, "bundle");
    }

    // ---- requires ------------------------------------------------------------------------------

    [Fact]
    public void Requires_IsMinimal()
    {
        var minimal = Compile(Minimal()).Document!["requires"]!;
        Assert.Equal(["agent"], Strings(minimal["kinds"]));
        Assert.Equal("{}", Canonical(minimal["operations"]));
        Assert.Equal("[]", Canonical(minimal["features"]));
        Assert.Equal("{\"test-json\":\"48\"}", Canonical(minimal["agents"]));

        var functions = Compile(ResolveFixture.Functions()).Document!["requires"]!;
        Assert.Equal(["agent", "computed", "for", "call"], Strings(functions["kinds"]));
        Assert.Equal(["parallel", "check-loops"], Strings(functions["features"]));
        Assert.Equal("{\"all_pass\":\"^1.0.0\",\"blank\":\"^1.0.0\",\"terms_subset\":\"^1.0.0\"}", Canonical(functions["operations"]));

        var sequential = ResolveFixture.NestedNote().WithJson("nodes/sections/index.json", h => h["parallel"] = false)
            .WithJson("nodes/sections/paragraphs/index.json", h => h["parallel"] = false);
        var nested = Compile(sequential).Document!["requires"]!;
        Assert.Equal("[]", Canonical(nested["features"]));
        Assert.DoesNotContain("call", Strings(nested["kinds"]));

        // The constants' order, as published.
        var constants = V31Contract.Json("constants.json");
        var kinds = Strings(constants["kinds"]);
        var features = Strings(constants["features"]);
        Assert.Equal(kinds.Where(Strings(functions["kinds"]).Contains), Strings(functions["kinds"]));
        Assert.Equal(features.Where(Strings(functions["features"]).Contains), Strings(functions["features"]));
    }

    [Fact]
    public void Requires_TakesPartsAndTheSeamsFeatures()
    {
        var files = Minimal().WithJson("nodes/plan.md", h =>
            h["parts"] = JsonNode.Parse("{ \"closing\": { \"if\": { \"!\": [{ \"blank\": [{ \"var\": \"input.consult_draft.content\" }] }] }, \"text\": [\"Close politely.\"] } }"));
        var document = Compile(files).Document!;
        Assert.Equal(["parts"], Strings(document["requires"]!["features"]));
        Assert.Equal(["closing"], Keys(document["nodes"]!["node.plan"]!["parts"]));

        var bodies = new Dictionary<string, TemplateReads>
        {
            ["node.plan"] = new(["input.consult_draft.content"], ["macro.sig"], [], [new TemplateRunSpot(1, "3:1", "run.date")], false),
        };
        var seam = new StubTemplates(new Dictionary<string, string[]>(), everyBody: true, bodies);
        var withSeam = ResolveCompiler.Compile(SourceCheck.Run(Minimal(), ResolveFixture.Published(), seam), ResolveFixture.Published(), Scriban);
        Assert.Equal(["macro-markers", "run-spots"], Strings(withSeam.Document!["requires"]!["features"]));
        Assert.Equal(["macro.sig"], Strings(withSeam.Document["nodes"]!["node.plan"]!["places"]));
        Assert.Equal("[{\"at\":\"3:1\",\"reads\":\"run.date\",\"site\":1}]", Canonical(withSeam.Document["nodes"]!["node.plan"]!["runSpots"]));
    }

    // ---- Determinism ---------------------------------------------------------------------------

    [Fact]
    public void Compile_IsDeterministic()
    {
        var once = Compile(ResolveFixture.Functions(), StubTemplates.Empty());
        var twice = Compile(ResolveFixture.Functions(), StubTemplates.Empty());
        Assert.Equal(once.Canonical, twice.Canonical);
        Assert.Equal(once.Hash, twice.Hash);
        Assert.Equal(CanonicalJson.HashText(once.Canonical!), once.Hash);
        Assert.Equal(CanonicalJson.Write(once.Document), once.Canonical);

        var reversed = new Dictionary<string, byte[]>();
        foreach (var (path, bytes) in ResolveFixture.Functions().Reverse())
            reversed[path] = bytes;
        Assert.Equal(once.Canonical, Compile(reversed, StubTemplates.Empty()).Canonical);

        var crlf = ResolveFixture.Functions();
        foreach (var path in crlf.Keys.ToList())
            crlf.With(path, Text(crlf, path).Replace("\n", "\r\n", StringComparison.Ordinal));
        Assert.Equal(once.Canonical, Compile(crlf, StubTemplates.Empty()).Canonical);

        Assert.Equal(once.Canonical, CanonicalJson.Write(JsonNode.Parse(once.Canonical!)));
        Assert.DoesNotMatch("(?i)\"[a-z]*(time|date)[a-z]*\":", once.Canonical!);
        Assert.DoesNotMatch("\\d{4}-\\d{2}-\\d{2}T\\d{2}:", once.Canonical!);
        foreach (var (member, value) in once.Document!)
        {
            if (member is not ("description" or "derivedFrom"))
                Assert.True(value != null, $"{member} is null");
        }
    }

    // ---- Refused, incomplete -------------------------------------------------------------------

    [Fact]
    public void ARefusedPackage_CompilesNothing()
    {
        var refused = SourceCheck.Run(Minimal().Without("nodes/plan.md"), ResolveFixture.Published());
        Assert.NotEmpty(refused.Refusals);
        var result = ResolveCompiler.Compile(refused, ResolveFixture.Published(), Scriban);
        Assert.Equal(refused.Refusals, result.Refusals);
        Assert.Null(result.Document);
        Assert.Null(result.Canonical);
        Assert.Null(result.Hash);
        Assert.Empty(result.Warnings);
        Assert.False(result.Complete);

        // A05-004: a function's own format bundled under a key the package already uses; the check says it, and nothing is compiled.
        var colliding = ResolveFixture.Functions()
            .With("functions/draft_section/formats/index.json", Index(["excerpt"]))
            .With("functions/draft_section/formats/excerpt.json", Json(
                "{ \"$schema\": \"urn:consultologist:spec:31:dialect\", \"type\": \"object\", \"required\": [\"text\"], "
                + "\"additionalProperties\": false, \"properties\": { \"text\": { \"$ref\": \"plaintext\" } } }"))
            .WithJson("functions/draft_section/standard_draft.md", h => h["format"] = "excerpt")
            .With("formats/index.json", Index(["standard_section", "draft_section__excerpt"]))
            .With("formats/draft_section__excerpt.json", Json(
                "{ \"$schema\": \"urn:consultologist:spec:31:dialect\", \"type\": \"object\", \"required\": [\"note\"], "
                + "\"additionalProperties\": false, \"properties\": { \"note\": { \"$ref\": \"plaintext\" } } }"))
            .WithJson("nodes/plan.md", h => h["format"] = "draft_section__excerpt");
        var check = SourceCheck.Run(colliding, ResolveFixture.Published());
        var refusal = Assert.Single(check.Refusals);
        Assert.Equal(("A05-004", "functions/draft_section/formats/excerpt.json"), (refusal.Id, refusal.Path));
        V31Contract.AssertSentence(refusal);
        var collided = ResolveCompiler.Compile(check, ResolveFixture.Published(), Scriban);
        Assert.Equal(check.Refusals, collided.Refusals);
        Assert.Null(collided.Document);
    }

    [Fact]
    public void WithoutTheSeam_IsNotComplete()
    {
        var without = Compile(ResolveFixture.NestedNote());
        Assert.False(without.Complete);
        Assert.NotNull(without.Canonical);
        var plan = without.Document!["nodes"]!["node.plan"]!;
        Assert.Equal("[]", Canonical(plan["places"]));
        Assert.Equal("[]", Canonical(plan["renders"]));
        Assert.Equal("[]", Canonical(plan["runSpots"]));
        Assert.Equal("[]", Canonical(plan["reads"]));

        var with = Compile(ResolveFixture.NestedNote(), ExampleTemplates());
        Assert.True(with.Complete);
        Assert.NotEqual(without.Hash, with.Hash);
    }

    // ---- Warnings ------------------------------------------------------------------------------

    [Fact]
    public void Warnings_AreOrderedByPath_NullFirst()
    {
        var files = ResolveFixture.Functions()
            .WithJson("functions/draft_section/index.json", f => f["uses"]!["formats"]!.AsArray().Add("number"));
        var result = Compile(files, limits: new PublishLimits(1, 200, 500));
        var warnings = result.Document!["warnings"]!.AsArray();
        Assert.Equal(result.Warnings.Count, warnings.Count);
        Assert.All(warnings, w => Assert.Equal(["at", "sentence"], Sorted(Keys(w))));

        var at = warnings.Select(w => (string?)w!["at"]).ToList();
        Assert.Contains(null, at);
        Assert.Contains("functions/draft_section/index.json", at);
        Assert.Equal(at.OrderBy(a => a == null ? 0 : 1).ThenBy(a => a, StringComparer.Ordinal), at);
        Assert.Contains(warnings, w => ((string)w!["sentence"]!).StartsWith("Function 'draft_section' lists format 'number'", StringComparison.Ordinal));

        Assert.DoesNotContain(Compile(files).Document!["warnings"]!.AsArray(), w => w!["at"] == null);
    }

    [Fact]
    public void Compile_TakesNoNull()
    {
        var check = Check(Minimal());
        Assert.Throws<ArgumentNullException>(() => ResolveCompiler.Compile(null!, ResolveFixture.Published(), Scriban));
        Assert.Throws<ArgumentNullException>(() => ResolveCompiler.Compile(check, null!, Scriban));
        Assert.Throws<ArgumentNullException>(() => ResolveCompiler.Compile(check, ResolveFixture.Published(), null!));
    }
}
