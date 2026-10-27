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

    // C2 (#1008): a 25 folder id is snake_case, so its directory is too —
    // data's directory segment keeps refusing the underscore it never took.
    [Fact]
    public void ASnippetFolderDirectory_TakesSnakeCase_ADataDirectoryStillDoesNot()
    {
        var errors = new List<string>();

        WorkflowPackageContentValidation.ValidateFilePaths(new Dictionary<string, string>
        {
            ["snippets/care_sections/index.json"] = "{}",
            ["snippets/care_sections/hpi.md"] = "x",
            ["snippets/care-sections/hpi.md"] = "x",
            ["data/care_sections/hpi.md"] = "x"
        }, errors);

        var error = Assert.Single(errors);
        Assert.StartsWith("File path 'data/care_sections/hpi.md' is not allowed", error);
    }

    [Fact]
    public void At26_ANodesPromptFile_IsReferenced_NotStray()
    {
        // #1102: a node names its prompt file; the closure references it as prompts[] did.
        var (manifest, files) = V26Package.Minimal();
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
        Assert.Equal("input.kind == \"new\"", content.Collections["sections"].Items[1].When);
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

    // #1182 (v26): a macro folder's files — macros/<folder>/<file>, the folder id
    // snake_case as a snippet folder's — are admitted at publish; the sentence names the shape.
    [Fact]
    public void AMacroFolderDirectory_TakesSnakeCase_AndIsAdmitted()
    {
        var errors = new List<string>();

        WorkflowPackageContentValidation.ValidateFilePaths(new Dictionary<string, string>
        {
            ["macros/section_notes/index.json"] = "[]",
            ["macros/section_notes/hpi.md"] = "x",
            ["macros/Section-Notes/hpi.md"] = "x"
        }, errors);

        var error = Assert.Single(errors);
        Assert.Equal("File path 'macros/Section-Notes/hpi.md' is not allowed: expected prompts/<file>, schemas/<file>, macros/<file>, macros/<folder>/<file>, formats/<file>, data/<collection>/<file>, data/<file>, snippets/<folder>/<file>, or snippets/<file>.", error);
    }
}
