using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>B2 (#1001): the Api's file closure and content resolution know the 25 members — snippets and package-local formats.</summary>
public class WorkflowPackageClosureV25Tests
{
    [Fact]
    public void SnippetFilesFoldersAndFormatFiles_AreReferenced_NotStray()
    {
        var (manifest, files) = V25Package.Minimal();
        manifest = manifest with { Formats = new List<WorkflowFormatReference> { new("meds", "formats/meds.json") } };
        files["formats/meds.json"] = FormatFixtures.LocalCustom;
        var errors = new List<string>();

        WorkflowPackageContentValidation.ValidateFileClosure(manifest, files, errors);

        Assert.Empty(errors);
    }

    [Fact]
    public void AStrayFile_IsStillNamed()
    {
        var (manifest, files) = V25Package.Minimal();
        files["snippets/orphan.md"] = "x";
        var errors = new List<string>();

        WorkflowPackageContentValidation.ValidateFileClosure(manifest, files, errors);

        Assert.Contains(errors, e => e.Contains("snippets/orphan.md"));
    }

    [Fact]
    public void ResolveContent_ReadsSnippets_WhenThereIsNoData()
    {
        var (manifest, files) = V25Package.Minimal();
        var errors = new List<string>();

        var content = WorkflowDataResolver.ResolveContent(manifest, files, errors);

        Assert.Empty(errors);
        Assert.Equal("Follow the local standards.", content.Scalars["standards"]);
        Assert.Equal(new[] { "hpi", "plan" }, content.Collections["sections"].Items.Select(i => i.Id));
        Assert.Equal("kind == new", content.Collections["sections"].Items[1].When);
    }

    [Fact]
    public void ResolveContent_ReadsData_Below25_AsBefore()
    {
        var (manifest, files) = V24Fixtures.Base();
        var errors = new List<string>();

        var content = WorkflowDataResolver.ResolveContent(manifest, files, errors);

        Assert.Empty(errors);
        Assert.Equal(WorkflowDataResolver.Resolve(manifest, files, new List<string>()).Collections.Count, content.Collections.Count);
    }
}
