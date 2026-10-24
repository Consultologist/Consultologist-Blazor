using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// Verifies the two concept rendering formats normative in package-format-v2.md,
/// byte-for-byte — these strings feed the model, so any harness must reproduce them.
/// #1138: the renderers live in the library (ConceptRenderers — Bullets was
/// ConsultGenerationConceptFormatter.Format, Context was AgentSectionGenerator.FormatConcepts),
/// where the editor renders the same sample the pane shows.
/// </summary>
public class ConceptFormatContractTests
{
    private static readonly IReadOnlyList<ClinicalConcept> Sample = new[]
    {
        new ClinicalConcept("Malignant neoplasm of breast", "disorder", "254837009", true, true, "draft"),
        new ClinicalConcept("Diabetes mellitus", "disorder", "73211009", true, false, "draft"),
        new ClinicalConcept("Family support strong", "", "", false, false, "draft"),
        new ClinicalConcept("Tamoxifen therapy", "procedure", "75367002", true, true, "typical", "adjuvant endocrine therapy")
    };

    [Fact]
    public void AnalysisFormat_MatchesSpec()
    {
        var expected =
            "- Malignant neoplasm of breast (disorder) - 254837009" + Environment.NewLine +
            "- Diabetes mellitus (disorder) - 73211009 [not active SNOMED concept]" + Environment.NewLine +
            "- Family support strong [not SNOMED concept]" + Environment.NewLine +
            "- Tamoxifen therapy (procedure) - 75367002";

        Assert.Equal(expected, ConceptRenderers.Bullets(Sample));
    }

    [Fact]
    public void AnalysisFormat_EmptyListRendersNone()
    {
        Assert.Equal("(none)", ConceptRenderers.Bullets(Array.Empty<ClinicalConcept>()));
    }

    [Fact]
    public void TrajectoryContextFormat_MatchesSpec()
    {
        var expected =
            "- Malignant neoplasm of breast (disorder) - 254837009; active: True; source: draft" + Environment.NewLine +
            "- Diabetes mellitus (disorder) - 73211009; active: False; source: draft" + Environment.NewLine +
            "- Family support strong () - ; active: False; source: draft" + Environment.NewLine +
            "- Tamoxifen therapy (procedure) - 75367002; active: True; source: typical support: adjuvant endocrine therapy";

        Assert.Equal(expected, ConceptRenderers.Context(Sample));
    }

    [Fact]
    public void TrajectoryContextFormat_EmptyListRendersNone()
    {
        Assert.Equal("(none)", ConceptRenderers.Context(Array.Empty<ClinicalConcept>()));
    }

    [Fact]
    public void TheSample_RendersEveryMarkOnce_ThroughBothRenderers()
    {
        // #1138: the library's fixture — what the editor's renderer pane shows — through the real code.
        var concepts = ConceptOutputContract.Deserialize(ConceptSamples.ConceptListJson, ConceptSamples.Source);
        Assert.Equal(ConceptSamples.Count, concepts.Count);

        Assert.Equal(
            "- Diabetes mellitus (disorder) - 73211009 [not active SNOMED concept]" + Environment.NewLine +
            "- Family support strong [not SNOMED concept]",
            ConceptRenderers.Bullets(concepts));

        Assert.Equal(
            "- Diabetes mellitus (disorder) - 73211009; active: False; source: extract support: long-standing type 2 diabetes" + Environment.NewLine +
            "- Family support strong (finding) - ; active: False; source: extract",
            ConceptRenderers.Context(concepts));
    }
}
