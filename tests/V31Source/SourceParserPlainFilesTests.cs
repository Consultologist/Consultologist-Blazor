using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Source;

/// <summary>
/// #1499 (specVersion 31, area 02): the manifest and the plain files of part 01 §§ 3–5 —
/// renderers, snippets, macros, formats, deliverables and tests — read into the model.
/// </summary>
public class SourceParserPlainFilesTests
{
    private static SourceRefusal Only(SourceParseResult result)
    {
        var refusal = Assert.Single(result.Refusals);
        Assert.Null(result.Package);
        V31Contract.AssertSentence(refusal);
        return refusal;
    }

    private const string RendererHeader =
        "{ \"label\": \"X\", \"kind\": \"rendered\", \"description\": null, \"input\": \"concept_list\", \"format\": \"plaintext\" }";

    /// <summary>Minimal with one renderer <c>x</c> whose file is <paramref name="text"/>.</summary>
    private static Dictionary<string, byte[]> WithRenderer(string text, string id = "x")
    {
        var files = SourceFixture.Minimal().With($"renderers/{id}.md", text);
        files.WithJson("renderers/index.json", json => json["items"] = new JsonArray(id));
        return files;
    }

    [Fact]
    public void Minimal_Parses()
    {
        var result = SourceFixture.Minimal().Parse();

        Assert.Empty(result.Refusals);
        Assert.Empty(result.Normalised);
        var package = result.Package!;
        Assert.Equal("example-minimal", package.Manifest.Name);
        Assert.Equal("v2026.12.1", package.Manifest.Version);
        Assert.Null(package.Manifest.DerivedFrom);
        Assert.Equal("Minimal note demo", package.Manifest.Title);
        Assert.Equal(["A fictional package for tests.", "Not intended for clinical use."], package.Manifest.Description!);
        Assert.Equal(["demo", "fictional"], package.Manifest.Tags);
        Assert.Equal(["consult_draft"], package.Manifest.Inputs.Select(i => i.Id));
        Assert.Equal("Consult draft", (string?)package.Manifest.Inputs[0].Json["label"]);
        Assert.Empty(package.Definitions.Renderers.Items);
        Assert.Empty(package.Definitions.Snippets.Items);
        Assert.Empty(package.Definitions.Macros.Items);
        Assert.Empty(package.Definitions.Formats.Items);
        Assert.Empty(package.Tests.Items);
        Assert.Equal("nodes", package.Nodes.Path);
        Assert.Equal("checks", package.Checks.Path);
        Assert.Equal("functions", package.Functions.Path);

        var deliverable = Assert.Single(package.Deliverables.Deliverables);
        Assert.Null(package.Deliverables.Id);
        Assert.Equal("deliverables", package.Deliverables.Path);
        Assert.Empty(package.Deliverables.Folders);
        Assert.Equal("note", deliverable.Id);
        Assert.Equal("deliverable.note", deliverable.Key);
        Assert.Equal("deliverables/note.md", deliverable.Path);
        Assert.Equal("{{ node.plan.content }}\n", deliverable.Body);
        Assert.Equal("Consultation note", (string?)deliverable.Header["label"]);
        Assert.Empty(package.Readmes);
    }

    [Fact]
    public void Full_ParsesItsDefinitions()
    {
        var result = SourceFixture.Full().Parse();

        Assert.Empty(result.Refusals);
        var package = result.Package!;
        Assert.Equal(["consult_draft", "include_billing"], package.Manifest.Inputs.Select(i => i.Id));

        var definitions = package.Definitions;
        var renderer = Assert.Single(definitions.Renderers.Items);
        Assert.Equal(("term_bullets", "renderers/term_bullets.md"), (renderer.Id, renderer.Path));
        Assert.Equal("{{~ for c in value.concepts ~}}\n- {{ c.term }}\n{{~ end ~}}\n", renderer.Body);
        Assert.Equal("concept_list", (string?)renderer.Header["input"]);

        Assert.Equal(["closing_line", "standards"], definitions.Snippets.Items.Select(s => s.Id));
        var closing = Assert.IsType<SourceSingleText>(definitions.Snippets.Items[0]);
        Assert.Equal("Thank you for seeing Margaret Okafor.\n", closing.Text.Body);
        var standards = Assert.IsType<SourceTextFolder>(definitions.Snippets.Items[1]);
        Assert.Equal("snippets/standards", standards.Path);
        Assert.Equal("standard_section", (string?)standards.Index["format"]);
        Assert.Equal(["hpi", "assessment_plan"], standards.Items.Select(i => i.Id));
        Assert.Equal("snippets/standards/hpi.md", standards.Items[0].Path);
        Assert.Equal("History of Present Illness", (string?)standards.Items[0].Header["title"]);

        Assert.Equal(["follow_up", "phrases"], definitions.Macros.Items.Select(m => m.Id));
        Assert.IsType<SourceSingleText>(definitions.Macros.Items[0]);
        var phrases = Assert.IsType<SourceTextFolder>(definitions.Macros.Items[1]);
        Assert.Equal("Thank you for the referral.\n", Assert.Single(phrases.Items).Body);

        Assert.Equal(["section_plan", "outline", "verdict", "standard_section"], definitions.Formats.Items.Select(f => f.Id));
        Assert.Equal("formats/verdict.json", definitions.Formats.Items[2].Path);
        Assert.Equal("object", (string?)definitions.Formats.Items[2].Schema["type"]);

        var test = Assert.Single(package.Tests.Items);
        Assert.Equal(("note_basic", "tests/note_basic.json"), (test.Id, test.Path));
        Assert.Equal("Dr A. Sample", (string?)test.Json["profile"]!["signature"]);

        Assert.Equal(["deliverable.note"], package.Deliverables.Deliverables.Select(d => d.Key));
        var emr = Assert.Single(package.Deliverables.Folders);
        Assert.Equal(("emr", "deliverables/emr"), (emr.Id, emr.Path));
        Assert.Empty(emr.Folders);
        var consult = Assert.Single(emr.Deliverables);
        Assert.Equal(("consult", "deliverable.emr.consult", "deliverables/emr/consult.md"), (consult.Id, consult.Key, consult.Path));

        Assert.Equal(["README.md"], package.Readmes.Keys);
    }

    [Fact]
    public void SpecVersion30_IsNotRead()
    {
        var refusal = Only(SourceFixture.Minimal().WithJson("manifest.json", json => json["specVersion"] = 30).Parse());

        Assert.Equal("A02-004", refusal.Id);
        Assert.Equal("The manifest declares specVersion 30; this reader reads 31.", refusal.Sentence);
        Assert.Equal("manifest.json", refusal.Path);
    }

    [Fact]
    public void ASpecVersionOfTheWrongType_IsOnlyAWrongType()
    {
        var refusal = Only(SourceFixture.Minimal().WithJson("manifest.json", json => json["specVersion"] = "31").Parse());

        Assert.Equal("A02-001", refusal.Id);
    }

    [Fact]
    public void AGoneManifestMember_IsUnknown()
    {
        var refusal = Only(SourceFixture.Minimal().WithJson("manifest.json", json => json["nodes"] = new JsonArray()).Parse());

        Assert.Equal("S01-023", refusal.Id);
        Assert.StartsWith("The manifest in 'manifest.json' declares unknown member 'nodes'.", refusal.Sentence);
    }

    [Fact]
    public void AnInputId_IsHeldToTheIdRules()
    {
        var refusal = Only(SourceFixture.Minimal().WithJson("manifest.json", json =>
        {
            var inputs = json["inputs"]!.AsObject();
            var draft = inputs["consult_draft"]!.DeepClone();
            inputs.Remove("consult_draft");
            inputs["Consult-Draft"] = draft;
        }).Parse());

        Assert.Equal("S01-030", refusal.Id);
        Assert.Equal("'Consult-Draft' is not a valid id: a lowercase letter, then lowercase letters, digits or underscores.", refusal.Sentence);
    }

    [Fact]
    public void AnInputIsCheckedByItsShape()
    {
        var refusal = Only(SourceFixture.Minimal()
            .WithJson("manifest.json", json => json["inputs"]!["consult_draft"]!.AsObject().Remove("channel"))
            .Parse());

        Assert.Equal("Input 'consult_draft' is missing 'channel'; every member is written, and null means none.", refusal.Sentence);
    }

    [Fact]
    public void AFileWithoutFences_IsRefused()
    {
        var refusal = Only(WithRenderer("Just a template.\n").Parse());

        Assert.Equal("S01-016", refusal.Id);
        Assert.Equal(
            "File 'renderers/x.md' must start with a line of exactly ---, then one JSON object, then a line of exactly ---.",
            refusal.Sentence);
        Assert.Equal("renderers/x.md", refusal.Path);
    }

    [Fact]
    public void AHeaderThatIsNotJson_CarriesTheParsersMessage()
    {
        var refusal = Only(WithRenderer("---\n{ label: 'X' }\n---\nT\n").Parse());

        Assert.Equal("S01-017", refusal.Id);
        Assert.StartsWith("The header of 'renderers/x.md' is not valid JSON: ", refusal.Sentence);
        Assert.DoesNotContain("..", refusal.Sentence);
    }

    [Fact]
    public void AHeaderThatIsAnArray_IsRefused()
    {
        var refusal = Only(WithRenderer("---\n[1, 2]\n---\nT\n").Parse());

        Assert.Equal("S01-016", refusal.Id);
    }

    [Fact]
    public void ARepeatedHeaderMember_IsNamed()
    {
        var refusal = Only(WithRenderer("---\n" + RendererHeader.Replace("{ ", "{ \"label\": \"Y\", ") + "\n---\nT\n").Parse());

        Assert.Equal("S01-018", refusal.Id);
        Assert.Equal("The header of 'renderers/x.md' declares 'label' twice.", refusal.Sentence);
    }

    [Fact]
    public void AHeaderNestedTooDeep_IsRefused()
    {
        var deep = string.Concat(Enumerable.Repeat("[", 33)) + string.Concat(Enumerable.Repeat("]", 33));
        var refusal = Only(WithRenderer("---\n{ \"label\": " + deep + " }\n---\nT\n").Parse());

        Assert.Equal("S01-041", refusal.Id);
        Assert.StartsWith("File 'renderers/x.md' nests JSON more than 32 levels deep at /label/0/", refusal.Sentence);
    }

    [Fact]
    public void AnIndexThatIsNotAnObject_IsRefused()
    {
        var refusal = Only(SourceFixture.Minimal().With("renderers/index.json", "[]\n").Parse());

        Assert.Equal("A02-005", refusal.Id);
        Assert.Equal("File 'renderers/index.json' must hold one JSON object.", refusal.Sentence);
    }

    [Fact]
    public void AnIndexIsCheckedByItsShape()
    {
        var refusal = Only(SourceFixture.Minimal().WithJson("snippets/index.json", json => json["description"] = new JsonArray()).Parse());

        Assert.Equal("snippets/index.json has an empty description; write null for none.", refusal.Sentence);
    }

    [Fact]
    public void ARendererWithoutABody_IsRefused()
    {
        var refusal = Only(WithRenderer("---\n" + RendererHeader + "\n---\n  \n").Parse());

        Assert.Equal("S01-019", refusal.Id);
        Assert.Equal("Renderer 'x' has an empty body; its template is the body.", refusal.Sentence);
    }

    [Fact]
    public void ARendererIsCheckedByItsShape()
    {
        var result = WithRenderer("---\n" + RendererHeader.Replace("\"label\"", "\"lable\"") + "\n---\nT\n").Parse();

        Assert.Equal(
            [
                "Renderer 'x' is missing 'label'; every member is written, and null means none.",
                "Renderer 'x' in 'renderers/x.md' declares unknown member 'lable'. Did you mean 'label'?",
            ],
            result.Refusals.Select(r => r.Sentence));
    }

    [Fact]
    public void ASnippetAndAMacroItem_NeedATextBody()
    {
        var full = SourceFixture.Full();
        foreach (var path in new[] { "snippets/closing_line.md", "macros/phrases/thanks.md" })
        {
            var header = FrontMatter.Split(SourceFixture.Text(full, path))!.Header;
            full.With(path, $"---\n{header}\n---\n\n");
        }

        var result = full.Parse();

        Assert.Equal(
            ["Item 'phrases.thanks' has an empty body; its text is the body.", "Snippet 'closing_line' has an empty body; its text is the body."],
            result.Refusals.Select(r => r.Sentence));
    }

    [Fact]
    public void ASnippetFolderItem_MayHaveAnEmptyBody()
    {
        var files = SourceFixture.Full().With("snippets/standards/hpi.md", "---\n{ \"title\": \"History of Present Illness\" }\n---\n");

        var result = files.Parse();

        Assert.Empty(result.Refusals);
        var standards = Assert.IsType<SourceTextFolder>(result.Package!.Definitions.Snippets.Items[1]);
        Assert.Equal("", standards.Items[0].Body);
    }

    [Fact]
    public void AMacroFolderItem_IsAMacro()
    {
        var refusal = Only(SourceFixture.Full()
            .WithJson("macros/phrases/thanks.md", header => header.Remove("instruction"))
            .Parse());

        Assert.Equal("Item 'phrases.thanks' is missing 'instruction'; every member is written, and null means none.", refusal.Sentence);
    }

    [Fact]
    public void AFormatReusingABuiltInName_IsRefused()
    {
        var files = SourceFixture.Minimal().With("formats/plaintext.json", "{ \"type\": \"string\" }\n");
        files.WithJson("formats/index.json", json => json["items"] = new JsonArray("plaintext"));
        var names = new HashSet<string> { "plaintext" };
        var none = new HashSet<string>();

        var refusal = Only(files.Parse(new SourceRegistry(names, names, none)));
        Assert.Equal("S01-034", refusal.Id);
        Assert.Equal("Format 'plaintext' reuses the name of a built-in format: rename it.", refusal.Sentence);
        Assert.Equal("formats/plaintext.json", refusal.Path);

        refusal = Only(files.Parse(new SourceRegistry(none, names, none)));
        Assert.Equal("Format 'plaintext' reuses the name of a catalogue format: rename it.", refusal.Sentence);
    }

    [Fact]
    public void AFormatThatIsNotAnObject_IsRefused()
    {
        var refusal = Only(SourceFixture.Full().With("formats/verdict.json", "\"string\"\n").Parse());

        Assert.Equal("File 'formats/verdict.json' must hold one JSON object.", refusal.Sentence);
    }

    [Fact]
    public void ARendererReusingABuiltInName_IsRefused()
    {
        var files = WithRenderer("---\n" + RendererHeader + "\n---\nT\n", "concept_bullets");
        var none = new HashSet<string>();

        var refusal = Only(files.Parse(new SourceRegistry(none, none, new HashSet<string> { "concept_bullets" })));

        Assert.Equal("S01-035", refusal.Id);
        Assert.Equal("Renderer 'concept_bullets' reuses the name of a built-in renderer: rename it.", refusal.Sentence);
    }

    [Fact]
    public void ATestIsCheckedByItsShape()
    {
        var refusal = Only(SourceFixture.Full().WithJson("tests/note_basic.json", json => json.Remove("expect")).Parse());

        Assert.Equal("Test 'note_basic' is missing 'expect'; every member is written, and null means none.", refusal.Sentence);
    }

    [Fact]
    public void ADeliverable_NeedsATemplate_AndItsShape()
    {
        var files = SourceFixture.Full()
            .With("deliverables/emr/consult.md", "---\n{ \"label\": \"Consult letter\" }\n---\n");

        var result = files.Parse();

        Assert.Contains(result.Refusals, r => r.Sentence == "Deliverable 'consult' has an empty body; its template is the body.");
        Assert.Contains(result.Refusals, r => r.Sentence == "Deliverable 'consult' is missing 'whenEmpty'; every member is written, and null means none.");
        Assert.All(result.Refusals, r => Assert.Equal("deliverables/emr/consult.md", r.Path));
    }

    [Fact]
    public void WrongTypes_AreRefused_AndNeverThrow()
    {
        string[] indexes =
        [
            "nodes/index.json", "checks/index.json", "functions/index.json", "renderers/index.json", "snippets/index.json",
            "macros/index.json", "formats/index.json", "deliverables/index.json", "tests/index.json",
            "snippets/standards/index.json", "macros/phrases/index.json", "deliverables/emr/index.json",
            "nodes/sections/index.json", "nodes/billing_gate/index.json", "nodes/sections_draft/route/index.json",
            "functions/draft_section/index.json", "functions/draft_section/snippets/index.json",
        ];
        JsonNode?[] values = [null, 1, 1.5, "x", true, new JsonObject(), new JsonArray(1, null, new JsonObject())];
        string[] members = ["items", "nodes", "then", "else", "branches", "description", "kind"];
        foreach (var index in indexes)
        {
            foreach (var member in members)
            {
                foreach (var value in values)
                {
                    var files = SourceFixture.Full().WithJson(index, json => json[member] = value?.DeepClone());
                    var result = files.Parse();
                    Assert.Equal(result.Refusals.Count == 0, result.Package != null);
                }
            }
        }

        foreach (var value in values)
        {
            var manifest = SourceFixture.Full().WithJson("manifest.json", json => json["inputs"] = value?.DeepClone());
            Assert.Equal(value is JsonObject, manifest.Parse().Refusals.Count == 0);
            var input = SourceFixture.Full().WithJson("manifest.json", json => json["inputs"]!["consult_draft"] = value?.DeepClone());
            Assert.NotEmpty(input.Parse().Refusals);
            var spec = SourceFixture.Full().WithJson("manifest.json", json => json["specVersion"] = value?.DeepClone());
            Assert.NotEmpty(spec.Parse().Refusals);
        }

        foreach (var text in new[] { "", "null", "1", "[]", "---", "---\n---\n", "---\nnull\n---\n", "{" })
        {
            foreach (var path in new[] { "manifest.json", "renderers/term_bullets.md", "formats/verdict.json", "tests/note_basic.json", "deliverables/note.md", "snippets/standards/hpi.md" })
            {
                Assert.NotEmpty(SourceFixture.Full().With(path, text).Parse().Refusals);
            }
        }
    }

    [Fact]
    public void EveryLayoutSentence_IsTheDesigns()
    {
        var sentences = new[]
        {
            SourceSentences.NotListed("nodes/x.md", "nodes"),
            SourceSentences.NoIndex("snippets"),
            SourceSentences.NoTopFolder("tests"),
            SourceSentences.ListedButMissing("nodes/index.json", "ghost", "nodes"),
            SourceSentences.ListedTwice("nodes/index.json", "plan"),
            SourceSentences.ListedWithSlash("snippets/index.json", "standards"),
            SourceSentences.FileAndFolder("nodes", "plan"),
            SourceSentences.NestedDeliveryFolder("deliverables/emr/inner"),
            SourceSentences.NoFrontMatter("renderers/x.md"),
            SourceSentences.NotJson("renderers/x.md", "'l' is an invalid start of a property name."),
            SourceSentences.DuplicateMember("renderers/x.md", "label"),
            SourceSentences.EmptyBody("Renderer 'x'", "template", "renderers/x.md"),
            SourceSentences.HasBody("Node 'count'", "computed node", "nodes/count.md"),
            SourceSentences.FormatReusesName("plaintext", "built-in", "formats/plaintext.json"),
            SourceSentences.RendererReusesName("concept_bullets", "renderers/concept_bullets.md"),
            SourceSentences.JsonTooDeep("renderers/x.md", "/a/0"),
            SourceSentences.NoManifest(),
            SourceSentences.SpecVersionNotRead("30"),
            SourceSentences.NotAnObject("renderers/index.json"),
        };

        Assert.Equal(
            [
                "S01-006", "S01-007", "S01-008", "S01-009", "S01-010", "S01-011", "S01-012", "S01-013", "S01-016",
                "S01-017", "S01-018", "S01-019", "S01-020", "S01-034", "S01-035", "S01-041", "A02-003", "A02-004", "A02-005",
            ],
            sentences.Select(s => s.Id));
        Assert.Equal("The header of 'renderers/x.md' is not valid JSON: 'l' is an invalid start of a property name.", sentences[9].Sentence);
        foreach (var sentence in sentences)
        {
            V31Contract.AssertSentence(sentence);
        }
    }
}
