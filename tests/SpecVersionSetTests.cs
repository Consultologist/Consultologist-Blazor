using System.Text.Json.Nodes;
using Consultologist.Api.Workflow;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Tests;

/// <summary>
/// #376: the set of specVersions this engine deals in was written out four
/// times — an array in the store, a pattern match plus an English sentence in
/// the validator, a floor in WorkflowPackagePicker and a ceiling in
/// Templates — with no compiler anywhere that would notice them drifting.
///
/// It is now also a published artifact: consultologist-package-format's
/// spec-versions.json, vendored here as a submodule. These hold the two
/// in-engine copies against each other and against that document.
///
/// Read off the submodule rather than fetched from the registry on purpose: a
/// network call here would make the suite that gates every merge fail when the
/// network does, to learn a fact that is checked into this very tree.
/// </summary>
public class SpecVersionSetTests
{
    private static JsonNode PublishedSpecVersions()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
        {
            dir = dir.Parent;
        }

        var path = Path.Combine(
            dir!.FullName, "external", "consultologist-package-format", "spec-versions.json");

        Assert.True(
            File.Exists(path),
            $"{path} is missing — the submodule is not checked out (git submodule update --init).");

        return JsonNode.Parse(File.ReadAllText(path))!;
    }

    private static int[] PublishedSupported() =>
        PublishedSpecVersions()["supported"]!.AsArray().Select(v => (int)v!).ToArray();

    [Fact]
    public void WhatTheEngineRuns_IsWhatTheRegistryPublishes()
    {
        // The whole point of publishing the set. If this fails, either the
        // engine moved and the document has not been republished, or the
        // submodule pin was bumped to a document this engine cannot honour.
        Assert.Equal(PublishedSupported(), WorkflowPackageStore.SupportedSpecVersions);
    }

    [Fact]
    public void EveryPublishedVersion_HasADocumentThatDefinesIt()
    {
        // A number in `supported` with no document is a version an author is
        // told to conform to with nothing to conform to.
        var documents = PublishedSpecVersions()["documents"]!.AsObject();

        foreach (var version in WorkflowPackageStore.SupportedSpecVersions)
        {
            Assert.True(
                documents.ContainsKey(version.ToString()),
                $"spec-versions.json names no document for specVersion {version}.");
        }
    }

    [Fact]
    public void WhatRuns_IsAlwaysASubsetOfWhatPublishes()
    {
        // Not equality. The validator's gate moves FIRST on purpose, so a
        // format can be published and validated against while running it still
        // refuses with SpecVersionNotYetExecutable — that staging is how v8
        // shipped. Accepted may lead; Supported may never.
        Assert.Empty(
            WorkflowPackageStore.SupportedSpecVersions
                .Except(WorkflowPackageValidator.AcceptedSpecVersions));
    }

    [Fact]
    public void TheRefusalSentence_StillReadsAsASentence()
    {
        // The set became a constant and the prose became generated; this pins
        // the wording so that refactor cannot quietly reword a message an
        // author reads. Note it is NOT the store's phrasing — see below.
        Assert.Equal("5, 6, 7, 8, 9, 10 or 11", WorkflowPackageValidator.DescribeAcceptedSpecVersions());
    }

    [Fact]
    public void TheTwoRefusals_AreAllowedToWordItDifferently()
    {
        // The store builds its own sentence with string.Join(" or "), giving
        // "5 or 6 or 7 or 8". Deliberately not unified: they are asserted
        // verbatim in different tests, and one generator producing both would
        // silently change whichever it did not match.
        var store = new WorkflowPackageSpecVersionException(
            "general@v2026.08.1", 3, WorkflowPackageStore.SupportedSpecVersions);

        Assert.Contains("5 or 6 or 7 or 8 or 9 or 10", store.Message);
        Assert.DoesNotContain(WorkflowPackageValidator.DescribeAcceptedSpecVersions(), store.Message);
    }
}
