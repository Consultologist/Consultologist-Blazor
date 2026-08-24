using System.Text.Json;
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
}
