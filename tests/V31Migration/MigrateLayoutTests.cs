using System.Text.Json.Nodes;
using Consultologist.Api.Tests.ByteEquivalence;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 1.2 rows 1 to 5 and the format rows, § 1.8; part 01 §§ 2, 3, 5; part 02
/// §§ 3.3, 6.1; part 05 § 6): the first rewrite pass. The nine folders are written, each with its index; the manifest
/// keeps identity and inputs; every member of every declaration is written; line endings become <c>\n</c>; a package
/// format becomes its schema alone, with every property required and the optional ones nullable; snippets and snippet
/// folders move with their 31 headers. A package a hand broke is refused or migrated, never thrown on.
/// </summary>
public class MigrateLayoutTests
{
    /// <summary>The sixteen packages of the fixtures: area 01's vendored packages and its composer fixtures.</summary>
    public static TheoryData<string> Packages() => new(
        "example-classifier-scope", "example-conditional-documents", "example-conditional-sections", "example-macros",
        "example-macros-warnings", "example-nested-note", "example-prior-letters", "example-problem-list",
        "example-structured-intake", "example-two-documents", "example-v12-constructs", "general", "test/general",
        "composer-gates", "composer-macros", "composer-sources");

    private static readonly string[] Folders =
        ["nodes", "checks", "functions", "renderers", "snippets", "macros", "formats", "deliverables", "tests"];

    private static Migration31Result Migrated(string package)
    {
        var result = MigrationFixture.Migrate(package);
        Assert.True(result.Succeeded, string.Join("\n", result.Refusals));
        return result;
    }

    private static JsonObject JsonOf(Migration31Result result, string path) =>
        JsonNode.Parse(result.Files[path])!.AsObject();

    private static (JsonObject Header, string Body) MdOf(Migration31Result result, string path)
    {
        var split = FrontMatter.Split(result.Files[path]);
        Assert.NotNull(split);
        Assert.DoesNotContain('\n', split!.Header);
        return (JsonNode.Parse(split.Header)!.AsObject(), split.Body);
    }

    private static string Compact(string json) => JsonNode.Parse(json)!.ToJsonString();

    [Theory]
    [MemberData(nameof(Packages))]
    public void AllNineFolders_AreWritten_EachWithItsIndex(string package)
    {
        var result = Migrated(package);

        foreach (var folder in Folders)
        {
            Assert.True(result.Files.ContainsKey($"{folder}/index.json"), $"{package}: {folder}/index.json is missing.");
            var index = JsonOf(result, $"{folder}/index.json");
            Assert.Null(index["description"]);
            Assert.IsType<JsonArray>(index["items"]);
        }

        // The root delivery folder, with no destination folder beside it, delivering pdf (R8, Task 22); its items are the
        // deliverables pass's (Task 11), 30's deliverables in order.
        var (json30, _) = ByteEquivalenceFixtures.ReadPackage(package);
        var ids = new JsonArray([.. JsonNode.Parse(json30)!["deliverables"]!.AsArray().Select(d => (JsonNode?)JsonValue.Create((string)d!["id"]!))]);
        Assert.Equal(new JsonObject { ["description"] = null, ["items"] = ids, ["delivery"] = new JsonArray("pdf"), ["bundle"] = null }.ToJsonString(),
            JsonOf(result, "deliverables/index.json").ToJsonString());
        Assert.DoesNotContain("manifest.json", result.Files.Keys);
    }

    [Fact]
    public void AllNineFolders_AreWritten_EvenWhenEmpty()
    {
        // example-nested-note has no macros, snippets, checks, functions, renderers or tests at 30.
        var result = Migrated("example-nested-note");

        foreach (var folder in new[] { "checks", "functions", "renderers", "snippets", "macros" })
        {
            Assert.Equal(Compact("""{ "description": null, "items": [] }"""), JsonOf(result, $"{folder}/index.json").ToJsonString());
        }

        // With no hand edit, tests/ holds the migration's snapshot test (Task 14, part 10 § 4).
        Assert.Equal(Compact("""{ "description": null, "items": ["migration_snapshot"] }"""), JsonOf(result, "tests/index.json").ToJsonString());

        Assert.Equal(Compact("""{ "description": null, "items": ["section_plan", "outline", "verdict"] }"""),
            JsonOf(result, "formats/index.json").ToJsonString());
    }

    [Fact]
    public void TheManifest_KeepsIdentityAndInputsOnly()
    {
        var (json30, _) = ByteEquivalenceFixtures.ReadPackage("example-nested-note");
        var root30 = JsonNode.Parse(json30)!.AsObject();
        var result = Migrated("example-nested-note");

        var manifest = JsonNode.Parse(result.ManifestJson)!.AsObject();

        Assert.Equal(["name", "version", "specVersion", "derivedFrom", "title", "description", "tags", "inputs"],
            manifest.Select(m => m.Key));
        Assert.Equal("example-nested-note", (string?)manifest["name"]);
        Assert.Equal("v2026.11.2", (string?)manifest["version"]);
        Assert.Equal(31, manifest["specVersion"]!.GetValue<int>());
        Assert.Null(manifest["derivedFrom"]);
        Assert.Equal("Nested note demo", (string?)manifest["title"]);
        Assert.Equal(new JsonArray((string?)root30["description"]).ToJsonString(), manifest["description"]!.ToJsonString());
        Assert.Equal("""["demo","nested"]""", manifest["tags"]!.ToJsonString());
        Assert.IsType<JsonObject>(manifest["inputs"]);
        Assert.EndsWith("}\n", result.ManifestJson, StringComparison.Ordinal);
    }

    [Fact]
    public void TheManifest_WritesNullForWhatThe30ManifestLacks()
    {
        // test/general has no title, description or derivedFrom at 30.
        var manifest = JsonNode.Parse(Migrated("test/general").ManifestJson)!.AsObject();

        Assert.Null(manifest["title"]);
        Assert.Null(manifest["description"]);
        Assert.Null(manifest["derivedFrom"]);
        Assert.True(manifest.ContainsKey("title") && manifest.ContainsKey("description") && manifest.ContainsKey("derivedFrom"));
    }

    [Fact]
    public void ADescriptionWithLineBreaks_BecomesItsLines()
    {
        var (json, files) = ByteEquivalenceFixtures.ReadPackage("example-nested-note");
        var root = JsonNode.Parse(json)!.AsObject();
        root["description"] = "First line.\nSecond line.";

        var result = WorkflowPackageMigration30To31.Migrate(root.ToJsonString(), files, MigrationFixture.Options("example-nested-note"));

        Assert.Equal("""["First line.","Second line."]""", JsonNode.Parse(result.ManifestJson)!["description"]!.ToJsonString());
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public void EveryMember_IsWritten(string package)
    {
        var result = Migrated(package);
        var refusals = new List<SourceRefusal>();

        SourceMembers.Check(JsonNode.Parse(result.ManifestJson)!.AsObject(), SourceShapes.Manifest, "Manifest", "manifest.json", refusals);
        foreach (var (id, input) in JsonNode.Parse(result.ManifestJson)!["inputs"]!.AsObject())
            SourceMembers.Check(input!.AsObject(), SourceShapes.Input, $"Input '{id}'", "manifest.json", refusals);

        var checkedFiles = 0;
        foreach (var (path, text) in result.Files)
        {
            if (ShapeOf(path) is not { } shape)
                continue;
            var declaration = path.EndsWith(".md", StringComparison.Ordinal)
                ? JsonNode.Parse(FrontMatter.Split(text)!.Header)!.AsObject()
                : JsonNode.Parse(text)!.AsObject();
            SourceMembers.Check(declaration, shape, shape.Name, path, refusals);
            checkedFiles++;
        }

        Assert.True(checkedFiles >= Folders.Length);
        Assert.DoesNotContain(refusals, r => r.Id is "S01-021" or "S01-023");
        Assert.Empty(refusals);
    }

    /// <summary>The declaration shape of a file this pass writes, or null for a format file or a folder item (a value).</summary>
    private static DeclarationShape? ShapeOf(string path)
    {
        var parts = path.Split('/');
        return parts switch
        {
            ["deliverables", "index.json"] => SourceShapes.DeliverablesIndex,
            [_, "index.json"] => SourceShapes.Index,
            ["snippets", _, "index.json"] => SourceShapes.SnippetFolderIndex,
            ["snippets", var file] when file.EndsWith(".md", StringComparison.Ordinal) => SourceShapes.Snippet,
            _ => null,
        };
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public void TheMigration_IsDeterministic(string package)
    {
        var first = Migrated(package);
        var second = Migrated(package);

        Assert.Equal(first.ManifestJson, second.ManifestJson);
        Assert.Equal(first.Files.OrderBy(f => f.Key, StringComparer.Ordinal), second.Files.OrderBy(f => f.Key, StringComparer.Ordinal));
        Assert.Equal(first.Notices, second.Notices);
    }

    [Fact]
    public void CarriageReturns_AreNormalised()
    {
        var (json, files) = ByteEquivalenceFixtures.ReadPackage("test/general");
        var options = MigrationFixture.Options("test/general");
        var plain = WorkflowPackageMigration30To31.Migrate(json, files, options);

        static string Crlf(string text) => text.Replace("\r\n", "\n").Replace("\n", "\r\n");
        var crlf = WorkflowPackageMigration30To31.Migrate(
            Crlf(json), files.ToDictionary(f => f.Key, f => Crlf(f.Value), StringComparer.Ordinal), options);

        // A BOM on every file, and a lone \r in a snippet body, too.
        var bom = files.ToDictionary(f => f.Key, f => "﻿" + f.Value, StringComparer.Ordinal);
        var bommed = WorkflowPackageMigration30To31.Migrate("﻿" + json, bom, options);

        Assert.True(plain.Succeeded, string.Join("\n", plain.Refusals));
        Assert.True(crlf.Succeeded, string.Join("\n", crlf.Refusals));
        Assert.True(bommed.Succeeded, string.Join("\n", bommed.Refusals));
        Assert.Equal(plain.ManifestJson, crlf.ManifestJson);
        Assert.Equal(plain.Files.OrderBy(f => f.Key, StringComparer.Ordinal), crlf.Files.OrderBy(f => f.Key, StringComparer.Ordinal));
        Assert.Equal(plain.Files.OrderBy(f => f.Key, StringComparer.Ordinal), bommed.Files.OrderBy(f => f.Key, StringComparer.Ordinal));
        Assert.All(crlf.Files.Values.Append(crlf.ManifestJson), text => Assert.DoesNotContain('\r', text));
        Assert.All(bommed.Files.Values.Append(bommed.ManifestJson), text => Assert.DoesNotContain('﻿', text));

        var withCr = new Dictionary<string, string>(files, StringComparer.Ordinal);
        withCr["snippets/snomed_tool_guidance.md"] = withCr["snippets/snomed_tool_guidance.md"].Replace("\n", "\r");
        var loneCr = WorkflowPackageMigration30To31.Migrate(json, withCr, options);
        Assert.True(loneCr.Succeeded, string.Join("\n", loneCr.Refusals));
        Assert.Equal(plain.Files["snippets/snomed_tool_guidance.md"], loneCr.Files["snippets/snomed_tool_guidance.md"]);

        // A lone \r changes the file's bytes, so it is said (A10-026); \r\n and a BOM are not.
        var notice = "snippets/snomed_tool_guidance.md: a carriage return that didn't end a line became a line end.";
        Assert.Contains(notice, loneCr.Notices);
        Assert.Equal(plain.Notices, loneCr.Notices.Where(n => n != notice));
        V31Contract.AssertSentence(new SourceRefusal("A10-026", notice, ""));
        Assert.Equal(plain.Notices, crlf.Notices);
        Assert.Equal(plain.Notices, bommed.Notices);
    }

    [Fact]
    public void APackageFormat_BecomesItsSchemaAlone()
    {
        var result = Migrated("example-nested-note");

        Assert.Equal(
            """
            {
              "$schema": "urn:consultologist:spec:31:dialect",
              "type": "object",
              "description": "A judgement of a draft summary: good, or revise with what to change.",
              "additionalProperties": false,
              "required": [
                "verdict",
                "feedback"
              ],
              "properties": {
                "verdict": {
                  "type": "string",
                  "enum": [
                    "good",
                    "revise"
                  ]
                },
                "feedback": {
                  "type": "string"
                }
              }
            }

            """,
            result.Files["formats/verdict.json"]);
        foreach (var format in new[] { "section_plan", "outline", "verdict" })
        {
            var schema = JsonOf(result, $"formats/{format}.json");
            Assert.Equal("urn:consultologist:spec:31:dialect", (string?)schema["$schema"]);
            Assert.False(schema.ContainsKey("producer"));
            Assert.False(schema.ContainsKey("outputMacros"));
            Assert.False(schema.ContainsKey("schema"));
        }
    }

    [Fact]
    public void EveryProperty_IsRequired_OptionalOnesNullable()
    {
        // 30 already lists every property of a package format in required and writes an optional one with a nullable type
        // (["string", "null"]); 31 writes a nullable enum with null among its values, and a nullable object or array as anyOf.
        var (json, files) = ByteEquivalenceFixtures.ReadPackage("example-nested-note");
        files = new Dictionary<string, string>(files, StringComparer.Ordinal)
        {
            ["formats/verdict.json"] = """
                {
                  "type": "object",
                  "producer": "custom",
                  "outputMacros": false,
                  "description": "A judgement of a draft summary.",
                  "schema": {
                    "type": "object",
                    "additionalProperties": false,
                    "required": ["verdict", "tone", "feedback", "already", "detail", "scores", "tags"],
                    "properties": {
                      "verdict": { "type": "string", "enum": ["good", "revise"] },
                      "tone": { "type": ["string", "null"], "enum": ["warm", "plain"] },
                      "feedback": { "type": ["string", "null"] },
                      "already": { "type": ["number", "null"] },
                      "detail": {
                        "type": ["object", "null"],
                        "additionalProperties": false,
                        "required": ["note", "count"],
                        "properties": { "note": { "type": "string" }, "count": { "type": ["integer", "null"] } }
                      },
                      "scores": {
                        "type": "array",
                        "items": {
                          "type": "object",
                          "additionalProperties": false,
                          "required": ["a", "b"],
                          "properties": { "a": { "type": "number" }, "b": { "type": ["boolean", "null"] } }
                        }
                      },
                      "tags": { "type": ["array", "null"], "items": { "type": "string" } }
                    }
                  }
                }
                """,
        };

        var result = WorkflowPackageMigration30To31.Migrate(json, files, MigrationFixture.Options("example-nested-note"));

        Assert.True(result.Succeeded, string.Join("\n", result.Refusals));
        Assert.Equal(Compact("""
            {
              "$schema": "urn:consultologist:spec:31:dialect",
              "type": "object",
              "description": "A judgement of a draft summary.",
              "additionalProperties": false,
              "required": ["verdict", "tone", "feedback", "already", "detail", "scores", "tags"],
              "properties": {
                "verdict": { "type": "string", "enum": ["good", "revise"] },
                "tone": { "type": ["string", "null"], "enum": ["warm", "plain", null] },
                "feedback": { "type": ["string", "null"] },
                "already": { "type": ["number", "null"] },
                "detail": {
                  "anyOf": [
                    {
                      "type": "object",
                      "additionalProperties": false,
                      "required": ["note", "count"],
                      "properties": { "note": { "type": "string" }, "count": { "type": ["integer", "null"] } }
                    },
                    { "type": "null" }
                  ]
                },
                "scores": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "additionalProperties": false,
                    "required": ["a", "b"],
                    "properties": { "a": { "type": "number" }, "b": { "type": ["boolean", "null"] } }
                  }
                },
                "tags": { "anyOf": [{ "type": "array", "items": { "type": "string" } }, { "type": "null" }] }
              }
            }
            """), JsonOf(result, "formats/verdict.json").ToJsonString());
    }

    [Fact]
    public void AnOmittedProperty_IsListedInRequired_AndMadeNullable_AtEveryDepth()
    {
        // Part 10 § 1.8 for an object whose required lists only some properties (30 refuses one in a package format; the
        // rule is the catalogue's too): every property listed, the omitted ones nullable, in nested objects and items too.
        var schema = JsonNode.Parse("""
            {
              "type": "object",
              "additionalProperties": false,
              "required": ["scores"],
              "properties": {
                "tone": { "type": "string", "enum": ["warm", "plain"] },
                "ref": { "$ref": "plaintext" },
                "detail": {
                  "type": "object",
                  "additionalProperties": false,
                  "properties": { "note": { "type": "string" } }
                },
                "scores": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "additionalProperties": false,
                    "required": ["a"],
                    "properties": { "a": { "type": "number" }, "b": { "type": "boolean" } }
                  }
                }
              }
            }
            """)!.AsObject();

        PackageFormat.V31.Migration.Migrate.Strict(schema);

        Assert.Equal(Compact("""
            {
              "type": "object",
              "additionalProperties": false,
              "required": ["tone", "ref", "detail", "scores"],
              "properties": {
                "tone": { "type": ["string", "null"], "enum": ["warm", "plain", null] },
                "ref": { "anyOf": [{ "$ref": "plaintext" }, { "type": "null" }] },
                "detail": {
                  "anyOf": [
                    { "type": "object", "additionalProperties": false, "properties": { "note": { "type": ["string", "null"] } }, "required": ["note"] },
                    { "type": "null" }
                  ]
                },
                "scores": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "additionalProperties": false,
                    "required": ["a", "b"],
                    "properties": { "a": { "type": "number" }, "b": { "type": ["boolean", "null"] } }
                  }
                }
              }
            }
            """), schema.ToJsonString());
    }

    [Fact]
    public void AFreeTextStringField_IsSuggested_NotChanged()
    {
        var result = Migrated("example-nested-note");

        // The fields stay data (string), as they were at 30; a notice suggests a text format for each.
        Assert.Equal("""{"type":"string"}""", JsonOf(result, "formats/verdict.json")["properties"]!["feedback"]!.ToJsonString());
        Assert.Equal(
            [
                "Format 'section_plan', field 'summary' holds free text; consider a text format.",
                "Format 'section_plan', field 'sections.title' holds free text; consider a text format.",
                "Format 'section_plan', field 'sections.purpose' holds free text; consider a text format.",
                "Format 'outline', field 'points.point' holds free text; consider a text format.",
                "Format 'verdict', field 'feedback' holds free text; consider a text format.",
            ],
            result.Report.Suggestions);
        Assert.All(result.Report.Suggestions, s =>
        {
            Assert.Contains(s, result.Notices);
            V31Contract.AssertSentence(new SourceRefusal("A10-021", s, ""));
        });
        Assert.Empty(result.HandEdits);
    }

    [Fact]
    public void SnippetsAndFolders_MoveWithTheir31Headers()
    {
        var (_, files30) = ByteEquivalenceFixtures.ReadPackage("test/general");
        var result = Migrated("test/general");

        // The 30 snippets in order, then the macro the macros pass makes fixed text (D4: test, renamed test_snippet).
        Assert.Equal(Compact("""{ "description": null, "items": ["snomed_tool_guidance", "standards", "test_snippet"] }"""),
            JsonOf(result, "snippets/index.json").ToJsonString());

        // A single snippet: label, description null, if null (Task 7 converts a 30 if), format markdown_block.
        var (single, singleBody) = MdOf(result, "snippets/snomed_tool_guidance.md");
        Assert.Equal(Compact("""{ "label": "SNOMED tool guidance", "description": null, "if": null, "format": "markdown_block" }"""),
            single.ToJsonString());
        Assert.Equal(WorkflowTextHeaders.Split(files30["snippets/snomed_tool_guidance.md"]).Body, singleBody);
        Assert.StartsWith("---\n{\"label\":", result.Files["snippets/snomed_tool_guidance.md"], StringComparison.Ordinal);

        // A folder: its index has description, items and the format its items' headers are values of.
        Assert.Equal(Compact("""
            { "description": null,
              "items": ["hpi", "pmh", "medications", "allergies", "social_history", "family_history", "exam", "investigations", "assessment_plan"],
              "format": "standard_section" }
            """), JsonOf(result, "snippets/standards/index.json").ToJsonString());
        var (hpi, hpiBody) = MdOf(result, "snippets/standards/hpi.md");
        Assert.Equal(Compact("""{ "title": "History of Present Illness" }"""), hpi.ToJsonString());
        Assert.Equal(WorkflowTextHeaders.Split(files30["snippets/standards/hpi.md"]).Body, hpiBody);

        // 30's item label is carried as the member title of a package format (part 01's test/general: standard_section).
        Assert.Equal(
            """
            {
              "$schema": "urn:consultologist:spec:31:dialect",
              "type": "object",
              "additionalProperties": false,
              "required": [
                "title"
              ],
              "properties": {
                "title": {
                  "type": "string"
                }
              }
            }

            """,
            result.Files["formats/standard_section.json"]);
        Assert.Contains("standard_section", JsonOf(result, "formats/index.json")["items"]!.AsArray().Select(i => (string?)i));
        Assert.False(result.Files.ContainsKey("snippets/standards/index.json.md"));
        Assert.DoesNotContain(result.Files.Keys, k => k.StartsWith("prompts/", StringComparison.Ordinal));
    }

    [Fact]
    public void AFolderWithNoHandChoice_TakesItsFolderItemFormat()
    {
        var result = Migrated("example-macros");

        Assert.Equal("sections_item", (string?)JsonOf(result, "snippets/sections/index.json")["format"]);
        Assert.True(result.Files.ContainsKey("formats/sections_item.json"));
        Assert.Equal(Compact("""{ "description": null, "items": ["sections_item"] }"""),
            JsonOf(result, "formats/index.json").ToJsonString());
        var (style, _) = MdOf(result, "snippets/style.md");
        Assert.Equal("markdown_block", (string?)style["format"]);
    }

    [Fact]
    public void AFolderItemsIf_IsAHandEdit()
    {
        // example-problem-list's follow_up_plan is gated at 30; a 31 folder item has no if.
        var result = Migrated("example-problem-list");

        var edit = Assert.Single(result.HandEdits);
        Assert.Equal(
            "Hand edit needed in snippets/sections/follow_up_plan.md, if: a snippet folder's item has no if at 31; gate the item with an if node in the loop over the folder.",
            edit);
        V31Contract.AssertSentence(new SourceRefusal("S10-002", edit, "snippets/sections/follow_up_plan.md"));
        var (header, _) = MdOf(result, "snippets/sections/follow_up_plan.md");
        Assert.Equal(Compact("""{ "title": "Follow-up plan", "if": "node.follow_up.value == \"yes\"" }"""), header.ToJsonString());
    }

    [Fact]
    public void AFolderFormatThatWouldCollide_TakesANumericSuffix()
    {
        var (json, files) = ByteEquivalenceFixtures.ReadPackage("example-problem-list");
        var root = JsonNode.Parse(json)!.AsObject();
        root["formats"]!.AsArray().Add(new JsonObject { ["id"] = "sections_item", ["file"] = "formats/sections_item.json" });
        files = new Dictionary<string, string>(files, StringComparer.Ordinal) { ["formats/sections_item.json"] = files["formats/problem_list.json"] };

        var result = WorkflowPackageMigration30To31.Migrate(root.ToJsonString(), files, MigrationFixture.Options("example-problem-list"));

        Assert.True(result.Succeeded, string.Join("\n", result.Refusals));
        Assert.Equal("sections_item_2", (string?)JsonOf(result, "snippets/sections/index.json")["format"]);
        Assert.Equal(Compact("""{ "description": null, "items": ["problem_list", "sections_item", "sections_item_2"] }"""),
            JsonOf(result, "formats/index.json").ToJsonString());
    }

    [Fact]
    public void ALongFoldersItemFormat_IsCutTo64_AndSaid()
    {
        // Final review A6 (part 01 § 7): <folder>_item is fitted to 64 characters as every generated id is (A10-034).
        var folder = "standing_sections_" + new string('s', 44);
        Assert.Equal(62, folder.Length);
        var (json, read) = ByteEquivalenceFixtures.ReadPackage("example-problem-list");
        json = json.Replace("\"sections/\"", $"\"{folder}/\"", StringComparison.Ordinal)
            .Replace("item in snippet.sections", $"item in snippet.{folder}", StringComparison.Ordinal);
        var files = read.ToDictionary(
            f => f.Key.StartsWith("snippets/sections/", StringComparison.Ordinal) ? $"snippets/{folder}/" + f.Key["snippets/sections/".Length..] : f.Key,
            f => f.Value, StringComparer.Ordinal);

        var result = WorkflowPackageMigration30To31.Migrate(json, files, MigrationFixture.Options("example-problem-list"));

        Assert.True(result.Succeeded, string.Join("\n", result.Refusals));
        var format = folder[..59] + "_item";
        Assert.Equal(format, (string?)JsonOf(result, $"snippets/{folder}/index.json")["format"]);
        Assert.Contains($"formats/{format}.json", result.Files.Keys);
        var notice = $"Renamed format '{folder}_item' to '{format}': an id is at most 64 characters at 31.";
        Assert.Contains(notice, result.Notices);
        V31Contract.AssertSentence(new SourceRefusal("A10-034", notice, ""));
    }

    [Theory]
    [InlineData("problem_list", true)]
    [InlineData("judgement", true)]
    [InlineData("Sections", false)]
    public void AHandChosenFolderFormatThatCannotBeUsed_IsRefused(string chosen, bool taken)
    {
        // Final review A6: a chosen name already a format (the package's, or the registry's) is refused, as a chosen
        // rename is, and never numbered; so is one that is no 31 id. Nothing is written.
        var (json, files) = ByteEquivalenceFixtures.ReadPackage("example-problem-list");
        var options = MigrationFixture.Options("example-problem-list") with
        {
            FolderFormats = new Dictionary<string, string>(StringComparer.Ordinal) { ["sections"] = chosen },
        };

        var result = WorkflowPackageMigration30To31.Migrate(json, files, options);

        Assert.False(result.Succeeded);
        var refusal = $"The chosen format '{chosen}' for snippet folder 'sections' cannot be used: '{chosen}' {(taken ? "is already taken" : "is not a 31 id")}; choose another.";
        Assert.Equal([refusal], result.Refusals);
        V31Contract.AssertSentence(new SourceRefusal("A10-069", refusal, ""));
        Assert.Equal(json.ReplaceLineEndings("\n"), result.ManifestJson.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void AUnionInsideAFormat_IsKeptAsWritten_AndIsOneHandEdit()
    {
        var (json, files) = ByteEquivalenceFixtures.ReadPackage("example-nested-note");
        files = new Dictionary<string, string>(files, StringComparer.Ordinal)
        {
            ["formats/verdict.json"] = files["formats/verdict.json"].Replace(
                "\"feedback\": {\n        \"type\": \"string\"\n      }",
                "\"feedback\": {\n        \"type\": [\"string\", \"number\", \"null\"]\n      }"),
        };
        Assert.Contains("\"number\"", files["formats/verdict.json"]);

        var result = WorkflowPackageMigration30To31.Migrate(json, files, MigrationFixture.Options("example-nested-note"));

        Assert.True(result.Succeeded, string.Join("\n", result.Refusals));
        var edit = Assert.Single(result.HandEdits);
        Assert.Equal("Hand edit needed in formats/verdict.json, /properties/feedback/type: the field takes one of several types, which 31 writes as an anyOf; choose one.", edit);
        V31Contract.AssertSentence(new SourceRefusal("S10-002", edit, "formats/verdict.json"));
        Assert.Equal("""{"type":["string","number","null"]}""", JsonOf(result, "formats/verdict.json")["properties"]!["feedback"]!.ToJsonString());

        // The form written is one the 31 checker refuses, so the hand edit is never silent.
        var refusals = FormatDialect.Check(JsonOf(result, "formats/verdict.json"), SchemaSite.PackageFormat, "Format 'verdict'", "verdict", "formats/verdict.json");
        Assert.NotEmpty(refusals);
    }

    [Fact]
    public void AUnionAtDepth_IsOneHandEditEach_NamedByItsPointerInTheWrittenFile()
    {
        // An optional array (wrapped as anyOf with null) of a union, and a union in a required field: two pointers into
        // the schema as 31 writes it, not as 30 wrote it.
        var schema = JsonNode.Parse("""
            { "type": "object", "additionalProperties": false, "required": ["b"],
              "properties": {
                "a": { "type": "array", "items": { "type": ["string", "integer"] } },
                "b": { "type": ["string", "boolean"] } } }
            """)!.AsObject();
        var pointers = new List<string>();

        PackageFormat.V31.Migration.Migrate.Strict(schema, (pointer, _) => pointers.Add(pointer));

        Assert.Equal(["/properties/a/anyOf/0/items/type", "/properties/b/type"], pointers);
        Assert.Equal("""{"type":["string","integer"]}""", schema["properties"]!["a"]!["anyOf"]![0]!["items"]!.ToJsonString());
    }

    [Fact]
    public void AFormatNullableAtItsRoot_IsWrittenAs31Writes_AndIsOneHandEdit()
    {
        var (json, files) = ByteEquivalenceFixtures.ReadPackage("example-nested-note");
        var wrapper = JsonNode.Parse(files["formats/verdict.json"])!.AsObject();
        wrapper["schema"]!["type"] = new JsonArray("object", "null");
        files = new Dictionary<string, string>(files, StringComparer.Ordinal) { ["formats/verdict.json"] = wrapper.ToJsonString() };

        var result = WorkflowPackageMigration30To31.Migrate(json, files, MigrationFixture.Options("example-nested-note"));

        Assert.True(result.Succeeded, string.Join("\n", result.Refusals));
        var edit = Assert.Single(result.HandEdits);
        Assert.Equal("Hand edit needed in formats/verdict.json, type: the format is nullable at its root, which a format a model writes cannot be at 31; make the root an object.", edit);
        V31Contract.AssertSentence(new SourceRefusal("S10-002", edit, "formats/verdict.json"));
        Assert.Equal(Compact("""
            { "$schema": "urn:consultologist:spec:31:dialect",
              "description": "A judgement of a draft summary: good, or revise with what to change.",
              "anyOf": [
                { "type": "object", "additionalProperties": false, "required": ["verdict", "feedback"],
                  "properties": { "verdict": { "type": "string", "enum": ["good", "revise"] }, "feedback": { "type": "string" } } },
                { "type": "null" } ] }
            """), JsonOf(result, "formats/verdict.json").ToJsonString());
    }

    [Theory]
    [InlineData("[\"null\", \"null\"]")]
    [InlineData("\"null\"")]
    public void AFormatWhoseRootIsNullAlone_IsOneHandEdit_NeverAThrow(string type)
    {
        // A 31 package format is an object or a list (part 02 § 3.3); a root of null alone (["null", "null"] too) is
        // written as 30 had it, which the dialect refuses, with one hand edit.
        var result = WithVerdictType(type);

        Assert.True(result.Succeeded, string.Join("\n", result.Refusals));
        var edit = Assert.Single(result.HandEdits, e => e.Contains("formats/verdict.json", StringComparison.Ordinal));
        Assert.Equal("Hand edit needed in formats/verdict.json, type: a package format's root is an object or a list at 31; make it one, or use a registry format.", edit);
        V31Contract.AssertSentence(new SourceRefusal("S10-002", edit, "formats/verdict.json"));
        V31Contract.AssertSentence(new SourceRefusal("A10-031", "a package format's root is an object or a list at 31; make it one, or use a registry format.", ""));
        Assert.NotEmpty(FormatDialect.Check(JsonOf(result, "formats/verdict.json"), SchemaSite.PackageFormat, "Format 'verdict'", "verdict", "formats/verdict.json"));
    }

    [Theory]
    [InlineData("\"string\"")]
    [InlineData("[\"string\"]")]
    public void AFormatWhoseRootIsAScalar_Is30sToRefuse(string type)
    {
        // 30 refuses a custom format whose root is a scalar, so the migration writes nothing (and never throws).
        var result = WithVerdictType(type);

        Assert.False(result.Succeeded);
        Assert.All(result.Refusals, r => Assert.StartsWith("At specVersion 30: ", r, StringComparison.Ordinal));
        Assert.Empty(result.HandEdits);
    }

    /// <summary>example-nested-note with its verdict format's schema type set to <paramref name="type"/>, migrated.</summary>
    private static Migration31Result WithVerdictType(string type)
    {
        var (json, files) = ByteEquivalenceFixtures.ReadPackage("example-nested-note");
        var wrapper = JsonNode.Parse(files["formats/verdict.json"])!.AsObject();
        wrapper["schema"]!["type"] = JsonNode.Parse(type);
        files = new Dictionary<string, string>(files, StringComparer.Ordinal) { ["formats/verdict.json"] = wrapper.ToJsonString() };
        return WorkflowPackageMigration30To31.Migrate(json, files, MigrationFixture.Options("example-nested-note"));
    }

    [Fact]
    public void APromptThatOpensLikeAHeader_IsNotHeldToStrictJson_But30RefusesIt()
    {
        // A prompt has no header at 30, so its first line is never read as one here (no A10-019); 30's own rule refuses a
        // prompt that opens with a header line.
        var (json, files) = ByteEquivalenceFixtures.ReadPackage("example-nested-note");
        files = new Dictionary<string, string>(files, StringComparer.Ordinal)
        {
            ["prompts/plan.md"] = "{\n  \"a\": 1,\n  \"a\": 2\n}\n" + files["prompts/plan.md"],
        };

        var result = WorkflowPackageMigration30To31.Migrate(json, files, MigrationFixture.Options("example-nested-note"));

        Assert.NotEmpty(result.Refusals);
        Assert.All(result.Refusals, r => Assert.StartsWith("At specVersion 30: ", r, StringComparison.Ordinal));
    }

    [Fact]
    public void ARootReadme_IsCopiedAsItWas()
    {
        var (json, files) = ByteEquivalenceFixtures.ReadPackage("example-nested-note");
        files = new Dictionary<string, string>(files, StringComparer.Ordinal)
        {
            ["README.md"] = "# example-nested-note\r\n\r\nA fictional demo for Dr A. Sample.\r\n",
        };

        var result = WorkflowPackageMigration30To31.Migrate(json, files, MigrationFixture.Options("example-nested-note"));

        Assert.True(result.Succeeded, string.Join("\n", result.Refusals));
        Assert.Equal("# example-nested-note\n\nA fictional demo for Dr A. Sample.\n", result.Files["README.md"]);
    }

    /// <summary>Review Focus 3: a manifest a hand broke, each a refusal (nothing written) or a migrated package.</summary>
    public static TheoryData<string> BrokenPackages() => new(
        "inputs-an-object", "for-and-while", "ret-naming-no-node", "prompt-missing", "format-not-json",
        "format-duplicate-member", "format-nested-duplicate", "snippet-header-duplicate-member", "macro-header-no-close",
        "macro-header-duplicate-member", "folder-index-not-json", "folder-index-duplicate-member", "crlf-everywhere",
        "bom", "two-mebibyte-prompt", "format-deeply-nested", "manifest-unknown-member", "snippet-file-missing");

    [Theory]
    [MemberData(nameof(BrokenPackages))]
    public void AnyBrokenPackage_IsRefusedOrMigrated_NeverThrows(string breakage)
    {
        var (json, files, package) = Broken(breakage);

        var result = WorkflowPackageMigration30To31.Migrate(json, files, MigrationFixture.Options(package));

        if (result.Succeeded)
        {
            Assert.All(result.Files.Values.Append(result.ManifestJson), text => Assert.DoesNotContain('\r', text));
            foreach (var folder in Folders)
                Assert.True(result.Files.ContainsKey($"{folder}/index.json"));
        }
        else
        {
            Assert.Equal(json, result.ManifestJson);
            Assert.Equal(files.OrderBy(f => f.Key, StringComparer.Ordinal), result.Files.OrderBy(f => f.Key, StringComparer.Ordinal));
            Assert.Empty(result.Notices);
            Assert.Empty(result.HandEdits);
        }

        var expectRefused = breakage is not ("crlf-everywhere" or "bom" or "two-mebibyte-prompt");
        Assert.Equal(expectRefused, !result.Succeeded);
    }

    [Theory]
    [InlineData("format-not-json", "The file 'formats/verdict.json' does not read: it is not JSON; fix it, then migrate.")]
    [InlineData("format-duplicate-member", "The file 'formats/verdict.json' does not read: it names the member 'type' twice; fix it, then migrate.")]
    [InlineData("format-nested-duplicate", "The file 'formats/verdict.json' does not read: it names the member 'feedback' twice; fix it, then migrate.")]
    [InlineData("format-deeply-nested", "The file 'formats/verdict.json' does not read: it nests deeper than 32 levels; fix it, then migrate.")]
    [InlineData("snippet-header-duplicate-member", "The file 'snippets/style.md' does not read: its header names the member 'label' twice; fix it, then migrate.")]
    [InlineData("macro-header-duplicate-member", "The file 'macros/closing.md' does not read: its header names the member 'label' twice; fix it, then migrate.")]
    [InlineData("folder-index-not-json", "The file 'snippets/sections/index.json' does not read: it is not JSON; fix it, then migrate.")]
    [InlineData("folder-index-duplicate-member", "The file 'snippets/sections/index.json' does not read: it names the member 'items' twice; fix it, then migrate.")]
    [InlineData("macro-header-no-close", "The file 'macros/closing.md' does not read: its header is not JSON; fix it, then migrate.")]
    public void AFileThatIsNotStrictJson_IsRefused_NamingIt(string breakage, string sentence)
    {
        var (json, files, package) = Broken(breakage);

        var result = WorkflowPackageMigration30To31.Migrate(json, files, MigrationFixture.Options(package));

        Assert.Equal([sentence], result.Refusals);
        V31Contract.AssertSentence(new SourceRefusal("A10-019", sentence, ""));
    }

    [Theory]
    [InlineData("inputs-an-object")]
    [InlineData("for-and-while")]
    [InlineData("ret-naming-no-node")]
    [InlineData("prompt-missing")]
    [InlineData("manifest-unknown-member")]
    [InlineData("snippet-file-missing")]
    public void APackageThat30Refuses_IsRefused_AtSpecVersion30(string breakage)
    {
        var (json, files, package) = Broken(breakage);

        var result = WorkflowPackageMigration30To31.Migrate(json, files, MigrationFixture.Options(package));

        Assert.NotEmpty(result.Refusals);
        Assert.All(result.Refusals, r =>
        {
            Assert.StartsWith("At specVersion 30: ", r, StringComparison.Ordinal);
            V31Contract.AssertSentence(new SourceRefusal("A10-017", r, ""));
        });
    }

    /// <summary>A package with one <paramref name="breakage"/>: its manifest, files and the fixture it is built from.</summary>
    private static (string Json, Dictionary<string, string> Files, string Package) Broken(string breakage)
    {
        var package = breakage switch
        {
            "snippet-header-duplicate-member" or "macro-header-no-close" or "macro-header-duplicate-member"
                or "folder-index-not-json" or "folder-index-duplicate-member" or "snippet-file-missing" => "example-macros",
            _ => "example-nested-note",
        };
        var (json, read) = ByteEquivalenceFixtures.ReadPackage(package);
        var files = new Dictionary<string, string>(read, StringComparer.Ordinal);
        var root = JsonNode.Parse(json)!.AsObject();

        switch (breakage)
        {
            case "inputs-an-object":
                root["inputs"] = new JsonObject { ["consult_draft"] = new JsonObject { ["label"] = "Consult draft" } };
                break;
            case "for-and-while":
                root["nodes"]!.AsArray().Single(n => (string?)n!["id"] == "parts")!["while"] = "true";
                break;
            case "ret-naming-no-node":
                root["nodes"]!.AsArray().Single(n => (string?)n!["id"] == "parts")!["ret"] = "node.nowhere";
                break;
            case "prompt-missing":
                files.Remove("prompts/plan.md");
                break;
            case "format-not-json":
                files["formats/verdict.json"] = "{ \"type\": \"object\", ";
                break;
            case "format-duplicate-member":
                files["formats/verdict.json"] = "{ \"type\": \"object\", \"type\": \"object\" }";
                break;
            case "format-nested-duplicate":
                files["formats/verdict.json"] = files["formats/verdict.json"].Replace(
                    "\"feedback\": {\n        \"type\": \"string\"\n      }",
                    "\"feedback\": {\n        \"type\": \"string\"\n      },\n      \"feedback\": {\n        \"type\": \"string\"\n      }");
                Assert.NotEqual(read["formats/verdict.json"], files["formats/verdict.json"]);
                break;
            case "format-deeply-nested":
                files["formats/verdict.json"] = new string('[', 40) + new string(']', 40);
                break;
            case "snippet-header-duplicate-member":
                files["snippets/style.md"] = "{\n  \"label\": \"Style\",\n  \"label\": \"Style again\"\n}\nWrite plainly.\n";
                break;
            case "macro-header-no-close":
                files["macros/closing.md"] = "{\n  \"label\": \"Closing\",\n  \"instruction\": \"Close.\"\nThe closing text.\n";
                break;
            case "macro-header-duplicate-member":
                files["macros/closing.md"] = "{\n  \"label\": \"Closing\",\n  \"label\": \"Closing\"\n}\nThe closing text.\n";
                break;
            case "folder-index-not-json":
                files["snippets/sections/index.json"] = "{ items: [hpi] }";
                break;
            case "folder-index-duplicate-member":
                files["snippets/sections/index.json"] = "{ \"items\": [\"hpi\"], \"items\": [\"hpi\"] }";
                break;
            case "crlf-everywhere":
                json = json.Replace("\n", "\r\n");
                files = files.ToDictionary(f => f.Key, f => f.Value.Replace("\n", "\r\n"), StringComparer.Ordinal);
                return (json, files, package);
            case "bom":
                files = files.ToDictionary(f => f.Key, f => "﻿" + f.Value, StringComparer.Ordinal);
                return ("﻿" + json, files, package);
            case "two-mebibyte-prompt":
                files["prompts/plan.md"] += new string('x', 2 * 1024 * 1024);
                break;
            case "manifest-unknown-member":
                root["nodez"] = new JsonArray();
                break;
            case "snippet-file-missing":
                files.Remove("snippets/style.md");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(breakage), breakage, null);
        }

        return (root.ToJsonString(), files, package);
    }
}
