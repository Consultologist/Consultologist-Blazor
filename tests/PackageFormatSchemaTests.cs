using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Consultologist.Api.Workflow;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Tests;

/// <summary>
/// #407: the published schemas are generated from WorkflowPackageManifest, so
/// this asserts the artifact in the registry is still what this model produces.
///
/// Byte identity, and a string comparison rather than a schema one: .NET has no
/// JSON Schema validator in the box and this needs none. Whether the schemas
/// agree with the conformance suite is the other direction, and the format
/// repo's CI owns it — that half genuinely needs a validator, which Python has.
///
/// Read off the submodule for the reason SpecVersionSetTests gives: a network
/// call in the suite that gates every merge fails when the network does.
/// </summary>
public class PackageFormatSchemaTests
{
    private static string PublishedSchema(int specVersion)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
        {
            dir = dir.Parent;
        }

        var path = Path.Combine(
            dir!.FullName, "external", "consultologist-package-format",
            "schemas", $"package-format-v{specVersion}.schema.json");

        Assert.True(File.Exists(path), $"{path} is missing — the submodule is not checked out.");
        return File.ReadAllText(path);
    }

    public static TheoryData<int> SupportedVersions()
    {
        var data = new TheoryData<int>();

        foreach (var specVersion in WorkflowPackageStore.SupportedSpecVersions)
        {
            data.Add(specVersion);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SupportedVersions))]
    public void ThePublishedSchema_IsWhatThisModelProduces(int specVersion)
    {
        // If this fails, either the manifest changed and the schemas were not
        // regenerated, or the submodule pin moved to schemas this engine does
        // not agree with. Regenerate with SCHEMA_EXPORT=<path> on
        // PackageFormatSchemaExport.
        Assert.Equal(PublishedSchema(specVersion), PackageFormatSchema.Render(specVersion));
    }

    [Fact]
    public void TheV11Schema_CarriesTheDeclarationRules_AheadOfItsPublication()
    {
        // #563: specVersion 11 is accepted before it is run, so no published
        // schema exists yet; the shape is pinned here as v10's was, and the
        // v10 render is seen to be untouched by the members v11 added.
        var schema = PackageFormatSchema.Build(11);
        var properties = schema["properties"]!.AsObject();

        Assert.True(properties.ContainsKey("macros"));
        var macroProperties = properties["macros"]!["items"]!["properties"]!.AsObject();
        Assert.True(macroProperties.ContainsKey("id"));
        Assert.True(macroProperties.ContainsKey("label"));
        Assert.True(macroProperties.ContainsKey("file"));
        Assert.True(properties["results"]!["items"]!["properties"]!.AsObject().ContainsKey("macros"));
        Assert.True(properties["results"]!["items"]!["properties"]!.AsObject().ContainsKey("signature"));
        Assert.True(properties["nodes"]!["items"]!["properties"]!.AsObject().ContainsKey("reproducible"));

        // And none of it below 11.
        var v10 = PackageFormatSchema.Build(10)["properties"]!.AsObject();
        Assert.False(v10.ContainsKey("macros"));
        Assert.False(v10["results"]!["items"]!["properties"]!.AsObject().ContainsKey("macros"));
        Assert.False(v10["results"]!["items"]!["properties"]!.AsObject().ContainsKey("signature"));
        Assert.False(v10["nodes"]!["items"]!["properties"]!.AsObject().ContainsKey("reproducible"));
        Assert.Equal("Consultologist workflow package manifest, specVersion 11", schema["title"]!.GetValue<string>());
    }

    [Fact]
    public void TheV12Schema_CarriesTheDeclarationRules()
    {
        // #617 pinned this ahead of publication; #623 published it, and the
        // byte-identity theory now covers 12 too — these content pins stay
        // as the readable statement of what the v12 render declares, and the
        // v11 render is seen to be untouched by the members v12 added.
        var schema = PackageFormatSchema.Build(12);
        var properties = schema["properties"]!.AsObject();

        var macroProperties = properties["macros"]!["items"]!["properties"]!.AsObject();
        Assert.True(macroProperties.ContainsKey("optional"));
        Assert.True(macroProperties.ContainsKey("default"));

        // The macro entry is the hand-written oneOf — the exporter cannot see
        // through the converter, so this shape is authored, and pinned.
        var resultProperties = properties["results"]!["items"]!["properties"]!.AsObject();
        var entry = resultProperties["macros"]!["items"]!.AsObject();
        Assert.True(entry.ContainsKey("oneOf"));
        Assert.True(resultProperties.ContainsKey("check"));

        var nodeProperties = properties["nodes"]!["items"]!["properties"]!.AsObject();
        Assert.True(nodeProperties.ContainsKey("op"));
        Assert.True(nodeProperties.ContainsKey("of"));
        Assert.True(nodeProperties.ContainsKey("in"));
        Assert.True(nodeProperties.ContainsKey("failWith"));
        Assert.Contains("check", nodeProperties["kind"]!["enum"]!.AsArray().Select(k => k!.GetValue<string>()));

        // And none of it below 12: the v11 macro entry is still the bare
        // string, and the check family absent.
        var v11 = PackageFormatSchema.Build(11)["properties"]!.AsObject();
        Assert.False(v11["macros"]!["items"]!["properties"]!.AsObject().ContainsKey("optional"));
        var v11Entry = v11["results"]!["items"]!["properties"]!.AsObject()["macros"]!["items"]!.AsObject();
        Assert.False(v11Entry.ContainsKey("oneOf"));
        Assert.Equal("string", v11Entry["type"]!.GetValue<string>());
        Assert.False(v11["results"]!["items"]!["properties"]!.AsObject().ContainsKey("check"));
        var v11Nodes = v11["nodes"]!["items"]!["properties"]!.AsObject();
        Assert.False(v11Nodes.ContainsKey("op"));
        Assert.DoesNotContain("check", v11Nodes["kind"]!["enum"]!.AsArray().Select(k => k!.GetValue<string>()));
        Assert.Equal("Consultologist workflow package manifest, specVersion 12", schema["title"]!.GetValue<string>());
    }

    [Fact]
    public void TheV10Schema_CarriesTheDeclarationRules_AheadOfItsPublication()
    {
        // #492: specVersion 10 is accepted before it is run, so no published
        // schema exists yet; the shape is pinned here as v9's was, and the v9
        // render is seen to be untouched by the members v10 added.
        var schema = PackageFormatSchema.Build(10);
        var input = schema["properties"]!["inputs"]!["items"]!;
        var field = input["properties"]!["fields"]!["items"]!;
        var node = schema["properties"]!["nodes"]!["items"]!["properties"]!;

        // items: a type name, or an element spec — recursively.
        var items = input["properties"]!["items"]!["oneOf"]!.AsArray();
        Assert.Equal(2, items.Count);
        Assert.Equal(WorkflowInputTypes.All, items[0]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal("#/properties/inputs/items/properties/items", items[1]!["properties"]!["items"]!["$ref"]!.GetValue<string>());
        Assert.Equal("#/properties/inputs/items/properties/fields/items", items[1]!["properties"]!["fields"]!["items"]!["$ref"]!.GetValue<string>());

        // A field may be anything and carries items and fields of its own.
        Assert.Equal(WorkflowInputTypes.All, field["properties"]!["type"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.True(field["properties"]!.AsObject().ContainsKey("items"));
        Assert.True(field["properties"]!.AsObject().ContainsKey("fields"));

        // A node's kind and values.
        Assert.Equal(new[] { "prompt", "classifier" }, node["kind"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal(2, node["values"]!["minItems"]!.GetValue<int>());

        // And none of it below 10.
        var v9Input = PackageFormatSchema.Build(9)["properties"]!["inputs"]!["items"]!;
        Assert.Equal("string", v9Input["properties"]!["items"]!["type"]!.GetValue<string>());
        Assert.False(v9Input["properties"]!["fields"]!["items"]!["properties"]!.AsObject().ContainsKey("items"));
        Assert.False(PackageFormatSchema.Build(9)["properties"]!["nodes"]!["items"]!["properties"]!.AsObject().ContainsKey("kind"));
        Assert.Equal("Consultologist workflow package manifest, specVersion 10", schema["title"]!.GetValue<string>());
    }

    [Fact]
    public void TheV9Schema_CarriesTheDeclarationRules_AheadOfItsPublication()
    {
        // #424: specVersion 9 is accepted before it is run, so there is no
        // published schema to compare against yet — that file ships with the
        // registry release (#430). The rules are pinned here so the generator
        // is ready when it does, and so the v8 schema's four-name enum is seen
        // to be keyed by version rather than by the vocabulary's growth.
        var schema = PackageFormatSchema.Build(9);
        var input = schema["properties"]!["inputs"]!["items"]!;
        var names = input["properties"]!["type"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>());

        Assert.Equal(new[] { "text", "date", "enum", "boolean", "number", "object", "array" }, names);
        Assert.Equal(
            new[] { "text", "date", "enum", "boolean", "number", "object" },
            input["properties"]!["items"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal(
            new[] { "text", "date", "enum", "boolean", "number" },
            input["properties"]!["fields"]!["items"]!["properties"]!["type"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal(3, input["allOf"]!.AsArray().Count);

        var v8Names = PackageFormatSchema.Build(8)["properties"]!["inputs"]!["items"]!["properties"]!["type"]!["enum"]!
            .AsArray().Select(n => n!.GetValue<string>());
        Assert.Equal(new[] { "text", "date", "enum", "boolean" }, v8Names);

        // #432: the title and the description, at 9 and not before.
        var title = schema["properties"]!["title"]!;
        Assert.Equal(1, title["minLength"]!.GetValue<int>());
        Assert.Equal(WorkflowPackageMetadata.MaxTitleLength, title["maxLength"]!.GetValue<int>());
        Assert.Equal(@"^[^\r\n]*$", title["pattern"]!.GetValue<string>());
        var description = schema["properties"]!["description"]!;
        Assert.Equal(1, description["minLength"]!.GetValue<int>());
        Assert.Equal(WorkflowPackageMetadata.MaxDescriptionLength, description["maxLength"]!.GetValue<int>());
        Assert.False(PackageFormatSchema.Build(8)["properties"]!.AsObject().ContainsKey("title"));
        Assert.False(PackageFormatSchema.Build(8)["properties"]!.AsObject().ContainsKey("description"));
        // The schema's own title is a different field that shares the name.
        Assert.Equal("Consultologist workflow package manifest, specVersion 9", schema["title"]!.GetValue<string>());

        // #426: the fan pattern widens at 9 to admit input: fans; v8's stays data:-only.
        Assert.Equal("^(data|input):.+$", schema["properties"]!["nodes"]!["items"]!["properties"]!["forEach"]!["pattern"]!.GetValue<string>());
        Assert.Equal("^data:.+$", PackageFormatSchema.Build(8)["properties"]!["nodes"]!["items"]!["properties"]!["forEach"]!["pattern"]!.GetValue<string>());

        // #448: derivedFrom admits a nested name of up to four segments, on every version.
        var derivedFrom = new System.Text.RegularExpressions.Regex(schema["properties"]!["derivedFrom"]!["pattern"]!.GetValue<string>());
        Assert.Matches(derivedFrom, "oncology/breast@v2026.08.1");
        Assert.Matches(derivedFrom, "acct-0123456789ab/oncology/breast@v2026.08.1");
        Assert.DoesNotMatch(derivedFrom, "a/b/c/d/e@v2026.08.1");
        Assert.DoesNotMatch(derivedFrom, "oncology//breast@v2026.08.1");
        Assert.Equal(derivedFrom.ToString(), PackageFormatSchema.Build(5)["properties"]!["derivedFrom"]!["pattern"]!.GetValue<string>());

        // #453: tags — required at 9, an array of single-line labels, absent before.
        var tags = schema["properties"]!["tags"]!;
        Assert.Equal("array", tags["type"]!.GetValue<string>());
        Assert.Equal(WorkflowPackageMetadata.MaxTags, tags["maxItems"]!.GetValue<int>());
        Assert.Equal("string", tags["items"]!["type"]!.GetValue<string>());
        Assert.Equal(1, tags["items"]!["minLength"]!.GetValue<int>());
        Assert.Equal(WorkflowPackageMetadata.MaxTagLength, tags["items"]!["maxLength"]!.GetValue<int>());
        Assert.Equal(@"^[^\r\n]*$", tags["items"]!["pattern"]!.GetValue<string>());
        Assert.Contains("tags", schema["required"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.DoesNotContain("tags", PackageFormatSchema.Build(8)["required"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.False(PackageFormatSchema.Build(8)["properties"]!.AsObject().ContainsKey("tags"));
    }

    [Fact]
    public void EveryVersionTheEngineRuns_HasAPublishedSchema()
    {
        // A format an author is told to conform to, with nothing to conform
        // against, is the gap #407 was filed about.
        foreach (var specVersion in WorkflowPackageStore.SupportedSpecVersions)
        {
            PublishedSchema(specVersion);
        }
    }

    [Fact]
    public void TheSchemaAndTheEngine_RefuseTheSameUnknownFields()
    {
        // This asserted a divergence until #416: the schema said
        // additionalProperties: false because package-format-v8.md says "a
        // section the version does not have is never a silently ignored field",
        // and the engine accepted them anyway. Both sides now hold, and this
        // fails if either drifts back.
        var schema = JsonDocument.Parse(PublishedSchema(8)).RootElement;

        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            JsonUnmappedMemberHandling.Disallow,
            WorkflowPackageManifestJson.ReadOptions.UnmappedMemberHandling);
    }

    /// <summary>
    /// A4 (#999) generated the v25 schema; A5 (#1018) published it. This pins
    /// its shape — every retired key gone, the replacements present and
    /// required, every reference in the object spelling (#1037), every id
    /// snake_case.
    /// </summary>
    [Fact]
    public void TheV25Schema_Renders_WithoutTheRetiredKeys()
    {
        var schema = PackageFormatSchema.Build(25);
        var properties = (JsonObject)schema["properties"]!;

        foreach (var retired in new[] { "preludes", "data", "schemas", "schemaRefs", "customSchemas", "result", "results" })
        {
            Assert.False(properties.ContainsKey(retired), retired);
        }

        Assert.True(properties.ContainsKey("deliverables"));
        Assert.True(properties.ContainsKey("snippets"));
        Assert.True(properties.ContainsKey("formats"));
        Assert.Contains("deliverables", schema["required"]!.AsArray().Select(r => r!.GetValue<string>()));
        Assert.Null(schema["oneOf"]);

        var node = (JsonObject)properties["nodes"]!["items"]!["properties"]!;
        foreach (var retired in new[] { "bindings", "output", "kind", "values", "op", "of", "in", "failWith" })
        {
            Assert.False(node.ContainsKey(retired), retired);
        }

        Assert.Equal("^(snippet|input)\\.[a-z][a-z0-9_]*$", node["forEach"]!["pattern"]!.GetValue<string>());
        Assert.Equal("^[a-z][a-z0-9_]*$", node["format"]!["pattern"]!.GetValue<string>());
        Assert.Equal("^node\\.[a-z][a-z0-9_]*$", node["aggregate"]!["items"]!["pattern"]!.GetValue<string>());
        Assert.Equal(1, node["failIfEmpty"]!["minLength"]!.GetValue<int>());
        Assert.NotNull(node["with"]);
        Assert.Equal(3, node["onInvalid"]!["enum"]!.AsArray().Count);

        var prompt = (JsonObject)properties["prompts"]!["items"]!["properties"]!;
        foreach (var retired in new[] { "variables", "prelude", "raw" })
        {
            Assert.False(prompt.ContainsKey(retired), retired);
        }
        Assert.Equal("^[a-z][a-z0-9_]*$", prompt["id"]!["pattern"]!.GetValue<string>());
        Assert.Equal(new[] { "id", "file" }, properties["prompts"]!["items"]!["required"]!.AsArray().Select(r => r!.GetValue<string>()));
        Assert.Equal("^[a-z][a-z0-9_]*$", properties["snippets"]!["propertyNames"]!["pattern"]!.GetValue<string>());
        Assert.Equal("^node\\.[a-z][a-z0-9_]*$", properties["deliverables"]!["items"]!["properties"]!["node"]!["pattern"]!.GetValue<string>());

        var macro = (JsonObject)properties["macros"]!["items"]!;
        Assert.Contains("instruction", macro["required"]!.AsArray().Select(r => r!.GetValue<string>()));

        var placement = (JsonObject)properties["deliverables"]!["items"]!["properties"]!["macros"]!["items"]!;
        Assert.Equal(new[] { "id", "at", "when" }, ((JsonObject)placement["properties"]!).Select(p => p.Key));
        Assert.Equal("^node\\.[a-z][a-z0-9_]*$", properties["deliverables"]!["items"]!["properties"]!["check"]!["pattern"]!.GetValue<string>());

        Assert.NotEmpty(PackageFormatSchema.Render(25));
    }

    [Fact]
    public void TheV26Schema_Renders_WithTheMembers()
    {
        // #1165: the model carries the 26 members ahead of the bump; Build(26)
        // renders them (the file itself is exported at #1149), and every
        // published 5–25 schema is unchanged — ThePublishedSchema_IsWhatThisModelProduces.
        var schema = PackageFormatSchema.Build(26);
        var properties = (JsonObject)schema["properties"]!;

        var node = (JsonObject)properties["nodes"]!["items"]!["properties"]!;
        foreach (var flow in new[] { "for", "if", "case", "then", "else", "branches", "nodes", "ret" })
        {
            Assert.True(node.ContainsKey(flow), flow);
        }

        Assert.Equal(1, node["ret"]!["minLength"]!.GetValue<int>());
        Assert.Equal("^[a-z][a-z0-9_]*$", node["nodes"]!["items"]!["pattern"]!.GetValue<string>());
        foreach (var body in new[] { "then", "else" })
        {
            var shape = (JsonObject)node[body]!;
            Assert.Null(shape["$ref"]);
            Assert.False(shape["additionalProperties"]!.GetValue<bool>());
            Assert.Equal(new[] { "nodes", "ret" }, ((JsonObject)shape["properties"]!).Select(p => p.Key));
        }

        var branch = (JsonObject)node["branches"]!["items"]!;
        Assert.Equal(new[] { "when", "nodes", "ret" }, ((JsonObject)branch["properties"]!).Select(p => p.Key));
        Assert.False(branch["additionalProperties"]!.GetValue<bool>());

        var placed = (JsonObject)node["macros"]!["items"]!["oneOf"]![1]!["properties"]!;
        Assert.True(placed.ContainsKey("if"));

        var deliverable = (JsonObject)properties["deliverables"]!["items"]!["properties"]!;
        Assert.Equal("^node\\.[a-z][a-z0-9_]*$", deliverable["sources"]!["items"]!["pattern"]!.GetValue<string>());
        Assert.Equal(1, deliverable["if"]!["minLength"]!.GetValue<int>());
        Assert.Equal(new[] { "id", "at", "when", "if" }, ((JsonObject)deliverable["macros"]!["items"]!["properties"]!).Select(p => p.Key));

        // #1105/#1099 (§ 5, § 5.3): macros is a map id → file at 26, as snippets is; the header carries the rest.
        var macros = (JsonObject)properties["macros"]!;
        Assert.Equal("object", macros["type"]!.GetValue<string>());
        Assert.Equal("^[a-z][a-z0-9_]*$", macros["propertyNames"]!["pattern"]!.GetValue<string>());
        Assert.Equal("string", macros["additionalProperties"]!["type"]!.GetValue<string>());
        Assert.Null(macros["items"]);

        var v25 = (JsonObject)PackageFormatSchema.Build(25)["properties"]!["nodes"]!["items"]!["properties"]!;
        Assert.False(v25.ContainsKey("for"));
        Assert.False(v25.ContainsKey("ret"));
    }

    [Fact]
    public void TheV24Schema_KeepsTheColonSpellings_TheBytesMustNotMove()
    {
        // A5 (#1018): every 25 pattern fix is gated ≥25; the published v≤24
        // schemas are byte-identical (the theory above proves the bytes; this
        // names the spellings so a regression reads as one).
        var schema = PackageFormatSchema.Build(24);
        var properties = (JsonObject)schema["properties"]!;
        var node = (JsonObject)properties["nodes"]!["items"]!["properties"]!;

        Assert.Equal("^(data|input):.+$", node["forEach"]!["pattern"]!.GetValue<string>());
        Assert.Equal("^node:.+$", node["aggregate"]!["items"]!["pattern"]!.GetValue<string>());
        Assert.Equal("^node:.+$", properties["results"]!["items"]!["properties"]!["node"]!["pattern"]!.GetValue<string>());
        Assert.Null(((JsonObject)properties["prompts"]!["items"]!["properties"]!)["id"]!["pattern"]);
        Assert.False(node.ContainsKey("failIfEmpty"));
    }
}
