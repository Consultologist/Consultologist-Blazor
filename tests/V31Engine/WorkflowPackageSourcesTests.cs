using Consultologist.Api.Tests.ByteEquivalence;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1507 (specVersion 31, area 10; R11, D22): what the weekly summary migrates an account's package from: the stored
/// <c>manifest.json</c> as its text, never the parsed model written again, and, below 31, the files a load gathers as
/// published (<see cref="WorkflowPackage.SourceFiles"/>). A <c>latest</c> ref is read at the version its pointer names; a
/// package at 31 or one whose version does not read is not loaded (the 5–30 load would refuse it).
/// </summary>
public class WorkflowPackageSourcesTests
{
    [Fact]
    public async Task APackageBelow31_IsItsStoredManifestText_AndItsPublishedFiles()
    {
        var package = ByteEquivalenceFixtures.Load("general");
        // the stored bytes, with the spacing and member order a parse and a write would not keep
        var stored = "{\n  \"specVersion\" :   30,\n  \"name\": \"general\"\n}\n";
        var downloads = new List<string>();
        var resolved = new List<WorkflowPackageRef>();

        var source = await WorkflowPackageStore.ReadSource(
            new WorkflowPackageRef("general", "latest"),
            (_, _) => Task.FromResult("v2026.11.3"),
            (name, path, _) =>
            {
                downloads.Add(path);
                return Task.FromResult(stored);
            },
            (reference, _) =>
            {
                resolved.Add(reference);
                return Task.FromResult(package);
            },
            CancellationToken.None);

        Assert.Equal("general@v2026.11.3", source.Ref);
        Assert.Equal(30, source.SpecVersion);
        Assert.Same(stored, source.ManifestJson);
        Assert.Equal(package.SourceFiles, source.Files);
        Assert.Same(package.Formats, source.Formats);
        Assert.Equal(["general/v2026.11.3/manifest.json"], downloads);
        Assert.Equal([new WorkflowPackageRef("general", "v2026.11.3")], resolved);
    }

    [Theory]
    [InlineData("{\"specVersion\": 31}", 31)]
    [InlineData("{\"specVersion\": 32}", 32)]
    [InlineData("{\"specVersion\": 4}", 4)]
    [InlineData("{ not json", null)]
    public async Task APackageTheLoadWouldRefuse_IsItsManifestTextAlone(string stored, int? version)
    {
        var source = await WorkflowPackageStore.ReadSource(
            new WorkflowPackageRef("acct-0123456789ab-letters", "v2026.11.2"),
            (reference, _) => Task.FromResult(reference.Version),
            (_, _, _) => Task.FromResult(stored),
            (_, _) => throw new InvalidOperationException("a package the 5–30 load refuses is not loaded"),
            CancellationToken.None);

        Assert.Equal("acct-0123456789ab-letters@v2026.11.2", source.Ref);
        Assert.Equal(version, source.SpecVersion);
        Assert.Same(stored, source.ManifestJson);
        Assert.Empty(source.Files);
        Assert.Null(source.Formats);
    }
}
