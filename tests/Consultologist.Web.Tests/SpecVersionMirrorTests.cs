using Consultologist.Api.Workflow;
using Consultologist.Web.Pages;
using Consultologist.Web.Shared.WorkflowEditor;

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
    public void TheEditorsCeiling_IsTheNewestFormatTheEngineRuns()
    {
        // What "Upgrade to specVersion N" stamps. Ahead of the engine, publish
        // refuses the package the author just built; behind it, the newest
        // format is unreachable from the editor — which was #347's defect.
        Assert.Equal(
            WorkflowPackageStore.SupportedSpecVersions.Max(),
            Templates.NewestSpecVersion);
    }
}
