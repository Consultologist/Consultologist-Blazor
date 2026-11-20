using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Types;

/// <summary>
/// #1500 (specVersion 31, area 03): <see cref="FormatDialect"/>'s rules for a format's root and
/// its annotations, by <see cref="SchemaSite"/> (part 02 § 3.1, § 4, § 5.1, § 6.2), the whole
/// checker against the published formats and the dialect's own examples, and garbage in.
/// </summary>
public class FormatDialectRootTests
{
    private const string Verdict = "Format 'verdict'";
    private const string Dialect = "\"$schema\": \"urn:consultologist:spec:31:dialect\"";
    private const string Object = "\"type\": \"object\", \"required\": [], \"additionalProperties\": false, \"properties\": {}";

    private static IReadOnlyList<SourceRefusal> Check(string json, SchemaSite site) => site == SchemaSite.Inline
        ? FormatDialect.Check(JsonNode.Parse(json), site, "Input 'notes'", "notes", "manifest.json")
        : FormatDialect.Check(JsonNode.Parse(json), site, Verdict, "verdict", "formats/verdict.json");

    /// <summary>Each root breaks one rule once: the root, the site, the id and the sentence it gets.</summary>
    public static TheoryData<string, SchemaSite, string, string> OneRuleBroken => new()
    {
        // No $schema, or another dialect's.
        { $$"""{ {{Object}} }""", SchemaSite.PackageFormat, "S02-022", "Format 'verdict' declares no $schema; every format file names the dialect (urn:consultologist:spec:31:dialect)." },
        { """{ "type": "string" }""", SchemaSite.RegistryFormat, "S02-022", "Format 'verdict' declares no $schema; every format file names the dialect (urn:consultologist:spec:31:dialect)." },
        {
            $$"""{ "$schema": "urn:consultologist:spec:30:dialect", {{Object}} }""", SchemaSite.PackageFormat,
            "S02-023", "Format 'verdict' declares $schema 'urn:consultologist:spec:30:dialect', but the package is specVersion 31, whose dialect is urn:consultologist:spec:31:dialect."
        },
        {
            """{ "$schema": "https://json-schema.org/draft/2020-12/schema", "type": "string" }""", SchemaSite.RegistryFormat,
            "S02-023", "Format 'verdict' declares $schema 'https://json-schema.org/draft/2020-12/schema', but the package is specVersion 31, whose dialect is urn:consultologist:spec:31:dialect."
        },
        { $$"""{ "$schema": 31, {{Object}} }""", SchemaSite.PackageFormat, "A03-003", "Format 'verdict' at the root writes '$schema' as a number; it is text." },

        // An inline schema has no root keywords.
        { $$"""{ {{Dialect}}, "type": "string" }""", SchemaSite.Inline, "S02-021", "Input 'notes' uses keyword '$schema' at the root, which is outside the formats dialect." },
        { """{ "$id": "urn:x", "type": "string" }""", SchemaSite.Inline, "S02-021", "Input 'notes' uses keyword '$id' at the root, which is outside the formats dialect." },
        { """{ "type": "string", "$defs": { "row": { "type": "string" } } }""", SchemaSite.Inline, "S02-021", "Input 'notes' uses keyword '$defs' at the root, which is outside the formats dialect." },
        { """{ "type": "string", "$defs": 5 }""", SchemaSite.Inline, "S02-021", "Input 'notes' uses keyword '$defs' at the root, which is outside the formats dialect." },

        // A package format's $id is written at publish.
        { $$"""{ {{Dialect}}, "$id": "urn:consultologist:package:x:format:verdict", {{Object}} }""", SchemaSite.PackageFormat, "S02-033", "Package format 'verdict' declares $id; a package format's $id is written at publish." },

        // A package format's root is an object or a list.
        { $$"""{ {{Dialect}}, "type": "string" }""", SchemaSite.PackageFormat, "S02-034", "Package format 'verdict' is a string; a package defines object and array formats, and refers to built-in and catalogue formats by name." },
        { $$"""{ {{Dialect}}, "type": ["null", "string"] }""", SchemaSite.PackageFormat, "S02-034", "Package format 'verdict' is a string; a package defines object and array formats, and refers to built-in and catalogue formats by name." },
        { $$"""{ {{Dialect}}, "type": "integer" }""", SchemaSite.PackageFormat, "S02-034", "Package format 'verdict' is a number; a package defines object and array formats, and refers to built-in and catalogue formats by name." },
        { $$"""{ {{Dialect}}, "type": "number" }""", SchemaSite.PackageFormat, "S02-034", "Package format 'verdict' is a number; a package defines object and array formats, and refers to built-in and catalogue formats by name." },
        { $$"""{ {{Dialect}}, "type": "boolean" }""", SchemaSite.PackageFormat, "S02-034", "Package format 'verdict' is a boolean; a package defines object and array formats, and refers to built-in and catalogue formats by name." },
        { $$"""{ {{Dialect}}, "$ref": "plaintext" }""", SchemaSite.PackageFormat, "A03-001", "Package format 'verdict' is neither an object nor a list; a package defines object and array formats, and refers to built-in and catalogue formats by name." },
        {
            $$"""{ {{Dialect}}, "anyOf": [{ "$ref": "judgement" }, { "type": "null" }] }""", SchemaSite.PackageFormat,
            "A03-001", "Package format 'verdict' is neither an object nor a list; a package defines object and array formats, and refers to built-in and catalogue formats by name."
        },
        { $$"""{ {{Dialect}}, "type": "null" }""", SchemaSite.PackageFormat, "A03-001", "Package format 'verdict' is neither an object nor a list; a package defines object and array formats, and refers to built-in and catalogue formats by name." },

        // The text annotations: never in an inline schema.
        { """{ "type": "string", "contentMediaType": "text/plain" }""", SchemaSite.Inline, "S02-038", "Input 'notes''s inline format declares contentMediaType; text inside an inline format is a $ref to a text format." },
        {
            """{ "type": "array", "items": { "type": "string", "x-markdown": "block" } }""", SchemaSite.Inline,
            "S02-038", "Input 'notes''s inline format declares x-markdown; text inside an inline format is a $ref to a text format."
        },

        // Never in a package format; below the root of any format.
        { $$"""{ {{Dialect}}, {{Object}}, "contentMediaType": "text/plain" }""", SchemaSite.PackageFormat, "S02-037", "Format 'verdict' declares contentMediaType at the root, where it doesn't apply." },
        { $$"""{ {{Dialect}}, {{Object}}, "x-markdown": "inline" }""", SchemaSite.PackageFormat, "S02-037", "Format 'verdict' declares x-markdown at the root, where it doesn't apply." },
        {
            $$"""{ {{Dialect}}, "type": "array", "items": { "type": "string", "contentMediaType": "text/markdown" } }""", SchemaSite.PackageFormat,
            "S02-037", "Format 'verdict' declares contentMediaType at /items, where it doesn't apply."
        },
        {
            $$"""{ {{Dialect}}, "type": "object", "required": ["a"], "additionalProperties": false, "properties": { "a": { "type": "string", "x-markdown": "block" } } }""", SchemaSite.RegistryFormat,
            "S02-037", "Format 'verdict' declares x-markdown at /properties/a, where it doesn't apply."
        },

        // A registry text format: the annotations' values and their combinations.
        { $$"""{ {{Dialect}}, "type": "string", "contentMediaType": "text/html" }""", SchemaSite.RegistryFormat, "A03-009", "Format 'verdict' declares contentMediaType 'text/html' (accepted: text/plain, text/markdown)." },
        {
            $$"""{ {{Dialect}}, "type": "string", "contentMediaType": "text/markdown", "x-markdown": "line" }""", SchemaSite.RegistryFormat,
            "A03-009", "Format 'verdict' declares x-markdown 'line' (accepted: block, inline)."
        },
        { $$"""{ {{Dialect}}, "type": "string", "x-markdown": true, "contentMediaType": "text/markdown" }""", SchemaSite.RegistryFormat, "A03-003", "Format 'verdict' at the root writes 'x-markdown' as true or false; it is text." },
        { $$"""{ {{Dialect}}, "type": "string", "x-markdown": "block" }""", SchemaSite.RegistryFormat, "S02-035", "Format 'verdict' is a text format and declares no contentMediaType." },
        { $$"""{ {{Dialect}}, "type": "string", "contentMediaType": "text/markdown" }""", SchemaSite.RegistryFormat, "S02-036", "Format 'verdict' is Markdown and declares no x-markdown (block or inline)." },
        {
            $$"""{ {{Dialect}}, "type": "string", "contentMediaType": "text/plain", "x-markdown": "block" }""", SchemaSite.RegistryFormat,
            "S02-037", "Format 'verdict' declares x-markdown at the root, where it doesn't apply."
        },
        { $$"""{ {{Dialect}}, {{Object}}, "contentMediaType": "text/plain" }""", SchemaSite.RegistryFormat, "S02-037", "Format 'verdict' declares contentMediaType at the root, where it doesn't apply." },
        { $$"""{ {{Dialect}}, "type": "number", "x-markdown": "block" }""", SchemaSite.RegistryFormat, "S02-037", "Format 'verdict' declares x-markdown at the root, where it doesn't apply." },
    };

    [Theory]
    [MemberData(nameof(OneRuleBroken))]
    public void EachRule_IsRefused_WithItsSentence(string json, SchemaSite site, string id, string sentence)
    {
        var refusal = Assert.Single(Check(json, site));
        Assert.Equal(id, refusal.Id);
        Assert.Equal(sentence, refusal.Sentence);
        V31Contract.AssertSentence(refusal);
    }

    [Fact]
    public void EachRootKeyword_IsAcceptedWhereItBelongs()
    {
        Assert.Empty(Check($$"""{ {{Dialect}}, {{Object}}, "$defs": {} }""", SchemaSite.PackageFormat));
        Assert.Empty(Check($$"""{ {{Dialect}}, "type": "array", "items": { "$ref": "plaintext" } }""", SchemaSite.PackageFormat));
        Assert.Empty(Check($$"""{ {{Dialect}}, "$id": "urn:consultologist:spec:31:format:x", "type": "string", "contentMediaType": "text/plain" }""", SchemaSite.RegistryFormat));
        Assert.Empty(Check($$"""{ {{Dialect}}, "type": "string", "contentMediaType": "text/markdown", "x-markdown": "inline" }""", SchemaSite.RegistryFormat));
        Assert.Empty(Check($$"""{ {{Dialect}}, "$ref": "plaintext" }""", SchemaSite.RegistryFormat));
        Assert.Empty(Check("""{ "anyOf": [{ "$ref": "plaintext" }, { "type": "null" }] }""", SchemaSite.Inline));
    }

    [Fact]
    public void AnInlineAnnotation_IsRefusedOncePerKeyword()
    {
        var refusals = Check("""
            { "type": "object", "required": ["a", "b"], "additionalProperties": false, "contentMediaType": "text/plain",
              "properties": { "a": { "type": "string", "contentMediaType": "text/plain" }, "b": { "type": "string", "x-markdown": "block" } } }
            """, SchemaSite.Inline);

        Assert.Equal(["S02-038", "S02-038"], refusals.Select(r => r.Id));
        Assert.Contains("declares contentMediaType;", refusals[0].Sentence);
        Assert.Contains("declares x-markdown;", refusals[1].Sentence);
    }

    [Fact]
    public void AnInlineDefinition_IsRefused_AndItsReferenceIsMissing()
    {
        var refusals = Check("""{ "type": "array", "items": { "$ref": "#/$defs/row" }, "$defs": { "row": { "type": "string" } } }""", SchemaSite.Inline);

        Assert.Equal(
            [
                "Input 'notes' at /items refers to #/$defs/row, which the file doesn't define.",
                "Input 'notes' uses keyword '$defs' at the root, which is outside the formats dialect.",
            ],
            refusals.Select(r => r.Sentence));
    }

    [Fact]
    public void ARootDefsThatIsNotAnObject_IsRefused_AndEveryReferenceIsMissing()
    {
        var refusals = Check($$"""{ {{Dialect}}, "type": "array", "items": { "$ref": "#/$defs/row" }, "$defs": [] }""", SchemaSite.PackageFormat);

        Assert.Equal(
            [
                "Format 'verdict' at /items refers to #/$defs/row, which the file doesn't define.",
                "Format 'verdict' at the root writes '$defs' as a list; it is an object.",
            ],
            refusals.Select(r => r.Sentence));
    }

    [Fact]
    public void TheDepthGuard_HoldsInsideADefinition()
    {
        var row = new JsonObject { ["type"] = "string" };
        for (var i = 0; i < 40; i++)
            row = new JsonObject { ["type"] = "array", ["items"] = row };
        var schema = new JsonObject
        {
            ["$schema"] = "urn:consultologist:spec:31:dialect",
            ["type"] = "array",
            ["items"] = new JsonObject { ["$ref"] = "#/$defs/row" },
            ["$defs"] = new JsonObject { ["row"] = row },
        };

        var refusal = Assert.Single(FormatDialect.Check(schema, SchemaSite.PackageFormat, Verdict, "verdict", "formats/verdict.json"));
        Assert.Equal("S01-041", refusal.Id);
        Assert.Contains("/$defs/row/items/", refusal.Sentence);
    }

    [Fact]
    public void AValuePrintedInASentence_NeverThrows_HoweverDeep()
    {
        // additionalProperties is printed, not walked: a hand-built value deeper than any writer's default depth.
        JsonNode value = new JsonArray();
        for (var i = 0; i < 2_000; i++)
            value = new JsonObject { ["a"] = value };
        var schema = new JsonObject
        {
            ["$schema"] = "urn:consultologist:spec:31:dialect",
            ["type"] = "object",
            ["properties"] = new JsonObject(),
            ["required"] = new JsonArray(),
            ["additionalProperties"] = value,
        };

        var refusal = Assert.Single(FormatDialect.Check(schema, SchemaSite.PackageFormat, Verdict, "verdict", "formats/verdict.json"));
        Assert.Equal("S02-024", refusal.Id);
        V31Contract.AssertSentence(refusal);
    }

    /// <summary>The nine built-ins and the catalogue's formats, by owner and name.</summary>
    public static TheoryData<string, string> PublishedFormats()
    {
        var data = new TheoryData<string, string>();
        var registry = TypeFixture.Published();
        foreach (var (name, _) in registry.BuiltInFormats)
            data.Add("built-in", name);
        foreach (var (name, _) in registry.CatalogueFormats)
            data.Add("catalogue", name);
        return data;
    }

    [Theory]
    [MemberData(nameof(PublishedFormats))]
    public void EveryPublishedFormat_IsInTheDialect_Whole(string owner, string name)
    {
        var registry = TypeFixture.Published();
        var schema = owner == "built-in" ? registry.BuiltInFormats[name] : registry.CatalogueFormats[name];

        Assert.Empty(FormatDialect.Check(schema, SchemaSite.RegistryFormat, $"Format '{name}'", name, $"formats/{name}.json"));
    }

    /// <summary>Each file of <c>v31/conformance/schemas/dialect/</c>, by name.</summary>
    public static TheoryData<string> DialectExamples()
    {
        var directory = Path.Combine(
            Path.GetDirectoryName(TypeFixture.RepoFile("external", "consultologist-package-format", "v31", "bounds.json"))!,
            "conformance", "schemas", "dialect");
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(directory, "*.json").Order(StringComparer.Ordinal))
            data.Add(Path.GetFileName(file));
        return data;
    }

    [Theory]
    [MemberData(nameof(DialectExamples))]
    public void TheDialectsOwnExamples_AgreeWithTheChecker(string file)
    {
        var example = JsonNode.Parse(File.ReadAllText(TypeFixture.RepoFile(
            "external", "consultologist-package-format", "v31", "conformance", "schemas", "dialect", file)))!;
        // An example judged against the meta-schema's #/$defs/inline is an inline schema; any other is a format file.
        var site = (string?)example["against"] == "#/$defs/inline" ? SchemaSite.Inline : SchemaSite.RegistryFormat;

        var refusals = FormatDialect.Check(example["instance"], site, "Format 'example'", "example", file);

        if (file.StartsWith("valid-", StringComparison.Ordinal))
            Assert.Empty(refusals);
        else
            Assert.NotEmpty(refusals);
        Assert.All(refusals, V31Contract.AssertSentence);
    }

    [Fact]
    public void AnyMutationOfAValidFormat_NeverThrows()
    {
        // Review Focus 2: each member of each valid format in turn deleted, or replaced by garbage, at every site.
        var registry = TypeFixture.Published();
        var sources = registry.BuiltInFormats.Values.Concat(registry.CatalogueFormats.Values).Cast<JsonNode>().Append(SectionsInput()).ToList();
        var places = sources.SelectMany((source, s) => Members(source, "").Select(member => (Source: s, Member: member))).ToList();
        Assert.NotEmpty(places);

        var random = new Random(1500);
        for (var i = 0; i < 2_000; i++)
        {
            var (s, member) = places[i % places.Count];
            var mutated = sources[s].DeepClone();
            Mutate(mutated, member, random.Next(8));

            foreach (var site in (SchemaSite[])[SchemaSite.PackageFormat, SchemaSite.RegistryFormat, SchemaSite.Inline])
            {
                var refusals = FormatDialect.Check(mutated, site, Verdict, "verdict", "formats/verdict.json");
                Assert.NotNull(refusals);
            }
        }
    }

    /// <summary>Part 02 § 8.4's <c>sections</c> input format.</summary>
    private static JsonNode SectionsInput() => JsonNode.Parse("""
        {
          "type": "array",
          "items": {
            "type": "object",
            "additionalProperties": false,
            "required": ["title", "kind", "notes"],
            "properties": {
              "title": { "type": "string", "title": "Title" },
              "kind": {
                "type": "string",
                "title": "Kind",
                "oneOf": [
                  { "const": "narrative", "title": "Narrative" },
                  { "const": "table",     "title": "Table" },
                  { "const": "omit",      "title": "Leave out" }
                ]
              },
              "notes": {
                "anyOf": [ { "$ref": "markdown_block" }, { "type": "null" } ],
                "title": "Notes for this section"
              }
            }
          }
        }
        """)!;

    /// <summary>Every member's JSON pointer (object members and list entries), at every depth, in document order.</summary>
    private static IEnumerable<string> Members(JsonNode? node, string pointer)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var (key, child) in o)
                {
                    var at = $"{pointer}/{key.Replace("~", "~0").Replace("/", "~1")}";
                    yield return at;
                    foreach (var below in Members(child, at))
                        yield return below;
                }

                break;
            case JsonArray a:
                for (var i = 0; i < a.Count; i++)
                {
                    var at = $"{pointer}/{i}";
                    yield return at;
                    foreach (var below in Members(a[i], at))
                        yield return below;
                }

                break;
        }
    }

    /// <summary>The member at <paramref name="pointer"/> deleted (0), or set to null, 5, "x", [], {}, true, or wrapped 40 deep (1–7).</summary>
    private static void Mutate(JsonNode root, string pointer, int how)
    {
        var steps = pointer.Split('/')[1..].Select(p => p.Replace("~1", "/").Replace("~0", "~")).ToArray();
        var parent = root;
        foreach (var step in steps[..^1])
            parent = parent is JsonArray list ? list[int.Parse(step)]! : parent[step]!;
        var last = steps[^1];
        var current = parent is JsonArray at ? at[int.Parse(last)] : parent[last];

        JsonNode? replacement = how switch
        {
            1 => null,
            2 => 5,
            3 => "x",
            4 => new JsonArray(),
            5 => new JsonObject(),
            6 => true,
            _ => Wrap(current?.DeepClone()),
        };

        if (parent is JsonArray array)
        {
            var index = int.Parse(last);
            if (how == 0)
                array.RemoveAt(index);
            else
                array[index] = replacement;
        }
        else if (how == 0)
        {
            parent.AsObject().Remove(last);
        }
        else
        {
            parent[last] = replacement;
        }
    }

    private static JsonNode Wrap(JsonNode? value)
    {
        var wrapped = value;
        for (var i = 0; i < 40; i++)
            wrapped = new JsonObject { ["type"] = "array", ["items"] = wrapped };
        return wrapped!;
    }
}
