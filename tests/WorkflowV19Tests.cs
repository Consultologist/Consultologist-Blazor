using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// v19 (#845): a placed macro may carry `forItem` to anchor to ONE item of a
/// forEach data-fan the before/after section aggregates. Below 19 it is refused
/// by name; the anchor must fan a data: collection and the item must exist; the
/// composer interleaves the macro between the fan's blocks, and a placement with
/// no forItem stays byte-identical to before.
/// </summary>
public static class V19Fixtures
{
    // PlacedMacro fans data:standards (items hpi, pmh) via node:section-instructions,
    // and its deliverable aggregates that node — so it is a real data fan to anchor into.
    public static (WorkflowPackageManifest Manifest, IReadOnlyDictionary<string, string> Files) ForItem(
        string anchor = "node:section-instructions", string? forItem = "hpi", int specVersion = 19)
    {
        var (manifest, files) = V12ExportFixtures.PlacedMacro(anchor: anchor, forItem: forItem);
        return (manifest with { SpecVersion = specVersion }, files);
    }

    public static WorkflowPackageValidator.ValidationResult Validate(
        (WorkflowPackageManifest Manifest, IReadOnlyDictionary<string, string> Files) bundle) =>
        WorkflowPackageValidator.Validate(bundle.Manifest, bundle.Files, TestOutputContracts.CatalogSchemas);
}

public class WorkflowV19ForItemTests
{
    private static IReadOnlyList<string> Errors((WorkflowPackageManifest, IReadOnlyDictionary<string, string>) bundle) =>
        V19Fixtures.Validate(bundle).Errors;

    [Fact]
    public void AForItem_OnADataFan_IsValid()
    {
        var bundle = V19Fixtures.ForItem(forItem: "hpi");

        Assert.True(V19Fixtures.Validate(bundle).IsValid, string.Join(" | ", Errors(bundle)));
    }

    [Fact]
    public void AForItem_BelowNineteen_IsRefusedByName()
    {
        var bundle = V19Fixtures.ForItem(forItem: "hpi", specVersion: 18);

        Assert.Contains(
            Errors(bundle),
            e => e.Contains("anchors macro 'disclaimer' to a fan item, which requires specVersion 19"));
    }

    [Fact]
    public void AForItem_NamingAMissingItem_IsRefused()
    {
        var bundle = V19Fixtures.ForItem(forItem: "nonexistent");

        Assert.Contains(
            Errors(bundle),
            e => e.Contains("to item 'nonexistent'") && e.Contains("does not contain"));
    }

    [Fact]
    public void AForItem_OnASectionThatIsNotAFan_IsRefused()
    {
        // node:scope is a classifier in the fixture — a real node, not a forEach fan.
        var bundle = V19Fixtures.ForItem(anchor: "node:scope", forItem: "hpi");

        Assert.Contains(
            Errors(bundle),
            e => e.Contains("is not a forEach fan"));
    }
}

public class WorkflowV19PlacementRuntimeTests
{
    private static readonly Dictionary<string, string> NoValues = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, string> Texts = new(StringComparer.Ordinal)
    {
        ["disclaimer"] = "This disclaimer is fixed."
    };

    private static Consultologist.Api.Jobs.ConsultMacroExpander.RunFacts Facts() =>
        new(new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc), "0123456789abcdef", "general@v2026.09.1", "east.ca.api.consultologist.ai", "Taylor Reyes");

    private static readonly string[] SourceRefs = { "node:intro", "node:findings" };

    private static readonly IReadOnlyList<Consultologist.Api.Jobs.ConsultAggregateRenderer.Part> Parts = new Consultologist.Api.Jobs.ConsultAggregateRenderer.Part[]
    {
        new Consultologist.Api.Jobs.ConsultAggregateRenderer.ScalarPart("Intro."),
        new Consultologist.Api.Jobs.ConsultAggregateRenderer.ForEachPart(new[] { ("history", "History", "Unremarkable."), ("exam", "Exam", "Benign.") })
    };

    private static (string Text, IReadOnlyList<Consultologist.Api.Models.ConsultAppendedEntry>? Appended, bool TokenCarried) Compose(
        IReadOnlyList<string>? macroIds,
        IReadOnlyList<Consultologist.Api.Models.ConsultMacroPlacement>? placements) =>
        Consultologist.Api.Jobs.ConsultMacroExpander.Compose(
            SourceRefs, Parts, macroIds, placements, Texts, NoValues, null, NoValues, Facts());

    [Fact]
    public void AForItemMacro_SitsAfterThatItemsBlock_NotAfterTheWholeFan()
    {
        var (text, appended, _) = Compose(
            new[] { "disclaimer" },
            new[] { new Consultologist.Api.Models.ConsultMacroPlacement("disclaimer", After: "node:findings", ForItem: "history") });

        Assert.Equal(
            "Intro.\n\n## History\n\nUnremarkable.\n\nThis disclaimer is fixed.\n\n## Exam\n\nBenign.",
            text);
        // The appended-entry trail still names the macro (§ 6 unchanged).
        Assert.Equal(new[] { (Consultologist.Api.Models.ConsultAppendedKinds.Macro, "disclaimer") },
            appended!.Select(e => (e.Kind, e.Id)).ToArray());
    }

    [Fact]
    public void AForItemMacro_CanSitBeforeAnItemsBlock()
    {
        var (text, _, _) = Compose(
            new[] { "disclaimer" },
            new[] { new Consultologist.Api.Models.ConsultMacroPlacement("disclaimer", Before: "node:findings", ForItem: "exam") });

        Assert.Equal(
            "Intro.\n\n## History\n\nUnremarkable.\n\nThis disclaimer is fixed.\n\n## Exam\n\nBenign.",
            text);
    }

    [Fact]
    public void NoForItem_LeavesTheFanOneBlock_ByteIdentical()
    {
        // The parity guard: a whole-source placement (no forItem) still wraps the
        // entire fan, exactly as before v19.
        var (text, _, _) = Compose(
            new[] { "disclaimer" },
            new[] { new Consultologist.Api.Models.ConsultMacroPlacement("disclaimer", After: "node:findings") });

        Assert.Equal(
            "Intro.\n\n## History\n\nUnremarkable.\n\n## Exam\n\nBenign.\n\nThis disclaimer is fixed.",
            text);
    }
}
