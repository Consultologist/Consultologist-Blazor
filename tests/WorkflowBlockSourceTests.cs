using Consultologist.Api.Models;
using Consultologist.Api.Workflow;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Tests;

public class WorkflowPackageBlocksTests
{
    [Fact]
    public void Resolve_V5Package_ReadsTheResultNodesCollection()
    {
        var manifest = V5Fixtures.Manifest();
        var files = V5Fixtures.Files(manifest);
        var errors = new List<string>();
        var data = WorkflowDataResolver.Resolve(manifest, files, errors);
        Assert.Empty(errors);

        var package = new WorkflowPackage(
            manifest,
            Nodes: manifest.Nodes,
            Data: data,
            ResultNodeId: "section-instructions");

        var sections = WorkflowPackageBlocks.Resolve(package);

        Assert.Equal(2, sections.Count);
        Assert.Equal(("hpi", "History of Present Illness", "Document the presenting illness."), (sections[0].Id, sections[0].Name, sections[0].Content));
    }

    // #426 (v9): a fan over a caller-supplied array expands to one block per
    // element, ids the engine minted, beside the data fan's blocks.

    private static WorkflowPackage FannedPackage()
    {
        var manifest = V9Fixtures.Fanned();
        var files = V6Fixtures.Files(manifest);
        var errors = new List<string>();
        var data = WorkflowDataResolver.Resolve(manifest, files, errors);
        Assert.Empty(errors);

        return new WorkflowPackage(
            manifest,
            Nodes: manifest.Nodes,
            Data: data,
            Results: new List<WorkflowResolvedResult> { new("consult", "assemble-note", "Consultation note") });
    }

    [Fact]
    public void Resolve_AnInputFan_ExpandsOneBlockPerElement()
    {
        var notes = manifestInput(FannedPackage(), "prior_notes");
        var inputFans = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>
        {
            ["input:prior_notes"] = WorkflowInputFans.Items(notes, ConsultInputValue.OfArray(new[]
            {
                ConsultInputValue.OfText("First."), ConsultInputValue.OfText("Second.")
            }))
        };

        var blocks = WorkflowPackageBlocks.Resolve(FannedPackage(), inputFans);

        Assert.Equal(
            new[] { "consult:section-instructions:hpi", "consult:section-instructions:pmh", "consult:summarise-note:0", "consult:summarise-note:1" },
            blocks.Select(block => block.Id).ToArray());
        Assert.Equal("Prior notes 2", blocks[3].Name);
        Assert.Equal(string.Empty, blocks[3].Content);
    }

    [Fact]
    public void Resolve_AnInputFan_ContributesNoBlocks_BeforeAJobExists()
    {
        // The WorkflowPackages/Current endpoint has no request: the rail
        // fills from the job's roster once one starts.
        var blocks = WorkflowPackageBlocks.Resolve(FannedPackage());

        Assert.Equal(
            new[] { "consult:section-instructions:hpi", "consult:section-instructions:pmh" },
            blocks.Select(block => block.Id).ToArray());
    }

    private static WorkflowInputSpec manifestInput(WorkflowPackage package, string id) =>
        package.Manifest.Inputs!.Single(input => input.Id == id);
}
