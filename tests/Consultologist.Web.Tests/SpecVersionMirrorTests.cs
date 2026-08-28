using Consultologist.Api.Workflow;
using Consultologist.Web.Pages;
using Consultologist.Web.Shared.WorkflowEditor;

using Consultologist.PackageFormat;
namespace Consultologist.Web.Tests;

/// <summary>
/// #376: the client mirrors the engine's spec-version set by hand, in two
/// places — the picker's floor and the editor's ceiling. It has to:
/// Consultologist.Web carries no ProjectReference at all, so the client
/// assembly genuinely cannot see WorkflowPackageStore.SupportedSpecVersions.
///
/// This test project references BOTH, which is what makes the mirror provable
/// without the client fetching anything at runtime. A drift here means a
/// picker that greys out a version the engine would happily run, or an Upgrade
/// button offering a format the engine refuses.
/// </summary>
public class SpecVersionMirrorTests
{
    [Fact]
    public void ThePickersFloor_IsTheOldestFormatTheEngineRuns()
    {
        Assert.Equal(
            WorkflowPackageStore.SupportedSpecVersions.Min(),
            WorkflowPackagePicker.MinSupportedSpecVersion);
    }

    [Fact]
    public void TheClientsAccountPrefix_IsTheOneTheServerStamps()
    {
        // #411 added a third hand-mirrored fact to the client: the prefix that
        // says a package belongs to an account. If the server ever renames it,
        // the editor would quietly stop recognising forks — and the notice that
        // exists to prevent an accidental publish would stop appearing on the
        // packages that need it.
        Assert.Equal(
            WorkflowPackageNaming.AccountPrefix,
            Consultologist.Web.Services.Workflow.WorkflowPackageNames.AccountPrefix);
    }

    [Fact]
    public void TheClientsInputTypes_AreTheServers()
    {
        // #424 added three names to the vocabulary. The client mirrors the
        // list by hand for the same reason as the others; a name the server
        // knows and the client does not is a control the form cannot draw.
        Assert.Equal(
            WorkflowInputTypes.All,
            Consultologist.Web.Services.Workflow.WorkflowInputTypes.All);
    }

    [Fact]
    public void TheEditorsCeiling_IsTheNewestFormatTheRegistryAccepts()
    {
        // What the newest "Upgrade to specVersion N" stamps (#429). Ahead of
        // the registry, publish refuses the package the author just built;
        // behind it, the newest format is unreachable from the editor — which
        // was #347's defect.
        Assert.Equal(
            WorkflowPackageValidator.AcceptedSpecVersions.Max(),
            Templates.NewestSpecVersion);
    }

    [Fact]
    public void TheEditorsRunnableCeiling_IsTheNewestFormatTheEngineRuns()
    {
        // The other button, and the line the not-yet-runnable notice is drawn
        // at. The two ceilings meet whenever the engine runs the newest format
        // the registry accepts — as they do at ten (#500).
        Assert.Equal(
            WorkflowPackageStore.SupportedSpecVersions.Max(),
            Templates.RunnableSpecVersion);
    }

    [Fact]
    public void TheClientsElementTypes_AreTheServers()
    {
        Assert.Equal(
            WorkflowInputTypes.ElementTypes,
            Consultologist.Web.Services.Workflow.WorkflowInputTypes.ElementTypes);
    }

    [Fact]
    public void TheClientsVersionKeyedTypeLists_AreTheServers()
    {
        // v10 (#498): the fields editor offers what the version admits.
        foreach (var version in new[] { 9, 10 })
        {
            Assert.Equal(WorkflowInputTypes.ScalarsFor(version), Consultologist.Web.Services.Workflow.WorkflowInputTypes.ScalarsFor(version));
            Assert.Equal(WorkflowInputTypes.ElementTypesFor(version), Consultologist.Web.Services.Workflow.WorkflowInputTypes.ElementTypesFor(version));
        }
    }

    [Fact]
    public void TheClientsScalars_AreTheServers()
    {
        Assert.Equal(
            WorkflowInputTypes.Scalars,
            Consultologist.Web.Services.Workflow.WorkflowInputTypes.Scalars);
    }

    [Fact]
    public void TheClientsTitleAndDescriptionLimits_AreTheServers()
    {
        // #432: the Package pane's maxlength and counters, and the desk's
        // sentences, all read these.
        Assert.Equal(
            WorkflowPackageMetadata.MaxTitleLength,
            Consultologist.Web.Services.Workflow.WorkflowPackageMetadata.MaxTitleLength);
        Assert.Equal(
            WorkflowPackageMetadata.MaxDescriptionLength,
            Consultologist.Web.Services.Workflow.WorkflowPackageMetadata.MaxDescriptionLength);
    }

    [Fact]
    public void TheClientsSlugRule_IsTheServers()
    {
        // #447: the desk refuses what the publish would, in the same terms.
        Assert.Equal(WorkflowPackageNaming.MaxSlugLength, Consultologist.Web.Services.Workflow.WorkflowPackageNames.MaxSlugLength);
        foreach (var slug in new[] { "breast-oncology", "a", "Breast", "breast-", "-b", "", "b c", new string('a', 40), new string('a', 41) })
        {
            Assert.Equal(WorkflowPackageNaming.IsValidSlug(slug), Consultologist.Web.Services.Workflow.WorkflowPackageNames.IsValidSlug(slug));
        }
    }

    [Fact]
    public void TheClientsPathRule_IsTheServers()
    {
        // #448: a folder path of slugs, at most three under the account root.
        Assert.Equal(WorkflowPackageNaming.MaxPathSegments, Consultologist.Web.Services.Workflow.WorkflowPackageNames.MaxPathSegments);
        foreach (var path in new[] { "breast", "oncology/breast", "a/b/c", "a/b/c/d", "oncology//breast", "/breast", "breast/", "Oncology/breast", "" })
        {
            Assert.Equal(WorkflowPackageNaming.IsValidPath(path), Consultologist.Web.Services.Workflow.WorkflowPackageNames.IsValidPath(path));
        }
    }

    [Fact]
    public void TheClientsTagLimits_AreTheServers()
    {
        // #453: the pane's per-tag maxlength and its count ceiling.
        Assert.Equal(
            WorkflowPackageMetadata.MaxTagLength,
            Consultologist.Web.Services.Workflow.WorkflowPackageMetadata.MaxTagLength);
        Assert.Equal(
            WorkflowPackageMetadata.MaxTags,
            Consultologist.Web.Services.Workflow.WorkflowPackageMetadata.MaxTags);
    }

    [Fact]
    public void TheClientsItemFields_AreTheServers()
    {
        // What a binding may read from an input fan's item (v9 § 5).
        Assert.Equal(
            WorkflowInputFans.ItemFields,
            Consultologist.Web.Services.Workflow.WorkflowInputFans.ItemFields);
    }
}
