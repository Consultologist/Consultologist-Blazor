using Consultologist.Api.Workflow;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Tests;

/// <summary>
/// #407: writes the per-specVersion schemas that consultologist-package-format
/// publishes. Same shape as ConformanceFixtureExport — a generator, gated on an
/// environment variable, because regenerating on every CI run would be a slow
/// way to write files nobody reads.
///
/// PackageFormatSchemaTests is the half that runs always: it asserts the
/// published schemas are byte-identical to what this produces now, so the model
/// and the artifact cannot drift apart unnoticed.
/// </summary>
public class PackageFormatSchemaExport
{
    [Fact]
    public void Export()
    {
        var outDir = Environment.GetEnvironmentVariable("SCHEMA_EXPORT");

        if (string.IsNullOrWhiteSpace(outDir))
        {
            return;
        }

        Directory.CreateDirectory(outDir!);

        foreach (var specVersion in WorkflowPackageStore.SupportedSpecVersions)
        {
            File.WriteAllText(
                Path.Combine(outDir!, $"package-format-v{specVersion}.schema.json"),
                PackageFormatSchema.Render(specVersion));
        }
    }
}
