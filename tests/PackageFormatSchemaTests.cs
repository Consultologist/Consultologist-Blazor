using System.Text.Json;
using Consultologist.Api.Workflow;

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
    public void TheSchemaRefusesWhatTheEngineQuietlyAccepts()
    {
        // The one place the schema is deliberately stricter than the engine.
        // package-format-v8.md: "a section the version does not have is never a
        // silently ignored field" — true here, not yet true in the store.
        var schema = JsonDocument.Parse(PublishedSchema(8)).RootElement;

        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
    }
}
