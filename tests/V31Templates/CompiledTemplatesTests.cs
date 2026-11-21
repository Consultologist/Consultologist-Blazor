using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using Corvus.Text.Json.Validator;
using static Consultologist.Api.Tests.V31Templates.TemplateFixture;

namespace Consultologist.Api.Tests.V31Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; D2, D4, D6, D15; part 07 §§ 2.5, 3.1, 4; part 05 § 5.3): what the dialect adds to
/// <c>resolve.json</c> and <c>lock.json</c>: each templated entry's <c>placements</c> in order with each <c>if</c>
/// rooted for the entry's place, its run spots with the unit that holds each and its neutral form, a built-in renderer
/// used embedded and locked, the features that follow from what is placed, and the documents fitting the pinned schemas.
/// Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class CompiledTemplatesTests
{
    private static CompiledPackage Published(Dictionary<string, byte[]> files)
    {
        var (refusals, compiled) = CompiledPackage.Publish(files, ResolveFixture.Published(), CompiledGoldenTests.Fixed);
        Assert.True(refusals.Count == 0, Said(refusals));
        Assert.NotNull(compiled);
        Assert.True(compiled!.Complete);
        return compiled;
    }

    private static JsonObject Resolve(Dictionary<string, byte[]> files) => JsonNode.Parse(Published(files).Resolve)!.AsObject();

    /// <summary>
    /// The loop <c>sections</c> over the plan's sections, of the classifier <c>classify</c> and the agent <c>draft</c>,
    /// whose part <c>p</c> (decided on the classifier's reason) places the macro <c>m</c> of the folder <c>f</c>
    /// (decided on the classifier's answer).
    /// </summary>
    internal static Dictionary<string, byte[]> Placing() =>
        Package()
            .Listed("macros/index.json", "f")
            .With("macros/f/index.json", SourceFixture.Index(["m"], extra: ", \"if\": null"))
            .With("macros/f/m.md", SourceFixture.Md(
                "{ \"label\": \"Follow-up\", \"description\": null, "
                + "\"if\": { \"===\": [ { \"var\": \"node.classify.value.answer\" }, true ] }, \"format\": \"plaintext\", "
                + "\"instruction\": [\"Place the follow-up line alone after the plan:\", \"{{~ macro.f.m.content }}\"] }",
                "Follow up with Dr A. Sample in six weeks.\n"))
            .Listed("nodes/index.json", "sections")
            .With("nodes/sections/index.json", SourceFixture.Json(SourceFixture.Node("for", "Sections", SourceFixture.ArrayOf(Plain), false, true,
                "\"in\": \"node.plan.value.sections\", \"parallel\": true, \"maxParallel\": null, \"until\": null, \"max\": null, "
                + "\"itemLabel\": \"title\", \"nodes\": [\"classify\", \"draft\"], \"ret\": \"node.sections.draft\"")))
            .With("nodes/sections/classify.md", SourceFixture.Agent("Classifying the section", "\"judgement\"",
                "Does the section '{{ item.value.title }}' need a follow-up?\n"))
            .With("nodes/sections/draft.md", SourceFixture.Agent("Drafting the section", Plain,
                "Draft the section '{{ item.value.title }}'.\n{{~ part.p }}\n"))
            .WithJson("nodes/sections/draft.md", h => h["parts"] = JsonNode.Parse(
                "{ \"p\": { \"if\": { \"!\": [ { \"blank\": [ { \"var\": \"node.classify.value.reason\" } ] } ] }, "
                + "\"text\": [\"Close the section.\", \"{{~ macro.f.m }}\"] } }"));

    [Fact]
    public void AMacroBodysSnippets_ArePlacements_AfterTheMacro_WithinIt()
    {
        // Fix round 1 (I2; part 05 § 6.3): the snippets a macro's body places, and those they place, are listed after the
        // macro's own entry, within it, with each if rooted for the place; the instruction's snippet follows.
        var files = MacroBodySnippets();

        var placements = Resolve(files)["nodes"]!["node.sections.draft"]!["placements"]!;

        Assert.Equal(
            CanonicalJson.Write(JsonNode.Parse("""
                [
                  { "site": "macro.m", "within": null, "if": null, "plainConverts": [], "form": "inline" },
                  { "site": "snippet.sign", "within": "macro.m",
                    "if": { "!": [ { "blank": [ { "var": "node.sections.classify.value.reason" } ] } ] }, "plainConverts": [], "form": null },
                  { "site": "snippet.name", "within": "snippet.sign", "if": null, "plainConverts": [], "form": null },
                  { "site": "snippet.where", "within": "macro.m", "if": null, "plainConverts": [], "form": null }
                ]
                """)),
            CanonicalJson.Write(placements));
    }

    /// <summary>
    /// The loop <c>sections</c> of the classifier <c>classify</c> and the agent <c>draft</c> placing the macro <c>m</c>, whose
    /// body places the snippet <c>sign</c> (decided on the classifier's reason, and placing <c>name</c>, which reads the
    /// profile) and whose instruction places <c>where</c>.
    /// </summary>
    internal static Dictionary<string, byte[]> MacroBodySnippets() =>
        Package()
            .WithSnippet("where", "in the plan")
            .WithSnippet("name", "{{ profile.signature }}")
            .Listed("snippets/index.json", "sign")
            .With("snippets/sign.md", SourceFixture.Md(
                "{ \"label\": \"Signature\", \"description\": null, "
                + "\"if\": { \"!\": [ { \"blank\": [ { \"var\": \"node.classify.value.reason\" } ] } ] }, \"format\": \"plaintext\" }",
                "Signed, {{ snippet.name.content }}"))
            .WithMacroOf("m", "Reviewed. {{ snippet.sign.content }}\n", ["Place {{ macro.m.content }} {{ snippet.where.content }}."])
            .Listed("nodes/index.json", "sections")
            .With("nodes/sections/index.json", SourceFixture.Json(SourceFixture.Node("for", "Sections", SourceFixture.ArrayOf(Plain), false, true,
                "\"in\": \"node.plan.value.sections\", \"parallel\": true, \"maxParallel\": null, \"until\": null, \"max\": null, "
                + "\"itemLabel\": \"title\", \"nodes\": [\"classify\", \"draft\"], \"ret\": \"node.sections.draft\"")))
            .With("nodes/sections/classify.md", SourceFixture.Agent("Classifying the section", "\"judgement\"",
                "Does the section '{{ item.value.title }}' need a signature?\n"))
            .With("nodes/sections/draft.md", SourceFixture.Agent("Drafting the section", Plain,
                "Draft the section '{{ item.value.title }}'.\n{{~ macro.m }}\n"))
            .WithJson("nodes/sections/draft.md", h => h["profileCarrying"] = true)
            .WithJson("nodes/sections/index.json", h => h["profileCarrying"] = true);

    [Fact]
    public void Placements_AreInOrder_WithEachIfRooted()
    {
        var document = Resolve(Placing());
        var draft = document["nodes"]!["node.sections.draft"]!;

        Assert.Equal(
            CanonicalJson.Write(JsonNode.Parse("""
                [
                  { "site": "part.p", "within": null,
                    "if": { "!": [ { "blank": [ { "var": "node.sections.classify.value.reason" } ] } ] }, "plainConverts": [], "form": null },
                  { "site": "macro-folder.f", "within": "part.p", "if": null, "plainConverts": [], "form": null },
                  { "site": "macro.f.m", "within": "part.p",
                    "if": { "===": [ { "var": "node.sections.classify.value.answer" }, true ] }, "plainConverts": [], "form": "block" }
                ]
                """)),
            CanonicalJson.Write(draft["placements"]));
        Assert.Equal(new JsonObject().ToJsonString(), draft["names"]!.ToJsonString());
        Assert.Equal(["macro.f.m"], draft["places"]!.AsArray().Select(p => (string)p!));

        // What places nothing has none; the macro keeps its if as written.
        Assert.Equal("[]", document["nodes"]!["node.plan"]!["placements"]!.ToJsonString());
        Assert.Equal("[]", document["deliverables"]!["deliverable.note"]!["placements"]!.ToJsonString());
        Assert.Equal("{\"===\":[{\"var\":\"node.classify.value.answer\"},true]}", document["macros"]!["macro.f.m"]!["if"]!.ToJsonString());
    }

    [Fact]
    public void Placements_NameWhatEachLocalHeadIs()
    {
        var files = ResolveFixture.NestedNote();
        var judge = Resolve(files)["nodes"]!["node.refine.judge"]!;

        Assert.Equal("{\"node.draft_summary\":\"node.refine.draft_summary\"}", judge["names"]!.ToJsonString());
    }

    /// <summary>
    /// The agent <c>draft</c>: a run fact, a snippet holding another, a run-dependent rendered node and a run-dependent
    /// computed value, in that order.
    /// </summary>
    internal static Dictionary<string, byte[]> Spotted() =>
        Package()
            .WithComputed("today", "\"date\"", "{ \"var\": \"run.date\" }", runDependent: true)
            .WithRendered("stamp", "Seen {{ run.date | date.to_string \"%d %B %Y\" }}\n").RunDependent("nodes/stamp.md")
            .WithSnippet("when", "Run {{ run.id }}\n")
            .WithAgent("draft",
                "Dated {{ run.date | date.to_string \"%d %B %Y\" }}.\n{{~ snippet.when.content }}\n{{ node.stamp.content }}\n{{ node.today.value }}\n")
            .RunDependent("nodes/draft.md");

    [Fact]
    public void RunSpots_CarryUnitAndNeutral()
    {
        var document = Resolve(Spotted());

        Assert.Equal(
            CanonicalJson.Write(JsonNode.Parse("""
                [
                  { "site": 1, "at": "1:7", "reads": "run.date", "unit": null, "neutral": "marker" },
                  { "site": 2, "at": "1:5", "reads": "run.id", "unit": "snippet.when", "neutral": "marker" },
                  { "site": 3, "at": "3:1", "reads": "node.stamp.content", "unit": null, "neutral": "text" },
                  { "site": 4, "at": "4:1", "reads": "node.today.value", "unit": null, "neutral": "marker" }
                ]
                """)),
            CanonicalJson.Write(document["nodes"]!["node.draft"]!["runSpots"]));
        Assert.Equal(
            "[{\"at\":\"1:6\",\"neutral\":\"marker\",\"reads\":\"run.date\",\"site\":1,\"unit\":null}]",
            CanonicalJson.Write(document["nodes"]!["node.stamp"]!["runSpots"]));

        // Each spot is warned of, with its file.
        var warnings = document["warnings"]!.AsArray().Select(w => ((string)w!["sentence"]!, (string?)w["at"])).ToList();
        Assert.Contains(warnings, w => w.Item2 == "nodes/draft.md" && w.Item1.Contains("run.date", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Item2 == "snippets/when.md" && w.Item1.Contains("run.id", StringComparison.Ordinal));
    }

    [Fact]
    public void ARunDependentParameter_IsARunSpot()
    {
        // D4: a parameter some call binds to run.* is run-dependent; its read in the function's prompt is a spot.
        var files = ResolveFixture.Functions()
            .WithJson("nodes/standards/section.md", h => h["with"]!["draft"] = "run.date")
            .WithJson("functions/draft_section/index.json", f => f["inputs"]!["draft"]!["format"] = "date")
            .With("functions/draft_section/patient_draft.md", SourceFixture.Agent("Drafting from the patient's facts", Plain,
                "Write what the draft says about Margaret Okafor on this day.\n\n{{ param.draft.value }}\n"));
        foreach (var path in new[] { "nodes/standards/section.md", "nodes/standards/index.json", "nodes/extract_note_terms.md",
                     "functions/draft_section/patient_draft.md", "functions/draft_section/apply_instructions.md",
                     "checks/section_coverage/index.json", "checks/section_coverage/coverage.md", "checks/all_sections.md" })
            files.RunDependent(path);

        var draft = Resolve(files)["functions"]!["draft_section"]!["nodes"]!["node.patient_draft"]!;

        Assert.Equal(
            "[{\"at\":\"3:1\",\"neutral\":\"marker\",\"reads\":\"param.draft.value\",\"site\":1,\"unit\":null}]",
            CanonicalJson.Write(draft["runSpots"]));
    }

    [Fact]
    public void Renders_AreNumberedForTheEntry_InOrderOfAppearance()
    {
        // A snippet piping into a renderer, placed between two of the body's: 1, 2 (the snippet's), 3; a chain's later
        // step names the step before it by the entry's number.
        var files = Package()
            .WithAgent("terms", "List the clinical terms in the draft.\n\n{{ input.consult_draft.content }}\n", "\"concept_list\"")
            .WithSnippet("context", "{{ node.terms.value | renderer.concept_context }}\n", "markdown_block")
            .WithAgent("summary",
                "Before:\n{{ node.terms.value | renderer.concept_bullets }}\n{{~ snippet.context.content }}\nAfter:\n"
                + "{{ node.terms.value | renderer.concept_bullets }}\n");

        var summary = Resolve(files)["nodes"]!["node.summary"]!;

        Assert.Equal(
            "[{\"n\":1,\"renderer\":\"concept_bullets\",\"source\":\"node.terms.value\"},"
            + "{\"n\":2,\"renderer\":\"concept_context\",\"source\":\"node.terms.value\"},"
            + "{\"n\":3,\"renderer\":\"concept_bullets\",\"source\":\"node.terms.value\"}]",
            CanonicalJson.Write(summary["renders"]));
        Assert.Equal(["concept_bullets", "concept_context"],
            JsonNode.Parse(Published(files).Lock)!["builtins"]!.AsObject().Select(b => b.Key).Where(k => k.StartsWith("concept_", StringComparison.Ordinal)));
    }

    [Fact]
    public void ABuiltInRendererUsed_IsEmbedded_AndLocked()
    {
        var files = Package()
            .WithAgent("terms", "List the clinical terms in the draft.\n\n{{ input.consult_draft.content }}\n", "\"concept_list\"")
            .WithAgent("summary", "Summarise these terms for Dr A. Sample:\n{{ node.terms.value | renderer.concept_bullets }}\n");
        var compiled = Published(files);
        var document = JsonNode.Parse(compiled.Resolve)!;
        var builtin = ResolveFixture.Published().BuiltInRenderers["concept_bullets"];

        Assert.Equal(
            "[{\"n\":1,\"renderer\":\"concept_bullets\",\"source\":\"node.terms.value\"}]",
            CanonicalJson.Write(document["nodes"]!["node.summary"]!["renders"]));
        var embedded = document["renderers"]!["renderer.concept_bullets"]!;
        Assert.Equal(builtin.Body, (string?)embedded["body"]);
        Assert.Equal(CanonicalJson.HashText(builtin.Body), (string?)embedded["bodyHash"]);
        Assert.Equal((string?)builtin.Header["label"], (string?)embedded["label"]);
        Assert.Equal("{\"$ref\":\"#/$defs/concept_list\"}", embedded["input"]!.ToJsonString());
        Assert.Equal("{\"$ref\":\"#/$defs/markdown_block\"}", embedded["format"]!.ToJsonString());
        Assert.Equal("concept_bullets", (string?)embedded["id"]);
        Assert.NotNull(document["formats"]!["$defs"]!["concept_list"]);
        Assert.NotNull(document["formats"]!["$defs"]!["markdown_block"]);
        Assert.Null(document["renderers"]!["renderer.concept_context"]);

        var builtins = JsonNode.Parse(compiled.Lock)!["builtins"]!;
        Assert.Equal("1.0.0", (string?)builtins["concept_bullets"]);
        Assert.Null(builtins["concept_context"]);
    }

    [Fact]
    public void Features_FollowWhatIsPlaced()
    {
        static string[] Features(Dictionary<string, byte[]> files) =>
            Resolve(files)["requires"]!["features"]!.AsArray().Select(f => (string)f!).ToArray();

        Assert.Empty(Features(Package()));
        Assert.Equal(["parallel", "macro-markers", "parts"], Features(Placing()));
        Assert.Equal(["run-spots"], Features(Spotted()));
        Assert.Equal(["macro-markers"], Features(Package()
            .WithMacro("m", "Follow up with Dr A. Sample in six weeks.\n")
            .WithAgent("draft", "Draft the note.\n{{~ macro.m }}\n")));
        Assert.Empty(Features(Package().WithMacro("m", "Follow up with Dr A. Sample in six weeks.\n")));
    }

    [Fact]
    public void ACompiledDocument_FitsItsSchema()
    {
        var resolve = Schema("resolve");
        var @lock = Schema("lock");
        foreach (var files in new[] { Placing(), Spotted(), ResolveFixture.NestedNote(), ResolveFixture.Functions(), ResolveFixture.Conditional() })
        {
            var compiled = Published(files);
            Assert.True(resolve.Validate(compiled.Resolve, null), "resolve.json does not fit resolve.schema.json.");
            Assert.True(@lock.Validate(compiled.Lock, null), "lock.json does not fit lock.schema.json.");
        }
    }

    /// <summary>
    /// The pinned <c>&lt;name&gt;.schema.json</c>, with the common definitions, the format dialect and (for
    /// <c>lock</c>) <c>resolve.schema.json</c>, which it refers to.
    /// </summary>
    private static JsonSchema Schema(string name)
    {
        List<AdditionalSchemaFile> files =
        [
            new("urn:consultologist:spec:31:schema:common", V31Contract.Path("schemas", "common.schema.json")),
            new("urn:consultologist:spec:31:dialect", V31Contract.Path("dialect.schema.json")),
        ];
        if (name != "resolve")
            files.Add(new("urn:consultologist:spec:31:schema:resolve", V31Contract.Path("schemas", "resolve.schema.json")));
        var options = new JsonSchema.Options(
            additionalSchemaFiles: files,
            allowFileSystemAndHttpResolution: false,
            defaultDialect: Corvus.Text.Json.RuntimeEvaluator.JsonSchemaDialect.Draft202012,
            alwaysAssertFormat: false,
            additionalDocumentResolver: null);
        var path = V31Contract.Path("schemas", $"{name}.schema.json");
        return JsonSchema.FromText(File.ReadAllText(path), $"urn:consultologist:spec:31:schema:{name}", options, false);
    }
}
